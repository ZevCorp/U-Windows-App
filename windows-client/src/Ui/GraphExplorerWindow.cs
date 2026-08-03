using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Runtime.InteropServices;
using U.WindowsClient.Actions;
using U.WindowsClient.Diagnostics;
using U.WindowsClient.Navigation;
using U.WindowsClient.Uia;

namespace U.WindowsClient.Ui;

/// <summary>
/// El grafo, tocable: la superficie de pruebas de la navegación real.
///
/// Cambio de principio (2026-07-31, decidido por el usuario y con razón): en vez de INFERIR el
/// grafo viendo al usuario clicar —que obliga a adivinar qué clic causó qué transición, la
/// pregunta que siete iteraciones de guardas no cerraron—, se EXPLORA. La ventana actual expone
/// sus elementos en tiempo real como aristas potenciales; pulsar una aquí pulsa el elemento REAL,
/// y si la pantalla cambia, la arista se aprende con su acción. Sin atribución que adivinar: la
/// certeza es absoluta porque la acción la ejecutó el propio sistema.
///
/// La frontera que esto NO cruza: explora el HUMANO, nunca un crawler. Pulsar botones reales
/// tiene efectos reales —un explorador autónomo podría pulsar «Vaciar papelera»— así que cada
/// arista se recorre porque el usuario la clicó en este panel. Ese clic es el consentimiento.
///
/// El hover ilumina el elemento real en pantalla (overlay click-through, como el inspector), para
/// que "arista del grafo" y "botón de verdad" se vean como la misma cosa antes de tocar nada.
/// </summary>
public sealed class GraphExplorerWindow : Window
{
    private readonly SurfaceMap _map;
    private readonly Func<SurfaceLocator.SurfaceLocation?> _where;
    // Lector propio, como hace el inspector: compartir el del agente mezclaría el estado mutable
    // (Elements) entre el refresco periódico y los turnos del cerebro.
    private readonly UiaReader _reader = new();
    private readonly HighlightOverlay _overlay = new();
    /// <summary>El MISMO ejecutor que corre los workflows: lo que se explora se pulsa igual que se ejecutará.</summary>
    private readonly U.Graph.Surfaces.UiaSurface _ejecutor = new() { Log = s => LogBus.Log("explorador", s) };

    private readonly TextBlock _nodeTitle = new()
    {
        Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.Bold,
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
    };
    private readonly TextBlock _status = new()
    {
        Foreground = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
        FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
    };
    private readonly StackPanel _edges = new();
    private readonly System.Windows.Threading.DispatcherTimer _refresh;
    private string _signature = "";   // para no redibujar (y matar el hover) si nada cambió
    private bool _busy;               // recorriendo una arista: el refresco espera
    private readonly Button _crawlBtn;
    private readonly Button _collapseBtn;
    private readonly Button _graphBtn;
    private ScrollViewer _lista = null!;
    private ScrollViewer _grafo = null!;
    private readonly Canvas _lienzo = new() { Background = Brushes.Transparent };
    private bool _collapsed;
    private bool _graphView;
    private string _nodoActual = "";
    /// <summary>Lo aprendido en la última corrida automática: lo único que la vista de grafo dibuja.</summary>
    private List<(string From, string To, string Label)> _ultimaCorrida = new();
    private CancellationTokenSource? _crawlCts;

    public GraphExplorerWindow(SurfaceMap map, Func<SurfaceLocator.SurfaceLocation?> where)
    {
        _map = map;
        _where = where;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Width = 380;
        Height = 560;
        Title = "Ü Explorador del grafo";

        // Cabecera con el plegador. Colapsado deja SOLO el botón de mapear: mientras el recorrido
        // corre, la lista de aristas es ruido —cambia cada segundo— y lo único que hace falta a
        // mano es poder pararlo. Además tapa menos la app que se está explorando.
        _collapseBtn = new Button
        {
            Content = "▾", Width = 22, Height = 20, Padding = new Thickness(0),
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            Foreground = Brushes.White, BorderThickness = new Thickness(0),
            FontSize = 10, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Top,
            ToolTip = "Plegar / desplegar el explorador",
        };
        _collapseBtn.Click += (_, __) => SetCollapsed(!_collapsed);

        // Vista de grafo: SOLO lo aprendido en la última corrida automática. El terreno completo
        // mezcla lo observado con lo explorado y arrastra ramas de otras apps y de otros días; lo
        // que se quiere ver aquí es qué comprobó ESTE recorrido, que es lo único de lo que se
        // puede afirmar que se pulsó y llegó.
        _graphBtn = new Button
        {
            Content = "🕸", Width = 22, Height = 20, Padding = new Thickness(0),
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            Foreground = Brushes.White, BorderThickness = new Thickness(0),
            FontSize = 10, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 4, 0),
            ToolTip = "Ver como grafo lo mapeado en la última corrida automática",
        };
        _graphBtn.Click += (_, __) => SetGraphView(!_graphView);

        var titulo = new TextBlock
        {
            Text = "🕸 Explorador del grafo",
            Foreground = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)),
            FontSize = 11.5, FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap, Cursor = Cursors.SizeAll,
            VerticalAlignment = VerticalAlignment.Center,
        };
        titulo.MouseLeftButtonDown += (_, __) => { try { DragMove(); } catch { } };

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(_collapseBtn, Dock.Right);
        header.Children.Add(_collapseBtn);
        DockPanel.SetDock(_graphBtn, Dock.Right);
        header.Children.Add(_graphBtn);
        header.Children.Add(titulo);

        // Mapeo AUTÓNOMO de la app que esté delante. Vive aquí, junto al recorrido manual, porque
        // son el mismo gesto a dos velocidades: uno lo conduce el usuario, el otro el sistema.
        _crawlBtn = new Button
        {
            Content = "🤖 Mapear esta app automáticamente",
            Height = 26,
            Margin = new Thickness(0, 0, 0, 8),
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontSize = 11,
            Cursor = Cursors.Hand,
            ToolTip = "Recorre la app abriendo lo que encuentra. Solo navegación: nunca pulsa botones ni menús.",
        };
        _crawlBtn.Click += (_, __) => _ = CrawlAsync();

        var panel = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        DockPanel.SetDock(_crawlBtn, Dock.Top);
        panel.Children.Add(_crawlBtn);
        DockPanel.SetDock(_nodeTitle, Dock.Top);
        panel.Children.Add(_nodeTitle);
        DockPanel.SetDock(_status, Dock.Bottom);
        panel.Children.Add(_status);
        _lista = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _edges,
        };
        _grafo = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _lienzo,
            Visibility = Visibility.Collapsed,
        };
        var pila = new Grid();
        pila.Children.Add(_lista);
        pila.Children.Add(_grafo);
        panel.Children.Add(pila);

        Content = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x10, 0x10, 0x14)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            Child = panel,
        };

        var wa = SystemParameters.WorkArea;
        Left = wa.Left + 16;
        Top = wa.Top + 60;

        // 1 s y con candado de no-solape: leer el árbol UIA de la ventana activa no es gratis, y
        // dos lecturas montadas es como el inspector ya aprendió a no hacerlo.
        _refresh = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refresh.Tick += (_, __) => RefreshEdges();
        _refresh.Start();
        Closed += (_, __) => { _refresh.Stop(); _overlay.Close(); };
        _overlay.Show();
    }

    // ── Aristas en tiempo real ───────────────────────────────────────────────

    /// <summary>Tipos que son una ACCIÓN al alcance de un clic. El resto es contenido o cromo.</summary>
    private static readonly HashSet<string> Clickable = new(StringComparer.OrdinalIgnoreCase)
    {
        "button", "hyperlink", "listitem", "treeitem", "tabitem", "menuitem",
        "splitbutton", "checkbox", "radiobutton", "combobox", "image",
    };

    private bool _reading;

    private void RefreshEdges()
    {
        if (_busy || _reading) return;
        _reading = true;
        Task.Run(() =>
        {
            try
            {
                _reader.Read(); // lee la ventana en PRIMER PLANO
                string proc = _reader.ForegroundProcess;
                var els = _reader.Elements
                    .Where(e => e.Label.Length > 0 && Clickable.Contains(e.ControlType))
                    .Take(48)
                    .ToList();
                Dispatcher.BeginInvoke(() => Render(proc, els));
            }
            catch { }
            finally { _reading = false; }
        });
    }

    private void Render(string proc, List<UiaReader.UiElement> els)
    {
        // La UI de Ü delante (este panel incluido): congelar lo último útil en vez de listarse a
        // sí misma — el observador no es terreno, regla vieja ya.
        if (proc.Equals("U", StringComparison.OrdinalIgnoreCase)) return;

        var loc = _where();
        string aqui = loc?.Id ?? "";
        var conocidas = aqui.Length > 0
            // Agrupando por etiqueta, no ToDictionary: desde que se registran TODAS las puertas
            // visibles, una pantalla puede tener dos salidas con el mismo nombre —el mismo archivo
            // en el panel y en la lista, dos elementos homónimos— y ToDictionary revienta con
            // «An item with the same key has already been added» al terminar el mapeo (2026-08-01).
            // Que dos puertas se llamen igual es normal; que la app se caiga por ello, no.
            ? _map.ExitsFrom(aqui).Where(h => h.Info.Selector.Length > 0)
                  .GroupBy(h => h.Info.Label, StringComparer.OrdinalIgnoreCase)
                  .ToDictionary(g => g.Key, g => g.First().To, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string firma = aqui + "|" + string.Join("|", els.Select(e => e.Label + ":" + e.ControlType));
        if (firma == _signature) return; // nada cambió: no matar el hover redibujando
        _signature = firma;

        _nodeTitle.Text = aqui.Length > 0 ? "◉ " + aqui : "◉ (sin superficie)";
        _edges.Children.Clear();

        foreach (var el in els)
        {
            bool sabida = conocidas.TryGetValue(el.Label, out string? destino);
            var chip = new Border
            {
                CornerRadius = new CornerRadius(6),
                // Verde = arista ya recorrida (se sabe a dónde lleva); gris = potencial, sin explorar.
                Background = new SolidColorBrush(sabida
                    ? Color.FromArgb(0x30, 0x2E, 0x7D, 0x32) : Color.FromArgb(0x1C, 0xC0, 0xC0, 0xC0)),
                BorderBrush = new SolidColorBrush(sabida
                    ? Color.FromArgb(0x55, 0x66, 0xBB, 0x6A) : Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 2, 0, 2),
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = (sabida ? "→ " : "· ") + el.Label
                         + (sabida ? $"   ⇒ {Corto(destino!)}" : $"   ({el.ControlType})"),
                    Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
                    FontSize = 11, FontFamily = new FontFamily("Consolas"),
                    TextWrapping = TextWrapping.Wrap,
                },
            };

            var elemento = el; // captura por arista, no la variable del bucle
            chip.MouseEnter += (_, __) => _overlay.ShowRect(elemento.Bounds);
            chip.MouseLeave += (_, __) => _overlay.HideRect();
            chip.MouseLeftButtonUp += (_, __) => _ = TraverseAsync(elemento, aqui);
            _edges.Children.Add(chip);
        }

        _status.Text = $"{els.Count} arista(s) a la vista · {conocidas.Count} ya recorrida(s) desde aquí";
    }

    private static string Corto(string id)
    {
        int i = id.IndexOf("://", StringComparison.Ordinal);
        return i >= 0 ? id[(i + 3)..] : id;
    }

    /// <summary>
    /// Plegado: solo cabecera, botón de mapear y el estado de una línea. El estado se conserva
    /// aunque esté plegado porque es donde se lee el progreso del recorrido — plegar es para
    /// estorbar menos, no para quedarse a ciegas.
    /// </summary>
    private void SetCollapsed(bool colapsar)
    {
        _collapsed = colapsar;
        var v = colapsar ? Visibility.Collapsed : Visibility.Visible;
        _nodeTitle.Visibility = v;
        _lista.Visibility = v;
        _collapseBtn.Content = colapsar ? "▸" : "▾";
        // Al plegar SÍ se encoge, porque el usuario lo pidió; al mapear NO. El encogido molesto que
        // se veía al iniciar el recorrido era este SetCollapsed disparado por el propio mapeo.
        Height = colapsar ? 116 : 560;
        Width = colapsar ? 300 : 380;
    }

    private void SetGraphView(bool grafo)
    {
        _graphView = grafo;
        _graphBtn.Background = new SolidColorBrush(grafo
            ? Color.FromArgb(0x55, 0x66, 0xBB, 0x6A) : Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
        if (_collapsed) SetCollapsed(false);
        _lista.Visibility = grafo ? Visibility.Collapsed : Visibility.Visible;
        _grafo.Visibility = grafo ? Visibility.Visible : Visibility.Collapsed;
        _nodeTitle.Visibility = grafo ? Visibility.Collapsed : Visibility.Visible;
        if (grafo) DibujarGrafo();
    }

    /// <summary>
    /// Dibuja el grafo de la ÚLTIMA corrida automática: nodos en filas por distancia a la raíz y
    /// aristas etiquetadas con lo que se pulsó.
    ///
    /// Por filas y no con un layout de fuerzas: un recorrido en profundidad produce un árbol con
    /// vuelta atrás, y en un árbol la distancia a la raíz ES la información —cuánto hay que bajar
    /// para llegar—. Un grafo de resortes lo taparía moviendo los nodos a donde quepan.
    /// </summary>
    private void DibujarGrafo()
    {
        _lienzo.Children.Clear();
        if (_ultimaCorrida.Count == 0)
        {
            _lienzo.Children.Add(new TextBlock
            {
                Text = "Todavía no hay ninguna corrida automática.\nPulsa «Mapear esta app automáticamente».",
                Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
                FontSize = 11, Margin = new Thickness(8),
            });
            _lienzo.Width = 300; _lienzo.Height = 80;
            return;
        }

        // Raíz: el origen de la primera arista aprendida (donde arrancó el recorrido).
        string raiz = _ultimaCorrida[0].From;
        var prof = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [raiz] = 0 };
        // Varias pasadas: una arista puede aprenderse antes de que su origen tenga profundidad.
        for (int pasada = 0; pasada < 6; pasada++)
            foreach (var (f, t, _) in _ultimaCorrida)
                if (prof.TryGetValue(f, out int d) && (!prof.TryGetValue(t, out int dt) || dt > d + 1))
                    prof[t] = d + 1;
        foreach (var (f, t, _) in _ultimaCorrida)
        {
            if (!prof.ContainsKey(f)) prof[f] = 0;
            if (!prof.ContainsKey(t)) prof[t] = 1;
        }

        const double anchoCaja = 168, altoCaja = 34, sepX = 16, sepY = 62;
        var pos = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
        double maxX = 0;
        foreach (var fila in prof.GroupBy(kv => kv.Value).OrderBy(g => g.Key))
        {
            int i = 0;
            foreach (var kv in fila.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                double x = 12 + i * (anchoCaja + sepX);
                double y = 12 + fila.Key * sepY;
                pos[kv.Key] = new Point(x, y);
                maxX = Math.Max(maxX, x + anchoCaja);
                i++;
            }
        }

        // Las aristas DEDUCIDAS, en gris y a trazos: no se recorrieron en esta corrida, pero el
        // mismo botón ya se cruzó desde otra pantalla, así que sabemos a dónde lleva desde aquí.
        // Son las que convierten la estrella en malla, y verlas es la diferencia entre creer que
        // el grafo tiene forma y comprobarlo.
        var dibujadas = new HashSet<string>(_ultimaCorrida.Select(e => e.From + "\n" + e.To), StringComparer.OrdinalIgnoreCase);
        foreach (var nodo in pos.Keys.ToList())
        {
            // PUERTAS SIN CRUZAR: salidas que existen y cuyo destino aún no se conoce. Se dibujan
            // como un muñón ámbar saliendo del nodo, porque son la FRONTERA del mapa —lo que
            // queda por descubrir— y no verlas hacía parecer terminado un terreno que no lo está.
            int pendiente = 0;
            foreach (var h in _map.ExitsFrom(nodo))
            {
                if (!SurfaceMap.EsPuerta(h.To)) continue;
                if (!pos.TryGetValue(nodo, out var origen)) continue;
                if (pendiente >= 6) break;                            // un puñado basta para leerlo
                double x = origen.X + 14 + pendiente * 13;
                _lienzo.Children.Add(new System.Windows.Shapes.Line
                {
                    X1 = x, Y1 = origen.Y + altoCaja,
                    X2 = x, Y2 = origen.Y + altoCaja + 13,
                    Stroke = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xB3, 0x00)),
                    StrokeThickness = 2,
                    ToolTip = $"puerta sin cruzar: «{h.Info.Label}» (destino desconocido)",
                });
                pendiente++;
            }

            foreach (var h in _map.ExitsFrom(nodo))
            {
                if (SurfaceMap.EsPuerta(h.To)) continue;              // ya dibujada arriba
                if (!pos.ContainsKey(h.To)) continue;                 // el otro extremo no está en pantalla
                if (!dibujadas.Add(h.From + "\n" + h.To)) continue;   // ya la dibujó la corrida
                if (!pos.TryGetValue(h.From, out var p1) || !pos.TryGetValue(h.To, out var p2)) continue;

                _lienzo.Children.Add(new System.Windows.Shapes.Line
                {
                    X1 = p1.X + anchoCaja / 2, Y1 = p1.Y + altoCaja,
                    X2 = p2.X + anchoCaja / 2, Y2 = p2.Y,
                    Stroke = new SolidColorBrush(Color.FromArgb(0x44, 0xAA, 0xAA, 0xAA)),
                    StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 },
                    ToolTip = $"«{h.Info.Label}» (deducida)  {Corto(h.From)} → {Corto(h.To)}",
                });
            }
        }

        // Aristas primero, para que las cajas queden encima de las líneas.
        foreach (var (f, t, label) in _ultimaCorrida)
        {
            if (!pos.TryGetValue(f, out var a) || !pos.TryGetValue(t, out var b)) continue;
            var linea = new System.Windows.Shapes.Line
            {
                X1 = a.X + anchoCaja / 2, Y1 = a.Y + altoCaja,
                X2 = b.X + anchoCaja / 2, Y2 = b.Y,
                Stroke = new SolidColorBrush(Color.FromArgb(0x66, 0x66, 0xBB, 0x6A)),
                StrokeThickness = 1.4,
                ToolTip = $"«{label}»  {Corto(f)} → {Corto(t)}",
            };
            _lienzo.Children.Add(linea);

            var et = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0x8B, 0xD9, 0x9B)),
                FontSize = 9.5, FontFamily = new FontFamily("Consolas"),
                Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x10, 0x10, 0x14)),
                Padding = new Thickness(3, 0, 3, 0),
            };
            Canvas.SetLeft(et, (linea.X1 + linea.X2) / 2 - 24);
            Canvas.SetTop(et, (linea.Y1 + linea.Y2) / 2 - 8);
            _lienzo.Children.Add(et);
        }

        foreach (var kv in pos)
        {
            // El nodo donde está el recorrido ahora mismo va en ámbar y con borde grueso: durante
            // un mapeo en vivo, saber DÓNDE está es tan informativo como ver aparecer las aristas.
            bool esActual = string.Equals(kv.Key, _nodoActual, StringComparison.OrdinalIgnoreCase);
            var caja = new Border
            {
                Width = anchoCaja, Height = altoCaja,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(esActual
                    ? Color.FromArgb(0x55, 0xFF, 0xB3, 0x00)
                    : Color.FromArgb(0x30, 0x2E, 0x7D, 0x32)),
                BorderBrush = new SolidColorBrush(esActual
                    ? Color.FromArgb(0xEE, 0xFF, 0xC1, 0x07)
                    : Color.FromArgb(0x55, 0x66, 0xBB, 0x6A)),
                BorderThickness = new Thickness(esActual ? 2 : 1),
                ToolTip = kv.Key,
                Child = new TextBlock
                {
                    Text = Corto(kv.Key),
                    Foreground = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)),
                    FontSize = 9.5, FontFamily = new FontFamily("Consolas"),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(6, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            Canvas.SetLeft(caja, kv.Value.X);
            Canvas.SetTop(caja, kv.Value.Y);
            _lienzo.Children.Add(caja);
        }

        _lienzo.Width = Math.Max(maxX + 12, 320);
        _lienzo.Height = 24 + (prof.Values.Max() + 1) * sepY;
        // Durante el mapeo el estado lo escribe el propio recorrido («explorando X · N pantallas»),
        // que dice más que un recuento: no se pisa.
        if (_crawlCts == null)
            _status.Text = $"grafo de la última corrida · {pos.Count} pantalla(s), {_ultimaCorrida.Count} ruta(s)";
    }

    // ── Mapeo autónomo ───────────────────────────────────────────────────────

    /// <summary>
    /// Lanza el recorrido automático. El botón se convierte en «Detener» mientras corre: parar
    /// tiene que estar a un clic, sin buscarlo, porque esto mueve el ratón y el foco de la máquina.
    /// </summary>
    private async Task CrawlAsync()
    {
        if (_crawlCts != null) { _crawlCts.Cancel(); return; }

        var loc = _where();
        if (loc == null) { _status.Text = "trae al frente la app que quieres mapear"; return; }

        _crawlCts = new CancellationTokenSource();
        _crawlBtn.Content = "⏹ Detener el mapeo";
        _busy = true;   // el refresco de aristas no compite con el recorrido
        try
        {
            // Se pasa a la vista de grafo al empezar: el mapeo es lo que hay que mirar mientras
            // ocurre, y la lista de aristas de la pantalla actual no dice nada durante el recorrido.
            _ultimaCorrida.Clear();
            _nodoActual = "";
            if (!_graphView) SetGraphView(true); else DibujarGrafo();

            var crawler = new GraphCrawler(_map, _where);
            crawler.Progress += (que, nodos, aristas) => Dispatcher.BeginInvoke(() =>
                _status.Text = $"{que} · {nodos} pantalla(s), {aristas} ruta(s)");

            // Grafo en vivo: cada arista aparece en cuanto se aprende, y el nodo donde está el
            // recorrido se resalta. BeginInvoke porque el crawler corre fuera del hilo de UI.
            crawler.EdgeLearned += (de, a, etiqueta) => Dispatcher.BeginInvoke(() =>
            {
                _ultimaCorrida.Add((de, a, etiqueta));
                DibujarGrafo();
            });
            crawler.NodeEntered += nodo => Dispatcher.BeginInvoke(() =>
            {
                _nodoActual = nodo;
                DibujarGrafo();
            });

            // 120 nodos y 4 niveles: suficiente para el árbol de carpetas del usuario sin que un
            // recorrido se eternice. El techo no es una limitación técnica, es una promesa que se
            // puede cumplir sobre la máquina de alguien.
            string r = await crawler.CrawlAsync(120, 4, _crawlCts.Token);
            _nodoActual = "";
            DibujarGrafo();
            _status.Text = r;
            LogBus.Log("explorador", "mapeo automático: " + r);
        }
        catch (Exception ex) { _status.Text = "el mapeo falló: " + ex.Message; }
        finally
        {
            _crawlCts?.Dispose();
            _crawlCts = null;
            _crawlBtn.Content = "🤖 Mapear esta app automáticamente";
            _busy = false;
            _signature = "";
        }
    }

    // ── Recorrer una arista ──────────────────────────────────────────────────

    /// <summary>
    /// Pulsa el elemento REAL y, si la pantalla cambia, aprende la arista con su acción. Aquí no
    /// hay atribución que adivinar: la acción la ejecutamos nosotros, así que la certeza es
    /// absoluta — y por eso la acción explorada SOBREESCRIBE cualquier acción observada, que como
    /// mucho llega al 78% de fiabilidad medida.
    /// </summary>
    private async Task TraverseAsync(UiaReader.UiElement el, string desde)
    {
        if (_busy) return;
        _busy = true;
        _overlay.HideRect();
        _status.Text = $"pulsando «{el.Label}»…";
        try
        {
            // MISMO camino que la ejecución de tareas. Explorar con un método y ejecutar con otro
            // haría que lo aprendido no garantizara nada: la arista diría «se llega pulsando esto»
            // habiéndolo pulsado de una forma que el ejecutor no usa. Se resuelve el selector aquí
            // y se pulsa con UiaSurface.Execute — el mismo que corre los workflows.
            var (lbl, ct, sels) = U.Graph.Surfaces.UiaSurface.DescribeElement(el.Native);
            var selectores = sels.Where(s => !s.EndsWith("path=", StringComparison.Ordinal)).ToList();
            if (selectores.Count == 0) { _status.Text = $"«{el.Label}» no tiene identidad utilizable"; return; }

            var paso = new U.Graph.PlanStep
            {
                StepOrder = 1,
                ActionType = "click",
                Selector = selectores[0],
                Label = lbl.Length > 0 ? lbl : el.Label,
            };
            bool pulsado = await Task.Run(() => _ejecutor.Execute(paso, out _));
            if (!pulsado)
            {
                _status.Text = $"«{el.Label}» no aceptó el clic (ni Invoke ni tap)";
                return;
            }

            // ¿Navegó? La UI tarda; se espera con paciencia y sin suponer nada.
            string llegue = "";
            for (int i = 0; i < 18; i++)
            {
                await Task.Delay(200);
                string ahora = _where()?.Id ?? "";
                if (ahora.Length > 0 && !string.Equals(ahora, desde, StringComparison.OrdinalIgnoreCase))
                {
                    llegue = ahora;
                    break;
                }
            }

            if (llegue.Length == 0)
            {
                _status.Text = $"«{el.Label}»: la pantalla no cambió (acción local, no navegación)";
                return;
            }

            // Se aprende EL MISMO selector con el que se acaba de pulsar: la arista promete
            // exactamente lo que se ejecutó, ni más ni menos.
            _map.LearnTraversal(desde, llegue,
                selectores[0], selectores.Skip(1).ToArray(),
                paso.Label, ct.Length > 0 ? ct : el.ControlType);

            _status.Text = $"✓ «{el.Label}» → {Corto(llegue)} — arista aprendida";
            LogBus.Log("explorador", $"arista recorrida y aprendida: «{el.Label}» lleva de '{desde}' a '{llegue}'");
        }
        finally
        {
            _busy = false;
            _signature = ""; // la pantalla cambió: redibujar en el próximo tick
        }
    }

}

/// <summary>
/// Un solo recuadro, click-through, sobre el elemento real: el puente visual entre la arista del
/// grafo y el botón de verdad. Coordenadas físicas de UIA → DIPs con la transform del propio HWND,
/// el mismo truco del inspector — sin esto, con escalado de pantalla el recuadro cae desplazado.
/// </summary>
public sealed class HighlightOverlay : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private readonly Canvas _canvas = new();
    private readonly System.Windows.Shapes.Rectangle _rect = new()
    {
        Stroke = new SolidColorBrush(Color.FromArgb(0xFF, 0xA9, 0x6B, 0xF6)), // violeta del inspector
        StrokeThickness = 3,
        Fill = new SolidColorBrush(Color.FromArgb(0x28, 0xA9, 0x6B, 0xF6)),
        Visibility = Visibility.Collapsed,
    };

    public HighlightOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = false;
        Focusable = false;
        ShowActivated = false;
        Left = 0; Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;
        _canvas.Children.Add(_rect);
        Content = _canvas;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var h = new WindowInteropHelper(this).Handle;
        SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW);
    }

    public void ShowRect(Rect fisico)
    {
        var src = PresentationSource.FromVisual(this);
        Matrix m = src?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var tl = m.Transform(new Point(fisico.X, fisico.Y));
        var br = m.Transform(new Point(fisico.Right, fisico.Bottom));
        Canvas.SetLeft(_rect, tl.X);
        Canvas.SetTop(_rect, tl.Y);
        _rect.Width = Math.Max(0, br.X - tl.X);
        _rect.Height = Math.Max(0, br.Y - tl.Y);
        _rect.Visibility = Visibility.Visible;
    }

    public void HideRect() => _rect.Visibility = Visibility.Collapsed;
}
