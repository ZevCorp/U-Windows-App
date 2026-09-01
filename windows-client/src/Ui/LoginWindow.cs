using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using U.WindowsClient.Cuenta;

namespace U.WindowsClient.Ui;

/// <summary>
/// EL LOGIN DE VERDAD: correo y contraseña contra la MISMA cuenta de Supabase del portal. Reemplaza
/// al popup de nombre+correo como identidad — aquel era una declaración, esto es una sesión.
/// </summary>
/// <remarks>
/// EL DISEÑO SE CONSERVA A PROPÓSITO (pedido por el usuario el 2026-09-01): la misma tarjeta
/// negra-azulada de <see cref="OnboardingWindow"/>, el triángulo, el acento azul eléctrico, las
/// esquinas de 18. Lo que cambia es lo que pide — contraseña en vez de nombre, porque el nombre ya
/// no se teclea: lo sabe la base (`profiles.full_name`) y de ahí se saluda.
///
/// Construida en código, sin XAML, por la misma razón que su antecesora: autocontenida.
/// </remarks>
public sealed class LoginWindow : Window
{
    private static readonly Regex EmailRe = new(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.Compiled);

    private readonly SesionMiracle _sesion;
    private readonly TextBox _email;
    private readonly PasswordBox _clave;
    private readonly Button _entrar;
    private readonly TextBlock _fallo;

    private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0xEA, 0xF2, 0xFF));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromArgb(0x9E, 0xC8, 0xDC, 0xFF));
    private static readonly Brush Blue = new SolidColorBrush(Color.FromRgb(0x4C, 0x8D, 0xFF));
    private static readonly Brush FieldBg = new SolidColorBrush(Color.FromRgb(0x0A, 0x12, 0x22));
    private static readonly Brush Line = new SolidColorBrush(Color.FromArgb(0x40, 0x60, 0x94, 0xEB));
    private static readonly Brush Danger = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x8A));

    public LoginWindow(SesionMiracle sesion)
    {
        _sesion = sesion;

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
            Text = "Entra a tu cuenta",
            Foreground = Ink,
            FontSize = 21,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        };
        var subtitle = new TextBlock
        {
            Text = "La misma cuenta con la que entras a Miracle en la web. Tus consultas quedan en el mismo sitio.",
            Foreground = Muted,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 22)
        };

        _email = new TextBox
        {
            Height = 40,
            Background = Brushes.Transparent,
            Foreground = Ink,
            CaretBrush = Blue,
            BorderThickness = new Thickness(0),
            FontSize = 14,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _clave = new PasswordBox
        {
            Height = 40,
            Background = Brushes.Transparent,
            Foreground = Ink,
            CaretBrush = Blue,
            BorderThickness = new Thickness(0),
            FontSize = 14,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _email.TextChanged += (_, __) => Validar();
        _clave.PasswordChanged += (_, __) => Validar();

        // El fallo se dice DENTRO de la tarjeta, en su sitio fijo: un MessageBox encima del login
        // rompería justo la estética que se quiso conservar.
        _fallo = new TextBlock
        {
            Text = "",
            Foreground = Danger,
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(2, 2, 0, 0)
        };

        stack.Children.Add(mark);
        stack.Children.Add(title);
        stack.Children.Add(subtitle);
        stack.Children.Add(FieldLabel("CORREO"));
        stack.Children.Add(Wrap(_email));
        stack.Children.Add(FieldLabel("CONTRASEÑA"));
        stack.Children.Add(Wrap(_clave));
        stack.Children.Add(_fallo);

        _entrar = new Button
        {
            Content = "Entrar",
            Height = 44,
            Margin = new Thickness(0, 20, 0, 0),
            Foreground = Brushes.White,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand,
            IsEnabled = false,
            Opacity = 0.5,
            BorderThickness = new Thickness(0),
            Background = Blue,
            Template = RoundedButtonTemplate()
        };
        _entrar.Click += async (_, __) => await EntrarAsync();
        stack.Children.Add(_entrar);

        card.Child = stack;
        Content = card;

        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        Loaded += (_, __) => _email.Focus();
        KeyDown += async (_, e) => { if (e.Key == Key.Enter && _entrar.IsEnabled) await EntrarAsync(); };
    }

    private void Validar()
    {
        bool ok = EmailRe.IsMatch(_email.Text.Trim()) && _clave.Password.Length > 0;
        _entrar.IsEnabled = ok;
        _entrar.Opacity = ok ? 1.0 : 0.5;
    }

    private async Task EntrarAsync()
    {
        _entrar.IsEnabled = false;
        _entrar.Content = "Entrando…";
        _fallo.Visibility = Visibility.Collapsed;
        try
        {
            bool dentro = await _sesion.EntrarAsync(_email.Text.Trim().ToLowerInvariant(), _clave.Password);
            if (dentro)
            {
                DialogResult = true;
                Close();
                return;
            }
            _fallo.Text = _sesion.UltimoFallo.Length > 0 ? _sesion.UltimoFallo : "No se pudo entrar.";
            _fallo.Visibility = Visibility.Visible;
        }
        finally
        {
            if (IsLoaded)
            {
                _entrar.Content = "Entrar";
                Validar();
            }
        }
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
