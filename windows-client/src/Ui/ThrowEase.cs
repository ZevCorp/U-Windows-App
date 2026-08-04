using System.Windows.Media.Animation;

namespace U.WindowsClient.Ui;

/// <summary>
/// La curva de un objeto lanzado: sale a la velocidad con la que lo soltaste y frena hasta pararse.
///
/// Es un muelle críticamente amortiguado con velocidad inicial:
///
///     p(t) = 1 − (1 + (k − v₀)·t)·e^(−k·t)
///
/// La diferencia con una curva de frenado normal (un <c>QuarticEase</c> EaseOut, por ejemplo) es que
/// esa arranca SIEMPRE a la misma velocidad, así que el objeto se detiene un instante al soltarlo y
/// vuelve a arrancar. Ese micro-parón es exactamente lo que hace que un movimiento se sienta de
/// software y no de algo con peso. Aquí la pendiente en t=0 es <see cref="InitialSlope"/>, así que el
/// dedo suelta y el movimiento continúa sin costura.
///
/// Dos detalles que hacen falta para que no se note el truco:
///
///  · **Se normaliza por p(1).** El muelle llega al destino en el infinito, no en t=1: sin dividir,
///    la ventana se quedaba parada a un 1 % del borde, que a simple vista es «no llegó del todo».
///  · **No rebota si v₀ ≤ k.** El muelle solo se pasa de largo cuando la velocidad inicial supera la
///    rigidez; quien llama acorta la duración para mantenerse por debajo. Importa porque pasarse aquí
///    significa que la barra se sale de la pantalla y vuelve, y eso no se lee como física sino como
///    un fallo.
/// </summary>
public sealed class ThrowEase : IEasingFunction
{
    /// <summary>
    /// Velocidad al soltar, normalizada: <c>velocidad · duración / distancia</c>. 0 = arranca parado
    /// (un arrastre lento), valores altos = venía lanzado.
    /// </summary>
    public double InitialSlope { get; init; }

    /// <summary>Cuánto tira el muelle. Más alto = frena antes y llega más seco.</summary>
    public double Stiffness { get; init; } = 6.5;

    public double Ease(double t)
    {
        double k = Stiffness;
        double v0 = Math.Min(InitialSlope, k);   // por encima de k rebotaría: ver la nota de arriba
        double p1 = Raw(1, k, v0);
        if (p1 <= 0) return t;                   // degenerado: mejor lineal que dividir por cero
        return Raw(t, k, v0) / p1;
    }

    private static double Raw(double t, double k, double v0) =>
        1 - (1 + (k - v0) * t) * Math.Exp(-k * t);
}
