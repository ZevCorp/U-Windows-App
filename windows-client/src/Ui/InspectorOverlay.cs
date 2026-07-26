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
    private readonly List<(Rect box, string caption, bool shell, bool mapped)> _sap = new();
    private (Rect clicked, Rect? intended, bool mismatch)? _flash;

    // Paleta: neutro (todo lo detectado por UIA), amarillo (clic que coincide con lo que el asistente
    // tocaría), rojo (mismatch — se marcan AMBOS: sólido lo que cliqueaste, punteado lo que el asistente
    // tocaría). SAP va aparte en cian (campos/botones) y ámbar (shells: árbol, grid) para que se vea de
    // un vistazo qué llega por Scripting y qué por UIA.
    private static readonly Brush NeutralStroke = Frozen(0x66, 0xFF, 0xFF, 0xFF);
    private static readonly Brush YellowStroke = Frozen(0xFF, 0xF2, 0xC2, 0x00);
    private static readonly Brush YellowFill = Frozen(0x33, 0xF2, 0xC2, 0x00);
    private static readonly Brush RedStroke = Frozen(0xFF, 0xE5, 0x53, 0x4B);
    private static readonly Brush RedFill = Frozen(0x2E, 0xE5, 0x53, 0x4B);
    private static readonly Brush SapStroke = Frozen(0xCC, 0x25, 0xC8, 0xE0);
    private static readonly Brush SapFill = Frozen(0x1E, 0x25, 0xC8, 0xE0);
    private static readonly Brush SapShellStroke = Frozen(0xFF, 0xFF, 0xA5, 0x1F);
    private static readonly Brush SapShellFill = Frozen(0x22, 0xFF, 0xA5, 0x1F);
    // Shell MAPEADO (árbol con filas enumeradas por clave): ya no es territorio desconocido, así que
    // nada de ámbar de alarma — gris neutro, apenas más marcado que las cajas de UIA para que el rótulo
    // se siga leyendo. El ámbar queda reservado a lo que sigue opaco (grids, toolbars).
    private static readonly Brush SapMappedStroke = Frozen(0xAA, 0xC9, 0xC9, 0xC9);
    private static readonly Brush SapMappedFill = Frozen(0x12, 0xC9, 0xC9, 0xC9);
    private static readonly Brush CaptionBg = Frozen(0xE0, 0x1A, 0x1A, 0x1A);
    private static readonly Brush CaptionFg = Frozen(0xFF, 0xFF, 0xFF, 0xFF);

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
        ShowActivated = false; // no robar el foco a la app de abajo (SAP): el overlay solo dibuja
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

    /// <summary>Recuadros persistentes de todos los elementos accionables detectados por UIA.</summary>
    public void SetNeutral(IEnumerable<Rect> physicalRects)
    {
        _neutral.Clear();
        _neutral.AddRange(physicalRects);
        Redraw();
    }

    /// <summary>
    /// Recuadros de los elementos SAP leídos por Scripting (van en cian; los shells —árbol, grid— en
    /// ámbar con rótulo). Se dibujan JUNTO a los de UIA: es la razón de ser del inspector doble.
    /// </summary>
    public void SetSap(IEnumerable<(Rect box, string caption, bool shell, bool mapped)> boxes)
    {
        _sap.Clear();
        _sap.AddRange(boxes);
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

        // SAP encima del neutro de UIA: los shells (árbol/grid) primero para que sus rótulos no queden
        // tapados por las cajas de campos que caen dentro.
        foreach (var s in _sap.Where(s => s.shell))
        {
            // Mapeado = gris neutro; sin mapear = ámbar de alarma. Ver SapBox.IsMapped.
            AddBox(s.box,
                s.mapped ? SapMappedStroke : SapShellStroke,
                s.mapped ? SapMappedFill : SapShellFill,
                2.0, false);
            if (!string.IsNullOrWhiteSpace(s.caption)) AddCaption(s.box, s.caption);
        }
        foreach (var s in _sap.Where(s => !s.shell))
            AddBox(s.box, SapStroke, SapFill, 1.2, false);

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

    /// <summary>Rótulo pegado a la esquina superior izquierda de una caja (para nombrar shells SAP).</summary>
    private void AddCaption(Rect physical, string text)
    {
        Rect r = ToDip(physical);
        if (r.Width <= 0 || r.Height <= 0 || double.IsInfinity(r.Width)) return;

        var label = new Border
        {
            Background = CaptionBg,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 1, 4, 1),
            Child = new TextBlock
            {
                Text = text.Length > 60 ? text.Substring(0, 59) + "…" : text,
                Foreground = CaptionFg,
                FontSize = 11,
            },
        };
        // Encima del borde superior; si no cabe arriba (pegado al techo), lo mete justo dentro.
        double top = r.Y - 18 >= 0 ? r.Y - 18 : r.Y + 1;
        Canvas.SetLeft(label, r.X);
        Canvas.SetTop(label, top);
        _canvas.Children.Add(label);
    }
}
