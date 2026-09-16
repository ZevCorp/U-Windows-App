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

    public Resultado Pulsa(string selector, string etiqueta)
    {
        string desde = _donde() ?? "";

        // EL GESTO APRENDIDO VA DIRECTO (promesa 82). Si esta arista ya se cruzó, la arista sabe
        // cómo: repetir el ensayo es pagar el mismo riesgo dos veces — y el clic de más cae sobre
        // la pantalla real (2026-08-26: tres clics físicos por visita, cada visita).
        string gesto = _grafo.GestoDe(desde, selector);

        string? motivo = _mano(selector, etiqueta, gesto);
        if (motivo != null)
            return new(false, false, desde, desde, false,
                motivo.Length > 0 ? $"no pude pulsar «{etiqueta}»: {motivo}" : $"no pude pulsar «{etiqueta}».");
        string hasta = EsperarACambiar(desde);
        // LA VENTANA DE TRABAJO SE CERRÓ (promesa 233): «dónde» volvió al foco de la persona, y eso
        // no es haber ido allí. Se cuenta tal cual y no se aprende ninguna arista.
        string aviso = AvisoDeLaVentana?.Invoke() ?? "";
        if (aviso.Length > 0)
            return new(true, hasta != desde, desde, hasta, false,
                $"pulsé «{etiqueta}» y {aviso}. Ahora estás en «{hasta}».");
        string gestoUsado = gesto;

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

    private string EsperarACambiar(string desde)
    {
        // EL RELOJ MANDA (promesa 245): antes esto sumaba 120 por vuelta y además pagaba _donde(), que
        // en la máquina del dueño costaba 2,8 s. Una espera de «1,8 s» duraba más de treinta.
        var compas = new Compas(EsperaMaximaMs);
        string ahora = "";
        do
        {
            ahora = _donde() ?? "";
            if (ahora.Length > 0 && ahora != desde) return ahora;
        }
        while (compas.Respira(120));
        return ahora.Length > 0 ? ahora : (_donde() ?? "");
    }
}