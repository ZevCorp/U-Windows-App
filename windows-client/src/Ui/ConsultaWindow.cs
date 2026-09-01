using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using U.Graph;
using U.WindowsClient.Clinical;
using U.WindowsClient.Clinical.Transcripcion;
using U.WindowsClient.Cuenta;
using U.WindowsClient.Diagnostics;
using U.WindowsClient.Voice;

namespace U.WindowsClient.Ui;

/// <summary>
/// LA CONSULTA, EN UNA VENTANA. Dos pestañas arriba —Consultas y Nota—, el botón grabar abajo, y
/// nada más.
/// </summary>
/// <remarks>
/// SIN CHROME DE WINDOWS (rediseño del 2026-09-01, pedido por el usuario). La barra de título del
/// sistema y sus tres botones sobraban: esta ventana es un botón grande, no un documento. Se
/// dibujan los propios —cerrar y minimizar— y NO hay maximizar: una ventana que quiere ser una
/// pastilla en una esquina no tiene nada que hacer a pantalla completa, y el botón invitaría a
/// romper el radio.
///
/// EL RADIO ES MUY PRONUNCIADO Y ESO CUESTA UN DETALLE TÉCNICO: con `WindowStyle.None` +
/// `AllowsTransparency` el marco lo pinta la app entera, así que arrastrar, cerrar y el borde
/// redondeado son código nuestro. A cambio, el borde es exactamente el del boceto.
///
/// LAS DOS PESTAÑAS CAMBIAN LA SUPERFICIE, no abren ventanas:
///   · **Consultas** — las anteriores, leídas de la MISMA tabla que lista el portal.
///   · **Nota** — el texto en vivo mientras se habla; al parar, la nota organizada.
///
/// NADIE ELIGE PLANTILLA (promesa 94). El selector de 204 plantillas era el primer obstáculo de una
/// pantalla que quiere ser un botón: se resuelve sola con <see cref="PlantillaAbierta"/>, y si no
/// existe se crea la primera vez.
///
/// ESTA VENTANA NO DECIDE NADA. Todo lo que pesa vive en clases que el contrato juzga sin pantalla:
/// <see cref="SesionMiracle"/> (84-86, 90), <see cref="Consulta"/> (84, 91),
/// <see cref="DictadoEnVivo"/> (88, 89), <see cref="EspejoDeConsulta"/> (93) y
/// <see cref="PlantillaAbierta"/> (94). Aquí solo se pinta y se conecta.
/// </remarks>
public sealed class ConsultaWindow : Window
{
    private readonly SesionMiracle _sesion;
    private readonly ClinicaClient _clinica;
    private readonly LiveAudio _audio;
    private readonly DictadoEnVivo _dictado;
    private readonly Consulta _consulta;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly TextBlock _quien;
    private readonly Button _tabConsultas;
    private readonly Button _tabNota;
    private readonly ScrollViewer _superficie;
    private readonly StackPanel _listaConsultas;
    private readonly StackPanel _panelNota;
    private readonly TextBlock _vivo;
    private readonly StackPanel _nota;
    private readonly Button _grabar;
    private readonly TextBlock _estado;

    private string _plantillaId = "";
    private string _plantillaNombre = "";
    private bool _enNota = true;

    private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0xEA, 0xF2, 0xFF));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromArgb(0x9E, 0xC8, 0xDC, 0xFF));
    private static readonly Brush Tenue = new SolidColorBrush(Color.FromArgb(0x55, 0xC8, 0xDC, 0xFF));
    private static readonly Brush Blue = new SolidColorBrush(Color.FromRgb(0x4C, 0x8D, 0xFF));
    private static readonly Brush Rec = new SolidColorBrush(Color.FromRgb(0xE8, 0x4C, 0x5C));
    private static readonly Brush Tinta = new SolidColorBrush(Color.FromRgb(0x0D, 0x12, 0x1E));
    private static readonly Brush Line = new SolidColorBrush(Color.FromArgb(0x33, 0x60, 0x94, 0xEB));

    public ConsultaWindow(SesionMiracle sesion, GraphConfig graphConfig)
    {
        _sesion = sesion;
        _clinica = new ClinicaClient(graphConfig.BaseUrl, sesion);
        _audio = new LiveAudio();
        _dictado = new DictadoEnVivo(graphConfig, _audio, sesion);
        _consulta = new Consulta(sesion, _clinica,
            abrirMicrofono: ct => _dictado.ArrancarAsync(ct),
            pararYRecogerLoDicho: () => _dictado.PararAsync(),
            espejar: async (encounterId, nota, verbatim) =>
            {
                string fila = EspejoDeConsulta.Fila(encounterId, nota, verbatim,
                    _plantillaNombre, PlantillaAbierta.Especialidad, DateTimeOffset.UtcNow);
                return await EspejoDeConsulta.EscribirAsync(_sesion, _http, fila);
            });

        // ── el marco ─────────────────────────────────────────────────────────
        Title = "Miracle";
        Width = 460;
        Height = 640;
        MinWidth = 380;
        MinHeight = 520;
        WindowStyle = WindowStyle.None;      // sin barra de título ni botones del sistema
        ResizeMode = ResizeMode.CanResize;   // sin maximizar: ver ChromeHook más abajo
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var tarjeta = new Border
        {
            // EL RADIO DEL BOCETO. 44 y no 18: la forma es casi una pastilla, y eso es lo que hace
            // que no parezca una ventana.
            CornerRadius = new CornerRadius(44),
            Background = new LinearGradientBrush(
                Color.FromRgb(0x07, 0x0C, 0x18), Color.FromRgb(0x02, 0x04, 0x0A), 90),
            BorderBrush = Line,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(16),
            Effect = new DropShadowEffect { BlurRadius = 44, ShadowDepth = 0, Opacity = 0.6, Color = Colors.Black },
        };

        var raiz = new Grid { Margin = new Thickness(26, 20, 26, 26) };
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // cabecera
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // pestañas
        raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });  // superficie
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // estado
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // grabar

        // ── cabecera: quién + los botones PROPIOS ────────────────────────────
        var cabecera = new DockPanel { Margin = new Thickness(4, 0, 0, 18) };

        var botonera = new StackPanel { Orientation = Orientation.Horizontal };
        var minimizar = BotonDeMarco("─", "Minimizar");
        minimizar.Click += (_, __) => WindowState = WindowState.Minimized;
        var cerrar = BotonDeMarco("✕", "Cerrar");
        cerrar.Click += (_, __) => Close();
        botonera.Children.Add(minimizar);
        botonera.Children.Add(cerrar);
        DockPanel.SetDock(botonera, Dock.Right);

        _quien = new TextBlock
        {
            Foreground = Ink, FontSize = 14, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        cabecera.Children.Add(botonera);
        cabecera.Children.Add(_quien);
        Grid.SetRow(cabecera, 0);
        raiz.Children.Add(cabecera);

        // ── las dos pestañas ─────────────────────────────────────────────────
        var pestanas = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 18),
        };
        _tabConsultas = Pestana("Consultas");
        _tabNota = Pestana("Nota");
        _tabConsultas.Click += async (_, __) => { Mostrar(nota: false); await CargarConsultasAsync(); };
        _tabNota.Click += (_, __) => Mostrar(nota: true);
        pestanas.Children.Add(_tabConsultas);
        pestanas.Children.Add(_tabNota);
        Grid.SetRow(pestanas, 1);
        raiz.Children.Add(pestanas);

        // ── la superficie que cambian las pestañas ───────────────────────────
        _listaConsultas = new StackPanel { Visibility = Visibility.Collapsed };
        _vivo = new TextBlock
        {
            Foreground = Ink, FontSize = 15, TextWrapping = TextWrapping.Wrap, LineHeight = 23,
            Margin = new Thickness(4, 0, 4, 8),
        };
        _nota = new StackPanel();
        _panelNota = new StackPanel();
        _panelNota.Children.Add(_vivo);
        _panelNota.Children.Add(_nota);

        var superficie = new StackPanel();
        superficie.Children.Add(_panelNota);
        superficie.Children.Add(_listaConsultas);
        _superficie = new ScrollViewer
        {
            Content = superficie,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        Grid.SetRow(_superficie, 2);
        raiz.Children.Add(_superficie);

        // ── estado ───────────────────────────────────────────────────────────
        _estado = new TextBlock
        {
            Foreground = Tenue, FontSize = 11.5, TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center, Margin = new Thickness(8, 12, 8, 10),
        };
        Grid.SetRow(_estado, 3);
        raiz.Children.Add(_estado);

        // ── el botón, abajo y en pastilla ────────────────────────────────────
        _grabar = new Button
        {
            Content = "Grabar",
            Width = 150, Height = 76,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = Brushes.White, Background = Tinta,
            BorderThickness = new Thickness(0),
            FontSize = 17, FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand,
            Template = Pastilla(38),
            Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 0, Opacity = 0.4, Color = Colors.Black },
        };
        _grabar.Click += async (_, __) => await AlternarAsync();
        Grid.SetRow(_grabar, 4);
        raiz.Children.Add(_grabar);

        tarjeta.Child = raiz;
        Content = tarjeta;

        // Sin barra de título, arrastrar es cosa nuestra. Los botones se tragan su clic.
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) WindowState = WindowState.Minimized; };
        // NO HAY MAXIMIZAR, y no basta con no dibujar el botón: Win+↑ sigue maximizando y la
        // pastilla se convertiría en una pantalla completa con las esquinas redondeadas flotando.
        // Se devuelve al tamaño normal en cuanto pasa.
        StateChanged += (_, __) => { if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal; };

        _dictado.Parcial += t => Dispatcher.BeginInvoke(() =>
        {
            _vivo.Text = t;
            if (_enNota) _superficie.ScrollToEnd();
        });
        _dictado.Fallo += m => Dispatcher.BeginInvoke(() => Estado("Dictado: " + m));
        _consulta.Cambio += _ => Dispatcher.BeginInvoke(PintarSegunEstado);

        Mostrar(nota: true);
        Loaded += async (_, __) => await ArrancarAsync();
        Closed += (_, __) => { _dictado.Dispose(); _audio.Dispose(); _http.Dispose(); };
    }

    // ── arranque ─────────────────────────────────────────────────────────────

    private async Task ArrancarAsync()
    {
        _quien.Text = _sesion.MedicoNombre.Length > 0 ? _sesion.MedicoNombre : _sesion.MedicoEmail;
        Estado("Preparando…");
        try
        {
            var catalogo = await _clinica.PlantillasAsync();
            var abierta = PlantillaAbierta.Elegir(catalogo);

            // No existe todavía: se crea una vez y queda para siempre. NO se cae a una del
            // catálogo — elegir por el médico sin decírselo es peor que preguntarle (promesa 94).
            abierta ??= await _clinica.CrearPlantillaAsync(
                PlantillaAbierta.Nombre, PlantillaAbierta.Especialidad, PlantillaAbierta.Secciones());

            _plantillaId = abierta.Id;
            _plantillaNombre = abierta.Nombre;
            Estado("Listo. Pulsa grabar y habla.");
        }
        catch (ErrorClinico e) { Estado(e.Message); }
        catch (Exception e)
        {
            Estado("No se pudo hablar con el backend clínico.");
            LogBus.Log("consulta-ui", $"arranque: {e.Message}");
        }
    }

    // ── las pestañas ─────────────────────────────────────────────────────────

    private void Mostrar(bool nota)
    {
        _enNota = nota;
        _panelNota.Visibility = nota ? Visibility.Visible : Visibility.Collapsed;
        _listaConsultas.Visibility = nota ? Visibility.Collapsed : Visibility.Visible;
        PintarPestanas();
        _superficie.ScrollToHome();
    }

    private void PintarPestanas()
    {
        _tabNota.Background = _enNota ? Blue : Brushes.Transparent;
        _tabNota.Foreground = _enNota ? Brushes.White : Muted;
        _tabConsultas.Background = _enNota ? Brushes.Transparent : Blue;
        _tabConsultas.Foreground = _enNota ? Muted : Brushes.White;
    }

    private async Task CargarConsultasAsync()
    {
        _listaConsultas.Children.Clear();
        _listaConsultas.Children.Add(Linea("Cargando…", Tenue, 12.5));
        var previas = await EspejoDeConsulta.UltimasAsync(_sesion, _http);
        _listaConsultas.Children.Clear();

        if (previas.Count == 0)
        {
            // VACÍO NO ES FALLO, y se distingue: aquí se dice que no hay ninguna, no «no se pudo».
            _listaConsultas.Children.Add(Linea(
                "Todavía no hay consultas. La primera que grabes aparece aquí y en el portal.",
                Tenue, 12.5));
            return;
        }
        foreach (var c in previas) _listaConsultas.Children.Add(FilaDeConsulta(c));
    }

    // ── grabar / parar ───────────────────────────────────────────────────────

    private async Task AlternarAsync()
    {
        _grabar.IsEnabled = false;
        try
        {
            if (_consulta.Estado == EstadoDeConsulta.Grabando)
            {
                Estado("Guardando y organizando la nota…");
                await _consulta.TerminarAsync();
                return;
            }

            if (_plantillaId.Length == 0) { Estado("Todavía no está lista la plantilla."); return; }

            Mostrar(nota: true);
            _nota.Children.Clear();
            _vivo.Text = "";
            Estado("Abriendo la consulta…");
            if (!await _consulta.EmpezarAsync(_plantillaId)) Estado(_consulta.Motivo);
        }
        finally { _grabar.IsEnabled = true; }
    }

    private void PintarSegunEstado()
    {
        switch (_consulta.Estado)
        {
            case EstadoDeConsulta.Grabando:
                _grabar.Content = "Parar";
                _grabar.Background = Rec;
                Estado("Escuchando…");
                break;
            case EstadoDeConsulta.GenerandoNota:
                _grabar.Content = "Grabar";
                _grabar.Background = Tinta;
                Estado("Organizando la nota…");
                break;
            case EstadoDeConsulta.NotaLista:
                _grabar.Content = "Grabar";
                _grabar.Background = Tinta;
                PintarNota();
                break;
            case EstadoDeConsulta.Fallida:
                _grabar.Content = "Grabar";
                _grabar.Background = Tinta;
                Estado(_consulta.Motivo);
                break;
            default:
                _grabar.Content = "Grabar";
                _grabar.Background = Tinta;
                break;
        }
    }

    private void PintarNota()
    {
        var nota = _consulta.Nota;
        _nota.Children.Clear();
        _vivo.Text = "";
        if (nota == null) { Estado("La nota volvió vacía."); return; }

        // SE DICE SI SE VIO O NO EN EL PORTAL. Suponerlo era justo el hueco que tenía esta app
        // hasta hoy (promesa 93).
        Estado(_consulta.VisibleEnElPortal
            ? "Nota lista. Ya se ve en el portal."
            : "Nota lista y guardada, pero NO se pudo espejar al portal: revisa el log.");

        if (nota.Resumen.Length > 0) _nota.Children.Add(Tarjeta("RESUMEN", nota.Resumen));
        foreach (var s in nota.Secciones)
        {
            if (s.Contenido.Trim().Length == 0) continue;   // una casilla vacía no es información
            _nota.Children.Add(Tarjeta(s.Titulo.ToUpperInvariant(), s.Contenido));
        }
        if (nota.Avisos.Count > 0)
            _nota.Children.Add(Tarjeta("AVISOS", string.Join("\n", nota.Avisos)));
        _superficie.ScrollToHome();
    }

    // ── piezas ───────────────────────────────────────────────────────────────

    private void Estado(string texto) => _estado.Text = texto;

    private static Button BotonDeMarco(string glifo, string queHace) => new()
    {
        Content = glifo,
        Width = 30, Height = 30, Margin = new Thickness(6, 0, 0, 0),
        Foreground = Muted, Background = Brushes.Transparent,
        BorderThickness = new Thickness(0), FontSize = 12,
        Cursor = Cursors.Hand, ToolTip = queHace,
        Template = Pastilla(15),
    };

    private static Button Pestana(string texto) => new()
    {
        Content = texto,
        Height = 38, MinWidth = 118, Margin = new Thickness(5, 0, 5, 0),
        Foreground = Muted, Background = Brushes.Transparent,
        BorderThickness = new Thickness(0), FontSize = 13.5,
        Cursor = Cursors.Hand,
        Template = Pastilla(19),
    };

    private static TextBlock Linea(string texto, Brush color, double tamano) => new()
    {
        Text = texto, Foreground = color, FontSize = tamano,
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 6, 4, 6),
    };

    private static Border FilaDeConsulta(ConsultaVista c)
    {
        var pila = new StackPanel();
        pila.Children.Add(new TextBlock
        {
            Text = c.Fecha == DateTimeOffset.MinValue ? "" : c.Fecha.ToLocalTime().ToString("d MMM · HH:mm"),
            Foreground = Tenue, FontSize = 10.5, Margin = new Thickness(0, 0, 0, 4),
        });
        pila.Children.Add(new TextBlock
        {
            Text = c.Motivo.Length > 0 ? c.Motivo : (c.Resumen.Length > 0 ? c.Resumen : "Consulta sin motivo anotado"),
            Foreground = Ink, FontSize = 13.5, TextWrapping = TextWrapping.Wrap,
        });
        if (c.Estado.Length > 0)
        {
            pila.Children.Add(new TextBlock
            {
                Text = c.Estado, Foreground = Muted, FontSize = 10.5, Margin = new Thickness(0, 5, 0, 0),
            });
        }
        return new Border
        {
            CornerRadius = new CornerRadius(16),
            Background = Tinta, BorderBrush = Line, BorderThickness = new Thickness(1),
            Padding = new Thickness(15, 12, 15, 12), Margin = new Thickness(2, 0, 2, 9),
            Child = pila,
        };
    }

    private static Border Tarjeta(string titulo, string cuerpo)
    {
        var pila = new StackPanel();
        pila.Children.Add(new TextBlock
        {
            Text = titulo, Foreground = Muted, FontSize = 10,
            FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6),
        });
        pila.Children.Add(new TextBlock
        {
            Text = cuerpo, Foreground = Ink, FontSize = 13.5,
            TextWrapping = TextWrapping.Wrap, LineHeight = 21,
        });
        return new Border
        {
            CornerRadius = new CornerRadius(16),
            Background = Tinta, BorderBrush = Line, BorderThickness = new Thickness(1),
            Padding = new Thickness(15, 13, 15, 13), Margin = new Thickness(2, 0, 2, 9),
            Child = pila,
        };
    }

    private static ControlTemplate Pastilla(double radio)
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radio));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        template.VisualTree = border;
        return template;
    }
}
