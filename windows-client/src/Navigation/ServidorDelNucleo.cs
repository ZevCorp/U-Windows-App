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
    private readonly Func<string, string, bool> _pulsar;    // (selector, etiqueta) → ¿se pulsó?
    private readonly Func<string, bool> _enfocar;           // (id de superficie) → ¿está delante?
    private readonly Func<string, string, bool> _escribir;  // (selector, texto) → ¿se escribió?
    private readonly Func<string, string, bool> _elegir;    // (selector, opción) → ¿se eligió?
    private readonly PasoDelNucleo _paso;
    private readonly int _puerto;
    private HttpListener? _oreja;

    /// <param name="puerto">El de siempre, <see cref="Puerto"/>; otro solo para que el contrato lo arranque de
    /// verdad sin pisar la app viva (promesa 401).</param>
    public ServidorDelNucleo(Nucleo.Grafo grafo, Func<string> donde,
        Func<string, string, bool> pulsar, Func<string, bool> enfocar,
        Func<string, string, bool>? escribir = null, Func<string, string, bool>? elegir = null,
        int puerto = Puerto)
    {
        _grafo = grafo;
        _donde = donde;
        _pulsar = pulsar;
        _enfocar = enfocar;
        _escribir = escribir ?? ((_, _) => false);
        _elegir = elegir ?? ((_, _) => false);
        _paso = new PasoDelNucleo(grafo, donde, pulsar, enfocar);
        _puerto = puerto;
    }

    /// <summary>El rastro de los batches (promesa 76), puesto por quien los corre. Nulo = sin pestaña.</summary>
    public RastroDeBatches? Rastro { get; set; }

public bool Arrancar()
    {
        try
        {
            _oreja = new HttpListener();
            _oreja.Prefixes.Add($"http://127.0.0.1:{_puerto}/");
            _oreja.Start();
            _ = Task.Run(Atender);
            LogBus.Log("nucleo-http", $"escuchando en http://127.0.0.1:{_puerto}/");
            return true;
        }
        catch (Exception e)
        {
            LogBus.Log("nucleo-http", $"no pude escuchar en {_puerto}: {e.Message}");
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

            // LA PUERTA, ANTES DE LEER NADA (promesa 401). Hasta el 2026-09-24 aquí se contestaba con
            // CORS abierto —«quien pregunta es una página abierta con file://, y escucha SOLO en
            // 127.0.0.1, así que abierto significa esta máquina»—, y el razonamiento tenía un hueco: el
            // navegador del médico TAMBIÉN es esta máquina. Cualquier página podía leer /batches, que
            // citaba lo escrito en SAP, y mandar POST a /escribir e /ir. Ya no hay cabeceras CORS: el
            // visor se sirve desde /visor, que es este mismo origen y no las necesita.
            var puerta = PuertaLocal.Admite(ctx.Request.Headers["Origin"], ctx.Request.Headers["Host"], _puerto);
            string cuerpo, tipo;
            if (!puerta.Pasa)
            {
                LogBus.Log("nucleo-http", $"rechazada {ctx.Request.HttpMethod} {ctx.Request.Url?.AbsolutePath}: {puerta.Porque}");
                ctx.Response.StatusCode = 403;
                (cuerpo, tipo) = (Json(new { error = "este servidor solo atiende a esta máquina y a su propia página", porque = puerta.Porque }), Json_);
            }
            else
            {
                try { (cuerpo, tipo) = Responder(ctx.Request); }
                catch (Exception e) { (cuerpo, tipo) = (Json(new { error = e.Message }), Json_); }
            }

            var bytes = Encoding.UTF8.GetBytes(cuerpo);
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
        // EL TERRENO POR DELANTE PARA EL VISOR (T2, promesa 75): el mismo árbol que map_ahead
        // cuenta al modelo, en datos. Vivo solo en la pantalla actual; lo demás es memoria y va
        // punteado. ?niveles=1..3 (por defecto 2), ?desde= para mirar desde otra ubicación.
        if (ruta.EndsWith("/terreno"))
        {
            string desdeQ = req.QueryString["desde"] ?? _donde();
            int niveles = int.TryParse(req.QueryString["niveles"], out int n) ? n : 2;
            return Json(TerrenoParaElVisor.Arbol(_grafo, desdeQ, niveles));
        }

        // EL RASTRO DE LOS BATCHES (promesa 76): lo que cada tanda contestó, lo último primero.
        if (ruta.EndsWith("/batches"))
            return Json(new
            {
                corridas = (Rastro?.Ultimas() ?? new List<RastroDeBatches.Corrida>())
                    .Select(c => new { cuando = c.Cuando.ToString("HH:mm:ss"), cuenta = c.Cuenta }),
            });

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
            if (destino.Length == 0) return NoPude("", "falta el destino");

            // EL PASO LO DA `PasoDelNucleo`, que es la ÚNICA implementación de «ir a» y la que usan
            // también el visor y la voz. Aquí estaba la copia buena y en el núcleo viejo la mala; el
            // modelo de voz usaba la mala y no podía llegar a una página web aunque el panel de al
            // lado sí supiera (2026-08-16). Una pregunta se contesta en un sitio.
            var r = _paso.Hacia(destino);
            if (!r.Ok) return NoPude(destino, r.Porque, r.Paso.Length > 0 ? r.Paso : null);

            if (r.Paso.Length > 0)
                LogBus.Log("nucleo-http", $"paso hacia «{Corto(destino)}»: pulsado «{r.Paso}»");
            return Json(new
            {
                ok = true,
                paso = r.Paso,
                selector = r.Selector,
                llegado = r.Llegado,
                porque = r.Llegado && r.Paso.Length == 0 ? "ya estás ahí" : "",
            });
        }

        // ESCRIBIR Y ELEGIR. Navegar no basta para trabajar: un formulario se rellena, y hasta hoy
        // el núcleo solo sabía pulsar. Las manos son las MISMAS que ya pulsan —`UiaSurface` sabe
        // `input` y `select` desde antes que nosotros—; lo que se añade aquí es la puerta, con la
        // misma disciplina de siempre.
        //
        // Y LA DISCIPLINA ES LA QUE IMPORTA: el elemento se busca POR ETIQUETA entre lo que el
        // núcleo dice que está VIVO en la pantalla de ahora, y si la etiqueta nombra a varias cosas
        // NO se acciona. Escribir a ciegas en «el segundo campo de texto» es exactamente la clase de
        // suposición que llena formularios oficiales con datos en la casilla equivocada, y eso no se
        // deshace con un ctrl+Z.
        if (ruta.EndsWith("/escribir") || ruta.EndsWith("/elegir"))
        {
            bool esEscribir = ruta.EndsWith("/escribir");
            var cuerpo = LeerCuerpo(req);
            string etiqueta = Campo(cuerpo, "elemento");
            string dato = Campo(cuerpo, esEscribir ? "texto" : "opcion");
            if (etiqueta.Length == 0) return Json(new { ok = false, porque = "falta «elemento»" });

            string aqui = _donde();
            var (selector, porque) = BuscarVivo(aqui, etiqueta);
            if (selector.Length == 0) return Json(new { ok = false, porque });

            bool hecho = esEscribir ? _escribir(selector, dato) : _elegir(selector, dato);
            // LO ESCRITO, POR SU LONGITUD (spec 051, E19). Esta línea corre en CADA /escribir y /elegir,
            // salga bien o mal; la de la excepción de las manos (E17) ya iba por su forma y esta no —el
            // censo del 2026-09-23 contó la rama rara y se dejó la de siempre—.
            LogBus.Log("nucleo-http", hecho
                ? $"{(esEscribir ? "escrito" : "elegido")} {SinValor.Forma(dato)} en «{etiqueta}»"
                : $"NO pude {(esEscribir ? "escribir" : "elegir")} {SinValor.Forma(dato)} en «{etiqueta}»");
            return Json(new
            {
                ok = hecho, elemento = etiqueta, selector, dato,
                porque = hecho ? "" : "el mapeador no consiguió accionar ese elemento",
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

        return Json(new { error = "no conozco esa ruta",
                          rutas = new[] { "/visor", "/nucleo", "/terreno", "/batches", "/ir", "/escribir", "/elegir", "/reglas", "/mapeador" } });
    }

    /// <summary>
    /// EL ELEMENTO QUE SE VA A TOCAR, buscado por etiqueta entre lo que está VIVO aquí y ahora.
    /// Devuelve su selector, o vacío y el motivo.
    /// </summary>
    /// <remarks>
    /// TRES «NO» DISTINTOS, y los tres importan porque piden cosas distintas de quien pregunta:
    /// que no exista pide mapear; que exista pero no esté en pantalla pide desplegar o esperar; que
    /// la etiqueta nombre a varias cosas pide precisar. Devolver un «no» genérico obligaría a
    /// adivinar cuál de los tres es.
    ///
    /// Y NO SE ACCIONA LO AMBIGUO, nunca. En el escritorio de Windows hay dos cosas llamadas
    /// «Nombre» y elegir a ojo ya nos costó una arista falsa (2026-08-12). Escribiendo el daño es
    /// mayor que navegando: un clic equivocado se ve, un número escrito en la casilla que no es se
    /// envía.
    /// </remarks>
    private (string Selector, string Porque) BuscarVivo(string aqui, string etiqueta)
    {
        var aca = _grafo.DesdeAqui(aqui);
        var conEseNombre = aca
            .Where(a => a.Que.Etiqueta.Equals(etiqueta, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (conEseNombre.Count == 0)
            return ("", $"el núcleo no conoce «{etiqueta}» en «{Corto(aqui)}»: hay que mapear esta "
                      + "pantalla una vez para que sepa que existe");

        var vivos = conEseNombre.Where(a => a.Vivo).ToList();
        if (vivos.Count == 0)
            return ("", $"«{etiqueta}» está en el grafo pero NO en pantalla ahora mismo: es memoria, "
                      + "no una promesa. Despliega el panel, haz scroll, o espera a que cargue");

        if (vivos.Count > 1)
            return ("", $"«{etiqueta}» nombra a {vivos.Count} cosas vivas en esta pantalla: no es una "
                      + "identidad, y accionar a ojo escribiría en la casilla equivocada");

        return (vivos[0].Que.Selector, "");
    }

    /// <summary>
    /// NO PODER IR TAMBIÉN SE ESCRIBE. Antes solo se dejaba rastro cuando se conseguía pulsar, así
    /// que el caso que importa —el fallo— era invisible: buscando por qué un clic en el grafo no
    /// hizo nada, el log no tenía NI UNA línea de esa petición y la conclusión obvia era que la
    /// petición nunca había llegado. Había llegado, y había contestado que no sabía llegar
    /// (2026-08-13; me costó el diagnóstico entero).
    ///
    /// Un registro que solo cuenta los aciertos no es un registro, es un escaparate.
    /// </summary>
    private static string NoPude(string destino, string porque, string? paso = null)
    {
        LogBus.Log("nucleo-http", destino.Length == 0
            ? $"no pude ir: {porque}"
            : $"paso hacia «{Corto(destino)}»: NO — {porque}");
        return Json(new { ok = false, paso, porque });
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

    /// <summary>El cuerpo de la petición, tal cual. Vacío si no se pudo leer.</summary>
    private static string LeerCuerpo(HttpListenerRequest req)
    {
        try
        {
            if (req.HttpMethod == "GET") return "";
            using var r = new StreamReader(req.InputStream, Encoding.UTF8);
            return r.ReadToEnd();
        }
        catch { return ""; }
    }

    /// <summary>Un campo del cuerpo JSON, o de la cadena de consulta si vino por GET.</summary>
    private static string Campo(string cuerpo, string nombre)
    {
        if (cuerpo.Length == 0) return "";
        try
        {
            using var doc = JsonDocument.Parse(cuerpo);
            return doc.RootElement.TryGetProperty(nombre, out var v) ? v.GetString() ?? "" : "";
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
            // camelCase: los records de C# (PascalCase) y el visor (minúsculas) hablan igual.
            // Las rutas viejas no cambian: sus propiedades anónimas ya eran minúsculas.
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

    public void Dispose()
    {
        try { _oreja?.Stop(); _oreja?.Close(); } catch { }
    }
}
