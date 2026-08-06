using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace U.WindowsClient.Ui;

/// <summary>
/// Lanzar la carita con dos dedos en el trackpad, hacia donde sea.
/// </summary>
/// <remarks>
/// Arrastrarla exige agarrarla y soltarla; con el trackpad el gesto natural es empujarla, y empujar
/// no es agarrar. Es el mismo gesto con el que se tira una tarjeta por la mesa.
///
/// WPF solo cuenta la rueda VERTICAL (<c>MouseWheel</c>). La horizontal existe —los trackpads de
/// precisión la mandan desde hace años— pero llega en un mensaje que WPF no traduce, así que hay que
/// escucharlo a mano. Sin eso, «hacia cualquier lugar» serían solo dos de las cuatro direcciones.
///
/// UN EMPUJÓN NO ES UN MENSAJE, SON VEINTE. El trackpad manda un goteo de deltas mientras los dedos
/// se mueven, y tratar cada uno como un lanzamiento daría veinte lanzamientos. Se suman mientras el
/// goteo dure y se lanza cuando para: lo que se mide entonces es el gesto entero, que es lo que la
/// persona hizo.
/// </remarks>
internal sealed class LanzarConScroll
{
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_MOUSEHWHEEL = 0x020E;

    /// <summary>
    /// De «muescas» de rueda a velocidad en DIP/s. Está calibrado para que un empujón normal de
    /// trackpad pase del umbral de lanzamiento de <see cref="EdgeSnap"/> sin tener que barrer el
    /// trackpad entero, y para que un roce accidental NO llegue.
    /// </summary>
    private const double PorMuesca = 900.0 / 120.0 * 2.2;

    /// <summary>Cuánto silencio cierra el gesto. Menos y un empujón largo se parte en dos.</summary>
    private static readonly TimeSpan Pausa = TimeSpan.FromMilliseconds(120);

    private readonly Window _win;
    private readonly Func<bool> _activo;
    private readonly Action<double, double> _lanzar;
    private readonly System.Windows.Threading.DispatcherTimer _fin;

    private double _dx, _dy;
    private HwndSource? _fuente;

    /// <param name="activo">Si ahora mismo el gesto cuenta. Colapsada sí; con la barra abierta no,
    /// que ahí el scroll es para el menú.</param>
    /// <param name="lanzar">Velocidad del gesto en DIP/s (x, y), lista para <see cref="EdgeSnap"/>.</param>
    public LanzarConScroll(Window win, Func<bool> activo, Action<double, double> lanzar)
    {
        _win = win;
        _activo = activo;
        _lanzar = lanzar;
        _fin = new System.Windows.Threading.DispatcherTimer { Interval = Pausa };
        _fin.Tick += (_, __) => Soltar();

        if (PresentationSource.FromVisual(win) is HwndSource ya) Enganchar(ya);
        else win.SourceInitialized += (_, __) =>
        {
            if (PresentationSource.FromVisual(win) is HwndSource s) Enganchar(s);
        };
        win.Closed += (_, __) => { _fin.Stop(); _fuente?.RemoveHook(Mensaje); };
    }

    private void Enganchar(HwndSource s)
    {
        _fuente = s;
        s.AddHook(Mensaje);
    }

    private IntPtr Mensaje(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_MOUSEWHEEL && msg != WM_MOUSEHWHEEL) return IntPtr.Zero;
        if (!_activo()) return IntPtr.Zero;

        // El delta viene en la parte alta de wParam, con signo.
        int delta = (short)((ToInt64(wParam) >> 16) & 0xFFFF);
        if (delta == 0) return IntPtr.Zero;

        // La rueda vertical va al revés que la pantalla: girar «hacia arriba» es delta positivo, y
        // arriba en pantalla es Y decreciente. La horizontal sí coincide: positivo es a la derecha.
        if (msg == WM_MOUSEHWHEEL) _dx += delta;
        else _dy -= delta;

        _fin.Stop();
        _fin.Start();
        handled = true;
        return IntPtr.Zero;
    }

    private void Soltar()
    {
        _fin.Stop();
        double dx = _dx, dy = _dy;
        _dx = _dy = 0;
        if (dx == 0 && dy == 0) return;
        _lanzar(dx * PorMuesca, dy * PorMuesca);
    }

    private static long ToInt64(IntPtr p) => IntPtr.Size == 8 ? p.ToInt64() : p.ToInt32();
}
