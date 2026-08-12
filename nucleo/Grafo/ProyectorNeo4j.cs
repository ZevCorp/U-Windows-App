using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Nucleo;

/// <summary>
/// Pinta el núcleo en Neo4j para poder MIRARLO mientras ocurre.
///
/// Va aparte del <see cref="Grafo"/> a propósito: el grafo es puro y no sabe que existe una base de
/// datos. Si supiera, no se podría probar sin levantar una, y la primera excusa para no probarlo
/// sería que «hoy no está Neo4j». Aquí se proyecta lo que el grafo ya decidió; ninguna regla vive
/// en este archivo.
///
/// LO QUE SE VE ES LO QUE HAY. La proyección borra y reescribe la app entera en cada pasada, así
/// que Neo4j no puede quedarse con restos de una versión anterior — que es exactamente el fallo que
/// este visor existe para hacer imposible: mirar algo distinto de lo que se está construyendo.
/// </summary>
public sealed class ProyectorNeo4j : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly string _url;
    private readonly string _auth;
    private int _ultimaVersion = -1;
    private bool _yaAvise;

    /// <summary>Lo que se dice cuando algo va mal. Se inyecta para no atar el núcleo a ningún log.</summary>
    public Action<string>? Cuenta { get; set; }

    public ProyectorNeo4j(string? url = null, string? usuario = null, string? clave = null)
    {
        _url = (url ?? Environment.GetEnvironmentVariable("U_NEO4J_HTTP") ?? "http://127.0.0.1:7474")
               .TrimEnd('/') + "/db/neo4j/tx/commit";
        _auth = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            (usuario ?? Environment.GetEnvironmentVariable("U_NEO4J_USER") ?? "neo4j") + ":" +
            (clave ?? Environment.GetEnvironmentVariable("U_NEO4J_PASS") ?? "grafo-local-2026")));
    }

    /// <summary>
    /// Vuelca el grafo entero. No hace nada si no ha cambiado desde la última vez: republicar lo
    /// mismo llenaría de ruido la única ventana que tenemos para ver cuándo pasó algo de verdad.
    /// </summary>
    public bool Proyectar(Grafo grafo, string app)
    {
        if (grafo.Version == _ultimaVersion) return false;
        _ultimaVersion = grafo.Version;

        var ubicaciones = grafo.Ubicaciones();
        var filas = new List<object>();
        foreach (string u in ubicaciones)
            foreach (var a in grafo.DesdeAqui(u))
                filas.Add(new
                {
                    donde = u,
                    sel = a.Que.Selector,
                    etq = a.Que.Etiqueta,
                    tipo = a.Que.Tipo,
                    vivo = a.Vivo,
                    destino = a.Destino,
                });

        var cypher = new
        {
            statements = new object[]
            {
                // BORRAR Y REESCRIBIR. Es más caro que actualizar en su sitio y es lo correcto: con
                // actualizaciones parciales, un elemento que desaparece del grafo se queda para
                // siempre en la pantalla de Neo4j, y entonces el visor miente igual que mentía el
                // dibujo anterior.
                new { statement = "MATCH (n {app:$app}) DETACH DELETE n", parameters = new { app } },
                new
                {
                    statement = """
                    UNWIND $ubis AS u
                      MERGE (p:Ubicacion {id:u}) SET p.app = $app, p.actual = (u = $aqui)
                    """,
                    parameters = new { ubis = ubicaciones, app, aqui = grafo.Aqui },
                },
                new
                {
                    statement = """
                    UNWIND $filas AS f
                      MATCH (p:Ubicacion {id:f.donde})
                      MERGE (e:Elemento {id: f.donde + '|' + f.sel})
                        SET e.selector = f.sel, e.etiqueta = f.etq, e.tipo = f.tipo,
                            e.vivo = f.vivo, e.app = $app
                      MERGE (p)-[m:ALCANZA]->(e) SET m.vivo = f.vivo
                    WITH e, f WHERE f.destino <> ''
                      MERGE (d:Ubicacion {id:f.destino}) ON CREATE SET d.app = $app, d.actual = false
                      MERGE (e)-[:LLEVA_A]->(d)
                    """,
                    parameters = new { filas, app },
                },
            },
        };

        return Mandar(JsonSerializer.Serialize(cypher));
    }

    private bool Mandar(string cuerpo)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, _url)
            {
                Content = new StringContent(cuerpo, Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("Authorization", "Basic " + _auth);
            using var res = _http.Send(req);
            string texto = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            // Neo4j contesta 200 con los errores DENTRO del cuerpo: fiarse del código de estado
            // dejaría pasar una consulta rota como si hubiera escrito algo.
            if (texto.Contains("\"errors\":[{", StringComparison.Ordinal))
            {
                Cuenta?.Invoke("Neo4j rechazó la consulta: " + texto[..Math.Min(400, texto.Length)]);
                return false;
            }
            return true;
        }
        catch (Exception e)
        {
            if (!_yaAvise)
            {
                _yaAvise = true;
                Cuenta?.Invoke($"Neo4j no responde en {_url} ({e.Message}). El núcleo sigue funcionando; solo no se ve.");
            }
            return false;
        }
    }

    public void Dispose() => _http.Dispose();
}
