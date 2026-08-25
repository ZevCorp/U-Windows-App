using Mapeador;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Navigation;

/// <summary>
/// EL PUENTE del mapeador al núcleo. Y nada más que eso.
///
/// LA FRONTERA, que es el motivo de que esta clase exista y sea tan corta (2026-08-12, insistido
/// por el usuario hasta que quedó claro):
///
///   · El MAPEADOR es todo lo que lee la pantalla: UIA, el localizador de superficies, las pestañas
///     del navegador, el crawler. Es imperfecto y está en desarrollo —hoy no distingue bien dos
///     pestañas del mismo sitio— y eso ESTÁ BIEN, porque no es el núcleo.
///   · El NÚCLEO es el grafo, vive en `nucleo/Grafo` y no conoce a nadie de aquí. Contesta una sola
///     pregunta: qué es alcanzable desde donde estoy, y a dónde llevó cada cosa cuando se cruzó.
///
/// Esta clase solo traduce de uno a otro. Lo que se ve en Neo4j es EL NÚCLEO, no un dibujo que el
/// cliente se inventa por su cuenta: el intento anterior escribía Cypher desde aquí, con la
/// estructura del mapa viejo, y eso era un visor montado encima del núcleo que queríamos
/// reemplazar — ver lo de siempre creyendo que se veía lo nuevo.
///
/// La dirección de la dependencia es una sola y no se invierte nunca: cliente → núcleo. El día que
/// el núcleo necesite algo de aquí, será la señal de que se le está metiendo algo que no le toca.
/// </summary>
public sealed class MapaVivo : IDisposable
{
    private readonly Nucleo.Grafo _grafo = new();
    private readonly Nucleo.ProyectorNeo4j _proyector = new();
    private readonly Func<string> _donde;
    private readonly Func<IReadOnlyList<(string Selector, string Etiqueta, string Tipo)>> _loQueVeo;
    private System.Threading.Timer? _reloj;
    private System.Threading.Timer? _relojUbicacion;
    private string _anterior = "";

    /// <summary>
    /// EL AVISO POR EL LOG cuando se descartan demasiadas vueltas seguidas.
    /// </summary>
    /// <remarks>
    /// El candado en sí ya no vive aquí: es <see cref="Mapeador.VueltaUnica"/>, una pieza con nombre
    /// y con su propio contrato. Mientras fueron dos `Interlocked` sueltos dentro de esta clase de
    /// WPF, el invariante no se podía nombrar en una promesa ni probar sin levantar la app entera.
    ///
    /// Lo que queda aquí es lo único que es del cliente: DECIRLO. El candado impide que se apilen;
    /// esto hace VISIBLE que estamos pidiendo de más, que es lo que faltó el 2026-08-12 — el fallo
    /// no fue ser lento, fue ser lento en silencio.
    ///
    /// Se dice una vez por minuto como mucho: un aviso que sale cada vuelta es ruido, y el ruido se
    /// aprende a ignorar.
    /// </remarks>
    private int _descartadas;
    private DateTime _ultimoAviso = DateTime.MinValue;

    private void Descarte(bool esUbicacion = true)
    {
        // CADA RELOJ SE AFLOJA CON SUS PROPIOS DESCARTES. El parámetro llevaba aquí desde antes sin
        // usarse, y al colgar de aquí el ajuste del ritmo pasó a importar: una lectura de PANTALLA
        // que llega tarde estaba frenando el reloj de la UBICACIÓN, que es otro trabajo y otro
        // coste. Frenar el que sí llegaba a tiempo por culpa del que no, además, empeora justo lo
        // que peor lleva ir lento: la atribución del clic vive en el de ubicación (2026-08-16).
        if (esUbicacion) AflojarElPaso();
        if (Interlocked.Increment(ref _descartadas) < 20) return;
        if ((DateTime.UtcNow - _ultimoAviso).TotalSeconds < 60) return;
        _ultimoAviso = DateTime.UtcNow;
        int cuantas = Interlocked.Exchange(ref _descartadas, 0);
        // SE DICE CUÁL DE LOS DOS. Los dos relojes escriben aquí y el mensaje no los distinguía, así
        // que «SATURADO» podía ser la ubicación o la lectura de pantalla —trabajos distintos, costes
        // distintos y arreglos distintos— y no había forma de saberlo leyendo el log.
        LogBus.Log("mapa-vivo", $"SATURADO ({(esUbicacion ? "ubicación" : "lectura de pantalla")}): "
            + $"{cuantas} vuelta(s) descartadas por llegar con otra en curso"
            + (esUbicacion ? $"; la ubicación va ya cada {_msUbicacion} ms." : "."));

        // NO ES UNA QUEJA DE RENDIMIENTO. Una lectura descartada es TERRENO QUE NADIE MIRÓ, y lo que
        // no se miró no está en el grafo. Solo cuenta la de pantalla: descartar una vuelta de
        // ubicación no pierde elementos, solo retrasa saber dónde estamos.
        if (!esUbicacion)
            PulsoDelMapeador.Actual.Roto.Anotar(QueSeRompio.TerrenoNoLeido, _anterior ?? "",
                $"{cuantas} lectura(s) de pantalla descartadas por no dar abasto");
    }

    // ── El ritmo se ajusta solo ───────────────────────────────────────────────────────────────

    /// <summary>Lo más rápido que se mira dónde estamos. Es un suelo, no una promesa.</summary>
    private const int MinUbicacionMs = 250;

    /// <summary>
    /// El techo. Alto a propósito: hay pantallas que cuestan segundos de leer —una ventana de
    /// Electron con quinientos elementos, medido— y fingir que se puede preguntar cada segundo no
    /// las hace más rápidas: solo llena el grupo de hilos de vueltas que se van a tirar.
    /// </summary>
    private const int MaxUbicacionMs = 4000;

    private int _msUbicacion = MinUbicacionMs;

    /// <summary>Lo que cuesta de verdad localizar, suavizado. Es de dónde sale el ritmo.</summary>
    private double _costeTipico;

    /// <summary>
    /// SE AJUSTA SOLO PORQUE EL NÚMERO BUENO NO EXISTE.
    ///
    /// Este intervalo ya se corrigió una vez a mano —de 120 ms a 250, tras medir que apilar vueltas
    /// hacía cinco veces más lenta la lectura de una pantalla (2026-08-12)— y el propio aviso de
    /// saturación decía qué hacer si volvía a pasar: subirlo. Volvió a pasar, con 250, en otra
    /// máquina: 20 vueltas descartadas y un minuto después 59, o sea a peor (2026-08-16).
    ///
    /// Elegir otra constante solo mueve el problema al siguiente equipo, porque lo que cuesta
    /// localizar depende de la app que haya delante —una pantalla de SAP no cuesta lo que el
    /// escritorio— y eso cambia cada minuto, no cada instalación. Así que se afloja cuando se
    /// descarta y se aprieta cuando se va sobrado, y el ritmo lo pone la máquina.
    ///
    /// Se afloja de golpe y se aprieta despacio, a propósito: quedarse corto cuesta vueltas
    /// perdidas, y pasarse solo cuesta un poco de retraso.
    /// </summary>
    private void AflojarElPaso()
    {
        Ritmo(Math.Min(MaxUbicacionMs, _msUbicacion + _msUbicacion / 2), "descarté una vuelta");
    }

    /// <summary>
    /// EL RITMO SALE DEL COSTE MEDIDO, no de reaccionar a los descartes.
    ///
    /// Reaccionar solo al descarte no basta, y está medido: con el techo en 1000 ms se seguían
    /// tirando 35 de cada 60 vueltas, minuto tras minuto, porque localizar costaba ~2 s con una
    /// ventana de Electron delante (2026-08-16, log del usuario). El bucle se quedó pegado al techo
    /// sin poder salir: para volver a apretar hacen falta vueltas limpias, y no había ninguna.
    ///
    /// Ya se cronometra cada localización para el pulso, así que el número existía y solo había que
    /// usarlo. Se pide con un margen sobre lo que cuesta —no justo lo que cuesta— porque una vuelta
    /// que empieza exactamente cuando acaba la anterior no deja hueco a nada más en la máquina.
    ///
    /// Suavizado y no el último valor: el coste salta mucho entre una pantalla y otra, y perseguir
    /// cada salto cambiaría el reloj varias veces por segundo.
    /// </summary>
    private void AjustarAlCoste(long ms)
    {
        _costeTipico = _costeTipico <= 0 ? ms : _costeTipico * 0.8 + ms * 0.2;
        int quiero = (int)Math.Clamp(_costeTipico * 1.4, MinUbicacionMs, MaxUbicacionMs);

        // Solo si el cambio es grande: mover el reloj por un 5% es ruido, y cada cambio reinicia la
        // cuenta del temporizador.
        if (Math.Abs(quiero - _msUbicacion) * 100 / Math.Max(1, _msUbicacion) < 25) return;
        Ritmo(quiero, $"localizar cuesta ~{_costeTipico:N0} ms");
    }

    private void Ritmo(int ms, string porque)
    {
        if (ms == _msUbicacion) return;
        bool afloja = ms > _msUbicacion;
        _msUbicacion = ms;
        try { _relojUbicacion?.Change(_msUbicacion, _msUbicacion); } catch { }
        LogBus.Log("mapa-vivo", $"ubicación cada {_msUbicacion} ms ({(afloja ? "más lento" : "más rápido")}): {porque}");
    }

    /// <summary>
    /// EL NÚMERO DEL ÚLTIMO CLIC QUE YA SE USÓ. Un clic explica UNA transición y solo una.
    ///
    /// Sin esto, el mismo clic se atribuía dos veces y la segunda siempre era falsa. El patrón está
    /// en los logs con dos segundos de separación: pulsas «Datos adjuntos», se atribuye bien
    /// descargas→datos-adjuntos; pulsas «Escritorio» pero aún no se ha registrado, así que el
    /// último clic sigue siendo el anterior y se le atribuye TAMBIÉN datos-adjuntos→escritorio.
    /// Nace «pulsa Datos adjuntos para ir a Escritorio», que estando ya en Datos adjuntos no mueve
    /// nada — y el navegador se quedaba dando vueltas sobre ese tramo (2026-08-12, lo midió el
    /// usuario pidiendo ir a «facturas»).
    ///
    /// Por eso «funcionaba antes»: la primera atribución era correcta. La falsa entraba después.
    /// </summary>
    private int _clicYaUsado = -1;

    /// <summary>El último clic que ya se contó como «cambió la pantalla y no el sitio». Un clic
    /// cuenta una vez: si no, una pantalla que se refresca sola dispararía el contador sin parar.</summary>
    private int _clicYaContadoSinSitio = -1;

    /// <summary>CUÁNDO LLEGAMOS a la pantalla en la que estamos. Es lo que permite exigir que el clic
    /// que explica una salida sea POSTERIOR a la llegada: el que te trajo no puede ser el que te saca.</summary>
    private DateTime _llegadaAlAnterior = DateTime.MinValue;

    /// <summary>
    /// Quién sabe qué se acaba de pulsar. Sin esto el núcleo solo aprende «esto se ve aquí» y nunca
    /// «esto llevó allí», así que el grafo no se arma nunca: quedan islas sin caminos entre ellas.
    /// </summary>
    public ClickWatcher? Clics { get; set; }

    /// <summary>El núcleo, para que quien quiera preguntarle no tenga que pasar por aquí.</summary>
    public Nucleo.Grafo Nucleo => _grafo;

    /// <summary>
    /// Pulsar un elemento por su identidad, con las mismas manos que usa todo lo demás. Lo inyecta
    /// quien tiene el lector UIA; aquí solo se guarda para que la ventanita del núcleo pueda
    /// ejecutar lo que el núcleo decide.
    /// </summary>
    public Func<string, string, bool>? Pulsar { get; set; }

    public MapaVivo(Func<string> donde,
        Func<IReadOnlyList<(string Selector, string Etiqueta, string Tipo)>> loQueVeo)
    {
        _donde = donde;
        _loQueVeo = loQueVeo;
        _proyector.Cuenta = m => LogBus.Log("mapa-vivo", m);
    }

    /// <summary>
    /// Empieza a observar. Cada <paramref name="cadaMs"/> le cuenta al núcleo dónde estamos y qué
    /// hay delante; el núcleo decide si eso cambió algo, y solo entonces se repinta.
    /// </summary>
    public void Arrancar(int cadaMs = 900)
    {
        // LO PRIMERO ES ACORDARSE. Sin esto el núcleo arrancaba vacío y su primera proyección
        // —que borra y reescribe— se llevaba por delante todo lo mapeado en sesiones anteriores.
        // Reiniciar la app perdía el mapa entero, y no se notaba porque siempre limpiábamos a mano
        // antes de cada prueba (2026-08-12).
        int volvieron = _proyector.Restaurar(_grafo);
        if (volvieron > 0)
            LogBus.Log("mapa-vivo", $"memoria recuperada: {volvieron} ubicación(es) de sesiones anteriores");

        // DOS CADENCIAS, PORQUE SON DOS COSTES. Saber dónde estoy vale 32 ms; leer la pantalla
        // entera, 400 (medido el 2026-08-12). Con las dos en el mismo latido, lo barato heredaba la
        // lentitud de lo caro: el cambio de sitio tardaba más de un segundo en registrarse, y si se
        // navegaba rápido una ubicación intermedia no llegaba a verse — el camino quedaba grabado
        // como A→C cuando en realidad fue A→B→C.
        //
        // Ahora el DÓNDE se mira cuatro veces por segundo y el QUÉ HAY a su ritmo. La atribución
        // del clic vive con el dónde, que es lo que la hace fiable: cuanto antes se detecte el
        // salto, más fresco es el clic que lo explica.
        //
        // 250 ms y no 120: con 120 la vuelta siguiente llegaba antes de terminar la anterior y se
        // apilaban. El candado de reentrada lo impide de todas formas, pero pedir cuatro veces por
        // segundo algo que a veces cuesta 200 ms ya es pedir de más.
        _reloj?.Dispose();
        _relojUbicacion?.Dispose();
        _msUbicacion = MinUbicacionMs;
        _costeTipico = 0;
        _relojUbicacion = new System.Threading.Timer(_ => MirarDonde(), null, 200, _msUbicacion);
        _reloj = new System.Threading.Timer(_ => Latido(), null, 600, cadaMs);
        LogBus.Log("mapa-vivo", $"ubicación cada {_msUbicacion} ms (se ajusta sola entre "
            + $"{MinUbicacionMs} y {MaxUbicacionMs}) · pantalla cada {cadaMs} ms · proyectando en Neo4j");
    }

    /// <summary>
    /// LA MITAD BARATA, cuatro veces por segundo: dónde estamos, y qué nos trajo.
    ///
    /// La atribución del clic vive AQUÍ y no en el latido lento, y eso es lo que la hace fiable:
    /// cuanto antes se detecte el salto, más fresco es el clic que lo explica. Con las dos cosas
    /// juntas, entre el salto y la atribución se colaba la lectura de la pantalla entera.
    /// </summary>
    private void MirarDonde()
    {
        // NUNCA DOS A LA VEZ. Un System.Threading.Timer no espera a que termine la vuelta anterior:
        // si la lectura tarda más que el intervalo, ENCOLA la siguiente y se van apilando sobre el
        // grupo de hilos. Con 120 ms de intervalo eso saturó la máquina y arrastró a todo lo demás
        // — leer la Maqueta pasó de 0,4 s a 2,2 s, cinco veces más lento, sin que la app hubiera
        // cambiado (2026-08-12, lo noto el usuario y se midió).
        //
        // Se DESCARTA la vuelta que llega con otra en curso, no se encola: mirar dónde estás es una
        // pregunta cuya respuesta caduca, y contestarla tarde no vale de nada.
        if (!PulsoDelMapeador.Actual.Ubicacion.MeToca()) { Descarte(); return; }
        try
        {
            // SE CRONOMETRA DONDE OCURRE EL TRABAJO. Un cronómetro externo mide también su propio
            // coste: dos veces esta semana el instrumento engañó al que medía —un `docker exec` de
            // 1,7 s, y comparar Gmail con el explorador— y las dos estuvo a punto de sacarse la
            // conclusión contraria a la verdad.
            var crono = System.Diagnostics.Stopwatch.StartNew();
            string aqui = DondeEstoySinColgarme();
            PulsoDelMapeador.Actual.Costo("localizar", crono.ElapsedMilliseconds);
            // El mismo número que alimenta el pulso decide cada cuánto se vuelve a preguntar.
            AjustarAlCoste(crono.ElapsedMilliseconds);
            if (aqui.Length == 0) return;
            if (aqui.Equals(_anterior, StringComparison.OrdinalIgnoreCase)) return;

            var clic = Clics?.Last;
            var cuando = DateTime.UtcNow;

            // ¿CAMBIAMOS DE SITIO? Entonces algo nos trajo, y ese «algo» es el otro hecho que el
            // núcleo guarda. Se atribuye al ÚLTIMO CLIC si es reciente y salió de donde estábamos;
            // si no se puede saber, no se inventa: una arista con el elemento equivocado es peor
            // que ninguna, porque el asistente la usaría para volver y pulsaría otra cosa.
            if (_anterior.Length > 0 && !_anterior.Equals(aqui, StringComparison.OrdinalIgnoreCase))
            {
                // ¿ESTO FUE NAVEGACIÓN, SIQUIERA? Se pregunta ANTES que nada, y el orden importa:
                // «no era navegación» y «era navegación y no supimos explicarla» son cosas opuestas
                // y hasta hoy salían mezcladas en el mismo número.
                //
                // Un clic no te lleva a otra app. Si el destino es de otra aplicación, lo que pasó
                // fue un cambio de ventana —alt-tab, la barra de tareas, un clic fuera—. Rechazarlo
                // es el sistema PORTÁNDOSE BIEN: sin esta valla el grafo acuñó «pulsar Ajustes en la
                // Maqueta lleva a la terminal», que es falso y además peligroso (2026-08-12).
                //
                // Pero al preguntarlo el ÚLTIMO, un alt-tab con un clic viejo salía como «el clic
                // era viejo» y contaba como navegación fallida: Spotify aparecía con 0 de 2
                // explicados cuando sus dos saltos eran cambios de ventana correctamente rechazados,
                // y el porcentaje decía «mapeamos mal» donde el sistema no tenía nada que mapear
                // (2026-08-13, lo notó el usuario al ensuciarse su propia prueba).
                if (!global::Nucleo.Grafo.AppDe(_anterior)
                        .Equals(global::Nucleo.Grafo.AppDe(aqui), StringComparison.OrdinalIgnoreCase))
                {
                    PulsoDelMapeador.Actual.NoEraNavegacion(global::Nucleo.Grafo.AppDe(_anterior));
                    _anterior = aqui; _llegadaAlAnterior = cuando;
                    _grafo.Estoy(aqui);
                    _proyector.Proyectar(_grafo);
                    return;
                }

                double edad = clic == null ? -1 : (cuando - clic.When).TotalSeconds;
                bool reciente = clic != null && edad < 6;
                bool sinEstrenar = clic != null && clic.DownIndex != _clicYaUsado;

                // EL CLIC QUE TE TRAJO AQUÍ NO PUEDE SER EL QUE TE SACA. Para explicar una SALIDA,
                // el clic tiene que haber ocurrido DESPUÉS de que llegáramos.
                //
                // Es la valla que faltaba, y sin ella el navegador se quedó atascado de verdad
                // (2026-08-13, lo pidió el usuario ir a «documentos» y llegó a «datos-adjuntos»):
                //
                //   10:35:15  salto documentos→datos-adjuntos SIN atribuir («Documentos» ya explicó…)
                //   10:35:15  pulsado «Datos adjuntos» → datos-adjuntos
                //   10:35:18  aprendido: «Datos adjuntos» lleva de datos-adjuntos a escritorio  ← FALSA
                //
                // «Datos adjuntos» es el clic que nos METIÓ en datos-adjuntos. Como el salto de
                // entrada se rechazó por otro motivo, el clic quedó «sin estrenar» y el salto
                // SIGUIENTE se lo comió. `_clicYaUsado` no cubre esto: solo marca los clics que sí
                // llegaron a explicar algo, y este no explicó nada — precisamente por eso siguió
                // disponible para mentir.
                //
                // El grafo acuñó «datos-adjuntos --[Datos adjuntos]--> documentos», el navegador la
                // siguió fielmente, pulsó la carpeta en la que YA ESTABA, y no se movió nunca.
                bool despuesDeLlegar = clic != null
                    && AQuienSeLeDioClic.PuedeExplicarLaSalida(clic.When, _llegadaAlAnterior);
                // ¿EL CLIC OCURRIÓ DONDE ESTÁBAMOS? Solo se puede preguntar cuando la ubicación se
                // nombra por su PROCESO. Una superficie web se nombra por su DOMINIO —«chatgpt.com»,
                // no «chrome»— y comparar un dominio con un proceso no es una comprobación: es un
                // «no» garantizado. Así se tiraron cuatro navegaciones web reales seguidas, con el
                // sitio cambiando correctamente de es-419 a images a library a plugins, todas con el
                // motivo «el clic "Imágenes" fue en "chrome", no en donde estábamos» (2026-08-13; el
                // usuario avisó de que en Chrome no había hecho alt-tab, y tenía razón).
                //
                // Para lo web la valla que sirve es la de arriba —misma superficie, mismo dominio—,
                // que ya pasó. Preguntar además por el proceso no añadía seguridad: solo rechazaba.
                bool porProceso = _anterior.StartsWith("uia://", StringComparison.OrdinalIgnoreCase);
                bool salioDeAlli = clic != null
                    && (!porProceso
                        || global::Nucleo.Grafo.AppDe(_anterior)
                            .StartsWith(clic.Process, StringComparison.OrdinalIgnoreCase));

                // EL CLIC SE TRADUCE AL ELEMENTO QUE EL NÚCLEO CONOCE, buscándolo POR ETIQUETA
                // entre lo observado. Dos motivos, los dos medidos:
                //
                //  1. Hay dos vocabularios de identidad: el vigilante de clics describe con
                //     «uia:aid=…» y el observador con «uia:name=…». Pasarle el selector del clic
                //     guardaba el destino bajo una clave que ningún elemento observado tenía, y el
                //     camino quedaba huérfano e invisible (de diez aprendidos llegaron tres).
                //  2. El clic suele caer en el TEXTO de dentro del control, no en el control. Y ese
                //     texto es justo el que se descarta por duplicado. Así se perdió el camino a
                //     «Lote A1-2»: el núcleo no conocía «Lote A1-2» (Text) porque el que guarda es
                //     el Button (2026-08-12 — lo rompió el arreglo de los duplicados, dos commits
                //     antes; a las 12:23 funcionaba y a las 12:52 ya no).
                //
                // Buscar por etiqueta entre lo que el núcleo YA tiene resuelve los dos a la vez, y
                // no inventa: si no hay nada con ese nombre aquí, no se atribuye.
                // SE BUSCA EN LA PANTALLA ANTERIOR, no en esta: el clic ocurrió ALLÍ. Buscarlo en
                // lo que se ve ahora acertaría solo cuando el elemento existe en las dos —el
                // mobiliario— y fallaría justo en lo que de verdad navega.
                // POR ETIQUETA **Y TIPO**, Y SOLO SI ES INEQUÍVOCO. Buscar solo por etiqueta acuñó
                // un camino falso que costó una prueba entera: en el explorador de Windows 11 la
                // celda del nombre de CADA FILA se llama «Nombre» —el nombre de la columna, no el
                // del archivo—, así que al hacer doble clic en una carpeta el clic se resuelve a
                // «Nombre», y en esa pantalla hay dos: la cabecera de columna (SplitButton) y la
                // celda (Edit). Se eligió la primera, y el grafo aprendió que «para llegar a
                // U-ROLLBACK, pulsa la cabecera Nombre». Al navegar hacía exactamente eso: pulsar
                // el filtro, una y otra vez (2026-08-12, lo midió el usuario).
                //
                // Si tras filtrar por tipo sigue habiendo varios, NO SE ATRIBUYE. Una etiqueta que
                // nombra a varias cosas en la misma pantalla no es una identidad, y adivinar entre
                // ellas es justo cómo nació la arista falsa. Sin camino se puede seguir explorando;
                // con un camino equivocado, el navegador va a pulsar lo que no es para siempre.
                //
                // Y QUIÉN CASA CON EL CLIC LO DECIDE `AQuienSeLeDioClic`, fuera del cliente. Está
                // ahí porque es lógica pura con un fallo caro detrás —el clic sobre las letras de un
                // botón resuelve al Text de dentro, que el observador ya había descartado por
                // duplicado— y aquí dentro no se podía probar sin levantar la app entera.
                var atribucion = clic == null
                    ? default
                    : AQuienSeLeDioClic.Resolver(
                        _grafo.DesdeAqui(_anterior)
                              .Select(a => (a.Que.Selector, a.Que.Etiqueta, a.Que.Tipo)).ToList(),
                        clic.Label, clic.ControlType);
                string selectorObservado = atribucion.Selector;

                if (clic != null && reciente && sinEstrenar && despuesDeLlegar && salioDeAlli
                    && selectorObservado.Length > 0
                    && _grafo.Cruzar(_anterior, selectorObservado, aqui))
                {
                    _clicYaUsado = clic.DownIndex;
                    PulsoDelMapeador.Actual.Aprendida(global::Nucleo.Grafo.AppDe(_anterior));
                    LogBus.Log("mapa-vivo", $"aprendido: «{clic.Label}» lleva de {Corto(_anterior)} a {Corto(aqui)}");
                }
                else
                {
                    // Se dice, y no se calla: un salto que no supimos atribuir dice dónde el
                    // mapeador no llega, que es justo lo que hay que ver.
                    // SE DICE QUÉ CLIC SE MIRÓ Y CUÁNTO HACE. Sin eso, «el clic es viejo» no
                    // distingue «tardamos demasiado» de «ese clic no se llegó a registrar y
                    // estamos mirando uno anterior» — y esas dos cosas se arreglan en sitios
                    // distintos. Un mensaje que no separa sus causas cuesta un diagnóstico entero.
                    string porQue = clic == null ? "(no hay ningún clic registrado)"
                        : !sinEstrenar ? $"(«{clic.Label}» ya explicó la transición anterior: un clic explica UNA, "
                                       + "y el que nos trajo aquí todavía no se ha registrado)"
                        : !despuesDeLlegar ? $"(«{clic.Label}» es el clic que nos TRAJO aquí: ocurrió antes "
                                           + "de llegar, así que no puede ser el que nos saca)"
                        : !reciente ? $"(el último clic registrado es «{clic.Label}», de hace {edad:N1} s "
                                    + "— o tardamos, o ese clic no se registró y estamos viendo uno anterior)"
                        : !salioDeAlli ? $"(el clic «{clic.Label}» fue en «{clic.Process}», no en donde estábamos)"
                        : atribucion.Candidatos > 1
                            ? $"(«{clic.Label}» ({clic.ControlType}) nombra a {atribucion.Candidatos} cosas en esa "
                            + "pantalla: no es una identidad, y adivinar acuñaría un camino falso)"
                            : $"(el núcleo no conoce «{clic.Label}» ({clic.ControlType}) en esa pantalla)";
                    // EL MOTIVO, EN UNA PALABRA, para poder contarlos por causa. El texto largo va
                    // al log; aquí hace falta una etiqueta estable que se pueda agrupar y comparar
                    // entre sesiones — «rechazadas: 12» no dice nada, «clic ya usado: 12» lo dice todo.
                    string causa = clic == null ? "no hubo clic"
                        : !sinEstrenar ? "el clic ya explicó otro salto"
                        : !despuesDeLlegar ? "ese clic nos trajo aquí, no nos saca"
                        : !reciente ? "el clic era viejo"
                        : !salioDeAlli ? "el clic fue en otra app"
                        : atribucion.Candidatos > 1 ? "la etiqueta nombra a varias cosas"
                        : "el núcleo no conoce ese elemento allí";
                    PulsoDelMapeador.Actual.Rechazada(causa, global::Nucleo.Grafo.AppDe(_anterior));
                    LogBus.Log("mapa-vivo", $"salto de {Corto(_anterior)} a {Corto(aqui)} SIN atribuir {porQue}");

                    // ESTE ES EL ERROR MÁS GRAVE DEL TERRENO: se cambió de pantalla y ningún clic lo
                    // explica, así que ese camino NO EXISTE en el grafo y nadie va a volver a pasar
                    // por aquí a crearlo. Ya se cantaba en el log; ahora además se cuenta, que es lo
                    // que permite saber cuántas veces pasa y en qué apps.
                    PulsoDelMapeador.Actual.Roto.Anotar(QueSeRompio.AristaNoCreada, _anterior,
                        $"salto a «{aqui}» {porQue}");
                }
            }
            if (!aqui.Equals(_anterior, StringComparison.OrdinalIgnoreCase))
            {
                _anterior = aqui;
                _llegadaAlAnterior = cuando;
            }

            // SE APUNTA EL SITIO AUNQUE NO SE HAYA MIRADO QUÉ HAY. Pasar por un sitio deprisa tiene
            // que dejar constancia de que se pasó: si no, la ubicación intermedia no existiría y el
            // camino quedaría grabado como si fuera directo.
            _grafo.Estoy(aqui);
            _proyector.Proyectar(_grafo);
        }
        catch (Exception e)
        {
            LogBus.Log("mapa-vivo", $"no pude mirar dónde estoy: {e.Message}");
        }
        finally { PulsoDelMapeador.Actual.Ubicacion.Termine(); }
    }

    /// <summary>
    /// LA VALLA CONTRA UIA SIN TIEMPO DE ESPERA. La lógica vive en el mapeador, que es donde se
    /// puede probar: un mecanismo de seguridad cuyo fallo es SILENCIOSO no puede depender de que
    /// alguien levante la app y espere a que algo se cuelgue de verdad.
    ///
    /// Cuatro segundos: veinte veces el coste normal (24 ms de media, 94 la peor), así que no puede
    /// dispararse por una máquina cargada — solo por algo que de verdad no va a contestar.
    /// </summary>
    private readonly Mapeador.SinColgarse _vigia = new(TimeSpan.FromSeconds(4));

    private string DondeEstoySinColgarme() => _vigia.Pregunta(
        _donde,
        alColgarse: () =>
        {
            PulsoDelMapeador.Actual.Colgada("localizar");
            if (!_yaDijeQueEstoyCiego)
            {
                _yaDijeQueEstoyCiego = true;
                LogBus.Log("mapa-vivo", "la app de delante no contesta a UIA: dejo de esperarla. El "
                                      + "mapa no sabrá dónde está hasta que vuelva — ciego, pero no mintiendo.");
            }
        },
        alVolver: () =>
        {
            _yaDijeQueEstoyCiego = false;
            LogBus.Log("mapa-vivo", "la consulta de ubicación que se había colgado por fin volvió; "
                                  + "el mapa vuelve a saber dónde está");
        });

    /// <summary>Para decir «estoy ciego» UNA vez y no cuatro veces por segundo mientras dure.</summary>
    private bool _yaDijeQueEstoyCiego;

    /// <summary>
    /// LA MITAD CARA, a su ritmo: qué hay en la pantalla de delante. Cuesta unos 400 ms de lectura
    /// UIA, así que va aparte de la ubicación — que cuesta 32.
    /// </summary>
    private void Latido()
    {
        // La misma valla: leer la pantalla puede tardar segundos en una app cargada, y encolar
        // lecturas es la forma más rápida de convertir un observador en un lastre.
        if (!PulsoDelMapeador.Actual.Pantalla.MeToca()) { Descarte(esUbicacion: false); return; }
        try
        {
            string aqui = _donde();
            if (aqui.Length == 0) return;

            // El mapeador cuenta lo que ve, el núcleo decide qué hacer con ello. Aquí no se filtra
            // ni se clasifica nada — meter criterio en el puente sería empezar otra vez a repartir
            // las reglas entre dos sitios.
            var crono = System.Diagnostics.Stopwatch.StartNew();
            var crudos = _loQueVeo();
            PulsoDelMapeador.Actual.Costo("leer la pantalla", crono.ElapsedMilliseconds);

            var visibles = SinEtiquetasDeControles(crudos)
                .Select(v => new Nucleo.Elemento(v.Selector, v.Etiqueta, v.Tipo))
                .ToList();
            PulsoDelMapeador.Actual.Embudo(crudos.Count, visibles.Count);

            // ¿PULSASTE ALGO Y CAMBIÓ LO QUE SE VE, PERO SEGUIMOS «EN EL MISMO SITIO»?
            //
            // Ese es el fallo que no aparecía POR NINGÚN LADO. Todo lo que el panel cuenta cuelga de
            // un salto: sin salto no hay atribución que rechazar, ni motivo que apuntar. Así que una
            // app cuya identidad no se mueve al navegar —Spotify paseando por varias pantallas y
            // dejando dos ubicaciones— salía con un grafo diminuto y CERO errores, que se lee como
            // «aquí no pasa nada» cuando lo que pasa es que no nos enteramos (2026-08-13).
            //
            // Se cuenta UNA VEZ POR CLIC, no por vuelta: si no, una pantalla que se refresca sola
            // dispararía el contador para siempre y volvería a ser ruido que se aprende a ignorar.
            long antesDeMirar = _grafo.Version;
            _grafo.Observar(aqui, visibles);
            var clic = Clics?.Last;
            if (clic != null && clic.DownIndex != _clicYaContadoSinSitio
                && (DateTime.UtcNow - clic.When).TotalSeconds < 6
                && _grafo.Version != antesDeMirar
                && aqui.Equals(_anterior, StringComparison.OrdinalIgnoreCase))
            {
                _clicYaContadoSinSitio = clic.DownIndex;
                PulsoDelMapeador.Actual.CambioLaPantallaYNoElSitio(global::Nucleo.Grafo.AppDe(aqui));
            }

            crono.Restart();
            _proyector.Proyectar(_grafo);
            PulsoDelMapeador.Actual.Costo("proyectar", crono.ElapsedMilliseconds);
        }
        catch (Exception e)
        {
            LogBus.Log("mapa-vivo", $"no pude observar: {e.Message}");
        }
        finally { PulsoDelMapeador.Actual.Pantalla.Termine(); }
    }

    /// <summary>
    /// «Se pulsó esto aquí y acabamos allí». Lo llama quien de verdad cruzó algo — es el otro hecho
    /// que el núcleo guarda, y el único que no se puede deducir mirando.
    /// </summary>
    public void Cruzado(string desde, string selector, string hasta)
    {
        _grafo.Cruzar(desde, selector, hasta);
        _proyector.Proyectar(_grafo);
    }

    /// <summary>
    /// EL TEXTO DE DENTRO DE UN BOTÓN NO ES OTRA COSA ALCANZABLE. UIA expone el control y, aparte,
    /// la etiqueta que lleva dentro, las dos con el mismo nombre: «Catálogo» salía como Button y
    /// como Text, y el anillo mostraba cada puerta por duplicado (2026-08-12, lo vio el usuario).
    ///
    /// VA AQUÍ Y NO EN EL NÚCLEO, y la frontera importa: el núcleo guarda fielmente lo que le
    /// cuentan y no puede saber que un Text vive dentro de un Button — eso es conocimiento sobre
    /// cómo se lee un árbol de interfaz, o sea, del mapeador. Meterlo en el grafo sería devolverle
    /// las opiniones sobre la UI que acabamos de quitarle.
    ///
    /// La regla es la más estrecha que resuelve el caso: se descarta un Text SOLO si otro elemento
    /// de la MISMA pantalla, que no es Text, se llama igual. Un texto suelto —un dato, un rótulo
    /// sin dueño— se queda, porque ése sí es algo que hay en la pantalla.
    /// </summary>
    private static List<(string Selector, string Etiqueta, string Tipo)> SinEtiquetasDeControles(
        IReadOnlyList<(string Selector, string Etiqueta, string Tipo)> crudos)
    {
        var utiles = crudos.Where(v => v.Selector.Length > 0 && v.Etiqueta.Length > 0).ToList();
        if (crudos.Count > utiles.Count)
            PulsoDelMapeador.Actual.Filtrado("sin nombre o sin selector", crudos.Count - utiles.Count);
        var conDueno = new HashSet<string>(
            utiles.Where(v => !v.Tipo.Equals("Text", StringComparison.OrdinalIgnoreCase))
                  .Select(v => v.Etiqueta),
            StringComparer.OrdinalIgnoreCase);

        var quedan = utiles
            .Where(v => !v.Tipo.Equals("Text", StringComparison.OrdinalIgnoreCase)
                        || !conDueno.Contains(v.Etiqueta))
            .ToList();
        if (utiles.Count > quedan.Count)
            PulsoDelMapeador.Actual.Filtrado("texto de dentro de un control", utiles.Count - quedan.Count);
        return quedan;
    }

    /// <summary>
    /// Vaciar el núcleo y lo que se ve de él. Una prueba del grafo empieza siempre desde cero — un
    /// grafo con historia esconde justo lo que se quiere medir.
    /// </summary>
    public void Limpiar()
    {
        _grafo.Olvidar();
        _anterior = "";
        _proyector.Vaciar();
        LogBus.Log("mapa-vivo", "núcleo vaciado: el grafo empieza de cero");
    }

    /// <summary>Solo para el log: la identidad entera no cabe y lo que distingue está al final.</summary>
    private static string Corto(string id)
    {
        int i = id.LastIndexOf('/');
        return i > 0 ? id[(i + 1)..] : id;
    }

    public void Dispose()
    {
        _reloj?.Dispose();
        _relojUbicacion?.Dispose();
        _proyector.Dispose();
    }
}
