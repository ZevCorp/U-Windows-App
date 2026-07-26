using System.Reflection;
using System.Text;
using System.Windows.Threading;

namespace U.Graph.Surfaces;

/// <summary>
/// Superficie SAP GUI, por la Scripting API (COM).
///
/// POR QUÉ NO UIA: SAP documenta que sus controles están acoplados a la lógica de negocio y no pueden
/// instanciarse fuera de SAP GUI, así que en vez de exponerlos a la automatización genérica crearon
/// esta API. En la práctica UIA se queda en un Pane y no ve nada dentro. Para SAP, esto o nada.
///
/// ENLACE TARDÍO A PROPÓSITO: todo el COM se llama por reflexión, sin referencia a sapfewse.ocx. Así
/// este proyecto compila en máquinas sin SAP GUI (CI, el portátil de un dev) y la ausencia de SAP es
/// un estado que se reporta, no un fallo de build. Además sobrevive mejor a los cambios de versión:
/// el interop de C# se ha roto entre 7.40 → 7.70 → 8.0. El acceso arranca por el ProgID
/// <c>SapROTWr.SapROTWrapper</c> (ver <see cref="RotWrapperProgIds"/>).
///
/// LO QUE NO CONTROLAMOS: el scripting depende de que el Basis del cliente ponga el parámetro de
/// perfil sapgui/user_scripting en TRUE (por defecto es FALSE) y de que el SAP GUI local lo permita.
/// Por eso <see cref="Check"/> distingue los modos de fallo en vez de devolver un booleano: en la
/// máquina de un cliente, "no funciona" es inútil; "tu Basis no ha habilitado el scripting" es
/// accionable.
/// </summary>
public sealed class SapGuiSurface : IUiSurface
{
    public string Name => "sap";

    public event EventHandler<ObservedStep>? StepObserved;

    /// <summary>
    /// Diagnóstico del enganche de eventos: qué modo quedó activo (COM-evento o sondeo) y, si es
    /// COM-evento, qué nombres/DISPIDs se encontraron. Es la única forma de confirmar contra un SAP
    /// real que la introspección calzó — ver <see cref="SapComEvents"/>.
    /// </summary>
    public event EventHandler<string>? Diagnostic;

    /// <summary>Tipos de GuiComponent con los que un humano interactúa. El resto es decorado.</summary>
    private static readonly HashSet<string> Interactive = new(StringComparer.OrdinalIgnoreCase)
    {
        "GuiTextField", "GuiCTextField", "GuiPasswordField", "GuiComboBox",
        "GuiCheckBox", "GuiRadioButton", "GuiButton", "GuiOkCodeField",
    };

    // ── Estado de observación (hilo STA dedicado, ver StartObserving) ───────────
    private readonly object _obsGate = new();
    private volatile bool _observing;
    private Thread? _pumpThread;
    private Dispatcher? _pumpDispatcher;
    private readonly ManualResetEventSlim _ready = new(false);
    private string? _startupError;
    private SapComEvents? _comEvents;
    private DispatcherTimer? _pollTimer;
    private DispatcherTimer? _treeTimer;
    private Dictionary<string, string?> _lastSnapshot = new();

    // ── Disponibilidad ───────────────────────────────────────────────────────

    public SurfaceAvailability Check()
    {
        try
        {
            object? engine = ScriptingEngine();
            if (engine == null)
                return SurfaceAvailability.No(
                    "No hay ninguna sesión de SAP GUI abierta, o SAP GUI no está instalado en este equipo.");

            dynamic app = engine;
            int connections = (int)app.Connections.Count;
            if (connections == 0)
                return SurfaceAvailability.No("SAP GUI está abierto pero no hay ninguna conexión activa.");

            dynamic conn = app.Connections.ElementAt(0);
            int sessions = (int)conn.Sessions.Count;
            if (sessions == 0)
                return SurfaceAvailability.No("La conexión de SAP no tiene ninguna sesión abierta.");

            return SurfaceAvailability.Ok;
        }
        catch (Exception e)
        {
            // El fallo típico aquí es que el scripting esté apagado: SAP no expone el motor y el COM
            // revienta al pedir GetScriptingEngine o al recorrer Connections.
            return SurfaceAvailability.No(
                "No se pudo hablar con SAP GUI Scripting. Lo más probable es que esté deshabilitado: " +
                "el Basis del sistema SAP debe poner sapgui/user_scripting en TRUE (RZ11), y SAP GUI " +
                $"debe permitir scripting en Opciones → Accessibility & Scripting. Detalle: {e.Message}");
        }
    }

    /// <summary>
    /// ProgIDs candidatos del wrapper de la Running Object Table que envía SAP GUI (saprotwr.dll).
    /// El registrado de verdad en SAP GUI for Windows es <c>SapROTWr.SapROTWrapper</c> — verificado
    /// contra SAP GUI 8.00 x64 (CLSID {62341062-29BC-4DCE-A87A-DC0CB19BF230}). El nombre con "C"
    /// (<c>CSapROTWrapper</c>) es el de la CLASE C++ interna y aparece en samples antiguos, pero NO es
    /// un ProgID COM registrado: pedirlo devuelve null y hace que todo parezca "SAP no instalado". Se
    /// prueban ambos por robustez entre versiones, el correcto primero.
    /// </summary>
    private static readonly string[] RotWrapperProgIds =
    {
        "SapROTWr.SapROTWrapper",   // el registrado de verdad (probado contra 8.00 x64)
        "SapROTWr.CSapROTWrapper",  // nombre histórico/de clase C++, por si alguna versión lo registra
    };

    // ── Cache del motor: attach UNA vez, no en cada lectura ─────────────────────
    // SAP GUI dispara su aviso de seguridad ("un script está intentando acceder a SAP GUI") en CADA
    // attach al motor de scripting. El inspector lee cada 700 ms y en cada clic; si resolviéramos el
    // motor de cero cada vez (attach nuevo), el aviso reaparecería sin parar. Cacheamos el motor a nivel
    // de proceso: se hace attach una sola vez y se reutiliza mientras siga vivo. Si SAP se cierra o el
    // proxy muere, la sonda de liveness falla, se invalida y se re-resuelve (nuevo attach → nuevo aviso,
    // pero solo tras un fallo real). El observador de grabación NO usa el cache: sus sinks COM exigen
    // resolver la sesión en su propio hilo STA — ver PumpMain, que llama con useCache:false.
    private static readonly object _engineGate = new();
    private static object? _cachedEngine;

    /// <summary>
    /// El motor de scripting, por la Running Object Table (SapROTWr.SapROTWrapper → GetROTEntry("SAPGUI")
    /// → GetScriptingEngine). Con <paramref name="useCache"/> reutiliza el motor ya enganchado en vez de
    /// hacer attach de nuevo. Devuelve null si SAP GUI no está corriendo (o no está instalado).
    /// </summary>
    private static object? ScriptingEngine(bool useCache = true)
    {
        if (!useCache) return ResolveEngine();

        lock (_engineGate)
        {
            if (_cachedEngine != null)
            {
                try { _ = (int)((dynamic)_cachedEngine).Connections.Count; return _cachedEngine; }
                catch { _cachedEngine = null; } // proxy muerto (SAP se cerró): re-resolver abajo
            }
            return _cachedEngine = ResolveEngine();
        }
    }

    private static object? ResolveEngine()
    {
        Type? wrapperType = null;
        foreach (string progId in RotWrapperProgIds)
        {
            wrapperType = Type.GetTypeFromProgID(progId);
            if (wrapperType != null) break;
        }
        if (wrapperType == null) return null; // saprotwr.dll no registrada → SAP GUI no instalado

        object? wrapper = Activator.CreateInstance(wrapperType);
        if (wrapper == null) return null;

        object? rot = wrapperType.InvokeMember(
            "GetROTEntry", BindingFlags.InvokeMethod, null, wrapper, new object[] { "SAPGUI" });
        if (rot == null) return null; // SAP GUI no está corriendo

        return rot.GetType().InvokeMember(
            "GetScriptingEngine", BindingFlags.InvokeMethod, null, rot, null);
    }

    /// <summary>La sesión con la que trabajamos: la primera de la primera conexión.</summary>
    private static dynamic? Session(bool useCache = true)
    {
        object? engine = ScriptingEngine(useCache);
        if (engine == null) return null;

        dynamic app = engine;
        if ((int)app.Connections.Count == 0) return null;

        dynamic conn = app.Connections.ElementAt(0);
        if ((int)conn.Sessions.Count == 0) return null;

        return conn.Sessions.ElementAt(0);
    }

    // ── Identidad ────────────────────────────────────────────────────────────

    /// <summary>
    /// Dónde estamos. El pathname lleva TRANSACCIÓN / PROGRAMA / Nº DE DYNPRO, no solo la transacción.
    ///
    /// POR QUÉ LOS TRES: una transacción SAP no es una pantalla, es una secuencia de ellas. En NV2000
    /// («Triage: Acceso») el usuario pasa por los dynpros 100, 200, 300… todos con la MISMA transacción.
    /// Con el pathname en solo <c>/NV2000</c>, las tres pantallas tenían identidad idéntica, así que el
    /// sistema de ubicaciones era literalmente incapaz de notar que había navegado: <c>SamePlace</c>
    /// daba true en todas, la ubicación ANTES/DESPUÉS del log salía igual, y el motor de carga no tenía
    /// contra qué esperar. De ahí el "dice que cargó el 100% y es completamente mentira".
    ///
    /// El ORIGIN no cambia (<c>sapgui://QAS</c>): es la app, y es lo que usa el alineador para saber a
    /// qué programa traer el foco. Todo el detalle nuevo va en el pathname, que es lo que distingue
    /// nodos DENTRO de la app.
    /// </summary>
    public SurfaceIdentity Identity()
    {
        try
        {
            dynamic? session = Session();
            if (session == null) return SurfaceIdentity.Unknown;

            dynamic info = session.Info;
            string system = Str(info.SystemName);            // p.ej. QAS
            string tcode = Str(info.Transaction);            // p.ej. NV2000
            string program = "";                             // p.ej. SAPMNPA10
            string screen = "";                              // p.ej. 0100
            try { program = Str(info.Program).Trim(); } catch { }
            try { screen = Str(info.ScreenNumber).Trim(); } catch { }

            string title = "";
            try { title = Str(session.FindById("wnd[0]").Text); } catch { }

            var path = new System.Text.StringBuilder("/").Append(tcode);
            if (program.Length > 0) path.Append('/').Append(program);
            // A 4 dígitos: SAP nombra los dynpros así (0100), y sin normalizar "100" y "0100"
            // parecerían pantallas distintas según de dónde venga el dato.
            if (screen.Length > 0) path.Append('/').Append(int.TryParse(screen, out int n) ? n.ToString("D4") : screen);

            return new SurfaceIdentity(
                Origin: $"sapgui://{(system.Length > 0 ? system : "sap")}",
                Pathname: path.ToString(),
                Title: title);
        }
        catch { return SurfaceIdentity.Unknown; }
    }

    /// <summary>
    /// Cuántos componentes accionables tiene la pantalla activa. Es la métrica del motor de carga.
    ///
    /// Antes devolvía 0 con la excusa de que "la navegación por scripting es síncrona". No lo es: SAP
    /// pinta el dynpro nuevo después del round-trip, y devolver 0 apagaba el respaldo por porcentaje
    /// justo en la superficie donde más falta hacía. Se cuenta sobre la ventana ACTIVA (que puede ser un
    /// modal), no sobre <c>wnd[0]/usr</c>, porque un popup es una pantalla distinta que hay que esperar
    /// igual.
    /// </summary>
    public int ReadinessCount()
    {
        dynamic? session;
        try { session = Session(); } catch { return 0; }
        if (session == null) return 0;

        dynamic? root = null;
        try { root = session.ActiveWindow; } catch { }
        if (root == null) { try { root = session.FindById("wnd[0]", false); } catch { } }
        if (root == null) return 0;

        var acc = new List<dynamic>();
        try { Walk(root, acc, 0); } catch { }
        return acc.Count;
    }

    /// <summary>
    /// ¿Está SAP a mitad de un viaje al servidor? <c>GuiSession.Busy</c> es la señal NATIVA de carga —
    /// mejor que cualquier heurística de conteo, porque la pone el propio SAP GUI. Si la propiedad no
    /// existe en esta versión, se responde false (no bloquear por no saber).
    /// </summary>
    public bool IsBusy()
    {
        try
        {
            dynamic? session = Session();
            if (session == null) return false;
            return (bool)session.Busy;
        }
        catch { return false; }
    }

    /// <summary>
    /// ¿El elemento del paso existe YA y se puede tocar? Antes esto devolvía <c>true</c> siempre, lo que
    /// hacía que <see cref="U.Graph.SurfaceReadiness"/> retornara en la primera iteración sin esperar
    /// nada: el motor de carga estaba inerte en SAP y se actuaba sobre la pantalla anterior.
    ///
    /// Se comprueba de verdad: que SAP siga ocupado cuenta como NO listo; que el id resuelva; y, si el
    /// paso apunta a una fila de árbol, que la clave (o su ruta) siga existiendo en el árbol.
    /// </summary>
    public bool IsStepReady(PlanStep step)
    {
        if (IsBusy()) return false;

        // Pasos sin elemento propio (tecla, alineación): no hay nada que resolver.
        if (!SapSelector.Owns(step.Selector)) return true;

        dynamic? session;
        try { session = Session(); } catch { return false; }
        if (session == null) return false;

        string id = SapSelector.IdOf(step.Selector);
        if (id.Length == 0) return true;

        dynamic? node;
        try { node = session.FindById(id, false); }
        catch { return false; }
        if (node == null) return false;

        string? nodeKey = step.NodeKey ?? SapSelector.NodeKeyOf(step.Selector);
        if (!string.IsNullOrEmpty(nodeKey))
        {
            // Fila de árbol: el árbol puede existir y estar todavía vacío tras navegar.
            try { return ResolveNodeKey(node, nodeKey!, step) != null; }
            catch { return false; }
        }

        // Un control presente pero aún no modificable es exactamente el caso de "no se puede habilitar
        // el elemento". Changeable no existe en todos los tipos: si no está, basta con que resuelva.
        try { return (bool)node.Changeable; }
        catch { return true; }
    }

    // ── Lectura ──────────────────────────────────────────────────────────────

    public IReadOnlyList<DetectedField> ReadFields()
    {
        var fields = new List<DetectedField>();
        dynamic? session;
        try { session = Session(); } catch { return fields; }
        if (session == null) return fields;

        try
        {
            // wnd[0]/usr es el área de usuario: lo que el operador rellena. Fuera quedan barra de
            // herramientas, menús y statusbar, que no son campos de formulario.
            dynamic area = session.FindById("wnd[0]/usr", false);
            if (area == null) return fields;

            var found = new List<dynamic>();
            Walk(area, found, 0);

            int order = 1;
            foreach (dynamic node in found)
            {
                var field = Describe(node, order);
                if (field != null) { fields.Add(field); order++; }
            }
        }
        catch { /* pantalla cambiando bajo los pies */ }

        return fields;
    }

    // ── Lectura VISUAL (inspector) ─────────────────────────────────────────────

    /// <summary>
    /// SubTypes de shell que son LAYOUT puro (no contenido): no se enmarcan, solo se recorren para
    /// llegar a sus hijos. El resto de shells (Tree, GridView, TextEdit, Picture, HTMLViewer…) SÍ son
    /// contenido que el usuario ve y toca, y se enmarcan como una caja.
    /// </summary>
    private static readonly HashSet<string> LayoutShellSubTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Splitter", "Container", "Docking", "Dockshell",
    };

    /// <summary>
    /// TODO lo visible de la pantalla SAP activa, con su geometría de PANTALLA, para el inspector visual.
    /// A diferencia de <see cref="ReadFields"/> (que solo mira <c>wnd[0]/usr</c> y los campos de
    /// formulario), esto recorre la ventana ENTERA (<c>session.ActiveWindow</c>): barra de herramientas,
    /// código OK, títulos, y los shells como el árbol de SAP Easy Access. Es la mitad SAP de lo que el
    /// overlay pinta; la otra mitad la pone UIA. Nunca lanza: si SAP/scripting no está, devuelve vacío.
    /// </summary>
    public IReadOnlyList<SapVisualElement> ReadVisibleElements()
    {
        var acc = new List<SapVisualElement>();
        dynamic? session;
        try { session = Session(); } catch { return acc; }
        if (session == null) return acc;

        dynamic? root = null;
        try { root = session.ActiveWindow; } catch { }
        if (root == null) { try { root = session.FindById("wnd[0]", false); } catch { } }
        if (root == null) return acc;

        try { WalkVisual(root, acc, 0); } catch { /* la pantalla puede cambiar bajo los pies */ }
        return acc;
    }

    /// <summary>
    /// Hit-test nativo de SAP: qué componente hay bajo un punto de PANTALLA. Es la verdad de terreno
    /// del inspector — SAP sabe exactamente qué control cae en ese píxel, mejor que adivinar por la caja
    /// más pequeña que lo contiene. Firma oficial: <c>findByPosition(x, y, raise=True) As GuiComponent</c>,
    /// con x/y en coordenadas de pantalla; con <c>raise=false</c> devuelve null en vez de lanzar cuando
    /// no hay nada. Devuelve el <c>Id</c> del componente, o null si SAP no está o no hay componente ahí.
    /// </summary>
    public string? HitTest(int screenX, int screenY)
    {
        dynamic? session;
        try { session = Session(); } catch { return null; }
        if (session == null) return null;

        try
        {
            dynamic comp = session.FindByPosition(screenX, screenY, false);
            if (comp == null) return null;
            return Str(comp.Id);
        }
        catch { return null; }
    }

    /// <summary>
    /// El nodo actualmente SELECCIONADO de un árbol SAP (por Id del shell), o null. Los nodos de árbol
    /// NO tienen geometría propia (§1.6 de la investigación), así que <see cref="HitTest"/> por píxel
    /// solo devuelve el shell entero, nunca la fila. La forma coordinate-free de saber QUÉ FILA tocó el
    /// usuario es leer la selección del árbol justo después del clic: un clic simple selecciona el nodo,
    /// y su clave es lo que luego acciona el asistente (<c>doubleClickNode</c>/<c>selectNode</c>).
    ///
    /// Distintos controles de árbol (GuiTree simple, de columnas, de lista) exponen getters de selección
    /// distintos, así que se prueban en orden. Nunca lanza: si nada devuelve una clave, da null y quien
    /// llama degrada (registra que hay que capturar la fila por el evento Change/commandArray al grabar).
    /// </summary>
    public (string Key, string Text)? SelectedTreeNode(string treeId)
    {
        dynamic? session; try { session = Session(); } catch { return null; }
        if (session == null) return null;

        dynamic? tree; try { tree = session.FindById(treeId, false); } catch { return null; }
        if (tree == null) return null;

        string? key = TrySelectedNodeKey(tree);
        if (string.IsNullOrEmpty(key)) return null;

        string text = NodeText(tree, key!, TreeColumnNames(tree));
        return (key!, text);
    }

    /// <summary>Clave del nodo seleccionado probando las variantes de la API de árbol. "" si ninguna responde.</summary>
    private static string? TrySelectedNodeKey(dynamic tree)
    {
        // 1. GetSelectedNodes() → GuiCollection de claves (árboles de columnas / lista, multi-selección).
        try
        {
            dynamic sel = tree.GetSelectedNodes();
            int n = (int)sel.Count;
            if (n > 0) { string k = Str(sel.ElementAt(0)); if (k.Length > 0) return k; }
        }
        catch { }

        // 2. Propiedad de clave única (árboles simples). El casing exacto varía entre controles: se
        //    prueban las formas documentadas por enlace tardío, tolerando la ausencia de cada una.
        //
        //    OJO: aquí NO va topNode. Es la primera fila VISIBLE (la posición del scroll), no la
        //    seleccionada — comprobado contra el SAP real, donde con selectedNode vacío topNode valía
        //    vw00722. Tomarlo como selección hacía que cada scroll pareciera un clic: la grabación
        //    habría inventado un paso por cada rueda de ratón sobre el árbol.
        foreach (string prop in new[] { "selectedNode", "SelectedNode", "GetSelectedNode" })
        {
            try
            {
                string k = Str(tree.GetType().InvokeMember(prop, BindingFlags.GetProperty, null, tree, null)).Trim();
                if (k.Length > 0) return k;
            }
            catch { }
        }
        return null;
    }

    private static void WalkVisual(dynamic node, List<SapVisualElement> acc, int depth)
    {
        if (depth > 30 || acc.Count > 700) return;

        string type; try { type = Str(node.Type); } catch { type = ""; }
        bool isContainer; try { isContainer = (bool)node.ContainerType; } catch { isContainer = false; }
        string subType = SubTypeOf(node);
        bool isContentShell = subType.Length > 0 && !LayoutShellSubTypes.Contains(subType);

        // Emitir HOJAS (botones, campos, labels, código OK, panes…) y SHELLS de contenido (árbol, grid…).
        // Los contenedores estructurales (ventana, área de usuario, splitters) no se enmarcan: solo se
        // recorren para alcanzar a sus hijos.
        if (isContentShell || !isContainer)
        {
            // Tipado explícito a propósito: node es dynamic, así que la llamada se resuelve en runtime;
            // sin esto, el compilador infiere `dynamic` y el `with` de abajo no compila (CS8858).
            SapVisualElement? el = DescribeVisual(node, type, subType);
            if (el != null)
            {
                if (IsTreeSubType(subType))
                {
                    List<SapVisualElement> nodes = EnumerateTreeNodes(node, el.Id);
                    acc.Add(el with { Label = $"{el.Label} · {nodes.Count} nodos", ChildCount = nodes.Count });
                    acc.AddRange(nodes);
                }
                else acc.Add(el);
            }
        }

        if (!isContainer) return;

        dynamic children; int count;
        try { children = node.Children; count = (int)children.Count; } catch { return; }
        for (int i = 0; i < count; i++)
        {
            dynamic child;
            try { child = children.ElementAt(i); } catch { continue; }
            try { WalkVisual(child, acc, depth + 1); } catch { }
        }
    }

    /// <summary>Solo los shells exponen SubType; en el resto la propiedad no existe y devolvemos "".</summary>
    private static string SubTypeOf(dynamic node)
    {
        try { return Str(node.SubType); } catch { return ""; }
    }

    private static bool IsTreeSubType(string subType) =>
        subType.IndexOf("Tree", StringComparison.OrdinalIgnoreCase) >= 0;

    private static SapVisualElement? DescribeVisual(dynamic node, string type, string subType)
    {
        string id; try { id = Str(node.Id); } catch { return null; }
        if (id.Length == 0) return null;

        int left, top, w, h;
        try
        {
            left = (int)node.ScreenLeft;
            top = (int)node.ScreenTop;
            w = (int)node.Width;
            h = (int)node.Height;
        }
        catch { return null; } // sin geometría no hay caja que dibujar

        // Descarta lo degenerado o fuera de pantalla: la barra de menú principal, por ejemplo, reporta
        // width/height/top negativos cuando no está desplegada (documentado en la comunidad SAP).
        bool boundsKnown = w > 1 && h > 1 && left > -30000 && top > -30000 && w < 20000 && h < 20000;
        if (!boundsKnown) return null;

        string label = LabelOf(node);
        if (label.Length == 0) label = subType.Length > 0 ? subType : type;

        return new SapVisualElement(
            Id: id,
            Type: type,
            SubType: subType,
            Label: label,
            Value: ValueOf(node, type),
            ScreenLeft: left, ScreenTop: top, Width: w, Height: h,
            BoundsKnown: true,
            ActionType: ActionTypeFor(type),
            ControlType: GraphControlType(type),
            IsNode: false,
            ParentId: null);
    }

    /// <summary>
    /// Tope de nodos por árbol. Alto a propósito: un árbol clínico real (transacción NWP1) trae 1197
    /// nodos y el de favoritos 916, así que el viejo tope de 600 TRUNCABA la mitad del árbol en
    /// silencio — y el rótulo del overlay decía "600 nodos" como si esa fuera la realidad. Esto ya no
    /// es un límite operativo sino una red de seguridad contra un árbol patológico.
    /// </summary>
    private const int MaxTreeNodes = 20000;

    /// <summary>
    /// Los nodos de un árbol SAP como elementos LÓGICOS. La Scripting API da sus claves, textos y RUTA,
    /// pero NINGUNA coordenada por nodo (verificado por sonda: GetItemLeft/Top/Width/Height y
    /// GetNodeHeight devuelven 0), así que van sin bounds: el cerebro los ve y los acciona por clave,
    /// mientras el overlay solo enmarca el árbol entero.
    ///
    /// DE UNA SOLA LLAMADA cuando se puede: <c>GetAllNodeKeys()</c> devuelve TODAS las claves cargadas
    /// de golpe (1197 en el árbol clínico real). El recorrido en anchura por <c>GetNodesCol</c> +
    /// <c>GetSubNodesCol</c> queda de RESPALDO para controles que no expongan GetAllNodeKeys: da el
    /// mismo resultado pero con una llamada COM por nodo, que sobre un árbol de mil nodos se nota.
    ///
    /// Las carpetas colapsadas cuyos hijos aún no se han traído del servidor no aparecen — es correcto:
    /// no las expandimos pasivamente (expandir dispara un viaje al servidor y, en un SAP clínico, puede
    /// disparar lógica de negocio); el agente las desplegará cuando navegue.
    /// </summary>
    private static List<SapVisualElement> EnumerateTreeNodes(dynamic tree, string treeId)
    {
        // Nombres de columna una sola vez: en árboles de columnas el texto visible vive en un ITEM
        // (GetItemText), no en el nodo. En el árbol clínico real GetNodeTextByKey solo responde en 140
        // de 1197 claves (las carpetas) y GetItemText en las 1197 — sin este respaldo se pierden justo
        // las HOJAS, que son las accionables.
        var columns = TreeColumnNames(tree);

        var nodes = new List<SapVisualElement>();
        foreach (string key in AllTreeKeys(tree))
        {
            if (nodes.Count >= MaxTreeNodes) break;

            string text = NodeText(tree, key, columns);
            if (text.Length == 0) continue;

            nodes.Add(new SapVisualElement(
                Id: treeId,
                Type: "GuiTreeNode",
                SubType: "",
                Label: text,
                Value: key,
                ScreenLeft: 0, ScreenTop: 0, Width: 0, Height: 0,
                BoundsKnown: false,
                ActionType: "click",
                ControlType: "treeitem",
                IsNode: true,
                ParentId: treeId,
                NodeKey: key,
                NodePath: NodePathOf(tree, key),
                IsFolder: BoolOf(tree, "IsFolder", key),
                IsExpanded: BoolOf(tree, "IsFolderExpanded", key)));
        }
        return nodes;
    }

    /// <summary>
    /// TODAS las claves del árbol. Prueba <c>GetAllNodeKeys()</c> (una llamada) y, si el control no la
    /// expone o devuelve vacío, cae al recorrido en anchura por <c>GetSubNodesCol</c>.
    /// </summary>
    private static List<string> AllTreeKeys(dynamic tree)
    {
        try
        {
            dynamic col = tree.GetAllNodeKeys();
            int count = (int)col.Count;
            if (count > 0)
            {
                var all = new List<string>(count);
                for (int i = 0; i < count && all.Count < MaxTreeNodes; i++)
                {
                    try { string k = Str(col.ElementAt(i)); if (k.Length > 0) all.Add(k); }
                    catch { }
                }
                if (all.Count > 0) return all;
            }
        }
        catch { /* el control no expone GetAllNodeKeys: respaldo abajo */ }

        var keys = new List<string>();
        var seen = new HashSet<string>();
        var queue = new Queue<string>();
        foreach (string k in TreeKeys(tree, null))
            if (seen.Add(k)) queue.Enqueue(k);

        while (queue.Count > 0 && keys.Count < MaxTreeNodes)
        {
            string key = queue.Dequeue();
            keys.Add(key);
            foreach (string child in TreeKeys(tree, key))
                if (seen.Add(child)) queue.Enqueue(child);
        }
        return keys;
    }

    /// <summary>Ruta jerárquica del nodo (p.ej. <c>1\2</c>), o null si el control no la expone.</summary>
    private static string? NodePathOf(dynamic tree, string key)
    {
        try
        {
            string p = Str(tree.GetNodePathByKey(key)).Trim();
            return p.Length > 0 ? p : null;
        }
        catch { return null; }
    }

    /// <summary>Un predicado del árbol por enlace tardío (IsFolder, IsFolderExpanded). false si no existe.</summary>
    private static bool BoolOf(dynamic tree, string method, string key)
    {
        try
        {
            object? r = tree.GetType().InvokeMember(
                method, BindingFlags.InvokeMethod, null, tree, new object[] { key });
            return r is bool b ? b : Convert.ToBoolean(r);
        }
        catch { return false; }
    }

    /// <summary>Claves de los nodos raíz (<paramref name="parentKey"/> null) o de los hijos de un nodo.</summary>
    private static List<string> TreeKeys(dynamic tree, string? parentKey)
    {
        var keys = new List<string>();
        dynamic col;
        try { col = parentKey == null ? tree.GetNodesCol() : tree.GetSubNodesCol(parentKey); }
        catch { return keys; }

        int count;
        try { count = (int)col.Count; } catch { return keys; }

        for (int i = 0; i < count; i++)
        {
            try
            {
                string k = Str(col.ElementAt(i));
                if (k.Length > 0) keys.Add(k);
            }
            catch { }
        }
        return keys;
    }

    private static List<string> TreeColumnNames(dynamic tree)
    {
        var names = new List<string>();
        dynamic col;
        try { col = tree.GetColumnNames(); }
        catch { return names; }

        int count;
        try { count = (int)col.Count; } catch { return names; }

        for (int i = 0; i < count && names.Count < 20; i++)
        {
            try { string n = Str(col.ElementAt(i)); if (n.Length > 0) names.Add(n); }
            catch { }
        }
        return names;
    }

    /// <summary>Texto de un nodo: primero el del nodo; si vacío, el primer item de columna no vacío.</summary>
    private static string NodeText(dynamic tree, string key, List<string> columns)
    {
        try { string t = Str(tree.GetNodeTextByKey(key)).Trim(); if (t.Length > 0) return t; }
        catch { }

        foreach (string col in columns)
        {
            try { string t = Str(tree.GetItemText(key, col)).Trim(); if (t.Length > 0) return t; }
            catch { }
        }
        return "";
    }

    private static void Walk(dynamic node, List<dynamic> acc, int depth)
    {
        if (depth > 20 || acc.Count > 300) return;
        try
        {
            dynamic children = node.Children;
            int count = (int)children.Count;
            for (int i = 0; i < count; i++)
            {
                dynamic child;
                try { child = children.ElementAt(i); } catch { continue; }

                try
                {
                    if (Interactive.Contains(Str(child.Type))) acc.Add(child);
                }
                catch { }

                try { Walk(child, acc, depth + 1); } catch { }
            }
        }
        catch { /* el componente no tiene hijos */ }
    }

    private static DetectedField? Describe(dynamic node, int order)
    {
        try
        {
            string type = Str(node.Type);
            string id = Str(node.Id);
            if (id.Length == 0) return null;

            string label = LabelOf(node);
            if (label.Length == 0) return null;

            return new DetectedField
            {
                StepOrder = order,
                ActionType = ActionTypeFor(type),
                Selector = SapSelector.ById(id),
                Label = label,
                ControlType = GraphControlType(type),
                CurrentValue = ValueOf(node, type),
                AllowedOptions = OptionsOf(node, type),
            };
        }
        catch { return null; }
    }

    /// <summary>
    /// Cómo se llama el campo para un humano. El Tooltip de SAP suele ser el texto del label de al
    /// lado (que es un GuiLabel aparte y no está enlazado al control), así que es la mejor pista
    /// disponible sin adivinar por coordenadas.
    /// </summary>
    private static string LabelOf(dynamic node)
    {
        foreach (string prop in new[] { "Tooltip", "Name", "Text" })
        {
            try
            {
                string v = Str(node.GetType().InvokeMember(prop, BindingFlags.GetProperty, null, node, null));
                if (v.Trim().Length > 0) return v.Trim();
            }
            catch { }
        }
        return "";
    }

    private static string? ValueOf(dynamic node, string type)
    {
        try
        {
            if (type.Equals("GuiCheckBox", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("GuiRadioButton", StringComparison.OrdinalIgnoreCase))
                return (bool)node.Selected ? "true" : "false";

            if (type.Equals("GuiComboBox", StringComparison.OrdinalIgnoreCase))
                return Str(node.Key);

            if (type.Equals("GuiPasswordField", StringComparison.OrdinalIgnoreCase))
                return null; // jamás se lee ni se graba una contraseña

            return Str(node.Text);
        }
        catch { return null; }
    }

    /// <summary>Las opciones de un combo. En SAP la clave interna (Key) y el texto visible difieren.</summary>
    private static List<FieldOption>? OptionsOf(dynamic node, string type)
    {
        if (!type.Equals("GuiComboBox", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            dynamic entries = node.Entries;
            int count = (int)entries.Count;
            var options = new List<FieldOption>();
            for (int i = 0; i < count && options.Count < 80; i++)
            {
                try
                {
                    dynamic entry = entries.ElementAt(i);
                    string key = Str(entry.Key);
                    string text = Str(entry.Value);
                    if (key.Length > 0 || text.Length > 0)
                        options.Add(new FieldOption { Value = key, Label = text, Text = text });
                }
                catch { }
            }
            return options.Count > 0 ? options : null;
        }
        catch { return null; }
    }

    private static string ActionTypeFor(string type) => type.ToLowerInvariant() switch
    {
        "guibutton" => "click",
        "guicheckbox" => "click",
        "guiradiobutton" => "click",
        "guicombobox" => "select",
        _ => "input",
    };

    /// <summary>Traduce el tipo de SAP al vocabulario que Graph ya usa (nacido del DOM).</summary>
    private static string GraphControlType(string type) => type.ToLowerInvariant() switch
    {
        "guicombobox" => "select",
        "guicheckbox" => "checkbox",
        "guiradiobutton" => "radio",
        "guibutton" => "button",
        "guipasswordfield" => "password",
        _ => "text",
    };

    // ── Ejecución ────────────────────────────────────────────────────────────

    public bool Execute(PlanStep step, out string error)
    {
        error = "";
        dynamic? session;
        try { session = Session(); }
        catch (Exception e) { error = $"SAP GUI no responde: {e.Message}"; return false; }

        if (session == null) { error = "no hay ninguna sesión de SAP GUI abierta"; return false; }

        // Pasos de TECLA (`key:down`, `key:enter`, `key:f3`…). Iban al foco, no a un elemento, así que
        // el bucle de abajo —que filtra por SapSelector.Owns— los descartaba y SAP respondía "no se
        // encontró el campo". Resultado: ninguna tecla se ejecutaba nunca sobre SAP, ni siquiera Enter.
        string selectorOfStep = step.Selector ?? "";
        if (selectorOfStep.StartsWith("key:", StringComparison.OrdinalIgnoreCase))
            return SendKey(session, step, out error);

        var candidates = new List<string> { selectorOfStep };
        candidates.AddRange(step.AlternativeTargets());

        foreach (string selector in candidates.Where(SapSelector.Owns))
        {
            string id = SapSelector.IdOf(selector);
            dynamic? node;
            try { node = session.FindById(id, false); }
            catch { continue; }
            if (node == null) continue;

            // Una fila de árbol no es un componente: el id resuelve al ÁRBOL y la fila viaja aparte.
            string? nodeKey = step.NodeKey ?? SapSelector.NodeKeyOf(selector);
            if (!string.IsNullOrEmpty(nodeKey))
            {
                try { return ApplyToNode(node, nodeKey!, step, out error); }
                catch (Exception e)
                {
                    error = $"SAP rechazó la acción sobre la fila «{step.Label}» ({id}): {e.Message}";
                    return false;
                }
            }

            try { return Apply(node, step, out error); }
            catch (Exception e)
            {
                error = $"SAP rechazó la acción sobre «{step.Label}» ({id}): {e.Message}";
                return false;
            }
        }

        error = $"no se encontró el campo «{step.Label}» en la pantalla actual de SAP ({step.Selector})";
        return false;
    }

    private static bool Apply(dynamic node, PlanStep step, out string error)
    {
        error = "";
        string type = Str(node.Type);

        switch (step.ActionType)
        {
            case "input":
                node.Text = step.Value ?? "";
                return true;

            case "select":
                // En un combo de SAP se fija la CLAVE, no el texto visible. Graph guarda ambos:
                // selectedValue es la clave y selectedLabel el texto.
                if (type.Equals("GuiComboBox", StringComparison.OrdinalIgnoreCase))
                {
                    string key = step.SelectedValue ?? step.Value ?? "";
                    if (key.Length == 0) { error = "el step no trae la clave de la opción"; return false; }
                    node.Key = key;
                    return true;
                }
                node.Text = step.SelectedValue ?? step.Value ?? "";
                return true;

            case "click":
                if (type.Equals("GuiButton", StringComparison.OrdinalIgnoreCase))
                {
                    node.Press();
                    return true;
                }
                if (type.Equals("GuiCheckBox", StringComparison.OrdinalIgnoreCase) ||
                    type.Equals("GuiRadioButton", StringComparison.OrdinalIgnoreCase))
                {
                    node.Selected = !string.Equals(step.Value, "false", StringComparison.OrdinalIgnoreCase);
                    return true;
                }
                node.SetFocus();
                return true;

            default:
                error = $"actionType no soportado en SAP: {step.ActionType}";
                return false;
        }
    }

    // ── Teclas ───────────────────────────────────────────────────────────────

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);

    private const uint KeyUp = 0x0002, KeyExtended = 0x0001;

    /// <summary>
    /// Teclas que SAP entiende como COMANDO propio: se mandan por <c>sendVKey</c>, la vía nativa, que
    /// dispara el round-trip al servidor igual que si el usuario las pulsara. Es más fiable que simular
    /// la tecla física, porque no depende de quién tenga el foco.
    /// </summary>
    private static readonly Dictionary<string, int> SapVKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Enter"] = 0, ["Return"] = 0,
        ["F1"] = 1, ["F2"] = 2, ["F3"] = 3, ["F4"] = 4, ["F5"] = 5, ["F6"] = 6,
        ["F7"] = 7, ["F8"] = 8, ["F9"] = 9, ["F10"] = 10, ["F11"] = 11, ["F12"] = 12,
    };

    /// <summary>
    /// Teclas de NAVEGACIÓN dentro de un control. SAP no las expone como VKey (no son comandos suyos:
    /// las procesa el control que tiene el foco), así que aquí sí hay que enviar la tecla física.
    /// </summary>
    private static readonly Dictionary<string, byte> NavKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
        ["Home"] = 0x24, ["End"] = 0x23, ["PageUp"] = 0x21, ["PageDown"] = 0x22,
        ["Tab"] = 0x09, ["Esc"] = 0x1B, ["Delete"] = 0x2E, ["Backspace"] = 0x08,
    };

    /// <summary>
    /// Ejecuta un paso de tecla contra SAP. Acepta repetidor (<c>"Down x7"</c>) para no llenar el
    /// workflow con siete pasos idénticos al bajar por una lista con la flecha.
    /// </summary>
    private static bool SendKey(dynamic session, PlanStep step, out string error)
    {
        error = "";
        string raw = (step.Value ?? step.Label ?? step.Selector.Substring(4)).Trim();

        int times = 1;
        int x = raw.LastIndexOf(" x", StringComparison.OrdinalIgnoreCase);
        if (x > 0 && int.TryParse(raw[(x + 2)..].Trim(), out int n) && n > 0)
        {
            times = Math.Clamp(n, 1, 200);
            raw = raw[..x].Trim();
        }

        if (SapVKeys.TryGetValue(raw, out int vkey))
        {
            try
            {
                dynamic wnd = session.FindById("wnd[0]");
                for (int i = 0; i < times; i++) wnd.SendVKey(vkey);
                return true;
            }
            catch (Exception e) { error = $"SAP rechazó la tecla «{raw}» (VKey {vkey}): {e.Message}"; return false; }
        }

        if (NavKeys.TryGetValue(raw, out byte vk))
        {
            // La tecla va a quien tenga el foco: hay que asegurarse de que sea SAP y no la carita.
            // SetFocus, NUNCA Maximize: redimensionar la ventana de SAP de un cliente para poder
            // teclear sería un efecto secundario visible que nadie pidió.
            try { session.FindById("wnd[0]").SetFocus(); } catch { }
            uint ext = vk is 0x25 or 0x26 or 0x27 or 0x28 or 0x24 or 0x23 or 0x21 or 0x22 or 0x2E
                ? KeyExtended : 0;
            for (int i = 0; i < times; i++)
            {
                keybd_event(vk, 0, ext, IntPtr.Zero);
                keybd_event(vk, 0, ext | KeyUp, IntPtr.Zero);
                Thread.Sleep(30); // que el control procese cada pulsación como una de verdad
            }
            return true;
        }

        error = $"tecla no soportada en SAP: «{raw}»";
        return false;
    }

    // ── Filas de árbol (GuiTree) ─────────────────────────────────────────────

    /// <summary>
    /// Acciona una FILA de un árbol SAP. Es el equivalente exacto del clic humano: SAP hace su viaje al
    /// servidor igual que si el usuario hubiera pinchado la fila. NO se usa el ratón ni coordenadas —
    /// una fila no tiene rectángulo (§ <see cref="SapVisualElement"/>) y además puede estar fuera del
    /// área visible del scroll, cosa que a la Scripting API le da igual.
    ///
    /// Antes de accionar se pone la fila a la vista con <c>topNode</c>: es el scroll NATIVO del control
    /// (mueve el árbol hasta esa clave), y deja la pantalla en un estado que el operador reconoce en vez
    /// de disparar acciones sobre filas que nunca vio.
    /// </summary>
    private static bool ApplyToNode(dynamic tree, string recordedKey, PlanStep step, out string error)
    {
        error = "";

        string? key = ResolveNodeKey(tree, recordedKey, step);
        if (key == null)
        {
            error = $"la fila «{step.Label}» ya no está en el árbol " +
                    $"(clave grabada {recordedKey}, ruta {step.NodePath ?? "?"}). " +
                    "Si cuelga de una carpeta plegada, hay que expandirla antes.";
            return false;
        }

        TrySetProp(tree, "topNode", key);          // scroll nativo hasta la fila
        if (!TryInvoke(tree, "selectNode", key))   // no todos los controles la exponen
            TrySetProp(tree, "selectedNode", key);

        // Carpeta → desplegar/plegar. Hoja → activar (doble clic), que es como SAP lanza la entrada.
        bool isFolder = BoolOf(tree, "IsFolder", key);
        string mode = (step.Value ?? "").Trim().ToLowerInvariant();

        if (mode.Length == 0) mode = isFolder ? "toggle" : "activate";

        switch (mode)
        {
            case "select":
                return true; // ya seleccionada arriba

            case "expand":
                if (!TryInvoke(tree, "expandNode", key)) { error = "el árbol no permite expandir esa fila"; return false; }
                return true;

            case "collapse":
                if (!TryInvoke(tree, "collapseNode", key)) { error = "el árbol no permite plegar esa fila"; return false; }
                return true;

            case "toggle":
                bool expanded = BoolOf(tree, "IsFolderExpanded", key);
                if (!TryInvoke(tree, expanded ? "collapseNode" : "expandNode", key))
                { error = "el árbol no permite desplegar esa carpeta"; return false; }
                return true;

            default: // "activate" y cualquier cosa que venga de un click normal
                if (TryInvoke(tree, "doubleClickNode", key)) return true;
                // Árboles de columnas: la activación va por ITEM, no por nodo.
                foreach (string col in TreeColumnNames(tree))
                    if (TryInvoke(tree, "doubleClickItem", key, col)) return true;
                error = "el árbol no aceptó doubleClickNode ni doubleClickItem sobre esa fila";
                return false;
        }
    }

    /// <summary>
    /// Qué clave accionar de verdad. Las claves de SAP (<c>vw00073</c>) son de la CARGA del árbol, no
    /// del negocio: pueden moverse entre sesiones. Y el texto no desambigua — en el árbol clínico real
    /// "Órdenes Clínicas" aparece 17 veces, una por servicio. Así que se prueba, en orden de fiabilidad:
    ///
    ///   1. la clave grabada, si sigue existiendo Y su texto coincide con el grabado;
    ///   2. la RUTA jerárquica (<c>GetNodePathByKey</c>), que describe la posición y sobrevive al recargue;
    ///   3. el texto, SOLO si es único en todo el árbol (si no, se prefiere fallar a clicar otra cosa).
    ///
    /// Devuelve null si nada resuelve: en un SAP clínico, accionar la fila equivocada es peor que no
    /// accionar ninguna.
    /// </summary>
    private static string? ResolveNodeKey(dynamic tree, string recordedKey, PlanStep step)
    {
        var columns = TreeColumnNames(tree);
        string wanted = (step.Label ?? "").Trim();

        // 1. La clave tal cual.
        string current = NodeText(tree, recordedKey, columns);
        if (current.Length > 0 &&
            (wanted.Length == 0 || current.Equals(wanted, StringComparison.OrdinalIgnoreCase)))
            return recordedKey;

        var keys = AllTreeKeys(tree);

        // 2. Por ruta jerárquica.
        if (!string.IsNullOrWhiteSpace(step.NodePath))
        {
            foreach (string k in keys)
                if (string.Equals(NodePathOf(tree, k), step.NodePath, StringComparison.OrdinalIgnoreCase))
                    return k;
        }

        // 3. Por texto, solo si es inequívoco.
        if (wanted.Length > 0)
        {
            string? only = null;
            foreach (string k in keys)
            {
                if (!NodeText(tree, k, columns).Equals(wanted, StringComparison.OrdinalIgnoreCase)) continue;
                if (only != null) return null; // ambiguo: mejor fallar que adivinar
                only = k;
            }
            if (only != null) return only;
        }

        // La clave existía aunque el texto no cuadre: último recurso antes de rendirse.
        return current.Length > 0 ? recordedKey : null;
    }

    /// <summary>Llama un método del árbol por enlace tardío. false si no existe o SAP lo rechaza.</summary>
    private static bool TryInvoke(dynamic tree, string method, params object[] args)
    {
        try
        {
            tree.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, tree, args);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Fija una propiedad del árbol por enlace tardío. false si no existe o SAP la rechaza.</summary>
    private static bool TrySetProp(dynamic tree, string prop, object value)
    {
        try
        {
            tree.GetType().InvokeMember(prop, BindingFlags.SetProperty, null, tree, new[] { value });
            return true;
        }
        catch { return false; }
    }

    // ── Observación ──────────────────────────────────────────────────────────

    /// <summary>
    /// Graba sobre SAP GUI. Tres restricciones verificadas contra la spec oficial (ver
    /// INVESTIGACION-SAPGUI-UIA.md) que esto respeta:
    ///
    /// 1. <c>Change</c> NO es un evento por pulsación: se dispara por lotes en el round-trip al
    ///    servidor. La granularidad máxima de grabación ES el viaje al servidor, no la tecla — por
    ///    eso se publica un STEP por CAMPO CUYO VALOR cambió entre dos eventos, no por tecla.
    /// 2. <c>Hit</c> NO es un evento de clic (es hover) — no se usa para grabar.
    /// 3. El parámetro de servidor <c>sapgui/user_scripting_disable_recording</c> apaga TODOS los
    ///    eventos de scripting sin avisar. Si el enganche COM se hizo pero nunca publica nada
    ///    mientras el operador sí interactúa, esa es la causa más probable — es indistinguible de un
    ///    bug nuestro salvo por este comentario.
    ///
    /// LÍMITE HEREDADO DE LA API DE SAP, no de esta implementación: un clic en un botón que NO cambia
    /// ningún valor de campo (p.ej. "Grabar" cuando ya se llenó todo) no se puede distinguir de "nada
    /// pasó" ni por eventos COM ni por sondeo — SAP no expone qué control tuvo el foco al disparar el
    /// round-trip. Graph puede inferirlo en el post-procesamiento por el contexto del video adjunto
    /// (ver WorkflowTeachSession), no aquí.
    ///
    /// El COM de SAP exige un hilo STA con bomba de mensajes, incompatible con el hilo de UIA — por
    /// eso esto arranca un hilo <see cref="Thread"/> dedicado con <see cref="Dispatcher.Run"/> en vez
    /// de compartir el hilo del llamador. Si el enganche de eventos COM falla por cualquier motivo
    /// (introspección no verificada contra un SAP real — ver <see cref="SapComEvents"/>), cae SOLO a
    /// sondeo en vez de lanzar: la grabación sigue funcionando, con la limitación de arriba.
    /// </summary>
    public void StartObserving()
    {
        lock (_obsGate)
        {
            if (_observing) return;
            _ready.Reset();
            _startupError = null;

            _pumpThread = new Thread(PumpMain) { IsBackground = true, Name = "SapGuiEvents" };
            _pumpThread.SetApartmentState(ApartmentState.STA);
            _pumpThread.Start();

            if (!_ready.Wait(TimeSpan.FromSeconds(8)))
                _startupError ??= "el hilo de observación de SAP no arrancó a tiempo";

            if (_startupError != null)
            {
                string error = _startupError;
                // No dejar el hilo huérfano corriendo Dispatcher.Run(): si arranca tarde, igual hay
                // que pararlo, o un reintento del operador acumularía un hilo STA por cada intento.
                try { _pumpDispatcher?.InvokeShutdown(); } catch { }
                try { _pumpThread?.Join(TimeSpan.FromSeconds(2)); } catch { }
                _pumpThread = null;
                _pumpDispatcher = null;
                throw new GraphException($"No se pudo iniciar la observación de SAP GUI: {error}");
            }

            _observing = true;
        }
    }

    /// <summary>Cuerpo del hilo STA dedicado: resuelve la sesión EN este hilo (nunca hereda un proxy COM de otro) y bombea mensajes.</summary>
    private void PumpMain()
    {
        try
        {
            _pumpDispatcher = Dispatcher.CurrentDispatcher;

            dynamic? session;
            // useCache:false a propósito: los sinks COM del observador exigen la sesión resuelta EN este
            // hilo STA. El cache del inspector (resuelto en otro hilo) rompería el enganche de eventos.
            try { session = Session(useCache: false); }
            catch (Exception e)
            {
                _startupError = $"no se pudo resolver la sesión SAP en el hilo de observación: {e.Message}";
                _ready.Set();
                return;
            }

            if (session == null)
            {
                _startupError = "no hay ninguna sesión de SAP GUI abierta al iniciar la observación";
                _ready.Set();
                return;
            }

            _comEvents = new SapComEvents(session);
            _comEvents.Diagnostic += (_, msg) => Diagnostic?.Invoke(this, $"[com] {msg}");
            _comEvents.Raised += (_, __) => PublishChangedFields();

            if (_comEvents.TryHook(out string reason))
            {
                Diagnostic?.Invoke(this, "observando por eventos COM de SAP GUI (Change/StartRequest/…).");
            }
            else
            {
                Diagnostic?.Invoke(this,
                    $"eventos COM no disponibles ({reason}); cae a sondeo (no detecta clics sin cambio de valor).");
                _lastSnapshot = SafeReadFields().ToDictionary(f => f.Selector, f => f.CurrentValue);
                _pollTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
                _pollTimer.Tick += (_, __) => PublishChangedFields();
                _pollTimer.Start();
            }

            // La selección de una fila de árbol se sondea SIEMPRE, en AMBOS modos, con su propio
            // reloj. Por qué no basta con los eventos COM: seleccionar una fila NO viaja al servidor
            // (Change no dispara), y cuando el doble clic SÍ viaja, al llegar el evento la pantalla ya
            // navegó y el árbol no existe — el paso se perdía justo cuando importaba. El sondeo es
            // barato (solo shells de árbol, sin releer el dynpro) y el clic queda capturado ENTRE la
            // selección y la navegación.
            //
            // Línea base primero: lo que ya estaba seleccionado ANTES de enseñar no es un paso del
            // operador — sin esto, el primer tick emitiría un clic fantasma.
            _lastTreeSelection = SafeReadTreeSelections().ToDictionary(s => s.TreeId, s => s.Key);
            _treeTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(400) };
            _treeTimer.Tick += (_, __) => { if (!SessionBusy(session)) PublishTreeSelections(); };
            _treeTimer.Start();

            _ready.Set();
            Dispatcher.Run(); // bombea hasta que StopObserving llame InvokeShutdown()
        }
        catch (Exception e)
        {
            _startupError = $"fallo iniciando observación SAP: {e.Message}";
            _ready.Set();
        }
    }

    /// <summary>
    /// Publica un ObservedStep por cada campo cuyo valor cambió desde la última lectura. Es el mismo
    /// mecanismo tanto si lo dispara un evento COM real como el sondeo: en ambos casos la fuente de
    /// verdad es releer el área de usuario completa, porque ni Change ni el sondeo traen "qué cambió".
    /// </summary>
    private void PublishChangedFields()
    {
        var current = SafeReadFields();
        string node = "", readiness = "";
        foreach (DetectedField field in current)
        {
            _lastSnapshot.TryGetValue(field.Selector, out string? prev);
            if (prev == field.CurrentValue) continue;

            // El NODO del paso — dónde estaba parado el usuario al hacerlo. SAP tiene la mejor
            // identidad de todo el sistema (sapgui://SID/TCODE/PROGRAMA/DYNPRO) pero era el único
            // motor que NO la grababa: sin observedSurface por paso, la fase de ubicación del motor
            // de carga se saltaba entera y el player ejecutaba contra la pantalla que hubiera.
            // Se captura una vez por lote (Identity() es un round-trip COM) y solo si hay pasos.
            if (node.Length == 0) { node = SafeNodeUrl(); readiness = SafeReadinessMeta(); }

            StepObserved?.Invoke(this, new ObservedStep(
                ActionType: field.ActionType,
                Selector: field.Selector,
                Label: field.Label,
                ControlType: field.ControlType,
                Value: field.CurrentValue,
                AllowedOptions: field.AllowedOptions,
                SelectedValue: field.ActionType == "select" ? field.CurrentValue : null,
                SelectedLabel: field.ActionType == "select" ? field.CurrentValue : null,
                SurfaceSection: null,
                AlternativeTargets: Array.Empty<string>())
            {
                Surface = node,
                Readiness = readiness,
            });
        }
        _lastSnapshot = current.ToDictionary(f => f.Selector, f => f.CurrentValue);

        PublishTreeSelections();
    }

    /// <summary>Última fila seleccionada por árbol, para detectar el cambio (Id del árbol → clave).</summary>
    private Dictionary<string, string> _lastTreeSelection = new();

    /// <summary>
    /// Publica un paso cuando el usuario selecciona una FILA de un árbol. Hace falta aparte porque
    /// <see cref="PublishChangedFields"/> relee <c>wnd[0]/usr</c> — los campos del dynpro — y un árbol
    /// vive en un shell fuera de ahí: un clic en el árbol no cambiaba NINGÚN campo, así que la grabación
    /// no veía nada y el paso se perdía entero.
    ///
    /// La identidad de la fila viaja en el selector (<c>sap:…/shell#node=clave</c>) más la RUTA en
    /// <see cref="ObservedStep.NodePath"/>. No se acciona nada aquí: solo se lee la selección que el
    /// clic del usuario ya provocó.
    /// </summary>
    private void PublishTreeSelections()
    {
        var seen = new Dictionary<string, string>();
        string node = "", readiness = "";

        foreach (var sel in SafeReadTreeSelections())
        {
            seen[sel.TreeId] = sel.Key;
            if (_lastTreeSelection.TryGetValue(sel.TreeId, out string? prev) && prev == sel.Key) continue;

            // Mismo nodo por lote que en PublishChangedFields: la selección se captura ENTRE el clic
            // y la navegación (reloj de 400 ms con guarda de Busy), así que Identity() aquí todavía
            // es la pantalla donde el usuario clicó — el nodo correcto del paso.
            if (node.Length == 0) { node = SafeNodeUrl(); readiness = SafeReadinessMeta(); }

            StepObserved?.Invoke(this, new ObservedStep(
                ActionType: "click",
                Selector: SapSelector.ByNode(sel.TreeId, sel.Key),
                Label: sel.Text,
                ControlType: "treeitem",
                Value: null,
                AllowedOptions: null,
                SelectedValue: null,
                SelectedLabel: null,
                SurfaceSection: null,
                AlternativeTargets: Array.Empty<string>())
            {
                NodePath = sel.Path ?? "",
                Surface = node,
                Readiness = readiness,
            });
        }

        _lastTreeSelection = seen;
    }

    /// <summary>URL del nodo actual para grabar en el paso; "" si la identidad no se puede leer
    /// (mejor sin dato — comportamiento viejo — que un nodo <c>unknown://</c> que nunca casará).</summary>
    private string SafeNodeUrl()
    {
        try
        {
            var id = Identity();
            return id.Origin.StartsWith("unknown", StringComparison.OrdinalIgnoreCase) ? "" : id.Url;
        }
        catch { return ""; }
    }

    /// <summary>Meta de carga del nodo al grabar; "" si no se pudo contar (0 apagaría el respaldo).</summary>
    private string SafeReadinessMeta()
    {
        try
        {
            int c = ReadinessCount();
            return c > 0 ? c.ToString() : "";
        }
        catch { return ""; }
    }

    private IReadOnlyList<(string TreeId, string Key, string Text, string? Path)> SafeReadTreeSelections()
    {
        try { return ReadTreeSelections(); }
        catch { return Array.Empty<(string, string, string, string?)>(); }
    }

    /// <summary>
    /// La fila seleccionada de CADA árbol de la pantalla activa. Es la vía coordinate-free de saber qué
    /// fila tocó el usuario: como los nodos no tienen geometría, el hit-test por píxel solo devuelve el
    /// shell entero, nunca la fila.
    /// </summary>
    public IReadOnlyList<(string TreeId, string Key, string Text, string? Path)> ReadTreeSelections()
    {
        var found = new List<(string, string, string, string?)>();

        dynamic? session;
        try { session = Session(); } catch { return found; }
        if (session == null) return found;

        dynamic? root = null;
        try { root = session.ActiveWindow; } catch { }
        if (root == null) { try { root = session.FindById("wnd[0]", false); } catch { } }
        if (root == null) return found;

        var trees = new List<dynamic>();
        try { CollectTrees(root, trees, 0); } catch { }

        foreach (dynamic tree in trees)
        {
            string id;
            try { id = Str(tree.Id); } catch { continue; }
            if (id.Length == 0) continue;

            string? key = TrySelectedNodeKey(tree);
            if (string.IsNullOrEmpty(key)) continue;

            found.Add((SapSelector.Normalize(id), key!,
                       NodeText(tree, key!, TreeColumnNames(tree)), NodePathOf(tree, key!)));
        }
        return found;
    }

    /// <summary>Todos los shells de tipo árbol colgando de un componente.</summary>
    private static void CollectTrees(dynamic node, List<dynamic> acc, int depth)
    {
        if (depth > 30 || acc.Count > 20) return;

        if (IsTreeSubType(SubTypeOf(node))) acc.Add(node);

        bool isContainer;
        try { isContainer = (bool)node.ContainerType; } catch { return; }
        if (!isContainer) return;

        dynamic children; int count;
        try { children = node.Children; count = (int)children.Count; } catch { return; }
        for (int i = 0; i < count; i++)
        {
            try { CollectTrees(children.ElementAt(i), acc, depth + 1); } catch { }
        }
    }

    private IReadOnlyList<DetectedField> SafeReadFields()
    {
        try { return ReadFields(); } catch { return Array.Empty<DetectedField>(); }
    }

    public void StopObserving()
    {
        lock (_obsGate)
        {
            if (!_observing) return;

            try
            {
                _pumpDispatcher?.Invoke(() =>
                {
                    _pollTimer?.Stop();
                    _pollTimer = null;
                    _treeTimer?.Stop();
                    _treeTimer = null;
                    // Descarga final: el último clic del operador puede haber caído DESPUÉS del último
                    // tick del reloj (típico: clic en la fila e inmediatamente "detener enseñanza").
                    // Sin esta lectura, el paso final del workflow se pierde en silencio.
                    try { PublishTreeSelections(); } catch { }
                    _comEvents?.Unhook();
                });
            }
            catch { /* la sesión SAP o el dispatcher pudieron morir antes que nosotros */ }

            _pumpDispatcher?.InvokeShutdown();
            _pumpThread?.Join(TimeSpan.FromSeconds(5));

            _comEvents?.Dispose();
            _comEvents = null;
            _pumpDispatcher = null;
            _pumpThread = null;
            _observing = false;
        }
    }

    // ── Utilidades ───────────────────────────────────────────────────────────

    /// <summary>
    /// ¿La sesión está en medio de un round-trip? Cualquier llamada al scripting con Busy=true se
    /// bloquea SIN retorno (spec oficial, ver INVESTIGACION-SAPGUI-UIA.md) — los relojes deben saltarse
    /// ese tick en vez de colgar el hilo de bombeo.
    /// </summary>
    private static bool SessionBusy(dynamic session)
    {
        try { return (bool)session.Busy; } catch { return false; }
    }

    private static string Str(object? v) => v?.ToString() ?? "";

    public void Dispose() => StopObserving();
}
