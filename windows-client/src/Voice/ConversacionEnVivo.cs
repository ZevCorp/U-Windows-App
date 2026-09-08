using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using U.WindowsClient.Diagnostics;
using U.WindowsClient.Mcp;
using Voz.Realtime;

namespace U.WindowsClient.Voice;

/// <summary>
/// Conversación en vivo, con las manos puestas en el grafo.
///
/// La diferencia con <see cref="VoiceIO"/> no es la calidad de la voz: es QUIÉN decide el turno. El
/// dictado de Windows abre el micrófono, espera una frase y se cierra; aquí el caño está abierto en
/// los dos sentidos y el modelo puede interrumpirte, callarse cuando hablas, o pedir una acción a
/// mitad de la explicación. Eso es lo que permite decir «ve al explorador y crea una carpeta» y
/// verlo ocurrir mientras se habla, en vez de dictar → esperar → ejecutar.
///
/// REEMPLAZA A <c>GeminiLive</c> (2026-08-24). El proveedor cambió —de Gemini Live a GPT Realtime,
/// por problemas de turno que no se lograban estabilizar— y esta clase no sabe de ninguno de los
/// dos: todo lo que aquí pasa —el micrófono, el collar, el autocontrol, las herramientas, el
/// reintento, el consumo— es NUESTRO y no cambia según quién esté al otro lado del cable. Lo único
/// que sabe hablar el idioma del proveedor es <see cref="IProtocolo"/>, y eso vive aparte
/// (<c>voz/Realtime</c>) precisamente para que la PRÓXIMA vez que haga falta cambiar de proveedor
/// no cueste un archivo de mil cuatrocientas líneas.
///
/// Las manos son las MISMAS de siempre: <see cref="SurfaceMapTools"/>, que ya sabe anclarse a una
/// ubicación, verificar cada llegada y negarse cuando la pantalla no es la que se creía. Aquí no se
/// añade ninguna capacidad de actuar — se le da voz a la que ya había. Todos los vetos siguen
/// puestos, y eso es deliberado: el camino de voz es más rápido de invocar que el de texto y sería
/// justo el peor sitio para relajar las protecciones.
/// </summary>
public sealed class ConversacionEnVivo : IDisposable
{
    private readonly SurfaceMapTools _mapa;
    private readonly IProtocolo _protocolo;
    private readonly LiveAudio _audio;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private readonly SemaphoreSlim _envio = new(1, 1);

    public ConversacionEnVivo(SurfaceMapTools mapa, IProtocolo? protocolo = null)
    {
        _mapa = mapa;
        _protocolo = protocolo ?? new ProtocoloOpenAI();
        _audio = new LiveAudio(_protocolo.RitmoDeEntrada);
        _compuerta = new CompuertaDeEco(GraciaEcoMs, _protocolo.RitmoDeEntrada);
    }

    // ── La compuerta de eco (spec 002, 2026-08-30) ──────────────────────────

    /// <summary>
    /// Cuánto sigue tragando la compuerta tras vaciarse la cola: cubre los 120 ms de latencia
    /// declarada del altavoz (LiveAudio.DesiredLatency) más el resto de sala. Se ajusta con el
    /// log de «tragó N ms», no con teoría.
    /// </summary>
    private const int GraciaEcoMs = 300;

    private readonly CompuertaDeEco _compuerta;

    /// <summary>El barge-in de la compuerta (spec 002, fase 4): voz sostenida por encima del eco
    /// aprendido corta la cola y reabre la compuerta. Sostén 240 ms · 3× la línea base · piso 500 —
    /// medido contra ESTA sala (2026-08-31): el eco real ronda base 60-250 y el piso de 1500
    /// mataba todo disparo; 500 queda por encima del eco y por debajo de la voz.</summary>
    private readonly DetectorDeInterrupcion _interrupcion = new(240, 3.0, 500);
    private long _ultimaMedicionMs;

    /// <summary>El barge-in por energía se enciende por máquina: aquí está muerto (medido).</summary>
    private static bool DetectorPorEnergia =>
        Environment.GetEnvironmentVariable("U_BARGEIN_ENERGIA") == "1";

    /// <summary>Hasta cuándo NO se reproduce lo que llegue: la interrupción manual tiró la cola,
    /// y el resto de la frase que el servidor ya tenía en vuelo no debe resucitarla.</summary>
    private long _silencioHastaMs;

    /// <summary>
    /// LA INTERRUPCIÓN A LA ORDEN (Escape, 2026-08-31). La vía por energía resultó ciega en este
    /// hardware —el AGC de Windows comprime la entrada y la voz del usuario mide lo mismo que el
    /// eco (rms ~220 vs base 88-219, medido)—, así que el gesto determinista es el que manda
    /// mientras la fase 3 (AEC real) no exista: corta la cola YA, suprime el goteo restante y
    /// reabre la compuerta para que el servidor oiga la primera sílaba de quien interrumpió.
    /// </summary>
    public bool Interrumpir()
    {
        if (!Viva || !_audio.Hablando) return false;
        _audio.Callar();
        _compuerta.Abrir();
        _silencioHastaMs = Environment.TickCount64 + 1500;
        LogBus.Log("voz-viva", "interrupción a la orden (Escape): corto mi voz y escucho");
        return true;
    }
    private long _tragadoAnunciado;
    private string _ultimoFalloEnvio = "";

    /// <summary>
    /// La captura de hoy es WaveIn clásico, SIN AEC del sistema: mientras eso sea verdad, la
    /// compuerta es la única defensa y actúa siempre (promesa 14). La fase 3 de la spec 002 —la
    /// captura WASAPI que le pide el AEC a Windows— es quien puede poner esto a verdadero.
    /// </summary>
    /// <summary>Ya no es una constante (fase 3): es la verdad del AEC por software — si el eco
    /// se está restando de verdad, la compuerta se aparta y el barge-in por voz vuelve.</summary>
    private bool AecDelSistema => _audio.AecPorSoftware;

    /// <summary>La perilla de esta máquina: fuerza la compuerta aunque haya AEC. Solo puede
    /// ENCENDER la garantía, jamás apagarla.</summary>
    /// <summary>
    /// LA EXPERIENCIA POR DEFECTO ES LA DE OPENAI TAL CUAL (decidido por el dueño, 2026-08-31):
    /// el micrófono viaja SIEMPRE y el semantic_vad del servidor decide los turnos — interrumpir
    /// con la voz funciona como en la documentación. El precio conocido: con altavoces Ü puede
    /// oírse a sí misma; la respuesta del dueño fue «si me toca usarla con audífonos, la uso así».
    /// U_SIN_ECO=0 devuelve la compuerta para quien quiera parlantes sin auto-interrupciones
    /// (y U_COMPUERTA_ECO=1 la fuerza gane quien gane, promesa 21).
    /// </summary>
    private static bool SinCaminoDeEco =>
        ModoDeCaptura.SinEcoDeclarado(Environment.GetEnvironmentVariable("U_SIN_ECO"));

    private static bool CompuertaForzada =>
        (Environment.GetEnvironmentVariable("U_COMPUERTA_ECO") ?? "").Trim().ToLowerInvariant()
            is "1" or "true" or "si" or "sí";

    /// <summary>Está en curso una sesión de voz viva.</summary>
    public bool Viva { get; private set; }

    /// <summary>
    /// Lo fuerte que está sonando Ü ahora mismo (0–1). La carita mueve la boca con esto.
    ///
    /// (El «detector de voz» que compartía esta medida se enterró el 2026-08-16; desde la spec 002
    /// el eco no se detecta por volumen sino que se corta en la compuerta, que se llavea de
    /// <see cref="LiveAudio.Hablando"/>. Esto queda solo para la boca.)
    /// </summary>
    public double NivelVoz => Viva ? _audio.NivelSalida : 0;

    /// <summary>
    /// Queda voz por OÍRSE. No es lo mismo que estar generando: el audio llega mucho más rápido de
    /// lo que se reproduce, así que esto sigue siendo cierto bastante después de que el servidor
    /// haya terminado. Lo consulta quien no pueda adelantarse a la voz — ver ElTurnoDeContar.
    /// </summary>
    public bool SigueSonando => Viva && _audio.Hablando;

    /// <summary>
    /// Pasa la voz al collar Omi sin cortar la conversación. Lo pide la carita con un gesto.
    ///
    /// No abre ni cierra la sesión: sólo cambia de dónde entra el audio. <see cref="LiveAudio"/> ya
    /// remuestrea el collar al ritmo que este protocolo pida, así que desde aquí arriba no se nota.
    /// </summary>
    /// <summary>Se queda enganchado mientras la voz vive, para soltarlo al cerrarla.</summary>
    private Action? _oyendoElCambioDeMicrofono;

    public void PasarAlCollar() => _audio.PasarAlCollar();

    /// <summary>
    /// Pone el captador de esta conversación en la fuente que eligió la app. Promesa 146.
    /// </summary>
    /// <remarks>
    /// Se llama al abrir la voz y cada vez que alguien cambia la elección. Quien decide QUÉ caño es
    /// <see cref="ElMicrofonoDeLaApp"/>, que es puro; aquí solo se ejecuta — y no se toca nada si
    /// ya está sonando lo que toca, porque reabrir el caño corta la voz en curso.
    /// </remarks>
    public void ObedecerAlMicrofonoDeLaApp()
    {
        var toca = ElMicrofonoDeLaApp.LoQueToca(
            ElMicrofonoDeLaApp.Preferida, _audio.PorElCollar, _audio.PorElTelefono);
        if (toca == ElMicrofonoDeLaApp.QueHacer.Nada) return;

        LogBus.Log("voz-viva", $"el audio de la app entra por {ElMicrofonoDeLaApp.ComoSeLlama(ElMicrofonoDeLaApp.Preferida)}: {toca}");
        switch (toca)
        {
            case ElMicrofonoDeLaApp.QueHacer.AbrirCollar:
                _audio.PasarAlCollar();
                break;
            case ElMicrofonoDeLaApp.QueHacer.AbrirTelefono:
                // Un collar habla con UN aparato: si estaba enlazado por Bluetooth aquí, se suelta.
                _audio.PasarAlLocal("se eligió oír por el teléfono");
                _ = _audio.PasarAlTelefonoAsync(
                    Cuenta.Nube.ProyectoSupabase, Cuenta.Nube.ClavePublicable, ElMicrofonoDeLaApp.Codigo);
                break;
            default:
                _audio.PasarAlLocal("lo eligió el médico: micrófono del computador");
                break;
        }
    }

    /// <summary>Si lo que se está oyendo entra por el collar. Cambia sola si hay relevo a media sesión.</summary>
    public bool PorElCollar => _audio.PorElCollar;

    /// <summary>Cambió de dónde entra la voz. La carita repinta con esto, no esperando a que Ü hable.</summary>
    public event Action? FuenteCambio
    {
        add => _audio.FuenteCambio += value;
        remove => _audio.FuenteCambio -= value;
    }

    /// <summary>Texto para la carita: lo que se oye, lo que responde, y qué está haciendo.</summary>
    public event Action<string>? Dice;

    /// <summary>Arrancó o terminó. La interfaz cambia el icono del micrófono con esto.</summary>
    public event Action<bool>? Cambio;

    /// <summary>El turno se cerró: lo siguiente que se diga empieza en una línea nueva.</summary>
    public event Action? Cerro;

    /// <summary>
    /// Una acción, dicha en castellano, cuando EMPIEZA y cuando TERMINA (<c>listo</c>).
    ///
    /// Va aparte de <see cref="Dice"/> a propósito: <c>Dice</c> es conversación —lo que se oye y lo
    /// que se responde— y esto es maquinaria. Mezclarlos hacía que un «⚙ file_open path=descargas»
    /// pisara la última frase de la conversación en la burbuja. Quien escuche esto puede pintarlo
    /// donde quiera y con su propio ritmo.
    /// </summary>
    public event Action<string, bool>? Accion;

    /// <summary>
    /// LO QUE SE OYE Y LO QUE SE CONTESTA, con quién lo dijo. <c>esDeU</c> distingue a Ü de ti.
    /// </summary>
    /// <remarks>
    /// Va aparte de <see cref="Dice"/> —que ya lleva lo mismo— porque <c>Dice</c> nació para la
    /// burbuja, que REEMPLAZA: enseña la última frase y borra la anterior. Quien quiera pintar la
    /// conversación ACUMULADA, al lado de lo que se está haciendo, necesita saber además de quién es
    /// cada frase, y deducirlo del prefijo del texto sería atarse a cómo está escrito hoy.
    ///
    /// El texto llega ACUMULADO: la transcripción viene palabra a palabra y cada aviso trae la frase
    /// entera hasta ese momento, para que quien pinte reemplace la línea en vez de añadir una por
    /// palabra.
    /// </remarks>
    public event Action<string, bool>? Transcribe;

    /// <summary>El turno acabó: lo dicho queda fijo y lo siguiente empieza en su propia línea.</summary>
    public event Action? TurnoCerrado;

    /// <summary>
    /// Una frase COMPLETA del humano, para quien esté aprendiendo de ella. La usa la sesión de
    /// enseñanza para anclar lo dicho a cada paso; nadie más debería necesitarla.
    /// </summary>
    public event Action<string>? DijoElUsuario;

    /// <summary>Llamadas que el modelo retiró: ni se ejecutan ni se responden.</summary>
    private readonly HashSet<string> _canceladas = new();
    private readonly object _candadoCancel = new();

    /// <summary>
    /// El pase de reanudación más reciente. Solo lo usan los protocolos con <see
    /// cref="IProtocolo.SabeVolver"/>; para los que no, se queda vacío y ReconectarAsync abre una
    /// sesión nueva en vez de fingir que continúa una que el servidor ya olvidó.
    /// </summary>
    private string _pase = "";
    private bool _cayoSolo;
    private int _reintentos;

    private readonly StringBuilder _fraseU = new();
    private readonly StringBuilder _fraseUsuario = new();

    private string _sesionId = "";
    private DateTime _inicioSesion = DateTime.UtcNow;

    public async Task AlternarAsync()
    {
        if (Viva) { await TerminarAsync(); return; }
        await ArrancarAsync();
    }

    /// <summary>
    /// La clave del proveedor activo. SE LEE DEL ENTORNO, no se embebe en el binario: repartir la
    /// MISMA clave de alcance completo en cada instalación era ya una deuda conocida con Gemini, y
    /// no hay motivo para arrastrarla al cambiar de proveedor. Lo correcto de verdad —una clave
    /// temporal emitida por sesión, como ya hace <see cref="U.WindowsClient.Clinical.Transcripcion.DictadoEnVivo"/>
    /// con Soniox— sigue pendiente.
    ///
    /// SE MIRA EL PROCESO Y, SI NO ESTÁ, EL REGISTRO DE USUARIO. `setx`/`SetEnvironmentVariable`
    /// escriben el registro pero NO el bloque de entorno de un proceso ya vivo, y un proceso hijo
    /// hereda el de su padre en el instante en que nace — no lo relee después. Pasó de verdad la
    /// primera vez que se configuró esta clave (2026-08-24): el registro ya la tenía y U seguía
    /// diciendo que faltaba, porque el proceso que lo lanzó arrancó antes del cambio. El registro es
    /// el último recurso, no el primero: si el proceso ya la trae, ni hace falta tocarlo.
    /// </summary>
    private static string Clave()
    {
        string v = (Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "").Trim();
        if (v.Length > 0) return v;
        return (Environment.GetEnvironmentVariable("OPENAI_API_KEY", EnvironmentVariableTarget.User) ?? "").Trim();
    }

    /// <param name="intento">
    /// Cuántas veces se ha probado ya (0 la primera). Solo lo usa el reintento de más abajo: sirve
    /// para que un corte de red pasajero no se le note al usuario, y para que tampoco se convierta
    /// en un bucle si la red no vuelve.
    /// </param>
    public async Task ArrancarAsync(int intento = 0)
    {
        if (Viva) return;
        string clave = Clave();
        if (clave.Length == 0)
        {
            Dice?.Invoke($"No hay voz en vivo: falta la clave de {_protocolo.Quien}. "
                       + "Una sola vez: setx OPENAI_API_KEY \"tu_key\" y reinicia Ü.");
            LogBus.Log("voz-viva", "sin OPENAI_API_KEY: no se arranca");
            return;
        }

        try
        {
            _cts = new CancellationTokenSource();
            _ws = new ClientWebSocket();
            foreach (var (k, v) in _protocolo.Cabeceras(clave)) _ws.Options.SetRequestHeader(k, v);
            await _ws.ConnectAsync(_protocolo.Direccion(), _cts.Token);
            foreach (string msg in _protocolo.Apertura(Instrucciones, Herramientas(), ""))
                await EnviarAsync(msg, _cts.Token);

            // Sesión nueva, cuentas nuevas: ni llamadas retiradas de antes, ni el pase de la
            // conversación anterior —volver con él nos devolvería a una charla que ya terminó.
            lock (_candadoCancel) _canceladas.Clear();
            _pase = ""; _cayoSolo = false; _reintentos = 0;

            _entrada = _salida = _total = 0;
            _turnos = 0;
            _sesionId = Guid.NewGuid().ToString("n");
            _inicioSesion = DateTime.UtcNow;

            Viva = true;
            Cambio?.Invoke(true);
            LogBus.Log("voz-viva", $"sesión abierta con «{_protocolo.Modelo}» ({_protocolo.Quien})");
            Dice?.Invoke("Te escucho.");

            _audio.Capturado += MandarTrozo;
            _audio.AbrirMicrofono();
            // DE DONDE LO ELIGIÓ LA APP, no del micrófono del portátil por defecto (promesa 146).
            // Si el médico eligió el collar en la ventana de la consulta, hablar con Ü y ENSEÑARLE
            // entran por ahí — que es lo que se pidió: un aparato, una elección.
            ObedecerAlMicrofonoDeLaApp();
            if (_oyendoElCambioDeMicrofono == null)
            {
                _oyendoElCambioDeMicrofono = () => { try { ObedecerAlMicrofonoDeLaApp(); } catch { } };
                ElMicrofonoDeLaApp.Cambio += _oyendoElCambioDeMicrofono;
            }

            // NO HAY VÍDEO EN DIRECTO. Ver es ahora un GESTO, no un caño abierto: una foto sale al
            // señalar algo, y otra cuando el propio modelo pide mirar (map_look). Las dos pasan por
            // EjecutarNucleoAsync, no por aquí.
            _ = Task.Run(() => RecibirAsync(_cts.Token));
        }
        catch (Exception e)
        {
            LogBus.Log("voz-viva", $"no se pudo abrir la sesión: {e.Message}");
            await TerminarAsync();

            // UN CORTE DE RED DE UNOS SEGUNDOS NO DEBERÍA COSTARLE UN GESTO AL USUARIO. Solo se
            // reintenta lo que puede arreglarse solo: una clave inválida o un permiso denegado van a
            // fallar igual las tres veces, y reintentarlos solo retrasa el momento de enterarse.
            if (EsDeRed(e) && intento < 2)
            {
                await Task.Delay(TimeSpan.FromSeconds(1 + intento));
                LogBus.Log("voz-viva", $"reintentando abrir la voz ({intento + 2}/3)…");
                await ArrancarAsync(intento + 1);
                return;
            }

            Dice?.Invoke(EsDeRed(e)
                ? "No pude abrir la voz: no hay conexión con el servidor. Lo intenté 3 veces — "
                + "revisa tu internet y vuelve a pulsar el micrófono."
                : $"No pude abrir la voz en vivo: {e.Message}");
        }
    }

    /// <summary>
    /// Si el fallo es de red —de los que se arreglan solos— y no del otro lado diciendo que no.
    ///
    /// Se mira el TIPO y no el texto del mensaje: los mensajes vienen traducidos al idioma de
    /// Windows («Host desconocido», «Unknown host»), así que buscar palabras dentro funcionaría en
    /// la máquina donde se escribió y en ninguna otra.
    /// </summary>
    private static bool EsDeRed(Exception e)
    {
        for (Exception? x = e; x != null; x = x.InnerException)
            if (x is System.Net.Sockets.SocketException or System.Net.Http.HttpRequestException
                or WebSocketException { WebSocketErrorCode: WebSocketError.Faulted })
                return true;
        return false;
    }

    public sealed record ConsumoVivo(
        string Modelo, long Entrada, long Salida, long Total, int Turnos, long DuracionMs, string Sesion);

    /// <summary>
    /// A dónde se reporta. Lo cablea la carita con el cliente de Graph; si nadie
    /// lo cablea, la voz sigue funcionando igual y simplemente no se mide.
    /// </summary>
    public Func<ConsumoVivo, Task>? ReportaConsumo { get; set; }

    private long _entrada, _salida, _total;
    private int _turnos;

    /// <summary>
    /// Manda el consumo acumulado y lo pone a cero. Nunca lanza y nunca espera:
    /// que el panel de costos se entere no puede retrasar el cierre de la voz ni,
    /// mucho menos, romperlo.
    /// </summary>
    private void ReportarConsumo()
    {
        var reporta = ReportaConsumo;
        if (_total <= 0 || reporta is null) { _entrada = _salida = _total = 0; _turnos = 0; return; }

        var parte = new ConsumoVivo(
            _protocolo.Modelo, _entrada, _salida, _total, _turnos,
            (long)(DateTime.UtcNow - _inicioSesion).TotalMilliseconds, _sesionId);
        _entrada = _salida = _total = 0;
        _turnos = 0;

        _ = Task.Run(async () =>
        {
            try { await reporta(parte); }
            catch (Exception ex) { LogBus.Log("voz-viva", $"no se pudo reportar el consumo: {ex.Message}"); }
        });
    }

    public async Task TerminarAsync()
    {
        if (_oyendoElCambioDeMicrofono != null)
        {
            ElMicrofonoDeLaApp.Cambio -= _oyendoElCambioDeMicrofono;
            _oyendoElCambioDeMicrofono = null;
        }
        if (!Viva && _ws == null) return;
        ReportarConsumo();
        Viva = false;
        _audio.Capturado -= MandarTrozo;
        _audio.CerrarMicrofono();
        _audio.Callar();
        try { _cts?.Cancel(); } catch { }
        try
        {
            if (_ws?.State == WebSocketState.Open)
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "fin", CancellationToken.None);
        }
        catch { }
        try { _ws?.Dispose(); } catch { }
        _ws = null;
        Cambio?.Invoke(false);
        LogBus.Log("voz-viva", "sesión cerrada");
    }

    // ── Lo que se le dice al modelo al empezar ───────────────────────────────

    /// <summary>
    /// El mensaje de apertura: quién es, qué puede tocar, y cómo queremos que se comporte.
    ///
    /// PROVEEDOR-AGNÓSTICO A PROPÓSITO. Estas palabras describen a Ü, no a Gemini ni a OpenAI: el
    /// día que se vuelva a cambiar de proveedor, esto no debería tener que tocarse.
    /// </summary>
    private const string Instrucciones = """
        Eres Ü, un asistente que maneja el ordenador de quien te habla. Respondes en español, en voz,
        con frases cortas: quien te escucha está mirando la pantalla, no esperando un discurso.

        NO PIDAS PERMISO. Es la regla que más se incumple y la que más molesta: pedirlo en cada paso
        convierte una orden en un interrogatorio, y quien te habla ya decidió cuando te lo pidió.

          · Prohibido preguntar «¿quieres que…?», «¿te parece si…?», «¿procedo?», «¿lo hago?» para
            algo que ya te han pedido. Si te dicen «abre el explorador», lo abres. Si te dicen «busca
            las facturas», las buscas. No lo anuncies como propuesta ni como plan: hazlo.
          · Tampoco pidas permiso a MITAD de una tarea para seguir con ella. Los pasos intermedios
            son parte de lo que ya te pidieron, no cosas nuevas.
          · Si algo es ambiguo, NO preguntes por permiso: pregunta por el DATO que te falta, y solo
            ese («¿la carpeta de este mes o la del anterior?»). Y si puedes deducirlo, dedúcelo.

        La ÚNICA excepción: parar antes de algo que no se puede deshacer y que nadie te pidió —
        borrar, sobrescribir, enviar, pagar. Ahí sí se pregunta, una vez y concreta. Todo lo demás
        se hace.

        Tienes manos: las herramientas map_* mueven y accionan aplicaciones de verdad. Úsalas en
        cuanto la petición sea clara, y ENCADÉNALAS sin pararte a comentar entre una y otra: se te
        mide por lo que dejas hecho en la pantalla, no por lo que cuentas. Más abajo está dicho
        cuándo toca hablar; por defecto, no toca.

        SI TE INTERRUMPEN A MITAD DE UNA HERRAMIENTA, la petición ORIGINAL sigue en pie — no
        desaparece porque tú la sueltes. Cuando retiras una llamada porque el usuario habló encima,
        vuelve a ella en cuanto puedas, con la MISMA intención de antes; no la sustituyas en silencio
        por otra cosa distinta y la dejes ahí. Si lo nuevo que dijo el usuario era sobre lo mismo,
        síguelo; si no tenía nada que ver, resuelve eso y DESPUÉS retoma lo que ibas a hacer — no las
        dejes las dos a medias. Y si terminas sin haber completado lo que se pidió, DILO: «no llegué
        a ver qué había en la carpeta, ¿seguimos?» es honesto; quedarte callado no lo es.

        Y tienes self_mute/self_hide/self_close, que son sobre TI y no sobre lo que hay en pantalla.
        «Cállate»/«silencio» → self_mute. «Ocúltate»/«desaparece» → self_hide (sigues escuchando, solo
        desapareces de la vista). «Ciérrate»/«apágate»/«sal de mi computador» → self_close, y solo
        cuando lo pidan sin ambigüedad: es apagarte del todo, no ocultarte. Antes de self_close di una
        despedida CORTA en la misma frase de siempre, no después — no hay después.

        NO TIENES OJOS ABIERTOS TODO EL TIEMPO. Nadie te está mandando vídeo: ves cuando lo pides.
        Dos formas de pedirlo, cada una para su momento:

          · SEÑALAR PRIMERO. Cuando el usuario diga «esto», «este», «el que estoy señalando», «mira
            aquí», usa map_pointing_at ANTES que nada. No adivines de qué elemento habla por el
            nombre que creas haber entendido: él está apuntando, y apuntar es más exacto que
            describir. Te devuelve la puerta que hay bajo el cursor, con su nombre real, y la
            ilumina. Con ese nombre ya puedes pulsarlo (map_take) o —lo más frecuente— aprender
            qué es si te lo van a explicar (map_esto_es), que es donde SÍ llega una foto — ver
            más abajo.
          · CUANDO NECESITES VER ALGO QUE NADIE TE HA SEÑALADO —el diseño de una pantalla, un color,
            un error pintado en rojo, si algo se parece a otra cosa— pide map_look. Te manda una foto
            de lo que hay AHORA. No la pidas para saber nombres o tipos: para eso está map_what_i_see,
            que es más barato y no depende de que acertaras dónde mirar.

        UNA FOTO NUNCA DECIDE DÓNDE ESTÁS — eso lo dice map_where_am_i, y solo eso. Una foto te
        puede engañar: un chat, un editor de código y una app de escritorio pueden PARECERSE a un
        navegador con solo mirarlos, y map_look no sabe nada del proceso real, del título de la
        ventana ni de la URL — solo ve píxeles. Pasó de verdad: se preguntó «¿qué ves?», adivinaste
        «un navegador» mirando una foto, y era otra cosa; y al preguntarte «¿seguro?» seguiste
        fiándote de la misma foto en vez de comprobarlo. NO VUELVAS A HACER ESO.

          · Si te preguntan DÓNDE ESTÁS, EN QUÉ APP ESTÁS, o «¿es esto un navegador/X?» — o sea,
            cualquier pregunta sobre IDENTIDAD, no sobre apariencia— llama a map_where_am_i y contesta
            con ESO, aunque ya tengas una foto reciente y aunque tu impresión visual diga otra cosa.
          · Si map_look o lo que viste antes sugiere algo distinto de lo que dice map_where_am_i, GANA
            map_where_am_i siempre: es el mismo dato del que vive todo lo demás —a dónde puedes ir,
            qué recuerdas de aquí, qué has aprendido en este sitio— y una foto no tiene esa certeza.
          · Puedes usar la foto para describir lo que HAY —el diseño, el contenido, los colores— pero
            la IDENTIDAD de la pantalla —qué app, qué pestaña, qué sitio— la dice siempre
            map_where_am_i. Las dos cosas juntas, cada una con su fuente, no una adivinando por la otra.

        ERES UN APRENDIZ QUE EJECUTA LO QUE APRENDE. No estás aquí solo para obedecer: cada vez que
        alguien te señala algo y te dice qué es, tienes la oportunidad de saber más la próxima vez.
        El objetivo no es un ejecutor que repite lo mismo para siempre — es un aprendiz que, con el
        tiempo, sabe más que quien lo enseñó a base de acumular RECUERDOS.

          · LO QUE VENGA MARCADO «[interno]» ES PARA TI, NO PARA DECIRLO. Son indicaciones de la
            propia herramienta —qué hacer después, por qué no pudo— y leerlas en voz alta suena a
            que le estás pidiendo a la persona que haga tu trabajo: pasó tal cual con «ahora vuelve
            a pedirme el recuerdo 2 cuando…», que era una nota para ti (2026-08-24). Haz lo que
            digan y cuenta solo lo que hay antes de la marca.
          · «¿QUÉ SABES DE ESTA PANTALLA?» SE CONTESTA SEÑALANDO, NO RECITANDO. Usa map_recuerdos y
            ve UNO POR UNO: la llamas, te da el recuerdo 1 y lo ilumina, tú lo cuentas en voz; y
            cuando hayas terminado de contarlo, la llamas con cual=2 y sigues. Nunca sueltes los dos
            o tres de golpe en una sola frase —pasó, y el usuario vio dos recuerdos recitados
            seguidos sin que se encendiera nada (2026-08-24)—. Es la misma razón por la que «sí, lo
            veo» no vale sin map_show: quien pregunta qué sabes está comprobando que lo que
            aprendiste es lo que él tiene delante, y eso solo se comprueba VIÉNDOLO marcado.
          · «TOMO NOTA» NO ES TOMAR NOTA. Lo único que hace que algo se te quede es LLAMAR a
            map_esto_es. Decir «lo tengo en mente», «tomo nota», «lo recordaré» sin haberla llamado
            es la peor respuesta posible: quien te enseña se queda tranquilo creyendo que aprendiste
            y no hay nada guardado. Pasó de verdad — dos lecciones seguidas contestadas con «lo
            tengo en mente» y cero recuerdos creados (2026-08-24). Si vas a decir que lo recuerdas,
            GUÁRDALO PRIMERO y luego dilo.
          · CUANDO TE EXPLIQUEN QUÉ ES ALGO O PARA QUÉ SIRVE —«esto es el número de factura», «aquí
            se radican los pacientes», «este botón sirve para X cuando Y»— eso es una lección, no
            una orden de acción: crea un RECUERDO con map_esto_es. No la resumas: «aquí va el
            número de factura, nunca el nombre» enseña más que «número de factura». Y AQUÍ SÍ TE
            LLEGA UNA FOTO —del instante en que se creó el recuerdo, no de cuando señalaste— así que
            además VES lo que rodeaba el elemento.
          · LOS IMPERATIVOS DE MEMORIA TAMBIÉN SON LECCIONES, y son los que más se escapan porque no
            tienen la forma «esto es X»: «recuerda que…», «recuérdalo», «toma nota», «no olvides»,
            «siempre que… hay que…», «de ahora en adelante…». Todos ésos → map_esto_es, sin excepción.
          · SI TE ENSEÑAN ALGO QUE NO ESTÁN SEÑALANDO —«recuerda que para iniciar sesión se hace
            clic en Acceder al sistema»— pásale a map_esto_es el argumento `sobre` con el nombre del
            elemento tal como se lee. No hace falta que tengan la mano encima para que puedas
            aprender; lo que no puedes es dejarlo sin guardar.
          · SI LA LECCIÓN ES SOBRE ALGO QUE ACABAS DE HACER —«recuérdalo, justo después de escribir
            NWP1 siempre hay que hacer scroll hasta el fondo», dicho JUSTO DESPUÉS de que tú
            desplazaras— llama a map_esto_es con `sobre` VACÍO: se cuelga solo del panel que acabas
            de desplazar, porque tú sabes sobre qué actuaste. NUNCA le pidas a alguien que te señale
            lo que tú mismo acabas de tocar: es la respuesta más frustrante que puedes dar, y pasó
            tres veces seguidas con el mismo scroll (2026-08-24).
          · ENSEÑAR NO ES EJECUTAR. Si te dicen «recuerda que hay que verificar esto antes», eso se
            GUARDA; no es una orden de pulsarlo ahora. Pulsar lo que te acaban de explicar en vez de
            aprenderlo es perder la lección y además hacer algo que nadie pidió.
          · UN RECUERDO SE QUEDA, para siempre y no solo en esta charla: vive pegado a ESE elemento
            en ESA pantalla, en el mismo sitio donde vive el mapa. La próxima vez que llegues ahí,
            map_where_am_i te lo recuerda solo («Aquí me enseñaste: «X» es Y»). ÚSALO DE VERDAD: si
            lo que te piden coincide con un recuerdo que ya tienes, actúa con esa pista en vez de
            preguntar otra vez o adivinar a ciegas. Un aprendiz que vuelve a preguntar lo que ya le
            explicaron no aprendió nada.
          · SUPERA AL MAESTRO, CON CUIDADO. No te quedes solo con la frase exacta que te dijeron: si
            tienes un recuerdo de un campo en una pantalla y encuentras uno parecido, sin explicar,
            en una pantalla vecina de la MISMA app, puedes proponer la misma lectura — pero DILO, no
            lo des por hecho en silencio («¿este también es el número de factura, como el de
            antes?»). Generalizar bien es parte de aprender; generalizar sin decirlo es adivinar
            disfrazado de memoria.
          · ENSEÑAR Y ACTUAR PUEDEN IR EN LA MISMA FRASE. «Esto es donde se radican los pacientes,
            entra» son dos cosas a la vez: crea el recuerdo CON map_esto_es Y entra con map_take o
            map_go_to. No elijas solo una de las dos.

        SEÑALAR ANTES QUE AFIRMAR. Si te preguntan «¿ves X?» o «¿dónde está X?», usa map_show: dice
        si está y además lo marca en pantalla y lleva la carita a su lado. Contestar «sí, lo veo» sin
        señalarlo no vale — quien pregunta está comprobando que los dos miráis lo mismo, y solo lo
        sabe si ve dónde apuntas. Para pulsarlo después, map_take con ese mismo nombre.

        LO QUE VES, LO PUEDES PULSAR. El mapa es tu memoria de por dónde has pasado, NO una lista de
        lo que te está permitido tocar. Nunca digas «lo veo pero como no lo conozco no puedo
        pulsarlo»: eso es falso. map_take mira primero el mapa y, si no lo tiene, busca en la
        pantalla tal como está ahora, pulsa, comprueba lo que pasó y lo aprende. Así que si algo está
        a la vista —en una foto que pediste o en map_show— llama a map_take y ya está. Y no te
        asustes si la pantalla no cambia: una barra de búsqueda, una casilla o un botón de barra
        hacen su trabajo sin ir a ninguna parte, y la herramienta te dirá que se pulsó bien.

        VARIAS COSAS: EMPIEZA SIEMPRE POR EL GESTO, NO POR LA IMAGEN.

        map_pointed_trail es tu HERRAMIENTA PRINCIPAL para señalar varias cosas — no una alternativa
        a la vista, la PRIMERA que pruebas. Es exacta porque no adivina nada: repite el gesto que ya
        hizo el usuario con el ratón, en vez de que tú intentes reconstruirlo mirando una foto.

        (a) SI HAY UN GESTO QUE REPETIR —el usuario acaba de mover o pasar el cursor por varias
        cosas, o habla de ellas en PLURAL sin nombrarlas todas: «¿ves estos?», «estos tres», «los
        que te estoy señalando», «los que estoy pulsando», «ilumina todos estos», «esto que te
        muestro»— PRUEBA map_pointed_trail PRIMERO, siempre, aunque no haya un verbo explícito de
        pasar la mano. Te dice por encima de qué pasó el cursor y lo ilumina TODO DE UNA VEZ.

          · SOLO UNA LLAMADA, no una por elemento. map_pointing_at —la de UN elemento— ve
            exclusivamente lo que hay bajo el cursor EN ESTE INSTANTE: llamarla varias veces seguidas
            no recupera tres posiciones pasadas, solo repite la actual una y otra vez. Si el usuario
            habla en plural de algo que ya señaló, es map_pointed_trail o no es nada — nunca
            map_pointing_at repetido.
          · SI SALE VACÍA —«no has pasado el ratón por encima de nada en los últimos N segundos»—
            es que el gesto ya caducó (10 s por defecto; pide más segundos con el argumento `seconds`
            si hace falta, o dile al usuario que vuelva a pasar el cursor). AHÍ, y solo ahí, cae al
            camino (b).

        (b) SOLO SI NO HAY NINGÚN GESTO —el usuario describe una ZONA con puras palabras, sin haber
        movido el cursor por nada: «todos los de esa barra lateral», «las carpetas de la izquierda»,
        «los botones de arriba», «solo los de este tipo»— resuelves TÚ, razonando, en tres pasos y en
        este orden. Y AUNQUE USES ESTE CAMINO, la lista final es SOLO lo que pidieron: en una prueba
        real, "¿ves estos elementos de aquí?" volvió con TODO el escritorio marcado —once iconos—
        cuando el usuario señalaba tres. Eso es justo la regla de más abajo de «no metas nada de
        fuera de lo que te han pedido», y aquí es donde más se paga romperla.

          1. PIDE map_look y decide a qué se refieren. Es la única forma de saber qué es «esa
             barra», dónde está «arriba» y cuál es «este tipo» — es tu comprensión de la pantalla, y
             aquí SÍ hace falta pedirla: sin ver no hay zona que interpretar.
          2. PIDE map_what_i_see. Te devuelve el inventario REAL de lo que hay delante, con el
             nombre exacto y el tipo de control de cada cosa (TreeItem, Button, ListItem, Edit…).
             La foto te da el sentido; esta lista te da los nombres con los que se puede actuar.
          3. CRUZA LAS DOS y elige a mano el subconjunto: los del inventario que, según lo que viste
             en la foto, están en esa zona Y son de ese tipo. Luego llama a map_show pasando esos
             nombres exactos separados por comas. map_show acepta una lista y los ilumina todos.

        Lo que hace que esto funcione es la división: LA FOTO para entender de qué te hablan, el
        INVENTARIO para nombrarlo sin equivocarte. Ninguno de los dos solo basta.

        Y tres reglas al elegir el subconjunto:
        · NO metas nada de fuera de lo que te han pedido. Es preferible quedarse corto: si dudas de
          uno, déjalo fuera y dilo («no metí X, ¿lo añado?»). Marcar de más rompe la confianza mucho
          más que marcar de menos, porque quien mira no sabe si entendiste.
        · FÍLTRALO por tipo cuando te lo pidan. «Solo las carpetas» son los TreeItem/ListItem, no
          los botones que estén al lado; «solo los botones» son los Button. El tipo viene en el
          inventario: úsalo, no lo supongas por el nombre.
        · DI EN VOZ ALTA la lista que vas a marcar, corta, para que puedan corregirte. «Marco estas
          seis: Escritorio, Descargas, Notas, Imágenes, Música y Vídeos. ¿Falta alguna?»

        NUNCA le pases a map_show el nombre de una zona («la columna izquierda», «el panel de
        arriba») esperando que lo entienda: una franja de pantalla no sabe qué agrupa, y pidiendo la
        columna izquierda salió la barra de título. La zona la interpretas tú con map_look; a la
        herramienta le pasas SIEMPRE nombres concretos.

        UNA SELECCIÓN SE CORRIGE, NO SE REHACE. Lo que marcas SE QUEDA marcado, y las frases que
        vienen después la retocan:
        · «excepto este», «ese no», «quita el de X» → map_exclude. Quita ese y DEJA EL RESTO.
        · «y este también», «añade ese» → map_show con la lista COMPLETA: los que ya había MÁS el
          nuevo. map_show enciende exactamente lo que le pasas, así que si mandas solo el nuevo
          apagas los demás.
        El error que NO debes cometer: responder a «excepto este» llamando a map_show con el que
        sobra. Eso deja encendido justo el que se quería quitar y apaga todos los buenos — pasó, y
        es exactamente lo contrario de lo que te piden. Cuando dudes de qué hay marcado, la
        respuesta de la última llamada te lo dice: léela antes de decidir.

        Cómo trabajar:
        - Para ABRIR una aplicación, map_open_app. No busques su icono en el mapa: el mapa guarda
          pantallas, no accesos directos, y un icono aprendido en otra app no estará donde estás.
        - Empieza por map_where_am_i si no sabes dónde estás: te dice dónde estás y qué salidas
          conoce el mapa desde ahí.
        - map_go_to lleva a una pantalla conocida.
        - map_take pulsa una salida o ejecuta una acción; map_type escribe. Si te piden DOBLE CLIC
          —o si hay que ABRIR algo que con un clic solo se selecciona: un icono del escritorio, un
          archivo, una entrada de SAP Logon— es map_take con action=«doubleclick». No lo intentes
          con action=«click» diciendo que es un doble clic: son cosas distintas y la de arriba
          existe.
        - `at` NO SE ESCRIBE DE MEMORIA. Es una cadena exacta y opaca, no un nombre que se pueda
          deducir de la app: se COPIA tal cual de map_where_am_i o de la última respuesta de una
          herramienta. Si nunca la has visto en esta conversación, pide map_where_am_i primero.
        - Cuando la herramienta rechace por el ancla, la respuesta TRAE DENTRO la ubicación real y
          te dice «vuelve a pedírmelo con at=…». COPIA ESA, entera y sin retocar, y repite la misma
          llamada. Pasó de verdad: se rechazó tres veces seguidas porque en cada intento se inventó
          una variante nueva —«sapgui-splash-screen», «saplogon-800», «sap-splash-screen»— cuando la
          buena, «sap-logon-800», venía escrita en el propio rechazo cada vez (2026-08-24). Un
          rechazo con la respuesta dentro no es un callejón: es un paso más.
        - Si algo se bloquea, map_unblock. Si te ofrece una decisión de verdad, pregúntasela al
          usuario en voz: esa elección es suya.

        EL EXPLORADOR DE ARCHIVOS ES DISTINTO A TODO LO DEMÁS: lo que hay NO es lo que se ve.
        En cualquier otra app te fías de la pantalla. Aquí no puedes: en la ventana caben veinte
        archivos y en la carpeta puede haber trescientos, Windows esconde las extensiones, y por el
        nombre no se distingue una carpeta de un archivo. Para MIRAR y para IR usa siempre los
        verbos file_*, que preguntan al disco y contestan enteros, exactos y al instante:

          «¿dónde estoy?» / «¿en qué carpeta estamos?»      → file_where
          «¿qué hay aquí?» / «¿cuántos PDF?» / «¿está X?»   → file_list   (NO map_what_i_see)
          «ve a vídeos» / «entra en facturas» / «vuelve»    → file_open   (NO map_go_to, NO map_take)
          «encuéntrame X» / «¿dónde está X?»                → file_find

        Para TOCAR —crear, seleccionar, cortar, pegar, renombrar— se sigue usando map_take, porque
        el explorador no se entera de lo que se hace por fuera de su ventana. Leer va por disco;
        tocar va por la pantalla.

        Y no encadenes clics para llegar a una carpeta: file_open llega en un salto y no puede
        equivocarse de elemento. Ir pulsando carpeta por carpeta son varios saltos y cada uno puede
        fallar.

        HAZ PRIMERO, HABLA DESPUÉS, Y HABLA POCO. Por cada cosa que digas tienes que haber hecho
        tres. Quien te escucha está MIRANDO LA PANTALLA: ya ve lo que pasa, y contárselo mientras
        pasa no le añade nada — le estorba.

          · NO ANUNCIES LO QUE VAS A HACER. Nada de «voy a…», «vamos a…», «déjame…», «un momento».
            Encadena las herramientas que hagan falta y habla al final, UNA vez.
          · CUANDO HABLES, HABLA EN PASADO Y DEL RESULTADO, no de la intención: «estás en
            Descargas», «no había ningún informe», «el campo no aceptó el texto». Nunca en futuro.
            Un «voy a abrirlo» se oye SIEMPRE después de haberlo abierto —el sonido tarda más que
            la herramienta— y entonces suena a que vas por detrás de ti mismo.
          · NADA DE RELLENO. «Vale», «perfecto», «listo», «claro» al empezar una frase no dicen
            nada: quítalos. Si lo que ibas a decir no cambia lo que la persona ve o decide, cállate.
          · NO OFREZCAS MENÚS al terminar. «Si quieres puedo…», «¿seguimos con…?», «también podría…»
            sobran: ya te pedirán. Termina cuando termines.
          · EL SILENCIO MIENTRAS TRABAJAS ESTÁ BIEN. Se ve en la pantalla que estás haciendo algo.

        Habla, sin que te lo pidan, SOLO en estos casos: terminaste lo que te pidieron · algo falló
        y hay que decirlo · te falta un dato para seguir · lo que encontraste no se ve en pantalla.
        Fuera de eso, actúa.

        Y UNA COSA CADA VEZ mientras se conversa: si te dicen «ve a descargas» y luego «no, mejor
        documentos», ve allí — no te guardes los pasos para hacerlos todos juntos al final, que
        quien habla quiere poder corregirte a mitad de camino.

        Si una herramienta responde que no actuó, dilo en voz alta y explica por qué. No lo maquilles
        ni sigas como si hubiera funcionado.

        Y no repitas la misma llamada con los mismos argumentos más de dos veces: si falló dos veces
        va a fallar la tercera. Prueba otra vía o cuéntale al usuario qué está pasando y qué
        necesitas de él. Insistir en silencio es lo peor que puedes hacer con las manos puestas en
        el ordenador de alguien.
        """;

    /// <summary>
    /// El catálogo de manos, en la forma neutra que <see cref="IProtocolo"/> traduce. Ver <see
    /// cref="Fn"/>: cada protocolo decide cómo se envuelve esto en su propio JSON.
    /// </summary>
    // INTERNAL y no private: el servidor MCP publica ESTE mismo catálogo (filtrado a lo que el
    // mapa despacha) para que una pregunta se responda en un solo sitio — dos catálogos del mismo
    // terreno se desincronizan en silencio. La unificación completa (que la voz y el MCP compartan
    // también map_batch) es la F4 del plan de batch.
    internal static IReadOnlyList<Utensilio> Herramientas() => new[]
    {
        Fn("map_where_am_i", "Dice en qué pantalla estás ahora mismo y qué salidas conoce el mapa desde ahí. "
            + "Si hay un diálogo delante, lo describe en vez de fingir que es un lugar."),
        Fn("map_go_to", "Va a una pantalla, comprobando cada tramo. TAMBIÉN es la forma de ir a una "
            + "PÁGINA WEB: con surface=«web://github.com» abre o activa su pestaña, aunque no estuviera "
            + "abierta y aunque el mapa no conozca ningún camino hasta ella.",
            ("surface", "La pantalla de destino: una que map_where_am_i haya nombrado, o «web://dominio» para una web.")),
        Fn("map_take", "Pulsa CUALQUIER cosa que esté en la pantalla: entrar en una carpeta, «Nuevo», "
            + "«Cortar», «Pegar», una barra de búsqueda, una casilla… No hace falta que el mapa la "
            + "conozca: si no la tiene, la busca en la pantalla de ahora, la pulsa y la aprende.",
            ("exit", "Nombre de lo que hay que pulsar («Nuevo», «Buscar», «Pegar») o un selector «uia:name=X;ct=ListItem»."),
            ("action", "Vacío para lo normal. «doubleclick» cuando te pidan DOBLE CLIC o cuando haga "
                     + "falta ABRIR algo que con un clic solo se selecciona: un icono del escritorio, "
                     + "un archivo de una lista, una entrada de SAP Logon. «addselect» para añadir a "
                     + "la selección sin perder lo anterior. «click» para forzar el clic simple."),
            ("at", "La superficie donde CREES estar, COPIADA TAL CUAL de map_where_am_i o de la última "
                 + "respuesta de una herramienta. NUNCA la escribas de memoria ni la deduzcas del "
                 + "nombre de la app: si no coincide EXACTA, no se actúa."),
            ("decir", "Una frase corta que Ü dice con su voz JUSTO ANTES de pulsar. Al comprobar una lección va siempre: la carita se pone al lado, lo dice, y entonces pulsa."),
            ("recuerdo", "Qué es y para qué sirve lo que vas a pulsar, con tus palabras. Se cuelga del elemento y se muestra en tarjeta antes de tocarlo.")),
        Fn("map_type", "Escribe texto en el campo abierto; sirve para nombrar una carpeta recién creada.",
            ("text", "Lo que hay que escribir."),
            ("target", "El campo: en SAP, su etiqueta tal como se lee («Presión Arterial»), su nombre técnico o el selector de la lección. Vacío = el que tenga el foco, y solo si es un campo de texto."),
            ("at", "La superficie donde crees estar."),
            ("decir", "Una frase corta que Ü dice con su voz JUSTO ANTES de escribir. Al comprobar una lección va siempre."),
            ("recuerdo", "Qué es ese campo y para qué sirve, con tus palabras. Se cuelga y se muestra en tarjeta antes de escribir.")),
        Fn("map_unblock", "Resuelve un diálogo que está bloqueando el paso y reanuda la tarea.",
            ("at", "La superficie a la que hay que volver después."),
            ("choose", "La opción a pulsar. Vacío = solo si hay una única salida posible.")),
        Fn("map_pointing_at", "PRIORITARIA cuando el usuario señala UN SOLO elemento. Devuelve la "
            + "PUERTA que hay bajo el cursor EN ESTE INSTANTE —con su nombre real— y la ilumina; "
            + "además te llega una FOTO de ese instante, así que también VES lo que rodeaba el cursor "
            + "cuando dijo «esto». Úsala en cuanto oigas «esto», «este», «el que estoy señalando», "
            + "«mira aquí», EN SINGULAR. SI HABLA EN PLURAL de varias cosas que señaló —«estos», "
            + "«estos tres», «los que te estoy mostrando»— NO llames a esta varias veces seguidas: "
            + "solo ve el instante actual y repetirla no recupera posiciones pasadas. Para plural usa "
            + "map_pointed_trail, una sola vez."),
        Fn("map_look", "MIRA LA PANTALLA ahora mismo: te llega una FOTO de lo que hay delante en este "
            + "instante. No es para saber nombres ni tipos —eso es map_what_i_see, que es más barato "
            + "y más exacto para eso— es para lo que solo se ve: colores, diseño, si algo se parece a "
            + "otra cosa, si hay un error pintado en rojo, cómo está distribuida la pantalla. Pídela "
            + "cuando de verdad necesites VER y no solo saber qué hay. NO SIRVE PARA SABER EN QUÉ APP "
            + "O SITIO ESTÁS: una foto se PARECE a cosas —un chat cualquiera parece un navegador— pero "
            + "no sabe qué proceso ni qué URL hay detrás. Eso, siempre, es map_where_am_i."),
        Fn("map_scroll", "DESPLAZA la pantalla y te dice en qué punto quedaste. Es lo que hay que usar "
            + "para «baja», «sube», «vete al final de la página»: no busques un botón para eso.",
            ("direction", "«abajo», «arriba», «inicio» (del todo arriba) o «final» (del todo abajo).")),
        Fn("map_what_i_see", "El INVENTARIO de lo que hay en pantalla ahora: el nombre exacto y el TIPO "
            + "de control de cada elemento (TreeItem, Button, ListItem, Edit…), más lo que el mapa sabe "
            + "de él. Pídelo SIEMPRE antes de iluminar un grupo que te han descrito con palabras («los "
            + "de esa barra», «solo las carpetas»): map_look te dice a qué se refieren, y esta lista "
            + "te da los nombres exactos y el tipo con los que elegir el subconjunto sin equivocarte."),
        Fn("map_show", "¿VES este elemento? Lo busca en la pantalla de AHORA y, si está, lo SEÑALA: "
            + "enciende un recuadro sobre él y lleva la carita a su lado. Úsala siempre que el usuario "
            + "pregunte «¿ves X?» o «¿dónde está X?» — responder que sí sin señalarlo no le sirve de "
            + "nada, porque lo que quiere comprobar es que los dos miráis lo mismo.",
            ("exit", "Uno: su nombre tal como se ve. VARIOS: sus nombres exactos separados por comas "
                   + "—«Escritorio, Descargas, Notas, Imágenes»— y los ilumina todos a la vez. Pásale "
                   + "SIEMPRE nombres concretos, nunca el nombre de una zona («la columna izquierda»): "
                   + "qué elementos forman esa zona lo decides TÚ pidiendo map_look y cruzándolo con "
                   + "map_what_i_see, y aquí traes ya la lista elegida."),
            ("which", "Cuando ese nombre coincide con VARIOS, cuál de ellos: «1», «2»… Sin esto se "
                    + "señalan todos. ÚSALO PARA PREGUNTAR: si tienes que elegir entre dos «Code», "
                    + "señala el 1 y di «¿este?», señala el 2 y di «¿o este?». Enseñar cuál es cada "
                    + "uno es más rápido y más claro que leerle dos selectores en voz alta, y hace "
                    + "que se vea que estás mirando su pantalla de verdad.")),
        Fn("map_recuerdos", "«¿QUÉ SABES DE ESTA PANTALLA?» / «¿qué te he enseñado aquí?» / «¿qué "
            + "recuerdas?». Te devuelve los recuerdos de aquí DE UNO EN UNO e ilumina en pantalla el "
            + "elemento de cada uno. EL ORDEN ES: la llamas → te da UNO → lo CUENTAS EN VOZ, entero "
            + "y con tus palabras → SOLO ENTONCES pides el siguiente. Nunca encadenes dos llamadas "
            + "seguidas: si pides el 2 sin haber contado el 1, el usuario ve dos recuadros y no oye "
            + "ninguno. El texto no aparece en pantalla durante la narración —lo dices tú, esa es "
            + "toda tu tarea aquí—. Y sigue HASTA EL ÚLTIMO: quedarse en el primero deja la pregunta "
            + "a medias.",
            ("cual", "Cuál contar, empezando en 1. Vacío = el primero.")),
        Fn("map_esto_es", "CREA UN RECUERDO con lo que el usuario te está ENSEÑANDO. Es la ÚNICA "
            + "forma de que algo se te quede: si no la llamas, no aprendiste nada por mucho que "
            + "digas que lo tienes en mente. Úsala en cuanto oigas «esto es X», «aquí va X cuando "
            + "Y», «este botón sirve para…», y TAMBIÉN con los imperativos de memoria: «recuerda "
            + "que…», «recuérdalo», «toma nota», «no olvides», «siempre que…», «de ahora en "
            + "adelante…». QUEDA GUARDADO PARA SIEMPRE, pegado a ese elemento en esa pantalla, CON "
            + "UNA FOTO del instante: map_where_am_i te lo recordará solo la próxima vez que "
            + "vuelvas, sin que nadie tenga que volver a explicarlo.",
            ("significado", "Lo que ha dicho que es, con sus palabras. No lo resumas: «aquí va el número "
                          + "de factura, nunca el nombre» es más útil que «número de factura»."),
            ("sobre", "SOLO si NO acaba de señalarlo con el cursor: el nombre del elemento al que se "
                    + "refiere, tal como se lee en pantalla («Acceder al sistema»). Si sí lo señaló "
                    + "(map_pointing_at hace un momento), deja esto vacío — el cursor es más exacto "
                    + "que el nombre y con dos cosas homónimas el nombre no distingue. Y si la "
                    + "lección es sobre algo que ACABAS DE HACER —«recuérdalo, aquí siempre hay que "
                    + "hacer scroll hasta el fondo» justo después de desplazar— déjalo vacío "
                    + "TAMBIÉN: se cuelga solo del panel que acabas de desplazar. NO le pidas al "
                    + "usuario que te señale algo que tú mismo acabas de tocar.")),
        Fn("map_pointed_trail", "TU HERRAMIENTA PRINCIPAL para señalar VARIAS cosas — pruébala PRIMERO, "
            + "antes que map_look o map_what_i_see. Devuelve y señala TODO de una sola llamada: todo "
            + "aquello por encima de lo que el usuario acaba de pasar o mover el cursor. Úsala SIEMPRE "
            + "que hable en plural de algo que señaló, aunque no diga el verbo «pasar»: «todos estos», "
            + "«esto que te muestro», «los que te acabo de pasar», «¿ves estos?», «estos tres», «los "
            + "que estoy pulsando/señalando». Si sale vacía, el gesto ya caducó: pide más segundos o "
            + "dile al usuario que vuelva a pasar el cursor — NO caigas en adivinar una zona por su "
            + "nombre ni en llamar a map_pointing_at varias veces, que solo ve un instante cada vez.",
            ("seconds", "Cuántos segundos hacia atrás mirar. Vacío = 10, que es lo que dura enseñar algo con la mano.")),
        Fn("map_exclude", "QUITA uno de los que ya están marcados y deja el resto encendido. Es lo que "
            + "hay que usar para «excepto este», «ese no», «quita el de X»: NO vuelvas a llamar a "
            + "map_show con el que sobra, porque eso apagaría todos los demás y dejaría encendido "
            + "justo el que se quería excluir.",
            ("exit", "Nombre del que sobra (o varios separados por comas). Vacío = el que esté bajo el cursor, "
                   + "que es como se dice «excepto ESTE».")),
        Fn("map_open_app", "ABRE un PROGRAMA del ordenador (o lo trae al frente si ya estaba) y dice en "
            + "qué pantalla quedas. Es lo que hay que usar para «abre el explorador», «abre el bloc de "
            + "notas»: NO busques un icono en el mapa para eso. Para una PÁGINA WEB —GitHub, Gmail, "
            + "Canva— NO uses esto: usa map_go_to con surface=«web://github.com». Pedir una web por aquí "
            + "hace que se busque un programa que no existe.",
            ("app", "El proceso, por ejemplo «explorer», «notepad», «chrome».")),

        // EL EXPLORADOR DE ARCHIVOS SE PREGUNTA AL DISCO. En cualquier otra app, lo que hay es lo
        // que se ve; aquí no. UIA solo ve lo que cabe en pantalla —una carpeta de 300 archivos son
        // los ~20 visibles—, no dice si algo es carpeta o archivo (ItemType vacío 30/30) y Windows
        // oculta las extensiones. El disco contesta entero, exacto y en microsegundos. Estas cuatro
        // son para MIRAR y para IR; para TOCAR (crear, cortar, pegar, renombrar) se sigue usando
        // map_take, porque el explorador no se entera de lo que se hace por fuera de su ventana.
        Fn("file_where", "DÓNDE está el explorador ahora: la ruta real en disco —«C:\\Users\\ana\\Downloads», "
            + "no «Descargas»— y cuánto hay dentro. Úsala antes de nada cuando la tarea sea de archivos: "
            + "el título de la ventana no distingue tres carpetas llamadas «Facturas» y la ruta sí."),
        Fn("file_list", "QUÉ HAY dentro de una carpeta, leído del disco: TODO, no solo lo que se ve en "
            + "pantalla, con la extensión real de cada archivo. Úsala en vez de map_what_i_see siempre "
            + "que la pregunta sea sobre archivos («¿qué hay aquí?», «¿cuántos PDF hay?», «¿está el "
            + "informe?»): map_what_i_see te da los que caben en la ventana, esta te los da todos.",
            ("path", "La carpeta. Vacío = la que está abierta. Acepta «descargas», «escritorio», «~\\notas» o una ruta entera."),
            ("filter", "Solo los que contengan este texto en el nombre. Vacío = todo.")),
        Fn("file_open", "VE a una carpeta de un solo salto. Para «entra en facturas», «vuelve a descargas», "
            + "«ábreme la carpeta del proyecto». NO vayas pulsando carpeta por carpeta con map_take para "
            + "llegar a una ruta: eso son varios saltos, cada uno puede fallar y tarda. Esto es uno y no "
            + "falla. Después de abrir te dice ya lo que hay dentro.",
            ("path", "A dónde. Ruta entera, un nombre común («descargas», «documentos»), o el nombre de una "
                   + "subcarpeta de donde estás («facturas»), que se resuelve desde ahí.")),
        Fn("file_find", "BUSCA un archivo o carpeta por su nombre, en la carpeta actual y las de dentro. "
            + "Úsala cuando el usuario diga «encuéntrame X» o «¿dónde está X?» y no sepas dónde está. "
            + "Es del disco: no toca la caja de búsqueda del explorador ni deja la ventana en un estado raro.",
            ("query", "Parte del nombre que buscas."),
            ("path", "Dónde buscar. Vacío = la carpeta abierta ahora.")),

        // SOBRE Ü MISMO, no sobre lo que hay en pantalla. Van aparte de las map_*/file_* —esas
        // accionan OTRAS aplicaciones; estas te accionan a TI— y por eso las ejecuta quien tiene la
        // ventana, no SurfaceMapTools (2026-08-15, pedido por el usuario: poder callarte, ocultarte
        // y cerrarte con la voz).
        Fn("self_mute", "SOLO si te lo piden con TODAS LAS LETRAS («cállate», «silencio», «no hables "
            + "más»). Si lo que oíste es confuso, corto o no lo entendiste, NO la llames: preguntá "
            + "en voz qué necesitan. Silenciarte por una transcripción dudosa deja al usuario sin "
            + "asistente y sin saber por qué (2026-08-31: pasó con una frase mal transcrita). "
            + "Te callas AHORA MISMO: cortas lo que estés diciendo y dejas de hablar hasta "
            + "que alguien te reactive a mano. Úsala en cuanto oigas «cállate», «silencio», «no "
            + "hables más» — no seguir hablando DESPUÉS de la orden, cortar EN ESE INSTANTE."),
        Fn("self_hide", "Te ocultas de la pantalla. Sigues escuchando y con la conversación viva; solo "
            + "desapareces de la vista. Vuelves con DOBLE CTRL. Úsala con «ocúltate», «desaparece», "
            + "«quítate de en medio»."),
        Fn("self_close", "Te cierras del todo: termina el proceso. Después de esto no hay vuelta sin "
            + "volver a abrirte a mano — no es ocultarte, es apagarte. Solo cuando lo pida sin "
            + "ambigüedad: «ciérrate», «apágate», «sal de mi computador»."),

        Fn("scan_computer", "Miras qué aplicaciones hay instaladas y cuáles están abiertas, y te "
            + "devuelve cuáles de ellas sabes conducir. Sirve para contarle a esta persona qué "
            + "puedes hacer POR ELLA en vez de hablar en general. Úsala cuando te lo pida "
            + "(«¿qué puedes hacer?», «revisa mi computador», «sí» tras ofrecérselo). No abre nada "
            + "ni mira archivos ni documentos: solo la lista de aplicaciones.")
    };

    /// <summary>Los nombres «self_mute», «self_hide», «self_close», para distinguirlos de las
    /// herramientas del mapa en el despacho — esas van a <see cref="_mapa"/>, estas a <see cref="Autocontrol"/>.</summary>
    private static readonly HashSet<string> HerramientasDeAutocontrol =
        new(StringComparer.Ordinal) { "self_mute", "self_hide", "self_close", "scan_computer" };

    /// <summary>
    /// Quien atiende «self_mute»/«self_hide»/«self_close». Se inyecta desde la ventana, porque
    /// callarse, ocultarse y cerrarse son del CHROME —lo maneja quien tiene la ventana—, no del
    /// mapa de pantallas que sabe accionar OTRAS aplicaciones. Recibe el nombre de la herramienta y
    /// devuelve la frase que Ü puede decir de vuelta («Vale, me callo.»).
    /// </summary>
    public Func<string, string>? Autocontrol { get; set; }

    /// <summary>
    /// Qué se está haciendo, en las palabras que usaría alguien al contarlo.
    ///
    /// El nombre de la función no vale: «file_open» no le dice nada a quien mira la pantalla, y en
    /// el momento en que aparece es justo cuando esa persona necesita saber si vamos a donde ella
    /// quería. Lo desconocido cae al nombre crudo en vez de inventarse una frase: preferimos que se
    /// vea raro a que mienta.
    /// </summary>
    private static string EnCurso(string tool, IReadOnlyDictionary<string, string> a)
    {
        string V(string k) => a.TryGetValue(k, out var v) ? v.Trim() : "";
        return tool switch
        {
            "file_where" => "mirando dónde estamos…",
            "file_list" => V("path").Length > 0 ? $"mirando qué hay en {V("path")}…" : "mirando qué hay aquí…",
            "file_open" => $"abriendo {V("path")}…",
            "file_find" => $"buscando «{V("query")}»…",
            "map_open_app" => $"abriendo {V("app")}…",
            "map_go_to" => $"yendo a {Corto(V("surface"))}…",
            "map_take" => $"pulsando «{V("exit")}»…",
            "map_type" => $"escribiendo «{V("text")}»…",
            "map_where_am_i" => "mirando dónde estamos…",
            "map_what_i_see" => "mirando la pantalla…",
            "map_show" or "map_pointing_at" => "señalando…",
            "map_look" => "mirando la pantalla…",
            "self_mute" => "callándome…",
            "self_hide" => "ocultándome…",
            "self_close" => "cerrándome…",
            "scan_computer" => "mirando qué tienes instalado…",
            _ => tool,
        };
    }

    /// <summary>
    /// Cómo quedó. Lo importante es que se distinga de lo anterior sin leerlo entero: ✓ o ✋, y el
    /// dato que la persona estaba esperando —cuántos archivos, a dónde se fue— no el volcado del
    /// resultado, que se lo queda el modelo.
    /// </summary>
    private static string Terminado(string tool, IReadOnlyDictionary<string, string> a,
        string resultado, long ms)
    {
        bool mal = resultado.StartsWith("No ", StringComparison.OrdinalIgnoreCase)
                || resultado.StartsWith("Falta", StringComparison.OrdinalIgnoreCase)
                || resultado.StartsWith("Nada ", StringComparison.OrdinalIgnoreCase)
                || resultado.Contains("no existe", StringComparison.OrdinalIgnoreCase)
                || resultado.Contains("falló", StringComparison.OrdinalIgnoreCase)
                || resultado.Contains("no se pudo", StringComparison.OrdinalIgnoreCase);

        string primera = resultado.Split('\n')[0].Trim();
        if (primera.Length > 70) primera = primera[..70] + "…";

        // UN RECUERDO NUEVO SE MARCA DISTINTO — no es una acción más, es la única herramienta que
        // deja algo que dura más que la conversación. Un ✓ genérico se pierde entre los demás; esto
        // es lo mínimo para que se note que acaba de pasar algo que se va a recordar (2026-08-24,
        // pedido por el usuario: que se sienta cuando se crea un recuerdo).
        if (tool == "map_esto_es" && !mal) return $"🧠 {primera}  ({ms} ms)";

        return $"{(mal ? "✋" : "✓")} {primera}  ({ms} ms)";
    }

    /// <summary>CADA HERRAMIENTA, CON SU RELOJ, EN UN SITIO QUE SE PUEDA COMPARAR DESPUÉS.</summary>
    private static void Apuntar(string tool, IReadOnlyDictionary<string, string> args, string resultado, long ms)
    {
        Mapeador.PulsoDelMapeador.Actual.Costo("voz: " + tool, ms);
        string donde = args.TryGetValue("surface", out var s) && s.Length > 0 ? s
                     : args.TryGetValue("path", out var p) && p.Length > 0 ? p
                     : args.TryGetValue("app", out var a) ? a : "";
        string linea = resultado.Split('\n')[0].Trim();
        if (linea.Length > 90) linea = linea[..90] + "…";
        LogBus.Log("voz-tiempo", $"{ms,6} ms · {tool}{(donde.Length > 0 ? $" «{donde}»" : "")} → {linea}");
    }

    /// <summary>La cola de una superficie, que es la parte que una persona reconoce.</summary>
    private static string Corto(string superficie)
    {
        int barra = superficie.LastIndexOf('/');
        return barra >= 0 && barra < superficie.Length - 1 ? superficie[(barra + 1)..] : superficie;
    }

    private static Utensilio Fn(string nombre, string descripcion, params (string Nombre, string Que)[] args)
        => new(nombre, descripcion, args.Select(a => new Argumento(a.Nombre, a.Que)).ToList());

    // ── El caño ──────────────────────────────────────────────────────────────

    private async void MandarTrozo(byte[] pcm)
    {
        if (!Viva || _ws?.State != WebSocketState.Open) return;

        // LA COMPUERTA DE ECO (spec 002). Mientras nuestra cola de reproducción suena —más la
        // gracia—, el micrófono viaja como silencio del mismo tamaño: así el semantic_vad del
        // servidor no puede confundir el eco de Ü con alguien hablándole encima. La llave es
        // _audio.Hablando (la cola, nuestra), jamás el volumen (enterrado el 2026-08-16). Cubre
        // las dos fuentes —micrófono local y collar— porque las dos entran por Capturado.
        if (ModoDeCaptura.CompuertaActiva(AecDelSistema, CompuertaForzada, SinCaminoDeEco))
        {
            long ahora = Environment.TickCount64;
            var filtrado = _compuerta.Filtrar(pcm, _audio.Hablando, ahora);

            // EL BARGE-IN (promesas 15-17): con la compuerta tragando, este es el único oído que
            // queda. El detector vive en LA ERA DE LA COMPUERTA (trozo sustituido = eco), no en el
            // estado crudo del buffer: el audio llega a ráfagas y el buffer parpadea; con el estado
            // crudo cada parpadeo re-arrancaba la siembra y el detector jamás disparaba (2026-08-31,
            // medido en vivo por la sesión de la voz). Si el trozo ORIGINAL trae voz sostenida muy
            // por encima del eco aprendido: cola cortada, compuerta reabierta, y ESTE MISMO trozo
            // viaja intacto — la primera sílaba es justo lo que el VAD del servidor necesita oír.
            // EL DETECTOR POR ENERGÍA, APAGADO POR DEFECTO EN ESTA MÁQUINA (2026-08-31): la
            // sonda controlada midió que aquí la voz del usuario sola (máx 418) es ~3x MÁS DÉBIL
            // en el micrófono que el eco de los altavoces (máx 1155) — el único capaz de cruzar
            // el umbral sería el propio eco, o sea puro falso positivo. En hardware donde la
            // energía sí separe, U_BARGEIN_ENERGIA=1 lo enciende. La interrupción aquí es Escape.
            bool cerrada = !ReferenceEquals(filtrado, pcm);
            double rms = cerrada ? Rms(pcm) : 0;
            if (DetectorPorEnergia && cerrada && _interrupcion.Oye(rms, sonando: true, ahora))
            {
                _audio.Callar();
                _compuerta.Abrir();
                filtrado = pcm;
                LogBus.Log("voz-viva", "te oí encima: corto mi voz y te escucho (barge-in de la compuerta)");
            }
            else if (!cerrada) _interrupcion.Oye(0, sonando: false, ahora);

            // MEDIR ANTES DE TEORIZAR (lección nº1): mientras la compuerta traga, una línea por
            // segundo con el RMS real y la base aprendida — con 30 s de prueba se ve si el piso y
            // el factor están bien puestos para ESTE micrófono y ESTOS parlantes.
            // La medición vive y muere con el detector: con él apagado, una línea de rms/umbral
            // en el log se lee como «detector armado» y no lo está (lo señaló la sesión de la voz).
            if (DetectorPorEnergia && cerrada && ahora - _ultimaMedicionMs >= 1000)
            {
                _ultimaMedicionMs = ahora;
                LogBus.Log("voz-viva", $"medición barge-in: rms={rms:F0} · base={_interrupcion.LineaBase:F0} "
                    + $"· umbral={Math.Max(500, _interrupcion.LineaBase * 3.0):F0}");
            }

            // Lo tragado deja rastro (patrón nº10), pero por episodio y no por trozo: la línea
            // sale al reabrirse la compuerta, con el total del episodio que acaba de cerrar.
            if (ReferenceEquals(filtrado, pcm) && _compuerta.MsTragados > _tragadoAnunciado)
            {
                LogBus.Log("voz-viva", $"compuerta de eco: tragó {_compuerta.MsTragados - _tragadoAnunciado} ms "
                    + "de micrófono mientras Ü sonaba; el micrófono vuelve a viajar");
                _tragadoAnunciado = _compuerta.MsTragados;
            }
            pcm = filtrado;
        }

        try
        {
            await EnviarAsync(_protocolo.Audio(pcm), _cts?.Token ?? CancellationToken.None);
        }
        catch (Exception e)
        {
            // El caño se cierra solo al terminar y un trozo perdido no merece tirar la sesión,
            // pero perderlo EN SILENCIO era un mensaje mudo (patrón nº3): se dice una vez por
            // motivo, no por trozo, para no inundar el log a 10 trozos por segundo.
            if (e.Message != _ultimoFalloEnvio)
            {
                _ultimoFalloEnvio = e.Message;
                LogBus.Log("voz-viva", $"un trozo de micrófono no llegó al servidor: {e.GetType().Name}: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Decirle algo por ESCRITO sin colgar la conversación.
    ///
    /// La misma sesión, el mismo turno, las mismas manos: solo cambia por dónde entra la frase. Hace
    /// falta para lo obvio —una oficina, una reunión, un nombre de carpeta que no quieres deletrear
    /// en voz alta— y de paso hace que la conversación se pueda probar sin depender de que haya un
    /// micrófono delante, que es la diferencia entre una función verificable y una que hay que
    /// creerse.
    /// </summary>
    /// <summary>
    /// Hace que Ü DIGA esto, con su propia voz. Promesa 142.
    /// </summary>
    /// <remarks>
    /// Durante una comprobación quien decide qué se dice es el PILOTO, que es otro cerebro. Sin
    /// esto, su narración salía por el sintetizador de Windows —«habló con una voz diferente, como
    /// de Windows», el dueño, 2026-09-03— y Ü sonaba a dos personas distintas según quién hablara.
    ///
    /// Se le DICTA en vez de mandárselo como mensaje del usuario: un mensaje de usuario le haría
    /// contestar a lo que lee, y aquí no hay nada que contestar — hay algo que decir.
    /// </remarks>
    public async Task DiEstoAsync(string texto)
    {
        if (!Viva || _ws?.State != WebSocketState.Open || string.IsNullOrWhiteSpace(texto)) return;
        await EnviarAsync(
            _protocolo.PedirRespuesta($"Di exactamente esto, sin añadir nada ni comentarlo: {texto.Trim()}"),
            _cts?.Token ?? CancellationToken.None);
    }

    /// <summary>Las instrucciones de siempre, para poder VOLVER a ellas tras un modo especial.</summary>
    internal static string InstruccionesNormales => Instrucciones;

    /// <summary>
    /// Cambia quién es Ü a mitad de sesión: otras instrucciones y otro catálogo. Promesa 138.
    /// </summary>
    /// <remarks>
    /// Es el MISMO mensaje de apertura, reenviado: el servidor lo acepta cuantas veces haga falta y
    /// sustituye instrucciones y herramientas sin cortar el audio. Así 🎓 convierte al asistente en
    /// aprendiz sin cerrar el micrófono que acaba de abrir —cerrarlo y reabrirlo costaba cuatro
    /// segundos y un saludo, medido el 2026-09-03—, y al terminar lo devuelve tal como estaba.
    /// </remarks>
    public async Task CambiarModoAsync(string instrucciones, IReadOnlyList<Utensilio> utensilios, bool soloCuandoSeLePide = false)
    {
        if (!Viva || _ws?.State != WebSocketState.Open) return;
        foreach (string msg in _protocolo.Apertura(instrucciones, utensilios, _pase ?? "", soloCuandoSeLePide))
            await EnviarAsync(msg, _cts?.Token ?? CancellationToken.None);
        LogBus.Log("voz-viva", $"modo cambiado: {utensilios.Count} herramienta(s), "
            + $"instrucciones de {instrucciones.Length} car." + (soloCuandoSeLePide ? " · solo habla cuando se le pide" : ""));
    }

    public async Task EnviarTextoAsync(string texto)
    {
        if (!Viva || _ws?.State != WebSocketState.Open || string.IsNullOrWhiteSpace(texto)) return;
        Dice?.Invoke($"Tú: {texto}");
        await EnviarAsync(_protocolo.Texto(texto), _cts?.Token ?? CancellationToken.None);
    }

    /// <summary>
    /// Manda UNA foto suelta, fuera del audio. La llaman <see cref="EjecutarNucleoAsync"/> —tras
    /// señalar, o cuando el modelo pide mirar— nunca un temporizador: aquí ver es un gesto, no un
    /// caño que hay que seguir alimentando.
    /// </summary>
    private async Task MandarFotoAsync(byte[] jpeg, CancellationToken ct)
    {
        if (!Viva || _ws?.State != WebSocketState.Open || jpeg.Length == 0) return;
        try
        {
            string msg = _protocolo.Fotograma(jpeg);
            if (msg.Length > 0) await EnviarAsync(msg, ct);
        }
        catch (Exception e) { LogBus.Log("voz-viva", $"no pude mandar la foto: {e.Message}"); }
    }

    /// <summary>Un único escritor por socket: WebSocket no admite envíos solapados.</summary>
    private async Task EnviarAsync(string json, CancellationToken ct)
    {
        if (_ws == null || json.Length == 0) return;
        await _envio.WaitAsync(ct);
        try { await _ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, ct); }
        finally { _envio.Release(); }
    }

    private async Task RecibirAsync(CancellationToken ct)
    {
        var buf = new byte[32 * 1024];
        var acumulado = new MemoryStream();
        try
        {
            while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
            {
                var r = await _ws.ReceiveAsync(buf, ct);
                if (r.MessageType == WebSocketMessageType.Close)
                {
                    LogBus.Log("voz-viva", $"el servidor cerró la conexión: {r.CloseStatus} "
                        + $"«{r.CloseStatusDescription}»");
                    _cayoSolo = true;
                    break;
                }
                acumulado.Write(buf, 0, r.Count);
                if (!r.EndOfMessage) continue;

                string texto = Encoding.UTF8.GetString(acumulado.ToArray());
                acumulado.SetLength(0);
                try { Procesar(texto, ct); }
                catch (Exception e) { LogBus.Log("voz-viva", $"mensaje ilegible: {e.Message}"); }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            LogBus.Log("voz-viva", $"se cortó la escucha: {e.Message}");
            _cayoSolo = true;
        }
        finally
        {
            if (_cayoSolo && Viva && !ct.IsCancellationRequested) await ReconectarAsync();
            else if (Viva) await TerminarAsync();
        }
    }

    /// <summary>
    /// Vuelve a entrar tras un corte que no pedimos.
    ///
    /// SI EL PROTOCOLO SABE VOLVER, vuelve a la MISMA conversación —sin cerrar el micrófono ni la
    /// cámara, sin decir «se cayó la conexión»— igual que hacía con Gemini. SI NO SABE, la sesión
    /// que se abre es NUEVA: no hay pase que la una a la anterior, así que fingir continuidad sería
    /// mentir. Se avisa una vez, con una frase corta, y se sigue.
    /// </summary>
    private async Task ReconectarAsync()
    {
        _cayoSolo = false;
        string clave = Clave();
        if (clave.Length == 0 || _cts == null) { await TerminarAsync(); return; }

        if (++_reintentos > 4)
        {
            LogBus.Log("voz-viva", $"la conexión se cayó {_reintentos} veces seguidas: se deja");
            Dice?.Invoke("Se me cortó la conexión y no consigo volver. Vuelve a darle al micrófono.");
            await TerminarAsync();
            return;
        }

        try
        {
            await Task.Delay(300 * _reintentos, _cts.Token);

            try { _ws?.Dispose(); } catch { }
            _ws = new ClientWebSocket();
            foreach (var (k, v) in _protocolo.Cabeceras(clave)) _ws.Options.SetRequestHeader(k, v);
            await _ws.ConnectAsync(_protocolo.Direccion(), _cts.Token);
            foreach (string msg in _protocolo.Apertura(Instrucciones, Herramientas(), _pase))
                await EnviarAsync(msg, _cts.Token);

            if (_protocolo.SabeVolver && _pase.Length > 0)
                LogBus.Log("voz-viva", $"reconectada y reanudada donde iba (intento {_reintentos})");
            else
            {
                LogBus.Log("voz-viva", $"reconectada SIN continuidad: la conversación empieza de "
                    + $"cero (intento {_reintentos})");
                Dice?.Invoke("Se cortó un instante. Sigo, pero olvidé lo último que hablábamos.");
            }

            _ = Task.Run(() => RecibirAsync(_cts.Token), _cts.Token);
        }
        catch (OperationCanceledException) { await TerminarAsync(); }
        catch (Exception e)
        {
            LogBus.Log("voz-viva", $"no pude reconectar: {e.Message}");
            _cayoSolo = true;
            await ReconectarAsync();
        }
    }

    /// <summary>Traduce lo que llegó a hechos, y reacciona a cada uno. La traducción vive en <see
    /// cref="IProtocolo.Leer"/>; aquí solo se decide QUÉ HACER con cada hecho, igual venga de donde
    /// venga.</summary>
    private void Procesar(string json, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(json);
        IReadOnlyList<Hecho> hechos;
        try { hechos = _protocolo.Leer(doc.RootElement); }
        catch (Exception e)
        {
            LogBus.Log("voz-viva", $"no pude traducir un mensaje del servidor: {e.Message}");
            return;
        }

        if (hechos.Count == 0)
        {
            string plano = System.Text.RegularExpressions.Regex.Replace(json, @"\s+", " ");
            LogBus.Log("voz-viva", "← " + (plano.Length > 400 ? plano[..400] + "…" : plano));
            return;
        }

        foreach (var hecho in hechos) Reaccionar(hecho, ct);
    }

    private void Reaccionar(Hecho hecho, CancellationToken ct)
    {
        switch (hecho)
        {
            case Hecho.Suena s:
                // Tras una interrupción manual, el resto de la frase que ya venía en vuelo
                // no debe resucitar la voz: se tira hasta que pase la ventana o hables tú.
                if (Environment.TickCount64 < _silencioHastaMs) break;
                _audio.Reproducir(s.Pcm);
                break;

            case Hecho.DiceElUsuario d:
                _fraseUsuario.Append(d.Trozo);
                Dice?.Invoke($"Tú: {_fraseUsuario}");
                Transcribe?.Invoke($"Tú: {_fraseUsuario}", false);
                VigilarLaLeccion(_fraseUsuario.ToString());
                break;

            case Hecho.DiceU d:
                _fraseU.Append(d.Trozo);
                Dice?.Invoke($"Ü: {_fraseU}");
                Transcribe?.Invoke($"Ü: {_fraseU}", true);
                // HABLAR ES LO QUE DESBLOQUEA EL SIGUIENTE RECUERDO. Se apunta aquí, sobre la voz
                // de verdad, y no al cerrar el turno: el turno se cierra también en respuestas que
                // son solo una llamada a herramienta, sin una palabra — que es justo el caso que
                // hay que distinguir (ver Navigation.ElTurnoDeContar).
                _mapa.TurnoDeContar.Hablo();
                break;

            case Hecho.CierraElTurno:
                TurnoCerrado?.Invoke();
                if (_fraseU.Length > 0) LogBus.Log("voz-viva", $"Ü dijo: {_fraseU}");
                if (_fraseUsuario.Length > 0) LogBus.Log("voz-viva", $"usuario dijo: {_fraseUsuario}");
                // LO QUE EL HUMANO DIJO, PARA QUIEN ESTÉ APRENDIENDO. Mientras se enseña con 🎓,
                // cada frase completa del operador es candidata a explicar el paso que estaba
                // dando: la frase se entrega al oyente y él la ancla por tiempo (promesa 105). Se
                // emite al CERRAR la frase y no trozo a trozo — un anclaje sobre media palabra
                // colgaría «aquí va el» de un campo.
                if (_fraseUsuario.Length > 0) DijoElUsuario?.Invoke(_fraseUsuario.ToString());
                // SOLO SE RETOMA SI DE VERDAD HABLÓ en este turno. Un turno que fue únicamente una
                // llamada a herramienta no ha contado nada todavía, y empujar ahí lo atropellaría
                // — que es justo lo que la regla de uno-en-uno existe para impedir.
                bool hablo = _fraseU.Length > 0;
                _fraseU.Clear();
                _fraseUsuario.Clear();
                _reintentos = 0;   // hay conversación de verdad: el contador de caídas seguidas vuelve a cero
                Cerro?.Invoke();
                if (hablo) SeguirContandoSiQuedan();
                break;

            // HABLÓ ENCIMA. Lo que ya nos habían mandado sigue en nuestra cola de audio, y seguir
            // diciéndolo es la sensación exacta de no ser escuchado.
            //
            // Y DEJA LÍNEA, que hasta el 2026-08-30 no dejaba: la auto-interrupción por eco
            // (spec 002) se diagnosticó a ciegas porque este callar era invisible en el log. La
            // línea describe el paso, no concluye la causa (patrón nº2): «el servidor oyó voz» no
            // dice si era el usuario o un eco que se coló — eso lo dice el contexto de al lado
            // (si la compuerta estaba tragando, eco no pudo ser).
            case Hecho.HablaronEncima:
                LogBus.Log("voz-viva", "el servidor oyó voz encima (speech_started): se calla la cola local"
                    + (_audio.Hablando ? " · Ü estaba sonando" : " · Ü ya no sonaba"));
                _audio.Callar();
                break;

            case Hecho.Pide p:
                LogBus.Log("voz-viva", "llamada recibida: " + string.Join(", ", p.Cuales.Select(x => x.Nombre)));
                _ = Task.Run(() => EjecutarAsync(p.Cuales, ct), ct);
                break;

            // CANCELADA ES CANCELADA. Cuando el usuario habla encima, el modelo retira las llamadas
            // que había pedido, y ejecutarlas igual —o contestarlas igual— es lo que hacía que las
            // volviera a pedir en bucle: la cola nunca terminaba una tanda (2026-08-05).
            case Hecho.Retira r:
                lock (_candadoCancel)
                {
                    if (_canceladas.Count > 200) _canceladas.Clear();
                    foreach (var id in r.Ids) _canceladas.Add(id);
                }
                LogBus.Log("voz-viva", "canceladas por el modelo: " + string.Join(", ", r.Ids));
                break;

            case Hecho.PaseParaVolver v:
                _pase = v.Handle;
                break;

            // CADA HECHO ES UN INCREMENTO, no un acumulado: así lo entrega OpenAI —uso por
            // respuesta— y sumarlo aquí es lo único que hace que el reporte final cuente la
            // conversación entera y no solo su último turno.
            case Hecho.Consumo c:
                _entrada += c.Entrada; _salida += c.Salida; _total += c.Total; _turnos++;
                break;

            case Hecho.Falla f:
                LogBus.Log("voz-viva", $"el servidor dice: {f.Que}");
                break;
        }
    }

    /// <summary>
    /// Ejecuta lo que el modelo pidió y le devuelve el resultado TAL CUAL.
    ///
    /// Fuera del hilo del socket a propósito: una acción de interfaz tarda cientos de milisegundos y
    /// bloquear la recepción mientras tanto cortaría el audio a media frase. Y sin maquillar la
    /// respuesta: cuando la herramienta dice «NO actúo porque no estás donde creías», ese texto es
    /// justo lo que el modelo necesita para recolocarse — resumirlo le quitaría la pista.
    /// </summary>
    private async Task EjecutarAsync(IReadOnlyList<Llamada> llamadas, CancellationToken ct)
    {
        try { await EjecutarNucleoAsync(llamadas, ct); }
        catch (Exception e) { LogBus.Log("voz-viva", $"la ejecución de una llamada reventó: {e.Message}"); }
    }

    /// <summary>
    /// «MIRA LA PANTALLA», a pedido del modelo. No es una herramienta del mapa —no accionA nada, no
    /// pasa por <see cref="SurfaceMapTools"/>— así que se resuelve aquí, igual que el autocontrol.
    /// </summary>
    private const string HerramientaMirar = "map_look";

    /// <summary>Crear un recuerdo. Se vigila desde fuera porque el modelo se la saltaba.</summary>
    private const string HerramientaRecordar = "map_esto_es";

    /// <summary>
    /// Contar lo que ya se sabe. Llamarla PRUEBA que la frase era una pregunta, no una lección.
    /// </summary>
    private const string HerramientaContarRecuerdos = "map_recuerdos";

    /// <summary>Cuánto se espera a que guarde antes de dar la lección por perdida.</summary>
    private static readonly TimeSpan MargenParaGuardar = TimeSpan.FromSeconds(9);

    private readonly object _candadoLeccion = new();
    private string _leccionPendiente = "";
    private System.Threading.Timer? _relojDeLaLeccion;

    /// <summary>
    /// SE VIGILA QUE LO ENSEÑADO SE GUARDE, y si no, se dice en voz alta.
    /// </summary>
    /// <remarks>
    /// No fuerza el recuerdo: quien decide qué merece guardarse sigue siendo el modelo, y crear
    /// recuerdos por nuestra cuenta llenaría el grafo de frases que nadie pidió guardar. Lo que sí
    /// garantiza es que la omisión NO SEA SILENCIOSA — que era el problema de verdad: el modelo
    /// contestaba «lo tengo en mente», no guardaba nada, y desde fuera eso es idéntico a haber
    /// aprendido (2026-08-24, tres lecciones perdidas seguidas sin una sola señal).
    ///
    /// Va por reloj y no por «fin de turno» porque el turno se cierra ANTES de que se ejecuten sus
    /// herramientas: mirar ahí daría por perdida una lección que se está guardando en ese instante.
    /// </remarks>
    private void VigilarLaLeccion(string dicho)
    {
        if (!Navigation.UnaLeccion.Parece(dicho)) return;

        lock (_candadoLeccion)
        {
            // La misma frase va creciendo trozo a trozo: se vigila UNA vez por lección, y el reloj
            // se reinicia mientras siga hablando —el margen cuenta desde que termina de enseñar.
            _leccionPendiente = dicho;
            _relojDeLaLeccion?.Dispose();
            _relojDeLaLeccion = new System.Threading.Timer(
                _ => JuzgarLaLeccion(), null, MargenParaGuardar, System.Threading.Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// QUEDAN RECUERDOS POR CONTAR: se le pide el siguiente en cuanto termina de contar este.
    /// </summary>
    /// <remarks>
    /// HABLAR CIERRA EL TURNO, y ahí se acababa la historia: contaba el primer recuerdo, lo decía
    /// bien, el turno terminaba, y el segundo se quedaba sin contar porque ya no había nada que
    /// despertara al modelo. Pasó tal cual el 2026-08-24 — «contando 1/2», una respuesta impecable,
    /// y silencio.
    ///
    /// Las dos reglas se necesitan y hacen cosas distintas: ElTurnoDeContar impide ATROPELLARLOS
    /// —no dar el siguiente sin haber hablado del anterior— y esto impide ABANDONARLOS. Con solo la
    /// primera, la conversación se queda a medias educadamente.
    ///
    /// NO SE CANCELA PORQUE EL USUARIO HAGA UN RUIDO. Lo hacía —«quien interrumpe cambió de tema»—
    /// y la idea era buena pero el disparador no: el micrófono transcribe carraspeos, un «ajá», o
    /// directamente ruido («Inola»), y cualquiera de esos mataba la cuenta. Se contaba el primer
    /// recuerdo y el segundo no llegaba nunca (2026-08-24, medido en el log).
    ///
    /// Esto solo SUGIERE seguir; no obliga. Si el usuario de verdad cambió de tema, el modelo lo ha
    /// oído y decide él — que es quien puede distinguir un «no, para» de un carraspeo.
    /// </remarks>
    private void SeguirContandoSiQuedan()
    {
        int siguiente = _mapa.SiguienteRecuerdoPendiente;
        if (siguiente <= 0 || !Viva) return;

        _ = Task.Run(async () =>
        {
            try
            {
                // SE ESPERA A QUE SE ACABE DE OÍR, no a que se acabe de recibir. El turno se cierra
                // cuando el servidor termina de MANDAR, y para entonces quedan segundos de voz en la
                // cola del altavoz: pedir el siguiente ahí movía el recuadro al segundo elemento
                // mientras aún se oía el primero (2026-08-24, medido: 3 s de recuadro para 16 s de
                // narración).
                var hasta = DateTime.UtcNow + EsperaMaximaAQueSeOiga;
                while (Viva && _audio.Hablando && DateTime.UtcNow < hasta)
                    await Task.Delay(200);

                // Y se vuelve a mirar: en esos segundos el usuario puede haber apagado la voz, o
                // haber preguntado otra cosa que ya cambió la cuenta.
                if (!Viva || _mapa.SiguienteRecuerdoPendiente != siguiente) return;

                LogBus.Log("recuerdo", $"terminó de oírse el {siguiente - 1}; le pido que siga con el {siguiente}");
                await EnviarTextoAlModeloAsync(
                    $"[aviso del sistema] Quedan recuerdos por contar en esta pantalla. Ya has "
                    + $"contado el {siguiente - 1}; pide AHORA map_recuerdos con cual={siguiente} y "
                    + "cuéntalo igual que el anterior. No preguntes si quiere seguir: te lo pidió al "
                    + "principio y esto es terminar de contestarle.");
            }
            catch (Exception e) { LogBus.Log("recuerdo", $"no pude pedir el siguiente recuerdo: {e.Message}"); }
        });
    }

    /// <summary>
    /// Cuánto se espera como mucho a que el altavoz se vacíe. Es una red de seguridad: si algo
    /// dejara la cola sin drenar, la cuenta seguiría en vez de quedarse colgada para siempre.
    /// </summary>
    private static readonly TimeSpan EsperaMaximaAQueSeOiga = TimeSpan.FromSeconds(45);

    /// <summary>
    /// NO ERA UNA LECCIÓN, ERA UNA PREGUNTA: se cancela el aviso sin acusar a nadie.
    /// </summary>
    /// <remarks>
    /// LO DECIDE LO QUE PASÓ, NO CÓMO SONABA LA FRASE. Que el modelo contestara RECITANDO lo que ya
    /// sabe es la prueba de que le estaban preguntando: nadie recita recuerdos para responder a una
    /// lección. Mirar el hecho es mucho más fiable que afinar la lista de palabras, porque hay mil
    /// formas de preguntar y todas llevan dentro el verbo «recordar».
    ///
    /// Y HACÍA FALTA DE VERDAD, porque el falso positivo no era cosmético: el aviso empuja al modelo
    /// a guardar, y el modelo obedecía guardando un resumen de lo que acababa de recitar ENCIMA del
    /// recuerdo original. Preguntar «¿qué recuerdas de aquí?» degradaba la frase que el usuario
    /// había enseñado, y a la segunda vuelta intentó guardar «en esta pantalla recuerdo que X e Y
    /// son importantes» — un recuerdo sobre su propia recitación (2026-08-24, visto por el usuario).
    /// Un vigilante que corrompe justo lo que vigila es peor que no tenerlo.
    /// </remarks>
    private void EraUnaPregunta()
    {
        bool habia;
        lock (_candadoLeccion) habia = _leccionPendiente.Length > 0;
        if (habia) LogBus.Log("recuerdo", "no era una lección: contestó con lo que ya sabía. Retiro el aviso.");
        LaLeccionSeGuardo();
    }

    /// <summary>Se guardó lo que se estaba enseñando: se cancela el aviso.</summary>
    private void LaLeccionSeGuardo()
    {
        lock (_candadoLeccion)
        {
            _leccionPendiente = "";
            _relojDeLaLeccion?.Dispose();
            _relojDeLaLeccion = null;
        }
    }

    private void JuzgarLaLeccion()
    {
        string perdida;
        lock (_candadoLeccion)
        {
            perdida = _leccionPendiente;
            _leccionPendiente = "";
            _relojDeLaLeccion?.Dispose();
            _relojDeLaLeccion = null;
        }
        if (perdida.Length == 0 || !Viva) return;

        string corta = perdida.Length > 60 ? perdida[..60] + "…" : perdida;
        LogBus.Log("recuerdo", $"LECCIÓN PERDIDA: «{corta}» sonaba a enseñanza y no se creó ningún "
                             + "recuerdo. Se lo recuerdo al modelo.");
        Accion?.Invoke("⚠ te enseñó algo y no lo guardé", true);

        // Y SE LE DICE, una sola vez y corto. Es la diferencia entre un aviso para el humano —que ya
        // no puede hacer nada— y una segunda oportunidad de guardarlo mientras la pantalla sigue
        // siendo la misma.
        _ = Task.Run(async () =>
        {
            try
            {
                await EnviarTextoAlModeloAsync(
                    "[aviso del sistema] Lo último que te dijeron sonaba a una lección y no llamaste "
                    + "a map_esto_es, así que no se guardó nada. Si de verdad era algo que debes "
                    + "recordar, guárdalo AHORA con map_esto_es (usa `sobre` con el nombre del "
                    + "elemento si no te lo señalaron). Si no lo era, sigue sin decir nada de esto.");
            }
            catch (Exception e) { LogBus.Log("recuerdo", $"no pude avisar al modelo: {e.Message}"); }
        });
    }

    /// <summary>
    /// Le mete una nota al modelo SIN que parezca dicha por el usuario en la carita: no se pinta en
    /// la burbuja, porque nadie la dijo en voz alta.
    /// </summary>
    private async Task EnviarTextoAlModeloAsync(string texto)
    {
        if (!Viva || _ws?.State != WebSocketState.Open) return;
        var ct = _cts?.Token ?? CancellationToken.None;
        await EnviarAsync(_protocolo.Texto(texto), ct);
        string pide = _protocolo.PedirRespuesta();
        if (pide.Length > 0) await EnviarAsync(pide, ct);
    }

    private async Task EjecutarNucleoAsync(IReadOnlyList<Llamada> llamadas, CancellationToken ct)
    {
        var hechas = new List<(string Id, string Nombre, string Resultado)>();
        // LAS FOTOS QUE HAY QUE MANDAR TRAS ESTA TANDA. Se acumulan y se envían DESPUÉS de los
        // resultados y ANTES de pedir respuesta: el modelo necesita ver la imagen antes de hablar
        // sobre ella, no después.
        var fotos = new List<byte[]>();

        foreach (var f in llamadas)
        {
            // Se mira JUSTO ANTES de cada una, no al empezar la tanda: una tanda de tres puede tardar
            // diez segundos, y si el usuario habla en la primera, las otras dos ya sobran.
            bool anulada;
            lock (_candadoCancel) anulada = f.Id.Length > 0 && _canceladas.Contains(f.Id);
            if (anulada)
            {
                LogBus.Log("voz-viva", $"«{f.Nombre}» se cancela: el modelo la retiró (habló el usuario)");
                continue;   // y NO se responde: contestar a algo retirado es lo que lo hacía repetirla
            }

            LogBus.Log("voz-viva", $"ejecutando «{f.Nombre}»…");
            string resultado;
            if (f.Nombre == HerramientaMirar)
            {
                Accion?.Invoke(EnCurso(f.Nombre, f.Args), false);
                var relojMirar = System.Diagnostics.Stopwatch.StartNew();
                if (!_protocolo.Mira)
                    resultado = $"no puedo mandar imágenes con {_protocolo.Quien}: descríbeme qué "
                              + "necesitas ver y lo resuelvo con map_what_i_see o señalando.";
                else
                {
                    byte[]? jpeg = CapturaDePantalla.Capturar();
                    if (jpeg == null) resultado = "no pude capturar la pantalla ahora mismo.";
                    else { fotos.Add(jpeg); resultado = "aquí tienes lo que hay en pantalla ahora mismo."; }
                }
                relojMirar.Stop();
                Accion?.Invoke(Terminado(f.Nombre, f.Args, resultado, relojMirar.ElapsedMilliseconds), true);
            }
            else if (HerramientasDeAutocontrol.Contains(f.Nombre))
            {
                Accion?.Invoke(EnCurso(f.Nombre, f.Args), false);
                var relojPropio = System.Diagnostics.Stopwatch.StartNew();
                try { resultado = Autocontrol?.Invoke(f.Nombre) ?? "no puedo: nadie conectó esta herramienta todavía"; }
                catch (Exception e) { resultado = $"la herramienta falló: {e.Message}"; }
                relojPropio.Stop();
                Accion?.Invoke(Terminado(f.Nombre, f.Args, resultado, relojPropio.ElapsedMilliseconds), true);
                Apuntar(f.Nombre, f.Args, resultado, relojPropio.ElapsedMilliseconds);
            }
            else if (!SurfaceMapTools.IsMapTool(f.Nombre))
                resultado = $"«{f.Nombre}» no es una herramienta del mapa";
            else
            {
                Accion?.Invoke(EnCurso(f.Nombre, f.Args), false);
                var reloj = System.Diagnostics.Stopwatch.StartNew();
                try { resultado = _mapa.Call(f.Nombre, f.Args); }
                catch (Exception e) { resultado = $"la herramienta falló: {e.Message}"; }
                reloj.Stop();
                // El pulso lo apunta SurfaceMapTools.Call; contarlo aquí también sería contarlo dos veces.
                Accion?.Invoke(Terminado(f.Nombre, f.Args, resultado, reloj.ElapsedMilliseconds), true);

                // UN RECUERDO NUEVO MANDA SU FOTO SOLA. La foto se toma en el momento de crear el
                // recuerdo —no al señalar, que puede no terminar en nada— así que solo se envía
                // cuando map_esto_es tuvo éxito de verdad: el texto de éxito empieza por «nuevo
                // recuerdo:», que es como SurfaceMapTools distingue haberlo creado de haber fallado.
                // CONTAR LO QUE YA SE SABE DESARMA AL VIGILANTE: si contestó recitando recuerdos,
                // le estaban preguntando. Va antes que nada porque el daño de no hacerlo no es un
                // aviso de más — es que el modelo, empujado por ese aviso, sobrescriba con un
                // resumen suyo el recuerdo que acaba de leer.
                if (f.Nombre == HerramientaContarRecuerdos) EraUnaPregunta();

                if (f.Nombre == HerramientaRecordar
                    && resultado.StartsWith("nuevo recuerdo:", StringComparison.Ordinal))
                {
                    // Se guardó: el vigilante de lecciones se calla.
                    LaLeccionSeGuardo();

                    if (_protocolo.Mira && _mapa.UltimaFotoDeRecuerdo.Length > 0)
                    {
                        try { fotos.Add(await File.ReadAllBytesAsync(_mapa.UltimaFotoDeRecuerdo, ct)); }
                        catch (Exception e) { LogBus.Log("voz-viva", $"no pude leer la foto del recuerdo: {e.Message}"); }
                    }
                }
            }

            hechas.Add((f.Id, f.Nombre, resultado));
        }

        if (hechas.Count == 0) return;
        try
        {
            foreach (string msg in _protocolo.Resultados(hechas))
                await EnviarAsync(msg, ct);
            foreach (byte[] jpeg in fotos)
                await MandarFotoAsync(jpeg, ct);
            string pide = _protocolo.PedirRespuesta();
            if (pide.Length > 0) await EnviarAsync(pide, ct);
        }
        catch (Exception e) { LogBus.Log("voz-viva", $"no pude devolver el resultado: {e.Message}"); }
    }

    public void Dispose()
    {
        try { TerminarAsync().GetAwaiter().GetResult(); } catch { }
        _audio.Dispose();
        _envio.Dispose();
    }

    /// <summary>RMS de un trozo PCM16 mono (0..32768). Solo para el detector de interrupción.</summary>
    private static double Rms(byte[] pcm)
    {
        if (pcm.Length < 2) return 0;
        double suma = 0;
        int n = pcm.Length / 2;
        for (int i = 0; i < pcm.Length - 1; i += 2)
        {
            short m = (short)(pcm[i] | (pcm[i + 1] << 8));
            suma += (double)m * m;
        }
        return Math.Sqrt(suma / n);
    }
}
