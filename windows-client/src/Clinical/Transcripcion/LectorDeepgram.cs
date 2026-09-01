using System.Text.Json;

namespace U.WindowsClient.Clinical.Transcripcion;

/// <summary>
/// Deepgram: autentica en la TUPLA del subprotocolo del WebSocket y entrega frases ya armadas.
/// </summary>
/// <remarks>
/// SIN PRIMER MENSAJE. Todo lo que Soniox configura por JSON —modelo, idioma, formato— Deepgram lo
/// lleva en la URL que firma el backend, así que mandar un primer frame de texto sería audio que no
/// espera. <see cref="MensajeDeArranque"/> devuelve <c>null</c> y eso es la respuesta correcta, no
/// un hueco.
///
/// DOS BANDERAS Y NO UNA, que es donde se equivoca quien viene de Soniox:
///   · <c>is_final</c> — este trozo ya no va a cambiar.
///   · <c>speech_final</c> — además, aquí acabó de hablar: ESTO es el fin de frase.
/// Cerrar la frase en cada `is_final` la partiría en pedazos, y no cerrarla nunca dejaría la consulta
/// entera como una sola frase. El motor del portal usa el mismo criterio.
///
/// SIN VERIFICAR CONTRA DEEPGRAM DE VERDAD (2026-09-01). El Provider Studio está hoy en Soniox, así
/// que este lector se juzga por su forma —promesa 89— y no por una corrida real. Es honesto decirlo:
/// vale para que conmutar el proveedor no tumbe el dictado, no para dar por probado Deepgram.
/// </remarks>
public sealed class LectorDeepgram : ILectorDeStream
{
    private readonly string _esquema;
    private readonly string _token;

    public LectorDeepgram(string esquema, string token)
    {
        _esquema = esquema;
        _token = token;
    }

    public string Nombre => "deepgram";

    /// <summary>La tupla [esquema, token]: así es como Deepgram autentica un WebSocket.</summary>
    public IReadOnlyList<string> Subprotocolos => new[] { _esquema, _token };

    /// <summary>Con F mayúscula. Cada proveedor escribe su despedida a su manera.</summary>
    public string MensajeDeFinalize => "{\"type\":\"Finalize\"}";

    public string? MensajeDeArranque(int ritmoHz) => null;

    public void Digerir(string json, Verbatim verbatim,
        Action<string> parcial, Action<string> frase, Action<string> fallo)
    {
        using var doc = JsonDocument.Parse(json);
        var raiz = doc.RootElement;

        string tipo = Texto(raiz, "type");
        if (tipo == "Error" || raiz.TryGetProperty("err_code", out _))
        {
            fallo($"Deepgram rechazó el stream: {Texto(raiz, "err_code")} {Texto(raiz, "err_msg")}".Trim());
            return;
        }

        if (!raiz.TryGetProperty("channel", out var canal)
            || !canal.TryGetProperty("alternatives", out var alternativas)
            || alternativas.ValueKind != JsonValueKind.Array
            || alternativas.GetArrayLength() == 0) return;

        string texto = Texto(alternativas[0], "transcript").Trim();
        if (texto.Length == 0) return;

        bool firme = Bandera(raiz, "is_final");
        if (!firme) { parcial((verbatim.Todo + " " + texto).Trim()); return; }

        verbatim.Confirmar(verbatim.Todo.Length > 0 ? " " + texto : texto);

        if (Bandera(raiz, "speech_final"))
        {
            string cerrada = verbatim.CerrarFrase();
            if (cerrada.Length > 0) frase(cerrada);
        }

        string pintar = verbatim.Todo;
        if (pintar.Length > 0) parcial(pintar);
    }

    private static bool Bandera(JsonElement o, string campo) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(campo, out var v)
        && v.ValueKind == JsonValueKind.True;

    private static string Texto(JsonElement o, string campo) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(campo, out var v)
        && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
