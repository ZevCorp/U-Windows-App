namespace U.WindowsClient.Clinical;

/// <summary>
/// LA PLANTILLA QUE NADIE ELIGE. El médico habla; la estructura la pone el organizador.
/// </summary>
/// <remarks>
/// EL PROBLEMA, dicho por el usuario el 2026-09-01: «no quiero preocuparme por seleccionar ninguna
/// plantilla, sino que se genere lo que yo vaya hablando y que sea súper abierto». Con 204
/// plantillas institucionales en el catálogo, el selector era el primer obstáculo de una pantalla
/// que quiere ser un botón.
///
/// LO QUE SE PUEDE Y LO QUE NO, y conviene no confundirlo:
///
///   · **Lo que NO se puede desde aquí**: que no haya plantilla. El backend EXIGE `template_id`
///     al crear el encounter y organiza la nota contra el `template_snapshot` congelado. «Ninguna»
///     no es una opción del contrato.
///   · **Lo que ya existe pero no para esto**: la plantilla verdaderamente dinámica —que la IA
///     DISEÑE las secciones de cada caso— está implementada en Graph, en
///     `BiopsyExtractionService` (`SYSTEM_DYNAMIC`: «aquí TÚ DISEÑAS la estructura del informe a
///     partir de lo que realmente contiene la hoja»). Está atada a `/api/v1/biopsy/extract`, para
///     hojas de laboratorio fotografiadas. Traerla a las consultas es trabajo del repo Graph y
///     queda dicho como tal, no simulado aquí.
///   · **Lo que sí se hace hoy**: una plantilla PROPIA del médico, con pocas secciones muy anchas
///     cuyas INSTRUCCIONES le piden al organizador que estructure libremente lo que se dijo. Se
///     elige sola y no se pregunta nunca.
///
/// SI NO EXISTE, SE CREA — no se cae a una cualquiera del catálogo. Caer a «Consulta inicial
/// adulto» porque estaba de primera sería elegir por el médico sin decírselo, y su nota saldría con
/// la forma de otra cosa. Es la promesa 94.
/// </remarks>
public static class PlantillaAbierta
{
    /// <summary>
    /// Cómo se llama. Lleva la marca para que el médico la reconozca entre las suyas en el portal y
    /// sepa que la creó esta app, no él.
    /// </summary>
    public static string Nombre => "Nota abierta (Ü)";

    /// <summary>La especialidad con la que nace. Genérica a propósito: la nota no la usa para nada
    /// más que agruparse en el catálogo.</summary>
    public static string Especialidad => "medicina_general";

    /// <summary>
    /// La abierta dentro de un catálogo, o <c>null</c> si todavía no existe.
    /// </summary>
    public static PlantillaClinica? Elegir(IReadOnlyList<PlantillaClinica> catalogo)
    {
        foreach (var p in catalogo)
        {
            if (string.Equals(p.Nombre.Trim(), Nombre, StringComparison.OrdinalIgnoreCase))
                return p;
        }
        return null;
    }

    /// <summary>
    /// Las secciones con las que se crea. Tres, anchas, y cada instrucción le pide al organizador
    /// que ESTRUCTURE lo que oyó en vez de rellenar casillas fijas.
    /// </summary>
    /// <remarks>
    /// Tres y no una: con una sola sección la nota sale como un bloque de texto y deja de servir
    /// para lo que viene después —los recuerdos guían campo a campo y los batches escriben campo a
    /// campo—, así que hace falta que la nota siga teniendo partes nombradas. Tres y no doce porque
    /// el médico pidió abierto: cuantas más casillas fijas, más se parece esto a la plantilla que
    /// no quería.
    ///
    /// El contrato del backend exige entre 2 y 30, y normaliza `key` y `order` por su cuenta.
    /// </remarks>
    public static IReadOnlyList<object> Secciones() => new object[]
    {
        new
        {
            label = "Nota",
            order = 1,
            instruction =
                "Organiza aquí TODO lo que se dijo en la consulta, con la estructura que mejor "
                + "represente esta conversación en concreto. No fuerces un esquema fijo: si es una "
                + "consulta de control, escríbela como un control; si es una urgencia, como una "
                + "urgencia. Usa párrafos cortos o viñetas con su rótulo cuando ayuden a leerla. "
                + "No inventes ni completes datos clínicos que no se hayan dicho.",
        },
        new
        {
            label = "Hallazgos y datos objetivos",
            order = 2,
            instruction =
                "Signos vitales, medidas, resultados y hallazgos del examen que se hayan MENCIONADO, "
                + "cada uno con su valor tal como se dijo. Si no se mencionó ninguno, deja la "
                + "sección vacía en vez de rellenarla con frases de cortesía.",
        },
        new
        {
            label = "Plan y recomendaciones",
            order = 3,
            instruction =
                "Lo acordado: medicamentos con su dosis, estudios pedidos, recomendaciones y "
                + "controles, tal como se dijeron. Si no se acordó nada, deja la sección vacía.",
        },
    };
}
