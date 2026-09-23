namespace U.WindowsClient.Ui.Jev;

/// <summary>
/// LO QUE MIDE EL PANEL DE JEV, en DIP. Promesa 376 (spec 049), su parte pura: como el notch
/// (<see cref="MedidaDelNotch"/>, promesa 249), el alto sale de sus partes y de un mínimo declarado, nunca del
/// contenido.
/// </summary>
/// <remarks>
/// LAS MEDIDAS SON LAS DEL VÍDEO, no las de la fórmula del `main` de TipTour (`JevStepPanelView.height(for:)`, que
/// da 228 con cinco barras). Medido sobre los fotogramas a 3,040 px/pt en el mismo fotograma que el ancho de 340
/// (plano §Medidas y §XAML fila a fila): 63,7 sin resultados (t=11,5) y 198,8 con cinco barras (t=16,0), con los
/// centros de las barras a 103,5 + 20·k exactos. Todo el déficit frente a los 201 del código está ABAJO: el relleno
/// inferior del vídeo es 7,6, no 10.
///
/// EL MÍNIMO SE DECLARA, NO SE DEDUCE. Sin resultados las partes suman 57,6 y el vídeo mide 63,7: lo que lo sostiene
/// es el `MinHeight="64"` del `Border` del XAML (el `baseHeight = 64` de `TextCommandPanelManager.swift:28`), no una
/// fila. Hasta la revisión de la spec el 64 se atribuía a las partes y no cuadraba (§Revisiones, 2): por eso
/// <see cref="AltoDe"/> es <c>max(AltoMinimo, SumaDePartes)</c> y las dos cosas se juzgan por separado.
///
/// El XAML de la ventana (fase 8) tiene que medir lo mismo sin pantalla: esa es la otra mitad de la 376 y se juzga
/// con <c>XamlReader.Parse</c> + <c>Measure</c> en el arnés.
/// </remarks>
public static class MedidaDelPanelDeJev
{
    /// <summary>Como el notch y como el vídeo: 747 px = 340,0 pt (t=11,5).</summary>
    public const double Ancho = 340;

    /// <summary>El mínimo declarado: calca los 63,7 del vídeo y es el <c>MinHeight</c> del <c>Border</c>.</summary>
    public const double AltoMinimo = 64;

    /// <summary>Relleno a izquierda y derecha (<c>TextCommandPanelView.swift:62</c>).</summary>
    public const double PaddingHorizontal = 13;

    /// <summary>Relleno de arriba.</summary>
    public const double PaddingArriba = 10;

    /// <summary>Relleno de abajo: 7,6 para calcar el vídeo, no los 10 del código (con 10 saldrían 201).</summary>
    public const double PaddingAbajo = 7.6;

    /// <summary>
    /// Lo que recibe la zona de resultados: 340 − 2·13, entero. En el XAML el trazo va en un <c>Border</c> aparte y
    /// sin hijos, porque en WPF <c>BorderThickness</c> ocupa sitio y le quitaba 1,6 (medido: 312,4).
    /// </summary>
    public const double AnchoDeResultados = Ancho - 2 * PaddingHorizontal;

    /// <summary>Fila 1, la entrada (el glifo y el objetivo).</summary>
    public const double AltoDeLaEntrada = 18;

    /// <summary>Hueco entre la entrada y el ticker, y entre el ticker y los resultados.</summary>
    public const double HuecoEntreFilas = 8;

    /// <summary>Fila 2, el ticker.</summary>
    public const double AltoDelTicker = 14;

    /// <summary>El divisor que abre los resultados.</summary>
    public const double AltoDelDivisor = 1;

    /// <summary>El espaciado dentro de los resultados: antes de la cabecera, de los medidores y de cada barra.</summary>
    public const double EspacioDeResultados = 5;

    /// <summary>
    /// Cabecera y medidores: 11, el alto natural del vídeo, no los 13 fijos del código. Consolas 10 pide 11,71 en WPF
    /// (medido), así que el XAML los fija con <c>BlockLineHeight</c> o recortan.
    /// </summary>
    public const double AltoDeLaCabecera = 11;

    /// <summary>Ídem.</summary>
    public const double AltoDeLosMedidores = 11;

    /// <summary>Una barra: fila de 15, con paso de 20 contando su espacio.</summary>
    public const double AltoDeUnaBarra = 15;

    /// <summary>
    /// Lo que suman las partes con tantas barras, sin mínimo. Cero barras es «sin resultados»: el modelo del panel
    /// solo da resultados con distribución, y con distribución hay dos barras o más
    /// (<see cref="EstadoDeLaDecision"/>, promesa 373), así que no existe un panel con resultados y sin barras.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Con menos de cero barras o más de <see cref="EstadoDeLaDecision.BarrasComoMucho"/>. El tope sale de ahí y
    /// no se repite: un panel de seis barras no existe (372), y devolverle un alto sería un número que mide algo
    /// que nadie pinta. Lanzar dice cuál fue el dato; un alto inventado no diría nada (fase 4, visto rojo antes).
    /// </exception>
    public static double SumaDePartes(int barras)
    {
        if (barras < 0 || barras > EstadoDeLaDecision.BarrasComoMucho)
            throw new ArgumentOutOfRangeException(nameof(barras), barras,
                $"el panel de Jev pinta de 0 a {EstadoDeLaDecision.BarrasComoMucho} barras: con {barras} no hay alto que medir");
        double sinResultados = PaddingArriba + AltoDeLaEntrada + HuecoEntreFilas + AltoDelTicker + PaddingAbajo;
        if (barras == 0) return sinResultados;
        return sinResultados
             + HuecoEntreFilas + AltoDelDivisor
             + EspacioDeResultados + AltoDeLaCabecera
             + EspacioDeResultados + AltoDeLosMedidores
             + barras * (EspacioDeResultados + AltoDeUnaBarra);
    }

    /// <summary>El alto del panel: el de sus partes, y nunca menos que el mínimo declarado.</summary>
    public static double AltoDe(int barras) => Math.Max(AltoMinimo, SumaDePartes(barras));
}
