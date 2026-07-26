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
    private readonly List<(Rect box, string text, bool isFolder)> _sapRows = new();
    private (Rect clicked, Rect? intended, bool mismatch)? _flash;

    // Paleta: neutro (todo lo detectado por UIA), VIOLETA (clic que coincide con lo que el asistente
    // tocaría), rojo (mismatch — se marcan AMBOS: sólido lo que cliqueaste, punteado lo que el asistente
    // tocaría). SAP va aparte en cian (campos/botones), ámbar (shells sin mapear) y verde (shells
    // mapeados) para que se vea de un vistazo qué llega por Scripting y qué por UIA.
    //
    // El destello de coincidencia era AMARILLO (0xF2C200) y resultaba indistinguible del ámbar de "shell
    // sin mapear" (0xFFA51F). Sobre un árbol se notaba muchísimo: una fila no tiene geometría propia, así
    // que el destello enmarca el SHELL ENTERO durante 1,6 s — el árbol pasaba de verde a un naranja
    // idéntico al de "no mapeado" en cada clic, y parecía que el mapeo se caía. Violeta no colisiona con
    // ningún otro color de la paleta.
    private static readonly Brush NeutralStroke = Frozen(0x66, 0xFF, 0xFF, 0xFF);
    private static readonly Brush MatchStroke = Frozen(0xFF, 0xA9, 0x6B, 0xF6);
    private static readonly Brush MatchFill = Frozen(0x33, 0xA9, 0x6B, 0xF6);
    private static readonly Brush RedStroke = Frozen(0xFF, 0xE5, 0x53, 0x4B);
    private static readonly Brush RedFill = Frozen(0x2E, 0xE5, 0x53, 0x4B);
    private static readonly Brush SapStroke = Frozen(0xCC, 0x25, 0xC8, 0xE0);
    private static readonly Brush SapFill = Frozen(0x1E, 0x25, 0xC8, 0xE0);
    private static readonly Brush SapShellStroke = Frozen(0xFF, 0xFF, 0xA5, 0x1F);
    private static readonly Brush SapShellFill = Frozen(0x22, 0xFF, 0xA5, 0x1F);
    // Shell MAPEADO (árbol con filas enumeradas por clave): ya no es territorio desconocido. VERDE, no
    // gris: el gris neutro anterior era casi indistinguible del ámbar sobre el azul del árbol de SAP, y
    // con dos shells solapados no había forma de decir a simple vista cuál caja había pintado cuál rama
    // del color. Verde=mapeado / ámbar=sin mapear se lee de un vistazo y sin ambigüedad.
    private static readonly Brush SapMappedStroke = Frozen(0xFF, 0x3F, 0xBF, 0x6F);
    private static readonly Brush SapMappedFill = Frozen(0x1E, 0x3F, 0xBF, 0x6F);
    // FILAS de un árbol mapeado. Verde tenue, emparentado con el verde del shell que las contiene: son su
    // contenido. Trazo fino y sin relleno porque son muchas y contiguas — con relleno la lista entera se
    // convierte en una mancha y deja de leerse el texto de SAP debajo.
    private static readonly Brush SapRowStroke = Frozen(0x99, 0x3F, 0xBF, 0x6F);
    private static readonly Brush SapRowFolderStroke = Frozen(0xCC, 0x5C, 0xD6, 0x8A);
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
    /// Recuadros de las FILAS visibles de los árboles SAP. Sus cajas las da SAP fila a fila
    /// (<c>GetItemTop</c>/<c>GetItemHeight</c> con columna), así que son exactas y siguen solas al DPI, al
    /// zoom de SAP y al tema de fuente. Ver <c>SapGuiSurface.VisibleTreeRows</c>.
    /// </summary>
    public void SetSapRows(IEnumerable<(Rect box, string text, bool isFolder)> rows)
    {
        _sapRows.Clear();
        _sapRows.AddRange(rows);
        Redraw();
    }

    /// <summary>
    /// Destello al hacer clic: violeta si el elemento cliqueado es el mismo que el asistente resolvería;
    /// si no (mismatch), rojo en el cliqueado (sólido) y en el que el asistente tocaría (punteado).
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
        //
        // Dentro de los shells, los SIN MAPEAR van antes que los mapeados. En SAP los shells se anidan
        // (ids tipo shellcont/shell/shellcont[1]/shell), así que un ancestro sin mapear puede tener casi
        // el mismo rectángulo que el árbol que envuelve; dibujándolo después, su ámbar se pintaba justo
        // encima del trazo del árbol y el árbol se veía sin mapear aunque no lo estuviera. El mapeado
        // manda: se pinta último y gana la superposición.
        foreach (var s in _sap.Where(s => s.shell).OrderBy(s => s.mapped ? 1 : 0))
        {
            // Mapeado = verde; sin mapear = ámbar de alarma. Ver SapBox.IsMapped.
            AddBox(s.box,
                s.mapped ? SapMappedStroke : SapShellStroke,
                s.mapped ? SapMappedFill : SapShellFill,
                2.0, false);
            if (!string.IsNullOrWhiteSpace(s.caption)) AddCaption(s.box, s.caption);
        }
        // Filas de árbol entre el shell y los campos: van DENTRO del shell (que ya está pintado) y no deben
        // tapar las cajas de campos, que son las accionables por selector directo.
        foreach (var r in _sapRows)
            AddBox(r.box, r.isFolder ? SapRowFolderStroke : SapRowStroke, null, 1.0, false);

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
                AddBox(f.clicked, MatchStroke, MatchFill, 2.5, false);
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
