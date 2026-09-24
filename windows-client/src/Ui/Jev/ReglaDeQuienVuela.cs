namespace U.WindowsClient.Ui.Jev;

/// <summary>
/// UNA SOLA COSA VUELA POR CLIC. Promesa 384 (spec 049).
/// </summary>
/// <remarks>
/// La carita plegada sigue al cursor sintético y viaja a cada clic (promesa 240, <c>FaceWindow.xaml.cs</c>). Con
/// Jev en un tramo, la flecha vuela a lo pulsado (381); si la carita viajara también, serían dos cuerpos cruzando
/// la pantalla hacia el mismo sitio, y el ojo no sabe a cuál seguir. Es una regla de una línea y vive aquí para
/// que los dos <c>if</c> de <c>FaceWindow</c> —seguir al cursor y viajar al clic— no sean código suelto: se
/// juzga la regla, y las dos llamadas se cuentan en el fuente (fase 9).
/// </remarks>
public static class ReglaDeQuienVuela
{
    /// <summary>Si la carita viaja al clic y sigue al cursor sintético: fuera de un tramo con Jev, sí; dentro, vuela la flecha.</summary>
    /// <param name="enTramoConJev">Lo que dice <see cref="MaquinaDeLaVista.EnTramo"/>.</param>
    public static bool LaCaritaViaja(bool enTramoConJev) => !enTramoConJev;
}
