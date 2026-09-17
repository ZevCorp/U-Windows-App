namespace U.WindowsClient.Navigation;

/// <summary>
/// CUÁNDO SE GUARDA UNA MIRADA. Promesa 257 (spec 027). Pura.
/// </summary>
/// <remarks>
/// EL ÁLBUM ESTABA VACÍO DE LO QUE IMPORTABA (2026-09-17). Sólo se guardaba al llamar map_look, y en una
/// investigación de verdad —buscar aves raras en Google, leer resultados— el modelo llama a escribir, a
/// pulsar y a recordar, pero casi nunca a mirar. El dueño pidió ver la foto de aquel momento y no había
/// ninguna.
///
/// LO QUE DEJA ALGO NUEVO QUE RECORDAR ES EL CAMBIO DE SITIO, no el paso del tiempo. Por eso no se
/// captura por reloj: una captura por tick compite con el lector de pantalla, que ya sale SATURADO en el
/// log, y es exactamente el aprendizaje nº14 —no tocar la cadencia antes que el coste por vuelta—.
///
/// Y VOLVER NO ES LLEGAR: pasar por un sitio del que ya hay una foto fresca no merece otra. Sin esto, ir
/// y venir entre dos pestañas llenaría el álbum de la misma pantalla.
/// </remarks>
public static class CuandoSeMira
{
    /// <summary>Cuánto dura fresca la foto de un sitio antes de que valga la pena repetirla.</summary>
    public const long FrescuraMs = 60_000;

    public static bool HayQueGuardar(string antes, string ahora, long ultimaDeEsa, long reloj, long frescuraMs)
    {
        string donde = (ahora ?? "").Trim();
        // Una foto sin ubicación no se puede pedir después, así que no se guarda.
        if (donde.Length == 0) return false;
        if (string.Equals((antes ?? "").Trim(), donde, StringComparison.OrdinalIgnoreCase)) return false;
        if (ultimaDeEsa > 0 && reloj - ultimaDeEsa <= frescuraMs) return false;
        return true;
    }
}
