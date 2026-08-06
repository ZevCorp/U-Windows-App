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
    /// <summary>
    /// Los puntos de las salidas, cada uno SOBRE su elemento real.
    ///
    /// Estaban agrupados en una tira arriba a la izquierda, y eso obligaba a traducir mentalmente
    /// entre «el punto número siete» y «ese botón de allí»: la relación existía —hover iluminaba el
    /// elemento— pero había que buscarla. Encima de la pantalla no hace falta buscar nada: el punto
    /// ESTÁ en el sitio del que habla, así que lo que el mapa sabe y lo que se ve son la misma
    /// imagen (2026-08-04). Es lo que ya permitía la capa a pantalla completa y no se aprovechaba.
    /// </summary>
    private readonly Canvas _edges = new();

    /// <summary>La tira de niveles del borde derecho: una app por nivel. Ver <see cref="DibujarNiveles"/>.</summary>
    private StackPanel _niveles = null!;

    /// <summary>Se enciende en ámbar mientras la capa acepta el ratón (Ctrl+Shift).</summary>
    private Border _marco = null!;
    private readonly System.Windows.Threading.DispatcherTimer _refresh;
    private string _signature = "";   // para no redibujar (y matar el hover) si nada cambió
    private bool _busy;               // recorriendo una arista: el refresco espera
    private readonly Button _crawlBtn;
    private Button _carruselBtn = null!;
    private Button _limpiarBtn = null!;
    private CarruselDeApps? _carrusel;

    /// <summary>
    /// Borra el grafo entero, preguntando antes. Borrar lo aprendido no se deshace.
    /// </summary>
    private void LimpiarGrafo()
    {
        var r = MessageBox.Show(
            "Se va a borrar TODO lo aprendido: pantallas, puertas y niveles, de todas las "
            + "aplicaciones.\n\nEsto no se puede deshacer. ¿Empezamos de cero?",
            "Borrar el grafo", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (r != MessageBoxResult.Yes) return;

        var (nodos, aristas) = _map.OlvidarTodo();
        _ultimaCorrida.Clear();
        _nodoActual = "";
        _signature = "";              // que el repintado no se salte por «nada ha cambiado»
        _status.Text = $"grafo a cero: borradas {nodos} pantalla(s) y {aristas} puerta(s)";
        RefreshEdges();
        if (_graphView) DibujarGrafo();
    }

    /// <summary>
    /// Pintar un NÚMERO en cada puerta. Se enciende mientras se le enseña una app a un modelo de
    /// visión: es lo que le permite señalar «el 7» en vez de «ese de la izquierda».
    ///
    /// Apagado el resto del tiempo, a propósito: un número encima de cada elemento es ruido cuando
    /// nadie lo está leyendo, y esto vive permanentemente encima de la app del usuario.
    /// </summary>
    public bool Numerar { get; set; }

    private readonly Dictionary<int, UiaReader.UiElement> _numeradas = new();

    /// <summary>Lo que hay numerado ahora mismo, para quien tenga que traducir «el 7» a un elemento
    /// de verdad. Copia: la lista se rehace en cada refresco.</summary>
    public IReadOnlyDictionary<int, UiaReader.UiElement> PuertasNumeradas() =>
        new Dictionary<int, UiaReader.UiElement>(_numeradas);
    private readonly Button _collapseBtn;
    private readonly Button _graphBtn;
    private ScrollViewer _lista = null!;
    private ScrollViewer _grafo = null!;
    /// <summary>Lo único sólido: título, botones y estado. Vive en <see cref="_ventanaBarra"/>.</summary>
    private Border _barra = null!;

    /// <summary>La ventana de la barra: lo único de esta vista que se puede tocar.</summary>
    private Window _ventanaBarra = null!;
    private readonly Canvas _lienzo = new() { Background = Brushes.Transparent };
    private bool _collapsed;
    private bool _graphView;
    /// <summary>El grafo se dibuja como mapa de puntos porque el detalle ya no cabría legible.</summary>
    private bool _compacto;
    private string _nodoActual = "";
    /// <summary>Lo aprendido en la última corrida automática: los SALTOS que la vista dibuja.</summary>
    private List<(string From, string To, string Label)> _ultimaCorrida = new();

    /// <summary>Los sitios por los que se ha pasado, con o sin salto. Un sitio pisado ya es un nodo.</summary>
    private readonly List<string> _vistos = new();

    /// <summary>Para no repetir la misma línea de log en cada redibujo.</summary>
    private string _huellaDibujo = "";
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

        // LAS DOS VISTAS A LA VEZ, no una o la otra. Eran modos alternativos y eso obligaba a elegir
        // entre ver QUÉ hay disponible (la lista de aristas) y ver POR DÓNDE va (el grafo), que es
        // justo lo que no se puede separar cuando lo que quieres es seguir en directo a un asistente
        // que se está moviendo solo: la lista dice qué puertas tiene delante y el grafo dice de
        // dónde viene (2026-08-04). Lista a la izquierda, grafo a la derecha.
        _lista = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _edges,
        };
        _grafo = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _lienzo,
            Margin = new Thickness(6, 0, 0, 0),
        };
        // Si cambia el sitio disponible, se recalcula el encaje: da igual de dónde venga el cambio
        // —otra resolución, la barra de tareas, un monitor distinto— porque la pregunta es la misma.
        _grafo.SizeChanged += (_, __) => AjustarALaVista();

        // Los puntos ya no necesitan media pantalla: una tira estrecha a la izquierda basta para
        // decir cuántas salidas hay y cuántas se conocen, y todo lo demás es para el grafo.
        // LOS NIVELES, pegados al borde derecho. Un nivel es una APP: el grafo de dentro de una
        // aplicación es un terreno cerrado —sus pantallas, sus botones— y lo que lleva de una a otra
        // no es una arista más, es un salto de nivel (abrirla, o su icono en la barra de tareas).
        // Dibujarlo todo junto mezclaba los botones del explorador con los de Configuración y hacía
        // ilegible lo que sí importa: cómo moverse DENTRO de donde estás (2026-08-04).
        _niveles = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(6, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        // Los puntos van DEBAJO y ocupando todo: están colocados sobre la pantalla real, así que no
        // pueden vivir en una columna. El grafo y los niveles se quedan a la derecha, encima.
        // EL GRAFO OCUPA TODO Y VA CENTRADO. Vivía en una columna fija de 560 px pegada a la
        // derecha, que era una herencia de cuando a su izquierda había una lista de aristas: se
        // quedó ahí cuando esa lista se convirtió en puntos sobre la pantalla, así que el grafo
        // seguía apretado en media pantalla sin que nada ocupara la otra mitad (2026-08-04).
        // Solo la tira de niveles conserva su sitio, porque su sitio ES el borde.
        var derecha = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        derecha.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        derecha.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_grafo, 0);
        Grid.SetColumn(_niveles, 1);
        _lienzo.HorizontalAlignment = HorizontalAlignment.Center;
        _lienzo.VerticalAlignment = VerticalAlignment.Center;
        derecha.Children.Add(_grafo);
        derecha.Children.Add(_niveles);

        var dos = new Grid();
        dos.Children.Add(_lista);      // capa de puntos, al fondo
        dos.Children.Add(derecha);     // grafo y niveles, encima

        // LA BARRA es lo único sólido y lo único que recibe el ratón: el resto es una capa que se
        // mira, no se toca (ver EsZonaViva y el enganche de WM_NCHITTEST más abajo).
        // DOS ICONOS Y NADA MÁS. La barra llevaba título, un botón ancho con su texto, la superficie
        // actual y el recuento de aristas: cinco cosas escritas permanentemente encima de la app que
        // se está mirando, para dos gestos que se hacen de vez en cuando (2026-08-04). Lo que se
        // hace poco se guarda pequeño; lo que se lee mucho —dónde estás, cuántas salidas hay— pasa a
        // los tooltips, que aparecen cuando se preguntan y no antes.
        _crawlBtn.Content = "🤖";
        _crawlBtn.Width = 26; _crawlBtn.Height = 26;
        _crawlBtn.Margin = new Thickness(4, 0, 0, 0);
        _crawlBtn.FontSize = 12;
        // MinWidth 0: el estilo por defecto de Button reserva 75 px, así que dos iconos de 26
        // ocupaban 166 y la «barra pequeña» seguía siendo una barra.
        _crawlBtn.MinWidth = 0; _crawlBtn.MinHeight = 0;
        _crawlBtn.Padding = new Thickness(0);

        _collapseBtn.Width = 26; _collapseBtn.Height = 26;
        _collapseBtn.FontSize = 12;
        _collapseBtn.MinWidth = 0; _collapseBtn.MinHeight = 0;
        _collapseBtn.Padding = new Thickness(0);
        _collapseBtn.VerticalAlignment = VerticalAlignment.Center;

        // ELEGIR QUÉ APRENDER, sin tener que estar dentro. El botón de al lado mapea la app que
        // tengas delante, lo que obliga a saber de antemano cuál quieres y a llegar hasta ella.
        // Este abre el catálogo de lo instalado y se elige con doble clic (2026-08-05).
        _carruselBtn = new Button
        {
            Content = "🗂",
            Width = 26, Height = 26, FontSize = 12,
            MinWidth = 0, MinHeight = 0, Padding = new Thickness(0),
            Margin = new Thickness(4, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            ToolTip = "Elegir qué aplicación aprender",
        };
        _carruselBtn.Click += (_, __) => AbrirCarrusel();

        // EMPEZAR DE CERO, a mano. Toda prueba del mapa tiene que arrancar sin historia: un grafo
        // con recorrido esconde justo lo que se quiere medir. Hasta ahora había que borrar un
        // archivo, que no es algo que se pueda pedir a nadie (2026-08-06, pedido por el usuario).
        _limpiarBtn = new Button
        {
            Content = "🧹",
            Width = 26, Height = 26, FontSize = 12,
            MinWidth = 0, MinHeight = 0, Padding = new Thickness(0),
            Margin = new Thickness(4, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            ToolTip = "Borrar TODO el grafo y empezar de cero",
        };
        _limpiarBtn.Click += (_, __) => LimpiarGrafo();

        var iconos = new StackPanel { Orientation = Orientation.Horizontal };
        iconos.Children.Add(_collapseBtn);
        iconos.Children.Add(_crawlBtn);
        iconos.Children.Add(_carruselBtn);
        iconos.Children.Add(_limpiarBtn);

        _barra = new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x10, 0x10, 0x14)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(5),
            Cursor = Cursors.SizeAll,
            Child = iconos,
        };
        // Se arrastra por el propio recuadro: sin título, no hay otro sitio del que agarrarla.
        _barra.MouseLeftButtonDown += (_, __) => { try { _ventanaBarra.DragMove(); } catch { } };

        // LA BARRA VIVE EN SU PROPIA VENTANA, y la capa del grafo no recibe ratón EN ABSOLUTO.
        //
        // El intento anterior era una sola ventana que respondía al hit test según la zona: barra
        // sólida, resto transparente. No funcionó — el clic no atravesaba y, peor, al pulsar encima
        // la ventana se activaba y desaparecía unos segundos (2026-08-04, reportado por el usuario).
        // Repartir una ventana en «esto sí y esto no» depende de demasiadas piezas; separar las dos
        // cosas en dos ventanas no depende de ninguna: la capa lleva WS_EX_TRANSPARENT —el ratón la
        // atraviesa siempre, sin excepciones— y WS_EX_NOACTIVATE, así que tampoco puede robar el
        // foco. Lo que hay que poder tocar está en otra ventana, normal y corriente.
        _ventanaBarra = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.Manual,   // si no, WPF la centra y se ignora Left/Top
            SizeToContent = SizeToContent.WidthAndHeight,
            Title = "Ü Explorador del grafo",
            Content = _barra,
        };

        // El marco solo se enciende mientras la capa se deja tocar (ver VigilarModificadores).
        _marco = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xC1, 0x07)),
            BorderThickness = new Thickness(0),
            Child = dos,
        };
        Content = _marco;

        // Se ocupa toda el área de trabajo: lo que se está siguiendo es un asistente moviéndose por
        // una app, y eso no cabe en un panel de 380 px sin obligar a hacer scroll justo cuando pasa
        // lo interesante. Como el fondo es transparente y los clics la atraviesan, ocupar la
        // pantalla entera no le quita sitio a nada.
        var wa = SystemParameters.WorkArea;
        Left = wa.Left; Top = wa.Top; Width = wa.Width; Height = wa.Height;

        // Lo que manda ahora son los EVENTOS (ver EscucharLaPantalla). Este reloj se queda de red,
        // y por eso pasa de 1 s a 3: si algún cambio no emite evento, se acaba viendo igual, pero
        // sin pagar una lectura por segundo que casi siempre no encuentra nada nuevo.
        EscucharLaPantalla();
        _refresh = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _refresh.Tick += (_, __) => RefreshEdges();
        _refresh.Start();
        // Esquina superior derecha: es donde no estorba y donde se busca lo accesorio. Se recoloca
        // en cada cambio de tamaño porque con SizeToContent el ancho real no se sabe hasta que WPF
        // ha medido, y colocarla antes la dejaba a media pantalla.
        bool colocada = false;
        _ventanaBarra.SizeChanged += (_, __) =>
        {
            if (colocada && _ventanaBarra.Left > 0) return;   // si el usuario la movió, se respeta
            _ventanaBarra.Left = wa.Right - _ventanaBarra.ActualWidth - 12;
            _ventanaBarra.Top = wa.Top + 10;
            colocada = true;
        };

        // Cuando el asistente dice que ve algo, el recuadro lo señala. Ver Senalador.
        Senalador.SenalaVarias += cajas => Dispatcher.BeginInvoke(() => { try { _overlay.ShowRects(cajas); } catch { } });
        Senalador.Suelta += () => Dispatcher.BeginInvoke(() => { try { _overlay.HideRect(); } catch { } });

        Closed += (_, __) => { _refresh.Stop(); _overlay.Close(); _ventanaBarra.Close(); };
        IsVisibleChanged += (_, __) =>
        {
            if (IsVisible) _ventanaBarra.Show(); else _ventanaBarra.Hide();
        };
        _overlay.Show();
    }

    // ── Una capa que se mira, no se toca ─────────────────────────────────────

    /// <summary>
    /// Los clics ATRAVIESAN la ventana salvo en la barra.
    ///
    /// Ocupar la pantalla entera solo vale si no le quita la pantalla a nadie: una capa a pantalla
    /// completa que además se traga el ratón no es una vista, es una persiana. Y hacerla del todo
    /// intransitable tampoco sirve, porque entonces no habría por dónde moverla ni cómo lanzar el
    /// mapeo. Windows tiene exactamente esta pregunta —WM_NCHITTEST, «¿esto es tuyo?»— y respondiendo
    /// HTTRANSPARENT fuera de la barra el clic sigue su camino hasta la app de abajo, que es donde
    /// el usuario estaba mirando (2026-08-04).
    ///
    /// Se responde por REGIÓN y no marcando la ventana entera con WS_EX_TRANSPARENT porque esa
    /// marca es de todo o nada: dejaría la barra tan muerta como el resto.
    /// </summary>
    /// <summary>
    /// Con Ctrl+Shift la capa se deja tocar; sin ellos, el ratón la atraviesa.
    ///
    /// Las dos cosas se querían a la vez y son contrarias: una capa a pantalla completa que se traga
    /// el ratón es una persiana, pero una que nunca lo recibe no deja pulsar un nivel ni leer el
    /// nombre completo de nada. La salida es que lo decida el usuario con las manos, sin apuntar a
    /// ningún sitio: mientras mantiene Ctrl+Shift, la capa existe para el ratón (2026-08-04).
    ///
    /// Se sondea con GetAsyncKeyState y no con eventos de teclado porque esta ventana nunca tiene el
    /// foco —lo tiene la app que se está mirando— y sin foco no llegan pulsaciones.
    /// </summary>
    private void VigilarModificadores()
    {
        var reloj = new System.Windows.Threading.DispatcherTimer
        { Interval = TimeSpan.FromMilliseconds(90) };
        reloj.Tick += (_, __) =>
        {
            bool quiere = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0
                       && (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
            if (quiere == _interactivo) return;
            _interactivo = quiere;

            var mano = new WindowInteropHelper(this).Handle;
            int estilo = GetWindowLong(mano, GWL_EXSTYLE);
            SetWindowLong(mano, GWL_EXSTYLE, quiere
                ? estilo & ~WS_EX_TRANSPARENT      // se deja tocar
                : estilo | WS_EX_TRANSPARENT);     // vuelve a ser solo mirable

            // Se AVISA de que ahora se puede tocar. Un cambio de comportamiento invisible es un
            // cambio que el usuario descubre a base de clics que no hacen lo que espera.
            _marco.BorderThickness = new Thickness(quiere ? 2 : 0);
        };
        reloj.Start();
        Closed += (_, __) => reloj.Stop();
    }

    private bool _interactivo;
    private const int VK_CONTROL = 0x11, VK_SHIFT = 0x10;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        VigilarModificadores();
        var h = new WindowInteropHelper(this).Handle;
        // TRANSPARENT: el ratón la atraviesa. NOACTIVATE: nunca se pone delante ni roba el foco —sin
        // esto, pulsar encima la activaba y la app de debajo perdía el foco, que se veía como que la
        // capa «desaparecía un momento». TOOLWINDOW: fuera de Alt+Tab, no es un sitio al que ir.
        SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE)
            | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_LAYERED = 0x80000;
    private const int WS_EX_NOACTIVATE = 0x8000000;
    private const int WS_EX_TOOLWINDOW = 0x80;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    // ── Refresco por EVENTOS, no por reloj ───────────────────────────────────

    private delegate void WinEventProc(IntPtr hook, uint evento, IntPtr hwnd,
        int idObjeto, int idHijo, uint hilo, uint ms);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr dll,
        WinEventProc callback, uint proceso, uint hilo, uint flags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    private const uint EVENT_OBJECT_SHOW = 0x8002;
    private const uint EVENT_OBJECT_HIDE = 0x8003;
    private const uint EVENT_OBJECT_FOCUS = 0x8005;
    private const uint EVENT_OBJECT_SELECTION = 0x8006;
    private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    private readonly List<IntPtr> _enganches = new();
    private WinEventProc? _alOcurrir;              // referencia viva: si la recoge el GC, Windows llama a un hueco
    private System.Windows.Threading.DispatcherTimer? _rebote;
    private DateTime _ultimaLectura = DateTime.MinValue;

    /// <summary>
    /// Windows AVISA cuando la pantalla cambia; no hace falta preguntárselo cada segundo.
    ///
    /// El refresco iba con un reloj de 1 s, y eso se veía: los puntos —que desde que viven encima de
    /// cada elemento tienen que seguirle el paso— llegaban tarde a cada cambio de la interfaz
    /// (2026-08-04, reportado por el usuario). Sondear más rápido no es la respuesta: leer el árbol
    /// UIA de una ventana no es gratis y hacerlo diez veces por segundo para nada es peor que el
    /// retraso.
    ///
    /// SetWinEventHook fuera de contexto es la vía barata: el sistema nos llama cuando algo pasa de
    /// verdad —cambia la ventana activa, aparece o desaparece algo, se mueve, cambia la selección—
    /// y en reposo no cuesta absolutamente nada. Se eligen esos eventos y no «todos» porque la lista
    /// completa incluye cosas como el parpadeo del cursor.
    ///
    /// Con dos frenos, porque estos eventos vienen en ráfagas: se espera a que la ráfaga termine
    /// (rebote) y no se lee dos veces seguidas antes de un mínimo. El reloj se queda como red, mucho
    /// más lento: si algún cambio no emite evento, se acaba viendo igual.
    /// </summary>
    private void EscucharLaPantalla()
    {
        _rebote = new System.Windows.Threading.DispatcherTimer
        { Interval = TimeSpan.FromMilliseconds(70) };
        _rebote.Tick += (_, __) => { _rebote!.Stop(); RefreshEdges(); };

        _alOcurrir = (_, evento, hwnd, idObjeto, _, _, _) =>
        {
            // Lo nuestro no cuenta: la propia capa dibujándose generaría eventos y con ellos otra
            // lectura, y esa lectura otro dibujo. Un observador que reacciona a sí mismo no para.
            if (hwnd != IntPtr.Zero && EsNuestraVentana(hwnd)) return;

            // SOLO LA VENTANA DE DELANTE. Estos eventos llegan de TODA la sesión, y las ventanas de
            // fondo que animan —un chat, un navegador reproduciendo algo— emiten sin parar. Se
            // midió: escuchando a todas, la capa quemaba un 22 % de un núcleo sin que nadie tocara
            // nada (2026-08-04). Y no aporta: lo que se dibuja son los elementos de la ventana en
            // primer plano, así que lo que pase detrás no cambia ni un punto.
            //
            // Se compara contra la ventana RAÍZ del evento: un botón emite desde su ventana hija, y
            // exigir que el hwnd sea exactamente el de primer plano descartaría casi todo.
            if (evento != EVENT_SYSTEM_FOREGROUND)
            {
                IntPtr delante = GetForegroundWindow();
                IntPtr raiz = hwnd == IntPtr.Zero ? IntPtr.Zero : GetAncestor(hwnd, GA_ROOT);
                if (delante == IntPtr.Zero || (raiz != delante && hwnd != delante)) return;
            }

            // UN EVENTO QUE LLEGA PRONTO SE APLAZA, NO SE TIRA. Antes, si venía antes del mínimo,
            // se descartaba: con una app que emite sin parar, el aviso de «cambió la ventana
            // activa» se perdía entre el ruido y la capa se quedaba con los puntos de la app
            // anterior hasta que el reloj de red la despertaba tres segundos después. Se veía como
            // que no se enteraba hasta que clicabas algo (2026-08-04, reportado por el usuario).
            // Filtrar ráfagas es retrasar, nunca olvidar.
            //
            // Y el cambio de ventana no espera: es EL cambio, el que decide todo lo demás. Igual si
            // hace rato que no se lee, para que una ráfaga continua no deje el redibujo en el limbo
            // reprogramándolo eternamente.
            bool urgente = evento == EVENT_SYSTEM_FOREGROUND
                        || (DateTime.UtcNow - _ultimaLectura).TotalMilliseconds > 400;
            int espera = evento == EVENT_OBJECT_LOCATIONCHANGE ? 200 : 60;

            Dispatcher.BeginInvoke(() =>
            {
                _rebote!.Stop();
                if (urgente) RefreshEdges();
                else { _rebote.Interval = TimeSpan.FromMilliseconds(espera); _rebote.Start(); }
            });
        };

        foreach (var (a, b) in new[]
        {
            (EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_MOVESIZEEND),
            (EVENT_OBJECT_SHOW, EVENT_OBJECT_HIDE),
            (EVENT_OBJECT_FOCUS, EVENT_OBJECT_SELECTION),
            (EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE),
        })
        {
            IntPtr h = SetWinEventHook(a, b, IntPtr.Zero, _alOcurrir, 0, 0, WINEVENT_OUTOFCONTEXT);
            if (h != IntPtr.Zero) _enganches.Add(h);
        }
        LogBus.Log("explorador", $"escuchando la pantalla por eventos ({_enganches.Count} enganches)");

        Closed += (_, __) =>
        {
            _rebote?.Stop();
            foreach (var h in _enganches) { try { UnhookWinEvent(h); } catch { } }
            _enganches.Clear();
        };
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    private const uint GA_ROOT = 2;

    private static bool EsNuestraVentana(IntPtr h) => Propio.EsVentana(h);

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
        _ultimaLectura = DateTime.UtcNow;
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
        if (Propio.EsProceso(proc)) return;

        var loc = _where();
        string aqui = loc?.Id ?? "";

        // EL GRAFO SIGUE AL ASISTENTE, no solo al recorrido automático. Antes solo se dibujaba
        // durante un mapeo, así que mientras la voz movía la app de verdad el panel de la derecha se
        // quedaba con el dibujo de la última corrida — justo cuando lo que se quiere es ver por
        // dónde va AHORA (2026-08-04). Cada cambio de pantalla se añade como un tramo más: el
        // resultado es la traza en vivo de por dónde ha pasado.
        if (!_busy && aqui.Length > 0
            && !aqui.Equals(_nodoActual, StringComparison.OrdinalIgnoreCase))
        {
            // CAMBIAR DE SELECCIÓN NO ES MOVERSE. Si las dos identidades son la misma pantalla y
            // solo cambia el «#sección», no ha habido navegación: sigues donde estabas, señalando
            // otra cosa. Anotarlo como tramo hacía que el grafo afirmara que para llegar a B hay que
            // pasar por A, cuando los dos son alcanzables directamente desde donde estás — pasos de
            // navegación inventados que luego alguien tendría que dar (2026-08-04).
            bool mismaPantalla = _nodoActual.Length > 0
                && _nodoActual.Split('#')[0].Equals(aqui.Split('#')[0], StringComparison.OrdinalIgnoreCase);

            if (_nodoActual.Length > 0 && !mismaPantalla)
            {
                string etiqueta = _map.ExitsFrom(_nodoActual)
                    .FirstOrDefault(h => h.To.Equals(aqui, StringComparison.OrdinalIgnoreCase))
                    ?.Info.Label ?? "";
                _ultimaCorrida.Add((_nodoActual, aqui, etiqueta));
                // Una traza infinita no se lee: se conservan los últimos tramos, que es el tramo de
                // historia que cabe en pantalla y el único que se está mirando.
                if (_ultimaCorrida.Count > 40) _ultimaCorrida.RemoveRange(0, _ultimaCorrida.Count - 40);
            }
            // ESTAR EN UN SITIO YA ES SABER QUE EXISTE. El grafo se guardaba solo como lista de
            // SALTOS, así que un nodo no aparecía hasta haber una transición: te plantabas delante
            // de una app y el panel seguía diciendo «todavía no hay recorrido», y con el filtro por
            // nivel bastaba con que los saltos registrados fueran de otra app para que el tuyo
            // saliera vacío estando dentro. De ahí la sensación de que cuesta que empiecen a
            // aparecer los nodos (2026-08-04, reportado por el usuario). Un sitio pisado se dibuja,
            // tenga o no aristas todavía.
            if (!_vistos.Contains(aqui, StringComparer.OrdinalIgnoreCase))
            {
                _vistos.Add(aqui);
                if (_vistos.Count > 60) _vistos.RemoveRange(0, _vistos.Count - 60);
            }

            _nodoActual = aqui;
            // Solo se anota si lo LEÍDO y el DÓNDE hablan de la misma app: ver AnotarPuertas.
            if (SurfaceMap.AppDe(aqui).StartsWith(proc + ".", StringComparison.OrdinalIgnoreCase))
                AnotarPuertas(aqui, els);
            DibujarGrafo();
        }

        var conocidas = aqui.Length > 0
            // Agrupando por etiqueta, no ToDictionary: desde que se registran TODAS las puertas
            // visibles, una pantalla puede tener dos salidas con el mismo nombre —el mismo archivo
            // en el panel y en la lista, dos elementos homónimos— y ToDictionary revienta con
            // «An item with the same key has already been added» al terminar el mapeo (2026-08-01).
            // Que dos puertas se llamen igual es normal; que la app se caiga por ello, no.
            ? _map.ExitsFrom(aqui).Where(h => h.Info.Selector.Length > 0)
                  .GroupBy(h => h.Info.Label, StringComparer.OrdinalIgnoreCase)
                  .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, SurfaceMap.Hop>(StringComparer.OrdinalIgnoreCase);

        // LA POSICIÓN FORMA PARTE DE LO QUE HAY QUE REDIBUJAR. La firma llevaba solo nombres y
        // tipos, que era lo correcto cuando los puntos vivían en una tira: daba igual dónde
        // estuviera cada botón. Desde que cada punto se coloca ENCIMA de su elemento, mover la
        // ventana cambia todo lo que importa y no cambiaba la firma — se arrastraba una ventana y
        // los puntos se quedaban clavados donde estaban (2026-08-04, reportado por el usuario).
        // Se redondea a píxeles enteros para no redibujar por medio punto de diferencia.
        // Encender o apagar los números CAMBIA lo que hay que dibujar aunque los elementos sean los
        // mismos: sin esto en la firma, se pedían los números y el repintado se saltaba por «nada
        // ha cambiado», así que la foto salía sin ellos.
        string firma = (Numerar ? "n|" : "") + aqui + "|" + string.Join("|", els.Select(e =>
            $"{e.Label}:{e.ControlType}:{(int)e.Bounds.X},{(int)e.Bounds.Y}"));
        if (firma == _signature) return; // nada cambió: no matar el hover redibujando
        _signature = firma;

        _nodeTitle.Text = aqui.Length > 0 ? "◉ " + aqui : "◉ (sin superficie)";
        _edges.Children.Clear();

        // PUNTOS, NO RENGLONES. Cada salida era una fila de ancho completo con su nombre y su
        // destino escritos; con una pantalla normal eso son cuarenta renglones que se comen media
        // pantalla y tapan justo la app que se está mirando (2026-08-04, visto en pantalla). Lo que
        // esta columna tiene que responder de un vistazo es CUÁNTAS salidas hay y cuántas se
        // conocen, y para eso el nombre sobra: un punto por salida lo dice igual y ocupa cien veces
        // menos. El texto no se pierde —vive en el tooltip y en AutomationProperties, así que sigue
        // estando para quien pase el ratón y para cualquier registro—, solo deja de gritar.
        // Las cajas de UIA vienen en píxeles FÍSICOS y WPF dibuja en unidades independientes del
        // monitor: sin convertir, con escalado al 125 % cada punto caería un cuarto más allá de su
        // elemento — cerca, que es peor que lejos, porque parece que funciona.
        var fuente = PresentationSource.FromVisual(this);
        Matrix aPantalla = fuente?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;

        _numeradas.Clear();   // los números valen para ESTA pantalla; en otra son otros elementos
        if (Numerar) LogBus.Log("maestro", $"repintando con números: {els.Count} elemento(s), vistaGrafo={_graphView}");

        foreach (var el in els)
        {
            bool sabida = conocidas.TryGetValue(el.Label, out var salida);

            // AZUL = NIVEL PRINCIPAL, y es el MISMO azul del grafo. Los dos dibujos hablan de lo
            // mismo —qué pertenece a la navegación principal de la app— y tenerlo solo en uno
            // obligaba a mirar el grafo para saber algo que se decide señalando los puntos. Si el
            // usuario acaba de fijar «Descargas» al nivel 1, tiene que verlo donde está Descargas
            // (2026-08-05, pedido por el usuario).
            bool primerNivel = sabida && salida!.Info.NivelNav == 1;
            bool aMano = sabida && salida!.Info.NivelFijado;

            string descripcion = el.Label
                + (sabida ? $"  ⇒  {Corto(salida!.To)}" : $"  ({el.ControlType}, sin explorar)")
                + (primerNivel ? "  ·  nivel 1" : "")
                + (aMano ? " (fijado a mano)" : "");

            // EL NÚMERO ES EL PUENTE ENTRE VER Y ACCIONAR. Cuando un modelo de visión mira la
            // pantalla y dice «el menú lateral», nadie sabe a qué elementos se refiere: describir
            // no identifica. Con un número encima de cada puerta, puede decir «el 7 y el 12» y eso
            // se traduce al elemento exacto del árbol UIA, que es lo único que se puede pulsar.
            // La imagen le da el sentido; el número, la identidad (2026-08-05).
            int numero = _numeradas.Count + 1;
            if (Numerar) _numeradas[numero] = el;

            var chip = new Border
            {
                Width = Numerar ? 18 : 12, Height = Numerar ? 18 : 12,
                CornerRadius = new CornerRadius(Numerar ? 9 : 6),
                Child = Numerar ? new TextBlock
                {
                    Text = numero.ToString(),
                    Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                } : null,
                // Azul = nivel principal; verde = arista ya recorrida; gris = potencial, sin explorar.
                Background = new SolidColorBrush(primerNivel
                    ? Color.FromArgb(aMano ? (byte)0xAA : (byte)0x88, 0x21, 0x96, 0xF3)
                    : sabida ? Color.FromArgb(0x88, 0x2E, 0x7D, 0x32) : Color.FromArgb(0x33, 0xC0, 0xC0, 0xC0)),
                BorderBrush = new SolidColorBrush(primerNivel
                    ? Color.FromArgb(0xCC, 0x64, 0xB5, 0xF6)
                    : sabida ? Color.FromArgb(0xAA, 0x66, 0xBB, 0x6A) : Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = descripcion,
            };
            System.Windows.Automation.AutomationProperties.SetName(chip, descripcion);

            var elemento = el; // captura por arista, no la variable del bucle
            chip.MouseEnter += (_, __) => _overlay.ShowRect(elemento.Bounds);
            chip.MouseLeave += (_, __) => _overlay.HideRect();
            chip.MouseLeftButtonUp += (_, __) => _ = TraverseAsync(elemento, aqui);

            // En la esquina superior izquierda del elemento, no en su centro: el centro es donde
            // está el texto o el icono del botón —lo que el usuario necesita seguir viendo— y un
            // punto encima lo taparía. La esquina es de nadie.
            var caja = el.Bounds;
            if (caja.Width <= 0 || caja.Height <= 0) continue;
            var esquina = aPantalla.Transform(new Point(caja.X, caja.Y));
            Canvas.SetLeft(chip, esquina.X + 2);
            Canvas.SetTop(chip, esquina.Y + 2);
            _edges.Children.Add(chip);
        }

        _status.Text = $"{els.Count} arista(s) a la vista · {conocidas.Count} ya recorrida(s) desde aquí";
        RefrescarAyudas();
    }

    /// <summary>
    /// Lo que antes estaba escrito en la barra —dónde estás y cuántas salidas hay— pasa a los
    /// tooltips de los dos iconos. No se ha perdido: se ha callado hasta que alguien pregunte.
    /// </summary>
    private void RefrescarAyudas()
    {
        _collapseBtn.ToolTip = (_collapsed ? "Mostrar la capa del grafo" : "Ocultar la capa del grafo")
            + "\n" + _nodeTitle.Text + "\n" + _status.Text
            + "\nCtrl+Shift: la capa se deja tocar";
        _crawlBtn.ToolTip = (_crawlCts != null ? "Detener el mapeo" : "Mapear esta app automáticamente")
            + "\nRecorre la app abriendo lo que encuentra. Solo navegación: nunca pulsa botones ni menús.";
    }

    /// <summary>
    /// Lo que DIFERENCIA a un nodo de los demás que hay en pantalla.
    ///
    /// Todos los nodos de un nivel comparten el principio —son la misma app, muchas veces la misma
    /// pantalla con distinta sección— así que enseñar la ruta entera y recortar por el final dejaba
    /// diez cajas idénticas que ponían «explorer.exe/program-manager…»: el texto ocupaba sitio para
    /// no decir nada, y lo único que las distinguía era justo lo que se recortaba (2026-08-04).
    /// Se quita el prefijo que todos comparten y se enseña el resto.
    /// </summary>
    /// <summary>
    /// Lo que se ve desde aquí queda anotado como SALIDA de aquí.
    ///
    /// El panel enseñaba los elementos de la pantalla y los olvidaba: eran una lista en vivo, no
    /// terreno. Por eso, al dejar de convertir cada icono del escritorio en un nodo falso, los
    /// accesos directos desaparecieron del grafo por completo — y desaparecer no era lo correcto,
    /// porque SÍ existen: son puertas del escritorio, alcanzables directamente desde él. Anotarlas
    /// como salidas dice justo eso y nada más: que están ahí y que se llega sin pasos intermedios;
    /// a dónde dan se sabrá el día que se crucen (2026-08-04, reportado por el usuario).
    ///
    /// Solo al CAMBIAR de pantalla, no en cada sondeo: describir cada elemento cuesta un viaje a
    /// UIA, y repetirlo cada segundo sobre lo mismo no aporta nada.
    ///
    /// Y SOLO SI LO LEÍDO Y EL DÓNDE COINCIDEN. Los elementos los da el lector sobre la ventana en
    /// primer plano y la identidad la da el localizador, y entre las dos lecturas la ventana puede
    /// haber cambiado: al probar esto, el nodo del Bloc de notas acabó con las acciones de la
    /// ventana de Claude —«Crear PR», «Editado GraphExplorerWindow.cs»— escritas dentro
    /// (2026-08-04). Mientras esto solo se pintaba, una lista desfasada un segundo no hacía daño;
    /// desde que se ESCRIBE en el mapa, es exactamente el veneno que costó una mañana limpiar.
    /// </summary>
    private void AnotarPuertas(string nodo, List<UiaReader.UiElement> els)
    {
        if (nodo.Length == 0 || els.Count == 0) return;
        try
        {
            var puertas = new List<(string, string, string, string[], string)>();
            foreach (var el in els.Take(80))   // un techo: una pantalla con cientos no se mapea mirándola
            {
                try
                {
                    var (l, t, sels) = U.Graph.Surfaces.UiaSurface.DescribeElement(el.Native);
                    var utiles = sels.Where(s => !s.Contains("path=", StringComparison.Ordinal)
                        && !System.Text.RegularExpressions.Regex.IsMatch(s, @"(name|aid)=(;|$)")).ToArray();
                    if (utiles.Length == 0) continue;
                    puertas.Add((l.Length > 0 ? l : el.Label, t.Length > 0 ? t : el.ControlType,
                                 utiles[0], utiles.Skip(1).ToArray(), U.Graph.Surfaces.UiaSurface.GrupoDe(el.Native)));
                }
                catch { }
            }
            if (puertas.Count > 0)
            {
                int con = puertas.Count(p => p.Item5.Length > 0);
                if (con == 0 && els.Count > 0)
                    LogBus.Log("explorador", $"SIN GRUPO {puertas.Count}/{puertas.Count} · «{els[0].Label}» → "
                        + U.Graph.Surfaces.UiaSurface.Ancestros(els[0].Native));
                else LogBus.Log("explorador", $"grupos: {con}/{puertas.Count} salidas con grupo");
                _map.ObserveExits(nodo, puertas);
            }
        }
        catch { }
    }

    private static string Distintivo(string id, string prefijoComun)
    {
        string corto = Corto(id);
        if (prefijoComun.Length > 0 && corto.StartsWith(prefijoComun, StringComparison.Ordinal))
        {
            string resto = corto[prefijoComun.Length..].TrimStart('/', '#', '-');
            if (resto.Length > 0) return resto;
        }
        // Sin resto —es el propio nodo del prefijo— se enseña su último tramo, que es su nombre.
        int corte = corto.LastIndexOfAny(new[] { '/', '#' });
        return corte >= 0 && corte < corto.Length - 1 ? corto[(corte + 1)..] : corto;
    }

    /// <summary>El principio que TODOS comparten, cortado en el último separador para no partir palabras.</summary>
    private static string PrefijoComun(IEnumerable<string> ids)
    {
        var lista = ids.Select(Corto).ToList();
        if (lista.Count < 2) return "";

        string primero = lista[0];
        int n = primero.Length;
        foreach (var s in lista.Skip(1))
        {
            int i = 0;
            while (i < n && i < s.Length && primero[i] == s[i]) i++;
            n = i;
            if (n == 0) return "";
        }
        string comun = primero[..n];
        int corte = comun.LastIndexOfAny(new[] { '/', '#' });
        return corte >= 0 ? comun[..corte] : "";
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
    /// <summary>
    /// Plegar esconde las DOS vistas y deja solo la barra. Ya no encoge la ventana: ocupa la
    /// pantalla entera y no le estorba a nadie, así que redimensionarla solo servía para que al
    /// desplegar volviera a un tamaño que ya no es el suyo.
    /// </summary>
    private void SetCollapsed(bool colapsar)
    {
        _collapsed = colapsar;
        var v = colapsar ? Visibility.Collapsed : Visibility.Visible;
        _nodeTitle.Visibility = v;
        _lista.Visibility = v;
        _grafo.Visibility = v;
        _status.Visibility = v;
        _collapseBtn.Content = colapsar ? "▸" : "▾";
    }

    /// <summary>
    /// El botón ya no ELIGE entre lista y grafo —las dos están puestas, una al lado de la otra—:
    /// ahora solo redibuja el grafo a mano, por si se quiere refrescar sin esperar al recorrido.
    /// </summary>
    private void SetGraphView(bool grafo)
    {
        _graphView = grafo;
        _graphBtn.Background = new SolidColorBrush(grafo
            ? Color.FromArgb(0x55, 0x66, 0xBB, 0x6A) : Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
        if (_collapsed) SetCollapsed(false);
        DibujarGrafo();
    }

    /// <summary>
    /// Encoge el grafo hasta que quepa entero en su columna.
    ///
    /// Crece a lo ancho con cada pantalla nueva de la misma profundidad, así que a la tercera o
    /// cuarta se salía por la derecha y lo que estaba pasando quedaba fuera de la pantalla, sin
    /// forma cómoda de seguirlo (2026-08-04). Se escala, no se hace scroll: el sentido de esta vista
    /// es ver la FORMA del recorrido de un vistazo, y un grafo que hay que arrastrar para leer ya no
    /// la enseña. Nunca se agranda por encima del 100 %: un grafo de dos nodos ocupando media
    /// pantalla se lee peor, no mejor.
    /// </summary>
    private void AjustarALaVista()
    {
        double dispW = _grafo.ActualWidth - 16, dispH = _grafo.ActualHeight - 16;
        double w = _lienzo.Width, h = _lienzo.Height;

        // NaN ANTES QUE CERO. Un Canvas sin tamaño fijado mide NaN, no 0, y `NaN <= 0` es FALSO: la
        // guarda lo dejaba pasar, la escala salía NaN y WPF tumbaba la aplicación con «no debería
        // devolver valores NaN como su DesiredSize» — en cascada, una ventana de error por intento
        // de dibujo (2026-08-04). Con dobles, comprobar «no es válido» nunca es comparar con cero.
        if (double.IsNaN(w) || double.IsNaN(h) || double.IsNaN(dispW) || double.IsNaN(dispH)) return;
        if (dispW <= 0 || dispH <= 0 || w <= 0 || h <= 0) return;

        // Se AGRANDA cuando sobra sitio, no solo se encoge cuando falta. Antes el tope era 1,0 —
        // pensado para que un grafo de dos nodos no ocupara media pantalla— pero con el lienzo
        // suelto a todo el ancho eso dejaba el dibujo pequeño en el centro de un espacio vacío.
        // El techo de 1,8 es el punto donde las cajas siguen pareciendo cajas y no carteles.
        double escala = Math.Min(1.8, Math.Min(dispW / w, dispH / h));
        if (double.IsNaN(escala) || double.IsInfinity(escala)) return;
        if (escala < 0.25) escala = 0.25;   // por debajo de esto ya no se lee: mejor scroll
        _lienzo.LayoutTransform = escala >= 0.999
            ? System.Windows.Media.Transform.Identity
            : new ScaleTransform(escala, escala);
    }

    /// <summary>
    /// Dibuja el grafo de la ÚLTIMA corrida automática: nodos en filas por distancia a la raíz y
    /// aristas etiquetadas con lo que se pulsó.
    ///
    /// Por filas y no con un layout de fuerzas: un recorrido en profundidad produce un árbol con
    /// vuelta atrás, y en un árbol la distancia a la raíz ES la información —cuánto hay que bajar
    /// para llegar—. Un grafo de resortes lo taparía moviendo los nodos a donde quepan.
    /// </summary>
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private const int SW_RESTORE = 9;

    /// <summary>Traer al frente lo hace <see cref="AppAligner.TraerAlFrente"/>, para toda la app.</summary>
    private static bool TraerAlFrente(IntPtr h) => AppAligner.TraerAlFrente(h);

    /// <summary>
    /// Traer al frente el nivel pedido.
    ///
    /// No vale con enfocar «el proceso»: el del explorador es la SHELL, y su ventana principal es
    /// el escritorio. Así que pulsar el nivel del explorador llevaba al escritorio, y los dos
    /// niveles acababan en el mismo sitio — no alternaban (2026-08-04, reportado por el usuario).
    /// Un nivel se alcanza buscando una ventana SUYA, no un proceso con su nombre.
    /// </summary>
    private void IrAlNivel(string nivel)
    {
        bool ok = false;
        try
        {
            if (Escritorio.EsProceso(nivel)) ok = Escritorio.Mostrar();
            else if (nivel.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase))
            {
                // Una ventana de archivos de verdad: CabinetWClass. Si no hay ninguna abierta, se
                // abre —que es lo que quiere quien pulsa «ir al explorador» sin tenerlo abierto.
                var ventana = AutomationElement.RootElement.FindFirst(TreeScope.Children,
                    new PropertyCondition(AutomationElement.ClassNameProperty, "CabinetWClass"));
                if (ventana != null)
                    ok = TraerAlFrente(new IntPtr(ventana.Current.NativeWindowHandle));
                else
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe")
                    { UseShellExecute = true });
                    ok = true;
                }
            }
            else
            {
                // Se busca su ventana y se trae con el enganche; FocusOrLaunch solo como respaldo
                // para abrirla si no hay ninguna.
                string proc = nivel.Replace(".exe", "", StringComparison.OrdinalIgnoreCase).Trim();
                var abierto = System.Diagnostics.Process.GetProcessesByName(proc)
                    .FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);
                ok = abierto != null
                    ? TraerAlFrente(abierto.MainWindowHandle)
                    : AppAligner.FocusOrLaunch(proc);
            }
        }
        catch (Exception e) { LogBus.Log("explorador", $"al ir a «{nivel}»: {e.Message}"); }
        LogBus.Log("explorador", $"nivel pulsado: «{nivel}» → {(ok ? "al frente" : "NO se pudo")}");
        ComprobarUnRato();
    }

    private System.Windows.Threading.DispatcherTimer? _insistir;
    private int _quedanComprobaciones;

    /// <summary>
    /// Tras actuar sobre el mundo, se vuelve a mirar unas cuantas veces durante un segundo y medio.
    ///
    /// La capa se refresca con los avisos del sistema, y eso basta para todo… menos para lo que
    /// hacemos NOSOTROS. Mostrar el escritorio minimiza las ventanas una a una: cuando llega el
    /// aviso, el escritorio todavía no está delante, así que se lee demasiado pronto — y como el
    /// escritorio quieto no genera más avisos, nadie vuelve a mirar y los puntos se quedan con la
    /// app anterior hasta que el usuario clica algo. Se notaba solo al ir al escritorio desde la
    /// tira de niveles, y en ningún otro sitio (2026-08-04, reportado por el usuario).
    ///
    /// El principio es el de siempre en esta casa: quien provoca un cambio comprueba su
    /// consecuencia, en vez de fiarse de que el mundo avise a tiempo. Es una ráfaga corta y
    /// acotada; en reposo no cuesta nada porque no está corriendo.
    /// </summary>
    private void ComprobarUnRato()
    {
        _quedanComprobaciones = 10;   // ~1,5 s, de sobra para una animación de minimizar
        if (_insistir == null)
        {
            _insistir = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromMilliseconds(150) };
            _insistir.Tick += (_, __) =>
            {
                if (--_quedanComprobaciones <= 0) _insistir!.Stop();
                RefreshEdges();
            };
            Closed += (_, __) => _insistir?.Stop();
        }
        _insistir.Start();
    }

    /// <summary>
    /// A qué NIVEL pertenece una pantalla. Casi siempre es su app, pero no siempre.
    ///
    /// El escritorio y el explorador de archivos son el mismo proceso —los dos son explorer.exe—
    /// así que agrupar por proceso los metía en el mismo nivel, y el sistema los confundía
    /// (2026-08-04, reportado por el usuario). Como terreno no se parecen en nada: el escritorio es
    /// una rejilla de accesos directos que abren OTRAS apps; el explorador es un árbol de carpetas.
    /// Un nivel es un terreno con sus propias reglas de moverse, no un identificador de proceso.
    /// </summary>
    private static string NivelDe(string id)
    {
        if (Escritorio.EsId(id)) return "escritorio";
        return SurfaceMap.AppDe(id);
    }

    /// <summary>
    /// La tira de niveles del borde derecho: una aplicación por nivel, la actual encendida.
    ///
    /// Existe porque el filtrado por app resuelve la legibilidad pero crea una pregunta nueva: si
    /// solo veo el terreno de donde estoy, ¿qué otros terrenos hay y cómo se salta? La tira los
    /// enumera en el orden en que se pisaron, así que se lee como lo que es —el camino entre apps—
    /// y deja claro que pasar de un nivel a otro no es pulsar una arista más: es abrir otra
    /// aplicación, o su icono en la barra de tareas (2026-08-04).
    /// </summary>
    private void DibujarNiveles(string appActual)
    {
        _niveles.Children.Clear();

        // Orden de primera aparición: es el camino real que se ha recorrido entre aplicaciones, y
        // ordenar por nombre o por tamaño lo borraría.
        var apps = new List<string>();
        foreach (var (f, t, _) in _ultimaCorrida)
            foreach (var a in new[] { NivelDe(f), NivelDe(t) })
                if (a.Length > 0 && !apps.Contains(a, StringComparer.OrdinalIgnoreCase)) apps.Add(a);
        if (appActual.Length > 0 && !apps.Contains(appActual, StringComparer.OrdinalIgnoreCase))
            apps.Add(appActual);
        if (apps.Count == 0) return;

        for (int i = 0; i < apps.Count; i++)
        {
            string app = apps[i];
            bool aqui = app.Equals(appActual, StringComparison.OrdinalIgnoreCase);
            int pantallas = _map.Nodes.Keys.Count(n =>
                NivelDe(n).Equals(app, StringComparison.OrdinalIgnoreCase));

            // AL PASAR POR ENCIMA SE ABRE Y ENSEÑA EL NOMBRE. El tooltip no valía: esta ventana
            // nunca se activa —es su gracia—, y sin activarse WPF no llega a mostrarlo, así que el
            // nombre completo quedaba escrito en un sitio al que no se podía llegar (2026-08-04).
            // Expandirse es además más honesto con lo que la tira es: no un menú que se despliega,
            // sino una fila de niveles que se ensancha cuando la miras.
            var nombre = new TextBlock
            {
                Text = app,
                Foreground = new SolidColorBrush(Color.FromArgb(0xEE, 0xFF, 0xFF, 0xFF)),
                FontSize = 10, FontFamily = new FontFamily("Consolas"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 4, 0),
                Visibility = Visibility.Collapsed,
            };

            var nivel = new Border
            {
                Width = 26, Height = 26,
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right,   // al ensancharse, crece hacia la izquierda
                CornerRadius = new CornerRadius(13),
                Margin = new Thickness(0, 3, 0, 3),
                Background = new SolidColorBrush(aqui
                    ? Color.FromArgb(0x66, 0xFF, 0xB3, 0x00) : Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF)),
                BorderBrush = new SolidColorBrush(aqui
                    ? Color.FromArgb(0xEE, 0xFF, 0xC1, 0x07) : Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(aqui ? 2 : 1),
                ToolTip = $"nivel {i + 1}: {app} · {pantallas} pantalla(s) conocidas"
                        + (aqui ? " · estás aquí" : " · Ctrl+Shift y clic para ir"),
            };

            var dentro = new StackPanel { Orientation = Orientation.Horizontal };
            dentro.Children.Add(nombre);
            dentro.Children.Add(new TextBlock
            {
                // Dos letras cuando está cerrada; el nombre entero aparece al lado al abrirse.
                Text = app.Length >= 2 ? app[..2].ToUpperInvariant() : app.ToUpperInvariant(),
                Foreground = new SolidColorBrush(aqui
                    ? Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
                FontSize = 9, FontWeight = FontWeights.Bold, FontFamily = new FontFamily("Consolas"),
                Width = 24, TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
            nivel.Child = dentro;

            nivel.MouseEnter += (_, __) =>
            {
                nombre.Visibility = Visibility.Visible;
                nivel.Width = double.NaN;          // NaN = «lo que ocupe», que es lo que hace falta
                nivel.CornerRadius = new CornerRadius(13);
            };
            nivel.MouseLeave += (_, __) =>
            {
                nombre.Visibility = Visibility.Collapsed;
                nivel.Width = 26;
            };

            // Pulsar un nivel es IR a esa aplicación. Es la acción natural de la tira —enumera los
            // terrenos disponibles, así que señalarlos y no poder entrar sería enseñar puertas
            // pintadas— y no inventa nada: usa el mismo enfocar-o-abrir que ya usa todo lo demás.
            // SIN el «.exe»: FocusOrLaunch busca por Process.GetProcessesByName, que quiere el
            // nombre pelado. Pasándole «claude.exe» no encontraba ningún proceso y se iba a intentar
            // LANZAR la app —que ya estaba abierta— así que pulsar el nivel no hacía nada visible
            // (2026-08-04, reportado por el usuario). El identificador de superficie lleva la
            // extensión; el buscador de procesos, no.
            string destinoNivel = app;
            nivel.MouseLeftButtonUp += (_, __) => IrAlNivel(destinoNivel);
            _niveles.Children.Add(nivel);

            // El salto entre niveles se dibuja: dos puntos y una línea, para que se vea que hay que
            // CRUZAR algo —abrir la app— y no simplemente seguir por el mismo terreno.
            if (i < apps.Count - 1)
                _niveles.Children.Add(new System.Windows.Shapes.Rectangle
                {
                    Width = 2, Height = 10,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Fill = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
                });
        }
    }

    private void DibujarGrafo()
    {
        _lienzo.Children.Clear();

        // SOLO EL NIVEL EN EL QUE ESTÁS. La traza cruza aplicaciones —del explorador a Configuración
        // y vuelta—, y pintarlas juntas mezclaba en un mismo dibujo botones que no comparten
        // terreno: «Nuevo» del explorador al lado de «Bluetooth», sin que nada dijera que para pasar
        // de uno a otro hay que abrir otra app. Filtrando por app, la forma de moverse DENTRO de
        // donde estás se lee sola, y los saltos entre apps se cuentan aparte, en la tira de niveles.
        string appActual = NivelDe(_nodoActual.Length > 0
            ? _nodoActual
            : (_ultimaCorrida.Count > 0 ? _ultimaCorrida[^1].To : ""));
        DibujarNiveles(appActual);

        var traza = appActual.Length == 0
            ? _ultimaCorrida
            : _ultimaCorrida.Where(h => NivelDe(h.From).Equals(appActual, StringComparison.OrdinalIgnoreCase)
                                     && NivelDe(h.To).Equals(appActual, StringComparison.OrdinalIgnoreCase))
                            .ToList();

        // Los sitios de ESTE nivel por los que ya se ha pasado, haya saltos o no.
        var pisados = _vistos
            .Where(n => appActual.Length == 0
                     || NivelDe(n).Equals(appActual, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (traza.Count == 0 && pisados.Count == 0)
        {
            _lienzo.Children.Add(new TextBlock
            {
                Text = appActual.Length > 0
                    ? $"Todavía no hay recorrido dentro de «{appActual}».\nMuévete por la app o púlsale «Mapear esta app automáticamente»."
                    : "Todavía no hay ninguna corrida automática.\nPulsa «Mapear esta app automáticamente».",
                Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
                FontSize = 11, Margin = new Thickness(8),
            });
            _lienzo.Width = 300; _lienzo.Height = 80;
            return;
        }

        // Raíz: el origen del primer salto, o —si aún no hay ninguno— el primer sitio pisado.
        string raiz = traza.Count > 0 ? traza[0].From : pisados[0];
        var prof = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [raiz] = 0 };
        // Varias pasadas: una arista puede aprenderse antes de que su origen tenga profundidad.
        for (int pasada = 0; pasada < 6; pasada++)
            foreach (var (f, t, _) in traza)
                if (prof.TryGetValue(f, out int d) && (!prof.TryGetValue(t, out int dt) || dt > d + 1))
                    prof[t] = d + 1;
        foreach (var (f, t, _) in traza)
        {
            if (!prof.ContainsKey(f)) prof[f] = 0;
            if (!prof.ContainsKey(t)) prof[t] = 1;
        }

        // Los pisados sin salto conocido entran a la altura de la raíz: se sabe que existen y que
        // están en este nivel, y no se sabe todavía cómo se encadenan. Colocarlos abajo del todo
        // insinuaría una profundidad que nadie ha comprobado.
        foreach (var n in pisados) if (!prof.ContainsKey(n)) prof[n] = 0;

        // EL CROMO DE LA APP CUELGA DE LA APP, no de cada pantalla.
        //
        // El panel izquierdo del explorador —Imágenes, Notas, Música, Vídeos, Descargas…— está en
        // TODAS sus pantallas: son hermanos, y se llega a cualquiera desde cualquiera. Dibujarlos
        // como salidas de cada carpeta llenaba el grafo de las mismas aristas repetidas N veces y
        // hacía parecer que hay que aprender a llegar a cada hermano desde cada sitio (2026-08-04,
        // observado por el usuario). No hay que aprenderlo: el mapa ya lo deduce en cuanto se cruza
        // UNA vez desde donde sea. Lo que faltaba era decirlo en el dibujo.
        //
        // EL NIVEL LO DICE LA ESTRUCTURA, NO EL PASEO. Si el mapa ya sabe a qué nivel pertenece cada
        // pantalla —cuántas puertas hay que abrir para verla— ese es el eje del dibujo, y el orden
        // en que alguien navegó deja de importar. Llegar a Escritorio pasando por Imágenes no pone
        // Escritorio debajo de Imágenes: los dos se ven al abrir la app, así que los dos están en el
        // nivel 1 (2026-08-04, replanteado por el usuario).
        //
        // El recorrido no se tira: sigue siendo el material de las ACCIONES, donde el orden SÍ es la
        // información. Simplemente deja de mandar en la navegación.
        foreach (var n in prof.Keys.ToList())
            if (_map.Nodes.TryGetValue(n, out var ni) && ni.Nivel >= 0)
                prof[n] = ni.Nivel;

        // QUIÉN ES CROMO LO DICE EL MAPA, no este dibujo. Aquí se recontaba por cuenta propia y solo
        // sobre los nodos que había delante, así que una salida que el mapa sabe que está en toda la
        // app podía no llegar al umbral localmente y caer una fila más abajo: en Configuración,
        // «Windows Update» aparecía debajo estando cruzada desde las once pantallas (2026-08-04,
        // visto por el usuario). Otra vez dos respuestas para la misma pregunta — y la de aquí era
        // la peor informada, porque solo veía un trozo.
        var cromo = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // destino → etiqueta
        foreach (var h in _map.CromoDe(SurfaceMap.AppDe(_nodoActual)))
            if (NivelDe(h.To).Equals(appActual, StringComparison.OrdinalIgnoreCase))
                cromo[h.To] = h.Info.Label;

        // El centro del nivel: la aplicación. Los hermanos cuelgan de él, a un solo salto.
        string centro = cromo.Count > 0 ? $"nivel://{appActual}" : "";
        if (centro.Length > 0)
        {
            prof[centro] = 0;
            foreach (var d in cromo.Keys) prof[d] = 1;
            // Lo que ya se recorrió cuelga por debajo, para no mezclarse con los hermanos.
            foreach (var n in prof.Keys.ToList())
                if (n != centro && !cromo.ContainsKey(n)) prof[n] = Math.Max(prof[n], 2);
        }

        // DOS REPRESENTACIONES, no una encogida. Escalar el mismo dibujo funciona hasta que la letra
        // deja de leerse; a partir de ahí se sigue pagando el sitio que ocupa un texto que ya nadie
        // puede leer, y el recorrido —que es lo que se quiere ver— queda enterrado bajo etiquetas
        // borrosas (2026-08-04). Cuando el detalle no cabe con holgura, se cambia a un mapa de
        // puntos: la FORMA del recorrido se lee igual de bien, y el único nombre que se conserva es
        // el del nodo donde está ahora, que es el que hace falta.
        int filas = prof.Values.Max() + 1;
        int columnas = prof.GroupBy(kv => kv.Value).Max(g => g.Count());
        double necesarioX = 12 + columnas * (168.0 + 16), necesarioY = 24 + filas * 62.0;
        double dispX = _grafo.ActualWidth - 16, dispY = _grafo.ActualHeight - 16;
        double cabeDetalle = (dispX > 0 && dispY > 0)
            ? Math.Min(dispX / necesarioX, dispY / necesarioY) : 1;
        _compacto = cabeDetalle < 0.55;

        double anchoCaja = _compacto ? 18 : 168, altoCaja = _compacto ? 18 : 34;
        double sepX = _compacto ? 10 : 16, sepY = _compacto ? 34 : 62;
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
        var dibujadas = new HashSet<string>(traza.Select(e => e.From + "\n" + e.To), StringComparer.OrdinalIgnoreCase);
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
                // En puntos caben menos muñones y más juntos: son una señal de «aquí queda algo por
                // abrir», no un recuento, así que tres bastan para decirlo sin emborronar el nodo.
                if (pendiente >= (_compacto ? 3 : 6)) break;
                double paso = _compacto ? 5 : 13;
                double x = origen.X + (_compacto ? 3 : 14) + pendiente * paso;
                _lienzo.Children.Add(new System.Windows.Shapes.Line
                {
                    X1 = x, Y1 = origen.Y + altoCaja,
                    X2 = x, Y2 = origen.Y + altoCaja + (_compacto ? 6 : 13),
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
                // Al cromo se llega desde todas partes: se dibuja UNA vez desde el centro del nivel,
                // no una por pantalla. Repetirlo era el enredo que ocultaba la forma real.
                if (cromo.ContainsKey(h.To)) continue;
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

        // Del centro del nivel a cada hermano: una sola arista por hermano, alcanzable desde
        // cualquier pantalla de la app. Es la forma que el usuario tiene en la cabeza y la que el
        // mapa ya sabía; solo faltaba dibujarla así.
        if (centro.Length > 0 && pos.TryGetValue(centro, out var pc))
            foreach (var (destino, etiqueta) in cromo.Select(k => (k.Key, k.Value)))
            {
                if (!pos.TryGetValue(destino, out var pd)) continue;
                _lienzo.Children.Add(new System.Windows.Shapes.Line
                {
                    X1 = pc.X + anchoCaja / 2, Y1 = pc.Y + altoCaja,
                    X2 = pd.X + anchoCaja / 2, Y2 = pd.Y,
                    Stroke = new SolidColorBrush(Color.FromArgb(0x77, 0x64, 0xB5, 0xF6)),
                    StrokeThickness = 1.2,
                    ToolTip = $"«{etiqueta}» · disponible desde cualquier pantalla de {appActual}",
                });
            }

        // Aristas primero, para que las cajas queden encima de las líneas.
        foreach (var (f, t, label) in traza)
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

            if (_compacto) continue;   // en puntos, la etiqueta de cada arista sobra: no se leería
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

        // Lo que todos comparten se calcula UNA vez y se quita de todas las etiquetas: enseñarlo
        // diez veces no informa, y el sitio que ocupa es justo el que le falta a lo que distingue.
        string prefijo = PrefijoComun(pos.Keys);

        foreach (var kv in pos)
        {
            // El nodo donde está el recorrido ahora mismo va en ámbar y con borde grueso: durante
            // un mapeo en vivo, saber DÓNDE está es tan informativo como ver aparecer las aristas.
            bool esActual = string.Equals(kv.Key, _nodoActual, StringComparison.OrdinalIgnoreCase);
            bool esCentro = kv.Key == centro;
            // Azul = su nivel lo puso una persona. Se distingue de lo deducido porque son dos cosas
            // distintas: una es lo que el sistema cree y la otra lo que alguien sabe.
            bool fijado = _map.ExitsFrom(kv.Key).Any(h => h.Info.NivelFijado)
                || _map.Edges().Any(e => e.To.Equals(kv.Key, StringComparison.OrdinalIgnoreCase)
                                      && e.Info.NivelFijado);
            var caja = new Border
            {
                Width = anchoCaja, Height = altoCaja,
                // En puntos son círculos: una caja diminuta con esquinas parece una caja rota, y un
                // punto se lee como «un sitio» sin fingir que dentro cabía algo.
                CornerRadius = new CornerRadius(_compacto ? anchoCaja / 2 : 6),
                // El centro del nivel va en azul: no es un sitio al que se llega, es la app misma.
                Background = new SolidColorBrush(esCentro ? Color.FromArgb(0x44, 0x21, 0x96, 0xF3)
                    : esActual ? Color.FromArgb(0x55, 0xFF, 0xB3, 0x00)
                    : fijado ? Color.FromArgb(0x4A, 0x21, 0x96, 0xF3)
                    : Color.FromArgb(0x30, 0x2E, 0x7D, 0x32)),
                BorderBrush = new SolidColorBrush(esCentro ? Color.FromArgb(0xAA, 0x64, 0xB5, 0xF6)
                    : esActual ? Color.FromArgb(0xEE, 0xFF, 0xC1, 0x07)
                    : fijado ? Color.FromArgb(0xCC, 0x64, 0xB5, 0xF6)
                    : Color.FromArgb(0x55, 0x66, 0xBB, 0x6A)),
                BorderThickness = new Thickness(esActual || esCentro ? 2 : 1),
                ToolTip = esCentro
                    ? $"{appActual} · lo que cuelga de aquí se alcanza desde cualquier pantalla de la app"
                    : kv.Key,
                Child = _compacto ? null : new TextBlock
                {
                    Text = esCentro ? appActual : Distintivo(kv.Key, prefijo),
                    Foreground = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)),
                    FontSize = 9.5, FontFamily = new FontFamily("Consolas"),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(6, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            // El ÚNICO nombre que sobrevive al alejarse es el de donde estás. Un mapa de puntos sin
            // ninguna referencia es bonito y no sirve: hace falta saber cuál de todos eres tú.
            if (_compacto && esActual)
            {
                var etiqueta = new TextBlock
                {
                    Text = Distintivo(kv.Key, prefijo),
                    Foreground = new SolidColorBrush(Color.FromArgb(0xEE, 0xFF, 0xC1, 0x07)),
                    FontSize = 10, FontFamily = new FontFamily("Consolas"),
                    Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x10, 0x10, 0x14)),
                    Padding = new Thickness(4, 1, 4, 1),
                };
                Canvas.SetLeft(etiqueta, kv.Value.X + anchoCaja + 6);
                Canvas.SetTop(etiqueta, kv.Value.Y - 2);
                _lienzo.Children.Add(etiqueta);
            }
            Canvas.SetLeft(caja, kv.Value.X);
            Canvas.SetTop(caja, kv.Value.Y);
            _lienzo.Children.Add(caja);
        }

        _lienzo.Width = Math.Max(maxX + 12, 320);
        _lienzo.Height = 24 + (prof.Values.Max() + 1) * sepY;
        AjustarALaVista();
        // Durante el mapeo el estado lo escribe el propio recorrido («explorando X · N pantallas»),
        // que dice más que un recuento: no se pisa.
        // Se dice EN QUÉ MODO está. Al alejarse desaparecen los nombres, y sin avisar eso se lee
        // como que el grafo se ha vaciado en vez de como que se ha resumido. Además la barra es lo
        // único de esta vista que sigue siendo legible para el sistema: la capa, al volverse
        // atravesable, dejó de exponer su contenido, así que este texto es el único sitio donde
        // comprobar desde fuera qué se está dibujando (2026-08-04).
        if (_crawlCts == null)
            _status.Text = $"grafo · {pos.Count} pantalla(s), {traza.Count} ruta(s)"
                         + (_compacto ? " · vista de puntos (alejado)" : " · vista con nombres");

        // Se registra lo dibujado. Desde que la capa es atravesable no expone su contenido a UIA,
        // así que esta línea es la única forma de comprobar desde fuera qué hay pintado — y sin ella
        // «no se ve nada» y «no se está dibujando nada» son indistinguibles (2026-08-04).
        string huella = $"{appActual}|{pos.Count}|{traza.Count}|{_compacto}";
        if (huella != _huellaDibujo)
        {
            _huellaDibujo = huella;
            LogBus.Log("explorador", $"grafo: nivel «{appActual}» · {pos.Count} nodo(s), "
                + $"{traza.Count} salto(s) · {(_compacto ? "puntos" : "nombres")}");
        }
    }

    // ── Mapeo autónomo ───────────────────────────────────────────────────────

    /// <summary>
    /// Lanza el recorrido automático. El botón se convierte en «Detener» mientras corre: parar
    /// tiene que estar a un clic, sin buscarlo, porque esto mueve el ratón y el foco de la máquina.
    /// </summary>
    /// <summary>
    /// Abre el catálogo de apps instaladas. Al elegir una, se abre y se aprende.
    /// </summary>
    /// <remarks>
    /// El mapeo empieza SIEMPRE por traer la app al frente y esperar a que esté: aprender una
    /// aplicación que aún se está pintando anota media pantalla y la da por completa. Y si no se
    /// consigue traerla, no se mapea nada — es preferible decirlo a llenar el grafo con las puertas
    /// de la ventana equivocada, que es el veneno que ya conocemos.
    /// </remarks>
    private void AbrirCarrusel()
    {
        if (_carrusel != null) { _carrusel.Activate(); return; }

        var carrusel = new CarruselDeApps();
        _carrusel = carrusel;
        carrusel.Closed += (_, __) => _carrusel = null;
        carrusel.Elegida += app => Dispatcher.BeginInvoke(new Action(async () => await AprenderAppAsync(app)));
        _ = carrusel.MostrarAsync();
    }

    private async Task AprenderAppAsync(SystemApi.AppInstalada app)
    {
        _status.Text = $"abriendo «{app.Nombre}»…";
        LogBus.Log("carrusel", $"abriendo «{app.Nombre}» para aprenderla");

        // SE LANZA EL ACCESO DIRECTO Y SE COMPRUEBA MIRANDO, no preguntando por un proceso con ese
        // nombre. «Control Panel» abre de verdad, pero su ventana es de explorer.exe —es una
        // ventana del shell—, así que esperar un proceso llamado «Control Panel» daba «no se pudo
        // abrir» con el Panel de control delante (2026-08-05). Lo que dice que se abrió es que la
        // pantalla cambió, que es la misma regla que rige en todo lo demás.
        string antes = _where()?.Id ?? "";
        await Task.Run(() =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(app.Lnk) { UseShellExecute = true }); }
            catch (Exception e) { LogBus.Log("carrusel", $"no se pudo lanzar «{app.Nombre}»: {e.Message}"); }
        });

        string ahora = antes;
        for (int i = 0; i < 40 && (ahora.Length == 0 || ahora == antes); i++)
        {
            await Task.Delay(250);
            ahora = _where()?.Id ?? "";
        }

        if (ahora.Length == 0 || ahora == antes)
        {
            _status.Text = $"abrí «{app.Nombre}» pero la pantalla no cambió; no mapeo a ciegas";
            LogBus.Log("carrusel", $"«{app.Nombre}»: la pantalla siguió siendo «{antes}»; no se mapea");
            return;
        }
        LogBus.Log("carrusel", $"«{app.Nombre}» abierta: la pantalla pasó a «{ahora}»");

        // Que esté delante no es que esté lista: se le da tiempo a terminar de pintarse.
        await Task.Delay(1200);

        await CrawlAsync();
    }

    /// <summary>
    /// Le pide a un modelo que MIRE la pantalla y explique la jerarquía de la app.
    ///
    /// Los números se encienden solo durante la pregunta y se apagan después: son para que el
    /// maestro pueda señalar sin ambigüedad, no para que vivan encima de la pantalla del usuario.
    /// </summary>
    private async Task EnsenarLaAppAsync()
    {
        var loc = _where();
        if (loc == null) { LogBus.Log("maestro", "no se sabe dónde estamos: no se enseña"); return; }
        string app = SurfaceMap.AppDe(loc.Id);
        if (app.Length == 0) { LogBus.Log("maestro", $"«{loc.Id}» no dice de qué app es: no se enseña"); return; }
        LogBus.Log("maestro", $"enseñando «{app}» desde «{loc.Id}»…");

        _status.Text = "mirando la app para entender su navegación…";

        // LOS PUNTOS SOLO EXISTEN EN LA VISTA DE PUNTOS. En la vista de grafo se dibuja el mapa, no
        // los elementos de la pantalla, así que no había nada que numerar y la lección se abortaba
        // con «los puntos no llegaron a numerarse» — justo al pedir el mapeo desde el catálogo, que
        // es cuando el explorador suele estar mostrando el grafo (2026-08-06, visto por el usuario).
        // Se cambia de vista para preguntar y se devuelve como estaba.
        bool veniaDelGrafo = _graphView;
        if (veniaDelGrafo) SetGraphView(false);

        Numerar = true;

        // SE ESPERA A QUE LOS NÚMEROS ESTÉN, no un rato «por si acaso». El repintado va por su
        // cuenta —lee el árbol de UI en otro hilo y pinta cuando puede—, así que una espera fija se
        // queda corta justo cuando la pantalla tiene mucho que leer: la foto salía sin números y el
        // maestro recibía una lista vacía (2026-08-05). Se mira hasta que los haya.
        for (int i = 0; i < 30 && _numeradas.Count == 0; i++)
        {
            RefreshEdges();
            await Task.Delay(150);
        }
        if (_numeradas.Count == 0)
        {
            LogBus.Log("maestro", "los puntos no llegaron a numerarse: no se enseña");
            Numerar = false;
            if (veniaDelGrafo) SetGraphView(true);
            return;
        }
        await Task.Delay(250);   // que el último repintado esté en pantalla ANTES de la foto

        try
        {
            var maestro = new Navigation.MaestroDeApps(_map);
            var leccion = await maestro.EnsenarAsync(app, loc.Id, PuertasNumeradas(), CancellationToken.None);
            _status.Text = leccion == null
                ? "no pude consultar al maestro; sigo con el recorrido"
                : $"jerarquía aprendida: {leccion.Resumen}";
        }
        catch (Exception e)
        {
            LogBus.Log("maestro", $"falló la enseñanza: {e.Message}");
            _status.Text = "la enseñanza falló; sigo con el recorrido";
        }
        finally
        {
            Numerar = false;
            RefreshEdges();
            if (veniaDelGrafo) SetGraphView(true);   // se devuelve la vista como estaba
        }
    }

    private async Task CrawlAsync()
    {
        if (_crawlCts != null) { _crawlCts.Cancel(); return; }

        var loc = _where();
        if (loc == null) { _status.Text = "trae al frente la app que quieres mapear"; return; }

        // EL GUARDIA SE ARMA ANTES DE LA LECCIÓN. Estaba después, y como preguntar tarda varios
        // segundos, cada pulsación que llegara mientras tanto pasaba el «¿ya hay uno en marcha?» y
        // arrancaba otra lección: tres seguidas en dos segundos, y tres facturas (2026-08-06).
        _crawlCts = new CancellationTokenSource();

        // PRIMERO LA LECCIÓN, DESPUÉS EL RECORRIDO. El maestro dice de un vistazo qué es navegación
        // permanente —algo que al recorredor le cuesta varias vueltas deducir contando— y con esa
        // jerarquía ya puesta, el recorrido sabe qué está explorando en vez de descubrirlo al final.
        // Va aquí y no en quien llama para que valga para TODAS las formas de pedir un mapeo: el
        // botón de «esta app» y el catálogo tienen que aprender lo mismo.
        await EnsenarLaAppAsync();
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

    public void ShowRect(Rect fisico) => ShowRects(new[] { fisico });

    /// <summary>
    /// Enciende VARIOS recuadros a la vez.
    ///
    /// Uno solo bastaba para el hover del explorador, pero señalar de viva voz casi nunca es de uno
    /// en uno: «esos cuatro son del menú principal» necesita ver los cuatro juntos, o no hay forma
    /// de confirmar que son esos y no otros (2026-08-05, pedido por el usuario). El primero se pinta
    /// más fuerte, porque es del que se está hablando.
    /// </summary>
    public void ShowRects(IReadOnlyList<Rect> fisicos)
    {
        var src = PresentationSource.FromVisual(this);
        Matrix m = src?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;

        // Se reutiliza el primero y se crean los demás al vuelo: lo normal es uno, y no tiene
        // sentido pagar por adelantado unos recuadros que casi nunca se usan.
        foreach (var extra in _extras) _canvas.Children.Remove(extra);
        _extras.Clear();

        for (int i = 0; i < fisicos.Count; i++)
        {
            var tl = m.Transform(new Point(fisicos[i].X, fisicos[i].Y));
            var br = m.Transform(new Point(fisicos[i].Right, fisicos[i].Bottom));

            var r = i == 0 ? _rect : NuevoRecuadro();
            Canvas.SetLeft(r, tl.X);
            Canvas.SetTop(r, tl.Y);
            r.Width = Math.Max(0, br.X - tl.X);
            r.Height = Math.Max(0, br.Y - tl.Y);
            r.Visibility = Visibility.Visible;
            if (i > 0) { _canvas.Children.Add(r); _extras.Add(r); }
        }
        if (fisicos.Count == 0) HideRect();
    }

    private readonly List<System.Windows.Shapes.Rectangle> _extras = new();

    private static System.Windows.Shapes.Rectangle NuevoRecuadro() => new()
    {
        Stroke = new SolidColorBrush(Color.FromArgb(0xCC, 0xA9, 0x6B, 0xF6)),
        StrokeThickness = 2,
        Fill = new SolidColorBrush(Color.FromArgb(0x1C, 0xA9, 0x6B, 0xF6)),
    };

    public void HideRect()
    {
        _rect.Visibility = Visibility.Collapsed;
        foreach (var extra in _extras) _canvas.Children.Remove(extra);
        _extras.Clear();
    }
}
