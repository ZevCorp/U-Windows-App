using System.IO;
using System.Text.Json;

namespace U.WindowsClient;

/// <summary>
/// Configuración del cliente. Lo ÚNICO que necesita saber: dónde está el backend y (opcional) el token
/// para hablarle. Ninguna key de modelo, ningún prompt, ningún parámetro del cerebro vive aquí — todo
/// eso es del servidor, incluida la key de Gemini que usa la enseñanza por video (🎓): el backend
/// firma las subidas y hace las llamadas al modelo, así el usuario no configura nada y no hay ninguna
/// key que extraer del .exe. Se persiste en %APPDATA%\U\config.json.
/// </summary>
public sealed class Config
{
    /// <summary>
    /// URL del backend viejo (u-windows-backend). Se conserva como constante porque es la vía de
    /// emergencia documentada: `set U_BACKEND_URL=https://u-windows-backend.vercel.app` y el cliente
    /// vuelve al backend viejo (rutas /api/* + Bearer ClientToken) sin recompilar nada.
    /// </summary>
    public const string LegacyBackendUrl = "https://u-windows-backend.vercel.app";

    /// <summary>
    /// El cerebro ya no es el backend dedicado de Windows: es Graph, el backend central, que expone
    /// las mismas rutas bajo /api/v1 (ver <see cref="Backend.BackendClient"/>). La auth también
    /// cambia: X-API-Key de Graph (%APPDATA%\U\graph.json o env GRAPH_API_KEY) en vez del Bearer.
    /// Emergencia: la variable de entorno U_BACKEND_URL pisa este valor (ver <see cref="Load"/>).
    /// </summary>
    public string BackendUrl { get; set; } = "https://graph-eight-pied.vercel.app";

    /// <summary>
    /// Token Bearer del backend VIEJO. Contra Graph no se usa (ahí manda la X-API-Key); solo viaja
    /// cuando U_BACKEND_URL apunta de vuelta a u-windows-backend, para que la vuelta atrás funcione
    /// sin configurar nada más.
    /// </summary>
    public string? ClientToken { get; set; } = "e86d4ec981bba9889aaf69d5ac37a781db288bb2f0d22b0c";

    public string UserId { get; set; } = "anon";

    /// <summary>
    /// Identidad del usuario capturada al instalar (popup de nombre+correo, ver
    /// <see cref="Ui.OnboardingWindow"/>). El CORREO es la clave canónica en el backend
    /// ("Windows Live"): mismo correo = mismo usuario. <see cref="UserId"/> se fija al correo
    /// para que el scoping de workflows y la telemetría hablen de la misma persona.
    /// </summary>
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";

    /// <summary>
    /// Identificador estable de ESTA instalación (GUID, se genera una sola vez). Un usuario (correo)
    /// puede tener varias instalaciones; esto las distingue sin romper la identidad por correo.
    /// </summary>
    public string InstallId { get; set; } = "";

    /// <summary>
    /// Código de la consulta del portal (puente clínico). Se persiste para que el operador lo teclee
    /// UNA VEZ por instalación y no en cada arranque: vivía solo en memoria y se perdía al cerrar Ü.
    ///
    /// Es un código de emparejamiento, no una credencial: quien lo tenga obtiene signos vitales, nunca
    /// la nota ni el paciente. Por eso puede vivir en el config.json junto al resto de preferencias.
    /// </summary>
    public string ClinicalCode { get; set; } = "";

    /// <summary>¿Ya se capturó nombre+correo? Evita re-preguntar en cada arranque.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Onboarded => !string.IsNullOrWhiteSpace(Email);

    /// <summary>
    /// Asistente mudo (botón 🔇 de la carita). Se persiste a propósito: quien lo silencia suele estar
    /// en una consulta o una reunión, y que volviera a hablar solo por reiniciar Ü sería justo el
    /// problema que el botón viene a resolver.
    /// </summary>
    public bool Muted { get; set; }

    /// <summary>
    /// Al enseñar, ¿mandar el video al LLM para resumirlo (contexto del workflow)? Ese paso es el que da
    /// timeout (504) en Vercel. Con esto en false, la enseñanza NO procesa el video con IA —el video
    /// igual se graba a disco y se ve en 🎞 Videos, y el workflow se guarda con sus pasos—. Default true.
    /// </summary>
    public bool ProcessTeachVideo { get; set; } = true;

    /// <summary>
    /// Tema de la carita (claro/oscuro), que se alterna manteniéndola oprimida. Se persiste para que
    /// arranque en el modo que el usuario dejó. Valores: "Light" o "Dark" (ver <see cref="Ui.FaceTheme"/>).
    /// </summary>
    public string FaceTheme { get; set; } = "Light";

    /// <summary>
    /// Dónde dejó el usuario la barra. Se persiste porque el operador la aparta de la barra de
    /// herramientas de SAP una vez, y sin esto tendría que volver a apartarla en cada arranque.
    ///
    /// <c>double?</c> y no <c>double</c>: <c>null</c> significa «nunca la movió» y hay que poder
    /// distinguirlo de <c>0,0</c>, que es la esquina superior izquierda y una posición legítima.
    /// </summary>
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }

    /// <summary>
    /// El área de trabajo en la que se guardó esa posición. Sin esto, un portátil que pasa de su
    /// pantalla a un proyector de menos resolución restaura la carita fuera de la pantalla, donde no
    /// se puede ni agarrar. Si no coincide, se descarta la posición y se vuelve al sitio por defecto.
    /// </summary>
    public double? SavedWorkAreaWidth { get; set; }
    public double? SavedWorkAreaHeight { get; set; }

    /// <summary>
    /// De dónde baja la carita sus propias actualizaciones (ver <see cref="Update.Updater"/> y
    /// RELEASING-WINDOWS.md). Es el bucket PÚBLICO `windows` de Supabase — público a propósito: el
    /// updater tiene que poder leerlo sin credenciales, igual que el bucket `apks` de Android. Aquí solo
    /// viajan binarios del cliente, que no contienen secretos del cerebro.
    /// </summary>
    public string UpdateFeedUrl { get; set; } =
        "https://zyvfamlhlmztliexvmej.supabase.co/storage/v1/object/public/windows";

    private static string Path =>
        System.IO.Path.Combine(U.Graph.UserPaths.Roaming, "U", "config.json");

    public static Config Load()
    {
        Config cfg;
        try
        {
            cfg = File.Exists(Path)
                ? JsonSerializer.Deserialize<Config>(File.ReadAllText(Path)) ?? new Config()
                : new Config();
        }
        catch
        {
            cfg = new Config();
        }

        // Migración silenciosa a Graph: los config.json guardados antes del cambio traen el backend
        // viejo persistido, y sin esto ninguna instalación existente se movería sola. Solo se migra
        // si es EXACTAMENTE el default viejo: una URL puesta a mano en el panel Backend se respeta.
        if (string.Equals(cfg.BackendUrl?.TrimEnd('/'), LegacyBackendUrl, StringComparison.OrdinalIgnoreCase))
            cfg.BackendUrl = "https://graph-eight-pied.vercel.app";

        // Vía de emergencia: si Graph se cae o el port sale mal, `set U_BACKEND_URL=<url>` (p.ej. la
        // LegacyBackendUrl de arriba) manda sobre lo persistido y sobre la migración, sin tocar disco.
        string? fromEnv = Environment.GetEnvironmentVariable("U_BACKEND_URL");
        if (!string.IsNullOrWhiteSpace(fromEnv)) cfg.BackendUrl = fromEnv.Trim();

        return cfg;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
