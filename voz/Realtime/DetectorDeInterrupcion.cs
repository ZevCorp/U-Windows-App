namespace Voz.Realtime;

/// <summary>
/// El barge-in de la compuerta (spec 002, fase 4 · 2026-08-31): mientras la compuerta de eco
/// manda —y por diseño el servidor oye silencio—, este detector es el único oído local que puede
/// devolverle la interrupción al usuario («no lo puedo interrumpir», 2026-08-31).
///
/// LA LLAVE SIGUE SIENDO EL ESTADO, NO EL VOLUMEN. Lo enterrado el 2026-08-16 fue usar volumen
/// para decidir QUÉ ES ECO y filtrar con eso; aquí el volumen decide una sola cosa, acotada y
/// distinta: que hay VOZ SOSTENIDA muy por encima del eco que este mismo detector aprendió de
/// esta máquina. Y su consecuencia no es filtrar nada: es cortar la cola de reproducción y
/// reabrir la compuerta — a partir de ahí vuelve a mandar el estado, como siempre.
///
/// Tres defensas contra el disparo falso, cada una con su promesa:
///  · el SOSTÉN: un portazo es un trozo; la voz dura (16).
///  · la LÍNEA BASE MÓVIL: es del eco de esta máquina y lo sigue si sube o baja (16).
///  · la SIEMBRA: al arrancar la cola, los primeros milisegundos son el eco de la frase nueva
///    —aunque sea más fuerte que la anterior— y siembran la base en vez de competir con ella (16).
/// Y tras disparar se desarma hasta que Ü vuelva a sonar: interrumpir una vez basta (17).
///
/// Pura a propósito: RMS, estado de la cola y reloj entran por parámetro; el contrato la juzga
/// sin micrófono, sin altavoz y sin socket (promesas 15-17).
/// </summary>
public sealed class DetectorDeInterrupcion
{
    /// <summary>Cuánto dura la siembra tras arrancar la cola: en ese tramo todo es eco por decreto.</summary>
    private const int SiembraMs = 250;

    private readonly int _sostenMs;
    private readonly double _factor;
    private readonly double _pisoRms;

    private double _lineaBase = -1;     // -1 = todavía no aprendió eco ninguno
    private long _sobreDesdeMs = -1;    // desde cuándo la energía está por encima; -1 = no lo está
    private long _anteriorMs = -1;
    private long _siembraHastaMs = -1;
    private bool _sonabaAntes;
    private bool _desarmado;            // ya disparó este episodio; se rearma cuando vuelve el eco

    /// <param name="sostenMs">Cuánta voz continua exige antes de disparar. Un golpe no llega.</param>
    /// <param name="factor">Cuántas veces por encima de la línea base del eco hay que estar.</param>
    /// <param name="pisoRms">Piso absoluto (PCM16: 0..32768): por debajo jamás es voz encima.</param>
    public DetectorDeInterrupcion(int sostenMs, double factor, double pisoRms)
    {
        _sostenMs = sostenMs;
        _factor = factor;
        _pisoRms = pisoRms;
    }

    /// <summary>
    /// «Oí este trozo (su RMS) con la cola en este estado». Devuelve true UNA vez por episodio:
    /// el momento de cortar la cola y reabrir la compuerta.
    /// </summary>
    public bool Oye(double rms, bool sonando, long ahoraMs)
    {
        long antes = _anteriorMs;
        _anteriorMs = ahoraMs;

        // Sin cola sonando no hay eco que confundir ni nada que interrumpir: el servidor ya oye.
        if (!sonando) { _sonabaAntes = false; _sobreDesdeMs = -1; return false; }

        // La cola acaba de arrancar: empieza la siembra de ESTA frase.
        if (!_sonabaAntes) _siembraHastaMs = ahoraMs + SiembraMs;
        _sonabaAntes = true;

        bool sembrando = ahoraMs <= _siembraHastaMs;
        bool sobre = !sembrando
            && rms >= _pisoRms
            && _lineaBase >= 0
            && rms >= _lineaBase * _factor;

        if (!sobre)
        {
            // Lo que suena y no es candidato a voz ES el eco de esta máquina: alimenta la línea
            // base (media móvil corta), que así sigue a Ü si sube o baja de volumen.
            _lineaBase = _lineaBase < 0 ? rms : _lineaBase * 0.8 + rms * 0.2;
            _sobreDesdeMs = -1;
            _desarmado = false;   // volvió el eco: episodio cerrado, guardia puesta
            return false;
        }

        if (_desarmado) return false;   // ya disparó este episodio: no se ametralla

        if (_sobreDesdeMs < 0) { _sobreDesdeMs = antes < 0 ? ahoraMs : antes; return false; }
        if (ahoraMs - _sobreDesdeMs < _sostenMs) return false;

        _desarmado = true;
        _sobreDesdeMs = -1;
        return true;
    }
}
