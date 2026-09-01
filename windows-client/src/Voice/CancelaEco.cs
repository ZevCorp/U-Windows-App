using SpeexDSPSharp.Core;
using Voz.Realtime;

namespace U.WindowsClient.Voice;

/// <summary>
/// EL AEC POR SOFTWARE (spec 002, fase 3 reescrita · 2026-08-31): el eco se resta EN EL CLIENTE,
/// con la ventaja que ningún tercero tiene — la referencia es NUESTRA PROPIA cola de reproducción,
/// tapeada en el consumo del dispositivo (no en el encolado: el buffer adelanta frases enteras).
///
/// Por qué existe: el AEC del sistema es DECORATIVO en el hardware medido (A/B: 0,0 dB con
/// AEC:on declarado por el driver), y sin resta de eco la compuerta tiene que tragar el micrófono
/// entero mientras Ü suena — matando el barge-in por voz. Con el eco restado, la compuerta se
/// aparta (promesa 14 vía AecActivo) y el VAD del servidor vuelve a oír al usuario encima.
///
/// El cancelador es speexdsp (elegido sobre WebRTC AEC3 a propósito: Speex ATENÚA en vez de
/// suprimir, y en doble-habla —el usuario interrumpiendo encima— no se traga al hablante cercano,
/// que es el único fallo que no podemos permitirnos). Cadena clásica: EchoPlayback con cada marco
/// de referencia + EchoCapture del micrófono + preprocesador con el estado de eco para el residuo.
///
/// Si el nativo no carga, <see cref="Activo"/> queda en falso y NADA cambia: la compuerta sigue
/// mandando, como siempre. Degradar a lo que ya funcionaba no es un error, es la red.
/// </summary>
public sealed class CancelaEco : IDisposable
{
    /// <summary>20 ms a 16 kHz. El mismo compás para captura y referencia.</summary>
    public const int MarcoMuestras = 320;

    /// <summary>~240 ms de cola de sala+altavoz (múltiplo del marco, como pide speex).</summary>
    private const int FiltroMuestras = MarcoMuestras * 20;   // ~400 ms: holgura para la latencia variable del WaveOut

    private readonly SpeexDSPEchoCanceler? _eco;
    private readonly SpeexDSPPreprocessor? _pre;
    private readonly ReferenciaDelEco _referencia = new(MarcoMuestras);
    private readonly object _llave = new();

    public bool Activo { get; }

    public CancelaEco(int ritmoHz, bool conPreprocesador = true)
    {
        try
        {
            _eco = new SpeexDSPEchoCanceler(MarcoMuestras, FiltroMuestras);
            int ritmo = ritmoHz;
            _eco.Ctl(EchoCancellationCtl.SPEEX_ECHO_SET_SAMPLING_RATE, ref ritmo);
            _pre = conPreprocesador ? new SpeexDSPPreprocessor(MarcoMuestras, ritmoHz) : null;
            Activo = true;
        }
        catch (Exception e)
        {
            U.WindowsClient.Diagnostics.LogBus.Log("voz-viva",
                $"AEC por software NO disponible ({e.Message}): la compuerta sigue mandando");
            Activo = false;
        }
    }

    /// <summary>Lo que el altavoz CONSUME (PCM16 mono 24 kHz), en el momento en que lo consume.</summary>
    public void Referencia(byte[] pcm24k)
    {
        if (!Activo) return;
        _referencia.Empuja(pcm24k);
    }

    /// <summary>La cola se calló (interrupción): lo pendiente ya no va a sonar.</summary>
    public void Vacia() => _referencia.Vacia();

    /// <summary>
    /// Un trozo del micrófono (PCM16 mono, mismo ritmo), con el eco restado. El trozo debe ser
    /// múltiplo del marco (el micrófono entrega 100 ms = 5 marcos); el sobrante pasa tal cual.
    /// </summary>
    public byte[] Procesa(byte[] mic)
    {
        if (!Activo || _eco == null) return mic;

        var salida = new byte[mic.Length];
        int marcoBytes = MarcoMuestras * 2;
        int completos = mic.Length / marcoBytes;

        lock (_llave)
        {
            for (int f = 0; f < completos; f++)
            {
                // El compás 1:1 del patrón speex: por cada marco capturado, un marco de
                // referencia — el que sonó, o silencio si Ü calla (restar nada es restar cero).
                var referencia = _referencia.SacaMarco();
                var marcoMic = new byte[marcoBytes];
                Buffer.BlockCopy(mic, f * marcoBytes, marcoMic, 0, marcoBytes);

                var limpio = new byte[marcoBytes];
                try
                {
                    _eco.EchoPlayback(referencia);
                    _eco.EchoCapture(marcoMic, limpio);
                    _pre?.Run(limpio);
                }
                catch
                {
                    limpio = marcoMic;   // un marco que no se pudo restar viaja crudo: nunca mudo
                }
                Buffer.BlockCopy(limpio, 0, salida, f * marcoBytes, marcoBytes);
            }
        }

        int resto = mic.Length - completos * marcoBytes;
        if (resto > 0) Buffer.BlockCopy(mic, completos * marcoBytes, salida, completos * marcoBytes, resto);
        return salida;
    }

    public void Dispose()
    {
        try { _eco?.Dispose(); } catch { }
        try { _pre?.Dispose(); } catch { }
    }
}
