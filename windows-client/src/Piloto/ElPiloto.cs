using System.IO;
using System.Diagnostics;
using System.Text.Json;
using U.WindowsClient.Diagnostics;
using U.WindowsClient.Teach;
using SinValor = U.Graph.SinValor;

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
    /// <summary>
    /// Con qué arranca node (promesa 195): el script y la carpeta en su modo. «--leccion=» para
    /// comprobar una lección; «--encargo=» para un encargo de la nota, que no tiene lección que leer.
    /// </summary>
    public static IReadOnlyList<string> Argumentos(string script, string carpeta, string modo = "leccion") =>
        new[] { script, $"--{(string.IsNullOrWhiteSpace(modo) ? "leccion" : modo.Trim())}={carpeta}" };

    public static async Task<Resultado> CorrerAsync(string carpetaLeccion, Action<string, string> avance, CancellationToken ct,
        string modo = "leccion")
    {
        string? script = RutaDelScript();
        if (script == null) return new(false, -1, "", "no encuentro agente-piloto/piloto.mjs (pon U_PILOTO o corre desde el repo)", 0);

        var psi = new ProcessStartInfo("node")
        {
            WorkingDirectory = Path.GetDirectoryName(script)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var a in Argumentos(script, carpetaLeccion, modo)) psi.ArgumentList.Add(a);
        LogBus.Log("piloto", $"lanzando: node {string.Join(" ", psi.ArgumentList)}");
        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        string sesion = "", ultimo = ""; double costo = 0;
        var fin = new TaskCompletionSource<int>();
        p.Exited += (_, _) => fin.TrySetResult(p.ExitCode);
        p.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            // UNA sola línea por lo que cuenta el piloto, y por su forma (spec 051, N1 y N2): la de antes
            // llevaba los 300 primeros caracteres de su narración, y el piloto trabaja con la nota delante.
            LogBus.Log("piloto", LineaDelPiloto(e.Data));
            if (Leer(e.Data, out string _) is not { } l) return;
            if (l.Sesion != null) sesion = l.Sesion;
            if (l.Costo is { } c) costo = c;
            if (l.Texto.Length > 0) ultimo = l.Texto;
            try { avance(l.Tipo, l.Texto); }
            catch (Exception ex)
            {
                // Antes, un solo catch cubría el JSON y el avance, y los dos acababan anotando la línea CRUDA:
                // «no era JSON» y «el avance reventó» se veían igual. Ahora el JSON lo dice LineaDelPiloto, y
                // esto dice que reventó el avance, sin lo que el piloto contó.
                LogBus.Log("piloto", $"el avance de una línea «{l.Tipo}» reventó: {ex.GetType().Name}: {ex.Message}");
            }
        };
        // Lo que el piloto escribe en stderr, por su longitud (spec 051, N3): una traza de Node puede arrastrar
        // el encargo, que lleva la nota. Para leer la traza entera, el piloto se corre a mano.
        p.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data)) LogBus.Log("piloto-err", $"el piloto escribió en stderr: {SinValor.Forma(e.Data)}");
        };
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

    /// <summary>
    /// La línea que el log guarda de lo que cuenta el piloto: su tipo y la longitud de su texto, nunca el
    /// texto (spec 051, promesa 398). <c>texto: ‹23 car.›</c>; una que no es JSON, <c>línea sin JSON: ‹N car.›</c>.
    /// </summary>
    /// <remarks>
    /// El piloto trabaja con el encargo delante, y el encargo lleva la nota; lo que narra la repite. Hasta el
    /// 2026-09-24 el log guardaba los 300 primeros caracteres de cada línea, y enteras las que no eran JSON, y
    /// el log salía del equipo por el espejo. El tipo lo pone <c>piloto.mjs</c> (<c>di("texto", …)</c>: siete
    /// literales, de <c>inicio</c> a <c>fin</c>): es vocabulario nuestro, y es lo que dice por dónde va.
    /// </remarks>
    public static string LineaDelPiloto(string linea)
        => Leer(linea, out string porque) is { } l
            ? $"{(l.Tipo.Length > 0 ? l.Tipo : "(sin tipo)")}: {SinValor.Forma(l.Texto)}"
            : $"{porque}: {SinValor.Forma(linea)}";

    /// <summary>Lo que trae una línea del piloto, ya sacado del JSON (el documento no sobrevive a la lectura).</summary>
    private readonly record struct Linea(string Tipo, string Texto, string? Sesion, double? Costo);

    /// <summary>
    /// Una línea del piloto leída, o null diciendo por qué no: no es JSON, o es JSON pero no un objeto. Son
    /// dos causas y se nombran las dos (patrón nº2).
    /// </summary>
    private static Linea? Leer(string linea, out string porque)
    {
        porque = "";
        try
        {
            using var doc = JsonDocument.Parse(linea);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) { porque = $"línea JSON que no es un objeto ({r.ValueKind})"; return null; }
            string Texto(string clave) => r.TryGetProperty(clave, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            return new Linea(
                Texto("tipo"),
                Texto("texto"),
                r.TryGetProperty("sesion", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null,
                r.TryGetProperty("costo", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetDouble() : null);
        }
        catch (JsonException) { porque = "línea sin JSON"; return null; }
    }
}
