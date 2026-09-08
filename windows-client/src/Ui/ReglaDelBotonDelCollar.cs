namespace U.WindowsClient.Ui;

/// <summary>Lo que hace el botón físico del collar al pulsarse.</summary>
public enum QueHaceElBotonDelCollar
{
    /// <summary>No había consulta: se abre y se empieza a grabar.</summary>
    AbrirLaConsultaYGrabar,

    /// <summary>La consulta está abierta y parada: se empieza a grabar.</summary>
    Grabar,

    /// <summary>Se estaba grabando: se para, igual que el botón «Grabar» de la pantalla.</summary>
    Parar,
}

/// <summary>
/// EL BOTÓN DEL COLLAR GRABA LA CONSULTA. Promesa 184 (spec 014).
/// </summary>
/// <remarks>
/// Hasta el 2026-09-07 el botón iba al mismo sitio que el clic en la carita: abría la voz en vivo.
/// El dueño lo pidió al revés ese día —«que se empiece a grabar el botón grabar en vez de que la
/// carita escuche»—, y la razón es de uso: quien lleva el collar puesto está con un paciente
/// delante, y lo que quiere del único botón que tiene es la consulta, no una conversación con Ü.
///
/// Vive separada de la carita por lo mismo que <see cref="ReglaDelToque"/>: el contrato no puede
/// pulsar un collar, pero sí puede preguntar qué haría la carita si lo pulsaran. Y a propósito NO
/// HAY una respuesta que abra la voz: si la hubiera, el comportamiento viejo tendría un camino de
/// vuelta que el contrato no vería.
/// </remarks>
public static class ReglaDelBotonDelCollar
{
    /// <param name="hayConsulta">Existe una ventana de consulta, esté donde esté.</param>
    /// <param name="grabando">…y está grabando ahora mismo.</param>
    public static QueHaceElBotonDelCollar AlPulsar(bool hayConsulta, bool grabando) =>
        !hayConsulta ? QueHaceElBotonDelCollar.AbrirLaConsultaYGrabar
        : grabando   ? QueHaceElBotonDelCollar.Parar
                     : QueHaceElBotonDelCollar.Grabar;
}
