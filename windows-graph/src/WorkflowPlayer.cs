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

        // LA UBICACIÓN COMO EJE: ¿ya estás parado en la superficie de algún paso del workflow? Si sí,
        // reanuda AHÍ y salta lo previo — "ubícate en cualquier nodo y haz solo lo que falta para el
        // objetivo", no siempre desde el primer paso. Solo aplica con grabaciones que traen la superficie
        // por paso (surfaceHints.observedSurface); las viejas caen al comportamiento de siempre.
        int resumeFrom = ResumeIndexFor(steps, surface.Identity());
        if (resumeFrom > 0)
        {
            L($"reanudando en el paso {steps[resumeFrom].StepOrder} — ya estás en su superficie ('{surface.Identity().Origin}'); se saltan {resumeFrom} paso(s) previos");
            steps = steps.Skip(resumeFrom).ToList();
            hasAlignmentStep = steps.Any(IsAlignmentStep); // recomputar tras el corte
        }

        // Si el workflow YA aprendió a alinearse (tiene un step `app:`), no se hace el pre-check: ese
        // step, al ejecutarse primero, lleva el foco a la superficie. Solo se aprende (pre-check +
        // prepend) cuando aún NO tiene el step de alineación. Si reanudamos a mitad, tampoco hay pre-check:
        // ya estamos parados en un nodo válido del workflow.
        L($"superficie actual: origin='{surface.Identity().Origin}' pathname='{surface.Identity().Pathname}' · hasAlignmentStep={hasAlignmentStep} · strictSurface={strictSurface} · resumeFrom={resumeFrom}");
        bool alignedConsciously = false;
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
                return new RunResult(false, workflowId, Array.Empty<StepOutcome>(), mismatch);
        }

        var outcomes = new List<StepOutcome>();
        for (int idx = 0; idx < steps.Count; idx++)
        {
            PlanStep step = steps[idx];
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
                await Task.Delay(40, ct);
                continue;
            }

            // Un workflow puede cruzar superficies (empieza en UIA, sigue en SAP): se reelige por paso.
            IUiSurface? target = SurfaceFor(step.Selector) ?? surface;

            string before = target.Identity().Url;
            L($"→ paso {step.StepOrder} «{step.Label}» ({step.ActionType}) · ubicación ANTES='{before}' · nodo grabado='{(string.IsNullOrWhiteSpace(step.Surface()) ? "(sin nodo)" : step.Surface())}'");

            // MOTOR DE CARGA: esperar a que el paso esté LISTO antes de actuar (evita el fallo "no se
            // puede habilitar el elemento" tras navegar). Corto-circuita en cuanto el elemento objetivo
            // está presente y habilitado; el % de carga es solo respaldo. Ver SurfaceReadiness.
            await SurfaceReadiness.WaitAsync(target, step, L, ct);

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
                        Report(step, true, "", outcomes); // el objetivo del paso ya está logrado por la ruta
                        idx = jump - 1; // el for lo lleva al paso de la ubicación actual
                        continue;
                    }
                }

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

            await Task.Delay(PauseMs(target), ct);
        }

        return new RunResult(true, workflowId, outcomes, "") { AlignedConsciously = alignedConsciously };
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

    /// <summary>El origin de una URL de superficie: <c>uia://chrome.exe/x</c> → <c>uia://chrome.exe</c>.</summary>
    private static string OriginOf(string url)
    {
        string s = (url ?? "").Trim();
        int scheme = s.IndexOf("://", StringComparison.Ordinal);
        if (scheme < 0) return s;
        int slash = s.IndexOf('/', scheme + 3);
        return slash < 0 ? s : s[..slash];
    }

    /// <summary>El pathname de una URL de superficie: <c>uia://chrome.exe/x</c> → <c>/x</c> ("" si no hay).</summary>
    private static string PathnameOf(string url)
    {
        string s = (url ?? "").Trim();
        int scheme = s.IndexOf("://", StringComparison.Ordinal);
        if (scheme < 0) return "";
        int slash = s.IndexOf('/', scheme + 3);
        return slash < 0 ? "" : s[slash..];
    }

    private static bool SameOrigin(SurfaceIdentity now, string recordedUrl) =>
        !string.IsNullOrWhiteSpace(now.Origin) &&
        string.Equals(now.Origin.TrimEnd('/'), OriginOf(recordedUrl).TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>¿Mismo LUGAR exacto (origin + pathname normalizado)? Falso si el paso no trae nodo.</summary>
    private static bool SamePlace(SurfaceIdentity now, string recordedUrl)
    {
        if (string.IsNullOrWhiteSpace(recordedUrl) || !SameOrigin(now, recordedUrl)) return false;
        string a = NormalizePlace(now.Pathname);
        string b = NormalizePlace(PathnameOf(recordedUrl));
        return a.Length > 0 && a == b;
    }

    /// <summary>
    /// Normaliza un pathname para comparar LUGARES, no instantes: minúsculas y solo letras (los dígitos
    /// y signos se van porque los títulos traen contadores/estado vivo — "Inbox (1,956)" ≠ texto pero
    /// = lugar que "Inbox (1,988)").
    /// </summary>
    private static string NormalizePlace(string pathname)
    {
        var sb = new System.Text.StringBuilder((pathname ?? "").Length);
        foreach (char c in (pathname ?? "").ToLowerInvariant())
            if (char.IsLetter(c)) sb.Append(c);
        return sb.ToString();
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
            !string.Equals(now.Pathname, plan.SourcePathname, StringComparison.OrdinalIgnoreCase))
            return $"Este workflow se grabó en {plan.SourcePathname} y ahora estás en {now.Pathname}.";

        return null;
    }
}
