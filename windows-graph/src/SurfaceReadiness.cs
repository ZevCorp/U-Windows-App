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
    /// Espera a que el paso esté LISTO para ejecutarse. Señal primaria: el elemento objetivo ya presente
    /// y habilitado (<see cref="IUiSurface.IsStepReady"/>) → ejecuta YA. Señal de respaldo (para pasos sin
    /// elemento resoluble): el % de carga global ≥ umbral. Si nada se confirma en el techo, se intenta
    /// igual (resiliente). Rápido en el caso común: en cuanto aparece lo que se busca, procede.
    /// </summary>
    public static async Task WaitAsync(IUiSurface surface, PlanStep step, Action<string>? log, CancellationToken ct)
    {
        int target = step.ReadinessCount();
        int polls = Math.Max(1, MaxWaitMs / PollMs);
        double lastLogged = -1;

        for (int i = 0; i < polls && !ct.IsCancellationRequested; i++)
        {
            // PRIMARIA: ¿ya está el elemento objetivo? Es la razón real de esperar; en cuanto sí, ejecutar.
            if (SafeReady(surface, step))
            {
                if (i > 0) log?.Invoke("  elemento objetivo listo → ejecutar");
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

        log?.Invoke($"  no se confirmó carga en {MaxWaitMs} ms — se intenta igual (resiliente)");
    }

    private static bool SafeReady(IUiSurface s, PlanStep step) { try { return s.IsStepReady(step); } catch { return false; } }
    private static int SafeCount(IUiSurface s) { try { return s.ReadinessCount(); } catch { return 0; } }

    private static string Bar(double pct)
    {
        int n = Math.Clamp((int)Math.Round(pct * 10), 0, 10);
        return "[" + new string('█', n) + new string('░', 10 - n) + "]";
    }
}
