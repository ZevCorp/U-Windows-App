using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace U.WindowsClient.Ui;

/// <summary>
/// Mover la ventana CON FÍSICA, cuadro a cuadro. El único sitio que la hace viajar.
/// </summary>
/// <remarks>
/// **Por qué no se usa la animación de WPF.** Animar <c>Window.Left</c> con un
/// <c>DoubleAnimation</c> parece lo natural y no funciona: la ventana no sigue la curva. Medido con
/// el reloj de la animación por dentro y la posición real por fuera, a la vez:
///
///     t=132 ms · la curva pide 358 · la ventana está en 87
///     t=415 ms · la curva pide  67 · la ventana está en 0 y ahí se queda
///
/// Left y Top no son propiedades normales: no las compone la GPU, cada cambio es un SetWindowPos de
/// verdad, y entre el reloj de la animación y el gestor de ventanas se pierde la forma de la curva.
/// El resultado era un lanzamiento que llegaba en 200 ms cuando su curva duraba 770, sin rebote y
/// con la animación siguiendo viva por dentro media pantalla después (2026-08-06).
///
/// Así que se mueve a mano, un cuadro cada vez, que es exactamente lo que hace la burbuja de Android
/// con su ValueAnimator. <c>CompositionTarget.Rendering</c> da el pulso del compositor, así que va
/// sincronizado con lo que se pinta y no con un temporizador aparte.
///
/// **Y de paso resuelve quién manda.** Mientras hay un viaje, la posición es suya: cualquier
/// corrección por cambio de tamaño —que las hay a montones, porque al soltar la carita se reordena
/// la barra y al señalar aparece texto en el globo— tiene que esperar. Antes esas correcciones
/// asignaban Left/Top y cancelaban la animación en el primer cuadro.
/// </remarks>
internal static class Vuelo
{
    private static Window? _win;
    private static double _x0, _y0, _x1, _y1;
    private static IEasingFunction? _ex, _ey;
    private static TimeSpan _dur;
    private static DateTime _inicio;
    private static bool _andando;

    /// <summary>¿Hay un viaje en curso? Si lo hay, la posición es suya.</summary>
    public static bool EnCurso => _andando;

    /// <summary>
    /// Lleva la ventana a un sitio con una curva por eje. Cada eje sale a SU velocidad: con una sola
    /// curva compartida, el eje lento arrancaría de golpe o el rápido arrancaría frenado.
    /// </summary>
    public static void Mover(Window win, double destLeft, double destTop, TimeSpan dur,
                            IEasingFunction ejeX, IEasingFunction ejeY)
    {
        Termina();
        _win = win;
        _x0 = win.Left; _y0 = win.Top;
        _x1 = destLeft; _y1 = destTop;
        _ex = ejeX; _ey = ejeY;
        _dur = dur <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : dur;
        _inicio = DateTime.UtcNow;
        _andando = true;
        CompositionTarget.Rendering += Cuadro;
    }

    /// <summary>Se acabó: alguien ha puesto la ventana en un sitio a mano.</summary>
    public static void Termina()
    {
        if (!_andando) return;
        _andando = false;
        CompositionTarget.Rendering -= Cuadro;
        _win = null; _ex = null; _ey = null;
    }

    private static void Cuadro(object? sender, EventArgs e)
    {
        var win = _win;
        if (win == null) { Termina(); return; }

        double t = (DateTime.UtcNow - _inicio).TotalMilliseconds / _dur.TotalMilliseconds;
        bool ultimo = t >= 1;
        if (ultimo) t = 1;

        try
        {
            win.Left = _x0 + (_x1 - _x0) * (_ex?.Ease(t) ?? t);
            win.Top = _y0 + (_y1 - _y0) * (_ey?.Ease(t) ?? t);
        }
        catch { ultimo = true; }   // ventana cerrándose: no hay a dónde mover nada

        if (ultimo)
        {
            // Se aterriza EXACTO. Una curva con rebote no acaba clavada en el destino por sí sola —
            // acaba a una milésima—, y una ventana que se queda a un píxel del borde se ve.
            try { win.Left = _x1; win.Top = _y1; } catch { }
            Termina();
        }
    }
}
