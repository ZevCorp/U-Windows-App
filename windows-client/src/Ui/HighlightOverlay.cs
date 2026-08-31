using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Interop;

namespace U.WindowsClient.Ui;

// RESCATADO de GraphExplorerWindow.cs en la gran limpieza (2026-08-30): la iluminación del
// señalador es PRODUCTO y vivía atrapada en el archivo del mapa viejo. El mapa murió; la
// luz se queda.
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
        // Esquinas redondeadas: esto dejó de ser una herramienta de inspección el día que se pintó
        // para todo el mundo, y una caja de ángulos vivos encima de la pantalla de alguien se lee
        // como un error del sistema. 6 px es lo que usa el propio Windows 11 en sus controles
        // (2026-08-22, pedido por el usuario).
        RadiusX = 6,
        RadiusY = 6,
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
        // Se reutiliza el primero y se crean los demás al vuelo: lo normal es uno, y no tiene
        // sentido pagar por adelantado unos recuadros que casi nunca se usan.
        foreach (var extra in _extras) _canvas.Children.Remove(extra);
        _extras.Clear();

        for (int i = 0; i < fisicos.Count; i++)
        {
            // Misma conversión que los puntos, y por el mismo sitio: dos formas de responder
            // «dónde cae esto en mi lienzo» acaban discrepando el día que la ventana se mueve.
            var caja = Pantallas.AlVisual(_canvas, fisicos[i]);

            var r = i == 0 ? _rect : NuevoRecuadro();
            Canvas.SetLeft(r, caja.X);
            Canvas.SetTop(r, caja.Y);
            r.Width = caja.Width;
            r.Height = caja.Height;
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
        // Redondeadas igual que la primera: si al señalar varias unas salen con esquinas y otras no,
        // parecen dos cosas distintas.
        RadiusX = 6,
        RadiusY = 6,
    };

    public void HideRect()
    {
        _rect.Visibility = Visibility.Collapsed;
        foreach (var extra in _extras) _canvas.Children.Remove(extra);
        _extras.Clear();
    }
}
