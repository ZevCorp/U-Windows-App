using System.Globalization;
using System.Text.RegularExpressions;
using U.WindowsClient.Decision;

namespace U.WindowsClient.Ui.Jev;

/// <summary>Lo que el panel pinta para un ciclo: exactamente esto, y nada que la ventana calcule por su cuenta.</summary>
/// <param name="Objetivo">El objetivo del tramo, tal cual.</param>
/// <param name="Ticker">La línea de estado. Vacía = el ticker no se ve.</param>
/// <param name="Punto">La opacidad del punto del ticker: 0,82 mientras el tramo sigue, 0,42 cuando para.</param>
/// <param name="Coste">«$0.00007» o «—».</param>
/// <param name="Resultados">Cabecera, medidores y barras; <c>null</c> sin distribución (373).</param>
public sealed record LoQuePinta(string Objetivo, string Ticker, double Punto, string Coste, ResultadosDelPaso? Resultados);

/// <summary>La zona de resultados: solo existe si Jev devolvió una distribución.</summary>
public sealed record ResultadosDelPaso(CabeceraDelPaso Cabecera, MedidoresDelPaso Medidores, IReadOnlyList<BarraDeJev> Barras);

/// <summary>«paso k» «· N detectados» «{ms}ms».</summary>
public sealed record CabeceraDelPaso(string Paso, string Detectados, string Ms);

/// <summary>Los dos medidores del plano.</summary>
public sealed record MedidoresDelPaso(MedidorDeJev Cumplido, MedidorDeJev Ausente);

/// <summary>Un medidor: su texto («0.94» o «—») y cuánto de la pista se rellena (0 sin dato).</summary>
public sealed record MedidorDeJev(string Texto, double Relleno);

/// <summary>Una barra de la distribución.</summary>
/// <param name="Id">El id de la candidata, tal como se ofreció: la identidad por la que se resalta.</param>
/// <param name="Etiqueta">La etiqueta sola; «{n}) {etiqueta}» solo si dos de las cinco la comparten.</param>
/// <param name="Probabilidad">La probabilidad que dio Jev.</param>
/// <param name="Valor">La probabilidad a dos decimales, invariante.</param>
/// <param name="Resaltada">Si es la barra resaltada (<c>SemiBold</c> y texto primario).</param>
/// <param name="PorElegida">Resaltada porque es la elegida y todavía no se sabe qué se pulsó.</param>
/// <param name="PorPulsada">Resaltada porque es la que la mano pulsó.</param>
public sealed record BarraDeJev(string Id, string Etiqueta, double Probabilidad, string Valor, bool Resaltada, bool PorElegida, bool PorPulsada);

/// <summary>
/// EL PANEL DE JEV SALE DE AQUÍ. Promesas 372, 373 y 374 (spec 049). Dado un ciclo, lo que se pinta; la ventana
/// solo dibuja lo que esto devuelve.
/// </summary>
/// <remarks>
/// PURO SALVO EL COSTE, que es un acumulador y se pasa de fuera: <see cref="De"/> suma en él la decisión cuando
/// el ciclo es <see cref="FaseDelCiclo.Decidido"/>, y en ningún otro. Todo lo demás sale del ciclo y solo del
/// ciclo, sin reloj, sin pantalla y sin red, y por eso el contrato lo juzga entero en un segundo.
///
/// LO RESALTADO ES LO PULSADO. La elegida es la que devolvió el decisor; la pulsada es la que la mano pulsó, que
/// puede ser la segunda mejor (<c>SurfaceMapTools.cs:304-309</c>). Una barra que resaltara la elegida mientras la
/// mano pulsa otra sería la caja que miente con distribución y todo (aprendizaje nº4). Así que: pulsada conocida
/// → la suya, por el NÚMERO del id; si no, la elegida por su id, marcada como elegida; con el tramo parado o sin
/// actuar, ninguna. Nunca por la etiqueta: dos «Buscar» existen.
///
/// SIN DISTRIBUCIÓN NO HAY GRÁFICO. Con Luna, con la regla local o con la probabilidad de la elegida a secas no
/// hay nada que repartir en barras, y unos medidores y una cabecera sin barras enseñarían que Jev decidió algo
/// que no decidió. <see cref="LoQuePinta.Resultados"/> es <c>null</c>, y el ticker sigue diciendo el paso.
/// </remarks>
public static class EstadoDeLaDecision
{
    /// <summary>Barras como mucho: las del plano.</summary>
    public const int BarrasComoMucho = 5;

    /// <summary>Opacidad del punto del ticker mientras el tramo sigue (<c>TextCommandPanelView.swift:114</c>).</summary>
    public const double PuntoSigue = 0.82;

    /// <summary>Opacidad del punto del ticker cuando el tramo para.</summary>
    public const double PuntoPara = 0.42;

    private static readonly CultureInfo Invariante = CultureInfo.InvariantCulture;

    /// <summary>
    /// La línea con que el tramo dice que paró: «tramo: {hechos} paso(s) · {motivo}» (<c>ElTramo.cs</c>, al
    /// final de <c>Bucle</c>). La de empezar es «tramo: {objetivo}», sin el recuento, y no casa.
    /// </summary>
    private static readonly Regex LineaDeParada = new(@"^tramo: \d+ paso\(s\) · ", RegexOptions.CultureInvariant);

    /// <summary>Lo que se pinta para este ciclo. Suma en <paramref name="coste"/> si el ciclo es la decisión.</summary>
    public static LoQuePinta De(CicloDeJev ciclo, CosteDeJev coste)
    {
        ArgumentNullException.ThrowIfNull(ciclo);
        ArgumentNullException.ThrowIfNull(coste);

        // UNA DECISIÓN SE FACTURA UNA VEZ: las líneas de progreso que vienen detrás son el mismo ciclo con la
        // línea puesta, y traen sus tokens consigo.
        if (ciclo.Fase == FaseDelCiclo.Decidido) coste.Acumular(ciclo.TokensFacturados);

        var candidatas = ciclo.Candidatas ?? Array.Empty<CandidataDeJev>();
        var decision = ciclo.Decision;
        // VACÍO NO ES AUSENTE (patrón nº9): una pulsada en blanco es que no se sabe cuál fue.
        string? pulsada = string.IsNullOrWhiteSpace(ciclo.Pulsada) ? null : ciclo.Pulsada;
        string? motivoDeParada = MotivoDeParada(ciclo.Linea);

        var (ticker, punto) = Ticker(ciclo, decision, candidatas, pulsada, motivoDeParada);
        var resultados = decision != null && HayDistribucion(decision)
            ? Resultados(ciclo, decision, candidatas, pulsada, parado: motivoDeParada != null)
            : null;
        return new LoQuePinta(ciclo.Objetivo, ticker, punto, coste.Texto, resultados);
    }

    /// <summary>Hay distribución si Jev repartió la probabilidad entre al menos dos candidatas.</summary>
    private static bool HayDistribucion(DecisionDeJev decision) => decision.Alternativas is { Count: >= 2 };

    /// <summary>El motivo de una línea de parada, sin tocar; <c>null</c> si la línea no es de parada.</summary>
    private static string? MotivoDeParada(string? linea)
    {
        if (string.IsNullOrWhiteSpace(linea)) return null;
        var m = LineaDeParada.Match(linea);
        return m.Success ? linea.Substring(m.Length) : null;
    }

    /// <summary>
    /// El ticker y su punto. El orden es el de lo más nuevo a lo más viejo: que el tramo paró manda sobre todo;
    /// que la mano pulsó otra que la elegida, sobre la línea que lo cuenta con su etiqueta; la línea, sobre la
    /// fase; y la decisión, al final.
    /// </summary>
    private static (string Ticker, double Punto) Ticker(
        CicloDeJev ciclo, DecisionDeJev? decision, IReadOnlyList<CandidataDeJev> candidatas, string? pulsada, string? motivoDeParada)
    {
        if (motivoDeParada != null) return (motivoDeParada, PuntoPara);

        string? elegida = decision is { Actuar: true } ? CandidataDeJev.NumeroDelId(decision.Puerta) : null;
        if (pulsada != null && elegida != null && !string.Equals(pulsada, elegida, StringComparison.Ordinal)
            && PorNumero(pulsada, candidatas) is { } segunda)
            return (TextosDeJev.SegundaMejor(EtiquetaDe(segunda.Id, candidatas), pulsada), PuntoSigue);

        if (!string.IsNullOrWhiteSpace(ciclo.Linea)) return (ciclo.Linea, PuntoSigue);

        if (ciclo.Fase == FaseDelCiclo.Mirando) return (TextosDeJev.Mirando, PuntoSigue);
        if (ciclo.Fase == FaseDelCiclo.Eligiendo) return (TextosDeJev.Eligiendo(ciclo.Paso, candidatas.Count), PuntoSigue);

        if (decision == null) return ("", PuntoSigue);
        if (decision.Actuar)
            return (TextosDeJev.Pulsando(EtiquetaDe(decision.Puerta, candidatas), elegida), PuntoSigue);

        // NO SE ACTÚA: las compuertas de Jev en el orden de ElDecisor.ConJev —ofrecida, cumplido, peligro, umbral— y
        // con sus mismos listones. SIN DISTRIBUCIÓN, O CON UNA PRIMERA QUE NO SE OFRECIÓ, Jev no llegó a ninguna: no
        // contestó, contestó vacío, decide Luna, o eligió fuera de la pantalla (que ElDecisor devuelve CON la
        // distribución). Su porqué ya distingue esas causas, y cualquiera de las cadenas de abajo sería elegir una
        // por él (aprendizaje nº2).
        if (!HayDistribucion(decision) || !LaPrimeraSeOfrecio(decision, candidatas))
            return (decision.Porque, PuntoPara);
        if (decision.Cumplido is double cumplido && cumplido >= ElDecisor.CumplidoMinimo)
            return (TextosDeJev.CreeQueYaEsta(cumplido), PuntoPara);
        if (decision.Peligro >= ElDecisor.PeligroMaximo)
            return (TextosDeJev.NoSeDeshace(EtiquetaDe(decision.Puerta, candidatas), decision.Peligro), PuntoPara);
        return (TextosDeJev.NoEstoySeguro(decision.Confianza), PuntoPara);
    }

    private static ResultadosDelPaso Resultados(
        CicloDeJev ciclo, DecisionDeJev decision, IReadOnlyList<CandidataDeJev> candidatas, string? pulsada, bool parado)
    {
        var cabecera = new CabeceraDelPaso(
            string.Format(Invariante, TextosDeJev.FormatoCabeceraPaso, ciclo.Paso),
            string.Format(Invariante, TextosDeJev.FormatoCabeceraDetectados, candidatas.Count),
            string.Format(Invariante, TextosDeJev.FormatoCabeceraMs, ciclo.MsDecidir));
        var medidores = new MedidoresDelPaso(Medidor(decision.Cumplido), Medidor(decision.Ausente));

        // DE MAYOR A MENOR, y estable: a igual probabilidad, en el orden en que vinieron.
        var cinco = decision.Alternativas.OrderByDescending(a => a.Probabilidad).Take(BarrasComoMucho).ToList();
        var etiquetas = cinco.Select(a => EtiquetaDe(a.Puerta, candidatas)).ToList();
        var barras = new List<BarraDeJev>(cinco.Count);
        for (int i = 0; i < cinco.Count; i++)
        {
            var (id, probabilidad) = cinco[i];
            string etiqueta = etiquetas[i];
            string? numero = CandidataDeJev.NumeroDelId(id);
            // EL NÚMERO SOLO CUANDO HACE FALTA para distinguir: dos de las cinco con la misma etiqueta.
            bool compartida = etiquetas.Count(e => string.Equals(e, etiqueta, StringComparison.Ordinal)) > 1;
            bool porPulsada = !parado && pulsada != null && string.Equals(numero, pulsada, StringComparison.Ordinal);
            bool porElegida = !parado && pulsada == null && decision.Actuar && string.Equals(id, decision.Puerta, StringComparison.Ordinal);
            barras.Add(new BarraDeJev(
                id,
                compartida && numero != null ? $"{numero}) {etiqueta}" : etiqueta,
                probabilidad,
                probabilidad.ToString("0.00", Invariante),
                porPulsada || porElegida,
                porElegida,
                porPulsada));
        }
        return new ResultadosDelPaso(cabecera, medidores, barras);
    }

    /// <summary>Un medidor con su valor a dos decimales, o «—» con la pista vacía si no vino.</summary>
    private static MedidorDeJev Medidor(double? valor) => valor is double v
        ? new MedidorDeJev(v.ToString("0.00", Invariante), v)
        : new MedidorDeJev(TextosDeJev.SinDato, 0);

    /// <summary>
    /// La etiqueta de una candidata por su id, comparado ORDINAL como la compara <c>ElDecisor</c>. Si el id no es
    /// de ninguna candidata ofrecida se enseña el id tal cual: inventarle una etiqueta sería peor.
    /// </summary>
    private static string EtiquetaDe(string id, IReadOnlyList<CandidataDeJev> candidatas)
    {
        var c = candidatas.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal));
        return c == null || string.IsNullOrWhiteSpace(c.Etiqueta) ? id : c.Etiqueta;
    }

    /// <summary>
    /// Si la más probable de la distribución es una de las candidatas ofrecidas, por id y ORDINAL, como comprueba
    /// <c>ElDecisor</c> antes que ninguna otra compuerta.
    /// </summary>
    private static bool LaPrimeraSeOfrecio(DecisionDeJev decision, IReadOnlyList<CandidataDeJev> candidatas)
    {
        string primera = decision.Alternativas.OrderByDescending(a => a.Probabilidad).First().Puerta;
        return candidatas.Any(c => string.Equals(c.Id, primera, StringComparison.Ordinal));
    }

    /// <summary>La candidata cuyo id lleva este número, por el mismo camino que la mano (<see cref="CandidataDeJev.NumeroDelId"/>).</summary>
    private static CandidataDeJev? PorNumero(string numero, IReadOnlyList<CandidataDeJev> candidatas) =>
        candidatas.FirstOrDefault(x => string.Equals(CandidataDeJev.NumeroDelId(x.Id), numero, StringComparison.Ordinal));
}
