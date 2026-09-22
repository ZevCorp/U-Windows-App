using System.Linq;

namespace U.WindowsClient.Navigation;

/// <summary>
/// PULSAR: tocar UNA cosa, comprobar qué pasó, y que el núcleo lo aprenda.
/// </summary>
/// <remarks>
/// PULSAR ES LA VERSIÓN MÍNIMA DE IR, y por eso va antes. Lo vio el usuario mirando el diseño:
/// «pulsar acaso no es la versión mínima de ir?» — y el código le da la razón, porque ir no es más
/// que preguntarle al grafo cuál es el siguiente paso y pulsarlo, en bucle. Construir ir primero
/// habría sido construir el bucle antes que el paso (2026-08-23).
///
/// Encaja además con cómo se quiere enseñar: señalar algo y pulsarlo es NAVEGAR EN CORTO, y de ahí
/// sale lo que el grafo aprende. Navegar en largo es repetir eso siguiendo lo aprendido.
///
/// LAS CUATRO COSAS QUE SE PUEDEN ROMPER EN SILENCIO, y que son todo lo que promete esta clase:
///
///   1. PULSAR NO ES HABER LLEGADO. Un clic es una petición; se mira la pantalla después y se
///      contesta con lo que hay, no con lo que se pretendía.
///   2. SI NO SE MOVIÓ NADA, NO SE APRENDE NADA. Acuñar un tramo que no ocurrió mete una mentira en
///      el grafo, y esa mentira la paga cada ruta que pase por ahí a partir de entonces.
///   3. SI LLEVÓ A OTRO SITIO, MANDA EL TERRENO. Se aprende a dónde llevó de verdad, no a dónde se
///      creía. El mapa se corrige solo yendo.
///   4. EL GESTO SE ENSAYA UNA VEZ Y SE RECUERDA (promesas 82-83, spec 003). En una lista el clic
///      simple selecciona y solo el doble abre; en un menú el doble ANULA (2026-08-03). El tipo no
///      dice qué hace falta — se prueba lo suave, se sube a lo fuerte solo sobre CONTENIDO, y lo
///      que funcionó viaja con la arista (promesa 21 del núcleo) para no volver a ensayarlo. Sin
///      esto eran tres clics físicos por visita, para siempre, medido el 2026-08-26.
///
/// LO QUE NO ESTÁ AQUÍ: resolver el selector y EJECUTAR el gesto. Eso es leer y accionar la
/// pantalla —UIA, SAP— y vive donde vive. Aquí se DECIDE el gesto (porque decidirlo exige juzgar
/// la consecuencia, y la consecuencia se juzga aquí); ejecutarlo es de la mano. Antes este
/// comentario decía que escalar al doble «vive en UIA» — era un mapa de un territorio borrado: la
/// escalada murió con el código viejo y ninguna capa la tenía (2026-08-26, port de la spec 003).
/// </remarks>
public sealed class PulsarSegunElNucleo
{
    private readonly Nucleo.Grafo _grafo;
    private readonly Func<string> _donde;
    /// <summary>La mano: selector, etiqueta y gesto → null si pudo; si no, POR QUÉ (promesa 231; vacío = no lo dijo).</summary>
    private readonly Func<string, string, string, string?> _mano;

    /// <summary>
    /// Lo que le pasó a la ventana de trabajo mientras se pulsaba (promesa 233): «la ventana en la
    /// que trabajaba (…) ya no existe». Con aviso, no se aprende ningún tramo: Ü no cruzó una puerta,
    /// la ventana se cerró.
    /// </summary>
    public Func<string>? AvisoDeLaVentana { get; set; }

    /// <param name="pulsar">Selector y etiqueta → ¿se pudo tocar? Lo hace quien sabe de UIA.</param>
    /// <remarks>La mano vieja no sabe de gestos: se adapta ignorándolos. Sigue siendo válida para
    /// quien solo necesite el clic de siempre — el arnés del contrato la usa así.</remarks>
    public PulsarSegunElNucleo(Nucleo.Grafo grafo, Func<string> donde, Func<string, string, bool> pulsar)
        : this(grafo, donde, (sel, et, _) => pulsar(sel, et)) { }

    /// <param name="pulsar">Selector, etiqueta y GESTO («» = clic simple, «doubleclick», …) →
    /// ¿se pudo tocar? La mano ejecuta el gesto que se le pide; decidirlo es de esta clase.</param>
    public PulsarSegunElNucleo(Nucleo.Grafo grafo, Func<string> donde, Func<string, string, string, bool> pulsar)
        : this(grafo, donde, (sel, et, gesto) => pulsar(sel, et, gesto) ? null : "", true) { }

    private PulsarSegunElNucleo(Nucleo.Grafo grafo, Func<string> donde, Func<string, string, string, string?> mano, bool _)
    {
        _grafo = grafo;
        _donde = donde;
        _mano = mano;
    }

    /// <summary>
    /// CON UNA MANO QUE DICE POR QUÉ (promesa 231): null si pudo; si no, la causa. «no pude pulsar «X».»
    /// cubría tres situaciones —no encontré el elemento en esa ventana, no admite ningún patrón, el
    /// patrón falló— y mandaba la investigación al sitio equivocado (aprendizaje nº2).
    /// </summary>
    public static PulsarSegunElNucleo ConMotivo(Nucleo.Grafo grafo, Func<string> donde, Func<string, string, string, string?> mano)
        => new(grafo, donde, mano, true);

    /// <summary>Qué pasó al pulsar. <paramref name="Aprendido"/> = el grafo se quedó con el tramo.</summary>
    public readonly record struct Resultado(
        bool SePudo, bool CambioLaPantalla, string Desde, string Hasta, bool Aprendido, string Cuenta);

    /// <summary>
    /// Cuánto se espera a que la pantalla reaccione. No es un tiempo fijo elegido a ojo: se
    /// pregunta cada poco y se sale en cuanto cambia, así que una pantalla rápida no paga la espera
    /// de una lenta.
    /// </summary>
    public int EsperaMaximaMs { get; init; } = 1800;

    /// <summary>
    /// LO QUE SE ESPERA TRAS PULSAR UN CAMPO DE TEXTO. Promesa 334 (spec 043): poco, porque un campo no navega.
    /// </summary>
    /// <remarks>
    /// MEDIDO EL 2026-09-18 sobre las 50 pulsaciones con reloj de tres pruebas del dueño: 16 no cambiaron de
    /// pantalla, y 7 de esas eran campos de texto (4 ComboBox, 3 Edit). LOS CAMPOS CAMBIARON DE PANTALLA 0 VECES DE 7.
    /// Cada una costó 3,3-3,9 s: 1,8 s esperando un cambio que un campo no produce, y 0,3-0,5 s preguntándole al
    /// terreno si esa «puerta» lleva a algún sitio. Un campo no es una puerta.
    ///
    /// NO ES CERO: si en este rato la pantalla cambia, manda lo que pasó y se cuenta como cualquier navegación.
    /// </remarks>
    public int EsperaDeCampoMs { get; init; } = 300;

    // ── LA ESPERA MIRA LO QUE SE VE (spec 047) ─────────────────────────────────────────────────────────────
    //
    // MEDIDO EL 2026-09-21 (u-20260921.log, 16:34-16:39, ChatGPT.exe y Gmail en Chrome): 13 de 13 clics que no
    // navegaron esperaron 1.797-1.825 ms a un cambio de UBICACIÓN que no llegaba, y en al menos 2 algo SÍ había
    // cambiado (el selector de perfiles de Chrome abrió OTRA ventana del proceso; «Minimizar» cambió la ventana
    // de delante) y el veredicto dijo «no cambió», porque solo se miraba la ubicación.
    //
    // FASE 0, SOLO REGISTRO: la espera sigue decidiendo por la ubicación hasta el techo, y ADEMÁS toma la huella
    // en sombra para dejar en el log a los cuántos ms se habría asentado y qué parte vio cambiar (promesa 355).
    // La regla que recorta (351) entra cuando la fase 0 haya medido: es la condición que la spec 043 dejó escrita
    // («acortar esa espera sin medir es la spec 038, aparcada por el dueño»).

    /// <summary>
    /// Quién mira lo que se ve. Nulo = nadie mira: se espera como hoy, y la línea de la 355 lo dice. Es lo que
    /// mantiene verdes la 245, la 248, la 296 y la 334 tal como están: sus arneses no inyectan huella.
    /// </summary>
    public Func<HuellaDeLoQueSeVe?>? Huella { get; set; }

    /// <summary>
    /// La ubicación de trabajo SIN la memoria de 400 ms (regla 2b de la 047). Nulo = <c>_donde</c>, que en la app
    /// sí pasa por la memoria; el contrato inyecta la suya para juzgar el sitio tardío.
    /// </summary>
    public Func<string>? SitioFresco { get; set; }

    /// <summary>Dónde se cuenta lo que pasó al pulsar. Nulo = el log de la app («mano»). El contrato lo inyecta para leerlo.</summary>
    public Action<string>? Diario { get; set; }

    /// <summary>Cuánto tienen que separarse dos huellas iguales para dar la pantalla por asentada. Sale de la fase 0; 250 = jkudish.</summary>
    public int RespiroMs { get; set; } = 250;

    /// <summary>Antes de esto no se declara nada: una página que aún no empezó a pintarse parece asentada (el 400 de la compuerta, 299).</summary>
    public int PrimeraHuellaMs { get; set; } = 400;

    private void Anota(string linea)
    {
        if (Diario != null) Diario(linea);
        else Diagnostics.LogBus.Log("mano", linea);
    }

    /// <summary>Para que el reintento de la 248 no se llame a sí mismo.</summary>
    private bool _yaRepeti;

    /// <summary>Cuántas veces se preguntó «dónde» en la última espera: para el reloj del log.</summary>
    private int _sondeos;

    /// <summary>A los cuántos ms cambió la ubicación en la última espera; -1 = no cambió en el techo.</summary>
    private long _msCambioUbicacion = -1;

    /// <summary>
    /// ¿ESTA PUERTA, DESDE ALGÚN OTRO SITIO, LLEVA JUSTO A DONDE ESTAMOS? Promesa 296 (spec 038).
    /// </summary>
    /// <remarks>
    /// SI LLEVA AQUÍ, DESDE AQUÍ NO NAVEGA. Medido el 2026-09-18 a las 05:51 con el reloj por fase del tramo:
    /// estando YA en Descargas, pulsar el TreeItem «Descargas» costó 6.157 ms. El log, gesto a gesto: clic y
    /// 1,8 s; el ENSAYO con doble clic FÍSICO y 1,8 s; la repetición de la 248 —«el terreno sabe que lleva a
    /// algún sitio»— y 1,8 s. Tres pulsaciones, una de ellas un doble clic real sobre la pantalla de la
    /// persona, esperando un cambio imposible: el paso anterior acababa de aprender que esa puerta, desde
    /// «Disco local», lleva a la pantalla donde ya estábamos.
    ///
    /// NO ES «SU DESTINO ES DONDE ESTOY»: el grafo guarda el destino por pantalla y rechaza las aristas a sí
    /// mismas, así que esa frase no se cumple nunca. Lo que el terreno sí sabe es lo de arriba: la MISMA
    /// puerta, vista desde OTRA pantalla, lleva aquí.
    ///
    /// SOLO SE CONSULTA CUANDO YA NO CAMBIÓ NADA, fuera del camino rápido: recorre las pantallas conocidas, y
    /// eso no se paga en cada clic.
    /// </remarks>
    public static bool LlevaAqui(Nucleo.Grafo grafo, string selector, string desde)
    {
        if (grafo == null || string.IsNullOrWhiteSpace(selector) || string.IsNullOrWhiteSpace(desde)) return false;
        try
        {
            foreach (string u in grafo.Ubicaciones())
            {
                if (Superficies.MismaPantalla(u, desde)) continue;
                foreach (var a in grafo.DesdeAqui(u))
                    if (a.Que.Selector == selector && a.Destino.Length > 0 && Superficies.MismaPantalla(a.Destino, desde))
                        return true;
            }
        }
        catch { }
        return false;
    }

    public Resultado Pulsa(string selector, string etiqueta)
    {
        string desde = _donde() ?? "";
        int vivosAntes = Vivos(desde);   // gratis: sale del núcleo (promesa 248)

        // EL GESTO APRENDIDO VA DIRECTO (promesa 82). Si esta arista ya se cruzó, la arista sabe
        // cómo: repetir el ensayo es pagar el mismo riesgo dos veces — y el clic de más cae sobre
        // la pantalla real (2026-08-26: tres clics físicos por visita, cada visita).
        string gesto = _grafo.GestoDe(desde, selector);

        // LA HUELLA DE ANTES DE TOCAR (355): lo que se ve ahora es contra lo que se juzga lo de después. Si no se
        // puede tomar, se dice por qué y se espera como hoy; no se pulsa a ciegas ni se deja de pulsar por eso.
        var (antes, causaAntes, msAntes) = TomarHuellaDeAntes(desde);

        // EL RELOJ DE «PULSAR», POR PARTES (spec 038): la mano, la espera del cambio y la consulta al terreno. El
        // reloj por fase del tramo dice cuánto cuesta «pulsar»; esto dice en qué se va.
        var relojMano = System.Diagnostics.Stopwatch.StartNew();
        string? motivo = _mano(selector, etiqueta, gesto);
        relojMano.Stop();
        if (motivo != null)
            return new(false, false, desde, desde, false,
                motivo.Length > 0 ? $"no pude pulsar «{etiqueta}»: {motivo}" : $"no pude pulsar «{etiqueta}».");
        bool esCampo = EsCampoDeTexto(desde, selector);
        int presupuesto = esCampo ? EsperaDeCampoMs : EsperaMaximaMs;
        var sombra = antes == null || Huella == null ? null
            : new EsperaAsentada(Huella, SitioFresco ?? _donde, antes, RespiroMs, PrimeraHuellaMs);
        var relojEspera = System.Diagnostics.Stopwatch.StartNew();
        string hasta = EsperarACambiar(desde, presupuesto, sombra);
        relojEspera.Stop();
        Anota($"⏱ pulsar «{etiqueta}»: la mano {relojMano.ElapsedMilliseconds} ms · esperar el cambio {relojEspera.ElapsedMilliseconds} ms ({_sondeos} sondeo(s) de «dónde») · {(hasta.Length > 0 && hasta != desde ? "cambió" : "no cambió")}");
        Anota(LaMedidaDeLaEspera(etiqueta, desde, hasta, presupuesto, antes, causaAntes, msAntes, sombra));
        // LA VENTANA DE TRABAJO SE CERRÓ (promesa 233): «dónde» volvió al foco de la persona, y eso
        // no es haber ido allí. Se cuenta tal cual y no se aprende ninguna arista.
        string aviso = AvisoDeLaVentana?.Invoke() ?? "";
        if (aviso.Length > 0)
            return new(true, hasta != desde, desde, hasta, false,
                $"pulsé «{etiqueta}» y {aviso}. Ahora estás en «{hasta}».");
        string gestoUsado = gesto;

        // UN CAMPO DE TEXTO NO NAVEGA (promesa 334): ni se consulta el terreno ni se repite el clic —las dos son para
        // puertas—, y se dice lo que es para que lo siguiente sea escribir.
        if (esCampo && (hasta.Length == 0 || hasta == desde))
            return new(true, false, desde, desde, false,
                $"pulsé «{etiqueta}»: es un campo de texto y ya tiene el foco (la pantalla no cambió, que es lo normal). Para escribir en él, map_type.");

        // UNA PUERTA QUE LLEVA AQUÍ NO SE ENSAYA NI SE REPITE (promesa 296). Va DESPUÉS de la primera espera a
        // propósito: si la pantalla SÍ cambió —un «Siguiente» que vive en todas las páginas— manda lo que pasó,
        // no lo que se sabía, y se cuenta como cualquier navegación.
        var relojTerreno = System.Diagnostics.Stopwatch.StartNew();
        bool llevaAqui = (hasta.Length == 0 || hasta == desde) && LlevaAqui(_grafo, selector, desde);
        relojTerreno.Stop();
        if (relojTerreno.ElapsedMilliseconds > 20)
            Anota($"⏱ consultar al terreno si «{etiqueta}» lleva aquí: {relojTerreno.ElapsedMilliseconds} ms");
        if (llevaAqui)
        {
            Anota($"«{etiqueta}» no movió nada y el terreno sabe que lleva justo a donde ya estamos: ni lo ensayo ni lo repito");
            return new(true, false, desde, desde, false,
                $"pulsé «{etiqueta}» y la pantalla no cambió: ya estás en «{desde}», que es a donde lleva.");
        }

        // EL ENSAYO, y solo cuando toca: nada cambió, el gesto de esta arista aún no se conoce, y
        // lo tocado es CONTENIDO (promesa 83). Sobre un botón el doble no se ensaya jamás — «hacer
        // su trabajo sin cambiar de pantalla» es lo normal de un «Guardar», y un segundo clic sería
        // repetir la acción, no averiguar nada.
        if ((hasta.Length == 0 || hasta == desde) && gesto.Length == 0 && EsContenido(desde, selector)
            && _mano(selector, etiqueta, "doubleclick") == null)
        {
            string tras = EsperarACambiar(desde);
            if (tras.Length > 0 && tras != desde)
            {
                hasta = tras;
                gestoUsado = "doubleclick";
            }
        }

        // NO MOVERSE NO SIEMPRE ES UN FALLO. Un botón de acción —«Guardar», «Copiar»— hace su
        // trabajo sin cambiar de pantalla, y llamar a eso un fracaso sería reportar mal algo que
        // salió bien. Lo que NO se hace es aprender un tramo: no lo hubo.
        // UN CLIC PERDIDO SE REPITE AQUÍ, NO EN EL MODELO (promesa 248). Solo cuando el terreno ya sabía
        // que esta puerta lleva a algún sitio: un botón que aplica algo sin cambiar de pantalla —«Guardar»—
        // no tiene destino aprendido y por eso nunca entra aquí, que es lo que promete la 83.
        if ((hasta.Length == 0 || hasta == desde) && !_yaRepeti
            && U.Graph.Surfaces.ComoSePulsa.HayQueRepetir(false, vivosAntes, Vivos(desde),
                   SafeToClick.EsDestructivo(etiqueta, out _), SabeQueLleva(desde, selector), yaSeRepitio: false))
        {
            _yaRepeti = true;
            try
            {
                Anota($"«{etiqueta}» no movió nada y el terreno sabe que lleva a algún sitio: lo repito una vez");
                if (_mano(selector, etiqueta, gesto) == null)
                {
                    string tras = EsperarACambiar(desde);
                    if (tras.Length > 0 && tras != desde) { hasta = tras; gestoUsado = gesto; }
                }
            }
            finally { _yaRepeti = false; }
        }

        if (hasta.Length == 0 || hasta == desde)
            return new(true, false, desde, desde, false,
                $"pulsé «{etiqueta}» y la pantalla no cambió.");

        // EL TERRENO MANDA SOBRE EL MAPA: se aprende a dónde llevó DE VERDAD — y CON QUÉ GESTO,
        // que es la mitad del saber que antes se tiraba (promesa 21).
        bool aprendido = _grafo.Cruzar(desde, selector, hasta, gestoUsado);

        return new(true, true, desde, hasta, aprendido,
            $"pulsé «{etiqueta}» y ahora estás en «{hasta}»."
            + (aprendido ? " Queda aprendido." : ""));
    }

    /// <summary>
    /// ¿Lo tocado es contenido de lista? Decide QUÉ ES SEGURO ENSAYAR, nunca el gesto: la lección
    /// del 2026-08-03 (Configuración anula con el segundo clic) es que el tipo no sabe el gesto,
    /// pero un ListItem admite el ensayo sin romper nada y un Button no.
    /// </summary>
    private bool EsContenido(string ubicacion, string selector)
    {
        // LA LISTA ES UIA-ONLY A PROPOSITO (2026-08-31). Los tipos SAP (GuiTreeFila, GuiGridFila)
        // NO estan aqui, y esta guarda trabaja EN PAREJA con la de FaceWindow: alli SapSelector.Owns
        // descarta el gesto y manda la mano vieja, porque en SAP Execute ya resuelve la accion real
        // por el selector (doubleClickNode en filas) y nuestro «doble» seria una segunda opinion.
        // Si algun dia se añade un tipo SAP aqui SIN tocar aquella guarda, el ensayo mandara un
        // doubleclick que FaceWindow degradara a clic simple: clic+clic sobre una fila SAP y una
        // arista que aprende un gesto que se reproducira como otro. Las dos guardas se cambian
        // JUNTAS o ninguna (lo vio el agente optimizador antes de que pasara).
        foreach (var a in _grafo.DesdeAqui(ubicacion))
            if (a.Que.Selector == selector)
                return a.Que.Tipo is "ListItem" or "TreeItem" or "DataItem";
        return false;
    }

    /// <summary>
    /// ¿Lo tocado es un campo de texto? Lo dice el terreno, y si el terreno aún no conoce el elemento —la primera
    /// vez que se ve una pantalla—, el selector, que siempre lleva el tipo (`;ct=Edit`).
    /// </summary>
    private bool EsCampoDeTexto(string ubicacion, string selector)
    {
        try
        {
            foreach (var a in _grafo.DesdeAqui(ubicacion))
                if (a.Que.Selector == selector)
                    return a.Que.Tipo is "Edit" or "ComboBox";
        }
        catch { }
        return selector.EndsWith(";ct=Edit", StringComparison.OrdinalIgnoreCase)
            || selector.EndsWith(";ct=ComboBox", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Cuántas cosas vivas hay aquí, según el núcleo. Gratis: no toca la pantalla (promesa 248).</summary>
    private int Vivos(string donde)
    {
        try { return _grafo.DesdeAqui(donde).Count(a => a.Vivo); }
        catch { return -1; }
    }

    /// <summary>¿El terreno ya vio que esta puerta lleva a algún sitio? (promesa 248).</summary>
    private bool SabeQueLleva(string donde, string selector)
    {
        try { return _grafo.DesdeAqui(donde).Any(a => a.Que.Selector == selector && a.Destino.Length > 0); }
        catch { return false; }
    }

    private string EsperarACambiar(string desde) => EsperarACambiar(desde, EsperaMaximaMs, null);

    /// <param name="sombra">La espera que mira lo que se ve, EN SOMBRA (fase 0 de la 047): se sondea en cada vuelta
    /// y se anota; no decide nada todavía. Nulo = nadie mira.</param>
    private string EsperarACambiar(string desde, int presupuestoMs, EsperaAsentada? sombra)
    {
        // EL RELOJ MANDA (promesa 245): antes esto sumaba 120 por vuelta y además pagaba _donde(), que
        // en la máquina del dueño costaba 2,8 s. Una espera de «1,8 s» duraba más de treinta.
        var compas = new Compas(presupuestoMs);
        string ahora = "";
        _sondeos = 0;
        _msCambioUbicacion = -1;
        do
        {
            _sondeos++;
            ahora = _donde() ?? "";
            if (ahora.Length > 0 && ahora != desde) { _msCambioUbicacion = compas.Transcurrido; return ahora; }
            sombra?.Sondea(compas.Transcurrido);
        }
        while (compas.Respira(120));
        return ahora.Length > 0 ? ahora : (_donde() ?? "");
    }

    /// <summary>La huella de antes de tocar, o por qué no se pudo (la cadena entera: patrón nº3), y lo que costó.</summary>
    private (HuellaDeLoQueSeVe? Huella, string Causa, long Ms) TomarHuellaDeAntes(string desde)
    {
        if (Huella == null) return (null, "", 0);
        var crono = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var h = Huella();
            return h == null ? (null, "la huella no devolvió nada", crono.ElapsedMilliseconds) : (h, "", crono.ElapsedMilliseconds);
        }
        catch (Exception e)
        {
            var partes = new List<string>();
            for (Exception? x = e; x != null; x = x.InnerException) partes.Add($"{x.GetType().Name}: {x.Message}");
            return (null, string.Join(" ← ", partes), crono.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// LA LÍNEA DE LA 355: la medida de la espera, en una sola línea y siempre —aunque no se recorte nada, y aunque
    /// nadie mire—. Es lo que la fase 0 de la 047 deja en el log para decidir con datos cuánto recortar: las dos
    /// ubicaciones y la ventana de delante antes y después, qué parte vio cambiar, a los cuántos ms se habría
    /// asentado (o que nunca), a los cuántos cambió la ubicación (o que no en el techo), y lo que costó cada parte.
    /// </summary>
    private string LaMedidaDeLaEspera(string etiqueta, string desde, string hasta, int presupuestoMs,
        HuellaDeLoQueSeVe? antes, string causaAntes, long msAntes, EsperaAsentada? sombra)
    {
        string ubicacion = hasta.Length > 0 && hasta != desde
            ? $"ubicación antes «{desde}» → después «{hasta}» (cambió a los {_msCambioUbicacion} ms)"
            : $"ubicación antes «{desde}» → después «{hasta}» (no cambió en el techo de {presupuestoMs} ms)";
        if (Huella == null)
            return $"👀 tras «{etiqueta}»: {ubicacion} · delante: nadie miraba (sin huella inyectada) · nunca se asentó: nadie miraba";
        if (antes == null || sombra == null)
            return $"👀 tras «{etiqueta}»: {ubicacion} · delante: no pude mirar antes de tocar ({causaAntes}; {msAntes} ms) · nunca se asentó: no había huella de antes";

        var ultima = sombra.Ultima;
        string delante = ultima == null
            ? $"delante antes «{antes.Delante}» → después: sin sondeo que lo viera"
            : $"delante antes «{antes.Delante}» → después «{ultima.Delante}»";
        string dentro;
        if (ultima == null) dentro = "dentro: sin sondeo";
        else
        {
            var dif = HuellaDeLoQueSeVe.Comparar(antes, ultima);
            dentro = dif.QueCambio switch
            {
                HuellaDeLoQueSeVe.QueCambio.Nada => "nada cambió (huella igual a la de antes)",
                HuellaDeLoQueSeVe.QueCambio.Dentro when dif.Parte == HuellaDeLoQueSeVe.Parte.Ventanas
                    => $"cambió dentro (lo vio: ventanas del proceso, {antes.Ventanas.Count} → {ultima.Ventanas.Count})",
                HuellaDeLoQueSeVe.QueCambio.Dentro => $"cambió dentro (lo vio: dentro, {antes.Dentro.Count} → {ultima.Dentro.Count} accionables)",
                HuellaDeLoQueSeVe.QueCambio.Delante => "cambió delante (lo vio: delante)",
                _ => $"cambió de sitio (lo vio: sitio, «{antes.Sitio}» → «{ultima.Sitio}»)",
            };
        }
        string sitioFresco = sombra.MsCambioDeSitio >= 0
            ? $"sitio fresco cambió a los {sombra.MsCambioDeSitio} ms («{sombra.SitioAhora}»)"
            : "sitio fresco no cambió";
        return $"👀 tras «{etiqueta}»: {ubicacion} · {delante} · {dentro} · {sombra.Resumen()} · {sitioFresco} · huella de antes {msAntes} ms";
    }
}