using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace U.Graph.Clinical;

/// <summary>
/// Falla del carril clínico, con el CÓDIGO estable que Graph devuelve en
/// <c>error.code</c>. El código importa más que el mensaje: <c>DEVICE_NOT_PAIRED</c>
/// es «muestra el código de emparejamiento», no un error a reintentar.
/// </summary>
public sealed class ClinicalApiException : Exception
{
    public string Code { get; }
    public int StatusCode { get; }

    public ClinicalApiException(string code, string message, int statusCode = 0)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    /// <summary>¿Puede salir bien al reintentar? (pasarela, arranque en frío, timeout).</summary>
    public bool Transient => StatusCode is 0 or 408 or 429 or 502 or 503 or 504;
}

/// <summary>
/// El puente con el carril clínico de Graph: enrolamiento del aparato
/// (/api/v1/enroll con la key embebida de bajo privilegio), código de
/// emparejamiento (/api/v1/devices/pair-code con el token per-install) y las
/// rutas /api/clinical/* con las que el aparato trabaja las consultas EN NOMBRE
/// del médico vinculado.
///
/// Es un cliente distinto de <see cref="GraphClient"/> a propósito: aquel habla
/// el contrato /api/v1 (errores <c>{"error":"texto"}</c>); el carril clínico
/// tiene su propia forma de error (<c>{"error":{"code","message"}}</c>) y su
/// propia credencial (el token per-install, JAMÁS la key compartida de env —
/// Graph la rechaza como actor clínico por diseño).
///
/// PHI: este cliente NUNCA loguea transcripciones ni cuerpos de nota. Ids y
/// status, nada más — la misma regla que ClinicalNoteGeneratorService en Graph.
/// </summary>
public sealed class ClinicalApiClient
{
    private readonly HttpClient _http;
    private readonly GraphConfig _config;

    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public ClinicalApiClient(GraphConfig config, HttpClient? http = null)
    {
        _config = config;
        // 100 s de techo del socket; el timeout REAL de generate-note (75 s) lo pone
        // el CancellationToken enlazado del llamador, para poder distinguir «tardó»
        // (→ sondear el estado) de «falló».
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(100) };
    }

    private string Url(string path) => $"{_config.BaseUrl.TrimEnd('/')}{path}";
    private static string Escape(string s) => Uri.EscapeDataString(s ?? "");

    // ── Identidad del aparato ────────────────────────────────────────────────

    /// <summary>¿Hay token per-install guardado? Sin él, primero <see cref="EnrollAsync"/>.</summary>
    public bool IsEnrolled => !string.IsNullOrWhiteSpace(_config.GetDeviceToken());

    /// <summary>
    /// Da de alta esta instalación con la key de ENROLAMIENTO (la embebida en el
    /// build / graph.json — lo único que esa key puede hacer). Guarda el token
    /// per-install de una vez (DPAPI): Graph no lo vuelve a enseñar.
    /// </summary>
    public async Task<EnrolledDevice> EnrollAsync(string label, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_config.ApiKey))
            throw new ClinicalApiException("ENROLL_FORBIDDEN", "No hay key de enrolamiento (graph.json / GRAPH_API_KEY).");
        var body = new { device_id = _config.EnsureDeviceId(), label };
        var res = await SendAsync<EnrollResponse>(
            HttpMethod.Post, "/api/v1/enroll", body, apiKey: _config.ApiKey, ct);
        if (string.IsNullOrWhiteSpace(res.Token))
            throw new ClinicalApiException("ENROLL_FORBIDDEN", "Graph no devolvió el token del dispositivo.");
        _config.SetDeviceToken(res.Token);
        return res.Device ?? new EnrolledDevice { DeviceId = _config.EnsureDeviceId(), Label = label };
    }

    /// <summary>
    /// Pide el código de emparejamiento para mostrarlo en pantalla. El médico lo
    /// canjea en Miracle Notes → Equipos. Dura 10 minutos, un solo uso.
    /// </summary>
    public Task<PairCodeResponse> PairCodeAsync(CancellationToken ct) =>
        SendAsync<PairCodeResponse>(HttpMethod.Post, "/api/v1/devices/pair-code", new { }, DeviceToken(), ct);

    // ── Plantillas ───────────────────────────────────────────────────────────

    public async Task<List<ClinicalTemplateInfo>> ListTemplatesAsync(string specialty, CancellationToken ct)
    {
        string query = string.IsNullOrWhiteSpace(specialty) ? "" : $"?specialty={Escape(specialty)}";
        var res = await SendAsync<TemplatesResponse>(HttpMethod.Get, $"/api/clinical/templates{query}", null, DeviceToken(), ct);
        return res.Templates;
    }

    public async Task<ClinicalTemplateInfo> CreateTemplateAsync(
        string name, string specialty, IReadOnlyList<string> sections, CancellationToken ct)
    {
        var body = new { name, specialty, sections };
        var res = await SendAsync<TemplateResponseEnvelope>(HttpMethod.Post, "/api/clinical/templates", body, DeviceToken(), ct);
        return res.Template ?? throw new ClinicalApiException("TEMPLATE_INVALID", "Graph no devolvió la plantilla creada.");
    }

    // ── Consultas (encounters) ───────────────────────────────────────────────

    public Task<CreateEncounterResponse> CreateEncounterAsync(string templateId, string consultationType, CancellationToken ct) =>
        SendAsync<CreateEncounterResponse>(HttpMethod.Post, "/api/clinical/encounters",
            new { template_id = templateId, consultation_type = consultationType }, DeviceToken(), ct);

    public async Task<ClinicalEncounterInfo> GetEncounterAsync(string encounterId, CancellationToken ct)
    {
        var res = await SendAsync<EncounterEnvelope>(
            HttpMethod.Get, $"/api/clinical/encounters/{Escape(encounterId)}", null, DeviceToken(), ct);
        return res.Encounter ?? throw new ClinicalApiException("ENCOUNTER_NOT_FOUND", "Graph no devolvió la consulta.");
    }

    /// <summary>REEMPLAZA la transcripción (semántica del backend). El append lo compone el llamador.</summary>
    public Task<TranscriptSaveResponse> SaveTranscriptAsync(string encounterId, string transcript, CancellationToken ct) =>
        SendAsync<TranscriptSaveResponse>(HttpMethod.Post,
            $"/api/clinical/encounters/{Escape(encounterId)}/transcript", new { transcript }, DeviceToken(), ct);

    /// <summary>
    /// Síncrono en el backend (espera al LLM). Si el CancellationToken del llamador
    /// corta antes, el encounter queda en note_generating y el RESCATE de Graph lo
    /// termina — el llamador sondea <see cref="GetEncounterAsync"/> (y ese mismo
    /// tráfico sobre /api/clinical dispara el rescate oportunista).
    /// </summary>
    public Task<NoteResponse> GenerateNoteAsync(string encounterId, CancellationToken ct) =>
        SendAsync<NoteResponse>(HttpMethod.Post,
            $"/api/clinical/encounters/{Escape(encounterId)}/generate-note", new { }, DeviceToken(), ct);

    public Task<NoteResponse> SaveNoteAsync(string encounterId, JsonElement noteJson, CancellationToken ct) =>
        SendAsync<NoteResponse>(HttpMethod.Put,
            $"/api/clinical/encounters/{Escape(encounterId)}/note", new { note_json = noteJson }, DeviceToken(), ct);

    /// <summary>Propone el ajuste; NO persiste. Aplicar con <see cref="SaveNoteAsync"/>.</summary>
    public Task<AdjustNoteResponse> AdjustNoteAsync(
        string encounterId, string instruction, string sectionKey, CancellationToken ct) =>
        SendAsync<AdjustNoteResponse>(HttpMethod.Post, "/api/clinical/assistant/note-adjustment",
            new { encounter_id = encounterId, instruction, section_key = string.IsNullOrWhiteSpace(sectionKey) ? null : sectionKey },
            DeviceToken(), ct);

    // ── Historial ────────────────────────────────────────────────────────────

    public async Task<List<ConsultationSummary>> ListConsultationsAsync(
        string estado, string desde, string hasta, CancellationToken ct)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(estado)) parts.Add($"estado={Escape(estado)}");
        if (!string.IsNullOrWhiteSpace(desde)) parts.Add($"desde={Escape(desde)}");
        if (!string.IsNullOrWhiteSpace(hasta)) parts.Add($"hasta={Escape(hasta)}");
        string query = parts.Count > 0 ? "?" + string.Join("&", parts) : "";
        var res = await SendAsync<ConsultationsResponse>(
            HttpMethod.Get, $"/api/clinical/consultations{query}", null, DeviceToken(), ct);
        return res.Consultations;
    }

    // ── HTTP ─────────────────────────────────────────────────────────────────

    private string DeviceToken()
    {
        string? token = _config.GetDeviceToken();
        if (string.IsNullOrWhiteSpace(token))
            throw new ClinicalApiException("DEVICE_NOT_ENROLLED",
                "Este equipo no está enrolado en Graph todavía (no hay token per-install).");
        return token;
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method, string path, object? body, string apiKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_config.BaseUrl))
            throw new ClinicalApiException("SUPABASE_NOT_CONFIGURED", "Graph no está configurado (falta la URL).");

        using var req = new HttpRequestMessage(method, Url(path));
        // La credencial va POR PETICIÓN, no en DefaultRequestHeaders: el enroll usa
        // la key de enrolamiento y todo lo demás el token per-install, y un cliente
        // compartido no puede quedarse pegado a la primera que pasó.
        req.Headers.Add("X-API-Key", apiKey);
        if (body != null)
            req.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");

        HttpResponseMessage res;
        try
        {
            res = await _http.SendAsync(req, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e)
        {
            throw new ClinicalApiException("NETWORK_ERROR", $"No se pudo contactar con Graph: {e.Message}");
        }

        using (res)
        {
            string text = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
                throw ErrorFrom(text, (int)res.StatusCode);
            try
            {
                var parsed = JsonSerializer.Deserialize<T>(text, Json);
                if (parsed == null) throw new ClinicalApiException("INTERNAL_ERROR", "Graph devolvió una respuesta vacía.");
                return parsed;
            }
            catch (JsonException e)
            {
                throw new ClinicalApiException("INTERNAL_ERROR", $"Graph devolvió un JSON inesperado: {e.Message}");
            }
        }
    }

    /// <summary>
    /// El carril clínico reporta <c>{"error":{"code","message"}}</c>; /api/v1
    /// reporta <c>{"error":"texto"}</c>. Se leen las dos formas — el código
    /// estable es lo que permite reaccionar (DEVICE_NOT_PAIRED → emparejar).
    /// </summary>
    private static ClinicalApiException ErrorFrom(string bodyText, int status)
    {
        try
        {
            using var doc = JsonDocument.Parse(bodyText);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.ValueKind == JsonValueKind.Object)
                {
                    string code = err.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
                    string message = err.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
                    return new ClinicalApiException(
                        string.IsNullOrWhiteSpace(code) ? "INTERNAL_ERROR" : code,
                        string.IsNullOrWhiteSpace(message) ? $"Graph respondió HTTP {status}." : message,
                        status);
                }
                if (err.ValueKind == JsonValueKind.String)
                    return new ClinicalApiException(status == 401 ? "UNAUTHORIZED" : "INTERNAL_ERROR", err.GetString() ?? "", status);
            }
        }
        catch { /* no era JSON */ }
        return new ClinicalApiException("INTERNAL_ERROR", $"Graph respondió HTTP {status}.", status);
    }
}
