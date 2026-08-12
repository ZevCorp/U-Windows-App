using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using U.Graph;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Navigation;

/// <summary>
/// LA VENTANITA DEL NÚCLEO: un servidor mínimo para preguntarle dónde estamos y pedirle que nos
/// lleve a un sitio. Vive en el mapeador y el núcleo NO SE ENTERA de que existe.
///
/// Por qué así y no de las otras dos formas que se plantearon (2026-08-12, las rechazó el usuario
/// por buenos motivos):
///   · Meter el clic en el explorador del grafo obligaba a tocar 3.000 líneas del pintor viejo,
///     que es justo lo que estamos reemplazando.
///   · Usar Neo4j como buzón —escribir allí «quiero ir a X»— convierte la base de datos en un
///     canal de mensajes, que no es lo que es, y ensucia lo único que sirve para MIRAR.
///
/// Aquí la frontera se respeta entera:
///   · El NÚCLEO decide («¿cuál es el siguiente paso?») y no cambia ni una línea por esto.
///   · El MAPEADOR ejecuta, con las mismas manos con las que ya pulsa todo lo demás.
///   · El visor pregunta y obedece: no calcula nada, así que no puede opinar distinto.
///
/// UN PASO POR PETICIÓN, y es deliberado. El núcleo contesta un solo paso porque el mapa es vivo;
/// si esto encadenara los pasos por su cuenta estaría reconstruyendo la ruta completa que el núcleo
/// se niega a dar, y volveríamos a seguir un plano viejo. Quien quiera llegar, que vuelva a
/// preguntar al llegar — que es exactamente lo que hace una persona.
/// </summary>
public sealed class ServidorDelNucleo : IDisposable
{
    public const int Puerto = 8792;

    private readonly Nucleo.Grafo _grafo;
    private readonly Func<string> _donde;
    private readonly Func<string, string, bool> _pulsar;   // (selector, etiqueta) → ¿se pulsó?
    private readonly Func<string, bool> _enfocar;          // (proceso) → ¿está delante?
    private HttpListener? _oreja;

    public ServidorDelNucleo(Nucleo.Grafo grafo, Func<string> donde,
        Func<string, string, bool> pulsar, Func<string, bool> enfocar)
    {
        _grafo = grafo;
        _donde = donde;
        _pulsar = pulsar;
        _enfocar = enfocar;
    }

    public bool Arrancar()
    {
        try
        {
            _oreja = new HttpListener();
            _oreja.Prefixes.Add($"http://127.0.0.1:{Puerto}/");
            _oreja.Start();
            _ = Task.Run(Atender);
            LogBus.Log("nucleo-http", $"escuchando en http://127.0.0.1:{Puerto}/");
            return true;
        }
        catch (Exception e)
        {
            LogBus.Log("nucleo-http", $"no pude escuchar en {Puerto}: {e.Message}");
            return false;
        }
    }

    private async Task Atender()
    {
        while (_oreja?.IsListening == true)
        {
            HttpListenerContext ctx;
            try { ctx = await _oreja.GetContextAsync(); }
            catch { return; }   // se cerró: no es un fallo

            string cuerpo;
            try { cuerpo = Responder(ctx.Request); }
            catch (Exception e) { cuerpo = Json(new { error = e.Message }); }

            var bytes = Encoding.UTF8.GetBytes(cuerpo);
            // CORS abierto porque quien pregunta es una página local abierta con file://, que no
            // tiene origen. Escucha SOLO en 127.0.0.1, así que abierto aquí significa «esta máquina».
            ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
            ctx.Response.Headers["Access-Control-Allow-Headers"] = "content-type";
            ctx.Response.ContentType = "application/json; charset=utf-8";
            try
            {
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            }
            catch { }
        }
    }

    private string Responder(HttpListenerRequest req)
    {
        string ruta = req.Url?.AbsolutePath.TrimEnd('/') ?? "";
        if (req.HttpMethod == "OPTIONS") return "{}";

        // DÓNDE ESTOY Y QUÉ ALCANZO, leído DEL NÚCLEO y no de Neo4j. Es la lectura más fiel que
        // existe: sin proyección de por medio, no hay nada que pueda desviarse.
        if (ruta.EndsWith("/nucleo") || ruta.Length == 0)
        {
            string aqui = _donde();
            return Json(new
            {
                aqui,
                app = Nucleo.Grafo.AppDe(aqui),
                ubicaciones = _grafo.Ubicaciones(),
                alcanzables = _grafo.DesdeAqui(aqui).Select(a => new
                {
                    etiqueta = a.Que.Etiqueta, tipo = a.Que.Tipo,
                    vivo = a.Vivo, destino = a.Destino,
                }),
            });
        }

        // LLÉVAME A X: el núcleo dice cuál es el siguiente paso y el mapeador lo pulsa. UNO.
        if (ruta.EndsWith("/ir"))
        {
            string destino = LeerDestino(req);
            if (destino.Length == 0) return Json(new { ok = false, porque = "falta el destino" });

            string aqui = _donde();

            // PRIMERO LA APP CORRECTA DELANTE. Para pulsar algo hay que tenerlo delante: no hay
            // forma de navegar una aplicación sin enfocarla, y fingir lo contrario sería pulsar a
            // ciegas. Que el foco se mueva aquí no es un descuido, es el trabajo — lo que la capa
            // sin activación evita es que sea EL CLIC EN EL GRAFO el que rompa el hilo.
            string appDestino = Nucleo.Grafo.AppDe(destino);
            if (!Nucleo.Grafo.AppDe(aqui).Equals(appDestino, StringComparison.OrdinalIgnoreCase))
            {
                string proc = appDestino.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
                if (!_enfocar(proc))
                    return Json(new { ok = false, porque = $"no pude traer «{appDestino}» al frente" });
                Thread.Sleep(700);   // que la ventana se asiente antes de leer dónde estamos
                aqui = _donde();
            }

            if (aqui.Equals(destino, StringComparison.OrdinalIgnoreCase))
                return Json(new { ok = true, llegado = true, porque = "ya estás ahí" });

            var paso = _grafo.SiguientePaso(aqui, destino);
            if (paso == null)
                return Json(new
                {
                    ok = false,
                    porque = "el núcleo no sabe llegar desde aquí, o el paso que haría falta no está "
                           + "en pantalla ahora mismo",
                });

            bool pulsado = _pulsar(paso.Que.Selector, paso.Que.Etiqueta);
            LogBus.Log("nucleo-http", pulsado
                ? $"paso hacia «{Corto(destino)}»: pulsado «{paso.Que.Etiqueta}»"
                : $"paso hacia «{Corto(destino)}»: NO pude pulsar «{paso.Que.Etiqueta}»");

            return Json(new
            {
                ok = pulsado,
                paso = paso.Que.Etiqueta,
                selector = paso.Que.Selector,
                llegado = false,
                porque = pulsado ? "" : "el mapeador no consiguió pulsarlo",
            });
        }

        // LAS REGLAS: el veredicto de la última vez que el contrato juzgó al núcleo. Se sirve el
        // archivo tal cual, sin interpretarlo — describirlas aquí crearía una segunda versión de
        // las reglas que envejecería en silencio, que es exactamente lo que este proyecto persigue.
        if (ruta.EndsWith("/reglas"))
        {
            try
            {
                string repo = Navigation.NucleoVersiones.Repo();
                string f = Path.Combine(repo, "nucleo", "visor", "reglas.json");
                if (File.Exists(f)) return File.ReadAllText(f, Encoding.UTF8);
                return Json(new { error = "el contrato todavía no ha dejado su veredicto", donde = f });
            }
            catch (Exception e) { return Json(new { error = e.Message }); }
        }

        return Json(new { error = "no conozco esa ruta", rutas = new[] { "/nucleo", "/ir", "/reglas" } });
    }

    private static string LeerDestino(HttpListenerRequest req)
    {
        try
        {
            if (req.HttpMethod == "GET") return req.QueryString["destino"] ?? "";
            using var r = new StreamReader(req.InputStream, Encoding.UTF8);
            using var doc = JsonDocument.Parse(r.ReadToEnd());
            return doc.RootElement.TryGetProperty("destino", out var d) ? d.GetString() ?? "" : "";
        }
        catch { return ""; }
    }

    private static string Corto(string id)
    {
        int i = id.LastIndexOf('/');
        return i > 0 ? id[(i + 1)..] : id;
    }

    private static string Json(object o) =>
        JsonSerializer.Serialize(o, new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });

    public void Dispose()
    {
        try { _oreja?.Stop(); _oreja?.Close(); } catch { }
    }
}
