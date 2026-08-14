using System.Net.Http;
using System.Text;
using System.Text.Json;
using U.Graph;
using U.Graph.Surfaces;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Clinical;

/// <summary>Un campo que se escribió, con lo que tenía antes. Es lo que permite deshacer.</summary>
public sealed record LoEscrito(DetectedField Campo, string Antes, string Ahora);

/// <summary>
/// DE LO DICHO A LOS CAMPOS DE SAP. Recibe frases sueltas, las manda a organizar, y escribe lo que
/// el emparejador decide — comprobando cada escritura.
/// </summary>
/// <remarks>
/// LA CADENCIA ES LA DEL PORTAL (public/plugin/plugin-execution-client.js), y cada detalle está
/// copiado por un motivo que se ve al leer el código de allá:
///
///   · Al emparejador NO se le manda el transcript, se le manda LA NOTA ORGANIZADA. Cada frase
///     reconstruye la nota entera, así que el LLM siempre ve un texto coherente y no fragmentos.
///   · Se espera 350 ms tras cada nota antes de emparejar: hablando seguido, no se dispara una
///     llamada por frase.
///   · Solo viajan los campos QUE FALTAN, y los ya escritos van como `alreadyFulfilled` para que el
///     modelo no se repita ni se contradiga.
///   · Una vuelta a la vez. Si llega otra nota mientras trabaja, se encadena en vez de solaparse —
///     el mismo invariante que <see cref="Mapeador.VueltaUnica"/> protege en el mapeador.
///
/// LO QUE AÑADE, y no está en el portal porque en el navegador no hace falta: SE RELEE CADA CAMPO
/// DESPUÉS DE ESCRIBIRLO. En SAP un campo puede aceptar el valor y devolverlo recortado, convertido
/// o vacío, y un campo bloqueado acepta la asignación sin quejarse. Medido el 2026-08-13: tres
/// campos de signos vitales quedaron de solo lectura y `Execute` habría dicho que los escribió.
/// Dar por bueno lo que no se ha releído es exactamente el «verde sin mirar» que este proyecto
/// persigue — y aquí acaba en una historia clínica.
/// </remarks>
public sealed class RellenadorSap
{
    private readonly GraphConfig _config;
    private readonly SapGuiSurface _sap;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(45) };

    /// <summary>
    /// La pantalla donde esto tiene sentido. Se compara por PROGRAMA y número de dynpro, no por la
    /// dirección entera: el prefijo lleva el sistema («QAS») y la transacción, que cambian entre
    /// entornos sin que la pantalla sea otra.
    /// </summary>
    public static bool EsLaPantallaDeTriage(string ubicacion) =>
        ubicacion.StartsWith("sapgui://", StringComparison.OrdinalIgnoreCase)
        && ubicacion.Contains("SAPLY000", StringComparison.OrdinalIgnoreCase);

    private readonly StringBuilder _dicho = new();
    private string _nota = "";
    private string _sesion = Guid.NewGuid().ToString();
    private int _secuencia;

    /// <summary>Lo ya colocado, por orden de campo: no se vuelve a ofrecer ni a pisar.</summary>
    private readonly Dictionary<int, string> _yaPuesto = new();

    /// <summary>El último llenado, para poder deshacerlo entero.</summary>
    private readonly List<LoEscrito> _ultimoLote = new();

    private CancellationTokenSource? _espera;
    private int _trabajando;
    private bool _otraVuelta;

    public RellenadorSap(GraphConfig config, SapGuiSurface sap)
    {
        _config = config;
        _sap = sap;
    }

    /// <summary>Qué está pasando, en castellano, para pintarlo en la carita.</summary>
    public event Action<string>? Cuenta;

    /// <summary>Se escribieron campos. Trae cuántos y en qué, para poder deshacer.</summary>
    public event Action<IReadOnlyList<LoEscrito>>? Escribio;

    public bool HayQueDeshacer => _ultimoLote.Count > 0;

    /// <summary>Empieza de cero: nota vacía, nada colocado, sesión nueva.</summary>
    public void Empezar()
    {
        _dicho.Clear();
        _nota = "";
        _sesion = Guid.NewGuid().ToString();
        _secuencia = 0;
        _yaPuesto.Clear();
        _ultimoLote.Clear();
    }

    /// <summary>
    /// Una frase cerrada. Se acumula y se pide la nota organizada; el emparejado va después, con su
    /// espera, para no llamar al modelo por cada cosa que se diga.
    /// </summary>
    public void Oido(string frase)
    {
        if (string.IsNullOrWhiteSpace(frase)) return;
        if (_dicho.Length > 0) _dicho.Append(' ');
        _dicho.Append(frase.Trim());

        _espera?.Cancel();
        _espera = new CancellationTokenSource();
        var ct = _espera.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                // 350 ms, el mismo respiro que el portal. Es lo que separa «sigue hablando» de «ya
                // dijo una idea completa».
                await Task.Delay(350, ct);
                await UnaVueltaAsync(ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { LogBus.Log("dictado", $"la vuelta de relleno falló: {e.Message}"); }
        }, ct);
    }

    private async Task UnaVueltaAsync(CancellationToken ct)
    {
        // UNA A LA VEZ. Si entra otra mientras ésta trabaja se apunta y se hace al terminar: dos
        // emparejados en paralelo escribirían el mismo campo dos veces con dos respuestas distintas.
        if (Interlocked.Exchange(ref _trabajando, 1) == 1) { _otraVuelta = true; return; }
        try
        {
            do
            {
                _otraVuelta = false;
                await OrganizarYRellenarAsync(ct);
            } while (_otraVuelta && !ct.IsCancellationRequested);
        }
        finally { Interlocked.Exchange(ref _trabajando, 0); }
    }

    /// <summary>
    /// Rellenar a partir de una nota YA ORGANIZADA, en una sola pasada. Es el camino de la
    /// exportación: la web ya escuchó al médico y organizó la nota, así que aquí solo queda
    /// emparejar y escribir.
    /// </summary>
    /// <returns>Lo escrito, y las etiquetas de lo que quedó sin llenar.</returns>
    public async Task<(IReadOnlyList<LoEscrito> Escritos, IReadOnlyList<string> SinLlenar)>
        RellenarConNotaAsync(string nota, CancellationToken ct)
    {
        Empezar();
        _nota = nota ?? "";

        var escritos = new List<LoEscrito>();
        // VARIAS PASADAS, y no por si acaso: al escribir en SAP unos campos se abren y otros se
        // cierran —lo vimos con la presión arterial el 2026-08-13—, así que un inventario tomado
        // una sola vez deja fuera lo que apareció después. Se repite mientras siga habiendo
        // progreso, y se para en cuanto una vuelta no escribe nada: insistir sin avanzar es un
        // bucle, no persistencia.
        for (int vuelta = 1; vuelta <= 4 && !ct.IsCancellationRequested; vuelta++)
        {
            int antes = escritos.Count;
            escritos.AddRange(await UnaPasadaDeRellenoAsync(ct));
            if (escritos.Count == antes) break;
        }

        var pendientes = _sap.ReadFields()
            .Where(f => f.Selector.Length > 0 && f.Label.Length > 0)
            .Where(f => f.ActionType is "input" or "select" or "click")
            .Where(f => !_yaPuesto.ContainsKey(f.StepOrder))
            .Where(f => string.IsNullOrWhiteSpace(f.CurrentValue))
            .Select(f => f.Label)
            .Distinct()
            .ToList();

        _ultimoLote.Clear();
        _ultimoLote.AddRange(escritos);
        return (escritos, pendientes);
    }

    private async Task OrganizarYRellenarAsync(CancellationToken ct)
    {
        string dicho = _dicho.ToString();
        if (dicho.Trim().Length == 0) return;

        // Los campos se leen AHORA, no al arrancar: en SAP la editabilidad cambia —hay campos que
        // se abren y se cierran según lo que ya haya puesto— y un inventario viejo ofrecería sitios
        // donde ya no se puede escribir (2026-08-13, visto con la presión arterial).
        var campos = _sap.ReadFields()
            .Where(f => f.Selector.Length > 0 && f.Label.Length > 0)
            .Where(f => f.ActionType is "input" or "select" or "click")
            .Where(f => !_yaPuesto.ContainsKey(f.StepOrder))
            .ToList();

        if (campos.Count == 0) { Cuenta?.Invoke("No quedan campos por llenar."); return; }

        Cuenta?.Invoke("Organizando la nota…");
        _secuencia++;

        string cuerpo = JsonSerializer.Serialize(new
        {
            session_id = _sesion,
            transcript = dicho,
            sequence = _secuencia,
            stages = new { transcription = false, note = true, autofill = true },
            note = new { title = "Triage", content = _nota },
            fields = campos.Select(f => new
            {
                stepOrder = f.StepOrder,
                actionType = f.ActionType,
                label = f.Label,
                selector = f.Selector,
                controlType = f.ControlType,
                allowedOptions = f.AllowedOptions?.Select(o => new { value = o.Value, label = o.Label }),
                currentValue = f.CurrentValue,
            }),
            already_fulfilled = _yaPuesto.Select(p => new { stepOrder = p.Key, value = p.Value }),
            page_url = "sapgui://triage",
        });

        JsonElement res;
        try { res = await PedirAsync(cuerpo, ct); }
        catch (Exception e) { Cuenta?.Invoke($"No pude organizar la nota: {e.Message}"); return; }

        if (res.TryGetProperty("note", out var nota) && nota.TryGetProperty("content", out var c)
            && c.ValueKind == JsonValueKind.String)
        {
            string texto = c.GetString() ?? "";
            if (texto.Trim().Length > 0) _nota = texto;
        }

        if (!res.TryGetProperty("autofill", out var relleno)
            || !relleno.TryGetProperty("matches", out var matches)
            || matches.ValueKind != JsonValueKind.Array)
        {
            Cuenta?.Invoke("La nota no trajo ningún campo que llenar todavía.");
            return;
        }

        var lote = EscribirLoEmparejado(matches, campos, ct);
        if (lote.Count == 0) { Cuenta?.Invoke("Nada nuevo que llenar."); return; }

        _ultimoLote.Clear();
        _ultimoLote.AddRange(lote);
        Cuenta?.Invoke($"✓ {lote.Count} campo(s): {string.Join(", ", lote.Select(x => x.Campo.Label))}");
        Escribio?.Invoke(lote);
    }

    /// <summary>
    /// UNA PASADA de emparejar y escribir, sobre la nota que ya se tenga. No organiza nada: se usa
    /// cuando la nota viene hecha —la exportación desde la web— y para las vueltas de repesca.
    /// </summary>
    private async Task<IReadOnlyList<LoEscrito>> UnaPasadaDeRellenoAsync(CancellationToken ct)
    {
        if (_nota.Trim().Length == 0) return Array.Empty<LoEscrito>();

        var campos = CamposQueFaltan();
        if (campos.Count == 0) return Array.Empty<LoEscrito>();

        string cuerpo = JsonSerializer.Serialize(new
        {
            session_id = _sesion,
            transcript = "",
            sequence = ++_secuencia,
            // Solo emparejar: la nota ya está organizada y volver a pasarla por el organizador
            // podría reescribirla, que es justo lo que no debe pasar con una nota FIRMADA.
            stages = new { transcription = false, note = false, autofill = true },
            note = new { title = "Historia clínica", content = _nota },
            fields = campos.Select(Retratar),
            already_fulfilled = _yaPuesto.Select(p => new { stepOrder = p.Key, value = p.Value }),
            page_url = "sapgui://triage",
        });

        JsonElement res;
        try { res = await PedirAsync(cuerpo, ct); }
        catch (Exception e) { Cuenta?.Invoke($"No pude emparejar los campos: {e.Message}"); return Array.Empty<LoEscrito>(); }

        if (!res.TryGetProperty("autofill", out var relleno)
            || !relleno.TryGetProperty("matches", out var matches)
            || matches.ValueKind != JsonValueKind.Array)
            return Array.Empty<LoEscrito>();

        return EscribirLoEmparejado(matches, campos, ct);
    }

    private List<DetectedField> CamposQueFaltan() => _sap.ReadFields()
        .Where(f => f.Selector.Length > 0 && f.Label.Length > 0)
        .Where(f => f.ActionType is "input" or "select" or "click")
        .Where(f => !_yaPuesto.ContainsKey(f.StepOrder))
        .ToList();

    private object Retratar(DetectedField f) => new
    {
        stepOrder = f.StepOrder,
        actionType = f.ActionType,
        label = f.Label,
        selector = f.Selector,
        controlType = f.ControlType,
        allowedOptions = f.AllowedOptions?.Select(o => new { value = o.Value, label = o.Label }),
        currentValue = f.CurrentValue,
    };

    /// <summary>Escribe lo que el emparejador decidió, y devuelve solo lo que de verdad cuajó.</summary>
    private List<LoEscrito> EscribirLoEmparejado(JsonElement matches, List<DetectedField> campos,
        CancellationToken ct)
    {
        var porOrden = campos.ToDictionary(f => f.StepOrder);
        var lote = new List<LoEscrito>();

        foreach (var m in matches.EnumerateArray())
        {
            if (ct.IsCancellationRequested) break;
            if (!m.TryGetProperty("stepOrder", out var so) || !so.TryGetInt32(out int orden)) continue;
            if (!porOrden.TryGetValue(orden, out var campo)) continue;
            string valor = m.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? "" : "";
            if (valor.Length == 0) continue;

            var escrito = Escribir(campo, valor);
            if (escrito != null) { lote.Add(escrito); _yaPuesto[orden] = valor; }
        }
        return lote;
    }

    /// <summary>
    /// Escribe UN campo y comprueba que quedó escrito. Devuelve qué pasó, o null si no cuajó.
    /// </summary>
    /// <remarks>
    /// SE RELEE SIEMPRE, y no es desconfianza gratuita: `Execute` devuelve true en cuanto la
    /// asignación no lanza, y en SAP eso no significa que el valor esté puesto. Un campo de solo
    /// lectura lo acepta y lo ignora; uno con formato lo convierte; un radio necesita `Select()` y
    /// con la propiedad no se entera. Todo eso se ve releyendo y de ninguna otra forma.
    /// </remarks>
    private LoEscrito? Escribir(DetectedField campo, string valor)
    {
        string antes = LeerAhora(campo);
        var paso = PlanStep.ForAutofill(campo, new FieldMatch { StepOrder = campo.StepOrder, Value = valor });

        try
        {
            if (!_sap.Execute(paso, out string err))
            {
                LogBus.Log("dictado", $"«{campo.Label}» no se pudo escribir: {err}");
                return null;
            }
        }
        catch (Exception e)
        {
            LogBus.Log("dictado", $"«{campo.Label}» lanzó al escribir: {e.Message}");
            return null;
        }

        string despues = LeerAhora(campo);
        if (despues.Trim().Length == 0 && valor.Trim().Length > 0 && campo.ActionType == "input")
        {
            LogBus.Log("dictado", $"«{campo.Label}» aceptó «{valor}» pero quedó vacío: no se cuenta");
            return null;
        }

        LogBus.Log("dictado", $"«{campo.Label}» = «{despues}» (pedido «{valor}»)");
        return new LoEscrito(campo, antes, despues);
    }

    /// <summary>El valor de un campo, releído de SAP en este instante.</summary>
    private string LeerAhora(DetectedField campo)
    {
        try
        {
            var f = _sap.ReadFields().FirstOrDefault(x => x.Selector == campo.Selector);
            return f?.CurrentValue ?? "";
        }
        catch { return ""; }
    }

    /// <summary>
    /// Deshacer el último llenado. Existe porque esto escribe SIN pedir permiso: la salida de
    /// emergencia es lo que hace que eso sea aceptable.
    /// </summary>
    public int Deshacer()
    {
        int n = 0;
        foreach (var e in Enumerable.Reverse(_ultimoLote))
        {
            var paso = PlanStep.ForAutofill(e.Campo,
                new FieldMatch { StepOrder = e.Campo.StepOrder, Value = e.Antes });
            try { if (_sap.Execute(paso, out _)) { n++; _yaPuesto.Remove(e.Campo.StepOrder); } }
            catch { }
        }
        _ultimoLote.Clear();
        if (n > 0) Cuenta?.Invoke($"Deshecho: {n} campo(s) volvieron a su valor anterior.");
        return n;
    }

    private async Task<JsonElement> PedirAsync(string cuerpo, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"{_config.BaseUrl.TrimEnd('/')}/api/v1/pipeline")
        { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };
        req.Headers.Add("X-API-Key", _config.ApiKey ?? "");
        if (_config.OperatorEmail.Length > 0) req.Headers.Add("X-Miracle-User-Email", _config.OperatorEmail);

        using var res = await Http.SendAsync(req, ct);
        string texto = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"HTTP {(int)res.StatusCode}: {texto[..Math.Min(200, texto.Length)]}");
        return JsonDocument.Parse(texto).RootElement.Clone();
    }
}
