using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace U.WindowsClient.Ui;

/// <summary>
/// Overlay transparente, topmost y CLICK-THROUGH que dibuja recuadros sobre los elementos de la
/// pantalla (inspector visual estilo TalkBack de Android). No recibe clics: los deja pasar a la app
/// de abajo (WS_EX_TRANSPARENT), así el usuario sigue usando su app con normalidad mientras ve las cajas.
///
/// Recibe rects en coordenadas FÍSICAS de pantalla (las que da UIA <c>BoundingRectangle</c>) y las
/// convierte a DIPs con la transform del propio HWND, de modo que se alinean aunque haya escalado de DPI.
/// Cubre la pantalla primaria (el resto de la app —InputExecutor/Screenshotter— también asume primaria).
/// </summary>
public sealed class InspectorOverlay : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private readonly Canvas _canvas = new();
    private Matrix _fromDevice = Matrix.Identity;
    private readonly List<Rect> _neutral = new();
    private (Rect clicked, Rect? intended, bool mismatch)? _flash;

    // Paleta: neutro (todo lo detectado), amarillo (clic que coincide con lo que el asistente tocaría),
    // rojo (mismatch — se marcan AMBOS: sólido lo que cliqueaste, punteado lo que el asistente tocaría).
    private static readonly Brush NeutralStroke = Frozen(0x66, 0xFF, 0xFF, 0xFF);
    private static readonly Brush YellowStroke = Frozen(0xFF, 0xF2, 0xC2, 0x00);
    private static readonly Brush YellowFill = Frozen(0x33, 0xF2, 0xC2, 0x00);
    private static readonly Brush RedStroke = Frozen(0xFF, 0xE5, 0x53, 0x4B);
    private static readonly Brush RedFill = Frozen(0x2E, 0xE5, 0x53, 0x4B);

    private static Brush Frozen(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    public InspectorOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = false;
        Focusable = false;
        Title = "Ü Inspector";
        Content = _canvas;
        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Click-through real: los clics atraviesan el overlay hasta la app de abajo.
        var h = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLong(h, GWL_EXSTYLE);
        SetWindowLong(h, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW);
        var src = PresentationSource.FromVisual(this);
        if (src?.CompositionTarget != null) _fromDevice = src.CompositionTarget.TransformFromDevice;
    }

    /// <summary>Recuadros persistentes de todos los elementos accionables detectados.</summary>
    public void SetNeutral(IEnumerable<Rect> physicalRects)
    {
        _neutral.Clear();
        _neutral.AddRange(physicalRects);
        Redraw();
    }

    /// <summary>
    /// Destello al hacer clic: amarillo si el elemento cliqueado es el mismo que el asistente
    /// resolvería; si no (mismatch), rojo en el cliqueado (sólido) y en el que el asistente tocaría (punteado).
    /// </summary>
    public void Flash(Rect clickedPhysical, Rect? intendedPhysical, bool mismatch)
    {
        _flash = (clickedPhysical, intendedPhysical, mismatch);
        Redraw();
    }

    public void ClearFlash()
    {
        _flash = null;
        Redraw();
    }

    private Rect ToDip(Rect r)
    {
        var tl = _fromDevice.Transform(new Point(r.X, r.Y));
        var br = _fromDevice.Transform(new Point(r.X + r.Width, r.Y + r.Height));
        return new Rect(tl, br);
    }

    private void Redraw()
    {
        _canvas.Children.Clear();
        foreach (var r in _neutral) AddBox(r, NeutralStroke, null, 1.0, false);

        if (_flash is { } f)
        {
            if (f.mismatch)
            {
                AddBox(f.clicked, RedStroke, RedFill, 2.5, false);                     // lo que cliqueaste
                if (f.intended is { } it) AddBox(it, RedStroke, RedFill, 2.5, true);    // lo que el asistente tocaría
            }
            else
            {
                AddBox(f.clicked, YellowStroke, YellowFill, 2.5, false);
            }
        }
    }

    private void AddBox(Rect physical, Brush stroke, Brush? fill, double thickness, bool dashed)
    {
        Rect r = ToDip(physical);
        if (r.Width <= 0 || r.Height <= 0 || double.IsInfinity(r.Width) || double.IsInfinity(r.Height)) return;
        var box = new Rectangle
        {
            Width = r.Width,
            Height = r.Height,
            Stroke = stroke,
            StrokeThickness = thickness,
            Fill = fill,
            RadiusX = 4,
            RadiusY = 4,
        };
        if (dashed) box.StrokeDashArray = new DoubleCollection(new double[] { 3, 2 });
        Canvas.SetLeft(box, r.X);
        Canvas.SetTop(box, r.Y);
        _canvas.Children.Add(box);
    }
}
