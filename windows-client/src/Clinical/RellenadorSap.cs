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

    /// <summary>
    /// SAP entero: nulo solo en un rellenador construido por su costura de prueba, que escribe y lee
    /// por <see cref="_ejecutar"/> y <see cref="_leer"/> y no tiene pantalla que recorrer.
    /// </summary>
    private readonly SapGuiSurface? _sap;

    /// <summary>Escribir un paso: (escribió, por qué no). Es <c>SapGuiSurface.Execute(paso, out err)</c>.</summary>
    private readonly Func<PlanStep, (bool Ok, string Error)> _ejecutar;

    /// <summary>Releer un campo por su selector. Es <c>SapGuiSurface.ValorActual(selector)</c>.</summary>
    private readonly Func<string, string?> _leer;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(45) };

    /// <summary>
    /// La pantalla donde esto tiene sentido. Se compara por PROGRAMA y número de dynpro, no por la
    /// dirección entera: el prefijo lleva el sistema («QAS») y la transacción, que cambian entre
    /// entornos sin que la pantalla sea otra.
    /// </summary>
    public static bool EsLaPantallaDeTriage(string ubicacion) =>
        ubicacion.StartsWith("sapgui://", StringComparison.OrdinalIgnoreCase)
        && ubicacion.Contains("SAPLY000", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Qué pantalla está mostrando SAP, preguntado a SAP MISMO — no dónde tiene puesta la
    /// atención el usuario.
    /// </summary>
    /// <remarks>
    /// La diferencia costó una exportación el 2026-08-25: el workflow llegó perfecto al formulario
    /// de Triage, pero el médico estaba mirando la página web para seguir el progreso, y el
    /// ejecutor —que preguntaba al locator, o sea al primer plano— creyó que el workflow había
    /// acabado en «web://…» y abortó con PANTALLA_INESPERADA. La escritura va por scripting COM,
    /// que no necesita el foco; la comprobación tiene que ir por el mismo canal que la escritura,
    /// o vigila una cosa distinta de la que protege.
    /// </remarks>
    public string DondeEstaSap() => Sap("preguntar qué pantalla muestra").Identity().Url;

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
        : this(config, paso => { bool ok = sap.Execute(paso, out string err); return (ok, err); }, sap.ValorActual)
    {
        _sap = sap;
    }

    /// <summary>
    /// LA COSTURA DE PRUEBA: el rellenador sin SAP, con «escribir» y «releer» de fuera. Existe para
    /// que el contrato juzgue la línea que <see cref="Escribir"/> DE VERDAD anota (promesa 396), y no
    /// una función aparte que nadie llama — un guardia que se cree puesto (aprendizaje nº18).
    /// </summary>
    /// <remarks>
    /// El constructor público pasa por aquí con <c>Execute</c> y <c>ValorActual</c> de SAP, así que la
    /// escritura y la relectura que se juzgan son las mismas que corren con el médico delante. Lo que
    /// esta costura no tiene es pantalla: <see cref="DondeEstaSap"/> y la lectura de campos lanzan
    /// diciendo qué se pidió (<see cref="Sap"/>).
    /// </remarks>
    internal RellenadorSap(GraphConfig config, Func<PlanStep, (bool Ok, string Error)> ejecutar,
        Func<string, string?> leer)
    {
        _config = config;
        _ejecutar = ejecutar;
        _leer = leer;
    }

    /// <summary>
    /// SAP entero, para lo que solo SAP sabe hacer. Un rellenador de la costura de prueba no lo tiene,
    /// y lo dice nombrando el paso en vez de un «Object reference not set» sin sitio (patrón nº2).
    /// </summary>
    private SapGuiSurface Sap(string para) => _sap
        ?? throw new InvalidOperationException(
            $"RellenadorSap de la costura de prueba: no tiene SAP para {para} (solo escribe y relee por fuera).");

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

        var pendientes = CamposQueFaltan()
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
        var campos = CamposQueFaltan();

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
            // El MISMO retrato que la otra pasada: dos formas de describir la misma pantalla se
            // desincronizan sin avisar, y lo enseñado llegaría por un camino y por el otro no.
            fields = campos.Select(Retratar),
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

    /// <summary>
    /// Lo que se le ofrece al emparejador: solo campos ESCRIBIBLES y con nombre de verdad.
    /// </summary>
    /// <remarks>
    /// Se filtra por tres cosas, y cada una salió de un fallo visto:
    ///   · `Editable`: «Fecha Crea» y «Hora Crea» son de solo lectura, el modelo las emparejaba y
    ///     SAP lanzaba al escribirlas (2026-08-14).
    ///   · Etiquetas que no nombran nada: en esta pantalla la casilla diastólica se llama «/», y
    ///     ofrecer un campo llamado «/» es invitar a que caiga ahí cualquier número suelto — pasó:
    ///     quedó con un 100 que nadie pidió.
    ///   · Los ya puestos, para no pisarlos ni gastar decisiones del modelo en ellos.
    /// </remarks>
    /// <summary>
    /// Lo que se le haya ENSEÑADO a Ü sobre un campo de esta pantalla, por su selector. Lo pone quien
    /// tiene el grafo delante (la carita); sin él, el rellenador se comporta como siempre.
    /// </summary>
    public Func<string, string>? RecuerdoDe { get; set; }

    /// <summary>La etiqueta útil que precede a cada campo, por StepOrder. Ver <c>CamposQueFaltan</c>.</summary>
    private readonly Dictionary<int, string> _etiquetaAnterior = new();

    private List<DetectedField> CamposQueFaltan()
    {
        var todos = Sap("leer los campos de la pantalla").ReadFields().Where(f => f.Selector.Length > 0).ToList();

        // QUIÉN NOMBRA A LA CASILLA DE AL LADO. Se recorre la pantalla EN ORDEN y cada campo se
        // queda con la última etiqueta que de verdad nombraba algo: así «/» sabe que va con
        // «Presión Arterial». Se calcula sobre TODOS los campos, antes de filtrar, porque el vecino
        // que da el nombre puede ser uno que después se descarte.
        _etiquetaAnterior.Clear();
        string ultimaUtil = "";
        foreach (var f in todos)
        {
            _etiquetaAnterior[f.StepOrder] = ultimaUtil;
            if (LoQueVeElEmparejador.EsUtil(f.Label)) ultimaUtil = f.Label.Trim();
        }

        return todos
            .Where(f => f.Editable)
            .Where(f => f.ActionType is "input" or "select" or "click")
            .Where(f => LoQueVeElEmparejador.MereceOfrecerse(
                f.Label, _etiquetaAnterior.GetValueOrDefault(f.StepOrder, ""), Ensenado(f)))
            .Where(f => !_yaPuesto.ContainsKey(f.StepOrder))
            .ToList();
    }

    private string Ensenado(DetectedField f)
    {
        try { return RecuerdoDe?.Invoke(f.Selector) ?? ""; }
        catch (Exception e) { LogBus.Log("dictado", $"no pude leer lo enseñado de «{f.Label}»: {e.Message}"); return ""; }
    }

    private object Retratar(DetectedField f) => new
    {
        stepOrder = f.StepOrder,
        actionType = f.ActionType,
        // LA ETIQUETA ES EL CANAL (promesas 115 y 116): el emparejador de Graph descarta cualquier
        // propiedad que no esté en su lista, así que una pista mandada aparte no llegaría al modelo.
        label = LoQueVeElEmparejador.EtiquetaCon(
            f.Label, _etiquetaAnterior.GetValueOrDefault(f.StepOrder, ""), Ensenado(f)),
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
            var (escribio, err) = _ejecutar(paso);
            if (!escribio)
            {
                LogBus.Log("dictado", $"«{campo.Label}» no se pudo escribir: {err}");
                return null;
            }
        }
        catch (Exception e)
        {
            // LA CADENA ENTERA, y no solo el mensaje de arriba (patrón nº3). El 2026-09-02 «Frec.
            // Cardíaca» falló dos veces con un «Object reference not set» que no decía de dónde
            // salía, y un catch mudo convierte un bug concreto en «SAP lo rechazó».
            var porque = new System.Text.StringBuilder();
            for (var x = e; x != null; x = x.InnerException)
                porque.Append(porque.Length > 0 ? " ← " : "").Append($"{x.GetType().Name}: {x.Message}");
            LogBus.Log("dictado", $"«{campo.Label}» lanzó al escribir: {porque}"
                + $" · en {e.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}");
            return null;
        }

        string despues = LeerAhora(campo);
        if (despues.Trim().Length == 0 && valor.Trim().Length > 0 && campo.ActionType == "input")
        {
            LogBus.Log("dictado", LineaDeVacio(campo.Label, valor));
            return null;
        }

        LogBus.Log("dictado", LineaDeEscrito(campo.Label, valor, despues));
        return new LoEscrito(campo, antes, despues);
    }

    /// <summary>
    /// La línea de un campo escrito: su etiqueta, la forma de lo releído y si coincide con lo pedido.
    /// NUNCA el valor (spec 051, promesa 396).
    /// </summary>
    /// <remarks>
    /// Esto anotaba «= «38,5» (pedido «38.5»)»: el signo vital del paciente, en el log local — y el log
    /// entero salía del equipo hacia el backend por el espejo. Lo que aquella línea servía para ver
    /// era si SAP aceptó el valor tal cual, lo recortó o lo convirtió; eso lo dice
    /// <see cref="SinValor.Contraste"/> con las dos longitudes, sin ninguno de los dos valores.
    /// </remarks>
    internal static string LineaDeEscrito(string etiqueta, string pedido, string leido) =>
        $"«{etiqueta}» = {SinValor.Contraste(pedido, leido)}";

    /// <summary>
    /// La línea de un campo que aceptó la escritura y al releerlo estaba vacío: su etiqueta y la forma
    /// de lo pedido. NUNCA el valor (spec 051, promesa 396).
    /// </summary>
    internal static string LineaDeVacio(string etiqueta, string pedido) =>
        $"«{etiqueta}» aceptó {SinValor.Forma(pedido)} pero quedó vacío: no se cuenta";

    /// <summary>
    /// El valor de un campo, releído de SAP en este instante — YENDO AL NODO, no recorriendo todo.
    /// </summary>
    /// <remarks>
    /// Esto usaba `ReadFields()`, que recorre el árbol entero de la pantalla, y se llamaba DOS VECES
    /// por campo (antes y después). Rellenar seis campos tardaba un minuto entero, con esperas de
    /// hasta 14 segundos entre uno y otro; el mismo trabajo por VBS, yendo directo al nodo, tardaba
    /// dos segundos (2026-08-14, lo sufrió el usuario mirando la pantalla).
    /// </remarks>
    private string LeerAhora(DetectedField campo) => _leer(campo.Selector) ?? "";

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
            try { if (_ejecutar(paso).Ok) { n++; _yaPuesto.Remove(e.Campo.StepOrder); } }
            catch (Exception ex)
            {
                // Era un «catch { }»: un deshacer que fallaba solo se notaba en que el recuento salía
                // corto, sin decir qué campo ni por qué (patrón nº3). La cadena de tipos y mensajes, como
                // en Escribir; nunca e.Antes, que es el valor que había en el campo (spec 051).
                var porque = new System.Text.StringBuilder();
                for (var x = ex; x != null; x = x.InnerException)
                    porque.Append(porque.Length > 0 ? " ← " : "").Append($"{x.GetType().Name}: {x.Message}");
                LogBus.Log("dictado", $"«{e.Campo.Label}» lanzó al deshacer: {porque}");
            }
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
