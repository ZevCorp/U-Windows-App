using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Radios;

namespace U.WindowsClient.Voice;

/// <summary>
/// SI HAY RADIO, Y QUÉ HACER CUANDO NO. Promesa 411 (spec 050).
/// </summary>
/// <remarks>
/// EL 2026-09-23 UNA USUARIA SIN BLUETOOTH vio el collar fallar doce veces en 42 s con
/// «COMException (0x800710DF): sin mensaje», y el estado le decía «no se encontró el collar». El
/// servicio trataba «no hay radio» igual que «el collar no está cerca» y reintentaba cada 6 s contra
/// algo que no iba a cambiar hasta que ella encendiera el Bluetooth — que es justo lo que el mensaje
/// no le decía (aprendizaje nº2).
///
/// La decisión es pura para que el contrato la juzgue sin radio; leer la radio de verdad es
/// <see cref="LeerAsync"/>, y eso solo se prueba en una máquina.
/// </remarks>
public static class ElBluetooth
{
    public enum Radio { Encendida, Apagada, SinAdaptador }

    public enum TrasFallar
    {
        /// <summary>Hay radio: el collar puede aparecer en cualquier momento.</summary>
        Reintentar,
        /// <summary>La radio está apagada: se espera a que la persona la encienda, sin sondear.</summary>
        EsperarAQueSeEncienda,
        /// <summary>No hay adaptador: no hay nada que esperar.</summary>
        NoReintentar,
    }

    /// <summary>ERROR_DEVICE_NOT_AVAILABLE: lo que contesta WinRT al rastrear con la radio apagada.</summary>
    private const int NoDisponible = unchecked((int)0x800710DF);

    /// <summary>
    /// Qué dice un fallo sobre la radio. Null si no dice nada: un error genérico no es «apagada», y
    /// decirlo sería inventar la causa.
    /// </summary>
    public static Radio? DelFallo(int hresult) => hresult == NoDisponible ? Radio.Apagada : null;

    public static TrasFallar QueHacer(Radio radio) => radio switch
    {
        Radio.Apagada => TrasFallar.EsperarAQueSeEncienda,
        Radio.SinAdaptador => TrasFallar.NoReintentar,
        _ => TrasFallar.Reintentar,
    };

    /// <summary>El estado que ve la persona: la causa y qué hacer, no «no se encontró el collar».</summary>
    public static string QueDecir(Radio radio) => radio switch
    {
        Radio.Apagada => "el Bluetooth de este equipo está apagado: enciéndelo y el collar se conecta solo",
        Radio.SinAdaptador => "este equipo no tiene Bluetooth: el collar no puede conectarse aquí",
        _ => "buscando el collar…",
    };

    /// <summary>¿Va al log? Solo si cambió: doce líneas iguales en 42 s no dicen más que una.</summary>
    public static bool SeDice(string? antes, string ahora) => !string.Equals(antes, ahora, System.StringComparison.Ordinal);

    /// <summary>
    /// La radio de verdad, y el objeto para enterarse de cuándo se enciende. Radio null = sin adaptador,
    /// o un adaptador sin radio que Windows sepa encender.
    /// </summary>
    public static async Task<(Radio Estado, Windows.Devices.Radios.Radio? Objeto)> LeerAsync()
    {
        var adaptador = await BluetoothAdapter.GetDefaultAsync();
        if (adaptador == null) return (Radio.SinAdaptador, null);
        var radio = await adaptador.GetRadioAsync();
        if (radio == null) return (Radio.SinAdaptador, null);
        return (radio.State == RadioState.On ? Radio.Encendida : Radio.Apagada, radio);
    }
}
