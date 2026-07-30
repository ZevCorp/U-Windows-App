using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using U.Graph;
using U.Graph.Surfaces;
using U.WindowsClient.Agent;
using U.WindowsClient.Backend;
using U.WindowsClient.Clinical;
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
    // Las dos superficies de la enseñanza, con su diagnóstico enchufado al registro. Sin esto sus
    // eventos se emitían al vacío: `SapGuiSurface.Diagnostic` no tenía UN SOLO suscriptor en todo el
    // cliente, así que la línea que dice si el enganche de eventos COM funcionó —«observando por
    // eventos COM» vs «eventos COM no disponibles, cae a sondeo»— nunca llegó al log. Justo la que
    // decide si la grabación puede capturar la entrada a una transacción.
    private readonly UiaSurface _teachUiaSurface = new() { Log = s => LogBus.Log("teach-uia", s) };
    private readonly SapGuiSurface _teachSapSurface = new();
    private bool _teaching;
    private UiInspector? _inspector;
    private SurfaceLocator? _locator;
    private LocatorBadge? _badge;
    private WorkflowMapWindow? _map;
    // El mapa base del computador (la capa gris): se alimenta SIEMPRE del caudal del locator,
    // esté o no abierta la visualización — el terreno se acumula mientras el usuario vive su día.
    private SurfaceMap? _surfaceMap;
    private ClickWatcher? _clickWatcher;
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
        // Aquí y no al crear la WorkflowTeachSession: esa se construye en CADA pulsación de «Enseñar»
        // y acumularía una suscripción por intento, multiplicando cada línea en el registro.
        _teachSapSurface.Diagnostic += (_, msg) => LogBus.Log("teach-sap", msg);
        SetStepModeUi(); // el botón nace con su etiqueta puesta, no vacío hasta el primer clic
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

        // Puente clínico: si ya se emparejó en otra sesión, se retoma solo. Sin esto había que
        // teclear el código en CADA arranque de Ü, que es la fricción que sobra en una consulta.
        if (_config.ClinicalCode.Length == 8)
        {
            _clinical.Pair(_config.ClinicalCode);
            if (_clinical.Active)
            {
                ClinicalCodeBox.Text = _clinical.Code;
                ClinicalPairBtn.Content = "Soltar";
                _clinicalStep = -1;
                SetClinicalStep(1, "Esperando a que guardes la nota en el portal.");
            }
        }
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
        _surfaceMap = SurfaceMap.Load();
        // El vigilante de clics: sin él las aristas del terreno solo dicen que dos pantallas
        // conectan; con él dicen CÓMO pasar de una a otra, que es lo que permite navegar sin
        // haber grabado un workflow. Siempre activo, porque el terreno se aprende viviendo.
        _clickWatcher = new ClickWatcher();
        _clickWatcher.Start();
        _surfaceMap.Clicks = _clickWatcher;
        Closed += (_, __) =>
        {
            _surfaceMap?.Save();
            _clickWatcher?.Dispose(); // un hook huérfano ralentiza el ratón de TODA la máquina
        };
        _locator.Changed += loc => Dispatcher.Invoke(() =>
        {
            _badge?.SetText(loc.Id);
            _surfaceMap?.Observe(loc.Id); // el terreno se aprende navegando, sin enseñar nada
            _map?.SetCurrent(loc.Id);     // y el mapa ilumina el nodo donde estás parado
        });
        _locator.Start();

        // Puente clínico: se sondea cada 3 s, no en cada cambio de pantalla. El médico
        // puede guardar la nota DESPUÉS de que SAP ya esté en la pantalla, así que
        // reaccionar solo al cambio de superficie perdería justo ese caso. Cuando no hay
        // código emparejado esto no hace ni una llamada.
        var clinicalTimer = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Background)
        { Interval = TimeSpan.FromSeconds(3) };
        clinicalTimer.Tick += async (_, __) =>
        {
            try { await ClinicalTickAsync(); }
            catch (Exception ex) { LogBus.Log("clinico", $"tick falló: {ex.Message}"); }
        };
        clinicalTimer.Start();

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

        // Cierres de grabación que quedaron a medias por un 504 de Graph: se reintentan al arrancar, en
        // segundo plano y sin molestar. Va ANTES de recargar la lista para que, si alguno sale, el
        // workflow ya aparezca con su resumen puesto.
        _ = RetryPendingFinishesAsync();

        // Precarga la lista para el selector directo del panel Backend (silencioso si Graph no está listo).
        _ = ReloadDirectWorkflowsAsync();
    }

    /// <summary>Completa en segundo plano los cierres que un 504 dejó pendientes. Nunca interrumpe.</summary>
    private async Task RetryPendingFinishesAsync()
    {
        if (!_graphConfig.IsConfigured) return;
        try
        {
            int done = await PendingFinish.RetryAllAsync(new GraphClient(_graphConfig), CancellationToken.None);
            if (done > 0)
            {
                SetStatus($"Se completó el resumen de {done} grabación(es) que habían quedado a medias.");
                await ReloadDirectWorkflowsAsync();
            }
        }
        catch (Exception e) { LogBus.Log("teach", $"reintento de cierres pendientes falló: {e.Message}"); }
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
            catch (FinishPendingException pending)
            {
                // NO es lo mismo que perder la grabación, y decirlo importa: los pasos ya están en
                // Graph. Se guarda el id para completar el resumen luego, sin regrabar nada.
                PendingFinish.Save(pending.SessionId, pending.WorkflowId);
                LogBus.Log("teach", $"cierre pendiente (HTTP {pending.StatusCode}): {pending.Message}");
                SetStatus("Los pasos SÍ se guardaron; falta el resumen (Graph tardó de más). "
                        + "Se completa solo al reabrir la app.");
                _ = ReloadDirectWorkflowsAsync(); // el workflow existe aunque le falte el resumen
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
            DryRunBtn.IsEnabled = true;
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
        DryRunBtn.IsEnabled = false;
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

    private bool _stepMode;
    private StepDebuggerWindow? _debugger;

    // ── Puente con la consulta del portal ───────────────────────────────────────
    private readonly ClinicalBridge _clinical = new();
    private readonly SapGuiSurface _clinicalSap = new();
    private bool _offering;             // ya hay un ofrecimiento en pantalla
    private string _offeredRev = "";    // no ofrecer dos veces lo mismo
    private string _focusedRev = "";    // no robar el foco más de una vez por versión
    private int _clinicalStep = -1;

    /// <summary>
    /// El paso del circuito, a la vista. Sin esto el operador solo veía «Emparejado» y no
    /// tenía forma de saber cuál de las dos condiciones faltaba —la nota guardada o la
    /// pantalla de SAP—, que es justo lo que hay que poder mirar de un vistazo en vivo.
    ///
    /// Solo se escribe cuando el paso CAMBIA: repintar el mismo texto cada 3 s haría
    /// parpadear el panel y ensuciaría el registro.
    /// </summary>
    private void SetClinicalStep(int step, string text)
    {
        if (_clinicalStep == step) return;
        _clinicalStep = step;
        string[] marks = { "①", "②", "③", "④" };
        string prefix = step >= 1 && step <= 4 ? $"{marks[step - 1]} " : "";
        Dispatcher.Invoke(() => ClinicalStatus.Text = prefix + text);
        LogBus.Log("clinico", $"paso {step}: {text}");
    }

    private void OnClinicalPair(object sender, RoutedEventArgs e)
    {
        if (_clinical.Active)
        {
            _clinical.Unpair();
            ClinicalCodeBox.Text = "";
            ClinicalPairBtn.Content = "Emparejar";
            ClinicalStatus.Text = "Sin emparejar.";
            _config.ClinicalCode = "";   // soltar es soltar: no debe resucitar al reiniciar
            _config.Save();
            return;
        }

        _clinical.Pair(ClinicalCodeBox.Text);
        if (!_clinical.Active)
        {
            ClinicalStatus.Text = "El código son 8 caracteres.";
            return;
        }

        _config.ClinicalCode = _clinical.Code;   // se teclea una vez por instalación, no por arranque
        _config.Save();

        _offeredRev = "";
        _focusedRev = "";
        _clinicalStep = -1;
        ClinicalPairBtn.Content = "Soltar";
        SetClinicalStep(1, "Esperando a que guardes la nota en el portal.");
    }

    /// <summary>
    /// ¿Esta pantalla admite datos de la consulta? Se decide INTENTANDO emparejar los
    /// conceptos con los campos que hay delante, no comparando la transacción contra una
    /// lista. Es autoverificable: si en la pantalla no existen «Talla», «Peso» y compañía,
    /// no se empareja nada y no se ofrece — sin depender de un código de transacción que
    /// puede cambiar entre hospitales o entre versiones.
    /// </summary>
    private async Task ClinicalTickAsync()
    {
        if (!_clinical.Active || _clinical.Stopped || _offering || _teaching || _runningDirect) return;

        // ── PASO 1: ¿la nota ya está guardada allá? ─────────────────────────────
        var data = await _clinical.FetchAsync(CancellationToken.None);
        if (data.Count == 0)
        {
            SetClinicalStep(1, "Esperando a que guardes la nota en el portal.");
            return;
        }

        // ── PASO 2: ¿SAP está delante? ──────────────────────────────────────────
        var loc = _locator?.Current;
        bool enSap = loc != null && loc.Origin.StartsWith("sapgui://", StringComparison.OrdinalIgnoreCase);
        if (!enSap)
        {
            // Los datos ya están; lo único que falta es SAP. Se trae al frente UNA vez
            // por versión de la nota: insistir cada 3 s le robaría el teclado al operador
            // mientras escribe en otro sitio, que es exactamente lo que no debe pasar.
            SetClinicalStep(2, $"{data.Count} dato(s) listos. Abriendo SAP…");
            if (_clinical.LastRev != _focusedRev)
            {
                _focusedRev = _clinical.LastRev;
                LogBus.Log("clinico", "datos listos y SAP no está delante: se trae al frente");
                try { await AppAligner.EnsureAsync("sapgui://", () => _locator?.Current?.Origin ?? "", CancellationToken.None); }
                catch (Exception e) { LogBus.Log("clinico", $"no se pudo traer SAP al frente: {e.Message}"); }
            }
            return;
        }

        if (_clinical.LastRev == _offeredRev) return;   // ya se ofreció esta versión

        string donde = loc?.Id ?? "SAP";

        // ── PASO 3: ¿esta pantalla tiene los campos? ────────────────────────────
        IReadOnlyList<DetectedField> fields;
        try { fields = await Task.Run(() => _clinicalSap.ReadFields()); }
        catch { return; }

        var bindings = ConceptBinder.Bind(data, fields);
        int escribibles = bindings.Count(b => !b.Occupied);
        if (escribibles == 0)
        {
            string why = bindings.Count == 0
                ? "Estás en SAP, pero esta pantalla no tiene campos de signos vitales."
                : "Todos los campos ya tienen valor: no se toca nada.";
            SetClinicalStep(3, why);
            LogBus.Log("clinico", $"{why} ({fields.Count} campo(s) leídos en '{loc!.Id}')");
            return;
        }

        // ── PASO 4: todo listo, se pide aprobación ──────────────────────────────
        _offering = true;
        _offeredRev = _clinical.LastRev;
        try
        {
            SetClinicalStep(4, $"Esperando tu aprobación para {escribibles} dato(s).");
            LogBus.Log("clinico", $"ofreciendo {escribibles} dato(s) en '{donde}'");

            var preview = new FillPreviewWindow(bindings, donde);
            bool ok = await preview.AskAsync();
            if (!ok)
            {
                LogBus.Log("clinico", "el operador canceló: no se escribió nada");
                _clinicalStep = -1;
                SetClinicalStep(3, "Cancelado. Se vuelve a ofrecer si cambias de pantalla o de nota.");
                _offeredRev = ""; // cancelar no es rechazar para siempre
                return;
            }

            int escritos = await Task.Run(() => Write(bindings));
            LogBus.Log("clinico", $"escritos {escritos}/{escribibles} dato(s) en SAP");
            _clinicalStep = -1;
            SetClinicalStep(4, $"✓ Escritos {escritos} dato(s) en SAP.");
        }
        finally { _offering = false; }
    }

    /// <summary>
    /// Escribe lo aprobado. Los ocupados NO se tocan — la regla no se comprueba solo al
    /// mostrar: se vuelve a comprobar aquí, porque entre la vista previa y el clic el
    /// operador pudo haber escrito en el campo.
    /// </summary>
    private int Write(IReadOnlyList<Binding> bindings)
    {
        int n = 0;
        foreach (Binding b in bindings)
        {
            if (b.Occupied) continue;
            var step = PlanStep.ForAutofill(b.Field, new FieldMatch { StepOrder = b.Field.StepOrder, Value = b.Data.Value });
            try
            {
                if (_clinicalSap.Execute(step, out string err)) n++;
                else LogBus.Log("clinico", $"«{b.FieldLabel}» no se pudo escribir: {err}");
            }
            catch (Exception e) { LogBus.Log("clinico", $"«{b.FieldLabel}» lanzó: {e.Message}"); }
        }
        return n;
    }

    /// <summary>
    /// Muestra u oculta el mapa del grafo. Se reconstruye al abrir —no en cada tick— porque los
    /// workflows cambian al grabar, no al navegar; y la iluminación del nodo actual sí es en vivo,
    /// por el mismo evento del locator que alimenta el badge.
    /// </summary>
    private async void OnToggleMap(object sender, RoutedEventArgs e)
    {
        if (_map != null)
        {
            _map.Close();
            _map = null;
            MapBtn.Content = "🗺 Mapa del grafo";
            return;
        }

        if (!_graphConfig.IsConfigured)
        {
            SetStatus("Configura Graph (URL + API key) para ver el mapa.");
            return;
        }

        _map = new WorkflowMapWindow(_surfaceMap);
        _map.Show();
        MapBtn.Content = "🗺 Mapa: cargando…";
        _directGraph ??= new GraphClient(_graphConfig);
        try
        {
            await _map.LoadAsync(_directGraph, CancellationToken.None);
            MapBtn.Content = "🗺 Mapa: visible — clic para ocultar";
            _map.SetCurrent(_locator?.Current?.Id ?? "");
        }
        catch (Exception ex)
        {
            LogBus.Log("mapa", $"no se pudo construir el grafo: {ex.Message}");
            SetStatus($"El mapa no cargó: {ex.Message}");
            _map.Close();
            _map = null;
            MapBtn.Content = "🗺 Mapa del grafo";
        }
    }

    private void OnToggleStepMode(object sender, RoutedEventArgs e)
    {
        _stepMode = !_stepMode;
        SetStepModeUi();
        SetStatus(_stepMode
            ? "Paso a paso ACTIVO: la próxima ejecución se detendrá antes de cada paso."
            : "Paso a paso apagado.");
    }

    private void SetStepModeUi()
    {
        StepModeBtn.Content = _stepMode ? "👣 Paso a paso: ACTIVO" : "👣 Paso a paso: apagado";
        StepModeBtn.Foreground = _stepMode
            ? System.Windows.Media.Brushes.White
            : new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
    }

    /// <summary>
    /// Ensayo en seco del workflow que apunta el slider: dice qué pasaría SIN tocar la pantalla.
    ///
    /// Se construye el MISMO player que ejecutaría de verdad —mismas superficies, mismo plan, mismo
    /// colapso de tecleos— porque un ensayo sobre una lista distinta de la que se ejecuta no vale nada.
    /// El detalle va al registro; en el globo solo el veredicto, que es lo que se mira de un vistazo
    /// cinco minutos antes de un demo.
    /// </summary>
    private async void OnDryRunWorkflow(object sender, RoutedEventArgs e)
    {
        if (_runningDirect) return;
        if (_directIndex < 0 || _directIndex >= _directWorkflows.Count) { SetStatus("Selecciona un workflow primero."); return; }
        var wf = _directWorkflows[_directIndex];

        DryRunBtn.IsEnabled = false;
        SetStatus($"Ensayando «{wf.Title}» en seco…");
        try
        {
            _directGraph ??= new GraphClient(_graphConfig);
            var uia = new UiaSurface { Log = s => LogBus.Log("uia", s) };
            var sap = new SapGuiSurface();
            sap.Diagnostic += (_, msg) => LogBus.Log("sap", msg);
            var player = new WorkflowPlayer(_directGraph, _graphConfig, uia, sap)
            {
                Log = s => LogBus.Log("ensayo", s)
            };

            DryRunReport report = await player.DryRunAsync(wf.Id, null, CancellationToken.None);

            string veredicto = report.Clean
                ? $"✓ Ensayo limpio: {report.Steps} pasos, sin bloqueantes"
                : $"✋ {report.Count(DryRunLevel.Bloqueante)} bloqueante(s) de {report.Steps} pasos";
            int avisos = report.Count(DryRunLevel.Aviso);
            SetStatus($"{veredicto}{(avisos > 0 ? $" · {avisos} aviso(s)" : "")} — detalle en 📜 Logs");
            Narrate(veredicto);
        }
        catch (Exception ex)
        {
            LogBus.Log("ensayo", $"el ensayo falló: {ex}");
            SetStatus($"El ensayo no pudo completarse: {ex.Message}");
        }
        finally { DryRunBtn.IsEnabled = true; }
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
            // La superficie SAP del player también habla: es la que dice qué rama tomó al accionar una
            // fila («isFolder=… → modo …»), el dato que faltaba para saber por qué un paso decía ✓ sin
            // que la pantalla cambiara. Antes se construía anónima y su Diagnostic no lo oía nadie.
            var sap = new SapGuiSurface();
            sap.Diagnostic += (_, msg) => LogBus.Log("sap", msg);
            var player = new WorkflowPlayer(_directGraph, _graphConfig, uia, sap)
            {
                // Banco de pruebas del motor de navegación nuevo (escalera de rutas: enfocar → acceso
                // directo → shell). Los otros dos sitios (MCP/chat, biblioteca) siguen en AppAligner
                // hasta validar aquí. Ver SurfaceNavigator.
                Aligner = SurfaceNavigator.Default.EnsureAsync,
                Log = s => LogBus.Log("workflow", s),
                // Las capturas de la enseñanza llevaban tiempo guardándose sin que las usara nadie.
                TaughtShotFor = (id, order) =>
                    System.IO.Path.Combine(StepShotCamera.FolderFor(id), $"step_{order}.png"),
            };

            if (_stepMode)
            {
                _debugger ??= new StepDebuggerWindow();
                player.OnStepPause = pause => _debugger.AskAsync(pause);
            }
            player.StepDone += (_, o) => Narrate(o.Ok ? $"✓ {o.Label}" : $"✗ {o.Label}: {o.Error}");

            RunResult result = await player.RunAsync(wf.Id, null, strictSurface: true, _cts.Token);
            SetStatus(result.Ok
                ? $"«{wf.Title}» terminó: {result.Tally}."
                : $"«{wf.Title}» se detuvo: {result.Error}");

            // Una ejecución nueva es una oportunidad nueva. El puente clínico se calla cuando ya
            // ofreció ESTA versión de la nota, y está bien para no repetir el ofrecimiento en la
            // misma pantalla — pero un workflow que acaba de navegar deja delante una pantalla
            // NUEVA y vacía, donde esos mismos datos sí hacen falta. Sin esto, correr el workflow
            // por segunda vez con la misma nota no ofrece nada y parece que el puente se rompió
            // (visto el 2026-07-28: 7/7 ejecutado y ni un ofrecimiento después).
            if (result.Ok && _clinical.Active)
            {
                _offeredRev = "";
                _clinicalStep = -1;
            }

            if (result.Ok && result.AlignedConsciously)
                _ = _directGraph.PrependAlignmentStepAsync(wf.Id, CancellationToken.None); // aprende a alcanzar su superficie

            // PUENTE subconsciente→consciente: si el workflow se detuvo (y no fue cancelado por el
            // usuario), el cerebro consciente (computer-use) retoma desde la pantalla actual con el
            // contexto del fallo. Al despejar el obstáculo puede re-invocar el workflow: la reanudación
            // por ubicación hace el resto (se salta lo ya hecho).
            if (!result.Ok && !_cts.IsCancellationRequested)
                bridgeGoal =
                    $"Estaba ejecutando el workflow «{wf.Title}» y se detuvo en: {result.Error}. " +
                    $"Del plan: {result.Tally}. Los omitidos NO se ejecutaron. " +
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
            // Se oculta, no se cierra: cerrar dispara el Closing, que significa «el operador paró».
            _debugger?.Finish();
        }

        // Fuera del try/finally: StartGoal crea su PROPIO _cts (adentro lo pisaría el finally).
        if (bridgeGoal != null)
        {
            // El workflow se detuvo ESTANDO en su aplicación, así que el origen de ahora es dónde vive
            // la tarea. Se lo pasamos al consciente como compuerta: puede navegar todo lo que quiera
            // DENTRO de esa app, pero no teclear en otra. Sin esto, «retoma desde la pantalla actual»
            // se ejecutó sobre la ventana que tuviera el foco — el incidente del 2026-07-26.
            string origin = _locator?.Current?.Origin ?? "";
            LogBus.Log("workflow-ui", "puente consciente: el workflow se detuvo → computer-use retoma"
                + (origin.Length > 0 ? $" (atado a «{origin}»)" : " · SIN origen conocido: va sin compuerta"));
            SetStatus("El workflow se detuvo — el modo consciente retoma…");
            _ = StartGoal(bridgeGoal, origin);
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

    /// <summary>
    /// Arranca el modo consciente. <paramref name="requireOrigin"/> ata el objetivo a una aplicación:
    /// vacío para lo que pide el usuario a mano (el destino puede ser cualquiera), y con valor cuando
    /// el objetivo viene del PUENTE — ahí sí se sabe dónde vive la tarea, y salirse de ahí es el bug.
    /// </summary>
    private async Task StartGoal(string goal, string requireOrigin = "")
    {
        _cts = new CancellationTokenSource();
        StopBtn.Visibility = Visibility.Visible;
        SetThinking(true);
        SetStatus("Pensando…");
        try
        {
            string summary = await _loop.RunAsync(goal, _cts.Token, requireOrigin);
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

