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
        bool esPropia = origen.EndsWith("//u.exe", StringComparison.OrdinalIgnoreCase)
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

            // EL CLIC DEBE HABER OCURRIDO EN LA APP DE LA QUE SALE la arista. Sin esto, un clic en
            // el chat de Claude acabó como "acción" de una transición de la barra de tareas
            // (2026-07-30): resolvió, era el último, y aun así no tenía nada que ver. Solo se
            // exige para orígenes uia:// — en web:// el clic legítimo viene del proceso del
            // navegador y la comparación por exe no aplica.
            if (clic != null && clic.Process.Length > 0
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
