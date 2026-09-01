using System.Runtime.InteropServices;
using System.Windows.Automation;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Navigation;

/// <summary>
/// Vigila los clics del usuario para poder decir QUÉ causó una transición de superficie. Es la
/// pieza que convierte el mapa base de «conozco este lugar» a «sé cómo llegar»: sin ella, una
/// arista es un entero y no se puede recorrer.
///
/// SIEMPRE ACTIVO, a diferencia del hook del recorder (que solo vive durante «Enseñar»), porque
/// el terreno se aprende mientras el usuario vive su día y no hay un momento en que decida
/// enseñar. Dos consecuencias que se tratan aquí y no se dejan al azar:
///
/// PRIVACIDAD. Se resuelve cada clic, pero solo se PERSISTE el que provocó un cambio de
/// superficie (lo decide <see cref="SurfaceMap"/> al cerrar la arista). Los demás viven en un
/// único campo en memoria y los sobreescribe el siguiente. Del texto tecleado no se guarda nada:
/// un mapa de navegación necesita saber qué se pulsó, jamás qué se escribió.
///
/// RENDIMIENTO. La resolución UIA NO ocurre en el callback del hook. Un hook lento retrasa el
/// ratón de TODA la máquina —este repo ya se topó con eso—, así que el callback solo anota el
/// punto y se aparta; resolver ocurre en un hilo de fondo. Si la resolución tarda más que el
/// siguiente clic, el de en medio se pierde y no pasa nada: el mapa aprende de la próxima vez.
///
/// La identidad se captura EN EL INSTANTE del clic, no después. Es la razón por la que esto no
/// puede hacerse post-procesando pantallazos: el árbol de accesibilidad es estado vivo y un PNG
/// tiene píxeles pero no ids. Después del clic la identidad ya no existe.
/// </summary>
public sealed class ClickWatcher : IDisposable
{
    private const int WH_MOUSE_LL = 14;

    /// <summary>
    /// Se escucha al PULSAR, no al soltar. El efecto de un clic se dispara al soltar, así que en
    /// el instante de pulsar el árbol de accesibilidad sigue siendo el de ANTES de la transición —
    /// que es el único momento en que resolver da la respuesta correcta.
    ///
    /// La primera versión escuchaba WM_LBUTTONUP y salió mal de forma medible: de 43 aristas
    /// aprendidas, casi todas guardaron el texto de la pantalla que apareció DESPUÉS («Pregunta lo
    /// que quieras» llevando a login.neo4j.com, tooltips de la barra de tareas como si fueran
    /// botones). Los clics a lanzadores del escritorio sí acertaban, porque el escritorio sigue
    /// visible mientras la app arranca; los que cambian algo dentro de una app, ninguno. Entre
    /// pulsar y soltar hay 80–150 ms humanos, de sobra para resolver.
    /// </summary>
    private const int WM_LBUTTONDOWN = 0x0201;

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr dwExtraInfo; }
    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RealGetWindowClass(IntPtr hWnd, System.Text.StringBuilder cls, int max);
    private const uint GA_ROOT = 2;

    /// <summary>¿El clic cayó en la barra de tareas o en otro navegador del shell?</summary>
    private static bool EnNavegadorDelSistema(int x, int y, string proc)
    {
        try
        {
            IntPtr raiz = GetAncestor(WindowFromPoint(new POINT { X = x, Y = y }), GA_ROOT);
            var cls = new System.Text.StringBuilder(256);
            RealGetWindowClass(raiz, cls, cls.Capacity);
            if (ShellNavClasses.Contains(cls.ToString())) return true;
        }
        catch { }
        // El menú de inicio y las jump lists viven en procesos propios; explorer NO basta por sí
        // solo (sus ventanas de carpeta también son explorer y esas sí son lugares).
        return proc.Length > 0 && ShellNavProcesses.Contains(proc)
            && !proc.Equals("explorer", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Lo que se pulsó, por identidad y en el MISMO formato que un paso de workflow: selector
    /// principal + alternativas (AutomationId → Name → Path) y la posición relativa a la ventana
    /// como último respaldo — el espejo exacto de lo que la grabación persiste, porque la arista
    /// que esto alimenta tiene que ser ejecutable por el mismo player. <see cref="Selector"/>
    /// vacío = no se pudo resolver, y eso es un dato honesto.
    /// </summary>
    public sealed record Click(
        string Selector, string[] Alternatives, string Label, string ControlType,
        string ClickPos, DateTime When, int DownIndex, string Process, bool IsSystemNavigator);

    /// <summary>
    /// QUIÉN NOMBRA UN CLIC DENTRO DE SAP. Dentro de una sesión, UIA ve un Pane sin etiquetas y
    /// el clic humano quedaba mudo — «salto SIN atribuir» en cada cruce a mano (lo encontró el
    /// usuario en la primera ronda de T4, 2026-08-30). El COM vive fuera; aquí solo se pregunta.
    /// Devuelve null si el punto no cae en una sesión SAP, y entonces sigue el camino UIA.
    /// </summary>
    public Func<int, int, (string Selector, string Etiqueta, string Tipo)?>? ResolverSap { get; set; }

    /// <summary>
    /// Clases de ventana del NAVEGADOR DEL SISTEMA: barra de tareas, miniaturas, vista de tareas.
    ///
    /// Merecen trato aparte porque son un VERBO, no un LUGAR. Como nodo no existen —nadie "vuelve"
    /// a la barra de tareas, se pasa por ella— pero como acción son lo más valioso de un grafo
    /// cross-app: «desde donde estés, ve a X». La correa que exige que el clic ocurra en la app de
    /// origen las mataría a todas, porque la barra es explorer.exe y el origen suele ser otra app.
    /// </summary>
    private static readonly HashSet<string> ShellNavClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Shell_TrayWnd",            // la barra principal
        "Shell_SecondaryTrayWnd",   // barras de monitores secundarios
        "TaskListThumbnailWnd",     // el flyout de miniaturas del hover
        "MultitaskingViewFrame",    // vista de tareas / alt-tab
        "XamlExplorerHostIslandWindow",
    };

    /// <summary>Procesos que SON el navegador del sistema (menú de inicio, jump lists).</summary>
    private static readonly HashSet<string> ShellNavProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "Explorer.EXE",
    };

    /// <summary>
    /// Cuántos DOWN reales ha habido (el segundo down de un doble clic no cuenta). Comparado con
    /// el <see cref="Click.DownIndex"/> del último clic resuelto responde la pregunta que ninguna
    /// ventana de tiempo responde: ¿el último GESTO del usuario es el que tenemos resuelto, o hubo
    /// otro después que no resolvió? Si hubo otro, atribuir el resuelto sería colgarle a una
    /// transición el clic de otra — el fallo medido dos veces el 2026-07-30: una cabecera
    /// «Nombre» heredada por la navegación siguiente, y un párrafo del chat de Claude como
    /// "acción" de una transición de la barra de tareas.
    /// </summary>
    public int Downs => _downs;
    private volatile int _downs;

    private HookProc? _proc;
    private IntPtr _hook;
    private Click? _last;
    private int _lastDownTick, _lastDownX, _lastDownY;

    /// <summary>
    /// Tipos de control que NO son una acción aunque el hit-test los devuelva: contenedores y
    /// cromo. Medido el 2026-07-29: «Vista Elementos» (list), «Host de ventanas emergentes»
    /// (pane), «Nombre» (headeritem) aparecieron como "acciones" de aristas — ninguno describe lo
    /// que el usuario hizo, y un paso «clic en el panel» no se puede reproducir con sentido.
    /// Un clic que resuelve a esto se trata como NO resuelto, que es un dato honesto.
    /// </summary>
    private static readonly HashSet<string> ContainerTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "pane", "window", "list", "tree", "table", "header", "headeritem",
        "toolbar", "titlebar", "group", "document",
    };
    private readonly object _gate = new();

    /// <summary>El último clic resuelto, o null. Lo consume quien cierra una arista.</summary>
    public Click? Last { get { lock (_gate) return _last; } }

    /// <summary>
    /// Cuántos clics se han resuelto en total. Sirve para saber CUÁNTOS hubo entre dos momentos,
    /// que es la única forma de detectar que una arista colapsó varios saltos: comparar la
    /// identidad del último clic no vale, porque entre guardar y cerrar la arista siempre ha
    /// ocurrido el siguiente.
    /// </summary>
    public int Count { get { lock (_gate) return _count; } }
    private int _count;

    public void Start()
    {
        if (_hook != IntPtr.Zero) return;
        _proc = HookCallback;
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
        LogBus.Log("mapa", _hook != IntPtr.Zero
            ? "vigilante de clics activo: las aristas del terreno llevarán su acción"
            : "NO se pudo enganchar el ratón: las aristas quedarán sin acción (solo conectividad)");
    }

    /// <summary>¿Este golpe lo inyectó software? Promesa 84: lo sintético no se acuña como humano.</summary>
    /// <remarks>
    /// Los dos bits del hook de bajo nivel: LLMHF_INJECTED (0x1, inyectado por cualquier proceso)
    /// y LLMHF_LOWER_IL_INJECTED (0x2, inyectado por uno de menor integridad). Inyectado es
    /// inyectado: los dos se dejan pasar sin atribuir. Función pública y pura a propósito — es lo
    /// que el contrato juzga sin necesitar un hook real.
    /// </remarks>
    public static bool EsSintetico(uint flags) => (flags & 0x3u) != 0;

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        // Lo mínimo posible aquí dentro. Nada de UIA, nada de logs, nada que pueda bloquear.
        if (code >= 0 && (int)wParam == WM_LBUTTONDOWN)
        {
            try
            {
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

                // LO SINTÉTICO NO ENSEÑA (promesa 84). Este vigía existe para atribuir clics
                // HUMANOS; los nuestros —RealClick, RealDoubleClick, el batch entero— llegan por el
                // mismo hook y hasta hoy se acuñaban como humanos: el 2026-08-31 el doble sintético
                // sobre «specs» se atribuyó a la cabecera «Fecha de modificación» y nació una
                // arista falsa docs→specs. Windows YA lo dice en flags (LLMHF_INJECTED); solo había
                // que preguntar. Sin esto, cada reproducción de una skill re-contamina el grafo.
                if (EsSintetico(data.flags)) return CallNextHookEx(_hook, code, wParam, lParam);

                int x = data.pt.X, y = data.pt.Y;

                // DOBLE CLIC: se queda la resolución del PRIMER down y el segundo no re-resuelve.
                // El primer down ve el árbol de ANTES (el efecto llega con el segundo up); el
                // segundo pierde la carrera contra la mutación de la página y devolvía cabeceras
                // («Nombre») en vez del elemento. Medido en la lista del explorador, 2026-07-29.
                int t = Environment.TickCount;
                bool esSegundoClic = (t - _lastDownTick) <= 600
                    && Math.Abs(x - _lastDownX) <= 8 && Math.Abs(y - _lastDownY) <= 8;
                _lastDownTick = t; _lastDownX = x; _lastDownY = y;
                if (esSegundoClic) return CallNextHookEx(_hook, code, wParam, lParam);

                int idx = ++_downs; // este down, numerado: el clic resuelto sabrá si fue el último
                IntPtr antes = GetForegroundWindow();
                _ = Task.Run(() => Resolve(x, y, antes, idx)); // fuera del hook: el ratón no espera a UIA
            }
            catch { }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private void Resolve(int x, int y, IntPtr foregroundAntes, int downIndex)
    {
        try
        {
            // PRIMERO SAP: si el punto cae en una sesión, su propia puerta nombra el clic — UIA
            // ahí solo vería el Pane opaco. El mismo centinela de incertidumbre que abajo: si la
            // ventana de delante cambió mientras resolvíamos, resolvimos la pantalla equivocada.
            var deSap = ResolverSap?.Invoke(x, y);
            if (deSap is { } sap)
            {
                if (GetForegroundWindow() != foregroundAntes) return;
                lock (_gate)
                {
                    _last = new Click(sap.Selector, Array.Empty<string>(), sap.Etiqueta, sap.Tipo,
                        $"{x},{y}", DateTime.UtcNow, downIndex, "saplogon", false);
                    _count++;
                }
                return;
            }

            var el = AutomationElement.FromPoint(new System.Windows.Point(x, y));
            if (el == null) return;

            // CENTINELA DE INCERTIDUMBRE. Si la ventana en primer plano cambió mientras
            // resolvíamos, resolvimos el árbol EQUIVOCADO: lo que tenemos es la pantalla nueva,
            // no el elemento pulsado. Se descarta. Una arista sin acción es información honesta;
            // una con la acción equivocada es una instrucción falsa que parece válida, y de esas
            // ya vimos 43 en el primer intento.
            if (GetForegroundWindow() != foregroundAntes) return;

            // Un clic sobre la propia UI de Ü no es una acción de navegación del usuario: es el
            // observador tocándose a sí mismo. Salió en la prueba como «Ü» llevando del escritorio
            // al explorador, que no describe nada. Misma regla que ya rige para los nodos.
            try
            {
                if (el.Current.ProcessId == Environment.ProcessId) return;   // lo nuestro no es terreno (ver Uia.Propio)
            }
            catch { }

            // UNA respuesta a «¿qué es este elemento?»: la del recorder. La primera versión de
            // esto inventaba su propio selector («uia:id=X») a partir de un FromPoint crudo y
            // rindió ~48% de acciones útiles, ninguna ejecutable. DescribeElement devuelve lo
            // mismo que se persiste al grabar un workflow: etiqueta, tipo y selectores con
            // alternativas — aristas en el idioma que el player ya sabe recorrer.
            var (label, type, selectors) = U.Graph.Surfaces.UiaSurface.DescribeElement(el);
            if (selectors.Count == 0) return;

            // Las mismas dos vallas que el recorder pone (o que su contexto le da): sin etiqueta
            // no hay paso —la grabación exige etiqueta y aquí faltaba, por eso salieron aristas
            // con «» —, y un contenedor no es una acción.
            if (string.IsNullOrWhiteSpace(label)) return;
            if (ContainerTypes.Contains(type)) return;

            // La posición RELATIVA a la ventana, como último respaldo — el mismo papel exacto que
            // cumple en la grabación: para elementos sin selector estable, nunca como identidad.
            string clickPos = "";
            try
            {
                if (GetWindowRect(foregroundAntes, out RECT r))
                    clickPos = $"{x - r.Left},{y - r.Top}";
            }
            catch { }

            // Un selector de ruta vacío («uia:path=») no identifica nada: en la grabación la ruta
            // se construye recorriendo el árbol, y aquí no hay recorrido. Guardar un respaldo que
            // no puede resolver es peor que no tenerlo — parece una alternativa y no lo es.
            var utiles = selectors.Where(s => !s.Contains("path=;") && !s.EndsWith("path=")).ToList();
            if (utiles.Count == 0) return;

            // En qué app se hizo el clic: la segunda correa contra la atribución cruzada. Una
            // arista que sale de explorer no puede llevar como acción un clic hecho en claude.
            string proc = "";
            try
            {
                using var p = System.Diagnostics.Process.GetProcessById(el.Current.ProcessId);
                proc = p.ProcessName;
            }
            catch { }

            // Un párrafo entero no es una etiqueta (el envenenamiento del chat medía 200+ chars).
            if (label.Length > 80) label = label[..80];

            lock (_gate)
            {
                bool navSistema = EnNavegadorDelSistema(x, y, proc);
                _last = new Click(utiles[0], utiles.Skip(1).ToArray(), label, type, clickPos,
                    DateTime.UtcNow, downIndex, proc, navSistema);

                // Se registra el reconocimiento, no solo el resultado: sin esta línea no había forma
                // de distinguir «la barra de tareas no se detectó» de «se detectó y algo posterior
                // la descartó» — y esa ambigüedad ya me costó un diagnóstico equivocado.
                if (navSistema)
                    LogBus.Log("mapa", $"navegador del sistema: «{label}» ({type})");
                _count++;
            }
        }
        catch { /* un clic que no resuelve deja la arista sin acción; se aprenderá en otra pasada */ }
    }

    public void Dispose()
    {
        // Soltar el hook importa más que casi nada aquí: uno huérfano ralentiza el ratón de toda
        // la máquina, no solo de esta app.
        if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        _proc = null;
    }
}
