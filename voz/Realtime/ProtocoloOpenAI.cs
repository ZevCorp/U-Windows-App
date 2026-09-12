using System.Text.Json;

namespace Voz.Realtime;

/// <summary>
/// HABLAR CON GPT REALTIME. Comprobado contra el servidor de verdad el 2026-08-24, no deducido de
/// la documentación: se abrió el socket, se mandó la sesión y se leyó lo que contestó.
/// </summary>
/// <remarks>
/// EL MODELO VA CLAVADO. <c>gpt-realtime-mini</c> es un alias y los alias se mueven solos: el día
/// que apunte a otra versión, la voz cambiaría de comportamiento sin que nadie hubiera tocado nada
/// y no habría forma de saber por qué. <c>gpt-realtime-2.1-mini</c> existe y es lo que se pidió.
///
/// DOS DIFERENCIAS QUE MUERDEN, y ninguna da error — las dos se manifiestan como «no me responde»:
///
///  · EL RITMO DE ENTRADA ES 24 kHz, no los 16 kHz de Gemini. Mandar 16 kHz diciendo que son 24 no
///    falla: el servidor oye una voz ralentizada y la transcripción se vuelve basura.
///
///  · HAY QUE PEDIRLE QUE CONTESTE después de devolverle el resultado de una herramienta. Gemini
///    sigue solo; aquí, sin un <c>response.create</c> detrás, el modelo se queda con el resultado en
///    la mano y callado para siempre. Es exactamente el síntoma del que veníamos huyendo.
///
/// NI VE NI SABE VOLVER: no hay canal de vídeo ni pase de reanudación. Se dice aquí y arriba se
/// actúa en consecuencia —no se abre la cámara, y una caída se cuenta como lo que es— en vez de
/// mandar cosas que se tragan sin efecto.
/// </remarks>
public sealed class ProtocoloOpenAI : IProtocolo
{
    /// <summary>
    /// LA VOZ ES FIJA. Sin fijarla, el servidor puede sonar distinto entre sesiones, y Ü sonando
    /// distinto cada vez que se abre una conversación ya pasó con Gemini (2026-08-10).
    /// </summary>
    public const string Voz = "marin";

    public string Quien => "OpenAI";
    public string Modelo { get; }
    public int RitmoDeEntrada => 24000;
    public int RitmoDeSalida => 24000;

    /// <summary>SÍ acepta imágenes — comprobado contra el servidor real el 2026-08-24: un PNG de
    /// prueba en un <c>conversation.item.create</c> se aceptó sin error. Lo que NO hace este
    /// protocolo es mandarlas SOLAS y seguidas como Gemini; por eso arriba, en <see
    /// cref="ConversacionEnVivo"/>, no hay vídeo en directo — solo fotos sueltas, a pedido.</summary>
    public bool Mira => true;
    public bool SabeVolver => false;

    public ProtocoloOpenAI(string modelo = "gpt-realtime-2.1-mini") => Modelo = modelo;

    public Uri Direccion() => new($"wss://api.openai.com/v1/realtime?model={Uri.EscapeDataString(Modelo)}");

    /// <summary>La clave va en la cabecera, NO en la URL: una URL acaba en los logs.</summary>
    public IReadOnlyDictionary<string, string> Cabeceras(string clave)
        => new Dictionary<string, string> { ["Authorization"] = "Bearer " + clave };

    public IEnumerable<string> Apertura(string instrucciones, IReadOnlyList<Utensilio> utensilios, string pase)
        => Apertura(instrucciones, utensilios, pase, soloCuandoSeLePide: false);

    public IEnumerable<string> Apertura(string instrucciones, IReadOnlyList<Utensilio> utensilios, string pase, bool soloCuandoSeLePide)
    {
        // El pase se ignora porque aquí no existe. No se disimula: `SabeVolver` ya lo dice, y quien
        // llama decide qué contar cuando se cae.
        yield return JsonSerializer.Serialize(new
        {
            type = "session.update",
            session = new
            {
                type = "realtime",
                model = Modelo,
                instructions = instrucciones,
                audio = new
                {
                    input = new
                    {
                        format = new { type = "audio/pcm", rate = RitmoDeEntrada },

                        // QUIÉN DECIDE QUE TERMINASTE DE HABLAR: el servidor, y por significado, no
                        // por silencio. `semantic_vad` mira lo que se está diciendo —si la frase
                        // suena acabada— en vez de cronometrar pausas.
                        //
                        // Esa es justo la avería de la que venimos: con un detector por silencio hay
                        // que elegir entre cortar a media frase o no contestar nunca, y las dos se
                        // sufrieron. `eagerness` queda en `auto` a propósito: es la perilla que hay
                        // que mover si contesta antes de tiempo (bájala a `low`) o si se hace de
                        // rogar (súbela a `high`), y ponerla ya en un extremo sin haberlo medido
                        // sería repetir el error de calibrar a ciegas.
                        // Y QUIÉN DECIDE QUE HAY QUE CONTESTAR: de normal, el propio detector. Con la
                        // voz prestada (promesa 192) nadie: se sigue oyendo y transcribiendo —el piloto
                        // lee lo que la persona contesta— pero solo se habla cuando la app pide turno.
                        turn_detection = new { type = "semantic_vad", eagerness = "auto", create_response = !soloCuandoSeLePide },

                        // La transcripción de lo que dice el usuario NO viene sola: hay que pedirla.
                        // Sin esto la carita se queda muda por su lado y no hay forma de leer en
                        // pantalla lo que el servidor creyó oír, que es la primera pista cuando algo
                        // no se entiende.
                        //
                        // gpt-transcribe, NO gpt-4o-mini-transcribe: el antiguo está deprecado y se
                        // apaga el 2027-02-26, y ese día la carita dejaría de escribir lo que oye sin
                        // un solo error. Medido el 2026-09-12 con la misma frase hablada: se acepta y
                        // manda la transcripción por TROZOS igual —9 deltas, la misma frase—, que es lo
                        // único que se lee abajo. Aceptarse no bastaba (promesa 43 de la voz).
                        transcription = new { model = "gpt-transcribe" },

                        // CAMPO LEJANO: el micrófono del portátil oye la sala entera —incluidos los
                        // altavoces— y no una boca pegada. `far_field` es el preprocesado que el
                        // propio servidor trae para ese escenario; sin él, lo que llega es la sala
                        // cruda y la transcripción alucina (2026-08-31: «дай видимо скуча» sobre
                        // una frase en español, y Ü se auto-silenció creyendo que se lo pedían).
                        noise_reduction = new { type = "far_field" },
                    },
                    output = new
                    {
                        format = new { type = "audio/pcm", rate = RitmoDeSalida },
                        voice = Voz,
                    },
                },
                tools = ComoFunciones(utensilios),
                tool_choice = "auto",
            },
        });
    }

    /// <summary>
    /// Las herramientas vestidas de <c>function</c>, con todos los argumentos de texto. En UN solo sitio
    /// porque GPT-Live las declara igual —dentro de su delegación— y dos copias de la misma forma
    /// divergen en silencio: el día que una cambiara, el delegado recibiría otra herramienta que la voz
    /// de respaldo, y ninguno de los dos daría error.
    /// </summary>
    internal static object[] ComoFunciones(IReadOnlyList<Utensilio> utensilios) => utensilios.Select(u => (object)new
    {
        type = "function",
        name = u.Nombre,
        description = u.Descripcion,
        parameters = new
        {
            type = "object",
            properties = u.Args.ToDictionary(
                a => a.Nombre, a => (object)new { type = "string", description = a.Que }),
            required = Array.Empty<string>(),
        },
    }).ToArray();

    public string Audio(byte[] pcm) => JsonSerializer.Serialize(new
    {
        type = "input_audio_buffer.append",
        audio = Convert.ToBase64String(pcm),
    });

    /// <summary>
    /// Una foto suelta, como un mensaje del usuario. Comprobado contra el servidor real: acepta
    /// <c>input_image</c> con la imagen como data URL en base64, dentro de un item de conversación
    /// normal — no hay un canal de vídeo aparte como en Gemini.
    /// </summary>
    public string Fotograma(byte[] jpeg) => JsonSerializer.Serialize(new
    {
        type = "conversation.item.create",
        item = new
        {
            type = "message",
            role = "user",
            content = new[] { new { type = "input_image", image_url = "data:image/jpeg;base64," + Convert.ToBase64String(jpeg) } },
        },
    });

    public string Texto(string texto) => JsonSerializer.Serialize(new
    {
        type = "conversation.item.create",
        item = new
        {
            type = "message",
            role = "user",
            content = new[] { new { type = "input_text", text = texto } },
        },
    });

    public IEnumerable<string> Resultados(IReadOnlyList<(string Id, string Nombre, string Resultado)> hechas)
    {
        foreach (var (id, _, resultado) in hechas)
            yield return JsonSerializer.Serialize(new
            {
                type = "conversation.item.create",
                item = new { type = "function_call_output", call_id = id, output = resultado },
            });
    }

    /// <summary>
    /// SIN ESTO EL MODELO SE QUEDA CON EL RESULTADO Y CALLADO. No es cortesía: es la diferencia
    /// entre que una herramienta sirva para algo o no. Quien llama manda esto UNA vez por tanda —
    /// mandarlo por cada resultado abriría varias respuestas a la vez y se pisarían.
    /// </summary>
    /// <remarks>
    /// SIN TEXTO ES LA DE SIEMPRE, y esa rama no se toca: pedir turno es lo que hace el resto de la
    /// conversación, y meterle instrucciones a todas convertiría cada respuesta en un dictado.
    ///
    /// CON TEXTO, Ü lo dice con SU voz. Nació el 2026-09-03: durante una comprobación quien decide
    /// qué se dice es el piloto —otro cerebro—, y el resumen salía por el sintetizador de Windows
    /// porque la voz viva estaba cerrada. El dueño lo oyó al instante: «habló con una voz diferente,
    /// como de Windows». Un asistente con dos voces no es un detalle de acabado: suena a otro.
    /// </remarks>
    public string PedirRespuesta(string instrucciones = "") =>
        string.IsNullOrWhiteSpace(instrucciones)
            ? JsonSerializer.Serialize(new { type = "response.create" })
            : JsonSerializer.Serialize(new
            {
                type = "response.create",
                // FUERA DE LA CONVERSACIÓN (2026-09-08): con el triage en el contexto, a «Di exactamente
                // esto: Temperatura.» la voz contestó «Glasgow, entre 3 y 15», y antepuso «Vale, déjame
                // pensar un momento…» a otra frase. Una respuesta sin conversación no tiene nada que la
                // tiente: solo la frase. El audio llega y suena igual.
                response = new { instructions = instrucciones, conversation = "none" },
            });

    public IReadOnlyList<Hecho> Leer(JsonElement m)
    {
        var hechos = new List<Hecho>();
        string tipo = m.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";

        switch (tipo)
        {
            case "response.output_audio.delta":
                if (m.TryGetProperty("delta", out var d) && d.GetString() is { Length: > 0 } b64)
                    hechos.Add(new Hecho.Suena(Convert.FromBase64String(b64)));
                break;

            case "response.output_audio_transcript.delta":
                if (m.TryGetProperty("delta", out var td) && td.GetString() is { Length: > 0 } suyo)
                    hechos.Add(new Hecho.DiceU(suyo));
                break;

            case "conversation.item.input_audio_transcription.delta":
                if (m.TryGetProperty("delta", out var ud) && ud.GetString() is { Length: > 0 } mio)
                    hechos.Add(new Hecho.DiceElUsuario(mio));
                break;

            // HABLÓ ENCIMA. Aquí el aviso es que EMPEZÓ a hablar, no que nos interrumpieron: el
            // servidor no manda un «interrumpido» como Gemini. Es el mismo hecho y hay que
            // atenderlo igual — callar lo que quedaba en la cola.
            case "input_audio_buffer.speech_started":
                hechos.Add(new Hecho.HablaronEncima());
                break;

            // LA LLAMADA LLEGA CUANDO SUS ARGUMENTOS ESTÁN COMPLETOS. Existe también un evento por
            // trozos (`.delta`) y no se atiende: con argumentos a medias no se puede ejecutar nada,
            // y juntarlos aquí sería llevar una cuenta que el servidor ya lleva.
            case "response.function_call_arguments.done":
                hechos.Add(new Hecho.Pide(new[] { LaLlamada(m) }));
                break;

            case "response.done":
                hechos.Add(new Hecho.CierraElTurno());
                if (m.TryGetProperty("response", out var r))
                {
                    if (r.TryGetProperty("usage", out var uso))
                        hechos.Add(new Hecho.Consumo(
                            Entero(uso, "input_tokens"), Entero(uso, "output_tokens"), Entero(uso, "total_tokens")));

                    // UNA RESPUESTA CANCELADA NO ES UN TURNO CERRADO CON LLAMADAS PENDIENTES: si el
                    // usuario habló encima, lo que el modelo hubiera pedido se retira. Contestarlo
                    // igual es lo que hacía que lo volviera a pedir en bucle.
                    if (r.TryGetProperty("status", out var st) && st.GetString() == "cancelled"
                        && r.TryGetProperty("output", out var salida) && salida.ValueKind == JsonValueKind.Array)
                    {
                        var ids = salida.EnumerateArray()
                            .Where(x => x.TryGetProperty("type", out var xt) && xt.GetString() == "function_call")
                            .Select(x => x.TryGetProperty("call_id", out var c) ? c.GetString() ?? "" : "")
                            .Where(x => x.Length > 0).ToList();
                        if (ids.Count > 0) hechos.Add(new Hecho.Retira(ids));
                    }
                }
                break;

            case "error":
                hechos.Add(new Hecho.Falla(m.TryGetProperty("error", out var e)
                    ? e.TryGetProperty("message", out var msg) ? msg.GetString() ?? e.GetRawText() : e.GetRawText()
                    : "error sin detalle"));
                break;
        }

        return hechos;
    }

    /// <summary>
    /// Una llamada leída de cualquier cosa con <c>call_id</c>, <c>name</c> y <c>arguments</c>: el evento
    /// de Realtime y el item function_call que GPT-Live manda envuelto tienen esos tres campos. Se
    /// comparte para que los argumentos se lean con la misma vara venga de quien venga.
    /// </summary>
    internal static Llamada LaLlamada(JsonElement m)
    {
        string id = m.TryGetProperty("call_id", out var c) ? c.GetString() ?? "" : "";
        string nombre = m.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        var args = new Dictionary<string, string>();

        // Los argumentos vienen como TEXTO con un JSON dentro, no como objeto. Es el pinchazo obvio
        // al traducir esto, y no da error: da un diccionario vacío y una herramienta que se ejecuta
        // sin parámetros, o sea que hace otra cosa.
        if (m.TryGetProperty("arguments", out var a) && a.GetString() is { Length: > 0 } crudo)
        {
            try
            {
                using var doc = JsonDocument.Parse(crudo);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    foreach (var p in doc.RootElement.EnumerateObject())
                        args[p.Name] = p.Value.ValueKind == JsonValueKind.String
                            ? p.Value.GetString() ?? "" : p.Value.ToString();
            }
            catch (JsonException) { /* argumentos ilegibles: mejor sin ellos que reventar el socket */ }
        }

        return new Llamada(id, nombre, args);
    }

    private static int Entero(JsonElement o, string campo)
        => o.TryGetProperty(campo, out var v) && v.TryGetInt32(out int n) ? n : 0;
}
