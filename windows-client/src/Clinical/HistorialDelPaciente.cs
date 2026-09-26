using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using U.WindowsClient.Cuenta;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Clinical;

/// <summary>Una consulta anterior del paciente, con lo justo para contestar «¿qué le mandé?».</summary>
public sealed record ConsultaAnterior(string Id, string Fecha, string Motivo, string Diagnostico,
    IReadOnlyList<string> Medicamentos, IReadOnlyList<string> Controles);

/// <summary>
/// EL HISTORIAL DE UN PACIENTE PARA LA VOZ (spec 060): «Ü, ¿qué le mandé la última vez?» se contesta
/// con DATOS —las consultas del espejo y el plan de cada una en el backend clínico—, no mirando la
/// web. Promesas 480, 482-484.
/// </summary>
/// <remarks>
/// LO QUE VIAJA A LA VOZ SE MINIMIZA. El dueño decidió (2026-09-26) que el historial vaya directo al
/// proveedor de voz; esta clase decide QUÉ: las últimas 3 consultas como mucho, con fecha, motivo,
/// diagnóstico, medicamentos y controles, recortado. NUNCA la transcripción: lo que se habló en la
/// consulta no hace falta para «qué le mandé» y es lo más sensible que hay.
///
/// NADA CLÍNICO AL LOG: se anota cuántas consultas se leyeron, no qué decían.
/// </remarks>
public static class HistorialDelPaciente
{
    /// <summary>El tope, lo pida quien lo pida.</summary>
    public const int Maximo = 3;
    private const int LargoMaximo = 1500;

    public static async Task<IReadOnlyList<ConsultaAnterior>> LeerAsync(SesionMiracle sesion, ClinicaClient clinica,
        string pacienteId, int cuantas = Maximo, CancellationToken ct = default)
    {
        var fuera = new List<ConsultaAnterior>();
        if (string.IsNullOrWhiteSpace(pacienteId)) return fuera;
        int n = Math.Clamp(cuantas, 1, Maximo);
        try
        {
            // Sin transcript en el select: lo que no se pide no puede viajar.
            var (http, cuerpo) = await sesion.RestAsync(HttpMethod.Get,
                $"/rest/v1/consultations?select=id,fecha,motivo,resumen&patient_id=eq.{Uri.EscapeDataString(pacienteId)}"
                + $"&order=fecha.desc&limit={n}", ct: ct);
            if (http is < 200 or >= 300) { LogBus.Log("historial", $"no se pudo leer el historial · HTTP {http}"); return fuera; }

            using var doc = JsonDocument.Parse(cuerpo);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return fuera;
            foreach (var fila in doc.RootElement.EnumerateArray().Take(n))
            {
                string id = Cad(fila, "id");
                if (id.Length == 0) continue;
                var meds = new List<string>();
                var controles = new List<string>();
                string diagnostico = "";
                try
                {
                    // EL PLAN VIVE EN EL BACKEND CLÍNICO, no en el espejo: de ahí sale «qué le mandé».
                    var enc = await clinica.LeerEncounterAsync(id, ct);
                    if (enc.Nota != null)
                    {
                        var eg = EgresoDeLaNota.Leer(enc.Nota.Crudo);
                        meds.AddRange(eg.Medicamentos.Select(m => string.Join(" ", new[] { m.Nombre, m.Dosis, m.Frecuencia, m.Duracion }
                            .Select(x => x.Trim()).Where(x => x.Length > 0))).Where(x => x.Length > 0));
                        controles.AddRange(eg.Seguimiento.Select(x => x.Trim()).Where(x => x.Length > 0));
                        diagnostico = Diagnostico(enc.Nota);
                    }
                }
                catch (Exception e)
                {
                    LogBus.Log("historial", $"sin el plan de una consulta: {e.GetType().Name}");
                }
                fuera.Add(new ConsultaAnterior(id, Cad(fila, "fecha"),
                    Cad(fila, "motivo") is { Length: > 0 } motivo ? motivo : Cad(fila, "resumen"),
                    diagnostico, meds, controles));
            }
            LogBus.Log("historial", $"{fuera.Count} consulta(s) anteriores leídas para la voz");
        }
        catch (Exception e)
        {
            LogBus.Log("historial", $"no se pudo leer el historial: {e.GetType().Name}: {e.Message}");
        }
        return fuera;
    }

    /// <summary>El texto que se le entrega a la voz. Recortado: la voz lo dice, no lo archiva.</summary>
    public static string ParaLaVoz(string paciente, IReadOnlyList<ConsultaAnterior> consultas)
    {
        string quien = string.IsNullOrWhiteSpace(paciente) ? "este paciente" : paciente.Trim();
        if (consultas.Count == 0) return $"No encuentro consultas anteriores de {quien}.";

        var sb = new StringBuilder();
        sb.Append($"Últimas {consultas.Count} consulta(s) de {quien}, de la más reciente a la más vieja:");
        foreach (var c in consultas.Take(Maximo))
        {
            sb.Append("\n· ");
            string fecha = FechaCorta(c.Fecha);
            if (fecha.Length > 0) sb.Append(fecha).Append(". ");
            if (c.Motivo.Length > 0) sb.Append("Motivo: ").Append(Recortar(c.Motivo, 160)).Append(". ");
            if (c.Diagnostico.Length > 0) sb.Append("Diagnóstico: ").Append(Recortar(c.Diagnostico, 160)).Append(". ");
            sb.Append(c.Medicamentos.Count > 0
                ? "Medicamentos: " + Recortar(string.Join("; ", c.Medicamentos), 300) + ". "
                : "Sin medicamentos registrados. ");
            if (c.Controles.Count > 0) sb.Append("Controles: ").Append(Recortar(string.Join("; ", c.Controles), 160)).Append('.');
        }
        string texto = sb.ToString().TrimEnd();
        return texto.Length <= LargoMaximo ? texto : texto[..(LargoMaximo - 1)] + "…";
    }

    /// <summary>
    /// A quién se refiere el médico. Uno que casa: ese. Varios: el que casa EXACTO por nombre, y si no
    /// hay uno solo exacto, NO se elige — se nombran los candidatos para que el médico diga cuál.
    /// </summary>
    public static (Paciente? Elegido, string Motivo) ElegirPaciente(IReadOnlyList<Paciente> candidatos, string dicho)
    {
        string oido = (dicho ?? "").Trim();
        if (candidatos.Count == 0) return (null, $"No encuentro ningún paciente que se llame «{oido}».");
        if (candidatos.Count == 1) return (candidatos[0], "");
        string clave = Plano(oido);
        var exactos = candidatos.Where(p => Plano(p.Nombre) == clave).ToList();
        if (exactos.Count == 1) return (exactos[0], "");
        var nombrados = candidatos.Take(4).Select(p => p.Documento.Length > 0 ? $"{p.Nombre} ({p.Documento})" : p.Nombre);
        return (null, $"Hay {candidatos.Count} pacientes que casan con «{oido}»: {string.Join(", ", nombrados)}. ¿Cuál?");
    }

    /// <summary>La sección de diagnóstico o impresión de la nota, si la hay.</summary>
    private static string Diagnostico(NotaClinica nota)
    {
        foreach (var s in nota.Secciones)
        {
            string nombre = ConceptosClinicos.SinTildes($"{s.Titulo} {s.Clave}".ToLowerInvariant());
            if ((nombre.Contains("diagn") || nombre.Contains("impresi")) && s.Contenido.Trim().Length > 0)
                return s.Contenido.Trim();
        }
        return "";
    }

    private static string FechaCorta(string iso)
    {
        if (!DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)) return "";
        return DocumentosDelPaciente.FechaLarga(d.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private static string Plano(string s) => ConceptosClinicos.SinTildes((s ?? "").Trim().ToLowerInvariant());

    private static string Recortar(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private static string Cad(JsonElement o, string campo) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.String
            ? (v.GetString() ?? "").Trim() : "";
}
