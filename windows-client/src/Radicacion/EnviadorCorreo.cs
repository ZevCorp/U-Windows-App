using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Radicacion;

/// <summary>
/// Manda el correo de radicación por Resend (api.resend.com) — API de una sola llamada, sin
/// verificación de remitente para empezar a probar.
///
/// AVISO QUE HAY QUE LEER ANTES DE LA DEMO: sin un dominio propio verificado en Resend, la cuenta
/// solo puede ENTREGAR a la casilla con la que se creó la cuenta — no a los correos ficticios que
/// tiene cada área (esos no existen de verdad). Por eso el "to" real es RESEND_TO_DEMO (la casilla
/// verificada) y el correo ficticio del área aparece en el asunto y en el cuerpo como "destino
/// oficial (simulado)". El día que haya dominio propio, el único cambio es apuntar RESEND_TO_DEMO
/// al correo real del área — el código no cambia.
/// </summary>
public static class EnviadorCorreo
{
    private static string ClaveApi => Environment.GetEnvironmentVariable("RESEND_API_KEY")?.Trim() ?? "";
    private static string Remitente => Environment.GetEnvironmentVariable("RESEND_FROM")?.Trim() is { Length: > 0 } v ? v : "onboarding@resend.dev";
    private static string CasillaDemo => Environment.GetEnvironmentVariable("RESEND_TO_DEMO")?.Trim() ?? "";

    /// <summary>Motivo va SIEMPRE lleno cuando Enviado es false — un booleano solo no distingue por qué.</summary>
    public static async Task<(bool Enviado, string? Motivo)> EnviarAsync(AreaDestino area, Radicado radicado, CancellationToken ct)
    {
        if (ClaveApi.Length == 0)
        {
            const string motivo = "falta RESEND_API_KEY en el entorno";
            LogBus.Log("radicacion", $"correo NO enviado para {radicado.Numero}: {motivo}");
            return (false, motivo);
        }
        if (CasillaDemo.Length == 0)
        {
            const string motivo = "falta RESEND_TO_DEMO (la casilla verificada de la cuenta de Resend)";
            LogBus.Log("radicacion", $"correo NO enviado para {radicado.Numero}: {motivo}");
            return (false, motivo);
        }

        var doc = radicado.Documento;
        string asunto = $"[DEMO · destino real: {area.CorreoFicticio}] Radicado {radicado.Numero} — {area.Nombre}";
        string advertencias = doc.Advertencias.Count > 0 ? string.Join(", ", doc.Advertencias) : "ninguna";
        string html = $"""
            <p><b>Este correo es una demo.</b> El área detectada es <b>{area.Nombre}</b> y en producción
            llegaría a <code>{area.CorreoFicticio}</code>; por ahora llega aquí porque la cuenta de
            Resend todavía no tiene un dominio propio verificado.</p>
            <hr/>
            <p><b>Número provisional:</b> {radicado.Numero} (pendiente de confirmación en el sistema oficial de radicación)<br/>
            <b>Fecha/hora:</b> {radicado.FechaHoraUtc:yyyy-MM-dd HH:mm} UTC<br/>
            <b>Tipo de documento:</b> {doc.TipoDocumento}<br/>
            <b>Remitente:</b> {doc.Remitente}<br/>
            <b>Cédula/NIT:</b> {doc.CedulaONit}<br/>
            <b>Afiliado/IPS/Empleador:</b> {doc.AfiliadoIpsEmpleador}<br/>
            <b>Fecha del documento:</b> {doc.FechaDocumento}<br/>
            <b>Asunto:</b> {doc.Asunto}<br/>
            <b>Número referido:</b> {doc.NumeroReferido}<br/>
            <b>Advertencias:</b> {advertencias}</p>
            <p><b>Resumen:</b> {doc.Resumen}</p>
            """;

        var cuerpo = new { from = Remitente, to = new[] { CasillaDemo }, subject = asunto, html };
        using var http = RedResiliente.ClienteHttp(TimeSpan.FromSeconds(30));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ClaveApi);
        using var contenido = new StringContent(JsonSerializer.Serialize(cuerpo), Encoding.UTF8, "application/json");

        try
        {
            var r = await http.PostAsync("https://api.resend.com/emails", contenido, ct);
            string texto = await r.Content.ReadAsStringAsync(ct);
            if (!r.IsSuccessStatusCode)
            {
                string motivo = ExtraerMensajeResend(texto) ?? $"Resend respondió {(int)r.StatusCode}";
                LogBus.Log("radicacion", $"Resend respondió {(int)r.StatusCode} para {radicado.Numero}: {texto}");
                return (false, motivo);
            }
            LogBus.Log("radicacion", $"correo enviado para {radicado.Numero} → {CasillaDemo} (área real simulada: {area.CorreoFicticio})");
            return (true, null);
        }
        catch (Exception e)
        {
            LogBus.Log("radicacion", $"fallo de red mandando el correo de {radicado.Numero}: {e.Message}");
            return (false, $"fallo de red: {e.Message}");
        }
    }

    private static string? ExtraerMensajeResend(string cuerpoRespuesta)
    {
        try
        {
            using var doc = JsonDocument.Parse(cuerpoRespuesta);
            return doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : null;
        }
        catch { return null; }
    }
}
