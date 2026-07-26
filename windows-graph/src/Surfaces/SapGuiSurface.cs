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

    public SurfaceIdentity Identity()
    {
        try
        {
            dynamic? session = Session();
            if (session == null) return SurfaceIdentity.Unknown;

            dynamic info = session.Info;
            string system = Str(info.SystemName);            // p.ej. PRD
            string tcode = Str(info.Transaction);            // p.ej. VA01
            string title = Str(session.FindById("wnd[0]").Text);

            return new SurfaceIdentity(
                Origin: $"sapgui://{(system.Length > 0 ? system : "sap")}",
                Pathname: "/" + tcode,
                Title: title);
        }
        catch { return SurfaceIdentity.Unknown; }
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
    /// más pequeña que lo contiene. Devuelve el <c>Id</c> del componente, o null si SAP no está o no
    /// hay componente ahí. Para el detalle completo (inner object, forma COM) ver
    /// <see cref="HitTestDetailed"/>.
    /// </summary>
    public string? HitTest(int screenX, int screenY) => HitTestDetailed(screenX, screenY)?.Id;

    /// <summary>
    /// Hit-test nativo con TODO lo que la API devolvió. El contrato de <c>FindByPosition</c> está en
    /// disputa dentro del propio repo: el código histórico asumía un GuiComponent con <c>.Id</c>, la
    /// spec citada en INVESTIGACION-SAPGUI-UIA.md documenta una GuiCollection de 2 strings ([0] Id,
    /// [1] inner object). Si la verdad es la colección, la versión anterior lanzaba SIEMPRE al pedir
    /// <c>.Id</c> y el diagnóstico de clic caía en silencio al fallback "caja más pequeña" — por eso
    /// aquí se aceptan AMBAS formas y <see cref="SapHit.ComShape"/> registra cuál llegó, para zanjar
    /// la contradicción con datos de un SAP real. El inner object [1] es además la única pista nativa
    /// de QUÉ fila/botón interno de un shell (árbol, toolbar, grid) hay bajo el punto — la pieza clave
    /// del mapeo del scrolleable (ver SONDA-MAPEO-ARBOL.md).
    /// </summary>
    public SapHit? HitTestDetailed(int screenX, int screenY)
    {
        dynamic? session;
        try { session = Session(); } catch { return null; }
        if (session == null) return null;

        object? raw;
        try { raw = session.FindByPosition(screenX, screenY, false); }
        catch { return null; }
        if (raw == null) return null;

        return InterpretHit(raw);
    }

    private static SapHit? InterpretHit(object raw)
    {
        dynamic d = raw;

        // Forma A: GuiComponent con .Id (lo que asumía el código histórico).
        try
        {
            string id = Str(d.Id);
            if (id.Length > 0) return new SapHit(id, null, "component");
        }
        catch { /* no es un componente: probar como colección */ }

        // Forma B: GuiCollection de strings — [0] Id, [1] descripción del inner object (spec oficial).
        try
        {
            int count = (int)d.Count;
            string id = count > 0 ? Str(d.ElementAt(0)) : "";
            string inner = count > 1 ? Str(d.ElementAt(1)) : "";
            if (id.Length > 0)
                return new SapHit(id, inner.Length > 0 ? inner : null, $"collection[{count}]");
        }
        catch { /* sin ElementAt: probar el indexador */ }

        // Forma C: colecciones COM que solo exponen el indexador Item(i).
        try
        {
            int count = (int)d.Count;
            string id = count > 0 ? Str(d.Item(0)) : "";
            string inner = count > 1 ? Str(d.Item(1)) : "";
            if (id.Length > 0)
                return new SapHit(id, inner.Length > 0 ? inner : null, $"collection-item[{count}]");
        }
        catch { /* forma desconocida: se reporta null y el caller cae a su fallback */ }

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
    /// Los nodos de un árbol SAP como elementos LÓGICOS. La Scripting API da sus claves y textos, pero
    /// NINGUNA coordenada por nodo (verificado contra la spec oficial), así que van sin bounds: el
    /// cerebro los ve y puede accionarlos por clave, pero el overlay solo enmarca el árbol entero.
    ///
    /// RECORRIDO EN ANCHURA por clave, no una sola pasada: <c>GetNodesCol</c> devuelve, según el control,
    /// solo los nodos RAÍZ (en SAP Easy Access, "Favoritos" y "Menú SAP" → dos). Para capturar todo lo
    /// CARGADO hay que bajar por <c>GetSubNodesCol</c> desde cada clave, deduplicando (si una versión sí
    /// devuelve todo de golpe, el HashSet evita repetir). Las carpetas colapsadas cuyos hijos aún no se
    /// han traído del servidor no aparecen — es correcto: no los expandimos pasivamente; el agente los
    /// desplegará cuando navegue.
    /// </summary>
    private static List<SapVisualElement> EnumerateTreeNodes(dynamic tree, string treeId)
    {
        var nodes = new List<SapVisualElement>();
        var seen = new HashSet<string>();
        var queue = new Queue<string>();

        foreach (string k in TreeKeys(tree, null))
            if (seen.Add(k)) queue.Enqueue(k);

        // Nombres de columna una sola vez: en árboles de columnas el texto visible vive en un ITEM
        // (GetItemText), no en el nodo (GetNodeTextByKey devuelve vacío). Se prueban ambos.
        var columns = TreeColumnNames(tree);

        while (queue.Count > 0 && nodes.Count < 600)
        {
            string key = queue.Dequeue();

            string text = NodeText(tree, key, columns);
            if (text.Length > 0)
            {
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
                    NodeKey: key));
            }

            if (seen.Count < 800)
                foreach (string child in TreeKeys(tree, key))
                    if (seen.Add(child)) queue.Enqueue(child);
        }
        return nodes;
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

        var candidates = new List<string> { step.Selector };
        candidates.AddRange(step.AlternativeTargets());

        foreach (string selector in candidates.Where(SapSelector.Owns))
        {
            string id = SapSelector.IdOf(selector);
            dynamic? node;
            try { node = session.FindById(id, false); }
            catch { continue; }
            if (node == null) continue;

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
        foreach (DetectedField field in current)
        {
            _lastSnapshot.TryGetValue(field.Selector, out string? prev);
            if (prev == field.CurrentValue) continue;

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
                AlternativeTargets: Array.Empty<string>()));
        }
        _lastSnapshot = current.ToDictionary(f => f.Selector, f => f.CurrentValue);
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

    // ── Sonda de mapeo del árbol (experimento — ver SONDA-MAPEO-ARBOL.md) ────

    /// <summary>
    /// EXPERIMENTO del mapeo de botones de un scrolleable de árbol (p.ej. el "Entorno de trabajo" del
    /// SAP del hospital). La Scripting API no expone coordenadas por nodo (límite de SAP verificado
    /// contra la spec), así que la única vía nativa de saber QUÉ nodo ocupa QUÉ franja de pantalla es
    /// preguntarle a SAP punto por punto con el hit-test (<see cref="HitTestDetailed"/> — llamadas COM
    /// locales, sin round-trips al servidor). Esta sonda barre dos columnas verticales dentro de cada
    /// shell de árbol visible y vuelca las bandas CRUDAS que SAP devuelve, para decidir con datos
    /// reales si el inner object identifica el nodo (clave/fila/texto) o no — de eso depende si el
    /// mapeo definitivo es determinista o hay que caer a accesibilidad/OCR.
    ///
    /// Deliberadamente de SOLO LECTURA y bajo demanda (botón en la ventana de registro): jamás corre
    /// en el timer del inspector. Devuelve líneas listas para el LogBus.
    /// </summary>
    public IReadOnlyList<string> ProbeTreeMapping()
    {
        var lines = new List<string>();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        dynamic? session;
        try { session = Session(); }
        catch (Exception e) { lines.Add($"sonda: SAP no responde: {e.Message}"); return lines; }
        if (session == null) { lines.Add("sonda: no hay ninguna sesión de SAP GUI abierta"); return lines; }

        // Nunca llamar al scripting con la sesión ocupada: la llamada se bloquea SIN retorno
        // (documentado en INVESTIGACION-SAPGUI-UIA.md). Mejor pedir reintento que colgar el hilo.
        try
        {
            if ((bool)session.Busy)
            {
                lines.Add("sonda: session.Busy=true — espera a que SAP termine el round-trip y reintenta");
                return lines;
            }
        }
        catch { lines.Add("sonda: session.Busy no legible; se continúa"); }

        var els = ReadVisibleElements();
        var trees = els.Where(e => !e.IsNode && IsTreeSubType(e.SubType) && e.BoundsKnown).ToList();
        int logicalNodes = els.Count(e => e.IsNode);
        lines.Add($"sonda: {trees.Count} shell(s) de árbol visibles · {logicalNodes} nodos lógicos · {els.Count} elementos totales");

        foreach (var tree in trees)
        {
            lines.Add($"── árbol {tree.Id}");
            lines.Add($"   rect: left={tree.ScreenLeft} top={tree.ScreenTop} w={tree.Width} h={tree.Height} · «{tree.Label}»");

            // TopNode: clave del primer nodo visible según la API. Con él (más el orden de los nodos
            // expandidos) se correlaciona cada banda del barrido con su nodo lógico en la fase 2.
            try
            {
                dynamic node = session.FindById(tree.Id, false);
                if (node != null) lines.Add($"   TopNode={Str(node.TopNode)}");
            }
            catch (Exception e) { lines.Add($"   TopNode no legible: {e.Message}"); }

            // Dos columnas: A cerca del borde izquierdo (flechas de expandir / iconos) y B sobre la
            // zona de textos/botones. Si el inner object difiere entre columnas, el formato trae
            // información de sub-partes de la fila — dato importante para el parseo.
            int xa = tree.ScreenLeft + Math.Min(24, Math.Max(4, tree.Width / 20));
            int xb = tree.ScreenLeft + Math.Min(tree.Width - 8, Math.Max(60, tree.Width * 35 / 100));
            SweepColumn(lines, "colA", xa, tree);
            SweepColumn(lines, "colB", xb, tree);
        }

        // Muestra de nodos lógicos por árbol: contra esto se casan las bandas (¿la clave? ¿el texto?).
        foreach (var group in els.Where(e => e.IsNode).GroupBy(e => e.ParentId ?? ""))
        {
            lines.Add($"── nodos lógicos de {group.Key} (primeros 15 de {group.Count()}):");
            foreach (var n in group.Take(15))
                lines.Add($"   key={n.NodeKey} · «{n.Label}»");
        }

        lines.Add($"sonda: fin en {sw.ElapsedMilliseconds} ms");
        return lines;
    }

    /// <summary>
    /// Barre una columna vertical de puntos sobre un shell y compacta los resultados idénticos en
    /// bandas (y-desde..y-hasta → mismo hit). Una banda por fila visible es el resultado ideal.
    /// </summary>
    private void SweepColumn(List<string> lines, string name, int x, SapVisualElement tree)
    {
        const int step = 8;      // px entre muestras: fino de sobra para filas de ~20-30 px
        const int maxLines = 80; // techo por columna: el LogBus retiene 500 entradas en total

        int calls = 0, emitted = 0;
        string? band = null;
        int bandStart = 0;

        void CloseBand(int yEnd)
        {
            if (band == null) return;
            if (emitted < maxLines) { lines.Add($"   {name} y={bandStart}..{yEnd}: {band}"); emitted++; }
            else if (emitted == maxLines) { lines.Add($"   {name}: …bandas restantes omitidas (techo {maxLines})"); emitted++; }
        }

        int yFrom = tree.ScreenTop + 2;
        int yTo = tree.ScreenTop + tree.Height - 2;
        for (int y = yFrom; y <= yTo; y += step)
        {
            SapHit? hit;
            try { hit = HitTestDetailed(x, y); } catch { hit = null; }
            calls++;

            string current = hit == null
                ? "(null)"
                : $"id={hit.Id} · inner={hit.InnerObject ?? "-"} · {hit.ComShape}";

            if (current != band)
            {
                CloseBand(y - 1);
                band = current;
                bandStart = y;
            }
        }
        CloseBand(yTo);
        lines.Add($"   {name}: x={x} · {calls} llamadas");
    }

    // ── Utilidades ───────────────────────────────────────────────────────────

    private static string Str(object? v) => v?.ToString() ?? "";

    public void Dispose() => StopObserving();
}
