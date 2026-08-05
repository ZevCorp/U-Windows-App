using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
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

    /// <summary>Texto para la carita: lo que se oye, lo que responde, y qué está haciendo.</summary>
    public event Action<string>? Dice;

    /// <summary>Arrancó o terminó. La interfaz cambia el icono del micrófono con esto.</summary>
    public event Action<bool>? Cambio;

    /// <summary>El turno se cerró: lo siguiente que se diga empieza en una línea nueva.</summary>
    public event Action? Cerro;

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
    /// La llave. En producción la emite el backend —el cliente NO tiene keys, que es regla vieja de
    /// este proyecto y por eso la enseñanza por video pide un token firmado— y en la máquina de
    /// quien desarrolla vale su propia GEMINI_API_KEY, igual que ya funciona GRAPH_API_KEY. Una key
    /// que el usuario ya tiene en SU equipo no se está repartiendo a nadie.
    /// </summary>
    private static string Clave() =>
        Environment.GetEnvironmentVariable("GEMINI_API_KEY")?.Trim() ?? "";

    public async Task AlternarAsync()
    {
        if (Viva) { await TerminarAsync(); return; }
        await ArrancarAsync();
    }

    public async Task ArrancarAsync()
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
            Dice?.Invoke($"No pude abrir la voz en vivo: {e.Message}");
            await TerminarAsync();
        }
    }

    public async Task TerminarAsync()
    {
        if (!Viva && _ws == null) return;
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
                generationConfig = new { responseModalities = new[] { "AUDIO" } },
                systemInstruction = new { parts = new[] { new { text = Instrucciones } } },
                tools = new object[] { new { functionDeclarations = Herramientas() } },
                inputAudioTranscription = new { },
                outputAudioTranscription = new { },

                // QUIÉN DECIDE QUE ESTÁS HABLANDO: nosotros, no el servidor.
                //
                // Con la detección automática, el micrófono abierto de continuo bastaba para que
                // cualquier ruido de sala se leyera como que alguien interrumpe: el modelo abortaba
                // el turno y llegaba «toolCallCancellation» sin que la llamada nos llegara siquiera.
                // Bajar la sensibilidad no lo arregló. Apagarla y marcar nosotros el principio y el
                // final de cada intervención sí, porque el criterio pasa a estar donde se puede
                // medir: en el volumen del trozo que acabamos de capturar (2026-08-04).
                realtimeInputConfig = new { automaticActivityDetection = new { disabled = true } },
            },
        };
        return JsonSerializer.Serialize(setup);
    }

    private const string Instrucciones = """
        Eres Ü, un asistente que maneja el ordenador de quien te habla. Respondes en español, en voz,
        con frases cortas: quien te escucha está mirando la pantalla, no esperando un discurso.

        Tienes manos: las herramientas map_* mueven y accionan aplicaciones de verdad. Úsalas en
        cuanto la petición sea clara, sin pedir permiso para cada paso — el usuario ya te lo pidió.
        Ve contando lo que haces mientras lo haces («voy al explorador», «creando la carpeta»), no al
        final: lo que se está viendo en pantalla y lo que oye tienen que ir juntos.

        SEÑALAR ANTES QUE AFIRMAR. Si te preguntan «¿ves X?» o «¿dónde está X?», usa map_show: dice
        si está y además lo marca en pantalla y lleva la carita a su lado. Contestar «sí, lo veo» sin
        señalarlo no vale — quien pregunta está comprobando que los dos miráis lo mismo, y solo lo
        sabe si ve dónde apuntas. Para pulsarlo después, map_take con ese mismo nombre.

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

        · «ve a vídeos» / «ábreme imágenes» / «llévame a notas» →
          map_go_to directo, sin preguntar ni mirar antes, con la superficie que corresponda:
          vídeos → uia://explorer.exe/videos
          imágenes → uia://explorer.exe/imágenes
          notas → uia://explorer.exe/notas
          Si te piden otro sitio que no esté en esta lista, map_places para encontrarlo y map_go_to.

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
        Fn("map_go_to", "Va a una pantalla conocida recorriendo el mapa, comprobando cada tramo.",
            ("surface", "La pantalla de destino, tal como la devuelve map_places.")),
        Fn("map_take", "Pulsa una salida o ejecuta una acción de la pantalla actual: entrar en una carpeta, "
            + "«Nuevo», «Cortar», «Pegar», seleccionar un archivo…",
            ("exit", "Nombre de la salida o acción («Nuevo», «Pegar») o un selector «uia:name=X;ct=ListItem»."),
            ("action", "Vacío para lo normal. «addselect» para añadir a la selección sin perder lo anterior."),
            ("at", "La superficie donde CREES estar. Si no coincide con la realidad, no se actúa.")),
        Fn("map_type", "Escribe texto en el campo abierto; sirve para nombrar una carpeta recién creada.",
            ("text", "Lo que hay que escribir."),
            ("target", "Selector del campo. Vacío = el que tenga el foco, y solo si es un campo de texto."),
            ("at", "La superficie donde crees estar.")),
        Fn("map_unblock", "Resuelve un diálogo que está bloqueando el paso y reanuda la tarea.",
            ("at", "La superficie a la que hay que volver después."),
            ("choose", "La opción a pulsar. Vacío = solo si hay una única salida posible.")),
        Fn("map_show", "¿VES este elemento? Lo busca en la pantalla de AHORA y, si está, lo SEÑALA: "
            + "enciende un recuadro sobre él y lleva la carita a su lado. Úsala siempre que el usuario "
            + "pregunte «¿ves X?» o «¿dónde está X?» — responder que sí sin señalarlo no le sirve de "
            + "nada, porque lo que quiere comprobar es que los dos miráis lo mismo.",
            ("exit", "Uno: su nombre tal como se ve. Varios: separados por comas. O una ZONA de la "
                   + "ventana: «la columna derecha», «el panel izquierdo», «la barra de arriba», "
                   + "«todos los elementos de abajo» — se señalan todos los de esa zona.")),
        Fn("map_set_level", "Corrige a mano a qué NIVEL pertenece una salida, para toda la app y de "
            + "forma permanente. Nivel 1 = navegación principal (los hermanos que están siempre a la "
            + "vista). Úsala cuando el usuario diga cosas como «esto es del menú principal», «esto no "
            + "pertenece al primer nivel» o «pon esto en el nivel 2». Con level = -1 se suelta y vuelve "
            + "a decidirlo el sistema.",
            ("exit", "La salida, por su nombre tal como se ve («Notas») o su selector."),
            ("level", "El nivel: 1 para la navegación principal, 2 o más para lo de dentro, -1 para soltar."),
            ("app", "La app; vacío = donde estés ahora.")),
        Fn("map_run", "Ejecuta una SECUENCIA de pasos de una sola vez, sin volver a consultarte entre "
            + "uno y otro. Es la forma rápida: úsala para las tareas que ya sabes hacer enteras.",
            ("steps", "JSON: lista de pasos. Cada uno {\"op\":\"go_to|take|type|unblock\", …} con los "
                    + "mismos argumentos que las herramientas sueltas.")),
        Fn("map_open_app", "ABRE una aplicación (o la trae al frente si ya estaba) y dice en qué pantalla "
            + "quedas. Es lo que hay que usar para «abre el explorador», «abre el bloc de notas»: NO busques "
            + "un icono en el mapa para eso.",
            ("app", "El proceso, por ejemplo «explorer», «notepad», «chrome».")),
        Fn("map_learn_app", "Recorre una aplicación entera y aprende sus pantallas. Tarda; úsala solo si hace "
            + "falta conocer una app que el mapa no tiene, no para abrirla.",
            ("app", "El proceso, por ejemplo «explorer» o «notepad».")),
    };

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

    /// <summary>
    /// Cuánto hay que subir la voz sobre el ruido de la sala para que cuente como hablar.
    ///
    /// Era un número fijo (0,045) medido en UN equipo, y eso lo hacía una lotería: con un micrófono
    /// de menos ganancia la puerta no se abría NUNCA, así que no se enviaba ni un byte y la sesión
    /// se quedaba abierta sin oír nada —«dice te escucho y no me escucha» (2026-08-04)—. El nivel de
    /// un micrófono depende del aparato, del sistema y de la sala; fijarlo a mano es adivinar.
    ///
    /// Ahora se aprende el silencio de esta sala y se exige destacar sobre ÉL. El suelo absoluto es
    /// solo una red para micrófonos con ruido eléctrico.
    /// </summary>
    private const double SueloAbsoluto = 0.008;
    private const double VecesSobreElRuido = 3.0;

    private double _ruidoSala = 0.02;
    private bool _usuarioHablando;
    private DateTime _ultimaVoz;
    private DateTime _ultimoAforo = DateTime.MinValue;
    private double _picoDelTramo;

    private async void MandarTrozo(byte[] pcm)
    {
        if (!Viva || _ws?.State != WebSocketState.Open) return;

        // EL TURNO SE ABRE Y SE CIERRA A MANO. Mientras el volumen no llega a voz, no se manda nada:
        // el silencio no tiene por qué viajar, y sobre todo no puede leerse como una interrupción.
        // Cuando arranca, se avisa con activityStart; cuando lleva un rato callado, activityEnd — y
        // ese cierre es lo que le dice al modelo «ya, te toca». Sin él esperaría eternamente.
        //
        // El umbral sube mientras Ü habla, no se cierra del todo: cortarle a media frase es media
        // gracia de hablar en vivo, pero su propia voz por los altavoces no puede valer como corte.
        double vol = Volumen(pcm);

        // El silencio se APRENDE: baja deprisa hacia lo más bajo que se oye y sube muy despacio, de
        // modo que una frase larga no lo arrastre consigo. Así el umbral se calibra solo en cualquier
        // equipo, que es justo lo que un número fijo no podía hacer.
        _ruidoSala = vol < _ruidoSala ? (_ruidoSala * 0.90) + (vol * 0.10)
                                      : (_ruidoSala * 0.999) + (vol * 0.001);
        double umbral = Math.Max(SueloAbsoluto, _ruidoSala * VecesSobreElRuido);
        if (_audio.Hablando) umbral *= 2.0;   // mientras Ü habla, solo una voz clara la corta

        // Se publica lo que se está oyendo. Sin esto, «no me escucha» y «no le llega audio» se ven
        // exactamente igual desde fuera, que es lo que costó encontrar este fallo.
        _picoDelTramo = Math.Max(_picoDelTramo, vol);
        if ((DateTime.UtcNow - _ultimoAforo).TotalSeconds >= 2)
        {
            _ultimoAforo = DateTime.UtcNow;
            LogBus.Log("voz-viva", $"micrófono: pico {_picoDelTramo:F3} · ruido {_ruidoSala:F3} · "
                + $"umbral {umbral:F3} · {(_usuarioHablando ? "HABLANDO" : "en silencio")}");
            _picoDelTramo = 0;
        }

        if (vol >= umbral)
        {
            _ultimaVoz = DateTime.UtcNow;
            if (!_usuarioHablando)
            {
                _usuarioHablando = true;
                await EnviarAsync("""{"realtimeInput":{"activityStart":{}}}""", _cts?.Token ?? default);
            }
        }
        else if (_usuarioHablando && (DateTime.UtcNow - _ultimaVoz).TotalMilliseconds > 700)
        {
            _usuarioHablando = false;
            await EnviarAsync("""{"realtimeInput":{"activityEnd":{}}}""", _cts?.Token ?? default);
            return;
        }

        if (!_usuarioHablando) return;

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
                if (r.MessageType == WebSocketMessageType.Close) break;
                acumulado.Write(buf, 0, r.Count);
                if (!r.EndOfMessage) continue;

                string texto = Encoding.UTF8.GetString(acumulado.ToArray());
                acumulado.SetLength(0);
                try { Procesar(texto, ct); }
                catch (Exception e) { LogBus.Log("voz-viva", $"mensaje ilegible: {e.Message}"); }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { LogBus.Log("voz-viva", $"se cortó la escucha: {e.Message}"); }
        finally { if (Viva) await TerminarAsync(); }
    }

    private void Procesar(string json, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(json);
        var raiz = doc.RootElement;

        // TODO lo que llega se anota. Atender solo lo que se sabe interpretar y tirar el resto en
        // silencio deja el peor de los diagnósticos posibles: la sesión abierta, el micrófono en
        // rojo, y ninguna pista de por qué no contesta. Un error del servidor tiene que verse.
        if (!raiz.TryGetProperty("serverContent", out _) && !raiz.TryGetProperty("toolCall", out _)
            && !raiz.TryGetProperty("sessionResumptionUpdate", out _))   // llega cada segundo; no dice nada
        {
            // Aplanado: el log es de una línea por entrada, y un JSON con saltos se veía como «{».
            string plano = System.Text.RegularExpressions.Regex.Replace(json, @"\s+", " ");
            LogBus.Log("voz-viva", "← " + (plano.Length > 400 ? plano[..400] + "…" : plano));
        }

        if (raiz.TryGetProperty("serverContent", out var contenido))
        {
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
            }

            if (contenido.TryGetProperty("outputTranscription", out var suyo)
                && suyo.TryGetProperty("text", out var tSuyo))
            {
                _fraseU.Append(tSuyo.GetString());
                Dice?.Invoke($"Ü: {_fraseU}");
            }

            // Turno cerrado: lo dicho queda fijo y la siguiente frase empieza línea nueva.
            if (contenido.TryGetProperty("turnComplete", out _)
                || contenido.TryGetProperty("generationComplete", out _))
            {
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

            LogBus.Log("voz-viva", $"ejecutando «{nombre}»…");
            string resultado;
            if (!SurfaceMapTools.IsMapTool(nombre))
                resultado = $"«{nombre}» no es una herramienta del mapa";
            else
            {
                Dice?.Invoke($"⚙ {nombre} {string.Join(" ", args.Select(kv => $"{kv.Key}={kv.Value}"))}".TrimEnd());
                try { resultado = _mapa.Call(nombre, args); }
                catch (Exception e) { resultado = $"la herramienta falló: {e.Message}"; }
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
