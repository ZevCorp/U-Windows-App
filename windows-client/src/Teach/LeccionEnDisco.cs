using System.IO;
using System.Text.Json;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Teach;

/// <summary>
/// DÓNDE VIVE UNA LECCIÓN: <c>%LOCALAPPDATA%\U\lecciones\&lt;id&gt;\</c> con <c>leccion.json</c>,
/// <c>cuadros\</c> y el mensaje que se le arma al piloto. Spec 013, fase 2.
/// </summary>
/// <remarks>
/// EN DISCO Y EN LOCAL a propósito: la lección es lo que el piloto lee y lo que el humano puede
/// revisar si el piloto entendió mal. Nada de esto viaja a Graph — decisión del dueño (2026-09-06):
/// la enseñanza se hace «aquí en nuestra aplicación local, directo a servidores».
/// </remarks>
public static class LeccionEnDisco
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string CarpetaRaiz => Path.Combine(U.Graph.UserPaths.Local, "U", "lecciones");

    /// <summary>La carpeta de una lección nueva, creada ya con su subcarpeta de cuadros.</summary>
    public static string NuevaCarpeta(string id)
    {
        string carpeta = Path.Combine(CarpetaRaiz, Sanear(id));
        Directory.CreateDirectory(Path.Combine(carpeta, "cuadros"));
        return carpeta;
    }

    public static string CarpetaDeCuadros(string carpeta) => Path.Combine(carpeta, "cuadros");

    /// <summary>Escribe la lección entera. Devuelve la ruta del json.</summary>
    public static string Guardar(Leccion leccion, string carpeta)
    {
        Directory.CreateDirectory(carpeta);
        string ruta = Path.Combine(carpeta, "leccion.json");
        File.WriteAllText(ruta, JsonSerializer.Serialize(leccion, Json));
        LogBus.Log("leccion", $"lección «{leccion.Id}» en disco: {leccion.Eventos.Count} evento(s), "
            + $"{leccion.Cuadros.Count} cuadro(s), {leccion.Frases.Count} frase(s), {leccion.DuracionMs / 1000} s → {ruta}");
        return ruta;
    }

    public static Leccion? Cargar(string carpeta)
    {
        try { return JsonSerializer.Deserialize<Leccion>(File.ReadAllText(Path.Combine(carpeta, "leccion.json"))); }
        catch (Exception ex) { LogBus.Log("leccion", $"no se pudo leer la lección de «{carpeta}»: {ex.Message}"); return null; }
    }

    /// <summary>La carpeta de la lección más reciente que tenga json, o null.</summary>
    public static string? Ultima()
    {
        try
        {
            if (!Directory.Exists(CarpetaRaiz)) return null;
            return Directory.GetDirectories(CarpetaRaiz)
                .Where(d => File.Exists(Path.Combine(d, "leccion.json")))
                .OrderByDescending(d => File.GetLastWriteTimeUtc(Path.Combine(d, "leccion.json")))
                .FirstOrDefault();
        }
        catch { return null; }
    }

    private static string Sanear(string id)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) id = id.Replace(c, '_');
        return string.IsNullOrWhiteSpace(id) ? "leccion" : id;
    }
}
