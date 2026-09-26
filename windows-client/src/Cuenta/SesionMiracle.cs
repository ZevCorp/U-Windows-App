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
    /// Crea una cuenta nueva contra Supabase Auth. Las mismas reglas que el registro del portal
    /// (app/registro/actions.ts): el nombre es obligatorio y va como metadato, y la contraseña
    /// pide 8 caracteres como mínimo.
    /// </summary>
    /// <remarks>
    /// LO QUE ESTA FUNCIÓN NO PUEDE CONFUNDIR, y es la promesa 97: Supabase contesta «ok» tanto
    /// cuando la cuenta queda lista como cuando queda creada PENDIENTE DE CONFIRMAR por correo. Lo
    /// único que las separa es si vino sesión en el cuerpo. Darlas por iguales metería al médico en
    /// una consulta con una sesión que no existe, y el 401 aparecería mucho después, sin relación
    /// aparente con el alta.
    ///
    /// NO SE MANDA `emailRedirectTo`: sin él, Supabase usa la Site URL del proyecto, que es el
    /// portal. El médico confirma en el navegador y vuelve aquí a entrar — que es el único camino
    /// que funciona, porque un enlace de correo no sabe abrir una ventana de WPF.
    ///
    /// UN CORREO YA REGISTRADO NO SE DELATA. Con la confirmación activa, Supabase contesta «ok» con
    /// un usuario sin identities justamente para no revelar qué cuentas existen; el portal enseña
    /// el mismo «revisa tu correo» y aquí se hace igual. Quien ya tenga cuenta recibirá el aviso de
    /// Supabase.
    /// </remarks>
    public async Task<ResultadoDeAlta> CrearCuentaAsync(string nombre, string email, string clave,
        CancellationToken ct = default)
    {
        UltimoFallo = "";

        if (string.IsNullOrWhiteSpace(nombre))
        {
            UltimoFallo = "Escribe tu nombre completo.";
            return ResultadoDeAlta.Fallo;
        }
        // SE PARA AQUÍ Y NO EN EL SERVIDOR. Supabase contesta en inglés y en genérico; «al menos 8
        // caracteres» se arregla solo, sin gastar una llamada ni hacer leer un error ajeno.
        if (clave.Length < 8)
        {
            UltimoFallo = "La contraseña necesita al menos 8 caracteres.";
            return ResultadoDeAlta.Fallo;
        }

        try
        {
            string cuerpo = JsonSerializer.Serialize(new
            {
                email,
                password = clave,
                // El nombre viaja como metadato, igual que en el portal: de ahí lo recoge el
                // trigger que crea el perfil y la organización personal.
                data = new { full_name = nombre.Trim() },
            });

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_urlSupabase}/auth/v1/signup")
            { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };
            req.Headers.Add("apikey", _clavePublicable);

            using var res = await _http.SendAsync(req, ct);
            string texto = await res.Content.ReadAsStringAsync(ct);

            if (!res.IsSuccessStatusCode)
            {
                UltimoFallo = MensajeDeAlta((int)res.StatusCode, texto);
                LogBus.Log("cuenta", $"alta → HTTP {(int)res.StatusCode}");
                return ResultadoDeAlta.Fallo;
            }

            using var doc = JsonDocument.Parse(texto);
            string access = Texto(doc.RootElement, "access_token");

            // SIN SESIÓN NO SE ENTRA. Aquí está la promesa 97 entera.
            if (access.Length == 0)
            {
                LogBus.Log("cuenta", "cuenta creada; falta confirmar el correo");
                return ResultadoDeAlta.FaltaConfirmar;
            }

            _credencial = LeerCredencial(doc.RootElement, access);
            await CompletarNombreAsync(ct);
            if (_credencial.Nombre.Length == 0) _credencial = _credencial with { Nombre = nombre.Trim() };
            Guardar();
            LogBus.Log("cuenta", $"cuenta creada y sesión abierta · {_credencial.UsuarioId}");
            Cambio?.Invoke(true);
            return ResultadoDeAlta.Entro;
        }
        catch (Exception e)
        {
            UltimoFallo = "No se pudo conectar con Miracle. Revisa la conexión e inténtalo de nuevo.";
            LogBus.Log("cuenta", $"alta falló: {e.GetType().Name}: {e.Message}");
            return ResultadoDeAlta.Fallo;
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

        return LeerCredencial(raiz, access);
    }

    /// <summary>
    /// La credencial que viene en un cuerpo de Supabase. La comparten entrar, renovar y crear
    /// cuenta: tres caminos que devuelven la misma forma, y leerla en tres sitios distintos sería
    /// tres formas de equivocarse.
    /// </summary>
    private Credencial LeerCredencial(JsonElement raiz, string access)
    {
        int duracion = raiz.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out int s) ? s : 3600;
        var usuario = raiz.TryGetProperty("user", out var u) && u.ValueKind == JsonValueKind.Object
            ? u : default;

        // El id sale del TOKEN, no del cuerpo: es el que el backend va a leer. Si el cuerpo dijera
        // otro, el que manda es el que viaja en el Bearer.
        string id = SubDelToken(access);
        if (id.Length == 0) id = Texto(usuario, "id");

        return new Credencial(
            AccessToken: access,
            RefreshToken: Texto(raiz, "refresh_token"),
            Caduca: _ahora().AddSeconds(duracion),
            UsuarioId: id,
            Email: Texto(usuario, "email"),
            Nombre: "");
    }

    /// <summary>Qué contestarle a alguien que no pudo crear la cuenta. Cada causa dice lo suyo.</summary>
    private static string MensajeDeAlta(int codigo, string cuerpo)
    {
        string texto = cuerpo.ToLowerInvariant();
        if (texto.Contains("already registered") || texto.Contains("already been registered"))
            return "Ese correo ya tiene cuenta. Entra con tu contraseña.";
        if (texto.Contains("password"))
            return "La contraseña no cumple los requisitos del servidor.";
        if (texto.Contains("email") && texto.Contains("invalid"))
            return "Ese correo no parece válido.";
        if (codigo == 422) return "Faltan datos o alguno no es válido.";
        if (codigo == 429) return "Demasiados intentos. Espera un momento e inténtalo de nuevo.";
        if (codigo >= 500) return "Miracle no está respondiendo ahora mismo. Inténtalo en un minuto.";
        return $"No se pudo crear la cuenta (HTTP {codigo}).";
    }

    /// <summary>
    /// Guarda el nombre del médico en su perfil. Es el único camino de ESCRITURA de esta clase —
    /// todo lo demás lee.
    /// </summary>
    /// <summary>
    /// UNA LLAMADA A LA BASE DEL PORTAL (PostgREST) con la clave pública y el token del médico, por el
    /// mismo transporte que la sesión. Devuelve el código HTTP y el cuerpo; 401 sin pedir nada si no
    /// hay médico dentro.
    /// </summary>
    /// <remarks>
    /// UN SOLO CAMINO, y es lo que lo hace juzgable (spec 055: atajos, preferencias, pacientes). Hasta
    /// hoy cada lectura de Supabase se armaba a mano contra <c>Nube</c> y un <c>HttpClient</c> propio,
    /// y el contrato no podía ver qué se pedía. Por aquí pasa por el transporte que se le inyectó a la
    /// sesión, así que una prueba ve cada petición y sus cabeceras.
    ///
    /// NUNCA SE MANDA EL <c>user_id</c>: lo pone la RLS con <c>auth.uid()</c>. Mandarlo sería dejar que
    /// el cliente diga de quién son los datos — la misma frontera que ya respetan el espejo y el pin.
    /// </remarks>
    public async Task<(int Http, string Cuerpo)> RestAsync(HttpMethod metodo, string ruta,
        string? cuerpo = null, string? prefer = null, CancellationToken ct = default)
    {
        string token = await TokenVigenteAsync(ct);
        if (token.Length == 0) return (401, "");

        using var req = new HttpRequestMessage(metodo, _urlSupabase + ruta);
        req.Headers.Add("apikey", _clavePublicable);
        req.Headers.Add("Authorization", $"Bearer {token}");
        if (prefer != null) req.Headers.Add("Prefer", prefer);
        if (cuerpo != null) req.Content = new StringContent(cuerpo, Encoding.UTF8, "application/json");

        using var res = await _http.SendAsync(req, ct);
        return ((int)res.StatusCode, await res.Content.ReadAsStringAsync(ct));
    }

    /// <remarks>
    /// SE LEE EL PERFIL ACTUAL ANTES DE GUARDAR, y no por prudencia: la RPC
    /// <c>update_own_profile</c> reescribe los siete campos a la vez sin <c>COALESCE</c> con lo que
    /// ya había (promesa 100). Sin este paso, guardar el nombre de alguien que ya tuviera su
    /// especialidad o su documento llenos se los borraría — un dato que el portal también lee.
    /// </remarks>
    public async Task<(bool Ok, string Mensaje)> GuardarNombreAsync(string nombreNuevo,
        CancellationToken ct = default)
    {
        string limpio = (nombreNuevo ?? "").Trim();
        // El mismo mínimo que la RPC, comprobado ANTES de salir a la red: el error de Postgres
        // llega en un JSON que hay que parsear, y «al menos 3 caracteres» se ve solo.
        if (limpio.Length < 3) return (false, "El nombre es demasiado corto.");
        if (limpio.Length > 120) return (false, "El nombre no puede pasar de 120 caracteres.");
        if (_credencial == null) return (false, "No hay sesión activa.");

        try
        {
            string token = await TokenVigenteAsync(ct);
            if (token.Length == 0) return (false, "No hay sesión activa.");

            var actual = await LeerPerfilActualAsync(token, ct);
            string cuerpo = PerfilRpc.CuerpoDeGuardarNombre(limpio, actual);

            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"{_urlSupabase}/rest/v1/rpc/update_own_profile")
            { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };
            req.Headers.Add("apikey", _clavePublicable);
            req.Headers.Add("Authorization", $"Bearer {token}");

            using var res = await _http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
            {
                string cuerpoError = await res.Content.ReadAsStringAsync(ct);
                LogBus.Log("cuenta", $"guardar nombre → HTTP {(int)res.StatusCode}");
                return (false, MensajeDeGuardarNombre((int)res.StatusCode, cuerpoError));
            }

            _credencial = _credencial! with { Nombre = limpio };
            Guardar();
            LogBus.Log("cuenta", "nombre guardado en el perfil");
            return (true, "");
        }
        catch (Exception e)
        {
            LogBus.Log("cuenta", $"guardar nombre falló: {e.GetType().Name}: {e.Message}");
            return (false, "No se pudo guardar. Revisa la conexión e inténtalo de nuevo.");
        }
    }

    /// <summary>
    /// La especialidad del médico, como código (`medicina_general`). Vacío si no la tiene puesta.
    /// </summary>
    /// <remarks>
    /// La pide «Tu sugerida» (<see cref="U.WindowsClient.Clinical.SugeridaDelMedico"/>, promesa
    /// 190): el pin de plantilla vive en `user_template_preferences`, cuya clave primaria es
    /// `(user_id, specialty_code)`. Sin especialidad, el pin se guardaría con la clave vacía y sería
    /// invisible desde el portal, que lo guarda con la de la plantilla.
    ///
    /// NO SE CACHEA aquí: se lee al abrir la ventana de consulta y se guarda allí. Un dato del
    /// perfil que esta clase memorizara quedaría viejo en cuanto el médico lo cambiara en el portal,
    /// y nadie sabría por qué su sugerida dejó de aparecer.
    /// </remarks>
    public async Task<string> EspecialidadAsync(CancellationToken ct = default)
    {
        string token = await TokenVigenteAsync(ct);
        if (token.Length == 0) return "";
        var perfil = await LeerPerfilActualAsync(token, ct);
        return perfil.SpecialtyCode;
    }

    /// <summary>Los seis campos del perfil profesional que NO son el nombre, tal como están hoy.</summary>
    private async Task<PerfilProfesional> LeerPerfilActualAsync(string token, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"{_urlSupabase}/rest/v1/profiles?id=eq.{Uri.EscapeDataString(_credencial!.UsuarioId)}"
                + "&select=identification_number,professional_registration,specialty_code,"
                + "specialty_name,practice_country,practice_city");
            req.Headers.Add("apikey", _clavePublicable);
            req.Headers.Add("Authorization", $"Bearer {token}");

            using var res = await _http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return PerfilProfesional.Vacio;

            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                return PerfilProfesional.Vacio;

            var f = doc.RootElement[0];
            return new PerfilProfesional(
                Texto(f, "identification_number"), Texto(f, "professional_registration"),
                Texto(f, "specialty_code"), Texto(f, "specialty_name"),
                Texto(f, "practice_country"), Texto(f, "practice_city"));
        }
        catch (Exception e)
        {
            LogBus.Log("cuenta", $"no se pudo leer el perfil antes de guardar: {e.Message}");
            return PerfilProfesional.Vacio;
        }
    }

    /// <summary>
    /// El mensaje del RPC ya viene en castellano y listo para mostrar («El nombre es demasiado
    /// corto.»): PostgREST lo entrega en <c>message</c>. Se usa tal cual antes que inventar uno
    /// propio que diría lo mismo peor.
    /// </summary>
    private static string MensajeDeGuardarNombre(int codigo, string cuerpo)
    {
        try
        {
            using var doc = JsonDocument.Parse(cuerpo);
            string msg = Texto(doc.RootElement, "message");
            if (msg.Length > 0) return msg;
        }
        catch (JsonException) { /* el cuerpo no es JSON: se cae a los genéricos de abajo */ }

        if (codigo is 401 or 403) return "No tienes permiso para editar este perfil.";
        if (codigo >= 500) return "Miracle no está respondiendo ahora mismo. Inténtalo en un minuto.";
        return "No se pudo guardar el nombre.";
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
/// En qué quedó un intento de crear cuenta. Son TRES y no dos a propósito: «creada pero falta
/// confirmar» no es entrar, y tampoco es un fallo — decirle a alguien que su alta falló cuando
/// tiene el correo esperándole es mandarle a repetirla.
/// </summary>
public enum ResultadoDeAlta
{
    /// <summary>Cuenta creada y sesión abierta: se puede empezar a trabajar.</summary>
    Entro,

    /// <summary>Cuenta creada; Supabase mandó el correo de confirmación y hay que abrirlo.</summary>
    FaltaConfirmar,

    /// <summary>No se creó. El motivo está en <see cref="SesionMiracle.UltimoFallo"/>.</summary>
    Fallo,
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
