using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace U.WindowsClient.Ui;

/// <summary>
/// EL NOTCH: arriba al centro, una pieza de dos líneas que dice EN QUÉ ESTÁ Ü.
///
/// Arriba, la tarea: lo último que pidió la persona, que se queda hasta que pida otra cosa. Abajo,
/// lo que está pasando ahora mismo: el paso que Ü da, su desenlace, o lo que la persona está
/// diciendo mientras lo dice. Y a la izquierda, un icono que dice de qué tipo es esa segunda línea.
/// </summary>
/// <remarks>
/// POR QUÉ DEJÓ DE SER UNA LISTA (spec 028, 2026-09-16). Nació como tres líneas del mismo peso y
/// servía para ver una secuencia; el dueño pidió otra cosa: «que en el título esté la MACRO TAREA, y
/// donde dice paso en ejecución, lo que el modelo va haciendo». Una lista contesta «qué ha pasado»;
/// dos líneas con dos pesos distintos contestan «en qué estás», que es lo que se mira de reojo
/// mientras trabajas.
///
/// Qué va en cada línea lo decide <see cref="LoQueDiceElNotch"/>, que es pura y está bajo contrato
/// (promesa 252). Aquí solo se pinta.
///
/// MIDE SIEMPRE LO MISMO (promesa 249): el alto y el ancho son fijos, así que ni una frase larga la
/// ensancha ni una línea nueva la estira. Una pieza que cambia de tamaño encima del trabajo de
/// alguien se lee como un sobresalto.
/// </remarks>
public sealed class PanelDeAcciones : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80;
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

    /// <summary>Qué toca decir en cada línea. La decisión vive aparte y bajo contrato (promesa 252).</summary>
    private readonly LoQueDiceElNotch _dice = new();

    private readonly Path _icono;
    private readonly RotateTransform _giro = new();
    private readonly TextBlock _tarea;
    private readonly TextBlock _paso;
    private readonly Border _placa;      // la gemela sin hijos: la única que lleva el Effect
    private readonly Border _notch;      // la pieza que se ve, con el contenido dentro
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
    private bool _dentroDeLaZona;
    private bool _asomadoSoloPorHover;

    public PanelDeAcciones()
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

        _tarea = new TextBlock
        {
            Foreground = Pincel(PaletaDelNotch.Tinta),
            FontFamily = Letra,
            FontSize = MedidaDelNotch.LetraDeLaTarea,
            FontWeight = FontWeights.SemiBold,
            MaxWidth = MedidaDelNotch.AnchoDelTexto,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Text = _dice.Tarea,
        };

        _paso = new TextBlock
        {
            Foreground = Pincel(PaletaDelNotch.TintaSecundaria),
            FontFamily = Letra,
            FontSize = MedidaDelNotch.LetraDelPaso,
            MaxWidth = MedidaDelNotch.AnchoDelTexto,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 1, 0, 0),
        };

        var dosLineas = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        dosLineas.Children.Add(_tarea);
        dosLineas.Children.Add(_paso);

        var rejilla = new Grid();
        rejilla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(MedidaDelNotch.CajaDelIcono) });
        rejilla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(MedidaDelNotch.AireDelIcono) });
        rejilla.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(cajaDelIcono, 0);
        Grid.SetColumn(dosLineas, 2);
        rejilla.Children.Add(cajaDelIcono);
        rejilla.Children.Add(dosLineas);

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
            Child = rejilla,
        };

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
        // adelanta. Ver SiempreDelante.
        this.Vigilar();

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
        Closed += (_, __) => _asomo.Stop();

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
        bool dentro = ReglaDeLaBandeja.Asoma(libre, new Size(MedidaDelNotch.Ancho, MedidaDelNotch.Alto), cursor);

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
        _dentroDeLaZona = dentro;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var h = new WindowInteropHelper(this).Handle;
        SetWindowLong(h, GWL_EXSTYLE,
            GetWindowLong(h, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW);
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
        if (esDeU) _dice.UDice(texto); else _dice.PersonaDice(texto);
        Pintar();
    }

    /// <summary>Se acabó el turno: lo que pidió la persona pasa a ser la tarea de arriba.</summary>
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

    /// <summary>Lo que hay que enseñar, enseñado en los propios controles. NO decide si se ve: eso es
    /// cosa de <see cref="Pintar"/>, que llama a esto y además trae la pieza a pantalla.</summary>
    private void PintarContenido()
    {
        if (_tarea.Text != _dice.Tarea) { _tarea.Text = _dice.Tarea; Restaña(_tarea); }
        _paso.Text = _dice.Paso;
        _paso.Visibility = _dice.Paso.Length > 0 ? Visibility.Visible : Visibility.Hidden;

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
            Restaña(_paso);
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
            _tarea.Text = _dice.Tarea;
            _paso.Text = "";
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
