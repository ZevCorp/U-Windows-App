using System.Speech.Recognition;
using System.Speech.Synthesis;

namespace U.WindowsClient.Voice;

/// <summary>
/// TTS y STT con los motores integrados de Windows (System.Speech). Es I/O puro del cliente: hablar y
/// escuchar. Ninguna transcripción se interpreta aquí — el texto reconocido se manda tal cual al
/// cerebro. (En Android esto lo hacían OpenAiTts/Transcribers; llevarlo al servidor es una fase
/// posterior si se quiere mayor calidad de voz.)
/// </summary>
public sealed class VoiceIO : IDisposable
{
    private readonly SpeechSynthesizer _tts = new();
    private bool _muted;

    public VoiceIO()
    {
        try { _tts.SelectVoiceByHints(VoiceGender.Female, VoiceAge.Adult); } catch { }
        // El sintetizador SÍ avisa de cuándo empieza y termina; nadie lo estaba escuchando, así que
        // «Ü está hablando» no existía como dato en ninguna parte y la interfaz no podía reflejarlo.
        _tts.SpeakStarted += (_, __) => Raise(escuchando: false, hablando: true);
        _tts.SpeakCompleted += (_, __) => Raise(escuchando: false, hablando: false);
    }

    /// <summary>
    /// Cambió lo que la voz está haciendo. Existe para que la carita pueda reflejarlo: antes la UI lo
    /// simulaba escribiendo «Escuchando…» antes del await y confiando en que el await tardase lo mismo
    /// que el micrófono, que no es un estado sino una suposición.
    /// </summary>
    public event EventHandler<VoiceActivity>? ActivityChanged;

    /// <summary>Lo que la voz está haciendo AHORA. Las dos pueden ser false (en reposo).</summary>
    public readonly record struct VoiceActivity(bool Escuchando, bool Hablando);

    /// <summary>Instantánea del estado, para quien se enganche tarde.</summary>
    public VoiceActivity Activity { get; private set; }

    private void Raise(bool escuchando, bool hablando)
    {
        var next = new VoiceActivity(escuchando, hablando);
        if (next == Activity) return;
        Activity = next;
        // Llega en el hilo del motor de voz, no en el de la UI: quien pinte tiene que marshalear.
        ActivityChanged?.Invoke(this, next);
    }

    /// <summary>
    /// Mudo: <see cref="Speak"/> no emite nada. Ponerlo en true CORTA EN SECO lo que esté diciendo —
    /// mutear a un asistente que ya arrancó una frase larga y esperar a que la termine no es mutear.
    ///
    /// Solo silencia el audio: el texto se sigue viendo en la carita, porque el usuario quiere dejar de
    /// oírlo, no dejar de enterarse.
    /// </summary>
    public bool Muted
    {
        get => _muted;
        set
        {
            _muted = value;
            if (value) Silence();
        }
    }

    /// <summary>Calla lo que esté diciendo ahora mismo, sin cambiar <see cref="Muted"/>.</summary>
    public void Silence()
    {
        try { _tts.SpeakAsyncCancelAll(); } catch { }
        // SpeakCompleted llega igual al cancelar, pero no siempre y no siempre a tiempo: dejar el
        // estado en «hablando» cuando ya se calló sería una carita mintiendo.
        Raise(Activity.Escuchando, hablando: false);
    }

    public void Speak(string text)
    {
        if (_muted || string.IsNullOrWhiteSpace(text)) return;
        try { _tts.SpeakAsyncCancelAll(); _tts.SpeakAsync(text); } catch { }
    }

    /// <summary>
    /// Escucha una frase por el micrófono y devuelve el texto (o "" si no reconoció).
    ///
    /// El estado «escuchando» se levanta y se baja AQUÍ, alrededor de la llamada bloqueante, que es
    /// el único sitio que sabe de verdad cuándo el micrófono está abierto. El motor se crea y se
    /// destruye por llamada, así que no hay nada más a lo que engancharse.
    /// </summary>
    public async Task<string> ListenOnceAsync(CancellationToken ct)
    {
        Raise(escuchando: true, Activity.Hablando);
        try
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var rec = new SpeechRecognitionEngine();
                    rec.LoadGrammar(new DictationGrammar());
                    rec.SetInputToDefaultAudioDevice();
                    var result = rec.Recognize(TimeSpan.FromSeconds(8));
                    return result?.Text ?? "";
                }
                catch { return ""; }
            }, ct);
        }
        finally { Raise(escuchando: false, Activity.Hablando); }
    }

    public void Dispose() => _tts.Dispose();
}
