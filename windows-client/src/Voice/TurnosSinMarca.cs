using System;
using System.Collections.Generic;
using Voz.Realtime;

namespace U.WindowsClient.Voice;

/// <summary>
/// LOS TURNOS DE UNA VOZ QUE NO LOS MARCA (promesas 209, 211 y 212, spec 018): cuándo empieza una petición
/// del usuario y cuándo toca dar el turno por cerrado, a falta de speech_started y response.done.
/// </summary>
/// <remarks>
/// GPT-Live no manda NINGUNA marca de turno (medido contra el servidor el 2026-09-12): hablarle encima no
/// produce evento, y su response.completed es del modelo delegado — la voz sigue hablando segundos
/// después. Sin cierre no pasa nada visible y nada da error: simplemente la conversación no escribe
/// «Ü dijo», no entrega la frase a quien aprende (promesa 105) ni al piloto, y Cerro no llega nunca.
///
/// QUÉ SUJETA EL TURNO: lo dicho por los dos (las transcripciones), la voz de Ü mientras SUENA, y el trabajo
/// en marcha — una llamada a herramienta entre que se pide y se devuelve no deja cerrar, y devolverla cuenta
/// como actividad, porque es cuando el delegado sigue. Hasta el 2026-09-12 solo sujetaba lo dicho, y el turno
/// se cerraba a mitad de la tarea: la voz dice «Claro», el delegado llama, y con una herramienta que tardara
/// más que el silencio salían «Ü dijo: Claro» y Cerro antes de hacer nada (revisa:regresiones).
///
/// EL AUDIO EN SILENCIO NO CUENTA. El servidor manda un delta de 100 ms cada ~100-130 ms también cuando la voz
/// calla (pico 45, medido): si reiniciara la cuenta, con GPT-Live no se cerraría nunca. Ese mismo goteo es lo
/// que hace que esto se consulte a menudo sin temporizador propio.
///
/// EL SILENCIO SON 2000 ms, MEDIDOS (sonda de los turnos, 2026-09-12: tres corridas contra GPT-Live con una
/// frase hablada y herramientas de 300 y 2500 ms, y la regla reproducida sobre la línea de tiempo real):
/// - entre devolver una herramienta y lo siguiente que dice Ü pasan 1658-1707 ms, 4 de 4. Con 1500 y las
///   guardas el turno se seguía cerrando a mitad de la tarea (4 cierres de más en 3 corridas); con 2000,
///   ninguno. El margen son 293 ms sobre cuatro muestras de una tarde: lo confirma o lo corrige el nivel 4;
/// - una pausa del usuario de ~1 s deja un hueco de transcripción de 1065-1138 ms y no parte nada;
/// - una de ~1,9 s deja 1868-2119 ms (1 de 2 por encima): ESA todavía parte la frase que recibe DijoElUsuario.
///   El tope ya no, por la 212;
/// - el precio de subir: Cerro, «Ü dijo» y DijoElUsuario llegan 500 ms más tarde. La voz no: contesta sola.
///
/// Sin conocer la conversación ni el reloj del sistema, para poder juzgarlo sin esperar segundos de verdad.
/// </remarks>
public sealed class TurnosSinMarca
{
    public const int SilencioPorDefectoMs = 2000;

    /// <summary>
    /// Desde qué pico un trozo de audio es voz. El silencio del servidor pica en 45 y la palabra más floja
    /// medida picó en 1152 (2026-09-12, 207 deltas): 1000 queda 22 veces por encima del ruido y por debajo de
    /// toda palabra. Las colas de palabra más bajas (184-508) no mueven nada: caen a menos de 100 ms de un
    /// trozo fuerte.
    /// </summary>
    public const int PicoDeVoz = 1000;

    /// <summary>Cuántas devoluciones sin oír se recuerdan. Una tanda del delegado trae una o dos.</summary>
    private const int DevueltasQueSeRecuerdan = 64;

    private readonly Func<long> _relojMs;
    private readonly int _silencioMs;
    private readonly object _candado = new();

    /// <summary>Cuándo pasó por última vez algo que sujeta el turno desde el último cierre; null si nada.</summary>
    private long? _ultimaActividad;

    /// <summary>Hay una petición del usuario que Ü todavía no ha contestado con un cierre de por medio.</summary>
    private bool _peticionAbierta;

    /// <summary>Ü habló después de lo último que dijo el usuario.</summary>
    private bool _uContesto;

    /// <summary>
    /// Las llamadas pedidas y sin devolver, por REFERENCIA: la conversación devuelve las mismas instancias que
    /// llegaron en el Pide, y un call_id puede venir vacío.
    /// </summary>
    private readonly HashSet<Llamada> _enCurso = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Devueltas antes de oírse. El hilo que ejecuta puede ganarle al que recibe —una herramienta que no
    /// existe se contesta en microsegundos—, y sin esto esa llamada se quedaría en curso para siempre.
    /// </summary>
    private readonly HashSet<Llamada> _devueltasSinOir = new(ReferenceEqualityComparer.Instance);

    /// <param name="relojMs">Milisegundos monótonos. Se inyecta para que el contrato juzgue sin esperar.</param>
    /// <param name="silencioMs">Cuánto tiempo sin nada que sujete el turno lo cierra.</param>
    public TurnosSinMarca(Func<long> relojMs, int silencioMs = SilencioPorDefectoMs)
    {
        _relojMs = relojMs;
        _silencioMs = silencioMs;
    }

    /// <summary>Llamadas a herramienta pedidas y todavía sin devolver.</summary>
    public int LlamadasEnCurso
    {
        get { lock (_candado) return _enCurso.Count; }
    }

    /// <summary>Un hecho que llegó del servidor. Verdadero si abre una petición nueva del usuario.</summary>
    public bool Oye(Hecho hecho)
    {
        lock (_candado)
        {
            switch (hecho)
            {
                // UNA PAUSA SIN RESPUESTA NO ES OTRA PETICIÓN (promesa 212). Lo que el usuario dice tras un cierre
                // abre turno —y reinicia el tope de la 204— solo si Ü le había contestado. «Contestado» no es
                // «habló desde el último cierre»: con GPT-Live lo que contesta Ü cae DENTRO del turno sintético,
                // que solo se cierra cuando callan los dos (medido, 3 de 3).
                case Hecho.DiceElUsuario:
                    _ultimaActividad = _relojMs();
                    _uContesto = false;
                    if (_peticionAbierta) return false;
                    _peticionAbierta = true;
                    return true;

                // Lo que dice Ü sujeta el turno y lo contesta, pero no es una petición: no reinicia el tope.
                case Hecho.DiceU:
                    _ultimaActividad = _relojMs();
                    if (_peticionAbierta) _uContesto = true;
                    return false;

                // LA VOZ MIENTRAS SUENA. Su transcripción llega por delante del audio —la última palabra, 650-750
                // ms antes de que calle (medido)—, así que sin esto el silencio empezaría a contar con Ü hablando.
                // Solo alarga lo que ya hay: sonido sin nada dicho no abre un turno que cerrar.
                case Hecho.Suena s:
                    if (_ultimaActividad != null && Pico(s.Pcm) > PicoDeVoz) _ultimaActividad = _relojMs();
                    return false;

                case Hecho.Pide p:
                    foreach (var llamada in p.Cuales)
                        if (!_devueltasSinOir.Remove(llamada)) _enCurso.Add(llamada);
                    return false;

                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// Las llamadas de un Pide ya ejecutadas y contestadas (o descartadas). El silencio vuelve a contar desde
    /// aquí: es cuando el delegado sigue.
    /// </summary>
    public void Devuelta(IReadOnlyList<Llamada> llamadas)
    {
        lock (_candado)
        {
            bool eranDeEsteTurno = false;
            foreach (var llamada in llamadas)
            {
                if (_enCurso.Remove(llamada)) { eranDeEsteTurno = true; continue; }
                if (_devueltasSinOir.Count >= DevueltasQueSeRecuerdan) _devueltasSinOir.Clear();
                _devueltasSinOir.Add(llamada);
            }
            // Una devolución de otra sesión, sin nada dicho en esta, no abre un turno que cerrar.
            if (eranDeEsteTurno || _ultimaActividad != null) _ultimaActividad = _relojMs();
        }
    }

    /// <summary>
    /// Verdadero UNA vez cuando hubo algo y lleva el silencio entero sin nada nuevo ni llamadas en curso. Sin
    /// nada oído, nunca: un cierre sin frase dispararía Cerro y TurnoCerrado sobre una conversación que no ocurrió.
    /// </summary>
    public bool TocaCerrar()
    {
        lock (_candado)
        {
            if (_enCurso.Count > 0) return false;
            if (_ultimaActividad is not long ultima || _relojMs() - ultima < _silencioMs) return false;
            _ultimaActividad = null;
            if (_uContesto) { _peticionAbierta = false; _uContesto = false; }
            return true;
        }
    }

    /// <summary>El pico absoluto de un trozo PCM16 mono.</summary>
    private static int Pico(byte[] pcm)
    {
        int pico = 0;
        for (int i = 0; i + 1 < pcm.Length; i += 2)
        {
            int v = Math.Abs((int)BitConverter.ToInt16(pcm, i));
            if (v > pico) pico = v;
        }
        return pico;
    }
}
