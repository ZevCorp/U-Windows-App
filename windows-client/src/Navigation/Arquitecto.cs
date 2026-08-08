using System.IO;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Navigation;

/// <summary>
/// EL ARQUITECTO, lanzado desde la app: un agente Claude que navega la aplicación y contrasta la
/// jerarquía real con la que el grafo está construyendo.
///
/// Vive en Node (<c>agente-arquitecto\arquitecto.mjs</c>) y no aquí dentro por una razón práctica:
/// el Agent SDK de Claude es JavaScript. Esta clase es el puente — encuentra el script, lo lanza
/// sobre la app pedida y va contando lo que hace, para que desde la app se vea igual que se ve un
/// mapeo mecánico.
///
/// Actúa por la MISMA sonda MCP local que usa el asistente (127.0.0.1:8791), así que no hay un
/// segundo camino de pulsar cosas: lo que el arquitecto puede hacer es exactamente lo que el
/// asistente puede hacer, ni más ni menos. Por eso exige <c>U_MCP_PROBE=1</c> — sin sonda no tiene
/// brazos, y es mejor decirlo que fingir que se está mapeando (2026-08-08).
/// </summary>
public static class Arquitecto
{
    /// <summary>
    /// ¿Puede correr? Devuelve el porqué cuando no.
    ///
    /// Y lo ESCRIBE EN EL LOG, no solo lo devuelve: la primera vez esto dijo que no —el registro
    /// de versiones se había quedado sin apuntar dónde vive el repo— el aviso murió en la barra de
    /// estado, el mapeo lo hizo el recorredor mecánico, y desde fuera se vio como «el arquitecto
    /// mapea igual de plano que el crawler». Un motivo que nadie lee es un motivo perdido
    /// (2026-08-08, diagnosticado en los logs de la prueba del usuario).
    /// </summary>
    public static (bool Puede, string Porque) Disponible()
    {
        var (puede, porque) = Comprobar();
        if (!puede) LogBus.Log("arquitecto", "NO puede correr: " + porque);
        return (puede, porque);
    }

    private static (bool, string) Comprobar()
    {
        if (Environment.GetEnvironmentVariable("U_MCP_PROBE") != "1")
            return (false, "la sonda MCP local no está encendida (U_MCP_PROBE=1): sin ella el arquitecto no tiene con qué actuar");
        string s = Script();
        if (s.Length == 0)
            return (false, NucleoVersiones.Repo().Length == 0
                ? "el registro de versiones no dice dónde vive el repo: corre una vez scripts\\version-nucleo.ps1 -Construir"
                : "no encuentro agente-arquitecto\\arquitecto.mjs bajo el repo apuntado en el registro");
        if (!Directory.Exists(Path.Combine(Path.GetDirectoryName(s)!, "node_modules")))
            return (false, "faltan las dependencias del arquitecto: corre «npm install» en agente-arquitecto\\");
        return (true, "");
    }

    private static string Script()
    {
        string repo = NucleoVersiones.Repo();
        if (repo.Length == 0) return "";
        string s = Path.Combine(repo, "agente-arquitecto", "arquitecto.mjs");
        return File.Exists(s) ? s : "";
    }

    /// <summary>
    /// Suelta al arquitecto sobre una app. <paramref name="cuenta"/> recibe cada línea suya según
    /// ocurre —sus decisiones y sus llamadas— porque una auditoría que solo se ve al final no deja
    /// pararla cuando va por mal camino.
    /// </summary>
    public static async Task<string> AuditarAsync(string app, int turnos, Action<string> cuenta, CancellationToken ct)
    {
        var (puede, porque) = Disponible();
        if (!puede) return porque;

        string script = Script();
        var psi = new System.Diagnostics.ProcessStartInfo("node")
        {
            Arguments = $"\"{script}\" \"{app}\" {turnos}",
            WorkingDirectory = Path.GetDirectoryName(script)!,
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };

        LogBus.Log("arquitecto", $"lanzando sobre «{app}» con {turnos} turno(s)");
        try
        {
            using var p = System.Diagnostics.Process.Start(psi);
            if (p == null) return "no pude lanzar node: ¿está instalado y en el PATH?";

            // UNA CONSOLA DE VERDAD, EN VIVO. El agente corría con CreateNoWindow y su salida solo
            // llegaba al log: para saber si estaba pensando, atascado o muerto había que abrir el
            // visor y refrescar. Un agente que razona durante minutos y no se ve por ningún sitio se
            // parece demasiado a uno colgado (2026-08-08, pedido por el usuario).
            //
            // No se quita el redirigido para poner la ventana del hijo: la app CONSUME esa salida
            // —Contar() alimenta el contador de turnos y el estado— y perderla para ganar una
            // consola sería cambiar información por decorado. Se abre una consola propia y se
            // escribe en las dos.
            ConsolaViva.Abrir($"arquitecto · {app}");
            p.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not { Length: > 0 }) return;
                ConsolaViva.Escribir(e.Data);
                Contar(e.Data, cuenta);
            };
            p.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not { Length: > 0 }) return;
                ConsolaViva.Escribir("! " + e.Data);
                LogBus.Log("arquitecto", "! " + e.Data);
            };
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            // El token para de verdad: sin esto, «detener» dejaría al agente navegando la máquina
            // de alguien mientras la app cree que ya paró.
            using var reg = ct.Register(() => { try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { } });
            await p.WaitForExitAsync(CancellationToken.None);

            if (ct.IsCancellationRequested) return "arquitecto detenido";
            return p.ExitCode == 0
                ? $"arquitecto: auditoría de «{app}» terminada; su informe está en {Informe(app)}"
                : $"el arquitecto terminó con código {p.ExitCode}; mira el log de «arquitecto»";
        }
        catch (Exception e)
        {
            LogBus.Log("arquitecto", $"falló: {e.Message}");
            return $"el arquitecto no pudo correr: {e.Message}";
        }
    }

    /// <summary>Dónde deja escrito lo que encuentre (lo escribe la sonda, ver map_feedback).</summary>
    public static string Informe(string app) =>
        Path.Combine(NucleoVersiones.Raiz, "feedback-arquitecto", $"{app.Replace(".exe", "")}.md");

    /// <summary>
    /// Traduce su salida a algo que se pueda seguir de un vistazo. El script imprime sus
    /// razonamientos con «[arquitecto]» y sus llamadas con «→»; ambas cosas interesan, pero en la
    /// barra de estado solo cabe una línea, así que al log va todo y arriba lo último.
    /// </summary>
    private static void Contar(string linea, Action<string> cuenta)
    {
        LogBus.Log("arquitecto", linea);
        string l = linea.Trim();
        if (l.StartsWith("[arquitecto]", StringComparison.Ordinal))
            cuenta(l["[arquitecto]".Length..].Trim());
        else if (l.StartsWith("→", StringComparison.Ordinal))
            cuenta(l);
    }
}
