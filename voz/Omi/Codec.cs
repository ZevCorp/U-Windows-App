namespace Omi;

/// <summary>
/// Qué códec declara el collar, y si lo sabemos decodificar.
///
/// SE PREGUNTA, NO SE SUPONE. El collar lo publica en la característica <c>19b10002</c> y no es el
/// mismo en todos los modelos: el CV1 medido el 2026-08-13 declara <see cref="OpusFs320"/> (tramas
/// de 20 ms) y el DevKit declara <see cref="Opus10ms"/>. Cablear uno a mano es apostar a que no
/// aparezca otro modelo, y el día que aparezca no da un error: da ruido.
/// </summary>
public static class Codec
{
    /// <summary>PCM de 16 bits a 16 kHz. No hay nada que decodificar: ya es el formato de salida.</summary>
    public const byte Pcm16 = 0;

    /// <summary>PCM de 8 bits. Se conoce y NO se soporta: haría falta expandir a 16, y no está escrito.</summary>
    public const byte Pcm8 = 1;

    /// <summary>Opus con tramas de 10 ms (160 muestras). DevKit.</summary>
    public const byte Opus10ms = 20;

    /// <summary>Opus FS320: tramas de 20 ms (320 muestras). Es el del CV1.</summary>
    public const byte OpusFs320 = 21;

    public static bool Soportado(byte id) => id is Pcm16 or Opus10ms or OpusFs320;

    /// <summary>
    /// Por qué no se acepta, DICIENDO CUÁL ERA. Cadena vacía si sí se acepta.
    ///
    /// El número va dentro del texto a propósito: un mensaje que dijera «códec no soportado» no
    /// distingue «llegó un 7» de «llegó un 1» de «no contestó nadie», y un mensaje que no distingue
    /// sus causas manda la investigación al sitio equivocado (aprendizaje nº2).
    /// </summary>
    public static string Motivo(byte id)
    {
        if (Soportado(id)) return "";
        if (id == Pcm8)
            return $"el collar declara el códec {id} (PCM de 8 bits) y no está escrito el paso a 16 bits";
        return $"el collar declara el códec {id} (0x{id:X2}), que no está en el protocolo conocido "
             + $"({Pcm16} PCM16, {Opus10ms} Opus 10 ms, {OpusFs320} Opus 20 ms)";
    }

    /// <summary>
    /// Cuántas muestras trae una trama de este códec. Es lo que separa los dos Opus, y equivocarlo no
    /// suena mal: suena acelerado o lento, que se parece demasiado a «va bien».
    /// </summary>
    public static int MuestrasPorTrama(byte id) => id switch
    {
        Opus10ms => 160,
        OpusFs320 => 320,
        _ => 0,
    };
}
