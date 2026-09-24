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

    /// <summary>
    /// Vacío si nada se vetó. Si no, POR QUÉ la elegida no se pulsa aunque Jev la quisiera: la lista de lo irreversible
    /// mordió (390, <c>DecisionDeUnPaso.Veto</c>), y el panel lo dice «vetada» con este texto tal cual (374). Con algo
    /// aquí, <see cref="Actuar"/> es falso. Es lo único que distingue una vetada de cualquier otro «no»: sin él, el panel
    /// caía a la última compuerta de Jev y decía «No estoy seguro (0.99)» de una elegida que Jev sí quería (2026-09-24).
    /// </summary>
    public string Veto { get; init; } = "";
}

/// <summary>
/// UN CICLO DE JEV, que es todo lo que el panel necesita para pintarse. Promesas 372-375 (spec 049). Solo datos.
/// </summary>
/// <remarks>
/// LA PULSADA NO VIENE POR LA DECISIÓN, y por eso va aparte. <c>UnPasoDecidido</c> prueba la elegida y, si la
/// mano dice «no está», la segunda mejor sin volver al decisor (<c>SurfaceMapTools.DecidirYPulsar</c>). El número de
/// la pulsada llega con el paso del evento de la 048 (<c>Paso.Numero</c>, <see cref="ObservadorDelDecisor.CicloDe(Mcp.SurfaceMapTools.PasoDecidido)"/>)
/// y su caja por <c>UiaSurface.Pulso</c>. Hasta el 2026-09-24 el número se sacaba de la línea de progreso, y dejó de
/// casar en cuanto la 353 cambió sus palabras.
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

    /// <summary>
    /// El número del id de lo que la mano pulsó —<c>Paso.Numero</c> del evento, solo si la mano terminó—, o <c>null</c> si
    /// no se sabe o no se pulsó nada.
    /// </summary>
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
    /// EL CICLO QUE SE PINTA CUANDO LLEGA UNA LÍNEA DEL TRAMO: el último paso decidido con la línea puesta, y nada más.
    /// La pulsada es la que trajo el paso (<see cref="ObservadorDelDecisor.CicloDe(Mcp.SurfaceMapTools.PasoDecidido)"/>):
    /// la línea no la pone ni la quita.
    /// </summary>
    /// <remarks>
    /// HASTA EL 2026-09-24 LA LÍNEA ERA FUENTE: una regex sacaba la pulsada de «paso k: «x» (n) conf c · (no )?cambió»,
    /// porque la costura del decisor no veía a cuál fue la mano (el puente provisional, spec 049). La 353 cambió las
    /// palabras —«cambió de sitio», «cambió dentro», «cambió delante»— y desde entonces toda pulsación que cambió algo
    /// dejaba la pulsada en null y la resaltada volvía a ser la elegida, con la 372 en verde porque sus líneas eran las de
    /// antes. El evento de la 048 trae el paso con su número: una sola fuente (aprendizaje nº16), y la prosa vuelve a ser
    /// solo lo que se enseña en el ticker.
    /// </remarks>
    /// <param name="decidido">El último paso decidido del tramo, o <c>null</c> si todavía no hubo: el ciclo nace del objetivo, sin decisión ni pulsada inventadas.</param>
    /// <param name="objetivo">El objetivo del tramo, para cuando no hay decisión.</param>
    /// <param name="linea">La línea tal cual llegó.</param>
    public static CicloDeJev ConLaLinea(CicloDeJev? decidido, string objetivo, string linea)
    {
        ArgumentNullException.ThrowIfNull(linea);
        var desde = decidido ?? new CicloDeJev { Objetivo = objetivo ?? "" };
        return desde with { Fase = FaseDelCiclo.Linea, Linea = linea };
    }
}
