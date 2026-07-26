using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Navigation;

/// <summary>
/// El "motorcito" que encuentra la ruta dentro del sistema operativo: dado el ORIGIN donde nació un
/// workflow (p.ej. <c>uia://saplogon.exe</c>) y una señal en vivo de "dónde estoy" (el SurfaceLocator,
/// la URL de arriba-derecha), lleva la máquina hasta ese destino ANTES de que el workflow arranque.
/// Es el reemplazo evolutivo de <see cref="U.WindowsClient.Uia.AppAligner"/>: en vez de una sola
/// jugada hardcodeada, es una ESCALERA ordenada de rutas de intento (<see cref="INavStrategy"/>).
///
/// El contrato es simple: corre las estrategias en orden; tras cada intento espera a que el locator
/// CONFIRME que ya llegó; en cuanto una confirma, para y devuelve true. Si agota la escalera sin
/// confirmar, devuelve false (el player reporta el mismatch, como antes).
///
/// Diseño (por qué así):
///   - Determinista y rápido en la capa 1 (llegar a la APP): enfocar/lanzar no necesita un LLM, y el
///     camino subconsciente tiene que ser instantáneo. El LLM se reserva para la capa 2 —navegar
///     DENTRO de la app hasta la pantalla correcta— que será un rung superior de esta misma escalera.
///   - Se confirma SIEMPRE contra el locator, nunca contra el "issued" de la estrategia: lanzar por
///     shell puede "no tirar excepción" sin que la app abra; la única verdad es que el origin cambió.
///   - Firma de <see cref="EnsureAsync"/> compatible con el delegate <c>U.Graph.SurfaceAligner</c>:
///     es un drop-in donde antes iba <c>AppAligner.EnsureAsync</c>.
/// </summary>
public sealed class SurfaceNavigator
{
    private readonly IReadOnlyList<INavStrategy> _ladder;

    public SurfaceNavigator(params INavStrategy[] ladder) => _ladder = ladder;

    /// <summary>
    /// Escalera v1 (capa 1, llegar a la app): enfocar la instancia viva → lanzar por acceso directo del
    /// menú Inicio → lanzar por shell. Rungs futuros (capa 2, navegar dentro) se agregan aquí.
    /// </summary>
    public static SurfaceNavigator Default { get; } = new(
        new ShowDesktopStrategy(),   // el escritorio (uia://desktop) se alcanza con Win+D, no lanzando nada
        new FocusRunningStrategy(),
        new StartMenuLaunchStrategy(),
        new ShellLaunchStrategy());

    /// <summary>
    /// Alcanza <paramref name="targetOrigin"/> y confirma con <paramref name="currentOrigin"/>. Idempotente:
    /// si ya estamos ahí, no toca nada. Drop-in del delegate <c>SurfaceAligner</c> del WorkflowPlayer.
    /// </summary>
    public async Task<bool> EnsureAsync(string targetOrigin, Func<string> currentOrigin, CancellationToken ct)
    {
        var target = NavTarget.FromOrigin(targetOrigin);
        string current = currentOrigin();
        LogBus.Log("nav", $"EnsureAsync destino='{targetOrigin}' actual='{current}'");

        if (Matches(current, target.Origin)) { LogBus.Log("nav", "ya en destino (no toco nada)"); return true; }

        foreach (INavStrategy strategy in _ladder)
        {
            if (ct.IsCancellationRequested) return false;
            if (!strategy.CanAttempt(target)) continue;

            bool issued;
            try { issued = await strategy.AttemptAsync(target, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { LogBus.Log("nav", $"[{strategy.Name}] excepción: {ex.Message}"); continue; }

            if (!issued) { LogBus.Log("nav", $"[{strategy.Name}] no aplica ahora, siguiente ruta"); continue; }

            LogBus.Log("nav", $"[{strategy.Name}] intento hecho · esperando confirmación (≤{strategy.ConfirmWindow.TotalSeconds:0}s)");
            if (await ConfirmAsync(currentOrigin, target.Origin, strategy.ConfirmWindow, ct))
            {
                LogBus.Log("nav", $"[{strategy.Name}] confirmado en destino");
                return true;
            }
            LogBus.Log("nav", $"[{strategy.Name}] no confirmó, siguiente ruta");
        }

        LogBus.Log("nav", $"escalera agotada · actual='{currentOrigin()}' (esperaba '{target.Origin}')");
        return Matches(currentOrigin(), target.Origin);
    }

    /// <summary>Sondea el locator hasta que el origin coincida o se agote la ventana de la estrategia.</summary>
    private static async Task<bool> ConfirmAsync(Func<string> currentOrigin, string targetOrigin, TimeSpan window, CancellationToken ct)
    {
        int polls = Math.Max(1, (int)(window.TotalMilliseconds / 200));
        for (int i = 0; i < polls && !ct.IsCancellationRequested; i++)
        {
            if (Matches(currentOrigin(), targetOrigin)) return true;
            await Task.Delay(200, ct);
        }
        return Matches(currentOrigin(), targetOrigin);
    }

    /// <summary>Comparación de origin tolerante al slash final (misma regla que usaba AppAligner).</summary>
    private static bool Matches(string? current, string target) =>
        !string.IsNullOrWhiteSpace(current) &&
        string.Equals(current.TrimEnd('/'), target.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// El destino de navegación, parseado del origin del workflow. Autónomo a propósito (no depende de
/// AppAligner) para que el componente evolucione solo. <c>uia://proceso.exe/loquesea</c> →
/// Scheme="uia", Process="proceso".
/// </summary>
public sealed record NavTarget(string Origin, string Scheme, string Process)
{
    public bool IsUia => string.Equals(Scheme, "uia", StringComparison.OrdinalIgnoreCase);

    public static NavTarget FromOrigin(string origin)
    {
        string s = (origin ?? "").Trim();
        string scheme = "";
        int i = s.IndexOf("://", StringComparison.Ordinal);
        if (i >= 0) { scheme = s[..i]; s = s[(i + 3)..]; }
        string host = s.Split('/')[0];
        string proc = host.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? host[..^4] : host;
        return new NavTarget(origin ?? "", scheme, proc.Trim());
    }
}

/// <summary>
/// Una ruta de intento de la escalera. <see cref="CanAttempt"/> dice si es aplicable al destino;
/// <see cref="AttemptAsync"/> hace el efecto (enfocar/lanzar) y devuelve si REALMENTE actuó (false =
/// "esta ruta no aplica ahora, pasa a la siguiente"). La confirmación de llegada la hace el navegador
/// contra el locator, no la estrategia. <see cref="ConfirmWindow"/> es cuánto esperar esa confirmación
/// (enfocar es instantáneo; lanzar una app tarda).
/// </summary>
public interface INavStrategy
{
    string Name { get; }
    TimeSpan ConfirmWindow { get; }
    bool CanAttempt(NavTarget target);
    Task<bool> AttemptAsync(NavTarget target, CancellationToken ct);
}
