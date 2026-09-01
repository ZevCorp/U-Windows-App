namespace U.WindowsClient.Cuenta;

/// <summary>
/// QUIÉN MANDA CUANDO HAY DOS IDENTIDADES. Un solo sitio que decide, para que la carita y la
/// consulta no contesten cosas distintas a la misma pregunta.
/// </summary>
/// <remarks>
/// NACE DE UN POPUP QUE NO DEBÍA ESTAR AHÍ (2026-09-01, lo vio el usuario). Con la Dra. Rincón ya
/// dentro de la ventana de consulta —sesión de Supabase abierta, su nombre en la cabecera—, la
/// carita le plantó encima el popup viejo de «Te damos la bienvenida» pidiéndole otra vez nombre y
/// correo.
///
/// LA CAUSA: hay DOS identidades conviviendo, y son de distinta forma.
///
///   · La de MÉDICO — <see cref="SesionMiracle"/>. Un JWT de Supabase con su uuid. Es la que abre
///     las rutas clínicas y la única que sabe de verdad quién está delante.
///   · La de MÁQUINA — <c>Config.Email</c>. Un correo tecleado una vez en un popup, sin
///     contraseña. La usan el scoping de workflows y la telemetría desde antes de que existiera la
///     otra.
///
/// Al reemplazar el login se cambió el camino de <c>--consulta</c> y se dejó intacto el de la
/// carita, así que cada uno seguía preguntando por su cuenta. Es el aprendizaje nº16 por sexta vez
/// en este repo: dos identidades de distinta forma, y quien las junta hereda el desacuerdo.
///
/// LA REGLA, y por eso vive aquí y no repartida: **si hay médico, manda el médico.** La de máquina
/// no se borra —los workflows y la telemetría que ya la usaban se quedarían sin identidad de
/// golpe—, pero deja de ser la que se pregunta.
///
/// SE JUZGA ESTO Y NO LA VENTANA (promesa 98). Que el popup aparezca o no es nivel 4; la regla de
/// precedencia es lo que se rompió, y una regla sí se puede escribir.
/// </remarks>
public static class Identidad
{
    /// <summary>
    /// ¿Hay que pedirle a esta persona quién es? Solo cuando no lo sabe nadie.
    /// </summary>
    /// <param name="hayMedicoDentro">Si <see cref="SesionMiracle.HayMedico"/> es cierto.</param>
    /// <param name="correoDeMaquina">El <c>Config.Email</c> de siempre; vacío si nunca se puso.</param>
    public static bool HayQuePreguntar(bool hayMedicoDentro, string correoDeMaquina)
    {
        // El médico manda. Con una sesión abierta, preguntar es no haberla mirado.
        if (hayMedicoDentro) return false;
        return string.IsNullOrWhiteSpace(correoDeMaquina);
    }

    /// <summary>
    /// El correo con el que esta máquina se identifica ante el backend. El de la sesión si lo hay;
    /// si no, el de siempre.
    /// </summary>
    /// <remarks>
    /// Vacío NO es ausente aquí tampoco: una sesión sin correo —que puede pasar si el token viene
    /// sin la reclamación— cae al de máquina en vez de dejar la identidad en blanco.
    /// </remarks>
    public static string CorreoQueMandaEnLaMaquina(string correoDelMedico, string correoDeMaquina) =>
        string.IsNullOrWhiteSpace(correoDelMedico) ? correoDeMaquina ?? "" : correoDelMedico;
}
