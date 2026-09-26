using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using U.WindowsClient.Cuenta;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Clinical;

/// <summary>
/// ARMA LA ENTRADA de <see cref="DocumentosDelPaciente.Construir"/> con lo que U sabe: la nota que se
/// ve, el paciente asociado, y el médico y su institución leídos de Supabase con SU token (spec 059).
/// </summary>
/// <remarks>
/// LAS MISMAS COLUMNAS QUE LA WEB: `profiles` (full_name, identification_number,
/// professional_registration, specialty_name, honorific, responsable_label) y `organizations`
/// (name, nit, address, city, phone, default_responsable_label), las dos bajo RLS. Lo que no se pueda
/// leer queda vacío y el papel lo omite (promesa 477): un papel sin membrete es mejor que no poder
/// imprimir.
/// </remarks>
public static class DatosDelPapel
{
    public static async Task<JsonElement> EntradaAsync(SesionMiracle sesion, string tipo, NotaClinica nota,
        Paciente? paciente, DateTime cuandoLocal, CancellationToken ct = default)
    {
        var perfil = await FilaAsync(sesion,
            $"/rest/v1/profiles?id=eq.{Uri.EscapeDataString(sesion.MedicoId)}&select=full_name,identification_number,"
            + "professional_registration,specialty_name,honorific,responsable_label", ct);
        var org = await FilaAsync(sesion,
            "/rest/v1/organizations?select=name,nit,address,city,phone,default_responsable_label&limit=1", ct);

        string responsable = Cad(perfil, "responsable_label") is { Length: > 0 } r ? r : Cad(org, "default_responsable_label");
        var entrada = new JsonObject
        {
            ["tipo"] = tipo,
            ["fecha"] = cuandoLocal.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture),
            ["org"] = new JsonObject
            {
                ["name"] = Cad(org, "name"), ["nit"] = Cad(org, "nit"), ["address"] = Cad(org, "address"),
                ["city"] = Cad(org, "city"), ["phone"] = Cad(org, "phone"),
            },
            ["medico"] = new JsonObject
            {
                ["nombre"] = Cad(perfil, "full_name") is { Length: > 0 } n ? n : sesion.MedicoNombre,
                ["documento"] = Cad(perfil, "identification_number"),
                ["registro"] = Cad(perfil, "professional_registration"),
                ["especialidad"] = Cad(perfil, "specialty_name"),
                ["honorifico"] = Cad(perfil, "honorific"),
                ["responsable"] = responsable,
            },
            ["paciente"] = paciente == null ? new JsonObject() : new JsonObject
            {
                ["nombre"] = paciente.Nombre,
                ["documento"] = paciente.Documento,
                ["edad"] = int.TryParse(paciente.Edad, NumberStyles.Integer, CultureInfo.InvariantCulture, out var edad) ? edad : null,
                ["sexo"] = paciente.Sexo,
                ["eps"] = paciente.Eps,
            },
            // La nota tal como está —con el plan y el egreso que se corrigieron aquí—, del JSON real.
            ["nota"] = nota.ComoNodo(),
        };
        using var doc = JsonDocument.Parse(entrada.ToJsonString());
        return doc.RootElement.Clone();
    }

    private static async Task<JsonElement> FilaAsync(SesionMiracle sesion, string ruta, CancellationToken ct)
    {
        try
        {
            var (http, cuerpo) = await sesion.RestAsync(HttpMethod.Get, ruta, ct: ct);
            if (http is < 200 or >= 300) { LogBus.Log("papeles", $"sin datos para el papel · HTTP {http}"); return default; }
            using var doc = JsonDocument.Parse(cuerpo);
            return doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0
                ? doc.RootElement[0].Clone() : default;
        }
        catch (Exception e)
        {
            LogBus.Log("papeles", $"sin datos para el papel: {e.GetType().Name}: {e.Message}");
            return default;
        }
    }

    private static string Cad(JsonElement o, string campo) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.String
            ? (v.GetString() ?? "").Trim() : "";
}
