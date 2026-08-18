using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Radicacion;

/// <summary>Lo que el LLM extrajo del documento. Todo string es "" y no null cuando no aplica.</summary>
public sealed class DocumentoClasificado
{
    [JsonPropertyName("tipoDocumento")] public string TipoDocumento { get; set; } = "";
    [JsonPropertyName("remitente")] public string Remitente { get; set; } = "";
    [JsonPropertyName("cedulaONit")] public string CedulaONit { get; set; } = "";
    [JsonPropertyName("afiliadoIpsEmpleador")] public string AfiliadoIpsEmpleador { get; set; } = "";
    [JsonPropertyName("fechaDocumento")] public string FechaDocumento { get; set; } = "";
    [JsonPropertyName("asunto")] public string Asunto { get; set; } = "";
    [JsonPropertyName("datosContacto")] public string DatosContacto { get; set; } = "";
    [JsonPropertyName("numeroReferido")] public string NumeroReferido { get; set; } = "";
    [JsonPropertyName("anexosCompletos")] public bool AnexosCompletos { get; set; } = true;
    [JsonPropertyName("anexosFaltantes")] public List<string> AnexosFaltantes { get; set; } = new();
    [JsonPropertyName("advertencias")] public List<string> Advertencias { get; set; } = new();
    [JsonPropertyName("resumen")] public string Resumen { get; set; } = "";
    [JsonPropertyName("areaSugerida")] public string AreaSugerida { get; set; } = "otro";
}

/// <summary>
/// Manda el documento a un LLM y pide de vuelta el JSON estructurado que necesita la radicación.
///
/// OPENAI PRIMERO, GEMINI DE RESPALDO. Se probó en vivo el 2026-08-17: gemini-flash-latest respondía
/// 503 «high demand» de forma sostenida (confirmado con una llamada directa, no solo desde la app) y
/// además tardaba mucho más que OpenAI incluso cuando respondía — sin cambiar el orden, cada análisis
/// hacía esperar la demo. Gemini reintenta poco si hay respaldo (2 veces, backoff corto): no tiene
/// sentido esperar 90 s a algo que ya se demostró lento/caído. Sin OPENAI_API_KEY, Gemini vuelve a
/// ser el único camino y reintenta las 4 veces de siempre.
/// </summary>
public static class ClasificadorDocumento
{
    // Alias "-latest" y no una versión fija: gemini-2.0-flash ya se retiró (2026-08), y clavar un
    // número concreto aquí es exactamente el mismo error un modelo después.
    private const string ModeloGemini = "gemini-flash-latest";
    private const string ModeloOpenAiDefecto = "gpt-5.4-mini";

    public static string ClaveGemini() => Environment.GetEnvironmentVariable("GEMINI_API_KEY")?.Trim() ?? "";
    public static string ClaveOpenAi() => Environment.GetEnvironmentVariable("OPENAI_API_KEY")?.Trim() ?? "";
    private static string ModeloOpenAi() =>
        Environment.GetEnvironmentVariable("OPENAI_MODEL")?.Trim() is { Length: > 0 } m ? m : ModeloOpenAiDefecto;

    public static async Task<DocumentoClasificado> ClasificarAsync(
        byte[] bytes, string mimeType, IReadOnlyList<AreaDestino> areas, CancellationToken ct)
    {
        string claveGemini = ClaveGemini();
        string claveOpenAi = ClaveOpenAi();
        if (claveGemini.Length == 0 && claveOpenAi.Length == 0)
            throw new InvalidOperationException("falta GEMINI_API_KEY u OPENAI_API_KEY en el entorno — no se puede clasificar el documento");

        string instruccion = Instruccion(areas);
        string base64 = Convert.ToBase64String(bytes);

        // OPENAI PRIMERO: probado en vivo el 2026-08-17, responde mucho más rápido y sin los 503 de
        // «alta demanda» que dio Gemini de forma sostenida. Gemini queda de respaldo, no al revés.
        string? textoJson = null;
        Exception? errorOpenAi = null;

        if (claveOpenAi.Length > 0)
        {
            try { textoJson = await LlamarOpenAiAsync(claveOpenAi, ModeloOpenAi(), instruccion, mimeType, base64, ct); }
            catch (Exception e)
            {
                errorOpenAi = e;
                LogBus.Log("radicacion", $"OpenAI falló ({e.Message}); {(claveGemini.Length > 0 ? "paso a Gemini" : "sin respaldo configurado")}");
            }
        }

        if (textoJson == null && claveGemini.Length > 0)
        {
            int intentos = claveOpenAi.Length > 0 ? 2 : 4; // con respaldo, no vale la pena esperar 90s a algo que ya falló
            try { textoJson = await LlamarGeminiAsync(claveGemini, instruccion, mimeType, base64, intentos, ct); }
            catch (Exception e)
            {
                throw new InvalidOperationException(
                    errorOpenAi != null
                        ? $"OpenAI falló ({errorOpenAi.Message}) y Gemini también falló ({e.Message})"
                        : $"Gemini falló: {e.Message}");
            }
        }

        if (textoJson == null)
            throw errorOpenAi ?? new InvalidOperationException("no se pudo clasificar el documento");

        var resultado = JsonSerializer.Deserialize<DocumentoClasificado>(textoJson) ?? new DocumentoClasificado();

        // El modelo pudo sugerir una clave que no existe en la config actual (áreas editadas a mano
        // entre corridas): si no calza con ninguna área real, se cae a "otro" para no enrutar a la nada.
        if (!areas.Any(a => a.Clave == resultado.AreaSugerida))
            resultado.AreaSugerida = "otro";

        return resultado;
    }

    private static string Instruccion(IReadOnlyList<AreaDestino> areas)
    {
        string clavesAreas = string.Join(", ", areas.Select(a => $"\"{a.Clave}\" ({a.Nombre})"));
        return $$"""
            Eres el asistente de radicación de un portal institucional de salud (EPS/IPS). Vas a leer
            UN documento (PDF, foto o escaneo) que una funcionaria acaba de recibir y radicar. Extrae
            los campos que puedas identificar con certeza; si un campo no aparece en el documento,
            devuélvelo como cadena vacía "" — NUNCA inventes un valor.

            Devuelve exactamente este JSON, sin texto alrededor, sin bloque de código:
            {
              "tipoDocumento": "ej. PQR, incapacidad médica, licencia de maternidad, factura, solicitud de prestador, contrato, otro",
              "remitente": "nombre de la persona o entidad que envía",
              "cedulaONit": "",
              "afiliadoIpsEmpleador": "nombre del afiliado, IPS o empleador relacionado, si aparece",
              "fechaDocumento": "fecha del documento en el formato en que aparezca",
              "asunto": "de qué trata en una frase",
              "datosContacto": "teléfono/correo si aparecen",
              "numeroReferido": "número de factura, incapacidad, contrato o solicitud si el documento trae uno",
              "anexosCompletos": true o false — ¿el documento parece traer todos los anexos que menciona?,
              "anexosFaltantes": ["lista de anexos que el propio documento menciona pero no vienen"],
              "advertencias": ["ilegible", "incompleto", "vencido", "posible duplicado" — solo las que apliquen, puede quedar vacía],
              "resumen": "dos o tres frases que resuman el documento para que la funcionaria lo revise rápido",
              "areaSugerida": "UNA de estas claves exactas, la que mejor corresponda: {{clavesAreas}} — si ninguna calza bien, usa \"otro\""
            }
            """;
    }

    private static async Task<string> LlamarGeminiAsync(
        string clave, string instruccion, string mimeType, string base64, int maxIntentos, CancellationToken ct)
    {
        var cuerpo = new
        {
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new object[]
                    {
                        new { text = instruccion },
                        new { inline_data = new { mime_type = mimeType, data = base64 } },
                    },
                },
            },
            generationConfig = new { responseMimeType = "application/json", temperature = 0.0 },
        };

        using var http = RedResiliente.ClienteHttp(TimeSpan.FromSeconds(90));
        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{ModeloGemini}:generateContent?key={Uri.EscapeDataString(clave)}";
        string json = JsonSerializer.Serialize(cuerpo);

        string texto = "";
        for (int intento = 1; intento <= maxIntentos; intento++)
        {
            using var contenido = new StringContent(json, Encoding.UTF8, "application/json");
            HttpResponseMessage r;
            try { r = await http.PostAsync(url, contenido, ct); }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                if (intento == maxIntentos) throw new InvalidOperationException($"no se pudo llegar a Gemini: {e.Message}");
                int esperaRed = 1000 * (1 << (intento - 1));
                LogBus.Log("radicacion", $"la red falló ({e.GetType().Name}); reintento {intento + 1} de {maxIntentos} en {esperaRed / 1000}s");
                await Task.Delay(esperaRed, ct);
                continue;
            }

            texto = await r.Content.ReadAsStringAsync(ct);
            if (r.IsSuccessStatusCode) break;

            int codigo = (int)r.StatusCode;
            bool vuelveAIntentarse = codigo is 429 or 500 or 502 or 503 or 504;
            if (!vuelveAIntentarse || intento == maxIntentos)
                throw new InvalidOperationException($"Gemini respondió {codigo}: {Recorta(texto)}");

            int esperaMs = 1000 * (1 << (intento - 1));
            LogBus.Log("radicacion", $"Gemini dijo {codigo}; reintento {intento + 1} de {maxIntentos} en {esperaMs / 1000}s");
            await Task.Delay(esperaMs, ct);
        }

        using var doc = JsonDocument.Parse(texto);
        return doc.RootElement.GetProperty("candidates")[0]
            .GetProperty("content").GetProperty("parts")[0]
            .GetProperty("text").GetString() ?? "{}";
    }

    /// <summary>
    /// Respaldo por la Responses API de OpenAI (POST /v1/responses), que acepta PDF directo por
    /// input_file (file_data en base64) — probado en vivo el 2026-08-17 contra un documento real.
    /// </summary>
    private static async Task<string> LlamarOpenAiAsync(
        string clave, string modelo, string instruccion, string mimeType, string base64, CancellationToken ct)
    {
        object contenidoDocumento = mimeType == "application/pdf"
            ? new { type = "input_file", filename = "documento.pdf", file_data = $"data:{mimeType};base64,{base64}" }
            : new { type = "input_image", image_url = $"data:{mimeType};base64,{base64}" };

        var cuerpo = new
        {
            model = modelo,
            input = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "input_text", text = instruccion },
                        contenidoDocumento,
                    },
                },
            },
        };

        using var http = RedResiliente.ClienteHttp(TimeSpan.FromSeconds(90));
        using var mensaje = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses")
        {
            Content = new StringContent(JsonSerializer.Serialize(cuerpo), Encoding.UTF8, "application/json"),
        };
        mensaje.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", clave);

        HttpResponseMessage r;
        try { r = await http.SendAsync(mensaje, ct); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new InvalidOperationException($"no se pudo llegar a OpenAI: {e.Message}");
        }

        string texto = await r.Content.ReadAsStringAsync(ct);
        if (!r.IsSuccessStatusCode)
            throw new InvalidOperationException($"OpenAI respondió {(int)r.StatusCode}: {Recorta(texto)}");

        using var doc = JsonDocument.Parse(texto);
        foreach (var salida in doc.RootElement.GetProperty("output").EnumerateArray())
        {
            if (salida.GetProperty("type").GetString() != "message") continue;
            foreach (var parte in salida.GetProperty("content").EnumerateArray())
            {
                if (parte.TryGetProperty("text", out var t)) return LimpiarBloqueDeCodigo(t.GetString() ?? "{}");
            }
        }
        throw new InvalidOperationException($"OpenAI no devolvió texto legible: {Recorta(texto)}");
    }

    /// <summary>Por si el modelo envuelve el JSON en ```json ... ``` a pesar de que se le pidió que no lo hiciera.</summary>
    private static string LimpiarBloqueDeCodigo(string s)
    {
        s = s.Trim();
        if (!s.StartsWith("```")) return s;
        int inicio = s.IndexOf('\n') + 1;
        int fin = s.LastIndexOf("```", StringComparison.Ordinal);
        return fin > inicio ? s[inicio..fin].Trim() : s;
    }

    private static string Recorta(string s) => s.Length <= 300 ? s : s[..300] + "…";
}
