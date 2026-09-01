namespace U.WindowsClient.Clinical;

/// <summary>
/// Los códigos de error del backend clínico, dichos en castellano y de uno en uno.
/// </summary>
/// <remarks>
/// DOCE CÓDIGOS, DOCE FRASES, y es la promesa 87. El contrato del backend
/// (docs/backend-clinical-api-contract.md) define doce; un `catch` que contesta «no se pudo generar
/// la nota» para los doce es el aprendizaje nº2 —un mensaje que no distingue sus causas manda la
/// investigación al lugar equivocado— cometido donde más caro sale: el que investiga es alguien con
/// un paciente delante, y «falta configurar el proveedor» y «el texto está vacío» se arreglan de
/// formas opuestas.
///
/// LO QUE NO SE CONOCE SE NOMBRA. Un código nuevo del backend sale con su nombre dentro del mensaje
/// en vez de caer a un genérico: así el día que Graph añada uno se ve en el log del primer médico
/// que lo toque, en lugar de perderse dentro de «error interno» durante semanas.
/// </remarks>
public static class MensajesClinicos
{
    public static string Traducir(string codigo) => (codigo ?? "").Trim() switch
    {
        "TEMPLATE_NOT_FOUND" =>
            "Esa plantilla ya no existe o no está disponible para tu cuenta. Elige otra.",
        "TEMPLATE_INVALID" =>
            "La plantilla tiene un problema de forma (nombre, especialidad o secciones).",
        "ENCOUNTER_NOT_FOUND" =>
            "Esta consulta no existe o es de otro médico.",
        "ENCOUNTER_INVALID" =>
            "Esta consulta ya está firmada: no admite una transcripción nueva.",
        "TRANSCRIPT_REQUIRED" =>
            "No se oyó nada que transcribir. Comprueba el micrófono y vuelve a grabar.",
        "TRANSCRIPT_TOO_LONG" =>
            "La consulta superó el máximo de texto que admite el backend (200.000 caracteres).",
        "LLM_NOT_CONFIGURED" =>
            "El organizador de notas no está configurado en el servidor. Esto no se arregla desde "
            + "aquí: avisa al equipo.",
        "NOTE_GENERATION_FAILED" =>
            "El organizador falló al armar la nota. La transcripción está guardada: puedes reintentar.",
        "NOTE_JSON_INVALID" =>
            "La nota editada no encaja con las secciones de su plantilla.",
        "UNAUTHORIZED" =>
            "Tu cuenta no tiene permiso para esta acción.",
        "SUPABASE_NOT_CONFIGURED" =>
            "El servidor no tiene acceso a la base de datos. Esto no se arregla desde aquí: avisa al equipo.",
        "INTERNAL_ERROR" =>
            "El servidor tuvo un error que no supo clasificar. Reintenta; si sigue, avisa al equipo.",
        "" => "El servidor rechazó la petición sin decir por qué.",
        var otro => $"El servidor contestó «{otro}», que esta versión de Ü todavía no conoce.",
    };
}

/// <summary>
/// Un fallo del backend clínico con su código intacto. El código se conserva porque quien lo recibe
/// decide con él: <c>NOTE_GENERATION_FAILED</c> se reintenta y <c>LLM_NOT_CONFIGURED</c> no.
/// </summary>
public sealed class ErrorClinico : Exception
{
    public ErrorClinico(string codigo, int http)
        : base(MensajesClinicos.Traducir(codigo))
    {
        Codigo = codigo;
        Http = http;
    }

    public string Codigo { get; }
    public int Http { get; }

    /// <summary>Merece la pena volver a intentarlo con el MISMO encounter.</summary>
    public bool SePuedeReintentar =>
        Codigo is "NOTE_GENERATION_FAILED" or "INTERNAL_ERROR" || Http >= 500;
}
