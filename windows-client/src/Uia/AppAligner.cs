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
        if (Escritorio.EsProceso(proc)) return Escritorio.Mostrar();

        var open = Process.GetProcessesByName(proc).FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);
        if (open != null)
        {
            return TraerAlFrente(open.MainWindowHandle);
        }
        return WindowsSystemApi.LaunchApp(proc);
    }

    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    /// <summary>
    /// Traer una ventana al frente DE VERDAD, y comprobarlo. La única forma de hacerlo en la app.
    /// </summary>
    /// <remarks>
    /// Windows no deja que un proceso que no está delante le robe el primer plano a otro: la llamada
    /// devuelve éxito y lo único que hace es parpadear su botón en la barra de tareas. La salida
    /// documentada es engancharse a la cola de entrada del hilo que SÍ está delante, y desengancharse
    /// enseguida —compartir cola con otra app más de lo necesario es pedir un bloqueo—.
    ///
    /// Vive AQUÍ y no en quien la necesita porque «traer al frente» se contestaba en cuatro sitios y
    /// solo uno tenía este arreglo: el más nuevo. Los otros tres seguían con la versión que falla en
    /// silencio, y nadie lo habría notado hasta toparse con el caso (2026-08-04). Una pregunta con
    /// varias respuestas no se mantiene: se desincroniza.
    ///
    /// Y se verifica mirando quién está delante DESPUÉS, no lo que devolvió la llamada: aceptado no
    /// es ejecutado.
    /// </remarks>
    public static bool TraerAlFrente(IntPtr h)
    {
        if (h == IntPtr.Zero) return false;
        try
        {
            // Restaurar SOLO si está minimizada: SW_RESTORE sobre una ventana maximizada la encoge,
            // y enfocar una app no debería cambiarle el tamaño a nadie (2026-08-01).
            if (IsIconic(h)) ShowWindow(h, SW_RESTORE);

            uint mio = GetCurrentThreadId();
            uint suyo = GetWindowThreadProcessId(GetForegroundWindow(), out _);
            bool enganchado = mio != suyo && AttachThreadInput(mio, suyo, true);
            try { SetForegroundWindow(h); BringWindowToTop(h); }
            finally { if (enganchado) AttachThreadInput(mio, suyo, false); }

            for (int i = 0; i < 12; i++)
            {
                if (GetForegroundWindow() == h) return true;
                System.Threading.Thread.Sleep(40);
            }
            return false;
        }
        catch { return false; }
    }

    // Qué es el escritorio y cómo se llega lo sabe Escritorio, para toda la app.
}
