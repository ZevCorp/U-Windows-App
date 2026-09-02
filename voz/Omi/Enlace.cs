namespace Omi;

/// <summary>Qué hacer con el enlace del collar cuando Bluetooth avisa de un cambio.</summary>
public enum Accion
{
    /// <summary>Nada. El aviso no cambia lo que ya estaba pasando.</summary>
    Nada = 0,

    /// <summary>Rehacer el enlace ENTERO: soltarlo todo y volver a abrirlo desde cero.</summary>
    Reconstruir = 1,

    /// <summary>Darlo por perdido y devolver la voz al micrófono del PC.</summary>
    Relevar = 2,
}

/// <summary>
/// La política del enlace, en una función. Reconectar es reconstruir.
///
/// LA MEDIDA QUE OBLIGÓ A ESTO, del log del 2026-09-01 con el collar en la mano y el dueño probando:
///
/// <code>
///   149  «Bluetooth dice que el collar volvió a conectarse»
///   145  «al reenganchar: ObjectDisposedException: Cannot access a disposed object»
///     5  líneas con tramas en TODA la sesión
/// </code>
///
/// El ciclo se repetía cada segundo y medio y no entregó audio ni una vez. La causa es que el
/// reenganche reescribía el descriptor sobre la MISMA característica GATT de antes de la caída, y
/// WinRT ya la había destruido: no podía funcionar en ninguna de las 145.
///
/// El parche nació el 2026-08-30 para un fallo real y bien diagnosticado —«volver a conectarse no es
/// volver a estar suscrito», los 56 minutos mudos del 2026-08-25— y eligió el camino equivocado:
/// REMENDAR una sesión muerta en vez de REHACERLA. Es el aprendizaje nº6 otra vez: cuando la
/// maquinaria de compensación crece por capas, cada capa trae su propio modo de fallo.
///
/// Lo que este SDK ajeno ya hacía bien, y de donde sale la regla: <c>listen_to_omi</c> del SDK
/// oficial de Omi es un <c>async with BleakClient(...)</c>. Si el enlace cae, el cliente muere, el
/// bloque sale, y quien llama vuelve a empezar desde el rastreo. No hay ruta de remiendo porque no
/// puede haberla.
///
/// Aquí sólo vive la DECISIÓN. Soltar los objetos de WinRT, redescubrir el servicio sin caché y
/// releer el códec es de <c>windows-client</c>, que es quien tiene WinRT.
/// </summary>
public static class Enlace
{
    /// <param name="conectado">Lo que Bluetooth dice AHORA.</param>
    /// <param name="huboCaida">Si hubo una desconexión desde la última vez que se armó el enlace.</param>
    public static Accion QueHacer(bool conectado, bool huboCaida)
    {
        // Todavía caído: no hay nada contra lo que reconstruir. Se espera, que es lo que
        // MaintainConnection está haciendo por su cuenta.
        if (!conectado) return Accion.Nada;

        // Conectado y venimos de una caída: TODO lo de antes está muerto aunque parezca vivo.
        if (huboCaida) return Accion.Reconstruir;

        // Conectado sin caída de por medio: un aviso repetido de WinRT, que los manda en ráfaga
        // cuando el enlace baila. Reconstruir aquí sería tirar un enlace sano.
        return Accion.Nada;
    }
}
