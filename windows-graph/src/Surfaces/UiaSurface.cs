using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace U.Graph.Surfaces;

/// <summary>
/// Superficie UIA: cualquier app de Windows. Es la red de seguridad — funciona en todas partes, pero
/// entiende menos que <see cref="SapGuiSurface"/> cuando la app de abajo es SAP.
///
/// Comparte enfoque con <c>windows-client/src/Uia/UiaReader.cs</c> pero NO lo reutiliza: aquel resume
/// el árbol como TEXTO para un LLM, este lo estructura como CAMPOS para Graph. Mezclarlos ataría este
/// módulo al asistente, que es justo lo que queremos evitar.
/// </summary>
public sealed class UiaSurface : IUiSurface
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
    private const uint GA_ROOT = 2;
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int maxCount);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder text, int maxCount);
    private const uint GW_HWNDNEXT = 2;

    /// <summary>El origin canónico del ESCRITORIO de Windows. Es una superficie de primera clase, distinta
    /// del Explorador de archivos (ambos son explorer.exe): así el navegador la alcanza con "mostrar
    /// escritorio" (Win+D) y nunca abre una ventana del Explorador por error. Ver DesktopStrategy.</summary>
    public const string DesktopOrigin = "uia://desktop";

    // Hook global de mouse: la captura de clics FIABLE (posición → elemento por UIA), el MISMO mecanismo
    // que el UiInspector. Reemplaza a InvokePattern.InvokedEvent, que solo disparaba para ciertos
    // controles y exigía que algo (el inspector) mantuviera vivo el canal UIA. Ahora la grabación NO
    // depende del inspector, captura clics en CUALQUIER app (cross-app) y excluye la propia UI de Ü.
    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)] private static extern IntPtr GetModuleHandle(string? lpModuleName);
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_MOUSEWHEEL = 0x020A;

    // Hook de teclado: para grabar teclas de ACCIÓN (Enter y, más adelante, Tab/Esc). El tecleo de texto
    // ya lo capta ValueProperty; esto es solo el gesto que envía/confirma, que UIA no expone como paso.
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const byte VK_RETURN = 0x0D;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    /// <summary>Sin este flag, Windows entrega las flechas/Inicio/Fin/Re-Av Pág como las del teclado
    /// numérico y el destino recibe otra tecla. Ver <see cref="IsExtendedKey"/>.</summary>
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT { public uint vkCode; public uint scanCode; public uint flags; public uint time; public IntPtr dwExtraInfo; }

    // Clic REAL: mover el cursor y hacer un clic físico (visible, y funciona en shell/escritorio/taskbar
    // donde InvokePattern no hace nada). Se usa mouse_event por consistencia con keybd_event del resto
    // del código. La ejecución por coordenadas es el espejo de cómo se graba (por posición).
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, IntPtr dwExtraInfo);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_RESTORE = 9;
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004, MOUSEEVENTF_WHEEL = 0x0800;

    /// <summary>
    /// Doble clic REAL sobre el elemento. Reutiliza <see cref="RealClick"/> dos veces para heredar
    /// todo lo que aquel resuelve —foco de la ventana, comprobación de oclusión, movimiento suave
    /// del cursor— en vez de duplicar esa lógica, que es donde vive el conocimiento caro.
    /// Los 120 ms son el margen del doble clic de Windows con holgura: más rápido y el sistema
    /// pierde el segundo, más lento y lo interpreta como dos clics sueltos.
    /// </summary>
    private bool RealDoubleClick(System.Windows.Automation.AutomationElement el, out string error)
    {
        // El PRIMER clic hace todo el trabajo caro —enfocar, desplazar a la vista, releer la caja,
        // mover el cursor—; el segundo va inmediatamente después, en el mismo punto y sin nada de
        // eso. Repetir RealClick entero metía ~300 ms entre pulsación y pulsación (ScrollIntoView
        // duerme 120, el enfoque 40, el movimiento suave más) y Windows dejaba de verlo como un
        // doble clic: lo interpretaba como dos clics sueltos, que solo SELECCIONAN. El síntoma era
        // desconcertante —«pulsé Facturas pero no se llegó», con ok=True— y de ahí salieron los
        // pegados en la carpeta equivocada (2026-08-02).
        // Y el primero tiene que ser un CLIC DE VERDAD. La sustitución por Select() —que existe
        // porque muchas apps WinUI ignoran el ratón sintético— convertía el doble clic en dos
        // selecciones, y seleccionar no abre nada: se creaban las tres carpetas y no se entraba en
        // ninguna, con ok=True en cada paso (2026-08-03). Seleccionar y abrir son intenciones
        // distintas; quien pide un doble clic pide abrir, y Select() no sabe decir eso.
        if (!RealClick(el, out error, permitirSelect: false)) return false;

        System.Threading.Thread.Sleep(60);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero);
        L("    → segundo clic del doble (mismo punto, sin recolocar)");
        return true;
    }

    /// <summary>El cursor automatizado, frame a frame: la carita colapsada lo escucha para SEGUIRLO
    /// (se ve "quién" está haciendo los clics). Estático a propósito: hay una sola automatización viva.</summary>
    public static event Action<int, int>? CursorMoved;

    /// <summary>
    /// Mueve el cursor con curva de aceleración (ease-in cúbica: arranque lento → aceleración fuerte) en
    /// vez de teletransportarlo. Duración según la distancia, acotada para que se sienta fluidamente rápido.
    /// </summary>
    private static void SmoothMove(int toX, int toY)
    {
        if (!GetCursorPos(out POINT from)) { SetCursorPos(toX, toY); return; }
        double dist = Math.Sqrt(Math.Pow(toX - from.X, 2) + Math.Pow(toY - from.Y, 2));
        if (dist < 4) { SetCursorPos(toX, toY); return; }

        int ms = (int)Math.Clamp(dist * 0.35, 110, 380);
        int frames = Math.Max(2, ms / 12);
        for (int i = 1; i <= frames; i++)
        {
            double t = (double)i / frames;
            double e = t * t * t; // la curva "brusca": lenta al inicio, fuerte al final
            int x = (int)Math.Round(from.X + (toX - from.X) * e);
            int y = (int)Math.Round(from.Y + (toY - from.Y) * e);
            SetCursorPos(x, y);
            try { CursorMoved?.Invoke(x, y); } catch { }
            Thread.Sleep(12);
        }
        SetCursorPos(toX, toY);
        try { CursorMoved?.Invoke(toX, toY); } catch { }
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData; public uint flags; public uint time; public IntPtr dwExtraInfo; }

    public string Name => "uia";

    /// <summary>
    /// Log de diagnóstico (opcional). windows-graph no depende de windows-client, así que el cliente
    /// enchufa aquí su LogBus (tag "uia"). Es el ojo dentro de la resolución y el clic — el punto ciego
    /// donde antes no veíamos por qué un paso decía ✓ sin pasar nada en pantalla.
    /// </summary>
    public Action<string>? Log { get; set; }
    private void L(string msg) { try { Log?.Invoke(msg); } catch { } }

    public event EventHandler<ObservedStep>? StepObserved;

    private IntPtr _hook = IntPtr.Zero;
    private HookProc? _hookProc;                 // referencia viva: si el GC lo recoge, el hook revienta
    private IntPtr _keyHook = IntPtr.Zero;
    private HookProc? _keyHookProc;              // idem para el hook de teclado

    // Scroll: se ACUMULA el delta de rueda y, tras una pausa, se publica UN paso "scroll" (no uno por
    // notch). Así un gesto de deslizar hasta el final es un solo paso con el delta total.
    private int _scrollAccum, _scrollX, _scrollY;
    private System.Threading.Timer? _scrollTimer;
    private readonly object _scrollGate = new();
    private AutomationElement? _valueRoot;       // ventana donde escuchamos cambios de valor (tecleo/select)
    private IntPtr _valueRootHwnd = IntPtr.Zero; // para re-anclar solo cuando cambia de ventana
    private AutomationPropertyChangedEventHandler? _propertyChanged;
    private bool _observing;
    private readonly object _gate = new();

    /// <summary>Qué se considera un campo con el que se interactúa. Text/Label quedan fuera: no son acciones.</summary>
    private static readonly HashSet<ControlType> Interactive = new()
    {
        ControlType.Edit, ControlType.ComboBox, ControlType.CheckBox, ControlType.RadioButton,
        ControlType.Button, ControlType.List, ControlType.ListItem, ControlType.MenuItem,
        ControlType.Hyperlink, ControlType.TabItem, ControlType.SplitButton, ControlType.Document,
    };

    public SurfaceAvailability Check() =>
        GetForegroundWindow() == IntPtr.Zero
            ? SurfaceAvailability.No("No hay ninguna ventana en primer plano.")
            : SurfaceAvailability.Ok;

    public SurfaceIdentity Identity()
    {
        IntPtr hwnd = RealForegroundWindow();
        if (hwnd == IntPtr.Zero) return SurfaceIdentity.Unknown;

        // El ESCRITORIO es su propia superficie, no "explorer.exe". Windows lo dibuja con la ventana
        // shell Progman (y a veces WorkerW cuando hay fondo activo): ambas son el escritorio. Detectarlo
        // por CLASE de ventana es lo auténtico —el título "Program Manager" es solo su nombre interno—.
        if (IsDesktopWindow(hwnd))
            return new SurfaceIdentity(DesktopOrigin, "/", "Escritorio");

        string proc = ProcessName(hwnd);
        string title = Root(hwnd)?.Current.Name ?? "";
        return new SurfaceIdentity($"uia://{proc}", "/" + title.Trim(), title.Trim());
    }

    /// <summary>¿Es la ventana del escritorio (no una ventana del Explorador de archivos)? Por clase.</summary>
    private static bool IsDesktopWindow(IntPtr hwnd)
    {
        var sb = new System.Text.StringBuilder(64);
        if (GetClassName(hwnd, sb, sb.Capacity) == 0) return false;
        string cls = sb.ToString();
        return cls == "Progman" || cls == "WorkerW";
    }

    /// <summary>
    /// La ventana "real" en foco, SALTANDO la propia UI de Ü. La carita es un overlay topmost que suele
    /// quedar como foreground; si se toma su ventana como superficie, el workflow nace con home
    /// <c>uia://U.exe</c> —nunca alcanzable ni alineable—, el pre-check siempre pasa ("ya estamos ahí")
    /// y el SurfaceNavigator jamás va a buscar el sitio de arranque real. Se baja por el orden-Z hasta la
    /// primera ventana visible y con título que no sea de Ü (el escritorio o la app que estabas usando).
    /// </summary>
    private static IntPtr RealForegroundWindow()
    {
        IntPtr hwnd = GetForegroundWindow();
        for (int i = 0; i < 50 && hwnd != IntPtr.Zero; i++)
        {
            if (IsWindowVisible(hwnd) && !IsOwnWindow(hwnd) && HasTitle(hwnd)) return hwnd;
            hwnd = GetWindow(hwnd, GW_HWNDNEXT);
        }
        return GetForegroundWindow(); // si no hallamos otra, la original: mejor algo que Unknown
    }

    private static bool HasTitle(IntPtr hwnd)
    {
        var sb = new System.Text.StringBuilder(64);
        return GetWindowText(hwnd, sb, sb.Capacity) > 0;
    }

    // ── Lectura ──────────────────────────────────────────────────────────────

    public IReadOnlyList<DetectedField> ReadFields()
    {
        var fields = new List<DetectedField>();
        IntPtr hwnd = GetForegroundWindow();
        AutomationElement? root = hwnd == IntPtr.Zero ? null : Root(hwnd);
        if (root == null) return fields;

        var found = new List<(AutomationElement El, List<int> Path)>();
        try { Walk(root, found, new List<int>(), 0); }
        catch { /* UIA lanza en árboles que cambian mientras se recorren */ }

        int order = 1;
        foreach (var (el, path) in found)
        {
            var field = Describe(el, path, order);
            if (field != null) { fields.Add(field); order++; }
        }
        return fields;
    }

    /// <summary>
    /// Métrica del motor de carga (SurfaceReadiness): número de elementos interactivos VISIBLES y
    /// HABILITADOS en la pantalla. Mientras la UI carga, pocos están on-screen/enabled; ya cargada, todos.
    /// Al grabar es la "meta" (100%); al ejecutar se compara el actual para saber el % cargado.
    /// </summary>
    public int ReadinessCount()
    {
        IntPtr hwnd = GetForegroundWindow();
        AutomationElement? root = hwnd == IntPtr.Zero ? null : Root(hwnd);
        if (root == null) return 0;
        var found = new List<(AutomationElement, List<int>)>();
        try { Walk(root, found, new List<int>(), 0); } catch { }
        return found.Count;
    }

    /// <summary>
    /// Huella estructural: AutomationId + tipo de cada elemento interactivo de la ventana en foco. El
    /// AutomationId solo no basta —bajo SAP son genéricos y se repiten (1001, 200, 100)—, así que se
    /// combina con el tipo y, si no hay id, con la RUTA, que es lo único que distingue dos controles
    /// por lo demás idénticos. Nunca el texto: eso es dato, y cambiaría en cada corrida.
    /// </summary>
    public string StructureFingerprint()
    {
        IntPtr hwnd = GetForegroundWindow();
        AutomationElement? root = hwnd == IntPtr.Zero ? null : Root(hwnd);
        if (root == null) return "";

        var found = new List<(AutomationElement El, List<int> Path)>();
        try { Walk(root, found, new List<int>(), 0); } catch { return ""; }

        var ids = new List<string>();
        foreach (var (el, path) in found)
        {
            try
            {
                var info = el.Current;
                string aid = info.AutomationId ?? "";
                ids.Add(aid.Length > 0
                    ? $"{aid}|{info.ControlType.ProgrammaticName}"
                    : $"@{string.Join(".", path)}|{info.ControlType.ProgrammaticName}");
            }
            catch { /* nodo muerto entre la enumeración y la lectura */ }
        }
        return Fingerprints.Of(ids);
    }

    /// <summary>
    /// Corto-circuito del motor de carga: ¿el elemento del paso ya está presente Y habilitado? Si sí, se
    /// ejecuta YA sin esperar el % global (que es inestable en listas como las de SAP). Tecla/scroll no
    /// tienen elemento → listos siempre.
    /// </summary>
    public bool IsStepReady(PlanStep step)
    {
        string at = step.ActionType ?? "";
        if (at.Equals("key", StringComparison.OrdinalIgnoreCase) || at.Equals("scroll", StringComparison.OrdinalIgnoreCase))
            return true;

        var candidates = new List<string> { step.Selector };
        candidates.AddRange(step.AlternativeTargets().Where(UiaSelector.Owns));
        foreach (string sel in candidates.Where(UiaSelector.Owns))
        {
            // foregroundOnly: LISTO significa «está en la ventana que el usuario tiene delante», no
            // «existe en algún sitio del escritorio». Resolve() cae, si no lo encuentra en el foreground,
            // a un barrido de TODAS las ventanas de nivel superior — y él mismo avisa de que el hallazgo
            // «puede estar tapado». Para EJECUTAR ese respaldo tiene sentido; para la COMPUERTA no: los
            // ids de SAP bajo UIA son genéricos (aid=1001, aid=200, aid=100 salen en cada pantalla), así
            // que el barrido encontraba uno siempre, IsStepReady decía «listo» al instante y la espera no
            // esperaba nada. Ese es el «clica tan rápido que el siguiente clic cae donde no existe»:
            // no era velocidad, era una compuerta que daba luz verde contra una ventana equivocada.
            var el = Resolve(sel, foregroundOnly: true);
            if (el == null) continue;
            try { if (el.Current.IsEnabled) return true; } // presente Y habilitado = listo
            catch { return true; } // si no podemos leer IsEnabled, no bloquear
        }
        return false;
    }

    // Cache por-superficie para no recorrer el árbol en CADA paso al grabar (solo al cambiar de pantalla).
    private string _readyCacheUrl = "";
    private int _readyCacheCount = -1;
    private int CachedReadinessCount(string? url = null)
    {
        url ??= Identity().Url;
        if (url == _readyCacheUrl && _readyCacheCount >= 0) return _readyCacheCount;
        int c = ReadinessCount();
        _readyCacheUrl = url;
        _readyCacheCount = c;
        return c;
    }

    private static void Walk(AutomationElement node, List<(AutomationElement, List<int>)> acc, List<int> path, int depth)
    {
        if (depth > 40 || acc.Count > 300) return;

        AutomationElement? child = TreeWalker.ControlViewWalker.GetFirstChild(node);
        int index = 0;
        while (child != null)
        {
            var here = new List<int>(path) { index };
            try
            {
                var info = child.Current;
                if (!info.IsOffscreen && Interactive.Contains(info.ControlType) && info.IsEnabled)
                    acc.Add((child, here));
            }
            catch { /* nodo muerto entre la enumeración y la lectura */ }

            try { Walk(child, acc, here, depth + 1); } catch { }

            try { child = TreeWalker.ControlViewWalker.GetNextSibling(child); }
            catch { break; }
            index++;
        }
    }

    private DetectedField? Describe(AutomationElement el, List<int> path, int order)
    {
        try
        {
            var info = el.Current;
            string label = LabelOf(el, info);
            if (string.IsNullOrWhiteSpace(label)) return null;

            string ct = ControlTypeName(info.ControlType);
            var selectors = SelectorsFor(info, path, ct);
            if (selectors.Count == 0) return null;

            return new DetectedField
            {
                StepOrder = order,
                ActionType = ActionTypeFor(info.ControlType),
                Selector = selectors[0],
                Label = label,
                ControlType = GraphControlType(info.ControlType, el),
                CurrentValue = ValueOf(el),
                AllowedOptions = OptionsOf(el, info.ControlType),
            };
        }
        catch { return null; }
    }

    /// <summary>Los selectores de un elemento, del más estable al más frágil.</summary>
    /// <summary>
    /// La respuesta canónica a «¿qué es este elemento?», para quien esté FUERA de la grabación.
    ///
    /// Existe porque la pregunta ya tuvo dos respuestas a la vez y salió caro: el mapa base del
    /// computador (ClickWatcher) resolvía clics con un FromPoint crudo y un selector inventado
    /// («uia:id=X»), mientras la grabación usaba LabelOf + SelectorsFor. Resultado medido
    /// (2026-07-29): ~48% de las acciones del terreno eran inservibles, y ninguna era ejecutable
    /// por el player porque ni siquiera hablaban el formato de los workflows. Mismo patrón que ya
    /// costó una jornada con dos varas para «¿estoy en esta pantalla?».
    ///
    /// Devuelve la MISMA tripleta que se persiste en un paso grabado: etiqueta humana, tipo de
    /// control y selectores por identidad con alternativas (AutomationId → Name → Path). Quien
    /// consuma esto produce aristas que el ejecutor puede recorrer tal cual.
    /// </summary>
    public static (string Label, string ControlType, List<string> Selectors) DescribeElement(AutomationElement el)
    {
        var info = el.Current;
        string ct = ControlTypeName(info.ControlType);
        return (LabelOf(el, info), ct, SelectorsFor(info, new List<int>(), ct));
    }

    /// <summary>
    /// A qué GRUPO de la interfaz pertenece un elemento, según UIA.
    ///
    /// La jerarquía no hay que inventarla: la aplicación ya la declara. Los doce enlaces del panel
    /// lateral del explorador viven dentro de un Tree; los botones de arriba, dentro de un ToolBar;
    /// los archivos, dentro de una List. Eso se sabe MIRANDO UNA pantalla, mientras que deducirlo
    /// por estadística —«lo he visto desde dos sitios, será del panel»— tarda dos visitas y se
    /// equivoca con lo que casualmente se repite (2026-08-04, a propuesta del usuario).
    ///
    /// Devuelve "" cuando el elemento cuelga directo de la ventana: no todo pertenece a un grupo, y
    /// fingir que sí sería el mismo error al revés.
    /// </summary>
    public static string GrupoDe(AutomationElement el)
    {
        // EL LANDMARK GANA AUNQUE ESTÉ MÁS ARRIBA. Lo genérico que se encuentre por el camino se
        // guarda y solo se usa si no aparece ningún landmark por encima.
        //
        // Sin esto el landmark casi nunca ganaba, y se midió: «Code · Issues · Pull requests» de
        // GitHub viven dentro de <nav aria-label="Repository"> pero maquetados con un <ul>, así
        // que el ancestro MÁS CERCANO era una List y el grupo salía «lista» — el nav quedaba dos
        // pisos más arriba, sin que nadie llegara a mirarlo (2026-08-08). Una lista dentro de una
        // navegación sigue siendo navegación: la lista es cómo se maqueta, el nav es lo que ES.
        string generico = "";
        try
        {
            var padre = TreeWalker.ControlViewWalker.GetParent(el);
            for (int i = 0; i < 10 && padre != null; i++)
            {
                var info = padre.Current;
                var ct = info.ControlType;
                string clase = (info.ClassName ?? "").Trim();

                // LOS LANDMARKS DE LA PÁGINA, PRIMERO. En una web la estructura no hay que
                // deducirla ni preguntársela a un modelo: HTML tiene un estándar para declararla
                // —<nav>, <main>, <header>, role="navigation"— y Chrome lo traduce a UIA. Medido
                // sobre GitHub el 2026-08-08: «banner» con la navegación global, «navegación
                // Repository» con Code/Issues/Pull requests, «navegación Breadcrumbs» con la cadena
                // de padres, y «principal» con las 97 puertas de contenido. Justo nuestro modelo,
                // escrito por el propio sitio.
                //
                // Va ANTES que el resto porque un landmark es más específico que el contenedor
                // genérico donde caiga: dentro de un <nav> puede haber una lista, y quedarse con
                // «lista» perdería lo único que decía que aquello es navegación.
                //
                // Se lee de LocalizedControlType y no de AriaRole a propósito: la API que usamos
                // (System.Windows.Automation) no expone AriaRole —comprobado, cero de 463
                // elementos— y el nombre localizado sí llega. Por eso la tabla incluye las dos
                // formas: el sistema traduce, y un mapa que solo funciona en inglés no es un mapa.
                string landmark = LandmarkWeb(info.LocalizedControlType);
                if (landmark.Length > 0) return Nombrar(landmark, padre);

                // El TIPO cuando lo hay: es lo estándar y lo que usan las apps clásicas. Se
                // APUNTA el primero que aparezca, pero no se devuelve todavía: puede haber un
                // landmark por encima que lo explique mejor (ver arriba).
                if (generico.Length == 0)
                {
                    if (ct == ControlType.Tree) generico = Nombrar("navegación", padre);
                    else if (ct == ControlType.ToolBar) generico = Nombrar("herramientas", padre);
                    else if (ct == ControlType.MenuBar || ct == ControlType.Menu) generico = Nombrar("menú", padre);
                    else if (ct == ControlType.Tab) generico = Nombrar("pestañas", padre);
                    else if (ct == ControlType.List || ct == ControlType.DataGrid) generico = Nombrar("lista", padre);
                }

                // Y LA CLASE cuando el tipo no dice nada, que es lo normal en WinUI. El explorador
                // de Windows 11 mete sus botones en un contenedor llamado «ApplicationBar» cuyo
                // ControlType viene VACÍO: mirando solo el tipo, la barra de herramientas entera
                // parecía no pertenecer a ningún grupo (2026-08-04, comprobado volcando el árbol).
                // La app declara la estructura; solo que a veces por un canal y a veces por el otro.
                string porClase = PorClase(clase);
                if (porClase.Length > 0 && generico.Length == 0) generico = Nombrar(porClase, padre);

                if (ct == ControlType.Window) break;   // se llegó a la ventana: no hay grupo
                padre = TreeWalker.ControlViewWalker.GetParent(padre);
            }
        }
        catch { }
        return generico;   // ningún landmark por encima: manda el contenedor que se encontró
    }

    /// <summary>
    /// La cadena de ancestros, para diagnosticar. Cuando <see cref="GrupoDe"/> devuelve vacío hay
    /// dos explicaciones opuestas —el elemento no está en ningún grupo, o no supimos verlo— y sin
    /// esto se ven igual.
    /// </summary>
    public static string Ancestros(AutomationElement el)
    {
        var partes = new List<string>();
        try
        {
            var p = TreeWalker.ControlViewWalker.GetParent(el);
            for (int i = 0; i < 6 && p != null; i++)
            {
                var inf = p.Current;
                partes.Add($"{ControlTypeName(inf.ControlType)}/{inf.ClassName}");
                p = TreeWalker.ControlViewWalker.GetParent(p);
            }
        }
        catch (Exception e) { partes.Add("ERROR:" + e.GetType().Name); }
        return partes.Count > 0 ? string.Join(" > ", partes) : "(sin padre)";
    }

    /// <summary>
    /// Los LANDMARKS de una página web, tal como el navegador los nombra.
    ///
    /// Cada uno responde a una pregunta que hasta ahora costaba dinero o varias visitas:
    ///   · banner / navegación → esto es NAVEGACIÓN, no contenido. La base del primer nivel.
    ///   · ruta (breadcrumb)   → la cadena de padres, dicha por el sitio: dónde estás en su árbol.
    ///   · principal          → CONTENIDO. Lo que hay dentro no es estructura, por mucho que se vea.
    ///   · complementario     → lateral: acompaña, no navega el sitio entero.
    ///
    /// Se comparan nombres en español e inglés porque UIA los localiza según el sistema, y una
    /// tabla que solo entiende inglés dejaría el mapa ciego en la mitad de las máquinas. El
    /// «contentinfo» de HTML llega como «pie» o «footer» según idioma.
    /// </summary>
    private static string LandmarkWeb(string localizado)
    {
        string l = (localizado ?? "").Trim().ToLowerInvariant();
        if (l.Length == 0) return "";
        // El breadcrumb primero: es una navegación, pero decir «ruta» conserva que además ordena.
        if (l.Contains("breadcrumb") || l.Contains("ruta de navegación")) return "ruta";
        if (l.StartsWith("navegaci") || l == "navigation") return "navegación";
        // LA CABECERA APARTE DE LA NAVEGACIÓN, aunque las dos sean navegar. El <header> es el
        // marco del SITIO —está en todas las páginas, es cromo de nivel 1— y un <nav> suelto suele
        // ser la navegación de UNA sección, que es nivel 2. Colapsarlas en «navegación» borraba
        // justo la diferencia que hace falta para asignar el nivel (2026-08-08).
        if (l == "banner" || l == "encabezado" || l == "header") return "cabecera";
        if (l == "principal" || l == "main") return "contenido";
        if (l == "complementario" || l == "complementary" || l == "aside") return "lateral";
        if (l == "pie" || l == "pie de página" || l == "footer" || l == "contentinfo") return "pie";
        return "";
    }

    /// <summary>Contenedores que se reconocen por su clase porque no declaran ControlType.</summary>
    private static string PorClase(string clase)
    {
        if (clase.Length == 0) return "";
        if (clase.Contains("NavigationView", StringComparison.OrdinalIgnoreCase)
            || clase.Contains("TreeView", StringComparison.OrdinalIgnoreCase)
            || clase.Contains("SysTreeView", StringComparison.OrdinalIgnoreCase)) return "navegación";
        if (clase.Contains("ApplicationBar", StringComparison.OrdinalIgnoreCase)
            || clase.Contains("CommandBar", StringComparison.OrdinalIgnoreCase)
            || clase.Contains("ToolbarWindow", StringComparison.OrdinalIgnoreCase)) return "herramientas";
        if (clase.Contains("SHELLDLL_DefView", StringComparison.OrdinalIgnoreCase)
            || clase.Contains("DirectUIHWND", StringComparison.OrdinalIgnoreCase)) return "contenido";
        if (clase.Contains("Breadcrumb", StringComparison.OrdinalIgnoreCase)) return "ruta";
        if (clase.Contains("TabView", StringComparison.OrdinalIgnoreCase)) return "pestañas";
        return "";
    }

    /// <summary>El grupo lleva su nombre cuando lo tiene: «navegación:Panel de navegación» dice más
    /// que «navegación», y una app con dos listas necesita distinguirlas.</summary>
    private static string Nombrar(string clase, AutomationElement contenedor)
    {
        string n = "";
        try { n = (contenedor.Current.Name ?? "").Trim(); } catch { }
        return n.Length > 0 ? $"{clase}:{n}" : clase;
    }

    private static List<string> SelectorsFor(
        AutomationElement.AutomationElementInformation info, List<int> path, string ct)
    {
        var list = new List<string>();
        string aid = (info.AutomationId ?? "").Trim();
        string name = (info.Name ?? "").Trim();

        // Un AutomationId NUMÉRICO no es una identidad: es una POSICIÓN. En la lista del
        // explorador de Windows cada fila lleva su índice («0», «1», «2»…), así que
        // «uia:aid=1;ct=ListItem» no señala un archivo concreto sino «el segundo de lo que haya
        // ahora» — apunta a otra cosa en cuanto se ordena distinto o se entra en otra carpeta, y
        // colisiona entre pantallas. El nombre, con todos sus defectos, sí describe la cosa.
        // Un aid así se conserva como respaldo, nunca como selector principal (2026-08-01).
        bool aidEsPosicional = aid.Length > 0 && aid.All(char.IsDigit);

        if (aid.Length > 0 && !aidEsPosicional) list.Add(UiaSelector.ByAutomationId(aid, ct));
        if (name.Length > 0) list.Add(UiaSelector.ByName(name, ct));
        if (aidEsPosicional) list.Add(UiaSelector.ByAutomationId(aid, ct));
        list.Add(UiaSelector.ByPath(path, ct));
        return list;
    }

    /// <summary>Name → AutomationId → HelpText. Es lo que un humano llamaría "el campo".</summary>
    private static string LabelOf(AutomationElement el, AutomationElement.AutomationElementInformation info)
    {
        if (!string.IsNullOrWhiteSpace(info.Name)) return info.Name.Trim();
        if (!string.IsNullOrWhiteSpace(info.AutomationId)) return info.AutomationId.Trim();
        try
        {
            if (el.GetCurrentPropertyValue(AutomationElement.HelpTextProperty) is string help &&
                !string.IsNullOrWhiteSpace(help))
                return help.Trim();
        }
        catch { }
        return "";
    }

    private static string? ValueOf(AutomationElement el)
    {
        try
        {
            if (el.TryGetCurrentPattern(ValuePattern.Pattern, out var p) && p is ValuePattern v)
                return v.Current.Value;
        }
        catch { }
        try
        {
            if (el.TryGetCurrentPattern(TogglePattern.Pattern, out var p) && p is TogglePattern t)
                return t.Current.ToggleState == ToggleState.On ? "true" : "false";
        }
        catch { }
        try
        {
            if (el.TryGetCurrentPattern(SelectionPattern.Pattern, out var p) && p is SelectionPattern s)
            {
                var sel = s.Current.GetSelection();
                if (sel.Length > 0) return sel[0].Current.Name;
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Las opciones de un combo/lista. NO se expande el control para leerlas: abrir un desplegable en
    /// el SAP de un cliente es una acción visible que puede disparar validaciones. Se leen los hijos
    /// que ya estén expuestos; si no hay, se devuelve null y Graph trata el campo como texto libre.
    /// </summary>
    private static List<FieldOption>? OptionsOf(AutomationElement el, ControlType ct)
    {
        if (ct != ControlType.ComboBox && ct != ControlType.List) return null;
        try
        {
            var items = el.FindAll(TreeScope.Children,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
            if (items.Count == 0) return null;

            var options = new List<FieldOption>();
            foreach (AutomationElement item in items)
            {
                string n = item.Current.Name?.Trim() ?? "";
                if (n.Length > 0) options.Add(new FieldOption { Value = n, Label = n, Text = n });
                if (options.Count >= 80) break; // el límite que aplica NoteFieldMatcher al otro lado
            }
            return options.Count > 0 ? options : null;
        }
        catch { return null; }
    }

    private static string ActionTypeFor(ControlType ct)
    {
        if (ct == ControlType.Edit || ct == ControlType.Document) return "input";
        if (ct == ControlType.ComboBox || ct == ControlType.List) return "select";
        return "click";
    }

    /// <summary>Traduce el ControlType de UIA al vocabulario que ya usa Graph (nacido del DOM).</summary>
    private static string GraphControlType(ControlType ct, AutomationElement el)
    {
        if (ct == ControlType.Edit) return IsMultiline(el) ? "textarea" : "text";
        if (ct == ControlType.Document) return "textarea";
        if (ct == ControlType.ComboBox || ct == ControlType.List) return "select";
        if (ct == ControlType.CheckBox) return "checkbox";
        if (ct == ControlType.RadioButton) return "radio";
        return "button";
    }

    private static bool IsMultiline(AutomationElement el)
    {
        try
        {
            return el.TryGetCurrentPattern(TextPattern.Pattern, out _) &&
                   !el.Current.BoundingRectangle.IsEmpty &&
                   el.Current.BoundingRectangle.Height > 40;
        }
        catch { return false; }
    }

    // ── Ejecución ────────────────────────────────────────────────────────────

    public bool Execute(PlanStep step, out string error)
    {
        error = "";

        // Tecla de acción (Enter…): no resuelve un elemento, va al foco. Se maneja antes de la resolución.
        if (string.Equals(step.ActionType, "key", StringComparison.OrdinalIgnoreCase))
            return SendKey(step, out error);

        // Scroll: la rueda al panel bajo la posición grabada, con el mismo delta → mismo punto.
        if (string.Equals(step.ActionType, "scroll", StringComparison.OrdinalIgnoreCase))
            return DoScroll(step, out error);

        // flexible: el valor/elemento exacto no importa (ej. "la pestaña nueva"). Best-effort: si no
        // resuelve, el step se salta sin romper el workflow. Ver doc coincidencia-superficie-estado.
        bool flexible = string.Equals(step.ValueMode, "flexible", StringComparison.OrdinalIgnoreCase);
        var candidates = new List<string> { step.Selector };
        candidates.AddRange(step.AlternativeTargets().Where(UiaSelector.Owns));

        L($"Execute «{step.Label}» · {step.ActionType} · valueMode={step.ValueMode ?? "-"}{(flexible ? " (flexible)" : "")} · foreground='{Identity().Url}'");
        L($"  selectores candidatos: [{string.Join(" | ", candidates.Where(UiaSelector.Owns))}]");

        // Las UIs tardan en pintarse (a veces poco, a veces un poco más): si el elemento no aparece a la
        // primera, se reintenta unos milisegundos antes de rendirse. No son segundos a propósito — la
        // espera larga de apps que abren en frío es trabajo del navegador/alineación, no de cada paso.
        AutomationElement? el = null;
        string hitSelector = "";
        int attempts = 0;
        for (int attempt = 0; attempt < 5 && el == null; attempt++)
        {
            attempts = attempt + 1;
            if (attempt > 0) Thread.Sleep(200);
            foreach (string sel in candidates.Where(UiaSelector.Owns))
            {
                el = Resolve(sel);
                if (el != null) { hitSelector = sel; break; }
            }
        }

        if (el == null)
        {
            // Fallback por POSICIÓN: si es un clic y grabamos dónde ocurrió, se clickea ahí (relativo a la
            // ventana). Salva los paneles SAP con id volátil que nunca resuelven por selector.
            if (string.Equals(step.ActionType, "click", StringComparison.OrdinalIgnoreCase) &&
                step.ClickPos() is { } rel)
            {
                var (winL, winT) = WindowOrigin();
                int x = winL + rel.RelX, y = winT + rel.RelY;
                L($"  selector no resolvió → fallback por POSICIÓN: clic en ({x},{y}) [ventana+({rel.RelX},{rel.RelY})]");
                try
                {
                    SmoothMove(x, y);
                    Thread.Sleep(20);
                    mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero);
                    mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero);
                    return true;
                }
                catch (Exception e) { error = $"fallback por posición falló: {e.Message}"; }
            }

            L($"  ✗ NO resuelto tras {attempts} intento(s) · {(flexible ? "flexible → se salta (ok)" : "falla")}");
            if (flexible) { error = ""; return true; }
            error = $"no se encontró el elemento «{step.Label}» ({step.Selector})";
            return false;
        }

        L($"  ✓ resuelto en {attempts} intento(s) con '{hitSelector}' → name='{Safe(() => el.Current.Name)}' ct={Safe(() => el.Current.ControlType.ProgrammaticName)} rect={Safe(() => el.Current.BoundingRectangle.ToString())} ventana='{WindowLabel(el)}'");

        return Actuar(el, step, flexible, out error);
    }

    /// <summary>
    /// Actúa sobre un elemento QUE YA SE TIENE EN LA MANO, sin volver a buscarlo por selector.
    ///
    /// Quien lee la pantalla se queda con el <see cref="AutomationElement"/> exacto; volver a
    /// resolverlo por nombre es un rodeo que puede fallar aunque el elemento siga ahí — y fallaba:
    /// el lector encontraba «Buscar en Notas» (Edit) y el ejecutor no lo resolvía ni en cinco
    /// intentos, así que el asistente veía la barra de búsqueda y no podía pulsarla (2026-08-05).
    ///
    /// Esto NO es pulsar por coordenadas: es pulsar EXACTAMENTE el elemento que se vio, que es la
    /// forma más fuerte de acción por identidad que hay — no hay nombre que pueda quedarse a medias
    /// ni homónimo que confunda, porque no se busca nada.
    /// </summary>
    public bool EjecutarSobre(AutomationElement el, PlanStep step, out string error)
    {
        L($"Ejecutar directo «{step.Label}» · {step.ActionType} · sobre el elemento ya leído "
          + $"(name='{Safe(() => el.Current.Name)}' ct={Safe(() => el.Current.ControlType.ProgrammaticName)})");

        // SOBRE UNA VENTANA TAPADA NO SE ACTÚA. Con la ventana detrás, UIA no da punto pulsable
        // —dice, con razón, que está tapado— y SetFocus no agarra: el foco se queda donde estaba.
        // Eso produjo lo peor que puede pasar aquí: se pulsó la barra de búsqueda del explorador,
        // se dio por hecha, y el texto siguiente acabó en la barra de direcciones de Chrome, que
        // sí tenía el foco, y navegó (2026-08-05). Se sube la ventana ANTES y se comprueba.
        IntPtr win = TopLevelWindow(el);
        if (win != IntPtr.Zero && !TraerAlFrente(win))
            L("    NO se pudo traer la ventana al frente; se actúa igual, pero puede no agarrar");

        return Actuar(el, step, flexible: false, out error);
    }

    /// <summary>
    /// Sube una ventana al primer plano DE VERDAD, y dice si lo consiguió. La única forma de
    /// hacerlo: quien la necesite, que llame aquí.
    /// </summary>
    /// <remarks>
    /// <c>SetForegroundWindow</c> a secas devuelve éxito y no hace nada cuando el proceso que llama
    /// no es el que está delante: Windows lo impide a propósito para que ninguna app te robe la
    /// ventana de las manos. La salida documentada es engancharse a la cola de entrada del hilo que
    /// SÍ manda (<c>AttachThreadInput</c>) y desengancharse enseguida — compartir cola de entrada
    /// más de lo necesario es pedir un bloqueo.
    ///
    /// Vive en esta capa, y no arriba, porque la necesitan las dos: la alineación de apps y la
    /// ejecución sobre un elemento. Estaba escrita dos veces y solo una tenía el enganche; la otra
    /// fallaba en silencio. Una pregunta con dos respuestas se desincroniza siempre.
    ///
    /// Se comprueba mirando quién está delante DESPUÉS, no el valor devuelto: aceptado no es
    /// ejecutado.
    /// </remarks>
    public static bool TraerAlFrente(IntPtr win)
    {
        if (win == IntPtr.Zero) return false;
        if (GetForegroundWindow() == win) return true;
        try
        {
            // Restaurar SOLO si está minimizada: SW_RESTORE sobre una maximizada la encoge, y
            // enfocar una app no debería cambiarle el tamaño a nadie.
            if (IsIconic(win)) ShowWindow(win, SW_RESTORE);

            uint mio = GetCurrentThreadId();
            uint suyo = GetWindowThreadProcessId(GetForegroundWindow(), out _);
            bool enganchado = suyo != 0 && mio != suyo && AttachThreadInput(mio, suyo, true);
            try { SetForegroundWindow(win); BringWindowToTop(win); }
            finally { if (enganchado) AttachThreadInput(mio, suyo, false); }

            for (int i = 0; i < 12; i++)
            {
                if (GetForegroundWindow() == win) return true;
                Thread.Sleep(40);
            }
            return false;
        }
        catch { return false; }
    }

    private bool Actuar(AutomationElement el, PlanStep step, bool flexible, out string error)
    {
        error = "";
        try
        {
            bool ok = step.ActionType switch
            {
                "input" => SetValue(el, step.Value ?? "", out error),
                "select" => Select(el, step.SelectedValue ?? step.Value ?? "", out error),
                "click" => RealClick(el, out error),
                // AÑADIR a la selección sin perder lo ya seleccionado. Se hace con el patrón de
                // UIA y no manteniendo Ctrl pulsado: un modificador es estado global del teclado
                // —si algo falla entre medias se queda hundido y todo lo posterior sale mal—,
                // mientras que AddToSelection se lo pide al control y punto.
                "addselect" => AddToSelection(el, out error),
                // Doble clic: en una LISTA, un clic selecciona y solo el doble abre. Sin esto el
                // recorrido se quedaba en el panel de navegación —donde un clic sí navega— y jamás
                // entraba en una subcarpeta: la superficie no cambiaba, así que se concluía «acción
                // local, nada que aprender». No era falta de criterio, era falta de esta acción.
                "doubleclick" => RealDoubleClick(el, out error),
                _ => Fail($"actionType no soportado en UIA: {step.ActionType}", out error),
            };
            L($"  resultado acción: ok={ok}{(ok ? "" : $" · motivo='{error}'")}");
            if (!ok && flexible) { L("  flexible → se salta pese al fallo (ok)"); error = ""; return true; }
            return ok;
        }
        catch (Exception e)
        {
            L($"  ✗ excepción ejecutando: {e.Message}");
            error = $"UIA falló ejecutando «{step.Label}»: {e.Message}";
            return false;
        }
    }

    private static bool Fail(string reason, out string error) { error = reason; return false; }

    /// <summary>
    /// Envía una tecla de acción al elemento con FOCO. Soporta todo <see cref="ActionKeys"/> (Enter,
    /// Tab, Esc, flechas, Home/End, Re/Av Pág, Supr, Retroceso y F1–F12).
    ///
    /// El paso puede traer un repetidor en <c>Value</c> (<c>"Down x7"</c>): navegar una lista con
    /// flechas son N pulsaciones iguales, y grabarlas como N pasos llenaría el workflow de ruido.
    /// Las teclas EXTENDIDAS (flechas, Inicio/Fin, Re/Av Pág, Supr) necesitan su flag: sin él, Windows
    /// las entrega como las del teclado numérico y el destino recibe otra cosa.
    /// </summary>
    private bool SendKey(PlanStep step, out string error)
    {
        error = "";
        string raw = (step.Value ?? step.Label ?? "").Trim();
        (string key, int times) = ParseKeySpec(raw);

        if (key.Equals("Return", StringComparison.OrdinalIgnoreCase)) key = "Enter";
        if (!KeyCodes.TryGetValue(key, out byte vk))
        {
            error = $"tecla no soportada: «{raw}»";
            return false;
        }

        L($"SendKey «{key}»{(times > 1 ? $" x{times}" : "")} al foco '{Identity().Url}'");
        uint extended = IsExtendedKey(vk) ? KEYEVENTF_EXTENDEDKEY : 0;
        for (int i = 0; i < times; i++)
        {
            keybd_event(vk, 0, extended, IntPtr.Zero);
            keybd_event(vk, 0, extended | KEYEVENTF_KEYUP, IntPtr.Zero);
            if (times > 1) Thread.Sleep(25); // que el destino procese cada pulsación como una de verdad
        }
        return true;
    }

    /// <summary>"Down x7" → ("Down", 7). Sin repetidor, ("Down", 1). El contador se acota para que un
    /// valor corrupto no deje la app tecleando miles de veces contra el SAP de un cliente.</summary>
    private static (string Key, int Times) ParseKeySpec(string raw)
    {
        int x = raw.LastIndexOf(" x", StringComparison.OrdinalIgnoreCase);
        if (x > 0 && int.TryParse(raw[(x + 2)..].Trim(), out int n) && n > 0)
            return (raw[..x].Trim(), Math.Clamp(n, 1, 200));
        return (raw, 1);
    }

    private static bool IsExtendedKey(byte vk) =>
        vk is 0x25 or 0x26 or 0x27 or 0x28    // flechas
           or 0x24 or 0x23                     // Inicio / Fin
           or 0x21 or 0x22                     // Re Pág / Av Pág
           or 0x2E;                            // Supr

    /// <summary>
    /// Reproduce un scroll: mueve el cursor al panel grabado (posición) y envía la rueda con el MISMO
    /// delta total, en trozos de un notch (120) para que el control lo procese como scroll real. "Mismo
    /// punto" = mismo delta desde el inicio (asume que el panel arranca en la misma posición de scroll,
    /// que es el caso al cargar una pantalla; el scroll ABSOLUTO por barra es una mejora posterior).
    /// </summary>
    private bool DoScroll(PlanStep step, out string error)
    {
        error = "";
        (int x, int y) = ParseScrollPos(step.Selector);
        int delta = int.TryParse(step.Value, out int d) ? d : 0;
        L($"DoScroll delta={delta} en ({x},{y})");
        if (delta == 0) return true;

        if (x != 0 || y != 0) { SmoothMove(x, y); Thread.Sleep(30); }
        int notch = delta > 0 ? 120 : -120, remaining = delta, guard = 0;
        while (Math.Abs(remaining) >= 120 && guard++ < 400)
        {
            mouse_event(MOUSEEVENTF_WHEEL, 0, 0, (uint)notch, IntPtr.Zero);
            Thread.Sleep(12);
            remaining -= notch;
        }
        if (remaining != 0) mouse_event(MOUSEEVENTF_WHEEL, 0, 0, (uint)remaining, IntPtr.Zero);
        return true;
    }

    /// <summary>Parsea el selector sintético "scroll:x,y" a coordenadas de pantalla.</summary>
    private static (int, int) ParseScrollPos(string selector)
    {
        try
        {
            int c = (selector ?? "").IndexOf(':');
            string[] xy = selector![(c + 1)..].Split(',');
            return (int.Parse(xy[0]), int.Parse(xy[1]));
        }
        catch { return (0, 0); }
    }

    /// <summary>
    /// Resuelve el selector a un elemento vivo. Antes solo miraba la ventana en primer plano (que al
    /// ejecutar suele ser Ü) y por eso "encontraba" cosas falsas o nada. Ahora: 1) intenta la ventana en
    /// foco (rápido, caso común); 2) si no, barre las ventanas de nivel superior —taskbar, escritorio,
    /// otra app— EXCLUYENDO a Ü, para alcanzar elementos de shell/cross-app. Los selectores por PATH son
    /// relativos a su ventana original, así que solo aplican al intento de la ventana en foco.
    /// </summary>
    /// <param name="foregroundOnly">
    /// true = solo la ventana en primer plano, sin el barrido del escritorio. Lo usa la COMPUERTA
    /// (<see cref="IsStepReady"/>): «listo» tiene que significar listo DONDE se va a clicar. El barrido
    /// sigue disponible para EJECUTAR, que es donde nació como respaldo.
    /// </param>
    /// <summary>
    /// Buscar SOLO en la ventana en foco, sin el barrido por todo el escritorio.
    ///
    /// El barrido nació como respaldo razonable, pero cuando quien llama YA sabe en qué app está
    /// —porque verificó la ubicación antes de actuar— es puro daño: ante un selector que no existe
    /// recorre el árbol completo de cada ventana abierta, y con un navegador con muchas pestañas
    /// eso son MINUTOS. Medido el 2026-08-03: una sola llamada tardó 181 s en fallar, cuando la
    /// respuesta correcta —«ese elemento no está aquí»— era instantánea. Fallar rápido es parte de
    /// ser honesto: un fallo que tarda tres minutos parece un cuelgue.
    /// </summary>
    public bool SoloEnFoco { get; set; }

    private AutomationElement? Resolve(string selector, bool foregroundOnly = false)
    {
        var parts = UiaSelector.Parse(selector);
        bool byPath = parts.TryGetValue("path", out string? raw) && !string.IsNullOrWhiteSpace(raw);
        Condition? condition = byPath ? null : UiaSelector.ConditionFor(parts);
        if (!byPath && condition == null) return null;

        // 1) Ventana en primer plano (si no es la propia Ü).
        IntPtr fg = GetForegroundWindow();
        if (fg != IntPtr.Zero && !IsOwnWindow(fg))
        {
            var hit = FindIn(Root(fg), byPath, raw, condition);
            if (hit != null) { L($"    ✓ '{selector}' en la ventana en foco ('{WindowLabel(hit)}')"); return hit; }
        }

        if (foregroundOnly || SoloEnFoco) return null;

        // 2) Barrido de ventanas de nivel superior (shell/otras apps), saltando a Ü. Solo por condición.
        if (!byPath && condition != null)
        {
            try
            {
                var walker = TreeWalker.ControlViewWalker;
                var child = walker.GetFirstChild(AutomationElement.RootElement);
                while (child != null)
                {
                    if (!IsOwnUi(child))
                    {
                        try { var hit = child.FindFirst(TreeScope.Descendants, condition); if (hit != null) { L($"    ⚠ '{selector}' NO estaba en foco — hallado por barrido en '{WindowLabel(hit)}' (elemento de otra ventana/escritorio; puede estar tapado)"); return hit; } }
                        catch { }
                    }
                    child = walker.GetNextSibling(child);
                }
            }
            catch { }
        }
        return null;
    }

    private static AutomationElement? FindIn(AutomationElement? root, bool byPath, string? raw, Condition? condition)
    {
        if (root == null) return null;
        if (byPath) return ByPath(root, raw!);
        if (condition == null) return null;
        try { return MejorCandidato(root.FindAll(TreeScope.Descendants, condition)); }
        catch { return null; }
    }

    /// <summary>
    /// De todos los elementos que casan con el selector, el que SE PUEDE USAR: visible y con
    /// geometría. Coger el primero era el error.
    ///
    /// Un nombre no es único. En el panel del explorador hay varios «Escritorio» —el de OneDrive,
    /// el anclado— y algunos cuelgan de ramas plegadas, así que existen en el árbol de UIA con
    /// rect vacío. `FindFirst` devolvía uno de esos: sin caja no hay dónde pulsar, se caía al
    /// respaldo `Invoke`, que sobre un TreeItem devuelve true SIN NAVEGAR, y la ruta se rompía
    /// reportando éxito en el tramo (2026-08-02). Preferir lo visible convierte un selector
    /// ambiguo en uno utilizable sin inventarse nada.
    /// </summary>
    private static AutomationElement? MejorCandidato(AutomationElementCollection? hits)
    {
        if (hits == null || hits.Count == 0) return null;
        AutomationElement? primero = null;
        foreach (AutomationElement el in hits)
        {
            primero ??= el;
            try
            {
                var info = el.Current;
                if (info.IsOffscreen) continue;
                var r = info.BoundingRectangle;
                if (r.IsEmpty || r.Width < 1 || r.Height < 1) continue;
                return el;                      // visible y con caja: este sirve
            }
            catch { }
        }
        return primero;                          // ninguno utilizable: el de siempre, y que falle honestamente
    }

    private static AutomationElement? ByPath(AutomationElement root, string raw)
    {
        var node = root;
        foreach (string chunk in raw.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(chunk, out int index)) return null;
            try
            {
                var child = TreeWalker.ControlViewWalker.GetFirstChild(node);
                for (int i = 0; i < index && child != null; i++)
                    child = TreeWalker.ControlViewWalker.GetNextSibling(child);
                if (child == null) return null;
                node = child;
            }
            catch { return null; }
        }
        return node;
    }

    private static bool SetValue(AutomationElement el, string value, out string error)
    {
        error = "";
        if (el.TryGetCurrentPattern(ValuePattern.Pattern, out var p) && p is ValuePattern v)
        {
            if (v.Current.IsReadOnly) { error = "el campo es de solo lectura"; return false; }
            v.SetValue(value);
            // Dejar el FOCO de teclado en el campo recién escrito. SetValue no enfoca, así que sin esto un
            // Enter posterior (keybd_event) iría a otra ventana y no submitearía —era el bug de SAP: se
            // escribía la transacción pero el Enter no navegaba—. Best-effort: si el control no enfoca, ni modo.
            try { el.SetFocus(); } catch { }
            return true;
        }
        error = "el campo no soporta ValuePattern (no se puede escribir por UIA)";
        return false;
    }

    private static bool Select(AutomationElement el, string value, out string error)
    {
        error = "";
        // El valor puede venir como texto de la opción: se busca entre los hijos ListItem.
        try
        {
            var item = el.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
                new PropertyCondition(AutomationElement.NameProperty, value)));

            if (item != null &&
                item.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var sp) &&
                sp is SelectionItemPattern sel)
            {
                sel.Select();
                return true;
            }
        }
        catch { }

        // Algunos combos aceptan el texto directamente.
        if (el.TryGetCurrentPattern(ValuePattern.Pattern, out var p) && p is ValuePattern v && !v.Current.IsReadOnly)
        {
            v.SetValue(value);
            return true;
        }

        error = $"no se pudo seleccionar «{value}»: la opción no existe o el control no lo permite";
        return false;
    }

    /// <summary>Suma este elemento a la selección actual, sin sustituirla.</summary>
    private static bool AddToSelection(AutomationElement el, out string error)
    {
        error = "";
        try
        {
            TraerALaVistaEstatico(el);
            if (el.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var p) && p is SelectionItemPattern sel)
            {
                sel.AddToSelection();
                return true;
            }
            error = "el elemento no admite selección múltiple";
            return false;
        }
        catch (Exception e) { error = e.Message; return false; }
    }

    private static void TraerALaVistaEstatico(AutomationElement el)
    {
        try
        {
            if (el.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var p) && p is ScrollItemPattern si)
            {
                si.ScrollIntoView();
                Thread.Sleep(120);
            }
        }
        catch { }
    }

    /// <summary>
    /// ¿Aquí se puede ESCRIBIR? La única respuesta, para quien pulsa y para quien teclea.
    /// </summary>
    /// <remarks>
    /// Por el nombre del tipo no se sabe, y creerlo costó las dos caras del mismo error:
    ///
    /// · Aceptar de más: se escribía sobre una fila seleccionada del explorador y Windows lo
    ///   entendía como RENOMBRAR — «logo-empresa.png» acabó llamándose «Datos.png», y la respuesta
    ///   fue «✓ escrito» (2026-08-03). Por eso las filas de listas y árboles se rechazan siempre.
    ///
    /// · Aceptar de menos: el buscador de YouTube es un ComboBox, y también la barra de Chrome. Se
    ///   rechazaban por no llamarse «Edit» aunque son exactamente donde se escribe en la web
    ///   (2026-08-05). Un ComboBox de solo selección y uno de búsqueda no se distinguen por el
    ///   tipo — los distingue tener ValuePattern y no ser de solo lectura.
    /// </remarks>
    public static bool AceptaTexto(AutomationElement el)
    {
        try
        {
            var ct = el.Current.ControlType;

            // Una fila de una lista o de un árbol NUNCA acepta texto, tenga el patrón que tenga:
            // escribir ahí no es escribir, es renombrar lo que haya debajo.
            if (ct == ControlType.ListItem || ct == ControlType.TreeItem || ct == ControlType.DataItem)
                return false;

            if (ct == ControlType.Edit || ct == ControlType.Document) return true;

            return el.TryGetCurrentPattern(ValuePattern.Pattern, out var vp)
                   && vp is ValuePattern v && !v.Current.IsReadOnly;
        }
        catch { return false; }
    }

    private static bool Click(AutomationElement el, out string error)
    {
        error = "";

        // SELECCIONAR va antes que INVOCAR en lo que es seleccionable. Un TreeItem o un ListItem
        // expone Invoke por herencia, pero invocarlo devuelve true sin navegar: lo que mueve un
        // árbol o una lista es la SELECCIÓN. Con Invoke primero, un tramo de ruta se daba por
        // bueno sin haber cambiado de pantalla (2026-08-02).
        if (el.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var sip) && sip is SelectionItemPattern selPrim)
        {
            selPrim.Select();
            return true;
        }
        if (el.TryGetCurrentPattern(InvokePattern.Pattern, out var ip) && ip is InvokePattern inv)
        {
            inv.Invoke();
            return true;
        }
        if (el.TryGetCurrentPattern(TogglePattern.Pattern, out var tp) && tp is TogglePattern tog)
        {
            tog.Toggle();
            return true;
        }
        if (el.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var sp) && sp is SelectionItemPattern sel)
        {
            sel.Select();
            return true;
        }

        // EN UN CAMPO DE TEXTO, PULSAR ES ENFOCAR. Un Edit no expone Invoke ni Toggle ni Select —no
        // hay nada que «invocar» en una caja donde se escribe—, así que aquí se daba por imposible
        // y se devolvía «no soporta Invoke/Toggle/Select». Es lo que impedía pulsar la barra de
        // búsqueda del explorador: UIA no le daba punto pulsable (está recogida hasta que se usa) y
        // los patrones de pulsación no le aplican. SetFocus sí, y es exactamente lo que consigue un
        // clic sobre ella (2026-08-05).
        if (AceptaTexto(el))
        {
            try { el.SetFocus(); } catch (Exception e) { error = $"no se pudo enfocar el campo: {e.Message}"; return false; }

            // ACEPTADO NO ES EJECUTADO, y aquí menos que en ninguna parte. SetFocus no protesta
            // cuando el foco acaba en otro sitio, y lo que viene detrás de pulsar un campo es
            // ESCRIBIR: dar por bueno un foco que no llegó no deja un paso fallido, deja el texto
            // metido en la ventana equivocada. Pasó: acabó en la barra de Chrome (2026-08-05).
            for (int i = 0; i < 8; i++)
            {
                try { if (Automation.Compare(AutomationElement.FocusedElement, el)) return true; }
                catch { }
                Thread.Sleep(40);
            }
            error = "pedí el foco al campo y se quedó en otra parte (¿ventana tapada o sin activar?)";
            return false;
        }

        error = "el elemento no soporta Invoke/Toggle/Select";
        return false;
    }

    /// <summary>
    /// Clic REAL: trae al frente la ventana del elemento, mueve el cursor a su centro y hace un clic
    /// físico. Es VISIBLE (ves el mouse moverse) y funciona donde InvokePattern no —iconos de escritorio,
    /// taskbar, Chrome—. Si el elemento no expone una caja usable, cae al Invoke de UIA (invisible pero
    /// mejor que nada). Espejo de cómo se graba: por posición en pantalla.
    /// </summary>
    /// <summary>
    /// Desplaza la lista hasta que el elemento esté visible, antes de intentar pulsarlo.
    ///
    /// Un elemento que existe en el árbol de UIA no está necesariamente en pantalla: en una lista
    /// con scroll —el panel lateral del explorador, sin ir más lejos— los de abajo tienen
    /// coordenadas fuera del área visible. Pulsar ahí no acierta en el elemento: acierta en lo que
    /// haya en ese punto, o en nada, y el recorrido se detiene sin saber por qué (2026-08-01).
    ///
    /// <c>ScrollItemPattern</c> es la forma que UIA da para esto y es la correcta: se le pide al
    /// control que traiga SU elemento a la vista, en lugar de calcular cuánto habría que rodar la
    /// rueda. Coherente con la regla del proyecto: se actúa por identidad, no por geometría.
    /// </summary>
    private void TraerALaVista(AutomationElement el)
    {
        try
        {
            if (el.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var p) && p is ScrollItemPattern si)
            {
                si.ScrollIntoView();
                Thread.Sleep(120);   // el desplazamiento se anima; sin esto se lee la caja de antes
                L("    traído a la vista con ScrollIntoView");
            }
        }
        catch (Exception e) { L($"    ScrollIntoView no pudo: {e.Message}"); }
    }

    /// <summary>¿El punto cae dentro del área visible de la ventana? Sin ventana, se da por bueno.</summary>
    private static bool PuntoDentroDe(IntPtr win, double x, double y)
    {
        if (win == IntPtr.Zero) return true;
        try
        {
            if (!GetWindowRect(win, out RECT w)) return true;
            return x >= w.Left && x <= w.Right && y >= w.Top && y <= w.Bottom;
        }
        catch { return true; }
    }

    /// <summary>
    /// ¿Es contenido de una lista —un archivo, una fila— y no un elemento de navegación?
    ///
    /// La diferencia importa porque en el contenido SELECCIONAR no es ABRIR: en el explorador hace
    /// falta el doble clic de verdad. Se distingue por el contenedor: una lista de contenido cuelga
    /// de un control de lista/rejilla; el menú lateral de una app también, así que además se mira
    /// si el elemento está en la franja izquierda, que es donde vive la navegación.
    /// </summary>
    private static bool EsContenidoDeLista(AutomationElement el)
    {
        try
        {
            if (el.Current.ControlType != ControlType.ListItem) return false;
            IntPtr win = TopLevelWindow(el);
            if (win == IntPtr.Zero || !GetWindowRect(win, out RECT w)) return true;
            double ancho = w.Right - w.Left;
            if (ancho <= 0) return true;

            // Se mide el CONTENEDOR, no el elemento. Mirar dónde cae el elemento —«en el tercio
            // izquierdo = navegación»— clasificaba la PRIMERA COLUMNA de la rejilla de archivos como
            // menú lateral, porque el contenido empieza justo después del panel y su primera columna
            // aún cae dentro de ese tercio. Con la ventana en x=743 una carpeta en x=1039 se tomaba
            // por navegación, se «pulsaba» con Select() y no se entraba nunca en ella (2026-08-03).
            // El contenedor no tiene esa ambigüedad: un panel de navegación es estrecho y una vista
            // de contenido ocupa el grueso de la ventana.
            var padre = System.Windows.Automation.TreeWalker.ControlViewWalker.GetParent(el);
            for (int i = 0; i < 6 && padre != null; i++)
            {
                var ct = padre.Current.ControlType;
                if (ct == ControlType.Tree) return false;               // árbol = navegación, siempre
                if (ct == ControlType.List || ct == ControlType.DataGrid)
                {
                    var c = padre.Current.BoundingRectangle;
                    if (c.IsEmpty || c.Width < 1) break;
                    return c.Width > ancho * 0.5;                        // ancho = contenido; estrecho = menú
                }
                padre = System.Windows.Automation.TreeWalker.ControlViewWalker.GetParent(padre);
            }

            // Sin contenedor identificable se vuelve al criterio antiguo, que al menos acierta
            // en los menús laterales claramente separados del contenido.
            var r = el.Current.BoundingRectangle;
            return r.Left > w.Left + ancho / 3.0;
        }
        catch { return true; }
    }

    private bool RealClick(AutomationElement el, out string error, bool permitirSelect = true)
    {
        error = "";
        try
        {
            TraerALaVista(el);

            // SELECCIONAR ANTES QUE PULSAR en lo que es seleccionable y NO es contenido de lista.
            // El menú de Configuración ignora el ratón sintético —clic simple y doble, ambos con
            // ok=True, la página no se movía— pero responde a SelectionItemPattern.Select() al
            // instante: se pulsó «Personalización» doce veces sin salir de «Inicio», y un Select()
            // la abrió a la primera (2026-08-03). Muchas apps WinUI son así.
            //
            // El contenido de una lista queda fuera a propósito: ahí seleccionar NO es abrir, y
            // confundirlos rompería el explorador, donde hace falta el doble clic de verdad.
            // …PERO SELECCIONAR NO ES ABRIR EN UN ÁRBOL DE NAVEGACIÓN, y esta rama se lo tragaba.
            //
            // El panel lateral del explorador son TreeItem con SelectionItemPattern, así que entraban
            // aquí: Select() marcaba la entrada, NO navegaba, y se devolvía éxito. El 2026-08-08 se
            // vio dos veces seguidas —«Descargas» y «Escritorio»—: el elemento se resolvía al primer
            // intento, se «pulsaba», y acto seguido «pulsé X pero no se llegó». Un `map_go_to` de
            // tres tramos moría en el primero. Aceptado no es ejecutado, otra vez.
            //
            // El orden se INVIERTE y se verifica, que es lo que permite conservar los dos casos:
            // primero el clic real —que en el explorador navega—, y solo si NO consiguió seleccionar
            // se recurre a Select(). El menú de Configuración sigue funcionando porque allí el ratón
            // sintético no selecciona nada, así que el respaldo entra igual; el explorador funciona
            // porque el clic real hace las dos cosas a la vez.
            bool seleccionable = permitirSelect
                && !EsContenidoDeLista(el)
                && el.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var sp0)
                && sp0 is SelectionItemPattern;

            bool RespaldoSelect()
            {
                if (!seleccionable) return false;
                try
                {
                    if (!el.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var sp1)
                        || sp1 is not SelectionItemPattern sel) return false;
                    if (sel.Current.IsSelected)
                    {
                        L("    → el clic real ya lo dejó seleccionado; no hace falta el patrón");
                        return true;
                    }
                    sel.Select();
                    L("    → Select() por patrón: el clic real no agarró (app que ignora el ratón sintético)");
                    return true;
                }
                catch (Exception e) { L($"    Select por patrón falló ({e.Message})"); return false; }
            }

            var r = el.Current.BoundingRectangle;
            if (!r.IsEmpty && !double.IsInfinity(r.Width) && r.Width >= 1 && r.Height >= 1)
            {
                IntPtr win = TopLevelWindow(el);
                if (win != IntPtr.Zero) { try { SetForegroundWindow(win); } catch { } }
                Thread.Sleep(40); // dar tiempo a que la ventana suba antes de comprobar visibilidad

                // La caja se relee DESPUÉS de desplazar: si el elemento se movió al traerlo a la
                // vista, la de antes apunta a donde ya no está.
                r = el.Current.BoundingRectangle;
                if (r.IsEmpty || r.Width < 1 || r.Height < 1)
                {
                    L("    RealClick: tras desplazar sigue sin caja usable → Invoke por UIA");
                    return Click(el, out error);
                }

                // DÓNDE se puede pulsar lo dice UIA, no nuestra aritmética. GetClickablePoint
                // devuelve un punto realmente alcanzable —contando recorte, scroll y solapes— o
                // lanza si el elemento no está a la vista. Calcularlo a mano, comparando el centro
                // de la caja contra GetWindowRect del contenedor, rechazaba clics BUENOS: un
                // TabItem en (549,233), perfectamente visible, se descartaba y caía a Invoke, que
                // sobre una pestaña no navega. Eso dejó el recorrido clavado en profundidad 1
                // (2026-08-01). La comprobación era correcta en intención y falsa en implementación.
                double cx, cy;
                try
                {
                    var punto = el.GetClickablePoint();
                    cx = punto.X; cy = punto.Y;
                }
                catch (NoClickablePointException)
                {
                    L("    RealClick: UIA dice que no hay punto pulsable (tapado o fuera de vista) → Select/Invoke");
                    if (Click(el, out error)) return true;
                    L($"    identidad tampoco pudo ({error}); no se pulsa a ciegas");
                    return false;
                }
                catch
                {
                    // Sin soporte para el punto pulsable: el centro de la caja, como siempre.
                    cx = r.Left + r.Width / 2; cy = r.Top + r.Height / 2;
                }
                L($"    RealClick: rect={r} punto=({(int)cx},{(int)cy}) ventana='{WindowLabel(el)}'{(IsDesktopWindow(win) ? " (ESCRITORIO)" : "")}");
                L($"    → clic físico en ({(int)cx},{(int)cy})");
                SmoothMove((int)cx, (int)cy);
                Thread.Sleep(20);
                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero);
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero);

                // ¿AGARRÓ? Solo se puede preguntar en lo seleccionable, y ahí basta: si tras el clic
                // real el elemento no quedó seleccionado, esta app ignora el ratón sintético y hay
                // que pedírselo por patrón. Se da un respiro para que el control procese el clic.
                if (seleccionable) { Thread.Sleep(120); RespaldoSelect(); }
                return true;
            }
            L($"    RealClick: sin caja usable (rect={r}) → respaldo a Invoke por UIA (INVISIBLE)");
        }
        catch (Exception e) { L($"    RealClick excepción: {e.Message} → respaldo a Invoke"); error = e.Message; }

        // Sin caja usable → respaldo a la invocación por UIA.
        bool invoked = Click(el, out error);
        L($"    Invoke UIA: ok={invoked}{(invoked ? "" : $" · {error}")}");
        return invoked;
    }

    // ── Ayudas de logging (no cambian comportamiento) ─────────────────────────
    private static string Safe(Func<object?> f) { try { return f()?.ToString() ?? ""; } catch { return "?"; } }

    /// <summary>Etiqueta corta de la ventana de un elemento, para logs: su título, o (escritorio)/(sin título).</summary>
    private static string WindowLabel(AutomationElement? el)
    {
        if (el == null) return "(nada)";
        try
        {
            IntPtr h = TopLevelWindow(el);
            if (h == IntPtr.Zero) return "?";
            var sb = new System.Text.StringBuilder(120);
            GetWindowText(h, sb, sb.Capacity);
            if (sb.Length > 0) return sb.ToString();
            return IsDesktopWindow(h) ? "(escritorio)" : "(sin título)";
        }
        catch { return "?"; }
    }

    /// <summary>La ventana (hwnd) que contiene al elemento, subiendo hasta el primer ancestro con handle.</summary>
    /// <summary>
    /// La ventana de NIVEL SUPERIOR del elemento — la que tiene barra de título y representa la
    /// pantalla.
    ///
    /// El primer ancestro con handle NO sirve para esto: dentro del panel lateral del explorador es
    /// una ventana hija (SysTreeView32, DirectUIHWND). Dar el foco a esa hija hacía que
    /// <c>GetForegroundWindow</c> devolviera la hija, cuyo título es el nombre del panel; el
    /// localizador lo vetaba por no identificar nada y la superficie se quedaba clavada en
    /// «uia://explorer.exe/ventana» durante todo el mapeo. Como los destinos sin identidad se
    /// descartan, NINGUNA transición se confirmaba y el grafo no crecía (2026-08-01). Un fallo de
    /// una línea que parecía tres problemas distintos.
    /// </summary>
    private static IntPtr TopLevelWindow(AutomationElement el)
    {
        IntPtr h = ContenedorDe(el);
        if (h == IntPtr.Zero) return IntPtr.Zero;
        try { IntPtr raiz = GetAncestor(h, GA_ROOT); return raiz != IntPtr.Zero ? raiz : h; }
        catch { return h; }
    }

    /// <summary>
    /// La ventana que CONTIENE al elemento, sea hija o no. Para recortar: lo que decide si un punto
    /// está a la vista es el panel donde vive, no la ventana entera.
    /// </summary>
    private static IntPtr ContenedorDe(AutomationElement el)
    {
        try
        {
            var node = el;
            while (node != null)
            {
                int h = node.Current.NativeWindowHandle;
                if (h != 0) return new IntPtr(h);
                node = TreeWalker.ControlViewWalker.GetParent(node);
            }
        }
        catch { }
        return IntPtr.Zero;
    }

    // ── Observación ──────────────────────────────────────────────────────────

    /// <summary>
    /// Empieza a grabar. Dos vías complementarias:
    ///   • CLICS — un hook global de mouse (WH_MOUSE_LL). En cada clic izquierdo se resuelve el elemento
    ///     bajo el cursor con UIA (FromPoint) y se publica. Es fiable (capta CUALQUIER clic, no solo los
    ///     controles que disparan InvokedEvent), cross-app (hook global + coordenadas de pantalla) y NO
    ///     depende de que el inspector esté encendido.
    ///   • TECLEO/SELECCIÓN — eventos de propiedad de UIA (ValueProperty/Toggle/SelectionItem) anclados a
    ///     la ventana en foco; se RE-ANCLAN al cambiar de ventana (ReanchorValueEvents) para seguir al
    ///     usuario por la app / entre apps.
    /// En ambas vías se excluye la propia UI de Ü: sus botones (Enseñar/Detener, la carita…) nunca son
    /// parte de un workflow.
    ///
    /// LÍMITE CONOCIDO: el tecleo llega por ValueProperty como cambio ya consolidado, no por tecla.
    /// </summary>
    public void StartObserving()
    {
        lock (_gate)
        {
            if (_observing) return;

            _hookProc = HookCallback; // guardar la referencia viva: el hook la usa por siempre
            _hook = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, GetModuleHandle(null), 0);
            _keyHookProc = KeyHookCallback;
            _keyHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyHookProc, GetModuleHandle(null), 0);
            _scrollTimer = new System.Threading.Timer(_ => FlushScroll(), null,
                System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);

            // Ancla inicial del tecleo a la ventana objetivo actual (tras el countdown, el foreground ya
            // es la app a enseñar). Si aún es Ü, se anclará en el primer clic real sobre la app.
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd != IntPtr.Zero && !IsOwnWindow(hwnd)) AttachValueEvents(hwnd);

            _observing = true;
        }
    }

    public void StopObserving()
    {
        lock (_gate)
        {
            if (_hook != IntPtr.Zero) { try { UnhookWindowsHookEx(_hook); } catch { } _hook = IntPtr.Zero; }
            if (_keyHook != IntPtr.Zero) { try { UnhookWindowsHookEx(_keyHook); } catch { } _keyHook = IntPtr.Zero; }
            _hookProc = null;
            _keyHookProc = null;
            FlushScroll(); // publicar el último scroll pendiente antes de cerrar
            _scrollTimer?.Dispose();
            _scrollTimer = null;
            DetachValueEvents();
            _valueRootHwnd = IntPtr.Zero;
            _observing = false;
        }
    }

    /// <summary>El hook corre en el hilo que lo instaló: devolver YA y hacer el trabajo UIA en un hilo de
    /// fondo, para no ahogar el input de todo el sistema.</summary>
    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int msg = (int)wParam;
            if (msg == WM_LBUTTONDOWN)
            {
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                int px = data.pt.X, py = data.pt.Y;
                Task.Run(() => OnHookClick(px, py));
            }
            else if (msg == WM_MOUSEWHEEL)
            {
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                short delta = (short)(data.mouseData >> 16); // el delta va en el word alto de mouseData
                bool firstNotch;
                lock (_scrollGate)
                {
                    firstNotch = _scrollAccum == 0;
                    _scrollAccum += delta; _scrollX = data.pt.X; _scrollY = data.pt.Y;
                }
                // El nodo del gesto se captura al PRIMER notch (en un hilo de fondo: el hook no puede
                // hacer trabajo UIA), no al publicar 350 ms después — para entonces el scroll pudo
                // haber cambiado lo que hay delante.
                if (firstNotch) Task.Run(CaptureScrollNode);
                _scrollTimer?.Change(350, System.Threading.Timeout.Infinite); // debounce: publica al parar
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    /// <summary>Publica el scroll acumulado como UN paso, anclado a la posición del panel. Se llama por el
    /// debounce (al dejar de deslizar) y al detener la grabación (para no perder el último gesto).</summary>
    /// <summary>Nodo capturado al primer notch del gesto de scroll (bajo _scrollGate).</summary>
    private string _scrollNodeUrl = "";
    private string _scrollNodeReadiness = "";

    private void CaptureScrollNode()
    {
        string url = SafeIdentityUrl();
        string readiness = SafeCachedReadiness(url);
        lock (_scrollGate) { _scrollNodeUrl = url; _scrollNodeReadiness = readiness; }
    }

    private void FlushScroll()
    {
        int delta, x, y;
        string nodeUrl, nodeReadiness;
        lock (_scrollGate)
        {
            delta = _scrollAccum; x = _scrollX; y = _scrollY; _scrollAccum = 0;
            nodeUrl = _scrollNodeUrl; nodeReadiness = _scrollNodeReadiness;
            _scrollNodeUrl = ""; _scrollNodeReadiness = "";
        }
        if (delta == 0) return;
        if (IsOwnWindow(GetForegroundWindow())) return; // scroll dentro de Ü no es parte del workflow

        var step = new ObservedStep(
            ActionType: "scroll",
            Selector: $"scroll:{x},{y}",   // el panel a scrollear va por posición (la rueda va al foco bajo el cursor)
            Label: delta < 0 ? "Scroll abajo" : "Scroll arriba",
            ControlType: "scroll",
            Value: delta.ToString(),       // delta total (múltiplos de 120 por notch)
            AllowedOptions: null,
            SelectedValue: null,
            SelectedLabel: null,
            SurfaceSection: null,
            AlternativeTargets: Array.Empty<string>())
        {
            // El nodo del PRIMER notch; si la captura de fondo no llegó a tiempo, se lee ahora.
            Surface = nodeUrl.Length > 0 ? nodeUrl : Identity().Url,
            Readiness = nodeReadiness.Length > 0 ? nodeReadiness : CachedReadinessCount().ToString(),
        };
        L($"observado: scroll delta={delta} en ({x},{y}) · '{step.Surface}'");
        try { StepObserved?.Invoke(this, step); } catch { }
    }

    private void OnHookClick(int px, int py)
    {
        // El NODO del paso se captura AQUÍ, lo primero de todo: si el clic navega (un menú, un
        // botón "Siguiente"), FromPoint/LabelOf/SelectorsFor tardan decenas de ms de trabajo UIA y
        // la identidad leída DESPUÉS ya sería la pantalla NUEVA — el paso quedaría grabado con el
        // nodo equivocado, y al ejecutar, la compuerta de ubicación esperaría una pantalla en la
        // que este paso nunca ocurrió.
        string nodeUrl = SafeIdentityUrl();
        string nodeReadiness = SafeCachedReadiness(nodeUrl);

        AutomationElement? el;
        try { el = AutomationElement.FromPoint(new System.Windows.Point(px, py)); }
        catch { return; }
        if (el == null || IsOwnUi(el)) return; // los clics sobre la propia UI de Ü no son un paso

        // Posición del clic RELATIVA a la ventana: fallback para elementos sin selector estable (paneles
        // SAP con id volátil, sin nombre). Tras un scroll al mismo punto, la fila está en el mismo lugar.
        var (winL, winT) = WindowOrigin();
        Publish(el, "click", null, $"{px - winL},{py - winT}", nodeUrl, nodeReadiness);
        ReanchorValueEvents(); // seguir el tecleo en la ventana recién activada
    }

    /// <summary>Identity().Url sin excepciones — para el snapshot del nodo al entrar al hook.</summary>
    private string SafeIdentityUrl()
    {
        try { return Identity().Url; } catch { return ""; }
    }

    /// <summary>Meta de carga cacheada bajo la URL YA capturada (no se relee la identidad: eso
    /// reabriría la carrera que el snapshot cierra).</summary>
    private string SafeCachedReadiness(string url)
    {
        try { return CachedReadinessCount(url).ToString(); } catch { return ""; }
    }

    /// <summary>Esquina superior-izquierda de la ventana real en foco (para coordenadas relativas).</summary>
    private static (int L, int T) WindowOrigin()
    {
        try { if (GetWindowRect(RealForegroundWindow(), out RECT r)) return (r.Left, r.Top); } catch { }
        return (0, 0);
    }

    /// <summary>
    /// Teclas de ACCIÓN que se graban y se saben reproducir. El tecleo de TEXTO no entra aquí: ese llega
    /// por ValueProperty, y grabarlo letra a letra duplicaría cada campo.
    ///
    /// Las FLECHAS están porque son la forma de navegar controles que no se dejan accionar de otro modo
    /// —un árbol o una tabla donde el elemento no resuelve— y hasta ahora el botón enseñar era ciego a
    /// ellas: el usuario las pulsaba, no pasaba nada, y el workflow quedaba incompleto sin avisar.
    /// Las F1–F12 están porque en SAP son la interfaz de verdad (F3 volver, F8 ejecutar…).
    /// </summary>
    private static readonly Dictionary<int, string> ActionKeys = new()
    {
        [0x0D] = "Enter",     [0x09] = "Tab",      [0x1B] = "Esc",
        [0x25] = "Left",      [0x26] = "Up",       [0x27] = "Right",   [0x28] = "Down",
        [0x24] = "Home",      [0x23] = "End",      [0x21] = "PageUp",  [0x22] = "PageDown",
        [0x2E] = "Delete",    [0x08] = "Backspace",
        [0x70] = "F1", [0x71] = "F2", [0x72] = "F3", [0x73] = "F4",
        [0x74] = "F5", [0x75] = "F6", [0x76] = "F7", [0x77] = "F8",
        [0x78] = "F9", [0x79] = "F10", [0x7A] = "F11", [0x7B] = "F12",
    };

    /// <summary>Nombre → código, para reproducir. Se construye del mapa de arriba: una sola fuente.</summary>
    private static readonly Dictionary<string, byte> KeyCodes =
        ActionKeys.ToDictionary(kv => kv.Value, kv => (byte)kv.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>Hook de teclado: graba las teclas de ACCIÓN (<see cref="ActionKeys"/>), no el texto.</summary>
    private IntPtr KeyHookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && (int)wParam == WM_KEYDOWN)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (ActionKeys.TryGetValue((int)data.vkCode, out string? name))
                Task.Run(() => PublishKey(name));
        }
        return CallNextHookEx(_keyHook, code, wParam, lParam);
    }

    /// <summary>Graba un paso "key", anclado al FOCO (no a un elemento). Se excluye la UI de Ü.</summary>
    private void PublishKey(string keyName)
    {
        // Nodo capturado ANTES de nada: un Enter navega, y si la identidad se leyera después del
        // round-trip el paso quedaría grabado en la pantalla de DESTINO en vez de donde se pulsó.
        string nodeUrl = SafeIdentityUrl();
        string nodeReadiness = SafeCachedReadiness(nodeUrl);

        if (IsOwnWindow(GetForegroundWindow())) return; // una tecla dentro de Ü no es parte del workflow
        var step = new ObservedStep(
            ActionType: "key",
            Selector: $"key:{keyName.ToLowerInvariant()}", // sintético: va al foco, no a un elemento resuelto
            Label: keyName,
            ControlType: "key",
            Value: keyName,
            AllowedOptions: null,
            SelectedValue: null,
            SelectedLabel: null,
            SurfaceSection: null,
            AlternativeTargets: Array.Empty<string>())
        {
            Surface = nodeUrl,
            Readiness = nodeReadiness,
        };
        L($"observado: tecla {keyName} en '{step.Surface}'");
        try { StepObserved?.Invoke(this, step); } catch { }
    }

    /// <summary>Re-ancla la escucha de cambios de valor a la ventana en foco si cambió (y no es Ü).</summary>
    private void ReanchorValueEvents()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero || hwnd == _valueRootHwnd || IsOwnWindow(hwnd)) return;
        lock (_gate)
        {
            DetachValueEvents();
            AttachValueEvents(hwnd);
        }
    }

    private void AttachValueEvents(IntPtr hwnd)
    {
        var root = Root(hwnd);
        if (root == null) return;
        _propertyChanged = OnPropertyChanged;
        try
        {
            Automation.AddAutomationPropertyChangedEventHandler(
                root, TreeScope.Subtree, _propertyChanged,
                ValuePattern.ValueProperty, TogglePattern.ToggleStateProperty,
                SelectionItemPattern.IsSelectedProperty);
            _valueRoot = root;
            _valueRootHwnd = hwnd;
        }
        catch { _valueRoot = null; _valueRootHwnd = IntPtr.Zero; }
    }

    private void DetachValueEvents()
    {
        try
        {
            if (_propertyChanged != null && _valueRoot != null)
                Automation.RemoveAutomationPropertyChangedEventHandler(_valueRoot, _propertyChanged);
        }
        catch { /* la ventana pudo morir antes que nosotros */ }
        _propertyChanged = null;
        _valueRoot = null;
    }

    private void OnPropertyChanged(object? sender, AutomationPropertyChangedEventArgs e)
    {
        if (sender is not AutomationElement el) return;

        if (e.Property == ValuePattern.ValueProperty)
            Publish(el, "input", e.NewValue as string);
        else if (e.Property == TogglePattern.ToggleStateProperty)
            Publish(el, "click", e.NewValue?.ToString());
        else if (e.Property == SelectionItemPattern.IsSelectedProperty && e.NewValue is true)
            Publish(el, "select", el.Current.Name);
    }

    /// <summary>¿El elemento pertenece a la propia app Ü? Sus controles nunca son parte de un workflow.</summary>
    private static bool IsOwnUi(AutomationElement el)
    {
        try { using var p = Process.GetProcessById(el.Current.ProcessId); return IsOwnProcName(p.ProcessName); }
        catch { return false; }
    }

    private static bool IsOwnWindow(IntPtr hwnd)
    {
        try { GetWindowThreadProcessId(hwnd, out uint pid); using var p = Process.GetProcessById((int)pid); return IsOwnProcName(p.ProcessName); }
        catch { return false; }
    }

    private static bool IsOwnProcName(string proc) => proc.Equals("U", StringComparison.OrdinalIgnoreCase);

    private void Publish(AutomationElement el, string actionType, string? value, string clickPos = "",
        string? nodeUrl = null, string? nodeReadiness = null)
    {
        if (IsOwnUi(el)) return; // choke point: nunca grabar la UI de Ü (venga de clic o de cambio de valor)
        ObservedStep step;
        try
        {
            var info = el.Current;
            string label = LabelOf(el, info);
            if (string.IsNullOrWhiteSpace(label)) return;

            string ct = ControlTypeName(info.ControlType);
            var selectors = SelectorsFor(info, new List<int>(), ct);
            if (selectors.Count == 0) return;

            step = new ObservedStep(
                ActionType: actionType,
                Selector: selectors[0],
                Label: label,
                ControlType: GraphControlType(info.ControlType, el),
                Value: value,
                AllowedOptions: null,
                SelectedValue: actionType == "select" ? value : null,
                SelectedLabel: actionType == "select" ? value : null,
                SurfaceSection: null,
                AlternativeTargets: selectors.Skip(1).ToList())
            {
                // El NODO del paso: dónde estaba parado el usuario AL HACERLO. Los clics lo traen ya
                // capturado desde la entrada del hook (antes del trabajo UIA, que en un clic que navega
                // tarda lo bastante como para leer la pantalla NUEVA); los cambios de valor no navegan,
                // así que aquí leerlo en el momento es correcto.
                Surface = nodeUrl ?? Identity().Url,
                Readiness = nodeReadiness ?? CachedReadinessCount().ToString(), // meta de carga del nodo
                ClickPos = clickPos,                           // posición del clic (fallback por id volátil)
            };
        }
        catch { return; }

        try { StepObserved?.Invoke(this, step); } catch { }
    }

    // ── Utilidades ───────────────────────────────────────────────────────────

    private static AutomationElement? Root(IntPtr hwnd)
    {
        try { return AutomationElement.FromHandle(hwnd); }
        catch { return null; }
    }

    private static string ProcessName(IntPtr hwnd)
    {
        try
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName + ".exe";
        }
        catch { return "app"; }
    }

    private static string ControlTypeName(ControlType ct) => ct.ProgrammaticName.Replace("ControlType.", "");

    public void Dispose() => StopObserving();
}
