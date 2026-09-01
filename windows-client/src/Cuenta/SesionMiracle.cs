using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using U.Graph;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Cuenta;

/// <summary>
/// LA IDENTIDAD DEL MÉDICO. Es la misma cuenta de Supabase con la que entra al portal: mismos
/// usuarios, misma organización, mismas reglas de fila (RLS).
/// </summary>
/// <remarks>
/// POR QUÉ HACÍA FALTA. Hasta hoy el cliente Windows se identificaba como MÁQUINA: la
/// <c>X-API-Key</c> de Graph más un correo tecleado en el popup de bienvenida, sin contraseña. Eso
/// vale para los carriles que son de la máquina —ejecutar workflows, reclamar exportaciones— pero
/// las rutas clínicas (<c>/api/clinical/*</c>) exigen el JWT del médico: Graph las protege con
/// <c>requireClinicalAuth</c> y deriva de ahí el <c>doctor_id</c>. Sin esto no hay encounter, no hay
/// nota, y «la misma base de datos que la web» no pasa de ser una frase.
///
/// EL REFRESCO VA POR DELANTE, CON MARGEN, y esa es la promesa 85. Un refresco reactivo —esperar al
/// 401 y reintentar— convierte cada expiración en una llamada fallida a mitad de consulta, que es
/// justo cuando no se puede perder una frase. Aquí el reloj se INYECTA para poder juzgarlo sin
/// esperar cinco minutos: una prueba que duerme se vuelve caprichosa, y un juez caprichoso deja de
/// creerse.
///
/// LA CLAVE PUBLICABLE NO ES UN SECRETO. <c>sb_publishable_…</c> viaja en el paquete de JavaScript
/// que el portal manda a cualquier navegador; está diseñada para ser pública y lo único que abre es
/// lo que la RLS permita a quien presente un token válido. Que esté en el <c>.exe</c> no añade
/// riesgo — a diferencia de la key de Gemini, que sí es de alcance completo y está señalada como
/// deuda en el csproj.
/// </remarks>
public sealed class SesionMiracle
{
    /// <summary>
    /// Cuánto antes de que muera el token se pide uno nuevo. Un minuto porque las llamadas que
    /// importan —guardar la transcripción, generar la nota— tardan segundos, no minutos: con este
    /// margen ninguna arranca con un token que pueda morírsele por el camino.
    /// </summary>
    private static readonly TimeSpan Margen = TimeSpan.FromSeconds(60);

    private readonly string _urlSupabase;
    private readonly string _clavePublicable;
    private readonly HttpClient _http;
    private readonly Func<DateTimeOffset> _ahora;
    private readonly SemaphoreSlim _unoCadaVez = new(1, 1);

    private Credencial? _credencial;

    public SesionMiracle(string urlSupabase, string clavePublicable,
        HttpMessageHandler? transporte = null, Func<DateTimeOffset>? ahora = null)
    {
        _urlSupabase = urlSupabase.TrimEnd('/');
        _clavePublicable = clavePublicable;
        _ahora = ahora ?? (() => DateTimeOffset.UtcNow);
        _http = transporte == null
            ? new HttpClient { Timeout = TimeSpan.FromSeconds(20) }
            : new HttpClient(transporte, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(20) };
    }

    /// <summary>Dónde vive la credencial. Respeta <c>U_DATA_DIR</c>, como todo lo demás.</summary>
    public static string RutaDeLaCredencial =>
        Path.Combine(UserPaths.Roaming, "U", "sesion.dat");

    /// <summary>¿Hay un médico dentro? Es tener CREDENCIAL, no tener archivo.</summary>
    public bool HayMedico => _credencial != null;

    /// <summary>El uuid del médico: el <c>sub</c> del token, que es lo que Graph casa con `profiles`.</summary>
    public string MedicoId => _credencial?.UsuarioId ?? "";

    public string MedicoEmail => _credencial?.Email ?? "";

    /// <summary>Su nombre para saludarle. Sale de `profiles`, no de lo que teclee nadie.</summary>
    public string MedicoNombre => _credencial?.Nombre ?? "";

    /// <summary>Lo último que salió mal al entrar, ya en castellano. Vacío si no ha fallado nada.</summary>
    public string UltimoFallo { get; private set; } = "";

    /// <summary>Alguien entró o salió. La interfaz se pinta con esto.</summary>
    public event Action<bool>? Cambio;

    // ── entrar, seguir dentro, salir ─────────────────────────────────────────

    /// <summary>
    /// Entra con correo y contraseña contra Supabase Auth. Devuelve si se entró; el motivo, cuando
    /// no, queda en <see cref="UltimoFallo"/>.
    /// </summary>
    public async Task<bool> EntrarAsync(string email, string clave, CancellationToken ct = default)
    {
        UltimoFallo = "";
        try
        {
            string cuerpo = JsonSerializer.Serialize(new { email, password = clave });
            var respuesta = await PedirTokenAsync("password", cuerpo, ct);
            if (respuesta == null) return false;

            _credencial = respuesta;
            await CompletarNombreAsync(ct);
            Guardar();

            // NI EL TOKEN NI EL CORREO EN EL LOG. El correo es dato personal y el token es la
            // sesión entera; lo que hace falta para diagnosticar es que se entró y quién, y el
            // uuid ya dice quién sin decir nada más.
            LogBus.Log("cuenta", $"médico dentro · {_credencial.UsuarioId}");
            Cambio?.Invoke(true);
            return true;
        }
        catch (Exception e)
        {
            UltimoFallo = "No se pudo conectar con Miracle. Revisa la conexión e inténtalo de nuevo.";
            LogBus.Log("cuenta", $"entrar falló: {e.GetType().Name}: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// El token con el que hablar con el backend clínico, renovado si le queda poco. Cadena vacía si
    /// no hay médico dentro — y vacío significa AUSENTE, no «da igual»: quien llame tiene que mirarlo.
    /// </summary>
    public async Task<string> TokenVigenteAsync(CancellationToken ct = default)
    {
        var actual = _credencial;
        if (actual == null) return "";
        if (!actual.CaducaAntesDe(_ahora() + Margen)) return actual.AccessToken;

        await _unoCadaVez.WaitAsync(ct);
        try
        {
            // Otro pudo renovarlo mientras se esperaba el turno: dos renovaciones simultáneas
            // invalidarían la primera (Supabase rota el refresh token en cada uso).
            actual = _credencial;
            if (actual == null) return "";
            if (!actual.CaducaAntesDe(_ahora() + Margen)) return actual.AccessToken;

            string cuerpo = JsonSerializer.Serialize(new { refresh_token = actual.RefreshToken });
            var nueva = await PedirTokenAsync("refresh_token", cuerpo, ct);
            if (nueva == null)
            {
                // El refresh token ya no vale: la sesión se acabó de verdad. Se sale del todo en vez
                // de dejar una sesión zombi que contesta 401 en cada llamada sin decir por qué.
                LogBus.Log("cuenta", "el refresh token fue rechazado: se cierra la sesión");
                Salir();
                return "";
            }

            // El nombre no vuelve a pedirse: renovar es lo mismo de antes con otro token.
            _credencial = nueva with { Nombre = actual.Nombre };
            Guardar();
            LogBus.Log("cuenta", "token renovado antes de usarse");
            return _credencial.AccessToken;
        }
        catch (Exception e)
        {
            LogBus.Log("cuenta", $"no se pudo renovar el token: {e.Message}");
            return "";
        }
        finally { _unoCadaVez.Release(); }
    }

    /// <summary>
    /// Cierra la sesión y BORRA la credencial del disco.
    /// </summary>
    /// <remarks>
    /// Borrar y no solo olvidar, y es la promesa 86: en un ordenador de hospital el siguiente médico
    /// es otra persona aunque Windows crea que es el mismo usuario, así que un `Salir()` que solo
    /// pone una bandera deja el refresh token en disco — y con él se vuelve a entrar sin contraseña.
    /// </remarks>
    public void Salir()
    {
        _credencial = null;
        try { if (File.Exists(RutaDeLaCredencial)) File.Delete(RutaDeLaCredencial); }
        catch (Exception e) { LogBus.Log("cuenta", $"no se pudo borrar la credencial: {e.Message}"); }
        LogBus.Log("cuenta", "sesión cerrada");
        Cambio?.Invoke(false);
    }

    /// <summary>
    /// Recupera la sesión guardada al arrancar. Devuelve si había una utilizable.
    /// </summary>
    public bool Restaurar()
    {
        try
        {
            if (!File.Exists(RutaDeLaCredencial)) return false;
            byte[] claro = Dpapi.Revelar(File.ReadAllBytes(RutaDeLaCredencial));
            var guardada = JsonSerializer.Deserialize<Credencial>(Encoding.UTF8.GetString(claro));
            if (guardada == null || string.IsNullOrWhiteSpace(guardada.RefreshToken)) return false;

            // Se acepta aunque el access token esté vencido: para eso está el refresh token. Exigir
            // que estuviera fresco haría que cerrar el portátil una noche pidiera la contraseña otra
            // vez, que es lo contrario de lo que la persistencia viene a resolver.
            _credencial = guardada;
            LogBus.Log("cuenta", $"sesión restaurada · {guardada.UsuarioId}");
            Cambio?.Invoke(true);
            return true;
        }
        catch (Exception e)
        {
            // Una credencial que no se puede leer (otra máquina, archivo corrupto) no es un error
            // que mostrar: es no tener sesión. Se dice en el log y se pide entrar.
            LogBus.Log("cuenta", $"la credencial guardada no se pudo leer: {e.Message}");
            try { File.Delete(RutaDeLaCredencial); } catch { }
            return false;
        }
    }

    // ── atribución ───────────────────────────────────────────────────────────

    /// <summary>
    /// Las cabeceras con las que Graph sabe de quién es el consumo. Vacías si no hay médico.
    /// </summary>
    /// <remarks>
    /// EL UUID Y NO EL CORREO, y es la promesa 90. La web manda <c>X-Miracle-User-Id</c> con el
    /// <c>sub</c> del token y Graph lo valida contra `profiles` antes de atribuir nada
    /// (app/api/stt/session/route.ts:55). El cliente Windows mandaba <c>X-Miracle-User-Email</c>:
    /// los dos lados creían estar atribuyendo y uno no lo estaba. Es el aprendizaje nº16 —dos
    /// identidades de distinta forma comparadas en silencio— por quinta vez en este repo.
    ///
    /// Sin médico dentro se devuelve vacío en vez de caer al id de máquina: atribuirle a alguien un
    /// consumo que no hizo es peor que no atribuirlo.
    /// </remarks>
    public IReadOnlyDictionary<string, string> CabecerasDeAtribucion()
    {
        var cabeceras = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (_credencial == null) return cabeceras;
        cabeceras["X-Miracle-User-Id"] = _credencial.UsuarioId;
        cabeceras["X-Miracle-App"] = "windows_app";
        cabeceras["X-Miracle-Feature"] = "transcription";
        return cabeceras;
    }

    // ── el camino a Supabase ─────────────────────────────────────────────────

    private async Task<Credencial?> PedirTokenAsync(string tipo, string cuerpo, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"{_urlSupabase}/auth/v1/token?grant_type={tipo}")
        { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };
        req.Headers.Add("apikey", _clavePublicable);

        using var res = await _http.SendAsync(req, ct);
        string texto = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
        {
            UltimoFallo = MensajeDeAuth((int)res.StatusCode, texto);
            LogBus.Log("cuenta", $"auth {tipo} → HTTP {(int)res.StatusCode}");
            return null;
        }

        using var doc = JsonDocument.Parse(texto);
        var raiz = doc.RootElement;
        string access = Texto(raiz, "access_token");
        string refresh = Texto(raiz, "refresh_token");
        if (access.Length == 0 || refresh.Length == 0)
        {
            UltimoFallo = "Miracle contestó sin sesión. Inténtalo de nuevo.";
            return null;
        }

        int duracion = raiz.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out int s) ? s : 3600;
        var usuario = raiz.TryGetProperty("user", out var u) && u.ValueKind == JsonValueKind.Object
            ? u : default;

        // El id sale del TOKEN, no del cuerpo: es el que el backend va a leer. Si el cuerpo dijera
        // otro, el que manda es el que viaja en el Bearer.
        string id = SubDelToken(access);
        if (id.Length == 0) id = Texto(usuario, "id");

        return new Credencial(
            AccessToken: access,
            RefreshToken: refresh,
            Caduca: _ahora().AddSeconds(duracion),
            UsuarioId: id,
            Email: Texto(usuario, "email"),
            Nombre: "");
    }

    /// <summary>
    /// Trae el nombre real desde `profiles`. Best-effort: no tenerlo no impide trabajar.
    /// </summary>
    /// <remarks>
    /// De la BASE y no de lo que teclee el usuario. El popup anterior pedía el nombre y lo guardaba
    /// en config.json, así que dos instalaciones del mismo médico podían llamarle distinto y
    /// ninguna coincidir con lo que ve en el portal.
    /// </remarks>
    private async Task CompletarNombreAsync(CancellationToken ct)
    {
        var cred = _credencial;
        if (cred == null || cred.UsuarioId.Length == 0) return;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"{_urlSupabase}/rest/v1/profiles?id=eq.{Uri.EscapeDataString(cred.UsuarioId)}&select=full_name");
            req.Headers.Add("apikey", _clavePublicable);
            req.Headers.Add("Authorization", $"Bearer {cred.AccessToken}");

            using var res = await _http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return;

            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0) return;

            string nombre = Texto(doc.RootElement[0], "full_name");
            if (nombre.Length > 0) _credencial = cred with { Nombre = nombre };
        }
        catch (Exception e) { LogBus.Log("cuenta", $"sin nombre de perfil: {e.Message}"); }
    }

    private void Guardar()
    {
        var cred = _credencial;
        if (cred == null) return;
        try
        {
            string carpeta = Path.GetDirectoryName(RutaDeLaCredencial)!;
            Directory.CreateDirectory(carpeta);
            byte[] claro = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cred));
            File.WriteAllBytes(RutaDeLaCredencial, Dpapi.Proteger(claro));
        }
        catch (Exception e) { LogBus.Log("cuenta", $"no se pudo guardar la sesión: {e.Message}"); }
    }

    // ── menudencias ──────────────────────────────────────────────────────────

    /// <summary>
    /// El <c>sub</c> de un JWT, sin verificar la firma — que es trabajo del backend, no nuestro.
    /// Cadena vacía si el token no tiene la forma esperada.
    /// </summary>
    public static string SubDelToken(string jwt)
    {
        try
        {
            var partes = jwt.Split('.');
            if (partes.Length < 2) return "";
            string carga = partes[1].Replace('-', '+').Replace('_', '/');
            carga = carga.PadRight(carga.Length + (4 - carga.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Convert.FromBase64String(carga));
            return Texto(doc.RootElement, "sub");
        }
        catch { return ""; }
    }

    /// <summary>
    /// Qué contestarle a alguien que no pudo entrar. Cada causa dice lo suyo: «revisa la contraseña»
    /// y «tu cuenta no está confirmada» se arreglan de formas distintas, y un genérico manda a la
    /// persona al sitio equivocado (aprendizaje nº2).
    /// </summary>
    private static string MensajeDeAuth(int codigo, string cuerpo)
    {
        string texto = cuerpo.ToLowerInvariant();
        if (texto.Contains("email not confirmed"))
            return "Tu cuenta todavía no está confirmada. Revisa el correo de bienvenida.";
        if (codigo == 400 || codigo == 401)
            return "Correo o contraseña incorrectos.";
        if (codigo == 429)
            return "Demasiados intentos. Espera un momento e inténtalo de nuevo.";
        if (codigo >= 500)
            return "Miracle no está respondiendo ahora mismo. Inténtalo en un minuto.";
        return $"No se pudo entrar (HTTP {codigo}).";
    }

    private static string Texto(JsonElement o, string campo) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(campo, out var v)
        && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}

/// <summary>
/// La sesión de un médico, tal como se guarda. Es un `record` para que renovar sea construir una
/// nueva y no mutar la de al lado a media llamada.
/// </summary>
public sealed record Credencial(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset Caduca,
    string UsuarioId,
    string Email,
    string Nombre)
{
    /// <summary>¿Se muere antes de ese instante? Con el margen ya sumado por quien pregunta.</summary>
    public bool CaducaAntesDe(DateTimeOffset limite) => Caduca <= limite;
}
