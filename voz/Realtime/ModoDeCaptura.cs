namespace Voz.Realtime;

/// <summary>
/// Quién corta el eco en esta máquina (spec 002, promesa 14): el AEC del sistema o la compuerta,
/// nunca los dos y nunca ninguno.
///
/// Los dos a la vez harían el AEC inútil —la compuerta calla todo igual y el barge-in que el AEC
/// permite muere—; los dos apagados son exactamente el bug del 2026-08-30: Ü oyéndose por los
/// altavoces y callándose a media frase. «Forzada» es la perilla de esta máquina para cuando un
/// AEC de endpoint promete y no cumple: forzar solo puede ENCENDER la garantía, jamás apagarla.
/// </summary>
public static class ModoDeCaptura
{
    /// <param name="aecDelSistema">El eco se está RESTANDO de verdad (AEC medido y activo).</param>
    /// <param name="forzada">La perilla que solo ENCIENDE la garantía, jamás la apaga.</param>
    /// <param name="sinCaminoDeEco">Declarado por quien puede saberlo: AURICULARES puestos — el
    /// altavoz no llega al micrófono y no hay eco que tragar. Con esto la compuerta se aparta y
    /// el barge-in por voz natural vuelve entero, vía el VAD del servidor (promesa 21).</param>
    public static bool CompuertaActiva(bool aecDelSistema, bool forzada, bool sinCaminoDeEco = false)
        => forzada || (!aecDelSistema && !sinCaminoDeEco);

    /// <summary>
    /// EL DEFAULT ES LA EXPERIENCIA OPENAI DE FÁBRICA (decisión del dueño, 2026-08-31): sin
    /// declarar nada, el micrófono viaja siempre y el semantic_vad del servidor decide los
    /// turnos — interrumpir con la voz funciona como en la documentación, y con altavoces se
    /// asume el eco o se usan audífonos. Solo U_SIN_ECO=0 devuelve la compuerta (promesa 22).
    /// </summary>
    public static bool SinEcoDeclarado(string? valorDeEntorno)
        => (valorDeEntorno ?? "").Trim().ToLowerInvariant() is not ("0" or "false" or "no");
}
