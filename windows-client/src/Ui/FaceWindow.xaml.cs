using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using U.Graph;
using U.Graph.Surfaces;
using U.WindowsClient.Agent;
using U.WindowsClient.Backend;
using U.WindowsClient.Diagnostics;
using U.WindowsClient.Mcp;
using U.WindowsClient.Navigation;
using U.WindowsClient.SystemApi;
using U.WindowsClient.Teach;
using U.WindowsClient.Telemetry;
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
    // "Enseñar" unificado: graba pasos UIA (WorkflowRecorder) + video (TeachSession) en paralelo.
    private WorkflowTeachSession? _teachSession;
    private readonly UiaSurface _teachUiaSurface = new();
    private readonly SapGuiSurface _teachSapSurface = new();
    private bool _teaching;
    private UiInspector? _inspector;
    private SurfaceLocator? _locator;
    private LocatorBadge? _badge;
    private WorkflowMcpRunner? _workflowRunner;

    // Selector de workflow directo en el panel Backend: lista cargada de Graph + un GraphClient propio
    // para listar/ejecutar sin abrir la biblioteca. El slider indexa esta lista.
    private GraphClient? _directGraph;
    private readonly List<WorkflowSummary> _directWorkflows = new();
    private int _directIndex;            // workflow seleccionado (carrusel y lista comparten este índice)
    private bool _directListMode;        // false = carrusel, true = lista
    private bool _syncingWorkflowUi;     // evita el ida y vuelta carrusel ↔ lista al sincronizar
    private bool _runningDirect;

    // Para resolver preguntas del asistente desde la caja de texto.
    private TaskCompletionSource<string>? _pendingAnswer;

    public FaceWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // Identidad del usuario (nombre+correo) al instalar. Debe ir ANTES de crear el backend para que
        // la telemetría de "Windows Live" arranque con el usuario correcto.
        EnsureOnboarded();

        // Esquina inferior derecha por defecto. El panel crece hacia arriba (ver OnSizeChanged) cuando
        // se abre un Expander (p.ej. "Backend"): sin esto, ResizeMode="NoResize" con altura fija
        // recortaba el contenido expandido y quedaba invisible.
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - Width - 24;
        Top = wa.Bottom - ActualHeight - 24;
        MaxHeight = wa.Height - 48; // al llegar al tope vertical, el globo hace scroll (ver Bubble)
        SizeChanged += OnSizeChanged;

        UpdateBackendStatus();
        UpdateVideoLlmToggle();
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
        _locator.Changed += loc => Dispatcher.Invoke(() =>
        {
            _badge?.SetText(loc.Id);
            // El inspector visual se SUSCRIBE a la ubicación en vez de descubrir el cambio por su
            // cuenta 700 ms después: así los recuadros no quedan dibujados sobre la pantalla anterior.
            // Es no-op si el inspector está apagado.
            _inspector?.InvalidateNow();
        });
        _locator.Start();

        var mcp = new LocalMcp(_uia);
        // El backend es Graph: la credencial (X-API-Key) sale del MISMO GraphConfig que usa la
        // ventana de workflows — una sola fuente de key para toda la app.
        _backend = new BackendClient(_config, _graphConfig);
        // "Windows Live": registra al usuario y empieza a emitir telemetría (pulsos consciente/
        // subconsciente + logs) al backend. No-op si el usuario no dio su correo.
        InitTelemetry();
        Closed += (_, __) => TelemetryBus.Shutdown();
        // La superficie actual viaja en cada turno (scoping de workflows) y las llamadas
        // workflow_* del cerebro se ejecutan con el WorkflowPlayer (subconsciente).
        _workflowRunner = new WorkflowMcpRunner(_graphConfig, this);
        _loop = new AgentLoop(_backend, _uia, mcp, this, this, InstalledApps.List,
            () => _locator?.Current, _workflowRunner);

        // Arrastrar por el texto de estado mueve el panel (la carita tiene sus propios gestos abajo).
        Header.MouseLeftButtonDown += (_, ev) => { if (ev.ButtonState == MouseButtonState.Pressed) DragMove(); };

        // La carita colapsada SIGUE al cursor automatizado durante la ejecución de workflows: se ve
        // "quién" está haciendo los clics. Evento estático de UiaSurface; se suelta al cerrar.
        UiaSurface.CursorMoved += OnAutomationCursorMoved;
        Closed += (_, __) => UiaSurface.CursorMoved -= OnAutomationCursorMoved;

        LoadSounds();
        WireFaceGestures();
        ApplyTheme(Enum.TryParse(_config.FaceTheme, out FaceTheme t) ? t : FaceTheme.Light);
        Face.StartIdle();          // gestos casuales: parpadeo, mirada, pulso
        CollapsedFace.StartIdle();

        StartUpdater();

        // Precarga la lista para el selector directo del panel Backend (silencioso si Graph no está listo).
        _ = ReloadDirectWorkflowsAsync();
    }

    /// <summary>
    /// Asegura la identidad del usuario: genera el InstallId (una vez) y, si aún no hay correo, muestra
    /// el popup de bienvenida para capturar nombre+correo. El correo es la clave canónica en el backend.
    /// Si el usuario cierra el popup sin completarlo, se seguirá sin telemetría y se re-preguntará en el
    /// próximo arranque — nunca bloquea el uso del asistente.
    /// </summary>
    private void EnsureOnboarded()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_config.InstallId))
            {
                _config.InstallId = Guid.NewGuid().ToString("N");
                _config.Save();
            }
            if (_config.Onboarded) return;

            var win = new OnboardingWindow { Owner = this };
            if (win.ShowDialog() == true && !string.IsNullOrWhiteSpace(win.EnteredEmail))
            {
                _config.DisplayName = win.EnteredName;
                _config.Email = win.EnteredEmail;
                _config.UserId = win.EnteredEmail; // el scoping de workflows y la telemetría hablan del mismo usuario
                _config.Save();
            }
        }
        catch (Exception ex) { LogBus.Log("onboarding", ex.Message); }
    }

    /// <summary>Arranca la telemetría de "Windows Live" con la identidad actual (no-op sin correo).</summary>
    private void InitTelemetry()
    {
        if (_backend == null || !_config.Onboarded) return;
        var identity = new TelemetryIdentity
        {
            Email = _config.Email,
            InstallId = _config.InstallId,
            DisplayName = _config.DisplayName,
            AppId = _graphConfig.AppId,
            AppVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "",
            MachineName = Environment.MachineName,
            OsVersion = Environment.OSVersion.VersionString
        };
        TelemetryBus.Init(_backend, identity);
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

    /// <summary>
    /// Mantiene fija la esquina inferior derecha: si la ventana cambia de tamaño (Expander abierto,
    /// o colapsar a solo la carita) crece/encoge hacia arriba y hacia la izquierda, sin mover esa
    /// esquina. Anclar por el delta (en vez de a la esquina del escritorio) conserva la posición si
    /// el usuario la arrastró a otro sitio.
    /// </summary>
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.PreviousSize.Width > 0) Left += e.PreviousSize.Width - e.NewSize.Width;
        if (e.PreviousSize.Height > 0) Top += e.PreviousSize.Height - e.NewSize.Height;

        // Nunca dejar la ventana fuera del área de trabajo (p.ej. al crecer cerca de un borde).
        var wa = SystemParameters.WorkArea;
        Left = Math.Clamp(Left, wa.Left, Math.Max(wa.Left, wa.Right - e.NewSize.Width));
        Top = Math.Clamp(Top, wa.Top, Math.Max(wa.Top, wa.Bottom - e.NewSize.Height));
    }

    // --- La carita sigue al cursor automatizado (solo colapsada) ---

    private long _lastFollowMs;

    /// <summary>
    /// Mueve la carita colapsada junto al cursor automatizado, con un offset para no tapar el objetivo
    /// del clic. Throttle a ~30ms para no inundar el Dispatcher (el cursor emite frame a frame). Las
    /// coordenadas llegan en píxeles físicos; WPF posiciona en DIPs → se divide por la escala de DPI.
    /// </summary>
    private void OnAutomationCursorMoved(int x, int y)
    {
        long now = Environment.TickCount64;
        if (now - _lastFollowMs < 30) return;
        _lastFollowMs = now;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_collapsed) return;
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
            double px = x / dpi.DpiScaleX + 18, py = y / dpi.DpiScaleY + 18;
            var wa = SystemParameters.WorkArea;
            Left = Math.Clamp(px, wa.Left, Math.Max(wa.Left, wa.Right - Width));
            Top = Math.Clamp(py, wa.Top, Math.Max(wa.Top, wa.Bottom - ActualHeight));
        }));
    }

    // --- Colapsar / expandir: la carita alterna entre el panel completo y solo ella misma ---

    private const double ExpandedWidth = 320;
    private const double CollapsedWidth = 128; // 72 de la carita + 28 de aire a cada lado (sombra)
    private bool _collapsed;

    /// <summary>
    /// Alterna entre el panel completo y SOLO la carita. Al colapsar, se oculta el panel y aparece la
    /// carita suelta (misma que el header); al expandir, vuelve el panel intacto. El cambio de tamaño
    /// lo reancla <see cref="OnSizeChanged"/> a la esquina inferior derecha.
    /// </summary>
    private void ToggleCollapsed()
    {
        _collapsed = !_collapsed;
        if (_collapsed)
        {
            CollapsedFace.Thinking = Face.Thinking; // que la carita suelta refleje el mismo estado
            Panel.Visibility = Visibility.Collapsed;
            CollapsedFace.Visibility = Visibility.Visible;
            Width = CollapsedWidth;
        }
        else
        {
            CollapsedFace.Visibility = Visibility.Collapsed;
            Panel.Visibility = Visibility.Visible;
            Width = ExpandedWidth;
        }
    }

    // --- Temas de la carita: se alternan manteniéndola oprimida (claro → oscuro → transparente) ---

    private FaceTheme _theme = FaceTheme.Dark;

    /// <summary>Aplica el tema (claro/oscuro) a ambas caritas.</summary>
    private void ApplyTheme(FaceTheme theme)
    {
        _theme = theme;
        Face.Theme = theme;
        CollapsedFace.Theme = theme;
    }

    /// <summary>Alterna el tema claro ↔ oscuro.</summary>
    private void CycleTheme()
    {
        var next = _theme == FaceTheme.Dark ? FaceTheme.Light : FaceTheme.Dark;
        ApplyTheme(next);
        _config.FaceTheme = next.ToString();
        _config.Save();
        PlayTick();
        SetStatus($"Tema {(next == FaceTheme.Dark ? "oscuro" : "claro")}");
    }

    /// <summary>Conecta los gestos (toque/doble toque/mantener/arrastre) a ambas caritas.</summary>
    private void WireFaceGestures()
    {
        // Carita del header (panel abierto): no se lanza al borde (arrastra el panel entero).
        new FaceGestures(this, Face, fling: false)
        {
            SingleTap = () => { PlayTick(); ToggleCollapsed(); },
            DoubleTap = StartMicByFace,
            LongPress = CycleTheme,
        };

        // Carita suelta (colapsada): además se puede lanzar de un lado a otro de la pantalla.
        new FaceGestures(this, CollapsedFace, fling: true)
        {
            SingleTap = () => { PlayTick(); ToggleCollapsed(); },
            DoubleTap = StartMicByFace,
            LongPress = CycleTheme,
        };
    }

    /// <summary>Doble clic en la carita = micrófono (como el doble toque de Android). El carrillón del
    /// doble clic predomina: no se solapa con el tick del clic simple porque el gesto ya se resolvió
    /// como doble antes de sonar nada.</summary>
    private void StartMicByFace()
    {
        PlayChime();
        OnMic(this, new RoutedEventArgs());
    }

    // --- Sonidos (los MISMOS WAV de Android): tick al clic, carrillón al micrófono ---

    private System.Media.SoundPlayer? _tick;
    private System.Media.SoundPlayer? _chime;

    private void LoadSounds()
    {
        _tick = LoadWav("assets/tick.wav");
        _chime = LoadWav("assets/mic_chime.wav");
    }

    private static System.Media.SoundPlayer? LoadWav(string relativeUri)
    {
        try
        {
            var stream = Application.GetResourceStream(new Uri(relativeUri, UriKind.Relative))?.Stream;
            if (stream == null) return null;
            var player = new System.Media.SoundPlayer(stream);
            player.Load(); // precarga: reproducir luego es inmediato
            return player;
        }
        catch { return null; }
    }

    private void PlayTick() { try { _tick?.Play(); } catch { } }
    private void PlayChime() { try { _chime?.Play(); } catch { } }

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
        if (!_graphConfig.IsConfigured)
        {
            SetStatus("Para enseñar, configura Graph (URL + API key) en el panel Backend.");
            return;
        }

        // Enseñanza unificada: recorder de pasos UIA + video, en paralelo, con un solo botón.
        var graph = new GraphClient(_graphConfig);
        _teachSession = new WorkflowTeachSession(
            graph, _graphConfig, _teachUiaSurface, _teachSapSurface, _backend!, _videoLibrary, _config.UserId)
        {
            ProcessVideo = _config.ProcessTeachVideo, // toggle del panel Backend: procesar o no el video con IA
        };
        _teachSession.StatusChanged += (_, msg) => Dispatcher.Invoke(() => SetStatus(msg));

        SetTeachingUi(true);
        try
        {
            // Título vacío: se autogenera al final desde lo aprendido (WorkflowLearner en Graph).
            // StartAsync incluye un countdown de 3s para que cambies a la app que vas a enseñar.
            await _teachSession.StartAsync("", CancellationToken.None);
        }
        catch (Exception ex)
        {
            LogBus.Log("teach", $"no se pudo iniciar la enseñanza: {ex}");
            SetStatus($"No se pudo iniciar la enseñanza: {ex.Message}");
            await _teachSession.DisposeAsync();
            _teachSession = null;
            SetTeachingUi(false);
        }
    }

    /// <summary>Botón 🎓 en rojo y 🔄 a la vista mientras se graba; todo de vuelta a lo normal si no.</summary>
    private void SetTeachingUi(bool teaching)
    {
        _teaching = teaching;
        TeachBtn.Content = teaching ? "⏸ Enseñar" : "🎓 Enseñar";
        TeachBtn.Background = new System.Windows.Media.SolidColorBrush(teaching
            ? System.Windows.Media.Color.FromRgb(255, 59, 48)  // rojo, como StopBtn
            : System.Windows.Media.Color.FromRgb(34, 34, 34)); // gris normal
        RestartTeachBtn.Visibility = Visibility.Collapsed; // reinicio en caliente: follow-up de la enseñanza unificada
    }

    /// <summary>
    /// "Me equivoqué": tira lo grabado y vuelve a grabar desde cero. Sin esto, un error a mitad de la
    /// demostración solo se podía resolver deteniendo — y detener manda el video malo a Gemini y sus
    /// notas al cerebro, que es exactamente lo que no queremos que aprenda.
    /// </summary>
    private void OnRestartTeach(object sender, RoutedEventArgs e)
    {
        // El reinicio en caliente aún no está en la enseñanza unificada (recorder de pasos + video):
        // detén con 🎓 y vuelve a empezar. Follow-up: WorkflowTeachSession.RestartAsync.
        SetStatus("Para rehacer: detén la enseñanza y vuelve a empezar.");
    }

    private async Task StopTeachingAsync()
    {
        SetTeachingUi(false);
        SetStatus("Cerrando la enseñanza y estructurando el workflow…");

        if (_teachSession != null)
        {
            try
            {
                FinishResponse finish = await _teachSession.StopAsync(CancellationToken.None);
                // El título se autogeneró en Graph a partir de lo aprendido (el summary del finish).
                // Sin voz aquí a propósito: el resultado se muestra en silencio en el estado.
                string title = string.IsNullOrWhiteSpace(finish.Summary) ? "el workflow" : finish.Summary;
                SetStatus($"Aprendido: {title}");
                _ = ReloadDirectWorkflowsAsync(); // que el recién enseñado aparezca ya en el selector
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

    // --- Toggle: procesar (o no) el video con IA al enseñar ---

    private void UpdateVideoLlmToggle() =>
        VideoLlmToggle.Content = _config.ProcessTeachVideo
            ? "🎬 Video → IA: activado"
            : "🎬 Video → IA: desactivado";

    /// <summary>
    /// Alterna si la enseñanza procesa el video con el LLM. Apagado evita el timeout (504) del backend;
    /// el video se sigue grabando y guardando (visible en 🎞 Videos). Se persiste en Config y se aplica
    /// a la próxima enseñanza (se lee al crear la WorkflowTeachSession).
    /// </summary>
    private void OnToggleVideoLlm(object sender, RoutedEventArgs e)
    {
        _config.ProcessTeachVideo = !_config.ProcessTeachVideo;
        _config.Save();
        UpdateVideoLlmToggle();
        SetStatus(_config.ProcessTeachVideo
            ? "Al enseñar, el video se procesará con IA"
            : "Al enseñar, el video NO se procesa con IA (se graba igual; míralo en Videos)");
    }

    // --- Selector de workflow directo (panel Backend) ---

    /// <summary>Al abrir el panel Backend refresca la lista, así un workflow recién enseñado ya aparece.</summary>
    private void OnBackendExpanded(object sender, RoutedEventArgs e)
    {
        if (!_runningDirect) _ = ReloadDirectWorkflowsAsync();
    }

    /// <summary>
    /// Carga los workflows de Graph para el selector directo del panel Backend: el carrusel/lista elige
    /// uno y "Ejecutar ahora" lo corre sin abrir la biblioteca. Silencioso si Graph aún no está configurado.
    /// </summary>
    private async Task ReloadDirectWorkflowsAsync()
    {
        if (_runningDirect) return; // no pisar la lista mientras corre uno
        if (!_graphConfig.IsConfigured)
        {
            SetWorkflowSelectorEmpty("Configura Graph (URL + API key) para ver tus workflows.");
            return;
        }

        _directGraph ??= new GraphClient(_graphConfig);
        try
        {
            var raw = await _directGraph.ListWorkflowsAsync(CancellationToken.None);
            _directWorkflows.Clear();
            _directWorkflows.AddRange(raw.Select(WorkflowSummary.FromJson));

            if (_directWorkflows.Count == 0)
            {
                SetWorkflowSelectorEmpty("No hay workflows todavía. Enseña uno con 🎓.");
                return;
            }

            // Llena la lista (la fuente de ambas vistas) y ancla el índice en rango.
            _syncingWorkflowUi = true;
            WorkflowListBox.ItemsSource = null;
            WorkflowListBox.ItemsSource = _directWorkflows;
            _syncingWorkflowUi = false;

            if (_directIndex >= _directWorkflows.Count) _directIndex = 0;
            bool many = _directWorkflows.Count > 1;
            WorkflowPrev.IsEnabled = many;
            WorkflowNext.IsEnabled = many;
            RunWorkflowBtn.IsEnabled = true;
            WorkflowDeleteBtn.IsEnabled = true;
            SetDirectIndex(_directIndex);
        }
        catch (Exception ex)
        {
            LogBus.Log("workflow-ui", $"selector directo: ListWorkflowsAsync falló: {ex}");
            SetWorkflowSelectorEmpty($"No se pudieron cargar los workflows: {ex.Message}");
        }
    }

    /// <summary>Sin workflows utilizables: mensaje en el carrusel y todo deshabilitado.</summary>
    private void SetWorkflowSelectorEmpty(string message)
    {
        _directWorkflows.Clear();
        WorkflowListBox.ItemsSource = null;
        WorkflowPick.Text = message;
        WorkflowPrev.IsEnabled = false;
        WorkflowNext.IsEnabled = false;
        RunWorkflowBtn.IsEnabled = false;
        WorkflowDeleteBtn.IsEnabled = false;
    }

    private void OnWorkflowPrev(object sender, RoutedEventArgs e) => StepWorkflow(-1);
    private void OnWorkflowNext(object sender, RoutedEventArgs e) => StepWorkflow(+1);

    /// <summary>Borra el workflow que muestra el carrusel/lista, tras confirmar. Refresca al terminar.</summary>
    private async void OnDeleteWorkflow(object sender, RoutedEventArgs e)
    {
        if (_runningDirect) return;
        if (_directIndex < 0 || _directIndex >= _directWorkflows.Count) return;
        var wf = _directWorkflows[_directIndex];

        var confirm = MessageBox.Show(
            $"¿Borrar «{wf.Title}»?\n\nNo se puede deshacer.",
            "Borrar workflow", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        WorkflowDeleteBtn.IsEnabled = false;
        try
        {
            _directGraph ??= new GraphClient(_graphConfig);
            await _directGraph.DeleteWorkflowAsync(wf.Id, CancellationToken.None);
            SetStatus($"Borrado: «{wf.Title}»");
            await ReloadDirectWorkflowsAsync();
        }
        catch (Exception ex)
        {
            LogBus.Log("workflow-ui", $"borrar workflow falló: {ex}");
            SetStatus($"No se pudo borrar: {ex.Message}");
            WorkflowDeleteBtn.IsEnabled = true;
        }
    }

    /// <summary>Avanza/retrocede en el carrusel con vuelta circular (del último salta al primero).</summary>
    private void StepWorkflow(int delta)
    {
        if (_directWorkflows.Count == 0) return;
        int n = _directWorkflows.Count;
        SetDirectIndex(((_directIndex + delta) % n + n) % n);
    }

    /// <summary>Alterna carrusel ↔ lista con el botón ☰.</summary>
    private void OnToggleWorkflowView(object sender, RoutedEventArgs e)
    {
        _directListMode = !_directListMode;
        WorkflowCarousel.Visibility = _directListMode ? Visibility.Collapsed : Visibility.Visible;
        WorkflowListBox.Visibility = _directListMode ? Visibility.Visible : Visibility.Collapsed;
        WorkflowViewToggle.Content = _directListMode ? "▤" : "☰";
        WorkflowViewToggle.ToolTip = _directListMode ? "Ver como carrusel" : "Ver como lista";
        if (_directListMode) SetDirectIndex(_directIndex); // deja la fila seleccionada a la vista
    }

    private void OnWorkflowListSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingWorkflowUi) return;
        if (WorkflowListBox.SelectedIndex >= 0) SetDirectIndex(WorkflowListBox.SelectedIndex);
    }

    /// <summary>Única fuente de verdad del workflow elegido: sincroniza carrusel y lista.</summary>
    private void SetDirectIndex(int i)
    {
        if (_directWorkflows.Count == 0) return;
        _directIndex = Math.Clamp(i, 0, _directWorkflows.Count - 1);
        var wf = _directWorkflows[_directIndex];
        WorkflowPick.Text = $"{_directIndex + 1}/{_directWorkflows.Count} · {wf.Title} ({wf.StepCount} paso(s))";

        if (!_syncingWorkflowUi && WorkflowListBox.SelectedIndex != _directIndex)
        {
            _syncingWorkflowUi = true;
            WorkflowListBox.SelectedIndex = _directIndex;
            WorkflowListBox.ScrollIntoView(wf);
            _syncingWorkflowUi = false;
        }
    }

    /// <summary>
    /// Ejecuta el workflow que apunta el slider, igual que "Ejecutar ahora" de la biblioteca: pide el
    /// plan a Graph y lo corre con el <see cref="WorkflowPlayer"/> (Graph decide QUÉ, esta máquina CÓMO).
    /// Se alinea conscientemente si la pantalla no coincide y aprende esa alineación para la próxima.
    /// El botón ⏹ cancela por el mismo <c>_cts</c> que el resto del panel.
    /// </summary>
    private async void OnRunWorkflowDirect(object sender, RoutedEventArgs e)
    {
        if (_runningDirect) return;
        if (_directIndex < 0 || _directIndex >= _directWorkflows.Count) { SetStatus("Selecciona un workflow primero."); return; }
        var wf = _directWorkflows[_directIndex];

        _runningDirect = true;
        RunWorkflowBtn.IsEnabled = false;
        _cts = new CancellationTokenSource();
        StopBtn.Visibility = Visibility.Visible;
        SetThinking(true);
        SetStatus($"Ejecutando «{wf.Title}»…");
        string? bridgeGoal = null; // puente subconsciente→consciente si el workflow se detiene
        try
        {
            _directGraph ??= new GraphClient(_graphConfig);
            // Log detallado dentro de la superficie (resolución + clic): el punto ciego donde no veíamos
            // por qué un paso decía ✓ sin pasar nada. Tag "uia" en 📜 Logs.
            var uia = new UiaSurface { Log = s => LogBus.Log("uia", s) };
            var player = new WorkflowPlayer(_directGraph, _graphConfig, uia, new SapGuiSurface())
            {
                // Banco de pruebas del motor de navegación nuevo (escalera de rutas: enfocar → acceso
                // directo → shell). Los otros dos sitios (MCP/chat, biblioteca) siguen en AppAligner
                // hasta validar aquí. Ver SurfaceNavigator.
                Aligner = SurfaceNavigator.Default.EnsureAsync,
                Log = s => LogBus.Log("workflow", s)
            };
            player.StepDone += (_, o) => Narrate(o.Ok ? $"✓ {o.Label}" : $"✗ {o.Label}: {o.Error}");

            RunResult result = await player.RunAsync(wf.Id, null, strictSurface: true, _cts.Token);
            SetStatus(result.Ok
                ? $"«{wf.Title}» terminó: {result.Completed}/{result.Steps.Count} pasos."
                : $"«{wf.Title}» se detuvo: {result.Error}");

            if (result.Ok && result.AlignedConsciously)
                _ = _directGraph.PrependAlignmentStepAsync(wf.Id, CancellationToken.None); // aprende a alcanzar su superficie

            // PUENTE subconsciente→consciente: si el workflow se detuvo (y no fue cancelado por el
            // usuario), el cerebro consciente (computer-use) retoma desde la pantalla actual con el
            // contexto del fallo. Al despejar el obstáculo puede re-invocar el workflow: la reanudación
            // por ubicación hace el resto (se salta lo ya hecho).
            if (!result.Ok && !_cts.IsCancellationRequested)
                bridgeGoal =
                    $"Estaba ejecutando el workflow «{wf.Title}» y se detuvo en: {result.Error}. " +
                    $"Ya se completaron {result.Completed} de {result.Steps.Count} pasos. " +
                    "Retoma desde la pantalla actual y termina la tarea del workflow. Si despejas el " +
                    "obstáculo, puedes invocar de nuevo la herramienta del workflow: se reanuda solo " +
                    "desde la ubicación actual sin repetir lo ya hecho.";
        }
        catch (OperationCanceledException) { SetStatus("Detenido"); }
        catch (Exception ex)
        {
            LogBus.Log("workflow-ui", $"selector directo: RunAsync falló: {ex}");
            SetStatus($"Error ejecutando: {ex.Message}");
        }
        finally
        {
            _runningDirect = false;
            RunWorkflowBtn.IsEnabled = true;
            StopBtn.Visibility = Visibility.Collapsed;
            SetThinking(false);
            _cts = null;
        }

        // Fuera del try/finally: StartGoal crea su PROPIO _cts (adentro lo pisaría el finally).
        if (bridgeGoal != null)
        {
            LogBus.Log("workflow-ui", "puente consciente: el workflow se detuvo → computer-use retoma");
            SetStatus("El workflow se detuvo — el modo consciente retoma…");
            _ = StartGoal(bridgeGoal);
        }
    }

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        if (_logWindow == null || !_logWindow.IsLoaded)
            _logWindow = new LogWindow();

        _logWindow.Show();
        _logWindow.Activate();
    }

    /// <summary>
    /// Estado de conexión con Graph (solo lectura). El backend ya no se configura a mano: la URL tiene
    /// default sano y la API key llega sola (embebida en el build distribuido, o env GRAPH_API_KEY en
    /// dev). Si falta la key, se dice cómo ponerla una sola vez, sin exponer ningún campo editable.
    /// </summary>
    private void UpdateBackendStatus()
    {
        string host = _graphConfig.BaseUrl.Replace("https://", "").Replace("http://", "").TrimEnd('/');
        BackendStatus.Text = _graphConfig.IsConfigured
            ? $"✓ Conectado a {host}"
            : "⚠ Sin API key. Una sola vez en dev: setx GRAPH_API_KEY \"tu_key\" y reinicia Ü.";
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
        SetThinking(true);
        SetStatus("Pensando…");
        try
        {
            string summary = await _loop.RunAsync(goal, _cts.Token);
            SetStatus(summary);
        }
        catch (OperationCanceledException) { SetStatus("Detenido"); }
        catch (Exception ex) { SetStatus($"Error: {ex.Message}"); }
        finally { StopBtn.Visibility = Visibility.Collapsed; SetThinking(false); }
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

    /// <summary>Pone la carita (ambas: la del header y la suelta) en modo "pensando" mientras ejecuta.</summary>
    private void SetThinking(bool on) => Dispatcher.Invoke(() =>
    {
        Face.Thinking = on;
        CollapsedFace.Thinking = on;
    });
}
