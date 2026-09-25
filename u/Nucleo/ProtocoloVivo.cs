using System.Text.Json;

namespace U.Ciclo;

/// <summary>Una llamada de Luna a una herramienta, ya terminada.</summary>
public sealed record LlamadaDeLuna(string CallId, string Nombre, string Argumentos);

/// <summary>
/// EL PROTOCOLO DE GPT-LIVE, lo mínimo (promesa 448). Todo lo que se sabe de él lo midió main contra el
/// servidor real el 2026-09-12 (voz/Realtime/ProtocoloGptLive.cs) y aquí se conserva el concepto, no el código:
///
///   · Su puerta es /v1/live/sessions y se abre con session.start (en /v1/realtime contesta «not supported»).
///   · La voz no lleva herramientas: las lleva la DELEGADA —Luna— en session.delegation.responses. Esa es
///     justo la arquitectura pedida: la voz conversa, Luna planea, Jev ejecuta.
///   · Una llamada llega TRES veces (added / arguments.done / item.done); solo la terminada se atiende.
///   · El resultado va como function_call_output con su call_id, y después hay que pedir turno.
/// </summary>
public static class ProtocoloVivo
{
    public const string ModeloDeVoz = "gpt-live-1";
    public const string Luna = "gpt-5.6-luna";
    public const string Voz = "marin";
    public const int Ritmo = 24000;

    public static Uri Direccion() => new("wss://api.openai.com/v1/live/sessions");

    /// <summary>La voz: corta, y que delegue. Sin ella, la voz promete lo que solo la delegada puede hacer.</summary>
    public const string InstruccionesDeLaVoz =
        "Eres Ü, un asistente que vive en la pantalla de esta persona y usa su computador por ella, con el ratón "
        + "y el teclado. Hablas en español, con frases cortas y naturales. Tú no ves la pantalla ni la tocas: "
        + "todo lo que sea mirar, abrir, pulsar, escribir u operar el computador lo delegas siempre, y después "
        + "cuentas lo que salió, en pasado y en una frase. No anuncies lo que vas a hacer. Nunca inventes lo que hay en pantalla.";

    /// <summary>Luna: planea con lo que hay en pantalla y le pasa el plan al ejecutor.</summary>
    public const string InstruccionesDeLuna =
        "Eres Luna, la que planea para Ü. Ü controla el computador Windows de la persona con el ratón y el teclado reales.\n"
        + "Para actuar llamas a «hacer» con una lista de pasos cortos, en orden. Cada paso es UNO de estos:\n"
        + "  «abre: <app>» abre una aplicación (notepad, configuración, explorador, calculadora, chrome, edge, paint) o una dirección que Windows sepa abrir —ms-settings:bluetooth, ms-settings:personalization-background, https://…—: es el camino más corto a una sección de Configuración.\n"
        + "  «escribe: <texto>» escribe el texto donde esté el foco.\n"
        + "  «tecla: <tecla>» pulsa una tecla o combinación: Enter, Escape, Tab, Ctrl+S, Alt+F4, F5.\n"
        + "  cualquier otra frase es un OBJETIVO en la pantalla («abrir el menú Archivo», «ir a Bluetooth y dispositivos»): "
        + "el ejecutor mira los botones que hay y pulsa hasta cumplirlo.\n"
        + "Reglas: un objetivo por pantalla y UNA intención por objetivo, aunque necesite varios clics («calcular 12 por 3 con los botones», no un paso por botón; pero «borrar» y «calcular» son dos objetivos): el ejecutor encadena los clics y comprueba solo; "
        + "si el pedido trae lo que hay delante, planea con eso sin volver a mirar; si no sabes qué hay delante, llama a «mirar». Si hace falta escribir en un campo, primero un objetivo "
        + "que ponga el foco en ese campo y después «escribe:». "
        + "Si «hacer» dice que un paso falló, mira y vuelve a planear desde donde quedó; no repitas lo ya hecho. "
        + "Si la app acepta teclado para lo que se pide (números en una calculadora, texto en un buscador), prefiere «escribe:» a pulsar botón por botón: es exacto y no hace dudar al ejecutor. "
        + "Nada irreversible (enviar, borrar, pagar) sin que la persona lo haya pedido explícitamente.";

    public static string Apertura(string instruccionesDeLuna) => JsonSerializer.Serialize(new
    {
        type = "session.start",
        session = new
        {
            model = ModeloDeVoz,
            instructions = InstruccionesDeLaVoz,
            audio = new { format = new { type = "audio/pcm", rate = Ritmo }, output = new { voice = Voz } },
            delegation = new
            {
                type = "responses",
                responses = new
                {
                    model = Luna,
                    instructions = string.IsNullOrWhiteSpace(instruccionesDeLuna) ? InstruccionesDeLuna : instruccionesDeLuna,
                    tools = Herramientas(),
                    tool_choice = "auto",
                },
            },
        },
    });

    /// <summary>Las dos herramientas de Luna, en el formato de la Responses API.</summary>
    public static object[] Herramientas() => new object[]
    {
        new
        {
            type = "function", name = "hacer",
            description = "Ejecuta un plan en el computador, paso a paso, y devuelve cómo acabó cada paso.",
            parameters = new
            {
                type = "object",
                properties = new { pasos = new { type = "array", items = new { type = "string" }, description = "Los pasos, en orden." } },
                required = new[] { "pasos" },
            },
        },
        new
        {
            type = "function", name = "mirar",
            description = "Qué ventana está delante, qué dice y qué se puede pulsar en ella ahora mismo.",
            parameters = new { type = "object", properties = new { } },
        },
    };

    /// <summary>Un evento del servidor → la llamada terminada, o nada. Solo response.output_item.done cuenta.</summary>
    public static LlamadaDeLuna? Llamada(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json);
            var r = d.RootElement;
            if (Texto(r, "type") != "response.event" || !r.TryGetProperty("event", out var ev)) return null;
            if (Texto(ev, "type") != "response.output_item.done" || !ev.TryGetProperty("item", out var item)) return null;
            if (Texto(item, "type") != "function_call") return null;
            string id = Texto(item, "call_id");
            return id.Length == 0 ? null : new LlamadaDeLuna(id, Texto(item, "name"), Texto(item, "arguments"));
        }
        catch (JsonException) { return null; }
    }

    /// <summary>El resultado de una llamada, con su call_id, y detrás el pedido de turno.</summary>
    public static IReadOnlyList<string> Resultado(string callId, string salida) => new[]
    {
        JsonSerializer.Serialize(new
        {
            type = "response.item.create",
            item = new { type = "function_call_output", call_id = callId, output = ParaLuna.Recortar(salida) },
        }),
        JsonSerializer.Serialize(new { type = "response.create" }),
    };

    public static string Audio(ReadOnlySpan<byte> pcm) => JsonSerializer.Serialize(new
    {
        type = "session.input_audio.append",
        audio = Convert.ToBase64String(pcm),
    });

    /// <summary>Una frase escrita, y el turno pedido detrás (sin él, el servidor la acepta y no contesta).</summary>
    public static IReadOnlyList<string> TextoDelUsuario(string texto) => new[]
    {
        JsonSerializer.Serialize(new
        {
            type = "response.item.create",
            item = new { type = "message", role = "user", content = new[] { new { type = "input_text", text = texto } } },
        }),
        JsonSerializer.Serialize(new { type = "response.create" }),
    };

    public static string Texto(JsonElement o, string campo) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
