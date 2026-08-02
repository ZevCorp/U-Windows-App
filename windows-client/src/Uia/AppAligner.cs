using System.Diagnostics;
using System.Runtime.InteropServices;
using U.WindowsClient.Diagnostics;
using U.WindowsClient.SystemApi;

namespace U.WindowsClient.Uia;

/// <summary>
/// Alineación CONSCIENTE de superficie: lleva el foco a la app donde nació un workflow antes de
/// ejecutarlo por el sistema subconsciente. Es el primer eslabón del loop consciente→subconsciente:
/// si al ejecutar no estamos en la superficie del workflow, el sistema se alinea (v1: enfocar la app
/// abierta o lanzarla) y confirma con el SurfaceLocator que ya llegó.
///
/// v1 solo cubre superficies UIA (apps de escritorio, origin uia://proceso.exe). SAP u otras no se
/// abren automáticamente: se devuelve false y el player reporta el mismatch como antes.
/// </summary>
public static class AppAligner
{
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    private const int SW_RESTORE = 9;

    /// <summary>uia://notepad.exe(/loquesea) → "notepad".</summary>
    public static string ProcessFromOrigin(string origin)
    {
        string s = (origin ?? "").Trim();
        int scheme = s.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) s = s[(scheme + 3)..];
        s = s.Split('/')[0];
        if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s[..^4];
        return s.Trim();
    }

    /// <summary>
    /// Enfoca/abre la app del <paramref name="targetOrigin"/> y espera a que <paramref name="currentOrigin"/>
    /// confirme que ya estamos ahí. Idempotente: si ya estamos en el origin, retorna true sin tocar nada.
    /// Firma compatible con el delegate <c>U.Graph.SurfaceAligner</c>.
    /// </summary>
    public static async Task<bool> EnsureAsync(string targetOrigin, Func<string> currentOrigin, CancellationToken ct)
    {
        string current = currentOrigin();
        LogBus.Log("align", $"EnsureAsync target='{targetOrigin}' actual='{current}'");
        if (Matches(current, targetOrigin)) { LogBus.Log("align", "ya alineado (no toco nada)"); return true; }
        if (!targetOrigin.StartsWith("uia://", StringComparison.OrdinalIgnoreCase))
        {
            LogBus.Log("align", $"origin no es uia:// — no sé abrir esta superficie ({targetOrigin})");
            return false;
        }

        string proc = ProcessFromOrigin(targetOrigin);
        if (string.IsNullOrWhiteSpace(proc)) { LogBus.Log("align", "no pude derivar el proceso del origin"); return false; }

        bool focused = FocusOrLaunch(proc);
        LogBus.Log("align", $"proceso='{proc}' · FocusOrLaunch={focused}");
        if (!focused) return false;

        // Esperar a que el foco realmente cambie (lanzar una app tarda; enfocar es rápido).
        for (int i = 0; i < 24 && !ct.IsCancellationRequested; i++)
        {
            await Task.Delay(250, ct);
            if (Matches(currentOrigin(), targetOrigin))
            {
                LogBus.Log("align", $"confirmado tras ~{(i + 1) * 250} ms");
                return true;
            }
        }
        LogBus.Log("align", $"NO confirmó tras 6s · actual='{currentOrigin()}' (esperaba '{targetOrigin}')");
        return Matches(currentOrigin(), targetOrigin);
    }

    private static bool Matches(string? current, string target) =>
        !string.IsNullOrWhiteSpace(current) &&
        string.Equals(current.TrimEnd('/'), target.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Enfoca la app si ya está abierta; si no, la lanza. También lo usa launch_app (LocalMcp).
    ///
    /// EL ESCRITORIO NO ES UNA APP, y tratarlo como tal tuvo consecuencias visibles: un workflow
    /// sellado en <c>uia://desktop</c> hizo que esto intentara «lanzar un programa llamado desktop»
    /// y el shell resolvió… Docker Desktop (2026-07-31). Abrir un programa al azar en la máquina de
    /// alguien es de las cosas más molestas que puede hacer un agente, y encima la alineación
    /// fallaba igual. El escritorio se alcanza con su gesto —Win+D—, que es lo que ya sabe hacer
    /// <see cref="Gestures.ShowDesktop"/>.
    /// </summary>
    public static bool FocusOrLaunch(string proc)
    {
        if (EsEscritorio(proc)) return Actions.Gestures.ShowDesktop();

        var open = Process.GetProcessesByName(proc).FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);
        if (open != null)
        {
            // Restaurar SOLO si está minimizada: SW_RESTORE sobre una ventana maximizada la encoge,
            // y enfocar una app no debería cambiarle el tamaño a nadie (2026-08-01).
            if (IsIconic(open.MainWindowHandle)) ShowWindow(open.MainWindowHandle, SW_RESTORE);
            return SetForegroundWindow(open.MainWindowHandle);
        }
        return WindowsSystemApi.LaunchApp(proc);
    }

    /// <summary>El escritorio, en las formas en que lo nombran el locator y los workflows.</summary>
    private static bool EsEscritorio(string proc) =>
        proc.Equals("desktop", StringComparison.OrdinalIgnoreCase)
        || proc.Equals("escritorio", StringComparison.OrdinalIgnoreCase)
        || proc.Equals("program-manager", StringComparison.OrdinalIgnoreCase)
        || proc.Equals("progman", StringComparison.OrdinalIgnoreCase);
}
