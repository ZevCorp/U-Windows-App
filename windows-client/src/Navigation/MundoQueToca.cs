using U.Graph.Surfaces;

namespace U.WindowsClient.Navigation;

/// <summary>
/// EL DESPACHO ENTRE MUNDOS: cada superficie se observa por su propia puerta y se pulsa por su
/// propia mano. Hoy hay dos mundos —UIA y SAP— y la decisión de cuál toca es todo lo que vive aquí.
/// </summary>
/// <remarks>
/// EXISTE POR EL HUECO QUE DEJABA A SAP FUERA DEL TERRENO (T1 del plan terreno-profundo,
/// 2026-08-25): `MapaVivo` observaba SIEMPRE con el lector UIA, y dentro de una ventana SAP el
/// sistema operativo ve un Pane opaco — el grafo aprendía los 12 elementos del marco y ninguno de
/// la sesión. La Scripting API de SAP y su vocabulario de identidad (`sap:wnd[0]/…`,
/// <see cref="SapSelector"/>) ya estaban construidos y probados; faltaba el despacho.
///
/// PURO A PROPÓSITO: las dos clases reciben las puertas como delegados y solo DECIDEN. Ni COM ni
/// UIA aquí — así el contrato juzga la decisión con fakes (promesas 68 y 69) sin abrir pantalla.
/// </remarks>
public sealed class SentidoPorMundo
{
    private readonly Func<List<Nucleo.Elemento>> _uia;
    private readonly Func<List<Nucleo.Elemento>> _sap;

    public SentidoPorMundo(Func<List<Nucleo.Elemento>> uia, Func<List<Nucleo.Elemento>> sap)
    {
        _uia = uia;
        _sap = sap;
    }

    /// <summary>
    /// Lo visible en esta ubicación, mirando por la puerta del mundo al que pertenece.
    /// </summary>
    /// <remarks>
    /// LA UBICACIÓN DECIDE, no el proceso: el MARCO de SAP Logon es `uia://saplogon.exe/…` y se
    /// lee por UIA como cualquier ventana; solo DENTRO de una sesión (`sapgui://…`) manda el
    /// scripting. Decidir por proceso habría dejado ciego el Logon mismo.
    /// </remarks>
    public List<Nucleo.Elemento> Lee(string ubicacion) =>
        (ubicacion ?? "").StartsWith("sapgui://", StringComparison.OrdinalIgnoreCase)
            ? _sap()
            : _uia();
}

public sealed class ManoPorMundo
{
    private readonly Func<string, string, bool> _uia;
    private readonly Func<string, string, bool> _sap;

    public ManoPorMundo(Func<string, string, bool> uia, Func<string, string, bool> sap)
    {
        _uia = uia;
        _sap = sap;
    }

    /// <summary>
    /// Pulsar por la mano que entiende el selector. Devuelve si se pudo tocar.
    /// </summary>
    /// <remarks>
    /// EL SELECTOR DECIDE, no la ubicación: el selector ES la identidad y lleva escrito de qué
    /// mundo viene (<see cref="SapSelector.Owns"/> reconoce `sap:…`, con fragmento o sin él).
    /// Mandarle un selector SAP a la mano UIA sería pedirle a Windows algo que no ve — fallaría en
    /// silencio o, peor, acertaría sobre otra cosa.
    /// </remarks>
    public bool Pulsa(string selector, string etiqueta) =>
        SapSelector.Owns(selector ?? "")
            ? _sap(selector!, etiqueta)
            : _uia(selector ?? "", etiqueta);
}

public sealed class EscribirPorMundo
{
    private readonly Func<string> _donde;
    private readonly Func<string, bool> _uia;
    private readonly Func<string, bool> _sap;

    public EscribirPorMundo(Func<string> donde, Func<string, bool> uia, Func<string, bool> sap)
    {
        _donde = donde;
        _uia = uia;
        _sap = sap;
    }

    /// <summary>
    /// Escribir por el lápiz del mundo en el que estamos. Devuelve si el texto quedó puesto.
    /// </summary>
    /// <remarks>
    /// LA UBICACIÓN DECIDE (como el sentido): dentro de una sesión SAP, teclear por UIA hacia «el
    /// foco de Windows» es mandar letras al aire — lo exigió la prueba real del 2026-08-26, el
    /// batch [«comando» → «NWP1» → «Continuar»] parado en «no pude escribir». En SAP el texto se
    /// le pone AL CAMPO por su identidad (.Text) y se relee para comprobar que quedó.
    /// </remarks>
    public bool Escribe(string texto) =>
        (_donde() ?? "").StartsWith("sapgui://", StringComparison.OrdinalIgnoreCase)
            ? _sap(texto)
            : _uia(texto);
}

/// <summary>
/// EL NOMBRADO DEL CLIC HUMANO EN SAP: qué puerta fue, dicha en el idioma del terreno.
/// </summary>
/// <remarks>
/// Lo encontró José David en la primera ronda de T4 (2026-08-30): recorrió el triage a mano y sus
/// cruces no dejaron arista — la atribución nombraba lo clicado con UIA, que dentro de SAP ve un
/// Pane sin etiquetas. SAP sí sabe qué se clicó (findByPosition; y en un árbol, la fila clicada
/// ES la seleccionada). Aquí vive solo el NOMBRADO, puro (promesa 77); el casado contra lo
/// observado sigue siendo de AQuienSeLeDioClic con sus vallas — dos «Consultas», no se atribuye.
/// </remarks>
public static class AtribucionSap
{
    /// <param name="nodo">La fila seleccionada del árbol clicado (clave y texto), si lo era.</param>
    public static (string Selector, string Etiqueta, string Tipo)? NombraElClic(
        string id, string tipo, string etiqueta, (string Key, string Text)? nodo, bool esCarpeta = false)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        // Un clic en el árbol es un clic en su FILA: la identidad entera (árbol MÁS clave), la
        // misma de la promesa 70 — la que el observador ya escribe y la mano ya sabe pulsar.
        // Y CON SU TIPO EXACTO: «Favoritos» nombrada GuiTreeFila fue rechazada por el juez —que
        // exige tipo exacto— aunque la puerta existía como GuiTreeCarpeta (ronda 4, 2026-08-30).
        if (nodo is { } n && !string.IsNullOrWhiteSpace(n.Key))
            return (SapSelector.ByNode(id, n.Key),
                    string.IsNullOrWhiteSpace(n.Text) ? n.Key : n.Text,
                    esCarpeta ? "GuiTreeCarpeta" : "GuiTreeFila");

        // Sin etiqueta no hay paso: la misma valla que el camino UIA. Una arista con acción
        // anónima no describe nada.
        if (string.IsNullOrWhiteSpace(etiqueta)) return null;

        return (SapSelector.ById(id), etiqueta, tipo);
    }

    /// <summary>
    /// ¿QUÉ FILA HAY BAJO EL PUNTO del clic? El punto ciego del cambio de selección, resuelto por
    /// geometría (ronda 3, 2026-08-30): el clic del usuario en «Triage» cayó en la fila YA
    /// seleccionada de su visita anterior — sin cambio no había nombre, y las filas retienen su
    /// selección entre visitas, así que el patrón más común del mundo real quedaba mudo. Las
    /// filas observadas traen su rectángulo; si el punto cae dentro, esa fila ES el clic.
    /// Fuera de toda fila: null — mejor mudo que equivocado.
    /// </summary>
    /// <param name="yLocal">La y del clic RELATIVA al árbol (y de pantalla menos el techo del árbol).</param>
    public static (string Key, string Text)? FilaEnElPunto(
        int yLocal, IReadOnlyList<(string Key, string Text, int Top, int Height)> filas)
    {
        foreach (var f in filas ?? Array.Empty<(string, string, int, int)>())
            if (f.Height > 0 && yLocal >= f.Top && yLocal < f.Top + f.Height)
                return (f.Key, f.Text);
        return null;
    }
}

/// <summary>
/// LA TRADUCCIÓN de lo que la Scripting API devuelve al vocabulario del núcleo. Es donde se decide
/// qué es PUERTA para el grafo — la misma pregunta que ParecePuerta contesta para la web.
/// </summary>
public static class SentidoSap
{
    /// <summary>
    /// Los tipos con los que un operador interactúa. Lo demás es decorado: un GuiLabel no se
    /// pulsa, y ofrecerlo como puerta sería la basura de la web otra vez (promesa 65).
    /// </summary>
    private static readonly HashSet<string> Puertas = new(StringComparer.OrdinalIgnoreCase)
    {
        "GuiTextField", "GuiCTextField", "GuiPasswordField", "GuiComboBox",
        "GuiCheckBox", "GuiRadioButton", "GuiButton", "GuiOkCodeField", "GuiTab",
    };

    /// <summary>
    /// Una rejilla ALV tal como la entrega la superficie: sus botones de toolbar (id y texto) y
    /// sus filas visibles (clave de pares columna=valor, y texto legible).
    /// </summary>
    public sealed record RejillaVista(
        string Id,
        IReadOnlyList<(string BtnId, string Texto)> Botones,
        IReadOnlyList<(string ClaveFila, string Texto)> Filas);

    public static List<Nucleo.Elemento> Traducir(IReadOnlyList<SapVisualElement> vistos) =>
        Traducir(vistos, null, null);

    public static List<Nucleo.Elemento> Traducir(
        IReadOnlyList<SapVisualElement> vistos,
        IReadOnlyDictionary<string, IReadOnlyList<SapGuiSurface.TreeRow>>? filasPorArbol) =>
        Traducir(vistos, filasPorArbol, null);

    /// <summary>
    /// Lo mismo, sumando las FILAS VISIBLES de cada árbol (clave: el id del árbol).
    /// </summary>
    /// <remarks>
    /// EL CONTENIDO NAVEGABLE DE SAP VIVE EN LOS ÁRBOLES, medido contra el SAP real (2026-08-26,
    /// QAS/NWP1): el sentido trajo 12 puertas y todas eran de la barra de herramientas, porque
    /// TODO el contenido de esa pantalla son dos `GuiShell[Tree]` bajo un splitter. Un árbol no se
    /// pulsa —se pulsa una de sus filas—, así que sin filas el terreno tenía la orilla y ningún
    /// camino tierra adentro.
    ///
    /// SOLO LAS VISIBLES, que es la bandera Vivo diciendo lo de siempre: un árbol clínico trae
    /// 1197 claves cargadas del servidor y ofrecerlas todas sería prometer pantalla para lo que
    /// solo es memoria. Quien llama pasa lo que `VisibleTreeRows` filtró por geometría.
    /// </remarks>
    public static List<Nucleo.Elemento> Traducir(
        IReadOnlyList<SapVisualElement> vistos,
        IReadOnlyDictionary<string, IReadOnlyList<SapGuiSurface.TreeRow>>? filasPorArbol,
        IReadOnlyList<RejillaVista>? rejillas)
    {
        var r = new List<Nucleo.Elemento>();

        // LA REJILLA (promesa 78): la observación 3 de la primera ronda de T4 — el panel derecho
        // del Triage era UNA caja ámbar y ni un botón suyo era puerta. Sus botones y filas viven
        // DENTRO del control ALV; se leen por su API y entran con identidad entera (#tbbtn, #row
        // por pares columna=valor — «la fila 0» es una posición, los pares dicen a QUIÉN).
        foreach (var g in rejillas ?? Array.Empty<RejillaVista>())
        {
            foreach (var (btnId, texto) in g.Botones ?? Array.Empty<(string, string)>())
            {
                if (string.IsNullOrWhiteSpace(btnId) || string.IsNullOrWhiteSpace(texto)) continue;
                r.Add(new Nucleo.Elemento(SapSelector.ByToolbarButton(g.Id, btnId), texto, "GuiGridBoton"));
            }
            foreach (var (clave, texto) in g.Filas ?? Array.Empty<(string, string)>())
            {
                if (string.IsNullOrWhiteSpace(clave)) continue;
                r.Add(new Nucleo.Elemento(SapSelector.ByRow(g.Id, clave),
                    string.IsNullOrWhiteSpace(texto) ? clave : texto, "GuiGridFila"));
            }
        }

        foreach (var (arbol, filas) in filasPorArbol ?? new Dictionary<string, IReadOnlyList<SapGuiSurface.TreeRow>>())
            foreach (var fila in filas ?? Array.Empty<SapGuiSurface.TreeRow>())
            {
                if (fila.Key.Length == 0) continue;
                // El texto es lo que el operador lee; si el árbol no lo suelta, la clave al menos
                // identifica la fila — callarla sería perder una puerta que SÍ se puede accionar.
                r.Add(new Nucleo.Elemento(
                    SapSelector.ByNode(arbol, fila.Key),
                    fila.Text.Length > 0 ? fila.Text : fila.Key,
                    fila.IsFolder ? "GuiTreeCarpeta" : "GuiTreeFila"));
            }

        foreach (var v in vistos ?? Array.Empty<SapVisualElement>())
        {
            if (v.Id.Length == 0) continue;

            // Una fila de árbol es puerta aunque su tipo sea el del shell: su identidad es el
            // árbol MÁS la clave del nodo, y el selector la lleva entera para ser autocontenido.
            if (v.IsNode && !string.IsNullOrEmpty(v.NodeKey))
            {
                r.Add(new Nucleo.Elemento(SapSelector.ByNode(v.Id, v.NodeKey!), v.Label, v.Type));
                continue;
            }

            if (!Puertas.Contains(v.Type)) continue;

            // EL CAMPO DE COMANDOS ENTRA CON NOMBRE. SAP no lo etiqueta (el describe-visual cae al
            // tipo, «GuiOkCodeField»), pero es la puerta a CUALQUIER transacción y el modelo tiene
            // que poder nombrarlo: «comando», que es lo que es para el operador.
            string etiqueta = v.Type.Equals("GuiOkCodeField", StringComparison.OrdinalIgnoreCase)
                ? "comando"
                : v.Label;

            r.Add(new Nucleo.Elemento(SapSelector.ById(v.Id), etiqueta, v.Type));
        }
        return r;
    }
}
