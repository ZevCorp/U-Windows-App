using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using U.WindowsClient.Backend;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Teach;

/// <summary>
/// Orquesta la sesión de enseñanza activa: graba pantalla+voz, sube el mp4, lo procesa y guarda.
///
/// EL CLIENTE NO TIENE NINGUNA KEY. Antes hablaba directo con Gemini y eso obligaba a repartir una
/// key de Google dentro del .exe: cada usuario nuevo tenía que pegarla a mano y era extraíble
/// descompilando. Ahora el backend firma las subidas y hace las llamadas al modelo; el cliente solo
/// mueve bytes. El video igual NO pasa por el backend (no cabe en el límite de payload de Vercel):
/// sube directo a Google y a Supabase con URLs firmadas de corta duración, que es lo mejor de los
/// dos mundos.
///
/// Flujo (rutas sin prefijo: BackendClient antepone /api/v1 contra Graph, /api contra el backend viejo):
/// 1. /teach/upload-token  → URL de subida a Gemini + URL de archivo (Supabase Storage)
/// 2. PUT del mp4 directo a Gemini (bytes, sin key) y también al archivo, para que lo veamos después
/// 3. /teach/file-state en bucle → hasta que Gemini deja el video ACTIVE
/// 4. /teach/process-video → el backend procesa con el prompt médico y guarda el conocimiento
/// 5. El mp4 y el resumen quedan además en el disco del usuario (VideoLibrary)
/// </summary>
public sealed class TeachSession : IAsyncDisposable
{
    private readonly BackendClient _backend;
    private readonly VideoLibrary _library;
    private readonly string _userId;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(30) };

    private ScreenRecorder? _recorder;
    private string? _recordingPath;

    /// <summary>Dónde quedó (o va a quedar) el mp4 de esta grabación. La lección lo referencia (spec 013).</summary>
    public string? RutaDelVideo => _recordingPath;
    private string? _recorderError;

    public event EventHandler<string>? StatusChanged;

    public TeachSession(BackendClient backend, VideoLibrary library, string userId)
    {
        _backend = backend;
        _library = library;
        _userId = userId;
    }

    /// <summary>Inicia grabación. El usuario ve la pantalla tal como la usa.</summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        LogBus.Log("teach", "TeachSession.StartAsync");
        _recordingPath = _library.NewRecordingPath();
        _recorder = new ScreenRecorder(_recordingPath);
        try
        {
            await _recorder.StartAsync(ct);
            StatusChanged?.Invoke(this, "Grabando pantalla + voz…");
        }
        catch (Exception ex)
        {
            LogBus.Log("teach", $"StartAsync falló: {ex}");
            StatusChanged?.Invoke(this, $"Error al iniciar grabación: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Tira a la basura la grabación en curso y empieza una nueva, sin subir ni procesar nada.
    ///
    /// Existe porque equivocarse enseñando (meterse en la plataforma que no era, hacer un paso al
    /// revés) era irreversible: la única salida era detener, y detener SUBE el video a Gemini y manda
    /// sus notas al MemoryStore del backend. Es decir, el error quedaba aprendido — justo lo contrario
    /// de lo que el médico quería. Reiniciar corta eso antes de que salga nada de la máquina.
    /// </summary>
    public async Task RestartAsync(CancellationToken ct = default)
    {
        await DiscardAsync();
        await StartAsync(ct);
    }

    /// <summary>
    /// Detiene la grabación en curso, si la hay, y borra su mp4. No sube ni procesa nada: lo grabado
    /// hasta aquí desaparece.
    /// </summary>
    private async Task DiscardAsync()
    {
        if (_recorder != null)
        {
            try
            {
                // Hay que esperar el Stop aunque el video se vaya a la basura: hasta que no llega
                // OnRecordingComplete, el mp4 sigue abierto y el File.Delete de abajo fallaría.
                await _recorder.StopAsync();
            }
            catch (Exception ex)
            {
                // Da igual por qué falló al detener; el archivo se descarta igual.
                LogBus.Log("teach", $"fallo al detener la grabación que se iba a descartar: {ex.Message}");
            }
            finally
            {
                await _recorder.DisposeAsync();
                _recorder = null;
            }
        }

        if (_recordingPath != null && !_library.Delete(_recordingPath))
            LogBus.Log("teach", $"no se pudo borrar la grabación descartada, quedará en 🎞 Videos: {_recordingPath}");
        else if (_recordingPath != null)
            LogBus.Log("teach", $"grabación descartada: {_recordingPath}");

        _recordingPath = null;
        _recorderError = null;
    }

    /// <summary>Detiene la grabación. El mp4 queda finalizado en disco, listo para procesar.</summary>
    public async Task StopAsync()
    {
        if (_recorder == null) return;
        try
        {
            await _recorder.StopAsync();
            _recorderError = _recorder.LastError;
            StatusChanged?.Invoke(this, _recorderError == null
                ? "Grabación detenida. Procesando…"
                : $"La grabación falló: {_recorderError}");
        }
        finally
        {
            await _recorder.DisposeAsync();
            _recorder = null;
        }
    }

    /// <summary>
    /// Sube el mp4, lo procesa vía backend y guarda el resumen. Devuelve lo que Ü aprendió.
    /// </summary>
    /// <param name="pasos">Los pasos de ESTA demo (promesa 135). Si van, el cerebro además
    /// interpreta cuáles fueron datos y qué significa cada elemento; si no van, se comporta como
    /// siempre y solo devuelve el resumen.</param>
    public async Task<VideoLeido> ProcessAsync(
        IReadOnlyList<PasoQueSePregunta>? pasos = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_recordingPath) || !File.Exists(_recordingPath))
        {
            string reason = _recorderError != null
                ? $"la grabación falló antes de producir un archivo: {_recorderError}"
                : "no se generó ningún archivo de video (¿se detuvo antes de que empezara a grabar?)";
            LogBus.Log("teach", $"ProcessAsync: no hay grabación para procesar — {reason}");
            throw new InvalidOperationException($"No hay grabación para procesar: {reason}");
        }

        long length = new FileInfo(_recordingPath).Length;
        LogBus.Log("teach", $"ProcessAsync: {_recordingPath} ({length / 1024 / 1024.0:0.0} MB)");

        StatusChanged?.Invoke(this, "Preparando subida…");
        var token = await _backend.PostAsync<UploadTokenResponse>(
            "/teach/upload-token", new UploadTokenRequest { ContentLength = length, UserId = _userId }, ct);
        if (token?.GeminiUploadUrl == null)
            throw new InvalidOperationException("el backend no devolvió URL de subida para Gemini");
        if (token.ArchiveError != null)
            LogBus.Log("teach", $"archivo del video no disponible: {token.ArchiveError}");

        StatusChanged?.Invoke(this, "Subiendo video…");
        string fileUri = await UploadToGeminiAsync(token.GeminiUploadUrl, _recordingPath, length, ct);
        LogBus.Log("teach", $"subido a Gemini: {fileUri}");

        // Archivar en Supabase es "best effort": si falla, el usuario no se queda sin enseñanza —
        // solo perdemos poder revisar ese video desde desarrollo.
        if (token.ArchiveUploadUrl != null)
        {
            try
            {
                await ArchiveAsync(token.ArchiveUploadUrl, _recordingPath, ct);
                LogBus.Log("teach", $"video archivado en {token.ArchivePath}");
            }
            catch (Exception ex)
            {
                LogBus.Log("teach", $"no se pudo archivar el video: {ex.Message}");
            }
        }

        StatusChanged?.Invoke(this, "Esperando a que el video se procese…");
        await WaitActiveAsync(fileUri, ct);

        StatusChanged?.Invoke(this, "Analizando lo que enseñaste…");
        var result = await _backend.PostAsync<ProcessResult>(
            "/teach/process-video", new ProcessRequest
            {
                FileUri = fileUri,
                UserId = _userId,
                // LOS PASOS DE ESTA DEMO Y NADA MÁS (promesa 135). Van como HECHOS: el criterio y
                // las palabras con las que se le pide viven en Graph, no aquí.
                Steps = pasos?.Count > 0 ? pasos.Select(x => new StepToRead
                {
                    Order = x.Orden, Field = x.Campo, Value = x.Valor, Said = x.Dicho,
                }).ToList() : null,
            }, ct);

        string summary = result?.Summary ?? "";
        // LA INTERPRETACIÓN VIAJA COMO JSON CRUDO a propósito: quien la entiende es
        // LoQueElModeloInterpreta, que es puro y está juzgado por el contrato (promesas 129 y 130).
        // Parsearla aquí sería un segundo lector del mismo hecho, y dos lectores se desincronizan.
        string interpretacion = result?.Interpretation?.ToString() ?? "";
        LogBus.Log("teach", $"procesado: {result?.Notes?.Count ?? 0} nota(s), "
            + $"interpretación {(interpretacion.Length > 0 ? $"de {interpretacion.Length} car." : "AUSENTE")}"
            + $". summary=\"{summary}\"");

        _library.SaveSummary(_recordingPath, summary);
        StatusChanged?.Invoke(this, summary.Length > 0 ? summary : "No encontré nada que aprender en el video.");
        return new VideoLeido(summary, interpretacion);
    }

    /// <summary>
    /// Interpreta la demo SIN video: solo los pasos y lo que se narró. Promesa 136.
    /// </summary>
    /// <remarks>
    /// EL SEGUNDO PELDAÑO. Se llama cuando el primero —el video— no pudo: sin saldo en la cuenta de
    /// Gemini (2026-09-03), con un 504 en una demo larga, o sin red al subir el mp4. Devuelve el
    /// mismo JSON crudo que el camino del video, porque quien lo lee es la misma pieza pura.
    ///
    /// UNA CADENA VACÍA SI FALLA, y nunca una excepción hacia arriba: este camino es en sí mismo un
    /// respaldo, y que un respaldo tumbe la enseñanza sería peor que no tenerlo. Lo que queda
    /// debajo es la regla del narrado, que es determinista y ya está en disco.
    ///
    /// ESTÁTICA porque se llama DESPUÉS de que la sesión de video se haya cerrado: lo único que
    /// necesita es el puente con el backend, y colgarla de una instancia ya soltada sería pedirle
    /// trabajo a un objeto que dijo que había terminado.
    /// </remarks>
    public static async Task<string> InterpretarPasosAsync(BackendClient backend,
        IReadOnlyList<PasoQueSePregunta> pasos, string dondeEmpieza, CancellationToken ct = default)
    {
        if (backend == null || pasos == null || pasos.Count == 0) return "";
        try
        {
            var res = await backend.PostAsync<InterpretResult>("/teach/interpret-steps", new InterpretRequest
            {
                StartsAt = dondeEmpieza ?? "",
                Steps = pasos.Select(x => new StepToRead
                {
                    Order = x.Orden, Field = x.Campo, Value = x.Valor, Said = x.Dicho,
                }).ToList(),
            }, ct);

            string json = res?.Interpretation?.ToString() ?? "";
            LogBus.Log("teach", json.Length > 0
                ? $"la demo se interpretó SIN video ({json.Length} car.)"
                : "el cerebro contestó sin interpretación: se queda la regla del narrado");
            return json;
        }
        catch (Exception ex)
        {
            LogBus.Log("teach", $"no se pudo interpretar sin video: {ex.Message}");
            return "";
        }
    }

    /// <summary>Paso 2 del resumable upload de Gemini. Sin key: la URL ya trae su token embebido.</summary>
    private async Task<string> UploadToGeminiAsync(string uploadUrl, string videoPath, long length, CancellationToken ct)
    {
        using var file = new FileStream(videoPath, FileMode.Open, FileAccess.Read);
        using var content = new StreamContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        content.Headers.ContentLength = length;

        using var req = new HttpRequestMessage(HttpMethod.Put, uploadUrl) { Content = content };
        req.Headers.Add("X-Goog-Upload-Offset", "0");
        req.Headers.Add("X-Goog-Upload-Command", "upload, finalize");

        using var res = await _http.SendAsync(req, ct);
        string body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"subida a Gemini HTTP {(int)res.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("file", out var fileObj) && fileObj.TryGetProperty("uri", out var uri))
            return uri.GetString() ?? throw new InvalidOperationException("Gemini devolvió una uri vacía");

        throw new InvalidOperationException("respuesta de Gemini sin uri del archivo");
    }

    /// <summary>Sube el mismo mp4 al archivo (Supabase Storage) con la URL firmada por el backend.</summary>
    private async Task ArchiveAsync(string uploadUrl, string videoPath, CancellationToken ct)
    {
        using var file = new FileStream(videoPath, FileMode.Open, FileAccess.Read);
        using var content = new StreamContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");

        using var res = await _http.PutAsync(uploadUrl, content, ct);
        if (!res.IsSuccessStatusCode)
        {
            string body = await res.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"HTTP {(int)res.StatusCode}: {body}");
        }
    }

    /// <summary>Espera a que Gemini deje el video ACTIVE, preguntándole al backend (que sí tiene la key).</summary>
    private async Task WaitActiveAsync(string fileUri, CancellationToken ct)
    {
        for (int i = 0; i < 90; i++)
        {
            var res = await _backend.PostAsync<FileStateResponse>(
                "/teach/file-state", new FileStateRequest { FileUri = fileUri }, ct);
            string state = res?.State ?? "UNKNOWN";

            if (state == "ACTIVE") return;
            if (state == "FAILED") throw new InvalidOperationException("el video quedó FAILED en Gemini");
            await Task.Delay(2000, ct);
        }
        throw new InvalidOperationException("el video sigue procesándose tras el tiempo máximo de espera");
    }

    public async ValueTask DisposeAsync()
    {
        if (_recorder != null) await _recorder.DisposeAsync();
        _http.Dispose();
    }
}

// ── Contratos con el backend ────────────────────────────────────────

public sealed record UploadTokenRequest
{
    [JsonPropertyName("contentLength")]
    public long ContentLength { get; set; }
    [JsonPropertyName("userId")]
    public required string UserId { get; set; }
}

public sealed record UploadTokenResponse
{
    [JsonPropertyName("geminiUploadUrl")]
    public string? GeminiUploadUrl { get; set; }
    [JsonPropertyName("archiveUploadUrl")]
    public string? ArchiveUploadUrl { get; set; }
    [JsonPropertyName("archivePath")]
    public string? ArchivePath { get; set; }
    [JsonPropertyName("archiveError")]
    public string? ArchiveError { get; set; }
}

public sealed record FileStateRequest
{
    [JsonPropertyName("fileUri")]
    public required string FileUri { get; set; }
}

public sealed record FileStateResponse
{
    [JsonPropertyName("state")]
    public string? State { get; set; }
}

/// <summary>Lo que el video dejó: el resumen para el humano y la interpretación para la skill.</summary>
/// <param name="Interpretacion">JSON crudo del cerebro, o vacío si no llegó. Lo lee
/// <c>Navigation.LoQueElModeloInterpreta</c>, que distingue «no opinó» de «no hay nada».</param>
public sealed record VideoLeido(string Resumen, string Interpretacion);

/// <summary>Un paso de la demo, tal como viaja a Graph. Nombres en inglés: es el contrato HTTP.</summary>
public sealed record StepToRead
{
    [JsonPropertyName("order")]
    public int Order { get; set; }
    [JsonPropertyName("field")]
    public string Field { get; set; } = "";
    [JsonPropertyName("value")]
    public string Value { get; set; } = "";
    [JsonPropertyName("said")]
    public string Said { get; set; } = "";
}

public sealed record InterpretRequest
{
    [JsonPropertyName("startsAt")]
    public string StartsAt { get; set; } = "";
    [JsonPropertyName("steps")]
    public List<StepToRead> Steps { get; set; } = new();
}

public sealed record InterpretResult
{
    [JsonPropertyName("interpretation")]
    public System.Text.Json.JsonElement? Interpretation { get; set; }
}

public sealed record ProcessRequest
{
    [JsonPropertyName("fileUri")]
    public required string FileUri { get; set; }
    [JsonPropertyName("userId")]
    public required string UserId { get; set; }
    /// <summary>Los pasos de esta demo. Null cuando no hay ninguna: el contrato viejo sigue vivo.</summary>
    [JsonPropertyName("steps")]
    public List<StepToRead>? Steps { get; set; }
}

public sealed record TeachNote
{
    [JsonPropertyName("app")]
    public string? App { get; set; }
    [JsonPropertyName("note")]
    public string? Note { get; set; }
}

public sealed record ProcessResult
{
    [JsonPropertyName("summary")]
    public string? Summary { get; set; }
    /// <summary>
    /// Lo que el modelo entendió de los pasos: <c>{campos:[…], recuerdos:[…]}</c>. Se recibe como
    /// JSON sin tipar y se pasa tal cual a quien sabe leerlo — un objeto tipado aquí sería un
    /// segundo lector del mismo hecho, y el que ya existe está juzgado por el contrato.
    /// </summary>
    [JsonPropertyName("interpretation")]
    public System.Text.Json.JsonElement? Interpretation { get; set; }
    [JsonPropertyName("notes")]
    public List<TeachNote>? Notes { get; set; }
    [JsonPropertyName("questions")]
    public List<string>? Questions { get; set; }
}
