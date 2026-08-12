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
    private readonly SurfaceMap? _mapa;
    private System.Threading.Timer? _reloj;

    /// <summary>El núcleo, para que quien quiera preguntarle no tenga que pasar por aquí.</summary>
    public Nucleo.Grafo Nucleo => _grafo;

    /// <param name="mapa">
    /// El mapa del que se escuchan los CRUCES. Sin esto, el núcleo nuevo recibía observaciones y
    /// nunca un cruce: su tabla de destinos quedaba vacía para siempre, y las dos promesas que
    /// hablan de destinos eran ciertas en el contrato e inertes en la app (medido el 2026-08-12:
    /// `Cruzado()` no tenía un solo llamante en todo el repo).
    ///
    /// Se escucha UN evento en vez de llamar desde los ocho sitios que aprenden un cruce, que es
    /// lo que garantiza que un llamante nuevo no se olvide — ver la nota en <see cref="SurfaceMap.SeCruzo"/>.
    /// </param>
    public MapaVivo(Func<string> donde,
        Func<IReadOnlyList<(string Selector, string Etiqueta, string Tipo)>> loQueVeo,
        SurfaceMap? mapa = null)
    {
        _donde = donde;
        _loQueVeo = loQueVeo;
        _proyector.Cuenta = m => LogBus.Log("mapa-vivo", m);

        _mapa = mapa;
        if (_mapa != null) _mapa.SeCruzo += Cruzado;
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
            var visibles = _loQueVeo()
                .Where(v => v.Selector.Length > 0 && v.Etiqueta.Length > 0)
                .Select(v => new Nucleo.Elemento(v.Selector, v.Etiqueta, v.Tipo))
                .ToList();

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
    ///
    /// SOLO ANOTA; publicar es del latido. Antes proyectaba aquí mismo, y desde que esto lo dispara
    /// <see cref="SurfaceMap.SeCruzo"/> el llamante es el hilo del crawler o el de una herramienta
    /// MCP: meterles un HTTP de hasta 5 s en mitad del recorrido es poner la red en el camino
    /// caliente. El latido va cada 900 ms y `Proyectar` no hace nada si la versión no cambió, así
    /// que el retraso máximo es un latido y el coste para quien cruzó es cero (2026-08-12).
    /// </summary>
    public void Cruzado(string desde, string selector, string hasta) =>
        _grafo.Cruzar(desde, selector, hasta);

    public void Dispose()
    {
        if (_mapa != null) _mapa.SeCruzo -= Cruzado;
        _reloj?.Dispose();
        _proyector.Dispose();
    }
}
