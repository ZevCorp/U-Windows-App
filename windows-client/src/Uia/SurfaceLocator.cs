using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using System.Windows.Threading;
using U.Graph.Surfaces;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Uia;

/// <summary>
/// Dónde está parado el usuario, como URL. Es el "location bar de Windows": un ID jerárquico y
/// legible de la superficie actual, el mismo concepto que la extensión de Chrome usa con la URL
/// (origin + pathname con slashes) para decidir qué workflows aplican.
///
///   - SAP GUI:     <c>sapgui://SID/TCODE/PROGRAMA/DYNPRO</c> — lo produce <see cref="SapGuiSurface"/>,
///                  no este locator: es la MISMA identidad con la que se sellan los pasos al grabar, y
///                  tener dos formas para la misma pantalla rompía la reproducción (ver <c>_sap</c>)
///   - App nativa:  <c>uia://proceso.exe/titulo-de-ventana-normalizado</c>
///   - Navegador:   <c>web://dominio/ruta/subruta</c> (la URL real leída de la barra de direcciones
///                  por UIA, sin query ni fragmento: esos son estado volátil, no ubicación)
///
/// El esquema coincide con el <c>SurfaceIdentity</c> que windows-graph ya sintetiza para grabar
/// workflows (<c>uia://proc/ventana</c>, <c>sapgui://SID/TCODE</c>), así que este ID sirve tal cual
/// como <c>source_url</c>: cargar workflows por superficie y decidir qué exponer por MCP.
///
/// Sondea la ventana en primer plano con un timer barato (hwnd + título vía GetWindowText); solo
/// cuando algo cambió hace el trabajo caro (UIA para la URL del navegador) en un hilo de fondo.
/// </summary>
public sealed class SurfaceLocator : IDisposable
{
    public sealed record SurfaceLocation(string Id, string Origin, string Path)
    {
        /// <summary>La ventana de la que salió esta ubicación, para poder fijarla como ventana de trabajo (promesa 233).</summary>
        public IntPtr Hwnd { get; init; }
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
    private const uint GA_ROOT = 2;
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
    private const uint GW_HWNDNEXT = 2;

    /// <summary>Qué cuenta como navegador lo dice <see cref="PestanasAbiertas.EsNavegador"/>, para toda la app.</summary>
    private static bool EsNavegador(string proc) => PestanasAbiertas.EsNavegador(proc);

    private readonly DispatcherTimer _timer;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private IntPtr _lastHwnd;
    private string _lastTitle = "";
    private bool _computing;

    /// <summary>
    /// La superficie SAP, para que la ubicación de SAP la produzca QUIEN SABE de SAP.
    ///
    /// Antes este locator sintetizaba <c>uia://saplogon.exe/&lt;título&gt;</c> para SAP, mientras la grabación
    /// y el reproductor usaban <c>SapGuiSurface.Identity()</c> → <c>sapgui://QAS/NWP1/SAPLN_WP_FRAMEWORK/0100</c>.
    /// Dos nombres para la misma pantalla, y la compuerta del reproductor compara STRINGS: un paso sellado
    /// con la forma <c>uia://</c> no casa nunca con una superficie <c>sapgui://</c> y se queda esperando para
    /// siempre.
    ///
    /// La forma de SAP es además la correcta: el título depende del idioma, del cliente y del texto de la
    /// ventana, y no distingue dos dynpros con el mismo título — el problema que ya atacó el commit 2d024c1.
    /// </summary>
    private readonly SapGuiSurface _sap = new();

    /// <summary>
    /// LA MISMA superficie SAP que acuña las ubicaciones, para quien necesite leer o accionar la
    /// sesión (el sentido y la mano del despacho entre mundos, promesas 68-69). Compartirla evita
    /// dos resoluciones COM por lectura y —más importante— dos opiniones sobre la misma sesión.
    /// </summary>
    public SapGuiSurface SuperficieSap => _sap;

    public SurfaceLocation? Current { get; private set; }
    public bool Active { get; private set; }
    public event Action<SurfaceLocation>? Changed;

    /// <summary>
    /// La memoria de 400 ms bajo <see cref="DondeEstoy"/> (promesa 369), EN SOMBRA salvo <c>U_DONDE_MEMORIA=si</c>:
    /// calcula siempre y cuenta cuántas respuestas habrían salido de memoria, y cuántas de esas habrían mentido.
    /// </summary>
    private readonly MemoriaDeUbicacion _memoria;

    public SurfaceLocator()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _timer.Tick += (_, __) => Probe();

        // EN SOMBRA POR DEFECTO, y no es prudencia de estilo (spec 048, regla 10): no está medido cuántas de las 25
        // llamadas a DondeEstoy() caen dentro de 400 ms de otra, ni cuántas de esas habrían servido un sitio viejo.
        // Encenderla sin ese número sería una promesa que no compra nada y puede mentir. Lo decide el nivel 4.
        bool encendida = MemoriaDeUbicacion.EncendidaEnElEntorno();
        _memoria = new MemoriaDeUbicacion(cuenta: l => LogBus.Log("donde", l)) { EnSombra = !encendida };
        LogBus.Log("donde", encendida
            ? $"memoria del dónde ENCENDIDA por U_DONDE_MEMORIA: misma ventana y mismo título, menos de {MemoriaDeUbicacion.VigenciaMs} ms y sin accionar → no se vuelve a calcular"
            : $"memoria del dónde en sombra: se calcula siempre y se cuenta cuántas habrían salido de memoria ({MemoriaDeUbicacion.VigenciaMs} ms); U_DONDE_MEMORIA=si la enciende");
    }

    public void Start()
    {
        if (Active) return;
        Active = true;
        _lastHwnd = IntPtr.Zero; // fuerza recomputar ya
        _timer.Start();
        Probe();
    }

    /// <summary>
    /// Para el localizador. SOLO al cerrar la aplicación (ver <see cref="Dispose"/>).
    ///
    /// Pararlo deja CIEGO a todo el sistema, no solo a lo que se ve: <see cref="Current"/> se
    /// congela en el último valor y el evento <see cref="Changed"/> deja de disparar, con lo que el
    /// ancla de ubicación, la comprobación de llegadas, el MCP y el aprendizaje del terreno pasan a
    /// razonar sobre una pantalla que ya no está delante. El botón «ID visible» llamaba aquí para
    /// esconder un badge y apagaba de paso media aplicación (2026-08-04). Si lo que quieres es
    /// dejar de VER el ID, oculta el badge: saber dónde estamos no se negocia.
    ///
    /// Es PRIVADO a propósito, y esa es la garantía: no se arregla con cuidado, se arregla haciendo
    /// que no se pueda. Mientras fue público, un botón de visibilidad lo llamó para esconder un
    /// badge y apagó de paso el ancla de ubicación, la comprobación de llegadas, el MCP y el
    /// aprendizaje del terreno. Ahora ningún código de fuera puede apagar el localizador aunque
    /// quiera: solo <see cref="Dispose"/>, que es el cierre de la aplicación (2026-08-04).
    /// </summary>
    private void Stop()
    {
        Active = false;
        _timer.Stop();
    }

    /// <summary>Chequeo barato en el hilo de UI: solo hwnd + título. Lo caro va a un hilo de fondo.</summary>
    /// <summary>
    /// La superficie AHORA, calculada en el acto en vez de esperar al siguiente tick.
    ///
    /// <see cref="Current"/> es un valor cacheado que se refresca cada 800 ms, y ese retraso es el
    /// suelo de latencia de todo lo que verifica llegadas: una ruta de cinco saltos se pasaba
    /// varios segundos esperando a que el reloj confirmara algo que ya había ocurrido. Para
    /// navegar rápido hay que preguntar, no esperar — la misma lección que con el proceso en
    /// primer plano (2026-08-02).
    ///
    /// No toca <see cref="Current"/> ni dispara <see cref="Changed"/>: es una consulta, no un
    /// latido. Devuelve null si delante hay una ventana nuestra o no se puede resolver.
    /// </summary>
    /// <summary>
    /// DÓNDE ESTOY. La única respuesta a esa pregunta en toda la aplicación.
    ///
    /// Existe porque la respuesta tenía dos mitades —calcular en el acto, o el último valor
    /// confirmado— y cada sitio elegía la suya: siete llamadas escribiendo la misma fórmula a mano,
    /// y una de ellas se había quedado con solo la mitad cacheada durante meses sin que nadie lo
    /// notara (2026-08-04). Cuando una pregunta se contesta en siete sitios, tarde o temprano dos
    /// contestan distinto; y ese día el fallo no parece un fallo, parece que el sistema «a veces se
    /// confunde».
    ///
    /// Se pregunta primero, porque preguntar es inmediato y esperar al siguiente latido cuesta
    /// hasta 800 ms. Se cae a lo último confirmado solo cuando no se puede resolver — que es el
    /// caso de tener nuestra propia ventana delante, donde lo correcto es conservar la app real en
    /// la que estaba el usuario.
    /// </summary>
    public SurfaceLocation? DondeEstoy() => Ahora() ?? Current;

    /// <summary>
    /// El cálculo inmediato. PRIVADO: quien pregunta usa <see cref="DondeEstoy"/>.
    ///
    /// No se arregla con cuidado, se arregla haciendo que no se pueda: mientras esto y
    /// <see cref="Current"/> fueran las dos públicas, elegir mal seguía estando a un descuido.
    /// </summary>
    private SurfaceLocation? Ahora()
    {
        try
        {
            // Y si por lo que sea no estuviera corriendo, se arranca aquí mismo. Segunda red tras
            // hacer Stop() privado: entre las dos, un localizador apagado deja de ser un estado
            // alcanzable. Cuesta comprobar un bool; costaba media aplicación no comprobarlo.
            if (!Active) Start();

            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            IntPtr raiz = GetAncestor(hwnd, GA_ROOT);
            if (raiz != IntPtr.Zero) hwnd = raiz;

            string proc = ProcessName(hwnd);
            if (Propio.EsProceso(proc))
            {
                // CON LA CARITA DELANTE, LA REGLA DE LA VENTANA DE DELANTE (promesa 230), calculada
                // AHORA. Devolver Current aquí es lo que describió una Tienda ya cerrada durante seis
                // segundos (2026-09-14): la persona le hablaba a la carita y la ubicación se congelaba.
                hwnd = LaVentanaDeDelante(hwnd);
                if (hwnd == IntPtr.Zero) return Current;
                proc = ProcessName(hwnd);
            }

            var sb = new StringBuilder(512);
            GetWindowText(hwnd, sb, sb.Capacity);
            string titulo = sb.ToString();
            // LO BARATO DECIDE SI HACE FALTA LO CARO (promesa 369): hwnd + título cuestan dos llamadas Win32; Compute
            // cuesta 12-194 ms (UIA para la URL del navegador o la sección seleccionada, COM para SAP). Con SAP
            // delante se calcula siempre, por la misma razón que Probe no se salta su tick: el dynpro cambia sin
            // que el título se mueva. Identificar(hwnd) NO pasa por aquí: fijar la ventana de trabajo (233) pregunta
            // por UNA ventana concreta y en el acto.
            IntPtr ventana = hwnd;
            return _memoria.Sirve(ventana, titulo, IsSap(proc), () => ConVentana(Compute(ventana, proc, titulo), ventana));
        }
        catch { return Current; }
    }

    /// <summary>La regla única (promesa 230) con los medios de esta capa: «propia» por PID, «se ve» por Win32 y DWM.</summary>
    private static IntPtr LaVentanaDeDelante(IntPtr foco) => U.Graph.Surfaces.VentanaDeDelante.Elegir(foco,
        U.Graph.Surfaces.UiaSurface.SeVe,
        h => U.Graph.Surfaces.UiaSurface.TituloDe(h).Length > 0,
        Propio.EsVentana,
        h => GetWindow(h, GW_HWNDNEXT));

    private static SurfaceLocation? ConVentana(SurfaceLocation? loc, IntPtr hwnd) => loc == null ? null : loc with { Hwnd = hwnd };

    /// <summary>La ubicación de UNA ventana concreta, pedida por su handle: es lo que se fija como ventana de trabajo (promesa 233).</summary>
    public SurfaceLocation? Identificar(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(512);
            GetWindowText(hwnd, sb, sb.Capacity);
            return ConVentana(Compute(hwnd, ProcessName(hwnd), sb.ToString()), hwnd);
        }
        catch { return null; }
    }

    private void Probe()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;

        // Subir SIEMPRE a la ventana de nivel superior. Si algo dejó activada una ventana hija —un
        // panel, una lista—, su título es el nombre del panel y no identifica ninguna pantalla:
        // la superficie se quedaba en «/ventana» hasta que el usuario tocaba otra app (2026-08-01).
        // Quién sea la pantalla no puede depender de qué trozo de ella tiene el foco.
        IntPtr raiz = GetAncestor(hwnd, GA_ROOT);
        if (raiz != IntPtr.Zero) hwnd = raiz;

        // De paso, se apunta cuál es la ventana DEL USUARIO. Este sondeo es lo único que mira el
        // primer plano de forma continua, así que es el sitio natural para recordarlo: cuando
        // alguien pulsa la carita para hablarnos, delante pasamos a estar nosotros y ya no hay
        // forma de deducir qué estaba mirando. Con esto sí (2026-08-05).
        AppAligner.VentanaDelUsuario();

        string proc = ProcessName(hwnd);
        // Nuestras propias ventanas (la carita, el badge, el inspector) no son "una superficie".
        // Antes se conservaba el ID de la app que el usuario estaba usando; desde la promesa 230 se
        // calcula cuál es la ventana de delante por la regla única, para que Current no se congele.
        if (Propio.EsProceso(proc))
        {
            hwnd = LaVentanaDeDelante(hwnd);
            if (hwnd == IntPtr.Zero) return;
            proc = ProcessName(hwnd);
        }

        var sb = new StringBuilder(512);
        GetWindowText(hwnd, sb, sb.Capacity);
        string title = sb.ToString();

        // La compuerta barata (mismo hwnd + mismo título ⇒ nada que hacer) NO VALE PARA SAP: dentro de una
        // transacción el dynpro cambia sin que el título se mueva, así que saltarse el tick dejaba la
        // ubicación congelada justo donde más importa. Con SAP delante se recomputa siempre y es la propia
        // identidad de SAP la que decide si hubo cambio (la comparación por Id de más abajo).
        if (!IsSap(proc) && hwnd == _lastHwnd && title == _lastTitle) return;
        if (_computing) return;
        _lastHwnd = hwnd;
        _lastTitle = title;
        _computing = true;

        Task.Run(() =>
        {
            try
            {
                var loc = ConVentana(Compute(hwnd, proc, title), hwnd);
                _dispatcher.BeginInvoke(new Action(() =>
                {
                    _computing = false;
                    if (loc == null || loc.Id == Current?.Id) return;
                    Current = loc;
                    Changed?.Invoke(loc);
                }));
            }
            catch
            {
                _dispatcher.BeginInvoke(new Action(() => _computing = false));
            }
        });
    }

    /// <summary>
    /// ¿El proceso en primer plano es SAP GUI? Mismo criterio que el resto del cliente
    /// (<c>UiInspector.IsSapForeground</c>, <c>SurfaceDetector</c>): basta el prefijo «sap», que cubre
    /// <c>saplogon</c> y las variantes históricas <c>sapgui</c>/<c>saplgpad</c> sin listar versiones.
    /// </summary>
    private static bool IsSap(string proc) =>
        proc.StartsWith("sap", StringComparison.OrdinalIgnoreCase);

    private SurfaceLocation? Compute(IntPtr hwnd, string proc, string title)
    {
        // SAP responde por sí mismo: sistema + transacción + programa/dynpro, que es la pantalla DE VERDAD
        // y el mismo string que sella la grabación. Si el scripting no está disponible se cae al esquema
        // uia:// de abajo — degradado, pero no inventamos una identidad SAP que nadie más reconocería.
        if (IsSap(proc))
        {
            try
            {
                // UNA PANTALLA EN TRÁNSITO NO SE ACUÑA. Durante el round-trip SAP actualiza la
                // transacción ANTES que el programa, y sondeando cada ~300 ms se llegó a acuñar la
                // QUIMERA «sapgui://QAS/NWP1/SAPLSMTR_NAVIGATION/0100» — una pantalla que no
                // existe. El clic humano sobre «NWP1» quedó explicando un salto partido en dos
                // (real→quimera→real) y la atribución lo rechazó: la quimera no tiene puertas
                // (2026-08-30, cazada a cuatro ojos con el usuario). SAP mismo dice cuándo está
                // en tránsito: Busy. Mientras tanto se sostiene la ubicación anterior, que sigue
                // siendo la última pantalla REAL que hubo.
                if (_sap.IsBusy() && Current != null) return Current;

                var id = _sap.Identity();
                if (id.Origin != SurfaceIdentity.Unknown.Origin && id.Url.Length > 0)
                    return new SurfaceLocation(id.Url, id.Origin, id.Pathname);
            }
            catch { }
        }

        if (EsNavegador(proc))
        {
            var url = TryReadBrowserUrl(hwnd, out bool hayBarra);
            if (url != null)
            {
                // Aquí, y en ningún otro sitio, coinciden a la vez el dominio, el navegador que lo
                // aloja y el título de la página. Sin apuntarlo ahora, luego no hay forma de volver
                // a una pestaña de fondo: el navegador solo publica su TÍTULO, y «github.com» no
                // aparece en «joseph1356k/Graph» (ver PestanasAbiertas).
                PestanasAbiertas.Apunta(url.Host, proc, title, url.Scheme);
                string path = url.AbsolutePath.TrimEnd('/');
                return new SurfaceLocation($"web://{url.Host}{path}", $"web://{url.Host}", path.Length == 0 ? "/" : path);
            }

            // SIN BARRA DE DIRECCIONES NO HAY PANTALLA QUE ACUÑAR.
            //
            // Un navegador abre muchas ventanas que no son páginas: la barra «meet.google.com está
            // compartiendo tu pantalla», el selector de qué compartir, burbujas de permisos, el
            // diálogo de impresión. Ninguna tiene barra de direcciones, así que la URL no se puede
            // leer y hasta hoy caían al esquema uia:// de abajo, que las bautizaba con su TÍTULO.
            // Así nació «uia://chrome.exe/meet-google-com-está-compartiendo-tu-pantalla»: un nodo
            // permanente, a profundidad 2 bajo chrome.exe, que se seguía dibujando dos días después
            // de cerrar la reunión (2026-08-08, observado por el usuario).
            //
            // Es el mismo veto que ya existe abajo para «ventana» y para los nombres de panel, por
            // la misma razón: un nombre que no identifica una pantalla no debe acuñar una.
            //
            // Se distingue NO HAY BARRA de HAY BARRA PERO NO SE PUEDE LEER, y solo se abstiene en el
            // primer caso. La nueva pestaña tiene barra y está vacía: es una pantalla de verdad
            // —sus azulejos son puertas— y sigue cayendo al slug del título como siempre.
            //
            // Abstenerse es devolver null, que para el sondeo significa «no ha cambiado nada»: la
            // ubicación se queda en la página que el usuario tenía delante, que es la verdad.
            if (!hayBarra)
            {
                LogBus.Log("superficie", $"«{proc}» sin barra de direcciones: «{title}» es mobiliario "
                                       + "del navegador, no una pantalla — no se acuña nodo");
                return null;
            }
        }

        // El título vacío no es una identidad: cae en el slug por defecto «ventana», y como TODAS
        // las ventanas sin título del sistema caen en el mismo, distintas pantallas se funden en un
        // solo nodo. Se midió el daño el 2026-07-31: el agente quedó parado en
        // «uia://explorer.exe/ventana», el mapa no conocía ninguna salida desde ahí —porque ese
        // nodo es varios sitios a la vez— y la navegación por grafo no pudo usarse.
        //
        // Antes de rendirse hay dos fuentes mejores, y ambas describen la pantalla de verdad:
        // el nombre que UIA da a la ventana (suele existir cuando GetWindowText aún devuelve vacío,
        // porque el título se rellena tarde al crearse) y, en el explorador, la ruta de la carpeta.
        // ¿El título lleva sufijo de app («Carpeta - Explorador de archivos»)? Si lo lleva, la parte
        // de delante YA identifica la pantalla y no hace falta mirar el contenido. Si no lo lleva
        // —«Configuración» a secas— el título es el nombre de la app y no dice dónde estás.
        string tituloOriginal = title;
        title = SinSufijoDeApp(title);
        bool elTituloIdentifica = !ReferenceEquals(title, tituloOriginal) && title != tituloOriginal;
        string slug = Slug(title);

        // Sin identidad utilizable: título vacío O título de PANEL. Lo segundo entra por aquí y no
        // solo por el camino alternativo — durante una navegación del explorador, el título de la
        // ventana pasa un instante por «Panel de navegación», y ese instante creó un nodo fantasma
        // con 10 aristas apuntándole (2026-07-31). Un nombre que vale para cualquier ventana no
        // identifica ninguna, venga por donde venga.
        if (slug == "ventana" || EsNombreDePanel(title))
        {
            // El alternativo TAMBIÉN se veta: en la corrida del 2026-07-31 el título se rechazó por
            // ser un panel y el alternativo devolvió «Vista de elementos» —otro panel—, que se
            // convirtió en el nodo 'vista-elementos' al que apuntaban las aristas de subcarpeta.
            // Un nombre de panel no identifica una pantalla, venga del título o del alternativo.
            string mejor = NombreAlternativo(hwnd);
            slug = mejor.Length > 0 && !EsNombreDePanel(mejor) ? Slug(mejor) : "ventana";
        }
        // LA SECCIÓN ABIERTA, cuando el título no la distingue. Muchas apps modernas viven en una
        // sola ventana cuyo título NUNCA cambia —Configuración dice «Configuración» estés en
        // Sistema, en Bluetooth o en Cuentas— así que derivar la identidad del título funde todas
        // sus pantallas en un solo nodo: el recorrido pulsa, la pantalla cambia de verdad, y el
        // sistema no ve ninguna transición. Con el explorador no se notaba porque ahí el título ES
        // la carpeta (2026-08-03, mapeando Configuración como segunda app).
        //
        // La señal es la que usa la propia app para decir dónde estás: el elemento SELECCIONADO de
        // su navegación. Solo se añade cuando aporta —si coincide con el título, no dice nada
        // nuevo— para no romper las identidades que ya funcionaban.
        // Solo se mira el contenido cuando el TÍTULO NO IDENTIFICA. Añadirlo siempre que
        // «difiriera del título» era una mala prueba: en el explorador, crear una carpeta la deja
        // seleccionada y la identidad pasaba a «u-prueba-organizar#nueva-carpeta-4», con lo que el
        // ancla rechazaba los 15 pasos siguientes (2026-08-03). Un título con sufijo de app ya trae
        // la pantalla delante; uno que es solo el nombre de la app, no.
        // EL ESCRITORIO NO TIENE SECCIONES: seleccionar un icono no es ir a otro sitio. Sin esta
        // excepción, cada icono del escritorio creaba su propio nodo —«program-manager#docker-desk»,
        // «program-manager#sap-logon-64»…— y, como la traza encadena de dónde venías a dónde estás,
        // el grafo acababa afirmando que para llegar a un icono hay que pasar por el anterior. Es
        // falso y además caro: todos son alcanzables directamente desde el escritorio, así que
        // inventaba pasos de navegación que nadie necesita dar (2026-08-04, visto en pantalla).
        //
        // La regla de fondo: una sección es un SITIO DISTINTO dentro de la misma ventana; una
        // selección es qué hay señalado en el sitio donde ya estás. El escritorio solo tiene lo
        // segundo.
        string seccion = elTituloIdentifica || Escritorio.EsVentana(hwnd) ? "" : SeccionSeleccionada(hwnd);
        if (seccion.Length > 0 && !seccion.Equals(slug, StringComparison.OrdinalIgnoreCase))
            return new SurfaceLocation($"uia://{proc}.exe/{slug}#{seccion}", $"uia://{proc}.exe", $"/{slug}#{seccion}");

        return new SurfaceLocation($"uia://{proc}.exe/{slug}", $"uia://{proc}.exe", $"/{slug}");
    }

    /// <summary>
    /// La URL real del navegador: el primer Edit del árbol (la omnibox en Chrome/Edge/Brave) leído
    /// por ValuePattern. Si lo que hay escrito no parsea como URL (una búsqueda a medias), se ignora.
    ///
    /// Es LA forma de preguntarle su URL a una ventana de navegador en toda la app: la usa este
    /// localizador para saber dónde estás y <see cref="PestanasAbiertas"/> para saber si un dominio
    /// ya está abierto en otra ventana. Dos lectores distintos de la misma barra acabarían midiendo
    /// cosas distintas de la misma pantalla.
    /// </summary>
    internal static Uri? LeerUrlDelNavegador(IntPtr hwnd) => TryReadBrowserUrl(hwnd, out _);

    /// <summary>
    /// La URL que muestra la barra de direcciones, o null si no se puede leer.
    ///
    /// <paramref name="hayBarra"/> separa las dos razones por las que esto devuelve null, que hasta
    /// el 2026-08-08 eran indistinguibles y pedían tratos opuestos: <c>false</c> significa que la
    /// ventana NO TIENE barra —no es una ventana de navegación, es mobiliario del navegador— y
    /// <c>true</c> que la tiene pero su contenido no sirve como URL: vacía (nueva pestaña), con
    /// espacios (texto de búsqueda a medio escribir) o un esquema interno como <c>chrome://newtab</c>.
    ///
    /// Solo se mira si el Edit existe. Que le falte el ValuePattern cuenta como barra presente: es
    /// el corte más estrecho posible, y el único que la evidencia sostiene.
    /// </summary>
    private static Uri? TryReadBrowserUrl(IntPtr hwnd, out bool hayBarra)
    {
        hayBarra = false;
        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            var edit = root?.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            if (edit == null) return null;
            hayBarra = true;
            if (!edit.TryGetCurrentPattern(ValuePattern.Pattern, out var p) || p is not ValuePattern vp) return null;

            string raw = (vp.Current.Value ?? "").Trim();
            if (raw.Length == 0 || raw.Contains(' ')) return null;
            if (!raw.Contains("://")) raw = "https://" + raw;
            return Uri.TryCreate(raw, UriKind.Absolute, out var uri) && uri.Host.Contains('.') ? uri : null;
        }
        catch { return null; }
    }

    /// <summary>Título de ventana → segmento de ruta estable y legible (minúsculas, guiones).</summary>
    /// <summary>
    /// Cómo llamar a una ventana cuyo título llega vacío. Dos intentos, del más fiable al menos:
    ///
    ///   1. El NAME de la ventana según UIA. Lo rellena el proveedor de accesibilidad y suele estar
    ///      cuando <c>GetWindowText</c> todavía devuelve vacío, que es el caso típico —una ventana
    ///      recién creada, o el foco pasando por ella mientras se pinta—.
    ///   2. En el explorador, la RUTA de la barra de direcciones: identifica la carpeta, que es
    ///      justo lo que distingue una ventana del explorador de otra.
    ///
    /// Devuelve "" si ninguna sirve; entonces se conserva el «ventana» de siempre, que al menos no
    /// miente. Nada de esto inventa un nombre: si no hay identidad, no se fabrica.
    /// </summary>
    /// <summary>
    /// Quita el « - Nombre de la app» final del título. Windows lo añade DESPUÉS de pintar la
    /// pantalla, así que durante una navegación el mismo sitio se lee primero como «app-dev-buena»
    /// y un instante después como «app-dev-buena - Explorador de archivos»: dos nodos distintos
    /// para una sola carpeta (2026-07-31). Quitarlo siempre hace que ambas lecturas coincidan.
    ///
    /// Es la convención de títulos de Windows —«documento - Word», «página - Google Chrome»—, así
    /// que vale para cualquier app, no solo el explorador. Solo se corta el ÚLTIMO segmento y solo
    /// si queda algo delante, para no vaciar títulos que empiezan por guion.
    /// </summary>
    private static string SinSufijoDeApp(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return title;
        int corte = title.LastIndexOf(" - ", StringComparison.Ordinal);
        if (corte <= 0) return title;
        string cabeza = title.Substring(0, corte).Trim();
        return cabeza.Length > 0 ? cabeza : title;
    }

    /// <summary>
    /// La sección abierta según la propia app: su elemento de navegación seleccionado.
    ///
    /// Es la respuesta a «¿dónde estoy?» que la app ya le da al usuario resaltando una entrada de
    /// su menú lateral. Cuando el título no distingue las pantallas, esto sí. Se pide a UIA de una
    /// vez —ListItem AND IsSelected— porque esto se consulta en cada sondeo y recorrer el árbol
    /// entero aquí costaría segundos.
    /// </summary>
    private static string SeccionSeleccionada(IntPtr hwnd)
    {
        try
        {
            var raiz = AutomationElement.FromHandle(hwnd);
            if (raiz == null) return "";
            // Puede haber VARIOS elementos seleccionados a la vez —en Configuración conviven la
            // sección del menú y opciones de la página, como el tema «Oscuro»— y coger el primero
            // que aparezca daba una identidad que no cambiaba al navegar: el recorrido pulsaba
            // todas las secciones y no aprendía ninguna (2026-08-03).
            //
            // La navegación es la columna de la IZQUIERDA. Entre los seleccionados se elige el de
            // menor X, que es donde vive el menú en toda app con panel lateral. Es geometría, sí,
            // pero geometría de la ESTRUCTURA, no de un punto concreto: no se pulsa nada con ella.
            var seleccionados = raiz.FindAll(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
                new PropertyCondition(SelectionItemPattern.IsSelectedProperty, true)));

            // SOLO cuenta lo seleccionado en la NAVEGACIÓN, no en el contenido. La diferencia no es
            // un detalle: al crear una carpeta en el explorador, esa carpeta queda seleccionada en
            // la lista, y tomarla por «dónde estás» cambiaba la identidad del nodo con cada
            // selección —«u-prueba-organizar» pasaba a «u-prueba-organizar#nueva-carpeta»— y el
            // ancla de ubicación rechazaba los 23 pasos siguientes (2026-08-03). Es el mismo error
            // que el botón «Subir»: una regla ganada en Configuración aplicada donde no vale.
            //
            // El menú lateral vive en el tercio izquierdo de la ventana; la lista de contenido, a
            // la derecha. Es geometría de la ESTRUCTURA, no de un punto: no se pulsa nada con ella.
            double limite = LimiteDeNavegacion(hwnd);
            AutomationElement? sel = null;
            double masIzquierda = double.MaxValue;
            foreach (AutomationElement el in seleccionados)
            {
                try
                {
                    var info = el.Current;
                    if (info.IsOffscreen) continue;
                    var r = info.BoundingRectangle;
                    if (r.IsEmpty || r.Left > limite || r.Left >= masIzquierda) continue;
                    masIzquierda = r.Left;
                    sel = el;
                }
                catch { }
            }
            string n = sel?.Current.Name?.Trim() ?? "";
            // Nombres larguísimos (un dispositivo con toda su descripción) no identifican una
            // sección: son contenido que resulta estar seleccionado.
            return n.Length is > 0 and <= 40 ? Slug(n) : "";
        }
        catch { return ""; }
    }

    /// <summary>Hasta dónde llega la franja de navegación: el primer tercio de la ventana.</summary>
    private static double LimiteDeNavegacion(IntPtr hwnd)
    {
        try
        {
            if (!GetWindowRect(hwnd, out RECT r)) return double.MaxValue;
            return r.Left + (r.Right - r.Left) / 3.0;
        }
        catch { return double.MaxValue; }
    }

    private static string NombreAlternativo(IntPtr hwnd)
    {
        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            if (root == null) return "";

            string name = (root.Current.Name ?? "").Trim();
            // El nombre de un PANEL no es el de la ventana. Al pasar el foco por el árbol del
            // explorador, UIA devuelve «Panel de navegación» y eso creó un nodo que no es una
            // pantalla —«explorer.exe/panel-de-navegación», con aristas entrando y saliendo
            // (2026-07-31)—. Es el mismo mal que «ventana» con otro disfraz: un nombre que vale
            // para cualquier ventana no identifica ninguna.
            if (name.Length > 0 && !EsNombreDePanel(name)) return name;

            // La barra de direcciones del explorador: su valor es la ruta de la carpeta.
            var barra = root.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            if (barra != null
                && barra.TryGetCurrentPattern(ValuePattern.Pattern, out var p)
                && p is ValuePattern vp)
            {
                string ruta = (vp.Current.Value ?? "").Trim();
                // Solo la última parte: la carpeta es lo que distingue, no la ruta entera —que
                // además haría el id enorme y distinto por cada nivel intermedio.
                if (ruta.Length > 0)
                {
                    string hoja = ruta.TrimEnd('\\', '/').Split('\\', '/').LastOrDefault() ?? "";
                    if (hoja.Length > 0) return hoja;
                }
            }
        }
        catch { }
        return "";
    }

    /// <summary>Nombres de partes de una ventana, no de la ventana. Sirven para cualquiera: no identifican.</summary>
    private static bool EsNombreDePanel(string n)
    {
        string[] partes =
        {
            "panel de navegación", "panel de navegacion", "navigation pane",
            "panel de detalles", "details pane", "barra de", "toolbar", "árbol", "arbol",
            "lista", "vista de elementos", "vista elementos", "items view", "contenido", "content",
            // Aparecido en la corrida del 2026-07-31 como nodo nuevo: es el árbol del panel
            // izquierdo, no una pantalla. Los nombres de panel son una familia, no casos sueltos.
            "control de árbol de espacios de nombres", "control de arbol de espacios de nombres",
            "namespace tree control", "shell folder view", "vista de carpeta",
        };
        string l = n.ToLowerInvariant();
        return partes.Any(p => l.Equals(p, StringComparison.Ordinal) || l.StartsWith(p + " ", StringComparison.Ordinal));
    }

    private static string Slug(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "ventana";
        var sb = new StringBuilder(title.Length);
        bool dash = false;
        foreach (char c in title.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) { sb.Append(c); dash = false; }
            else if (!dash && sb.Length > 0) { sb.Append('-'); dash = true; }
            if (sb.Length >= 60) break;
        }
        string slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? "ventana" : slug;
    }

    // De quién es la ventana lo sabe AppAligner, para toda la app: las UWP viven bajo un anfitrión
    // común y preguntárselo a Windows a secas las llama a todas igual.
    private static string ProcessName(IntPtr hwnd) => AppAligner.ProcesoDe(hwnd);

    public void Dispose()
    {
        Stop();
        _memoria.Dispose();   // lo que quede del último minuto se dice al cerrar
    }
}

/// <summary>
/// DÓNDE ESTOY, UNA VEZ POR INSTANTE (spec 048, promesa 369).
/// </summary>
/// <remarks>
/// MEDIDO (spec 048, 2026-09-22): 25 llamadas a <c>DondeEstoy()</c> sin memoria —24 en <c>FaceWindow</c>, 1 aquí—,
/// cada una de 12 a 194 ms; y ráfagas de «descarté una vuelta» en el mapa vivo tras cada salto. Cuántas caen dentro
/// de 400 ms de otra NO está medido, y por eso esto nace EN SOMBRA: calcula siempre y cuenta.
///
/// La regla: se sirve la última calculada mientras la ventana de delante y su título sean los mismos, tenga menos
/// de <see cref="VigenciaMs"/> y NADIE HAYA ACCIONADO desde que se calculó —ni <see cref="Olvida"/> ni
/// <see cref="Observatorio.Invalida"/>, que es la llamada de Take y Type al volver la mano—. Con SAP delante se
/// calcula siempre. Es la misma idea que <c>DondeTrabajo</c> (promesa 246) con la invalidación que a aquella le
/// faltaba cuando la pregunta se hace desde fuera: la refutación nº5 de la spec mostró que, sin olvidar al accionar,
/// <c>SeguirElFoco</c> olvidaba lo suyo y recogía de aquí lo viejo.
///
/// En sombra cuenta también cuántas de las que habrían salido de memoria traían OTRA respuesta: «habría servido»
/// no dice si lo servido era verdad, y una memoria que miente es peor que ninguna (patrón nº8). Ese número, junto
/// al de cuántas habrían salido, es el que decide en el nivel 4 si se enciende.
///
/// Pura y con reloj inyectable, para que el contrato la juzgue sin pantalla. Segura entre hilos: la preguntan el
/// hilo de la interfaz y el reloj de ubicación del mapa vivo; el cálculo corre FUERA del cerrojo.
/// </remarks>
public sealed class MemoriaDeUbicacion : IDisposable
{
    /// <summary>
    /// Cuánto vale una ubicación calculada. La misma cifra que <c>DondeTrabajo</c> (promesa 246), y no está medida
    /// por este camino: la spec 040 midió que la ubicación cambia 405-510 ms después de un clic que navega.
    /// </summary>
    public const int VigenciaMs = 400;

    private sealed record Recordada(IntPtr Ventana, string Titulo, long Cuando, long Olvidos, long Acciones,
                                    SurfaceLocator.SurfaceLocation Ubicacion);

    /// <summary>Lo contado en un tramo: el minuto en curso o todo desde el arranque.</summary>
    private sealed class Tramo
    {
        public int Calculadas, DeMemoria, EnSombra, ConOtraRespuesta, ConSap;
        public bool Vacio => Calculadas == 0 && DeMemoria == 0;
    }

    private readonly object _cerrojo = new();
    private readonly Func<long> _reloj;
    private Recordada? _recordada;
    private long _olvidos;
    private readonly Tramo _desdeElArranque = new();
    private Tramo _minuto = new();
    private long _inicioDelMinuto;

    /// <param name="reloj">ms monotónicos; null = el de la máquina. El contrato pone el suyo.</param>
    /// <param name="cuenta">Por dónde se dice el resumen por minuto. Entra por el constructor, como en el proyector de
    /// Neo4j (366): lo que se cuenta antes de que alguien escuche no lo oye nadie.</param>
    public MemoriaDeUbicacion(Func<long>? reloj = null, Action<string>? cuenta = null)
    {
        _reloj = reloj ?? (() => Environment.TickCount64);
        Cuenta = cuenta;
    }

    /// <summary>Por dónde se dice el resumen por minuto.</summary>
    public Action<string>? Cuenta { get; set; }

    /// <summary>
    /// EN SOMBRA: cada llamada calcula, y solo se cuenta cuántas habrían salido de memoria. Recién construida está
    /// encendida; el localizador la pone en sombra salvo <c>U_DONDE_MEMORIA=si</c>.
    /// </summary>
    public bool EnSombra { get; set; }

    /// <summary>¿El entorno la enciende? <c>U_DONDE_MEMORIA=si</c> (o sí, 1, true). Vacío es no (patrón nº9).</summary>
    public static bool EncendidaEnElEntorno()
    {
        string v = Environment.GetEnvironmentVariable("U_DONDE_MEMORIA") ?? "";
        return !string.IsNullOrWhiteSpace(v) && v.Trim().ToLowerInvariant() is "si" or "sí" or "1" or "true";
    }

    /// <summary>Cuántas se calcularon desde el arranque (en sombra, todas).</summary>
    public int Calculadas { get { lock (_cerrojo) return _desdeElArranque.Calculadas; } }

    /// <summary>Cuántas se sirvieron de memoria sin volver a calcular (en sombra, ninguna).</summary>
    public int DeMemoria { get { lock (_cerrojo) return _desdeElArranque.DeMemoria; } }

    /// <summary>En sombra: cuántas habrían salido de memoria.</summary>
    public int HabrianSidoDeMemoria { get { lock (_cerrojo) return _desdeElArranque.EnSombra; } }

    /// <summary>En sombra: de las que habrían salido de memoria, cuántas traían otra respuesta al calcularlas.</summary>
    public int HabrianMentido { get { lock (_cerrojo) return _desdeElArranque.ConOtraRespuesta; } }

    /// <summary>
    /// La ubicación para la ventana de delante <paramref name="ventana"/> con título <paramref name="titulo"/>: la
    /// recordada si vale, o la que traiga <paramref name="calcula"/>. Una respuesta nula no se recuerda: no es una
    /// ubicación, es «no se pudo», y quien pregunta cae a lo último confirmado.
    /// </summary>
    public SurfaceLocator.SurfaceLocation? Sirve(IntPtr ventana, string titulo, bool esSap,
                                                 Func<SurfaceLocator.SurfaceLocation?> calcula)
    {
        if (calcula == null) throw new ArgumentNullException(nameof(calcula));
        titulo ??= "";
        long ahora = _reloj();
        // Se toman ANTES de calcular: si se acciona mientras el cálculo está en vuelo, lo calculado nace caducado.
        long acciones = Observatorio.Invalidaciones;
        long olvidos;
        Recordada? valdria;
        bool sombra;
        var decir = new List<string>(1);
        lock (_cerrojo)
        {
            CerrarElMinutoSiToca(ahora, decir);
            olvidos = _olvidos;
            sombra = EnSombra;
            valdria = !esSap
                      && _recordada is { } r
                      && r.Ventana == ventana
                      && string.Equals(r.Titulo, titulo, StringComparison.Ordinal)
                      && ahora - r.Cuando < VigenciaMs
                      && r.Olvidos == olvidos
                      && r.Acciones == acciones
                ? r : null;
            foreach (var t in new[] { _desdeElArranque, _minuto })
            {
                if (valdria != null && !sombra) { t.DeMemoria++; continue; }
                t.Calculadas++;
                if (esSap) t.ConSap++;
                if (valdria != null) t.EnSombra++;
            }
        }
        foreach (var l in decir) Cuenta?.Invoke(l);
        if (valdria != null && !sombra) return valdria.Ubicacion;

        var calculada = calcula();

        if (valdria != null)
        {
            // EN SOMBRA, LO RECORDADO NO SE RENUEVA: la memoria encendida habría servido esto sin calcular, así que
            // su reloj habría seguido corriendo desde el cálculo de antes. Renovarlo contaría de más.
            if (!string.Equals(calculada?.Id, valdria.Ubicacion.Id, StringComparison.Ordinal))
                lock (_cerrojo) { _desdeElArranque.ConOtraRespuesta++; _minuto.ConOtraRespuesta++; }
            return calculada;
        }
        if (!esSap && calculada != null)
            lock (_cerrojo) _recordada = new Recordada(ventana, titulo, ahora, olvidos, acciones, calculada);
        return calculada;
    }

    /// <summary>Acabamos de accionar: lo recordado ya no vale, aunque no hayan pasado los 400 ms.</summary>
    public void Olvida()
    {
        lock (_cerrojo)
        {
            _olvidos++;
            _recordada = null;
        }
    }

    /// <summary>Todo lo contado desde el arranque, con el mismo formato que la línea por minuto.</summary>
    public string Resumen()
    {
        lock (_cerrojo) return $"{Decir(_desdeElArranque)} — desde el arranque, {Modo()}";
    }

    private string Modo() => EnSombra ? "en sombra (U_DONDE_MEMORIA=si la enciende)" : "encendida";

    private static string Decir(Tramo t)
    {
        string s = $"dónde: {t.Calculadas} calculadas · {t.DeMemoria} de memoria · {t.EnSombra} en sombra";
        if (t.EnSombra > 0) s += $" ({t.ConOtraRespuesta} con otra respuesta)";
        if (t.ConSap > 0) s += $" · {t.ConSap} con SAP delante, que se calcula siempre";
        return s;
    }

    /// <summary>
    /// EL RESUMEN POR MINUTO, dicho al llegar la primera pregunta después de cumplirse el minuto y con lo que pasó
    /// antes de ella, diciendo cuánto duró de verdad el tramo (el mismo criterio que el proyector de Neo4j, 366): un
    /// minuto sin preguntas no dice nada, y un «último minuto» que fueron diez sería una cifra que miente.
    /// </summary>
    private void CerrarElMinutoSiToca(long ahora, List<string> decir)
    {
        if (_minuto.Vacio) { _inicioDelMinuto = ahora; return; }
        if (ahora - _inicioDelMinuto < 60_000) return;
        decir.Add($"{Decir(_minuto)} — en los últimos {(ahora - _inicioDelMinuto) / 1000} s, {Modo()}");
        _minuto = new Tramo();
        _inicioDelMinuto = ahora;
    }

    /// <summary>Lo que quede del último minuto se dice al cerrar: las corridas cortas del nivel 4 también cuentan.</summary>
    public void Dispose()
    {
        string? ultimo = null;
        lock (_cerrojo)
            if (!_minuto.Vacio)
                ultimo = $"{Decir(_minuto)} — al cerrar, en los últimos {(_reloj() - _inicioDelMinuto) / 1000} s, {Modo()}";
        if (ultimo != null) Cuenta?.Invoke(ultimo);
    }
}
