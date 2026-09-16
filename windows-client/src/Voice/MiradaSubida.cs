using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace U.WindowsClient.Voice;

/// <summary>
/// UNA MIRADA: la foto se sube, se mira, y se borra. Promesa 250 (spec 027).
/// </summary>
/// <remarks>
/// LA COPIA EN OPENAI DURA LO QUE DURA LA MIRADA, y es una condición del dueño dicha en mayúsculas:
/// «QUE NO DUREN MUCHO TIEMPO EN OPENAI» (2026-09-16). Lo que se queda es la foto LOCAL, en el álbum
/// de Ü; lo que viaja es una copia efímera que existe solo para que Luna pueda verla.
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
    private string _id = "";

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
        _id = await _subir(jpeg);
        return _id;
    }

    /// <summary>Borra la copia subida. Sin nada subido no hace nada; dos veces tampoco.</summary>
    public async Task SoltarAsync()
    {
        string id = _id;
        if (id.Length == 0) return;
        _id = "";
        await _borrar(id);
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
