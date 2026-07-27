using U.Graph.Surfaces;

namespace U.Graph;

/// <summary>Veredicto de la compuerta de un paso. Ver <see cref="SurfaceReadiness.WaitAsync"/>.</summary>
public enum StepGate
{
    /// <summary>Confirmado: en la superficie del paso y con el elemento (o el % de carga) listo.</summary>
    Ready,

    /// <summary>Llegamos a la superficie del paso y el paso NO apunta a un elemento concreto (tecla,
    /// scroll): no hay nada que confirmar, se ejecuta. Estando en la pantalla correcta es barato y honesto.</summary>
    ArrivedUnconfirmed,

    /// <summary>
    /// Estamos en la pantalla correcta, pero el ELEMENTO del paso nunca llegó a estar presente y
    /// habilitado dentro del techo. NO se ejecuta.
    ///
    /// Antes esto caía en <see cref="ArrivedUnconfirmed"/> y se clicaba igual. Es la diferencia entre
    /// «el botón todavía no está» y «el botón está listo», y clicar en el primer caso es exactamente el
    /// síntoma que reportó el operador: el clic entra en un sitio que aún no existe, y el paso siguiente
    /// arranca desde un estado que nadie previó. Detenerse es recuperable — el puente consciente retoma;
    /// clicar a ciegas no lo es.
    /// </summary>
    ElementNotReady,

    /// <summary>NUNCA se llegó a la superficie del paso y el elemento exacto tampoco apareció.
    /// El paso NO debe ejecutarse: actuaría sobre la pantalla equivocada.</summary>
    NotAtLocation,
}

/// <summary>
/// Resultado de la espera. <see cref="ShouldExecute"/> es lo único que el player necesita consultar;
/// <see cref="Expected"/> y <see cref="LastSeen"/> alimentan el mensaje de error honesto.
/// </summary>
public sealed record StepGateResult(StepGate Outcome, string Expected, string LastSeen)
{
    public bool ShouldExecute =>
        Outcome is not (StepGate.NotAtLocation or StepGate.ElementNotReady);
}

/// <summary>
/// Motor de detección de CARGA de UI — la "barra de carga" interna del sistema, análoga al sistema de
/// URLs de Windows pero para el ESTADO de carga de una pantalla.
///
/// EL PROBLEMA: tras navegar (p.ej. un Enter en SAP), la interfaz tarda ms/segundos en pintar y
/// habilitar sus elementos. Si actuamos antes, el paso falla ("no se puede habilitar el elemento") —
/// o peor: se ejecuta contra la pantalla ANTERIOR y reporta éxito.
///
/// LA REGLA (como la definió el usuario): si el elemento EXACTO del paso ya resuelve, se ejecuta YA.
/// Si no, NO SE AVANZA hasta estar en la superficie grabada del paso; y una vez ahí, se espera el
/// % de carga (≥80%) como respaldo. Si nunca se llega a la superficie y el elemento tampoco aparece,
/// el veredicto es NO EJECUTAR — un paso sobre la pantalla equivocada es el único resultado peor que
/// un workflow detenido, porque además REPORTA éxito.
///
/// Diseño mantenible: es un motor AISLADO. Solo depende de <see cref="IUiSurface.ReadinessCount"/>
/// (la métrica), de <see cref="IUiSurface.Identity"/> (la ubicación) y del nodo/meta guardados por
/// paso (surfaceHints.observedSurface / .readiness). No sabe de workflows ni de Graph; el
/// <c>WorkflowPlayer</c> lo invoca antes de cada paso y RESPETA su veredicto.
/// </summary>
public static class SurfaceReadiness
{
    /// <summary>Respaldo: si no podemos confirmar el elemento, se ejecuta al superar este % de carga.</summary>
    public const double Threshold = 0.80;

    private const int PollMs = 120;
    private const int MaxWaitMs = 4000;                  // techo duro: no colgar un workflow por esperar

    /// <summary>
    /// Techo para la fase de UBICACIÓN. Es aparte y más generoso que <see cref="MaxWaitMs"/> porque
    /// esperar a que llegue la pantalla correcta es una transición de red (round-trip a SAP), no un
    /// repintado local.
    /// </summary>
    private const int MaxLocationWaitMs = 8000;

    /// <summary>
    /// Espera a que el paso esté LISTO. En tres fases, en este orden — el orden ES el arreglo:
    ///
    ///   1. UBICACIÓN (vinculante). ¿Estamos en la pantalla donde se grabó este paso? Antes esto era
    ///      un warning decorativo: se avisaba "NUNCA se llegó" y se ejecutaba igual. Ahora el
    ///      veredicto viaja al player y el paso NO se ejecuta sobre la pantalla equivocada. Única
    ///      excepción, la regla del operador: si el elemento EXACTO del paso ya resuelve, se ejecuta
    ///      aunque la URL no case (la URL puede estar mal grabada; el elemento presente es evidencia
    ///      más fuerte que un string).
    ///   2. ELEMENTO. El objetivo ya resuelve y se puede tocar (<see cref="IUiSurface.IsStepReady"/>).
    ///   3. RESPALDO. El % de carga global — que ahora se mide ESTANDO en la pantalla correcta, no
    ///      contra la que hubiera delante (el % es un escalar sin identidad: la pantalla anterior,
    ///      entera, daba 100% al instante).
    ///
    /// Pasos sin nodo grabado (workflows viejos): sin fase 1, comportamiento de siempre.
    /// </summary>
    public static async Task<StepGateResult> WaitAsync(IUiSurface surface, PlanStep step, Action<string>? log, CancellationToken ct)
    {
        string expected = step.Surface();
        string lastSeen = "";

        // ── FASE 1: UBICACIÓN (vinculante) ──────────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(expected))
        {
            int locPolls = Math.Max(1, MaxLocationWaitMs / PollMs);
            bool arrived = false;
            string? lastLoggedUrl = null;

            for (int i = 0; i < locPolls && !ct.IsCancellationRequested; i++)
            {
                string now = SafeUrl(surface);
                lastSeen = now;

                if (SurfacePlace.Same(now, expected))
                {
                    if (i > 0) log?.Invoke($"  ubicación alcanzada tras {i * PollMs} ms → '{now}'");
                    arrived = true;
                    break;
                }

                // La regla del operador: elemento exacto presente ⇒ ejecutar, aunque la URL no case.
                if (SafeReady(surface, step))
                {
                    log?.Invoke($"  el elemento exacto ya resuelve (aunque la ubicación es '{now}', no '{expected}') → ejecutar");
                    return new StepGateResult(StepGate.Ready, expected, now);
                }

                if (now != lastLoggedUrl)
                {
                    log?.Invoke($"  esperando ubicación · aquí='{now}' · esperada='{expected}'");
                    lastLoggedUrl = now;
                }
                await Task.Delay(PollMs, ct);
            }

            if (!arrived)
            {
                log?.Invoke($"  ✋ no se llegó a '{expected}' en {MaxLocationWaitMs} ms (seguimos en '{lastSeen}') y el elemento tampoco resolvió — el paso NO se ejecuta sobre la pantalla equivocada");
                return new StepGateResult(StepGate.NotAtLocation, expected, lastSeen);
            }
        }

        // ── FASES 2 y 3: ELEMENTO y % DE CARGA (ya en la pantalla correcta, o sin nodo grabado) ──
        int target = step.ReadinessCount();
        int polls = Math.Max(1, MaxWaitMs / PollMs);
        double lastLogged = -1;

        for (int i = 0; i < polls && !ct.IsCancellationRequested; i++)
        {
            // PRIMARIA: ¿ya está el elemento objetivo? Es la razón real de esperar; en cuanto sí, ejecutar.
            if (SafeReady(surface, step))
            {
                if (i > 0) log?.Invoke($"  elemento objetivo listo tras {i * PollMs} ms → ejecutar");
                return new StepGateResult(StepGate.Ready, expected, SafeUrl(surface));
            }

            // RESPALDO: el % de carga (útil para pasos sin elemento a resolver). Log solo cuando cambia.
            if (target > 0)
            {
                int cur = SafeCount(surface);
                double pct = Math.Min(1.0, (double)cur / target);
                if (Math.Abs(pct - lastLogged) >= 0.05) { log?.Invoke($"  carga UI {Bar(pct)} {pct:P0} ({cur}/{target})"); lastLogged = pct; }
                if (pct >= Threshold) return new StepGateResult(StepGate.Ready, expected, SafeUrl(surface));
            }

            await Task.Delay(PollMs, ct);
        }

        // Aquí se acabó el techo sin confirmar. La respuesta depende de si hay algo que confirmar.
        if (TargetsAnElement(step))
        {
            log?.Invoke($"  ✋ el elemento «{step.Label}» no llegó a estar presente y habilitado en {MaxWaitMs} ms "
                + "— el paso NO se ejecuta: clicar un control que aún no está listo deja la pantalla en un "
                + "estado que el resto del workflow no espera");
            return new StepGateResult(StepGate.ElementNotReady, expected, SafeUrl(surface));
        }

        log?.Invoke($"  ⚠ sin elemento que confirmar (paso de {step.ActionType}) — se ejecuta: estamos en la pantalla correcta");
        return new StepGateResult(StepGate.ArrivedUnconfirmed, expected, SafeUrl(surface));
    }

    /// <summary>
    /// ¿El paso apunta a un control concreto que se pueda comprobar? Las teclas y el scroll van al FOCO,
    /// no a un elemento resuelto: no hay nada que esperar y bloquearlos colgaría workflows legítimos.
    /// Se decide por el prefijo del selector, que es lo que distingue los sintéticos de los reales.
    /// </summary>
    private static bool TargetsAnElement(PlanStep step)
    {
        string sel = step.Selector ?? "";
        return sel.Length > 0
            && !sel.StartsWith("key:", StringComparison.OrdinalIgnoreCase)
            && !sel.StartsWith("scroll:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SafeReady(IUiSurface s, PlanStep step) { try { return s.IsStepReady(step); } catch { return false; } }
    private static int SafeCount(IUiSurface s) { try { return s.ReadinessCount(); } catch { return 0; } }
    private static string SafeUrl(IUiSurface s) { try { return s.Identity().Url; } catch { return ""; } }

    private static string Bar(double pct)
    {
        int n = Math.Clamp((int)Math.Round(pct * 10), 0, 10);
        return "[" + new string('█', n) + new string('░', 10 - n) + "]";
    }
}
