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

    /// <summary>
    /// El portal de Miracle Notes, para «Abrir en la web» (spec 055): el mismo dominio que la web
    /// declara en `lib/site.ts`. Se pisa con <c>MIRACLE_PORTAL_URL</c> para apuntar a una preview.
    /// </summary>
    public static string PortalUrl =>
        (Ambiente("MIRACLE_PORTAL_URL") ?? "https://itsmiracleai.com.co").TrimEnd('/');

    /// <summary>
    /// La referencia del proyecto, sacada de la propia URL en vez de escrita otra vez.
    ///
    /// Hace falta suelta porque Realtime no vive en <c>https://…</c> sino en
    /// <c>wss://&lt;ref&gt;.supabase.co/realtime/v1/websocket</c>. Escribirla aparte sería tener el
    /// mismo dato en dos sitios, y el día que se apunte a otro entorno con la variable de entorno
    /// uno de los dos se quedaría viejo — que es el aprendizaje nº16 en su forma más barata.
    /// </summary>
    public static string ProyectoSupabase
    {
        get
        {
            var host = new Uri(SupabaseUrl).Host;          // zyvfamlhlmztliexvmej.supabase.co
            int punto = host.IndexOf('.');
            return punto > 0 ? host[..punto] : host;
        }
    }

    /// <summary>Vacío no es ausente: una variable puesta a "" cae al valor por defecto.</summary>
    private static string? Ambiente(string nombre)
    {
        string? v = Environment.GetEnvironmentVariable(nombre);
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }
}
