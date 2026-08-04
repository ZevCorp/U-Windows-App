using System.Windows;
using System.Windows.Media.Animation;

namespace U.WindowsClient.Ui;

/// <summary>
/// Ü vive pegado a un borde lateral. Aquí está la única regla de cómo llega.
///
/// Existe como sitio propio porque hay DOS formas de mover la ventana y las dos tienen que acabar
/// igual: los gestos de la carita (<see cref="FaceGestures"/>, que sí miden velocidad) y el arrastre
/// por el cuerpo de la barra (<c>DragMove</c> de Windows, que no la mide). Si el pegado viviera solo
/// en los gestos, arrastrar por la barra dejaría a Ü plantado en mitad de la pantalla.
/// </summary>
public static class EdgeSnap
{
    /// <summary>Por debajo de esto el gesto no tiene dirección: manda la cercanía, no el impulso.</summary>
    private const double DireccionMinimaDip = 260;

    /// <summary>Rigidez del muelle. Ver <see cref="ThrowEase"/>: es también el tope de velocidad sin rebote.</summary>
    private const double Rigidez = 6.5;

    /// <summary>¿En qué mitad de la pantalla está la ventana ahora mismo?</summary>
    public static bool EstáALaIzquierda(Window win)
    {
        var wa = SystemParameters.WorkArea;
        return win.Left + win.ActualWidth / 2 < (wa.Left + wa.Right) / 2;
    }

    /// <summary>
    /// Manda la ventana a un lado. SIEMPRE a un lado: soltarla en mitad de la pantalla la dejaba
    /// encima del trabajo del usuario, que es justo donde no tiene que estar. La velocidad no decide
    /// SI se va al borde; decide a CUÁL y con cuánto ímpetu llega.
    ///
    /// **A qué lado.** Si se lanzó con intención (más de <see cref="DireccionMinimaDip"/>), manda la
    /// DIRECCIÓN del lanzamiento aunque el borde contrario esté más cerca — tirar hacia la derecha y
    /// que se vaya a la izquierda porque estaba a tres píxeles se siente como que no te hizo caso.
    /// Si se movió despacio, manda el borde más próximo.
    ///
    /// **La altura es del usuario.** Queda donde la dejó, más lo que proyecte el impulso vertical.
    /// Solo el lado es innegociable.
    ///
    /// **Cuánto tarda.** Proporcional a la distancia, pero acortado si venía rápido: mantener una
    /// duración larga con mucha velocidad obligaría al muelle a pasarse del borde y volver.
    /// </summary>
    /// <param name="vx">Velocidad horizontal al soltar, en DIP/s. 0 si no se midió.</param>
    /// <param name="alLlegar">Se llama con el DESTINO (no con la posición actual, que está a medio camino).</param>
    public static void Aplicar(Window win, double vx, double vy, Action<double, double>? alLlegar)
    {
        var wa = SystemParameters.WorkArea;
        double w = win.ActualWidth, h = win.ActualHeight;

        double bordeIzq = wa.Left;
        double bordeDer = Math.Max(wa.Left, wa.Right - w);

        bool aLaDerecha = Math.Abs(vx) >= DireccionMinimaDip
            ? vx > 0                                            // lo lanzaste: manda hacia dónde iba
            : win.Left + w / 2 >= (wa.Left + wa.Right) / 2;     // lo posaste: manda dónde está

        double destLeft = aLaDerecha ? bordeDer : bordeIzq;
        double destTop = Math.Clamp(win.Top + vy * 0.12, wa.Top, Math.Max(wa.Top, wa.Bottom - h));

        double dx = destLeft - win.Left, dy = destTop - win.Top;
        double dist = Math.Sqrt(dx * dx + dy * dy);

        if (dist < 0.5) { alLlegar?.Invoke(destLeft, destTop); return; }   // ya estaba ahí

        double rapidez = Math.Sqrt(vx * vx + vy * vy);
        double ms = Math.Clamp(300 + dist * 0.55, 320, 820);
        // Tope por velocidad: la v₀ normalizada (rapidez·T/dist) tiene que quedar bajo la rigidez.
        if (rapidez > 1) ms = Math.Min(ms, Rigidez * dist / rapidez * 1000);
        ms = Math.Max(ms, 170);   // por debajo de esto ya no es un movimiento, es un salto
        var dur = new Duration(TimeSpan.FromMilliseconds(ms));
        double segundos = ms / 1000.0;

        // Una curva por eje: cada uno sale a SU velocidad. Con una sola compartida, el eje lento
        // arrancaría de golpe o el rápido arrancaría frenado, y se nota.
        Animar(win, Window.LeftProperty, destLeft, dur, Curva(vx, dx, segundos));
        Animar(win, Window.TopProperty, destTop, dur, Curva(vy, dy, segundos));

        alLlegar?.Invoke(destLeft, destTop);
    }

    /// <summary>
    /// La curva de un eje. La pendiente inicial es la velocidad real del cursor llevada a la escala de
    /// la animación; si en ese eje apenas hay recorrido se arranca parado, porque dividir por una
    /// distancia diminuta da una pendiente absurda y un tirón.
    /// </summary>
    private static IEasingFunction Curva(double v, double d, double segundos)
    {
        double pendiente = Math.Abs(d) < 1 ? 0 : v * segundos / d;
        // Negativa = el impulso iba al revés del destino (te pasaste y vuelve). En física pura habría
        // que arrancar hacia atrás; en pantalla se lee como un tirón, así que se ignora.
        return new ThrowEase { InitialSlope = Math.Max(0, pendiente), Stiffness = Rigidez };
    }

    private static void Animar(Window win, DependencyProperty prop, double to, Duration dur, IEasingFunction ease) =>
        win.BeginAnimation(prop, new DoubleAnimation(to, dur)
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd,
        });
}
