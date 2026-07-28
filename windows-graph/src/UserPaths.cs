namespace U.Graph;

/// <summary>
/// Raíz de los datos del usuario (config, credencial, logs, step-shots, vídeos). Un solo sitio que
/// responde "¿dónde escribe esta instancia?", para que las seis piezas que guardan algo no puedan
/// contestar cosas distintas.
///
/// Con <c>U_DATA_DIR</c> puesta, TODO se muda bajo esa carpeta (<c>&lt;dir&gt;\roaming</c> y
/// <c>&lt;dir&gt;\local</c>). Sin ella, se comporta exactamente como antes: %APPDATA% y
/// %LOCALAPPDATA%. Es decir, la app distribuida no cambia de comportamiento en absoluto.
///
/// PARA QUÉ: desde 2026-07-28 hay una versión estable corriendo que no se cierra, y el desarrollo
/// ocurre en una SEGUNDA instancia viva al mismo tiempo (scripts/dev-paralelo.ps1). Sin esto, las
/// dos comparten config.json —la de desarrollo le reescribe la identidad y los ajustes a la
/// estable— y escriben en el mismo log diario, que es justo la evidencia que hace falta leer
/// limpia cuando algo falla.
///
/// POR QUÉ NO BASTA CON REDIRIGIR %APPDATA% EN EL PROCESO HIJO (verificado el 2026-07-28, y falló):
/// <c>Environment.GetFolderPath</c> NO lee las variables de entorno APPDATA/LOCALAPPDATA — llama a
/// la API de carpetas conocidas de Windows, que las resuelve del token del usuario. Se lanzó una
/// segunda app con las dos variables redirigidas y siguió escribiendo en el log de la primera
/// («[12:11:08] update: auto-update desactivado» apareció en el log de la estable). Por eso la
/// redirección tiene que vivir en el código y no en el lanzador.
/// </summary>
public static class UserPaths
{
    /// <summary>Equivalente a %APPDATA% (Roaming): config.json y graph.json.</summary>
    public static string Roaming => Resolve("roaming", Environment.SpecialFolder.ApplicationData);

    /// <summary>Equivalente a %LOCALAPPDATA%: logs, step-shots, vídeos y pendientes de cierre.</summary>
    public static string Local => Resolve("local", Environment.SpecialFolder.LocalApplicationData);

    private static string Resolve(string sub, Environment.SpecialFolder fallback)
    {
        string? root = Environment.GetEnvironmentVariable("U_DATA_DIR");
        if (string.IsNullOrWhiteSpace(root)) return Environment.GetFolderPath(fallback);

        // Vacío no es ausente: una variable puesta a "" tiene que caer al comportamiento normal, no
        // producir una ruta relativa que acabe escribiendo junto al .exe.
        string dir = System.IO.Path.Combine(root.Trim(), sub);
        try { System.IO.Directory.CreateDirectory(dir); }
        catch { return Environment.GetFolderPath(fallback); } // un U_DATA_DIR inválido no puede dejar la app sin logs
        return dir;
    }
}
