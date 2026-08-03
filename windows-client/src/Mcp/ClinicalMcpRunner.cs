using System.Text;
using System.Text.Json;
using U.Graph;
using U.Graph.Clinical;
using U.WindowsClient.Agent;
using U.WindowsClient.Diagnostics;
using U.WindowsClient.Telemetry;

namespace U.WindowsClient.Mcp;

/// <summary>
/// Ejecuta las llamadas MCP <c>notes_*</c> que decide el cerebro: Miracle Notes
/// pilotado por API, sin tocar una sola pantalla. Graph declara el catálogo SOLO
/// cuando este equipo tiene un médico vinculado (el turno viaja con el token
/// per-install); aquí vive el CÓMO — mismo reparto que <see cref="WorkflowMcpRunner"/>.
///
/// Dos reglas que el catálogo le promete al modelo y este runner cumple:
///
///  · EL DICTADO NO PASA POR LA PLUMA DEL MODELO. <c>notes_guardar_dictado</c>
///    ignora cualquier argumento y toma el texto del buffer local de dictado
///    (lo que el micrófono oyó, tal cual). Fidelidad literal, menos PHI en el
///    contexto del modelo, y cero tokens re-tecleando transcripciones.
///
///  · FIRMAR Y EXPORTAR NO EXISTEN AQUÍ. Eso lo hace el médico en el portal;
///    lo que este runner produce son borradores en su historial.
///
/// PHI: se loguean ids y status, jamás transcripciones ni cuerpos de nota.
/// </summary>
public sealed class ClinicalMcpRunner
{
    private readonly GraphConfig _graphConfig;
    private readonly ClinicalApiClient _api;
    private readonly IVoice _voice;

    private readonly object _gate = new();
    private readonly StringBuilder _dictation = new();
    private string _currentEncounterId = "";

    /// <summary>Se dispara con el código de emparejamiento cuando Graph responde DEVICE_NOT_PAIRED,
    /// para que la UI lo pinte grande además de que el cerebro lo diga.</summary>
    public event EventHandler<string>? PairingCodeAvailable;

    /// <summary>Se abrió una consulta nueva (arg: encounter id). La UI arranca aquí el dictado continuo.</summary>
    public event EventHandler<string>? EncounterOpened;

    /// <summary>Va a generarse la nota: el dictado de la consulta terminó y la UI apaga el micrófono.</summary>
    public event EventHandler? GenerationStarted;

    public ClinicalMcpRunner(GraphConfig graphConfig, IVoice voice, ClinicalApiClient? api = null)
    {
        _graphConfig = graphConfig;
        _voice = voice;
        _api = api ?? new ClinicalApiClient(graphConfig);
    }

    public static bool IsClinicalTool(string? tool) =>
        (tool ?? "").StartsWith("notes_", StringComparison.OrdinalIgnoreCase);

    /// <summary>La consulta sobre la que trabajan dictado/nota/ajustes. La fija notes_nueva_consulta.</summary>
    public string CurrentEncounterId { get { lock (_gate) return _currentEncounterId; } }

    /// <summary>
    /// El buffer de dictado. Lo alimenta quien capture voz de consulta (no los
    /// comandos al asistente): cada tramo oído se apila aquí hasta que el cerebro
    /// llame notes_guardar_dictado.
    /// </summary>
    public void AppendDictation(string heard)
    {
        if (string.IsNullOrWhiteSpace(heard)) return;
        lock (_gate)
        {
            if (_dictation.Length > 0) _dictation.Append('\n');
            _dictation.Append(heard.Trim());
        }
    }

    public async Task<string> RunAsync(string tool, IReadOnlyDictionary<string, string> args, CancellationToken ct)
    {
        string A(string k) => args.TryGetValue(k, out var v) ? v.Trim() : "";
        LogBus.Log("notes", $"MCP invoca {tool}");
        TelemetryBus.Emit("mcp", label: tool);

        try
        {
            return await DispatchAsync(tool, A, ct);
        }
        catch (ClinicalApiException e) when (e.Code == "DEVICE_NOT_ENROLLED")
        {
            // Primera vez en esta máquina: enrolar con la key embebida (lo único
            // que esa key puede hacer) y reintentar UNA vez. Cero config.
            try
            {
                LogBus.Log("notes", "sin token per-install: enrolando esta instalación…");
                var device = await _api.EnrollAsync(Environment.MachineName, ct);
                LogBus.Log("notes", $"enrolado como {device.DeviceId}");
                return await DispatchAsync(tool, A, ct);
            }
            catch (ClinicalApiException enrollError)
            {
                LogBus.Log("notes", $"enrolamiento falló: {enrollError.Code}");
                return $"este equipo no se pudo enrolar en Graph ({enrollError.Message}). Sin enrolamiento no hay carril clínico.";
            }
        }
        catch (ClinicalApiException e) when (e.Code == "DEVICE_NOT_PAIRED")
        {
            return await OfferPairingAsync(ct);
        }
        catch (ClinicalApiException e)
        {
            LogBus.Log("notes", $"{tool} falló: {e.Code} (HTTP {e.StatusCode})");
            return $"la herramienta falló: {e.Message}{(e.Transient ? " Puede reintentarse." : "")}";
        }
    }

    private async Task<string> DispatchAsync(string tool, Func<string, string> A, CancellationToken ct)
    {
        switch (tool.ToLowerInvariant())
        {
            case "notes_listar_plantillas":
            {
                var templates = await _api.ListTemplatesAsync(A("especialidad"), ct);
                var active = templates.Where(t => t.Status != "archived").ToList();
                if (active.Count == 0)
                    return "el médico no tiene plantillas activas todavía; crea una con notes_crear_plantilla";
                return string.Join("\n", active.Select(t =>
                    $"{t.Id} · «{t.Name}» ({t.Specialty}, {t.SectionsCount} secciones{(t.Scope == "institutional" ? ", institucional" : "")})"));
            }

            case "notes_crear_plantilla":
            {
                var sections = A("secciones").Split('|')
                    .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                if (sections.Count == 0)
                    return "faltan las secciones: pásalas separadas por «|», p.ej. «Motivo de consulta|Plan»";
                var template = await _api.CreateTemplateAsync(A("nombre"), A("especialidad"), sections, ct);
                return $"plantilla creada: {template.Id} · «{template.Name}» con {sections.Count} secciones";
            }

            case "notes_nueva_consulta":
            {
                string tipo = A("tipo");
                var created = await _api.CreateEncounterAsync(
                    A("plantilla_id"), string.IsNullOrWhiteSpace(tipo) ? "presencial" : tipo, ct);
                lock (_gate)
                {
                    _currentEncounterId = created.EncounterId;
                    _dictation.Clear();
                }
                LogBus.Log("notes", $"consulta actual: {created.EncounterId}");
                EncounterOpened?.Invoke(this, created.EncounterId);
                return $"consulta {created.EncounterId} abierta y fijada como la actual. El dictado que se oiga desde ahora se guarda en ella con notes_guardar_dictado.";
            }

            case "notes_guardar_dictado":
            {
                string encounterId = RequireCurrent();
                string pending;
                lock (_gate) pending = _dictation.ToString();
                if (string.IsNullOrWhiteSpace(pending))
                    return "el buffer de dictado está vacío: no se ha oído dictado nuevo desde la última guardada";
                // El backend REEMPLAZA la transcripción; el append se compone aquí
                // leyendo lo ya guardado (verificado: saveTranscript pisa, no suma).
                var current = await _api.GetEncounterAsync(encounterId, ct);
                string merged = string.IsNullOrWhiteSpace(current.Transcript)
                    ? pending
                    : $"{current.Transcript}\n{pending}";
                var saved = await _api.SaveTranscriptAsync(encounterId, merged, ct);
                lock (_gate) _dictation.Clear();
                LogBus.Log("notes", $"dictado guardado en {encounterId} ({saved.TranscriptLength} caracteres en total)");
                return $"dictado guardado ({saved.TranscriptLength} caracteres en total en la consulta)";
            }

            case "notes_generar_nota":
                return await GenerateWithRescueAsync(RequireCurrent(), ct);

            case "notes_ajustar_nota":
            {
                string encounterId = RequireCurrent();
                string instruction = A("instruccion");
                if (string.IsNullOrWhiteSpace(instruction))
                    return "falta la instrucción de ajuste";
                var proposal = await _api.AdjustNoteAsync(encounterId, instruction, A("seccion"), ct);
                if (proposal.ProposedNoteJson is not JsonElement note || proposal.ChangedSections.Count == 0)
                    return $"no hubo nada que ajustar: {proposal.Explanation}";
                // La propuesta no persiste sola (regla del backend): se aplica con PUT /note,
                // que además refresca el historial del médico (mirror) en este carril.
                var saved = await _api.SaveNoteAsync(encounterId, note, ct);
                string mirror = saved.Mirror == null ? ""
                    : saved.Mirror.Refreshed ? " El historial del médico quedó actualizado."
                    : saved.Mirror.Reason == "web_edito"
                        ? " OJO: el médico editó la nota en el portal; su versión manda y el historial NO se tocó."
                        : $" El historial no se refrescó ({saved.Mirror.Reason}).";
                return $"nota ajustada y guardada ({string.Join(", ", proposal.ChangedSections)}). {proposal.Explanation}.{mirror}";
            }

            case "notes_estado":
            {
                string encounterId = RequireCurrent();
                var encounter = await _api.GetEncounterAsync(encounterId, ct);
                int sections = encounter.NoteJson is JsonElement n && n.ValueKind == JsonValueKind.Object
                    && n.TryGetProperty("sections", out var s) && s.ValueKind == JsonValueKind.Array
                    ? s.GetArrayLength() : 0;
                return $"consulta {encounter.Id}: estado {encounter.Status}, dictado de {encounter.Transcript.Length} caracteres, "
                    + $"{(sections > 0 ? $"nota con {sections} secciones" : "sin nota todavía")}. "
                    + $"Para abrírsela al médico: open_url con {PortalUrl(encounter.Id)}";
            }

            case "notes_listar_consultas":
            {
                var (desde, hasta) = RangeFrom(A("rango"));
                var rows = await _api.ListConsultationsAsync(A("estado"), desde, hasta, ct);
                if (rows.Count == 0) return "sin consultas en ese rango";
                return string.Join("\n", rows.Select(r =>
                    $"{r.Fecha[..Math.Min(16, r.Fecha.Length)]} · {r.Estado} · «{r.Motivo}» ({r.Especialidad}) · abrir: {PortalUrl(r.Id)}"));
            }

            default:
                return $"herramienta clínica desconocida: {tool}";
        }
    }

    /// <summary>
    /// generate-note es síncrono en el backend y Vercel corta a los 60 s. Aquí se
    /// intenta en línea con 75 s y, si se corta, se sondea el estado cada 5 s
    /// hasta 2 minutos: el encounter quedó en note_generating y el RESCATE de
    /// Graph lo termina — estos mismos GET son el tráfico que lo dispara. No hay
    /// cola async que duplicar: la infraestructura ya existe y se llama rescate.
    /// </summary>
    private async Task<string> GenerateWithRescueAsync(string encounterId, CancellationToken ct)
    {
        GenerationStarted?.Invoke(this, EventArgs.Empty);
        _voice.Narrate("Generando la nota…");
        try
        {
            using var inline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            inline.CancelAfter(TimeSpan.FromSeconds(75));
            var res = await _api.GenerateNoteAsync(encounterId, inline.Token);
            return DescribeGenerated(res.Status, res.NoteJson);
        }
        catch (Exception e) when (e is OperationCanceledException && !ct.IsCancellationRequested
                                  || e is ClinicalApiException { Transient: true })
        {
            LogBus.Log("notes", $"generate-note no contestó en línea; sondeando {encounterId}…");
        }

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(120);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            var encounter = await _api.GetEncounterAsync(encounterId, ct);
            if (encounter.Status is "note_generated" or "completed")
                return DescribeGenerated(encounter.Status, encounter.NoteJson);
            if (encounter.Status == "failed")
                return "la generación de la nota FALLÓ en el backend; revisa el dictado y reintenta";
        }
        return "la nota sigue generándose (el backend la rescata solo); consulta notes_estado en un momento";
    }

    private string DescribeGenerated(string status, JsonElement? noteJson)
    {
        int sections = noteJson is JsonElement n && n.ValueKind == JsonValueKind.Object
            && n.TryGetProperty("sections", out var s) && s.ValueKind == JsonValueKind.Array
            ? s.GetArrayLength() : 0;
        _voice.Narrate("Nota lista.");
        LogBus.Log("notes", $"nota generada · status={status} · {sections} secciones");
        return $"nota generada ({sections} secciones). Ya aparece como borrador en el historial del médico; "
             + "puede ajustarse con notes_ajustar_nota o abrírsela con open_url (ver notes_estado)";
    }

    private async Task<string> OfferPairingAsync(CancellationToken ct)
    {
        try
        {
            var pair = await _api.PairCodeAsync(ct);
            PairingCodeAvailable?.Invoke(this, pair.Code);
            _voice.Narrate($"Este equipo no está vinculado a un médico. El código es {string.Join(" ", pair.Code.ToCharArray())}.");
            LogBus.Log("notes", "DEVICE_NOT_PAIRED → código de emparejamiento emitido (no se loguea el código)");
            return $"este equipo no está vinculado a ningún médico. Pídele al médico que entre a Miracle Notes → Equipos "
                 + $"y teclee el código {pair.Code} (vence en 10 minutos). Después reintenta la herramienta.";
        }
        catch (ClinicalApiException e)
        {
            return $"este equipo no está vinculado a un médico y no se pudo pedir el código de emparejamiento: {e.Message}";
        }
    }

    private string RequireCurrent()
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(_currentEncounterId))
                throw new ClinicalApiException("ENCOUNTER_NOT_FOUND",
                    "no hay consulta actual: abre una con notes_nueva_consulta");
            return _currentEncounterId;
        }
    }

    /// <summary>El deep link del portal (misma resolución de base que ClinicalBridge).</summary>
    private static string PortalUrl(string consultationId)
    {
        string baseUrl = (Environment.GetEnvironmentVariable("MIRACLE_PORTAL_URL") ?? "https://itsmiracleai.com.co")
            .TrimEnd('/');
        return $"{baseUrl}/app/consultas/{Uri.EscapeDataString(consultationId)}";
    }

    /// <summary>«hoy» y «semana» se calculan AQUÍ, en la zona horaria del consultorio — el servidor no adivina.</summary>
    private static (string desde, string hasta) RangeFrom(string rango) => rango switch
    {
        "todas" => ("", ""),
        "semana" => (DateTime.Now.Date.AddDays(-7).ToUniversalTime().ToString("o"), ""),
        _ => (DateTime.Now.Date.ToUniversalTime().ToString("o"), ""),
    };
}
