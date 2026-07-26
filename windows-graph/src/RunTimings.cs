using System.Linq;
using System.Text;

namespace U.Graph;

/// <summary>
/// Sistema de MEDICIÓN de tiempos de respuesta de un workflow, milimétrico. No decide nada: solo
/// cronometra cada fase para ver dónde se va el tiempo — espera de carga (SurfaceReadiness), acción
/// (IUiSurface.Execute) y total, por paso, más la alineación inicial y el total del run.
///
/// Lo alimenta el <c>WorkflowPlayer</c> con dos <see cref="System.Diagnostics.Stopwatch"/> por paso
/// (uno para la espera, otro para la acción) y emite un RESUMEN al terminar (éxito o fallo) por el mismo
/// log <c>workflow</c>. Aislado a propósito: apagarlo/cambiarlo no toca la ejecución.
/// </summary>
public sealed class RunTimings
{
    /// <summary>Tiempo de la alineación consciente inicial (llegar a la superficie del workflow).</summary>
    public long AlignMs { get; set; }

    private readonly List<(int Order, string Label, string Kind, long ReadyMs, long ActionMs)> _steps = new();

    /// <summary>Registra un paso: su espera de carga y su tiempo de acción, en ms.</summary>
    public void Add(int order, string label, string kind, long readyMs, long actionMs) =>
        _steps.Add((order, label ?? "", kind ?? "", readyMs, actionMs));

    /// <summary>Resumen legible para el log: total, alineación, desglose por paso, sumas y el más lento.</summary>
    public string Summary(long totalMs)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"⏱ TIEMPOS · total={totalMs} ms · alineación={AlignMs} ms · {_steps.Count} paso(s) medido(s)");

        long sumReady = 0, sumAction = 0;
        foreach (var s in _steps)
        {
            long t = s.ReadyMs + s.ActionMs;
            sb.AppendLine($"   paso {s.Order} [{s.Kind}] «{Trunc(s.Label)}»: espera-carga={s.ReadyMs} ms · acción={s.ActionMs} ms · total={t} ms");
            sumReady += s.ReadyMs;
            sumAction += s.ActionMs;
        }

        if (_steps.Count > 0)
        {
            var slowest = _steps.OrderByDescending(s => s.ReadyMs + s.ActionMs).First();
            sb.AppendLine($"   sumas: espera-carga={sumReady} ms · acciones={sumAction} ms · más lento = paso {slowest.Order} ({slowest.ReadyMs + slowest.ActionMs} ms)");
        }
        return sb.ToString().TrimEnd();
    }

    private static string Trunc(string s) => s.Length > 30 ? s[..30] + "…" : s;
}
