using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using U.WindowsClient.Navigation;

namespace U.WindowsClient.Ui;

/// <summary>
/// «¿De qué aplicaciones olvido lo aprendido?»
///
/// Lo enseñado sobrevive a borrar el grafo —es aprendizaje, no terreno— y por eso hace falta una
/// forma propia de tirarlo: si la única manera de quitar una jerarquía equivocada fuera borrarlo
/// todo, la enseñanza se volvería una cárcel. Aquí se elige por aplicación, porque enseñar bien el
/// explorador no es motivo para perder lo que se aprendió del navegador.
///
/// Se dice de dónde vino cada una —cuántas las dijo una persona— porque no pesan igual: lo que
/// enseñó el maestro se vuelve a deducir en la siguiente pasada, lo que dijo alguien hay que
/// volver a decirlo.
/// </summary>
public sealed class OlvidarJerarquias : Window
{
    private readonly SurfaceMap _mapa;
    private readonly StackPanel _lista = new();
    private readonly List<(CheckBox Casilla, string App)> _filas = new();

    /// <summary>Se olvidó algo: quien dibuja debería repintarse.</summary>
    public event Action? Olvidado;

    public OlvidarJerarquias(SurfaceMap mapa)
    {
        _mapa = mapa;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var col = new StackPanel();
        col.Children.Add(new TextBlock
        {
            Text = "Olvidar la jerarquía aprendida",
            Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.SemiBold,
        });
        col.Children.Add(new TextBlock
        {
            Text = "Marca las aplicaciones cuyo primer nivel quieres que olvide. "
                 + "El resto de lo aprendido no se toca.",
            Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
            FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12),
        });
        col.Children.Add(new ScrollViewer
        {
            MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _lista,
        });

        var olvidar = Boton("Olvidar las marcadas", Color.FromArgb(0x44, 0xE5, 0x73, 0x73));
        var cerrar = Boton("Cerrar", Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        olvidar.Click += (_, __) => Olvidar();
        cerrar.Click += (_, __) => Close();

        var fila = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        fila.Children.Add(cerrar);
        fila.Children.Add(olvidar);
        col.Children.Add(fila);

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x18, 0x18, 0x1C)),
            CornerRadius = new CornerRadius(14),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(18),
            Child = col,
        };

        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Pintar();
    }

    private static Button Boton(string texto, Color fondo) => new()
    {
        Content = texto, Height = 28, MinWidth = 100, FontSize = 12,
        Margin = new Thickness(8, 0, 0, 0), Cursor = Cursors.Hand,
        Background = new SolidColorBrush(fondo), Foreground = Brushes.White,
        BorderThickness = new Thickness(0),
    };

    private void Pintar()
    {
        _lista.Children.Clear();
        _filas.Clear();

        var apps = _mapa.AppsConJerarquia();
        if (apps.Count == 0)
        {
            _lista.Children.Add(new TextBlock
            {
                Text = "Todavía no hay ninguna aplicación con jerarquía aprendida.",
                Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
                FontSize = 12, Margin = new Thickness(2, 6, 0, 6),
            });
            return;
        }

        foreach (var (app, cuantas, deHumano) in apps)
        {
            var casilla = new CheckBox
            {
                Foreground = Brushes.White, FontSize = 13, Margin = new Thickness(2, 5, 0, 5),
                Cursor = Cursors.Hand,
                Content = new TextBlock
                {
                    Inlines =
                    {
                        new System.Windows.Documents.Run(app) { FontWeight = FontWeights.SemiBold },
                        new System.Windows.Documents.Run($"   {cuantas} salida(s)"
                            + (deHumano > 0 ? $", {deHumano} dicha(s) por ti" : ", todas del maestro"))
                        {
                            Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
                            FontSize = 11,
                        },
                    },
                },
            };
            _filas.Add((casilla, app));
            _lista.Children.Add(casilla);
        }
    }

    private void Olvidar()
    {
        var marcadas = _filas.Where(f => f.Casilla.IsChecked == true).Select(f => f.App).ToList();
        if (marcadas.Count == 0) { Close(); return; }

        // Se pregunta porque no se deshace, y lo dicho a mano cuesta volver a decirlo.
        var r = MessageBox.Show(
            $"Se va a olvidar la jerarquía aprendida de {marcadas.Count} aplicación(es):\n\n"
            + string.Join("\n", marcadas.Select(a => $"  · {a}"))
            + "\n\nEsto no se puede deshacer. ¿Seguimos?",
            "Olvidar jerarquía", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (r != MessageBoxResult.Yes) return;

        int total = marcadas.Sum(a => _mapa.OlvidarJerarquiaDe(a));
        Diagnostics.LogBus.Log("mapa", $"olvidadas {total} salida(s) enseñadas de {marcadas.Count} app(s)");
        Olvidado?.Invoke();
        Pintar();
    }
}
