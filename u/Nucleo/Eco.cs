namespace U.Ciclo;

/// <summary>
/// ¿ESTÁ SONANDO Ü DE VERDAD? (promesa 452). La compuerta del eco calla el micrófono mientras Ü habla, para que
/// la voz no se oiga a sí misma. La primera versión la cerraba con cualquier cosa encolada en el altavoz, y el
/// servidor manda un delta cada ~100 ms aunque la voz calle: en la prueba del 2026-09-24 (23:17) llegaban
/// muestras de −17 a −12, no ceros. Encoladas, el micrófono habría quedado mudo para siempre.
///
/// Umbral 512 (≈ −36 dBFS): el siseo medido pica en 17; una frase normal, en miles.
/// </summary>
public static class Eco
{
    public const int PicoDeVoz = 512;

    public static bool Suena(byte[] pcm)
    {
        if (pcm == null) return false;
        for (int i = 0; i + 1 < pcm.Length; i += 2)
            if (Math.Abs((int)BitConverter.ToInt16(pcm, i)) > PicoDeVoz) return true;
        return false;
    }
}
