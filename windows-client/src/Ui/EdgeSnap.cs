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

        // EN DIAGONAL TAMBIÉN. La proyección vertical estaba en 0,05 —casi nada— así que cualquier
        // lanzamiento acababa plano: se iba al lado, sí, pero a la misma altura, y un gesto hecho en
        // diagonal se veía enderezar por el camino (2026-08-06, pedido por el usuario).
        //
        // Se abre a 0,14 pero con un TOPE de recorrido en vez de dejarlo suelto, que es lo que en su
        // día obligó a cerrarla: sin tope, un gesto rápido la mandaba de una esquina a la otra y el
        // usuario la perdía de vista. Con el tope el gesto se nota y la carita nunca se va tan lejos
        // como para tener que buscarla.
        double saltoY = Math.Clamp(vy * 0.14, -wa.Height * 0.45, wa.Height * 0.45);
        double destTop = Math.Clamp(win.Top + saltoY, wa.Top, Math.Max(wa.Top, wa.Bottom - h));

        double dx = destLeft - win.Left, dy = destTop - win.Top;
        double dist = Math.Sqrt(dx * dx + dy * dy);

        if (dist < 0.5) { alLlegar?.Invoke(destLeft, destTop); return; }   // ya estaba ahí

        // Solo distancia. Un recorrido corto se resuelve rápido, uno largo se ve viajar.
        //
        // Subido de (300 + 0,62·d, tope 900) porque cruzar la pantalla en 0,9 s no se lee como un
        // viaje, se lee como un corte: el ojo ve la salida y la llegada y se pierde el medio, que es
        // justo donde está la sensación de peso. Con esto un lado a otro son ~1,3 s (2026-08-06).
        double ms = Math.Clamp(420 + dist * 0.95, 380, 1400);
        double segundos = ms / 1000.0;

        // El viaje se lanza ANTES de avisar a nadie: `alLlegar` reordena la barra hacia el lado
        // nuevo, y esa reordenación cambia el tamaño de la ventana, lo que disparaba una corrección
        // de posición que se comía el movimiento (2026-08-06).
        //
        // Una curva por eje: cada uno sale a SU velocidad. Con una sola compartida, el eje lento
        // arrancaría de golpe o el rápido arrancaría frenado, y se nota.
        // La panza del camino: proporcional al viaje y con techo, porque en un salto corto un arco
        // grande se ve como un tropiezo. Hacia ARRIBA siempre (la perpendicular con el signo que
        // toque), como quien lanza algo por encima de la mesa en vez de arrastrarlo por ella.
        double arco = Math.Min(dist * 0.10, 55) * (dx >= 0 ? -1 : 1);

        Vuelo.Mover(win, destLeft, destTop, TimeSpan.FromMilliseconds(ms),
                    Curva(vx, dx, segundos), Curva(vy, dy, segundos), arco);

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
        // CON REBOTE, como la burbuja de Android. Llegar y parar en seco es correcto y se lee como
        // software; pasarse un poco y volver es lo que hace que parezca que pesa (2026-08-05, pedido
        // por el usuario comparándolo con el de Android, donde esto ya funcionaba bien).
        // SIN REBOTE aquí. El destino es un borde de la pantalla, y un muelle que rebota tiene que
        // pasarse del destino para volver: pasarse de un borde es salirse, y Windows no deja —clava
        // la ventana y se come el rebote—. Se probó a reflejarlo hacia dentro y el efecto es otro:
        // ya no parece que llega, parece que se arrepiente (2026-08-06, descartado por el usuario).
        //
        // Lo que da la sensación de física aquí no es el rebote, es el recorrido: la curva de
        // velocidad y la panza del camino.
        return new MuelleEase { InitialSlope = pendiente, Stiffness = Rigidez, Damping = 1.0 };
    }

    /// <summary>Un rebote corto. Ver <see cref="MuelleEase.Damping"/>.</summary>
    private const double Amortiguamiento = 0.62;

    /// <summary>
    /// La rigidez del muelle QUE REBOTA, que no puede ser la misma que la del que no rebota.
    ///
    /// <see cref="Rigidez"/> se bajó a 4,5 para repartir el recorrido de una curva que llega y para.
    /// A un muelle que rebota, 4,5 le deja el rebote a medias cuando la animación se acaba —tarda
    /// ≈4/ζω en asentarse, o sea 1,4 veces la duración— y la normalización final se lo come. Con 6,5
    /// el muelle está quieto justo al terminar y el rebote se ve entero.
    /// </summary>
    private const double RigidezConRebote = 6.5;

}
