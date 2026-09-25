using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace U.Ciclo;

/// <summary>Lo que decidió Jev, ya validado. <see cref="Numero"/> es 0 cuando no se pulsa nada.</summary>
public sealed record Eleccion(bool Pulsar, int Numero, double Confianza, double Cumplido, string Porque);

/// <summary>
/// JEV (TypeSafe, systemone): elige UN número entre los accionables de la pantalla. Es la única pieza del
/// ciclo de main que ya estaba a la altura —200-263 ms en caliente, medido el 2026-09-24— y se conserva su
/// concepto: una llamada, tres preguntas (qué puerta, ¿ya está cumplido?, ¿es peligroso?).
/// </summary>
public static class Jev
{
    public const string Url = "https://api.typesafe.ai/v1/systemone";
    public const string ModeloPorDefecto = "jev-latest";
    public const double CumplidoMinimo = 0.70;
    public const double PeligroMaximo = 0.50;

    /// <summary>El cuerpo de la pregunta (promesa 435). La clave NO va aquí: va en la cabecera.</summary>
    public static string Cuerpo(string pantalla, string objetivo, IReadOnlyList<Accionable> ofrecidas, string modelo)
    {
        var estado = new StringBuilder();
        estado.Append("Pantalla actual: ").Append(pantalla).Append('\n');
        estado.Append("Lo que se quiere conseguir: ").Append(objetivo).Append('\n');
        estado.Append("Accionables en esta pantalla, en orden de lectura:\n");
        foreach (var a in ofrecidas) estado.Append("  - ").Append(a.Id).Append('\n');

        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("state", estado.ToString());
            w.WriteString("model", string.IsNullOrWhiteSpace(modelo) ? ModeloPorDefecto : modelo);
            w.WriteStartObject("questions");

            w.WriteStartObject("puerta");
            w.WriteString("type", "choice");
            w.WriteString("instructions",
                $"¿Qué accionable de esta pantalla hay que pulsar AHORA para avanzar hacia «{objetivo}»? Elige solo entre los listados.");
            w.WriteStartObject("criteria");
            // Los ids son únicos por construcción (promesa 431): llevan el número delante.
            foreach (var a in ofrecidas) w.WriteNull(a.Id);
            w.WriteEndObject();
            w.WriteEndObject();

            w.WriteStartObject("cumplido");
            w.WriteString("type", "noul");
            w.WriteString("instructions", "¿El objetivo descrito en el estado YA está cumplido en esta pantalla, sin pulsar nada más?");
            w.WriteStartObject("criteria");
            w.WriteString("true", "Lo que se quería conseguir ya se ve conseguido en esta pantalla");
            w.WriteString("false", "Todavía falta pulsar algo para conseguirlo");
            w.WriteEndObject();
            w.WriteEndObject();

            w.WriteStartObject("peligro");
            w.WriteString("type", "noul");
            w.WriteString("instructions",
                "¿Pulsar el accionable elegido sería irreversible o peligroso: enviar, eliminar, pagar, confirmar, cerrar sin guardar?");
            w.WriteStartObject("criteria");
            w.WriteString("true", "Deja un efecto que no se puede deshacer o que afecta a otros");
            w.WriteString("false", "Navegar, abrir, seleccionar o mirar: se puede volver atrás");
            w.WriteEndObject();
            w.WriteEndObject();

            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// La respuesta, validada ENTERA (promesa 434). Lo que promete el servidor se comprueba: un número que
    /// no se ofreció, una confianza baja, «ya está cumplido» o «es peligroso» paran el ciclo, y el porqué dice cuál.
    /// </summary>
    public static Eleccion Interpretar(string json, IReadOnlyList<Accionable> ofrecidas, double umbral)
    {
        string elegida; double conf, cumplido = 0, peligro = 0;
        try
        {
            using var doc = JsonDocument.Parse(json ?? "");
            var answers = doc.RootElement.GetProperty("answers");
            var p = answers.GetProperty("puerta");
            elegida = p.GetProperty("choice").GetString() ?? "";
            conf = p.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetDouble() : 0;
            cumplido = Noul(answers, "cumplido");
            peligro = Noul(answers, "peligro");
        }
        catch (Exception e)
        {
            return new Eleccion(false, 0, 0, 0, $"la respuesta de Jev no se entiende ({e.GetType().Name}: {e.Message})");
        }

        var a = ofrecidas.FirstOrDefault(x => string.Equals(x.Id, elegida, StringComparison.Ordinal));
        string C(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);

        if (a == null)
            return new Eleccion(false, 0, conf, cumplido, $"Jev contestó «{elegida}», que no se ofreció entre los {ofrecidas.Count} accionables");
        if (cumplido >= CumplidoMinimo)
            return new Eleccion(false, a.Numero, conf, cumplido, $"Jev dice que el objetivo ya está cumplido ({C(cumplido)})");
        if (peligro >= PeligroMaximo)
            return new Eleccion(false, a.Numero, conf, cumplido, $"Jev dice que pulsar «{a.Nombre}» es peligroso ({C(peligro)}): no lo pulso sin que me lo confirmes");
        if (conf < umbral)
            return new Eleccion(false, a.Numero, conf, cumplido, $"Jev eligió «{a.Nombre}» con confianza {C(conf)}, por debajo de {C(umbral)}");
        return new Eleccion(true, a.Numero, conf, cumplido, $"Jev eligió «{a.Nombre}» ({C(conf)})");
    }

    private static double Noul(JsonElement answers, string id) =>
        answers.TryGetProperty(id, out var n) && n.ValueKind == JsonValueKind.Object
        && n.TryGetProperty("noul", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
}

/// <summary>
/// La conexión con TypeSafe, VIVA. La primera llamada de la medición costó 625 ms y las siguientes ~220:
/// la diferencia es abrir TLS. Por eso se calienta al arrancar y la conexión no se cierra.
/// </summary>
public sealed class ClienteJev : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _clave;
    public string Modelo { get; init; } = Jev.ModeloPorDefecto;
    public double Umbral { get; init; } = 0.60;

    public ClienteJev(string clave, int plazoMs = 3000)
    {
        _clave = clave ?? throw new ArgumentNullException(nameof(clave));
        _http = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(30),
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
            KeepAlivePingDelay = TimeSpan.FromSeconds(20),
            AutomaticDecompression = DecompressionMethods.All,
        })
        { Timeout = TimeSpan.FromMilliseconds(plazoMs) };
    }

    public string Preguntar(string cuerpo)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, Jev.Url) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _clave);
        using var res = _http.Send(req);
        string texto = new StreamReader(res.Content.ReadAsStream()).ReadToEnd();
        if (!res.IsSuccessStatusCode)
            // Sin el cuerpo de la petición: el log se pega en los PR.
            throw new HttpRequestException($"TypeSafe contestó HTTP {(int)res.StatusCode}: {(texto.Length > 200 ? texto[..200] : texto)}");
        return texto;
    }

    public Eleccion Decidir(string pantalla, string objetivo, IReadOnlyList<Accionable> ofrecidas)
    {
        if (ofrecidas.Count == 0) return new Eleccion(false, 0, 0, 0, "no hay ningún accionable en esta pantalla");
        try { return Jev.Interpretar(Preguntar(Jev.Cuerpo(pantalla, objetivo, ofrecidas, Modelo)), ofrecidas, Umbral); }
        catch (Exception e)
        {
            string causa = "";
            for (var x = e; x != null; x = x.InnerException) causa += (causa.Length > 0 ? " ← " : "") + $"{x.GetType().Name}: {x.Message}";
            return new Eleccion(false, 0, 0, 0, "Jev no contestó: " + causa);
        }
    }

    /// <summary>Abre la conexión antes de que haga falta. Devuelve lo que tardó, para el log.</summary>
    public long Calentar()
    {
        var r = Stopwatch.StartNew();
        var una = new[] { new Accionable(1, "Aceptar", "Button", new Caja(0, 0, 1, 1)) };
        try { Preguntar(Jev.Cuerpo("calentando", "calentar la conexión", una, Modelo)); } catch { /* calentar no decide nada */ }
        return r.ElapsedMilliseconds;
    }

    public void Dispose() => _http.Dispose();
}
