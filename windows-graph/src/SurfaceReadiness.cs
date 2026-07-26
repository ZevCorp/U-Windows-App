using U.Graph.Surfaces;

namespace U.Graph;

/// <summary>
/// Motor de detección de CARGA de UI — la "barra de carga" interna del sistema, análoga al sistema de
/// URLs de Windows pero para el ESTADO de carga de una pantalla.
///
/// EL PROBLEMA: tras navegar (p.ej. un Enter en SAP), la interfaz tarda ms/segundos en pintar y
/// habilitar sus elementos. Si actuamos antes, el paso falla ("no se puede habilitar el elemento").
///
/// LA IDEA (como la enseñó el usuario): al GRABAR se toma un snapshot de cuántos elementos interactivos
/// hay listos en esa ruta (la META = 100%). Al EJECUTAR se compara el conteo actual contra la meta para
/// saber el % cargado, y NO se ejecuta el paso hasta superar un umbral (80%). Es resiliente: sigue
/// esperando hasta 100% y da una gracia final por si el elemento objetivo tarda un poco más.
///
/// Diseño mantenible: es un motor AISLADO. Solo depende de <see cref="IUiSurface.ReadinessCount"/>
/// (la métrica) y del conteo-meta guardado por paso (surfaceHints.readiness). No sabe de workflows ni
/// de Graph; el <c>WorkflowPlayer</c> lo invoca antes de cada paso. Documentado en Provider Studio.
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
    ///   1. UBICACIÓN. ¿Estamos en la pantalla donde se grabó este paso? Antes NO se comprobaba: se
    ///      medía "cuánto ha cargado" sin mirar QUÉ había cargado. Como el % es un escalar sin
    ///      identidad, la pantalla ANTERIOR (que ya estaba entera) daba 100% al instante y el paso se
    ///      ejecutaba contra ella. Ese es el "dice que cargó el 100% y es completamente mentira": no
    ///      mentía sobre el porcentaje, mentía sobre la PANTALLA.
    ///   2. ELEMENTO. El objetivo ya resuelve y se puede tocar (<see cref="IUiSurface.IsStepReady"/>).
    ///   3. RESPALDO. El % de carga global, solo para pasos sin elemento resoluble.
    ///
    /// Resiliente en las tres: si algo no se confirma dentro de su techo, se sigue igual pero se DEJA
    /// DICHO en el log. Un workflow que se planta en silencio es peor que uno que avisa y sigue.
    /// </summary>
    public static async Task WaitAsync(IUiSurface surface, PlanStep step, Action<string>? log, CancellationToken ct)
    {
        await WaitForLocationAsync(surface, step, log, ct);

        int target = step.ReadinessCount();
        int polls = Math.Max(1, MaxWaitMs / PollMs);
        double lastLogged = -1;

        for (int i = 0; i < polls && !ct.IsCancellationRequested; i++)
        {
            // PRIMARIA: ¿ya está el elemento objetivo? Es la razón real de esperar; en cuanto sí, ejecutar.
            if (SafeReady(surface, step))
            {
                if (i > 0) log?.Invoke($"  elemento objetivo listo tras {i * PollMs} ms → ejecutar");
                return;
            }

            // RESPALDO: el % de carga (útil para pasos sin elemento a resolver). Log solo cuando cambia.
            if (target > 0)
            {
                int cur = SafeCount(surface);
                double pct = Math.Min(1.0, (double)cur / target);
                if (Math.Abs(pct - lastLogged) >= 0.05) { log?.Invoke($"  carga UI {Bar(pct)} {pct:P0} ({cur}/{target})"); lastLogged = pct; }
                if (pct >= Threshold) return;
            }

            await Task.Delay(PollMs, ct);
        }

        log?.Invoke($"  ⚠ no se confirmó que el elemento estuviera listo en {MaxWaitMs} ms — se intenta igual (resiliente)");
    }

    /// <summary>
    /// Fase 1: esperar a estar en la pantalla del paso. Se compara contra el nodo GRABADO
    /// (<c>surfaceHints.observedSurface</c>); los pasos sin nodo grabado (workflows viejos) se saltan
    /// esta fase para no romperlos.
    ///
    /// El log dice en cada cambio dónde estamos vs. dónde deberíamos estar: es exactamente lo que hay
    /// que contrastar con el pantallazo cuando algo se ejecuta en la pantalla equivocada.
    /// </summary>
    private static async Task WaitForLocationAsync(IUiSurface surface, PlanStep step, Action<string>? log, CancellationToken ct)
    {
        string expected = step.Surface();
        if (string.IsNullOrWhiteSpace(expected)) return;   // grabación vieja: sin nodo que exigir

        int polls = Math.Max(1, MaxLocationWaitMs / PollMs);
        string? lastSeen = null;

        for (int i = 0; i < polls && !ct.IsCancellationRequested; i++)
        {
            string now = SafeUrl(surface);
            if (SamePlace(now, expected))
            {
                if (i > 0) log?.Invoke($"  ubicación alcanzada tras {i * PollMs} ms → '{now}'");
                return;
            }

            if (now != lastSeen)
            {
                log?.Invoke($"  esperando ubicación · aquí='{now}' · esperada='{expected}'");
                lastSeen = now;
            }
            await Task.Delay(PollMs, ct);
        }

        log?.Invoke($"  ⚠ NUNCA se llegó a '{expected}' en {MaxLocationWaitMs} ms (seguimos en '{SafeUrl(surface)}') — " +
                    "se intenta igual, pero este paso puede estar actuando sobre la pantalla equivocada");
    }

    /// <summary>
    /// ¿La ubicación actual es la esperada? Tolerante por PREFIJO de ruta a propósito: la identidad SAP
    /// pasó de <c>/NV2000</c> a <c>/NV2000/SAPMNPA10/0100</c> (transacción + programa + dynpro), y sin
    /// esto toda grabación anterior al cambio dejaría de coincidir. Lo más específico sigue mandando:
    /// si la grabación trae el dynpro, se exige el dynpro.
    /// </summary>
    internal static bool SamePlace(string now, string expected)
    {
        if (string.IsNullOrWhiteSpace(now) || string.IsNullOrWhiteSpace(expected)) return false;
        if (string.Equals(now, expected, StringComparison.OrdinalIgnoreCase)) return true;

        // Grabación menos específica que la lectura actual: /NV2000 debe casar con /NV2000/SAPMNPA10/0100,
        // pero NO con /NV20001 — de ahí el separador explícito.
        return now.StartsWith(expected.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);
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
