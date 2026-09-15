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
/// EL NOTCH: la lista de lo que va pasando, apoyada en la barra de tareas. Cada acción aparece EN
/// CUANTO empieza y se resuelve donde estaba cuando termina; lo dicho y lo hecho, en la misma
/// columna.
///
/// Por qué hacía falta algo nuevo, habiendo tanto pintado ya: lo que existía o se sobrescribía o no
/// se veía. La burbuja de la carita usa <c>Narrate</c>, que REEMPLAZA, así que solo se ve el último
/// paso y los anteriores desaparecen; el chip dice «Trabajando…» sin decir en qué; el log lo tiene
/// todo pero hay que abrirlo y leerlo. Con una tarea de un solo paso da igual, pero en cuanto son
/// cuatro —«ve a descargas», «no, mejor documentos», «busca el informe»— la persona quiere ver la
/// secuencia, no el último fotograma.
///
/// TRES ESTADOS, NO DOS. Una acción que no se llegó a ejecutar tiene que dejar rastro: contar solo
/// los éxitos y los fallos fue exactamente lo que hizo que una corrida que se saltó 19 pasos
/// informara «29 de 30» (aprendizaje nº10 del CLAUDE.md). Aquí se pintan en curso, hecho, falló y
/// omitido, cada uno con su color de <see cref="UiPalette"/>.
///
/// Es click-through y sin foco, como el resto de capas: se mira, no se toca.
/// </summary>
/// <remarks>
/// POR QUÉ SE PARECE A UNA ISLA DINÁMICA (rediseño del 2026-09-14, pedido por el dueño mirando la
/// aplicación ya instalable). Lo de antes era una caja gris con un borde de un píxel plantada en la
/// esquina, y no es que fuera fea: es que se leía como una ventana AJENA encima del escritorio. Las
/// tres cosas que la convierten en parte del sistema son las mismas que usan el notch de Omi y la
/// isla de Apple, y ninguna es un adorno:
///
///   · <b>Se apoya en la barra de tareas y en su hueco libre.</b> Una capa que nace del borde de
///     algo del sistema se lee como del sistema. Ver <see cref="ReglaDeLaBandeja"/>, que es donde
///     vive el criterio — incluida la parte que importa de verdad: en un Windows 11 de fábrica los
///     iconos están AL CENTRO, así que la esquina donde esto vivía clavado es justo la ocupada.
///   · <b>Negro de verdad, sin filete duro.</b> El borde de un píxel es lo que dibuja el contorno de
///     «ventanita»; el relieve lo tiene que dar la sombra y un hilo de luz interior, que es lo que
///     hace que una pieza negra sobre un escritorio cualquiera parezca un objeto y no un recorte.
///   · <b>Entra y se va con un gesto, no aparece de golpe.</b> Cada fila sube 8 px mientras aparece,
///     y el panel entero hace lo mismo al nacer. Una capa que se materializa sin transición se lee
///     como un fallo de repintado; la misma capa con 200 ms de entrada se lee como algo que llegó.
///
/// LO VIEJO SE DESVANECE EN VEZ DE IRSE. Las filas anteriores bajan de opacidad según su edad, con
/// un suelo para que sigan siendo legibles. Es la jerarquía que hacía falta y no había: antes las
/// diez filas gritaban lo mismo, así que para encontrar lo último había que leerlas todas.
/// </remarks>
public sealed class PanelDeAcciones : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    /// <summary>Cuántas líneas se conservan a la vista. Suficiente para ver la secuencia de una
    /// conversación sin convertirse en un log: para eso ya está el log. Sube de 7 a 10 desde que
    /// aquí también va lo dicho: si la conversación y la maquinaria se reparten las mismas filas,
    /// dos herramientas seguidas te borran la pregunta que las provocó.</summary>
    private const int Memoria = 10;

    /// <summary>
    /// Cuánto aguanta en pantalla sin nada nuevo.
    ///
    /// Eran 20 s cuando esto solo enseñaba maquinaria —cuando deja de pasar algo, sobra—. Ahora
    /// también lleva la conversación, y una pausa de veinte segundos pensando qué pedir no es que
    /// sobre: es parte de hablar. Se sube a 90, que sigue siendo «se va solo» y no «hay que
    /// cerrarlo».
    /// </summary>
    private static readonly TimeSpan Caducidad = TimeSpan.FromSeconds(90);

    /// <summary>El radio máximo del notch. Con una sola fila manda la mitad del alto: una píldora.</summary>
    private const double RadioMaximo = 20;

    /// <summary>Lo más ancho que puede ponerse el texto de una fila antes de cortarse con puntos.</summary>
    private const double AnchoDelTexto = 420;

    /// <summary>
    /// La sombra del notch: NEGRA, y no la azulada de <see cref="Estudio.Sombra3"/>.
    ///
    /// La escala del estudio está calibrada para lo elevado SOBRE BLANCO, donde un gris azulado da
    /// profundidad y el negro puro ensucia. Aquí el suelo es el escritorio de otra persona —puede
    /// ser un SAP azul claro, un Word blanco o una foto— y lo que tiene que separar la pieza no es
    /// un tono, es oscuridad. Es la misma excepción declarada que ya tiene la rampa flotante de
    /// Estudio: misma regla (la profundidad la hace la sombra), distinto suelo.
    /// </summary>
    private static readonly DropShadowEffect Sombra = Sombreado();

    private static DropShadowEffect Sombreado()
    {
        var s = new DropShadowEffect
        {
            BlurRadius = 36,
            ShadowDepth = 8,
            Direction = 270,
            Opacity = 0.45,
            Color = Colors.Black,
            RenderingBias = RenderingBias.Quality,
        };
        s.Freeze();
        return s;
    }

    public enum Estado { EnCurso, Hecho, Fallo, Omitido }

    private readonly StackPanel _filas = new();

    /// <summary>Las filas vivas, en el mismo orden que sus cajas dentro de <see cref="_filas"/>.
    /// Se lleva aparte porque quien decide la opacidad por edad necesita hablar con la FILA, no con
    /// el <c>Border</c> que la dibuja.</summary>
    private readonly List<Fila> _lista = new();
    private readonly Border _placa;      // la gemela sin hijos: la única que lleva el Effect
    private readonly Border _notch;      // la pieza que se ve, con el contenido dentro
    private readonly ScaleTransform _entrada = new(1, 1);
    private readonly DispatcherTimer _caducar;
    private DateTime _ultimoCambio = DateTime.UtcNow;

    /// <summary>La fila que está en curso, para resolverla en su sitio en vez de añadir otra.</summary>
    private Fila? _enCurso;

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
        // dentro cada letra pierde el ClearType y sale lavada. Es la regla de Estudio.Elevar, dicha
        // aquí a mano porque este árbol se construye en código.
        _placa = new Border { Background = Brushes.Black, Effect = Sombra };

        _notch = new Border
        {
            // Negro con una gota de azul, no negro plano: el negro absoluto sobre un escritorio
            // claro se lee como un agujero. Y casi opaco, para que lo de debajo se intuya sin que
            // el texto tenga que competir contra ello.
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x0A, 0x0C, 0x12)),
            // EL FILETE ES UN HILO DE LUZ, no un contorno. A 0x24 de alfa no se ve como una línea:
            // se ve como el canto de algo iluminado desde arriba, que es lo que despega la pieza del
            // fondo sin dibujarle un marco de ventana alrededor.
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 11, 18, 11),
            Child = _filas,
        };

        var caja = new Grid
        {
            // El hueco para que el desenfoque quepa dentro del cristal: la ventana es SizeToContent
            // sobre AllowsTransparency, así que mide exactamente lo que mide el contenido y sin
            // reservar sitio la sombra sale cortada contra el borde.
            Margin = Estudio.HolguraDe(Sombra),
            RenderTransformOrigin = new Point(0.5, 1),
            RenderTransform = _entrada,
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
            if (_enCurso == null && DateTime.UtcNow - _ultimoCambio > Caducidad) Limpiar();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var h = new WindowInteropHelper(this).Handle;
        SetWindowLong(h, GWL_EXSTYLE,
            GetWindowLong(h, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW);
    }

    /// <summary>
    /// Empieza una acción: se pinta YA, en azul de trabajo, antes de que se sepa cómo va a acabar.
    /// Ese instante es el que da la sensación de tiempo real; esperar al resultado para contar algo
    /// es justo lo que hacía que pareciera colgada.
    /// </summary>
    public void Empieza(string texto)
    {
        // Si la anterior nunca se resolvió, no se borra: se marca omitida. Una acción sin desenlace
        // que desaparece en silencio es la que hace que el recuento mienta.
        if (_enCurso != null) Resolver(Estado.Omitido, "sin respuesta");

        _enCurso = Fila.DePunto(UiPalette.Trabajando, texto, latiendo: true);
        Anadir(_enCurso);
    }

    /// <summary>Cierra la que estaba en curso con su desenlace.</summary>
    public void Termina(string texto, bool ok) => Resolver(ok ? Estado.Hecho : Estado.Fallo, texto);

    /// <summary>La frase que se está diciendo ahora mismo: la tuya y la de Ü, cada una en su fila.</summary>
    private Fila? _loQueDigo, _loQueDiceU;

    /// <summary>
    /// LO QUE SE OYE Y LO QUE SE CONTESTA, EN EL MISMO SITIO QUE LO QUE SE HACE.
    /// </summary>
    /// <remarks>
    /// La conversación se veía en la burbuja y la maquinaria aquí, así que para saber si te entendió
    /// había que mirar a dos sitios a la vez — y la burbuja REEMPLAZA, así que lo que dijiste hace
    /// dos frases ya no estaba. Cuando algo no funciona, la primera pregunta es «¿me oyó bien?», y
    /// no había forma de contestarla mirando (2026-08-16, lo pidió el usuario).
    ///
    /// Se ACTUALIZA LA FILA en vez de añadir una por trozo: la transcripción llega palabra a palabra
    /// y una fila por trozo convierte una frase en una columna de palabras sueltas. Lo que se busca
    /// es verla escribirse, que es lo que dice que te está oyendo AHORA.
    /// </remarks>
    public void Habla(string texto, bool esDeU)
    {
        var fila = esDeU ? _loQueDiceU : _loQueDigo;

        // Si su fila ya se fue por arriba —solo caben unas pocas— se empieza otra: actualizar una
        // fila que ya no está en pantalla es escribir donde nadie mira.
        if (fila != null && !_lista.Contains(fila)) fila = null;

        if (fila == null)
        {
            fila = Fila.DeVoz(esDeU ? "Ü" : "Tú", esDeU ? UiPalette.Vivo : UiPalette.Trabajando, texto);
            if (esDeU) _loQueDiceU = fila; else _loQueDigo = fila;
            Anadir(fila);
            return;
        }

        fila.Texto.Text = texto;
        Toca();
    }

    /// <summary>Se acabó el turno: lo dicho queda fijo y la siguiente frase empieza fila nueva.</summary>
    public void CierraTurno()
    {
        _loQueDigo = null;
        _loQueDiceU = null;
    }

    private void Resolver(Estado estado, string texto)
    {
        if (_enCurso == null) { Empieza(texto); }
        var fila = _enCurso;
        if (fila == null) return;
        _enCurso = null;

        var color = estado switch
        {
            Estado.Hecho => UiPalette.Vivo,
            Estado.Fallo => UiPalette.Fallo,
            Estado.Omitido => UiPalette.Inactivo,
            _ => UiPalette.Trabajando,
        };

        fila.Resolver(color, texto, apagada: estado == Estado.Omitido);
        Toca();
    }

    private void Anadir(Fila fila)
    {
        _lista.Add(fila);
        _filas.Children.Add(fila.Caja);
        while (_lista.Count > Memoria) { _lista.RemoveAt(0); _filas.Children.RemoveAt(0); }
        Envejecer();
        Toca();
        if (!IsVisible) Aparecer();
        fila.Entrar();
        if (!_caducar.IsEnabled) _caducar.Start();
    }

    /// <summary>
    /// LO ÚLTIMO ES LO QUE SE LEE. Cada fila baja de opacidad según lo lejos que esté del final, con
    /// un suelo de 0,42 para que lo viejo siga siendo legible si alguien va a buscarlo. Sin esto las
    /// diez filas pesan igual y encontrar la de ahora mismo cuesta leerlas todas.
    /// </summary>
    private void Envejecer()
    {
        // Se ATENÚA CON ANIMACIÓN y no asignando la propiedad, y no es estética: una fila recién
        // entrada tiene una animación viva sobre su opacidad, y en WPF un valor local puesto a mano
        // NO se ve mientras esa animación corra. Asignando, las filas se quedaban todas a 1 y este
        // método no hacía nada visible — la clase de fallo que no da error y solo se nota mirando.
        for (int i = 0; i < _lista.Count; i++)
            if (i < _lista.Count - 1)   // la última entra sola, con su propia animación
                _lista[i].Atenuar(Math.Max(0.42, 1.0 - (_lista.Count - 1 - i) * 0.13));
    }

    private void Toca()
    {
        _ultimoCambio = DateTime.UtcNow;
        Recolocar();
    }

    /// <summary>
    /// Nace con un gesto: sube 10 px y crece desde el 96 % en 220 ms, anclado al borde de abajo —que
    /// es el que toca la barra de tareas—. Aparecer de golpe se lee como un fallo de repintado.
    /// </summary>
    private void Aparecer()
    {
        Show();
        var suave = new CubicEase { EasingMode = EasingMode.EaseOut };
        var dur = TimeSpan.FromMilliseconds(220);
        _entrada.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, 1, dur) { EasingFunction = suave });
        _entrada.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, 1, dur) { EasingFunction = suave });
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
    }

    public void Limpiar()
    {
        _caducar.Stop();
        _enCurso = null;
        _loQueDigo = null;
        _loQueDiceU = null;

        // Se va como vino: 180 ms de desvanecido y entonces se esconde. Y la opacidad se devuelve a 1
        // al terminar, o el siguiente Show() enseñaría una ventana invisible.
        var irse = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        irse.Completed += (_, __) =>
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            _lista.Clear();
            _filas.Children.Clear();
            Hide();
        };
        BeginAnimation(OpacityProperty, irse);
    }

    /// <summary>
    /// Apoyado en la barra de tareas y en el hueco que ella deja libre. El criterio entero vive en
    /// <see cref="ReglaDeLaBandeja"/>; aquí solo se mide el panel y se pregunta.
    /// </summary>
    private void Recolocar()
    {
        // Las puntas: con una sola fila es una píldora, y a partir de tres filas se queda en el radio
        // de panel. Se calcula en vez de escribirse para que no haya que volver aquí cada vez que
        // cambie el alto de una fila.
        double radio = Math.Min(RadioMaximo, Math.Max(12, _notch.ActualHeight / 2));
        _notch.CornerRadius = new CornerRadius(radio);
        _placa.CornerRadius = new CornerRadius(radio);

        // SE COLOCA LA PIEZA QUE SE VE, NO LA VENTANA. Es la misma lección de la promesa 159, y aquí
        // se pagó igual (2026-09-14, medido sobre la pantalla): la ventana es SizeToContent y lleva
        // dentro el hueco que la sombra necesita —18 px por los lados, 26 por abajo—, así que pedir
        // los 8 px de junta contra el borde de la VENTANA dejaba el notch a 34 px de la barra de
        // tareas, flotando a media altura en vez de apoyado en ella. A la regla se le pregunta por el
        // rectángulo visible y después se descuenta el hueco.
        var holgura = Estudio.HolguraDe(Sombra);
        var (libre, lado, iconos) = LaBarraDeTareas.Mirar();
        var sitio = ReglaDeLaBandeja.Sitio(libre, lado, iconos,
            new Size(_notch.ActualWidth, _notch.ActualHeight));
        Left = sitio.Left - holgura.Left;
        Top = sitio.Top - holgura.Top;
    }

    /// <summary>
    /// Una línea del notch: la marca de la izquierda —un punto de estado o el nombre de quien
    /// habla— y el texto.
    /// </summary>
    /// <remarks>
    /// Existe como clase y no como un <c>Border</c> suelto porque lo de antes resolvía las filas
    /// hurgando en el árbol visual (<c>fila.Child is StackPanel sp &amp;&amp; sp.Children.Count == 2</c>):
    /// cualquier retoque del dibujo —añadir un icono, envolver algo en un Grid— rompía en silencio
    /// el resolver y las acciones se quedaban para siempre en azul. Con las piezas guardadas por
    /// nombre, el dibujo se puede cambiar sin que nada deje de funcionar.
    /// </remarks>
    private sealed class Fila
    {
        /// <summary>
        /// La letra del notch. «Segoe UI Variable Text» es la de Windows 11 y está dibujada para
        /// tamaños de interfaz; con la coma detrás, WPF cae a la de siempre en un Windows 10 sin
        /// que haya que preguntar qué sistema es.
        /// </summary>
        private static readonly FontFamily Letra = new("Segoe UI Variable Text, Segoe UI");

        public Border Caja { get; }
        public TextBlock Texto { get; }
        private readonly TranslateTransform _sube = new();
        private readonly Ellipse? _punto;

        private Fila(UIElement marca, string texto, Ellipse? punto)
        {
            _punto = punto;

            var rejilla = new Grid();
            // Columna fija para la marca: así todos los textos arrancan en la misma x, se llame la
            // marca «Ü», «Tú» o sea un punto de 7 px. Sin ella, cada fila empezaba donde acabara su
            // glifo y la columna de texto quedaba dentada.
            rejilla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            rejilla.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Grid.SetColumn(marca, 0);
            rejilla.Children.Add(marca);

            Texto = new TextBlock
            {
                Text = texto,
                Foreground = new SolidColorBrush(Color.FromArgb(0xF0, 0xEA, 0xF2, 0xFF)),
                FontFamily = Letra,
                FontSize = 12.5,
                MaxWidth = AnchoDelTexto,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(Texto, 1);
            rejilla.Children.Add(Texto);

            Caja = new Border
            {
                Padding = new Thickness(0, 3, 0, 3),
                Child = rejilla,
                Opacity = 0,                 // la pone Entrar; nacer visible se salta la animación
                RenderTransform = _sube,
            };
        }

        /// <summary>Una acción de la maquinaria: un punto de color y lo que está haciendo.</summary>
        public static Fila DePunto(Color color, string texto, bool latiendo)
        {
            var punto = new Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = new SolidColorBrush(color),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                // El halo del color del propio punto: a 7 px un color plano no se lee, y con el
                // resplandor el estado se ve de reojo sin tener que enfocar la vista.
                Effect = new DropShadowEffect
                {
                    BlurRadius = 10, ShadowDepth = 0, Opacity = 0.9, Color = color,
                },
            };
            var fila = new Fila(punto, texto, punto);
            if (latiendo) fila.Latir();
            return fila;
        }

        /// <summary>Una frase: quién la dijo y qué dijo.</summary>
        /// <remarks>
        /// QUIÉN HABLA SE DICE CON UNA ETIQUETA, NO CON UN EMOJI. El 🗣 de antes tenía el mismo peso
        /// que el punto de una acción, así que a simple vista las filas de la conversación y las de
        /// la maquinaria eran la misma lista. «Tú» y «Ü» separan las dos voces por lectura, que es
        /// como se separan en un chat.
        /// </remarks>
        public static Fila DeVoz(string quien, Color color, string texto) => new(new TextBlock
        {
            Text = quien,
            Foreground = new SolidColorBrush(color),
            FontFamily = Letra,
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
        }, texto, punto: null);

        /// <summary>Sube 8 px mientras aparece. Es lo que hace que se lea como recién llegada.</summary>
        public void Entrar()
        {
            _sube.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(200))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            Caja.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
        }

        /// <summary>Se aparta: baja a la opacidad que le toca por edad, sin saltos.</summary>
        public void Atenuar(double opacidad) => Caja.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(opacidad, TimeSpan.FromMilliseconds(160)));

        /// <summary>Mientras la acción está en curso, el punto respira. Es lo que dice «sigo aquí».</summary>
        private void Latir() => _punto?.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(900))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });

        /// <summary>La acción terminó: el punto deja de latir, se tiñe del desenlace y el texto cambia.</summary>
        public void Resolver(Color color, string texto, bool apagada)
        {
            if (_punto != null)
            {
                _punto.BeginAnimation(UIElement.OpacityProperty, null);
                _punto.Opacity = apagada ? 0.5 : 1;
                _punto.Fill = new SolidColorBrush(color);
                if (_punto.Effect is DropShadowEffect halo) halo.Color = color;
            }
            Texto.Text = texto;
            Texto.Foreground = new SolidColorBrush(
                Color.FromArgb(apagada ? (byte)0x80 : (byte)0xF0, 0xEA, 0xF2, 0xFF));
        }
    }
}
