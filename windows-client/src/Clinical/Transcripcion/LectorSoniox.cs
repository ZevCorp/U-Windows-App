using System.Text;
using System.Text.Json;

namespace U.WindowsClient.Clinical.Transcripcion;

/// <summary>
/// Soniox: autentica y se configura en el PRIMER MENSAJE, y entrega token a token.
/// </summary>
/// <remarks>
/// EL FORMATO SE DECLARA, y está medido. El backend manda <c>audio_format: "auto"</c> porque quien
/// estrenó este camino fue el navegador, que sube WebM —y el WebM trae cabecera, así que «auto»
/// tiene algo que detectar—. Desde Windows sube PCM crudo del micrófono, que no tiene ninguna
/// cabecera, así que «auto» no detecta nada y el stream muere. Se sobrescriben los tres campos del
/// formato y se copia el resto tal cual: ahí viaja la clave temporal de 60 s y tocarla lo rompería.
///
/// Probado el 2026-08-14 con voz sintetizada: dictando «ciento veinte sobre ochenta» devolvió
/// «120/80», y «treinta y seis con ocho» devolvió «36,8» — Soniox normaliza las cifras como las
/// quiere un formulario clínico.
///
/// EL FIN DE FRASE ES UN TOKEN, no un campo del mensaje: llega uno cuyo texto es
/// <c>&lt;end&gt;</c>. Ahí es donde el portal dispara el organizador, y por eso se replica: es el
/// punto en el que lo dicho ya es una idea completa.
/// </remarks>
public sealed class LectorSoniox : ILectorDeStream
{
    private readonly JsonElement _arranqueDelBackend;
    private readonly bool _tieneArranque;

    public LectorSoniox(JsonElement arranqueDelBackend, bool tieneArranque)
    {
        _arranqueDelBackend = arranqueDelBackend;
        _tieneArranque = tieneArranque;
    }

    public string Nombre => "soniox";

    /// <summary>Socket pelado: la autenticación va en el primer mensaje, no en el subprotocolo.</summary>
    public IReadOnlyList<string> Subprotocolos => Array.Empty<string>();

    public string MensajeDeFinalize => "{\"type\":\"finalize\"}";

    public string? MensajeDeArranque(int ritmoHz)
    {
        if (!_tieneArranque) return null;

        var buffer = new System.IO.MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            foreach (var campo in _arranqueDelBackend.EnumerateObject())
            {
                if (campo.NameEquals("audio_format") || campo.NameEquals("sample_rate")
                    || campo.NameEquals("num_channels")) continue;
                campo.WriteTo(w);
            }
            w.WriteString("audio_format", "pcm_s16le");
            w.WriteNumber("sample_rate", ritmoHz);
            w.WriteNumber("num_channels", 1);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    public void Digerir(string json, Verbatim verbatim,
        Action<string> parcial, Action<string> frase, Action<string> fallo)
    {
        using var doc = JsonDocument.Parse(json);
        var raiz = doc.RootElement;

        if (raiz.TryGetProperty("error_code", out var codigo) || raiz.TryGetProperty("error_message", out _))
        {
            string msg = Texto(raiz, "error_message");
            // El 408 tras el finalize es la DESPEDIDA, no un fallo: llega después del texto
            // completo (medido 2026-08-14). Decirlo como error mandaría a mirar la red por nada.
            if (!msg.Contains("timeout", StringComparison.OrdinalIgnoreCase))
                fallo($"Soniox rechazó el stream: {Texto(raiz, "error_code")} {msg}".Trim());
            return;
        }

        if (!raiz.TryGetProperty("tokens", out var tokens) || tokens.ValueKind != JsonValueKind.Array)
            return;

        var provisional = new StringBuilder();
        foreach (var t in tokens.EnumerateArray())
        {
            string texto = Texto(t, "text");
            if (texto.Length == 0) continue;
            bool firme = t.TryGetProperty("is_final", out var f) && f.ValueKind == JsonValueKind.True;

            // Los dos tokens de control. No son habla y no se pintan nunca.
            if (texto == "<end>")
            {
                if (firme) { string cerrada = verbatim.CerrarFrase(); if (cerrada.Length > 0) frase(cerrada); }
                continue;
            }
            if (texto == "<fin>") continue;   // fin del STREAM, no del habla

            if (firme) verbatim.Confirmar(texto);
            else provisional.Append(texto);
        }

        string pintar = (verbatim.Todo + provisional).Trim();
        if (pintar.Length > 0) parcial(pintar);
    }

    private static string Texto(JsonElement o, string campo) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(campo, out var v)
        && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
