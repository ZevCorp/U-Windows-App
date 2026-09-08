namespace U.WindowsClient.Ui;

/// <summary>Los momentos del botón del collar que hay que confirmarle a quien no mira la pantalla.</summary>
public enum MomentoDelCollar
{
    /// <summary>Arrancó una consulta nueva y ya está grabando.</summary>
    Empieza,

    /// <summary>Vuelve a grabar sobre la consulta que estaba en pausa.</summary>
    Reanuda,

    /// <summary>Se pausó. La consulta sigue viva.</summary>
    Pausa,

    /// <summary>Se cerró la consulta y se pidió la nota. No tiene vuelta atrás.</summary>
    Termina,

    /// <summary>El toque llegó pero no podía hacer nada.</summary>
    NoSePudo,
}

/// <summary>
/// QUÉ SUENA EN CADA MOMENTO DEL BOTÓN DEL COLLAR. Promesa 188 (spec 014).
/// </summary>
/// <remarks>
/// EL DUEÑO PIDIÓ VIBRACIÓN Y NO SE PUEDE, y conviene que quede escrito aquí para que nadie lo
/// vuelva a intentar de memoria. Medido el 2026-09-07 por tres caminos independientes:
///
///   · una sonda de solo lectura sobre ESTE CV1 enumeró sus <b>12 servicios</b> GATT: audio, botón,
///     batería, información, almacenamiento, hora, altavoz y tres sin identificar. <b>Ninguno
///     háptico</b>;
///   · la lista de UUID de la <b>propia app de Omi</b> (<c>app/lib/services/devices/models.dart</c>)
///     no tiene <c>haptic</c>, <c>vibration</c>, <c>motor</c> ni <c>LED</c>: la app oficial tampoco
///     puede hacerlo vibrar;
///   · y Omi tiene una <b>recompensa abierta de 500 $</b> titulada «Simulate Haptics with Speaker»
///     (issue #573), que es la confesión de que el motor no existe.
///
/// Fingir la vibración habría sido el peor resultado: un feedback que la interfaz promete y el
/// cuerpo no siente.
///
/// ESTA REGLA DICE QUÉ SUENA, NO POR DÓNDE. Hoy suena el PC, que es lo que hay. El collar tiene
/// altavoz (<c>cab1ab95-…</c>) y ese es el sitio correcto para un aparato que se lleva puesto, pero
/// su protocolo no está documentado y escribirle bytes a ciegas a una característica desconocida no
/// se hace. El día que se descifre, cambia el altavoz y no esto.
///
/// EL PRINCIPIO: el sonido dice el ESTADO EN EL QUE QUEDAS, no la acción que hiciste. Quien no mira
/// la pantalla necesita saber una sola cosa —¿me está grabando?— y necesita que esa respuesta suene
/// siempre igual. Por eso <see cref="MomentoDelCollar.Empieza"/> y
/// <see cref="MomentoDelCollar.Reanuda"/> comparten sonido a propósito: los dos dejan el mismo
/// estado, y de cómo llegaste a él no depende nada.
/// </remarks>
public static class ReglaDelSonidoDelCollar
{
    /// <summary>Estás en vivo. Dos notas que SUBEN — abrirse, la metáfora que la carita ya usa.</summary>
    public const string EnVivo = "en-vivo";

    /// <summary>Pausa. Un toque corto y neutro: mismo número de notas, menos energía.</summary>
    public const string Pausado = "pausa";

    /// <summary>Se cerró. Dos notas que BAJAN: el espejo exacto de empezar.</summary>
    public const string Cierre = "cierre";

    /// <summary>No se pudo. Dos notas graves y cortas, que no se confunden con ninguna de arriba.</summary>
    public const string NoSePudo = "no-se-pudo";

    /// <summary>
    /// Qué suena en este momento.
    /// </summary>
    /// <remarks>
    /// «NO SE PUDO» SUENA, y esa es la que no estaba en la petición y más protege: un toque que no
    /// hace nada es indistinguible de uno que no llegó, y para quien no mira la pantalla esa duda es
    /// lo peor que le puede pasar un botón — no sabe si pausó, si el collar se soltó, o si falló.
    /// </remarks>
    public static string Para(MomentoDelCollar momento) => momento switch
    {
        MomentoDelCollar.Empieza => EnVivo,
        MomentoDelCollar.Reanuda => EnVivo,
        MomentoDelCollar.Pausa => Pausado,
        MomentoDelCollar.Termina => Cierre,
        _ => NoSePudo,
    };
}
