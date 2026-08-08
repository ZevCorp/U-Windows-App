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
    private static double _arcoX, _arcoY;
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
    /// <param name="arco">
    /// Cuánto se aparta el camino de la línea recta, en píxeles, medido en su punto más ancho.
    ///
    /// Una ventana que va del punto A al B por una recta perfecta no se lee como algo que vuela: se
    /// lee como algo a lo que le han cambiado dos números. Nada que se lanza de verdad viaja en
    /// línea recta —una pelota describe un arco— y basta con muy poco para que el ojo lo compre.
    /// Se abre y se cierra con un seno, así que sale y entra por donde debe: pegado a la recta en
    /// los dos extremos y separado al máximo por la mitad (2026-08-06, pedido por el usuario: «se
    /// siente muy recto de un lado a otro»).
    /// </param>
    public static void Mover(Window win, double destLeft, double destTop, TimeSpan dur,
                            IEasingFunction ejeX, IEasingFunction ejeY, double arco = 0)
    {
        Termina();
        _win = win;
        _x0 = win.Left; _y0 = win.Top;
        _x1 = destLeft; _y1 = destTop;

        // El arco va PERPENDICULAR al viaje, que es la única dirección en la que apartarse no
        // acorta ni alarga el recorrido: si se inclinara hacia el destino, cambiaría la velocidad
        // que ya define la curva de cada eje y se pisarían las dos cosas.
        double dx = _x1 - _x0, dy = _y1 - _y0;
        double largo = Math.Sqrt(dx * dx + dy * dy);
        if (largo > 1 && Math.Abs(arco) > 0.5)
        {
            _arcoX = -dy / largo * arco;
            _arcoY = dx / largo * arco;
        }
        else { _arcoX = _arcoY = 0; }

        _ex = ejeX; _ey = ejeY;
        _dur = dur <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : dur;
        _inicio = DateTime.UtcNow;
        _andando = true;
        CompositionTarget.Rendering += Cuadro;
    }

    /// <summary>
    /// UN SOLO viaje que pasa por todas las paradas, en vez de N viajes encadenados.
    /// </summary>
    /// <remarks>
    /// Ir a una parada, frenar, arrancar hacia la siguiente y repetir se lee como una lista de
    /// saltos: el ojo pierde el hilo entre uno y otro y lo que queda no es «estos seis», es «este,
    /// este, este…». Un recorrido continuo mantiene el hilo — y ese hilo ES la información: lo que
    /// se está diciendo es que los seis van juntos.
    ///
    /// La curva es una Catmull-Rom, que es la que pasa POR los puntos en vez de acercarse a ellos.
    /// Una Bézier corriente los usaría como imanes y la carita no llegaría a ninguno; aquí toca cada
    /// uno y sale hacia el siguiente sin detenerse, que es lo que hace una mano al enumerar.
    ///
    /// El avance va por LONGITUD y no por índice: con paradas desiguales, repartir el tiempo a
    /// partes iguales haría que los tramos cortos se vieran lentísimos y los largos, disparados.
    /// </remarks>
    public static void Recorrido(Window win, IReadOnlyList<Point> paradas, TimeSpan dur)
    {
        if (paradas.Count == 0) return;
        if (paradas.Count == 1)
        {
            Mover(win, paradas[0].X, paradas[0].Y, dur,
                  new MuelleEase { InitialSlope = 0 }, new MuelleEase { InitialSlope = 0 });
            return;
        }

        Termina();
        _win = win;
        _ruta = new List<Point> { new(win.Left, win.Top) };
        _ruta.AddRange(paradas);

        // Longitud acumulada: es lo que permite avanzar a velocidad pareja por todo el recorrido.
        _largos = new double[_ruta.Count];
        _largos[0] = 0;
        for (int i = 1; i < _ruta.Count; i++)
        {
            double dx = _ruta[i].X - _ruta[i - 1].X, dy = _ruta[i].Y - _ruta[i - 1].Y;
            _largos[i] = _largos[i - 1] + Math.Sqrt(dx * dx + dy * dy);
        }
        if (_largos[^1] < 1) { _ruta = null; return; }   // todas en el mismo sitio

        _dur = dur <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : dur;
        _inicio = DateTime.UtcNow;
        _andando = true;
        CompositionTarget.Rendering += Cuadro;
    }

    private static List<Point>? _ruta;
    private static double[]? _largos;

    /// <summary>El punto de la ruta a una fracción del camino, con la curva que pasa por todos.</summary>
    private static Point EnLaRuta(double t)
    {
        var ruta = _ruta!;
        var largos = _largos!;
        double meta = t * largos[^1];

        int i = 1;
        while (i < largos.Length - 1 && largos[i] < meta) i++;

        double tramo = largos[i] - largos[i - 1];
        double u = tramo < 0.001 ? 0 : (meta - largos[i - 1]) / tramo;

        // Catmull-Rom necesita un punto antes y otro después; en los extremos se repite el borde.
        Point p0 = ruta[Math.Max(i - 2, 0)], p1 = ruta[i - 1], p2 = ruta[i], p3 = ruta[Math.Min(i + 1, ruta.Count - 1)];
        return new Point(CatmullRom(p0.X, p1.X, p2.X, p3.X, u),
                         CatmullRom(p0.Y, p1.Y, p2.Y, p3.Y, u));
    }

    private static double CatmullRom(double a, double b, double c, double d, double u)
    {
        double u2 = u * u, u3 = u2 * u;
        return 0.5 * ((2 * b) + (-a + c) * u
                    + (2 * a - 5 * b + 4 * c - d) * u2
                    + (-a + 3 * b - 3 * c + d) * u3);
    }

    /// <summary>Se acabó: alguien ha puesto la ventana en un sitio a mano.</summary>
    public static void Termina()
    {
        if (!_andando) return;
        _andando = false;
        CompositionTarget.Rendering -= Cuadro;
        _win = null; _ex = null; _ey = null; _ruta = null; _largos = null;
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
            if (_ruta != null)
            {
                // Arranca y termina suave, pero por el medio no frena: parar en cada parada es
                // justo lo que convertía el recorrido en una lista de saltos.
                double suave = t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
                var p = EnLaRuta(suave);
                win.Left = p.X;
                win.Top = p.Y;
            }
            else
            {
                // El arco se abre y se cierra con un seno: cero en los dos extremos, máximo en medio.
                // Va en tiempo REAL y no en el progreso de la curva, para que la panza quede en mitad
                // del viaje y no amontonada al principio donde la curva corre más.
                double panza = _arcoX == 0 && _arcoY == 0 ? 0 : Math.Sin(Math.PI * t);
                win.Left = _x0 + (_x1 - _x0) * (_ex?.Ease(t) ?? t) + _arcoX * panza;
                win.Top = _y0 + (_y1 - _y0) * (_ey?.Ease(t) ?? t) + _arcoY * panza;
            }
        }
        catch { ultimo = true; }   // ventana cerrándose: no hay a dónde mover nada

        if (ultimo)
        {
            // Se aterriza EXACTO. Una curva con rebote no acaba clavada en el destino por sí sola —
            // acaba a una milésima—, y una ventana que se queda a un píxel del borde se ve.
            try
            {
                if (_ruta != null) { win.Left = _ruta[^1].X; win.Top = _ruta[^1].Y; }
                else { win.Left = _x1; win.Top = _y1; }
            }
            catch { }
            Termina();
        }
    }
}
