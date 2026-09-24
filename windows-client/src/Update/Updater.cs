using U.WindowsClient.Diagnostics;
using Velopack;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace U.WindowsClient.Update;

/// <summary>
/// Auto-actualización del cliente. Es la contraparte de `RELEASING-WINDOWS.md`: nosotros publicamos con
/// `vpk pack` + subir al bucket, y esto lo recoge sin que el usuario haga nada.
///
/// Por qué existe: el cerebro (backend en Vercel) se actualiza solo con un push, pero la carita vive
/// como .exe en la máquina del cliente. Sin esto, cada cambio del cliente exige mandarle un zip y que
/// lo reemplace a mano — insostenible mientras estemos iterando.
///
/// Cómo funciona: sondea el feed al arrancar y cada <see cref="PollInterval"/>, descarga en segundo
    /// plano y avisa por <see cref="UpdateReady"/>. La carita muestra entonces una pastilla; si el usuario
/// la toca, reinicia ya (<see cref="ApplyAndRestart"/>); si la ignora, <see cref="ApplyOnExit"/> deja
/// la versión nueva instalada al cerrar. Nunca interrumpe lo que el usuario esté haciendo.
///
/// NO hace nada en desarrollo (`dotnet run`): sin instalación de Velopack detrás, <c>IsInstalled</c> es
/// false y esta clase se apaga entera. Ver <see cref="Enabled"/>.
/// </summary>
public sealed class Updater
{
    public sealed record ReleaseMessage(string Version, string Message, string? Locale, DateTimeOffset? PublishedAt)
    {
        public string Speech => string.IsNullOrWhiteSpace(Message)
            ? $"Esta versión trae mejoras para que Ü sea más útil y confiable."
            : Message.Trim();
    }

    public sealed record UpdateReadyInfo(string Version, ReleaseMessage Message);
    /// <summary>Cada cuánto se vuelve a mirar el feed. Igual que Android (RELEASING.md): ~30 min.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(30);

    private readonly UpdateManager _mgr;
    private readonly string _feedUrl;
    private VelopackAsset? _ready;
    private ReleaseMessage? _readyMessage;

    /// <summary>Se dispara con la versión y el mensaje humano cuando el paquete ya está descargado.</summary>
    public event Action<UpdateReadyInfo>? UpdateReady;

    public UpdateReadyInfo? ReadyInfo => _ready == null
        ? null
        : new UpdateReadyInfo(_ready.Version.ToString(), _readyMessage ?? new ReleaseMessage(_ready.Version.ToString(), "", null, null));

    /// <param name="feedUrl">
    /// De dónde se leen las versiones. Si apunta a un repositorio de GitHub se usan sus *releases*;
    /// cualquier otra cosa se trata como una carpeta estática. Ver <see cref="Config.UpdateFeedUrl"/>.
    /// </param>
    public Updater(string feedUrl)
    {
        _feedUrl = feedUrl.TrimEnd('/');
        // Sin canal explícito a propósito: Velopack usa el mismo con el que se empaquetó ("win"), y
        // pasarle uno distinto haría que pidiera un releases.<canal>.json que no existe → 404.
        //
        // EL FEED SE MUDÓ A GITHUB Y POR ESO HAY DOS CAMINOS. Vivía en un bucket de Supabase, que es
        // una carpeta de archivos y no necesitaba nada más que la URL. Pero el plan gratuito corta
        // las subidas en 50 MB —tope global, por encima del ajuste del bucket— y el paquete pesa 80:
        // el .nupkg no llegó a subir NUNCA, así que durante meses el botón de actualizar solo podía
        // decir «ya estás al día» porque al otro lado no había nada (2026-08-16). Las releases de
        // GitHub admiten 2 GB por archivo.
        //
        // Se conserva el camino de carpeta estática, y no se sustituye: es el que sirve para
        // publicar en cualquier sitio sin credenciales, y el que usan las pruebas locales.
        _mgr = EsRepositorioDeGithub(feedUrl)
            ? new UpdateManager(new Velopack.Sources.GithubSource(feedUrl, TokenDeLectura(), prerelease: false))
            : new UpdateManager(feedUrl);
    }

    private static bool EsRepositorioDeGithub(string url) =>
        url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// El token de solo lectura embebido en el build de distribución (ver WindowsClient.csproj).
    ///
    /// Devuelve null si no hay: <c>GithubSource</c> lo acepta y entonces solo puede leer releases
    /// públicas. Es lo correcto para un build hecho en una máquina de desarrollo, donde no hay
    /// secreto que embeber — mejor que buscar actualizaciones falle por no estar autorizado, a que
    /// el build no compile por faltar algo que solo hace falta al distribuir.
    /// </summary>
    private static string? TokenDeLectura()
    {
        try
        {
            string? t = System.Reflection.Assembly.GetExecutingAssembly()
                .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
                .Cast<System.Reflection.AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "UpdateGithubToken")?.Value;
            return string.IsNullOrWhiteSpace(t) ? null : t;
        }
        catch { return null; }
    }

    /// <summary>False en desarrollo o si se corre la carpeta suelta sin instalar: ahí no hay nada que actualizar.</summary>
    public bool Enabled => _mgr.IsInstalled;

    /// <summary>Versión instalada, para mostrar en el panel (soporte: "¿qué versión tenés?").</summary>
    public string CurrentVersion => _mgr.CurrentVersion?.ToString() ?? "dev";

    /// <summary>Arranca el sondeo en segundo plano. No lanza: los fallos de red son normales y se loguean.</summary>
    public void Start()
    {
        if (!Enabled)
        {
            LogBus.Publico("update", "auto-update desactivado (no es una instalación Velopack; normal en dotnet run)");
            return;
        }
        _ = PollLoopAsync();
    }

    private async Task PollLoopAsync()
    {
        // Se corta en cuanto hay una lista: ya no hay nada que sondear hasta que se aplique.
        while (_ready == null)
        {
            try
            {
                await CheckOnceAsync();
            }
            catch (Exception ex)
            {
                // Quedarse sin internet, o el bucket caído, no es motivo para molestar al usuario:
                // la carita sigue funcionando con la versión que tiene.
                // Al panel el tipo; el mensaje, en el log local (spec 051, P5).
                LogBus.Publico("update", $"no se pudo comprobar actualizaciones: {ex.GetType().Name}");
                LogBus.Log("update", $"el motivo de no poder comprobar: {ex.Message}");
            }
            if (_ready != null) break;
            await Task.Delay(PollInterval);
        }
    }

    private async Task CheckOnceAsync()
    {
        UpdateInfo? info = await _mgr.CheckForUpdatesAsync();
        if (info == null) return; // null = estamos al día. No es error.

        string version = info.TargetFullRelease.Version.ToString();
        LogBus.Publico("update", $"versión nueva disponible: {version} — descargando…");
        await _mgr.DownloadUpdatesAsync(info);

        _ready = info.TargetFullRelease;
        _readyMessage = await LeerMensajeAsync(version);
        LogBus.Publico("update", $"versión {version} descargada y lista para aplicar");
        UpdateReady?.Invoke(new UpdateReadyInfo(version, _readyMessage));
    }

    /// <summary>Cómo salió un «buscar actualizaciones» pedido a mano.</summary>
    public enum Busqueda { NoAplica, AlDia, YaEstabaLista, Descargada, Fallo }

    /// <summary>
    /// Buscar AHORA, porque alguien lo pidió. Devuelve qué pasó, para poder decírselo.
    /// </summary>
    /// <remarks>
    /// EXISTE PORQUE EL SONDEO SOLO NO BASTA PARA UNA PERSONA. El bucle mira cada 30 minutos y no
    /// dice nada mientras tanto — que es lo correcto para no molestar, pero deja sin respuesta a
    /// quien acaba de enterarse de que hay versión nueva y quiere tenerla YA. Sin este camino, la
    /// única forma de forzarlo era cerrar y volver a abrir, que es justo lo que la
    /// auto-actualización venía a evitar (2026-08-15, pedido por el usuario).
    ///
    /// DEVUELVE UN VEREDICTO Y NO UN BOOLEANO. «No pasó nada» tiene tres causas que se arreglan en
    /// sitios distintos: estar al día, no ser una instalación de Velopack —correr desde la carpeta
    /// suelta o en desarrollo—, y que el feed no conteste. Un `false` para las tres obligaría a
    /// mirar el log para saber cuál fue, que es lo que este botón viene a ahorrar.
    /// </remarks>
    public async Task<(Busqueda Que, string Detalle)> BuscarAhoraAsync()
    {
        if (!Enabled)
            return (Busqueda.NoAplica,
                "esta copia no se instaló con el instalador, así que no hay de dónde actualizarse");

        if (_ready != null)
            return (Busqueda.YaEstabaLista, _ready.Version.ToString());

        try
        {
            UpdateInfo? info = await _mgr.CheckForUpdatesAsync();
            if (info == null)
            {
                LogBus.Log("update", "búsqueda a mano: ya está en la última versión");
                return (Busqueda.AlDia, CurrentVersion);
            }

            string version = info.TargetFullRelease.Version.ToString();
            LogBus.Log("update", $"búsqueda a mano: hay {version}, descargando…");
            await _mgr.DownloadUpdatesAsync(info);

            _ready = info.TargetFullRelease;
            _readyMessage = await LeerMensajeAsync(version);
            LogBus.Log("update", $"búsqueda a mano: {version} descargada y lista");
            UpdateReady?.Invoke(new UpdateReadyInfo(version, _readyMessage));
            return (Busqueda.Descargada, version);
        }
        catch (Exception e)
        {
            LogBus.Log("update", $"búsqueda a mano: falló — {e.Message}");
            return (Busqueda.Fallo, e.Message);
        }
    }

    /// <summary>Aplica ya y relanza la carita. Lo que hace la pastilla al tocarla.</summary>
    public void ApplyAndRestart()
    {
        if (_ready == null) return;
        LogBus.Publico("update", "aplicando actualización y reiniciando");
        _mgr.ApplyUpdatesAndRestart(_ready); // no retorna: mata el proceso
    }

    /// <summary>
    /// Si hay una versión descargada que el usuario nunca aplicó, la instala al cerrar la carita, sin
    /// ventanas ni relanzar. El siguiente arranque ya es la versión nueva. Llamar al salir.
    /// </summary>
    public void ApplyOnExit()
    {
        if (_ready == null) return;
        try
        {
            LogBus.Log("update", "aplicando actualización pendiente al salir");
            _mgr.WaitExitThenApplyUpdates(_ready, silent: true, restart: false);
        }
        catch (Exception ex)
        {
            // Fallar aquí solo significa que seguirá en la versión vieja y lo reintentará al arrancar.
            LogBus.Publico("update", $"no se pudo dejar la actualización aplicándose al salir: {ex.GetType().Name}");
            LogBus.Log("update", $"el motivo de no poder dejarla aplicándose: {ex.Message}");
        }
    }

    /// <summary>Mensaje humano firmado por el release, para que la app narre la intención del cambio.</summary>
    private async Task<ReleaseMessage> LeerMensajeAsync(string version)
    {
        string? url = UrlDelMensaje(version);
        if (url == null) return new ReleaseMessage(version, "", null, null);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            string? token = TokenDeLectura();
            if (!string.IsNullOrWhiteSpace(token))
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("U-Windows-App/1.0");
            using var response = await http.GetAsync(url);
            if (!response.IsSuccessStatusCode) return new ReleaseMessage(version, "", null, null);
            await using var stream = await response.Content.ReadAsStreamAsync();
            var data = await JsonSerializer.DeserializeAsync<ReleaseMessageDto>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return new ReleaseMessage(version, data?.Message ?? "", data?.Locale, data?.PublishedAt);
        }
        catch (Exception ex)
        {
            LogBus.Log("update", $"no se pudo leer el mensaje humano de {version}: {ex.Message}");
            return new ReleaseMessage(version, "", null, null);
        }
    }

    private string? UrlDelMensaje(string version)
    {
        if (EsRepositorioDeGithub(_feedUrl))
            return $"{_feedUrl}/releases/download/v{version}/release-message.json";
        if (Uri.TryCreate(_feedUrl + $"/release-message-{version}.json", UriKind.Absolute, out var uri))
            return uri.ToString();
        return null;
    }

    private sealed class ReleaseMessageDto
    {
        public string? Message { get; set; }
        public string? Locale { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
    }
}
