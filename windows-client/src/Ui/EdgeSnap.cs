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
    /// <summary>
    /// Velocidad a partir de la cual un gesto cuenta como LANZAMIENTO y puede mandar a Ü al lado
    /// contrario. Estaba en 260 y era demasiado poco: cualquier arrastre normal pasa de ahí sin
    /// querer, así que reposicionar la barra la cruzaba de pantalla sola. Un lanzamiento de verdad
    /// —el gesto de tirarla— pasa de 900 sin esfuerzo.
    /// </summary>
    private const double LanzamientoDip = 900;

    /// <summary>
    /// Rigidez del muelle. Bajada de 6.5 a 4.5 porque el muelle duro concentraba el viaje al
    /// principio: con 6.5 el 73 % del recorrido se hacía en el primer 20 % del tiempo, y eso no se lee
    /// como velocidad sino como un SALTO con una cola lenta detrás. A 4.5 el movimiento se reparte y
    /// se ve frenar.
    /// </summary>
    private const double Rigidez = 4.5;

    /// <summary>
    /// Tope de la pendiente inicial. Es lo que de verdad decidía la sensación de «se mueve
    /// demasiado»: dejar que la velocidad del cursor entre entera hace que la ventana salga disparada.
    /// Con este tope se conserva la continuidad con el gesto —sales moviéndote, no arrancas de cero—
    /// pero sin que el arranque se coma el recorrido.
    /// </summary>
    private const double PendienteMaxima = 2.2;

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
    /// **A qué lado.** Solo un LANZAMIENTO cruza de lado: hace falta pasar de
    /// <see cref="LanzamientoDip"/> y que el gesto sea claramente horizontal. Cualquier otra cosa
    /// —arrastrarla para recolocarla, moverla en vertical— la deja en el borde que tenga más cerca.
    /// Con el umbral bajo que había antes, reposicionar la barra la mandaba al otro lado sola.
    ///
    /// **La altura es del usuario.** Queda donde la dejó, con una pizca de proyección del impulso.
    /// Solo el lado es innegociable.
    ///
    /// **Cuánto tarda.** Proporcional a la distancia y nada más. Antes se acortaba con la velocidad
    /// —un lanzamiento fuerte daba 200 ms— y por eso parecía que se teletransportaba; ahora el ímpetu
    /// se nota en la FORMA de la curva, no en recortar el viaje.
    /// </summary>
    /// <param name="vx">Velocidad horizontal al soltar, en DIP/s. 0 si no se midió.</param>
    /// <param name="alLlegar">Se llama con el DESTINO (no con la posición actual, que está a medio camino).</param>
    public static void Aplicar(Window win, double vx, double vy, Action<double, double>? alLlegar)
    {
        var wa = SystemParameters.WorkArea;
        double w = win.ActualWidth, h = win.ActualHeight;

        double bordeIzq = wa.Left;
        double bordeDer = Math.Max(wa.Left, wa.Right - w);

        // Cruzar de lado exige las dos cosas: fuerza Y que el gesto vaya de verdad en horizontal.
        // Sin la segunda, arrastrarla hacia abajo con un poco de deriva lateral la cruzaba entera.
        bool lanzada = Math.Abs(vx) >= LanzamientoDip && Math.Abs(vx) > Math.Abs(vy);

        bool aLaDerecha = lanzada
            ? vx > 0                                            // la lanzaste: manda hacia dónde iba
            : win.Left + w / 2 >= (wa.Left + wa.Right) / 2;     // la posaste: manda dónde está

        double destLeft = aLaDerecha ? bordeDer : bordeIzq;
        // Proyección vertical corta (antes 0.12): con la larga, un gesto rápido la mandaba al otro
        // extremo de la pantalla y el usuario la perdía de vista.
        double destTop = Math.Clamp(win.Top + vy * 0.05, wa.Top, Math.Max(wa.Top, wa.Bottom - h));

        double dx = destLeft - win.Left, dy = destTop - win.Top;
        double dist = Math.Sqrt(dx * dx + dy * dy);

        if (dist < 0.5) { alLlegar?.Invoke(destLeft, destTop); return; }   // ya estaba ahí

        // Solo distancia. Un recorrido corto se resuelve rápido, uno largo se ve viajar.
        double ms = Math.Clamp(300 + dist * 0.62, 280, 900);
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
        // Y con techo: la continuidad con el gesto se nota mucho antes de dejar entrar la velocidad
        // entera, y dejarla entera es lo que hacía que saliera disparada.
        pendiente = Math.Clamp(pendiente, 0, PendienteMaxima);
        return new ThrowEase { InitialSlope = pendiente, Stiffness = Rigidez };
    }

    private static void Animar(Window win, DependencyProperty prop, double to, Duration dur, IEasingFunction ease) =>
        win.BeginAnimation(prop, new DoubleAnimation(to, dur)
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd,
        });
}
