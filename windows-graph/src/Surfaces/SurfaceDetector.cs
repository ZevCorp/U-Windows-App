using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace U.Graph.Surfaces;

/// <summary>
/// Elige UIA o SAP GUI según la ventana en primer plano cuando arranca una enseñanza: el operador ya
/// cambió a la app que va a enseñar (ver el countdown en WorkflowTeachSession), así que el proceso en
/// foreground ES la superficie correcta.
///
/// LECCIÓN APRENDIDA (workflow wf_1785096110817): una enseñanza sobre SAP quedó grabada por UIA — 7
/// pasos idénticos "clic en el panel" — y nadie pudo saber por qué, porque la decisión del detector
/// era invisible. Por eso ahora (1) la detección es por PREFIJO "sap" (igual que el resto del
/// cliente: UiInspector/AgentLoop), no por lista exacta; (2) si el nombre del proceso es ilegible
/// (proceso elevado → acceso denegado), se decide por el TÍTULO de la ventana; y (3)
/// <see cref="Detect(IUiSurface, IUiSurface, out string)"/> siempre cuenta el porqué, para que el
/// llamador lo deje en el registro.
/// </summary>
public static class SurfaceDetector
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    public static IUiSurface Detect(IUiSurface uia, IUiSurface sap) => Detect(uia, sap, out _);

    /// <summary>Detecta la superficie y explica la decisión (para el registro de la enseñanza).</summary>
    public static IUiSurface Detect(IUiSurface uia, IUiSurface sap, out string reason)
    {
        string proc = ForegroundProcessName();

        // El front-end de SAP GUI for Windows corre bajo saplogon.exe (y variantes históricas
        // sapgui/saplgpad). El prefijo cubre todas sin listar versiones — mismo criterio que
        // UiInspector.IsSapForeground.
        if (proc.StartsWith("sap", StringComparison.OrdinalIgnoreCase))
        {
            reason = $"proceso en primer plano «{proc}»";
            return sap;
        }

        if (proc.Length == 0)
        {
            // Nombre ilegible (típico: proceso elevado y nosotros no). El título de la ventana sí se
            // puede leer siempre; el de una sesión SAP menciona SAP o la transacción del sistema.
            string title = ForegroundWindowTitle();
            if (title.IndexOf("SAP", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                reason = $"proceso ilegible, pero el título de la ventana es «{title}»";
                return sap;
            }
            reason = $"proceso ilegible y el título «{title}» no menciona SAP";
            return uia;
        }

        reason = $"proceso en primer plano «{proc}» (no es SAP)";
        return uia;
    }

    private static string ForegroundProcessName()
    {
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return "";
            GetWindowThreadProcessId(hwnd, out uint pid);
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch { return ""; }
    }

    private static string ForegroundWindowTitle()
    {
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return "";
            var sb = new StringBuilder(256);
            GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString().Trim();
        }
        catch { return ""; }
    }
}
