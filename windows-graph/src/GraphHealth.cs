namespace U.Graph;

/// <summary>
/// Qué se sabe del enlace con Graph. Siete valores y no dos, porque el indicador anterior tenía uno
/// solo —«hay API key»— y con él describía situaciones que no se parecen en nada: sin configurar,
/// configurado pero nunca usado, respondiendo, rechazando la credencial, caído, e inalcanzable por
/// red. La noche del 2026-08-03 el punto estaba VERDE mientras el DNS fallaba, porque «verde»
/// significaba «dos cadenas no están vacías».
///
/// Un estado que no puede distinguir sus causas manda la investigación al sitio equivocado. Es el
/// mismo aprendizaje que costó dos diagnósticos con «getters de selección sin resultado».
/// </summary>
public enum GraphLink
{
    /// <summary>No hay URL o no hay API key: ni se intentó.</summary>
    SinKey,
    /// <summary>Hay credencial, pero todavía no se ha hablado con Graph en esta sesión.</summary>
    Desconocido,
    /// <summary>La última petición obtuvo 2xx. Graph está vivo y la key vale.</summary>
    Ok,
    /// <summary>401/403: se llegó a Graph y dijo que no con esta credencial.</summary>
    KeyRechazada,
    /// <summary>Respondió, pero con un error (5xx, o 4xx que no es de autenticación).</summary>
    ErrorDelServidor,
    /// <summary>No se pudo ni establecer la conexión: DNS, TLS, sin red.</summary>
    SinContacto,
    /// <summary>Se conectó pero no contestó dentro del tiempo de espera.</summary>
    SinRespuesta,
}

/// <summary>Una observación con su momento, para poder decir «hace cuánto» y no solo «qué».</summary>
public sealed record GraphObservation(GraphLink Link, DateTime AtUtc, int StatusCode, string Reason, string Host)
{
    public static readonly GraphObservation Ninguna =
        new(GraphLink.Desconocido, DateTime.MinValue, 0, "", "");

    /// <summary>¿Se ha hablado alguna vez con Graph? Distingue «no sé» de «sé que está mal».</summary>
    public bool Observado => AtUtc != DateTime.MinValue;

    /// <summary>Cuánto hace de la observación. <c>null</c> si nunca hubo ninguna.</summary>
    public TimeSpan? Edad => Observado ? DateTime.UtcNow - AtUtc : null;
}

/// <summary>
/// El estado del enlace con Graph, publicado. Estático puro y calcado de <c>LogBus</c> a propósito:
/// mismo par «instantánea + evento de cambio», mismo disparo fuera del lock, misma obligación de
/// desuscribirse (un evento estático al que se engancha una ventana sin soltarse es una fuga).
///
/// Vive en U.Graph y no en el cliente porque quien produce el dato es <see cref="GraphClient"/>, y la
/// dependencia va cliente → graph. Aquí no se decide NADA sobre cómo se pinta: el color y el texto
/// son cosa de <c>GraphHealthText</c>, del lado del cliente.
/// </summary>
public static class GraphHealth
{
    private static readonly object _lock = new();
    private static GraphObservation _current = GraphObservation.Ninguna;

    /// <summary>La última observación. El consumidor la drena al engancharse, como <c>LogBus.Snapshot()</c>.</summary>
    public static GraphObservation Current
    {
        get { lock (_lock) return _current; }
    }

    /// <summary>
    /// Cambió lo que se sabe del enlace. OJO: llega en el hilo de la continuación HTTP, no en el de
    /// la UI — quien lo consuma para pintar tiene que pasar por el Dispatcher.
    /// </summary>
    public static event EventHandler<GraphObservation>? Changed;

    /// <summary>
    /// Anota una observación. La escriben los dos embudos HTTP que hablan con Graph
    /// (<see cref="GraphClient.SendAsync"/> y el <c>BackendClient</c> del cliente).
    /// </summary>
    public static void Report(GraphLink link, string host, int statusCode = 0, string reason = "")
    {
        var obs = new GraphObservation(link, DateTime.UtcNow, statusCode, reason, host);
        lock (_lock) _current = obs;
        Changed?.Invoke(null, obs);   // fuera del lock: un handler lento no debe bloquear al siguiente HTTP
    }

    /// <summary>El host, sin esquema ni barra final: es lo único que se enseña en la interfaz.</summary>
    public static string HostOf(string baseUrl) =>
        (baseUrl ?? "").Replace("https://", "").Replace("http://", "").TrimEnd('/');

    /// <summary>
    /// Lo que se sabe DE ESTE host en concreto. Si la última observación es de otro, no vale: un
    /// indicador que dice describir <c>graph-eight-pied</c> no puede ponerse verde porque respondiera
    /// una máquina distinta.
    ///
    /// Suena teórico y no lo es — se vio en pruebas: los dos clientes HTTP toman su URL de sitios
    /// distintos (<c>GraphConfig.BaseUrl</c> y <c>Config.BackendUrl</c>, con sus respectivas variables
    /// de entorno de emergencia), así que pueden apuntar a hosts distintos y turnarse el semáforo.
    /// </summary>
    public static GraphObservation CurrentFor(string baseUrl)
    {
        string host = HostOf(baseUrl);
        var obs = Current;
        return string.Equals(obs.Host, host, StringComparison.OrdinalIgnoreCase)
            ? obs
            : new GraphObservation(GraphLink.Desconocido, DateTime.MinValue, 0, "", host);
    }
}
