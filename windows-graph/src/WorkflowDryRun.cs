using U.Graph.Surfaces;

namespace U.Graph;

/// <summary>Gravedad de un hallazgo del ensayo. Ordena el informe: lo que rompe primero.</summary>
public enum DryRunLevel
{
    /// <summary>Contexto útil, no hay nada que arreglar.</summary>
    Info,

    /// <summary>Puede funcionar, pero es donde el flujo suele romperse. Merece una mirada humana.</summary>
    Aviso,

    /// <summary>Esto NO va a ejecutarse. Con uno solo, el workflow se detiene ahí.</summary>
    Bloqueante,
}

public sealed record DryRunFinding(DryRunLevel Level, int StepOrder, string Label, string Message);

public sealed record DryRunReport(string WorkflowId, int Steps, IReadOnlyList<DryRunFinding> Findings)
{
    public int Count(DryRunLevel l) => Findings.Count(f => f.Level == l);
    public bool Clean => Count(DryRunLevel.Bloqueante) == 0;
}

/// <summary>
/// ENSAYO EN SECO: recorre el plan y dice qué pasaría, SIN tocar la pantalla.
///
/// Por qué existe: en un flujo de 40 pasos, descubrir que el paso 31 no resuelve cuesta un minuto de
/// ejecución real sobre un SAP clínico — y si falla delante de un cliente, ya falló. Esto da el mismo
/// veredicto en segundos y sin efectos.
///
/// LO QUE ESTE ENSAYO NO PUEDE HACER, dicho por delante para que nadie confíe de más: no navega. Solo
/// puede comprobar EN VIVO los pasos cuya pantalla grabada es la que está delante ahora mismo; para el
/// resto se limita a lo que se deduce del plan. Un ensayo limpio no garantiza una ejecución limpia —
/// garantiza que los fallos que SÍ son detectables sin ejecutar no están.
///
/// El hallazgo más valioso no es "esto falla", es el CAMBIO DE PANTALLA: cada vez que dos pasos
/// consecutivos viven en superficies distintas, algo tuvo que provocar esa transición. Si ese algo no
/// está grabado —un botón que SAP no deja ver— el workflow se detendrá ahí siempre. Es exactamente el
/// fallo que costó el día del 2026-07-26: «Crear Triage Administrativo» nunca se grabó, y el plan
/// saltaba de NWP1 a NV2000 sin nada en medio que lo explicara.
/// </summary>
public static class WorkflowDryRun
{
    public static DryRunReport Analyze(
        string workflowId,
        IReadOnlyList<PlanStep> steps,
        IReadOnlyList<IUiSurface> surfaces,
        Action<string>? log = null)
    {
        var found = new List<DryRunFinding>();
        void Add(DryRunLevel lvl, PlanStep s, string msg) =>
            found.Add(new DryRunFinding(lvl, s.StepOrder, s.Label ?? "", msg));

        if (steps.Count == 0)
            return new DryRunReport(workflowId, 0,
                new[] { new DryRunFinding(DryRunLevel.Bloqueante, 0, "", "el plan no trae ningún paso") });

        IUiSurface? SurfaceFor(string sel) =>
            SapSelector.Owns(sel) ? surfaces.FirstOrDefault(s => s.Name == "sap")
            : UiaSelector.Owns(sel) ? surfaces.FirstOrDefault(s => s.Name == "uia")
            : null;

        // Dónde estamos AHORA, para poder comprobar en vivo lo que caiga en esta pantalla.
        var here = new Dictionary<string, string>();
        foreach (var s in surfaces)
        {
            try { here[s.Name] = s.Identity().Url; } catch { }
        }

        string prevSurface = "";
        PlanStep? prev = null;

        foreach (PlanStep step in steps)
        {
            string sel = step.Selector ?? "";
            string recorded = step.Surface();
            bool synthetic = sel.StartsWith("key:", StringComparison.OrdinalIgnoreCase)
                          || sel.StartsWith("scroll:", StringComparison.OrdinalIgnoreCase);

            // ── 1. ¿Sabe alguien ejecutar esto? ──────────────────────────────────────────
            IUiSurface? target = SurfaceFor(sel);
            if (target == null && !synthetic && !sel.StartsWith("app:", StringComparison.OrdinalIgnoreCase))
            {
                Add(DryRunLevel.Bloqueante, step, $"ningún motor sabe ejecutar «{sel}»");
                prev = step; prevSurface = recorded;
                continue;
            }

            // ── 2. Sin pantalla grabada = sin compuerta ──────────────────────────────────
            if (string.IsNullOrWhiteSpace(recorded))
            {
                Add(DryRunLevel.Aviso, step,
                    "sin pantalla grabada: la compuerta no puede protegerlo y se ejecutará contra lo que haya delante");
            }

            // ── 3. CAMBIO DE PANTALLA: el punto donde se rompen los flujos largos ────────
            if (recorded.Length > 0 && prevSurface.Length > 0 && !SurfacePlace.Same(recorded, prevSurface))
            {
                string cause = prev == null ? "(nada)" : $"paso {prev.StepOrder} «{prev.Label}» ({prev.ActionType})";
                bool causePlausible = prev != null
                    && (prev.ActionType is "click" or "key"
                        || SapSelector.ToolbarButtonOf(prev.Selector ?? "") != null);

                Add(causePlausible ? DryRunLevel.Info : DryRunLevel.Bloqueante, step,
                    $"la pantalla cambia aquí ({prevSurface} → {recorded}) y quien debería provocarlo es {cause}. "
                    + (causePlausible
                        ? "Confirma que ese paso es realmente lo que navega."
                        : "Un input no navega: falta el paso que lo hace — típicamente un botón que SAP no "
                          + "deja grabar. Sin él, la ejecución se detiene justo aquí."));
            }

            // ── 4. Ruido de la grabación ─────────────────────────────────────────────────
            if (step.ActionType == "input" && string.IsNullOrEmpty(step.Value))
                Add(DryRunLevel.Aviso, step, "input con valor vacío: escribirá «» y puede borrar un campo precargado");

            if (prev != null && prev.Selector == sel && prev.ActionType == step.ActionType && !synthetic)
                Add(DryRunLevel.Aviso, step, $"idéntico al paso {prev.StepOrder}: probable duplicado de la grabación");

            if (SapSelector.NodeKeyOf(sel) != null && string.IsNullOrWhiteSpace(step.NodePath))
                Add(DryRunLevel.Aviso, step,
                    "fila de árbol sin ruta jerárquica: si la clave cambia entre sesiones no habrá con qué reencontrarla");

            // ── 5. EN VIVO, solo si este paso vive en la pantalla que tenemos delante ────
            if (target != null && recorded.Length > 0
                && here.TryGetValue(target.Name, out string? now) && SurfacePlace.Same(now, recorded))
            {
                bool ready;
                try { ready = target.IsStepReady(step); } catch { ready = false; }
                Add(ready ? DryRunLevel.Info : DryRunLevel.Bloqueante, step,
                    ready
                        ? "comprobado EN VIVO: el elemento resuelve y está habilitado"
                        : "comprobado EN VIVO en su propia pantalla: el elemento NO resuelve o está deshabilitado");
            }

            prev = step;
            if (recorded.Length > 0) prevSurface = recorded;
        }

        var report = new DryRunReport(workflowId, steps.Count, found);
        if (log != null) foreach (string line in Format(report)) log(line);
        return report;
    }

    /// <summary>El informe en texto, lo bloqueante primero. Es lo que se lee cinco minutos antes del demo.</summary>
    public static IEnumerable<string> Format(DryRunReport r)
    {
        yield return $"🧪 ENSAYO EN SECO · {r.WorkflowId} · {r.Steps} pasos · "
                   + $"{r.Count(DryRunLevel.Bloqueante)} bloqueante(s), {r.Count(DryRunLevel.Aviso)} aviso(s)";

        foreach (DryRunLevel lvl in new[] { DryRunLevel.Bloqueante, DryRunLevel.Aviso, DryRunLevel.Info })
        {
            foreach (var f in r.Findings.Where(f => f.Level == lvl))
            {
                string mark = lvl switch { DryRunLevel.Bloqueante => "✋", DryRunLevel.Aviso => "⚠", _ => "·" };
                yield return $"   {mark} paso {f.StepOrder} «{Short(f.Label)}»: {f.Message}";
            }
        }

        yield return r.Clean
            ? "   sin bloqueantes. Ojo: el ensayo no navega, así que solo se comprobó en vivo lo que cae en la pantalla actual."
            : "   NO ejecutar tal cual: los bloqueantes detienen el workflow donde están.";
    }

    private static string Short(string s) => s.Length <= 34 ? s : s[..34] + "…";
}
