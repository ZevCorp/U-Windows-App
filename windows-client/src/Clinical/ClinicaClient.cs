using System.Net.Http;
using System.Text;
using System.Text.Json;
using U.WindowsClient.Cuenta;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Clinical;

/// <summary>
/// EL ÚNICO SITIO POR DONDE SE HABLA CON EL BACKEND CLÍNICO. Equivalente en C# de
/// <c>lib/api/clinical.ts</c> del portal, recortado a la cadena mínima.
/// </summary>
/// <remarks>
/// Contrato: <c>docs/backend-clinical-api-contract.md</c> (copia viva en el repo del portal). Cinco
/// llamadas y ninguna más:
///
///   GET  /api/clinical/templates                        ← qué plantillas hay
///   POST /api/clinical/encounters                       ← abre la consulta
///   POST /api/clinical/encounters/:id/transcript        ← lo que se dijo
///   POST /api/clinical/encounters/:id/generate-note     ← el backend organiza la nota
///   GET  /api/clinical/encounters/:id                   ← la consulta entera
///
/// UN SOLO EMBUDO, a propósito, y por la misma razón que lo dice el cliente del portal: las rutas y
/// los nombres de campo del contrato viven en un archivo, no repartidos por la interfaz. Cuando el
/// backend cambie algo, se cambia aquí y se acabó.
///
/// LA AUTENTICACIÓN ES DEL MÉDICO, no de la máquina: <c>Authorization: Bearer</c> con el token de
/// Supabase. El backend deriva de ahí el <c>doctor_id</c>, así que la consulta grabada en Windows
/// nace ya con dueño y aparece en el portal de esa persona sin ningún puente extra.
///
/// EL TEXTO CLÍNICO NO SE ESCRIBE EN EL LOG. Ni transcripciones, ni notas, ni nombres de paciente:
/// se anotan rutas, códigos y tamaños, que es lo que hace falta para diagnosticar.
/// </remarks>
public sealed class ClinicaClient
{
    private readonly string _baseUrl;
    private readonly SesionMiracle _sesion;
    private readonly HttpClient _http;

    public ClinicaClient(string baseUrl, SesionMiracle sesion, HttpMessageHandler? transporte = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _sesion = sesion;
        _http = transporte == null
            ? new HttpClient { Timeout = TimeSpan.FromSeconds(90) }
            : new HttpClient(transporte, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(90) };
    }

    // ── las cinco llamadas ───────────────────────────────────────────────────

    /// <summary>Las plantillas visibles para este médico: institucionales + las suyas.</summary>
    public async Task<IReadOnlyList<PlantillaClinica>> PlantillasAsync(
        string? especialidad = null, CancellationToken ct = default)
    {
        string ruta = "/api/clinical/templates"
            + (string.IsNullOrWhiteSpace(especialidad)
                ? "" : $"?specialty={Uri.EscapeDataString(especialidad)}");

        var raiz = await PedirAsync(HttpMethod.Get, ruta, null, ct);
        var lista = new List<PlantillaClinica>();
        if (raiz.TryGetProperty("templates", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in arr.EnumerateArray())
            {
                lista.Add(new PlantillaClinica(
                    Id: Texto(t, "id"),
                    Nombre: Texto(t, "name"),
                    Especialidad: Texto(t, "specialty"),
                    EsLaPorDefecto: t.TryGetProperty("is_default", out var d)
                                    && d.ValueKind == JsonValueKind.True));
            }
        }
        LogBus.Log("clinica", $"{lista.Count} plantilla(s)");
        return lista;
    }

    /// <summary>
    /// Abre la consulta y devuelve su id. El servidor registra aquí la declaración profesional de
    /// consentimiento (<c>consent_source: clinician_attestation</c>).
    /// </summary>
    public async Task<string> CrearEncounterAsync(string plantillaId,
        string tipo = "presencial", CancellationToken ct = default)
    {
        string cuerpo = JsonSerializer.Serialize(new
        {
            patient_id = (string?)null,
            consultation_type = tipo,
            template_id = plantillaId,
        });
        var raiz = await PedirAsync(HttpMethod.Post, "/api/clinical/encounters", cuerpo, ct);
        string id = Texto(raiz, "encounter_id");
        LogBus.Log("clinica", $"encounter {id} · {Texto(raiz, "status")}");
        return id;
    }

    /// <summary>Guarda lo que se dijo. Devuelve cuántos caracteres aceptó el backend.</summary>
    public async Task<int> GuardarTranscripcionAsync(string encounterId, string texto,
        CancellationToken ct = default)
    {
        string cuerpo = JsonSerializer.Serialize(new { transcript = texto });
        var raiz = await PedirAsync(HttpMethod.Post,
            $"/api/clinical/encounters/{Uri.EscapeDataString(encounterId)}/transcript", cuerpo, ct);

        int largo = raiz.TryGetProperty("transcript_length", out var l) && l.TryGetInt32(out int n)
            ? n : texto.Length;
        LogBus.Log("clinica", $"transcripción guardada · {largo} caracteres");
        return largo;
    }

    /// <summary>El backend organiza la nota con el snapshot de la plantilla y el LLM configurado.</summary>
    public async Task<NotaClinica> GenerarNotaAsync(string encounterId, CancellationToken ct = default)
    {
        var raiz = await PedirAsync(HttpMethod.Post,
            $"/api/clinical/encounters/{Uri.EscapeDataString(encounterId)}/generate-note", "{}", ct);
        var nota = NotaClinica.Leer(raiz.TryGetProperty("note_json", out var n) ? n : default);
        LogBus.Log("clinica", $"nota generada · {nota.Secciones.Count} sección(es) · "
                            + $"{nota.Avisos.Count} aviso(s)");
        return nota;
    }

    /// <summary>La consulta entera, con transcripción y nota si las tiene.</summary>
    public async Task<EncounterClinico> LeerEncounterAsync(string encounterId,
        CancellationToken ct = default)
    {
        var raiz = await PedirAsync(HttpMethod.Get,
            $"/api/clinical/encounters/{Uri.EscapeDataString(encounterId)}", null, ct);
        var e = raiz.TryGetProperty("encounter", out var x) ? x : raiz;
        return new EncounterClinico(
            Id: Texto(e, "id"),
            Estado: Texto(e, "status"),
            Transcripcion: Texto(e, "transcript"),
            Nota: e.TryGetProperty("note_json", out var n) && n.ValueKind == JsonValueKind.Object
                ? NotaClinica.Leer(n) : null);
    }

    // ── el camino ────────────────────────────────────────────────────────────

    /// <summary>
    /// Una llamada al backend clínico, con el token del médico y el envelope de error traducido.
    /// </summary>
    /// <remarks>
    /// SIN TOKEN NO SE LLAMA. Lanzar aquí con <c>UNAUTHORIZED</c> en vez de mandar la petición pelada
    /// evita que el 401 del servidor se confunda con «tu cuenta no tiene permiso»: son dos cosas
    /// distintas y solo una se arregla entrando otra vez.
    /// </remarks>
    private async Task<JsonElement> PedirAsync(HttpMethod metodo, string ruta, string? cuerpo,
        CancellationToken ct)
    {
        string token = await _sesion.TokenVigenteAsync(ct);
        if (token.Length == 0) throw new ErrorClinico("UNAUTHORIZED", 401);

        using var req = new HttpRequestMessage(metodo, _baseUrl + ruta);
        req.Headers.Add("Authorization", $"Bearer {token}");
        foreach (var (clave, valor) in _sesion.CabecerasDeAtribucion())
        {
            // La atribución de una llamada clínica es la clínica, no la del dictado.
            req.Headers.Add(clave, clave.Equals("X-Miracle-Feature", StringComparison.OrdinalIgnoreCase)
                ? "clinical" : valor);
        }
        if (cuerpo != null) req.Content = new StringContent(cuerpo, Encoding.UTF8, "application/json");

        using var res = await _http.SendAsync(req, ct);
        string texto = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode) throw ErrorDe(texto, (int)res.StatusCode);

        try { return JsonDocument.Parse(texto).RootElement.Clone(); }
        catch (JsonException)
        {
            // Un 200 con un cuerpo que no es JSON es el backend contestando otra cosa (una página de
            // error de la plataforma, casi siempre). Se dice así y no «error interno».
            throw new ErrorClinico("INTERNAL_ERROR", (int)res.StatusCode);
        }
    }

    /// <summary>
    /// El envelope <c>{ error: { code, message } }</c> del contrato → error tipado. Si no viene con
    /// esa forma, el código se deduce del HTTP: no se inventa uno que el backend no dijo.
    /// </summary>
    private static ErrorClinico ErrorDe(string cuerpo, int http)
    {
        string codigo = "";
        try
        {
            using var doc = JsonDocument.Parse(cuerpo);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                codigo = err.ValueKind == JsonValueKind.Object ? Texto(err, "code")
                       : err.ValueKind == JsonValueKind.String ? "" : "";
            }
        }
        catch (JsonException) { /* cuerpo sin JSON: el HTTP es lo único que hay */ }

        if (codigo.Length == 0)
        {
            codigo = http switch
            {
                401 or 403 => "UNAUTHORIZED",
                404 => "ENCOUNTER_NOT_FOUND",
                >= 500 => "INTERNAL_ERROR",
                _ => "",
            };
        }
        LogBus.Log("clinica", $"HTTP {http} · {(codigo.Length > 0 ? codigo : "sin código")}");
        return new ErrorClinico(codigo, http);
    }

    private static string Texto(JsonElement o, string campo) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(campo, out var v)
        && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}

// ── los modelos del contrato, solo lo que esta app usa ───────────────────────

public sealed record PlantillaClinica(string Id, string Nombre, string Especialidad, bool EsLaPorDefecto);

public sealed record SeccionDeNota(string Clave, string Titulo, string Contenido);

/// <summary>
/// La nota organizada. <c>sections</c> trae EXACTAMENTE las secciones del snapshot de la plantilla y
/// en su orden: aquí no se parte markdown ni se adivina estructura (regla 6 del contrato).
/// </summary>
public sealed record NotaClinica(
    string Resumen,
    IReadOnlyList<SeccionDeNota> Secciones,
    IReadOnlyList<string> Avisos,
    IReadOnlyList<string> SeccionesQueFaltan)
{
    public static NotaClinica Leer(JsonElement n)
    {
        if (n.ValueKind != JsonValueKind.Object)
            return new NotaClinica("", Array.Empty<SeccionDeNota>(),
                Array.Empty<string>(), Array.Empty<string>());

        var secciones = new List<SeccionDeNota>();
        if (n.TryGetProperty("sections", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in arr.EnumerateArray())
            {
                secciones.Add(new SeccionDeNota(
                    Clave: Str(s, "key"),
                    Titulo: Str(s, "label"),
                    // `content` y nunca value/text/body: lo dice el contrato con esas palabras.
                    Contenido: Str(s, "content")));
            }
        }

        return new NotaClinica(Str(n, "summary"), secciones,
            Lista(n, "warnings"), Lista(n, "missing_required_sections"));
    }

    private static string Str(JsonElement o, string campo) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(campo, out var v)
        && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static IReadOnlyList<string> Lista(JsonElement o, string campo)
    {
        if (o.ValueKind != JsonValueKind.Object || !o.TryGetProperty(campo, out var a)
            || a.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
        return a.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString() ?? "")
            .Where(x => x.Length > 0).ToList();
    }
}

public sealed record EncounterClinico(string Id, string Estado, string Transcripcion, NotaClinica? Nota);
