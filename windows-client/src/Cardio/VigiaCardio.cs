using System.Windows.Threading;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Cardio;

/// <summary>
/// Borra la sesión de estudios caducada aunque nadie abra el panel: al abrir Ü y cada 10 minutos.
/// </summary>
/// <remarks>
/// Sin esto, unas fotos cargadas un lunes y olvidadas seguirían en disco hasta que alguien volviera a
/// pulsar «Subir» — y la promesa al médico es que «se borran solas», no que se borran al mirarlas.
/// </remarks>
public static class VigiaCardio
{
    public static readonly TimeSpan Cada = TimeSpan.FromMinutes(10);

    /// <summary>Avisa de que la sesión se borró sola, para que un panel abierto se vacíe.</summary>
    public static event Action? SeBorro;

    private static DispatcherTimer? _reloj;

    /// <summary>Se llama una vez, desde el hilo de la interfaz, al arrancar la carita.</summary>
    public static void Arrancar()
    {
        if (_reloj != null) return;
        Revisar("al abrir Ü");
        _reloj = new DispatcherTimer(DispatcherPriority.Background) { Interval = Cada };
        _reloj.Tick += (_, _) => Revisar("revisión de cada 10 min");
        _reloj.Start();
    }

    public static void Revisar(string cuando)
    {
        try
        {
            if (AlmacenCardio.DeLaApp().PurgarSiCaduco())
            {
                LogBus.Log("cardio", $"las fotos de estudios caducaron y se borraron ({cuando})");
                SeBorro?.Invoke();
            }
        }
        catch (Exception e)
        {
            // Un disco que no deja borrar no puede tumbar la carita. Se dice, y en 10 minutos se reintenta.
            LogBus.Log("cardio", $"no pude borrar las fotos caducadas ({cuando}): {e.GetType().Name}: {e.Message}");
        }
    }
}
