using System.IO;
using System.Text.Json;
using U.Graph;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Navigation;

/// <summary>
/// El MAPA BASE del computador: el grafo de superficies que Ü ha VISTO, construido pasivamente
/// con el caudal del <see cref="Uia.SurfaceLocator"/> mientras el usuario usa su máquina. Es
/// "la tierra" — gris en el mapa — sobre la que los workflows son rutas intencionales (verdes).
///
/// La distinción gris/verde no es de color sino de CAPACIDAD, y conviene no olvidarla: este
/// mapa sabe QUE dos pantallas conectan (lo observó); un workflow sabe CÓMO transitar (tiene el
/// paso ejecutable). Por eso el terreno se acumula gratis y las rutas hay que enseñarlas.
///
/// Vive en DISCO LOCAL (%LOCALAPPDATA%\U\surface-map.json, vía UserPaths), no en Neo4j, a
/// propósito: es el registro de cada pantalla que el usuario pisa, y eso no sale de su máquina
/// sin una decisión explícita que hoy nadie ha tomado. Sincronizarlo al backend será una
/// decisión de producto, no un efecto colateral.
/// </summary>
public sealed class SurfaceMap
{
    /// <summary>
    /// Cuánto hay que QUEDARSE en una superficie para que cuente como visitada. Sin esto, un
    /// alt-tab que atraviesa tres ventanas en un segundo sembraría nodos y caminos que el
    /// usuario jamás usó: el mapa debe registrar dónde SE ESTÁ, no por dónde se pasó volando.
    /// </summary>
    private const int MinDwellMs = 1200;

    /// <summary>Red de seguridad, no límite operativo: un mapa real ronda los cientos.</summary>
    private const int MaxNodes = 2000;

    public sealed class NodeInfo
    {
        public int Visits { get; set; }
        public DateTime LastSeen { get; set; }
    }

    /// <summary>
    /// Una transición observada. <see cref="Count"/> sola solo dice que dos pantallas conectan;
    /// la ACCIÓN es lo que la hace recorrible — la diferencia entre «conozco este lugar» y «sé
    /// cómo llegar», y el requisito para que el asistente navegue sin workflow grabado.
    ///
    /// Puede quedar vacía: transiciones por teclado, por otra app que se pone delante, o clics
    /// que no resolvieron. Una arista sin acción sigue siendo información honesta —conectividad
    /// sin instrucciones— y por eso se guarda igual en vez de descartarse.
    /// </summary>
    public sealed class EdgeInfo
    {
        public int Count { get; set; }
        public string Selector { get; set; } = "";
        public string Label { get; set; } = "";
        public string ControlType { get; set; } = "";
        /// <summary>Selectores de respaldo (Name, Path…), como en un paso grabado: si el id
        /// principal no resuelve mañana, la arista sigue teniendo con qué intentarlo.</summary>
        public string[] Alternatives { get; set; } = Array.Empty<string>();
        /// <summary>Posición relativa a la ventana. ÚLTIMO respaldo, jamás identidad — el mismo
        /// papel que cumple en la grabación de workflows.</summary>
        public string ClickPos { get; set; } = "";

        /// <summary>
        /// La acción la ejecutó el PROPIO sistema (explorador del grafo), no se infirió viendo al
        /// usuario. Importa porque son dos niveles de certeza distintos: la observada llegó al 78%
        /// medido; la explorada es cierta por construcción — no hubo atribución que adivinar.
        /// </summary>
        public bool Explored { get; set; }

        /// <summary>
        /// Cómo se recorre: «click» o «doubleclick». Guardarlo no es un detalle — una carpeta de la
        /// lista solo se abre con doble clic, y una arista que dijera «clic» ahí prometería un
        /// camino que al ejecutarse solo selecciona. La acción es parte de la ruta, no del momento.
        /// </summary>
        public string ActionType { get; set; } = "click";

        /// <summary>«navegacion» (lleva a otra pantalla) o «accion» (hace algo aquí: Nuevo,
        /// Cortar, Pegar…). Vacío en datos viejos = navegación. El mapeo solo cruza navegación;
        /// la ejecución usa las de acción a propósito.</summary>
        public string Kind { get; set; } = "";
    }

    private readonly Dictionary<string, NodeInfo> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EdgeInfo> _edges = new(StringComparer.Ordinal); // "from\nto"

    private string _pendingId = "";
    private DateTime _pendingSince = DateTime.MinValue;
    private string _lastCommitted = "";

    /// <summary>
    /// El clic que SALE de <see cref="_lastCommitted"/>, guardado al confirmarlo y consumido al
    /// confirmar el nodo siguiente. Este desfase de un paso no es un detalle de implementación:
    /// una arista A→B se crea cuando se confirma B, y en ese instante el último clic ya es el que
    /// sale de B. Leerlo ahí desplazaba cada acción una arista — «Descargas» acababa colgada de la
    /// arista que llevaba a escritorio, mientras la que de verdad se recorre pulsando «Descargas»
    /// quedaba con la acción de otra. Medido el 2026-07-29 sobre 15 transiciones del explorador.
    /// </summary>
    private ClickWatcher.Click? _actionLeavingLast;

    /// <summary>
    /// El último clic que ya se guardó, para no reutilizarlo. Un clic explica UNA transición.
    ///
    /// Sin esto, una transición cuyo clic no resolvió heredaba el clic anterior y quedaba con una
    /// instrucción falsa. Se midió el 2026-07-29 con el botón «Mostrar escritorio» de la esquina de
    /// la barra de tareas: al minimizar todo, el primer plano cambia al instante y el centinela
    /// descarta la resolución —correctamente—, pero el clic de 4 segundos antes seguía siendo «el
    /// último» y pasaba la ventana de 6 s. Resultado: «Imágenes» aparecía llevando al escritorio.
    ///
    /// Que un clic no resuelva es información legítima: significa «no sé cómo se hace esto». Lo que
    /// no es legítimo es rellenar ese hueco con la respuesta de otra pregunta.
    /// </summary>
    private ClickWatcher.Click? _lastStashed;

    /// <summary>Clics resueltos al confirmar el nodo anterior. La diferencia contra el contador
    /// actual dice cuántos clics explica esta arista: uno es correcto, más es ambiguo.</summary>
    private int _clicksAtLastCommit = -1;

    private int _dirty;

    /// <summary>Cambia cuando el mapa aprende algo; la visualización lo usa para saber si redibujar.</summary>
    public int Version { get; private set; }

    private static string Path =>
        System.IO.Path.Combine(UserPaths.Local, "U", "surface-map.json");

    // ── Observación ──────────────────────────────────────────────────────────

    /// <summary>
    /// Alimenta el mapa con la superficie actual. Se llama en cada Changed del locator; el
    /// nodo se confirma cuando la superficie es REEMPLAZADA tras haber durado el mínimo.
    /// </summary>
    /// <summary>
    /// De dónde sale la acción de cada arista. Se inyecta en vez de construirse aquí para que el
    /// mapa siga siendo comprobable sin enganchar el ratón de la máquina.
    /// </summary>
    public ClickWatcher? Clicks { get; set; }

    public void Observe(string surfaceId)
    {
        string id = (surfaceId ?? "").Trim().TrimEnd('/');
        if (id.Length == 0 || string.Equals(id, _pendingId, StringComparison.OrdinalIgnoreCase)) return;

        // La UI de Ü no es terreno: la carita, el mapa y el badge son el observador, y un
        // observador que se registra a sí mismo llena el grafo de bucles hacia ninguna parte.
        // (Misma regla que ya rige en los workflows: la UI propia nunca es parte de uno.)
        //
        // Tampoco lo es el CROMO TRANSITORIO del shell, y se trata como pasarela — se ignora y el
        // viaje real queda A→B directo. Identificado el 2026-07-30 con dos ventanas del explorador
        // y la barra de tareas: el flyout de miniaturas es una ventana de explorer SIN TÍTULO
        // (slug «ventana», 4 visitas fundidas en un nodo cajón de sastre) y la jump list vive en
        // ShellExperienceHost. Ninguno es un lugar al que se pueda "volver": son el pasillo.
        string origen = SurfacePlace.OriginOf(id);
        bool esPropia = origen.EndsWith("//u.exe", StringComparison.OrdinalIgnoreCase)
            || origen.Contains("shellexperiencehost", StringComparison.OrdinalIgnoreCase)
            || id.EndsWith("/ventana", StringComparison.OrdinalIgnoreCase);

        var now = DateTime.UtcNow;
        if (_pendingId.Length > 0 && (now - _pendingSince).TotalMilliseconds >= MinDwellMs)
            Commit(_pendingId, now);

        _pendingId = esPropia ? "" : id;
        _pendingSince = now;
    }

    private void Commit(string id, DateTime when)
    {
        if (!_nodes.TryGetValue(id, out var n))
        {
            if (_nodes.Count >= MaxNodes) return;
            n = new NodeInfo();
            _nodes[id] = n;
        }
        n.Visits++;
        n.LastSeen = when;

        if (_lastCommitted.Length > 0 && !string.Equals(_lastCommitted, id, StringComparison.OrdinalIgnoreCase))
        {
            string k = _lastCommitted + "\n" + id;
            if (!_edges.TryGetValue(k, out var e)) { e = new EdgeInfo(); _edges[k] = e; }
            e.Count++;

            // La acción de la arista es el clic que salió del nodo ORIGEN, guardado cuando ese
            // nodo se confirmó. La ventana de 6 s es la misma que ya usa la grabación de SAP para
            // el mismo problema — un clic de hace mucho no disparó esta transición, y atribuírselo
            // inventaría una instrucción falsa. Preferimos una arista sin acción a una que miente
            // sobre cómo recorrerla.
            var clic = _actionLeavingLast;

            // MULTI-SALTO: si entre la confirmación del origen y la de este destino hubo MÁS DE UN
            // clic, la arista colapsó saltos que no duraron el mínimo y el clic guardado explica
            // solo el primero. Medido dos veces: «Imágenes» como acción de documentos→música
            // (pasando de largo por imágenes), y las cabeceras «Nombre» —ordenar no cambia de
            // pantalla, así que ordenar-y-navegar son dos clics entre confirmaciones—.
            //
            // Convive con la comprobación del índice de down y NO la sustituye: aquella detecta
            // que el clic resuelto no fue el último gesto; esta, que un solo clic no explica el
            // viaje entero. Fusionarlas fue mi error y devolvió los dos fallos a la vez.
            //
            // EXCEPCIÓN: el navegador del sistema. Usar la barra de tareas son DOS clics —el icono
            // abre el selector, la miniatura elige destino— y esta guarda los descartaba a todos.
            // Es un solo GESTO en dos tiempos, y el que manda es el segundo, que es justo el que
            // queda guardado. Restaurar esta guarda mató la barra de tareas el mismo día que se
            // añadió su soporte: dos arreglos correctos que se anulaban entre sí.
            if (clic != null && !clic.IsSystemNavigator && _clicksAtLastCommit >= 0
                && (Clicks?.Count ?? 0) - _clicksAtLastCommit > 1) clic = null;

            // EL CLIC DEBE HABER OCURRIDO EN LA APP DE LA QUE SALE la arista. Sin esto, un clic en
            // el chat de Claude acabó como "acción" de una transición de la barra de tareas
            // (2026-07-30): resolvió, era el último, y aun así no tenía nada que ver.
            //
            // SALVO EL NAVEGADOR DEL SISTEMA. La barra de tareas es explorer.exe, así que esta
            // correa la habría descartado SIEMPRE — y con ella la clase de arista más valiosa de
            // un grafo cross-app: «desde donde estés, ve a X». La barra no es un lugar, es un
            // verbo: como nodo se ignora, como acción es legítima venga de donde venga.
            if (clic != null && !clic.IsSystemNavigator && clic.Process.Length > 0
                && _lastCommitted.StartsWith("uia://", StringComparison.OrdinalIgnoreCase))
            {
                string exe = SurfacePlace.OriginOf(_lastCommitted)
                    .Replace("uia://", "").Replace(".exe", "");
                if (!string.Equals(exe, clic.Process, StringComparison.OrdinalIgnoreCase)) clic = null;
            }

            if (clic != null && clic.Selector.Length > 0
                && (when - clic.When).TotalSeconds is >= 0 and <= 6)
            {
                // Solo se rellena si estaba vacía: la primera vez que se aprende cómo pasar por
                // aquí, se conserva. Sobreescribir en cada paso haría que la acción bailara según
                // el último camino, y una instrucción que cambia sola no es una instrucción.
                if (e.Selector.Length == 0)
                {
                    e.Selector = clic.Selector;
                    e.Alternatives = clic.Alternatives;
                    e.ClickPos = clic.ClickPos;
                    e.Label = clic.Label;
                    e.ControlType = clic.ControlType;
                    LogBus.Log("mapa", $"aprendido: «{clic.Label}» lleva de '{ShortId(_lastCommitted)}' a '{ShortId(id)}'");
                }
            }
        }
        _lastCommitted = id;

        // El clic de AHORA es el que sale de este nodo: se guarda para la arista que se cerrará
        // cuando se confirme el siguiente (ver _actionLeavingLast). Pero solo si es NUEVO: si es el
        // mismo que ya usamos, es que el clic de esta transición no resolvió, y entonces lo honesto
        // es no saber (ver _lastStashed).
        var ahora = Clicks?.Last;
        // ¿El último GESTO es el que está resuelto? Si después del clic resuelto hubo otro down
        // que no resolvió, el resuelto es de otra transición y atribuirlo mentiría — el fallo
        // «Nombre»/párrafo-del-chat, medido dos veces el 2026-07-30. El índice de down responde
        // eso con precisión; ninguna ventana de tiempo lo hace.
        if (ahora != null && Clicks != null && Clicks.Downs != ahora.DownIndex) ahora = null;
        _actionLeavingLast = ReferenceEquals(ahora, _lastStashed) ? null : ahora;
        _lastStashed = ahora;
        _clicksAtLastCommit = Clicks?.Count ?? 0;
        Version++;

        if (++_dirty >= 20) Save(); // persistencia periódica; el cierre hace la final
    }

    // ── Lectura (para la visualización) ──────────────────────────────────────

    public IReadOnlyDictionary<string, NodeInfo> Nodes => _nodes;

    public IEnumerable<(string From, string To, EdgeInfo Info)> Edges()
    {
        foreach (var kv in _edges)
        {
            int cut = kv.Key.IndexOf('\n');
            yield return (kv.Key[..cut], kv.Key[(cut + 1)..], kv.Value);
        }
    }

    /// <summary>Cuántas aristas saben ya CÓMO recorrerse. Es la medida de madurez del mapa.</summary>
    public int EdgesWithAction => _edges.Values.Count(e => e.Selector.Length > 0);

    /// <summary>
    /// Aprende una arista RECORRIDA por el propio sistema (explorador del grafo). A diferencia de
    /// las observadas, aquí no hay nada que adivinar: la acción se ejecutó y la transición se vio
    /// ocurrir. Por eso sobreescribe cualquier acción observada que hubiera — certeza por
    /// construcción gana a inferencia al 78% — y por eso se guarda a disco al instante: una arista
    /// explorada costó un clic real del usuario y no puede perderse por un cierre brusco.
    /// </summary>
    /// <summary>
    /// Marca de destino desconocido. Una PUERTA es una salida que se ha VISTO pero no cruzado:
    /// sabemos que el botón está ahí y cómo pulsarlo, no a dónde lleva.
    /// </summary>
    public const string MarcaPuerta = "?";

    /// <summary>El «destino» de una puerta sin cruzar: único por selector, para que en una misma
    /// pantalla quepan todas las puertas que tenga y no se pisen entre sí.</summary>
    private static string DestinoPuerta(string selector) => MarcaPuerta + selector;

    /// <summary>¿Este destino es en realidad una puerta cuyo otro lado no conocemos?</summary>
    public static bool EsPuerta(string to) => to.StartsWith(MarcaPuerta, StringComparison.Ordinal);

    /// <summary>
    /// Registra todas las salidas VISIBLES de una pantalla, se vayan a cruzar o no.
    ///
    /// Antes el grafo solo contenía lo que el sistema había pulsado, y eso lo dejaba con forma de
    /// estrella: como las salidas del panel de navegación solo se aprendían desde la raíz, cualquier
    /// otra pantalla aparecía sin salidas propias y todo el mapa colgaba del punto de arranque
    /// (2026-07-31). Pero esas puertas EXISTEN en todas las pantallas, y saber que existen ya es
    /// información útil: el asistente puede consultar por MCP qué hay disponible desde donde está
    /// sin tener que ir hasta allí a comprobarlo.
    /// </summary>
    public void ObserveExits(string from,
        IEnumerable<(string Label, string ControlType, string Selector, string[] Alternatives)> salidas)
    {
        string f = Norm(from);
        if (f.Length == 0) return;
        if (!_nodes.ContainsKey(f) && _nodes.Count < MaxNodes) _nodes[f] = new NodeInfo();

        foreach (var s in salidas)
        {
            if (s.Selector.Length == 0) continue;

            // Si ya conocemos una salida REAL con este selector desde aquí, no se toca: lo recorrido
            // manda sobre lo observado.
            if (Edges().Any(e => string.Equals(e.From, f, StringComparison.OrdinalIgnoreCase)
                              && string.Equals(e.Info.Selector, s.Selector, StringComparison.Ordinal)
                              && !EsPuerta(e.To)))
                continue;

            // El destino puede deducirse: si este MISMO selector ya llevó a algún sitio desde otra
            // pantalla, lleva al mismo desde aquí. Vale para el cromo de navegación —el panel
            // izquierdo es idéntico en todas las carpetas— y NO para el contenido, porque dos
            // carpetas distintas pueden tener cada una su «readme.txt» y no son el mismo destino.
            string deducido = EsContenido(s.ControlType) ? "" : DestinoConocidoDe(s.Selector);
            string destino = deducido.Length > 0 ? deducido : DestinoPuerta(s.Selector);
            if (string.Equals(destino, f, StringComparison.OrdinalIgnoreCase)) continue; // no lleva a sí misma

            string k = f + "\n" + destino;
            if (_edges.ContainsKey(k)) continue;
            _edges[k] = new EdgeInfo
            {
                Selector = s.Selector,
                Alternatives = s.Alternatives,
                Label = s.Label,
                ControlType = s.ControlType,
                ActionType = EsContenido(s.ControlType) ? "doubleclick" : "click",
                Kind = SafeToClick.Clasificar(s.Label, s.ControlType),
                Explored = deducido.Length > 0,
            };
        }
        Save();
    }

    /// <summary>
    /// En cuántas pantallas DISTINTAS se ha visto esta misma puerta.
    ///
    /// No hace falta guardar nada aparte: como cada pantalla registra todas sus salidas, contar
    /// los orígenes distintos de un selector ya dice cuánto se repite. Sobrevive a guardar y
    /// cargar el mapa porque se deriva de él.
    /// </summary>
    public int Ubicuidad(string selector) =>
        Edges().Where(e => string.Equals(e.Info.Selector, selector, StringComparison.Ordinal))
               .Select(e => e.From)
               .Distinct(StringComparer.OrdinalIgnoreCase)
               .Count();

    /// <summary>
    /// ¿Es un elemento PERSISTENTE de la app —el panel lateral, una barra, un menú— que sigue ahí
    /// aunque se cambie de pantalla?
    ///
    /// Se detecta por repetición, no por una lista de nombres: si la misma puerta aparece en tres
    /// pantallas distintas, es parte del marco de la app y no de ninguna de ellas. Así vale para
    /// cualquier app sin conocerla de antemano. El contenido queda fuera por definición: dos
    /// carpetas pueden tener cada una su «readme.txt» y no son la misma puerta.
    /// </summary>
    public bool EsCromoGlobal(string selector, string controlType = "")
    {
        if (selector.Length == 0) return false;
        // El tipo va DENTRO del selector; si no lo pasan, se lee de ahí. No hacerlo dejaba fuera
        // la exclusión del contenido justo donde más falta hacía: en el explorador, el
        // AutomationId de una fila es su ÍNDICE, así que «uia:aid=1;ct=ListItem» existe en todas
        // las carpetas, la ubicuidad lo daba por marco de la app y sus subcarpetas no se
        // exploraban nunca (2026-08-01).
        if (EsRelativo(selector)) return false;   // está en todas partes pero NO lleva al mismo sitio
        string ct = controlType.Length > 0 ? controlType : TipoDelSelector(selector);
        return !EsContenido(ct) && Ubicuidad(selector) >= 3;
    }

    private static string TipoDelSelector(string selector)
    {
        var m = System.Text.RegularExpressions.Regex.Match(selector, @"ct=([A-Za-z]+)");
        return m.Success ? m.Groups[1].Value : "";
    }

    /// <summary>
    /// Un ATAJO hasta <paramref name="destino"/> pulsable desde donde sea.
    ///
    /// Es la utilidad real de detectar lo persistente: un elemento que está en todas las pantallas
    /// sirve para llegar a la suya desde cualquiera de ellas, sin recorrer el camino. Convierte
    /// «no sé volver» en «pulso el panel y ya estoy» — que es como lo haría una persona.
    /// </summary>
    public Hop? AtajoHacia(string destino)
    {
        string d = Norm(destino);
        foreach (var (from, to, info) in Edges())
        {
            if (!string.Equals(to, d, StringComparison.OrdinalIgnoreCase)) continue;
            if (info.Selector.Length == 0 || !info.Explored) continue;
            if (EsCromoGlobal(info.Selector, info.ControlType)) return new Hop(from, to, info);
        }
        return null;
    }

    /// <summary>El contenido de una lista no es cromo: su nombre no identifica un destino global.</summary>
    private static bool EsContenido(string controlType) =>
        controlType.Equals("listitem", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Selectores cuyo destino es RELATIVO a la pantalla desde la que se pulsan.
    ///
    /// «Subir un nivel» tiene el mismo selector en todas partes (aid=upButton) pero lleva a un
    /// sitio distinto en cada una: al padre de donde estés. La deducción por selector —«el mismo
    /// botón lleva al mismo destino»— es cierta para el cromo global y FALSA para estos, y
    /// aplicarla los convirtió en una escalera: el recorrido subía de la carpeta de pruebas a
    /// Documentos, luego a felip, a Usuarios y a Disco local (C:), creyendo obedecer al mapa
    /// (2026-08-02). Se aprenden por pareja concreta (de aquí a su padre) y NUNCA se generalizan.
    /// </summary>
    private static bool EsRelativo(string selector) =>
        selector.Contains("upButton", StringComparison.OrdinalIgnoreCase)
        || selector.Contains("backButton", StringComparison.OrdinalIgnoreCase)
        || selector.Contains("forwardButton", StringComparison.OrdinalIgnoreCase);

    /// <summary>A dónde lleva este selector, si ya se recorrió desde cualquier pantalla.</summary>
    private string DestinoConocidoDe(string selector)
    {
        if (EsRelativo(selector)) return "";   // su destino depende de dónde estés: no se deduce
        foreach (var (_, to, info) in Edges())
            if (!EsPuerta(to) && to.Length > 0 && info.Explored
                && string.Equals(info.Selector, selector, StringComparison.Ordinal))
                return to;
        return "";
    }

    /// <summary>
    /// Al aprender un destino de verdad, todas las puertas con el MISMO selector —en cualquier
    /// pantalla— dejan de ser desconocidas. Es lo que convierte la estrella en una malla: descubrir
    /// una vez a dónde va «Documentos» resuelve ese botón en todas las carpetas donde se ha visto.
    /// </summary>
    private void ResolverPuertasIguales(string selector, string destino, string controlType)
    {
        if (EsContenido(controlType) || selector.Length == 0 || EsRelativo(selector)) return;

        var promover = _edges
            .Where(kv => kv.Key.EndsWith("\n" + DestinoPuerta(selector), StringComparison.Ordinal))
            .ToList();

        foreach (var kv in promover)
        {
            string desde = kv.Key[..kv.Key.IndexOf('\n')];
            if (string.Equals(desde, destino, StringComparison.OrdinalIgnoreCase)) { _edges.Remove(kv.Key); continue; }
            _edges.Remove(kv.Key);
            string k = desde + "\n" + destino;
            if (!_edges.ContainsKey(k)) { kv.Value.Explored = true; _edges[k] = kv.Value; }
        }
    }

    public void LearnTraversal(string from, string to, string selector, string[] alternatives,
        string label, string controlType, string actionType = "click")
    {
        string f = Norm(from), t = Norm(to);
        if (f.Length == 0 || t.Length == 0 || selector.Length == 0) return;

        // La puerta que acabamos de cruzar deja de ser una incógnita aquí y en todas partes.
        _edges.Remove(f + "\n" + DestinoPuerta(selector));
        ResolverPuertasIguales(selector, t, controlType);

        if (!_nodes.ContainsKey(t) && _nodes.Count < MaxNodes) _nodes[t] = new NodeInfo();
        if (_nodes.TryGetValue(t, out var n)) { n.Visits++; n.LastSeen = DateTime.UtcNow; }

        string k = f + "\n" + t;
        if (!_edges.TryGetValue(k, out var e)) { e = new EdgeInfo(); _edges[k] = e; }
        e.Count++;
        e.Selector = selector;
        e.Alternatives = alternatives;
        e.Label = label;
        e.ControlType = controlType;
        e.ActionType = actionType;
        e.Kind = SafeToClick.Clasificar(label, controlType);
        e.Explored = true;
        Version++;
        Save();
    }

    // ── Consulta y rutas (lo que el asistente ve por MCP) ────────────────────

    /// <summary>Un tramo de ruta: a dónde lleva y con qué acción. Sin acción = no recorrible.</summary>
    public sealed record Hop(string From, string To, EdgeInfo Info);

    /// <summary>
    /// La ruta más corta desde <paramref name="from"/> hasta <paramref name="to"/> usando SOLO
    /// aristas que saben cómo recorrerse.
    ///
    /// Exigir acción en cada tramo no es un detalle: una ruta con un hueco es una ruta que el
    /// asistente empezaría y no podría terminar, y prefiero devolver «no sé llegar» a dejarlo a
    /// mitad de camino en la máquina de alguien. Anchura primero porque el coste real de un tramo
    /// es un round-trip de UI: menos saltos es menos oportunidades de que la pantalla no esté.
    /// </summary>
    public List<Hop>? Route(string from, string to)
    {
        string origen = Norm(from), destino = Norm(to);
        if (origen.Length == 0 || destino.Length == 0) return null;
        if (string.Equals(origen, destino, StringComparison.OrdinalIgnoreCase)) return new List<Hop>();

        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { origen };
        var cola = new Queue<List<Hop>>();
        cola.Enqueue(new List<Hop>());

        while (cola.Count > 0)
        {
            var camino = cola.Dequeue();
            string actual = camino.Count == 0 ? origen : camino[^1].To;

            foreach (var (f, t, info) in Edges())
            {
                if (info.Selector.Length == 0) continue;              // sin acción no se recorre
                if (EsPuerta(t)) continue;                            // puerta sin cruzar: no sé a dónde da
                if (!string.Equals(f, actual, StringComparison.OrdinalIgnoreCase)) continue;
                if (!vistos.Add(t)) continue;

                var siguiente = new List<Hop>(camino) { new(f, t, info) };
                if (string.Equals(t, destino, StringComparison.OrdinalIgnoreCase)) return siguiente;
                if (siguiente.Count < 8) cola.Enqueue(siguiente);     // rutas absurdamente largas no son rutas
            }
        }
        return null;
    }

    /// <summary>Las salidas conocidas de una superficie, con acción o sin ella (se dice cuál es cuál).</summary>
    public List<Hop> ExitsFrom(string surface)
    {
        string s = Norm(surface);
        return Edges().Where(e => string.Equals(e.From, s, StringComparison.OrdinalIgnoreCase))
                      .Select(e => new Hop(e.From, e.To, e.Info))
                      .OrderByDescending(h => h.Info.Count)
                      .ToList();
    }

    private static string Norm(string id) => (id ?? "").Trim().TrimEnd('/');

    private static string ShortId(string id)
    {
        int i = id.IndexOf("://", StringComparison.Ordinal);
        return i >= 0 ? id[(i + 3)..] : id;
    }

    // ── Persistencia ─────────────────────────────────────────────────────────

    /// <summary>
    /// Versión del esquema. Sube cuando los datos guardados dejan de ser de fiar y hay que
    /// descartarlos, no solo cuando cambia su forma.
    ///
    /// v2: las acciones capturadas por la v1 eran FALSAS. Resolvían el elemento al soltar el
    /// botón, cuando la UI ya había cambiado, así que guardaban el texto de la pantalla siguiente
    /// («Pregunta lo que quieras» llevando a login.neo4j.com). Al subir a v2 se tiran TODAS las
    /// acciones y se conservan nodos, conteos y conectividad, que salen del locator y sí son
    /// fiables. Reaprender el cómo cuesta unos minutos de uso; arrastrar instrucciones inventadas
    /// costaría confiar en un grafo que miente.
    /// </summary>
    /// v3: las acciones de la v2 estaban DESPLAZADAS una arista (cada una guardaba la acción de la
    /// siguiente). La identidad era correcta, el destino no — que para navegar es igual de inútil.
    /// v4: un clic que no resolvía dejaba que la arista heredara el clic anterior. 19 de 24 acciones
    /// de la v3 eran correctas; las 5 restantes eran todas esa fuga.
    /// v5: las acciones pasan a ser pasos REALES — resueltos por la misma maquinaria del recorder
    /// (UiaSurface.DescribeElement), con selectores alternativos y posición de respaldo. Las de la
    /// v4 hablaban un formato inventado («uia:id=X») que el player no sabe ejecutar: se purgan.
    /// v6: tres vallas que faltaban — etiqueta obligatoria (salían «»), contenedores fuera
    /// (cabeceras y popup-host como "acciones") y el doble clic conserva la resolución del PRIMER
    /// down + guard multi-salto. Las acciones v5 mezclan buenas con esas tres plagas: se purgan.
    private const int SchemaVersion = 6;

    private sealed record Stored(
        Dictionary<string, NodeInfo> Nodes,
        Dictionary<string, EdgeInfo> Edges,
        int Version = 1);

    public static SurfaceMap Load()
    {
        var map = new SurfaceMap();
        try
        {
            if (File.Exists(Path))
            {
                var s = JsonSerializer.Deserialize<Stored>(File.ReadAllText(Path));
                if (s != null)
                {
                    foreach (var kv in s.Nodes) map._nodes[kv.Key] = kv.Value;
                    foreach (var kv in s.Edges) map._edges[kv.Key] = kv.Value;

                    if (s.Version < SchemaVersion)
                    {
                        int purgadas = 0;
                        foreach (var e in map._edges.Values)
                            if (e.Selector.Length > 0)
                            {
                                e.Selector = ""; e.Label = ""; e.ControlType = "";
                                e.Alternatives = Array.Empty<string>(); e.ClickPos = "";
                                purgadas++;
                            }
                        LogBus.Log("mapa", $"esquema v{s.Version}→v{SchemaVersion}: {purgadas} acción(es) "
                            + "descartadas por haberse capturado tarde (identidad no fiable). "
                            + "Nodos y conectividad intactos.");
                        map.Save();
                    }
                }
            }
        }
        catch (Exception e)
        {
            // Un mapa ilegible no puede impedir arrancar: se empieza de cero y se dice.
            LogBus.Log("mapa", $"surface-map.json ilegible, se reconstruye desde cero: {e.Message}");
        }
        return map;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(new Stored(_nodes, _edges, SchemaVersion)));
            _dirty = 0;
        }
        catch (Exception e) { LogBus.Log("mapa", $"no se pudo guardar el mapa: {e.Message}"); }
    }
}
