namespace U.Graph.NoteExport;

/// <summary>
/// Qué hacer con un trabajo reclamado. Los tres desenlaces del contrato más «adelante»: un tipo
/// cerrado en vez de banderas sueltas, para que ninguna rama del ejecutor pueda terminar sin decir
/// qué se reporta. Un trabajo reclamado que no reporta nada quema un intento y bloquea el lease
/// diez minutos, así que «no decidir» no es una opción representable.
/// </summary>
public enum ExportVerdict
{
    /// <summary>Se puede ejecutar contra SAP.</summary>
    Proceed,

    /// <summary>No se ejecuta: hace falta que una persona complete o decida algo. → outcome needs_doctor.</summary>
    NeedsDoctor,

    /// <summary>No se ejecuta por un problema técnico u operativo. → outcome error.</summary>
    Fail,
}

/// <summary>
/// La decisión, con lo que hay que reportar si no se procede.
///
/// <see cref="UnresolvedFields"/> son ETIQUETAS QUE LEE EL MÉDICO: Miracle Notes las pinta
/// literalmente dentro de la frase «Quedaron campos sin completar en la historia clínica: …».
/// Por eso van en español, en singular, capitalizadas y sin jerga técnica — nada de
/// <c>RNPA1-PASSNR</c> ni <c>patient_guard_failed</c>. Nunca llevan valores del paciente.
/// </summary>
public sealed record ExportDecision(
    ExportVerdict Verdict,
    string ErrorCode = "",
    string DetailCode = "",
    IReadOnlyList<string>? UnresolvedFields = null,
    string Explanation = "")
{
    public static ExportDecision Proceed() => new(ExportVerdict.Proceed);

    /// <summary>Hace falta una persona. <paramref name="fields"/> lo verá el médico tal cual.</summary>
    public static ExportDecision NeedsDoctor(string explanation, params string[] fields) =>
        new(ExportVerdict.NeedsDoctor, UnresolvedFields: fields, Explanation: explanation);

    /// <summary>Fallo técnico. <paramref name="errorCode"/> se le muestra al médico dentro de
    /// «La exportación falló (CÓDIGO). Puedes reintentarla.», así que debe ser legible y sin PHI.</summary>
    public static ExportDecision Fail(string errorCode, string explanation, string detailCode = "") =>
        new(ExportVerdict.Fail, ErrorCode: errorCode, DetailCode: detailCode, Explanation: explanation);
}
