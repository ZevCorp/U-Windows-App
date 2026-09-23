using System.Windows;

namespace U.WindowsClient.Ui.Jev;

/// <summary>
/// POR DÓNDE Y EN CUÁNTO VUELA LA FLECHA A LO PULSADO, calculado sin pantalla. Promesa 381 (spec 049), su parte
/// pura. La ventana que la mueve fotograma a fotograma (<c>FlechaDeJev</c>) y la pregunta a Windows de quién está
/// delante antes de volar (<c>VistaDeJev</c>) son de la fase 8; aquí solo se decide si hay vuelo y cuál.
/// </summary>
/// <remarks>
/// EN FÍSICOS del escritorio virtual, con las longitudes del plano (520, 42, 70/220, 18/58) en DIP multiplicadas
/// por la escala del monitor de LLEGADA (<c>plano-para-wpf.md</c> §DPI y unidades, fila «Vuelo»). Convertir el
/// camino con la escala del monitor que la flecha va cruzando haría saltar la posición en la costura entre dos
/// monitores; en físicos el camino es continuo, y lo único que cambia al cruzar es el tamaño con que se dibuja.
///
/// LA CURVA ES LA DEL PLANO (<c>OverlayWindow.swift:823-1058</c>, portada en el plano §El vuelo): una Bézier cúbica
/// con los controles a lo largo del camino y un arco perpendicular que sale hacia un lado y entra más recogido,
/// recorrida con <i>smootherstep</i>, que sale y llega con velocidad y aceleración 0. Con tres desviaciones, cada
/// una con su porqué (fase 5, 2026-09-23):
///
/// 1. LA DURACIÓN CUENTA HASTA LO PULSADO, no hasta donde se posa: es la distancia que la 381 enuncia y la que el
///    contrato juzga (780 → 1,5 s). TipTour cuenta hasta la pose de aterrizaje; la diferencia son 42·s entre
///    520·s, 0,08 s como mucho, y solo entre los topes.
/// 2. SE POSA EN LA RECTA DE LLEGADA, 42·s antes del centro, y no en la mejor de las ocho poses del plano: la pose
///    se puntúa contra el <c>rcWork</c> (margen de 34·s) y la firma de la 381 no lo trae. Con la flecha a más de
///    42·s, el punto de llegada cae en el segmento que la une con lo pulsado, así que no sale del área que
///    contenga a los dos (D).
/// 3. EL CONTROL Y EL ARCO SE ACOTAN A LA MITAD DEL CAMINO. Los mínimos del plano (70·s y 18·s) están pensados para
///    cruzar media pantalla: en un vuelo corto el control cae más allá del punto de llegada y el camino se pasa y
///    vuelve. Medido en la fase 5 sobre la curva del plano tal cual: 2.040 de 3.360 vuelos (28 distancias, 24
///    direcciones, 5 escalas) se devolvían, todos con 58·s de camino o menos; con los dos topes, 0 de 15.408, y la
///    versión en C# sin ellos dejó roja la comprobación de los vuelos cortos (M). Con 140·s de camino o más los
///    topes no tocan nada: ahí es la curva del plano, número a número.
/// </remarks>
public sealed class PlanDeVuelo
{
    /// <summary>La velocidad del plano, en DIP por segundo: la duración es la distancia entre 520·escala.</summary>
    public const double DipPorSegundo = 520;

    /// <summary>La duración más corta, en segundos: un vuelo de al lado no es un salto.</summary>
    public const double DuracionMinima = 1.05;

    /// <summary>La más larga, en segundos: cruzar la pantalla no se hace esperar.</summary>
    public const double DuracionMaxima = 2.2;

    /// <summary>A cuánto del centro de lo pulsado se posa, en DIP: la flecha no se posa encima, lo señala.</summary>
    public const double Aterrizaje = 42;

    // La forma del camino, en DIP salvo los factores: OverlayWindow.swift:938-994, leídos en el plano §El vuelo.
    private const double ControlPorDistancia = 0.38, ControlMinimo = 70, ControlMaximo = 220;
    private const double ArcoPorDistancia = 0.08, ArcoMinimo = 18, ArcoMaximo = 58;
    private const double ArcoDeLlegada = 0.65;

    private readonly Point _control1, _control2;

    private PlanDeVuelo(Point inicio, Point fin, Point centroPulsado, Point control1, Point control2, TimeSpan duracion)
    {
        Inicio = inicio; Fin = fin; CentroPulsado = centroPulsado; _control1 = control1; _control2 = control2; Duracion = duracion;
    }

    /// <summary>De dónde sale, en físicos.</summary>
    public Point Inicio { get; }

    /// <summary>Dónde se posa, en físicos: a 42·escala del centro de lo pulsado.</summary>
    public Point Fin { get; }

    /// <summary>Lo que señala al posarse, en físicos: hacia ahí apunta al llegar.</summary>
    public Point CentroPulsado { get; }

    public TimeSpan Duracion { get; }

    /// <param name="inicio">Dónde está la flecha ahora, en físicos.</param>
    /// <param name="centroPulsado">El centro de la caja que la mano dice haber pulsado (<c>UiaSurface.Pulso</c>), en físicos.</param>
    /// <param name="escala">La del monitor de llegada (DPI / 96).</param>
    /// <param name="appDelante">
    /// Si la ventana de trabajo es la de delante. Lo contesta Windows (<c>VistaDeJev</c> pregunta antes de cada
    /// vuelo) y no se da por hecho: detrás de otra ventana lo pulsado no se ve, y una flecha que señala lo que no
    /// se ve es la caja que miente (aprendizaje nº4).
    /// </param>
    /// <returns>El vuelo, o <c>null</c> si no hay que volar.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Con una escala que no es un número positivo: con 0 —un <c>GetDpiForMonitor</c> que falló— los 520 serían 0
    /// (duración infinita, acotada a 2,2 sin decir nada) y la flecha se posaría ENCIMA de lo pulsado. Es la misma
    /// guarda que <see cref="DondeVaElPanel.Calcular"/> y el conversor de <see cref="Pantallas.DelMonitor"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Con un punto sin coordenadas finitas: un NaN de UIA llegaría a <c>SetWindowPos</c> como un entero sin
    /// sentido y la flecha se iría de la pantalla en silencio (patrón nº9). <c>UiaSurface.Pulso</c> descarta las
    /// cajas de menos de 1 de lado, pero un NaN pasa esa comparación.
    /// </exception>
    public static PlanDeVuelo? Calcular(Point inicio, Point centroPulsado, double escala, bool appDelante)
    {
        if (!double.IsFinite(escala) || escala <= 0)
            throw new ArgumentOutOfRangeException(nameof(escala), escala,
                "la escala del monitor es un número positivo (DPI / 96): 0, NaN o negativa es un GetDpiForMonitor que falló");
        if (!EsFinito(inicio))
            throw new ArgumentException($"el inicio del vuelo no tiene coordenadas finitas: ({inicio.X}, {inicio.Y})", nameof(inicio));
        if (!EsFinito(centroPulsado))
            throw new ArgumentException($"el centro de lo pulsado no tiene coordenadas finitas: ({centroPulsado.X}, {centroPulsado.Y})", nameof(centroPulsado));

        if (!appDelante) return null;

        var hacia = centroPulsado - inicio;
        double distancia = hacia.Length;
        // Con la flecha ya encima de lo pulsado no hay dirección de llegada: se toma la de quien llega desde abajo a
        // la derecha, que es donde descansa la flecha (ratón + (35, 25), plano §Los tres modos).
        var llegada = distancia > 0 ? hacia / distancia : new Vector(-Math.Sqrt(0.5), -Math.Sqrt(0.5));
        var fin = centroPulsado - llegada * (Aterrizaje * escala);

        var camino = fin - inicio;
        double largo = camino.Length;
        Point control1 = inicio, control2 = fin;
        if (largo > 0)
        {
            var avance = camino / largo;
            var lado = new Vector(-avance.Y, avance.X);
            double sentido = inicio.X <= fin.X ? -1 : 1;
            double control = Math.Min(Math.Clamp(largo * ControlPorDistancia, ControlMinimo * escala, ControlMaximo * escala), largo / 2);
            double arco = Math.Min(Math.Clamp(largo * ArcoPorDistancia, ArcoMinimo * escala, ArcoMaximo * escala), largo / 2) * sentido;
            control1 = inicio + avance * control + lado * arco;
            control2 = fin - avance * control - lado * (arco * ArcoDeLlegada);
        }

        var duracion = TimeSpan.FromSeconds(Math.Clamp(distancia / (DipPorSegundo * escala), DuracionMinima, DuracionMaxima));
        return new PlanDeVuelo(inicio, fin, centroPulsado, control1, control2, duracion);
    }

    /// <summary>Dónde va la flecha a la fracción <paramref name="p"/> del vuelo (transcurrido / <see cref="Duracion"/>).</summary>
    /// <remarks>
    /// Fuera de [0, 1] se queda en los extremos: el último fotograma llega tarde y su p pasa de 1, y una Bézier
    /// evaluada ahí sigue la curva de largo y deja la flecha más allá de donde se posó.
    /// </remarks>
    public Point PosicionEn(double p)
    {
        double q = Math.Clamp(p, 0, 1);
        double t = q * q * q * (q * (q * 6 - 15) + 10);   // smootherstep: velocidad y aceleración 0 en los dos extremos
        double r = 1 - t;
        double a = r * r * r, b = 3 * r * r * t, c = 3 * r * t * t, d = t * t * t;
        return new Point(a * Inicio.X + b * _control1.X + c * _control2.X + d * Fin.X,
                         a * Inicio.Y + b * _control1.Y + c * _control2.Y + d * Fin.Y);
    }

    private static bool EsFinito(Point p) => double.IsFinite(p.X) && double.IsFinite(p.Y);
}
