using U.Graph;
using U.Graph.Surfaces;
using U.WindowsClient.Agent;
using U.WindowsClient.Diagnostics;
using U.WindowsClient.Telemetry;
using U.WindowsClient.Uia;

namespace U.WindowsClient.Mcp;

/// <summary>
/// Ejecuta las llamadas MCP <c>workflow_*</c> que decide el cerebro: el subconsciente invocado desde
/// el consciente. El cerebro declara el workflow como herramienta (scopeada por superficie) e inyecta
/// el <c>workflow_id</c> en los args; aquí solo se pide el plan a Graph y se ejecuta con el
/// <see cref="WorkflowPlayer"/> — el MISMO reparto de la biblioteca manual: Graph decide QUÉ,
/// esta máquina decide CÓMO.
/// </summary>
public sealed class WorkflowMcpRunner
{
    private readonly GraphConfig _graphConfig;
    private readonly UiaSurface _uia = new();
    private readonly SapGuiSurface _sap = new();
    private readonly IVoice _voice;

    public WorkflowMcpRunner(GraphConfig graphConfig, IVoice voice)
    {
        _graphConfig = graphConfig;
        _voice = voice;
        // Esta es la vía por la que el CEREBRO ejecuta workflows, y es la que corrió en el incidente del
        // 2026-07-26 («MCP invoca workflow_id=…»). Su superficie SAP tiene que contar lo mismo que la de
        // la biblioteca manual, o el mismo fallo es diagnosticable por un camino y mudo por el otro.
        _sap.Diagnostic += (_, msg) => LogBus.Log("sap", msg);
    }

    public async Task<string> RunAsync(string workflowId, string context, CancellationToken ct)
    {
        LogBus.Log("workflow", $"MCP invoca workflow_id='{workflowId}' context='{context}'");
        // Telemetría "Windows Live": corrida de workflow (subconsciente). runId correlaciona sus pasos.
        string runId = TelemetryBus.NewRunId();
        TelemetryBus.Emit("workflow_start", workflowId: workflowId, runId: runId, label: context);
        if (string.IsNullOrWhiteSpace(workflowId))
            return "la llamada al workflow no trajo workflow_id";
        if (!_graphConfig.IsConfigured)
            return "workflows no disponibles: falta la API key de Graph (graph.json)";

        var graph = new GraphClient(_graphConfig);
        var player = new WorkflowPlayer(graph, _graphConfig, _uia, _sap)
        {
            // Alineación consciente: si no estamos en la superficie del workflow, abrir/enfocar la app.
            Aligner = AppAligner.EnsureAsync,
            Log = s => LogBus.Log("workflow", s)
        };
        player.StepDone += (_, outcome) =>
        {
            _voice.Narrate(outcome.Ok ? $"✓ {outcome.Label}" : $"✗ {outcome.Label}: {outcome.Error}");
            TelemetryBus.Emit("workflow_step", workflowId: workflowId, runId: runId,
                phase: outcome.Ok ? "ok" : "error", label: outcome.Label);
        };

        var variables = string.IsNullOrWhiteSpace(context)
            ? null
            : new Dictionary<string, string> { ["context"] = context };

        // strictSurface: el cerebro eligió este workflow porque ESTA superficie coincide; si al
        // ejecutarse ya no coincide (el usuario navegó), el Aligner se alinea antes de tocar nada.
        RunResult result = await player.RunAsync(workflowId, variables, strictSurface: true, ct);
        // El total va sobre el PLAN (result.Total), no sobre los veredictos registrados: eso último es
        // lo que dejaba pasar «2/2» en una corrida que no ejecutó la mitad. Y los omitidos se nombran.
        LogBus.Log("workflow", $"resultado: ok={result.Ok} · {result.Tally} · alineado={result.AlignedConsciously}{(result.Ok ? "" : " · error=" + result.Error)}");
        TelemetryBus.Emit("workflow_end", workflowId: workflowId, runId: runId,
            phase: result.Ok ? "ok" : "error",
            label: result.Ok ? $"completado ({result.Tally})" : result.Error,
            detail: new { completed = result.Completed, omitted = result.Omitted, steps = result.Total, aligned = result.AlignedConsciously });
        if (result.Ok && result.AlignedConsciously)
        {
            // APRENDIZAJE: me tuve que alinear conscientemente. Enseñárselo al workflow para que la
            // próxima vez arranque solo desde el principio (loop consciente→subconsciente).
            LogBus.Log("workflow", $"aprendiendo alineación → prepend en {workflowId}");
            _ = graph.PrependAlignmentStepAsync(workflowId, ct);
        }
        // Lo que lee el CEREBRO. Si aquí se le dice «completado» de una corrida que omitió pasos, el
        // consciente da la tarea por buena y no la retoma — el peor final posible de los tres.
        if (!result.Ok) return $"el workflow falló: {result.Error}";
        return result.Omitted > 0
            ? $"parcial — {result.Tally}. Los omitidos NO se ejecutaron; comprueba si hacían falta antes de dar la tarea por terminada."
            : $"ok — workflow completado ({result.Tally})";
    }
}
