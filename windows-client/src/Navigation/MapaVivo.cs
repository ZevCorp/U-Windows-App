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
    /// Quién sabe qué se acaba de pulsar. Sin esto el núcleo solo aprende «esto se ve aquí» y nunca
    /// «esto llevó allí», así que el grafo no se arma nunca: quedan islas sin caminos entre ellas.
    /// </summary>
    public ClickWatcher? Clics { get; set; }

    /// <summary>El núcleo, para que quien quiera preguntarle no tenga que pasar por aquí.</summary>
    public Nucleo.Grafo Nucleo => _grafo;

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
                var clic = Clics?.Last;
                bool reciente = clic != null && (DateTime.UtcNow - clic.When).TotalSeconds < 6;
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

                // UN SOLO VOCABULARIO DE IDENTIDAD. El vigilante de clics describe con
                // «uia:aid=…» y el observador con «uia:name=…», así que pasarle al núcleo el
                // selector del clic guardaba el destino bajo una clave que ningún elemento
                // observado tenía: el camino quedaba huérfano e invisible. Se traduce al idioma del
                // observador, que es el que usa quien luego pregunta «qué alcanzo desde aquí».
                string selectorObservado = clic == null ? ""
                    : $"uia:name={clic.Label};ct={clic.ControlType}";

                if (clic != null && reciente && salioDeAlli && mismaApp && selectorObservado.Length > 0
                    && _grafo.Cruzar(_anterior, selectorObservado, aqui))
                {
                    LogBus.Log("mapa-vivo", $"aprendido: «{clic.Label}» lleva de {Corto(_anterior)} a {Corto(aqui)}");
                }
                else
                {
                    // Se dice, y no se calla: un salto que no supimos atribuir dice dónde el
                    // mapeador no llega, que es justo lo que hay que ver.
                    LogBus.Log("mapa-vivo", $"salto de {Corto(_anterior)} a {Corto(aqui)} SIN atribuir "
                        + (clic == null ? "(no hay clic)"
                           : !reciente ? "(el clic es viejo)"
                           : !salioDeAlli ? "(el clic no salió de allí)"
                           : !mismaApp ? "(es otra app: fue un cambio de ventana, no navegación)"
                           : "(el núcleo no reconoce ese elemento aquí)"));
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
