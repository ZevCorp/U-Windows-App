namespace U.WindowsClient.Cuenta;

/// <summary>
/// Dónde vive la cuenta: el proyecto de Supabase del portal (`miracle-app`). Las mismas señas que
/// usa la web, para que «misma base de datos» sea literal.
/// </summary>
/// <remarks>
/// LA CLAVE PUBLICABLE NO ES UN SECRETO: viaja en el JavaScript que el portal manda a cualquier
/// navegador. Lo único que abre es lo que la RLS permita a quien presente un token válido.
/// Las variables de entorno pisan los valores por la misma razón que U_BACKEND_URL pisa el backend:
/// poder apuntar a otro entorno sin recompilar.
/// </remarks>
public static class Nube
{
    public static string SupabaseUrl =>
        Ambiente("MIRACLE_SUPABASE_URL") ?? "https://zyvfamlhlmztliexvmej.supabase.co";

    public static string ClavePublicable =>
        Ambiente("MIRACLE_SUPABASE_KEY") ?? "sb_publishable_qroW231Ts7UYAEgr_f5cnQ_3SrW2ZrI";

    /// <summary>Vacío no es ausente: una variable puesta a "" cae al valor por defecto.</summary>
    private static string? Ambiente(string nombre)
    {
        string? v = Environment.GetEnvironmentVariable(nombre);
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }
}
