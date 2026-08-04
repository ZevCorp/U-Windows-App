using U.Graph;
using U.Graph.Surfaces;
using U.WindowsClient.Diagnostics;
using U.WindowsClient.Navigation;
using U.WindowsClient.Uia;

namespace U.WindowsClient.Mcp;

/// <summary>
/// El mapa del computador, expuesto al cerebro. Cuatro herramientas: tres que MIRAN y una que
/// ACTÚA, y esa separación es el diseño, no una casualidad de implementación.
///
/// Lo que hace útil a este mapa es que se aprendió solo, viendo al usuario trabajar: el asistente
/// puede llegar a sitios que nadie le enseñó como workflow. Lo que lo hace peligroso es lo mismo —
/// nadie revisó esas rutas. De ahí las dos reglas que gobiernan todo lo de abajo:
///
///   · SOLO SE OFRECEN RUTAS COMPLETAS. Una arista sin acción no se puede recorrer, así que no
///     entra en ninguna ruta. Mejor «no sé llegar» que dejar al asistente a mitad de camino en la
///     máquina de alguien.
///   · SE VERIFICA CADA TRAMO. Tras ejecutar la acción se comprueba que la pantalla es la esperada,
///     y si no llegó se PARA y se dice dónde quedó. La medición de este mapa dio ~78% de acciones
///     correctas: sin verificación, una de cada cinco navegaciones acabaría en un sitio distinto
///     del que el modelo cree, actuando sobre lo que no reconoce.
/// </summary>
public sealed class SurfaceMapTools
{
    private readonly SurfaceMap _map;
    private readonly Func<SurfaceLocator.SurfaceLocation?> _where;
    // SoloEnFoco: esta capa verifica la ubicación antes de actuar, así que si un selector no está
    // en la ventana de delante es que no está. Sin esto, un nombre inexistente disparaba un barrido
    // por todas las ventanas del escritorio que llegó a tardar 181 s en fallar (2026-08-03).
    private readonly UiaSurface _uia = new() { Log = s => LogBus.Log("mapa-mcp", s), SoloEnFoco = true };
    private readonly UiaReader _lector = new();

    /// <summary>La app con la que se estaba trabajando. Se usa para volver a ella si algo roba el foco.</summary>
    private string _ultimaApp = "";
    private List<string> _seleccionPrevia = new();

    /// <summary>Lo último que se intentó. Sin esto, quien deba decidir ante un diálogo no sabe
    /// para qué apareció, y «continuar o no» depende justamente de eso.</summary>
    private string _ultimaAccion = "";

    /// <summary>
    /// A dónde llevaría «Atrás» AHORA MISMO. Estado efímero de la sesión, nunca una arista.
    ///
    /// El botón Atrás no describe una propiedad de la pantalla —depende de cómo se llegó— así que
    /// guardarlo en el mapa lo hace mentir en cuanto se llega por otro camino. Pero SÍ se sabe a
    /// dónde lleva en esta sesión: al sitio del que se vino. Recordarlo permite usarlo cuando
    /// conviene y descartarlo cuando el destino es otro, en vez de tener que elegir entre
    /// aprenderlo mal o no tenerlo (idea del usuario, 2026-08-02).
    /// </summary>
    private readonly Stack<string> _historial = new();

    /// <summary>Anota que se pasó de <paramref name="de"/> a <paramref name="a"/>.</summary>
    private void Anotar(string de, string a)
    {
        if (de.Length == 0 || a.Length == 0 || string.Equals(de, a, StringComparison.OrdinalIgnoreCase)) return;
        // Si volvimos justo al sitio anterior, se DESAPILA en vez de apilar: si no, el historial
        // crecería con idas y vueltas y «Atrás» acabaría prometiendo un bucle.
        if (_historial.Count > 0 && string.Equals(_historial.Peek(), a, StringComparison.OrdinalIgnoreCase))
            _historial.Pop();
        else
            _historial.Push(de);
    }

    /// <summary>A dónde lleva «Atrás» en esta sesión, o "" si no se sabe.</summary>
    private string DestinoDeAtras() => _historial.Count > 0 ? _historial.Peek() : "";

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int max);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void keybd_event(byte key, byte scan, uint flags, IntPtr extra);
    private delegate bool EnumProc(IntPtr h, IntPtr l);

    /// <summary>Superficies del propio Windows que se ponen delante solas y tapan la app.</summary>
    private static bool EsPanelDelShell(string proc) =>
        proc.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase)
        || proc.Equals("SearchHost", StringComparison.OrdinalIgnoreCase)
        || proc.Equals("StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase)
        || proc.Equals("TextInputHost", StringComparison.OrdinalIgnoreCase);

    /// <summary>«uia://explorer.exe/loquesea» → «explorer».</summary>
    private static string AppDe(string id)
    {
        var m = System.Text.RegularExpressions.Regex.Match(id ?? "", @"^uia://([^/]+?)\.exe/");
        return m.Success ? m.Groups[1].Value : "";
    }

    /// <summary>
    /// Devuelve el foco a la app con la que se está trabajando si algo se lo ha llevado.
    ///
    /// El centro de notificaciones, el buscador o cualquier aviso de Windows se ponen delante solos
    /// y la secuencia se rompe: el asistente pedía «Nuevo» y se le contestaba con las opciones del
    /// centro de notificaciones (2026-08-02). El recorrido automático ya se recolocaba; esta capa
    /// no, y es la que usa el asistente para hacer tareas de verdad. No se lanza nada: si la app no
    /// está viva, se dice y punto — abrir aplicaciones por iniciativa propia no es recuperarse.
    /// </summary>
    private bool AsegurarFoco(string app)
    {
        if (app.Length == 0) return true;
        // Se sale pronto solo si LAS DOS fuentes coinciden. Bastaba con el primer plano y era la
        // trampa: tras cerrarse un panel del shell el foco ya era correcto pero el localizador
        // —que sondea cada 800 ms— seguía diciendo «SearchHost», así que la ruta se calculaba desde
        // un sitio donde ya no estábamos y se respondía «no conozco ruta» (2026-08-02).
        if (Coinciden(app)) return true;

        // Los paneles del shell —centro de notificaciones, buscador, menú inicio— NO se apartan con
        // SetForegroundWindow: Windows lo bloquea mientras uno de ellos tiene el foco, así que el
        // intento fallaba en silencio y la tarea moría ahí (2026-08-02). Se DESCARTAN con Escape,
        // que es lo mismo que haría una persona, y solo después se recupera la app.
        for (int intento = 0; intento < 2 && EsPanelDelShell(AppEnFrente()); intento++)
        {
            LogBus.Log("mapa-mcp", $"«{AppEnFrente()}» tiene el foco: se descarta con Escape");
            keybd_event(0x1B, 0, 0, IntPtr.Zero);
            keybd_event(0x1B, 0, 2, IntPtr.Zero);
            System.Threading.Thread.Sleep(500);
        }
        if (EsperarCoincidencia(app)) return true;

        IntPtr elegida = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            var sb = new System.Text.StringBuilder(300);
            if (GetWindowText(h, sb, 300) == 0) return true;   // sin título: no es una pantalla
            GetWindowThreadProcessId(h, out uint pid);
            try
            {
                using var p = System.Diagnostics.Process.GetProcessById((int)pid);
                if (!p.ProcessName.Equals(app, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { return true; }
            elegida = h;
            return false;
        }, IntPtr.Zero);

        if (elegida == IntPtr.Zero) return false;
        if (IsIconic(elegida)) ShowWindow(elegida, 9 /* SW_RESTORE */);
        SetForegroundWindow(elegida);

        // Se espera a que lo confirmen LAS DOS fuentes: el sistema (quién está delante) y el
        // localizador, que sondea cada 800 ms. Conformarse con la primera dejaba una ventana en la
        // que el foco ya era correcto pero la superficie seguía siendo la de antes, y la ruta se
        // calculaba desde el sitio equivocado: «no conozco ruta desde SearchHost» estando ya en el
        // explorador (2026-08-02). Recuperar el foco no es haberlo notado.
        if (!EsperarCoincidencia(app)) return false;
        LogBus.Log("mapa-mcp", $"el foco se había ido; devuelto a «{app}»");
        return true;
    }

    /// <summary>
    /// Qué hay seleccionado ahora mismo en la pantalla.
    ///
    /// Existe porque una acción como «Cortar» opera sobre LO SELECCIONADO, y el asistente no tenía
    /// forma de saber qué era. En una tarea de organizar archivos, un paso previo falló, la
    /// selección se quedó en una carpeta recién creada, y el siguiente «Cortar» la cortó a ella:
    /// se intentó pegar la carpeta dentro de sí misma (2026-08-02). Nadie mintió —cada paso
    /// reportó su fallo— pero el que actuaba no sabía sobre qué actuaba. Decirlo convierte un
    /// encadenamiento a ciegas en algo comprobable antes de tocar nada.
    /// </summary>
    /// <summary>
    /// Al bajar a una carpeta, aprende también la SUBIDA a su padre.
    ///
    /// «Subir» (aid=upButton) es estructural: desde una carpeta siempre lleva a la que la contiene,
    /// se haya llegado como se haya llegado. Eso lo hace una arista legítima, al contrario que
    /// «Atrás», que depende del historial y por eso NO se aprende. Sin esto el grafo bajaba y no
    /// subía: al pedir volver de «facturas» a su padre la respuesta era «no conozco una ruta
    /// COMPLETA», y una tarea que creaba carpetas hermanas acababa creándolas anidadas — Windows
    /// mismo lo paró con «la carpeta de destino es una subcarpeta de la de origen» (2026-08-02).
    ///
    /// Solo cuando la bajada fue por un elemento de LISTA —una carpeta de contenido—: pulsar algo
    /// del panel lateral no es descender, y su padre no es de donde veníamos.
    /// </summary>
    private void AprenderSubida(string padre, string hijo, string tipoDePuerta)
    {
        if (!tipoDePuerta.Equals("listitem", StringComparison.OrdinalIgnoreCase)) return;
        if (padre.Length == 0 || hijo.Length == 0
            || string.Equals(padre, hijo, StringComparison.OrdinalIgnoreCase)) return;

        // ¿EXISTE el botón de subir en ESTA app? «Subir un nivel» es del explorador de archivos;
        // Configuración no lo tiene. Aprender la arista sin comprobarlo llenó su grafo de caminos
        // imposibles: las rutas se planificaban por un botón inexistente y 6 de 7 navegaciones
        // fallaban sobre un mapa que por lo demás estaba bien (2026-08-03). Una regla ganada en una
        // app no se exporta a las demás sin verificarla — es justo lo que la segunda app existe
        // para enseñarnos.
        if (!ExisteBotonSubir()) return;

        _map.LearnTraversal(hijo, padre, "uia:aid=upButton;ct=Button",
            Array.Empty<string>(), "Subir un nivel", "Button", "click");
        LogBus.Log("mapa-mcp", $"aprendida la subida: '{hijo}' → '{padre}'");
    }

    /// <summary>
    /// Aprende una app ENTERA: la trae al frente ella sola (o la abre) y la recorre.
    ///
    /// Antes, mapear dependía de quién tuviera el foco al pulsar el botón, y eso es frágil hasta el
    /// absurdo: cualquier ventana que se pusiera delante en ese instante —incluida la de quien
    /// lanzaba la prueba— hacía que se mapeara la app equivocada (2026-08-03). Decir QUÉ app se
    /// quiere aprender y que el sistema se encargue del resto es la forma correcta: la intención
    /// la pone quien pide, no el azar del escritorio.
    /// </summary>
    private string LearnApp(string app)
    {
        if (app.Length == 0)
            return "falta `app`: el proceso a aprender (por ejemplo «explorer» o «ApplicationFrameHost»)";

        _ultimaApp = app;
        if (!AsegurarFoco(app))
        {
            // No estaba viva: se abre. Lanzar una app es razonable cuando ALGUIEN LA PIDIÓ por su
            // nombre; lo que no vale es abrir cosas por iniciativa propia al recuperarse de un fallo.
            LogBus.Log("mapa-mcp", $"«{app}» no estaba delante; se intenta abrir");
            AppAligner.FocusOrLaunch(app);
            if (!AsegurarFoco(app))
                return $"no pude poner «{app}» delante (ahora hay «{AppEnFrente()}»); no mapeo a ciegas";
        }

        var loc = _where();
        if (loc == null || !AppDe(loc.Id).Equals(app, StringComparison.OrdinalIgnoreCase))
            return $"«{app}» está delante pero la superficie no lo confirma ({loc?.Id}); no mapeo.";

        LogBus.Log("mapa-mcp", $"→ aprender «{app}» desde '{loc.Id}'");
        var crawler = new GraphCrawler(_map, _where);
        try
        {
            string r = crawler.CrawlAsync(120, 4, System.Threading.CancellationToken.None)
                              .GetAwaiter().GetResult();
            LogBus.Log("mapa-mcp", $"← aprender «{app}»: {r}");
            return $"aprendida «{app}»: {r}";
        }
        catch (Exception e) { return $"el recorrido de «{app}» falló: {e.Message}"; }
    }

    /// <summary>¿La app de delante tiene el botón «Subir un nivel»? Solo el explorador lo tiene.</summary>
    private static bool ExisteBotonSubir()
    {
        try
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;
            var raiz = System.Windows.Automation.AutomationElement.FromHandle(fg);
            return raiz?.FindFirst(System.Windows.Automation.TreeScope.Descendants,
                new System.Windows.Automation.PropertyCondition(
                    System.Windows.Automation.AutomationElement.AutomationIdProperty, "upButton")) != null;
        }
        catch { return false; }
    }

    private List<string> SeleccionActual()
    {
        var sel = new List<string>();
        try
        {
            // Consulta DIRIGIDA, no una lectura del árbol entero. Saber qué hay marcado solo
            // necesita los elementos de lista seleccionados, y UIA sabe pedirlos: una condición
            // compuesta (ListItem AND IsSelected) los resuelve de una vez. Recorrer todo el lector
            // —ventanas hijas, menús, cientos de elementos, un viaje entre procesos por cada uno—
            // costaba ~3 s por llamada, y esto se consulta en cada acción (2026-08-02).
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return sel;
            var raiz = System.Windows.Automation.AutomationElement.FromHandle(fg);
            if (raiz == null) return sel;

            var cond = new System.Windows.Automation.AndCondition(
                new System.Windows.Automation.PropertyCondition(
                    System.Windows.Automation.AutomationElement.ControlTypeProperty,
                    System.Windows.Automation.ControlType.ListItem),
                new System.Windows.Automation.PropertyCondition(
                    System.Windows.Automation.SelectionItemPattern.IsSelectedProperty, true));

            foreach (System.Windows.Automation.AutomationElement el
                     in raiz.FindAll(System.Windows.Automation.TreeScope.Descendants, cond))
            {
                try
                {
                    string n = el.Current.Name?.Trim() ?? "";
                    if (n.Length > 0 && !sel.Contains(n)) sel.Add(n);
                }
                catch { }
            }
        }
        catch { }
        return sel;
    }

    /// <summary>
    /// ¿Esta acción puede DESTAPAR elementos nuevos (un menú, un desplegable, un diálogo)?
    /// Solo entonces vale la pena releer la pantalla: lo demás no cambia las puertas y releer
    /// cuesta un recorrido completo del árbol de UI.
    /// </summary>
    /// <summary>
    /// Registra SOLO los elementos de menú visibles. Consulta dirigida, no una relectura completa.
    ///
    /// Tras pulsar «Nuevo» lo único que interesa es lo que acaba de aparecer —«Carpeta», «Acceso
    /// directo»…—, no las 300 puertas que la pantalla ya tenía. Releerlo todo costaba ~7 s por
    /// acción de menú; pedirle a UIA los MenuItem de una vez lo resuelve en una fracción.
    /// </summary>
    /// <summary>
    /// Espera a que HAYA menú abierto, y vuelve en cuanto lo hay. Devuelve false si no apareció.
    ///
    /// Es la diferencia entre esperar el efecto y esperar el reloj: un menú se abre en unos
    /// 200 ms, así que un plazo fijo de 800 ms desperdiciaba medio segundo cada vez y aun así se
    /// quedaba corto en un equipo cargado. Preguntar por lo que se espera sirve para las dos cosas.
    /// </summary>
    private bool EsperarMenu(int msMax, int habiaAntes)
    {
        for (int i = 0; i < msMax / 70; i++)
        {
            if (CuantosMenus() > habiaAntes) return true;
            System.Threading.Thread.Sleep(70);
        }
        return false;
    }

    /// <summary>Cuántos elementos de menú hay ahora en la ventana de delante.</summary>
    private static int CuantosMenus()
    {
        try
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return 0;
            var raiz = System.Windows.Automation.AutomationElement.FromHandle(fg);
            if (raiz == null) return 0;
            return raiz.FindAll(System.Windows.Automation.TreeScope.Descendants,
                new System.Windows.Automation.PropertyCondition(
                    System.Windows.Automation.AutomationElement.ControlTypeProperty,
                    System.Windows.Automation.ControlType.MenuItem)).Count;
        }
        catch { return 0; }
    }

    private void ObservarMenus(string nodo)
    {
        try
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return;
            var raiz = System.Windows.Automation.AutomationElement.FromHandle(fg);
            if (raiz == null) return;

            var puertas = new List<(string, string, string, string[])>();
            foreach (System.Windows.Automation.AutomationElement el in raiz.FindAll(
                System.Windows.Automation.TreeScope.Descendants,
                new System.Windows.Automation.PropertyCondition(
                    System.Windows.Automation.AutomationElement.ControlTypeProperty,
                    System.Windows.Automation.ControlType.MenuItem)))
            {
                try
                {
                    var info = el.Current;
                    if (info.IsOffscreen) continue;
                    string n = info.Name?.Trim() ?? "";
                    if (n.Length == 0) continue;
                    puertas.Add((n, "MenuItem", $"uia:name={n};ct=MenuItem", Array.Empty<string>()));
                }
                catch { }
            }
            if (puertas.Count == 0) return;
            _map.ObserveExits(nodo, puertas);
            LogBus.Log("mapa-mcp", $"menú abierto: {puertas.Count} opción(es) anotadas");
        }
        catch { }
    }

    private static bool PuedeAbrirMenu(string etiqueta) =>
        etiqueta.Contains("Nuevo", StringComparison.OrdinalIgnoreCase)
        || etiqueta.Contains("Ordenar", StringComparison.OrdinalIgnoreCase)
        || etiqueta.Contains("Ver", StringComparison.OrdinalIgnoreCase)
        || etiqueta.Contains("opciones", StringComparison.OrdinalIgnoreCase)
        || etiqueta.Contains("Más", StringComparison.OrdinalIgnoreCase)
        || etiqueta.Contains("Compartir", StringComparison.OrdinalIgnoreCase);

    /// <summary>Acciones que operan sobre lo seleccionado: antes de ejecutarlas hay que saber qué es.</summary>
    private static bool OperaSobreLaSeleccion(string etiqueta) =>
        etiqueta.Contains("Cortar", StringComparison.OrdinalIgnoreCase)
        || etiqueta.Contains("Copiar", StringComparison.OrdinalIgnoreCase)
        || etiqueta.Contains("Eliminar", StringComparison.OrdinalIgnoreCase)
        || etiqueta.Contains("Cambiar nombre", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// ¿Estoy donde quien me pide la acción cree que estoy? Devuelve "" si sí (o si no lo dijo),
    /// y el motivo del desacuerdo si no.
    ///
    /// Es el ancla de la ejecución. Sin ella, un paso que falla deja el recorrido en otra pantalla
    /// y los siguientes se ejecutan perfectamente… en el sitio equivocado: así se pegaron archivos
    /// dentro de su propia carpeta de origen y se crearon carpetas anidadas (2026-08-02). Se
    /// recupera el foco primero, porque «no estoy donde creía» y «algo me tapó» son cosas distintas
    /// y solo la segunda tiene arreglo automático.
    /// </summary>
    private string ComprobarUbicacion(string esperada)
    {
        if (esperada.Length == 0) return "";   // no lo declaró: se actúa como antes

        string app = AppDe(esperada);
        if (app.Length > 0) { _ultimaApp = app; AsegurarFoco(app); }

        // Se ESPERA a que la superficie se asiente antes de negarse. Justo después de una acción la
        // pantalla pasa por estados intermedios —al crear una carpeta el foco va un instante a la
        // ventana emergente del menú— y rechazar en el primer desacuerdo bloqueaba el paso
        // siguiente en 11 ms, con la carpeta quedándose como «Nueva carpeta» (2026-08-03). La
        // garantía no cambia: si al cabo de un segundo seguimos en otro sitio, no se actúa.
        string aqui = "";
        for (int i = 0; i < 25; i++)
        {
            aqui = _where()?.Id ?? "";
            if (string.Equals(aqui, esperada, StringComparison.OrdinalIgnoreCase)) return "";
            // UN MENÚ ABIERTO NO ES OTRO SITIO: es una capa sobre el mismo. Mientras está
            // desplegado, la superficie en foco es su ventana emergente, y tomarla por una
            // ubicación distinta hacía que el ancla rechazara elegir la opción del menú que
            // acabábamos de abrir (2026-08-03). Se acepta si pertenece a la misma app.
            if (EsCapaSobreLaPantalla(aqui, esperada)) return "";
            System.Threading.Thread.Sleep(45);   // el caso normal acierta a la primera; el resto, pronto
        }

        LogBus.Log("mapa-mcp", $"NO SE ACTÚA: se esperaba estar en '{esperada}' y estamos en '{aqui}'");

        // Si lo que bloquea es un diálogo, decir CUÁL. «No estás donde creías» obliga a investigar;
        // «hay este diálogo delante, con estas opciones» se puede resolver en el acto. Un rechazo
        // honesto que además explica la causa es la diferencia entre pararse y poder continuar.
        string interrupcion = DescribirInterrupcion();
        if (interrupcion.Length > 0)
            return $"NO actúo: creías estar en «{esperada}» y lo que hay delante es otra cosa.\n{interrupcion}";

        return $"NO actúo: creías estar en «{esperada}» pero estamos en «{aqui}». "
             + "Algo salió distinto en un paso anterior; comprueba dónde estás antes de seguir.";
    }

    /// <summary>
    /// ¿Lo que hay delante es una CAPA sobre la pantalla esperada —un menú, un desplegable— y no
    /// otro sitio? Se exige que sea de la MISMA app: una ventana emergente de otro programa sí es
    /// irse a otra parte, y ahí el ancla debe seguir negándose.
    /// </summary>
    private static bool EsCapaSobreLaPantalla(string aqui, string esperada)
    {
        if (aqui.Length == 0) return false;
        if (!AppDe(aqui).Equals(AppDe(esperada), StringComparison.OrdinalIgnoreCase)) return false;
        return aqui.Contains("ventanas-emergentes", StringComparison.OrdinalIgnoreCase)
            || aqui.Contains("popup", StringComparison.OrdinalIgnoreCase)
            || aqui.EndsWith("/ventana", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>El sistema y el localizador dicen los dos que estamos en esta app.</summary>
    private bool Coinciden(string app) =>
        AppEnFrente().Equals(app, StringComparison.OrdinalIgnoreCase)
        && AppDe(_where()?.Id ?? "").Equals(app, StringComparison.OrdinalIgnoreCase);

    private bool EsperarCoincidencia(string app)
    {
        for (int i = 0; i < 20; i++)
        {
            if (Coinciden(app)) return true;
            System.Threading.Thread.Sleep(150);
        }
        return false;
    }

    private static string AppEnFrente()
    {
        try
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch { return ""; }
    }

    public SurfaceMapTools(SurfaceMap map, Func<SurfaceLocator.SurfaceLocation?> where)
    {
        _map = map;
        _where = where;
    }

    public static bool IsMapTool(string tool) => tool is
        "map_where_am_i" or "map_places" or "map_routes_from" or "map_go_to" or "map_take"
        or "map_type" or "map_unblock" or "map_run" or "map_learn_app";

    public string Call(string tool, IReadOnlyDictionary<string, string> args)
    {
        string A(string k) => args.TryGetValue(k, out var v) ? v.Trim() : "";

        // Se registra CADA llamada y su respuesta. Sin esto, «el mapa no aportó nada» y «el modelo
        // ni lo intentó» se ven exactamente igual en el log — y esa ambigüedad me llevó a un
        // diagnóstico equivocado el 2026-07-31, buscando en el mapa un fallo que estaba en el
        // lanzador de apps.
        string args_ = string.Join(" ", args.Select(kv => $"{kv.Key}={kv.Value}"));
        LogBus.Log("mapa-mcp", $"→ {tool} {args_}".TrimEnd());

        string r = tool switch
        {
            "map_where_am_i" => WhereAmI(),
            "map_places" => Places(A("app")),
            "map_routes_from" => Routes(A("surface")),
            "map_go_to" => GoTo(A("surface")),
            "map_take" => Take(A("exit"), A("action"), A("at")),
            "map_type" => Type(A("text"), A("target"), A("at")),
            "map_unblock" => Unblock(A("at"), A("choose")),
            "map_learn_app" => LearnApp(A("app")),
            "map_run" => Run(A("steps")),
            _ => $"herramienta de mapa no soportada: {tool}",
        };

        LogBus.Log("mapa-mcp", "← " + (r.Length > 200 ? r[..200] + "…" : r).Replace("\n", " | "));
        return r;
    }

    private string WhereAmI()
    {
        var loc = _where();
        if (loc == null) return "no se pudo determinar la superficie actual";

        // UN DIÁLOGO NO ES UN LUGAR: es una interrupción. Se comprueba ANTES de describir salidas
        // porque, si lo hay, todo lo demás es ruido — no hay «rutas desde aquí», hay una pregunta
        // que responder para poder seguir.
        string interrupcion = DescribirInterrupcion();
        if (interrupcion.Length > 0) return interrupcion;

        ObservarAqui(loc.Id);
        var salidas = _map.ExitsFrom(loc.Id);
        int recorribles = salidas.Count(h => h.Info.Selector.Length > 0);
        var sel = SeleccionActual();
        return $"Estás en «{loc.Id}». Desde aquí el mapa conoce {salidas.Count} salida(s), "
             + $"{recorribles} de ellas recorribles."
             + (sel.Count > 0 ? $" Seleccionado ahora mismo: {string.Join(", ", sel.Select(s => $"«{s}»"))}." : "")
             + (DestinoDeAtras().Length > 0 ? $" «Atrás» llevaría a «{DestinoDeAtras()}»." : "");
    }

    /// <summary>
    /// EJECUCIÓN DE BAJA FRECUENCIA: un plan entero en UNA llamada.
    ///
    /// Es la otra mitad de la arquitectura de dos frecuencias. Quien decide —el modelo— emite el
    /// plan una vez; el ejecutor lo recorre verificando cada paso, sin volver a preguntar. La
    /// diferencia con ir paso a paso no es el trabajo, que es el mismo: es cuántas veces se
    /// consulta a quien decide. Veintiuna consultas para organizar unos archivos era el coste de
    /// no tener esta pieza, no del sistema (2026-08-03).
    ///
    /// Se PARA en el primer paso que no confirme lo esperado, y dice dónde. Seguir tras un fallo
    /// es exactamente cómo un error se convierte en daño tres pasos después: ya nos pasó, y el
    /// ancla de ubicación existe por eso. Aquí se aplica a la secuencia entera.
    ///
    /// Formato: [{"op":"go_to","surface":"..."},
    ///           {"op":"take","exit":"...","at":"...","action":"click|addselect|doubleclick"},
    ///           {"op":"type","text":"...","at":"..."}]
    /// </summary>
    private string Run(string pasosJson)
    {
        if (pasosJson.Length == 0) return "falta `steps`: la lista de pasos a ejecutar";

        List<Dictionary<string, string>> pasos;
        try
        {
            pasos = new List<Dictionary<string, string>>();
            using var doc = System.Text.Json.JsonDocument.Parse(pasosJson);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in el.EnumerateObject())
                    d[p.Name] = p.Value.ValueKind == System.Text.Json.JsonValueKind.String
                        ? p.Value.GetString() ?? "" : p.Value.ToString();
                pasos.Add(d);
            }
        }
        catch (Exception e) { return $"no entendí `steps`: {e.Message}"; }

        var informe = new System.Text.StringBuilder();
        var reloj = System.Diagnostics.Stopwatch.StartNew();
        int hechos = 0;

        foreach (var p in pasos)
        {
            string op = p.TryGetValue("op", out var o) ? o.Trim().ToLowerInvariant() : "";
            string V(string k) => p.TryGetValue(k, out var v) ? v.Trim() : "";
            long t0 = reloj.ElapsedMilliseconds;

            string r = op switch
            {
                "go_to" => GoTo(V("surface")),
                "take" => Take(V("exit"), V("action"), V("at")),
                "type" => Type(V("text"), V("target"), V("at")),
                "unblock" => Unblock(V("at"), V("choose")),
                _ => $"paso desconocido: «{op}»",
            };
            long ms = reloj.ElapsedMilliseconds - t0;
            hechos++;

            bool mal = r.StartsWith("NO actúo", StringComparison.Ordinal)
                    || r.StartsWith("no ", StringComparison.OrdinalIgnoreCase)
                    || r.StartsWith("ATASCADO", StringComparison.Ordinal)
                    || r.Contains("pero no se llegó", StringComparison.OrdinalIgnoreCase)
                    || r.Contains("paso desconocido", StringComparison.Ordinal);

            informe.AppendLine($"  {hechos,2}. [{ms,5} ms] {op} {V("exit")}{V("surface")}{V("text")} → {Recortar(r, 90)}");
            if (mal)
            {
                reloj.Stop();
                LogBus.Log("mapa-mcp", $"PLAN detenido en el paso {hechos}/{pasos.Count}");
                return $"PLAN DETENIDO en el paso {hechos} de {pasos.Count} ({reloj.ElapsedMilliseconds} ms).\n"
                     + informe.ToString()
                     + "  No sigo tras un fallo: continuar es cómo un error se vuelve daño más adelante.";
            }
        }

        reloj.Stop();
        LogBus.Log("mapa-mcp", $"PLAN completo: {hechos} paso(s) en {reloj.ElapsedMilliseconds} ms");
        return $"PLAN COMPLETO: {hechos} paso(s) en {reloj.ElapsedMilliseconds} ms, una sola consulta.\n"
             + informe.ToString();
    }

    private static string Recortar(string s, int max)
    {
        s = (s ?? "").Replace("\n", " · ");
        return s.Length > max ? s[..max] + "…" : s;
    }

    /// <summary>
    /// EL DESBLOQUEADOR. Detecta que la ejecución está atascada, sale del atasco y reanuda por el
    /// mapa, dejando constancia de lo ocurrido.
    ///
    /// Nace de una crítica acertada: estábamos arreglando los colapsos uno a uno —un diálogo, otro
    /// diálogo, otro más— en vez de tener un sistema que los resuelva. Los bloqueos comparten forma
    /// (algo se cruza, hay que responderle, y después hay que volver a donde íbamos), así que
    /// merecen un mecanismo, no un parche por cada caso.
    ///
    /// La política es DELIBERADAMENTE conservadora, y es lo importante de este diseño:
    ///   • una sola opción → es informativo, se acepta y ya está;
    ///   • hay una opción que NO compromete nada (Cancelar, No, Cerrar) → esa;
    ///   • cualquier otra cosa → NO se adivina. Se describe y decide la capa consciente.
    /// Elegir mal aquí es destructivo —el aviso de cambiar la extensión de un archivo tiene un «Sí»
    /// que lo corrompe—, así que ante la duda se prefiere no avanzar antes que avanzar mal.
    /// </summary>
    /// <param name="reanudarEn">A dónde volver una vez resuelto, para continuar la tarea.</param>
    /// <param name="eleccion">Opción impuesta por la capa consciente cuando la política no decide.</param>
    private string Unblock(string reanudarEn, string eleccion)
    {
        var (titulo, textos, opciones) = LeerInterrupcion();
        if (opciones.Count == 0)
            return "no hay nada que desbloquear: no veo ningún diálogo delante.";

        string elegida = eleccion.Length > 0
            ? opciones.FirstOrDefault(o => o.Equals(eleccion, StringComparison.OrdinalIgnoreCase)) ?? ""
            : OpcionSegura(opciones);

        if (elegida.Length == 0)
            return $"ATASCADO · decide tú, esto no lo automatizo.\n"
                 + $"  Diálogo: «{titulo}»\n"
                 + $"  Dice: {string.Join(" ", textos.Take(3))}\n"
                 + $"  Opciones: {string.Join(", ", opciones.Select(o => $"«{o}»"))}\n"
                 + (_ultimaAccion.Length > 0 ? $"  Veníamos de: {_ultimaAccion}\n" : "")
                 + "  Hay una DECISIÓN aquí, y depende de lo que estuvieras intentando: continuar o "
                 + "no es tuyo, no mío. Vuelve a llamarme con `choose` indicando la opción.";

        // El veto, ahora sobre el VERBO y no sobre el tipo de control: responder a un diálogo es
        // legítimo —para eso está—, lo que no lo es es responder «Eliminar». Aunque lo pida la
        // capa consciente: esa decisión es del usuario.
        if (SafeToClick.EsDestructivo(elegida, out string motivo))
            return $"NO pulso «{elegida}»: {motivo}. Una opción destructiva la confirma el usuario, no yo.";

        var paso = new PlanStep
        {
            StepOrder = 1, ActionType = "click",
            Selector = $"uia:name={elegida};ct=Button", Label = elegida,
        };
        if (!_uia.Execute(paso, out string error))
            return $"no pude pulsar «{elegida}» para salir del atasco: {error}";

        // ¿Se fue de verdad? Un desbloqueo que no desbloquea es peor que no intentarlo.
        bool libre = false;
        for (int i = 0; i < 20; i++)
        {
            System.Threading.Thread.Sleep(120);
            if (LeerInterrupcion().Opciones.Count == 0) { libre = true; break; }
        }
        LogBus.Log("mapa-mcp", $"DESBLOQUEO: «{titulo}» → pulsado «{elegida}» · {(libre ? "resuelto" : "sigue ahí")}");
        if (!libre)
            return $"pulsé «{elegida}» y el diálogo «{titulo}» sigue delante. No insisto sola: dime qué hacer.";

        // Reanudar por el mapa, que es de lo que se trata: salir del atasco no sirve de nada si la
        // tarea no puede continuar desde donde estaba.
        string vuelta = reanudarEn.Length > 0 ? GoTo(reanudarEn) : "";
        return $"DESBLOQUEADO. Era «{titulo}» ({string.Join(" ", textos.Take(1))}); pulsé «{elegida}»."
             + (vuelta.Length > 0 ? $"\n  Reanudación: {vuelta}" : "")
             + "\n  INCIDENTE registrado — si este diálogo se repite, es una regla que falta.";
    }

    /// <summary>
    /// La opción que se puede tomar SIN decidir nada, o "" si hay que preguntar al consciente.
    ///
    /// Solo hay un caso: el aviso informativo, el que tiene una única salida. Ahí no se elige nada
    /// —se acusa recibo— y automatizarlo no arriesga.
    ///
    /// En cuanto hay dos opciones hay una DECISIÓN, y no es de esta capa. La versión anterior
    /// prefería siempre la que «no compromete» (Cancelar/No/Cerrar), y eso parecía prudente y era
    /// falso: si la tarea quería continuar de verdad, cancelar por regla la rompe igual, solo que
    /// en silencio y con aire de cautela. Si continuar o no depende de lo que se estuviera
    /// intentando, y eso solo lo sabe quien tiene la intención (2026-08-03, corregido por el
    /// usuario). Se prefiere preguntar a acertar por casualidad.
    /// </summary>
    private static string OpcionSegura(List<string> opciones) =>
        opciones.Count == 1 ? opciones[0] : "";

    /// <summary>
    /// Si delante hay un DIÁLOGO, lo describe como lo que es: una interrupción con una pregunta y
    /// unas opciones. Devuelve "" si no lo hay.
    ///
    /// El sistema los trataba como nodos del grafo —«estás en uia://explorer.exe/ubicación-no-
    /// disponible, 15 salidas»— y eso es un error de modelado con consecuencias: quien tenga que
    /// resolverlo, persona o modelo, recibía ruido en vez del dato que necesita. Un diálogo no es
    /// un sitio al que se llega: es algo que se cruza en el camino, dice POR QUÉ, y ofrece unas
    /// opciones entre las que hay que elegir. Es la misma corrección que ya hicimos con los menús
    /// («no es otro sitio, es una capa»), aplicada a las interrupciones (2026-08-03).
    ///
    /// Solo describe. No decide: elegir entre «Aceptar», «Omitir» o «Sí» es criterio —el aviso de
    /// cambiar la extensión de un archivo tiene un «Sí» que lo corrompe— y esa decisión es de la
    /// capa consciente, con el veto de SafeToClick encima.
    /// </summary>
    private string DescribirInterrupcion()
    {
        string d = Interrupcion.Describir();
        if (d.Length > 0) LogBus.Log("mapa-mcp", "INTERRUPCIÓN detectada");
        return d;
    }

    /// <summary>
    /// Lee el diálogo que haya delante: título, lo que dice y entre qué se puede elegir.
    /// Opciones vacías = no hay diálogo.
    ///
    /// Se reconoce por su FORMA —pocos botones de respuesta más texto que explica— y no por el
    /// título, que cambia con el idioma y con cada versión de Windows. El explorador normal, con
    /// 18 botones, no se confunde (comprobado el 2026-08-03).
    /// </summary>
    private (string Titulo, List<string> Textos, List<string> Opciones) LeerInterrupcion()
        => Interrupcion.Leer();

    private (string Titulo, List<string> Textos, List<string> Opciones) LeerInterrupcionVieja()
    {
        var textos = new List<string>();
        var opciones = new List<string>();
        string titulo = "";
        try
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return (titulo, textos, opciones);
            var v = System.Windows.Automation.AutomationElement.FromHandle(fg);
            if (v == null) return (titulo, textos, opciones);

            foreach (System.Windows.Automation.AutomationElement b in v.FindAll(
                System.Windows.Automation.TreeScope.Descendants,
                new System.Windows.Automation.PropertyCondition(
                    System.Windows.Automation.AutomationElement.ControlTypeProperty,
                    System.Windows.Automation.ControlType.Button)))
            {
                try { string n = b.Current.Name?.Trim() ?? ""; if (n.Length > 0 && !opciones.Contains(n)) opciones.Add(n); }
                catch { }
            }
            // Muchos botones = es una app, no un diálogo. Ninguno = no hay nada que responder.
            if (opciones.Count == 0 || opciones.Count > 8) { opciones.Clear(); return (titulo, textos, opciones); }

            foreach (System.Windows.Automation.AutomationElement t in v.FindAll(
                System.Windows.Automation.TreeScope.Descendants,
                new System.Windows.Automation.PropertyCondition(
                    System.Windows.Automation.AutomationElement.ControlTypeProperty,
                    System.Windows.Automation.ControlType.Text)))
            {
                try { string n = t.Current.Name?.Trim() ?? ""; if (n.Length > 12 && !textos.Contains(n)) textos.Add(n); }
                catch { }
            }
            if (textos.Count == 0) { opciones.Clear(); return (titulo, textos, opciones); }

            try { titulo = v.Current.Name?.Trim() ?? ""; } catch { }
        }
        catch { opciones.Clear(); }
        return (titulo, textos, opciones);
    }

    /// <summary>
    /// Registra las salidas de la pantalla actual, si aún no se conocen.
    ///
    /// El mapa solo se llenaba durante un recorrido automático, así que el asistente podía LLEGAR
    /// a un sitio nuevo por MCP y quedarse ciego allí: «0 salidas conocidas» estando delante de
    /// una carpeta llena de cosas (2026-08-02). Preguntar dónde estoy es el momento natural para
    /// mirar alrededor — el terreno se aprende viviendo, no solo explorando a propósito.
    /// </summary>
    private void ObservarAqui(string nodo, bool forzar = false)
    {
        try
        {
            if (forzar) { ObservarSinGuardia(nodo); return; }
            // Se mira alrededor salvo que la pantalla ya se conozca COMPLETA: con salidas
            // recorribles Y con sus acciones. «Alguna salida» no bastaba (conectividad pasiva sin
            // acción), y «alguna recorrible» tampoco: los nodos mapeados antes de clasificar
            // puertas conocían la navegación pero ninguna acción, y sin este repaso se quedaban
            // así para siempre.
            // Se mira alrededor salvo que la pantalla se conozca DE VERDAD. «Que haya alguna de
            // cada clase» no bastaba: la arista de subida que se aprende al entrar es ella sola una
            // acción, así que una carpeta recién creada parecía conocida y al llegar solo se veía
            // «Subir un nivel» — se pidió «Pegar» y no existía, con la barra a la vista
            // (2026-08-02). Una pantalla real tiene muchas puertas; dos no es conocerla.
            var conocidas = _map.ExitsFrom(nodo);
            if (conocidas.Count(h => h.Info.Selector.Length > 0) >= 6
                && conocidas.Count(h => h.Info.Kind.Equals("accion", StringComparison.OrdinalIgnoreCase)) >= 3) return;
            ObservarSinGuardia(nodo);
        }
        catch { }
    }

    /// <summary>
    /// Relee la pantalla y anota lo que haya, sin preguntarse si ya se conocía.
    ///
    /// Hace falta después de EJECUTAR una acción: un menú abierto no es una pantalla nueva —la
    /// superficie sigue siendo la misma— así que el guardia de «esto ya se conoce» impedía ver los
    /// elementos que acababan de aparecer. Se pulsaba «Nuevo», el menú se abría con «Carpeta»
    /// dentro, y el asistente seguía viendo la lista de antes (2026-08-02).
    /// </summary>
    private void ObservarSinGuardia(string nodo)
    {
        try
        {
            _lector.Read();

            var puertas = new List<(string, string, string, string[])>();
            foreach (var el in _lector.Elements)
            {
                // Igual que el crawler: TODO lo accionable, también los botones de ejecución.
                if (el.ControlType.Equals("text", StringComparison.OrdinalIgnoreCase)
                    || el.ControlType.Equals("image", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var (l, t, sels) = UiaSurface.DescribeElement(el.Native);
                    var utiles = sels.Where(s => !s.Contains("path=", StringComparison.Ordinal)
                        && !System.Text.RegularExpressions.Regex.IsMatch(s, @"(name|aid)=(;|$)")).ToArray();
                    if (utiles.Length == 0) continue;
                    puertas.Add((l.Length > 0 ? l : el.Label, t.Length > 0 ? t : el.ControlType,
                                 utiles[0], utiles.Skip(1).ToArray()));
                }
                catch { }
            }
            if (puertas.Count == 0) return;

            _map.ObserveExits(nodo, puertas);
            LogBus.Log("mapa-mcp", $"al llegar a '{nodo}' se anotaron {puertas.Count} salida(s)");
        }
        catch { }
    }

    /// <summary>
    /// Las superficies conocidas, agrupadas por app y ordenadas por frecuencia — lo más visitado
    /// primero, que es lo que un humano llamaría "los sitios donde trabajo".
    /// </summary>
    private string Places(string app)
    {
        var nodos = _map.Nodes.AsEnumerable();
        if (app.Length > 0)
            nodos = nodos.Where(kv => kv.Key.Contains(app, StringComparison.OrdinalIgnoreCase));

        var lista = nodos.OrderByDescending(kv => kv.Value.Visits).Take(60).ToList();
        if (lista.Count == 0) return app.Length > 0
            ? $"el mapa no conoce ninguna pantalla de «{app}» todavía"
            : "el mapa está vacío: aún no se ha observado ninguna pantalla";

        var sb = new System.Text.StringBuilder($"{lista.Count} pantalla(s) conocida(s):\n");
        foreach (var kv in lista)
            sb.AppendLine($"  {kv.Key}  ({kv.Value.Visits} visita/s)");
        return sb.ToString();
    }

    /// <summary>
    /// A dónde se puede ir desde una pantalla, y con qué. Se dice EXPLÍCITAMENTE cuáles no tienen
    /// acción: que el modelo sepa que existe un camino pero que no sabemos recorrerlo es
    /// información útil —puede pedirlo al usuario o buscar otra vía—, y ocultarlo sería fingir que
    /// el mapa es más completo de lo que es.
    /// </summary>
    private string Routes(string surface)
    {
        string desde = surface.Length > 0 ? surface : (_where()?.Id ?? "");
        if (desde.Length == 0) return "no sé desde dónde: pasa `surface` o asegúrate de que hay una app en primer plano";

        var salidas = _map.ExitsFrom(desde);
        if (salidas.Count == 0) return $"el mapa no conoce ninguna salida desde «{desde}»";

        // Navegación y ejecución separadas: son preguntas distintas («¿a dónde puedo ir?» vs
        // «¿qué puedo hacer aquí?») y mezclarlas obliga al modelo a adivinar cuál es cuál.
        var sb = new System.Text.StringBuilder($"Desde «{desde}»:\n");
        foreach (var h in salidas.Where(x => !x.Info.Kind.Equals("accion", StringComparison.OrdinalIgnoreCase)))
            sb.AppendLine(h.Info.Selector.Length > 0
                ? $"  → {h.To}   pulsando «{h.Info.Label}»  ({h.Info.Count} vez/veces)"
                : $"  → {h.To}   (observado {h.Info.Count} vez/veces, pero NO se sabe con qué acción)");

        var acciones = salidas.Where(x => x.Info.Kind.Equals("accion", StringComparison.OrdinalIgnoreCase)
                                       && x.Info.Selector.Length > 0).ToList();
        if (acciones.Count > 0)
        {
            sb.AppendLine("Acciones disponibles aquí (se toman con map_take, no navegan):");
            sb.AppendLine("  " + string.Join(", ", acciones.Select(a => $"«{a.Info.Label}»")));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Recorre la ruta. Ejecuta cada tramo y COMPRUEBA la llegada antes de seguir; si un tramo no
    /// lleva a donde debía, se detiene y lo dice. Nunca improvisa: si no hay ruta completa, no se
    /// mueve — el asistente ya tiene computer-use para lo desconocido, y mezclar ambas cosas
    /// convertiría un fallo de mapa en clics a ciegas.
    /// </summary>
    private string GoTo(string destino)
    {
        if (destino.Length == 0) return "falta `surface`: a dónde hay que ir";

        // El destino dice a qué app pertenece la tarea: si el foco se ha ido, se recupera antes de
        // calcular nada. Planificar una ruta desde el centro de notificaciones no tiene sentido.
        string app = AppDe(destino);
        if (app.Length > 0)
        {
            _ultimaApp = app;
            if (!AsegurarFoco(app))
                return $"no pude poner «{app}» en primer plano (ahora hay «{AppEnFrente()}»); no me muevo a ciegas";
        }

        var actual = _where();
        if (actual == null) return "no se pudo determinar dónde estamos ahora mismo";

        // ¿El destino es justo de donde venimos? Entonces «Atrás» es el camino más corto y seguro,
        // y lo sabemos con certeza porque lo recordamos de ESTA sesión. Si el destino es otro, no
        // se toca: pulsar Atrás «a ver si suena» es como se acabó subiendo hasta Disco local (C:).
        if (string.Equals(DestinoDeAtras(), destino, StringComparison.OrdinalIgnoreCase))
        {
            var atras = new PlanStep
            {
                StepOrder = 1, ActionType = "click",
                Selector = "uia:aid=backButton;ct=Button", Label = "Atrás",
            };
            if (_uia.Execute(atras, out _) && Llego(destino, 4000))
            {
                Anotar(actual.Id, destino);
                ObservarAqui(destino);
                LogBus.Log("mapa-mcp", $"vuelta por «Atrás» (historial de sesión) → {destino}");
                return $"volví a «{destino}» con «Atrás» (era de donde venía)";
            }
            LogBus.Log("mapa-mcp", "«Atrás» no llevó a donde el historial decía; se sigue por el mapa");
        }

        var ruta = _map.Route(actual.Id, destino);

        // Sin ruta conocida, queda el ATAJO: un elemento presente en TODAS las pantallas —el panel
        // lateral, una barra de la app— que lleva al destino desde donde sea. No hace falta haber
        // recorrido nunca este camino concreto para poder usarlo.
        if (ruta == null)
        {
            var atajo = _map.AtajoHacia(destino);
            if (atajo != null)
            {
                LogBus.Log("mapa-mcp", $"sin ruta; atajo por cromo «{atajo.Info.Label}» hacia «{destino}»");
                ruta = new List<SurfaceMap.Hop> { atajo };
            }
        }
        if (ruta == null)
            return $"no conozco una ruta COMPLETA de «{actual.Id}» a «{destino}». "
                 + "Puede que el camino exista pero falte saber con qué acción se recorre alguno de sus tramos.";
        if (ruta.Count == 0) return $"ya estás en «{destino}»";

        LogBus.Log("mapa-mcp", $"ruta de {ruta.Count} tramo(s) hacia «{destino}»");

        for (int i = 0; i < ruta.Count; i++)
        {
            var h = ruta[i];
            var paso = new PlanStep
            {
                StepOrder = i + 1,
                // La acción con la que se APRENDIÓ la arista, no un clic por defecto: una carpeta
                // de la lista solo se abre con doble clic, y recorrerla con un clic la seleccionaría
                // sin navegar — la ruta prometería un camino que no cumple.
                ActionType = h.Info.ActionType,
                Selector = h.Info.Selector,
                Label = h.Info.Label,
            };

            if (!_uia.Execute(paso, out string error))
                return $"tramo {i + 1}/{ruta.Count}: no se pudo pulsar «{h.Info.Label}» ({error}). "
                     + $"El recorrido se detuvo en «{_where()?.Id}».";

            // La llegada se COMPRUEBA, no se supone. Sin esto, una acción equivocada —y el mapa
            // tiene ~1 de cada 5— dejaría al modelo creyendo que está donde no está.
            if (!Llego(h.To, 4000))
                return $"tramo {i + 1}/{ruta.Count}: pulsé «{h.Info.Label}» pero no se llegó a «{h.To}». "
                     + $"Estamos en «{_where()?.Id}». La ruta del mapa no coincide con la realidad aquí.";

            LogBus.Log("mapa-mcp", $"✓ tramo {i + 1}/{ruta.Count}: «{h.Info.Label}» → {h.To}");
        }
        Anotar(actual.Id, destino);
        ObservarAqui(destino);   // llegar es mirar alrededor
        return $"llegué a «{destino}» en {ruta.Count} paso(s)";
    }

    /// <summary>
    /// Toma UNA salida de la pantalla actual, la que el modelo elija por su nombre.
    ///
    /// Es la pieza que faltaba, y la pidió el usuario con mejor criterio que el mío: yo había hecho
    /// que <see cref="GoTo"/> exigiera el id exacto del destino —«uia://explorer.exe/videos-
    /// explorador-de-archivos»— y el modelo no tenía por qué acertar esa forma. Le estaba pidiendo
    /// que hablara mi idioma. Con esto el reparto es el natural: el modelo LEE las salidas
    /// (map_routes_from), DECIDE cuál sirve, y el cliente EJECUTA y verifica. La inteligencia de la
    /// ruta es del modelo; la honestidad del paso, nuestra.
    ///
    /// Un salto cada vez, a propósito: así el modelo ve a dónde llegó antes de decidir el
    /// siguiente, en vez de encadenar a ciegas una ruta que quizá dejó de ser válida.
    /// </summary>
    /// <param name="accionPedida">
    /// «click» o «doubleclick» para forzar la forma de pulsar. Existe porque SELECCIONAR y ABRIR
    /// son cosas distintas sobre el mismo elemento: para cortar un archivo hay que seleccionarlo
    /// con un clic, mientras que la acción aprendida para un archivo es el doble clic, que lo
    /// abre en otra aplicación. Sin esto, una tarea de organizar archivos era imposible por la
    /// interfaz: todo intento de tocar un archivo lo abría (2026-08-02). Vacío = la del mapa.
    /// </param>
    /// <param name="dondeCreoEstar">
    /// La superficie donde quien pide la acción CREE estar. Si no coincide con la real, no se
    /// actúa. Es el ancla de toda la ejecución: un paso que falla en silencio deja el recorrido en
    /// otra pantalla, y las acciones siguientes se ejecutan igual de bien… sobre el sitio
    /// equivocado. Así se pegaron archivos en la carpeta de origen y se crearon carpetas anidadas
    /// (2026-08-02). Comprobar la ubicación ANTES convierte un encadenamiento optimista en uno
    /// verificado, y el fallo aparece donde se produce en vez de tres pasos después.
    /// </param>
    private string Take(string salida, string accionPedida = "", string dondeCreoEstar = "")
    {
        string desalineado = ComprobarUbicacion(dondeCreoEstar);
        if (desalineado.Length > 0) return desalineado;

        if (salida.Length == 0) return "falta `exit`: el nombre de la salida a tomar (el que aparece en map_routes_from)";

        // Si algo se llevó el foco entre dos pasos de una tarea, se vuelve a la app de antes: el
        // asistente pidió «Nuevo» y recibió las opciones del centro de notificaciones porque nadie
        // comprobaba dónde estábamos realmente (2026-08-02).
        if (_ultimaApp.Length > 0 && !AppEnFrente().Equals(_ultimaApp, StringComparison.OrdinalIgnoreCase))
            AsegurarFoco(_ultimaApp);

        var actual = _where();
        if (actual == null) return "no se pudo determinar dónde estamos ahora mismo";

        var opciones = _map.ExitsFrom(actual.Id).Where(h => h.Info.Selector.Length > 0).ToList();
        if (opciones.Count == 0)
            return $"desde «{actual.Id}» el mapa no conoce ninguna salida recorrible";

        // Coincidencia exacta primero, y luego por contención — «videos» debe encontrar «Videos»,
        // pero si dos salidas contienen lo pedido NO se elige por el modelo: se le devuelven las
        // candidatas. Adivinar entre dos destinos es exactamente lo que no debe hacer esta capa.
        // El SELECTOR desempata. Dos elementos pueden llamarse igual —en el explorador hay dos
        // «Detalles»: el modo de vista y el panel lateral— y entonces el nombre no alcanza para
        // elegir. Decir «coincide con 2, elige por nombre exacto» dejaba al asistente sin salida,
        // porque el nombre exacto era el mismo (2026-08-02). Se acepta el selector, que sí es único.
        var porSelector = opciones.Where(h =>
            h.Info.Selector.Equals(salida, StringComparison.OrdinalIgnoreCase)
            || h.Info.Selector.Contains($"={salida};", StringComparison.OrdinalIgnoreCase)).ToList();

        var exactas = opciones.Where(h => h.Info.Label.Equals(salida, StringComparison.OrdinalIgnoreCase)).ToList();
        var candidatas = porSelector.Count > 0 ? porSelector
            : exactas.Count > 0 ? exactas
            : opciones.Where(h => h.Info.Label.Contains(salida, StringComparison.OrdinalIgnoreCase)).ToList();

        if (candidatas.Count == 0)
            return $"desde «{actual.Id}» no hay ninguna salida que se llame «{salida}». Disponibles: "
                 + string.Join(", ", opciones.Select(h => $"«{h.Info.Label}»"));
        if (candidatas.Count > 1)
            return $"«{salida}» coincide con {candidatas.Count} salidas: "
                 + string.Join("; ", candidatas.Select(h => $"«{h.Info.Label}» [{h.Info.Selector}]"))
                 + ". Repite `exit` con el SELECTOR de la que quieras (o con su AutomationId).";

        var elegida = candidatas[0];
        _ultimaApp = AppDe(actual.Id).Length > 0 ? AppDe(actual.Id) : _ultimaApp;
        // Seleccionar es TODA acción pedida que no pretende navegar: un clic sobre algo que
        // normalmente se abre con doble, y añadir a la selección. Dejar fuera «addselect» hacía
        // que sumar el segundo archivo se juzgara como puerta y se reportara «la pantalla no
        // cambió» —cierto y engañoso: no tenía que cambiar (2026-08-02).
        bool seleccionar = accionPedida.Equals("addselect", StringComparison.OrdinalIgnoreCase)
                           || (accionPedida.Equals("click", StringComparison.OrdinalIgnoreCase)
                               && elegida.Info.ActionType.Equals("doubleclick", StringComparison.OrdinalIgnoreCase));
        // Se EMPIEZA por el clic simple aunque la arista diga doble: es la acción menos agresiva y
        // la que funciona en los menús de navegación. Si no mueve nada, más abajo se sube al doble.
        string accionInicial = accionPedida.Length > 0 ? accionPedida
            : elegida.Info.ActionType.Equals("doubleclick", StringComparison.OrdinalIgnoreCase)
                ? "click" : elegida.Info.ActionType;
        var paso = new PlanStep
        {
            StepOrder = 1,
            ActionType = accionInicial,
            Selector = elegida.Info.Selector,
            Label = elegida.Info.Label,
        };

        string desde = actual.Id;
        _ultimaAccion = $"intentar «{elegida.Info.Label}» en «{desde}»";

        // La selección se lee ANTES de pulsar: «Cortar» la vacía, así que después ya no hay nada
        // que contar y no se podría decir sobre qué se actuó.
        _seleccionPrevia = OperaSobreLaSeleccion(elegida.Info.Label) ? SeleccionActual() : new List<string>();

        // Y los elementos de menú TAMBIÉN se cuentan antes: la señal de que un menú se abrió es que
        // haya MÁS que antes, no que haya alguno. Pueden quedar restos del menú anterior en el
        // árbol, y darlos por buenos hacía continuar sin que el menú estuviera abierto: el paso
        // siguiente no encontraba su opción y el grupo entero fallaba (2026-08-03). Comparar contra
        // el estado previo en vez de contra cero es lo que ya nos resolvió la identidad de pantalla.
        int menusAntes = PuedeAbrirMenu(elegida.Info.Label) ? CuantosMenus() : 0;

        if (!_uia.Execute(paso, out string error))
            return $"no se pudo pulsar «{elegida.Info.Label}»: {error}";

        // UN CLIC PRIMERO, EL DOBLE SOLO SI HACE FALTA. La acción de un elemento de lista depende
        // de la APP, no del tipo: en una lista de archivos el doble clic abre, pero en un menú de
        // navegación —el panel de Configuración— un solo clic navega y el segundo lo ANULA, así que
        // el recorrido pulsaba las doce secciones sin moverse de «Inicio» (2026-08-03). En vez de
        // adivinar por app, se prueba lo suave y se sube a lo fuerte solo si no pasó nada: se
        // acierta en las dos sin saber en cuál estamos.
        if (elegida.Info.ActionType.Equals("doubleclick", StringComparison.OrdinalIgnoreCase)
            && accionPedida.Length == 0 && !seleccionar && EsperarCambio(desde, 450).Length == 0)
        {
            var doble = new PlanStep
            {
                StepOrder = 1, ActionType = "doubleclick",
                Selector = elegida.Info.Selector, Label = elegida.Info.Label,
            };
            LogBus.Log("mapa-mcp", $"«{elegida.Info.Label}»: un clic no movió nada, se prueba el doble");
            _uia.Execute(doble, out _);
        }

        // Selección deliberada: no se espera ningún cambio de pantalla, y exigirlo sería reportar
        // fallo a un clic que hizo exactamente lo pedido.
        if (seleccionar)
        {
            LogBus.Log("mapa-mcp", $"✓ seleccionado «{elegida.Info.Label}» (sin abrir)");
            return $"seleccioné «{elegida.Info.Label}» sin abrirlo; ya puedes usar una acción sobre él (Cortar, Copiar, Cambiar nombre…)";
        }

        // PUERTA DE ACCIÓN: su éxito no es llegar a otra pantalla — es haber hecho algo AQUÍ.
        // Exigirle navegación reportaría fallo a un «Nuevo» que abrió su menú perfectamente. Si
        // resulta que sí navegó (un «Guardar como…» que abre diálogo), eso también se cuenta.
        if (elegida.Info.Kind.Equals("accion", StringComparison.OrdinalIgnoreCase))
        {
            // Sobre QUÉ actuó: para Cortar/Copiar/Eliminar es la única forma de comprobar que se
            // hizo sobre lo que se creía, y no sobre lo que quedó seleccionado de un paso anterior.
            string sobre = "";
            if (OperaSobreLaSeleccion(elegida.Info.Label))
            {
                var s = _seleccionPrevia;
                sobre = s.Count > 0
                    ? $" sobre {s.Count} elemento(s): {string.Join(", ", s.Select(x => $"«{x}»"))}"
                    : " sobre NADA seleccionado (probablemente no hizo nada)";
            }

            // Se espera EL EFECTO, no un tiempo. Un botón que abre menú se da por hecho cuando
            // aparecen sus opciones —suele ser <300 ms— y no cuando se agota un reloj; el resto
            // se comprueba en una ventana corta, porque una acción que navega lo hace enseguida.
            // Antes eran 800 ms fijos por acción, casi siempre esperando a nada (2026-08-03).
            bool abreMenu = PuedeAbrirMenu(elegida.Info.Label);
            string tras = EsperarCambio(desde, abreMenu ? 240 : 320);
            if (abreMenu && !EsperarMenu(1500, menusAntes))
                LogBus.Log("mapa-mcp", $"«{elegida.Info.Label}» no llegó a abrir menú (seguía habiendo {menusAntes})");
            LogBus.Log("mapa-mcp", $"✓ acción «{elegida.Info.Label}» ejecutada" + (tras.Length > 0 ? $" → {tras}" : ""));

            // Se relee SIEMPRE: una acción suele destapar cosas nuevas —un menú, un diálogo— en la
            // misma superficie, y sin releer el asistente actuaría sobre la pantalla de antes.
            // Tras una acción se relee SOLO si pudo destapar algo nuevo: un menú, un diálogo. Un
            // «Cortar» o un «Pegar» no cambian las puertas de la pantalla, y releerla entera —con
            // su viaje UIA por cada elemento— costaba segundos por acción sin aportar nada
            // (medido el 2026-08-02: «Carpeta» llegó a agotar 150 s de espera).
            string aqui = tras.Length > 0 ? tras : desde;
            if (abreMenu) ObservarMenus(aqui);
            var nuevas = _map.ExitsFrom(aqui)
                .Where(h => h.Info.Kind.Equals("accion", StringComparison.OrdinalIgnoreCase) && h.Info.Selector.Length > 0)
                .Select(h => h.Info.Label).Distinct().Take(18).ToList();

            return (tras.Length > 0
                    ? $"ejecuté «{elegida.Info.Label}»{sobre} y la pantalla pasó a «{tras}». "
                    : $"ejecuté «{elegida.Info.Label}»{sobre} (la superficie sigue siendo «{desde}»). ")
                 + (nuevas.Count > 0 ? "Ahora hay: " + string.Join(", ", nuevas.Select(n => $"«{n}»")) : "");
        }

        // PUERTA SIN CRUZAR: no hay destino contra el que comparar, así que el éxito es que la
        // pantalla CAMBIE, y lo que se descubre se aprende. Comparar contra el marcador «?selector»
        // hacía que cruzar una puerta se reportara siempre como fallo, incluso llegando —justo lo
        // contrario de para lo que existen las puertas, que es descubrir a dónde dan (2026-08-02).
        if (SurfaceMap.EsPuerta(elegida.To))
        {
            string llegada = EsperarCambio(desde, 4000);
            if (llegada.Length == 0)
                return $"pulsé «{elegida.Info.Label}» pero la pantalla no cambió; sigue sin saberse a dónde da.";

            _map.LearnTraversal(desde, llegada, elegida.Info.Selector, elegida.Info.Alternatives,
                elegida.Info.Label, elegida.Info.ControlType, elegida.Info.ActionType);
            Anotar(desde, llegada);
            AprenderSubida(desde, llegada, elegida.Info.ControlType);
            ObservarAqui(llegada);
            LogBus.Log("mapa-mcp", $"✓ puerta «{elegida.Info.Label}» descubierta → {llegada}");
            return $"tomé «{elegida.Info.Label}»: era una puerta sin explorar y lleva a «{llegada}». Queda aprendida.";
        }

        if (!Llego(elegida.To, 4000))
            return $"pulsé «{elegida.Info.Label}» pero no se llegó a «{elegida.To}». "
                 + $"Estamos en «{_where()?.Id}».";

        // Llegar es mirar alrededor: si no, el asistente se planta en una pantalla nueva y no sabe
        // qué puede hacer allí. Se pegó un archivo en una carpeta recién abierta y la respuesta
        // fue «aquí no hay ninguna salida que se llame Pegar», con la barra a la vista (2026-08-02).
        Anotar(desde, elegida.To);
        AprenderSubida(desde, elegida.To, elegida.Info.ControlType);
        ObservarAqui(elegida.To);
        LogBus.Log("mapa-mcp", $"✓ salida «{elegida.Info.Label}» → {elegida.To}");
        return $"tomé «{elegida.Info.Label}» y llegué a «{elegida.To}»";
    }

    /// <summary>
    /// Escribe texto en un campo. Sin `target`, en el que tenga el foco del teclado.
    ///
    /// Es la primitiva que faltaba para que una tarea completa se pueda hacer por la interfaz:
    /// renombrar exige escribir, y sin ella el asistente podía crear una carpeta pero no ponerle
    /// nombre. Se escribe por VALOR cuando el control lo admite —determinista, sin depender de la
    /// distribución del teclado ni de que ninguna tecla se quede hundida— y solo si no lo admite
    /// se recurre a teclear, con Enter al final para confirmar la edición en línea.
    /// </summary>
    private string Type(string texto, string target, string dondeCreoEstar = "")
    {
        if (texto.Length == 0) return "falta `text`: qué hay que escribir";
        string desalineado = ComprobarUbicacion(dondeCreoEstar);
        if (desalineado.Length > 0) return desalineado;

        if (_ultimaApp.Length > 0 && !AppEnFrente().Equals(_ultimaApp, StringComparison.OrdinalIgnoreCase))
            AsegurarFoco(_ultimaApp);

        string selector = target;
        if (selector.Length == 0)
        {
            // Se ESPERA a que aparezca un campo editable. Una edición en línea —el nombre de una
            // carpeta recién creada— tarda un instante en aparecer, y desde que las acciones son
            // rápidas se llegaba aquí antes que ella: se respondía «no hay ningún campo con el
            // foco» y la carpeta se quedaba como «Nueva carpeta» (2026-08-03).
            for (int i = 0; i < 30; i++)
            {
                try
                {
                    var f = System.Windows.Automation.AutomationElement.FocusedElement;
                    if (f != null && f.Current.ControlType == System.Windows.Automation.ControlType.Edit) break;
                }
                catch { }
                System.Threading.Thread.Sleep(50);   // se sondea fino: se sale en cuanto aparece
            }

            // El campo con el foco: es donde una persona escribiría sin pensarlo.
            try
            {
                var foco = System.Windows.Automation.AutomationElement.FocusedElement;
                string aid = foco?.Current.AutomationId ?? "";
                string nombre = foco?.Current.Name ?? "";
                string tipo = (foco?.Current.ControlType.ProgrammaticName ?? "").Replace("ControlType.", "");
                selector = aid.Length > 0 && !aid.All(char.IsDigit) ? $"uia:aid={aid};ct={tipo}"
                         : nombre.Length > 0 ? $"uia:name={nombre};ct={tipo}"
                         : "";
            }
            catch { }
            if (selector.Length == 0) return "no hay ningún campo con el foco; pasa `target` con su selector";
        }

        var paso = new PlanStep { StepOrder = 1, ActionType = "input", Selector = selector, Value = texto };
        if (!_uia.Execute(paso, out string error))
            return $"no pude escribir en «{selector}»: {error}";

        // Enter confirma: en una edición en línea (renombrar) el texto no se aplica hasta que se
        // acepta, y dejarlo a medias deja la interfaz en un estado del que nadie se acuerda luego.
        keybd_event(0x0D, 0, 0, IntPtr.Zero);
        keybd_event(0x0D, 0, 2, IntPtr.Zero);
        System.Threading.Thread.Sleep(400);

        LogBus.Log("mapa-mcp", $"✓ escrito «{texto}» en {selector}");
        return $"escribí «{texto}» y confirmé con Enter";
    }

    /// <summary>Espera a que la superficie DEJE de ser la de partida y devuelve la nueva, o "".</summary>
    private string EsperarCambio(string desde, int msMax)
    {
        for (int i = 0; i < msMax / 80; i++)
        {
            System.Threading.Thread.Sleep(80);
            string ahora = _where()?.Id ?? "";
            if (ahora.Length > 0 && !string.Equals(ahora, desde, StringComparison.OrdinalIgnoreCase)
                && !ahora.EndsWith("/ventana", StringComparison.OrdinalIgnoreCase))
                return ahora;
        }
        return "";
    }

    /// <summary>Espera a que la superficie sea la esperada. La UI tarda; la paciencia va aquí.</summary>
    private bool Llego(string esperada, int msMax)
    {
        var hasta = DateTime.UtcNow.AddMilliseconds(msMax);
        while (DateTime.UtcNow < hasta)
        {
            string ahora = _where()?.Id ?? "";
            if (ahora.Length > 0 && SurfacePlace.Same(ahora, esperada)) return true;
            System.Threading.Thread.Sleep(200);
        }
        return false;
    }
}
