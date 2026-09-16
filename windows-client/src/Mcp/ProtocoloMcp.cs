using System.Text.Json;
using Voz.Realtime;

namespace U.WindowsClient.Mcp;

/// <summary>
/// EL PROTOCOLO MCP DE VERDAD: JSON-RPC 2.0 con initialize / tools/list / tools/call.
/// </summary>
/// <remarks>
/// Existe porque la sonda (8791) NO habla MCP —es un shim de desarrollo con {"tool","args"} a
/// pelo— y el Agent SDK habla MCP de verdad: descubierto al planificar F2, verificando en vez de
/// suponer (2026-08-24). Un cliente MCP genérico —el inspector oficial, el Agent SDK, cualquier
/// otro— tiene que poder listar y llamar nuestras herramientas SIN saber nada de U.
///
/// PURO A PROPÓSITO: entra un cuerpo JSON y sale la respuesta (o null si no toca responder). Ni
/// HTTP ni sockets aquí — eso vive en <see cref="ServidorMcp"/>. Es la misma separación de
/// siempre: lo que puede equivocarse en silencio (hablar mal el protocolo) se juzga sin levantar
/// nada; el cable es aparte.
///
/// SIN SESIONES, y es legal: la especificación dice que el servidor PUEDE asignar Mcp-Session-Id;
/// no asignarlo es ser un servidor sin estado, que es exactamente lo que somos — el estado del
/// terreno vive en el grafo, no en la conversación MCP.
/// </remarks>
public sealed class ProtocoloMcp
{
    private readonly IReadOnlyList<Utensilio> _catalogo;
    private readonly Func<string, IReadOnlyDictionary<string, string>, string> _ejecutar;

    public ProtocoloMcp(IReadOnlyList<Utensilio> catalogo,
        Func<string, IReadOnlyDictionary<string, string>, string> ejecutar)
    {
        _catalogo = catalogo;
        _ejecutar = ejecutar;
    }

    /// <summary>Cuánto puede tardar una herramienta antes de contestarle al cliente que no llegó.</summary>
    public TimeSpan TiempoMaximoDeHerramienta { get; init; } = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Atiende UN mensaje JSON-RPC. Devuelve el JSON de respuesta, o null si era una notificación
    /// (esas no se contestan: contestar a una notificación es hablar cuando nadie preguntó).
    /// </summary>
    public string? Atiende(string cuerpo)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(cuerpo); }
        catch
        {
            // El -32700 del estándar, no silencio ni prosa: el cliente genérico SABE leer ese código.
            return Error(null, -32700, "Parse error");
        }

        using (doc)
        {
            var raiz = doc.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object)
                return Error(null, -32600, "Invalid Request");

            // El id se conserva TAL CUAL (número, texto o null): es cómo el cliente casa pregunta y
            // respuesta, y reescribirlo asigna la respuesta a la petición equivocada.
            JsonElement? id = raiz.TryGetProperty("id", out var idEl) ? idEl.Clone() : null;
            string metodo = raiz.TryGetProperty("method", out var m) ? m.GetString() ?? "" : "";
            bool esNotificacion = id == null;

            switch (metodo)
            {
                case "initialize":
                    // Se acepta la versión que el cliente trae: contestar otra lo obliga a
                    // renegociar o a rendirse, y no dependemos de nada específico de una versión.
                    string version = raiz.TryGetProperty("params", out var pr)
                        && pr.TryGetProperty("protocolVersion", out var v)
                        ? v.GetString() ?? "2025-06-18" : "2025-06-18";
                    return Resultado(id, new
                    {
                        protocolVersion = version,
                        capabilities = new { tools = new { } },
                        serverInfo = new { name = "u-mapa", version = "1.0" },
                    });

                case "tools/list":
                    return Resultado(id, new { tools = _catalogo.Select(Publicado).ToArray() });

                case "tools/call":
                    return Llamada(id, raiz);

                // Las notificaciones del ciclo de vida se aceptan y no se contestan.
                case "notifications/initialized":
                case "notifications/cancelled":
                    return null;

                // ping es de las pocas cosas que TODO cliente manda para saber si seguimos aquí.
                case "ping":
                    return Resultado(id, new { });

                default:
                    return esNotificacion ? null : Error(id, -32601, $"Method not found: {metodo}");
            }
        }
    }

    private string? Llamada(JsonElement? id, JsonElement raiz)
    {
        if (!raiz.TryGetProperty("params", out var p) || !p.TryGetProperty("name", out var n))
            return Error(id, -32602, "Invalid params: falta name");
        string nombre = n.GetString() ?? "";

        // Solo lo publicado se despacha: ejecutar lo que no está en el catálogo sería un catálogo
        // de mentira — el cliente decide con la lista, y la lista tiene que ser la verdad entera.
        if (!_catalogo.Any(u => u.Nombre == nombre))
            return Error(id, -32602, $"Unknown tool: {nombre}");

        var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (p.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.Object)
            foreach (var campo in a.EnumerateObject())
                args[campo.Name] = campo.Value.ValueKind == JsonValueKind.String
                    ? campo.Value.GetString() ?? ""
                    // Un número o un objeto se aplana a texto, que es lo que nuestras herramientas
                    // hablan — la misma regla del catálogo de la voz.
                    : campo.Value.GetRawText();

        // UNA HERRAMIENTA COLGADA NO CUELGA LA PUERTA. map_what_i_see se quedó 1014 segundos sin
        // contestar y todo lo que llegó detrás murió en cadena — «The operation timed out» tras
        // «The operation timed out», y la tarea entera perdida (2026-08-25, revancha del piloto).
        // Pasado el plazo se contesta ERROR con el porqué; la llamada huérfana puede seguir
        // corriendo por debajo, y se dice, porque fingir que se canceló sería mentir.
        string texto;
        bool fallo = false;
        var trabajo = System.Threading.Tasks.Task.Run(() =>
        {
            try { return (Texto: _ejecutar(nombre, args), Fallo: false); }
            catch (Exception e) { return (Texto: $"la herramienta reventó: {e.Message}", Fallo: true); }
        });
        if (trabajo.Wait(TiempoMaximoDeHerramienta))
        {
            (texto, fallo) = trabajo.Result;
        }
        else
        {
            texto = $"«{nombre}» no contestó en {TiempoMaximoDeHerramienta.TotalSeconds:0} s y suelto "
                  + "la llamada para no colgar la puerta. Puede seguir corriendo por debajo: mira el "
                  + "estado con map_where_am_i antes de repetirla.";
            fallo = true;
        }

        // Lo que la herramienta contestó vuelve TAL CUAL: nuestras herramientas hablan prosa con
        // el porqué dentro, y resumirla le quitaría al modelo justo la pista que necesita.
        //
        // SALVO SI ES UNA IMAGEN (promesa 181). `map_shot` devuelve «data:image/png;base64,…» y esto
        // lo empaquetaba como TEXTO: al modelo le llegaba un chorro de base64 que no puede mirar, así
        // que nunca miraba. El 2026-09-07 el dueño lo vio de golpe —«con otro screenshot de lo actual
        // puedes determinar si está bien o mal e iterar rápidamente»— después de que yo seleccionara
        // una fila de paciente CORRECTAMENTE y no tuviera forma de saberlo: la URL no cambia al
        // seleccionar, así que el juez por pantalla es ciego a media SAP. Ver sí lo ve.
        return Resultado(id, new
        {
            content = ComoContenido(texto),
            isError = fallo,
        });
    }

    /// <summary>
    /// Lo que contestó una herramienta, en bloques de contenido MCP: una imagen como imagen, y todo
    /// lo demás como texto. Promesa 181.
    /// </summary>
    /// <remarks>
    /// MCP tiene un bloque `image` con `data` (base64 pelado) y `mimeType`; el data URI es cosa del
    /// navegador. Se parte por la coma: delante el tipo, detrás los datos. Si el data URI viene roto
    /// —sin coma, sin base64— vuelve como texto: un bloque de imagen con datos que no son una imagen
    /// rompe la petición ENTERA del modelo, y perder la respuesta es peor que verla en prosa.
    /// </remarks>
    public static object[] ComoContenido(string texto)
    {
        string t = texto ?? "";
        if (t.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            int coma = t.IndexOf(',');
            if (coma > 0)
            {
                string cabecera = t[5..coma];                      // «image/png;base64»
                string datos = t[(coma + 1)..];
                int puntoYComa = cabecera.IndexOf(';');
                string tipo = puntoYComa > 0 ? cabecera[..puntoYComa] : cabecera;
                if (cabecera.Contains("base64", StringComparison.OrdinalIgnoreCase) && datos.Length > 0)
                    return new object[] { new { type = "image", data = datos, mimeType = tipo } };
            }
        }
        return new object[] { new { type = "text", text = t } };
    }

    /// <summary>Un utensilio del catálogo, dicho como MCP lo pide: name, description, inputSchema.</summary>
    private static object Publicado(Utensilio u) => new
    {
        name = u.Nombre,
        description = u.Descripcion,
        inputSchema = new
        {
            type = "object",
            properties = u.Args.ToDictionary(
                a => a.Nombre,
                a => (object)new { type = "string", description = a.Que }),
        },
    };

    private static string Resultado(JsonElement? id, object resultado) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = resultado,
        });

    private static string Error(JsonElement? id, int codigo, string mensaje) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new { code = codigo, message = mensaje },
        });
}
