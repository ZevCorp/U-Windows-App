namespace Voz.Realtime;

/// <summary>
/// La compuerta que impide que Ü se oiga a sí misma (spec 002, 2026-08-30).
///
/// El bucle que corta: el micrófono capta lo que suena por los altavoces, el semantic_vad del
/// servidor cree que le hablan encima, manda speech_started y Ü se calla a media frase. Mientras
/// nuestra cola de reproducción suena —más la gracia que tarda el eco en morir—, lo que entra por
/// el micrófono viaja al servidor como SILENCIO DEL MISMO TAMAÑO: el servidor no puede creer que
/// le hablan porque no le llega nada que oír, y el compás del buffer no se pierde (mandar ausencia
/// en vez de silencio descuadraría audio_end_ms del reloj del servidor).
///
/// LA LLAVE ES EL ESTADO DE LA REPRODUCCIÓN, JAMÁS EL VOLUMEN. Las defensas por volumen se
/// enterraron el 2026-08-16 («nunca puedo interrumpirlo», «me toca hablarle muy duro»): un número
/// no distingue «¿esto es el eco de Ü?» de «¿esto es quien me habla, interrumpiéndola?». Si la
/// cola de audio tiene muestras, ese dato es nuestro y no admite duda.
///
/// Es pura a propósito: el reloj y el estado de la cola entran por parámetro, así el contrato de
/// la voz la juzga sin micrófono, sin altavoz y sin socket (promesas 11-13).
/// </summary>
public sealed class CompuertaDeEco
{
    private readonly int _graciaMs;
    private readonly int _bytesPorMs;

    /// <summary>Cuándo se supo por última vez que la cola sonaba. MinValue = Ü no ha sonado nunca,
    /// y la compuerta nace abierta: una que naciera cerrada dejaría muda la sesión hasta que Ü
    /// hablara por primera vez.</summary>
    private long _ultimoSonandoMs = long.MinValue;

    /// <summary>
    /// Cuántos milisegundos de micrófono se sustituyeron por silencio. Patrón nº10: un paso no
    /// ejecutado deja rastro — sin esta cuenta, lo callado sería tan invisible como el
    /// speech_started que hoy no deja ni una línea de log. Se cuenta lo TRAGADO, no lo enviado.
    /// </summary>
    public long MsTragados { get; private set; }

    /// <param name="graciaMs">Cuánto sigue tragando tras vaciarse la cola. Cubre la latencia
    /// declarada del altavoz (120 ms) más el resto de sala; se ajusta con el log, no con teoría.</param>
    /// <param name="ritmoHz">Ritmo del PCM16 mono que fluye, para convertir bytes en tiempo.</param>
    public CompuertaDeEco(int graciaMs, int ritmoHz)
    {
        _graciaMs = graciaMs;
        _bytesPorMs = ritmoHz * 2 / 1000; // PCM16 mono: 2 bytes por muestra
    }

    /// <summary>
    /// Deja pasar el trozo intacto, o lo sustituye por silencio del mismo tamaño si Ü está sonando
    /// o el eco aún no murió. <paramref name="sonando"/> es «la cola de reproducción tiene audio»
    /// (LiveAudio.Hablando); <paramref name="ahoraMs"/> es un reloj monótono cualquiera.
    /// </summary>
    public byte[] Filtrar(byte[] trozo, bool sonando, long ahoraMs)
    {
        if (sonando) _ultimoSonandoMs = ahoraMs;

        bool cerrada = sonando
            || (_ultimoSonandoMs != long.MinValue && ahoraMs - _ultimoSonandoMs < _graciaMs);
        if (!cerrada) return trozo;

        MsTragados += _bytesPorMs > 0 ? trozo.Length / _bytesPorMs : 0;
        return new byte[trozo.Length];
    }
}
