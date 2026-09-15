using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace U.WindowsClient.Ui;

/// <summary>
/// QUE UNA CAPA DE Ü NO SE QUEDE DEBAJO. Vuelve a poner la ventana en lo más alto cada pocos
/// segundos, porque <c>Topmost="True"</c> no es una promesa: es una posición en una lista.
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
/// enganchar el shell entero, y un sondeo de una llamada cada tres segundos cuesta menos que eso y
/// no puede colgar nada.
/// </remarks>
public static class SiempreDelante
{
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr h, IntPtr tras, int x, int y, int cx, int cy, uint flags);

    /// <summary>Ata la ventana a lo más alto mientras viva. El temporizador se va con ella.</summary>
    public static void Vigilar(this Window ventana)
    {
        var reloj = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(3),
        };
        reloj.Tick += (_, __) =>
        {
            // Escondida no se toca: subir una ventana invisible no arregla nada y en cambio puede
            // sacarla del orden en que la dejó quien la escondió.
            if (!ventana.IsVisible) return;
            try
            {
                var h = new WindowInteropHelper(ventana).Handle;
                if (h != IntPtr.Zero)
                    SetWindowPos(h, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
            catch { /* que una capa no se pueda subir no puede tumbar lo que la capa acompaña */ }
        };
        ventana.Closed += (_, __) => reloj.Stop();
        reloj.Start();
    }
}
