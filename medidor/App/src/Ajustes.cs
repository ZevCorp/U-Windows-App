using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Medidor.App;

/// <summary>
/// La config que el medidor obedece, y de dónde sale. El orden de prioridad para la API key es el
/// mismo que en GraphWorkflows (medidor.json > env > embebida) por la misma razón: un build de
/// distribución llega conectado sin que nadie teclee nada, pero se puede sobreescribir en la
/// máquina sin recompilar.
///
/// La CONFIG DE MEDICIÓN (allowlists de apps, reglas de identidad, cadencias) NO se hornea: llega
/// del servidor en el enrolamiento y se refresca. Así se cambia una regla de extracción para todo
/// un hospital sin tocar el binario.
/// </summary>
public sealed class Ajustes
{
    public string BaseUrl { get; set; } = "https://graph-eight-pied.vercel.app";
    public string? ApiKey { get; set; }

    public static string BaseUrlPorDefecto => "https://graph-eight-pied.vercel.app";

    public static Ajustes Cargar()
    {
        var a = new Ajustes();
        try
        {
            if (File.Exists(Rutas.ArchivoDeAjustes))
            {
                var disco = JsonSerializer.Deserialize<Ajustes>(File.ReadAllText(Rutas.ArchivoDeAjustes));
                if (disco != null) a = disco;
            }
        }
        catch (Exception e) { Registro.Excepcion("ajustes", e); }

        // env pisa al archivo solo si el archivo no trae key
        if (string.IsNullOrWhiteSpace(a.ApiKey))
            a.ApiKey = Environment.GetEnvironmentVariable("MEDIDOR_API_KEY");
        // la embebida es el último respaldo
        if (string.IsNullOrWhiteSpace(a.ApiKey))
            a.ApiKey = KeyEmbebida();

        return a;
    }

    private static string? KeyEmbebida()
    {
        var meta = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(m => m.Key == "MedidorDefaultApiKey");
        return string.IsNullOrWhiteSpace(meta?.Value) ? null : meta!.Value;
    }
}

/// <summary>La config de medición que llega del servidor. Versionada: el cliente solo pide lo
/// nuevo si su versión quedó vieja.</summary>
public sealed class ConfigDeMedicion
{
    [JsonPropertyName("config_version")] public int Version { get; set; }
    [JsonPropertyName("apps_por_proceso")] public Dictionary<string, string> AppsPorProceso { get; set; } = new();
    [JsonPropertyName("dominios_permitidos")] public List<string> DominiosPermitidos { get; set; } = new();
    [JsonPropertyName("dominios_miracle")] public List<string> DominiosMiracle { get; set; } = new();
    [JsonPropertyName("reglas_identidad")] public List<ReglaCruda> ReglasIdentidad { get; set; } = new();
    [JsonPropertyName("foreground_ms")] public int ForegroundMs { get; set; } = 800;
    [JsonPropertyName("sap_identity_ms")] public int SapIdentityMs { get; set; } = 1500;
    [JsonPropertyName("solo_foreground")] public bool SoloForeground { get; set; }

    public ConfigDeNormalizacion ParaNormalizar() => new(
        AppsPorProceso,
        new HashSet<string>(DominiosPermitidos, StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(DominiosMiracle, StringComparer.OrdinalIgnoreCase));

    public List<ReglaDeIdentidad> Reglas() => ReglasIdentidad
        .Select(r => new ReglaDeIdentidad(r.Id, r.Tcode, r.Fuente, r.Selector, r.Patron, r.Normalizar))
        .ToList();

    public sealed class ReglaCruda
    {
        public string Id { get; set; } = "";
        public string Tcode { get; set; } = "*";
        public string Fuente { get; set; } = "titulo_sap";
        public string? Selector { get; set; }
        public string Patron { get; set; } = "";
        public string Normalizar { get; set; } = "digitos_sin_ceros";
    }
}

/// <summary>La identidad de esta instalación: lo que el enrolamiento devolvió. El secreto HMAC NO
/// vive aquí — vive cifrado aparte (DPAPI), y nunca se serializa junto al resto.</summary>
public sealed class Identidad
{
    [JsonPropertyName("device_id")] public string DeviceId { get; set; } = "";
    [JsonPropertyName("organization_id")] public string OrganizationId { get; set; } = "";
    [JsonPropertyName("org_name")] public string OrgName { get; set; } = "";
    [JsonPropertyName("hmac_version")] public int HmacVersion { get; set; }
    [JsonPropertyName("config_version")] public int ConfigVersion { get; set; }
}
