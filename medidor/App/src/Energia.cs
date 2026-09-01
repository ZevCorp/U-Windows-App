using Microsoft.Win32;
using System.Runtime.Versioning;

namespace Medidor.App;

/// <summary>
/// Los eventos del PC que el medidor tiene que saber para no mentir: bloqueo/desbloqueo de sesión
/// y suspensión/reanudación. Un PC bloqueado dos horas no es «dos horas de trabajo», y despertar
/// de una suspensión no es actividad. El reloj ya acota los saltos (Reloj.AporteMaxMs), pero estos
/// eventos permiten CERRAR el turno con su causa correcta y distinguir «medidor vivo, PC quieto»
/// de «medidor muerto».
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Energia : IDisposable
{
    public event Action? Bloqueo;
    public event Action? Desbloqueo;
    public event Action? Suspende;
    public event Action? Reanuda;

    public bool Bloqueado { get; private set; }

    public void Escuchar()
    {
        SystemEvents.SessionSwitch += OnSession;
        SystemEvents.PowerModeChanged += OnPower;
    }

    private void OnSession(object sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock: Bloqueado = true; Bloqueo?.Invoke(); break;
            case SessionSwitchReason.SessionUnlock: Bloqueado = false; Desbloqueo?.Invoke(); break;
        }
    }

    private void OnPower(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) Suspende?.Invoke();
        else if (e.Mode == PowerModes.Resume) Reanuda?.Invoke();
    }

    public void Dispose()
    {
        SystemEvents.SessionSwitch -= OnSession;
        SystemEvents.PowerModeChanged -= OnPower;
    }
}
