using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Ui;

/// <summary>
/// Parar en cada paso, enseñar lo que acaba de pasar, y dejar decir qué se ha visto.
///
/// Depurar esto por el log tiene un problema de fondo: el log cuenta lo que el sistema CREE que
/// hizo, y lo que hace falta saber es qué pasó de verdad en la pantalla. Entre las dos cosas está
/// justo el fallo — se numeraron los puntos de una app mientras se abría otra, y en el log las dos
/// líneas se veían perfectamente correctas por separado (2026-08-06).
///
/// Con esto el proceso se detiene, se ve el estado real con los propios ojos, y lo observado se
/// escribe EN EL MOMENTO, junto al paso al que pertenece. Un «esto no se actualizó» dicho media
/// hora después ya no se puede situar; dicho aquí, sí.
///
/// Apagado no cuesta nada: <see cref="EsperarAsync"/> vuelve enseguida y el proceso corre igual.
/// </summary>
public static class PasoAPaso
{
    /// <summary>¿Se para en cada paso? Lo enciende el botón de la barra.</summary>
    public static bool Activo { get; set; }

    /// <summary>
    /// ¿Esta prueba se congela como escenario de CI al terminar? Lo marca el usuario en la
    /// ventanita (desmarcado por defecto): una prueba que salió bien vale más si además queda
    /// como vara de medir para las versiones futuras del núcleo (2026-08-08, pedido por él).
    /// Quien mapea lo consulta al FINAL del recorrido; ver EscenarioCi.
    /// </summary>
    public static bool GuardarComoCi { get; set; }

    /// <summary>Lo que se ha ido observando, paso a paso. Para poder leerlo entero al final.</summary>
    public static IReadOnlyList<(string Paso, string Visto)> Observado => _observado;
    private static readonly List<(string Paso, string Visto)> _observado = new();

    private static Ventana? _ventana;

    /// <summary>Se abandona el proceso: quien esté esperando debe rendirse.</summary>
    public sealed class Abandonado : Exception
    {
        public Abandonado(string paso) : base($"paso a paso: se detuvo en «{paso}»") { }
    }

    /// <summary>
    /// Detiene el proceso y no sigue hasta que se diga. Si está apagado, no hace nada.
    /// </summary>
    /// <param name="paso">Qué acaba de ocurrir, en una línea.</param>
    /// <param name="detalle">Los datos del paso: lo que habría que mirar para juzgarlo.</param>
    public static async Task EsperarAsync(string paso, string detalle = "")
    {
        if (!Activo) return;

        LogBus.Log("paso", $"— {paso}" + (detalle.Length > 0 ? $" · {detalle}" : ""));

        var espera = new TaskCompletionSource<(bool Sigue, string Visto)>();
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            _ventana ??= new Ventana();
            _ventana.Mostrar(paso, detalle, espera);
        });

        var (sigue, visto) = await espera.Task;
        if (visto.Length > 0)
        {
            _observado.Add((paso, visto));
            LogBus.Log("paso", $"OBSERVADO en «{paso}»: {visto}");
        }
        if (!sigue) throw new Abandonado(paso);
    }

    /// <summary>Olvida lo observado. Se llama al empezar una corrida nueva.</summary>
    public static void Limpiar() => _observado.Clear();

    /// <summary>La ventanita: qué paso es, qué hay que mirar, y un sitio para decir qué se ve.</summary>
    private sealed class Ventana : Window
    {
        private readonly TextBlock _paso = new()
        {
            Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        private readonly TextBox _detalle = new()
        {
            IsReadOnly = true, AcceptsReturn = true, MaxHeight = 220,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            Foreground = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0), FontFamily = new FontFamily("Consolas"),
            FontSize = 11, Padding = new Thickness(8), Margin = new Thickness(0, 8, 0, 0),
        };
        private readonly TextBox _visto = new()
        {
            AcceptsReturn = true, Height = 64, TextWrapping = TextWrapping.Wrap,
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            Foreground = Brushes.White, BorderThickness = new Thickness(0),
            Padding = new Thickness(8), Margin = new Thickness(0, 8, 0, 0), FontSize = 12,
        };
        private TaskCompletionSource<(bool, string)>? _espera;

        public Ventana()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            Width = 460;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.Manual;

            var col = new StackPanel();
            col.Children.Add(new TextBlock
            {
                Text = "PASO A PASO",
                Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
                FontSize = 10, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6),
            });
            col.Children.Add(_paso);
            col.Children.Add(_detalle);
            col.Children.Add(new TextBlock
            {
                Text = "¿Qué ves en la pantalla ahora mismo? (opcional)",
                Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
                FontSize = 11, Margin = new Thickness(0, 10, 0, 0),
            });
            col.Children.Add(_visto);

            // La casilla de CI vive aquí y no en la barra: se decide DURANTE la prueba, cuando ya
            // se está viendo si va bien, no antes de saber qué va a pasar.
            var ci = new CheckBox
            {
                Content = "al terminar, guardar esta prueba para CI",
                Foreground = new SolidColorBrush(Color.FromArgb(0xBB, 0xFF, 0xFF, 0xFF)),
                FontSize = 11, Margin = new Thickness(0, 10, 0, 0),
                IsChecked = false,
            };
            ci.Checked += (_, __) => GuardarComoCi = true;
            ci.Unchecked += (_, __) => GuardarComoCi = false;
            col.Children.Add(ci);

            var seguir = Boton("Continuar  ⏎", Color.FromArgb(0x55, 0x66, 0xBB, 0x6A));
            var parar = Boton("Detener", Color.FromArgb(0x44, 0xE5, 0x73, 0x73));
            seguir.Click += (_, __) => Responder(true);
            parar.Click += (_, __) => Responder(false);

            var fila = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0),
            };
            fila.Children.Add(parar);
            fila.Children.Add(seguir);
            col.Children.Add(fila);

            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x18, 0x18, 0x1C)),
                CornerRadius = new CornerRadius(14),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16),
                Child = col,
            };

            // Enter sigue: el gesto más repetido no debería pedir apuntar con el ratón.
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
                { e.Handled = true; Responder(true); }
                else if (e.Key == Key.Escape) { e.Handled = true; Responder(false); }
            };
        }

        private static Button Boton(string texto, Color fondo) => new()
        {
            Content = texto, Height = 28, MinWidth = 90, FontSize = 12,
            Margin = new Thickness(8, 0, 0, 0), Cursor = Cursors.Hand,
            Background = new SolidColorBrush(fondo), Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
        };

        public void Mostrar(string paso, string detalle, TaskCompletionSource<(bool, string)> espera)
        {
            _espera = espera;
            _paso.Text = paso;
            _detalle.Text = detalle;
            _detalle.Visibility = detalle.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _visto.Text = "";

            // ABAJO Y A LA DERECHA, no en el centro: en el centro taparía justo la app que hay que
            // mirar para responder, que es lo único que esta ventana pide.
            var area = SystemParameters.WorkArea;
            Left = area.Right - Width - 24;
            Top = area.Bottom - 340;

            Show();
            Activate();
            _visto.Focus();
        }

        private void Responder(bool sigue)
        {
            var e = _espera;
            _espera = null;
            Hide();
            e?.TrySetResult((sigue, _visto.Text.Trim()));
        }
    }
}
