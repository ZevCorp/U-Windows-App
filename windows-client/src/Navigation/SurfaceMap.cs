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

        /// <summary>
        /// Esta es la pantalla por la que se ENTRÓ en la app. Es dato de bronce: se observó.
        ///
        /// Vivía disfrazado de <see cref="Nivel"/> == 0, y eso mezclaba dos cosas que no lo son —
        /// «aquí se entró» es una observación y no se mueve nunca; el nivel es una derivación que
        /// se recalcula entera cada vez que la estructura cambia—. Mientras compartieron campo, el
        /// lector de bronce tenía que mirar un dato que la plata reescribe (2026-08-10).
        /// </summary>
        public bool EsRaiz { get; set; }

        /// <summary>
        /// La última vez que se MIRÓ esta pantalla y se apuntó qué puertas tenía.
        ///
        /// Es la referencia contra la que se sabe si una puerta sigue estando: las que se vieron en
        /// esta misma pasada están ahora; las que traen una fecha anterior estuvieron, pero hoy no.
        /// </summary>
        public DateTime UltimaObservacion { get; set; }
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
        /// Lo fijó UNA PERSONA, no el maestro que mira la pantalla.
        /// </summary>
        /// <remarks>
        /// Los dos «fijan», pero solo uno enseña. Si las correcciones que se le devuelven al
        /// maestro incluyeran las suyas propias, se estaría confirmando a sí mismo: repetiría su
        /// criterio de ayer creyendo que es una corrección humana, y un error se volvería
        /// permanente por el simple hecho de haberlo cometido una vez (2026-08-06).
        /// </remarks>
        public bool PorPersona { get; set; }

        /// <summary>Esta salida es CROMO: navegación persistente dentro de su ámbito. Es lo que se
        /// pinta de azul, en el nivel que sea — ver <see cref="Ensenanza"/>.</summary>
        public bool EsCromo { get; set; }

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

        /// <summary>
        /// Lo que ALGUIEN AFIRMA que es esta salida («accion»), frente a <see cref="Kind"/>, que es
        /// lo que el sistema deduce solo del tipo de control.
        ///
        /// Son dos campos porque son dos cosas, y meterlas en uno costó una prueba entera: el
        /// arquitecto marcaba acciones a mano sobre el mismo campo que el sistema rellena al nacer
        /// CADA arista, así que «lo ya clasificado» era el 100% y la lista de pendientes contestaba
        /// «no queda nada» con cero salidas declaradas (2026-08-10). Es la misma regla de
        /// procedencia que ya rige en los niveles: lo dicho y lo deducido nunca comparten sitio,
        /// porque el día que discrepan hay que saber cuál es cuál.
        /// </summary>
        public string KindDeclarado { get; set; } = "";

        /// <summary>
        /// La última vez que esta puerta se vio EN PANTALLA.
        /// </summary>
        /// <remarks>
        /// Dejar de verse no es dejar de existir: una puerta que hoy no está —porque la app cambió
        /// de modo, porque el panel está plegado, porque la carpeta está vacía— sigue siendo parte
        /// de lo que se aprendió de esa pantalla, y borrarla haría que el mapa olvidara y
        /// reaprendiera lo mismo una y otra vez. Pero tampoco puede ofrecerse como si estuviera:
        /// prometer una salida que no está en pantalla es mandar al asistente a pulsar el vacío.
        ///
        /// Así que se queda, con la fecha de cuándo se vio por última vez. Comparándola con la
        /// última observación de su pantalla se sabe si está AHORA o solo estuvo
        /// (2026-08-05, pedido por el usuario).
        /// </remarks>
        public DateTime VistaPorUltimaVez { get; set; }
    }

    private readonly Dictionary<string, NodeInfo> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EdgeInfo> _edges = new(StringComparer.Ordinal); // "from\nto"

    private string _pendingId = "";
    private DateTime _lastCommitTime = DateTime.MinValue;
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
        if (_pendingId.Length > 0)
        {
            if ((now - _pendingSince).TotalMilliseconds >= MinDwellMs) Commit(_pendingId, now);
            // PASAR DE LARGO SE APUNTA. Una pantalla que no llega al mínimo no se confirma, y hasta
            // aquí bien: no es un sitio donde se haya estado. Pero el viaje SÍ pasó por ella, y la
            // arista que se cierre después ya no va del sitio anterior a este, sino de dos saltos
            // más atrás. Sin este contador no hay forma de distinguirlo (2026-08-07, ver Commit).
            else if (!string.Equals(_pendingId, _lastCommitted, StringComparison.OrdinalIgnoreCase))
            {
                _pasadasDeLargo++;
                LogBus.Log("mapa", $"pasó de largo por «{ShortId(_pendingId)}» "
                    + $"({(now - _pendingSince).TotalMilliseconds:F0} ms): el clic guardado ya no explica el viaje");
            }
        }

        _pendingId = esPropia ? "" : id;
        _pendingSince = now;
    }

    /// <summary>
    /// Cuántas pantallas se han cruzado sin quedarse desde la última confirmación. Ver <see cref="Commit"/>.
    /// </summary>
    private int _pasadasDeLargo;

    private void Commit(string id, DateTime when)
    {
        // MEDICIÓN, no arreglo: una SPA cambia de identidad a mitad de transición («code» y al
        // rato «graph» para la misma pantalla de GitHub) y si el estado intermedio aguanta el
        // MinDwell, se consolida un nodo FANTASMA con su arista. Antes de tocar el umbral o la
        // estabilización hay que ver cuántas veces pasa y con qué separación: dos commits web del
        // mismo dominio muy seguidos son el sospechoso (2026-08-07, observado por el usuario).
        if (id.StartsWith("web://", StringComparison.OrdinalIgnoreCase)
            && _lastCommitted.StartsWith("web://", StringComparison.OrdinalIgnoreCase)
            && AppDe(id).Equals(AppDe(_lastCommitted), StringComparison.OrdinalIgnoreCase)
            && (when - _lastCommitTime).TotalMilliseconds < 4000)
            LogBus.Log("mapa", $"SPA: «{ShortId(_lastCommitted)}» → «{ShortId(id)}» en "
                + $"{(when - _lastCommitTime).TotalMilliseconds:F0} ms — posible identidad transitoria");
        _lastCommitTime = when;

        if (!_nodes.TryGetValue(id, out var n))
        {
            if (_nodes.Count >= MaxNodes) return;
            n = new NodeInfo();
            _nodes[id] = n;
        }
        n.Visits++;
        n.LastSeen = when;

        // CAMBIAR DE APP NO ES NAVEGAR, tampoco a mano. LearnTraversal ya lo impedía para el cruce
        // deliberado, pero esta vía —la navegación de una persona— sí las acuñaba, y el curado del
        // arranque las borraba después. Entre medias, el grafo afirmaba caminos que no existen:
        // apareció una pantalla «claude.exe» dentro del grafo del explorador solo porque el usuario
        // se cambió de app para escribirme (2026-08-10, aclarado por él).
        //
        // Borrarlo al arrancar no basta: el arquitecto lo vio y gastó un hallazgo en investigarlo.
        // Lo que no es una transición no debe entrar, ni un minuto.
        if (_lastCommitted.Length > 0 && !string.Equals(_lastCommitted, id, StringComparison.OrdinalIgnoreCase)
            && MismaApp(_lastCommitted, id)
            // EL ATRÁS NO ACUÑA ARISTAS: es un gesto de historial, no de estructura. Puedes venir
            // de cualquier parte, así que «a dónde lleva» no es una propiedad del botón sino del
            // camino andado — su rastro es efímero por naturaleza. La arista «vercel → graph» que
            // se acuñaba al volver fue la que hizo saltar a vercel a la altura de su padre en el
            // dibujo (2026-08-07, observado por el usuario). La posición SÍ se sigue actualizando
            // (_lastCommitted): saber dónde estás no es lo mismo que afirmar por dónde se llega.
            && !(_actionLeavingLast is { } gesto
                 && EsGestoDeAtras(AppDe(_lastCommitted), gesto.Label, gesto.Selector)))
        {
            // Se REUTILIZA la arista que ya una estos dos sitios, sea cual sea su puerta. Desde que
            // la puerta forma parte de la clave, crear una nueva aquí duplicaría la misma
            // transición: una anotada al verla pasar y otra aprendida al cruzarla a propósito. Dos
            // caminos distintos sí son dos aristas; el mismo camino visto dos veces, no.
            string k = _edges.Keys.FirstOrDefault(x =>
                           x.StartsWith(_lastCommitted + "\n" + id + "\n", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(x, _lastCommitted + "\n" + id, StringComparison.OrdinalIgnoreCase))
                       ?? Clave(_lastCommitted, id, "");
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

            // UNA PANTALLA SALTADA INVALIDA EL CLIC GUARDADO, y contar clics no lo detecta.
            //
            // El caso, medido entero (2026-08-07, prueba del usuario en el explorador): estaba en
            // Descargas, pulsó «Escritorio» —enseñado de primer nivel— y entró enseguida en una
            // carpeta de dentro. Escritorio no llegó a los 1200 ms, así que nunca se confirmó, y el
            // clic que salía de Descargas se le acabó atribuyendo al viaje Descargas→Nueva carpeta.
            // La arista quedó etiquetada «Escritorio» y, como esa etiqueta está enseñada de primer
            // nivel, AplicarEnsenanza subió a la fila 1 una carpeta de SEGUNDO nivel. De paso,
            // «escritorio» cayó a la fila 2: el puente por nombre se abstuvo al ver su etiqueta ya
            // anclada por esa arista fijada —falsa—. Un solo clic mal atribuido movió dos cosas.
            //
            // Y el botón atrás no tuvo nada que ver, aunque todo saltara al pulsarlo: un nodo se
            // confirma al ABANDONARLO, así que volver fue solo el instante en que la arista mal
            // formada salió a la luz.
            //
            // La guarda de multi-salto de arriba no puede verlo: cuenta los clics posteriores a la
            // confirmación del origen, y el clic culpable es ANTERIOR a ella. En los dos casos —el
            // sano y el saltado— la cuenta da exactamente 1. Lo que sí los distingue es si el viaje
            // atravesó una pantalla que no se quedó.
            if (clic != null && _pasadasDeLargo > 0)
            {
                LogBus.Log("mapa", $"'{ShortId(_lastCommitted)}' → '{ShortId(id)}' sin acción: "
                    + $"por medio quedó {_pasadasDeLargo} pantalla(s) sin confirmar, y «{clic.Label}» no llevaba aquí");
                clic = null;
            }

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

                    // Esta es la vía por la que nacen las aristas cuando navega UNA PERSONA — y era
                    // la única de las tres que no reponía lo enseñado (2026-08-06).
                    AplicarEnsenanza(_lastCommitted, e);

                    // Y TAMBIÉN SITÚA LA PANTALLA A LA QUE LLEGA. «La pantalla que hay tras una
                    // puerta vive en el nivel de esa puerta» ya estaba escrito en el núcleo, pero
                    // solo se aplicaba en LearnTraversal — el camino del cruce DELIBERADO (el
                    // recorredor, map_take). Navegar a mano pasa por aquí, así que en una web,
                    // donde el usuario navega con el ratón, ningún nodo se situaba jamás.
                    //
                    // Lo que se veía: «Insights» y «Pull requests» un nivel por debajo de sus
                    // hermanos, y colocándose solos a la quinta visita (2026-08-08, trazado por el
                    // usuario). No se movía el nodo: se movía el DIBUJO, que cuando una pantalla no
                    // tiene nivel la coloca por distancia, y esa distancia cambia según aparecen
                    // aristas. Un sitio que baila mientras exploras no es un mapa.
                    //
                    // LA PROFUNDIDAD SE CALCULA ENTERA, no se hereda de la puerta. Aquí ponía
                    // `n.Nivel = e.NivelNav`, y eso confunde «a un clic» con «a esta profundidad»:
                    // una puerta cromo lleva a sitios que están a un clic pero viven hondo. Ver
                    // RecalcularProfundidades, que lo resuelve por camino más corto ignorando los
                    // atajos (2026-08-09, medido por el arquitecto).
                    RecalcularProfundidades(AppDe(id));
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
        // Lo saltado ya se ha cobrado en la arista de arriba; a partir de aquí el viaje vuelve a
        // empezar desde un sitio confirmado.
        _pasadasDeLargo = 0;
        Version++;

        if (++_dirty >= 20) Save(); // persistencia periódica; el cierre hace la final
    }

    // ── Lectura (para la visualización) ──────────────────────────────────────

    public IReadOnlyDictionary<string, NodeInfo> Nodes => _nodes;

    /// <summary>
    /// La clave de una arista: DE DÓNDE, A DÓNDE y POR QUÉ PUERTA.
    /// </summary>
    /// <remarks>
    /// La puerta forma parte de la identidad, y esto no es un capricho de modelado: sin ella, dos
    /// puertas distintas que llevan al mismo sitio son la MISMA arista, y la segunda pisa a la
    /// primera. En el explorador pasa constantemente —al panel de «Imágenes» se llega desde el árbol
    /// de la izquierda y desde los accesos anclados—, y el mapa se quedaba solo con la última
    /// aprendida: el otro camino desaparecía del grafo aunque siguiera existiendo en la pantalla.
    ///
    /// Dos caminos al mismo sitio son dos caminos, y el usuario lo pidió así explícitamente
    /// (2026-08-05). Fundirlos es perder información que la pantalla sí tiene.
    ///
    /// Las claves viejas —sin puerta— se siguen leyendo: se les entiende la puerta vacía.
    /// </remarks>
    private static string Clave(string from, string to, string selector) => from + "\n" + to + "\n" + selector;

    public IEnumerable<(string From, string To, EdgeInfo Info)> Edges()
    {
        foreach (var kv in _edges)
        {
            int a = kv.Key.IndexOf('\n');
            if (a < 0) continue;
            int b = kv.Key.IndexOf('\n', a + 1);
            string to = b < 0 ? kv.Key[(a + 1)..] : kv.Key[(a + 1)..b];
            yield return (kv.Key[..a], to, kv.Value);
        }
    }

    /// <summary>Cuántas aristas saben ya CÓMO recorrerse. Es la medida de madurez del mapa.</summary>
    public int EdgesWithAction => _edges.Values.Count(e => e.Selector.Length > 0);

    /// <summary>
    /// ¿Esta salida estaba a la vista la última vez que se miró su pantalla?
    ///
    /// «No estar ahora» no borra nada —la puerta se queda en el mapa, con su nivel y su destino—,
    /// pero sí cambia lo que se puede prometer: ofrecer como salida algo que no está en pantalla es
    /// mandar a pulsar el vacío. Quien pregunta merece saber cuál de las dos cosas es.
    ///
    /// Sin fecha —datos de antes de que esto existiera— se responde que sí: lo que se aprendió
    /// entonces se vio alguna vez, y tratarlo como ausente escondería medio mapa de golpe.
    /// </summary>
    public bool SigueALaVista(string desde, EdgeInfo e)
    {
        if (e.VistaPorUltimaVez == default) return true;
        string d = Norm(desde);
        if (!_nodes.TryGetValue(d, out var n) || n.UltimaObservacion == default) return true;
        // Misma pasada de observación = sigue estando. Se deja un margen por si la anotación de la
        // arista y el sello del nodo caen en milisegundos distintos.
        return e.VistaPorUltimaVez >= n.UltimaObservacion.AddMilliseconds(-500);
    }

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
            if (!hayOtraSituada) { nf.Nivel = 0; nf.EsRaiz = true; }
        }
        // Lo que YA se conoce en esta app, con el nivel que se le puso la primera vez. Una puerta no
        // cambia de nivel por volver a verla desde más adentro: si el panel lateral está en el nivel
        // 1, sigue estando en el 1 aunque lo vuelvas a ver tres carpetas más abajo.
        // Y CON EL NIVEL VIAJA QUIÉN LO PUSO. Al entrar en una carpeta se anota una aparición NUEVA
        // de las mismas salidas del panel, y esa copia nacía sin el sello de «fijado a mano»: el
        // nivel sí se heredaba, pero el azul exige además que alguien lo haya dicho, así que lo que
        // el usuario acababa de marcar volvía a verde en cuanto entraba en ello (2026-08-06,
        // observado por el usuario). Una corrección humana es de la SALIDA en toda la app, no de la
        // pantalla desde la que se hizo.
        var fijadoPorSelector = new Dictionary<string, bool>(StringComparer.Ordinal);
        var humanoPorSelector = new Dictionary<string, bool>(StringComparer.Ordinal);
        var nivelPorSelector = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (fr, _, info) in Edges())
            if (info.NivelNav >= 0 && info.Selector.Length > 0
                && AppDe(fr).Equals(AppDe(f), StringComparison.OrdinalIgnoreCase)
                && !nivelPorSelector.ContainsKey(info.Selector))
            {
                nivelPorSelector[info.Selector] = info.NivelNav;
                fijadoPorSelector[info.Selector] = info.NivelFijado;
                humanoPorSelector[info.Selector] = info.PorPersona;
            }

        // Se sella la pasada. Lo que se vea en ella queda con esta misma marca de tiempo, y lo que
        // no, se queda con la anterior: ahí está la diferencia entre «está» y «estuvo».
        var ahora = DateTime.UtcNow;
        if (_nodes.TryGetValue(f, out var nObs)) nObs.UltimaObservacion = ahora;

        // Cuántas puertas NUEVAS deja esta pasada. Ver el porqué al final del método.
        int nacidas = 0;

        foreach (var s in salidas)
        {
            if (s.Selector.Length == 0) continue;

            // Si ya conocemos una salida REAL con este selector desde aquí, no se toca: lo recorrido
            // manda sobre lo observado. Pero SÍ se le refresca la fecha: sigue estando delante, y
            // sin esto lo ya aprendido se leería como desaparecido en cuanto se aprende.
            var yaReal = _edges.FirstOrDefault(kv =>
                kv.Key.StartsWith(f + "\n", StringComparison.OrdinalIgnoreCase)
                && string.Equals(kv.Value.Selector, s.Selector, StringComparison.Ordinal));
            if (yaReal.Value != null)
            {
                yaReal.Value.VistaPorUltimaVez = ahora;
                int corte = yaReal.Key.IndexOf('\n');
                int corte2 = yaReal.Key.IndexOf('\n', corte + 1);
                string haciaDonde = corte2 < 0 ? yaReal.Key[(corte + 1)..] : yaReal.Key[(corte + 1)..corte2];
                if (!EsPuerta(haciaDonde)) continue;
            }

            // El destino puede deducirse: si este MISMO selector ya llevó a algún sitio desde otra
            // pantalla, lleva al mismo desde aquí. Vale para el cromo de navegación —el panel
            // izquierdo es idéntico en todas las carpetas— y NO para el contenido, porque dos
            // carpetas distintas pueden tener cada una su «readme.txt» y no son el mismo destino.
            string deducido = EsContenido(s.ControlType) ? "" : DestinoConocidoDe(s.Selector, f);
            string destino = deducido.Length > 0 ? deducido : DestinoPuerta(s.Selector);
            if (string.Equals(destino, f, StringComparison.OrdinalIgnoreCase)) continue; // no lleva a sí misma

            string k = Clave(f, destino, s.Selector);
            if (_edges.TryGetValue(k, out var yaEsta)) { yaEsta.VistaPorUltimaVez = ahora; continue; }

            // EL NIVEL NO SALE DEL PASEO. Si esta puerta ya se vio antes en esta app, conserva el
            // nivel que se le puso entonces —da igual desde dónde se esté mirando ahora—; y si es
            // nueva, nace SIN NIVEL, esperando a que alguien lo diga.
            //
            // Antes nacía en «nivelAqui + 1», un nivel por debajo de la pantalla que la revelaba.
            // Congelar ese número la volvía ESTABLE, no CORRECTA: congelaba el accidente de dónde
            // se la vio primero. Medido el 2026-08-10 sobre explorer.exe: el mismo botón de
            // scrollbar salía sin nivel en «inicio» y en nivel 4 en «u-versiones», porque allí
            // nació. Y un ARCHIVO nació en «nivel 5» —anunciando estructura más profunda—, se
            // cruzó confiando en esa etiqueta y abrió el Bloc de notas.
            //
            // Un número que depende de por dónde pasaste no describe la app: describe tu paseo.
            // Decir «no sé» es más barato que decir un número inventado, y es lo que vuelve
            // «cuánto queda sin situar» una pregunta con respuesta.
            int nivelPuerta = nivelPorSelector.TryGetValue(s.Selector, out int ya) ? ya : -1;

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
                NivelFijado = fijadoPorSelector.TryGetValue(s.Selector, out bool fj) && fj,
                PorPersona = humanoPorSelector.TryGetValue(s.Selector, out bool hm) && hm,
                VistaPorUltimaVez = ahora,
            };

            // LO ENSEÑADO SE REAPLICA AL VERLO: una puerta recién observada nace ya con el nivel
            // que se le dio en su día, aunque el grafo se haya borrado entero desde entonces.
            AplicarEnsenanza(f, _edges[k]);
            nacidas++;
        }

        // VER PUERTAS NUEVAS ES APRENDER, y <see cref="Version"/> dice justo eso: «cambia cuando el
        // mapa aprende algo». Aquí no se movía, y mientras nadie derivara del mapa no se notaba —el
        // pintor tiene además su propio latido—. Desde que hay cachés colgadas de esa versión (el
        // cromo, la plata), no moverla significa servir una estructura vieja hasta que otra cosa la
        // empuje.
        //
        // Solo si NACIÓ alguna: esto se llama en cada refresco del locator, y subir la versión al
        // ver lo mismo de siempre convertiría cada tick en un repintado y en una derivación. Es la
        // lección nº8 —el costo por iteración antes que la cadencia— aplicada al escribirlo.
        if (nacidas > 0) Version++;
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
    /// <param name="porPersona">
    /// Lo dice una persona (true) o el maestro que mira la pantalla (false). Solo lo humano se le
    /// devuelve después como corrección: si le devolviéramos lo suyo, se confirmaría a sí mismo.
    /// </param>
    /// <param name="cromo">
    /// ¿Es navegación persistente (azul)? Sin decirlo, el nivel 1 lo es y los demás no — que es el
    /// comportamiento de siempre. Decirlo permite lo nuevo: cromo de nivel 2, o un nivel 1 que no
    /// sea cromo.
    /// </param>
    public string FijarNivel(string app, string etiquetaOSelector, int nivel, bool porPersona = true,
        bool? cromo = null)
    {
        string a = app.Trim();
        string q = etiquetaOSelector.Trim();
        if (a.Length == 0 || q.Length == 0) return "falta la app o qué salida mover";
        bool esCromo = cromo ?? nivel == 1;

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
            info.PorPersona = nivel >= 0 && (porPersona || info.PorPersona);   // lo humano no se degrada
            info.EsCromo = nivel >= 0 && esCromo;
            // Se apunta también SU SELECTOR: es lo que permite que dos salidas con el mismo nombre
            // tengan niveles distintos y dejen de pisarse al recargar (ver Ensenanza).
            Aprender(a, info.Label, nivel, porPersona, esCromo, info.Selector);   // sobrevive a borrar el grafo
            // La pantalla que hay detrás vive en el nivel de su puerta: si se mueve la puerta, se
            // mueve el sitio. Y si la puerta se SUELTA, el sitio también se suelta — dejarle el
            // nivel viejo lo clavaba en esa fila para siempre: «Graph» se soltó y siguió a la
            // altura de «Code» porque su nodo conservaba el 2 declarado (2026-08-07).
            if (!EsPuerta(to) && _nodes.TryGetValue(to, out var n))
                n.Nivel = nivel >= 0 ? nivel : -1;
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
        if (EsRelativo(selector)) return false;   // está en todas partes pero NO lleva al mismo sitio
        return SelectoresCromo().Contains(selector);
    }

    /// <summary>
    /// TODOS los selectores que son cromo, de una pasada. Mismo criterio que
    /// <see cref="EsCromoGlobal"/> — es la misma pregunta hecha para todos a la vez.
    ///
    /// SOLO LO DECLARADO. Antes esto CONTABA: una puerta vista en tres pantallas distintas se daba
    /// por mobiliario de la app. Era el último recurso cuando nadie había dicho nada, y hoy sobra
    /// —lo pidió el usuario el 2026-08-08— porque hay tres fuentes que DECLARAN en vez de adivinar:
    /// la persona, el maestro, y la propia página con sus landmarks de HTML.
    ///
    /// Contar tenía tres defectos que ninguna declaración tiene: necesitaba tres visitas para
    /// opinar, confundía lo que casualmente se repite con lo que pertenece al marco, y llegaba a
    /// contradecir a fuentes más fiables. Un mapa que adivina cuando podría preguntar acaba
    /// discutiendo consigo mismo.
    ///
    /// Sigue existiendo por el COSTE: el pintor pregunta por cada punto en cada cuadro, y esto se
    /// resuelve una vez para todos — O(aristas) en vez de O(puntos × aristas).
    /// </summary>
    public HashSet<string> SelectoresCromo()
    {
        if (_selectoresCromo != null && _selectoresCromoVersion == Version) return _selectoresCromo;

        var cromo = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, _, info) in Edges())
            if (info.EsCromo && info.NivelFijado && info.Selector.Length > 0 && !EsRelativo(info.Selector))
                cromo.Add(info.Selector);

        // LO DERIVADO ENTRA DESPUÉS Y NO PISA NADA: ver la nota larga en CromoDe. El filtro de
        // selectores relativos se mantiene aunque `Plata` ya los descarte por evidencia — la
        // evidencia necesita DOS destinos observados, y hasta que se hayan visto los dos, el
        // reconocimiento por nombre sigue siendo la única red. Lo uno no sustituye a lo otro:
        // «Subir» está en todas las pantallas, así que sin esto sería mobiliario ejemplar.
        foreach (string app in Edges().Select(e => AppDe(e.From))
                     .Where(a => a.Length > 0)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
            foreach (var s in Plata.DerivadaDe(this, app).SalidasPorSelector.Values)
                if (s.Clase == Plata.Clase.Cromo && s.Selector.Length > 0 && !EsRelativo(s.Selector))
                    cromo.Add(s.Selector);

        _selectoresCromoVersion = Version;
        _selectoresCromo = cromo;
        return cromo;
    }

    /// <summary>Lo mismo que la caché de <see cref="CromoDe"/> y por lo mismo: esto se pregunta por
    /// cada punto y en cada cuadro, y desde que además deriva, resolverlo entero cada vez sería
    /// meter el cálculo en el camino caliente.</summary>
    private HashSet<string>? _selectoresCromo;
    private int _selectoresCromoVersion = -1;

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

        // La puerta va ahora en la clave, así que se buscan las dos formas: la nueva
        // «desde\n?sel\nsel» y la vieja «desde\n?sel», que puede venir de un mapa ya guardado.
        string colgante = "\n" + DestinoPuerta(selector);
        var promover = _edges
            .Where(kv => kv.Key.EndsWith(colgante + "\n" + selector, StringComparison.Ordinal)
                      || kv.Key.EndsWith(colgante, StringComparison.Ordinal))
            .ToList();

        foreach (var kv in promover)
        {
            string desde = kv.Key[..kv.Key.IndexOf('\n')];
            if (string.Equals(desde, destino, StringComparison.OrdinalIgnoreCase)) { _edges.Remove(kv.Key); continue; }
            _edges.Remove(kv.Key);
            string k = Clave(desde, destino, selector);
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
        _edges.Remove(Clave(f, DestinoPuerta(selector), selector));
        _edges.Remove(f + "\n" + DestinoPuerta(selector));   // por si viene de un mapa guardado antes
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

            // QUE LA PUERTA ESTÉ DECLARADA ES LA RAZÓN MÁS FUERTE PARA SITUAR SU DESTINO, no una
            // razón para negarse. Aquí había un `!fijado` que miraba si la PUERTA estaba fijada
            // para decidir si se situaba el NODO — un error de categoría, y con consecuencias:
            //
            // el nodo caía entre dos sillas. FijarNivel sí sitúa el destino, pero solo cuando ya se
            // conoce (si la puerta todavía es «?selector» no hay destino que situar); y cuando por
            // fin se cruzaba, este guardián lo bloqueaba. Nunca se situaba. Se midió en cuanto los
            // landmarks empezaron a declarar TODAS las puertas de una web: las trece pantallas de
            // GitHub en «nivel ?», y el dibujo poniéndolas todas a la misma altura porque sin nivel
            // caen al mismo suelo (2026-08-08, observado por el usuario: «todos los botones
            // quedaron en el mismo nivel»).
            //
            // La profundidad NO se hereda de la puerta: se calcula entera al final de este método
            // (ver RecalcularProfundidades). Un atajo cromo dice a cuántos clics está el destino,
            // no a qué profundidad vive — y confundirlo dejaba «C:» por debajo de su propio padre.
        }

        // También aquí: el ATRÁS no acuña. Esta es la vía del cruce deliberado (map_take «Atrás»),
        // y sin este guardia la regla valía para el paseo a mano pero no para el asistente.
        if (EsGestoDeAtras(AppDe(f), label, selector))
        {
            LogBus.Log("mapa", $"«{label}» es el gesto de volver: se navegó, no se acuña arista");
            return;
        }

        string k = Clave(f, t, selector);
        if (!_edges.TryGetValue(k, out var e)) { e = new EdgeInfo(); _edges[k] = e; }
        e.Count++;
        e.VistaPorUltimaVez = DateTime.UtcNow;   // acabamos de cruzarla: por fuerza estaba a la vista
        e.Selector = selector;
        e.Alternatives = alternatives;
        e.Label = label;
        e.ControlType = controlType;
        e.ActionType = actionType;
        e.Kind = SafeToClick.Clasificar(label, controlType);
        e.Explored = true;

        // LO ENSEÑADO SE REAPLICA TAMBIÉN AL CRUZAR. Se hacía solo al observar, y una arista nace
        // por dos vías: verla y cruzarla. Al cruzarla nacía sin nivel, así que tras borrar el grafo
        // y volver a navegar, lo enseñado quedaba colocado por el orden del paseo en vez de en su
        // nivel — que es justo lo que no debe pasar: el nivel es de la salida, no del camino por el
        // que se llegó (2026-08-06, observado por el usuario).
        AplicarEnsenanza(f, e);

        // La estructura cambió: hay una arista más, así que las profundidades pueden haber
        // cambiado. Se recalculan enteras — es lo que las hace independientes del orden en que se
        // navegue (ver RecalcularProfundidades).
        RecalcularProfundidades(AppDe(f));

        Version++;
        Save();

        // UN SOLO SITIO DESDE EL QUE SE CUENTA UN CRUCE, y por eso está aquí dentro y no en quien
        // llama. `LearnTraversal` tiene OCHO llamantes en cuatro archivos —el crawler, el
        // explorador, cinco herramientas MCP— y avisar desde cada uno es la forma exacta del fallo
        // que este repo ya pagó: el diagnóstico de la superficie SAP se cableó en dos de los tres
        // sitios que la construían, y el que faltaba era justo el que usa el operador.
        //
        // Se avisa DESPUÉS de acuñar y no antes: los guardias de arriba —cambiar de app no es
        // navegar, el gesto de atrás no acuña— salen por `return`, así que llegar aquí ya significa
        // que hubo un cruce de verdad. Anunciar antes convertiría cada uno de esos rechazos en un
        // hecho falso en quien escuche (2026-08-12).
        SeCruzo?.Invoke(f, selector, t);
    }

    /// <summary>
    /// Un cruce APRENDIDO: se pulsó <c>selector</c> en <c>desde</c> y se acabó en <c>hasta</c>.
    ///
    /// Existe para que el núcleo nuevo (<c>nucleo/Grafo</c>, vía <c>MapaVivo</c>) se entere de lo
    /// mismo que este mapa, sin que ninguno de los ocho llamantes tenga que acordarse. La dirección
    /// de la dependencia no se invierte: esto ANUNCIA, y quien quiera escuchar se suscribe — el
    /// núcleo nuevo sigue sin conocer a nadie de aquí.
    ///
    /// Hasta hoy `MapaVivo.Cruzado()` no lo llamaba nadie: el núcleo nuevo recibía observaciones
    /// pero jamás un cruce, así que su tabla de destinos estaba siempre vacía y sus promesas 3 y 4
    /// —«cruzar guarda a dónde llevó» y «el mismo selector puede llevar a sitios distintos»— eran
    /// ciertas en su contrato e inertes en producción.
    /// </summary>
    public event Action<string, string, string>? SeCruzo;

    /// <summary>
    /// Esta puerta ya no está: se le quita la acción para que deje de usarse al trazar rutas.
    ///
    /// El terreno cambia —una carpeta se borra, un botón desaparece— y el mapa seguía enrutando por
    /// ahí: `map_go_to` intentaba pasar por sitios muertos y acababa en «Ubicación no disponible»
    /// (2026-08-03). Se olvida la ACCIÓN, no el nodo ni la conectividad: que un sitio no se pueda
    /// alcanzar hoy por esta puerta no prueba que el sitio no exista, y borrarlo entero haría que el
    /// mapa se vaciara solo con cualquier fallo pasajero. Solo se olvida tras COMPROBAR la ausencia.
    /// </summary>
    /// <summary>
    /// Olvidarlo TODO y volver a empezar. Devuelve qué había, para poder decirlo.
    /// </summary>
    /// <remarks>
    /// Existe porque toda prueba del mapa tiene que empezar desde cero: un grafo con historia
    /// esconde justo lo que se quiere medir —si las puertas ya están cruzadas, no se ve si el
    /// sistema sabe llegar a un sitio nuevo ni si una app desconocida se mapea sola—. Hasta ahora
    /// eso se hacía borrando un archivo a mano, que no es algo que se pueda pedir a nadie.
    ///
    /// No hay confirmación aquí: quien llama es quien sabe si preguntó. Y se guarda en el acto, para
    /// que un cierre inesperado no resucite lo borrado.
    /// </remarks>
    /// <summary>
    /// LA PROFUNDIDAD DE CADA PANTALLA, calculada entera y no a trocitos.
    ///
    /// «A cuántos clics está» y «a qué profundidad vive» son dos preguntas distintas, y estaban
    /// fundidas: la pantalla heredaba el nivel de la PUERTA que la abría. Pero una puerta cromo
    /// dice «estoy a un clic desde cualquier sitio», no «lo que hay detrás es de primer nivel».
    ///
    /// Lo midió el arquitecto recorriendo Este equipo → C: → Usuarios → felip → .claude → skills
    /// (2026-08-09): «disco-local-c» quedaba en 0 —POR DEBAJO de su propio padre, porque se alcanza
    /// por el atajo del panel lateral—, tres saltos consecutivos empataban en 2, y «skills» saltaba
    /// a 4 sin que existiera un 3. Con esos números el navegador cree que C: es una raíz y que
    /// .claude y Usuarios son hermanos.
    ///
    /// Se CALCULA por camino más corto desde la raíz IGNORANDO las aristas cromo, que son atajos y
    /// no estructura. Lo que solo se alcanza por cromo se queda en 1: es una sección de primer
    /// nivel, alcanzable desde cualquier parte — que es exactamente lo que significa.
    ///
    /// Y se recalcula ENTERO en vez de parchear al cruzar, porque asignar al vuelo depende del
    /// orden en que uno navegue: el mismo sitio salía en niveles distintos según el día. Un cálculo
    /// completo da el mismo resultado siempre, se llegue por donde se llegue.
    ///
    /// Lo fijado a mano no se toca: quien declaró un nivel sabe algo que este cálculo no.
    /// </summary>
    public void RecalcularProfundidades(string app)
    {
        if (app.Length == 0) return;
        bool DeLaApp(string id) => AppDe(id).Equals(app, StringComparison.OrdinalIgnoreCase);

        // ESTE MÉTODO YA NO CALCULA: PROYECTA. Tenía su propio recorrido —correcto, y lo dice el
        // historial de arriba— pero desde que existe `Plata` había DOS respondiendo a la misma
        // pregunta con entradas distintas: aquí lo estructural se decidía mirando `EsCromo`, que
        // solo existe si alguien lo declaró; allí sale de la permanencia medida.
        //
        // Dos cálculos de la misma cosa es el fallo que este repo ya pagó con rondas enteras —está
        // escrito en el pintor con todas las letras: «el dibujo no opina, una sola fuente»—. La
        // convivencia fue deliberada mientras la derivación no tenía kilómetros; esto la termina.
        // Lo que se conserva intacto es el CONTRATO de este método: quién manda (la persona),
        // cuándo no se toca nada (sin raíz), y que se avise en el log de cuántas se movieron.
        var plata = Plata.DerivadaDe(this, app);

        // Sin ancla no hay nada que medir, y adivinarla sería peor. Escribir aquí lo que la
        // derivación devuelve —todo a −1— borraría el nivel 0 de la raíz y la app no volvería a
        // situarse nunca: el único estado del que no se sale solo.
        if (plata.Raiz.Length == 0) return;

        int movidas = 0;
        foreach (var (id, n) in _nodes)
        {
            if (!DeLaApp(id)) continue;
            int nuevo = plata.Pantallas.TryGetValue(id, out var pp) ? pp.Profundidad : -1;
            if (n.Nivel == nuevo) continue;
            n.Nivel = nuevo;
            movidas++;
        }
        if (movidas > 0)
        {
            Version++;
            LogBus.Log("mapa", $"profundidades de «{app}» recalculadas: {movidas} pantalla(s) movida(s) "
                + $"· raíz «{ShortId(plata.Raiz)}» · {plata.M.PantallasSituadas} situada(s) "
                + $"· cobertura {plata.M.Cobertura:P0}");
        }
    }

    public (int Nodos, int Aristas) OlvidarTodo()
    {
        int nodos = _nodes.Count, aristas = _edges.Count;
        _nodes.Clear();
        _edges.Clear();
        _cromo.Clear();
        _cromoVersion = -1;
        _lastCommitted = "";
        _pendingId = "";
        Version++;
        Save();
        LogBus.Log("mapa", $"grafo olvidado a petición: {nodos} nodo(s) y {aristas} arista(s) borradas");
        return (nodos, aristas);
    }

    /// <summary>
    /// Olvidar el terreno de UNA app: sus pantallas y sus puertas, y las de nadie más.
    ///
    /// Existe porque probar el núcleo sobre una app costaba el mapa de todas: la prueba limpia
    /// antes de recorrer —un grafo con historia esconde lo que se mide— pero solo OlvidarTodo
    /// existía, así que verificar el explorador arrasaba lo andado en Chrome, que no tenía nada
    /// que ver (2026-08-08, señalado por el usuario).
    ///
    /// Las ENSEÑANZAS no se tocan, como en OlvidarTodo: son aprendizaje, no terreno, y se reponen
    /// solas sobre las puertas que vuelvan a nacer.
    /// </summary>
    public (int Nodos, int Aristas) OlvidarApp(string app)
    {
        if (string.IsNullOrWhiteSpace(app)) return (0, 0);
        bool DeLaApp(string id) => AppDe(id).Equals(app, StringComparison.OrdinalIgnoreCase);

        var nodos = _nodes.Keys.Where(DeLaApp).ToList();
        // Una arista es de la app si sale de ella o entra en ella: dejar la mitad que "entra"
        // desde otra app apuntaría a pantallas que ya no existen.
        var aristas = _edges.Keys.Where(k =>
        {
            var p = k.Split('\n');
            return DeLaApp(p[0]) || (p.Length > 1 && !EsPuerta(p[1]) && DeLaApp(p[1]));
        }).ToList();

        foreach (var n in nodos) _nodes.Remove(n);
        foreach (var k in aristas) _edges.Remove(k);
        _cromo.Clear();
        _cromoVersion = -1;
        if (DeLaApp(_lastCommitted)) _lastCommitted = "";
        if (DeLaApp(_pendingId)) _pendingId = "";
        Version++;
        Save();
        LogBus.Log("mapa", $"terreno de «{app}» olvidado: {nodos.Count} nodo(s) y {aristas.Count} arista(s); el resto queda");
        return (nodos.Count, aristas.Count);
    }

    /// <param name="selector">
    /// Cuál de las puertas entre esos dos sitios. Vacío = todas. Entre un par puede haber varias
    /// —el panel lateral y la miga de pan llevan al mismo sitio— y quien llama suele saber cuál
    /// falló: quitarle la acción a la buena por culpa de la mala sería peor que no hacer nada.
    /// </param>
    public void OlvidarAccion(string from, string to, string selector = "")
    {
        // LA CLAVE TENÍA DOS PARTES Y EL DICCIONARIO TRES.
        //
        // Esto construía «from\nto» y buscaba con TryGetValue, pero _edges se indexa con
        // Clave(from, to, selector) —tres partes—, así que la búsqueda no acertaba NUNCA y el método
        // salía por el primer return sin tocar nada. Su propia línea de log aparece cero veces en
        // todos los logs que existen: no es que fallara a veces, es que no ha ocurrido jamás.
        //
        // Lo que se caía con ello es el mecanismo entero de «esta puerta ya no lleva ahí, olvídala y
        // busca otro camino». Una arista falsa se volvía a elegir en cada intento, para siempre: el
        // mapa creía que «Nombre» —la cabecera de columna del explorador— llevaba a inetpub, y la
        // ruta moría ahí una y otra vez aunque el que llamaba pidiera olvidarla (2026-08-08).
        //
        // Se recorre en vez de indexar. Es O(aristas) y se llama al fallar un tramo, no en bucle.
        string f = Norm(from), t = Norm(to);
        var tocadas = _edges.Where(kv =>
        {
            var p = kv.Key.Split('\n');
            return p.Length >= 2
                && p[0].Equals(f, StringComparison.OrdinalIgnoreCase)
                && p[1].Equals(t, StringComparison.OrdinalIgnoreCase)
                && kv.Value.Selector.Length > 0
                && (selector.Length == 0 || kv.Value.Selector.Equals(selector, StringComparison.Ordinal));
        }).Select(kv => kv.Value).ToList();

        if (tocadas.Count == 0) return;
        foreach (var e in tocadas)
        {
            LogBus.Log("mapa", $"«{e.Label}» ya no está en '{ShortId(f)}': se deja de enrutar por ahí");
            e.Selector = ""; e.Alternatives = Array.Empty<string>(); e.ClickPos = "";
            e.Explored = false;
        }
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

            // LO QUE ESTÁ EN TODAS PARTES SE PUEDE TOMAR DESDE AQUÍ. El planificador recorría solo
            // las aristas crudas, así que la herencia del nivel llegaba a «¿qué hay desde aquí?» y
            // a «púlsalo», pero NO a «¿cómo llego?»: se decía «no sé llegar» a un sitio que estaba
            // a un clic en el panel lateral (2026-08-05). El mobiliario fijo de una app es
            // alcanzable desde cualquiera de sus pantallas — eso es lo que lo hace mobiliario.
            //
            // Se añaden como tramos VIRTUALES, no como aristas guardadas: materializarlas sería
            // escribir el mismo hecho una vez por pantalla —dieciséis salidas por cada sitio— y dos
            // copias de una misma verdad acaban siempre desincronizadas.
            var salidas = Edges().Where(e =>
                    string.Equals(e.From, actual, StringComparison.OrdinalIgnoreCase))
                .Select(e => (e.From, e.To, e.Info))
                .Concat(CromoDe(AppDe(actual))
                    .Where(h => !string.Equals(h.To, actual, StringComparison.OrdinalIgnoreCase))
                    .Select(h => (From: actual, h.To, h.Info)));

            foreach (var (f, t, info) in salidas)
            {
                if (info.Selector.Length == 0) continue;              // sin acción no se recorre
                if (EsPuerta(t)) continue;                            // puerta sin cruzar: no sé a dónde da
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
                // EL NIVEL DECLARADO SE PEGA A LA SALIDA DE AQUÍ. La de esta pantalla manda sobre
                // el destino —es lo comprobado— pero no sobre el NIVEL: si alguien declaró que esa
                // salida es del primer nivel, lo es desde donde se mire. Sin esto, el azul dependía
                // de dónde estuvieras: en «Música» salían tres marcadas y en «Notas» solo dos,
                // porque allí había una arista propia sin marcar que tapaba a la declarada
                // (2026-08-06, observado por el usuario). Un nivel que cambia según dónde estés no
                // es un nivel de la app.
                if (h.Info.NivelFijado && !propias[ya].Info.NivelFijado)
                {
                    propias[ya].Info.NivelNav = h.Info.NivelNav;
                    propias[ya].Info.NivelFijado = true;
                    propias[ya].Info.PorPersona = h.Info.PorPersona;
                    propias[ya].Info.EsCromo = h.Info.EsCromo;
                }
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
    /// <summary>
    /// Ya no hay nada que silenciar: el cromo SOLO sale de lo declarado.
    ///
    /// Fue un interruptor de experimento —mientras se construía la enseñanza había que poder ver
    /// qué producía ELLA y no la deducción— y el 2026-08-08 el usuario pidió eliminar la deducción
    /// del todo. Se conserva la propiedad para no romper a quien la lea, siempre en true: la
    /// pregunta que hacía ya solo tiene una respuesta posible.
    /// </summary>
    public static bool SoloLoDeclarado => true;

    /// <summary>
    /// Lo que UNA PERSONA ha corregido a mano en esta app: qué salida va a qué nivel.
    /// </summary>
    /// <remarks>
    /// Es la memoria de las correcciones, y se le devuelve al maestro antes de que vuelva a mirar
    /// esa aplicación. Corregir una vez y que al día siguiente lo vuelva a fallar no es corregir,
    /// es repetirse — la misma razón por la que el nivel puesto a mano no lo mueve la deducción.
    ///
    /// Solo lo humano. Lo que el propio maestro fijó no cuenta: devolvérselo sería confirmarse a sí
    /// mismo y convertir un error de ayer en doctrina (2026-08-06).
    ///
    /// Se agrupa por etiqueta porque una misma salida aparece muchas veces —el panel lateral está
    /// en cada pantalla— y la corrección es de la salida, no de dónde se estaba al hacerla.
    /// </remarks>
    public IReadOnlyList<(string Etiqueta, int Nivel)> CorreccionesDe(string app)
    {
        string a = app.Trim();
        if (a.Length == 0) return Array.Empty<(string, int)>();
        return _ensenanzas.TryGetValue(a, out var d)
            ? d.Where(kv => kv.Value.Humano && !kv.Value.Atras && kv.Value.Nivel >= 0)
              .Select(kv => (kv.Value.Etiqueta.Length > 0 ? kv.Value.Etiqueta : kv.Key, kv.Value.Nivel))
               .OrderBy(x => x.Item2).ThenBy(x => x.Item1, StringComparer.CurrentCultureIgnoreCase)
               .ToList()
            : Array.Empty<(string, int)>();
    }

    /// <summary>
    /// LO ENSEÑADO NO ES TERRENO. Qué salidas de cada app son de qué nivel, dicho por una persona.
    /// </summary>
    /// <remarks>
    /// Vive fuera del grafo y sobrevive a borrarlo, porque no es lo mismo: el grafo es por dónde se
    /// ha pasado en ESTA máquina —terreno, y se tira sin pena— y esto es qué es la navegación
    /// permanente de una aplicación, que vale igual mañana, en otro equipo y para cualquiera que
    /// use esa app. Borrar el mapa y perder la jerarquía obligaba a volver a enseñar lo mismo cada
    /// vez que se quería una prueba limpia (2026-08-06, pedido por el usuario).
    ///
    /// Es la misma frontera que ya rige en graphify: se comparten las reglas, nunca el terreno.
    /// Guardarlo aparte es lo que permitirá algún día subirlo — enseñar una app una vez y que
    /// sirva para todos.
    /// </remarks>
    /// <summary>Una jerarquía aprendida: a qué nivel va, y si lo dijo una persona o el maestro.</summary>
    /// <remarks>
    /// Se guardan LAS DOS —lo que enseña el maestro también es aprendizaje, y volver a
    /// preguntárselo cuesta tiempo y dinero por algo que ya acertó— pero se recuerda de quién vino,
    /// porque solo lo humano se le devuelve después: devolverle lo suyo sería confirmarse a sí
    /// mismo (2026-08-06).
    /// </remarks>
    /// <summary>
    /// CROMO es una propiedad, no un sinónimo de «nivel 1».
    ///
    /// Hasta ahora azul y primer nivel eran lo mismo, y una página web lo desmintió: tenía una
    /// barra de cromo en el primer nivel Y, dentro de cada sección, otra barra de cromo de segundo
    /// nivel que cambia el contenido del tercero. Ser cromo —navegación persistente dentro de su
    /// ámbito— puede darse en cualquier nivel; el nivel dice DÓNDE vive, el cromo dice QUÉ ES
    /// (2026-08-07, observado por el usuario). Por compatibilidad, declarar nivel 1 sigue
    /// marcando cromo salvo que se diga lo contrario.
    /// </summary>
    /// <summary>
    /// Lo enseñado de una salida. <paramref name="Selector"/> es lo que la hace ÚNICA cuando su
    /// nombre no lo es.
    ///
    /// Sin él, dos salidas que se llaman igual comparten enseñanza a la fuerza. Lo midió el
    /// arquitecto en el explorador (2026-08-10): en UNA sola pantalla, el panel lateral salía con
    /// «Documentos» e «Imágenes» en nivel 1 y «Descargas», «Música» y «Videos» en nivel 2 — siendo
    /// hermanos del mismo árbol. El panel duplica nombres (Acceso rápido y OneDrive tienen cada
    /// uno su «Escritorio», su «Documentos») y, resolviendo por etiqueta, cada declaración pisaba
    /// todas las apariciones: ganaba la última escritura.
    ///
    /// Su consecuencia operativa, que es la que importa: «el navegador buscará ruta hacia Música
    /// creyéndola de nivel 2 cuando está a un clic desde cualquier sitio».
    /// </summary>
    /// <remarks>
    /// <paramref name="Etiqueta"/> viaja aparte de la clave porque desde que la clave es el
    /// SELECTOR, la clave dejó de ser legible. Y quien pregunta «qué se enseñó de esta app» quiere
    /// el nombre que se ve en pantalla, no un selector — lo cazó el contrato en cuanto cambió la
    /// clave: la promesa nº2 empezó a fallar porque devolvía selectores donde promete etiquetas
    /// (2026-08-10). Cambiar cómo se guarda algo no puede cambiar lo que se promete de ello.
    /// </remarks>
    /// <remarks>
    /// <paramref name="Kind"/> viaja aquí por la misma razón que el nivel: es una DECISIÓN de quien
    /// audita, y una decisión tomada no puede volver a preguntarse. Antes, marcar algo como acción
    /// escribía en las aristas que existían en ese instante y se olvidaba; entrar en una pantalla
    /// nueva devolvía el mismo mobiliario a la lista de pendientes. Medido: «Retroceder poco» salía
    /// clasificada en /inicio y sin clasificar en las otras cinco pantallas, y como la lista agrupa
    /// por etiqueta, una sola aparición sin marcar devolvía las seis (2026-08-10). Vaciar la lista
    /// costaba O(controles × pantallas), y en un explorador el número de pantallas es el número de
    /// carpetas del disco: el criterio de terminado era inalcanzable por construcción.
    ///
    /// Y por vivir aquí y no en el terreno, SOBREVIVE A BORRAR EL GRAFO: mientras estuvo solo en
    /// <c>EdgeInfo.KindDeclarado</c>, clasificar cuarenta salidas y limpiar el terreno dejaba el
    /// trabajo en nada. Un aprendizaje que no sobrevive al terreno no es un aprendizaje.
    /// </remarks>
    public sealed record Ensenanza(int Nivel, bool Humano, bool Atras = false, bool Cromo = false,
        string Selector = "", string Etiqueta = "", string Kind = "");

    /// <summary>
    /// ¿Este control es el gesto de VOLVER de su app?
    /// </summary>
    /// <remarks>
    /// El atrás no es constante: puedes venir de cualquier parte, así que a dónde lleva depende del
    /// historial, no de la estructura. Una arista acuñada al pulsarlo —«vercel → graph»— afirma una
    /// jerarquía que no existe, y fue lo que hizo saltar a «vercel» a la altura de su padre en el
    /// dibujo (2026-08-07, observado por el usuario). Se reconocen los modismos universales y,
    /// para el resto de aplicaciones, lo que el maestro haya señalado como atrás al enseñarlas.
    /// </remarks>
    public bool EsGestoDeAtras(string app, string label, string selector)
    {
        if (EsRelativo(selector)) return true;   // backButton, upButton, forwardButton
        string l = label.Trim();
        if (l.Equals("Atrás", StringComparison.OrdinalIgnoreCase)
            || l.Equals("Back", StringComparison.OrdinalIgnoreCase)
            || l.Equals("Volver", StringComparison.OrdinalIgnoreCase)
            || l.Equals("Adelante", StringComparison.OrdinalIgnoreCase)
            || l.Equals("Forward", StringComparison.OrdinalIgnoreCase)) return true;
        return _ensenanzas.TryGetValue(app, out var d) && d.TryGetValue(l, out var e) && e.Atras;
    }

    /// <summary>El maestro (o una persona) dice cuál es el botón de volver de esta app.</summary>
    public void AprenderAtras(string app, string etiqueta, bool humano)
    {
        if (app.Length == 0 || etiqueta.Length == 0) return;
        if (!_ensenanzas.TryGetValue(app, out var d))
            _ensenanzas[app] = d = new Dictionary<string, Ensenanza>(StringComparer.OrdinalIgnoreCase);
        d[etiqueta] = new Ensenanza(-1, humano || (d.TryGetValue(etiqueta, out var ya) && ya.Humano), Atras: true);
        GuardarEnsenanzas();
        LogBus.Log("mapa", $"«{etiqueta}» es el gesto de volver de «{app}»: no acuñará aristas");
    }

    private readonly Dictionary<string, Dictionary<string, Ensenanza>> _ensenanzas =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>TODO lo enseñado de una app —persona y maestro—, etiqueta a etiqueta.</summary>
    /// <remarks>A diferencia de <see cref="CorreccionesDe"/> (solo lo humano, para devolvérselo al
    /// maestro sin que se confirme a sí mismo), esto es para DIBUJAR y NAVEGAR: ahí da igual quién
    /// lo dijo — los dos describen la estructura.</remarks>
    public IReadOnlyList<(string Etiqueta, int Nivel)> EnsenanzasDe(string app) =>
        _ensenanzas.TryGetValue(app.Trim(), out var d)
            ? d.Where(kv => !kv.Value.Atras && kv.Value.Nivel >= 0)
              .Select(kv => (kv.Value.Etiqueta.Length > 0 ? kv.Value.Etiqueta : kv.Key, kv.Value.Nivel)).ToList()
            : Array.Empty<(string, int)>();

    /// <summary>Las apps con jerarquía enseñada, para poder verlas y borrarlas por separado.</summary>
    public IReadOnlyList<(string App, int Cuantas, int DeHumano)> AppsConJerarquia() =>
        _ensenanzas.Where(kv => kv.Value.Count > 0)
            .Select(kv => (kv.Key, kv.Value.Count, kv.Value.Count(x => x.Value.Humano)))
            .OrderBy(x => x.Key, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>Olvida lo enseñado de UNA app. Lo demás no se toca.</summary>
    public int OlvidarJerarquiaDe(string app)
    {
        if (!_ensenanzas.TryGetValue(app, out var d)) return 0;
        int n = d.Count;
        _ensenanzas.Remove(app);
        GuardarEnsenanzas();

        // Y se sueltan las aristas vivas de esa app: si no, el grafo en memoria seguiría
        // afirmando un nivel que ya nadie sostiene.
        //
        // SE SUELTA TODO LO DECLARADO, no solo el sello. Antes se quitaban NivelFijado y PorPersona
        // pero se dejaban el NÚMERO y el CROMO, así que tras «olvidar» el grafo seguía dibujando
        // exactamente los mismos niveles — solo que ya sin nadie que los sostuviera. Olvidar a
        // medias es peor que no olvidar: deja afirmaciones huérfanas que parecen deducidas
        // (2026-08-10, lo señaló el usuario preguntando si esto no debería estar conectado).
        //
        // También se va la clasificación DECLARADA (acción). Es parte de lo que se aprendió de esta
        // app; la deducida por el sistema se queda, porque esa no la dijo nadie.
        foreach (var e in Edges())
        {
            if (!AppDe(e.From).Equals(app, StringComparison.OrdinalIgnoreCase)) continue;
            e.Info.NivelFijado = false;
            e.Info.PorPersona = false;
            e.Info.NivelNav = -1;
            e.Info.EsCromo = false;
            e.Info.KindDeclarado = "";
        }
        // Y las profundidades, que salían de esos niveles, dejan de tener en qué apoyarse.
        foreach (var (id, nodo) in _nodes)
            if (AppDe(id).Equals(app, StringComparison.OrdinalIgnoreCase) && nodo.Nivel > 0) nodo.Nivel = -1;
        Version++;
        Save();
        LogBus.Log("mapa", $"olvidada la jerarquía enseñada de «{app}»: {n} salida(s)");
        return n;
    }

    private static string RutaEnsenanzas =>
        System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, "jerarquias-ensenadas.json");

    /// <summary>
    /// Le repone a una arista el nivel que se enseñó para su etiqueta. LA ÚNICA respuesta.
    /// </summary>
    /// <remarks>
    /// Una arista nace por TRES vías —observarla, cruzarla a propósito, y la transición que se ve
    /// pasar al navegar a mano— y la reposición estaba copiada en dos de ellas y ausente en la
    /// tercera, que es justo la que usa una persona paseando. El diagnóstico lo dijo sin ambigüedad:
    /// «NINGÚN nodo llega declarado» con diecisiete enseñadas en disco (2026-08-06). Una pregunta,
    /// una respuesta, un sitio; y quien cree una arista nueva, que llame aquí.
    /// </remarks>
    private void AplicarEnsenanza(string desde, EdgeInfo e)
    {
        if (e.Label.Length == 0) return;
        if (!_ensenanzas.TryGetValue(AppDe(desde), out var sabidas)) return;

        // PRIMERO POR SELECTOR, que es lo que identifica. Solo se cae a la etiqueta cuando la
        // enseñanza no traía selector — lo aprendido antes de este cambio sigue funcionando, y lo
        // nuevo deja de confundir dos salidas que se llaman igual.
        Ensenanza? ens = null;
        if (e.Selector.Length > 0 && sabidas.TryGetValue(e.Selector, out var porSel)) ens = porSel;
        else if (sabidas.TryGetValue(e.Label, out var porEtq) && porEtq.Selector.Length == 0) ens = porEtq;

        if (ens == null || ens.Atras) return;

        if (ens.Nivel >= 0)
        {
            e.NivelNav = ens.Nivel;
            e.NivelFijado = true;
            e.PorPersona = e.PorPersona || ens.Humano;
            e.EsCromo = ens.Cromo;
        }

        // Y LA CLASIFICACIÓN, que es la otra mitad de lo mismo. Va aparte del nivel porque se
        // declaran por separado: una acción no tiene nivel y no por eso deja de estar decidida.
        if (ens != null && !ens.Atras && ens.Kind.Length > 0) e.KindDeclarado = ens.Kind;
    }

    /// <summary>
    /// Guardar lo enseñado. LA CLAVE ES EL SELECTOR cuando se conoce, y la etiqueta solo cuando no.
    ///
    /// Así dos salidas que se llaman igual dejan de compartir enseñanza: cada una tiene su entrada
    /// y su nivel. Con la etiqueta por clave, declarar una pisaba a la otra y el árbol salía con
    /// hermanos en niveles distintos (ver <see cref="Ensenanza"/>).
    /// </summary>
    private void Aprender(string app, string etiqueta, int nivel, bool humano, bool cromo = false,
        string selector = "")
    {
        if (app.Length == 0 || etiqueta.Length == 0) return;
        if (!_ensenanzas.TryGetValue(app, out var d))
            _ensenanzas[app] = d = new Dictionary<string, Ensenanza>(StringComparer.OrdinalIgnoreCase);

        string clave = selector.Length > 0 ? selector : etiqueta;
        d.TryGetValue(clave, out var ya);

        // LAS DOS DECLARACIONES COMPARTEN ENTRADA Y NO SE PISAN. Nivel y clasificación son cosas
        // distintas dichas sobre la misma salida, y se dicen en momentos distintos: soltar el nivel
        // no puede borrar que alguien ya decidió que eso era una acción, ni al revés.
        if (nivel < 0)
        {
            // Soltar el nivel deja la entrada SOLO si aún dice algo — si no, sobra.
            if (ya != null && ya.Kind.Length > 0)
                d[clave] = ya with { Nivel = -1, Cromo = false };
            else d.Remove(clave);
        }
        else d[clave] = new Ensenanza(nivel,
            humano || (ya?.Humano ?? false),
            Atras: false, Cromo: cromo, Selector: selector, Etiqueta: etiqueta,
            Kind: ya?.Kind ?? "");
        GuardarEnsenanzas();
    }

    /// <summary>
    /// Alguien decide QUÉ ES esta salida —navegación, acción— para toda la app, no para la pantalla
    /// desde la que lo dijo. Espejo exacto de <see cref="FijarNivel"/>: toca lo que hay delante y
    /// deja la enseñanza para lo que venga.
    /// </summary>
    public string ClasificarSalida(string app, string etiquetaOSelector, string kind)
    {
        string a = app.Trim();
        string q = etiquetaOSelector.Trim();
        string k = kind.Trim().ToLowerInvariant();
        if (a.Length == 0 || q.Length == 0) return "falta la app o qué salida clasificar";

        var tocadas = Edges().Where(e =>
                AppDe(e.From).Equals(a, StringComparison.OrdinalIgnoreCase)
                && (e.Info.Label.Equals(q, StringComparison.OrdinalIgnoreCase)
                    || e.Info.Selector.Equals(q, StringComparison.Ordinal)))
            .ToList();
        if (tocadas.Count == 0) return $"no encuentro ninguna salida «{q}» en «{a}»";

        // POR SELECTOR, que es lo que identifica. Y de paso resuelve las etiquetas plantilla: las
        // cinco «Actualizar "X" (F5)» —una por carpeta visitada— son un solo control, y ya
        // compartían uia:aid=refreshButton;ct=Button. Clasificar una las clasifica todas.
        foreach (var sel in tocadas.Select(t => t.Info.Selector).Distinct(StringComparer.Ordinal))
        {
            if (sel.Length == 0) continue;
            foreach (var (_, _, info) in tocadas.Where(t =>
                string.Equals(t.Info.Selector, sel, StringComparison.Ordinal)))
                info.KindDeclarado = k;
            AprenderClase(a, tocadas.First(t => t.Info.Selector == sel).Info.Label, k, sel);
        }
        // Una salida sin selector solo puede declararse por su nombre: es todo lo que tiene.
        foreach (var (_, _, info) in tocadas.Where(t => t.Info.Selector.Length == 0))
        {
            info.KindDeclarado = k;
            AprenderClase(a, info.Label, k, "");
        }

        Version++;
        Save();
        int cuantas = tocadas.Count;
        return $"«{tocadas[0].Info.Label}» queda clasificada como «{k}» en «{a}» "
             + $"({cuantas} aparición/es). Queda APRENDIDA: las apariciones que salgan en otras "
             + "pantallas nacerán ya clasificadas, no hay que repetirlo.";
    }

    public void AprenderClase(string app, string etiqueta, string kind, string selector)
    {
        if (app.Length == 0 || (etiqueta.Length == 0 && selector.Length == 0)) return;
        if (!_ensenanzas.TryGetValue(app, out var d))
            _ensenanzas[app] = d = new Dictionary<string, Ensenanza>(StringComparer.OrdinalIgnoreCase);

        string clave = selector.Length > 0 ? selector : etiqueta;
        if (d.TryGetValue(clave, out var ya))
            d[clave] = ya with { Kind = kind, Etiqueta = etiqueta };
        else
            d[clave] = new Ensenanza(-1, Humano: false, Atras: false, Cromo: false,
                Selector: selector, Etiqueta: etiqueta, Kind: kind);
        GuardarEnsenanzas();
    }

    private void GuardarEnsenanzas()
    {
        try { File.WriteAllText(RutaEnsenanzas, JsonSerializer.Serialize(_ensenanzas)); }
        catch (Exception e) { LogBus.Log("mapa", $"no se pudieron guardar las jerarquías: {e.Message}"); }
    }

    private void CargarEnsenanzas()
    {
        try
        {
            if (!File.Exists(RutaEnsenanzas)) return;
            var d = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Ensenanza>>>(
                File.ReadAllText(RutaEnsenanzas));
            if (d == null) return;
            foreach (var kv in d)
                _ensenanzas[kv.Key] = new Dictionary<string, Ensenanza>(
                    // Lo guardado antes de que el cromo fuera una propiedad no trae el campo, pero
                    // entonces nivel 1 SIGNIFICABA cromo: se repone al leer, no se pierde el azul.
                    kv.Value.ToDictionary(x => x.Key,
                        x => x.Value is { Nivel: 1, Atras: false, Cromo: false } v
                            ? v with { Cromo = true } : x.Value),
                    StringComparer.OrdinalIgnoreCase);
            LogBus.Log("mapa", $"jerarquías enseñadas: {_ensenanzas.Sum(x => x.Value.Count)} en {_ensenanzas.Count} app(s)");
        }
        catch (Exception e) { LogBus.Log("mapa", $"no se pudieron leer las jerarquías: {e.Message}"); }
    }

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

        // AQUÍ YA NO SE DEDUCE NADA. El cromo salía de dos sitios a la vez —lo DECLARADO y lo
        // CONTADO— y viéndolo en pantalla no había forma de saber cuál de los dos puso cada punto
        // azul, ni por tanto de saber si la enseñanza funcionaba (2026-08-06). Se silenció con un
        // interruptor mientras se medía; el 2026-08-08 el usuario pidió eliminarla del todo, y ya
        // no hace falta: entre la persona, el maestro y los landmarks de la propia página, todo lo
        // que es mobiliario acaba DECLARADO por alguien que lo sabe.
        //
        // Lo que sigue debajo llena la lista SOLO con lo declarado.
        var cromo = new List<Hop>();

        // LO DICHO A MANO NO ESPERA AL CONTADOR. El umbral de «visto desde dos pantallas» existe
        // para DEDUCIR qué es mobiliario fijo cuando nadie lo ha dicho. Cuando alguien —el usuario
        // señalando, o el maestro mirando la pantalla— declara que algo es del primer nivel, ya
        // está dicho: hacerle esperar a que el contador llegue a dos es pedir pruebas de algo que
        // acaban de afirmar.
        //
        // Esto es lo que faltaba para que «ponlo en el primer nivel» signifique algo. Antes solo
        // pintaba el punto de azul y congelaba un número: si además acababa siendo accesible desde
        // todas partes era porque el contador había llegado a dos por su cuenta, no por haberlo
        // dicho (2026-08-05, visto por el usuario: «los azules aún quedan en segundo nivel»).
        var yaEsta = new HashSet<string>(cromo.Select(h => h.Info.Selector), StringComparer.Ordinal);
        foreach (var (from, to, info) in Edges())
        {
            // POR LA PROPIEDAD, NO POR EL NIVEL. Aquí decía «NivelNav != 1»: el último sitio donde
            // cromo y primer nivel seguían siendo sinónimos, después de que una web con barra fija
            // dentro de cada sección demostrara que son cosas distintas (2026-08-07, pedido por el
            // usuario). El nivel dice DÓNDE vive la puerta; el cromo dice que TE SIGUE. Lo declarado
            // de nivel 1 sigue entrando —FijarNivel marca EsCromo por defecto en ese caso y la
            // carga repone el campo en lo guardado antiguo— pero ahora un cromo de nivel 2 también.
            if (!info.NivelFijado || !info.EsCromo) continue;
            if (info.Selector.Length == 0 || info.Label.Length == 0) continue;
            if (!AppDe(from).Equals(app, StringComparison.OrdinalIgnoreCase)) continue;
            if (!yaEsta.Add(info.Selector)) continue;

            // Se prefiere la aparición que SÍ sabe a dónde lleva: una puerta sin cruzar sigue
            // valiendo como mobiliario —«existe y está en todas partes»— pero no como ruta.
            var conDestino = Edges().FirstOrDefault(e =>
                string.Equals(e.Info.Selector, info.Selector, StringComparison.Ordinal)
                && !EsPuerta(e.To) && AppDe(e.To).Equals(app, StringComparison.OrdinalIgnoreCase));
            cromo.Add(conDestino.Info != null
                ? new Hop(conDestino.From, conDestino.To, conDestino.Info)
                : new Hop(from, to, info));
        }
        // Y LO QUE NADIE DECLARÓ PERO EL BRONCE DEMUESTRA. Aquí vivía una deducción por conteo que
        // se eliminó el 2026-08-08 con razón: contaba «visto desde tres pantallas» sin guardar de
        // dónde salía el número, así que en pantalla no había forma de saber si un punto azul lo
        // había puesto una persona o la estadística, y llegaba a contradecir a fuentes mejores.
        //
        // Lo que vuelve NO es aquello. Es la misma pregunta contestada por `Plata`, que además
        // guarda la evidencia («visto en 9 de 11 pantallas observadas»), descarta por construcción
        // lo que tiene destino variable —un «Subir» está en todas partes y no lleva al mismo sitio—
        // y nunca pisa lo declarado: esto corre DESPUÉS y solo rellena lo que nadie dijo.
        //
        // Y es lo que hace que la plata deje de ser cosmética. Mientras el mobiliario solo llegara
        // declarado, borrar la plata entera no rompía nada: era una capa de dibujo. Desde aquí, un
        // grafo sin plata pierde los atajos y `Route` vuelve a decir «no sé llegar» a un sitio que
        // está a un clic (promesa 16 del contrato).
        foreach (var s in Plata.DerivadaDe(this, app).SalidasPorSelector.Values
                     .Where(s => s.Clase == Plata.Clase.Cromo)
                     .OrderBy(s => s.Selector, StringComparer.Ordinal))
        {
            if (s.Selector.Length == 0 || !yaEsta.Add(s.Selector)) continue;
            if (!destinoDe.TryGetValue(s.Selector, out var hop)) continue;   // sin destino no es ruta
            cromo.Add(hop);
        }

        if (_cromoVersion != Version) { _cromo.Clear(); _cromoVersion = Version; }
        _cromo[app] = cromo;
        return cromo;
    }

    /// <summary>Nombre del nivel que el sistema deduce solo: lo que está en todas las pantallas.</summary>
    public const string NivelCromo = "cromo";

    /// <summary>Copia de una arista marcada como heredada del nivel. Copia y no la misma: escribir
    /// en el original convertiría en «heredada» la arista real de la pantalla donde sí se cruzó.</summary>
    /// <remarks>
    /// EL NIVEL VIAJA CON LA COPIA. Se copiaba todo menos <see cref="EdgeInfo.NivelNav"/> y
    /// <see cref="EdgeInfo.NivelFijado"/>, así que lo heredado llegaba a cada pantalla con nivel
    /// −1: el sitio donde se enseñó lo sabía y ninguno de los demás. Y desde que lo DECLARADO entra
    /// precisamente por aquí, el maestro fijaba quince elementos al primer nivel y no se ponía
    /// azul ni uno —lo aplicado era cierto y lo que se dibujaba, otra cosa (2026-08-06, observado
    /// por el usuario). Un nivel que no viaja con la salida no es un nivel de la app.
    /// </remarks>
    private static EdgeInfo Heredada(EdgeInfo o) => new()
    {
        Count = o.Count, Selector = o.Selector, Label = o.Label, ControlType = o.ControlType,
        Alternatives = o.Alternatives, ClickPos = o.ClickPos, Explored = o.Explored,
        ActionType = o.ActionType, Kind = o.Kind, Nivel = NivelCromo,
        NivelNav = o.NivelNav, NivelFijado = o.NivelFijado, EsCromo = o.EsCromo, PorPersona = o.PorPersona,
        VistaPorUltimaVez = o.VistaPorUltimaVez,
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

    /// <summary>
    /// UNA ARISTA, SOLO CON LO QUE SE OBSERVÓ. Es lo que se ESCRIBE en el terreno desde el
    /// 2026-08-10; lo que se LEE sigue siendo <see cref="EdgeInfo"/> entero, y esa asimetría es
    /// deliberada — ver <see cref="Save"/> y la cosecha de <see cref="Load"/>.
    ///
    /// Faltan a propósito `NivelNav`, `NivelFijado`, `PorPersona`, `EsCromo` y `KindDeclarado`:
    /// no son terreno, son lo que alguien afirmó DESPUÉS. Vivían aquí y el resultado era que el
    /// archivo del bronce contenía plata, que ninguna de las dos etapas se podía recomputar por
    /// separado, y que borrar el grafo se llevaba por delante clasificaciones que nadie había
    /// vuelto a guardar. Ahora viven en `jerarquias-ensenadas.json`, que ya existía para esto y ya
    /// sobrevive al borrado, y se reponen al cargar.
    ///
    /// NO se sube <see cref="SchemaVersion"/> por este cambio, y conviene decir por qué: la
    /// migración de versión de este archivo PURGA las acciones de todas las aristas —es su
    /// naturaleza desde la v2— y aquí no hay nada que purgar. Los datos viejos se siguen leyendo,
    /// se cosechan a la capa de overrides y el archivo queda limpio solo, en el primer guardado.
    /// Subir la versión habría destruido el trabajo de todos para arreglar una mezcla de campos.
    /// </summary>
    private sealed record EdgeBronce(
        int Count, string Selector, string Label, string ControlType,
        string[] Alternatives, string ClickPos, bool Explored, string Nivel,
        string ActionType, string Kind, DateTime VistaPorUltimaVez);

    public static SurfaceMap Load()
    {
        var map = new SurfaceMap();
        map.CargarEnsenanzas();   // el aprendizaje se lee aunque no haya terreno que leer
        try
        {
            if (File.Exists(Path))
            {
                var s = JsonSerializer.Deserialize<Stored>(File.ReadAllText(Path));
                if (s != null)
                {
                    foreach (var kv in s.Nodes) map._nodes[kv.Key] = kv.Value;
                    foreach (var kv in s.Edges) map._edges[kv.Key] = kv.Value;

                    // LO DECLARADO QUE VENGA EN EL TERRENO SE COSECHA ANTES DE PERDERSE.
                    //
                    // Desde el 2026-08-10 el terreno se escribe SIN los campos declarados (ver
                    // EdgeBronce), pero se sigue leyendo el objeto entero: los archivos guardados
                    // antes los traen dentro, y unos pocos pueden no tener enseñanza que los
                    // reponga —declaraciones anteriores a que `Aprender` guardara el selector, o
                    // hechas por vías que ya no existen—. Se pasan a la capa de overrides aquí, una
                    // vez, y el primer guardado deja el archivo limpio.
                    //
                    // Es una migración que no destruye nada, que es justo lo que la migración por
                    // SchemaVersion no podía ofrecer: la suya purga acciones.
                    int cosechadas = 0;
                    foreach (var (from, _, info) in map.Edges())
                    {
                        string app = AppDe(from);
                        if (app.Length == 0 || info.Label.Length == 0) continue;
                        string clave = info.Selector.Length > 0 ? info.Selector : info.Label;
                        bool yaSabida = map._ensenanzas.TryGetValue(app, out var d) && d.ContainsKey(clave);
                        if (yaSabida) continue;

                        if (info.NivelFijado && info.NivelNav >= 0)
                        {
                            map.Aprender(app, info.Label, info.NivelNav, info.PorPersona, info.EsCromo, info.Selector);
                            cosechadas++;
                        }
                        if (info.KindDeclarado.Length > 0)
                        {
                            map.AprenderClase(app, info.Label, info.KindDeclarado, info.Selector);
                            cosechadas++;
                        }
                    }
                    if (cosechadas > 0)
                        LogBus.Log("mapa", $"{cosechadas} declaración(es) que vivían en el terreno pasan "
                            + "a la capa de enseñanzas: el bronce se queda solo con lo observado");

                    // Y AL CARGAR SE SANA TODO: da igual por qué vía nació cada arista o con qué
                    // versión del código — al arrancar, toda arista cuya etiqueta esté enseñada
                    // recibe su nivel. Es la red de seguridad de las tres vías de nacimiento.
                    foreach (var (from, _, info) in map.Edges()) map.AplicarEnsenanza(from, info);

                    // Las aristas del gesto de VOLVER se purgan: las acuñadas antes de que la regla
                    // existiera siguen en el mapa afirmando jerarquías que no son («vercel→graph»).
                    var deAtras = map._edges
                        .Where(kv => kv.Value.Label.Length > 0 && kv.Key.IndexOf('\n') > 0
                                  && map.EsGestoDeAtras(AppDe(kv.Key[..kv.Key.IndexOf('\n')]),
                                                        kv.Value.Label, kv.Value.Selector))
                        .Select(kv => kv.Key).ToList();
                    foreach (var k in deAtras) map._edges.Remove(k);
                    if (deAtras.Count > 0)
                        LogBus.Log("mapa", $"purgadas {deAtras.Count} arista(s) del gesto de volver: "
                                         + string.Join(" · ", deAtras.Take(10).Select(k =>
                                             k.Replace("\n", "→").Replace("uia://explorer.exe/", ""))));

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
            // Se escribe BRONCE, no el objeto en memoria: ver EdgeBronce. Lo declarado ya está en
            // la capa de enseñanzas —FijarNivel y AprenderClase la escriben siempre— y vuelve solo
            // al cargar, así que esto no pierde nada: deja de duplicarlo donde no le toca.
            var bronce = new Dictionary<string, EdgeBronce>(_edges.Count, StringComparer.Ordinal);
            foreach (var kv in _edges)
                bronce[kv.Key] = new EdgeBronce(
                    kv.Value.Count, kv.Value.Selector, kv.Value.Label, kv.Value.ControlType,
                    kv.Value.Alternatives, kv.Value.ClickPos, kv.Value.Explored, kv.Value.Nivel,
                    kv.Value.ActionType, kv.Value.Kind, kv.Value.VistaPorUltimaVez);

            File.WriteAllText(Path, JsonSerializer.Serialize(new
            {
                Nodes = _nodes,
                Edges = bronce,
                Version = SchemaVersion,
            }));
            _dirty = 0;
        }
        catch (Exception e) { LogBus.Log("mapa", $"no se pudo guardar el mapa: {e.Message}"); }
    }
}
