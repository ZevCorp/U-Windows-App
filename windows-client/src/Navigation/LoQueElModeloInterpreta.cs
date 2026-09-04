using System.Text.Json;

namespace U.WindowsClient.Navigation;

/// <summary>Lo que el cerebro entendió de una demostración: qué es dato y qué se aprendió.</summary>
/// <param name="Juzgados">Los campos sobre los que el modelo SÍ opinó, diga que son dato o que no.
/// Sin esta lista, «el modelo dijo que esto es navegación» y «el modelo no lo miró» son
/// indistinguibles — y llevan a decisiones opuestas: en el primer caso el valor se reproduce, en el
/// segundo hay que quedarse con lo que dedujo la regla del narrado (promesa 134).</param>
public sealed record LoInterpretado(
    IReadOnlyList<Hueco> Huecos,
    IReadOnlyList<(string Selector, string Significado)> Recuerdos,
    bool Hubo,
    IReadOnlyList<string>? Juzgados = null);

/// <summary>
/// LO QUE EL MODELO ENTIENDE DE UNA DEMOSTRACIÓN. Promesas 129 y 130 (spec 009).
/// </summary>
/// <remarks>
/// POR QUÉ EL MODELO Y NO UNA REGLA (decisión del dueño, 2026-09-03). Distinguir un DATO de una
/// parte fija de la tarea es exactamente el tipo de juicio que necesita contexto: «70» en un campo
/// llamado Peso es un dato del paciente; «nwp1» en el campo de comandos es cómo se llega. Una regla
/// —«lo que narras es un dato»— acierta en el caso que la inspiró y falla en el siguiente, y este
/// sistema tiene que servir para cualquier programa y cualquier tarea. Quien entiende el contexto
/// entero de la demo es el modelo, así que se le pregunta a él.
///
/// LO QUE SE LE DELEGA Y LO QUE NO, y esta línea es lo único que hace segura la delegación:
///
///   · SE LE DELEGA EL CRITERIO: esto es un dato, esto es navegación, esto significa aquello.
///   · NO SE LE DELEGA LA IDENTIDAD. Un campo que la demo no TOCÓ no existe para esta skill, lo
///     nombre quien lo nombre (promesa 129). Es el mismo guardarraíl que protege los recuerdos del
///     video (125): el selector lo pone la mano, no la prosa.
///
/// Sin esa separación, una alucinación del modelo escribiría un dato clínico en un campo que nadie
/// eligió — y eso no daría error, daría un número plausible en el sitio equivocado.
///
/// Y SI EL CEREBRO NO CONTESTA, NO SE PIERDE NADA (promesa 130). Vive al otro lado de una red que en
/// esta misma máquina parpadea, y Vercel devuelve 504 con demos largas. Una respuesta ausente o
/// ilegible se lee como «no opinó», nunca como «no hay datos»: la skill se queda con lo que se narró
/// —que ya está en disco y es determinista— y la comprobación lo dice en vez de callarlo.
///
/// PURO: no hace la llamada, solo lee la respuesta. Así el contrato juzga la parte que puede
/// equivocarse en silencio sin necesitar red ni cerebro.
/// </remarks>
public static class LoQueElModeloInterpreta
{
    /// <summary>¿Llegó a opinar el cerebro? Distinguirlo es lo que permite decirlo.</summary>
    public static bool HuboInterpretacion(LoInterpretado lo) => lo?.Hubo == true;

    /// <summary>
    /// Lee la respuesta del cerebro, acotada SIEMPRE a los campos que la demo tocó.
    /// </summary>
    /// <remarks>
    /// Forma esperada, y si no llega así no pasa nada:
    /// <code>
    /// { "campos":   [{ "campo": "&lt;selector&gt;", "esDato": true, "significado": "..." }],
    ///   "recuerdos":[{ "campo": "&lt;selector&gt;", "significado": "..." }] }
    /// </code>
    /// </remarks>
    public static LoInterpretado Leer(string json, SkillEnsenada skill)
    {
        var vacio = new LoInterpretado(
            Array.Empty<Hueco>(), Array.Empty<(string, string)>(), false, Array.Empty<string>());
        if (skill == null || string.IsNullOrWhiteSpace(json)) return vacio;

        // LO QUE LA DEMO TOCÓ, con su valor: la única lista de identidades legítimas.
        var tocado = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in skill.Pasos)
            if (p.Exit.Length > 0 && !tocado.ContainsKey(p.Exit)) tocado[p.Exit] = p.Texto ?? "";

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return vacio;

            var huecos = new List<Hueco>();
            var recuerdos = new List<(string, string)>();
            var juzgados = new List<string>();
            bool hubo = false;

            if (doc.RootElement.TryGetProperty("campos", out var campos)
                && campos.ValueKind == JsonValueKind.Array)
            {
                hubo = true;
                foreach (var c in campos.EnumerateArray())
                {
                    string campo = Texto(c, "campo");
                    if (!tocado.TryGetValue(campo, out var ejemplo)) continue;   // no lo tocó nadie
                    if (!c.TryGetProperty("esDato", out var d)
                        || d.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        continue;                                                 // no llegó a opinar
                    juzgados.Add(campo);                                          // opinó, diga lo que diga
                    if (d.ValueKind is JsonValueKind.False) continue;              // parte de la tarea
                    if (huecos.Any(h => h.Campo.Equals(campo, StringComparison.OrdinalIgnoreCase))) continue;
                    huecos.Add(new Hueco(campo, Texto(c, "significado"), "texto", ejemplo));
                }
            }

            if (doc.RootElement.TryGetProperty("recuerdos", out var rs)
                && rs.ValueKind == JsonValueKind.Array)
            {
                hubo = true;
                foreach (var r in rs.EnumerateArray())
                {
                    string campo = Texto(r, "campo");
                    string que = Texto(r, "significado");
                    if (que.Length == 0 || !tocado.ContainsKey(campo)) continue;
                    if (recuerdos.Any(x => x.Item1.Equals(campo, StringComparison.OrdinalIgnoreCase))) continue;
                    recuerdos.Add((campo, que));
                }
            }

            return new LoInterpretado(huecos, recuerdos, hubo, juzgados);
        }
        catch (JsonException)
        {
            // Ilegible es «no opinó», no «no hay nada»: la diferencia es lo que permite seguir con
            // lo narrado en vez de dar la demo por muda.
            return vacio;
        }
    }

    private static string Texto(JsonElement e, string prop) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v)
        && v.ValueKind == JsonValueKind.String
            ? (v.GetString() ?? "").Trim() : "";
}
