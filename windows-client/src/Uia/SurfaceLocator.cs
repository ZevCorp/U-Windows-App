using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using System.Windows.Threading;
using U.Graph.Surfaces;

namespace U.WindowsClient.Uia;

/// <summary>
/// Dónde está parado el usuario, como URL. Es el "location bar de Windows": un ID jerárquico y
/// legible de la superficie actual, el mismo concepto que la extensión de Chrome usa con la URL
/// (origin + pathname con slashes) para decidir qué workflows aplican.
///
///   - SAP GUI:     <c>sapgui://SID/TCODE/PROGRAMA/DYNPRO</c> — lo produce <see cref="SapGuiSurface"/>,
///                  no este locator: es la MISMA identidad con la que se sellan los pasos al grabar, y
///                  tener dos formas para la misma pantalla rompía la reproducción (ver <c>_sap</c>)
///   - App nativa:  <c>uia://proceso.exe/titulo-de-ventana-normalizado</c>
///   - Navegador:   <c>web://dominio/ruta/subruta</c> (la URL real leída de la barra de direcciones
///                  por UIA, sin query ni fragmento: esos son estado volátil, no ubicación)
///
/// El esquema coincide con el <c>SurfaceIdentity</c> que windows-graph ya sintetiza para grabar
/// workflows (<c>uia://proc/ventana</c>, <c>sapgui://SID/TCODE</c>), así que este ID sirve tal cual
/// como <c>source_url</c>: cargar workflows por superficie y decidir qué exponer por MCP.
///
/// Sondea la ventana en primer plano con un timer barato (hwnd + título vía GetWindowText); solo
/// cuando algo cambió hace el trabajo caro (UIA para la URL del navegador) en un hilo de fondo.
/// </summary>
public sealed class SurfaceLocator : IDisposable
{
    public sealed record SurfaceLocation(string Id, string Origin, string Path);

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
        { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc" };

    private readonly DispatcherTimer _timer;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private IntPtr _lastHwnd;
    private string _lastTitle = "";
    private bool _computing;

    /// <summary>
    /// La superficie SAP, para que la ubicación de SAP la produzca QUIEN SABE de SAP.
    ///
    /// Antes este locator sintetizaba <c>uia://saplogon.exe/&lt;título&gt;</c> para SAP, mientras la grabación
    /// y el reproductor usaban <c>SapGuiSurface.Identity()</c> → <c>sapgui://QAS/NWP1/SAPLN_WP_FRAMEWORK/0100</c>.
    /// Dos nombres para la misma pantalla, y la compuerta del reproductor compara STRINGS: un paso sellado
    /// con la forma <c>uia://</c> no casa nunca con una superficie <c>sapgui://</c> y se queda esperando para
    /// siempre.
    ///
    /// La forma de SAP es además la correcta: el título depende del idioma, del cliente y del texto de la
    /// ventana, y no distingue dos dynpros con el mismo título — el problema que ya atacó el commit 2d024c1.
    /// </summary>
    private readonly SapGuiSurface _sap = new();

    public SurfaceLocation? Current { get; private set; }
    public bool Active { get; private set; }
    public event Action<SurfaceLocation>? Changed;

    public SurfaceLocator()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _timer.Tick += (_, __) => Probe();
    }

    public void Start()
    {
        if (Active) return;
        Active = true;
        _lastHwnd = IntPtr.Zero; // fuerza recomputar ya
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

        string proc = ProcessName(hwnd);
        // Nuestras propias ventanas (la carita, el badge, el inspector) no son "una superficie":
        // conservan el ID de la app real que el usuario estaba usando.
        if (proc.Equals("U", StringComparison.OrdinalIgnoreCase)) return;

        var sb = new StringBuilder(512);
        GetWindowText(hwnd, sb, sb.Capacity);
        string title = sb.ToString();

        // La compuerta barata (mismo hwnd + mismo título ⇒ nada que hacer) NO VALE PARA SAP: dentro de una
        // transacción el dynpro cambia sin que el título se mueva, así que saltarse el tick dejaba la
        // ubicación congelada justo donde más importa. Con SAP delante se recomputa siempre y es la propia
        // identidad de SAP la que decide si hubo cambio (la comparación por Id de más abajo).
        if (!IsSap(proc) && hwnd == _lastHwnd && title == _lastTitle) return;
        if (_computing) return;
        _lastHwnd = hwnd;
        _lastTitle = title;
        _computing = true;

        Task.Run(() =>
        {
            try
            {
                var loc = Compute(hwnd, proc, title);
                _dispatcher.BeginInvoke(new Action(() =>
                {
                    _computing = false;
                    if (loc == null || loc.Id == Current?.Id) return;
                    Current = loc;
                    Changed?.Invoke(loc);
                }));
            }
            catch
            {
                _dispatcher.BeginInvoke(new Action(() => _computing = false));
            }
        });
    }

    /// <summary>
    /// ¿El proceso en primer plano es SAP GUI? Mismo criterio que el resto del cliente
    /// (<c>UiInspector.IsSapForeground</c>, <c>SurfaceDetector</c>): basta el prefijo «sap», que cubre
    /// <c>saplogon</c> y las variantes históricas <c>sapgui</c>/<c>saplgpad</c> sin listar versiones.
    /// </summary>
    private static bool IsSap(string proc) =>
        proc.StartsWith("sap", StringComparison.OrdinalIgnoreCase);

    private SurfaceLocation? Compute(IntPtr hwnd, string proc, string title)
    {
        // SAP responde por sí mismo: sistema + transacción + programa/dynpro, que es la pantalla DE VERDAD
        // y el mismo string que sella la grabación. Si el scripting no está disponible se cae al esquema
        // uia:// de abajo — degradado, pero no inventamos una identidad SAP que nadie más reconocería.
        if (IsSap(proc))
        {
            try
            {
                var id = _sap.Identity();
                if (id.Origin != SurfaceIdentity.Unknown.Origin && id.Url.Length > 0)
                    return new SurfaceLocation(id.Url, id.Origin, id.Pathname);
            }
            catch { }
        }

        if (Browsers.Contains(proc))
        {
            var url = TryReadBrowserUrl(hwnd);
            if (url != null)
            {
                string path = url.AbsolutePath.TrimEnd('/');
                return new SurfaceLocation($"web://{url.Host}{path}", $"web://{url.Host}", path.Length == 0 ? "/" : path);
            }
        }

        string slug = Slug(title);
        return new SurfaceLocation($"uia://{proc}.exe/{slug}", $"uia://{proc}.exe", $"/{slug}");
    }

    /// <summary>
    /// La URL real del navegador: el primer Edit del árbol (la omnibox en Chrome/Edge/Brave) leído
    /// por ValuePattern. Si lo que hay escrito no parsea como URL (una búsqueda a medias), se ignora.
    /// </summary>
    private static Uri? TryReadBrowserUrl(IntPtr hwnd)
    {
        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            var edit = root?.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            if (edit == null) return null;
            if (!edit.TryGetCurrentPattern(ValuePattern.Pattern, out var p) || p is not ValuePattern vp) return null;

            string raw = (vp.Current.Value ?? "").Trim();
            if (raw.Length == 0 || raw.Contains(' ')) return null;
            if (!raw.Contains("://")) raw = "https://" + raw;
            return Uri.TryCreate(raw, UriKind.Absolute, out var uri) && uri.Host.Contains('.') ? uri : null;
        }
        catch { return null; }
    }

    /// <summary>Título de ventana → segmento de ruta estable y legible (minúsculas, guiones).</summary>
    private static string Slug(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "ventana";
        var sb = new StringBuilder(title.Length);
        bool dash = false;
        foreach (char c in title.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) { sb.Append(c); dash = false; }
            else if (!dash && sb.Length > 0) { sb.Append('-'); dash = true; }
            if (sb.Length >= 60) break;
        }
        string slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? "ventana" : slug;
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

    public void Dispose() => Stop();
}
