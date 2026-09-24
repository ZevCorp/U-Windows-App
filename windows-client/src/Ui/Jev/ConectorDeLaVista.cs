namespace U.WindowsClient.Ui.Jev;

/// <summary>
/// EL ÚNICO PASO ENTRE EL CICLO DE JEV Y LO QUE SE PINTA. Promesa 382 (spec 049): publicar un ciclo solo encola y
/// nunca ejecuta en el acto; con la cola sin vaciar se pinta solo el último; y una excepción al pintar no sale al
/// ciclo y queda en el log con su tipo y su mensaje.
/// </summary>
/// <remarks>
/// SE PINTA EL ÚLTIMO, NO LA COLA ENTERA. Si la ventana va atrasada —un arrastre, un GC, el hilo de la interfaz
/// ocupado— y llegan diez ciclos antes de que pueda pintar, pintar los diez es enseñar nueve estados que ya no son
/// verdad y llegar tarde al décimo. Se guarda el último y se encola UN vaciado: los que llegan mientras tanto
/// sustituyen al guardado y no encolan otro.
///
/// EL ORDEN DE <see cref="Vaciar"/> IMPORTA y es a propósito: primero se baja la marca de «hay un vaciado en cola»
/// y DESPUÉS se toma el último. Al revés, un ciclo publicado entre las dos cosas vería la marca aún puesta, no
/// encolaría nada, y se quedaría sin pintar hasta el siguiente. Así, lo peor que pasa es un vaciado de sobra que
/// no encuentra nada.
///
/// El pintor corre en el hilo del despachador; <see cref="Publicar"/>, en el del ciclo. Lo que comparten son dos
/// campos, y los dos se cambian con <see cref="Interlocked"/>: no hay candado que el ciclo pueda esperar.
/// </remarks>
public sealed class ConectorDeLaVista
{
    private readonly IDespachador _despachador;
    private readonly Action<CicloDeJev> _pintor;
    private readonly Action<string> _log;
    private CicloDeJev? _ultimo;
    private int _vaciadoEnCola;

    /// <param name="despachador">Dónde corre el pintor: el de WPF en la app, un doble sin reloj en el contrato.</param>
    /// <param name="pintor">Lo que pinta un ciclo. Corre en el hilo del despachador, nunca en el del ciclo.</param>
    /// <param name="log">A dónde va lo que falla al pintar: <c>LogBus</c> en la app.</param>
    public ConectorDeLaVista(IDespachador despachador, Action<CicloDeJev> pintor, Action<string> log)
    {
        _despachador = despachador ?? throw new ArgumentNullException(nameof(despachador));
        _pintor = pintor ?? throw new ArgumentNullException(nameof(pintor));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>
    /// Deja el ciclo para que se pinte y vuelve. No espera al pintado, no lo ejecuta en el acto y no lanza: lo llama
    /// el observador desde el hilo del paso, y nada de la vista puede frenar ni tumbar el ciclo.
    /// </summary>
    public void Publicar(CicloDeJev ciclo)
    {
        ArgumentNullException.ThrowIfNull(ciclo);
        Interlocked.Exchange(ref _ultimo, ciclo);
        if (Interlocked.Exchange(ref _vaciadoEnCola, 1) == 1) return;   // ya hay uno en cola: pintará este
        try { _despachador.Encolar(Vaciar); }
        catch (Exception e)
        {
            // UN DESPACHADOR QUE NO ACEPTA TRABAJO (la app cerrándose) no puede dejar la marca puesta: con ella
            // arriba no se volvería a encolar nada y la vista se quedaría congelada sin decirlo.
            Interlocked.Exchange(ref _vaciadoEnCola, 0);
            _log($"✘ no se pudo encolar el pintado de un ciclo de Jev: {Cadena(e)}. El ciclo sigue; la vista no se actualiza.");
        }
    }

    /// <summary>
    /// Pinta el último ciclo publicado, si hay uno, en el hilo que lo llama. Es lo que se encola; el contrato lo
    /// ejecuta a mano para juzgar el coalescing sin reloj.
    /// </summary>
    public void Vaciar()
    {
        Interlocked.Exchange(ref _vaciadoEnCola, 0);
        var ciclo = Interlocked.Exchange(ref _ultimo, null);
        if (ciclo == null) return;
        try { _pintor(ciclo); }
        catch (Exception e)
        {
            // LA CADENA ENTERA (patrón nº3), y el paso que falló. Pintar no es decidir: el ciclo ya pasó y la
            // mano ya pulsó, así que esto no sube a nadie; se dice y se sigue.
            _log($"✘ pintar un ciclo de Jev (paso {ciclo.Paso}, {ciclo.Fase}) lanzó {Cadena(e)}. El ciclo no se entera; la vista se queda con lo anterior.");
        }
    }

    private static string Cadena(Exception e)
    {
        string causa = "";
        for (var x = e; x != null; x = x.InnerException)
            causa += $"{x.GetType().Name}: {x.Message}" + (x.InnerException != null ? " ← " : "");
        return causa;
    }
}
