using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using U.WindowsClient.Diagnostics;
using U.WindowsClient.SystemApi;

namespace U.WindowsClient.Navigation;

/// <summary>
/// Rutas de intento v1 de <see cref="SurfaceNavigator"/> (capa 1: llegar a la APP correcta). Todas
/// operan solo sobre superficies <c>uia://</c>: SAP/web devuelven <see cref="INavStrategy.CanAttempt"/>
/// false y la escalera se agota (mismo comportamiento que antes para esos orígenes).
/// </summary>

// ── Rung 0: el escritorio (mostrar escritorio) ──────────────────────────────────────────────────
/// <summary>
/// El escritorio de Windows es una superficie propia (<c>uia://desktop</c>), no una app que se lance.
/// Se alcanza con "mostrar escritorio" (Win+D): minimiza todo y trae el escritorio al frente. Es clave
/// separarlo del Explorador de archivos —ambos son explorer.exe—: por eso NUNCA se lanza 'explorer'
/// para esto (abriría una ventana del Explorador, no el escritorio). Ver UiaSurface.DesktopOrigin.
/// </summary>
public sealed class ShowDesktopStrategy : INavStrategy
{
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
    private const byte VK_LWIN = 0x5B, VK_D = 0x44;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    public string Name => "mostrar-escritorio";
    public TimeSpan ConfirmWindow => TimeSpan.FromSeconds(2);
    public bool CanAttempt(NavTarget t) => t.Process.Equals("desktop", StringComparison.OrdinalIgnoreCase);

    public Task<bool> AttemptAsync(NavTarget t, CancellationToken ct)
    {
        keybd_event(VK_LWIN, 0, 0, IntPtr.Zero);
        keybd_event(VK_D, 0, 0, IntPtr.Zero);
        keybd_event(VK_D, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
        keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
        LogBus.Log("nav", "[mostrar-escritorio] Win+D");
        return Task.FromResult(true);
    }
}

// ── Rung 1: enfocar la instancia ya abierta ─────────────────────────────────────────────────────
/// <summary>
/// Si el proceso ya está vivo y tiene ventana principal, lo trae al frente. Es el caso más común y el
/// más barato (instantáneo): la app está abierta pero detrás de otra. Si NO está viva, devuelve false
/// para que una ruta de lanzamiento tome el relevo.
/// </summary>
public sealed class FocusRunningStrategy : INavStrategy
{
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_RESTORE = 9;

    public string Name => "enfocar-app-viva";
    public TimeSpan ConfirmWindow => TimeSpan.FromSeconds(2); // enfocar es inmediato
    public bool CanAttempt(NavTarget t) => t.IsUia && !string.IsNullOrWhiteSpace(t.Process);

    public Task<bool> AttemptAsync(NavTarget t, CancellationToken ct)
    {
        Process? open = Process.GetProcessesByName(t.Process).FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);
        if (open == null) return Task.FromResult(false); // no está viva → que la lance otra ruta
        ShowWindow(open.MainWindowHandle, SW_RESTORE); // por si estaba minimizada
        return Task.FromResult(SetForegroundWindow(open.MainWindowHandle));
    }
}

// ── Rung 2: lanzar por acceso directo del menú Inicio ───────────────────────────────────────────
/// <summary>
/// La ruta que arregla la fuga de resiliencia #1: apps cuyo nombre de proceso NO es un comando en el
/// PATH (SAP Logon = <c>saplogon</c>, muchas apps de negocio/Electron) no se pueden lanzar con
/// <c>Process.Start("saplogon")</c>. Pero casi todas dejan un acceso directo en el menú Inicio, y
/// lanzar el <c>.lnk</c> sí abre la app. Se busca por NOMBRE del acceso directo normalizado
/// ("SAP Logon" ~ saplogon, "Google Chrome" ~ chrome), sin COM.
///
/// Rung siguiente (aún no): resolver el DESTINO del .lnk vía IShellLink para casar por exe cuando el
/// nombre del acceso directo no se parece al del proceso.
/// </summary>
public sealed class StartMenuLaunchStrategy : INavStrategy
{
    public string Name => "acceso-directo-inicio";
    public TimeSpan ConfirmWindow => TimeSpan.FromSeconds(10); // lanzar en frío tarda
    public bool CanAttempt(NavTarget t) => t.IsUia && !string.IsNullOrWhiteSpace(t.Process);

    public Task<bool> AttemptAsync(NavTarget t, CancellationToken ct)
    {
        // Misma lógica que usa launch_app: un solo lugar (StartMenuLauncher).
        string? lnk = StartMenuLauncher.FindShortcut(t.Process);
        if (lnk == null) { LogBus.Log("nav", $"[{Name}] sin acceso directo para '{t.Process}'"); return Task.FromResult(false); }
        try
        {
            Process.Start(new ProcessStartInfo { FileName = lnk, UseShellExecute = true });
            LogBus.Log("nav", $"[{Name}] lanzando '{Path.GetFileName(lnk)}'");
            return Task.FromResult(true);
        }
        catch (Exception ex) { LogBus.Log("nav", $"[{Name}] no pude lanzar '{lnk}': {ex.Message}"); return Task.FromResult(false); }
    }
}

// ── Rung 3: último recurso, dejar que el shell resuelva el nombre ────────────────────────────────
/// <summary>
/// Fallback histórico (el comportamiento de <c>AppAligner</c>): <c>Process.Start(proceso)</c> y, si
/// falla, <c>cmd /c start</c>. Solo funciona si el nombre del proceso es un comando/alias en el PATH
/// (notepad, calc, msedge…), pero cubre esos casos y no cuesta nada intentarlo al final. La verdad de
/// si abrió la pone la confirmación del navegador, no el "issued" de aquí.
/// </summary>
public sealed class ShellLaunchStrategy : INavStrategy
{
    public string Name => "shell";
    public TimeSpan ConfirmWindow => TimeSpan.FromSeconds(8);
    public bool CanAttempt(NavTarget t) => t.IsUia && !string.IsNullOrWhiteSpace(t.Process);

    public Task<bool> AttemptAsync(NavTarget t, CancellationToken ct)
    {
        bool issued = WindowsSystemApi.LaunchApp(t.Process);
        LogBus.Log("nav", $"[{Name}] LaunchApp('{t.Process}')={issued}");
        return Task.FromResult(issued);
    }
}
