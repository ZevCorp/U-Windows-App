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

        /// <summary>
        /// A qué NIVEL de la app pertenece esta pantalla: cuántas puertas hay que abrir para verla.
        ///
        /// Es el eje del grafo de NAVEGACIÓN, y no tiene nada que ver con por dónde se pasó. Antes
        /// el nivel salía del recorrido —si llegaste a Escritorio pasando por Imágenes, el grafo
        /// decía que Escritorio cuelga de Imágenes— y eso describe un CAMINO, no una estructura: al
        /// día siguiente, entrando en otro orden, el mismo sitio cambiaba de sitio.
        ///
        /// Lo que no cambia es qué puerta revela qué. Lo visible nada más abrir la app es el nivel 1;
        /// lo que solo aparece tras abrir una puerta está un nivel por debajo de ESA puerta. Se
        /// asigna la primera vez que se ve y no se toca más: un sitio no cambia de nivel porque hoy
        /// hayas llegado por otro lado (2026-08-04, replanteado por el usuario).
        ///
        /// -1 = todavía sin situar.
        /// </summary>
        public int Nivel { get; set; } = -1;
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
        /// A qué GRUPO pertenece esta salida dentro de su app: "" si a ninguno todavía.
        ///
        /// Hoy el sistema deduce un solo grupo y lo hace al vuelo —el cromo, lo que está en todas
        /// las pantallas— y por eso este campo puede quedarse vacío sin que nada se rompa. Existe
        /// para lo que viene: que un agente mire una app y diga «esto es la barra de herramientas»,
        /// «esto el menú de archivo», «esto la navegación lateral», y lo escriba aquí.
        ///
        /// Es un campo y no una jerarquía a propósito: agrupar es ETIQUETAR, no mover nada de sitio.
        /// Una salida sigue estando donde está y llevando a donde lleva; el grupo solo dice con
        /// quién se lee mejor. Así, quien agrupe mañana no puede romper la navegación de hoy.
        /// </summary>
        public string Nivel { get; set; } = "";

        /// <summary>
        /// El nivel de NAVEGACIÓN de esta puerta: cuántas puertas hay que abrir antes de verla.
        ///
        /// Se fija la primera vez que la puerta se observa y ya no se mueve. Una puerta visible al
        /// abrir la app es nivel 1; una que solo aparece después de abrir otra está un nivel por
        /// debajo de aquélla. Eso da la jerarquía REAL de la aplicación —qué contiene qué— en vez
        /// del orden accidental en que alguien paseó por ella.
        ///
        /// -1 = sin situar. Ver <see cref="NodeInfo.Nivel"/>.
        /// </summary>
        public int NivelNav { get; set; } = -1;

        /// <summary>
        /// El nivel lo puso una PERSONA, no la deducción. Entonces no se toca.
        ///
        /// La regla automática acierta casi siempre y se equivoca en lo raro —un botón que aparece
        /// tarde y en realidad es de la navegación principal, o al revés—. Que el usuario pueda
        /// corregirlo no sirve de nada si el siguiente recorrido lo vuelve a mover: una corrección
        /// que no sobrevive no es una corrección, es un comentario (2026-08-04, pedido por el
        /// usuario). Lo dicho a mano gana siempre y se queda.
        /// </summary>
        public bool NivelFijado { get; set; }

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
        bool esPropia = Uia.Propio.EsSuperficie(origen)
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
        IEnumerable<(string Label, string ControlType, string Selector, string[] Alternatives, string Grupo)> salidas)
    {
        string f = Norm(from);
        if (f.Length == 0) return;
        if (!_nodes.ContainsKey(f) && _nodes.Count < MaxNodes) _nodes[f] = new NodeInfo();

        // La pantalla donde primero se entra en una app es su raíz de navegación: nivel 0. Sin este
        // ancla, ningún nivel tiene desde dónde contarse.
        if (_nodes.TryGetValue(f, out var nf) && nf.Nivel < 0)
        {
            string appF = AppDe(f);
            bool hayOtraSituada = _nodes.Any(kv => kv.Value.Nivel >= 0
                && AppDe(kv.Key).Equals(appF, StringComparison.OrdinalIgnoreCase));
            if (!hayOtraSituada) nf.Nivel = 0;
        }
        int nivelAqui = _nodes.TryGetValue(f, out var na) ? na.Nivel : -1;

        // Lo que YA se conoce en esta app, con el nivel que se le puso la primera vez. Una puerta no
        // cambia de nivel por volver a verla desde más adentro: si el panel lateral está en el nivel
        // 1, sigue estando en el 1 aunque lo vuelvas a ver tres carpetas más abajo.
        var nivelPorSelector = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (fr, _, info) in Edges())
            if (info.NivelNav >= 0 && info.Selector.Length > 0
                && AppDe(fr).Equals(AppDe(f), StringComparison.OrdinalIgnoreCase)
                && !nivelPorSelector.ContainsKey(info.Selector))
                nivelPorSelector[info.Selector] = info.NivelNav;

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
            string deducido = EsContenido(s.ControlType) ? "" : DestinoConocidoDe(s.Selector, f);
            string destino = deducido.Length > 0 ? deducido : DestinoPuerta(s.Selector);
            if (string.Equals(destino, f, StringComparison.OrdinalIgnoreCase)) continue; // no lleva a sí misma

            string k = f + "\n" + destino;
            if (_edges.ContainsKey(k)) continue;

            // EL NIVEL SE FIJA UNA VEZ. Si esta puerta ya se vio antes en esta app, conserva el
            // nivel que se le puso entonces —da igual desde dónde se esté mirando ahora—; si es
            // nueva, pertenece a un nivel por debajo de la pantalla que la revela. Eso es lo que
            // convierte «qué abre qué» en una jerarquía estable, en vez de un reflejo del paseo.
            int nivelPuerta = nivelPorSelector.TryGetValue(s.Selector, out int ya)
                ? ya
                : (nivelAqui >= 0 ? nivelAqui + 1 : -1);

            _edges[k] = new EdgeInfo
            {
                Selector = s.Selector,
                Alternatives = s.Alternatives,
                Label = s.Label,
                ControlType = s.ControlType,
                ActionType = EsContenido(s.ControlType) ? "doubleclick" : "click",
                Kind = SafeToClick.Clasificar(s.Label, s.ControlType),
                Explored = deducido.Length > 0,
                Nivel = s.Grupo,
                NivelNav = nivelPuerta,
            };
        }
        Save();
    }

    /// <summary>
    /// Poner a mano el nivel de una salida en toda una app, y dejarlo fijo.
    ///
    /// Es la corrección humana de la jerarquía: la deducción acierta casi siempre, pero quien mira
    /// la pantalla sabe cosas que el árbol UIA no dice —que ese botón raro es navegación principal,
    /// que ese otro no lo es—. Se aplica a TODAS las apariciones de esa salida en la app, porque el
    /// nivel es una propiedad de la salida, no del sitio desde donde se mire.
    ///
    /// Con nivel negativo se suelta: vuelve a mandar la deducción.
    /// </summary>
    public string FijarNivel(string app, string etiquetaOSelector, int nivel)
    {
        string a = app.Trim();
        string q = etiquetaOSelector.Trim();
        if (a.Length == 0 || q.Length == 0) return "falta la app o qué salida mover";

        var tocadas = Edges().Where(e =>
                AppDe(e.From).Equals(a, StringComparison.OrdinalIgnoreCase)
                && (e.Info.Label.Equals(q, StringComparison.OrdinalIgnoreCase)
                    || e.Info.Selector.Equals(q, StringComparison.Ordinal)))
            .ToList();
        if (tocadas.Count == 0) return $"no encuentro ninguna salida «{q}» en «{a}»";

        foreach (var (_, to, info) in tocadas)
        {
            info.NivelNav = nivel;
            info.NivelFijado = nivel >= 0;
            // La pantalla que hay detrás vive en el nivel de su puerta: si se mueve la puerta, se
            // mueve el sitio. Si no, el dibujo diría una cosa y el mapa otra.
            if (nivel >= 0 && !EsPuerta(to) && _nodes.TryGetValue(to, out var n)) n.Nivel = nivel;
        }
        Version++;
        Save();
        string quien = tocadas[0].Info.Label;
        return nivel >= 0
            ? $"«{quien}» queda en el nivel {nivel} de «{a}» ({tocadas.Count} aparición/es). Fijado: la deducción ya no lo mueve."
            : $"«{quien}» vuelve a nivel automático en «{a}».";
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

    /// <summary>Qué app es esta superficie: el proceso dentro de «uia://proceso/loquesea».</summary>
    public static string AppDe(string id)
    {
        if (id.Length == 0) return "";
        int i = id.IndexOf("//", StringComparison.Ordinal);
        if (i < 0) return "";
        int j = id.IndexOf('/', i + 2);
        return j < 0 ? id[(i + 2)..] : id[(i + 2)..j];
    }

    /// <summary>¿Estas dos superficies pertenecen a la misma aplicación?</summary>
    public static bool MismaApp(string a, string b) =>
        string.Equals(AppDe(a), AppDe(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A dónde lleva este selector, si ya se recorrió desde otra pantalla DE LA MISMA APP.
    ///
    /// La deducción entre apps distintas no es un atajo, es una invención: que «Nuevo» lleve a algún
    /// sitio en el explorador no dice nada de un «Nuevo» del Bloc de notas, y mezclarlos convertía
    /// un destino equivocado en un destino equivocado EN TODAS PARTES (2026-08-03).
    /// </summary>
    private string DestinoConocidoDe(string selector, string desde = "")
    {
        if (EsRelativo(selector)) return "";   // su destino depende de dónde estés: no se deduce
        foreach (var (_, to, info) in Edges())
            if (!EsPuerta(to) && to.Length > 0 && info.Explored
                && (desde.Length == 0 || MismaApp(desde, to))
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

        // UN CLIC DENTRO DE UNA APP NO LLEVA A OTRA APP. Cuando otra ventana roba el foco justo
        // después de una acción, lo que aparece delante no es el destino: es una interrupción. El
        // mapa lo anotaba como transición, y «uia://claude.exe/claude» llegó a ser el nodo MÁS
        // visitado del grafo —49 visitas— sin que nadie hubiera navegado nunca hasta allí. Tenía
        // dos consecuencias, y la segunda es la grave: rutas imposibles, y sobre todo que
        // DestinoConocidoDe propagaba ese destino a TODA pantalla que compartiera el selector, así
        // que un solo robo de foco envenenaba el grafo entero (2026-08-03).
        if (!MismaApp(f, t))
        {
            LogBus.Log("mapa", $"NO se aprende «{label}»: «{ShortId(f)}» y «{ShortId(t)}» son apps distintas; "
                             + "eso es un robo de foco, no una transición");
            return;
        }

        // La puerta que acabamos de cruzar deja de ser una incógnita aquí y en todas partes.
        _edges.Remove(f + "\n" + DestinoPuerta(selector));
        ResolverPuertasIguales(selector, t, controlType);

        if (!_nodes.ContainsKey(t) && _nodes.Count < MaxNodes) _nodes[t] = new NodeInfo();
        if (_nodes.TryGetValue(t, out var n))
        {
            n.Visits++; n.LastSeen = DateTime.UtcNow;

            // La pantalla que hay tras una puerta vive en el nivel de esa puerta. Se toma el MENOR
            // encontrado: si a un mismo sitio se llega por dos puertas de niveles distintos, su
            // nivel es el del camino más corto — que es lo que significa «cuántas puertas hay que
            // abrir para verlo», no «cuántas abrí yo esta vez».
            // La puerta se busca por su SELECTOR, no por «de aquí a allí»: antes de cruzarla no
            // tenía destino conocido —era «?selector»— así que buscarla por el par origen→destino
            // no la encontraba nunca y el nivel se quedaba sin asignar (2026-08-04). El selector es
            // lo único que la identifica desde que se ve hasta después de cruzarla.
            int nivelPuerta = -1;
            foreach (var (fr, _, info) in Edges())
                if (info.NivelNav >= 0
                    && string.Equals(info.Selector, selector, StringComparison.Ordinal)
                    && AppDe(fr).Equals(AppDe(f), StringComparison.OrdinalIgnoreCase))
                { nivelPuerta = info.NivelNav; break; }

            if (nivelPuerta < 0 && _nodes.TryGetValue(f, out var origen) && origen.Nivel >= 0)
                nivelPuerta = origen.Nivel + 1;

            // Lo fijado a mano no se toca: ver EdgeInfo.NivelFijado.
            bool fijado = Edges().Any(e => e.Info.NivelFijado
                && string.Equals(e.Info.Selector, selector, StringComparison.Ordinal)
                && AppDe(e.From).Equals(AppDe(f), StringComparison.OrdinalIgnoreCase));
            if (!fijado && nivelPuerta >= 0 && (n.Nivel < 0 || nivelPuerta < n.Nivel)) n.Nivel = nivelPuerta;
        }

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

    /// <summary>
    /// Esta puerta ya no está: se le quita la acción para que deje de usarse al trazar rutas.
    ///
    /// El terreno cambia —una carpeta se borra, un botón desaparece— y el mapa seguía enrutando por
    /// ahí: `map_go_to` intentaba pasar por sitios muertos y acababa en «Ubicación no disponible»
    /// (2026-08-03). Se olvida la ACCIÓN, no el nodo ni la conectividad: que un sitio no se pueda
    /// alcanzar hoy por esta puerta no prueba que el sitio no exista, y borrarlo entero haría que el
    /// mapa se vaciara solo con cualquier fallo pasajero. Solo se olvida tras COMPROBAR la ausencia.
    /// </summary>
    public void OlvidarAccion(string from, string to)
    {
        string k = Norm(from) + "\n" + Norm(to);
        if (!_edges.TryGetValue(k, out var e) || e.Selector.Length == 0) return;
        LogBus.Log("mapa", $"«{e.Label}» ya no está en '{ShortId(Norm(from))}': se deja de enrutar por ahí");
        e.Selector = ""; e.Alternatives = Array.Empty<string>(); e.ClickPos = "";
        e.Explored = false;
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
    /// <summary>
    /// Las salidas de una pantalla: las suyas MÁS las del nivel al que pertenece.
    ///
    /// El panel lateral de una app está en todas sus pantallas, pero se anotaba pantalla por
    /// pantalla en el momento de observarla — y la deducción de a dónde lleva cada botón se hacía
    /// UNA vez, ahí. Consecuencia: una carpeta observada antes de que se cruzara «Música» se quedaba
    /// con ese hermano en gris para siempre, mientras otra observada después lo tenía en verde. Se
    /// veía como que el sistema «conoce» cosas distintas según dónde estés, cuando en realidad la
    /// app expone lo mismo en todas partes (2026-08-04, observado por el usuario).
    ///
    /// Lo que pertenece al nivel se calcula al preguntar, no al observar, así que llega a las
    /// pantallas viejas igual que a las nuevas: lo aprendido en una beneficia a todas.
    /// </summary>
    public List<Hop> ExitsFrom(string surface)
    {
        string s = Norm(surface);
        var propias = Edges().Where(e => string.Equals(e.From, s, StringComparison.OrdinalIgnoreCase))
                             .Select(e => new Hop(e.From, e.To, e.Info))
                             .ToList();

        // UNA PUERTA NO TAPA AL NIVEL. Una puerta dice «esto existe y no sé a dónde va»; el nivel
        // dice «existe y va aquí». Descartar lo heredado por haber ya una salida con ese nombre
        // dejaba siempre la peor de las dos respuestas: cada sección de Configuración veía sus once
        // hermanas como puertas sin destino, aun sabiendo el mapa perfectamente a dónde llevan
        // (2026-08-04). Si lo que hay aquí es una incógnita y el nivel trae la respuesta, gana el
        // nivel; si aquí ya se cruzó de verdad, lo de aquí manda, porque es lo comprobado.
        foreach (var h in CromoDe(AppDe(s)))
        {
            if (h.Info.Label.Length == 0
                || string.Equals(h.To, s, StringComparison.OrdinalIgnoreCase)) continue;   // no lleva a sí misma
            int ya = propias.FindIndex(p => p.Info.Label.Equals(h.Info.Label, StringComparison.OrdinalIgnoreCase));
            if (ya >= 0)
            {
                if (!EsPuerta(propias[ya].To)) continue;          // aquí ya se comprobó: manda lo de aquí
                propias.RemoveAt(ya);                             // era una incógnita: el nivel la resuelve
            }
        }

        var vistas = new HashSet<string>(propias.Select(h => h.Info.Label), StringComparer.OrdinalIgnoreCase);
        foreach (var h in CromoDe(AppDe(s)))
            if (h.Info.Label.Length > 0 && !vistas.Contains(h.Info.Label)
                && !string.Equals(h.To, s, StringComparison.OrdinalIgnoreCase))   // no lleva a sí misma
                // Lo heredado se MARCA. Sin marca, «cruzado desde aquí» y «disponible porque la app
                // lo tiene en todas partes» se leen igual desde fuera, y eso no es un detalle de
                // presentación: perdí la forma de medir cuántas pantallas habían cruzado algo de
                // verdad, justo después de escribir la herencia (2026-08-04). Lo que se hereda hay
                // que poder distinguirlo de lo que se comprobó, o el sistema deja de saber lo que
                // sabe. Y al modelo le sirve igual: una salida heredada es fiable pero no probada
                // desde esta pantalla concreta.
                propias.Add(new Hop(s, h.To, Heredada(h.Info)));

        return propias.OrderByDescending(h => h.Info.Count).ToList();
    }

    /// <summary>
    /// El CROMO de una app: lo que está en todas sus pantallas y lleva siempre al mismo sitio.
    ///
    /// Se reconoce por lo que es, sin listas escritas a mano: una salida que lleva al mismo destino
    /// con el mismo botón desde DOS pantallas distintas ya no describe una pantalla —describe la
    /// aplicación—. Dos y no una, porque desde una sola no hay forma de distinguir el panel lateral
    /// de una carpeta que casualmente contiene algo con ese nombre.
    ///
    /// Es el primer NIVEL que el sistema deduce solo. La estructura admite más: el día que un agente
    /// quiera agrupar salidas por otro criterio —«esto es la barra de herramientas», «esto es el menú
    /// de archivo»— le basta con marcar <see cref="EdgeInfo.Nivel"/> y esto seguirá funcionando
    /// igual, porque agrupar es etiquetar, no mover nada de sitio.
    /// </summary>
    public List<Hop> CromoDe(string app)
    {
        if (app.Length == 0) return new List<Hop>();
        if (_cromo.TryGetValue(app, out var guardado) && _cromoVersion == Version) return guardado;

        // ESTAR EN TODAS PARTES SE SABE MIRANDO, NO CRUZANDO. Antes solo contaban las aristas ya
        // recorridas, y eso exigía cruzar cada hermano DOS veces desde sitios distintos para que el
        // sistema aceptara que pertenece a la app: recorriendo el panel de Configuración en orden,
        // cada sección se alcanzaba desde la anterior —un solo origen— así que ninguna calificaba y
        // el nodo central no aparecía hasta la segunda vuelta (2026-08-04, observado por el usuario).
        //
        // Pero las doce secciones se ven a la vez desde la primera pantalla. Que estén en todas
        // partes es una propiedad OBSERVABLE, y el mapa ya anota lo que ve aunque no lo haya cruzado.
        // Se cuenta por SELECTOR y contando también las puertas sin cruzar: eso responde «¿está en
        // todas las pantallas?», que es la pregunta. A dónde lleva es otra pregunta distinta, y para
        // esa sí hace falta haberla cruzado al menos una vez.
        var vistoDesde = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var destinoDe = new Dictionary<string, Hop>(StringComparer.OrdinalIgnoreCase);

        foreach (var (from, to, info) in Edges())
        {
            if (info.Label.Length == 0 || info.Selector.Length == 0) continue;
            if (!AppDe(from).Equals(app, StringComparison.OrdinalIgnoreCase)) continue;

            if (!vistoDesde.TryGetValue(info.Selector, out var origenes))
                vistoDesde[info.Selector] = origenes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            origenes.Add(from);

            // El destino solo lo aporta una arista CRUZADA: una puerta dice que existe, no a dónde va.
            if (!EsPuerta(to) && AppDe(to).Equals(app, StringComparison.OrdinalIgnoreCase)
                && !destinoDe.ContainsKey(info.Selector))
                destinoDe[info.Selector] = new Hop(from, to, info);
        }

        var cromo = destinoDe
            .Where(kv => vistoDesde.TryGetValue(kv.Key, out var o) && o.Count >= 2)
            .Select(kv => kv.Value)
            .ToList();
        if (_cromoVersion != Version) { _cromo.Clear(); _cromoVersion = Version; }
        _cromo[app] = cromo;
        return cromo;
    }

    /// <summary>Nombre del nivel que el sistema deduce solo: lo que está en todas las pantallas.</summary>
    public const string NivelCromo = "cromo";

    /// <summary>Copia de una arista marcada como heredada del nivel. Copia y no la misma: escribir
    /// en el original convertiría en «heredada» la arista real de la pantalla donde sí se cruzó.</summary>
    private static EdgeInfo Heredada(EdgeInfo o) => new()
    {
        Count = o.Count, Selector = o.Selector, Label = o.Label, ControlType = o.ControlType,
        Alternatives = o.Alternatives, ClickPos = o.ClickPos, Explored = o.Explored,
        ActionType = o.ActionType, Kind = o.Kind, Nivel = NivelCromo,
    };

    private readonly Dictionary<string, List<Hop>> _cromo = new(StringComparer.OrdinalIgnoreCase);
    private int _cromoVersion = -1;

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

                    // El terreno ya escrito también se cura. Las aristas entre apps distintas no
                    // son transiciones sino robos de foco anotados como si lo fueran, y mientras
                    // sigan en el fichero el mapa arranca sucio aunque la regla nueva ya no las
                    // deje entrar. Se van con sus nodos huérfanos (2026-08-03).
                    var cruzadas = map._edges.Keys
                        .Where(k => { int c = k.IndexOf('\n'); return c > 0 && !EsPuerta(k[(c + 1)..])
                                                                  && !MismaApp(k[..c], k[(c + 1)..]); })
                        .ToList();
                    // EL ESCRITORIO NUNCA TUVO SECCIONES. Cada icono seleccionado creó su propio
                    // nodo —«program-manager#docker-desktop», «program-manager#sap-logon-64»…— y con
                    // ellos aristas que afirmaban que para llegar a un icono hay que pasar por el
                    // anterior. Nunca fue cierto: todos se alcanzan directamente desde el escritorio.
                    // La regla nueva ya no los crea; estos son los que quedaron escritos (2026-08-04).
                    var falsos = map._nodes.Keys
                        .Where(n => Uia.Escritorio.EsId(n) && n.Contains('#'))
                        .ToList();
                    if (falsos.Count > 0)
                    {
                        var fuera = new HashSet<string>(falsos, StringComparer.OrdinalIgnoreCase);
                        foreach (var n in falsos) map._nodes.Remove(n);
                        foreach (var k in map._edges.Keys.ToList())
                        {
                            int c = k.IndexOf('\n');
                            if (c <= 0) continue;
                            if (fuera.Contains(k[..c]) || fuera.Contains(k[(c + 1)..])) map._edges.Remove(k);
                        }
                        LogBus.Log("mapa", $"curado: {falsos.Count} nodo(s) del escritorio que eran una "
                            + "selección, no un sitio, eliminados con sus aristas");
                        map.Save();
                    }

                    if (cruzadas.Count > 0)
                    {
                        foreach (var k in cruzadas) map._edges.Remove(k);
                        var vivos = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var k in map._edges.Keys)
                        { int c = k.IndexOf('\n'); vivos.Add(k[..c]); if (c > 0) vivos.Add(k[(c + 1)..]); }
                        int sueltos = map._nodes.Keys.Where(n => !vivos.Contains(n)).ToList()
                            .Count(n => map._nodes.Remove(n));
                        LogBus.Log("mapa", $"curado: {cruzadas.Count} arista(s) entre apps distintas "
                            + $"y {sueltos} nodo(s) sin conexión eliminados; eran foco robado, no terreno");
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
