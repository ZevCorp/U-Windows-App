using System.IO;
using System.Diagnostics;
using System.Text.Json;
using U.WindowsClient.Diagnostics;
using U.WindowsClient.Teach;

namespace U.WindowsClient.Piloto;

/// <summary>
/// EL PILOTO: un agente Claude (Agent SDK, en Node) que lee la lección, dice qué entendió, cuelga
/// recuerdos, hace la tarea de uno en uno por la puerta MCP de esta app y empaqueta la skill. Spec 013.
/// </summary>
/// <remarks>
/// UN SOLO CEREBRO, UNA SOLA CONVERSACIÓN (decisión del dueño, 2026-09-06): el mismo agente que
/// entiende la lección es el que la hace. Corre en un proceso aparte (<c>agente-piloto/piloto.mjs</c>)
/// porque el Agent SDK es de Node; esta clase lo lanza, le escribe el mensaje en disco y lee lo que
/// va contando por stdout, una línea JSON por vez.
///
/// LAS MANOS SIGUEN SIENDO LAS DE LA APP: el piloto no toca la pantalla, le pide a la app por MCP
/// (127.0.0.1:8790), que es la misma puerta por la que ya actúa Claude Code sobre este PC.
///
/// DÓNDE ESTÁ EL SCRIPT: en <c>U_PILOTO</c> si está puesto; si no, se busca <c>agente-piloto/piloto.mjs</c>
/// subiendo desde el directorio del ejecutable (la app se corre desde <c>windows-client/bin/…</c> en
/// desarrollo). En una instalación de Velopack no está: hay que empaquetarlo (pendiente, dicho en la spec).
/// </remarks>
public static class ElPiloto
{
    public sealed record Resultado(bool Termino, int Salida, string Sesion, string Ultimo, double CostoUsd);

    /// <summary>Lo que va al disco para que el piloto lo lea.</summary>
    public sealed record Mensaje(IReadOnlyList<Bloque> Bloques, IReadOnlyList<string> Caja,
        IReadOnlyList<string> Prohibidas, string Modelo, string Mcp);

    public static string? RutaDelScript()
    {
        string? env = Environment.GetEnvironmentVariable("U_PILOTO");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            string candidato = Path.Combine(dir.FullName, "agente-piloto", "piloto.mjs");
            if (File.Exists(candidato)) return candidato;
        }
        return null;
    }

    public static bool Disponible() => RutaDelScript() != null;

    public static string EscribirMensaje(string carpetaLeccion, Mensaje mensaje)
    {
        string ruta = Path.Combine(carpetaLeccion, "mensaje.json");
        File.WriteAllText(ruta, JsonSerializer.Serialize(mensaje, new JsonSerializerOptions { WriteIndented = true }));
        return ruta;
    }

    /// <summary>Lanza el piloto sobre una lección y espera a que termine. Cada línea que cuenta pasa por <paramref name="avance"/>.</summary>
    public static async Task<Resultado> CorrerAsync(string carpetaLeccion, Action<string, string> avance, CancellationToken ct)
    {
        string? script = RutaDelScript();
        if (script == null) return new(false, -1, "", "no encuentro agente-piloto/piloto.mjs (pon U_PILOTO o corre desde el repo)", 0);

        var psi = new ProcessStartInfo("node")
        {
            ArgumentList = { script, $"--leccion={carpetaLeccion}" },
            WorkingDirectory = Path.GetDirectoryName(script)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        LogBus.Log("piloto", $"lanzando: node {script} --leccion={carpetaLeccion}");
        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        string sesion = "", ultimo = ""; double costo = 0;
        var fin = new TaskCompletionSource<int>();
        p.Exited += (_, _) => fin.TrySetResult(p.ExitCode);
        p.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            try
            {
                using var doc = JsonDocument.Parse(e.Data);
                var r = doc.RootElement;
                string tipo = r.TryGetProperty("tipo", out var t) ? t.GetString() ?? "" : "";
                string texto = r.TryGetProperty("texto", out var x) ? x.GetString() ?? "" : "";
                if (r.TryGetProperty("sesion", out var s)) sesion = s.GetString() ?? sesion;
                if (r.TryGetProperty("costo", out var c) && c.ValueKind == JsonValueKind.Number) costo = c.GetDouble();
                if (texto.Length > 0) ultimo = texto;
                LogBus.Log("piloto", $"{tipo}: {(texto.Length > 300 ? texto[..300] + "…" : texto)}");
                avance(tipo, texto);
            }
            catch { LogBus.Log("piloto", e.Data); }
        };
        p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) LogBus.Log("piloto-err", e.Data); };
        try
        {
            if (!p.Start()) return new(false, -1, "", "node no arrancó", 0);
        }
        catch (Exception ex)
        {
            return new(false, -1, "", $"no pude lanzar node: {ex.Message} (¿está Node instalado y en el PATH?)", 0);
        }
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        using (ct.Register(() => { try { if (!p.HasExited) p.Kill(true); } catch { } }))
        {
            int salida = await fin.Task;
            return new(salida == 0, salida, sesion, ultimo, costo);
        }
    }
}
