using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Voice;

/// <summary>
/// El collar hablando por el teléfono: audio que llega por la red, no por Bluetooth.
///
/// EL CAMINO ENTERO, medido el 2026-09-01:
/// <code>
///   collar ──BLE──► app oficial de Omi ──WebSocket──► función omi-directo (Supabase)
///                                                          │ agrupa 200 ms
///                                                          ▼
///                                        canal Realtime «omi-CODIGO» ──► esta clase ──► LiveAudio
/// </code>
///
/// El audio NO pasa por la nube de Omi. La app de Omi queda reducida a un puente Bluetooth, que es
/// lo único que necesitábamos de ella para llegar a iPhone sin publicar una app propia. Y como Omi
/// no transcribe en ese modo, el plan de Omi no aplica: su propio backend marca a esos usuarios como
/// exentos de los topes.
///
/// LO QUE ENTRA POR AQUÍ ES EL COLLAR, no el micrófono del teléfono. La firma es el tamaño de trama:
/// 640 bytes = 320 muestras = 20 ms, que es la trama nativa del CV1. Medido en una corrida de
/// 100,7 s: 3.856 tramas, ~32.000 B/s sostenidos, PCM16 16 kHz mono en tiempo real.
///
/// SE HABLA PHOENIX A MANO, y es a propósito: el cliente oficial de Supabase para .NET arrastraría
/// una dependencia entera para lo único que hace falta aquí —unirse a un canal y leer mensajes—, y
/// este cliente cabe en un archivo que se lee de una sentada.
/// </summary>
public sealed class FuenteTelefono : IDisposable
{
    /// <summary>PCM16 a 16 kHz mono, tal como lo entrega el collar. Mismo formato que el Bluetooth.</summary>
    public event Action<byte[]>? Capturado;

    /// <summary>Se cayó el canal y hay que relevar. Lleva el motivo.</summary>
    public event Action<string>? Perdido;

    /// <summary>Cambió el estado del canal (uniéndose, unido, caído): para repintar la interfaz.</summary>
    public event Action? Cambio;

    public string Estado { get; private set; } = "sin conectar";
    public bool Unido { get; private set; }

    /// <summary>Cuántos trozos han llegado. Distingue «no se unió» de «se unió y nadie habla».</summary>
    public long Trozos { get; private set; }

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private int _ref;

    private readonly string _url;
    private readonly string _tema;

    /// <param name="proyecto">La referencia del proyecto de Supabase.</param>
    /// <param name="clave">La clave publicable. Es la misma que ya usa la sesión del médico.</param>
    /// <param name="codigo">El código de emparejamiento: nombra el canal y es lo único que lo protege.</param>
    public FuenteTelefono(string proyecto, string clave, string codigo)
    {
        _url = $"wss://{proyecto}.supabase.co/realtime/v1/websocket?apikey={clave}&vsn=1.0.0";
        _tema = $"realtime:omi-{codigo}";
    }

    public async Task<bool> AbrirAsync(CancellationToken ct)
    {
        try
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _ws = new ClientWebSocket();
            Poner("uniéndose al canal…");

            await _ws.ConnectAsync(new Uri(_url), _cts.Token);
            await MandarAsync(new
            {
                topic = _tema,
                @event = "phx_join",
                payload = new { config = new { broadcast = new { self = false } } },
                @ref = (++_ref).ToString(),
            });

            _ = Task.Run(() => LeerAsync(_cts.Token));
            _ = Task.Run(() => LatirAsync(_cts.Token));
            return true;
        }
        catch (Exception e)
        {
            // NO SE LANZA HACIA ARRIBA. Quedarse sin teléfono no puede tumbar la consulta: el
            // micrófono del portátil sigue ahí y es exactamente para esto.
            LogBus.Log("telefono", $"no se pudo abrir el canal: {e.GetType().Name}: {e.Message}");
            Poner("no se pudo abrir el canal");
            return false;
        }
    }

    /// <summary>
    /// EL LATIDO NO ES OPCIONAL. Realtime cierra los canales que llevan ~60 s callados, y este puede
    /// pasar minutos sin audio con toda normalidad: el collar no transmite mientras nadie habla.
    /// Sin latido, callarse un rato se vería exactamente igual que perder la conexión.
    /// </summary>
    private async Task LatirAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
            {
                await Task.Delay(25000, ct);
                await MandarAsync(new { topic = "phoenix", @event = "heartbeat", payload = new { }, @ref = (++_ref).ToString() });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { LogBus.Log("telefono", $"el latido se paró: {e.GetType().Name}: {e.Message}"); }
    }

    private async Task LeerAsync(CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        var acumulado = new List<byte>(64 * 1024);
        try
        {
            while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
            {
                acumulado.Clear();
                WebSocketReceiveResult r;
                do
                {
                    r = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (r.MessageType == WebSocketMessageType.Close)
                    {
                        Perder($"el canal se cerró ({r.CloseStatus}): {r.CloseStatusDescription}");
                        return;
                    }
                    acumulado.AddRange(new ArraySegment<byte>(buffer, 0, r.Count));
                } while (!r.EndOfMessage);

                Digerir(Encoding.UTF8.GetString(acumulado.ToArray()));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Perder($"{e.GetType().Name}: {e.Message}"); }
    }

    private void Digerir(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var raiz = doc.RootElement;
            string evento = raiz.TryGetProperty("event", out var ev) ? ev.GetString() ?? "" : "";

            if (evento == "phx_reply")
            {
                string estado = raiz.TryGetProperty("payload", out var p) && p.TryGetProperty("status", out var st)
                    ? st.GetString() ?? "" : "";
                if (estado == "ok" && !Unido) { Unido = true; Poner("unido al canal, esperando voz"); }
                else if (estado == "error") Perder("el canal rechazó la unión");
                return;
            }

            if (evento != "broadcast") return;

            // {"event":"broadcast","payload":{"event":"audio","payload":{"seq":1,"pcm":"<base64>"}}}
            if (!raiz.TryGetProperty("payload", out var sobre)) return;
            if (!sobre.TryGetProperty("payload", out var carga)) return;
            if (!carga.TryGetProperty("pcm", out var pcm)) return;

            var bytes = Convert.FromBase64String(pcm.GetString() ?? "");
            if (bytes.Length == 0) return;

            if (Trozos == 0) LogBus.Log("telefono", $"primer audio por el teléfono · {bytes.Length} bytes");
            Trozos++;
            if (!Unido) { Unido = true; Poner("entregando"); }
            else if (Estado != "entregando") Poner("entregando");

            Capturado?.Invoke(bytes);
        }
        catch (Exception e)
        {
            // UN MENSAJE ILEGIBLE NO PUEDE TUMBAR EL CANAL. Es el mismo criterio que la promesa 5
            // para las tramas del collar: se descarta y se sigue oyendo.
            LogBus.Log("telefono", $"mensaje que no entendí: {e.GetType().Name}: {e.Message}");
        }
    }

    private async Task MandarAsync(object mensaje)
    {
        if (_ws?.State != WebSocketState.Open) return;
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(mensaje));
        await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts?.Token ?? default);
    }

    private void Poner(string estado)
    {
        Estado = estado;
        LogBus.Log("telefono", estado);
        Cambio?.Invoke();
    }

    private void Perder(string motivo)
    {
        if (!Unido && Estado.StartsWith("se perdió")) return;
        Unido = false;
        Poner("se perdió el canal: " + motivo);
        Perdido?.Invoke(motivo);
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        try { _ws?.Dispose(); } catch { }
        _ws = null;
        Unido = false;
    }
}
