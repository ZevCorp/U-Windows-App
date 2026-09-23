using System.Windows;

namespace U.WindowsClient.Ui.Jev;

/// <summary>Una esquina del panel respecto de lo que la sitúa, en coordenadas de Windows (origen arriba a la izquierda).</summary>
public enum EsquinaDelPanel
{
    AbajoDerecha,
    ArribaDerecha,
    AbajoIzquierda,
    ArribaIzquierda,
}

/// <summary>Dónde va el panel.</summary>
/// <param name="Rect">El rect del panel, en físicos del escritorio virtual: lo que recibe <c>SetWindowPos</c>.</param>
/// <param name="Esquina">La esquina elegida.</param>
/// <param name="JuntoAlAncla">
/// Si la esquina es JUNTO AL ANCLA (a 56/32 de ella) o la del área de trabajo a la que se va cuando ninguna cabe.
/// «ArribaIzquierda» sola no distingue las dos, y la línea de log que diga dónde quedó el panel tiene que poder
/// hacerlo (aprendizaje nº2).
/// </param>
public readonly record struct LugarDelPanel(Rect Rect, EsquinaDelPanel Esquina, bool JuntoAlAncla);

/// <summary>
/// DÓNDE VA EL PANEL DE JEV, calculado sin pantalla. Promesa 377 (spec 049); y la 385 lo llama con la caja de lo
/// pulsado como <c>objetivo</c> para que el panel se aparte en cuanto la conoce.
/// </summary>
/// <remarks>
/// TODO EN FÍSICOS del escritorio virtual: el ancla, el tamaño del panel (ya multiplicado por la escala), el área de
/// trabajo (<c>rcWork</c>), los obstáculos y lo pulsado (<c>UiaSurface.Pulso</c> avisa en físicos). Las longitudes
/// del plano (56, 32, 44, 12, 8) son DIP y se multiplican aquí por la escala del monitor que manda; así no hace
/// falta convertir nada a mano (380).
///
/// EL NOTCH ES UN OBSTÁCULO MÁS: quien llame le pasa el rect de <see cref="ReglaDeLaBandeja.ArribaAlCentro"/>, el
/// mismo que el notch usa para ponerse, y no uno calculado aparte (aprendizaje nº16: comparar por el mismo camino).
///
/// EL ORDEN DE LAS ESQUINAS ES EL DEL PLANO, leído en <c>TextCommandPanelManager.swift:194-240</c> y dado la
/// vuelta en vertical porque AppKit tiene el origen abajo: abajo a la derecha, arriba a la derecha, abajo a la
/// izquierda, arriba a la izquierda. La primera que cabe gana; no se busca «la mejor».
/// </remarks>
public static class DondeVaElPanel
{
    /// <summary>Desplazamiento horizontal del panel respecto del ancla, en DIP.</summary>
    public const double DesplazamientoX = 56;

    /// <summary>Desplazamiento vertical del panel respecto del ancla, en DIP.</summary>
    public const double DesplazamientoY = 32;

    /// <summary>Lado del cuadrado alrededor del ancla que el panel no tapa, en DIP.</summary>
    public const double Holgura = 44;

    /// <summary>Margen al borde del área de trabajo, en DIP.</summary>
    public const double MargenAlBorde = 12;

    /// <summary>Cuánto se infla la caja de lo pulsado antes de esquivarla, en DIP.</summary>
    public const double InflarLoPulsado = 8;

    private static readonly EsquinaDelPanel[] EnOrden =
    {
        EsquinaDelPanel.AbajoDerecha, EsquinaDelPanel.ArribaDerecha, EsquinaDelPanel.AbajoIzquierda, EsquinaDelPanel.ArribaIzquierda,
    };

    /// <param name="ancla">Lo que sitúa el panel (la carita o el cursor), en físicos.</param>
    /// <param name="tamaño">El panel en físicos: <see cref="MedidaDelPanelDeJev"/> por la escala.</param>
    /// <param name="rcWork">El área de trabajo del monitor, en físicos.</param>
    /// <param name="escala">La del monitor que manda (DPI / 96).</param>
    /// <param name="obstaculos">Lo que no se cruza si alguna esquina cabe: el notch, y lo que venga.</param>
    /// <param name="objetivo">La caja de lo pulsado, o <c>null</c> mientras no se conozca.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Con una escala que no es un número positivo. Con 0 —un <c>GetDpiForMonitor</c> que falló— los 56/32/44/12
    /// valdrían 0 y el panel caería ENCIMA del ancla diciendo que cabe (patrón nº9). No hay sitio que calcular.
    /// </exception>
    public static LugarDelPanel Calcular(Point ancla, Size tamaño, Rect rcWork, double escala, IReadOnlyList<Rect> obstaculos, Rect? objetivo)
    {
        if (!double.IsFinite(escala) || escala <= 0)
            throw new ArgumentOutOfRangeException(nameof(escala), escala,
                "la escala del monitor es un número positivo (DPI / 96): 0, NaN o negativa es un GetDpiForMonitor que falló");
        ArgumentNullException.ThrowIfNull(obstaculos);

        double dx = DesplazamientoX * escala, dy = DesplazamientoY * escala, margen = MargenAlBorde * escala;
        var dondeCabe = Encogido(rcWork, margen);
        var cuadrado = new Rect(ancla.X - Holgura * escala / 2, ancla.Y - Holgura * escala / 2, Holgura * escala, Holgura * escala);
        Rect? pulsado = objetivo is { IsEmpty: false } o ? Rect.Inflate(o, InflarLoPulsado * escala, InflarLoPulsado * escala) : null;

        foreach (var esquina in EnOrden)
        {
            var r = JuntoAl(ancla, tamaño, esquina, dx, dy);
            if (dondeCabe.Contains(r) && !r.IntersectsWith(cuadrado)
                && !obstaculos.Any(ob => r.IntersectsWith(ob))
                && !(pulsado is { } p && r.IntersectsWith(p)))
                return new LugarDelPanel(r, esquina, JuntoAlAncla: true);
        }

        // NINGUNA CABE: a la esquina del área de trabajo OPUESTA a lo pulsado (al ancla, si aún no se conoce).
        // Se ordenan por distancia, de más lejos a más cerca, y eso no es otra regla: las cuatro son simétricas
        // respecto del centro del área y la suma de cuadrados se separa por ejes, así que la más lejana ES la
        // opuesta. Lo que el orden añade es la siguiente: con el ancla metida en esa misma esquina, la opuesta
        // taparía el cuadrado de 44 —que vale SIEMPRE—, y se pasa a la más lejana que no lo tape (fase 4, caso
        // visto rojo antes que su código: el ancla en (100, 100) y lo pulsado ocupando el resto).
        var lejosDe = objetivo is { IsEmpty: false } ob2 ? Centro(ob2) : ancla;
        var delArea = EnOrden
            .Select(e => new LugarDelPanel(EnLaEsquinaDe(dondeCabe, tamaño, e), e, JuntoAlAncla: false))
            .OrderByDescending(l => DistanciaAlCuadrado(Centro(l.Rect), lejosDe))
            .ToList();
        // Si las cuatro tapan el ancla es que el área no da para el panel y el cuadrado a la vez: se queda la
        // opuesta, y el cuadrado tapado lo ve quien lea el rect; inventar un quinto sitio sería peor.
        return delArea.FirstOrDefault(l => !l.Rect.IntersectsWith(cuadrado), delArea[0]);
    }

    /// <summary>
    /// EL NOTCH QUE EL PANEL ESQUIVA, en físicos: el MISMO rect con que se pone el notch —<see cref="ReglaDeLaBandeja.ArribaAlCentro"/>
    /// sobre el área libre y con <see cref="MedidaDelNotch"/>, que es lo que hace <c>PanelDeAcciones.Recolocar</c>—
    /// llevado a físicos por el conversor del primario (380). No se recalcula el sitio del notch por otro camino:
    /// dos cálculos del mismo sitio acaban discrepando en silencio (aprendizaje nº16).
    /// </summary>
    /// <param name="libre">El área libre del primario en DIP, la de <c>LaBarraDeTareas.Mirar()</c>: la que usa el notch.</param>
    /// <param name="primario">El conversor del monitor primario (<see cref="Pantallas.DelMonitor"/>), que es donde vive el notch.</param>
    public static Rect NotchEnFisicos(Rect libre, ConversorDeMonitor primario)
    {
        ArgumentNullException.ThrowIfNull(primario);
        var enDip = ReglaDeLaBandeja.ArribaAlCentro(libre, new Size(MedidaDelNotch.Ancho, MedidaDelNotch.Alto));
        var arribaIzquierda = primario.AFisico(enDip.TopLeft);
        return new Rect(arribaIzquierda.X, arribaIzquierda.Y, enDip.Width * primario.Escala, enDip.Height * primario.Escala);
    }

    private static Rect JuntoAl(Point ancla, Size tamaño, EsquinaDelPanel esquina, double dx, double dy)
    {
        bool derecha = esquina is EsquinaDelPanel.AbajoDerecha or EsquinaDelPanel.ArribaDerecha;
        bool abajo = esquina is EsquinaDelPanel.AbajoDerecha or EsquinaDelPanel.AbajoIzquierda;
        double x = derecha ? ancla.X + dx : ancla.X - dx - tamaño.Width;
        double y = abajo ? ancla.Y + dy : ancla.Y - dy - tamaño.Height;
        return new Rect(x, y, tamaño.Width, tamaño.Height);
    }

    private static Rect EnLaEsquinaDe(Rect area, Size tamaño, EsquinaDelPanel esquina)
    {
        bool derecha = esquina is EsquinaDelPanel.AbajoDerecha or EsquinaDelPanel.ArribaDerecha;
        bool abajo = esquina is EsquinaDelPanel.AbajoDerecha or EsquinaDelPanel.AbajoIzquierda;
        double x = derecha ? area.Right - tamaño.Width : area.Left;
        double y = abajo ? area.Bottom - tamaño.Height : area.Top;
        return new Rect(x, y, tamaño.Width, tamaño.Height);
    }

    private static Point Centro(Rect r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);

    private static double DistanciaAlCuadrado(Point a, Point b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);

    private static Rect Encogido(Rect r, double margen) =>
        new(r.X + margen, r.Y + margen, Math.Max(0, r.Width - 2 * margen), Math.Max(0, r.Height - 2 * margen));
}
