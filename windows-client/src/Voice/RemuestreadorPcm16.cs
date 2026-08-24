using NAudio.Wave;

namespace U.WindowsClient.Voice;

/// <summary>
/// SUBE EL RITMO DE UN PCM16 MONO, trozo a trozo, sin perder el estado del filtro entre llamadas.
/// </summary>
/// <remarks>
/// Hace falta por el collar: su hardware entrega 16 kHz fijos —lo decide el firmware de Omi, no
/// nosotros, ver <see cref="FuenteOmi"/>— y OpenAI exige un mínimo de 24 kHz (comprobado contra el
/// servidor real el 2026-08-24: pedir 16000 devuelve <c>integer_below_min_value</c>). Sin esto, el
/// collar dejaría de servir en cuanto se cambiara de proveedor, y el fallo no se vería en el
/// arranque: se vería como «la transcripción sale mal cuando hablo por el collar», semanas después.
///
/// Usa el remuestreador de Windows (Media Foundation) y no uno propio: reinventar el filtro
/// anti-aliasing es apostar a acertar a la primera con algo que sonará bien o mal según decenas de
/// coeficientes. UNA instancia por conversación, y se conserva entre trozos — crearla de cero en
/// cada paquete de 20 ms perdería el estado del filtro y se oiría como un clic en cada corte.
/// </remarks>
public sealed class RemuestreadorPcm16 : IDisposable
{
    private readonly BufferedWaveProvider _entrada;
    private readonly MediaFoundationResampler _resampler;

    public RemuestreadorPcm16(int desdeHz, int hastaHz)
    {
        var formatoEntrada = new WaveFormat(desdeHz, 16, 1);
        // ReadFully EN FALSO: sin esto, cuando el remuestreador pide más de lo que hay disponible,
        // la cola rellena con SILENCIO en vez de devolver menos — y ese silencio inventado sale por
        // el otro lado como parte del habla. Con ReadFully en falso, un trozo corto simplemente
        // produce menos muestras de salida, que es lo correcto: falta AUDIO, no falta TIEMPO.
        _entrada = new BufferedWaveProvider(formatoEntrada)
        {
            BufferDuration = TimeSpan.FromSeconds(5),
            ReadFully = false,
        };
        _resampler = new MediaFoundationResampler(_entrada, new WaveFormat(hastaHz, 16, 1))
        {
            ResamplerQuality = 60,
        };
    }

    /// <summary>Entra un trozo al ritmo de ORIGEN, sale lo que ya esté listo al ritmo de DESTINO.</summary>
    public byte[] Remuestrear(byte[] pcm)
    {
        if (pcm.Length == 0) return Array.Empty<byte>();
        _entrada.AddSamples(pcm, 0, pcm.Length);

        // Se pide de más a propósito —el doble del trozo de entrada, más margen— porque el ritmo de
        // salida es mayor que el de entrada y el resampler no puede dar más de lo que TIENE: pedir
        // de menos recortaría muestras que ya estaban listas.
        var salida = new byte[pcm.Length * 4 + 512];
        int leidos = _resampler.Read(salida, 0, salida.Length);
        return leidos == 0 ? Array.Empty<byte>() : salida[..leidos];
    }

    public void Dispose() { try { _resampler.Dispose(); } catch { } }
}
