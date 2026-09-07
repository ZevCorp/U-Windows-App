using System.Text.Json;

namespace U.WindowsClient.Piloto;

/// <summary>Un paso del plan que el piloto entrega: qué puerta, qué texto, qué recuerdo, y qué evento de la lección es.</summary>
/// <param name="N">El evento de la lección que este paso cumple (0 si no corresponde a ninguno).</param>
/// <param name="Exit">La puerta por su nombre, como la entiende <c>map_take</c>.</param>
/// <param name="Texto">Lo que se teclea, si el paso escribe.</param>
/// <param name="Tecla">La tecla que cierra el paso (enter, f3…), si la hay.</param>
/// <param name="Recuerdo">Lo que Ü entendió del elemento: se cuelga ANTES de tocarlo, en la pantalla donde vive.</param>
/// <param name="Decir">Lo que Ü dice en voz alta antes de dar el paso. Corto.</param>
public sealed record PasoDelPlan(int N, string Exit, string Texto, string Tecla, string Recuerdo, string Decir);

/// <summary>
/// EL PLAN DE COMPROBACIÓN: lo que el piloto entiende, dicho en el idioma del ejecutor. Promesa 179 (spec 012).
/// </summary>
/// <remarks>
/// POR QUÉ UN PLAN Y NO PASO A PASO. Las dos primeras pruebas reales (2026-09-07) costaron 333 s y
/// $4,06, y luego 114 s y $1,75, con el modelo dando cada paso y esperando cada respuesta. El
/// ejecutor de tanda hizo la misma ruta en 25 s el 2026-09-03. El «de uno en uno» (promesa 141)
/// nació porque el batch iba ciego —72 toques ciegos, sin recuerdos—; con la lección ya no está
/// ciego. El valor del modelo está en INTERPRETAR —cuadros, puertas y lo dicho → qué tocar, qué
/// significa, a dónde lleva—, no en ejecutar. El dueño lo aprobó: «me gusta mucho».
///
/// LO QUE SE CONSERVA DEL «DE UNO EN UNO»: la app recorre el plan paso a paso, y en cada paso dice
/// en voz lo que el piloto escribió, cuelga el recuerdo donde vive el elemento, da el paso por el
/// MISMO ejecutor de tanda (compuerta, cuenta honesta) y juzga la llegada. Si un paso no se puede
/// dar, PARA AHÍ y le devuelve al piloto dónde quedó y qué faltó: el piloto sigue con las manos
/// desde ese paso. Eso es el rescate de la spec 009, y es el único momento en que el modelo actúa.
///
/// PURO: leer el JSON y redactar el relato. El recorrido lo hace la ventana con las manos de la app.
/// </remarks>
public static class PlanDeComprobacion
{
    public readonly record struct Lectura(IReadOnlyList<PasoDelPlan> Pasos, string Error);

    /// <summary>Del JSON del piloto a pasos. Un paso sin puerta ni texto es un error que se dice.</summary>
    public static Lectura Leer(string json)
    {
        var pasos = new List<PasoDelPlan>();
        if (string.IsNullOrWhiteSpace(json))
            return new(pasos, "falta `pasos`: una lista JSON de pasos, p. ej. [{\"n\":1,\"exit\":\"comando\",\"text\":\"nwp1\",\"tecla\":\"enter\",\"recuerdo\":\"…\",\"decir\":\"…\"}].");
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return new(pasos, "`pasos` tiene que ser una LISTA de pasos, no un objeto suelto.");
            foreach (var p in doc.RootElement.EnumerateArray())
            {
                string S(string k) => p.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";
                int n = p.TryGetProperty("n", out var nv) && nv.ValueKind == JsonValueKind.Number ? nv.GetInt32() : 0;
                string exit = S("exit"), texto = S("text"), tecla = S("tecla");
                if (exit.Length == 0 && texto.Length == 0 && tecla.Length == 0)
                    return new(pasos, $"el paso {pasos.Count + 1} no trae ni `exit` ni `text` ni `tecla`: no sé qué hacer con él.");
                pasos.Add(new PasoDelPlan(n, exit, texto, tecla, S("recuerdo"), S("decir")));
            }
        }
        catch (Exception ex) { return new(pasos, $"no entendí `pasos` como JSON: {ex.Message}"); }
        if (pasos.Count == 0) return new(pasos, "la lista de pasos vino vacía.");
        return new(pasos, "");
    }

    /// <summary>
    /// El relato de vuelta al piloto. Si paró, dice dónde y le pasa las manos; si acabó, dice la cuenta.
    /// </summary>
    /// <param name="hechos">Pasos del plan que se dieron.</param>
    /// <param name="total">Pasos del plan.</param>
    /// <param name="paradoEn">El paso en que paró (1..total), o 0 si acabó.</param>
    /// <param name="porQue">Lo que dijo el ejecutor al parar.</param>
    /// <param name="donde">Dónde quedó la pantalla.</param>
    /// <param name="aterrizados">Cuántos eventos que navegan aterrizaron, según el juez.</param>
    /// <param name="deLosQueNavegan">Cuántos eventos navegan en la lección.</param>
    public static string Relato(int hechos, int total, int paradoEn, string porQue, string donde,
        int aterrizados, int deLosQueNavegan)
    {
        string juez = $"El juez dice: {aterrizados} de {deLosQueNavegan} evento(s) que navegan aterrizaron.";
        if (paradoEn > 0)
            return $"HICE {hechos} DE {total} y PARÉ en el paso {paradoEn}: {porQue} Estás en «{donde}». "
                 + "Sigue tú desde ese paso, de uno en uno, con las manos: mira con map_what_i_see, actúa, "
                 + $"y declara cada llegada con leccion_llegue(n). {juez}";
        return $"HICE LOS {total} PASO(S) del plan. Estás en «{donde}». {juez} "
             + "Si todos aterrizaron, guarda la skill con leccion_guardar_skill; si no, revisa los que fallaron con las manos.";
    }
}
