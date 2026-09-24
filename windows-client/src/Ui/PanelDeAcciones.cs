using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.RegularExpressions;

namespace U.WindowsClient.Ui;

/// <summary>
/// EL NOTCH: arriba al centro, una pieza compacta que dice EN QUÉ ESTÁ Ü con una sola frase.
/// </summary>
public sealed class PanelDeAcciones : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    /// <summary>
    /// Cuánto aguanta en pantalla sin nada nuevo. Noventa segundos: sigue siendo «se va solo» y no
    /// «hay que cerrarlo», y una pausa pensando qué pedir es parte de hablar.
    /// </summary>
    private static readonly TimeSpan Caducidad = TimeSpan.FromSeconds(90);

    /// <summary>El radio de las puntas. Sobre 62 de alto es una esquina continua, no una pastilla.</summary>
    private const double Radio = 20;

    /// <summary>La letra del notch: la de Windows 11, con la de siempre detrás por si no está.</summary>
    private static readonly FontFamily Letra = new("Segoe UI Variable Text, Segoe UI");

    /// <summary>
    /// La sombra del notch: NEGRA, y no la azulada del estudio.
    ///
    /// La escala del estudio está calibrada para lo elevado SOBRE BLANCO, donde un gris azulado da
    /// profundidad y el negro puro ensucia. Aquí el suelo es el escritorio de otra persona —puede ser
    /// un SAP azul claro, un Word blanco o una foto— y lo que separa la pieza no es un tono, es
    /// oscuridad. Corta (24 y 4) y no larga: una sombra larga bajo una pieza pequeña la hace flotar
    /// como un cartel; una corta la apoya.
    /// </summary>
    private static readonly DropShadowEffect Sombra = Elevacion();

    private static DropShadowEffect Elevacion()
    {
        var s = new DropShadowEffect
        {
            BlurRadius = 24,
            ShadowDepth = 4,
            Direction = 270,
            Opacity = 0.45,
            Color = Colors.Black,
            RenderingBias = RenderingBias.Quality,
        };
        s.Freeze();
        return s;
    }

    /// <summary>Un pincel congelado desde un ARGB de <see cref="PaletaDelNotch"/>.</summary>
    private static SolidColorBrush Pincel(uint argb)
    {
        var b = new SolidColorBrush(ColorDe(argb));
        b.Freeze();
        return b;
    }

    private static Color ColorDe(uint argb) => Color.FromArgb(
        (byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    private readonly LoQueDiceElNotch _dice = new();

    private readonly Path _icono;
    private readonly RotateTransform _giro = new();
    private readonly TextBlock _texto;
    private readonly Grid _ventanaDelTexto;
    private readonly TranslateTransform _desplazamientoDelTexto = new();
    private readonly Border _placa;      // la gemela sin hijos: la única que lleva el Effect
    private readonly Border _notch;      // la pieza que se ve, con el contenido dentro
    private readonly Grid _contenidoCompacto;
    private readonly Grid _contenidoChat;
    private readonly Button _botonMensajes;
    private readonly Button _botonCerrarChat;
    private readonly TextBox _entradaChat;
    private readonly ScrollViewer _scrollChat;
    private readonly StackPanel _listaChat;
    private readonly DispatcherTimer _scrollChatTimer;
    private readonly List<(bool EsDeU, string Texto)> _mensajes = new();
    private readonly ScaleTransform _entrada = new(1, 1);
    private readonly TranslateTransform _baja = new(0, 0);
    private readonly DispatcherTimer _caducar;
    private DateTime _ultimoCambio = DateTime.UtcNow;
    private EstadoDelNotch _pintado = EstadoDelNotch.Voz;

    /// <summary>Vigila el borde de arriba para asomar el notch aunque no tenga nada nuevo que decir
    /// (promesa 260, pedido del dueño 2026-09-17). Sondea en vez de engancharse a un hook global de
    /// ratón: <see cref="Uia.UiInspector"/> ya tiene uno para cuando de verdad hace falta cada
    /// movimiento, y aquí basta con mirar cada rato — el gesto es acercarse, no pasar de largo.</summary>
    private readonly DispatcherTimer _asomo;
    private readonly DispatcherTimer _marquesina;
    private bool _dentroDeLaZona;
    private bool _asomadoSoloPorHover;
    private bool _cursorSobreLaPieza;
    private double _excesoDeTexto;
    private double _textoX;
    private int _direccionMarquesina = -1;
    private DateTime _pausaMarquesinaHasta;

    /// <summary>Señal del botón de mensajes; la conversación vive en esta misma ventana.</summary>
    public event Action? ChatSolicitado;

    /// <summary>Texto enviado desde la caja del chat embebido.</summary>
    public event Action<string>? TextoEnviado;

    public bool ChatAbierto { get; private set; }

    public PanelDeAcciones()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        IsHitTestVisible = true;
        Focusable = true;
        ShowActivated = false;
        Title = "Ü Acciones";

        // LA SOMBRA VA EN UNA PLACA GEMELA SIN HIJOS, no en la pieza que lleva el texto. Un Effect es
        // un shader: WPF rasteriza a una textura intermedia todo el subárbol que cuelgue de él, y ahí
        // dentro cada letra pierde el ClearType y sale lavada.
        _placa = new Border
        {
            Background = Brushes.Black,
            Effect = Sombra,
            CornerRadius = new CornerRadius(Radio),
            Width = MedidaDelNotch.Ancho,
            Height = MedidaDelNotch.Alto,
        };

        _icono = new Path
        {
            Stroke = Pincel(PaletaDelNotch.Tinta),
            StrokeThickness = IconosDelNotch.Grosor,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = null,
            Width = IconosDelNotch.Caja,
            Height = IconosDelNotch.Caja,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _giro,
        };

        // El icono se dibuja en su caja de 24 y se escala entera: así todos pesan igual pase lo que
        // pase con el tamaño de la pieza.
        var cajaDelIcono = new Viewbox
        {
            Width = MedidaDelNotch.CajaDelIcono,
            Height = MedidaDelNotch.CajaDelIcono,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            Child = _icono,
        };

        _texto = new TextBlock
        {
            Foreground = Pincel(PaletaDelNotch.Tinta),
            FontFamily = Letra,
            FontSize = MedidaDelNotch.LetraDeLaTarea,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.None,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            RenderTransform = _desplazamientoDelTexto,
            RenderTransformOrigin = new Point(0, 0.5),
            Text = _dice.Texto,
        };

        _ventanaDelTexto = new Grid
        {
            Width = MedidaDelNotch.AnchoDelTexto,
            Height = 30,
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _ventanaDelTexto.Children.Add(_texto);

        _botonMensajes = BotonMensajes();
        _botonMensajes.Click += (_, __) => AbrirChat(true);

        var rejilla = new Grid();
        rejilla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(MedidaDelNotch.CajaDelIcono) });
        rejilla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(MedidaDelNotch.AireDelIcono) });
        rejilla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rejilla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(MedidaDelNotch.AireDelChat) });
        rejilla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(MedidaDelNotch.CajaDelChat) });
        Grid.SetColumn(cajaDelIcono, 0);
        Grid.SetColumn(_ventanaDelTexto, 2);
        Grid.SetColumn(_botonMensajes, 4);
        rejilla.Children.Add(cajaDelIcono);
        rejilla.Children.Add(_ventanaDelTexto);
        rejilla.Children.Add(_botonMensajes);

        _contenidoCompacto = rejilla;
        (_contenidoChat, _botonCerrarChat, _entradaChat, _scrollChat, _listaChat) = CrearChat();

        _notch = new Border
        {
            // NEGRO DE VERDAD (spec 023): lo que lo despega del escritorio es el filete de luz de
            // abajo, no una gota de azul.
            Background = Pincel(PaletaDelNotch.Fondo),
            // EL FILETE ES UN HILO DE LUZ, no un contorno: a ese alfa se ve como el canto de algo
            // iluminado desde arriba, que es lo que despega la pieza sin dibujarle un marco.
            BorderBrush = Pincel(PaletaDelNotch.Filete),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Radio),
            Padding = new Thickness(MedidaDelNotch.AireIzquierda, MedidaDelNotch.AireVertical,
                                    MedidaDelNotch.AireDerecha, MedidaDelNotch.AireVertical),
            // MIDE SIEMPRE LO MISMO (promesa 249).
            Width = MedidaDelNotch.Ancho,
            Height = MedidaDelNotch.Alto,
        };

        var contenido = new Grid();
        contenido.Children.Add(_contenidoCompacto);
        contenido.Children.Add(_contenidoChat);
        _notch.Child = contenido;
        _contenidoChat.Visibility = Visibility.Collapsed;

        // ESCALA Y CAÍDA A LA VEZ: antes solo escalaba (0.96→1). Combinarlo con un desplazamiento
        // vertical es lo que hace que la pieza «llegue» cayendo desde el borde de arriba en vez de
        // solo desvanecerse en su sitio (pedido del dueño, 2026-09-17: «sorpréndeme con su transición»).
        var entrada = new TransformGroup();
        entrada.Children.Add(_entrada);
        entrada.Children.Add(_baja);

        var caja = new Grid
        {
            // El hueco para que el desenfoque quepa dentro del cristal: la ventana es SizeToContent
            // sobre AllowsTransparency, así que mide lo que mide el contenido y sin reservar sitio la
            // sombra sale cortada contra el borde.
            Margin = Estudio.HolguraDe(Sombra),
            RenderTransformOrigin = new Point(0.5, 0),
            RenderTransform = entrada,
        };
        caja.Children.Add(_placa);
        caja.Children.Add(_notch);
        Content = caja;

        // El texto, nítido: AllowsTransparency apaga el ClearType por defecto y sin volver a pedirlo
        // las letras salen blandas. Se puede porque el notch de dentro es opaco.
        this.Nitida();

        SizeChanged += (_, __) => Recolocar();

        // Topmost no basta: es una posición en una lista y cualquier otra capa que pida lo mismo nos
        // adelanta. Ver SiempreDelante. En el grupo, por encima de la carita y por debajo de lo de
        // Jev, con el reloj de todos y no con uno propio (promesa 378, spec 049).
        SiempreDelante.EntraAlGrupo(this, Jev.Capa.Notch);

        _caducar = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _caducar.Tick += (_, __) =>
        {
            if (_dice.Estado != EstadoDelNotch.EnCurso && DateTime.UtcNow - _ultimoCambio > Caducidad) Limpiar();
        };

        // CIENTO CINCUENTA MILISEGUNDOS: rápido para que el gesto se sienta al toque, y lejos de la
        // cadencia de un hook por movimiento —aquí no hace falta cada píxel, solo saber si el cursor
        // ronda el borde—. Arranca aquí y no al primer Habla(): el gesto tiene que funcionar «sin
        // importar si Ü está hablando o no», también antes de que diga su primera palabra.
        _asomo = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _asomo.Tick += (_, __) => RevisarAsomo();
        _asomo.Start();
        _marquesina = new DispatcherTimer(DispatcherPriority.Render)
        { Interval = TimeSpan.FromMilliseconds(32) };
        _marquesina.Tick += (_, __) => MoverMarquesina();
        _scrollChatTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _scrollChatTimer.Tick += (_, __) => MoverScrollChat();
        Closed += (_, __) => { _asomo.Stop(); _marquesina.Stop(); _scrollChatTimer.Stop(); };

        // SOLO EL CONTENIDO, NO LA VISIBILIDAD. `Pintar()` completo llama a `Aparecer()`, y eso era
        // inofensivo mientras esta clase nacía en el primer Habla() —el mismo Pintar() de esa llamada
        // volvía a pasar por aquí un instante después—. Desde que nace al arrancar la app (promesa
        // 257, para que el asomo funcione desde el primer segundo), ese mismo Aparecer() sin querer
        // sacaba el notch a pantalla en cuanto Ü abría, sin que nadie hubiera dicho nada todavía.
        PintarContenido();
    }

    /// <summary>
    /// ¿EL CURSOR RONDA EL BORDE DE ARRIBA? Si es así y el notch está escondido, lo trae —sin tocar
    /// <see cref="_dice"/>, que sigue diciendo lo mismo que decía—. Si se había asomado SOLO por esto
    /// —nada nuevo que decir— y el cursor se aleja, se retira: quedarse ahí sin motivo sería un
    /// cartel pegado, no una isla que asoma.
    /// </summary>
    private void RevisarAsomo()
    {
        if (!GetCursorPos(out var p)) return;
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        var cursor = new Point(p.X / dpi.DpiScaleX, p.Y / dpi.DpiScaleY);
        var (libre, _) = LaBarraDeTareas.Mirar();
        if (ChatAbierto)
        {
            _cursorSobreLaPieza = true;
            _dentroDeLaZona = true;
            return;
        }

        var tamano = new Size(MedidaDelNotch.Ancho, MedidaDelNotch.Alto);
        bool dentro = ReglaDeLaBandeja.MantieneLaIntencion(libre, tamano, cursor, IsVisible);
        bool sobrePieza = IsVisible && ReglaDeLaBandeja.ActivaEscritura(libre, tamano, cursor);

        if (dentro && !_dentroDeLaZona && !IsVisible)
        {
            _asomadoSoloPorHover = true;
            Aparecer();
        }
        else if (!dentro && _dentroDeLaZona && _asomadoSoloPorHover)
        {
            _asomadoSoloPorHover = false;
            Limpiar();
        }

        _cursorSobreLaPieza = sobrePieza;
        _dentroDeLaZona = dentro;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var h = new WindowInteropHelper(this).Handle;
        SetWindowLong(h, GWL_EXSTYLE,
            GetWindowLong(h, GWL_EXSTYLE) | WS_EX_LAYERED | WS_EX_TOOLWINDOW);
    }

    /// <summary>Empieza un paso: se pinta YA, antes de saber cómo acaba. Ese instante es el tiempo real.</summary>
    public void Empieza(string texto)
    {
        _dice.Empieza(texto);
        Pintar();
    }

    /// <summary>Cierra el paso con su desenlace.</summary>
    public void Termina(string texto, bool ok)
    {
        _dice.Termina(texto, ok);
        Pintar();
    }

    /// <summary>
    /// Lo que se está diciendo ahora mismo, de quien sea. Lo de la persona además queda en boca para
    /// subir a ser la tarea cuando cierre el turno (promesa 252).
    /// </summary>
    public void Habla(string texto, bool esDeU)
    {
        string limpio = TextoSinEmojis(texto);
        if (limpio.Length == 0) return;
        if (esDeU) _dice.UDice(limpio); else _dice.PersonaDice(limpio);
        ActualizaMensaje(limpio, esDeU);
        Pintar();
    }

    public void Mensaje(string texto, bool esDeU)
    {
        ActualizaMensaje(TextoSinEmojis(texto), esDeU);
    }

    public void Avisar(string texto)
    {
        string limpio = TextoSinEmojis(texto);
        if (limpio.Length == 0) return;
        _dice.UDice(limpio);
        Pintar();
    }

    public void AbrirChat(bool enfocar)
    {
        ChatSolicitado?.Invoke();
        if (ChatAbierto) { if (enfocar) EnfocarEntrada(); return; }
        ChatAbierto = true;
        _caducar.Stop();
        _asomadoSoloPorHover = false;
        _contenidoCompacto.Visibility = Visibility.Collapsed;
        _contenidoChat.Visibility = Visibility.Visible;
        _notch.Width = MedidaDelNotch.AnchoDelChat;
        _notch.Height = MedidaDelNotch.AltoDelChat;
        _placa.Width = _notch.Width;
        _placa.Height = _notch.Height;
        Recolocar();
        _scrollChat.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            BajarScrollChat();
            if (enfocar) EnfocarEntrada();
        }), DispatcherPriority.Input);
    }

    public void CerrarChat()
    {
        if (!ChatAbierto) return;
        ChatAbierto = false;
        _contenidoChat.Visibility = Visibility.Collapsed;
        _contenidoCompacto.Visibility = Visibility.Visible;
        _notch.Width = MedidaDelNotch.Ancho;
        _notch.Height = MedidaDelNotch.Alto;
        _placa.Width = _notch.Width;
        _placa.Height = _notch.Height;
        Recolocar();
        _entradaChat.Clear();
        _caducar.Start();
    }

    private Button BotonMensajes()
    {
        var boton = new Button
        {
            Width = MedidaDelNotch.CajaDelChat,
            Height = MedidaDelNotch.CajaDelChat,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Pincel(PaletaDelNotch.Tinta),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        AutomationProperties.SetName(boton, "Abrir conversación");
        EstilizarBoton(boton, 10);
        var glifo = new Path
        {
            Data = Geometry.Parse("M3,4 A2,2 0 0 1 5,2 H19 A2,2 0 0 1 21,4 V13 A2,2 0 0 1 19,15 H10 L6,19 V15 H5 A2,2 0 0 1 3,13 Z"),
            Stroke = Pincel(PaletaDelNotch.Tinta),
            StrokeThickness = 1.7,
            Fill = Brushes.Transparent,
            Stretch = Stretch.Uniform,
            Width = 19,
            Height = 19,
        };
        boton.Content = glifo;
        return boton;
    }

    private (Grid, Button, TextBox, ScrollViewer, StackPanel) CrearChat()
    {
        var cerrar = new Button
        {
            Width = 28, Height = 28, Content = "×", FontSize = 18,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Foreground = Pincel(PaletaDelNotch.Tinta), Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(cerrar, "Cerrar conversación");
        EstilizarBoton(cerrar, 10);
        cerrar.Content = new Path
        {
            Data = Geometry.Parse("M7,7 L17,17 M17,7 L7,17"), Stroke = Pincel(PaletaDelNotch.Tinta),
            StrokeThickness = 1.7, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Width = 24, Height = 24,
        };
        cerrar.Click += (_, __) => CerrarChat();
        var lista = new StackPanel { Orientation = Orientation.Vertical };
        var scroll = new ScrollViewer
        {
            Content = lista, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.VerticalOnly, CanContentScroll = false,
            Margin = new Thickness(0, 10, 0, 10),
        };
        var entrada = new TextBox
        {
            Height = 38, Padding = new Thickness(12, 0, 12, 0),
            Background = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)),
            Foreground = Pincel(PaletaDelNotch.Tinta), CaretBrush = Pincel(PaletaDelNotch.Tinta),
            BorderThickness = new Thickness(0), FontFamily = Letra, FontSize = 14,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        entrada.KeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Enter) return;
            EnviarTexto(); e.Handled = true;
        };
        var enviar = new Button
        {
            Width = 38, Height = 38, Content = "↑", FontSize = 20,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Foreground = Pincel(PaletaDelNotch.Tinta), Cursor = System.Windows.Input.Cursors.Hand,
        };
        AutomationProperties.SetName(enviar, "Enviar mensaje");
        EstilizarBoton(enviar, 10);
        enviar.Content = new Path
        {
            Data = Geometry.Parse("M5,12 H19 M12,5 L19,12 L12,19"), Stroke = Pincel(PaletaDelNotch.Tinta),
            StrokeThickness = 1.7, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round, Width = 22, Height = 22,
        };
        enviar.Click += (_, __) => EnviarTexto();
        var entradaFila = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        entradaFila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        entradaFila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        Grid.SetColumn(entrada, 0); Grid.SetColumn(enviar, 1);
        entradaFila.Children.Add(entrada); entradaFila.Children.Add(enviar);
        var cabecera = new Grid { Height = 28 };
        var titulo = new TextBlock { Text = "", Foreground = Pincel(PaletaDelNotch.Tinta), FontSize = 14, FontWeight = FontWeights.SemiBold };
        Grid.SetColumn(cerrar, 1);
        cabecera.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cabecera.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        cabecera.Children.Add(titulo); cabecera.Children.Add(cerrar);
        var vista = new Grid { Margin = new Thickness(18, 14, 18, 14) };
        vista.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
        vista.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        vista.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(cabecera, 0); Grid.SetRow(scroll, 1); Grid.SetRow(entradaFila, 2);
        vista.Children.Add(cabecera); vista.Children.Add(scroll); vista.Children.Add(entradaFila);
        return (vista, cerrar, entrada, scroll, lista);
    }

    private static void EstilizarBoton(Button boton, double radio)
    {
        boton.FocusVisualStyle = null;
        var plantilla = new ControlTemplate(typeof(Button));
        var raiz = new FrameworkElementFactory(typeof(Grid));
        var fondo = new FrameworkElementFactory(typeof(Border));
        fondo.Name = "fondo";
        fondo.SetValue(Border.CornerRadiusProperty, new CornerRadius(radio));
        fondo.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        fondo.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        fondo.SetValue(Border.BorderThicknessProperty, new Thickness(0));
        var contenido = new FrameworkElementFactory(typeof(ContentPresenter));
        contenido.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        contenido.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        contenido.SetValue(ContentPresenter.ContentSourceProperty, "Content");
        raiz.AppendChild(fondo);
        raiz.AppendChild(contenido);
        plantilla.VisualTree = raiz;
        var hover = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.72, "fondo"));
        plantilla.Triggers.Add(hover);
        var foco = new Trigger { Property = Button.IsKeyboardFocusedProperty, Value = true };
        foco.Setters.Add(new Setter(Border.BorderBrushProperty, Pincel(PaletaDelNotch.Filete), "fondo"));
        foco.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(1), "fondo"));
        plantilla.Triggers.Add(foco);
        boton.Template = plantilla;
    }

    private void EnviarTexto()
    {
        string texto = _entradaChat.Text.Trim();
        if (texto.Length == 0) return;
        _entradaChat.Clear();
        ActualizaMensaje(texto, false, nuevaLinea: true);
        TextoEnviado?.Invoke(texto);
    }

    private void ActualizaMensaje(string texto, bool esDeU, bool nuevaLinea = false)
    {
        texto = TextoParaChat(texto);
        if (string.IsNullOrWhiteSpace(texto)) return;
        if (!nuevaLinea && _mensajes.Count > 0 && _mensajes[^1].EsDeU == esDeU)
            _mensajes[^1] = (esDeU, texto);
        else _mensajes.Add((esDeU, texto));
        if (_mensajes.Count > 40) _mensajes.RemoveRange(0, _mensajes.Count - 40);
        PintarChat();
    }

    private static string TextoSinEmojis(string texto)
    {
        if (string.IsNullOrEmpty(texto)) return "";
        var limpio = new StringBuilder(texto.Length);
        foreach (var rune in texto.EnumerateRunes())
        {
            int n = rune.Value;
            bool emoji = n is >= 0x1F000 and <= 0x1FAFF
                or >= 0x2600 and <= 0x27BF
                or >= 0xFE0E and <= 0xFE0F
                or 0x200D;
            if (!emoji) limpio.Append(rune.ToString());
        }
        return limpio.ToString().Trim();
    }

    private static string TextoParaChat(string texto)
    {
        texto = TextoSinEmojis(texto).Replace("\r\n", "\n").Replace('\r', '\n');
        var lineas = new List<string>();
        bool lineaVacia = false;
        foreach (string original in texto.Split('\n'))
        {
            string linea = Regex.Replace(original, @"^\s{0,3}#{1,6}\s*", "");
            linea = Regex.Replace(linea, @"^\s*[-*+]\s+", "• ");
            linea = Regex.Replace(linea, @"\[([^\]]+)\]\([^)]*\)", "$1");
            linea = linea.Replace("**", "").Replace("__", "").Replace("`", "");
            linea = Regex.Replace(linea, @"(?<!\*)\*([^*]+)\*", "$1").TrimEnd();
            if (linea.Length == 0)
            {
                if (lineaVacia) continue;
                lineaVacia = true;
            }
            else lineaVacia = false;
            lineas.Add(linea);
        }
        return string.Join("\n", lineas).Trim();
    }

    private static Thickness MargenDelMensaje(bool esDeU) =>
        esDeU ? new Thickness(0, 6, 44, 6) : new Thickness(44, 6, 0, 6);

    private void PintarChat()
    {
        _listaChat.Children.Clear();
        foreach (var (esDeU, texto) in _mensajes)
        {
            var linea = new Border
            {
                Background = new SolidColorBrush(esDeU
                    ? Color.FromArgb(38, 255, 255, 255)
                    : Color.FromArgb(24, 255, 255, 255)),
                CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 8, 12, 8),
                Margin = MargenDelMensaje(esDeU),
                HorizontalAlignment = esDeU ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                MaxWidth = 304,
                Child = new TextBlock
                {
                    Text = texto, TextWrapping = TextWrapping.Wrap,
                    Foreground = Pincel(PaletaDelNotch.Tinta), FontFamily = Letra,
                    FontSize = 14, FontWeight = FontWeights.Normal, LineHeight = 20,
                    LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                },
            };
            _listaChat.Children.Add(linea);
        }
        Dispatcher.BeginInvoke(new Action(BajarScrollChat), DispatcherPriority.Loaded);
    }

    private void BajarScrollChat()
    {
        _scrollChat.UpdateLayout();
        double destino = Math.Max(0, _scrollChat.ExtentHeight - _scrollChat.ViewportHeight);
        _scrollChatTimer.Stop();
        if (destino <= _scrollChat.VerticalOffset + 1) { _scrollChat.ScrollToEnd(); return; }
        _scrollChat.BeginAnimation(OpacityProperty, null);
        _scrollChatTimer.Start();
    }

    private void MoverScrollChat()
    {
        double destino = Math.Max(0, _scrollChat.ExtentHeight - _scrollChat.ViewportHeight);
        double siguiente = _scrollChat.VerticalOffset + (destino - _scrollChat.VerticalOffset) * 0.24;
        _scrollChat.ScrollToVerticalOffset(siguiente);
        if (Math.Abs(destino - siguiente) < 0.5) { _scrollChat.ScrollToVerticalOffset(destino); _scrollChatTimer.Stop(); }
    }

    private void EnfocarEntrada()
    {
        Activate();
        _entradaChat.Focus();
        _entradaChat.CaretIndex = _entradaChat.Text.Length;
    }

    /// <summary>Se acabó el turno: lo que pidió la persona pasa a ser la tarea persistente.</summary>
    public void CierraTurno()
    {
        _dice.CierraTurno();
        Pintar();
    }

    /// <summary>La persona lo paró a mano: se queda dicho, en vez de desaparecer sin más (promesa 259,
    /// pedido del dueño 2026-09-17: «que una vez yo detuve la conversación… que se guarde en notch»).</summary>
    public void Detenido(string texto)
    {
        _dice.Detenido(texto);
        Pintar();
    }

    /// <summary>
    /// Un texto que cambia de golpe se lee como un sobresalto; uno que entra con un parpadeo breve
    /// se lee como parte del mismo lenguaje que ya tiene el notch al aparecer (pedido del dueño,
    /// 2026-09-17: transiciones «más suaves… con el mismo feeling que todo el diseño»). Se llama
    /// SOLO en los cambios de verdad —abajo, en <see cref="Pintar"/>, guardado tras las mismas
    /// comparaciones que ya decidían si algo cambió—, nunca palabra a palabra mientras se sigue
    /// hablando: eso convertiría el parpadeo en el problema que se quería arreglar.
    /// </summary>
    private static void Restaña(TextBlock t)
    {
        t.BeginAnimation(OpacityProperty, null);
        t.Opacity = 0.18;
        t.BeginAnimation(OpacityProperty, new DoubleAnimation(0.18, 1, TimeSpan.FromMilliseconds(220))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void PrepararMarquesina()
    {
        _marquesina.Stop();
        _textoX = 0;
        _desplazamientoDelTexto.X = 0;
        _excesoDeTexto = 0;
        _direccionMarquesina = -1;
        _pausaMarquesinaHasta = DateTime.UtcNow.AddMilliseconds(900);

        Dispatcher.BeginInvoke(new Action(() =>
        {
            _texto.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double anchoVisible = _ventanaDelTexto.ActualWidth > 0
                ? _ventanaDelTexto.ActualWidth
                : MedidaDelNotch.AnchoDelTexto;
            _excesoDeTexto = Math.Max(0, _texto.DesiredSize.Width - anchoVisible);
            _texto.HorizontalAlignment = _excesoDeTexto > 1 ? HorizontalAlignment.Left : HorizontalAlignment.Center;
            _texto.TextAlignment = _excesoDeTexto > 1 ? TextAlignment.Left : TextAlignment.Center;
            if (_excesoDeTexto > 1) _marquesina.Start();
        }), DispatcherPriority.Loaded);
    }

    private void MoverMarquesina()
    {
        if (_excesoDeTexto <= 1 || DateTime.UtcNow < _pausaMarquesinaHasta) return;

        _textoX += _direccionMarquesina * 0.42;
        if (_textoX <= -_excesoDeTexto)
        {
            _textoX = -_excesoDeTexto;
            _direccionMarquesina = 1;
            _pausaMarquesinaHasta = DateTime.UtcNow.AddMilliseconds(900);
        }
        else if (_textoX >= 0)
        {
            _textoX = 0;
            _direccionMarquesina = -1;
            _pausaMarquesinaHasta = DateTime.UtcNow.AddMilliseconds(1300);
        }

        _desplazamientoDelTexto.X = _textoX;
    }

    /// <summary>Lo que hay que enseñar, enseñado en los propios controles. NO decide si se ve: eso es
    /// cosa de <see cref="Pintar"/>, que llama a esto y además trae la pieza a pantalla.</summary>
    private void PintarContenido()
    {
        if (_texto.Text != _dice.Texto)
        {
            _texto.Text = _dice.Texto;
            PrepararMarquesina();
        }

        if (_pintado != _dice.Estado)
        {
            _pintado = _dice.Estado;
            _icono.Data = Geometry.Parse(IconosDelNotch.De(_dice.Estado));
            _giro.BeginAnimation(RotateTransform.AngleProperty, null);
            _giro.Angle = 0;
            if (IconosDelNotch.Gira(_dice.Estado))
                _giro.BeginAnimation(RotateTransform.AngleProperty,
                    new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(1100))
                    { RepeatBehavior = RepeatBehavior.Forever });
            Restaña(_texto);
        }

        // A PARTIR DE AQUÍ HAY ALGO DE VERDAD QUE DECIR: si el notch estaba a la vista solo porque el
        // cursor rondaba el borde (promesa 260), deja de estarlo — ya no hay que retirarlo cuando el
        // cursor se aleje, porque ahora lo sostiene el contenido, con su propia caducidad.
        _asomadoSoloPorHover = false;
    }

    /// <summary>Pinta el contenido Y trae la pieza a pantalla si hacía falta. Esto es lo que llaman
    /// Habla/Empieza/Termina/CierraTurno/Detenido: eventos de verdad, que sí justifican asomarse.</summary>
    private void Pintar()
    {
        PintarContenido();
        if (!IsVisible) Aparecer();
        _caducar.Start();
        Toca();
    }

    private void Aparecer()
    {
        Show();
        _entrada.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _entrada.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _baja.BeginAnimation(TranslateTransform.YProperty, null);
        BeginAnimation(OpacityProperty, null);

        // CAE Y SE ASIENTA CON UN PELÍN DE REBOTE, en vez de solo desvanecerse en su sitio (pedido
        // del dueño, 2026-09-17: «sorpréndeme con su transición»). El rebote es pequeño —Amplitude
        // 0.35— porque uno grande en una pieza de trabajo se lee como un juguete, no como el sistema.
        var rebote = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };
        var dur = TimeSpan.FromMilliseconds(380);
        _entrada.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.9, 1, dur) { EasingFunction = rebote });
        _entrada.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.9, 1, dur) { EasingFunction = rebote });
        _baja.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-14, 0, dur) { EasingFunction = rebote });
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
    }

    public void Limpiar()
    {
        _caducar.Stop();

        var sale = new CubicEase { EasingMode = EasingMode.EaseIn };
        var dur = TimeSpan.FromMilliseconds(200);
        // SE RETIRA HACIA ARRIBA MIENTRAS SE VA, en espejo de cómo llega: entrar cayendo y salir
        // desvaneciéndose en el sitio se sentía como dos piezas distintas.
        _baja.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -10, dur) { EasingFunction = sale });
        var irse = new DoubleAnimation(0, dur) { EasingFunction = sale };
        irse.Completed += (_, __) =>
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            _baja.BeginAnimation(TranslateTransform.YProperty, null);
            _baja.Y = 0;
            _dice.Olvida();
            _texto.Text = _dice.Texto;
            PrepararMarquesina();
            _textoX = 0;
            _desplazamientoDelTexto.X = 0;
            _marquesina.Stop();
            Hide();
        };
        BeginAnimation(OpacityProperty, irse);
    }

    private void Toca()
    {
        _ultimoCambio = DateTime.UtcNow;
        Recolocar();
    }

    /// <summary>
    /// ARRIBA Y AL CENTRO (promesa 251).
    ///
    /// SE COLOCA LA PIEZA QUE SE VE, NO LA VENTANA. Es la lección de la promesa 159, y aquí se pagó
    /// igual: la ventana lleva dentro el hueco que la sombra necesita, así que pedir la junta contra
    /// el borde de la VENTANA dejaba el notch flotando a media distancia. A la regla se le pregunta
    /// por el rectángulo visible y después se descuenta el hueco.
    /// </summary>
    private void Recolocar()
    {
        var holgura = Estudio.HolguraDe(Sombra);
        var (libre, _) = LaBarraDeTareas.Mirar();
        var sitio = ReglaDeLaBandeja.ArribaAlCentro(libre, new Size(_notch.ActualWidth, _notch.ActualHeight));
        Left = sitio.Left - holgura.Left;
        Top = sitio.Top - holgura.Top;
    }
}
