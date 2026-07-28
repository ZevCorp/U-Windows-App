using System.Reflection;
using System.Runtime.InteropServices;
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

    /// <summary>
    /// Tipos de GuiComponent con los que un humano interactúa. El resto es decorado.
    ///
    /// <c>GuiOkCodeField</c> cuenta aquí para <see cref="ReadinessCount"/> y
    /// <see cref="ReadVisibleElements"/>, que recorren la ventana ENTERA. Pero NO llega nunca por
    /// <see cref="ReadFields"/>: ese arranca en <c>wnd[0]/usr</c> y el campo de comandos vive en
    /// <c>wnd[0]/tbar[0]/okcd</c>, que es HERMANO de <c>usr</c>, no descendiente. Es deliberado —
    /// <c>ReadFields</c> es el contrato de autofill y el campo de comandos no es un campo del
    /// formulario. La grabación de la transacción lo lee aparte: ver <see cref="TryReadOkCode"/>.
    /// </summary>
    private static readonly HashSet<string> Interactive = new(StringComparer.OrdinalIgnoreCase)
    {
        "GuiTextField", "GuiCTextField", "GuiPasswordField", "GuiComboBox",
        "GuiCheckBox", "GuiRadioButton", "GuiButton", "GuiOkCodeField",
    };

    /// <summary>
    /// El campo de comandos de SAP: donde el operador teclea la transacción («NWP1», «/nVA01»). Es la
    /// puerta de entrada a CUALQUIER transacción, y hasta ahora la grabación era ciega a él — todo
    /// workflow empezaba asumiendo que ya estabas dentro, y al reproducir desde Easy Access el primer
    /// paso esperaba una pantalla a la que nadie había navegado.
    /// </summary>
    private const string OkCodeId = "wnd[0]/tbar[0]/okcd";

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

    /// <summary>
    /// La pantalla a la que pertenece <see cref="_lastSnapshot"/>. Un snapshot sin pantalla asociada es
    /// una trampa: se compara contra él estando en otra pantalla y la diferencia —que es «son dos
    /// pantallas distintas»— se publica como si el operador hubiera editado veinte campos.
    /// </summary>
    private string _snapshotSurface = "";

    // ── Instantánea PREVIA a la acción ──────────────────────────────────────────
    //
    // El nodo de un paso es DÓNDE ESTABA EL USUARIO al hacerlo, y leerlo después ya no lo dice: si la
    // acción navegó, Identity() devuelve la pantalla de DESTINO y el paso queda sellado con una
    // pantalla en la que nunca ocurrió — la compuerta del player espera entonces algo imposible.
    // UiaSurface cierra esa carrera capturando el nodo lo primero de todo (ver UiaSurface.OnHookClick);
    // aquí no se puede, porque no hay hook de clic: la acción se descubre DESPUÉS, releyendo. La
    // equivalencia es esta sombra, refrescada en cada tick en que SAP está OCIOSO — es decir, siempre
    // fuera de un round-trip, que es justo cuando la pantalla es todavía la de origen.
    private string _preNodeUrl = "";
    private string _preReadiness = "";
    private string _preFingerprint = "";

    /// <summary>Lo último tecleado en el campo de comandos mientras SAP estaba ocioso. Se consume en
    /// StartRequest: para entonces SAP puede estar ya ocupado y no se le puede preguntar nada.</summary>
    private string _pendingOkCode = "";

    // ── Último clic del operador, en coordenadas de PANTALLA ────────────────────────────
    //
    // Por qué hace falta un hook de ratón en una superficie que presume de no usar coordenadas: SAP no
    // dice qué control disparó el round-trip —verificado por introspección ITypeInfo: GuiSession y
    // GuiFrameWindow no tienen ningún getter de foco—, así que un botón que no cambia ningún valor de
    // campo es indistinguible de «no pasó nada». Los de barra de ALV se cazan por GetToolbarFocusButton;
    // los GuiButton sueltos —Guardar, Continuar, Buscar— no tenían forma de detectarse.
    //
    // La coordenada NO se graba: se usa UNA VEZ, en StartRequest, para preguntarle a SAP por su propio
    // hit-test (FindByPosition) quién está ahí. Lo que se guarda en el paso es el ID del control. El
    // píxel es el soplo; el selector sigue siendo estable.
    private int _clickX, _clickY;
    private DateTime _clickAt = DateTime.MinValue;
    private IntPtr _mouseHook = IntPtr.Zero;
    private LowLevelMouseProc? _mouseProc;   // campo, no local: si lo recoge el GC, el hook muere

    private delegate IntPtr LowLevelMouseProc(int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc proc, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int code, IntPtr wParam, IntPtr lParam);
    private const int WH_MOUSE_LL = 14, WM_LBUTTONDOWN = 0x0201;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public int X; public int Y; public uint MouseData, Flags, Time; public IntPtr Extra; }

    // Readiness cacheado por superficie: recorrer la ventana entera en cada tick sería pagar un Walk
    // completo a 2,5 Hz durante toda la grabación. Solo se recuenta al cambiar de pantalla.
    private string _readyCacheUrl = "";
    private int _readyCacheCount = -1;

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

            // El SUBDYNPRO cargado en el área de usuario, si lo hay. Sin esto la identidad es demasiado
            // gruesa dentro del Puesto de trabajo (NWP1): abrir una fila del árbol cambia el panel
            // derecho pero NO cambia transacción, programa ni dynpro, así que TODA la transacción tenía
            // una sola identidad. Consecuencias medidas el 2026-07-26 en wf_1785111989995: los 20 pasos
            // del formulario de paciente se sellaron como «NWP1/SAPLN_WP_FRAMEWORK/0100» igual que los
            // clics del árbol, el salto-adelante del player los confundió entre sí y se saltó 19 pasos
            // —el llenado entero del paciente— para ir directo a «Buscar», reportando 29 de 30 hechos.
            //
            // Un subdynpro es una pantalla de SAP con todas las de la ley (área/programa/dynpro), no
            // maquetación: por eso califica como parte del lugar. Los demás contenedores del área de
            // usuario NO se añaden a propósito — son layout, y meterlos haría la identidad tan
            // quisquillosa que grabación y reproducción dejarían de casar por diferencias cosméticas.
            // Si algún día dos paneles distintos resultan indistinguibles sin subdynpro, se extiende AQUÍ.
            string sub = UserAreaSubscreen(session);
            if (sub.Length > 0) path.Append('/').Append(sub);

            return new SurfaceIdentity(
                Origin: $"sapgui://{(system.Length > 0 ? system : "sap")}",
                Pathname: path.ToString(),
                Title: title);
        }
        catch { return SurfaceIdentity.Unknown; }
    }

    /// <summary>
    /// El subdynpro cargado directamente bajo <c>wnd[0]/usr</c> (p.ej. <c>subPATEINST:SAPLNCHD:2000</c>),
    /// o "" si el área de usuario no tiene ninguno. Es el discriminador de PANEL dentro de una misma
    /// transacción; ver el porqué en <see cref="Identity"/>.
    ///
    /// Solo los hijos DIRECTOS y solo el primero: esto corre en cada lectura de identidad —el locator,
    /// y el motor de carga cada 120 ms— y un recorrido en profundidad aquí sería pagar el árbol entero
    /// a esa cadencia. Tres llamadas COM es el presupuesto.
    /// </summary>
    private static string UserAreaSubscreen(dynamic session)
    {
        try
        {
            dynamic? area = session.FindById("wnd[0]/usr", false);
            if (area == null) return "";

            dynamic children = area.Children;
            int count = (int)children.Count;
            for (int i = 0; i < count && i < 12; i++)
            {
                string id;
                try { id = Str(children.ElementAt(i).Id); } catch { continue; }
                if (id.Length == 0) continue;

                // «sub» Y «ssub»: SAP usa los dos prefijos (área de subdynpro dinámica y estática). Con
                // solo «sub» esta pantalla real se escapaba —
                // usr/ssubVIEW_SCREEN:SAPLN1LSTAMB:0007— y la identidad se quedaba corta justo en el
                // Puesto de trabajo, que es donde hacía falta el detalle.
                string leaf = id[(id.LastIndexOf('/') + 1)..];
                if (leaf.StartsWith("ssub", StringComparison.OrdinalIgnoreCase) && leaf.Length > 4) return leaf;
                if (leaf.StartsWith("sub", StringComparison.OrdinalIgnoreCase) && leaf.Length > 3) return leaf;
            }
        }
        catch { /* la pantalla puede cambiar bajo los pies; sin dato es mejor que un dato inventado */ }
        return "";
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
    /// Huella estructural: los IDS de los componentes interactivos de la ventana activa, ordenados y
    /// resumidos en un hash corto. Mismo recorrido que <see cref="ReadinessCount"/> —que solo cuenta—,
    /// pero quedándose con QUIÉNES son y no cuántos: dos pantallas distintas pueden tener 36 elementos.
    /// </summary>
    public string StructureFingerprint()
    {
        dynamic? session;
        try { session = Session(); } catch { return ""; }
        if (session == null) return "";

        dynamic? root = null;
        try { root = session.ActiveWindow; } catch { }
        if (root == null) { try { root = session.FindById("wnd[0]", false); } catch { } }
        if (root == null) return "";

        var acc = new List<dynamic>();
        try { Walk(root, acc, 0); } catch { return ""; }

        var ids = new List<string>();
        foreach (dynamic n in acc)
        {
            try { string id = Str(n.Id); if (id.Length > 0) ids.Add(id); } catch { }
        }

        // LOS SHELLS TAMBIÉN CUENTAN. Sin esto la huella era ciega justo donde vive el contenido: dos
        // pantallas del Puesto de trabajo dieron la MISMA huella (15) porque el allowlist solo mira
        // botones y campos, y lo que cambiaba estaba dentro de un ALV. Se añade el shell con su TAMAÑO
        // —filas del árbol, filas del grid, botones de la barra—, que es un número, no un dato: un
        // árbol con 525 filas sigue teniendo 525 mañana, pero un panel vacío frente a uno cargado no.
        foreach (dynamic shell in AllShells(session))
        {
            string id;
            try { id = Str(shell.Id); } catch { continue; }
            if (id.Length == 0) continue;

            var size = new List<string>();
            try { size.Add($"nodos={(int)shell.GetAllNodeKeys().Count}"); } catch { }
            try { size.Add($"filas={(int)shell.RowCount}"); } catch { }
            try { size.Add($"btns={(int)shell.ToolbarButtonCount}"); } catch { }
            ids.Add(size.Count > 0 ? $"{id}#{string.Join(",", size)}" : id);
        }

        return Fingerprints.Of(ids);
    }

    /// <summary>Todos los shells de la ventana activa, sean del tipo que sean. Para la huella.</summary>
    private static IEnumerable<dynamic> AllShells(dynamic session)
    {
        var acc = new List<dynamic>();
        dynamic? root = null;
        try { root = session.ActiveWindow; } catch { }
        if (root == null) { try { root = session.FindById("wnd[0]", false); } catch { } }
        if (root == null) return acc;

        void Walk(dynamic node, int depth)
        {
            if (depth > 14 || acc.Count > 24) return;
            try { if (Str(node.Type).Contains("Shell", StringComparison.OrdinalIgnoreCase)) acc.Add(node); } catch { }

            dynamic children; int n;
            try { children = node.Children; n = (int)children.Count; } catch { return; }
            for (int i = 0; i < n && i < 200; i++)
            {
                try { Walk(children.ElementAt(i), depth + 1); } catch { }
            }
        }
        try { Walk(root, 0); } catch { }
        return acc;
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

        string? nodeKey = NodeKeyOf(step, step.Selector);
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
            var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Walk(area, found, 0, labels);

            int order = 1;
            foreach (dynamic node in found)
            {
                var field = Describe(node, order, labels);
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
    public (string Key, string Text, string Via)? SelectedTreeNode(string treeId, out string reason)
    {
        dynamic? session;
        try { session = Session(); } catch { reason = "sin sesión SAP"; return null; }
        if (session == null) { reason = "sin sesión SAP"; return null; }

        // El id de los elementos del inspector es ABSOLUTO (/app/con[0]/ses[0]/wnd[0]/…), pero FindById de
        // la SESIÓN resuelve rutas relativas a ella (ver SapSelector). Pasarle el absoluto devolvía null y
        // se salía de aquí sin probar ni un getter — el registro culpaba a los getters de selección de algo
        // que nunca llegaron a intentar. Se prueba normalizado y, por si algún id ya viniera relativo, tal cual.
        dynamic? tree = null;
        string relative = SapSelector.Normalize(treeId);
        try { tree = session.FindById(relative, false); } catch { }
        if (tree == null) { try { tree = session.FindById(treeId, false); } catch { } }
        if (tree == null)
        {
            reason = $"árbol no resuelto por FindById (probado «{relative}» y el absoluto)";
            return null;
        }

        string? key = TrySelectedNodeKey(tree, out string via);
        if (string.IsNullOrEmpty(key))
        {
            reason = "árbol resuelto, pero ningún getter de selección respondió";
            return null;
        }

        reason = "";
        string text = NodeText(tree, key!, TreeColumnNames(tree));
        return (key!, text, via);
    }

    // ── Filas VISIBLES del árbol (para enmarcar fila a fila) ──────────────────

    /// <summary>
    /// Una fila VISIBLE del árbol, con la posición que SAP le atribuye. <paramref name="Top"/> va relativo
    /// al borde del árbol y <paramref name="Height"/> en píxeles: ambos LEÍDOS, nunca estimados.
    /// </summary>
    public sealed record TreeRow(string Key, string Text, int Top, int Height, bool IsFolder);

    /// <summary>
    /// Geometría REAL de una fila, si SAP la suelta. Devuelve alto 0 si no.
    ///
    /// El repo daba por comprobado que <c>GetItemTop/GetItemHeight</c> devuelven 0 «para cualquier clave».
    /// Pero en un árbol de COLUMNAS estos getters toman DOS argumentos —<c>(clave, columna)</c>— igual que
    /// <c>GetItemText(key, col)</c>, que sí responde y es lo que hace funcionar a <see cref="NodeText"/>.
    /// Llamados con un solo argumento fallan por ARIDAD, que desde fuera es indistinguible de "no existe":
    /// de ahí, probablemente, la conclusión de que no hay geometría por nodo. Se prueba con columna y, si
    /// responde, las cajas del inspector salen exactas y sin calibrar nada.
    /// </summary>
    private static void ItemGeometry(
        dynamic tree, string key, List<string> columns, out int itemTop, out int itemHeight)
    {
        // out y no tupla: tree es dynamic, así que la llamada se resuelve en runtime y el resultado sería
        // dynamic — no deconstruible (CS8133). Mismo motivo por el que los descartes de este archivo llevan
        // el tipo escrito.
        itemTop = 0;
        itemHeight = 0;
        foreach (string col in columns)
        {
            int h = TreeInt(tree, "GetItemHeight", key, col);
            if (h <= 0) continue;
            itemHeight = h;
            itemTop = TreeInt(tree, "GetItemTop", key, col);
            return;
        }
    }

    /// <summary>
    /// La geometría que SAP atribuye a UNA fila concreta. Existe para poder CONTRASTARLA con la realidad:
    /// tras un clic se conoce la y de pantalla y la clave de la fila tocada, así que preguntar aquí por esa
    /// misma clave dice si <c>GetItemTop</c> devuelve la posición REAL de ese nodo o algo derivado del índice.
    /// La sospecha viene de haber visto dos árboles distintos —de 589 y 129 px de alto— reportar la misma
    /// secuencia 43/73/103/133, que es lo que haría un valor por slot y no por nodo.
    /// </summary>
    public bool TreeItemGeometry(string treeId, string nodeKey, out int itemTop, out int itemHeight)
    {
        itemTop = 0;
        itemHeight = 0;

        dynamic? session;
        try { session = Session(); } catch { return false; }
        if (session == null) return false;

        dynamic? tree = null;
        try { tree = session.FindById(SapSelector.Normalize(treeId), false); } catch { }
        if (tree == null) { try { tree = session.FindById(treeId, false); } catch { } }
        if (tree == null) return false;

        ItemGeometry(tree, nodeKey, TreeColumnNames(tree), out itemTop, out itemHeight);
        return itemHeight > 0;
    }

    /// <summary>Un getter entero del árbol por enlace tardío con (clave, columna). 0 si no responde.</summary>
    private static int TreeInt(dynamic tree, string method, string key, string column)
    {
        try
        {
            object? r = tree.GetType().InvokeMember(
                method, BindingFlags.InvokeMethod, null, tree, new object[] { key, column });
            return r == null ? 0 : Convert.ToInt32(r);
        }
        catch { return 0; }
    }


    /// <summary>
    /// Las filas VISIBLES del árbol, preguntándole a SAP dónde está cada una. UNA sola regla:
    ///
    ///   <c>si el top que SAP da para la clave cae dentro del alto del árbol, la fila se ve — y su caja es
    ///   exactamente ese top con ese alto.</c>
    ///
    /// POR QUÉ ASÍ: antes esto reconstruía lo visible a partir de <c>topNode</c> (el scroll) + un recorrido
    /// en profundidad respetando el plegado + un alto de fila calibrado con clics. Tres capas, cada una con
    /// su propio modo de fallo, y todas existían para compensar una limitación que resultó no existir: la
    /// creencia de que SAP no da geometría por nodo venía de llamar <c>GetItemTop/GetItemHeight</c> con UN
    /// argumento cuando piden DOS (clave y columna). Verificado contra el SAP real —cuatro clics, los cuatro
    /// dentro de la banda que predice el top crudo—, así que las tres capas se borraron: no hay orden que
    /// acertar, ni scroll que localizar, ni nada que estimar, y por tanto nada que pueda desalinearse.
    /// Se adapta solo a DPI, zoom de SAP y tema de fuente, porque todo eso ya viene dentro de la respuesta.
    ///
    /// EL COSTE es preguntar por todas las claves y no solo por las ~20 visibles. Es la contrapartida
    /// aceptada a cambio de quitar los supuestos; quien llama debe espaciar las llamadas (no en cada cuadro).
    ///
    /// POSICIONES REPETIDAS: dos filas no pueden ocupar la misma y. Si un mismo top aparece en más de dos
    /// claves, no es una posición sino un valor centinela que SAP devuelve para lo que no está visible
    /// (típicamente 0), así que ese grupo se descarta en bloque. Es la regla que evita confundir "invisible"
    /// con "primera fila" sin tener que adivinar cuál es el centinela.
    /// </summary>
    public IReadOnlyList<TreeRow> VisibleTreeRows(string treeId, int treeHeight, out string reason)
    {
        var rows = new List<TreeRow>();
        dynamic? session;
        try { session = Session(); } catch { reason = "sin sesión SAP"; return rows; }
        if (session == null) { reason = "sin sesión SAP"; return rows; }

        dynamic? tree = null;
        try { tree = session.FindById(SapSelector.Normalize(treeId), false); } catch { }
        if (tree == null) { try { tree = session.FindById(treeId, false); } catch { } }
        if (tree == null) { reason = "árbol no resuelto por FindById"; return rows; }

        var columns = TreeColumnNames(tree);
        var keys = AllTreeKeys(tree);
        if (keys.Count == 0) { reason = "el árbol no devolvió ninguna clave"; return rows; }

        // Paso 1: solo geometría (2 llamadas COM por clave). El texto y el tipo de nodo se piden DESPUÉS y
        // únicamente para las que se ven, que son un puñado — describir las 525 sería el derroche que ya
        // hizo parpadear el inspector una vez.
        var placed = new List<(string Key, int Top, int Height)>();
        foreach (string key in keys)
        {
            ItemGeometry(tree, key, columns, out int itemTop, out int itemHeight);
            if (itemHeight <= 0) continue;
            if (itemTop < 0 || itemTop >= treeHeight) continue;
            placed.Add((key, itemTop, itemHeight));
        }

        // Paso 2: descartar los tops compartidos por más de dos claves (centinelas, no posiciones).
        var bogus = placed.GroupBy(p => p.Top).Where(g => g.Count() > 2).Select(g => g.Key).ToHashSet();
        int dropped = placed.Count(p => bogus.Contains(p.Top));

        foreach (var p in placed.Where(p => !bogus.Contains(p.Top)).OrderBy(p => p.Top))
            rows.Add(new TreeRow(
                p.Key, NodeText(tree, p.Key, columns), p.Top, p.Height, BoolOf(tree, "IsFolder", p.Key)));

        reason = $"{rows.Count} filas visibles de {keys.Count} claves" +
                 (dropped > 0 ? $"; {dropped} descartadas por compartir posición (centinela)" : "");
        return rows;
    }

    /// <summary>
    /// Clave del nodo seleccionado probando las variantes de la API de árbol. null si ninguna responde.
    /// <paramref name="via"/> dice QUÉ getter contestó (o "ninguno"), para que el registro lo nombre: sin
    /// eso, un árbol que no suelta su selección es indistinguible de un árbol sin fila seleccionada.
    /// </summary>
    private static string? TrySelectedNodeKey(dynamic tree, out string via)
    {
        // 1. GetSelectedNodes() → GuiCollection de claves (árboles de columnas / lista, multi-selección).
        try
        {
            dynamic sel = tree.GetSelectedNodes();
            int n = (int)sel.Count;
            if (n > 0)
            {
                string k = Str(sel.ElementAt(0));
                if (k.Length > 0) { via = "GetSelectedNodes"; return k; }
            }
        }
        catch { }

        // 2. Propiedad de clave única. El casing exacto varía entre controles: se prueban las formas
        //    documentadas por enlace tardío, tolerando la ausencia de cada una.
        //
        //    selectedItemNode va PRIMERO y es el que faltaba. En un ÁRBOL DE COLUMNAS —el de SAP Easy
        //    Access— lo que se selecciona al clicar una fila es un ITEM de columna, no un nodo, así que
        //    selectedNode queda vacío y la fila parecía imposible de leer: el registro decía "getters de
        //    selección sin resultado" en cada clic de árbol, incluso sondeando 600 ms. La clave del nodo
        //    dueño de ese item vive en selectedItemNode (su columna, en selectedItemColumn, que no nos
        //    hace falta: accionamos la FILA).
        //
        //    OJO: aquí NO va topNode. Es la primera fila VISIBLE (la posición del scroll), no la
        //    seleccionada — comprobado contra el SAP real, donde con selectedNode vacío topNode valía
        //    vw00722. Tomarlo como selección hacía que cada scroll pareciera un clic: la grabación
        //    habría inventado un paso por cada rueda de ratón sobre el árbol.
        foreach (string prop in new[]
                 { "selectedItemNode", "SelectedItemNode", "selectedNode", "SelectedNode", "GetSelectedNode" })
        {
            try
            {
                string k = Str(tree.GetType().InvokeMember(prop, BindingFlags.GetProperty, null, tree, null)).Trim();
                if (k.Length > 0) { via = prop; return k; }
            }
            catch { }
        }

        via = "ninguno";
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
                    // Solo el RECUENTO de filas, no su descripción. El inspector pinta la caja del árbol
                    // con la geometría del shell y un rótulo con el número; cada nodo que se describía se
                    // tiraba acto seguido (SapInspectorReader.Read salta todo lo que no trae bounds, y los
                    // nodos nunca traen). Describir fila a fila costaba GetNodeTextByKey + un GetItemText
                    // por columna + GetNodePathByKey + IsFolder + IsFolderExpanded —hasta ~24 viajes COM
                    // por nodo, miles en un árbol clínico, en cada refresco— para conservar solo el número.
                    // Quien SÍ necesita la fila (grabar y reproducir) la pide por clave en el momento: ver
                    // ObserveTreeSelection y ResolveNodeKey.
                    // Solo el DATO (cuántas filas). El rótulo lo compone el cliente, que además sostiene el
                    // último recuento bueno si SAP falla en un refresco: ver SapInspectorReader.Read.
                    acc.Add(el with { ChildCount = CountTreeNodes(node) });
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
    /// CUÁNTAS filas tiene el árbol, sin describir ninguna. Es todo lo que el inspector necesita del
    /// contenido de un árbol: la caja se dibuja con la geometría del shell, el rótulo solo dice el número
    /// ("Favoritos · 20 nodos"), y un recuento &gt; 0 es lo que lo marca como MAPEADO — gris neutro en vez
    /// de ámbar de "territorio desconocido" (ver <c>SapInspectorReader.SapBox.IsMapped</c>).
    ///
    /// DE UNA SOLA LLAMADA cuando se puede: <c>GetAllNodeKeys()</c> devuelve TODAS las claves cargadas de
    /// golpe (1197 en el árbol clínico real). El recorrido en anchura por <c>GetNodesCol</c> +
    /// <c>GetSubNodesCol</c> queda de RESPALDO para controles que no expongan GetAllNodeKeys.
    ///
    /// Cuenta CLAVES, no textos legibles. Antes se construía un elemento por fila y se descartaban las
    /// que no soltaban texto, así que un árbol cuyo texto vive en un item de columna que no responde daba
    /// 0 y salía en ámbar de "sin mapear" — aunque sus claves estuvieran ahí y fueran perfectamente
    /// accionables por <c>doubleClickNode</c>. La clave ES la identidad que se acciona; el texto es
    /// decoración del rótulo.
    ///
    /// Las carpetas colapsadas cuyos hijos aún no se han traído del servidor no cuentan — es correcto: no
    /// las expandimos pasivamente (expandir dispara un viaje al servidor y, en un SAP clínico, puede
    /// disparar lógica de negocio); el agente las desplegará cuando navegue.
    /// </summary>
    private static int CountTreeNodes(dynamic tree)
    {
        // El TAMAÑO de la colección, en UNA llamada COM. Traer las claves una por una (ElementAt) costaba
        // una llamada por fila —361 en SAP Easy Access, con el refresco cada 200 ms son ~1.800 llamadas
        // por segundo— y era la causa del parpadeo verde/ámbar: con SAP ocupado alguna de esas cientos de
        // llamadas fallaba, el recuento se caía a 0 y la caja se repintaba como "sin mapear". Para colorear
        // y rotular basta cuántas hay; las claves solo se piden cuando de verdad se va a accionar una fila.
        try
        {
            dynamic col = tree.GetAllNodeKeys();
            int n = (int)col.Count;
            if (n > 0) return n;
        }
        catch { /* el control no expone GetAllNodeKeys: respaldo abajo */ }

        return AllTreeKeys(tree).Count;
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

    /// <summary>
    /// Las columnas por las que intentar activar una fila, la más probable primero: la que TIENE texto
    /// para esa clave. Es la celda que el operador ve y sobre la que hizo el doble clic; las columnas
    /// vacías para esa fila son las que menos sentido tienen. El resto va detrás, por si acaso.
    /// </summary>
    private static IEnumerable<string> PreferredColumnsFor(dynamic tree, string key, List<string> columns)
    {
        var withText = new List<string>();
        var rest = new List<string>();
        foreach (string col in columns)
        {
            string t = "";
            try { t = Str(tree.GetItemText(key, col)).Trim(); } catch { }
            (t.Length > 0 ? withText : rest).Add(col);
        }
        foreach (string c in withText) yield return c;
        foreach (string c in rest) yield return c;
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

    /// <summary>
    /// Recorre el área de usuario juntando los controles interactivos y, de paso, ANOTANDO LAS
    /// ETIQUETAS.
    ///
    /// En un dynpro el texto que el humano lee no está en el campo: vive en un GuiLabel aparte, y la
    /// relación entre los dos es por IDENTIDAD, no por posición — el Name del label es el del campo
    /// con un «*» delante. Verificado contra el SAP real (2026-07-28, pantalla NWP1/SAPLY000):
    ///
    ///   GuiTextField  Name = Y0000000-ZTXTTALLA   Tooltip = (vacío)   Text = «1.70»
    ///   GuiLabel      Name = *Y0000000-ZTXTTALLA                      Text = «Talla»
    ///
    /// Se anotan en ESTE recorrido y no preguntando por el padre de cada campo a posteriori: ya
    /// pasamos por todos los nodos, y en esta API lo caro son las llamadas COM. Hacerlo después
    /// habría multiplicado por los hermanos de cada campo.
    /// </summary>
    private static void Walk(dynamic node, List<dynamic> acc, int depth, Dictionary<string, string>? labels = null)
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
                    string ctype = Str(child.Type);
                    if (Interactive.Contains(ctype))
                    {
                        acc.Add(child);
                    }
                    else if (labels != null && string.Equals(ctype, "GuiLabel", StringComparison.OrdinalIgnoreCase))
                    {
                        string ln = Str(child.Name);
                        // Solo los que APUNTAN a un campo («*NOMBRE»). Los demás son unidades y
                        // rótulos sueltos —«Kg», «mm Hg», «x min»— que no identifican a nadie y
                        // llenarían el mapa de ruido.
                        if (ln.Length > 1 && ln[0] == '*')
                        {
                            string txt = Str(child.Text).Trim();
                            if (txt.Length > 0) labels[ln.Substring(1)] = txt;
                        }
                    }
                }
                catch { }

                try { Walk(child, acc, depth + 1, labels); } catch { }
            }
        }
        catch { /* el componente no tiene hijos */ }
    }

    // ── Filas de ALV (GridView) ──────────────────────────────────────────────
    //
    // Un ALV no expone sus filas como componentes: no tienen Id y un recorrido del árbol no las ve.
    // Se accionan por índice contra el shell, pero el índice NO se persiste: se resuelve en cada
    // ejecución a partir de los pares columna=valor que lleva el selector.

    /// <summary>
    /// Último estado visto por grid, para distinguir «el operador eligió» de «así vino la pantalla».
    ///
    /// Hacen falta LAS DOS señales. Mirar solo la fila actual no sirve: en una lista de trabajo con
    /// UN paciente, <c>CurrentCellRow</c> vale 0 antes y después del clic —comprobado el
    /// 2026-07-28, y por eso la primera versión de esto no publicó nada—. Lo que cambia ahí es la
    /// selección, que pasa de vacía a «0».
    /// </summary>
    private readonly Dictionary<string, int> _gridRow = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _gridSel = new(StringComparer.Ordinal);

    /// <summary>La primera fila de un <c>SelectedRows</c> («0», «0,2», «1-3»), o -1 si no hay ninguna.</summary>
    private static int FirstRowOf(string selectedRows)
    {
        foreach (char c in selectedRows ?? "")
        {
            if (char.IsDigit(c)) break;
            if (c != ' ') return -1;
        }
        var digits = new string((selectedRows ?? "").TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out int r) ? r : -1;
    }

    private static List<KeyValuePair<string, string>> ParseRowKey(string rowKey)
    {
        var pairs = new List<KeyValuePair<string, string>>();
        foreach (string part in (rowKey ?? "").Split('|'))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) continue;
            string col = part.Substring(0, eq).Trim();
            if (col.Length > 0) pairs.Add(new KeyValuePair<string, string>(col, part.Substring(eq + 1).Trim()));
        }
        return pairs;
    }

    private static string GridCell(dynamic grid, int row, string column)
    {
        try { return Str(grid.GetCellValue(row, column)).Trim(); }
        catch { return ""; }
    }

    /// <summary>
    /// Deja SELECCIONADA la fila que casa con los pares del selector.
    ///
    /// Gana la que casa con MÁS pares, y solo si es única: dos filas empatadas significan que la
    /// clave no distingue, y elegir entre ellas es elegir un paciente al azar. Si no casa ninguna,
    /// esa entrada ya no está en la lista y el paso FALLA — mejor un workflow detenido que uno que
    /// abre la historia clínica de otra persona.
    /// </summary>
    private bool SelectGridRow(dynamic grid, string rowKey, PlanStep step, out string error)
    {
        error = "";
        var pairs = ParseRowKey(rowKey);
        if (pairs.Count == 0) { error = $"el paso «{step.Label}» no trae con qué identificar la fila"; return false; }

        int rows;
        try { rows = (int)grid.RowCount; }
        catch (Exception e) { error = $"el ALV no dijo cuántas filas tiene: {e.Message}"; return false; }
        if (rows <= 0) { error = "la lista está vacía: no hay ninguna fila que seleccionar"; return false; }

        int best = -1, bestScore = 0, tied = 0;
        for (int r = 0; r < rows; r++)
        {
            int score = pairs.Count(p =>
                string.Equals(GridCell(grid, r, p.Key), p.Value, StringComparison.OrdinalIgnoreCase));
            if (score == 0) continue;
            if (score > bestScore) { bestScore = score; best = r; tied = 1; }
            else if (score == bestScore) tied++;
        }

        if (best < 0)
        {
            error = $"ninguna de las {rows} fila(s) casa con «{step.Label}»: esa entrada ya no está en la lista";
            return false;
        }
        if (tied > 1)
        {
            error = $"{tied} filas casan igual de bien con «{step.Label}»: la clave no distingue y no se elige al azar";
            return false;
        }

        try
        {
            grid.GetType().InvokeMember("SelectedRows", BindingFlags.SetProperty, null, grid,
                new object[] { best.ToString() });
        }
        catch (Exception e) { error = $"el ALV no aceptó SelectedRows=«{best}»: {e.Message}"; return false; }

        // La celda actual además de la selección: el clic humano pone las dos, y no sabemos cuál de
        // las dos mira cada botón de la barra. Si falla, la selección ya está hecha.
        try
        {
            grid.GetType().InvokeMember("SetCurrentCell", BindingFlags.InvokeMethod, null, grid,
                new object[] { best, pairs[0].Key });
        }
        catch { }

        Diagnostic?.Invoke(this,
            $"fila «{step.Label}» seleccionada: fila {best} de {rows} ({bestScore}/{pairs.Count} campo(s) casados)");
        return true;
    }

    /// <summary>
    /// Identidad de una fila: hasta cuatro pares columna=valor con contenido de verdad.
    ///
    /// Se descartan los iconos (<c>@KN\Q…@</c>, que son códigos de pintado y no dicen quién es), los
    /// marcadores de refresco de este ALV (<c>*** Aktualizar ***</c>), que salen iguales en toda
    /// fila, y cualquier valor con <c>|</c> o <c>=</c>, que rompería el fragmento.
    /// </summary>
    private static string RowKeyAt(dynamic grid, int row, out string label)
    {
        var parts = new List<string>();
        var shown = new List<string>();
        try
        {
            dynamic cols = grid.ColumnOrder;
            int n = (int)cols.Count;
            for (int i = 0; i < n && parts.Count < 4; i++)
            {
                string col;
                try { col = Str(cols.ElementAt(i)).Trim(); } catch { continue; }
                if (col.Length == 0) continue;

                string v = GridCell(grid, row, col);
                if (v.Length == 0 || v[0] == '@') continue;
                if (v.StartsWith("***", StringComparison.Ordinal)) continue;
                if (v.Contains('|') || v.Contains('=')) continue;

                parts.Add(col + "=" + v);
                shown.Add(v);
            }
        }
        catch { }

        // Para el rótulo, lo que un humano reconoce: el primer valor con letras («GIRALDO») antes que
        // una fecha o una hora, que no distinguen nada a la vista.
        label = shown.FirstOrDefault(v => v.Any(char.IsLetter)) ?? (shown.Count > 0 ? shown[0] : "");
        return string.Join("|", parts);
    }

    /// <summary>Los ALV (GridView) del área de usuario de la pantalla activa.</summary>
    private static List<dynamic> GridViews(dynamic session)
    {
        var found = new List<dynamic>();
        try
        {
            dynamic area = session.FindById("wnd[0]/usr", false);
            if (area != null) CollectGrids(area, found, 0);
        }
        catch { }
        return found;
    }

    private static void CollectGrids(dynamic node, List<dynamic> acc, int depth)
    {
        if (depth > 20 || acc.Count > 20) return;
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
                    if (string.Equals(Str(child.Type), "GuiShell", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(Str(child.SubType), "GridView", StringComparison.OrdinalIgnoreCase))
                        acc.Add(child);
                }
                catch { }
                try { CollectGrids(child, acc, depth + 1); } catch { }
            }
        }
        catch { }
    }

    /// <summary>
    /// Publica la SELECCIÓN DE FILA de un ALV mientras se enseña.
    ///
    /// Va por temporizador y no por el hook de ratón porque seleccionar una fila es cliente puro: no
    /// viaja al servidor, así que el StartRequest donde se publican los clics no llega nunca. Es el
    /// mismo motivo por el que las filas de árbol tienen su propio publicador — y la razón de que
    /// hasta hoy el clic del operador sobre el paciente se descartara SIEMPRE, con el mensaje «el
    /// clic cayó en shell, no en un botón».
    ///
    /// La PRIMERA lectura de cada grid no se publica: es la pantalla presentándose con su fila por
    /// defecto, no algo que el operador hiciera. Publicarla metería un paso fantasma en cada
    /// grabación, que es exactamente el fallo que ya costó una jornada.
    /// </summary>
    private void PublishGridSelections(dynamic session)
    {
        if (StepObserved == null) return;

        foreach (dynamic grid in GridViews(session))
        {
            string gid;
            try { gid = Str(grid.Id); } catch { continue; }
            if (gid.Length == 0) continue;

            int cur;
            try { cur = (int)grid.CurrentCellRow; } catch { continue; }

            string sel = "";
            try
            {
                sel = Str(grid.GetType().InvokeMember(
                    "SelectedRows", BindingFlags.GetProperty, null, grid, null)).Trim();
            }
            catch { }

            bool primera = !_gridRow.ContainsKey(gid);
            int prevCur = primera ? int.MinValue : _gridRow[gid];
            string prevSel = _gridSel.TryGetValue(gid, out string? ps) ? ps : "";
            _gridRow[gid] = cur;
            _gridSel[gid] = sel;
            if (primera) continue;

            // Se publica al SELECCIONAR, no al deseleccionar: quitar la marca no es una acción que
            // haya que reproducir, y grabarla dejaría un paso que al ejecutarse no hace nada.
            bool eligio = sel.Length > 0 && sel != prevSel;
            bool movio = cur != prevCur;
            if (!eligio && !movio) continue;

            int fila = sel.Length > 0 ? FirstRowOf(sel) : cur;
            if (fila < 0) fila = cur;
            if (fila < 0) continue;

            string key = RowKeyAt(grid, fila, out string label);
            if (key.Length == 0) continue;

            string node = SafeNodeUrl();
            if (node.Length == 0) node = _preNodeUrl;

            StepObserved?.Invoke(this, new ObservedStep(
                ActionType: "click",
                Selector: SapSelector.ByRow(gid, key),
                Label: label.Length > 0 ? label : $"fila {fila}",
                ControlType: "row",
                Value: null,
                AllowedOptions: null,
                SelectedValue: null,
                SelectedLabel: null,
                SurfaceSection: null,
                AlternativeTargets: Array.Empty<string>())
            {
                Surface = node,
                Readiness = node == _preNodeUrl ? _preReadiness : CachedReadinessMeta(node),
                Fingerprint = _preFingerprint,
            });

            Diagnostic?.Invoke(this, $"observado: fila de ALV «{label}» ({key}) en {gid} desde '{node}'");
        }
    }

    private static DetectedField? Describe(dynamic node, int order, Dictionary<string, string>? labels = null)
    {
        try
        {
            string type = Str(node.Type);
            string id = Str(node.Id);
            if (id.Length == 0) return null;

            string label = LabelOf(node, labels);
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
    /// Cómo se llama el campo para un humano.
    ///
    /// Orden: Tooltip → GuiLabel hermano → Name → Text.
    ///
    /// El Tooltip sigue primero porque cuando existe SUELE ser el texto del label de al lado, y no
    /// cambiarle la precedencia deja intacto todo lo que hoy funciona en otras pantallas. Lo nuevo
    /// es el segundo escalón, y es el que importa: en el dynpro clínico el Tooltip viene VACÍO, así
    /// que hasta ahora se caía a <c>Name</c> y el «nombre humano» acababa siendo
    /// «Y0000000-ZTXTTALLA». Con eso, emparejar por etiqueta —que es TODO lo que hace
    /// ConceptBinder— era imposible: ninguno de los ocho signos vitales casaba, nunca.
    ///
    /// El mapa lo construye <see cref="Walk"/> por identidad («*NOMBRE» → NOMBRE), no por
    /// cercanía en pantalla: dos campos contiguos con la etiqueta encima se resolverían al revés
    /// por coordenadas, y aquí se está decidiendo dónde va una cifra clínica.
    ///
    /// Name y Text siguen de último como red: un campo sin label hermano al menos se identifica.
    /// </summary>
    private static string LabelOf(dynamic node, Dictionary<string, string>? labels = null)
    {
        try
        {
            string tip = Str(node.GetType().InvokeMember("Tooltip", BindingFlags.GetProperty, null, node, null));
            if (tip.Trim().Length > 0) return tip.Trim();
        }
        catch { }

        if (labels != null && labels.Count > 0)
        {
            try
            {
                string name = Str(node.GetType().InvokeMember("Name", BindingFlags.GetProperty, null, node, null));
                if (name.Length > 0 && labels.TryGetValue(name, out string? human) && human.Length > 0)
                    return human;
            }
            catch { }
        }

        foreach (string prop in new[] { "Name", "Text" })
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

            // Un botón de toolbar tampoco es un componente: el id resuelve al SHELL y el botón viaja
            // en el fragmento. Se pulsa con PressToolbarButton, sin coordenadas.
            string? tbButton = SapSelector.ToolbarButtonOf(selector);
            if (tbButton != null)
            {
                if (TryInvoke(node, "PressToolbarButton", tbButton))
                {
                    Diagnostic?.Invoke(this, $"botón de toolbar «{step.Label}» ({tbButton}) pulsado en {id}");
                    return true;
                }
                error = $"el shell {id} no aceptó PressToolbarButton(«{tbButton}») para «{step.Label}»";
                return false;
            }

            // Una fila de ALV tampoco es un componente. Y no basta con «pulsar luego el botón»: el
            // botón de la barra actúa sobre la fila SELECCIONADA, y sin selección se acepta sin
            // hacer nada. Este paso es el que pone esa selección.
            string? rowKey = SapSelector.RowKeyOf(selector);
            if (rowKey != null)
            {
                try { return SelectGridRow(node, rowKey, step, out error); }
                catch (Exception e)
                {
                    error = $"SAP rechazó seleccionar la fila «{step.Label}» ({id}): {e.Message}";
                    return false;
                }
            }

            // Una fila de árbol no es un componente: el id resuelve al ÁRBOL y la fila viaja aparte.
            string? nodeKey = NodeKeyOf(step, selector);
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
    /// <summary>
    /// Acciona una FILA de árbol. Instancia, no estática, para poder contar por <see cref="Diagnostic"/>
    /// qué rama tomó: <c>TryInvoke</c> devuelve true cuando la llamada COM no lanzó, que NO es lo mismo
    /// que «SAP hizo algo». Un <c>doubleClickNode</c> que SAP ignora en silencio es indistinguible de
    /// uno que navegó — y así se ve en el registro del 2026-07-26: pasos con <c>done=True</c> y la
    /// pantalla <c>SIN CAMBIO</c>. Sin saber qué rama corrió no se puede arreglar sin adivinar.
    /// </summary>
    private bool ApplyToNode(dynamic tree, string recordedKey, PlanStep step, out string error)
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

        // OJO a esta línea cuando una fila diga ✓ sin que pase nada: la grabación NO distingue «el
        // operador desplegó una carpeta» de «el operador la activó y SAP navegó» — ambas llegan como un
        // click sin Value. Aquí se ADIVINA por IsFolder, y si la fila es carpeta se toma la rama toggle:
        // despliega, devuelve true, y la pantalla no cambia. Es candidato nº1 a explicar los pasos 3/4
        // de wf_1785109929654. No se cambia a ciegas: primero que el registro diga qué rama corrió.
        Diagnostic?.Invoke(this, $"fila «{step.Label}» clave={key} isFolder={isFolder} → modo «{mode}»");

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
                // ÁRBOL DE COLUMNAS PRIMERO. Medido el 2026-07-26 en el árbol de NWP1:
                //   sap: fila «Triage Administrativo» clave=vw00030 isFolder=False → modo «activate»
                //   sap: fila «Triage Administrativo»: doubleClickNode aceptado (sin excepción)
                //   workflow: ← paso 4 ejecutado: done=True · ubicación DESPUÉS=… (SIN CAMBIO)
                // doubleClickNode EXISTE, no lanza, y no hace nada. El orden anterior —nodo primero, item
                // solo si el nodo LANZABA— hacía que el respaldo bueno no se probara jamás. Es la misma
                // trampa que la aridad de los getters de geometría: en un árbol de columnas la fila no es
                // un nodo, es un ITEM (clave, columna), y las llamadas por nodo se aceptan en vacío.
                //
                // Si el árbol expone columnas, se activa por item. Si no expone ninguna, no es de columnas
                // y se va por nodo como siempre — el comportamiento viejo queda intacto donde era correcto.
                var columns = TreeColumnNames(tree);
                foreach (string col in PreferredColumnsFor(tree, key, columns))
                    if (TryInvoke(tree, "doubleClickItem", key, col))
                    {
                        Diagnostic?.Invoke(this, $"fila «{step.Label}»: vía doubleClickItem(col={col})");
                        return true;
                    }

                if (TryInvoke(tree, "doubleClickNode", key))
                {
                    Diagnostic?.Invoke(this, columns.Count == 0
                        ? $"fila «{step.Label}»: vía doubleClickNode (el árbol no expone columnas)"
                        : $"fila «{step.Label}»: vía doubleClickNode tras fallar los {columns.Count} item(s) "
                          + "de columna. Aceptado ≠ ejecutado: si la pantalla no cambia, mirar aquí.");
                    return true;
                }

                error = "el árbol no aceptó doubleClickItem ni doubleClickNode sobre esa fila";
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

    /// <summary>
    /// La CLAVE de fila del paso: la del PlanStep si trae una, y si no la del fragmento del selector
    /// (<c>…/shell#node=vw00030</c>). Devuelve null si el paso no apunta a una fila.
    ///
    /// Existe por un bug que costó tres rondas de diagnóstico. Estaba escrito
    /// <c>step.NodeKey ?? SapSelector.NodeKeyOf(selector)</c>, y <c>??</c> solo cae al respaldo cuando el
    /// primero es NULL. Graph serializa el campo ausente como CADENA VACÍA (<c>"nodeKey":""</c>), no como
    /// null — así que el respaldo nunca corría, la clave quedaba en "" y el paso dejaba de reconocerse
    /// como fila de árbol. Se iba entonces por la rama de control normal: <c>Apply()</c> → un click sobre
    /// un shell → <c>node.SetFocus()</c> → <c>return true</c>. Enfocaba el árbol, no abría nada, y
    /// reportaba éxito. Ese era el «done=True · SIN CAMBIO» de todos los clics de árbol, y también el
    /// motivo de que el diagnóstico de <see cref="ApplyToNode"/> nunca apareciera en el registro: ese
    /// método jamás llegó a ejecutarse.
    ///
    /// Vacío y ausente son lo mismo aquí. Cualquier dato que venga de Graph merece esa lectura.
    /// </summary>
    private static string? NodeKeyOf(PlanStep step, string selector)
    {
        if (!string.IsNullOrWhiteSpace(step.NodeKey)) return step.NodeKey!.Trim();
        string? fromSelector = SapSelector.NodeKeyOf(selector);
        return string.IsNullOrWhiteSpace(fromSelector) ? null : fromSelector;
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

            // Línea base de los campos ANTES de nada, en AMBOS modos. Estaba solo en la rama de sondeo:
            // con eventos COM no había línea base, así que el primer Change diffeaba contra un
            // diccionario vacío y emitía un paso fantasma por CADA campo no vacío del dynpro. El camino
            // del árbol sí tomaba la suya (más abajo); este no. Además _lastSnapshot no se limpia al
            // parar, así que sin esta asignación una segunda grabación heredaba la de la primera.
            _lastSnapshot = SafeReadFields().ToDictionary(f => f.Selector, f => f.CurrentValue);
            _snapshotSurface = SafeNodeUrl();   // el snapshot nace atado a SU pantalla

            _readyCacheUrl = ""; _readyCacheCount = -1;
            RefreshPreflight(session);

            // Va DESPUÉS del refresco, a propósito: lo que ya hubiera escrito en la barra de comandos
            // antes de pulsar «Enseñar» es línea base, no un paso del operador — la misma disciplina que
            // _lastSnapshot y _lastTreeSelection. Y sin este reset lo heredaría la siguiente grabación.
            _pendingOkCode = "";

            _comEvents = new SapComEvents(session);
            _comEvents.Diagnostic += (_, msg) => Diagnostic?.Invoke(this, $"[com] {msg}");
            _comEvents.Raised += (_, e) => OnSapEvent(session, e.Name);

            if (_comEvents.TryHook(out string reason))
            {
                Diagnostic?.Invoke(this, "observando por eventos COM de SAP GUI (Change/StartRequest/…).");
            }
            else
            {
                Diagnostic?.Invoke(this,
                    $"eventos COM no disponibles ({reason}); cae a sondeo (no detecta clics sin cambio de valor, "
                    + "ni la entrada a una transacción: sin StartRequest no hay instante ANTES del viaje).");
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
            _treeTimer.Tick += (_, __) =>
            {
                if (SessionBusy(session)) return;
                // El refresco va ANTES de publicar, y solo con SAP ocioso: así la sombra es siempre la
                // pantalla de origen. Durante el round-trip este tick se salta entero, que es lo que
                // mantiene la sombra congelada en el origen hasta que la navegación termina.
                RefreshPreflight(session);
                PublishTreeSelections();
                PublishGridSelections(session);
            };
            _treeTimer.Start();

            // El hook de ratón vive en ESTE hilo, que es el único con bomba de mensajes propia
            // (Dispatcher.Run más abajo). WH_MOUSE_LL exige una, o los eventos no llegan nunca.
            _mouseProc = MouseHookCallback;
            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, IntPtr.Zero, 0);
            Diagnostic?.Invoke(this, _mouseHook != IntPtr.Zero
                ? "hook de ratón puesto: los botones del dynpro se identificarán por FindByPosition"
                : "NO se pudo poner el hook de ratón: los botones que no cambian ningún campo no se grabarán");

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
    /// Reparte los eventos COM de la sesión. <c>StartRequest</c> NO es uno más: es el único instante en
    /// que la pantalla de ORIGEN sigue en pie y el campo de comandos aún conserva lo tecleado (SAP lo
    /// vacía al ejecutar la transacción). Todo lo que haya que leer "antes del viaje" se lee aquí.
    /// </summary>
    private void OnSapEvent(dynamic session, string name)
    {
        if (!string.Equals(name, "StartRequest", StringComparison.OrdinalIgnoreCase))
        {
            PublishChangedFields();
            return;
        }

        // StartRequest cae en el BORDE del round-trip. Con Busy=true cualquier llamada al scripting se
        // bloquea SIN retorno (spec oficial, ver INVESTIGACION-SAPGUI-UIA.md) y colgaría este hilo de
        // bombeo para siempre — la grabación moriría en silencio. Por eso se pregunta primero: si SAP ya
        // arrancó, no se le toca y se publica solo desde la sombra, que no habla con COM.
        bool idle = !SessionBusy(session);
        if (idle) PublishChangedFields();

        // El orden es el del operador: primero rellenó los campos, luego DISPARÓ el viaje. Y el disparo
        // es una cosa o la otra —tecleó una transacción y pulsó Enter, o pulsó un botón—, nunca las dos.
        if (!PublishTransactionEntry(session, idle) && idle)
            PublishFocusedButton(session);
    }

    /// <summary>
    /// Graba el BOTÓN que disparó el viaje al servidor, si se puede saber cuál fue.
    ///
    /// EL AGUJERO QUE CIERRA (medido con el vídeo del 2026-07-26): en el Puesto de trabajo, la fila del
    /// árbol abre la lista en el panel derecho — eso sí se graba— pero lo que salta a la transacción
    /// NV2000 es el botón «Crear Triage Administrativo» de la barra de ese panel. Un botón no cambia
    /// ningún valor de campo, así que el diff de <see cref="PublishChangedFields"/> no lo ve, y el
    /// workflow quedaba sin el paso que lo lleva a la pantalla siguiente: se detenía ahí siempre.
    ///
    /// LA APUESTA, dicha en voz alta: SAP no documenta «qué control disparó el round-trip», pero en
    /// <c>StartRequest</c> la pantalla de origen sigue viva y el botón pulsado suele conservar el foco.
    /// No se da por hecho que la propiedad exista: se prueban varios nombres y se DEJA EN EL REGISTRO
    /// cuál respondió, o que ninguno lo hizo. Un catch mudo aquí volvería a hacer indistinguible «la API
    /// no lo expone» de «lo llamamos mal», que es el error nº3 del CLAUDE.md.
    ///
    /// SOLO se graba si el elemento con foco es un BOTÓN. Si el foco está en un campo de texto, el
    /// disparo fue un Enter sobre ese campo y no lo sabemos distinguir: grabar un «clic» ahí inventaría
    /// un paso que el operador no dio. Sin dato es mejor que con dato falso.
    /// </summary>
    private void PublishFocusedButton(dynamic session)
    {
        // El foco a nivel de sesión o ventana NO EXISTE en esta API. Comprobado por introspección
        // ITypeInfo contra el SAP real (2026-07-26): GuiSession expone FindById, SendCommand,
        // StartTransaction, FindByPosition, GetObjectTree…, y GuiFrameWindow expone SetFocus —escribir—
        // pero NINGÚN getter de foco. Por eso aquí ya no se prueba focusedElement y compañía: no es que
        // fallen, es que no están, y dejar el intento haría pensar que algún día responderán.
        //
        // Donde SÍ hay foco es DENTRO del shell: GuiGridView expone GetToolbarFocusButton. Y es justo
        // donde vive el botón que importa: «Crear Triage Administrativo» es el item NV44 de la barra del
        // ALV, no un GuiButton. Ni el diff de campos ni un recorrido de componentes lo verían nunca —
        // por eso el workflow se quedaba siempre sin el paso que salta a NV2000.
        string shellId = "", buttonId = "", label = "";
        foreach (dynamic grid in ToolbarShells(session))
        {
            string focused = "";
            try
            {
                focused = Str(grid.GetType().InvokeMember(
                    "GetToolbarFocusButton", BindingFlags.InvokeMethod, null, grid, null)).Trim();
            }
            catch { }
            if (focused.Length == 0) continue;

            // GetToolbarFocusButton devuelve el ÍNDICE, no la clave. Verificado contra el SAP real
            // (2026-07-26): con «Crear Triage Administrativo» pulsado responde «3», y el 3 de esa barra
            // es NV44. Guardar el índice como si fuera la clave daba un selector que no resolvería y una
            // etiqueta vacía. El índice además NO es estable —depende de qué botones muestre la barra en
            // esa pantalla y de la autorización del usuario—, así que se convierte a clave AQUÍ, al
            // grabar, y lo que se persiste es la clave.
            buttonId = ToolbarButtonIdAt(grid, focused);
            if (buttonId.Length == 0) continue;

            try { shellId = Str(grid.Id); } catch { }
            label = ToolbarButtonLabel(grid, buttonId);
            break;
        }

        if (buttonId.Length == 0 || shellId.Length == 0)
        {
            PublishClickedButton(session);
            return;
        }

        string node = SafeNodeUrl();
        if (node.Length == 0) node = _preNodeUrl;

        StepObserved?.Invoke(this, new ObservedStep(
            ActionType: "click",
            Selector: SapSelector.ByToolbarButton(shellId, buttonId),
            Label: label.Length > 0 ? label : buttonId,
            ControlType: "button",
            Value: null,
            AllowedOptions: null,
            SelectedValue: null,
            SelectedLabel: null,
            SurfaceSection: null,
            AlternativeTargets: Array.Empty<string>())
        {
            Surface = node,
            Readiness = node == _preNodeUrl ? _preReadiness : CachedReadinessMeta(node),
            Fingerprint = _preFingerprint,
        });

        Diagnostic?.Invoke(this,
            $"observado: botón de toolbar «{label}» ({buttonId}) en {shellId} desde '{node}'");
    }

    /// <summary>
    /// Último recurso para saber qué disparó el viaje: preguntarle a SAP quién hay bajo el último clic.
    ///
    /// Solo se graba si resulta ser un BOTÓN. Si el clic cayó en un campo de texto, el viaje lo disparó
    /// un Enter sobre ese campo y no hay clic que reproducir; grabarlo inventaría un paso que el
    /// operador no dio. El valor se guarda como el texto del botón, que es lo que Apply() espera.
    /// </summary>
    private void PublishClickedButton(dynamic session)
    {
        string id = ClickedComponentId();
        if (id.Length == 0)
        {
            Diagnostic?.Invoke(this, "round-trip sin código de transacción, sin botón de toolbar con foco y "
                + "sin clic reciente que SAP reconozca: este paso NO se graba.");
            return;
        }

        dynamic? comp = null;
        try { comp = session.FindById(SapSelector.Normalize(id), false); } catch { }
        if (comp == null) { Diagnostic?.Invoke(this, $"el clic apuntaba a {id}, pero ya no resuelve"); return; }

        string type = "", label = "", text = "";
        try { type = Str(comp.Type); label = LabelOf(comp); text = Str(comp.Text); } catch { }

        if (!type.Equals("GuiButton", StringComparison.OrdinalIgnoreCase))
        {
            Diagnostic?.Invoke(this, $"el clic cayó en «{label}» ({type}), no en un botón — el viaje lo "
                + "disparó un Enter sobre ese control. No se graba un clic que nadie dio.");
            return;
        }

        string node = SafeNodeUrl();
        if (node.Length == 0) node = _preNodeUrl;

        StepObserved?.Invoke(this, new ObservedStep(
            ActionType: "click",
            Selector: SapSelector.ById(id),
            Label: label.Length > 0 ? label : (text.Trim().Length > 0 ? text.Trim() : "Botón"),
            ControlType: "button",
            Value: text,
            AllowedOptions: null,
            SelectedValue: null,
            SelectedLabel: null,
            SurfaceSection: null,
            AlternativeTargets: Array.Empty<string>())
        {
            Surface = node,
            Readiness = node == _preNodeUrl ? _preReadiness : CachedReadinessMeta(node),
            Fingerprint = _preFingerprint,
        });

        Diagnostic?.Invoke(this, $"observado: botón «{label}» ({id}) por hit-test del clic, desde '{node}'");
    }

    /// <summary>
    /// Anota dónde clicó el operador. No hace NADA más: nada de COM aquí dentro.
    ///
    /// Un hook de bajo nivel corre en la cola de mensajes de todo el escritorio; si tarda, Windows lo
    /// desengancha y el ratón se siente pegajoso en TODA la máquina. Guardar dos enteros es lo único
    /// que se puede permitir. La consulta cara —FindByPosition— se hace después, en StartRequest.
    /// </summary>
    private IntPtr MouseHookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && (int)wParam == WM_LBUTTONDOWN)
        {
            try
            {
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                _clickX = data.X;
                _clickY = data.Y;
                _clickAt = DateTime.UtcNow;
            }
            catch { }
        }
        return CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    /// <summary>
    /// Quién había bajo el último clic, según el hit-test del propio SAP. "" si no hay clic reciente o
    /// si SAP no reconoce nada ahí.
    ///
    /// La ventana de 6 s no es arbitraria: un round-trip que llega mucho después de un clic no lo
    /// disparó ese clic —lo disparó un Enter, un menú o un temporizador— y atribuírselo inventaría un
    /// paso. Ante la duda, no se graba: un paso de más en un flujo clínico es peor que uno de menos.
    /// </summary>
    private string ClickedComponentId()
    {
        // Cada rama dice QUÉ paso falló, no una conclusión. La versión anterior daba un solo texto
        // —«sin clic reciente que SAP reconozca»— para tres situaciones distintas, y con eso no se podía
        // saber si el problema era el hook, la antigüedad del clic o el hit-test. Costó una corrida.
        if (_clickAt == DateTime.MinValue)
        {
            Diagnostic?.Invoke(this, "no hay ningún clic anotado: ¿se puso el hook de ratón?");
            return "";
        }

        double age = (DateTime.UtcNow - _clickAt).TotalSeconds;
        if (age > 6)
        {
            Diagnostic?.Invoke(this, $"el último clic es de hace {age:F1}s (>6s): no se le atribuye este viaje");
            return "";
        }

        string? byHitTest = HitTest(_clickX, _clickY);
        if (!string.IsNullOrEmpty(byHitTest)) return byHitTest;

        // FindByPosition devuelve null en este SAP —ya estaba documentado para las filas de árbol, y
        // resulta que también para los botones. Hit-test propio con la MISMA geometría que el inspector
        // usa para dibujar sus recuadros, que sabemos exacta porque se ve encajar en pantalla.
        string byGeometry = ComponentAt(_clickX, _clickY);
        Diagnostic?.Invoke(this, byGeometry.Length > 0
            ? $"FindByPosition({_clickX},{_clickY}) dio null; por geometría → {byGeometry}"
            : $"clic en ({_clickX},{_clickY}) hace {age:F1}s: ni FindByPosition ni la geometría "
              + "encuentran un componente ahí (¿clic fuera de la ventana de SAP?)");
        return byGeometry;
    }

    /// <summary>
    /// Nuestro propio hit-test: el componente interactivo MÁS PEQUEÑO cuya caja en pantalla contiene el
    /// punto. El más pequeño y no el primero, porque los contenedores también contienen el punto y
    /// devolverían el panel entero en vez del botón.
    /// </summary>
    private string ComponentAt(int screenX, int screenY)
    {
        string best = "";
        long bestArea = long.MaxValue;

        foreach (SapVisualElement el in ReadVisibleElements())
        {
            // Sin caja conocida no se puede decidir (los nodos de árbol entran aquí), y sin caja se
            // acabaría eligiendo un elemento por descarte. Los nodos se accionan por clave, no por píxel.
            if (!el.BoundsKnown || el.IsNode || el.Width <= 0 || el.Height <= 0) continue;
            if (screenX < el.ScreenLeft || screenX >= el.ScreenLeft + el.Width) continue;
            if (screenY < el.ScreenTop || screenY >= el.ScreenTop + el.Height) continue;

            long area = (long)el.Width * el.Height;
            if (area >= bestArea) continue;
            bestArea = area;
            best = el.Id;
        }
        return best;
    }

    /// <summary>Los shells de la pantalla activa que tienen barra de botones propia (ALV/GridView).</summary>
    private static IEnumerable<dynamic> ToolbarShells(dynamic session)
    {
        var acc = new List<dynamic>();
        dynamic? root = null;
        try { root = session.ActiveWindow; } catch { }
        if (root == null) { try { root = session.FindById("wnd[0]", false); } catch { } }
        if (root == null) return acc;

        void Walk(dynamic node, int depth)
        {
            if (depth > 14 || acc.Count > 12) return;
            int count = 0;
            try { count = (int)node.ToolbarButtonCount; } catch { }
            if (count > 0) acc.Add(node);

            dynamic children; int n;
            try { children = node.Children; n = (int)children.Count; } catch { return; }
            for (int i = 0; i < n && i < 200; i++)
            {
                try { Walk(children.ElementAt(i), depth + 1); } catch { }
            }
        }
        try { Walk(root, 0); } catch { }
        return acc;
    }

    /// <summary>
    /// La CLAVE del botón que ocupa esa posición de la barra (<c>3</c> → <c>NV44</c>). "" si el valor no
    /// es un índice válido. Se acepta también que alguna versión de SAP devuelva ya la clave: en ese
    /// caso no parsea como número y se usa tal cual, siempre que exista en la barra.
    /// </summary>
    private static string ToolbarButtonIdAt(dynamic shell, string focused)
    {
        int count = 0;
        try { count = (int)shell.ToolbarButtonCount; } catch { return ""; }
        if (count <= 0) return "";

        if (int.TryParse(focused, out int index))
        {
            if (index < 0 || index >= count) return ""; // -1 = nada enfocado
            try { return Str(shell.GetToolbarButtonId(index)).Trim(); } catch { return ""; }
        }

        for (int i = 0; i < count && i < 60; i++)
        {
            try
            {
                if (string.Equals(Str(shell.GetToolbarButtonId(i)).Trim(), focused, StringComparison.OrdinalIgnoreCase))
                    return focused;
            }
            catch { }
        }
        return "";
    }

    /// <summary>El texto visible de un botón de toolbar, por su clave. "" si no se puede leer.</summary>
    private static string ToolbarButtonLabel(dynamic shell, string buttonId)
    {
        int count = 0;
        try { count = (int)shell.ToolbarButtonCount; } catch { return ""; }

        for (int i = 0; i < count && i < 60; i++)
        {
            string id;
            try { id = Str(shell.GetToolbarButtonId(i)).Trim(); } catch { continue; }
            if (!string.Equals(id, buttonId, StringComparison.OrdinalIgnoreCase)) continue;

            foreach (string getter in new[] { "GetToolbarButtonText", "GetToolbarButtonTooltip" })
            {
                try
                {
                    string v = Str(shell.GetType().InvokeMember(
                        getter, BindingFlags.InvokeMethod, null, shell, new object[] { i })).Trim();
                    if (v.Length > 0) return v;
                }
                catch { }
            }
            return "";
        }
        return "";
    }

    /// <summary>
    /// Refresca la instantánea previa (nodo + readiness + campo de comandos). SOLO se llama con SAP
    /// ocioso: es la garantía de que lo que guarda es la pantalla de origen y no una de tránsito.
    /// </summary>
    private void RefreshPreflight(dynamic session)
    {
        string url = SafeNodeUrl();
        if (url.Length > 0)
        {
            bool moved = url != _preNodeUrl;
            _preNodeUrl = url;
            _preReadiness = CachedReadinessMeta(url);
            // La huella SÍ se recalcula aunque la superficie no cambie: su razón de ser es notar que la
            // pantalla cambió de ESTADO sin cambiar de transacción. Cachearla por URL la volvería ciega
            // justo al caso para el que existe. Es un recorrido de la ventana por tick ocioso; si algún
            // día pesa, el sitio donde bajarla es este y no la cadencia del reloj.
            try { _preFingerprint = StructureFingerprint(); } catch { _preFingerprint = ""; }
            if (moved) Diagnostic?.Invoke(this, $"pantalla '{url}' · huella {_preFingerprint}");
        }

        // La sombra es un ESPEJO fiel del campo, vacío incluido. La tentación es guardar solo lo no
        // vacío (SAP limpia el okcd al ejecutar, y perder la sombra sería perder el paso), pero eso deja
        // pegado un código que el operador escribió y luego BORRÓ: el siguiente round-trip —un clic en
        // una fila, un botón— lo consumiría y grabaría una entrada a transacción que nunca ocurrió.
        // El caso que la tentación protege no existe: StartRequest se dispara con el Enter, y durante el
        // round-trip este tick no corre (guarda de Busy), así que no hay tick que pueda pisar la sombra
        // entre que SAP la limpia y que StartRequest la consume.
        if (TryReadOkCode(session, out string code)) _pendingOkCode = code;
    }

    /// <summary>Lee el campo de comandos. false si no resuelve (no todas las pantallas lo tienen: los
    /// modales de SAP no llevan barra de comandos).</summary>
    private static bool TryReadOkCode(dynamic session, out string code)
    {
        code = "";
        try
        {
            dynamic? field = session.FindById(OkCodeId, false);
            if (field == null) return false;
            code = Str(field.Text).Trim();
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Graba la ENTRADA a una transacción: el operador tecleó un código en la barra de comandos y pulsó
    /// Enter. Se emiten DOS pasos, que es literalmente lo que hizo y lo que el player ya sabe
    /// reproducir por separado (<see cref="Apply"/> fija el texto, <see cref="SendKey"/> manda VKey 0).
    ///
    /// Ambos se sellan con la pantalla de ORIGEN. Ese es el punto entero: un workflow que empieza en
    /// SAP Easy Access ahora tiene un paso 1 que ocurre EN Easy Access, así que la compuerta de
    /// ubicación del player lo deja pasar en vez de esperar para siempre una transacción a la que nadie
    /// ha navegado.
    /// </summary>
    /// <returns>true si el viaje lo disparó una transacción tecleada (y por tanto NO fue un botón).</returns>
    private bool PublishTransactionEntry(dynamic session, bool idle)
    {
        // Con SAP ocioso se lee en vivo (más fresco que la sombra y todavía pre-navegación); si ya
        // arrancó el viaje, la sombra del último tick es lo único seguro que hay.
        // `live` se declara aparte: session es dynamic, así que la llamada se resuelve en runtime y el
        // compilador no puede dar por asignado un `out var` dentro de una condición (CS0165).
        string live = "";
        string code = _pendingOkCode;
        if (idle && TryReadOkCode(session, out live) && live.Length > 0) code = live;

        _pendingOkCode = "";
        if (code.Length == 0) return false; // el viaje vino de un botón, una fila o un Enter en un campo

        string node = idle ? SafeNodeUrl() : "";
        if (node.Length == 0) node = _preNodeUrl;
        string readiness = node == _preNodeUrl ? _preReadiness : CachedReadinessMeta(node);

        StepObserved?.Invoke(this, new ObservedStep(
            ActionType: "input",
            Selector: SapSelector.ById(OkCodeId),
            Label: $"Código de transacción «{code}»",
            ControlType: "text",
            Value: code,
            AllowedOptions: null,
            SelectedValue: null,
            SelectedLabel: null,
            SurfaceSection: null,
            AlternativeTargets: Array.Empty<string>())
        {
            Surface = node,
            Fingerprint = _preFingerprint,
            Readiness = readiness,
        });

        // El Enter va aparte y con el mismo vocabulario que emite UiaSurface.PublishKey (`key:enter`),
        // para que el player no necesite saber de qué superficie salió el paso.
        StepObserved?.Invoke(this, new ObservedStep(
            ActionType: "key",
            Selector: "key:enter",
            Label: "Enter",
            ControlType: "key",
            Value: "Enter",
            AllowedOptions: null,
            SelectedValue: null,
            SelectedLabel: null,
            SurfaceSection: null,
            AlternativeTargets: Array.Empty<string>())
        {
            Fingerprint = _preFingerprint,
            Surface = node,
            Readiness = readiness,
        });

        Diagnostic?.Invoke(this, $"observado: entrada a transacción «{code}» desde '{node}'");
        return true;
    }

    /// <summary>Readiness cacheado por superficie: un Walk de la ventana entera por CAMBIO de pantalla,
    /// no por tick. "" si no se pudo contar (0 apagaría el respaldo por porcentaje del player).</summary>
    private string CachedReadinessMeta(string url)
    {
        if (url.Length > 0 && url == _readyCacheUrl && _readyCacheCount > 0) return _readyCacheCount.ToString();
        try
        {
            int c = ReadinessCount();
            if (c <= 0) return "";
            _readyCacheUrl = url;
            _readyCacheCount = c;
            return c.ToString();
        }
        catch { return ""; }
    }

    /// <summary>
    /// Publica un ObservedStep por cada campo cuyo valor cambió desde la última lectura. Es el mismo
    /// mecanismo tanto si lo dispara un evento COM real como el sondeo: en ambos casos la fuente de
    /// verdad es releer el área de usuario completa, porque ni Change ni el sondeo traen "qué cambió".
    /// </summary>
    private void PublishChangedFields()
    {
        var current = SafeReadFields();

        // ── LÍNEA BASE NUEVA TRAS NAVEGAR, SIN PUBLICAR NADA ────────────────────────────
        // El snapshot pertenece a una PANTALLA concreta. Si la pantalla ya no es esa, comparar contra él
        // no dice «qué cambió»: dice «en qué se diferencian dos pantallas distintas», y eso son TODOS
        // los campos de la nueva. Así nacían los ~20 `input` con valor vacío de cada grabación (los 14
        // avisos del ensayo en seco): no son acciones del operador, son la pantalla nueva presentándose.
        //
        // Y no se pierde nada real: después de navegar, lo que el operador tecleó en la pantalla vieja
        // YA NO SE PUEDE LEER. Esos pasos se publican en StartRequest, antes del viaje, que es cuando
        // los valores siguen ahí. Lo que aquí se suprimía era ruido con forma de paso.
        string nowSurface = SafeNodeUrl();
        if (_snapshotSurface.Length > 0 && nowSurface.Length > 0 && nowSurface != _snapshotSurface)
        {
            _lastSnapshot = current.ToDictionary(f => f.Selector, f => f.CurrentValue);
            _snapshotSurface = nowSurface;
            Diagnostic?.Invoke(this,
                $"pantalla nueva ({nowSurface}): línea base rehecha, {current.Count} campo(s) NO publicados "
                + "(son la pantalla presentándose, no lo que hizo el operador)");
            PublishTreeSelections();
            return;
        }
        if (_snapshotSurface.Length == 0) _snapshotSurface = nowSurface;

        string node = "", readiness = "";
        foreach (DetectedField field in current)
        {
            bool known = _lastSnapshot.TryGetValue(field.Selector, out string? prev);
            if (known && prev == field.CurrentValue) continue;

            // Campo que aparece por primera vez Y viene VACÍO: eso no es una edición, es un campo que
            // hasta ahora no habíamos visto. Vaciar un campo A MANO sí se graba, porque entonces el
            // campo estaba en el snapshot con valor: la condición exige que sea desconocido.
            if (!known && string.IsNullOrEmpty(field.CurrentValue)) continue;

            // El NODO del paso — dónde estaba parado el usuario al hacerlo. Sale de la SOMBRA, no de
            // un Identity() de ahora: Change llega DESPUÉS del round-trip, así que preguntar aquí
            // devolvería la pantalla de destino y sellaría el paso con una pantalla en la que nunca
            // ocurrió. La sombra es del último tick ocioso, es decir, de antes del viaje.
            if (node.Length == 0) { node = PreNodeUrl(out readiness); }

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
                Fingerprint = _preFingerprint,
            });
        }
        _lastSnapshot = current.ToDictionary(f => f.Selector, f => f.CurrentValue);
        if (nowSurface.Length > 0) _snapshotSurface = nowSurface;

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
        string node = "", readiness = "";

        foreach (var sel in SafeReadTreeSelections())
        {
            // La línea base se actualiza POR ÁRBOL LEÍDO, no reemplazando el diccionario entero con lo
            // visto en este tick. Reemplazarlo tenía este efecto: basta un tick en que el árbol no
            // resuelva —está navegando, o los getters de selección fallan, cosa que pasa y está en el
            // log— para que su entrada desaparezca y la MISMA fila se vuelva a publicar como nueva en el
            // siguiente tick. Así se grabaron los pasos 3 y 4 de wf_1785109929654: idénticos, misma
            // clave vw00030, un doble clic del operador convertido en dos pasos.
            bool known = _lastTreeSelection.TryGetValue(sel.TreeId, out string? prev);
            _lastTreeSelection[sel.TreeId] = sel.Key;
            if (known && prev == sel.Key) continue;

            // Mismo nodo por lote que en PublishChangedFields, y por el mismo motivo: de la sombra. El
            // tick refresca ANTES de llamar aquí y solo con SAP ocioso, así que es la pantalla donde el
            // usuario clicó. Leerlo en vivo era una apuesta al reloj de 400 ms: si la navegación ganaba
            // la carrera, el doble clic en un favorito de Easy Access quedaba sellado con la
            // transacción de DESTINO — un paso que dice «clica el favorito NWP1, estando en NWP1».
            if (node.Length == 0) { node = PreNodeUrl(out readiness); }

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
                Fingerprint = _preFingerprint,
            });
        }
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

    /// <summary>
    /// El nodo de la sombra: dónde estaba el usuario en el último instante ocioso. Es lo que se sella
    /// en un paso, NUNCA un Identity() del momento de publicar (ver <see cref="RefreshPreflight"/>).
    ///
    /// Respaldo si la sombra está vacía (aún no corrió ningún tick, o la identidad no se pudo leer):
    /// se lee en vivo. Es el comportamiento viejo y puede quedar sellado con la pantalla de destino,
    /// pero un paso con nodo aproximado sigue siendo mejor que uno sin nodo, que apaga la compuerta.
    /// </summary>
    private string PreNodeUrl(out string readiness)
    {
        if (_preNodeUrl.Length > 0)
        {
            readiness = _preReadiness;
            return _preNodeUrl;
        }

        string url = SafeNodeUrl();
        readiness = url.Length > 0 ? CachedReadinessMeta(url) : "";
        return url;
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

            // Tipo explícito en el descarte: tree es dynamic, así que la llamada se resuelve en runtime y
            // un `out _` sin tipo no se puede deducir (CS8183).
            string? key = TrySelectedNodeKey(tree, out string _);
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
                    // El hook se suelta EN SU PROPIO HILO: un WH_MOUSE_LL desenganchado desde otro hilo
                    // puede quedar colgado, y un hook huérfano ralentiza el ratón de toda la máquina.
                    if (_mouseHook != IntPtr.Zero)
                    {
                        try { UnhookWindowsHookEx(_mouseHook); } catch { }
                        _mouseHook = IntPtr.Zero;
                        _mouseProc = null;
                    }
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
