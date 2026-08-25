using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Ui;

/// <summary>
/// LO QUE SE APRENDIÓ, ESCRITO SOBRE EL ELEMENTO Y CORREGIBLE AHÍ MISMO.
/// </summary>
/// <remarks>
/// El recuadro dice CUÁL, y esto dice QUÉ. Iluminar sin el texto obliga a escuchar la narración para
/// saber qué se aprendió de ese elemento, y quien está revisando lo aprendido no siempre tiene la
/// voz puesta (2026-08-24, pedido por el usuario).
///
/// SE CORRIGE DONDE SE LEE. Un recuerdo mal cogido —una frase que el modelo resumió de más, un
/// matiz que faltaba— se descubre justo al verlo escrito encima de su elemento. Mandar a la persona
/// a otro sitio a arreglarlo, o a dictárselo otra vez de viva voz, es perder el momento en que se
/// dio cuenta.
///
/// UNA VENTANA POR TARJETA, y no una sola a pantalla completa: una ventana grande e interactiva se
/// come todos los clics de la pantalla, incluidos los que no son para ella. Así, cada tarjeta ocupa
/// solo lo suyo y el resto del escritorio sigue siendo del usuario.
/// </remarks>
public static class TarjetasDeRecuerdo
{
    /// <summary>Quién guarda una corrección: (selector, texto nuevo) → si se pudo.</summary>
    public static Func<string, string, bool>? Guardar { get; set; }

    private static readonly List<TarjetaDeRecuerdo> Abiertas = new();

    /// <summary>
    /// Alguien está escribiendo una corrección AHORA MISMO. Lo consulta el narrador para no pasar al
    /// siguiente recuerdo mientras tanto: cambiar de recuerdo con alguien a media frase le borraría
    /// lo que está escribiendo y le movería el foco (2026-08-24, pedido por el usuario).
    /// </summary>
    /// <remarks>
    /// ES UN BOOLEANO SUELTO Y NO UNA PREGUNTA A LAS TARJETAS, y esa es toda la diferencia entre
    /// funcionar y no: preguntarles obligaba a leer <c>IsFocused</c> y <c>Text</c>, que son del hilo
    /// de la interfaz, y quien pregunta es el narrador —que llega por el servidor de red, en otro
    /// hilo—. Reventaba con «no se puede tener acceso a este objeto porque lo posee otro
    /// subproceso», y el asistente lo contaba en voz alta: «el sistema lanzó un error, la
    /// herramienta no pudo acceder a un objeto desde otro subproceso» (2026-08-24, oído por el
    /// usuario). Lo escribe la interfaz cuando cambia; lo lee quien quiera, desde donde sea.
    /// </remarks>
    public static bool EscribiendoAlguna => _escribiendo;

    private static volatile bool _escribiendo;

    /// <summary>Lo llaman las tarjetas, siempre desde el hilo de la interfaz.</summary>
    internal static void RecalcularEscribiendo()
        => _escribiendo = Abiertas.Any(t => t.Escribiendo);

    /// <summary>
    /// AL HILO DE LA INTERFAZ, SIEMPRE. Una ventana de WPF solo puede nacer en un hilo STA, y quien
    /// enseña estas tarjetas casi nunca está en él: las herramientas del asistente llegan por el
    /// servidor de red, en un hilo cualquiera. Sin esto, la tarjeta no aparecía y el único rastro
    /// era una línea en el log —«el subproceso de llamada debe ser STA»— mientras el recuadro sí se
    /// encendía, porque ese sí pasaba por el Dispatcher (2026-08-24, cazado al mirar qué ventanas
    /// tenía abiertas el proceso: solo tres, ninguna era la tarjeta).
    ///
    /// Se resuelve AQUÍ y no en cada llamador: quien quiere enseñar una nota no tiene por qué saber
    /// en qué hilo está, y el día que lo llame alguien nuevo volvería a fallar igual.
    /// </summary>
    private static void EnLaInterfaz(Action hacer)
    {
        var app = Application.Current;
        if (app == null) return;   // sin aplicación no hay dónde pintar; se calla y ya
        if (app.Dispatcher.CheckAccess()) hacer();
        else app.Dispatcher.BeginInvoke(hacer);
    }

    /// <summary>Enseña estas tarjetas y quita las de antes.</summary>
    public static void Mostrar(IReadOnlyList<(Rect Caja, string Selector, string Etiqueta, string Significado)> cuales)
        => EnLaInterfaz(() =>
        {
            // LO QUE SE ESTÉ ESCRIBIENDO NO SE TIRA. Redibujar mientras alguien corrige —el mapeador
            // relee la pantalla cada 900 ms, así que esto pasa solo— le borraría la frase a medias.
            if (EscribiendoAlguna) return;

            CerrarYa();
            foreach (var (caja, selector, etiqueta, significado) in cuales)
            {
                try
                {
                    var t = new TarjetaDeRecuerdo(caja, selector, etiqueta, significado);
                    t.Show();
                    Abiertas.Add(t);
                }
                catch (Exception e) { LogBus.Log("recuerdo", $"no pude enseñar la tarjeta de «{etiqueta}»: {e.Message}"); }
            }
        });

    /// <summary>Las quita todas. Lo que se estuviera escribiendo se guarda antes de cerrar.</summary>
    public static void Cerrar() => EnLaInterfaz(CerrarYa);

    private static void CerrarYa()
    {
        foreach (var t in Abiertas.ToList())
        {
            try { t.GuardarSiCambio(); t.Close(); } catch { }
        }
        Abiertas.Clear();
        RecalcularEscribiendo();
    }
}

/// <summary>Una nota sobre un elemento: su nombre y lo que se aprendió, editable.</summary>
public sealed class TarjetaDeRecuerdo : Window
{
    private const double Ancho = 280;

    private readonly string _selector;
    private readonly TextBox _texto;
    private string _guardado;

    /// <summary>Tiene el foco y el texto ya no es el que estaba guardado.</summary>
    public bool Escribiendo => _texto.IsFocused && _texto.Text != _guardado;

    public TarjetaDeRecuerdo(Rect cajaFisica, string selector, string etiqueta, string significado)
    {
        _selector = selector;
        _guardado = significado;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        // NO SE ACTIVA AL APARECER: robarle el foco a lo que la persona estaba haciendo, solo por
        // enseñarle una nota, sería peor que no enseñarla. El foco llega cuando ella la pulsa.
        ShowActivated = false;
        Width = Ancho;

        var nombre = new TextBlock
        {
            Text = etiqueta,
            Foreground = new SolidColorBrush(Color.FromRgb(0xD9, 0xC2, 0xFF)),
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        _texto = new TextBox
        {
            Text = significado,
            Foreground = Brushes.White,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 12,
            // SIN RECORTAR NI UNA LÍNEA: el matiz de lo enseñado suele estar al final —«…nunca el
            // nombre»— y una nota a medias enseña otra cosa distinta de la que se guardó.
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = false,
            Margin = new Thickness(0, 2, 0, 0),
            CaretBrush = Brushes.White,
        };
        // TODO LO QUE PUEDE CAMBIAR «¿está escribiendo?» lo vuelve a calcular. Se hace aquí, en el
        // hilo de la interfaz, para que quien pregunte desde fuera solo tenga que leer un booleano.
        _texto.GotFocus += (_, __) => TarjetasDeRecuerdo.RecalcularEscribiendo();
        _texto.TextChanged += (_, __) => TarjetasDeRecuerdo.RecalcularEscribiendo();
        _texto.LostFocus += (_, __) => { GuardarSiCambio(); TarjetasDeRecuerdo.RecalcularEscribiendo(); };
        _texto.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { GuardarSiCambio(); TarjetasDeRecuerdo.RecalcularEscribiendo(); e.Handled = true; }
            // ESCAPE DEVUELVE LO QUE HABÍA. Es la salida de «me equivoqué escribiendo», y sin ella
            // la única forma de deshacer sería acordarse de la frase original.
            else if (e.Key == Key.Escape)
            {
                _texto.Text = _guardado;
                TarjetasDeRecuerdo.RecalcularEscribiendo();
                e.Handled = true;
            }
        };

        var pila = new StackPanel();
        pila.Children.Add(nombre);
        pila.Children.Add(_texto);

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x10, 0x10, 0x14)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xCC, 0xA9, 0x6B, 0xF6)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 6, 8, 6),
            Child = pila,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 12, ShadowDepth = 0, Opacity = 0.6, Color = Colors.Black,
            },
        };

        _donde = cajaFisica;
        // SE COLOCA CUANDO YA ES UNA VENTANA DE VERDAD, no en el constructor. Antes de mostrarse no
        // hay PresentationSource —así que la conversión de píxeles físicos a unidades de ventana se
        // cae al valor sin convertir— ni una medida fiable del alto. Con las dos cosas mal, la
        // tarjeta caía JUSTO ENCIMA del elemento y lo tapaba entero (2026-08-24, visto por el
        // usuario). ContentRendered es el primer momento en que ambas cosas son ciertas.
        ContentRendered += (_, __) => Colocar();
    }

    private readonly Rect _donde;

    /// <summary>
    /// ENCIMA del elemento, que es donde no estorba: lo señalado se sigue viendo entero. Solo baja
    /// si arriba no cabe, y aun entonces se pone DEBAJO — nunca sobre el propio elemento.
    /// </summary>
    private void Colocar()
    {
        // Las coordenadas de una ventana van en unidades independientes del dispositivo, y UIA da
        // píxeles físicos: en una pantalla al 150 % la nota aparecería a un tercio de distancia de
        // donde debe. Se convierte por el mismo sitio que todo lo demás.
        var caja = Pantallas.AlVisual(this, _donde);

        // El alto REAL, ya renderizado. Medir a mano daba 0 antes de existir la ventana, y con un
        // alto de cero «cabe encima» siempre salía que sí — por eso acababa pisando el elemento.
        double alto = ActualHeight > 0 ? ActualHeight : 40;

        var area = SystemParameters.WorkArea;
        Left = Math.Max(area.Left + 4, Math.Min(caja.X, area.Right - Ancho - 4));

        double arriba = caja.Y - alto - 6;
        Top = arriba >= area.Top ? arriba : caja.Bottom + 6;
    }

    /// <summary>Guarda si de verdad cambió. Volver a guardar lo mismo solo ensuciaría el log.</summary>
    public void GuardarSiCambio()
    {
        string ahora = _texto.Text.Trim();
        if (ahora.Length == 0 || ahora == _guardado) return;

        bool ok = TarjetasDeRecuerdo.Guardar?.Invoke(_selector, ahora) ?? false;
        if (ok)
        {
            LogBus.Log("recuerdo", $"CORREGIDO a mano: antes «{_guardado}» → ahora «{ahora}»");
            _guardado = ahora;
        }
        else
        {
            // Se devuelve lo que había: dejar en pantalla un texto que NO se guardó haría creer que
            // la corrección quedó hecha, y eso es peor que no poder corregir.
            LogBus.Log("recuerdo", $"no pude guardar la corrección de «{_selector}»: se devuelve lo anterior");
            _texto.Text = _guardado;
        }
    }
}
