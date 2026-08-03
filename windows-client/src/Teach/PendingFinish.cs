using System.IO;
using System.Text.Json;
using U.Graph;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Teach;

/// <summary>
/// Grabaciones cuyo CIERRE quedó a medias, para poder completarlo después sin volver a grabar.
///
/// El caso real: en un flujo largo, el post-procesado de Graph —título, resumen y guía, escritos por un
/// LLM— se pasa del tiempo máximo de la función serverless y Vercel devuelve 504. Los pasos ya están
/// guardados (se envían uno a uno mientras se graba), así que lo único perdido es el resumen. Antes de
/// esto el id de sesión se descartaba al fallar y no había forma de reintentar: tocaba regrabar tres
/// minutos que no hacía falta regrabar.
///
/// Se persiste a DISCO y no en memoria a propósito: el 504 aparece justo al terminar de enseñar, que es
/// cuando el operador tiende a cerrar la aplicación.
/// </summary>
public static class PendingFinish
{
    private sealed record Entry(string SessionId, string WorkflowId, string SavedAt);

    private static string FilePath => Path.Combine(UserPaths.Local, "U", "pending-finish.json");

    public static void Save(string sessionId, string workflowId)
    {
        try
        {
            var list = Load();
            if (list.Any(e => e.SessionId == sessionId)) return;
            list.Add(new Entry(sessionId, workflowId, DateTime.Now.ToString("s")));

            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(list));
            LogBus.Log("teach", $"cierre pendiente guardado (sesión {sessionId}) — se reintentará solo");
        }
        catch (Exception e) { LogBus.Log("teach", $"no se pudo guardar el cierre pendiente: {e.Message}"); }
    }

    private static List<Entry> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new List<Entry>();
            return JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(FilePath)) ?? new List<Entry>();
        }
        catch { return new List<Entry>(); }
    }

    /// <summary>
    /// Reintenta los cierres pendientes. Devuelve cuántos se completaron. Los que vuelvan a fallar se
    /// conservan para el próximo arranque; los que fallen por algo NO transitorio —la sesión ya no
    /// existe, la key cambió— se descartan, o quedarían reintentándose para siempre.
    /// </summary>
    public static async Task<int> RetryAllAsync(GraphClient graph, CancellationToken ct)
    {
        var list = Load();
        if (list.Count == 0) return 0;

        LogBus.Log("teach", $"reintentando {list.Count} cierre(s) de grabación pendiente(s)…");
        var keep = new List<Entry>();
        int done = 0;

        foreach (Entry e in list)
        {
            try
            {
                FinishResponse r = await graph.FinishSessionAsync(e.SessionId, ct);
                done++;
                LogBus.Log("teach", $"✓ cierre completado (sesión {e.SessionId}): {r.Summary}");
            }
            catch (GraphException g) when (g.Transient)
            {
                keep.Add(e);
                LogBus.Log("teach", $"el cierre de {e.SessionId} sigue sin poder completarse (HTTP {g.StatusCode})");
            }
            catch (Exception g)
            {
                LogBus.Log("teach", $"cierre de {e.SessionId} descartado, no es reintentable: {g.Message}");
            }
        }

        try
        {
            if (keep.Count == 0) File.Delete(FilePath);
            else File.WriteAllText(FilePath, JsonSerializer.Serialize(keep));
        }
        catch { }

        return done;
    }
}
