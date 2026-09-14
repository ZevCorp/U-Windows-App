using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Automation;
using Omi;
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
    /// <summary>El panel de aprendizajes: la lista, la ficha y las capturas, una vista a la vez.</summary>
    private readonly StackPanel _panelAprendizajes = new();
    private readonly Button _aprendizajes;
    private bool _enAprendizajes;
    /// <summary>Los «sin repasar» empiezan plegados: son 11 de 19 y con nombres que puso el modelo.</summary>
    private bool _pendientesALaVista;
    private bool _mostrando;
    private readonly StackPanel _panelNota;
    private readonly TextBlock _vivo;
    private readonly StackPanel _nota;
    private readonly Border _vacioNota;
    private readonly Button _grabar;
    private readonly TextBlock _puntoDeGrabar;
    private readonly TextBlock _etiquetaDeGrabar;
    private readonly TextBlock _estado;

    // ── el micrófono ─────────────────────────────────────────────────────────
    private readonly Button _microfono;
    private readonly Popup _menuMicrofono;
    private readonly TextBlock _iconoMicrofono;

    /// <summary>Quién manda entre las tres fuentes. La decisión es suya, no de esta ventana.</summary>
    private readonly Omi.Selector _selector = new();

    /// <summary>Cómo llama el médico a cada aparato. Promesa 151 (spec 010).</summary>
    private readonly Voice.NombresDeDispositivos _nombresDeDispositivos = new();

    /// <summary>
    /// Quién decide si se puede pintar verde. Tres segundos sin audio y deja de decir que lo hay.
    ///
    /// TRES Y NO TREINTA: esto sólo decide qué se PINTA, no cuándo se cambia de micrófono (eso es
    /// <c>Omi.Relevo</c>, y su umbral es largo a propósito). Un verde que sobrevive medio minuto a
    /// la avería es peor que un verde que parpadea — el del 2026-08-25 sobrevivió 56 minutos.
    /// </summary>
    private readonly Vigia _vigia = new(3000);

    /// <summary>
    /// Cuándo entregó cada fuente, POR SEPARADO.
    ///
    /// Era un solo reloj compartido y eso pintaba de verde al collar con el collar en rojo: el
    /// micrófono del portátil grababa de respaldo y sus tramas avalaban al collar muerto
    /// (2026-09-01, visto por el dueño). Cada fuente responde por sí misma — promesa 32.
    /// </summary>
    private readonly Testigo _testigo = new();

    /// <summary>El código de este médico en este PC, y cuándo se emitió. Vacío hasta que se pida.</summary>
    private string _codigo = "";
    private long _codigoEmitidoMs;

    /// <summary>
    /// Dónde escucha el receptor del teléfono. Se puede pisar con <c>U_OMI_DIRECTO</c> para probar
    /// contra otro despliegue sin recompilar.
    /// </summary>
    private static string BaseDelEnlace =>
        Environment.GetEnvironmentVariable("U_OMI_DIRECTO") is { Length: > 0 } v
            ? v
            : "wss://zyvfamlhlmztliexvmej.supabase.co/functions/v1/omi-directo";

    private const string GlifoMicrofono = "";
    private const string GlifoBluetooth = "";
    private const string GlifoTelefono = "";

    private readonly DispatcherTimer _cronometro = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>
    /// Repinta el indicador una vez por segundo, GRABANDO O NO.
    ///
    /// No se cuelga del cronómetro —que sólo corre mientras se graba— porque la pregunta «¿me va a
    /// oír?» se hace ANTES de pulsar. Y no se cuelga sólo de los eventos de audio porque el estado
    /// que hay que detectar es justo la AUSENCIA de eventos: un enlace que se quedó mudo no manda
    /// ninguna notificación diciéndolo.
    /// </summary>
    private readonly DispatcherTimer _pulso = new() { Interval = TimeSpan.FromSeconds(1) };

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
        DejarseAgarrarPorElBorde();

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

        // ── el micrófono, al lado del nombre ─────────────────────────────────
        //
        // UN ICONO Y NADA MÁS. Es una preferencia que se toca una vez cada varios días: no merece
        // ni una etiqueta permanente ni un renglón propio. Lo que tiene que verse de un vistazo es
        // el COLOR, que dice si está entrando voz; el resto se pregunta pulsando.
        _microfono = new Button
        {
            Width = 28,
            Height = 28,
            Margin = new Thickness(2, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Template = Estudio.Pastilla(14),
        };
        _iconoMicrofono = new TextBlock
        {
            // Segoe MDL2 y no un emoji, por lo mismo que la tira de la carita: un emoji lo pinta la
            // fuente del sistema, trae su propio color y su propio peso, y rompe la línea.
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            Text = GlifoMicrofono,
            FontSize = 13,
            Foreground = Estudio.TintaMedia,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _microfono.Content = _iconoMicrofono;
        _microfono.MouseEnter += (_, __) => _microfono.Background = Estudio.SuperficieSuave;
        _microfono.MouseLeave += (_, __) => _microfono.Background = Brushes.Transparent;
        _microfono.Click += (_, __) => { PintarMenuDeMicrofono(); _menuMicrofono.IsOpen = !_menuMicrofono.IsOpen; };

        _menuMicrofono = new Popup
        {
            PlacementTarget = _microfono,
            // CENTRADO EN SU BOTÓN, no colgando de su esquina (2026-09-05, lo pidió el dueño
            // mirándolo: «está anclado a la esquina superior izquierda»). Con Bottom a secas, WPF
            // alinea el borde IZQUIERDO del menú con el del botón, así que un menú ancho se
            // desparrama hacia la derecha y el botón queda en una punta — se lee como si el menú
            // perteneciera a otra cosa.
            //
            // Custom y no un HorizontalOffset a mano: el desplazamiento depende del ancho REAL del
            // menú, que no se sabe hasta que se mide. Un número fijo se rompería en cuanto el menú
            // creciera —y crece: la sección de dispositivos aparece y desaparece con lo que haya
            // enlazado.
            Placement = PlacementMode.Custom,
            CustomPopupPlacementCallback = (menu, boton, _) => new[]
            {
                // El alto que se resta es el hueco que Estudio.Elevar reserva ARRIBA para que la
                // sombra quepa: sin descontarlo, ese aire transparente se sumaría a la separación y
                // el menú aparecería flotando lejos del botón que lo abrió. El centrado horizontal
                // no necesita cuenta porque la holgura es igual a los dos lados.
                new CustomPopupPlacement(
                    new Point((boton.Width - menu.Width) / 2,
                              boton.Height + 6 - Estudio.HolguraDe(Estudio.Sombra3).Top),
                    PopupPrimaryAxis.Horizontal),
            },
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
        };

        var izquierdaDeLaCabecera = new StackPanel { Orientation = Orientation.Horizontal };
        izquierdaDeLaCabecera.Children.Add(_quienBoton);
        izquierdaDeLaCabecera.Children.Add(_microfono);

        // EL CEREBRO, Y NO UN TERCER SEGMENTO EN EL CARRIL (spec 016). El carril contesta «qué
        // estoy mirando DE ESTE PACIENTE» —sus consultas, su nota—; los aprendizajes no son del
        // paciente, son del asistente. Meterlos ahí es un error de categoría, y se nota como ruido
        // aunque cada pieza esté bien dibujada.
        _aprendizajes = BotonDeIcono(CerebroDibujado(), "Aprendizajes");
        _aprendizajes.Click += (_, __) => { if (_enAprendizajes) Mostrar(nota: _enNota); else AbrirAprendizajes(); };
        izquierdaDeLaCabecera.Children.Add(_aprendizajes);

        cabecera.Children.Add(botonera);
        cabecera.Children.Add(izquierdaDeLaCabecera);
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
        _panelAprendizajes.Visibility = Visibility.Collapsed;
        contenido.Children.Add(_panelNota);
        contenido.Children.Add(_listaConsultas);
        contenido.Children.Add(_panelAprendizajes);
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

        // LA ÚNICA PRUEBA DE QUE HAY MICRÓFONO ES QUE LLEGUE AUDIO. Anotar la hora de cada trozo es
        // barato y es lo que impide repetir el verde falso del 2026-08-25: 56 minutos de «conectado»
        // sin una sola trama, con el usuario delante de una demo.
        // SE ANOTA A NOMBRE DE QUIEN ENTREGÓ, y esa es toda la diferencia: LiveAudio sabe si el
        // trozo viene del collar o del micrófono del portátil, y sin preguntárselo el respaldo
        // acaba avalando a la fuente que falló.
        _audio.Capturado += _ => _testigo.Anota(
            _audio.PorElCollar ? Origen.CollarPorBluetooth
            : _audio.PorElTelefono ? Origen.CollarPorTelefono
            : Origen.MicrofonoDelPc,
            Environment.TickCount64);
        _audio.FuenteCambio += () => Dispatcher.BeginInvoke(PintarMicrofono);
        CollarPermanente.Cambio += () => Dispatcher.BeginInvoke(PintarMicrofono);
        _pulso.Tick += (_, __) => PintarMicrofono();
        _pulso.Start();

        // SE RECONECTA SOLO AL ARRANCAR si el código sigue vivo, igual que el collar por Bluetooth
        // se reconecta solo. Sin esto habría que volver a elegir «Teléfono» cada vez que se abre la
        // app, aunque el teléfono ya esté configurado y mandando.
        RestaurarElCodigo();
        if (_codigo.Length > 0)
        {
            _selector.Preferir(Origen.CollarPorTelefono);
            Voice.ElMicrofonoDeLaApp.Elegir(Origen.CollarPorTelefono, _codigo ?? "");
            _ = _audio.PasarAlTelefonoAsync(Nube.ProyectoSupabase, Nube.ClavePublicable, _codigo);
            LogBus.Log("telefono", "código recordado de una sesión anterior: volviendo a escuchar el canal");
        }

        PintarMicrofono();

        Mostrar(nota: true);
        Loaded += async (_, __) => await ArrancarAsync();
        Closed += (_, __) => { _cronometro.Stop(); _pulso.Stop(); _dictado.Dispose(); _audio.Dispose(); _http.Dispose(); };
    }

    // ── el micrófono ─────────────────────────────────────────────────────────

    /// <summary>
    /// El menú: tres líneas y nada más. Se repinta al abrirlo para marcar cuál manda.
    ///
    /// SIN TEXTOS DE AYUDA, y es una corrección con fecha (2026-09-01, dicha por el dueño mirando la
    /// primera versión): «los usuarios no van a leer esos textos largos». Una preferencia de tres
    /// opciones no necesita que le expliquen cada una — necesita que se vea cuál está puesta.
    /// </summary>
    /// <summary>Lo que mide el menú de lado a lado. Fijo para que no baile al cambiar el contenido.</summary>
    private const double AnchoDelMenu = 292;

    private void PintarMenuDeMicrofono()
    {
        var lista = new StackPanel { Margin = new Thickness(8) };
        var real = FuenteReal();
        lista.Children.Add(FilaDeMicrofono(Origen.MicrofonoDelPc, GlifoMicrofono, "Computador", real));
        lista.Children.Add(FilaDeMicrofono(Origen.CollarPorBluetooth, GlifoBluetooth, "Collar Omi", real));
        lista.Children.Add(FilaDeMicrofono(Origen.CollarPorTelefono, GlifoTelefono, "Teléfono", real));

        // El enlace SÓLO cuando el teléfono es lo elegido: hasta entonces no significa nada y sería
        // una línea de ruido en un menú de tres.
        if (_selector.Preferida == Origen.CollarPorTelefono) lista.Children.Add(CajaDelEnlace());

        var aparatos = SeccionDeDispositivos();
        if (aparatos != null) lista.Children.Add(aparatos);

        var caja = new Border
        {
            Width = AnchoDelMenu,
            CornerRadius = new CornerRadius(Estudio.RadioMedio),
            Background = Estudio.Superficie,
            BorderBrush = Estudio.Borde,
            BorderThickness = new Thickness(1),
        };
        caja.Child = lista;
        _menuMicrofono.Child = Estudio.Elevar(caja, Estudio.Sombra3);
    }

    /// <summary>
    /// LOS APARATOS EMPAREJADOS CON ESTA MÁQUINA: cómo se llaman, cómo están, y cómo olvidarlos.
    /// </summary>
    /// <remarks>
    /// POR QUÉ ESTÁ AQUÍ Y NO EN UNA VENTANA PROPIA. Lo estuvo: <c>PanelDelCollar</c>. Se retiró el
    /// 2026-09-05 porque enlazar ya se hacía desde este mismo menú, y al retirarla se fueron con
    /// ella las dos únicas puertas a <c>CollarPermanente.Olvidar()</c> y a su <c>Estado</c> — o sea,
    /// se podía emparejar y no se podía deshacer. Esto lo devuelve donde de verdad se mira: al lado
    /// de la elección que esos aparatos sirven.
    ///
    /// EL NOMBRE SE ESCRIBE ENCIMA, sin botón de «renombrar» ni diálogo: «Collar Omi» no distingue
    /// un collar de otro y en un hospital se prestan. La caja parece texto hasta que se pulsa, que
    /// es lo que hace que se descubra sin tener que explicarla.
    ///
    /// Devuelve <c>null</c> cuando no hay nada emparejado: una sección vacía titulada
    /// «Dispositivos» promete algo que no está, y ofrecer «Olvidar» sobre un aparato que no existe
    /// es peor que no ofrecer nada.
    /// </remarks>
    private UIElement? SeccionDeDispositivos()
    {
        // EL HECHO, NO LA INTENCIÓN: Enlazado y no Permanente. Ver CollarPermanente.Enlazado — con
        // la intención, elegir «Collar Omi» en el menú de arriba hacía aparecer aquí un collar que
        // no existía, con su botón de olvidar (2026-09-06, lo cazó el dueño).
        var ids = DispositivosEnlazados.Listar(CollarPermanente.Enlazado);
        if (ids.Length == 0) return null;

        var caja = new StackPanel { Margin = new Thickness(6, 12, 6, 4) };
        caja.Children.Add(new Border
        {
            Height = 1,
            Background = Estudio.Borde,
            Margin = new Thickness(0, 0, 0, 12),
        });
        caja.Children.Add(Estudio.Rotulo("Dispositivos enlazados"));

        // En rejilla de dos columnas, como el diseño que dio el dueño: los aparatos son OBJETOS y
        // se reconocen por su estampa, no por una línea de lista. Hoy cabe uno —el servicio recuerda
        // un collar cada vez— y la rejilla ya está puesta para cuando sean varios.
        var rejilla = new UniformGrid { Columns = 2, Margin = new Thickness(0, 8, 0, 0) };
        foreach (string id in ids) rejilla.Children.Add(TarjetaDeDispositivo(id));
        caja.Children.Add(rejilla);
        return caja;
    }

    /// <summary>
    /// La estampa de un aparato: su retrato, su nombre —editable— y el botón de olvidarlo.
    /// </summary>
    /// <remarks>
    /// EL PUNTO VERDE NO ES ADORNO. Un collar enlazado puede estar apagado, lejos, o sin batería, y
    /// las tres cosas se ven igual que uno funcionando si no se dice: el punto separa «lo tengo
    /// emparejado» de «me está oyendo ahora». Verde vivo, gris apagado; y debajo, en palabras, lo
    /// mismo — el color solo es un atajo para quien ya lo sabe, no la única forma de saberlo.
    /// </remarks>
    private UIElement TarjetaDeDispositivo(string id)
    {
        bool vivo = CollarPermanente.Conectado;

        var tarjeta = new StackPanel { Margin = new Thickness(4, 0, 4, 6) };

        // El retrato: un disco con la silueta del collar. Es un DIBUJO y no una foto porque no hay
        // ninguna en el repo; el día que la haya, entra aquí y no cambia nada más.
        var retrato = new Grid { Width = 92, Height = 92, HorizontalAlignment = HorizontalAlignment.Center };
        retrato.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Fill = Estudio.SuperficieSuave,
            Stroke = vivo ? Estudio.Ok : Estudio.Borde,
            StrokeThickness = vivo ? 2 : 1,
        });
        var dibujo = new Canvas { Width = 44, Height = 44, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        dibujo.Children.Add(new System.Windows.Shapes.Path
        {
            Stroke = Estudio.TintaMedia,
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Data = Geometry.Parse("M 10,9 C 10,25 34,25 34,9"),   // el cordón
        });
        dibujo.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 15, Height = 15, Fill = Estudio.TintaMedia,
            Margin = new Thickness(14.5, 24, 0, 0),               // la pieza que cuelga
        });
        retrato.Children.Add(dibujo);
        tarjeta.Children.Add(retrato);

        // El nombre, editable en el sitio y centrado bajo su retrato.
        var nombre = new TextBox
        {
            Text = _nombresDeDispositivos.ComoSeLlama(id),
            FontSize = 13,
            TextAlignment = TextAlignment.Center,
            Foreground = Estudio.Tinta,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(2, 4, 2, 2),
        };

        // El nombre, editable en el sitio. Parece texto y se comporta como caja al pulsarlo.
        nombre.GotKeyboardFocus += (_, __) => { nombre.Background = Estudio.SuperficieSuave; nombre.SelectAll(); };
        void Guardar()
        {
            nombre.Background = Brushes.Transparent;
            _nombresDeDispositivos.Poner(id, nombre.Text);
            // Vacío QUITA el nombre, así que la caja tiene que volver a enseñar el de fábrica: si se
            // quedara vacía, el aparato se vería sin nombre y no habría forma de saber cuál es.
            nombre.Text = _nombresDeDispositivos.ComoSeLlama(id);
        }
        nombre.LostKeyboardFocus += (_, __) => Guardar();
        nombre.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Guardar(); Keyboard.ClearFocus(); e.Handled = true; }
            else if (e.Key == Key.Escape) { nombre.Text = _nombresDeDispositivos.ComoSeLlama(id); Keyboard.ClearFocus(); e.Handled = true; }
        };
        tarjeta.Children.Add(nombre);

        // EL ESTADO, EN PALABRAS. Es lo que decía el panel viejo y se perdió al retirarlo: un collar
        // «recordado, esperando» no es lo mismo que uno conectado, y sin decirlo los dos se ven igual.
        var estado = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 1, 0, 0),
        };
        estado.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 7, Height = 7,
            Fill = vivo ? Estudio.Ok : Estudio.TintaTenue,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0),
        });
        estado.Children.Add(new TextBlock
        {
            Text = vivo ? "Activo" : CollarPermanente.Estado,
            FontSize = 10.5,
            Foreground = vivo ? Estudio.Ok : Estudio.TintaTenue,
            VerticalAlignment = VerticalAlignment.Center,
        });
        tarjeta.Children.Add(estado);

        var olvidar = new Button
        {
            Content = new TextBlock { Text = "Olvidar", FontSize = 11.5 },
            Foreground = Estudio.TintaMedia,
            Background = Brushes.Transparent,
            BorderBrush = Estudio.Borde,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Cursor = Cursors.Hand,
            Template = Estudio.Pastilla(Estudio.RadioChico),
        };
        // El rojo solo al acercarse: en reposo sería una advertencia permanente sobre algo que
        // funciona bien, y una interfaz que grita todo el rato deja de poder decir nada.
        olvidar.MouseEnter += (_, __) => { olvidar.Background = Estudio.AlertaSuave; olvidar.Foreground = Estudio.Alerta; };
        olvidar.MouseLeave += (_, __) => { olvidar.Background = Brushes.Transparent; olvidar.Foreground = Estudio.TintaMedia; };
        olvidar.Click += (_, __) => OlvidarDispositivo(id);
        tarjeta.Children.Add(olvidar);

        return tarjeta;
    }

    /// <summary>
    /// Desenlaza un aparato: se va él, se va su nombre, y si era el que estaba oyendo, la voz vuelve
    /// al micrófono del computador.
    /// </summary>
    /// <remarks>
    /// LO ÚLTIMO NO ES UN EXTRA. Olvidar el aparato por el que estás entrando y no mover la fuente
    /// dejaría la consulta apuntando a algo que ya no existe: grabaría en silencio y lo diría al
    /// final, cuando no hay nada que recuperar. El micrófono del computador es el único que no se
    /// puede desenlazar, así que es el respaldo que siempre está.
    /// </remarks>
    private void OlvidarDispositivo(string id)
    {
        if (_consulta.Estado == EstadoDeConsulta.Grabando)
        {
            Estado("Estás grabando: para antes de desenlazar un dispositivo.");
            return;
        }

        bool mandaba = _selector.Preferida == Origen.CollarPorBluetooth;

        CollarPermanente.Olvidar();
        _nombresDeDispositivos.Olvidar(id);
        LogBus.Log("dispositivos", "collar desenlazado desde el selector de micrófono");

        if (mandaba)
        {
            _selector.Preferir(Origen.MicrofonoDelPc);
            Voice.ElMicrofonoDeLaApp.Elegir(Origen.MicrofonoDelPc, "");
            _audio.PasarAlLocal("se desenlazó el aparato que estaba oyendo");
        }

        PintarMicrofono();
        PintarMenuDeMicrofono();
    }

    /// <summary>
    /// Obliga al menú abierto a volver a colocarse.
    /// </summary>
    /// <remarks>
    /// HACE FALTA PORQUE WPF NO LO HACE SOLO (2026-09-06, lo vio el dueño: al elegir «Teléfono» el
    /// menú saltaba de vuelta a la esquina). El menú CRECE cuando aparece la caja del enlace, pero
    /// <c>Popup</c> solo pregunta a su <c>CustomPopupPlacementCallback</c> al abrirse: con el nuevo
    /// tamaño y la colocación vieja, el centrado deja de estar centrado.
    ///
    /// Mover el desplazamiento y devolverlo es el empujón conocido para que vuelva a preguntar. Es
    /// un rodeo y se dice que lo es: la alternativa —cerrar y reabrir— parpadea, y un menú que
    /// parpadea cada vez que eliges algo se siente roto.
    /// </remarks>
    private void RecolocarElMenu()
    {
        if (!_menuMicrofono.IsOpen) return;
        _menuMicrofono.HorizontalOffset += 1;
        _menuMicrofono.HorizontalOffset -= 1;
    }

    private Button FilaDeMicrofono(Origen origen, string glifo, string etiqueta,
                                   (Origen Origen, bool Entregando) real)
    {
        bool manda = _selector.Activa == origen;

        // EL VERDE ES DE QUIEN ENTREGA, no de quien está elegido (2026-09-06, lo pidió el dueño:
        // «que se ponga en verde el que realmente se está utilizando»). Elegido y oyendo son cosas
        // distintas —un collar elegido puede estar apagado— y pintarlas igual es la misma mentira
        // que hacía que la aplicación grabara por una fuente distinta de la que enseñaba.
        bool activa = ReglaDeLaFuente.SeVeActiva(origen, real.Origen, real.Entregando);

        // UNA REJILLA Y NO UNA FILA, y la diferencia se veía (2026-09-05, lo dijo el dueño: «están
        // desalineados los textos y los íconos»). Con un StackPanel horizontal, cada glifo de Segoe
        // MDL2 mide lo que mide —el del Bluetooth es estrecho, el del teléfono ancho— así que el
        // texto empezaba en una x distinta en cada línea y las tres se leían torcidas. Con una
        // columna de ancho FIJO para el icono, las tres etiquetas arrancan en el mismo sitio pase lo
        // que pase con la fuente.
        var fila = new Grid();
        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var icono = new TextBlock
        {
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            Text = glifo,
            FontSize = 14,
            Foreground = activa ? Estudio.Ok : manda ? Estudio.Acento : Estudio.TintaMedia,
            HorizontalAlignment = HorizontalAlignment.Center,   // centrado EN SU COLUMNA
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(icono, 0);
        fila.Children.Add(icono);

        var texto = new TextBlock
        {
            Text = etiqueta,
            FontSize = 14,
            FontWeight = manda ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = manda ? Estudio.Tinta : Estudio.TintaMedia,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };
        Grid.SetColumn(texto, 1);
        fila.Children.Add(texto);

        var reposo = manda ? Estudio.SuperficieSuave : Brushes.Transparent;
        var b = new Button
        {
            Content = fila,
            // ESTIRADO, y de ahí depende la alineación entera. Con el contenido centrado —lo que
            // hace la pastilla por defecto— la rejilla se encoge a su tamaño natural y se centra,
            // así que cada fila empieza donde le dice su glifo. Ver Estudio.Pastilla(estirado).
            Template = Estudio.Pastilla(Estudio.RadioChico, estirado: true),
            Height = 46,                               // una tira delgada no se deja pulsar
            Padding = new Thickness(10, 0, 10, 0),
            Margin = new Thickness(0, 2, 0, 2),
            Background = reposo,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
        };
        // EL HOVER DICE QUÉ VAS A PULSAR. Sin él, un menú de opciones iguales obliga a fiarse de la
        // puntería: se pulsa y se comprueba después cuál cayó.
        b.MouseEnter += (_, __) => { if (!manda) b.Background = Estudio.SuperficieSuave; };
        b.MouseLeave += (_, __) => b.Background = reposo;
        b.Click += (_, __) =>
        {
            // CAMBIAR DE MICRÓFONO A MEDIA GRABACIÓN dejaría la mitad del dictado en una fuente y la
            // otra mitad en otra, con el reloj partido. Se bloquea igual que el cambio de cuenta.
            if (_consulta.Estado == EstadoDeConsulta.Grabando)
            {
                _menuMicrofono.IsOpen = false;
                Estado("Estás grabando: para antes de cambiar de micrófono.");
                return;
            }

            _selector.Preferir(origen);
            // Y SE ANUNCIA A TODA LA APP (promesa 146). Hasta hoy esta elección era privada de esta
            // ventana: el médico elegía el collar aquí y al ponerse a ENSEÑAR volvía a hablarle al
            // micrófono del portátil, porque la carita tiene su propio captador y no se enteraba.
            Voice.ElMicrofonoDeLaApp.Elegir(origen, _codigo ?? "");
            // LO DE ANTES NO AVALA LO DE AHORA. Al elegir se rehace el enlace, y una trama de hace
            // dos segundos pintaría verde un collar que todavía no ha entregado nada por el enlace
            // nuevo — que es el fallo del 2026-08-25 con otro disfraz.
            _testigo.Olvida(origen);
            // LO QUE SE ELIGE ES LO QUE OYE, Y LO DEMÁS SE SUELTA. Antes, elegir el computador
            // solo dejaba de LEER el collar: seguía conectado por Bluetooth, en azul, y su audio
            // seguía llegando a la grabación. El dueño lo comprobó silenciando el micrófono del
            // portátil por teclado y viendo que la voz entraba igual (2026-09-06). En una consulta
            // clínica eso no es un detalle: quien silencia su micrófono lo hace por algo.
            //
            // Y se APAGA, no se desconecta: el servicio reintenta mientras la intención siga
            // puesta, así que desconectar sin más lo devolvería solo a los pocos segundos. Apagar
            // no pierde el emparejamiento —eso es «Enlazado», que es otra cosa desde la promesa
            // 153— así que el collar sigue en la lista y se puede volver a elegir.
            if (ReglaDeLaFuente.AlElegir(origen) == QueHacerConElCollar.Soltarlo)
                CollarPermanente.Apagar();

            switch (origen)
            {
                case Origen.CollarPorBluetooth:
                    _audio.PasarAlCollar();
                    _ = CollarPermanente.Permanente ? CollarPermanente.ConectarAsync() : CollarPermanente.EncenderAsync();
                    break;
                case Origen.CollarPorTelefono:
                    _audio.PasarAlLocal("se eligió oír por el teléfono");
                    PrepararElEnlace();
                    _ = _audio.PasarAlTelefonoAsync(Nube.ProyectoSupabase, Nube.ClavePublicable, _codigo);
                    break;
                default:
                    _audio.PasarAlLocal("lo eligió el médico: micrófono del computador");
                    break;
            }

            PintarMicrofono();
            // El menú se queda abierto al elegir teléfono: el enlace es lo siguiente que hay que
            // hacer, y cerrarlo obligaría a volver a abrirlo para nada.
            if (origen == Origen.CollarPorTelefono) { PintarMenuDeMicrofono(); RecolocarElMenu(); }
            else _menuMicrofono.IsOpen = false;
        };
        return b;
    }

    /// <summary>
    /// El enlace, a la vista y con su botón de copiar.
    ///
    /// SE ENSEÑA, no sólo se copia al portapapeles (2026-09-01, pedido por el dueño): un mensaje que
    /// diga «copiado» obliga a creerse que pasó algo invisible. Verlo y poder copiarlo otra vez es
    /// lo que hace que se entienda sin leer una sola línea de ayuda.
    /// </summary>
    private UIElement CajaDelEnlace()
    {
        var caja = new StackPanel { Margin = new Thickness(6, 10, 6, 4) };
        caja.Children.Add(Estudio.Rotulo("Pega esto en la app de Omi"));

        // NO SE ENSEÑA EL wss:// ENTERO (2026-09-06, el dueño: «estéticamente se ve muy mal»). Y no
        // es solo estética: un churro de ochenta caracteres partido en tres renglones no se lee, no
        // se comprueba de un vistazo, y ocupa el menú entero. Lo único que una persona necesita
        // reconocer de ese enlace es SU CÓDIGO —ocho letras— que es lo que distingue el suyo del de
        // otro; lo demás es fontanería y viaja igual al portapapeles.
        var codigo = new TextBox
        {
            Text = _codigo.Length > 0 ? _codigo : "…",
            IsReadOnly = true,
            FontSize = 17,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"),
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            Foreground = Estudio.Tinta,
            Background = Estudio.SuperficieSuave,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 9, 10, 9),
            Margin = new Thickness(0, 6, 0, 8),
        };
        codigo.GotFocus += (_, __) => codigo.SelectAll();
        var enlace = new TextBox { Text = _codigo.Length > 0 ? Emparejamiento.Enlace(BaseDelEnlace, _codigo) : "" };
        caja.Children.Add(codigo);

        var textoDelBoton = new TextBlock { Text = "Copiar el enlace", FontSize = 12.5, FontWeight = FontWeights.SemiBold };
        var copiar = new Button
        {
            Content = textoDelBoton,
            Foreground = Estudio.Acento,
            Background = Estudio.AcentoSuave,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(14, 9, 14, 9),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Cursor = Cursors.Hand,
            Template = Estudio.Pastilla(Estudio.RadioChico),
        };
        copiar.Click += (_, __) =>
        {
            try { Clipboard.SetText(enlace.Text); textoDelBoton.Text = "Copiado"; }
            catch (Exception e)
            {
                // El portapapeles lo puede tener tomado otra aplicación. Que falle copiar no puede
                // dejar al médico sin el enlace — por eso está a la vista y se puede copiar a mano.
                LogBus.Log("consulta-ui", "no se pudo copiar el enlace: " + e.GetType().Name + ": " + e.Message);
                textoDelBoton.Text = "Cópialo a mano";
            }
        };

        caja.Children.Add(copiar);
        return caja;
    }

    /// <summary>
    /// Emite el código de este médico si no hay uno vivo, y lo deja en el portapapeles.
    ///
    /// EL CÓDIGO SOBREVIVE AL CIERRE DE LA APP, y sin esto la función no servía para trabajar: cada
    /// arranque emitía uno nuevo, así que el médico tendría que volver a pegar el enlace en el
    /// teléfono cada mañana. Un emparejamiento que hay que rehacer a diario no es un emparejamiento.
    /// Vive las 8 horas del turno y después se renueva solo.
    /// </summary>
    private void PrepararElEnlace()
    {
        var ahora = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (_codigo.Length == 0 || !Emparejamiento.Vivo(_codigoEmitidoMs, ahora))
        {
            _codigo = Emparejamiento.Nuevo();
            _codigoEmitidoMs = ahora;
            GuardarElCodigo();
            LogBus.Log("telefono", $"código de emparejamiento nuevo · vive {Emparejamiento.VidaMs / 3600000} h");
        }
        try { Clipboard.SetText(Emparejamiento.Enlace(BaseDelEnlace, _codigo)); } catch { /* está a la vista */ }
    }

    private static string RutaDelCodigo =>
        System.IO.Path.Combine(UserPaths.Roaming, "U", "omi-telefono.json");

    private void GuardarElCodigo()
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(RutaDelCodigo);
            if (dir != null) System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(RutaDelCodigo,
                System.Text.Json.JsonSerializer.Serialize(new { codigo = _codigo, emitido = _codigoEmitidoMs }));
        }
        catch (Exception e) { LogBus.Log("telefono", $"no se pudo guardar el código: {e.GetType().Name}: {e.Message}"); }
    }

    private void RestaurarElCodigo()
    {
        try
        {
            if (!System.IO.File.Exists(RutaDelCodigo)) return;
            using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(RutaDelCodigo));
            var raiz = doc.RootElement;
            var codigo = raiz.TryGetProperty("codigo", out var c) ? c.GetString() ?? "" : "";
            var emitido = raiz.TryGetProperty("emitido", out var e) ? e.GetInt64() : 0;
            if (codigo.Length == 0) return;

            // Un código caducado no se restaura: dejarlo puesto haría que el enlace del teléfono
            // siguiera pareciendo válido cuando el receptor ya lo rechaza.
            if (!Emparejamiento.Vivo(emitido, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())) return;

            _codigo = codigo;
            _codigoEmitidoMs = emitido;
        }
        catch (Exception e) { LogBus.Log("telefono", $"no se pudo leer el código guardado: {e.GetType().Name}: {e.Message}"); }
    }

    /// <summary>
    /// Pinta el icono del micrófono: qué fuente manda y si está entregando de verdad.
    ///
    /// EL COLOR LO DA <see cref="Vigia"/>, NUNCA EL ESTADO DE LA CONEXIÓN. Es la promesa 29 y tiene
    /// fecha: el 2026-08-25 el panel decía «conectado», la carita pintaba azul, y pasaron 56 minutos
    /// sin una sola trama. Verde aquí significa exactamente una cosa: llegó audio hace menos de tres
    /// segundos.
    /// </summary>
    /// <summary>
    /// DEJA QUE SE LE CAMBIE EL TAMAÑO ARRASTRANDO SUS BORDES.
    /// </summary>
    /// <remarks>
    /// <c>ResizeMode.CanResize</c> ya estaba puesto y aun así la ventana no se dejaba agarrar. Dos
    /// razones que se suman y ninguna se ve mirando esa línea:
    ///
    ///   · con <c>WindowStyle.None</c> + <c>AllowsTransparency</c> la ventana NO TIENE área no
    ///     cliente, que es donde Windows pone sus tiradores. Sin marco no hay dónde agarrar, así
    ///     que hay que contestar a <c>WM_NCHITTEST</c> a mano;
    ///   · y el borde que se ve está 24 px por dentro del borde de la ventana — el hueco que la
    ///     sombra necesita (promesa 154). El cálculo vive en <see cref="ReglaDelBorde"/>.
    ///
    /// Se engancha en <c>SourceInitialized</c> y no antes: hasta que no hay HWND no hay a qué
    /// engancharse.
    /// </remarks>
    private void DejarseAgarrarPorElBorde()
    {
        const int WM_NCHITTEST = 0x0084;

        SourceInitialized += (_, __) =>
        {
            var fuente = (System.Windows.Interop.HwndSource)PresentationSource.FromVisual(this)!;
            fuente.AddHook((IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool manejado) =>
            {
                if (msg != WM_NCHITTEST) return IntPtr.Zero;

                // lParam trae la posición en píxeles de PANTALLA y con signo (un monitor a la
                // izquierda da negativos): el cast a short es lo que lo respeta.
                int px = unchecked((short)(long)lp), py = unchecked((short)((long)lp >> 16));
                var enVentana = PointFromScreen(new Point(px, py));

                // La caja de lo que se VE: la ventana menos el hueco de la sombra.
                var hueco = Estudio.HolguraDe(Estudio.Sombra3);
                var tarjeta = new Rect(hueco.Left, hueco.Top,
                    Math.Max(0, ActualWidth - hueco.Left - hueco.Right),
                    Math.Max(0, ActualHeight - hueco.Top - hueco.Bottom));

                int codigo = ReglaDelBorde.De(enVentana, tarjeta) switch
                {
                    ZonaDelBorde.Izquierda => 10,        // HTLEFT
                    ZonaDelBorde.Derecha => 11,          // HTRIGHT
                    ZonaDelBorde.Arriba => 12,           // HTTOP
                    ZonaDelBorde.ArribaIzquierda => 13,  // HTTOPLEFT
                    ZonaDelBorde.ArribaDerecha => 14,    // HTTOPRIGHT
                    ZonaDelBorde.Abajo => 15,            // HTBOTTOM
                    ZonaDelBorde.AbajoIzquierda => 16,   // HTBOTTOMLEFT
                    ZonaDelBorde.AbajoDerecha => 17,     // HTBOTTOMRIGHT
                    _ => 0,
                };
                if (codigo == 0) return IntPtr.Zero;   // no es borde: que lo trate WPF como siempre

                manejado = true;
                return new IntPtr(codigo);
            });
        };
    }

    /// <summary>
    /// Quién manda AHORA y si está entregando audio. En UN solo sitio: el icono grande y las tres
    /// filas del menú tienen que decir lo mismo, y dos cálculos del mismo hecho acaban discrepando
    /// (aprendizaje nº16).
    /// </summary>
    private (Origen Origen, bool Entregando) FuenteReal()
    {
        var ahora = Environment.TickCount64;

        // DISPONIBLE ES ENTREGANDO, no «elegido». Que el canal se una no prueba que llegue audio:
        // el collar puede estar apagado al otro lado. LiveAudio sólo pone PorElTelefono cuando ha
        // recibido la primera trama de verdad.
        var origen = _selector.Decidir(CollarPermanente.Conectado, _audio.PorElTelefono, ahora);
        return (origen, _vigia.HayMicrofono(EnlaceEnPie(origen), _testigo.UltimaTrama(origen), ahora));
    }

    /// <summary>¿El canal de esa fuente está en pie? El micrófono del PC lo está siempre.</summary>
    private bool EnlaceEnPie(Origen origen) => origen switch
    {
        Origen.CollarPorBluetooth => CollarPermanente.Conectado,
        Origen.CollarPorTelefono => _audio.PorElTelefono,
        _ => true,
    };

    private void PintarMicrofono()
    {
        var (origen, entregando) = FuenteReal();
        bool enlaceEnPie = EnlaceEnPie(origen);
        bool grabando = _consulta.Estado == EstadoDeConsulta.Grabando;

        _iconoMicrofono.Text = origen switch
        {
            Origen.CollarPorBluetooth => GlifoBluetooth,
            Origen.CollarPorTelefono => GlifoTelefono,
            _ => GlifoMicrofono,
        };

        // EL VERDE SÓLO APARECE GRABANDO, y no es una limitación: sin grabar el micrófono está
        // cerrado, así que no hay tramas que puedan probar nada. Lo demás sería adornar.
        //
        // Y el verde NO consulta el enlace, sólo la última trama (ver Omi.Vigia): el 2026-09-01 el
        // collar entregó 4.815 tramas con el icono en gris porque `Conectado` seguía en false — sale
        // de un evento de transición que no dispara al abrir sobre un aparato ya conectado.
        _iconoMicrofono.Foreground =
            entregando ? Estudio.Ok
            : grabando ? Estudio.Alerta
            : origen != Origen.MicrofonoDelPc && !enlaceEnPie ? Estudio.Espera
            : Estudio.TintaMedia;
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

        pila.Children.Add(ConstruirNombreEditable());
        pila.Children.Add(SeparadorMenu());

        pila.Children.Add(ItemDeMenu("Cambiar de cuenta", puede,
            () => _ = CambiarCuentaAsync(creando: false)));
        pila.Children.Add(ItemDeMenu("Agregar cuenta", puede,
            () => _ = CambiarCuentaAsync(creando: true)));
        pila.Children.Add(SeparadorMenu());
        pila.Children.Add(ItemDeMenu("Cerrar sesión", puede, CerrarSesion));

        tarjeta.Child = pila;
        // Margen extra para que la sombra del menú no se recorte contra el borde del Popup: un
        // Popup se dimensiona justo al contenido, y sin este aire el desenfoque queda cortado.
        var elevado = Estudio.Elevar(tarjeta, Estudio.Sombra2);
        elevado.Margin = new Thickness(12);
        return elevado;
    }

    /// <summary>
    /// El nombre, editable. Es la respuesta a «no guardo mi nombre, quedo con mi correo»: una
    /// cuenta que se creó sin nombre —o que nunca lo tuvo— antes no tenía NINGÚN sitio donde
    /// ponérselo. Se guarda al pulsar Enter o al salir del campo; nunca al escribir letra a letra,
    /// que gastaría una llamada de red por tecla.
    /// </summary>
    private UIElement ConstruirNombreEditable()
    {
        var cabecera = new StackPanel { Margin = new Thickness(10, 8, 10, 8) };

        var caja = new TextBox
        {
            Text = _sesion.MedicoNombre,
            Foreground = Estudio.Tinta, FontSize = 13.5, FontWeight = FontWeights.SemiBold,
            Background = Brushes.Transparent,
            // El filete de abajo es la única pista de que esto se puede tocar: un TextBox sin
            // ningún borde no se distingue de una etiqueta.
            BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = Estudio.Borde,
            Padding = new Thickness(0, 0, 0, 3),
            CaretBrush = Estudio.Acento, SelectionBrush = Estudio.Acento,
        };

        var estado = new TextBlock
        {
            FontSize = 10.5, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed,
        };

        bool guardando = false;
        async Task GuardarSiCambioAsync()
        {
            string nuevo = caja.Text.Trim();
            // Comparar contra MedicoNombre y no contra el valor con el que se abrió la caja: tras
            // guardar bien, MedicoNombre ya es el nuevo, así que un segundo disparo (Enter y luego
            // LostFocus) no vuelve a gastar una llamada.
            if (guardando || nuevo.Length == 0 || nuevo == _sesion.MedicoNombre) return;
            guardando = true;
            estado.Text = "Guardando…"; estado.Foreground = Estudio.TintaTenue;
            estado.Visibility = Visibility.Visible;
            try
            {
                var (ok, mensaje) = await _sesion.GuardarNombreAsync(nuevo);
                if (!caja.IsLoaded) return;   // el menú se cerró mientras se guardaba
                if (ok)
                {
                    estado.Visibility = Visibility.Collapsed;
                    _quien.Text = _sesion.MedicoNombre;   // refresca la cabecera de la ventana
                }
                else
                {
                    estado.Text = mensaje; estado.Foreground = Estudio.Alerta;
                }
            }
            finally { guardando = false; }
        }

        caja.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            await GuardarSiCambioAsync();
        };
        caja.LostFocus += async (_, __) => await GuardarSiCambioAsync();

        cabecera.Children.Add(caja);
        cabecera.Children.Add(new TextBlock
        {
            Text = _sesion.MedicoEmail, Foreground = Estudio.TintaTenue, FontSize = 11.5,
            Margin = new Thickness(0, 6, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis,
        });
        cabecera.Children.Add(estado);
        return cabecera;
    }

    /// <remarks>
    /// Hasta el 2026-09-06 llevaba un cuarto argumento con el motivo de estar bloqueado, y ese
    /// motivo solo se leia al pasar el raton. Al irse los carteles (promesa 164) se fue con ellos:
    /// un parametro que nadie lee es codigo inerte, y aqui ya sabemos lo que cuesta dejarlo.
    /// </remarks>
    private UIElement ItemDeMenu(string texto, bool activo, Action accion)
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
        _enAprendizajes = false;
        _panelAprendizajes.Visibility = Visibility.Collapsed;
        _panelNota.Visibility = nota ? Visibility.Visible : Visibility.Collapsed;
        _listaConsultas.Visibility = nota ? Visibility.Collapsed : Visibility.Visible;
        PintarPestanas();
        PintarElCerebro();
        _superficie.ScrollToHome();
    }

    // ── APRENDIZAJES (spec 016) ───────────────────────────────────────────────────────────
    //
    // Lo que entra aquí y lo que no está escrito en la spec, y el criterio es uno: entra lo que
    // hace falta para CONFIAR en algo que va a escribir en una historia clínica. Por eso no hay
    // contadores de pasos, ni selectores, ni insignia verde en lo que ya está listo —lo normal no
    // se decora—, ni los workflows del grafo, que son otra cosa y son justo lo que hace ilegible
    // el panel viejo de la carita.

    private void AbrirAprendizajes()
    {
        _enAprendizajes = true;
        _panelNota.Visibility = Visibility.Collapsed;
        _listaConsultas.Visibility = Visibility.Collapsed;
        _panelAprendizajes.Visibility = Visibility.Visible;
        PintarPestanas();
        PintarElCerebro();
        PintarLaLista();
        _superficie.ScrollToHome();
    }

    private void PintarElCerebro()
    {
        _aprendizajes.Background = _enAprendizajes ? Estudio.AcentoSuave : Brushes.Transparent;
        if (_aprendizajes.Content is FrameworkElement dibujo)
            dibujo.SetValue(System.Windows.Shapes.Shape.StrokeProperty, _enAprendizajes ? Estudio.Acento : Estudio.TintaMedia);
    }

    private void PintarLaLista()
    {
        _panelAprendizajes.Children.Clear();
        var catalogo = Navigation.SkillEnsenada.Catalogo(Navigation.SkillEnsenada.CarpetaPorDefecto);
        var listos = catalogo.Where(c => c.Comprobada).ToList();
        var pendientes = catalogo.Where(c => !c.Comprobada).ToList();

        _panelAprendizajes.Children.Add(TituloDeVista("Aprendizajes",
            listos.Count == 0 ? "" : listos.Count == 1 ? "1 listo" : $"{listos.Count} listos"));

        if (catalogo.Count == 0)
        {
            _panelAprendizajes.Children.Add(TarjetaVacia("Todavía no me has enseñado nada.",
                "Pon delante la aplicación que quieras enseñarme, pulsa 🎓 en la carita y hazlo una "
                + "vez hablando. Al terminar aparece aquí."));
            return;
        }
        foreach (var c in listos) _panelAprendizajes.Children.Add(FilaDeAprendizaje(c));
        if (listos.Count == 0)
            _panelAprendizajes.Children.Add(TarjetaVacia("Nada repasado todavía.",
                "Abre uno de los de abajo y pulsa «Mostrar»: lo hago una vez contigo mirando, y a "
                + "partir de ahí puedo usarlo solo."));
        if (pendientes.Count == 0) return;

        var plegado = new Button
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand, Margin = new Thickness(2, 8, 2, 0), Padding = new Thickness(12, 13, 12, 13),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Template = Estudio.Pastilla(14, estirado: true),
        };
        var lineaPlegado = new DockPanel();
        lineaPlegado.Children.Add(new TextBlock
        {
            Text = _pendientesALaVista ? "▾" : "›",
            Foreground = Estudio.TintaTenue, FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
        });
        DockPanel.SetDock(lineaPlegado.Children[0], Dock.Right);
        lineaPlegado.Children.Add(new TextBlock
        {
            Text = $"Sin repasar · {pendientes.Count}",
            Foreground = Estudio.TintaMedia, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center,
        });
        plegado.Content = lineaPlegado;
        plegado.Click += (_, __) => { _pendientesALaVista = !_pendientesALaVista; PintarLaLista(); };
        _panelAprendizajes.Children.Add(new Border
        {
            BorderBrush = Estudio.Borde, BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(4, 10, 4, 0), Child = plegado,
        });
        if (!_pendientesALaVista) return;
        foreach (var c in pendientes) _panelAprendizajes.Children.Add(FilaDeAprendizaje(c));
    }

    /// <summary>Una fila: el nombre y una línea. Nada más — si no se entiende en una línea, el
    /// problema es el nombre, no la ficha.</summary>
    private UIElement FilaDeAprendizaje(Navigation.SkillAnunciada c)
    {
        var texto = new StackPanel();
        texto.Children.Add(new TextBlock
        {
            Text = c.Nombre, Foreground = Estudio.Tinta, FontSize = 14, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 5),
        });
        texto.Children.Add(new TextBlock
        {
            Text = UnaLinea(c.Description), Foreground = Estudio.TintaMedia, FontSize = 12.5,
            LineHeight = 19, TextWrapping = TextWrapping.Wrap, MaxHeight = 40,
        });
        var fila = new DockPanel();
        fila.Children.Add(new TextBlock
        {
            Text = "›", Foreground = Estudio.TintaTenue, FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0),
        });
        DockPanel.SetDock(fila.Children[0], Dock.Right);
        fila.Children.Add(texto);

        var boton = new Button
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
            Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Template = Estudio.Pastilla(18, estirado: true), Content = fila,
        };
        boton.Click += (_, __) => PintarFicha(c);

        var tarjeta = Estudio.Tarjeta(18);
        tarjeta.Padding = new Thickness(16, 15, 16, 16);
        tarjeta.Margin = new Thickness(2, 0, 2, 10);
        tarjeta.Child = boton;
        return Estudio.Elevar(tarjeta);
    }

    /// <summary>La ficha: una sola acción, y debajo qué hace y qué datos necesita.</summary>
    private void PintarFicha(Navigation.SkillAnunciada anunciada)
    {
        var skill = Navigation.SkillEnsenada.Cargar(anunciada.Archivo);
        _panelAprendizajes.Children.Clear();
        _panelAprendizajes.Children.Add(Volver("Aprendizajes", PintarLaLista));
        if (skill == null)
        {
            _panelAprendizajes.Children.Add(TarjetaVacia("No puedo abrir este aprendizaje.",
                $"El archivo está en disco pero no se deja leer: {Path.GetFileName(anunciada.Archivo)}"));
            return;
        }

        _panelAprendizajes.Children.Add(new TextBlock
        {
            Text = skill.Nombre, Foreground = Estudio.Tinta, FontSize = 19, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, LineHeight = 24, Margin = new Thickness(2, 0, 2, 8),
        });
        if (skill.Description.Length > 0)
            _panelAprendizajes.Children.Add(new TextBlock
            {
                Text = skill.Description, Foreground = Estudio.TintaMedia, FontSize = 13, LineHeight = 21,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 2, 18),
            });

        // LA ÚNICA ACCIÓN. Qué hace al pulsarla lo decide una regla que el contrato juzga
        // (promesa 225): lo repasado se corre, lo no repasado se repasa, y sin manos se dice.
        var decision = Navigation.LoQuePasaAlMostrar.Decidir(skill, PuenteDeAprendizajes.Disponible);
        var mostrar = new Button
        {
            Content = new TextBlock { Text = "Mostrar", FontSize = 14.5, FontWeight = FontWeights.SemiBold },
            Height = 46, Margin = new Thickness(2, 0, 2, 0), Cursor = Cursors.Hand,
            Background = decision.Que == "no" ? Estudio.SuperficieSuave : Estudio.Acento,
            Foreground = decision.Que == "no" ? Estudio.TintaTenue : Brushes.White,
            BorderThickness = new Thickness(0), IsEnabled = decision.Que != "no",
            Template = Estudio.Pastilla(23, estirado: true),
        };
        mostrar.Click += async (_, __) => await MostrarAprendizajeAsync(anunciada, mostrar);
        _panelAprendizajes.Children.Add(mostrar);
        _panelAprendizajes.Children.Add(new TextBlock
        {
            Text = decision.Motivo.Length > 0
                ? decision.Motivo
                : "Lo hago en SAP mientras miras, narrando cada paso. No grabo nada: me detengo antes.",
            Foreground = Estudio.TintaTenue, FontSize = 11.5, LineHeight = 17, TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 9, 10, 20),
        });

        // QUÉ HACE, en castellano y sin un solo selector (promesa 199).
        var frases = Navigation.LoQueHaceLaSkill.EnCastellano(skill);
        if (frases.Count > 0)
        {
            _panelAprendizajes.Children.Add(Rotulo("Qué hace"));
            var pila = new StackPanel { Margin = new Thickness(2, 0, 2, 0) };
            void PintarPasos(int cuantos)
            {
                pila.Children.Clear();
                for (int i = 0; i < Math.Min(cuantos, frases.Count); i++) pila.Children.Add(LineaDePaso(i + 1, frases[i]));
                if (cuantos >= frases.Count) return;
                var mas = Enlace($"Ver los {frases.Count} pasos ›");
                mas.Click += (_, __) => PintarPasos(frases.Count);
                pila.Children.Add(mas);
            }
            PintarPasos(6);
            _panelAprendizajes.Children.Add(pila);
        }

        // QUÉ DATOS NECESITA: es lo que decide si la nota clínica puede usarlo.
        var datos = skill.Huecos.Select(h => h.Significado.Trim()).Where(x => x.Length > 0).Distinct().ToList();
        if (datos.Count > 0)
        {
            _panelAprendizajes.Children.Add(Rotulo("Datos que necesita"));
            var fichas = new WrapPanel { Margin = new Thickness(2, 0, 2, 20) };
            foreach (string d in datos) fichas.Children.Add(Ficha(d));
            _panelAprendizajes.Children.Add(fichas);
        }

        // LAS CAPTURAS, BAJO UN TOQUE. Nunca de entrada: son la excepción, no la portada.
        string carpeta = CarpetaDeSuLeccion(skill);
        if (carpeta.Length > 0)
        {
            var ver = FilaDeAccion("Ver capturas del paso a paso");
            ver.Click += (_, __) => PintarCapturas(anunciada, skill, carpeta);
            _panelAprendizajes.Children.Add(ver);
        }
        else
            _panelAprendizajes.Children.Add(new TextBlock
            {
                Text = "De esta tarea no guardé capturas: la aprendí antes de que supiera conservarlas.",
                Foreground = Estudio.TintaTenue, FontSize = 11.5, LineHeight = 17, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 4, 2, 0),
            });

        // Renombrar y borrar existen, pero no compiten: al fondo y en gris.
        var quietos = new StackPanel
        {
            Orientation = Orientation.Horizontal, Margin = new Thickness(2, 22, 2, 4),
        };
        var renombrar = Enlace("Renombrar", Estudio.TintaTenue);
        renombrar.Click += (_, __) => PedirNombre(anunciada);
        var borrar = Enlace("Eliminar", Estudio.Alerta);
        borrar.Margin = new Thickness(18, 0, 0, 0);
        borrar.Click += (_, __) => ConfirmarBorrado(anunciada);
        quietos.Children.Add(renombrar);
        quietos.Children.Add(borrar);
        _panelAprendizajes.Children.Add(quietos);
        _superficie.ScrollToHome();
    }

    private void PintarCapturas(Navigation.SkillAnunciada anunciada, Navigation.SkillEnsenada skill, string carpeta)
    {
        _panelAprendizajes.Children.Clear();
        _panelAprendizajes.Children.Add(Volver(Recorte(skill.Nombre, 26), () => PintarFicha(anunciada)));
        _panelAprendizajes.Children.Add(new TextBlock
        {
            Text = "Paso a paso", Foreground = Estudio.Tinta, FontSize = 17, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(2, 0, 2, 6),
        });
        _panelAprendizajes.Children.Add(new TextBlock
        {
            Text = "Lo que se vio en la pantalla cuando me lo enseñaste, en orden.",
            Foreground = Estudio.TintaMedia, FontSize = 13, LineHeight = 20, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 0, 2, 16),
        });

        var leccion = Teach.LeccionEnDisco.Cargar(carpeta);
        var capturas = Navigation.CapturasDeLaSkill.De(skill, leccion!)
            .Where(c => c.Cuadro.Length > 0 && File.Exists(c.Cuadro)).ToList();
        if (capturas.Count == 0)
        {
            _panelAprendizajes.Children.Add(TarjetaVacia("No encuentro las capturas.",
                "La lección de la que salió esta tarea ya no tiene sus cuadros en disco."));
            return;
        }
        foreach (var c in capturas) _panelAprendizajes.Children.Add(UnaCaptura(c));
        _superficie.ScrollToHome();
    }

    private UIElement UnaCaptura(Navigation.Captura c)
    {
        var pila = new StackPanel();
        var imagen = new Image
        {
            Source = CuadroDeDisco(c.Cuadro), Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var recorte = new Border
        {
            CornerRadius = new CornerRadius(12), BorderBrush = Estudio.Borde,
            BorderThickness = new Thickness(1), ClipToBounds = true, Child = imagen,
            Margin = new Thickness(0, 0, 0, 9),
        };
        pila.Children.Add(recorte);
        pila.Children.Add(new TextBlock
        {
            Text = $"{c.Paso} · {c.Que}", Foreground = Estudio.Tinta, FontSize = 12, LineHeight = 18,
            TextWrapping = TextWrapping.Wrap,
        });
        var t = Estudio.Tarjeta(18);
        t.Padding = new Thickness(14, 14, 14, 15);
        t.Margin = new Thickness(2, 0, 2, 12);
        t.Child = pila;
        return Estudio.Elevar(t);
    }

    /// <summary>
    /// El cuadro, leído entero y soltado. Sin <c>OnLoad</c> WPF deja el archivo abierto mientras la
    /// imagen viva, y la carpeta de una lección no se podría borrar nunca.
    /// </summary>
    private static ImageSource? CuadroDeDisco(string ruta)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(ruta);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 760;   // el ancho útil de la ventana, con holgura para pantallas densas
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception e)
        {
            LogBus.Log("aprendizajes", $"no pude abrir el cuadro «{ruta}»: {e.GetType().Name}: {e.Message}");
            return null;
        }
    }

    private async Task MostrarAprendizajeAsync(Navigation.SkillAnunciada anunciada, Button boton)
    {
        if (_mostrando) return;
        if (!PuenteDeAprendizajes.Disponible) { Estado("La carita todavía no está lista."); return; }
        _mostrando = true;
        string antes = ((TextBlock)boton.Content).Text;
        ((TextBlock)boton.Content).Text = "Mostrando…";
        boton.IsEnabled = false;
        try
        {
            LogBus.Log("aprendizajes", $"mostrar «{anunciada.Nombre}» · {anunciada.Archivo}");
            string cuenta = await PuenteDeAprendizajes.Mostrar!(anunciada.Archivo,
                new Progress<string>(Estado), CancellationToken.None);
            Estado(cuenta);
            LogBus.Log("aprendizajes", $"← {cuenta}");
        }
        catch (Exception e)
        {
            Estado($"Se detuvo: {e.Message}");
            LogBus.Log("aprendizajes", $"mostrar reventó: {e.GetType().Name}: {e.Message}");
        }
        finally
        {
            _mostrando = false;
            ((TextBlock)boton.Content).Text = antes;
            boton.IsEnabled = true;
            PintarLaLista();
        }
    }

    private void PedirNombre(Navigation.SkillAnunciada anunciada)
    {
        _panelAprendizajes.Children.Clear();
        _panelAprendizajes.Children.Add(Volver("Aprendizajes", PintarLaLista));
        _panelAprendizajes.Children.Add(Rotulo("Cómo se llama"));
        var caja = new TextBox
        {
            Text = anunciada.Nombre, FontSize = 14, Height = 42, Margin = new Thickness(2, 0, 2, 14),
            VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(12, 0, 12, 0),
            Background = Estudio.SuperficieSuave, BorderThickness = new Thickness(0),
            Foreground = Estudio.Tinta,
        };
        _panelAprendizajes.Children.Add(caja);
        var guardar = BotonPrincipal("Guardar");
        guardar.Click += (_, __) =>
        {
            string nuevo = caja.Text.Trim();
            if (nuevo.Length == 0) { Estado("Un nombre vacío no sirve para encontrarlo después."); return; }
            string archivo = Navigation.SkillEnsenada.Renombrar(anunciada.Archivo, nuevo);
            LogBus.Log("aprendizajes", $"renombrado «{anunciada.Nombre}» → «{nuevo}» · {archivo}");
            PintarLaLista();
        };
        _panelAprendizajes.Children.Add(guardar);
        caja.Focus();
        caja.SelectAll();
    }

    private void ConfirmarBorrado(Navigation.SkillAnunciada anunciada)
    {
        _panelAprendizajes.Children.Clear();
        _panelAprendizajes.Children.Add(Volver("Aprendizajes", PintarLaLista));
        _panelAprendizajes.Children.Add(new TextBlock
        {
            Text = $"¿Elimino «{anunciada.Nombre}»?", Foreground = Estudio.Tinta, FontSize = 17,
            FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 0, 2, 8),
        });
        _panelAprendizajes.Children.Add(new TextBlock
        {
            Text = "Se borra de este computador y no se puede deshacer. Volver a enseñármelo son dos minutos.",
            Foreground = Estudio.TintaMedia, FontSize = 13, LineHeight = 20, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 0, 2, 18),
        });
        var si = BotonPrincipal("Eliminar", Estudio.Alerta);
        si.Click += (_, __) =>
        {
            bool ok = Navigation.SkillEnsenada.Borrar(anunciada.Archivo);
            LogBus.Log("aprendizajes", ok ? $"eliminado «{anunciada.Nombre}»" : $"no pude eliminar «{anunciada.Nombre}»");
            Estado(ok ? $"Eliminado «{Recorte(anunciada.Nombre, 30)}»." : "No pude eliminarlo; mira el registro.");
            PintarLaLista();
        };
        _panelAprendizajes.Children.Add(si);
        var no = Enlace("Dejarlo como está");
        no.HorizontalAlignment = HorizontalAlignment.Center;
        no.Margin = new Thickness(0, 14, 0, 0);
        no.Click += (_, __) => PintarFicha(anunciada);
        _panelAprendizajes.Children.Add(no);
    }

    /// <summary>La carpeta de la lección de la que salió, o vacío si no la sabe o ya no está.</summary>
    private static string CarpetaDeSuLeccion(Navigation.SkillEnsenada skill)
    {
        if (string.IsNullOrWhiteSpace(skill.DeLaLeccion)) return "";
        string carpeta = Path.Combine(Teach.LeccionEnDisco.CarpetaRaiz, skill.DeLaLeccion.Trim());
        return File.Exists(Path.Combine(carpeta, "leccion.json")) ? carpeta : "";
    }

    private static string UnaLinea(string texto)
    {
        string t = (texto ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        while (t.Contains("  ")) t = t.Replace("  ", " ");
        return t.Length == 0 ? "Sin descripción." : t;
    }

    private static string Recorte(string t, int n) =>
        (t ?? "").Length <= n ? (t ?? "") : (t ?? "")[..n].TrimEnd() + "…";

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
        // EN APRENDIZAJES NO MANDA NINGUNA DE LAS DOS. Medido en la máquina el 2026-09-11: con
        // el panel abierto, el carril seguía pintando «Nota» como activa, así que la interfaz
        // decía que estabas en un sitio distinto del que estabas. Un carril que miente es peor
        // que un carril apagado.
        Pintar(_tabNota, !_enAprendizajes && _enNota);
        Pintar(_tabConsultas, !_enAprendizajes && !_enNota);
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
            // SE GRABA CON LO QUE HAY, PERO SE DICE CUÁL ES. El 2026-09-01 el dueño eligió
            // «Teléfono», pulsó grabar, y la consulta se grabó con el micrófono del computador sin
            // que nada lo dijera: la fuente elegida no entregaba y el sistema se cayó al respaldo en
            // silencio. Eso es el aprendizaje nº10 en una pantalla — lo peor no es que falle, es que
            // parezca que funcionó.
            //
            // No se BLOQUEA la grabación, y es a propósito: un médico con el paciente delante
            // prefiere la nota tomada con el micrófono del portátil antes que un botón que se niega.
            // Lo que no puede pasar es que no se entere.
            if (_selector.Preferida is { } pedida && _selector.Activa != pedida)
                Estado($"Grabando con «{Omi.Selector.Nombre((int)_selector.Activa)}»: "
                     + $"«{Omi.Selector.Nombre((int)pedida)}» no está entregando audio.");
            else
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
        _estadoDeSeccion.Clear();
        var conTexto = new List<SeccionDeNota>();
        foreach (var s in nota.Secciones)
        {
            // Una casilla vacía no es información: la plantilla abierta deja en blanco lo que no se
            // dijo, y pintar «—» sería llenar la pantalla de nada.
            if (s.Contenido.Trim().Length == 0) continue;
            conTexto.Add(s);
            _nota.Children.Add(TarjetaConEnvio(s));
        }
        if (conTexto.Count > 1) _nota.Children.Add(BotonTodoASap(conTexto));
        if (nota.Avisos.Count > 0)
            _nota.Children.Add(TarjetaDeTexto("Avisos", string.Join("\n", nota.Avisos)));
        _superficie.ScrollToHome();
    }

    // ── el ✓: la sección aprobada se va a SAP (spec 008) ─────────────────────

    /// <summary>El renglón de estado de cada sección, para pintar «enviando…» y la cuenta.</summary>
    private readonly Dictionary<string, TextBlock> _estadoDeSeccion = new();
    private bool _enviando;

    /// <summary>
    /// Una sección con su ✓. Pulsarlo es aprobarla: solo ella viaja (promesa 112). El resultado se
    /// pinta debajo del texto, en la misma tarjeta, para que se vea qué pasó con ESA sección.
    /// </summary>
    private UIElement TarjetaConEnvio(SeccionDeNota s)
    {
        var cabecera = new DockPanel();
        var check = new Button
        {
            Width = 30,
            Height = 30,
            Background = Estudio.AcentoSuave,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Template = Estudio.Pastilla(15),
            Content = new TextBlock
            {
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                Text = "",
                FontSize = 13,
                Foreground = Estudio.Acento,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        DockPanel.SetDock(check, Dock.Right);
        cabecera.Children.Add(check);
        var rotulo = Estudio.Rotulo(s.Titulo);
        rotulo.VerticalAlignment = VerticalAlignment.Center;
        cabecera.Children.Add(rotulo);

        var estado = new TextBlock
        {
            Foreground = Estudio.TintaMedia,
            FontSize = 12,
            LineHeight = 17,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        _estadoDeSeccion[s.Clave] = estado;

        check.Click += async (_, __) => await EnviarASapAsync(new[] { s.Clave });

        var pila = new StackPanel();
        pila.Children.Add(cabecera);
        pila.Children.Add(Estudio.Parrafo(s.Contenido));
        pila.Children.Add(estado);
        var t = Estudio.Tarjeta(18);
        t.Padding = new Thickness(16, 12, 16, 15);
        t.Margin = new Thickness(2, 0, 2, 10);
        t.Child = pila;
        return Estudio.Elevar(t);
    }

    private UIElement BotonTodoASap(IReadOnlyList<SeccionDeNota> secciones)
    {
        var b = new Button
        {
            Content = "✓ Todo a SAP",
            Height = 36,
            MinWidth = 150,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 12),
            Background = Estudio.AcentoSuave,
            Foreground = Estudio.Acento,
            BorderThickness = new Thickness(0),
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand,
            Template = Estudio.Pastilla(18),
        };
        b.Click += async (_, __) => await EnviarASapAsync(secciones.Select(x => x.Clave).ToList());
        return b;
    }

    /// <summary>
    /// Manda lo marcado por el puente. La cuenta que vuelve es la que se pinta: la lleva el código
    /// que releyó cada campo, no una frase de nadie.
    /// </summary>
    private async Task EnviarASapAsync(IReadOnlyList<string> claves)
    {
        if (_enviando) { Estado("Ya hay un envío en marcha."); return; }
        if (_consulta.Nota == null) { Estado("No hay nota que enviar."); return; }
        if (!PuenteASap.Disponible)
        {
            Estado("La carita no está lista para escribir en SAP: espera a que arranque y vuelve a pulsar ✓.");
            return;
        }

        var encargo = Encargo.De(_consulta.Nota, claves);
        if (encargo.EstaVacio) { Estado("Esa sección está vacía: no hay nada que enviar."); return; }

        _enviando = true;
        void Pinta(string texto)
        {
            Estado(texto);
            foreach (string c in claves)
                if (_estadoDeSeccion.TryGetValue(c, out var tb)) { tb.Text = texto; tb.Visibility = Visibility.Visible; }
        }
        try
        {
            Pinta("Enviando a SAP…");
            LogBus.Log("consulta", $"✓ enviado: {string.Join(", ", claves)}");
            string cuenta = await PuenteASap.Enviar!(encargo, new Progress<string>(Pinta), CancellationToken.None);
            Pinta(cuenta);
            LogBus.Log("consulta", $"cuenta del envío: {cuenta}");
        }
        catch (Exception e)
        {
            Pinta($"El envío se detuvo: {e.Message}");
            LogBus.Log("consulta", $"el envío reventó: {e.GetType().Name}: {e.Message}");
        }
        finally { _enviando = false; }
    }

    // ── piezas ───────────────────────────────────────────────────────────────

    private void Estado(string texto) => _estado.Text = texto;

    /// <summary>El título de una vista: el nombre y, si hay algo que contar, una coletilla tenue.</summary>
    private static UIElement TituloDeVista(string texto, string coletilla)
    {
        var fila = new StackPanel
        {
            Orientation = Orientation.Horizontal, Margin = new Thickness(2, 2, 2, 16),
        };
        fila.Children.Add(new TextBlock
        {
            Text = texto, Foreground = Estudio.Tinta, FontSize = 21, FontWeight = FontWeights.SemiBold,
        });
        if (coletilla.Length > 0)
            fila.Children.Add(new TextBlock
            {
                Text = coletilla, Foreground = Estudio.TintaTenue, FontSize = 13,
                VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(9, 0, 0, 2),
            });
        return fila;
    }

    private static Button Volver(string aDonde, Action alPulsar)
    {
        var b = Enlace($"‹ {aDonde}");
        b.Margin = new Thickness(2, 2, 2, 14);
        b.Click += (_, __) => alPulsar();
        return b;
    }

    private static TextBlock Rotulo(string texto)
    {
        var r = Estudio.Rotulo(texto);
        r.Margin = new Thickness(2, 0, 2, 8);
        return r;
    }

    private static UIElement LineaDePaso(int n, string frase)
    {
        var fila = new DockPanel { Margin = new Thickness(0, 7, 0, 7) };
        var numero = new TextBlock
        {
            Text = n.ToString(), Foreground = Estudio.TintaTenue, FontSize = 11, MinWidth = 17,
            Margin = new Thickness(0, 1, 0, 0),
        };
        DockPanel.SetDock(numero, Dock.Left);
        fila.Children.Add(numero);
        fila.Children.Add(new TextBlock
        {
            Text = frase, Foreground = Estudio.Tinta, FontSize = 12.5, LineHeight = 18,
            TextWrapping = TextWrapping.Wrap,
        });
        return new Border
        {
            BorderBrush = Estudio.Borde, BorderThickness = new Thickness(0, 0, 0, 1), Child = fila,
        };
    }

    private static UIElement Ficha(string texto) => new Border
    {
        CornerRadius = new CornerRadius(9), Background = Estudio.SuperficieSuave,
        Padding = new Thickness(9, 4, 9, 5), Margin = new Thickness(0, 0, 6, 6),
        Child = new TextBlock { Text = texto, Foreground = Estudio.TintaMedia, FontSize = 11.5 },
    };

    private static Button FilaDeAccion(string texto)
    {
        var fila = new DockPanel();
        var chevron = new TextBlock
        {
            Text = "›", Foreground = Estudio.TintaTenue, FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(chevron, Dock.Right);
        fila.Children.Add(chevron);
        fila.Children.Add(new TextBlock
        {
            Text = texto, Foreground = Estudio.Tinta, FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var b = new Button
        {
            Content = fila, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand, Padding = new Thickness(2, 14, 2, 14),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Template = Estudio.Pastilla(12, estirado: true), Margin = new Thickness(2, 0, 2, 0),
        };
        return b;
    }

    private static Button BotonPrincipal(string texto, Brush? fondo = null) => new()
    {
        Content = new TextBlock { Text = texto, FontSize = 14.5, FontWeight = FontWeights.SemiBold },
        Height = 46, Margin = new Thickness(2, 0, 2, 0), Cursor = Cursors.Hand,
        Background = fondo ?? Estudio.Acento, Foreground = Brushes.White,
        BorderThickness = new Thickness(0), Template = Estudio.Pastilla(23, estirado: true),
    };

    private static Button Enlace(string texto, Brush? tinta = null) => new()
    {
        Content = new TextBlock { Text = texto, FontSize = 12.5 },
        Foreground = tinta ?? Estudio.Acento, Background = Brushes.Transparent,
        BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Padding = new Thickness(0, 6, 0, 6),
        HorizontalAlignment = HorizontalAlignment.Left, Template = Estudio.Pastilla(10),
    };

    private static UIElement TarjetaVacia(string titulo, string cuerpo)
    {
        var pila = new StackPanel();
        pila.Children.Add(new TextBlock
        {
            Text = titulo, Foreground = Estudio.Tinta, FontSize = 14, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
        });
        pila.Children.Add(new TextBlock
        {
            Text = cuerpo, Foreground = Estudio.TintaMedia, FontSize = 12.5, LineHeight = 19,
            TextWrapping = TextWrapping.Wrap,
        });
        var t = Estudio.Tarjeta(20);
        t.Padding = new Thickness(20, 22, 20, 22);
        t.Margin = new Thickness(2, 8, 2, 0);
        t.Child = pila;
        return Estudio.Elevar(t);
    }

    /// <summary>
    /// El cerebro, dibujado y no un glifo del sistema: «Segoe MDL2 Assets» no tiene ninguno que
    /// signifique esto, y un glifo que no existe se pinta como un cuadrado vacío.
    /// </summary>
    private static System.Windows.Shapes.Shape CerebroDibujado() => new System.Windows.Shapes.Path
    {
        Data = Geometry.Parse(
            "M9.5 3.5 A3 3 0 0 0 6.6 6 A2.8 2.8 0 0 0 4.8 10.6 A3 3 0 0 0 6 15.6 "
            + "A3 3 0 0 0 9.5 19.9 A2.5 2.5 0 0 0 12 17.7 L12 5.9 A2.5 2.5 0 0 0 9.5 3.5 Z "
            + "M14.5 3.5 A3 3 0 0 1 17.4 6 A2.8 2.8 0 0 1 19.2 10.6 A3 3 0 0 1 18 15.6 "
            + "A3 3 0 0 1 14.5 19.9 A2.5 2.5 0 0 1 12 17.7"),
        Stroke = Estudio.TintaMedia, StrokeThickness = 1.5, Fill = null,
        StrokeLineJoin = PenLineJoin.Round, Stretch = Stretch.Uniform, Width = 17, Height = 17,
    };

    /// <summary>Como <see cref="BotonDeMarco"/>, pero con un dibujo dentro en vez de un glifo.</summary>
    private static Button BotonDeIcono(System.Windows.Shapes.Shape dibujo, string queHace)
    {
        var b = new Button
        {
            Content = dibujo, Width = 30, Height = 30, Margin = new Thickness(2, 0, 0, 0),
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand, Template = Estudio.Pastilla(15),
        };
        AutomationProperties.SetName(b, queHace);
        b.MouseEnter += (_, __) => { if (b.Background == Brushes.Transparent) b.Background = Estudio.SuperficieSuave; };
        b.MouseLeave += (_, __) => { if (b.Background == Estudio.SuperficieSuave) b.Background = Brushes.Transparent; };
        return b;
    }

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
