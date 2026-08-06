using System.Windows.Automation;
using U.Graph;
using U.Graph.Surfaces;
using U.WindowsClient.Diagnostics;
using U.WindowsClient.Uia;

namespace U.WindowsClient.Navigation;

/// <summary>
/// El asistente explorando por sí mismo: recorre una app abriendo lo que encuentra y aprende el
/// grafo con certeza por construcción — cada arista se guarda con la acción que el propio sistema
/// acaba de ejecutar, no con una inferida viendo al usuario.
///
/// Es el mismo principio del explorador manual, sin la mano humana en cada paso. Y por eso las
/// barreras van aquí y son duras:
///
///   · SOLO NAVEGACIÓN. <see cref="SafeToClick"/> decide, y decide sin red y sin modelo: tipos de
///     navegación y veto por verbo. Navegar es reversible; operar no. Un LLM eligiendo qué es
///     seguro acierta casi siempre, y «casi» sobre un botón que borra archivos no es garantía.
///   · PRESUPUESTO. Tope de nodos, de profundidad y de tiempo. Un recorrido sin techo sobre la
///     máquina de alguien es una promesa que no se puede cumplir.
///   · SE PUEDE PARAR EN CUALQUIER MOMENTO, y parar significa parar ya: el token se comprueba
///     antes de cada clic.
///
/// El regreso es por Alt+Izquierda (el «Atrás» del explorador y de casi toda app con historial):
/// es un GESTO, no un botón que haya que identificar, así que no depende de acertar una etiqueta.
/// Si el regreso falla, se abandona esa rama en vez de seguir a ciegas desde donde sea — perder
/// una rama es barato; explorar creyendo estar en otro sitio, no.
/// </summary>
public sealed class GraphCrawler
{
    private readonly SurfaceMap _map;
    private readonly Func<SurfaceLocator.SurfaceLocation?> _where;
    private readonly UiaReader _reader = new();
    // SoloEnFoco: el recorrido mapea UNA app y siempre la trae al frente antes de actuar, así que
    // un selector que no esté en la ventana de delante no está. Sin esto, el barrido por todo el
    // escritorio hacía algo peor que fallar: al mapear Configuración —que no tiene «Atrás»— buscó
    // el botón por ahí y lo encontró EN OTRA APP, pulsando el explorador de archivos que estaba
    // detrás (2026-08-03). Un respaldo que actúa sobre una aplicación distinta no es un respaldo.
    private readonly UiaSurface _ejecutor = new() { Log = s => LogBus.Log("crawler", s), SoloEnFoco = true };

    /// <summary>Progreso para la UI: nodos vistos, aristas aprendidas y qué está haciendo ahora.</summary>
    public event Action<string, int, int>? Progress;

    /// <summary>Una arista recién aprendida (desde, hasta, etiqueta), en el momento de aprenderla.</summary>
    public event Action<string, string, string>? EdgeLearned;

    /// <summary>La pantalla en la que está el recorrido ahora mismo, para resaltarla en el grafo.</summary>
    public event Action<string>? NodeEntered;

    /// <summary>
    /// Lo aprendido en ESTA corrida, en orden. Es lo que se dibuja en la vista de grafo: no el
    /// terreno acumulado de días —que mezcla observado con explorado y ramas de otras apps— sino
    /// exactamente lo que este recorrido comprobó pulsando. Cada entrada es una arista que se
    /// ejecutó y cuya llegada se verificó.
    /// </summary>
    public IReadOnlyList<(string From, string To, string Label)> Learned => _learned;
    private readonly List<(string From, string To, string Label)> _learned = new();

    public GraphCrawler(SurfaceMap map, Func<SurfaceLocator.SurfaceLocation?> where)
    {
        _map = map;
        _where = where;
    }

    private int _nodos, _aristas;
    private string _appObjetivo = "";
    private string _raiz = "";
    private readonly HashSet<string> _visitados = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Deja la app objetivo en primer plano y lo confirma. Se llama al empezar y cada vez que el
    /// foco se ha ido —al pulsar el botón del panel, o si el usuario toca otra ventana—, porque
    /// leer y clicar exigen que la app esté delante.
    /// </summary>
    private async Task<bool> EnfocarObjetivoAsync(CancellationToken ct)
    {
        if (_appObjetivo.Length == 0) return false;
        if (EsObjetivoElFrente()) return true;

        if (!EnfocarVentanaDe(_appObjetivo)) AppAligner.FocusOrLaunch(_appObjetivo);
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(150, ct);
            if (EsObjetivoElFrente()) return true;
        }
        LogBus.Log("crawler", $"no logré poner «{_appObjetivo}» delante (ahora hay '{ProcesoDelFrente()}')");
        return false;
    }

    /// <summary>
    /// Quién está delante AHORA. Se consulta al sistema cada vez, no al lector: su
    /// <c>ForegroundProcess</c> es un valor cacheado que solo cambia al llamar a Read(), y
    /// esperar sobre un dato congelado es esperar para siempre — así se colgó la primera versión
    /// de esto (2026-07-31), tres segundos mirando el mismo valor de antes de empezar.
    /// </summary>
    private static string ProcesoDelFrente()
    {
        try
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch { return ""; }
    }

    private bool EsObjetivoElFrente() =>
        ProcesoDelFrente().Equals(_appObjetivo, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// ¿Este selector identifica algo de verdad?
    ///
    /// Se descartan los de RUTA —frágiles por definición: describen dónde estaba el elemento, no
    /// qué es— y sobre todo los que llegan con la clave VACÍA. «Recientes», «Favoritos» y
    /// «Compartido» del panel lateral producían <c>uia:path=;ct=Custom</c>: no tienen nombre ni
    /// AutomationId, así que el único selector posible salía sin contenido y nunca resolvía. El
    /// filtro anterior buscaba selectores TERMINADOS en «path=» y estos no terminan ahí, así que
    /// pasaban y consumían cinco intentos cada uno antes de fallar (2026-08-01).
    /// </summary>
    private static bool EsSelectorUtil(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        if (s.Contains("path=", StringComparison.Ordinal)) return false;  // ruta: no es identidad
        return !System.Text.RegularExpressions.Regex.IsMatch(s, @"(name|aid)=(;|$)");
    }

    /// <summary>
    /// ¿La app tiene botón «Subir un nivel»? Solo el explorador de archivos.
    ///
    /// Aprender la subida al padre sin comprobar que el botón EXISTE llenaba el grafo de otras
    /// apps con caminos imposibles: Configuración acabó con rutas que pasaban por un botón que no
    /// tiene, y 6 de 7 navegaciones fallaban sobre un mapa por lo demás correcto (2026-08-03). Una
    /// regla ganada en una app no se exporta a las demás sin verificarla.
    /// </summary>
    /// <summary>
    /// Ejecuta algo que habla con OTRO proceso —COM, UIA— con un plazo. Si se pasa, devuelve el
    /// valor de reserva y sigue.
    ///
    /// Hace falta porque estas llamadas pueden no volver NUNCA: mapeando Configuración el
    /// recorrido se quedó tres minutos sin registrar una sola línea, congelado dentro de una de
    /// ellas, y ni el botón de detener respondía (2026-08-03). Un cuelgue silencioso es peor que un
    /// fallo: el fallo se ve y se corrige. Es la misma regla que ya aplicamos al barrido de
    /// ventanas —fallar rápido es parte de ser honesto— en el último sitio donde faltaba.
    ///
    /// El hilo colgado se abandona: no se puede matar sin arriesgar el estado del proceso, y sigue
    /// bloqueado en su llamada hasta que el sistema la resuelva.
    /// </summary>
    private static async Task<T> ConPlazoAsync<T>(Func<T> trabajo, int ms, T reserva, CancellationToken ct)
    {
        try
        {
            var tarea = Task.Run(trabajo, ct);
            var cual = await Task.WhenAny(tarea, Task.Delay(ms, ct));
            if (cual == tarea) return await tarea;
            LogBus.Log("crawler", $"una llamada al sistema no respondió en {ms} ms; se sigue sin ella");
            return reserva;
        }
        catch (OperationCanceledException) { throw; }
        catch { return reserva; }
    }

    private static bool HayBotonSubir()
    {
        try
        {
            var raiz = AutomationElement.FromHandle(GetForegroundWindow());
            return raiz?.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "upButton")) != null;
        }
        catch { return false; }
    }

    /// <summary>¿Este identificador de superficie pertenece a la app que estamos mapeando?</summary>
    private bool EsDelObjetivo(string id) =>
        id.StartsWith($"uia://{_appObjetivo}.exe/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// La ruta de la carpeta que el explorador tiene abierta en primer plano, preguntándole a él.
    ///
    /// Hace falta porque UIA no dice si un elemento de la lista es carpeta o archivo: la propiedad
    /// <c>ItemType</c> llega vacía en el explorador de Windows 11 (medido el 2026-08-01: 30 de 30
    /// elementos de una carpeta de capturas se dieron por «entrables»). Y el nombre no basta,
    /// porque Windows oculta las extensiones conocidas: «Captura de pantalla 1» es un .png que no
    /// lo parece, y por ahí se coló el recorrido hasta dentro de Photos.
    ///
    /// Con la ruta, el sistema de archivos responde la pregunta exacta y al instante, sin abrir
    /// nada. Es la misma idea de siempre: para saber QUÉ hay, se lee el disco; la UI se usa para
    /// aprender CÓMO se navega.
    /// </summary>
    private static string CarpetaEnPrimerPlano()
    {
        try
        {
            IntPtr fg = GetForegroundWindow();
            var t = Type.GetTypeFromProgID("Shell.Application");
            if (t == null) return "";
            dynamic? shell = Activator.CreateInstance(t);
            if (shell == null) return "";
            foreach (dynamic w in shell.Windows())
            {
                try
                {
                    if ((IntPtr)(long)w.HWND != fg) continue;
                    return (string)w.Document.Folder.Self.Path ?? "";
                }
                catch { }
            }
        }
        catch { }
        return "";
    }

    /// <summary>
    /// Trae al frente una ventana REAL del proceso, enumerando las de nivel superior.
    ///
    /// Hace falta porque <c>MainWindowHandle</c> no sirve para el shell: explorer.exe ES Windows,
    /// y su ventana principal es el escritorio — ninguna ventana de carpeta se encuentra por ahí.
    /// Enumerar y quedarse con la primera visible y con título funciona igual para el explorador
    /// que para cualquier app con varias ventanas.
    /// </summary>
    private static bool EnfocarVentanaDe(string proc)
    {
        IntPtr elegida = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            var sb = new System.Text.StringBuilder(256);
            if (GetWindowText(h, sb, sb.Capacity) == 0) return true; // sin título: cromo, no ventana
            try
            {
                GetWindowThreadProcessId(h, out uint pid);
                using var p = System.Diagnostics.Process.GetProcessById((int)pid);
                if (!p.ProcessName.Equals(proc, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { return true; }
            elegida = h;
            return false; // la primera sirve
        }, IntPtr.Zero);

        if (elegida == IntPtr.Zero) return false;

        // SOLO se restaura si está minimizada. SW_RESTORE sobre una ventana MAXIMIZADA la devuelve
        // a su tamaño anterior, y eso es lo que encogía la app del usuario cada vez que se pulsaba
        // «Mapear» (2026-08-01). Traer algo al frente no debería cambiarle el tamaño a nadie.
        if (IsIconic(elegida)) ShowWindow(elegida, SW_RESTORE);
        return SetForegroundWindow(elegida);
    }

    private delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder s, int max);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    private const int SW_RESTORE = 9;

    /// <summary>
    /// Recorre en profundidad desde donde esté ahora. <paramref name="maxNodos"/> y
    /// <paramref name="maxProfundidad"/> son el presupuesto; el token, el freno.
    /// </summary>
    public async Task<string> CrawlAsync(int maxNodos, int maxProfundidad, CancellationToken ct)
    {
        _nodos = 0; _aristas = 0; _visitados.Clear(); _learned.Clear(); _raiz = "";
        _destinosSabidos.Clear(); _puertasDeRaiz.Clear();

        string raiz = _where()?.Id ?? "";
        if (raiz.Length == 0) return "no sé dónde estoy: trae al frente la app que quieres mapear";
        _raiz = raiz;

        LogBus.Log("crawler", $"inicio en '{raiz}' · tope {maxNodos} nodo(s), profundidad {maxProfundidad}");

        // La app objetivo AL FRENTE antes de nada. El recorrido lo lanza un botón de Ü, así que en
        // ese instante la ventana de delante es la NUESTRA: sin esto el lector devuelve los botones
        // del propio panel, SafeToClick los rechaza por no ser navegación, y el recorrido termina
        // con «1 pantalla, 0 rutas» sin haber mirado la app. Pasó en la primera corrida (2026-07-31).
        _appObjetivo = AppAligner.ProcessFromOrigin(raiz);
        if (!await EnfocarObjetivoAsync(ct))
            return $"no pude traer «{_appObjetivo}» al frente para explorarla";

        try
        {
            await RecorrerAsync(raiz, maxNodos, maxProfundidad, ct);
        }
        catch (OperationCanceledException)
        {
            return $"detenido por el usuario · {_nodos} pantalla(s), {_aristas} ruta(s) aprendida(s)";
        }
        catch (InvalidOperationException e)
        {
            return $"parado: {e.Message} · {_nodos} pantalla(s), {_aristas} ruta(s) aprendida(s)";
        }
        return $"listo · {_nodos} pantalla(s) recorrida(s), {_aristas} ruta(s) aprendida(s)";
    }

    /// <summary>Un nodo abierto y sus puertas AÚN por cruzar, en el orden en que se ven en pantalla.</summary>
    private sealed class Frente
    {
        public string Nodo = "";
        public int Prof;
        /// <summary>Veces seguidas que no se ha podido volver aquí. Un fallo suelto no cierra un nodo.</summary>
        public int FallosSeguidos;
        public Queue<(string Label, string Tipo, string Selector, string[] Alts, bool Entrable, string Grupo)> Puertas = new();
    }

    /// <summary>
    /// EL GRAFO ES EL PLAN, no el registro. Cada pantalla abierta aporta sus puertas a una frontera
    /// explícita, y el bucle consume LA FRONTERA: nada de lo descubierto depende de referencias
    /// vivas de UI ni de la pila de llamadas.
    ///
    /// Antes esto era una recursión que caminaba mirándose los pies —listas en memoria capturadas
    /// de la pantalla— y el mapa solo anotaba resultados. De ahí salieron como síntomas media
    /// sesión de fallos: referencias muertas tras cada repintado, «solo entra a la primera
    /// carpeta», y ramas enteras perdidas cuando un regreso fallaba, porque el pendiente vivía en
    /// la pila y moría con ella (2026-08-01). Ahora un regreso fallido cuesta exactamente las
    /// puertas de ese nodo — el resto de la frontera sobrevive, y queda registrado cuántas se
    /// perdieron para poder retomarlas.
    ///
    /// El orden es en profundidad por diseño: se agota la sección abierta antes de pasar a la
    /// hermana, que es como pidió el usuario que se explorara (Documentos entero, luego Videos).
    /// </summary>
    private async Task RecorrerAsync(string raiz, int maxNodos, int maxProf, CancellationToken ct)
    {
        var pila = new Stack<Frente>();
        var inicial = await AbrirNodoAsync(raiz, 0, ct);
        if (inicial != null) pila.Push(inicial);

        while (pila.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            if (_nodos >= maxNodos)
            {
                LogBus.Log("crawler", $"tope de {maxNodos} pantallas alcanzado; quedan puertas sin cruzar");
                return;
            }

            var frente = pila.Peek();
            if (frente.Puertas.Count == 0) { pila.Pop(); continue; }

            // Estar donde el plan dice. Un solo mecanismo de recolocación para todo — después de
            // bajar, después de un fallo, después de que el usuario tocara algo: si no estamos en
            // el nodo del frente, se vuelve a él. Y si no se puede llegar, se pierden SOLO las
            // puertas de ese nodo: la frontera de los demás sigue viva.
            string actual = _where()?.Id ?? "";
            if (!string.Equals(actual, frente.Nodo, StringComparison.OrdinalIgnoreCase))
            {
                if (await VolverAsync(frente.Nodo, ct))
                {
                    frente.FallosSeguidos = 0;
                }
                else if (++frente.FallosSeguidos >= 3)
                {
                    // Solo se cierra el nodo tras VARIOS intentos. Con un único fallo se perdían
                    // todas sus puertas restantes: al fallar «Code» el retroceso se pasó hasta
                    // Documentos y 20+ subcarpetas quedaron sin mapear, dejando la corrida entera
                    // en profundidad 1 (2026-08-01). Volver es intrínsecamente inestable —el
                    // historial se pasa, una pantalla tarda— así que un intento no es evidencia.
                    LogBus.Log("crawler", $"no pude recolocarme en '{Corto(frente.Nodo)}' tras 3 intentos; " +
                                          $"quedan {frente.Puertas.Count} puerta(s) pendientes ahí");
                    pila.Pop();
                }
                continue;
            }
            frente.FallosSeguidos = 0;

            var (label, tipo, selector, alts, entrable, _) = frente.Puertas.Dequeue();
            if (EsNavegacionGlobalYaConocida(selector, frente.Prof)) continue;
            // Un archivo ya está anotado como puerta; abrirlo solo serviría para irse de la app.
            if (!entrable) continue;

            // Un elemento que falla no se lleva la corrida: se anota y la frontera sigue.
            string destino;
            try { destino = await PulsarYAprenderAsync(frente.Nodo, label, tipo, selector, alts, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                LogBus.Log("crawler", $"«{label}» falló ({e.GetType().Name}: {e.Message}); se sigue con el resto");
                continue;
            }
            if (destino.Length == 0) continue;   // no navegó: acción local, nada que aprender

            if (!_visitados.Contains(destino) && frente.Prof + 1 <= maxProf)
            {
                var hijo = await AbrirNodoAsync(destino, frente.Prof + 1, ct);
                if (hijo != null) pila.Push(hijo);
            }
            // Sin «volver» explícito aquí: la próxima vuelta del bucle detecta dónde estamos y se
            // recoloca sola si hace falta.
        }
    }

    /// <summary>
    /// Entra en un nodo por primera vez: lo cuenta, registra TODAS sus puertas en el mapa —se
    /// crucen o no; son las salidas que existen ahí y el asistente puede consultarlas por MCP—
    /// y devuelve su frente de exploración.
    /// </summary>
    private async Task<Frente?> AbrirNodoAsync(string nodo, int prof, CancellationToken ct)
    {
        if (!_visitados.Add(nodo)) return null;
        _nodos++;
        NodeEntered?.Invoke(nodo);
        Progress?.Invoke($"explorando {Corto(nodo)}", _nodos, _aristas);

        // ALGO CRUZADO DELANTE = no se mapea. Recorrer con un diálogo abierto es pulsar a ciegas:
        // los clics aterrizan donde no deben y todo lo que se aprenda después describe un camino
        // que nadie hizo. Mapear tampoco es el momento de responder diálogos —eso es una decisión—
        // así que se para y se dice cuál es, para que alguien lo resuelva y se relance
        // (2026-08-03: al mapear Configuración se abrió «Cambiar el nombre de tu PC» y el recorrido
        // siguió pulsando detrás).
        if (Interrupcion.Hay())
        {
            string aviso = Interrupcion.Describir().Replace("\n", " ")
                         + " — no mapeo con algo cruzado; resuélvelo y vuelve a lanzarlo.";
            LogBus.Log("crawler", "PARADA · " + aviso);
            throw new InvalidOperationException(aviso);
        }

        var candidatos = await LeerSalidasAsync(ct);
        _map.ObserveExits(nodo, candidatos.Select(c => (c.Label, c.Tipo, c.Selector, c.Alternativas, c.Grupo)));
        LogBus.Log("crawler", $"en '{Corto(nodo)}': {candidatos.Count} salida(s), " +
                              $"{candidatos.Count(c => c.Entrable)} para entrar (carpetas)");

        // El cromo de la raíz queda fichado ANTES de cruzar nada: así ninguna pantalla más
        // profunda vuelve a planificarlo, aunque su destino todavía no se conozca.
        if (prof == 0)
            foreach (var c in candidatos)
                if (!c.Tipo.Equals("listitem", StringComparison.OrdinalIgnoreCase))
                    _puertasDeRaiz.Add(c.Selector);

        var frente = new Frente { Nodo = nodo, Prof = prof };
        foreach (var c in candidatos) frente.Puertas.Enqueue(c);

        // El PLAN, escrito antes de ejecutarlo. Que el orden previsto quede en el log es lo que
        // permite comprobar que se recorre la columna como se ve, y no fiarse de que así sea.
        // El plan REAL: lo que se va a pulsar de verdad, ya sin el cromo que se salta. Antes se
        // registraban los candidatos en bruto y el log parecía decir que cada pantalla replanificaba
        // el panel entero, cuando el filtro sí lo estaba saltando. Un log que miente cuesta una
        // ronda de depuración entera (2026-08-01).
        var plan = candidatos
            .Where(c => c.Entrable && !EsNavegacionGlobalYaConocida(c.Selector, prof))
            .Select(c => c.Label).Take(12).ToList();
        LogBus.Log("crawler", plan.Count > 0
            ? $"plan en '{Corto(nodo)}': {string.Join(" → ", plan)}…"
            : $"plan en '{Corto(nodo)}': nada nuevo que cruzar");
        return frente;
    }

    /// <summary>
    /// ¿Esta salida es del panel de navegación global, y ya la aprendimos desde la raíz?
    ///
    /// El panel izquierdo del explorador está presente en TODAS las pantallas, así que sin este
    /// filtro cada carpeta ganaba las mismas ~9 aristas (Inicio, Galería, Documentos, Videos…):
    /// información cierta pero repetida, que infla el grafo y se come el presupuesto del recorrido
    /// —en la corrida del 2026-07-31, 9 de las 19 rutas eran esto—. Desde la raíz sí se aprenden,
    /// una vez, porque ahí es donde describen algo nuevo.
    /// </summary>
    private bool EsNavegacionGlobalYaConocida(string selector, int prof)
    {
        if (prof == 0) return false;
        // Se salta el cromo cuyo DESTINO ya se conoce, y solo ese. Vetar todo lo que estuviera en
        // la raíz parecía más barato y dejaba agujeros PERMANENTES: «Documentos» no puede
        // aprenderse EN la raíz —pulsarlo allí no cambia de pantalla— y quedaba vetado también en
        // las demás, así que el mapa nunca supo volver a documentos desde ningún sitio
        // (2026-08-02). Una puerta se cruza UNA vez, desde donde primero se pueda; a partir de ahí
        // se deduce sola en el resto de pantallas.
        // Y SE LE PREGUNTA AL MAPA, no solo a lo que haya cruzado ESTA corrida. Desde que el grafo
        // anota TODAS las puertas visibles al llegar a una pantalla, una visita ya revela dónde
        // lleva cada selector que se conozca de antes: volver a cruzarlo desde cada sitio es
        // comprobar N veces lo mismo, y eso es lo que convertía el recorrido en N² — se veía en el
        // log, cada pantalla replanificando el panel lateral entero (2026-08-06, medido por el
        // usuario). Una puerta se cruza UNA vez en toda la app; su destino se deduce en el resto.
        if (_destinosSabidos.Contains(selector) || _map.EsCromoGlobal(selector)) return true;

        return _map.Edges().Any(e =>
            string.Equals(e.Info.Selector, selector, StringComparison.Ordinal)
            && !SurfaceMap.EsPuerta(e.To)
            && SurfaceMap.MismaApp(e.From, e.To));
    }

    /// <summary>
    /// Las puertas visibles en la pantalla de arranque. Es el panel de navegación global: está en
    /// TODAS las pantallas, así que explorarlo es responsabilidad de la raíz y de nadie más.
    ///
    /// Antes se saltaba solo lo que ya tuviera destino conocido, y como se desciende tras aprender
    /// las primeras puertas, las demás seguían siendo «nuevas» en cada nivel: cada pantalla
    /// replanificaba el panel entero y lo primero que pulsaba era «Inicio», que devolvía el
    /// recorrido al principio (2026-08-01). Por eso el contenido de las carpetas solo se alcanzaba
    /// mucho más tarde: el presupuesto se iba en repasar el panel. Sus aristas no se pierden: se
    /// deducen al registrar la puerta, porque el mismo selector lleva al mismo sitio.
    /// </summary>
    private readonly HashSet<string> _puertasDeRaiz = new(StringComparer.Ordinal);

    /// <summary>
    /// Selectores cuyo destino YA conocemos. Pulsarlos otra vez desde otra pantalla no enseña
    /// nada: <see cref="SurfaceMap.ObserveExits"/> deduce esa arista sola al registrar la puerta,
    /// porque el mismo selector lleva al mismo sitio.
    /// </summary>
    private readonly HashSet<string> _destinosSabidos = new(StringComparer.Ordinal);

    /// <summary>Los elementos pulsables por un autónomo, en la pantalla actual.</summary>
    private async Task<List<(string Label, string Tipo, string Selector, string[] Alternativas, bool Entrable, string Grupo)>> LeerSalidasAsync(CancellationToken ct)
    {
        // Quién está delante se le PREGUNTA al sistema, que es instantáneo; antes se leía el árbol
        // UIA entero solo para averiguarlo y luego se volvía a leer para usarlo. Recorrer una
        // pantalla llena cuesta cientos de milisegundos, así que era medio segundo por nodo tirado
        // en responder algo que GetForegroundWindow contesta al momento (2026-08-02).
        if (!EsObjetivoElFrente() && !await EnfocarObjetivoAsync(ct))
            return new List<(string, string, string, string[], bool, string)>();

        string carpeta = await ConPlazoAsync(() => CarpetaEnPrimerPlano(), 2000, "", ct);

        // Leer el árbol de una app grande también puede no volver. Con plazo: una pantalla que no
        // se deja leer se salta, y el recorrido sigue con las demás en vez de congelarse entero.
        return await ConPlazoAsync(() =>
        {
            var salidas = new List<(string, string, string, string[], bool, string)>();
            try
            {
                _reader.Read();
                if (!EsObjetivoElFrente()) return salidas; // cambió bajo los pies: mejor nada que ajeno

                // TODO lo accionable entra al mapa — también los botones de EJECUCIÓN (Nuevo,
                // Cortar, Pegar…), que son la mitad del valor del grafo: sin ellos el asistente
                // llega a cualquier sitio y no puede hacer nada al llegar. La distinción no es
                // qué se registra sino qué se CRUZA: durante el mapeo, solo navegación segura.
                // Texto e imagen se quedan fuera: no son puertas, son decoración con nombre.
                var seguros = _reader.Elements
                    .Where(e => !e.ControlType.Equals("text", StringComparison.OrdinalIgnoreCase)
                             && !e.ControlType.Equals("image", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // LA COLUMNA IZQUIERDA PRIMERO, de arriba abajo, y después el contenido, también de
                // arriba abajo. Es el plan que pidió el usuario: recorrer las secciones del panel
                // una por una, en el orden en que se ven. Dentro de una sección el panel casi
                // entero ya se conoce desde la raíz —el filtro de navegación global lo salta— así
                // que en la práctica ahí manda el contenido. Seguir la pantalla como la lee una
                // persona hace el recorrido reproducible: si falta algo, se ve dónde.
                foreach (var el in seguros
                    .OrderBy(e => e.ControlType.Equals("treeitem", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(e => e.Bounds.Top)
                    .ThenBy(e => e.Bounds.Left))
                {
                    if (salidas.Any(s => s.Item1.Equals(el.Label, StringComparison.OrdinalIgnoreCase))) continue;

                    // Los archivos entran en el mapa como puertas, pero NO se cruzan: abrir una foto
                    // no explora el explorador, lo abandona y arrastra el recorrido a otra app.
                    // Si sabemos la carpeta abierta, el disco resuelve la duda sin margen de error;
                    // si no, se cae a la heurística (ItemType / extensión), que es peor pero es algo.
                    bool esLista = el.ControlType.Equals("listitem", StringComparison.OrdinalIgnoreCase);
                    // CRUZABLE = navegación segura Y (si es lista) carpeta de verdad. Lo que no es
                    // cruzable igual se registra: queda como puerta, con su clase, para ejecución.
                    bool entrable = SafeToClick.Auto(el.Label, el.ControlType, out _)
                        && (!esLista || (carpeta.Length > 0
                            ? System.IO.Directory.Exists(System.IO.Path.Combine(carpeta, el.Label))
                            : SafeToClick.EsContenedor(el.Label, el.ItemType)));

                    // El SELECTOR se calcula ahora, mientras el elemento está vivo, y es lo único
                    // que se guarda: sobrevive a que la lista se repinte, la referencia no.
                    string sel = "", etiqueta = el.Label, tipo = el.ControlType;
                    string[] alts = Array.Empty<string>();
                    try
                    {
                        var (l, t, sels) = UiaSurface.DescribeElement(el.Native);
                        var utiles = sels.Where(EsSelectorUtil).ToArray();
                        if (utiles.Length > 0) { sel = utiles[0]; alts = utiles.Skip(1).ToArray(); }
                        if (l.Length > 0) etiqueta = l;
                        if (t.Length > 0) tipo = t;
                    }
                    catch { }
                    if (sel.Length == 0) continue;   // sin identidad no hay nada que recorrer

                    salidas.Add((etiqueta, tipo, sel, alts, entrable, U.Graph.Surfaces.UiaSurface.GrupoDe(el.Native)));
                }
            }
            catch { }
            return salidas.Take(30).ToList();
        }, 12000, new List<(string, string, string, string[], bool, string)>(), ct);
    }

    /// <summary>
    /// Pulsa por SELECTOR y, si la pantalla cambia, aprende la arista. Devuelve el destino o "".
    ///
    /// Recibe el selector y no el elemento vivo a propósito. Guardar la referencia de UIA y usarla
    /// después no funciona: al entrar en una carpeta y volver, la lista se repinta y todas las
    /// referencias capturadas antes quedan muertas. El síntoma era engañoso —parecía que el
    /// recorrido «solo entraba en la primera carpeta»— cuando en realidad entraba en la primera y
    /// las demás fallaban en silencio, una a una (2026-08-01). El selector, en cambio, se resuelve
    /// de nuevo cada vez contra la pantalla que hay AHORA.
    /// </summary>
    private async Task<string> PulsarYAprenderAsync(
        string desde, string etiqueta, string ct_, string selector, string[] alternativas,
        CancellationToken ct)
    {
        if (selector.Length == 0) return "";

        // La acción depende de DÓNDE vive el elemento, no de qué es. En el panel de navegación un
        // clic navega; en la lista de archivos un clic solo selecciona y hace falta el doble. Usar
        // siempre clic dejaba el recorrido pegado al panel izquierdo, sin entrar nunca a una
        // subcarpeta (medido el 2026-07-31: 12 pantallas, todas de primer nivel).
        string accion = ct_.Equals("listitem", StringComparison.OrdinalIgnoreCase) ? "doubleclick" : "click";
        var paso = new PlanStep { StepOrder = 1, ActionType = accion, Selector = selector, Label = etiqueta };
        Progress?.Invoke($"pulsando «{etiqueta}»", _nodos, _aristas);

        bool ok = await Task.Run(() => _ejecutor.Execute(paso, out _), ct);
        if (!ok) return "";

        // ¿El clic abrió algo cruzado? Se comprueba tras CADA acción, no solo al entrar en una
        // pantalla: en Configuración, «Cambiar nombre» abre un diálogo del sistema y el recorrido
        // seguía pulsando detrás durante toda la corrida, aprendiendo caminos que nadie hizo
        // (2026-08-03). El primer clic que levanta una barrera tiene que parar la corrida entera.
        if (Interrupcion.Hay())
        {
            string aviso = $"«{etiqueta}» abrió algo que bloquea: {Interrupcion.Describir().Replace("\n", " ")}";
            LogBus.Log("crawler", "PARADA · " + aviso);
            throw new InvalidOperationException(aviso);
        }

        string llegue = await EsperarCambioAsync(desde, 4000, ct);

        // RED DE SEGURIDAD, sobre el DESTINO y no sobre el reloj. Comprobar el primer plano justo
        // tras el clic no servía: una app tarda en arrancar, así que explorer seguía delante, la
        // comprobación pasaba, y la espera posterior acababa capturando Photos como destino bueno
        // (2026-07-31: acabó mapeando Photos.exe con la red marcando 0). Mirar a qué app pertenece
        // el sitio al que se llegó no depende de cuánto tarde nada.
        if (llegue.Length > 0 && !EsDelObjetivo(llegue))
        {
            LogBus.Log("crawler", $"«{etiqueta}» abrió otra aplicación ('{Corto(llegue)}'); se vuelve sin aprender");
            Actions.Gestures.NavigateBack();
            await EnfocarObjetivoAsync(ct);
            return "";
        }
        if (llegue.Length == 0)
        {
            // Se pulsó y no cambió nada identificable. Queda registrado: la diferencia entre «no
            // lleva a ninguna parte» y «lleva pero no supe leer dónde» es justo lo que hay que
            // vigilar para saber si el mapa tiene agujeros.
            LogBus.Log("crawler", $"«{etiqueta}» no llevó a ninguna pantalla identificable");
            return "";
        }

        _map.LearnTraversal(desde, llegue, selector, alternativas, etiqueta, ct_, accion);
        // Este selector ya tiene destino: desde cualquier otra pantalla, su arista se DEDUCE al
        // registrar la puerta y no hace falta volver a pulsarlo. Sin esto, «Saved Searches» —que
        // no era visible en la raíz y por tanto no contaba como navegación conocida— se pulsaba en
        // TODAS las pantallas, con su ida y su vuelta: dos navegaciones desperdiciadas por nodo
        // (2026-08-01). Solo el cromo se deduce; el contenido no, y por eso se excluye aquí igual
        // que en el mapa: dos carpetas pueden tener cada una su «readme.txt».
        if (!ct_.Equals("listitem", StringComparison.OrdinalIgnoreCase))
            _destinosSabidos.Add(selector);
        else if (!string.Equals(desde, llegue, StringComparison.OrdinalIgnoreCase) && HayBotonSubir())
        {
            // La SUBIDA al padre, que es estructural: desde una carpeta «Subir» lleva siempre a la
            // que la contiene, se haya llegado como se haya llegado. Aprenderla aquí es lo que
            // hace el grafo recorrible en los dos sentidos sin depender del historial.
            _map.LearnTraversal(llegue, desde, "uia:aid=upButton;ct=Button",
                Array.Empty<string>(), "Subir un nivel", "Button", "click");
            _aristas++;
            _learned.Add((llegue, desde, "Subir un nivel"));
            EdgeLearned?.Invoke(llegue, desde, "Subir un nivel");
        }
        _aristas++;
        _learned.Add((desde, llegue, etiqueta));
        // En caliente, para que el grafo se dibuje mientras se construye y no al terminar: ver
        // aparecer cada arista es lo que convierte el mapeo en algo observable en vez de una espera.
        EdgeLearned?.Invoke(desde, llegue, etiqueta);
        LogBus.Log("crawler", $"aprendido: «{etiqueta}» lleva de '{Corto(desde)}' a '{Corto(llegue)}'");
        Progress?.Invoke($"aprendido «{etiqueta}»", _nodos, _aristas);
        return llegue;
    }

    /// <summary>
    /// Volver al nodo anterior. Alt+Izquierda primero —gesto, no botón, así no depende de acertar
    /// la etiqueta de una flecha—; y si no basta, se REANCLA volviendo a la raíz por su propia
    /// arista aprendida.
    ///
    /// El reancle existe porque en la primera corrida (2026-07-31) el historial del explorador no
    /// devolvía al estado esperado y se abandonaban 4 ramas seguidas: de 4 pantallas recorridas,
    /// la mitad del recorrido se perdió en regresos fallidos. Abandonar era lo correcto —seguir
    /// desde un sitio desconocido produce aristas que describen un camino que nadie hizo— pero
    /// abandonar TODO era caro cuando basta con recolocarse en un punto conocido.
    /// </summary>
    private async Task<bool> VolverAsync(string esperado, CancellationToken ct)
    {
        // 1) El botón «Atrás» POR IDENTIDAD. Su AutomationId es estable y no depende del idioma
        //    —aid='backButton'—, así que es el camino más fiable y además VERIFICABLE: si no
        //    llegamos, lo sabemos. Va antes que el gesto porque Alt+Izquierda se lo lleva quien
        //    tenga el foco, y falló las 4 veces que se intentó (2026-07-31).
        var atras = new PlanStep
        {
            StepOrder = 1, ActionType = "click",
            Selector = "uia:aid=backButton;ct=Button", Label = "Atrás",
        };
        // «Atrás» se USA para volver pero NO se aprende como arista, y esto costó entenderlo:
        // depende del HISTORIAL, no de la pantalla. Aprender «win-x64 --Atrás--> documentos» fue
        // cierto una vez, en aquel recorrido, y falso en cuanto se llegó a win-x64 por otro camino:
        // al pedir esa ruta por MCP el tramo falló, con el botón ahí delante (2026-08-02). Una
        // arista describe una propiedad del sitio; un gesto de historial no lo es. La vuelta que
        // SÍ es mapa es el panel de navegación, que está en todas las pantallas y siempre lleva
        // al mismo lugar.
        if (await Task.Run(() => _ejecutor.Execute(atras, out _), ct)
            && (await EsperarLlegadaAsync(esperado, 2500, ct)).Length > 0)
            return true;

        // 2) El gesto, por si la barra no está (otras apps, o el explorador sin barra).
        Actions.Gestures.NavigateBack();
        if ((await EsperarLlegadaAsync(esperado, 2500, ct)).Length > 0) return true;

        // 3) UN ATAJO por el cromo persistente. Si el destino tiene una entrada en el panel lateral
        //    —o en cualquier elemento que esté en todas las pantallas—, se pulsa y se llega directo,
        //    sin importar dónde estemos ahora. Es lo que haría una persona: en vez de deshacer el
        //    camino, ir al panel y entrar otra vez.
        var atajo = _map.AtajoHacia(esperado);
        if (atajo != null)
        {
            LogBus.Log("crawler", $"atajo por cromo: «{atajo.Info.Label}» lleva a '{Corto(esperado)}'");
            var salto = new PlanStep
            {
                StepOrder = 1, ActionType = atajo.Info.ActionType,
                Selector = atajo.Info.Selector, Label = atajo.Info.Label,
            };
            if (await Task.Run(() => _ejecutor.Execute(salto, out _), ct)
                && (await EsperarLlegadaAsync(esperado, 2500, ct)).Length > 0)
                return true;
        }

        // 3) Por el mapa. Si conocemos una arista que lleve de donde estamos al destino, se toma
        //    — es exactamente lo que el grafo existe para responder.
        string aqui = _where()?.Id ?? "";
        if (aqui.Length == 0) return false;
        var vuelta = _map.ExitsFrom(aqui).FirstOrDefault(h =>
            h.Info.Selector.Length > 0 &&
            string.Equals(h.To, esperado, StringComparison.OrdinalIgnoreCase));
        if (vuelta == null) return false;

        LogBus.Log("crawler", $"regreso por el mapa: «{vuelta.Info.Label}» desde '{Corto(aqui)}'");
        var paso = new PlanStep
        {
            StepOrder = 1, ActionType = vuelta.Info.ActionType,
            Selector = vuelta.Info.Selector, Label = vuelta.Info.Label,
        };
        if (!await Task.Run(() => _ejecutor.Execute(paso, out _), ct)) return false;
        return (await EsperarLlegadaAsync(esperado, 2500, ct)).Length > 0;
    }

    /// <summary>
    /// Espera el cambio de superficie y a que el destino se ESTABILICE. Aprender «en cuanto
    /// cambia» capturaba el estado transitorio de la navegación —el título pasa un instante por
    /// el nombre del panel— y así nacieron aristas hacia un nodo fantasma (10 de 45 en la corrida
    /// del 2026-07-31). Un destino solo cuenta cuando se lee IGUAL dos veces seguidas y no es
    /// cromo sin identidad.
    /// </summary>
    private async Task<string> EsperarCambioAsync(string desde, int msMax, CancellationToken ct)
    {
        // Muestreo fino: la superficie se calcula en el acto, así que sondear cada 60 ms confirma
        // la transición en cuanto ocurre en vez de en el siguiente latido. Con el valor cacheado
        // esto no habría servido de nada —se leería el mismo dato viejo— pero con lectura directa
        // es la diferencia entre ~1,6 s y ~120 ms de reloj por arista (2026-08-02).
        string candidato = "";
        for (int i = 0; i < msMax / 60; i++)
        {
            await Task.Delay(60, ct);
            string ahora = _where()?.Id ?? "";

            // Lecturas que no dicen nada: vacío, seguimos donde estábamos, o cromo sin identidad.
            // Se SALTAN sin borrar el candidato. Borrarlo era el error: durante una navegación se
            // intercalan estados intermedios, así que exigir dos lecturas buenas SEGUIDAS hacía que
            // muchos destinos reales no se confirmaran nunca. El resultado era navegar a una
            // carpeta y no aprender la arista: el recorrido pasaba por sitios que el grafo no
            // llegaba a mostrar (2026-07-31, «motorola edge 60 fusion» entre otras).
            if (ahora.Length == 0
                || string.Equals(ahora, desde, StringComparison.OrdinalIgnoreCase)
                || ahora.EndsWith("/ventana", StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.Equals(ahora, candidato, StringComparison.OrdinalIgnoreCase))
                return ahora;      // visto dos veces: esto ya es el destino, no el tránsito
            candidato = ahora;
        }

        // Se agotó el tiempo con un candidato visto una sola vez. Es mejor dato que ninguno: la
        // pantalla cambió de verdad, y perderlo deja agujeros en el mapa de sitios donde SÍ estuvo.
        if (candidato.Length > 0)
            LogBus.Log("crawler", $"destino '{Corto(candidato)}' visto una sola vez; se acepta");
        return candidato;
    }

    private async Task<string> EsperarLlegadaAsync(string esperado, int msMax, CancellationToken ct)
    {
        for (int i = 0; i < msMax / 60; i++)
        {
            await Task.Delay(60, ct);
            string ahora = _where()?.Id ?? "";
            if (string.Equals(ahora, esperado, StringComparison.OrdinalIgnoreCase)) return ahora;
        }
        return "";
    }

    private static string Corto(string id)
    {
        int i = id.IndexOf("://", StringComparison.Ordinal);
        return i >= 0 ? id[(i + 3)..] : id;
    }
}
