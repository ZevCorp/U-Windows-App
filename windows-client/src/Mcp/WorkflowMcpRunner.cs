using U.Graph;
using U.Graph.Surfaces;
using U.WindowsClient.Agent;
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
    }

    public async Task<string> RunAsync(string workflowId, string context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(workflowId))
            return "la llamada al workflow no trajo workflow_id";
        if (!_graphConfig.IsConfigured)
            return "workflows no disponibles: falta la API key de Graph (graph.json)";

        var graph = new GraphClient(_graphConfig);
        var player = new WorkflowPlayer(graph, _graphConfig, _uia, _sap)
        {
            // Alineación consciente: si no estamos en la superficie del workflow, abrir/enfocar la app.
            Aligner = AppAligner.EnsureAsync
        };
        player.StepDone += (_, outcome) =>
            _voice.Narrate(outcome.Ok ? $"✓ {outcome.Label}" : $"✗ {outcome.Label}: {outcome.Error}");

        var variables = string.IsNullOrWhiteSpace(context)
            ? null
            : new Dictionary<string, string> { ["context"] = context };

        // strictSurface: el cerebro eligió este workflow porque ESTA superficie coincide; si al
        // ejecutarse ya no coincide (el usuario navegó), el Aligner se alinea antes de tocar nada.
        RunResult result = await player.RunAsync(workflowId, variables, strictSurface: true, ct);
        if (result.Ok && result.AlignedConsciously)
        {
            // APRENDIZAJE: me tuve que alinear conscientemente. Enseñárselo al workflow para que la
            // próxima vez arranque solo desde el principio (loop consciente→subconsciente).
            _ = graph.PrependAlignmentStepAsync(workflowId, ct);
        }
        return result.Ok
            ? $"ok — workflow completado ({result.Completed} pasos)"
            : $"el workflow falló: {result.Error}";
    }
}
