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
public sealed partial class ConsultaWindow : Window
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
    /// <summary>«¿Por qué vino a cardiología?», arriba de la Nota, al soltar la historia clínica (spec 051).</summary>
    private readonly PanelDelMotivo _motivo = new();
    private readonly Button _grabar;
    /// <summary>El icono del botón: el micrófono en reposo, el punto rojo vivo mientras se graba.</summary>
    private readonly Border _puntoDeGrabar;
    private readonly TextBlock _etiquetaDeGrabar;
    /// <summary>El cronómetro, dentro del botón y en Geist Mono como el de la web (`.capture-time`).</summary>
    private readonly TextBlock _relojDeGrabar;
    /// <summary>La tarjeta de «Transcripción en vivo»: se ve solo mientras llega texto.</summary>
    private readonly Border _tarjetaVivo;
    private readonly TextBlock _estado;

    // ── el micrófono ─────────────────────────────────────────────────────────
    private readonly Button _microfono;
    private readonly Popup _menuMicrofono;
    /// <summary>El sitio del icono de la fuente: se cambia de icono y de color según quién entrega.</summary>
    private readonly Border _iconoMicrofono;

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

    // Los iconos de Lucide de cada fuente (spec 054): los mismos que dibuja la web.
    private const string GlifoMicrofono = "mic";
    private const string GlifoBluetooth = "bluetooth";
    private const string GlifoTelefono = "smartphone";

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

    /// <summary>
    /// CON LO QUE ABRE: las dimensiones del pantallazo del dueño (2026-09-17). Promesa 271 (spec 031).
    /// </summary>
    /// <remarks>
    /// HASTA EL 2026-09-17 ABRÍA A 470×660, una tarjeta vertical: servía para grabar una consulta y
    /// nada más. El dueño pidió que abriera como en su pantallazo, donde la ventana es el centro de
    /// operaciones con la carita sentada en medio: la tarjeta visible mide 1180×765 px al 125 %, o
    /// sea 944×612 puntos, y la ventana entera suma el hueco de la sombra (22+22 de ancho, 18+26 de
    /// alto; es el margen de <c>Estudio.Elevar</c> que juzga la promesa 154). El mínimo no sube:
    /// sigue pudiendo estirarse hasta lo pequeño de antes.
    /// </remarks>
    public static Size TamanoInicial => new(988, 656);

    public static Size TamanoMinimo => new(400, 540);
    // ── la plantilla se elige (spec 053) ─────────────────────────────────────

    /// <summary>El caret del botón partido: abre el selector sin arrancar nada.</summary>
    private readonly Button _caretPlantilla;
    private readonly Popup _menuPlantilla;

    /// <summary>Con qué se va a grabar, debajo del botón. Lo que el portal enseña en el panel.</summary>
    private readonly TextBlock _rotuloPlantilla;

    /// <summary>Lo que hay para elegir. Se pide una vez al arrancar.</summary>
    private IReadOnlyList<PlantillaClinica> _catalogo = Array.Empty<PlantillaClinica>();

    /// <summary>«Tu sugerida»: el pin de este médico, leído de la misma tabla que el portal.</summary>
    private string _sugeridaId = "";

    /// <summary>Lo que tocó en el selector para ESTA consulta. Manda sobre todo lo demás.</summary>
    private string _elegidaId = "";

    /// <summary>Su especialidad, para saber en qué fila vive su pin. Vacía si no la tiene puesta.</summary>
    private string _especialidad = "";

    // ── una consulta anterior, abierta desde la lista (spec 053) ─────────────

    /// <summary>El encounter que se está mirando, o vacío si lo que se ve es la nota en curso.</summary>
    private string _abiertaId = "";
    private string _abiertaEstado = "";
    private NotaClinica? _abiertaNota;

    /// <summary>
    /// La nota que se está VIENDO: la de la consulta en curso, o la de una abierta desde la lista.
    /// </summary>
    /// <remarks>
    /// Es lo que lee el ✓ para saber qué mandar a SAP. Antes se leía <c>_consulta.Nota</c> directo,
    /// que era correcto cuando la única nota posible era la de ahora; con consultas anteriores
    /// abiertas, mandaría la nota de un paciente estando mirando la de otro.
    /// </remarks>
    private NotaClinica? _notaEnPantalla;

    public ConsultaWindow(SesionMiracle sesion, GraphConfig graphConfig)
    {
        _sesion = sesion;
        _clinica = new ClinicaClient(graphConfig.BaseUrl, sesion);
        _audio = new LiveAudio();
        _dictado = new DictadoEnVivo(graphConfig, _audio, sesion);
        _consulta = new Consulta(sesion, _clinica,
            abrirMicrofono: _dictado.ArrancarAsync,
            pararYRecogerLoDicho: () => _dictado.PararAsync(),
            // CORREGIR NO ES DAR DE ALTA, y por eso la fila no es la misma (promesa 444): el alta
            // lleva estado y firma, la corrección solo lo que cambió.
            espejar: async (encounterId, nota, verbatim, yaExiste) =>
            {
                string fila = yaExiste
                    ? EspejoDeConsulta.FilaDeCorreccion(encounterId, nota)
                    : EspejoDeConsulta.Fila(encounterId, nota, verbatim,
                        _plantillaNombre, PlantillaAbierta.Especialidad, DateTimeOffset.UtcNow);
                return await EspejoDeConsulta.EscribirAsync(_sesion, _http, fila);
            });

        // ── el marco ─────────────────────────────────────────────────────────
        Title = "Miracle";
        // LA LETRA DE MIRACLE (spec 054): Inter para toda la ventana. Los títulos, la nota y el
        // reloj piden la suya en su sitio.
        FontFamily = Estudio.FuenteCuerpo;
        Width = TamanoInicial.Width;
        Height = TamanoInicial.Height;
        MinWidth = TamanoMinimo.Width;
        MinHeight = TamanoMinimo.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        this.PonerLaBarraDeScroll();
        DejarseAgarrarPorElBorde();

        var tarjeta = new Border
        {
            // 28 Y NO 46 DESDE EL 2026-09-26 (spec 054): el 46 del boceto era una pastilla, y una
            // ventana de 988×656 con la nota dentro ya no lo es — se leía como un globo. 28 sigue
            // siendo más redondo que cualquier ventana de Windows (el marco de U), y queda en la
            // escala de la web (radius-xl 30).
            CornerRadius = new CornerRadius(28),
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
        var minimizar = BotonDeMarco("minus", "Minimizar");
        minimizar.Click += (_, __) => WindowState = WindowState.Minimized;
        var cerrar = BotonDeMarco("x", "Cerrar");
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
        var chevron = Estudio.Icono("chevron-down", 13, Estudio.TintaTenue);
        chevron.Margin = new Thickness(6, 1, 0, 0);
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
        // UN ICONO DE LUCIDE y no un emoji ni un glifo de MDL2: un emoji lo pinta la fuente del
        // sistema con su propio color y su propio peso, y MDL2 dibuja otra familia de iconos que la
        // web (spec 054). PintarMicrofono cambia el icono y el color.
        _iconoMicrofono = new Border
        {
            Child = Estudio.Icono(GlifoMicrofono, 15, Estudio.TintaMedia),
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

        // LOS MENÚS NO HEREDAN LA LETRA DE LA VENTANA: un Popup no cuelga del árbol visual, así que
        // sin esto se pintarían en Segoe UI. Se la pone cada uno, y sus hijos la heredan de él.
        foreach (var menu in new[] { _menuCuenta, _menuMicrofono })
            menu.SetValue(System.Windows.Documents.TextElement.FontFamilyProperty, Estudio.FuenteCuerpo);

        // EL ORBE Y «MIRACLE», como la barra de la web (`Logo`): lo primero que dice que esta es la
        // misma app que el portal. La palabra se esconde cuando la ventana se estrecha — el nombre
        // del médico no puede quedarse sin sitio por un adorno.
        var marca = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        marca.Children.Add(Estudio.Orbe(26));
        var palabra = new TextBlock
        {
            Text = "Miracle",
            FontFamily = Estudio.FuenteCuerpo,
            FontWeight = FontWeights.ExtraLight,
            FontSize = 15.5,
            Foreground = Estudio.TintaFuerte,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 1),
        };
        // El tracking de .22em de la palabra de la web: WPF no tiene espaciado, van espacios finos.
        palabra.Text = string.Join('\u2009', "Miracle".ToCharArray());
        marca.Children.Add(palabra);
        SizeChanged += (_, __) => palabra.Visibility = ActualWidth < 620 ? Visibility.Collapsed : Visibility.Visible;
        var separador = new Border { Width = 1, Height = 18, Background = Estudio.Borde, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };

        var izquierdaDeLaCabecera = new StackPanel { Orientation = Orientation.Horizontal };
        izquierdaDeLaCabecera.Children.Add(marca);
        izquierdaDeLaCabecera.Children.Add(separador);
        izquierdaDeLaCabecera.Children.Add(_quienBoton);
        izquierdaDeLaCabecera.Children.Add(_microfono);

        // EL CEREBRO, Y NO UN TERCER SEGMENTO EN EL CARRIL (spec 016). El carril contesta «qué
        // estoy mirando DE ESTE PACIENTE» —sus consultas, su nota—; los aprendizajes no son del
        // paciente, son del asistente. Meterlos ahí es un error de categoría, y se nota como ruido
        // aunque cada pieza esté bien dibujada.
        _aprendizajes = BotonDeIcono(Estudio.Icono("brain", 17, Estudio.TintaMedia), "Aprendizajes");
        _aprendizajes.Click += (_, __) => { if (_enAprendizajes) Mostrar(nota: _enNota); else AbrirAprendizajes(); };
        izquierdaDeLaCabecera.Children.Add(_aprendizajes);

        cabecera.Children.Add(botonera);
        cabecera.Children.Add(izquierdaDeLaCabecera);
        Grid.SetRow(cabecera, 0);
        raiz.Children.Add(cabecera);

        // ── el segmentado ────────────────────────────────────────────────────
        // El `.seg` de la web: carril hundido con su filete y la pestaña activa como un botón blanco
        // que sobresale. El relieve lo pone la sombra de U.
        var carril = new Border
        {
            CornerRadius = new CornerRadius(21),
            Background = Estudio.SuperficieSuave,
            BorderBrush = Estudio.Borde,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(3),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 18),
        };
        var segmentos = new StackPanel { Orientation = Orientation.Horizontal };
        _tabConsultas = Pestana("Consultas");
        _tabNota = Pestana("Nota");
        _tabConsultas.Click += async (_, __) => { Mostrar(nota: false); await CargarConsultasAsync(); };
        // VOLVER A LA PESTAÑA NOTA CIERRA LA CONSULTA ABIERTA. Sin esto, mirar una consulta vieja
        // y volver dejaría su texto en pantalla como si fuera la de ahora — y el ✓ mandaría a SAP
        // la que no es.
        _tabNota.Click += (_, __) => { CerrarLaAbierta(); Mostrar(nota: true); };
        segmentos.Children.Add(_tabConsultas);
        segmentos.Children.Add(_tabNota);
        carril.Child = segmentos;
        Grid.SetRow(carril, 1);
        raiz.Children.Add(carril);

        // ── la superficie ────────────────────────────────────────────────────
        _listaConsultas = new StackPanel { Visibility = Visibility.Collapsed };

        _vivo = new TextBlock
        {
            Foreground = Estudio.TintaSuave,
            FontSize = 15,
            LineHeight = 25,
            TextWrapping = TextWrapping.Wrap,
        };
        // «TRANSCRIPCIÓN EN VIVO» con su punto rojo, como la tarjeta de la web. Solo existe mientras
        // llega texto: vacía no dice nada que el botón no diga ya.
        var cabeceraVivo = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        cabeceraVivo.Children.Add(PuntoVivo());
        var rotuloVivo = Estudio.Rotulo("Transcripción en vivo");
        rotuloVivo.Margin = new Thickness(8, 0, 0, 0);
        rotuloVivo.VerticalAlignment = VerticalAlignment.Center;
        cabeceraVivo.Children.Add(rotuloVivo);
        var pilaVivo = new StackPanel();
        pilaVivo.Children.Add(cabeceraVivo);
        pilaVivo.Children.Add(_vivo);
        _tarjetaVivo = Estudio.Tarjeta(Estudio.RadioMedio);
        _tarjetaVivo.Padding = new Thickness(18, 14, 18, 16);
        _tarjetaVivo.Margin = new Thickness(2, 0, 2, 10);
        _tarjetaVivo.Child = pilaVivo;
        _tarjetaVivo.Visibility = Visibility.Collapsed;
        _nota = new StackPanel();
        // VACÍO NO ES FALLO, y una pantalla en blanco no lo distingue: se dice qué va a pasar aquí.
        _vacioNota = Estudio.Tarjeta(Estudio.RadioMedio);
        _vacioNota.Padding = new Thickness(22, 22, 22, 22);
        _vacioNota.Margin = new Thickness(2, 8, 2, 0);
        var pilaVacio = new StackPanel();
        // El `EmptyState` de la web: una baldosa hielo con el icono en azul, y el título debajo.
        pilaVacio.Children.Add(new Border
        {
            Width = 44, Height = 44, CornerRadius = new CornerRadius(14), Background = Estudio.Hielo,
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 14),
            Child = Estudio.Icono("mic", 20, Estudio.Acento),
        });
        pilaVacio.Children.Add(new TextBlock
        {
            Text = "Pulsa grabar y habla con normalidad.",
            Foreground = Estudio.TintaFuerte, FontFamily = Estudio.FuenteTitulo, FontSize = 16.5,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
        });
        pilaVacio.Children.Add(new TextBlock
        {
            Text = "Verás aquí lo que se va oyendo. Al parar, la nota queda organizada y guardada "
                 + "en tu cuenta — la misma que ves en el portal.",
            Foreground = Estudio.TintaMedia, FontSize = 12.5, LineHeight = 19,
            TextWrapping = TextWrapping.Wrap,
        });
        // Sin esta línea soltar documentos no se descubre: la ventana no tiene ningún botón que lo sugiera.
        pilaVacio.Children.Add(new TextBlock
        {
            Text = "Suelta aquí la historia clínica —fotos o PDF— y te digo por qué vino a cardiología.",
            Foreground = Estudio.TintaMedia, FontSize = 12.5, LineHeight = 19,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
        });
        _vacioNota.Child = pilaVacio;

        _panelNota = new StackPanel();
        // ARRIBA DE TODO: es lo primero que el médico tiene que leer (spec 051).
        _panelNota.Children.Add(_motivo.Vista);
        _panelNota.Children.Add(Estudio.Elevar(_tarjetaVivo));
        _panelNota.Children.Add(Estudio.Elevar(_vacioNota));
        _panelNota.Children.Add(_nota);
        _panelNota.Children.Add(HuecoDeLaCarita());   // promesa 272: aquí se sienta la carita

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
        // EL GRANDE DE U CON EL AZUL DE MIRACLE (spec 054). El tamaño y el relieve son la base de U
        // —es LO que se toca en esta ventana—; el color es el del botón de grabar de la web: la
        // píldora azul con el micrófono blanco. Grabando, pasa al secundario con filete rojo, el
        // punto vivo y el reloj en Geist Mono, como el panel de dictado de la web: el objeto es el
        // mismo, su estado no.
        var dentroDelBoton = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _puntoDeGrabar = new Border
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
            Child = Estudio.Icono("mic", 20, Brushes.White),
        };
        _etiquetaDeGrabar = new TextBlock
        {
            Text = "Grabar",
            Foreground = Brushes.White,
            FontSize = 17.5,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _relojDeGrabar = new TextBlock
        {
            Foreground = Estudio.TintaMedia,
            FontFamily = Estudio.FuenteMono,
            FontSize = 16,
            FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        dentroDelBoton.Children.Add(_puntoDeGrabar);
        dentroDelBoton.Children.Add(_etiquetaDeGrabar);
        dentroDelBoton.Children.Add(_relojDeGrabar);

        _grabar = new Button
        {
            Content = dentroDelBoton,
            MinWidth = 178,
            Height = 64,
            Padding = new Thickness(28, 0, 28, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = Estudio.AcentoDegradado,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Template = Estudio.Pastilla(32),
        };
        _grabar.ConRelieve(Estudio.Sombra2);
        _grabar.MouseEnter += (_, __) =>
        {
            if (_grabar.IsEnabled && _consulta.Estado != EstadoDeConsulta.Grabando) _grabar.Background = Estudio.AcentoDegradadoEncima;
        };
        _grabar.MouseLeave += (_, __) => PintarElBotonDeGrabar(_consulta.Estado == EstadoDeConsulta.Grabando);
        _grabar.Click += async (_, __) => await AlternarAsync();
        _grabar.HorizontalAlignment = HorizontalAlignment.Center;

        // ── la plantilla: la LÍNEA que la dice ES el botón que la cambia ────
        //
        // EMPEZÓ SIENDO UN BOTÓN PARTIDO como el del portal (`ActionDock.tsx`) y se cambió el
        // 2026-09-07, dicho por el dueño mirándolo: «se ve horrible ese botón para abrir las
        // plantillas que ni se entiende». Y tenía razón por debajo del gusto: una pastilla vacía
        // pegada al botón de grabar no dice de qué es. La línea, en cambio, YA dice el nombre de la
        // plantilla — así que hacerla pulsable no añade ningún objeto a la pantalla y lo que se
        // toca es exactamente lo que se quiere cambiar.
        _rotuloPlantilla = new TextBlock
        {
            Foreground = Estudio.TintaMedia,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 320,
        };
        var chevronPlantilla = Estudio.Icono("chevron-down", 13, Estudio.TintaTenue);
        chevronPlantilla.Margin = new Thickness(6, 1, 0, 0);
        var iconoPlantilla = Estudio.Icono("layout-template", 14, Estudio.TintaTenue);
        iconoPlantilla.Margin = new Thickness(0, 0, 7, 0);
        var dentroDeLaPlantilla = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        dentroDeLaPlantilla.Children.Add(iconoPlantilla);
        dentroDeLaPlantilla.Children.Add(_rotuloPlantilla);
        dentroDeLaPlantilla.Children.Add(chevronPlantilla);

        _caretPlantilla = new Button
        {
            Content = dentroDeLaPlantilla,
            Padding = new Thickness(14, 7, 12, 7),
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Template = Estudio.Pastilla(16),
        };
        _caretPlantilla.MouseEnter += (_, __) => _caretPlantilla.Background = Estudio.SuperficieSuave;
        _caretPlantilla.MouseLeave += (_, __) => _caretPlantilla.Background = Brushes.Transparent;
        _caretPlantilla.Click += (_, __) => { PintarMenuDePlantilla(); _menuPlantilla.IsOpen = !_menuPlantilla.IsOpen; };

        _menuPlantilla = new Popup
        {
            PlacementTarget = _caretPlantilla,
            Placement = PlacementMode.Top,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
        };
        _menuPlantilla.SetValue(System.Windows.Documents.TextElement.FontFamilyProperty, Estudio.FuenteCuerpo);

        var abajo = new StackPanel();
        abajo.Children.Add(_grabar);
        abajo.Children.Add(_caretPlantilla);
        Grid.SetRow(abajo, 4);
        raiz.Children.Add(abajo);

        tarjeta.Child = raiz;
        // La sombra la echa una PLACA detrás, nunca el borde que lleva el contenido: dentro de un
        // Effect el texto pierde ClearType y toda la ventana se ve lavada (2026-09-01).
        var marco = Estudio.Elevar(tarjeta, Estudio.Sombra3);
        marco.Margin = new Thickness(22, 18, 22, 26);
        Content = marco;
        this.Nitida();

        // Sin barra de título, arrastrar es cosa nuestra. Los botones se tragan su propio clic.
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Escape) WindowState = WindowState.Minimized;
            // CTRL+S ACEPTA EL AJUSTE PROPUESTO (spec 055): el mismo atajo que guarda una sección, para
            // que aprobar lo que propuso la IA no pida buscar el botón.
            else if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) != 0 && _propuesta != null)
            {
                e.Handled = true;
                await GuardarPropuestaAsync();
            }
        };
        // NO HAY MAXIMIZAR, y no basta con no dibujar el botón: Win+↑ maximiza igual y la pastilla
        // se convertiría en una pantalla completa con las esquinas flotando.
        StateChanged += (_, __) => { if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal; };

        _cronometro.Tick += (_, __) => PintarCronometro();
        _dictado.Parcial += t => Dispatcher.BeginInvoke(() =>
        {
            // DICTAR UN CAMBIO NO ES GRABAR UNA CONSULTA: el mismo dictado sirve a los dos, pero lo que
            // se dice al micrófono de una sección no es transcripción de la consulta y no se pinta
            // como tal. Lo oído se enseña en la barra de estado para saber que está escuchando.
            if (_dictandoA != null) { Estado("Te escucho: «" + (t.Length > 90 ? "…" + t[^90..] : t) + "»"); return; }
            _vivo.Text = t;
            _tarjetaVivo.Visibility = t.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
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

        // SOLTAR LA HISTORIA CLÍNICA (spec 051): en la vista de Nota, grabando o no. En Consultas y en
        // Aprendizajes no se admite: allí no hay paciente al que pertenezca lo soltado.
        AllowDrop = true;
        bool AdmiteSoltar() => _enNota && !_enAprendizajes;
        DragEnter += (_, e) => { if (AdmiteSoltar()) _motivo.AlPasarPorEncima(e); else { e.Effects = DragDropEffects.None; e.Handled = true; } };
        DragOver += (_, e) => { if (AdmiteSoltar()) _motivo.AlPasarPorEncima(e); else { e.Effects = DragDropEffects.None; e.Handled = true; } };
        DragLeave += (_, __) => _motivo.AlSalir();
        Drop += async (_, e) =>
        {
            if (!AdmiteSoltar()) return;
            e.Handled = true;
            _superficie.ScrollToTop();
            await _motivo.SoltarAsync(e.Data);
        };

        Mostrar(nota: true);
        Loaded += async (_, __) => await ArrancarAsync();
        Closed += (_, __) => { _cronometro.Stop(); _pulso.Stop(); _dictado.Dispose(); _audio.Dispose(); _http.Dispose(); _motivo.Dispose(); };
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
        // POR QUÉ NO CONECTA, bajo su fila (promesa 412): sin collar enlazado todavía, la tarjeta de
        // abajo no existe y el motivo —«el Bluetooth está apagado»— no salía en ninguna parte (2026-09-24).
        string? porQue = Voice.ElBluetooth.LineaDelMenu(
            _selector.Preferida == Origen.CollarPorBluetooth, CollarPermanente.Conectado,
            CollarPermanente.Enlazado, CollarPermanente.Estado);
        if (porQue != null)
            lista.Children.Add(new TextBlock
            {
                Text = porQue,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = Estudio.TintaMedia,
                Margin = new Thickness(44, 0, 12, 8),
            });
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

        var icono = Estudio.Icono(glifo, 16, activa ? Estudio.Ok : manda ? Estudio.Acento : Estudio.TintaMedia);
        icono.HorizontalAlignment = HorizontalAlignment.Center;   // centrado EN SU COLUMNA
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

        string icono = origen switch
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
        var color =
            entregando ? Estudio.Ok
            : grabando ? Estudio.Alerta
            : origen != Origen.MicrofonoDelPc && !enlaceEnPie ? Estudio.Espera
            : Estudio.TintaMedia;
        // Se repinta cada segundo: el icono solo se rehace si cambió de fuente, el color siempre.
        if ((_iconoMicrofono.Tag as string) != icono)
        {
            _iconoMicrofono.Child = Estudio.Icono(icono, 15, color);
            _iconoMicrofono.Tag = icono;
        }
        else if (_iconoMicrofono.Child is FrameworkElement dibujo) Estudio.Colorear(dibujo, color);
    }

    // ── arranque ─────────────────────────────────────────────────────────────

    private async Task ArrancarAsync()
    {
        _quien.Text = _sesion.MedicoNombre.Length > 0 ? _sesion.MedicoNombre : _sesion.MedicoEmail;
        Estado("Preparando…");
        // Las preferencias ANTES que la plantilla: el modo y los pines deciden con cuál se graba.
        await CargarLoDelMedicoAsync();
        await ResolverPlantillaAsync();
    }

    /// <summary>
    /// Deja lista la plantilla abierta: la busca en el catálogo y, si no existe, la crea. Se llama
    /// al arrancar y otra vez al pulsar grabar si la primera no cuajó.
    /// </summary>

    /// <summary>Que plantilla manda ahora mismo, debajo del boton.</summary>
    private void PintarPlantilla() =>
        _rotuloPlantilla.Text = _plantillaNombre.Length > 0
            ? (_sugeridaId.Length > 0 && _plantillaId == _sugeridaId ? "★ " : "") + _plantillaNombre
            : "";

    /// <summary>
    /// EL SELECTOR DE PLANTILLA, detras del caret. Se repinta al abrirlo para marcar cual manda.
    /// </summary>
    /// <remarks>
    /// CON BUSCADOR, y no es un adorno: el catalogo real tiene 204 plantillas (medido en el log el
    /// 2026-09-07). Una lista de 204 sin buscar no es un selector, es un obstaculo -que es justo lo
    /// que hizo que en septiembre se decidiera no preguntar nunca (promesa 94)-. El buscador es lo
    /// que permite ofrecer la eleccion sin devolver el obstaculo.
    ///
    /// LAS SUYAS PRIMERO, mismo criterio que `splitTemplatesBySpecialty` del portal: lo que el
    /// medico creo va arriba porque es lo que reconoce.
    ///
    /// LA ESTRELLA FIJA «TU SUGERIDA» y escribe en `user_template_preferences`, la MISMA tabla que
    /// lee el navegador: fijarla aqui se ve alli, y al reves. Es la promesa 441.
    /// </remarks>
    private void PintarMenuDePlantilla(string filtro = "")
    {
        var pila = new StackPanel { Width = 352 };

        var buscador = new TextBox
        {
            Text = filtro,
            FontSize = 13,
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 0, 0, 10),
            Background = Estudio.SuperficieSuave,
            Foreground = Estudio.Tinta,
            BorderThickness = new Thickness(0),
        };
        buscador.TextChanged += (_, __) => PintarMenuDePlantilla(buscador.Text);
        pila.Children.Add(buscador);

        var lista = new StackPanel();
        var scroll = new ScrollViewer
        {
            Content = lista,
            MaxHeight = 300,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        scroll.PonerLaBarraDeScroll();

        string q = Navigation.Nombres.Aplanar(filtro ?? "").Trim();
        bool Casa(PlantillaClinica p) => q.Length == 0
            || Navigation.Nombres.Aplanar(p.Nombre + " " + p.Especialidad).Contains(q, StringComparison.Ordinal);

        var mias = _catalogo.Where(x => x.EsMia && Casa(x)).ToList();
        var delHospital = _catalogo.Where(x => !x.EsMia && Casa(x)).ToList();

        if (mias.Count > 0)
        {
            var r = Estudio.Rotulo("Mias");
            r.Margin = new Thickness(4, 2, 0, 6);
            lista.Children.Add(r);
            foreach (var x in mias) lista.Children.Add(FilaDePlantilla(x));
        }
        if (delHospital.Count > 0)
        {
            var r = Estudio.Rotulo("Del hospital");
            r.Margin = new Thickness(4, mias.Count > 0 ? 12 : 2, 0, 6);
            lista.Children.Add(r);
            // SE CORTA LA LISTA LARGA y se DICE cuantas quedaron fuera: con 204 plantillas, pintarlas
            // todas cuesta y no sirve. Callar el corte seria peor que cortarlo -quien busca la suya
            // creeria que no esta.
            foreach (var x in delHospital.Take(40)) lista.Children.Add(FilaDePlantilla(x));
            if (delHospital.Count > 40)
                lista.Children.Add(new TextBlock
                {
                    Text = $"y {delHospital.Count - 40} mas: escribe arriba para encontrarla",
                    Foreground = Estudio.TintaTenue, FontSize = 11.5,
                    Margin = new Thickness(6, 8, 6, 2), TextWrapping = TextWrapping.Wrap,
                });
        }
        if (mias.Count == 0 && delHospital.Count == 0)
            lista.Children.Add(new TextBlock
            {
                Text = _catalogo.Count == 0
                    ? "Todavia no se pudo leer el catalogo de plantillas."
                    : "Ninguna plantilla se llama asi.",
                Foreground = Estudio.TintaMedia, FontSize = 12.5,
                Margin = new Thickness(6, 6, 6, 6), TextWrapping = TextWrapping.Wrap,
            });

        pila.Children.Add(scroll);

        var tarjeta = Estudio.Tarjeta(20);
        tarjeta.Padding = new Thickness(14, 14, 14, 14);
        tarjeta.Child = pila;
        var elevado = Estudio.Elevar(tarjeta, Estudio.Sombra3);
        elevado.Margin = new Thickness(0, 0, 0, 8);
        _menuPlantilla.Child = elevado;

        // El foco al buscador SOLO al abrir de cero: al repintar por cada tecla, robarlo otra vez
        // dejaria el cursor al principio en mitad de la palabra.
        if ((filtro ?? "").Length == 0)
            Dispatcher.BeginInvoke(new Action(() => buscador.Focus()),
                System.Windows.Threading.DispatcherPriority.Input);
    }

    private UIElement FilaDePlantilla(PlantillaClinica p)
    {
        bool manda = p.Id == _plantillaId;
        bool esLaSugerida = _sugeridaId.Length > 0 && p.Id == _sugeridaId;

        var texto = new StackPanel();
        texto.Children.Add(new TextBlock
        {
            Text = p.Nombre,
            Foreground = Estudio.Tinta,
            FontSize = 13,
            FontWeight = manda ? FontWeights.SemiBold : FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        string bajo = p.Especialidad.Replace('_', ' ');
        if (esLaSugerida) bajo += "  ·  tu predeterminada";
        else if (p.EsLaPorDefecto) bajo += "  ·  la del hospital";
        texto.Children.Add(new TextBlock
        {
            Text = bajo,
            Foreground = Estudio.TintaTenue,
            FontSize = 10.5,
            Margin = new Thickness(0, 2, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var estrella = new Button
        {
            // Llena si es la suya, hueca si no: el estado se lee sin leer.
            Content = Estudio.Icono("star", 15, esLaSugerida ? Estudio.Espera : Estudio.TintaTenue,
                                    relleno: esLaSugerida ? Estudio.Espera : null),
            Width = 30,
            Height = 30,
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Template = Estudio.Pastilla(15),
        };
        estrella.Click += async (_, e) =>
        {
            // FIJARLA Y ELEGIRLA SON DOS COSAS: sin esto, el clic subiria a la fila y ademas la
            // seleccionaria, que no es lo que pidio quien toco la estrella.
            e.Handled = true;
            // FIJARLA ES PEDIR QUE MANDE (promesa 459): escribe el pin y deja el modo en «fixed», en
            // las mismas tablas que la web — fijarla aquí cambia con qué empieza la web, y al revés.
            string especialidad = _especialidad.Length > 0 ? _especialidad : p.Especialidad;
            if (await PreferenciasDelMedico.FijarPredeterminadaAsync(_sesion, especialidad, p.Id))
            {
                _sugeridaId = p.Id;
                _modoDePlantilla = "fixed";
                _pines = new[] { new Predeterminada(PlantillaPredeterminada.NormalizarEspecialidad(especialidad), p.Id,
                                     DateTimeOffset.UtcNow.ToString("O")) }.Concat(_pines).ToList();
                PintarPlantilla();
                PintarMenuDePlantilla();
                Estado($"«{p.Nombre}» es tu predeterminada, aquí y en la web.");
            }
            else Estado("No se pudo fijar tu predeterminada. Se sigue pudiendo elegir a mano.");
        };

        var dentro = new DockPanel();
        DockPanel.SetDock(estrella, Dock.Right);
        dentro.Children.Add(estrella);
        dentro.Children.Add(texto);

        var fila = new Button
        {
            Content = dentro,
            Padding = new Thickness(10, 8, 6, 8),
            Margin = new Thickness(0, 0, 0, 2),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = manda ? Estudio.AcentoSuave : Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Template = Estudio.Pastilla(12, estirado: true),
        };
        if (!manda)
        {
            fila.MouseEnter += (_, __) => fila.Background = Estudio.SuperficieSuave;
            fila.MouseLeave += (_, __) => fila.Background = Brushes.Transparent;
        }
        fila.Click += (_, __) =>
        {
            // LO ELEGIDO SE QUEDA ELEGIDO para las siguientes consultas de esta sesion: quien cambio
            // de plantilla casi nunca quiere volver a la anterior en la consulta siguiente.
            _elegidaId = p.Id;
            _plantillaId = p.Id;
            _plantillaNombre = p.Nombre;
            PintarPlantilla();
            _menuPlantilla.IsOpen = false;
            Estado($"Se grabara con «{p.Nombre}».");
            LogBus.Log("plantilla", $"el medico eligio «{p.Nombre}»");
        };
        return fila;
    }

    private async Task ResolverPlantillaAsync()
    {
        try
        {
            // La especialidad decide en que fila vive su pin, asi que se pide antes que el pin. Un
            // medico sin especialidad puesta no es un error: se queda sin pin y la cadena sigue.
            if (_especialidad.Length == 0) _especialidad = await _sesion.EspecialidadAsync();

            _catalogo = await _clinica.PlantillasAsync();

            // LA MISMA CADENA QUE LA WEB (promesa 458): lo tocado en esta consulta, la predeterminada
            // que fijó el médico, la última usada o la de su especialidad, según su modo.
            var decision = PlantillaPredeterminada.ParaGrabar(_catalogo, _pines,
                PreferenciasDelMedico.UltimaUsada(_sesion.MedicoId), _especialidad, _modoDePlantilla, _elegidaId);
            _sugeridaId = PlantillaPredeterminada.Elegir(_catalogo, _pines, null, null, "fixed") is { Length: > 0 } fijada
                          && _pines.Any(x => x.PlantillaId == fijada) ? fijada : "";

            if (decision.HayQuePreguntar)
            {
                // «LA ELIJO CADA VEZ»: no se elige por el médico. El botón de grabar abre el selector.
                _plantillaId = "";
                _plantillaNombre = "";
                _rotuloPlantilla.Text = "Elige la plantilla";
                LogBus.Log("plantilla", $"modo «manual» · {_catalogo.Count} disponible(s) · se pregunta al grabar");
                Estado("Elige con qué plantilla grabar.");
                return;
            }

            var elegida = decision.Plantilla;

            // NINGUN ESLABON RESOLVIO: se CREA la abierta, que es lo que la promesa 94 exige desde
            // el 2026-09-01. Caer en una cualquiera del catalogo -hoy son 204- seria elegir por el
            // medico sin decirselo, y su nota saldria con la forma de otra cosa.
            if (elegida == null)
            {
                elegida = await _clinica.CrearPlantillaAsync(
                    PlantillaAbierta.Nombre, PlantillaAbierta.Especialidad, PlantillaAbierta.Secciones());
                _catalogo = new List<PlantillaClinica>(_catalogo) { elegida };
            }

            _plantillaId = elegida.Id;
            _plantillaNombre = elegida.Nombre;
            PintarPlantilla();
            LogBus.Log("plantilla", $"se grabara con «{_plantillaNombre}» · {_catalogo.Count} disponible(s) · modo «{_modoDePlantilla}» · "
                                  + (_elegidaId.Length > 0 && _elegidaId == _plantillaId ? "la elegiste tu"
                                     : _sugeridaId.Length > 0 && _sugeridaId == _plantillaId ? "es tu predeterminada"
                                     : elegida.Nombre == PlantillaAbierta.Nombre ? "es la abierta"
                                     : "la eligio la cadena de la web"));
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
            Estudio.Colorear(dibujo, _enAprendizajes ? Estudio.Acento : Estudio.TintaMedia);
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
            b.BorderBrush = activa ? Estudio.Borde : Brushes.Transparent;
            b.Foreground = activa ? Estudio.TintaFuerte : Estudio.TintaSuave;
            b.Tag = activa ? Estudio.Sombra1 : null;
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
            if (_dictandoA != null) { Estado("Termina de dictar el cambio antes de grabar."); return; }

            if (_plantillaId.Length == 0)
            {
                Estado("Reintentando la conexión…");
                await ResolverPlantillaAsync();
                if (_plantillaId.Length == 0 && _modoDePlantilla == "manual" && _catalogo.Count > 0)
                {
                    PintarMenuDePlantilla();
                    _menuPlantilla.IsOpen = true;
                    Estado("Elige con qué plantilla grabar y vuelve a pulsar grabar.");
                    return;
                }
                if (_plantillaId.Length == 0) return;   // ResolverPlantilla ya dijo por qué
            }

            // EMPEZAR A GRABAR CIERRA LA QUE SE ESTABA MIRANDO. Si no, la consulta vieja seguiría
            // en pantalla mientras se graba otra, y el ✓ mandaría a SAP la que no es.
            _abiertaId = "";
            _abiertaEstado = "";
            _abiertaNota = null;
            _notaEnPantalla = null;
            // Lo de la nota anterior —su propuesta, su paciente, su plantilla congelada— no es de esta.
            _propuesta = null;
            _paciente = null;
            _contextoDe = "";
            _contextoPlantilla = default;
            _contextoTranscripcion = "";

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
            // LA ÚLTIMA USADA SE ANOTA AL GRABAR, no al mirar el selector: es lo que hace la web.
            else PreferenciasDelMedico.RecordarUltima(_sesion.MedicoId, _plantillaId);
        }
        finally { _grabar.IsEnabled = true; }
    }

    private void PintarSegunEstado()
    {
        bool grabando = _consulta.Estado == EstadoDeConsulta.Grabando;
        PintarElBotonDeGrabar(grabando);
        if (!grabando) _tarjetaVivo.Visibility = Visibility.Collapsed;

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
        _relojDeGrabar.Text = va.ToString(va.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss");
        Estado("Escuchando…");
    }

    /// <summary>
    /// El botón de grabar en sus dos estados, en UN sitio: el reposo es el primario azul de la web
    /// con el micrófono blanco; grabando es el secundario con filete rojo, el punto vivo, «Parar» y
    /// el reloj. Lo llaman el estado de la consulta y el ratón al salir, y los dos tienen que decir lo
    /// mismo — dos sitios que pintan el mismo botón acaban discrepando (aprendizaje nº16).
    /// </summary>
    private void PintarElBotonDeGrabar(bool grabando)
    {
        if (grabando)
        {
            _grabar.Background = Estudio.Superficie;
            _grabar.BorderBrush = Estudio.Alerta;
            _puntoDeGrabar.Child = PuntoVivo(12);
            _etiquetaDeGrabar.Text = "Parar";
            _etiquetaDeGrabar.Foreground = Estudio.AlertaTinta;
            _relojDeGrabar.Visibility = Visibility.Visible;
        }
        else
        {
            _grabar.Background = Estudio.AcentoDegradado;
            _grabar.BorderBrush = Brushes.Transparent;
            _puntoDeGrabar.Child = Estudio.Icono("mic", 20, Brushes.White);
            _etiquetaDeGrabar.Text = "Grabar";
            _etiquetaDeGrabar.Foreground = Brushes.White;
            _relojDeGrabar.Visibility = Visibility.Collapsed;
            _relojDeGrabar.Text = "";
        }
    }

    /// <summary>
    /// El punto rojo que late mientras se graba (`.live-dot` de la web): un halo que se abre y se
    /// desvanece cada 2,1 s. Es la única animación de la nota y dice lo único que importa ahí: te
    /// estoy oyendo.
    /// </summary>
    private static FrameworkElement PuntoVivo(double lado = 10)
    {
        var caja = new Grid { Width = lado * 2.4, Height = lado * 2.4, VerticalAlignment = VerticalAlignment.Center };
        var halo = new System.Windows.Shapes.Ellipse
        {
            Width = lado, Height = lado, Fill = Estudio.Alerta, Opacity = 0.55,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1),
        };
        var punto = new System.Windows.Shapes.Ellipse { Width = lado, Height = lado, Fill = Estudio.Alerta };
        caja.Children.Add(halo);
        caja.Children.Add(punto);
        var escala = new System.Windows.Media.Animation.DoubleAnimation(1, 2.4, TimeSpan.FromSeconds(1.5))
        {
            RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
            BeginTime = TimeSpan.Zero,
        };
        var desvanece = new System.Windows.Media.Animation.DoubleAnimation(0.55, 0, TimeSpan.FromSeconds(1.5))
        {
            RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
        };
        ((ScaleTransform)halo.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, escala);
        ((ScaleTransform)halo.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, escala);
        halo.BeginAnimation(UIElement.OpacityProperty, desvanece);
        return caja;
    }

    private void PintarNota() => PintarNota(anunciar: true);

    private void PintarNota(bool anunciar)
    {
        var nota = _consulta.Nota;
        _nota.Children.Clear();
        _vivo.Text = "";
        _vacioNota.Visibility = Visibility.Collapsed;
        if (nota == null) { Estado("La nota volvió vacía."); return; }

        // SE DICE SI SE VIO O NO EN EL PORTAL, no se supone (promesa 93).
        if (anunciar)
            Estado(_consulta.VisibleEnElPortal
                ? "Nota lista. Ya se ve en el portal."
                : "Nota guardada, pero no se pudo espejar al portal. Está en el log.");

        // Los avisos necesitan la plantilla congelada y lo que se habló: se traen detrás, y la nota
        // se repinta una vez al llegar. La nota ya se ve; esperar no cuesta nada al médico.
        _ = CargarContextoDeLaNotaAsync();

        // La nota recién generada SE PUEDE CORREGIR (spec 053). Hasta el 2026-09-07 esto era texto
        // muerto: la IA organizaba y el médico solo podía mirar lo que iba a quedar en la historia
        // clínica de su paciente.
        async Task<bool> Guardado(bool ok)
        {
            Estado(ok
                ? (_consulta.VisibleEnElPortal
                    ? "Corregido. El portal ya ve el cambio."
                    : "Corregido y guardado, pero el portal no se pudo actualizar. Está en el log.")
                : _consulta.Motivo);
            return ok;
        }

        PintarSecciones(nota, editable: true,
            guardar: async (clave, texto) =>
            {
                Estado("Guardando la corrección…");
                return await Guardado(await _consulta.CorregirSeccionAsync(clave, texto));
            },
            guardarResumen: async texto =>
            {
                Estado("Guardando la corrección…");
                return await Guardado(await _consulta.CorregirResumenAsync(texto));
            });
    }

    /// <summary>
    /// LAS SECCIONES DE UNA NOTA, vengan de la consulta en curso o de una abierta desde la lista.
    /// </summary>
    /// <remarks>
    /// UN SOLO SITIO QUE LAS PINTA, y es a propósito: dos pantallas que enseñan la misma nota con
    /// dos códigos distintos acaban enseñándola distinta, y la que se ve mal es siempre la que
    /// nadie mira. Lo que cambia entre las dos es QUIÉN guarda, y eso entra por parámetro.
    ///
    /// LAS VACÍAS SE PINTAN SI SE PUEDE ESCRIBIR, y no antes. Mientras la nota era de solo lectura,
    /// una casilla vacía no era información y llenaba la pantalla de nada; en cuanto se puede
    /// corregir, esa casilla es el sitio donde el médico añade lo que la IA no oyó — esconderla
    /// sería esconder justo el hueco que viene a llenar.
    /// </remarks>
    private void PintarSecciones(NotaClinica nota, bool editable,
        Func<string, string, Task<bool>> guardar, Func<string, Task<bool>> guardarResumen,
        string cinta = "")
    {
        _nota.Children.Clear();
        _estadoDeSeccion.Clear();
        _notaEnPantalla = nota;

        // CON UN AJUSTE PENDIENTE SE VE LA PROPUESTA y no se edita nada: dos cambios a medio aprobar
        // sobre la misma nota no se pueden revisar. La nota guardada sigue siendo _notaEnPantalla.
        var vista = _propuesta?.Nota ?? nota;
        bool sePuedeEscribir = editable;
        editable = editable && _propuesta == null;

        _nota.Children.Add(CabeceraDeLaNota(vista, _abiertaId.Length > 0 ? _abiertaEstado : "borrador"));
        if (cinta.Length > 0) _nota.Children.Add(Cinta(cinta));
        _nota.Children.Add(FilaDelPaciente(sePuedeEscribir));
        _nota.Children.Add(PanelDeAvisos(vista));
        if (_propuesta != null) _nota.Children.Add(BandaDePropuesta(_propuesta));
        else if (editable) _nota.Children.Add(BarraDeAjuste());
        nota = vista;

        // EL PAPEL (spec 054): las secciones van dentro de UN documento con el filete cálido de la
        // web y el blanco de U, separadas por un hilo. Es lo que hace que la nota se lea como
        // registro y no como una lista de controles.
        var secciones = new List<UIElement>();

        // EL RESUMEN TAMBIÉN SE CORRIGE. Es el bloque más grande y el primero que se lee —y el que
        // el portal enseña en la lista de consultas—, así que dejarlo muerto mientras las secciones
        // se editaban era la mitad del trabajo y encima la mitad que más se toca. No lleva ✓: no es
        // una sección del snapshot, y el emparejador de SAP trabaja con secciones.
        if (nota.Resumen.Length > 0 || editable)
            secciones.Add(TarjetaDeSeccion(
                new SeccionDeNota("", "Resumen", nota.Resumen), editable,
                guardar: (_, texto) => guardarResumen(texto), conEnvioASap: false));

        var conTexto = new List<SeccionDeNota>();
        foreach (var s in nota.Secciones)
        {
            bool vacia = s.Contenido.Trim().Length == 0;
            if (vacia && !editable) continue;
            if (!vacia) conTexto.Add(s);
            secciones.Add(TarjetaDeSeccion(s, editable, guardar));
        }

        var hojas = new StackPanel();
        for (int i = 0; i < secciones.Count; i++)
            hojas.Children.Add(new Border
            {
                Child = secciones[i],
                BorderBrush = Estudio.DocLineaSuave,
                BorderThickness = new Thickness(0, 0, 0, i < secciones.Count - 1 ? 1 : 0),
            });
        var papel = new Border
        {
            CornerRadius = new CornerRadius(Estudio.RadioChico),
            Background = Estudio.Superficie,
            BorderBrush = Estudio.DocLinea,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(26, 4, 22, 4),
            Margin = new Thickness(2, 0, 2, 12),
            Child = hojas,
        };
        _nota.Children.Add(Estudio.Elevar(papel, Estudio.Sombra2));

        if (conTexto.Count > 1 && MostrarElEnvioASap()) _nota.Children.Add(BotonTodoASap(conTexto));
        // LA TARJETA «AVISOS» SE VA: los avisos del backend (note_json.warnings) ya entran en el panel
        // de revisión de arriba, como en la web, con su severidad y en su orden.
        _superficie.ScrollToHome();
    }

    /// <summary>Un renglón que explica por qué esta nota no se puede tocar.</summary>
    private static UIElement Cinta(string texto)
    {
        // El `AlertBanner` de la web en su tono de advertencia: icono, texto, filete y fondo suave.
        var t = Estudio.Aviso("espera", texto);
        t.Margin = new Thickness(2, 0, 2, 12);
        return t;
    }

    // ── el ✓: la sección aprobada se va a SAP (spec 008) ─────────────────────

    /// <summary>El renglón de estado de cada sección, para pintar «enviando…» y la cuenta.</summary>
    private readonly Dictionary<string, TextBlock> _estadoDeSeccion = new();
    private bool _enviando;

    /// <summary>
    /// Una sección con su ✓. Pulsarlo es aprobarla: solo ella viaja (promesa 112). El resultado se
    /// pinta debajo del texto, en la misma tarjeta, para que se vea qué pasó con ESA sección.
    /// </summary>
    /// <summary>
    /// Una sección con su ✓ y, si se puede, su editor. Promesas 112 (el ✓) y 191 (corregir).
    /// </summary>
    /// <remarks>
    /// EL TEXTO ES EL BOTÓN. No hay un lápiz aparte: se toca lo que se quiere arreglar, que es el
    /// gesto que ya usa el portal (`NoteSectionView`) y el que no hay que aprender.
    ///
    /// SE ATIENDE EL BOTÓN ABAJO Y NO EL DE ARRIBA, y esto no es un detalle de estilo: esta ventana
    /// no tiene barra de título, así que arrastra con `MouseLeftButtonDown` sobre cualquier hueco.
    /// Sin marcar el evento como atendido aquí, `DragMove` se queda con el clic y el editor no se
    /// abre nunca — el sitio se vería pulsable y no lo sería.
    ///
    /// GUARDAR ES DE VERDAD: no cierra el editor hasta que quien guarda contesta que sí. Cerrarlo
    /// antes enseñaría el texto nuevo sobre una nota que no lo tiene, que es el aprendizaje nº10 en
    /// una pantalla — parecer que funcionó.
    /// </remarks>
    private UIElement TarjetaDeSeccion(SeccionDeNota s, bool editable,
        Func<string, string, Task<bool>> guardar, bool conEnvioASap = true)
    {
        // UNA SECCIÓN DEL DOCUMENTO, NO UNA TARJETA SUELTA (spec 054, 2026-09-26). Hasta hoy cada
        // sección flotaba en su propia tarjeta con sombra, y la nota se leía como una pila de
        // controles. La web la pinta como UN papel con secciones separadas por un filete: el rótulo
        // en versalitas con aire y el cuerpo en serif. Esto es la sección; el papel lo pone
        // PintarSecciones.
        string contenido = s.Contenido;
        var cuerpo = new Grid { Margin = new Thickness(0, 8, 0, 0) };

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

        void Editar()
        {
            LogBus.Log("consulta-ui", $"editor abierto en «{s.Titulo}»");
            cuerpo.Children.Clear();

            // El campo de la web (`.clinical-control`): esquinas de 12, filete azul al escribir.
            // WPF no redondea un TextBox, así que lo redondea el borde que lo envuelve.
            var caja = new TextBox
            {
                Text = contenido,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 84,
                MaxHeight = 320,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontSize = 14.5,
                Padding = new Thickness(4, 2, 4, 2),
                Background = Brushes.Transparent,
                Foreground = Estudio.Tinta,
                BorderThickness = new Thickness(0),
                CaretBrush = Estudio.Acento,
                SelectionBrush = Estudio.Acento,
            };
            var marcoDeLaCaja = new Border
            {
                Child = caja,
                CornerRadius = new CornerRadius(Estudio.RadioChico),
                BorderBrush = Estudio.Acento,
                BorderThickness = new Thickness(1.5),
                Background = Estudio.Superficie,
                Padding = new Thickness(8, 7, 8, 7),
            };

            var guardarBtn = Estudio.BotonPrimario("Guardar", "check", 34);
            var cancelar = Estudio.BotonSecundario("Cancelar", null, 34);
            cancelar.Margin = new Thickness(8, 0, 0, 0);
            cancelar.Click += (_, __) => Leer();
            async Task Guardar()
            {
                string nuevo = caja.Text ?? "";
                if (nuevo == contenido) { Leer(); return; }
                guardarBtn.IsEnabled = false;
                cancelar.IsEnabled = false;
                guardarBtn.Content = Estudio.ConIcono("loader-circle", "Guardando…", Brushes.White);
                bool ok = await guardar(s.Clave, nuevo);
                // SE REPINTA LA NOTA ENTERA y no solo la sección: los avisos se recalculan con lo
                // corregido, y el ajuste siguiente parte de la nota guardada, no de la de antes.
                if (ok) { contenido = nuevo; Leer(); RepintarLaNota(); }
                else
                {
                    // NO SE CIERRA EL EDITOR SI NO SE GUARDÓ: lo escrito se queda donde está para
                    // poder reintentar. Cerrarlo perdería el trabajo y encima parecería guardado.
                    guardarBtn.IsEnabled = true;
                    cancelar.IsEnabled = true;
                    guardarBtn.Content = Estudio.ConIcono("check", "Guardar", Brushes.White);
                }
            }
            guardarBtn.Click += async (_, __) => await Guardar();

            // LOS ATAJOS «/» VAN ANTES que el teclado del editor: con el menú abierto, Enter y Esc son
            // suyos. Lo insertado es texto normal de la sección y se edita como cualquier otro.
            EngancharAtajos(caja, s.Clave);

            // EL TECLADO PRIMERO, que es lo que hace a Windows más rápido que la web: Ctrl+Enter o
            // Ctrl+S guardan, Esc deja la sección como estaba. Esc se atiende AQUÍ porque la ventana
            // entera lo usa para minimizarse, y sin marcarlo se minimizaría con el texto a medias.
            caja.PreviewKeyDown += async (_, e) =>
            {
                if (e.Handled) return;
                bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
                if (ctrl && (e.Key == Key.Enter || e.Key == Key.S)) { e.Handled = true; await Guardar(); }
                else if (e.Key == Key.Escape) { e.Handled = true; Leer(); }
            };

            var ayuda = new TextBlock
            {
                Text = _atajos.Count > 0 ? "/ atajos · Ctrl+Enter guarda · Esc cancela" : "Ctrl+Enter guarda · Esc cancela",
                Foreground = Estudio.TintaTenue,
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
            };

            var botones = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 10, 0, 0),
            };
            botones.Children.Add(guardarBtn);
            botones.Children.Add(cancelar);
            botones.Children.Add(ayuda);

            var pilaEdicion = new StackPanel();
            pilaEdicion.Children.Add(marcoDeLaCaja);
            pilaEdicion.Children.Add(botones);
            cuerpo.Children.Add(pilaEdicion);

            caja.Focus();
            caja.CaretIndex = caja.Text.Length;
        }

        void Leer()
        {
            cuerpo.Children.Clear();

            bool vacia = contenido.Trim().Length == 0;
            UIElement texto = vacia
                ? new TextBlock
                {
                    Text = editable ? "Sin contenido. Toca para escribir." : "Sin contenido.",
                    Foreground = Estudio.DocTenue,
                    FontSize = 14,
                    TextWrapping = TextWrapping.Wrap,
                }
                : Estudio.Documento(contenido);

            if (!editable) { cuerpo.Children.Add(texto); return; }

            var zona = new Border
            {
                Child = texto,
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(Estudio.RadioChico),
                Padding = new Thickness(10, 6, 10, 6),
                // Márgenes negativos: la zona pulsable respira más que el texto sin mover el texto
                // ni un píxel respecto a cómo se veía antes.
                Margin = new Thickness(-10, -4, -10, -4),
                Cursor = Cursors.IBeam,
            };
            zona.MouseEnter += (_, __) => zona.Background = Estudio.HieloSuave;
            zona.MouseLeave += (_, __) => zona.Background = Brushes.Transparent;
            zona.MouseLeftButtonDown += (_, e) => { e.Handled = true; Editar(); };
            cuerpo.Children.Add(zona);
        }

        // ── la cabecera: el rótulo y las acciones ───────────────────────────
        //
        // ICONOS CALLADOS QUE SE ENCIENDEN AL ACERCARSE (2026-09-07, lo pidió el dueño: «se ve como
        // unos fondos malucos que sobrecargan la parte visual»), y los de la web: copiar, corregir y
        // —solo donde hay SAP— el ✓. El lápiz existe porque sin él nada decía que el texto se podía
        // tocar (la promesa 164 prohíbe los carteles al pasar el ratón).
        var acciones = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        var copiar = Estudio.BotonIcono("clipboard-copy", "Copiar sección", 30, 15, Estudio.TintaTenue);
        copiar.Click += (_, e) =>
        {
            e.Handled = true;
            if (contenido.Trim().Length == 0) { Estado("Esa sección está vacía: no hay nada que copiar."); return; }
            try { Clipboard.SetText(contenido); Estado($"«{s.Titulo}» copiada."); }
            catch (Exception ex) { Estado($"No se pudo copiar: {ex.Message}"); }
        };
        acciones.Children.Add(copiar);

        if (editable)
        {
            var lapiz = Estudio.BotonIcono("pencil", "Corregir sección", 30, 15, Estudio.TintaTenue);
            lapiz.Margin = new Thickness(2, 0, 0, 0);
            lapiz.Click += (_, e) => { e.Handled = true; Editar(); };
            acciones.Children.Add(lapiz);

            // AJUSTAR EN VOZ ALTA (promesa 452): «quiero que diga…» se escribe tal cual, «agrega que…»
            // se redacta dentro, y lo demás es una instrucción. Una pulsación escucha, otra decide.
            var micro = Estudio.BotonIcono("mic", "Dictar un cambio a esta sección", 30, 15, Estudio.TintaTenue);
            micro.Margin = new Thickness(2, 0, 0, 0);
            micro.Click += async (_, e) =>
            {
                e.Handled = true;
                await DictarCambioAsync(new SeccionDeNota(s.Clave, s.Titulo, contenido), micro);
            };
            if (_dictandoA == s.Clave) PintarMicroDeDictado(micro, true);
            acciones.Children.Add(micro);
        }

        if (conEnvioASap && MostrarElEnvioASap())
        {
            var check = Estudio.BotonIcono("check", "Enviar a SAP", 30, 16, Estudio.TintaTenue);
            check.Margin = new Thickness(2, 0, 0, 0);
            check.Click += async (_, e) => { e.Handled = true; await EnviarASapAsync(new[] { s.Clave }); };
            acciones.Children.Add(check);
        }

        var cabecera = new DockPanel();
        DockPanel.SetDock(acciones, Dock.Right);
        cabecera.Children.Add(acciones);
        var rotulo = Estudio.RotuloDeDocumento(s.Titulo);
        rotulo.VerticalAlignment = VerticalAlignment.Center;
        cabecera.Children.Add(rotulo);

        Leer();

        var pila = new StackPanel { Margin = new Thickness(0, 14, 0, 16) };
        pila.Children.Add(cabecera);
        pila.Children.Add(cuerpo);
        pila.Children.Add(estado);
        if (!LaCambioLaPropuesta(s.Clave)) return pila;

        // LO QUE CAMBIÓ LA PROPUESTA SE VE SIN BUSCARLO: una banda azul a la izquierda y el hielo de
        // fondo, como la web marca las secciones sin guardar.
        pila.Margin = new Thickness(0);
        return new Border
        {
            Child = pila,
            Background = Estudio.HieloSuave,
            BorderBrush = Estudio.Acento,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(14, 14, 10, 16),
            Margin = new Thickness(-17, 0, -12, 0),
        };
    }

    /// <summary>
    /// ¿SE ENSEÑA EL ✓ A SAP? Solo si hay puente y SAP GUI está abierto en este equipo.
    /// </summary>
    /// <remarks>
    /// El médico privado —el usuario de ahora, dicho por el dueño el 2026-09-25— no tiene SAP, y un ✓
    /// que manda a un sistema que no existe es un botón que miente. El envío no se toca: vuelve en
    /// cuanto hay un SAP abierto, que es la etapa hospital.
    /// </remarks>
    private static bool MostrarElEnvioASap()
    {
        if (!PuenteASap.Disponible) return false;
        try { return System.Diagnostics.Process.GetProcessesByName("saplogon").Length > 0; }
        catch (Exception e)
        {
            LogBus.Log("consulta-ui", $"no se pudo mirar si SAP está abierto: {e.GetType().Name}: {e.Message}");
            return false;
        }
    }

    private UIElement BotonTodoASap(IReadOnlyList<SeccionDeNota> secciones)
    {
        var b = Estudio.BotonSecundario("Todo a SAP", "check", 38, Estudio.Acento);
        b.HorizontalAlignment = HorizontalAlignment.Center;
        b.Margin = new Thickness(0, 2, 0, 12);
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
        // LA QUE SE VE, y no siempre la de la consulta en curso: desde el 2026-09-07 se puede tener
        // abierta una consulta anterior, y mandar a SAP la nota de otra sería escribir en la
        // historia clínica de un paciente lo que se dijo de otro.
        if (_notaEnPantalla == null) { Estado("No hay nota que enviar."); return; }
        if (!PuenteASap.Disponible)
        {
            Estado("La carita no está lista para escribir en SAP: espera a que arranque y vuelve a pulsar ✓.");
            return;
        }

        var encargo = Encargo.De(_notaEnPantalla, claves);
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
        // El título de página de la web: Schibsted Grotesk con la barra azul a la izquierda.
        fila.Children.Add(new Border
        {
            Width = 3, Height = 22, CornerRadius = new CornerRadius(2), Background = Estudio.Acento,
            Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center,
        });
        fila.Children.Add(Estudio.Titulo(texto, 21));
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

    private static Button BotonPrincipal(string texto, Brush? fondo = null)
    {
        // El primario de la web (degradado azul) salvo cuando el fondo dice otra cosa (borrar es rojo).
        var b = Estudio.BotonPrimario(texto, alto: 46);
        b.Margin = new Thickness(2, 0, 2, 0);
        b.HorizontalAlignment = HorizontalAlignment.Stretch;
        if (fondo != null)
        {
            b.Background = fondo;
            b.MouseEnter += (_, __) => b.Background = fondo;
            b.MouseLeave += (_, __) => b.Background = fondo;
        }
        return b;
    }

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
            Text = titulo, Foreground = Estudio.TintaFuerte, FontFamily = Estudio.FuenteTitulo, FontSize = 16,
            FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
        });
        pila.Children.Add(new TextBlock
        {
            Text = cuerpo, Foreground = Estudio.TintaMedia, FontSize = 12.5, LineHeight = 19,
            TextWrapping = TextWrapping.Wrap,
        });
        var t = Estudio.Tarjeta(Estudio.RadioMedio);
        t.Padding = new Thickness(20, 22, 20, 22);
        t.Margin = new Thickness(2, 8, 2, 0);
        t.Child = pila;
        return Estudio.Elevar(t);
    }

    /// <summary>Como <see cref="BotonDeMarco"/>, pero con un dibujo dentro en vez de un glifo.</summary>
    private static Button BotonDeIcono(FrameworkElement dibujo, string queHace)
    {
        var b = new Button
        {
            Content = dibujo, Width = 30, Height = 30, Margin = new Thickness(2, 0, 0, 0),
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand, Template = Estudio.Pastilla(15),
        };
        AutomationProperties.SetName(b, queHace);
        b.MouseEnter += (_, __) => { if (b.Background == Brushes.Transparent) b.Background = Estudio.HieloSuave; };
        b.MouseLeave += (_, __) => { if (b.Background == Estudio.HieloSuave) b.Background = Brushes.Transparent; };
        return b;
    }

    private static Button BotonDeMarco(string icono, string queHace)
    {
        // Los de Lucide y no los de MDL2 (spec 054): minimizar y cerrar se reconocen sin leerlos en
        // cualquier familia de iconos, y así la cabecera entera habla la misma.
        var b = new Button
        {
            Content = Estudio.Icono(icono, 15, Estudio.TintaMedia),
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
        System.Windows.Automation.AutomationProperties.SetName(b, queHace);
        b.MouseEnter += (_, __) => b.Background = Estudio.HieloSuave;
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
        BorderBrush = Brushes.Transparent,
        BorderThickness = new Thickness(1),
        FontSize = 13,
        // Todas en seminegrita, como `.seg-item` de la web: la activa no se lee por el peso sino
        // por ser la pieza blanca que sobresale del carril.
        FontWeight = FontWeights.SemiBold,
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

    private UIElement FilaDeConsulta(ConsultaVista c)
    {
        // LA TARJETA DE CONSULTA DE LA WEB (`ConsultationCard`, spec 054): arriba lo que la
        // identifica y el estado con las MISMAS palabras y colores que el portal; debajo el motivo.
        var pila = new StackPanel();

        var arriba = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var chip = Estudio.ChipDeEstado(c.Estado);
        DockPanel.SetDock(chip, Dock.Right);
        arriba.Children.Add(chip);
        arriba.Children.Add(new TextBlock
        {
            Text = c.Fecha == DateTimeOffset.MinValue ? "" : c.Fecha.ToLocalTime().ToString("d MMM · HH:mm"),
            Foreground = Estudio.TintaMedia, FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
        });
        pila.Children.Add(arriba);

        pila.Children.Add(new TextBlock
        {
            Text = c.Motivo.Length > 0 ? c.Motivo
                 : c.Resumen.Length > 0 ? c.Resumen
                 : "Consulta sin motivo anotado",
            Foreground = Estudio.TintaFuerte, FontSize = 14.5, FontWeight = FontWeights.SemiBold, LineHeight = 21,
            TextWrapping = TextWrapping.Wrap, MaxHeight = 64, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        // DE QUIÉN ES, como la tarjeta de la web: el paciente antes que la plantilla.
        string quien = string.Join(" · ", new[] { c.Paciente, c.Documento }.Where(x => x.Length > 0));
        if (quien.Length > 0)
            pila.Children.Add(new TextBlock
            {
                Text = quien, Foreground = Estudio.TintaSuave, FontSize = 12.5, FontWeight = FontWeights.Medium,
                Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis,
            });
        if (c.Plantilla.Length > 0)
            pila.Children.Add(new TextBlock
            {
                Text = c.Plantilla, Foreground = Estudio.TintaMedia, FontSize = 12.5,
                Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis,
            });

        var t = Estudio.Tarjeta(Estudio.RadioMedio);
        t.Padding = new Thickness(16, 13, 16, 14);
        t.Margin = new Thickness(2, 0, 2, 10);
        t.Child = pila;

        // LA LISTA SE ABRE. Hasta el 2026-09-07 esto era un escaparate: la consulta se veía y no se
        // podía tocar, así que corregir una nota de hace diez minutos obligaba a irse al navegador.
        t.Cursor = Cursors.Hand;
        t.MouseEnter += (_, __) => { t.Background = Estudio.HieloSuave; t.BorderBrush = Estudio.Niebla; };
        t.MouseLeave += (_, __) => { t.Background = Estudio.Superficie; t.BorderBrush = Estudio.Borde; };
        // Atendido aquí porque la ventana arrastra con este mismo evento: sin marcarlo, DragMove se
        // queda con el clic y la tarjeta parecería pulsable sin serlo.
        t.MouseLeftButtonDown += async (_, e) => { e.Handled = true; await AbrirConsultaAsync(c); };

        return Estudio.Elevar(t);
    }

    // ── una consulta anterior, abierta desde la lista (spec 053) ─────────────

    /// <summary>
    /// Abre una consulta de la lista: trae su nota del backend y la deja lista para corregir.
    /// </summary>
    /// <remarks>
    /// LA NOTA SE PIDE AL BACKEND CLÍNICO Y NO AL ESPEJO, y esa decisión es lo que hace que
    /// corregir sea posible. El espejo (`consultations.note`) guarda las secciones como
    /// <c>id/titulo/kind/texto</c> para que el portal las pinte; el backend devuelve el
    /// <c>note_json</c> con las CLAVES del snapshot, que es lo único que `PUT /note` acepta
    /// (promesa 442). Reconstruir la nota desde el espejo daría un texto idéntico en pantalla y un
    /// 400 al guardar — o, peor, una nota a la que le faltan las secciones que el espejo no pintó.
    ///
    /// EL ESTADO SÍ SALE DE LA LISTA: `borrador`, `revisada`, `aprobada` o `exportada` son del
    /// portal, y el backend clínico no sabe nada de ellos. Es lo que decide si se puede corregir
    /// (promesa 443).
    /// </remarks>
    private async Task AbrirConsultaAsync(ConsultaVista c)
    {
        Mostrar(nota: true);
        _nota.Children.Clear();
        _vivo.Text = "";
        _vacioNota.Visibility = Visibility.Collapsed;
        Estado("Abriendo la consulta…");

        try
        {
            var enc = await _clinica.LeerEncounterAsync(c.Id);
            if (enc.Nota == null || enc.Nota.Secciones.Count == 0)
            {
                // SE DICE QUÉ PASÓ, no «no se pudo»: una consulta sin nota es una que se grabó y no
                // llegó a organizarse, y eso se arregla en otro sitio.
                Estado("Esta consulta todavía no tiene una nota organizada.");
                LogBus.Log("consulta-ui", $"la consulta {c.Id} no traía nota");
                return;
            }

            // LO QUE SE CORRIGIÓ EN LA WEB MANDA (promesa 462): el detalle web escribe en el espejo,
            // no en el backend clínico, y abrir aquí la de Graph enseñaría la nota de antes.
            var fila = await EspejoDeConsulta.LeerFilaAsync(_sesion, c.Id);
            var nota = NotaDelPortal.Fusionar(enc.Nota, fila);
            string pacienteId = enc.PacienteId.Length > 0 ? enc.PacienteId
                : fila.ValueKind == System.Text.Json.JsonValueKind.Object && fila.TryGetProperty("patient_id", out var pid)
                  && pid.ValueKind == System.Text.Json.JsonValueKind.String ? pid.GetString() ?? "" : "";

            _abiertaId = c.Id;
            _abiertaEstado = c.Estado;
            _abiertaNota = nota;
            _propuesta = null;
            _contextoDe = c.Id;
            _contextoPlantilla = enc.PlantillaCongelada;
            _contextoTranscripcion = enc.Transcripcion;
            _paciente = await PacientesDelMedico.LeerAsync(_sesion, pacienteId);

            bool sePuede = ReglaDeLaEdicion.SePuedeEditar(c.Estado);
            PintarLaAbierta();

            Estado(sePuede
                ? "Toca cualquier sección para corregirla. El ✓ la manda a SAP."
                : "Solo lectura. El ✓ sigue mandando a SAP.");
            LogBus.Log("consulta-ui", $"consulta abierta · estado «{c.Estado}» · "
                                    + $"{nota.Secciones.Count} sección(es) · "
                                    + (sePuede ? "editable" : "solo lectura"));
        }
        catch (ErrorClinico e) { Estado(e.Message); }
        catch (Exception e)
        {
            Estado("No se pudo abrir la consulta. Comprueba la red y vuelve a tocarla.");
            LogBus.Log("consulta-ui", $"abrir consulta: {e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>
    /// Guarda la corrección de una consulta anterior: primero el backend, después el espejo.
    /// </summary>
    /// <remarks>
    /// EL ORDEN ES EL MISMO QUE EN <see cref="Consulta.CorregirSeccionAsync"/> y por la misma razón:
    /// si el espejo fuera primero, un `PUT` fallido dejaría el portal enseñando un texto que la
    /// historia clínica no tiene.
    ///
    /// NO PASA POR <see cref="Consulta"/> a propósito: esa clase es la máquina de estados de la
    /// consulta EN CURSO, y meterle dentro un encounter viejo la dejaría creyendo que tiene una
    /// nota que no es la suya — con el botón de grabar leyendo ese estado.
    /// </remarks>
    private async Task<bool> GuardarCorreccionDeLaAbiertaAsync(string clave, string texto)
    {
        if (_abiertaNota == null || _abiertaId.Length == 0) return false;

        Estado("Guardando la corrección…");
        try
        {
            var guardada = await _clinica.GuardarNotaEditadaAsync(
                _abiertaId, _abiertaNota.ConSeccion(clave, texto));
            _abiertaNota = guardada;
            _notaEnPantalla = guardada;

            bool enElPortal = await EspejoDeConsulta.EscribirAsync(_sesion, _http,
                EspejoDeConsulta.FilaDeCorreccion(_abiertaId, guardada));

            Estado(enElPortal
                ? "Corregido. El portal ya ve el cambio."
                : "Corregido y guardado, pero el portal no se pudo actualizar. Está en el log.");
            LogBus.Log("consulta-ui", $"sección «{clave}» corregida en la consulta abierta");
            return true;
        }
        catch (ErrorClinico e) { Estado(e.Message); return false; }
        catch (Exception e)
        {
            Estado($"No se pudo guardar la corrección: {e.Message}");
            return false;
        }
    }

    /// <summary>El resumen de una consulta abierta de la lista. Mismo camino que una sección.</summary>
    private async Task<bool> GuardarResumenDeLaAbiertaAsync(string texto)
    {
        if (_abiertaNota == null || _abiertaId.Length == 0) return false;

        Estado("Guardando la corrección…");
        try
        {
            var guardada = await _clinica.GuardarNotaEditadaAsync(
                _abiertaId, _abiertaNota.ConResumen(texto));
            _abiertaNota = guardada;
            _notaEnPantalla = guardada;

            bool enElPortal = await EspejoDeConsulta.EscribirAsync(_sesion, _http,
                EspejoDeConsulta.FilaDeCorreccion(_abiertaId, guardada));
            Estado(enElPortal
                ? "Corregido. El portal ya ve el cambio."
                : "Corregido y guardado, pero el portal no se pudo actualizar. Está en el log.");
            return true;
        }
        catch (ErrorClinico e) { Estado(e.Message); return false; }
        catch (Exception e)
        {
            Estado($"No se pudo guardar la corrección: {e.Message}");
            return false;
        }
    }

    /// <summary>Deja de mirar la consulta anterior y vuelve a la de ahora.</summary>
    private void CerrarLaAbierta()
    {
        if (_abiertaId.Length == 0) return;
        _abiertaId = "";
        _abiertaEstado = "";
        _abiertaNota = null;
        _propuesta = null;
        _paciente = null;
        _contextoDe = "";

        // Se repinta lo que corresponda: la nota en curso si la hay, y si no la pantalla de empezar.
        _nota.Children.Clear();
        _notaEnPantalla = null;
        if (_consulta.Nota != null) PintarNota();
        else
        {
            _vivo.Text = "";
            _vacioNota.Visibility = Visibility.Visible;
            Estado(_plantillaId.Length > 0 ? "Listo." : "");
        }
    }

    /// <summary>Pinta la consulta abierta desde la lista con lo que haya ahora (tras abrirla o guardarla).</summary>
    private void PintarLaAbierta()
    {
        if (_abiertaNota == null) return;
        bool sePuede = ReglaDeLaEdicion.SePuedeEditar(_abiertaEstado);
        PintarSecciones(_abiertaNota, sePuede,
            guardar: GuardarCorreccionDeLaAbiertaAsync,
            guardarResumen: GuardarResumenDeLaAbiertaAsync,
            cinta: sePuede ? "" : ReglaDeLaEdicion.PorQueNo(_abiertaEstado));
    }
}
