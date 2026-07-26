using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using U.WindowsClient.Backend;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Telemetry;

/// <summary>
/// Identidad del usuario/instalación para "Windows Live". El correo es la clave canónica en el backend.
/// </summary>
public sealed class TelemetryIdentity
{
    public string Email = "";
    public string InstallId = "";
    public string DisplayName = "";
    public string AppId = "windows-u";
    public string AppVersion = "";
    public string MachineName = "";
    public string OsVersion = "";
}

/// <summary>
/// Telemetría de "Windows Live": reporta al backend Graph (POST /api/v1/agent/{register,events}) para
/// que el dashboard del Provider Studio muestre al usuario en vivo (pulsos consciente/subconsciente +
/// logs). Es una FACHADA ESTÁTICA a propósito, igual que <see cref="LogBus"/>: los puntos de enganche
/// (AgentLoop, WorkflowMcpRunner) solo llaman <c>Telemetry.Emit(...)</c> sin conocer el transporte.
///
/// Regla de oro: la telemetría JAMÁS debe tumbar al agente. Todo es best-effort — si el backend no
/// responde, se pierde el evento y ya. Nada aquí lanza hacia el llamador.
/// </summary>
public static class TelemetryBus
{
    private static TelemetryClient? _client;

    /// <summary>Arranca la telemetría. Sin correo (usuario no onboarded) queda deshabilitada: Emit es no-op.</summary>
    public static void Init(BackendClient backend, TelemetryIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(identity.Email)) return;
        try
        {
            _client?.Dispose();
            _client = new TelemetryClient(backend, identity);
            _client.Start();
        }
        catch (Exception e) { LogBus.Log("telemetry", $"init falló: {e.Message}"); }
    }

    public static void Emit(string kind, string phase = "", string appId = "", string surfaceUrl = "",
        string workflowId = "", string runId = "", string label = "", object? detail = null)
    {
        _client?.Enqueue(new TelemetryEvent
        {
            Kind = kind,
            Phase = phase,
            AppId = appId,
            SurfaceUrl = surfaceUrl,
            WorkflowId = workflowId,
            RunId = runId,
            Label = label,
            Detail = detail,
            At = DateTime.UtcNow.ToString("o")
        });
    }

    /// <summary>Correlaciona todos los eventos de una misma corrida (consciente o workflow).</summary>
    public static string NewRunId() => Guid.NewGuid().ToString("N");

    public static void Shutdown() { try { _client?.Dispose(); } catch { } _client = null; }
}

/// <summary>Bufferiza eventos y los sube por lotes; refresca el registro/heartbeat del usuario.</summary>
internal sealed class TelemetryClient : IDisposable
{
    private const int FlushMs = 2000;
    private const int HeartbeatMs = 60_000;
    private const int MaxBatch = 100;
    private const int MaxQueue = 2000;

    private readonly BackendClient _backend;
    private readonly TelemetryIdentity _id;
    private readonly ConcurrentQueue<TelemetryEvent> _queue = new();
    private System.Threading.Timer? _flushTimer;
    private System.Threading.Timer? _heartbeatTimer;
    private int _flushing; // 0/1, evita lotes solapados

    public TelemetryClient(BackendClient backend, TelemetryIdentity id)
    {
        _backend = backend;
        _id = id;
    }

    public void Start()
    {
        // Registro inicial (best-effort) + heartbeat periódico.
        _ = RegisterAsync();
        _heartbeatTimer = new System.Threading.Timer(_ => { _ = RegisterAsync(); }, null, HeartbeatMs, HeartbeatMs);
        _flushTimer = new System.Threading.Timer(_ => { _ = FlushAsync(); }, null, FlushMs, FlushMs);
    }

    public void Enqueue(TelemetryEvent ev)
    {
        if (_queue.Count >= MaxQueue) return; // no crecer sin límite si el backend está caído
        _queue.Enqueue(ev);
    }

    private async Task RegisterAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await _backend.PostAsync<AckResponse>("/agent/register", new RegisterPayload
            {
                Email = _id.Email,
                DisplayName = _id.DisplayName,
                InstallId = _id.InstallId,
                AppId = _id.AppId,
                AppVersion = _id.AppVersion,
                MachineName = _id.MachineName,
                OsVersion = _id.OsVersion
            }, cts.Token);
        }
        catch (Exception e) { LogBus.Log("telemetry", $"register: {e.Message}"); }
    }

    private async Task FlushAsync()
    {
        if (Interlocked.Exchange(ref _flushing, 1) == 1) return;
        try
        {
            var batch = new List<TelemetryEvent>(MaxBatch);
            while (batch.Count < MaxBatch && _queue.TryDequeue(out var ev)) batch.Add(ev);
            if (batch.Count == 0) return;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await _backend.PostAsync<AckResponse>("/agent/events", new EventsPayload
            {
                Email = _id.Email,
                InstallId = _id.InstallId,
                Events = batch
            }, cts.Token);
        }
        catch (Exception e)
        {
            LogBus.Log("telemetry", $"flush: {e.Message}");
            // No re-encolamos: preferimos perder eventos antes que acumular y reintentar en bucle.
        }
        finally
        {
            Interlocked.Exchange(ref _flushing, 0);
        }
    }

    public void Dispose()
    {
        try { _flushTimer?.Dispose(); } catch { }
        try { _heartbeatTimer?.Dispose(); } catch { }
        _ = FlushAsync(); // último intento por vaciar la cola
    }
}

// -------- DTOs (snake/camel que el backend ya tolera; usamos camelCase) --------

internal sealed class TelemetryEvent
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("phase")] public string Phase { get; set; } = "";
    [JsonPropertyName("appId")] public string AppId { get; set; } = "";
    [JsonPropertyName("surfaceUrl")] public string SurfaceUrl { get; set; } = "";
    [JsonPropertyName("workflowId")] public string WorkflowId { get; set; } = "";
    [JsonPropertyName("runId")] public string RunId { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("detail")] public object? Detail { get; set; }
    [JsonPropertyName("at")] public string At { get; set; } = "";
}

internal sealed class RegisterPayload
{
    [JsonPropertyName("email")] public string Email { get; set; } = "";
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("installId")] public string InstallId { get; set; } = "";
    [JsonPropertyName("appId")] public string AppId { get; set; } = "";
    [JsonPropertyName("appVersion")] public string AppVersion { get; set; } = "";
    [JsonPropertyName("machineName")] public string MachineName { get; set; } = "";
    [JsonPropertyName("osVersion")] public string OsVersion { get; set; } = "";
}

internal sealed class EventsPayload
{
    [JsonPropertyName("email")] public string Email { get; set; } = "";
    [JsonPropertyName("installId")] public string InstallId { get; set; } = "";
    [JsonPropertyName("events")] public List<TelemetryEvent> Events { get; set; } = new();
}

internal sealed class AckResponse
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
}
