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
    private readonly TextBox _nombre;
    private readonly TextBox _email;
    private readonly PasswordBox _clave;
    private readonly Border _cajaNombre;
    private readonly TextBlock _rotuloNombre;
    private readonly TextBlock _titulo;
    private readonly TextBlock _bajada;
    private readonly Button _entrar;
    private readonly Button _cambiarModo;
    private readonly TextBlock _fallo;

    /// <summary>En modo alta se pide el nombre y el boton crea la cuenta.</summary>
    private bool _creando;

    /// <param name="sesion">La sesión en la que entrar o darse de alta.</param>
    /// <param name="empezarCreando">
    /// Abre directo en el modo de crear cuenta. Lo usa el selector de cuenta de la consulta cuando
    /// se pulsa «Agregar cuenta»: no tiene sentido aterrizar en «Entrar» para tener que cambiar de
    /// modo a mano justo después de haber pedido lo contrario.
    /// </param>
    public LoginWindow(SesionMiracle sesion, bool empezarCreando = false)
    {
        _sesion = sesion;

        Title = "Miracle";
        // La letra de Miracle (spec 054), como la ventana de la nota.
        FontFamily = Estudio.FuenteCuerpo;
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
            CornerRadius = new CornerRadius(28),
            Background = Estudio.Fondo,
            BorderBrush = Estudio.Borde,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0),
        };

        var pila = new StackPanel { Margin = new Thickness(32, 26, 32, 28) };

        // ── el marco: solo cerrar. Aquí no hay nada que minimizar todavía ────
        var cerrar = new Button
        {
            Content = Estudio.Icono("x", 15, Estudio.TintaMedia),
            Width = 30, Height = 30,
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Template = Estudio.Pastilla(15),
            Margin = new Thickness(0, -6, -6, 2),
        };
        cerrar.Click += (_, __) => { DialogResult = false; Close(); };
        cerrar.MouseEnter += (_, __) => cerrar.Background = Estudio.HieloSuave;
        cerrar.MouseLeave += (_, __) => cerrar.Background = Brushes.Transparent;
        pila.Children.Add(cerrar);

        // ── marca ────────────────────────────────────────────────────────────
        // EL ORBE DE MIRACLE Y NO EL TRIÁNGULO (spec 054): lo primero que ve el médico es la misma
        // marca que en la web.
        var marca = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 20) };
        marca.Children.Add(Estudio.Orbe(40));
        marca.Children.Add(new TextBlock
        {
            Text = string.Join('\u2009', "Miracle".ToCharArray()),
            FontFamily = Estudio.FuenteCuerpo,
            FontWeight = FontWeights.ExtraLight,
            FontSize = 19,
            Foreground = Estudio.TintaFuerte,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 2),
        });
        pila.Children.Add(marca);

        _titulo = new TextBlock
        {
            Foreground = Estudio.TintaFuerte,
            FontFamily = Estudio.FuenteTitulo,
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 7),
        };
        _bajada = new TextBlock
        {
            Foreground = Estudio.TintaMedia,
            FontSize = 13,
            LineHeight = 19,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 24),
        };
        pila.Children.Add(_titulo);
        pila.Children.Add(_bajada);

        // ── los campos ───────────────────────────────────────────────────────
        _nombre = new TextBox
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
        _nombre.TextChanged += (_, __) => Validar();
        _email.TextChanged += (_, __) => Validar();
        _clave.PasswordChanged += (_, __) => Validar();

        _rotuloNombre = Estudio.Rotulo("Nombre completo");
        _cajaNombre = Caja(_nombre);
        pila.Children.Add(_rotuloNombre);
        pila.Children.Add(_cajaNombre);
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
            Height = 48,
            Margin = new Thickness(0, 22, 0, 0),
            Foreground = Brushes.White,
            // El primario de Miracle: el degradado del azul un punto más claro (spec 054).
            Background = Estudio.AcentoDegradado,
            BorderThickness = new Thickness(0),
            FontSize = 14.5,
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand,
            IsEnabled = false,
            Opacity = 0.45,
            Template = Estudio.Pastilla(24),
        };
        _entrar.ConRelieve(Estudio.Sombra1);
        _entrar.MouseEnter += (_, __) => { if (_entrar.IsEnabled) _entrar.Background = Estudio.AcentoDegradadoEncima; };
        _entrar.MouseLeave += (_, __) => _entrar.Background = Estudio.AcentoDegradado;
        _entrar.Click += async (_, __) => await ConfirmarAsync();
        pila.Children.Add(_entrar);

        // El cambio de modo es texto y no boton macizo: es la salida secundaria y no debe competir
        // con la accion principal, que en esta tarjeta es la unica que importa.
        _cambiarModo = new Button
        {
            Height = 34,
            Margin = new Thickness(0, 12, 0, 0),
            Foreground = Estudio.Acento,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 12.5,
            Cursor = Cursors.Hand,
            Template = Estudio.Pastilla(17),
        };
        _cambiarModo.Click += (_, __) => Modo(!_creando);
        pila.Children.Add(_cambiarModo);

        tarjeta.Child = pila;
        var marco = Estudio.Elevar(tarjeta, Estudio.Sombra3);
        marco.Margin = new Thickness(22, 18, 22, 26);   // sitio para la sombra
        Content = marco;
        this.Nitida();

        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        Loaded += (_, __) => _email.Focus();
        KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && _entrar.IsEnabled) await ConfirmarAsync();
            if (e.Key == Key.Escape) { DialogResult = false; Close(); }
        };

        Modo(creando: empezarCreando);
    }

    // ── los dos modos ────────────────────────────────────────────────────────

    private void Modo(bool creando)
    {
        _creando = creando;
        _titulo.Text = creando ? "Crea tu cuenta" : "Entra a tu cuenta";
        _bajada.Text = creando
            ? "Con esta cuenta entras tambien al portal en la web. Las consultas quedan en el mismo sitio."
            : "La misma con la que entras a Miracle en la web. Tus consultas quedan en el mismo sitio.";
        _entrar.Content = creando ? "Crear cuenta" : "Entrar";
        _cambiarModo.Content = creando ? "¿Ya tienes cuenta? Entra" : "¿No tienes cuenta? Crea una";

        var visible = creando ? Visibility.Visible : Visibility.Collapsed;
        _rotuloNombre.Visibility = visible;
        _cajaNombre.Visibility = visible;

        // EL CORREO ESCRITO NO SE PIERDE al cambiar de modo: quien viene a entrar y descubre que no
        // tiene cuenta ya lo habia tecleado, y volver a pedirselo es castigarle por equivocarse.
        _fallo.Visibility = Visibility.Collapsed;
        Validar();
        if (creando && _nombre.Text.Length == 0) _nombre.Focus();
    }

    private void Validar()
    {
        bool ok = EmailRe.IsMatch(_email.Text.Trim()) && _clave.Password.Length > 0
                  && (!_creando || _nombre.Text.Trim().Length > 0);
        _entrar.IsEnabled = ok;
        _entrar.Opacity = ok ? 1.0 : 0.45;
    }

    /// <summary>
    /// Confirma lo que toque segun el modo. El alta tiene TRES finales y los tres se dicen
    /// distinto (promesa 97); el de en medio -creada pero sin confirmar- es el que engana, porque
    /// Supabase contesta "ok" igual que cuando la cuenta queda lista.
    /// </summary>
    private async Task ConfirmarAsync()
    {
        _entrar.IsEnabled = false;
        _cambiarModo.IsEnabled = false;
        _entrar.Content = _creando ? "Creando…" : "Entrando…";
        _fallo.Visibility = Visibility.Collapsed;
        try
        {
            string email = _email.Text.Trim().ToLowerInvariant();

            if (!_creando)
            {
                if (await _sesion.EntrarAsync(email, _clave.Password)) { DialogResult = true; Close(); return; }
                Decir(_sesion.UltimoFallo.Length > 0 ? _sesion.UltimoFallo : "No se pudo entrar.", malo: true);
                return;
            }

            var alta = await _sesion.CrearCuentaAsync(_nombre.Text, email, _clave.Password);
            switch (alta)
            {
                case ResultadoDeAlta.Entro:
                    DialogResult = true;
                    Close();
                    return;

                case ResultadoDeAlta.FaltaConfirmar:
                    // NO SE ENTRA. Se vuelve al modo de entrar con el correo ya puesto: cuando el
                    // medico vuelva del enlace solo tendra que escribir la contrasena.
                    // SE DICEN LAS DOS POSIBILIDADES, y no es palabrería: Supabase contesta lo
                    // MISMO cuando la cuenta es nueva y cuando ya existía, a propósito, para no
                    // revelar qué correos están registrados. El 2026-09-01 el usuario probó con un
                    // correo que ya tenía cuenta desde junio: el log dijo «user_repeated_signup»,
                    // no se mandó ningún correo —no había nada que confirmar— y este mensaje le
                    // dejó esperando un email que no iba a llegar nunca.
                    //
                    // No se puede distinguir desde aquí; lo que sí se puede es no dar por segura
                    // una de las dos.
                    Modo(creando: false);
                    Decir("Listo. Si el correo era nuevo, te llega un enlace para confirmarlo. "
                        + "Si ya tenías cuenta con él, no llega nada: entra con tu contraseña.",
                        malo: false);
                    return;

                default:
                    Decir(_sesion.UltimoFallo.Length > 0 ? _sesion.UltimoFallo
                        : "No se pudo crear la cuenta.", malo: true);
                    return;
            }
        }
        finally
        {
            if (IsLoaded)
            {
                _entrar.Content = _creando ? "Crear cuenta" : "Entrar";
                _cambiarModo.IsEnabled = true;
                Validar();
            }
        }
    }

    /// <summary>
    /// Lo que hay que decir. EL COLOR SEPARA un tropiezo de una buena noticia: "revisa tu correo"
    /// en rojo se lee como un error, y quien lo lea creera que su alta fallo.
    /// </summary>
    private void Decir(string texto, bool malo)
    {
        _fallo.Text = texto;
        _fallo.Foreground = malo ? Estudio.Alerta : Estudio.Acento;
        _fallo.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// La caja de un campo. HUNDIDA y no elevada: un campo de texto es un hueco donde se escribe,
    /// no un objeto que sobresale — darle la sombra de un botón invitaría a pulsarlo.
    /// </summary>
    private static Border Caja(UIElement dentro) => new()
    {
        CornerRadius = new CornerRadius(Estudio.RadioChico),
        Background = Estudio.Superficie,
        BorderBrush = Estudio.BordeFuerte,
        BorderThickness = new Thickness(1),
        Padding = new Thickness(14, 1, 14, 1),
        Margin = new Thickness(0, 0, 0, 16),
        Child = dentro,
    };
}
