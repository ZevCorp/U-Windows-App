namespace Omi;

/// <summary>
/// Cuándo entregó audio cada fuente, por separado. Es lo que convierte el verde en una prueba.
///
/// EL FALLO QUE LO TRAJO, contado por el dueño el 2026-09-01 mirando la app: *«se mostraba en verde
/// el icono de Bluetooth mientras el Omi aún se mostraba en rojo, es decir que el Omi realmente no
/// estaba transmitiendo todavía»*.
///
/// Lo que pasaba: el reloj de «última trama» era UNO SOLO para toda la aplicación y lo movía
/// cualquier audio. El micrófono del portátil estaba grabando de respaldo —correctamente— y sus
/// tramas hacían que el collar muerto pareciera vivo. El indicador probaba «llega audio»; la
/// pregunta era «llega audio POR ESTA FUENTE», y no son la misma frase.
///
/// Es la cuarta vez que este repo tropieza con esta clase de mentira —el enlace verde sin tramas del
/// 2026-08-25, el punto gris con audio del 2026-08-14, el botón del collar oyendo por el portátil
/// del 2026-08-13— y la primera en que el fallo estaba DENTRO de la pieza escrita para impedirla.
/// De ahí que cada fuente lleve su reloj y nadie pueda heredar el de otra.
/// </summary>
public sealed class Testigo
{
    private readonly long[] _ultima = new long[3];
    private readonly object _candado = new();

    /// <summary>Llegó audio por esta fuente, a esta hora.</summary>
    public void Anota(Origen origen, long ahoraMs)
    {
        int i = (int)origen;
        if (i < 0 || i >= _ultima.Length) return;
        lock (_candado) _ultima[i] = ahoraMs;
    }

    /// <summary>Cuándo entregó por última vez esta fuente. Cero si nunca entregó.</summary>
    public long UltimaTrama(Origen origen)
    {
        int i = (int)origen;
        if (i < 0 || i >= _ultima.Length) return 0;
        lock (_candado) return _ultima[i];
    }

    /// <summary>
    /// Olvida lo anotado de una fuente. Se usa al cambiar de micrófono: lo que entregó hace un rato
    /// no puede seguir avalando un enlace que se acaba de rehacer.
    /// </summary>
    public void Olvida(Origen origen)
    {
        int i = (int)origen;
        if (i < 0 || i >= _ultima.Length) return;
        lock (_candado) _ultima[i] = 0;
    }
}
