using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using System.Windows.Threading;

namespace U.WindowsClient.Uia;

/// <summary>
/// Dónde está parado el usuario, como URL. Es el "location bar de Windows": un ID jerárquico y
/// legible de la superficie actual, el mismo concepto que la extensión de Chrome usa con la URL
/// (origin + pathname con slashes) para decidir qué workflows aplican.
///
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

        if (hwnd == _lastHwnd && title == _lastTitle) return;
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

    private static SurfaceLocation? Compute(IntPtr hwnd, string proc, string title)
    {
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
