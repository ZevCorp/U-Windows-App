using System.Text.Json;

namespace U.Ciclo;

/// <summary>Un plan leído: sus pasos, o por qué no hay ninguno.</summary>
public sealed record PlanLeido(IReadOnlyList<string> Pasos, string Porque);

/// <summary>Un paso del plan con cómo acabó: Hecho, Fallido u Omitido.</summary>
public sealed record PasoDelPlan(string Objetivo, string Estado);

/// <summary>El plan entero juzgado sobre el plan, nunca sobre lo ejecutado (promesa 439, patrón nº10).</summary>
public sealed record PlanResultado(IReadOnlyList<PasoDelPlan> Pasos, string Resumen);

/// <summary>
/// EL PLAN DE LUNA: una lista de objetivos cortos que el ciclo sabe ejecutar. Formato:
/// <c>{"pasos": ["abre: notepad", "abrir el menú Archivo", "escribe: hola", "tecla: Enter"]}</c>.
/// Un paso sin prefijo es un objetivo para Jev; con prefijo, un gesto directo que no necesita decidir nada.
/// </summary>
public static class Plan
{
    public static PlanLeido Leer(string respuesta)
    {
        string texto = (respuesta ?? "").Trim();
        if (texto.StartsWith("```"))
        {
            int ini = texto.IndexOf('\n'), fin = texto.LastIndexOf("```", StringComparison.Ordinal);
            texto = ini >= 0 && fin > ini ? texto[(ini + 1)..fin].Trim() : texto.Trim('`');
        }
        if (texto.Length == 0) return new PlanLeido(Array.Empty<string>(), "Luna no devolvió ningún plan");
        try
        {
            using var doc = JsonDocument.Parse(texto);
            if (!doc.RootElement.TryGetProperty("pasos", out var pasos) || pasos.ValueKind != JsonValueKind.Array)
                return new PlanLeido(Array.Empty<string>(), "el plan de Luna no trae «pasos»");
            var lista = pasos.EnumerateArray()
                .Where(p => p.ValueKind == JsonValueKind.String)
                .Select(p => (p.GetString() ?? "").Trim())
                .Where(p => p.Length > 0)
                .ToList();
            return lista.Count == 0
                ? new PlanLeido(Array.Empty<string>(), "el plan de Luna está vacío: no hay nada que ejecutar")
                : new PlanLeido(lista, "");
        }
        catch (JsonException e)
        {
            return new PlanLeido(Array.Empty<string>(), $"el plan de Luna no es JSON ({e.Message})");
        }
    }

    /// <summary>
    /// Cómo acabó cada paso. <paramref name="hechos"/> trae los que se intentaron, en orden; los que no llegaron
    /// a intentarse quedan Omitidos, y el resumen se cuenta sobre el plan entero.
    /// </summary>
    public static PlanResultado Resultado(IReadOnlyList<string> plan, IReadOnlyList<bool> hechos)
    {
        var pasos = new List<PasoDelPlan>();
        for (int i = 0; i < plan.Count; i++)
            pasos.Add(new PasoDelPlan(plan[i], i < hechos.Count ? (hechos[i] ? "Hecho" : "Fallido") : "Omitido"));
        int h = pasos.Count(p => p.Estado == "Hecho"), f = pasos.Count(p => p.Estado == "Fallido"), o = pasos.Count(p => p.Estado == "Omitido");
        return new PlanResultado(pasos, $"{h} de {plan.Count} hechos · {f} fallido(s) · {o} omitido(s)");
    }
}
