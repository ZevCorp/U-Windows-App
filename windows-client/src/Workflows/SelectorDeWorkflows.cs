namespace U.WindowsClient.Workflows;

/// <summary>
/// EN QUÉ ORDEN SE VEN y CUÁL QUEDA ELEGIDO. Promesa 110 (spec 007).
/// </summary>
/// <remarks>
/// Graph lista del más viejo al más nuevo (<c>ORDER BY w.id ASC</c>), y el carrusel conservaba su
/// índice al recargar: lo recién enseñado quedaba al final y sin elegir, y el operador tenía que ir
/// a buscarlo a ciegas entre tres «Workflow sin descripción». Lo último que enseñaste es lo primero
/// que ves, y si acabas de enseñar, es lo que está elegido.
/// </remarks>
public static class SelectorDeWorkflows
{
    /// <summary>Del más nuevo al más viejo. Empate (o sin fecha) por id descendente: los ids de Graph
    /// son marcas de tiempo, así que el desempate sigue siendo «lo más nuevo primero».</summary>
    public static List<WorkflowSummary> Ordenar(IEnumerable<WorkflowSummary> workflows) =>
        workflows
            .OrderByDescending(w => w.CreatedAt)
            .ThenByDescending(w => w.Id, StringComparer.Ordinal)
            .ToList();

    /// <summary>Dónde está el workflow con este id en la lista ya ordenada; 0 —el más nuevo— si no
    /// consta (por ejemplo, porque el cierre no devolvió el id).</summary>
    public static int IndiceDe(IReadOnlyList<WorkflowSummary> ordenados, string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return 0;
        for (int i = 0; i < ordenados.Count; i++)
            if (string.Equals(ordenados[i].Id, id, StringComparison.Ordinal)) return i;
        return 0;
    }
}
