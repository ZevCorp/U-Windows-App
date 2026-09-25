using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using NAudio.Wave;
using U.Ciclo;

namespace U.Nuevo;

/// <summary>
/// LA VOZ: una sesión de GPT-Live con Luna como delegada (ProtocoloVivo, promesa 448). Oye por el micrófono,
/// habla por el altavoz, y cuando Luna llama a «hacer» o «mirar» lo atiende el Asistente en otro hilo.
///
/// ECO, en la v1, por turnos: mientras Ü suena no se manda el micrófono (y 300 ms después), para que la voz
/// no se oiga a sí misma. Main necesitó SpeexDSP para poder hablarle encima; aquí se paga con no poder
/// interrumpirla, a cambio de cero dependencias. Está dicho para que nadie lo tome por un olvido.
/// </summary>
public sealed class SesionDeVoz : IAsyncDisposable
{
    private readonly string _clave;
    private readonly Asistente _ü;
    private ClientWebSocket? _ws;
    private WaveInEvent? _micro;
    private WaveOutEvent? _altavoz;
    private BufferedWaveProvider? _salida;
    private readonly SemaphoreSlim _envio = new(1, 1);
    private readonly CancellationTokenSource _fin = new();
    private DateTime _sonoHasta = DateTime.MinValue;
    private DateTime _ultimaActividad = DateTime.Now;

    public event Action<string>? Estado;       // «escuchando», «pensando», «actuando», «cerrada»
    public event Action<string>? DiceUsuario;
    public event Action<string>? DiceU;
    public bool Abierta { get; private set; }

    /// <summary>Quieta hasta que se le habla: sin nada que oír ni hacer en este tiempo, se cierra sola.</summary>
    public TimeSpan CierreEnSilencio { get; init; } = TimeSpan.FromSeconds(90);

    public SesionDeVoz(string claveOpenAI, Asistente ü) { _clave = claveOpenAI; _ü = ü; }

    public async Task AbrirAsync()
    {
        _ws = new ClientWebSocket();
        _ws.Options.SetRequestHeader("Authorization", "Bearer " + _clave);
        var r = System.Diagnostics.Stopwatch.StartNew();
        await _ws.ConnectAsync(ProtocoloVivo.Direccion(), _fin.Token);
        await EnviarAsync(ProtocoloVivo.Apertura(ProtocoloVivo.InstruccionesDeLuna));
        Registro.Log($"voz: socket abierto en {r.ElapsedMilliseconds} ms, esperando session.started");
        _ = Task.Run(RecibirAsync);
        _ = Task.Run(VigilarSilencioAsync);
    }

    private void EmpezarAudio()
    {
        var formato = new WaveFormat(ProtocoloVivo.Ritmo, 16, 1);
        _salida = new BufferedWaveProvider(formato) { BufferDuration = TimeSpan.FromSeconds(60), DiscardOnBufferOverflow = true };
        _altavoz = new WaveOutEvent { DesiredLatency = 120 };
        _altavoz.Init(_salida);
        _altavoz.Play();

        _micro = new WaveInEvent { WaveFormat = formato, BufferMilliseconds = 100 };
        _micro.DataAvailable += (_, e) =>
        {
            if (!Abierta || _ws?.State != WebSocketState.Open) return;
            if (DateTime.Now < _sonoHasta || (_salida?.BufferedDuration ?? TimeSpan.Zero) > TimeSpan.Zero) return;   // Ü está sonando
            _ = EnviarAsync(ProtocoloVivo.Audio(e.Buffer.AsSpan(0, e.BytesRecorded)));
        };
        _micro.StartRecording();
    }

    /// <summary>Un pedido escrito, por la misma sesión: Luna lo recibe igual que si se hubiera dicho.</summary>
    public async Task EscribirAsync(string texto)
    {
        _ultimaActividad = DateTime.Now;
        foreach (var m in ProtocoloVivo.TextoDelUsuario(texto)) await EnviarAsync(m);
    }

    private async Task RecibirAsync()
    {
        var buffer = new byte[1 << 16];
        var mensaje = new MemoryStream();
        try
        {
            while (_ws is { State: WebSocketState.Open } && !_fin.IsCancellationRequested)
            {
                var r = await _ws.ReceiveAsync(buffer, _fin.Token);
                if (r.MessageType == WebSocketMessageType.Close) break;
                mensaje.Write(buffer, 0, r.Count);
                if (!r.EndOfMessage) continue;
                string json = Encoding.UTF8.GetString(mensaje.GetBuffer(), 0, (int)mensaje.Length);
                mensaje.SetLength(0);
                Atender(json);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Registro.Log($"voz: se cayó la conexión ({e.GetType().Name}: {e.Message})");
        }
        await CerrarAsync("la conexión terminó");
    }

    private void Atender(string json)
    {
        using var d = JsonDocument.Parse(json);
        var m = d.RootElement;
        switch (ProtocoloVivo.Texto(m, "type"))
        {
            case "session.started":
                Abierta = true;
                Registro.Log("voz: sesión abierta, el servidor la confirmó");
                EmpezarAudio();
                Estado?.Invoke("escuchando");
                break;
            case "session.output_audio.delta":
                var b64 = ProtocoloVivo.Texto(m, "delta");
                if (b64.Length > 0)
                {
                    var pcm = Convert.FromBase64String(b64);
                    if (!EsSilencio(pcm))
                    {
                        _salida?.AddSamples(pcm, 0, pcm.Length);
                        _sonoHasta = DateTime.Now + (_salida?.BufferedDuration ?? TimeSpan.Zero) + TimeSpan.FromMilliseconds(300);
                    }
                }
                break;
            case "session.input_transcript.delta":
                _ultimaActividad = DateTime.Now;
                DiceUsuario?.Invoke(ProtocoloVivo.Texto(m, "delta"));
                break;
            case "session.output_transcript.delta":
                DiceU?.Invoke(ProtocoloVivo.Texto(m, "delta"));
                break;
            case "response.event":
                var llamada = ProtocoloVivo.Llamada(json);
                if (llamada != null)
                {
                    _ultimaActividad = DateTime.Now;
                    _ = Task.Run(() => EjecutarAsync(llamada));
                }
                break;
            case "error":
                Registro.Log("voz: el servidor dice error: " + (m.TryGetProperty("error", out var e) ? e.GetRawText() : json));
                break;
            case "session.closed":
                Registro.Log("voz: el servidor cerró la sesión: " + ProtocoloVivo.Texto(m, "reason"));
                _ = CerrarAsync("el servidor la cerró");
                break;
        }
    }

    private async Task EjecutarAsync(LlamadaDeLuna l)
    {
        Estado?.Invoke(l.Nombre == "hacer" ? "actuando" : "pensando");
        Registro.Log($"🌙 Luna → {l.Nombre} {l.Argumentos}");
        string salida = _ü.Atender(l.Nombre, l.Argumentos);
        foreach (var msg in ProtocoloVivo.Resultado(l.CallId, salida)) await EnviarAsync(msg);
        _ultimaActividad = DateTime.Now;
        Estado?.Invoke("escuchando");
    }

    private async Task VigilarSilencioAsync()
    {
        while (!_fin.IsCancellationRequested)
        {
            await Task.Delay(2000);
            if (Abierta && DateTime.Now - _ultimaActividad > CierreEnSilencio && DateTime.Now > _sonoHasta)
            {
                Registro.Log($"voz: {CierreEnSilencio.TotalSeconds:0} s sin que se le hable: vuelve a quedarse quieta");
                await CerrarAsync("silencio");
                return;
            }
        }
    }

    private async Task EnviarAsync(string json)
    {
        if (_ws?.State != WebSocketState.Open) return;
        await _envio.WaitAsync();
        try { await _ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, _fin.Token); }
        catch (Exception e) { Registro.Log($"voz: no pude enviar ({e.GetType().Name}: {e.Message})"); }
        finally { _envio.Release(); }
    }

    private int _cerrada;
    public async Task CerrarAsync(string porque)
    {
        if (Interlocked.Exchange(ref _cerrada, 1) == 1) return;
        Abierta = false;
        try { _micro?.StopRecording(); _micro?.Dispose(); } catch { }
        try { _altavoz?.Stop(); _altavoz?.Dispose(); } catch { }
        try { if (_ws?.State == WebSocketState.Open) await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, porque, CancellationToken.None); } catch { }
        _fin.Cancel();
        Registro.Log("voz: cerrada · " + porque);
        Estado?.Invoke("cerrada");
    }

    /// <summary>El silencio del servidor son ceros exactos (medido en main el 2026-09-12): no se encola.</summary>
    private static bool EsSilencio(byte[] pcm)
    {
        for (int i = 0; i + 1 < pcm.Length; i += 2) if (BitConverter.ToInt16(pcm, i) != 0) return false;
        return true;
    }

    public async ValueTask DisposeAsync() => await CerrarAsync("se cerró Ü");
}
