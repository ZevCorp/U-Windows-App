using System.Net.Http;
using System.Text;
using System.Text.Json;
using U.WindowsClient.Cuenta;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Clinical;

/// <summary>
/// «TU SUGERIDA»: la plantilla que el médico fijó, guardada donde el portal la lee. Promesa 190.
/// </summary>
/// <remarks>
/// LA MISMA TABLA QUE EL PORTAL, y ese es todo el punto: `public.user_template_preferences`
/// (`20260811141100_user_template_preferences.sql`). Fijar la sugerida en Windows tiene que verse en
/// el navegador y al revés, porque es UNA preferencia de UNA persona — dos tablas serían dos
/// médicos distintos con la misma cara.
///
/// UNA POR ESPECIALIDAD: la clave primaria es `(user_id, specialty_code)`, así que el pin es de la
/// especialidad de la plantilla, no del médico. Se manda el `specialty_code` de la plantilla que se
/// fija, igual que hace `setTemplatePreference` en el portal.
///
/// EL `user_id` NO SE MANDA, y no es un olvido: la columna es `default auth.uid()` y la RLS exige
/// `user_id = auth.uid()` para leer, insertar y actualizar. Mandarlo desde el cliente sería dejar
/// que el cliente diga de quién es la preferencia — la misma frontera que ya respeta el espejo de
/// consultas con `organization_id` y `medico_id`.
///
/// UN FALLO AQUÍ NO PUEDE TUMBAR NADA. Si no se puede leer el pin, se graba con el siguiente eslabón
/// de <see cref="ReglaDeLaPlantilla"/>; si no se puede guardar, se dice y la consulta sigue. Una
/// preferencia perdida es una molestia; una consulta que no arranca por una preferencia es una
/// avería.
/// </remarks>
public static class SugeridaDelMedico
{
    /// <summary>
    /// El cuerpo del upsert, sin red. Es lo que juzga la promesa 190: un POST contesta 201 igual
    /// aunque el cuerpo vaya sin especialidad, y entonces el pin se guardaría en la fila equivocada.
    /// </summary>
    public static string Fila(string especialidad, string plantillaId) =>
        JsonSerializer.Serialize(new
        {
            specialty_code = Aplanar(especialidad),
            template_id = plantillaId,
            updated_at = DateTimeOffset.UtcNow.ToString("o"),
        });

    /// <summary>
    /// `Medicina de Urgencias` → `medicina_de_urgencias`. La misma normalización que
    /// <c>normalizeSpecialtyCode</c> del portal: si aquí se guardara con otra forma, el pin puesto
    /// en Windows sería invisible desde el navegador y al revés (forma nº16 de fallar).
    /// </summary>
    public static string Aplanar(string? especialidad)
    {
        string plano = Navigation.Nombres.Aplanar(especialidad ?? "").Trim();
        var sb = new StringBuilder(plano.Length);
        foreach (char c in plano)
        {
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '_') sb.Append('_');
        }
        return sb.ToString().Trim('_');
    }

    /// <summary>La plantilla fijada para esa especialidad, o vacío si no hay ninguna.</summary>
    public static async Task<string> LeerAsync(SesionMiracle sesion, HttpClient http,
        string especialidad, CancellationToken ct = default)
    {
        string token = await sesion.TokenVigenteAsync(ct);
        if (token.Length == 0) return "";

        try
        {
            // Sin filtro por user_id: lo pone la RLS. Filtrar aquí sería una segunda opinión sobre
            // quién ve qué, que es la misma razón por la que el espejo tampoco filtra por médico.
            string ruta = $"{Nube.SupabaseUrl}/rest/v1/user_template_preferences"
                        + "?select=template_id,specialty_code,updated_at"
                        + "&order=updated_at.desc";
            if (!string.IsNullOrWhiteSpace(especialidad))
                ruta += $"&specialty_code=eq.{Uri.EscapeDataString(Aplanar(especialidad))}";

            using var req = new HttpRequestMessage(HttpMethod.Get, ruta);
            req.Headers.Add("apikey", Nube.ClavePublicable);
            req.Headers.Add("Authorization", $"Bearer {token}");

            using var res = await http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
            {
                LogBus.Log("plantilla", $"no se pudo leer tu sugerida · HTTP {(int)res.StatusCode}");
                return "";
            }

            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return "";
            foreach (var f in doc.RootElement.EnumerateArray())
            {
                string id = f.TryGetProperty("template_id", out var v) && v.ValueKind == JsonValueKind.String
                    ? v.GetString() ?? "" : "";
                if (id.Length > 0) return id;
            }
            return "";
        }
        catch (Exception e)
        {
            LogBus.Log("plantilla", $"no se pudo leer tu sugerida: {e.GetType().Name}: {e.Message}");
            return "";
        }
    }

    /// <summary>Fija (o reemplaza) la sugerida de esa especialidad. Devuelve si quedó guardada.</summary>
    public static async Task<bool> GuardarAsync(SesionMiracle sesion, HttpClient http,
        string especialidad, string plantillaId, CancellationToken ct = default)
    {
        string token = await sesion.TokenVigenteAsync(ct);
        if (token.Length == 0) return false;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"{Nube.SupabaseUrl}/rest/v1/user_template_preferences?on_conflict=user_id,specialty_code")
            { Content = new StringContent(Fila(especialidad, plantillaId), Encoding.UTF8, "application/json") };
            req.Headers.Add("apikey", Nube.ClavePublicable);
            req.Headers.Add("Authorization", $"Bearer {token}");
            req.Headers.Add("Prefer", "resolution=merge-duplicates,return=minimal");

            using var res = await http.SendAsync(req, ct);
            if (res.IsSuccessStatusCode)
            {
                LogBus.Log("plantilla", "tu sugerida quedó fijada; también se ve en el portal");
                return true;
            }

            string cuerpo = await res.Content.ReadAsStringAsync(ct);
            LogBus.Log("plantilla", $"no se pudo fijar tu sugerida · HTTP {(int)res.StatusCode} · "
                                  + (cuerpo.Length <= 200 ? cuerpo : cuerpo[..200]));
            return false;
        }
        catch (Exception e)
        {
            LogBus.Log("plantilla", $"no se pudo fijar tu sugerida: {e.GetType().Name}: {e.Message}");
            return false;
        }
    }
}
