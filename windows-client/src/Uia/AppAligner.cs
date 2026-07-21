using System.Diagnostics;
using System.Runtime.InteropServices;
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
        if (Matches(currentOrigin(), targetOrigin)) return true;
        if (!targetOrigin.StartsWith("uia://", StringComparison.OrdinalIgnoreCase)) return false;

        string proc = ProcessFromOrigin(targetOrigin);
        if (string.IsNullOrWhiteSpace(proc)) return false;

        if (!FocusOrLaunch(proc)) return false;

        // Esperar a que el foco realmente cambie (lanzar una app tarda; enfocar es rápido).
        for (int i = 0; i < 24 && !ct.IsCancellationRequested; i++)
        {
            await Task.Delay(250, ct);
            if (Matches(currentOrigin(), targetOrigin)) return true;
        }
        return Matches(currentOrigin(), targetOrigin);
    }

    private static bool Matches(string? current, string target) =>
        !string.IsNullOrWhiteSpace(current) &&
        string.Equals(current.TrimEnd('/'), target.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    private static bool FocusOrLaunch(string proc)
    {
        var open = Process.GetProcessesByName(proc).FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);
        if (open != null)
        {
            ShowWindow(open.MainWindowHandle, SW_RESTORE);
            return SetForegroundWindow(open.MainWindowHandle);
        }
        return WindowsSystemApi.LaunchApp(proc);
    }
}
