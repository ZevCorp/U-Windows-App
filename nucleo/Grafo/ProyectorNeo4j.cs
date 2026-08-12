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
    /// Vuelca el grafo ENTERO. No hace nada si no ha cambiado desde la última vez: republicar lo
    /// mismo llenaría de ruido la única ventana que tenemos para ver cuándo pasó algo de verdad.
    /// </summary>
    /// <remarks>
    /// EL GRAFO ES UNO, AUNQUE TENGA VARIAS APPS. La primera versión recibía «la app actual» y la
    /// usaba para dos cosas a la vez: borrar lo suyo y etiquetar lo que escribía. El resultado fue
    /// que CADA ubicación —de cualquier app— quedaba marcada con la app de donde estuvieras en ese
    /// momento: con la Maqueta delante, «uia://chrome.exe» figuraba como app «claude.exe»
    /// (2026-08-12, lo vio el usuario). Y como el borrado iba por esa misma etiqueta, cada pasada
    /// arrasaba lo que la anterior acababa de mal-etiquetar.
    ///
    /// La app de una ubicación sale de SU PROPIO id y de ningún otro sitio. Y el borrado se lleva
    /// todo lo que el núcleo proyecta, porque el núcleo es el dueño de estas etiquetas: dejar
    /// restos de una pasada anterior es exactamente la clase de mentira que este visor existe para
    /// hacer imposible.
    /// </remarks>
    public bool Proyectar(Grafo grafo)
    {
        if (grafo.Version == _ultimaVersion) return false;
        _ultimaVersion = grafo.Version;

        var ubicaciones = grafo.Ubicaciones()
            .Select(u => new { id = u, app = Grafo.AppDe(u), actual = u == grafo.Aqui })
            .ToList();

        var filas = new List<object>();
        foreach (string u in grafo.Ubicaciones())
            foreach (var a in grafo.DesdeAqui(u))
                filas.Add(new
                {
                    donde = u,
                    app = Grafo.AppDe(u),
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
                new { statement = "MATCH (n) WHERE n:Ubicacion OR n:Elemento DETACH DELETE n" },
                new
                {
                    statement = """
                    UNWIND $ubis AS u
                      MERGE (p:Ubicacion {id:u.id}) SET p.app = u.app, p.actual = u.actual
                    """,
                    parameters = new { ubis = ubicaciones },
                },
                new
                {
                    statement = """
                    UNWIND $filas AS f
                      MATCH (p:Ubicacion {id:f.donde})
                      MERGE (e:Elemento {id: f.donde + '|' + f.sel})
                        SET e.selector = f.sel, e.etiqueta = f.etq, e.tipo = f.tipo,
                            e.vivo = f.vivo, e.app = f.app
                      MERGE (p)-[m:ALCANZA]->(e) SET m.vivo = f.vivo
                    WITH e, f WHERE f.destino <> ''
                      MERGE (d:Ubicacion {id:f.destino}) ON CREATE SET d.app = f.app, d.actual = false
                      MERGE (e)-[:LLEVA_A]->(d)
                    """,
                    parameters = new { filas },
                },
            },
        };

        return Mandar(JsonSerializer.Serialize(cypher));
    }

    /// <summary>
    /// ¿Lo que hay en Neo4j es EXACTAMENTE lo que dice el núcleo? Lee el grafo de vuelta y lo
    /// compara, hecho por hecho. Devuelve vacío si coinciden; si no, qué sobra y qué falta.
    /// </summary>
    /// <remarks>
    /// Esto existe porque «confía en que el proyector escribe bien» es exactamente la clase de
    /// promesa que ya nos falló. Los dos fallos del 2026-08-12 —la app de cada ubicación pisada con
    /// la actual, y la ubicación colapsada a nivel de app— vivían AQUÍ, en el paso de escribir, y
    /// cualquier visor del mundo los habría pintado con la misma seguridad: un dibujo fiel a una
    /// base de datos equivocada sigue siendo un dibujo equivocado.
    ///
    /// Cambiar de herramienta de visualización no cubre esto. Comparar, sí. Y comparar LEYENDO DE
    /// VUELTA es lo único que lo cubre de verdad: revisar el código del proyector demuestra lo que
    /// pretende hacer, no lo que hizo.
    /// </remarks>
    public string Verificar(Grafo grafo)
    {
        var esperado = Retrato(grafo);

        var consulta = new
        {
            statements = new object[]
            {
                new
                {
                    statement = """
                    MATCH (u:Ubicacion)
                    OPTIONAL MATCH (u)-[a:ALCANZA]->(e:Elemento)
                    OPTIONAL MATCH (e)-[:LLEVA_A]->(d:Ubicacion)
                    RETURN u.id AS donde, e.selector AS sel, a.vivo AS vivo, d.id AS destino
                    """,
                },
            },
        };

        string cuerpo = Pedir(JsonSerializer.Serialize(consulta));
        if (cuerpo.Length == 0) return "no pude leer de vuelta desde Neo4j: la comprobación NO se hizo";

        var enBase = new SortedSet<string>(StringComparer.Ordinal);
        using (var doc = JsonDocument.Parse(cuerpo))
        {
            if (!doc.RootElement.TryGetProperty("results", out var res) || res.GetArrayLength() == 0)
                return "Neo4j no devolvió resultados: la comprobación NO se hizo";
            foreach (var fila in res[0].GetProperty("data").EnumerateArray())
            {
                var row = fila.GetProperty("row");
                string donde = row[0].GetString() ?? "";
                if (row[1].ValueKind == JsonValueKind.Null) continue;   // ubicación sin elementos
                string sel = row[1].GetString() ?? "";
                bool vivo = row[2].ValueKind == JsonValueKind.True;
                string destino = row[3].ValueKind == JsonValueKind.Null ? "" : row[3].GetString() ?? "";
                enBase.Add(Linea(donde, sel, vivo, destino));
            }
        }

        var sobra = enBase.Except(esperado, StringComparer.Ordinal).Take(6).ToList();
        var falta = esperado.Except(enBase, StringComparer.Ordinal).Take(6).ToList();
        if (sobra.Count == 0 && falta.Count == 0) return "";

        var sb = new StringBuilder($"NO COINCIDEN: el núcleo tiene {esperado.Count} hechos y Neo4j {enBase.Count}.");
        if (falta.Count > 0) sb.Append("\n  FALTA en Neo4j: ").Append(string.Join(" | ", falta));
        if (sobra.Count > 0) sb.Append("\n  SOBRA en Neo4j: ").Append(string.Join(" | ", sobra));
        return sb.ToString();
    }

    /// <summary>
    /// Estropear la base a propósito. EXISTE PARA QUE EL CONTRATO PUEDA PROBAR QUE
    /// <see cref="Verificar"/> sabe fallar: una comprobación que solo se ha visto en verde es
    /// indistinguible de una que devuelve verde siempre, y esa fue la forma del peor fallo que
    /// hemos tenido —una herramienta contestando «no queda nada pendiente» sobre un grafo vacío—.
    ///
    /// No la llama nadie más, y por eso lleva este nombre: si algún día aparece en código de
    /// producción, el nombre lo delata a la primera lectura.
    /// </summary>
    public void Sabotear(string cypher) =>
        Mandar(JsonSerializer.Serialize(new { statements = new object[] { new { statement = cypher } } }));

    /// <summary>El grafo como un conjunto de hechos comparables. Ordenado, para que dos retratos
    /// del mismo grafo sean iguales carácter a carácter.</summary>
    private static SortedSet<string> Retrato(Grafo grafo)
    {
        var s = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string u in grafo.Ubicaciones())
            foreach (var a in grafo.DesdeAqui(u))
                s.Add(Linea(u, a.Que.Selector, a.Vivo, a.Destino));
        return s;
    }

    private static string Linea(string donde, string sel, bool vivo, string destino) =>
        $"{donde}{sel}{(vivo ? "vivo" : "memoria")}{destino}";

    private string Pedir(string cuerpo)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, _url)
            {
                Content = new StringContent(cuerpo, Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("Authorization", "Basic " + _auth);
            using var res = _http.Send(req);
            return res.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            Cuenta?.Invoke($"no pude leer de Neo4j: {e.Message}");
            return "";
        }
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
