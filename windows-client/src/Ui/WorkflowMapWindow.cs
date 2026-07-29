using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using U.Graph;

namespace U.WindowsClient.Ui;

/// <summary>
/// El mapa del grafo pre-aprendido: los workflows dibujados como lo que de verdad son —
/// UBICACIONES unidas por pasos que transicionan. Flota a la derecha, debajo del badge del
/// locator, y el nodo donde estás parado se ilumina mientras navegas.
///
/// Dos workflows que pasan por la misma pantalla comparten NODO sin que nadie los una a mano:
/// el id de superficie es la identidad, así que la concatenación aparece sola en el dibujo.
/// Este mapa es la fase de VER ese grafo; enrutar por él viene después, y conviene en ese
/// orden — el dibujo delata antes que nada los huecos (pasos sin superficie grabada, pantallas
/// duplicadas) que un enrutador se tragaría en silencio.
///
/// Es click-through y sin foco, como <see cref="LocatorBadge"/>, y por la misma razón elevada
/// a crítica: el puente clínico solo actúa con SAP en primer plano. Un mapa que robara un solo
/// clic rompería justo la condición que el resto del sistema necesita.
///
/// La coincidencia nodo↔ubicación usa <see cref="SurfacePlace.Same"/> — la MISMA vara con la
/// que el WorkflowPlayer decide si estás en la pantalla de un paso. Si este mapa ilumina un
/// nodo, el ejecutor estaría de acuerdo; dos varas distintas para "¿estoy aquí?" ya costaron
/// una jornada una vez.
/// </summary>
public sealed class WorkflowMapWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private sealed class Node
    {
        public required string Surface;   // id completo: la identidad del nodo
        public required string Label;     // lo que se pinta
        public int Depth;                 // distancia BFS desde una raíz
        public readonly HashSet<int> Workflows = new(); // qué workflows pasan por aquí
        public Border? Box;               // el visual, para iluminar sin redibujar
    }

    private sealed record Edge(string From, string To, int Workflow);

    private readonly Dictionary<string, Node> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Edge> _edges = new();
    private readonly StackPanel _rows = new() { Orientation = Orientation.Vertical };
    private readonly TextBlock _status = new()
    {
        Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
        FontSize = 10,
        Margin = new Thickness(2, 4, 2, 0),
        TextWrapping = TextWrapping.Wrap,
    };
    private string _currentSurface = "";

    public WorkflowMapWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        IsHitTestVisible = false;
        Focusable = false;
        ShowActivated = false;
        Title = "Ü Mapa";

        var panel = new StackPanel { Orientation = Orientation.Vertical, MaxWidth = 360 };
        panel.Children.Add(new TextBlock
        {
            Text = "🗺 Grafo de workflows",
            Foreground = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(2, 0, 2, 6),
        });
        panel.Children.Add(_rows);
        panel.Children.Add(_status);

        Content = new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0xC8, 0x10, 0x10, 0x14)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8, 10, 8),
            Child = panel,
        };
        SizeChanged += (_, __) => Reposition();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var h = new WindowInteropHelper(this).Handle;
        SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW);
    }

    private void Reposition()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - ActualWidth - 12;
        Top = wa.Top + 48; // debajo del badge del locator, que vive en Top+12
    }

    // ── Construcción del grafo ───────────────────────────────────────────────

    /// <summary>
    /// Reconstruye el grafo desde los workflows guardados. Cada paso lleva su superficie
    /// observada (surfaceHints.observedSurface); los nodos son las superficies DISTINTAS y las
    /// aristas los saltos entre pasos consecutivos que cambian de superficie.
    /// </summary>
    public async Task LoadAsync(GraphClient graph, CancellationToken ct)
    {
        _nodes.Clear();
        _edges.Clear();
        int total = 0, sinSuperficie = 0;

        var list = await graph.ListWorkflowsAsync(ct);
        for (int wi = 0; wi < list.Count; wi++)
        {
            string? id = list[wi].TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            if (string.IsNullOrEmpty(id)) continue;

            JsonElement wf;
            try { wf = await graph.GetWorkflowAsync(id!, ct); }
            catch { continue; } // un workflow que no baja no tumba el mapa

            if (!wf.TryGetProperty("steps", out var steps) || steps.ValueKind != JsonValueKind.Array)
                continue;

            total++;
            string prev = "";
            foreach (var step in steps.EnumerateArray())
            {
                string surface = "";
                if (step.TryGetProperty("surfaceHints", out var hints)
                    && hints.ValueKind == JsonValueKind.Object
                    && hints.TryGetProperty("observedSurface", out var obs))
                    surface = obs.GetString() ?? "";

                // Un paso sin superficie no puede ser nodo — pero se CUENTA: los huecos del
                // grafo son el dato que decide si se puede enrutar con él.
                if (surface.Length == 0) { sinSuperficie++; continue; }

                var node = NodeFor(surface);
                node.Workflows.Add(wi);

                // Arista si la pantalla CAMBIÓ de verdad. Comparar con Same se comía justamente los
                // saltos entre un marco y su subpantalla —«entrar a la lista de trabajo»—, que son
                // transiciones reales y de las que más importan para enrutar.
                if (prev.Length > 0 && !string.Equals(KeyOf(surface), KeyOf(prev), StringComparison.OrdinalIgnoreCase))
                    _edges.Add(new Edge(KeyOf(prev), KeyOf(surface), wi));
                prev = surface;
            }
        }

        AssignDepths();
        Dispatcher.Invoke(() =>
        {
            Render();
            _status.Text = $"{total} workflow(s) · {_nodes.Count} pantalla(s) · {_edges.Count} salto(s)"
                + (sinSuperficie > 0 ? $" · ⚠ {sinSuperficie} paso(s) sin superficie grabada" : "");
            SetCurrent(_currentSurface); // re-ilumina tras redibujar
        });
    }

    /// <summary>
    /// La identidad de un NODO es su id de superficie exacto, no <see cref="SurfacePlace.Same"/>.
    ///
    /// Usar Same aquí fue un error, y uno que se vio en pantalla: Same es ASIMÉTRICO a propósito
    /// —da verdadero cuando lo grabado es un PREFIJO de donde estás, para que una grabación vieja
    /// y menos específica siga casando—. Eso lo hace la vara correcta para «¿este paso vive en mi
    /// pantalla?» y una relación de equivalencia pésima: fundía la lista de trabajo
    /// (<c>…/0100/ssubVIEW_SCREEN:SAPLN1LSTAMB:0007</c>) con el marco que la contiene
    /// (<c>…/0100</c>) en un solo nodo, y con ellas el salto entre las dos.
    ///
    /// Nodos exactos, y la tolerancia de prefijo se aplica SOLO al iluminar, que es donde
    /// significa lo que debe significar. Ver <see cref="SetCurrent"/>.
    /// </summary>
    private static string KeyOf(string surface) => (surface ?? "").Trim().TrimEnd('/');

    private Node NodeFor(string surface)
    {
        string key = KeyOf(surface);
        if (_nodes.TryGetValue(key, out var found)) return found;

        var node = new Node { Surface = key, Label = ShortLabel(key) };
        _nodes[key] = node;
        return node;
    }

    /// <summary>«sapgui://QAS/NWP1/SAPLY000/0001» → «NWP1 · SAPLY000/0001». Legible, no exhaustivo.</summary>
    private static string ShortLabel(string surface)
    {
        string s = surface;
        int scheme = s.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) s = s[(scheme + 3)..];
        var parts = s.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 1) return s;
        // host fuera (QAS, dominio…): lo que distingue pantallas es el camino
        var path = parts.Skip(1).ToArray();
        if (path.Length <= 2) return string.Join("/", path);
        return path[0] + " · " + string.Join("/", path.Skip(Math.Max(1, path.Length - 2)));
    }

    private void AssignDepths()
    {
        // Raíces: nodos sin arista entrante (donde arrancan los workflows). BFS desde ahí.
        var incoming = new HashSet<string>(_edges.Select(e => e.To), StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<Node>();
        foreach (var n in _nodes.Values)
        {
            n.Depth = int.MaxValue;
            if (!incoming.Contains(n.Surface)) { n.Depth = 0; queue.Enqueue(n); }
        }
        // Grafo cíclico o sin raíz clara: que nadie se quede sin fila
        if (queue.Count == 0 && _nodes.Count > 0)
        {
            var first = _nodes.Values.First();
            first.Depth = 0; queue.Enqueue(first);
        }
        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            foreach (var e in _edges.Where(e => string.Equals(e.From, n.Surface, StringComparison.OrdinalIgnoreCase)))
            {
                var to = NodeFor(e.To);
                if (to.Depth > n.Depth + 1) { to.Depth = n.Depth + 1; queue.Enqueue(to); }
            }
        }
        foreach (var n in _nodes.Values)
            if (n.Depth == int.MaxValue) n.Depth = 0;
    }

    // ── Dibujo ───────────────────────────────────────────────────────────────

    private void Render()
    {
        _rows.Children.Clear();
        foreach (var fila in _nodes.Values.GroupBy(n => n.Depth).OrderBy(g => g.Key))
        {
            if (fila.Key > 0)
                _rows.Children.Add(new Path
                {
                    Data = Geometry.Parse("M 0 0 L 4 6 L 8 0"),
                    Stroke = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
                    StrokeThickness = 1.5,
                    Margin = new Thickness(18, 2, 0, 2),
                });

            var row = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var n in fila.OrderBy(n => n.Label, StringComparer.OrdinalIgnoreCase))
            {
                var box = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(7, 3, 7, 3),
                    Margin = new Thickness(0, 2, 6, 2),
                    Child = new TextBlock
                    {
                        Text = n.Label + (n.Workflows.Count > 1 ? $"  ×{n.Workflows.Count}" : ""),
                        Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
                        FontSize = 10.5,
                        FontFamily = new FontFamily("Consolas"),
                        // Un nodo compartido es el hallazgo del mapa: ahí se concatenan workflows
                        ToolTip = n.Surface,
                    },
                };
                n.Box = box;
                row.Children.Add(box);
            }
            _rows.Children.Add(row);
        }
    }

    /// <summary>
    /// Ilumina el nodo de la ubicación actual, y solo UNO: el MÁS ESPECÍFICO de los que casan.
    ///
    /// Aquí sí manda <see cref="SurfacePlace.Same"/> —con su tolerancia de prefijo— porque la
    /// pregunta es la del ejecutor: «¿este nodo describe donde estoy?». Pero varios nodos pueden
    /// responder que sí a la vez: estando en <c>…/0100/ssubVIEW_SCREEN:…</c> casan tanto esa
    /// pantalla como el marco <c>…/0100</c> que la contiene. Encender el marco fue el desajuste
    /// que se vio: el mapa señalaba un nodo y la barra de direcciones decía otro. Gana el de
    /// pathname más largo, que es el que de verdad describe dónde estás.
    /// </summary>
    public void SetCurrent(string surfaceId)
    {
        _currentSurface = surfaceId ?? "";

        Node? actual = null;
        if (_currentSurface.Length > 0)
        {
            int mejor = -1;
            foreach (var n in _nodes.Values)
            {
                if (!SurfacePlace.Same(_currentSurface, n.Surface)) continue;
                int especificidad = SurfacePlace.PathnameOf(n.Surface).Length;
                if (especificidad > mejor) { mejor = especificidad; actual = n; }
            }
        }

        foreach (var n in _nodes.Values)
        {
            if (n.Box == null) continue;
            bool aqui = ReferenceEquals(n, actual);
            n.Box.Background = new SolidColorBrush(aqui
                ? Color.FromArgb(0xE0, 0x2E, 0x7D, 0x32)     // verde: estás aquí
                : Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF));
            n.Box.BorderBrush = new SolidColorBrush(aqui
                ? Color.FromArgb(0xFF, 0x66, 0xBB, 0x6A)
                : Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        }
    }
}
