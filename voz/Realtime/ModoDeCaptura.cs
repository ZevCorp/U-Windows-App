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
    public static bool CompuertaActiva(bool aecDelSistema, bool forzada) => forzada || !aecDelSistema;
}
