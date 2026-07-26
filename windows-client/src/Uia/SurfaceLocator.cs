using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using U.Graph.Surfaces;

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

    /// <summary>
    /// Cada cuánto se mira si cambió la superficie. La sonda es barata (hwnd + título + nombre de
    /// proceso); lo caro —leer la identidad por UIA/COM— solo corre cuando algo cambió de verdad.
    ///
    /// 200 ms y no 800: se midió el retraso real contra los eventos de ventana de Windows y salía de
    /// 15 a 734 ms (media ~348), suficiente para que el inspector visual dibujara los recuadros de la
    /// pantalla anterior sobre la nueva. A 200 ms el peor caso queda por debajo del umbral en que eso
    /// se percibe.
    ///
    /// Se intentó sustituir el sondeo por EVENT_SYSTEM_FOREGROUND/EVENT_OBJECT_NAMECHANGE y salió 4x
    /// PEOR (media ~1,4 s): las ráfagas de eventos chocaban contra la lectura en curso y los cambios se
    /// perdían. La causa no era el sondeo sino el descarte que arregla <see cref="_missedWhileComputing"/>.
    /// Si algún día se vuelve a intentar, que sea DESPUÉS de este arreglo y midiendo igual.
    /// </summary>
    private const int ProbeIntervalMs = 200;

    private readonly DispatcherTimer _timer;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly Func<SurfaceIdentity> _identity;
    private readonly IDisposable[] _owned;
    private IntPtr _lastHwnd;
    private string _lastTitle = "";
    private bool _computing;
    /// <summary>Llegó un cambio mientras se leía la identidad: hay que volver a sondear al terminar.</summary>
    private bool _missedWhileComputing;

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

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ProbeIntervalMs) };
        _timer.Tick += (_, __) => Probe();
    }

    /// <summary>Productor explícito (tests): esta clase solo lo llama, nunca calcula.</summary>
    public SurfaceLocator(Func<SurfaceIdentity> identity)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _owned = Array.Empty<IDisposable>();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ProbeIntervalMs) };
        _timer.Tick += (_, __) => Probe();
    }

    public void Start()
    {
        if (Active) return;
        Active = true;
        _lastHwnd = IntPtr.Zero; // fuerza recomputar ya
        _missedWhileComputing = false;
        _timer.Start();
        Probe();
    }

    public void Stop()
    {
        Active = false;
        _timer.Stop();
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

        // Hay un cambio, pero se está leyendo la identidad del ANTERIOR. Antes esto hacía `return` a
        // secas: el cambio se perdía —no se apuntaba, no se reintentaba— y solo lo rescataba la
        // siguiente sonda que encontrara el hueco libre. Con lecturas lentas (SAP por COM) o sondas
        // frecuentes, un cambio podía quedar sin publicar durante segundos, dejando la ubicación
        // congelada en una ventana que el usuario ya había cerrado. Ahora se anota y se reintenta en
        // cuanto la lectura en curso termina.
        if (_computing) { _missedWhileComputing = true; return; }

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

                if (loc != null && loc.Id != Current?.Id)
                {
                    Current = loc;
                    Changed?.Invoke(loc);
                }

                // Mientras se leía llegó otro cambio: atenderlo YA, sin esperar al siguiente tick.
                // Va al final para que los suscriptores vean primero la ubicación que sí se resolvió.
                if (_missedWhileComputing)
                {
                    _missedWhileComputing = false;
                    Probe();
                }
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
