using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace U.WindowsClient.Credenciales;

/// <summary>
/// LAS CLAVES DE PAGO NO VIAJAN DENTRO DEL .EXE: SE LE PIDEN A GRAPH. Promesa 300 (spec 041).
/// </summary>
/// <remarks>
/// QUÉ PROBLEMA RESUELVE, PORQUE NO ERA EL QUE PARECÍA. Un <c>Setup.exe</c> distribuido llevaba
/// embebidas la credencial de Graph y el token de actualizaciones, y se daba por hecho que la voz iba
/// cubierta por <c>GeminiDefaultApiKey</c>. Dejó de estarlo el día que la voz pasó a GPT-Live: esa
/// clave sigue inyectándose en el build y NADIE LA LEE. Resultado, descubierto el 2026-09-18 al ir a
/// generar un instalador para otra persona: la copia instalada llegaba SIN VOZ y SIN JEV, porque las
/// dos claves salían de variables de entorno del equipo de quien desarrolla.
///
/// POR QUÉ NO SE EMBEBEN, HABIENDO MECANISMO. Era una línea en el .csproj. Se descartó con el dueño:
/// un .exe que lleva claves de pago se las entrega a quien lo reciba, y rotarlas obligaría a sacar
/// instalador nuevo y a que cada usuario lo instalara. La credencial de Graph sí se embebe porque la
/// emite el propio producto y se revoca por etiqueta; OpenAI y TypeSafe son dinero de terceros.
///
/// CÓMO, ENTONCES: con la credencial que YA va embebida. El cliente pide sus claves a
/// <c>GET /api/v1/agent/claves</c>, que cuelga de <c>/api/v1</c> y por tanto ya pasa por el
/// <c>requireApiKey</c> de Graph — no viaja ni un secreto nuevo dentro del binario, y rotar es cambiar
/// una variable de entorno en el backend.
///
/// SE LE INYECTA TODO —el entorno, el cómo pedir y el log— para que el contrato lo juzgue entero sin
/// red y sin pantalla, que es la única forma de que la promesa signifique algo.
///
/// LO QUE ESTO NO ES: la clave acaba en la memoria del cliente. Es mejor que dentro del instalador
/// —no se reparte, y se puede rotar y revocar— pero lo óptimo es una credencial efímera por sesión,
/// como ya hace <c>DictadoEnVivo</c> con Soniox. Está escrito en la spec 041 como el corte siguiente.
/// </remarks>
public sealed class ClavesDelBackend
{
    /// <summary>Las que se piden. Los nombres son los de las variables de entorno de siempre.</summary>
    public const string Voz = "OPENAI_API_KEY";
    public const string Jev = "TYPESAFE_API_KEY";

    /// <summary>Cómo se llama cada una en la respuesta del backend.</summary>
    private static readonly (string Variable, string Campo)[] Pedidas =
    {
        (Voz, "openai"),
        (Jev, "typesafe"),
    };

    private readonly Func<string, string?> _entorno;
    private readonly Func<CancellationToken, Task<string>> _pedir;
    private readonly Action<string> _log;
    private readonly Dictionary<string, string> _traidas = new(StringComparer.Ordinal);
    private readonly object _candado = new();
    private bool _yaSePidio;

    /// <param name="entorno">De dónde sale una variable de entorno. Manda sobre el backend.</param>
    /// <param name="pedir">Cómo se le piden las claves a Graph. Devuelve el cuerpo JSON tal cual.</param>
    /// <param name="log">Dónde contar lo que pasó. NUNCA recibe una clave.</param>
    public ClavesDelBackend(Func<string, string?> entorno, Func<CancellationToken, Task<string>> pedir, Action<string> log)
    {
        _entorno = entorno ?? throw new ArgumentNullException(nameof(entorno));
        _pedir = pedir ?? throw new ArgumentNullException(nameof(pedir));
        _log = log ?? (_ => { });
    }

    /// <summary>
    /// Qué hay, en una línea, para el log y el panel: CUÁNTAS claves y CUÁLES faltan, jamás su valor.
    /// </summary>
    public string Estado { get; private set; } = "sin pedir todavía";

    /// <summary>
    /// Pide las claves al backend UNA sola vez y devuelve cuántas llegaron.
    /// </summary>
    /// <remarks>
    /// UNA VEZ, Y TAMBIÉN CUANDO FALLA. Dos claves son un viaje, no dos. Y si el backend está caído no
    /// se reintenta en bucle: sin voz se puede trabajar, y un bucle contra un backend caído es el
    /// pendiente nº3 de CLAUDE.md, que ya se pagó una vez con 14 turnos rebotando.
    /// </remarks>
    public async Task<int> TraerAsync(CancellationToken ct = default)
    {
        lock (_candado)
        {
            if (_yaSePidio) return _traidas.Count;
            _yaSePidio = true;
        }

        string cuerpo;
        try
        {
            cuerpo = await _pedir(ct).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // LA CADENA ENTERA, no solo el mensaje de fuera: un catch mudo convierte un bug de
            // aridad en «la API no existe» (patrón nº3).
            string causa = "";
            for (var x = e; x != null; x = x.InnerException)
                causa += (causa.Length > 0 ? " ← " : "") + $"{x.GetType().Name}: {x.Message}";
            Estado = "sin claves del backend: " + causa;
            _log($"claves: no pude pedírselas a Graph ({causa}). Sigo con lo que haya en el entorno.");
            return 0;
        }

        try
        {
            using var json = JsonDocument.Parse(cuerpo);
            foreach (var (variable, campo) in Pedidas)
            {
                if (json.RootElement.TryGetProperty(campo, out var v)
                    && v.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(v.GetString()))
                {
                    lock (_candado) _traidas[variable] = v.GetString()!.Trim();
                }
            }
        }
        catch (Exception e)
        {
            Estado = $"la respuesta del backend no se entiende: {e.GetType().Name}";
            _log($"claves: la respuesta de Graph no se entiende ({e.GetType().Name}: {e.Message}).");
            return 0;
        }

        Contar();
        _log("claves: " + Estado);
        return _traidas.Count;
    }

    /// <summary>
    /// La clave que toca usar: la del entorno si está, y si no la que dio el backend. Cadena vacía si
    /// no hay ninguna — nunca null, para que quien la use no tenga que distinguir dos formas de nada.
    /// </summary>
    /// <remarks>
    /// EL ENTORNO MANDA, Y ES DELIBERADO: la máquina de quien desarrolla sigue comportándose
    /// exactamente igual que antes de esta promesa, y probar con OTRA clave es poner la variable, sin
    /// tocar el backend ni a los demás. VACÍO NO ES AUSENTE (patrón nº9): una variable puesta a cadena
    /// vacía no es una clave, y cae al backend igual que si no existiera.
    /// </remarks>
    public string Resolver(string variable)
    {
        string? delEntorno = null;
        try { delEntorno = _entorno(variable); } catch { }
        if (!string.IsNullOrWhiteSpace(delEntorno)) return delEntorno.Trim();
        lock (_candado) return _traidas.TryGetValue(variable, out var v) ? v : "";
    }

    // ── La instancia de la app ───────────────────────────────────────────────────────────────

    /// <summary>
    /// La de la app viva. Null en el contrato y en cualquier prueba, y ahí <see cref="DeLaApp"/> se
    /// queda con el entorno — que es exactamente como se comportaba todo antes de esta promesa.
    /// </summary>
    public static ClavesDelBackend? Viva { get; set; }

    /// <summary>
    /// La clave que toca usar en la app. Estático porque quien la pide —la voz— también lo es.
    /// </summary>
    public static string DeLaApp(string variable)
    {
        var viva = Viva;
        if (viva != null) return viva.Resolver(variable);
        return DelEntornoDeSiempre(variable) ?? "";
    }

    /// <summary>
    /// El proceso primero y el registro de usuario después.
    /// </summary>
    /// <remarks>
    /// `setx` escribe el registro pero NO el bloque de entorno de un proceso ya vivo, y un hijo hereda
    /// el de su padre en el instante en que nace. Pasó de verdad al configurar la clave de la voz
    /// (2026-08-24): el registro ya la tenía y Ü seguía diciendo que faltaba. El registro es el último
    /// recurso, no el primero.
    /// </remarks>
    public static string? DelEntornoDeSiempre(string variable)
    {
        string? v = Environment.GetEnvironmentVariable(variable);
        if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
        try { v = Environment.GetEnvironmentVariable(variable, EnvironmentVariableTarget.User); } catch { v = null; }
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }

    /// <summary>
    /// La de la app de verdad: entorno de siempre, y lo que falte se le pide a Graph con la credencial
    /// que el instalador ya lleva embebida.
    /// </summary>
    public static ClavesDelBackend DeGraph(string baseUrl, string? apiKey, Action<string> log)
    {
        var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        return new ClavesDelBackend(
            DelEntornoDeSiempre,
            async ct =>
            {
                using var req = new System.Net.Http.HttpRequestMessage(
                    System.Net.Http.HttpMethod.Get, $"{(baseUrl ?? "").TrimEnd('/')}/api/v1/agent/claves");
                req.Headers.Add("X-API-Key", apiKey ?? "");
                using var res = await http.SendAsync(req, ct).ConfigureAwait(false);
                string cuerpo = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode)
                    // NUNCA el cuerpo entero: en un 200 trae las claves, y un mensaje de error que las
                    // arrastrara acabaría en el log igual que ellas.
                    throw new InvalidOperationException($"HTTP {(int)res.StatusCode} pidiendo las claves a Graph");
                return cuerpo;
            },
            log);
    }

    /// <summary>Cuántas y cuáles faltan. Sin valores: el log se pega en los PR.</summary>
    private void Contar()
    {
        var faltan = new List<string>();
        foreach (var (variable, _) in Pedidas)
            if (!_traidas.ContainsKey(variable)) faltan.Add(variable);

        Estado = faltan.Count == 0
            ? $"{_traidas.Count} clave(s) del backend, no falta ninguna"
            : $"{_traidas.Count} clave(s) del backend; falta(n) {string.Join(", ", faltan)}";
    }
}
