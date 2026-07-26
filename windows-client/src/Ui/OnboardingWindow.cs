using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace U.WindowsClient.Ui;

/// <summary>
/// Popup de bienvenida al instalar: captura NOMBRE y CORREO (sin contraseña por ahora). El correo es la
/// identidad canónica del usuario en "Windows Live". Construida en código (sin XAML) para ser
/// autocontenida; estética alineada al estudio Miracle: fondo negro-azulado, acento azul eléctrico.
/// </summary>
public sealed class OnboardingWindow : Window
{
    private static readonly Regex EmailRe = new(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.Compiled);

    private readonly TextBox _name;
    private readonly TextBox _email;
    private readonly Button _continue;

    public string EnteredName { get; private set; } = "";
    public string EnteredEmail { get; private set; } = "";

    private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0xEA, 0xF2, 0xFF));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromArgb(0x9E, 0xC8, 0xDC, 0xFF));
    private static readonly Brush Blue = new SolidColorBrush(Color.FromRgb(0x4C, 0x8D, 0xFF));
    private static readonly Brush FieldBg = new SolidColorBrush(Color.FromRgb(0x0A, 0x12, 0x22));
    private static readonly Brush Line = new SolidColorBrush(Color.FromArgb(0x40, 0x60, 0x94, 0xEB));

    public OnboardingWindow()
    {
        Title = "Miracle";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        Topmost = true;

        var card = new Border
        {
            CornerRadius = new CornerRadius(18),
            Background = new LinearGradientBrush(
                Color.FromRgb(0x07, 0x0C, 0x18), Color.FromRgb(0x02, 0x04, 0x0A), 90),
            BorderBrush = Line,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(18),
            Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 0, Opacity = 0.55, Color = Colors.Black }
        };

        var stack = new StackPanel { Margin = new Thickness(30, 28, 30, 26) };

        // marca (triángulo Miracle)
        var mark = new System.Windows.Shapes.Polygon
        {
            Points = new PointCollection { new Point(15, 2), new Point(28, 25), new Point(2, 25) },
            Stroke = Blue,
            StrokeThickness = 1.6,
            Fill = new SolidColorBrush(Color.FromArgb(0x22, 0x4C, 0x8D, 0xFF)),
            Width = 30, Height = 27,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 16)
        };

        var title = new TextBlock
        {
            Text = "Te damos la bienvenida",
            Foreground = Ink,
            FontSize = 21,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        };
        var subtitle = new TextBlock
        {
            Text = "Cuéntanos quién eres para personalizar tu experiencia y activar tu espacio.",
            Foreground = Muted,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 22)
        };

        _name = MakeField("Nombre completo");
        _email = MakeField("Correo electrónico");
        _name.TextChanged += (_, __) => Validate();
        _email.TextChanged += (_, __) => Validate();

        stack.Children.Add(mark);
        stack.Children.Add(title);
        stack.Children.Add(subtitle);
        stack.Children.Add(FieldLabel("NOMBRE"));
        stack.Children.Add(Wrap(_name));
        stack.Children.Add(FieldLabel("CORREO"));
        stack.Children.Add(Wrap(_email));

        _continue = new Button
        {
            Content = "Continuar",
            Height = 44,
            Margin = new Thickness(0, 20, 0, 0),
            Foreground = Brushes.White,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand,
            IsEnabled = false,
            BorderThickness = new Thickness(0),
            Background = Blue,
            Template = RoundedButtonTemplate()
        };
        _continue.Click += OnContinue;
        stack.Children.Add(_continue);

        card.Child = stack;
        Content = card;

        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        Loaded += (_, __) => { _name.Focus(); };
        KeyDown += (_, e) => { if (e.Key == Key.Enter && _continue.IsEnabled) OnContinue(this, new RoutedEventArgs()); };
    }

    private void Validate()
    {
        bool ok = !string.IsNullOrWhiteSpace(_name.Text) && EmailRe.IsMatch(_email.Text.Trim());
        _continue.IsEnabled = ok;
        _continue.Opacity = ok ? 1.0 : 0.5;
    }

    private void OnContinue(object sender, RoutedEventArgs e)
    {
        if (!(!string.IsNullOrWhiteSpace(_name.Text) && EmailRe.IsMatch(_email.Text.Trim()))) return;
        EnteredName = _name.Text.Trim();
        EnteredEmail = _email.Text.Trim().ToLowerInvariant();
        DialogResult = true;
        Close();
    }

    private static TextBlock FieldLabel(string text) => new()
    {
        Text = text,
        Foreground = Muted,
        FontSize = 10.5,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(2, 0, 0, 6)
    };

    private static Border Wrap(UIElement inner) => new()
    {
        CornerRadius = new CornerRadius(10),
        Background = FieldBg,
        BorderBrush = Line,
        BorderThickness = new Thickness(1),
        Padding = new Thickness(12, 2, 12, 2),
        Margin = new Thickness(0, 0, 0, 14),
        Child = inner
    };

    private static TextBox MakeField(string placeholder)
    {
        var tb = new TextBox
        {
            Height = 40,
            Background = Brushes.Transparent,
            Foreground = Ink,
            CaretBrush = Blue,
            BorderThickness = new Thickness(0),
            FontSize = 14,
            VerticalContentAlignment = VerticalAlignment.Center,
            Tag = placeholder
        };
        return tb;
    }

    private static ControlTemplate RoundedButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(11));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        template.VisualTree = border;
        return template;
    }
}
