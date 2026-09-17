using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace U.WindowsClient.Voice;

/// <summary>
/// LAS MIRADAS DE UNA CONVERSACIÓN: se suben, se miran, y se retiran todas al cerrarla. Promesa 250 (spec 027).
/// </summary>
/// <remarks>
/// LA COPIA DURA LO QUE DURA LA CONVERSACIÓN QUE PUEDE LEERLA, y eso sigue cumpliendo la condición del
/// dueño dicha en mayúsculas: «QUE NO DUREN MUCHO TIEMPO EN OPENAI» (2026-09-16) — duran una llamada,
/// no días. Lo que se queda es la foto LOCAL, en el álbum de Ü; lo que viaja es una copia efímera.
///
/// NO SE BORRA AL RATO, Y ESTO SE APRENDIÓ ROMPIÉNDOLO. La primera versión solía una espera de ocho
/// segundos y borraba. Medido en la app el 2026-09-16: subida a las 20:26:55, borrada a las 20:27:03, y el
/// servidor contestando «Files [file-XPZ…] were not found» a las 20:27:04, 20:27:10 y 20:27:28. Con ningún
/// temporizador habría funcionado: la REFERENCIA se queda en el historial de la sesión, así que cualquier
/// respuesta posterior vuelve a pedir el archivo. Mientras la sesión viva, la copia tiene que estar.
///
/// Y POR ESO SE GUARDAN TODAS: mirar dos veces deja dos referencias vivas en el historial, y quedarse solo
/// con la última dejaba la primera colgada para siempre en la cuenta —basura— o borrada en falso.
///
/// POR QUÉ SUBIR Y NO INCRUSTAR: el buzón de la sesión admite 32.768 bytes para la conversación
/// entera y una captura pesa 118.000 codificada, así que dentro del mensaje no cabía ninguna — de ahí
/// venía que Ü fuera ciega. Subida aparte, en la sesión solo entra su identificador, unos treinta
/// bytes, y mirar deja de tener tope.
///
/// SE SUELTA PASE LO QUE PASE, salga bien la mirada o falle: un fallo no puede dejar basura en la
/// cuenta de nadie. Soltar dos veces no borra dos veces, y soltar sin haber subido no llama a nada.
/// </remarks>
public sealed class MiradaSubida
{
    private readonly Func<byte[], Task<string>> _subir;
    private readonly Func<string, Task> _borrar;
    private readonly List<string> _subidas = new();

    /// <param name="subir">Deja la foto en OpenAI y devuelve su identificador.</param>
    /// <param name="borrar">Borra esa copia.</param>
    public MiradaSubida(Func<byte[], Task<string>> subir, Func<string, Task> borrar)
    {
        _subir = subir;
        _borrar = borrar;
    }

    /// <summary>La que habla con OpenAI de verdad.</summary>
    public static MiradaSubida Real(Func<string> clave, Action<string>? anotar = null)
        => new(jpeg => SubirAOpenAI(jpeg, clave(), anotar), id => BorrarDeOpenAI(id, clave(), anotar));

    /// <summary>Sube la foto y devuelve con qué referirse a ella. Vacío si no se pudo.</summary>
    public async Task<string> SubirAsync(byte[] jpeg)
    {
        if (jpeg == null || jpeg.Length == 0) return "";
        string id = await _subir(jpeg);
        // La anterior NO se toca: su referencia sigue viva en el historial de la sesión.
        if (id.Length > 0) lock (_subidas) _subidas.Add(id);
        return id;
    }

    /// <summary>
    /// Retira TODAS las copias de esta conversación. Se llama al cerrarla, que es cuando ya nadie puede
    /// leerlas. Sin nada subido no hace nada; dos veces tampoco.
    /// </summary>
    public async Task SoltarAsync()
    {
        string[] pendientes;
        lock (_subidas) { pendientes = _subidas.ToArray(); _subidas.Clear(); }
        foreach (string id in pendientes) await _borrar(id);
    }

    private static readonly HttpClient Red = new() { Timeout = TimeSpan.FromSeconds(30) };

    private static async Task<string> SubirAOpenAI(byte[] jpeg, string clave, Action<string>? anotar)
    {
        try
        {
            using var cuerpo = new MultipartFormDataContent();
            cuerpo.Add(new StringContent("vision"), "purpose");
            var foto = new ByteArrayContent(jpeg);
            foto.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            cuerpo.Add(foto, "file", "pantalla.jpg");

            using var peticion = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/files") { Content = cuerpo };
            peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", clave);
            using var r = await Red.SendAsync(peticion);
            string texto = await r.Content.ReadAsStringAsync();
            if (!r.IsSuccessStatusCode) { anotar?.Invoke($"no pude subir la mirada: {(int)r.StatusCode} {Corto(texto)}"); return ""; }

            string id = JsonDocument.Parse(texto).RootElement.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "";
            anotar?.Invoke($"mirada subida: {jpeg.Length} bytes → {id}");
            return id;
        }
        catch (Exception e) { anotar?.Invoke($"no pude subir la mirada: {e.Message}"); return ""; }
    }

    private static async Task BorrarDeOpenAI(string id, string clave, Action<string>? anotar)
    {
        try
        {
            using var peticion = new HttpRequestMessage(HttpMethod.Delete, $"https://api.openai.com/v1/files/{id}");
            peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", clave);
            using var r = await Red.SendAsync(peticion);
            anotar?.Invoke(r.IsSuccessStatusCode
                ? $"mirada borrada de OpenAI: {id}"
                : $"no pude borrar la mirada {id}: {(int)r.StatusCode}");
        }
        catch (Exception e) { anotar?.Invoke($"no pude borrar la mirada {id}: {e.Message}"); }
    }

    private static string Corto(string t) => t.Length <= 160 ? t : t[..160];
}
