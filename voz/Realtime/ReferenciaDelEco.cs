namespace Voz.Realtime;

/// <summary>
/// EL COMPÁS DE LA REFERENCIA del AEC por software (spec 002, fase 3 reescrita): lo que Ü encola
/// para sonar baja aquí de 24 kHz a los 16 kHz del micrófono y sale en marcos exactos para el
/// cancelador. La ventaja que ningún tercero tiene: la referencia es NUESTRA propia cola — no hay
/// loopback, no hay relojes ajenos, solo la señal que nosotros mismos mandamos al altavoz.
///
/// Tres reglas, cada una con su promesa:
///  · 3 entran, 2 salen (24000:16000 = 3:2) y nada se pierde ni se inventa: una referencia corrida
///    en el tiempo hace que el cancelador compare contra otro instante y no reste nada (18).
///  · Sin referencia pendiente sale SILENCIO del tamaño del marco: el cancelador nunca espera, y
///    cuando Ü no suena, restar nada es restar cero (19).
///  · Vaciar tira lo pendiente: si la cola se calló (interrupción), lo que ya no va a sonar no
///    puede restarse del micrófono (20).
///
/// La bajada es un promedio del par que acompaña a cada muestra conservada — un filtro corto y
/// honesto: para una señal de VOZ que solo sirve de referencia, la fase importa más que el brillo,
/// y el cancelador adapta lo que le des mientras el compás no mienta.
///
/// Pura a propósito: bytes entran, bytes salen; el contrato la juzga sin audio de verdad.
/// </summary>
public sealed class ReferenciaDelEco
{
    private readonly int _marcoMuestras;
    private readonly Queue<short> _muestras16k = new();

    /// <summary>Profundidad objetivo del amortiguador (marcos). El consumo del dispositivo llega a
    /// ráfagas y el micrófono saca a compás fijo: sin colchón, cada hueco rellena silencio y la
    /// referencia DERIVA respecto al eco real — la desalineación que mata la resta. El colchón
    /// absorbe la ráfaga; el tope resincroniza descartando lo que ya sonó sin restarse.</summary>
    /// La física del desfase: la referencia entra al leerse del buffer (~120 ms ANTES de sonar)
    /// y sale tras profundidad/16k segundos; el eco llega al micrófono a +~125 ms. Con profundidad
    /// BAJA la referencia llega temprano — causal, el filtro de 240 ms lo absorbe—; con
    /// profundidad ALTA llega DESPUÉS del eco — acausal, y restar el pasado es imposible. Por eso
    /// el tope es corto y el objetivo más: mejor temprano que tarde, siempre.
    private const int ObjetivoMarcos = 3;
    private const int TopeMarcos = 12;

    /// <summary>Muestras descartadas para resincronizar (rastro, patrón nº10).</summary>
    public long DescartadasPorDeriva { get; private set; }

    /// <summary>Marcos que salieron como silencio por no haber referencia (rastro).</summary>
    public long MarcosRellenados { get; private set; }

    /// <summary>Cuántas muestras esperan ahora (diagnóstico del arnés).</summary>
    public int MuestrasEnCola { get { lock (_llave) return _muestras16k.Count; } }
    private readonly object _llave = new();

    // El resto de la terna 3:2 que quedó a medias entre un empujón y el siguiente.
    private readonly short[] _resto = new short[2];
    private int _enResto;

    public ReferenciaDelEco(int marcoMuestras) => _marcoMuestras = marcoMuestras;

    /// <summary>PCM16 mono a 24 kHz, tal como se encola al altavoz. Cada terna de muestras
    /// produce dos a 16 kHz: la primera tal cual, la segunda el promedio de las dos restantes.</summary>
    public void Empuja(byte[] pcm24k)
    {
        lock (_llave)
        {
            for (int i = 0; i + 1 < pcm24k.Length; i += 2)
            {
                short m = (short)(pcm24k[i] | (pcm24k[i + 1] << 8));
                _resto[_enResto == 0 ? 0 : 1] = m;
                if (_enResto == 0) { _muestras16k.Enqueue(m); _enResto = 1; }
                else if (_enResto == 1) { _enResto = 2; }
                else
                {
                    _muestras16k.Enqueue((short)((_resto[1] + m) / 2));
                    _enResto = 0;
                }
            }

            // EL TOPE RESINCRONIZA: cola más honda que el tope = la referencia va por detrás del
            // eco real (se rellenó silencio mientras esto esperaba). Se descarta hasta el objetivo:
            // esos marcos ya sonaron sin restarse, y conservarlos restaría el pasado del presente.
            if (_muestras16k.Count > TopeMarcos * _marcoMuestras)
            {
                int objetivo = ObjetivoMarcos * _marcoMuestras;
                while (_muestras16k.Count > objetivo) { _muestras16k.Dequeue(); DescartadasPorDeriva++; }
            }
        }
    }

    /// <summary>Un marco de referencia a 16 kHz. Si no hay suficiente, SILENCIO del tamaño exacto.</summary>
    public byte[] SacaMarco()
    {
        var marco = new byte[_marcoMuestras * 2];
        lock (_llave)
        {
            if (_muestras16k.Count < _marcoMuestras) { MarcosRellenados++; return marco; }   // silencio: nunca se espera
            for (int i = 0; i < _marcoMuestras; i++)
            {
                short m = _muestras16k.Dequeue();
                marco[i * 2] = (byte)(m & 0xFF);
                marco[i * 2 + 1] = (byte)((m >> 8) & 0xFF);
            }
        }
        return marco;
    }

    /// <summary>¿Hay al menos un marco entero esperando?</summary>
    public bool HayMarco { get { lock (_llave) return _muestras16k.Count >= _marcoMuestras; } }

    /// <summary>La cola se calló: lo pendiente ya no va a sonar y no puede restarse.</summary>
    public void Vacia()
    {
        lock (_llave) { _muestras16k.Clear(); _enResto = 0; }
    }
}
