using System.Diagnostics;
using System.Runtime.InteropServices;
using U.WindowsClient.Diagnostics;
using U.WindowsClient.SystemApi;

namespace U.WindowsClient.Uia;

/// <summary>
/// Alineación CONSCIENTE de superficie: lleva el foco a la app donde nació un workflow antes de
/// ejecutarlo por el sistema subconsciente. Es el primer eslabón del loop consciente→subconsciente:
/// si al ejecutar no estamos en la superficie del workflow, el sistema se alinea (v1: enfocar la app
/// abierta o lanzarla) y confirma con el SurfaceLocator que ya llegó.
///
/// v1 solo cubre superficies UIA (apps de escritorio, origin uia://proceso.exe). SAP u otras no se
/// abren automáticamente: se devuelve false y el player reporta el mismatch como antes.
/// </summary>
public static class AppAligner
{
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    private const int SW_RESTORE = 9;

    /// <summary>
    /// PONERSE DELANTE DE UNA SUPERFICIE, sea del tipo que sea. Devuelve si se consiguió.
    /// </summary>
    /// <remarks>
    /// NO TODA SUPERFICIE ES UN PROCESO, y tratarlas como si lo fueran es un fallo que ya nos ha
    /// mordido por los dos lados. La navegación del núcleo tomaba la «app» del id y la pasaba a
    /// <see cref="FocusOrLaunch"/>:
    ///
    ///   · «web://itsmiracleai.com.co/…» → buscaba un proceso «itsmiracleai.com.co» y, al no
    ///     encontrarlo, INTENTABA LANZAR un programa con ese nombre. El usuario lo vio como «no pude
    ///     traer "itsmiracleai.com.co" al frente» (2026-08-14).
    ///   · «sapgui://QAS/…» → intentaría lanzar «QAS».
    ///
    /// Es el mismo error de identidad que tumbaba la atribución de clics en web: comparar —o usar—
    /// un DOMINIO como si fuera un PROCESO.
    ///
    /// Para lo web NO SE INVENTA NADA NUEVO: <see cref="PestanasAbiertas"/> ya sabe qué navegador
    /// aloja cada dominio y con qué título, precisamente porque el navegador solo publica el título
    /// y «github.com» no aparece en «joseph1356k/Graph». Y activa la pestaña COMPROBANDO POR
    /// CONSECUENCIA: vuelve a leer la barra de direcciones antes de decir que sí.
    ///
    /// Se devuelve false —y no se adivina— cuando el esquema no se conoce. Rendirse diciéndolo es
    /// seguro; abrir un programa al azar en la máquina de alguien no.
    /// </remarks>
    /// <remarks>
    /// LA DECISIÓN vive en <see cref="Mapeador.ComoMePongoDelante"/>, fuera del cliente y pura, con
    /// sus promesas. Aquí solo se EJECUTA lo que allí se decidió: la regla que se equivocaba tres
    /// veces era la decisión, no la ejecución, y allí se puede probar sin abrir una ventana.
    /// </remarks>
    public static bool PonerDelante(string idDeSuperficie)
    {
        var plan = Mapeador.ComoMePongoDelante.De(idDeSuperficie);
        switch (plan.Via)
        {
            case Mapeador.ComoMePongoDelante.Via.PestanaDelNavegador:
                // PRIMERO LA QUE YA ESTÁ, y solo si no está se abre. El orden importa: ir a un sitio
                // y crear OTRA copia del sitio no son la misma acción, y la segunda deja al usuario
                // con dos estados de la misma página y pierde lo que tuviera a medias en la primera.
                if (PestanasAbiertas.IrA(plan.Que)) return true;
                return PestanasAbiertas.Abrir(
                    Mapeador.ComoMePongoDelante.UrlDe(idDeSuperficie, PestanasAbiertas.EsquemaDe(plan.Que)),
                    plan.Que);
            case Mapeador.ComoMePongoDelante.Via.SapGui:
            case Mapeador.ComoMePongoDelante.Via.Proceso:
                return FocusOrLaunch(plan.Que);
            default:
                LogBus.Log("align", $"no sé ponerme delante de «{idDeSuperficie}»: no reconozco ese "
                                  + "tipo de superficie, y prefiero decirlo a abrir algo al azar");
                return false;
        }
    }

    /// <summary>uia://notepad.exe(/loquesea) → "notepad".</summary>
    public static string ProcessFromOrigin(string origin)
    {
        string s = (origin ?? "").Trim();
        int scheme = s.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) s = s[(scheme + 3)..];
        s = s.Split('/')[0];
        if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s[..^4];
        return s.Trim();
    }

    /// <summary>
    /// Enfoca/abre la app del <paramref name="targetOrigin"/> y espera a que <paramref name="currentOrigin"/>
    /// confirme que ya estamos ahí. Idempotente: si ya estamos en el origin, retorna true sin tocar nada.
    /// Firma compatible con el delegate <c>U.Graph.SurfaceAligner</c>.
    /// </summary>
    public static async Task<bool> EnsureAsync(string targetOrigin, Func<string> currentOrigin, CancellationToken ct)
    {
        string current = currentOrigin();
        LogBus.Log("align", $"EnsureAsync target='{targetOrigin}' actual='{current}'");
        if (Matches(current, targetOrigin)) { LogBus.Log("align", "ya alineado (no toco nada)"); return true; }
        if (!targetOrigin.StartsWith("uia://", StringComparison.OrdinalIgnoreCase))
        {
            LogBus.Log("align", $"origin no es uia:// — no sé abrir esta superficie ({targetOrigin})");
            return false;
        }

        string proc = ProcessFromOrigin(targetOrigin);
        if (string.IsNullOrWhiteSpace(proc)) { LogBus.Log("align", "no pude derivar el proceso del origin"); return false; }

        bool focused = FocusOrLaunch(proc);
        LogBus.Log("align", $"proceso='{proc}' · FocusOrLaunch={focused}");
        if (!focused) return false;

        // Esperar a que el foco realmente cambie (lanzar una app tarda; enfocar es rápido).
        for (int i = 0; i < 24 && !ct.IsCancellationRequested; i++)
        {
            await Task.Delay(250, ct);
            if (Matches(currentOrigin(), targetOrigin))
            {
                LogBus.Log("align", $"confirmado tras ~{(i + 1) * 250} ms");
                return true;
            }
        }
        LogBus.Log("align", $"NO confirmó tras 6s · actual='{currentOrigin()}' (esperaba '{targetOrigin}')");
        return Matches(currentOrigin(), targetOrigin);
    }

    private static bool Matches(string? current, string target) =>
        !string.IsNullOrWhiteSpace(current) &&
        string.Equals(current.TrimEnd('/'), target.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Enfoca la app si ya está abierta; si no, la lanza. También lo usa launch_app (LocalMcp).
    ///
    /// EL ESCRITORIO NO ES UNA APP, y tratarlo como tal tuvo consecuencias visibles: un workflow
    /// sellado en <c>uia://desktop</c> hizo que esto intentara «lanzar un programa llamado desktop»
    /// y el shell resolvió… Docker Desktop (2026-07-31). Abrir un programa al azar en la máquina de
    /// alguien es de las cosas más molestas que puede hacer un agente, y encima la alineación
    /// fallaba igual. El escritorio se alcanza con su gesto —Win+D—, que es lo que ya sabe hacer
    /// <see cref="Gestures.ShowDesktop"/>.
    /// </summary>
    public static bool FocusOrLaunch(string proc)
    {
        if (Escritorio.EsProceso(proc)) return Escritorio.Mostrar();

        IntPtr ventana = VentanaDe(proc);
        if (ventana != IntPtr.Zero) return TraerAlFrente(ventana);
        return WindowsSystemApi.LaunchApp(proc);
    }

    /// <summary>
    /// Una ventana de verdad de ese programa: visible, con título y que no sea el escritorio.
    /// </summary>
    /// <remarks>
    /// <c>Process.MainWindowHandle</c> parecía servir y no sirve para el caso que más usamos. En
    /// explorer.exe la ventana «principal» del proceso es el SHELL —el escritorio y la barra de
    /// tareas—, no la carpeta que el usuario está mirando: traerla al frente no hacía nada, así que
    /// «abre el explorador» respondía «no pude traerla al frente» con la carpeta abierta y visible
    /// delante (2026-08-05). Y con varias ventanas abiertas, «la principal» tampoco es una
    /// pregunta con respuesta: hay que elegir.
    ///
    /// Se recorren las ventanas de arriba abajo en el orden Z, así que la primera que valga es la
    /// que el usuario usó más recientemente — que es la que quiere decir cuando dice «el
    /// explorador».
    /// </remarks>
    public static IntPtr VentanaDe(string proc)
    {
        IntPtr elegida = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            // Se compara por la app REAL de la ventana, no por el proceso que la posee: si no, las
            // UWP no se encuentran nunca —su ventana es de ApplicationFrameHost— y «abre la
            // calculadora» acababa lanzando otra copia en vez de traer la que ya estaba.
            if (!ProcesoDe(h).Equals(proc, StringComparison.OrdinalIgnoreCase)) return true;
            if (Escritorio.EsVentana(h)) return true;          // el escritorio no es una ventana de app

            var sb = new System.Text.StringBuilder(300);
            GetWindowText(h, sb, sb.Capacity);
            if (sb.Length == 0) return true;                    // sin título: barra de tareas y demás

            elegida = h;
            return false;                                       // la primera en orden Z: la más reciente
        }, IntPtr.Zero);
        return elegida;
    }

    /// <summary>
    /// La ventana que el usuario está mirando: la de delante, salvo que la de delante sea NUESTRA.
    /// </summary>
    /// <remarks>
    /// Para hablarnos hay que pulsar la carita, y pulsar la carita nos pone en primer plano. Así que
    /// justo cuando alguien pregunta «¿ves la barra de búsqueda?», lo que está delante somos
    /// nosotros. Antes eso se resolvía negándose —«lo que está delante es mi propia interfaz»— y el
    /// asistente lo contaba como que «Claude se puso por medio», que además no era verdad: era
    /// nuestra propia ventana (2026-08-05, visto en una prueba con YouTube, dos veces seguidas).
    ///
    /// Negarse era la respuesta equivocada a un guardia correcto: el observador no debe observarse,
    /// pero la ventana del usuario no ha desaparecido — está JUSTO DEBAJO. Se baja por el orden Z
    /// hasta la primera que no sea nuestra, y esa es la que quiere decir quien pregunta.
    /// </remarks>
    private static IntPtr _ultimaDelUsuario;

    public static IntPtr VentanaDelUsuario()
    {
        IntPtr delante = GetForegroundWindow();
        if (delante != IntPtr.Zero && !Propio.EsVentana(delante))
        {
            _ultimaDelUsuario = delante;
            return delante;
        }

        // LA QUE ESTABA USANDO, no la que resulta que quedó debajo. El orden Z de un instante no
        // sirve para esto: en la prueba con YouTube, debajo de nuestra ventana había otra app
        // cualquiera —Claude— y responder por ella habría sido igual de falso que negarse. Lo que
        // quiere decir «lo que estoy mirando» es la última ventana que la persona activó, y eso
        // hay que recordarlo mientras pasa. Se refresca en cada sondeo del localizador.
        if (_ultimaDelUsuario != IntPtr.Zero && IsWindowVisible(_ultimaDelUsuario))
            return _ultimaDelUsuario;

        IntPtr elegida = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || Propio.EsVentana(h)) return true;
            if (Escritorio.EsVentana(h)) return true;

            var sb = new System.Text.StringBuilder(300);
            GetWindowText(h, sb, sb.Capacity);
            if (sb.Length == 0) return true;

            elegida = h;
            return false;                                       // la primera por debajo de la nuestra
        }, IntPtr.Zero);
        return elegida;
    }

    /// <summary>
    /// De qué aplicación es esta ventana. La única respuesta, para todo el sistema.
    /// </summary>
    /// <remarks>
    /// Preguntarle a Windows de quién es la ventana da la respuesta equivocada para media tienda de
    /// aplicaciones: la Calculadora, Configuración, Fotos, el Correo… son UWP, y sus ventanas
    /// pertenecen a <c>ApplicationFrameHost.exe</c>, un anfitrión común. El proceso que de verdad
    /// dibuja está DENTRO, en una ventana hija de clase <c>Windows.UI.Core.CoreWindow</c>.
    ///
    /// Sin esto, todas las UWP se llaman igual —«ApplicationFrameHost»— y el sistema entero pierde
    /// el hilo: la pantalla se identifica como de otra app, el guardia de ubicación se niega a
    /// actuar, «tráela al frente» dice que no pudo con la ventana delante, y la regla de misma-app
    /// trata dos pantallas de la misma aplicación como si fueran de dos. Se vio entero al pedirle
    /// una suma a la Calculadora: no llegó ni a pulsar el primer botón (2026-08-05).
    /// </remarks>
    public static string ProcesoDe(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "app";
        try
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            using var p = Process.GetProcessById((int)pid);
            string nombre = p.ProcessName;

            if (!nombre.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
                return nombre;

            // Es el anfitrión: el inquilino es la ventana hija que pinta de verdad.
            uint pidReal = 0;
            EnumChildWindows(hwnd, (h, _) =>
            {
                var clase = new System.Text.StringBuilder(200);
                GetClassName(h, clase, clase.Capacity);
                if (!clase.ToString().Equals("Windows.UI.Core.CoreWindow", StringComparison.OrdinalIgnoreCase))
                    return true;
                GetWindowThreadProcessId(h, out uint hijo);
                if (hijo != 0 && hijo != pid) { pidReal = hijo; return false; }
                return true;
            }, IntPtr.Zero);

            if (pidReal != 0)
            {
                using var real = Process.GetProcessById((int)pidReal);
                return real.ProcessName;
            }
            return nombre;
        }
        catch { return "app"; }
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder s, int max);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder s, int max);

    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    /// <summary>
    /// Traer una ventana al frente DE VERDAD, y comprobarlo. La única forma de hacerlo en la app.
    /// </summary>
    /// <remarks>
    /// Windows no deja que un proceso que no está delante le robe el primer plano a otro: la llamada
    /// devuelve éxito y lo único que hace es parpadear su botón en la barra de tareas. La salida
    /// documentada es engancharse a la cola de entrada del hilo que SÍ está delante, y desengancharse
    /// enseguida —compartir cola con otra app más de lo necesario es pedir un bloqueo—.
    ///
    /// Vive AQUÍ y no en quien la necesita porque «traer al frente» se contestaba en cuatro sitios y
    /// solo uno tenía este arreglo: el más nuevo. Los otros tres seguían con la versión que falla en
    /// silencio, y nadie lo habría notado hasta toparse con el caso (2026-08-04). Una pregunta con
    /// varias respuestas no se mantiene: se desincroniza.
    ///
    /// Y se verifica mirando quién está delante DESPUÉS, no lo que devolvió la llamada: aceptado no
    /// es ejecutado.
    /// </remarks>
    public static bool TraerAlFrente(IntPtr h) => U.Graph.Surfaces.UiaSurface.TraerAlFrente(h);

    // Qué es el escritorio y cómo se llega lo sabe Escritorio, para toda la app.
}
