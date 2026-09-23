using System.Windows.Automation;
using U.Graph.Surfaces;

namespace U.WindowsClient.Navigation;

/// <summary>
/// LA HUELLA REAL, tomada de la pantalla: la que <see cref="PulsarSegunElNucleo.Huella"/> consume en la app.
/// Spec 047, fase 0. Cada parte deja su coste en la huella, para la línea de la 355.
/// </summary>
/// <remarks>
/// TRES PARTES POR WIN32 Y UNA POR UIA, y la de UIA es BARATA a propósito: UN <c>FindAll</c> bajo un
/// <c>CacheRequest</c> que solo trae <c>RuntimeId</c> y <c>ControlType</c> —una llamada entre procesos, sin
/// <c>.Current</c> por nodo, sin <c>Reconocedor.SelectorDe</c>—. La lectura completa (<c>UiaReader.Read</c> →
/// selectores) cuesta 144-420 ms medidos (Bloc 144, Configuración 155, Wikipedia 270, Explorador 420; 18-09) y
/// sería más que el respiro: subir la cadencia sin bajar el costo por iteración es el patrón nº14. Lo que cuesta
/// ESTA lectura no está medido al escribir esto: la fase 0 lo mide por sondeo, y el Explorador antes de la fase 2.
///
/// CUÁNDO SE PAGA CADA PARTE: las de Win32 en cada sondeo; lo de dentro como mucho cada <see cref="RespiroMs"/> y
/// solo si delante y ventanas no cambiaron (si cambiaron, ya hay veredicto sin pagar UIA); el sitio fresco como
/// mucho cada respiro (cuesta un <c>DondeTrabajo</c> sin memoria: sin medir, fase 0).
///
/// CUANDO EXISTA la observación de la rama C (<c>Observatorio.Reciente(hwnd)</c>, promesa 362), lo de dentro sale
/// de su hash sin leer. Hoy no existe (0 coincidencias en el repo el 22-09), así que se lee.
/// </remarks>
public sealed class HuellaEnVivo
{
    private readonly Func<string> _sitioFresco;
    private readonly Func<IntPtr> _ventanaDeTrabajo;

    /// <summary>Cada cuánto, como mucho, se paga lo de dentro y el sitio fresco.</summary>
    public int RespiroMs { get; set; } = EsperaAsentada.RespiroMetaMs;

    private string _sitio = "";
    // NULO = AÚN NO SE LEYÓ. Con long.MinValue, «ahora - _tSitio» desbordaba a negativo (sonda del 22-09) y la PRIMERA
    // huella de cada sesión salía con el sitio vacío: su línea habría dicho que el sitio cambió sin cambiar (355).
    private long? _tSitio;
    private IReadOnlyList<string> _dentro = Array.Empty<string>();
    private long _tDentro = long.MinValue;
    private string _delanteDeLaUltima = "";
    private IReadOnlyList<string> _ventanasDeLaUltima = Array.Empty<string>();
    private IntPtr _hwndDeLaUltima;

    /// <param name="sitioFresco">La ubicación de trabajo SIN memoria: <c>_dondeTrabajo.Olvida()</c> + <c>DondeTrabajo()</c>.</param>
    /// <param name="ventanaDeTrabajo">El hwnd en el que la mano pulsa (<c>VentanaObjetivo</c>).</param>
    public HuellaEnVivo(Func<string> sitioFresco, Func<IntPtr> ventanaDeTrabajo)
    {
        _sitioFresco = sitioFresco;
        _ventanaDeTrabajo = ventanaDeTrabajo;
    }

    /// <summary>El sitio, releído fresco ahora mismo. Para <see cref="PulsarSegunElNucleo.SitioFresco"/>.</summary>
    public string SitioFresco()
    {
        _sitio = _sitioFresco() ?? "";
        _tSitio = Environment.TickCount64;
        return _sitio;
    }

    /// <summary>
    /// Toma la huella de lo que se ve ahora. Lanza —con la causa— si no hay ventana de trabajo o UIA no deja
    /// leerla: «no pude mirar» tiene que decir por qué (patrón nº3), y quien espera decide qué hacer con eso.
    /// </summary>
    public HuellaDeLoQueSeVe Tomar()
    {
        long ahora = Environment.TickCount64;
        var crono = System.Diagnostics.Stopwatch.StartNew();

        // 1. EL SITIO, fresco como mucho cada respiro.
        long sitioMs = 0;
        if (_tSitio is not long tSitio || ahora - tSitio >= RespiroMs) { SitioFresco(); sitioMs = crono.ElapsedMilliseconds; }

        // 2. LA VENTANA DE DELANTE, por Win32 (saltando las de Ü, la misma regla que la ventana de delante del localizador).
        crono.Restart();
        var (hwndDelante, titulo) = UiaSurface.VentanaDelanteAhora();
        string delante = hwndDelante == IntPtr.Zero ? "(ninguna)" : $"{hwndDelante:X}·{titulo}";
        long delanteMs = crono.ElapsedMilliseconds;

        // 3. LA VENTANA DE TRABAJO y 4. LAS VENTANAS DE SU PROCESO, por Win32.
        IntPtr hwnd = _ventanaDeTrabajo();
        if (hwnd == IntPtr.Zero) throw new InvalidOperationException("no hay ventana de trabajo de la que tomar la huella");
        crono.Restart();
        var ventanas = UiaSurface.VentanasDelProceso(hwnd).Select(h => h.ToString("X")).ToArray();
        long ventanasMs = crono.ElapsedMilliseconds;

        // 5. LO DE DENTRO, solo si las baratas no cambiaron y ya pasó un respiro; si no, la última.
        bool baratasIguales = hwnd == _hwndDeLaUltima
            && string.Equals(delante, _delanteDeLaUltima, StringComparison.Ordinal)
            && ventanas.OrderBy(v => v, StringComparer.Ordinal).SequenceEqual(_ventanasDeLaUltima, StringComparer.Ordinal);
        long dentroMs = 0;
        if (!baratasIguales || ahora - _tDentro >= RespiroMs || _tDentro == long.MinValue)
        {
            crono.Restart();
            _dentro = HuellaBarata(hwnd);
            dentroMs = crono.ElapsedMilliseconds;
            _tDentro = ahora;
        }
        _hwndDeLaUltima = hwnd; _delanteDeLaUltima = delante;
        _ventanasDeLaUltima = ventanas.OrderBy(v => v, StringComparer.Ordinal).ToArray();

        return HuellaDeLoQueSeVe.De(_sitio, delante, _dentro, ventanas)
            .ConCoste(new HuellaDeLoQueSeVe.Costes(sitioMs, delanteMs, dentroMs, ventanasMs));
    }

    /// <summary>Lo que se considera accionable para la huella: lo mismo que la observación cuenta como puertas, más los ítems de árbol y tabla.</summary>
    private static readonly Condition CondicionDeAccionable = new OrCondition(
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TreeItem),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Hyperlink),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.CheckBox),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.RadioButton),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.SplitButton),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Menu),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));

    /// <summary>
    /// Las identidades de los accionables de esa ventana: RuntimeId + tipo, en UNA petición. Lanza con la causa si UIA
    /// no la deja leer: aquí un catch mudo convertiría «la ventana se cerró» en «no cambió nada».
    /// </summary>
    public static IReadOnlyList<string> HuellaBarata(IntPtr hwnd)
    {
        var raiz = AutomationElement.FromHandle(hwnd)
            ?? throw new InvalidOperationException($"UIA no da raíz para la ventana {hwnd:X}");
        var peticion = new CacheRequest { AutomationElementMode = AutomationElementMode.None, TreeScope = TreeScope.Element };
        peticion.Add(AutomationElement.RuntimeIdProperty);
        peticion.Add(AutomationElement.ControlTypeProperty);
        AutomationElementCollection hallados;
        using (peticion.Activate()) hallados = raiz.FindAll(TreeScope.Descendants, CondicionDeAccionable);
        var ids = new List<string>(hallados.Count);
        foreach (AutomationElement el in hallados)
        {
            var rid = el.GetCachedPropertyValue(AutomationElement.RuntimeIdProperty) as int[] ?? Array.Empty<int>();
            string tipo = (el.GetCachedPropertyValue(AutomationElement.ControlTypeProperty) as ControlType)?.ProgrammaticName ?? "?";
            ids.Add(string.Join(".", rid) + ":" + tipo.Replace("ControlType.", ""));
        }
        return ids;
    }
}
