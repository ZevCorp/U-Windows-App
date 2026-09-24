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
    private readonly int _puerto;
    private HttpListener? _listener;

    /// <param name="puerto">El de siempre, <see cref="Puerto"/>; otro solo para que el contrato lo arranque de
    /// verdad sin pisar la app viva (promesa 401).</param>
    public ServidorMcp(ProtocoloMcp protocolo, int puerto = Puerto)
    {
        _protocolo = protocolo;
        _puerto = puerto;
    }

    public bool Start()
    {
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{_puerto}/mcp/");
            _listener.Start();
            _ = Task.Run(BucleAsync);
            LogBus.Log("mcp", $"servidor MCP escuchando en 127.0.0.1:{_puerto}/mcp");
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

            // CADA PETICIÓN EN SU PROPIA TAREA. Atendiendo en serie, una herramienta lenta ponía en
            // cola TODO lo que llegara detrás — y cuando map_what_i_see se colgó, hasta el
            // map_where_am_i más barato moría de timeout esperando turno (2026-08-25). El protocolo
            // ya tiene su propio plazo por herramienta; el cable no puede añadir una cola encima.
            _ = Task.Run(() => AtenderAsync(ctx));
        }
    }

    private async Task AtenderAsync(HttpListenerContext ctx)
    {
            try
            {
                // LA PUERTA, ANTES DE LEER EL CUERPO (promesa 401). Sin CORS una página ajena no podía leer
                // la respuesta, pero sí MANDAR la llamada: un POST con text/plain no pide permiso previo, y
                // Atiende lee el cuerpo como JSON-RPC sin mirar su tipo. Medido el 2026-09-24 leyendo el
                // código: cualquier página del navegador podía despachar map_type. El piloto y el Agent
                // SDK no son navegadores y no mandan Origin: pasan.
                var puerta = Navigation.PuertaLocal.Admite(ctx.Request.Headers["Origin"], ctx.Request.Headers["Host"], _puerto);
                if (!puerta.Pasa)
                {
                    LogBus.Log("mcp", $"rechazada {ctx.Request.HttpMethod}: {puerta.Porque}");
                    ctx.Response.StatusCode = 403;
                    ctx.Response.Close();
                    return;
                }

                if (ctx.Request.HttpMethod != "POST")
                {
                    // GET pediría un stream de servidor que no ofrecemos; DELETE cerraría una
                    // sesión que no existe. 405 es la respuesta del estándar para ambos.
                    ctx.Response.StatusCode = 405;
                    ctx.Response.Close();
                    return;
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
                    return;
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

    public void Dispose()
    {
        try { _listener?.Stop(); _listener?.Close(); } catch { }
    }
}
