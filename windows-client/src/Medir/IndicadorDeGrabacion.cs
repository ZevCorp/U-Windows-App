using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace U.WindowsClient.Medir;

/// <summary>
/// EL PUNTO ROJO: se está grabando. Siempre visible mientras dure.
/// </summary>
/// <remarks>
/// NO ES CORTESÍA, ES LA DIFERENCIA ENTRE UNA HERRAMIENTA DE PRUEBAS Y UNA CÁMARA OCULTA. El modo se
/// enciende a propósito, pero va a estar encendido durante un turno entero de urgencias y nadie se
/// acuerda doce horas de algo que no se ve. Quien esté delante tiene que poder saberlo de un vistazo
/// en cualquier momento, sin abrir nada y sin preguntarle a nadie.
///
/// VENTANA PROPIA Y NO UN TROZO DE LA CARITA, por dos razones. La primera es que la carita se puede
/// ocultar —hay una herramienta para eso, self_hide— y un indicador que desaparece cuando alguien
/// esconde el asistente no indica nada. La segunda es que el modo feedback esconde el resto de la
/// interfaz a propósito: esto es justo lo único que debe seguir viéndose.
///
/// ARRIBA A LA DERECHA Y SIN RECIBIR CLICS: no tapa lo que se está haciendo, no se puede cerrar sin
/// querer, y no roba el foco a la aplicación que se está usando (que en urgencias es lo que importa).
/// </remarks>
public sealed class IndicadorDeGrabacion : Window
{
    private readonly Ellipse _punto = new()
    {
        Width = 9,
        Height = 9,
        Fill = new SolidColorBrush(Color.FromRgb(0xE5, 0x3E, 0x3E)),
        VerticalAlignment = VerticalAlignment.Center,
    };

    public IndicadorDeGrabacion()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        IsHitTestVisible = false;   // no roba clics a la app que se está usando
        Focusable = false;
        ShowActivated = false;
        Title = "Ü grabando";

        var fila = new StackPanel { Orientation = Orientation.Horizontal };
        fila.Children.Add(_punto);
        fila.Children.Add(new TextBlock
        {
            Text = "grabando",
            Margin = new Thickness(7, 0, 0, 0),
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF2)),
            VerticalAlignment = VerticalAlignment.Center,
        });

        Content = new Border
        {
            CornerRadius = new CornerRadius(11),
            Background = new SolidColorBrush(Color.FromArgb(0xD8, 0x18, 0x10, 0x12)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xE5, 0x3E, 0x3E)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 5, 12, 5),
            Child = fila,
        };

        // LATE, y despacio. Un punto rojo quieto se lee como un adorno de la interfaz; uno que late
        // se lee como que algo está pasando ahora mismo, que es justo lo que hay que comunicar. Dos
        // segundos por ciclo: se nota si lo miras y no distrae si no.
        Loaded += (_, __) =>
        {
            Colocar();
            _punto.BeginAnimation(OpacityProperty, new DoubleAnimation(1.0, 0.35, TimeSpan.FromSeconds(1))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
            });
        };
        SizeChanged += (_, __) => Colocar();
    }

    private void Colocar()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 16;
        Top = area.Top + 16;
    }
}
