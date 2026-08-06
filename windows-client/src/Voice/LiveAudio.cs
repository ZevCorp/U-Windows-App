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

    /// <summary>
    /// CUÁNTO DE FUERTE estamos sonando ahora mismo, en la misma escala que el micrófono (0–1).
    ///
    /// Hace falta para no confundir nuestra propia voz con la del usuario. Lo que entra por el
    /// micro cuando hablamos es un eco de esto, y lo alto que llegue depende del volumen de los
    /// altavoces: con el volumen bajo no molesta y con el volumen alto tapa la voz de cualquiera.
    /// Saber a qué volumen estamos sonando es lo único que permite distinguir «me están
    /// interrumpiendo» de «me estoy oyendo a mí mismo» (2026-08-05).
    ///
    /// Baja sola: se queda con el pico reciente y lo va soltando, para que el silencio entre dos
    /// palabras de una misma frase no la ponga a cero y abra la puerta al eco de la siguiente.
    ///
    /// SONAR NO ES RECIBIR (2026-08-06). El modelo manda el audio mucho más rápido de lo que se
    /// oye: una frase de cinco segundos entra en menos de uno y se queda en la cola. Medir el
    /// tiempo desde que LLEGA el último trozo dejaba esto en cero a los 400 ms, con el altavoz
    /// todavía hablando —o sea, en cero justo cuando hace falta—. Y de ahí colgaban las tres
    /// defensas contra el eco de GeminiLive: aprender la ganancia, subir el umbral y exigir dos
    /// tramos seguidos. Las tres se apagaban a la vez y Ü se cortaba a media frase oyéndose a sí
    /// misma. Mientras quede cola estamos sonando y no hay nada que soltar; la caída empieza
    /// cuando la cola se vacía.
    /// </summary>
    public double NivelSalida
    {
        get
        {
            lock (_candado)
            {
                if (_cola != null && _cola.BufferedBytes > 0)
                {
                    _cuandoSalida = DateTime.UtcNow;
                    return _nivelSalida;
                }
                double caida = (DateTime.UtcNow - _cuandoSalida).TotalMilliseconds / 400.0;
                if (caida >= 1) { _nivelSalida = 0; return 0; }
                return _nivelSalida * (1 - caida);
            }
        }
    }

    private double _nivelSalida;
    private DateTime _cuandoSalida = DateTime.MinValue;

    /// <summary>El pico de un bloque PCM de 16 bits, normalizado a 0–1. Igual que mide la entrada.</summary>
    private static double Pico(byte[] pcm)
    {
        int max = 0;
        for (int i = 0; i + 1 < pcm.Length; i += 2)
        {
            int m = Math.Abs((short)(pcm[i] | (pcm[i + 1] << 8)));
            if (m > max) max = m;
        }
        return max / 32768.0;
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

            // Se anota lo fuerte que va a sonar esto. Se queda el pico más alto mientras no haya
            // decaído: dentro de una frase hay silencios cortos, y dejar caer el nivel en cada uno
            // abriría la puerta al eco de la sílaba siguiente.
            //
            // Se compara contra el pico CRUDO, no contra el getter: el getter devuelve el valor ya
            // soltado, así que un trozo flojo lo superaba y bajaba el pico sostenido en vez de
            // mantenerlo. El getter lo pone a cero solo cuando la cola se vacía y termina la caída,
            // que es cuando empieza de verdad una frase nueva.
            double p = Pico(pcm);
            if (p >= _nivelSalida) _nivelSalida = p;
            _cuandoSalida = DateTime.UtcNow;
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
