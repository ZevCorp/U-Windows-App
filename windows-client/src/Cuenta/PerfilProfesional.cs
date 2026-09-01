using System.Text.Json;

namespace U.WindowsClient.Cuenta;

/// <summary>
/// Los campos del perfil profesional distintos del nombre, tal como están HOY en la base. Existe
/// para poder mandarlos de vuelta sin tocarlos al guardar solo el nombre.
/// </summary>
public sealed record PerfilProfesional(
    string IdentificationNumber,
    string ProfessionalRegistration,
    string SpecialtyCode,
    string SpecialtyName,
    string PracticeCountry,
    string PracticeCity)
{
    public static readonly PerfilProfesional Vacio = new("", "", "", "", "", "");
}

/// <summary>
/// EL CUERPO DE <c>update_own_profile</c>, la RPC con la que el portal edita su propio perfil
/// (supabase/migrations/20260829120100_update_own_profile.sql).
/// </summary>
/// <remarks>
/// UNA RPC Y NO UN UPDATE DIRECTO, porque las dos políticas de UPDATE de `profiles` no sirven para
/// autoeditarse: una exige <c>role = 'medico'</c> —un supervisor no entra, y un médico B2C nace
/// como <c>admin</c>— y la otra deja a un admin escribir TODA su organización, no solo su fila. La
/// RPC es la lista blanca: nunca toca <c>role</c>, <c>organization_id</c>, <c>email</c>,
/// <c>is_demo</c> ni <c>disabled_at</c>.
///
/// LA RPC PIDE LOS SIETE CAMPOS A LA VEZ Y LOS REESCRIBE TODOS — no hace <c>COALESCE</c> con lo que
/// ya había. Mandar solo el nombre y el resto vacío BORRARÍA el documento, el registro profesional
/// y la especialidad de cualquier médico que ya los tuviera llenos: un dato que el portal también
/// lee, dañado desde un cliente que solo quería cambiar una cosa. Por eso <see cref="PerfilProfesional"/>
/// se lee de la base ANTES de guardar y viaja de vuelta intacto.
/// </remarks>
public static class PerfilRpc
{
    public static string CuerpoDeGuardarNombre(string nombreNuevo, PerfilProfesional actual) =>
        JsonSerializer.Serialize(new
        {
            p_full_name = (nombreNuevo ?? "").Trim(),
            p_identification_number = actual.IdentificationNumber,
            p_professional_registration = actual.ProfessionalRegistration,
            p_specialty_code = actual.SpecialtyCode,
            p_specialty_name = actual.SpecialtyName,
            p_practice_country = actual.PracticeCountry,
            p_practice_city = actual.PracticeCity,
        });
}
