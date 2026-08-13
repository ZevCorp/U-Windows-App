using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using U.Graph;
using Mapeador;
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

            string cuerpo, tipo;
            try { (cuerpo, tipo) = Responder(ctx.Request); }
            catch (Exception e) { (cuerpo, tipo) = (Json(new { error = e.Message }), Json_); }

            var bytes = Encoding.UTF8.GetBytes(cuerpo);
            // CORS abierto porque quien pregunta es una página local abierta con file://, que no
            // tiene origen. Escucha SOLO en 127.0.0.1, así que abierto aquí significa «esta máquina».
            ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
            ctx.Response.Headers["Access-Control-Allow-Headers"] = "content-type";
            ctx.Response.ContentType = tipo;
            try
            {
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            }
            catch { }
        }
    }

    private const string Json_ = "application/json; charset=utf-8";

    /// <summary>
    /// EL VISOR SE SIRVE DESDE AQUÍ. Antes había que abrirlo con `file://` y saberse la ruta del
    /// repo de memoria; ahora basta con la dirección que ya se usa para todo lo demás. No es
    /// comodidad: una herramienta que cuesta abrir se deja de abrir, y este visor existe justo para
    /// mirar lo que si no se mira acaba en arqueología del log.
    ///
    /// Se lee del repo en cada petición, sin caché, para que editar el visor y recargar baste.
    /// </summary>
    private (string Cuerpo, string Tipo) Responder(HttpListenerRequest req)
    {
        if ((req.Url?.AbsolutePath.TrimEnd('/') ?? "").EndsWith("/visor"))
        {
            try
            {
                string f = Path.Combine(Navigation.NucleoVersiones.Repo(), "nucleo", "visor", "index.html");
                if (File.Exists(f)) return (File.ReadAllText(f, Encoding.UTF8), "text/html; charset=utf-8");
                return (Json(new { error = "no encuentro el visor", donde = f }), Json_);
            }
            catch (Exception e) { return (Json(new { error = e.Message }), Json_); }
        }
        return (Contestar(req), Json_);
    }

    private string Contestar(HttpListenerRequest req)
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
            if (!pulsado)
            {
                LogBus.Log("nucleo-http", $"paso hacia «{Corto(destino)}»: NO pude pulsar «{paso.Que.Etiqueta}»");
                return Json(new { ok = false, paso = paso.Que.Etiqueta, porque = "el mapeador no consiguió pulsarlo" });
            }

            // ¿NOS MOVIÓ? Un paso que no mueve no se repite. Sin esto, un camino equivocado en el
            // grafo —«pulsa Datos adjuntos para ir a Escritorio», cuando ya estás en Datos
            // adjuntos— hacía que el mismo clic se calculara y se pulsara una y otra vez: doce
            // veces seguidas hasta agotar el límite, sin avanzar un paso (2026-08-12, lo midió el
            // usuario pidiendo ir a «facturas»).
            //
            // Repetir algo que acaba de no funcionar no es insistir, es no estar mirando. Y decirlo
            // en voz alta importa el doble aquí, porque el motivo casi siempre es que el grafo
            // aprendió mal ese tramo — quedarse callado esconde justo el dato que lo delata.
            string despues = aqui;
            for (int i = 0; i < 12 && despues.Equals(aqui, StringComparison.OrdinalIgnoreCase); i++)
            {
                Thread.Sleep(150);
                despues = _donde();
            }

            if (despues.Equals(aqui, StringComparison.OrdinalIgnoreCase))
            {
                LogBus.Log("nucleo-http", $"paso hacia «{Corto(destino)}»: pulsé «{paso.Que.Etiqueta}» "
                    + "y la pantalla NO cambió — ese tramo del grafo no lleva a donde dice");
                return Json(new
                {
                    ok = false,
                    paso = paso.Que.Etiqueta,
                    porque = $"pulsé «{paso.Que.Etiqueta}» y no nos movió. El grafo cree que ese tramo "
                           + "lleva a otro sitio, y no es cierto: hay que volver a recorrerlo para corregirlo",
                });
            }

            LogBus.Log("nucleo-http", $"paso hacia «{Corto(destino)}»: pulsado «{paso.Que.Etiqueta}» → {Corto(despues)}");
            return Json(new
            {
                ok = true,
                paso = paso.Que.Etiqueta,
                selector = paso.Que.Selector,
                llegado = despues.Equals(destino, StringComparison.OrdinalIgnoreCase),
                porque = "",
            });
        }

        // LAS REGLAS: el veredicto de la última vez que el contrato juzgó al núcleo. Se sirve el
        // archivo tal cual, sin interpretarlo — describirlas aquí crearía una segunda versión de
        // las reglas que envejecería en silencio, que es exactamente lo que este proyecto persigue.
        //
        // DOS CONTRATOS, DOS ARCHIVOS, UNA SOLA PESTAÑA. El del núcleo dice qué se sabe; el del
        // mapeador, cómo se sabe. Contestan la misma pregunta —«¿qué está garantizado?»— y por eso
        // se miran juntos: partirlas en dos paneles obliga a acordarse de abrir los dos, y el que
        // se olvida es siempre el que está en rojo.
        if (ruta.EndsWith("/reglas") || ruta.EndsWith("/reglas-mapeador"))
        {
            try
            {
                string cual = ruta.EndsWith("/reglas-mapeador") ? "reglas-mapeador.json" : "reglas.json";
                string f = Path.Combine(Navigation.NucleoVersiones.Repo(), "nucleo", "visor", cual);
                if (File.Exists(f)) return File.ReadAllText(f, Encoding.UTF8);
                return Json(new { error = "ese contrato todavía no ha dejado su veredicto", donde = f });
            }
            catch (Exception e) { return Json(new { error = e.Message }); }
        }

        // EL PULSO DEL MAPEADOR. Se sirven los contadores que él mismo lleva, sin tocarlos: ni una
        // media, ni un porcentaje, ni un veredicto. Todo eso lo calcula quien los lleva o no se
        // calcula — si esta ruta dedujera algo, tendríamos dos opiniones sobre el mismo hecho, que
        // es lo que llevamos la semana entera pagando.
        if (ruta.EndsWith("/mapeador"))
        {
            var p = PulsoDelMapeador.Actual;
            return Json(new
            {
                desde = p.Desde.ToString("yyyy-MM-dd HH:mm:ss"),
                saturacion = new { ubicacion = p.DescartadasUbicacion, pantalla = p.DescartadasPantalla },
                atribuciones = new
                {
                    saltos = p.Saltos,
                    aprendidas = p.Aprendidas,
                    rechazos = p.Rechazos.OrderByDescending(x => x.Value)
                        .Select(x => new { motivo = x.Key, veces = x.Value }),
                    // EL DESGLOSE POR APP, que es lo que decide si los fallos de mapeo son propios
                    // de cada aplicación o los mismos en todas. En el total agregado esas dos cosas
                    // se ven idénticas, y de esa respuesta depende si hay que modularizar.
                    porApp = p.Apps.OrderByDescending(x => x.Value.Saltos).Select(x => new
                    {
                        app = x.Key,
                        saltos = x.Value.Saltos,
                        aprendidas = x.Value.Aprendidas,
                        // Aparte del porcentaje: rechazar un alt-tab es acertar, no fallar.
                        noEranNavegacion = x.Value.NoEranNavegacion,
                        // Y el fallo que no cuelga de ningún salto, así que no se ve en los rechazos.
                        sinCambiarDeSitio = x.Value.CambioLaPantallaYNoElSitio,
                        rechazos = x.Value.Rechazos.OrderByDescending(r => r.Value)
                            .Select(r => new { motivo = r.Key, veces = r.Value }),
                    }),
                },
                costes = p.Tiempos.OrderByDescending(x => x.Value.TotalMs)
                    .Select(x => new
                    {
                        que = x.Key, veces = x.Value.Veces,
                        mediaMs = x.Value.Veces == 0 ? 0 : x.Value.TotalMs / x.Value.Veces,
                        peorMs = x.Value.PeorMs,
                        // APARTE, porque la media no sobrevive a un colgado: 22 ms reales se leían
                        // como 1.461 de media por UNA muestra de veintiún minutos.
                        colgadas = p.Colgadas.TryGetValue(x.Key, out int c) ? c : 0,
                    }),
                embudo = new
                {
                    leidos = p.Leidos,
                    entregados = p.Entregados,
                    filtrados = p.Filtrados.OrderByDescending(x => x.Value)
                        .Select(x => new { motivo = x.Key, veces = x.Value }),
                },
            });
        }

        return Json(new { error = "no conozco esa ruta", rutas = new[] { "/visor", "/nucleo", "/ir", "/reglas", "/mapeador" } });
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
