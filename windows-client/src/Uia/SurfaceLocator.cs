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
/// Antes esta clase sintetizaba su propio ID (<c>uia://notepad.exe/slug-del-titulo</c>) mientras el
/// grabador guardaba otro (<c>uia://notepad</c>, título crudo). Dos formatos para la misma pantalla:
/// el catálogo MCP descartaba en silencio los workflows de la app donde el usuario estaba parado.
/// Si hace falta cambiar la semántica del ID (p.ej. que el pathname sea una firma de pantalla en vez
/// del título), se cambia en Identity() y se propaga a todo por construcción — no aquí.
///
/// Lo que SÍ aporta esta clase es el ritmo y la señal de cambio: sondea la ventana en primer plano
/// con un timer barato (hwnd + título vía GetWindowText) y solo cuando algo cambió pide la identidad
/// real (UIA/COM, que puede bloquear) en un hilo de fondo.
/// </summary>
public sealed class SurfaceLocator : IDisposable
{
    public sealed record SurfaceLocation(string Id, string Origin, string Path);

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    // ── MEDICIÓN (temporal) ──────────────────────────────────────────────────
    // Windows YA avisa cuando cambia la ventana en foco (EVENT_SYSTEM_FOREGROUND). Este locator no lo
    // escucha: sondea con un reloj de 800 ms, así que descubre tarde algo que el sistema sabía al
    // instante. Ese retraso es el que se ve como desfase en el inspector visual (recuadros de la
    // pantalla anterior sobre la pantalla nueva).
    //
    // Esto NO cambia el comportamiento: solo engancha el evento para MEDIR cuánto tarda el reloj en
    // enterarse. Con el número medido se decide promover el hook de sonda a FUENTE (disparar Probe()
    // desde OnForegroundChanged y dejar el timer como red de seguridad) — una línea.
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventProc lpfnWinEventProc,
        uint idProcess, uint idThread, uint dwFlags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hWinEventHook);
    private delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;   // callback en NUESTRO hilo, sin inyectar DLL
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002; // las ventanas de Ü no son superficie
    private const int OBJID_WINDOW = 0;                  // el resto (menús, cursor, caret) no es la ventana

    private IntPtr _winEventHook;
    private WinEventProc? _winEventProc; // referencia viva: si el GC lo recoge, el hook revienta.
    private IntPtr _eventHwnd;
    private long _eventAtMs;
    private bool _primed; // la primera sonda tras Start() siempre "cambia": no es una medición válida

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

        // El hook se instala desde el hilo de UI (tiene bomba de mensajes): con WINEVENT_OUTOFCONTEXT
        // el callback llega a ESTE hilo, así que puede tocar los campos sin sincronización.
        _winEventProc = OnForegroundChanged;
        _winEventHook = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _winEventProc, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        if (_winEventHook == IntPtr.Zero) LogBus.Log("locator-lag", "no se pudo enganchar EVENT_SYSTEM_FOREGROUND (sin medición)");

        _timer.Start();
        Probe();
    }

    public void Stop()
    {
        Active = false;
        _timer.Stop();
        if (_winEventHook != IntPtr.Zero) { UnhookWinEvent(_winEventHook); _winEventHook = IntPtr.Zero; }
        _winEventProc = null;
    }

    /// <summary>
    /// Windows acaba de cambiar la ventana en primer plano. HOY solo se anota la hora para medir
    /// cuánto tarda el reloj en descubrir lo mismo. Para PROMOVERLO a fuente: llamar aquí a Probe().
    /// </summary>
    private void OnForegroundChanged(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != OBJID_WINDOW || hwnd == IntPtr.Zero) return;
        _eventHwnd = hwnd;
        _eventAtMs = Environment.TickCount64;
    }

    /// <summary>
    /// ¿Cuánto tardó el reloj en ver este cambio? Devuelve la hora del evento (0 si no hubo) para poder
    /// medir después el total hasta publicar, que es lo que de verdad percibe el usuario.
    /// </summary>
    private long ConsumeEventStamp(IntPtr hwnd, bool hwndChanged)
    {
        if (!_primed) { _primed = true; _eventHwnd = IntPtr.Zero; _eventAtMs = 0; return 0; }

        if (!hwndChanged)
        {
            // Mismo hwnd, otro título (abriste otro documento). Windows NO emite foreground para esto:
            // aquí el reloj es insustituible, o hace falta además EVENT_OBJECT_NAMECHANGE.
            LogBus.Log("locator-lag", "titulo cambiado (mismo hwnd): sin evento de foreground, solo lo ve el reloj");
            return 0;
        }

        if (_eventHwnd == hwnd && _eventAtMs > 0)
        {
            long stamp = _eventAtMs;
            LogBus.Log("locator-lag", $"foreground: el reloj tardo {Environment.TickCount64 - stamp} ms en ver lo que Windows ya sabia");
            _eventHwnd = IntPtr.Zero;
            _eventAtMs = 0;
            return stamp;
        }

        LogBus.Log("locator-lag", "cambio de ventana sin evento previo (evento perdido, o ventana de otro escritorio)");
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
        long eventAtMs = ConsumeEventStamp(hwnd, hwnd != _lastHwnd);
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
                // El total es lo que percibe el usuario: reloj + lectura de identidad (UIA/COM). Es la
                // cifra a comparar contra los 700 ms del refresco del inspector visual.
                if (eventAtMs > 0)
                    LogBus.Log("locator-lag", $"total hasta publicar '{loc.Id}': {Environment.TickCount64 - eventAtMs} ms");
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
