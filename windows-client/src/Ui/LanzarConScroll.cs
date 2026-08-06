using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace U.WindowsClient.Ui;

/// <summary>
/// Mover y lanzar la carita con dos dedos en el trackpad, hacia donde sea.
/// </summary>
/// <remarks>
/// **La carita va con los dedos, no después de ellos.** La primera versión sumaba el gesto entero y
/// movía al terminar, y eso no es empujar algo: es mandarle una orden y esperar a ver qué hace. Si
/// los dedos dan una vuelta despacio, la carita tiene que dar esa vuelta a la vez; si a mitad del
/// mismo gesto se lanza fuerte, tiene que salir disparada hacia allí. Igual que arrastrarla con el
/// ratón, que ya funcionaba así (2026-08-06, pedido por el usuario).
///
/// Así que cada mensaje mueve la ventana en el acto, y la velocidad se mide sobre los últimos
/// milisegundos para lanzarla cuando los dedos paran. Medirla sobre el gesto ENTERO daría la media
/// de todo el paseo, que es justo lo contrario de lo que hace un flick al final: lo que manda es
/// cómo iba al soltar, no cómo empezó.
///
/// **El sentido es el del dedo, no el del scroll.** Aquí no se desplaza un documento —donde la
/// convención es que el contenido va al revés que el dedo—, se empuja un objeto.
///
/// WPF solo traduce la rueda vertical; la horizontal llega en un mensaje que no convierte, así que
/// se escucha a mano. Sin eso, «hacia cualquier lugar» serían dos direcciones de cuatro.
/// </remarks>
internal sealed class LanzarConScroll
{
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_MOUSEHWHEEL = 0x020E;

    /// <summary>
    /// Píxeles que recorre la carita por cada unidad de rueda. Una muesca de ratón son 120; los
    /// trackpads de precisión mandan trocitos mucho más pequeños y seguidos, que es lo que hace que
    /// el movimiento salga continuo en vez de a saltos.
    /// </summary>
    private const double PixelesPorUnidad = 0.55;

    /// <summary>Cuánto silencio cierra el gesto. Menos, y un empujón largo se parte en dos.</summary>
    private static readonly TimeSpan Pausa = TimeSpan.FromMilliseconds(110);

    /// <summary>Sobre cuánto se mide la velocidad al soltar: el final del gesto, no su historia.</summary>
    private static readonly TimeSpan Ventana = TimeSpan.FromMilliseconds(120);

    private readonly Window _win;
    private readonly Func<bool> _activo;
    private readonly Action<double, double> _lanzar;
    private readonly System.Windows.Threading.DispatcherTimer _fin;
    private readonly List<(DateTime Cuando, double Dx, double Dy)> _recientes = new();

    private HwndSource? _fuente;
    private double _pendX, _pendY;
    private bool _pintando;

    // ── Oír la rueda aunque el cursor ya no esté encima ──────────────────────────────────────
    //
    // WM_MOUSEWHEEL lo entrega Windows a la ventana QUE ESTÁ BAJO EL CURSOR. Y lo primero que hace
    // este gesto es apartar la carita de debajo del cursor, así que a los pocos píxeles dejábamos de
    // recibir el resto del empujón: el movimiento salía con velocidad y se cortaba en seco a mitad,
    // como si el trackpad hubiera dejado de existir (2026-08-06, lo describió el usuario: «sale de
    // su contorno de acción»).
    //
    // Mientras dura el gesto se escucha la rueda de TODO el sistema con un gancho de bajo nivel, y
    // se suelta en cuanto los dedos paran. Un gancho global permanente sería caro y de mala
    // educación; uno que vive lo que dura un empujón, no.

    private const int WH_MOUSE_LL = 14;
    private delegate IntPtr Gancho(int code, IntPtr wParam, IntPtr lParam);
    private Gancho? _fnGancho;           // referencia viva: si se la lleva el recolector, Windows cae
    private IntPtr _hGancho;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public int X, Y;
        public uint MouseData, Flags, Time;
        public IntPtr Extra;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, Gancho fn, IntPtr mod, uint hilo);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);

    private void EngancharRuedaGlobal()
    {
        if (_hGancho != IntPtr.Zero) return;
        _fnGancho = RuedaGlobal;
        _hGancho = SetWindowsHookEx(WH_MOUSE_LL, _fnGancho, IntPtr.Zero, 0);
    }

    private void SoltarRuedaGlobal()
    {
        if (_hGancho == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hGancho);
        _hGancho = IntPtr.Zero;
        _fnGancho = null;
    }

    private IntPtr RuedaGlobal(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int msg = (int)(IntPtr.Size == 8 ? wParam.ToInt64() : wParam.ToInt32());
            if (msg == WM_MOUSEWHEEL || msg == WM_MOUSEHWHEEL)
            {
                var datos = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                int delta = (short)((datos.MouseData >> 16) & 0xFFFF);
                if (delta != 0) Empujar(msg, delta);
                return new IntPtr(1);   // el empujón es nuestro: que no lo desplace nadie más
            }
        }
        return CallNextHookEx(_hGancho, code, wParam, lParam);
    }

    /// <summary>Aplica lo acumulado desde el último fotograma. Uno por cuadro, no uno por mensaje.</summary>
    private void Cuadro(object? sender, EventArgs e)
    {
        double dx = _pendX, dy = _pendY;
        _pendX = _pendY = 0;
        if (dx == 0 && dy == 0) return;

        // POR LOS CUATRO BORDES. El lateral dejaba asomar la carita fuera de la pantalla y el
        // vertical no, así que arriba y abajo topaba contra una pared invisible mientras a los lados
        // se salía tan ricamente. No hay razón para que un borde se comporte distinto de otro
        // (2026-08-06). Lo que la trae de vuelta es EdgeSnap al soltar, no un tope aquí.
        var wa = SystemParameters.WorkArea;
        double w = _win.ActualWidth, h = _win.ActualHeight;
        try
        {
            _win.Left = Math.Clamp(_win.Left + dx, wa.Left - w * 0.35, wa.Right - w * 0.65);
            _win.Top = Math.Clamp(_win.Top + dy, wa.Top - h * 0.35, wa.Bottom - h * 0.65);
        }
        catch { }
    }

    private void DejarDePintar()
    {
        if (!_pintando) return;
        _pintando = false;
        System.Windows.Media.CompositionTarget.Rendering -= Cuadro;
        _pendX = _pendY = 0;
    }

    /// <param name="activo">Si ahora mismo el gesto cuenta. Colapsada sí; con la barra abierta no,
    /// que ahí el scroll es para el menú.</param>
    /// <param name="lanzar">Velocidad al soltar, en DIP/s, lista para <see cref="EdgeSnap"/>.</param>
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
        win.Closed += (_, __) =>
        {
            _fin.Stop();
            SoltarRuedaGlobal();
            DejarDePintar();
            _fuente?.RemoveHook(Mensaje);
        };
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

        int delta = (short)((ToInt64(wParam) >> 16) & 0xFFFF);
        if (delta == 0) return IntPtr.Zero;

        // Desde aquí manda el gancho global: el cursor va a dejar de estar encima enseguida.
        EngancharRuedaGlobal();
        Empujar(msg, delta);
        handled = true;
        return IntPtr.Zero;
    }

    /// <summary>Un trocito de empujón, venga del mensaje de la ventana o del gancho global.</summary>
    private void Empujar(int msg, int delta)
    {
        double dx = 0, dy = 0;
        if (msg == WM_MOUSEHWHEEL) dx = -delta * PixelesPorUnidad;
        else dy = delta * PixelesPorUnidad;

        // Tocarla corta el viaje que llevara: mandan los dedos, como al agarrarla con el ratón.
        Vuelo.Termina();

        // NO SE MUEVE LA VENTANA AQUÍ. Un trackpad de precisión manda más de cien mensajes por
        // segundo, y mover la ventana en cada uno son cien SetWindowPos —cada uno de verdad, sobre
        // una ventana transparente— hechos en el hilo que atiende los mensajes. La cola se llena, el
        // gesto se retrasa y la carita llega tarde a donde ya están los dedos (2026-08-06).
        //
        // Se apunta el desplazamiento y se aplica UNA VEZ por fotograma, que es lo máximo que puede
        // verse de todos modos. La respuesta se siente inmediata porque el fotograma llega enseguida,
        // y el trabajo pasa a ser el mismo mueva el trackpad lo que mueva.
        _pendX += dx;
        _pendY += dy;
        if (!_pintando)
        {
            _pintando = true;
            System.Windows.Media.CompositionTarget.Rendering += Cuadro;
        }

        var ahora = DateTime.UtcNow;
        _recientes.Add((ahora, dx, dy));
        // Solo interesa el final del gesto: lo viejo se tira aquí y no al soltar, para que la lista
        // no crezca durante un paseo largo.
        _recientes.RemoveAll(r => ahora - r.Cuando > Ventana);

        _fin.Stop();
        _fin.Start();
    }

    private void Soltar()
    {
        _fin.Stop();
        SoltarRuedaGlobal();             // el gesto acabó: la rueda vuelve a ser de quien la use
        Cuadro(null, EventArgs.Empty);   // lo que quedara sin aplicar, antes de soltar
        DejarDePintar();
        var ahora = DateTime.UtcNow;
        _recientes.RemoveAll(r => ahora - r.Cuando > Ventana + Pausa);
        if (_recientes.Count == 0) return;

        double dx = _recientes.Sum(r => r.Dx), dy = _recientes.Sum(r => r.Dy);
        double segundos = Math.Max((ahora - _recientes[0].Cuando).TotalSeconds - Pausa.TotalSeconds, 0.04);
        _recientes.Clear();

        // Si los dedos venían PARANDO, la velocidad del final es casi cero y no hay lanzamiento: se
        // queda donde la dejaron y EdgeSnap solo la pega al lado más cercano, que es lo correcto —
        // pasear la carita hasta un sitio no debería acabar mandándola a otro.
        _lanzar(dx / segundos, dy / segundos);
    }

    private static long ToInt64(IntPtr p) => IntPtr.Size == 8 ? p.ToInt64() : p.ToInt32();
}
