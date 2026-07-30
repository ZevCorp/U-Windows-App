using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using U.Graph.Surfaces;

namespace U.Graph;

/// <summary>Cómo terminó la atención de UN trabajo, ya reportado y con ack de Graph.</summary>
public sealed record ExportJobOutcome(
    string ExportId, string Outcome, string Folio, string ErrorCode, string Status, bool ConsultationExported);

/// <summary>
/// El ejecutor real de la cola de exportaciones: el reemplazo del simulador
/// (scripts/simulate-operations-executor.js del repo Graph), hablando exactamente su mismo carril.
///
/// El ciclo: claim → ejecutar el workflow contra SAP → verificar la señal de éxito → result.
/// Trabaja por PULL (nunca recibe conexiones entrantes: pregunta), así que da igual que viva
/// detrás del firewall del hospital.
///
/// Las dos reglas que no se negocian, porque de ellas depende que «exportada» diga la verdad:
///
///   1. <c>outcome:'ok'</c> SOLO se reporta con la señal de éxito de SAP verificada: el mensaje
///      tipo S de la barra de estado tras el round-trip del guardado. Que el workflow terminara
///      sin fallos NO es la señal — «no lanzó excepción» no es exportar.
///   2. El result se reintenta HASTA recibir ack. Nunca best-effort: si el resultado no llega,
///      Graph no puede saber que SAP ya se escribió, re-serviría el trabajo al vencer el lease y
///      la nota acabaría dos veces en la historia clínica. Por eso el resultado se persiste a
///      disco ANTES del primer envío y se reenvía al arrancar si quedó sin ack (reenviar es
///      seguro: el endpoint es idempotente).
/// </summary>
public sealed class NoteExportExecutor
{
    private readonly GraphClient _graph;
    private readonly GraphConfig _config;
    private readonly string _device;
    private readonly IUiSurface[] _surfaces;
    private readonly SapGuiSurface? _sap;

    /// <summary>Cada cuánto se pregunta por trabajo cuando la cola está vacía.</summary>
    public int PollSeconds { get; set; } = 15;

    /// <summary>Cuánto se espera la señal de la barra de estado tras el último paso. Un guardado en
    /// el SAP del hospital puede tardar; cortísimo daría falsos «sin señal».</summary>
    public int SignalTimeoutMs { get; set; } = 20_000;

    /// <summary>Log de diagnóstico. El cliente enchufa su LogBus, como en WorkflowPlayer.</summary>
    public Action<string>? Log { get; set; }
    private void L(string msg) { try { Log?.Invoke(msg); } catch { } }

    /// <summary>Alineación consciente (abrir/enfocar SAP) si la pantalla no coincide. Mismo delegate
    /// que usa el player; lo pone el cliente (AppAligner.EnsureAsync).</summary>
    public SurfaceAligner? Aligner { get; set; }

    /// <summary>
    /// Veto del cliente: devuelve el MOTIVO por el que ahora no se debe reclamar («grabando una
    /// enseñanza», «hay un workflow corriendo a mano»…) o "" si está libre. Se pregunta ANTES del
    /// claim a propósito: reclamar y no poder ejecutar quema un intento y un lease del trabajo.
    /// </summary>
    public Func<string>? HoldReason { get; set; }

    /// <summary>Estado legible para la UI («Atendiendo la cola», «Exportando consulta…»).</summary>
    public event EventHandler<string>? StatusChanged;
    private void Status(string s) { try { StatusChanged?.Invoke(this, s); } catch { } }

    /// <summary>Se dispara cuando un trabajo quedó reportado Y con ack.</summary>
    public event EventHandler<ExportJobOutcome>? JobDone;

    /// <summary>True mientras hay un trabajo en ejecución contra SAP.</summary>
    public bool Working { get; private set; }

    private string _lastNote = ""; // para no repetir el mismo aviso en cada vuelta del sondeo

    public NoteExportExecutor(GraphClient graph, GraphConfig config, string device, params IUiSurface[] surfaces)
    {
        _graph = graph;
        _config = config;
        _device = string.IsNullOrWhiteSpace(device) ? "operations-sin-nombre" : device.Trim();
        _surfaces = surfaces;
        _sap = surfaces.OfType<SapGuiSurface>().FirstOrDefault();
    }

    /// <summary>El loop. Corre hasta que se cancele; cada vuelta atiende como mucho un trabajo.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        L($"ejecutor de exportaciones encendido · device='{_device}' · sondeo cada {PollSeconds}s");
        Status("Atendiendo la cola de exportaciones.");

        // Primero lo que quedó sin ack de corridas anteriores: mientras un resultado no se entregue,
        // no se reclama trabajo nuevo — es el mismo backend, y el orden protege contra el doble claim.
        try { await RedeliverPendingAsync(ct); }
        catch (OperationCanceledException) { Status("Ejecutor detenido."); return; }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                string hold = HoldReason?.Invoke() ?? "";
                if (hold.Length > 0)
                {
                    Note($"en pausa: {hold}");
                    await Task.Delay(TimeSpan.FromSeconds(PollSeconds), ct);
                    continue;
                }

                ExportJobResponse? job;
                try { job = await _graph.ClaimExportAsync(_device, ct); }
                catch (GraphException e)
                {
                    // Transitorio (arranque en frío, red): se insiste callado. Un rechazo de verdad
                    // (401: la key) se dice con su motivo — reintentar no lo va a arreglar solo.
                    Note(e.Transient ? $"Graph no respondió al claim ({e.Message})"
                                     : $"el claim fue rechazado: {e.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(PollSeconds), ct);
                    continue;
                }

                if (job?.Export == null || string.IsNullOrWhiteSpace(job.Export.Id))
                {
                    Note("cola vacía");
                    await Task.Delay(TimeSpan.FromSeconds(PollSeconds), ct);
                    continue;
                }

                _lastNote = "";
                await HandleAsync(job, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e)
            {
                L($"la vuelta del ejecutor falló: {e.Message}");
                try { await Task.Delay(TimeSpan.FromSeconds(PollSeconds), ct); }
                catch (OperationCanceledException) { break; }
            }
        }
        Status("Ejecutor detenido.");
        L("ejecutor de exportaciones apagado");
    }

    /// <summary>Un aviso de sondeo se loguea UNA vez por cambio, no en cada vuelta.</summary>
    private void Note(string msg)
    {
        if (msg == _lastNote) return;
        _lastNote = msg;
        L(msg);
    }

    // ── Un trabajo, de punta a punta ─────────────────────────────────────────

    private async Task HandleAsync(ExportJobResponse job, CancellationToken ct)
    {
        ExportJobInfo export = job.Export!;
        Working = true;
        try
        {
            L($"▶ trabajo {export.Id} · workflow='{export.WorkflowId}' · intento {export.Attempts} · lease hasta {export.LeaseExpiresAt}");
            Status($"Exportando consulta (intento {export.Attempts})…");

            ExportResultRequest result;
            try
            {
                result = await ExecuteAsync(export, job.Payload, ct);
            }
            catch (OperationCanceledException)
            {
                // Apagado a media ejecución: no sabemos el desenlace y no se inventa uno. El trabajo
                // queda 'claimed' hasta que venza el lease y otro claim lo re-sirva. Se deja dicho.
                L($"✋ ejecutor detenido a mitad del trabajo {export.Id}: NO se reporta resultado. " +
                  "El lease vencerá y el trabajo volverá a la cola; si SAP quedó a medio llenar, " +
                  "revisarlo a mano antes del reintento.");
                throw;
            }

            // El resultado se persiste ANTES del primer envío: si la app muere con SAP ya escrito y
            // el ack sin llegar, al arrancar se reenvía. Perderlo sería el peor final: Graph
            // re-serviría el trabajo y la nota acabaría dos veces en la historia clínica.
            PendingResultStore.Save(export.Id, result, L);

            ExportResultAck? ack = await DeliverAsync(export.Id, result, ct);
            if (ack == null) return; // rechazo de contrato; ya quedó logueado con todas las letras

            string tail = result.Outcome == "ok"
                ? (ack.ConsultationExported
                    ? $"✅ consulta EXPORTADA{(string.IsNullOrEmpty(result.Folio) ? "" : $" · folio {result.Folio}")}"
                    : "⚠ ok reportado, pero la consulta no cambió a exportada (¿ya lo estaba, o dejó de estar aprobada?)")
                : $"↩ outcome={result.Outcome} · error_code={result.ErrorCode}";
            L($"{tail} · status={ack.Status}{(ack.Idempotent ? " · (ack idempotente)" : "")}");
            Status(result.Outcome == "ok" && ack.ConsultationExported
                ? "✓ Consulta exportada. Atendiendo la cola."
                : $"El trabajo terminó en {ack.Status}. Atendiendo la cola.");

            try
            {
                JobDone?.Invoke(this, new ExportJobOutcome(
                    export.Id, result.Outcome, result.Folio ?? "", result.ErrorCode ?? "",
                    ack.Status, ack.ConsultationExported));
            }
            catch { }
        }
        finally { Working = false; }
    }

    /// <summary>
    /// Ejecuta el workflow del trabajo y construye el resultado según lo que SAP diga de verdad.
    /// Nunca lanza por fallos de ejecución: todo desenlace se convierte en un result tipado, porque
    /// un trabajo reclamado sin result es un lease quemado.
    /// </summary>
    private async Task<ExportResultRequest> ExecuteAsync(ExportJobInfo export, ExportPayload? payload, CancellationToken ct)
    {
        string workflowId = (export.WorkflowId ?? "").Trim();
        if (workflowId.Length == 0)
            return Result("error", errorCode: "EXPORT_WORKFLOW_MISSING");

        SapGuiSurface? sap = _sap;
        if (sap == null)
            return Result("error", errorCode: "SAP_SURFACE_MISSING");

        SurfaceAvailability sapCheck = sap.Check();
        if (!sapCheck.Available)
        {
            L($"SAP no disponible: {sapCheck.Reason}");
            return Result("error", errorCode: "SAP_UNAVAILABLE");
        }

        // LÍNEA BASE de la señal: lo que la barra de estado ya mostraba ANTES de ejecutar no puede
        // ser la señal de este guardado. Sin esto, un «Documento grabado» residual de hace una hora
        // pasaría por éxito de hoy — el «parece que funcionó», que es peor que fallar.
        SapGuiSurface.SapStatusMessage? baseline = await Task.Run(() => sap.AwaitStatusbarMessage(1_200, ct), ct);
        if (baseline != null) L($"sbar previo (línea base): tipo='{baseline.Type}' código='{baseline.Code}'");

        var player = new WorkflowPlayer(_graph, _config, _surfaces)
        {
            Aligner = Aligner,
            Log = s => L($"  {s}"),
        };
        player.StepDone += (_, o) =>
        {
            if (!o.Omitted) Status(o.Ok ? $"Exportando: ✓ {o.Label}" : $"Exportando: ✗ {o.Label}");
        };

        // El contexto clínico viaja como variable, por el mismo canal que usa el cerebro (context):
        // Graph puede usarlo al armar el plan y los pasos dynamic/bindTo pueden resolver contra él.
        var variables = new Dictionary<string, string>();
        string context = FirstNonEmpty(payload?.Context, payload?.RenderedText);
        if (context.Length > 0) variables["context"] = context;

        RunResult run;
        try
        {
            run = await player.RunAsync(workflowId, variables.Count > 0 ? variables : null,
                strictSurface: true, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e)
        {
            L($"el player lanzó: {e.Message}");
            return Result("error", errorCode: "WORKFLOW_CRASHED");
        }

        if (!run.Ok)
        {
            L($"workflow detenido: {run.Error} · {run.Tally}");
            int failed = run.Steps.LastOrDefault(s => !s.Ok && !s.Omitted)?.StepOrder ?? 0;
            return Result("error", errorCode: "WORKFLOW_STEP_FAILED",
                detailCode: failed > 0 ? $"STEP_{failed}" : "");
        }

        // El workflow terminó sin fallos. Eso NO exporta nada: la señal es el mensaje de negocio que
        // SAP publica en la barra de estado cuando el round-trip del guardado termina.
        L($"workflow completado ({run.Tally}); esperando la señal de SAP…");
        Status("Workflow completado. Verificando la confirmación de SAP…");
        SapGuiSurface.SapStatusMessage? msg = await Task.Run(() => sap.AwaitStatusbarMessage(SignalTimeoutMs, ct), ct);

        if (msg == null)
        {
            L("sin mensaje en la barra de estado dentro del plazo: no hay señal que verificar → no es 'ok'");
            return Result("error", errorCode: "SAP_SUCCESS_SIGNAL_NOT_FOUND");
        }

        // El texto completo se queda en el log LOCAL: un mensaje de SAP puede llevar datos del
        // paciente, y hacia Graph solo viajan códigos tipados y el folio (un identificador).
        L($"sbar: tipo='{msg.Type}' código='{msg.Code}' texto='{msg.Text}'");

        if (baseline != null && msg.Type == baseline.Type && msg.Text == baseline.Text && msg.Code == baseline.Code)
        {
            // La misma señal que ya estaba antes de empezar no confirma ESTE guardado. (Residuo
            // conocido: un mensaje S de un paso intermedio del propio workflow sí pasaría esta
            // criba; si algún día muerde, la criba se afina por paso, no se quita.)
            L("el mensaje es idéntico al de la línea base: señal VIEJA, no confirma este guardado");
            return Result("error", errorCode: "SAP_SUCCESS_SIGNAL_NOT_FOUND", detailCode: "STALE_MESSAGE");
        }

        if (msg.Type == "S")
            return Result("ok", folio: FolioFrom(msg.Text), detailCode: msg.Code);

        if (msg.Type is "E" or "A")
            return Result("error", errorCode: "SAP_REPORTED_ERROR", detailCode: msg.Code);

        // W (advertencia) o I (información) no confirman un guardado. Mejor un error honesto que un
        // «exportada» apoyado en una advertencia. needs_doctor queda para cuando sepamos ENUMERAR
        // los campos que faltan; adivinarlo desde aquí sería concluir sin poder distinguir.
        return Result("error", errorCode: "SAP_SUCCESS_SIGNAL_NOT_FOUND",
            detailCode: msg.Code.Length > 0 ? msg.Code : msg.Type);
    }

    // ── Entrega del resultado: hasta el ack ──────────────────────────────────

    /// <summary>
    /// Entrega el resultado reintentando hasta el ack. Devuelve null solo ante un rechazo de
    /// CONTRATO (lease vencido, el trabajo es de otro): reintentar un rechazo solo lo repite.
    /// </summary>
    private async Task<ExportResultAck?> DeliverAsync(string exportId, ExportResultRequest result, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                ExportResultAck ack = await _graph.ReportExportResultAsync(exportId, result, ct);
                PendingResultStore.Delete(exportId);
                return ack;
            }
            catch (GraphException e) when (e.Transient)
            {
                int backoff = Math.Min(1 << Math.Min(attempt, 6), 60);
                L($"result de {exportId} sin ack (intento {attempt}: {e.Message}); se reintenta en {backoff}s. " +
                  "No es opcional: sin ack, Graph no sabe que SAP ya se escribió.");
                Status($"Reintentando confirmar la exportación (intento {attempt})…");
                await Task.Delay(TimeSpan.FromSeconds(backoff), ct);
            }
            catch (GraphException e)
            {
                // El desenlace grave. Si esto era un 'ok', SAP ya tiene la nota y otro ejecutor puede
                // re-reclamar el trabajo: doble escritura. No hay arreglo desde aquí — se deja dicho
                // con todas las letras y se descarta el pendiente (el servidor no lo va a aceptar).
                L($"✋ Graph RECHAZÓ el resultado de {exportId}: {e.Message}. " +
                  (result.Outcome == "ok"
                      ? "OJO: SAP ya se escribió; si otro equipo re-reclama este trabajo, la nota puede duplicarse. Revisar en Graph."
                      : "El trabajo lo decidirá otro claim."));
                PendingResultStore.Delete(exportId);
                Status("Graph rechazó el resultado de una exportación — revisar el registro.");
                return null;
            }
        }
    }

    /// <summary>Reenvía los resultados que una corrida anterior dejó persistidos sin ack.</summary>
    private async Task RedeliverPendingAsync(CancellationToken ct)
    {
        foreach ((string exportId, ExportResultRequest result) in PendingResultStore.LoadAll(L))
        {
            L($"reenviando el resultado pendiente de {exportId} (quedó sin ack en una corrida anterior)");
            await DeliverAsync(exportId, result, ct);
        }
    }

    // ── Ayudantes ────────────────────────────────────────────────────────────

    private ExportResultRequest Result(
        string outcome, string folio = "", string errorCode = "", string detailCode = "") => new()
    {
        Device = _device,
        Outcome = outcome,
        Folio = folio.Length > 0 ? folio : null,
        ErrorCode = errorCode.Length > 0 ? errorCode : null,
        DetailCode = detailCode.Length > 0 ? detailCode : null,
    };

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "";

    /// <summary>
    /// El folio del mensaje de éxito: el primer token que lleva un dígito («Documento 4711 grabado»
    /// → «4711»). Es trazabilidad, no verdad clínica — la señal es el TIPO del mensaje; el texto
    /// completo queda en el log local y nunca viaja a Graph.
    /// </summary>
    private static string FolioFrom(string text)
    {
        foreach (string raw in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string token = raw.Trim('.', ',', ';', ':', '(', ')', '«', '»', '"', '\'');
            if (token.Length > 0 && token.Any(char.IsDigit)) return token;
        }
        return "";
    }
}

/// <summary>
/// Resultados persistidos a disco entre el «SAP ya se escribió» y el ack de Graph. Es la pieza que
/// hace que el reintento sobreviva a un cierre de la app — el mismo motivo por el que existe
/// PendingFinish en el cliente. Un archivo por trabajo, en %LOCALAPPDATA%\U\export-results\.
/// </summary>
internal static class PendingResultStore
{
    private sealed class Entry
    {
        [JsonPropertyName("export_id")] public string ExportId { get; set; } = "";
        [JsonPropertyName("saved_at")] public string SavedAt { get; set; } = "";
        [JsonPropertyName("result")] public ExportResultRequest? Result { get; set; }
    }

    private static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "U", "export-results");

    private static string PathFor(string exportId) =>
        Path.Combine(Folder, new string(exportId.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray()) + ".json");

    public static void Save(string exportId, ExportResultRequest result, Action<string>? log)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var entry = new Entry { ExportId = exportId, SavedAt = DateTime.Now.ToString("s"), Result = result };
            File.WriteAllText(PathFor(exportId), JsonSerializer.Serialize(entry));
        }
        catch (Exception e)
        {
            // Sin disco se sigue: el reintento en memoria cubre la sesión. Pero se avisa, porque lo
            // que se perdió es la garantía de reenvío tras un cierre.
            log?.Invoke($"no se pudo persistir el resultado de {exportId}: {e.Message} — " +
                        "si la app se cierra antes del ack, este resultado se pierde");
        }
    }

    public static void Delete(string exportId)
    {
        try { File.Delete(PathFor(exportId)); } catch { }
    }

    public static IReadOnlyList<(string ExportId, ExportResultRequest Result)> LoadAll(Action<string>? log)
    {
        var list = new List<(string, ExportResultRequest)>();
        try
        {
            if (!Directory.Exists(Folder)) return list;
            foreach (string file in Directory.GetFiles(Folder, "*.json"))
            {
                try
                {
                    Entry? e = JsonSerializer.Deserialize<Entry>(File.ReadAllText(file));
                    if (e?.Result != null && !string.IsNullOrWhiteSpace(e.ExportId))
                        list.Add((e.ExportId, e.Result));
                    else
                        File.Delete(file); // ilegible o incompleto: reenviarlo sería inventar
                }
                catch (Exception ex) { log?.Invoke($"pendiente ilegible ({Path.GetFileName(file)}): {ex.Message}"); }
            }
        }
        catch (Exception e) { log?.Invoke($"no se pudieron leer los resultados pendientes: {e.Message}"); }
        return list;
    }
}
