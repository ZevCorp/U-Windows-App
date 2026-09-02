using System.Text.Json;

namespace U.WindowsClient.Workflows;

/// <summary>
/// Vista liviana de un workflow para el selector y la biblioteca. Vivía dentro de la ventana de la
/// biblioteca; salió de ahí el 2026-09-02 (spec 007) porque el nombre, el orden y la precarga del
/// plan son decisiones que el contrato tiene que poder juzgar sin abrir una ventana.
///
/// El shape de <c>GET /api/v1/workflows</c> se midió contra el Graph vivo ese día: <c>id</c>,
/// <c>description</c>, <c>summary</c>, <c>sourceOrigin</c>, <c>sourceTitle</c>, <c>totalSteps</c>,
/// <c>createdAt</c> como entero Neo4j <c>{low, high}</c>. Se tolera lo demás.
/// </summary>
public sealed class WorkflowSummary
{
    public string Id { get; init; } = "";

    /// <summary>El nombre DERIVADO: la descripción si es de verdad, y si no, lo que se sabe del
    /// workflow (app, ventana, cuándo, cuántos pasos). Ver <see cref="NombreDeWorkflow"/>.</summary>
    public string Title { get; init; } = "";

    /// <summary>El nombre que le puso el operador, si le puso. Manda sobre <see cref="Title"/>.</summary>
    public string? NombrePropio { get; set; }

    /// <summary>Lo que se muestra: el propio si lo hay, el derivado si no.</summary>
    public string Nombre => string.IsNullOrWhiteSpace(NombrePropio) ? Title : NombrePropio!;

    public int StepCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string SourceOrigin { get; init; } = "";
    public string SourceTitle { get; init; } = "";

    public override string ToString() => $"{Nombre} · {StepCount} paso(s)";

    public static WorkflowSummary FromJson(JsonElement e)
    {
        string id = Str(e, "id") ?? Str(e, "workflowId") ?? Str(e, "workflow_id") ?? "";
        int steps = Int(e, "totalSteps") ?? Int(e, "stepCount") ?? Int(e, "step_count") ?? CountArray(e, "steps") ?? 0;
        return new WorkflowSummary
        {
            Id = id,
            Title = NombreDeWorkflow.Derivar(e),
            StepCount = steps,
            CreatedAt = NombreDeWorkflow.CreadoEn(e),
            SourceOrigin = Str(e, "sourceOrigin") ?? Str(e, "source_origin") ?? "",
            SourceTitle = Str(e, "sourceTitle") ?? Str(e, "source_title") ?? "",
        };
    }

    internal static string? Str(JsonElement e, string prop) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    internal static int? Int(JsonElement e, string prop) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32() : null;

    private static int? CountArray(JsonElement e, string prop) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.GetArrayLength() : null;
}
