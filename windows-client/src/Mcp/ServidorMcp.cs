using System.Net;
using System.Text;
using System.Threading.Tasks;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Mcp;

/// <summary>
/// EL CABLE del servidor MCP: HTTP streamable en 127.0.0.1, POST /mcp. Nada más.
/// </summary>
/// <remarks>
/// Todo lo que puede equivocarse en silencio —hablar mal JSON-RPC, publicar un catálogo a medias,
/// despachar lo no publicado— vive en <see cref="ProtocoloMcp"/> y se juzga sin levantar esto
/// (promesas 60-62). Aquí solo se mueve el agua: POST entra, respuesta sale.
///
/// ES LA PUERTA DE PRODUCCIÓN para el Agent SDK (F2 del plan de batch), a diferencia de la sonda
/// 8791 que es solo-desarrollo y habla un shim. Por eso arranca SIEMPRE — pero únicamente en
/// 127.0.0.1: el terreno de este PC se conduce desde este PC.
///
/// Del transporte streamable, lo mínimo legal: a un POST se contesta application/json (el SSE es
/// opcional del lado servidor), a una notificación 202 sin cuerpo, y a GET 405 — no ofrecemos
/// stream de servidor porque no empujamos nada: quien pregunta recibe, y ya.
/// </remarks>
public sealed class ServidorMcp : IDisposable
{
    public const int Puerto = 8790;

    private readonly ProtocoloMcp _protocolo;
    private HttpListener? _listener;

    public ServidorMcp(ProtocoloMcp protocolo) => _protocolo = protocolo;

    public bool Start()
    {
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{Puerto}/mcp/");
            _listener.Start();
            _ = Task.Run(BucleAsync);
            LogBus.Log("mcp", $"servidor MCP escuchando en 127.0.0.1:{Puerto}/mcp");
            return true;
        }
        catch (Exception e)
        {
            LogBus.Log("mcp", $"no pudo abrirse el servidor MCP: {e.Message}");
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

            try
            {
                if (ctx.Request.HttpMethod != "POST")
                {
                    // GET pediría un stream de servidor que no ofrecemos; DELETE cerraría una
                    // sesión que no existe. 405 es la respuesta del estándar para ambos.
                    ctx.Response.StatusCode = 405;
                    ctx.Response.Close();
                    continue;
                }

                using var lector = new System.IO.StreamReader(ctx.Request.InputStream, Encoding.UTF8);
                string cuerpo = await lector.ReadToEndAsync();
                string? salida = _protocolo.Atiende(cuerpo);

                if (salida == null)
                {
                    // Una notificación se acepta y no se contesta: 202 sin cuerpo, como manda el
                    // transporte.
                    ctx.Response.StatusCode = 202;
                    ctx.Response.Close();
                    continue;
                }

                byte[] bytes = Encoding.UTF8.GetBytes(salida);
                ctx.Response.ContentType = "application/json; charset=utf-8";
                ctx.Response.ContentLength64 = bytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            }
            catch (Exception e)
            {
                LogBus.Log("mcp", $"petición malograda: {e.Message}");
                try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
            }
        }
    }

    public void Dispose()
    {
        try { _listener?.Stop(); _listener?.Close(); } catch { }
    }
}
