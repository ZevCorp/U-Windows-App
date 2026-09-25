using System.Text.Json;

namespace U.Ciclo;

/// <summary>
/// Las claves de pago no viajan en el .exe (la misma decisión de main, spec 045): primero el entorno, y lo
/// que falte se le pide a Graph con GRAPH_API_KEY. Nunca se escriben en el log ni en disco.
/// </summary>
public static class Claves
{
    public const string GraphPorDefecto = "https://graph-eight-pied.vercel.app";

    public static (string? OpenAI, string? TypeSafe, string Estado) Traer()
    {
        string? openai = Entorno("OPENAI_API_KEY"), typesafe = Entorno("TYPESAFE_API_KEY");
        if (openai != null && typesafe != null) return (openai, typesafe, "las dos claves del entorno");

        string? graph = Entorno("GRAPH_API_KEY");
        if (graph == null) return (openai, typesafe, "faltan claves y no hay GRAPH_API_KEY para pedirlas");
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            using var req = new HttpRequestMessage(HttpMethod.Get, (Entorno("GRAPH_BASE_URL") ?? GraphPorDefecto).TrimEnd('/') + "/api/v1/agent/claves");
            req.Headers.Add("X-API-Key", graph);
            using var res = http.Send(req);
            if (!res.IsSuccessStatusCode) return (openai, typesafe, $"Graph contestó HTTP {(int)res.StatusCode} al pedir las claves");
            using var doc = JsonDocument.Parse(new StreamReader(res.Content.ReadAsStream()).ReadToEnd());
            openai ??= Campo(doc, "openai");
            typesafe ??= Campo(doc, "typesafe");
            return (openai, typesafe, $"claves: OpenAI {(openai != null ? "sí" : "NO")} · TypeSafe {(typesafe != null ? "sí" : "NO")}");
        }
        catch (Exception e)
        {
            string causa = "";
            for (var x = e; x != null; x = x.InnerException) causa += (causa.Length > 0 ? " ← " : "") + $"{x.GetType().Name}: {x.Message}";
            return (openai, typesafe, "no pude pedirle las claves a Graph: " + causa);
        }
    }

    private static string? Campo(JsonDocument d, string c) =>
        d.RootElement.TryGetProperty(c, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString()!.Trim() : null;

    private static string? Entorno(string n)
    {
        string? v = Environment.GetEnvironmentVariable(n);
        if (string.IsNullOrWhiteSpace(v)) try { v = Environment.GetEnvironmentVariable(n, EnvironmentVariableTarget.User); } catch { v = null; }
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }
}
