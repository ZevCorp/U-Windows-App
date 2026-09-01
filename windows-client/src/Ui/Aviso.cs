using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Ui;

/// <summary>
/// EL FALLO QUE SE VE. Una tarjeta que dice QUÉ paso falló y POR QUÉ, con el detalle copiable.
/// </summary>
/// <remarks>
/// POR QUÉ EXISTE (2026-09-01, tras probar el login). `U.exe --consulta` entró bien y después no
/// pasó nada: ni ventana, ni error, ni mensaje. El log tenía la respuesta —«médico dentro» y ni una
/// línea más— pero **el log lo lee quien sabe que existe**. Para quien está delante de la pantalla,
/// un fallo mudo es indistinguible de una app que no hace nada, y de las dos saca la misma
/// conclusión: esto no sirve.
///
/// DICE EL PASO, NO UNA CONCLUSIÓN. El título es «No se pudo &lt;paso&gt;» —«abrir la ventana de
/// consulta», «pedir la contraseña»— y debajo el motivo real con la cadena entera de excepciones.
/// Es el aprendizaje nº2 puesto en la interfaz: un mensaje que no distingue sus causas manda a la
/// persona a mirar donde no es.
///
/// SE PUEDE COPIAR, y no es un adorno: lo primero que hace alguien cuando algo falla es contárnoslo,
/// y lo que llega suele ser «no funcionó». El botón convierte eso en el texto exacto, con la hora.
///
/// NO SE USA PARA TODO. Esto es para fallos que dejan al usuario sin poder seguir. Lo que tiene su
/// propio sitio donde decirse —el error del login dentro de su tarjeta, el estado de la consulta en
/// su línea— se dice ahí, que es donde la persona está mirando.
/// </remarks>
public static class Aviso
{
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
            Width = 470,
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
            CornerRadius = new CornerRadius(30),
            Background = Estudio.Fondo,
            BorderBrush = Estudio.Borde,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0),
        };

        var pila = new StackPanel { Margin = new Thickness(30, 26, 30, 26) };

        // El disco de alerta: rojo suave con el glifo dentro. El color va en el FONDO del disco y
        // no en el texto del título — un titular en rojo se lee como una regañina.
        pila.Children.Add(new Border
        {
            Width = 44, Height = 44,
            CornerRadius = new CornerRadius(22),
            Background = Estudio.AlertaSuave,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 16),
            Child = new TextBlock
            {
                Text = "",                       // triángulo de aviso
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 18,
                Foreground = Estudio.Alerta,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        });

        pila.Children.Add(new TextBlock
        {
            // EL PASO, no «ocurrió un error». Lo primero que se lee tiene que ser accionable.
            Text = $"No se pudo {paso}",
            Foreground = Estudio.Tinta,
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14),
        });

        var caja = Estudio.Tarjeta(16);
        caja.Padding = new Thickness(14, 12, 14, 12);
        caja.Child = new TextBlock
        {
            Text = porque,
            Foreground = Estudio.TintaMedia,
            FontSize = 12,
            LineHeight = 18,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"),
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 210,
        };
        var cajaElevada = Estudio.Elevar(caja);
        cajaElevada.Margin = new Thickness(0, 0, 0, 10);
        pila.Children.Add(cajaElevada);

        pila.Children.Add(new TextBlock
        {
            Text = @"El detalle completo está en %LOCALAPPDATA%\U\logs\",
            Foreground = Estudio.TintaTenue,
            FontSize = 11,
            Margin = new Thickness(2, 0, 0, 20),
        });

        var botones = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var copiar = new Button
        {
            Content = "Copiar detalle",
            Height = 42,
            Padding = new Thickness(18, 0, 18, 0),
            Margin = new Thickness(0, 0, 10, 0),
            Foreground = Estudio.Tinta,
            Background = Estudio.Superficie,
            BorderBrush = Estudio.Borde,
            BorderThickness = new Thickness(1),
            FontSize = 13,
            Cursor = Cursors.Hand,
            Template = Estudio.Pastilla(21),
        };
        copiar.ConRelieve(Estudio.Sombra1);
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
            Height = 42,
            Padding = new Thickness(26, 0, 26, 0),
            Foreground = Brushes.White,
            Background = Estudio.Acento,
            BorderThickness = new Thickness(0),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand,
            Template = Estudio.Pastilla(21),
        };
        cerrar.ConRelieve(Estudio.Sombra1);
        cerrar.Click += (_, __) => ventana.Close();

        botones.Children.Add(copiar);
        botones.Children.Add(cerrar);
        pila.Children.Add(botones);

        tarjeta.Child = pila;
        var marco = Estudio.Elevar(tarjeta, Estudio.Sombra3);
        marco.Margin = new Thickness(22, 18, 22, 26);
        ventana.Content = marco;
        ventana.Nitida();
        ventana.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) ventana.DragMove(); };
        ventana.KeyDown += (_, e) => { if (e.Key == Key.Escape) ventana.Close(); };

        LogBus.Log("aviso", $"enseñado: no se pudo {paso}");
        ventana.ShowDialog();
    }
}
