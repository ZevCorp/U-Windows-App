using System.Text.Json;

namespace U.WindowsClient.Clinical.Transcripcion;

/// <summary>
/// LO QUE EL BACKEND CONTESTA A <c>POST /api/v1/transcription/session</c>, y con ello QUIÉN lo va a
/// leer.
/// </summary>
/// <remarks>
/// EL PROVEEDOR LO DECIDE ESTA RESPUESTA, no el cliente — promesa 89. Está configurado en el
/// Provider Studio y puede conmutarse entre Soniox y Deepgram sin avisar a nadie; el cliente tiene
/// que enterarse por lo que le llega. El campo que manda es <c>auth_scheme</c>, y se lee con la
/// misma regla que el motor del portal (lib/stt/deepgram-dictation.js:63-67): «message» es Soniox,
/// cualquier otra cosa es la tupla del subprotocolo.
///
/// EL TOKEN QUE VIENE AQUÍ DURA ~60 SEGUNDOS. No es la key del proveedor: es una credencial temporal
/// que el backend emite por sesión, precisamente para que la key permanente no viaje nunca al
/// cliente. Por eso no se guarda, no se registra en el log y se pide una nueva en cada grabación.
/// </remarks>
public sealed class SesionDeStream
{
    private SesionDeStream(string url, string proveedor, string modelo, string idioma,
        ILectorDeStream lector)
    {
        Url = url;
        Proveedor = proveedor;
        Modelo = modelo;
        Idioma = idioma;
        Lector = lector;
    }

    public string Url { get; }
    public string Proveedor { get; }
    public string Modelo { get; }
    public string Idioma { get; }

    /// <summary>Quien sabe hablar con este proveedor. Elegido por la sesión, no por el compilador.</summary>
    public ILectorDeStream Lector { get; }

    /// <summary>
    /// Lee la respuesta del backend. Lanza si no trae dirección: sin <c>websocket_url</c> no hay
    /// nada que abrir, y decirlo aquí evita un fallo más adelante que sonaría a otra cosa.
    /// </summary>
    public static SesionDeStream Leer(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var raiz = doc.RootElement;

        string url = Texto(raiz, "websocket_url");
        if (url.Length == 0)
            throw new InvalidOperationException(
                "el backend no devolvió la dirección del stream (websocket_url)");

        string proveedor = Texto(raiz, "provider");
        string esquema = Texto(raiz, "auth_scheme");
        if (esquema.Length == 0) esquema = "bearer";
        string token = Texto(raiz, "access_token");

        bool tieneArranque = raiz.TryGetProperty("start_message", out var arranque)
                             && arranque.ValueKind == JsonValueKind.Object;

        // LA MISMA REGLA QUE EL PORTAL, y las dos mitades importan: el nombre del proveedor cuando
        // viene, y el esquema de autenticación siempre — porque el esquema es lo que decide CÓMO se
        // abre el socket, y equivocarse ahí cierra la conexión sin explicación.
        bool esSoniox = proveedor.Equals("soniox", StringComparison.OrdinalIgnoreCase)
                        || esquema.Equals("message", StringComparison.OrdinalIgnoreCase);

        ILectorDeStream lector = esSoniox
            ? new LectorSoniox(tieneArranque ? arranque.Clone() : default, tieneArranque)
            : new LectorDeepgram(esquema, token);

        return new SesionDeStream(url,
            proveedor.Length > 0 ? proveedor : lector.Nombre,
            Texto(raiz, "model"), Texto(raiz, "language"), lector);
    }

    private static string Texto(JsonElement o, string campo) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(campo, out var v)
        && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
