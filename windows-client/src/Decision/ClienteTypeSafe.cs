using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;

namespace U.WindowsClient.Decision;

/// <summary>
/// EL ÚNICO SITIO QUE TOCA LA RED. Sostiene las promesas 277, 280 y 283 en producción (spec 035).
/// </summary>
/// <remarks>
/// NINGUNA PRUEBA PASA POR AQUÍ, y es deliberado: el contrato inyecta su propio transporte. Esta
/// clase existe para que la app tenga uno de verdad, y está escrita para que lo único que pueda
/// fallar aquí sea la red — todo lo que se puede juzgar sin red vive en <see cref="ElDecisor"/>,
/// <see cref="PeticionASystemOne"/> y <see cref="PoliticaDeReintento"/>, que sí se juzgan.
///
/// LA CLAVE SE LEE AL CONSTRUIR Y NO SE GUARDA EN NINGÚN CAMPO PROPIO: va directa a la cabecera del
/// <see cref="HttpClient"/>. No se registra, no se imprime, y no entra en el cuerpo.
///
/// EL PLAZO SE APLICA DOS VECES a propósito: el <c>Timeout</c> del cliente corta la llamada, y el
/// <see cref="CancellationTokenSource"/> corta la espera entre reintentos. Sin lo segundo, tres
/// reintentos de 2 s con sus esperas se comen catorce segundos de un paso que prometía dos.
/// </remarks>
public sealed class ClienteTypeSafe : IDisposable
{
    private readonly HttpClient _http;
    private readonly int _plazoMs;
    private readonly Action<string>? _log;

    /// <param name="clave">La credencial. Sale del entorno; nunca del código.</param>
    /// <param name="plazoMs">Plazo total, incluidas las esperas entre reintentos.</param>
    /// <param name="log">Dónde contar lo que pasó. Nunca recibe la clave ni el cuerpo entero.</param>
    public ClienteTypeSafe(string clave, int plazoMs = ConfiguracionDelDecisor.TiempoMaximoPorDefectoMs, Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(clave))
            throw new ArgumentException($"sin {PeticionASystemOne.VariableDeLaClave} no se construye el cliente", nameof(clave));

        _plazoMs = plazoMs;
        _log = log;
        _http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(plazoMs) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", clave.Trim());
    }

    /// <summary>
    /// Manda el cuerpo a <c>/v1/systemone</c> y devuelve la respuesta cruda.
    /// </summary>
    /// <remarks>
    /// LANZA CUANDO NO PUEDE CONTESTAR, en vez de devolver vacío: <see cref="ElDecisor"/> ya sabe
    /// caer a Luna ante una excepción, y devolver "" obligaría a distinguir «no contestó» de
    /// «contestó vacío» en dos sitios en vez de uno.
    /// </remarks>
    public string Pregunta(string cuerpo)
    {
        using var reloj = new CancellationTokenSource(_plazoMs);
        Exception? ultima = null;

        for (int intento = 0; intento < PoliticaDeReintento.IntentosMaximos; intento++)
        {
            if (intento > 0)
            {
                int espera = PoliticaDeReintento.EsperaMs(intento - 1);
                if (reloj.IsCancellationRequested) break;
                // Si la espera no cabe en lo que queda de plazo, no se espera: se devuelve el turno.
                if (reloj.Token.WaitHandle.WaitOne(espera)) break;
            }

            try
            {
                using var peticion = new HttpRequestMessage(HttpMethod.Post, PeticionASystemOne.Url)
                {
                    Content = new StringContent(cuerpo, Encoding.UTF8, "application/json"),
                };
                using var r = _http.Send(peticion, reloj.Token);
                int codigo = (int)r.StatusCode;

                if (r.IsSuccessStatusCode)
                    return r.Content.ReadAsStringAsync(reloj.Token).GetAwaiter().GetResult();

                // EL CUERPO DEL ERROR SE LEE Y SE CUENTA. Un 422 dice QUÉ campo está mal, y sin eso
                // el mensaje sería «falló» — una conclusión disfrazada de hecho (patrón nº2).
                string detalle = DetalleDelError(() => r.Content.ReadAsStringAsync(reloj.Token).GetAwaiter().GetResult());
                if (detalle.Length > 300) detalle = detalle.Substring(0, 300) + "…";

                ultima = new HttpRequestException($"TypeSafe contestó {codigo}. {detalle}");
                _log?.Invoke($"typesafe: {codigo} en el intento {intento + 1}/{PoliticaDeReintento.IntentosMaximos}. {detalle}");

                if (!PoliticaDeReintento.SeReintenta(codigo)) throw ultima;
            }
            catch (HttpRequestException e) when (PoliticaDeReintento.SeReintenta(CodigoDe(e)))
            {
                ultima = e;
            }
            catch (OperationCanceledException e)
            {
                // Se acabó el plazo. No se reintenta: ya no queda tiempo que gastar.
                throw new TimeoutException($"TypeSafe no contestó en {_plazoMs} ms.", e);
            }
        }

        throw ultima ?? new TimeoutException($"TypeSafe no contestó en {_plazoMs} ms.");
    }

    /// <summary>
    /// El cuerpo de un error de la API, o POR QUÉ no se pudo leer (promesa 388, patrón nº3). Hasta el
    /// 2026-09-22 esto era un <c>catch { }</c> mudo: un 422 cuyo cuerpo no llegaba se contaba como «TypeSafe
    /// contestó 422.» a secas, y «vino vacío» y «se cortó al leerlo» eran indistinguibles.
    /// </summary>
    public static string DetalleDelError(Func<string> leer)
    {
        try { return leer() ?? ""; }
        catch (Exception e)
        {
            var cadena = "";
            for (var x = e; x != null; x = x.InnerException)
                cadena += $"{x.GetType().Name}: {x.Message}" + (x.InnerException != null ? " ← " : "");
            return $"(no se pudo leer el cuerpo del error: {cadena})";
        }
    }

    /// <summary>Saca el código que se metió en el mensaje, para poder decidir si se reintenta.</summary>
    private static int CodigoDe(HttpRequestException e) =>
        e.StatusCode.HasValue ? (int)e.StatusCode.Value
        : (e.Message.Contains(" 429") ? 429 : e.Message.Contains(" 529") ? 529 : 0);

    /// <summary>
    /// El transporte que <see cref="ElDecisor"/> espera, o <c>null</c> si esta máquina no puede hablar
    /// con TypeSafe. Devolver null y no lanzar es lo que deja que la app arranque igual sin clave.
    /// </summary>
    public static Func<string, string>? TransporteSegun(ConfiguracionDelDecisor cfg, Func<string, string?> entorno, Action<string>? log = null)
    {
        if (cfg == null || cfg.Quien != "jev") return null;
        string? clave = entorno(PeticionASystemOne.VariableDeLaClave);
        if (string.IsNullOrWhiteSpace(clave)) return null;
        var cliente = new ClienteTypeSafe(clave, cfg.TiempoMaximoMs, log);
        return cliente.Pregunta;
    }

    public void Dispose() => _http.Dispose();
}
