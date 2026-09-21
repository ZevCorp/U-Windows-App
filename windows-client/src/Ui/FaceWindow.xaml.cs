using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Animation;
using U.Graph;
using U.Graph.Surfaces;
using U.WindowsClient.Agent;
using U.WindowsClient.Backend;
using U.WindowsClient.Clinical;
using U.WindowsClient.Diagnostics;
using U.WindowsClient.Mcp;
using U.WindowsClient.Navigation;
using U.WindowsClient.SystemApi;
using U.WindowsClient.Teach;
using U.WindowsClient.Telemetry;
using U.WindowsClient.Uia;
using U.WindowsClient.Update;
using U.WindowsClient.Voice;
using U.WindowsClient.Workflows;

namespace U.WindowsClient.Ui;

/// <summary>
/// La carita flotante: el frontend completo. Recoge lo que el usuario pide (texto o voz), lanza el
/// <see cref="AgentLoop"/> (que consulta al cerebro remoto) y muestra narración/estado. Implementa
/// <see cref="IVoice"/> y <see cref="IUserChannel"/> para que el bucle hable y pregunte por esta UI.
/// No contiene ninguna lógica de decisión.
/// </summary>
public partial class FaceWindow : Window, IVoice, IUserChannel
{
    private Config _config = Config.Load();
    private readonly UiaReader _uia = new();
    private readonly VoiceIO _voice = new();

    /// <summary>La conversación en vivo, si el mapa está disponible. Ver <see cref="ConversacionEnVivo"/>.</summary>
    private ConversacionEnVivo? _vivo;

    /// <summary>El mapa vivo publicado en Neo4j. Ver <see cref="Navigation.MapaVivo"/>.</summary>
    private Navigation.MapaVivo? _mapaVivo;
    /// <summary>Lo último que la mano SAP pulsó: el destino del lápiz cuando el foco calla.</summary>
    private string _ultimoSapPulsado = "";
    /// <summary>La ventana en la que Ü trabaja, distinta del foco de la persona (spec 020, promesa 233).</summary>
    private readonly Navigation.VentanaDeTrabajo _trabajo = new();
    /// <summary>Por qué la última mano no pudo, para que el ejecutor lo cuente (promesa 231).</summary>
    private string _ultimoMotivoDeLaMano = "";
    /// <summary>
    /// Los árboles de la última observación SAP, con su caja de pantalla y sus filas visibles
    /// (clave, texto y rectángulo local). El nombrado del clic humano vive de esto: geometría
    /// primero, selección como corroboración.
    /// </summary>
    private readonly List<(string Id, string Type, string Label, int X, int Y, int W, int H,
        List<(string Key, string Text, int Top, int Height, bool EsCarpeta)> Filas)> _arbolesVistos = new();

    /// <summary>De qué ubicación son los árboles compartidos: contra otra pantalla, están rancios.</summary>
    private string _ubicacionDeArboles = "";

    /// <summary>El recuadro que se pinta sobre lo señalado. Nace al primer señalamiento y no antes:
    /// quien nunca señala no paga una ventana de más.</summary>
    private HighlightOverlay? _iluminacion;

    /// <summary>La ventanita por la que se le puede pedir al núcleo que nos lleve a un sitio.</summary>
    private Navigation.ServidorDelNucleo? _servidorNucleo;

    /// <summary>Hay una frase escribiéndose: los trozos que lleguen la actualizan, no la repiten.</summary>
    private bool _turnoAbierto;
    private readonly VideoLibrary _videoLibrary = new();
    private readonly GraphConfig _graphConfig = GraphConfig.Load();
    private Updater? _updater;
    private bool _actualizando;
    private Updater.ReleaseMessage? _mensajeDeActualizacion;
    private AgentLoop _loop = null!;
    private BackendClient? _backend;
    private CancellationTokenSource? _cts;
    private VideoLibraryWindow? _videoWindow;
    private LogWindow? _logWindow;
    private WorkflowLibraryWindow? _workflowWindow;
    // "Enseñar" unificado: graba pasos UIA (WorkflowRecorder) + video (TeachSession) en paralelo.
    private WorkflowTeachSession? _teachSession;
    // Las dos superficies de la enseñanza, con su diagnóstico enchufado al registro. Sin esto sus
    // eventos se emitían al vacío: `SapGuiSurface.Diagnostic` no tenía UN SOLO suscriptor en todo el
    // cliente, así que la línea que dice si el enganche de eventos COM funcionó —«observando por
    // eventos COM» vs «eventos COM no disponibles, cae a sondeo»— nunca llegó al log. Justo la que
    // decide si la grabación puede capturar la entrada a una transacción.
    private readonly UiaSurface _teachUiaSurface = new() { Log = s => LogBus.Log("teach-uia", s) };
    private readonly SapGuiSurface _teachSapSurface = new();
    private bool _teaching;
    // El aura en los bordes del monitor mientras Ü aprende (spec 006). Se crea la primera vez que
    // hace falta y se reutiliza: ocultar y volver a mostrar es más barato que otra ventana por capas.
    private AuraDeAprendizaje? _aura;
    private UiInspector? _inspector;
    private SurfaceLocator? _locator;
    private LocatorBadge? _badge;

    /// <summary>La lista de lo que va pasando. Ver <see cref="PanelDeAcciones"/>.</summary>
    private PanelDeAcciones? _acciones;

    // El mapa base del computador (la capa gris): se alimenta SIEMPRE del caudal del locator,
    // esté o no abierta la visualización — el terreno se acumula mientras el usuario vive su día.
    private ClickWatcher? _clickWatcher;
    private WorkflowMcpRunner? _workflowRunner;

    /// <summary>Las manos del asistente, para poder preguntarles desde el panel. Ver OnVerRecuerdos.</summary>
    private Mcp.SurfaceMapTools? _mapaDeMano;

    /// <summary>La puerta MCP real (127.0.0.1:8790/mcp) por la que entra el Agent SDK.</summary>
    private Mcp.ServidorMcp? _servidorMcp;
    /// <summary>Los nombres del catálogo MCP, para armar las dos cajas del piloto (spec 013).</summary>
    private List<string> _nombresMcp = new();

    // Selector de workflow directo en el panel Backend: lista cargada de Graph + un GraphClient propio
    // para listar/ejecutar sin abrir la biblioteca. El slider indexa esta lista.
    private GraphClient? _directGraph;
    private readonly List<WorkflowSummary> _directWorkflows = new();
    private int _directIndex;            // workflow seleccionado (carrusel y lista comparten este índice)
    private bool _directListMode;        // false = carrusel, true = lista
    private bool _syncingWorkflowUi;     // evita el ida y vuelta carrusel ↔ lista al sincronizar
    // El workflow a la mano (spec 007): los nombres que el operador pone, los planes pedidos por
    // adelantado, y el id de lo que se acaba de enseñar para que quede elegido al recargar.
    private readonly NombresDeWorkflows _nombres = new();
    private PlanesALaMano? _planes;
    private string? _nuevoWorkflowId;
    // La precarga espera a que el carrusel se quede quieto: pasar por diez workflows con las
    // flechas no tiene que pedir diez planes.
    private readonly System.Windows.Threading.DispatcherTimer _precarga = new()
    {
        Interval = TimeSpan.FromMilliseconds(300),
    };
    private bool _runningDirect;

    // Para resolver preguntas del asistente desde la caja de texto.
    private TaskCompletionSource<string>? _pendingAnswer;

    public FaceWindow()
    {
        InitializeComponent();
        // La precarga del plan dispara cuando el carrusel lleva 300 ms quieto sobre un workflow (spec 007).
        _precarga.Tick += (_, _) =>
        {
            _precarga.Stop();
            if (_directIndex >= 0 && _directIndex < _directWorkflows.Count)
                _planes?.Precarga(_directWorkflows[_directIndex].Id);
        };
        // La misma barra de scroll rehecha que las ventanas claras, en su variante para suelo
        // oscuro: un pulgar redondeado sin flechas ni carril. La de Windows por defecto era lo
        // único de este panel que seguía pareciendo de otra aplicación.
        this.PonerLaBarraDeScroll(sobreOscuro: true);
        // Aquí y no al crear la WorkflowTeachSession: esa se construye en CADA pulsación de «Enseñar»
        // y acumularía una suscripción por intento, multiplicando cada línea en el registro.
        _teachSapSurface.Diagnostic += (_, msg) => LogBus.Log("teach-sap", msg);
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // Identidad del usuario (nombre+correo) al instalar. Debe ir ANTES de crear el backend para que
        // la telemetría de "Windows Live" arranque con el usuario correcto.
        EnsureOnboarded();

        MaxHeight = SystemParameters.WorkArea.Height - 48; // al llegar al tope, el globo hace scroll
        ColocarVentana();

        // El reanclaje se engancha DESPUÉS del primer layout completo, no aquí.
        //
        // Con SizeToContent la ventana sigue asentando su tamaño después de Loaded, y OnSizeChanged
        // no puede distinguir «el contenido terminó de medirse» de «el usuario abrió el menú»: trata
        // el asentamiento como un crecimiento y desplaza la ventana para mantener el borde derecho.
        // Medido: 20 px de deriva por arranque, acumulándose hasta pegarse al borde. Se recoloca una
        // última vez con el tamaño ya definitivo y ahí sí se escucha.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ColocarVentana();
            RefreshBarSide();          // si quedó a la izquierda, el layout se espeja antes de verse
            SizeChanged += OnSizeChanged;
            // El muelle se enseña DESPUÉS del primer layout, por lo mismo que la carita: con
            // SizeToContent no sabe cuánto ocupa hasta que se ha medido, y sin eso no puede pegarse
            // al borde derecho — nacería centrado y daría un salto a su sitio.
            _muelle?.Show();
            RefreshRestingChevron();   // ya se puede medir el hueco: el chevron dice hacia dónde abrirá
            LeerSuEscritorio("al arrancar");   // promesa 270: su escritorio es el de su carita, leído del sistema
        }), System.Windows.Threading.DispatcherPriority.Loaded);

        UpdateBackendStatus();
        // El semáforo se repinta solo cada vez que alguien habla con Graph, venga de donde venga.
        // Llega desde la continuación HTTP, no del Dispatcher: hay que marshalear, como ya hace
        // _updater.UpdateReady. Y hay que soltarlo al cerrar: es un evento ESTÁTICO.
        GraphHealth.Changed += OnGraphHealthChanged;
        Closed += (_, __) => GraphHealth.Changed -= OnGraphHealthChanged;
        SetMuted(_config.Muted); // si lo silenciaron en una sesión anterior, sigue mudo

        // NACE AL ARRANCAR Y NO A LA PRIMERA PALABRA: el gesto de asomarlo acercando el cursor al
        // borde de arriba (promesa 260) tiene que funcionar «sin importar si Ü está hablando o no»
        // (pedido del dueño, 2026-09-17), también antes de que diga nada. Antes nacía perezoso, en
        // el primer Habla()/Empieza(), y hasta ese momento el gesto no tenía a quién asomar.
        _acciones ??= new PanelDeAcciones();

        Closed += (_, __) =>
        {
            _inspector?.Dispose(); // suelta el hook global de mouse al cerrar
            _locator?.Dispose();
            _badge?.Close();
        };

        // El "location bar de Windows": arranca encendido mostrando el ID de superficie arriba a la
        // derecha. Es la base del scoping de workflows (mismo formato que source_url en Graph).
        // LA CARITA VA A DONDE MIRA. Cuando el asistente dice que ve un elemento, ponerse a su lado
        // es lo que convierte «lo veo» en algo comprobable: si se planta junto a otra cosa, se ve al
        // instante. Es la misma idea que el recuadro, dicha con el cuerpo (2026-08-05).
        Senalador.Senala += (caja, _) => Dispatcher.BeginInvoke(() => IrJuntoA(caja));

        // ILUMINAR ES PARA QUIEN MIRA, NO PARA QUIEN PROGRAMA. El recuadro se pintaba solo desde
        // GraphExplorerWindow —la ventana del grafo, una herramienta de desarrollo— así que señalar
        // solo se veía si esa ventana estaba abierta. Para todo el mundo demás, Ü decía «lo estoy
        // iluminando» y no se iluminaba nada: prometer algo que no pasa es peor que no prometerlo
        // (2026-08-22, observado por el usuario).
        //
        // La carita ya se movía al lado de la caja; lo que faltaba era la caja. Va aquí, que es la
        // ventana que SIEMPRE está.
        Senalador.SenalaVarias += cajas => Dispatcher.BeginInvoke(() =>
        {
            try
            {
                // Y SE MUESTRA. Sin este Show() la ventana existe, recibe las cajas y no la ve
                // nadie: exactamente el mismo síntoma que veníamos a arreglar, con otra causa. Lo
                // enseñó una captura de pantalla —la carita se movía al lado del icono y no había
                // recuadro— que es la única forma de haberlo cazado (2026-08-22).
                if (_iluminacion == null) { _iluminacion = new HighlightOverlay(); _iluminacion.Show(); }
                _iluminacion.ShowRects(cajas);
            }
            catch { }
        });
        Senalador.Suelta += () => Dispatcher.BeginInvoke(() =>
        {
            try { _iluminacion?.HideRect(); } catch { }
            // Y LAS TARJETAS CON ÉL. Soltar lo señalado es «ya no estoy mirando eso»; dejar el
            // texto encima diría lo contrario. TarjetasDeRecuerdo respeta al que esté escribiendo.
            try { TarjetasDeRecuerdo.Cerrar(); } catch { }
        });
        Closed += (_, __) => { try { _iluminacion?.Close(); } catch { } };

        // VARIAS COSAS SE SEÑALAN RECORRIÉNDOLAS. Plantarse junto a una de las seis y quedarse ahí
        // era el gesto de señalar UNA, heredado sin más al señalar varias: los recuadros decían seis
        // y el cuerpo decía una (2026-08-07, pedido por el usuario). Ir a cada una es lo que hace
        // una persona cuando enumera algo con la mano.
        Senalador.SenalaVarias += cajas => Dispatcher.BeginInvoke(() => Recorrer(cajas));

        // Cuando se abre el catálogo de apps, la carita se pone JUSTO ENCIMA y centrada: es ella la
        // que está preguntando «¿cuál quieres que aprenda?», y una pregunta se hace de frente, no
        // desde una esquina. Al cerrarse vuelve a donde estaba.
        CarruselDeApps.Colocado += caja => Dispatcher.BeginInvoke(() =>
        {
            if (caja.IsEmpty) { VolverASuSitio(); return; }
            EncimaDe(caja);
        });
        Senalador.Suelta += () => Dispatcher.BeginInvoke(() => { try { CollapsedFace?.DejarDeMirar(); } catch { } });
        // LA PASTILLA DEL ID NACE APAGADA (2026-09-14, pedido por el dueño preparando la versión
        // instalable). El localizador SIGUE MIDIENDO desde el primer segundo —de él viven el terreno,
        // el vigilante de clics y media navegación—, lo que ya no hace es enseñarse: «uia://…» es
        // vocabulario de quien depura esto, y a quien solo va a usar la aplicación le aparecía una
        // etiqueta en la esquina de su pantalla sin haber pedido nada. Se enciende con 📍 en el panel
        // (doble Ctrl+Shift), que es donde vive el resto del instrumental.
        _badge = new LocatorBadge();
        _locator = new SurfaceLocator();
        // El vigilante de clics: sin él las aristas del terreno solo dicen que dos pantallas
        // conectan; con él dicen CÓMO pasar de una a otra, que es lo que permite navegar sin
        // haber grabado un workflow. Siempre activo, porque el terreno se aprende viviendo.
        _clickWatcher = new ClickWatcher();
        // EL CLIC HUMANO EN SAP SE NOMBRA POR SU PUERTA (promesa 77): findByPosition dice el
        // componente; si era un árbol, la fila clicada ES la seleccionada (las filas no tienen
        // geometría propia). El nombrado es puro (AtribucionSap); aquí solo se juntan las piezas.
        // EL CLIC HUMANO EN SAP SE NOMBRA SIN COORDENADAS (promesa 77). El plan A era
        // findByPosition y está MUERTO en este SAP GUI 800: un barrido entero de la ventana no
        // resolvió ni un punto (2026-08-30, sondeado por COM). La vía que sí es de SAP: un clic en
        // un árbol CAMBIA SU SELECCIÓN — se compara la selección de cada árbol visible contra la
        // última vista, y el árbol que cambió nombra la fila. Un árbol recién visto solo se
        // apunta (atribuir su selección vieja al primer clic colgaría filas a clics de botones).
        _clickWatcher.ResolverSap = (x, y) =>
        {
            var sap = _locator?.SuperficieSap;
            if (sap == null) return null;
            var id = _locator?.DondeEstoy()?.Id ?? "";
            if (!id.StartsWith("sapgui://", StringComparison.OrdinalIgnoreCase))
            {
                LogBus.Log("clic-sap", $"delante no es SAP («{id}»): que lo intente UIA");
                return null;
            }

            // TRES INTENTOS CON PACIENCIA CRECIENTE: recién cambiada la pantalla, la observación
            // (cada 900 ms, y con árboles tarda) aún no pasó — y unos árboles de OTRA ubicación
            // son rancios, no válidos (ronda 4: los clics de NWP1 se compararon contra los árboles
            // de Easy Access).
            foreach (int espera in new[] { 150, 700, 1400 })
            {
                System.Threading.Thread.Sleep(espera);
                List<(string Id, string Type, string Label, int X, int Y, int W, int H,
                    List<(string Key, string Text, int Top, int Height, bool EsCarpeta)> Filas)> arboles;
                string deDonde;
                lock (_arbolesVistos) { arboles = new(_arbolesVistos); deDonde = _ubicacionDeArboles; }

                // LA REFERENCIA ES LA PANTALLA DEL CLIC, no la de ahora: el clic navega, la
                // ubicación cambia ~600 ms después, y comparar contra «ahora» descartaba los
                // árboles CORRECTOS —los de donde se clicó— y el nombre llegaba 3 s tarde,
                // perdiendo la carrera contra el salto (2026-08-30, ronda 6).
                if (arboles.Count == 0 || !deDonde.Equals(id, StringComparison.OrdinalIgnoreCase))
                {
                    LogBus.Log("clic-sap", $"árboles de «{(deDonde.Length > 0 ? deDonde[(deDonde.LastIndexOf('/') + 1)..] : "nadie")}» y el clic fue en «{id[(id.LastIndexOf('/') + 1)..]}»: espero a la observación");
                    continue;
                }

                foreach (var arbol in arboles)
                {
                    bool dentro = x >= arbol.X && x < arbol.X + arbol.W && y >= arbol.Y && y < arbol.Y + arbol.H;
                    if (!dentro) continue;

                    var fila = Navigation.AtribucionSap.FilaEnElPunto(y - arbol.Y,
                        arbol.Filas.Select(fl => (fl.Key, fl.Text, fl.Top, fl.Height)).ToList());
                    var n = sap.SelectedTreeNode(arbol.Id, out string porqueNo);

                    // LA SELECCIÓN ES LA PALABRA DE SAP — pero durante una navegación el árbol
                    // MUERE y la palabra llega rancia: el doble clic en «NWP1» se nombró
                    // «Favoritos» porque la selección aún era la vieja (ronda 4). Si discrepan,
                    // se relee ASENTADA; y si el árbol ya no contesta es que el clic NAVEGÓ — y
                    // entonces la fila bajo el punto era la verdad.
                    if (n != null && fila is { } fg && n.Value.Key != fg.Key)
                    {
                        System.Threading.Thread.Sleep(350);
                        var n2 = sap.SelectedTreeNode(arbol.Id, out _);
                        if (n2 == null)
                        {
                            LogBus.Log("clic-sap", $"discrepaban y el árbol murió: el clic navegó — manda la geometría «{fg.Text}»");
                            return Nombrar(arbol, (fg.Key, fg.Text));
                        }
                        n = n2;
                    }

                    if (n != null)
                    {
                        bool corrobora = fila is { } fx && fx.Key == n.Value.Key;
                        // El nombre CON CARPETA de la observación, si lo hay: el juez casa por
                        // etiqueta y las puertas ya se llaman así (promesa 81).
                        string textoSel = arbol.Filas.FirstOrDefault(fl => fl.Key == n.Value.Key).Text ?? "";
                        if (string.IsNullOrEmpty(textoSel)) textoSel = n.Value.Text;
                        LogBus.Log("clic-sap", $"clic en el árbol → selección «{textoSel}»"
                            + (corrobora ? " · la geometría corrobora" : ""));
                        return Nombrar(arbol, (n.Value.Key, textoSel));
                    }

                    if (fila is { } fsolo)
                    {
                        // Sin selección que hable: si el árbol tampoco resuelve ya, el clic navegó
                        // y la geometría es lo único y lo suficiente; si el árbol vive pero calla,
                        // mudo antes que arista falsa (la de «Consulta» nació así).
                        bool muerto = porqueNo.Contains("no resuelto", StringComparison.OrdinalIgnoreCase);
                        if (muerto)
                        {
                            LogBus.Log("clic-sap", $"el árbol murió tras el clic: navegó — manda la geometría «{fsolo.Text}»");
                            return Nombrar(arbol, (fsolo.Key, fsolo.Text));
                        }
                        LogBus.Log("clic-sap", $"punto en el árbol pero la selección calla ({porqueNo}): mudo antes que arista falsa");
                        return null;
                    }

                    LogBus.Log("clic-sap", $"punto dentro del árbol pero sin fila ni selección ({porqueNo})");
                    return null;
                }
                LogBus.Log("clic-sap", "el punto no cae en ningún árbol visto: pruebo la rejilla");
                break;
            }

            // LA REJILLA, cuando el árbol no fue (promesa 182): un clic en una fila de ALV —la lista
            // de pacientes— lo nombra su fila, no un árbol. Reusa la lectura del grabador.
            try
            {
                var deRejilla = sap.FilaDeGridEn(x, y);
                if (deRejilla is { } fr)
                {
                    LogBus.Log("clic-sap", $"clic en una fila de rejilla → «{fr.Etiqueta}»");
                    return (fr.Selector, fr.Etiqueta, "GuiGridFila");
                }
            }
            catch (Exception e) { LogBus.Log("clic-sap", $"no pude leer la fila de rejilla: {e.Message}"); }
            // EL CAMPO, cuando no fue ni árbol ni rejilla (2026-09-07): los clics sobre los campos del
            // triage quedaban sin identidad, y el tecleo descargado al parar no tenía de qué colgarse.
            try
            {
                var campo = sap.CampoEn(x, y);
                if (campo is { } c)
                {
                    LogBus.Log("clic-sap", $"clic en un campo → «{c.Etiqueta}» ({c.Tipo})");
                    return (c.Selector, c.Etiqueta, c.Tipo);
                }
            }
            catch (Exception e) { LogBus.Log("clic-sap", $"no pude leer el campo bajo el punto: {e.Message}"); }
            LogBus.Log("clic-sap", "ni árbol, ni rejilla, ni campo: que lo intente UIA");
            return null;

            (string, string, string)? Nombrar(
                (string Id, string Type, string Label, int X, int Y, int W, int H,
                 List<(string Key, string Text, int Top, int Height, bool EsCarpeta)> Filas) arbol,
                (string Key, string Text) elegida)
            {
                bool esCarpeta = arbol.Filas.Any(fl => fl.Key == elegida.Key && fl.EsCarpeta);
                return Navigation.AtribucionSap.NombraElClic(
                    arbol.Id, arbol.Type, arbol.Label, elegida, esCarpeta);
            }
        };
        _clickWatcher.Start();

        Closed += (_, __) =>
        {
            _clickWatcher?.Dispose(); // un hook huérfano ralentiza el ratón de TODA la máquina
        };
        _locator.Changed += loc => Dispatcher.Invoke(() =>
        {
            _badge?.SetText(loc.Id);
            // LOS RECUERDOS SIGUEN A LA PANTALLA. Con la vista encendida, moverse a otro sitio
            // dejaba los carteles del anterior flotando encima: texto de una pantalla sobre otra,
            // que es peor que no enseñar nada (2026-08-24, pedido por el usuario). Es una VISTA de
            // «lo que sé de aquí», no una foto de lo que sabía cuando la encendí.
            if (_recuerdosALaVista) RefrescarRecuerdosALaVista();
        });
        _locator.Start();

        // El rastro del cursor va desde el arranque: cuando alguien dice «ilumina todo esto que te
        // estoy mostrando», ya ha PASADO el ratón por encima. Si se empezara a mirar al oír la
        // frase, lo que se quiere enseñar ya habría ocurrido.
        RastroDelCursor.Arrancar();

        var mcp = new LocalMcp(_uia);
        // El terreno, al alcance del cerebro. Desde la gran limpieza (2026-08-30) las
        // herramientas del mapa hablan SOLO con el núcleo por sus delegados.
        {
            mcp.Map = new SurfaceMapTools(() => _locator?.DondeEstoy());
            // Se guarda para el panel: «ver recuerdos de aquí» pregunta por lo aprendido en esta
            // pantalla, y quien lo sabe es este mismo objeto — no una copia con su propio lector.
            _mapaDeMano = mcp.Map;

            // QUIÉN ELIGE LA PUERTA (spec 035, promesas 284-286). Con U_DECISOR ausente no cambia NADA:
            // map_decidir ni aparece en el catálogo de Luna. Encendido, Luna pide el objetivo y el
            // decisor elige entre las puertas de ahora; ante cualquier duda o fallo, ElDecisor cae a
            // Luna (280). La clave sale del entorno y va a la cabecera: aquí no se lee ni se registra.
            // Y SE PUEDE CAMBIAR EN VIVO (spec 036, promesa 290): el botón «Jev» del panel enciende y apaga
            // por el mismo interruptor; la variable solo fija el estado inicial.
            {
                // LAS CLAVES DE PAGO NO VIAJAN DENTRO DEL .EXE (promesa 300, spec 045): la copia
                // distribuida se las pide a Graph con la credencial que el instalador ya embebe. Se
                // pide SIN esperar: bloquear el arranque en una llamada de red seria pagar el peor
                // caso de la red en cada abrir. Los dos que las usan las piden mas tarde —la voz al
                // abrir sesion, Jev al pulsar el boton— y para entonces ya estan.
                Credenciales.ClavesDelBackend.Viva = Credenciales.ClavesDelBackend.DeGraph(
                    _graphConfig.BaseUrl, _graphConfig.ApiKey, m => LogBus.Log("claves", m));
                _ = Credenciales.ClavesDelBackend.Viva.TraerSiFaltaAlgunaAsync();

                var cfgDecisor = Decision.ConfiguracionDelDecisor.DelSistema();
                LogBus.Log("decisor", cfgDecisor.Porque);
                _interruptorDelDecisor = new Decision.InterruptorDelDecisor(mcp.Map, ReenviarCatalogoALaVozAsync, m => LogBus.Log("decisor", m));
                // EL TRAMO (spec 037): el freno es el de Escape, cada paso va al notch, y la cuenta final entra a la
                // sesión de voz como un mensaje —la llamada de map_tramo ya se contestó al instante—.
                mcp.Map.HayQueParar = () => Actions.Freno.Pidieron;
                mcp.Map.PedirFreno = porque => Actions.Freno.Pide(porque);
                mcp.Map.AlEmpezarTramo = tarea => Actions.Freno.Empezar(tarea);
                mcp.Map.AlTerminarTramo = () => Actions.Freno.Termine();
                mcp.Map.Progreso = linea => Dispatcher.BeginInvoke(() =>
                {
                    _acciones ??= new PanelDeAcciones();
                    if (linea.StartsWith("tramo:", StringComparison.Ordinal)) _acciones.Termina(linea, !linea.Contains("no pud"));
                    else { _acciones.Empieza(linea); SetStatus(linea); }
                });
                mcp.Map.AvisarALaVoz = cuenta =>
                {
                    var vivo = _vivo;
                    if (vivo == null) { LogBus.Log("tramo", "sin sesión de voz: la cuenta queda para map_tramo_estado"); return; }
                    _ = vivo.EnviarTextoAsync("[el tramo terminó] " + cuenta);
                };
                if (cfgDecisor.Quien != "luna") _interruptorDelDecisor.Encender(Credenciales.ClavesDelBackend.DeLaApp);
                PintarBotonJev();
            }

            // CORREGIR UN RECUERDO DESDE SU TARJETA entra por la misma puerta que enseñarlo de viva
            // voz, con sus mismas reglas. Y el narrador se entera de que hay alguien escribiendo,
            // para no pasar al siguiente y borrárselo a media frase.
            TarjetasDeRecuerdo.Guardar = (sel, texto) => mcp.Map.CorregirRecuerdo(sel, texto);
            mcp.Map.TurnoDeContar.EscribiendoAlguien = () => TarjetasDeRecuerdo.EscribiendoAlguna;
            // Y no se pasa al siguiente mientras siga sonando el anterior: quien sabe si queda voz
            // por oír es el altavoz, no el servidor. Ver ElTurnoDeContar.SigueSonando.
            mcp.Map.TurnoDeContar.SigueSonando = () => _vivo?.SigueSonando == true;

            // La voz en vivo usa EXACTAMENTE estas manos, no unas propias. Darle a la conversación
            // hablada su propio camino para actuar habría significado duplicar el ancla de
            // ubicación, la verificación de llegadas y los vetos — y duplicar una protección es la
            // forma más segura de que una de las dos copias se quede atrás.
            _vivo = new ConversacionEnVivo(mcp.Map);
            // «Cállate», «ocúltate», «ciérrate»: van al chrome de la ventana, no al mapa de
            // pantallas — por eso se resuelven aquí y no dentro de SurfaceMapTools.
            _vivo.Autocontrol = AtenderAutocontrol;

            // EL MAPA VIVO: el nodo donde estás rodeado de lo alcanzable, publicado en Neo4j para
            // poder mirarlo mientras ocurre. Lee las MISMAS fuentes que todo lo demás —el mapa y la
            // pantalla— y no el dibujo: un visor que leyera al pintor heredaría sus mentiras, que
            // es justo lo que este visor existe para detectar (2026-08-12, pedido por el usuario).
            _mapaVivo = new Navigation.MapaVivo(
                // `Id` y NO `Origin`: Origin es solo la app —«uia://Maqueta.exe»— así que TODAS las
                // pantallas de una app colapsaban en un único nodo y navegar por dentro no movía
                // nada. La ubicación es la pantalla, que es justo lo que el núcleo llama nodo
                // (2026-08-12, lo vio el usuario al probar la Maqueta).
                () => _locator?.DondeEstoy()?.Id ?? "",
                () =>
                {
                    // CADA MUNDO POR SU PUERTA (promesa 68): dentro de una sesión SAP, UIA ve un
                    // Pane opaco — ahí se mira por la Scripting API. El marco de SAP Logon sigue
                    // siendo uia:// y va por el camino de siempre.
                    var sentido = new Navigation.SentidoPorMundo(
                        uia: () =>
                        {
                            var lector = new Uia.UiaReader();
                            lector.Read();
                            return lector.Elements
                                .Select(e => new Nucleo.Elemento(
                                    Uia.Reconocedor.SelectorDe(e), e.Label, e.ControlType))
                                .Where(el => el.Selector.Length > 0 && el.Etiqueta.Length > 0)
                                .ToList();
                        },
                        sap: () =>
                        {
                            var sap = _locator?.SuperficieSap;
                            if (sap == null) return new List<Nucleo.Elemento>();
                            var vistos = sap.ReadVisibleElements();

                            // LAS FILAS DEL ÁRBOL SON EL CONTENIDO NAVEGABLE (promesa 70). Se piden
                            // solo las VISIBLES y con la altura del propio árbol, que es lo que
                            // decide qué cabe en pantalla.
                            var filas = new Dictionary<string, IReadOnlyList<U.Graph.Surfaces.SapGuiSurface.TreeRow>>();
                            lock (_arbolesVistos) _arbolesVistos.Clear();
                            foreach (var arbol in vistos.Where(v =>
                                         v.SubType.IndexOf("Tree", StringComparison.OrdinalIgnoreCase) >= 0))
                            {
                                var suyas = sap.VisibleTreeRows(arbol.Id, arbol.Height, out string porque);
                                lock (_arbolesVistos)
                                {
                                    _arbolesVistos.Add((arbol.Id, arbol.Type, arbol.Label,
                                        arbol.ScreenLeft, arbol.ScreenTop, arbol.Width, arbol.Height,
                                        suyas.Select(fl => (fl.Key, fl.Ruta.Length > 0 ? fl.Ruta : fl.Text, fl.Top, fl.Height, fl.IsFolder)).ToList()));
                                    _ubicacionDeArboles = _locator?.DondeEstoy()?.Id ?? "";
                                }
                                if (suyas.Count > 0) filas[arbol.Id] = suyas;
                                else LogBus.Log("sentido-sap", $"«{arbol.Label}» no dio filas: {porque}");
                            }
                            // LAS REJILLAS (promesa 78): el panel derecho del Puesto de trabajo.
                            var rejillas = new List<Navigation.SentidoSap.RejillaVista>();
                            foreach (var shell in vistos.Where(v =>
                                         v.SubType.IndexOf("Grid", StringComparison.OrdinalIgnoreCase) >= 0))
                            {
                                var (botones, filasG) = sap.LeerRejilla(shell.Id);
                                if (botones.Count > 0 || filasG.Count > 0)
                                    rejillas.Add(new Navigation.SentidoSap.RejillaVista(shell.Id,
                                        botones.Select(bt => (bt.Id, bt.Texto)).ToList(),
                                        filasG.Select(fl => (fl.Clave, fl.Texto)).ToList()));
                            }
                            return Navigation.SentidoSap.Traducir(vistos, filas, rejillas);
                        });
                    return sentido.Lee(_locator?.DondeEstoy()?.Id ?? "")
                        .Select(el => (el.Selector, el.Etiqueta, el.Tipo))
                        .ToList();
                });
            // El mismo vigilante de clics que ya usa el mapa viejo: sin él, el núcleo aprende dónde
            // está y qué ve, pero nunca QUÉ LE TRAJO — y sin eso el grafo no se arma, se queda en
            // islas sueltas sin caminos entre ellas.
            _mapaVivo.Clics = _clickWatcher;

            // LAS MANOS. El núcleo decide qué pulsar; pulsarlo es del mapeador, y se hace con el
            // mismo UiaSurface que ya usa todo lo demás — no hay un segundo camino de accionar.
            // CADA MUNDO POR SU MANO (promesa 69): el selector decide. Un `sap:…` va por la
            // Scripting API — la única que ve dentro de la sesión—; lo demás, por UIA como siempre.
            // LO QUE LA MANO DECIDE SE VE (promesa 231): cada UiaSurface que crean las manos escribe
            // aquí en qué ventana buscó, qué patrón usó y por qué no pudo.
            U.Graph.Surfaces.UiaSurface.LogGlobal = m => LogBus.Log("mano", m);
            var manoPorMundo = new Navigation.ManoPorMundo(
                uia: (selector, etiqueta) =>
                {
                    try
                    {
                        // EN LA VENTANA DE TRABAJO (promesas 233 y 234): se busca ahí y se pulsa por
                        // patrón si se puede, sin foco ni ratón. La persona sigue en lo suyo.
                        var superficie = new U.Graph.Surfaces.UiaSurface { SoloEnFoco = true };
                        bool ok = superficie.Execute(new U.Graph.PlanStep
                        {
                            StepOrder = 1, ActionType = "click", Selector = selector, Label = etiqueta,
                        }, VentanaObjetivo(), out string error);
                        _ultimoMotivoDeLaMano = ok ? "" : error;
                        return ok;
                    }
                    catch (Exception e)
                    {
                        LogBus.Log("nucleo-http", $"no pude pulsar «{etiqueta}»: {e.Message}");
                        _ultimoMotivoDeLaMano = e.Message;
                        return false;
                    }
                },
                sap: (selector, etiqueta) =>
                {
                    try
                    {
                        var sap = _locator?.SuperficieSap;
                        if (sap == null) return false;
                        // El lápiz escribe sobre lo último pulsado cuando el foco no dice nada:
                        // el campo de comandos vive en la toolbar y SystemFocus no lo rastrea.
                        _ultimoSapPulsado = selector;
                        // «click» es la intención; Execute resuelve por el selector la acción real
                        // (press, seleccionar la fila, el botón de toolbar) — ahí vive ese saber.
                        bool ok = sap.Execute(new U.Graph.PlanStep
                        {
                            StepOrder = 1, ActionType = "click", Selector = selector, Label = etiqueta,
                        }, out string error);
                        if (!ok) LogBus.Log("nucleo-http", $"SAP no pudo pulsar «{etiqueta}»: {error}");
                        _ultimoMotivoDeLaMano = ok ? "" : error;
                        return ok;
                    }
                    catch (Exception e)
                    {
                        LogBus.Log("nucleo-http", $"no pude pulsar «{etiqueta}» en SAP: {e.Message}");
                        return false;
                    }
                });
            _mapaVivo.Pulsar = manoPorMundo.Pulsa;
            _mapaVivo.Arrancar();

            // SITUARSE PASA AL NÚCLEO. Se enchufa aquí y no en el constructor de SurfaceMapTools
            // porque el mapa vivo nace después; hasta entonces la herramienta contesta como
            // siempre. Es la primera de las cinco capacidades que la voz usa de verdad
            // (2026-08-22, medido sobre 26 días de log).
            var aqui = new Navigation.AquiSegunElNucleo(_mapaVivo.Nucleo, DondeTrabajo);
            if (mcp.Map != null) mcp.Map.VentanaDeTrabajo = VentanaObjetivo;
            if (mcp.Map != null) mcp.Map.Situarse = () =>
            {
                // SITUARSE ES SITUAR A Ü (promesa 233), y decir si la persona está en otra parte o si
                // la ventana de trabajo acaba de desaparecer: el modelo decide con eso.
                // Contestar dónde estás NO acciona nada, así que no paga la lectura de la ventana entera
                // que necesita la compuerta antes de pulsar (promesa 246, spec 025).
                var d = _trabajo.Resolver(U.Graph.Surfaces.UiaSurface.VentanaExiste, FocoDeLaPersona);
                string foco = FocoDeLaPersona();
                string nota = d.Aviso.Length > 0 ? $"Ojo: {d.Aviso}. "
                    : _trabajo.Hay && foco.Length > 0 && foco != d.Id ? $"Trabajo en «{d.Id}»; la persona está mirando «{foco}». "
                    : "";
                return nota + aqui.Ahora();
            };

            // SEÑALAR, igual: la lectura de la pantalla se queda en SurfaceMapTools —es UIA— y lo
            // que se CONTESTA sobre lo señalado lo compone el núcleo. Es la capacidad más usada de
            // todas: 168 veces en 26 días.
            var senalar = new Navigation.LoQueSenalas(
                _mapaVivo.Nucleo, () => _locator?.DondeEstoy()?.Id ?? "");
            if (mcp.Map != null) mcp.Map.Senalar = visto => senalar.Con(visto);

            // ABRIR. La vía —programa, pestaña del navegador o SAP— la decide el mapeador, que ya
            // tiene sus promesas; traer al frente de verdad es Win32 y se queda en AppAligner. Lo
            // que se muda es lo que se hacía mal: relanzar lo que ya estaba delante, dar por hecho
            // que lanzar es llegar, y fallar sin decir dónde te deja.
            var abrir = new Navigation.AbrirSegunElNucleo(
                FocoDeLaPersona,   // abrir y traer al frente cambian lo que la persona ve: se mide ahí
                plan => Uia.AppAligner.PonerDelante(plan.Via switch
                {
                    Mapeador.ComoMePongoDelante.Via.PestanaDelNavegador => "web://" + plan.Que,
                    Mapeador.ComoMePongoDelante.Via.SapGui => "sapgui://" + plan.Que,
                    _ => "uia://" + plan.Que + ".exe",
                }),
                Uia.PestanasAbiertas.DominioQueSuena,
                SystemApi.AppsDelSistema.Todas,
                SystemApi.AppsDelSistema.Lanzar,
                // LO QUE YA HAY ABIERTO (promesa 232), y traer UNA ventana concreta: la que se trae
                // pasa a ser la ventana de trabajo, se haya podido subir al frente o no.
                U.Graph.Surfaces.UiaSurface.VentanasAbiertas,
                h =>
                {
                    bool ok = Uia.AppAligner.TraerAlFrente(h);
                    var loc = _locator?.Identificar(h);
                    if (loc != null) { _trabajo.Fijar(h, loc.Id); LogBus.Log("trabajo", $"la ventana de trabajo es ahora «{loc.Id}» (traída)"); }
                    return ok;
                });
            if (mcp.Map != null) mcp.Map.AbrirPorElNucleo = (app, instancia) =>
            {
                string antes = FocoDeLaPersona();
                IntPtr trabajoAntes = _trabajo.Hwnd;
                var previas = new HashSet<IntPtr>(U.Graph.Surfaces.UiaSurface.VentanasAbiertas().Select(v => v.Hwnd));
                string cuenta = abrir.Abrir(app, instancia);
                SeguirElFoco(antes);   // lo recién lanzado es lo que Ü va a operar
                // LANZAR VUELVE ANTES DE QUE EXISTA LA VENTANA (Paint tardó 15 s, Git Bash 7 s el 2026-09-14), y
                // entonces Ü se quedaba sin ventana de trabajo y escribía en la de la persona. Se espera la
                // ventana nueva de ESA app (hasta 8 s) y se fija; si no llega, se dice.
                // SÓLO SI SE LANZÓ ALGO (promesa 262): traer al frente una app que ya estaba abierta no produce ninguna
                // ventana nueva, y esperarla igual costaba los 8 s enteros —la mediana de map_open_app era 10,7 s—.
                if (abrir.Lanzo && _trabajo.Hwnd == trabajoAntes)
                {
                    var crono = System.Diagnostics.Stopwatch.StartNew();
                    var nueva = Navigation.AbrirSegunElNucleo.EsperarVentanaNueva(app, previas, U.Graph.Surfaces.UiaSurface.VentanasAbiertas, 8000);
                    if (nueva.Hwnd != IntPtr.Zero)
                    {
                        Uia.AppAligner.TraerAlFrente(nueva.Hwnd);
                        var loc = _locator?.Identificar(nueva.Hwnd);
                        if (loc != null)
                        {
                            _trabajo.Fijar(nueva.Hwnd, loc.Id);
                            LogBus.Log("trabajo", $"la ventana de trabajo es ahora «{loc.Id}» (lanzada, apareció a los {crono.ElapsedMilliseconds} ms)");
                            cuenta += $" Su ventana ya está: «{nueva.Titulo}». Estás en «{loc.Id}».";
                        }
                    }
                    else LogBus.Log("trabajo", $"«{app}» no mostró ninguna ventana nueva en 8 s");
                }
                return cuenta;
            };

            // LA VENTANITA DEL NÚCLEO, para que el visor pueda pedirle que nos lleve a un sitio sin
            // que nadie toque el núcleo ni el explorador viejo.
            // ESCRIBIR Y ELEGIR, con las MISMAS manos que ya pulsan. No hay un segundo camino de
            // accionar: `UiaSurface.Execute` sabe `input` (ValuePattern.SetValue) y `select`
            // (SelectionItemPattern sobre la opción por su nombre) desde antes que nosotros, y
            // reescribirlo aquí sería tener dos formas de tocar la pantalla que se desincronizarían.
            //
            // Lo único que cambia respecto a pulsar es el verbo. La identidad, el foco y la
            // verificación de que el elemento está vivo son las de siempre.
            Func<string, string, string, bool> accionar = (accion, selector, dato) =>
            {
                try
                {
                    var superficie = new U.Graph.Surfaces.UiaSurface { SoloEnFoco = true };
                    return superficie.Execute(new U.Graph.PlanStep
                    {
                        StepOrder = 1, ActionType = accion, Selector = selector,
                        Value = dato, SelectedValue = accion == "select" ? dato : null,
                    }, out _);
                }
                catch (Exception e)
                {
                    LogBus.Log("nucleo-http", $"no pude {accion} «{dato}»: {e.Message}");
                    return false;
                }
            };

            // LA VOZ EMPIEZA A USAR EL NÚCLEO NUEVO. Solo para lo que el mapa viejo nunca supo
            // alcanzar —web y SAP—: el camino del explorador por voz es rápido y funciona, y se
            // queda entero donde está. `PasoDelNucleo` es la MISMA pieza que usan el visor y la
            // ventanita HTTP, así que las tres puertas contestan lo mismo (2026-08-16).
            mcp.Map.PorElNucleo = destino =>
            {
                string antes = FocoDeLaPersona();
                string cuenta = new Navigation.PasoDelNucleo(
                    _mapaVivo!.Nucleo,
                    DondeTrabajo,
                    (sel, etq) => _mapaVivo?.Pulsar?.Invoke(sel, etq) ?? false,
                    superficie => Uia.AppAligner.PonerDelante(superficie))
                {
                    // La ventana que quedó delante ES ya la de trabajo: si no, «dónde» sigue mirando la anterior (332).
                    AlPonerseDelante = () => SeguirElFoco(antes),
                }.Hasta(destino);
                SeguirElFoco(antes);
                return cuenta;
            };

            // PULSAR, sobre el núcleo. Es la versión mínima de ir —ir no es más que preguntar el
            // siguiente paso y pulsarlo, en bucle— así que va antes y lo demás se apoya en esto.
            // Resolver el selector es de UIA; el GESTO lo decide PulsarSegunElNucleo y esta mano lo
            // EJECUTA (spec 003). Antes aquí decía que la escalada al doble «sigue siendo de UIA»:
            // era falso — murió con el código viejo y ningún batch abría una carpeta del Explorador
            // (clic simple = seleccionar). Un gesto distinto del clic va por UIA con su ActionType;
            // en SAP el gesto se ignora a propósito, porque Execute ya resuelve la acción real por
            // el selector (doubleClickNode en filas, press en botones) y mandarle nuestro «doble»
            // sería una segunda opinión sobre lo mismo.
            var pulsar = Navigation.PulsarSegunElNucleo.ConMotivo(
                _mapaVivo.Nucleo,
                DondeTrabajo,
                (sel, etq, gesto) =>
                {
                    _ultimoMotivoDeLaMano = "";
                    if (gesto.Length == 0 || U.Graph.Surfaces.SapSelector.Owns(sel))
                        return (_mapaVivo?.Pulsar?.Invoke(sel, etq) ?? false) ? null : _ultimoMotivoDeLaMano;
                    try
                    {
                        var superficie = new U.Graph.Surfaces.UiaSurface { SoloEnFoco = true };
                        return superficie.Execute(new U.Graph.PlanStep
                        {
                            StepOrder = 1, ActionType = gesto, Selector = sel, Label = etq,
                        }, VentanaObjetivo(), out string error) ? null : error;
                    }
                    catch (Exception e)
                    {
                        LogBus.Log("nucleo-http", $"no pude pulsar «{etq}» con gesto «{gesto}»: {e.Message}");
                        return e.Message;
                    }
                });
            pulsar.AvisoDeLaVentana = _trabajo.TomarAviso;
            if (mcp.Map != null) mcp.Map.PulsarPorElNucleo = (sel, etq) =>
            {
                string antes = FocoDeLaPersona();
                ObservarLaVentanaDeTrabajo();
                var r = pulsar.Pulsa(sel, etq);
                SeguirElFoco(antes);
                return r.Cuenta;
            };

            // RECORRER EN BATCH: N pasos por llamada con la compuerta de vida antes de cada uno.
            // Usa EL MISMO pulsar de arriba —mismas manos, misma verificación por consecuencia,
            // mismo aprendizaje de aristas— y el freno de siempre: Escape corta la tanda donde va.
            var recorrer = new Navigation.RecorrerSegunElNucleo(
                _mapaVivo.Nucleo,
                DondeTrabajo,
                pulsar,
                // EL LÁPIZ TAMBIÉN DESPACHA POR MUNDO (promesa 71): en SAP el texto va al
                // campo con el foco por la Scripting API; fuera, map_type como siempre.
                escribir: new Navigation.EscribirPorMundo(
                    donde: DondeTrabajo,
                    uia: (campo, texto) => (mcp.Map?.Call("map_type",
                            new Dictionary<string, string> { ["text"] = texto }) ?? "no")
                        .StartsWith("escrib", StringComparison.OrdinalIgnoreCase),
                    sap: (campo, texto) =>
                    {
                        var sap = _locator?.SuperficieSap;
                        if (sap == null) return false;

                        // EL CAMPO PRIMERO, y este orden se invirtió el 2026-09-03. Antes se
                        // escribía siempre al foco y solo se caía al Id «de lo último pulsado» si
                        // eso fallaba: un respaldo que acierta mientras el paso anterior sea justo
                        // ese campo. Cuando el paso TRAE su campo —una skill enseñada siempre lo
                        // trae— preguntar por el foco es tirar el dato bueno y quedarse con la
                        // suposición. El foco sigue siendo el respaldo, que es donde le toca.
                        string id = campo.Length > 0 ? campo : _ultimoSapPulsado;
                        // POR SU NOMBRE (promesa 185, 2026-09-07): el piloto pide «Motivo de Consulta»
                        // o «Y0000000-ZTRNOMPAC», que es lo que ve; un selector sap: entero solo lo
                        // trae la lección. Lo que no sea selector se busca entre los campos de ahora.
                        if (id.Length > 0 && !U.Graph.Surfaces.SapSelector.Owns(id))
                        {
                            string? porNombre = sap.SelectorDelCampo(id);
                            if (porNombre != null) id = porNombre;
                            else LogBus.Log("sentido-sap", $"ningún campo de esta pantalla se llama «{id}» (o hay más de uno): pruebo el foco");
                        }
                        string porque = "";
                        bool ok = false;
                        if (id.Length > 0)
                        {
                            ok = sap.Execute(new U.Graph.PlanStep
                            {
                                StepOrder = 1, ActionType = "input", Selector = id, Value = texto,
                            }, out string error)
                            && string.Equals(sap.ValorActual(id) ?? "", texto,
                                StringComparison.OrdinalIgnoreCase);
                            if (!ok) porque = $"por Id «{id}»: {error}";
                        }
                        // SystemFocus solo rastrea campos del dynpro, y el campo de comandos vive en
                        // la toolbar (2026-08-26, «nada tiene el foco»): por eso hay dos vías.
                        if (!ok)
                        {
                            ok = sap.EscribirEnElFoco(texto, out string porFoco);
                            if (!ok) porque += (porque.Length > 0 ? "; y al foco tampoco: " : "") + porFoco;
                        }
                        if (!ok) LogBus.Log("sentido-sap", $"no pude escribir «{texto}»: {porque}");
                        return ok;
                    }).Escribe,
                hayQueParar: () => Actions.Freno.Pidieron,
                // LA TECLA, POR EL MISMO DESPACHO (promesa 132). En SAP el Enter y las F son
                // comandos del servidor y van por sendVKey aunque el foco esté en otra parte; fuera
                // de SAP no hay servidor a quien mandarle un comando y la tecla va al teclado.
                teclear: new Navigation.TeclearPorMundo(
                    donde: () => _locator?.DondeEstoy()?.Id ?? "",
                    uia: tecla => Actions.InputExecutor.Key(tecla),
                    sap: tecla =>
                    {
                        var sap = _locator?.SuperficieSap;
                        if (sap == null) return false;
                        bool ok = sap.Execute(new U.Graph.PlanStep
                        {
                            StepOrder = 1, ActionType = "key",
                            Selector = "key:" + tecla, Value = tecla,
                        }, out string error);
                        if (!ok) LogBus.Log("sentido-sap", $"no pude pulsar «{tecla}»: {error}");
                        return ok;
                    }).Teclea)
            // Una página web tarda en cargar Y en ser leída (la pantalla se relee cada 900 ms), así
            // que la compuerta espera más que en una app nativa. Sale en cuanto lo ve: una pantalla
            // rápida no paga la espera de una lenta.
            {
                EsperaMaximaMs = 4000,
                // Filas de árbol y de rejilla de SAP: su clave cargada se alcanza por identidad
                // aunque estén desplazadas — seleccionarlas las trae a la vista (promesa 80).
                AccionableAunSinVerse = sel => sel.StartsWith("sap:", StringComparison.OrdinalIgnoreCase)
                    && (sel.Contains("#node=", StringComparison.Ordinal)
                        || sel.Contains("#row=", StringComparison.Ordinal)),
                // LA COMPUERTA MIRA OTRA VEZ ANTES DE RENDIRSE (promesa 264): la ventana de trabajo, ahora, sin el
                // freno de 800 ms de la observación de fondo.
                MiraOtraVez = MirarOtraVezLaVentana,
                Diario = linea => LogBus.Log("compuerta", linea),
            };
            // EL RASTRO (promesa 76): cada relato de batch queda en el anillo que sirve el 8792
            // para la pestaña «Terreno» del visor.
            var rastroDeBatches = new Navigation.RastroDeBatches();
            if (mcp.Map != null) mcp.Map.RecorrerPorElNucleo = pasos =>
            {
                string antes = FocoDeLaPersona();
                ObservarLaVentanaDeTrabajo();
                var r = recorrer.Recorre(pasos);
                rastroDeBatches.Agrega(r.Cuenta);
                SeguirElFoco(antes);
                return r;
            };

            // EL TERRENO POR DELANTE (T3): la consulta de la profundidad, sobre el mismo grafo.
            var terreno = new Navigation.TerrenoPorDelante(_mapaVivo.Nucleo);
            if (mcp.Map != null) mcp.Map.TerrenoPorElNucleo = (puerta, niveles) =>
                terreno.Cuenta(_locator?.DondeEstoy()?.Id ?? "", puerta,
                    int.TryParse(niveles, out int n) ? n : 2);

            // LO QUE SE VA ENSEÑANDO VIVE EN EL GRAFO, colgado del elemento. Estuvo un rato en un
            // archivo aparte con las mismas claves, y el usuario lo vio en cuanto se lo dibujé:
            // «¿es paralelo al grafo?». Lo era, y dos sitios que saben de lo mismo se desincronizan
            // sin avisar. Solo la foto se queda en disco: la ruta va al grafo, el PNG no.
            if (mcp.Map != null)
            {
                mcp.Map.Ensenar = (donde, sel, que, foto) => _mapaVivo.Nucleo.Ensenar(donde, sel, que, foto);
                mcp.Map.RecuerdosAqui = donde => _mapaVivo.Nucleo.RecuerdosDe(donde)
                    .Select(x => (x.Que.Selector, x.Que.Etiqueta, x.Eso.Significado)).ToList();
                // LO VIVO DEL TERRENO, para poder enseñar donde UIA no ve (promesa 117).
                mcp.Map.PuertasVivas = donde => _mapaVivo.Nucleo.DesdeAqui(donde)
                    .Where(a => a.Vivo)
                    .Select(a => (a.Que.Selector, a.Que.Etiqueta, a.Que.Tipo)).ToList();

                // LAS DOS MITADES SAP DEL DESPACHO (promesas 118 y 119): dónde está algo, y qué hay
                // bajo un punto. La decisión de a quién preguntar vive en MundoQueToca; aquí solo se
                // le pasa la mano de SAP, igual que con observar, pulsar y escribir.
                // EL GRABADOR DE SAP ENSEÑA AL TERRENO (promesa 189): el último paso publicado y la
                // pantalla nueva que SAP anuncia después son una arista, con el selector con el que el
                // terreno conoce la puerta. Es lo que el mapa vivo no alcanza cuando SAP tarda.
                Teach.PasoQueVioSap? ultimoPasoQueVioSap = null;
                var relojDeSap = System.Diagnostics.Stopwatch.StartNew();
                _teachSapSurface.StepObserved += (_, paso) =>
                    ultimoPasoQueVioSap = new Teach.PasoQueVioSap(relojDeSap.ElapsedMilliseconds, paso.Surface ?? "", paso.Selector ?? "");
                _teachSapSurface.PantallaNueva += (desde, hasta) =>
                {
                    var cruce = Teach.ElCruceQueVioSap.Emparejar(ultimoPasoQueVioSap, new Teach.CambioQueVioSap(relojDeSap.ElapsedMilliseconds, desde, hasta));
                    if (cruce is not { } c) return;
                    bool aprendida = _mapaVivo.Nucleo.Cruzar(c.Desde, c.Selector, c.Hasta);
                    LogBus.Log("terreno", aprendida
                        ? $"el grabador de SAP enseñó la arista: «{c.Selector[(c.Selector.LastIndexOf('/') + 1)..]}» lleva de …{c.Desde[(c.Desde.LastIndexOf('/') + 1)..]} a …{c.Hasta[(c.Hasta.LastIndexOf('/') + 1)..]}"
                        : $"el grabador de SAP vio cruzar «{c.Selector}» desde {c.Desde}, pero el terreno no conoce esa puerta ahí: no se aprende");
                };
                // LAS MANOS DEL PILOTO DAN EL PASO CON LA MISMA COREOGRAFÍA QUE EL PLAN (promesa 191).
                mcp.Map.DarUnPasoConCoreografia = DarUnPasoConCoreografia;
                mcp.Map.CajasEnSap = LeerCajasDeSap;
                // LOS CAMPOS DEL DYNPRO, para nombrarlos (promesa 188). Solo dentro de SAP: fuera, vacío.
                mcp.Map.CamposDeSap = () =>
                {
                    var sap = _locator?.SuperficieSap;
                    string donde = _locator?.DondeEstoy()?.Id ?? "";
                    if (sap == null || !donde.StartsWith("sapgui://", StringComparison.OrdinalIgnoreCase))
                        return Array.Empty<U.Graph.DetectedField>();
                    try { return sap.ReadFields(); } catch { return Array.Empty<U.Graph.DetectedField>(); }
                };
                // SEÑALAR DENTRO DE SAP, con el hit-test que SÍ funciona. La nativa
                // «FindByPosition» no resuelve nada en este SAP —medido el 2026-07-26 y escrito en
                // windows-graph/CLAUDE.md: devuelve null en árboles Y en botones—, así que el punto
                // se resuelve con la MISMA geometría que ya se lee para iluminar, y lo elige el
                // MISMO elector que en UIA: el más pequeño que contiene el punto (promesas 34-35).
                // Un mundo aporta candidatos; quién gana se decide en un solo sitio.
                mcp.Map.SenaladoEnSap = (x, y) =>
                {
                    var punto = new System.Windows.Point(x, y);
                    var cajas = LeerCajasDeSap();
                    var elegido = Navigation.LoQueSenalas.Elegir(
                        cajas.Select(c => new Navigation.LoQueSenalas.Candidato(
                            c.Etiqueta.Length > 0 ? c.Etiqueta : c.Selector, c.Tipo, c.Caja, c.Selector)),
                        punto);
                    if (elegido is not { } e)
                    {
                        // POR QUÉ NO SE ENCONTRÓ, y no solo que no se encontró: si las cajas están
                        // en otra escala que el cursor (DPI), esto lo canta a la primera.
                        var muestra = cajas.FirstOrDefault();
                        LogBus.Log("recuerdo", $"nada bajo ({x},{y}) entre {cajas.Count} caja(s) de SAP"
                            + (cajas.Count > 0 ? $" · ejemplo: «{muestra.Etiqueta}» en {muestra.Caja}" : ""));
                        return null;
                    }
                    return (e.Selector, e.Nombre, e.Tipo, e.Caja);
                };
                // Entra por `Recordar` y no por `Observar`: observar significa «esto es lo que hay
                // en pantalla» y REEMPLAZA la lista viva entera, así que presentar un panel suelto
                // borraría de un plumazo todas las puertas de esta ubicación.
                mcp.Map.Presentar = (donde, sel, etq, tipo) =>
                    _mapaVivo.Nucleo.Recordar(donde, new[] { new Nucleo.Elemento(sel, etq, tipo) });
            }

            // EL QUINTO SITIO (spec 025, 2026-09-17): el servidor del núcleo —y con él PasoDelNucleo, el camino de
            // map_go_to— recibía el localizador CRUDO, que busca la barra de direcciones en todo el árbol UIA en
            // cada llamada. Sus bucles lo llamaban hasta ochenta veces seguidas. Se recuerda 400 ms, como ya hace
            // DondeTrabajo: dentro de ese instante preguntar otra vez no toca la pantalla.
            var dondeParaElNucleo = new Navigation.MemoriaCorta<string>(400);
            _servidorNucleo = new Navigation.ServidorDelNucleo(
                _mapaVivo.Nucleo,
                () => dondeParaElNucleo.Pide(() => _locator?.DondeEstoy()?.Id ?? ""),
                (sel, etq) => _mapaVivo?.Pulsar?.Invoke(sel, etq) ?? false,
                superficie => Uia.AppAligner.PonerDelante(superficie),
                (sel, texto) => accionar("input", sel, texto),
                (sel, opcion) => accionar("select", sel, opcion));
            _servidorNucleo.Rastro = rastroDeBatches;
            _servidorNucleo.Arrancar();

            // El consumo de la voz en vivo se reporta a Graph al cerrar la sesión.
            // Hace falta porque este WebSocket va DIRECTO a Google: Graph no ve la
            // conversación, así que si el cliente no lo cuenta, ese gasto no
            // aparece en el panel de costos aunque Google lo facture igual.
            //
            // Solo van cifras. Quién es el operador NO se manda: Graph lo resuelve
            // contra la key y el correo con los que ya viene autenticada la
            // petición, así que este equipo no puede atribuirle su gasto a otro.
            _vivo.ReportaConsumo = async parte =>
            {
                if (_backend == null) return;
                await _backend.PostAsync<object>("/agent/usage", new
                {
                    provider = "google",
                    feature = "live_voice",
                    model = parte.Modelo,
                    inputTokens = parte.Entrada,
                    outputTokens = parte.Salida,
                    totalTokens = parte.Total,
                    turns = parte.Turnos,
                    durationMs = parte.DuracionMs,
                    sessionId = parte.Sesion,
                    clientVersion = System.Reflection.Assembly.GetExecutingAssembly()
                        .GetName().Version?.ToString() ?? ""
                }, CancellationToken.None);
            };
            // BeginInvoke, no Invoke: quien avisa a la carita es el mismo hilo que está ejecutando
            // la acción sobre la pantalla, y `Invoke` lo deja esperando a que la interfaz le
            // conteste. Con eso, `map_where_am_i` no llegaba ni a empezar —el modelo la pedía, se
            // quedaba colgada y a los 20 s llegaba «toolCallCancellation»— y desde fuera parecía que
            // el modelo no hacía nada (2026-08-04). Contar lo que haces no puede costarte hacerlo.
            _vivo.Dice += t => Dispatcher.BeginInvoke(() => { AppendChat(t); SetStatus(t); });
            _vivo.Cerro += () => Dispatcher.BeginInvoke(() => _turnoAbierto = false);
            // La maquinaria va a su propio panel y NO a la burbuja: la burbuja reemplaza, así que un
            // «abriendo Descargas…» borraba la última frase de la conversación, y además solo dejaba
            // ver el último paso. En el panel se acumulan y se ve la secuencia entera.
            // LA CONVERSACIÓN, EN EL MISMO PANEL QUE LA MAQUINARIA. Para saber si te entendió había
            // que mirar a dos sitios —la burbuja y este panel— y la burbuja REEMPLAZA, así que lo
            // que dijiste hace dos frases ya no estaba. Cuando algo no funciona, la primera pregunta
            // es «¿me oyó bien?» (2026-08-16, pedido por el usuario).
            _vivo.Transcribe += (texto, esDeU) => Dispatcher.BeginInvoke(() =>
            {
                _acciones ??= new PanelDeAcciones();
                _acciones.Habla(texto, esDeU);
            });
            _vivo.TurnoCerrado += () => Dispatcher.BeginInvoke(() => _acciones?.CierraTurno());
            _vivo.Accion += (texto, listo) => Dispatcher.BeginInvoke(() =>
            {
                _acciones ??= new PanelDeAcciones();
                if (listo) _acciones.Termina(texto, !texto.StartsWith("✋"));
                else { _acciones.Empieza(texto); SetStatus(texto); }
            });
            _vivo.Cambio += viva => Dispatcher.Invoke(() =>
            {
                // HABLAR POR VOZ NO ABRE EL CHAT (petición del dueño, 2026-09-05: «se me abre un
                // chat que es superestorboso»). Abrir la conversación por voz es justo el momento
                // en que NO hace falta leer nada: quien habla está mirando su trabajo, no el globo.
                // Que está escuchando ya lo dicen la cara y la pastilla de voz, que se encienden
                // solas — el globo era una tercera señal encima del trabajo de alguien.
                //
                // El globo NO desaparece: sigue abriéndose donde hay algo que leer (una pregunta,
                // una narración, un fallo) y por el atajo de escribirle. Lo que se quita es que la
                // voz lo abra por su cuenta.
                // La boca la mueve el audio EN VIVO, que no pasa por VoiceIO: sin esto el gesto
                // quedaba dibujado y sin nadie que lo moviera (2026-08-05).
                ActualizarBoca();
                // Y el halo. Al colgar se apaga solo: lee _vivo.Viva y no depende de dónde esté el
                // ratón —con el botón del collar no hay ratón de por medio—. Con las pastillas, quien
                // lo apagaba era apartar el ratón, y por eso se quedaba encendido para siempre.
                PintarHalo();
            });
            // El halo repinta AL MOMENTO en que cambia el origen, y no sólo cuando Ü habla: el
            // temporizador de la boca vive únicamente mientras Ü está hablando, así que encender la
            // voz con el botón del collar se quedaba pintado en gris para siempre si Ü no llegaba a
            // decir palabra (2026-08-14, visto por el usuario con el audio ya entrando por el collar).
            _vivo.FuenteCambio += () => Dispatcher.BeginInvoke(PintarHalo);

            Closed += (_, __) => _vivo?.Dispose();
        }

        // EL SERVIDOR MCP DE VERDAD (F2 del plan de batch): la puerta por la que el Agent SDK —o
        // cualquier cliente MCP genérico— conduce el terreno. El catálogo es EL MISMO de la voz,
        // filtrado a lo que el mapa despacha, más map_batch (que la voz aún no usa; F4 unifica);
        // el despacho es el MISMO LocalMcp: mismas manos, mismos vetos, mismo freno.
        var catalogoMcp = Voice.ConversacionEnVivo.Herramientas()
            .Where(u => SurfaceMapTools.IsMapTool(u.Nombre))
            .Append(new Voz.Realtime.Utensilio("map_batch",
                "RECORRE VARIOS PASOS DE UNA SOLA LLAMADA sobre el mapa del computador, con una "
                + "compuerta antes de cada paso: solo se pulsa lo que está VIVO en pantalla ahora. "
                + "Llega tan lejos como el terreno deje; al primer paso no-vivo PARA y te dice N de "
                + "M, dónde quedó, por qué, y qué SÍ está vivo ahí — con eso replanificas sin gastar "
                + "otra llamada. Es tu RUTA PREDILECTA para navegar: una llamada en vez de una por "
                + "clic. Cada paso verificado deja su tramo aprendido en el mapa.",
                new[] { new Voz.Realtime.Argumento("pasos",
                    "Lista JSON de pasos, en orden. Cada paso: {\"exit\":\"...\"} para cruzar, o "
                    + "{\"text\":\"...\"} para escribir en el campo con foco. En exit puedes poner "
                    + "el NOMBRE de la puerta tal como se ve, su selector, O EL NOMBRE DEL DESTINO "
                    + "al que quieres llegar («Portal:Ajedrez», «Descargas») — si el mapa ya "
                    + "aprendió qué puerta lleva ahí, la usa solo. Ejemplo: "
                    + "[{\"exit\":\"Recibidos\"},{\"exit\":\"Correo de Jerónimo\"}]") }))
            .Append(new Voz.Realtime.Utensilio("map_ahead",
                "MIRA EL TERRENO POR DELANTE sin tocar nada: qué habrá tras una puerta, según lo "
                + "que el mapa aprendió al cruzarla otras veces. Úsala ANTES de map_batch para "
                + "planificar varios pasos de una vez: te dice a qué pantalla lleva cada puerta "
                + "cruzada y qué recuerda allí. Lo nunca cruzado se anuncia «por descubrir» — ahí "
                + "no hay promesa, solo se aprende yendo. La predicción es memoria: el batch "
                + "igualmente verifica que cada cosa esté viva antes de pulsarla.",
                new[]
                {
                    new Voz.Realtime.Argumento("exit",
                        "La puerta que te interesa (su nombre o su selector). Vacío = el panorama: "
                        + "todas las puertas cruzadas desde aquí y a dónde llevan."),
                    new Voz.Realtime.Argumento("levels",
                        "Cuántas pantallas hacia delante (1-3, por defecto 2)."),
                }))
            // LAS TAREAS ENSEÑADAS, anunciadas al cerebro (spec 009). Sin catálogo no se puede
            // pedir «haz lo que te enseñé»: el cerebro no puede pedir lo que no se anuncia.
            .Append(new Voz.Realtime.Utensilio("map_skills",
                "QUÉ TAREAS TE HAN ENSEÑADO en este computador, y cuáles están listas para usar. "
                + "Cada una dice CUÁNDO usarla. Pregúntalo antes de resolver algo a mano: si ya te "
                + "enseñaron a hacerlo, reproducirlo es más rápido y más seguro que improvisarlo. "
                + "Las que salgan como PENDIENTE de comprobar todavía no se pueden ejecutar.",
                Array.Empty<Voz.Realtime.Argumento>()))
            .Append(new Voz.Realtime.Utensilio("map_skill_run",
                "HAZ UNA TAREA QUE TE ENSEÑARON, con los datos de ahora. Se reproduce por el MISMO "
                + "batch que todo lo demás: compuerta antes de cada paso, verificación por "
                + "consecuencia y cuenta honesta. Los valores que se tecleaeron durante la "
                + "demostración NUNCA se repiten — son huecos, y solo se llenan con los datos que "
                + "le pases. Si la tarea termina en una puerta que no se puede deshacer (Grabar, "
                + "Finalizar), se detiene ahí y te la deja a ti.",
                new[]
                {
                    new Voz.Realtime.Argumento("nombre",
                        "Cuál de las tareas enseñadas. Pídelas con map_skills."),
                    new Voz.Realtime.Argumento("datos",
                        "JSON con los datos de esta corrida, con los NOMBRES que map_skills lista en "
                        + "«necesita»: {\"Peso\":\"68\",\"Talla\":\"170\"}. Lo que no pases queda en "
                        + "blanco y se te dice; lo que no tenga hueco también."),
                }))
            // LAS DEL PILOTO (spec 013): la voz de Ü, la pregunta a la persona, la llegada que juzga
            // la app y la skill de lo verificado. Viven en el catálogo MCP porque el piloto es un
            // cliente MCP más; la voz en vivo no las ve porque su catálogo se arma aparte.
            .Append(new Voz.Realtime.Utensilio("voz_decir",
                "DI ESTO EN VOZ ALTA con la voz de Ü, a la persona que está delante. Corto: una o dos frases.",
                new[] { new Voz.Realtime.Argumento("texto", "Lo que hay que decir, tal cual.") }))
            .Append(new Voz.Realtime.Utensilio("voz_preguntar",
                "PREGÚNTALE ALGO A LA PERSONA y ESPERA su respuesta hablada (hasta un minuto). Úsalo solo "
                + "cuando la duda cambie lo que vas a hacer; lo que puedas resolver mirando, míralo.",
                new[] { new Voz.Realtime.Argumento("texto", "La pregunta, corta y concreta.") }))
            .Append(new Voz.Realtime.Utensilio("leccion_llegue",
                "DECLARA QUE ACABAS DE HACER EL EVENTO N DE LA LECCIÓN. La app compara dónde estás ahora con "
                + "dónde llegó la demo en ese evento y te contesta si aterrizaste. No es opcional: sin esto "
                + "el paso no cuenta.",
                new[] { new Voz.Realtime.Argumento("n", "El número del evento, tal como viene en la lección.") }))
            // LOS OJOS (promesa 181): map_shot ya existía y devolvía la foto como texto, así que el
            // modelo nunca la miraba. Ahora viaja como imagen; el catálogo lo dice para que se use.
            .Append(new Voz.Realtime.Utensilio("map_shot",
                "MIRA LA PANTALLA: te devuelve una FOTO de la ventana del usuario, que puedes ver. Úsala "
                + "cuando lo que hiciste NO cambia de pantalla y por tanto nadie más puede confirmártelo: "
                + "seleccionar una fila, marcar una casilla, escribir en un campo. Mira, comprueba si "
                + "salió, y si no salió corrígelo. Es más barato equivocarse mirando que declararlo a ciegas.",
                Array.Empty<Voz.Realtime.Argumento>()))
            .Append(new Voz.Realtime.Utensilio("leccion_plan",
                "ENTREGA EL PLAN de la lección y la app lo RECORRE por ti: por cada paso dice tu «decir» en voz, "
                + "cuelga tu «recuerdo» en el elemento estando en su pantalla, da el paso por el ejecutor de "
                + "siempre y juzga la llegada contra la lección. Si un paso no se puede dar, PARA ahí y te "
                + "devuelve dónde quedó: entonces sigues tú con las manos desde ese paso. Es la forma barata y "
                + "rápida de comprobar; úsala primero.",
                new[]
                {
                    new Voz.Realtime.Argumento("pasos",
                        "Lista JSON, en orden: [{\"n\":1,\"exit\":\"comando\",\"text\":\"nwp1\",\"tecla\":\"enter\","
                        + "\"recuerdo\":\"qué es y para qué sirve\",\"decir\":\"frase corta\"}, …]. «n» es el evento "
                        + "de la lección que cumple; «exit» la PUERTA por su nombre; «text»/«tecla» si escribe."),
                }))
            .Append(new Voz.Realtime.Utensilio("leccion_guardar_skill",
                "GUARDA LA SKILL con lo que se VERIFICÓ en esta comprobación: solo entran los pasos que "
                + "aterrizaron. Llámala al final, una vez.",
                new[]
                {
                    new Voz.Realtime.Argumento("nombre", "Nombre corto de la tarea, en español (p. ej. «Abrir triage de un paciente»)."),
                    new Voz.Realtime.Argumento("descripcion", "Cuándo usar esta skill, en una frase."),
                }))
            .ToList();
        _nombresMcp = catalogoMcp.Select(u => u.Nombre).ToList();
        _servidorMcp = new ServidorMcp(new ProtocoloMcp(catalogoMcp, (tool, args) => mcp.Call(tool, args)));
        _servidorMcp.Start();
        Closed += (_, __) => _servidorMcp?.Dispose();
        // El backend es Graph: la credencial (X-API-Key) sale del MISMO GraphConfig que usa la
        // ventana de workflows — una sola fuente de key para toda la app.
        _backend = new BackendClient(_config, _graphConfig);
        if (_vivo != null)
            _vivo.Memoria = new MemoriaPersonal(_backend, _config.UserId);
        // "Windows Live": registra al usuario y empieza a emitir telemetría (pulsos consciente/
        // subconsciente + logs) al backend. No-op si el usuario no dio su correo.
        InitTelemetry();
        Closed += (_, __) => TelemetryBus.Shutdown();

        // ESC PARA LO QUE Ü ESTÉ HACIENDO. Va aquí y no en GlobalHotkeys porque aquel REGISTRA las
        // teclas —se las quita al resto del sistema— y Escape no se le puede quitar a nadie: es la
        // tecla de «déjame en paz» de todas las apps. Actions.Freno solo la mira pasar y la deja
        // seguir su camino. Ver Actions/Freno.cs.
        Actions.Freno.Escuchar();
        // ESCAPE TAMBIÉN APAGA LO ILUMINADO, corra algo o no. Un recuadro encendido sobre la
        // pantalla de alguien es algo de lo que hay que poder salir, y ahí no hay nada que «parar»:
        // por eso cuelga de la tecla (SePulso) y no del alto (Pidio), que a propósito se desentiende
        // cuando no hay tarea (promesa 21).
        Actions.Freno.SePulso += () => Dispatcher.BeginInvoke(() =>
        {
            // ESCAPE TAMBIÉN CALLA A Ü EN VIVO (2026-08-31): «detener es detener, también la voz»
            // —lo decía ya el botón de parar—. Es LA interrupción determinista mientras el AEC
            // real no exista: en este hardware la energía no distingue tu voz del eco (medido).
            if (_vivo?.Interrumpir() == true) SetStatus("Te escucho.");

            // Se anota SOLO si de verdad había algo encendido: Escape se pulsa cien veces al día
            // para cerrar diálogos ajenos, y un log por cada una sería ruido que se aprende a
            // ignorar — y el log que se ignora no sirve el día que hace falta.
            // LA VISTA DE RECUERDOS SE APAGA AUNQUE NO HAYA NADA ENCENDIDO, y va antes del corte de
            // abajo: es un MODO que sigue a la pantalla, así que en un sitio sin recuerdos está
            // puesta y vacía. Si dependiera de que hubiera algo iluminado, Escape no podría salir
            // justo de esos sitios — y el botón seguiría diciendo «clic para apagar».
            if (_recuerdosALaVista)
            {
                LogBus.Log("recuerdo", "Escape apaga la vista de recuerdos");
                try { _iluminacion?.HideRect(); } catch { }
                MarcarRecuerdosALaVista(false);
            }

            // LAS TARJETAS SE VAN SIEMPRE CON ESCAPE, esté la vista puesta o no: también las
            // enseña la narración por voz, y ahí no hay ningún botón que pulsar para quitarlas.
            try { TarjetasDeRecuerdo.Cerrar(); } catch { }

            // Se anota SOLO si de verdad había algo encendido: Escape se pulsa cien veces al día
            // para cerrar diálogos ajenos, y un log por cada una sería ruido.
            if (Senalador.Actual == null) return;
            LogBus.Log("señalar", $"Escape apaga lo iluminado («{Senalador.Actual?.Que}»)");
            try { Senalador.Soltar(); } catch { }
        });
        // Y SE DICE. Pararse en silencio se vive igual que colgarse, y son cosas opuestas: en una te
        // obedeció y en la otra te dejó tirado. La frase vive en Freno.DevuelvoElControl para que la
        // carita y la voz digan lo MISMO (2026-08-22, pedido por el usuario).
        Actions.Freno.Dice += frase => Dispatcher.BeginInvoke(() =>
        {
            try { SetStatus(frase); } catch { }
        });
        // La primera vez, que se presente ella. No hace nada en los arranques siguientes.
        OfrecerElPrimerEncuentro();
        // La superficie actual viaja en cada turno (scoping de workflows) y las llamadas
        // workflow_* del cerebro se ejecutan con el WorkflowPlayer (subconsciente).
        _workflowRunner = new WorkflowMcpRunner(_graphConfig, this);

        // EL RELLENADOR DE SAP. Se arma aquí porque necesita la configuración de Graph y la
        // superficie de SAP. Hasta el 2026-09-02 lo alimentaba también un dictado clínico propio
        // —un fonendoscopio al lado de la carita, con su propio micrófono— que no usaba nadie y se
        // retiró con las pastillas (spec 008, promesa 112). El dictado en vivo sigue existiendo
        // donde sí se usa: la consulta (ConsultaWindow, spec 004).
        _rellenador = new RellenadorSap(_graphConfig, _clinicalSap);
        // LO ENSEÑADO LLEGA A LA HORA DE ESCRIBIR (promesa 115). Se pregunta por la MISMA ubicación
        // que usa la enseñanza —la del locator— para que las dos puntas del canal usen la misma
        // llave; y por la misma identidad, que el grafo escribe con «sap:» y el formulario sin él.
        _rellenador.RecuerdoDe = selector =>
        {
            string donde = _locator?.DondeEstoy()?.Id ?? "";
            if (donde.Length == 0 || _mapaDeMano?.RecuerdosAqui == null) return "";
            string busco = Clinical.LoQueVeElEmparejador.MismaIdentidad(selector);
            foreach (var r in _mapaDeMano.RecuerdosAqui(donde))
                if (Clinical.LoQueVeElEmparejador.MismaIdentidad(r.Selector)
                        .Equals(busco, StringComparison.OrdinalIgnoreCase))
                    return r.Significado;
            return "";
        };
        // EL ✓ DE LA CONSULTA LLEGA POR AQUÍ (spec 008): la ventana de consulta nace antes que la
        // carita y no ve las manos; se le cuelga esta función y ella la llama al pulsar ✓.
        Clinical.PuenteASap.Enviar = EnviarEncargoAsync;
        // Y EL DE LOS APRENDIZAJES (promesa 225): el panel de la consulta pide «muéstrame esto» y
        // las manos están aquí. Sin colgarlo, el botón «Mostrar» sale gris y dice por qué.
        Clinical.PuenteDeAprendizajes.Mostrar = MostrarAprendizajeAsync;
        _rellenador.Cuenta += m => Dispatcher.Invoke(() => SetStatus("🩺 " + m));

        // EL EJECUTOR DE EXPORTACIONES. Pregunta al backend si el médico pulsó «Exportar a HC» y,
        // cuando lo hizo, navega y llena la historia clínica. Va encendido desde el arranque y sin
        // botón: el operador no tiene que acordarse de activarlo para que su compañero pueda
        // exportar desde la web. Sin trabajo no hace nada más que una petición cada tres segundos.
        _exportador = new EjecutorDeExportaciones(_graphConfig, _rellenador, Dispatcher);
        _exportador.Cuenta += m => Dispatcher.Invoke(() => { SetStatus(m); ShowTalk(MotivoDelGlobo.SoloEsProgreso); });
        _exportador.Arrancar();
        Closed += (_, __) => _exportador?.Dispose();
        // La superficie se PREGUNTA, igual que en el camino del mapa. Aquí se quedó el valor
        // cacheado —que se refresca cada 800 ms— porque este código es anterior a que existiera
        // Ahora(), y nadie volvió a mirarlo: el consciente decidía su siguiente paso sobre dónde
        // estaba hasta casi un segundo antes, justo cuando retoma una tarea que acaba de moverse
        // (2026-08-04). Dos caminos que responden «¿dónde estoy?» de forma distinta no es una
        // optimización pendiente: es que uno de los dos está equivocado.
        _loop = new AgentLoop(_backend, _uia, mcp, this, this, InstalledApps.List,
            () => _locator?.DondeEstoy(), _workflowRunner);

        // LA BARRA YA NO SE ARRASTRA, y no es un olvido. Arrastrarla por cualquier zona libre movía
        // ESTA ventana con un DragMove; desde la spec 010 la barra vive en el muelle, contra el borde
        // derecho, así que ese handler habría movido la ventana de la carita desde un clic dado en
        // otra ventana — un gesto que mueve algo que no estás tocando. La carita conserva los suyos
        // (ver WireFaceGestures), que es donde el arrastre significa algo.

        // El menú extendido: hover/clic/teclado sobre el activador, cierre con retraso, Backend
        // plegado. Toda la coreografía vive en la región «menú extendido» de abajo.
        WireMenu();

        // La carita colapsada SIGUE al cursor automatizado durante la ejecución de workflows: se ve
        // "quién" está haciendo los clics. Evento estático de UiaSurface; se suelta al cerrar.
        UiaSurface.CursorMoved += OnAutomationCursorMoved;
        Closed += (_, __) => UiaSurface.CursorMoved -= OnAutomationCursorMoved;
        UiaSurface.Pulso += OnManoPulso;
        Closed += (_, __) => UiaSurface.Pulso -= OnManoPulso;

        // La voz ya dice cuándo está escuchando y cuándo hablando (antes no lo decía nadie y la UI lo
        // simulaba escribiendo «Escuchando…» y cruzando los dedos). Llega desde el hilo del motor de
        // voz, así que RefreshMood marshalea por dentro.
        _voice.ActivityChanged += (_, __) => RefreshMood();
        Closed += (_, __) => _voice.Dispose();

        LoadSounds();
        WireFaceGestures();
        ApplyTheme(Enum.TryParse(_config.FaceTheme, out FaceTheme t) ? t : FaceTheme.Light);
        Face.StartIdle();          // gestos casuales: parpadeo, mirada, pulso
        CollapsedFace.StartIdle();

        // SE ARRANCA EN LA CARITA, no en la barra. Abrir la aplicación desplegaba las siete
        // herramientas de golpe sobre el trabajo de alguien que no ha pedido ninguna todavía: lo
        // primero que se ve tiene que ser lo que representa a Ü, y lo demás llegar cuando se pida
        // (2026-08-05).
        //
        // Y DESDE LA SPEC 010 LA BARRA NI SIQUIERA ESTÁ AQUÍ: se muda entera al muelle, contra el
        // borde derecho, donde siempre se le puede encontrar. Esta ventana se queda con lo que de
        // verdad flota — la carita — y ya no alterna entre dos estados.
        MudarElPanelAlMuelle();

        StartUpdater();

        // Cierres de grabación que quedaron a medias por un 504 de Graph: se reintentan al arrancar, en
        // segundo plano y sin molestar. Va ANTES de recargar la lista para que, si alguno sale, el
        // workflow ya aparezca con su resumen puesto.
        _ = RetryPendingFinishesAsync();

        // Precarga la lista para el selector directo del panel Backend (silencioso si Graph no está listo).
        _ = ReloadDirectWorkflowsAsync();
    }

    /// <summary>Completa en segundo plano los cierres que un 504 dejó pendientes. Nunca interrumpe.</summary>
    private async Task RetryPendingFinishesAsync()
    {
        if (!_graphConfig.IsConfigured) return;
        try
        {
            int done = await PendingFinish.RetryAllAsync(new GraphClient(_graphConfig), CancellationToken.None);
            if (done > 0)
            {
                SetStatus($"Se completó el resumen de {done} grabación(es) que habían quedado a medias.");
                await ReloadDirectWorkflowsAsync();
            }
        }
        catch (Exception e) { LogBus.Log("teach", $"reintento de cierres pendientes falló: {e.Message}"); }
    }

    /// <summary>
    /// Asegura la identidad del usuario: genera el InstallId (una vez) y, si aún no hay correo, muestra
    /// el popup de bienvenida para capturar nombre+correo. El correo es la clave canónica en el backend.
    /// Si el usuario cierra el popup sin completarlo, se seguirá sin telemetría y se re-preguntará en el
    /// próximo arranque — nunca bloquea el uso del asistente.
    /// </summary>
    private void EnsureOnboarded()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_config.InstallId))
            {
                _config.InstallId = Guid.NewGuid().ToString("N");
                _config.Save();
            }
            // ¿SABE YA ALGUIEN QUIÉN ES? Se mira la sesión de Supabase ANTES de preguntar: si la
            // Dra. Rincón acaba de entrar en la ventana de consulta, plantarle encima el popup de
            // «Te damos la bienvenida» es no haber mirado (promesa 98, 2026-09-01).
            //
            // La sesión se restaura de disco aquí mismo, sin red: es leer un archivo cifrado. La
            // carita no se queda esperando a nadie.
            var sesion = new Cuenta.SesionMiracle(Cuenta.Nube.SupabaseUrl, Cuenta.Nube.ClavePublicable);
            bool hayMedico = sesion.Restaurar();

            if (hayMedico)
            {
                // El correo que manda pasa a ser el del token. La identidad de máquina no se borra
                // —los workflows y la telemetría la usan desde antes—, pero deja de ser la que
                // decide quién es esta persona.
                string correo = Cuenta.Identidad.CorreoQueMandaEnLaMaquina(sesion.MedicoEmail, _config.Email);
                if (!string.IsNullOrWhiteSpace(correo) && correo != _config.Email)
                {
                    _config.Email = correo;
                    _config.UserId = correo;
                    if (sesion.MedicoNombre.Length > 0) _config.DisplayName = sesion.MedicoNombre;
                    _config.Save();
                    LogBus.Log("onboarding", $"identidad tomada de la sesión del médico · {sesion.MedicoId}");
                }
            }

            if (!Cuenta.Identidad.HayQuePreguntar(hayMedico, _config.Email))
            {
                LogBus.Log("onboarding", hayMedico
                    ? "no pregunto quién eres: ya hay un médico con sesión iniciada"
                    : "no pregunto quién eres: ya había correo en este equipo");
                return;
            }

            var win = new OnboardingWindow { Owner = this };
            if (win.ShowDialog() == true && !string.IsNullOrWhiteSpace(win.EnteredEmail))
            {
                _config.DisplayName = win.EnteredName;
                _config.Email = win.EnteredEmail;
                _config.UserId = win.EnteredEmail; // el scoping de workflows y la telemetría hablan del mismo usuario
                _config.Save();
            }
        }
        catch (Exception ex) { LogBus.Log("onboarding", ex.Message); }
    }

    /// <summary>Arranca la telemetría de "Windows Live" con la identidad actual (no-op sin correo).</summary>
    private void InitTelemetry()
    {
        if (_backend == null || !_config.Onboarded) return;
        var identity = new TelemetryIdentity
        {
            Email = _config.Email,
            InstallId = _config.InstallId,
            DisplayName = _config.DisplayName,
            AppId = _graphConfig.AppId,
            AppVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "",
            MachineName = Environment.MachineName,
            OsVersion = Environment.OSVersion.VersionString
        };
        TelemetryBus.Init(_backend, identity);
        // Después de Init y no antes: el espejo escribe una línea al encenderse, y sin cliente
        // levantado esa primera línea se perdería y no se sabría si quedó reflejando o no.
        Telemetry.EspejoDelLog.Encender();
    }

    // --- Auto-actualización ---

    /// <summary>
    /// Arranca el sondeo de versiones nuevas. El usuario no toca nada: si aparece una, se descarga en
    /// segundo plano y recién ahí asoma la pastilla. Ver <see cref="Updater"/>.
    /// </summary>
    private void StartUpdater()
    {
        _updater = new Updater(_config.UpdateFeedUrl);
        VersionText.Text = $"Versión {_updater.CurrentVersion}";
        // UpdateReady llega desde un hilo del pool, no del Dispatcher: tocar la UI directo reventaría.
        // QUÉ versión es ya no se dice al pasar el ratón (promesa 164): el ⬇ dice que hay algo nuevo
        // y VersionText, dentro del panel, dice cuál — que es donde se lee sin tener que descubrirlo.
        _updater.UpdateReady += info => Dispatcher.Invoke(() =>
        {
            _mensajeDeActualizacion = info.Message;
            ShowUpdate(true);
            SetStatus($"Hay una actualización lista: {info.Version}.");
        });
        _updater.Start();
    }

    private void OnApplyUpdate(object sender, RoutedEventArgs e)
    {
        _ = AplicarActualizacionConNarrativaAsync();
    }

    /// <summary>Flujo visible y hablado: morado mientras se instala, mensaje del release y reinicio.</summary>
    private async Task AplicarActualizacionConNarrativaAsync()
    {
        if (_actualizando || _updater == null) return;
        _actualizando = true;
        PintarHalo();
        try
        {
            if (_updater.ReadyInfo == null)
            {
                SetStatus("Buscando una actualización…");
                ShowTalk(MotivoDelGlobo.SoloEsProgreso);
                var resultado = await _updater.BuscarAhoraAsync();
                if (resultado.Que is not (Updater.Busqueda.Descargada or Updater.Busqueda.YaEstabaLista))
                {
                    _actualizando = false;
                    PintarHalo();
                    Speak(resultado.Que == Updater.Busqueda.AlDia
                        ? "Ya estoy al día. No hay una actualización nueva para instalar."
                        : $"No pude actualizarme: {resultado.Detalle}.");
                    return;
                }
            }

            _mensajeDeActualizacion = _updater.ReadyInfo?.Message ?? _mensajeDeActualizacion;
            string mensaje = _mensajeDeActualizacion?.Speech
                ?? "Traigo mejoras para que nuestra experiencia sea más útil y confiable.";
            Speak($"Encontré una actualización. Esto es lo que trae: {mensaje} En un momento vuelvo.");
            SetStatus("Actualizando Ü…");
            await Task.Delay(TimeSpan.FromSeconds(2.2));
            _updater.ApplyAndRestart(); // no retorna: el instalador relanza el proceso
        }
        catch (Exception ex)
        {
            _actualizando = false;
            PintarHalo();
            LogBus.Log("update", $"actualización pedida por voz falló: {ex.Message}");
            Speak("No pude completar la actualización, pero sigo aquí. Puedes intentarlo de nuevo.");
        }
    }

    /// <summary>
    /// «Buscar actualizaciones» pulsado a mano. SIEMPRE contesta algo — incluso «ya estás al día».
    /// </summary>
    /// <remarks>
    /// Un botón que no responde se pulsa tres veces. El sondeo automático puede permitirse el
    /// silencio porque nadie lo está mirando; éste no: alguien acaba de pulsarlo y está esperando.
    /// Por eso cada rama de <see cref="Updater.Busqueda"/> tiene su frase, incluida la de «esta
    /// copia no se instaló con el instalador», que es la que explica por qué en desarrollo no pasa
    /// nada por más que se insista.
    /// </remarks>
    private async void OnCheckUpdate(object sender, RoutedEventArgs e)
    {
        if (_updater == null) { SetStatus("El actualizador no está disponible."); ShowTalk(MotivoDelGlobo.AlgoFallo); return; }

        CheckUpdateBtn.IsEnabled = false;
        SetStatus("Buscando actualizaciones…");
        ShowTalk(MotivoDelGlobo.SoloEsProgreso);
        try
        {
            var (que, detalle) = await _updater.BuscarAhoraAsync();
            SetStatus(que switch
            {
                Updater.Busqueda.AlDia => $"Ya tienes la última versión ({detalle}).",
                Updater.Busqueda.Descargada => $"Versión {detalle} descargada. Pulsa ⬇ para escuchar qué trae y reiniciar.",
                Updater.Busqueda.YaEstabaLista => $"La versión {detalle} ya estaba lista. Pulsa ⬇ para reiniciar.",
                Updater.Busqueda.NoAplica => $"No se puede actualizar: {detalle}.",
                _ => $"No pude comprobarlo: {detalle}",
            });
        }
        finally { CheckUpdateBtn.IsEnabled = true; }
    }

    /// <summary>
    /// Si el usuario nunca tocó la pastilla, la versión descargada se instala al cerrar: el próximo
    /// arranque ya es la nueva, sin que él haya hecho nada.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        _updater?.ApplyOnExit();
        base.OnClosed(e);
    }

    // ── Atajos globales: llamar a Ü sin soltar SAP ────────────────────────────────────────────

    private readonly GlobalHotkeys _hotkeys = new();
    private IntPtr _prevForeground;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>El HWND existe a partir de aquí: es el momento de registrar las combinaciones.</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hotkeys.Attach(this, InvocarPorAtajo, MicPorAtajo);
        Closed += (_, __) => _hotkeys.Dispose();
        // El atajo que quedó ACTIVO —no el que se pretendía— lo dice HotkeyStatus, dentro del panel.
        // Hasta el 2026-09-06 también asomaba al pasar el ratón sobre el activador del menú; eso se
        // fue con los carteles (promesa 164), y el sitio que queda es el que se lee sin descubrirlo.
        if (_hotkeys.Resumen.Length > 0) HotkeyStatus.Text = _hotkeys.Resumen;
    }

    /// <summary>
    /// Trae a Ü al frente con el cursor ya dentro de la caja de texto. No mueve la ventana a
    /// propósito: el usuario la dejó donde la dejó, y reubicarla pisaría la posición que se persiste.
    /// </summary>
    private void InvocarPorAtajo()
    {
        _prevForeground = GetForegroundWindow();   // para poder devolver el teclado con Esc
        _muelle?.Desplegar("atajo: escribirle a Ü");
        Show();
        _muelle?.Show();
        _muelle?.Activate();
        ShowTalk(MotivoDelGlobo.LoPidioAlguien, focusInput: true);   // el mismo camino que ya usa AskAsync; no se duplica el foco
        if (!IsActive)
            // Si esto sale en el registro de la máquina del hospital, hará falta el rodeo de
            // SetForegroundWindow + AttachThreadInput. No se implementa por adelantado: que la
            // necesidad la demuestre el log y no una teoría.
            LogBus.Log("atajo", "Activate() no trajo la ventana al frente");
    }

    private void MicPorAtajo()
    {
        _prevForeground = GetForegroundWindow();
        SacandoLaCaritaDelAnfitrion();
        Show();
        Activate();
        OnMic(this, new RoutedEventArgs());
    }

    /// <summary>
    /// Devuelve el teclado a donde estaba. Sin esto, el operador que invoca a Ü y se arrepiente tiene
    /// que buscar SAP con el ratón — justo lo que el atajo venía a evitar.
    /// </summary>
    private void DevolverElFoco()
    {
        if (_prevForeground == IntPtr.Zero) return;
        try { SetForegroundWindow(_prevForeground); } catch { }
        _prevForeground = IntPtr.Zero;
    }

    /// <summary>
    /// Mantiene fija la esquina inferior derecha: si la ventana cambia de tamaño (Expander abierto,
    /// o colapsar a solo la carita) crece/encoge hacia arriba y hacia la izquierda, sin mover esa
    /// esquina. Anclar por el delta (en vez de a la esquina del escritorio) conserva la posición si
    /// el usuario la arrastró a otro sitio.
    /// </summary>
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // SI ESTÁ VIAJANDO, LA POSICIÓN ES SUYA. Todo lo de aquí abajo asigna Left/Top, y en WPF eso
        // cancela la animación en curso: la carita llegaba al destino de un salto en vez de volando.
        // Y los cambios de tamaño caen justo mientras viaja —al soltarla se reordena la barra, al
        // señalar algo aparece texto en el globo—, así que la animación se moría en el primer
        // cuadro. El destino del viaje ya viene acotado a la pantalla por quien lo lanzó, así que no
        // hay nada que corregir aquí (2026-08-06).
        if (Vuelo.EnCurso) return;

        // El borde clavado es el del lado donde vive la barra: a la derecha, el menú y el globo crecen
        // hacia la izquierda y hay que compensar; a la izquierda crecen hacia la derecha y no hay nada
        // que compensar, porque el borde izquierdo ya está donde tiene que estar.
        if (!_barLeft && e.PreviousSize.Width > 0) Left += e.PreviousSize.Width - e.NewSize.Width;

        // El vertical depende de hacia dónde crece el menú. Con el menú hacia arriba se ancla el
        // borde inferior (la barra no se mueve y el panel sube). Con el menú hacia abajo hay que
        // anclar el SUPERIOR: mantener el inferior empujaría la barra hacia arriba, que es peor que
        // el problema que la apertura hacia abajo venía a resolver.
        if (!_menuDown && e.PreviousSize.Height > 0) Top += e.PreviousSize.Height - e.NewSize.Height;

        // Nunca dejar la ventana fuera del área de trabajo (p.ej. al crecer cerca de un borde).
        var wa = SystemParameters.WorkArea;
        MoveTo(Math.Clamp(Left, wa.Left, Math.Max(wa.Left, wa.Right - e.NewSize.Width)),
               Math.Clamp(Top, wa.Top, Math.Max(wa.Top, wa.Bottom - e.NewSize.Height)));

        // Si la barra cambió de zona de la pantalla, el chevron en reposo puede estar prometiendo una
        // dirección que ya no es la que va a tomar el menú.
        if (!_menuOpen) RefreshRestingChevron();
    }

    /// <summary>
    /// Recalcula hacia dónde apuntaría el chevron con el menú cerrado. Barato, y evita que la flecha
    /// mienta sobre dónde va a nacer el menú.
    /// </summary>
    private void RefreshRestingChevron()
    {
        if (_menuOpen) return;
        bool down = ShouldOpenDown();
        if (down == _menuDown) return;
        ApplyMenuDirection(down);
        UpdateChevron();
    }

    /// <summary>
    /// Coloca la ventana donde el usuario la dejó, o en la esquina inferior derecha si no hay nada
    /// guardado. Es idempotente a propósito: se llama al cargar y otra vez cuando el tamaño ya es el
    /// definitivo (ver <see cref="OnLoaded"/>).
    ///
    /// <c>ActualWidth</c> y no <c>Width</c>: con <c>SizeToContent</c>, <c>Width</c> es <c>NaN</c>.
    /// </summary>
    private void ColocarVentana()
    {
        // El tamaño tiene que ser el definitivo ANTES de medir con él: con SizeToContent, un
        // ActualWidth a medio asentar coloca la ventana a un puñado de píxeles de donde debía.
        UpdateLayout();

        var wa = SystemParameters.WorkArea;
        bool mismaPantalla =
            _config.SavedWorkAreaWidth is double sw && Math.Abs(sw - wa.Width) < 1 &&
            _config.SavedWorkAreaHeight is double sh && Math.Abs(sh - wa.Height) < 1;

        double top;
        double left;

        if (_config.WindowLeft is double savedL && _config.WindowTop is double savedT && mismaPantalla)
        {
            left = savedL;
            top = savedT;
        }
        else
        {
            // Si la pantalla cambió, se DESCARTA y se dice. Remapear proporcionalmente sería adivinar
            // dónde la habría querido el usuario en una pantalla que nunca ha visto.
            if (_config.WindowLeft != null && !mismaPantalla && !_avisóDelDescarte)
            {
                _avisóDelDescarte = true;
                LogBus.Log("ui", $"posición guardada descartada: el área de trabajo es ahora "
                                + $"{wa.Width:0}x{wa.Height:0} y se guardó en "
                                + $"{_config.SavedWorkAreaWidth:0}x{_config.SavedWorkAreaHeight:0}");
            }
            left = wa.Right;                        // el borde: lo pega el ajuste de abajo
            top = wa.Bottom - ActualHeight - 24;
        }

        // Al arrancar también se pega a un lado, pase lo que pase. Dos motivos: un config viejo puede
        // traer una posición de en medio (el margen de 24 px del sitio por defecto era justo eso), y
        // una posición guardada en una sesión anterior puede haber quedado descolgada del borde al
        // cambiar el tamaño de la barra. Si la regla es «Ü vive en un lado», el arranque la cumple.
        double centro = left + ActualWidth / 2;
        left = centro >= (wa.Left + wa.Right) / 2
            ? Math.Max(wa.Left, wa.Right - ActualWidth)
            : wa.Left;

        top = Math.Clamp(top, wa.Top, Math.Max(wa.Top, wa.Bottom - ActualHeight));
        MoveTo(left, top);

        // Aserción viva: colocar la ventana depende de tres cosas que cambian solas (el tamaño ya
        // asentado, el área de trabajo y lo guardado). Cuando alguna falla, el síntoma es «aparece
        // en un sitio raro», que no se distingue de nada. Esto lo hace legible en el registro.
        LogBus.Log("ui", $"colocada en ({left:0},{top:0}) · tamaño {ActualWidth:0}x{ActualHeight:0} · "
                       + $"guardado=({_config.WindowLeft:0},{_config.WindowTop:0}) "
                       + $"mismaPantalla={mismaPantalla} · área {wa.Width:0}x{wa.Height:0}");
    }

    private bool _avisóDelDescarte;

    /// <summary>
    /// Mueve la ventana DE VERDAD. Hay que pasar por aquí siempre.
    ///
    /// Lanzar la carita a un borde deja una animación sobre <c>Left</c>/<c>Top</c> con
    /// <c>FillBehavior.HoldEnd</c> (ver <see cref="FaceGestures.FlingToEdge"/>), y mientras esa
    /// animación está retenida WPF IGNORA cualquier asignación directa a esas propiedades. El
    /// síntoma es desconcertante porque no falla nada: simplemente la ventana deja de obedecer.
    ///
    /// Eso ya rompía dos cosas antes de que existiera este helper —la carita colapsada dejaba de
    /// seguir al cursor automatizado después de un lanzamiento, y el reanclaje de tamaño no
    /// reanclaba— y ninguna de las dos daba error. Tres sitios con la misma clase de fallo.
    /// </summary>
    private void MoveTo(double left, double top,
        [System.Runtime.CompilerServices.CallerMemberName] string quien = "")
    {
        if (Vuelo.EnCurso)
            LogBus.Log("ui-anim", $"«{quien}» CLAVA la ventana en ({left:0},{top:0}) durante un vuelo");
        Vuelo.Termina();   // ponerla a mano cancela el viaje: ya no hay nada que respetar
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        Left = left;
        Top = top;
    }

    /// <summary>
    /// Mueve la ventana CON EL MISMO MUELLE con el que se mueve cuando la lanzas.
    ///
    /// Cuando la carita va sola —a ponerse junto a lo que está señalando, o encima del catálogo de
    /// apps— aparecía de golpe en el destino, porque <see cref="MoveTo"/> asigna y punto. Un objeto
    /// que se teletransporta no parece el mismo objeto: parece que se apagó aquí y se encendió allá,
    /// y se pierde de vista a dónde fue. Moverse es lo que deja seguirla con la mirada, y eso
    /// importa justo aquí, que es cuando está enseñando algo (2026-08-05).
    ///
    /// Arranca parada (pendiente 0) porque nadie la ha empujado: el impulso solo existe cuando hay
    /// una mano detrás.
    /// </summary>
    private void MoverConMuelle(double left, double top)
    {
        double dx = left - Left, dy = top - Top;
        double dist = Math.Sqrt(dx * dx + dy * dy);

        // Un salto corto se resuelve moviendo y ya: animar 12 px es un parpadeo, no un movimiento.
        if (dist < 24) { MoveTo(left, top); return; }

        // El MISMO motor que el lanzamiento: mover la ventana con física se contesta en un solo
        // sitio. Arranca parada (pendiente 0) porque nadie la ha empujado.
        var dur = TimeSpan.FromMilliseconds(Math.Clamp(260 + dist * 0.45, 260, 720));
        Vuelo.Mover(this, left, top, dur,
                    new MuelleEase { InitialSlope = 0 }, new MuelleEase { InitialSlope = 0 });
    }

    /// <summary>
    /// EL VIAJE AL CLIC (promesa 240): como <see cref="MoverConMuelle"/> pero con la curva y los
    /// tiempos de <see cref="ComoViajaLaCarita"/> —más corta, y sin el rebote del lanzamiento—.
    ///
    /// No se reutiliza el muelle de lanzar porque esto pasa en CADA clic: 720 ms con rebote está bien
    /// para un gesto de la persona, y encadenado veinte veces en un plan se lee como gelatina.
    /// </summary>
    private void ViajarAlClic(double left, double top)
    {
        double dx = left - Left, dy = top - Top;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        if (!ComoViajaLaCarita.MereceViaje(dist)) { MoveTo(left, top); return; }
        var dur = ComoViajaLaCarita.Cuanto(dist);
        // Se anota porque es lo único que hace medible el viaje sin mirar la pantalla: en el log se lee
        // de dónde salió, a dónde fue y cuánto tardó.
        LogBus.Log("ui-anim", $"viaje al clic: ({Left:0},{Top:0}) → ({left:0},{top:0}) · {dist:0} px en {dur.TotalMilliseconds:0} ms");
        Vuelo.Mover(this, left, top, dur, new CurvaDelClic(), new CurvaDelClic());
    }

    // --- Recordar dónde dejó el usuario la barra ---
    //
    // Solo se guardan los movimientos que el usuario QUISO: soltar un arrastre y asentar un gesto de
    // la carita. Los otros dos orígenes se ignoran a propósito:
    //   · OnSizeChanged mueve la ventana cada vez que se abre el menú o el globo — eso es layout, no
    //     intención, y guardarlo desplazaría la barra un poco en cada arranque.
    //   · OnAutomationCursorMoved la mueve DECENAS DE VECES POR SEGUNDO mientras corre un workflow;
    //     persistir eso dejaría la barra en un punto aleatorio de SAP el próximo arranque.

    private System.Windows.Threading.DispatcherTimer? _saveTimer;

    /// <param name="left">Destino final. En un lanzamiento NO se puede leer <c>Left</c>: vale lo de
    /// antes hasta que termine la animación.</param>
    private void SavePositionSoon(double left, double top)
    {
        var wa = SystemParameters.WorkArea;
        _config.WindowLeft = left;
        _config.WindowTop = top;
        _config.SavedWorkAreaWidth = wa.Width;
        _config.SavedWorkAreaHeight = wa.Height;
        LogBus.Log("ui", $"posición anotada: ({left:0},{top:0})");

        // Escribir a disco en cada gesto es barato pero innecesario; el debounce fusiona el par
        // «asentar + lanzar» en una sola escritura.
        if (_saveTimer == null)
        {
            _saveTimer = new System.Windows.Threading.DispatcherTimer(
                System.Windows.Threading.DispatcherPriority.Background)
            { Interval = TimeSpan.FromMilliseconds(700) };
            _saveTimer.Tick += (_, __) => { _saveTimer!.Stop(); _config.Save(); };
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    // --- La zona contextual de la barra (⬇ ⏹ 🔄) ---
    //
    // Estos botones NO se muestran/ocultan a mano desde los sitios que los provocan. Pasan por aquí
    // por dos razones: la zona entera (con su separador) tiene que colapsarse cuando no queda ninguno
    // visible, y porque cinco `X.Visibility = …` sueltos por el archivo son cinco oportunidades de
    // que aparezca un sexto y se olvide.

    private void ShowStop(bool on)
    {
        StopBtn.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        UpdateContextZone();
    }

    private void ShowUpdate(bool on)
    {
        UpdateBtn.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        UpdateContextZone();
    }

    /// <summary>La zona vive solo mientras haya algo dentro; si no, se lleva su separador con ella.</summary>
    private void UpdateContextZone() =>
        ContextZone.Visibility =
            UpdateBtn.Visibility == Visibility.Visible ||
            StopBtn.Visibility == Visibility.Visible ||
            RestartTeachBtn.Visibility == Visibility.Visible
                ? Visibility.Visible : Visibility.Collapsed;

    // --- La carita sigue al cursor automatizado (solo colapsada) ---

    private long _lastFollowMs;

    /// <summary>
    /// Mueve la carita colapsada junto al cursor automatizado, con un offset para no tapar el objetivo
    /// del clic. Throttle a ~30ms para no inundar el Dispatcher (el cursor emite frame a frame). Las
    /// coordenadas llegan en píxeles físicos; WPF posiciona en DIPs → se divide por la escala de DPI.
    /// </summary>
    private void OnAutomationCursorMoved(int x, int y)
    {
        long now = Environment.TickCount64;
        if (now - _lastFollowMs < 30) return;
        _lastFollowMs = now;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_collapsed) return;
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
            double px = x / dpi.DpiScaleX + 18, py = y / dpi.DpiScaleY + 18;
            var wa = SystemParameters.WorkArea;
            // Por MoveTo y no por asignación directa: si el usuario lanzó la carita a un borde, la
            // animación retenida se traga los Left/Top y la carita se queda plantada sin seguir a nadie.
            MoveTo(Math.Clamp(px, wa.Left, Math.Max(wa.Left, wa.Right - ActualWidth)),
                   Math.Clamp(py, wa.Top, Math.Max(wa.Top, wa.Bottom - ActualHeight)));
        }));
    }

    // --- Colapsar / expandir: la carita alterna entre la barra y solo ella misma ---

    /// <summary>El muelle está plegado, o sea: el panel no se ve. Lo mantiene el propio muelle.</summary>
    private bool _collapsed = true;

    private Muelle? _muelle;

    /// <summary>
    /// EL PANEL SE MUDA AL MUELLE, VIVO (spec 010, 2026-09-05).
    /// </summary>
    /// <remarks>
    /// Dos líneas y no un refactor, y la diferencia importa: <c>RootPanel</c> arrastra ~35 elementos
    /// con nombre que maneja este mismo archivo, 4.695 líneas. Reparentar en vez de copiar mantiene
    /// todo eso funcionando SIN TOCARLO, porque los handlers que el XAML cableó
    /// (<c>Click="OnDemoPuntaAPunta"</c>) se compilan contra ESTA instancia y no contra el padre
    /// visual. La ventana cambia; el dueño del panel sigue siendo FaceWindow.
    /// </remarks>
    /// <summary>
    /// EL PANEL SE VISTE CON EL ESTUDIO, y no con hexadecimales sueltos en el XAML.
    /// </summary>
    /// <remarks>
    /// El panel nació oscuro (<c>#F20F131C</c>) cuando era una tira de iconos flotando sobre el
    /// escritorio. Desde que vive en el muelle es una superficie que se lee, no un adorno, y el
    /// dueño pidió que siguiera el mismo manual que la ventana de la consulta clínica: fondo claro,
    /// lo elevado blanco, y lo que los separa no es el color sino la SOMBRA
    /// (<see cref="Estudio"/>, rediseño del 2026-09-01).
    ///
    /// SE PINTA DESDE CÓDIGO A PROPÓSITO. Copiar los hex al XAML habría dejado la paleta en dos
    /// sitios, y dos sitios para un mismo hecho se desincronizan siempre — es el aprendizaje nº16,
    /// que en este repo ya costó cuatro comparaciones falsas en un solo día. Si mañana cambia
    /// <c>Estudio.Superficie</c>, cambia esto sin que nadie se acuerde de venir.
    /// </remarks>
    private void VestirElPanelConElEstudio()
    {
        // El panel: un ESTADIO, como el diseño que dio el dueño. El radio es la mitad del ancho, y
        // por eso se calcula en vez de escribirse: con un número a mano, cambiar el ancho dejaría
        // las puntas ovaladas sin que nadie entendiera por qué.
        var estadio = new CornerRadius(BarPanel.Width / 2);
        BarPanel.CornerRadius = estadio;
        // NO el blanco puro ni el filete de la casa: la barra flota sobre el escritorio de otro y
        // ahí los dos desaparecen. Ver Estudio.SuperficieDeLaBarra, donde está el porqué entero.
        BarPanel.Background = Estudio.SuperficieDeLaBarra;
        BarPanel.BorderBrush = Estudio.BordeDeLaBarra;

        // La placa: gemela, sin un solo hijo, y es la única que lleva el Effect. Ver el comentario
        // del XAML y Estudio.Elevar — con la sombra puesta en el panel, cada letra de dentro caería
        // en la textura del shader y saldría lavada.
        BarPlaca.CornerRadius = estadio;
        // La placa va del MISMO color que el panel: es su gemela y lo que se ve de ella es la sombra,
        // pero un color distinto asomaría por el filo en cuanto el redondeo no encajara al píxel.
        BarPlaca.Background = Estudio.SuperficieDeLaBarra;
        BarPlaca.Effect = Estudio.Sombra3;

        // Y EL HUECO PARA QUE ESA SOMBRA QUEPA. El muelle es SizeToContent sobre una ventana
        // transparente: mide exactamente lo que mide el panel, así que sin reservar sitio el
        // desenfoque se queda al otro lado del cristal y la sombra sale cortada contra el borde
        // (2026-09-06, lo vio el dueño). Estos dos no pasan por Estudio.Elevar —la sombra está
        // puesta a mano sobre una placa que declara el XAML— así que se pide la misma cuenta.
        BarShell.Margin = Estudio.HolguraDe(Estudio.Sombra3);

        foreach (var b in new[] { LearnBtn, WorkBtn })
        {
            b.Template = Estudio.Pastilla(b.Height / 2);
            b.Background = Estudio.Superficie;
            b.BorderBrush = Estudio.Borde;
            b.BorderThickness = new Thickness(1);
            b.Foreground = Estudio.Tinta;
            b.FontSize = 14.5;
            b.FontWeight = FontWeights.Medium;
            b.Cursor = System.Windows.Input.Cursors.Hand;
            b.ConRelieve();   // sube con el ratón encima y baja al pulsarlo: la única animación
        }

        // Lo secundario se lee como secundario: los iconos que nacieron blancos sobre negro serían
        // invisibles sobre blanco, así que se les da la tinta del estudio uno a uno. No es un
        // barrido genérico: cada uno dibuja con su propio pincel y un barrido dejaría alguno fuera.
        // EL FONDO, UNO A UNO Y POR LO QUE SIGNIFICA CADA BOTÓN. El estilo BarBtn los pinta con
        // blanco al 10 % (#1AFFFFFF): sobre el negro de antes era una pastilla tenue, sobre el
        // blanco de ahora es NADA. Pero la respuesta no es la misma para todos, y ponerles a todos
        // el mismo gris —lo que hice el 2026-09-05— borró el color de dos que SÍ lo tenían:
        // «hay versión nueva» era azul y «detener» era rojo, y los dejé grises a los dos. El color
        // ahí no es adorno: es lo único que distingue un botón que informa de uno que interrumpe.
        UpdateBtn.Background = Estudio.AcentoSuave;   // hay algo nuevo
        StopBtn.Background = Estudio.AlertaSuave;     // esto para lo que está pasando
        UpdateBtn.Foreground = Estudio.Acento;
        StopBtn.Foreground = Estudio.Alerta;

        // Y LOS NEUTROS, SIN FONDO NINGUNO (2026-09-06, lo pidió el dueño mirando el del collar:
        // «sin contorno, el ícono directo al fondo blanco»). Una pastilla gris permanente alrededor
        // de un icono que no está pasando nada es ruido: sobre una superficie blanca el icono ya se
        // lee solo. El realce aparece al acercar la mano, que es cuando dice algo.
        // Y LAS CUATRO HERRAMIENTAS DEL MENÚ (🔍 📍 🧠 📜) con ellos, que se habían quedado fuera:
        // sobre el blanco del estudio, el #1AFFFFFF que les pone BarBtn no es una pastilla tenue, es
        // nada — y desde que 📍 nace APAGADO, un interruptor cuyo estado de reposo no se ve es un
        // interruptor que no se encuentra.
        foreach (var b in new System.Windows.Controls.Primitives.ButtonBase[]
                 { RestartTeachBtn, ComprobarBtn, MenuActivator, CollarModoBtn,
                   InspectorBtn, LocatorBtn, RecuerdosBtn, LogsBtn })
        {
            b.Foreground = Estudio.Tinta;
            b.Background = System.Windows.Media.Brushes.Transparent;
        }

        ActivatorChevron.Stroke = Estudio.TintaMedia;
        foreach (var trazo in ((Canvas)CollarModoBtn.Content).Children)
        {
            if (trazo is System.Windows.Shapes.Path camino) camino.Stroke = Estudio.Tinta;
            if (trazo is System.Windows.Shapes.Ellipse punto) punto.Fill = Estudio.Tinta;
        }

        SepContexto.Background = Estudio.Borde;
        SepBarra.Background = Estudio.Borde;

        // El globo de conversación y la píldora de estado, del mismo estudio: eran las dos únicas
        // superficies que quedaban oscuras, y una interfaz con dos temas a la vez no se lee como
        // dos temas, se lee como un fallo.
        TalkPanel.Background = Estudio.Superficie;
        TalkPanel.BorderBrush = Estudio.Borde;
        TalkPanel.CornerRadius = new CornerRadius(Estudio.RadioPanel);
        TalkPanel.Effect = Estudio.Sombra3;
        var aireDelGlobo = Estudio.HolguraDe(Estudio.Sombra3);
        // Por la derecha ya había 8 de separación con la barra; se conserva el mayor de los dos en
        // vez de sumarlos, o el globo se despegaría el doble de lo que nadie pidió.
        TalkPanel.Margin = new Thickness(aireDelGlobo.Left, aireDelGlobo.Top,
            Math.Max(aireDelGlobo.Right, TalkPanel.Margin.Right), aireDelGlobo.Bottom);
        Status.Foreground = Estudio.Tinta;
        Bubble.Foreground = Estudio.TintaMedia;
        Input.Background = Estudio.SuperficieSuave;
        Input.Foreground = Estudio.Tinta;
        Input.CaretBrush = Estudio.Tinta;
        TalkCloseBtn.Background = Estudio.SuperficieSuave;
        TalkCloseBtn.Foreground = Estudio.TintaMedia;

        StatusChip.Background = Estudio.Superficie;
        StatusChip.BorderBrush = Estudio.Borde;
        StatusChip.CornerRadius = new CornerRadius(Estudio.RadioChico);
        StatusChipText.Foreground = Estudio.Tinta;
    }

    private void MudarElPanelAlMuelle()
    {
        var raiz = (Grid)Content;
        raiz.Children.Remove(RootPanel);
        RootPanel.Visibility = Visibility.Visible;   // dentro del muelle, quien lo esconde es él
        VestirElPanelConElEstudio();

        // La barra vive SIEMPRE a la derecha ahora, así que el espejo de lados se aplica una vez y
        // deja de depender de dónde ande la carita.
        ApplyBarSide(false);

        // La carita, en cambio, ya no alterna: flota siempre.
        CollapsedGroup.Visibility = Visibility.Visible;

        // EL GLOBO ABIERTO LO MANTIENE DESPLEGADO, LA VOZ NO. Con la voz contando, el panel se
        // quedaba abierto toda la conversación — y desde que hablar ya no abre el chat, eso sería
        // exactamente el estorbo que el dueño pidió quitar (2026-09-05). Lo que no se puede cerrar
        // por debajo es lo que estás LEYENDO o ESCRIBIENDO; hablar no ocupa la pantalla.
        _muelle = new Muelle(RootPanel, () => _talkOpen) { Hueco = SillaDelMuelle };
        _muelle.Cambio += AlCambiarElMuelle;
        Closed += (_, __) => { try { _muelle?.Close(); } catch { } };
    }

    private void AlCambiarElMuelle(bool desplegado)
    {
        _collapsed = !desplegado;
        if (!desplegado) CloseMenu();
        else CollapsedFace.Mood = Face.Mood;   // que las dos caras digan lo mismo al verse juntas
        // Plegado, la píldora no aparece: su sitio está dentro del panel.
        UpdateChip(_mood);
    }

    // ── Guardar la carita en el muelle, y sacarla de él (promesas 149 y 150) ─────────────────
    //
    // POR QUÉ EXISTE: la carita vive encima del trabajo de alguien. Cuando estorba, lo que se quería
    // no era cerrarla —seguir hablándole por el collar o por el atajo tiene sentido— sino quitarla de
    // en medio sin perderla. El muelle ya está siempre ahí y ya tiene su sitio; guardarla dentro
    // hace que el escondite tenga una PUERTA VISIBLE, en vez de ser un estado que hay que recordar.

    // LA SILLA ES UNA Y SE MUDA (promesa 272, spec 031). Hasta el 2026-09-17 el único escondite era
    // el muelle y este archivo hablaba con él por su nombre. Ahora hay ANFITRIONES —el muelle y la
    // consulta— y el FaceControl «Face» pasa del hueco de uno al del otro; SillaDeLaCarita dice en
    // cuál está, y es un solo valor: nunca dos sentadas a la vez.
    private readonly SillaDeLaCarita _silla = new();
    private AnfitrionDeLaCarita? _anfitrion;

    /// <summary>Los anfitriones donde se puede sentar, en orden Z: el muelle (topmost) primero.</summary>
    private List<AnfitrionDeLaCarita> Anfitriones()
    {
        var lista = new List<AnfitrionDeLaCarita>();
        if (_muelle != null) lista.Add(_muelle);
        foreach (var w in Application.Current.Windows.OfType<ConsultaWindow>())
            if (w.IsVisible && w.WindowState != WindowState.Minimized) lista.Add(w);
        return lista;
    }

    /// <summary>Soltar la carita encima de un anfitrión la sienta ahí. Devuelve si se quedó el gesto.</summary>
    private bool GuardarSiCaeEnUnAnfitrion(double x, double y)
    {
        if (_silla.Ocupada) return false;
        var anfitriones = Anfitriones();
        int cual = ReglaDelAnfitrion.Elegir(anfitriones.Select(a => a.Caja).ToArray(), new Point(x, y));
        if (cual < 0) return false;

        SentarEn(anfitriones[cual]);
        PlayTick();
        LogBus.Log("muelle", $"la carita se guarda en «{anfitriones[cual].Nombre}»: soltada en ({x:0},{y:0}), caja {anfitriones[cual].Caja}");
        return true;
    }

    private void SentarEn(AnfitrionDeLaCarita anfitrion)
    {
        if (Face.Parent is Decorator viejo) viejo.Child = null;
        anfitrion.Hueco.Child = Face;
        _silla.Sentar(anfitrion.Nombre);
        _anfitrion = anfitrion;
        anfitrion.Guardando = true;
        Face.Visibility = Visibility.Visible;   // ahora sí hay alguien sentado en esa silla
        // Si el anfitrión se cierra con la carita dentro, la carita vuelve a flotar: un escondite que
        // desaparece con lo escondido dentro no es un escondite. El muelle no se cierra solo.
        if (anfitrion is not Muelle) anfitrion.Ventana.Closed += ElAnfitrionSeFue;
        // Sentada en la consulta, el botón de llevar tiene quien lleve (promesa 274).
        if (anfitrion is ConsultaWindow consulta) consulta.Llevar = (destino, nuevo) => _ = ViajarAsync(destino, nuevo);
        Hide();
    }

    private void ElAnfitrionSeFue(object? sender, EventArgs e)
    {
        if (!_silla.Ocupada || sender is not Window w || !ReferenceEquals(w, _anfitrion?.Ventana)) return;
        LogBus.Log("muelle", $"«{_anfitrion!.Nombre}» se cerró con la carita dentro: vuelve a flotar");
        LevantarLaCarita();
        ShowActivated = false;
        Show();
        PosarLaCarita(Left, Top);
    }

    /// <summary>La carita deja la silla: el hueco queda vacío y la silla vuelve al muelle para la próxima.</summary>
    private void LevantarLaCarita()
    {
        if (!_silla.Ocupada) return;
        var a = _anfitrion;
        _silla.Levantar();
        _anfitrion = null;
        if (a != null)
        {
            a.Guardando = false;
            if (a is not Muelle) a.Ventana.Closed -= ElAnfitrionSeFue;
            if (a is ConsultaWindow consulta) consulta.Llevar = null;
        }
        // Y AL SACARLA, LA SILLA QUEDA VACÍA (petición del dueño, 2026-09-05). Dejarla puesta
        // enseñaba dos caras a la vez —una flotando y otra dentro del panel— sin que nada dijera
        // cuál era cuál: la del panel decía «Ü está guardada aquí» mintiendo.
        Face.Visibility = Visibility.Collapsed;
        if (Face.Parent is Decorator d && !ReferenceEquals(d, SillaDelMuelle))
        {
            d.Child = null;
            SillaDelMuelle.Child = Face;
        }
    }

    /// <summary>
    /// SACAR LA CARITA Y QUE EL ARRASTRE LO LLEVE WINDOWS.
    /// </summary>
    /// <remarks>
    /// ESTE ES EL ARREGLO DETERMINISTA, tras dos intentos que no lo eran. Los dos anteriores
    /// intentaban conservar la captura del ratón de un elemento que vive dentro del muelle; el
    /// muelle se pliega al salir el cursor, ese elemento deja de existir, y no hay forma de
    /// conservar la captura de algo que ya no está.
    ///
    /// <c>DragMove()</c> no tiene ese problema porque no es nuestro: manda un
    /// <c>WM_NCLBUTTONDOWN</c> con <c>HTCAPTION</c> y **Windows** entra en su propio bucle modal de
    /// mover ventana. A partir de ahí, la ventana sigue al ratón hasta que se suelte el botón, y da
    /// exactamente igual lo que le pase a nuestra interfaz por debajo: que el muelle se pliegue, que
    /// el elemento se destruya, que otra ventana se ponga delante. No hay nada nuestro que se pueda
    /// romper a medio gesto.
    ///
    /// Es BLOQUEANTE y eso es lo que lo hace cómodo: retorna cuando ya se soltó, así que el sitio
    /// final se lee justo después. El repo ya usaba este mismo patrón para arrastrar la barra.
    /// </remarks>
    private void SacarYArrastrar()
    {
        SacandoLaCaritaDelAnfitrion();
        try { DragMove(); }
        catch (Exception e)
        {
            // DragMove exige el botón pulsado. Si se soltó en el intervalo, no es un fallo: es un
            // tirón que no llegó a ser. Se DICE, que es lo contrario de un catch mudo (patrón nº3).
            LogBus.Log("muelle", $"el tirón no llegó a arrastre: {e.GetType().Name}: {e.Message}");
        }

        // Al volver ya está soltada, y donde la dejó la mano. Solo queda acotarla a la pantalla y
        // recordar el sitio.
        var sitio = ReglaDelMuelle.SitioAlSacar(
            new Point(Left, Top), new Size(ActualWidth, ActualHeight), SystemParameters.WorkArea);
        MoveTo(sitio.X, sitio.Y);
        OnWindowMoved(sitio.X, sitio.Y);
    }

    /// <summary>
    /// La carita sentada vuelve a existir, centrada donde está el cursor.
    /// </summary>
    /// <remarks>
    /// APARECE AL TIRAR Y NO AL SOLTAR. Si esperara al final, estarías arrastrando algo invisible y
    /// no habría forma de ver dónde va a caer hasta que ya cayó — que es el mismo vicio que «una caja
    /// que miente es peor que no tener caja» (aprendizaje nº4), con el dibujo ausente en vez de mal.
    /// </remarks>
    private void SacandoLaCaritaDelAnfitrion()
    {
        if (!_silla.Ocupada) return;
        LevantarLaCarita();

        // APARECE BAJO EL CURSOR. Escondida, su ventana conservaba el sitio donde se guardó —encima
        // del muelle—, así que al enseñarla salía ahí y el arrastre continuaba desde ese punto: se
        // veía como un salto y había que volver a agarrarla (2026-09-06, segunda vuelta del mismo
        // fallo). Ver ReglaDelMuelle.SitioAlAparecer.
        var cursor = PointToScreen(Mouse.GetPosition(this));
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        var sitio = ReglaDelMuelle.SitioAlAparecer(
            new Point(cursor.X / dpi.DpiScaleX, cursor.Y / dpi.DpiScaleY),
            new Size(ActualWidth, ActualHeight), SystemParameters.WorkArea);
        MoveTo(sitio.X, sitio.Y);

        // Y SIN ROBARLE LA ACTIVACIÓN AL ANFITRIÓN, que es la otra mitad del fallo: quien tiene el
        // ratón capturado es la carita PEQUEÑA, que vive en la ventana del anfitrión. Mostrar esta
        // ventana con activación se la quita, Windows suelta la captura y el arrastre muere en el
        // acto. Se queda en false para siempre: una carita flotante no debe robar el foco a nadie,
        // y los atajos que sí quieren traerla al frente llaman a Activate(), que no depende de esto.
        ShowActivated = false;
        Show();
    }

    /// <summary>La carita se posa donde la soltaste, entera dentro de la pantalla.</summary>
    private void PosarLaCarita(double x, double y)
    {
        var sitio = ReglaDelMuelle.SitioAlSacar(
            new Point(x, y), new Size(ActualWidth, ActualHeight), SystemParameters.WorkArea);
        MoveTo(sitio.X, sitio.Y);
        OnWindowMoved(sitio.X, sitio.Y);   // recordar el sitio y espejar al lado que toque
    }

    /// <summary>
    /// LIVE: la consulta clínica. Grabar en vivo y que la nota llegue al triage.
    /// </summary>
    /// <remarks>
    /// Hasta hoy solo se llegaba por <c>U.exe --consulta</c> o por su acceso directo del
    /// escritorio: la función más específica del producto no tenía puerta dentro de la aplicación.
    ///
    /// SI YA HAY UNA ABIERTA SE TRAE AL FRENTE en vez de abrir otra. Dos consultas vivas a la vez
    /// serían dos grabaciones sobre el mismo paciente, y la segunda no tendría forma de saber de la
    /// primera — un fallo que no se ve hasta que la nota llega partida en dos.
    /// </remarks>
    private void OnAbrirConsulta(object sender, RoutedEventArgs e)
    {
        var abierta = Application.Current.Windows.OfType<ConsultaWindow>().FirstOrDefault();

        // AL FRENTE es «se ve Y tiene el foco». Minimizada NO es oculta para Windows —IsVisible
        // sigue siendo true— y por eso el botón no hacía nada con la nota minimizada: existía, así
        // que se daba por atendida, y Activate() sobre una minimizada no la levanta (2026-09-06).
        bool alFrente = abierta != null
                        && abierta.IsVisible
                        && abierta.WindowState != WindowState.Minimized
                        && abierta.IsActive;

        PlayTick();
        switch (ReglaDeLaVentana.AlPulsarSuBoton(abierta != null, alFrente))
        {
            case QueHacerConLaVentana.Abrir:
                (Application.Current as App)?.AbrirLaConsulta();
                break;

            case QueHacerConLaVentana.TraerAlFrente:
                if (!abierta!.IsVisible) abierta.Show();
                if (abierta.WindowState == WindowState.Minimized) abierta.WindowState = WindowState.Normal;
                abierta.Activate();
                LogBus.Log("consulta", "la nota clínica se trae al frente");
                break;

            case QueHacerConLaVentana.Ocultar:
                // Minimizar y no Hide: minimizada sigue en la barra de tareas, así que quien no se
                // acuerde de este botón tiene otro camino de vuelta.
                abierta!.WindowState = WindowState.Minimized;
                LogBus.Log("consulta", "la nota clínica se quita de en medio");
                break;
        }
    }

    /// <summary>Alterna el muelle. Conserva el nombre porque lo llaman los atajos de siempre.</summary>
    private void ToggleCollapsed()
    {
        if (_muelle == null) return;
        if (_muelle.EstaDesplegado) _muelle.Plegar("lo pidió la aplicación");
        else _muelle.Desplegar("lo pidió la aplicación");
    }

    // --- El botón de voz que asoma al pasar por encima de la carita suelta ---


    /// <summary>
    /// Acercar el ratón a la carita suelta. Hasta el 2026-09-02 aquí asomaban tres pastillas
    /// —hablar, escribir, dictar a SAP—; se retiraron (spec 008) porque hablarle es lo que más se
    /// hace y no puede estar detrás de acertarle a una barrita de 4,5 px. Lo que hoy hace el hover
    /// es asegurarse de que el halo tenga el aspecto que toca; los ojos y la línea de texto llegan
    /// en las fases 2 y 3 de la spec.
    /// </summary>
    // ── La línea «Escríbele…» (promesa 167) ─────────────────────────────────
    //
    // Al morir la pastilla del chat (promesa 162) se fue con ella la ÚNICA forma de abrir el globo
    // con el ratón desde la carita suelta. Esto no es un adorno: es la puerta que tapa ese hueco.

    /// <summary>El reposo antes de que la línea asome. Lo decide <see cref="ReglaDeLaLinea"/>.</summary>
    private readonly System.Windows.Threading.DispatcherTimer _lineaTimer =
        new() { Interval = TimeSpan.FromMilliseconds(ReglaDeLaLinea.ReposoMs) };

    /// <summary>
    /// La gracia entre salir de la carita y que la línea se esconda.
    /// </summary>
    /// <remarks>
    /// La línea es un Popup, así que vive FUERA de los límites de CollapsedGroup y llevar la mano
    /// de la carita hacia ella dispara un MouseLeave. Sin este respiro, la puerta se cierra justo
    /// cuando vas a cruzarla.
    /// </remarks>
    private readonly System.Windows.Threading.DispatcherTimer _cerrarLineaTimer =
        new() { Interval = TimeSpan.FromMilliseconds(280) };

    /// <summary>Se está tirando de la carita. Mientras dure, la línea no asoma ni se queda.</summary>
    private bool _arrastrandoLaCarita;

    /// <summary>Conecta el reposo, la gracia y el aviso de arrastre. Se llama una vez.</summary>
    private void WireLaLinea()
    {
        _lineaTimer.Tick += (_, __) =>
        {
            _lineaTimer.Stop();
            if (ReglaDeLaLinea.Asoma(ReglaDeLaLinea.ReposoMs, _arrastrandoLaCarita))
                GhostPista.IsOpen = true;
        };

        _cerrarLineaTimer.Tick += (_, __) =>
        {
            _cerrarLineaTimer.Stop();
            // Si la mano volvió a la carita o entró en la propia línea, no era una salida.
            if (CollapsedGroup.IsMouseOver || GhostBorde.IsMouseOver) return;
            GhostPista.IsOpen = false;
        };

        // TIRAR DE LA CARITA CANCELA LA LÍNEA, y se cancela al APRETAR y no al empezar a arrastrar:
        // apretar es lo primero que hacen por igual el clic, el mantener y el tirón, y ninguno de
        // los tres es escribir.
        CollapsedFace.PreviewMouseLeftButtonDown += (_, __) =>
        {
            _arrastrandoLaCarita = true;
            _lineaTimer.Stop();
            GhostPista.IsOpen = false;
        };
        CollapsedFace.PreviewMouseLeftButtonUp += (_, __) => _arrastrandoLaCarita = false;

        // Ir de la carita a la línea y volver no la cierra: el cierre se agenda y se cancela.
        GhostBorde.MouseEnter += (_, __) => _cerrarLineaTimer.Stop();
        GhostBorde.MouseLeave += (_, __) => { _cerrarLineaTimer.Stop(); _cerrarLineaTimer.Start(); };
    }

    /// <summary>Acercar el ratón a la carita: el halo se pone al día y arranca el reposo.</summary>
    private void OnCollapsedHoverIn(object sender, System.Windows.Input.MouseEventArgs e)
    {
        PintarHalo();   // que aparezca ya con el aspecto que toca, no con el de la vez anterior
        _prevForeground = GetForegroundWindow();   // para que Esc devuelva el teclado a donde estaba
        _cerrarLineaTimer.Stop();
        _lineaTimer.Stop();
        _lineaTimer.Start();
    }

    private void OnCollapsedHoverOut(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _lineaTimer.Stop();
        _cerrarLineaTimer.Stop();
        _cerrarLineaTimer.Start();
    }

    /// <summary>
    /// Pulsar la línea abre el globo, que es lo que hacía la pastilla del chat. Por el camino de
    /// main y sin tocarlo: <see cref="ShowTalk"/> ya sabe desplegar el muelle y pedir el foco.
    /// </summary>
    private void OnGhostClic(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        PlayTick();
        if (_talkOpen) HideTalk(); else ShowTalk(MotivoDelGlobo.LoPidioAlguien, focusInput: true);
    }

    /// <summary>
    /// El halo dice si la conversación está viva, y respira con lo que se está diciendo.
    /// </summary>
    /// <remarks>
    /// Con el micrófono abierto, la carita suelta se veía EXACTAMENTE igual que apagada: la única
    /// señal de que había una conversación en marcha estaba en la barra, que es justo lo que no se
    /// ve cuando la carita está sola (2026-08-06). Hasta el 2026-09-02 la señal era una pastilla
    /// invertida con un halo detrás; sin pastillas, el halo rodea a la carita misma.
    ///
    /// Crece con el volumen de la voz: no es un adorno que late solo, es el mismo nivel que mueve la
    /// boca, así que lo que se ve pulsar es lo que se está oyendo. Y DE QUÉ COLOR se está oyendo:
    /// azul = por el collar; gris = por un micrófono del PC. Es la única forma de saber cuál de los
    /// dos te está escuchando sin abrir el log, y cambia sola si hay relevo a media conversación.
    ///
    /// Vive en el mismo Grid que la carita y detrás de ella: crece sin empujar nada, y no depende
    /// de dónde esté el ratón —lo que está pasando ahora mismo no puede depender de eso—.
    /// </remarks>
    private static string Recorte(string t, int n) => t.Length <= n ? t : "…" + t[^n..];

    private void PintarHalo()
    {
        bool viva = _vivo?.Viva == true;
        if (!viva && !_actualizando)
        {
            VoiceHalo.Opacity = 0;
            VoiceHaloEscala.ScaleX = VoiceHaloEscala.ScaleY = 1;
            return;
        }

        // La ventana no decide nada: pregunta. Cuánto crece y de qué color vive en ReglaDelHalo,
        // donde el contrato puede barrer el rango entero de voz y comprobar que el halo cabe en el
        // aire que la carita tiene (promesa 163).
        double nivel = viva ? _vivo!.NivelVoz : 0.18;
        bool collar = viva && _vivo!.PorElCollar;
        VoiceHaloColor.Color = ReglaDelHalo.ColorParaEstado(collar, _actualizando);
        VoiceHalo.Opacity = ReglaDelHalo.Opacidad(nivel, _bocaPaso);
        VoiceHaloEscala.ScaleX = VoiceHaloEscala.ScaleY = ReglaDelHalo.Escala(nivel, _bocaPaso);
    }

    // --- Temas de la carita: se alternan manteniéndola oprimida (claro → oscuro → transparente) ---

    private FaceTheme _theme = FaceTheme.Dark;

    /// <summary>Aplica el tema (claro/oscuro) a ambas caritas.</summary>
    private void ApplyTheme(FaceTheme theme)
    {
        _theme = theme;
        Face.Theme = theme;
        CollapsedFace.Theme = theme;
    }

    /// <summary>Alterna el tema claro ↔ oscuro.</summary>
    private void CycleTheme()
    {
        var next = _theme == FaceTheme.Dark ? FaceTheme.Light : FaceTheme.Dark;
        ApplyTheme(next);
        _config.FaceTheme = next.ToString();
        _config.Save();
        PlayTick();
        SetStatus($"Tema {(next == FaceTheme.Dark ? "oscuro" : "claro")}");
    }

    /// <summary>Conecta los gestos (toque/doble toque/mantener/arrastre) a ambas caritas.</summary>
    private void WireFaceGestures()
    {
        // LA CARITA DEL PANEL NO USA FaceGestures, y es a propósito. FaceGestures arrastra moviendo
        // la ventana en cada MouseMove, lo que exige conservar la captura del ratón durante todo el
        // gesto — y este elemento vive DENTRO del muelle, que se pliega en cuanto el cursor sale de
        // él. Al plegarse, Face desaparece del árbol visual, la captura se va con él y el arrastre
        // muere justo al cruzar el borde del panel: exactamente lo que describió el dueño («tiene
        // como un límite que es hasta dónde llega la ventana», 2026-09-06).
        //
        // Aquí el gesto es más simple y NO depende de que este elemento siga existiendo: en cuanto
        // se reconoce como arrastre, se le entrega el trabajo a Windows. Ver SacarYArrastrar.
        Point origenDelTiron = default;
        bool tirando = false;

        Face.MouseLeftButtonDown += (_, ev) =>
        {
            origenDelTiron = ev.GetPosition(this);
            tirando = false;
            Face.CaptureMouse();
            ev.Handled = true;
        };
        Face.MouseMove += (_, ev) =>
        {
            if (tirando || ev.LeftButton != MouseButtonState.Pressed || !Face.IsMouseCaptured) return;
            var ahora = ev.GetPosition(this);
            // El mismo umbral que FaceGestures: por debajo, un pulso normal convertiría en arrastre
            // lo que iba a ser un clic.
            if ((ahora - origenDelTiron).LengthSquared <= 169) return;

            tirando = true;
            Face.ReleaseMouseCapture();
            SacarYArrastrar();
        };
        Face.MouseLeftButtonUp += (_, ev) =>
        {
            if (Face.IsMouseCaptured) Face.ReleaseMouseCapture();
            if (!tirando) StartMicByFace();
            ev.Handled = true;
        };

        // Carita suelta: arrastra su propia ventana, y al soltar se va a un lado como la sueltas.
        new FaceGestures(this, CollapsedFace)
        {
            // UN CLIC HABLA (spec 010, 2026-09-05, pedido por el usuario). Antes el clic abría la
            // barra y el micrófono estaba detrás de un doble clic: el gesto más usado escondido
            // detrás del que hay que saberse. Ahora el clic ALTERNA la conversación, y sin doble
            // toque que distinguir abre en el acto — ver ReglaDelToque.
            SingleTap = StartMicByFace,
            DoubleTap = null,
            LongPress = CycleTheme,
            // SOLTARLA ENCIMA DEL MUELLE LA GUARDA (promesa 149). Se pregunta antes que el borde
            // porque el borde no es una opción: si no, la carita saldría disparada al lado derecho
            // —que es justo donde está el muelle— y nunca llegaría a guardarse.
            Soltada = GuardarSiCaeEnUnAnfitrion,
            // Un solo callback alimenta las dos cosas que dependen de dónde quedó: recordar el sitio
            // y espejar la carita al lado que toque. Llega con el DESTINO, así que el espejo se
            // aplica al empezar el vuelo y no al terminarlo — viaja ya con su forma final en vez de
            // darse la vuelta al aterrizar.
            Moved = OnWindowMoved,
        };

        // Y con dos dedos en el trackpad, sin tener que agarrarla. Solo con la carita suelta: con la
        // barra abierta el scroll es del menú, y robárselo sería quitarle una función que sí tiene.
        _ = new LanzarConScroll(this, () => _collapsed,
            (vx, vy) => EdgeSnap.Aplicar(this, vx, vy, OnWindowMoved));
    }

    /// <summary>Un clic en la carita = micrófono (spec 010; era el doble clic hasta el 2026-09-05).
    /// Suena el carrillón y no el tick: el tick acompañaba a abrir la barra, y abrir la conversación
    /// es otra cosa — dos notas que SUBEN, escuchar = abrirse.</summary>
    private void StartMicByFace()
    {
        PlayChime();
        OnMic(this, new RoutedEventArgs());
    }

    /// <summary>
    /// Doble Ctrl. Si Ü está oculta, REAPARECE; si está a la vista, abre o cierra el micrófono.
    /// </summary>
    /// <remarks>
    /// REAPARECER TIENE PRIORIDAD SOBRE ALTERNAR EL MICRÓFONO, y no es una preferencia estética: el
    /// gesto de siempre llama a `OnMic`, que ALTERNA — y `self_hide` deja la conversación VIVA. Así
    /// que estando oculta, el doble Ctrl de antes habría colgado la conversación sin traerla de
    /// vuelta: exactamente lo contrario de lo que pide quien hace el gesto para recuperarla
    /// (2026-08-15, pedido por el usuario: «que aparezca de nuevo con doble Ctrl»).
    ///
    /// Estando oculta NO se toca el micrófono. Quien la escondió por voz sigue hablando con ella; lo
    /// único que falta es verla.
    /// </remarks>
    private void DobleCtrl()
    {
        if (!IsVisible)
        {
            _prevForeground = GetForegroundWindow();   // para poder devolver el teclado con Esc
            // Si estaba GUARDADA en el muelle, sale de ahí: pedir que vuelva y que vuelva sin que el
            // muelle deje de decir que la tiene dentro sería dejar la señal mintiendo.
            SacandoLaCaritaDelAnfitrion();
            Show();
            _muelle?.Show();
            Activate();
            PlayTick();
            LogBus.Log("atajo", "doble Ctrl: Ü estaba oculta y vuelve a la vista");
            return;
        }
        StartMicByFace();
    }

    // --- Sonidos: tick al clic, carrillón al micrófono. Sintetizados a propósito (2026-08-31):
    //     los WAV heredados de Android sonaban a otro sistema. Estos siguen la gramática de los
    //     de Apple — cortos, ataque suave (nada de clic digital), caída exponencial, frecuencias
    //     cálidas y volumen contenido: el tick es un «thock» de 45 ms (cuerpo 190 Hz + tap
    //     1,2 kHz) y el carrillón dos notas que SUBEN (D5→A5, escuchar = abrirse), 550 ms. ---

    private System.Media.SoundPlayer? _tick;
    private System.Media.SoundPlayer? _chime;

    private void LoadSounds()
    {
        _tick = LoadWav("assets/tick.wav");
        _chime = LoadWav("assets/mic_chime.wav");
    }

    private static System.Media.SoundPlayer? LoadWav(string relativeUri)
    {
        try
        {
            var stream = Application.GetResourceStream(new Uri(relativeUri, UriKind.Relative))?.Stream;
            if (stream == null) return null;
            var player = new System.Media.SoundPlayer(stream);
            player.Load(); // precarga: reproducir luego es inmediato
            return player;
        }
        catch { return null; }
    }

    private void PlayTick() { try { _tick?.Play(); } catch { } }
    private void PlayChime() { try { _chime?.Play(); } catch { } }

    // --- Entrada del usuario ---

    private void OnInputKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        string text = Input.Text.Trim();
        Input.Clear();
        if (text.Length == 0) return;

        // Si el asistente está esperando una respuesta a su pregunta, esto la resuelve.
        if (_pendingAnswer != null && !_pendingAnswer.Task.IsCompleted)
        {
            _pendingAnswer.TrySetResult(text);
            return;
        }

        // Con la conversación viva abierta, escribir es SEGUIR HABLANDO, no empezar otra cosa.
        // Arrancar aquí un objetivo aparte pondría dos cerebros a mover la misma pantalla a la vez.
        if (_vivo?.Viva == true) { _ = _vivo.EnviarTextoAsync(text); return; }

        _ = StartGoal(text);
    }

    /// <summary>
    /// El micrófono. Con voz en vivo disponible, ABRE Y CIERRA una conversación; sin ella, cae al
    /// dictado de una frase de siempre.
    ///
    /// Los dos comportamientos no son intercambiables y por eso el icono cambia: el dictado escucha
    /// ocho segundos y se cierra solo, así que pulsar y hablar basta; una conversación viva sigue
    /// abierta hasta que la cuelgas, y un micrófono que se queda abierto sin decirlo es lo último
    /// que quiere nadie. Rojo = te está oyendo ahora mismo.
    ///
    /// El doble clic en la carita entra por aquí, así que hereda las dos cosas: abre la conversación
    /// y, con otro doble clic, la cuelga.
    /// </summary>
    private async void OnMic(object sender, RoutedEventArgs e)
    {

        if (_vivo != null) { await _vivo.AlternarAsync(); return; }

        SetStatus("Escuchando…");   // a la píldora, no al globo: ver el comentario de _vivo.Cambio
        string heard = await _voice.ListenOnceAsync(CancellationToken.None);
        if (string.IsNullOrWhiteSpace(heard)) { SetStatus("No te escuché"); return; }
        if (_pendingAnswer != null && !_pendingAnswer.Task.IsCompleted) { _pendingAnswer.TrySetResult(heard); return; }
        _ = StartGoal(heard);
    }

    // ── EL COLLAR OMI: los dos gestos que lo piden (spec 001) ────────────────────
    //
    // Sin variable de entorno y sin ajuste escondido: se pide con la mano, en el momento. Un
    // interruptor que hay que saber que existe obliga a arrancar la aplicación de una forma
    // especial, y entonces «probarlo» ya no es lo mismo que usarlo.

    /// <summary>
    /// EL COLLAR ABRE LA VENTANA DE ANÁLISIS CLÍNICO (petición del dueño, 2026-09-05).
    /// </summary>
    /// <remarks>
    /// Abría <c>PanelDelCollar</c>, una ventana propia con «Enlazar el collar» y «Olvidar este
    /// collar». Esa pantalla quedó vieja: elegir por dónde se te oye —computador, collar por
    /// Bluetooth, o collar por el teléfono— ya vive en el selector de micrófono de
    /// <see cref="ConsultaWindow"/>, que además ANUNCIA la elección a toda la app (promesa 146),
    /// cosa que el panel viejo no hacía. Dos puertas al mismo enlace, una de ellas sin enterarse
    /// de la otra, es como se acaba con dos verdades sobre un mismo hecho.
    ///
    /// Se comprobó antes de borrarlo, y no de memoria: enlazar y conectar los hace igual el
    /// selector (<c>Permanente ? ConectarAsync() : EncenderAsync()</c>), y el servicio
    /// <c>CollarPermanente</c> —que es quien de verdad sabe del collar— no se toca: lo siguen
    /// usando la carita y la consulta.
    ///
    /// LO QUE SÍ SE PIERDE, dicho para que conste: «Olvidar este collar». Era el único sitio que
    /// llamaba a <c>CollarPermanente.Olvidar()</c>. Desenlazar deja de tener puerta hasta que se le
    /// dé una en el selector de la consulta, que es donde le toca.
    /// </remarks>
    private void OnCollar(object sender, RoutedEventArgs e) => OnAbrirConsulta(sender, e);

    /// <summary>
    /// EL BOTÓN DEL COLLAR ENCIENDE Y APAGA EL HABLA, y se engancha al SERVICIO y no a la sesión de
    /// voz. Ahí está la diferencia: colgado del servicio, el botón llega también con la voz apagada,
    /// que es la única forma de que pueda ENCENDERLA. Colgado de la conversación sólo podía apagar.
    ///
    /// Y va al mismo <see cref="StartMicByFace"/> que el doble clic en la carita y el doble Ctrl: un
    /// solo sitio decide qué es «alternar», así que ningún gesto puede quedar desincronizado.
    /// </summary>
    private void EngancharCollar()
    {
        CollarPermanente.BotonPulsado += () => Dispatcher.BeginInvoke(() => StartMicByFace());
        CollarPermanente.Cambio += () => Dispatcher.BeginInvoke(PintarCollar);
        CollarPermanente.Restaurar();
        PintarCollar();
    }

    /// <summary>
    /// La pieza del collar dice el estado sin abrir nada: verde conectado, ámbar enlazado pero sin
    /// conexión, blanco sin enlazar. Mismo criterio que el popup — una sola verdad, dos sitios donde
    /// se ve, y ninguno puede contradecir al otro porque los dos leen del servicio.
    /// </summary>
    private void PintarCollar()
    {
        if (CollarPunto == null) return;
        CollarPunto.Fill = CollarPermanente.Conectado
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x6E, 0xD8, 0x8B))
            : CollarPermanente.Permanente
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xA5, 0x1F))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xE8, 0xFF, 0xFF, 0xFF));
    }

    /// <summary>
    /// La voz pasa a entrar por el collar. Vale con la conversación abierta y sin abrir.
    /// </summary>
    private void PasarLaVozAlCollar()
    {
        Voice.LiveAudio.UsarCollar = true;
        _vivo?.PasarAlCollar();
        SetStatus("Buscando el collar Omi…");
        LogBus.Log("voz-viva", "el collar lo pidió el usuario con un gesto");
    }

    private void OnStop(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        _pendingAnswer?.TrySetResult("");
        // Cancelar el bucle no callaba lo ya encolado en el TTS: se pedía parar y Ü seguía hablando
        // hasta terminar la frase. Detener es detener, también la voz.
        _voice.Silence();
        SetStatus("Detenido");
        ShowStop(false);
        // SE QUEDA DICHO EN EL NOTCH (promesa 259, pedido del dueño 2026-09-17: «una vez yo detuve la
        // conversación… que se guarde en notch»). Solo si ya existía: pulsar ⏹ sin que hubiera nada
        // en marcha no tiene por qué traer el notch a la fuerza.
        _acciones?.Detenido("detenido a mano");
    }

    private void SetMuted(bool muted)
    {
        _voice.Muted = muted;   // el setter ya corta en seco lo que estuviera diciendo
        _config.Muted = muted;
    }

    /// <summary>
    /// Lo que hace Ü con self_mute/self_hide/self_close, pedido por VOZ. Es <see cref="ConversacionEnVivo.Autocontrol"/>.
    /// </summary>
    /// <remarks>
    /// SIEMPRE EN EL HILO DE LA VENTANA. Esto se llama desde el bucle que recibe mensajes del
    /// WebSocket de Gemini, que no es el hilo de UI de WPF — tocar `Hide()` o el Dispatcher fuera de
    /// su hilo lanza o, peor, funciona a veces y otras no. `Dispatcher.Invoke` (no InvokeAsync)
    /// porque quien llama necesita la frase de vuelta YA, para devolvérsela al modelo.
    ///
    /// self_close NO cierra en este mismo tick: si `Application.Current.Shutdown()` corriera aquí
    /// dentro, mataría el proceso ANTES de que la respuesta de la herramienta saliera por el
    /// WebSocket, y Ü se callaría a media frase de despedida en vez de decirla. Se deja un respiro
    /// para que la respuesta viaje y el modelo pueda hablar antes de que el proceso termine.
    /// </remarks>
    private string AtenderAutocontrol(string herramienta) => Dispatcher.Invoke(() =>
    {
        switch (herramienta)
        {
            case "self_mute":
                SetMuted(true);
                return "Silenciado. Un clic en el altavoz para que vuelva a hablar.";

            case "self_hide":
                // Las DOS ventanas. Esconder solo la carita habría dejado la pestaña del muelle
                // contra el borde: «me oculto» y seguir viéndose es peor que no ocultarse.
                _muelle?.Plegar("Ü se oculta");
                _muelle?.Hide();
                Hide();
                // Se ofrece el doble Ctrl y no Ctrl+Alt+U porque es el gesto que ya usa para
                // hablarle: una tecla menos que recordar, y la misma que tenía en la mano.
                return "Me oculto. Doble Ctrl para que vuelva.";

            case "self_close":
                LogBus.Log("atajo", "self_close pedido por voz: cerrando en 2,5 s para dar tiempo a la despedida");
                var cierre = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
                cierre.Tick += (_, __) => { cierre.Stop(); Application.Current.Shutdown(); };
                cierre.Start();
                return "Cerrándome. Hasta luego.";

            case "self_update":
                _ = AplicarActualizacionConNarrativaAsync();
                return "Voy a buscar la actualización. Verás el halo morado y te contaré qué trae antes de reiniciarme.";

            // No es autocontrol —no se acciona a sí misma— pero se despacha por aquí porque mira
            // ESTE equipo, y eso lo sabe la ventana y no el mapa de pantallas de otras apps.
            case "scan_computer":
                return Onboarding.Presentacion.Escanear();

            default:
                return $"«{herramienta}» no es una herramienta de autocontrol conocida";
        }
    });

    /// <summary>
    /// Que la primera vez se presente ELLA, en voz alta, y ofrezca mirar el equipo.
    ///
    /// POR QUÉ. Recién instalada aparecía una carita en la esquina y nadie decía para qué servía:
    /// quien la recibe tiene que adivinar que se le habla y qué se le puede pedir. Presentarse es la
    /// diferencia entre un icono raro y una herramienta (2026-08-16, pedido por el usuario).
    ///
    /// SE MARCA ANTES DE HABLAR, NO DESPUÉS. Si se marcara al terminar, cualquier fallo a mitad
    /// —sin red, sin micrófono— dejaría el saludo pendiente y volvería a soltarlo en cada arranque,
    /// que es peor que no haberlo dado: una presentación repetida dice que no te recuerda.
    /// </summary>
    private void OfrecerElPrimerEncuentro()
    {
        // SE DICE SIEMPRE POR QUÉ, TAMBIÉN CUANDO NO PASA NADA. Un camino que solo escribe en el log
        // cuando funciona es indistinguible de uno que no existe: al probar la primera experiencia
        // no había NI UNA línea sobre ella, y la explicación —que ya se había dado por hecha en una
        // prueba anterior— no estaba escrita en ningún sitio (2026-08-16, lo pidió el usuario:
        // «aquí no veo la ejecución que hizo»). Callar el caso normal es lo que deja a oscuras el
        // caso raro, porque son el mismo silencio.
        if (!_config.Onboarded)
        {
            LogBus.Log("presentacion", "no me presento: todavía no hay correo (onboarding sin terminar)");
            return;
        }
        if (_config.PresentacionHecha)
        {
            LogBus.Log("presentacion", "no me presento: ya lo hice en este equipo. "
                + @"Para volver a verlo: cierra Ü, pon ""PresentacionHecha"": false en "
                + @"%APPDATA%\U\config.json y vuelve a abrir.");
            return;
        }

        LogBus.Log("presentacion", $"PRIMER ENCUENTRO: es la primera vez en este equipo"
            + (string.IsNullOrWhiteSpace(_config.DisplayName) ? "" : $" · usuario «{_config.DisplayName}»"));
        _config.PresentacionHecha = true;
        _config.Save();

        // Se espera a que la ventana esté puesta: hablarle a alguien que todavía no te ha visto
        // aparecer es una voz saliendo de ningún sitio.
        var arranque = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        arranque.Tick += async (_, __) =>
        {
            arranque.Stop();
            var crono = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (_vivo is null)
                {
                    LogBus.Log("presentacion", "ABORTADO: no hay capa de voz montada, así que no hay con qué hablar");
                    return;
                }

                if (!_vivo.Viva) { LogBus.Log("presentacion", "abriendo la voz para saludar…"); StartMicByFace(); }
                else LogBus.Log("presentacion", "la voz ya estaba abierta");

                // Abrir la sesión es ir y volver por la red, y no avisa cuando termina. Se le da
                // margen comprobando, en vez de dormir a ciegas un número redondo: así el saludo
                // sale en cuanto está lista y no siempre en el peor caso.
                for (int i = 0; i < 40 && _vivo?.Viva != true; i++) await Task.Delay(250);
                if (_vivo?.Viva != true)
                {
                    LogBus.Log("presentacion", $"ABORTADO: la voz no abrió en {crono.ElapsedMilliseconds} ms. "
                        + "No hay saludo — mira las líneas «voz-viva» de justo antes para saber por qué.");
                    return;
                }

                LogBus.Log("presentacion", $"voz lista en {crono.ElapsedMilliseconds} ms · mandando el saludo");
                await _vivo.EnviarTextoAsync(Onboarding.Presentacion.Saludo(_config.DisplayName));
                LogBus.Log("presentacion", "saludo entregado. Lo que Ü diga a partir de aquí sale en «voz-viva»; "
                    + "si acepta el escaneo, se verá «ejecutando «scan_computer»» y luego el resultado.");
            }
            catch (Exception ex) { LogBus.Log("presentacion", $"ABORTADO por excepción: {ex.Message}"); }
        };
        arranque.Start();
    }

    // --- Enseñanza activa (grabar pantalla+voz) ---

    /// <summary>El interruptor del decisor (spec 036). Nulo hasta que exista el mapa.</summary>
    private Decision.InterruptorDelDecisor? _interruptorDelDecisor;

    /// <summary>
    /// Instrucciones y catálogo, otra vez a la sesión: lo mismo que hace Learn/Work al cambiar de modo. Sin
    /// sesión de voz no hay nada que re-mandar; la próxima apertura ya lee el catálogo nuevo.
    /// </summary>
    private Task ReenviarCatalogoALaVozAsync() =>
        _vivo != null
            ? _vivo.CambiarModoAsync(Voice.ConversacionEnVivo.InstruccionesNormales, Voice.ConversacionEnVivo.Herramientas())
            : Task.CompletedTask;

    /// <summary>
    /// EL BOTÓN «JEV»: enciende y apaga el decisor sin reiniciar. Encender pide Jev; sin clave se queda apagado
    /// y el botón dice por qué (promesa 290). Si el entorno dice «luna» o no dice nada, el botón significa
    /// «enciende Jev»: es lo que una persona espera de pulsarlo.
    /// </summary>
    private void OnToggleJev(object sender, RoutedEventArgs e)
    {
        if (_interruptorDelDecisor == null) return;
        if (_interruptorDelDecisor.Encendido) _interruptorDelDecisor.Apagar();
        else _interruptorDelDecisor.Encender(n =>
        {
            // POR EL RESOLUTOR Y NO POR EL ENTORNO PELADO (promesa 300): en una copia distribuida la
            // clave de TypeSafe la dio Graph, y leyendo solo el entorno el boton diria que falta.
            string? v = Credenciales.ClavesDelBackend.DeLaApp(n);
            if (n == Decision.ConfiguracionDelDecisor.Interruptor && (string.IsNullOrWhiteSpace(v) || v.Trim().Equals("luna", StringComparison.OrdinalIgnoreCase)))
                return "jev";
            return v;
        });
        PintarBotonJev();
    }

    /// <summary>
    /// El botón dice en qué estado está de verdad, y el porqué va a la línea de estado: NADA al pasar el
    /// ratón (promesa 164). Un verde sin medir es peor que no tener botón.
    /// </summary>
    private void PintarBotonJev()
    {
        if (JevBtn == null) return;
        bool on = _interruptorDelDecisor?.Encendido == true;
        JevBtn.Content = on ? "Jev · on" : "Jev · off";
        JevBtn.Opacity = on ? 1.0 : 0.7;
        if (_interruptorDelDecisor != null) SetStatus("Jev " + _interruptorDelDecisor.Estado);
    }

    private async void OnToggleTeach(object sender, RoutedEventArgs e)
    {
        if (_teaching) await StopTeachingAsync();
        else await StartTeachingAsync();
    }

    /// <summary>
    /// Se espera de verdad y se capturan los errores: si arrancar la grabación falla (p.ej. la
    /// librería nativa no carga), el botón vuelve a su estado normal y el error queda visible en el
    /// estado y en el LogBus. Cuando esto era `_ = StartAsync()` (fire-and-forget), la excepción se
    /// perdía en el aire y el usuario solo se enteraba minutos después, al detener, con un "no hay
    /// grabación para procesar" que no explicaba nada.
    /// </summary>
    private async Task StartTeachingAsync()
    {
        if (!_graphConfig.IsConfigured)
        {
            SetStatus("Para enseñar, configura Graph (URL + API key) en el panel Backend.");
            return;
        }

        // Enseñanza unificada: recorder de pasos UIA + video, en paralelo, con un solo botón.
        var graph = new GraphClient(_graphConfig);
        _teachSession = new WorkflowTeachSession(
            graph, _graphConfig, _teachUiaSurface, _teachSapSurface, _backend!, _videoLibrary, _config.UserId)
        {
            // LA LLEGADA DE UN CLIC LA DA EL TERRENO (promesa 178): la misma arista que map_batch
            // verifica. Por selector si el vigía lo trajo; si no, por el nombre de la puerta.
            LlegadaSegunElTerreno = (desde, selector, etiqueta) =>
            {
                if (string.IsNullOrWhiteSpace(desde)) return "";
                var salidas = _mapaVivo.Nucleo.DesdeAqui(desde);
                var a = (selector.Length > 0 ? salidas.FirstOrDefault(v => v.Que.Selector.Equals(selector, StringComparison.Ordinal)) : null)
                     ?? (etiqueta.Length > 0 ? salidas.FirstOrDefault(v => v.Que.Etiqueta.Equals(etiqueta, StringComparison.OrdinalIgnoreCase)) : null);
                return a?.Destino ?? "";
            },
        };
        _teachSession.StatusChanged += (_, msg) => Dispatcher.Invoke(() => SetStatus(msg));
        // LO QUE DICES MIENTRAS ENSEÑAS es lo que convierte un valor tecleado en un DATO con
        // nombre (promesa 123) y un elemento en un recuerdo (124). Sin este enganche la demo ve las
        // manos y no oye nada, que es como estaba desde julio.
        if (_vivo != null)
        {
            var sesion = _teachSession;
            _oyendoParaEnsenar = frase => sesion.Oyo(frase);
            _vivo.DijoElUsuario += _oyendoParaEnsenar;

            // EL MICRÓFONO SE ABRE SOLO AL ENSEÑAR (pedido por el dueño, 2026-09-03). Enseñar es
            // hablar: lo que se dice mientras se hace es lo que convierte un valor tecleado en un
            // DATO con nombre y un elemento en un recuerdo. Pedirle al humano que se acuerde de
            // encender el micrófono es pedirle que se acuerde de la mitad de la función — y quien
            // se olvide se lleva una demo muda sin enterarse hasta el final.
            //
            // Se recuerda si lo abrimos NOSOTROS para devolverlo como estaba al terminar: dejar el
            // micrófono abierto en la cara de alguien que no lo pidió es la avería opuesta.
            _vozAbiertaParaEnsenar = !_vivo.Viva;
            if (_vozAbiertaParaEnsenar)
            {
                SetStatus("Abriendo el micrófono: cuéntame lo que vas haciendo…");
                try { await _vivo.ArrancarAsync(); }
                catch (Exception ex)
                {
                    // Sin voz se PUEDE enseñar, solo que sin datos con nombre. Se dice y se sigue:
                    // perder la demo entera por el micrófono sería peor.
                    _vozAbiertaParaEnsenar = false;
                    LogBus.Log("teach", $"no pude abrir el micrófono para enseñar: {ex.Message}");
                    SetStatus("No pude abrir el micrófono: enseñaré igual, pero sin lo que digas "
                            + "no sabré qué es un dato y qué es parte de la tarea.");
                }
            }
        }
        _teachSession.PasosEnviados += (_, n) => Dispatcher.Invoke(() => _aura?.Pasos(n));

        // Ü PASA A APRENDIZ (promesa 138): sin manos y con las instrucciones de quien escucha. El
        // 2026-09-03 la voz seguía siendo el asistente mientras se le enseñaba, y tomó «vas a hacer
        // scroll» por una orden. Mismo micrófono, mismo socket: solo cambia quién es.
        if (_vivo is { Viva: true })
        {
            try
            {
                await _vivo.CambiarModoAsync(Teach.ModoAprendiz.Instrucciones,
                    Teach.ModoAprendiz.Utensilios(Voice.ConversacionEnVivo.Herramientas()));
            }
            catch (Exception ex) { LogBus.Log("teach", $"no pude poner la voz en modo aprendiz: {ex.Message}"); }
        }

        SetTeachingUi(true); // el aura arranca TENUE aquí: enseñando, pero aún sin grabar
        ShowTalk(MotivoDelGlobo.SoloEsProgreso); // el conteo regresivo y el estado de la grabación se ven ahí
        try
        {
            // Título vacío: se autogenera al final desde lo aprendido (WorkflowLearner en Graph).
            // StartAsync incluye un countdown de 3s para que cambies a la app que vas a enseñar.
            await _teachSession.StartAsync("", CancellationToken.None);
            PintarAura(); // ya graba: el aura se enciende del todo y respira
        }
        catch (Exception ex)
        {
            LogBus.Log("teach", $"no se pudo iniciar la enseñanza: {ex}");
            SetStatus($"No se pudo iniciar la enseñanza: {ex.Message}");
            await _teachSession.DisposeAsync();
            _teachSession = null;
            SetTeachingUi(false);
        }
    }

    /// <summary>Botón 🎓 en rojo (⏸) mientras se graba; todo de vuelta a lo normal si no.
    /// En la barra el botón es solo icono: el verbo va en el tooltip, y el estado en el color.</summary>
    private void SetTeachingUi(bool teaching)
    {
        _teaching = teaching;
        RefreshMood();   // grabar es un estado de la cara, no solo un color de botón
        PintarAura();    // y de la pantalla entera: el aura sigue a la misma bandera
        TeachBtn.Content = teaching ? "⏸" : "🎓";
        TeachBtn.Background = new System.Windows.Media.SolidColorBrush(teaching
            ? System.Windows.Media.Color.FromArgb(0x88, 255, 59, 48)   // rojo, como StopBtn
            : System.Windows.Media.Color.FromArgb(0x1A, 255, 255, 255)); // el fondo normal de BarBtn
        // Reinicio en caliente: sigue siendo follow-up de la enseñanza unificada, así que el botón
        // no llega a mostrarse nunca. Pasa por la zona igual, para que el día que se implemente no
        // haya que acordarse de este sitio.
        RestartTeachBtn.Visibility = Visibility.Collapsed;
        UpdateContextZone();
    }

    /// <summary>
    /// El aura de los bordes sigue a la REGLA (promesa 107, spec 006), y la regla se alimenta de
    /// las dos banderas que ya existían: «enseñando» (el botón) y «grabando» (la sesión). No se
    /// decide aquí en qué fase está: se pregunta, para que lo que se pinta sea lo que el contrato
    /// juzga. Se llama desde los tres sitios en que cambia alguna de las dos: al pulsar Enseñar
    /// (preparando), al arrancar la grabación (aprendiendo) y al terminar o fallar (apagada).
    /// </summary>
    private void PintarAura()
    {
        var fase = ReglaDelAura.Decidir(_teaching, _teachSession?.IsRecording == true);
        if (fase == FaseDelAura.Apagada) { _aura?.Mostrar(fase); return; }

        bool nueva = _aura == null;
        _aura ??= new AuraDeAprendizaje();
        _aura.Mostrar(fase);
        // Las dos ventanas son Topmost y gana la última en aparecer: sin esto, la primera vez que
        // el aura sale, la carita plegada en el borde queda DEBAJO del degradado.
        if (nueva) { Topmost = false; Topmost = true; }
    }

    /// <summary>
    /// "Me equivoqué": tira lo grabado y vuelve a grabar desde cero. Sin esto, un error a mitad de la
    /// demostración solo se podía resolver deteniendo — y detener manda el video malo a Gemini y sus
    /// notas al cerebro, que es exactamente lo que no queremos que aprenda.
    /// </summary>
    private void OnRestartTeach(object sender, RoutedEventArgs e)
    {
        // El reinicio en caliente aún no está en la enseñanza unificada (recorder de pasos + video):
        // detén con 🎓 y vuelve a empezar. Follow-up: WorkflowTeachSession.RestartAsync.
        SetStatus("Para rehacer: detén la enseñanza y vuelve a empezar.");
    }

    // ── COMPROBAR EL APRENDIZAJE (spec 009) ──────────────────────────────────
    //
    // Ü repite la tarea que le acabas de enseñar y aprende de cada paso. Es OBLIGATORIO antes de
    // poder usarla (decisión del dueño, 2026-09-03): una skill recién enseñada es una hipótesis
    // —los pasos son lo que la demo VIO— y hasta que no se recorren no se sabe si se pueden volver
    // a andar. Ejecutar sin repasar es justo la apuesta que este proyecto lleva dos meses
    // aprendiendo a no hacer.

    private bool _comprobando;

    /// <summary>
    /// La lección que el panel de aprendizajes quiere repasar. Promesa 225.
    /// </summary>
    /// <remarks>
    /// SIN ESTO, «Mostrar» sobre una tarea sin repasar repasaría OTRA: el botón de la carita elige
    /// la última lección grabada, que es lo correcto cuando lo pulsa quien acaba de enseñar, y lo
    /// equivocado cuando alguien señala una tarea concreta de una lista de diecinueve.
    /// </remarks>
    private string? _leccionParaComprobar;

    /// <summary>
    /// EL BOTÓN GRANDE DICE LO QUE VA A PASAR. Con una tarea recién enseñada, lo siguiente no es
    /// ejecutarla —no se puede, comprobar es obligatorio— sino comprobarla; así que el botón lo
    /// dice y lo hace.
    /// </summary>
    /// <remarks>
    /// Nace de perderse buscándolo (2026-09-03, el dueño): la comprobación vivía en un icono del
    /// menú extendido y la acción principal seguía diciendo «Ejecutar ahora», que era justo lo único
    /// que no se podía hacer. Un botón que ofrece lo imposible y esconde lo necesario se prueba tres
    /// veces y se abandona.
    /// </remarks>
    private void PintarBotonDeAccion()
    {
        var (pendiente, _) = SkillPorComprobar();
        _botonComprueba = pendiente != null;
        RunWorkflowBtn.Content = _botonComprueba
            ? $"🧭 Comprobar «{Recorte(pendiente!.Nombre, 26)}»"
            : "▶ Ejecutar ahora";
        if (_botonComprueba) RunWorkflowBtn.IsEnabled = true;
    }

    /// <summary>¿El botón grande comprueba en vez de ejecutar? Lo decide PintarBotonDeAccion.</summary>
    private bool _botonComprueba;

    private async void OnComprobarAprendizaje(object sender, RoutedEventArgs e) => await ComprobarAsync(null);

    /// <summary>
    /// COMPRUEBA, Y DEVUELVE EL VEREDICTO. Lo mismo que hacía el botón, partido para que el panel de
    /// aprendizajes pueda ESPERARLO (spec 019): el 2026-09-11 «Mostrar» lanzó la comprobación, dijo
    /// «la estoy repasando» y volvió en cero segundos; el veredicto salió doce minutos después en la
    /// carita, y el dueño concluyó que «así no funciona la comprobación».
    /// </summary>
    private async Task<string> ComprobarAsync(IProgress<string>? progreso)
    {
        if (_comprobando) { SetStatus("Ya estoy comprobando una tarea."); return "ya estoy comprobando una tarea; espera a que termine."; }
        if (_teaching) { SetStatus("Termina de enseñar primero: pulsa 🎓 para cerrar la grabación."); return "termina de enseñar primero."; }
        if (_loop == null || _mapaDeMano == null) { SetStatus("El piloto no está listo todavía."); ShowTalk(MotivoDelGlobo.AlgoFallo); return "el piloto no está listo todavía."; }

        // CUÁL SE COMPRUEBA: la última enseñada si sigue en memoria; si no, la primera pendiente del
        // catálogo. No se elige «la más nueva» a ciegas: se elige la que le falta el repaso, que es
        // la única que no se puede usar.
        var (skill, archivo) = SkillPorComprobar();
        // LA LECCIÓN MANDA (spec 013): si la demo dejó lección y el piloto está a mano, comprobar es
        // del piloto —un solo cerebro que ve los cuadros, cuelga recuerdos y hace de uno en uno—.
        // La skill vieja sigue siendo el camino cuando no hay lección (demos anteriores a la spec).
        string? carpetaLeccion = _leccionParaComprobar ?? _teachSession?.UltimaLeccion ?? Teach.LeccionEnDisco.Ultima();
        _leccionParaComprobar = null;   // vale para ESTA pulsación; la siguiente vuelve a decidir sola
        bool porElPiloto = carpetaLeccion != null && Piloto.ElPiloto.Disponible();
        if (carpetaLeccion != null && !porElPiloto)
            LogBus.Log("comprobar", "hay lección pero no encuentro agente-piloto/piloto.mjs (U_PILOTO): voy por el camino viejo");
        if (skill == null && !porElPiloto)
        {
            SetStatus("No hay ninguna tarea pendiente de comprobar. Enseña una con 🎓.");
            ShowTalk(MotivoDelGlobo.AlgoFallo);
            return "no hay ninguna tarea pendiente de comprobar.";
        }

        // COMPROBAR ES UN ENCARGO, NO UN GUION (promesa 139). Hasta el 2026-09-03 esto instanciaba
        // los pasos observados y los mandaba al batch: reproducía lo que las manos hicieron y tiraba
        // todo lo demás —el scroll pedido por voz, «baja hasta el final», lo que el video entendió—.
        // El dueño lo vio: «seguimos tratando la enseñanza como si fueran workflows». Ahora el
        // piloto recibe el OBJETIVO y todo el contexto, va hacia él por identidad y con la
        // compuerta, y cuelga un recuerdo de cada elemento que usa. Los pasos son pistas.
        _comprobando = true;
        ShowTalk(MotivoDelGlobo.SoloEsProgreso);
        var reloj = System.Diagnostics.Stopwatch.StartNew();

        // LA VOZ SE ABRE PARA COMPROBAR (promesa 142). Ü va a narrar todo el recorrido, y sin la
        // conversación viva esa narración sale por el sintetizador de Windows: el dueño lo oyó al
        // instante —«habló con una voz diferente»— y tenía razón, eran dos voces para el mismo
        // asistente. Se recuerda si la abrimos NOSOTROS para devolverla como estaba, igual que al
        // enseñar: dejar el micrófono abierto en la cara de alguien que no lo pidió es la avería
        // opuesta.
        bool vozAbiertaParaComprobar = _vivo is { Viva: false };
        if (vozAbiertaParaComprobar)
        {
            try { await _vivo!.ArrancarAsync(); }
            catch (Exception ex)
            {
                vozAbiertaParaComprobar = false;
                LogBus.Log("comprobar", $"sin voz: narraré por escrito ({ex.Message})");
            }
        }

        try
        {
            if (porElPiloto)
                return await ComprobarConElPilotoAsync(carpetaLeccion!, reloj, progreso);
            if (skill == null) return "no hay skill que comprobar.";
            LogBus.Log("comprobar", $"«{skill.Nombre}»: {skill.Pasos.Count} paso(s) de contexto, "
                + $"{skill.Huecos.Count} hueco(s), {skill.Sugerencias.Count} sugerencia(s) · "
                + $"de «{skill.DondeEmpieza}» a «{Navigation.ElEncargoDeComprobar.Destino(skill)}»");

            // LO DICHO DURANTE LA DEMO SE CUELGA ANTES DE ARRANCAR (promesa 124): son recuerdos
            // que ya son verdad —se dijeron sobre elementos que las manos tocaron— y le sirven al
            // piloto desde el primer map_where_am_i. Por la MISMA puerta que la voz (Ensenar), con
            // sus mismas reglas: escribir a mano no es un atajo para meter en el grafo algo que la
            // voz no habría podido meter.
            int colgados = 0, deLoDicho = 0;
            var recuerdos = Navigation.RecuerdosDeUnaSkill.De(
                skill, skill.Description,
                skill.Sugerencias.Select(x => (x.Campo, x.Significado)).ToList());
            foreach (var r in recuerdos)
            {
                if (r.DeDonde == "lo dicho") deLoDicho++;
                _mapaDeMano.Presentar?.Invoke(r.Ubicacion, r.Selector, r.Selector, "");
                if (_mapaDeMano.Ensenar?.Invoke(r.Ubicacion, r.Selector, r.Significado, "") == true) colgados++;
                else LogBus.Log("comprobar", $"no pude colgar el recuerdo de «{r.Selector}» en «{r.Ubicacion}»");
            }

            // EL ENCARGO, atado a la app donde vive la tarea: la misma compuerta de superficie que
            // usa el puente del workflow (2026-07-26: sin ella el consciente tecleó en otra app).
            string encargo = Navigation.ElEncargoDeComprobar.Texto(skill);
            string origen = _locator?.DondeEstoy()?.Origin ?? "";
            LogBus.Log("comprobar", $"encargo de {encargo.Length} car."
                + (origen.Length > 0 ? $" · atado a «{origen}»" : " · SIN origen conocido: va sin compuerta"));

            SetStatus($"Comprobando «{skill.Nombre}»: voy a hacer la tarea yo…");
            // QUE SE VEA DE QUÉ HABLA: cada elemento que vaya a tocar se enciende y la carita se
            // pone a su lado (petición del dueño, 2026-09-03).
            if (_mapaDeMano != null) _mapaDeMano.SenalarAlActuar = true;
            _cts = new CancellationTokenSource();
            ShowStop(true);
            SetWorking(true);
            string relato;
            try { relato = await _loop.RunAsync(encargo, _cts.Token, origen); }
            finally { SetWorking(false); ShowStop(false); }

            // EL VEREDICTO LO DA LA COMPUERTA, NO EL MODELO (promesa 121): «terminé» no es un
            // veredicto. Se compara dónde acabó la demo con dónde está el piloto AHORA.
            string destino = Navigation.ElEncargoDeComprobar.Destino(skill);
            string aqui = _locator?.DondeEstoy()?.Id ?? "";
            var aterrizaje = Navigation.ElRescate.Aterrizo(destino, aqui);
            LogBus.Log("comprobar", $"piloto: {relato}");
            LogBus.Log("comprobar", aterrizaje.Llego
                ? $"ATERRIZÓ en «{destino}»"
                : $"NO aterrizó: {aterrizaje.Motivo}");

            // LO APRENDIDO SE QUEDA PASE LO QUE PASE; lo que depende del veredicto es el SELLO.
            if (aterrizaje.Llego)
                skill.ConLaComprobacionHecha().Guardar(Navigation.SkillEnsenada.CarpetaPorDefecto);

            string cuentaRecuerdos = Navigation.RecuerdosDeUnaSkill.Cuenta(
                skill.Description, deLoDicho, colgados - deLoDicho);
            string cuentaFinal = aterrizaje.Llego
                ? $"«{skill.Nombre}» comprobada: llegué a donde acabó la demo · {cuentaRecuerdos} Ya se puede usar."
                : $"«{skill.Nombre}» SIGUE PENDIENTE: {aterrizaje.Motivo} · {cuentaRecuerdos} "
                    + "Enséñamela otra vez o vuelve a comprobar desde la pantalla de partida.";
            SetStatus(cuentaFinal);
            LogBus.Log("comprobar", $"«{skill.Nombre}» "
                + (aterrizaje.Llego ? "COMPROBADA" : "SIGUE PENDIENTE")
                + $" en {reloj.ElapsedMilliseconds} ms · {colgados} recuerdo(s) colgado(s) de "
                + $"{recuerdos.Count} antes de arrancar · {archivo}");
            return cuentaFinal;
        }
        catch (OperationCanceledException)
        {
            SetStatus($"Paraste la comprobación de «{skill?.Nombre ?? "la lección"}»: sigue pendiente.");
            LogBus.Log("comprobar", "parada por el usuario: sigue pendiente");
            return "paraste la comprobación: sigue pendiente.";
        }
        catch (Exception ex)
        {
            var porque = new System.Text.StringBuilder();
            for (var x = ex; x != null; x = x.InnerException)
                porque.Append(porque.Length > 0 ? " ← " : "").Append($"{x.GetType().Name}: {x.Message}");
            SetStatus($"La comprobación se detuvo: {ex.Message}");
            LogBus.Log("comprobar", $"reventó: {porque}");
            return $"la comprobación se detuvo: {ex.Message}";
        }
        finally
        {
            // EL ORDEN IMPORTA: primero se baja la bandera —para que lo que quede por narrar deje de
            // ir a la voz— y después se cierra lo que abrimos.
            _comprobando = false;
            if (_mapaDeMano != null) _mapaDeMano.SenalarAlActuar = false;
            if (vozAbiertaParaComprobar && _vivo is { Viva: true })
            {
                try { await _vivo.TerminarAsync(); }
                catch (Exception ex) { LogBus.Log("comprobar", $"no pude cerrar la voz: {ex.Message}"); }
            }
            PintarBotonDeAccion();
        }
    }

    /// <summary>
    /// COMPROBAR CON EL PILOTO (spec 013): un agente Claude lee la lección, cuelga recuerdos, hace la
    /// tarea de uno en uno por MCP y la app juzga cada llegada. Corre dentro del try/finally de
    /// <see cref="OnComprobarAprendizaje"/>, que es quien abre y cierra la voz.
    /// </summary>
    private async Task<string> ComprobarConElPilotoAsync(string carpetaLeccion, System.Diagnostics.Stopwatch reloj,
        IProgress<string>? progreso = null)
    {
        var leccion = Teach.LeccionEnDisco.Cargar(carpetaLeccion);
        if (leccion == null || _mapaDeMano == null)
        {
            SetStatus("La lección no se pudo leer: no hay nada que comprobar.");
            ShowTalk(MotivoDelGlobo.AlgoFallo);
            return "la lección no se pudo leer: no hay nada que comprobar.";
        }
        // EL JUEZ LEE LOS CAMPOS (promesa 175, enmendada): un campo tecleado está hecho si dice ahora lo
        // que la demo tecleó. Se lee por SAP; un campo de UIA no se sabe leer aquí, y el juez lo dice.
        var registro = new Piloto.RegistroDeLaComprobacion(leccion, sel =>
            U.Graph.Surfaces.SapSelector.Owns(sel) ? _locator?.SuperficieSap?.ValorActual(sel) : null);
        string modelo = ModeloDelPiloto();
        var mensaje = new Piloto.ElPiloto.Mensaje(
            Teach.MensajeDeLaLeccion.Armar(leccion),
            Piloto.CajasDelPiloto.Caja(_nombresMcp),
            Piloto.CajasDelPiloto.Prohibidas(_nombresMcp),
            modelo, $"http://127.0.0.1:{Mcp.ServidorMcp.Puerto}/mcp/");
        string rutaMensaje = Piloto.ElPiloto.EscribirMensaje(carpetaLeccion, mensaje);
        LogBus.Log("comprobar", $"lección «{leccion.Id}»: {leccion.Eventos.Count} evento(s), "
            + $"{registro.Total} que navegan, {mensaje.Bloques.Count(b => b.Tipo == "image")} cuadro(s) en el mensaje · "
            + $"modelo {modelo} · {rutaMensaje}");

        // LO QUE EL PILOTO PUEDE PEDIRLE A LA VENTANA. Corren en el hilo del servidor MCP, nunca en
        // el de la UI: la pregunta BLOQUEA hasta que la persona contesta, y eso en la UI congelaría
        // la carita.
        _mapaDeMano.Decir = DecirPorLaVozPrestada;
        _mapaDeMano.Preguntar = texto => PreguntarYEsperar(texto, TimeSpan.FromSeconds(60));
        _mapaDeMano.Llegue = n =>
        {
            string aqui = _locator?.DondeEstoy()?.Id ?? "";
            var v = registro.Llegue(n, aqui);
            return v.Aterrizo
                ? $"ATERRIZASTE: el evento {n} llegó a «{v.Esperada}». Sigue con el siguiente."
                : $"NO ATERRIZÓ el evento {n}: {v.Motivo}";
        };
        _mapaDeMano.Plan = json => RecorrerElPlan(json, leccion, registro);
        _mapaDeMano.GuardarSkill = (nombre, descripcion) =>
        {
            var s = Piloto.SkillDeLoVerificado.Empaquetar(leccion, registro.Veredictos, nombre, descripcion);
            if (s == null) return "no hay pasos verificados con identidad: no se guarda ninguna skill.";
            string f = s.GuardarComoElUnicoDeSuLeccion(Navigation.SkillEnsenada.CarpetaPorDefecto);   // promesa 228
            return $"skill «{s.Nombre}» guardada con {s.Pasos.Count} paso(s) verificado(s)"
                + (s.Comprobada ? ", COMPROBADA" : ", pendiente: no todos los eventos aterrizaron") + $" → {f}";
        };

        // LA VOZ PRESTADA (promesa 192): mientras el piloto comprueba, la conversación en vivo no
        // tiene herramientas ni turno propio; solo dice lo que se le pide.
        await PrestarLaVozAlPilotoAsync("comprobar");
        SetStatus("Comprobando con el piloto: leo la lección y la hago de uno en uno…");
        _mapaDeMano.SenalarAlActuar = true;
        _cts = new CancellationTokenSource();
        ShowStop(true);
        SetWorking(true);
        Piloto.ElPiloto.Resultado r;
        try
        {
            r = await Piloto.ElPiloto.CorrerAsync(carpetaLeccion, (tipo, texto) =>
            {
                if (tipo == "texto" && texto.Length > 0)
                {
                    string corto = texto.Length > 160 ? texto[..160] + "…" : texto;
                    Dispatcher.Invoke(() => SetStatus(corto));
                    progreso?.Report(corto);
                }
            }, _cts.Token);
        }
        finally
        {
            SetWorking(false); ShowStop(false);
            _mapaDeMano.Decir = null; _mapaDeMano.Preguntar = null; _mapaDeMano.Llegue = null; _mapaDeMano.GuardarSkill = null; _mapaDeMano.Plan = null;
            _mapaDeMano.SenalarAlActuar = false;
            await DevolverLaVozAsync("comprobar");   // promesa 192: la conversación vuelve a ser quien era
        }

        // EL VEREDICTO LO DA LA APP, con la misma compuerta de la promesa 131: el total es el plan.
        var final = registro.Final();
        LogBus.Log("comprobar", $"piloto terminó ({(r.Termino ? "bien" : $"salida {r.Salida}")}) en {reloj.ElapsedMilliseconds} ms · "
            + $"{registro.Hechos}/{registro.Total} hechos · costo estimado ${r.CostoUsd:0.000} · "
            + (final.Comprobada ? "COMPROBADA" : "SIGUE PENDIENTE") + $" · {final.Motivo}");
        if (!r.Termino && r.Ultimo.Length > 0) LogBus.Log("comprobar", $"piloto: {r.Ultimo}");
        string veredicto = final.Comprobada
            ? $"Comprobada: {final.Motivo}"
            : $"Sigue pendiente: {final.Motivo}" + (r.Termino ? "" : $" · el piloto no terminó bien ({r.Ultimo})");
        SetStatus(veredicto);
        return veredicto;
    }

    /// <summary>
    /// RECORRER EL PLAN DEL PILOTO (promesa 179): por cada paso, la voz, el recuerdo donde vive el
    /// elemento, el paso por el MISMO ejecutor de tanda, y el juez. Para donde no pueda.
    /// </summary>
    /// <remarks>
    /// Corre en el hilo del servidor MCP mientras el piloto espera la respuesta; por eso hay un techo
    /// de tiempo por herramienta en <c>ProtocoloMcp</c> (120 s) y aquí se para antes de agotarlo.
    /// </remarks>
    private string RecorrerElPlan(string json, Teach.Leccion leccion, Piloto.RegistroDeLaComprobacion registro)
    {
        var lectura = Piloto.PlanDeComprobacion.Leer(json);
        if (lectura.Error.Length > 0) return lectura.Error;
        if (_mapaDeMano?.RecorrerPorElNucleo == null) return "todavía no sé recorrer un plan.";
        var pasos = lectura.Pasos;
        LogBus.Log("comprobar", $"plan del piloto: {pasos.Count} paso(s) → "
            + string.Join(" → ", pasos.Select(p => p.Texto.Length > 0 ? $"escribir «{p.Texto}» en «{p.Exit}»" : $"«{p.Exit}»")));
        var reloj = System.Diagnostics.Stopwatch.StartNew();
        int hechos = 0;
        for (int i = 0; i < pasos.Count; i++)
        {
            var p = pasos[i];
            // LA GUARDA CRECE CON EL PLAN (2026-09-08): 18 pasos con su tarjeta de lectura pasan de
            // 100 s, y el corte cayó justo en el paso 18. Ocho segundos por paso, y nunca menos de 100.
            if (reloj.Elapsed > TimeSpan.FromSeconds(Math.Max(100, 8 * pasos.Count)))
                return Piloto.PlanDeComprobacion.Relato(hechos, pasos.Count, i + 1,
                    "se me acabó el tiempo de una sola llamada; el resto lo sigues tú o me vuelves a mandar el plan desde aquí.",
                    _locator?.DondeEstoy()?.Id ?? "", registro.Hechos, registro.Total, LoQueFalta(registro));

            // LA IDENTIDAD DEL PASO SE DECIDE EN UN SITIO (promesa 191): se señala y se nombra por la
            // puerta aunque el piloto traiga el selector; se escribe por el selector de la lección
            // aunque traiga el nombre. La llegada viaja con el paso: es la que el terreno aprendió, y
            // el batch la verifica con su propia compuerta (promesas 103 y 122).
            var evento = p.N > 0 ? leccion.Eventos.FirstOrDefault(e => e.N == p.N) : null;
            var id = Piloto.ElPasoQueSeDa.Resolver(p.Exit, evento?.Etiqueta ?? "", evento?.Selector ?? "", p.Texto);
            var res = DarUnPasoConCoreografia(id.ParaSenalar,
                new Navigation.RecorrerSegunElNucleo.Paso(id.ParaElEjecutor, p.Texto, evento?.Llegada ?? "", p.Tecla), p.Recuerdo, p.Decir);
            if (res.Hechos < 1)
            {
                LogBus.Log("comprobar", $"plan · PARÓ en el paso {i + 1} «{id.ParaSenalar}»: {res.Cuenta}");
                return Piloto.PlanDeComprobacion.Relato(hechos, pasos.Count, i + 1, res.Cuenta,
                    _locator?.DondeEstoy()?.Id ?? "", registro.Hechos, registro.Total, LoQueFalta(registro));
            }
            hechos++;
            if (p.N > 0) registro.Llegue(p.N, _locator?.DondeEstoy()?.Id ?? "");
        }
        LogBus.Log("comprobar", $"plan · hice los {pasos.Count} paso(s) en {reloj.ElapsedMilliseconds} ms · {registro.Hechos}/{registro.Total} hechos");
        return Piloto.PlanDeComprobacion.Relato(hechos, pasos.Count, 0, "", _locator?.DondeEstoy()?.Id ?? "", registro.Hechos, registro.Total, LoQueFalta(registro));
    }

    /// <summary>Lo que el juez todavía no da por hecho, dicho para el piloto: evento, nombre y motivo.</summary>
    private static string LoQueFalta(Piloto.RegistroDeLaComprobacion registro) =>
        string.Join("; ", registro.Pendientes().Select(p => $"evento {p.N} «{p.Que}» ({p.Motivo})"));

    /// <summary>
    /// DAR UN PASO CON SU COREOGRAFÍA. Promesas 180 y 191: la carita al lado, el elemento encendido,
    /// se dice, el recuerdo escrito y a la vista, y SOLO DESPUÉS el toque; al tocar, la tarjeta se
    /// cierra y la señal se suelta. El orden lo decide <see cref="Piloto.ElRecuerdoQueSeVe.Coreografia"/>.
    /// </summary>
    /// <remarks>
    /// ES UNA SOLA FUNCIÓN A PROPÓSITO (2026-09-08): vivía dentro del recorrido del plan, y cuando el
    /// plan paraba y el piloto seguía con las manos, la experiencia desaparecía: ni carita, ni voz, ni
    /// tarjeta. El dueño lo vio en la undécima prueba: «quiero que esa sea la experiencia estándar que
    /// siempre suceda». Ahora map_take y map_type pasan por aquí.
    /// </remarks>
    private Navigation.RecorrerSegunElNucleo.Resultado DarUnPasoConCoreografia(string senalar,
        Navigation.RecorrerSegunElNucleo.Paso paso, string recuerdo, string decir)
    {
        var mano = _mapaDeMano!;
        recuerdo ??= ""; decir ??= ""; senalar ??= "";
        bool hayElemento = senalar.Length > 0 && mano.SenalarElemento(senalar, senalar);
        // FUERA DE UNA COMPROBACIÓN NO HAY TARJETA NI PAUSA (promesa 266): la coreografía de la 180 es para cuando la
        // persona está viendo una lección; en un clic normal quería ver la carita al lado y el clic, en un solo gesto.
        bool enComprobacion = mano.Llegue != null;
        var coreografia = Piloto.ElRecuerdoQueSeVe.Coreografia(hayElemento, recuerdo.Length > 0 && senalar.Length > 0, decir.Length > 0, enComprobacion);
        if (!hayElemento && senalar.Length > 0) LogBus.Log("comprobar", $"paso · «{senalar}» no está en pantalla para señalarlo: va al ejecutor sin tarjeta");
        foreach (var gesto in coreografia)
        {
            switch (gesto)
            {
                case Piloto.ElRecuerdoQueSeVe.Gesto.Senalar: break; // ya hecho arriba: es lo que dice si hay elemento
                case Piloto.ElRecuerdoQueSeVe.Gesto.Decir:
                    if (mano.Decir != null) { try { mano.Decir(decir); } catch { } }
                    break;
                case Piloto.ElRecuerdoQueSeVe.Gesto.Escribir:
                {
                    string r = mano.Call("map_esto_es", new Dictionary<string, string> { ["significado"] = recuerdo, ["sobre"] = senalar });
                    LogBus.Log("comprobar", $"paso · recuerdo en «{senalar}»: {(r.Length > 120 ? r[..120] + "…" : r)}");
                    break;
                }
                case Piloto.ElRecuerdoQueSeVe.Gesto.Mostrar:
                    if (Senalador.Actual is { } senalado)
                        TarjetasDeRecuerdo.Mostrar(new[] { (senalado.Caja, senalar, senalar, recuerdo) });
                    break;
                case Piloto.ElRecuerdoQueSeVe.Gesto.Esperar:
                    Thread.Sleep(Piloto.ElRecuerdoQueSeVe.TiempoDeLectura(recuerdo));
                    break;
                case Piloto.ElRecuerdoQueSeVe.Gesto.Cerrar: TarjetasDeRecuerdo.Cerrar(); break;
                case Piloto.ElRecuerdoQueSeVe.Gesto.Soltar: Senalador.Soltar(); break;
                case Piloto.ElRecuerdoQueSeVe.Gesto.Actuar: break; // el paso va abajo, con su veredicto
            }
            if (gesto == Piloto.ElRecuerdoQueSeVe.Gesto.Actuar) break;
        }
        var res = mano.RecorrerPorElNucleo!(new[] { paso });
        if (res.Hechos < 1)
        {
            TarjetasDeRecuerdo.Cerrar(); Senalador.Soltar();
            return res;
        }
        // Lo que va después de tocar: la tarjeta se cierra y la señal se suelta (180).
        foreach (var gesto in coreografia.SkipWhile(g => g != Piloto.ElRecuerdoQueSeVe.Gesto.Actuar).Skip(1))
        {
            if (gesto == Piloto.ElRecuerdoQueSeVe.Gesto.Cerrar) TarjetasDeRecuerdo.Cerrar();
            if (gesto == Piloto.ElRecuerdoQueSeVe.Gesto.Soltar) Senalador.Soltar();
            // EL RECUERDO SE ESCRIBE DESPUÉS DE TOCAR fuera de una comprobación (promesa 266): lo que el modelo quiso
            // recordar se guarda igual, pero no se paga antes de lo que la persona pidió.
            if (gesto == Piloto.ElRecuerdoQueSeVe.Gesto.Escribir)
            {
                string r = mano.Call("map_esto_es", new Dictionary<string, string> { ["significado"] = recuerdo, ["sobre"] = senalar });
                LogBus.Log("comprobar", $"paso · recuerdo tras tocar en «{senalar}»: {(r.Length > 120 ? r[..120] + "…" : r)}");
            }
        }
        return res;
    }

    /// <summary>Pregunta con la voz de Ü y espera la siguiente frase de la persona, con techo.</summary>
    private string PreguntarYEsperar(string pregunta, TimeSpan techo)
    {
        if (_vivo is not { Viva: true })
        {
            Dispatcher.Invoke(() => SetStatus($"Pregunta: {pregunta}"));
            return "no hay voz abierta: la pregunta quedó escrita y nadie pudo contestarla. Decide con lo que ves.";
        }
        var respuesta = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<string> oyente = frase => { if (!string.IsNullOrWhiteSpace(frase)) respuesta.TrySetResult(frase.Trim()); };
        _vivo.DijoElUsuario += oyente;
        try
        {
            _vivo.DiEstoAsync(pregunta).GetAwaiter().GetResult();
            if (respuesta.Task.Wait(techo)) return $"la persona dijo: «{respuesta.Task.Result}»";
            return $"la persona no contestó en {techo.TotalSeconds:0} s. Decide con lo que ves y dilo.";
        }
        catch (Exception ex) { return $"no pude preguntar por la voz: {ex.Message}"; }
        finally { _vivo.DijoElUsuario -= oyente; }
    }

    /// <summary>
    /// Cuál toca comprobar: la última enseñada, o la primera del catálogo que siga pendiente.
    /// </summary>
    /// <remarks>
    /// NO SE ELIGE «LA MÁS NUEVA» sin mirar: se elige la que le falta el repaso, porque es la única
    /// que no se puede usar. Si todas están comprobadas, no hay nada que hacer y se dice.
    /// </remarks>
    private (Navigation.SkillEnsenada? Skill, string Archivo) SkillPorComprobar()
    {
        var ultima = _teachSession?.UltimaSkill;
        if (ultima is { Comprobada: false }) return (ultima, "(la recién enseñada)");

        foreach (var c in Navigation.SkillEnsenada.Catalogo(Navigation.SkillEnsenada.CarpetaPorDefecto))
        {
            if (c.Comprobada) continue;
            var s = Navigation.SkillEnsenada.Cargar(c.Archivo);
            if (s != null) return (s, c.Archivo);
        }
        return (null, "");
    }

    /// <summary>El oído prestado a la enseñanza mientras dura, para poder soltarlo al parar.</summary>
    private Action<string>? _oyendoParaEnsenar;

    /// <summary>¿Abrimos NOSOTROS el micrófono para enseñar? Entonces al terminar se cierra.</summary>
    private bool _vozAbiertaParaEnsenar;

    private async Task StopTeachingAsync()
    {
        SetTeachingUi(false);
        // SE SUELTA EL OÍDO. Un manejador que sobrevive a su sesión seguiría metiendo frases en una
        // demo que ya terminó — y peor, en la siguiente.
        if (_oyendoParaEnsenar != null && _vivo != null)
        {
            try { _vivo.DijoElUsuario -= _oyendoParaEnsenar; } catch { }
            _oyendoParaEnsenar = null;
        }
        // Y el micrófono vuelve como estaba: solo se cierra si lo abrimos para esto.
        // Y VUELVE A SER EL ASISTENTE, con todas sus herramientas: el modo aprendiz dura lo que
        // dura la demo. Se hace ANTES de decidir si se cierra el micrófono: si se queda abierto,
        // tiene que quedarse siendo quien era.
        if (_vivo is { Viva: true })
        {
            try
            {
                await _vivo.CambiarModoAsync(Voice.ConversacionEnVivo.InstruccionesNormales,
                    Voice.ConversacionEnVivo.Herramientas());
            }
            catch (Exception ex) { LogBus.Log("teach", $"no pude devolver la voz a asistente: {ex.Message}"); }
        }

        if (_vozAbiertaParaEnsenar && _vivo is { Viva: true })
        {
            try { await _vivo.AlternarAsync(); } catch (Exception ex) { LogBus.Log("teach", $"no pude cerrar el micrófono: {ex.Message}"); }
        }
        _vozAbiertaParaEnsenar = false;
        SetStatus("Cerrando la enseñanza y estructurando el workflow…");
        ShowTalk(MotivoDelGlobo.SoloEsProgreso); // el cierre tarda y termina en un veredicto: que no pase en silencio

        if (_teachSession != null)
        {
            try
            {
                FinishResponse finish = await _teachSession.StopAsync(CancellationToken.None);
                // Sin voz aquí a propósito: el resultado se muestra en silencio en el estado.
                // El nombre lo pone el selector al recargar (derivado o propio), no el summary del
                // LLM: ese llegaba como «User workflow summary:» (spec 007).
                _nuevoWorkflowId = finish.WorkflowId;
                await ReloadDirectWorkflowsAsync(); // el recién enseñado queda elegido y con el cuadro de nombre abierto
                SetStatus(_directWorkflows.Count > 0 && _directIndex < _directWorkflows.Count
                    ? $"Aprendido: «{_directWorkflows[_directIndex].Nombre}». Ponle nombre y pulsa Enter."
                    : "Aprendido.");
            }
            catch (FinishPendingException pending)
            {
                // NO es lo mismo que perder la grabación, y decirlo importa: los pasos ya están en
                // Graph. Se guarda el id para completar el resumen luego, sin regrabar nada.
                PendingFinish.Save(pending.SessionId, pending.WorkflowId);
                LogBus.Log("teach", $"cierre pendiente (HTTP {pending.StatusCode}): {pending.Message}");
                SetStatus("Los pasos SÍ se guardaron; falta el resumen (Graph tardó de más). "
                        + "Se completa solo al reabrir la app.");
                _nuevoWorkflowId = pending.WorkflowId;
                _ = ReloadDirectWorkflowsAsync(); // el workflow existe aunque le falte el resumen
            }
            catch (Exception ex)
            {
                SetStatus($"Error en enseñanza: {ex.Message}");
            }
            finally
            {
                await _teachSession.DisposeAsync();
                _teachSession = null;
            }
        }
    }

    private void OnOpenVideos(object sender, RoutedEventArgs e)
    {
        if (_videoWindow == null || !_videoWindow.IsLoaded)
            _videoWindow = new VideoLibraryWindow(_videoLibrary);
        else
            _videoWindow.Reload();

        _videoWindow.Show();
        _videoWindow.Activate();
    }

    private void OnOpenWorkflows(object sender, RoutedEventArgs e)
    {
        if (_workflowWindow == null || !_workflowWindow.IsLoaded)
            _workflowWindow = new WorkflowLibraryWindow(_graphConfig, _backend!, _videoLibrary, _config.UserId);

        _workflowWindow.Show();
        _workflowWindow.Activate();
    }

    // --- Toggle: procesar (o no) el video con IA al enseñar ---

    /// <summary>
    /// El estado de un toggle-icono se dice con el FONDO: acento = encendido. Un interruptor que se
    /// ve igual encendido que apagado obliga a mirar la pantalla para saber si funcionó.
    /// </summary>
    /// <remarks>
    /// Hasta el 2026-09-06 el detalle iba en un cartel al pasar el ratón. Se fue con todos los demás
    /// (promesa 164): lo que un botón hace se dice en la píldora de estado al pulsarlo, que es
    /// cuando hace falta saberlo.
    /// </remarks>
    private static void PintarToggle(System.Windows.Controls.Button btn, bool on)
    {
        // DEL ESTUDIO Y NO A MANO: el azul al 53 % y el blanco al 10 % nacieron sobre el panel NEGRO
        // de antes. Sobre el blanco de ahora, el encendido grita y el apagado no existe — que es
        // justo lo contrario de lo que este método promete (2026-09-14).
        btn.Background = on ? Estudio.AcentoSuave : System.Windows.Media.Brushes.Transparent;
        btn.Foreground = on ? Estudio.Acento : Estudio.Tinta;
    }



    // ── Menú extendido: activador con hover, cierre con retraso, fijado por clic ──────────────
    //
    // Reglas (calcadas del brief del rediseño):
    //  · Hover sobre el activador abre el menú HACIA ARRIBA (la ventana crece anclada abajo-derecha).
    //  · Mientras el cursor esté sobre el menú O sobre la barra, no se cierra: la barra entera es la
    //    zona segura entre activador y panel, así el trayecto nunca lo cierra por accidente.
    //  · Al salir del todo, espera ~320 ms; si el cursor vuelve antes, se cancela el cierre.
    //  · Clic (o Enter/Espacio) en el activador lo FIJA: ya no se cierra por hover-out, solo con
    //    Esc, otro clic, o al colapsar la carita.
    //  · Backend, dentro del menú, se abre y se cierra SOLO CON CLIC (o Enter/Espacio). Antes repetía
    //    el patrón de hover en miniatura y era indistinguible de un fallo: rozarlo lo abría, y el clic
    //    caía sobre una sección que el hover ya había abierto, así que no cambiaba nada visible —
    //    parecía que el control no detectaba el clic (2026-08-08). Es la misma corrección que ya se
    //    le hizo al menú principal, por la misma razón: abrir por roce no se pide, se sufre.

    private bool _menuOpen, _menuPinned;
    private AtajoPorGolpes? _golpes;

    // ── El panel de desarrollo, en el centro y por su cuenta ──────────────────────────────────

    private Window? _panelSuelto;
    private (HorizontalAlignment H, VerticalAlignment V, Thickness M)? _panelComoEstaba;

    /// <summary>
    /// Saca el panel a una ventana propia, centrada en la pantalla, y lo devuelve al cerrarlo.
    /// </summary>
    /// <remarks>
    /// El panel nació pegado a la barra —era «la continuación de la barra»— y por eso abrirlo
    /// obligaba a desplegar la barra entera. Pero desde que se pide con un atajo ya no es la
    /// continuación de nada: es una herramienta que se convoca, y una herramienta que se convoca
    /// aparece donde estás mirando, que es el centro (2026-08-07, pedido por el usuario).
    ///
    /// Se REAPROVECHA el mismo control, no se hace una copia: se saca de su sitio y se mete en la
    /// ventana nueva. Duplicar el panel significaría mantener dos, y el día que alguien añada un
    /// botón lo añadirá en uno solo. Todos los manejadores y los nombres siguen apuntando al mismo
    /// objeto, así que lo de dentro sigue funcionando sin tocar una línea.
    /// </remarks>
    private void AlternarPanelDesarrollo()
    {
        if (_panelSuelto != null) { CerrarPanelDesarrollo(); return; }

        _panelComoEstaba = (MenuPanel.HorizontalAlignment, MenuPanel.VerticalAlignment, MenuPanel.Margin);
        RootPanel.Children.Remove(MenuPanel);
        MenuPanel.HorizontalAlignment = HorizontalAlignment.Center;
        MenuPanel.VerticalAlignment = VerticalAlignment.Center;
        MenuPanel.Margin = new Thickness(0);
        MenuPanel.Visibility = Visibility.Visible;

        _panelSuelto = new Window
        {
            Title = "Ü",
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.Manual,
            ResizeMode = ResizeMode.NoResize,
            Content = MenuPanel,
        };
        // Que quepa: si el panel crece más que la pantalla, quien cede es su scroll interno.
        MenuPanel.MaxHeight = SystemParameters.WorkArea.Height * 0.86;
        _panelSuelto.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) CerrarPanelDesarrollo(); };
        _panelSuelto.Show();

        // Centrar DESPUÉS de mostrarlo: hasta que no se mide, no se sabe cuánto ocupa.
        var wa = SystemParameters.WorkArea;
        _panelSuelto.Left = wa.Left + (wa.Width - _panelSuelto.ActualWidth) / 2;
        _panelSuelto.Top = wa.Top + (wa.Height - _panelSuelto.ActualHeight) / 2;
        _panelSuelto.Activate();
    }

    private void CerrarPanelDesarrollo()
    {
        if (_panelSuelto == null) return;

        var ventana = _panelSuelto;
        _panelSuelto = null;
        ventana.Content = null;
        try { ventana.Close(); } catch { }

        // De vuelta a su sitio y con su forma de antes: la ventana de la carita lo espera en su
        // fila, y dejarlo con la alineación del centro lo descolocaría la próxima vez.
        MenuPanel.MaxHeight = double.PositiveInfinity;
        MenuPanel.Visibility = Visibility.Collapsed;
        if (_panelComoEstaba is { } antes)
        {
            MenuPanel.HorizontalAlignment = antes.H;
            MenuPanel.VerticalAlignment = antes.V;
            MenuPanel.Margin = antes.M;
            _panelComoEstaba = null;
        }
        if (!RootPanel.Children.Contains(MenuPanel)) RootPanel.Children.Add(MenuPanel);
    }
    private bool _talkOpen;
    private System.Windows.Threading.DispatcherTimer _menuOpenTimer = null!, _menuCloseTimer = null!;

    /// <summary>Conecta toda la coreografía del menú. Se llama una vez, desde OnLoaded.</summary>
    private void WireMenu()
    {
        // Pausa de intención al abrir por hover: pasar de largo por el activador no abre nada.
        _menuOpenTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(110) };
        _menuOpenTimer.Tick += (_, __) => { _menuOpenTimer.Stop(); if (MenuActivator.IsMouseOver) OpenMenu(pin: false); };

        _menuCloseTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(320) };
        _menuCloseTimer.Tick += (_, __) =>
        {
            _menuCloseTimer.Stop();
            if (_menuOpen && !_menuPinned && !MenuPanel.IsMouseOver && !BarPanel.IsMouseOver) CloseMenu();
        };

        // EL PANEL YA NO CUELGA DE LA FLECHA. Lo que hay dentro —ejecutar workflows, ensayo en seco,
        // paso a paso, la consulta del portal, el backend— son herramientas de DESARROLLO, y estaban
        // a un hover de distancia de quien solo quiere hablar con Ü: bastaba rozar la barra para que
        // se desplegara media pantalla de controles que esa persona no va a usar nunca
        // (2026-08-06). Ahora se pide a propósito, con Ctrl+Shift dos veces seguidas.
        //
        // La flecha se queda para lo que ya hacía por sí sola: decir hacia dónde crecería el panel.
        MenuActivator.Visibility = Visibility.Collapsed;

        // Ctrl+Shift dos veces seguidas abre y cierra el panel. Global: se puede pedir sin soltar la
        // aplicación en la que se esté trabajando, que es cuando de verdad hace falta.
        // Ninguno de los dos convierte la carita en barra: la carita se queda como está y lo que se
        // pidió aparece por su cuenta. Abrir el micrófono es EXACTAMENTE lo del doble clic sobre la
        // cara —el mismo gesto, dicho con el teclado— y el panel es una ventana aparte, centrada
        // (2026-08-07, pedido por el usuario).
        // Y TRIPLE Ctrl pasa la voz al collar Omi. El doble sigue abriendo el micrófono como siempre,
        // así que lo que se ve al hacer triple es la conversación abriéndose y mudándose al collar —
        // el gesto nuevo no le cobra ni un milisegundo de espera al que ya funcionaba.
        EngancharCollar();

        _golpes = new AtajoPorGolpes(
            soloCtrl: () => Dispatcher.BeginInvoke(() => DobleCtrl()),
            ctrlShift: () => Dispatcher.BeginInvoke(() => AlternarPanelDesarrollo()),
            tripleCtrl: () => Dispatcher.BeginInvoke(() =>
            {
                PasarLaVozAlCollar();
                // Si no había conversación, el triple la abre: pedir el collar sin nada que oír
                // dejaría el gesto sin efecto visible y parecería que no funcionó.
                if (_vivo?.Viva != true) StartMicByFace();
            }));
        Closed += (_, __) => { _golpes?.Dispose(); CerrarPanelDesarrollo(); };
        WireLaLinea();

        // Zona segura: menú y barra cancelan el cierre al entrar y lo agendan al salir.
        MenuPanel.MouseEnter += (_, __) => _menuCloseTimer.Stop();
        MenuPanel.MouseLeave += (_, __) => ScheduleMenuClose();
        BarPanel.MouseEnter += (_, __) => _menuCloseTimer.Stop();
        BarPanel.MouseLeave += (_, __) => ScheduleMenuClose();

        // Globo de conversación: ✕ lo cierra; abrirlo va por las pastillas de la carita.
        TalkCloseBtn.Click += (_, __) => HideTalk();

        // Esc cierra lo más volátil primero: menú, luego conversación. Y al cerrar la conversación
        // devuelve el teclado a la aplicación desde la que se invocó a Ü con el atajo.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            if (_menuOpen) { CloseMenu(); e.Handled = true; }
            else if (_talkOpen) { HideTalk(); DevolverElFoco(); e.Handled = true; }
        };

        // Clic fuera (en otra app estando esta ventana activa): el menú sin fijar se cierra.
        // El fijado sobrevive a propósito — fijar es pedir que se quede mientras trabajas al lado.
        Deactivated += (_, __) => { if (_menuOpen && !_menuPinned) CloseMenu(); };

        // Timers muertos al cerrar: un DispatcherTimer vivo mantiene la ventana en memoria. Y la
        // posición se escribe ya, sin esperar al debounce: si el usuario mueve la barra y cierra Ü
        // en menos de 700 ms, ese último movimiento se perdería.
        Closed += (_, __) =>
        {
            _menuOpenTimer.Stop(); _menuCloseTimer.Stop();
            if (_saveTimer != null) { _saveTimer.Stop(); _config.Save(); }
        };

        // A partir del primer ciclo ocioso, el foco ya es intención del usuario y no reparto inicial.
        Dispatcher.BeginInvoke(new Action(() => _uiReady = true),
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private bool _uiReady;

    private void ScheduleMenuClose()
    {
        if (!_menuOpen || _menuPinned) return;
        _menuCloseTimer.Stop();
        _menuCloseTimer.Start();
    }

    // ── De qué lado de la pantalla vive Ü ─────────────────────────────────────────────────────
    //
    // Todo el layout nació asumiendo «la barra está a la derecha»: el menú crece hacia la izquierda,
    // el globo se pone a la izquierda, la píldora también, y la ventana se ancla por su borde
    // derecho. Con la barra pegada al borde IZQUIERDO, todo eso apunta fuera de la pantalla.
    //
    // Así que el lado no es solo una posición: es un espejo del layout.

    private bool _barLeft;

    private void ApplyBarSide(bool left)
    {
        if (_barLeft == left && _sideApplied) return;
        _barLeft = left;
        _sideApplied = true;

        // En un DockPanel el orden de los hijos decide qué franja ocupa cada uno; invirtiendo los
        // Dock, el orden visual se invierte solo: [globo][píldora][barra] ↔ [barra][píldora][globo].
        DockPanel.SetDock(TalkPanel, left ? Dock.Right : Dock.Left);
        DockPanel.SetDock(BarShell, left ? Dock.Left : Dock.Right);
        DockPanel.SetDock(StatusChip, left ? Dock.Left : Dock.Right);

        MenuPanel.HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        BarRow.HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right;

        // Y la píldora respira hacia el lado contrario a la barra.
        StatusChip.Margin = left ? new Thickness(8, 0, 0, 14) : new Thickness(0, 0, 8, 14);
        TalkPanel.Margin = left ? new Thickness(8, 0, 0, 0) : new Thickness(0, 0, 8, 0);
    }

    private bool _sideApplied;

    /// <summary>
    /// El espejo de LA CARITA, que sí sigue viajando de un borde a otro.
    /// </summary>
    /// <remarks>
    /// Vivía dentro de <see cref="ApplyBarSide"/> porque hasta la spec 010 la barra y la carita eran
    /// la misma ventana y compartían lado. Ya no: la barra vive clavada a la derecha en el muelle y
    /// la carita va donde la lancen, así que un solo lado para las dos habría espejado la barra cada
    /// vez que alguien tirase la carita a la izquierda.
    ///
    /// El botón de voz se pone del lado de FUERA: pegada al borde izquierdo, a la derecha de la
    /// carita; pegada al derecho, a su izquierda. Si no, quedaría contra el borde de la pantalla.
    ///
    /// SOLO SI DE VERDAD CAMBIA. Esto se llama en cada movimiento de la ventana —también al empezar
    /// un lanzamiento—, y sacar y volver a meter los hijos fuerza una pasada de layout entera sobre
    /// una ventana que se está midiendo sola (SizeToContent). Reconstruir el árbol para dejarlo
    /// exactamente igual es trabajo tirado, y trabajo tirado en mitad de una animación se nota.
    /// </remarks>
    private void ApplyCaritaSide(bool left)
    {
        // YA NO HAY NADA QUE REORDENAR (spec 011). Esto sacaba y volvía a meter los hijos para poner
        // las pastillas del lado de fuera; sin pastillas, la carita es el único hijo y reconstruir
        // el árbol para dejarlo igual era trabajo tirado en mitad de una animación. Queda la
        // alineación, que es lo único que de verdad depende del lado.
        CollapsedGroup.HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right;

        // Y la línea sale del lado de FUERA: pegada al borde izquierdo va a su derecha, pegada al
        // derecho a su izquierda. Del otro modo nacería contra el borde y no se leería entera.
        GhostPista.Placement = left
            ? System.Windows.Controls.Primitives.PlacementMode.Right
            : System.Windows.Controls.Primitives.PlacementMode.Left;
    }


    /// <summary>Recoloca el espejo de la carita según dónde quedó. Barato: sale pronto si no cambia.</summary>
    private void RefreshBarSide() => ApplyCaritaSide(EdgeSnap.EstáALaIzquierda(this));

    /// <summary>
    /// La ventana acabó en un sitio nuevo por voluntad del usuario. Llega con el DESTINO, así que
    /// todo lo que dependa del lado se aplica mientras la barra todavía está viajando.
    /// </summary>
    private void OnWindowMoved(double left, double top)
    {
        SavePositionSoon(left, top);
        var wa = SystemParameters.WorkArea;
        ApplyCaritaSide(left + ActualWidth / 2 < (wa.Left + wa.Right) / 2);
    }

    // ── Hacia dónde se abre el menú ───────────────────────────────────────────────────────────
    //
    // Por defecto hacia arriba, que es donde hay sitio con la barra en su esquina. Pero si el usuario
    // la arrastra al borde superior, abrir hacia arriba obliga a la ventana a acotarse contra el área
    // de trabajo y quien acaba bajando es la BARRA: el menú aparecería justo donde estaba la mano.

    private bool _menuDown;
    private double _menuHeightGuess = 420;   // se corrige sola tras la primera apertura

    /// <summary>
    /// Cambia la dirección moviendo las dos filas del <c>RootPanel</c>. La fila «*» sigue siendo
    /// SIEMPRE la del menú: esa es la garantía de que, cuando no cabe, quien cede y hace scroll es el
    /// menú y nunca la barra.
    /// </summary>
    private void ApplyMenuDirection(bool down)
    {
        if (_menuDown == down) return;
        _menuDown = down;
        RootPanel.RowDefinitions[0].Height = down ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        RootPanel.RowDefinitions[1].Height = down ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        Grid.SetRow(BarRow, down ? 0 : 1);
        Grid.SetRow(MenuPanel, down ? 1 : 0);
        MenuPanel.VerticalAlignment = down ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        MenuPanel.Margin = down ? new Thickness(0, 8, 0, 0) : new Thickness(0, 0, 0, 8);
    }

    /// <summary>
    /// ¿Hay que abrir hacia abajo? Solo si arriba no cabe Y abajo hay MÁS sitio. La segunda condición
    /// importa: sin ella el menú se voltearía en cuanto faltara un píxel arriba, aunque abajo hubiera
    /// todavía menos — y entonces el scroll interno resuelve mejor quedándose donde está.
    /// </summary>
    private bool ShouldOpenDown()
    {
        try
        {
            var wa = SystemParameters.WorkArea;
            double scale = System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleY;
            double barTop = BarPanel.PointToScreen(new Point(0, 0)).Y / scale;
            double barBottom = barTop + BarPanel.ActualHeight;
            double necesario = Math.Min(_menuHeightGuess, wa.Height - 48) + 8;
            double arriba = barTop - wa.Top, abajo = wa.Bottom - barBottom;
            return arriba < necesario && abajo > arriba;
        }
        catch (Exception ex)
        {
            // PointToScreen exige la ventana renderizada. Si falla, se cae al modo conocido (arriba)
            // y se DICE: un fallback silencioso aquí se vería como «a veces se abre raro».
            LogBus.Log("ui", $"no se pudo medir el hueco para el menú, se abre hacia arriba: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// El chevron apunta a DONDE VA A NACER el menú, y girado significa «ciérralo». Hay que
    /// recalcularlo también en reposo: con la barra arriba, un chevron que apunta hacia arriba
    /// promete un menú que va a salir por abajo.
    /// </summary>
    private void UpdateChevron() =>
        Rotate(ActivatorRot, _menuOpen ? (_menuDown ? 0 : 180) : (_menuDown ? 180 : 0));

    /// <summary>Abre el menú extendido (idempotente). <paramref name="pin"/> lo deja fijado.</summary>
    private void OpenMenu(bool pin)
    {
        if (pin) _menuPinned = true;
        if (_menuOpen) return;
        _menuOpen = true;
        _menuCloseTimer.Stop();
        ApplyMenuDirection(ShouldOpenDown());

        // Sin cuentas de altura aquí: el menú vive en la fila «*» del RootPanel y la barra en la
        // fila «Auto», así que el layout ya garantiza que quien cede y hace scroll es el menú.
        // La primera versión lo calculaba a mano con BarPanel.ActualHeight — que al arrancar valía
        // 0 — y el resultado fue una barra recortada fuera de la pantalla.
        // Hacia abajo entra deslizando desde arriba: el movimiento sale del activador, no hacia él.
        FadeSlideIn(MenuPanel, MenuShift, fromY: _menuDown ? -10 : 10);
        UpdateChevron();

        // La altura real solo se conoce una vez montado. Se guarda para que la SIGUIENTE decisión de
        // dirección no dependa de una estimación.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (MenuPanel.ActualHeight > 0) _menuHeightGuess = MenuPanel.ActualHeight;
        }), System.Windows.Threading.DispatcherPriority.Loaded);

        // Refresca la lista al abrir (antes lo hacía el Expander de Backend): un workflow recién
        // enseñado aparece sin reiniciar nada. Con freno: abrir por hover puede pasar muchas veces
        // por minuto y cada recarga es un GET a Graph.
        if (!_runningDirect && Environment.TickCount64 - _lastWorkflowReloadMs > 3000)
        {
            _lastWorkflowReloadMs = Environment.TickCount64;
            _ = ReloadDirectWorkflowsAsync();
        }

        // Y se comprueba que Graph siga vivo, con el mismo criterio: solo si hace rato que no se
        // sabe nada de él. Si acaba de responder (p.ej. la recarga de arriba), no hace falta preguntar.
        if (Environment.TickCount64 - _lastProbeMs > 15000 &&
            (GraphHealth.Current.Edad is not TimeSpan edad || edad > TimeSpan.FromSeconds(15)))
        {
            _lastProbeMs = Environment.TickCount64;
            _ = ProbeGraphAsync();
        }
        UpdateBackendStatus();   // repinta la EDAD aunque no toque sondear
    }

    private long _lastWorkflowReloadMs;

    /// <summary>Cierra el menú (y despliega el cierre de Backend si no está fijado).</summary>
    private void CloseMenu()
    {
        _menuPinned = false;
        if (!_menuOpen) return;
        _menuOpen = false;
        _menuCloseTimer.Stop();
        FadeSlideOut(MenuPanel, MenuShift, toY: _menuDown ? -8 : 8, () =>
        {
            if (_menuOpen) return;
            MenuPanel.Visibility = Visibility.Collapsed;
            // La dirección se suelta AQUÍ y no antes: el último SizeChanged es el del colapso, y si
            // _menuDown ya fuera false se anclaría por el lado equivocado y la barra daría un salto.
            ApplyMenuDirection(false);
            UpdateChevron();
        });
        UpdateChevron();
    }

    // ── Globo de conversación (estado + narración + entrada de texto) ─────────────────────────

    /// <summary>
    /// Muestra el globo de conversación. Se llama solo cuando hay algo que ver: una narración, una
    /// pregunta del asistente, una ejecución en curso. Con la carita colapsada no hace nada (mismo
    /// contrato de siempre: colapsado = solo la carita).
    /// </summary>
    /// <summary>
    /// Pide el globo. Lo concede <see cref="ReglaDelGlobo"/>, no quien llama.
    /// </summary>
    /// <remarks>
    /// TODA LLAMADA DECLARA SU MOTIVO, y no hay valor por defecto a propósito: el defecto sería
    /// exactamente la decisión que aquí no se puede tomar de oficio. Veinte sitios abrían este
    /// globo, ninguno se preguntaba si conversar era lo que tocaba, y el resultado fue un chat
    /// saliendo encima del trabajo del dueño cada vez que Ü contaba algo (2026-09-06).
    /// </remarks>
    private void ShowTalk(MotivoDelGlobo motivo, bool focusInput = false)
    {
        // Un fallo saca el panel aunque no abra el globo: la píldora vive dentro.
        if (_collapsed && ReglaDelGlobo.DespliegaElMuelle(motivo))
            _muelle?.Desplegar(motivo == MotivoDelGlobo.AlgoFallo ? "algo falló" : "lo pidió alguien");

        // El progreso deja su texto escrito y se va: quien abra el globo lo encontrará ahí.
        if (!ReglaDelGlobo.SeAbre(motivo)) { UpdateChip(_mood); return; }

        if (_muelle == null) return;
        if (!_talkOpen)
        {
            _talkOpen = true;
            FadeSlideIn(TalkPanel, TalkShift, fromY: 6);
        }
        UpdateChip(_mood);   // el globo lleva el texto largo: la píldora sobra mientras esté abierto
        // El foco se pide DESPUÉS del pase de layout: si el globo acaba de hacerse visible,
        // enfocar en el mismo instante puede caer en el vacío.
        if (focusInput)
            Dispatcher.BeginInvoke(new Action(() => Input.Focus()),
                System.Windows.Threading.DispatcherPriority.Input);
    }

    private void HideTalk()
    {
        if (!_talkOpen) return;
        _talkOpen = false;
        FadeSlideOut(TalkPanel, TalkShift, toY: 6, () => { if (!_talkOpen) TalkPanel.Visibility = Visibility.Collapsed; });
        UpdateChip(_mood);   // sin globo, la píldora vuelve a ser la que informa
    }

    // ── Microanimaciones compartidas: fundido + deslizamiento corto, sin rebotes ──────────────

    private static void FadeSlideIn(UIElement el, System.Windows.Media.TranslateTransform shift, double fromY)
    {
        el.Visibility = Visibility.Visible;
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        el.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
        shift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(fromY, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
    }

    private static void FadeSlideOut(UIElement el, System.Windows.Media.TranslateTransform shift, double toY, Action done)
    {
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease };
        fade.Completed += (_, __) => done();
        el.BeginAnimation(OpacityProperty, fade);
        shift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(toY, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease });
    }

    private static void Rotate(System.Windows.Media.RotateTransform t, double angle) =>
        t.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty,
            new DoubleAnimation(angle, TimeSpan.FromMilliseconds(160)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });

    // --- Selector de workflow directo (en el menú extendido) ---

    /// <summary>
    /// Carga los workflows de Graph para el selector directo del panel Backend: el carrusel/lista elige
    /// uno y "Ejecutar ahora" lo corre sin abrir la biblioteca. Silencioso si Graph aún no está configurado.
    /// </summary>
    private async Task ReloadDirectWorkflowsAsync()
    {
        if (_runningDirect) return; // no pisar la lista mientras corre uno
        if (!_graphConfig.IsConfigured)
        {
            SetWorkflowSelectorEmpty("Configura Graph (URL + API key) para ver tus workflows.");
            return;
        }

        _directGraph ??= new GraphClient(_graphConfig);
        _planes ??= new PlanesALaMano(_directGraph);
        try
        {
            var raw = await _directGraph.ListWorkflowsAsync(CancellationToken.None);
            // Recargar es la señal de que algo cambió en Graph: los planes de antes pueden no valer.
            _planes.OlvidaTodos();
            // El id que tenía elegido, para no perderlo al reordenar (salvo que acabe de enseñar).
            string? elegido = _directIndex >= 0 && _directIndex < _directWorkflows.Count
                ? _directWorkflows[_directIndex].Id : null;
            _directWorkflows.Clear();
            // Del más nuevo al más viejo (Graph los manda al revés), y cada uno con el nombre que
            // el operador le puso, si le puso (promesas 108-110).
            foreach (var wf in SelectorDeWorkflows.Ordenar(raw.Select(WorkflowSummary.FromJson)))
            {
                wf.NombrePropio = _nombres.De(wf.Id);
                _directWorkflows.Add(wf);
            }

            PintarBotonDeAccion();
            if (_directWorkflows.Count == 0)
            {
                SetWorkflowSelectorEmpty("No hay workflows todavía. Enseña uno con 🎓.");
                return;
            }

            // Llena la lista (la fuente de ambas vistas) y ancla el índice en rango.
            _syncingWorkflowUi = true;
            WorkflowListBox.ItemsSource = null;
            WorkflowListBox.ItemsSource = _directWorkflows;
            _syncingWorkflowUi = false;

            bool many = _directWorkflows.Count > 1;
            WorkflowPrev.IsEnabled = many;
            WorkflowNext.IsEnabled = many;
            RunWorkflowBtn.IsEnabled = true;
            WorkflowDeleteBtn.IsEnabled = true;
            WorkflowRenameBtn.IsEnabled = true;
            // Lo recién enseñado queda ELEGIDO; si no hay nada recién enseñado, se conserva lo que
            // estaba elegido, y si tampoco, el más nuevo (índice 0).
            string? nuevo = _nuevoWorkflowId;
            _nuevoWorkflowId = null;
            SetDirectIndex(SelectorDeWorkflows.IndiceDe(_directWorkflows, nuevo ?? elegido));
            if (nuevo != null) Bautizar();
        }
        catch (Exception ex)
        {
            LogBus.Log("workflow-ui", $"selector directo: ListWorkflowsAsync falló: {ex}");
            SetWorkflowSelectorEmpty($"No se pudieron cargar los workflows: {ex.Message}");
        }
    }

    /// <summary>Sin workflows utilizables: mensaje en el carrusel y todo deshabilitado.</summary>
    private void SetWorkflowSelectorEmpty(string message)
    {
        _directWorkflows.Clear();
        WorkflowListBox.ItemsSource = null;
        WorkflowPick.Text = message;
        WorkflowPrev.IsEnabled = false;
        WorkflowNext.IsEnabled = false;
        RunWorkflowBtn.IsEnabled = false;
        WorkflowDeleteBtn.IsEnabled = false;
        WorkflowRenameBtn.IsEnabled = false;
        WorkflowNameBox.Visibility = Visibility.Collapsed;
    }

    // ── Ponerle nombre (promesa 109) ─────────────────────────────────────────

    /// <summary>
    /// Abre el cuadro de nombre sobre el workflow elegido, con el nombre actual seleccionado para
    /// que escribir lo reemplace. Se abre solo al terminar de enseñar —es el momento en que uno
    /// sabe qué acaba de grabar— y con ✏ cuando se quiera.
    /// </summary>
    private void Bautizar()
    {
        if (_directIndex < 0 || _directIndex >= _directWorkflows.Count) return;
        var wf = _directWorkflows[_directIndex];
        OpenMenu(pin: true); // el cuadro vive en el panel: si el panel está cerrado, nadie lo vería
        WorkflowNameBox.Text = wf.Nombre;
        WorkflowNameBox.Visibility = Visibility.Visible;
        // El foco DESPUÉS del pase de layout, como hace Input: enfocar en el mismo instante en que
        // la caja se hace visible puede caer en el vacío.
        Dispatcher.BeginInvoke(new Action(() => { WorkflowNameBox.Focus(); WorkflowNameBox.SelectAll(); }),
            System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OnRenameWorkflow(object sender, RoutedEventArgs e) => Bautizar();

    private void OnWorkflowNameKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { GuardarNombre(); e.Handled = true; }
        else if (e.Key == Key.Escape) { WorkflowNameBox.Visibility = Visibility.Collapsed; e.Handled = true; }
    }

    /// <summary>Perder el foco también guarda: cerrar el panel a medio escribir no tira el nombre.</summary>
    private void OnWorkflowNameLostFocus(object sender, RoutedEventArgs e)
    {
        if (WorkflowNameBox.Visibility == Visibility.Visible) GuardarNombre();
    }

    private void GuardarNombre()
    {
        WorkflowNameBox.Visibility = Visibility.Collapsed;
        if (_directIndex < 0 || _directIndex >= _directWorkflows.Count) return;
        var wf = _directWorkflows[_directIndex];
        string texto = WorkflowNameBox.Text.Trim();
        // Dejar el derivado tal cual no es «ponerle nombre»: no se guarda una copia del derivado,
        // que se quedaría vieja si cambia cómo se deriva.
        string? propio = texto.Length == 0 || texto == wf.Title ? null : texto;
        if (propio == wf.NombrePropio) return;
        _nombres.Poner(wf.Id, propio);
        wf.NombrePropio = propio;
        // La lista pinta ToString() al llenarse: se rellena para que el nombre nuevo se vea ya.
        _syncingWorkflowUi = true;
        WorkflowListBox.ItemsSource = null;
        WorkflowListBox.ItemsSource = _directWorkflows;
        _syncingWorkflowUi = false;
        SetDirectIndex(_directIndex);
        SetStatus(propio == null ? $"Sin nombre propio: «{wf.Nombre}»" : $"Se llama «{propio}»");
    }

    private void OnWorkflowPrev(object sender, RoutedEventArgs e) => StepWorkflow(-1);
    private void OnWorkflowNext(object sender, RoutedEventArgs e) => StepWorkflow(+1);

    /// <summary>Borra el workflow que muestra el carrusel/lista, tras confirmar. Refresca al terminar.</summary>
    private async void OnDeleteWorkflow(object sender, RoutedEventArgs e)
    {
        if (_runningDirect) return;
        if (_directIndex < 0 || _directIndex >= _directWorkflows.Count) return;
        var wf = _directWorkflows[_directIndex];

        // El diálogo de confirmación desactiva esta ventana, y un menú abierto por hover se
        // cerraría debajo. Fijarlo primero: la pregunta y su consecuencia se ven en el mismo sitio.
        OpenMenu(pin: true);

        var confirm = MessageBox.Show(
            $"¿Borrar «{wf.Nombre}»?\n\nNo se puede deshacer.",
            "Borrar workflow", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        WorkflowDeleteBtn.IsEnabled = false;
        try
        {
            _directGraph ??= new GraphClient(_graphConfig);
            await _directGraph.DeleteWorkflowAsync(wf.Id, CancellationToken.None);
            _nombres.Poner(wf.Id, null); // el nombre propio muere con el workflow
            SetStatus($"Borrado: «{wf.Nombre}»");
            await ReloadDirectWorkflowsAsync();
        }
        catch (Exception ex)
        {
            LogBus.Log("workflow-ui", $"borrar workflow falló: {ex}");
            SetStatus($"No se pudo borrar: {ex.Message}");
            WorkflowDeleteBtn.IsEnabled = true;
        }
    }

    /// <summary>Avanza/retrocede en el carrusel con vuelta circular (del último salta al primero).</summary>
    private void StepWorkflow(int delta)
    {
        if (_directWorkflows.Count == 0) return;
        int n = _directWorkflows.Count;
        SetDirectIndex(((_directIndex + delta) % n + n) % n);
    }

    /// <summary>Alterna carrusel ↔ lista con el botón ☰.</summary>
    private void OnToggleWorkflowView(object sender, RoutedEventArgs e)
    {
        _directListMode = !_directListMode;
        WorkflowCarousel.Visibility = _directListMode ? Visibility.Collapsed : Visibility.Visible;
        WorkflowListBox.Visibility = _directListMode ? Visibility.Visible : Visibility.Collapsed;
        WorkflowViewToggle.Content = _directListMode ? "▤" : "☰";
        if (_directListMode) SetDirectIndex(_directIndex); // deja la fila seleccionada a la vista
    }

    private void OnWorkflowListSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingWorkflowUi) return;
        if (WorkflowListBox.SelectedIndex >= 0) SetDirectIndex(WorkflowListBox.SelectedIndex);
    }

    /// <summary>Única fuente de verdad del workflow elegido: sincroniza carrusel y lista.</summary>
    private void SetDirectIndex(int i)
    {
        if (_directWorkflows.Count == 0) return;
        _directIndex = Math.Clamp(i, 0, _directWorkflows.Count - 1);
        var wf = _directWorkflows[_directIndex];
        WorkflowPick.Text = $"{_directIndex + 1}/{_directWorkflows.Count} · {wf.Nombre} ({wf.StepCount} paso(s))";
        // El plan se pide en cuanto el carrusel se queda quieto sobre este (promesa 111).
        _precarga.Stop();
        _precarga.Start();

        if (!_syncingWorkflowUi && WorkflowListBox.SelectedIndex != _directIndex)
        {
            _syncingWorkflowUi = true;
            WorkflowListBox.SelectedIndex = _directIndex;
            WorkflowListBox.ScrollIntoView(wf);
            _syncingWorkflowUi = false;
        }
    }


    // ── El rellenador de SAP: la exportación a HC escribe en los campos ─────────
    //
    // VA APARTE DEL PUENTE DE ABAJO, que es otra cosa: aquel trae valores YA GUARDADOS por el
    // médico en el portal y los ofrece para aprobación; esto escribe sin preguntar lo que el
    // exportador trae. Comparten la superficie de SAP y nada más. El dictado clínico en directo
    // que también escribía aquí —con su micrófono propio— se retiró el 2026-09-02 con su pastilla
    // (spec 008, promesa 112).
    private readonly SapGuiSurface _clinicalSap = new();
    private RellenadorSap? _rellenador;

    /// <summary>Quien atiende los «Exportar a HC» que llegan de la web. Vive todo el rato.</summary>
    private EjecutorDeExportaciones? _exportador;

    /// <summary>
    /// Ejecuta el workflow que apunta el slider, igual que "Ejecutar ahora" de la biblioteca: pide el
    /// plan a Graph y lo corre con el <see cref="WorkflowPlayer"/> (Graph decide QUÉ, esta máquina CÓMO).
    /// Se alinea conscientemente si la pantalla no coincide y aprende esa alineación para la próxima.
    /// El botón ⏹ cancela por el mismo <c>_cts</c> que el resto del panel.
    /// </summary>
    private async void OnRunWorkflowDirect(object sender, RoutedEventArgs e)
    {
        // LO PRIMERO QUE TOCA: si hay una tarea sin comprobar, el botón está ofreciendo comprobarla
        // y eso es lo que hace. Ejecutar viene después, cuando ya se puede.
        if (_botonComprueba) { OnComprobarAprendizaje(sender, e); return; }
        if (_runningDirect) return;
        if (_directIndex < 0 || _directIndex >= _directWorkflows.Count) { SetStatus("Selecciona un workflow primero."); return; }
        var wf = _directWorkflows[_directIndex];

        _runningDirect = true;
        RunWorkflowBtn.IsEnabled = false;
        _cts = new CancellationTokenSource();
        ShowStop(true);
        SetWorking(true);
        SetStatus($"Ejecutando «{wf.Nombre}»…");
        ShowTalk(MotivoDelGlobo.SoloEsProgreso); // el progreso se narra ahí, y el ⏹ de la barra ya quedó visible
        string? bridgeGoal = null; // puente subconsciente→consciente si el workflow se detiene
        bool paróElUsuario = false, falló = false;
        try
        {
            _directGraph ??= new GraphClient(_graphConfig);
            // Log detallado dentro de la superficie (resolución + clic): el punto ciego donde no veíamos
            // por qué un paso decía ✓ sin pasar nada. Tag "uia" en 📜 Logs.
            var uia = new UiaSurface { Log = s => LogBus.Log("uia", s) };
            // La superficie SAP del player también habla: es la que dice qué rama tomó al accionar una
            // fila («isFolder=… → modo …»), el dato que faltaba para saber por qué un paso decía ✓ sin
            // que la pantalla cambiara. Antes se construía anónima y su Diagnostic no lo oía nadie.
            var sap = new SapGuiSurface();
            sap.Diagnostic += (_, msg) => LogBus.Log("sap", msg);
            var player = new WorkflowPlayer(_directGraph, _graphConfig, uia, sap)
            {
                // Banco de pruebas del motor de navegación nuevo (escalera de rutas: enfocar → acceso
                // directo → shell). Los otros dos sitios (MCP/chat, biblioteca) siguen en AppAligner
                // hasta validar aquí. Ver SurfaceNavigator.
                Aligner = SurfaceNavigator.Default.EnsureAsync,
                Log = s => LogBus.Log("workflow", s),
                // Las capturas de la enseñanza llevaban tiempo guardándose sin que las usara nadie.
                TaughtShotFor = (id, order) =>
                    System.IO.Path.Combine(StepShotCamera.FolderFor(id), $"step_{order}.png"),
            };

            player.StepDone += (_, o) => Narrate(o.Ok ? $"✓ {o.Label}" : $"✗ {o.Label}: {o.Error}");

            // El plan que ya se pidió al elegir el workflow (promesa 111). El log dice de dónde
            // salió y cuánto tardó: es la única forma de saber si la precarga sirve de algo.
            _planes ??= new PlanesALaMano(_directGraph);
            var planes = _planes;
            bool aLaMano = planes.EstaListo(wf.Id);
            var playSw = System.Diagnostics.Stopwatch.StartNew();
            player.PlanSource = async (id, ct) =>
            {
                var plan = await planes.Toma(id, ct);
                LogBus.Log("workflow-ui", $"▶ play «{wf.Nombre}»: plan {(aLaMano ? "precargado" : "pedido")} en {playSw.ElapsedMilliseconds} ms");
                return plan;
            };

            RunResult result = await player.RunAsync(wf.Id, null, strictSurface: true, _cts.Token);
            SetStatus(result.Ok
                ? $"«{wf.Nombre}» terminó: {result.Tally}."
                : $"«{wf.Nombre}» se detuvo: {result.Error}");
            // Un workflow que se detiene NO lanza excepción: devuelve un resultado que dice que no
            // llegó. Sin esta línea la cara se quedaría tan contenta tras una corrida fallida.
            falló = !result.Ok && !_cts.IsCancellationRequested;

            if (result.Ok && result.AlignedConsciously)
            {
                // Graph va a anteponer un paso: el plan que teníamos a la mano deja de ser el suyo.
                planes.Olvida(wf.Id);
                _ = _directGraph.PrependAlignmentStepAsync(wf.Id, CancellationToken.None)
                    .ContinueWith(_ => planes.Precarga(wf.Id), TaskScheduler.Default); // aprende a alcanzar su superficie, y se vuelve a tener a la mano
            }

            // PUENTE subconsciente→consciente: si el workflow se detuvo (y no fue cancelado por el
            // usuario), el cerebro consciente (computer-use) retoma desde la pantalla actual con el
            // contexto del fallo. Al despejar el obstáculo puede re-invocar el workflow: la reanudación
            // por ubicación hace el resto (se salta lo ya hecho).
            if (!result.Ok && !_cts.IsCancellationRequested)
                bridgeGoal =
                    $"Estaba ejecutando el workflow «{wf.Nombre}» y se detuvo en: {result.Error}. " +
                    $"Del plan: {result.Tally}. Los omitidos NO se ejecutaron. " +
                    "Retoma desde la pantalla actual y termina la tarea del workflow. Si despejas el " +
                    "obstáculo, puedes invocar de nuevo la herramienta del workflow: se reanuda solo " +
                    "desde la ubicación actual sin repetir lo ya hecho.";
        }
        catch (OperationCanceledException) { SetStatus("Detenido"); paróElUsuario = true; }
        catch (Exception ex)
        {
            LogBus.Log("workflow-ui", $"selector directo: RunAsync falló: {ex}");
            SetStatus($"Error ejecutando: {ex.Message}");
            falló = true;
        }
        finally
        {
            _runningDirect = false;
            RunWorkflowBtn.IsEnabled = true;
            ShowStop(false);
            _working = false;
            // «Yo lo paré» y «se rompió» se cuentan por separado: son causas distintas y la cara
            // tiene que poder decir cuál fue.
            SetOutcome(paróElUsuario, falló);
            _cts = null;
        }

        // Fuera del try/finally: StartGoal crea su PROPIO _cts (adentro lo pisaría el finally).
        if (bridgeGoal != null)
        {
            // El workflow se detuvo ESTANDO en su aplicación, así que el origen de ahora es dónde vive
            // la tarea. Se lo pasamos al consciente como compuerta: puede navegar todo lo que quiera
            // DENTRO de esa app, pero no teclear en otra. Sin esto, «retoma desde la pantalla actual»
            // se ejecutó sobre la ventana que tuviera el foco — el incidente del 2026-07-26.
            // Lectura inmediata: esta compuerta ata al consciente a UNA app, y atarlo a la de hace
            // 800 ms es justo el fallo que la compuerta existe para evitar.
            string origin = _locator?.DondeEstoy()?.Origin ?? "";
            LogBus.Log("workflow-ui", "puente consciente: el workflow se detuvo → computer-use retoma"
                + (origin.Length > 0 ? $" (atado a «{origin}»)" : " · SIN origen conocido: va sin compuerta"));
            SetStatus("El workflow se detuvo — el modo consciente retoma…");
            _ = StartGoal(bridgeGoal, origin);
        }
    }

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        if (_logWindow == null || !_logWindow.IsLoaded)
            _logWindow = new LogWindow();

        _logWindow.Show();
        _logWindow.Activate();
    }

    /// <summary>
    /// Estado de conexión con Graph (solo lectura). El backend ya no se configura a mano: la URL tiene
    /// default sano y la API key llega sola (embebida en el build distribuido, o env GRAPH_API_KEY en
    /// dev). Si falta la key, se dice cómo ponerla una sola vez, sin exponer ningún campo editable.
    /// </summary>
    private void UpdateBackendStatus()
    {
        // Toda la tabla de estados vive en GraphHealthText, que es también quien alimenta el
        // indicador de la biblioteca de workflows. Aquí solo se pinta.
        var (dot, text) = GraphHealthText.Describe(
            _graphConfig.IsConfigured
                ? GraphHealth.CurrentFor(_graphConfig.BaseUrl)   // solo lo que sepamos de ESTE host
                : GraphHealth.CurrentFor(_graphConfig.BaseUrl) with { Link = GraphLink.SinKey });
        BackendStatus.Text = text;
        BackendDot.Fill = dot;
    }

    private void OnGraphHealthChanged(object? sender, GraphObservation obs) =>
        Dispatcher.BeginInvoke(new Action(UpdateBackendStatus));

    private long _lastProbeMs;

    /// <summary>
    /// Le pregunta a Graph si sigue ahí, con el manifiesto (<c>GET /api/v1</c>) — que existía desde
    /// siempre descrito como «prueba de vida y de key» y no lo llamaba nadie.
    ///
    /// Se sondea cuando el usuario MIRA (al abrir el menú), no por temporizador: un latido permanente
    /// gasta red toda la jornada para responder una pregunta que solo se hace de vez en cuando.
    ///
    /// Cinco segundos y no los noventa del cliente: si Graph está caído, el punto tiene que ponerse
    /// rojo mientras el menú sigue abierto, no minuto y medio después.
    /// </summary>
    private async Task ProbeGraphAsync()
    {
        if (!_graphConfig.IsConfigured) { UpdateBackendStatus(); return; }
        _directGraph ??= new GraphClient(_graphConfig);   // el mismo cliente del selector, no uno nuevo
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await _directGraph.ManifestAsync(cts.Token);  // el éxito lo anota SendAsync
        }
        catch (OperationCanceledException)
        {
            // El corte es NUESTRO, de 5 s. SendAsync no puede distinguirlo de un ⏹ del usuario, así
            // que lo reporta quien sí lo sabe: este método.
            GraphHealth.Report(GraphLink.SinRespuesta, GraphHealth.HostOf(_graphConfig.BaseUrl),
                0, "no respondió en 5 s");
        }
        catch (GraphException) { /* ya lo anotó SendAsync, con su causa distinguida */ }
        catch (Exception ex) { LogBus.Log("backend", $"sonda de vida falló de forma inesperada: {ex.Message}"); }
    }


    /// <summary>
    /// QUÉ ME HAN ENSEÑADO AQUÍ, de un vistazo: todo iluminado y con su texto encima.
    /// </summary>
    /// <remarks>
    /// Es la misma información que cuenta la voz con map_recuerdos, pero para los ojos. Aquella los
    /// da de uno en uno a propósito —habla entre uno y otro, y el recuadro debe estar sobre lo que
    /// se está contando— y eso solo tiene sentido si hay conversación. Para revisar lo aprendido no
    /// se puede depender de tener que pedírselo en voz alta (2026-08-24, pedido por el usuario).
    ///
    /// SE APAGA COMO TODO LO DEMÁS: con Escape, por el freno, que es donde ya vive «deja de
    /// iluminar». No se inventa un segundo gesto para apagar algo que ya sabe apagarse.
    /// </remarks>
    private void OnVerRecuerdos(object sender, RoutedEventArgs e)
    {
        // EL MISMO BOTÓN LOS APAGA. Encender con un clic y tener que buscar OTRA forma de apagar no
        // es un interruptor, es una trampa: quien lo pulsó espera que vuelva a pulsarse
        // (2026-08-24, dicho por el usuario).
        if (_recuerdosALaVista) { OcultarRecuerdos(); return; }
        if (_mapaDeMano == null) { SetStatus("todavía no puedo mirar los recuerdos"); return; }

        MarcarRecuerdosALaVista(true);
        RefrescarRecuerdosALaVista();
    }

    /// <summary>Están encendidos los recuerdos de esta pantalla.</summary>
    private bool _recuerdosALaVista;

    /// <summary>
    /// Pinta los recuerdos de DONDE ESTAMOS AHORA. Se llama al encender y en cada cambio de
    /// pantalla mientras la vista siga puesta.
    /// </summary>
    /// <remarks>
    /// ENCENDER Y REFRESCAR SON LO MISMO, y por eso comparten este camino: si fueran dos, el día que
    /// se cambie cómo se pintan habría que acordarse de los dos sitios, y uno de los dos se quedaría
    /// atrás. Encender es el primer refresco.
    ///
    /// UNA PANTALLA SIN RECUERDOS NO APAGA LA VISTA: se queda encendida y vacía, esperando. Apagarla
    /// sola al pasar por un sitio donde no se ha enseñado nada obligaría a volver a pulsar el botón
    /// cada vez que se cruza una pantalla cualquiera, que es justo lo contrario de un modo.
    /// </remarks>
    private void RefrescarRecuerdosALaVista()
    {
        if (_mapaDeMano == null) return;
        try
        {
            // NO SE REDIBUJA ENCIMA DE QUIEN ESCRIBE. La ubicación se mira sola cada pocos cientos
            // de milisegundos, así que sin esto una corrección a media frase se borraría sola.
            if (TarjetasDeRecuerdo.EscribiendoAlguna) return;

            var recuerdos = _mapaDeMano.RecuerdosEnPantalla();
            if (recuerdos.Count == 0)
            {
                Senalador.Soltar();
                try { _iluminacion?.HideRect(); } catch { }
                TarjetasDeRecuerdo.Cerrar();
                SetStatus("aquí no te he aprendido nada todavía · la vista sigue puesta");
                return;
            }

            // PASA POR EL SEÑALADOR aunque el dibujo lo haga el overlay, y no es un rodeo: el
            // Señalador es quien responde «¿hay algo encendido?», y de esa respuesta cuelga Escape
            // —que se sale antes de tiempo si Actual está vacío—. Pintando por fuera, lo iluminado
            // existía para los ojos y no para el resto del sistema: Escape no lo apagaba.
            Senalador.SenalarVarias(recuerdos.Select(r => (r.Caja, r.Etiqueta)).ToList());

            if (_iluminacion == null) { _iluminacion = new HighlightOverlay(); _iluminacion.Show(); }
            _iluminacion.ShowRects(recuerdos.Select(r => r.Caja).ToList());
            // EL TEXTO LO PONEN LAS TARJETAS, no el overlay: son las mismas que usa la narración, y
            // además se pueden corregir. Dos formas de pintar lo mismo acabarían divergiendo — y una
            // de las dos sería la que no deja escribir.
            TarjetasDeRecuerdo.Mostrar(recuerdos.Select(r => (r.Caja, r.Selector, r.Etiqueta, r.Significado)).ToList());

            SetStatus(recuerdos.Count == 1
                ? "1 recuerdo · edítalo escribiendo encima · Escape lo apaga"
                : $"{recuerdos.Count} recuerdos · edítalos escribiendo encima · Escape los apaga");
        }
        catch (Exception ex) { LogBus.Log("recuerdo", $"no pude enseñar los recuerdos: {ex.Message}"); }
    }

    private void OcultarRecuerdos()
    {
        try { Senalador.Soltar(); } catch { }
        try { _iluminacion?.HideRect(); } catch { }
        // LAS TARJETAS TAMBIÉN. Son ventanas propias, así que no se van con el recuadro: apagar la
        // vista y que el texto siguiera flotando encima de la pantalla dejaba algo que no se podía
        // quitar de ninguna forma (2026-08-24, visto por el usuario).
        try { TarjetasDeRecuerdo.Cerrar(); } catch { }
        MarcarRecuerdosALaVista(false);
        SetStatus("recuerdos apagados");
    }

    /// <summary>
    /// El botón dice en qué estado está. Un interruptor que se ve igual encendido que apagado
    /// obliga a mirar la pantalla para saber si funcionó.
    /// </summary>
    private void MarcarRecuerdosALaVista(bool si)
    {
        _recuerdosALaVista = si;
        PintarToggle(RecuerdosBtn, si);
    }

    private void OnToggleInspector(object sender, RoutedEventArgs e)
    {
        _inspector ??= new UiInspector();
        bool on = _inspector.Toggle();
        PintarToggle(InspectorBtn, on);
        SetStatus(on ? "Inspector de elementos activo" : "Inspector apagado");
    }

    /// <summary>
    /// Muestra/oculta el ID de superficie. SOLO el badge: el localizador no se apaga nunca.
    ///
    /// Antes este botón llamaba a <c>_locator.Stop()</c>, y con eso paraba el motor entero: el ID
    /// dejaba de recalcularse, <c>Current</c> se congelaba en el último valor y todo lo que vive de
    /// saber dónde estamos se quedaba ciego —el ancla de ubicación, la comprobación de llegadas, el
    /// MCP, y sobre todo el aprendizaje del terreno, porque sin el evento <c>Changed</c> el mapa
    /// deja de observar las pantallas por las que pasa el usuario. Ocultar un dato no es dejar de
    /// medirlo (2026-08-04, reportado por el usuario: «que solo se active o desactive visualmente»).
    ///
    /// Es la misma regla que ya sigue el vigilante de clics: siempre activo, porque el terreno se
    /// aprende viviendo. Lo que el usuario decide aquí es si quiere VERLO, no si el sistema sabe.
    /// </summary>
    /// <summary>
    /// Lleva la carita junto a una caja de pantalla, sin taparla.
    ///
    /// Se coloca a la DERECHA del elemento y, si ahí no cabe, a la izquierda: taparlo justo cuando
    /// se está diciendo «mira esto» sería la peor forma de señalarlo. La caja llega en píxeles
    /// físicos —como los da UIA— y se convierte aquí, porque el escalado lo sabe la ventana.
    /// </summary>
    /// <summary>Cuando se señalan varias, el aviso de «una» llega detrás y no debe pisar el recorrido.</summary>
    private bool _recorridoReciénLanzado;

    /// <summary>
    /// LA MANO ACABA DE PULSAR AHÍ: la carita va a verlo (promesa 240). La caja llega en píxeles
    /// físicos, que es como la da UIA y como la espera <see cref="IrJuntoA"/>.
    /// </summary>
    private void OnManoPulso(double x, double y, double ancho, double alto)
        => Dispatcher.BeginInvoke(new Action(() => IrJuntoA(new Rect(x, y, ancho, alto), alClic: true)));

    /// <param name="alClic">
    /// Viene de un clic de la mano y no de señalar: viaja con la curva rápida, y solo si está
    /// colapsada. Con el panel abierto la carita es una barra con contenido, y arrastrarla por la
    /// pantalla en cada clic taparía justo lo que la persona está leyendo.
    /// </param>
    private void IrJuntoA(Rect fisico, bool alClic = false)
    {
        if (alClic && !_collapsed) return;
        if (JuntoA(fisico) is not { } sitio) return;

        // SEÑALAR VARIAS EMITE LAS DOS SEÑALES. Senalador avisa de «estas seis» y acto seguido de
        // «la principal es esta», y las dos llegan a la carita: el recorrido arrancaba y el aviso
        // siguiente lo sustituía por un viaje corriente a la primera. Desde fuera parecía que el
        // recorrido no se había implementado (2026-08-07). Los ojos sí miran; lo que se ignora es
        // el movimiento, que ya lo lleva la ruta.
        if (_recorridoReciénLanzado) _recorridoReciénLanzado = false;
        else if (alClic) ViajarAlClic(sitio.X, sitio.Y);
        else MoverConMuelle(sitio.X, sitio.Y);

        // Y los ojos hacia él: si la carita quedó a su derecha, mira a la izquierda.
        try
        {
            var m = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice
                    ?? System.Windows.Media.Matrix.Identity;
            var tl = m.Transform(new Point(fisico.X, fisico.Y));
            var br = m.Transform(new Point(fisico.Right, fisico.Bottom));
            double ancho = ActualWidth > 0 ? ActualWidth : 160;
            CollapsedFace?.MirarHacia((tl.X + br.X) / 2 < sitio.X + ancho / 2);
        }
        catch { }
    }

    /// <summary>
    /// DÓNDE SE PONE la carita para señalar algo. Solo lo calcula; no la mueve.
    /// </summary>
    /// <remarks>
    /// Separado de <see cref="IrJuntoA"/> porque hay dos formas de usarlo y solo una mueve: señalar
    /// una cosa va y se planta, y señalar varias necesita SABER los sitios de todas antes de salir,
    /// para trazar un camino que pase por ellos. Si el cálculo viviera dentro del movimiento, el
    /// recorrido tendría que ir parándose para preguntar (2026-08-07).
    /// </remarks>
    private Point? JuntoA(Rect fisico)
    {
        try
        {
            var src = PresentationSource.FromVisual(this);
            System.Windows.Media.Matrix m = src?.CompositionTarget?.TransformFromDevice
                ?? System.Windows.Media.Matrix.Identity;
            var tl = m.Transform(new Point(fisico.X, fisico.Y));
            var br = m.Transform(new Point(fisico.Right, fisico.Bottom));

            // DÓNDE PUEDE PONERSE. El área de trabajo es la del monitor PRINCIPAL, así que recortar
            // contra ella arrastraba la carita de vuelta a la pantalla principal cada vez que el
            // elemento estaba en otra: quedaba lejísimos de lo que decía estar mirando. Si el
            // elemento cae dentro del área de trabajo se usa esa —así no tapa la barra de tareas—;
            // si no, manda el escritorio ENTERO, que es donde de verdad está (2026-08-05).
            var trabajo = SystemParameters.WorkArea;
            var todo = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            var elemento = new Rect(tl, br);
            var area = trabajo.Contains(elemento) ? trabajo : todo;

            double ancho = ActualWidth > 0 ? ActualWidth : 160;
            double alto = ActualHeight > 0 ? ActualHeight : 160;

            double x = br.X + 12;
            if (x + ancho > area.Right) x = tl.X - ancho - 12;      // no cabe a la derecha: al otro lado
            x = Math.Max(area.Left, Math.Min(x, area.Right - ancho));

            double y = tl.Y + ((br.Y - tl.Y) / 2) - (alto / 2);      // centrada con el elemento
            y = Math.Max(area.Top, Math.Min(y, area.Bottom - alto));

            return new Point(x, y);
        }
        catch { return null; }
    }

    private int _recorrido;   // cada recorrido nuevo invalida el anterior

    /// <summary>
    /// Va PASANDO por todas las cosas señaladas, una tras otra, en vez de plantarse junto a la
    /// primera.
    /// </summary>
    /// <remarks>
    /// Señalar seis cosas y quedarse junto a una es decir dos cosas distintas a la vez: los
    /// recuadros dicen seis y el cuerpo dice una. Recorrerlas es lo que hace una persona cuando
    /// enumera algo con la mano — y de paso convierte una lista en algo que se puede seguir con la
    /// mirada, que es justo lo que no se puede hacer con seis recuadros encendidos de golpe.
    ///
    /// Se para en cada una lo justo para que se lea, y se acaba en la primera: es la que manda —la
    /// que <see cref="Senalador"/> considera la principal— y dejar la carita en la última sería
    /// terminar señalando algo que no es el asunto.
    ///
    /// Cada recorrido nuevo cancela el anterior por número de serie y no por una bandera: si el
    /// asistente señala otra cosa a mitad de camino, el recorrido viejo tiene que morir en silencio,
    /// no pelearse por mover la ventana.
    /// </remarks>
    private void Recorrer(IReadOnlyList<Rect> cajas)
    {
        if (cajas.Count <= 1) return;   // una sola ya la lleva IrJuntoA

        _recorrido++;

        // TODAS, sin recortar. Antes se enseñaban seis por miedo a que fuera largo, y eso mentía:
        // se marcaban treinta recuadros y el cuerpo visitaba seis. Lo que hacía largo el recorrido
        // no era el número de paradas, era pararse en cada una — resuelto yendo de un tirón
        // (2026-08-07). Lo que sí se acota es el TIEMPO, no el contenido.
        var paradas = new List<Point>();
        foreach (var caja in cajas)
        {
            if (JuntoA(caja) is { } sitio) paradas.Add(sitio);
        }
        if (paradas.Count == 0) return;

        // EL ORDEN EN QUE LLEGAN NO ES UN ORDEN. Las cosas se señalan pasando el ratón por encima,
        // y eso se hace en desorden —arriba, abajo, otra vez arriba—, así que recorrerlas en ese
        // orden producía un zigzag que no se lee como mirar nada. Ese desorden es el comportamiento
        // normal de quien señala y no va a cambiar: quien tiene que ordenar es esto (2026-08-07).
        //
        // Se ordenan por el eje en el que están REPARTIDAS: una columna se recorre de arriba abajo y
        // una fila de izquierda a derecha, que es como se mira una lista.
        double anchoTotal = paradas.Max(p => p.X) - paradas.Min(p => p.X);
        double altoTotal = paradas.Max(p => p.Y) - paradas.Min(p => p.Y);
        paradas = (altoTotal >= anchoTotal
            ? paradas.OrderBy(p => p.Y).ThenBy(p => p.X)
            : paradas.OrderBy(p => p.X).ThenBy(p => p.Y)).ToList();

        // Y SE EMPIEZA POR EL EXTREMO QUE PILLA MÁS CERCA. Ir hasta la otra punta para empezar desde
        // allí es un viaje que no dice nada; salir de donde ya se está y terminar en el extremo
        // contrario recorre lo mismo sin el paseo previo.
        var desdeAqui = new Point(Left, Top);
        if ((paradas[^1] - desdeAqui).Length < (paradas[0] - desdeAqui).Length) paradas.Reverse();

        // No se vuelve a la primera: se termina donde termina la lista. Volver al principio
        // convertía el recorrido en un circuito, y lo que se está diciendo es «de aquí hasta aquí».

        double largo = 0;
        for (int i = 1; i < paradas.Count; i++)
            largo += (paradas[i] - paradas[i - 1]).Length;

        // TRANQUILO. El tiempo sale de la distancia Y del número de paradas, porque cada cosa mirada
        // pide su momento aunque esté pegada a la anterior: solo con la distancia, seis elementos de
        // una barra lateral se despachaban en menos de un segundo y no daba tiempo a leer nada.
        var dur = TimeSpan.FromMilliseconds(
            Math.Clamp(500 + largo * 0.55 + paradas.Count * 260, 900, 8000));
        _recorridoReciénLanzado = true;
        Vuelo.Recorrido(this, paradas, dur);
    }

    /// <summary>Dónde estaba antes de irse a presidir algo. Vacío = no se ha movido.</summary>
    private (double Left, double Top)? _sitioDeAntes;

    /// <summary>Se pone centrada justo ENCIMA de una caja de pantalla (píxeles ya en unidades WPF).</summary>
    private void EncimaDe(Rect caja)
    {
        try
        {
            _sitioDeAntes ??= (Left, Top);
            double ancho = ActualWidth > 0 ? ActualWidth : 160;
            double alto = ActualHeight > 0 ? ActualHeight : 160;
            var area = SystemParameters.WorkArea;

            double x = caja.Left + (caja.Width - ancho) / 2;
            double y = caja.Top - alto + 12;                 // pegada al borde de arriba, solapando un poco
            MoverConMuelle(Math.Max(area.Left, Math.Min(x, area.Right - ancho)),
                           Math.Max(area.Top, Math.Min(y, area.Bottom - alto)));
            try { CollapsedFace?.DejarDeMirar(); } catch { }
        }
        catch { }
    }

    private void VolverASuSitio()
    {
        if (_sitioDeAntes is not { } sitio) return;
        MoverConMuelle(sitio.Left, sitio.Top);
        _sitioDeAntes = null;
    }

    private void OnToggleLocator(object sender, RoutedEventArgs e)
    {
        if (_locator == null || _badge == null) return;

        if (!_locator.Active) _locator.Start();   // red: si algo lo paró, vuelve a andar

        _idALaVista = !_idALaVista;
        if (_idALaVista)
        {
            _badge.Show();
            PintarToggle(LocatorBtn, on: true);
            SetStatus("ID de superficie a la vista");
        }
        else
        {
            _badge.Hide();
            PintarToggle(LocatorBtn, on: false);
            SetStatus("ID oculto (se sigue midiendo)");
        }
    }

    /// <summary>Si el badge del ID se está mostrando. El localizador corre igual, se vea o no —y por
    /// eso esto puede nacer en false sin que se pierda nada: lo que arranca apagado es el CARTEL, no
    /// la medición.</summary>
    private bool _idALaVista = false;

    /// <summary>
    /// Arranca el modo consciente. <paramref name="requireOrigin"/> ata el objetivo a una aplicación:
    /// vacío para lo que pide el usuario a mano (el destino puede ser cualquiera), y con valor cuando
    /// el objetivo viene del PUENTE — ahí sí se sabe dónde vive la tarea, y salirse de ahí es el bug.
    /// </summary>
    private async Task StartGoal(string goal, string requireOrigin = "")
    {
        _cts = new CancellationTokenSource();
        ShowStop(true);
        SetWorking(true);
        SetStatus("Pensando…");
        ShowTalk(MotivoDelGlobo.SoloEsProgreso); // que se vea el estado (y quede a mano el ⏹) desde el primer segundo
        bool paró = false, falló = false;
        try
        {
            string summary = await _loop.RunAsync(goal, _cts.Token, requireOrigin);
            SetStatus(summary);
        }
        catch (OperationCanceledException) { SetStatus("Detenido"); paró = true; }
        catch (Exception ex) { SetStatus($"Error: {ex.Message}"); falló = true; }
        finally { ShowStop(false); _working = false; SetOutcome(paró, falló); }
    }

    // --- IVoice ---
    // Narrar y hablar abren el globo de conversación: en la barra compacta no hay texto permanente,
    // así que lo que Ü dice tiene que traer su propia ventana.
    public void Narrate(string text) => Dispatcher.Invoke(() =>
    {
        Bubble.Text = text;
        if (!string.IsNullOrWhiteSpace(text)) ShowTalk(MotivoDelGlobo.SoloEsProgreso);
        // MIENTRAS SE COMPRUEBA, LO QUE EL PILOTO NARRA SE OYE (promesa 142). Fuera de eso, narrar
        // es un estado y va escrito: un asistente que lee en voz cada «voy por el paso 3» cansa.
        if (_comprobando && !string.IsNullOrWhiteSpace(text) && _vivo is { Viva: true })
            _ = _vivo.DiEstoAsync(text);
    });
    public void Speak(string text)
    {
        Dispatcher.Invoke(() => { Bubble.Text = text; SetStatus(text); ShowTalk(MotivoDelGlobo.SoloEsProgreso); });
        // Durante una conversación en vivo la voz de Ü la pone Gemini. Añadir encima el sintetizador
        // de Windows serían dos Ü hablando a la vez, cada una su frase: el texto se sigue viendo,
        // que es lo que hace falta, pero se oye una sola.
        // SI LA VOZ DE Ü ESTÁ VIVA, HABLA ELLA (promesa 142). Antes esta rama solo CALLABA al
        // sintetizador para no oír dos Ü a la vez, y daba por hecho que lo que había que decir ya lo
        // estaba diciendo la conversación. En una comprobación no: quien decide qué se dice es el
        // piloto, y sin esta línea su frase salía por el sintetizador de Windows — dos voces para
        // el mismo asistente, que fue lo que el dueño oyó el 2026-09-03.
        if (_vivo is { Viva: true }) { _ = _vivo.DiEstoAsync(text); return; }
        _voice.Speak(text);
    }

    /// <summary>
    /// El globo, línea a línea: quién la dijo y qué dijo. Vive aparte de lo pintado —antes se releía
    /// de <c>Bubble.Text</c>, que es la trampa de que la pantalla sea también el estado (patrón
    /// nº8: una caja que miente es peor que no tener caja, y aquí la caja era el propio texto)—.
    /// De aquí sale tanto el reemplazo en vivo de una frase a medio decir como el peso de cada
    /// línea: la etiqueta «Ü: »/«Tú: » hacía dos trabajos —decir quién habla, y decirle al código si
    /// esta frase sigue el turno anterior—, y quitarla de la pantalla (pedido del dueño, 2026-09-17)
    /// se habría llevado los dos si no queda guardada aquí.
    /// </summary>
    private readonly List<(string Quien, string Texto)> _globo = new();

    /// <summary>
    /// Añade una línea al globo sin borrar lo anterior.
    ///
    /// <see cref="Narrate"/> REEMPLAZA, que es lo correcto para un estado («voy por el paso 3»), y
    /// justo lo contrario de lo que necesita una conversación: ahí lo dicho y lo hecho tienen que
    /// quedarse a la vista. Cuando la voz mueve archivos de verdad, poder leer después qué se pidió
    /// y qué herramienta se ejecutó no es un lujo.
    /// </summary>
    private void AppendChat(string linea)
    {
        if (string.IsNullOrWhiteSpace(linea)) return;

        // Una frase que se está diciendo REEMPLAZA a su versión anterior en vez de añadirse: llega a
        // trozos y añadirlos dejaba una columna de palabras sueltas. Se compara por quién habla, y
        // solo mientras el turno sigue abierto: al cerrarse, `_turnoAbierto` cae y la siguiente
        // frase del mismo interlocutor empieza línea nueva, que es lo que hace legible el historial.
        //
        // QUIÉN LO DICE VIENE EN EL PROPIO TEXTO (lo deciden `EnviarTextoAsync` y `Reaccionar` en
        // ConversacionEnVivo), pero ya no se guarda ni se enseña tal cual: se lee aquí y se recorta.
        string quien = linea.StartsWith("Ü: ", StringComparison.Ordinal) ? "Ü"
            : linea.StartsWith("Tú: ", StringComparison.Ordinal) ? "Tú"
            : "";
        string texto = quien switch { "Ü" => linea[3..], "Tú" => linea[4..], _ => linea };

        if (_turnoAbierto && quien.Length > 0 && _globo.Count > 0 && _globo[^1].Quien == quien)
            _globo[^1] = (quien, texto);
        else
            _globo.Add((quien, texto));
        _turnoAbierto = true;
        if (_globo.Count > 40) _globo.RemoveRange(0, _globo.Count - 40);
        PintarGlobo();
        ShowTalk(MotivoDelGlobo.SoloEsProgreso);
    }

    /// <summary>
    /// Pinta el globo entero a partir de <see cref="_globo"/>: un <see cref="Run"/> por línea, y lo
    /// que dice Ü un poco más grueso que lo que dice la persona (pedido del dueño, 2026-09-17: sin
    /// la etiqueta delante, el peso de la letra es lo único que sigue separando a los dos). Cada
    /// repintado entra con un parpadeo breve —de 0,2 a 1 de opacidad en 160 ms— para que el texto no
    /// cambie de golpe: el mismo lenguaje que ya usa el notch al aparecer.
    /// </summary>
    private void PintarGlobo()
    {
        Bubble.BeginAnimation(OpacityProperty, null);
        Bubble.Inlines.Clear();
        for (int i = 0; i < _globo.Count; i++)
        {
            var (quien, texto) = _globo[i];
            Bubble.Inlines.Add(new Run(texto)
            { FontWeight = quien == "Ü" ? FontWeights.SemiBold : FontWeights.Normal });
            if (i < _globo.Count - 1) Bubble.Inlines.Add(new LineBreak());
        }
        Bubble.Opacity = 0.2;
        Bubble.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(160))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    // --- IUserChannel ---
    public Task<string> AskAsync(string question, CancellationToken ct)
    {
        Dispatcher.Invoke(() =>
        {
            SetStatus(question);
            ShowTalk(MotivoDelGlobo.HayQueContestar, focusInput: true); // la pregunta necesita la caja de texto delante
        });
        _pendingAnswer = new TaskCompletionSource<string>();
        ct.Register(() => _pendingAnswer?.TrySetResult(""));
        RefreshMood();   // la cara pasa a «te toca a ti» mientras la pregunta siga sin responder
        // Y vuelve a lo que toque en cuanto se resuelva, sea por texto, por voz o por ⏹.
        _pendingAnswer.Task.ContinueWith(_ => RefreshMood(), TaskScheduler.Default);
        return _pendingAnswer.Task;
    }

    private void SetStatus(string s) => Dispatcher.Invoke(() => Status.Text = s.Length > 120 ? s[..120] + "…" : s);

    // ── La carita como semáforo ───────────────────────────────────────────────────────────────
    //
    // El estado NO se asigna a mano desde cada sitio: se DERIVA de los flags que ya existen, en una
    // sola función. Así no puede haber dos partes del código afirmando cosas distintas sobre lo mismo
    // — que es exactamente lo que habría pasado manteniendo el viejo `Thinking` junto al `Mood`.

    private bool _working;   // el consciente pensando, o un workflow corriendo
    private bool _stopped;   // el usuario pulsó ⏹ (se limpia al siguiente arranque)
    private bool _failed;    // la última corrida terminó mal
    private FaceMood _mood = FaceMood.Reposo;

    /// <summary>
    /// El orden de las ramas ES la prioridad: primero lo que está pasando ahora mismo, después lo que
    /// acaba de pasar. Escuchar gana a todo porque el micrófono está abierto y el usuario necesita
    /// saberlo ya.
    /// </summary>
    private FaceMood ResolveMood()
    {
        // LA CONVERSACIÓN EN VIVO ES OTRA VOZ, y esta función solo miraba a la de Windows. Mientras
        // había una sesión abierta la carita se quedaba en reposo: ni hablando cuando hablaba, ni
        // atenta con el micrófono abierto (2026-08-05).
        //
        // Y no vale preguntar «¿suena algo AHORA?»: entre dos palabras de una misma frase hay
        // silencio, así que el estado iría y volvería varias veces por segundo. Cada ida y vuelta
        // reinicia las animaciones de la cara —y de paso el parpadeo y la mirada—, o sea que la
        // carita se quedaría sin parpadear justo mientras habla. Se sostiene medio segundo.
        if (_vivo?.Viva == true)
        {
            if (_vivo.NivelVoz > 0.004) _ultimoSonido = DateTime.UtcNow;
            return (DateTime.UtcNow - _ultimoSonido).TotalMilliseconds < 600
                ? FaceMood.Hablando
                : FaceMood.Conversando;
        }

        var voz = _voice.Activity;
        if (voz.Escuchando) return FaceMood.Escuchando;
        if (_teaching) return FaceMood.Grabando;
        // Una pregunta sin responder: el agente está parado esperando al usuario, no trabajando.
        if (_pendingAnswer is { Task.IsCompleted: false }) return FaceMood.Esperando;
        if (voz.Hablando) return FaceMood.Hablando;
        if (_working || _runningDirect) return FaceMood.Trabajando;
        if (_failed) return FaceMood.Fallo;
        if (_stopped) return FaceMood.Detenido;
        return FaceMood.Reposo;
    }

    /// <summary>Recalcula el estado y lo aplica a las DOS caritas y a la píldora, en un solo sitio.</summary>
    private void RefreshMood() => Dispatcher.Invoke(() =>
    {
        var mood = ResolveMood();
        if (mood == _mood) return;
        _mood = mood;
        Face.Mood = mood;
        CollapsedFace.Mood = mood;
        UpdateChip(mood);
        ActualizarBoca();
    });

    /// <summary>
    /// ¿Hay que estar moviendo la boca? Dos voces distintas pueden estar hablando y la carita no
    /// tiene por qué saber cuál.
    ///
    /// La de Windows avisa por <see cref="FaceMood.Hablando"/>; la de la conversación en vivo NO
    /// pasa por ahí —su audio sale por otro sitio— y ese fue el fallo: la boca estaba dibujada y
    /// nadie la movía, porque el único disparador miraba a la voz vieja (2026-08-05).
    /// </summary>
    private void ActualizarBoca() =>
        MoverLaBoca(_mood == FaceMood.Hablando || _vivo?.Viva == true);

    // ── La boca, mientras habla ───────────────────────────────────────────────────────────────

    private System.Windows.Threading.DispatcherTimer? _boca;
    private double _bocaAbierta;
    private int _bocaPaso;

    /// <summary>La última vez que se oyó algo por el altavoz. Sostiene el estado «hablando» durante
    /// los silencios cortos de dentro de una frase.</summary>
    private DateTime _ultimoSonido = DateTime.MinValue;

    /// <summary>
    /// Abre y cierra la boca al ritmo de lo que se está diciendo.
    ///
    /// El movimiento sale del VOLUMEN REAL de la voz, no de un bucle de animación: una boca que se
    /// mueve sola mientras suena una frase acaba desincronizada de ella y se nota enseguida —es la
    /// diferencia entre un muñeco que habla y uno al que le suena un altavoz detrás—. Ese volumen ya
    /// lo mide la capa de voz para otra cosa (no confundir su propio eco con el usuario), así que
    /// aquí se aprovecha en vez de medirlo por segunda vez.
    ///
    /// Cuando no hay sesión viva —la voz vieja de Windows no da nivel— se cae a un vaivén, que es
    /// mejor que una boca quieta mientras se oye hablar.
    ///
    /// 16 cuadros por segundo y no 60: la boca cambia de FORMA, así que cada cuadro es un repintado
    /// de la carita entera, y esto solo puede correr mientras habla. A 16 el habla ya se lee como
    /// habla —el cine mudo iba a esa velocidad— y cuesta la cuarta parte.
    /// </summary>
    private void MoverLaBoca(bool hablando)
    {
        if (!hablando)
        {
            _boca?.Stop();
            _boca = null;
            _bocaAbierta = 0;
            Face.MouthOpen = 0;
            CollapsedFace.MouthOpen = 0;
            return;
        }
        if (_boca != null) return;

        _boca = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(60) };
        _boca.Tick += (_, __) =>
        {
            _bocaPaso++;
            bool enVivo = _vivo?.Viva == true;

            // LA BOCA TIENE QUE SABER PARARSE SOLA. Al temporizador solo lo apagaba RefreshMood, y
            // a RefreshMood solo se le llamaba desde aquí abajo MIENTRAS había sesión viva. Si la
            // sesión moría con la cara en «Hablando» —que es exactamente lo que pasa al cortarla a
            // media frase, o al decirle «cállate»— nadie volvía a evaluar el estado: el temporizador
            // seguía corriendo, se quedaba sin nivel al que seguir y caía al vaivén de más abajo.
            // La boca se movía sola, en silencio, hasta que otra cosa cambiara el ánimo
            // (2026-08-15, visto por el usuario: «cuando se queda ya callado la boca se sigue
            // moviendo sola»). Depender de que otro se dé cuenta era el fallo; ahora se comprueba
            // aquí, que es el único sitio que sigue vivo cuando todo lo demás se apagó.
            if (!enVivo && !_voice.Activity.Hablando)
            {
                MoverLaBoca(false);   // se para y cierra la boca; Stop() impide otro tick
                RefreshMood();        // y se corrige el ánimo, que se quedó en «Hablando»
                return;
            }

            // CON NIVEL REAL, EL SILENCIO CIERRA LA BOCA. Caer al vaivén cuando el nivel es bajo
            // haría que la carita moviera los labios durante las pausas de la conversación —y en una
            // conversación se calla más de lo que se habla—, que es peor que no moverlos: parece que
            // dice cosas que no dice. El vaivén es solo para la voz de Windows, que no da nivel.
            double objetivo;
            if (enVivo)
            {
                // Se estira porque la voz normal vive en la parte baja de la escala: una boca que
                // solo se abre en los gritos no parece que hable.
                double nivel = _vivo!.NivelVoz;
                objetivo = nivel <= 0.004 ? 0 : Math.Min(1, Math.Pow(nivel, 0.55) * 1.45);
            }
            else
            {
                objetivo = 0.35 + 0.30 * Math.Sin(_bocaPaso * 0.9) + 0.15 * Math.Sin(_bocaPaso * 2.3);
            }

            // Se persigue el objetivo en vez de saltar a él: los labios tienen inercia, y sin esto
            // la boca parpadea entre abierta y cerrada como un interruptor.
            _bocaAbierta += (Math.Max(0, Math.Min(1, objetivo)) - _bocaAbierta) * 0.55;

            // Redondeado a centésimas: por debajo de eso no se ve nada y solo serían repintados.
            double abierta = Math.Round(_bocaAbierta, 2);
            // La forma acompaña pero no va a la par: abrir mucho tiende a «a», poco a «o», y una
            // onda lenta desempata para que no salga siempre la misma cara.
            double redonda = Math.Round(Math.Max(0, Math.Min(1,
                (1 - abierta) * 0.7 + 0.3 * (0.5 + 0.5 * Math.Sin(_bocaPaso * 0.37)))), 2);

            if (Math.Abs(Face.MouthOpen - abierta) >= 0.01) { Face.MouthOpen = abierta; CollapsedFace.MouthOpen = abierta; }
            if (Math.Abs(Face.MouthRound - redonda) >= 0.02) { Face.MouthRound = redonda; CollapsedFace.MouthRound = redonda; }

            // Y que el resto de la cara acompañe: en vivo se alterna entre hablar y escuchar sin que
            // nadie más lo avise. RefreshMood no hace nada si el estado no cambió, así que llamarla
            // en cada cuadro sale gratis.
            if (_vivo?.Viva == true || _actualizando) { RefreshMood(); PintarHalo(); }
        };
        _boca.Start();
    }

    /// <summary>
    /// Sustituye al viejo <c>SetThinking</c>: los sitios que ejecutan siguen diciendo «estoy
    /// trabajando» y la cara la decide <see cref="ResolveMood"/>. Arrancar limpia los estados de
    /// resultado: si vuelves a lanzar algo, el «se detuvo» de antes ya no describe nada.
    /// </summary>
    private void SetWorking(bool on)
    {
        _working = on;
        if (on) { _stopped = false; _failed = false; }
        RefreshMood();
    }

    /// <summary>Cómo terminó lo último. Se separan a propósito: «yo lo paré» no es «se rompió».</summary>
    private void SetOutcome(bool stopped, bool failed)
    {
        _stopped = stopped;
        _failed = failed;
        RefreshMood();
        // Un resultado es un aviso, no un estado: a los 5 s la cara vuelve a la calma.
        if (stopped || failed) ClearOutcomeSoon();
    }

    private System.Windows.Threading.DispatcherTimer? _outcomeTimer;

    private void ClearOutcomeSoon()
    {
        if (_outcomeTimer == null)
        {
            _outcomeTimer = new System.Windows.Threading.DispatcherTimer(
                System.Windows.Threading.DispatcherPriority.Background)
            { Interval = TimeSpan.FromSeconds(5) };
            _outcomeTimer.Tick += (_, __) =>
            {
                _outcomeTimer!.Stop();
                _stopped = _failed = false;
                RefreshMood();
            };
        }
        _outcomeTimer.Stop();
        _outcomeTimer.Start();
    }

    /// <summary>
    /// La frase corta que acompaña a la carita.
    ///
    /// Nació con la regla «es la suplente del globo, se esconde cuando el globo está abierto». Esa
    /// regla la hacía INVISIBLE SIEMPRE: todos los caminos que producen un estado —micrófono,
    /// enseñar, ejecutar— abren el globo por su cuenta, así que la condición nunca se cumplía y la
    /// píldora no llegó a verse ni una vez desde que se escribió.
    ///
    /// No compiten: son dos granularidades. La píldora dice UNA palabra pegada a la cara, para saber
    /// qué pasa de un vistazo; el globo lleva el texto largo, para leerlo. Solo se calla con la
    /// carita colapsada, donde el contrato es «solo la carita» y el semáforo es la cara misma.
    /// </summary>
    private void UpdateChip(FaceMood mood)
    {
        string s = ShortPhrase(mood);
        bool show = s.Length > 0 && !_collapsed;

        if (show)
        {
            StatusChipText.Text = s;
            if (StatusChip.Visibility != Visibility.Visible)
                FadeSlideIn(StatusChip, ChipShift, fromY: 4);
        }
        else if (StatusChip.Visibility == Visibility.Visible)
        {
            FadeSlideOut(StatusChip, ChipShift, toY: 4,
                () => { if (StatusChip.Visibility == Visibility.Visible && !ShouldShowChip()) StatusChip.Visibility = Visibility.Collapsed; });
        }
    }

    private bool ShouldShowChip() => ShortPhrase(_mood).Length > 0 && !_collapsed;

    /// <summary>
    /// El texto sale del ESTADO, no del <c>Status</c> libre: así la cara y la frase no pueden
    /// discrepar. Sin contadores del tipo «3 de 7» — el denominador de un plan ya mintió una vez, y
    /// el detalle es cosa del globo.
    /// </summary>
    private static string ShortPhrase(FaceMood mood) => mood switch
    {
        FaceMood.Escuchando => "Escuchando…",
        FaceMood.Trabajando => "Trabajando…",
        FaceMood.Grabando => "Grabando",
        FaceMood.Esperando => "Te toca a ti",
        FaceMood.Detenido => "Detenido",
        FaceMood.Fallo => "Se detuvo",
        // Reposo, Hablando y Conversando: la cara basta. Conversando además dura minutos, y una
        // etiqueta fija ahí no informa de nada — solo ocupa sitio en la pantalla de alguien que
        // está trabajando. Que el micrófono sigue abierto ya lo dice su botón, en rojo.
        _ => "",
    };

    // ── DEMO DE PUNTA A PUNTA (2026-08-31): del Easy Access de SAP a los campos clínicos
    //    llenos, TODO por batches del terreno y por la misma puerta que usa el agente (el MCP
    //    8790). Nada de coordenadas y nada de atajos: lo que se demuestra es el mecanismo real.
    //    Los selectores de la fila del paciente y de los campos se toman del terreno VIVO en el
    //    momento (la fila del censo cambia cada día; fijarla sería mentir).

    private static readonly System.Net.Http.HttpClient _demoHttp = new() { Timeout = TimeSpan.FromSeconds(180) };
    private bool _demoCorriendo;

    private async Task<string> DemoTool(string name, object args)
    {
        string cuerpo = System.Text.Json.JsonSerializer.Serialize(new
        { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name, arguments = args } });
        using var res = await _demoHttp.PostAsync("http://127.0.0.1:8790/mcp/",
            new System.Net.Http.StringContent(cuerpo, System.Text.Encoding.UTF8, "application/json"));
        using var doc = System.Text.Json.JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        string r = doc.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString() ?? "";
        LogBus.Log("demo", $"{name} → " + (r.Length > 160 ? r[..160] : r));
        return r;
    }

    private async Task<System.Text.Json.JsonElement> DemoTerreno()
    {
        string json = await _demoHttp.GetStringAsync("http://127.0.0.1:8792/terreno?niveles=1");
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    /// <summary>Primera puerta VIVA del terreno actual que cumple el criterio, o null.</summary>
    private static string? DemoPuerta(System.Text.Json.JsonElement t, string tipo, string? etiqueta = null)
    {
        foreach (var p in t.GetProperty("puertas").EnumerateArray())
        {
            if (!p.GetProperty("vivo").GetBoolean()) continue;
            if ((p.GetProperty("tipo").GetString() ?? "") != tipo) continue;
            if (etiqueta != null && !string.Equals(p.GetProperty("etiqueta").GetString(), etiqueta,
                    StringComparison.OrdinalIgnoreCase)) continue;
            return p.GetProperty("selector").GetString();
        }
        return null;
    }

    /// <summary>Espera a que el terreno tenga una puerta viva del tipo pedido (el sentido tarda
    /// unos segundos en leer la rejilla tras llegar). Devuelve el terreno, la tenga o no.</summary>
    private async Task<System.Text.Json.JsonElement> DemoEsperarPuerta(string tipo, int intentos = 14)
    {
        var t = await DemoTerreno();
        for (int i = 0; i < intentos && DemoPuerta(t, tipo) == null; i++)
        {
            await Task.Delay(900);
            t = await DemoTerreno();
        }
        return t;
    }

    private static string DemoPasos(params object[] pasos) =>
        System.Text.Json.JsonSerializer.Serialize(pasos);

    private async void OnDemoPuntaAPunta(object sender, RoutedEventArgs e)
    {
        if (_demoCorriendo) return;
        _demoCorriendo = true;
        try
        {
            SetStatus("Demo: SAP al frente…");
            await DemoTool("map_open_app", new { app = "saplogon" });
            await Task.Delay(1000);

            // ── Batch 1: del Easy Access a la vista de Triage (si no estamos ya) ──
            var t = await DemoTerreno();
            string aqui = t.GetProperty("id").GetString() ?? "";
            bool yaEnFormulario = aqui.Contains("SAPLY000", StringComparison.OrdinalIgnoreCase);
            if (!yaEnFormulario && !aqui.Contains("vista:Urgencias Adultos Triage", StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("Demo: batch 1 — caminando a Urgencias/Triage…");
                await DemoTool("map_batch", new { pasos = DemoPasos(
                    new { exit = "Favoritos/NWP1 - IS-H: Pto.tbjo.clínico" },
                    new { exit = "Urgencias Adultos/Triage" }) });
                t = await DemoEsperarPuerta("GuiGridFila");
            }
            else t = await DemoEsperarPuerta("GuiGridFila");

            // ── Batch 2: la fila del paciente y el botón Triage, por identidad viva ──
            if (!yaEnFormulario)
            {
                string? fila = DemoPuerta(t, "GuiGridFila");
                string? boton = DemoPuerta(t, "GuiGridBoton", "Triage");
                if (fila == null || boton == null)
                {
                    SetStatus("Demo: el censo no muestra paciente o botón Triage — ¿la vista está a la vista?");
                    return;
                }
                SetStatus("Demo: batch 2 — paciente y su triage…");
                await DemoTool("map_batch", new { pasos = DemoPasos(new { exit = fila }, new { exit = boton }) });
            }

            // ── El envío único: TODO el formulario de un golpe, por identidad ──
            await DemoEsperarPuerta("GuiTextField");   // el formulario tarda en entregar sus campos
            SetStatus("Demo: llenando el formulario completo de un solo envío…");
            int escritos = await Task.Run(DemoLlenarFormulario);
            SetStatus(escritos > 0
                ? $"✓ Demo completa: {escritos} campo(s) del triage llenos de un solo envío. Nada se graba."
                : "Demo: llegué al triage pero no pude escribir los campos.");
        }
        catch (Exception ex)
        {
            SetStatus($"Demo se detuvo: {ex.Message}");
            LogBus.Log("demo", ex.ToString());
        }
        finally { _demoCorriendo = false; }
    }

    /// <summary>
    /// EL ENVÍO ÚNICO del demo: lee los campos del formulario que hay delante (la misma mano que
    /// usa el dictado clínico) y los llena TODOS de una pasada — texto por `input`, combos por
    /// `select` — con valores de demostración. Cada escritura es un SetText de la Scripting API
    /// (~ms), así que el formulario entero aparece lleno de un golpe. No pisa lo que ya tenga
    /// valor y jamás pulsa Grabar.
    /// </summary>
    private int DemoLlenarFormulario()
    {
        var campos = _clinicalSap.ReadFields();
        int n = 0;
        foreach (var c in campos)
        {
            if (!c.Editable || string.IsNullOrEmpty(c.Selector)) continue;
            bool ocupado = !string.IsNullOrWhiteSpace(c.CurrentValue)
                && c.CurrentValue.Trim() != "0" && c.CurrentValue.Trim() != "0,0";
            if (ocupado) continue;

            string clave = ((c.Selector ?? "") + "|" + (c.Label ?? "")).ToUpperInvariant();
            var paso = new U.Graph.PlanStep { StepOrder = 1, Selector = c.Selector, Label = c.Label };

            if (c.ActionType == "select" && c.AllowedOptions is { Count: > 0 })
            {
                var opcion = DemoOpcion(clave, c.AllowedOptions);
                if (opcion == null) continue;
                paso.ActionType = "select";
                paso.SelectedValue = opcion.Value;
                paso.SelectedLabel = opcion.Label;
            }
            else
            {
                string? valor = DemoValor(clave);
                if (valor == null) continue;
                paso.ActionType = "input";
                paso.Value = valor;
            }

            if (_clinicalSap.Execute(paso, out string error)) { n++; LogBus.Log("demo", $"lleno «{c.Label}» "); }
            else LogBus.Log("demo", $"no pude llenar «{c.Label}»: {error}");
        }

        // LOS EDITORES DE TEXTO LIBRE (Motivo de Consulta, Conducta) no son campos del dynpro:
        // son shells GuiTextedit y ReadFields no los ve. Se identifican por su control contenedor.
        foreach (var v in _clinicalSap.ReadVisibleElements())
        {
            if (!v.SubType.Equals("TextEdit", StringComparison.OrdinalIgnoreCase)) continue;
            string id = v.Id.ToUpperInvariant();
            string? texto =
                  id.Contains("MTVCN") ? "Paciente refiere dolor torácico opresivo de 2 horas de evolución, irradiado a brazo izquierdo, acompañado de diaforesis."
                : id.Contains("TXTOBS") ? "Se prioriza atención. Se indica toma de signos vitales seriados, EKG de 12 derivaciones y valoración médica inmediata."
                : null;
            if (texto == null) continue;
            var paso = new U.Graph.PlanStep { StepOrder = 1, ActionType = "input", Selector = "sap:" + v.Id, Label = v.Label, Value = texto };
            if (_clinicalSap.Execute(paso, out string error)) { n++; LogBus.Log("demo", $"lleno el editor {v.Id[^20..]}"); }
            else LogBus.Log("demo", $"editor no aceptó texto: {error}");
        }
        return n;
    }

    // ── Las cajas de SAP, con freno ──────────────────────────────────────────

    private IReadOnlyList<(string Selector, string Etiqueta, string Tipo, System.Windows.Rect Caja)> _cajasSap
        = Array.Empty<(string, string, string, System.Windows.Rect)>();
    private DateTime _cajasSapLeidas = DateTime.MinValue;
    private bool _leyendoCajasSap;

    /// <summary>
    /// Dónde está cada elemento de SAP en pantalla. Con freno: como mucho una lectura cada segundo
    /// y medio, y nunca dos a la vez.
    /// </summary>
    /// <remarks>
    /// EL FRENO NO ES PRUDENCIA, ES UN CUELGUE YA VISTO. La vista de recuerdos se refresca en CADA
    /// cambio de ubicación —el localizador mira cada 250 ms— y esto corre en el hilo de la interfaz,
    /// que es donde el COM de SAP tiene que correr. Sin tope, sobre el triage la app se quedaba
    /// clavada al pulsar 🧠 (2026-09-02, lo vio el dueño). Se devuelve la última lectura buena
    /// mientras el freno esté echado: un recuadro un segundo viejo es infinitamente mejor que una
    /// app parada.
    /// </remarks>
    private IReadOnlyList<(string Selector, string Etiqueta, string Tipo, System.Windows.Rect Caja)> LeerCajasDeSap()
    {
        if (_leyendoCajasSap) return _cajasSap;
        if ((DateTime.UtcNow - _cajasSapLeidas).TotalMilliseconds < 1500) return _cajasSap;

        var sap = _locator?.SuperficieSap;
        if (sap == null) return _cajasSap;

        _leyendoCajasSap = true;
        var reloj = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var cajas = new List<(string, string, string, System.Windows.Rect)>();
            var vistos = sap.ReadVisibleElements();
            foreach (var v in vistos)
            {
                if (!v.BoundsKnown || v.Width <= 0 || v.Height <= 0) continue;
                cajas.Add((U.Graph.Surfaces.SapSelector.ById(v.Id),
                    (v.Label ?? "").Trim().Length > 0 ? v.Label!.Trim() : v.Id,
                    v.Type ?? "",
                    new System.Windows.Rect(v.ScreenLeft, v.ScreenTop, v.Width, v.Height)));
            }

            // LOS BOTONES DE LA BARRA DE UN ALV ENTRAN SIN CAJA, con su rótulo (promesa 144). SAP
            // no les da geometría —medido con sonda el 2026-09-03: DumpState("Toolbar") no trae ni
            // un getter geométrico— pero sí dice cómo se leen, y con ese rótulo UIA los encuentra:
            // «Triage» → Button [793,227 84x30]. Aquí solo se aporta la MITAD que sabe SAP; la otra
            // la pone LaCajaDeUnBotonDeBarra preguntándole a UIA. Sin esta línea el puente no tiene
            // por dónde empezar, y el recuerdo del botón seguía sin dibujarse («0 de 1 localizados»,
            // 21:29 y 21:31 del 2026-09-03, ya con el resto del arreglo puesto).
            foreach (var rejilla in vistos.Where(v => v.SubType.IndexOf("Grid", StringComparison.OrdinalIgnoreCase) >= 0
                                                   || v.SubType.IndexOf("ALV", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                foreach (var b in sap.LeerRejilla(rejilla.Id).Botones)
                    cajas.Add((U.Graph.Surfaces.SapSelector.ByToolbarButton(rejilla.Id, b.Id),
                        b.Texto, "ToolbarButton", System.Windows.Rect.Empty));
            }

            // LAS FILAS DE LOS ÁRBOLES SON CANDIDATAS, con la caja que SAP les da (promesa 70: en
            // SAP el contenido navegable son las filas, no el árbol). Sin esto, señalar sobre el
            // Puesto de trabajo contestaba el shell entero —lo más pequeño que había— porque una
            // fila no es un componente y no sale en ReadVisibleElements (2026-09-02, 21:19, lo vio
            // el dueño). Los MISMOS candidatos que el terreno: identidad árbol+clave, etiqueta con
            // carpeta, y la banda top/alto que ya valida el «CONTRASTE geometría» del inspector.
            //
            // Las filas de una REJILLA no entran: SAP no da geometría por fila de un ALV, y una caja
            // inventada es peor que ninguna (aprendizaje nº4). Quedan dichas como hueco.
            foreach (var arbol in vistos.Where(v => v.SubType.IndexOf("Tree", StringComparison.OrdinalIgnoreCase) >= 0
                                                  && v.BoundsKnown && v.Height > 0))
            {
                foreach (var fila in sap.VisibleTreeRows(arbol.Id, arbol.Height, out _))
                {
                    if (fila.Height <= 0 || fila.Key.Length == 0) continue;
                    // La banda se recorta contra el borde inferior del árbol, como hace el inspector:
                    // la última fila suele estar medio scrolleada.
                    int alto = Math.Min(fila.Height, arbol.Height - fila.Top);
                    if (alto < 3) continue;
                    cajas.Add((U.Graph.Surfaces.SapSelector.ByNode(arbol.Id, fila.Key),
                        fila.Ruta.Length > 0 ? fila.Ruta : (fila.Text.Length > 0 ? fila.Text : fila.Key),
                        fila.IsFolder ? "GuiTreeCarpeta" : "GuiTreeFila",
                        new System.Windows.Rect(arbol.ScreenLeft, arbol.ScreenTop + fila.Top, arbol.Width, alto)));
                }
            }
            _cajasSap = cajas;
            LogBus.Log("recuerdo", $"cajas de SAP: {cajas.Count} con geometría ({reloj.ElapsedMilliseconds} ms)");
        }
        catch (Exception e)
        {
            var porque = new System.Text.StringBuilder();
            for (var x = e; x != null; x = x.InnerException)
                porque.Append(porque.Length > 0 ? " ← " : "").Append($"{x.GetType().Name}: {x.Message}");
            LogBus.Log("recuerdo", $"no pude leer la geometría de SAP: {porque}");
        }
        finally
        {
            _cajasSapLeidas = DateTime.UtcNow;
            _leyendoCajasSap = false;
        }
        return _cajasSap;
    }

    // ── LA NOTA LLEGA A SAP CON UN ✓, POR UNA SKILL ENSEÑADA (spec 015) ────────────────────
    //
    // El camino fijo de la spec 008 (NWP1 → vista → primera fila → Triage → rellenador → editores)
    // era una fase de prueba y el dueño lo retiró el 2026-09-10 («lo que importa es la ejecución de
    // skills y sus acciones»). Ahora el encargo va al PILOTO, que elige la skill del catálogo por su
    // criterio, arma los datos con lo que la nota trae y la corre por map_skill_run: sin manos
    // sueltas, sin preguntas. Lo que la nota no trae queda en blanco y se dice. Jamás pulsa Grabar:
    // la skill para antes (126).

    // ── LA VENTANA DE TRABAJO DE Ü (spec 020) ────────────────────────────────────────────────
    //
    // Dos ideas de «dónde estoy», con nombre. El FOCO DE LA PERSONA es la ventana que ella mira, y
    // es lo que alimenta el grafo desde el lado humano (el vigía, Observar): no cambia. La VENTANA DE
    // TRABAJO es la que Ü opera: lo que Ü ejecuta —buscar el elemento, pulsarlo, comprobar la
    // consecuencia, situarse— se resuelve respecto a ella. Sin ninguna fijada, es el foco de la
    // persona, y todo se comporta como antes.

    private string FocoDeLaPersona() => _locator?.DondeEstoy()?.Id ?? "";

    /// <summary>
    /// LO QUE SE ACABA DE MIRAR NO SE VUELVE A MIRAR (promesa 246). Identificar la ventana de trabajo
    /// en vivo cuesta lo que cueste esa ventana, y esto se pregunta varias veces por segundo: contestar
    /// «dónde estás» llegó a costar 2.771 ms, más que leer la pantalla entera (2026-09-15).
    /// </summary>
    private readonly Navigation.MemoriaCorta<string> _dondeTrabajo = new(400);

    private string DondeTrabajo() => _dondeTrabajo.Pide(() =>
    {
        RefrescarLaVentanaDeTrabajo();
        return _trabajo.Resolver(U.Graph.Surfaces.UiaSurface.VentanaExiste, FocoDeLaPersona).Id;
    });

    /// <summary>
    /// LA VENTANA DE TRABAJO CAMBIA DE PANTALLA POR DENTRO (spec 020, hallazgo del 2026-09-14 20:26): una
    /// pestaña nueva en Chrome es la misma ventana con otra ubicación, y si la persona tiene el foco en
    /// otra parte nadie la volvía a identificar: Ü seguía «en instagram.com» con la pestaña nueva delante.
    /// Se vuelve a identificar por su hwnd cada vez que se pregunta dónde trabaja.
    /// </summary>
    private void RefrescarLaVentanaDeTrabajo()
    {
        if (!_trabajo.Hay || !U.Graph.Surfaces.UiaSurface.VentanaExiste(_trabajo.Hwnd)) return;
        try
        {
            var loc = _locator?.Identificar(_trabajo.Hwnd);
            if (loc != null && loc.Id.Length > 0 && loc.Id != _trabajo.Id)
            {
                LogBus.Log("trabajo", $"la ventana de trabajo cambió por dentro: «{_trabajo.Id}» → «{loc.Id}»");
                _trabajo.Fijar(_trabajo.Hwnd, loc.Id);
            }
        }
        catch (Exception e) { LogBus.Log("trabajo", $"no pude volver a identificar la ventana de trabajo: {e.Message}"); }
    }

    /// <summary>La ventana en la que la mano busca y pulsa: la de trabajo, o la que la persona mira.</summary>
    private IntPtr VentanaObjetivo()
    {
        if (_trabajo.Hay && U.Graph.Surfaces.UiaSurface.VentanaExiste(_trabajo.Hwnd)) return _trabajo.Hwnd;
        return _locator?.DondeEstoy()?.Hwnd ?? IntPtr.Zero;
    }

    /// <summary>
    /// LO QUE HAY VIVO EN LA VENTANA DE TRABAJO, mirado ahora. El mapa vivo solo observa el foco de
    /// la persona; si Ü trabaja en otra ventana, la compuerta decidiría con lo último que se vio de
    /// ella. Se lee esa ventana con el mismo lector y la misma criba, y se le cuenta al núcleo.
    /// </summary>
    /// <summary>Cuándo se observó por última vez, para no releer la misma ventana dos veces seguidas.</summary>
    private long _ultimaObservacion;

    /// <summary>
    /// MIRAR OTRA VEZ, AHORA, la ventana en la que se va a pulsar. Promesa 264 (spec 030). Es la misma lectura que
    /// hace la observación de fondo, sin su freno: se paga sólo cuando el mapa no tenía la puerta como viva, que es
    /// justo cuando hoy se pagaban 4 s de espera y un «no lo conozco».
    /// </summary>
    private bool MirarOtraVezLaVentana(string aqui)
    {
        if (_mapaVivo == null || string.IsNullOrWhiteSpace(aqui)) return false;
        if (aqui.StartsWith("sapgui://", StringComparison.OrdinalIgnoreCase)) return false;   // SAP se lee por su API
        try
        {
            IntPtr hwnd = _trabajo.Hay && U.Graph.Surfaces.UiaSurface.VentanaExiste(_trabajo.Hwnd)
                ? _trabajo.Hwnd
                : (_locator?.DondeEstoy()?.Hwnd ?? IntPtr.Zero);
            if (hwnd == IntPtr.Zero) return false;
            var lector = new Uia.UiaReader();
            var crono = System.Diagnostics.Stopwatch.StartNew();
            lector.Read(hwnd);
            var crudos = lector.Elements
                .Select(e => (Selector: Uia.Reconocedor.SelectorDe(e), Etiqueta: e.Label, Tipo: e.ControlType))
                .ToList();
            _mapaVivo.ObservarVentana(aqui, crudos);
            _ultimaObservacion = Environment.TickCount64;
            LogBus.Log("trabajo", $"miré otra vez «{aqui}» antes de rendirme: {crudos.Count} elemento(s) en {crono.ElapsedMilliseconds} ms");
            return crudos.Count > 0;
        }
        catch (Exception e) { LogBus.Log("trabajo", $"no pude mirar otra vez: {e.Message}"); return false; }
    }

    private void ObservarLaVentanaDeTrabajo()
    {
        if (_mapaVivo == null || !_trabajo.Hay) return;
        // MIRAR ES PARA ACTUAR (promesa 246): leer la ventana entera alimenta la compuerta de vida, y eso
        // hace falta antes de pulsar o recorrer. Repetirlo dentro del mismo gesto solo cuesta tiempo.
        if (Environment.TickCount64 - _ultimaObservacion < 800) return;
        _ultimaObservacion = Environment.TickCount64;
        if (_trabajo.Id.StartsWith("sapgui://", StringComparison.OrdinalIgnoreCase)) return;   // SAP se lee por su API, con o sin foco
        if (_trabajo.Id == FocoDeLaPersona()) return;   // el latido ya la observa
        if (!U.Graph.Surfaces.UiaSurface.VentanaExiste(_trabajo.Hwnd)) return;
        try
        {
            var lector = new Uia.UiaReader();
            lector.Read(_trabajo.Hwnd);
            var crudos = lector.Elements
                .Select(e => (Selector: Uia.Reconocedor.SelectorDe(e), Etiqueta: e.Label, Tipo: e.ControlType))
                .ToList();
            _mapaVivo.ObservarVentana(_trabajo.Id, crudos);
            LogBus.Log("trabajo", $"observé la ventana de trabajo «{_trabajo.Id}» aparte del foco: {crudos.Count} elemento(s)");
        }
        catch (Exception e) { LogBus.Log("trabajo", $"no pude observar la ventana de trabajo: {e.Message}"); }
    }

    /// <summary>
    /// Si la acción de Ü cambió la ventana de delante, esa es ahora la de trabajo: el sistema activó
    /// lo que Ü abrió o a donde fue. Si la persona sigue en la suya, la de trabajo no se mueve.
    /// </summary>
    private void SeguirElFoco(string antes)
    {
        _dondeTrabajo.Olvida();   // acabamos de accionar: lo recordado ya no vale (promesa 246)
        var loc = _locator?.DondeEstoy();
        if (loc == null || loc.Hwnd == IntPtr.Zero || loc.Id == antes || Propio.EsVentana(loc.Hwnd)) return;
        _trabajo.Fijar(loc.Hwnd, loc.Id);
        LogBus.Log("trabajo", $"la ventana de trabajo es ahora «{loc.Id}»");
    }

    private bool _enviandoEncargo;

    private async Task<string> EnviarEncargoAsync(Clinical.Encargo encargo, IProgress<string> progreso, CancellationToken ct)
    {
        if (encargo.EstaVacio) return "no hay nada marcado para enviar.";
        if (_mapaDeMano == null) return "las manos no están listas todavía.";
        if (_enviandoEncargo) return "ya hay un envío en marcha; espera a que termine.";
        if (!Piloto.ElPiloto.Disponible()) return "no encuentro el piloto (agente-piloto/piloto.mjs): sin él no hay quien elija la tarea.";
        _enviandoEncargo = true;
        var reloj = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var catalogo = Navigation.SkillEnsenada.Catalogo(_mapaDeMano.CarpetaDeSkills);
            string modelo = ModeloDelPiloto();
            var mensaje = new Piloto.ElPiloto.Mensaje(
                Piloto.MensajeDelEncargo.Armar(encargo, catalogo),
                Piloto.CajasDelPiloto.CajaDelEncargo(_nombresMcp),
                Piloto.CajasDelPiloto.ProhibidasEnElEncargo(_nombresMcp),
                modelo, $"http://127.0.0.1:{Mcp.ServidorMcp.Puerto}/mcp/");
            string carpeta = Piloto.MensajeDelEncargo.NuevaCarpeta();
            string rutaMensaje = Piloto.ElPiloto.EscribirMensaje(carpeta, mensaje);
            LogBus.Log("envio", $"encargo: {encargo.Secciones.Count} sección(es) · {encargo.Texto.Length} caracteres · "
                + $"{catalogo.Count(c => c.Comprobada)} skill(s) comprobada(s) de {catalogo.Count} · modelo {modelo} · {rutaMensaje}");
            progreso.Report("El piloto elige la tarea enseñada…");
            await PrestarLaVozAlPilotoAsync("envio");
            _mapaDeMano.SenalarAlActuar = true;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            ShowStop(true);
            SetWorking(true);
            Piloto.ElPiloto.Resultado r;
            try
            {
                r = await Piloto.ElPiloto.CorrerAsync(carpeta, (tipo, texto) =>
                {
                    if (tipo == "texto" && texto.Length > 0) progreso.Report(texto.Length > 160 ? texto[..160] + "…" : texto);
                }, _cts.Token, modo: "encargo");
            }
            finally
            {
                SetWorking(false); ShowStop(false);
                _mapaDeMano.Decir = null;
                _mapaDeMano.SenalarAlActuar = false;
                await DevolverLaVozAsync("envio");
            }
            LogBus.Log("envio", $"piloto terminó ({(r.Termino ? "bien" : $"salida {r.Salida}")}) en {reloj.ElapsedMilliseconds} ms · "
                + $"costo estimado ${r.CostoUsd:0.000} · {r.Ultimo}");
            return r.Termino
                ? (r.Ultimo.Length > 0 ? r.Ultimo : "el piloto terminó sin contar nada.")
                : $"el piloto no terminó bien: {r.Ultimo}";
        }
        catch (Exception ex)
        {
            LogBus.Log("envio", $"el envío reventó: {ex.GetType().Name}: {ex.Message}");
            return $"el envío se detuvo: {ex.Message}";
        }
        finally { _enviandoEncargo = false; }
    }

    /// <summary>
    /// MUESTRA UN APRENDIZAJE. Promesa 225 (spec 016): lo que el panel de la consulta pide al
    /// pulsar «Mostrar».
    /// </summary>
    /// <remarks>
    /// DOS CAMINOS Y UN SOLO BOTÓN, y cuál toca lo decide <see cref="Navigation.LoQuePasaAlMostrar"/>,
    /// que es una regla que el contrato juzga. Lo ya repasado se CORRE: sus pasos, con la coreografía
    /// de siempre y SIN DATOS DE NADIE, así que los huecos quedan vacíos y no se escribe el valor del
    /// paciente de prueba (promesa 123). Lo no repasado se COMPRUEBA, que es lo que la promesa 127
    /// exige antes de dejar ejecutar nada — y se comprueba SU lección, no la última grabada.
    /// </remarks>
    private async Task<string> MostrarAprendizajeAsync(string archivo, IProgress<string> progreso, CancellationToken ct)
    {
        var skill = Navigation.SkillEnsenada.Cargar(archivo);
        var decision = Navigation.LoQuePasaAlMostrar.Decidir(skill, _mapaDeMano?.RecorrerPorElNucleo != null);
        if (decision.Que == "no") return decision.Motivo;

        if (decision.Que == "comprobar")
        {
            string carpeta = CarpetaDeLaLeccionDe(skill!);
            if (carpeta.Length == 0)
                return "esta tarea la aprendí antes de que guardara las lecciones, así que no tengo la "
                     + "demostración para repasarla. Vuelve a enseñármela con 🎓 y queda lista.";
            LogBus.Log("aprendizajes", $"«{skill!.Nombre}» sin repasar: se comprueba su lección {carpeta}");
            _leccionParaComprobar = carpeta;
            progreso.Report("No la he repasado todavía: la repaso una vez contigo mirando. Tarda unos minutos.");
            // Y SE ESPERA (spec 019): el panel recibe el veredicto, no un «mira la pantalla».
            return await ComprobarAsync(progreso);
        }

        if (_mapaDeMano?.RecorrerPorElNucleo == null) return "todavía no sé recorrer en batch.";
        // SIN DATOS: mostrar es enseñar el camino, no rellenar la historia de nadie.
        var pasos = Navigation.InstanciarSkill.Pasos(skill!, new Dictionary<string, string>());
        if (pasos.Count == 0)
            return $"«{skill!.Nombre}» es toda datos: sin ninguno que darle no queda ningún paso que enseñar.";
        // Lo que se dice en cada paso es la MISMA frase que el panel enseña, para que oír y leer
        // cuenten lo mismo.
        var frases = Navigation.LoQueHaceLaSkill.EnCastellano(skill!);
        var porPuerta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < skill!.Pasos.Count && i < frases.Count; i++)
            if (skill.Pasos[i].Exit.Length > 0) porPuerta[skill.Pasos[i].Exit] = frases[i];

        LogBus.Log("aprendizajes", $"mostrando «{skill.Nombre}»: {pasos.Count} paso(s) de {skill.Pasos.Count}");
        await PrestarLaVozAlPilotoAsync("aprendizajes");
        _mapaDeMano.SenalarAlActuar = true;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        ShowStop(true);
        SetWorking(true);
        try
        {
            // EN OTRO HILO: la coreografía espera a que se lea cada tarjeta, y esto lo llama la
            // ventana de la consulta desde SU hilo de interfaz — hacerlo aquí la congelaría.
            var res = await Task.Run(() => Mcp.SurfaceMapTools.RecorrerSkill(pasos, paso =>
                DarUnPasoConCoreografia(paso.Exit, paso, "",
                    porPuerta.TryGetValue(paso.Exit, out var f) ? f : "")), _cts.Token);
            LogBus.Log("aprendizajes", "← " + res.Cuenta);
            return res.Cuenta;
        }
        catch (Exception ex)
        {
            LogBus.Log("aprendizajes", $"mostrar reventó: {ex.GetType().Name}: {ex.Message}");
            return $"se detuvo: {ex.Message}";
        }
        finally
        {
            SetWorking(false); ShowStop(false);
            _mapaDeMano.SenalarAlActuar = false;
            _mapaDeMano.Decir = null;
            await DevolverLaVozAsync("aprendizajes");
        }
    }

    /// <summary>La carpeta de la lección de la que salió, o vacío si no la sabe o ya no está.</summary>
    private static string CarpetaDeLaLeccionDe(Navigation.SkillEnsenada skill)
    {
        if (skill == null || string.IsNullOrWhiteSpace(skill.DeLaLeccion)) return "";
        string carpeta = System.IO.Path.Combine(Teach.LeccionEnDisco.CarpetaRaiz, skill.DeLaLeccion.Trim());
        return System.IO.File.Exists(System.IO.Path.Combine(carpeta, "leccion.json")) ? carpeta : "";
    }

    private static string ModeloDelPiloto() =>
        Environment.GetEnvironmentVariable("U_PILOTO_MODELO") is { Length: > 0 } m ? m : "claude-opus-5";

    // ── LA VOZ PRESTADA AL PILOTO (promesa 192), en UN sitio para comprobar y para el encargo ──

    /// <summary>
    /// Mientras el piloto trabaja, la conversación en vivo no tiene herramientas ni turno propio:
    /// solo dice lo que la app le pide. Sin esto, en la duodécima prueba había dos manos a la vez y
    /// una voz que anunciaba pasos que no tocaban.
    /// </summary>
    private async Task PrestarLaVozAlPilotoAsync(string etiqueta)
    {
        _mapaDeMano!.Decir = DecirPorLaVozPrestada;
        if (_vivo is not { Viva: true }) return;
        try
        {
            await _vivo.CambiarModoAsync(Piloto.VozPrestada.Instrucciones,
                Piloto.VozPrestada.Utensilios(Voice.ConversacionEnVivo.Herramientas()), soloCuandoSeLePide: true);
        }
        catch (Exception ex) { LogBus.Log(etiqueta, $"no pude prestar la voz: {ex.Message}"); }
    }

    /// <summary>Y se devuelve: la conversación vuelve a ser quien era.</summary>
    private async Task DevolverLaVozAsync(string etiqueta)
    {
        if (_vivo is not { Viva: true }) return;
        try { await _vivo.CambiarModoAsync(Voice.ConversacionEnVivo.InstruccionesNormales, Voice.ConversacionEnVivo.Herramientas()); }
        catch (Exception ex) { LogBus.Log(etiqueta, $"no pude devolver la voz: {ex.Message}"); }
    }

    /// <summary>Lo que el piloto (o la coreografía) pide decir. Corre en el hilo del servidor MCP.</summary>
    private string DecirPorLaVozPrestada(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "no había nada que decir.";
        if (_vivo is { Viva: true })
        {
            try { _vivo.DiEstoAsync(texto).GetAwaiter().GetResult(); return "dicho."; }
            catch (Exception ex) { return $"no pude decirlo por la voz ({ex.Message}); lo dejé escrito."; }
        }
        Dispatcher.Invoke(() => SetStatus(texto));
        return "no hay voz abierta: quedó escrito en pantalla.";
    }


    /// <summary>El valor de demo para un campo de texto, por su identidad. Null = no se toca.</summary>
    private static string? DemoValor(string clave)
    {
        if (clave.Contains("FRCAR")) return "78";
        if (clave.Contains("FRRES")) return "16";
        if (clave.Contains("PESO")) return "70";
        if (clave.Contains("TALLA") || clave.Contains("ESTATURA")) return "170";
        if (clave.Contains("TEMP")) return "36,5";
        if (clave.Contains("SAT")) return "98";
        if (clave.Contains("DOL")) return "3";
        if (clave.Contains("TADIA") || clave.Contains("DIAST")) return "80";
        if (clave.Contains("SIST") || clave.Contains("PASIS")) return "120";
        if (clave.Contains("DIAST") || clave.Contains("PADIA")) return "80";
        if (clave.Contains("PRESION") || clave.Contains("PRESIÓN") || clave.Contains("TENSI")) return "120";
        return null;
    }

    /// <summary>
    /// La opción de demo para un combo: por semántica cuando el combo se reconoce (Glasgow pleno,
    /// paciente alerta, triage 3), y si no, la primera opción con texto — el demo enseña que TODO
    /// se llena, no decide medicina.
    /// </summary>
    private static U.Graph.FieldOption? DemoOpcion(string clave, IReadOnlyList<U.Graph.FieldOption> opciones)
    {
        U.Graph.FieldOption? Busca(params string[] pistas) =>
            opciones.FirstOrDefault(o => pistas.Any(p =>
                (o.Label ?? "").Contains(p, StringComparison.OrdinalIgnoreCase)
                || (o.Text ?? "").Contains(p, StringComparison.OrdinalIgnoreCase)));

        U.Graph.FieldOption? elegida = null;
        if (clave.Contains("APOCU")) elegida = Busca("Espont");
        else if (clave.Contains("RTAVB")) elegida = Busca("Orientad");
        else if (clave.Contains("RTAMT")) elegida = Busca("Obedec");
        else if (clave.Contains("ETDCN") || clave.Contains("CONCIENCIA")) elegida = Busca("Alerta");
        else if (clave.Contains("CLTRG") || clave.Contains("TRIAGE")) elegida = Busca("3", "III");
        else if (clave.Contains("MLLEG")) elegida = Busca("Caminando", "Ambulat", "propios");

        return elegida ?? opciones.FirstOrDefault(o =>
            !string.IsNullOrWhiteSpace(o.Value) && !string.IsNullOrWhiteSpace(o.Label));
    }

}

