namespace U.WindowsClient.Ui.Jev;

/// <summary>En qué punto del ciclo de Jev está lo que se va a pintar.</summary>
public enum FaseDelCiclo
{
    /// <summary>Se está leyendo la pantalla: todavía no hay candidatas.</summary>
    Mirando,

    /// <summary>Hay candidatas y el decisor está eligiendo entre ellas.</summary>
    Eligiendo,

    /// <summary>El decisor contestó. Es el ÚNICO ciclo que factura: <see cref="CosteDeJev"/> suma aquí y en ningún otro.</summary>
    Decidido,

    /// <summary>
    /// Llegó una línea de progreso del tramo (<see cref="CicloDeJev.Linea"/>). Es el ciclo decidido de antes
    /// con la línea puesta: lleva su decisión y sus tokens, y por eso no vuelve a facturar.
    /// </summary>
    Linea,
}

/// <summary>
/// Una candidata tal como se le ofreció al decisor: su id numerado, su etiqueta y su tipo, y su caja si se leyó.
/// </summary>
/// <remarks>
/// EL ID ES LA IDENTIDAD, NO LA ETIQUETA. En SAP hay dos «Buscar» en la misma pantalla; lo que las distingue es
/// el id que numera <c>UnPasoDecidido</c> (<c>SurfaceMapTools.cs:270</c>, «6) Buscar (Button)»). Todo lo que
/// la vista resalta se compara por el id o por su número, nunca por la etiqueta.
/// </remarks>
public sealed record CandidataDeJev
{
    /// <summary>El id tal como se le ofreció al decisor: «{n}) {etiqueta} ({tipo})».</summary>
    public string Id { get; init; } = "";

    /// <summary>La etiqueta sola, la que se enseña.</summary>
    public string Etiqueta { get; init; } = "";

    /// <summary>El tipo de control. No se enseña: el vídeo no lo trae y el número basta para distinguir.</summary>
    public string Tipo { get; init; } = "";

    /// <summary>La caja en físicos, o <c>null</c> si no se leyó. Una candidata sin caja se ofrece pero no se pinta (375).</summary>
    public System.Windows.Rect? Caja { get; init; }

    /// <summary>Si la geometría es LEÍDA (UIA, Scripting) o ESTIMADA (la banda de una fila de árbol de SAP): aprendizaje nº4.</summary>
    public bool EsLeida { get; init; }

    /// <summary>
    /// El número de un id: «6) Buscar (Button)» → «6». <c>null</c> si el id no lleva «)».
    /// </summary>
    /// <remarks>
    /// POR EL MISMO CAMINO QUE LA MANO (aprendizaje nº16). La línea de progreso del tramo trae este número entre
    /// paréntesis, y lo saca <c>SurfaceMapTools.cs:318</c> con <c>id.Substring(0, id.IndexOf(')'))</c>: si la
    /// vista lo sacara de otra forma —un <c>Split</c>, un número parseado—, «pulsé la 3» y la barra de la 3
    /// podrían no casar nunca, y en silencio. Son los dos únicos sitios del cliente que derivan el número de un
    /// id (grep de <c>IndexOf(')')</c>, 2026-09-22).
    /// </remarks>
    public static string? NumeroDelId(string? id)
    {
        if (id == null) return null;
        int cierre = id.IndexOf(')');
        return cierre < 0 ? null : id.Substring(0, cierre);
    }
}

/// <summary>
/// La decisión de un paso tal como la ve el panel: la de <c>ElDecisor</c>, con dos diferencias que son de la vista.
/// </summary>
/// <remarks>
/// <see cref="Cumplido"/> y <see cref="Ausente"/> son <c>double?</c>: «no vino» no es 0, y un medidor sin dato
/// enseña «—» (373). <see cref="Puerta"/> es la ELEGIDA —la primera de la distribución— también cuando no se
/// actúa, porque el panel la necesita para decir «"Grabar" no se deshace»; en <c>DecisionDeUnPaso</c> viene
/// vacía cuando no se actúa, y quien traduzca de una a otra (el observador, fase 6 de la 049) la saca de
/// <c>Alternativas</c>.
/// </remarks>
public sealed record DecisionDeJev
{
    /// <summary>Si el decisor dijo que se actúe.</summary>
    public bool Actuar { get; init; }

    /// <summary>El id de la elegida, tal como se ofreció.</summary>
    public string Puerta { get; init; } = "";

    /// <summary>La confianza de la elegida (0-1).</summary>
    public double Confianza { get; init; }

    /// <summary>La distribución entera: id y probabilidad. Vacía con Luna y con la regla local, que no la tienen.</summary>
    public IReadOnlyList<(string Puerta, double Probabilidad)> Alternativas { get; init; } = Array.Empty<(string, double)>();

    /// <summary>Cuánto dice Jev que el objetivo ya está cumplido, o <c>null</c> si no vino.</summary>
    public double? Cumplido { get; init; }

    /// <summary>Cuánto dice Jev que lo buscado no está en esta pantalla, o <c>null</c> si no vino.</summary>
    public double? Ausente { get; init; }

    /// <summary>Cuánto dice Jev que accionar la elegida es irreversible (0-1).</summary>
    public double Peligro { get; init; }

    /// <summary>El porqué del decisor, tal cual. Ya distingue sus causas (<c>ElDecisor.cs</c>); la vista no lo reescribe.</summary>
    public string Porque { get; init; } = "";
}

/// <summary>
/// UN CICLO DE JEV, que es todo lo que el panel necesita para pintarse. Promesas 372-375 (spec 049). Solo datos.
/// </summary>
/// <remarks>
/// LA PULSADA NO VIENE POR LA DECISIÓN, y por eso va aparte. <c>UnPasoDecidido</c> prueba la elegida y, si la
/// mano dice «no está», la segunda mejor sin volver al decisor (<c>SurfaceMapTools.cs:304-309</c>): la costura
/// del decisor solo ve <c>d.Puerta</c>. El número de la pulsada llega por la línea de progreso
/// («paso k: «x» (n) …», <c>ElTramo.cs:204</c>) y su caja por <c>UiaSurface.Pulso</c>.
/// </remarks>
public sealed record CicloDeJev
{
    /// <summary>Lo que el tramo quiere conseguir, tal cual lo pidió la persona.</summary>
    public string Objetivo { get; init; } = "";

    /// <summary>El número de paso dentro del tramo.</summary>
    public int Paso { get; init; }

    /// <summary>Las candidatas que se le ofrecieron al decisor, en su orden.</summary>
    public IReadOnlyList<CandidataDeJev> Candidatas { get; init; } = Array.Empty<CandidataDeJev>();

    /// <summary>Lo que devolvió el decisor, o <c>null</c> si todavía no contestó.</summary>
    public DecisionDeJev? Decision { get; init; }

    /// <summary>El número del id de lo que la mano pulsó —el «(n)» de la línea de progreso—, o <c>null</c> si no se sabe todavía.</summary>
    public string? Pulsada { get; init; }

    /// <summary>La caja de lo que la mano dice haber pulsado, en físicos (<c>UiaSurface.Pulso</c>), o <c>null</c>.</summary>
    public System.Windows.Rect? CajaPulsada { get; init; }

    /// <summary>Lo que tardó en decidir, cronometrado alrededor de la llamada al decisor. Es el «ms» de la cabecera.</summary>
    public long MsDecidir { get; init; }

    /// <summary>Los tokens que el cliente dice haber facturado por esta decisión, o <c>null</c> si no lo dijo.</summary>
    public int? TokensFacturados { get; init; }

    /// <summary>En qué punto del ciclo estamos.</summary>
    public FaseDelCiclo Fase { get; init; }

    /// <summary>La última línea de progreso del tramo, tal cual llegó, o <c>null</c>.</summary>
    public string? Linea { get; init; }

    /// <summary>
    /// «paso k: «etiqueta» (n) conf c · cambió» o «· no cambió»: la línea que <c>ElTramo.Cuenta</c> escribe cuando la
    /// mano TERMINÓ de pulsar (<c>ElTramo.cs:204</c>). La etiqueta puede llevar paréntesis y comillas, así que el
    /// número es el que va entre paréntesis justo antes de « conf». «· no pudo» es que la mano no terminó, y no casa.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex LineaDePaso = new(
        @"^paso \d+: «.*» \((?<n>[^()]+)\) conf \S+ · (?:no )?cambió$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// EL PUENTE POR LA LÍNEA DE PROGRESO (spec 049 §El puente provisional, 2; revisión del 2026-09-23): el ciclo que
    /// se pinta cuando llega una línea del tramo. Es la última decisión con la línea puesta y, si la línea dice qué se
    /// pulsó, con su número en <see cref="Pulsada"/>: la mano puede haber pulsado la segunda mejor sin volver al
    /// decisor (<c>SurfaceMapTools.cs:304-331</c>), y solo la línea lo cuenta. Hasta el 2026-09-23 la vista no la
    /// leía, así que la barra resaltada era siempre la elegida y el ticker nunca decía «la 1.ª no estaba».
    /// </summary>
    /// <param name="decidido">La última decisión del tramo, o <c>null</c> si todavía no hubo: el ciclo nace del objetivo, sin decisión inventada.</param>
    /// <param name="objetivo">El objetivo del tramo, para cuando no hay decisión.</param>
    /// <param name="linea">La línea tal cual llegó.</param>
    public static CicloDeJev ConLaLinea(CicloDeJev? decidido, string objetivo, string linea)
    {
        ArgumentNullException.ThrowIfNull(linea);
        var desde = decidido ?? new CicloDeJev { Objetivo = objetivo ?? "" };
        return desde with { Fase = FaseDelCiclo.Linea, Linea = linea, Pulsada = PulsadaDeLaLinea(linea) };
    }

    /// <summary>
    /// El número de lo que la mano pulsó según una línea de paso —el mismo texto que <see cref="CandidataDeJev.NumeroDelId"/>
    /// saca del id, porque la línea lo escribe desde ahí—, o <c>null</c> si la línea no dice que se pulsó nada.
    /// </summary>
    public static string? PulsadaDeLaLinea(string? linea)
    {
        if (string.IsNullOrWhiteSpace(linea)) return null;
        var m = LineaDePaso.Match(linea);
        return m.Success ? m.Groups["n"].Value : null;
    }
}
