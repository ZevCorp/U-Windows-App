using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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
/// SIN CHROME DE WINDOWS. La barra de título del sistema y sus tres botones sobraban: esto es un
/// botón grande, no un documento. Se dibujan los propios —minimizar y cerrar— y NO hay maximizar:
/// una ventana que quiere ser una pastilla no tiene nada que hacer a pantalla completa. No basta
/// con no dibujar el botón, porque <c>Win+↑</c> maximiza igual; se devuelve al tamaño normal en
/// <c>StateChanged</c>.
///
/// TODO EL COLOR Y TODA LA SOMBRA SALEN DE <see cref="Estudio"/>. Aquí no se elige ni un gris: si
/// esta ventana empezara a inventar tonos, en dos cambios habría tres diseños distintos en la misma
/// app. La regla del estudio manda —fondo claro, lo elevado también, y la sombra es la que separa—
/// y esta pantalla solo la aplica.
///
/// EL SEGMENTADO EN VEZ DE DOS BOTONES: las pestañas viven dentro de un carril hundido y la activa
/// es una pastilla BLANCA con sombra encima. Así el estado seleccionado se lee por relieve y no por
/// color, que es exactamente lo que pide un diseño donde todo es claro.
///
/// LAS DOS PESTAÑAS CAMBIAN LA SUPERFICIE, no abren ventanas:
///   · **Consultas** — las anteriores, leídas de la MISMA tabla que lista el portal.
///   · **Nota** — el texto en vivo mientras se habla; al parar, la nota organizada.
///
/// NADIE ELIGE PLANTILLA (promesa 94): se resuelve sola con <see cref="PlantillaAbierta"/>.
///
/// EL NOMBRE DEL MÉDICO ES EL SELECTOR DE CUENTA: un clic despliega cambiar de cuenta, agregar una
/// nueva, o cerrar sesión. Las tres cierran la sesión actual, y eso se bloquea mientras se está
/// grabando (<see cref="Consulta.PuedeCambiarDeUsuario"/>, promesa 99) — cerrar sesión con el
/// micrófono abierto dejaría un dictado huérfano que nadie para ni guarda.
///
/// ESTA VENTANA NO DECIDE NADA. Lo que pesa vive en clases que el contrato juzga sin pantalla:
/// <see cref="SesionMiracle"/> (84-86, 90), <see cref="Consulta"/> (84, 91, 99),
/// <see cref="DictadoEnVivo"/> (88, 89), <see cref="EspejoDeConsulta"/> (93) y
/// <see cref="PlantillaAbierta"/> (94).
/// </remarks>
public sealed class ConsultaWindow : Window
{
    private readonly SesionMiracle _sesion;
    private readonly ClinicaClient _clinica;
    private readonly LiveAudio _audio;
    private readonly DictadoEnVivo _dictado;
    private readonly Consulta _consulta;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly Button _quienBoton;
    private readonly TextBlock _quien;
    private readonly Popup _menuCuenta;
    private readonly Button _tabConsultas;
    private readonly Button _tabNota;
    private readonly ScrollViewer _superficie;
    private readonly StackPanel _listaConsultas;
    private readonly StackPanel _panelNota;
    private readonly TextBlock _vivo;
    private readonly StackPanel _nota;
    private readonly Border _vacioNota;
    private readonly Button _grabar;
    private readonly TextBlock _puntoDeGrabar;
    private readonly TextBlock _etiquetaDeGrabar;
    private readonly TextBlock _estado;

    private readonly DispatcherTimer _cronometro = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTimeOffset _empezoAGrabar;

    private string _plantillaId = "";
    private string _plantillaNombre = "";
    private bool _enNota = true;

    public ConsultaWindow(SesionMiracle sesion, GraphConfig graphConfig)
    {
        _sesion = sesion;
        _clinica = new ClinicaClient(graphConfig.BaseUrl, sesion);
        _audio = new LiveAudio();
        _dictado = new DictadoEnVivo(graphConfig, _audio, sesion);
        _consulta = new Consulta(sesion, _clinica,
            abrirMicrofono: _dictado.ArrancarAsync,
            pararYRecogerLoDicho: () => _dictado.PararAsync(),
            espejar: async (encounterId, nota, verbatim) =>
            {
                string fila = EspejoDeConsulta.Fila(encounterId, nota, verbatim,
                    _plantillaNombre, PlantillaAbierta.Especialidad, DateTimeOffset.UtcNow);
                return await EspejoDeConsulta.EscribirAsync(_sesion, _http, fila);
            });

        // ── el marco ─────────────────────────────────────────────────────────
        Title = "Miracle";
        Width = 470;
        Height = 660;
        MinWidth = 400;
        MinHeight = 540;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        this.PonerLaBarraDeScroll();

        var tarjeta = new Border
        {
            // El radio del boceto: casi una pastilla, y eso es lo que hace que no parezca una ventana.
            CornerRadius = new CornerRadius(46),
            Background = Estudio.Fondo,
            BorderBrush = Estudio.Borde,
            BorderThickness = new Thickness(1),
            // El margen tiene que dar cabida a la sombra: con menos, se recorta contra el borde de
            // la ventana y el relieve se convierte en una raya.
            Margin = new Thickness(0),
        };

        var raiz = new Grid { Margin = new Thickness(24, 20, 24, 24) };
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // cabecera
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // segmentado
        raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });  // superficie
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // estado
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // grabar

        // ── cabecera ─────────────────────────────────────────────────────────
        var cabecera = new DockPanel { Margin = new Thickness(6, 0, 0, 16) };

        var botonera = new StackPanel { Orientation = Orientation.Horizontal };
        var minimizar = BotonDeMarco("", "Minimizar");
        minimizar.Click += (_, __) => WindowState = WindowState.Minimized;
        var cerrar = BotonDeMarco("", "Cerrar");
        cerrar.Click += (_, __) => Close();
        botonera.Children.Add(minimizar);
        botonera.Children.Add(cerrar);
        DockPanel.SetDock(botonera, Dock.Right);

        // EL NOMBRE ES EL SELECTOR DE CUENTA. Un clic despliega cambiar de cuenta, agregar una
        // nueva, o cerrar sesión — el mismo patrón que cualquier app con varias cuentas, y evita
        // una segunda ventana de «gestionar cuenta» para tres acciones que caben en un menú.
        _quien = new TextBlock
        {
            Foreground = Estudio.Tinta,
            FontSize = 14.5,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var chevron = new TextBlock
        {
            Text = "",   // ChevronDown de Segoe MDL2: el mismo lenguaje que minimizar/cerrar
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 8,
            Foreground = Estudio.TintaTenue,
            Margin = new Thickness(7, 3, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var contenidoQuien = new StackPanel { Orientation = Orientation.Horizontal };
        contenidoQuien.Children.Add(_quien);
        contenidoQuien.Children.Add(chevron);

        _quienBoton = new Button
        {
            Content = contenidoQuien,
            // Sin esto, el DockPanel estiraría el botón a todo el ancho que sobra y su plantilla
            // (que centra el contenido) dejaría el nombre flotando en medio de la barra en vez de
            // pegado al borde izquierdo, que es donde estaba siempre.
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(6, 4, 8, 4),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Template = Estudio.Pastilla(Estudio.RadioChico),
        };
        _quienBoton.MouseEnter += (_, __) => _quienBoton.Background = Estudio.SuperficieSuave;
        _quienBoton.MouseLeave += (_, __) => _quienBoton.Background = Brushes.Transparent;
        _quienBoton.Click += (_, __) => AlternarMenuCuenta();

        _menuCuenta = new Popup
        {
            PlacementTarget = _quienBoton,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
        };

        cabecera.Children.Add(botonera);
        cabecera.Children.Add(_quienBoton);
        Grid.SetRow(cabecera, 0);
        raiz.Children.Add(cabecera);

        // ── el segmentado ────────────────────────────────────────────────────
        var carril = new Border
        {
            CornerRadius = new CornerRadius(21),
            Background = Estudio.SuperficieSuave,
            Padding = new Thickness(4),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 18),
        };
        var segmentos = new StackPanel { Orientation = Orientation.Horizontal };
        _tabConsultas = Pestana("Consultas");
        _tabNota = Pestana("Nota");
        _tabConsultas.Click += async (_, __) => { Mostrar(nota: false); await CargarConsultasAsync(); };
        _tabNota.Click += (_, __) => Mostrar(nota: true);
        segmentos.Children.Add(_tabConsultas);
        segmentos.Children.Add(_tabNota);
        carril.Child = segmentos;
        Grid.SetRow(carril, 1);
        raiz.Children.Add(carril);

        // ── la superficie ────────────────────────────────────────────────────
        _listaConsultas = new StackPanel { Visibility = Visibility.Collapsed };

        _vivo = new TextBlock
        {
            Foreground = Estudio.Tinta,
            FontSize = 15,
            LineHeight = 25,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 0, 4, 8),
        };
        _nota = new StackPanel();
        // VACÍO NO ES FALLO, y una pantalla en blanco no lo distingue: se dice qué va a pasar aquí.
        _vacioNota = Estudio.Tarjeta(20);
        _vacioNota.Padding = new Thickness(20, 22, 20, 22);
        _vacioNota.Margin = new Thickness(2, 8, 2, 0);
        var pilaVacio = new StackPanel();
        pilaVacio.Children.Add(new TextBlock
        {
            Text = "Pulsa grabar y habla con normalidad.",
            Foreground = Estudio.Tinta, FontSize = 14, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
        });
        pilaVacio.Children.Add(new TextBlock
        {
            Text = "Verás aquí lo que se va oyendo. Al parar, la nota queda organizada y guardada "
                 + "en tu cuenta — la misma que ves en el portal.",
            Foreground = Estudio.TintaMedia, FontSize = 12.5, LineHeight = 19,
            TextWrapping = TextWrapping.Wrap,
        });
        _vacioNota.Child = pilaVacio;

        _panelNota = new StackPanel();
        _panelNota.Children.Add(_vivo);
        _panelNota.Children.Add(Estudio.Elevar(_vacioNota));
        _panelNota.Children.Add(_nota);

        var contenido = new StackPanel();
        contenido.Children.Add(_panelNota);
        contenido.Children.Add(_listaConsultas);
        _superficie = new ScrollViewer
        {
            Content = contenido,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            // El pulgar corre por fuera del texto en vez de encima: si no, la última letra de cada
            // línea queda debajo de la barra en cuanto el contenido crece.
            Padding = new Thickness(0, 0, 6, 0),
        };
        Grid.SetRow(_superficie, 2);
        raiz.Children.Add(_superficie);

        // ── estado ───────────────────────────────────────────────────────────
        _estado = new TextBlock
        {
            Foreground = Estudio.TintaMedia,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(10, 14, 10, 12),
        };
        Grid.SetRow(_estado, 3);
        raiz.Children.Add(_estado);

        // ── el botón ─────────────────────────────────────────────────────────
        //
        // BLANCO SOBRE CLARO, Y LA SOMBRA HACE EL RESTO. Un botón oscuro aquí gritaría; este se
        // lee como un objeto que sobresale del papel. El punto de color es lo único que cambia
        // entre reposo y grabando: el objeto es el mismo, su estado no.
        var dentroDelBoton = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _puntoDeGrabar = new TextBlock
        {
            Text = "●",
            Foreground = Estudio.Alerta,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        _etiquetaDeGrabar = new TextBlock
        {
            Text = "Grabar",
            Foreground = Estudio.Tinta,
            FontSize = 17.5,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        dentroDelBoton.Children.Add(_puntoDeGrabar);
        dentroDelBoton.Children.Add(_etiquetaDeGrabar);

        _grabar = new Button
        {
            Content = dentroDelBoton,
            Width = 178,
            Height = 74,
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = Estudio.Superficie,
            BorderBrush = Estudio.Borde,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Template = Estudio.Pastilla(37),
        };
        _grabar.ConRelieve(Estudio.Sombra2);
        _grabar.Click += async (_, __) => await AlternarAsync();
        Grid.SetRow(_grabar, 4);
        raiz.Children.Add(_grabar);

        tarjeta.Child = raiz;
        // La sombra la echa una PLACA detrás, nunca el borde que lleva el contenido: dentro de un
        // Effect el texto pierde ClearType y toda la ventana se ve lavada (2026-09-01).
        var marco = Estudio.Elevar(tarjeta, Estudio.Sombra3);
        marco.Margin = new Thickness(22, 18, 22, 26);
        Content = marco;
        this.Nitida();

        // Sin barra de título, arrastrar es cosa nuestra. Los botones se tragan su propio clic.
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) WindowState = WindowState.Minimized; };
        // NO HAY MAXIMIZAR, y no basta con no dibujar el botón: Win+↑ maximiza igual y la pastilla
        // se convertiría en una pantalla completa con las esquinas flotando.
        StateChanged += (_, __) => { if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal; };

        _cronometro.Tick += (_, __) => PintarCronometro();
        _dictado.Parcial += t => Dispatcher.BeginInvoke(() =>
        {
            _vivo.Text = t;
            _vacioNota.Visibility = t.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
            if (_enNota) _superficie.ScrollToEnd();
        });
        _dictado.Fallo += m => Dispatcher.BeginInvoke(() => Estado("Dictado: " + m));
        _consulta.Cambio += _ => Dispatcher.BeginInvoke(PintarSegunEstado);

        Mostrar(nota: true);
        Loaded += async (_, __) => await ArrancarAsync();
        Closed += (_, __) => { _cronometro.Stop(); _dictado.Dispose(); _audio.Dispose(); _http.Dispose(); };
    }

    // ── arranque ─────────────────────────────────────────────────────────────

    private async Task ArrancarAsync()
    {
        _quien.Text = _sesion.MedicoNombre.Length > 0 ? _sesion.MedicoNombre : _sesion.MedicoEmail;
        Estado("Preparando…");
        await ResolverPlantillaAsync();
    }

    /// <summary>
    /// Deja lista la plantilla abierta: la busca en el catálogo y, si no existe, la crea. Se llama
    /// al arrancar y otra vez al pulsar grabar si la primera no cuajó.
    /// </summary>
    private async Task ResolverPlantillaAsync()
    {
        try
        {
            var catalogo = await _clinica.PlantillasAsync();
            var abierta = PlantillaAbierta.Elegir(catalogo);
            abierta ??= await _clinica.CrearPlantillaAsync(
                PlantillaAbierta.Nombre, PlantillaAbierta.Especialidad, PlantillaAbierta.Secciones());

            _plantillaId = abierta.Id;
            _plantillaNombre = abierta.Nombre;
            Estado("Listo.");
        }
        catch (ErrorClinico e) { Estado(e.Message); }
        catch (Exception e)
        {
            // SE DICE QUÉ HACER, no solo que falló. «No se pudo hablar con el backend» deja a
            // alguien mirando la pantalla; decirle que vuelva a pulsar le da una salida.
            Estado("Sin conexión con Miracle. Comprueba la red y vuelve a pulsar grabar.");
            LogBus.Log("consulta-ui", $"plantilla: {e.GetType().Name}: {e.Message}");
        }
    }

    // ── la cuenta ────────────────────────────────────────────────────────────

    private void AlternarMenuCuenta()
    {
        if (_menuCuenta.IsOpen) { _menuCuenta.IsOpen = false; return; }
        // Se reconstruye en cada apertura y no una sola vez al crear la ventana: el estado que
        // decide qué se puede pulsar —si se está grabando— cambia mientras la ventana vive, y un
        // menú fijo mostraría opciones activas que la promesa 99 ya no permite tocar.
        _menuCuenta.Child = ConstruirMenuCuenta();
        _menuCuenta.IsOpen = true;
    }

    private UIElement ConstruirMenuCuenta()
    {
        bool puede = _consulta.PuedeCambiarDeUsuario;

        var tarjeta = Estudio.Tarjeta(16);
        tarjeta.Padding = new Thickness(6);

        var pila = new StackPanel { Width = 224 };

        var cabecera = new StackPanel { Margin = new Thickness(10, 8, 10, 8) };
        cabecera.Children.Add(new TextBlock
        {
            Text = _sesion.MedicoNombre.Length > 0 ? _sesion.MedicoNombre : "Sin nombre",
            Foreground = Estudio.Tinta, FontSize = 13.5, FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        cabecera.Children.Add(new TextBlock
        {
            Text = _sesion.MedicoEmail, Foreground = Estudio.TintaTenue, FontSize = 11.5,
            Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis,
        });
        pila.Children.Add(cabecera);
        pila.Children.Add(SeparadorMenu());

        string motivoBloqueo = "Termina la consulta antes de cambiar de cuenta.";
        pila.Children.Add(ItemDeMenu("Cambiar de cuenta", puede,
            () => _ = CambiarCuentaAsync(creando: false), motivoBloqueo));
        pila.Children.Add(ItemDeMenu("Agregar cuenta", puede,
            () => _ = CambiarCuentaAsync(creando: true), motivoBloqueo));
        pila.Children.Add(SeparadorMenu());
        pila.Children.Add(ItemDeMenu("Cerrar sesión", puede, CerrarSesion, motivoBloqueo));

        tarjeta.Child = pila;
        // Margen extra para que la sombra del menú no se recorte contra el borde del Popup: un
        // Popup se dimensiona justo al contenido, y sin este aire el desenfoque queda cortado.
        var elevado = Estudio.Elevar(tarjeta, Estudio.Sombra2);
        elevado.Margin = new Thickness(12);
        return elevado;
    }

    private UIElement ItemDeMenu(string texto, bool activo, Action accion, string motivoInactivo)
    {
        var t = new TextBlock
        {
            Text = texto, FontSize = 13,
            Foreground = activo ? Estudio.Tinta : Estudio.TintaTenue,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var b = new Border
        {
            CornerRadius = new CornerRadius(Estudio.RadioChico),
            Padding = new Thickness(10, 9, 10, 9),
            Background = Brushes.Transparent,
            Child = t,
            Cursor = activo ? Cursors.Hand : Cursors.Arrow,
            ToolTip = activo ? null : motivoInactivo,
        };
        if (activo)
        {
            b.MouseEnter += (_, __) => b.Background = Estudio.SuperficieSuave;
            b.MouseLeave += (_, __) => b.Background = Brushes.Transparent;
            b.MouseLeftButtonUp += (_, __) => { _menuCuenta.IsOpen = false; accion(); };
        }
        return b;
    }

    private static Border SeparadorMenu() => new()
    {
        Height = 1, Background = Estudio.Borde, Margin = new Thickness(4, 6, 4, 6),
    };

    /// <summary>
    /// Cambia de cuenta o agrega una nueva: cierra la sesión actual y abre el login. Sin sesión
    /// nueva, la consulta se cierra entera (promesa 84: sin médico no hay consulta).
    /// </summary>
    private async Task CambiarCuentaAsync(bool creando)
    {
        // Guardia por si el menú quedó abierto de antes de que se empezara a grabar: el clic pasó
        // por ItemDeMenu con `activo` ya calculado, pero comprobar aquí también no cuesta nada y
        // es la misma promesa 99 aplicada dos veces por seguridad, no una segunda opinión.
        if (!_consulta.PuedeCambiarDeUsuario) return;

        _consulta.Cerrar();
        _sesion.Salir();

        var login = new LoginWindow(_sesion, empezarCreando: creando) { Owner = this };
        if (login.ShowDialog() != true) { Close(); return; }

        _plantillaId = "";
        _plantillaNombre = "";
        _nota.Children.Clear();
        _vivo.Text = "";
        _vacioNota.Visibility = Visibility.Visible;
        _listaConsultas.Children.Clear();
        Mostrar(nota: true);
        await ArrancarAsync();
    }

    private void CerrarSesion()
    {
        if (!_consulta.PuedeCambiarDeUsuario) return;
        _sesion.Salir();
        Close();
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

    /// <summary>
    /// La activa se eleva: blanca y con sombra. La otra se queda hundida en el carril, sin fondo ni
    /// relieve. El estado se lee por profundidad, no por color.
    /// </summary>
    private void PintarPestanas()
    {
        void Pintar(Button b, bool activa)
        {
            b.Background = activa ? Estudio.Superficie : Brushes.Transparent;
            b.Foreground = activa ? Estudio.Tinta : Estudio.TintaMedia;
            b.FontWeight = activa ? FontWeights.SemiBold : FontWeights.Normal;
            b.Effect = activa ? Estudio.Sombra1 : null;
        }
        Pintar(_tabNota, _enNota);
        Pintar(_tabConsultas, !_enNota);
    }

    private async Task CargarConsultasAsync()
    {
        _listaConsultas.Children.Clear();
        _listaConsultas.Children.Add(new TextBlock
        {
            Text = "Cargando…", Foreground = Estudio.TintaTenue, FontSize = 12.5,
            Margin = new Thickness(6, 8, 6, 6),
        });

        var previas = await EspejoDeConsulta.UltimasAsync(_sesion, _http);
        _listaConsultas.Children.Clear();

        if (previas.Count == 0)
        {
            var vacio = Estudio.Tarjeta(20);
            vacio.Padding = new Thickness(20, 22, 20, 22);
            vacio.Margin = new Thickness(2, 8, 2, 0);
            var pila = new StackPanel();
            pila.Children.Add(new TextBlock
            {
                Text = "Todavía no hay consultas.",
                Foreground = Estudio.Tinta, FontSize = 14, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6),
            });
            pila.Children.Add(new TextBlock
            {
                Text = "La primera que grabes aparece aquí y en el portal.",
                Foreground = Estudio.TintaMedia, FontSize = 12.5, TextWrapping = TextWrapping.Wrap,
            });
            vacio.Child = pila;
            _listaConsultas.Children.Add(vacio);
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

            // SE REINTENTA AQUÍ, y no es un detalle. Si al arrancar falló la red —medido el
            // 2026-09-01: «Host desconocido (graph-eight-pied.vercel.app:443)» por un tropiezo de
            // DNS—, la plantilla se quedaba sin resolver y el botón contestaba «todavía no está
            // lista» PARA SIEMPRE: la única salida era cerrar y volver a abrir. Un fallo pasajero
            // no puede dejar la app inservible hasta el siguiente arranque.
            if (_plantillaId.Length == 0)
            {
                Estado("Reintentando la conexión…");
                await ResolverPlantillaAsync();
                if (_plantillaId.Length == 0) return;   // ResolverPlantilla ya dijo por qué
            }

            Mostrar(nota: true);
            _nota.Children.Clear();
            _vivo.Text = "";
            _vacioNota.Visibility = Visibility.Visible;
            Estado("Abriendo la consulta…");
            if (!await _consulta.EmpezarAsync(_plantillaId)) Estado(_consulta.Motivo);
        }
        finally { _grabar.IsEnabled = true; }
    }

    private void PintarSegunEstado()
    {
        bool grabando = _consulta.Estado == EstadoDeConsulta.Grabando;
        _etiquetaDeGrabar.Text = grabando ? "Parar" : "Grabar";
        _puntoDeGrabar.Foreground = grabando ? Estudio.Alerta : Estudio.TintaTenue;
        _puntoDeGrabar.Text = grabando ? "■" : "●";

        if (grabando) { _empezoAGrabar = DateTimeOffset.Now; _cronometro.Start(); PintarCronometro(); }
        else _cronometro.Stop();

        switch (_consulta.Estado)
        {
            case EstadoDeConsulta.GenerandoNota: Estado("Organizando la nota…"); break;
            case EstadoDeConsulta.NotaLista: PintarNota(); break;
            case EstadoDeConsulta.Fallida: Estado(_consulta.Motivo); break;
        }
    }

    /// <summary>
    /// Cuánto lleva grabando. No es adorno: quien está en una consulta necesita saberlo sin mirar
    /// el reloj, y verlo correr es además la prueba de que el micrófono sigue abierto.
    /// </summary>
    private void PintarCronometro()
    {
        var va = DateTimeOffset.Now - _empezoAGrabar;
        Estado($"Escuchando · {va:mm\\:ss}");
    }

    private void PintarNota()
    {
        var nota = _consulta.Nota;
        _nota.Children.Clear();
        _vivo.Text = "";
        _vacioNota.Visibility = Visibility.Collapsed;
        if (nota == null) { Estado("La nota volvió vacía."); return; }

        // SE DICE SI SE VIO O NO EN EL PORTAL, no se supone (promesa 93).
        Estado(_consulta.VisibleEnElPortal
            ? "Nota lista. Ya se ve en el portal."
            : "Nota guardada, pero no se pudo espejar al portal. Está en el log.");

        if (nota.Resumen.Length > 0) _nota.Children.Add(TarjetaDeTexto("Resumen", nota.Resumen));
        foreach (var s in nota.Secciones)
        {
            // Una casilla vacía no es información: la plantilla abierta deja en blanco lo que no se
            // dijo, y pintar «—» sería llenar la pantalla de nada.
            if (s.Contenido.Trim().Length == 0) continue;
            _nota.Children.Add(TarjetaDeTexto(s.Titulo, s.Contenido));
        }
        if (nota.Avisos.Count > 0)
            _nota.Children.Add(TarjetaDeTexto("Avisos", string.Join("\n", nota.Avisos)));
        _superficie.ScrollToHome();
    }

    // ── piezas ───────────────────────────────────────────────────────────────

    private void Estado(string texto) => _estado.Text = texto;

    private static Button BotonDeMarco(string glifo, string queHace)
    {
        var b = new Button
        {
            Content = new TextBlock
            {
                Text = glifo,
                // La fuente de iconos de Windows: el mismo glifo de minimizar y cerrar que usa el
                // sistema, para que se reconozcan sin leerlos.
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 9.5,
                Foreground = Estudio.TintaMedia,
            },
            Width = 30,
            Height = 30,
            Margin = new Thickness(7, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            ToolTip = queHace,
            Template = Estudio.Pastilla(15),
        };
        // Los del marco no llevan sombra en reposo —serían dos objetos flotando junto al nombre—
        // pero sí se encienden al pasar por encima, que es lo que dice que son pulsables.
        b.MouseEnter += (_, __) => b.Background = Estudio.SuperficieSuave;
        b.MouseLeave += (_, __) => b.Background = Brushes.Transparent;
        return b;
    }

    private static Button Pestana(string texto) => new Button
    {
        Content = texto,
        Height = 36,
        MinWidth = 122,
        Margin = new Thickness(0),
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        FontSize = 13.5,
        Cursor = Cursors.Hand,
        Template = Estudio.Pastilla(18),
    };

    private static UIElement TarjetaDeTexto(string titulo, string cuerpo)
    {
        var pila = new StackPanel();
        pila.Children.Add(Estudio.Rotulo(titulo));
        pila.Children.Add(Estudio.Parrafo(cuerpo));
        var t = Estudio.Tarjeta(18);
        t.Padding = new Thickness(16, 14, 16, 15);
        t.Margin = new Thickness(2, 0, 2, 10);
        t.Child = pila;
        return Estudio.Elevar(t);
    }

    private static UIElement FilaDeConsulta(ConsultaVista c)
    {
        var pila = new StackPanel();

        var arriba = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
        if (c.Estado.Length > 0)
        {
            var chip = new Border
            {
                CornerRadius = new CornerRadius(9),
                Background = Estudio.AcentoSuave,
                Padding = new Thickness(8, 3, 8, 3),
                Child = new TextBlock
                {
                    Text = c.Estado, Foreground = Estudio.Acento,
                    FontSize = 9.5, FontWeight = FontWeights.SemiBold,
                },
            };
            DockPanel.SetDock(chip, Dock.Right);
            arriba.Children.Add(chip);
        }
        arriba.Children.Add(new TextBlock
        {
            Text = c.Fecha == DateTimeOffset.MinValue ? "" : c.Fecha.ToLocalTime().ToString("d MMM · HH:mm"),
            Foreground = Estudio.TintaTenue, FontSize = 10.5, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        pila.Children.Add(arriba);

        pila.Children.Add(new TextBlock
        {
            Text = c.Motivo.Length > 0 ? c.Motivo
                 : c.Resumen.Length > 0 ? c.Resumen
                 : "Consulta sin motivo anotado",
            Foreground = Estudio.Tinta, FontSize = 13.5, LineHeight = 20,
            TextWrapping = TextWrapping.Wrap, MaxHeight = 62, TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var t = Estudio.Tarjeta(18);
        t.Padding = new Thickness(16, 13, 16, 14);
        t.Margin = new Thickness(2, 0, 2, 10);
        t.Child = pila;
        return Estudio.Elevar(t);
    }
}
