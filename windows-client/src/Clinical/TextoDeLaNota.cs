using System.Text.Json;

namespace U.WindowsClient.Clinical;

/// <summary>
/// LA NOTA COMO TEXTO PLANO: lo que copia «Copiar nota». Promesa 460 (spec 055).
/// </summary>
/// <remarks>
/// PUERTO de `noteSections` + `noteAsPlainText` (`lib/clinical/note-plain-text.ts`, extraído de la
/// pantalla en vivo de la web el 2026-09-26 justo para esto). Resumen, secciones y el cierre —plan,
/// recomendaciones, signos de alarma—, cada bloque con su título y separados por una línea en blanco;
/// lo vacío se dice «Sin información documentada.» en vez de desaparecer. Pegado en otro sistema, la
/// nota copiada en Windows y la copiada en la web son la misma.
/// </remarks>
public static class TextoDeLaNota
{
    private const string SinInformacion = "Sin información documentada.";

    public static string Plano(JsonElement nota)
    {
        var bloques = new List<(string Titulo, string Contenido)>();
        var cierre = Obj(nota, "discharge");
        var plan = Obj(cierre, "plan");

        var lineasDelPlan = new List<string>();
        foreach (var m in Arr(plan, "medications"))
        {
            var partes = new[] { "name", "dose", "route", "frequency", "duration", "instructions" }
                .Select(c => Cad(m, c)).Where(x => x.Length > 0);
            lineasDelPlan.Add(string.Join(" · ", partes));
        }
        lineasDelPlan.AddRange(Arr(plan, "non_pharmacological").Select(x => Cad(x, "text")));
        lineasDelPlan.AddRange(Arr(plan, "follow_up").Select(x => Cad(x, "text")));
        lineasDelPlan = lineasDelPlan.Where(x => x.Length > 0).ToList();

        bloques.Add(("Resumen", O(Cad(nota, "summary").Trim())));
        foreach (var s in Arr(nota, "sections"))
            bloques.Add((Cad(s, "label"), O(Cad(s, "content").Trim())));
        bloques.Add(("Plan terapéutico", O(string.Join("\n", lineasDelPlan))));
        bloques.Add(("Recomendaciones", O(string.Join("\n", Arr(cierre, "recommendations").Select(x => Cad(x, "text"))))));
        bloques.Add(("Signos de alarma", O(string.Join("\n", Arr(cierre, "alarm_signs").Select(x => Cad(x, "text"))))));

        return string.Join("\n\n", bloques.Select(b => $"{b.Titulo}\n{b.Contenido}"));
    }

    /// <summary>La nota que se ve, como texto plano (con su crudo si lo tiene: ahí vive el cierre).</summary>
    public static string DeLaNota(NotaClinica nota)
    {
        using var doc = JsonDocument.Parse(nota.ComoNodo().ToJsonString());
        return Plano(doc.RootElement);
    }

    private static string O(string s) => s.Length > 0 ? s : SinInformacion;

    private static JsonElement Obj(JsonElement o, string campo) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.Object ? v : default;

    private static IEnumerable<JsonElement> Arr(JsonElement o, string campo) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray() : Enumerable.Empty<JsonElement>();

    private static string Cad(JsonElement o, string campo) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";
}
