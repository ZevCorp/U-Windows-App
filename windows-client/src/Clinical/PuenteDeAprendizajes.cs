namespace U.WindowsClient.Clinical;

/// <summary>
/// EL PUENTE ENTRE EL PANEL DE APRENDIZAJES Y LAS MANOS. Promesa 225 (spec 016).
/// </summary>
/// <remarks>
/// MISMO PATRÓN Y MISMA RAZÓN QUE <see cref="PuenteASap"/>: la ventana de la consulta nace en
/// <c>OnStartup</c>, antes que la carita, y no puede recibir por constructor ni la superficie de
/// SAP ni el recorredor ni la puerta MCP. La carita cuelga aquí una función cuando está lista, y el
/// panel la llama al pulsar «Mostrar». Mismo proceso y mismo hilo de interfaz, que es el que el COM
/// de SAP exige (lección del 2026-08-14).
///
/// SE PASA LA RUTA DEL ARCHIVO y no la skill: el panel lista el catálogo, que anuncia archivos, y
/// así quien la ejecuta la vuelve a leer del disco — si el usuario acaba de renombrarla o el
/// repaso la dejó comprobada, corre lo que hay ahora y no lo que el panel pintó hace un minuto.
/// </remarks>
public static class PuenteDeAprendizajes
{
    /// <summary>
    /// Muestra un aprendizaje. Recibe la ruta de su archivo y un canal de progreso, y devuelve la
    /// CUENTA honesta: qué hizo, dónde paró si paró, y por qué.
    /// </summary>
    public static Func<string, IProgress<string>, CancellationToken, Task<string>>? Mostrar { get; set; }

    public static bool Disponible => Mostrar != null;
}
