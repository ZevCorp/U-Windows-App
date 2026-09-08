using System.IO;
using U.Graph;
using U.Graph.Surfaces;
using U.WindowsClient.Backend;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Teach;

/// <summary>
/// Orquesta "Enseñar workflow": graba pasos estructurados (UIA o SAP GUI, detectado por la ventana en
/// primer plano) Y video de contexto (<see cref="TeachSession"/>) EN PARALELO, sobre la misma acción
/// del operador. Son dos grabadoras completamente independientes que solo esta clase sincroniza.
///
/// ORDEN CRÍTICO al detener: <see cref="WorkflowRecorder.StopAsync"/> ya cierra la sesión en Graph
/// (post-procesa y persiste el workflow) como parte de sí misma. Por eso el resumen del video debe
/// llegar a Graph vía <see cref="WorkflowRecorder.AddContextAsync"/> ANTES de llamar a
/// <c>StopAsync</c> — después ya no hay sesión abierta a la que adjuntarlo.
/// </summary>
public sealed class WorkflowTeachSession : IAsyncDisposable
{
    private readonly GraphClient _graph;
    private readonly GraphConfig _graphConfig;
    private readonly IUiSurface _uia;
    private readonly IUiSurface _sap;
    private readonly BackendClient _backend;
    private readonly VideoLibrary _videoLibrary;
    private readonly string _userId;

    private WorkflowRecorder? _recorder;
    private TeachSession? _teach;

    // ── LA TERCERA SALIDA DE LA DEMO: la skill local que map_batch sí sabe leer ──────────
    //
    // La spec 005 dejó juzgadas SkillEnsenada, AncladorDeVoz y SesionDeDemo (promesas 102, 104,
    // 105 y 106) y NADIE las llamaba: la demo mandaba sus pasos a Graph y ahí se quedaban, en un
    // formato que el batch no lee. Esto es el cableado que faltaba.
    private Teach.SesionDeDemo? _demo;
    private readonly List<(ObservedStep Paso, long HoraMs)> _observados = new();
    private readonly List<Navigation.FraseDicha> _frases = new();
    private System.Diagnostics.Stopwatch? _reloj;
    private string _dondeEmpezo = "";

    // ── LA LECCIÓN (spec 013): la cámara que mira al pasado, los clics del vigía y su carpeta ──
    private CamaraDeCuadros? _camara;
    private readonly List<ClicVisto> _clics = new();
    private Action<int, int, long>? _oyenteDeClics;
    private Action<int, int, string, string, string, string>? _oyenteDeIdentidad;

    private long _tickAlArrancar, _relojAlArrancar;
    private string _carpetaLeccion = "", _idLeccion = "";

    /// <summary>La carpeta de la lección que dejó la última demo cerrada, o null si no se entregó.</summary>
    public string? UltimaLeccion { get; private set; }

    /// <summary>
    /// A dónde lleva una puerta desde una pantalla, según el TERRENO: (pantalla, selector, etiqueta) → destino.
    /// Lo pone la ventana, que es quien tiene el grafo. Es la única fuente de las llegadas (promesa 178).
    /// </summary>
    public Func<string, string, string, string>? LlegadaSegunElTerreno { get; set; }

    /// <summary>
    /// Lo que el humano va diciendo mientras enseña, con su hora. Lo alimenta quien oye (la voz);
    /// esta clase solo lo guarda para que <see cref="Navigation.AncladorDeVoz"/> lo reparta.
    /// </summary>
    /// <remarks>
    /// UN SOLO RELOJ para las frases y para los pasos, y por eso la hora se toma AQUÍ y no la trae
    /// quien habla: dos relojes distintos harían que el anclaje por cercanía comparara peras con
    /// manzanas, y el síntoma sería una frase colgada del paso de al lado — imposible de distinguir
    /// de un error del modelo.
    /// </remarks>
    public void Oyo(string frase)
    {
        if (_reloj == null || string.IsNullOrWhiteSpace(frase)) return;
        lock (_frases) _frases.Add(new Navigation.FraseDicha(frase.Trim(), _reloj.ElapsedMilliseconds));
    }

    /// <summary>La skill que salió de la última demo cerrada, o null. La lee la interfaz.</summary>
    public Navigation.SkillEnsenada? UltimaSkill { get; private set; }

    // Pantallazo por paso (meta visual para el computer-use). Ver StepShotCamera.
    private IUiSurface? _shotSurface;
    private EventHandler<ObservedStep>? _shotHandler;
    private int _shotCount;

    /// <summary>Progreso legible para la UI: countdown, superficie detectada, pasos enviados, errores.</summary>
    public event EventHandler<string>? StatusChanged;

    /// <summary>Cuántos pasos lleva enviados la demo, como NÚMERO. El aura de aprendizaje lo pinta
    /// en su píldora; sacarlo del texto de <see cref="StatusChanged"/> sería parsear prosa.</summary>
    public event EventHandler<int>? PasosEnviados;


    public bool IsRecording => _recorder != null;

    public WorkflowTeachSession(
        GraphClient graph, GraphConfig graphConfig, IUiSurface uia, IUiSurface sap,
        BackendClient backend, VideoLibrary videoLibrary, string userId)
    {
        _graph = graph;
        _graphConfig = graphConfig;
        _uia = uia;
        _sap = sap;
        _backend = backend;
        _videoLibrary = videoLibrary;
        _userId = userId;
    }

    /// <summary>
    /// Inicia la enseñanza. <paramref name="description"/> es lo que Graph guarda como descripción del
    /// workflow — obligatoria porque WorkflowRecorder.StartAsync la necesita para abrir la sesión.
    /// </summary>
    public async Task StartAsync(string description, CancellationToken ct)
    {
        if (IsRecording) throw new InvalidOperationException("Ya hay una enseñanza de workflow en curso.");
        if (!_graphConfig.IsConfigured)
            throw new InvalidOperationException(
                "Graph no está configurado: falta la URL o la API key (panel Workflows → Conexión).");

        // SE ESPERA A QUE PONGAS DELANTE LA APP; no se cuentan segundos (promesa 137). Aquí hubo
        // hasta el 2026-09-03 una cuenta atrás de 3 s más 4 s de gracia, y ese plazo se comió dos
        // demos seguidas el mismo día que el micrófono pasó a abrirse solo: Ü saludaba, el humano
        // contestaba «¿y tú me escuchas?», y a los 7 s esto abortaba. El plazo no era corto — es que
        // había dejado de ser suficiente cuando arriba apareció una conversación.
        //
        // Lo que NO cambia es por qué existe la compuerta: nadie puede enseñar la ventana de Ü. Con
        // Ü delante el detector elige UIA, y una enseñanza de SAP por UIA sale inservible — un paso
        // por PULSACIÓN («n», «nw», «nwp», «nwp1») y clics sobre el Pane opaco (wf_1785096110817 y
        // wf_1785110820731, del 2026-07-26). Antes eso se abortaba; ahora se espera.
        if (!await EsperarLaAppDelanteAsync(ct))
            throw new InvalidOperationException(
                "No se puede enseñar la ventana de Ü. Pon delante la aplicación que vas a enseñar "
                + "(SAP, el navegador…) y vuelve a pulsar Enseñar.");

        // La decisión de superficie SIEMPRE queda en el registro: una enseñanza sobre SAP grabada por
        // UIA produce pasos "clic en el panel" inservibles, y sin esta línea es indistinguible de un
        // bug del grabador (pasó: wf_1785096110817).
        IUiSurface surface = SurfaceDetector.Detect(_uia, _sap, out string why);
        LogBus.Log("workflow-teach", $"superficie elegida: «{surface.Name}» — {why}");

        var availability = surface.Check();
        if (!availability.Available)
        {
            // LA APP DE DELANTE NO DECIDE SI SE PUEDE ENSEÑAR. Medido el 2026-09-02, tres veces
            // seguidas: con SAP Logon delante —el lanzador, sin sesión abierta— el detector elegía
            // «sap», Check() decía «no hay ninguna conexión activa» y Enseñar se negaba en redondo.
            // El Logon es una ventana normal de Windows y UIA la ve entera; la única razón para no
            // grabarla era que nadie lo intentaba. Se cae al otro mundo y se DICE; si tampoco está
            // disponible, ahí sí no se puede y se dice por qué.
            var otra = ReferenceEquals(surface, _sap) ? _uia : _sap;
            var deLaOtra = otra.Check();
            if (!deLaOtra.Available)
                throw new InvalidOperationException($"[{surface.Name}] {availability.Reason}");
            LogBus.Log("workflow-teach",
                $"«{surface.Name}» no está disponible ({availability.Reason}): se graba por «{otra.Name}»");
            surface = otra;
        }

        StatusChanged?.Invoke(this, $"Grabando pasos sobre «{surface.Name}»…");

        var recorder = new WorkflowRecorder(_graph, _graphConfig, surface);
        // AVISO DE GRABACIÓN LARGA. El post-procesado de Graph escribe título, resumen y guía con un
        // LLM sobre TODOS los pasos, y en un flujo largo se pasa del tiempo máximo de la función
        // serverless: 504 al cerrar. Ya nos pasó con una grabación de seis pantallas y tres minutos.
        // El cliente no puede subir ese techo, pero sí avisar a tiempo de que conviene partir el flujo
        // en dos workflows encadenados — que además se reproducen y se depuran mucho mejor.
        const int AvisoPasos = 30;
        bool avisado = false;
        recorder.Progress += (_, status) =>
        {
            if (status.StepsSent >= AvisoPasos && !avisado)
            {
                avisado = true;
                LogBus.Log("workflow-teach",
                    $"grabación larga: {status.StepsSent} pasos. Riesgo de 504 al cerrar (el post-procesado "
                    + "de Graph se pasa del límite de Vercel). Considera partirla en dos workflows.");
            }
            StatusChanged?.Invoke(this,
                $"Grabando pasos… {status.StepsSent} enviados"
                + (avisado ? " · ⚠ larga: mejor pártela en dos" : "")
                + (status.LastError != null ? $" (último error: {status.LastError})" : ""));
            PasosEnviados?.Invoke(this, status.StepsSent);
        };

        string workflowId = await recorder.StartAsync(description, ct);
        _recorder = recorder;

        // Pantallazo por paso: cada step queda con su "meta visual" en disco, consumible por el
        // computer-use cuando tenga que concatenar/retomar un workflow fallido. La numeración sigue el
        // orden de llegada, el mismo con el que Graph numera los steps.
        _shotCount = 0;
        _shotSurface = surface;

        // DE DÓNDE PARTE LA SKILL: la pantalla que había al empezar a grabar. Es el objetivo del
        // primer paso al reproducirla, y sin él la skill no sabría dónde se puede correr.
        _demo = new Teach.SesionDeDemo();
        _observados.Clear();
        lock (_frases) _frases.Clear();
        _reloj = System.Diagnostics.Stopwatch.StartNew();
        _dondeEmpezo = "";
        try { _dondeEmpezo = surface.Identity().Url ?? ""; } catch { }

        // LA CÁMARA ARRANCA CON EL RELOJ y guarda TODOS los cuadros (spec 013): el piloto podrá
        // pedir la pantalla de cualquier segundo, aunque ahí no hubiera clic. Y el vigía, que ya ve
        // cada pulsación humana, le presta la hora del gancho: es lo que convierte 26 clics en 26
        // eventos en vez de en 1 paso.
        _idLeccion = "leccion_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _carpetaLeccion = LeccionEnDisco.NuevaCarpeta(_idLeccion);
        lock (_clics) _clics.Clear();
        _tickAlArrancar = Environment.TickCount64;
        _relojAlArrancar = _reloj.ElapsedMilliseconds;
        try
        {
            _camara = new CamaraDeCuadros(LeccionEnDisco.CarpetaDeCuadros(_carpetaLeccion), _reloj);
            _camara.Arrancar();
        }
        catch (Exception ex) { LogBus.Log("camara", $"la cámara no arrancó: {ex.Message}. La lección saldrá sin cuadros y no se entregará."); }
        var superficieDeLaDemo = surface;
        _oyenteDeClics = (x, y, tick) =>
        {
            long hora = _relojAlArrancar + (tick - _tickAlArrancar);
            AnotarClic(hora, x, y, superficieDeLaDemo);
        };
        Navigation.ClickWatcher.AlPulsar += _oyenteDeClics;
        // LA IDENTIDAD LLEGA UN POCO DESPUÉS DEL GOLPE (el vigía resuelve fuera del gancho): se casa
        // con el último clic anotado en ese mismo punto. Es la etiqueta con la que el terreno conoce
        // la puerta, y lo único que map_take entiende (primera prueba real, 2026-09-07).
        _oyenteDeIdentidad = (x, y, selector, etiqueta, tipo, proceso) =>
        {
            lock (_clics)
            {
                int i = _clics.FindLastIndex(c => c.X == x && c.Y == y);
                if (i < 0) return;
                _clics[i] = _clics[i] with { Selector = selector ?? "", Etiqueta = etiqueta ?? "", Tipo = tipo ?? "", DeU = proceso == "propio" };
            }
        };
        Navigation.ClickWatcher.AlResolver += _oyenteDeIdentidad;

        _shotHandler = (_, paso) =>
        {
            int n = Interlocked.Increment(ref _shotCount);
            Task.Run(() => StepShotCamera.Capture(workflowId, n));
            // EL PASO SE GUARDA CON SU HORA, del mismo reloj que las frases: es lo que permite
            // saber qué estaba diciendo el humano mientras tocaba esto (promesa 105).
            lock (_observados) _observados.Add((paso, _reloj?.ElapsedMilliseconds ?? 0));
        };
        surface.StepObserved += _shotHandler;

        var teach = new TeachSession(_backend, _videoLibrary, _userId);
        teach.StatusChanged += (_, msg) => StatusChanged?.Invoke(this, msg);
        try
        {
            await teach.StartAsync(ct);
            _teach = teach;
        }
        catch (Exception ex)
        {
            // El video es parte del requisito ("UIA + video en paralelo"): si no arranca, no dejamos una
            // grabación de pasos a medias sin su contexto — se aborta todo y se reporta el motivo real.
            LogBus.Log("workflow-teach", $"el video no arrancó, se cancela la grabación de pasos: {ex.Message}");
            DetachShots();
            await recorder.StopAsync(CancellationToken.None);
            _recorder = null;
            throw;
        }
    }

    /// <summary>Detiene ambas grabaciones y devuelve el workflow ya post-procesado y persistido por Graph.</summary>
    public async Task<FinishResponse> StopAsync(CancellationToken ct)
    {
        if (_recorder == null) throw new InvalidOperationException("No hay ninguna enseñanza de workflow en curso.");
        WorkflowRecorder recorder = _recorder;
        _recorder = null;

        // DÓNDE ACABÓ LA DEMO, leído AHORA y no estimado (promesa 140): es la llegada del último
        // paso, y la única forma de saberla es mirar la pantalla en el instante de parar. Se lee
        // ANTES de soltar la superficie, y se insiste un poco: SAP contesta con retraso justo
        // después de un round-trip.
        string dondeTermino = "";
        for (int intento = 0; intento < 4 && dondeTermino.Length == 0; intento++)
        {
            try { dondeTermino = _shotSurface?.Identity().Url ?? ""; } catch { }
            if (dondeTermino.Length == 0) await Task.Delay(200, ct);
        }
        LogBus.Log("workflow-teach", dondeTermino.Length > 0
            ? $"la demo acabó en «{dondeTermino}»"
            : "✋ no pude leer dónde acabó la demo: la skill no tendrá destino y no se empaquetará");

        // EL CIERRE SIGUE EL ORDEN DE «ElCierreDeLaDemo.Orden» (promesa 187), y su primer paso es
        // este: descargar lo que la superficie tenga observado y sin emitir. Tiene que ser AQUÍ, con
        // el oyente todavía enganchado (DetachShots viene justo debajo) y mucho antes de
        // GuardarLaLeccion, que escribe la lección en disco. Los demás pasos del orden —soltar,
        // parar el video, armar la lección, parar al grabador— siguen más abajo, cada uno en su sitio.
        //
        // POR QUÉ (2026-09-07): con eventos COM, SAP publica lo tecleado solo cuando la pantalla
        // VIAJA. Una demo que acaba dentro del formulario del triage no viaja, y todo lo escrito se
        // quedaba sin publicar: 24 eventos y ni un texto. La primera versión de este arreglo puso la
        // descarga en StopObserving, que corre treinta líneas DESPUÉS de escribir la lección —
        // funcionaba y la lección salía vacía igual, que es la peor forma de fallar.
        if (ElCierreDeLaDemo.Orden(_teach != null, _shotSurface != null).Contains(Cierre.DescargarLoPendiente))
        {
            try { _shotSurface!.DescargarLoPendiente(); }
            catch (Exception e) { LogBus.Log("workflow-teach", $"no pude descargar lo pendiente: {e.Message}"); }
        }

        DetachShots();
        SoltarLaCamaraYElVigia();

        // LOS PASOS SE ARMAN AQUÍ, ANTES DEL VIDEO, y ese orden es el cambio del 2026-09-03: al
        // video hay que preguntarle POR ESTOS PASOS (promesa 135), así que tienen que existir antes
        // de llamarlo. Lo único que todavía no se sabe es el nombre, que sale de lo que Graph
        // devuelve al cerrar — y un nombre no hace falta para interpretar lo que se hizo.
        var pasosDeLaDemo = PasosDeLaDemo();


        string? summary = null;
        string interpretacion = "";
        string mp4 = _teach?.RutaDelVideo ?? "";
        if (_teach != null)
        {
            try
            {
                await _teach.StopAsync(); // finaliza el mp4 en disco → visible en 🎞 Videos, se procese o no

                // LA LECCIÓN SE ESCRIBE AQUÍ, con el mp4 ya cerrado y ANTES de hablar con Gemini o
                // con Graph: cualquiera de los dos puede fallar (429, 504) y la lección no depende
                // de ninguno. Es lo que el piloto va a leer (spec 013).
                GuardarLaLeccion(dondeTermino, mp4);

                // EL VIDEO SIEMPRE SE PROCESA (decisión del dueño, 2026-09-03). Tenía un
                // interruptor en el panel para esquivar los 504 de flujos largos, y un interruptor
                // que apaga la mitad de lo que la demo aprende es una trampa: quien lo deje apagado
                // se lleva demos mudas sin enterarse. Si el proceso falla, se dice y la enseñanza
                // sigue con los pasos y lo narrado — que es lo que la promesa 128 protege.
                StatusChanged?.Invoke(this, "Procesando video de contexto…");
                var leido = await _teach.ProcessAsync(
                    Teach.LoQueSePregunta.De(new Navigation.SkillEnsenada(
                        "demo", "", _dondeEmpezo, pasosDeLaDemo)), ct);
                summary = leido.Resumen;
                interpretacion = leido.Interpretacion;
            }
            catch (Exception ex)
            {
                LogBus.Log("workflow-teach", $"el video falló, el workflow se guarda solo con los pasos: {ex.Message}");
                StatusChanged?.Invoke(this, $"El video falló ({ex.Message}); el workflow se guarda solo con los pasos.");
            }
            finally
            {
                await _teach.DisposeAsync();
                _teach = null;
            }
        }

        if (!string.IsNullOrWhiteSpace(summary))
        {
            try { await recorder.AddContextAsync(summary!, ct); }
            catch (Exception ex)
            {
                LogBus.Log("workflow-teach", $"no se pudo adjuntar el contexto de video: {ex.Message}");
            }
        }

        // EL SEGUNDO PELDAÑO (promesa 136): si el video no llegó a interpretar —sin saldo, 504, sin
        // red—, el mismo juicio se pide con los pasos y lo narrado, que no necesitan ver la
        // pantalla. Debajo sigue estando la regla del narrado, que es determinista.
        var comoQuedo = new Navigation.SkillEnsenada("demo", "", _dondeEmpezo, pasosDeLaDemo);
        if (Teach.LoQueSePregunta.SinPantalla(comoQuedo, interpretacion))
        {
            StatusChanged?.Invoke(this, "El video no pudo; interpreto lo que hiciste y dijiste…");
            interpretacion = await TeachSession.InterpretarPasosAsync(
                _backend, Teach.LoQueSePregunta.De(comoQuedo), _dondeEmpezo, ct);
        }

        StatusChanged?.Invoke(this, "Cerrando la grabación y pidiéndole a Graph que la estructure…");
        var terminado = await recorder.StopAsync(ct);

        // ── LA SKILL, aquí y no antes: hace falta el resumen de Graph para poder nombrarla ──
        EmpaquetarLaSkill(terminado, summary, pasosDeLaDemo, interpretacion, dondeTermino);
        return terminado;
    }

    /// <summary>
    /// De lo observado a una skill en disco. Es la tercera salida de la demo, y la única que
    /// <c>map_batch</c> sabe leer.
    /// </summary>
    /// <remarks>
    /// LA LLEGADA DE UN PASO ES DÓNDE ESTABA EL SIGUIENTE. Cada <c>ObservedStep</c> trae la
    /// superficie en la que OCURRIÓ, no a la que llevó — para los clics se lee antes de actuar, a
    /// propósito, porque un clic que navega tarda lo bastante como para leer ya la pantalla nueva.
    /// Así que la llegada del paso N es la superficie del paso N+1, y la del último es donde
    /// acabamos. No se estima nada: es la cadena que la propia demo observó.
    ///
    /// EL NOMBRE NO SE PIDE EN VOZ ALTA (decisión del dueño, 2026-09-02: «no me interesa que Ü
    /// hable el título»). Sale de lo que ya se sabe —app, ventana, cuándo, cuántos pasos— con
    /// <see cref="Workflows.NombreDeWorkflow"/>, que es la pieza que la promesa 108 juzga; y la
    /// descripción, del resumen de Graph solo si NO es relleno.
    ///
    /// UN FALLO AQUÍ NO TUMBA LA ENSEÑANZA: el workflow ya está en Graph y el video en disco. Lo
    /// que se pierde es la skill local, y se DICE — no se traga.
    /// </remarks>
    private void EmpaquetarLaSkill(FinishResponse terminado, string? resumenDelVideo,
        IReadOnlyList<Navigation.PasoEnsenado> pasos, string interpretacion, string dondeTermino)
    {
        var demo = _demo;
        _demo = null;
        if (demo == null) return;

        try
        {
            if (pasos.Count == 0)
            {
                LogBus.Log("workflow-teach", "la demo no dejó ningún paso observado: no hay skill que empaquetar");
                return;
            }
            var voz = Navigation.AncladorDeVoz.Ancla(
                LasFrases(), LasHoras());

            string nombre = NombreDeLaSkill(terminado);
            string descripcion = Workflows.NombreDeWorkflow.EsRelleno(terminado?.Summary)
                ? "" : (terminado?.Summary ?? "").Trim();
            if (descripcion.Length == 0 && !string.IsNullOrWhiteSpace(resumenDelVideo))
                descripcion = resumenDelVideo!.Trim();
            if (descripcion.Length == 0 && voz.Contexto.Length > 0) descripcion = voz.Contexto;

            demo.Pasos.AddRange(pasos);
            var recienHecha = Navigation.SkillEnsenada.Empaquetar(
                nombre, descripcion, _dondeEmpezo, pasos, dondeTermino);

            // EL CRITERIO DEL MODELO, APLICADO (promesa 134). La regla del narrado ya dejó sus
            // huecos dentro de `recienHecha`; esto los corrige donde el modelo opinó y los deja
            // intactos donde no. Si el video no llegó —429, 504, sin red— `Leer` devuelve «no
            // opinó» y la skill sale exactamente igual que antes de existir esta línea.
            if (recienHecha != null)
            {
                var lo = Navigation.LoQueElModeloInterpreta.Leer(interpretacion, recienHecha);
                if (Navigation.LoQueElModeloInterpreta.HuboInterpretacion(lo))
                {
                    int antes = recienHecha.Huecos.Count;
                    recienHecha = recienHecha.ConLoInterpretado(lo);
                    LogBus.Log("workflow-teach",
                        $"el modelo interpretó la demo: {antes} hueco(s) por la regla → "
                        + $"{recienHecha.Huecos.Count} tras su criterio, y {lo.Recuerdos.Count} "
                        + "sugerencia(s) de significado para el repaso");
                }
                else LogBus.Log("workflow-teach",
                    "el modelo no llegó a interpretar la demo: se queda lo que dedujo la regla del "
                    + "narrado, que ya está en disco");
            }

            demo.Skill = recienHecha;
            demo.Cerrar();

            var entrega = demo.Entrega();
            if (entrega?.Skill == null)
            {
                LogBus.Log("workflow-teach", $"no se pudo empaquetar la skill (pasos={pasos.Count}, "
                    + $"empieza en «{_dondeEmpezo}», acaba en «{dondeTermino}»): sin nombre, sin punto "
                    + "de partida o sin destino no es reproducible");
                return;
            }

            string archivo = entrega.Skill.Guardar(Navigation.SkillEnsenada.CarpetaPorDefecto);
            UltimaSkill = entrega.Skill;
            LogBus.Log("workflow-teach",
                $"skill «{entrega.Skill.Nombre}» guardada: {entrega.Skill.Pasos.Count} paso(s) · "
                + $"{entrega.Skill.Huecos.Count} hueco(s) · empieza en «{entrega.Skill.DondeEmpieza}» · "
                + $"acaba en «{entrega.Skill.DondeTermina}» · "
                + $"SIN comprobar · {archivo}");
            StatusChanged?.Invoke(this,
                $"Aprendí «{entrega.Skill.Nombre}»: {entrega.Skill.Pasos.Count} pasos y "
                + $"{entrega.Skill.Huecos.Count} datos. Pulsa «Comprobar aprendizaje» para poder usarla.");
        }
        catch (Exception ex)
        {
            // La cadena entera: un catch mudo aquí convertiría «no supe empaquetar» en «no había nada».
            var porque = new System.Text.StringBuilder();
            for (var x = ex; x != null; x = x.InnerException)
                porque.Append(porque.Length > 0 ? " ← " : "").Append($"{x.GetType().Name}: {x.Message}");
            LogBus.Log("workflow-teach", $"la skill no se pudo empaquetar: {porque}");
        }
    }

    /// <summary>
    /// Lo observado, vuelto pasos con su llegada y lo que se dijo en cada uno.
    /// </summary>
    /// <remarks>
    /// LA LLEGADA DE UN PASO ES DÓNDE ESTABA EL SIGUIENTE. Cada <c>ObservedStep</c> trae la
    /// superficie en la que OCURRIÓ, no a la que llevó — para los clics se lee antes de actuar, a
    /// propósito, porque un clic que navega tarda lo bastante como para leer ya la pantalla nueva.
    /// Así que la llegada del paso N es la superficie del paso N+1, y la del último es donde
    /// acabamos. No se estima nada: es la cadena que la propia demo observó.
    /// </remarks>
    private List<Navigation.PasoEnsenado> PasosDeLaDemo()
    {
        List<(ObservedStep Paso, long HoraMs)> observados;
        lock (_observados) observados = _observados.ToList();

        // LO DICHO, REPARTIDO POR CERCANÍA (promesa 105). Lo que no le quede cerca de ningún paso
        // va al contexto de la skill, no a un paso cualquiera.
        var voz = Navigation.AncladorDeVoz.Ancla(LasFrases(), observados.Select(o => o.HoraMs).ToList());

        var pasos = new List<Navigation.PasoEnsenado>();
        for (int i = 0; i < observados.Count; i++)
        {
            var o = observados[i].Paso;
            string llegada = i + 1 < observados.Count ? (observados[i + 1].Paso.Surface ?? "") : "";
            string selector = (o.Selector ?? "").Trim();
            string texto = o.ActionType is "input" or "select"
                ? (o.SelectedValue ?? o.Value ?? "").Trim()
                : "";
            string dicho = i < voz.DichoPorPaso.Count ? voz.DichoPorPaso[i] : "";
            pasos.Add(new Navigation.PasoEnsenado(selector, texto, llegada.Trim(), dicho));
        }
        return pasos;
    }

    private List<Navigation.FraseDicha> LasFrases()
    {
        lock (_frases) return _frases.ToList();
    }

    private List<long> LasHoras()
    {
        lock (_observados) return _observados.Select(o => o.HoraMs).ToList();
    }

    /// <summary>El nombre de la skill, por lo que se SABE de ella y nunca por el relleno del LLM.</summary>
    private string NombreDeLaSkill(FinishResponse? terminado)
    {
        try
        {
            if (terminado?.Workflow is { } wf)
            {
                string derivado = Workflows.NombreDeWorkflow.Derivar(wf);
                if (derivado.Trim().Length > 0) return derivado.Trim();
            }
        }
        catch (Exception e) { LogBus.Log("workflow-teach", $"no pude derivar el nombre: {e.Message}"); }

        string app = Workflows.NombreDeWorkflow.AppDe(_dondeEmpezo);
        return (app.Length > 0 ? app : "tarea") + "-" + DateTime.Now.ToString("MMdd-HHmm");
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder clase, int max);

    /// <summary>
    /// Espera a que el primer plano sea una ventana AJENA a Ü, contándolo mientras espera.
    /// Promesa 137. Devuelve false solo si se llega al techo.
    /// </summary>
    /// <remarks>
    /// QUIÉN DECIDE ES <see cref="ElArranqueDeLaDemo"/>, que es puro y está juzgado por el contrato.
    /// Aquí solo se mira la ventana de delante y se duerme: el criterio —empezar, seguir, rendirse—
    /// no puede vivir enredado con las llamadas a user32, porque entonces la única forma de probarlo
    /// sería con una pantalla y dos manos.
    ///
    /// EL SONDEO ES RÁPIDO Y LO QUE SE DICE ES LENTO: se mira cada 150 ms para empezar en cuanto
    /// aparezca la app, pero el mensaje solo se repite cuando cambia — un estado que parpadea
    /// veinte veces por segundo no informa, molesta.
    /// </remarks>
    private async Task<bool> EsperarLaAppDelanteAsync(CancellationToken ct)
    {
        const int PollMs = 150;
        int ownPid = Environment.ProcessId;
        string ultimoDicho = "";

        for (int esperado = 0; ; esperado += PollMs)
        {
            bool nuestro = true, escritorio = false;
            IntPtr fg = GetForegroundWindow();
            if (fg != IntPtr.Zero)
            {
                uint pid = 0;
                try { GetWindowThreadProcessId(fg, out pid); } catch { }
                if (pid != 0 && pid != ownPid) nuestro = false;
                // EL ESCRITORIO NO ES UNA APP QUE ENSEÑAR. La cuarta prueba real (2026-09-07 16:45)
                // arrancó con el escritorio delante: el detector eligió UIA y la demo de SAP salió
                // con 35 eventos por pulsación y llegadas «uia://» que ningún juez podía casar.
                var clase = new System.Text.StringBuilder(64);
                try { GetClassName(fg, clase, clase.Capacity); } catch { }
                escritorio = ElArranqueDeLaDemo.EsEscritorio(clase.ToString());
            }

            var paso = ElArranqueDeLaDemo.Juzgar(nuestro, escritorio, esperado, ElArranqueDeLaDemo.TechoPorDefectoMs);
            if (paso.Decir != ultimoDicho)
            {
                ultimoDicho = paso.Decir;
                StatusChanged?.Invoke(this, paso.Decir);
            }

            if (paso.Empezar)
            {
                if (esperado > 0)
                    LogBus.Log("workflow-teach", $"el foco salió de Ü tras {esperado} ms de espera");
                return true;
            }
            if (!paso.Seguir)
            {
                LogBus.Log("workflow-teach",
                    $"✋ el primer plano siguió siendo Ü {esperado} ms — NO se graba: la detección "
                    + "habría elegido «uia» y una enseñanza de SAP por UIA sale inservible");
                return false;
            }
            await Task.Delay(PollMs, ct);
        }
    }

    // ── La lección (spec 013) ─────────────────────────────────────────────────────────────

    /// <summary>Un clic humano, en el reloj de la demo. A dónde llevó se lee un poco después.</summary>
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(System.Drawing.Point p);

    private void AnotarClic(long horaMs, int x, int y, IUiSurface superficie)
    {
        // UN CLIC SOBRE Ü NO ES LA TAREA (parar la demo, abrir el panel). El vigía lo descarta ANTES de
        // avisar cuando la ventana de delante cambió, así que aquí se mira directo qué ventana había
        // bajo el punto. Segunda prueba real: el clic de parar entró como evento 5 sin llegada.
        bool deU = false;
        try { GetWindowThreadProcessId(WindowFromPoint(new System.Drawing.Point(x, y)), out uint pid); deU = pid == (uint)Environment.ProcessId; } catch { }
        lock (_clics) _clics.Add(new ClicVisto(horaMs, x, y, "", DeU: deU));
        if (deU) return;
        // LA LLEGADA SE LEE CUANDO LA PANTALLA SE ASIENTA, no a plazo fijo (promesa 178). Con 1,2 s
        // fijos la primera prueba real grabó «…/0100» donde la verdad era «…/0100/ssub…Triage», y al
        // comprobar la app castigó al piloto por llegar a la pantalla correcta. Se lee cada 250 ms
        // hasta que dos lecturas seguidas coinciden —y SAP no esté de viaje—, con techo de 5 s.
        // LA PANTALLA EN EL INSTANTE DE PULSAR, y nada más (promesa 178). Es la llegada del clic
        // ANTERIOR. Se lee en un hilo aparte porque esto corre dentro del gancho del ratón y SAP
        // contesta por COM; pero se lee YA, antes de que el clic haga efecto. Sin bucle ni techo:
        // tres techos distintos fallaron en tres pruebas reales el 2026-09-07.
        _ = Task.Run(() =>
        {
            string pantalla = "";
            try { pantalla = superficie.Identity().Url ?? ""; } catch { }
            lock (_clics)
            {
                int i = _clics.FindIndex(c => c.HoraMs == horaMs && c.X == x && c.Y == y);
                if (i >= 0) _clics[i] = _clics[i] with { PantallaAlPulsar = pantalla };
            }
        });
    }

    private void SoltarLaCamaraYElVigia()
    {
        if (_oyenteDeClics != null) { Navigation.ClickWatcher.AlPulsar -= _oyenteDeClics; _oyenteDeClics = null; }
        if (_oyenteDeIdentidad != null) { Navigation.ClickWatcher.AlResolver -= _oyenteDeIdentidad; _oyenteDeIdentidad = null; }
        try { _camara?.Parar(); } catch { }
    }

    /// <summary>De lo visto, oído y grabado a la lección en disco. Entera o nada (promesa 172).</summary>
    private void GuardarLaLeccion(string dondeTermino, string mp4)
    {
        var camara = _camara;
        _camara = null;
        UltimaLeccion = null;
        if (_reloj == null || _carpetaLeccion.Length == 0) return;
        try
        {
            List<ClicVisto> clics; lock (_clics) clics = _clics.ToList();
            // LA LLEGADA DE CADA CLIC ES LA PANTALLA DEL CLIC SIGUIENTE, y la del último, donde acabó
            // la demo (promesa 178). Sin reloj.
            List<(ObservedStep Paso, long HoraMs)> observados; lock (_observados) observados = _observados.ToList();
            var pasos = observados.Select(o =>
            {
                string sel = (o.Paso.Selector ?? "").Trim();
                string tecla = sel.StartsWith("key:", StringComparison.OrdinalIgnoreCase) ? sel[4..] : "";
                string texto = o.Paso.ActionType is "input" or "select" ? (o.Paso.SelectedValue ?? o.Paso.Value ?? "").Trim() : "";
                return new PasoVisto(o.HoraMs, tecla.Length > 0 ? "" : sel, (o.Paso.Label ?? "").Trim(),
                    (o.Paso.ControlType ?? "").Trim(), texto, tecla, (o.Paso.Surface ?? "").Trim());
            }).ToList();
            // PRIMERO LA IDENTIDAD QUE SAP VIO, DESPUÉS LAS LLEGADAS (promesa 178, 2026-09-08): al
            // terreno se le pregunta con la puerta que SAP publicó, no con la que el vigía adivinó.
            clics = ArmarLaLeccion.ConLaIdentidadDeSap(clics, pasos).ToList();
            clics = ArmarLaLeccion.Llegadas(clics, dondeTermino, LlegadaSegunElTerreno).ToList();
            if (LlegadaSegunElTerreno == null) LogBus.Log("leccion", "sin terreno a mano: las llegadas quedan vacías salvo la del último clic");
            var frases = LasFrases();
            var tomados = camara?.Cuadros ?? Array.Empty<CuadroTomado>();
            var cuadros = tomados.Select(c => c.ComoCuadro()).ToList();

            var eventos = ArmarLaLeccion.Eventos(clics, pasos, frases, cuadros);
            var entrega = LaEntregaDeLaLeccion.Juzgar(mp4.Length > 0 && File.Exists(mp4), cuadros.Count, eventos.Count);
            int sobreU = clics.Count(c => c.DeU), conIdentidad = clics.Count(c => !c.DeU && c.Etiqueta.Length > 0);
            LogBus.Log("leccion", $"{clics.Count} clic(s) del vigía ({sobreU} sobre Ü, fuera; {conIdentidad} con identidad) · {pasos.Count} paso(s) observados · {frases.Count} frase(s) · "
                + $"{cuadros.Count} cuadro(s) → {eventos.Count} evento(s) · {entrega.Motivo}");
            if (!entrega.Entregable) return;

            var leccion = new Leccion(_idLeccion, _dondeEmpezo, dondeTermino, _reloj.ElapsedMilliseconds, mp4,
                eventos, frases,
                tomados.Select(c => new CuadroDeLaLeccion(c.HoraMs, c.Ruta, c.CursorX, c.CursorY, c.Ancho, c.Alto)).ToList(),
                ArmarLaLeccion.Contexto(frases, eventos));
            LeccionEnDisco.Guardar(leccion, _carpetaLeccion);
            UltimaLeccion = _carpetaLeccion;
            StatusChanged?.Invoke(this, $"Lección guardada: {eventos.Count} evento(s) y {cuadros.Count} cuadro(s). Pulsa «Comprobar» para que el piloto la lea.");
        }
        catch (Exception ex)
        {
            var porque = new System.Text.StringBuilder();
            for (var x = ex; x != null; x = x.InnerException)
                porque.Append(porque.Length > 0 ? " ← " : "").Append($"{x.GetType().Name}: {x.Message}");
            LogBus.Log("leccion", $"la lección no se pudo guardar: {porque}");
        }
    }

    /// <summary>Suelta la cámara de pasos (idempotente).</summary>
    private void DetachShots()
    {
        if (_shotSurface != null && _shotHandler != null) _shotSurface.StepObserved -= _shotHandler;
        _shotSurface = null;
        _shotHandler = null;
    }

    public async ValueTask DisposeAsync()
    {
        DetachShots();
        SoltarLaCamaraYElVigia();
        if (_teach != null) { await _teach.DisposeAsync(); _teach = null; }
        if (_recorder != null) { await _recorder.DisposeAsync(); _recorder = null; }
    }
}
