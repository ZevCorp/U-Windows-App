namespace U.WindowsClient.Clinical;

/// <summary>
/// QUÉ NOTA SE PUEDE CORREGIR Y CUÁL YA NO. Promesa 192 (spec 015).
/// </summary>
/// <remarks>
/// UNA NOTA FIRMADA ES UN DOCUMENTO CLÍNICO-LEGAL, y esto no es una opinión de esta app: la base lo
/// impone con un trigger que revienta el UPDATE
/// (`20260721000000_consultation_immutability_and_addenda.sql`):
///
///     CONSULTATION_IMMUTABLE: la nota firmada no admite cambios; usa una adenda
///
/// PERO EL TRIGGER SOLO PROTEGE LA MITAD DEL CAMINO, y esa es la razón de que esta regla exista.
/// Corregir desde Windows toca DOS sitios: el backend clínico con `PUT /note` y el espejo de
/// `consultations`. Si se dejara editar una nota firmada, el PUT la cambiaría —Graph no sabe de la
/// firma del portal— y el espejo lo rechazaría el trigger. Resultado: la MISMA consulta distinta
/// según por dónde se mire, sin un solo error a la vista del médico. Eso es exactamente el
/// aprendizaje nº10 — lo peor no es que falle, es que parezca que funcionó.
///
/// SE PREGUNTA ANTES DE PINTAR EL EDITOR, no al guardar. Un editor que se abre, se rellena y al
/// guardar dice que no, ya perdió el trabajo de quien escribió.
///
/// LO QUE SIGUE PUDIÉNDOSE HACER con una nota firmada: verla entera y **mandarla a SAP** con el ✓
/// (spec 008). Firmar cierra la corrección, no el uso — y en este hospital exportar a la historia
/// clínica es justo lo que se hace DESPUÉS de firmar.
///
/// LA VUELTA CORRECTA para corregir lo firmado son las ADENDAS (tabla `consultation_addenda`,
/// append-only). No están en esta spec y se dice por su nombre en vez de dejar el hueco mudo.
/// </remarks>
public static class ReglaDeLaEdicion
{
    /// <summary>
    /// ¿Se puede corregir el texto de esta consulta?
    /// </summary>
    /// <param name="estado">
    /// El estado del portal: <c>borrador</c>, <c>revisada</c>, <c>aprobada</c>, <c>exportada</c>.
    /// Vacío es una consulta recién generada en esta ventana, que todavía no ha llegado a la lista.
    /// </param>
    public static bool SePuedeEditar(string? estado)
    {
        string e = (estado ?? "").Trim().ToLowerInvariant();

        // UNA NOTA RECIÉN GENERADA SE EDITA. Nace como borrador —lo escribe el espejo— así que el
        // vacío es esa misma consulta antes de que nadie la haya releído de la base.
        if (e.Length == 0) return true;

        // LISTA BLANCA Y NO LISTA NEGRA, a propósito. Con una lista negra («todo menos aprobada y
        // exportada»), un estado nuevo que el portal añadiera mañana nacería EDITABLE en Windows sin
        // que nadie lo decidiera. Al revés, nace bloqueado: se corrige en el portal y se añade aquí
        // cuando se sepa qué significa.
        return e is "borrador" or "revisada";
    }

    /// <summary>Por qué no se puede, para decirlo en la pantalla en vez de dejar el editor mudo.</summary>
    public static string PorQueNo(string? estado)
    {
        string e = (estado ?? "").Trim().ToLowerInvariant();
        return e switch
        {
            "aprobada" => "Esta nota ya está firmada: no se puede cambiar. Puedes seguir mandándola a SAP.",
            "exportada" => "Esta nota ya se exportó a la historia clínica: no se puede cambiar.",
            _ => $"Esta consulta está en «{e}» y no se edita desde aquí.",
        };
    }
}
