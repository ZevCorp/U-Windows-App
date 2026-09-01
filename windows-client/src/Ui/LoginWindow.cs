using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using U.WindowsClient.Cuenta;

namespace U.WindowsClient.Ui;

/// <summary>
/// EL LOGIN: correo y contraseña contra la MISMA cuenta de Supabase del portal. Reemplaza al popup
/// de nombre+correo como identidad — aquel era una declaración, esto es una sesión.
/// </summary>
/// <remarks>
/// LA FORMA SE CONSERVA DEL POPUP QUE LE GUSTÓ AL USUARIO —tarjeta única, marca arriba, dos campos,
/// un botón ancho— y lo que cambia es la piel: el estudio claro de <see cref="Estudio"/>, igual que
/// el resto de la app. Y lo que pide: contraseña en vez de nombre, porque el nombre ya no se
/// teclea — lo sabe la base (`profiles.full_name`) y de ahí se saluda.
///
/// EL FALLO SE DICE DENTRO DE LA TARJETA, en su sitio fijo bajo los campos. Un MessageBox encima
/// rompería justo la estética que se quiso conservar, y además tapa el formulario que hay que
/// corregir.
///
/// SIN CHROME DE WINDOWS, como la ventana de consulta: es una tarjeta, no un documento.
/// </remarks>
public sealed class LoginWindow : Window
{
    private static readonly Regex EmailRe = new(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.Compiled);

    private readonly SesionMiracle _sesion;
    private readonly TextBox _email;
    private readonly PasswordBox _clave;
    private readonly Button _entrar;
    private readonly TextBlock _fallo;

    public LoginWindow(SesionMiracle sesion)
    {
        _sesion = sesion;

        Title = "Miracle";
        Width = 452;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;

        var tarjeta = new Border
        {
            CornerRadius = new CornerRadius(34),
            Background = Estudio.Fondo,
            BorderBrush = Estudio.Borde,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0),
        };

        var pila = new StackPanel { Margin = new Thickness(32, 26, 32, 28) };

        // ── el marco: solo cerrar. Aquí no hay nada que minimizar todavía ────
        var cerrar = new Button
        {
            Content = new TextBlock
            {
                Text = "",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 9.5,
                Foreground = Estudio.TintaMedia,
            },
            Width = 30, Height = 30,
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            ToolTip = "Cerrar",
            Template = Estudio.Pastilla(15),
            Margin = new Thickness(0, -6, -6, 2),
        };
        cerrar.Click += (_, __) => { DialogResult = false; Close(); };
        cerrar.MouseEnter += (_, __) => cerrar.Background = Estudio.SuperficieSuave;
        cerrar.MouseLeave += (_, __) => cerrar.Background = Brushes.Transparent;
        pila.Children.Add(cerrar);

        // ── marca ────────────────────────────────────────────────────────────
        pila.Children.Add(new System.Windows.Shapes.Polygon
        {
            Points = new PointCollection { new Point(15, 2), new Point(28, 25), new Point(2, 25) },
            Stroke = Estudio.Acento,
            StrokeThickness = 1.8,
            Fill = Estudio.AcentoSuave,
            Width = 30, Height = 27,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 18),
        });

        pila.Children.Add(new TextBlock
        {
            Text = "Entra a tu cuenta",
            Foreground = Estudio.Tinta,
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 7),
        });
        pila.Children.Add(new TextBlock
        {
            Text = "La misma con la que entras a Miracle en la web. Tus consultas quedan en el mismo sitio.",
            Foreground = Estudio.TintaMedia,
            FontSize = 13,
            LineHeight = 19,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 24),
        });

        // ── los campos ───────────────────────────────────────────────────────
        _email = new TextBox
        {
            Height = 42,
            Background = Brushes.Transparent,
            Foreground = Estudio.Tinta,
            CaretBrush = Estudio.Acento,
            SelectionBrush = Estudio.Acento,
            BorderThickness = new Thickness(0),
            FontSize = 14,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _clave = new PasswordBox
        {
            Height = 42,
            Background = Brushes.Transparent,
            Foreground = Estudio.Tinta,
            CaretBrush = Estudio.Acento,
            SelectionBrush = Estudio.Acento,
            BorderThickness = new Thickness(0),
            FontSize = 14,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _email.TextChanged += (_, __) => Validar();
        _clave.PasswordChanged += (_, __) => Validar();

        pila.Children.Add(Estudio.Rotulo("Correo"));
        pila.Children.Add(Caja(_email));
        pila.Children.Add(Estudio.Rotulo("Contraseña"));
        pila.Children.Add(Caja(_clave));

        _fallo = new TextBlock
        {
            Foreground = Estudio.Alerta,
            FontSize = 12.5,
            LineHeight = 18,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(2, 2, 0, 0),
        };
        pila.Children.Add(_fallo);

        // ── entrar ───────────────────────────────────────────────────────────
        //
        // ESTE SÍ ES AZUL, y es la excepción que confirma la regla del estudio: en una tarjeta con
        // un solo camino posible, la acción principal se señala con color además de con relieve.
        // En la ventana de consulta no hace falta porque el botón de grabar está SOLO — aquí
        // compite con dos campos y un botón de cerrar.
        _entrar = new Button
        {
            Content = "Entrar",
            Height = 48,
            Margin = new Thickness(0, 22, 0, 0),
            Foreground = Brushes.White,
            Background = Estudio.Acento,
            BorderThickness = new Thickness(0),
            FontSize = 14.5,
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand,
            IsEnabled = false,
            Opacity = 0.45,
            Template = Estudio.Pastilla(24),
        };
        _entrar.ConRelieve(Estudio.Sombra1);
        _entrar.Click += async (_, __) => await EntrarAsync();
        pila.Children.Add(_entrar);

        tarjeta.Child = pila;
        var marco = Estudio.Elevar(tarjeta, Estudio.Sombra3);
        marco.Margin = new Thickness(22, 18, 22, 26);   // sitio para la sombra
        Content = marco;
        this.Nitida();

        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        Loaded += (_, __) => _email.Focus();
        KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && _entrar.IsEnabled) await EntrarAsync();
            if (e.Key == Key.Escape) { DialogResult = false; Close(); }
        };
    }

    private void Validar()
    {
        bool ok = EmailRe.IsMatch(_email.Text.Trim()) && _clave.Password.Length > 0;
        _entrar.IsEnabled = ok;
        _entrar.Opacity = ok ? 1.0 : 0.45;
    }

    private async Task EntrarAsync()
    {
        _entrar.IsEnabled = false;
        _entrar.Content = "Entrando…";
        _fallo.Visibility = Visibility.Collapsed;
        try
        {
            bool dentro = await _sesion.EntrarAsync(_email.Text.Trim().ToLowerInvariant(), _clave.Password);
            if (dentro) { DialogResult = true; Close(); return; }

            _fallo.Text = _sesion.UltimoFallo.Length > 0 ? _sesion.UltimoFallo : "No se pudo entrar.";
            _fallo.Visibility = Visibility.Visible;
        }
        finally
        {
            if (IsLoaded) { _entrar.Content = "Entrar"; Validar(); }
        }
    }

    /// <summary>
    /// La caja de un campo. HUNDIDA y no elevada: un campo de texto es un hueco donde se escribe,
    /// no un objeto que sobresale — darle la sombra de un botón invitaría a pulsarlo.
    /// </summary>
    private static Border Caja(UIElement dentro) => new()
    {
        CornerRadius = new CornerRadius(14),
        Background = Estudio.Superficie,
        BorderBrush = Estudio.Borde,
        BorderThickness = new Thickness(1),
        Padding = new Thickness(14, 1, 14, 1),
        Margin = new Thickness(0, 0, 0, 16),
        Child = dentro,
    };
}
