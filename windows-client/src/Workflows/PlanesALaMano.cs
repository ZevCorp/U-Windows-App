using System.Collections.Concurrent;
using U.Graph;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Workflows;

/// <summary>
/// EL PLAN, PEDIDO ANTES DE QUE HAGA FALTA. Promesa 111 (spec 007).
/// </summary>
/// <remarks>
/// Darle play empezaba por <c>POST /workflows/:id/plan</c>: 342-484 ms en caliente y 2,6 s la
/// primera vez (arranque frío de Vercel), medido el 2026-09-02. Ese tiempo está en el camino
/// crítico del botón y no hace falta que lo esté: en cuanto el operador ELIGE un workflow en el
/// carrusel, el plan se pide en segundo plano, y el play lo encuentra hecho.
///
/// Reglas: una petición por id, compartida (elegir tres veces no pide tres veces); si la precarga
/// falló, el play vuelve a pedir en vez de heredar el error; y <see cref="Olvida"/> cuando el plan
/// puede haber cambiado (Graph antepuso un paso de alineación tras una corrida).
/// </remarks>
public sealed class PlanesALaMano
{
    private readonly GraphClient _graph;
    private readonly ConcurrentDictionary<string, Task<ExecutionPlan>> _planes = new(StringComparer.Ordinal);

    private static readonly Dictionary<string, string> Intento = new()
    {
        ["source"] = "windows-u",
        ["surface"] = "native",
    };

    public PlanesALaMano(GraphClient graph) => _graph = graph;

    /// <summary>Empieza a pedir el plan de este id si no está ya pedido. No espera.</summary>
    public void Precarga(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        _planes.GetOrAdd(id, Pedir);
    }

    /// <summary>¿Está el plan YA en la mano? Para que el log diga de dónde vino.</summary>
    public bool EstaListo(string id) =>
        _planes.TryGetValue(id ?? "", out var t) && t.IsCompletedSuccessfully;

    /// <summary>El plan de este id: el precargado si lo hay y salió bien; si no, se pide ahora.</summary>
    public Task<ExecutionPlan> Toma(string id, CancellationToken ct)
    {
        if (_planes.TryGetValue(id, out var t) && !t.IsFaulted && !t.IsCanceled) return t.WaitAsync(ct);
        var nuevo = Pedir(id);
        _planes[id] = nuevo;
        return nuevo.WaitAsync(ct);
    }

    /// <summary>Este plan ya no vale (o puede no valer): la próxima vez se pide de nuevo.</summary>
    public void Olvida(string id) => _planes.TryRemove(id ?? "", out _);

    public void OlvidaTodos() => _planes.Clear();

    private Task<ExecutionPlan> Pedir(string id)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var tarea = _graph.GetPlanAsync(id, null, new Dictionary<string, string>(Intento), CancellationToken.None);
        _ = tarea.ContinueWith(t => LogBus.Log("workflow-ui", t.IsCompletedSuccessfully
            ? $"plan de {id} a la mano en {sw.ElapsedMilliseconds} ms ({t.Result.Steps.Count} pasos)"
            : $"la precarga del plan de {id} falló tras {sw.ElapsedMilliseconds} ms: {t.Exception?.GetBaseException().Message}"),
            TaskScheduler.Default);
        return tarea;
    }
}
