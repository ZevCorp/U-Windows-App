using System.Windows;

namespace U.WindowsClient.Ui.Jev;

/// <summary>Una caja que el overlay pinta: exactamente esto, y la ventana no decide nada más.</summary>
/// <param name="Id">El id de la candidata tal como se ofreció; vacío si la caja es solo la de la mano.</param>
/// <param name="Etiqueta">La etiqueta sola, sin «n) » ni tipo; vacía si la caja es solo la de la mano.</param>
/// <param name="Caja">La geometría en físicos, tal cual llegó: el modelo no la retoca.</param>
/// <param name="EsLeida">Geometría LEÍDA (lleva punto) o ESTIMADA (no lo lleva): aprendizaje nº4.</param>
/// <param name="Rosa">Si es lo pulsado. Se pinta con <see cref="PaletaDeJev.Elegida"/>, que conserva su nombre del plano.</param>
public sealed record CajaDeJev(string Id, string Etiqueta, Rect Caja, bool EsLeida, bool Rosa);

/// <summary>
/// EL OVERLAY DE JEV SALE DE AQUÍ. Promesa 375 (spec 049). Dada la lista que se le ofreció al decisor, las
/// cajas que se pintan; la ventana (<c>OverlayDeJev</c>, fase 8) solo dibuja lo que esto devuelve.
/// </summary>
/// <remarks>
/// LO QUE SE PINTA ES EXACTAMENTE LO QUE SE OFRECIÓ (285, aprendizaje nº16): una caja por candidata con
/// geometría que se pueda pintar, en su orden, y ninguna más. Una candidata sin caja se ofrece al decisor pero
/// no se pinta —una caja inventada es la caja que miente, aprendizaje nº4— y se cuenta en <see cref="SinCaja"/>,
/// para que el panel pueda decir cuántas no se ven en vez de callarlo.
///
/// LA ROSA ES LO PULSADO, NUNCA LO ELEGIDO. La elegida es la que devolvió el decisor; la pulsada, la que la mano
/// pulsó, que puede ser la segunda mejor (<c>SurfaceMapTools.cs:304-309</c>). Por eso este modelo ni siquiera
/// recibe la decisión: sin pulsada conocida no hay rosa, y la elegida se marca solo en el panel, como elegida
/// (<see cref="EstadoDeLaDecision"/>). La rosa llega por dos caminos que ya existen: el id —su número viene con
/// el paso del evento de la 048, <c>Paso.Numero</c>; hasta el 2026-09-24 se leía de la línea de progreso— y la caja
/// que la mano dice haber pulsado (<c>UiaSurface.Pulso</c>). Nunca por la etiqueta: en SAP hay dos «Buscar» en la
/// misma pantalla.
///
/// INMUTABLE Y PURO: cada operación devuelve un modelo nuevo. Sin reloj, sin pantalla y sin Windows, así que el
/// contrato lo juzga entero en un segundo; el gancho que dispara <see cref="Caducar"/> (cambió la ventana de
/// delante) es de la ventana y se lee en su fuente (375, parte (b), fase 8).
/// </remarks>
public sealed class CajasDelOverlay
{
    /// <summary>
    /// Con este lado o menos, en ancho o en alto, una caja no se pinta: el paso 1 de «una caja» del plano
    /// (<c>DetectionOverlayView.swift:47-132</c>, «descartar si mide ≤ 2»). Se aplica AQUÍ y no en la ventana,
    /// para que lo que se cuenta como pintado sea lo que de verdad se ve.
    /// </summary>
    public const double LadoMinimo = 2;

    /// <summary>
    /// Cuánto tiene que calzar la caja de la mano con la de una candidata —intersección sobre unión— para que
    /// sea ella. 0,6 es el umbral con que TipTour reencuentra su elegido entre dos lecturas (plano §La máquina
    /// de TipTour, que cita <c>LocalTargetContinuity.swift:5-20</c>): tomado del plano, NO medido en Ü. Cuando
    /// el nivel 4 traiga cajas de C y de la mano del mismo elemento, se mide cuánto calzan de verdad.
    /// </summary>
    /// <remarks>
    /// CONTENCIÓN NO ES ALINEACIÓN (patrón nº7): con «el centro de la mano cae dentro», un formulario que
    /// contiene el botón pulsado se volvería rosa, y dos cajas que se tocan se disputarían el mismo clic. La
    /// intersección sobre unión exige que las dos cajas sean la misma, con unos píxeles de holgura.
    /// </remarks>
    public const double CalceMinimo = 0.6;

    /// <summary>El overlay sin nada: el que queda al caducar.</summary>
    public static CajasDelOverlay Vacio { get; } = new(Array.Empty<CajaDeJev>(), null, 0);

    private readonly IReadOnlyList<CajaDeJev> _deLasCandidatas;
    private readonly CajaDeJev? _soloDeLaMano;

    private CajasDelOverlay(IReadOnlyList<CajaDeJev> deLasCandidatas, CajaDeJev? soloDeLaMano, int sinCaja)
    {
        _deLasCandidatas = deLasCandidatas;
        _soloDeLaMano = soloDeLaMano;
        SinCaja = sinCaja;
        Cajas = soloDeLaMano == null ? deLasCandidatas : deLasCandidatas.Append(soloDeLaMano).ToList();
    }

    /// <summary>
    /// Lo que se pinta, de abajo arriba: las de las candidatas en el orden en que se ofrecieron y, si la mano
    /// pulsó algo que no calza con ninguna, su caja sola al final. Rosa hay una como mucho.
    /// </summary>
    public IReadOnlyList<CajaDeJev> Cajas { get; }

    /// <summary>Cuántas candidatas se ofrecieron sin una caja que se pueda pintar: se cuentan, no se pintan.</summary>
    public int SinCaja { get; }

    /// <summary>Las cajas de las candidatas, con la rosa en la pulsada si se sabe cuál es.</summary>
    /// <param name="candidatas">La lista tal como se le ofreció al decisor.</param>
    /// <param name="pulsadaId">
    /// La pulsada: su id entero («6) Buscar (Button)») o su número («6», como lo lleva
    /// <see cref="CicloDeJev.Pulsada"/>); <c>null</c> o en blanco si todavía no se sabe.
    /// </param>
    public static CajasDelOverlay De(IReadOnlyList<CandidataDeJev>? candidatas, string? pulsadaId)
    {
        // VACÍO NO ES AUSENTE (patrón nº9): una pulsada en blanco es que no se sabe cuál fue.
        string? pulsada = string.IsNullOrWhiteSpace(pulsadaId) ? null : pulsadaId;
        var lista = candidatas ?? Array.Empty<CandidataDeJev>();
        var cajas = new List<CajaDeJev>(lista.Count);
        int sinCaja = 0;
        foreach (var c in lista)
        {
            if (c.Caja is not Rect caja || !EsPintable(caja)) { sinCaja++; continue; }
            cajas.Add(new CajaDeJev(c.Id, c.Etiqueta, caja, c.EsLeida, pulsada != null && EsLaPulsada(c.Id, pulsada)));
        }
        return new CajasDelOverlay(cajas, null, sinCaja);
    }

    /// <summary>
    /// Las cajas que pinta un ciclo, o <c>null</c> si el ciclo no toca el overlay. El decidido las pinta siempre —también
    /// vacías: un paso sin nada accionable deja el overlay vacío—; una línea, solo si viene de un paso
    /// (<see cref="CicloDeJev.DeUnPaso"/>), y entonces las mismas que su decidido, también ninguna; «Mirando», «Eligiendo» y
    /// una línea sin paso detrás, ninguna.
    /// </summary>
    /// <remarks>
    /// LA LÍNEA PINTA PORQUE LLEGA PEGADA AL EVENTO (382, al juntar C y D, 2026-09-24). El evento de la 048 sale cuando el
    /// paso terminó, y el tramo escribe su línea en el acto: el conector pinta solo el último ciclo encolado, y casi siempre
    /// es la línea. Con el puente, el decidido salía al decidir y la línea cientos de ms después, y bastaba con que pintara
    /// el decidido.
    ///
    /// LO QUE HACE A UNA LÍNEA «DE UN PASO» ES VENIR DE UN DECIDIDO, no llevar decisión o candidatas (revisión de la fusión,
    /// 2026-09-24). Con ese criterio, un paso sin nada accionable —ni decisión ni candidatas— tenía una línea que no se
    /// reconocía como suya: llegaba pegada, se pintaba solo ella, y el overlay se quedaba con las cajas del paso anterior,
    /// la caja que miente (patrón nº8). El comentario lo daba por «límite dicho»; era uno de los cuatro estados que la
    /// vista tiene que pintar bien.
    /// </remarks>
    public static CajasDelOverlay? DelCiclo(CicloDeJev ciclo)
    {
        ArgumentNullException.ThrowIfNull(ciclo);
        bool llevaUnPaso = ciclo.Fase == FaseDelCiclo.Decidido || (ciclo.Fase == FaseDelCiclo.Linea && ciclo.DeUnPaso);
        return llevaUnPaso ? De(ciclo.Candidatas, ciclo.Pulsada) : null;
    }

    /// <summary>
    /// La mano dice haber pulsado esta caja (<c>UiaSurface.Pulso</c>, en físicos): la rosa pasa a la candidata
    /// que calza con ella y, si no calza ninguna, la caja de la mano se pinta sola. Siempre una rosa como mucho.
    /// </summary>
    /// <remarks>
    /// Sin candidatas con caja —las del terreno o del dynpro (365), o las de un paso que cambió lo que se ve (382)— la
    /// caja de la mano es lo único que se pinta, y es LEÍDA: la geometría es la del elemento que se pulsó. Una caja de la mano que no
    /// se puede pintar deja el overlay sin rosa: lo pulsado antes ya no es lo pulsado ahora.
    /// </remarks>
    public CajasDelOverlay ConPulsada(Rect caja)
    {
        int calza = -1;
        double mejor = 0;
        if (EsPintable(caja))
            for (int i = 0; i < _deLasCandidatas.Count; i++)
            {
                double k = Calce(_deLasCandidatas[i].Caja, caja);
                if (k >= CalceMinimo && k > mejor) { calza = i; mejor = k; }
            }

        var cajas = new List<CajaDeJev>(_deLasCandidatas.Count);
        for (int i = 0; i < _deLasCandidatas.Count; i++) cajas.Add(_deLasCandidatas[i] with { Rosa = i == calza });
        var sola = calza < 0 && EsPintable(caja) ? new CajaDeJev("", "", caja, EsLeida: true, Rosa: true) : null;
        return new CajasDelOverlay(cajas, sola, SinCaja);
    }

    /// <summary>
    /// Cambió la ventana de delante: lo pintado ya no es de lo que se ve. El overlay se vacía y no se vuelve a
    /// pintar hasta la siguiente lista, al revés que TipTour (plano, «cajas de la terminal sobre Music», t=73).
    /// </summary>
    public CajasDelOverlay Caducar() => Vacio;

    /// <summary>
    /// Si una caja se puede pintar: finita y de más de <see cref="LadoMinimo"/> de lado. <c>Rect.Empty</c> —lo
    /// que UIA da a lo que no está en pantalla— no pasa, porque su origen es infinito.
    /// </summary>
    public static bool EsPintable(Rect r) =>
        double.IsFinite(r.X) && double.IsFinite(r.Y) && double.IsFinite(r.Width) && double.IsFinite(r.Height)
        && r.Width > LadoMinimo && r.Height > LadoMinimo;

    /// <summary>Intersección sobre unión de dos cajas (0 si alguna no se puede pintar o no se solapan).</summary>
    public static double Calce(Rect a, Rect b)
    {
        if (!EsPintable(a) || !EsPintable(b)) return 0;
        var i = Rect.Intersect(a, b);
        if (i.IsEmpty) return 0;
        double comun = i.Width * i.Height;
        return comun / (a.Width * a.Height + b.Width * b.Height - comun);
    }

    /// <summary>
    /// Si esta candidata es la pulsada. LA PULSADA LLEGA CON DOS FORMAS, y cada una se compara por el camino de
    /// quien la produce (aprendizaje nº16): el id entero, ORDINAL, como <c>ElDecisor</c>; o su número, por
    /// <see cref="CandidataDeJev.NumeroDelId"/>, como la mano (<c>SurfaceMapTools.cs:318</c>). Un overlay que
    /// solo casara ids enteros recibiría «6» del ciclo y no pintaría nunca una rosa, en silencio; uno que
    /// redujera el id entero a su número pintaría «6) Guardar» de otra lista sobre el «6) Buscar» de esta.
    /// </summary>
    private static bool EsLaPulsada(string id, string pulsada) =>
        CandidataDeJev.NumeroDelId(pulsada) != null
            ? string.Equals(id, pulsada, StringComparison.Ordinal)
            : string.Equals(CandidataDeJev.NumeroDelId(id), pulsada, StringComparison.Ordinal);
}
