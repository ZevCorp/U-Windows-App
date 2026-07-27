using System.Net.Http;
using System.Text.Json;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Clinical;

/// <summary>Un dato clínico listo para colocar, con la frase que lo justifica.</summary>
public sealed record ClinicalValue(string Concept, string Value, string Evidence);

/// <summary>
/// Trae los signos vitales de la consulta que el médico emparejó con este equipo.
///
/// El puente es un CÓDIGO de ocho caracteres, no una credencial: el portal lo genera
/// atado a una consulta y el médico lo teclea aquí. Este equipo nunca tiene la sesión
/// del médico — sería una credencial real suelta en una máquina de hospital — y lo que
/// recibe son nueve números, jamás la nota ni el paciente.
///
/// Y solo hay datos DESPUÉS de que el médico guarda la nota. Es deliberado: estos
/// valores acaban escritos en la historia clínica, así que pasan por sus ojos primero.
/// Mientras no la guarde, esto devuelve vacío y no hay nada que ofrecer.
/// </summary>
public sealed class ClinicalBridge
{
    // El portal donde vive la consulta. Sobrescribible por si el demo corre contra otro
    // despliegue; sin variable, el de producción.
    private static string BaseUrl =>
        (Environment.GetEnvironmentVariable("MIRACLE_PORTAL_URL") ?? "https://itsmiracleai.com.co")
        .TrimEnd('/');

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>El código emparejado, o "" si no hay ninguno. Ocho caracteres, sin ambigüedades.</summary>
    public string Code { get; private set; } = "";

    public bool Active => Code.Length == 8;

    /// <summary>La última revisión traída. Sirve para no volver a ofrecer lo mismo.</summary>
    public string LastRev { get; private set; } = "";

    /// <summary>
    /// El portal dijo que este código ya no sirve (caducó, se revocó, o se generó otro).
    /// Cuando pasa, se deja de preguntar: insistir contra un código muerto es ruido.
    /// </summary>
    public bool Stopped { get; private set; }

    public void Pair(string code)
    {
        Code = (code ?? "").Trim().ToUpperInvariant();
        LastRev = "";
        Stopped = false;
        LogBus.Log("clinico", Active
            ? $"emparejado con la consulta (código {Code})"
            : "código inválido: son 8 caracteres");
    }

    public void Unpair()
    {
        LogBus.Log("clinico", "desemparejado");
        Code = "";
        LastRev = "";
        Stopped = false;
    }

    /// <summary>
    /// Pide los valores. Devuelve lista vacía si no hay nada nuevo, si el código murió,
    /// o si el portal no responde — nunca lanza: un fallo de red no puede tumbar nada
    /// de lo que el operador esté haciendo en SAP.
    /// </summary>
    public async Task<IReadOnlyList<ClinicalValue>> FetchAsync(CancellationToken ct)
    {
        if (!Active || Stopped) return Array.Empty<ClinicalValue>();

        try
        {
            using var res = await Http.GetAsync($"{BaseUrl}/api/agent/values?code={Code}", ct);

            if ((int)res.StatusCode == 404 || (int)res.StatusCode == 409)
            {
                Stopped = true;
                LogBus.Log("clinico", $"el código dejó de servir (HTTP {(int)res.StatusCode}); se deja de preguntar");
                return Array.Empty<ClinicalValue>();
            }
            if (!res.IsSuccessStatusCode)
            {
                LogBus.Log("clinico", $"el portal respondió HTTP {(int)res.StatusCode}");
                return Array.Empty<ClinicalValue>();
            }

            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;

            string rev = root.TryGetProperty("rev", out var r) ? r.GetString() ?? "" : "";
            if (!root.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Object)
                return Array.Empty<ClinicalValue>();

            root.TryGetProperty("evidence", out var evidence);

            var list = new List<ClinicalValue>();
            foreach (var p in values.EnumerateObject())
            {
                if (p.Value.ValueKind != JsonValueKind.String) continue;
                string v = p.Value.GetString() ?? "";
                if (v.Length == 0) continue;

                string ev = "";
                if (evidence.ValueKind == JsonValueKind.Object
                    && evidence.TryGetProperty(p.Name, out var e)
                    && e.ValueKind == JsonValueKind.String)
                    ev = e.GetString() ?? "";

                list.Add(new ClinicalValue(p.Name, v, ev));
            }

            LastRev = rev;
            LogBus.Log("clinico", $"la consulta trae {list.Count} dato(s) · rev={rev}");
            return list;
        }
        catch (OperationCanceledException) { return Array.Empty<ClinicalValue>(); }
        catch (Exception e)
        {
            LogBus.Log("clinico", $"no se pudo consultar el portal: {e.Message}");
            return Array.Empty<ClinicalValue>();
        }
    }
}
