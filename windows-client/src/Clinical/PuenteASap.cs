namespace U.WindowsClient.Clinical;

/// <summary>
/// EL PUENTE ENTRE LA VENTANA DE CONSULTA Y LAS MANOS. La consulta nace en <c>OnStartup</c>, antes
/// que la carita, y no puede recibir por constructor ni la superficie SAP ni el rellenador ni la
/// puerta MCP: todo eso vive en <c>FaceWindow</c>, que se crea después. Aquí la carita cuelga una
/// función cuando está lista, y la consulta la llama al pulsar ✓. Mismo proceso, mismo hilo de
/// interfaz: el COM de SAP exige STA y ese es el del Dispatcher (lección del 2026-08-14).
/// </summary>
public static class PuenteASap
{
    /// <summary>
    /// Lleva un encargo a la app. Recibe el encargo, un canal de progreso para pintar, y devuelve
    /// la CUENTA honesta de lo que pasó: qué se escribió, qué no, y por qué paró si paró.
    /// </summary>
    public static Func<Encargo, IProgress<string>, CancellationToken, Task<string>>? Enviar { get; set; }

    public static bool Disponible => Enviar != null;
}
