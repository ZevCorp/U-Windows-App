using System.Text.Json;

namespace Voz.Realtime;

/// <summary>
/// HABLAR CON GPT-LIVE. Medido contra el servidor de verdad el 2026-09-11 y el 2026-09-12 con la clave
/// de esta máquina — se abrió el socket, se mandó cada evento y se leyó lo que contestó —, no deducido
/// de la documentación.
/// </summary>
/// <remarks>
/// NO ES REALTIME CON OTRO NOMBRE. Las diferencias no dan error: dan «no me responde».
///
///  · SU PROPIA PUERTA. <c>gpt-live-1</c> en /v1/realtime contesta «not supported in realtime mode».
///    Va por /v1/live/sessions, sin ?model=, y se abre con <c>session.start</c>.
///
///  · LA VOZ NO LLEVA HERRAMIENTAS. Las lleva un modelo DELEGADO (<c>gpt-5.6-luna</c>; <c>gpt-5.6-terra</c>
///    también funciona) dentro de <c>session.delegation.responses</c>: él mira, llama y decide; la voz
///    conversa y le pasa el trabajo. Por eso las instrucciones completas de Ü van al delegado y la voz
///    lleva una persona corta.
///
///  · LA SESIÓN ES INMUTABLE SALVO LA DELEGACIÓN. <c>session.update</c> con <c>session.instructions</c>
///    contesta «Unknown parameter: 'session.instructions'»; con <c>session.delegation</c> contesta
///    <c>session.updated</c>, y el delegado llamó a la herramienta NUEVA (map_where_am_i, con su
///    argumento nuevo) en la frase siguiente.
///
///  · NO HAY MARCAS DE TURNO. Ni speech_started ni response.done: hablarle encima no produce ningún
///    evento, y <c>response.completed</c> es del DELEGADO — la voz sigue hablando segundos después. No
///    se inventan aquí; se declara <see cref="MarcaLosTurnos"/> y las marca la conversación.
///
///  · NO SABE CALLAR. No hay turn_detection ni create_response:false, y «no hables por tu cuenta» en
///    las instrucciones no se respetó: la voz prestada (promesa 192) no se puede hacer con GPT-Live.
///
///  · NI PASE PARA VOLVER: una caída empieza de cero, y <see cref="SabeVolver"/> lo dice.
/// </remarks>
public sealed class ProtocoloGptLive : IProtocolo
{
    /// <summary>La misma voz que en Realtime: Ü sonando distinto al cambiar de servidor suena a otro.</summary>
    public const string Voz = "marin";

    /// <summary>
    /// LO QUE LLEVA LA VOZ: quién es y que delega. Corta a propósito — la voz no ve la pantalla ni tiene
    /// herramientas, y si llevara las instrucciones de operar prometería lo que no puede hacer ella y
    /// contestaría de memoria en vez de delegar.
    /// </summary>
    /// <remarks>
    /// Y LA REGLA DE LA 161, que no viaja sola: las instrucciones de Ü van al delegado, pero quien suena es
    /// la voz. Sin ella, en la sonda del 2026-09-12 dijo «Vale. Dame un momento para revisarlo.» antes de
    /// que el delegado hiciera nada. Promesa 46 de la voz.
    /// </remarks>
    public const string InstruccionesDeLaVoz =
        "Eres Ü, el asistente que ayuda a operar las aplicaciones de este ordenador, sobre todo SAP. "
        + "Hablas en español, con frases cortas y naturales. Tú no ves la pantalla ni la tocas: todo lo que "
        + "sea mirar, buscar, pulsar, escribir u operar la pantalla lo delegas siempre, y después cuentas lo "
        + "que salió. Nunca inventes lo que hay en pantalla."
        + " NO ANUNCIES LO QUE VAS A HACER: nada de «voy a…», «vamos a…», «déjame…», «dame un momento», «un momento», «ahora lo miro». Mientras se hace el trabajo, calla."
        + " CUANDO HABLES, HABLA EN PASADO Y DEL RESULTADO: «estás en SAP Easy Access», «no había ningún informe». Nunca en futuro.";

    public string Quien => "OpenAI GPT-Live";
    public string Modelo { get; }

    /// <summary>El modelo que lleva las herramientas. Clavado, como la voz: un alias se mueve solo.</summary>
    public string Delegado { get; }

    public int RitmoDeEntrada => 24000;
    public int RitmoDeSalida => 24000;

    /// <summary>SÍ: una foto en un mensaje del usuario, con response.create detrás, y el delegado dijo el
    /// color y el texto de la imagen (2026-09-12).</summary>
    public bool Mira => true;
    public bool SabeVolver => false;
    public bool MarcaLosTurnos => false;
    public bool SabeEsperarTurno => false;

    public ProtocoloGptLive(string modelo = "gpt-live-1", string delegado = "gpt-5.6-luna")
    {
        Modelo = modelo;
        Delegado = delegado;
    }

    public Uri Direccion() => new("wss://api.openai.com/v1/live/sessions");

    /// <summary>La clave va en la cabecera, NO en la URL: una URL acaba en los logs.</summary>
    public IReadOnlyDictionary<string, string> Cabeceras(string clave)
        => new Dictionary<string, string> { ["Authorization"] = "Bearer " + clave };

    public IEnumerable<string> Apertura(string instrucciones, IReadOnlyList<Utensilio> utensilios, string pase)
        => Apertura(instrucciones, utensilios, pase, soloCuandoSeLePide: false);

    public IEnumerable<string> Apertura(string instrucciones, IReadOnlyList<Utensilio> utensilios, string pase, bool soloCuandoSeLePide)
    {
        // El pase y el «solo cuando se le pide» se ignoran porque aquí no existen. No se disimula:
        // SabeVolver y SabeEsperarTurno ya lo dicen, y quien llama decide qué contar.
        yield return JsonSerializer.Serialize(new
        {
            type = "session.start",
            session = new
            {
                model = Modelo,
                instructions = InstruccionesDeLaVoz,
                audio = new
                {
                    format = new { type = "audio/pcm", rate = RitmoDeEntrada },
                    output = new { voice = Voz },
                },
                delegation = Delegacion(instrucciones, utensilios),
            },
        });
    }

    /// <summary>
    /// UN session.update CON LA DELEGACIÓN ENTERA, y nada más. Lo único que el servidor deja cambiar a
    /// mitad de sesión; mandar otro session.start no cambiaría de modo, abriría otra conversación.
    /// </summary>
    public IEnumerable<string> CambioDeModo(string instrucciones, IReadOnlyList<Utensilio> utensilios, bool soloCuandoSeLePide)
    {
        yield return JsonSerializer.Serialize(new
        {
            type = "session.update",
            session = new { delegation = Delegacion(instrucciones, utensilios) },
        });
    }

    private object Delegacion(string instrucciones, IReadOnlyList<Utensilio> utensilios) => new
    {
        type = "responses",
        responses = new
        {
            model = Delegado,
            instructions = instrucciones,
            tools = ProtocoloOpenAI.ComoFunciones(utensilios),
            tool_choice = "auto",
        },
    };

    public string Audio(byte[] pcm) => JsonSerializer.Serialize(new
    {
        type = "session.input_audio.append",
        audio = Convert.ToBase64String(pcm),
    });

    public string Fotograma(byte[] jpeg) => MensajeDelUsuario(new
    {
        type = "input_image",
        image_url = "data:image/jpeg;base64," + Convert.ToBase64String(jpeg),
    });

    /// <summary>
    /// Una frase escrita. NO pide turno: sin un response.create detrás el servidor la acepta y no
    /// contesta (medido); pedirlo es <see cref="PedirRespuesta"/>, aparte, como en Realtime.
    /// </summary>
    public string Texto(string texto) => MensajeDelUsuario(new { type = "input_text", text = texto });

    private static string MensajeDelUsuario(object contenido) => JsonSerializer.Serialize(new
    {
        type = "response.item.create",
        item = new { type = "message", role = "user", content = new[] { contenido } },
    });

    public IEnumerable<string> Resultados(IReadOnlyList<(string Id, string Nombre, string Resultado)> hechas)
    {
        foreach (var (id, _, resultado) in hechas)
            yield return JsonSerializer.Serialize(new
            {
                type = "response.item.create",
                item = new { type = "function_call_output", call_id = id, output = resultado },
            });
    }

    /// <summary>
    /// Sin texto, pedir turno: <c>response.create</c>. Del resultado a la primera voz, 9–72 ms.
    /// </summary>
    /// <remarks>
    /// CON TEXTO, DICTAR: <c>session.commentary.append</c>. Es lo que hizo decir a la voz la frase
    /// literal «X» ante «Di exactamente esto, sin añadir nada: X» (2026-09-12). El response.create con
    /// instrucciones que usa Realtime aquí no existe, y session.instructions.append no provoca respuesta.
    /// </remarks>
    public string PedirRespuesta(string instrucciones = "") =>
        string.IsNullOrWhiteSpace(instrucciones)
            ? JsonSerializer.Serialize(new { type = "response.create" })
            : JsonSerializer.Serialize(new
            {
                type = "session.commentary.append",
                delegation_id = (string?)null,
                content = instrucciones,
            });

    public IReadOnlyList<Hecho> Leer(JsonElement m)
    {
        var hechos = new List<Hecho>();

        switch (Cadena(m, "type"))
        {
            // CONTINUO, también en silencio: el servidor manda audio aunque la voz calle. Un delta
            // vacío no es sonido.
            case "session.output_audio.delta":
                if (Cadena(m, "delta") is { Length: > 0 } b64)
                    hechos.Add(new Hecho.Suena(Convert.FromBase64String(b64)));
                break;

            case "session.output_transcript.delta":
                if (Cadena(m, "delta") is { Length: > 0 } suyo)
                    hechos.Add(new Hecho.DiceU(suyo));
                break;

            case "session.input_transcript.delta":
                if (Cadena(m, "delta") is { Length: > 0 } mio)
                    hechos.Add(new Hecho.DiceElUsuario(mio));
                break;

            // LO QUE HACE EL DELEGADO llega envuelto: response.event con un evento de la Responses API
            // dentro. De todos, solo UNO es una llamada: el item de tipo function_call terminado. La
            // misma llamada llega antes como response.function_call_arguments.done, y atender los dos
            // ejecutaría cada herramienta dos veces. response.completed tampoco se atiende: es el fin
            // del trabajo del delegado, no del turno — la voz sigue hablando después.
            case "response.event":
                if (m.TryGetProperty("event", out var ev) && Cadena(ev, "type") == "response.output_item.done"
                    && ev.TryGetProperty("item", out var item) && Cadena(item, "type") == "function_call")
                    hechos.Add(new Hecho.Pide(new[] { ProtocoloOpenAI.LaLlamada(item) }));
                break;

            case "error":
                hechos.Add(new Hecho.Falla(m.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object
                    ? Cadena(e, "message") is { Length: > 0 } msg ? msg : e.GetRawText()
                    : "error sin detalle"));
                break;

            // LO QUE DURA, no lo que cuesta en fichas: GPT-Live no manda fichas. Llega cada ~15 s con el
            // ACUMULADO (12.0 y luego 25.0, medido el 2026-09-12). Sin traducirlo, el panel de costos no
            // recibía nada de estas sesiones. Solo un número: un «seconds» vacío o en texto no es cero.
            // session.closed también trae usage, pero llega después de que la conversación reportó al
            // cerrar, y la 41 lo congela como UNA falla: no se traduce allí (promesa 48).
            case "session.usage.updated":
                if (m.TryGetProperty("usage", out var uso) && uso.ValueKind == JsonValueKind.Object
                    && uso.TryGetProperty("seconds", out var seg) && seg.ValueKind == JsonValueKind.Number)
                    hechos.Add(new Hecho.Duracion(seg.GetDouble()));
                break;

            // EL SERVIDOR CERRÓ: se cuenta con su motivo (close_requested, expiración…). Callarlo deja una
            // sesión muerta con el micrófono en rojo y ninguna pista de por qué no contesta.
            case "session.closed":
                hechos.Add(new Hecho.Falla("sesión cerrada: "
                    + (Cadena(m, "reason") is { Length: > 0 } motivo ? motivo : "sin motivo")));
                break;
        }

        return hechos;
    }

    /// <summary>El campo si es texto; vacío si falta o es de otra forma. Lo que viene de la red se
    /// normaliza aquí y no se le pregunta dos veces.</summary>
    private static string Cadena(JsonElement o, string campo)
        => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";
}
