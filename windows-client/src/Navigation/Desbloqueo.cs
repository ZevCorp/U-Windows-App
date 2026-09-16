namespace U.WindowsClient.Navigation;

/// <summary>
/// EL DIÁLOGO QUE SE CRUZÓ, CON SUS BOTONES ENGANCHADOS. Promesa 236 (spec 021).
/// </summary>
/// <remarks>
/// EL 2026-09-14 map_unblock CERRÓ CHROME DOS VECES. El detector leyó la barra «Continúa por donde lo
/// dejaste» como diálogo con la opción «Cerrar» (la X de la barra), devolvió solo el NOMBRE, y el
/// desbloqueo construyó «uia:name=Cerrar;ct=Button» y lo buscó en toda la ventana: el primer «Cerrar»
/// de Chrome es el de la barra de título. Cerró la ventana, vio que «el diálogo ya no estaba» y dijo
/// «DESBLOQUEADO · resuelto» (19:42:29 y 19:43:46).
///
/// Un nombre no es un botón. El diálogo se entrega con la forma de pulsar CADA opción sobre el elemento
/// que se leyó, y quien desbloquea pulsa por ahí y por ningún otro sitio.
/// </remarks>
public static class Desbloqueo
{
    /// <param name="Titulo">Cómo se llama el diálogo.</param>
    /// <param name="Textos">Lo que explica.</param>
    /// <param name="Opciones">Entre qué se puede elegir, en el orden en que se leyeron.</param>
    /// <param name="Pulsar">Pulsa ESA opción sobre su propio botón; false si el botón no admite ningún patrón.</param>
    public sealed record Dialogo(string Titulo, IReadOnlyList<string> Textos, IReadOnlyList<string> Opciones, Func<string, bool> Pulsar);
}
