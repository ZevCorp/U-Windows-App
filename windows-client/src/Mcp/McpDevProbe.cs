using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Mcp;

/// <summary>
/// Una puerta LOCAL y solo-de-desarrollo para invocar las herramientas MCP desde fuera del
/// proceso, exactamente por el mismo camino que las invoca el asistente (<see cref="LocalMcp"/>).
///
/// Existe porque «¿el terreno es navegable por MCP?» no se puede responder leyendo el código: hay
/// que llamar a las herramientas contra un mapa real, sobre la máquina real, y ver si llevan a
/// donde dicen. Sin esto, probarlo exigía pedírselo al modelo y esperar a que decidiera usarlas,
/// lo que mezcla dos preguntas distintas: si el mapa sirve y si el modelo acierta a usarlo.
///
/// Encendida SOLO con U_MCP_PROBE=1 (la pone dev-paralelo.ps1), escucha únicamente en 127.0.0.1 y
/// no se registra en ningún catálogo: en la app del usuario esta clase nunca arranca.
/// </summary>
public sealed class McpDevProbe
{
    public const int Puerto = 8791;

    private readonly LocalMcp _mcp;
    private HttpListener? _listener;

    public McpDevProbe(LocalMcp mcp) => _mcp = mcp;

    /// <summary>Arranca la sonda si la variable de entorno lo pide. Silenciosa si no.</summary>
    public static McpDevProbe? StartIfEnabled(LocalMcp mcp)
    {
        if (Environment.GetEnvironmentVariable("U_MCP_PROBE") != "1") return null;
        var p = new McpDevProbe(mcp);
        return p.Start() ? p : null;
    }

    private bool Start()
    {
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{Puerto}/mcp/");
            _listener.Start();
            _ = Task.Run(BucleAsync);
            LogBus.Log("mcp-probe", $"sonda de desarrollo escuchando en 127.0.0.1:{Puerto}/mcp");
            return true;
        }
        catch (Exception e)
        {
            LogBus.Log("mcp-probe", $"no pudo abrirse la sonda: {e.Message}");
            return false;
        }
    }

    private async Task BucleAsync()
    {
        while (_listener is { IsListening: true })
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { return; }   // listener cerrado

            string salida;
            try
            {
                using var lector = new System.IO.StreamReader(ctx.Request.InputStream, Encoding.UTF8);
                string cuerpo = await lector.ReadToEndAsync();
                salida = Invocar(cuerpo);
            }
            catch (Exception e) { salida = "error en la sonda: " + e.Message; }

            byte[] bytes = Encoding.UTF8.GetBytes(salida);
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            try
            {
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            }
            catch { }
        }
    }

    /// <summary>{"tool":"map_go_to","args":{"surface":"..."}} → el texto que devuelve la herramienta.</summary>
    private string Invocar(string json)
    {
        string tool;
        var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(json);
            tool = doc.RootElement.GetProperty("tool").GetString() ?? "";
            if (doc.RootElement.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Object)
                foreach (var p in a.EnumerateObject())
                    args[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.ToString();
        }
        catch (Exception e) { return "json inválido: " + e.Message; }

        if (tool.Length == 0) return "falta 'tool'";

        // MISMO camino que el asistente: LocalMcp.Call, sin atajos. Si aquí funciona y allí no, la
        // diferencia está en la decisión del modelo, no en el terreno — y eso ya es un dato.
        return _mcp.Call(tool, args);
    }

    public void Stop()
    {
        try { _listener?.Stop(); _listener?.Close(); } catch { }
        _listener = null;
    }
}
