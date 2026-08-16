using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Telemetry;

/// <summary>
/// Manda al backend cada línea del log, para poder mirar desde otra máquina qué está haciendo esta.
///
/// POR QUÉ EXISTE. El panel de Windows del Provider Studio —lista de equipos, eventos en vivo por
/// SSE, grafo por usuario— ya estaba entero y llevaba meses enseñando una pantalla vacía: el carril
/// de telemetría existía en las dos puntas, pero <c>TelemetryBus.Emit</c> no tenía UN SOLO llamador.
/// Había tubería y no había agua. La consecuencia práctica era que, para saber qué había pasado en
/// el equipo de otra persona, tocaba pedirle que buscara %LOCALAPPDATA%\U\logs y lo mandara a mano
/// (2026-08-16, pedido: «necesito poder ver sus logs, grafo, todo desde mi pc»).
///
/// SE ENGANCHA AL LOG Y NO A CADA SITIO QUE INFORMA. La alternativa era ir sembrando <c>Emit</c> por
/// el código, y entonces lo que se ve en remoto y lo que se ve en el archivo se van separando en
/// cuanto alguien añade un log y olvida el otro. Enganchándose a <see cref="LogBus"/> hay una sola
/// fuente: lo que está en el archivo del equipo es exactamente lo que llega aquí.
/// </summary>
public static class EspejoDelLog
{
    private static bool _encendido;
    private static readonly object _candado = new();

    /// <summary>
    /// La etiqueta que NO se reenvía nunca.
    ///
    /// Es una realimentación, no una preferencia: la telemetría escribe en el log cuando falla al
    /// subir («flush: …»), así que reenviar esa línea encolaría otro evento, que fallaría igual, que
    /// escribiría otra línea. Con el backend caído —el momento exacto en que más se loguea— la cola
    /// se llenaría sola de quejas sobre sí misma y echaría fuera los eventos de verdad.
    /// </summary>
    private const string SuPropiaEtiqueta = "telemetry";

    /// <summary>Empieza a reflejar. Llamar después de <c>TelemetryBus.Init</c>; sin correo, Emit ya es no-op.</summary>
    public static void Encender()
    {
        lock (_candado)
        {
            if (_encendido) return;   // suscribirse dos veces duplicaría cada línea
            LogBus.Anotado += Reflejar;
            _encendido = true;
        }
        LogBus.Log("espejo", "el log de este equipo se está reflejando en el panel");
    }

    public static void Apagar()
    {
        lock (_candado)
        {
            if (!_encendido) return;
            LogBus.Anotado -= Reflejar;
            _encendido = false;
        }
    }

    private static void Reflejar(string etiqueta, string texto)
    {
        if (string.Equals(etiqueta, SuPropiaEtiqueta, StringComparison.OrdinalIgnoreCase)) return;

        // `label` es lo que el panel enseña en la fila sin abrir nada, así que va el texto y no la
        // etiqueta: «no se pudo comprobar actualizaciones» dice algo desde la lista; «update», no.
        // El backend lo recorta a 500, y el detalle completo viaja aparte por si la línea es larga.
        TelemetryBus.Emit(
            kind: "log",
            phase: etiqueta,
            label: texto,
            detail: new { tag = etiqueta, text = texto });
    }
}
