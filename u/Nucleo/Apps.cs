using System.Diagnostics;
using System.Runtime.InteropServices;

namespace U.Ciclo;

/// <summary>
/// ABRIR UNA APP y esperar a que esté delante, sin más. En main, map_open_app tardó 560-13.995 ms y dos de
/// cinco veces no trajo la app al frente (2026-09-24). Aquí: ShellExecute, y se mira la ventana de delante
/// cada 15 ms hasta que cambie, con techo de 3 s.
/// </summary>
public static class Apps
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

    /// <summary>Los nombres con los que se pide, a lo que Windows sabe abrir.</summary>
    public static string Comando(string nombre) => (nombre ?? "").Trim().ToLowerInvariant() switch
    {
        "bloc de notas" or "notepad" or "bloc" => "notepad.exe",
        "configuración" or "configuracion" or "ajustes" or "settings" => "ms-settings:",
        "explorador" or "explorador de archivos" or "archivos" or "explorer" => "explorer.exe",
        "calculadora" or "calc" => "calc.exe",
        "navegador" or "chrome" => "chrome.exe",
        "edge" => "msedge.exe",
        "paint" => "mspaint.exe",
        var otro => otro,
    };

    /// <summary>Abre y espera a que cambie la ventana de delante. Devuelve (llegó, ms).</summary>
    public static (bool Llego, long Ms) Abrir(string nombre, int techoMs = 3000)
    {
        var antes = GetForegroundWindow();
        var r = Stopwatch.StartNew();
        // Un nombre que Windows no sabe abrir es «no llegó», no una excepción que tumbe el plan entero
        // (Luna pidió «abre: comando de Windows» el 2026-09-24, 23:29, y el Win32Exception subió hasta arriba).
        try { Process.Start(new ProcessStartInfo(Comando(nombre)) { UseShellExecute = true })?.Dispose(); }
        catch (System.ComponentModel.Win32Exception) { return (false, r.ElapsedMilliseconds); }
        while (r.ElapsedMilliseconds < techoMs)
        {
            var ahora = GetForegroundWindow();
            if (ahora != IntPtr.Zero && ahora != antes) return (true, r.ElapsedMilliseconds);
            Thread.Sleep(15);
        }
        return (false, r.ElapsedMilliseconds);
    }
}
