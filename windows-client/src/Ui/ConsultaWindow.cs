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
/// LA CONSULTA, EN UNA VENTANA. Entrar → elegir plantilla → grabar → ver la transcripción en vivo →
/// parar → leer la nota organizada. Nada más, a propósito: es la cadena mínima para probar que la
/// consulta grabada aquí aparece en el portal.
/// </summary>
/// <remarks>
/// HIPER-MINIMALISTA ES UNA DECISIÓN, no una carencia: cuatro cosas en pantalla (quién, con qué
/// plantilla, el botón, lo que se va oyendo) y la nota al final. Editar, firmar y pacientes viven en
/// el portal; el día que hagan falta aquí, son llamadas que <see cref="ClinicaClient"/> ya sabe
/// hacer o aprenderá con su promesa delante.
///
/// ESTA VENTANA NO DECIDE NADA. Todo lo que pesa vive en clases que el contrato ya juzga sin
/// pantalla: <see cref="SesionMiracle"/> (84-86, 90), <see cref="Consulta"/> (84, 91),
/// <see cref="DictadoEnVivo"/> con sus lectores (88, 89) y <see cref="MensajesClinicos"/> (87).
/// Aquí solo se pinta y se conecta, que es exactamente lo que la compuerta prueba a mano (nivel 4).
///
/// LA NOTA SE PINTA PERO SU DESTINO NO ES PINTARSE: queda en <see cref="Consulta.Nota"/> tipada
/// clave→texto, que es la materia prima del cierre recuerdos→batches→SAP (spec siguiente).
/// </remarks>
public sealed class ConsultaWindow : Window
{
    private readonly SesionMiracle _sesion;
    private readonly ClinicaClient _clinica;
    private readonly LiveAudio _audio;
    private readonly DictadoEnVivo _dictado;
    private readonly Consulta _consulta;

    private readonly TextBlock _quien;
    private readonly ComboBox _plantillas;
    private readonly Button _boton;
    private readonly TextBlock _estado;
    private readonly TextBlock _vivo;
    private readonly StackPanel _nota;
    private readonly ScrollViewer _scrollNota;
    private readonly Button _reintentar;

    private IReadOnlyList<PlantillaClinica> _catalogo = Array.Empty<PlantillaClinica>();

    private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0xEA, 0xF2, 0xFF));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromArgb(0x9E, 0xC8, 0xDC, 0xFF));
    private static readonly Brush Blue = new SolidColorBrush(Color.FromRgb(0x4C, 0x8D, 0xFF));
    private static readonly Brush Rec = new SolidColorBrush(Color.FromRgb(0xE8, 0x4C, 0x5C));
    private static readonly Brush FieldBg = new SolidColorBrush(Color.FromRgb(0x0A, 0x12, 0x22));
    private static readonly Brush Line = new SolidColorBrush(Color.FromArgb(0x40, 0x60, 0x94, 0xEB));

    public ConsultaWindow(SesionMiracle sesion, GraphConfig graphConfig)
    {
        _sesion = sesion;
        _clinica = new ClinicaClient(graphConfig.BaseUrl, sesion);
        _audio = new LiveAudio();
        _dictado = new DictadoEnVivo(graphConfig, _audio, sesion);
        _consulta = new Consulta(sesion, _clinica,
            abrirMicrofono: ct => _dictado.ArrancarAsync(ct),
            pararYRecogerLoDicho: () => _dictado.PararAsync());

        Title = "Miracle — Consulta";
        Width = 520;
        Height = 640;
        MinWidth = 420;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new LinearGradientBrush(
            Color.FromRgb(0x07, 0x0C, 0x18), Color.FromRgb(0x02, 0x04, 0x0A), 90);

        var raiz = new Grid { Margin = new Thickness(26, 20, 26, 20) };
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });          // quién
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });          // plantilla
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });          // botón
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });          // estado
        raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // vivo / nota

        // ── quién ────────────────────────────────────────────────────────────
        var cabecera = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        _quien = new TextBlock
        {
            Foreground = Ink, FontSize = 15, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var salir = new Button
        {
            Content = "Salir", Foreground = Muted, Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), FontSize = 12, Cursor = Cursors.Hand,
            Padding = new Thickness(8, 4, 8, 4),
        };
        salir.Click += (_, __) => { _sesion.Salir(); Close(); };
        DockPanel.SetDock(salir, Dock.Right);
        cabecera.Children.Add(salir);
        cabecera.Children.Add(_quien);
        Grid.SetRow(cabecera, 0);
        raiz.Children.Add(cabecera);

        // ── plantilla ────────────────────────────────────────────────────────
        _plantillas = new ComboBox
        {
            Height = 38, FontSize = 13, Margin = new Thickness(0, 0, 0, 18),
            Background = FieldBg, Foreground = Colors(FieldBg), BorderBrush = Line,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(_plantillas, 1);
        raiz.Children.Add(_plantillas);

        // ── el botón: el 60 % de la ventana es esto ──────────────────────────
        _boton = new Button
        {
            Content = "●  GRABAR",
            Height = 96, FontSize = 22, FontWeight = FontWeights.Bold,
            Foreground = Brushes.White, Background = Blue, BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 0, 14),
            Template = Redondo(16),
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 0, Opacity = 0.35, Color = Color.FromRgb(0x4C, 0x8D, 0xFF) },
        };
        _boton.Click += async (_, __) => await AlternarAsync();
        Grid.SetRow(_boton, 2);
        raiz.Children.Add(_boton);

        // ── estado y reintento ───────────────────────────────────────────────
        var filaEstado = new DockPanel { Margin = new Thickness(2, 0, 2, 10) };
        _reintentar = new Button
        {
            Content = "Reintentar la nota", Visibility = Visibility.Collapsed,
            Foreground = Blue, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            FontSize = 12.5, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand,
        };
        _reintentar.Click += async (_, __) =>
        {
            _reintentar.Visibility = Visibility.Collapsed;
            Estado("Reintentando la nota…");
            if (await _consulta.ReintentarNotaAsync()) PintarNota();
            else PintarFallo();
        };
        DockPanel.SetDock(_reintentar, Dock.Right);
        _estado = new TextBlock
        {
            Foreground = Muted, FontSize = 12.5, TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        filaEstado.Children.Add(_reintentar);
        filaEstado.Children.Add(_estado);
        Grid.SetRow(filaEstado, 3);
        raiz.Children.Add(filaEstado);

        // ── lo que se va oyendo / la nota ────────────────────────────────────
        _vivo = new TextBlock
        {
            Foreground = Muted, FontSize = 14, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 4, 2, 4),
        };
        _nota = new StackPanel();
        var contenido = new StackPanel();
        contenido.Children.Add(_vivo);
        contenido.Children.Add(_nota);
        _scrollNota = new ScrollViewer
        {
            Content = contenido,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Grid.SetRow(_scrollNota, 4);
        raiz.Children.Add(_scrollNota);

        Content = raiz;

        // ── el cableado ──────────────────────────────────────────────────────
        _dictado.Parcial += t => Dispatcher.BeginInvoke(() =>
        {
            _vivo.Text = t;
            _scrollNota.ScrollToEnd();
        });
        _dictado.Fallo += m => Dispatcher.BeginInvoke(() => Estado("Dictado: " + m));
        _consulta.Cambio += _ => Dispatcher.BeginInvoke(PintarSegunEstado);

        Loaded += async (_, __) => await ArrancarAsync();
        Closed += (_, __) => { _dictado.Dispose(); _audio.Dispose(); };
    }

    // ── el ciclo de la ventana ───────────────────────────────────────────────

    private async Task ArrancarAsync()
    {
        _quien.Text = _sesion.MedicoNombre.Length > 0 ? _sesion.MedicoNombre : _sesion.MedicoEmail;
        Estado("Cargando plantillas…");
        try
        {
            _catalogo = await _clinica.PlantillasAsync();
            _plantillas.ItemsSource = _catalogo.Select(p => p.Nombre).ToList();
            int porDefecto = _catalogo.ToList().FindIndex(p => p.EsLaPorDefecto);
            _plantillas.SelectedIndex = porDefecto >= 0 ? porDefecto : (_catalogo.Count > 0 ? 0 : -1);
            Estado(_catalogo.Count > 0
                ? "Listo. Elige la plantilla y pulsa grabar."
                : "No hay plantillas disponibles para tu cuenta.");
        }
        catch (ErrorClinico e) { Estado(e.Message); }
        catch (Exception e)
        {
            Estado("No se pudo hablar con el backend clínico.");
            LogBus.Log("consulta-ui", $"plantillas: {e.Message}");
        }
    }

    private async Task AlternarAsync()
    {
        _boton.IsEnabled = false;
        try
        {
            if (_consulta.Estado == EstadoDeConsulta.Grabando)
            {
                Estado("Guardando la transcripción y organizando la nota…");
                await _consulta.TerminarAsync();
                return; // PintarSegunEstado pinta el resultado
            }

            if (_plantillas.SelectedIndex < 0 || _plantillas.SelectedIndex >= _catalogo.Count)
            {
                Estado("Elige una plantilla primero.");
                return;
            }
            LimpiarNota();
            _vivo.Text = "";
            Estado("Abriendo la consulta…");
            bool arranco = await _consulta.EmpezarAsync(_catalogo[_plantillas.SelectedIndex].Id);
            if (!arranco) Estado(_consulta.Motivo);
        }
        finally { _boton.IsEnabled = true; }
    }

    private void PintarSegunEstado()
    {
        switch (_consulta.Estado)
        {
            case EstadoDeConsulta.Grabando:
                _boton.Content = "■  PARAR";
                _boton.Background = Rec;
                Estado("Grabando. Habla con normalidad; puedes ver lo que se va oyendo abajo.");
                break;
            case EstadoDeConsulta.Transcrita:
                Estado("Transcripción guardada.");
                break;
            case EstadoDeConsulta.GenerandoNota:
                _boton.Content = "●  GRABAR";
                _boton.Background = Blue;
                Estado("El organizador está armando la nota…");
                break;
            case EstadoDeConsulta.NotaLista:
                _boton.Content = "●  GRABAR";
                _boton.Background = Blue;
                PintarNota();
                break;
            case EstadoDeConsulta.Fallida:
                _boton.Content = "●  GRABAR";
                _boton.Background = Blue;
                PintarFallo();
                break;
            default:
                _boton.Content = "●  GRABAR";
                _boton.Background = Blue;
                break;
        }
    }

    private void PintarNota()
    {
        var nota = _consulta.Nota;
        LimpiarNota();
        _vivo.Text = "";
        if (nota == null) { Estado("La nota volvió vacía."); return; }

        Estado($"Nota lista. La consulta ya está en tu cuenta ({_consulta.EncounterId[..Math.Min(8, _consulta.EncounterId.Length)]}…) y se ve en el portal.");

        if (nota.Resumen.Length > 0) _nota.Children.Add(Tarjeta("RESUMEN", nota.Resumen));
        foreach (var s in nota.Secciones)
            _nota.Children.Add(Tarjeta(s.Titulo.ToUpperInvariant(), s.Contenido));

        // Los avisos del organizador se enseñan: esconderlos sería decidir por el médico.
        if (nota.Avisos.Count > 0 || nota.SeccionesQueFaltan.Count > 0)
        {
            string avisos = string.Join("\n", nota.Avisos.Concat(
                nota.SeccionesQueFaltan.Select(s => $"Falta información para: {s}")));
            _nota.Children.Add(Tarjeta("AVISOS DEL ORGANIZADOR", avisos));
        }
        _scrollNota.ScrollToHome();
    }

    private void PintarFallo()
    {
        Estado(_consulta.Motivo);
        // Reintentar solo cuando reintentar puede servir: con LLM_NOT_CONFIGURED el botón sería
        // una promesa falsa.
        bool reintentable = _consulta.CodigoDeFallo is "NOTE_GENERATION_FAILED" or "INTERNAL_ERROR";
        _reintentar.Visibility = reintentable ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── piezas ───────────────────────────────────────────────────────────────

    private void Estado(string texto) => _estado.Text = texto;

    private void LimpiarNota()
    {
        _nota.Children.Clear();
        _reintentar.Visibility = Visibility.Collapsed;
    }

    private static Border Tarjeta(string titulo, string cuerpo)
    {
        var pila = new StackPanel();
        pila.Children.Add(new TextBlock
        {
            Text = titulo, Foreground = Muted, FontSize = 10.5,
            FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6),
        });
        pila.Children.Add(new TextBlock
        {
            Text = cuerpo.Length > 0 ? cuerpo : "—",
            Foreground = Ink, FontSize = 13.5, TextWrapping = TextWrapping.Wrap,
        });
        return new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = FieldBg,
            BorderBrush = Line,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 0, 0, 10),
            Child = pila,
        };
    }

    private static Brush Colors(Brush _) => Ink;

    private static ControlTemplate Redondo(double radio)
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radio));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        template.VisualTree = border;
        return template;
    }
}
