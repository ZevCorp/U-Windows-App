using System.Text;
using U.Graph;

namespace U.WindowsClient.Clinical;

/// <summary>Un dato, el campo donde iría, y por qué. Lo que el operador aprueba o no.</summary>
public sealed record Binding(
    ClinicalValue Data,
    DetectedField Field,
    string FieldLabel,
    bool Occupied);

/// <summary>
/// Empareja los conceptos clínicos con los campos que hay en la pantalla.
///
/// Por ETIQUETA y no por id, a propósito: los ids del dynpro (`RNPA10-TALLA`) cambian
/// entre transacciones y entre versiones del sistema, mientras que lo que el humano lee
/// —«Talla», «Peso», «Frec. Cardíaca»— es estable porque es lo que hace usable la
/// pantalla. Además así no hay que grabar nada antes: funciona la primera vez.
///
/// LA REGLA QUE MANDA: ante la duda, no se coloca. Un campo vacío lo llena el médico en
/// dos segundos; uno con el número equivocado en una historia clínica puede no verlo
/// nadie. Por eso cada patrón exige palabras completas, y todo lo ambiguo se descarta
/// en vez de resolverse a favor.
/// </summary>
public static class ConceptBinder
{
    /// <summary>
    /// Qué etiqueta corresponde a cada concepto. El orden importa: gana el primero que
    /// case, así que lo específico va antes que lo genérico —«frecuencia cardíaca»
    /// antes que cualquier cosa con «frecuencia».
    /// </summary>
    private static readonly (string Concept, string[] Words)[] Rules =
    {
        ("paciente.edad",                  new[] { "edad" }),
        ("vital.talla",                    new[] { "talla", "estatura" }),
        ("vital.peso",                     new[] { "peso" }),
        ("vital.frecuencia.cardiaca",      new[] { "freccardiaca", "frecuenciacardiaca", "fc", "pulso" }),
        ("vital.frecuencia.respiratoria",  new[] { "frecrespiratoria", "frecuenciarespiratoria", "fr" }),
        ("vital.temperatura",              new[] { "temperatura", "temp" }),
        ("vital.saturacion",               new[] { "sato2", "saturacion", "spo2" }),
    };

    /// <summary>La presión arterial son DOS campos contiguos y se resuelven juntos o nada.</summary>
    private static readonly string[] PressureWords = { "presionarterial", "tensionarterial", "presion", "tension" };

    /// <summary>
    /// Cruza los datos con los campos de la pantalla. Devuelve solo lo que se pudo
    /// resolver sin ambigüedad; lo demás sencillamente no aparece.
    /// </summary>
    public static IReadOnlyList<Binding> Bind(
        IReadOnlyList<ClinicalValue> data,
        IReadOnlyList<DetectedField> fields)
    {
        var result = new List<Binding>();
        if (data.Count == 0 || fields.Count == 0) return result;

        // Solo campos de texto que se puedan escribir. Nada de botones ni de shells.
        var writable = fields
            .Where(f => f.ActionType == "input" && f.Selector.Length > 0)
            .Select(f => (Field: f, Key: Normalize(f.Label)))
            .Where(f => f.Key.Length > 0)
            .ToList();

        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (concept, words) in Rules)
        {
            ClinicalValue? value = data.FirstOrDefault(d => d.Concept == concept);
            if (value == null) continue;

            // Se exige que el campo case con ALGUNA palabra completa. Un `Contains`
            // suelto haría que «fr» encajara dentro de «frecuencia cardíaca» y colocara
            // la respiratoria en el campo de la cardíaca.
            var matches = writable
                .Where(f => !used.Contains(f.Field.Selector) && words.Any(w => Matches(f.Key, w)))
                .ToList();

            // Cero campos: la pantalla no tiene ese dato. Más de uno: no sabemos cuál, y
            // adivinar entre dos campos clínicos no es una opción.
            if (matches.Count != 1) continue;

            var target = matches[0];
            used.Add(target.Field.Selector);
            result.Add(new Binding(
                value, target.Field, target.Field.Label,
                Occupied: !string.IsNullOrWhiteSpace(target.Field.CurrentValue)));
        }

        BindPressure(data, writable, used, result);
        return result;
    }

    /// <summary>
    /// La presión son dos casillas juntas —sistólica y diastólica— que suelen compartir
    /// etiqueta. Se resuelven por POSICIÓN: la primera de las dos es la sistólica,
    /// porque así se lee y así se escribe. Si no hay exactamente dos, no se toca
    /// ninguna: media presión arterial es peor dato que ninguna.
    /// </summary>
    private static void BindPressure(
        IReadOnlyList<ClinicalValue> data,
        List<(DetectedField Field, string Key)> writable,
        HashSet<string> used,
        List<Binding> result)
    {
        ClinicalValue? sys = data.FirstOrDefault(d => d.Concept == "vital.presion.sistolica");
        ClinicalValue? dia = data.FirstOrDefault(d => d.Concept == "vital.presion.diastolica");
        if (sys == null || dia == null) return;

        var boxes = writable
            .Where(f => !used.Contains(f.Field.Selector) && PressureWords.Any(w => Matches(f.Key, w)))
            .OrderBy(f => f.Field.StepOrder)   // orden de lectura de la pantalla
            .ToList();

        if (boxes.Count != 2) return;

        result.Add(new Binding(sys, boxes[0].Field, boxes[0].Field.Label,
            !string.IsNullOrWhiteSpace(boxes[0].Field.CurrentValue)));
        result.Add(new Binding(dia, boxes[1].Field, boxes[1].Field.Label,
            !string.IsNullOrWhiteSpace(boxes[1].Field.CurrentValue)));
        used.Add(boxes[0].Field.Selector);
        used.Add(boxes[1].Field.Selector);
    }

    /// <summary>
    /// ¿La etiqueta contiene esa palabra como palabra, no como trozo? Como al normalizar
    /// se quitan espacios y puntos, se comprueba que lo que rodea a la coincidencia no
    /// sea una letra: así «fr» casa con «frecresp» pero no dentro de «frecuencia».
    /// </summary>
    private static bool Matches(string haystack, string needle)
    {
        int i = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (i >= 0)
        {
            bool leftOk = i == 0 || !char.IsLetter(haystack[i - 1]);
            int end = i + needle.Length;
            bool rightOk = end >= haystack.Length || !char.IsLetter(haystack[end]);
            if (leftOk && rightOk) return true;
            i = haystack.IndexOf(needle, i + 1, StringComparison.Ordinal);
        }
        return false;
    }

    /// <summary>
    /// Etiqueta → clave comparable: minúsculas, sin tildes y sin nada que no sea letra o
    /// dígito. «Frec. Cardíaca» y «FREC CARDIACA» tienen que ser lo mismo, porque SAP las
    /// escribe distinto según de dónde salga el texto (Tooltip, Name o Text).
    /// </summary>
    private static string Normalize(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return "";
        string lower = label.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(lower.Length);
        foreach (char c in lower)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) sb.Append(c);
        }
        return sb.ToString();
    }
}
