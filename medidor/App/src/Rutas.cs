namespace Medidor.App;

/// <summary>
/// Dónde vive cada cosa del medidor. Separado de las carpetas de U.exe a propósito: son dos
/// programas, y compartir carpeta es compartir accidentes. `MEDIDOR_DATA_DIR` redirige todo
/// (mismo patrón que `U_DATA_DIR` en UserPaths) para correr una instancia de prueba junto a la real.
/// </summary>
internal static class Rutas
{
    private static string Base(string raiz)
    {
        var forzada = Environment.GetEnvironmentVariable("MEDIDOR_DATA_DIR");
        var dir = string.IsNullOrWhiteSpace(forzada) ? Path.Combine(raiz, "MedidorU") : forzada!;
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Config e identidad (%APPDATA%\MedidorU): sobrevive a actualizaciones.</summary>
    public static string Config => Base(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    /// <summary>Datos locales (%LOCALAPPDATA%\MedidorU): spool y logs.</summary>
    public static string Datos => Base(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    public static string ArchivoDeEstado => Path.Combine(Config, "estado.json");
    public static string ArchivoDeAjustes => Path.Combine(Config, "medidor.json");
    public static string ArchivoDeSecreto => Path.Combine(Config, "secreto.bin");
    public static string ArchivoDelSpool => Path.Combine(Datos, "spool.db");
    public static string CarpetaDeLogs { get { var d = Path.Combine(Datos, "logs"); Directory.CreateDirectory(d); return d; } }
}
