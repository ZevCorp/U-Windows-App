using U.Graph;
using U.WindowsClient.Actions;
using U.WindowsClient.Backend;
using U.WindowsClient.Capture;
using U.WindowsClient.Diagnostics;
using U.WindowsClient.Domain;
using U.WindowsClient.Mcp;
using U.WindowsClient.Telemetry;
using U.WindowsClient.Uia;
using System.Globalization;

namespace U.WindowsClient.Agent;

/// <summary>Canal de voz/narración del asistente hacia el usuario (lo implementa la UI).</summary>
public interface IVoice
{
    void Narrate(string text);
    void Speak(string text);
}

/// <summary>El asistente puede preguntar algo al usuario (respuesta por texto o voz).</summary>
public interface IUserChannel
{
    Task<string> AskAsync(string question, CancellationToken ct);
}

/// <summary>
/// El bucle de ejecución del lado cliente. Es el gemelo de <c>core/application/Engine.kt</c>, pero
/// donde aquel llamaba a un <c>Brain</c> local, este hace <c>POST /api/agent/turn</c> al backend.
/// El cliente CONDUCE el bucle (capturar → pedir decisión → ejecutar → repetir) y el cerebro remoto
/// solo decide. Toda la inteligencia está del otro lado del cable.
/// </summary>
public sealed class AgentLoop
{
    private readonly BackendClient _backend;
    private readonly UiaReader _uia;
    private readonly LocalMcp _mcp;
    private readonly IVoice _voice;
    private readonly IUserChannel _user;
    private readonly Func<string[]> _installedApps;
    private readonly Func<SurfaceLocator.SurfaceLocation?>? _surface;
    private readonly WorkflowMcpRunner? _workflows;
    private readonly SapContextReader _sapContext = new();
    private readonly int _maxTurns;

    public AgentLoop(BackendClient backend, UiaReader uia, LocalMcp mcp, IVoice voice, IUserChannel user,
        Func<string[]> installedApps, Func<SurfaceLocator.SurfaceLocation?>? surface = null,
        WorkflowMcpRunner? workflows = null, int maxTurns = 40)
    {
        _backend = backend;
        _uia = uia;
        _mcp = mcp;
        _voice = voice;
        _user = user;
        _installedApps = installedApps;
        _surface = surface;
        _workflows = workflows;
        _maxTurns = maxTurns;
    }

    /// <summary>
    /// Acciones que van a la VENTANA EN PRIMER PLANO, no a un elemento resuelto: si el foco cambia, se
    /// ejecutan sobre la aplicación equivocada. Son las que pasan por la compuerta de superficie.
    /// <c>mcp</c> y <c>wait</c> no entran: no tocan la pantalla.
    /// </summary>
    private static readonly HashSet<string> ForegroundActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "tap", "type", "scroll", "swipe", "key",
    };

    /// <summary>
    /// Ejecuta un objetivo hasta que el cerebro devuelve el control con texto. Devuelve ese resumen.
    ///
    /// <paramref name="requireOrigin"/> es la COMPUERTA DE SUPERFICIE: el origen (<c>sapgui://QAS</c>,
    /// <c>uia://saplogon.exe</c>) fuera del cual este objetivo no puede teclear ni clicar. Vacío =
    /// sin compuerta (el modo libre de la carita: el usuario pide algo y el destino es cualquiera).
    ///
    /// POR QUÉ EXISTE (2026-07-26): un workflow de SAP se detuvo, el puente consciente entregó el
    /// control aquí con el encargo de «retoma desde la pantalla actual», y el cerebro hizo lo lógico —
    /// teclear el código de transacción. Para cuando lo tecleó, el foco ya no era SAP: el texto y el
    /// Enter acabaron en otra aplicación. El WorkflowPlayer lleva esta comprobación desde el commit
    /// 862f59b y se negó dos veces a actuar sobre la pantalla equivocada, en la misma corrida; el
    /// consciente, que actúa a coordenadas sobre el foreground, no la tenía. Se arregla la CLASE de
    /// error, no el caso: cualquier actor que teclee tiene que saber sobre qué está tecleando.
    /// </summary>
    public async Task<string> RunAsync(string goal, CancellationToken ct, string requireOrigin = "")
    {
        // LO QUE SE NARRA ES QUE EMPEZAMOS, NO EL ENCARGO ENTERO. Desde que narrar se OYE
        // (promesa 142), soltar aquí el objetivo tal cual hacía que Ü leyera en voz alta los 4442
        // caracteres del encargo de comprobar, superficies y URLs incluidas — 2026-09-03 21:35:12,
        // insufrible. El objetivo entero sigue yendo al log, que es donde sirve.
        _voice.Narrate(goal.Length > 90 ? "¡Vamos!" : $"¡Vamos! {goal}");
        LogBus.Log("agent", $"▶ objetivo: «{Short(goal, 160)}»" +
            (requireOrigin.Length > 0 ? $" · compuerta: solo actúa en «{requireOrigin}»" : " · SIN compuerta de superficie"));
        // Telemetría "Windows Live": esta corrida consciente entera se correlaciona por runId.
        // El objetivo sube por su FORMA: con el dictado de respaldo el objetivo ES la frase dicha
        // (FaceWindow.xaml.cs:2607), y aquí salía entero hacia el panel (spec 051, S2).
        string runId = TelemetryBus.NewRunId();
        TelemetryBus.Emit("conscious_run_start", runId: runId, label: SinValor.Forma(goal));
        string? session = null;
        string[] results = Array.Empty<string>();
        string? inform = null; // respuesta pendiente a una pregunta del asistente (ask_user)
        string summary = "";
        bool wantShot = false;
        int actions = 0;

        for (int turn = 0; turn < _maxTurns && !ct.IsCancellationRequested; turn++)
        {
            // 1) Capturar el estado (texto por UIA; screenshot solo si el cerebro lo pidió).
            var state = await ReadStateAsync(wantShot, runId);

            // 2) Pedir la decisión al backend.
            var req = new TurnRequest
            {
                Session = session,
                Goal = session == null ? goal : null,
                State = state,
                Results = results,
                Inform = inform,
                Timezone = ZonaIanaLocal(),
                Locale = CultureInfo.CurrentCulture.Name.StartsWith("es", StringComparison.OrdinalIgnoreCase)
                    ? CultureInfo.CurrentCulture.Name : "es-CO",
                ClientNowUtc = DateTimeOffset.UtcNow.ToString("O"),
            };
            inform = null;
            TurnResponse resp;
            try
            {
                resp = await _backend.TurnAsync(req, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                _voice.Speak("No pude contactar con el cerebro. Revisa la conexión.");
                LogBus.Log("agent", $"✗ el cerebro no respondió: {e.Message}");
                // Al panel, el TIPO del error y no su mensaje: el de BackendClient lleva el cuerpo entero
                // de la respuesta (BackendClient.cs:117). El mensaje queda en la línea de arriba, en local
                // (spec 051, S3).
                TelemetryBus.Emit("conscious_run_end", phase: "error", runId: runId, label: e.GetType().Name);
                return $"error de backend: {e.Message}";
            }

            session = resp.Session;
            wantShot = resp.NeedsScreenshot;

            if (!string.IsNullOrWhiteSpace(resp.Narration)) _voice.Narrate(resp.Narration);
            if (!string.IsNullOrWhiteSpace(resp.Speech)) _voice.Speak(resp.Speech!);
            if (!string.IsNullOrWhiteSpace(resp.Text)) summary = resp.Text;
            if (resp.Done) break;

            // 3) Ejecutar las acciones que decidió el cerebro.
            if (resp.Actions.Count > 0)
                LogBus.Log("agent", $"turno {turn + 1}: {resp.Actions.Count} acción(es) · aquí='{Here()}'");

            var outResults = new List<string>();
            for (int i = 0; i < resp.Actions.Count; i++)
            {
                if (ct.IsCancellationRequested) break;
                if (i < resp.Intents.Count && !string.IsNullOrWhiteSpace(resp.Intents[i]))
                    _voice.Narrate(resp.Intents[i]);
                outResults.Add(await ExecuteAsync(resp.Actions[i], ct, runId, requireOrigin));
                actions++;
                if (resp.Actions.Count > 1) await Task.Delay(350, ct);
            }
            results = outResults.ToArray();

            // 4) Si preguntó algo, resolverlo antes del próximo turno. La respuesta viaja en `inform`
            //    (no en `results`): el backend la enruta al output del ask_user pendiente.
            if (!string.IsNullOrWhiteSpace(resp.Question))
            {
                _voice.Speak(resp.Question!);
                string answer = await _user.AskAsync(resp.Question!, ct);
                inform = string.IsNullOrWhiteSpace(answer) ? "usa tu mejor criterio" : answer;
            }

            await Task.Delay(300, ct);
        }

        if (!string.IsNullOrWhiteSpace(summary)) _voice.Speak(summary);
        else if (actions == 0)
        {
            summary = "Mmm, no estoy seguro de haberte entendido. ¿Me lo dices de otra forma?";
            _voice.Speak(summary);
        }
        _voice.Narrate("¡Listo! 🎉");
        LogBus.Log("agent", $"■ fin · {actions} acción(es) · {Short(summary, 160)}");
        // Lo que Ü contestó sube por su forma: repite lo pedido y lo leído de la pantalla (spec 051, S4).
        TelemetryBus.Emit("conscious_run_end", runId: runId, label: SinValor.Forma(summary));
        return string.IsNullOrWhiteSpace(summary) ? "Hecho" : summary;
    }

    /// <summary>La superficie en primer plano AHORA, para el registro y la compuerta. "" si no se sabe.</summary>
    private string Here()
    {
        try { return _surface?.Invoke()?.Id ?? ""; } catch { return ""; }
    }

    private string HereOrigin()
    {
        try { return _surface?.Invoke()?.Origin ?? ""; } catch { return ""; }
    }

    private static string Short(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "…");

    private static string ZonaIanaLocal()
    {
        try
        {
            string windowsId = TimeZoneInfo.Local.Id;
            if (TimeZoneInfo.TryConvertWindowsIdToIanaId(windowsId, out string? iana) && !string.IsNullOrWhiteSpace(iana))
                return iana;
            return windowsId;
        }
        catch { return "UTC"; }
    }

    private async Task<ScreenState> ReadStateAsync(bool withScreenshot, string runId = "")
    {
        // UIA puede bloquear; se corre fuera del hilo de UI.
        var state = await Task.Run(() => _uia.Read());
        state.Apps = _installedApps();
        // El "URL de Windows": con esto el cerebro scopea qué workflows declara por MCP este turno.
        var loc = _surface?.Invoke();
        if (loc != null)
        {
            state.SurfaceId = loc.Id;
            state.SurfaceOrigin = loc.Origin;
            state.SurfacePathname = loc.Path;
        }
        // Telemetría: "analiza la pantalla" (pulso del consciente hacia el nodo Analizar).
        TelemetryBus.Emit("analyze", runId: runId,
            appId: loc != null ? AppAligner.ProcessFromOrigin(loc.Origin) : "",
            surfaceUrl: loc?.Id ?? "");

        // SAP GUI Scripting SE AÑADE al árbol de lectura (UIA apenas ve dentro de SAP): si la app en
        // foco es SAP, el cerebro recibe además los campos reales de la pantalla SAP.
        string proc = loc != null ? AppAligner.ProcessFromOrigin(loc.Origin) : "";
        if (proc.StartsWith("sap", StringComparison.OrdinalIgnoreCase))
        {
            string? sap = await Task.Run(() => _sapContext.Read());
            if (!string.IsNullOrWhiteSpace(sap))
                state.UiContext = $"{state.UiContext}\n\n{sap}";
        }

        if (withScreenshot)
            state.Screenshot = await Task.Run(Screenshotter.CaptureBase64Png);
        return state;
    }

    private async Task<string> ExecuteAsync(AgentAction a, CancellationToken ct, string runId = "",
        string requireOrigin = "")
    {

        if (requireOrigin.Length > 0 && ForegroundActions.Contains(a.Kind))
        {
            string here = HereOrigin();
            if (!string.Equals(here, requireOrigin, StringComparison.OrdinalIgnoreCase))
            {
                string why = $"acción «{a.Kind}» NO ejecutada: el primer plano es "
                    + $"«{(here.Length > 0 ? here : "desconocido")}» y esta tarea es de «{requireOrigin}». "
                    + "Trae esa aplicación al frente antes de volver a intentarlo.";
                LogBus.Log("agent", $"✋ {why}"
                    + (a.Kind == "type" ? $" · texto descartado='{Short(a.Text, 40)}'" : ""));
                return why;
            }
        }

        // Workflows (subconsciente invocado desde el consciente): el cerebro inyectó workflow_id en
        // los args; se ejecutan con el WorkflowPlayer, no con el registro MCP local. La telemetría del
        // workflow la emite WorkflowMcpRunner (workflow_start/step/end), no aquí.
        if (a.Kind == "mcp" && _workflows != null &&
            (a.Tool ?? "").StartsWith("workflow_", StringComparison.OrdinalIgnoreCase))
        {
            string id = a.Args != null && a.Args.TryGetValue("workflow_id", out var wid) ? wid : "";
            string context = a.Args != null && a.Args.TryGetValue("context", out var c) ? c : "";
            return await _workflows.RunAsync(id, context, ct);
        }

        // Telemetría: acción en pantalla (pulso del consciente hacia el nodo Clic) o consulta MCP.
        if (a.Kind == "mcp")
            TelemetryBus.Emit("mcp", runId: runId, label: a.Tool ?? "");
        else if (a.Kind is "tap" or "type" or "scroll" or "swipe" or "key")
            TelemetryBus.Emit("action", runId: runId, label: a.Kind, detail: new { x = a.X, y = a.Y });

        // La superficie se lee ANTES de actuar: si la acción navega o cambia de app, leerla después
        // diría a dónde fuimos a parar, no sobre qué se actuó — que es justo lo que hay que auditar.
        string where = Here();

        string result = a.Kind switch
        {
            "tap" => InputExecutor.Tap(a.X, a.Y) ? "ok" : "no se pudo ejecutar la acción",
            "type" => InputExecutor.Type(a.X, a.Y, a.Text ?? "") ? "ok" : "no se pudo ejecutar la acción",
            "scroll" => InputExecutor.Scroll(a.Down) ? "ok" : "no se pudo ejecutar la acción",
            "swipe" => InputExecutor.Swipe(a.X1, a.Y1, a.X2, a.Y2, a.Ms) ? "ok" : "no se pudo ejecutar la acción",
            "key" => InputExecutor.Key(a.Key ?? "") ? "ok" : "no se pudo ejecutar la acción",
            "wait" => await WaitAsync(a.Ms, ct),
            "mcp" => _mcp.Call(a.Tool ?? "", a.Args ?? new Dictionary<string, string>()),
            _ => $"acción desconocida: {a.Kind}",
        };

        // Para una acción mcp, result es la respuesta del mapa, que cita lo escrito («escribí «…»»,
        // SurfaceMapTools.RelatoDeEscribir): se tapa ANTES de recortar, o un valor partido por el corte
        // saldría a medias y entero (promesa 397).
        LogBus.Log("agent", $"{Describe(a)} en '{where}' → {Short(SinValor.Tapar(result, LoQueEscribe(a)), 120)}");
        return result;
    }

    /// <summary>Lo que una acción trae para escribir: el texto de un type y los valores de una llamada mcp.</summary>
    private static string?[] LoQueEscribe(AgentAction a) =>
        SurfaceMapTools.ValoresEscritos(a.Args).Append(a.Text).ToArray();

    /// <summary>
    /// Una acción en una línea legible. El texto de un `type` va por su LONGITUD, no por su valor.
    /// </summary>
    /// <remarks>
    /// Hasta el 2026-09-24 este comentario decía que el texto recortado «queda solo en el log LOCAL, nunca
    /// sale hacia Graph». Era falso desde el 2026-08-16: el espejo (EspejoDelLog) subía cada línea del log
    /// al backend, y esta con ella. Un guardia que se cree puesto (aprendizaje nº18), y en forma de
    /// comentario. El incidente del 2026-07-26 necesitaba «qué y dónde»; queda «dónde y cuánto», y lo
    /// escrito sigue donde vive: en la pantalla que se escribió (spec 051, decisión 2).
    /// </remarks>
    private static string Describe(AgentAction a) => a.Kind switch
    {
        "tap" => $"tap ({a.X},{a.Y})",
        "type" => $"type ({a.X},{a.Y}) {SinValor.Forma(a.Text)}",
        "key" => $"key «{a.Key}»",
        "scroll" => $"scroll {(a.Down ? "abajo" : "arriba")}",
        "swipe" => $"swipe ({a.X1},{a.Y1})→({a.X2},{a.Y2})",
        "wait" => $"wait {a.Ms} ms",
        "mcp" => $"mcp {a.Tool}",
        _ => a.Kind,
    };

    private static async Task<string> WaitAsync(int ms, CancellationToken ct)
    {
        await Task.Delay(Math.Clamp(ms, 0, 10000), ct);
        return "ok";
    }
}
