using System.Text.Json;
using System.Text.Json.Nodes;

namespace U.WindowsClient.Clinical;

/// <summary>Un medicamento del plan. Concentración y cantidad son las que pide la fórmula (spec 059).</summary>
public sealed record Medicamento(string Nombre, string Dosis, string Via, string Frecuencia, string Duracion,
    string Indicaciones, string Concentracion, string Cantidad);

/// <summary>Un signo de alarma y su urgencia («emergency», «priority», «monitor» o vacía).</summary>
public sealed record SignoDeAlarma(string Texto, string Urgencia);

/// <summary>El cierre de la consulta, como lo ve el médico.</summary>
public sealed record Egreso(
    IReadOnlyList<Medicamento> Medicamentos,
    IReadOnlyList<string> NoFarmacologicas,
    IReadOnlyList<string> Seguimiento,
    IReadOnlyList<string> Recomendaciones,
    IReadOnlyList<SignoDeAlarma> SignosDeAlarma);

/// <summary>
/// EL PLAN Y EL EGRESO de la nota (`note_json.discharge`): leerlo como la web y corregirlo campo a
/// campo. Promesas 472-474 (spec 058).
/// </summary>
/// <remarks>
/// SE CORRIGE SOBRE EL JSON REAL DE LA NOTA (<see cref="NotaClinica.Crudo"/>), no sobre este modelo:
/// cada operación toca UN campo y deja el resto como vino —la evidencia, la urgencia, lo que el
/// backend añada mañana—. Es la lección de la 461 aplicada al cierre, y el bug de la web al revés:
/// allí «Dosis y vía» era un solo campo que borraba la vía al corregir la dosis.
/// </remarks>
public static class EgresoDeLaNota
{
    /// <summary>Los campos de un medicamento que el médico puede tocar. Nada más se escribe.</summary>
    public static readonly IReadOnlyList<string> CamposDelMedicamento =
        new[] { "name", "dose", "route", "frequency", "duration", "instructions", "concentration", "quantity" };

    /// <summary>Las listas de texto del cierre, con su ruta dentro de `discharge`.</summary>
    private static readonly Dictionary<string, string[]> Listas = new(StringComparer.Ordinal)
    {
        ["non_pharmacological"] = new[] { "plan", "non_pharmacological" },
        ["follow_up"] = new[] { "plan", "follow_up" },
        ["recommendations"] = new[] { "recommendations" },
        ["alarm_signs"] = new[] { "alarm_signs" },
    };

    /// <summary>`ensureClinicalDischarge` de la web: lo que no es lista, vacío.</summary>
    public static Egreso Leer(JsonElement nota)
    {
        JsonElement d = nota.ValueKind == JsonValueKind.Object && nota.TryGetProperty("discharge", out var x) ? x : default;
        JsonElement plan = d.ValueKind == JsonValueKind.Object && d.TryGetProperty("plan", out var p) ? p : default;

        var meds = Arreglo(plan, "medications").Select(m => new Medicamento(
            Cad(m, "name"), Cad(m, "dose"), Cad(m, "route"), Cad(m, "frequency"), Cad(m, "duration"),
            Cad(m, "instructions"), Cad(m, "concentration"), Cad(m, "quantity"))).ToList();
        List<string> Textos(JsonElement o, string campo) => Arreglo(o, campo).Select(i => Cad(i, "text")).ToList();

        return new Egreso(meds, Textos(plan, "non_pharmacological"), Textos(plan, "follow_up"),
            Textos(d, "recommendations"),
            Arreglo(d, "alarm_signs").Select(a => new SignoDeAlarma(Cad(a, "text"), Cad(a, "urgency"))).ToList());
    }

    // ── corregir: cada operación toca UN campo y devuelve la nota nueva ──────

    public static NotaClinica CambiarMedicamento(NotaClinica nota, int indice, string campo, string valor)
    {
        if (!CamposDelMedicamento.Contains(campo)) return nota;
        return Editar(nota, cierre =>
        {
            var meds = (JsonArray)cierre["plan"]!["medications"]!;
            if (indice < 0 || indice >= meds.Count) return;
            if (meds[indice] is not JsonObject m) meds[indice] = m = new JsonObject();
            m[campo] = valor ?? "";
        });
    }

    public static NotaClinica AgregarMedicamento(NotaClinica nota) =>
        Editar(nota, cierre => ((JsonArray)cierre["plan"]!["medications"]!).Add(new JsonObject { ["name"] = "" }));

    public static NotaClinica QuitarMedicamento(NotaClinica nota, int indice) =>
        Editar(nota, cierre =>
        {
            var meds = (JsonArray)cierre["plan"]!["medications"]!;
            if (indice >= 0 && indice < meds.Count) meds.RemoveAt(indice);
        });

    public static NotaClinica CambiarItem(NotaClinica nota, string lista, int indice, string texto) =>
        Editar(nota, cierre =>
        {
            var arr = Lista(cierre, lista);
            if (arr == null || indice < 0 || indice >= arr.Count) return;
            if (arr[indice] is not JsonObject item) arr[indice] = item = new JsonObject();
            item["text"] = texto ?? "";
        });

    public static NotaClinica AgregarItem(NotaClinica nota, string lista, string texto) =>
        Editar(nota, cierre => Lista(cierre, lista)?.Add(new JsonObject { ["text"] = texto ?? "" }));

    public static NotaClinica QuitarItem(NotaClinica nota, string lista, int indice) =>
        Editar(nota, cierre =>
        {
            var arr = Lista(cierre, lista);
            if (arr != null && indice >= 0 && indice < arr.Count) arr.RemoveAt(indice);
        });

    /// <summary>La urgencia de un signo de alarma. Solo las tres de la web; vacía la quita.</summary>
    public static NotaClinica CambiarUrgencia(NotaClinica nota, int indice, string urgencia) =>
        Editar(nota, cierre =>
        {
            var arr = (JsonArray)cierre["alarm_signs"]!;
            if (indice < 0 || indice >= arr.Count || arr[indice] is not JsonObject item) return;
            if (urgencia is "emergency" or "priority" or "monitor") item["urgency"] = urgencia;
            else item.Remove("urgency");
        });

    // ── la maquinaria ───────────────────────────────────────────────────────

    private static NotaClinica Editar(NotaClinica nota, Action<JsonObject> cambio)
    {
        var raiz = nota.ComoNodo();
        var cierre = Asegurar(raiz);
        cambio(cierre);
        using var doc = JsonDocument.Parse(raiz.ToJsonString(NotaClinica.Escritura));
        return NotaClinica.Leer(doc.RootElement.Clone());
    }

    /// <summary>
    /// `ensureClinicalDischarge` sobre el nodo: el cierre con TODAS sus listas, conservando las que
    /// ya eran listas y lo demás que traiga.
    /// </summary>
    private static JsonObject Asegurar(JsonObject raiz)
    {
        if (raiz["discharge"] is not JsonObject cierre) raiz["discharge"] = cierre = new JsonObject();
        if (cierre["plan"] is not JsonObject plan) cierre["plan"] = plan = new JsonObject();
        foreach (var campo in new[] { "medications", "non_pharmacological", "follow_up" })
            if (plan[campo] is not JsonArray) plan[campo] = new JsonArray();
        foreach (var campo in new[] { "recommendations", "alarm_signs" })
            if (cierre[campo] is not JsonArray) cierre[campo] = new JsonArray();
        return cierre;
    }

    private static JsonArray? Lista(JsonObject cierre, string lista)
    {
        if (!Listas.TryGetValue(lista, out var ruta)) return null;
        JsonNode? n = cierre;
        foreach (var paso in ruta) n = n?[paso];
        return n as JsonArray;
    }

    private static IEnumerable<JsonElement> Arreglo(JsonElement o, string campo) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(campo, out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().ToList() : Enumerable.Empty<JsonElement>();

    private static string Cad(JsonElement o, string campo) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";
}
