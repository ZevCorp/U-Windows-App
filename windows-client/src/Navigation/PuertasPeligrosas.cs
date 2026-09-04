namespace U.WindowsClient.Navigation;

/// <summary>
/// LAS PUERTAS QUE Ü NO CRUZA: las que no se pueden deshacer. Promesa 126 (spec 009).
/// </summary>
/// <remarks>
/// NACE DE DOS SUSTOS REALES. «Finalizar (Shift+F3)» cerró la sesión de SAP y dejó un batch colgado
/// 120 s (2026-08-30). Y la comprobación del aprendizaje recorre una tarea DE VERDAD sobre el SAP
/// del hospital: si la demo terminaba en Grabar, comprobar dejaría datos en la historia clínica de
/// un paciente por el simple hecho de estar aprendiendo.
///
/// LA REGLA ES POR ETIQUETA APLANADA, no por selector, y a propósito: el botón de grabar de SAP no
/// se llama igual en dos pantallas —«Grabar», «Guardar», «Finalizar   (Shift+F3)» con su atajo
/// pegado— pero lo que significa se lee en su nombre. Se aplana con <see cref="Nombres.Aplanar"/>,
/// la misma normalización que usa el batch para resolver puertas: una sola forma de comparar
/// nombres en todo el sistema (aprendizaje nº16).
///
/// SE COMPARA POR PALABRA CONTENIDA, no por igualdad: «Finalizar   (Shift+F3)» tiene que morder, y
/// exigir la igualdad exacta dejaría pasar cualquier variante con el atajo pegado — que es la forma
/// normal en que SAP los nombra.
///
/// LO QUE ESTA LISTA NO HACE: decidir por el humano. No prohíbe pulsar Grabar: prohíbe que lo pulse
/// Ü. El médico revisa y graba, y ese es el punto de control que la experiencia entera respeta.
/// </remarks>
public static class PuertasPeligrosas
{
    /// <summary>
    /// Lo que no se cruza. Verbos de consecuencia irreversible, en el idioma de la pantalla.
    /// </summary>
    /// <remarks>
    /// Cada una está por un motivo, no por precaución genérica: grabar y guardar escriben en la base
    /// del hospital; finalizar y salir matan la sesión y con ella el batch; borrar y eliminar no
    /// tienen vuelta; enviar y firmar comprometen a una persona ante otra.
    /// </remarks>
    private static readonly string[] Verbos =
    {
        "grabar", "guardar", "finalizar", "salir del sistema",
        "borrar", "eliminar", "enviar", "firmar",
    };

    public static bool EsPeligrosa(string etiqueta)
    {
        string plano = Nombres.Aplanar(etiqueta ?? "");
        if (plano.Length == 0) return false;
        foreach (string v in Verbos)
            if (plano.Contains(Nombres.Aplanar(v), StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>Cuál de los verbos mordió, para poder DECIRLO en vez de solo negarse.</summary>
    public static string PorQue(string etiqueta)
    {
        string plano = Nombres.Aplanar(etiqueta ?? "");
        foreach (string v in Verbos)
            if (plano.Contains(Nombres.Aplanar(v), StringComparison.Ordinal))
                return $"«{(etiqueta ?? "").Trim()}» no se puede deshacer: te la dejo a ti.";
        return "";
    }
}
