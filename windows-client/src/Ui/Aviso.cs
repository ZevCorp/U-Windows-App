using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Ui;

/// <summary>
/// EL FALLO QUE SE VE. Una tarjeta que dice QUÉ paso falló y POR QUÉ, con el detalle copiable.
/// </summary>
/// <remarks>
/// POR QUÉ EXISTE (2026-09-01, pedido por el usuario tras probar el login). `U.exe --consulta`
/// entró bien y después no pasó nada: ni ventana, ni error, ni mensaje. El log tenía la respuesta
/// —«médico dentro» y ni una línea más— pero **el log lo lee quien sabe que existe**. Para el que
/// está delante de la pantalla, un fallo mudo es indistinguible de una app que no hace nada, y de
/// las dos saca la misma conclusión: esto no sirve.
///
/// DICE EL PASO, NO UNA CONCLUSIÓN. El título es «No se pudo <paso>» —«abrir la ventana de
/// consulta», «pedir la contraseña»— y debajo va el motivo real con la cadena entera de
/// excepciones. Es el aprendizaje nº2 puesto en la interfaz: un mensaje que no distingue sus causas
/// manda a la persona a mirar donde no es.
///
/// SE PUEDE COPIAR, y no es un adorno: lo primero que hace alguien cuando algo falla es contárnoslo,
/// y lo que llega suele ser «no funcionó». El botón convierte eso en el texto exacto, con la hora.
///
/// NO SE USA PARA TODO. Esto es para fallos que dejan al usuario sin poder seguir. Lo que tiene su
/// propio sitio donde decirse —el error del login dentro de la tarjeta de login, el estado de la
/// consulta en su línea de estado— se dice ahí, que es donde la persona está mirando.
/// </remarks>
public static class Aviso
{
    private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0xEA, 0xF2, 0xFF));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromArgb(0x9E, 0xC8, 0xDC, 0xFF));
    private static readonly Brush Danger = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x8A));
    private static readonly Brush Blue = new SolidColorBrush(Color.FromRgb(0x4C, 0x8D, 0xFF));
    private static readonly Brush FieldBg = new SolidColorBrush(Color.FromRgb(0x0A, 0x12, 0x22));
    private static readonly Brush Line = new SolidColorBrush(Color.FromArgb(0x40, 0x60, 0x94, 0xEB));

    /// <summary>
    /// Enseña el fallo. <paramref name="paso"/> es lo que se estaba intentando («abrir la ventana
    /// de consulta»), <paramref name="porque"/> el motivo real.
    /// </summary>
    public static void Fallo(string paso, string porque)
    {
        // El aviso puede llegar desde un hilo cualquiera; la ventana se construye en el de la
        // interfaz o WPF lanza, y entonces el fallo del aviso taparía el fallo original.
        var app = Application.Current;
        if (app == null) { LogBus.Log("aviso", $"sin aplicación viva para enseñar: {paso}"); return; }
        if (!app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.BeginInvoke(new Action(() => Fallo(paso, porque)));
            return;
        }
        Pintar(paso, porque);
    }

    private static void Pintar(string paso, string porque)
    {
        string detalle = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] No se pudo {paso}\n{porque}";

        var ventana = new Window
        {
            Title = "Miracle",
            Width = 460,
            SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = true,
            Topmost = true,
        };

        var tarjeta = new Border
        {
            CornerRadius = new CornerRadius(18),
            Background = new LinearGradientBrush(
                Color.FromRgb(0x07, 0x0C, 0x18), Color.FromRgb(0x02, 0x04, 0x0A), 90),
            BorderBrush = Line,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(18),
            Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 0, Opacity = 0.55, Color = Colors.Black },
        };

        var pila = new StackPanel { Margin = new Thickness(28, 24, 28, 22) };

        pila.Children.Add(new TextBlock
        {
            Text = "⚠",
            Foreground = Danger,
            FontSize = 26,
            Margin = new Thickness(0, 0, 0, 10),
        });
        pila.Children.Add(new TextBlock
        {
            // EL PASO, no «ocurrió un error». Lo primero que se lee tiene que ser accionable.
            Text = $"No se pudo {paso}",
            Foreground = Ink,
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        });

        pila.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = FieldBg,
            BorderBrush = Line,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 8),
            Child = new TextBlock
            {
                Text = porque,
                Foreground = Muted,
                FontSize = 12.5,
                FontFamily = new FontFamily("Consolas, Cascadia Mono, monospace"),
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 220,
            },
        });

        pila.Children.Add(new TextBlock
        {
            Text = @"El detalle completo está en %LOCALAPPDATA%\U\logs\",
            Foreground = Muted,
            FontSize = 11,
            Margin = new Thickness(2, 0, 0, 16),
        });

        var botones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

        var copiar = new Button
        {
            Content = "Copiar detalle",
            Height = 38,
            Padding = new Thickness(16, 0, 16, 0),
            Margin = new Thickness(0, 0, 10, 0),
            Foreground = Ink,
            Background = FieldBg,
            BorderThickness = new Thickness(0),
            FontSize = 13,
            Cursor = Cursors.Hand,
            Template = Redondo(11),
        };
        copiar.Click += (_, __) =>
        {
            try { Clipboard.SetText(detalle); copiar.Content = "Copiado ✓"; }
            catch (Exception e)
            {
                // El portapapeles lo puede tener bloqueado otra app. Se dice, no se finge.
                copiar.Content = "No se pudo copiar";
                LogBus.Log("aviso", $"portapapeles ocupado: {e.Message}");
            }
        };

        var cerrar = new Button
        {
            Content = "Cerrar",
            Height = 38,
            Padding = new Thickness(22, 0, 22, 0),
            Foreground = Brushes.White,
            Background = Blue,
            BorderThickness = new Thickness(0),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand,
            Template = Redondo(11),
        };
        cerrar.Click += (_, __) => ventana.Close();

        botones.Children.Add(copiar);
        botones.Children.Add(cerrar);
        pila.Children.Add(botones);

        tarjeta.Child = pila;
        ventana.Content = tarjeta;
        ventana.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) ventana.DragMove(); };
        ventana.KeyDown += (_, e) => { if (e.Key == Key.Escape) ventana.Close(); };

        LogBus.Log("aviso", $"enseñado: no se pudo {paso}");
        ventana.ShowDialog();
    }

    private static ControlTemplate Redondo(double radio)
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radio));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        template.VisualTree = border;
        return template;
    }
}
