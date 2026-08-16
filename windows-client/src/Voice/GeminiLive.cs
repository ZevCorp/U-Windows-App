using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using U.WindowsClient.Diagnostics;
using U.WindowsClient.Mcp;

namespace U.WindowsClient.Voice;

/// <summary>
/// Conversación en vivo con Gemini, con las manos puestas en el grafo.
///
/// La diferencia con <see cref="VoiceIO"/> no es la calidad de la voz: es QUIÉN decide el turno. El
/// dictado de Windows abre el micrófono, espera una frase y se cierra; aquí el caño está abierto en
/// los dos sentidos y el modelo puede interrumpirte, callarse cuando hablas, o pedir una acción a
/// mitad de la explicación. Eso es lo que permite decir «ve al explorador y crea una carpeta» y
/// verlo ocurrir mientras se habla, en vez de dictar → esperar → ejecutar.
///
/// Las manos son las MISMAS de siempre: <see cref="SurfaceMapTools"/>, que ya sabe anclarse a una
/// ubicación, verificar cada llegada y negarse cuando la pantalla no es la que se creía. Aquí no se
/// añade ninguna capacidad de actuar — se le da voz a la que ya había. Todos los vetos siguen
/// puestos, y eso es deliberado: el camino de voz es más rápido de invocar que el de texto y sería
/// justo el peor sitio para relajar las protecciones.
/// </summary>
public sealed class GeminiLive : IDisposable
{
    private const string Host = "wss://generativelanguage.googleapis.com/ws/"
        + "google.ai.generativelanguage.v1beta.GenerativeService.BidiGenerateContent";

    private readonly SurfaceMapTools _mapa;
    private readonly LiveAudio _audio = new();
    private readonly LiveVideo _video = new();
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private readonly SemaphoreSlim _envio = new(1, 1);

    public GeminiLive(SurfaceMapTools mapa) => _mapa = mapa;

    /// <summary>Está en curso una sesión de voz viva.</summary>
    public bool Viva { get; private set; }

    /// <summary>
    /// Lo fuerte que está sonando Ü ahora mismo (0–1). La carita mueve la boca con esto.
    ///
    /// Es la MISMA medida que usa el detector de voz para no confundir su propio eco con el usuario:
    /// una sola fuente para «cuánto estoy sonando», y así la boca no puede acabar diciendo una cosa
    /// distinta de lo que se oye.
    /// </summary>
    public double NivelVoz => Viva ? _audio.NivelSalida : 0;

    /// <summary>
    /// Pasa la voz al collar Omi sin cortar la conversación. Lo pide la carita con un gesto.
    ///
    /// No abre ni cierra la sesión: sólo cambia de dónde entra el audio. Los dos orígenes entregan
    /// PCM16 a 16 kHz mono, así que desde aquí abajo no se nota (promesa 3 del contrato de la voz).
    /// </summary>
    public void PasarAlCollar() => _audio.PasarAlCollar();

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

    /// <summary>Llamadas que el modelo retiró: ni se ejecutan ni se responden.</summary>
    private readonly HashSet<string> _canceladas = new();
    private readonly object _candadoCancel = new();

    /// <summary>El pase de reanudación más reciente, y si el último cierre no lo pedimos nosotros.</summary>
    private string _pase = "";
    private bool _cayoSolo;
    private int _reintentos;

    private readonly StringBuilder _fraseU = new();
    private readonly StringBuilder _fraseUsuario = new();

    private static string _modelo = "";

    /// <summary>
    /// Qué modelo atiende la voz. SE PREGUNTA, no se supone.
    ///
    /// Escribir aquí un identificador a mano —«gemini-3.1-flash-live»— es apostar a que el catálogo
    /// de Google no se mueva, y se mueve más rápido que nuestros despliegues: el día que cambie, la
    /// voz deja de arrancar con un error que no dice nada. La API sabe cuáles hablan en vivo, y lo
    /// dice: son los que soportan `bidiGenerateContent`. Preguntar cuesta una llamada al abrir la
    /// sesión y quita una suposición del sistema.
    ///
    /// U_LIVE_MODEL lo fuerza si algún día hace falta un modelo concreto.
    /// </summary>
    private static async Task<string> ModeloAsync(string clave, CancellationToken ct)
    {
        string forzado = Environment.GetEnvironmentVariable("U_LIVE_MODEL")?.Trim() ?? "";
        if (forzado.Length > 0) return forzado;
        if (_modelo.Length > 0) return _modelo;

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        string json = await http.GetStringAsync(
            $"https://generativelanguage.googleapis.com/v1beta/models?pageSize=200&key={Uri.EscapeDataString(clave)}", ct);

        using var doc = JsonDocument.Parse(json);
        var vivos = new List<string>();
        if (doc.RootElement.TryGetProperty("models", out var modelos))
            foreach (var m in modelos.EnumerateArray())
            {
                if (!m.TryGetProperty("supportedGenerationMethods", out var metodos)) continue;
                bool habla = metodos.EnumerateArray()
                    .Any(x => (x.GetString() ?? "").Equals("bidiGenerateContent", StringComparison.OrdinalIgnoreCase));
                if (!habla) continue;
                string nombre = (m.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "")
                    .Replace("models/", "");
                if (nombre.Length > 0) vivos.Add(nombre);
            }

        if (vivos.Count == 0)
            throw new InvalidOperationException("esta clave no tiene acceso a ningún modelo de voz en vivo");

        // No todo lo que habla en vivo sirve para esto: la lista real trae también un modelo de
        // robótica y uno de traducción simultánea, que hablarían pero no son un asistente. Se
        // descartan por nombre y entre los que quedan se prefiere «flash-live», que es la familia
        // que Google publica para conversación en tiempo real; en una conversación el retardo
        // importa más que el tamaño del modelo.
        var utiles = vivos.Where(v => !v.Contains("robotics", StringComparison.OrdinalIgnoreCase)
                                   && !v.Contains("translate", StringComparison.OrdinalIgnoreCase)).ToList();
        if (utiles.Count == 0) utiles = vivos;
        _modelo = utiles.FirstOrDefault(v => v.Contains("flash-live", StringComparison.OrdinalIgnoreCase))
               ?? utiles.FirstOrDefault(v => v.Contains("flash", StringComparison.OrdinalIgnoreCase))
               ?? utiles[0];
        LogBus.Log("voz-viva", $"modelos con voz en vivo: {string.Join(", ", vivos)} → se usa «{_modelo}»");
        return _modelo;
    }

    /// <summary>
    /// La llave. Tres escalones, en orden:
    ///   1. GEMINI_API_KEY en la máquina — quien desarrolla pone la suya, igual que GRAPH_API_KEY.
    ///   2. La key EMBEBIDA en el build de distribución (AssemblyMetadata GeminiDefaultApiKey), que
    ///      el CI inyecta desde un secreto — mismo mecanismo que GraphDefaultApiKey. Vacía en los
    ///      builds del repo: cero secretos en código o historial.
    ///   3. Vacío: no hay voz en vivo, y se dice por qué.
    ///
    /// ESTO ES UN ATAJO, dicho en voz alta (2026-08-14, decisión consciente del usuario, no la
    /// recomendada): la key de Gemini es de alcance COMPLETO, y embeberla reparte la MISMA key en
    /// cada copia distribuida — si se filtra o se agota el saldo, afecta a todas las instalaciones a
    /// la vez. Lo correcto sería que el backend la EMITIERA por sesión, como ya hace
    /// <see cref="U.WindowsClient.Clinical.DictadoSoniox"/> con Soniox (clave temporal de 60 s, el
    /// cliente nunca ve la real). Se elige embeber por velocidad; el cambio queda pendiente.
    /// </summary>
    private static string Clave()
    {
        string desdeElEntorno = Environment.GetEnvironmentVariable("GEMINI_API_KEY")?.Trim() ?? "";
        if (desdeElEntorno.Length > 0) return desdeElEntorno;

        return typeof(GeminiLive).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "GeminiDefaultApiKey")?.Value?.Trim() ?? "";
    }

    public async Task AlternarAsync()
    {
        if (Viva) { await TerminarAsync(); return; }
        await ArrancarAsync();
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
            Dice?.Invoke("No hay voz en vivo: falta la clave de Gemini. "
                       + "Una sola vez: setx GEMINI_API_KEY \"tu_key\" y reinicia Ü.");
            LogBus.Log("voz-viva", "sin GEMINI_API_KEY: no se arranca");
            return;
        }

        try
        {
            _cts = new CancellationTokenSource();
            string modelo = await ModeloAsync(clave, _cts.Token);
            _ws = new ClientWebSocket();
            await _ws.ConnectAsync(new Uri($"{Host}?key={Uri.EscapeDataString(clave)}"), _cts.Token);
            await EnviarAsync(Configuracion(modelo), _cts.Token);

            // Sesión nueva, cuentas nuevas: ni llamadas retiradas de antes, ni el pase de la
            // conversación anterior —volver con él nos devolvería a una charla que ya terminó.
            lock (_candadoCancel) _canceladas.Clear();
            _pase = ""; _cayoSolo = false; _reintentos = 0;

            // Cuentas del consumo, también a cero. El id de sesión viaja hasta el
            // ledger y hace de clave de idempotencia: si el envío se reintenta
            // tras un corte de red, la misma conversación no se cobra dos veces.
            _entrada = _salida = _total = 0;
            _turnos = 0;
            _modeloVivo = modelo;
            _sesionId = Guid.NewGuid().ToString("n");
            _inicioSesion = DateTime.UtcNow;

            Viva = true;
            Cambio?.Invoke(true);
            LogBus.Log("voz-viva", $"sesión abierta con «{modelo}»");
            Dice?.Invoke("Te escucho.");

            _audio.Capturado += MandarTrozo;
            _audio.AbrirMicrofono();

            // OJOS. Un fotograma por segundo, con el cursor pintado: es lo que permite decir «esto
            // que estoy señalando» y que signifique algo. Va por el mismo canal que el audio.
            _video.Capturado += MandarFotograma;
            _video.Abrir(1000);
            _ = Task.Run(() => RecibirAsync(_cts.Token));
        }
        catch (Exception e)
        {
            LogBus.Log("voz-viva", $"no se pudo abrir la sesión: {e.Message}");
            await TerminarAsync();

            // UN CORTE DE RED DE UNOS SEGUNDOS NO DEBERÍA COSTARLE UN GESTO AL USUARIO. Antes se
            // rendía al primer intento: alguien pulsaba el micrófono, el DNS fallaba un instante
            // —«Host desconocido (generativelanguage.googleapis.com)»— y la única salida era darse
            // cuenta y volver a pulsar. Y para darse cuenta hay que leer un mensaje que habla de
            // resolución de nombres (2026-08-16, le pasó al usuario; a los pocos minutos el mismo
            // host respondía sin tocar nada).
            //
            // Solo se reintenta lo que puede arreglarse solo. Una clave inválida o un permiso
            // denegado van a fallar igual las tres veces, y reintentarlos solo retrasa el momento de
            // enterarse: ahí se informa y punto.
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
        {
            if (x is System.Net.Sockets.SocketException
                  or System.Net.WebSockets.WebSocketException
                  or TaskCanceledException
                  or TimeoutException) return true;
            // Una petición HTTP que ni siquiera llegó a tener respuesta: no hay veredicto del
            // servidor, así que es del camino. Si trae código de estado, ya no lo es.
            if (x is HttpRequestException http && http.StatusCode is null) return true;
        }
        return false;
    }

    // ── Cuánto ha costado esta conversación ──────────────────────────────────

    /// <summary>
    /// Lo que Ü le manda a Graph al acabar la conversación, para que el panel de
    /// costos la vea. Solo cifras: ni una palabra de lo que se dijo.
    /// </summary>
    public sealed record ConsumoVivo(
        string Modelo, long Entrada, long Salida, long Total, int Turnos, long DuracionMs, string Sesion);

    /// <summary>
    /// A dónde se reporta. Lo cablea la carita con el cliente de Graph; si nadie
    /// lo cablea, la voz sigue funcionando igual y simplemente no se mide.
    /// </summary>
    public Func<ConsumoVivo, Task>? ReportaConsumo { get; set; }

    private long _entrada, _salida, _total;
    private int _turnos;
    private string _modeloVivo = "";
    private string _sesionId = "";
    private DateTime _inicioSesion = DateTime.UtcNow;

    /// <summary>
    /// La Live API va mandando <c>usageMetadata</c> con el TOTAL ACUMULADO de la
    /// sesión, no con el incremento de cada mensaje. Por eso se REEMPLAZA y no se
    /// suma: sumarlo multiplicaría el consumo por el número de mensajes recibidos,
    /// que en una conversación de dos minutos son cientos.
    /// </summary>
    private void AnotarConsumo(JsonElement raiz)
    {
        if (!raiz.TryGetProperty("usageMetadata", out var uso)) return;

        // El ValueKind se comprueba ANTES de pedir el número: TryGetInt64 no
        // devuelve false sobre un texto o un null, lanza InvalidOperationException
        // — y una excepción aquí dentro tumbaría el bucle que recibe la voz.
        static long Leer(JsonElement padre, params string[] nombres)
        {
            foreach (var nombre in nombres)
                if (padre.TryGetProperty(nombre, out var v)
                    && v.ValueKind == JsonValueKind.Number
                    && v.TryGetInt64(out long n) && n > 0)
                    return n;
            return 0;
        }

        long total = Leer(uso, "totalTokenCount");
        if (total <= 0) return;

        _entrada = Leer(uso, "promptTokenCount");
        _salida = Leer(uso, "responseTokenCount", "candidatesTokenCount");
        _total = total;
        _turnos++;
    }

    /// <summary>
    /// Manda el consumo acumulado y lo pone a cero. Nunca lanza y nunca espera:
    /// que el panel de costos se entere no puede retrasar el cierre de la voz ni,
    /// mucho menos, romperlo.
    /// </summary>
    private void ReportarConsumo()
    {
        // Copia local: entre la comprobación y el envío diferido, otro hilo podría
        // dejar la propiedad en null y el `await` reventaría dentro del Task.
        var reporta = ReportaConsumo;
        if (_total <= 0 || reporta is null) { _entrada = _salida = _total = 0; _turnos = 0; return; }

        var parte = new ConsumoVivo(
            _modeloVivo, _entrada, _salida, _total, _turnos,
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
        if (!Viva && _ws == null) return;
        ReportarConsumo();
        Viva = false;
        _audio.Capturado -= MandarTrozo;
        _audio.CerrarMicrofono();
        _audio.Callar();
        _video.Capturado -= MandarFotograma;
        _video.Cerrar();
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
    /// Se piden las DOS transcripciones —la de entrada y la de salida— porque la carita tiene que
    /// poder enseñar la conversación: una voz que actúa sobre el equipo sin dejar rastro escrito de
    /// lo que se le pidió no es auditable, y aquí se están moviendo archivos de verdad.
    /// </summary>
    private static string Configuracion(string modelo)
    {
        var setup = new
        {
            setup = new
            {
                model = $"models/{modelo}",
                // LA VOZ ES FIJA: Iapetus, elegida a mano en el catálogo de AI Studio. Sin esto el
                // servidor sortea una de las 30 en cada sesión nueva, así que Ü sonaba distinto cada
                // vez que se abría una conversación (2026-08-10).
                generationConfig = new
                {
                    responseModalities = new[] { "AUDIO" },
                    speechConfig = new { voiceConfig = new { prebuiltVoiceConfig = new { voiceName = Voz } } },
                },
                systemInstruction = new { parts = new[] { new { text = Instrucciones } } },
                tools = new object[] { new { functionDeclarations = Herramientas() } },
                inputAudioTranscription = new { },
                outputAudioTranscription = new { },

                realtimeInputConfig = DeteccionDeVoz,

                // QUE LA CONVERSACIÓN SOBREVIVA A LA CONEXIÓN. El servidor corta el socket cuando le
                // parece —se midieron cortes a los 36 s, a los 2 min y a los 3 min en la misma
                // tarde— y sin esto cada corte era el final: la conversación se apagaba a media
                // frase (2026-08-05). Pidiéndolo, el servidor va mandando un pase con el que se
                // puede volver a entrar donde lo dejamos, en vez de empezar de cero sin memoria.
                sessionResumption = new { },
            },
        };
        return JsonSerializer.Serialize(setup);
    }

    /// <summary>El pase para volver a la MISMA conversación si se cae la conexión.</summary>
    private static string Reanudacion(string modelo, string pase)
    {
        var setup = new
        {
            setup = new
            {
                model = $"models/{modelo}",
                generationConfig = new
                {
                    responseModalities = new[] { "AUDIO" },
                    speechConfig = new { voiceConfig = new { prebuiltVoiceConfig = new { voiceName = Voz } } },
                },
                systemInstruction = new { parts = new[] { new { text = Instrucciones } } },
                tools = new object[] { new { functionDeclarations = Herramientas() } },
                inputAudioTranscription = new { },
                outputAudioTranscription = new { },
                realtimeInputConfig = DeteccionDeVoz,
                sessionResumption = new { handle = pase },
            },
        };
        return JsonSerializer.Serialize(setup);
    }

    /// <summary>
    /// QUIÉN DECIDE QUE ESTÁS HABLANDO: el servidor, que es como Live API está pensada.
    ///
    /// Esto estuvo APAGADO —<c>disabled = true</c>— desde el 2026-08-04 hasta el 2026-08-16, con un
    /// detector propio en su lugar: umbral aprendido sobre el ruido de sala, más un margen sobre el
    /// eco de la propia Ü. Funcionaba para lo que se escribió (que el eco no la interrumpiera) y
    /// hacía IMPOSIBLE lo otro:
    ///
    ///   interrumpir = hablar MIENTRAS Ü habla = justo cuando aquel umbral estaba más alto.
    ///
    /// No era cuestión de calibrarlo mejor. El diseño usaba UN número —el volumen— para contestar
    /// dos preguntas que por volumen son indistinguibles: «¿esto es el eco de Ü?» y «¿esto es quien
    /// me habla, interrumpiéndola?». Cuanto mejor tapaba el eco, más había que gritar para cortarla.
    /// De ahí los dos síntomas que lo destaparon: «nunca puedo interrumpirlo» y «me toca hablarle
    /// muy duro» (2026-08-16, dicho por el usuario).
    ///
    /// El servidor no tiene ese problema porque no juzga por volumen. Y el barge-in es su
    /// comportamiento por defecto: <c>activityHandling</c> vale <c>START_OF_ACTIVITY_INTERRUPTS</c>
    /// salvo que se pida lo contrario, así que basta con no estorbar y atender el aviso
    /// <c>serverContent.interrupted</c>, que ya se atendía.
    ///
    /// Las cuatro perillas, y por qué estas y no otras:
    ///
    ///   · startOfSpeechSensitivity = LOW — el motivo por el que se apagó todo esto era que el ruido
    ///     de sala se leía como voz. ESTA es la perilla para eso, y por defecto viene en HIGH. El
    ///     comentario viejo decía «bajar la sensibilidad no lo arregló», pero se probó peleando
    ///     contra el ECO, que es otra cosa y no se arregla con sensibilidad.
    ///   · endOfSpeechSensitivity = LOW — no dar el turno por terminado a la primera pausa; cortar a
    ///     media frase se siente como no ser escuchado.
    ///   · silenceDurationMs = 700 — dentro de la banda recomendada (500–800). Por debajo, la
    ///     documentación avisa de que el audio se fragmenta y la transcripción se degrada.
    ///   · prefixPaddingMs = 20 — que no se coma el arranque de la primera sílaba.
    ///
    /// UNA SOLA COPIA, compartida por abrir y por reanudar. Estaban duplicadas, y dos copias del
    /// mismo criterio divergen: basta que alguien afine una.
    /// </summary>
    private static readonly object DeteccionDeVoz = new
    {
        automaticActivityDetection = new
        {
            startOfSpeechSensitivity = "START_SENSITIVITY_LOW",
            endOfSpeechSensitivity = "END_SENSITIVITY_LOW",
            prefixPaddingMs = 20,
            silenceDurationMs = 700,
        },
    };

    /// <summary>Elegida a mano en el catálogo de voces de AI Studio (2026-08-10). Es la voz de Ü.</summary>
    private const string Voz = "Iapetus";

    private const string Instrucciones = """
        Eres Ü, un asistente que maneja el ordenador de quien te habla. Respondes en español, en voz,
        con frases cortas: quien te escucha está mirando la pantalla, no esperando un discurso.

        NO PIDAS PERMISO. Es la regla que más se incumple y la que más molesta: pedirlo en cada paso
        convierte una orden en un interrogatorio, y quien te habla ya decidió cuando te lo pidió.

          · Prohibido preguntar «¿quieres que…?», «¿te parece si…?», «¿procedo?», «¿lo hago?» para
            algo que ya te han pedido. Si te dicen «abre el explorador», lo abres. Si te dicen «busca
            las facturas», las buscas. No lo anuncies como propuesta: hazlo y ve contándolo.
          · Tampoco pidas permiso a MITAD de una tarea para seguir con ella. Los pasos intermedios
            son parte de lo que ya te pidieron, no cosas nuevas.
          · Si algo es ambiguo, NO preguntes por permiso: pregunta por el DATO que te falta, y solo
            ese («¿la carpeta de este mes o la del anterior?»). Y si puedes deducirlo, dedúcelo.

        La ÚNICA excepción: parar antes de algo que no se puede deshacer y que nadie te pidió —
        borrar, sobrescribir, enviar, pagar. Ahí sí se pregunta, una vez y concreta. Todo lo demás
        se hace.

        Tienes manos: las herramientas map_* mueven y accionan aplicaciones de verdad. Úsalas en
        cuanto la petición sea clara. Ve contando lo que haces mientras lo haces («voy al
        explorador», «creando la carpeta»), no al final: lo que se está viendo en pantalla y lo que
        oye tienen que ir juntos.

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

        EL CURSOR MANDA SOBRE TU INTERPRETACIÓN. Cuando el usuario diga «esto», «este», «el que estoy
        señalando», «mira aquí» —o cuando en el vídeo veas su puntero sobre algo— usa map_pointing_at
        ANTES que nada. No adivines de qué elemento habla por el nombre que creas haber entendido:
        él está apuntando, y apuntar es más exacto que describir. map_pointing_at te da la puerta que
        hay bajo el cursor, con su nombre real, y la ilumina. Con ese nombre ya puedes usar
        map_set_level o map_take.

        SEÑALAR ANTES QUE AFIRMAR. Si te preguntan «¿ves X?» o «¿dónde está X?», usa map_show: dice
        si está y además lo marca en pantalla y lleva la carita a su lado. Contestar «sí, lo veo» sin
        señalarlo no vale — quien pregunta está comprobando que los dos miráis lo mismo, y solo lo
        sabe si ve dónde apuntas. Para pulsarlo después, map_take con ese mismo nombre.

        LO QUE VES, LO PUEDES PULSAR. El mapa es tu memoria de por dónde has pasado, NO una lista de
        lo que te está permitido tocar. Nunca digas «lo veo pero como no lo conozco no puedo
        pulsarlo»: eso es falso. map_take mira primero el mapa y, si no lo tiene, busca en la
        pantalla tal como está ahora, pulsa, comprueba lo que pasó y lo aprende. Así que si algo está
        a la vista —en el vídeo o en map_show— llama a map_take y ya está. Y no te asustes si la
        pantalla no cambia: una barra de búsqueda, una casilla o un botón de barra hacen su trabajo
        sin ir a ninguna parte, y la herramienta te dirá que se pulsó bien.

        VARIAS COSAS: DOS CAMINOS, Y ELIGES TÚ CUÁL.

        (a) LO QUE TE ENSEÑAN CON LA MANO. Si acaban de pasar el ratón por encima de varias cosas
        —«ilumina todos estos», «esto que te estoy mostrando»— usa map_pointed_trail: te dice por
        encima de qué pasó el cursor y lo ilumina. Es exacto porque no adivina nada: repite el gesto.

        (b) LO QUE TE DESCRIBEN CON PALABRAS. «Todos los de esa barra lateral», «las carpetas de la
        izquierda», «los botones de arriba», «solo los de este tipo». Aquí NO hay gesto que repetir,
        así que lo resuelves TÚ, razonando, en tres pasos y en este orden:

          1. MIRA EL VÍDEO y decide a qué se refieren. El vídeo es lo único que te dice qué es «esa
             barra», dónde está «arriba» y cuál es «este tipo» — es tu comprensión de la pantalla.
          2. PIDE map_what_i_see. Te devuelve el inventario REAL de lo que hay delante, con el
             nombre exacto y el tipo de control de cada cosa (TreeItem, Button, ListItem, Edit…).
             El vídeo te da el sentido; esta lista te da los nombres con los que se puede actuar.
          3. CRUZA LAS DOS y elige a mano el subconjunto: los del inventario que, según lo que ves
             en el vídeo, están en esa zona Y son de ese tipo. Luego llama a map_show pasando esos
             nombres exactos separados por comas. map_show acepta una lista y los ilumina todos.

        Lo que hace que esto funcione es la división: el VÍDEO para entender de qué te hablan, el
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
        columna izquierda salió la barra de título. La zona la interpretas tú con el vídeo; a la
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

        LA JERARQUÍA SE PUEDE CORREGIR, y el usuario manda. El sistema deduce solo a qué nivel
        pertenece cada cosa —nivel 1 es la navegación principal de la app, la que está siempre a la
        vista— y acierta casi siempre. Cuando el usuario te diga que algo pertenece o no al nivel
        principal, o te señale elementos, usa map_set_level: queda fijo para esa app y la deducción
        ya no lo mueve. Si te señala varios seguidos, uno por uno, y confirma en voz cuáles quedaron.

        Cómo trabajar:
        - Para ABRIR una aplicación, map_open_app. No busques su icono en el mapa: el mapa guarda
          pantallas, no accesos directos, y un icono aprendido en otra app no estará donde estás.
        - Empieza por map_where_am_i si no sabes dónde estás.
        - map_go_to lleva a una pantalla conocida; map_places dice cuáles hay; map_routes_from dice
          qué se puede hacer desde donde estás.
        - map_take pulsa una salida o ejecuta una acción; map_type escribe.
        - Pasa SIEMPRE `at` con la superficie donde crees estar. Si la realidad no coincide, la
          herramienta se negará a actuar: eso es una protección, no un error. Léela y recolócate.
        - Si algo se bloquea, map_unblock. Si te ofrece una decisión de verdad, pregúntasela al
          usuario en voz: esa elección es suya.
        - Si una app no está mapeada, map_learn_app la aprende sola.

        TAREAS QUE YA SE SABEN HACER. Cuando la petición es una de estas, no la improvises paso a
        paso: manda la secuencia entera de una vez con map_run. Una llamada en vez de treinta es la
        diferencia entre verlo ocurrir y verlo pensar.

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

        · «organiza la carpeta de pruebas» / «ordena los archivos por tipo» →
          map_run con steps = el JSON de abajo, tal cual. Di en voz que vas a organizarlos por tipo
          y que son tres grupos, y luego lánzalo.

        [{"op":"go_to","surface":"uia://explorer.exe/u-prueba-organizar"},
         {"op":"take","exit":"Nuevo","at":"uia://explorer.exe/u-prueba-organizar"},
         {"op":"take","exit":"Carpeta","at":"uia://explorer.exe/u-prueba-organizar"},
         {"op":"type","text":"Docs","at":"uia://explorer.exe/u-prueba-organizar"},
         {"op":"take","exit":"factura-enero","action":"click","at":"uia://explorer.exe/u-prueba-organizar"},
         {"op":"take","exit":"factura-febrero","action":"addselect","at":"uia://explorer.exe/u-prueba-organizar"},
         {"op":"take","exit":"contrato-servicios","action":"addselect","at":"uia://explorer.exe/u-prueba-organizar"},
         {"op":"take","exit":"Cortar","at":"uia://explorer.exe/u-prueba-organizar"},
         {"op":"take","exit":"uia:name=Docs;ct=ListItem","at":"uia://explorer.exe/u-prueba-organizar"},
         {"op":"take","exit":"Pegar","at":"uia://explorer.exe/docs"},
         {"op":"go_to","surface":"uia://explorer.exe/u-prueba-organizar"},
         {"op":"take","exit":"Nuevo","at":"uia://explorer.exe/u-prueba-organizar"},
         {"op":"take","exit":"Carpeta","at":"uia://explorer.exe/u-prueba-organizar"},
         {"op":"type","text":"Fotos","at":"uia://explorer.exe/u-prueba-organizar"},
         {"op":"take","exit":"logo-empresa","action":"click","at":"uia://explorer.exe/u-prueba-organizar"},
         {"op":"take","exit":"captura-error","action":"addselect","at":"uia://explorer.exe/u-prueba-organizar"},
         {"op":"take","exit":"foto-equipo","action":"addselect","at":"uia://explorer.exe/u-prueba-organizar"},
         {"op":"take","exit":"Cortar","at":"uia://explorer.exe/u-prueba-organizar"},
         {"op":"take","exit":"uia:name=Fotos;ct=ListItem","at":"uia://explorer.exe/u-prueba-organizar"},
         {"op":"take","exit":"Pegar","at":"uia://explorer.exe/fotos"},
         {"op":"go_to","surface":"uia://explorer.exe/u-prueba-organizar"},
         {"op":"take","exit":"Nuevo","at":"uia://explorer.exe/u-prueba-organizar"},
         {"op":"take","exit":"Carpeta","at":"uia://explorer.exe/u-prueba-organizar"},
         {"op":"type","text":"Datos","at":"uia://explorer.exe/u-prueba-organizar"},
         {"op":"take","exit":"presupuesto-2026.xlsx","action":"click","at":"uia://explorer.exe/u-prueba-organizar"},
         {"op":"take","exit":"inventario.xlsx","action":"addselect","at":"uia://explorer.exe/u-prueba-organizar"},
         {"op":"take","exit":"Cortar","at":"uia://explorer.exe/u-prueba-organizar"},
         {"op":"take","exit":"uia:name=Datos;ct=ListItem","at":"uia://explorer.exe/u-prueba-organizar"},
         {"op":"take","exit":"Pegar","at":"uia://explorer.exe/datos"}]

        DI LO QUE VAS A HACER, Y LUEGO HAZLO. Antes de cada llamada, una frase corta en voz —«voy a
        Descargas», «busco el informe»— y a continuación la herramienta. No al revés y no en
        silencio: quien te habla está mirando la pantalla, y unos segundos sin que digas nada no se
        distinguen de que te hayas colgado. Es la diferencia entre verlo ocurrir y no saber si pasa
        algo.

        Y UNA COSA CADA VEZ mientras se conversa. Si te dicen «ve a descargas», ve y cuenta qué hay;
        si luego te dicen «no, mejor documentos», ve allí y vuelve a contar. No te guardes los pasos
        para hacerlos todos juntos al final: quien habla quiere corregirte a mitad de camino, y no
        puede corregir lo que todavía no ha visto. map_run es para las tareas largas que ya te han
        pedido enteras de una vez, no para una conversación.

        Si una herramienta responde que no actuó, dilo en voz alta y explica por qué. No lo maquilles
        ni sigas como si hubiera funcionado.

        Y no repitas la misma llamada con los mismos argumentos más de dos veces: si falló dos veces
        va a fallar la tercera. Prueba otra vía o cuéntale al usuario qué está pasando y qué
        necesitas de él. Insistir en silencio es lo peor que puedes hacer con las manos puestas en
        el ordenador de alguien.
        """;

    /// <summary>
    /// Las herramientas del mapa, descritas para el modelo.
    ///
    /// Se declaran a mano en vez de generarlas: el texto de cada una es lo que decide si el modelo
    /// la usa bien, y esa redacción es conocimiento del proyecto —«pasa `at` con dónde crees estar»
    /// es una lección que costó varias tareas rotas— no un detalle que convenga derivar de una firma.
    /// </summary>
    private static object[] Herramientas() => new object[]
    {
        Fn("map_where_am_i", "Dice en qué pantalla estás ahora mismo y qué salidas conoce el mapa desde ahí. "
            + "Si hay un diálogo delante, lo describe en vez de fingir que es un lugar."),
        Fn("map_places", "Lista las pantallas que el mapa ya conoce.",
            ("app", "Filtra por aplicación, por ejemplo «explorer». Vacío = todas.")),
        Fn("map_routes_from", "Qué se puede hacer desde una pantalla: a dónde se puede ir y qué acciones hay.",
            ("surface", "La pantalla, por ejemplo «uia://explorer.exe/documentos». Vacío = donde estés.")),
        Fn("map_go_to", "Va a una pantalla, comprobando cada tramo. TAMBIÉN es la forma de ir a una "
            + "PÁGINA WEB: con surface=«web://github.com» abre o activa su pestaña, aunque no estuviera "
            + "abierta y aunque el mapa no conozca ningún camino hasta ella.",
            ("surface", "La pantalla de destino: la que devuelve map_places, o «web://dominio» para una web.")),
        Fn("map_take", "Pulsa CUALQUIER cosa que esté en la pantalla: entrar en una carpeta, «Nuevo», "
            + "«Cortar», «Pegar», una barra de búsqueda, una casilla… No hace falta que el mapa la "
            + "conozca: si no la tiene, la busca en la pantalla de ahora, la pulsa y la aprende.",
            ("exit", "Nombre de lo que hay que pulsar («Nuevo», «Buscar», «Pegar») o un selector «uia:name=X;ct=ListItem»."),
            ("action", "Vacío para lo normal. «addselect» para añadir a la selección sin perder lo anterior."),
            ("at", "La superficie donde CREES estar. Si no coincide con la realidad, no se actúa.")),
        Fn("map_type", "Escribe texto en el campo abierto; sirve para nombrar una carpeta recién creada.",
            ("text", "Lo que hay que escribir."),
            ("target", "Selector del campo. Vacío = el que tenga el foco, y solo si es un campo de texto."),
            ("at", "La superficie donde crees estar.")),
        Fn("map_unblock", "Resuelve un diálogo que está bloqueando el paso y reanuda la tarea.",
            ("at", "La superficie a la que hay que volver después."),
            ("choose", "La opción a pulsar. Vacío = solo si hay una única salida posible.")),
        Fn("map_pointing_at", "PRIORITARIA cuando el usuario señala algo. Devuelve la PUERTA que hay "
            + "bajo el cursor —con su nombre real— y la ilumina. Úsala en cuanto oigas «esto», «este», "
            + "«el que estoy señalando», «mira aquí», o cuando en el vídeo veas su puntero sobre algo. "
            + "Apuntar es más exacto que describir: no adivines el nombre, pregúntalo aquí."),
        Fn("map_what_i_see", "El INVENTARIO de lo que hay en pantalla ahora: el nombre exacto y el TIPO "
            + "de control de cada elemento (TreeItem, Button, ListItem, Edit…), más lo que el mapa sabe "
            + "de él. Pídelo SIEMPRE antes de iluminar un grupo que te han descrito con palabras («los "
            + "de esa barra», «solo las carpetas»): el vídeo te dice a qué se refieren, y esta lista te "
            + "da los nombres exactos y el tipo con los que elegir el subconjunto sin equivocarte."),
        Fn("map_show", "¿VES este elemento? Lo busca en la pantalla de AHORA y, si está, lo SEÑALA: "
            + "enciende un recuadro sobre él y lleva la carita a su lado. Úsala siempre que el usuario "
            + "pregunte «¿ves X?» o «¿dónde está X?» — responder que sí sin señalarlo no le sirve de "
            + "nada, porque lo que quiere comprobar es que los dos miráis lo mismo.",
            ("exit", "Uno: su nombre tal como se ve. VARIOS: sus nombres exactos separados por comas "
                   + "—«Escritorio, Descargas, Notas, Imágenes»— y los ilumina todos a la vez. Pásale "
                   + "SIEMPRE nombres concretos, nunca el nombre de una zona («la columna izquierda»): "
                   + "qué elementos forman esa zona lo decides TÚ mirando el vídeo y cruzándolo con "
                   + "map_what_i_see, y aquí traes ya la lista elegida.")),
        Fn("map_set_level", "Corrige a mano a qué NIVEL pertenece una salida, para toda la app y de "
            + "forma permanente. Nivel 1 = navegación principal. IMPORTANTE: «quítalo del primer "
            + "nivel», «esto no va ahí» o «no es del menú principal» se hace con level = -1 (SOLTAR), "
            + "nunca inventando otro nivel: un nivel declarado CLAVA el elemento en esa fila y la "
            + "jerarquía real ya no puede colocarlo — quitar no es mover, es soltar. Declara un nivel "
            + "concreto solo cuando el usuario lo diga con número o señale dónde va.",
            ("exit", "La salida por su nombre tal como se ve («Notas») o su selector. VARIAS a la vez: "
                   + "sus nombres separados por comas —«Escritorio, Descargas, Notas, Música»—, que es "
                   + "como se corrige una barra entera sin repetir la llamada veinte veces. Te dirá "
                   + "cuáles quedaron fijados y cuáles no encontró."),
            ("level", "El nivel: 1 para la navegación principal, 2 o más para lo de dentro, -1 para soltar."),
            ("cromo", "«true» si es navegación PERSISTENTE de su nivel (se marca en azul): una barra "
                    + "que sigue ahí mientras te mueves dentro de esa sección. El cromo puede vivir en "
                    + "cualquier nivel — una web puede tener barra de cromo en el nivel 1 Y otra dentro "
                    + "de cada sección (nivel 2). Vacío = nivel 1 es cromo y los demás no."),
            ("app", "La app; vacío = donde estés ahora.")),
        Fn("map_run", "Ejecuta una SECUENCIA de pasos de una sola vez, sin volver a consultarte entre "
            + "uno y otro. Es la forma rápida: úsala para las tareas que ya sabes hacer enteras.",
            ("steps", "JSON: lista de pasos. Cada uno {\"op\":\"go_to|take|type|unblock\", …} con los "
                    + "mismos argumentos que las herramientas sueltas.")),
        Fn("map_pointed_trail", "«Ilumina TODO ESTO que te estoy mostrando». Devuelve y señala todo aquello "
            + "por encima de lo que el usuario acaba de pasar el ratón. Úsala SIEMPRE que hable en plural "
            + "señalando —«todos estos», «esto que te muestro», «los que te acabo de pasar»— en vez de "
            + "adivinar una zona de la pantalla por su nombre.",
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
        Fn("map_learn_app", "Recorre una aplicación entera y aprende sus pantallas. Tarda; úsala solo si hace "
            + "falta conocer una app que el mapa no tiene, no para abrirla.",
            ("app", "El proceso, por ejemplo «explorer» o «notepad».")),

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
        Fn("self_mute", "Te callas AHORA MISMO: cortas lo que estés diciendo y dejas de hablar hasta "
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
            "map_run" => "haciendo la secuencia…",
            "map_learn_app" => $"aprendiendo {V("app")}… (esto tarda)",
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
        // Las herramientas contestan en prosa, así que «no se pudo» se reconoce por cómo empieza.
        bool mal = resultado.StartsWith("No ", StringComparison.OrdinalIgnoreCase)
                || resultado.StartsWith("Falta", StringComparison.OrdinalIgnoreCase)
                || resultado.StartsWith("Nada ", StringComparison.OrdinalIgnoreCase)
                || resultado.Contains("no existe", StringComparison.OrdinalIgnoreCase)
                || resultado.Contains("falló", StringComparison.OrdinalIgnoreCase)
                || resultado.Contains("no se pudo", StringComparison.OrdinalIgnoreCase);

        string primera = resultado.Split('\n')[0].Trim();
        if (primera.Length > 70) primera = primera[..70] + "…";
        return $"{(mal ? "✋" : "✓")} {primera}  ({ms} ms)";
    }

    /// <summary>
    /// CADA HERRAMIENTA, CON SU RELOJ, EN UN SITIO QUE SE PUEDA COMPARAR DESPUÉS.
    /// </summary>
    /// <remarks>
    /// El tiempo ya se medía y ya se enseñaba —el «✓ … (817 ms)» del panel— pero solo se veía PASAR:
    /// no quedaba en ningún sitio, así que no se podía contestar «¿qué es lo lento?» sin volver a
    /// hacerlo todo mirando. Y esa es justo la pregunta que hay que contestar para que navegar por
    /// voz vaya tan rápido como el explorador (2026-08-16, pedido por el usuario).
    ///
    /// Va al MISMO pulso donde ya viven «localizar», «leer la pantalla» y «proyectar», y con el
    /// mismo trato: veces, media y LA PEOR. Un panel con dos tablas de tiempos distintas obligaría a
    /// mirar en dos sitios para comparar lo que compite por los mismos milisegundos.
    ///
    /// El prefijo «voz:» las agrupa sin mezclarlas con lo que hace el mapeador por su cuenta: son
    /// costes de cosas distintas y confundirlos es como comparar Gmail con el explorador.
    /// </remarks>
    private static void Apuntar(string tool, IReadOnlyDictionary<string, string> args, string resultado, long ms)
    {
        Mapeador.PulsoDelMapeador.Actual.Costo("voz: " + tool, ms);
        string donde = args.TryGetValue("surface", out var s) && s.Length > 0 ? s
                     : args.TryGetValue("path", out var p) && p.Length > 0 ? p
                     : args.TryGetValue("app", out var a) ? a : "";
        // UNA LÍNEA POR LLAMADA, con lo que hace falta para ordenar por lentitud y saber sobre qué
        // fue. SE RECORTA SOLO EL RESULTADO, nunca el reloj: cortar la línea entera se llevaría por
        // delante justo el número que se viene a buscar.
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

    private static object Fn(string nombre, string descripcion, params (string Nombre, string Que)[] args)
    {
        var props = new Dictionary<string, object>();
        foreach (var (n, q) in args) props[n] = new { type = "string", description = q };
        return new
        {
            name = nombre,
            description = descripcion,
            parameters = new { type = "object", properties = props },
        };
    }

    // ── El caño ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Cuánto sonido trae el trozo (0..1). Sirve para distinguir «alguien habla» de «hay sala».
    /// </summary>
    private static double Volumen(byte[] pcm)
    {
        if (pcm.Length < 2) return 0;
        double suma = 0;
        int n = pcm.Length / 2;
        for (int i = 0; i + 1 < pcm.Length; i += 2)
        {
            short m = (short)(pcm[i] | (pcm[i + 1] << 8));
            suma += (double)m * m;
        }
        return Math.Sqrt(suma / n) / short.MaxValue;
    }

    /// <summary>Cada cuánto se anota el nivel de entrada. Es diagnóstico, no criterio.</summary>
    private static readonly TimeSpan CadenciaDelAforo = TimeSpan.FromSeconds(5);

    private DateTime _ultimoAforo = DateTime.MinValue;
    private double _picoDelTramo;

    /// <summary>
    /// EL AUDIO VIAJA ENTERO Y SIN JUZGAR. Quien decide qué es voz es el servidor.
    ///
    /// Aquí vivían ~120 líneas que decidían por su cuenta cuándo empezaba y terminaba una
    /// intervención: umbral aprendido sobre el ruido de sala, ganancia de eco, dos tramos seguidos
    /// para confirmar, cierre por reloj a los 12 s, y los avisos activityStart/activityEnd. Todo eso
    /// existía porque la detección del servidor estaba apagada; con ella encendida es al revés —
    /// filtrar aquí es esconderle al servidor justo lo que necesita para detectar que le hablas.
    ///
    /// Se borró entero el 2026-08-16 (patrón nº6 del repo: cuando cae la limitación que justificaba
    /// la maquinaria de compensación, se BORRA, no se parchea). Lo que hacía falta no era afinar
    /// aquel umbral: era dejar de tener uno.
    ///
    /// Lo único que queda es un aforo del nivel de entrada cada 5 s, y no decide nada — está para
    /// contestar «¿el micrófono está oyendo algo?» cuando alguien diga que no le escucha, que es una
    /// pregunta que sin este número solo se puede responder adivinando.
    /// </summary>
    private async void MandarTrozo(byte[] pcm)
    {
        if (!Viva || _ws?.State != WebSocketState.Open) return;

        double vol = Volumen(pcm);
        if (vol > _picoDelTramo) _picoDelTramo = vol;
        if (DateTime.UtcNow - _ultimoAforo >= CadenciaDelAforo)
        {
            _ultimoAforo = DateTime.UtcNow;
            LogBus.Log("voz-viva", $"micrófono: pico {_picoDelTramo:F3} en los últimos "
                + $"{CadenciaDelAforo.TotalSeconds:F0} s (el turno lo decide el servidor)");
            _picoDelTramo = 0;
        }

        try
        {
            var msg = new
            {
                realtimeInput = new
                {
                    audio = new
                    {
                        data = Convert.ToBase64String(pcm),
                        mimeType = $"audio/pcm;rate={LiveAudio.RitmoEntrada}",
                    },
                },
            };
            await EnviarAsync(JsonSerializer.Serialize(msg), _cts?.Token ?? CancellationToken.None);
        }
        catch { /* el caño se cierra solo al terminar; un trozo perdido no merece tirar la sesión */ }
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
    public async Task EnviarTextoAsync(string texto)
    {
        if (!Viva || _ws?.State != WebSocketState.Open || string.IsNullOrWhiteSpace(texto)) return;
        Dice?.Invoke($"Tú: {texto}");
        var msg = new
        {
            clientContent = new
            {
                turns = new[] { new { role = "user", parts = new[] { new { text = texto } } } },
                turnComplete = true,
            },
        };
        await EnviarAsync(JsonSerializer.Serialize(msg), _cts?.Token ?? CancellationToken.None);
    }

    /// <summary>
    /// Un fotograma de la pantalla, por el mismo caño que el audio.
    ///
    /// Va sin cola: si el envío anterior no ha terminado, este se pierde y no pasa nada. Un
    /// fotograma viejo no informa de nada —lo que importa es lo que hay AHORA— y acumularlos solo
    /// serviría para retrasar lo siguiente.
    /// </summary>
    private async void MandarFotograma(byte[] jpeg)
    {
        if (!Viva || _ws?.State != WebSocketState.Open || jpeg.Length == 0) return;
        try
        {
            var msg = new
            {
                realtimeInput = new
                {
                    video = new { data = Convert.ToBase64String(jpeg), mimeType = "image/jpeg" },
                },
            };
            await EnviarAsync(JsonSerializer.Serialize(msg), _cts?.Token ?? CancellationToken.None);
        }
        catch { }
    }

    /// <summary>Un único escritor por socket: WebSocket no admite envíos solapados.</summary>
    private async Task EnviarAsync(string json, CancellationToken ct)
    {
        if (_ws == null) return;
        await _envio.WaitAsync(ct);
        try
        {
            await _ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, ct);
        }
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
                    // SE SALÍA DE AQUÍ EN SILENCIO, y desde fuera «lo colgué yo» y «se cayó solo»
                    // eran exactamente lo mismo: micrófono cerrado, vídeo cerrado, sesión cerrada, y
                    // ni una pista de por qué (2026-08-05). Quien cierra tiene un motivo y lo manda;
                    // no anotarlo era tirarlo.
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
            // CAERSE NO ES COLGAR. Si el socket se fue solo —y se va, a los 36 s o a los 3 min, sin
            // avisar— la conversación no ha terminado: la persona sigue hablando. Se vuelve a entrar
            // con el pase de reanudación, que devuelve la MISMA conversación con su memoria.
            if (_cayoSolo && Viva && !ct.IsCancellationRequested) await ReconectarAsync();
            else if (Viva) await TerminarAsync();
        }
    }

    /// <summary>
    /// Vuelve a entrar en la MISMA conversación después de un corte que no pedimos.
    ///
    /// Sin ruido para quien habla: no se cierra el micrófono ni la cámara, no se dice «se cayó la
    /// conexión». Alguien que está a media frase no necesita un parte de red, necesita seguir. Solo
    /// se avisa si de verdad no se puede volver, que entonces sí cambia lo que tiene que hacer.
    ///
    /// Se reintenta unas pocas veces y con espera creciente: si lo que falla es la red o la clave,
    /// insistir a toda velocidad no lo arregla y sí quema la cuota.
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
            string modelo = await ModeloAsync(clave, _cts.Token);

            try { _ws?.Dispose(); } catch { }
            _ws = new ClientWebSocket();
            await _ws.ConnectAsync(new Uri($"{Host}?key={Uri.EscapeDataString(clave)}"), _cts.Token);
            await EnviarAsync(_pase.Length > 0 ? Reanudacion(modelo, _pase) : Configuracion(modelo), _cts.Token);

            LogBus.Log("voz-viva", _pase.Length > 0
                ? $"reconectada y reanudada donde iba (intento {_reintentos})"
                : $"reconectada, pero SIN pase: la conversación empieza de cero (intento {_reintentos})");

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

    private void Procesar(string json, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(json);
        var raiz = doc.RootElement;

        AnotarConsumo(raiz);

        // TODO lo que llega se anota. Atender solo lo que se sabe interpretar y tirar el resto en
        // silencio deja el peor de los diagnósticos posibles: la sesión abierta, el micrófono en
        // rojo, y ninguna pista de por qué no contesta. Un error del servidor tiene que verse.
        // EL PASE PARA VOLVER. Llega cada pocos segundos y antes se tiraba por «no dice nada»: sí lo
        // dice, dice cómo recuperar esta misma conversación si se cae el socket. Solo vale el que
        // viene marcado como reanudable.
        if (raiz.TryGetProperty("sessionResumptionUpdate", out var pase))
        {
            bool sirve = !pase.TryGetProperty("resumable", out var res) || res.ValueKind != JsonValueKind.False;
            if (sirve && pase.TryGetProperty("newHandle", out var h) && h.GetString() is { Length: > 0 } valor)
                _pase = valor;
        }

        if (!raiz.TryGetProperty("serverContent", out _) && !raiz.TryGetProperty("toolCall", out _)
            && !raiz.TryGetProperty("sessionResumptionUpdate", out _))   // llega cada segundo; no dice nada
        {
            // Aplanado: el log es de una línea por entrada, y un JSON con saltos se veía como «{».
            string plano = System.Text.RegularExpressions.Regex.Replace(json, @"\s+", " ");
            LogBus.Log("voz-viva", "← " + (plano.Length > 400 ? plano[..400] + "…" : plano));
        }

        // CANCELADA ES CANCELADA. Cuando el usuario habla encima, el modelo retira las llamadas que
        // había pedido — y aquí no se atendía ese aviso: se seguían ejecutando igual, en serie y
        // tardando segundos, y encima se le contestaba a algo que él ya había dado por muerto.
        //
        // El resultado era un bucle que se comía la conversación: el usuario hablaba, se cancelaban
        // las llamadas, el modelo volvía a pedir LAS MISMAS, y mientras tanto la cola de trabajo
        // seguía creciendo con las viejas. Nunca terminaba una tanda, así que nunca llegaba a
        // responder: «le hablaba y no me respondía» (2026-08-05). Se llegaron a ejecutar llamadas
        // después de colgar la sesión.
        if (raiz.TryGetProperty("toolCallCancellation", out var cancelacion)
            && cancelacion.TryGetProperty("ids", out var ids))
        {
            lock (_candadoCancel)
            {
                // No crece sin fin: los identificadores son de un solo uso y solo importan mientras
                // su tanda esté en la cola.
                if (_canceladas.Count > 200) _canceladas.Clear();
                foreach (var x in ids.EnumerateArray())
                {
                    string s = x.GetString() ?? "";
                    if (s.Length > 0) _canceladas.Add(s);
                }
            }
            LogBus.Log("voz-viva", "canceladas por el modelo: " + string.Join(", ",
                ids.EnumerateArray().Select(x => x.GetString())));
        }

        if (raiz.TryGetProperty("serverContent", out var contenido))
        {
            // Hay conversación de verdad otra vez: el contador de caídas seguidas vuelve a cero. Si
            // no, una sesión larga con un corte cada media hora acabaría rindiéndose por sumar
            // cuatro caídas que no tenían nada que ver entre sí.
            _reintentos = 0;

            // INTERRUMPIDO: el usuario habló encima. Lo que ya nos habían mandado sigue en nuestra
            // cola de audio, y seguir diciéndolo es la sensación exacta de no ser escuchado.
            if (contenido.TryGetProperty("interrupted", out _)) _audio.Callar();

            // LA TRANSCRIPCIÓN LLEGA A TROZOS, no por frases: «Voy a», « intentar», « crear»… Pintar
            // cada trozo como una línea propia convertía la conversación en una columna de palabras
            // sueltas, cada una con su «Ü:» delante (2026-08-04, visto en pantalla). Se acumula y se
            // manda la frase entera cada vez; la carita reemplaza la última línea en vez de añadir,
            // que es lo que hace que se vea escribiéndose en directo en lugar de a saltos.
            if (contenido.TryGetProperty("inputTranscription", out var mio)
                && mio.TryGetProperty("text", out var tMio))
            {
                _fraseUsuario.Append(tMio.GetString());
                Dice?.Invoke($"Tú: {_fraseUsuario}");
                Transcribe?.Invoke($"Tú: {_fraseUsuario}", false);
            }

            if (contenido.TryGetProperty("outputTranscription", out var suyo)
                && suyo.TryGetProperty("text", out var tSuyo))
            {
                _fraseU.Append(tSuyo.GetString());
                Dice?.Invoke($"Ü: {_fraseU}");
                Transcribe?.Invoke($"Ü: {_fraseU}", true);
            }

            // Turno cerrado: lo dicho queda fijo y la siguiente frase empieza línea nueva.
            if (contenido.TryGetProperty("turnComplete", out _)
                || contenido.TryGetProperty("generationComplete", out _))
            {
                TurnoCerrado?.Invoke();
                if (_fraseU.Length > 0) LogBus.Log("voz-viva", $"Ü dijo: {_fraseU}");
                if (_fraseUsuario.Length > 0) LogBus.Log("voz-viva", $"usuario dijo: {_fraseUsuario}");
                _fraseU.Clear();
                _fraseUsuario.Clear();
                Cerro?.Invoke();
            }

            if (contenido.TryGetProperty("modelTurn", out var turno)
                && turno.TryGetProperty("parts", out var partes))
                foreach (var p in partes.EnumerateArray())
                    if (p.TryGetProperty("inlineData", out var dato)
                        && dato.TryGetProperty("data", out var b64))
                        _audio.Reproducir(Convert.FromBase64String(b64.GetString() ?? ""));
        }

        if (raiz.TryGetProperty("toolCall", out var llamada)
            && llamada.TryGetProperty("functionCalls", out var funciones))
        {
            // Se anota que LLEGÓ, antes de intentar nada. Una llamada con un nombre que no
            // reconocemos no dejaba rastro en ninguna parte, y desde fuera eso es idéntico a que el
            // modelo no hubiera pedido nada: dos diagnósticos opuestos con la misma cara.
            LogBus.Log("voz-viva", "llamada recibida: " + System.Text.RegularExpressions.Regex
                .Replace(funciones.GetRawText(), @"\s+", " "));

            // El clon se saca AQUÍ, no dentro de la tarea. Dentro se evaluaba cuando el `using` de
            // esta función ya había liberado el documento, así que reventaba con ObjectDisposed…
            // y como nadie espera la tarea, la excepción se perdía: la llamada llegaba, no se
            // ejecutaba nada, y a los 20 s el modelo la cancelaba (2026-08-04).
            var copia = funciones.Clone();
            _ = Task.Run(() => EjecutarAsync(copia, ct), ct);
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
    private async Task EjecutarAsync(JsonElement funciones, CancellationToken ct)
    {
        try { await EjecutarNucleoAsync(funciones, ct); }
        // Una tarea suelta que revienta se lleva su excepción a la tumba: nadie la espera. Y el
        // síntoma desde fuera es el peor posible — la llamada llega, no pasa nada, y a los 20 s el
        // modelo la cancela sin que en el log haya una sola pista.
        catch (Exception e) { LogBus.Log("voz-viva", $"la ejecución se cayó: {e.GetType().Name}: {e.Message}"); }
    }

    private async Task EjecutarNucleoAsync(JsonElement funciones, CancellationToken ct)
    {
        var respuestas = new List<object>();
        foreach (var f in funciones.EnumerateArray())
        {
            string nombre = f.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            string id = f.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "";
            var args = new Dictionary<string, string>();
            if (f.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Object)
                foreach (var p in a.EnumerateObject())
                    args[p.Name] = p.Value.ValueKind == JsonValueKind.String
                        ? p.Value.GetString() ?? "" : p.Value.ToString();

            // Se mira JUSTO ANTES de cada una, no al empezar la tanda: una tanda de tres puede tardar
            // diez segundos, y si el usuario habla en la primera, las otras dos ya sobran.
            bool anulada;
            lock (_candadoCancel) anulada = id.Length > 0 && _canceladas.Contains(id);
            if (anulada)
            {
                LogBus.Log("voz-viva", $"«{nombre}» se cancela: el modelo la retiró (habló el usuario)");
                continue;   // y NO se responde: contestar a algo retirado es lo que lo hacía repetirla
            }

            LogBus.Log("voz-viva", $"ejecutando «{nombre}»…");
            string resultado;
            if (HerramientasDeAutocontrol.Contains(nombre))
            {
                // Va ANTES que el mapa y sin pasar por SurfaceMapTools: esto no acciona una app de
                // fuera, acciona la propia ventana, y solo quien la tiene (FaceWindow) puede hacerlo.
                Accion?.Invoke(EnCurso(nombre, args), false);
                var relojPropio = System.Diagnostics.Stopwatch.StartNew();
                try { resultado = Autocontrol?.Invoke(nombre) ?? "no puedo: nadie conectó esta herramienta todavía"; }
                catch (Exception e) { resultado = $"la herramienta falló: {e.Message}"; }
                relojPropio.Stop();
                // Se cronometra IGUAL que las demás. Antes se reportaba 0 ms, y un cero no significa
                // «instantáneo»: significa «nadie miró». Las dos cosas se leen igual en un panel.
                Accion?.Invoke(Terminado(nombre, args, resultado, relojPropio.ElapsedMilliseconds), true);
                Apuntar(nombre, args, resultado, relojPropio.ElapsedMilliseconds);
            }
            else if (!SurfaceMapTools.IsMapTool(nombre))
                resultado = $"«{nombre}» no es una herramienta del mapa";
            else
            {
                // SE AVISA ANTES, Y EN CASTELLANO. Esto ya se disparaba antes de la llamada —el
                // instante bueno— pero decía «⚙ file_open path=descargas», que es el nombre de una
                // función, no lo que está pasando. Quien mira la pantalla necesita saber qué se está
                // haciendo mientras se hace; la mitad de la sensación de tiempo real es esta línea.
                Accion?.Invoke(EnCurso(nombre, args), false);
                var reloj = System.Diagnostics.Stopwatch.StartNew();
                try { resultado = _mapa.Call(nombre, args); }
                catch (Exception e) { resultado = $"la herramienta falló: {e.Message}"; }
                reloj.Stop();
                // El pulso lo apunta SurfaceMapTools.Call, por donde pasan todos los que llaman
                // —la voz, la sonda y el bucle del agente—. Contarlo aquí también sería contarlo dos
                // veces, y dos cuentas del mismo hecho acaban discrepando.
                Accion?.Invoke(Terminado(nombre, args, resultado, reloj.ElapsedMilliseconds), true);
            }

            respuestas.Add(new { id, name = nombre, response = new { result = resultado } });
        }

        if (respuestas.Count == 0) return;
        try
        {
            await EnviarAsync(JsonSerializer.Serialize(
                new { toolResponse = new { functionResponses = respuestas } }), ct);
        }
        catch (Exception e) { LogBus.Log("voz-viva", $"no pude devolver el resultado: {e.Message}"); }
    }

    public void Dispose()
    {
        try { TerminarAsync().GetAwaiter().GetResult(); } catch { }
        _audio.Dispose();
        _envio.Dispose();
    }
}
