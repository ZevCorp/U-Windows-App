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

        // EN UNA CONSOLA VISIBLE, y no escondido. Ver al agente decidir mientras decide es lo que
        // permite pararlo cuando va por mal camino; con la salida solo en el log, para cuando se
        // lee ya terminó (2026-08-08, pedido por el usuario). Se hace con Tee-Object: la consola
        // enseña y el archivo deja que la app siga narrando en su barra y en su log — una salida,
        // dos lectores, sin duplicar nada.
        string salida = Path.Combine(NucleoVersiones.Raiz, $"arquitecto-{app.Replace(".exe", "")}.log");
        try { if (File.Exists(salida)) File.Delete(salida); } catch { }

        string comando =
            $"& node \"{script}\" \"{app}\" {turnos} 2>&1 | Tee-Object -FilePath \"{salida}\"; "
            + "Write-Host ''; Read-Host 'Terminó. Pulsa Enter para cerrar esta ventana'";
        var psi = new System.Diagnostics.ProcessStartInfo("powershell")
        {
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{comando.Replace("\"", "\\\"")}\"",
            WorkingDirectory = Path.GetDirectoryName(script)!,
            UseShellExecute = true,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal,
        };

        LogBus.Log("arquitecto", $"lanzando sobre «{app}» con {turnos} turno(s); su consola queda abierta");
        try
        {
            using var p = System.Diagnostics.Process.Start(psi);
            if (p == null) return "no pude lanzar la consola del arquitecto";
            using var reg = ct.Register(() => { try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { } });

            // Se sigue el archivo, no el proceso: la consola se queda abierta a propósito para
            // poder leerla, así que esperar a que muera sería esperar a que el usuario la cierre.
            // Lo que dice que terminó es la marca [[FIN]] que el propio agente imprime al final.
            bool termino = await SeguirSalidaAsync(salida, p, cuenta, ct);

            if (ct.IsCancellationRequested) return "arquitecto detenido";
            if (!termino) return "el arquitecto se cortó sin terminar; mira su consola y el log de «arquitecto»";
            return $"arquitecto: auditoría de «{app}» terminada; su informe está en {Informe(app)}";
        }
        catch (Exception e)
        {
            LogBus.Log("arquitecto", $"falló: {e.Message}");
            return $"el arquitecto no pudo correr: {e.Message}";
        }
    }

    /// <summary>
    /// Va leyendo lo que el agente escribe y lo cuenta según llega. Devuelve true si vio la marca
    /// de final; false si el proceso murió antes o se canceló.
    /// </summary>
    private static async Task<bool> SeguirSalidaAsync(string salida, System.Diagnostics.Process p,
        Action<string> cuenta, CancellationToken ct)
    {
        long leido = 0;
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(600, CancellationToken.None);
            try
            {
                if (!File.Exists(salida))
                {
                    // Ni archivo ni proceso: algo murió antes de empezar a escribir.
                    if (p.HasExited) return false;
                    continue;
                }
                // Compartido para lectura Y escritura: el otro proceso lo tiene abierto.
                using var fs = new FileStream(salida, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (fs.Length <= leido) { if (p.HasExited) return false; continue; }
                fs.Seek(leido, SeekOrigin.Begin);
                using var sr = new StreamReader(fs, System.Text.Encoding.UTF8);
                string? linea;
                while ((linea = sr.ReadLine()) != null)
                {
                    if (linea.Contains("[[FIN]]", StringComparison.Ordinal)) return true;
                    if (linea.Trim().Length > 0) Contar(linea, cuenta);
                }
                leido = fs.Position;
            }
            catch (IOException) { /* justo lo estaba escribiendo: se reintenta al siguiente latido */ }
        }
        return false;
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
