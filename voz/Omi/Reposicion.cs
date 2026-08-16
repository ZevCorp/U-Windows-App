namespace Omi;

/// <summary>
/// Devuelve al flujo el silencio que el collar decidió no transmitir.
///
/// ESTO ES EL CORAZÓN DE LA SPEC 001, y el fallo que evita no se parece a su causa.
///
/// Medido el 2026-08-13: el collar DEJA DE EMITIR cuando no hay voz. Hablando seguido entrega 43,3
/// tramas/s y cubre el 86,7 % del reloj; callado baja a 27,0/s y al 54 %. Si esas tramas se pegan
/// una detrás de otra, lo que oye Gemini Live es habla continua sin una sola pausa — y la pausa es
/// exactamente cómo Live API decide que tu turno terminó. Resultado: Ü escucha para siempre y no
/// contesta nunca, con el audio llegando perfecto. El síntoma es «no me responde»; la causa está
/// aquí.
///
/// Y EL NÚMERO DE PAQUETE NO SIRVE PARA MEDIR EL HUECO. También se midió que el contador es contiguo
/// a través de un parón de 2,01 s: no avanza mientras el collar calla. Contar paquetes es por eso la
/// implementación que parece correcta y no lo es. Sólo el reloj de llegada sabe cuánto silencio pasó.
/// </summary>
public sealed class Reposicion
{
    public const int Hz = 16000;

    private long _msPrimera = -1;
    private long _emitidas;

    /// <summary>
    /// Cuántas muestras hay que entregar por esta trama, contando el silencio que le falta delante.
    /// </summary>
    /// <param name="msLlegada">Instante en que llegó la trama, en milisegundos de un reloj monótono.</param>
    /// <param name="muestrasDeLaTrama">Lo que trae la trama ya decodificada (320 en el CV1).</param>
    public int Muestras(long msLlegada, int muestrasDeLaTrama)
    {
        if (muestrasDeLaTrama < 0) muestrasDeLaTrama = 0;

        // La primera no inventa silencio delante: no hay «antes» contra el que medir, y rellenar
        // desde cero metería el tiempo que tardó el usuario en hablar como si fuera parte del turno.
        if (_msPrimera < 0)
        {
            _msPrimera = msLlegada;
            _emitidas = muestrasDeLaTrama;
            return muestrasDeLaTrama;
        }

        // Lo que el RELOJ dice que debió salir desde la primera trama, menos lo que de verdad salió.
        long deberian = (msLlegada - _msPrimera) * Hz / 1000;
        long silencio = deberian - _emitidas;

        // Nunca negativo: si dos tramas llegan pegadas —el enlace las entrega a ráfagas, se vio un
        // 14,5 % entre 30 y 100 ms— no se recorta audio real para cuadrar el reloj. Se prefiere ir
        // un pelo largo que comerse una sílaba.
        if (silencio < 0) silencio = 0;

        long total = silencio + muestrasDeLaTrama;
        _emitidas += total;
        return (int)total;
    }

    /// <summary>Vuelve al estado de recién nacida. Se llama al abrir una sesión, no entre tramas.</summary>
    public void Reiniciar()
    {
        _msPrimera = -1;
        _emitidas = 0;
    }
}
