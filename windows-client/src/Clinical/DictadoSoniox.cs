using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using U.Graph;
using U.WindowsClient.Diagnostics;
using U.WindowsClient.Voice;

namespace U.WindowsClient.Clinical;

/// <summary>
/// DICTADO EN VIVO POR SONIOX. Abre el micrófono, manda lo que oye, y va soltando el texto por
/// frases según Soniox las cierra.
/// </summary>
/// <remarks>
/// VA APARTE DE <see cref="ConversacionEnVivo"/> A PROPÓSITO, y no es duplicación: son dos mecanismos
/// distintos para dos cosas distintas. La voz en vivo mantiene una CONVERSACIÓN —oye, piensa,
/// contesta con voz, llama herramientas— y su transcripción es un subproducto. Esto no conversa:
/// transcribe y se calla, que es lo que hace falta cuando alguien está dictando una historia clínica
/// y no quiere que le respondan a media frase.
///
/// EL CAMINO, tal como lo hace el portal (public/shared/deepgram-dictation.js):
///   1. Se pide una sesión al backend. Trae una clave TEMPORAL de 60 s, no la permanente.
///   2. Se abre un WebSocket normal a Soniox. La autenticación es el PRIMER MENSAJE, no una
///      cabecera: por eso `auth_scheme` viene como «message».
///   3. Se mandan los trozos de audio tal cual, en binario.
///   4. Al cerrar: `{"type":"finalize"}` y un frame vacío.
///
/// EL ÚNICO CAMBIO RESPECTO AL NAVEGADOR, y está medido: el backend pide
/// <c>audio_format: "auto"</c>, que sirve para WebM o MP3 —traen cabecera y Soniox los reconoce—
/// pero NO para el PCM crudo que sale de <see cref="LiveAudio"/>, que no tiene ninguna. Se declara
/// el formato de forma explícita y ya. Probado el 2026-08-14 con voz sintetizada: dictando «ciento
/// veinte sobre ochenta» devolvió «120/80», y «treinta y seis con ocho» devolvió «36,8» — Soniox
/// normaliza las cifras como las quiere un formulario.
///
/// LO QUE NO HACE: no toca SAP, no interpreta, no decide nada. Solo entrega frases. Quien las
/// convierta en campos es otro, y esa frontera es la que permite probar este trozo sin abrir SAP.
/// </remarks>
public sealed class DictadoSoniox : IDisposable
{
    private readonly GraphConfig _config;
    private readonly LiveAudio _audio;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _vida;
    private Task? _escucha;

    /// <summary>Lo confirmado que Soniox aún no ha cerrado con un fin de frase.</summary>
    private readonly StringBuilder _enCurso = new();

    public DictadoSoniox(GraphConfig config, LiveAudio audio)
    {
        _config = config;
        _audio = audio;
    }

    /// <summary>Está dictando ahora mismo.</summary>
    public bool Activo { get; private set; }

    /// <summary>
    /// Una frase CERRADA. Es el latido que dispara todo lo demás: el portal llama al organizador en
    /// cada una de estas, no en cada palabra.
    /// </summary>
    public event Action<string>? Frase;

    /// <summary>Lo que se lleva oído sin cerrar todavía. Solo para pintar; no se actúa con esto.</summary>
    public event Action<string>? Parcial;

    /// <summary>Algo fue mal. Se dice y se para: dictar a medias es peor que no dictar.</summary>
    public event Action<string>? Fallo;

    /// <summary>Arrancó o paró, para que la interfaz pinte el botón.</summary>
    public event Action<bool>? Cambio;

    public async Task ArrancarAsync(CancellationToken ct = default)
    {
        if (Activo) return;

        JsonElement sesion;
        try { sesion = await PedirSesionAsync(ct); }
        catch (Exception e)
        {
            Avisar($"no pude pedir la sesión de dictado: {e.Message}");
            return;
        }

        string url = Texto(sesion, "websocket_url");
        if (url.Length == 0) { Avisar("el backend no devolvió la dirección del stream"); return; }

        string arranque = MensajeDeArranque(sesion, _audio.RitmoEntrada);
        if (arranque.Length == 0) { Avisar("el backend no devolvió la configuración del stream"); return; }

        _vida = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            _ws = new ClientWebSocket();
            await _ws.ConnectAsync(new Uri(url), _vida.Token);
            // LA AUTENTICACIÓN ES ESTE MENSAJE. Va antes que cualquier byte de audio; si llega
            // audio primero, Soniox cierra sin decir por qué.
            await _ws.SendAsync(Encoding.UTF8.GetBytes(arranque), WebSocketMessageType.Text, true, _vida.Token);
        }
        catch (Exception e)
        {
            Avisar($"no pude abrir el stream de dictado: {e.Message}");
            Limpiar();
            return;
        }

        _enCurso.Clear();
        Activo = true;
        _escucha = Task.Run(() => EscucharAsync(_vida.Token));
        _audio.Capturado += Mandar;
        _audio.AbrirMicrofono();

        LogBus.Log("dictado", $"escuchando · {Texto(sesion, "model")} · {Texto(sesion, "language")}");
        Cambio?.Invoke(true);
    }

    public async Task PararAsync()
    {
        if (!Activo) return;
        Activo = false;
        _audio.Capturado -= Mandar;
        _audio.CerrarMicrofono();

        // SE DESPIDE ANTES DE COLGAR. Sin el `finalize`, lo último dicho se queda dentro de Soniox
        // y se pierde justo la frase que se acababa de decir — que suele ser la que importa.
        try
        {
            if (_ws is { State: WebSocketState.Open })
            {
                await _ws.SendAsync(Encoding.UTF8.GetBytes("{\"type\":\"finalize\"}"),
                    WebSocketMessageType.Text, true, CancellationToken.None);
                await _ws.SendAsync(ReadOnlyMemory<byte>.Empty, WebSocketMessageType.Binary, true,
                    CancellationToken.None);
                // Un momento para que llegue lo último antes de cerrar el socket.
                await Task.WhenAny(_escucha ?? Task.CompletedTask, Task.Delay(2500));
            }
        }
        catch { /* cerrar no puede fallar hacia fuera */ }

        SoltarLoQueQuede();
        Limpiar();
        LogBus.Log("dictado", "dictado terminado");
        Cambio?.Invoke(false);
    }

    // ── el camino del audio ──────────────────────────────────────────────────

    private async void Mandar(byte[] pcm)
    {
        var ws = _ws;
        var vida = _vida;
        if (ws is not { State: WebSocketState.Open } || vida == null || vida.IsCancellationRequested) return;
        try { await ws.SendAsync(pcm, WebSocketMessageType.Binary, true, vida.Token); }
        catch { /* si el socket se cayó, lo dice el que escucha */ }
    }

    private async Task EscucharAsync(CancellationToken ct)
    {
        var buffer = new byte[32 * 1024];
        var acumulado = new StringBuilder();
        try
        {
            while (_ws is { State: WebSocketState.Open } && !ct.IsCancellationRequested)
            {
                var r = await _ws.ReceiveAsync(buffer, ct);
                if (r.MessageType == WebSocketMessageType.Close) break;
                acumulado.Append(Encoding.UTF8.GetString(buffer, 0, r.Count));
                if (!r.EndOfMessage) continue;

                string mensaje = acumulado.ToString();
                acumulado.Clear();
                Digerir(mensaje);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            // «Request timeout» es lo que Soniox contesta al cerrar tras el finalize: no es un
            // fallo, es la despedida. Medido el 2026-08-14 — llega DESPUÉS del texto completo.
            if (Activo) Avisar($"el stream de dictado se cortó: {e.Message}");
        }
    }

    /// <summary>
    /// Un mensaje de Soniox. Trae tokens sueltos: unos confirmados y otros aún provisionales.
    /// </summary>
    /// <remarks>
    /// EL FIN DE FRASE ES UN TOKEN, no un campo del mensaje: llega un token cuyo texto es
    /// <c>&lt;end&gt;</c>. Ahí es donde el portal dispara el organizador, y por eso se replica: es
    /// el punto en el que lo dicho ya es una idea completa y merece ir al LLM. Ni antes —se
    /// organizarían fragmentos— ni cuando se para de dictar —se perdería el tiempo real—.
    /// </remarks>
    private void Digerir(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var raiz = doc.RootElement;

            if (raiz.TryGetProperty("error_code", out var codigo) || raiz.TryGetProperty("error_message", out _))
            {
                string msg = Texto(raiz, "error_message");
                // Ver el comentario de EscucharAsync: el 408 tras el finalize es la despedida.
                if (Activo && !msg.Contains("timeout", StringComparison.OrdinalIgnoreCase))
                    Avisar($"Soniox rechazó el stream: {codigo} {msg}");
                return;
            }

            if (!raiz.TryGetProperty("tokens", out var tokens) || tokens.ValueKind != JsonValueKind.Array) return;

            var provisional = new StringBuilder();
            foreach (var t in tokens.EnumerateArray())
            {
                string texto = Texto(t, "text");
                if (texto.Length == 0) continue;
                bool firme = t.TryGetProperty("is_final", out var f) && f.ValueKind == JsonValueKind.True;

                if (texto == "<end>") { if (firme) Cerrar(); continue; }
                if (texto == "<fin>") continue;             // fin del stream, no del habla

                if (firme) _enCurso.Append(texto);
                else provisional.Append(texto);
            }

            if (_enCurso.Length > 0 || provisional.Length > 0)
                Parcial?.Invoke((_enCurso.ToString() + provisional).Trim());
        }
        catch (Exception e) { LogBus.Log("dictado", $"no entendí un mensaje de Soniox: {e.Message}"); }
    }

    private void Cerrar()
    {
        string frase = _enCurso.ToString().Trim();
        _enCurso.Clear();
        if (frase.Length == 0) return;
        LogBus.Log("dictado", $"frase: {frase}");
        Frase?.Invoke(frase);
    }

    /// <summary>Lo dicho al final que Soniox no llegó a cerrar. Se entrega igual: se dijo.</summary>
    private void SoltarLoQueQuede() => Cerrar();

    // ── la sesión ────────────────────────────────────────────────────────────

    private async Task<JsonElement> PedirSesionAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"{_config.BaseUrl.TrimEnd('/')}/api/v1/transcription/session")
        { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        req.Headers.Add("X-API-Key", _config.ApiKey ?? "");
        if (_config.OperatorEmail.Length > 0) req.Headers.Add("X-Miracle-User-Email", _config.OperatorEmail);

        using var res = await Http.SendAsync(req, ct);
        string cuerpo = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"HTTP {(int)res.StatusCode}: {Recortar(cuerpo)}");

        return JsonDocument.Parse(cuerpo).RootElement.Clone();
    }

    /// <summary>
    /// El primer mensaje, con el formato del audio DECLARADO.
    /// </summary>
    /// <remarks>
    /// Se copia entero lo que manda el backend —ahí viaja la clave temporal, y tocarla sería
    /// romperlo— y solo se le añaden los tres campos del formato. El backend dice «auto» porque
    /// quien lo estrenó fue el navegador, que manda WebM con cabecera; nosotros mandamos PCM crudo,
    /// que no tiene ninguna, así que «auto» no tiene nada que detectar.
    /// </remarks>
    private static string MensajeDeArranque(JsonElement sesion, int ritmoEntrada)
    {
        if (!sesion.TryGetProperty("start_message", out var inicio) || inicio.ValueKind != JsonValueKind.Object)
            return "";

        var buffer = new System.IO.MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            foreach (var campo in inicio.EnumerateObject())
            {
                if (campo.NameEquals("audio_format") || campo.NameEquals("sample_rate")
                    || campo.NameEquals("num_channels")) continue;
                campo.WriteTo(w);
            }
            w.WriteString("audio_format", "pcm_s16le");
            w.WriteNumber("sample_rate", ritmoEntrada);
            w.WriteNumber("num_channels", 1);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    // ── menudencias ──────────────────────────────────────────────────────────

    private void Avisar(string porque)
    {
        LogBus.Log("dictado", porque);
        Fallo?.Invoke(porque);
        if (Activo) { Activo = false; Cambio?.Invoke(false); }
    }

    private void Limpiar()
    {
        try { _vida?.Cancel(); } catch { }
        try { _ws?.Dispose(); } catch { }
        _ws = null;
        _vida = null;
        _escucha = null;
    }

    private static string Texto(JsonElement o, string campo) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(campo, out var v)
        && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string Recortar(string s) => s.Length <= 200 ? s : s[..200];

    public void Dispose()
    {
        _audio.Capturado -= Mandar;
        Limpiar();
    }
}
