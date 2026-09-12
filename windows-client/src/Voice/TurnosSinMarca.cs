using System;
using Voz.Realtime;

namespace U.WindowsClient.Voice;

/// <summary>
/// LOS TURNOS DE UNA VOZ QUE NO LOS MARCA (promesa 209, spec 018): cuándo empieza un turno del usuario y
/// cuándo toca darlo por cerrado, a falta de speech_started y response.done.
/// </summary>
/// <remarks>
/// GPT-Live no manda NINGUNA marca de turno (medido contra el servidor el 2026-09-12): hablarle encima no
/// produce evento, y su response.completed es del modelo delegado — la voz sigue hablando segundos
/// después. Sin cierre no pasa nada visible y nada da error: simplemente la conversación no escribe
/// «Ü dijo», no entrega la frase a quien aprende (promesa 105) ni al piloto, y Cerro no llega nunca.
///
/// LO QUE CUENTA COMO ACTIVIDAD ES LO DICHO, NO EL AUDIO. El servidor manda session.output_audio.delta
/// sin parar, también en silencio: si el audio reiniciara la cuenta, con GPT-Live no se cerraría nunca.
/// Ese mismo audio continuo es lo que hace que esto se consulte a menudo sin temporizador propio.
///
/// EL PRECIO, DICHO ANTES DE QUE SE DESCUBRA: una pausa de más del silencio a media frase la parte en dos
/// turnos, y el turno empieza con el primer trozo de TRANSCRIPCIÓN, que llega después de que el usuario
/// empezó a hablar. Los 1500 ms son del diseño; sin medir con voz real (spec 018, «Lo que se pierde»).
///
/// Sin conocer la conversación ni el reloj del sistema, para poder juzgarlo sin esperar segundos de verdad.
/// </remarks>
public sealed class TurnosSinMarca
{
    public const int SilencioPorDefectoMs = 1500;

    private readonly Func<long> _relojMs;
    private readonly int _silencioMs;
    private readonly object _candado = new();

    /// <summary>Ya se abrió el turno de lo que está diciendo el usuario: el trozo siguiente no abre otro.</summary>
    private bool _usuarioEnTurno;

    /// <summary>Cuándo dijo algo cualquiera de los dos por última vez desde el último cierre; null si nada.</summary>
    private long? _ultimaActividad;

    /// <param name="relojMs">Milisegundos monótonos. Se inyecta para que el contrato juzgue sin esperar.</param>
    /// <param name="silencioMs">Cuánto silencio de lo dicho cierra el turno.</param>
    public TurnosSinMarca(Func<long> relojMs, int silencioMs = SilencioPorDefectoMs)
    {
        _relojMs = relojMs;
        _silencioMs = silencioMs;
    }

    /// <summary>Un hecho que llegó del servidor. Verdadero si abre un turno del usuario.</summary>
    public bool Oye(Hecho hecho)
    {
        lock (_candado)
        {
            switch (hecho)
            {
                case Hecho.DiceElUsuario:
                    _ultimaActividad = _relojMs();
                    if (_usuarioEnTurno) return false;
                    _usuarioEnTurno = true;
                    return true;

                // Lo que dice Ü mantiene el turno abierto y también se cierra por silencio, pero no es una
                // petición: no reinicia el tope ni la medida del turno del usuario.
                case Hecho.DiceU:
                    _ultimaActividad = _relojMs();
                    return false;

                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// Verdadero UNA vez cuando hubo algo dicho y lleva el silencio entero sin nada nuevo. Sin nada oído,
    /// nunca: un cierre sin frase dispararía Cerro y TurnoCerrado sobre una conversación que no ocurrió.
    /// </summary>
    public bool TocaCerrar()
    {
        lock (_candado)
        {
            if (_ultimaActividad is not long ultima || _relojMs() - ultima < _silencioMs) return false;
            _ultimaActividad = null;
            _usuarioEnTurno = false;
            return true;
        }
    }
}
