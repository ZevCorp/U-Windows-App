using U.WindowsClient.Diagnostics;
using SinValor = U.Graph.SinValor;

namespace U.WindowsClient.Voice;

/// <summary>Reloj local de recordatorios. Vive aunque la sesión de voz esté apagada.</summary>
public sealed class RecordatoriosEnVivo : IDisposable
{
    private readonly MemoriaPersonal _memoria;
    private readonly Action<string> _avisar;
    private readonly Timer _reloj;
    private int _ocupado;

    public RecordatoriosEnVivo(MemoriaPersonal memoria, Action<string> avisar)
    {
        _memoria = memoria;
        _avisar = avisar;
        _reloj = new Timer(_ => Revisar(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(750));
    }

    private void Revisar()
    {
        if (Interlocked.Exchange(ref _ocupado, 1) != 0) return;
        try
        {
            foreach (var recordatorio in _memoria.Pendientes(DateTimeOffset.Now))
            {
                string mensaje = $"Te debía recordar: {recordatorio.Text}. ¿Quieres que haga algo?";
                if (!_memoria.MarcarEntregado(recordatorio.Id)) continue;
                // El recordatorio lo dictó la persona: por su longitud (spec 051, D5). El texto se le dice a ella
                // por _avisar; al log le basta saber que se entregó y cuándo.
                LogBus.Log("recordatorio", $"entregado a las {DateTimeOffset.Now:HH:mm:ss}: {SinValor.Forma(recordatorio.Text)}");
                _avisar(mensaje);
            }
        }
        catch (Exception e)
        {
            LogBus.Log("recordatorio", $"no pude revisar recordatorios locales: {e.Message}");
        }
        finally { Volatile.Write(ref _ocupado, 0); }
    }

    public void Dispose() => _reloj.Dispose();
}
