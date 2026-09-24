using System.Windows;
using System.Windows.Media;

namespace U.WindowsClient.Ui;

/// <summary>
/// DE LA PANTALLA A UNA VENTANA: la única respuesta, en toda la app, a «¿dónde cae este punto de
/// la pantalla dentro de mi lienzo?».
///
/// La pregunta parece trivial y no lo es, y por eso existe este archivo. Había dos conversiones
/// escritas a mano —la de los puntos sobre los elementos y la de los recuadros que los resaltan— y
/// las dos hacían lo mismo mal: aplicaban <c>TransformFromDevice</c> (que corrige el DPI) y NO
/// restaban dónde está la ventana. Eso funciona exactamente mientras la ventana viva pegada al
/// origen de la pantalla principal, que era el caso… hasta que la capa se pudo mudar al segundo
/// monitor. Entonces los puntos se dibujaban desplazados una pantalla entera: se calculaban con lo
/// que había en la pantalla 1 y se pintaban en la 2 (2026-08-09, visto por el usuario).
///
/// La forma correcta es <see cref="Visual.PointFromScreen"/>: convierte coordenadas de PANTALLA al
/// sistema del propio visual, y resuelve de una vez las tres cosas que hay que acertar a la vez —
/// dónde está la ventana, el DPI de ESE monitor, y el escalado del visual. Escribirlo a mano exige
/// acertar las tres; llamarlo exige acertar cero.
///
/// Y va en un archivo aparte a propósito: en cuanto una app puede vivir en más de un monitor, esta
/// conversión deja de ser un detalle de dibujo y pasa a ser una regla del sistema. Una pregunta,
/// una respuesta, un sitio.
/// </summary>
public static class Pantallas
{
    /// <summary>
    /// Un punto de la pantalla (píxeles físicos, como los da UIA) llevado al sistema de
    /// coordenadas de <paramref name="destino"/>.
    ///
    /// Si el visual todavía no está conectado a una ventana viva, <c>PointFromScreen</c> no puede
    /// contestar: se cae al valor sin transformar, que es lo que había antes y no empeora nada.
    /// </summary>
    public static Point AlVisual(Visual destino, double xPantalla, double yPantalla)
    {
        try
        {
            if (PresentationSource.FromVisual(destino) != null)
                return destino.PointFromScreen(new Point(xPantalla, yPantalla));
        }
        catch { }
        return new Point(xPantalla, yPantalla);
    }

    /// <summary>Un rectángulo de la pantalla llevado al sistema del visual. Se convierten las DOS
    /// esquinas y no una más el tamaño: con dos monitores de escala distinta, ancho y alto no se
    /// pueden escalar por separado sin que el recuadro deje de encajar con lo que rodea.</summary>
    public static Rect AlVisual(Visual destino, Rect pantalla)
    {
        var tl = AlVisual(destino, pantalla.X, pantalla.Y);
        var br = AlVisual(destino, pantalla.Right, pantalla.Bottom);
        return new Rect(tl.X, tl.Y, Math.Max(0, br.X - tl.X), Math.Max(0, br.Y - tl.Y));
    }

    /// <summary>
    /// El conversor de UN MONITOR CONCRETO, sin ventana: de píxeles físicos del escritorio virtual a la unidad de
    /// ese monitor (DIP con origen en su esquina) y vuelta. Promesa 380 (spec 049).
    ///
    /// Lo de arriba necesita un visual conectado; las ventanas de Jev se ponen con <c>SetWindowPos</c> en físicos
    /// ANTES de existir en pantalla, y el overlay es una por monitor, así que la pregunta que tienen es otra:
    /// «¿dónde cae este físico dentro de ESTE monitor?». Se contesta restando su origen y dividiendo por SU escala;
    /// las siete conversiones a mano de hoy solo valen en la primaria (spec 049 §Diagnóstico).
    /// </summary>
    /// <param name="rcMonitorFisico">El <c>rcMonitor</c> de <c>GetMonitorInfo</c>, en físicos.</param>
    /// <param name="escala">La de <c>GetDpiForMonitor</c> / 96 para ese monitor.</param>
    public static ConversorDeMonitor DelMonitor(Rect rcMonitorFisico, double escala) =>
        new(rcMonitorFisico.TopLeft, escala);
}

/// <summary>De físicos a la unidad de un monitor, y vuelta. Nace de <see cref="Pantallas.DelMonitor"/>.</summary>
public sealed class ConversorDeMonitor
{
    /// <exception cref="ArgumentOutOfRangeException">
    /// Con una escala que no es un número positivo. Un <c>GetDpiForMonitor</c> que falla deja 0 (patrón nº9: vacío
    /// no es ausente), y con 0 la inversa da infinitos y la ida los devuelve como NaN: un conversor así no
    /// convierte nada y lo haría en silencio. No nace, y el mensaje dice el valor que llegó.
    /// </exception>
    internal ConversorDeMonitor(Point origen, double escala)
    {
        if (!double.IsFinite(escala) || escala <= 0)
            throw new ArgumentOutOfRangeException(nameof(escala), escala,
                "la escala de un monitor es un número positivo (DPI / 96): 0, NaN o negativa es un GetDpiForMonitor que falló");
        Origen = origen; Escala = escala;
    }

    public Point Origen { get; }

    public double Escala { get; }

    public Point AUnidad(Point fisico) => new((fisico.X - Origen.X) / Escala, (fisico.Y - Origen.Y) / Escala);

    public Point AFisico(Point unidad) => new(Origen.X + unidad.X * Escala, Origen.Y + unidad.Y * Escala);
}
