using System.IO;
using System.Reflection;
using System.Text.Json;

namespace U.Graph;

/// <summary>
/// Lo único que el módulo necesita saber: dónde está Graph y con qué API key hablarle.
///
/// Se persiste en %APPDATA%\U\graph.json, SEPARADO del config.json del asistente: son dos backends
/// distintos con credenciales distintas, y mezclarlos ataría este módulo al resto de la app.
///
/// La key NO se hardcodea en el CÓDIGO (quedaría en el historial de Git para siempre). Prioridad de
/// resolución: %APPDATA%\U\graph.json  >  env GRAPH_API_KEY  >  key embebida en el BUILD de
/// distribución (AssemblyMetadata GraphDefaultApiKey, que el CI inyecta desde un secreto; VACÍA en los
/// builds del repo). Así el Setup.exe distribuido llega conectado sin intervención del usuario, pero
/// nada de eso vive en el código fuente. Ver GraphWorkflows.csproj.
/// </summary>
public sealed class GraphConfig
{
    /// <summary>
    /// Base del backend Graph. Sin barra final. El dominio viejo (graph-five-orpin) quedó degradado;
    /// el deploy sano vive en graph-eight-pied. Sigue siendo sobreescribible por %APPDATA%\U\graph.json.
    /// </summary>
    public string BaseUrl { get; set; } = "https://graph-eight-pied.vercel.app";

    /// <summary>API key permanente. Viaja como X-API-Key en todas las rutas /api/v1.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Identifica esta instalación en los workflows que graba. Útil con varios clientes.</summary>
    public string AppId { get; set; } = "windows-u";

    /// <summary>Pausa entre pasos al ejecutar un plan. La UI de destino necesita respirar.</summary>
    public int StepDelayMs { get; set; } = 250;

    private static string Path =>
        System.IO.Path.Combine(UserPaths.Roaming, "U", "graph.json");

    public static GraphConfig Load()
    {
        GraphConfig cfg;
        try
        {
            cfg = File.Exists(Path)
                ? JsonSerializer.Deserialize<GraphConfig>(File.ReadAllText(Path)) ?? new GraphConfig()
                : new GraphConfig();
        }
        catch
        {
            cfg = new GraphConfig();
        }

        // La variable de entorno gana sobre el disco: permite instalar sin escribir la key en disco.
        string? fromEnv = Environment.GetEnvironmentVariable("GRAPH_API_KEY");
        if (!string.IsNullOrWhiteSpace(fromEnv)) cfg.ApiKey = fromEnv.Trim();

        // Igual que GRAPH_API_KEY pero para la BASE: `set GRAPH_BASE_URL=http://localhost:3000` apunta
        // el módulo de workflows a un Graph local sin tocar %APPDATA% ni recompilar. Existe para poder
        // probar cliente y backend juntos en la misma máquina: el asistente ya tenía su equivalente
        // (U_BACKEND_URL) y este módulo no, así que la mitad de la app seguía yendo al Graph remoto.
        string? baseFromEnv = Environment.GetEnvironmentVariable("GRAPH_BASE_URL");
        if (!string.IsNullOrWhiteSpace(baseFromEnv)) cfg.BaseUrl = baseFromEnv.Trim().TrimEnd('/');

        // Último recurso: la key embebida en el build de distribución (el CI la inyecta; vacía en los
        // builds del repo). Es lo que hace que el exe distribuido funcione "de fábrica" sin que el
        // usuario ponga nada; graph.json o GRAPH_API_KEY la sobreescriben en la máquina.
        if (string.IsNullOrWhiteSpace(cfg.ApiKey) && !string.IsNullOrWhiteSpace(BakedDefaultApiKey))
            cfg.ApiKey = BakedDefaultApiKey;

        // Migración silenciosa: los graph.json guardados antes del cambio de dominio traen el deploy
        // degradado (graph-five-orpin) persistido, y sin esto ninguna instalación existente se
        // movería sola al deploy sano. Solo se toca el valor si es EXACTAMENTE el default viejo:
        // una URL puesta a mano (p.ej. un entorno de pruebas) se respeta.
        if (string.Equals(cfg.BaseUrl?.TrimEnd('/'), "https://graph-five-orpin.vercel.app",
                StringComparison.OrdinalIgnoreCase))
        {
            cfg.BaseUrl = "https://graph-eight-pied.vercel.app";
        }

        return cfg;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* si %APPDATA% no es escribible, se sigue con lo que haya en memoria */ }
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>Key embebida en el build de distribución (AssemblyMetadata). Vacía en los builds del repo.</summary>
    private static string BakedDefaultApiKey =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "GraphDefaultApiKey")?.Value?.Trim() ?? "";
}
