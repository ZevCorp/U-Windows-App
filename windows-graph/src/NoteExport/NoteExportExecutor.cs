using U.Graph.Surfaces;

namespace U.Graph.NoteExport;

/// <summary>Cómo terminó un trabajo, ya reportado y con ack de Graph.</summary>
public sealed record ExportJobOutcome(
    string ExportId, string Outcome, string Folio, string ErrorCode,
    string Status, bool ConsultationExported, bool Idempotent);

/// <summary>Lo que se le enseña a quien aprueba antes de escribir en la historia de un paciente.</summary>
public sealed record ExportApprovalRequest(
    ExportJobInfo Export,
    ExportPayload Payload,
    PatientInspection Patient,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<(string Label, string Value)> Summary);

/// <summary>Devuelve true para escribir. Lo implementa el cliente con una ventana; ver FaceWindow.</summary>
public delegate Task<bool> ExportApproval(ExportApprovalRequest request, CancellationToken ct);

/// <summary>
/// El ejecutor real de la cola de exportaciones clínicas: reemplaza al simulador de Graph
/// (<c>scripts/simulate-operations-executor.js</c>) hablando su mismo carril <c>/api/v1</c>.
///
/// Ciclo: reclamar → revisar → verificar el paciente → ejecutar el workflow en SAP → verificar la
/// señal de SAP → reportar hasta el ack. Trabaja por PULL: nunca acepta conexiones entrantes, así
/// que funciona detrás del firewall del hospital.
///
/// Las tres reglas que no se negocian, porque de ellas depende que «exportada» diga la verdad:
///
///   1. <c>ok</c> SOLO con la señal de éxito de SAP verificada (mensaje tipo S de la barra de
///      estado, distinto del que ya había). Terminar el workflow sin fallos NO es la señal.
///   2. El resultado se reintenta HASTA el ack, y se persiste ANTES del primer envío. Si no llega,
///      Graph no sabe que SAP ya se escribió, vence el lease y el trabajo se vuelve a servir.
///   3. No se escribe en la historia de un paciente sin que la identidad esté verificada — por
///      máquina si algún día se puede, y mientras tanto por una persona. Ver <see cref="PatientGuard"/>.
///
/// La IDENTIDAD (<c>device</c>) debe ser idéntica en el claim y en el result: Graph la compara como
/// string exacto contra <c>claimed_by</c>, y un cambio devuelve 409 EXPORT_NOT_OWNED, deja el trabajo
/// bloqueado hasta que venza el lease y quema uno de los tres intentos. Por eso se fija una vez en
/// el constructor, viaja en ambas llamadas y se persiste dentro del resultado pendiente.
/// </summary>
public sealed class NoteExportExecutor
{
    private readonly GraphClient _graph;
    private readonly GraphConfig _config;
    private readonly string _device;
    private readonly IUiSurface[] _surfaces;
    private readonly SapGuiSurface? _sap;
    private readonly ExportJournal _journal;
    private readonly PatientGuard _patients;

    /// <summary>Cada cuánto se pregunta por trabajo cuando la cola está vacía.</summary>
    public int PollSeconds { get; set; } = 15;

    /// <summary>Cuánto se espera la señal de SAP tras el último paso. Un guardado real puede tardar;
    /// un plazo corto produciría «sin confirmación» sobre exportaciones que sí funcionaron.</summary>
    public int SignalTimeoutMs { get; set; } = 20_000;

    /// <summary>Qué se hace cuando la identidad del paciente no se puede verificar por máquina.</summary>
    public PatientPolicy PatientPolicy { get; set; } = PatientPolicy.OperatorConfirms;

    /// <summary>Log de diagnóstico. El cliente enchufa su LogBus, igual que en WorkflowPlayer.</summary>
    public Action<string>? Log { get; set; }
    private void L(string msg) { try { Log?.Invoke(msg); } catch { } }

    /// <summary>Alineación consciente (abrir/enfocar SAP). Mismo delegate que usa el player.</summary>
    public SurfaceAligner? Aligner { get; set; }

    /// <summary>La aprobación humana. Sin esto, <see cref="PatientPolicy.OperatorConfirms"/> no puede
    /// cumplirse y todo trabajo acaba en needs_doctor — que es el fallo seguro, no el cómodo.</summary>
    public ExportApproval? Approval { get; set; }

    /// <summary>
    /// Motivo por el que ahora no se debe reclamar («enseñando», «hay un workflow a mano»), o "".
    /// Se pregunta ANTES del claim: reclamar sin poder ejecutar quema un intento y bloquea el lease
    /// diez minutos, y solo hay tres intentos por trabajo.
    /// </summary>
    public Func<string>? HoldReason { get; set; }

    public event EventHandler<string>? StatusChanged;
    private void Status(string s) { try { StatusChanged?.Invoke(this, s); } catch { } }

    public event EventHandler<ExportJobOutcome>? JobDone;

    public bool Working { get; private set; }

    private string _lastNote = "";

    public NoteExportExecutor(
        GraphClient graph, GraphConfig config, string device,
        ExportJournal? journal = null, params IUiSurface[] surfaces)
    {
        _graph = graph;
        _config = config;
        _device = string.IsNullOrWhiteSpace(device) ? "operations-sin-nombre" : device.Trim();
        _surfaces = surfaces;
        _sap = surfaces.OfType<SapGuiSurface>().FirstOrDefault();
        _journal = journal ?? new ExportJournal(s => L(s));
        _patients = new PatientGuard(config.PatientFieldNames);
    }

    /// <summary>El loop. Corre hasta que se cancele; cada vuelta atiende como mucho un trabajo.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        L($"ejecutor encendido · device='{_device}' · política de paciente={PatientPolicy} · sondeo {PollSeconds}s");
        if (PatientPolicy == PatientPolicy.Off)
        {
            L("✋ AVISO: la verificación del paciente está DESACTIVADA. Este modo es solo para entornos " +
              "de prueba con datos ficticios: se escribirá en el paciente que SAP tenga abierto, sea cual sea.");
        }
        Status("Atendiendo la cola de exportaciones.");

        // Antes de reclamar nada: cerrar lo que dejó la corrida anterior. El orden importa — entregar
        // un resultado pendiente ANTES de pedir trabajo nuevo evita competir contra uno mismo.
        try { await RecoverAsync(ct); }
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
                    Note(e.Transient
                        ? $"Graph no respondió al claim ({e.Message}); se sigue intentando"
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
        L("ejecutor apagado");
    }

    /// <summary>Un aviso de sondeo se registra UNA vez por cambio, no en cada vuelta.</summary>
    private void Note(string msg)
    {
        if (msg == _lastNote) return;
        _lastNote = msg;
        L(msg);
    }

    /// <summary>Entrega lo que quedó sin ack y marca como incierto lo que quedó a medio ejecutar.</summary>
    private async Task RecoverAsync(CancellationToken ct)
    {
        foreach (ExportRecord record in _journal.Recover())
        {
            if (record.Result == null) continue;
            L($"reenviando el resultado pendiente de {record.ExportId} (quedó sin ack en una corrida anterior)");
            Status("Confirmando una exportación que quedó pendiente…");
            await DeliverAsync(record.ExportId, record.Result, ct);
        }
    }

    // ── Un trabajo, de punta a punta ─────────────────────────────────────────

    private async Task HandleAsync(ExportJobResponse job, CancellationToken ct)
    {
        ExportJobInfo export = job.Export!;
        Working = true;
        bool touchedSap = false;
        try
        {
            ExportRecord? previous = _journal.Find(export.Id);
            _journal.Begin(export.Id, _device, export.Attempts);

            L($"▶ trabajo {export.Id} · workflow='{export.WorkflowId}' · intento {export.Attempts} · " +
              $"lease hasta {export.LeaseExpiresAt}");
            Status($"Revisando una exportación (intento {export.Attempts})…");

            ExportResultRequest result;
            try
            {
                (result, touchedSap) = await ProcessAsync(job, previous, ct);
            }
            catch (OperationCanceledException)
            {
                // Apagado a media faena. Si ya se tocó SAP no se inventa un desenlace: se marca
                // incierto para que la próxima vez que Graph sirva este trabajo se pida intervención
                // en vez de escribirlo otra vez.
                if (touchedSap)
                {
                    _journal.MarkUncertain(export.Id, "el ejecutor se detuvo durante la escritura en SAP");
                    L($"✋ detenido a mitad del trabajo {export.Id} CON SAP YA TOCADO: no se reporta nada. " +
                      "El lease vencerá y el trabajo volverá a la cola, donde se pedirá intervención " +
                      "porque nadie sabe si la nota quedó escrita.");
                }
                else
                {
                    L($"detenido antes de tocar SAP en el trabajo {export.Id}: no hay nada que deshacer.");
                }
                throw;
            }

            // El resultado se persiste ANTES del primer envío: si la aplicación muere entre esto y el
            // ack, al arrancar se reenvía. Perderlo dejaría a Graph sin saber que SAP ya se escribió.
            _journal.SaveResult(export.Id, result);

            ExportResultAck? ack = await DeliverAsync(export.Id, result, ct);
            if (ack == null) return; // rechazo de contrato, ya registrado con su explicación

            ReportToOperator(export, result, ack);
        }
        finally { Working = false; }
    }

    /// <summary>
    /// Decide y, si procede, ejecuta. Devuelve el resultado a reportar y si se llegó a tocar SAP.
    /// Nunca lanza por fallos de ejecución: un trabajo reclamado que no reporta nada quema un intento
    /// y bloquea el lease, así que todo desenlace tiene que convertirse en un result tipado.
    /// </summary>
    private async Task<(ExportResultRequest Result, bool TouchedSap)> ProcessAsync(
        ExportJobResponse job, ExportRecord? previous, CancellationToken ct)
    {
        ExportJobInfo export = job.Export!;

        // ── Revisión del trabajo ────────────────────────────────────────────
        JobReview review = ExportJobReview.Review(job, previous, DateTimeOffset.Now);
        foreach (string warning in review.Warnings) L($"  ⚠ {warning}");
        if (!review.CanProceed)
        {
            L($"no se ejecuta: {review.Decision.Explanation}");
            return (ResultFrom(review.Decision), false);
        }

        SapGuiSurface? sap = _sap;
        if (sap == null)
            return (ResultFrom(ExportDecision.Fail("SIN_SUPERFICIE_SAP",
                "Este cliente se construyó sin superficie SAP.")), false);

        SurfaceAvailability availability = sap.Check();
        if (!availability.Available)
        {
            L($"SAP no disponible: {availability.Reason}");
            return (ResultFrom(ExportDecision.Fail("SAP_NO_DISPONIBLE", availability.Reason)), false);
        }

        // ── La compuerta del paciente ───────────────────────────────────────
        ExportPayload payload = job.Payload!;
        PatientInspection patient = await Task.Run(() => _patients.Inspect(sap, payload), ct);
        // Al registro van los IDS de los campos hallados, jamás sus valores: ahí vive el nombre y el
        // documento del paciente, y un registro es un archivo que se copia y se comparte.
        L($"identidad en pantalla: {patient.LogSummary} · pantalla={patient.Screen}" +
          (patient.AutoVerdict.Length > 0 ? $" · veredicto automático={patient.AutoVerdict}" : ""));

        ExportDecision? blocked = await CheckPatientAsync(export, payload, patient, review.Warnings, ct);
        if (blocked != null) return (ResultFrom(blocked), false);

        // ── Ejecución ───────────────────────────────────────────────────────
        // La línea base de la barra de estado se toma ANTES de ejecutar: sin ella, un mensaje de
        // éxito que llevaba rato en pantalla se leería como la confirmación de este guardado.
        SapGuiSurface.SapStatusMessage? baseline =
            await Task.Run(() => sap.AwaitStatusbarMessage(1_500, ct), ct);
        if (baseline != null) L($"barra de estado antes de ejecutar: tipo='{baseline.Type}' código='{baseline.Code}'");

        // A partir de aquí SAP puede quedar escrito: el diario lo registra ANTES del primer clic.
        _journal.Mark(export.Id, ExportPhase.Executing);
        Status("Escribiendo la nota en el sistema hospitalario…");

        RunResult run;
        try
        {
            run = await BuildPlayer().RunAsync(
                export.WorkflowId!, VariablesFor(payload), strictSurface: true, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e)
        {
            L($"el player lanzó: {e.Message}");
            _journal.MarkUncertain(export.Id, "la automatización falló de forma inesperada");
            return (ResultFrom(ExportDecision.Fail("AUTOMATIZACION_ROTA",
                "La automatización se interrumpió de forma inesperada.")), true);
        }

        if (!run.Ok)
        {
            // El workflow paró en un paso. Puede haber escrito parte del formulario, pero NO llegó a
            // guardar: sin guardado no hay nada en la historia clínica, así que esto es un fallo
            // limpio y reintentable, no un estado incierto.
            L($"workflow detenido: {run.Error} · {run.Tally}");
            int failed = run.Steps.LastOrDefault(s => !s.Ok && !s.Omitted)?.StepOrder ?? 0;
            return (ResultFrom(ExportDecision.Fail("AUTOMATIZACION_INCOMPLETA",
                $"La automatización se detuvo antes de terminar ({run.Tally}).",
                failed > 0 ? $"PASO_{failed}" : "")), true);
        }

        // ── Verificación: lo único que puede producir un `ok` ───────────────
        L($"workflow completado ({run.Tally}); esperando la señal de SAP…");
        Status("Verificando la confirmación del sistema hospitalario…");
        SapGuiSurface.SapStatusMessage? message =
            await Task.Run(() => sap.AwaitStatusbarMessage(SignalTimeoutMs, ct), ct);

        // El texto completo se queda en el registro LOCAL: un mensaje de SAP puede nombrar al
        // paciente. Hacia Graph solo viajan el tipo, el código y el folio.
        if (message != null)
            L($"barra de estado: tipo='{message.Type}' código='{message.Code}' texto='{message.Text}'");

        _journal.Mark(export.Id, ExportPhase.Verified);
        ExportDecision decision = SapSaveSignal.Classify(baseline, message);

        if (decision.Verdict == ExportVerdict.Proceed)
        {
            string folio = SapSaveSignal.FolioFrom(message!.Text);
            L($"✓ SAP confirmó el guardado (mensaje tipo S{(folio.Length > 0 ? $", folio {folio}" : "")})");
            return (new ExportResultRequest
            {
                Device = _device,
                Outcome = "ok",
                Folio = folio.Length > 0 ? folio : null,
                DetailCode = message.Code.Length > 0 ? message.Code : null,
            }, true);
        }

        L($"NO se reporta éxito: {decision.Explanation}");
        return (ResultFrom(decision), true);
    }

    /// <summary>
    /// La verificación del paciente. Devuelve null para seguir, o la decisión que lo impide.
    ///
    /// El orden no es negociable: un paciente CONTRADICHO por la máquina frena pase lo que pase, sin
    /// que ninguna política ni ninguna aprobación humana pueda levantarlo. Escribir la nota de una
    /// persona en la historia de otra es el peor fallo que este sistema puede cometer, y es el único
    /// sitio del ejecutor donde no hay una salida «pero es que hace falta para el demo».
    /// </summary>
    private async Task<ExportDecision?> CheckPatientAsync(
        ExportJobInfo export, ExportPayload payload, PatientInspection patient,
        IReadOnlyList<string> warnings, CancellationToken ct)
    {
        if (patient.AutoVerdict == PatientGuard.Contradicted)
        {
            L("✋ FRENO: el identificador del paciente en pantalla NO coincide con el del trabajo. " +
              "No se escribe nada.");
            return ExportDecision.NeedsDoctor(
                "El paciente abierto en el sistema hospitalario no es el de esta consulta.",
                "Abrir el paciente correcto");
        }

        if (patient.AutoVerdict == PatientGuard.Verified)
        {
            L("paciente verificado por identificador: se procede sin intervención.");
            return null;
        }

        if (PatientPolicy == PatientPolicy.Off)
        {
            L("⚠ se escribe SIN verificar el paciente (política Off, solo entornos de prueba).");
            return null;
        }

        if (Approval == null)
        {
            // Sin vía de aprobación no se puede cumplir la política. Se para: el fallo seguro es no
            // escribir. Que este caso exista y sea alcanzable es deliberado — es lo que impide que
            // alguien despliegue el ejecutor sin la compuerta y no se entere.
            L("✋ no hay forma de pedir aprobación y la política la exige: no se escribe.");
            return ExportDecision.NeedsDoctor(
                "Este equipo no puede pedir confirmación del paciente y la política la exige.",
                "Confirmar el paciente en el equipo");
        }

        Status("Esperando que confirmes el paciente…");
        bool approved;
        try
        {
            approved = await Approval(new ExportApprovalRequest(
                export, payload, patient, warnings, ExportJobReview.Summarize(payload)), ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e)
        {
            L($"la ventana de aprobación falló ({e.Message}): no se escribe.");
            return ExportDecision.NeedsDoctor(
                "No se pudo mostrar la confirmación del paciente en el equipo.",
                "Confirmar el paciente en el equipo");
        }

        if (!approved)
        {
            L("el operador NO aprobó: no se escribe nada.");
            return ExportDecision.NeedsDoctor(
                "Quien opera el equipo no confirmó que el paciente abierto sea el de esta consulta.",
                "Confirmar el paciente en el equipo");
        }

        L("el operador confirmó el paciente; se procede a escribir.");
        return null;
    }

    private WorkflowPlayer BuildPlayer()
    {
        var player = new WorkflowPlayer(_graph, _config, _surfaces)
        {
            Aligner = Aligner,
            Log = s => L($"  {s}"),
        };
        player.StepDone += (_, o) =>
        {
            if (!o.Omitted) Status(o.Ok ? $"Escribiendo: ✓ {o.Label}" : $"Escribiendo: ✗ {o.Label}");
        };
        return player;
    }

    /// <summary>El texto clínico viaja como variable por el mismo canal que usa el cerebro.</summary>
    private static Dictionary<string, string>? VariablesFor(ExportPayload payload)
    {
        string context = new[] { payload.Context, payload.RenderedText }
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "";
        return context.Length > 0 ? new Dictionary<string, string> { ["context"] = context } : null;
    }

    // ── Entrega del resultado: hasta el ack ──────────────────────────────────

    /// <summary>
    /// Entrega reintentando hasta el ack. Devuelve null solo ante un rechazo de CONTRATO (lease
    /// vencido, el trabajo es de otro): repetir un 4xx solo lo repite.
    /// </summary>
    private async Task<ExportResultAck?> DeliverAsync(
        string exportId, ExportResultRequest result, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                ExportResultAck ack = await _graph.ReportExportResultAsync(exportId, result, ct);
                _journal.Complete(exportId);
                return ack;
            }
            catch (GraphException e) when (e.Transient)
            {
                int backoff = Math.Min(1 << Math.Min(attempt, 6), 60);
                L($"result de {exportId} sin ack (intento {attempt}: {e.Message}); reintento en {backoff}s. " +
                  "No es opcional: sin ack, Graph no sabe que SAP ya se escribió.");
                Status($"Reintentando confirmar la exportación (intento {attempt})…");
                await Task.Delay(TimeSpan.FromSeconds(backoff), ct);
            }
            catch (GraphException e)
            {
                // Lo grave. Si el resultado era 'ok', SAP ya tiene la nota y el trabajo puede volver a
                // la cola: doble escritura. No hay arreglo desde aquí, así que se deja dicho entero y
                // el diario conserva el rastro de lo que sí se escribió.
                bool wrote = result.Outcome == "ok";
                L($"✋ Graph RECHAZÓ el resultado de {exportId}: {e.Message}. " +
                  (wrote
                      ? "OJO: la nota YA se escribió en el sistema hospitalario. Si el trabajo vuelve a " +
                        "servirse, alguien debe comprobar que no se duplique."
                      : "El desenlace lo decidirá otro intento."));
                if (wrote) _journal.MarkUncertain(exportId, "Graph rechazó un resultado 'ok' ya escrito en SAP");
                else _journal.Complete(exportId);
                Status("Graph rechazó el resultado de una exportación — revisar el registro.");
                return null;
            }
        }
    }

    private void ReportToOperator(ExportJobInfo export, ExportResultRequest result, ExportResultAck ack)
    {
        // En un ack IDEMPOTENTE, Graph devuelve siempre consultation_exported=false y el status que la
        // fila ya tenía — no es una respuesta sobre este envío. Concluir de ahí que «se reportó ok
        // pero no se exportó» sería una alarma falsa en el caso más normal: el reenvío tras un corte
        // de red. Verificado contra la rama ALREADY_TERMINAL de la RPC.
        string line = ack.Idempotent
            ? $"↩ {export.Id}: Graph ya tenía un desenlace para este trabajo (status={ack.Status}). " +
              "Ack idempotente: no dice nada sobre este envío, y es lo esperado tras un reenvío."
            : result.Outcome == "ok"
                ? ack.ConsultationExported
                    ? $"✅ consulta EXPORTADA{(string.IsNullOrEmpty(result.Folio) ? "" : $" · folio {result.Folio}")}"
                    : "⚠ se reportó ok y Graph lo aceptó, pero la consulta NO pasó a exportada: " +
                      "había dejado de estar aprobada. La nota SÍ está escrita en SAP."
                : $"↩ outcome={result.Outcome} · código={result.ErrorCode ?? "—"} · status={ack.Status}";
        L(line);

        Status(result.Outcome == "ok" && ack.ConsultationExported
            ? "✓ Consulta exportada. Atendiendo la cola."
            : $"Trabajo terminado en {ack.Status}. Atendiendo la cola.");

        try
        {
            JobDone?.Invoke(this, new ExportJobOutcome(
                export.Id, result.Outcome, result.Folio ?? "", result.ErrorCode ?? "",
                ack.Status, ack.ConsultationExported, ack.Idempotent));
        }
        catch { }
    }

    /// <summary>
    /// Traduce una decisión al cuerpo del result. <c>unresolved_fields</c> se limita a 12 etiquetas:
    /// Miracle Notes las concatena en una sola frase para el médico, y Graph trunca en 50 sin avisar.
    /// </summary>
    private ExportResultRequest ResultFrom(ExportDecision decision) => decision.Verdict switch
    {
        ExportVerdict.NeedsDoctor => new ExportResultRequest
        {
            Device = _device,
            Outcome = "needs_doctor",
            UnresolvedFields = (decision.UnresolvedFields ?? Array.Empty<string>())
                .Where(f => !string.IsNullOrWhiteSpace(f)).Take(12).ToList(),
        },
        _ => new ExportResultRequest
        {
            Device = _device,
            Outcome = "error",
            ErrorCode = decision.ErrorCode.Length > 0 ? decision.ErrorCode : "EXECUTOR_ERROR",
            DetailCode = decision.DetailCode.Length > 0 ? decision.DetailCode : null,
        },
    };
}
