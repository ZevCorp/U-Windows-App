using System.Net.Http;
using System.Text;
using System.Text.Json;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Navigation;

/// <summary>
/// EL MAPA VIVO, publicado en Neo4j para poder MIRARLO mientras ocurre.
///
/// Nace de un cambio de modelo del usuario (2026-08-12) que hay que dejar escrito porque contradice
/// buena parte de lo construido antes:
///
///   «El problema central no es *cuál es la ruta a X*, sino *qué es alcanzable desde donde estoy*.
///    Siempre debería verificarse en tiempo real qué se ve desde aquí, y eso es lo que puedo
///    navegar. Con la estructura elemental de un grafo —un nodo al centro y alrededor todo lo
///    alcanzable— es suficiente.»
///
/// Lo que eso implica, y es el motivo de que esta clase sea tan pequeña:
///
///  · NO HAY NIVELES. Navegar deja de ser calcular un camino sobre una jerarquía y pasa a ser
///    caminar mirando: en cada sitio se mira qué hay, y de eso se elige. Los niveles existían para
///    que el dibujo tuviera filas, no para navegar.
///  · NO HAY CROMO como pieza estructural. Si en cada paso se comprueba lo visible, el panel
///    lateral simplemente ESTÁ visible desde aquí, y se pulsa. No hace falta un concepto para
///    evitar aristas redundantes: no se recorren aristas, se mira la pantalla.
///  · LO GRABADO Y LO VIVO SON COSAS DISTINTAS, y se pintan distinto. Un elemento que el mapa
///    recuerda pero que hoy no está en pantalla es memoria, no una promesa: prometerlo es lo que
///    hacía que el asistente fuera a buscar algo donde ya no existe.
///
/// Se publica por la API HTTP de Neo4j y no con su driver: una dependencia nueva por cuatro
/// consultas es cara, y el transporte es HTTP igualmente. Si Neo4j no está, esto se calla y la app
/// sigue — es un visor, y un visor no puede tumbar lo que visualiza.
/// </summary>
public sealed class MapaVivo : IDisposable
{
    private readonly SurfaceMap _mapa;
    private readonly Func<string> _donde;
    private readonly Func<IReadOnlyList<(string Selector, string Etiqueta, string Tipo)>> _loQueVeo;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };
    private System.Threading.Timer? _reloj;
    private string _ultimaHuella = "";
    private bool _avisadoDeQueNoHay;

    /// <summary>Dónde vive el Neo4j local. Se puede apuntar a otro con U_NEO4J_HTTP.</summary>
    private static string Url =>
        (Environment.GetEnvironmentVariable("U_NEO4J_HTTP") ?? "http://127.0.0.1:7474")
        .TrimEnd('/') + "/db/neo4j/tx/commit";

    private static string Auth =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(
            (Environment.GetEnvironmentVariable("U_NEO4J_USER") ?? "neo4j") + ":" +
            (Environment.GetEnvironmentVariable("U_NEO4J_PASS") ?? "grafo-local-2026")));

    public MapaVivo(SurfaceMap mapa, Func<string> donde,
        Func<IReadOnlyList<(string Selector, string Etiqueta, string Tipo)>> loQueVeo)
    {
        _mapa = mapa;
        _donde = donde;
        _loQueVeo = loQueVeo;
    }

    /// <summary>
    /// Empieza a publicar. Cada <paramref name="cadaMs"/> mira dónde estamos y qué hay delante, y
    /// solo escribe si algo CAMBIÓ: republicar lo mismo veinte veces por minuto llenaría el registro
    /// de ruido y haría imposible ver, en Neo4j, cuándo pasó algo de verdad.
    /// </summary>
    public void Arrancar(int cadaMs = 900)
    {
        _reloj?.Dispose();
        _reloj = new System.Threading.Timer(_ => Publicar(), null, 500, cadaMs);
        LogBus.Log("mapa-vivo", $"publicando en {Url} cada {cadaMs} ms");
    }

    private void Publicar()
    {
        try
        {
            string aqui = _donde();
            if (aqui.Length == 0) return;

            // LO GRABADO: lo que el mapa recuerda que sale de aquí.
            var recordadas = _mapa.ExitsFrom(aqui)
                .Where(h => h.Info.Selector.Length > 0)
                .GroupBy(h => h.Info.Selector, StringComparer.Ordinal)
                .Select(g => g.First())
                .ToList();

            // LO VIVO: lo que hay en pantalla AHORA. Es la mitad que convierte esto en un mapa vivo
            // y no en una foto vieja.
            var vivos = new HashSet<string>(_loQueVeo().Select(v => v.Selector), StringComparer.OrdinalIgnoreCase);

            // Lo que se ve y el mapa NO recordaba: también es alcanzable desde aquí, y callarlo
            // sería exactamente el error contrario al de prometer lo que ya no está.
            var nuevos = _loQueVeo()
                .Where(v => v.Selector.Length > 0
                         && !recordadas.Any(r => r.Info.Selector.Equals(v.Selector, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            var hijos = recordadas
                .Select(h => new
                {
                    sel = h.Info.Selector,
                    etq = h.Info.Label,
                    tipo = h.Info.ControlType,
                    vivo = vivos.Contains(h.Info.Selector),
                    destino = SurfaceMap.EsPuerta(h.To) ? "" : h.To,
                })
                .Concat(nuevos.Select(v => new
                {
                    sel = v.Selector, etq = v.Etiqueta, tipo = v.Tipo, vivo = true, destino = "",
                }))
                .ToList();

            // La huella evita reescribir lo mismo. Incluye lo VIVO, no solo lo recordado: entrar y
            // salir de un menú no cambia lo que el mapa sabe, pero sí cambia lo que se ve — y eso
            // es justo lo que se quiere ver moverse.
            string huella = aqui + "|" + string.Join(",", hijos.Select(h => h.sel + (h.vivo ? "+" : "-")));
            if (huella == _ultimaHuella) return;
            _ultimaHuella = huella;

            var cypher = new
            {
                statements = new object[]
                {
                    // UNA SOLA PANTALLA ES «la actual». Sin esto, mirar el grafo en Neo4j no diría
                    // dónde estás, que es la única pregunta que este visor existe para contestar.
                    new { statement = "MATCH (p:Pantalla {actual:true}) SET p.actual = false" },
                    new
                    {
                        statement = """
                        MERGE (p:Pantalla {id:$aqui})
                          SET p.actual = true, p.app = $app, p.visto = timestamp()
                        WITH p
                        UNWIND $hijos AS h
                          MERGE (e:Elemento {id: $aqui + '|' + h.sel})
                            SET e.selector = h.sel, e.etiqueta = h.etq, e.tipo = h.tipo,
                                e.vivo = h.vivo, e.app = $app
                          MERGE (p)-[m:MUESTRA]->(e)
                            SET m.vivo = h.vivo
                        WITH e, h WHERE h.destino <> ''
                          MERGE (d:Pantalla {id: h.destino}) ON CREATE SET d.app = $app, d.actual = false
                          MERGE (e)-[:LLEVA_A]->(d)
                        """,
                        parameters = new { aqui, app = SurfaceMap.AppDe(aqui), hijos },
                    },
                },
            };

            Mandar(JsonSerializer.Serialize(cypher));
        }
        catch (Exception e)
        {
            LogBus.Log("mapa-vivo", $"no pude publicar: {e.Message}");
        }
    }

    private void Mandar(string cuerpo)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, Url)
            {
                Content = new StringContent(cuerpo, Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("Authorization", "Basic " + Auth);
            using var res = _http.Send(req);
            string texto = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            // Neo4j contesta 200 con los errores DENTRO del cuerpo: dar por bueno el código de
            // estado dejaría pasar una consulta rota como si hubiera escrito algo.
            if (texto.Contains("\"errors\":[{", StringComparison.Ordinal))
                LogBus.Log("mapa-vivo", "Neo4j rechazó la consulta: " + texto[..Math.Min(400, texto.Length)]);
        }
        catch (Exception e)
        {
            if (_avisadoDeQueNoHay) return;   // se dice UNA vez: si no hay Neo4j, no hay que repetirlo cada segundo
            _avisadoDeQueNoHay = true;
            LogBus.Log("mapa-vivo", $"Neo4j no responde en {Url} ({e.Message}). "
                                  + "El mapa vivo no se publica; la app sigue igual.");
        }
    }

    public void Dispose()
    {
        _reloj?.Dispose();
        _http.Dispose();
    }
}
