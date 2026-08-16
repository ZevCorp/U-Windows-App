namespace Omi;

/// <summary>
/// El formato que entrega cualquier fuente de voz, venga del collar o del micrófono del portátil.
///
/// Existe para que <c>GeminiLive</c> NO tenga que enterarse de cuál está puesta. El punto de
/// integración es <c>LiveAudio.Capturado</c>, que ya emite PCM16 a 16 kHz mono; si el collar
/// entregara otra cosa, cada consumidor tendría que preguntar de dónde viene el audio — y ese
/// acoplamiento es justo lo que esta clase impide.
///
/// Los ritmos los fija Google y no son negociables: se ENVÍA a 16 kHz y se RECIBE a 24 kHz.
/// Mezclarlos suena a acelerado o a ralentizado, que es el primer síntoma cuando algo va mal.
///
/// Son propiedades y no constantes a propósito: una constante se hornea en el ensamblado que la usa,
/// así que cambiarla aquí dejaría a windows-client con el valor viejo hasta recompilarlo entero.
/// </summary>
public static class Fuente
{
    public static int Hz => 16000;

    public static int Bits => 16;

    public static int Canales => 1;

    /// <summary>Cuántos bytes ocupa un segundo de este formato. 32.000 B/s.</summary>
    public static int BytesPorSegundo => Hz * Canales * (Bits / 8);
}
