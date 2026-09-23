using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;

namespace U.Graph.Surfaces;

/// <summary>
/// UN barrido de la ventana en foco que sirve a los tres consumidores del reproductor (promesa 361,
/// spec 048).
///
/// MEDIDO (2026-09-22): <c>ReadFields</c>, <c>ReadinessCount</c> y <c>StructureFingerprint</c> hacían
/// cada uno el MISMO recorrido nodo a nodo con TreeWalker + <c>.Current</c> —3 sitios— y el inventario
/// costaba 200–350 ms. Aquí el recorrido se hace una vez, se captura por nodo lo que los tres leen
/// (nombre, AutomationId, tipo, caja, visible, habilitado, ruta) y los derivados salen de la captura.
///
/// Es puro a propósito: no toca UIA. Quién recorre (<see cref="UiaSurface.Barre"/>) y qué hora es
/// (<see cref="UiaSurface.Reloj"/>) entran de fuera, para que el contrato juzgue los tres métodos
/// reales con un árbol de mentira que cuenta llamadas.
/// </summary>
public static class BarridoUia
{
    /// <summary>
    /// Cuánto vive una captura. 100 ms, MENOR que <c>SurfaceReadiness.PollMs</c> (120): un sondeo de
    /// carga nunca recibe el árbol del sondeo anterior. La versión previa de la spec decía 250 «menor
    /// que 120 × 2», que es falso y además lo contrario de lo que argumentaba (refutación 7 de la 048).
    /// </summary>
    public const int VigenciaMs = 100;

    /// <summary>Lo que el recorrido captura de cada elemento, tal como lo leen los tres consumidores.</summary>
    public sealed class Nodo
    {
        public Nodo(string nombre, string automationId, string tipo, bool visible, bool habilitado,
                    IReadOnlyList<int> ruta, Rect caja = default, string helpText = "",
                    AutomationElement? elemento = null)
        {
            Nombre = nombre ?? "";
            AutomationId = automationId ?? "";
            Tipo = tipo ?? "";
            Visible = visible;
            Habilitado = habilitado;
            Ruta = ruta ?? Array.Empty<int>();
            Caja = caja;
            HelpText = helpText ?? "";
            Elemento = elemento;
        }

        public string Nombre { get; }
        public string AutomationId { get; }
        /// <summary>El nombre programático de UIA («ControlType.Button»), que es lo que va a la huella.</summary>
        public string Tipo { get; }
        public bool Visible { get; }
        public bool Habilitado { get; }
        public IReadOnlyList<int> Ruta { get; }
        public Rect Caja { get; }
        public string HelpText { get; }
        /// <summary>
        /// El elemento vivo, si el recorrido fue real: valor, opciones y multilínea se siguen leyendo
        /// de él (como hoy). Un árbol de mentira no lo trae y esos tres salen vacíos.
        /// </summary>
        public AutomationElement? Elemento { get; }

        /// <summary>El tipo sin el prefijo, como lo escriben los selectores («Button»).</summary>
        public string TipoCorto => Tipo.Replace("ControlType.", "");
    }

    /// <summary>Un barrido hecho: los nodos crudos, cuándo, cuánto costó y a quién ha servido.</summary>
    public sealed class Captura
    {
        public Captura(int numero, IntPtr hwnd, IReadOnlyList<Nodo> nodos, long leidaEn, long ms)
        {
            Numero = numero; Hwnd = hwnd; Nodos = nodos; LeidaEn = leidaEn; Ms = ms;
        }
        public int Numero { get; }
        public IntPtr Hwnd { get; }
        public IReadOnlyList<Nodo> Nodos { get; }
        public long LeidaEn { get; }
        public long Ms { get; }
        public List<string> Servidos { get; } = new();

        private string? _invalidada;

        /// <summary>
        /// Por qué ya no vale aunque siga dentro de la vigencia —se accionó—, o null si vale. La vigencia protege
        /// a un sondeo del árbol del sondeo anterior; esto protege de servir la pantalla de ANTES de una acción
        /// (hallazgo del 2026-09-22: la huella del paso N+1 llegaba 60-90 ms después del Enter del paso N).
        /// </summary>
        public string? Invalidada => Volatile.Read(ref _invalidada);

        /// <summary>Accionar la invalida: lo barrido antes de tocar la pantalla no describe la de después.</summary>
        public void Invalida(string porque)
            => Volatile.Write(ref _invalidada, string.IsNullOrWhiteSpace(porque) ? "se accionó" : porque);
    }

    /// <summary>
    /// Lo que hoy filtra <c>Walk</c> antes de acumular: interactivo, visible y habilitado. El recorrido
    /// real ya viene filtrado (y con eso los topes de 40 niveles y 300 elementos cuentan lo mismo que
    /// hoy); sobre un árbol de mentira crudo es aquí donde se aplica.
    /// </summary>
    public static IEnumerable<Nodo> Interactivos(Captura c)
        => c.Nodos.Where(n => n.Visible && n.Habilitado && TiposInteractivos.Contains(n.Tipo));

    /// <summary>Text/Label quedan fuera: no son acciones. Misma lista que <c>UiaSurface.Interactive</c>.</summary>
    public static readonly HashSet<string> TiposInteractivos = new(StringComparer.Ordinal)
    {
        "ControlType.Edit", "ControlType.ComboBox", "ControlType.CheckBox", "ControlType.RadioButton",
        "ControlType.Button", "ControlType.List", "ControlType.ListItem", "ControlType.MenuItem",
        "ControlType.Hyperlink", "ControlType.TabItem", "ControlType.SplitButton", "ControlType.Document",
    };

    /// <summary>
    /// La huella estructural: AutomationId + tipo, o ruta + tipo cuando no hay id. NUNCA el texto: eso
    /// es dato y cambia en cada corrida. Ordenada y deduplicada por <see cref="Fingerprints.Of"/>.
    /// </summary>
    public static string Huella(Captura c)
        => Fingerprints.Of(Interactivos(c).Select(n => n.AutomationId.Length > 0
                ? $"{n.AutomationId}|{n.Tipo}"
                : $"@{string.Join(".", n.Ruta)}|{n.Tipo}"));

    /// <summary>
    /// Sirve la captura anterior si es de la misma ventana y más fresca que la vigencia; si no, barre.
    /// Cada llamada deja UNA línea en el log: «barrido nº N: M nodos en X ms · servido a Q» cuando se
    /// barre, o «barrido nº N reutilizado (E ms de edad) · servido a Q» cuando se sirve el mismo. Esa
    /// línea es la medida de cuántos Walk paga un paso del reproductor. Una captura INVALIDADA (se accionó
    /// después de tomarla) no se sirve aunque sea fresca, y la línea del barrido nuevo dice por qué.
    /// </summary>
    public static Captura Toma(IntPtr hwnd, Captura? anterior, Func<IntPtr, IReadOnlyList<Nodo>?> barre,
                               Func<long> reloj, string paraQuien, Action<string>? log)
    {
        long ahora = reloj();
        string? invalidada = anterior?.Invalidada;
        if (anterior != null && invalidada == null && anterior.Hwnd == hwnd && ahora - anterior.LeidaEn <= VigenciaMs)
        {
            anterior.Servidos.Add(paraQuien);
            log?.Invoke($"barrido nº{anterior.Numero} reutilizado ({ahora - anterior.LeidaEn} ms de edad) · servido a {paraQuien}");
            return anterior;
        }

        var sw = Stopwatch.StartNew();
        IReadOnlyList<Nodo> nodos;
        try { nodos = barre(hwnd) ?? Array.Empty<Nodo>(); }
        catch (Exception e)
        {
            // Sin catch mudo (patrón nº3): el recorrido real ya trae dentro lo que UIA lanza en árboles
            // que cambian; si aun así llega aquí, se dice qué fue y se sirve vacío.
            log?.Invoke($"barrido nº{(anterior?.Numero ?? 0) + 1} falló: {e.GetType().Name}: {e.Message} · servido vacío a {paraQuien}");
            nodos = Array.Empty<Nodo>();
        }
        sw.Stop();

        var c = new Captura((anterior?.Numero ?? 0) + 1, hwnd, nodos, ahora, sw.ElapsedMilliseconds);
        c.Servidos.Add(paraQuien);
        log?.Invoke($"barrido nº{c.Numero}: {nodos.Count} nodos en {c.Ms} ms · servido a {paraQuien}"
                    + (invalidada != null ? $" · el nº{anterior!.Numero} no se reutilizó: {invalidada}" : ""));
        return c;
    }
}
