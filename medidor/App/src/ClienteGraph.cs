using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Medidor.App;

/// <summary>Lo que el servidor contestó a un lote: cuántas filas por colección hay que sacar del
/// spool (aceptadas + duplicadas), y cuáles rechazó como veneno (para envenenarlas).</summary>
public sealed record RespuestaDeLote(
    bool Ok, int Codigo,
    IReadOnlyList<(string Coleccion, long Seq)> Confirmar,
    IReadOnlyList<(string Coleccion, long Seq)> Veneno,
    int? ConfigVersion, int? HmacVersion, long? DesfaseMs, int? RetryAfterS);

/// <summary>
/// El caño con Graph. Todo saliente, X-API-Key en cada request — la misma topología que U.exe
/// (nunca un puerto entrante en la máquina del médico; funciona detrás del firewall del hospital).
/// Timeout corto y errores que NO tumban: si Graph no contesta, el lote se queda en el spool y se
/// reintenta. Perder la red no puede perder datos.
/// </summary>
public sealed class ClienteGraph
{
    private readonly HttpClient _http;
    private readonly string _apiKey;

    public ClienteGraph(string baseUrl, string apiKey)
    {
        _apiKey = apiKey;
        _http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(15) };
    }

    public async Task<JsonDocument?> EnrolarAsync(string codigo, string machineName, string osVersion, string appVersion)
    {
        var cuerpo = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["enrollment_code"] = codigo,
            ["machine_name"] = machineName,
            ["os_version"] = osVersion,
            ["app_version"] = appVersion,
        });
        var (ok, codigoHttp, texto) = await PostAsync("/api/v1/metrics/enroll", cuerpo);
        if (!ok)
        {
            Registro.Anota("enroll", $"falló ({codigoHttp}): el código no fue aceptado");
            return null;
        }
        return JsonDocument.Parse(texto);
    }

    public async Task<JsonDocument?> ConfigAsync(string deviceId, int configVersion, int hmacVersion)
    {
        var (ok, codigo, texto) = await GetAsync(
            $"/api/v1/metrics/config?device_id={Uri.EscapeDataString(deviceId)}&config_version={configVersion}&hmac_version={hmacVersion}");
        if (!ok) { Registro.Anota("config", $"sin config ({codigo})"); return null; }
        return JsonDocument.Parse(texto);
    }

    public async Task<RespuestaDeLote> EnviarLoteAsync(string cuerpo, LoteTomado lote)
    {
        var (ok, codigo, texto) = await PostAsync("/api/v1/metrics/batch", cuerpo);
        if (!ok)
        {
            // 403 = device pausado/retirado; el que llama decide pausar. El resto: reintentar.
            return new RespuestaDeLote(false, codigo, Array.Empty<(string, long)>(), Array.Empty<(string, long)>(),
                null, null, null, RetryAfterDe(codigo));
        }

        // El servidor aceptó el lote entero (200): se confirma todo lo que iba en él, y se envenena
        // lo que rechazó por fila. El «aceptar y verificar después» del aprendizaje nº19: solo tras
        // el 200 se toca el spool.
        var confirmar = TodoElLote(lote);
        var veneno = new List<(string, long)>();
        int? cfg = null, hmac = null; long? desfase = null;
        try
        {
            using var doc = JsonDocument.Parse(texto);
            var raiz = doc.RootElement;
            if (raiz.TryGetProperty("rejected", out var rechazadas) && rechazadas.ValueKind == JsonValueKind.Array)
                foreach (var r in rechazadas.EnumerateArray())
                {
                    var col = r.GetProperty("col").GetString();
                    var seq = r.GetProperty("seq").GetInt64();
                    if (col != null) veneno.Add((col, seq));
                }
            if (raiz.TryGetProperty("config_version", out var cv) && cv.ValueKind == JsonValueKind.Number) cfg = cv.GetInt32();
            if (raiz.TryGetProperty("hmac_version", out var hv) && hv.ValueKind == JsonValueKind.Number) hmac = hv.GetInt32();
            if (raiz.TryGetProperty("clock_skew_ms", out var cs) && cs.ValueKind == JsonValueKind.Number) desfase = cs.GetInt64();
        }
        catch (Exception e) { Registro.Excepcion("batch", e); }

        // El veneno no se confirma (se saca aparte); todo lo demás sí.
        var venenoSet = veneno.ToHashSet();
        confirmar = confirmar.Where(c => !venenoSet.Contains(c)).ToList();
        return new RespuestaDeLote(true, codigo, confirmar, veneno, cfg, hmac, desfase, null);
    }

    private static List<(string, long)> TodoElLote(LoteTomado lote) =>
        lote.Turnos.Select(f => ("turnos", f.Seq))
            .Concat(lote.Muestras.Select(f => ("muestras", f.Seq)))
            .Concat(lote.Eventos.Select(f => ("eventos", f.Seq)))
            .Concat(lote.Visitas.Select(f => ("visitas", f.Seq)))
            .ToList();

    private static int? RetryAfterDe(int codigo) => codigo == (int)HttpStatusCode.TooManyRequests ? 60 : null;

    private async Task<(bool Ok, int Codigo, string Texto)> PostAsync(string ruta, string cuerpo)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, ruta)
            { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };
            req.Headers.Add("X-API-Key", _apiKey);
            using var resp = await _http.SendAsync(req);
            var texto = await resp.Content.ReadAsStringAsync();
            return ((int)resp.StatusCode < 300, (int)resp.StatusCode, texto);
        }
        catch (Exception e) { Registro.Excepcion("http", e); return (false, 0, ""); }
    }

    private async Task<(bool Ok, int Codigo, string Texto)> GetAsync(string ruta)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, ruta);
            req.Headers.Add("X-API-Key", _apiKey);
            using var resp = await _http.SendAsync(req);
            var texto = await resp.Content.ReadAsStringAsync();
            return ((int)resp.StatusCode < 300, (int)resp.StatusCode, texto);
        }
        catch (Exception e) { Registro.Excepcion("http", e); return (false, 0, ""); }
    }
}
