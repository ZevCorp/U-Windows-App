using System.Windows;

namespace U.WindowsClient.Ui;

/// <summary>
/// LA GEOMETRÍA del anillo de acciones, separada del dibujo para que el contrato la juzgue sin
/// pantalla (promesa 115, spec 008). El anillo real (<see cref="AnilloDeAcciones"/>) no decide
/// nada: pide aquí sus posiciones y las pinta, así lo juzgado y lo pintado no pueden discrepar
/// (aprendizaje nº16: comparar por el mismo camino).
/// </summary>
/// <remarks>
/// Un abanico de burbujas alrededor de la carita, abierto hacia el centro de la pantalla: la carita
/// vive pegada a un borde, así que hacia el otro lado no hay sitio. Tres cosas que se garantizan y
/// que el contrato comprueba en las cuatro esquinas:
///
/// · <b>Cada burbuja cabe ENTERA en el área de trabajo.</b> En una esquina el abanico no cabe tal
///   cual; lo que se hace es ACORTARLO por el lado que choca y, si con eso las burbujas quedan
///   demasiado juntas, AGRANDAR EL RADIO hasta que vuelvan a caber. No se desplaza el abanico en
///   bloque, que fue la primera idea: desplazado, la burbuja de arriba se venía sobre la carita.
/// · <b>Ninguna burbuja pisa la carita</b>: todas están a la misma distancia (el radio), que nunca
///   baja de 72 —la carita suelta mide 72, o sea 36 de radio, más 22 de media burbuja—.
/// · <b>Ninguna burbuja pisa a otra</b>: la cuerda entre vecinas es al menos un diámetro.
///
/// Las posiciones son CENTROS, en DIPs de pantalla, en el orden de arriba a abajo.
/// </remarks>
public static class ReglaDelAnillo
{
    /// <summary>Distancia del centro de la carita al centro de cada burbuja, si hay sitio.</summary>
    public const double Radio = 72;
    /// <summary>El diámetro de una burbuja.</summary>
    public const double DiametroItem = 44;
    /// <summary>Cuánto abre el abanico cuando hay sitio, en grados. 150 y no 180: a 180 las de los
    /// extremos quedarían a la altura de la carita, pegadas al borde, donde no hay sitio.</summary>
    public const double Abanico = 150;

    /// <summary>Más allá de esto una burbuja ya no está «hacia el centro»: está a la altura de la
    /// carita. Y estrictamente menos de 90 para que x quede del lado bueno sin discusión.</summary>
    private const double AnguloMaximo = 80;
    /// <summary>Aire entre el borde de una burbuja y el del área de trabajo, para que un redondeo
    /// no deje medio píxel fuera.</summary>
    private const double Aire = 2;

    /// <summary>
    /// Dónde va cada una de las <paramref name="n"/> burbujas alrededor de <paramref name="centro"/>
    /// (el centro de la carita), abriéndose hacia la derecha si la carita está pegada al borde
    /// izquierdo y hacia la izquierda si no, sin salirse de <paramref name="areaDeTrabajo"/>.
    /// </summary>
    public static IReadOnlyList<Point> Posiciones(Point centro, bool pegadaALaIzquierda, int n, Rect areaDeTrabajo)
    {
        if (n <= 0) return Array.Empty<Point>();

        double r = DiametroItem / 2 + Aire;
        double topeArriba = areaDeTrabajo.Top + r;      // la y mínima de un centro
        double topeAbajo = areaDeTrabajo.Bottom - r;    // la y máxima de un centro
        IReadOnlyList<Point>? ultimo = null;

        // Se prueba del radio natural hacia fuera: el primero con el que las burbujas caben sin
        // tocarse es el bueno. En medio de un borde es 72 a la primera; en una esquina sube a ~104.
        for (double radio = Radio; radio <= Radio * 4; radio += 4)
        {
            // El tramo de ángulos disponible: lo que el abanico quisiera (±75°), recortado por el
            // borde de arriba y el de abajo. Ángulo 0 = hacia el centro de la pantalla; negativo =
            // hacia arriba.
            double desde = Math.Max(-Abanico / 2, Grados(Math.Asin(Math.Clamp((topeArriba - centro.Y) / radio, -1, 1))));
            double hasta = Math.Min(Abanico / 2, Grados(Math.Asin(Math.Clamp((topeAbajo - centro.Y) / radio, -1, 1))));
            desde = Math.Max(desde, -AnguloMaximo);
            hasta = Math.Min(hasta, AnguloMaximo);
            if (hasta <= desde) continue;   // ni un tramo: que crezca el radio

            // Con sitio de sobra el abanico va entero y lo más centrado posible; sin sitio, va lo
            // que cabe.
            double apertura = Math.Min(Abanico, hasta - desde);
            // Sin Math.Clamp: cuando el abanico es justo el tramo disponible, el mínimo y el máximo
            // son el mismo número salvo por el redondeo, y Clamp LANZA si el mínimo queda un ulp por
            // encima del máximo. Lo vio el contrato a la primera, en la esquina (2026-09-02).
            double minimo = desde + apertura / 2, maximo = hasta - apertura / 2;
            double centroAngular = Math.Max(minimo, Math.Min(maximo, 0));
            double primero = centroAngular - apertura / 2;
            double paso = n == 1 ? 0 : apertura / (n - 1);

            var puntos = new List<Point>(n);
            for (int i = 0; i < n; i++)
            {
                double a = Radianes(n == 1 ? centroAngular : primero + paso * i);
                double dx = radio * Math.Cos(a);
                puntos.Add(new Point(pegadaALaIzquierda ? centro.X + dx : centro.X - dx, centro.Y + radio * Math.Sin(a)));
            }
            ultimo = puntos;

            // ¿Se tocan? La cuerda entre vecinas tiene que dar para un diámetro.
            bool separadas = n == 1 || 2 * radio * Math.Sin(Radianes(paso) / 2) >= DiametroItem;
            // ¿Caben de lado? En un monitor normal siempre; en uno diminuto, que siga creciendo no
            // arregla nada, así que se devuelve lo que hay.
            if (separadas) return puntos;
        }

        return ultimo ?? Array.Empty<Point>();
    }

    /// <summary>
    /// Qué burbuja está bajo <paramref name="cursor"/>: la más cercana, si está a menos de media
    /// burbuja más <paramref name="tolerancia"/>. Soltar sobre la carita (o sobre nada) es
    /// <c>null</c>: cancelar. Es geometría pura, sin hit-test visual, para que se pueda juzgar.
    /// </summary>
    public static int? ElegirEn(IReadOnlyList<Point> posiciones, Point cursor, double tolerancia = 8)
    {
        int? mejor = null;
        double mejorDistancia = double.MaxValue;
        for (int i = 0; i < posiciones.Count; i++)
        {
            double d = (posiciones[i] - cursor).Length;
            if (d < mejorDistancia) { mejorDistancia = d; mejor = i; }
        }
        return mejorDistancia <= DiametroItem / 2 + tolerancia ? mejor : null;
    }

    private static double Grados(double radianes) => radianes * 180 / Math.PI;
    private static double Radianes(double grados) => grados * Math.PI / 180;
}
