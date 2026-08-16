using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;
using U.Graph;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Uia;

/// <summary>
/// ¿Está este dominio YA abierto en el navegador, y dónde? Una sola respuesta, un solo sitio.
///
/// Pulsar el nivel «github.com» abría SIEMPRE una pestaña nueva, aunque GitHub estuviera abierto a
/// dos pestañas de distancia (2026-08-07, observado por el usuario). Ir a un sitio y CREAR otra
/// copia del sitio no son la misma acción: la segunda deja al usuario con dos estados de la misma
/// página y pierde lo que tuviera a medias en la primera.
///
/// LO QUE EL NAVEGADOR DEJA VER, MEDIDO Y NO SUPUESTO (sonda del 2026-08-07 sobre 4 ventanas de
/// Chrome y 41 pestañas):
///   · la barra de direcciones da la URL de UNA pestaña, la activa de cada ventana;
///   · las pestañas de fondo se exponen como TabItem con el TÍTULO de la página por nombre —
///     «joseph1356k/Graph»— y nada más: ni HelpText, ni ItemStatus, ni AutomationId útil. No hay
///     forma de leer la URL de una pestaña que no está activa sin activarla.
/// De ahí la estrategia en dos pasos: primero lo que se sabe seguro (URLs de las activas), y solo
/// después el título, que es lo único que queda. Y para que el título sirva hay que RECORDAR cuál
/// vimos en cada dominio, porque «github.com» no aparece en «joseph1356k/Graph».
///
/// La memoria se llena sola desde <see cref="SurfaceLocator"/>: cada vez que identifica una
/// superficie web ya tiene delante el dominio, el proceso y el título. Es terreno —se puede borrar
/// sin perder nada aprendido— y por eso vive en su propio archivo y no en el mapa.
/// </summary>
public static class PestanasAbiertas
{
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern int GetWindowTextLength(IntPtr h);
    private delegate bool EnumProc(IntPtr h, IntPtr l);

    /// <summary>
    /// Los navegadores, en UN sitio. Estaban escritos tres veces —el locator, las herramientas del
    /// mapa y aquí— y tres listas de lo mismo se desincronizan en silencio: añadir «arc» en una y
    /// no en las otras da una app que se identifica como web pero a la que nadie sabe volver.
    /// </summary>
    public static bool EsNavegador(string proc)
    {
        string p = (proc ?? "").Trim();
        if (p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) p = p[..^4];
        return Navegadores.Contains(p);
    }

    private static readonly HashSet<string> Navegadores = new(StringComparer.OrdinalIgnoreCase)
        { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc", "chromium" };

    // ── Memoria: qué títulos y qué navegador vimos en cada dominio ───────────

    private sealed class Rastro
    {
        public string Proc { get; set; } = "";
        public List<string> Titulos { get; set; } = new();

        /// <summary>Con qué esquema se vio este dominio. Suponer «https» rompe los sitios que solo
        /// hablan http —un portal cautivo, un equipo de la red local— y el fallo sería mudo: el
        /// navegador abre, no carga, y nadie dice por qué.</summary>
        public string Esquema { get; set; } = "";
    }

    private static readonly Dictionary<string, Rastro> _rastros = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _candado = new();
    private static bool _cargado;

    private static string Archivo => System.IO.Path.Combine(UserPaths.Local, "U", "titulos-web.json");

    /// <summary>Cuántos títulos se guardan por dominio: los últimos, que son los que siguen abiertos.</summary>
    private const int MaxTitulos = 12;

    /// <summary>
    /// Apuntar que este dominio se está viendo con este título. Lo llama el localizador, que es
    /// quien tiene delante las tres cosas a la vez.
    /// </summary>
    public static void Apunta(string dominio, string proc, string tituloVentana, string esquema = "")
    {
        if (string.IsNullOrWhiteSpace(dominio)) return;
        string t = TituloDePagina(tituloVentana);
        lock (_candado)
        {
            Cargar();
            if (!_rastros.TryGetValue(dominio, out var r)) _rastros[dominio] = r = new Rastro();
            if (proc.Length > 0) r.Proc = proc.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? proc : proc + ".exe";
            if (esquema.Length > 0) r.Esquema = esquema;
            if (t.Length >= 4)
            {
                r.Titulos.RemoveAll(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase));
                r.Titulos.Add(t);
                while (r.Titulos.Count > MaxTitulos) r.Titulos.RemoveAt(0);
            }
            Guardar();
        }
    }

    /// <summary>Qué navegador aloja este dominio, para dibujarlo como el subnivel que es. Vacío si no consta.</summary>
    public static string NavegadorDe(string dominio)
    {
        lock (_candado)
        {
            Cargar();
            return _rastros.TryGetValue(dominio ?? "", out var r) ? r.Proc : "";
        }
    }

    /// <summary>
    /// ¿ESTE NOMBRE ES UN SITIO QUE YA CONOCEMOS? «github» → «github.com». Vacío si no, o si suena
    /// a varios.
    /// </summary>
    /// <remarks>
    /// Se pidió «abre github» y se intentó arrancar un ejecutable llamado «github»: seis segundos
    /// para acabar en «no pude abrir github», y Ü se lo contó al usuario como si GitHub no se
    /// pudiera abrir. Era mentira: un minuto antes su pestaña se había traído al frente en un
    /// segundo (2026-08-16, en el log).
    ///
    /// SOLO SE CONTESTA SI ES INEQUÍVOCO, y solo entre los dominios POR LOS QUE YA SE PASÓ: no se
    /// inventa «github.com» a partir de una cadena, se reconoce lo que está en la memoria. Con dos
    /// candidatos —«google.com» y «docs.google.com» ante «google»— se devuelve vacío: elegir a ojo
    /// entre dos sitios es la misma forma de fallo que acuñar una arista adivinando cuál de dos
    /// puertas con el mismo nombre era.
    /// </remarks>
    public static string DominioQueSuena(string nombre)
    {
        string n = (nombre ?? "").Trim().TrimEnd('/').ToLowerInvariant();
        if (n.Length < 3) return "";

        lock (_candado)
        {
            Cargar();
            var claves = _rastros.Keys.ToList();

            // Tal cual, o con el punto puesto: «github.com» y «github» son la misma petición.
            var exacto = claves.Where(d => d.Equals(n, StringComparison.OrdinalIgnoreCase)).ToList();
            if (exacto.Count == 1) return exacto[0];

            // Por la primera etiqueta del dominio: «github» reconoce «github.com», y «google» NO
            // reconoce nada porque suena igual a «google.com» y a «docs.google.com».
            var porEtiqueta = claves.Where(d =>
                d.Split('.').FirstOrDefault()?.Equals(n, StringComparison.OrdinalIgnoreCase) == true).ToList();
            return porEtiqueta.Count == 1 ? porEtiqueta[0] : "";
        }
    }

    /// <summary>Con qué esquema se vio este dominio. Vacío si no consta.</summary>
    public static string EsquemaDe(string dominio)
    {
        lock (_candado)
        {
            Cargar();
            return _rastros.TryGetValue(dominio ?? "", out var r) ? r.Esquema : "";
        }
    }

    /// <summary>
    /// El título de la PÁGINA a partir del de la ventana: «X - Google Chrome» → «X».
    ///
    /// Hace falta porque lo que se compara al final es el nombre de un TabItem, que es el título
    /// pelado. Se corta por el final y solo si lo que sobra nombra a un navegador: hay páginas cuyo
    /// título lleva guiones («Graphify - Knowledge Graphs»), y cortar por el primero las destroza.
    /// </summary>
    public static string TituloDePagina(string tituloVentana)
    {
        string t = (tituloVentana ?? "").Trim();

        int corte = Math.Max(t.LastIndexOf(" - ", StringComparison.Ordinal),
                             t.LastIndexOf(" — ", StringComparison.Ordinal));
        if (corte > 0 && ColaDeNavegador(t[(corte + 3)..])) t = t[..corte].TrimEnd();

        // Edge cuelga «and 2 more pages» / «y 2 páginas más» y detrás el nombre del PERFIL, que es
        // texto libre y no hay forma de reconocer. Se corta por la marca, que sí es fija, y se va
        // con ella todo lo que viniera después.
        var marca = System.Text.RegularExpressions.Regex.Match(t,
            @"\s+(and\s+\d+\s+more\s+page|y\s+\d+\s+p[áa]gina)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (marca.Success && marca.Index > 0) t = t[..marca.Index].TrimEnd();

        return t;
    }

    /// <summary>
    /// ¿Este final de título es el nombre del navegador?
    ///
    /// Se compara por PALABRAS ENTERAS y no por contenido, y la razón salió de una medición: la
    /// página «Graphify - Knowledge Graphs for AI Coding Assistants» quedaba reducida a «Graphify»
    /// porque «Knowledge» contiene «edge» (2026-08-07). Buscar una marca dentro de una palabra
    /// encuentra marcas que nadie puso.
    /// </summary>
    private static bool ColaDeNavegador(string cola)
    {
        // El espacio de ancho cero que Edge mete dentro de su propio nombre se va al quedarnos con
        // las letras de cada palabra.
        return cola.Split(new[] { ' ', '\t', ' ', '​', '‎' },
                          StringSplitOptions.RemoveEmptyEntries)
            .Select(p => new string(p.Where(char.IsLetter).ToArray()))
            .Any(p => p.Length > 0
                   && (Navegadores.Contains(p) || p.Equals("edge", StringComparison.OrdinalIgnoreCase)));
    }

    private static void Cargar()
    {
        if (_cargado) return;
        _cargado = true;
        try
        {
            if (!File.Exists(Archivo)) return;
            var leido = JsonSerializer.Deserialize<Dictionary<string, Rastro>>(File.ReadAllText(Archivo));
            if (leido == null) return;
            foreach (var (k, v) in leido) _rastros[k] = v;
        }
        catch (Exception e) { LogBus.Log("pestañas", $"no se pudo leer la memoria de títulos: {e.Message}"); }
    }

    private static void Guardar()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Archivo)!);
            File.WriteAllText(Archivo, JsonSerializer.Serialize(_rastros,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) { LogBus.Log("pestañas", $"no se pudo guardar la memoria de títulos: {e.Message}"); }
    }

    // ── Ir a lo que ya está abierto ─────────────────────────────────────────

    /// <summary>
    /// Llevar el foco al dominio si ya está abierto. Devuelve false si no lo está —y entonces quien
    /// llama SÍ debe abrirlo: no encontrarlo es una respuesta legítima, no un fallo.
    ///
    /// Se comprueba POR CONSECUENCIA: seleccionar una pestaña es una petición, y una petición
    /// aceptada no es una pestaña activada. Después de pedirlo se vuelve a leer la barra de
    /// direcciones, y solo si el dominio coincide se dice que sí.
    /// </summary>
    public static bool IrA(string dominio)
    {
        if (string.IsNullOrWhiteSpace(dominio)) return false;
        var ventanas = VentanasDeNavegador();
        if (ventanas.Count == 0) return false;

        // 1. LO QUE SE SABE SEGURO: la pestaña activa de cada ventana dice su URL.
        foreach (var h in ventanas)
        {
            var url = SurfaceLocator.LeerUrlDelNavegador(h);
            if (url != null && MismoSitio(url.Host, dominio))
            {
                bool ok = AppAligner.TraerAlFrente(h);
                LogBus.Log("pestañas", $"«{dominio}» ya estaba activo en una ventana → {(ok ? "al frente" : "NO se pudo enfocar")}");
                return ok;
            }
        }

        // 2. LO QUE QUEDA: el título. Solo sirve si recordamos haber visto este dominio.
        List<string> titulos;
        lock (_candado)
        {
            Cargar();
            titulos = _rastros.TryGetValue(dominio, out var r) ? new List<string>(r.Titulos) : new List<string>();
        }
        if (titulos.Count == 0)
        {
            LogBus.Log("pestañas", $"«{dominio}» no está en ninguna pestaña activa y no recuerdo ningún título suyo");
            return false;
        }

        foreach (var h in ventanas)
        {
            var pestanas = PestanasDe(h);
            if (pestanas.Count == 0) continue;
            var activaAntes = pestanas.FirstOrDefault(EstaSeleccionada);
            bool toqueAlgo = false;

            foreach (var pestana in pestanas)
            {
                string nombre = pestana.Current.Name ?? "";
                if (nombre.Length == 0) continue;
                // Los últimos títulos primero: es más probable que siga abierto lo último visto.
                string? casa = titulos.AsEnumerable().Reverse().FirstOrDefault(t => Casan(nombre, t));
                if (casa == null || !Seleccionar(pestana, nombre)) continue;
                toqueAlgo = true;
                AppAligner.TraerAlFrente(h);

                if (ConfirmaDominio(h, dominio))
                {
                    LogBus.Log("pestañas", $"«{dominio}» estaba en la pestaña «{nombre}» (por el título «{casa}») → activada");
                    return true;
                }
                LogBus.Log("pestañas", $"la pestaña «{nombre}» parecía de «{dominio}» por el título, pero al activarla la URL era otra");
            }

            // NO ESTABA AQUÍ: SE DEVUELVE LA VENTANA COMO ESTABA. Buscar no es reordenar la mesa de
            // nadie: si el título llevó a una pestaña equivocada, dejar al usuario plantado en ella
            // es un daño causado por MI búsqueda, no por su clic.
            if (toqueAlgo && activaAntes != null && Seleccionar(activaAntes, activaAntes.Current.Name ?? ""))
                LogBus.Log("pestañas", "no estaba en esta ventana: devuelta a la pestaña que tenía");
        }

        LogBus.Log("pestañas", $"«{dominio}» no está abierto en ninguna de las {ventanas.Count} ventana(s) de navegador");
        return false;
    }

    /// <summary>
    /// ABRIRLA, cuando no está abierta. Devuelve si se llegó, comprobado releyendo la dirección.
    /// </summary>
    /// <remarks>
    /// <see cref="IrA"/> devuelve false cuando el sitio no está en ninguna pestaña, y su contrato ya
    /// decía que entonces «quien llama SÍ debe abrirlo». Nadie lo hacía: hacer clic en un nodo web
    /// del mapa funcionaba solo si la pestaña ya estaba abierta y, si no, no pasaba nada. Media
    /// función es peor que ninguna, porque parece que funciona (2026-08-16, lo vio el usuario).
    ///
    /// SE ABRE EN EL NAVEGADOR DONDE SE VIO, no en el predeterminado del sistema: ahí es donde está
    /// la sesión iniciada, y llegar a un sitio pidiendo login otra vez no es llegar.
    ///
    /// Y SE COMPRUEBA POR CONSECUENCIA, como todo lo demás aquí: lanzar es una petición, no una
    /// llegada. Se espera a que la barra de direcciones diga el dominio; si no lo dice, se devuelve
    /// false y quien preguntó se entera, en vez de creer que ya está donde no está.
    /// </remarks>
    public static bool Abrir(string url, string dominio)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(dominio)) return false;

        string navegador = NavegadorDe(dominio);
        try
        {
            if (navegador.Length > 0)
                Process.Start(new ProcessStartInfo(navegador, url) { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            LogBus.Log("pestañas", $"«{dominio}» no estaba abierto: abriendo {url}"
                                 + (navegador.Length > 0 ? $" en {navegador}" : " en el navegador del sistema"));
        }
        catch (Exception e)
        {
            LogBus.Log("pestañas", $"no pude abrir {url}: {e.Message}");
            return false;
        }

        // Abrir una pestaña y cargar una página tardan lo suyo, y más si el navegador no estaba
        // arrancado. Se espera hasta 12 s mirando TODAS las ventanas: la pestaña nueva puede caer en
        // cualquiera de ellas, o en una recién creada.
        for (int intento = 0; intento < 40; intento++)
        {
            System.Threading.Thread.Sleep(300);
            foreach (var h in VentanasDeNavegador())
            {
                var ahora = SurfaceLocator.LeerUrlDelNavegador(h);
                if (ahora == null || !MismoSitio(ahora.Host, dominio)) continue;
                bool alFrente = AppAligner.TraerAlFrente(h);
                LogBus.Log("pestañas", $"«{dominio}» abierto y cargado tras ~{(intento + 1) * 300} ms"
                                     + (alFrente ? "" : " (pero no pude traer la ventana al frente)"));
                return alFrente;
            }
        }
        LogBus.Log("pestañas", $"abrí {url} pero a los 12 s la barra de direcciones seguía sin decir «{dominio}»");
        return false;
    }

    /// <summary>
    /// ¿El nombre de esta pestaña y un título recordado hablan de la misma página?
    ///
    /// Se mira en LOS DOS SENTIDOS porque los dos textos se recortan por sitios distintos: Chrome
    /// antepone «Uso de memoria de …: 109 MB» al nombre de la pestaña —ahí el título recordado va
    /// dentro—, y Edge añade al título de la VENTANA «and 2 more pages - Personal» —ahí el que va
    /// dentro es el nombre de la pestaña—. Exigir contención en un solo sentido fallaba en uno de
    /// los dos navegadores, según cuál se mirara.
    /// </summary>
    private static bool Casan(string nombrePestana, string titulo)
    {
        if (titulo.Length >= 4 && nombrePestana.Contains(titulo, StringComparison.OrdinalIgnoreCase)) return true;
        // Al revés hace falta más letra: un nombre corto metido en un título largo es casualidad.
        return nombrePestana.Length >= 8 && titulo.Contains(nombrePestana, StringComparison.OrdinalIgnoreCase);
    }

    private static bool EstaSeleccionada(AutomationElement e)
    {
        try
        {
            return e.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var p)
                && ((SelectionItemPattern)p).Current.IsSelected;
        }
        catch { return false; }
    }

    private static bool Seleccionar(AutomationElement pestana, string nombre)
    {
        try
        {
            if (!pestana.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pat)) return false;
            ((SelectionItemPattern)pat).Select();
            return true;
        }
        catch (Exception e)
        {
            LogBus.Log("pestañas", $"no se pudo seleccionar la pestaña «{nombre}»: {e.Message}");
            return false;
        }
    }

    /// <summary>Aceptado no es hecho: se pregunta a la barra de direcciones dónde está de verdad.</summary>
    private static bool ConfirmaDominio(IntPtr hwnd, string dominio)
    {
        for (int intento = 0; intento < 6; intento++)
        {
            System.Threading.Thread.Sleep(120);
            var ahora = SurfaceLocator.LeerUrlDelNavegador(hwnd);
            if (ahora != null && MismoSitio(ahora.Host, dominio)) return true;
        }
        return false;
    }

    /// <summary>
    /// ¿Son el mismo sitio? «github.com» y «www.github.com» lo son; «api.github.com» también cuenta
    /// como estar en GitHub, que es lo que pregunta quien pulsa el nivel.
    /// </summary>
    private static bool MismoSitio(string host, string dominio)
    {
        string a = host.TrimStart().TrimEnd('/');
        string b = dominio.TrimStart().TrimEnd('/');
        return a.Equals(b, StringComparison.OrdinalIgnoreCase)
            || a.EndsWith("." + b, StringComparison.OrdinalIgnoreCase)
            || b.EndsWith("." + a, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Las ventanas de navegador visibles y CON título: las de app y los popups no tienen pestañas.</summary>
    private static List<IntPtr> VentanasDeNavegador()
    {
        var r = new List<IntPtr>();
        try
        {
            EnumWindows((h, _) =>
            {
                if (!IsWindowVisible(h) || GetWindowTextLength(h) == 0) return true;
                GetWindowThreadProcessId(h, out uint pid);
                try
                {
                    using var p = Process.GetProcessById((int)pid);
                    if (EsNavegador(p.ProcessName)) r.Add(h);
                }
                catch { }
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception e) { LogBus.Log("pestañas", $"al enumerar ventanas de navegador: {e.Message}"); }
        return r;
    }

    private static List<AutomationElement> PestanasDe(IntPtr hwnd)
    {
        var r = new List<AutomationElement>();
        try
        {
            var raiz = AutomationElement.FromHandle(hwnd);
            if (raiz == null) return r;
            var todas = raiz.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem));
            foreach (AutomationElement e in todas) r.Add(e);
        }
        catch (Exception e) { LogBus.Log("pestañas", $"al leer las pestañas: {e.Message}"); }
        return r;
    }
}
