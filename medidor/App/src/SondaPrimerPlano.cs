using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Medidor.App;

/// <summary>Lo que la sonda vio, ya podado: el proceso (para normalizar a app) y, si es un
/// navegador, la URL. El título se lee para NADA salvo, quizá, sacar la URL de la barra — y no
/// sale de aquí. Es el patrón de SurfaceLocator, pero entregando aún menos.</summary>
public sealed record VistaDePrimerPlano(string Proceso, string? UrlNavegador);

/// <summary>
/// El sondeo barato de «¿qué app está delante?»: GetForegroundWindow → ventana raíz → nombre de
/// proceso, a 800 ms (la cadencia de SurfaceLocator, verificada sobre el SAP real). Salta el tick
/// si nada cambió. NO resuelve la identidad SAP — de eso se encarga el hilo STA, porque el COM de
/// SAP no se puede tocar desde aquí.
///
/// La URL del navegador es el único dato «rico» que toca, y solo para clasificar el dominio contra
/// la lista blanca; el título entero jamás se guarda ni se emite (promesa 1).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SondaPrimerPlano
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr hwnd, StringBuilder text, int max);
    private const uint GA_ROOT = 2;

    private static readonly HashSet<string> Navegadores = new(StringComparer.OrdinalIgnoreCase)
    { "chrome", "msedge", "firefox", "brave", "opera" };

    private IntPtr _ultimoHwnd;

    public VistaDePrimerPlano? Mirar()
    {
        var hwnd = GetAncestor(GetForegroundWindow(), GA_ROOT);
        if (hwnd == IntPtr.Zero) return null;
        _ultimoHwnd = hwnd;

        GetWindowThreadProcessId(hwnd, out var pid);
        string proceso;
        try { proceso = Process.GetProcessById((int)pid).ProcessName; }
        catch { return null; } // el proceso murió entre el hwnd y el lookup: el próximo tick lo recoge

        var procesoExe = proceso.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? proceso : proceso + ".exe";

        string? url = null;
        if (Navegadores.Contains(proceso))
            url = UrlDesdeTitulo(hwnd); // aproximación honesta: el título del navegador suele ser "Página - Dominio"

        return new VistaDePrimerPlano(procesoExe, url);
    }

    /// <summary>Los navegadores no exponen su URL por Win32 sin UIA; el título es lo que hay. Se
    /// intenta sacar un host de él y NADA MÁS — el título completo no se conserva. Si no hay host
    /// reconocible, se devuelve null y la web queda sin dominio (cae a «navegador a secas»).</summary>
    private static string? UrlDesdeTitulo(IntPtr hwnd)
    {
        var sb = new StringBuilder(512);
        if (GetWindowTextW(hwnd, sb, sb.Capacity) == 0) return null;
        var titulo = sb.ToString();

        // Un dominio dentro del título: "algo — miracle.itsmiracleai.com.co" o similar. Se busca el
        // primer token con forma de host; se descarta todo lo demás (que puede ser el nombre del
        // paciente). Es deliberadamente conservador: mejor sin dominio que con un título filtrado.
        foreach (var token in titulo.Split(new[] { ' ', '\t', '—', '-', '|', '·', '(', ')' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var t = token.Trim().TrimEnd('/');
            if (t.Contains('.') && !t.Contains(' ') && Uri.CheckHostName(t) != UriHostNameType.Unknown)
                return "https://" + t;
        }
        return null;
    }
}
