using U.WindowsClient.Actions;
using U.WindowsClient.Backend;
using U.WindowsClient.Capture;
using U.WindowsClient.Domain;
using U.WindowsClient.Mcp;
using U.WindowsClient.Uia;

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

    /// <summary>Ejecuta un objetivo hasta que el cerebro devuelve el control con texto. Devuelve ese resumen.</summary>
    public async Task<string> RunAsync(string goal, CancellationToken ct)
    {
        _voice.Narrate($"¡Vamos! {goal}");
        string? session = null;
        string[] results = Array.Empty<string>();
        string? inform = null; // respuesta pendiente a una pregunta del asistente (ask_user)
        string summary = "";
        bool wantShot = false;
        int actions = 0;

        for (int turn = 0; turn < _maxTurns && !ct.IsCancellationRequested; turn++)
        {
            // 1) Capturar el estado (texto por UIA; screenshot solo si el cerebro lo pidió).
            var state = await ReadStateAsync(wantShot);

            // 2) Pedir la decisión al backend.
            var req = new TurnRequest
            {
                Session = session,
                Goal = session == null ? goal : null,
                State = state,
                Results = results,
                Inform = inform,
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
                return $"error de backend: {e.Message}";
            }

            session = resp.Session;
            wantShot = resp.NeedsScreenshot;

            if (!string.IsNullOrWhiteSpace(resp.Narration)) _voice.Narrate(resp.Narration);
            if (!string.IsNullOrWhiteSpace(resp.Speech)) _voice.Speak(resp.Speech!);
            if (!string.IsNullOrWhiteSpace(resp.Text)) summary = resp.Text;
            if (resp.Done) break;

            // 3) Ejecutar las acciones que decidió el cerebro.
            var outResults = new List<string>();
            for (int i = 0; i < resp.Actions.Count; i++)
            {
                if (ct.IsCancellationRequested) break;
                if (i < resp.Intents.Count && !string.IsNullOrWhiteSpace(resp.Intents[i]))
                    _voice.Narrate(resp.Intents[i]);
                outResults.Add(await ExecuteAsync(resp.Actions[i], ct));
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
        return string.IsNullOrWhiteSpace(summary) ? "Hecho" : summary;
    }

    private async Task<ScreenState> ReadStateAsync(bool withScreenshot)
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
        if (withScreenshot)
            state.Screenshot = await Task.Run(Screenshotter.CaptureBase64Png);
        return state;
    }

    private async Task<string> ExecuteAsync(AgentAction a, CancellationToken ct)
    {
        // Workflows (subconsciente invocado desde el consciente): el cerebro inyectó workflow_id en
        // los args; se ejecutan con el WorkflowPlayer, no con el registro MCP local.
        if (a.Kind == "mcp" && _workflows != null &&
            (a.Tool ?? "").StartsWith("workflow_", StringComparison.OrdinalIgnoreCase))
        {
            string id = a.Args != null && a.Args.TryGetValue("workflow_id", out var wid) ? wid : "";
            string context = a.Args != null && a.Args.TryGetValue("context", out var c) ? c : "";
            return await _workflows.RunAsync(id, context, ct);
        }

        return a.Kind switch
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
    }

    private static async Task<string> WaitAsync(int ms, CancellationToken ct)
    {
        await Task.Delay(Math.Clamp(ms, 0, 10000), ct);
        return "ok";
    }
}
