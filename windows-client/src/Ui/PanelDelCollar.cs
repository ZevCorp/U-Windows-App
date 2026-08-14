using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using U.WindowsClient.Voice;

namespace U.WindowsClient.Ui;

/// <summary>
/// La pantalla del collar: enlazar, ver si está, y saber qué lo enciende.
///
/// Es un popup y no una pestaña del panel de desarrollo a propósito: enlazar el collar es algo que
/// hace el USUARIO una vez, no algo que se diagnostica. Lo que aquí se decide —el enlace permanente—
/// sobrevive a cerrar la conversación y a cerrar la aplicación, y por eso tiene su propio sitio en
/// vez de esconderse entre las herramientas.
///
/// Sin XAML por una razón práctica: el csproj de WPF compila por glob, y una ventana nueva con su
/// .xaml obliga a tocar la compilación. Una ventana de cuatro controles no lo paga.
/// </summary>
public sealed class PanelDelCollar : Window
{
    private readonly TextBlock _estado = new() { FontSize = 13, Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap };
    private readonly Button _boton = new() { Height = 34, FontSize = 13 };
    private readonly TextBlock _ayuda = new() { FontSize = 11, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0) };
    private readonly Button _olvidar = new()
    {
        Content = "Olvidar este collar",
        Height = 26,
        FontSize = 11,
        Opacity = 0.7,
        Margin = new Thickness(0, 12, 0, 0),
        HorizontalAlignment = HorizontalAlignment.Left,
        Padding = new Thickness(10, 0, 10, 0),
    };

    public PanelDelCollar()
    {
        Title = "Collar Omi";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x22));
        Foreground = Brushes.White;

        var caja = new StackPanel { Margin = new Thickness(18) };
        caja.Children.Add(new TextBlock
        {
            Text = "Collar Omi",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10),
        });
        caja.Children.Add(_estado);
        caja.Children.Add(_boton);
        caja.Children.Add(_ayuda);
        caja.Children.Add(_olvidar);
        Content = caja;

        // El botón grande CONECTA, nunca desenlaza. Desenlazar es raro y destructivo —hay que volver
        // a emparejar— así que vive abajo, pequeño y aparte: un botón que a veces conecta y a veces
        // olvida es un botón en el que no se puede confiar sin leerlo cada vez.
        _boton.Click += async (_, __) =>
        {
            _boton.IsEnabled = false;
            try { await CollarPermanente.EncenderAsync(); }
            finally { _boton.IsEnabled = true; Pintar(); }
        };

        _olvidar.Click += (_, __) => { CollarPermanente.Olvidar(); Pintar(); };

        // Se repinta con lo que diga el servicio, no con lo que esta ventana crea recordar: si el
        // collar se cae mientras la ventana está abierta, lo que se ve tiene que cambiar solo.
        CollarPermanente.Cambio += AlCambiar;
        Closed += (_, __) => CollarPermanente.Cambio -= AlCambiar;

        Pintar();
    }

    private void AlCambiar() => Dispatcher.BeginInvoke(Pintar);

    private void Pintar()
    {
        bool enlazado = CollarPermanente.Permanente;
        bool vivo = CollarPermanente.Conectado;

        _estado.Text = vivo
            ? "✓ Conectado — la voz puede entrar por el collar."
            : enlazado
                ? $"Recordado, esperando al collar ({CollarPermanente.Estado}). Se conecta solo en cuanto aparezca."
                : "Sin enlazar. El collar tiene que estar encendido y cerca.";
        _estado.Foreground = vivo ? Brushes.LightGreen : enlazado ? Brushes.Orange : Brushes.Gainsboro;

        _boton.Content = vivo ? "Reconectar" : enlazado ? "Conectar ahora" : "Enlazar el collar";
        _boton.Visibility = vivo ? Visibility.Collapsed : Visibility.Visible;
        _olvidar.Visibility = enlazado ? Visibility.Visible : Visibility.Collapsed;

        _ayuda.Text = enlazado
            ? "Este collar ya está recordado: la aplicación lo espera y se conecta sola al arrancar, "
              + "al encenderlo, o al volver a su alcance. No hace falta abrir esta ventana.\n\n"
              + "Enciende y apaga el habla con el botón del collar, o con el icono de voz de la carita."
            : "Sólo hace falta una vez. En cuanto conecte, el collar queda recordado y a partir de ahí "
              + "se conecta solo — como cualquier aparato Bluetooth ya emparejado.";
    }
}
