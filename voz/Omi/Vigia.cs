namespace Omi;

/// <summary>
/// Si hay micrófono de verdad, o sólo un enlace que dice que sí.
///
/// ESTA CLASE EXISTE POR UNA FECHA: 2026-08-25, con el usuario delante de una demo. El panel decía
/// «conectado», la carita pintaba el punto azul, y pasaron 3.358.464 ms —cincuenta y seis minutos—
/// sin una sola trama de audio. WinRT tira las suscripciones GATT cuando se cae el enlace y nadie
/// las rehacía; el estado de la conexión decía la verdad y aun así el indicador mentía.
///
/// No fue la única vez. El 2026-08-14 la carita pintaba gris con el audio entrando por el collar, y
/// el 2026-08-13 el botón del collar abría la conversación oyendo por el micrófono del portátil. Son
/// tres versiones del mismo fallo, y las tres tienen la misma causa: <b>el indicador miraba el
/// estado del transporte en vez de mirar si llegaba audio</b>.
///
/// La regla, entonces, es una sola y no admite matices: <b>el verde lo da la última trama, nunca el
/// socket</b>. Conectarse no es entregar. Un indicador que consulte el estado de la conexión vuelve
/// a pintar exactamente el mismo verde falso, por muy bien escrito que esté.
///
/// Aplica igual a los tres caminos —Bluetooth, teléfono y micrófono local—, porque los tres pueden
/// quedarse en pie sin entregar: el Bluetooth por la suscripción perdida, el teléfono por una red
/// que se cuelga sin cerrar el socket, y el micrófono local por un dispositivo que Windows silencia.
/// </summary>
public sealed class Vigia
{
    private readonly int _toleranciaMs;

    /// <param name="toleranciaMs">
    /// Cuánto se aguanta sin audio antes de dejar de decir que hay micrófono.
    ///
    /// NO ES EL UMBRAL DE RELEVO y no hay que confundirlos. <see cref="Relevo"/> decide cuándo
    /// CAMBIAR de micrófono y por eso su umbral es largo: el collar no transmite silencio, y
    /// relevar mientras alguien escucha fue el fallo del 2026-08-13. Esto sólo decide qué PINTAR, y
    /// por eso puede ser corto: apagar el verde no interrumpe nada, y un verde que sobrevive medio
    /// minuto a la avería es peor que un verde que parpadea.
    /// </param>
    public Vigia(int toleranciaMs) => _toleranciaMs = toleranciaMs;

    /// <param name="enlaceEnPie">Lo que el transporte dice de sí mismo. Se acepta como dato, jamás como prueba.</param>
    /// <param name="ultimaTramaMs">Reloj de la última trama recibida. 0 si todavía no llegó ninguna.</param>
    /// <param name="ahoraMs">Reloj actual.</param>
    /// <returns>Si se puede decir que hay micrófono entregando.</returns>
    public bool HayMicrofono(bool enlaceEnPie, long ultimaTramaMs, long ahoraMs)
    {
        // NUNCA LLEGÓ UNA TRAMA. Es el caso del 2026-08-25 en su forma más pura: el enlace se abrió,
        // se rehízo veinte veces, y el audio no llegó ni una sola vez. Si esto devolviera true
        // «mientras se calienta», el indicador volvería a mentir justo durante la ventana en la que
        // el usuario está mirando si funcionó.
        if (ultimaTramaMs <= 0) return false;

        // LA TRAMA MANDA, Y EL ENLACE NO PUEDE VETARLA. Esta línea empezó siendo
        // `if (!enlaceEnPie) return false;` al principio, y estaba mal: el 2026-09-01 el collar
        // entregó 4.815 tramas con el icono en gris. `Conectado` sale de un evento de TRANSICIÓN de
        // Bluetooth, y al abrir sobre un aparato que YA estaba conectado no hay transición que lo
        // dispare — la bandera se queda en false con el audio entrando.
        //
        // Exigir las dos cosas convertía la única prueba dura que hay (llegó audio) en algo que una
        // bandera floja podía vetar. El enlace se queda para <see cref="Estado"/>, donde sirve para
        // distinguir «esperando» de «sin conectar»; para decidir si HAY micrófono no pinta nada.
        return ahoraMs - ultimaTramaMs < _toleranciaMs;
    }

    /// <summary>
    /// Qué decirle a la persona, para que el indicador no tenga que inventarse el texto.
    ///
    /// Distingue sus causas a propósito (aprendizaje nº2): «esperando» y «se quedó mudo» son dos
    /// situaciones distintas y mandan la investigación a sitios distintos — la primera a la
    /// configuración, la segunda al enlace.
    /// </summary>
    public string Estado(bool enlaceEnPie, long ultimaTramaMs, long ahoraMs)
    {
        if (!enlaceEnPie) return "sin conectar";
        if (ultimaTramaMs <= 0) return "conectado, esperando la primera voz";
        if (HayMicrofono(enlaceEnPie, ultimaTramaMs, ahoraMs)) return "entregando";
        return $"conectado pero mudo desde hace {(ahoraMs - ultimaTramaMs) / 1000.0:0.0} s";
    }
}
