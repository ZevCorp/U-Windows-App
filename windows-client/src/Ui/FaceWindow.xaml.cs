using System.Windows;
using System.Windows.Controls;
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

    /// <summary>La conversación en vivo, si el mapa está disponible. Ver <see cref="GeminiLive"/>.</summary>
    private GeminiLive? _vivo;

    /// <summary>El mapa vivo publicado en Neo4j. Ver <see cref="Navigation.MapaVivo"/>.</summary>
    private Navigation.MapaVivo? _mapaVivo;

    /// <summary>La ventanita por la que se le puede pedir al núcleo que nos lleve a un sitio.</summary>
    private Navigation.ServidorDelNucleo? _servidorNucleo;

    /// <summary>Hay una frase escribiéndose: los trozos que lleguen la actualizan, no la repiten.</summary>
    private bool _turnoAbierto;
    private readonly VideoLibrary _videoLibrary = new();
    private readonly GraphConfig _graphConfig = GraphConfig.Load();
    private Updater? _updater;
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
    private UiInspector? _inspector;
    private SurfaceLocator? _locator;
    private LocatorBadge? _badge;

    /// <summary>La lista de lo que va pasando. Ver <see cref="PanelDeAcciones"/>.</summary>
    private PanelDeAcciones? _acciones;

    private WorkflowMapWindow? _map;
    // El mapa base del computador (la capa gris): se alimenta SIEMPRE del caudal del locator,
    // esté o no abierta la visualización — el terreno se acumula mientras el usuario vive su día.
    private SurfaceMap? _surfaceMap;
    private ClickWatcher? _clickWatcher;
    private WorkflowMcpRunner? _workflowRunner;

    // Selector de workflow directo en el panel Backend: lista cargada de Graph + un GraphClient propio
    // para listar/ejecutar sin abrir la biblioteca. El slider indexa esta lista.
    private GraphClient? _directGraph;
    private readonly List<WorkflowSummary> _directWorkflows = new();
    private int _directIndex;            // workflow seleccionado (carrusel y lista comparten este índice)
    private bool _directListMode;        // false = carrusel, true = lista
    private bool _syncingWorkflowUi;     // evita el ida y vuelta carrusel ↔ lista al sincronizar
    private bool _runningDirect;

    // Para resolver preguntas del asistente desde la caja de texto.
    private TaskCompletionSource<string>? _pendingAnswer;

    public FaceWindow()
    {
        InitializeComponent();
        // Aquí y no al crear la WorkflowTeachSession: esa se construye en CADA pulsación de «Enseñar»
        // y acumularía una suscripción por intento, multiplicando cada línea en el registro.
        _teachSapSurface.Diagnostic += (_, msg) => LogBus.Log("teach-sap", msg);
        SetStepModeUi(); // el botón nace con su etiqueta puesta, no vacío hasta el primer clic
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
            RefreshRestingChevron();   // ya se puede medir el hueco: el chevron dice hacia dónde abrirá
        }), System.Windows.Threading.DispatcherPriority.Loaded);

        UpdateBackendStatus();
        // El semáforo se repinta solo cada vez que alguien habla con Graph, venga de donde venga.
        // Llega desde la continuación HTTP, no del Dispatcher: hay que marshalear, como ya hace
        // _updater.UpdateReady. Y hay que soltarlo al cerrar: es un evento ESTÁTICO.
        GraphHealth.Changed += OnGraphHealthChanged;
        Closed += (_, __) => GraphHealth.Changed -= OnGraphHealthChanged;
        UpdateVideoLlmToggle();
        SetMuted(_config.Muted); // si lo silenciaron en una sesión anterior, sigue mudo

        // Puente clínico: si ya se emparejó en otra sesión, se retoma solo. Sin esto había que
        // teclear el código en CADA arranque de Ü, que es la fricción que sobra en una consulta.
        if (_config.ClinicalCode.Length == 8)
        {
            _clinical.Pair(_config.ClinicalCode);
            if (_clinical.Active)
            {
                ClinicalCodeBox.Text = _clinical.Code;
                ClinicalPairBtn.Content = "Soltar";
                _clinicalStep = -1;
                SetClinicalStep(1, "Esperando a que guardes la nota en el portal.");
            }
        }
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
        _badge = new LocatorBadge();
        _badge.Show();
        _locator = new SurfaceLocator();
        _surfaceMap = SurfaceMap.Load();
        // El vigilante de clics: sin él las aristas del terreno solo dicen que dos pantallas
        // conectan; con él dicen CÓMO pasar de una a otra, que es lo que permite navegar sin
        // haber grabado un workflow. Siempre activo, porque el terreno se aprende viviendo.
        _clickWatcher = new ClickWatcher();
        _clickWatcher.Start();
        _surfaceMap.Clicks = _clickWatcher;

        // Modo prueba: con U_AUTO_EXPLORER=1 el explorador del grafo se abre solo al arrancar, para
        // que una instancia recién compilada quede lista para lanzar un mapeo sin tocar la carita.
        // Lo pone dev-paralelo.ps1; en la app del usuario esa variable no existe y no cambia nada.
        if (Environment.GetEnvironmentVariable("U_AUTO_EXPLORER") == "1")
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try { OnToggleExplorer(this, new RoutedEventArgs()); } catch { }
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Closed += (_, __) =>
        {
            _surfaceMap?.Save();
            _clickWatcher?.Dispose(); // un hook huérfano ralentiza el ratón de TODA la máquina
        };
        _locator.Changed += loc => Dispatcher.Invoke(() =>
        {
            _badge?.SetText(loc.Id);
            _surfaceMap?.Observe(loc.Id); // el terreno se aprende navegando, sin enseñar nada
            _map?.SetCurrent(loc.Id);     // y el mapa ilumina el nodo donde estás parado
        });
        _locator.Start();

        // El rastro del cursor va desde el arranque: cuando alguien dice «ilumina todo esto que te
        // estoy mostrando», ya ha PASADO el ratón por encima. Si se empezara a mirar al oír la
        // frase, lo que se quiere enseñar ya habría ocurrido.
        RastroDelCursor.Arrancar();

        // Puente clínico: se sondea cada 3 s, no en cada cambio de pantalla. El médico
        // puede guardar la nota DESPUÉS de que SAP ya esté en la pantalla, así que
        // reaccionar solo al cambio de superficie perdería justo ese caso. Cuando no hay
        // código emparejado esto no hace ni una llamada.
        var clinicalTimer = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Background)
        { Interval = TimeSpan.FromSeconds(3) };
        clinicalTimer.Tick += async (_, __) =>
        {
            try { await ClinicalTickAsync(); }
            catch (Exception ex) { LogBus.Log("clinico", $"tick falló: {ex.Message}"); }
        };
        clinicalTimer.Start();

        var mcp = new LocalMcp(_uia);
        // El terreno aprendido, al alcance del cerebro: puede consultar dónde está, qué pantallas
        // conoce y recorrer rutas que nadie enseñó como workflow.
        if (_surfaceMap != null)
            // Lectura INMEDIATA de la superficie, no el valor cacheado: navegar verificando cada
            // salto contra un dato que se refresca cada 800 ms convertía una ruta de cinco tramos
            // en varios segundos de espera por algo que ya había pasado.
        {
            mcp.Map = new SurfaceMapTools(_surfaceMap, () => _locator?.DondeEstoy());

            // La voz en vivo usa EXACTAMENTE estas manos, no unas propias. Darle a la conversación
            // hablada su propio camino para actuar habría significado duplicar el ancla de
            // ubicación, la verificación de llegadas y los vetos — y duplicar una protección es la
            // forma más segura de que una de las dos copias se quede atrás.
            _vivo = new GeminiLive(mcp.Map);
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
                    var lector = new Uia.UiaReader();
                    lector.Read();
                    return lector.Elements
                        .Select(e => (Uia.Reconocedor.SelectorDe(e), e.Label, e.ControlType))
                        .Where(t => t.Item1.Length > 0 && t.Label.Length > 0)
                        .ToList();
                });
            // El mismo vigilante de clics que ya usa el mapa viejo: sin él, el núcleo aprende dónde
            // está y qué ve, pero nunca QUÉ LE TRAJO — y sin eso el grafo no se arma, se queda en
            // islas sueltas sin caminos entre ellas.
            _mapaVivo.Clics = _clickWatcher;

            // LAS MANOS. El núcleo decide qué pulsar; pulsarlo es del mapeador, y se hace con el
            // mismo UiaSurface que ya usa todo lo demás — no hay un segundo camino de accionar.
            _mapaVivo.Pulsar = (selector, etiqueta) =>
            {
                try
                {
                    var superficie = new U.Graph.Surfaces.UiaSurface { SoloEnFoco = true };
                    return superficie.Execute(new U.Graph.PlanStep
                    {
                        StepOrder = 1, ActionType = "click", Selector = selector, Label = etiqueta,
                    }, out _);
                }
                catch (Exception e)
                {
                    LogBus.Log("nucleo-http", $"no pude pulsar «{etiqueta}»: {e.Message}");
                    return false;
                }
            };
            _mapaVivo.Arrancar();

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
            mcp.Map.PorElNucleo = destino => new Navigation.PasoDelNucleo(
                _mapaVivo!.Nucleo,
                () => _locator?.DondeEstoy()?.Id ?? "",
                (sel, etq) => _mapaVivo?.Pulsar?.Invoke(sel, etq) ?? false,
                superficie => Uia.AppAligner.PonerDelante(superficie)).Hasta(destino);

            _servidorNucleo = new Navigation.ServidorDelNucleo(
                _mapaVivo.Nucleo,
                () => _locator?.DondeEstoy()?.Id ?? "",
                (sel, etq) => _mapaVivo?.Pulsar?.Invoke(sel, etq) ?? false,
                superficie => Uia.AppAligner.PonerDelante(superficie),
                (sel, texto) => accionar("input", sel, texto),
                (sel, opcion) => accionar("select", sel, opcion));
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
                MicBtn.Content = viva ? "🔴" : "🎤";
                MicBtn.ToolTip = viva ? "Conversación en vivo — clic para colgar" : "Hablarle a Ü";
                if (viva) ShowTalk();
                // La boca la mueve el audio EN VIVO, que no pasa por VoiceIO: sin esto el gesto
                // quedaba dibujado y sin nadie que lo moviera (2026-08-05).
                ActualizarBoca();
                PintarBotonVoz();

                // Al COLGAR vuelve a su reposo. Sin esto la pastilla se quedaba encendida para
                // siempre: quien la enciende es la conversación, y quien la apagaba era apartar el
                // ratón — que con el botón del collar no ocurre nunca.
                if (!viva && !ZonaVoz.IsMouseOver && !CollapsedFace.IsMouseOver)
                {
                    CrecerPastilla(VozCuerpo, VozFondo, VozIcono, crece: false);
                    EsconderBotonVoz();
                }
            });
            // La pastilla repinta AL MOMENTO en que cambia el origen, y no sólo cuando Ü habla: el
            // temporizador de la boca vive únicamente mientras Ü está hablando, así que encender la
            // voz con el botón del collar se quedaba pintado en gris para siempre si Ü no llegaba a
            // decir palabra (2026-08-14, visto por el usuario con el audio ya entrando por el collar).
            _vivo.FuenteCambio += () => Dispatcher.BeginInvoke(PintarBotonVoz);

            Closed += (_, __) => _vivo?.Dispose();
        }
        // Sonda de desarrollo: permite invocar las MISMAS herramientas MCP desde fuera para
        // comprobar si el terreno es navegable, sin depender de que el modelo decida usarlas.
        // Solo con U_MCP_PROBE=1; en la app del usuario no arranca.
        McpDevProbe.StartIfEnabled(mcp);
        // El backend es Graph: la credencial (X-API-Key) sale del MISMO GraphConfig que usa la
        // ventana de workflows — una sola fuente de key para toda la app.
        _backend = new BackendClient(_config, _graphConfig);
        // "Windows Live": registra al usuario y empieza a emitir telemetría (pulsos consciente/
        // subconsciente + logs) al backend. No-op si el usuario no dio su correo.
        InitTelemetry();
        Closed += (_, __) => TelemetryBus.Shutdown();
        // La primera vez, que se presente ella. No hace nada en los arranques siguientes.
        OfrecerElPrimerEncuentro();
        // La superficie actual viaja en cada turno (scoping de workflows) y las llamadas
        // workflow_* del cerebro se ejecutan con el WorkflowPlayer (subconsciente).
        _workflowRunner = new WorkflowMcpRunner(_graphConfig, this);

        // EL DICTADO CLÍNICO. Se arma aquí porque necesita la configuración de Graph —la clave con
        // la que se pide la sesión de Soniox y se llama al emparejador— y la superficie de SAP.
        // Nace apagado: hasta que no se pulsa el fonendoscopio no abre micrófono ni toca nada.
        _rellenador = new RellenadorSap(_graphConfig, _clinicalSap);
        _dictadoClinico = new DictadoSoniox(_graphConfig, _audioDictado);
        _dictadoClinico.Frase += f => _rellenador.Oido(f);
        // Lo provisional se pinta pero NO se actúa: son palabras que Soniox aún puede corregir.
        _dictadoClinico.Parcial += t => Dispatcher.Invoke(() => SetStatus("🩺 " + Recorte(t, 90)));
        _dictadoClinico.Fallo += m => Dispatcher.Invoke(() => SetStatus("Dictado: " + m));
        _dictadoClinico.Cambio += viva => Dispatcher.Invoke(() =>
        {
            PintarDictado(viva);
            if (viva) { _rellenador.Empezar(); SetStatus("🩺 Escuchando… dicta y los campos se van llenando."); }
            else SetStatus("Dictado terminado.");
        });
        _rellenador.Cuenta += m => Dispatcher.Invoke(() => SetStatus("🩺 " + m));

        // EL EJECUTOR DE EXPORTACIONES. Pregunta al backend si el médico pulsó «Exportar a HC» y,
        // cuando lo hizo, navega y llena la historia clínica. Va encendido desde el arranque y sin
        // botón: el operador no tiene que acordarse de activarlo para que su compañero pueda
        // exportar desde la web. Sin trabajo no hace nada más que una petición cada tres segundos.
        _exportador = new EjecutorDeExportaciones(_graphConfig, _rellenador,
            () => _locator?.DondeEstoy()?.Id ?? "", Dispatcher);
        _exportador.Cuenta += m => Dispatcher.Invoke(() => { SetStatus(m); ShowTalk(); });
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

        // Arrastrar por cualquier zona libre de la barra mueve la ventana (la carita tiene sus
        // propios gestos abajo; los botones se tragan el clic, así que no interfieren). DragMove()
        // es bloqueante y retorna al soltar, así que ahí mismo se anota dónde quedó.
        void OnDragSurface(object _, MouseButtonEventArgs ev)
        {
            if (ev.ButtonState != MouseButtonState.Pressed) return;
            DragMove();   // bloqueante: retorna al soltar
            // Y de ahí se va a un lado, igual que si lo hubieras arrastrado por la carita. Sin
            // velocidad que medir (DragMove no la da), así que manda el borde más cercano.
            EdgeSnap.Aplicar(this, 0, 0, OnWindowMoved);
        }
        BarPanel.MouseLeftButtonDown += OnDragSurface;
        TalkPanel.MouseLeftButtonDown += OnDragSurface;

        // El menú extendido: hover/clic/teclado sobre el activador, cierre con retraso, Backend
        // plegado. Toda la coreografía vive en la región «menú extendido» de abajo.
        WireMenu();

        // La carita colapsada SIGUE al cursor automatizado durante la ejecución de workflows: se ve
        // "quién" está haciendo los clics. Evento estático de UiaSurface; se suelta al cerrar.
        UiaSurface.CursorMoved += OnAutomationCursorMoved;
        Closed += (_, __) => UiaSurface.CursorMoved -= OnAutomationCursorMoved;

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
        // (2026-08-05). Un clic en la carita abre la barra de siempre, intacta.
        ToggleCollapsed();

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
            if (_config.Onboarded) return;

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
        // En la barra el botón es solo el icono ⬇; la versión concreta va en el tooltip.
        _updater.UpdateReady += version => Dispatcher.Invoke(() =>
        {
            UpdateBtn.ToolTip = $"Versión {version} lista — clic para reiniciar (si no, se instala sola al cerrar Ü)";
            ShowUpdate(true);
        });
        _updater.Start();
    }

    private void OnApplyUpdate(object sender, RoutedEventArgs e)
    {
        SetStatus("Actualizando Ü…");
        _updater?.ApplyAndRestart(); // no retorna: reinicia el proceso
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
        if (_updater == null) { SetStatus("El actualizador no está disponible."); ShowTalk(); return; }

        CheckUpdateBtn.IsEnabled = false;
        SetStatus("Buscando actualizaciones…");
        ShowTalk();
        try
        {
            var (que, detalle) = await _updater.BuscarAhoraAsync();
            SetStatus(que switch
            {
                Updater.Busqueda.AlDia => $"Ya tienes la última versión ({detalle}).",
                Updater.Busqueda.Descargada => $"Versión {detalle} descargada. Pulsa ⬇ para reiniciar, o se instala sola al cerrar.",
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
        // Los tooltips dicen el atajo que quedó ACTIVO, no el que se pretendía: si el preferido
        // estaba ocupado y anunciáramos ese, el usuario pulsaría algo que no hace nada.
        foreach (string s in _hotkeys.Activos)
        {
            if (s.Contains("invocar")) MenuActivator.ToolTip = $"Más herramientas (hover abre · clic fija · Esc cierra) · {s.Split(' ')[0]} llama a Ü";
            if (s.Contains("micrófono")) MicBtn.ToolTip = $"Hablarle a Ü · {s.Split(' ')[0]}";
        }
        if (_hotkeys.Resumen.Length > 0) HotkeyStatus.Text = _hotkeys.Resumen;
    }

    /// <summary>
    /// Trae a Ü al frente con el cursor ya dentro de la caja de texto. No mueve la ventana a
    /// propósito: el usuario la dejó donde la dejó, y reubicarla pisaría la posición que se persiste.
    /// </summary>
    private void InvocarPorAtajo()
    {
        _prevForeground = GetForegroundWindow();   // para poder devolver el teclado con Esc
        if (_collapsed) ToggleCollapsed();
        Show();
        Activate();
        ShowTalk(focusInput: true);   // el mismo camino que ya usa AskAsync; no se duplica el foco
        if (!IsActive)
            // Si esto sale en el registro de la máquina del hospital, hará falta el rodeo de
            // SetForegroundWindow + AttachThreadInput. No se implementa por adelantado: que la
            // necesidad la demuestre el log y no una teoría.
            LogBus.Log("atajo", "Activate() no trajo la ventana al frente");
    }

    private void MicPorAtajo()
    {
        _prevForeground = GetForegroundWindow();
        if (_collapsed) ToggleCollapsed();
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

    private bool _collapsed;

    /// <summary>
    /// Alterna entre la barra (con sus flyouts) y SOLO la carita. Al colapsar se cierra el menú
    /// extendido (si estaba abierto) y aparece la carita suelta; al expandir, la barra vuelve intacta
    /// — incluido el globo de conversación si estaba abierto. El tamaño lo pone SizeToContent y el
    /// cambio lo reancla <see cref="OnSizeChanged"/> a la esquina inferior derecha.
    /// </summary>
    private void ToggleCollapsed()
    {
        _collapsed = !_collapsed;
        if (_collapsed)
        {
            CollapsedFace.Mood = Face.Mood; // que la carita suelta refleje el mismo estado
            CloseMenu();
            RootPanel.Visibility = Visibility.Collapsed;
            CollapsedGroup.Visibility = Visibility.Visible;
        }
        else
        {
            CollapsedGroup.Visibility = Visibility.Collapsed;
            EsconderBotonVoz();   // que no se quede encendido al volver a la barra
            RootPanel.Visibility = Visibility.Visible;
        }
        // Colapsada, el contrato es «solo la carita»: la píldora no aparece y el semáforo ES la cara.
        UpdateChip(_mood);
    }

    // --- El botón de voz que asoma al pasar por encima de la carita suelta ---

    /// <summary>
    /// Aparece al acercar el ratón y se va al retirarlo.
    ///
    /// Hablarle es lo que más se hace y estaba escondido detrás de dos gestos que hay que saberse:
    /// abrir la barra, o un doble clic sobre la carita. Un botón permanente al lado sobraría —la
    /// carita vive encima del trabajo de alguien y cuanto menos ocupe, mejor—, así que se enseña
    /// solo cuando la mano ya está ahí, que es justo cuando puede servir.
    ///
    /// Se desvanece, no se quita: quitarlo del árbol movería la carita de sitio, y una cosa que se
    /// mueve cuando te acercas es una cosa que no se deja pulsar.
    /// </summary>
    private void OnCollapsedHoverIn(object sender, System.Windows.Input.MouseEventArgs e)
    {
        PintarBotonVoz();   // que aparezca ya con el aspecto que toca, no con el de la vez anterior
        VoiceDotGrupo.IsHitTestVisible = true;
        VoiceDotGrupo.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    // ── Las dos pastillas: de insinuación a botón ─────────────────────────────────────────────

    /// <summary>
    /// Una pastilla crece hasta ser un botón cuando la mano va hacia ella, y vuelve al soltarla.
    /// </summary>
    /// <remarks>
    /// Dos fases y no una: acercarse a la carita SUGIERE que hay algo —dos barritas mínimas—, y solo
    /// acercarse a una de ellas la convierte en botón. Es lo que hace el Dock de macOS al ampliar el
    /// icono bajo el cursor, y la forma es la del indicador de inicio de iOS: una barra redondeada
    /// que no pide nada mientras no la mires.
    ///
    /// El truco de que no haya dos dibujos: el radio de esquina se queda fijo y grande, así que la
    /// forma la decide el TAMAÑO. A 4x16 se lee pastilla, a 26x26 círculo, y entre medias es una
    /// transición y no un cambio de estado. Un umbral se nota; esto no.
    ///
    /// Y el blanco del gesto mide 26x26 aunque la pastilla mida 4: acertarle a cuatro píxeles sería
    /// puntería, no una interfaz.
    /// </remarks>
    private void CrecerPastilla(System.Windows.Shapes.Rectangle cuerpo,
                                System.Windows.Media.SolidColorBrush fondo,
                                UIElement icono, bool crece)
    {
        var suave = new CubicEase { EasingMode = crece ? EasingMode.EaseOut : EasingMode.EaseIn };
        var dur = TimeSpan.FromMilliseconds(crece ? 160 : 200);

        cuerpo.BeginAnimation(WidthProperty, new DoubleAnimation(crece ? 26 : AnchoPastilla, dur) { EasingFunction = suave });
        cuerpo.BeginAnimation(HeightProperty, new DoubleAnimation(crece ? 26 : AltoPastilla, dur) { EasingFunction = suave });

        // El radio viaja con el tamaño: barrita casi recta arriba, círculo abajo. Si se quedara
        // fijo, o la barrita saldría ovalada o el botón saldría con esquinas de caja.
        var radio = new DoubleAnimation(crece ? 13 : RadioPastilla, dur) { EasingFunction = suave };
        cuerpo.BeginAnimation(System.Windows.Shapes.Rectangle.RadiusXProperty, radio);
        cuerpo.BeginAnimation(System.Windows.Shapes.Rectangle.RadiusYProperty, radio);

        // Y SE SEPARAN AL ABRIRSE. En reposo van casi pegadas —dos marcas de una misma cosa—; al
        // convertirse en botones necesitan aire, porque ya no son una marca sino dos sitios donde
        // pulsar, y dos botones pegados se pulsan mal. La separación viaja con la forma, así que no
        // hay un instante en que se note el reajuste.
        ZonaChat.BeginAnimation(MarginProperty, new ThicknessAnimation(
            new Thickness(0, crece ? 7 : 1, 0, 0), dur) { EasingFunction = suave });
        icono.BeginAnimation(OpacityProperty, new DoubleAnimation(crece ? 1 : 0,
            TimeSpan.FromMilliseconds(crece ? 130 : 110)) { EasingFunction = suave });

        // Y el color va con la forma: barrita clara sobre lo que haya detrás, botón oscuro con el
        // icono en blanco. Animar el color y no cambiarlo de golpe es lo que evita el parpadeo.
        fondo.BeginAnimation(System.Windows.Media.SolidColorBrush.ColorProperty, new ColorAnimation(
            crece ? System.Windows.Media.Color.FromArgb(0xE6, 0x20, 0x20, 0x22) : ColorPastilla, dur));
    }

    // El reposo de la pastilla, en UN SITIO: lo pone el XAML al nacer y lo restaura la animación al
    // encogerse, y si cada uno lleva su copia acaban discrepando — la pastilla nacería de un tamaño
    // y volvería a otro después del primer hover, que es de esos fallos que solo se ven a la
    // segunda vez (2026-08-07).
    private const double AnchoPastilla = 4.5, AltoPastilla = 10, RadioPastilla = 2;

    /// <summary>Gris apagado al 50 %: se ve que hay algo y no compite con la carita.</summary>
    private static readonly System.Windows.Media.Color ColorPastilla =
        System.Windows.Media.Color.FromArgb(0x80, 0xA8, 0xA8, 0xAE);

    private void OnZonaVozEntra(object sender, System.Windows.Input.MouseEventArgs e)
        => CrecerPastilla(VozCuerpo, VozFondo, VozIcono, crece: true);

    private void OnZonaVozSale(object sender, System.Windows.Input.MouseEventArgs e)
        => CrecerPastilla(VozCuerpo, VozFondo, VozIcono, crece: false);

    private void OnZonaChatEntra(object sender, System.Windows.Input.MouseEventArgs e)
        => CrecerPastilla(ChatCuerpo, ChatFondo, ChatIcono, crece: true);

    private void OnZonaChatSale(object sender, System.Windows.Input.MouseEventArgs e)
        => CrecerPastilla(ChatCuerpo, ChatFondo, ChatIcono, crece: false);

    /// <summary>Pulsar la pastilla de voz es lo mismo que el doble clic en la cara: ni abre la barra
    /// ni mueve nada, solo empieza (o cuelga) la conversación.</summary>
    private void OnMicDesdePastilla(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;   // que el clic no llegue a la carita y le cuente como gesto
        StartMicByFace();
    }

    /// <summary>Y la de abajo abre el globo para escribirle, que es la otra forma de hablarle.</summary>
    private void OnChatDesdePastilla(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        PlayTick();
        if (_talkOpen) HideTalk(); else ShowTalk(focusInput: true);
    }

    /// <summary>La pastilla en rojo mientras escucha: un micrófono abierto que no se ve es lo último
    /// que quiere nadie, y aquí además está escribiendo en una historia clínica.</summary>
    private void PintarDictado(bool escuchando)
    {
        DictadoFondo.Color = escuchando
            ? System.Windows.Media.Color.FromRgb(0xE5, 0x3E, 0x3E)
            : System.Windows.Media.Color.FromArgb(0x80, 0xA8, 0xA8, 0xAE);
        ZonaDictado.ToolTip = escuchando
            ? "Dictando… pulsa para parar"
            : "Dictar y rellenar los campos de SAP";
    }

    private static string Recorte(string t, int n) => t.Length <= n ? t : "…" + t[^n..];

    private void OnZonaDictadoEntra(object sender, System.Windows.Input.MouseEventArgs e)
        => CrecerPastilla(DictadoCuerpo, DictadoFondo, DictadoIcono, crece: true);

    private void OnZonaDictadoSale(object sender, System.Windows.Input.MouseEventArgs e)
        => CrecerPastilla(DictadoCuerpo, DictadoFondo, DictadoIcono, crece: false);

    /// <summary>
    /// EL FONENDOSCOPIO: dictar y que los campos se vayan llenando solos.
    /// </summary>
    /// <remarks>
    /// NO ES UN MODO DEL MICRÓFONO, y por eso tiene botón propio. El micrófono abre una CONVERSACIÓN
    /// con Ü —oye, piensa, contesta en voz alta, llama herramientas—. Esto no conversa: transcribe
    /// con Soniox, organiza la nota, y escribe. Meterlo dentro del micrófono habría obligado a
    /// decidir en cada frase si era una orden o un dato clínico, y equivocarse ahí significa o bien
    /// contestarle a un médico que está dictando, o bien escribir en la historia lo que era una
    /// orden para Ü.
    ///
    /// LA COMPUERTA ES LA PANTALLA, no un permiso: fuera del triage no hay campos que llenar, así
    /// que encenderlo sería prometer un trabajo imposible. Se dice dónde hay que estar en vez de
    /// quedarse mudo — un botón que no hace nada y no explica por qué se prueba tres veces.
    /// </remarks>
    private async void OnDictadoDesdePastilla(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        PlayTick();
        if (_dictadoClinico == null) { SetStatus("El dictado clínico no está disponible."); ShowTalk(); return; }

        if (_dictadoClinico.Activo) { await _dictadoClinico.PararAsync(); return; }

        // DOS MICRÓFONOS ABIERTOS ES PEOR QUE NINGUNO: se mezclarían dos flujos y, peor, lo dicho
        // iría a la vez a la conversación y a la historia clínica. Se dice cuál hay que cerrar en
        // vez de cerrarla por nuestra cuenta: el que está hablando es quien decide.
        if (_vivo?.Viva == true)
        {
            SetStatus("Cuelga la conversación antes de dictar: no pueden oírte los dos a la vez.");
            ShowTalk();
            return;
        }

        string donde = _locator?.DondeEstoy()?.Id ?? "";
        if (!RellenadorSap.EsLaPantallaDeTriage(donde))
        {
            SetStatus("El dictado clínico solo funciona en la pantalla de triage de SAP.");
            ShowTalk();
            return;
        }

        ShowTalk();
        await _dictadoClinico.ArrancarAsync();
    }

    private void OnCollapsedHoverOut(object sender, System.Windows.Input.MouseEventArgs e) => EsconderBotonVoz();

    /// <summary>
    /// El punto dice si la conversación está viva, y respira con lo que se está diciendo.
    /// </summary>
    /// <remarks>
    /// Con el micrófono abierto, el botón se veía EXACTAMENTE igual que apagado: la única señal de
    /// que había una conversación en marcha estaba en la barra, que es justo lo que no se ve cuando
    /// la carita está sola (2026-08-06).
    ///
    /// La señal es de dos partes, y las dos son suaves a propósito. El punto se INVIERTE —claro con
    /// el micrófono oscuro— que es un cambio que se reconoce de reojo sin gritar. Y un halo detrás
    /// crece con el volumen de la voz: no es un adorno que late solo, es el mismo nivel que mueve la
    /// boca, así que lo que se ve pulsar es lo que se está oyendo.
    ///
    /// El halo vive fuera del botón para poder crecer más que él sin empujar nada: dentro, cada
    /// latido movería la carita de sitio.
    /// </remarks>
    private void PintarBotonVoz()
    {
        bool viva = _vivo?.Viva == true;

        // Con la conversación abierta, la pastilla de voz no espera a que te acerques: se queda
        // encendida. Lo que está pasando ahora mismo no puede depender de dónde tengas el ratón.
        if (viva && !ZonaVoz.IsMouseOver)
            CrecerPastilla(VozCuerpo, VozFondo, VozIcono, crece: true);

        // Y EL GRUPO ENTERO TIENE QUE ESTAR VISIBLE, que es lo que faltaba: nace con Opacity=0 y sólo
        // se encendía al acercar el ratón a la carita. La pastilla crecía dentro de un contenedor
        // transparente, así que con la voz abierta por el botón del collar —sin ratón de por medio—
        // no se veía absolutamente nada (2026-08-13, lo vio el usuario). Crecer no es aparecer.
        if (viva)
        {
            VoiceDotGrupo.BeginAnimation(OpacityProperty, null);
            VoiceDotGrupo.Opacity = 1;
            VoiceDotGrupo.IsHitTestVisible = true;
        }

        // DE QUÉ COLOR SE ESTÁ OYENDO. Azul = por el collar; el gris de siempre = por un micrófono
        // del PC. Es la única forma de saber cuál de los dos te está escuchando sin abrir el log, y
        // cambia sola si hay relevo a media conversación.
        VozFondo.Color = !viva
            ? System.Windows.Media.Color.FromArgb(0x80, 0xA8, 0xA8, 0xAE)
            : _vivo!.PorElCollar
                ? System.Windows.Media.Color.FromRgb(0x3E, 0x9B, 0xFF)
                : System.Windows.Media.Color.FromArgb(0xC0, 0xA8, 0xA8, 0xAE);

        if (!viva)
        {
            VoiceHalo.Opacity = 0;
            VoiceHaloEscala.ScaleX = VoiceHaloEscala.ScaleY = 1;
            return;
        }

        // Al hablar late con la voz; callada, un latido lento que solo dice «sigo aquí».
        double nivel = _vivo!.NivelVoz;
        double fuerza = nivel > 0.004
            ? Math.Min(1, Math.Pow(nivel, 0.55) * 1.45)
            : 0.18 + 0.10 * Math.Sin(_bocaPaso * 0.16);

        VoiceHalo.Opacity = 0.10 + fuerza * 0.22;
        double escala = 1.15 + fuerza * 0.55;
        VoiceHaloEscala.ScaleX = VoiceHaloEscala.ScaleY = escala;
    }

    private void EsconderBotonVoz()
    {
        // Con la conversación abierta NO se esconde: mientras Ü escucha, esa pastilla es lo único
        // que lo dice, y retirarla al apartar el ratón sería quitar la señal justo cuando importa.
        if (_vivo?.Viva == true) return;

        VoiceHalo.BeginAnimation(OpacityProperty, null);
        VoiceHalo.Opacity = 0;
        VoiceDotGrupo.IsHitTestVisible = false;
        VoiceDotGrupo.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        });
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
        // Carita de la barra: arrastra la barra entera, y al soltar se va a un lado como la suelta.
        new FaceGestures(this, Face)
        {
            SingleTap = () => { PlayTick(); ToggleCollapsed(); },
            DoubleTap = StartMicByFace,
            LongPress = CycleTheme,
            // Un solo callback alimenta las tres cosas que dependen de dónde quedó la barra: recordar
            // el sitio, espejar el layout al lado que toque, y hacia dónde abrirá el menú. Llega con
            // el DESTINO, así que el espejo se aplica al empezar el vuelo y no al terminarlo — la
            // barra viaja ya con su forma final en vez de darse la vuelta al aterrizar.
            Moved = OnWindowMoved,
        };

        // Carita suelta (colapsada): mismos gestos, mismo pegado al borde.
        new FaceGestures(this, CollapsedFace)
        {
            SingleTap = () => { PlayTick(); ToggleCollapsed(); },
            DoubleTap = StartMicByFace,
            LongPress = CycleTheme,
            // Un solo callback alimenta las tres cosas que dependen de dónde quedó la barra: recordar
            // el sitio, espejar el layout al lado que toque, y hacia dónde abrirá el menú. Llega con
            // el DESTINO, así que el espejo se aplica al empezar el vuelo y no al terminarlo — la
            // barra viaja ya con su forma final en vez de darse la vuelta al aterrizar.
            Moved = OnWindowMoved,
        };

        // Y con dos dedos en el trackpad, sin tener que agarrarla. Solo con la carita suelta: con la
        // barra abierta el scroll es del menú, y robárselo sería quitarle una función que sí tiene.
        _ = new LanzarConScroll(this, () => _collapsed,
            (vx, vy) => EdgeSnap.Aplicar(this, vx, vy, OnWindowMoved));
    }

    /// <summary>Doble clic en la carita = micrófono (como el doble toque de Android). El carrillón del
    /// doble clic predomina: no se solapa con el tick del clic simple porque el gesto ya se resolvió
    /// como doble antes de sonar nada.</summary>
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
            if (_collapsed) ToggleCollapsed();
            Show();
            Activate();
            PlayTick();
            LogBus.Log("atajo", "doble Ctrl: Ü estaba oculta y vuelve a la vista");
            return;
        }
        StartMicByFace();
    }

    // --- Sonidos (los MISMOS WAV de Android): tick al clic, carrillón al micrófono ---

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
        // MANTENER PULSADO EL MICRÓFONO = entrar por el collar. Se pregunta ANTES de alternar, para
        // que la sesión nazca ya pidiendo collar en vez de abrir con el micrófono local y mudarse.
        // El clic corto sigue haciendo exactamente lo de siempre: el gesto nuevo no le quita nada al
        // que ya existía (2026-08-13, pedido por el usuario).
        if (TomarPulsacionLarga()) PasarLaVozAlCollar();

        if (_vivo != null) { await _vivo.AlternarAsync(); return; }

        SetStatus("Escuchando…");
        ShowTalk(); // que «Escuchando…» y lo que se entienda queden a la vista
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

    /// <summary>Cuánto hay que mantener pulsado el micrófono para pedir el collar.</summary>
    private static readonly TimeSpan Sostenido = TimeSpan.FromMilliseconds(500);

    private DateTime _micPulsado;

    private void OnMicDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => _micPulsado = DateTime.UtcNow;

    /// <summary>
    /// ¿El clic que se acaba de soltar venía de mantener pulsado? Se CONSUME al preguntar.
    ///
    /// Consumirlo importa: a <c>OnMic</c> se llega también por el doble clic en la carita, por el
    /// atajo global y por la pastilla de voz, y esos no pasan por el botón. Sin borrar la marca, una
    /// pulsación larga de hace media hora haría que el siguiente doble clic pidiera collar sin que
    /// nadie lo hubiera pedido.
    /// </summary>
    private bool TomarPulsacionLarga()
    {
        if (_micPulsado == default) return false;
        bool largo = DateTime.UtcNow - _micPulsado >= Sostenido;
        _micPulsado = default;
        return largo;
    }

    private PanelDelCollar? _panelCollar;

    /// <summary>La pantalla del collar: enlazar, ver el estado, quitar el enlace.</summary>
    private void OnCollar(object sender, RoutedEventArgs e)
    {
        PlayTick();
        if (_panelCollar is { IsVisible: true }) { _panelCollar.Activate(); return; }
        _panelCollar = new PanelDelCollar { Owner = this };
        _panelCollar.Closed += (_, __) => _panelCollar = null;
        _panelCollar.Show();
    }

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
    }

    /// <summary>
    /// Callar/dejar hablar a Ü. Un solo clic corta la frase en curso y lo deja mudo — antes, si arrancaba
    /// a hablar en mal momento, no había forma de pararlo salvo cerrar la aplicación.
    /// </summary>
    private void OnToggleMute(object sender, RoutedEventArgs e)
    {
        SetMuted(!_voice.Muted);
        _config.Save();
        SetStatus(_voice.Muted ? "Ü en silencio" : "Ü vuelve a hablar");
    }

    private void SetMuted(bool muted)
    {
        _voice.Muted = muted;   // el setter ya corta en seco lo que estuviera diciendo
        _config.Muted = muted;
        MuteBtn.Content = muted ? "🔇" : "🔊";
        MuteBtn.ToolTip = muted ? "Ü está en silencio — clic para que vuelva a hablar" : "Callar a Ü ahora mismo";
    }

    /// <summary>
    /// Lo que hace Ü con self_mute/self_hide/self_close, pedido por VOZ. Es <see cref="GeminiLive.Autocontrol"/>.
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
                if (_collapsed) ToggleCollapsed();
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
            ProcessVideo = _config.ProcessTeachVideo, // toggle del panel Backend: procesar o no el video con IA
        };
        _teachSession.StatusChanged += (_, msg) => Dispatcher.Invoke(() => SetStatus(msg));

        SetTeachingUi(true);
        ShowTalk(); // el conteo regresivo y el estado de la grabación se ven ahí
        try
        {
            // Título vacío: se autogenera al final desde lo aprendido (WorkflowLearner en Graph).
            // StartAsync incluye un countdown de 3s para que cambies a la app que vas a enseñar.
            await _teachSession.StartAsync("", CancellationToken.None);
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
        TeachBtn.Content = teaching ? "⏸" : "🎓";
        TeachBtn.ToolTip = teaching
            ? "Enseñando: clic para terminar y guardar lo aprendido"
            : "Enseñar: Ü graba la pantalla y tu voz para aprender un workflow";
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

    private async Task StopTeachingAsync()
    {
        SetTeachingUi(false);
        SetStatus("Cerrando la enseñanza y estructurando el workflow…");
        ShowTalk(); // el cierre tarda y termina en un veredicto: que no pase en silencio

        if (_teachSession != null)
        {
            try
            {
                FinishResponse finish = await _teachSession.StopAsync(CancellationToken.None);
                // El título se autogeneró en Graph a partir de lo aprendido (el summary del finish).
                // Sin voz aquí a propósito: el resultado se muestra en silencio en el estado.
                string title = string.IsNullOrWhiteSpace(finish.Summary) ? "el workflow" : finish.Summary;
                SetStatus($"Aprendido: {title}");
                _ = ReloadDirectWorkflowsAsync(); // que el recién enseñado aparezca ya en el selector
            }
            catch (FinishPendingException pending)
            {
                // NO es lo mismo que perder la grabación, y decirlo importa: los pasos ya están en
                // Graph. Se guarda el id para completar el resumen luego, sin regrabar nada.
                PendingFinish.Save(pending.SessionId, pending.WorkflowId);
                LogBus.Log("teach", $"cierre pendiente (HTTP {pending.StatusCode}): {pending.Message}");
                SetStatus("Los pasos SÍ se guardaron; falta el resumen (Graph tardó de más). "
                        + "Se completa solo al reabrir la app.");
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

    private void UpdateVideoLlmToggle() =>
        VideoLlmToggle.Content = _config.ProcessTeachVideo
            ? "🎬 Video → IA: activado"
            : "🎬 Video → IA: desactivado";

    /// <summary>
    /// Alterna si la enseñanza procesa el video con el LLM. Apagado evita el timeout (504) del backend;
    /// el video se sigue grabando y guardando (visible en 🎞 Videos). Se persiste en Config y se aplica
    /// a la próxima enseñanza (se lee al crear la WorkflowTeachSession).
    /// </summary>
    private void OnToggleVideoLlm(object sender, RoutedEventArgs e)
    {
        _config.ProcessTeachVideo = !_config.ProcessTeachVideo;
        _config.Save();
        UpdateVideoLlmToggle();
        SetStatus(_config.ProcessTeachVideo
            ? "Al enseñar, el video se procesará con IA"
            : "Al enseñar, el video NO se procesa con IA (se graba igual; míralo en Videos)");
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
    private bool _backendOpen;
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

        // Zona segura: menú y barra cancelan el cierre al entrar y lo agendan al salir.
        MenuPanel.MouseEnter += (_, __) => _menuCloseTimer.Stop();
        MenuPanel.MouseLeave += (_, __) => ScheduleMenuClose();
        BarPanel.MouseEnter += (_, __) => _menuCloseTimer.Stop();
        BarPanel.MouseLeave += (_, __) => ScheduleMenuClose();

        // Backend: solo clic. Sin MouseEnter/MouseLeave a propósito — ver la nota de arriba.
        BackendHeader.MouseLeftButtonUp += (_, __) => ToggleBackend();
        BackendHeader.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { ToggleBackend(); e.Handled = true; } };

        // Globo de conversación: 💬 lo alterna, ✕ lo cierra.
        TalkBtn.Click += (_, __) => { if (_talkOpen) HideTalk(); else ShowTalk(focusInput: true); };
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
        DockPanel.SetDock(BarPanel, left ? Dock.Left : Dock.Right);
        DockPanel.SetDock(StatusChip, left ? Dock.Left : Dock.Right);

        MenuPanel.HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        BarRow.HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        CollapsedGroup.HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right;

        // El botón de voz se pone del lado de FUERA: pegada al borde izquierdo, a la derecha de la
        // carita; pegada al derecho, a su izquierda. Si no, quedaría contra el borde de la pantalla.
        //
        // SOLO SI DE VERDAD CAMBIA. Esto se llama en cada movimiento de la ventana —también al
        // empezar un lanzamiento—, y sacar y volver a meter los hijos fuerza una pasada de layout
        // entera sobre una ventana que se está midiendo sola (SizeToContent). Reconstruir el árbol
        // para dejarlo exactamente igual es trabajo tirado, y trabajo tirado en mitad de una
        // animación se nota.
        bool caraPrimero = CollapsedGroup.Children.Count > 0 && CollapsedGroup.Children[0] == CollapsedFace;
        if (caraPrimero != left)
        {
            CollapsedGroup.Children.Clear();
            if (left) { CollapsedGroup.Children.Add(CollapsedFace); CollapsedGroup.Children.Add(VoiceDotGrupo); }
            else { CollapsedGroup.Children.Add(VoiceDotGrupo); CollapsedGroup.Children.Add(CollapsedFace); }
            VoiceDotGrupo.Margin = left ? new Thickness(10, 0, 0, 0) : new Thickness(0, 0, 10, 0);
        }

        // Los tooltips salían siempre por la izquierda: pegados al borde izquierdo se saldrían de la
        // pantalla. Es un ajuste por botón porque ToolTipService.Placement no se hereda.
        foreach (var b in BarButtons())
            ToolTipService.SetPlacement(b, left ? System.Windows.Controls.Primitives.PlacementMode.Right
                                               : System.Windows.Controls.Primitives.PlacementMode.Left);

        // Y la píldora respira hacia el lado contrario a la barra.
        StatusChip.Margin = left ? new Thickness(8, 0, 0, 14) : new Thickness(0, 0, 8, 14);
        TalkPanel.Margin = left ? new Thickness(8, 0, 0, 0) : new Thickness(0, 0, 8, 0);
    }

    private bool _sideApplied;

    private IEnumerable<Button> BarButtons()
    {
        foreach (object child in ((StackPanel)BarPanel.Child).Children)
        {
            if (child is Button b) yield return b;
            else if (child is StackPanel zona)
                foreach (object nieto in zona.Children)
                    if (nieto is Button nb) yield return nb;
        }
    }

    /// <summary>Recoloca el espejo según dónde está la ventana. Barato: sale pronto si no cambia.</summary>
    private void RefreshBarSide() => ApplyBarSide(EdgeSnap.EstáALaIzquierda(this));

    /// <summary>
    /// La ventana acabó en un sitio nuevo por voluntad del usuario. Llega con el DESTINO, así que
    /// todo lo que dependa del lado se aplica mientras la barra todavía está viajando.
    /// </summary>
    private void OnWindowMoved(double left, double top)
    {
        SavePositionSoon(left, top);
        var wa = SystemParameters.WorkArea;
        ApplyBarSide(left + ActualWidth / 2 < (wa.Left + wa.Right) / 2);
        RefreshRestingChevron();
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
        // Backend se queda como lo dejaste. Antes se replegaba al cerrar el menú porque podía haberse
        // abierto de refilón, por hover; ahora abrirlo cuesta un clic deliberado y deshacerlo a sus
        // espaldas sería contradecir lo que el usuario pidió.
    }

    /// <summary>Abrir/cerrar Backend con clic (o Enter/Espacio) en su cabecera.</summary>
    private void ToggleBackend()
    {
        if (_backendOpen) CloseBackend(); else OpenBackend();
    }

    private void OpenBackend()
    {
        if (_backendOpen) return;
        _backendOpen = true;
        BackendBody.Visibility = Visibility.Visible;
        BackendBody.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        Rotate(BackendRot, 90);
    }

    private void CloseBackend()
    {
        if (!_backendOpen) return;
        _backendOpen = false;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120));
        fade.Completed += (_, __) => { if (!_backendOpen) BackendBody.Visibility = Visibility.Collapsed; };
        BackendBody.BeginAnimation(OpacityProperty, fade);
        Rotate(BackendRot, 0);
    }

    // ── Globo de conversación (estado + narración + entrada de texto) ─────────────────────────

    /// <summary>
    /// Muestra el globo de conversación. Se llama solo cuando hay algo que ver: una narración, una
    /// pregunta del asistente, una ejecución en curso. Con la carita colapsada no hace nada (mismo
    /// contrato de siempre: colapsado = solo la carita).
    /// </summary>
    private void ShowTalk(bool focusInput = false)
    {
        if (_collapsed) return;
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
        try
        {
            var raw = await _directGraph.ListWorkflowsAsync(CancellationToken.None);
            _directWorkflows.Clear();
            _directWorkflows.AddRange(raw.Select(WorkflowSummary.FromJson));

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

            if (_directIndex >= _directWorkflows.Count) _directIndex = 0;
            bool many = _directWorkflows.Count > 1;
            WorkflowPrev.IsEnabled = many;
            WorkflowNext.IsEnabled = many;
            RunWorkflowBtn.IsEnabled = true;
            DryRunBtn.IsEnabled = true;
            WorkflowDeleteBtn.IsEnabled = true;
            SetDirectIndex(_directIndex);
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
        DryRunBtn.IsEnabled = false;
        WorkflowDeleteBtn.IsEnabled = false;
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
            $"¿Borrar «{wf.Title}»?\n\nNo se puede deshacer.",
            "Borrar workflow", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        WorkflowDeleteBtn.IsEnabled = false;
        try
        {
            _directGraph ??= new GraphClient(_graphConfig);
            await _directGraph.DeleteWorkflowAsync(wf.Id, CancellationToken.None);
            SetStatus($"Borrado: «{wf.Title}»");
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
        WorkflowViewToggle.ToolTip = _directListMode ? "Ver como carrusel" : "Ver como lista";
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
        WorkflowPick.Text = $"{_directIndex + 1}/{_directWorkflows.Count} · {wf.Title} ({wf.StepCount} paso(s))";

        if (!_syncingWorkflowUi && WorkflowListBox.SelectedIndex != _directIndex)
        {
            _syncingWorkflowUi = true;
            WorkflowListBox.SelectedIndex = _directIndex;
            WorkflowListBox.ScrollIntoView(wf);
            _syncingWorkflowUi = false;
        }
    }

    private bool _stepMode;
    private StepDebuggerWindow? _debugger;

    // ── Dictado clínico: hablar y que los campos se llenen ──────────────────────
    //
    // VA APARTE DEL PUENTE DE ABAJO, que es otra cosa: aquel trae valores YA GUARDADOS por el
    // médico en el portal y los ofrece para aprobación; esto escucha en directo y escribe sin
    // preguntar. Comparten la superficie de SAP y nada más.
    private DictadoSoniox? _dictadoClinico;
    private RellenadorSap? _rellenador;

    /// <summary>Quien atiende los «Exportar a HC» que llegan de la web. Vive todo el rato.</summary>
    private EjecutorDeExportaciones? _exportador;

    /// <summary>Su propio micrófono, y NO el de la conversación viva. Son dos sesiones de audio con
    /// destinos distintos; compartir una obligaría a decidir en cada frase a quién iba dirigida.</summary>
    private readonly Voice.LiveAudio _audioDictado = new();

    // ── Puente con la consulta del portal ───────────────────────────────────────
    private readonly ClinicalBridge _clinical = new();
    private readonly SapGuiSurface _clinicalSap = new();
    private bool _offering;             // ya hay un ofrecimiento en pantalla
    private string _offeredRev = "";    // no ofrecer dos veces lo mismo
    private string _focusedRev = "";    // no robar el foco más de una vez por versión
    private int _clinicalStep = -1;

    /// <summary>
    /// El paso del circuito, a la vista. Sin esto el operador solo veía «Emparejado» y no
    /// tenía forma de saber cuál de las dos condiciones faltaba —la nota guardada o la
    /// pantalla de SAP—, que es justo lo que hay que poder mirar de un vistazo en vivo.
    ///
    /// Solo se escribe cuando el paso CAMBIA: repintar el mismo texto cada 3 s haría
    /// parpadear el panel y ensuciaría el registro.
    /// </summary>
    private void SetClinicalStep(int step, string text)
    {
        if (_clinicalStep == step) return;
        _clinicalStep = step;
        string[] marks = { "①", "②", "③", "④" };
        string prefix = step >= 1 && step <= 4 ? $"{marks[step - 1]} " : "";
        Dispatcher.Invoke(() => ClinicalStatus.Text = prefix + text);
        LogBus.Log("clinico", $"paso {step}: {text}");
    }

    private void OnClinicalPair(object sender, RoutedEventArgs e)
    {
        if (_clinical.Active)
        {
            _clinical.Unpair();
            ClinicalCodeBox.Text = "";
            ClinicalPairBtn.Content = "Emparejar";
            ClinicalStatus.Text = "Sin emparejar.";
            _config.ClinicalCode = "";   // soltar es soltar: no debe resucitar al reiniciar
            _config.Save();
            return;
        }

        _clinical.Pair(ClinicalCodeBox.Text);
        if (!_clinical.Active)
        {
            ClinicalStatus.Text = "El código son 8 caracteres.";
            return;
        }

        _config.ClinicalCode = _clinical.Code;   // se teclea una vez por instalación, no por arranque
        _config.Save();

        _offeredRev = "";
        _focusedRev = "";
        _clinicalStep = -1;
        ClinicalPairBtn.Content = "Soltar";
        SetClinicalStep(1, "Esperando a que guardes la nota en el portal.");
    }

    /// <summary>
    /// ¿Esta pantalla admite datos de la consulta? Se decide INTENTANDO emparejar los
    /// conceptos con los campos que hay delante, no comparando la transacción contra una
    /// lista. Es autoverificable: si en la pantalla no existen «Talla», «Peso» y compañía,
    /// no se empareja nada y no se ofrece — sin depender de un código de transacción que
    /// puede cambiar entre hospitales o entre versiones.
    /// </summary>
    private async Task ClinicalTickAsync()
    {
        if (!_clinical.Active || _clinical.Stopped || _offering || _teaching || _runningDirect) return;

        // EL CAMINO NUEVO MANDA. Mientras el ejecutor de exportaciones esté escuchando, este puente
        // se calla: son dos sistemas queriendo llenar la MISMA pantalla, y el viejo abre una ventana
        // de aprobación que roba el foco a mitad de la escritura del nuevo. El usuario lo vio en
        // vivo el 2026-08-14 — el popup de «voy a escribir 2 dato(s)» apareciendo encima mientras el
        // exportador estaba trabajando.
        //
        // No se borra: el puente sigue entero y vuelve solo si el ejecutor no está. Pero dos cosas
        // escribiendo a la vez en una historia clínica no es una redundancia útil, es una carrera.
        if (_exportador?.Encendido == true) return;

        // ── PASO 1: ¿la nota ya está guardada allá? ─────────────────────────────
        var data = await _clinical.FetchAsync(CancellationToken.None);
        if (data.Count == 0)
        {
            SetClinicalStep(1, "Esperando a que guardes la nota en el portal.");
            return;
        }

        // ── PASO 2: ¿SAP está delante? ──────────────────────────────────────────
        // Inmediato: de esto depende traer SAP al frente o no, y con el valor cacheado se decidía
        // sobre una pantalla de hasta 800 ms antes.
        var loc = _locator?.DondeEstoy();
        bool enSap = loc != null && loc.Origin.StartsWith("sapgui://", StringComparison.OrdinalIgnoreCase);
        if (!enSap)
        {
            // Los datos ya están; lo único que falta es SAP. Se trae al frente UNA vez
            // por versión de la nota: insistir cada 3 s le robaría el teclado al operador
            // mientras escribe en otro sitio, que es exactamente lo que no debe pasar.
            SetClinicalStep(2, $"{data.Count} dato(s) listos. Abriendo SAP…");
            if (_clinical.LastRev != _focusedRev)
            {
                _focusedRev = _clinical.LastRev;
                LogBus.Log("clinico", "datos listos y SAP no está delante: se trae al frente");
                try { await AppAligner.EnsureAsync("sapgui://", () => _locator?.DondeEstoy()?.Origin ?? "", CancellationToken.None); }
                catch (Exception e) { LogBus.Log("clinico", $"no se pudo traer SAP al frente: {e.Message}"); }
            }
            return;
        }

        if (_clinical.LastRev == _offeredRev) return;   // ya se ofreció esta versión

        string donde = loc?.Id ?? "SAP";

        // ── PASO 3: ¿esta pantalla tiene los campos? ────────────────────────────
        IReadOnlyList<DetectedField> fields;
        try { fields = await Task.Run(() => _clinicalSap.ReadFields()); }
        catch { return; }

        var bindings = ConceptBinder.Bind(data, fields);
        int escribibles = bindings.Count(b => !b.Occupied);
        if (escribibles == 0)
        {
            string why = bindings.Count == 0
                ? "Estás en SAP, pero esta pantalla no tiene campos de signos vitales."
                : "Todos los campos ya tienen valor: no se toca nada.";
            SetClinicalStep(3, why);
            LogBus.Log("clinico", $"{why} ({fields.Count} campo(s) leídos en '{loc!.Id}')");
            return;
        }

        // ── PASO 4: todo listo, se pide aprobación ──────────────────────────────
        _offering = true;
        _offeredRev = _clinical.LastRev;
        try
        {
            SetClinicalStep(4, $"Esperando tu aprobación para {escribibles} dato(s).");
            LogBus.Log("clinico", $"ofreciendo {escribibles} dato(s) en '{donde}'");

            var preview = new FillPreviewWindow(bindings, donde);
            bool ok = await preview.AskAsync();
            if (!ok)
            {
                LogBus.Log("clinico", "el operador canceló: no se escribió nada");
                _clinicalStep = -1;
                SetClinicalStep(3, "Cancelado. Se vuelve a ofrecer si cambias de pantalla o de nota.");
                _offeredRev = ""; // cancelar no es rechazar para siempre
                return;
            }

            int escritos = await Task.Run(() => Write(bindings));
            LogBus.Log("clinico", $"escritos {escritos}/{escribibles} dato(s) en SAP");
            _clinicalStep = -1;
            SetClinicalStep(4, $"✓ Escritos {escritos} dato(s) en SAP.");
        }
        finally { _offering = false; }
    }

    /// <summary>
    /// Escribe lo aprobado. Los ocupados NO se tocan — la regla no se comprueba solo al
    /// mostrar: se vuelve a comprobar aquí, porque entre la vista previa y el clic el
    /// operador pudo haber escrito en el campo.
    /// </summary>
    private int Write(IReadOnlyList<Binding> bindings)
    {
        int n = 0;
        foreach (Binding b in bindings)
        {
            if (b.Occupied) continue;
            var step = PlanStep.ForAutofill(b.Field, new FieldMatch { StepOrder = b.Field.StepOrder, Value = b.Data.Value });
            try
            {
                if (_clinicalSap.Execute(step, out string err)) n++;
                else LogBus.Log("clinico", $"«{b.FieldLabel}» no se pudo escribir: {err}");
            }
            catch (Exception e) { LogBus.Log("clinico", $"«{b.FieldLabel}» lanzó: {e.Message}"); }
        }
        return n;
    }

    /// <summary>
    /// Muestra u oculta el mapa del grafo. Se reconstruye al abrir —no en cada tick— porque los
    /// workflows cambian al grabar, no al navegar; y la iluminación del nodo actual sí es en vivo,
    /// por el mismo evento del locator que alimenta el badge.
    /// </summary>
    private async void OnToggleMap(object sender, RoutedEventArgs e)
    {
        if (_map != null)
        {
            _map.Close();
            _map = null;
            MapBtn.Content = "🗺 Mapa del grafo";
            return;
        }

        if (!_graphConfig.IsConfigured)
        {
            SetStatus("Configura Graph (URL + API key) para ver el mapa.");
            return;
        }

        _map = new WorkflowMapWindow(_surfaceMap);
        _map.Show();
        MapBtn.Content = "🗺 Mapa: cargando…";
        _directGraph ??= new GraphClient(_graphConfig);
        try
        {
            await _map.LoadAsync(_directGraph, CancellationToken.None);
            MapBtn.Content = "🗺 Mapa: visible — clic para ocultar";
            _map.SetCurrent(_locator?.DondeEstoy()?.Id ?? "");
        }
        catch (Exception ex)
        {
            LogBus.Log("mapa", $"no se pudo construir el grafo: {ex.Message}");
            SetStatus($"El mapa no cargó: {ex.Message}");
            _map.Close();
            _map = null;
            MapBtn.Content = "🗺 Mapa del grafo";
        }
    }

    private GraphExplorerWindow? _explorer;

    private void OnToggleExplorer(object sender, RoutedEventArgs e)
    {
        if (_explorer != null)
        {
            _explorer.Close();
            _explorer = null;
            ExplorerBtn.Content = "🕸 Explorar el grafo";
            return;
        }
        if (_surfaceMap == null) { SetStatus("El mapa del terreno no está cargado."); return; }
        // Lectura inmediata también para el MAPEO: el recorrido confirma cada transición esperando
        // dos lecturas estables de la superficie, y contra un valor que se refresca cada 800 ms eso
        // son ~1,6 s de reloj por arista, más que el clic y la carga de la pantalla juntos.
        _explorer = new GraphExplorerWindow(_surfaceMap, () => _locator?.DondeEstoy());
        // LA VOZ SE PRESTA, NO SE DUPLICA. El explorador narra el mapeo con la MISMA conversación
        // en vivo que atiende al micrófono: darle una suya sería una segunda conexión a Gemini
        // hablando por la misma boca, y las dos se pisarían.
        if (_vivo != null) _explorer.Narrador = new Voice.NarradorDelArquitecto(_vivo);
        _explorer.MapaVivo = _mapaVivo;   // para poder vaciar el núcleo desde su botón
        _explorer.Closed += (_, __) => { _explorer = null; Dispatcher.Invoke(() => ExplorerBtn.Content = "🕸 Explorar el grafo"); };
        _explorer.Show();
        ExplorerBtn.Content = "🕸 Explorador: visible — clic para cerrar";
    }

    private void OnToggleStepMode(object sender, RoutedEventArgs e)
    {
        _stepMode = !_stepMode;
        SetStepModeUi();
        SetStatus(_stepMode
            ? "Paso a paso ACTIVO: la próxima ejecución se detendrá antes de cada paso."
            : "Paso a paso apagado.");
    }

    private void SetStepModeUi()
    {
        StepModeBtn.Content = _stepMode ? "👣 Paso a paso: ACTIVO" : "👣 Paso a paso: apagado";
        StepModeBtn.Foreground = _stepMode
            ? System.Windows.Media.Brushes.White
            : new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
    }

    /// <summary>
    /// Ensayo en seco del workflow que apunta el slider: dice qué pasaría SIN tocar la pantalla.
    ///
    /// Se construye el MISMO player que ejecutaría de verdad —mismas superficies, mismo plan, mismo
    /// colapso de tecleos— porque un ensayo sobre una lista distinta de la que se ejecuta no vale nada.
    /// El detalle va al registro; en el globo solo el veredicto, que es lo que se mira de un vistazo
    /// cinco minutos antes de un demo.
    /// </summary>
    private async void OnDryRunWorkflow(object sender, RoutedEventArgs e)
    {
        if (_runningDirect) return;
        if (_directIndex < 0 || _directIndex >= _directWorkflows.Count) { SetStatus("Selecciona un workflow primero."); return; }
        var wf = _directWorkflows[_directIndex];

        DryRunBtn.IsEnabled = false;
        SetStatus($"Ensayando «{wf.Title}» en seco…");
        try
        {
            _directGraph ??= new GraphClient(_graphConfig);
            var uia = new UiaSurface { Log = s => LogBus.Log("uia", s) };
            var sap = new SapGuiSurface();
            sap.Diagnostic += (_, msg) => LogBus.Log("sap", msg);
            var player = new WorkflowPlayer(_directGraph, _graphConfig, uia, sap)
            {
                Log = s => LogBus.Log("ensayo", s)
            };

            DryRunReport report = await player.DryRunAsync(wf.Id, null, CancellationToken.None);

            string veredicto = report.Clean
                ? $"✓ Ensayo limpio: {report.Steps} pasos, sin bloqueantes"
                : $"✋ {report.Count(DryRunLevel.Bloqueante)} bloqueante(s) de {report.Steps} pasos";
            int avisos = report.Count(DryRunLevel.Aviso);
            SetStatus($"{veredicto}{(avisos > 0 ? $" · {avisos} aviso(s)" : "")} — detalle en 📜 Logs");
            Narrate(veredicto);
        }
        catch (Exception ex)
        {
            LogBus.Log("ensayo", $"el ensayo falló: {ex}");
            SetStatus($"El ensayo no pudo completarse: {ex.Message}");
        }
        finally { DryRunBtn.IsEnabled = true; }
    }

    /// <summary>
    /// Ejecuta el workflow que apunta el slider, igual que "Ejecutar ahora" de la biblioteca: pide el
    /// plan a Graph y lo corre con el <see cref="WorkflowPlayer"/> (Graph decide QUÉ, esta máquina CÓMO).
    /// Se alinea conscientemente si la pantalla no coincide y aprende esa alineación para la próxima.
    /// El botón ⏹ cancela por el mismo <c>_cts</c> que el resto del panel.
    /// </summary>
    private async void OnRunWorkflowDirect(object sender, RoutedEventArgs e)
    {
        if (_runningDirect) return;
        if (_directIndex < 0 || _directIndex >= _directWorkflows.Count) { SetStatus("Selecciona un workflow primero."); return; }
        var wf = _directWorkflows[_directIndex];

        _runningDirect = true;
        RunWorkflowBtn.IsEnabled = false;
        _cts = new CancellationTokenSource();
        ShowStop(true);
        SetWorking(true);
        SetStatus($"Ejecutando «{wf.Title}»…");
        ShowTalk(); // el progreso se narra ahí, y el ⏹ de la barra ya quedó visible
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

            if (_stepMode)
            {
                _debugger ??= new StepDebuggerWindow();
                player.OnStepPause = pause => _debugger.AskAsync(pause);
            }
            player.StepDone += (_, o) => Narrate(o.Ok ? $"✓ {o.Label}" : $"✗ {o.Label}: {o.Error}");

            RunResult result = await player.RunAsync(wf.Id, null, strictSurface: true, _cts.Token);
            SetStatus(result.Ok
                ? $"«{wf.Title}» terminó: {result.Tally}."
                : $"«{wf.Title}» se detuvo: {result.Error}");
            // Un workflow que se detiene NO lanza excepción: devuelve un resultado que dice que no
            // llegó. Sin esta línea la cara se quedaría tan contenta tras una corrida fallida.
            falló = !result.Ok && !_cts.IsCancellationRequested;

            // Una ejecución nueva es una oportunidad nueva. El puente clínico se calla cuando ya
            // ofreció ESTA versión de la nota, y está bien para no repetir el ofrecimiento en la
            // misma pantalla — pero un workflow que acaba de navegar deja delante una pantalla
            // NUEVA y vacía, donde esos mismos datos sí hacen falta. Sin esto, correr el workflow
            // por segunda vez con la misma nota no ofrece nada y parece que el puente se rompió
            // (visto el 2026-07-28: 7/7 ejecutado y ni un ofrecimiento después).
            if (result.Ok && _clinical.Active)
            {
                _offeredRev = "";
                _clinicalStep = -1;
            }

            if (result.Ok && result.AlignedConsciously)
                _ = _directGraph.PrependAlignmentStepAsync(wf.Id, CancellationToken.None); // aprende a alcanzar su superficie

            // PUENTE subconsciente→consciente: si el workflow se detuvo (y no fue cancelado por el
            // usuario), el cerebro consciente (computer-use) retoma desde la pantalla actual con el
            // contexto del fallo. Al despejar el obstáculo puede re-invocar el workflow: la reanudación
            // por ubicación hace el resto (se salta lo ya hecho).
            if (!result.Ok && !_cts.IsCancellationRequested)
                bridgeGoal =
                    $"Estaba ejecutando el workflow «{wf.Title}» y se detuvo en: {result.Error}. " +
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
            // Se oculta, no se cierra: cerrar dispara el Closing, que significa «el operador paró».
            _debugger?.Finish();
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
        BackendHeader.ToolTip = text;
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
    /// Enciende/apaga el inspector visual de elementos (overlay click-through con recuadros +
    /// diagnóstico de clic amarillo/rojo). Ver <see cref="UiInspector"/>.
    /// </summary>
    /// <summary>
    /// SOLO GRAFO: le quita al asistente las acciones a coordenadas, para medir hasta dónde llega
    /// el grafo por sí solo. Ver <see cref="Agent.AgentLoop.SoloGrafo"/>.
    ///
    /// El texto del botón dice el ESTADO, no la acción — «GRAFO + coordenadas» / «SOLO GRAFO» — y no
    /// «activar solo grafo». Un botón que nombra lo que hará obliga a deducir dónde estás, y este se
    /// mira justo cuando se está midiendo, que es cuando peor se deduce.
    /// </summary>
    private void OnToggleSoloGrafo(object sender, RoutedEventArgs e)
    {
        bool on = !Agent.AgentLoop.SoloGrafo;
        Agent.AgentLoop.SoloGrafo = on;

        SoloGrafoBtn.Content = on ? "🕸 Navegación: SOLO GRAFO" : "🕸 Navegación: GRAFO + coordenadas";
        SoloGrafoBtn.Background = new System.Windows.Media.SolidColorBrush(on
            ? System.Windows.Media.Color.FromArgb(0x66, 0x21, 0x96, 0xF3)   // el mismo azul que el cromo del grafo
            : System.Windows.Media.Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        SetStatus(on
            ? "SOLO GRAFO: sin tap/type/scroll. Si el grafo no sabe llegar, se detiene."
            : "Grafo + coordenadas: si el grafo no sabe llegar, computer-use lo rodea.");
        LogBus.Log("agent", on
            ? "🕸 SOLO GRAFO puesto: las acciones a coordenadas quedan prohibidas (medición)"
            : "🕸 SOLO GRAFO quitado: vuelve el respaldo por coordenadas");
    }

    /// <summary>Si el modo «verlo todo» está puesto. Ver <see cref="OnToggleFullTooltips"/>.</summary>
    private bool _fullTooltips;

    /// <summary>
    /// TODO ENCENDIDO DE UN GOLPE: inspector, ID de superficie, explorador del grafo y mapa.
    ///
    /// Ver la pantalla entera anotada exigía cuatro interruptores, y había que acordarse de los
    /// cuatro. Cuando lo que quieres es mirar, quieres mirarlo todo (2026-08-08, pedido por el
    /// usuario).
    ///
    /// Reutiliza los manejadores de siempre en vez de duplicar lo que hacen, y por eso pregunta
    /// primero por el estado de cada uno: son TOGGLES, así que llamarlos a ciegas apagaría lo que ya
    /// estuviera encendido — pulsar «encender todo» con el inspector puesto lo habría apagado. Cada
    /// uno se toca solo si no está ya como queremos.
    /// </summary>
    private void OnToggleFullTooltips(object sender, RoutedEventArgs e)
    {
        _fullTooltips = !_fullTooltips;

        if ((_inspector?.Active ?? false) != _fullTooltips) OnToggleInspector(sender, e);
        if (_idALaVista != _fullTooltips) OnToggleLocator(sender, e);
        if ((_explorer != null) != _fullTooltips) OnToggleExplorer(sender, e);
        if ((_map != null) != _fullTooltips) OnToggleMap(sender, e);

        FullTooltipsBtn.Content = _fullTooltips ? "👁 Full Tooltips: TODO a la vista" : "👁 Full Tooltips";
        SetStatus(_fullTooltips
            ? "Todo a la vista: inspector, ID, explorador y mapa"
            : "Todo apagado");
    }

    private void OnToggleInspector(object sender, RoutedEventArgs e)
    {
        _inspector ??= new UiInspector();
        bool on = _inspector.Toggle();
        InspectorBtn.Content = on ? "🔍 Inspector activo — clic para apagar" : "🔍 Inspector de elementos";
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

    private void IrJuntoA(Rect fisico)
    {
        if (JuntoA(fisico) is not { } sitio) return;

        // SEÑALAR VARIAS EMITE LAS DOS SEÑALES. Senalador avisa de «estas seis» y acto seguido de
        // «la principal es esta», y las dos llegan a la carita: el recorrido arrancaba y el aviso
        // siguiente lo sustituía por un viaje corriente a la primera. Desde fuera parecía que el
        // recorrido no se había implementado (2026-08-07). Los ojos sí miran; lo que se ignora es
        // el movimiento, que ya lo lleva la ruta.
        if (_recorridoReciénLanzado) _recorridoReciénLanzado = false;
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
            LocatorBtn.Content = "📍 ID visible — clic para ocultar";
            SetStatus("ID de superficie a la vista");
        }
        else
        {
            _badge.Hide();
            LocatorBtn.Content = "📍 ID oculto — clic para mostrar";
            SetStatus("ID oculto (se sigue midiendo)");
        }
    }

    /// <summary>Si el badge del ID se está mostrando. El localizador corre igual, se vea o no.</summary>
    private bool _idALaVista = true;

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
        ShowTalk(); // que se vea el estado (y quede a mano el ⏹) desde el primer segundo
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
        if (!string.IsNullOrWhiteSpace(text)) ShowTalk();
    });
    public void Speak(string text)
    {
        Dispatcher.Invoke(() => { Bubble.Text = text; SetStatus(text); ShowTalk(); });
        // Durante una conversación en vivo la voz de Ü la pone Gemini. Añadir encima el sintetizador
        // de Windows serían dos Ü hablando a la vez, cada una su frase: el texto se sigue viendo,
        // que es lo que hace falta, pero se oye una sola.
        if (_vivo?.Viva == true) return;
        _voice.Speak(text);
    }

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
        var lineas = (Bubble.Text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();

        // Una frase que se está diciendo REEMPLAZA a su versión anterior en vez de añadirse: llega a
        // trozos y añadirlos dejaba una columna de palabras sueltas. Se compara por quién habla, y
        // solo mientras el turno sigue abierto: al cerrarse, `_turnoAbierto` cae y la siguiente
        // frase del mismo interlocutor empieza línea nueva, que es lo que hace legible el historial.
        string quien = linea.Length > 3 ? linea[..3] : "";
        if (_turnoAbierto && (quien == "Ü: " || quien == "Tú:") && lineas.Count > 0
            && lineas[^1].StartsWith(quien, StringComparison.Ordinal))
            lineas[^1] = linea;
        else
            lineas.Add(linea);
        _turnoAbierto = true;
        if (lineas.Count > 40) lineas.RemoveRange(0, lineas.Count - 40);
        Bubble.Text = string.Join("\n", lineas);
        ShowTalk();
    }

    // --- IUserChannel ---
    public Task<string> AskAsync(string question, CancellationToken ct)
    {
        Dispatcher.Invoke(() =>
        {
            SetStatus(question);
            ShowTalk(focusInput: true); // la pregunta necesita la caja de texto delante
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
            if (_vivo?.Viva == true) { RefreshMood(); PintarBotonVoz(); }
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
}

