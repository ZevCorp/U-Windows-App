namespace Mapeador;

/// <summary>
/// HACER UNA PREGUNTA QUE PUEDE NO CONTESTARSE NUNCA, sin quedarse esperando para siempre.
/// </summary>
/// <remarks>
/// UIA NO TIENE TIEMPO DE ESPERA. Una llamada a una ventana que no bombea mensajes —congelada,
/// suspendida por Windows, ocupada— no vuelve NUNCA. Y como la vuelta que pregunta dónde estamos
/// lleva candado de reentrada, mientras no vuelva el mapa deja de enterarse de dónde estás: no se
/// equivoca, se queda CIEGO, que es peor porque no lo dice.
///
/// Pasó de verdad: una consulta tardó 1.251.056 ms —veintiún minutos— y en ese rato se descartaron
/// 73 vueltas de ubicación sin un solo error en el log. Salió al mirar por qué la media de
/// `localizar` marcaba 1.461 ms cuando el coste real son 22 (2026-08-13).
///
/// SOLO UNA COLGADA A LA VEZ, y esto es lo que hace que la valla sea segura en vez de peligrosa: si
/// se abandonara la espera y se lanzara otra consulta cada 250 ms, todas se bloquearían contra la
/// MISMA ventana muerta y tendríamos cientos de hilos parados —cambiar un cuelgue por una fuga—.
/// Aquí se abandona la espera pero NO se lanza otra hasta que la anterior vuelva.
///
/// Mientras tanto se contesta lo vacío, que es la verdad: no sé dónde estoy. Quien pregunta ya sabe
/// no inventar con eso.
///
/// Vive aquí y no dentro del cliente por la misma razón que <see cref="VueltaUnica"/>: un mecanismo
/// de seguridad cuyo fallo es SILENCIOSO tiene que poder probarse, y allí dentro haría falta
/// levantar la app entera y esperar a que algo se cuelgue de verdad.
/// </remarks>
public sealed class SinColgarse(TimeSpan cuantoEsperar)
{
    private Task<string>? _enMarcha;

    /// <summary>¿Hay una pregunta abandonada que todavía no ha vuelto?</summary>
    public bool Colgada => _enMarcha is { IsCompleted: false };

    /// <summary>
    /// Pregunta y espera lo pactado. Devuelve la respuesta, o vacío si no llegó a tiempo —o si sigue
    /// sin llegar la anterior—.
    /// </summary>
    /// <param name="alColgarse">Se avisa la primera vez que se abandona la espera, y en cada intento
    /// posterior mientras siga sin volver: es lo que hace visible un mapa ciego.</param>
    /// <param name="alVolver">Se avisa cuando la abandonada por fin vuelve.</param>
    public string Pregunta(Func<string> trabajo, Action? alColgarse = null, Action? alVolver = null)
    {
        var antes = _enMarcha;
        if (antes != null)
        {
            if (!antes.IsCompleted) { alColgarse?.Invoke(); return ""; }
            _enMarcha = null;
            alVolver?.Invoke();
            // Lo que trae la que volvió YA NO SIRVE: contesta dónde estábamos hace un buen rato, y
            // una respuesta caducada sobre dónde estás es justo la mentira que rebobinaba el mapa.
            // Se descarta a propósito; la siguiente vuelta preguntará de nuevo, ya en limpio.
            return "";
        }

        var tarea = Task.Run(trabajo);
        if (tarea.Wait(cuantoEsperar))
            return tarea.IsCompletedSuccessfully ? tarea.Result : "";

        _enMarcha = tarea;
        alColgarse?.Invoke();
        return "";
    }
}
