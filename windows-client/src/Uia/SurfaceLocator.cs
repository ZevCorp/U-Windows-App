using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using U.Graph.Surfaces;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Uia;

/// <summary>
/// Dónde está parado el usuario, como URL. Es el "location bar de Windows": el mismo concepto que la
/// extensión de Chrome usa con la URL (origin + pathname) para decidir qué workflows aplican.
///
/// IMPORTANTE — esta clase NO calcula el ID. Es el RELOJ, no el productor.
///
/// El ÚNICO productor del ID de superficie es <see cref="IUiSurface.Identity"/> (UiaSurface /
/// SapGuiSurface, en windows-graph): el mismo que usa <c>WorkflowRecorder</c> al grabar y
/// <c>WorkflowPlayer.SurfaceMismatch</c> al ejecutar. Que haya un solo productor es lo que hace que
/// el ID sirva de verdad para decidir: el origin que el agente reporta al backend cada turno es
/// LITERALMENTE el mismo string que quedó guardado en <c>source_origin</c> al grabar, así que el
/// scoping de workflows (AgentWorkflowStore.matchesSurface en Graph) compara manzanas con manzanas.
///
/// Antes esta clase sintetizaba su propio ID en paralelo al del grabador. Para apps nativas coincidía
/// por casualidad (ambos daban <c>uia://chrome.exe</c>, por caminos distintos), pero divergía en todo
/// lo demás: escritorio (<c>uia://explorer.exe/...</c> vs <c>uia://desktop</c>), navegador
/// (<c>web://dominio/ruta</c> vs <c>uia://chrome.exe/título</c>), SAP (<c>uia://saplogon.exe/slug</c>
/// vs <c>sapgui://PRD/VA01</c>) y el pathname (slug vs título crudo). En esas superficies el catálogo
/// MCP descartaba en silencio los workflows del sitio donde el usuario estaba parado.
/// Si hace falta cambiar la semántica del ID (p.ej. que el pathname sea una firma de pantalla en vez
/// del título), se cambia en Identity() y se propaga a todo por construcción — no aquí.
///
/// Lo que SÍ aporta esta clase es la señal de cambio: escucha los eventos de Windows (ver abajo) y,
/// cuando algo cambió, pide la identidad real (UIA/COM, que puede bloquear) en un hilo de fondo.
/// </summary>
public sealed class SurfaceLocator : IDisposable
{
    public sealed record SurfaceLocation(string Id, string Origin, string Path);

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    // ── LA SEÑAL: eventos de Windows, no un reloj ────────────────────────────
    // Windows avisa cuando cambia la ventana en foco (EVENT_SYSTEM_FOREGROUND) y cuando cambia su
    // título (EVENT_OBJECT_NAMECHANGE: otra pestaña, otro documento, misma ventana). Antes esto se
    // sondeaba con un timer de 800 ms y se medió el coste: el reloj tardaba de 15 a 734 ms (media
    // ~348) en descubrir algo que el sistema ya sabía, y el total hasta publicar era EXACTAMENTE ese
    // retraso — leer la identidad por UIA cuesta ~0 ms. O sea: todo el desfase era el reloj.
    //
    // Ese retraso es el que se veía en el inspector visual como recuadros de la pantalla anterior
    // dibujados sobre la nueva.
    //
    // El timer sigue, pero degradado a RED DE SEGURIDAD: cubre lo que los eventos no alcanzan
    // (ventanas de otro escritorio virtual, un hook que no se pudo instalar). Cuando el reloj tiene
    // que rescatar un cambio se registra en el log: si eso aparece seguido, algún evento falta.
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventProc lpfnWinEventProc,
        uint idProcess, uint idThread, uint dwFlags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hWinEventHook);
    private delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint EVENT_OBJECT_NAMECHANGE = 0x800C; // cambió el título: otra pestaña/documento
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;   // callback en NUESTRO hilo, sin inyectar DLL
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002; // las ventanas de Ü no son superficie
    private const int OBJID_WINDOW = 0;                  // el resto (menús, cursor, caret) no es la ventana

    /// <summary>Por encima de esto, el cambio lo cazó el reloj y no un evento: se registra para saberlo.</summary>
    private const long SafetyNetLogThresholdMs = 250;

    private IntPtr _foregroundHook;
    private IntPtr _nameHook;
    private WinEventProc? _winEventProc; // referencia viva: si el GC lo recoge, el hook revienta.
    private IntPtr _eventHwnd;
    private long _eventAtMs;
    private bool _probeQueued; // coalesce: NAMECHANGE llega en ráfagas
    private bool _primed;      // la primera sonda tras Start() siempre "cambia": no es un cambio real

    private readonly DispatcherTimer _timer;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly Func<SurfaceIdentity> _identity;
    private readonly IDisposable[] _owned;
    private IntPtr _lastHwnd;
    private string _lastTitle = "";
    private bool _computing;

    public SurfaceLocation? Current { get; private set; }
    public bool Active { get; private set; }
    public event Action<SurfaceLocation>? Changed;

    /// <summary>
    /// Uso normal: crea sus propias superficies y pregunta a la que corresponda según la app en foco
    /// (<see cref="SurfaceDetector"/>), igual que hacen el grabador y la biblioteca de workflows.
    /// </summary>
    public SurfaceLocator() : this(null, null) { }

    /// <summary>Inyectable para tests o para reutilizar superficies ya creadas.</summary>
    public SurfaceLocator(IUiSurface? uia, IUiSurface? sap)
    {
        bool ownsSurfaces = uia == null && sap == null;
        IUiSurface u = uia ?? new UiaSurface();
        IUiSurface s = sap ?? new SapGuiSurface();
        _identity = () => SurfaceDetector.Detect(u, s).Identity();
        _owned = ownsSurfaces ? new IDisposable[] { u, s } : Array.Empty<IDisposable>();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _timer.Tick += (_, __) => Probe();
    }

    /// <summary>Productor explícito (tests): esta clase solo lo llama, nunca calcula.</summary>
    public SurfaceLocator(Func<SurfaceIdentity> identity)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _owned = Array.Empty<IDisposable>();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _timer.Tick += (_, __) => Probe();
    }

    public void Start()
    {
        if (Active) return;
        Active = true;
        _lastHwnd = IntPtr.Zero; // fuerza recomputar ya
        _primed = false;

        // Los hooks se instalan desde el hilo de UI (tiene bomba de mensajes): con WINEVENT_OUTOFCONTEXT
        // el callback llega a ESTE hilo, así que puede tocar los campos sin sincronización.
        _winEventProc = OnWinEvent;
        _foregroundHook = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _winEventProc, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        // Rango aparte: FOREGROUND (0x0003) y NAMECHANGE (0x800C) están lejos, y pedirlos como un solo
        // rango arrastraría todos los eventos intermedios.
        _nameHook = SetWinEventHook(
            EVENT_OBJECT_NAMECHANGE, EVENT_OBJECT_NAMECHANGE,
            IntPtr.Zero, _winEventProc, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

        if (_foregroundHook == IntPtr.Zero || _nameHook == IntPtr.Zero)
            LogBus.Log("locator", "no se pudieron enganchar los eventos de ventana: se depende del reloj de 800 ms");

        _timer.Start();
        Probe();
    }

    public void Stop()
    {
        Active = false;
        _timer.Stop();
        if (_foregroundHook != IntPtr.Zero) { UnhookWinEvent(_foregroundHook); _foregroundHook = IntPtr.Zero; }
        if (_nameHook != IntPtr.Zero) { UnhookWinEvent(_nameHook); _nameHook = IntPtr.Zero; }
        _winEventProc = null;
    }

    /// <summary>
    /// Windows acaba de cambiar de ventana en primer plano, o de título de la ventana en foco. Esta es
    /// LA FUENTE del ritmo: se sondea al instante en vez de esperar al reloj.
    ///
    /// El trabajo no se hace aquí dentro (un callback de win-event debe volver rápido): se encola en el
    /// dispatcher. Y se coalesce, porque NAMECHANGE llega en ráfagas — de nada sirven diez sondas para
    /// un mismo cambio, y Probe() ya descarta por su cuenta lo que no cambió.
    /// </summary>
    private void OnWinEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (!Active || idObject != OBJID_WINDOW || hwnd == IntPtr.Zero) return;
        // NAMECHANGE lo emite cualquier ventana del sistema; solo importa el título de la que está delante.
        if (ev == EVENT_OBJECT_NAMECHANGE && hwnd != GetForegroundWindow()) return;

        _eventHwnd = hwnd;
        _eventAtMs = Environment.TickCount64;

        if (_probeQueued) return;
        _probeQueued = true;
        _dispatcher.BeginInvoke(new Action(() => { _probeQueued = false; Probe(); }));
    }

    /// <summary>
    /// ¿Este cambio lo trajo un evento o lo rescató el reloj? Devuelve la hora del evento (0 si no hubo).
    /// Silencioso cuando el camino rápido funciona; solo habla cuando la red de seguridad tuvo que actuar,
    /// que es justo lo que hay que vigilar.
    /// </summary>
    private long ConsumeEventStamp(IntPtr hwnd)
    {
        if (!_primed) { _primed = true; _eventHwnd = IntPtr.Zero; _eventAtMs = 0; return 0; }

        if (_eventHwnd == hwnd && _eventAtMs > 0)
        {
            long stamp = _eventAtMs;
            long lag = Environment.TickCount64 - stamp;
            if (lag > SafetyNetLogThresholdMs)
                LogBus.Log("locator", $"cambio servido por el reloj, no por el evento ({lag} ms)");
            _eventHwnd = IntPtr.Zero;
            _eventAtMs = 0;
            return stamp;
        }

        LogBus.Log("locator", "cambio sin evento previo (otro escritorio virtual, o evento perdido)");
        return 0;
    }

    /// <summary>Chequeo barato en el hilo de UI: solo hwnd + título. Lo caro va a un hilo de fondo.</summary>
    private void Probe()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;

        // Nuestras propias ventanas (la carita, el badge, el inspector) no son "una superficie":
        // conservan el ID de la app real que el usuario estaba usando. UiaSurface.Identity() también
        // las salta (RealForegroundWindow); esto es solo el corto-circuito barato para no recomputar.
        if (ProcessName(hwnd).Equals("U", StringComparison.OrdinalIgnoreCase)) return;

        var sb = new StringBuilder(512);
        GetWindowText(hwnd, sb, sb.Capacity);
        string title = sb.ToString();

        if (hwnd == _lastHwnd && title == _lastTitle) return;
        if (_computing) return;
        long eventAtMs = ConsumeEventStamp(hwnd);
        _lastHwnd = hwnd;
        _lastTitle = title;
        _computing = true;

        Task.Run(() =>
        {
            SurfaceLocation? loc = null;
            try
            {
                var id = _identity();
                // "unknown://" = la superficie no supo responder (sin ventana, SAP sin sesión). No se
                // publica: es mejor conservar la última ubicación conocida que borrar el scoping.
                if (id != null && !ReferenceEquals(id, SurfaceIdentity.Unknown)
                    && !string.IsNullOrWhiteSpace(id.Origin)
                    && !id.Origin.StartsWith("unknown://", StringComparison.OrdinalIgnoreCase))
                {
                    loc = new SurfaceLocation(id.Url, id.Origin, id.Pathname);
                }
            }
            catch
            {
                loc = null;
            }

            _dispatcher.BeginInvoke(new Action(() =>
            {
                _computing = false;
                if (loc == null || loc.Id == Current?.Id) return;
                // Lo que percibe el usuario: evento → publicar (incluye leer la identidad por UIA/COM).
                // Medido en ~0 ms con UIA; SAP por COM puede ser más lento, así que se vigila.
                long total = eventAtMs > 0 ? Environment.TickCount64 - eventAtMs : 0;
                if (total > SafetyNetLogThresholdMs)
                    LogBus.Log("locator", $"'{loc.Id}' tardo {total} ms en publicarse");
                Current = loc;
                Changed?.Invoke(loc);
            }));
        });
    }

    private static string ProcessName(IntPtr hwnd)
    {
        try
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch { return "app"; }
    }

    public void Dispose()
    {
        Stop();
        foreach (var d in _owned)
        {
            try { d.Dispose(); } catch { /* cerrar nunca debe tumbar la app */ }
        }
    }
}
