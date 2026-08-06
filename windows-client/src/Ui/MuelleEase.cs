using System.Windows.Media.Animation;

namespace U.WindowsClient.Ui;

/// <summary>
/// Un muelle que SE PASA UN POCO y vuelve. Es lo que hace que algo parezca tener peso.
///
/// <see cref="ThrowEase"/> ya movía la carita con física, pero críticamente amortiguada: llega y
/// para, sin pasarse ni un píxel. Eso es correcto y se lee como software. Lo que hace que la
/// burbuja de Android se sienta viva es que rebota — se pasa del destino un ocho por ciento y
/// vuelve, una vez. Aquí está el mismo muelle con un grado de libertad más: el amortiguamiento.
///
///     p(t) = 1 − e^(−ζωt)·( cos(ω_d·t) + ((ζω − v₀)/ω_d)·sen(ω_d·t) ),   ω_d = ω·√(1−ζ²)
///
/// con ζ = 1 vuelve a ser exactamente la curva de antes.
///
/// LA DURACIÓN SALE DEL MUELLE, no al revés. Un muelle llega al destino en el infinito, así que si
/// la animación se corta antes de que se asiente, el rebote se queda a medias y hay que estirar la
/// curva para que acabe en 1 — y estirarla se come justo el rebote que se quería. Con ζ=0,62 y
/// ω=6,5 el muelle está quieto en t=1 (tiempo de asentamiento ≈ 4/ζω ≈ 1), así que la corrección
/// final es inapreciable y el rebote se ve entero.
///
/// El rebote se pasa un ~8 % del recorrido. Es poco a propósito: en Android la burbuja puede salirse
/// de la pantalla y aquí también, pero pasarse mucho al llegar a un borde no se lee como un muelle,
/// se lee como que la ventana se ha ido y ha tenido que volver.
/// </summary>
public sealed class MuelleEase : IEasingFunction
{
    /// <summary>
    /// Velocidad al soltar, normalizada: <c>velocidad · duración / distancia</c>. 0 = arranca parado
    /// —que es lo correcto cuando la carita se mueve sola, porque nadie la ha empujado—.
    /// </summary>
    public double InitialSlope { get; init; }

    /// <summary>Cuánto tira el muelle.</summary>
    public double Stiffness { get; init; } = 6.5;

    /// <summary>
    /// Amortiguamiento. 1 = llega y para (la curva de siempre); por debajo de 1 rebota, y cuanto más
    /// bajo, más veces. 0,62 da UN rebote corto, que es lo que se lee como peso: dos ya se lee como
    /// gelatina.
    /// </summary>
    public double Damping { get; init; } = 0.62;

    /// <summary>
    /// El rebote va HACIA DENTRO en vez de pasarse de largo. Para cuando el destino es una pared.
    /// </summary>
    /// <remarks>
    /// Un muelle se pasa del destino y vuelve. Eso está bien en mitad de la pantalla, pero cuando el
    /// destino ES el borde, pasarse significa salirse — y ahí Windows no deja: clava la ventana en el
    /// borde y se come el rebote entero. Medido: la curva pedía −67 px y la ventana se quedaba en 0
    /// durante medio segundo, así que el lanzamiento se veía llegar y pararse en seco mientras la
    /// animación seguía corriendo sola por dentro (2026-08-06).
    ///
    /// Reflejar lo que se pasa de 1 convierte «se sale y vuelve» en «choca y rebota hacia dentro»,
    /// que es lo que hace una pelota contra una pared: el mismo gesto, del lado donde hay sitio.
    /// </remarks>
    public bool RebotaHaciaDentro { get; init; }

    public double Ease(double t)
    {
        double p1 = P(1);
        double v = Math.Abs(p1) < 1e-6 ? t : P(t) / p1;
        return RebotaHaciaDentro && v > 1 ? 2 - v : v;
    }

    private double P(double t)
    {
        double w = Stiffness, z = Damping, v = InitialSlope;

        // Sin rebote: el muelle crítico de toda la vida. Se conserva para que poner Damping=1 sea
        // literalmente la curva anterior y no una aproximación parecida.
        if (z >= 1) return 1 - (1 + (w - v) * t) * Math.Exp(-w * t);

        double wd = w * Math.Sqrt(1 - z * z);
        double b = (z * w - v) / wd;
        return 1 - Math.Exp(-z * w * t) * (Math.Cos(wd * t) + b * Math.Sin(wd * t));
    }
}
