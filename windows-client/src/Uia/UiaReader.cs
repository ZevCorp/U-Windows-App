using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using U.WindowsClient.Domain;

namespace U.WindowsClient.Uia;

/// <summary>
/// Lee el árbol de UI de Windows con UIA (UI Automation) y lo resume como TEXTO para el cerebro,
/// igual que <c>GraphAccessibilityService</c> hacía con el árbol de accesibilidad en Android.
///
/// Produce el <see cref="ScreenState"/> por defecto (sin imagen): <c>Screen</c> (proceso · título de
/// ventana) y <c>UiContext</c> (tipo de pantalla + etiquetas accionables visibles). Con eso al cerebro
/// le basta para ubicarse y actuar por MCP; el screenshot solo se adjunta cuando pide computer-use.
///
/// También expone <see cref="Elements"/> (accionables con bounds) para resolver taps por etiqueta
/// (herramientas aprendidas) y <see cref="TapTargetForLabel"/> para el ejecutor MCP.
/// </summary>
public sealed class UiaReader
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc cb, IntPtr lparam);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder s, int max);
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lparam);
    private const int SM_CXSCREEN = 0, SM_CYSCREEN = 1;

    /// <summary>Un elemento accionable detectado en la pantalla visible.</summary>
    /// <summary>
    /// Un accionable de la pantalla. <paramref name="ItemType"/> es lo que la app dice que ES el
    /// elemento —«Carpeta de archivos», «Imagen PNG»—, y es la diferencia entre entrar en una
    /// carpeta y abrir una foto en otra aplicación.
    /// </summary>
    public sealed record UiElement(string Label, string ControlType, System.Windows.Rect Bounds,
        AutomationElement Native, string ItemType = "");

    /// <summary>Snapshot de accionables del último <see cref="Read"/>. Sirve para taps por etiqueta.</summary>
    public IReadOnlyList<UiElement> Elements { get; private set; } = Array.Empty<UiElement>();

    /// <summary>Nombre del proceso en primer plano en el último <see cref="Read"/> (p.ej. "saplogon", "notepad").</summary>
    public string ForegroundProcess { get; private set; } = "";

    private static readonly HashSet<ControlType> Actionable = new()
    {
        ControlType.Button, ControlType.MenuItem, ControlType.ListItem, ControlType.TreeItem,
        ControlType.TabItem, ControlType.Hyperlink, ControlType.Edit, ControlType.CheckBox,
        ControlType.RadioButton, ControlType.ComboBox, ControlType.SplitButton, ControlType.Text,
    };

    /// <summary>
    /// Captura el estado actual. <paramref name="withScreenshot"/> lo llena aparte el
    /// <c>Screenshotter</c>; aquí solo el texto del árbol de UI.
    /// </summary>
    public ScreenState Read()
    {
        var state = new ScreenState
        {
            Width = GetSystemMetrics(SM_CXSCREEN),
            Height = GetSystemMetrics(SM_CYSCREEN),
        };

        // La ventana DEL USUARIO, no la de delante sin más: si delante estamos nosotros —y lo
        // estamos siempre que alguien acaba de pulsar la carita para hablarnos— leer el primer
        // plano es leerse a uno mismo. Quien pregunta se refiere a lo que hay debajo.
        IntPtr hwnd = AppAligner.VentanaDelUsuario();
        if (hwnd == IntPtr.Zero)
        {
            state.Screen = "escritorio";
            state.UiContext = "Escritorio de Windows (sin ventana en primer plano).";
            Elements = Array.Empty<UiElement>();
            ForegroundProcess = "";
            return state;
        }

        string proc = ProcessName(hwnd);
        ForegroundProcess = proc;
        AutomationElement? root = SafeFromHandle(hwnd);
        string title = root?.Current.Name ?? "";
        state.Screen = string.IsNullOrWhiteSpace(title) ? proc : $"{proc} · {title}";

        var elements = new List<UiElement>();
        if (root != null)
        {
            try { Collect(root, elements, 0); } catch { /* UIA puede lanzar en árboles inestables */ }
        }

        // Contenido en VENTANAS HIJAS. El explorador de Windows 11 (y otras apps shell) mete su
        // lista de archivos en un HWND hijo —DirectUIHWND / SHELLDLL_DefView— cuyo árbol UIA NO
        // cuelga del de la ventana principal: FromHandle(principal) + descenso da 1 solo nodo.
        // Verificado leyendo el árbol en vivo (2026-07-31): los ListItem de los archivos solo
        // aparecen al hacer FromHandle sobre ese hijo. Sin esto el recorrido nunca veía la lista y
        // se quedaba paseando el panel izquierdo. Los duplicados por etiqueta se filtran abajo.
        try { CollectFromChildren(hwnd, elements); } catch { }
        try { CollectMenus(root, elements); } catch { }

        Elements = elements;

        state.UiContext = BuildContext(proc, title, elements);
        return state;
    }

    /// <summary>Devuelve el punto (centro) donde tocar el primer elemento cuya etiqueta coincida.</summary>
    public (int x, int y)? TapTargetForLabel(string label)
    {
        var el = Elements.FirstOrDefault(e => Match(e.Label, label));
        if (el == null || el.Bounds.IsEmpty || double.IsInfinity(el.Bounds.X)) return null;
        return ((int)(el.Bounds.X + el.Bounds.Width / 2), (int)(el.Bounds.Y + el.Bounds.Height / 2));
    }

    /// <summary>Invoca por patrón UIA (sin ratón) el primer elemento con esa etiqueta, si soporta Invoke.</summary>
    public bool InvokeLabel(string label)
    {
        var el = Elements.FirstOrDefault(e => Match(e.Label, label));
        if (el == null) return false;
        if (el.Native.TryGetCurrentPattern(InvokePattern.Pattern, out var p) && p is InvokePattern inv)
        {
            try { inv.Invoke(); return true; } catch { return false; }
        }
        return false;
    }

    private static bool Match(string a, string b) =>
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Clases de ventana hija que contienen elementos accionables que el árbol de la
    /// ventana principal no expone: la vista de contenido del shell y su host DirectUI.</summary>
    private static readonly HashSet<string> ChildContentClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "DirectUIHWND", "SHELLDLL_DefView", "SysListView32", "SysTreeView32",
        // WinUI 3: la barra de herramientas del explorador de Windows 11 (Atrás, Adelante, Subir,
        // barra de direcciones, pestañas) vive en estas dos y en ninguna otra. Sin ellas el sistema
        // no veía NADA de la barra —ni el botón de volver, que es justo lo que necesita para
        // retroceder por identidad en vez de con un gesto a ciegas (verificado el 2026-07-31).
        "Microsoft.UI.Content.DesktopChildSiteBridge", "InputSiteWindowClass",
    };

    /// <summary>
    /// Recorre las ventanas HIJAS que albergan contenido accionable fuera del árbol principal.
    /// Solo las clases conocidas —no todo HWND hijo— para no leer cromo ni pagar UIA sobre nada.
    /// </summary>
    private void CollectFromChildren(IntPtr parent, List<UiElement> acc)
    {
        var hijos = new List<IntPtr>();
        EnumChildWindows(parent, (h, _) =>
        {
            var sb = new System.Text.StringBuilder(128);
            GetClassName(h, sb, sb.Capacity);
            if (ChildContentClasses.Contains(sb.ToString())) hijos.Add(h);
            return true;
        }, IntPtr.Zero);

        foreach (var h in hijos)
        {
            var el = SafeFromHandle(h);
            if (el != null) { try { Collect(el, acc, 0); } catch { } }
        }
    }

    /// <summary>
    /// Los elementos de MENÚ, buscados con FindAll en vez de caminando el árbol.
    ///
    /// El recorrido con TreeWalker no llega a ellos: un menú abierto cuelga de una frontera que el
    /// ControlViewWalker no cruza —vive además en su propia ventana emergente
    /// (Microsoft.UI.Content.PopupWindowSiteBridge)— aunque FindAll(Descendants) sobre la ventana
    /// principal SÍ los encuentra. Sin esto, el asistente pulsaba «Nuevo», el menú se abría con
    /// «Carpeta» dentro, y seguía sin ver ninguna opción: podía abrir menús pero nunca elegir en
    /// ellos (2026-08-02). Es general, no del explorador: los menús de cualquier app estaban
    /// invisibles.
    /// </summary>
    private static void CollectMenus(AutomationElement? root, List<UiElement> acc)
    {
        if (root == null) return;
        var menus = root.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem));
        foreach (AutomationElement el in menus)
        {
            try
            {
                var info = el.Current;
                if (info.IsOffscreen) continue;
                string label = LabelOf(el, info);
                var r = info.BoundingRectangle;
                if (string.IsNullOrWhiteSpace(label) || r.IsEmpty || r.Width < 1 || r.Height < 1) continue;
                if (acc.Any(e => e.Label.Equals(label, StringComparison.OrdinalIgnoreCase)
                              && e.ControlType.Equals("MenuItem", StringComparison.OrdinalIgnoreCase))) continue;
                acc.Add(new UiElement(label, "MenuItem", r, el, ItemTypeDe(el)));
            }
            catch { }
        }
    }

    /// <summary>Lo que la app declara que es el elemento. Vacío si no lo dice.</summary>
    private static string ItemTypeDe(AutomationElement el)
    {
        try { return (el.GetCurrentPropertyValue(AutomationElement.ItemTypeProperty) as string ?? "").Trim(); }
        catch { return ""; }
    }

    private static void Collect(AutomationElement node, List<UiElement> acc, int depth)
    {
        if (depth > 40 || acc.Count > 400) return;
        AutomationElement? child = TreeWalker.ControlViewWalker.GetFirstChild(node);
        while (child != null)
        {
            try
            {
                var info = child.Current;
                var ct = info.ControlType;
                bool offscreen = info.IsOffscreen;
                if (!offscreen && Actionable.Contains(ct))
                {
                    string label = LabelOf(child, info);
                    // Sin geometría no hay dónde pulsar: un rect vacío acababa en un clic a (0,0)
                    // que el sistema daba por bueno (visto el 2026-07-31 en un TreeItem 'Escritorio'
                    // con rect=Empty). Aceptado no es ejecutado, y aquí ni siquiera es accionable.
                    var r = info.BoundingRectangle;
                    if (!string.IsNullOrWhiteSpace(label) && !r.IsEmpty && r.Width >= 1 && r.Height >= 1)
                        acc.Add(new UiElement(label, ControlTypeName(ct), r, child, ItemTypeDe(child)));
                }
            }
            catch { /* nodo muerto */ }

            try { Collect(child, acc, depth + 1); } catch { }
            child = TreeWalker.ControlViewWalker.GetNextSibling(child);
        }
    }

    /// <summary>La etiqueta con la que el agente identifica un elemento: Name → AutomationId → HelpText.</summary>
    private static string LabelOf(AutomationElement el, AutomationElement.AutomationElementInformation info)
    {
        if (!string.IsNullOrWhiteSpace(info.Name)) return info.Name.Trim();
        if (!string.IsNullOrWhiteSpace(info.AutomationId)) return info.AutomationId.Trim();
        try
        {
            var help = el.GetCurrentPropertyValue(AutomationElement.HelpTextProperty) as string;
            if (!string.IsNullOrWhiteSpace(help)) return help.Trim();
        }
        catch { }
        return "";
    }

    private static string BuildContext(string proc, string title, List<UiElement> elements)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"App en primer plano: {proc}");
        if (!string.IsNullOrWhiteSpace(title)) sb.AppendLine($"Ventana: {title}");
        sb.AppendLine($"Elementos accionables visibles ({elements.Count}):");

        // Agrupa por tipo para que el texto sea legible y compacto (como el uiContext de Android).
        foreach (var grp in elements.GroupBy(e => e.ControlType))
        {
            var labels = grp.Select(e => e.Label)
                            .Where(l => l.Length <= 60)
                            .Distinct()
                            .Take(40);
            sb.AppendLine($"- {grp.Key}: {string.Join(" | ", labels)}");
        }
        return sb.ToString().TrimEnd();
    }

    private static string ProcessName(IntPtr hwnd)
    {
        try
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch { return "app"; }
    }

    private static AutomationElement? SafeFromHandle(IntPtr hwnd)
    {
        try { return AutomationElement.FromHandle(hwnd); }
        catch { return null; }
    }

    private static string ControlTypeName(ControlType ct) => ct.ProgrammaticName.Replace("ControlType.", "");
}
