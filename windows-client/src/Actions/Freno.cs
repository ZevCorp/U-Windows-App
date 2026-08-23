using System.Runtime.InteropServices;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Actions;

/// <summary>
/// ESC PARA. Mientras Ü está haciendo algo en la pantalla, la tecla Escape lo detiene y te devuelve
/// el control.
/// </summary>
/// <remarks>
/// LO PIDIÓ EL USUARIO VIENDO CÓMO NO PODÍA PARARME: una recolocación del escritorio se quedó en
/// bucle moviendo el cursor entre dos casillas y no había forma de intervenir salvo matar la app
/// (2026-08-16). El bucle era un fallo y está arreglado, pero la ausencia de freno no era un fallo:
/// era que nunca se había puesto. Un asistente que mueve el ratón y coloca cosas TIENE que poder
/// interrumpirse, y no por el fallo de hoy sino porque el ordenador es de quien está delante.
///
/// SE MIRA LA TECLA, NO SE COGE. Con RegisterHotKey —lo que ya usa <see cref="Ui.GlobalHotkeys"/>
/// para sus atajos— Escape dejaría de funcionar en todo el sistema: no se podría cerrar un diálogo
/// ni salir de pantalla completa mientras Ü esté abierta. Un gancho de bajo nivel lo VE pasar y lo
/// deja seguir su camino (devuelve CallNextHookEx siempre), así que aquí Escape no le quita la tecla
/// a nadie: solo se entera.
///
/// EL FRENO ES DE UN SOLO USO Y SE ARMA AL EMPEZAR. <see cref="Empezar"/> borra cualquier petición
/// vieja: un Escape pulsado hace diez minutos, para otra cosa, no puede abortar lo siguiente que se
/// pida. Y quien no esté haciendo nada no se entera de nada.
/// </remarks>
public static class Freno
{
    /// <summary>
    /// SE ENCHUFA SOLO. La puerta de la pantalla (<c>U.Graph.Surfaces.UiaSurface</c>) vive en un
    /// proyecto por debajo de este y no puede ver el freno, así que expone un enganche y lo rellena
    /// quien sí puede: nosotros, aquí, en cuanto alguien toca el freno por primera vez.
    ///
    /// Va en el constructor estático y no en <see cref="Escuchar"/> a propósito: `Escuchar` la llama
    /// la ventana al arrancar, y lo que garantiza esta clase no puede depender de que haya ventana.
    /// El contrato, por ejemplo, no levanta ninguna.
    /// </summary>
    static Freno()
    {
        U.Graph.Surfaces.UiaSurface.HayQueParar = () => Pidieron;
    }

    private static int _pidieron;
    private static int _haciendoAlgo;

    /// <summary>El gancho, guardado en un campo: si se deja al GC, Windows lo tira y Escape deja de verse.</summary>
    private static Gancho? _gancho;
    private static IntPtr _hook = IntPtr.Zero;

    /// <summary>¿El usuario ha pedido parar? Lo consultan los bucles largos entre paso y paso.</summary>
    public static bool Pidieron => Volatile.Read(ref _pidieron) == 1;

    /// <summary>Qué está haciendo Ü ahora mismo, para poder decirlo al pararse.</summary>
    public static string Tarea { get; private set; } = "";

    /// <summary>Avisa de que alguien ha pedido parar. Para que la carita y la voz puedan reaccionar.</summary>
    public static event Action? Pidio;

    /// <summary>
    /// LO QUE Ü DICE AL SOLTARSE. Pararse en silencio se vive igual que colgarse, y son cosas
    /// opuestas: en una te obedeció y en la otra te dejó tirado. La frase existe para que quien
    /// acaba de pulsar Escape sepa CUÁL de las dos pasó (2026-08-22, pedido por el usuario).
    /// </summary>
    public static event Action<string>? Dice;

    /// <summary>La frase, en un solo sitio: la dicen la carita y la voz, y tienen que decir lo mismo.</summary>
    public const string DevuelvoElControl = "Listo, tienes el control de vuelta.";

    /// <summary>Arranca una acción interrumpible. Desarma el freno: lo de antes ya no cuenta.</summary>
    public static void Empezar(string tarea)
    {
        Tarea = tarea;
        Volatile.Write(ref _pidieron, 0);
        Interlocked.Exchange(ref _haciendoAlgo, 1);
    }

    /// <summary>Se acabó (bien o parada). Escape vuelve a ser una tecla cualquiera.</summary>
    public static void Termine()
    {
        Interlocked.Exchange(ref _haciendoAlgo, 0);
        // Y SE SUELTA EL FRENO. Antes solo se apagaba «estoy haciendo algo» y el alto se quedaba
        // pedido hasta el siguiente Empezar. Daba igual mientras el freno fuera una bandera que solo
        // miraban los bucles —fuera de una tarea no hay bucle que mire—, pero al ponerlo en la
        // PUERTA pasó a importar mucho: entre una tarea y la siguiente el teclado y el ratón
        // quedaban muertos, y nada lo decía. Lo encontró la promesa 26 el mismo día que se escribió
        // (2026-08-22): es exactamente el fallo que aparece cuando una garantía cambia de sitio y
        // el resto del código todavía piensa con las reglas de antes.
        Volatile.Write(ref _pidieron, 0);
        Tarea = "";
    }

    /// <summary>
    /// Espera, pero atenta: devuelve true si hay que parar. Sustituye a <c>Thread.Sleep</c> dentro de
    /// cualquier acción larga — dormir de un tirón medio segundo es medio segundo sin poder pararse,
    /// y son justo los ratos en los que el usuario decide que ya ha visto bastante.
    /// </summary>
    public static bool Duerme(int ms)
    {
        const int trozo = 40;
        for (int ido = 0; ido < ms; ido += trozo)
        {
            if (Pidieron) return true;
            Thread.Sleep(Math.Min(trozo, ms - ido));
        }
        return Pidieron;
    }

    /// <summary>Pedir el alto a mano (un botón, una orden hablada): lo mismo que pulsar Escape.</summary>
    public static void Pide(string porque)
    {
        if (Interlocked.CompareExchange(ref _haciendoAlgo, 1, 1) == 0) return;
        if (Interlocked.Exchange(ref _pidieron, 1) == 1) return;
        LogBus.Log("freno", $"alto pedido ({porque}); paro «{Tarea}»");
        try { Pidio?.Invoke(); } catch { }
        try { Dice?.Invoke(DevuelvoElControl); } catch { }
    }

    /// <summary>Empieza a mirar el teclado. Se llama una vez al arrancar la app.</summary>
    public static void Escuchar()
    {
        if (_hook != IntPtr.Zero) return;
        try
        {
            _gancho = Mirar;
            using var proceso = System.Diagnostics.Process.GetCurrentProcess();
            using var modulo = proceso.MainModule!;
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _gancho, GetModuleHandle(modulo.ModuleName), 0);
            LogBus.Log("freno", _hook != IntPtr.Zero ? "Escape detiene lo que esté haciendo" : "no pude escuchar el teclado");
        }
        catch (Exception e) { LogBus.Log("freno", $"no pude instalar el gancho de teclado: {e.Message}"); }
    }

    private static IntPtr Mirar(int codigo, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (codigo >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN)
                && Marshal.ReadInt32(lParam) == VK_ESCAPE)
                Pide("Escape");
        }
        catch { }
        // SIEMPRE se deja pasar: aquí se mira la tecla, no se secuestra.
        return CallNextHookEx(IntPtr.Zero, codigo, wParam, lParam);
    }

    private delegate IntPtr Gancho(int codigo, IntPtr wParam, IntPtr lParam);

    private const int WH_KEYBOARD_LL = 13, VK_ESCAPE = 0x1B;
    private static readonly IntPtr WM_KEYDOWN = new(0x0100), WM_SYSKEYDOWN = new(0x0104);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, Gancho fn, IntPtr modulo, uint hilo);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int codigo, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string nombre);
}
