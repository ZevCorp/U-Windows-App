using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using U.WindowsClient.Clinical;

namespace U.WindowsClient.Ui;

/// <summary>
/// Lo que Ü va a escribir en SAP, antes de escribirlo.
///
/// Es la última compuerta y la más importante: estos números terminan en la historia
/// clínica de un paciente. Cada fila trae el valor, el campo donde va y **la frase de
/// la consulta de la que salió** — sin esa tercera columna esto sería pedirle al
/// operador que confíe, y confiar no es verificar.
///
/// Los campos que ya tienen algo se muestran tachados y no se escriben. No se ocultan a
/// propósito: que se vea que el dato existía y que Ü decidió no pisarlo es parte de lo
/// que hay que poder demostrar.
/// </summary>
public sealed class FillPreviewWindow : Window
{
    private readonly TaskCompletionSource<bool> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF5));
    private static readonly Brush Dim = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Good = new SolidColorBrush(Color.FromRgb(0x5B, 0xD6, 0x7A));

    public FillPreviewWindow(IReadOnlyList<Binding> bindings, string surface)
    {
        int aEscribir = bindings.Count(b => !b.Occupied);

        Title = "Ü · datos de la consulta";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 720;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x18));

        var panel = new StackPanel { Margin = new Thickness(20) };

        panel.Children.Add(new TextBlock
        {
            Text = aEscribir > 0
                ? $"Voy a escribir {aEscribir} dato(s) de la consulta"
                : "No hay nada que escribir",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = Ink,
        });
        panel.Children.Add(new TextBlock
        {
            Text = surface,
            FontSize = 10,
            FontFamily = new FontFamily("Consolas"),
            Foreground = Dim,
            Margin = new Thickness(0, 4, 0, 12),
            TextWrapping = TextWrapping.Wrap,
        });

        foreach (Binding b in bindings) panel.Children.Add(Row(b));

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
        };
        buttons.Children.Add(Button("Cancelar", "#5A5A66", false));
        if (aEscribir > 0) buttons.Children.Add(Button($"Escribir {aEscribir}", "#2FB457", true));
        panel.Children.Add(buttons);

        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel };

        // Cerrar con la X es cancelar. Nunca «escribir por si acaso».
        Closing += (_, __) => _answer.TrySetResult(false);
    }

    private UIElement Row(Binding b)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var campo = new TextBlock
        {
            Text = b.FieldLabel,
            Foreground = b.Occupied ? Dim : Ink,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var valor = new TextBlock
        {
            Text = b.Data.Value,
            Foreground = b.Occupied ? Dim : Good,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Consolas"),
        };
        if (b.Occupied)
        {
            campo.TextDecorations = TextDecorations.Strikethrough;
            valor.TextDecorations = TextDecorations.Strikethrough;
        }

        // La evidencia es la razón de ser de esta ventana: de dónde salió el número.
        var porque = new TextBlock
        {
            Text = b.Occupied
                ? $"ya tiene «{b.Field.CurrentValue}» — no se toca"
                : b.Data.Evidence,
            Foreground = Dim,
            FontSize = 11,
            FontStyle = b.Occupied ? FontStyles.Normal : FontStyles.Italic,
            TextWrapping = TextWrapping.Wrap,
        };

        Grid.SetColumn(campo, 0);
        Grid.SetColumn(valor, 1);
        Grid.SetColumn(porque, 2);
        grid.Children.Add(campo);
        grid.Children.Add(valor);
        grid.Children.Add(porque);
        return grid;
    }

    private Button Button(string text, string hex, bool result)
    {
        var b = new Button
        {
            Content = text,
            Height = 34,
            MinWidth = 110,
            Margin = new Thickness(8, 0, 0, 0),
            FontSize = 12,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        b.Click += (_, __) => { _answer.TrySetResult(result); Close(); };
        return b;
    }

    /// <summary>true = escribir. Se resuelve con el botón, la X o Escape.</summary>
    public Task<bool> AskAsync()
    {
        Show();
        Activate();
        return _answer.Task;
    }
}
