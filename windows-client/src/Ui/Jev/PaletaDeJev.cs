namespace U.WindowsClient.Ui.Jev;

/// <summary>Un color de la vista de Jev: cómo se llama, qué ARGB pinta y qué significa.</summary>
/// <remarks>
/// EL SIGNIFICADO VA DENTRO DEL TOKEN, y no en un comentario, porque es lo que la promesa 371 juzga:
/// dos colores cromáticos que se parecen solo pueden convivir si significan lo mismo o si alguien
/// escribió por qué se acepta el parecido. Un significado en un comentario no lo lee ningún juez.
/// </remarks>
public sealed record TokenDeJev(string Nombre, uint Argb, string Significado)
{
    /// <summary>¿Tiene tono que se vea? Saturación HSL de 0,25 o más (promesa 371).</summary>
    public bool EsCromatico => PaletaDeJev.SaturacionHsl(Argb) >= PaletaDeJev.UmbralCromatico;
}

/// <summary>Un par de tokens cromáticos que distan menos de 30° de tono y se acepta, con su porqué.</summary>
public sealed record ChoqueAceptado(string A, string B, string Porque);

/// <summary>
/// LA PALETA DE JEV ES LA DEL PLANO, y se juzga sin pincel. Promesa 371 (spec 049). Pura: solo
/// números y texto, ningún <c>Brush</c>, para que el contrato la lea sin levantar WPF — igual que
/// <see cref="PaletaDelNotch"/>.
/// </summary>
/// <remarks>
/// LOS 16 ARGB SON LOS DEL CÓDIGO DE TIPTOUR (<c>DesignSystem.swift</c>, <c>DetectionOverlayView.swift</c>,
/// <c>5258246</c>), con el alfa ya multiplicado, tal como los lista <c>plano-para-wpf.md</c> §Tokens. No
/// son los del vídeo: el píxel medido sale desplazado por la mezcla alfa, el 4:2:0 de H.264 y la gestión
/// de color. Hay una desviación, y es deliberada: <see cref="FondoDelPanel"/> es OPACO (<c>0xFF</c>) y no
/// el 0,96 del vídeo, porque en WPF con <c>AllowsTransparency</c> el ClearType solo sobrevive sobre fondo
/// opaco (<c>Estudio.cs</c>, <c>Nitida()</c>). Sobre el negro de detrás son un 4 % de diferencia.
///
/// OSCURO EN UNA APP CLARA, Y NO ES UNA CONTRADICCIÓN. El lienzo de Ü es blanco desde el 2026-09-06
/// (<c>Estudio.cs</c>), pero esa doctrina es la de las VENTANAS de la aplicación, que flotan sobre su
/// propio lienzo. El panel de Jev flota sobre el escritorio de la persona, junto al notch, que es negro
/// por la promesa 242; la spec 049 lo quiere oscuro como el vídeo y opaco como el notch. Contraste sobre
/// <see cref="FondoDelPanel"/>, medido con la fórmula de WCAG el 2026-09-22: <see cref="TextoPrimario"/>
/// 16,1:1, <see cref="TextoSecundario"/> 9,0:1, <see cref="Placeholder"/> 6,2:1 y
/// <see cref="TextoTerciario"/> **3,9:1 — por debajo del 4,5:1 de AA para texto normal**. El plano lo usa
/// para metadatos de 9–10 DIP («· N detectados», ms, $). Se deja el ARGB del plano porque la 371 lo fija
/// y el cambio es del dueño, no de esta fase; queda dicho aquí para que no se descubra en el PC real.
///
/// CROMÁTICO ES SATURACIÓN ≥ 0,25, y la frontera no es estética. Los seis grises del plano viven a
/// 150–157,5° de tono —a 0,6–8,1° de <see cref="Cumplido"/>— con saturación 0,01–0,06: exigirles 30° de
/// distancia daba 24 pares «en choque» que ningún ojo distingue como verdes (revisión del 2026-09-22).
/// Un gris se juzga por su valor; un color, por su tono.
///
/// DENTRO DE LA PALETA, TRES CHOQUES ACEPTADOS: los azules entre sí (3,7–8,1°). Ver <see cref="ChoquesAceptados"/>.
///
/// CON OTRAS PALETAS DE Ü HAY TRES MÁS, y no van en <see cref="ChoquesAceptados"/> porque no se resuelven
/// aceptándolos sino no enseñándolos juntos (<see cref="ExclusionConElInspector"/>), medidos el 2026-09-22:
/// <list type="bullet">
/// <item><see cref="Ausente"/> ≈ ámbar de «shell sin mapear» del inspector (<c>0xFFA51F</c>): <b>3,0°</b>.</item>
/// <item><see cref="Candidata"/> ≈ verde de «shell mapeado» del inspector (<c>0x3FBF6F</c>): <b>25,4°</b>; y
/// ≈ <c>UiPalette.Vivo</c> (<c>0x2FB457</c>): 20,9°.</item>
/// <item><see cref="Elegida"/> ≈ <c>UiPalette.Fallo</c> (<c>0xFF3B30</c>): <b>14,9°</b>.</item>
/// </list>
///
/// NO HAY CIAN. TipTour pinta en cian las cajas del OCR; Ü no tiene OCR, y un token para lo que no
/// existe acaba pintando algo que no significa nada. La 371 exige que no aparezca.
/// </remarks>
public static class PaletaDeJev
{
    // ── el panel ─────────────────────────────────────────────────────────────

    /// <summary>El relleno del panel. Opaco a propósito: ver las notas de la clase.</summary>
    public const uint FondoDelPanel = 0xFF101211;

    /// <summary>El trazo de 0,8 del panel.</summary>
    public const uint BordeDelPanel = 0xB8373B39;

    /// <summary>La etiqueta y el valor de la barra resaltada.</summary>
    public const uint TextoPrimario = 0xFFECEEED;

    /// <summary>Ticker, «paso k», valores de los medidores, etiquetas no resaltadas; y su punto, con alfa.</summary>
    public const uint TextoSecundario = 0xFFADB5B2;

    /// <summary>Glifo, «· N detectados», ms, $, títulos de los medidores, valores no resaltados.</summary>
    public const uint TextoTerciario = 0xFF6B736F;

    /// <summary>El texto de un campo vacío.</summary>
    /// <remarks>
    /// El panel de la 049 NO tiene campo de texto (spec 049 §Lo que NO entra: el objetivo llega por la
    /// voz). El token está porque el plano lo mide —el vídeo enseña «Pídele algo a Ü…» a <c>#9A9C9C</c>, que
    /// no es el terciario— y la spec que traiga el campo lo encontrará medido en vez de adivinarlo.
    /// </remarks>
    public const uint Placeholder = 0xFF939594;

    // ── las barras y los medidores ───────────────────────────────────────────

    /// <summary>El relleno de la barra resaltada: la pulsada, o la elegida mientras no se sepa.</summary>
    public const uint BarraElegida = 0xFF60A5FA;

    /// <summary>El relleno de las demás barras.</summary>
    public const uint BarraNoElegida = 0x8C2563EB;

    /// <summary>La cápsula de fondo de barras y medidores. Blanco al 5 %: sin tono.</summary>
    public const uint Pista = 0x0DFFFFFF;

    /// <summary>El medidor «cumplido».</summary>
    public const uint Cumplido = 0xE634D399;

    /// <summary>El medidor «ausente».</summary>
    public const uint Ausente = 0xE6FFB224;

    // ── fuera del panel ──────────────────────────────────────────────────────

    /// <summary>El trazo de la flecha, sus halos y su píldora.</summary>
    public const uint AzulDelCursor = 0xFF4F8EF7;

    /// <summary>La caja de una candidata que se le ofreció al decisor, con caja leída.</summary>
    /// <remarks>El verde de TipTour, que allí era lo que un YOLO adivinaba; en Ü, lo que UIA o SAP declaran.</remarks>
    public const uint Candidata = 0x9438FF2E;

    /// <summary>La caja de LO PULSADO.</summary>
    /// <remarks>
    /// Conserva el nombre del plano, pero en Ü no marca la elegida: marca lo que la mano pulsó, que puede
    /// ser la segunda mejor (288). Resaltar la elegida mientras la mano pulsa otra es la caja que miente
    /// (aprendizaje nº4). Spec 049 §Nota de nombres.
    /// </remarks>
    public const uint Elegida = 0xE6FF476B;

    /// <summary>La cápsula de la etiqueta de una caja.</summary>
    public const uint FondoDeEtiqueta = 0x7A000000;

    /// <summary>La cápsula de la etiqueta de lo pulsado: el mismo negro, más denso.</summary>
    public const uint FondoDeEtiquetaElegida = 0xB8000000;

    /// <summary>Desde qué saturación HSL un color cuenta como cromático.</summary>
    public const double UmbralCromatico = 0.25;

    /// <summary>Cuántos grados de tono separan, como mínimo, dos cromáticos con distinto significado.</summary>
    public const double DistanciaMinimaDeTono = 30;

    /// <summary>Los 16 tokens del plano, cada uno con lo que significa. Ni uno más ni uno menos.</summary>
    public static IReadOnlyList<TokenDeJev> Todos { get; } = new TokenDeJev[]
    {
        new(nameof(FondoDelPanel), FondoDelPanel, "el fondo del panel de Jev"),
        new(nameof(BordeDelPanel), BordeDelPanel, "el canto del panel de Jev"),
        new(nameof(TextoPrimario), TextoPrimario, "el texto de lo resaltado en el panel"),
        new(nameof(TextoSecundario), TextoSecundario, "el texto de lo que pasa ahora: ticker, paso, valores"),
        new(nameof(TextoTerciario), TextoTerciario, "el texto de los metadatos: detectados, ms, coste, rótulos"),
        new(nameof(Placeholder), Placeholder, "el texto de un campo vacío"),
        new(nameof(BarraElegida), BarraElegida, "la probabilidad de la candidata resaltada en el panel"),
        new(nameof(BarraNoElegida), BarraNoElegida, "la probabilidad de las demás candidatas en el panel"),
        new(nameof(Pista), Pista, "el fondo de una barra o un medidor: el cien por cien que no se llenó"),
        new(nameof(Cumplido), Cumplido, "cuánto cree Jev que el objetivo ya está"),
        new(nameof(Ausente), Ausente, "cuánto cree Jev que lo que busca no está en la pantalla"),
        new(nameof(AzulDelCursor), AzulDelCursor, "la flecha de Jev volando a lo pulsado, fuera del panel"),
        new(nameof(Candidata), Candidata, "una candidata que se le ofreció al decisor y cuya caja se leyó"),
        new(nameof(Elegida), Elegida, "lo pulsado: la caja que la mano pulsó"),
        new(nameof(FondoDeEtiqueta), FondoDeEtiqueta, "el fondo de la etiqueta de una candidata"),
        new(nameof(FondoDeEtiquetaElegida), FondoDeEtiquetaElegida, "el fondo de la etiqueta de lo pulsado"),
    };

    /// <summary>
    /// Los pares cromáticos con distinto significado que distan menos de 30° y se aceptan por escrito.
    /// Son exactamente los tres azules entre sí, medidos el 2026-09-22: <see cref="BarraElegida"/>–
    /// <see cref="BarraNoElegida"/> 8,1°, <see cref="BarraElegida"/>–<see cref="AzulDelCursor"/> 4,4° y
    /// <see cref="BarraNoElegida"/>–<see cref="AzulDelCursor"/> 3,7°.
    /// </summary>
    public static IReadOnlyList<ChoqueAceptado> ChoquesAceptados { get; } = new ChoqueAceptado[]
    {
        new(nameof(BarraElegida), nameof(BarraNoElegida), PorqueDeLosAzules),
        new(nameof(BarraElegida), nameof(AzulDelCursor), PorqueDeLosAzules),
        new(nameof(BarraNoElegida), nameof(AzulDelCursor), PorqueDeLosAzules),
    };

    private const string PorqueDeLosAzules =
        "los tres son Jev trabajando; se distinguen por alfa y por sitio: dos viven dentro del panel y uno fuera";

    /// <summary>La saturación HSL (0–1) del color, sin mirar el alfa.</summary>
    public static double SaturacionHsl(uint argb) => Hsl(argb).S;

    /// <summary>
    /// Los grados de tono (0–180) entre dos colores, por el camino corto del círculo. <see cref="double.NaN"/>
    /// si alguno no tiene tono (un gris exacto, un blanco, un negro): NO TENER TONO NO ES TENER TONO 0, que
    /// es el rojo, y un 0 aquí mediría la distancia de un negro al rojo (patrón nº9, vacío no es ausente).
    /// </summary>
    public static double DistanciaDeTono(uint a, uint b)
    {
        var (ha, sa, _) = Hsl(a);
        var (hb, sb, _) = Hsl(b);
        if (sa == 0 || sb == 0) return double.NaN;
        double d = Math.Abs(ha - hb);
        return Math.Min(d, 360 - d);
    }

    private static (double H, double S, double L) Hsl(uint argb)
    {
        double r = ((argb >> 16) & 0xFF) / 255.0, g = ((argb >> 8) & 0xFF) / 255.0, b = (argb & 0xFF) / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double d = max - min, l = (max + min) / 2;
        if (d == 0) return (0, 0, l);
        double s = d / (1 - Math.Abs(2 * l - 1));
        double h = max == r ? ((g - b) / d) % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        h *= 60;
        if (h < 0) h += 360;
        return (h, s, l);
    }
}
