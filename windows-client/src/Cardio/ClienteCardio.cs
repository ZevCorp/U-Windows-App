using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Cardio;

/// <summary>
/// Leer, resumir y preguntar sobre las fotos de una <see cref="SesionCardio"/>. El «cómo se envía» se
/// inyecta: en la app es OpenAI; en el contrato, un modelo de mentira que anota lo que recibe.
/// </summary>
/// <remarks>
/// NO USA ConfigureAwait(false), y es a propósito: escribe en la sesión que el panel está pintando. Con
/// las continuaciones en el hilo de la interfaz, la sesión solo la toca un hilo.
/// </remarks>
public sealed class ClienteCardio
{
    /// <summary>
    /// El MISMO modelo de texto que ya usa Ü: el delegado de GPT-Live (<c>ProtocoloGptLive</c>). La
    /// familia gpt-5.x lee imágenes (ver <c>CapturaDePantalla</c>). <c>U_CARDIO_MODELO</c> lo cambia sin
    /// recompilar —p. ej. a <c>gpt-5.6-terra</c>, que también está en uso—.
    /// </summary>
    public const string ModeloPorDefecto = "gpt-5.6-luna";

    private const string Puerta = "https://api.openai.com/v1/responses";

    private readonly Func<string, CancellationToken, Task<string>> _enviar;
    private readonly string _modelo;

    /// <param name="enviar">Manda un cuerpo a la Responses API y devuelve la respuesta cruda (JSON).</param>
    public ClienteCardio(Func<string, CancellationToken, Task<string>> enviar, string modelo)
    {
        _enviar = enviar ?? throw new ArgumentNullException(nameof(enviar));
        _modelo = string.IsNullOrWhiteSpace(modelo) ? ModeloPorDefecto : modelo.Trim();
    }

    public string Modelo => _modelo;

    public static ClienteCardio DeLaApp()
    {
        string? pedido = Environment.GetEnvironmentVariable("U_CARDIO_MODELO");
        return new ClienteCardio(EnviarAOpenAIAsync, string.IsNullOrWhiteSpace(pedido) ? ModeloPorDefecto : pedido);
    }

    /// <summary>
    /// Lee lo que falte por leer, en lotes de 3, y rehace el resumen.
    /// </summary>
    /// <remarks>
    /// LO QUE FALTA es lo que no tiene lectura o quedó «sin leer»: una foto ya leída no se vuelve a
    /// pagar al agregar otras (promesa 357). Si un lote falla, lo leído antes se queda, se lanza
    /// diciendo QUÉ lote y POR QUÉ, y no se hace resumen: uno a medias parecería completo.
    /// </remarks>
    public async Task GenerarAsync(SesionCardio s, Action<string> progreso, CancellationToken ct)
    {
        var porLeer = s.Fotos.Where(f => f.Resultado == null || f.Resultado.Estado == EstadoDeFoto.SinLeer).ToList();
        int total = porLeer.Count;
        for (int i = 0; i < total; i += LecturaCardio.TamanoDeLote)
        {
            var lote = porLeer.Skip(i).Take(LecturaCardio.TamanoDeLote).ToList();
            int desde = i + 1, hasta = i + lote.Count;
            progreso(ReglaCardio.Progreso(desde, hasta, total));

            var ids = lote.Select(f => f.Id).ToList();
            string cuerpo = LecturaCardio.CuerpoAnalizar(_modelo, ids,
                lote.Select(f => "data:image/jpeg;base64," + Convert.ToBase64String(f.Jpeg)).ToList());
            string texto;
            try
            {
                texto = LecturaCardio.TextoDeLaRespuesta(await _enviar(cuerpo, ct));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception e)
            {
                string cuales = desde == hasta ? $"No se pudo leer la foto {desde}" : $"No se pudieron leer las fotos {desde}–{hasta}";
                throw new InvalidOperationException($"{cuales}: {Causa(e)}", e);
            }

            var resultados = LecturaCardio.Emparejar(texto, ids);
            for (int k = 0; k < lote.Count; k++) lote[k].Resultado = resultados[k];
            int sinLeer = resultados.Count(r => r.Estado == EstadoDeFoto.SinLeer);
            LogBus.Log("cardio", $"{ReglaCardio.Rango(desde, hasta)} de {total}: "
                + $"{resultados.Count(r => r.Estado == EstadoDeFoto.Cardiologia)} cardiológica(s), "
                + $"{resultados.Count(r => r.Estado == EstadoDeFoto.Omitida)} omitida(s), {sinLeer} sin leer");
        }

        var cardio = s.Fotos.Where(f => f.Resultado?.Estado == EstadoDeFoto.Cardiologia).Select(f => f.Resultado!).ToList();
        if (cardio.Count == 0)
        {
            // Nada que resumir no es un error ni una llamada: se dice tal cual.
            s.Resumen = "Ninguna de las fotos cargadas es material cardiológico, así que no hay nada que resumir.";
        }
        else
        {
            progreso("Generando resumen…");
            string resumen;
            try
            {
                resumen = LecturaCardio.TextoDeLaRespuesta(await _enviar(LecturaCardio.CuerpoResumir(_modelo, cardio), ct));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception e)
            {
                throw new InvalidOperationException($"Las fotos se leyeron, pero no se pudo generar el resumen: {Causa(e)}", e);
            }
            if (string.IsNullOrWhiteSpace(resumen))
                throw new InvalidOperationException("Las fotos se leyeron, pero el modelo devolvió el resumen vacío.");
            s.Resumen = resumen.Trim();
        }
        s.FotosDelResumen = s.Fotos.Select(f => f.Id).ToList();
    }

    /// <summary>Una pregunta sobre lo leído. Sin imágenes; la vuelta queda en el chat de la sesión.</summary>
    public async Task<string> PreguntarAsync(SesionCardio s, string pregunta, CancellationToken ct)
    {
        var resultados = s.Fotos.Where(f => f.Resultado != null).Select(f => f.Resultado!).ToList();
        string cuerpo = LecturaCardio.CuerpoPreguntar(_modelo, resultados, s.Resumen, s.Chat, pregunta);
        string respuesta;
        try
        {
            respuesta = LecturaCardio.TextoDeLaRespuesta(await _enviar(cuerpo, ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e)
        {
            throw new InvalidOperationException($"No se pudo responder: {Causa(e)}", e);
        }
        if (string.IsNullOrWhiteSpace(respuesta))
            throw new InvalidOperationException("No se pudo responder: el modelo devolvió una respuesta vacía.");
        s.Chat.Add(new VueltaDeChat { Pregunta = pregunta.Trim(), Respuesta = respuesta.Trim() });
        return respuesta.Trim();
    }

    /// <summary>La cadena entera, no solo el mensaje de fuera (patrón nº3).</summary>
    private static string Causa(Exception e)
    {
        var partes = new List<string>();
        for (var x = e; x != null; x = x.InnerException) partes.Add(x.Message);
        return string.Join(" ← ", partes.Distinct());
    }

    // ── La puerta real ────────────────────────────────────────────────────────────────────────────

    private static readonly HttpClient Red = new() { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>
    /// Manda un cuerpo a OpenAI con la clave que ya usa Ü (entorno, o la que entrega Graph, spec 045).
    /// </summary>
    /// <remarks>
    /// EL LOG NO LLEVA NI FOTOS NI TEXTO CLÍNICO: cuántas imágenes, cuántos bytes, qué status y cuánto
    /// tardó. El log de Ü se pega en los PR y vive en disco sin caducidad; estos datos caducan a las 24 h.
    /// </remarks>
    private static async Task<string> EnviarAOpenAIAsync(string cuerpo, CancellationToken ct)
    {
        string clave = Credenciales.ClavesDelBackend.DeLaApp(Credenciales.ClavesDelBackend.Voz);
        if (string.IsNullOrWhiteSpace(clave) && Credenciales.ClavesDelBackend.Viva is { } viva)
        {
            await viva.TraerAsync(ct);
            clave = Credenciales.ClavesDelBackend.DeLaApp(Credenciales.ClavesDelBackend.Voz);
        }
        if (string.IsNullOrWhiteSpace(clave))
            throw new InvalidOperationException("no hay clave de OpenAI: Graph no la entregó y OPENAI_API_KEY no está puesta. "
                + (Credenciales.ClavesDelBackend.Viva?.Estado ?? ""));

        int imagenes = CuantasVeces(cuerpo, "\"input_image\"");
        using var tiempo = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // Dos minutos y medio: tres fotos con detalle alto y razonamiento pueden tardar, pero una
        // espera sin tope deja el panel en «Leyendo…» para siempre si la red se queda colgada.
        tiempo.CancelAfter(TimeSpan.FromSeconds(150));

        var reloj = Stopwatch.StartNew();
        using var peticion = new HttpRequestMessage(HttpMethod.Post, Puerta)
        {
            Content = new StringContent(cuerpo, Encoding.UTF8, "application/json"),
        };
        peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", clave);
        HttpResponseMessage r;
        try
        {
            r = await Red.SendAsync(peticion, tiempo.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            LogBus.Log("cardio", $"OpenAI no contestó en 150 s ({imagenes} imagen(es), {cuerpo.Length / 1024} KB)");
            throw new TimeoutException("OpenAI no contestó en 150 s");
        }
        using (r)
        {
            string texto = await r.Content.ReadAsStringAsync(ct);
            LogBus.Log("cardio", $"OpenAI {(int)r.StatusCode} en {reloj.ElapsedMilliseconds} ms · {imagenes} imagen(es) · {cuerpo.Length / 1024} KB enviados");
            if (!r.IsSuccessStatusCode)
                throw new HttpRequestException($"OpenAI contestó {(int)r.StatusCode}: {MensajeDeError(texto)}");
            return texto;
        }
    }

    /// <summary>
    /// Solo <c>error.message</c> y recortado: el cuerpo de un error de OpenAI no trae las imágenes, pero
    /// un mensaje de error es lo que acaba en pantalla, y ahí no tiene que caber un JSON entero.
    /// </summary>
    private static string MensajeDeError(string texto)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(texto);
            if (doc.RootElement.TryGetProperty("error", out var e) && e.TryGetProperty("message", out var m))
            {
                string msg = m.GetString() ?? "";
                return msg.Length <= 200 ? msg : msg[..200] + "…";
            }
        }
        catch (System.Text.Json.JsonException) { }
        return "sin detalle";
    }

    private static int CuantasVeces(string texto, string aguja)
    {
        int n = 0;
        for (int i = texto.IndexOf(aguja, StringComparison.Ordinal); i >= 0; i = texto.IndexOf(aguja, i + aguja.Length, StringComparison.Ordinal)) n++;
        return n;
    }
}
