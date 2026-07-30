using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using U.Graph.NoteExport;

namespace U.WindowsClient.Ui;

/// <summary>
/// La compuerta humana antes de escribir una nota firmada en la historia clínica de un paciente.
///
/// Por qué existe una ventana y no una comprobación automática: el trabajo identifica al paciente
/// con un uuid de Miracle que SAP no conoce, y en todo el sistema no hay hoy ningún identificador
/// compartido con el hospital. La máquina NO PUEDE verificar que la pantalla abierta corresponda a
/// esta consulta — puede leer lo que la pantalla dice y ponerlo al lado de lo que dice el trabajo.
/// Quien cierra esa distancia es una persona, igual que en <see cref="FillPreviewWindow"/>.
///
/// La ventana no concluye nada. Enseña dos columnas —lo que hay en SAP y de qué consulta es el
/// trabajo— y pregunta. Si no encontró ninguna pista de identidad lo dice tal cual, en vez de
/// callarse y dejar que el silencio parezca conformidad.
/// </summary>
public sealed class ExportApprovalWindow : Window
{
    private readonly TaskCompletionSource<bool> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF5));
    private static readonly Brush Dim = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Warn = new SolidColorBrush(Color.FromRgb(0xF2, 0xA5, 0x1F));
    private static readonly Brush Good = new SolidColorBrush(Color.FromRgb(0x5B, 0xD6, 0x7A));

    public ExportApprovalWindow(ExportApprovalRequest request)
    {
        Title = "Ü · confirmar paciente antes de exportar";
        Width = 640;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 780;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x18));

        var panel = new StackPanel { Margin = new Thickness(20) };

        panel.Children.Add(new TextBlock
        {
            Text = "¿Es este el paciente de la consulta?",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = Ink,
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Se va a escribir una nota clínica firmada en la historia que SAP tiene abierta. " +
                   "Ü no puede comprobar por su cuenta que sea la correcta.",
            FontSize = 11,
            Foreground = Dim,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 14),
        });

        foreach (string warning in request.Warnings) panel.Children.Add(Banner(warning));

        // ── Lo que SAP tiene delante ──────────────────────────────────────────
        panel.Children.Add(Heading("En el sistema hospitalario (ahora mismo)"));
        if (request.Patient.FoundAnything)
        {
            foreach (PatientClue clue in request.Patient.Clues)
                panel.Children.Add(Row(clue.Label, clue.Value, mono: true));
        }
        else
        {
            // Decirlo importa: «no encontré nada» no es «está bien». Sin esta línea, una pantalla
            // equivocada y una pantalla sin campos de paciente se verían exactamente igual.
            panel.Children.Add(new TextBlock
            {
                Text = "No se reconoció ningún campo de identidad del paciente en esta pantalla. " +
                       "Compruébalo tú directamente en SAP antes de aprobar.",
                Foreground = Warn,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6),
            });
        }
        panel.Children.Add(new TextBlock
        {
            Text = request.Patient.Screen,
            FontSize = 10,
            FontFamily = new FontFamily("Consolas"),
            Foreground = Dim,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 14),
        });

        // ── De qué consulta es el trabajo ─────────────────────────────────────
        panel.Children.Add(Heading("La consulta que se va a exportar"));
        foreach ((string label, string value) in request.Summary) panel.Children.Add(Row(label, value));

        // ── Qué se va a escribir ──────────────────────────────────────────────
        string text = (request.Payload.RenderedText ?? request.Payload.Context ?? "").Trim();
        if (text.Length > 0)
        {
            panel.Children.Add(Heading("Texto que se escribirá"));
            panel.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 6),
                Child = new ScrollViewer
                {
                    MaxHeight = 170,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = new TextBlock
                    {
                        Text = text,
                        Foreground = Ink,
                        FontSize = 11,
                        FontFamily = new FontFamily("Consolas"),
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
            });
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
        };
        buttons.Children.Add(MakeButton("No es este paciente", "#5A5A66", false));
        buttons.Children.Add(MakeButton("Sí, es este — escribir", "#2FB457", true));
        panel.Children.Add(buttons);

        panel.Children.Add(new TextBlock
        {
            Text = "Si no confirmas, la consulta queda «Requiere acción» en el portal y no se escribe nada.",
            FontSize = 10,
            Foreground = Dim,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
        });

        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel };

        // Cerrar con la X es NO aprobar. Nunca «escribir por si acaso».
        Closing += (_, __) => _answer.TrySetResult(false);
    }

    private static UIElement Heading(string text) => new TextBlock
    {
        Text = text.ToUpperInvariant(),
        FontSize = 10,
        FontWeight = FontWeights.SemiBold,
        Foreground = Dim,
        Margin = new Thickness(0, 6, 0, 6),
    };

    private static UIElement Banner(string text) => new Border
    {
        Background = new SolidColorBrush(Color.FromArgb(0x33, 0xF2, 0xA5, 0x1F)),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(10, 8, 10, 8),
        Margin = new Thickness(0, 0, 0, 8),
        Child = new TextBlock
        {
            Text = "⚠  " + text,
            Foreground = Ink,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
        },
    };

    private static UIElement Row(string label, string value, bool mono = false)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var name = new TextBlock { Text = label, Foreground = Dim, FontSize = 12 };
        var content = new TextBlock
        {
            Text = value,
            Foreground = mono ? Good : Ink,
            FontSize = 13,
            FontWeight = mono ? FontWeights.SemiBold : FontWeights.Normal,
            FontFamily = mono ? new FontFamily("Consolas") : SystemFonts.MessageFontFamily,
            TextWrapping = TextWrapping.Wrap,
        };

        Grid.SetColumn(name, 0);
        Grid.SetColumn(content, 1);
        grid.Children.Add(name);
        grid.Children.Add(content);
        return grid;
    }

    private Button MakeButton(string text, string hex, bool result)
    {
        var button = new Button
        {
            Content = text,
            Height = 34,
            MinWidth = 150,
            Margin = new Thickness(8, 0, 0, 0),
            FontSize = 12,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        button.Click += (_, __) => { _answer.TrySetResult(result); Close(); };
        return button;
    }

    /// <summary>true = escribir en SAP. Se resuelve con los botones o al cerrar la ventana.</summary>
    public Task<bool> AskAsync()
    {
        Show();
        Activate();
        return _answer.Task;
    }
}
