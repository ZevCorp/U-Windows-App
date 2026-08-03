using System.Net.Http;
using System.Text;
using System.Text.Json;
using U.Graph.NoteExport;

namespace U.Graph;

/// <summary>Falla del backend Graph, ya con el mensaje que Graph devolvió en <c>error</c>.</summary>
public class GraphException : Exception
{
    public int StatusCode { get; }
    public GraphException(string message, int statusCode = 0) : base(message) => StatusCode = statusCode;

    /// <summary>
    /// ¿El fallo es de los que pueden salir bien al reintentar? 502/503/504 son la pasarela o el
    /// arranque en frío del serverless; 408 y 0 (timeout del cliente) son la espera. Un 4xx de verdad
    /// —400, 401, 404— no se reintenta: reintentar un error de contrato solo lo repite.
    /// </summary>
    public bool Transient => StatusCode is 0 or 408 or 429 or 502 or 503 or 504;
}

/// <summary>
/// El cierre de una grabación falló, pero LOS PASOS YA ESTÁN EN GRAPH: se envían uno a uno mientras se
/// graba, no al final. Lo que quedó pendiente es el post-procesado —título, resumen y guía— que Graph
/// hace con un LLM y que en flujos largos se pasa del tiempo máximo de Vercel (504).
///
/// Existe como excepción propia porque la diferencia importa y el mensaje genérico la borraba: el
/// operador creía haber perdido tres minutos de grabación cuando lo perdido era el resumen. Lleva el
/// <see cref="SessionId"/> para que el cliente pueda guardarlo y reintentar sin volver a grabar.
/// </summary>
public sealed class FinishPendingException : GraphException
{
    public string SessionId { get; }
    public string WorkflowId { get; }

    public FinishPendingException(string sessionId, string workflowId, string message, int statusCode)
        : base(message, statusCode)
    {
        SessionId = sessionId;
        WorkflowId = workflowId;
    }
}

/// <summary>
/// El único puente con Graph. Habla EXACTAMENTE el contrato público /api/v1 documentado en
/// docs/API.md del repo Graph — ni una ruta interna (/api/workflows sin versionar) se toca desde aquí:
/// esas son del dashboard de Graph y pueden cambiar sin aviso.
///
/// Graph decide, este cliente ejecuta. El backend nunca toca la UI del cliente: devuelve pasos y
/// matches, y quien los aplica sobre SAP GUI o UIA es este proceso.
/// </summary>
public sealed class GraphClient
{
    private readonly HttpClient _http;
    private readonly GraphConfig _config;

    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public GraphClient(GraphConfig config, HttpClient? http = null)
    {
        _config = config;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        _http.DefaultRequestHeaders.Remove("X-API-Key");
        if (!string.IsNullOrWhiteSpace(config.ApiKey))
            _http.DefaultRequestHeaders.Add("X-API-Key", config.ApiKey);
    }

    private string Url(string path) => $"{_config.BaseUrl.TrimEnd('/')}{path}";

    // ── Grabación ────────────────────────────────────────────────────────────

    /// <summary>Abre la sesión de grabación. Devuelve el id, que ES el workflowId.</summary>
    public async Task<string> StartSessionAsync(StartSessionRequest req, CancellationToken ct)
    {
        var res = await PostAsync<StartSessionResponse>("/api/v1/learning/sessions", req, ct);
        string id = res.Session?.Id ?? "";
        if (string.IsNullOrWhiteSpace(id)) throw new GraphException("Graph no devolvió un id de sesión.");
        return id;
    }

    /// <summary>
    /// Registra un paso observado. El id de sesión va SIEMPRE en la ruta, nunca implícito.
    ///
    /// Es deliberado: Graph guarda las sesiones activas en memoria (LearningSessionService) y corre en
    /// serverless, así que la "sesión activa" del servidor no sobrevive de forma fiable entre
    /// peticiones. Con el id explícito, la grabación funciona aunque la instancia sea otra.
    /// </summary>
    public async Task<int> RecordStepAsync(string sessionId, StepRequest step, CancellationToken ct)
    {
        var res = await PostAsync<StepResponse>($"/api/v1/learning/sessions/{Escape(sessionId)}/steps", step, ct);
        return res.Step?.StepOrder ?? 0;
    }

    public async Task AddContextNoteAsync(string sessionId, string transcript, CancellationToken ct)
    {
        var body = new ContextNoteRequest { Note = new ContextNote { Transcript = transcript } };
        await PostAsync<JsonElement>($"/api/v1/learning/sessions/{Escape(sessionId)}/context-notes", body, ct);
    }

    /// <summary>Cierra la sesión: Graph post-procesa y persiste el workflow en Neo4j.</summary>
    public async Task<FinishResponse> FinishSessionAsync(string sessionId, CancellationToken ct) =>
        await PostAsync<FinishResponse>($"/api/v1/learning/sessions/{Escape(sessionId)}/finish", new { }, ct);

    // ── Ejecución ────────────────────────────────────────────────────────────

    /// <summary>Workflows privados de esta API key + los globales.</summary>
    public async Task<List<JsonElement>> ListWorkflowsAsync(CancellationToken ct)
    {
        var res = await GetAsync<WorkflowListResponse>("/api/v1/workflows", ct);
        return res.Workflows;
    }

    /// <summary>
    /// Un workflow COMPLETO, con sus steps tal como están guardados (incluidos surfaceHints, que es
    /// donde vive la superficie observada de cada paso). El plan (<see cref="GetPlanAsync"/>) no
    /// sirve para esto: filtra y transforma steps, y el mapa del grafo necesita lo grabado, no lo
    /// ejecutable.
    /// </summary>
    public async Task<JsonElement> GetWorkflowAsync(string workflowId, CancellationToken ct)
    {
        var res = await GetAsync<JsonElement>($"/api/v1/workflows/{Escape(workflowId)}", ct);
        return res.TryGetProperty("workflow", out var w) ? w : res;
    }

    /// <summary>
    /// Enseña al workflow a alcanzar su propia superficie: prepend de un step de alineación (abrir/enfocar
    /// la app) en orden 0, para que la próxima vez arranque solo. Graph deriva la app del sourceOrigin ya
    /// guardado del workflow. Best-effort: si el backend aún no lo soporta, no interrumpe nada.
    /// </summary>
    public async Task PrependAlignmentStepAsync(string workflowId, CancellationToken ct)
    {
        try
        {
            await PostAsync<JsonElement>($"/api/v1/workflows/{Escape(workflowId)}/prepend-alignment", new { }, ct);
        }
        catch { /* aprendizaje best-effort */ }
    }

    /// <summary>Pide el plan ejecutable. Graph ya filtra los steps no ejecutables.</summary>
    public async Task<ExecutionPlan> GetPlanAsync(
        string workflowId, Dictionary<string, string>? variables, Dictionary<string, string>? intent,
        CancellationToken ct)
    {
        var body = new PlanRequest
        {
            Variables = variables ?? new(),
            ExecutionIntent = intent ?? new(),
        };
        var res = await PostAsync<PlanResponse>($"/api/v1/workflows/{Escape(workflowId)}/plan", body, ct);
        if (res.ExecutionPlan == null) throw new GraphException("Graph no devolvió un plan de ejecución.");
        return res.ExecutionPlan;
    }

    // ── Exportación de notas clínicas (carril Operations) ────────────────────

    /// <summary>
    /// Reclama el siguiente trabajo de exportación de la cola (FIFO con lease). Devuelve null si la
    /// cola está vacía (204) — eso no es un error, es «vuelve a preguntar luego».
    /// </summary>
    public async Task<ExportJobResponse?> ClaimExportAsync(string device, CancellationToken ct)
    {
        string payload = JsonSerializer.Serialize(new ExportClaimRequest { Device = device }, Json);
        using var res = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, Url("/api/v1/operations/exports/claim"))
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        }, ct);
        if (res.StatusCode == System.Net.HttpStatusCode.NoContent) return null;
        return await ReadAsync<ExportJobResponse>(res, ct);
    }

    /// <summary>
    /// Reporta el desenlace de un trabajo. Esta llamada es UN intento: la política de
    /// reintentar-hasta-ack —que el contrato exige, porque sin ack Graph no puede saber que el HIS
    /// ya se escribió— vive en <see cref="NoteExportExecutor"/>, que usa <see cref="GraphException.Transient"/>
    /// para distinguir «reintenta» de «el servidor rechazó el resultado» (lease vencido, otro dueño).
    /// </summary>
    public async Task<ExportResultAck> ReportExportResultAsync(
        string exportId, ExportResultRequest result, CancellationToken ct) =>
        await PostAsync<ExportResultAck>($"/api/v1/operations/exports/{Escape(exportId)}/result", result, ct);

    // ── Autofill ─────────────────────────────────────────────────────────────

    /// <summary>Mapea una nota organizada contra los campos detectados en la superficie.</summary>
    public async Task<AutofillResult> MatchAsync(AutofillRequest req, CancellationToken ct)
    {
        var res = await PostAsync<AutofillResponse>("/api/v1/autofill/match", req, ct);
        return res.Autofill ?? new AutofillResult();
    }

    // ── Diagnóstico ──────────────────────────────────────────────────────────

    /// <summary>
    /// GET /api/v1: el manifiesto de capacidades. Sirve de prueba de vida y de key: si responde 200,
    /// la URL y la API key son correctas. Úsalo al instalar en la máquina del cliente.
    /// </summary>
    public async Task<JsonElement> ManifestAsync(CancellationToken ct) =>
        await GetAsync<JsonElement>("/api/v1", ct);

    /// <summary>Borra un workflow (desde el carrusel del panel Backend). 404 si ya no existe.</summary>
    public async Task DeleteWorkflowAsync(string workflowId, CancellationToken ct)
    {
        using var res = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Delete, Url($"/api/v1/workflows/{Escape(workflowId)}")), ct);
        if (!res.IsSuccessStatusCode)
        {
            string text = await res.Content.ReadAsStringAsync(ct);
            throw new GraphException(ErrorFrom(text, res.StatusCode), (int)res.StatusCode);
        }
    }

    // ── HTTP ─────────────────────────────────────────────────────────────────

    private static string Escape(string segment) => Uri.EscapeDataString(segment ?? "");

    private async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        using var res = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, Url(path)), ct);
        return await ReadAsync<T>(res, ct);
    }

    private async Task<T> PostAsync<T>(string path, object body, CancellationToken ct)
    {
        string payload = JsonSerializer.Serialize(body, Json);
        using var res = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, Url(path))
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        }, ct);
        return await ReadAsync<T>(res, ct);
    }

    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> build, CancellationToken ct)
    {
        if (!_config.IsConfigured)
            throw new GraphException("Graph no está configurado: falta la URL o la API key.");
        try
        {
            return await _http.SendAsync(build(), ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e)
        {
            throw new GraphException($"No se pudo contactar con Graph: {e.Message}");
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage res, CancellationToken ct)
    {
        string text = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
            throw new GraphException(ErrorFrom(text, res.StatusCode), (int)res.StatusCode);

        try
        {
            var parsed = JsonSerializer.Deserialize<T>(text, Json);
            if (parsed == null) throw new GraphException("Graph devolvió una respuesta vacía.");
            return parsed;
        }
        catch (JsonException e)
        {
            throw new GraphException($"Graph devolvió un JSON inesperado: {e.Message}");
        }
    }

    /// <summary>Graph reporta los fallos como <c>{"error": "..."}</c>; si no, nos quedamos con el status.</summary>
    private static string ErrorFrom(string body, System.Net.HttpStatusCode status)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("error", out var err) &&
                err.ValueKind == JsonValueKind.String)
            {
                return $"{err.GetString()} (HTTP {(int)status})";
            }
        }
        catch { /* no era JSON: caemos al genérico */ }

        return status == System.Net.HttpStatusCode.Unauthorized || status == System.Net.HttpStatusCode.Forbidden
            ? $"Graph rechazó la API key (HTTP {(int)status})."
            : $"Graph respondió HTTP {(int)status}.";
    }
}
