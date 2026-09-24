using System.Windows.Threading;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Ui.Jev;

/// <summary>
/// EL ADAPTADOR A WPF DE <see cref="IDespachador"/>: el único sitio de <c>Ui/Jev/</c> que deja trabajo en la cola del
/// <see cref="Dispatcher"/> de la interfaz. Promesa 382 (b), spec 049; lo que se juzga sin reloj vive en
/// <see cref="ConectorDeLaVista"/>, y aquí solo se traduce «encola» y «ya» a WPF.
/// </summary>
/// <remarks>
/// NINGÚN COMENTARIO DE ESTE ARCHIVO NOMBRA EL MÉTODO QUE ENCOLA: la 382 (b) cuenta sus apariciones bajo
/// <c>Ui/Jev/</c> como texto y exige una, y desde el paso 8c exige además que esa sea una llamada (hallazgo de la
/// fase 6, <see cref="IDespachador"/>).
///
/// UN ENCOLADO QUE WPF NO VA A EJECUTAR NO SE CALLA. Con la app cerrándose, el <see cref="Dispatcher"/> devuelve una
/// operación abortada en vez de lanzar; si eso volviera como «encolado», el conector se quedaría con su marca de
/// «hay un vaciado en cola» arriba para siempre y la vista dejaría de pintar sin decirlo. Se lanza, y el conector lo
/// lleva al log y baja la marca.
///
/// LO ENCOLADO NO TUMBA Ü. Una excepción que saliera de una acción encolada llegaría al manejador global del
/// <see cref="Dispatcher"/>; se queda en el log, con la cadena entera (patrón nº3). El pintor del conector ya
/// atrapa las suyas; esto es para lo demás que la vista encola —el aviso de lo pulsado—.
/// </remarks>
public sealed class DespachadorDeWpf : IDespachador
{
    private readonly Dispatcher _interfaz;

    /// <param name="interfaz">El del hilo de la interfaz: el de las ventanas de Jev.</param>
    public DespachadorDeWpf(Dispatcher interfaz) => _interfaz = interfaz ?? throw new ArgumentNullException(nameof(interfaz));

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">Si WPF no va a ejecutarlo: el hilo de la interfaz ya terminó o está terminando.</exception>
    public void Encolar(Action accion)
    {
        ArgumentNullException.ThrowIfNull(accion);
        var operacion = _interfaz.BeginInvoke(DispatcherPriority.Normal, new Action(() => Ejecutar(accion)));
        if (operacion.Status == DispatcherOperationStatus.Aborted)
            throw new InvalidOperationException(
                $"el hilo de la interfaz no acepta trabajo (HasShutdownStarted={_interfaz.HasShutdownStarted}, HasShutdownFinished={_interfaz.HasShutdownFinished}): lo encolado no se ejecutará");
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">
    /// Llamado desde otro hilo. Ejecutar ahí tocaría las ventanas desde fuera de su hilo, y esperar al de la interfaz
    /// es justo lo que la 382 prohíbe; desde otro hilo se usa <see cref="Encolar"/>.
    /// </exception>
    public void Ahora(Action accion)
    {
        ArgumentNullException.ThrowIfNull(accion);
        if (!_interfaz.CheckAccess())
            throw new InvalidOperationException(
                $"«Ahora» es para el hilo de la interfaz (el {_interfaz.Thread.ManagedThreadId}) y llegó desde el {Environment.CurrentManagedThreadId}: desde otro hilo se encola");
        accion();
    }

    private static void Ejecutar(Action accion)
    {
        try { accion(); }
        catch (Exception e)
        {
            for (var x = e; x != null; x = x.InnerException)
                LogBus.Log("jev-vista", $"✘ una acción encolada en el hilo de la interfaz lanzó {x.GetType().Name}: {x.Message}");
        }
    }
}
