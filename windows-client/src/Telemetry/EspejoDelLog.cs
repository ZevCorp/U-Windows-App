using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Telemetry;

/// <summary>
/// Refleja el log en el backend, para poder mirar desde otra máquina qué está haciendo esta: la línea
/// MARCADA (<see cref="LogBus.Publico"/>) sube entera; cualquier otra sube solo como CADENCIA —su
/// etiqueta, su hora y su longitud—, y la de <c>telemetry</c> no sube.
///
/// POR QUÉ YA NO SUBE CADA LÍNEA (spec 051, 2026-09-24). Hasta hoy subía el texto entero de toda línea,
/// y el log lleva lo que Ü escribe en SAP: <c>RellenadorSap</c> anotaba ««Talla» = «38,5» (pedido
/// «38.5»)». La historia clínica salía de la máquina del médico por un canal de diagnóstico que nadie
/// había mirado con esos ojos. Arreglar los sitios que anotan valores no basta: el espejo subía POR
/// DEFECTO, y el sitio que alguien escriba mañana saldría igual. Ahora sale entero solo lo que alguien
/// decidió marcar, y lo marcado está congelado en el contrato (promesa 394). El fallo posible pasa a
/// ser «el panel no ve X» —visible, y se arregla marcando la línea— en vez de «la historia clínica
/// sale» —invisible—.
///
/// POR QUÉ EXISTE. El panel de Windows del Provider Studio —lista de equipos, eventos en vivo por
/// SSE, grafo por usuario— ya estaba entero, y el cliente ya emitía: arranques y finales de corrida,
/// pasos de workflow, acciones. Lo que NO viajaba era el log. Y la diferencia importa, porque los
/// eventos cuentan QUÉ se intentó y el log cuenta POR QUÉ salió como salió: el elemento que no se
/// encontró, la pantalla en la que se creía estar, el campo que no cuajó. Sin él, mirar el panel de
/// otra máquina decía que un paso falló pero no daba con qué arreglarlo, y tocaba pedirle a esa
/// persona que buscara %LOCALAPPDATA%\U\logs y lo mandara a mano (2026-08-16, pedido: «necesito
/// poder ver sus logs, grafo, todo desde mi pc»).
///
/// SE ENGANCHA AL LOG Y NO A CADA SITIO QUE INFORMA. La alternativa era ir sembrando <c>Emit</c> por
/// el código, y entonces lo que se ve en remoto y lo que se ve en el archivo se van separando en
/// cuanto alguien añade un log y olvida el otro. Enganchándose a <see cref="LogBus"/> hay una sola
/// fuente: cada línea del archivo del equipo llega aquí —entera si está marcada, como pulso si no—,
/// y el panel no se queda ciego a un subsistema porque nadie se acordó de él: lo ve hablar.
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
            LogBus.AnotadoConMarca += Reflejar;
            _encendido = true;
        }
        LogBus.Publico("espejo", "el log de este equipo se está reflejando en el panel");
    }

    public static void Apagar()
    {
        lock (_candado)
        {
            if (!_encendido) return;
            LogBus.AnotadoConMarca -= Reflejar;
            _encendido = false;
        }
    }

    private static void Reflejar(string etiqueta, string texto, bool publica)
    {
        // Ni cadencia: la cadencia de una queja de subida es otro evento que fallaría igual.
        if (string.Equals(etiqueta, SuPropiaEtiqueta, StringComparison.OrdinalIgnoreCase)) return;

        // UN SOLO Emit para las dos, a propósito: el censo de la 395 congela cada Emit del código con
        // todos sus argumentos, y dos llamadas serían dos sitios que pueden separarse.
        //
        // Marcada: `label` es lo que el panel enseña en la fila sin abrir nada, así que va el texto y no
        // la etiqueta: «no se pudo comprobar actualizaciones» dice algo desde la lista; «update», no.
        // Sin marcar: la etiqueta y la longitud, con las claves exactas `tag` y `largo`. El volumen de
        // eventos no cambia —el pulso en vivo del panel sigue—; cambia lo que lleva cada uno.
        TelemetryBus.Emit(
            kind: "log",
            phase: etiqueta,
            label: publica ? texto : Cadencia(etiqueta, texto),
            detail: publica ? (object)new { tag = etiqueta, text = texto } : new { tag = etiqueta, largo = texto.Length });
    }

    /// <summary>Lo que sube de una línea sin marcar: <c>‹etiqueta› · línea de N car.</c></summary>
    internal static string Cadencia(string etiqueta, string texto) => $"‹{etiqueta}› · línea de {texto.Length} car.";
}
