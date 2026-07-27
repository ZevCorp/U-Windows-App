using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using U.Graph;

namespace U.WindowsClient.Ui;

/// <summary>
/// El depurador paso a paso: se abre cuando el player se detiene ANTES de cada paso y muestra, en el
/// mismo sitio, lo que hace falta para decidir en dos segundos —
///
///   · qué va a hacer el paso y con qué selector,
///   · el veredicto (¿estamos en su pantalla?, ¿el elemento responde?),
///   · qué pasó en el paso anterior — sobre todo si dijo ✓ pero la pantalla no cambió,
///   · y CÓMO SE VEÍA la pantalla cuando el operador enseñó ese paso.
///
/// Lo último es la mitad del valor y ya estaba en disco: StepShotCamera lleva tiempo guardando un
/// pantallazo por paso en cada enseñanza y no lo usaba nadie. Contrastar lo enseñado con lo que hay
/// ahora es lo que convierte esto en un depurador en vez de un botón de «siguiente».
///
/// Se coloca a la IZQUIERDA y estrecha a propósito: la app enseñada tiene que seguir visible, o el
/// contraste no se puede hacer. Nunca modal — bloquear el hilo de UI colgaría al propio player.
/// </summary>
public sealed class StepDebuggerWindow : Window
{
    private readonly TextBlock _headline = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White };
    private readonly TextBlock _verdict = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _detail = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)), FontFamily = new FontFamily("Consolas") };
    private readonly TextBlock _previous = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)) };
    private readonly TextBlock _shotCaption = new() { FontSize = 10, Margin = new Thickness(0, 10, 0, 4), Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)) };
    private readonly Image _shot = new() { Stretch = Stretch.Uniform, MaxHeight = 320 };

    private TaskCompletionSource<StepDecision>? _answer;

    public StepDebuggerWindow()
    {
        Title = "Ü · paso a paso";
        Width = 460;
        Height = 720;
        WindowStyle = WindowStyle.ToolWindow;
        ShowInTaskbar = false;
        Topmost = true;
        Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x18));

        var wa = SystemParameters.WorkArea;
        Left = wa.Left + 16;
        Top = wa.Top + 16;

        var buttons = new UniformGrid { Rows = 1, Columns = 4, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(Btn("▶  Ejecutar", "#2FB457", StepDecision.Ejecutar));
        buttons.Children.Add(Btn("▶▶ Al final", "#3A7BD5", StepDecision.HastaElFinal));
        buttons.Children.Add(Btn("⏭  Saltar", "#7A7A85", StepDecision.Saltar));
        buttons.Children.Add(Btn("⏹  Parar", "#C0392B", StepDecision.Parar));

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(_headline);
        panel.Children.Add(_verdict);
        panel.Children.Add(_detail);
        panel.Children.Add(_previous);
        panel.Children.Add(buttons);
        panel.Children.Add(_shotCaption);
        panel.Children.Add(_shot);

        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel };

        // Cerrar la ventana a mitad de una corrida es «parar», no «seguir a ciegas».
        Closing += (_, __) => _answer?.TrySetResult(StepDecision.Parar);
    }

    private Button Btn(string text, string hex, StepDecision decision)
    {
        var b = new Button
        {
            Content = text,
            Height = 34,
            Margin = new Thickness(2, 0, 2, 0),
            FontSize = 11,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        b.Click += (_, __) => _answer?.TrySetResult(decision);
        return b;
    }

    /// <summary>
    /// Muestra la pausa y espera la decisión. Se llama desde el hilo del player, no del de UI: todo el
    /// pintado va por el Dispatcher y lo que se devuelve es una espera, nunca un bloqueo del hilo de UI.
    /// </summary>
    public Task<StepDecision> AskAsync(StepPause p)
    {
        _answer = new TaskCompletionSource<StepDecision>(TaskCreationOptions.RunContinuationsAsynchronously);

        Dispatcher.Invoke(() =>
        {
            _headline.Text = $"{p.Index}/{p.Total} · {p.Headline}";

            _verdict.Text = p.Verdict;
            _verdict.Foreground = new SolidColorBrush(
                p.Verdict.StartsWith("✓") ? Color.FromRgb(0x5B, 0xD6, 0x7A)
                : p.Verdict.StartsWith("⚠") ? Color.FromRgb(0xFF, 0xC1, 0x4D)
                : Color.FromRgb(0xFF, 0x6B, 0x5B));

            _detail.Text =
                $"selector : {p.Selector}\n"
              + (p.Value.Length > 0 ? $"valor    : «{p.Value}»\n" : "")
              + $"grabado  : {(p.ExpectedSurface.Length > 0 ? p.ExpectedSurface : "(sin pantalla grabada)")}\n"
              + $"ahora    : {p.CurrentSurface}"
              + (p.ExpectedFingerprint.Length > 0
                  ? $"\nhuella   : {p.ExpectedFingerprint} → {(p.CurrentFingerprint.Length > 0 ? p.CurrentFingerprint : "(no calculable)")}"
                    + (p.SameStructure ? "" : "   ← DISTINTA")
                  : "");

            _previous.Text = p.PreviousOutcome.Length > 0
                ? $"anterior → {p.PreviousOutcome}"
                : "anterior → (este es el primero)";

            LoadShot(p.TaughtShotPath);

            if (!IsVisible) Show();
            Activate();
        });

        return _answer.Task;
    }

    /// <summary>
    /// Carga la captura de la enseñanza. Se lee a memoria (OnLoad + cierre del stream) para no dejar el
    /// PNG bloqueado en disco: con el fichero tomado, la siguiente enseñanza del mismo workflow no
    /// podría sobrescribirlo y las capturas se quedarían congeladas en la primera versión.
    /// </summary>
    private void LoadShot(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            _shotCaption.Text = "sin captura de la enseñanza para este paso";
            _shot.Source = null;
            return;
        }

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            _shot.Source = bmp;
            _shotCaption.Text = "así se veía al ENSEÑAR este paso — compáralo con la pantalla de detrás";
        }
        catch (Exception e)
        {
            _shotCaption.Text = $"no se pudo abrir la captura: {e.Message}";
            _shot.Source = null;
        }
    }

    /// <summary>Cierra sin que el Closing lo interprete como una parada del operador.</summary>
    public void Finish()
    {
        _answer = null;
        Dispatcher.Invoke(() => { if (IsVisible) Hide(); });
    }
}
