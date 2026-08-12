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
    private string _ultimaHuella = "";
    private bool _yaAvise;

    /// <summary>
    /// Lo último que se escribió de CADA ubicación. Es lo que permite escribir solo lo que cambió
    /// en vez de rehacer el grafo entero.
    /// </summary>
    private readonly Dictionary<string, string> _huellaPorUbicacion = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// UNA PROYECCIÓN A LA VEZ. Sin esto, el latido de 900 ms disparaba una escritura nueva antes de
    /// que terminara la anterior: con 3.868 elementos cada una tardaba segundos, se solapaban, y
    /// Neo4j las rechazaba por INTERBLOQUEO —310 fallos medidos en un rato—. Las escrituras
    /// perdidas dejaban datos viejos, y el visor mostraba una pantalla donde no estabas.
    ///
    /// Se DESCARTA la que llega mientras hay otra en curso, no se encola: si el estado volvió a
    /// cambiar, el siguiente latido lo recogerá igual, y una cola solo serviría para pintar con
    /// retraso una foto que ya caducó.
    /// </summary>
    private int _escribiendo;

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

        // UNA A LA VEZ. Si hay otra escribiendo, esta se descarta: el siguiente latido recogerá el
        // estado igual, y encolarlas solo serviría para pintar con retraso una foto ya caducada.
        if (Interlocked.Exchange(ref _escribiendo, 1) == 1) return false;
        try
        {
            _ultimaVersion = grafo.Version;

            // SOLO SE ESCRIBE LO QUE CAMBIÓ. Antes esto borraba el grafo entero y lo reescribía en
            // cada cambio. Con 3.868 elementos cada pasada tardaba segundos, el latido de 900 ms
            // disparaba la siguiente antes de que acabara la anterior, y Neo4j las rechazaba por
            // interbloqueo: 310 fallos medidos, y el visor enseñando una pantalla donde no estabas
            // (2026-08-12, lo vio el usuario).
            //
            // Ahora se compara ubicación por ubicación y se manda solo la que se movió — que en el
            // caso normal es UNA, la de delante. No hace falta borrar nada en el camino caliente:
            // el núcleo nunca quita elementos de una ubicación (observar no borra), así que un
            // MERGE basta. Vaciar el grafo entero tiene su propio botón, que sí borra.
            var cambiadas = new List<string>();
            foreach (string u in grafo.Ubicaciones())
            {
                string h = HuellaDeUbicacion(grafo, u);
                if (_huellaPorUbicacion.TryGetValue(u, out var ya) && ya == h) continue;
                _huellaPorUbicacion[u] = h;
                cambiadas.Add(u);
            }

            var declaraciones = new List<object>
            {
                // DÓNDE ESTAMOS, siempre y aparte: es lo que más cambia y lo más barato de escribir.
                new
                {
                    statement = """
                    MATCH (p:Ubicacion {actual:true}) WHERE p.id <> $aqui SET p.actual = false
                    WITH count(*) AS _
                    MERGE (n:Ubicacion {id:$aqui}) SET n.actual = true, n.app = $app
                    """,
                    parameters = new { aqui = grafo.Aqui, app = Grafo.AppDe(grafo.Aqui) },
                },
            };

            if (cambiadas.Count > 0)
            {
                var filas = new List<object>();
                foreach (string u in cambiadas)
                    foreach (var a in grafo.DesdeAqui(u))
                        filas.Add(new
                        {
                            donde = u, app = Grafo.AppDe(u),
                            sel = a.Que.Selector, etq = a.Que.Etiqueta, tipo = a.Que.Tipo,
                            vivo = a.Vivo, destino = a.Destino,
                        });

                declaraciones.Add(new
                {
                    statement = """
                    UNWIND $ubis AS u
                      MERGE (p:Ubicacion {id:u.id}) SET p.app = u.app
                    """,
                    parameters = new { ubis = cambiadas.Select(u => new { id = u, app = Grafo.AppDe(u) }) },
                });
                declaraciones.Add(new
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
                });
            }

            _ultimaHuella = HuellaDeContenido(grafo);
            return Mandar(JsonSerializer.Serialize(new { statements = declaraciones }));
        }
        finally { Interlocked.Exchange(ref _escribiendo, 0); }
    }

    /// <summary>La huella de UNA ubicación: qué elementos tiene, cuáles vivos y a dónde llevan.</summary>
    private static string HuellaDeUbicacion(Grafo grafo, string u)
    {
        var sb = new StringBuilder();
        foreach (var a in grafo.DesdeAqui(u))
            sb.Append(a.Que.Selector).Append(a.Vivo ? '+' : '-').Append(a.Destino).Append(';');
        return sb.ToString();
    }

    /// <summary>
    /// DEVOLVERLE AL NÚCLEO LO QUE RECUERDA. Lee Neo4j y lo replica dentro del grafo. Devuelve
    /// cuántas ubicaciones volvieron.
    /// </summary>
    /// <remarks>
    /// Hasta hoy el núcleo no leía nada al arrancar: empezaba vacío, y su primera proyección
    /// —que borra y reescribe— se llevaba por delante todo lo mapeado en sesiones anteriores.
    /// Reiniciar la app perdía el mapa entero, y nadie lo había notado porque siempre lo
    /// limpiábamos a mano antes de cada prueba (2026-08-12, salió al explicarle el modelo al
    /// usuario). Neo4j deja de ser solo el espejo y pasa a ser también la memoria.
    ///
    /// ENTRA POR LA MISMA PUERTA QUE TODO LO DEMÁS: `Recordar` y `Cruzar`, no escribiendo en los
    /// diccionarios del grafo. Así las reglas del núcleo se aplican también a lo que viene del
    /// disco — si Neo4j trae un destino de un elemento que no existe en su ubicación, `Cruzar` lo
    /// rechaza igual que lo rechazaría en vivo. Una puerta trasera para «restaurar rápido» sería
    /// justo por donde entraría lo que las promesas no vigilan.
    ///
    /// Y VUELVE COMO MEMORIA, NO COMO VIVO: nada de lo restaurado está en pantalla, porque estamos
    /// arrancando. Decir lo contrario mandaría al asistente a pulsar cosas que no están delante.
    /// </remarks>
    public int Restaurar(Grafo grafo)
    {
        var consulta = new
        {
            statements = new object[]
            {
                new
                {
                    statement = """
                    MATCH (u:Ubicacion)-[:ALCANZA]->(e:Elemento)
                    OPTIONAL MATCH (e)-[:LLEVA_A]->(d:Ubicacion)
                    RETURN u.id AS donde, e.selector AS sel, e.etiqueta AS etq, e.tipo AS tipo,
                           d.id AS destino
                    """,
                },
            },
        };

        string cuerpo = Pedir(JsonSerializer.Serialize(consulta));
        if (cuerpo.Length == 0) return 0;

        var porUbicacion = new Dictionary<string, List<Elemento>>(StringComparer.OrdinalIgnoreCase);
        var caminos = new List<(string Donde, string Sel, string Destino)>();
        try
        {
            using var doc = JsonDocument.Parse(cuerpo);
            if (!doc.RootElement.TryGetProperty("results", out var res) || res.GetArrayLength() == 0) return 0;
            foreach (var fila in res[0].GetProperty("data").EnumerateArray())
            {
                var row = fila.GetProperty("row");
                string donde = row[0].GetString() ?? "";
                string sel = row[1].ValueKind == JsonValueKind.Null ? "" : row[1].GetString() ?? "";
                if (donde.Length == 0 || sel.Length == 0) continue;

                if (!porUbicacion.TryGetValue(donde, out var lista))
                    porUbicacion[donde] = lista = new List<Elemento>();
                lista.Add(new Elemento(sel, row[2].GetString() ?? "", row[3].GetString() ?? ""));

                if (row[4].ValueKind != JsonValueKind.Null)
                    caminos.Add((donde, sel, row[4].GetString() ?? ""));
            }
        }
        catch (Exception e)
        {
            Cuenta?.Invoke($"no pude leer la memoria de Neo4j: {e.Message}");
            return 0;
        }

        // PRIMERO LOS ELEMENTOS Y DESPUÉS LOS CAMINOS, y el orden no es casual: `Cruzar` exige que
        // el elemento ya se conozca en esa ubicación, así que al revés se rechazaría todo.
        foreach (var (donde, elementos) in porUbicacion) grafo.Recordar(donde, elementos);
        int rechazados = caminos.Count(c => !grafo.Cruzar(c.Donde, c.Sel, c.Destino));
        if (rechazados > 0)
            Cuenta?.Invoke($"al restaurar, {rechazados} camino(s) de Neo4j no pasaron las reglas del núcleo y se descartaron");

        // La huella se pone al día para que la primera proyección no sea un volcado completo de lo
        // que Neo4j ya tiene: acabamos de leerlo de ahí.
        _ultimaHuella = HuellaDeContenido(grafo);
        _ultimaVersion = grafo.Version;
        foreach (string u in grafo.Ubicaciones()) _huellaPorUbicacion[u] = HuellaDeUbicacion(grafo, u);
        return porUbicacion.Count;
    }

    /// <summary>
    /// Borrar lo proyectado. Se olvida también la última huella para que el siguiente volcado sea
    /// completo: si no, el proyector creería que Neo4j ya tiene lo que acaba de perder.
    /// </summary>
    public void Vaciar()
    {
        _ultimaVersion = -1;
        _ultimaHuella = "";
        _huellaPorUbicacion.Clear();
        Mandar(JsonSerializer.Serialize(new
        {
            statements = new object[]
            {
                new { statement = "MATCH (n) WHERE n:Ubicacion OR n:Elemento DETACH DELETE n" },
            },
        }));
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
    public void Sabotear(string cypher)
    {
        // Se olvida lo que creíamos escrito: acabamos de corromper la base por detrás, así que
        // nuestra caché por ubicación ya no describe lo que hay y la siguiente pasada tiene que
        // volver a escribirlo todo.
        _huellaPorUbicacion.Clear();
        _ultimaHuella = "";
        Mandar(JsonSerializer.Serialize(new { statements = new object[] { new { statement = cypher } } }));
    }

    /// <summary>
    /// Una huella de TODO menos de dónde estamos. Sirve para distinguir «me moví» de «el grafo
    /// cambió», que son lo mismo para la versión del núcleo y cuestan cosas muy distintas de
    /// escribir. No entra <see cref="Grafo.Aqui"/> a propósito: si entrara, moverse siempre
    /// parecería un cambio de contenido y volveríamos al volcado completo por cada ventana.
    /// </summary>
    private static string HuellaDeContenido(Grafo grafo)
    {
        var sb = new StringBuilder();
        foreach (string u in grafo.Ubicaciones())
        {
            sb.Append(u).Append('{');
            foreach (var a in grafo.DesdeAqui(u))
                sb.Append(a.Que.Selector).Append(a.Vivo ? '+' : '-').Append(a.Destino).Append(';');
            sb.Append('}');
        }
        return sb.ToString();
    }

    /// <summary>
    /// ¿Lo que hay en Neo4j es EXACTAMENTE lo que dice el núcleo? Lee el grafo de vuelta y lo
    /// compara, hecho por hecho. Devuelve vacío si coinciden; si no, qué sobra y qué falta.
    /// </summary>
    /// <remarks>
    /// Existe porque «confía en que el proyector escribe bien» es exactamente la clase de promesa
    /// que ya nos falló. Y comparar LEYENDO DE VUELTA es lo único que lo cubre: revisar el código
    /// del proyector demuestra lo que pretende hacer, no lo que hizo. Con la escritura incremental
    /// vale todavía más — ahora hay una caché de por medio, y una caché que se desincronice
    /// dejaría Neo4j viejo sin que nadie se enterara.
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
                    MATCH (u:Ubicacion)-[a:ALCANZA]->(e:Elemento)
                    OPTIONAL MATCH (e)-[:LLEVA_A]->(d:Ubicacion)
                    RETURN u.id AS donde, e.selector AS sel, a.vivo AS vivo, d.id AS destino
                    """,
                },
            },
        };

        string cuerpo = Pedir(JsonSerializer.Serialize(consulta));
        if (cuerpo.Length == 0) return "no pude leer de vuelta desde Neo4j: la comprobación NO se hizo";

        // SE COMPARA SOLO LO QUE ESTE GRAFO DICE CONOCER. Neo4j puede tener a la vez lo de otro
        // núcleo —la app corriendo mientras el contrato se juzga— y eso no es una infidelidad de
        // ESTE grafo: es otro inquilino. Comparar la base entera hacía fallar la comprobación por
        // la sola presencia del vecino (2026-08-12, medido con la app en marcha).
        //
        // Sigue detectando lo que importa: si a una ubicación conocida le falta o le sobra un
        // elemento, se ve. El sabotaje del contrato —borrar un elemento por detrás— cae aquí.
        var mias = new HashSet<string>(grafo.Ubicaciones(), StringComparer.OrdinalIgnoreCase);

        var enBase = new SortedSet<string>(StringComparer.Ordinal);
        using (var doc = JsonDocument.Parse(cuerpo))
        {
            if (!doc.RootElement.TryGetProperty("results", out var res) || res.GetArrayLength() == 0)
                return "Neo4j no devolvió resultados: la comprobación NO se hizo";
            foreach (var fila in res[0].GetProperty("data").EnumerateArray())
            {
                var row = fila.GetProperty("row");
                string donde = row[0].GetString() ?? "";
                if (!mias.Contains(donde)) continue;
                if (row[1].ValueKind == JsonValueKind.Null) continue;
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
    /// ¿Hay en Neo4j ubicaciones que no son de este grafo? Es decir: ¿lo está usando alguien más?
    /// </summary>
    /// <remarks>
    /// Las comprobaciones de FIDELIDAD y de IDA Y VUELTA necesitan la base para ellas solas: la
    /// segunda restaura TODO lo que haya, así que con la app corriendo se traía su grafo y la
    /// comparación fallaba por la sola presencia del vecino. Un rojo que no significa «el núcleo
    /// está roto» es peor que no comprobar: enseña a desconfiar del juez (2026-08-12).
    /// </remarks>
    public bool HayOtroInquilino(IEnumerable<string> mias)
    {
        string cuerpo = Pedir(JsonSerializer.Serialize(new
        {
            statements = new object[] { new { statement = "MATCH (u:Ubicacion) RETURN u.id" } },
        }));
        if (cuerpo.Length == 0) return false;
        var propias = new HashSet<string>(mias, StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(cuerpo);
            if (!doc.RootElement.TryGetProperty("results", out var res) || res.GetArrayLength() == 0) return false;
            foreach (var fila in res[0].GetProperty("data").EnumerateArray())
            {
                string id = fila.GetProperty("row")[0].GetString() ?? "";
                if (id.Length > 0 && !propias.Contains(id)) return true;
            }
        }
        catch { }
        return false;
    }

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
