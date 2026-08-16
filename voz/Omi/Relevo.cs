namespace Omi;

/// <summary>
/// Decide cuándo dejar de esperar al collar y volver al micrófono del portátil.
///
/// El collar se queda sin batería, te lo quitas, o sales del alcance. Lo que NO puede pasar es que Ü
/// se quede muda esperando tramas que ya no vienen: el micrófono local sigue ahí y la conversación
/// puede seguir.
///
/// EL TIEMPO SIN TRAMAS NO DISTINGUE SUS DOS CAUSAS, y esto costó una corrida el 2026-08-13. Con
/// umbral de 4 s la voz se cayó sola mientras el usuario ESCUCHABA a Ü: el collar no transmite
/// silencio, así que estarse callado se veía idéntico a haberse ido. El umbral se había elegido
/// midiendo pausas de alguien hablando (2,01 s la mayor), y escuchando el silencio dura lo que dure
/// la frase de Ü.
///
/// Subir el umbral sólo habría hecho el fallo más raro y más difícil de reproducir. Quien sí
/// distingue es Bluetooth: el aparato está conectado o no lo está. El tiempo se queda de red para la
/// conexión colgada que nunca avisa, y por eso su umbral es largo.
///
/// Sólo la DECISIÓN vive aquí. Abrir y cerrar micrófonos es de windows-client, que es quien tiene
/// WinRT y NAudio. Aquí se puede juzgar sin collar; allí no.
/// </summary>
public sealed class Relevo
{
    private readonly int _umbralMs;

    public Relevo(int umbralMs) => _umbralMs = umbralMs;

    /// <summary>
    /// Por qué se relevó. Vacío mientras no se haya relevado.
    ///
    /// Se guarda el motivo y no sólo el hecho: «se cambió sola de micrófono» sin explicación es
    /// indistinguible de un fallo, y acabaría investigándose como tal.
    /// </summary>
    public string Motivo { get; private set; } = "";

    /// <summary>
    /// Cuánto se le da a Windows para rehacer el enlace antes de darlo por perdido.
    ///
    /// Con <c>MaintainConnection</c> puesto, Windows tira y rehace la conexión él solo: se midió un
    /// ciclo entero de desconectar y volver en UN segundo (2026-08-13, 22:32:48 → 22:32:49). Relevar
    /// en el primer sondeo convertía ese parpadeo en un cambio de micrófono, que es lo que se sentía
    /// como inestabilidad.
    /// </summary>
    private const int GraciaDesconexionMs = 3000;

    /// <param name="msSinTrama">Cuánto lleva el collar sin mandar audio.</param>
    /// <param name="msDesconectado">Cuánto lleva Bluetooth diciendo que no está. 0 si sigue conectado.</param>
    public bool HayQueRelevar(long msSinTrama, long msDesconectado)
    {
        // LA SEÑAL DE VERDAD es la desconexión, pero sostenida: un parpadeo no es una pérdida.
        if (msDesconectado >= GraciaDesconexionMs)
        {
            Motivo = $"el collar lleva {msDesconectado / 1000.0:0.0} s desconectado "
                   + $"y Windows no lo ha recuperado (gracia {GraciaDesconexionMs} ms)";
            return true;
        }

        // Y el tiempo se queda SÓLO como red de seguridad, para la conexión que se cuelga sin
        // avisar: sigue diciendo «conectado» y no vuelve a mandar una trama nunca. Por eso el umbral
        // es largo — no está midiendo silencio, está midiendo una avería.
        if (msSinTrama < _umbralMs) return false;

        Motivo = $"{msSinTrama} ms sin una sola trama con el collar aún conectado "
               + $"(red de seguridad, umbral {_umbralMs} ms): la conexión parece colgada";
        return true;
    }

    /// <summary>Volvió a llegar audio del collar: se olvida el motivo y se vuelve a empezar a contar.</summary>
    public void Reiniciar() => Motivo = "";
}
