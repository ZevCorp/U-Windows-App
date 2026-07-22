using U.Graph.Surfaces;

namespace U.Graph;

/// <summary>Cómo fue un paso. La UI narra esto mientras el workflow corre.</summary>
public sealed record StepOutcome(int StepOrder, string Label, string ActionType, bool Ok, string Error);

/// <summary>Resultado de ejecutar un workflow entero.</summary>
public sealed record RunResult(bool Ok, string WorkflowId, IReadOnlyList<StepOutcome> Steps, string Error)
{
    public int Completed => Steps.Count(s => s.Ok);

    /// <summary>
    /// True si hubo que alinearse conscientemente (abrir/enfocar la app) porque no estábamos en la
    /// superficie del workflow. El caller usa esto para APRENDER: prepend del step de alineación al
    /// workflow, así la próxima vez corre entero por el sistema subconsciente. Es el loop consciente→subconsciente.
    /// </summary>
    public bool AlignedConsciously { get; init; }
}

/// <summary>
/// Lleva el foco a la superficie donde nació el workflow (<paramref name="targetOrigin"/>), consultando
/// <paramref name="currentOrigin"/> para confirmar. Lo implementa el cliente (AppAligner): enfocar o
/// abrir la app. Devuelve true si al terminar estamos en el origin.
/// </summary>
public delegate Task<bool> SurfaceAligner(string targetOrigin, Func<string> currentOrigin, CancellationToken ct);

/// <summary>
/// Ejecuta un workflow de Graph sobre la superficie que toque.
///
/// El reparto de responsabilidades es el de Graph y no se negocia: el backend decide QUÉ hacer
/// (devuelve el plan) y este proceso decide CÓMO tocarlo en Windows. Graph nunca controla la máquina.
///
/// PARA ARRIBA EN PRODUCCIÓN: esto corre sobre el SAP real de un cliente, así que las dos decisiones
/// de diseño importantes son conservadoras —
///   1. Se verifica que estamos en la pantalla correcta ANTES de tocar nada.
///   2. Al primer paso que falla, se para. Un formulario a medio llenar es recuperable por un humano;
///      uno llenado a ciegas con los campos corridos, no.
/// </summary>
public sealed class WorkflowPlayer
{
    private readonly GraphClient _graph;
    private readonly GraphConfig _config;
    private readonly IReadOnlyList<IUiSurface> _surfaces;

    /// <summary>Se dispara por cada paso ejecutado.</summary>
    public event EventHandler<StepOutcome>? StepDone;

    /// <summary>
    /// Log de diagnóstico (opcional). windows-graph no depende de windows-client, así que el cliente
    /// enchufa aquí su LogBus. Registra las decisiones de superficie/alineación/steps — el punto ciego
    /// que hacía difícil ver por qué un run se alineaba y el siguiente no.
    /// </summary>
    public Action<string>? Log { get; set; }
    private void L(string msg) { try { Log?.Invoke(msg); } catch { } }

    /// <summary>
    /// Opcional: si está y detectamos que no estamos en la superficie del workflow, en vez de fallar
    /// se intenta alinear conscientemente (abrir/enfocar la app) y recién ahí ejecutar. Lo pone el
    /// cliente (AppAligner.EnsureAsync). Sin él, el comportamiento es el de antes: fallar con el mismatch.
    /// </summary>
    public SurfaceAligner? Aligner { get; set; }

    public WorkflowPlayer(GraphClient graph, GraphConfig config, params IUiSurface[] surfaces)
    {
        _graph = graph;
        _config = config;
        _surfaces = surfaces;
    }

    /// <summary>
    /// Pide el plan a Graph y lo ejecuta. <paramref name="strictSurface"/> exige que la pantalla actual
    /// coincida con la que se grabó; ponerlo en false permite forzar la ejecución bajo tu criterio.
    /// </summary>
    public async Task<RunResult> RunAsync(
        string workflowId,
        Dictionary<string, string>? variables,
        bool strictSurface,
        CancellationToken ct)
    {
        ExecutionPlan plan;
        try
        {
            plan = await _graph.GetPlanAsync(workflowId, variables, new Dictionary<string, string>
            {
                ["source"] = "windows-u",
                ["surface"] = "native",
            }, ct);
        }
        catch (GraphException e)
        {
            return new RunResult(false, workflowId, Array.Empty<StepOutcome>(), e.Message);
        }

        L($"plan {workflowId}: {plan.Steps.Count} steps · sourceOrigin='{plan.SourceOrigin}' pathname='{plan.SourcePathname}'");
        for (int i = 0; i < plan.Steps.Count; i++)
            L($"  step[{plan.Steps[i].StepOrder}] {plan.Steps[i].ActionType} · sel='{plan.Steps[i].Selector}' · '{plan.Steps[i].Label}'");

        if (plan.Steps.Count == 0)
            return new RunResult(false, workflowId, Array.Empty<StepOutcome>(),
                "Graph no devolvió ningún paso ejecutable para este workflow.");

        // La superficie se deduce del primer paso REAL, no de una config: el workflow sabe dónde nació.
        // Los steps de alineación (`app:`) no pertenecen a ninguna superficie de ejecución — los
        // resuelve el Aligner —, así que se saltan al elegir la superficie.
        bool hasAlignmentStep = plan.Steps.Any(IsAlignmentStep);
        PlanStep? firstReal = plan.Steps.OrderBy(s => s.StepOrder).FirstOrDefault(s => !IsAlignmentStep(s));
        IUiSurface? surface = firstReal != null ? SurfaceFor(firstReal.Selector) : null;
        if (surface == null)
            return new RunResult(false, workflowId, Array.Empty<StepOutcome>(),
                $"Este workflow se grabó en una superficie que este cliente no maneja ({firstReal?.Selector ?? plan.Steps[0].Selector}).");

        var availability = surface.Check();
        if (!availability.Available)
            return new RunResult(false, workflowId, Array.Empty<StepOutcome>(), availability.Reason);

        // Si el workflow YA aprendió a alinearse (tiene un step `app:`), no se hace el pre-check: ese
        // step, al ejecutarse primero, lleva el foco a la superficie. Solo se aprende (pre-check +
        // prepend) cuando aún NO tiene el step de alineación.
        L($"superficie actual: origin='{surface.Identity().Origin}' pathname='{surface.Identity().Pathname}' · hasAlignmentStep={hasAlignmentStep} · strictSurface={strictSurface}");
        bool alignedConsciously = false;
        if (strictSurface && !hasAlignmentStep)
        {
            string? mismatch = SurfaceMismatch(surface.Identity(), plan);
            L(mismatch == null ? "pre-check: ya estamos en la superficie" : $"pre-check mismatch: {mismatch}");
            if (mismatch != null && Aligner != null && !string.IsNullOrWhiteSpace(plan.SourceOrigin))
            {
                // No estamos donde nació el workflow: alinearse conscientemente (abrir/enfocar la app)
                // y reintentar la comprobación. Este es el eslabón consciente del loop.
                L($"alineando conscientemente → {plan.SourceOrigin}");
                bool reached = await Aligner(plan.SourceOrigin, () => surface.Identity().Origin, ct);
                string? after = SurfaceMismatch(surface.Identity(), plan);
                L($"aligner reached={reached} · post-align mismatch={(after ?? "ninguno")}");
                if (reached && after == null)
                {
                    alignedConsciously = true;
                    mismatch = null;
                }
                else
                {
                    mismatch = after ?? mismatch;
                }
            }
            else if (mismatch != null)
            {
                L($"NO se intenta alinear (Aligner={(Aligner != null)}, sourceOrigin='{plan.SourceOrigin}')");
            }
            if (mismatch != null)
                return new RunResult(false, workflowId, Array.Empty<StepOutcome>(), mismatch);
        }

        var outcomes = new List<StepOutcome>();
        foreach (PlanStep step in plan.Steps.OrderBy(s => s.StepOrder))
        {
            if (ct.IsCancellationRequested)
                return new RunResult(false, workflowId, outcomes, "Ejecución cancelada.");

            // Step de alineación (`app:`): no lo ejecuta una superficie, lo resuelve el Aligner
            // (abrir/enfocar la app). Idempotente: si ya estamos ahí, no hace nada.
            if (IsAlignmentStep(step))
            {
                L($"step alineación '{step.Selector}' → objetivo '{plan.SourceOrigin}', actual '{surface.Identity().Origin}'");
                bool reached = Aligner != null
                    && await Aligner(plan.SourceOrigin, () => surface.Identity().Origin, ct);
                if (!reached) reached = SurfaceMismatch(surface.Identity(), plan) == null;
                L($"step alineación resultado: reached={reached}");
                if (!Report(step, reached, reached ? "" : "no me pude alinear con la superficie del workflow", outcomes))
                    return new RunResult(false, workflowId, outcomes,
                        $"Se detuvo en el paso {step.StepOrder} («{step.Label}»): {outcomes[^1].Error}");
                await Task.Delay(Math.Clamp(_config.StepDelayMs, 0, 5000), ct);
                continue;
            }

            // Un workflow puede cruzar superficies (empieza en UIA, sigue en SAP): se reelige por paso.
            IUiSurface? target = SurfaceFor(step.Selector) ?? surface;

            bool ok;
            try
            {
                // El resultado y el motivo se capturan en la MISMA llamada. Reintentar para recuperar
                // el error ejecutaría la acción dos veces contra el SAP del cliente.
                (bool done, string reason) = await Task.Run(() =>
                {
                    bool r = target.Execute(step, out string err);
                    return (r, err);
                }, ct);

                ok = Report(step, done, done ? "" : Reason(reason), outcomes);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                ok = Report(step, false, e.Message, outcomes);
            }

            if (!ok)
                return new RunResult(false, workflowId, outcomes,
                    $"Se detuvo en el paso {step.StepOrder} («{step.Label}»): {outcomes[^1].Error}");

            await Task.Delay(Math.Clamp(_config.StepDelayMs, 0, 5000), ct);
        }

        return new RunResult(true, workflowId, outcomes, "") { AlignedConsciously = alignedConsciously };
    }

    private bool Report(PlanStep step, bool ok, string error, List<StepOutcome> acc)
    {
        var outcome = new StepOutcome(step.StepOrder, step.Label ?? "", step.ActionType, ok, error);
        acc.Add(outcome);
        L($"step[{step.StepOrder}] {(ok ? "OK" : "FALLÓ")} '{step.Label}'{(ok ? "" : " · " + error)}");
        try { StepDone?.Invoke(this, outcome); } catch { }
        return ok;
    }

    private static string Reason(string error) =>
        string.IsNullOrWhiteSpace(error) ? "el paso no se pudo ejecutar" : error;

    /// <summary>Un step de alineación de superficie (lo antepone el aprendizaje consciente→subconsciente).</summary>
    private static bool IsAlignmentStep(PlanStep step) =>
        (step.Selector ?? "").StartsWith("app:", StringComparison.OrdinalIgnoreCase);

    private IUiSurface? SurfaceFor(string selector)
    {
        if (SapSelector.Owns(selector)) return _surfaces.FirstOrDefault(s => s.Name == "sap");
        if (UiaSelector.Owns(selector)) return _surfaces.FirstOrDefault(s => s.Name == "uia");
        return null;
    }

    /// <summary>
    /// ¿Estamos donde se grabó? Se compara el origen (sistema SAP o proceso) y la transacción/ruta.
    /// El título no: cambia con el documento abierto y daría falsos negativos constantes.
    /// </summary>
    private static string? SurfaceMismatch(SurfaceIdentity now, ExecutionPlan plan)
    {
        if (!string.IsNullOrWhiteSpace(plan.SourceOrigin) &&
            !string.Equals(now.Origin, plan.SourceOrigin, StringComparison.OrdinalIgnoreCase))
            return $"Este workflow se grabó en {plan.SourceOrigin} y ahora estás en {now.Origin}.";

        // El pathname SOLO acota superficies con ruta ESTABLE (web: URL, sapgui: transacción). En una
        // app nativa (uia://) el pathname es el TÍTULO de la ventana = el documento abierto: es
        // instancia, no identidad. Un workflow de "escribir en Notepad" debe servir en CUALQUIER nota,
        // no solo en la que se grabó. Por eso, en uia://, se scopea por app y se ignora el pathname
        // (misma razón por la que el título nunca acotó).
        bool pathnameScopes = !(plan.SourceOrigin ?? "").StartsWith("uia://", StringComparison.OrdinalIgnoreCase);
        if (pathnameScopes && !string.IsNullOrWhiteSpace(plan.SourcePathname) &&
            !string.Equals(now.Pathname, plan.SourcePathname, StringComparison.OrdinalIgnoreCase))
            return $"Este workflow se grabó en {plan.SourcePathname} y ahora estás en {now.Pathname}.";

        return null;
    }
}
