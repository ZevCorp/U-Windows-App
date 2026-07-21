using System.Windows;
using System.Windows.Input;
using U.Graph;
using U.WindowsClient.Agent;
using U.WindowsClient.Backend;
using U.WindowsClient.Diagnostics;
using U.WindowsClient.Mcp;
using U.WindowsClient.SystemApi;
using U.WindowsClient.Teach;
using U.WindowsClient.Uia;
using U.WindowsClient.Update;
using U.WindowsClient.Voice;

namespace U.WindowsClient.Ui;

/// <summary>
/// La carita flotante: el frontend completo. Recoge lo que el usuario pide (texto o voz), lanza el
/// <see cref="AgentLoop"/> (que consulta al cerebro remoto) y muestra narración/estado. Implementa
/// <see cref="IVoice"/> y <see cref="IUserChannel"/> para que el bucle hable y pregunte por esta UI.
/// No contiene ninguna lógica de decisión.
/// </summary>
public partial class FaceWindow : Window, IVoice, IUserChannel
{
    private Config _config = Config.Load();
    private readonly UiaReader _uia = new();
    private readonly VoiceIO _voice = new();
    private readonly VideoLibrary _videoLibrary = new();
    private readonly GraphConfig _graphConfig = GraphConfig.Load();
    private Updater? _updater;
    private AgentLoop _loop = null!;
    private BackendClient? _backend;
    private CancellationTokenSource? _cts;
    private VideoLibraryWindow? _videoWindow;
    private LogWindow? _logWindow;
    private WorkflowLibraryWindow? _workflowWindow;
    private TeachSession? _teachSession;
    private bool _teaching;
    private UiInspector? _inspector;
    private SurfaceLocator? _locator;
    private LocatorBadge? _badge;
    private WorkflowMcpRunner? _workflowRunner;

    // Para resolver preguntas del asistente desde la caja de texto.
    private TaskCompletionSource<string>? _pendingAnswer;

    public FaceWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // Esquina inferior derecha por defecto. El panel crece hacia arriba (ver OnSizeChanged) cuando
        // se abre un Expander (p.ej. "Backend"): sin esto, ResizeMode="NoResize" con altura fija
        // recortaba el contenido expandido y quedaba invisible.
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - Width - 24;
        Top = wa.Bottom - ActualHeight - 24;
        SizeChanged += OnSizeChanged;

        BackendUrl.Text = _config.BackendUrl;
        SetMuted(_config.Muted); // si lo silenciaron en una sesión anterior, sigue mudo
        Closed += (_, __) =>
        {
            _inspector?.Dispose(); // suelta el hook global de mouse al cerrar
            _locator?.Dispose();
            _badge?.Close();
        };

        // El "location bar de Windows": arranca encendido mostrando el ID de superficie arriba a la
        // derecha. Es la base del scoping de workflows (mismo formato que source_url en Graph).
        _badge = new LocatorBadge();
        _badge.Show();
        _locator = new SurfaceLocator();
        _locator.Changed += loc => Dispatcher.Invoke(() => _badge?.SetText(loc.Id));
        _locator.Start();

        var mcp = new LocalMcp(_uia);
        // El backend es Graph: la credencial (X-API-Key) sale del MISMO GraphConfig que usa la
        // ventana de workflows — una sola fuente de key para toda la app.
        _backend = new BackendClient(_config, _graphConfig);
        // La superficie actual viaja en cada turno (scoping de workflows) y las llamadas
        // workflow_* del cerebro se ejecutan con el WorkflowPlayer (subconsciente).
        _workflowRunner = new WorkflowMcpRunner(_graphConfig, this);
        _loop = new AgentLoop(_backend, _uia, mcp, this, this, InstalledApps.List,
            () => _locator?.Current, _workflowRunner);

        Header.MouseLeftButtonDown += (_, ev) => { if (ev.ButtonState == MouseButtonState.Pressed) DragMove(); };

        StartUpdater();
    }

    // --- Auto-actualización ---

    /// <summary>
    /// Arranca el sondeo de versiones nuevas. El usuario no toca nada: si aparece una, se descarga en
    /// segundo plano y recién ahí asoma la pastilla. Ver <see cref="Updater"/>.
    /// </summary>
    private void StartUpdater()
    {
        _updater = new Updater(_config.UpdateFeedUrl);
        VersionText.Text = $"Versión {_updater.CurrentVersion}";
        // UpdateReady llega desde un hilo del pool, no del Dispatcher: tocar la UI directo reventaría.
        _updater.UpdateReady += version => Dispatcher.Invoke(() =>
        {
            UpdateBtn.Content = $"⬇ Versión {version} lista — reiniciar";
            UpdateBtn.Visibility = Visibility.Visible;
        });
        _updater.Start();
    }

    private void OnApplyUpdate(object sender, RoutedEventArgs e)
    {
        SetStatus("Actualizando Ü…");
        _updater?.ApplyAndRestart(); // no retorna: reinicia el proceso
    }

    /// <summary>
    /// Si el usuario nunca tocó la pastilla, la versión descargada se instala al cerrar: el próximo
    /// arranque ya es la nueva, sin que él haya hecho nada.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        _updater?.ApplyOnExit();
        base.OnClosed(e);
    }

    /// <summary>Mantiene fija la esquina inferior derecha: si la ventana crece (Expander abierto), crece hacia arriba.</summary>
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var wa = SystemParameters.WorkArea;
        Top = wa.Bottom - ActualHeight - 24;
    }

    // --- Entrada del usuario ---

    private void OnInputKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        string text = Input.Text.Trim();
        Input.Clear();
        if (text.Length == 0) return;

        // Si el asistente está esperando una respuesta a su pregunta, esto la resuelve.
        if (_pendingAnswer != null && !_pendingAnswer.Task.IsCompleted)
        {
            _pendingAnswer.TrySetResult(text);
            return;
        }
        _ = StartGoal(text);
    }

    private async void OnMic(object sender, RoutedEventArgs e)
    {
        SetStatus("Escuchando…");
        string heard = await _voice.ListenOnceAsync(CancellationToken.None);
        if (string.IsNullOrWhiteSpace(heard)) { SetStatus("No te escuché"); return; }
        if (_pendingAnswer != null && !_pendingAnswer.Task.IsCompleted) { _pendingAnswer.TrySetResult(heard); return; }
        _ = StartGoal(heard);
    }

    private void OnStop(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        _pendingAnswer?.TrySetResult("");
        // Cancelar el bucle no callaba lo ya encolado en el TTS: se pedía parar y Ü seguía hablando
        // hasta terminar la frase. Detener es detener, también la voz.
        _voice.Silence();
        SetStatus("Detenido");
        StopBtn.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Callar/dejar hablar a Ü. Un solo clic corta la frase en curso y lo deja mudo — antes, si arrancaba
    /// a hablar en mal momento, no había forma de pararlo salvo cerrar la aplicación.
    /// </summary>
    private void OnToggleMute(object sender, RoutedEventArgs e)
    {
        SetMuted(!_voice.Muted);
        _config.Save();
        SetStatus(_voice.Muted ? "Ü en silencio" : "Ü vuelve a hablar");
    }

    private void SetMuted(bool muted)
    {
        _voice.Muted = muted;   // el setter ya corta en seco lo que estuviera diciendo
        _config.Muted = muted;
        MuteBtn.Content = muted ? "🔇" : "🔊";
        MuteBtn.ToolTip = muted ? "Ü está en silencio — clic para que vuelva a hablar" : "Callar a Ü ahora mismo";
    }

    // --- Enseñanza activa (grabar pantalla+voz) ---

    private async void OnToggleTeach(object sender, RoutedEventArgs e)
    {
        if (_teaching) await StopTeachingAsync();
        else await StartTeachingAsync();
    }

    /// <summary>
    /// Se espera de verdad y se capturan los errores: si arrancar la grabación falla (p.ej. la
    /// librería nativa no carga), el botón vuelve a su estado normal y el error queda visible en el
    /// estado y en el LogBus. Cuando esto era `_ = StartAsync()` (fire-and-forget), la excepción se
    /// perdía en el aire y el usuario solo se enteraba minutos después, al detener, con un "no hay
    /// grabación para procesar" que no explicaba nada.
    /// </summary>
    private async Task StartTeachingAsync()
    {
        _teachSession = new TeachSession(_backend!, _videoLibrary, _config.UserId);
        _teachSession.StatusChanged += (_, msg) => SetStatus(msg);

        try
        {
            await _teachSession.StartAsync();
        }
        catch (Exception ex)
        {
            LogBus.Log("teach", $"no se pudo iniciar la enseñanza: {ex}");
            SetStatus($"No se pudo iniciar la grabación: {ex.Message}");
            await _teachSession.DisposeAsync();
            _teachSession = null;
            return;
        }

        SetTeachingUi(true);
    }

    /// <summary>Botón 🎓 en rojo y 🔄 a la vista mientras se graba; todo de vuelta a lo normal si no.</summary>
    private void SetTeachingUi(bool teaching)
    {
        _teaching = teaching;
        TeachBtn.Content = teaching ? "⏸ Enseñar" : "🎓 Enseñar";
        TeachBtn.Background = new System.Windows.Media.SolidColorBrush(teaching
            ? System.Windows.Media.Color.FromRgb(255, 59, 48)  // rojo, como StopBtn
            : System.Windows.Media.Color.FromRgb(34, 34, 34)); // gris normal
        RestartTeachBtn.Visibility = teaching ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// "Me equivoqué": tira lo grabado y vuelve a grabar desde cero. Sin esto, un error a mitad de la
    /// demostración solo se podía resolver deteniendo — y detener manda el video malo a Gemini y sus
    /// notas al cerebro, que es exactamente lo que no queremos que aprenda.
    /// </summary>
    private async void OnRestartTeach(object sender, RoutedEventArgs e)
    {
        if (!_teaching || _teachSession == null) return;

        // Descartar y rearrancar la grabación no es instantáneo (hay que esperar a que el mp4 se cierre
        // en disco). Se bloquean AMBOS botones mientras tanto: un segundo toque en 🔄 dejaría grabadores
        // huérfanos, y un ⏸ en medio intentaría detener una grabación que ya no existe.
        RestartTeachBtn.IsEnabled = false;
        TeachBtn.IsEnabled = false;
        SetStatus("Descartando lo grabado y empezando de nuevo…");
        try
        {
            await _teachSession.RestartAsync();
        }
        catch (Exception ex)
        {
            // Se descartó lo viejo pero no arrancó lo nuevo: no hay grabación en curso, así que la UI
            // no puede seguir diciendo que sí.
            LogBus.Log("teach", $"no se pudo reiniciar la enseñanza: {ex}");
            SetStatus($"No se pudo volver a grabar: {ex.Message}");
            await _teachSession.DisposeAsync();
            _teachSession = null;
            SetTeachingUi(false);
        }
        finally
        {
            RestartTeachBtn.IsEnabled = true;
            TeachBtn.IsEnabled = true;
        }
    }

    private async Task StopTeachingAsync()
    {
        SetTeachingUi(false);
        SetStatus("Procesando video grabado…");

        if (_teachSession != null)
        {
            try
            {
                await _teachSession.StopAsync();
                string summary = await _teachSession.ProcessAsync();
                // ProcessAsync ya llama a SetStatus con el resumen.
                Speak($"He aprendido: {summary}");
            }
            catch (Exception ex)
            {
                SetStatus($"Error en enseñanza: {ex.Message}");
            }
            finally
            {
                await _teachSession.DisposeAsync();
                _teachSession = null;
            }
        }
    }

    private void OnOpenVideos(object sender, RoutedEventArgs e)
    {
        if (_videoWindow == null || !_videoWindow.IsLoaded)
            _videoWindow = new VideoLibraryWindow(_videoLibrary);
        else
            _videoWindow.Reload();

        _videoWindow.Show();
        _videoWindow.Activate();
    }

    private void OnOpenWorkflows(object sender, RoutedEventArgs e)
    {
        if (_workflowWindow == null || !_workflowWindow.IsLoaded)
            _workflowWindow = new WorkflowLibraryWindow(_graphConfig, _backend!, _videoLibrary, _config.UserId);

        _workflowWindow.Show();
        _workflowWindow.Activate();
    }

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        if (_logWindow == null || !_logWindow.IsLoaded)
            _logWindow = new LogWindow();

        _logWindow.Show();
        _logWindow.Activate();
    }

    private void OnSaveConfig(object sender, RoutedEventArgs e)
    {
        _config.BackendUrl = BackendUrl.Text.Trim();
        _config.Save();
        // Recablea el cliente con la nueva URL (la credencial contra Graph sale de graph.json,
        // vía X-API-Key; el ClientToken legacy ya no se expone en la UI).
        var mcp = new LocalMcp(_uia);
        _backend = new BackendClient(_config, _graphConfig);
        _loop = new AgentLoop(_backend, _uia, mcp, this, this, InstalledApps.List,
            () => _locator?.Current, _workflowRunner);
        SetStatus("Backend guardado");
    }

    /// <summary>
    /// Enciende/apaga el inspector visual de elementos (overlay click-through con recuadros +
    /// diagnóstico de clic amarillo/rojo). Ver <see cref="UiInspector"/>.
    /// </summary>
    private void OnToggleInspector(object sender, RoutedEventArgs e)
    {
        _inspector ??= new UiInspector();
        bool on = _inspector.Toggle();
        InspectorBtn.Content = on ? "🔍 Inspector activo — clic para apagar" : "🔍 Inspector de elementos";
        SetStatus(on ? "Inspector de elementos activo" : "Inspector apagado");
    }

    /// <summary>Muestra/oculta el ID de superficie (badge arriba a la derecha). Ver <see cref="SurfaceLocator"/>.</summary>
    private void OnToggleLocator(object sender, RoutedEventArgs e)
    {
        if (_locator == null || _badge == null) return;
        if (_locator.Active)
        {
            _locator.Stop();
            _badge.Hide();
            LocatorBtn.Content = "📍 ID de superficie";
            SetStatus("Localizador apagado");
        }
        else
        {
            _locator.Start();
            _badge.Show();
            LocatorBtn.Content = "📍 ID visible — clic para ocultar";
            SetStatus("Localizador activo");
        }
    }

    private async Task StartGoal(string goal)
    {
        _cts = new CancellationTokenSource();
        StopBtn.Visibility = Visibility.Visible;
        SetStatus("Pensando…");
        try
        {
            string summary = await _loop.RunAsync(goal, _cts.Token);
            SetStatus(summary);
        }
        catch (OperationCanceledException) { SetStatus("Detenido"); }
        catch (Exception ex) { SetStatus($"Error: {ex.Message}"); }
        finally { StopBtn.Visibility = Visibility.Collapsed; }
    }

    // --- IVoice ---
    public void Narrate(string text) => Dispatcher.Invoke(() => Bubble.Text = text);
    public void Speak(string text)
    {
        Dispatcher.Invoke(() => { Bubble.Text = text; SetStatus(text); });
        _voice.Speak(text);
    }

    // --- IUserChannel ---
    public Task<string> AskAsync(string question, CancellationToken ct)
    {
        Dispatcher.Invoke(() =>
        {
            SetStatus(question);
            Input.Focus();
        });
        _pendingAnswer = new TaskCompletionSource<string>();
        ct.Register(() => _pendingAnswer?.TrySetResult(""));
        return _pendingAnswer.Task;
    }

    private void SetStatus(string s) => Dispatcher.Invoke(() => Status.Text = s.Length > 120 ? s[..120] + "…" : s);
}
