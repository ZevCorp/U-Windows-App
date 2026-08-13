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
    private string _anterior = "";

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

        _reloj?.Dispose();
        _reloj = new System.Threading.Timer(_ => Latido(), null, 500, cadaMs);
        LogBus.Log("mapa-vivo", $"observando cada {cadaMs} ms y proyectando el núcleo en Neo4j");
    }

    private void Latido()
    {
        try
        {
            string aqui = _donde();
            if (aqui.Length == 0) return;

            // EL CLIC SE MIRA ANTES DE LEER LA PANTALLA, no después. Leer el árbol UIA entero
            // cuesta ~1 s (medido en la Maqueta) y bastante más en apps grandes; si la edad del
            // clic se comprobara al final, esa lectura se le sumaría y un clic perfectamente
            // reciente podría llegar «viejo» a la comparación. Es una carrera silenciosa: nadie
            // vería el fallo, solo faltarían caminos (2026-08-12).
            var clic = Clics?.Last;
            var cuando = DateTime.UtcNow;

            // OBSERVAR: el mapeador cuenta lo que ve, el núcleo decide qué hacer con ello. Aquí no
            // se filtra ni se clasifica nada — meter criterio en el puente sería empezar otra vez a
            // repartir las reglas entre dos sitios.
            var visibles = SinEtiquetasDeControles(_loQueVeo())
                .Select(v => new Nucleo.Elemento(v.Selector, v.Etiqueta, v.Tipo))
                .ToList();

            // ¿CAMBIAMOS DE SITIO? Entonces algo nos trajo, y ese «algo» es el otro hecho que el
            // núcleo guarda. Se atribuye al ÚLTIMO CLIC si es reciente y salió de donde estábamos;
            // si no se puede saber, no se inventa: una arista con el elemento equivocado es peor
            // que ninguna, porque el asistente la usaría para volver y pulsaría otra cosa.
            if (_anterior.Length > 0 && !_anterior.Equals(aqui, StringComparison.OrdinalIgnoreCase))
            {
                double edad = clic == null ? -1 : (cuando - clic.When).TotalSeconds;
                bool reciente = clic != null && edad < 6;
                bool sinEstrenar = clic != null && clic.DownIndex != _clicYaUsado;
                bool salioDeAlli = clic != null
                    && global::Nucleo.Grafo.AppDe(_anterior)
                        .StartsWith(clic.Process, StringComparison.OrdinalIgnoreCase);

                // UN CLIC NO TE LLEVA A OTRA APP. Si el destino es de otra aplicación, lo que pasó
                // fue un cambio de ventana —alt-tab, la barra de tareas, un clic fuera— y no una
                // navegación. Sin esta valla el grafo acuñó «pulsar Ajustes en la Maqueta lleva a
                // la terminal», que es falso y además peligroso: el asistente lo usaría para
                // volver y pulsaría otra cosa (2026-08-12, visto en el primer camino aprendido).
                bool mismaApp = global::Nucleo.Grafo.AppDe(_anterior)
                    .Equals(global::Nucleo.Grafo.AppDe(aqui), StringComparison.OrdinalIgnoreCase);

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
                var candidatos = clic == null ? new List<Nucleo.Alcanzable>()
                    : _grafo.DesdeAqui(_anterior)
                        .Where(a => a.Que.Etiqueta.Equals(clic.Label, StringComparison.OrdinalIgnoreCase)
                                 && a.Que.Tipo.Equals(clic.ControlType, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                string selectorObservado = candidatos.Count == 1 ? candidatos[0].Que.Selector : "";

                if (clic != null && reciente && sinEstrenar && salioDeAlli && mismaApp
                    && selectorObservado.Length > 0
                    && _grafo.Cruzar(_anterior, selectorObservado, aqui))
                {
                    _clicYaUsado = clic.DownIndex;
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
                        : !reciente ? $"(el último clic registrado es «{clic.Label}», de hace {edad:N1} s "
                                    + "— o tardamos, o ese clic no se registró y estamos viendo uno anterior)"
                        : !salioDeAlli ? $"(el clic «{clic.Label}» fue en «{clic.Process}», no en donde estábamos)"
                        : !mismaApp ? "(es otra app: fue un cambio de ventana, no navegación)"
                        : candidatos.Count > 1
                            ? $"(«{clic.Label}» ({clic.ControlType}) nombra a {candidatos.Count} cosas en esa "
                            + "pantalla: no es una identidad, y adivinar acuñaría un camino falso)"
                            : $"(el núcleo no conoce «{clic.Label}» ({clic.ControlType}) en esa pantalla)";
                    LogBus.Log("mapa-vivo", $"salto de {Corto(_anterior)} a {Corto(aqui)} SIN atribuir {porQue}");
                }
            }
            _anterior = aqui;

            _grafo.Observar(aqui, visibles);
            _proyector.Proyectar(_grafo);
        }
        catch (Exception e)
        {
            LogBus.Log("mapa-vivo", $"no pude observar: {e.Message}");
        }
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
        var conDueno = new HashSet<string>(
            utiles.Where(v => !v.Tipo.Equals("Text", StringComparison.OrdinalIgnoreCase))
                  .Select(v => v.Etiqueta),
            StringComparer.OrdinalIgnoreCase);

        return utiles
            .Where(v => !v.Tipo.Equals("Text", StringComparison.OrdinalIgnoreCase)
                        || !conDueno.Contains(v.Etiqueta))
            .ToList();
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
        _proyector.Dispose();
    }
}
