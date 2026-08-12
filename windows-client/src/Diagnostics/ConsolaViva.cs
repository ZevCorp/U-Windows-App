using System.IO;
using System.Runtime.InteropServices;

namespace U.WindowsClient.Diagnostics;

/// <summary>
/// Una consola de verdad, adosada a la app, para ver un proceso largo MIENTRAS ocurre.
///
/// Nace por el arquitecto: corre varios minutos razonando y llamando herramientas, y su salida solo
/// llegaba al log. Para saber si estaba pensando, atascado o muerto había que abrir el visor y
/// refrescar — y un agente que no se ve por ningún sitio se parece demasiado a uno colgado
/// (2026-08-08, pedido por el usuario).
///
/// NO SUSTITUYE AL LOG, lo acompaña. El log es la evidencia que se lee después; esto es la ventana
/// que se mira durante. Escribir en los dos cuesta nada y cada uno sirve para lo que sirve.
///
/// Una app WPF no tiene consola: se pide una con <c>AllocConsole</c>. Solo se puede tener UNA por
/// proceso, así que <see cref="Abrir"/> es idempotente y lo que hace en las siguientes llamadas es
/// cambiarle el título y separar la corrida con una línea.
/// </summary>
public static class ConsolaViva
{
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AllocConsole();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GetConsoleWindow();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool SetConsoleTitle(string t);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    private const int SW_SHOW = 5, SW_RESTORE = 9;

    private static readonly object _lock = new();
    private static bool _rota;   // si el SO no da consola, no insistir en cada línea

    /// <summary>
    /// Abrir la consola —o traerla al frente si ya estaba— y titularla con lo que se va a ver.
    /// Silenciosa si falla: quedarse sin ventana de diagnóstico no puede tumbar lo que se diagnostica.
    /// </summary>
    public static void Abrir(string titulo)
    {
        if (_rota) return;
        lock (_lock)
        {
            try
            {
                var h = GetConsoleWindow();
                if (h == IntPtr.Zero)
                {
                    if (!AllocConsole()) { _rota = true; return; }
                    h = GetConsoleWindow();
                    // La salida estándar de un proceso WPF apunta a la nada hasta que hay consola.
                    var w = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
                    Console.SetOut(w);
                    Console.OutputEncoding = System.Text.Encoding.UTF8;
                }
                else
                {
                    // Ya había una de una corrida anterior: se separa para que no se lean pegadas.
                    Console.WriteLine();
                    Console.WriteLine(new string('─', 70));
                }
                SetConsoleTitle(titulo);
                SinSeleccionRapida();
                if (h != IntPtr.Zero) { NoRobarElFoco(h); ShowWindow(h, SW_RESTORE); ShowWindow(h, SW_SHOW); }
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {titulo}");
            }
            catch { _rota = true; }
        }
    }

    /// <summary>
    /// Mover la consola a una posición de pantalla. La usa el explorador cuando se lleva el grafo
    /// al otro monitor: mirar el razonamiento del agente y mirar el grafo son la MISMA tarea, y
    /// dejarlos en pantallas distintas obligaría a girar la cabeza en cada paso (2026-08-08).
    ///
    /// Silenciosa si no hay consola: mover algo que no existe no es un error, es un no-op.
    /// </summary>
    public static void MoverA(int x, int y, int ancho, int alto)
    {
        try
        {
            var h = GetConsoleWindow();
            if (h == IntPtr.Zero) return;
            SetWindowPos(h, IntPtr.Zero, x, y, ancho, alto, SWP_NOZORDER | SWP_NOACTIVATE);
        }
        catch { }
    }

    /// <summary>
    /// QUE MIRARLA NO CUESTE EL FOCO. La consola es una ventana normal, así que pulsarla —para
    /// subir, para leer— se lo quitaba a la app que el arquitecto está navegando, y eso no es un
    /// detalle: el agente actúa sobre LA VENTANA DE DELANTE, así que un clic tuyo en la consola
    /// podía cambiarle el terreno bajo los pies en mitad de una auditoría.
    ///
    /// Es el mismo WS_EX_NOACTIVATE que ya usa la carita flotante, y por la misma razón: hay
    /// ventanas que están para MIRARSE, no para trabajar en ellas (2026-08-10, pedido por el
    /// usuario). Se puede seguir leyendo y haciendo scroll con el ratón encima; lo que ya no pasa
    /// es que pulsarla se lleve el foco.
    /// </summary>
    private static void NoRobarElFoco(IntPtr h)
    {
        try
        {
            if (h == IntPtr.Zero) return;
            int estilo = GetWindowLong(h, GWL_EXSTYLE);
            SetWindowLong(h, GWL_EXSTYLE, estilo | WS_EX_NOACTIVATE);
        }
        catch { }
    }

    /// <summary>
    /// QUITAR LA SELECCIÓN RÁPIDA (QuickEdit), que viene puesta de fábrica en toda consola de
    /// Windows. Con ella, pinchar o arrastrar dentro de la ventana la pone en modo selección y
    /// BLOQUEA cualquier escritura hasta que se pulse Enter o Esc.
    ///
    /// Eso no es una molestia visual: <see cref="Escribir"/> se llama desde el manejador que drena
    /// la salida del arquitecto, así que un clic tuyo aquí bloquea el manejador, la tubería se
    /// llena, y node se queda parado intentando escribir. EL AGENTE SE CONGELA. Medido el
    /// 2026-08-11: siete minutos sin una línea y después veinte líneas con el MISMO segundo —todas
    /// soltadas de golpe al desbloquear—, y el usuario ya había notado que «tenía que oprimir enter
    /// para que actualizara».
    ///
    /// Es el mismo problema que WS_EX_NOACTIVATE vino a resolver —una ventana que está para
    /// MIRARSE no puede alterar lo que muestra— y le faltaba esta mitad: no robar el foco no basta
    /// si además puede parar al que observa. Seleccionar con el ratón se pierde; el menú del
    /// sistema (clic derecho → Editar → Marcar) sigue estando para copiar.
    /// </summary>
    private static void SinSeleccionRapida()
    {
        try
        {
            var entrada = GetStdHandle(STD_INPUT_HANDLE);
            if (entrada == IntPtr.Zero || entrada == new IntPtr(-1)) return;
            if (!GetConsoleMode(entrada, out uint modo)) return;
            // EXTENDED_FLAGS hay que ponerlo SIEMPRE al tocar QUICK_EDIT: sin él, Windows ignora
            // el cambio en silencio y todo parece hecho sin estarlo.
            SetConsoleMode(entrada, (modo & ~ENABLE_QUICK_EDIT_MODE) | ENABLE_EXTENDED_FLAGS);
        }
        catch { }
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GetStdHandle(int n);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetConsoleMode(IntPtr h, out uint modo);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetConsoleMode(IntPtr h, uint modo);
    private const int STD_INPUT_HANDLE = -10;
    private const uint ENABLE_QUICK_EDIT_MODE = 0x0040, ENABLE_EXTENDED_FLAGS = 0x0080;

    [DllImport("user32.dll", SetLastError = true)] private static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowLong(IntPtr h, int i, int v);
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    private const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

    /// <summary>
    /// En qué pantalla está la consola del agente, o null si no hay consola. El grafo la SIGUE:
    /// mirar su razonamiento y mirar el grafo son la misma tarea, así que quien decide dónde vive
    /// esa pareja es la consola, y el grafo va detrás sin que haya que moverlo aparte.
    /// </summary>
    public static string? PantallaDeLaConsola()
    {
        try
        {
            var h = GetConsoleWindow();
            if (h == IntPtr.Zero) return null;
            return System.Windows.Forms.Screen.FromHandle(h).DeviceName;
        }
        catch { return null; }
    }

    /// <summary>
    /// Una línea, con su hora. No hace nada si nunca se abrió la consola.
    /// </summary>
    /// <remarks>
    /// NUNCA BLOQUEA A QUIEN LLAMA, y esa es toda la razón de que haya una cola. Esto se invoca
    /// desde el manejador que drena la salida del arquitecto: si escribir en la consola se para
    /// —selección rápida, un scroll, la ventana ocupada—, el manejador se para con ella, la tubería
    /// se llena y el agente se congela al escribir. Quitar QuickEdit tapa la causa conocida
    /// (ver <see cref="SinSeleccionRapida"/>); esto quita la POSIBILIDAD: la consola es una ventana
    /// de diagnóstico, y una ventana de diagnóstico no puede detener lo que diagnostica.
    ///
    /// La hora se sella AQUÍ, al ocurrir, y no en el hilo que escribe: si se sellara al pintar,
    /// un atasco haría que veinte líneas figuraran con el mismo segundo y perderíamos justo la
    /// prueba de que hubo atasco (2026-08-11, que es como se encontró este fallo).
    /// </remarks>
    public static void Escribir(string linea)
    {
        if (_rota) return;
        if (GetConsoleWindow() == IntPtr.Zero) return;
        _cola.Add($"[{DateTime.Now:HH:mm:ss}] {linea}");
    }

    private static readonly System.Collections.Concurrent.BlockingCollection<string> _cola = new();

    // Un solo hilo escribiendo, de fondo y en segundo plano: si la app se cierra con líneas por
    // pintar, se van con ella —son diagnóstico, no datos— y no impiden salir.
    private static readonly Task _pintor = Task.Factory.StartNew(() =>
    {
        foreach (var l in _cola.GetConsumingEnumerable())
        {
            try { lock (_lock) Console.WriteLine(l); }
            catch { _rota = true; return; }
        }
    }, TaskCreationOptions.LongRunning);
}
