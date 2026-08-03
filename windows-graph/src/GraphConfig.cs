using System.IO;
using System.Reflection;
using System.Security.Cryptography;
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

    /// <summary>
    /// API key de la flota. Viaja como X-API-Key en las rutas /api/v1 y, desde el
    /// enrolamiento por instalación, es también la key de ENROLAMIENTO: lo que
    /// permite pedir el token per-install en /api/v1/enroll. El plan de corte
    /// (autenticacion-interna-plan.md en Graph) la degradará a solo-enrolamiento
    /// cuando la flota esté enrolada.
    /// </summary>
    public string? ApiKey { get; set; }

    // ── Identidad por instalación (carril clínico de Operations) ─────────────
    //
    // El token per-install es la credencial REAL de este equipo: revocable uno a
    // uno desde Graph, y la llave del carril clínico (actuar en nombre del médico
    // vinculado). En disco vive PROTEGIDO con DPAPI (CurrentUser); solo si DPAPI
    // no está disponible se cae a texto plano, y queda dicho en el log de quien
    // lo use. Graph solo guarda el sha256: si esto se pierde, se re-enrola.

    /// <summary>Identificador estable de ESTA instalación. Se crea una vez y se persiste.</summary>
    public string? DeviceId { get; set; }

    /// <summary>Token per-install cifrado con DPAPI (base64). Preferente.</summary>
    public string? DeviceTokenProtected { get; set; }

    /// <summary>Respaldo SOLO para cuando DPAPI falla en esta máquina. Peor es no poder operar.</summary>
    public string? DeviceTokenPlain { get; set; }

    /// <summary>Devuelve (creándolo y persistiéndolo si hace falta) el id estable de la instalación.</summary>
    public string EnsureDeviceId()
    {
        if (string.IsNullOrWhiteSpace(DeviceId))
        {
            // Un GUID persistido es estable por instalación y no filtra nada de la
            // máquina (nombre de host, usuario) hacia el backend.
            DeviceId = $"uwd-{Guid.NewGuid():N}";
            Save();
        }
        return DeviceId;
    }

    /// <summary>El token per-install, o null si este equipo aún no se enroló. Env gana (pruebas).</summary>
    public string? GetDeviceToken()
    {
        string? fromEnv = Environment.GetEnvironmentVariable("GRAPH_DEVICE_TOKEN");
        if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv.Trim();

        if (!string.IsNullOrWhiteSpace(DeviceTokenProtected))
        {
            try
            {
                byte[] raw = ProtectedData.Unprotect(
                    Convert.FromBase64String(DeviceTokenProtected), null, DataProtectionScope.CurrentUser);
                return System.Text.Encoding.UTF8.GetString(raw);
            }
            catch
            {
                // Cambió el perfil de usuario o el blob se corrompió: el token es
                // irrecuperable POR DISEÑO (DPAPI). Se re-enrola; no se adivina.
            }
        }
        return string.IsNullOrWhiteSpace(DeviceTokenPlain) ? null : DeviceTokenPlain.Trim();
    }

    /// <summary>Guarda el token recién emitido (el enroll lo enseña UNA vez). DPAPI, con respaldo plano.</summary>
    public void SetDeviceToken(string token)
    {
        try
        {
            DeviceTokenProtected = Convert.ToBase64String(ProtectedData.Protect(
                System.Text.Encoding.UTF8.GetBytes(token ?? ""), null, DataProtectionScope.CurrentUser));
            DeviceTokenPlain = null;
        }
        catch
        {
            DeviceTokenProtected = null;
            DeviceTokenPlain = token;
        }
        Save();
    }

    /// <summary>Identifica esta instalación en los workflows que graba. Útil con varios clientes.</summary>
    public string AppId { get; set; } = "windows-u";

    /// <summary>Pausa entre pasos al ejecutar un plan. La UI de destino necesita respirar.</summary>
    public int StepDelayMs { get; set; } = 250;

    /// <summary>
    /// Nombres técnicos ABAP EXTRA que identifican al paciente en las pantallas de este hospital
    /// (p. ej. <c>["PATNR", "FALNR"]</c>, sin el prefijo de estructura: de <c>RNPA1-PASSNR</c> va
    /// <c>PASSNR</c>). Se suman a los que <see cref="NoteExport.PatientGuard"/> ya conoce.
    ///
    /// Existe porque los dynpros de IS-H cambian entre instalaciones y quien sabe cuáles son es el
    /// implantador, no este código. Ampliar la lista NUNCA produce un «verificado» falso: solo hace
    /// que la compuerta encuentre más pistas que enseñarle a quien aprueba.
    /// </summary>
    public List<string> PatientFieldNames { get; set; } = new();

    /// <summary>
    /// Política de verificación del paciente para el ejecutor de exportaciones:
    /// <c>OperatorConfirms</c> (por defecto — una persona confirma) u <c>Off</c> (solo entornos de
    /// prueba con datos ficticios). Se lee como texto para que un valor desconocido en el archivo
    /// caiga en el DEFAULT SEGURO en vez de romper el arranque.
    /// </summary>
    public string PatientPolicy { get; set; } = "OperatorConfirms";

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
