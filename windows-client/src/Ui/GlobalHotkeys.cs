using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Ui;

/// <summary>
/// Atajos de teclado que funcionan con Ü en segundo plano, para poder llamarlo sin soltar SAP.
///
/// Es el primer sitio del cliente que procesa mensajes de ventana: no había ningún
/// <c>HwndSource.AddHook</c> en el repo. El ciclo de vida está calcado del de los hooks globales que
/// ya existen (<c>ClickWatcher</c>, <c>UiInspector</c>): guarda por handle, todo se libera en
/// <see cref="Dispose"/>, y un fallo se REGISTRA en vez de lanzarse — que no haya atajo no puede
/// impedir que la aplicación arranque.
///
/// ── AVISO SOBRE LA COMBINACIÓN, que no es un detalle de estilo ──────────────────────────────────
/// El teclado de la máquina del hospital es español-Latinoamérica, y ahí <b>AltGr ES Ctrl+Alt</b>.
/// Registrar <c>Ctrl+Alt+letra</c> secuestra <c>AltGr+esa letra</c> en TODAS las aplicaciones de la
/// máquina, no solo en Ü.
///
/// <c>U</c> y <c>M</c> son seguras: en es-LA no producen ningún carácter con AltGr. Lo que NO se
/// puede hacer es ofrecer una alternativa con otra letra en <c>Ctrl+Alt</c> — <c>Q</c>, <c>E</c>,
/// <c>2</c> y <c>4</c> son <c>@</c>, <c>€</c>, <c>"</c> y <c>~</c>, y dejaríamos al operador sin poder
/// escribir un correo. Por eso el respaldo es <c>Ctrl+Shift+</c> y no otra letra.
/// </summary>
public sealed class GlobalHotkeys : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004;
    /// <summary>Sin esto, dejar la combinación pulsada dispara la acción decenas de veces.</summary>
    private const uint MOD_NOREPEAT = 0x4000;
    private const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly Dictionary<int, Action> _actions = new();
    private HwndSource? _source;
    private IntPtr _hwnd = IntPtr.Zero;
    private int _nextId = 0xB0;

    /// <summary>Qué atajo quedó activo para cada acción. Vacío si no se pudo registrar ninguno.</summary>
    public List<string> Activos { get; } = new();

    /// <summary>Lo que hay que contarle al usuario: qué funciona y, si algo no, por qué.</summary>
    public string Resumen { get; private set; } = "";

    /// <summary>
    /// Engancha los atajos sobre el HWND de la ventana. Se llama desde <c>OnSourceInitialized</c>,
    /// que es cuando el handle existe (el mismo momento en que las otras cuatro ventanas del cliente
    /// aplican sus estilos extendidos).
    /// </summary>
    public void Attach(Window window, Action invocar, Action microfono)
    {
        _hwnd = new WindowInteropHelper(window).Handle;
        if (_hwnd == IntPtr.Zero) { LogBus.Log("atajo", "la ventana no tiene HWND todavía: sin atajos"); return; }

        _source = HwndSource.FromHwnd(_hwnd);
        if (_source == null) { LogBus.Log("atajo", "no se pudo obtener el HwndSource: sin atajos"); return; }
        _source.AddHook(WndProc);

        // 'U' de Ü, 'M' de micrófono. Los códigos virtuales de las letras son su ASCII en mayúscula.
        bool u = TryRegister("invocar a Ü", MOD_CONTROL | MOD_ALT, 'U', "Ctrl+Alt+U",
                             MOD_CONTROL | MOD_SHIFT, "Ctrl+Shift+U", invocar);
        bool m = TryRegister("abrir el micrófono", MOD_CONTROL | MOD_ALT, 'M', "Ctrl+Alt+M",
                             MOD_CONTROL | MOD_SHIFT, "Ctrl+Shift+M", microfono);

        Resumen = (u || m)
            ? "⌨ " + string.Join(" · ", Activos)
            : "⌨ No se pudo registrar ningún atajo (otra aplicación usa esas combinaciones). Detalle en 📜 Logs.";
        LogBus.Log("atajo", Resumen);
    }

    /// <summary>
    /// Intenta la combinación preferida y, si está ocupada, la de respaldo. Cada intento deja línea en
    /// el registro y el resultado DICE cuál quedó: registrar otra combinación en silencio sería una
    /// caja que miente — el usuario pulsaría lo que anuncia el tooltip y no pasaría nada.
    /// </summary>
    private bool TryRegister(string paraQué, uint mods, char tecla, string nombre,
                             uint modsAlt, string nombreAlt, Action accion)
    {
        int id = _nextId++;
        if (RegisterHotKey(_hwnd, id, mods | MOD_NOREPEAT, tecla))
        {
            _actions[id] = accion;
            Activos.Add($"{nombre} {paraQué}");
            return true;
        }

        int err = Marshal.GetLastWin32Error();
        string causa = err == ERROR_HOTKEY_ALREADY_REGISTERED
            ? "otra aplicación ya la tiene"
            : $"Windows la rechazó (error {err})";
        LogBus.Log("atajo", $"{nombre} no se pudo registrar para {paraQué} — {causa}. Probando {nombreAlt}.");

        int idAlt = _nextId++;
        if (RegisterHotKey(_hwnd, idAlt, modsAlt | MOD_NOREPEAT, tecla))
        {
            _actions[idAlt] = accion;
            Activos.Add($"{nombreAlt} {paraQué}");
            return true;
        }

        int err2 = Marshal.GetLastWin32Error();
        LogBus.Log("atajo", $"{nombreAlt} tampoco: error {err2}. Sin atajo para {paraQué}.");
        return false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_HOTKEY) return IntPtr.Zero;
        if (!_actions.TryGetValue(wParam.ToInt32(), out var accion)) return IntPtr.Zero;
        handled = true;
        try { accion(); }
        catch (Exception ex) { LogBus.Log("atajo", $"la acción del atajo lanzó: {ex.Message}"); }
        return IntPtr.Zero;
    }

    /// <summary>
    /// Suelta las combinaciones. Un atajo huérfano no ralentiza la máquina como un hook de ratón, pero
    /// SECUESTRA la combinación hasta que muere el proceso — y con el updater reiniciando el .exe eso
    /// se notaría.
    /// </summary>
    public void Dispose()
    {
        foreach (int id in _actions.Keys) { try { UnregisterHotKey(_hwnd, id); } catch { } }
        _actions.Clear();
        try { _source?.RemoveHook(WndProc); } catch { }
        _source = null;
        _hwnd = IntPtr.Zero;
    }
}
