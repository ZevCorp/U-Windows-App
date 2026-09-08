using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using U.Graph;
using U.WindowsClient.Cuenta;
using U.WindowsClient.Diagnostics;
using U.WindowsClient.Voice;

namespace U.WindowsClient.Clinical.Transcripcion;

/// <summary>
/// DICTADO EN VIVO. Abre el micrófono, manda lo que oye, y va soltando el texto por frases según el
/// proveedor las cierra. Guarda además TODO lo dicho, que es lo que se manda al backend clínico.
/// </summary>
/// <remarks>
/// SE LLAMABA <c>DictadoSoniox</c> Y ESE NOMBRE ERA EL BUG. Mientras el archivo llevara el nombre de
/// un proveedor, «no arranca con Deepgram» parecía correcto en vez de parecer un fallo — y lo era:
/// el Provider Studio puede conmutar el proveedor sin avisar, y este cliente moría diciendo «el
/// backend no devolvió la configuración del stream», un mensaje que no distingue «es Deepgram» de
/// «el backend falló» (aprendizaje nº2). Ahora el proveedor lo elige <see cref="SesionDeStream"/> y
/// aquí solo queda el camino, que es el mismo para los dos.
///
/// VA APARTE DE <see cref="ConversacionEnVivo"/> A PROPÓSITO, y no es duplicación: son dos mecanismos
/// para dos cosas distintas. La voz en vivo mantiene una CONVERSACIÓN —oye, piensa, contesta con voz,
/// llama herramientas— y su transcripción es un subproducto. Esto no conversa: transcribe y se calla,
/// que es lo que hace falta cuando un médico habla con un paciente y no quiere que le respondan a
/// media frase.
///
/// EL CAMINO, el mismo que el portal (lib/stt/deepgram-dictation.js):
///   1. Se pide una sesión al backend. Trae una clave TEMPORAL de ~60 s, no la permanente.
///   2. Se abre el WebSocket como diga el lector: con subprotocolo (Deepgram) o pelado + primer
///      mensaje (Soniox).
///   3. Se manda el audio tal cual, en binario.
///   4. Al cerrar: el finalize del proveedor y un frame vacío.
///
/// LO QUE NO HACE: no toca SAP, no interpreta, no decide nada, y no sabe qué es un encounter. Solo
/// entrega frases y guarda el verbatim. Quien las convierta en campos o en nota es otro, y esa
/// frontera es la que permite juzgar este trozo sin abrir una pantalla.
/// </remarks>
public sealed class DictadoEnVivo : IDisposable
{
    private readonly GraphConfig _config;
    private readonly LiveAudio _audio;
    private readonly SesionMiracle? _sesion;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _vida;
    private Task? _escucha;
    private ILectorDeStream? _lector;

    /// <param name="sesion">
    /// El médico que está dentro, para atribuirle el consumo. Puede ser <c>null</c> en los usos que
    /// no son de una consulta clínica (el dictado a SAP de la carita, que se identifica por máquina).
    /// </param>
    public DictadoEnVivo(GraphConfig config, LiveAudio audio, SesionMiracle? sesion = null)
    {
        _config = config;
        _audio = audio;
        _sesion = sesion;
    }

    /// <summary>Está dictando ahora mismo.</summary>
    public bool Activo { get; private set; }

    /// <summary>Todo lo que se ha dicho en esta grabación, incluida la frase a medias.</summary>
    public Verbatim Dicho { get; } = new();

    /// <summary>
    /// Una frase CERRADA. Es el latido que dispara todo lo demás: el portal llama al organizador en
    /// cada una de estas, no en cada palabra.
    /// </summary>
    public event Action<string>? Frase;

    /// <summary>Lo que se lleva oído sin cerrar. Solo para pintar; no se actúa con esto.</summary>
    public event Action<string>? Parcial;

    /// <summary>Algo fue mal. Se dice y se para: dictar a medias es peor que no dictar.</summary>
    public event Action<string>? Fallo;

    /// <summary>Arrancó o paró, para que la interfaz pinte el botón.</summary>
    public event Action<bool>? Cambio;

    // ── arrancar y parar ─────────────────────────────────────────────────────

    /// <summary>
    /// Abre el micrófono y el stream. DEVUELVE SI DE VERDAD ARRANCÓ.
    /// </summary>
    /// <remarks>
    /// Antes no devolvía nada, y por eso el 2026-09-01 la consulta pudo declararse «grabando» con
    /// el stream sin conectar: quien llamaba no tenía forma de enterarse. Un arranque que puede
    /// fallar y no lo dice obliga a suponer, y suponer aquí cuesta una consulta entera.
    /// </remarks>
    public async Task<bool> ArrancarAsync(CancellationToken ct = default)
    {
        // ARRANCAR LIMPIA; REANUDAR NO. Es la unica diferencia entre los dos caminos, y es la
        // promesa 187 entera: si reanudar pasara por aqui, lo dicho antes de la pausa se perderia
        // en silencio y la nota saldria con media consulta.
        Dicho.Limpiar();
        return await ArrancarInternoAsync(ct);
    }

    /// <summary>
    /// VUELVE A DICTAR SOBRE LO YA DICHO. Es <see cref="ArrancarAsync"/> sin el borrado.
    /// </summary>
    /// <remarks>
    /// Promesa 187. NO es un alias de arrancar y no puede llegar a serlo: arrancar empieza por
    /// <c>Dicho.Limpiar()</c>, y ese borrado es justo lo que una pausa no puede hacer.
    ///
    /// Se abre un socket NUEVO en vez de mantener vivo el de antes, y es a proposito: este modulo no
    /// tiene latido (se comprobo el 2026-09-07: ni keepalive ni ping), asi que un socket en pausa se
    /// muere solo a los pocos segundos de silencio. Cerrarlo al pausar no es solo mas barato en
    /// minutos de transcripcion — es lo unico que aguanta una pausa de verdad, de las de salir de la
    /// sala.
    /// </remarks>
    public async Task<bool> ReanudarAsync(CancellationToken ct = default)
    {
        if (Activo) return true;
        bool arranco = await ArrancarInternoAsync(ct);
        if (arranco)
        {
            Pausado = false;
            LogBus.Log("dictado", $"dictado reanudado · se sigue sobre {Dicho.Largo} caracteres ya dichos");
        }
        return arranco;
    }

    private async Task<bool> ArrancarInternoAsync(CancellationToken ct = default)
    {
        if (Activo) return true;

        _vida = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // TODO EL ARRANQUE VA DENTRO DEL REINTENTO —pedir la sesión Y abrir el socket—, y no solo
        // el socket: la clave temporal dura ~60 s, así que reusar la sesión de hace tres intentos
        // sería presentarse con una credencial muerta. Cada intento pide la suya.
        var politica = new PoliticaDeReintento();
        SesionDeStream? sesion = null;
        string ultimoTropiezo = "";

        bool abrio = await politica.HastaQueSalgaAsync(async _ =>
        {
            try
            {
                sesion = SesionDeStream.Leer(await PedirSesionAsync(_vida.Token));
                _lector = sesion.Lector;

                var ws = new ClientWebSocket();
                foreach (var sub in _lector.Subprotocolos) ws.Options.AddSubProtocol(sub);
                _ws = ws;
                await ws.ConnectAsync(new Uri(sesion.Url), _vida.Token);

                // EL PRIMER MENSAJE VA ANTES QUE CUALQUIER BYTE DE AUDIO. Si llega audio primero,
                // el proveedor cierra sin decir por qué. Cuando es null —Deepgram— no se manda
                // nada, y eso es la respuesta correcta, no un hueco.
                string? arranque = _lector.MensajeDeArranque(_audio.RitmoEntrada);
                if (arranque != null)
                    await ws.SendAsync(Encoding.UTF8.GetBytes(arranque), WebSocketMessageType.Text,
                        true, _vida.Token);
                return true;
            }
            catch (Exception e)
            {
                ultimoTropiezo = $"{e.GetType().Name}: {e.Message}";
                // El socket a medio abrir no se reusa en el intento siguiente.
                try { _ws?.Dispose(); } catch { }
                _ws = null;
                return false;
            }
        }, _vida.Token);

        if (!abrio || sesion == null)
        {
            // SE DICE CUÁNTAS VECES SE INTENTÓ. «No pude abrir el stream» a secas no distingue un
            // parpadeo de un servicio caído, y son dos cosas que se atienden distinto.
            Avisar($"no pude abrir el dictado tras {politica.Intentos} intentos · {ultimoTropiezo}");
            Limpiar();
            return false;
        }

        Activo = true;
        Pausado = false;
        _escucha = Task.Run(() => EscucharAsync(_vida.Token));
        _audio.Capturado += Mandar;
        _audio.AbrirMicrofono();

        LogBus.Log("dictado", $"escuchando · {sesion.Proveedor} · {sesion.Modelo} · {sesion.Idioma}");
        Cambio?.Invoke(true);
        return true;
    }

    /// <summary>
    /// EL DICTADO ESTA EN PAUSA: no escucha, pero la consulta sigue viva y lo dicho sigue entero.
    /// </summary>
    /// <remarks>
    /// Distinto de <c>!Activo</c> a proposito: «no activo» tambien es un dictado que nunca arranco o
    /// uno que ya termino, y las tres cosas se atienden distinto. Es el aprendizaje n2 — un estado
    /// que no distingue sus causas manda la investigacion al sitio equivocado.
    /// </remarks>
    public bool Pausado { get; private set; }

    /// <summary>
    /// Deja de escuchar SIN cerrar la consulta y SIN tocar lo dicho.
    /// </summary>
    /// <remarks>
    /// Promesa 187. Hace lo mismo que <see cref="PararAsync"/> —incluido el <c>finalize</c>, para no
    /// perder la frase a medias— y se diferencia en lo unico que importa: al reanudar se sigue sobre
    /// el mismo verbatim. Ver <see cref="ReanudarAsync"/>.
    /// </remarks>
    public async Task PausarAsync()
    {
        if (!Activo) return;
        await CerrarElCanalAsync();
        Pausado = true;
        LogBus.Log("dictado", $"dictado en pausa · {Dicho.Largo} caracteres a salvo");
        Cambio?.Invoke(false);
    }

    /// <summary>Para de dictar y devuelve TODO lo dicho.</summary>
    public async Task<string> PararAsync()
    {
        if (!Activo && !Pausado) return Dicho.Todo;

        // TERMINAR DESDE LA PAUSA no tiene nada que cerrar: el canal ya se cerro al pausar, y lo
        // dicho esta entero. Volver a despedirse de un socket muerto solo gastaria los 2,5 s de
        // espera del finalize.
        if (!Activo) { Pausado = false; LogBus.Log("dictado", $"dictado terminado desde la pausa · {Dicho.Largo} caracteres"); return Dicho.Todo; }

        await CerrarElCanalAsync();
        Pausado = false;
        LogBus.Log("dictado", $"dictado terminado · {Dicho.Largo} caracteres");
        Cambio?.Invoke(false);
        return Dicho.Todo;
    }

    /// <summary>El cierre del canal, que pausar y parar comparten entero.</summary>
    private async Task CerrarElCanalAsync()
    {
        Activo = false;
        _audio.Capturado -= Mandar;
        _audio.CerrarMicrofono();

        // SE DESPIDE ANTES DE COLGAR. Sin el finalize, lo último dicho se queda dentro del proveedor
        // y se pierde justo la frase que se acababa de decir — que suele ser la que importa.
        try
        {
            if (_ws is { State: WebSocketState.Open } && _lector != null)
            {
                await _ws.SendAsync(Encoding.UTF8.GetBytes(_lector.MensajeDeFinalize),
                    WebSocketMessageType.Text, true, CancellationToken.None);
                await _ws.SendAsync(ReadOnlyMemory<byte>.Empty, WebSocketMessageType.Binary, true,
                    CancellationToken.None);
                await Task.WhenAny(_escucha ?? Task.CompletedTask, Task.Delay(2500));
            }
        }
        catch { /* cerrar no puede fallar hacia fuera */ }

        // La frase que quedó sin cerrar se entrega igual: se dijo (promesa 88).
        string ultima = Dicho.CerrarFrase();
        if (ultima.Length > 0) Frase?.Invoke(ultima);

        Limpiar();
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
            // «Request timeout» es lo que contesta Soniox al cerrar tras el finalize: no es un
            // fallo, es la despedida. Medido el 2026-08-14 — llega DESPUÉS del texto completo.
            if (Activo) Avisar($"el stream de dictado se cortó: {e.Message}");
        }
    }

    private void Digerir(string json)
    {
        try
        {
            _lector?.Digerir(json, Dicho,
                parcial: t => Parcial?.Invoke(t),
                frase: f => { LogBus.Log("dictado", $"frase de {f.Length} caracteres"); Frase?.Invoke(f); },
                fallo: Avisar);
        }
        catch (Exception e)
        {
            LogBus.Log("dictado", $"no entendí un mensaje de {_lector?.Nombre}: {e.Message}");
        }
    }

    // ── la sesión ────────────────────────────────────────────────────────────

    /// <remarks>
    /// LA ATRIBUCIÓN VIAJA CON EL MÉDICO cuando hay uno dentro (promesa 90): <c>X-Miracle-User-Id</c>
    /// con el uuid del token, que es lo que Graph valida contra `profiles` — igual que hace el portal.
    /// Sin médico —el dictado a SAP de la carita— se cae al correo del operador, que es la identidad
    /// de máquina que ese carril ya usaba.
    /// </remarks>
    private async Task<string> PedirSesionAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"{_config.BaseUrl.TrimEnd('/')}/api/v1/transcription/session")
        { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        req.Headers.Add("X-API-Key", _config.ApiKey ?? "");

        var atribucion = _sesion?.CabecerasDeAtribucion();
        if (atribucion is { Count: > 0 })
        {
            foreach (var (clave, valor) in atribucion) req.Headers.Add(clave, valor);
        }
        else if (_config.OperatorEmail.Length > 0)
        {
            req.Headers.Add("X-Miracle-User-Email", _config.OperatorEmail);
        }

        using var res = await Http.SendAsync(req, ct);
        string cuerpo = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"HTTP {(int)res.StatusCode}: {Recortar(cuerpo)}");
        return cuerpo;
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

    /// <summary>Nunca el cuerpo entero en el log: puede traer el token temporal del proveedor.</summary>
    private static string Recortar(string s) => s.Length <= 200 ? s : s[..200];

    public void Dispose()
    {
        _audio.Capturado -= Mandar;
        Limpiar();
    }
}
