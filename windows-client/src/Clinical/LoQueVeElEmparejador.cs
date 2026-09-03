namespace U.WindowsClient.Clinical;

/// <summary>
/// CÓMO SE LE PRESENTA LA PANTALLA AL EMPAREJADOR. Promesas 115 y 116 (spec 008).
/// </summary>
/// <remarks>
/// LA APUESTA DE ESTA ETAPA, decidida el 2026-09-02: el dato vuelve de la prosa **con inteligencia**,
/// no con reglas. Un extractor determinista de signos vitales llenaría más campos hoy y sabría de
/// medicina para siempre — y eso es justo lo que no queremos: el día que la nota sea de otra cosa
/// (una factura, un acta) la regla no sirve y el modelo sí. Así que no se le quita trabajo al
/// modelo: se le quitan las VENDAS.
///
/// LA NOTA ORGANIZADA ES LO ÚNICO QUE SE MANDA, y es una decisión, no una limitación: organizar la
/// nota es el primer filtro de calidad, el que el médico revisa y aprueba con el ✓. Mandar además
/// la transcripción cruda sería saltarse ese filtro por la puerta de atrás — entraría a la historia
/// clínica lo que el organizador descartó a propósito.
///
/// LO QUE SÍ FALTABA es lo que se sabe de la PANTALLA, medido en la corrida del 18:10 (3 campos de
/// 9). Dos vendas, las dos sobre el inventario de campos:
///
///   1. **La casilla diastólica se llamaba «/» y se escondía.** El filtro de etiquetas cortas la
///      dejaba fuera para que un número suelto no cayera ahí; el efecto real es que la presión
///      arterial NUNCA se podía llenar entera. Esconder un campo no es enseñar dónde va: ahora se
///      ofrece diciendo de quién es la casilla, y decide el modelo.
///   2. **Lo enseñado no llegaba.** Los recuerdos («esto es X», con foto) viven colgados del
///      elemento en el grafo desde el 2026-08-23 y nadie los leía a la hora de decidir qué escribir.
///      Ahora viajan con su campo: es el canal por el que una enseñanza cambia lo que Ü hace, sin
///      una sola regla de dominio en el código.
///
/// EL CANAL ES LA ETIQUETA, y no es un capricho: el emparejador de Graph copia el campo a campo lo
/// que recibe (`NoteFieldMatcher.buildMessages`) y descarta cualquier propiedad que no esté en su
/// lista — stepOrder, actionType, label, selector, controlType, allowedOptions, currentValue. Una
/// pista mandada en un campo nuevo no llegaría nunca al modelo. La etiqueta sí llega, y es además
/// donde una persona la leería.
///
/// SE ESCRIBE AQUÍ, PURO, y no dentro del rellenador, porque el emparejador vive al otro lado de la
/// red: esto es lo único de esa conversación que se puede juzgar sin backend y sin pantalla.
/// </remarks>
public static class LoQueVeElEmparejador
{
    /// <summary>Una etiqueta que de verdad nombra algo. «Peso» sí; «/», «:» o «» no.</summary>
    public static bool EsUtil(string etiqueta) =>
        (etiqueta ?? "").Trim().Length >= 3 && (etiqueta ?? "").Any(char.IsLetter);

    /// <summary>
    /// ¿Este campo entra en el inventario? Entra si se puede nombrar: por su etiqueta, por la del
    /// campo que lo precede en la pantalla, o porque alguien le enseñó a Ü qué es. Una casilla que
    /// nadie puede nombrar no se ofrece — el modelo no tendría con qué decidir y adivinaría.
    /// </summary>
    public static bool MereceOfrecerse(string etiqueta, string etiquetaAnterior, string recuerdo) =>
        EsUtil(etiqueta) || EsUtil(etiquetaAnterior) || (recuerdo ?? "").Trim().Length > 0;

    /// <summary>
    /// Cómo se le presenta un campo al emparejador: su etiqueta, de quién es la casilla cuando su
    /// etiqueta no nombra nada, y lo que se le haya enseñado sobre él.
    /// </summary>
    public static string EtiquetaCon(string etiqueta, string etiquetaAnterior, string recuerdo)
    {
        string propia = (etiqueta ?? "").Trim();
        string anterior = (etiquetaAnterior ?? "").Trim();
        string ensenado = (recuerdo ?? "").Trim();

        string baseTexto = EsUtil(propia) ? propia
            : !EsUtil(anterior) ? propia
            : propia.Length > 0
                ? $"«{propia}» — la casilla que sigue a «{anterior}», parte del mismo dato"
                : $"la casilla que sigue a «{anterior}», parte del mismo dato";

        if (ensenado.Length == 0) return baseTexto;

        // LO ENSEÑADO VA AL FINAL Y DICHO COMO LO QUE ES: una instrucción de quien opera esta
        // pantalla, no una etiqueta más. Sin la marca, el modelo lee una etiqueta larguísima y no
        // sabe que eso pesa más que el nombre del campo.
        return baseTexto.Length > 0
            ? $"{baseTexto} · lo que me enseñaron aquí: {ensenado}"
            : $"lo que me enseñaron aquí: {ensenado}";
    }

    /// <summary>
    /// La misma identidad escrita por los dos lados. El grafo guarda los campos de SAP con el
    /// prefijo <c>sap:</c> (<c>SapSelector.ById</c>) y el lector del formulario los devuelve sin él,
    /// así que un recuerdo enseñado nunca casaría con su campo: la comparación daría falso SIEMPRE
    /// y en silencio, que es el aprendizaje nº16 de este repo con otra ropa.
    /// </summary>
    public static string MismaIdentidad(string selector)
    {
        string s = (selector ?? "").Trim();
        if (s.StartsWith("sap:", StringComparison.OrdinalIgnoreCase)) s = s[4..];
        return s;
    }
}
