using NAudio.Wave;

namespace U.WindowsClient.Voice;

/// <summary>
/// EL GRIFO DEL CONSUMO (spec 002, fase 3): un envoltorio transparente sobre la cola de
/// reproducción que copia hacia el AEC exactamente lo que el dispositivo LEE, en el momento en
/// que lo lee. La referencia del cancelador tiene que ir alineada con lo que SUENA — tomarla del
/// encolado la adelantaría frases enteras (el buffer guarda hasta 30 s) y el cancelador
/// compararía el micrófono contra un futuro.
/// </summary>
public sealed class GrifoDeConsumo : IWaveProvider
{
    private readonly IWaveProvider _fuente;
    private readonly Action<byte[]> _alConsumir;

    public GrifoDeConsumo(IWaveProvider fuente, Action<byte[]> alConsumir)
    {
        _fuente = fuente;
        _alConsumir = alConsumir;
    }

    public WaveFormat WaveFormat => _fuente.WaveFormat;

    public int Read(byte[] buffer, int offset, int count)
    {
        int n = _fuente.Read(buffer, offset, count);
        if (n > 0)
        {
            var copia = new byte[n];
            Buffer.BlockCopy(buffer, offset, copia, 0, n);
            try { _alConsumir(copia); } catch { /* la referencia jamás tumba la reproducción */ }
        }
        return n;
    }
}
