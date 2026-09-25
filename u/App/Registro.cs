using System.IO;
namespace U.Nuevo;

/// <summary>
/// EL LOG ES LA FUENTE DE VERDAD, también aquí: %LOCALAPPDATA%\U-nuevo\logs\u-AAAAMMDD.log. Aparte del de
/// main a propósito: las dos apps pueden correr a la vez y sus líneas no se mezclan.
/// </summary>
public static class Registro
{
    private static readonly object Candado = new();
    public static string Carpeta { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "U-nuevo", "logs");
    public static event Action<string>? Linea;

    public static void Log(string texto)
    {
        string l = $"[{DateTime.Now:HH:mm:ss.fff}] {texto}";
        lock (Candado)
        {
            try
            {
                Directory.CreateDirectory(Carpeta);
                File.AppendAllText(Path.Combine(Carpeta, $"u-{DateTime.Now:yyyyMMdd}.log"), l + Environment.NewLine);
            }
            catch { /* sin disco no se deja de trabajar */ }
        }
        try { Linea?.Invoke(texto); } catch { }
    }
}
