using Omi;

namespace U.WindowsClient.Voice;

/// <summary>Lo que hay que hacer con el collar cuando se elige una fuente de audio.</summary>
public enum QueHacerConElCollar
{
    /// <summary>Es el elegido: conectarlo y dejarlo escuchando.</summary>
    Conectarlo,

    /// <summary>Se eligió otra cosa: SOLTARLO, no solo dejar de leerlo.</summary>
    Soltarlo,
}

/// <summary>
/// LO QUE LA INTERFAZ DICE QUE TE OYE TIENE QUE SER LO QUE TE OYE.
/// </summary>
/// <remarks>
/// EL FALLO, tal como lo describió el dueño (2026-09-06) y es de los peores que puede tener esto:
/// con el collar conectado, elegir «Computador» dejaba el collar EN AZUL y seguía entrando su
/// audio. Silenció el micrófono del portátil por teclado —o sea, seguro de que el portátil no
/// oía— pulsó grabar, y la voz llegó igual: por el collar. La interfaz decía una fuente y estaba
/// grabando por otra.
///
/// En una consulta clínica eso no es un detalle de interfaz. Quien silencia su micrófono lo hace
/// porque va a decir algo que no quiere que se grabe.
///
/// LA CAUSA: elegir el computador llamaba a <c>PasarAlLocal</c> —«deja de leer el collar»— y a
/// nadie más. El collar seguía conectado por Bluetooth y entregando tramas. Dejar de leer una
/// fuente no es soltarla, igual que <see cref="ReglaDelMuelle"/> no es lo mismo que esconder algo.
///
/// Y NO BASTA CON <c>Desconectar()</c>: el servicio reintenta mientras
/// <c>CollarPermanente.Permanente</c> siga puesto, así que el collar volvería solo a los pocos
/// segundos. Hay que apagar la INTENCIÓN, que es justo lo que <c>Apagar()</c> hace y lo que se pudo
/// separar limpiamente al partir <c>Permanente</c> de <c>Enlazado</c> (promesa 153): el collar deja
/// de escuchar sin dejar de estar emparejado, así que sigue en la lista y se puede volver a elegir.
///
/// El comentario de la rama del teléfono ya decía esto —«si el collar estaba enlazado aquí, hay que
/// soltarlo o seguiría oyéndose por él»— y el código no lo hacía. Otra vez la intención escrita y
/// no cumplida, como en <c>EncenderAsync</c>.
/// </remarks>
public static class ReglaDeLaFuente
{
    /// <summary>
    /// Con la fuente elegida, ¿qué se hace con el collar? Solo se queda escuchando si es él.
    /// </summary>
    public static QueHacerConElCollar AlElegir(Origen elegido) =>
        elegido == Origen.CollarPorBluetooth
            ? QueHacerConElCollar.Conectarlo
            : QueHacerConElCollar.Soltarlo;

    /// <summary>
    /// ¿Esta fila del menú se pinta como ACTIVA (en verde)?
    /// </summary>
    /// <remarks>
    /// ENTREGAR, NO ESTAR ELEGIDA. Es la misma exigencia que el verde del icono grande, que nació
    /// del verde falso del 2026-08-25 —56 minutos de «conectado» sin una sola trama, con el usuario
    /// delante de una demo—: la única prueba de que hay micrófono es que llegue audio. Una fila
    /// elegida pero muda tiene que verse distinta de una que está oyendo, o el color miente sobre
    /// lo único que importa.
    ///
    /// Y por eso son DOS condiciones y no una: ser la fuente real no basta si no entrega.
    /// </remarks>
    /// <param name="fila">El origen que dibuja esta fila.</param>
    /// <param name="real">La fuente que de verdad manda ahora mismo.</param>
    /// <param name="entregando">…y que está llegando audio por ella.</param>
    public static bool SeVeActiva(Origen fila, Origen real, bool entregando) =>
        entregando && fila == real;
}
