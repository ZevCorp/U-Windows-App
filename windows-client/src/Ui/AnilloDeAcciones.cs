using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Ui;

/// <summary>
/// EL ANILLO DE ACCIONES: cinco burbujas en abanico alrededor de la carita, hacia el centro de la
/// pantalla, que aparecen al mantenerla oprimida o con clic derecho (spec 008). Es lo que sustituye
/// a los botones que asomaban a su lado: no ocupa nada hasta que se pide, y se pide con un gesto
/// que tiene versión de siempre (el clic derecho) para quien no sepa la de mantener.
/// </summary>
/// <remarks>
/// Dos formas de elegir, para dos manos distintas:
///
/// · <b>Mantener → deslizar → soltar.</b> Un solo gesto continuo, como un menú en tarta: el ratón
///   sigue capturado por la carita (<see cref="FaceGestures"/>), así que esta ventana no recibe
///   nada; la carita le dice dónde va el cursor (<see cref="Resaltar"/>) y dónde se soltó
///   (<see cref="ElegirEn"/>). Soltar sobre nada es cancelar.
/// · <b>Clic derecho → clic en la burbuja.</b> Aquí sí llega el ratón, porque ya no hay captura.
///
/// Dónde va cada burbuja lo decide <see cref="ReglaDelAnillo"/>, que es lo que juzga el contrato
/// (promesa 115): esta clase solo pinta lo que la regla dice.
///
/// Es una ventana satélite QUE NO SE ACTIVA (<c>ShowActivated=false</c> + <c>WS_EX_NOACTIVATE</c>),
/// como el aura: abrirla no le quita el foco a nadie, y hacer clic en una burbuja tampoco desactiva
/// a la carita —que es justo lo que permite cerrar el anillo cuando la carita se desactiva por un
/// clic FUERA—. A diferencia del aura no es click-through: las burbujas se pulsan.
/// </remarks>
public sealed class AnilloDeAcciones : Window
{
    /// <summary>Una acción del anillo: el icono que se ve, el nombre que dice el tooltip, y qué hace.</summary>
    public sealed record Item(string Icono, string Nombre, Action Accion);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    /// <summary>Aire alrededor del abanico para que la sombra de las burbujas no se recorte.</summary>
    private const double Margen = 14;

    private static readonly Brush Cristal = Frozen(Color.FromArgb(0xF2, 0x0F, 0x13, 0x1C));
    private static readonly Brush Hairline = Frozen(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));

    private readonly Canvas _lienzo = new();
    private readonly List<Border> _burbujas = new();
    private IReadOnlyList<Item> _items = Array.Empty<Item>();
    private IReadOnlyList<Point> _posiciones = Array.Empty<Point>();   // centros, en DIPs de pantalla
    private int? _resaltada;

    /// <summary>Se cerró, por la razón que fuera: quien lo abrió deja de mirar hacia él.</summary>
    public event Action? Cerrado;

    public AnilloDeAcciones()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        ShowActivated = false;
        SizeToContent = SizeToContent.Manual;
        Content = _lienzo;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var h = new WindowInteropHelper(this).Handle;
        SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }

    /// <summary>
    /// Abre el anillo alrededor de <paramref name="centroCarita"/> (DIPs de pantalla), hacia el
    /// centro de la pantalla, con estas acciones de arriba a abajo.
    /// </summary>
    public void Mostrar(Point centroCarita, bool pegadaALaIzquierda, IReadOnlyList<Item> items)
    {
        _items = items;
        _resaltada = null;
        // El área de trabajo del monitor principal, que es donde vive la carita (ColocarVentana
        // acota contra la misma). Un segundo monitor es otra conversación.
        _posiciones = ReglaDelAnillo.Posiciones(centroCarita, pegadaALaIzquierda, items.Count, SystemParameters.WorkArea);
        if (_posiciones.Count == 0) return;

        double r = ReglaDelAnillo.DiametroItem / 2;
        double minX = _posiciones.Min(p => p.X) - r - Margen, maxX = _posiciones.Max(p => p.X) + r + Margen;
        double minY = _posiciones.Min(p => p.Y) - r - Margen, maxY = _posiciones.Max(p => p.Y) + r + Margen;
        Left = minX; Top = minY; Width = maxX - minX; Height = maxY - minY;

        _lienzo.Children.Clear();
        _burbujas.Clear();
        for (int i = 0; i < items.Count; i++)
        {
            var b = Burbuja(items[i], i);
            Canvas.SetLeft(b, _posiciones[i].X - r - minX);
            Canvas.SetTop(b, _posiciones[i].Y - r - minY);
            _lienzo.Children.Add(b);
            _burbujas.Add(b);
        }

        Show();
        LogBus.Log("carita", $"anillo abierto con {items.Count} acciones, hacia la {(pegadaALaIzquierda ? "derecha" : "izquierda")}");

        // Nacen desde la carita: cada una crece y se enciende con un pequeño desfase respecto a la
        // anterior, de arriba a abajo. Es lo que hace que «aparecer» se lea como abrirse y no como
        // un parpadeo. Todo en RenderTransform, que no vuelve a medir nada.
        for (int i = 0; i < _burbujas.Count; i++)
        {
            var b = _burbujas[i];
            var escala = (ScaleTransform)b.RenderTransform;
            var retraso = TimeSpan.FromMilliseconds(28 * i);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            b.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)) { BeginTime = retraso, EasingFunction = ease });
            escala.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(200)) { BeginTime = retraso, EasingFunction = ease });
            escala.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(200)) { BeginTime = retraso, EasingFunction = ease });
        }
    }

    /// <summary>El cursor va por aquí (DIPs de pantalla): se enciende la burbuja que elegiría soltar ahora.</summary>
    public void Resaltar(Point cursor)
    {
        if (!IsVisible) return;
        int? idx = ReglaDelAnillo.ElegirEn(_posiciones, cursor);
        if (idx == _resaltada) return;
        _resaltada = idx;
        for (int i = 0; i < _burbujas.Count; i++) Pintar(_burbujas[i], encendida: i == idx);
    }

    /// <summary>Qué acción hay bajo <paramref name="cursor"/> (DIPs de pantalla), o ninguna.</summary>
    public Item? ElegirEn(Point cursor)
    {
        int? idx = ReglaDelAnillo.ElegirEn(_posiciones, cursor);
        return idx is int i && i < _items.Count ? _items[i] : null;
    }

    public void Cerrar()
    {
        if (!IsVisible) return;
        Hide();
        Cerrado?.Invoke();
    }

    private Border Burbuja(Item item, int indice)
    {
        var b = new Border
        {
            Width = ReglaDelAnillo.DiametroItem,
            Height = ReglaDelAnillo.DiametroItem,
            CornerRadius = new CornerRadius(ReglaDelAnillo.DiametroItem / 2),
            Background = Cristal,
            BorderBrush = Hairline,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            ToolTip = item.Nombre,
            Opacity = 0,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Direction = 270, Opacity = 0.45, Color = Color.FromRgb(0x0B, 0x0F, 0x18) },
            Child = new TextBlock
            {
                Text = item.Icono,
                FontSize = 18,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            },
        };
        System.Windows.Automation.AutomationProperties.SetName(b, item.Nombre);
        // El camino del clic derecho: sin captura, el ratón llega aquí.
        b.MouseEnter += (_, __) => { _resaltada = indice; for (int i = 0; i < _burbujas.Count; i++) Pintar(_burbujas[i], encendida: i == indice); };
        b.MouseLeave += (_, __) => { if (_resaltada == indice) { _resaltada = null; Pintar(b, encendida: false); } };
        b.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Cerrar();
            item.Accion();
        };
        return b;
    }

    /// <summary>Encendida = el azul de Ü y un poco más grande; el resto, cristal.</summary>
    private static void Pintar(Border b, bool encendida)
    {
        b.Background = encendida ? UiPalette.TrabajandoBrush : Cristal;
        var escala = (ScaleTransform)b.RenderTransform;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        double objetivo = encendida ? 1.15 : 1;
        escala.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(objetivo, TimeSpan.FromMilliseconds(120)) { EasingFunction = ease });
        escala.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(objetivo, TimeSpan.FromMilliseconds(120)) { EasingFunction = ease });
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
