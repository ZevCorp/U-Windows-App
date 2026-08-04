using NAudio.Wave;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Voice;

/// <summary>
/// El micrófono y el altavoz de la conversación en vivo, en crudo.
///
/// Live API no habla de «frases»: es un caño de audio abierto en los dos sentidos. Eso lo separa de
/// <see cref="VoiceIO"/>, que dicta UNA frase con los motores de Windows y luego se calla: allí el
/// turno lo decide un temporizador de 8 s, aquí lo decide el modelo mientras te oye. Por eso hace
/// falta PCM crudo y no un reconocedor.
///
/// Los dos ritmos son distintos a propósito y los fija Google: se ENVÍA a 16 kHz y se RECIBE a
/// 24 kHz, ambos mono y de 16 bits. Mezclarlos suena a acelerado o a ralentizado, que es el primer
/// síntoma cuando algo va mal aquí.
/// </summary>
public sealed class LiveAudio : IDisposable
{
    public const int RitmoEntrada = 16000;
    public const int RitmoSalida = 24000;

    private WaveInEvent? _mic;
    private BufferedWaveProvider? _cola;
    private WaveOutEvent? _altavoz;
    private readonly object _candado = new();

    /// <summary>Un trozo de micrófono, ya en el formato que espera el modelo.</summary>
    public event Action<byte[]>? Capturado;

    /// <summary>¿El altavoz tiene algo pendiente por decir? La carita lo usa para animarse.</summary>
    public bool Hablando
    {
        get { lock (_candado) return _cola != null && _cola.BufferedBytes > 0; }
    }

    public void AbrirMicrofono()
    {
        lock (_candado)
        {
            if (_mic != null) return;
            _mic = new WaveInEvent
            {
                WaveFormat = new WaveFormat(RitmoEntrada, 16, 1),
                // Trozos cortos: el modelo interrumpe y responde mientras hablas, así que un buffer
                // largo no ahorra nada y sí añade retardo a todo lo que venga después.
                BufferMilliseconds = 100,
            };
            _mic.DataAvailable += (_, e) =>
            {
                if (e.BytesRecorded <= 0) return;
                var trozo = new byte[e.BytesRecorded];
                Buffer.BlockCopy(e.Buffer, 0, trozo, 0, e.BytesRecorded);
                Capturado?.Invoke(trozo);
            };
            _mic.StartRecording();
            LogBus.Log("voz-viva", $"micrófono abierto a {RitmoEntrada} Hz");
        }
    }

    public void CerrarMicrofono()
    {
        lock (_candado)
        {
            if (_mic == null) return;
            try { _mic.StopRecording(); _mic.Dispose(); } catch { }
            _mic = null;
            LogBus.Log("voz-viva", "micrófono cerrado");
        }
    }

    /// <summary>Encola audio del modelo. Se reproduce en cuanto llega, sin esperar a la frase entera.</summary>
    public void Reproducir(byte[] pcm)
    {
        if (pcm.Length == 0) return;
        lock (_candado)
        {
            if (_altavoz == null)
            {
                _cola = new BufferedWaveProvider(new WaveFormat(RitmoSalida, 16, 1))
                {
                    // Que descarte lo viejo en vez de reventar: si el audio se acumula porque la
                    // máquina va justa, preferimos perder un fragmento a que se caiga la sesión.
                    BufferDuration = TimeSpan.FromSeconds(30),
                    DiscardOnBufferOverflow = true,
                };
                _altavoz = new WaveOutEvent { DesiredLatency = 120 };
                _altavoz.Init(_cola);
                _altavoz.Play();
            }
            _cola!.AddSamples(pcm, 0, pcm.Length);
        }
    }

    /// <summary>
    /// Callar AHORA lo que el modelo estaba diciendo.
    ///
    /// Es lo que ocurre cuando el usuario habla encima: Live API avisa con «interrupted» y lo que ya
    /// se había enviado sigue en nuestra cola. Sin vaciarla, Ü seguiría diciendo la frase que el
    /// propio modelo ya dio por cancelada, que es exactamente la sensación de no ser escuchado.
    /// </summary>
    public void Callar()
    {
        lock (_candado) { try { _cola?.ClearBuffer(); } catch { } }
    }

    public void Dispose()
    {
        CerrarMicrofono();
        lock (_candado)
        {
            try { _altavoz?.Stop(); _altavoz?.Dispose(); } catch { }
            _altavoz = null; _cola = null;
        }
    }
}
