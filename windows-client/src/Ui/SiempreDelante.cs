using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using U.WindowsClient.Ui.Jev;

namespace U.WindowsClient.Ui;

/// <summary>
/// QUE UNA CAPA DE Ü NO SE QUEDE DEBAJO. Vuelve a poner las ventanas del grupo en lo más alto cada pocos
/// segundos, en un orden fijo, porque <c>Topmost="True"</c> no es una promesa: es una posición en una lista.
/// </summary>
/// <remarks>
/// POR QUÉ NO BASTA CON <c>Topmost</c> (2026-09-14, lo vio el dueño: «la barra flotante a veces
/// queda detrás de las apps»). Windows mantiene UNA banda de ventanas topmost, ordenada entre sí
/// como cualquier otra: la última que se pone arriba tapa a las demás. Basta con que otra
/// aplicación pida lo mismo —un instalador, una notificación, un reproductor, una ventana de UAC, o
/// un juego a pantalla completa que fuerza el reordenado al salir— para que la nuestra baje un
/// puesto y no vuelva a subir sola. No es un fallo raro: sobre el escritorio de alguien que trabaja
/// pasa varias veces al día.
///
/// SE REAFIRMA CON <c>SetWindowPos</c> Y NO CON <c>Topmost = false; Topmost = true</c>, que es el
/// truco que se encuentra primero. Ese par destruye y recrea el estado de la ventana por dentro y
/// parpadea; y sobre una ventana <c>AllowsTransparency</c> el parpadeo se ve. <c>SWP_NOACTIVATE</c>
/// es la mitad importante de la llamada: estas capas están para MIRARSE, y robarle el foco a la
/// aplicación que alguien está usando —cada tres segundos, para siempre— sería mucho peor que
/// quedarse debajo.
///
/// CADA 3 s Y NO EN UN GANCHO DE EVENTOS: no hay un aviso de «te han adelantado». Habría que
/// enganchar el shell entero, y un sondeo de una llamada por ventana cada tres segundos cuesta menos
/// que eso y no puede colgar nada.
///
/// UN SOLO RELOJ PARA TODAS (promesa 378, spec 049, 2026-09-23). Hasta aquí cada ventana llevaba el
/// suyo, y el orden entre las nuestras lo decidía cuál tocaba la última: con el muelle y el notch daba
/// igual porque casi nunca se pisan, pero el overlay, el panel y la flecha de Jev sí se pisan, y el
/// panel quedaría debajo de sus propias cajas un rato sí y otro no. Ahora las ventanas entran al grupo
/// con su capa y <see cref="VigilanteEnOrden"/> las sube de abajo arriba en el orden de
/// <see cref="OrdenEnZ.Capas"/>. El reloj por ventana se BORRÓ, no se dejó al lado: mientras exista,
/// cualquiera puede volver a llamarlo y habría dos relojes subiendo capas cada uno a su aire (la 378
/// cuenta los relojes de este archivo).
/// </remarks>
public static class SiempreDelante
{
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr h, IntPtr tras, int x, int y, int cx, int cy, uint flags);

    private static VigilanteEnOrden? _vigilante;

    /// <summary>
    /// Mete la ventana en el grupo, en su capa, mientras viva: el reloj la sube con las demás en orden, se reordena el
    /// grupo en cuanto ella se muestra, y sale del grupo al cerrarse. Se llama en el hilo de la interfaz.
    /// </summary>
    public static void EntraAlGrupo(Window ventana, Capa capa)
    {
        ArgumentNullException.ThrowIfNull(ventana);
        var vigilante = ElVigilante();

        // EL HANDLE SE GUARDA AL REGISTRARLA y no se vuelve a pedir: al cerrarse, la ventana ya no tiene HWND y
        // preguntarlo entonces daría 0, así que el Sale no encontraría a quién sacar.
        var ayudante = new WindowInteropHelper(ventana);
        IntPtr handle = IntPtr.Zero;
        void Registrar()
        {
            handle = ayudante.Handle;
            vigilante.Registra(handle, capa, () => ventana.IsVisible);
        }

        // Si todavía no tiene HWND —el muelle y el notch entran desde su constructor—, se registra cuando lo tenga.
        // No se fuerza con EnsureHandle: eso dispararía su SourceInitialized antes de que su constructor termine de
        // colgarle lo que tenga que hacer ahí.
        if (ayudante.Handle != IntPtr.Zero) Registrar();
        else ventana.SourceInitialized += (_, __) => Registrar();

        ventana.IsVisibleChanged += (_, e) => { if (e.NewValue is true) vigilante.AlMostrar(handle); };
        ventana.Closed += (_, __) => vigilante.Sale(handle);
    }

    private static VigilanteEnOrden ElVigilante()
    {
        if (_vigilante != null) return _vigilante;
        // El reloj, uno para todo el grupo y para toda la vida de la app: el muelle y el notch viven lo que ella.
        // Arrancado, lo sujeta el despachador; no hace falta guardarlo.
        var vigilante = new VigilanteEnOrden(Subir);
        var reloj = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(3) };
        reloj.Tick += (_, __) => vigilante.Tick();
        reloj.Start();
        return _vigilante = vigilante;
    }

    /// <summary>
    /// Una ventana, arriba de la banda topmost. Si Windows dice que no, se lanza con su código: el vigilante lo lleva
    /// al log con la capa y sigue con las demás. Antes el <c>bool</c> se tiraba y el fallo no dejaba rastro.
    /// </summary>
    private static void Subir(IntPtr h)
    {
        if (!SetWindowPos(h, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }
}
