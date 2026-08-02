using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace U.WindowsClient.SystemApi;

/// <summary>
/// Acciones de sistema por API/protocolo de Windows: el equivalente a los "Common Intents" de Android
/// (<c>SystemApi</c> del core). Abren apps, URLs, correo, ajustes… sin navegar la interfaz. El cerebro
/// las prefiere sobre computer-use para tareas de sistema; aquí solo se EJECUTAN.
/// </summary>
public static class WindowsSystemApi
{
    [DllImport("user32.dll")] private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    private const byte VK_VOLUME_MUTE = 0xAD, VK_VOLUME_DOWN = 0xAE, VK_VOLUME_UP = 0xAF;

    /// <summary>
    /// Nombres visibles que el shell NO resuelve como comando, mapeados a su proceso real. Son las
    /// piezas del propio Windows: el usuario (y el modelo) las llaman por su nombre de pantalla y
    /// en español, y ningún acceso directo del menú Inicio las encuentra.
    /// </summary>
    private static readonly Dictionary<string, string> AliasDeApp = new(StringComparer.OrdinalIgnoreCase)
    {
        ["explorador de archivos"] = "explorer", ["explorador"] = "explorer",
        ["file explorer"] = "explorer", ["explorer"] = "explorer", ["archivos"] = "explorer",
        ["papelera"] = "explorer", ["esta pc"] = "explorer", ["este equipo"] = "explorer",
        ["bloc de notas"] = "notepad", ["notepad"] = "notepad",
        ["calculadora"] = "calc", ["símbolo del sistema"] = "cmd", ["simbolo del sistema"] = "cmd",
    };

    /// <summary>
    /// Abre una app y CONFIRMA que se abrió. Devolver «ok» sin comprobarlo fue un fallo real,
    /// medido el 2026-07-31: el cerebro pidió «Explorador de archivos», esto respondió ok, y lo que
    /// había delante era una ventana de <c>cmd.exe</c> titulada «explorador de archivos» — la dejó
    /// el fallback <c>cmd /c start</c>, y <see cref="Shell"/> la dio por buena porque
    /// <c>Process.Start</c> no lanzó excepción. Pero arrancar un proceso no es abrir una app.
    ///
    /// Ahora se resuelve el nombre visible a su proceso, se lanza, y se ESPERA a que ese proceso
    /// tenga ventana. Si no aparece, se devuelve false y el modelo puede reaccionar en vez de
    /// seguir creyendo que llegó. Es la misma regla que gobierna el resto del sistema: aceptado no
    /// es ejecutado.
    /// </summary>
    public static bool LaunchApp(string app)
    {
        if (string.IsNullOrWhiteSpace(app)) return false;
        string pedido = app.Trim();
        string esperado = AliasDeApp.TryGetValue(pedido, out var alias)
            ? alias
            : pedido.Replace(".exe", "", StringComparison.OrdinalIgnoreCase).Trim();

        // Ya abierta: traerla al frente es más fiable que relanzar (y no abre una segunda copia).
        if (TieneVentana(esperado)) { Shell(esperado); return true; }

        // 1) Acceso directo del menú Inicio: resuelve NOMBRES VISIBLES ("Google Chrome" → chrome.exe)
        //    que el shell no encuentra como comando. Es lo que el cerebro suele pasar a launch_app.
        bool lanzado = StartMenuLauncher.TryLaunch(pedido) || Shell(esperado) || Shell(pedido);

        // NO se cae a `cmd /c start`: ese fallback es el que dejaba consolas abiertas haciéndose
        // pasar por la app. Si nada de lo anterior sirvió, es más honesto decir que no se pudo.
        if (!lanzado) return false;

        return EsperarVentana(esperado, 12000); // arranque en frío de Chrome/Teams pasa de 6 s
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    private static bool TieneVentana(string proc)
    {
        try
        {
            return Process.GetProcessesByName(proc).Any(p => p.MainWindowHandle != IntPtr.Zero);
        }
        catch { return false; }
    }

    /// <summary>¿La ventana de delante es de esta app? Es la señal más rápida de que ya se abrió.</summary>
    private static bool EstaDelante(string proc)
    {
        try
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName.Equals(proc, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>
    /// Espera a que la app esté realmente abierta. Arrancar tarda; la paciencia va aquí, no en el
    /// modelo.
    ///
    /// DOS señales, porque una sola falla: <c>MainWindowHandle</c> tarda en poblarse en apps
    /// multiproceso —Chrome abrió y esto reportó fallo a los 6 s (2026-07-31), así que el modelo
    /// declaró éxito por su cuenta mientras la herramienta decía lo contrario—. La ventana en
    /// primer plano lo delata mucho antes, porque una app recién lanzada se pone delante.
    ///
    /// Convertir una mentira optimista en una pesimista no era el objetivo: lo que hace útil a esta
    /// función es acertar, y para eso 6 s no bastan en un arranque en frío.
    /// </summary>
    private static bool EsperarVentana(string proc, int msMax)
    {
        var hasta = DateTime.UtcNow.AddMilliseconds(msMax);
        while (DateTime.UtcNow < hasta)
        {
            if (EstaDelante(proc) || TieneVentana(proc)) return true;
            System.Threading.Thread.Sleep(200);
        }
        // Hay apps cuyo proceso no se llama como su nombre visible. En esos casos no podemos
        // AFIRMAR que se abrió — y afirmarlo sin más es justo el error que esto vino a corregir.
        return false;
    }

    public static bool OpenUrl(string url) => Shell(NormalizeUrl(url));
    public static bool WebSearch(string query) => Shell($"https://www.google.com/search?q={Uri.EscapeDataString(query)}");
    public static bool OpenMaps(string q) => Shell($"https://www.google.com/maps/search/{Uri.EscapeDataString(q)}");
    public static bool Directions(string dest) => Shell($"https://www.google.com/maps/dir/?api=1&destination={Uri.EscapeDataString(dest)}");

    public static bool SendEmail(string to, string subject, string body) =>
        Shell($"mailto:{Uri.EscapeDataString(to)}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}");

    public static bool Dial(string number) => Shell($"tel:{Uri.EscapeDataString(number)}");
    public static bool SendSms(string number, string message) => Shell($"sms:{Uri.EscapeDataString(number)}?body={Uri.EscapeDataString(message)}");
    public static bool OpenCamera() => Shell("microsoft.windows.camera:");

    public static bool OpenSettings(string section)
    {
        string page = section switch
        {
            "wifi" => "network-wifi",
            "bluetooth" => "bluetooth",
            "network" => "network",
            "display" => "display",
            "sound" => "sound",
            "battery" => "batterysaver",
            "privacy" => "privacy",
            "apps" => "appsfeatures",
            _ => "",
        };
        return Shell($"ms-settings:{page}");
    }

    public static bool SetClipboard(string text)
    {
        try { Clipboard.SetText(text ?? ""); return true; } catch { return false; }
    }

    public static bool ShareText(string text)
    {
        // Windows no tiene un Intent de compartir universal desde consola; copiamos al portapapeles como
        // fallback razonable y honesto (el cerebro puede seguir con computer-use si necesita el diálogo).
        return SetClipboard(text);
    }

    public static bool AdjustVolume(string direction)
    {
        byte vk = direction switch
        {
            "raise" => VK_VOLUME_UP,
            "lower" => VK_VOLUME_DOWN,
            "mute" or "unmute" => VK_VOLUME_MUTE,
            _ => 0,
        };
        if (vk == 0) return false;
        keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, 2 /*KEYUP*/, UIntPtr.Zero);
        return true;
    }

    /// <summary>Ajuste aproximado por pasos de tecla (control fino requiere CoreAudio; fase siguiente).</summary>
    public static bool SetVolume(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        // Baja al mínimo y sube ~ el porcentaje pedido (cada pulsación ≈ 2%).
        for (int i = 0; i < 50; i++) AdjustVolume("lower");
        int steps = percent / 2;
        for (int i = 0; i < steps; i++) AdjustVolume("raise");
        return true;
    }

    public static bool SetAlarm(int hour, int minute, string message) =>
        // La app Reloj no expone protocolo de alarma directa; abrirla es el fallback honesto.
        Shell("ms-clock:");

    public static bool SetTimer(int seconds, string message) => Shell("ms-clock:");
    public static bool CreateEvent(string title, string startIso, string location) => Shell("outlookcal:");

    private static string NormalizeUrl(string url) =>
        url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : $"https://{url}";

    private static bool Shell(string file, string? args = null)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = file, Arguments = args ?? "", UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }
}
