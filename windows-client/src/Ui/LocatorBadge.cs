using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace U.WindowsClient.Ui;

/// <summary>
/// La "barra de direcciones de Windows": una pastilla flotante arriba a la derecha que muestra el
/// ID de superficie actual (<see cref="Uia.SurfaceLocator"/>) mientras el usuario navega. Es
/// click-through y sin foco: pura lectura, nunca estorba.
/// </summary>
public sealed class LocatorBadge : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private readonly TextBlock _text = new()
    {
        Foreground = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)),
        FontSize = 12,
        FontFamily = new FontFamily("Consolas"),
    };

    public LocatorBadge()
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
        Title = "Ü Locator";
        Content = new Border
        {
            CornerRadius = new CornerRadius(11),
            Background = new SolidColorBrush(Color.FromArgb(0xC8, 0x10, 0x10, 0x14)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 4, 10, 4),
            Child = _text,
        };
        SizeChanged += (_, __) => Reposition();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var h = new WindowInteropHelper(this).Handle;
        SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW);
    }

    public void SetText(string id)
    {
        _text.Text = id;
        Reposition();
    }

    private void Reposition()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - ActualWidth - 12;
        Top = wa.Top + 12;
    }
}
