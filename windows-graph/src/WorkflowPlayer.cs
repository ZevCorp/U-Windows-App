using U.Graph.Surfaces;

namespace U.Graph;

/// <summary>Cómo fue un paso. La UI narra esto mientras el workflow corre.</summary>
public sealed record StepOutcome(int StepOrder, string Label, string ActionType, bool Ok, string Error)
{
    /// <summary>
    /// El paso NO se ejecutó: se omitió a propósito (reanudación por ubicación, salto adelante, o el
    /// operador lo saltó en el paso a paso). Omitido no es hecho, y tampoco es un fallo del workflow.
    /// Sin esta distinción los omitidos desaparecían del recuento y «se completaron 29 de 30» era una
    /// frase cierta sobre una lista de la que faltaban 19 pasos.
    /// </summary>
    public bool Omitted { get; init; }
}

/// <summary>
/// Resultado de ejecutar un workflow entero.
///
/// <see cref="Steps"/> son los pasos con VEREDICTO, que no es lo mismo que los pasos del plan: si el
/// motor se salta unos cuantos, esos no dejaban rastro y el denominador mentía. Por eso
/// <see cref="Planned"/> viaja aparte y es el único número que se puede usar como total.
/// </summary>
public sealed record RunResult(bool Ok, string WorkflowId, IReadOnlyList<StepOutcome> Steps, string Error)
{
    /// <summary>Ejecutados de verdad y con éxito. NUNCA incluye omitidos.</summary>
    public int Completed => Steps.Count(s => s.Ok && !s.Omitted);

    /// <summary>Registrados pero no ejecutados (reanudación, salto adelante, saltados a mano).</summary>
    public int Omitted => Steps.Count(s => s.Omitted);

    /// <summary>Cuántos pasos tenía el plan. 0 en resultados viejos → cae al recuento de veredictos.</summary>
    public int Planned { get; init; }

    /// <summary>El total honesto para mostrar: el del plan si se conoce.</summary>
    public int Total => Planned > 0 ? Planned : Steps.Count;

    /// <summary>Una línea que no se puede leer mal: siempre sobre el total del PLAN.</summary>
    public string Tally =>
        $"{Completed}/{Total} ejecutado(s)" + (Omitted > 0 ? $" · {Omitted} omitido(s)" : "");

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

    /// <summary>
    /// PASO A PASO. Si está, se llama ANTES de ejecutar cada paso y se espera la decisión del operador.
    ///
    /// Antes y no después, a propósito: sobre un SAP clínico el momento útil para frenar es el instante
    /// previo a la acción destructiva, no el posterior. El informe que recibe incluye el resultado del
    /// paso ANTERIOR, así que no se pierde nada por pausar antes.
    /// </summary>
    public Func<StepPause, Task<StepDecision>>? OnStepPause { get; set; }

    /// <summary>
    /// Dónde está el pantallazo que se tomó de ese paso al ENSEÑARLO (workflowId, stepOrder) → ruta.
    /// Lo pone el cliente: windows-graph no sabe de disco ni de StepShotCamera. "" si no hay.
    ///
    /// Esas capturas ya se venían tomando en cada enseñanza y no las usaba nadie. Puestas al lado de la
    /// pantalla actual son la diferencia entre depurar leyendo ids y verlo.
    /// </summary>
    public Func<string, int, string>? TaughtShotFor { get; set; }

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
    /// <summary>
    /// Ensayo en seco: pide el plan y lo analiza SIN ejecutar nada. Ver <see cref="WorkflowDryRun"/>.
    /// Comparte con <see cref="RunAsync"/> la carga del plan y el colapso de tecleos, para que lo
    /// ensayado sea EXACTAMENTE lo que se ejecutaría — un ensayo sobre otra lista no vale de nada.
    /// </summary>
    public async Task<DryRunReport> DryRunAsync(
        string workflowId, Dictionary<string, string>? variables, CancellationToken ct)
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
            return new DryRunReport(workflowId, 0,
                new[] { new DryRunFinding(DryRunLevel.Bloqueante, 0, "", $"Graph no devolvió el plan: {e.Message}") });
        }

        var steps = CollapseInputRuns(plan.Steps.OrderBy(s => s.StepOrder).ToList());
        return WorkflowDryRun.Analyze(workflowId, steps, _surfaces, L);
    }

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

        if (plan.Steps.Count == 0)
            return new RunResult(false, workflowId, Array.Empty<StepOutcome>(),
                "Graph no devolvió ningún paso ejecutable para este workflow.");

        // Varios `input` seguidos al MISMO campo son snapshots del tecleo (SetValue reemplaza el valor):
        // se colapsan al último. Escritura instantánea (1 SetValue en vez de N) y log limpio, sin tocar
        // el workflow guardado. (La deduplicación "de verdad" la hará el LLM organizador; esto es la red.)
        var steps = CollapseInputRuns(plan.Steps.OrderBy(s => s.StepOrder).ToList());
        L($"plan {workflowId}: {steps.Count} pasos (de {plan.Steps.Count} grabados) · origin='{plan.SourceOrigin}'");
        foreach (var s in steps)
            L($"  · paso {s.StepOrder}: {s.ActionType} «{s.Label}» selector={s.Selector} nodo={(string.IsNullOrWhiteSpace(s.Surface()) ? "(sin nodo)" : s.Surface())}");

        // La superficie se deduce del primer paso REAL, no de una config: el workflow sabe dónde nació.
        // Los steps de alineación (`app:`) no pertenecen a ninguna superficie de ejecución — los
        // resuelve el Aligner —, así que se saltan al elegir la superficie.
        bool hasAlignmentStep = steps.Any(IsAlignmentStep);
        // La superficie se elige del primer paso que PERTENECE a una (uia:/sap:). Los pasos de tecla
        // (`key:`) van al foco, no a una superficie, así que se saltan al elegir.
        PlanStep? firstReal = steps.FirstOrDefault(s => !IsAlignmentStep(s) && SurfaceFor(s.Selector) != null);
        IUiSurface? surface = firstReal != null ? SurfaceFor(firstReal.Selector) : null;
        if (surface == null)
            return new RunResult(false, workflowId, Array.Empty<StepOutcome>(),
                $"Este workflow se grabó en una superficie que este cliente no maneja ({firstReal?.Selector ?? plan.Steps[0].Selector}).");

        var availability = surface.Check();
        if (!availability.Available)
            return new RunResult(false, workflowId, Array.Empty<StepOutcome>(), availability.Reason);

        // El TOTAL honesto: los pasos del plan, antes de que la reanudación recorte la lista. Va aquí
        // arriba porque Finish lo estampa en TODA salida — un resultado sin total del plan es el que
        // permitía decir «2/2» de una corrida de 4 pasos.
        int planned = steps.Count;

        // MEDICIÓN de tiempos: cronómetro total + colector por paso; el resumen se loguea en CADA salida.
        var timings = new RunTimings();
        var runSw = System.Diagnostics.Stopwatch.StartNew();
        RunResult Finish(RunResult r)
        {
            r = r with { Planned = planned };
            try { L(timings.Summary(runSw.ElapsedMilliseconds)); } catch { }
            try { L($"↩ resultado: {r.Tally}{(r.Ok ? "" : " · DETENIDO")}"); } catch { }
            return r;
        }

        // LA UBICACIÓN COMO EJE: ¿ya estás parado en la superficie de algún paso del workflow? Si sí,
        // reanuda AHÍ y salta lo previo — "ubícate en cualquier nodo y haz solo lo que falta para el
        // objetivo", no siempre desde el primer paso. Solo aplica con grabaciones que traen la superficie
        // por paso (surfaceHints.observedSurface); las viejas caen al comportamiento de siempre.
        var skippedByResume = new List<PlanStep>();

        int resumeFrom = ResumeIndexFor(steps, surface.Identity());
        if (resumeFrom > 0)
        {
            L($"reanudando en el paso {steps[resumeFrom].StepOrder} — ya estás en su superficie ('{surface.Identity().Origin}'); se saltan {resumeFrom} paso(s) previos");
            // Los saltados se registran como OMITIDOS. Antes se cortaban de la lista y desaparecían sin
            // dejar rastro: el resultado decía «2/2» de un plan de 4 y sonaba a éxito completo.
            skippedByResume.AddRange(steps.Take(resumeFrom));
            steps = steps.Skip(resumeFrom).ToList();
            hasAlignmentStep = steps.Any(IsAlignmentStep); // recomputar tras el corte
        }

        // Si el workflow YA aprendió a alinearse (tiene un step `app:`), no se hace el pre-check: ese
        // step, al ejecutarse primero, lleva el foco a la superficie. Solo se aprende (pre-check +
        // prepend) cuando aún NO tiene el step de alineación. Si reanudamos a mitad, tampoco hay pre-check:
        // ya estamos parados en un nodo válido del workflow.
        L($"superficie actual: origin='{surface.Identity().Origin}' pathname='{surface.Identity().Pathname}' · hasAlignmentStep={hasAlignmentStep} · strictSurface={strictSurface} · resumeFrom={resumeFrom}");
        bool alignedConsciously = false;
        var alignSw = System.Diagnostics.Stopwatch.StartNew();
        if (resumeFrom == 0 && strictSurface && !hasAlignmentStep)
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
                return Finish(new RunResult(false, workflowId, Array.Empty<StepOutcome>(), mismatch));
        }
        timings.AlignMs = alignSw.ElapsedMilliseconds;

        var outcomes = new List<StepOutcome>();
        foreach (PlanStep s in skippedByResume) Omit(s, "reanudado por ubicación: ya estabas más adelante", outcomes);

        bool stepping = OnStepPause != null;   // «Hasta el final» lo apaga sin tocar el resto del bucle
        string lastOutcome = "";

        for (int idx = 0; idx < steps.Count; idx++)
        {
            PlanStep step = steps[idx];
            if (ct.IsCancellationRequested)
                return Finish(new RunResult(false, workflowId, outcomes, "Ejecución cancelada."));

            // Step de alineación (`app:`): no lo ejecuta una superficie, lo resuelve el Aligner
            // (abrir/enfocar la app). Idempotente: si ya estamos ahí, no hace nada.
            if (IsAlignmentStep(step))
            {
                var alignStepSw = System.Diagnostics.Stopwatch.StartNew();
                L($"step alineación '{step.Selector}' → objetivo '{plan.SourceOrigin}', actual '{surface.Identity().Origin}'");
                bool reached = Aligner != null
                    && await Aligner(plan.SourceOrigin, () => surface.Identity().Origin, ct);
                if (!reached) reached = SurfaceMismatch(surface.Identity(), plan) == null;
                L($"step alineación resultado: reached={reached}");
                timings.Add(step.StepOrder, step.Label ?? "", "align", 0, alignStepSw.ElapsedMilliseconds);
                if (!Report(step, reached, reached ? "" : "no me pude alinear con la superficie del workflow", outcomes))
                    return Finish(new RunResult(false, workflowId, outcomes,
                        $"Se detuvo en el paso {step.StepOrder} («{step.Label}»): {outcomes[^1].Error}"));
                await Task.Delay(40, ct);
                continue;
            }

            // Un workflow puede cruzar superficies (empieza en UIA, sigue en SAP): se reelige por paso.
            IUiSurface? target = SurfaceFor(step.Selector) ?? surface;

            string before = target.Identity().Url;
            L($"→ paso {step.StepOrder} «{step.Label}» ({step.ActionType}) · ubicación ANTES='{before}' · nodo grabado='{(string.IsNullOrWhiteSpace(step.Surface()) ? "(sin nodo)" : step.Surface())}'");

            // ── HUELLA: ¿es esta la MISMA pantalla, o solo la misma transacción? ────────
            // La superficie no distingue dos ESTADOS del mismo dynpro. Un clic que debia llenar un
            // panel y no lo lleno deja la misma superficie, y el paso siguiente encuentra los
            // selectores de la pantalla vieja y se ejecuta contra ellos reportando exito. Esto lo ve.
            //
            // AVISA, no detiene: la huella es nueva y todavia no sabemos que tan estable es en pantallas
            // con contenido variable. Un falso positivo que PARA un workflow en un demo es peor que uno
            // que avisa. Cuando tenga kilometros encima, se sube a bloqueante.
            string expectedPrint = step.Fingerprint();
            string livePrint = "";
            if (expectedPrint.Length > 0)
            {
                try { livePrint = target.StructureFingerprint(); } catch { }
                if (livePrint.Length > 0 && livePrint != expectedPrint)
                    L($"  ⚠ HUELLA distinta · grabada={expectedPrint} · ahora={livePrint} — misma superficie "
                      + "pero la pantalla no esta como cuando se enseño este paso");
            }

            // ── PASO A PASO ─────────────────────────────────────────────────────────────
            if (stepping && OnStepPause != null)
            {
                bool ready;
                try { ready = target.IsStepReady(step); } catch { ready = false; }

                var pause = new StepPause(
                    Index: idx + 1, Total: steps.Count, StepOrder: step.StepOrder,
                    Label: step.Label ?? "", ActionType: step.ActionType ?? "",
                    Selector: step.Selector ?? "", Value: step.Value ?? "",
                    ExpectedSurface: step.Surface() ?? "", CurrentSurface: before,
                    ElementReady: ready, PreviousOutcome: lastOutcome,
                    TaughtShotPath: SafeShot(workflowId, step.StepOrder),
                    ExpectedFingerprint: expectedPrint, CurrentFingerprint: livePrint);

                L($"⏸ {pause.Headline} · {pause.Verdict}");
                StepDecision decision;
                try { decision = await OnStepPause(pause); }
                catch (Exception e) { L($"la pausa falló ({e.Message}); se sigue sin pausar"); decision = StepDecision.HastaElFinal; }

                switch (decision)
                {
                    case StepDecision.Parar:
                        L($"⏹ detenido por el operador en el paso {step.StepOrder}");
                        return Finish(new RunResult(false, workflowId, outcomes,
                            $"Detenido a mano en el paso {step.StepOrder} («{step.Label}»)."));

                    case StepDecision.Saltar:
                        // Saltado A MANO no es lo mismo que hecho: se reporta como fallo con motivo, o
                        // el resumen diría que el workflow se completó cuando el operador lo mutiló.
                        L($"⏭ paso {step.StepOrder} SALTADO por el operador");
                        Omit(step, "saltado a mano en el paso a paso", outcomes);
                        lastOutcome = $"paso {step.StepOrder} saltado a mano";
                        continue;

                    case StepDecision.HastaElFinal:
                        stepping = false;
                        L("▶▶ sin más pausas hasta el final");
                        break;
                }
            }

            // MOTOR DE CARGA: esperar a que el paso esté LISTO antes de actuar (evita el fallo "no se
            // puede habilitar el elemento" tras navegar). Corto-circuita en cuanto el elemento objetivo
            // está presente y habilitado; el % de carga es solo respaldo. Ver SurfaceReadiness.
            var readySw = System.Diagnostics.Stopwatch.StartNew();
            StepGateResult gate = await SurfaceReadiness.WaitAsync(target, step, L, ct);
            long readyMs = readySw.ElapsedMilliseconds;

            // LA COMPUERTA ES VINCULANTE. "No se avanza en el workflow hasta estar en la superficie
            // adecuada": si nunca se llegó al nodo del paso (y el elemento exacto tampoco resolvió),
            // ejecutarlo actuaría sobre la pantalla equivocada — y encima reportaría ✓. Antes esto era
            // un warning en el log y se ejecutaba igual; ese era EL bug. Última carta antes de parar:
            // ¿la pantalla actual coincide con un paso POSTERIOR de la ruta? Entonces la UI ya pasó
            // este punto (p.ej. una transacción saltó un dynpro) y se salta adelante en vez de romper.
            if (!gate.ShouldExecute)
            {
                var here = target.Identity();
                int jumpTo = JumpForwardIndex(steps, idx, here);
                if (jumpTo > idx)
                {
                    L($"↷ la ubicación actual coincide con el paso {steps[jumpTo].StepOrder}: ya pasaste este punto — salto adelante ({jumpTo - idx - 1} paso(s) intermedios omitidos)");
                    OmitRange(steps, idx, jumpTo, "la pantalla ya está más adelante en la ruta", outcomes);
                    idx = jumpTo - 1; // el for lo lleva al paso de la ubicación actual
                    continue;
                }

                timings.Add(step.StepOrder, step.Label ?? "", step.ActionType ?? "", readyMs, 0);
                // El motivo lo dice el VEREDICTO, no una frase fija. Son dos fallos distintos —«estoy en
                // otra pantalla» y «estoy en la buena pero el control no está listo»— y darles el mismo
                // texto manda la investigación al sitio equivocado (aprendizaje nº2 del CLAUDE.md).
                string why = gate.Outcome == StepGate.ElementNotReady
                    ? $"estamos en la pantalla correcta («{gate.LastSeen}») pero el elemento del paso nunca "
                      + "estuvo presente y habilitado. No se clica un control que aún no está listo."
                    : $"no se llegó a la superficie del paso («{gate.Expected}»); la pantalla actual es "
                      + $"«{gate.LastSeen}». No se ejecuta un paso sobre la pantalla equivocada."
                      + Culprit(steps, idx, gate.LastSeen);
                Report(step, false, why, outcomes);
                return Finish(new RunResult(false, workflowId, outcomes,
                    $"Se detuvo en el paso {step.StepOrder} («{step.Label}»): {outcomes[^1].Error}"));
            }

            bool ok;
            try
            {
                // El resultado y el motivo se capturan en la MISMA llamada. Reintentar para recuperar
                // el error ejecutaría la acción dos veces contra el SAP del cliente.
                var execSw = System.Diagnostics.Stopwatch.StartNew();
                (bool done, string reason) = await Task.Run(() =>
                {
                    bool r = target.Execute(step, out string err);
                    return (r, err);
                }, ct);
                timings.Add(step.StepOrder, step.Label ?? "", step.ActionType ?? "", readyMs, execSw.ElapsedMilliseconds);

                // El detector de fantasmas: ¿cambió la ubicación tras el paso? Un ✓ sin cambio de
                // ubicación (cuando debería navegar) es exactamente el bug que perseguimos.
                string after = target.Identity().Url;
                L($"← paso {step.StepOrder} ejecutado: done={done} · ubicación DESPUÉS='{after}'{(before == after ? " (SIN CAMBIO)" : " (cambió)")}");

                // RESILIENCIA POR UBICACIÓN antes de fallar: pregúntale a la pantalla dónde estamos. Si
                // ya coincide con un nodo POSTERIOR de la ruta, este paso sobraba (ej.: Chrome abrió
                // directo en la página objetivo) — se salta adelante en vez de romper el workflow.
                if (!done)
                {
                    var here = target.Identity();
                    L($"paso {step.StepOrder} no resolvió · ubicación actual='{here.Url}' · nodo grabado del paso='{step.Surface()}'");
                    int jump = JumpForwardIndex(steps, idx, here);
                    if (jump > idx)
                    {
                        L($"↷ la ubicación coincide con el paso {steps[jump].StepOrder}: ya pasaste este punto — salto adelante ({jump - idx - 1} paso(s) intermedios omitidos)");
                        OmitRange(steps, idx, jump, "la pantalla ya está más adelante en la ruta", outcomes);
                        idx = jump - 1; // el for lo lleva al paso de la ubicación actual
                        continue;
                    }
                }

                lastOutcome = done
                    ? $"paso {step.StepOrder} «{step.Label}» ✓ · {(before == after ? "la pantalla NO cambió" : $"la pantalla pasó a {after}")}"
                    : $"paso {step.StepOrder} «{step.Label}» ✗ · {Reason(reason)}";
                ok = Report(step, done, done ? "" : Reason(reason), outcomes);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                ok = Report(step, false, e.Message, outcomes);
            }

            if (!ok)
                return Finish(new RunResult(false, workflowId, outcomes,
                    $"Se detuvo en el paso {step.StepOrder} («{step.Label}»): {outcomes[^1].Error}"));

            await Task.Delay(PauseMs(target), ct);
        }

        return Finish(new RunResult(true, workflowId, outcomes, "") { AlignedConsciously = alignedConsciously });
    }

    /// <summary>
    /// Registra un paso como OMITIDO: queda en el resultado, con su motivo, y no cuenta como hecho.
    ///
    /// La alternativa era la de antes —no registrarlo— y tiene un problema que no se ve hasta que muerde:
    /// el paso desaparece del resultado, el denominador encoge, y «2/2» describe con exactitud una
    /// corrida que no hizo la mitad del trabajo. Un omitido silencioso es indistinguible de un éxito.
    /// </summary>
    private void Omit(PlanStep step, string why, List<StepOutcome> acc)
    {
        var outcome = new StepOutcome(step.StepOrder, step.Label ?? "", step.ActionType, false, why) { Omitted = true };
        acc.Add(outcome);
        try { StepDone?.Invoke(this, outcome); } catch { }
    }

    /// <summary>Omite desde <paramref name="from"/> (incluido) hasta <paramref name="to"/> (excluido).</summary>
    private void OmitRange(IReadOnlyList<PlanStep> steps, int from, int to, string why, List<StepOutcome> acc)
    {
        for (int i = from; i < to && i < steps.Count; i++) Omit(steps[i], why, acc);
        L($"   {to - from} paso(s) marcados como OMITIDOS — no cuentan como ejecutados");
    }

    private bool Report(PlanStep step, bool ok, string error, List<StepOutcome> acc)
    {
        var outcome = new StepOutcome(step.StepOrder, step.Label ?? "", step.ActionType, ok, error);
        acc.Add(outcome);
        L(LogLineFor(step, ok, error));
        try { StepDone?.Invoke(this, outcome); } catch { }
        return ok;
    }

    /// <summary>Log terse por acción (nada de una línea por tecla): "escribió", "clic en X", "abrió/enfocó X".</summary>
    private static string LogLineFor(PlanStep step, bool ok, string error)
    {
        string what = IsAlignmentStep(step) ? $"abrió/enfocó la app"
            : step.ActionType == "input" ? "escribió"
            : step.ActionType == "select" ? $"eligió «{step.Label}»"
            : step.ActionType == "click" ? $"clic en «{step.Label}»"
            : $"«{step.Label}»";
        return ok ? $"✓ {what}" : $"✗ {what} · {error}";
    }

    private static string Reason(string error) =>
        string.IsNullOrWhiteSpace(error) ? "el paso no se pudo ejecutar" : error;

    /// <summary>
    /// Colapsa runs de `input` consecutivos al MISMO selector, dejando solo el último. Como el input
    /// se ejecuta con ValuePattern.SetValue (reemplaza el valor completo), los intermedios son snapshots
    /// del tecleo y no aportan nada. Efecto: escritura instantánea y un solo log por bloque de escritura.
    /// </summary>
    private static List<PlanStep> CollapseInputRuns(List<PlanStep> steps)
    {
        var result = new List<PlanStep>();
        for (int i = 0; i < steps.Count; i++)
        {
            if (steps[i].ActionType == "input")
            {
                int j = i;
                while (j + 1 < steps.Count && steps[j + 1].ActionType == "input"
                       && string.Equals(steps[j + 1].Selector, steps[i].Selector, StringComparison.OrdinalIgnoreCase))
                    j++;
                result.Add(steps[j]); // solo el valor final del bloque de escritura
                i = j;
            }
            else result.Add(steps[i]);
        }
        return result;
    }

    /// <summary>Pausa entre pasos: UIA nativo es síncrono y no necesita respirar (snappy); SAP sí.</summary>
    private int PauseMs(IUiSurface surface) =>
        string.Equals(surface.Name, "uia", StringComparison.OrdinalIgnoreCase)
            ? Math.Min(Math.Clamp(_config.StepDelayMs, 0, 5000), 40)
            : Math.Clamp(_config.StepDelayMs, 0, 5000);

    /// <summary>Un step de alineación de superficie (lo antepone el aprendizaje consciente→subconsciente).</summary>
    private static bool IsAlignmentStep(PlanStep step) =>
        (step.Selector ?? "").StartsWith("app:", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// ¿Dónde reanudar? La ubicación manda, con dos niveles de precisión:
    ///   1. Match COMPLETO (origin + pathname normalizado): si estás parado exactamente en el nodo de
    ///      algún paso, se reanuda en el grupo de pasos MÁS ADELANTE con ese nodo — hacer solo lo que
    ///      falta. (El pathname se normaliza porque los títulos traen contadores vivos: "Inbox (1,956)"
    ///      y "Inbox (1,988)" son el mismo lugar.)
    ///   2. Fallback por origin (misma app): primer paso de esa app, como antes.
    /// 0 si nada coincide o la grabación no trae superficie por paso (workflows viejos).
    /// </summary>
    private static int ResumeIndexFor(List<PlanStep> steps, SurfaceIdentity now)
    {
        // Nivel 1: el ÚLTIMO paso cuyo nodo coincide exacto; de su grupo contiguo, el primero.
        int last = -1;
        for (int i = 0; i < steps.Count; i++)
        {
            if (IsAlignmentStep(steps[i])) continue;
            if (SamePlace(now, steps[i].Surface())) last = i;
        }
        if (last >= 0)
        {
            int start = last;
            while (start > 0 && !IsAlignmentStep(steps[start - 1]) && SamePlace(now, steps[start - 1].Surface()))
                start--;
            return start;
        }

        // Nivel 2: misma app (origin), primer paso de ella.
        for (int i = 0; i < steps.Count; i++)
        {
            if (IsAlignmentStep(steps[i])) continue;
            string recorded = steps[i].Surface();
            if (string.IsNullOrWhiteSpace(recorded)) continue;
            if (SameOrigin(now, recorded)) return i;
        }
        return 0;
    }

    /// <summary>
    /// Tras fallar el paso <paramref name="failedIndex"/>: ¿la pantalla dice que ya estamos MÁS ADELANTE
    /// en la ruta? (ej.: Chrome abrió directo en la página objetivo y este clic ya no hacía falta).
    /// Devuelve el índice del primer paso posterior cuyo nodo coincide EXACTO con la ubicación actual,
    /// o -1 si no hay salto que hacer. Solo match completo: saltar por origin sería adivinar.
    /// </summary>
    private static int JumpForwardIndex(List<PlanStep> steps, int failedIndex, SurfaceIdentity now)
    {
        for (int j = failedIndex + 1; j < steps.Count; j++)
        {
            if (IsAlignmentStep(steps[j])) continue;
            if (SamePlace(now, steps[j].Surface())) return j;
        }
        return -1;
    }

    // Comparación de lugares: TODA la lógica vive en SurfacePlace — la única vara del sistema.
    // (Antes el player, el motor de carga y el pre-check comparaban cada uno a su manera y daban
    // veredictos contradictorios sobre la misma pantalla.)
    private static bool SameOrigin(SurfaceIdentity now, string recordedUrl) =>
        SurfacePlace.SameOrigin(now.Url, recordedUrl);

    /// <summary>¿Mismo LUGAR (origin + pathname normalizado)? Falso si el paso no trae nodo.</summary>
    private static bool SamePlace(SurfaceIdentity now, string recordedUrl) =>
        SurfacePlace.Same(now, recordedUrl);

    /// <summary>Ruta del pantallazo de enseñanza de un paso; "" si el cliente no la puso o falla.</summary>
    private string SafeShot(string workflowId, int stepOrder)
    {
        try { return TaughtShotFor?.Invoke(workflowId, stepOrder) ?? ""; } catch { return ""; }
    }

    /// <summary>
    /// SEÑALAR AL CULPABLE, no al que se tropieza. Cuando un paso no llega a su pantalla y seguimos
    /// exactamente en la del paso ANTERIOR, el que falló no es este: es el anterior, que al enseñarlo
    /// navegaba y ahora no hizo nada — aunque haya reportado ✓.
    ///
    /// Costó tres rondas de diagnóstico: el mensaje culpaba al paso 5 de «no llegar a NV2000» cuando el
    /// problema era que el paso 4 no accionaba la fila. Un error que nombra al paso equivocado manda la
    /// investigación al sitio equivocado (aprendizaje nº2 del CLAUDE.md).
    /// </summary>
    private static string Culprit(IReadOnlyList<PlanStep> steps, int idx, string lastSeen)
    {
        if (idx <= 0) return "";
        PlanStep prev = steps[idx - 1];
        string prevSurface = prev.Surface() ?? "";
        if (prevSurface.Length == 0 || lastSeen.Length == 0) return "";
        if (!SurfacePlace.Same(lastSeen, prevSurface)) return "";

        return $" Seguimos donde vivía el paso {prev.StepOrder} («{prev.Label}», {prev.ActionType}): "
             + "al enseñarlo ESE paso cambiaba de pantalla y ahora no lo ha hecho. Mira ahí, no aquí.";
    }

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
            !SurfacePlace.Covers(plan.SourcePathname, now.Pathname))
            return $"Este workflow se grabó en {plan.SourcePathname} y ahora estás en {now.Pathname}.";

        return null;
    }
}
