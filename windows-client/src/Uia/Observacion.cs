using System.Windows.Automation;

namespace U.WindowsClient.Uia;

/// <summary>
/// UN ELEMENTO TAL COMO SE LEYÓ: selector, etiqueta, tipo, caja LEÍDA (nunca estimada) e identidad.
/// </summary>
/// <remarks>
/// <paramref name="Identidad"/> es el <c>RuntimeId</c> que la petición trajo (promesa 298). VACÍA
/// SIGNIFICA «NO VINO», NUNCA «LA MISMA»: una identidad vacía no funde con nada (spec 048, regla 7).
/// </remarks>
public sealed record ElementoVisto(string Selector, string Etiqueta, string Tipo, System.Windows.Rect Caja = default, string Identidad = "")
{
    /// <summary>La identidad de un nodo de UIA leído con caché, o vacía si la petición no la trajo (o no fue con caché).</summary>
    public static string IdentidadDe(AutomationElement? n)
    {
        if (n == null) return "";
        try { return n.GetCachedPropertyValue(AutomationElement.RuntimeIdProperty) is int[] id ? string.Join(".", id) : ""; }
        catch (InvalidOperationException) { return ""; }   // leído nodo a nodo: sin caché no hay RuntimeId «que vino»
        catch (ElementNotAvailableException) { return ""; }
    }
}

/// <summary>
/// UNA LECTURA DE PANTALLA CON VERSIÓN, para compartirla (spec 048, promesa 362).
/// </summary>
/// <remarks>
/// MEDIDO (spec 048, 2026-09-21): un paso del tramo podía pagar hasta CINCO lecturas UIA de la misma ventana,
/// 77-113 ms cada una. Se promete una, publicada, y reutilizada por la MISMA ventana que se leyó y el MISMO
/// dónde, con una edad máxima que pone quien pide.
///
/// <paramref name="Elementos"/> son LOS CRUDOS de la lectura, sin criba ni tope: cada consumidor aplica la
/// suya (las candidatas su «con nombre, ni text ni image»; la compuerta la del latido). <paramref name="Hwnd"/>
/// es la ventana que se leyó, tal como la devolvió quien leyó. <paramref name="Completa"/> distingue una
/// lectura de la ventana entera de la respuesta a preguntar por UN selector (promesa 364).
/// </remarks>
public sealed record Observacion(
    long Version,
    IntPtr Hwnd,
    string Donde,
    string Pantalla,
    long LeidaEn,
    long MsDeLectura,
    string ComoSeLeyo,
    IReadOnlyList<ElementoVisto> Elementos,
    bool Completa)
{
    /// <summary>
    /// CUÁNTO VALE UNA OBSERVACIÓN, en ms. PROVISIONAL (patrón nº8): la edad de la vista al llegar a la compuerta
    /// no está medida por este camino; la spec 040 midió que la ubicación cambia 405-510 ms después de un clic que
    /// navega, así que 1.000 no es una cifra de aquí. El nivel 4 la fija con la medida propia (p95 + margen).
    /// </summary>
    public const int VigenciaMs = 1000;

    /// <summary>Cuántos ms tiene esta observación en el instante <paramref name="ahora"/> del reloj del observatorio.</summary>
    public long EdadEn(long ahora) => ahora - LeidaEn;
}

/// <summary>
/// LA OBSERVACIÓN COMPARTIDA: la última lectura publicada, y quién puede recibirla sin leer.
/// </summary>
/// <remarks>
/// Leer para el mapa es publicar; reutilizar es pedir por la misma ventana y el mismo dónde, con edad; accionar
/// invalida. Los dos lados de la comparación tienen que salir del MISMO camino (aprendizaje nº16): el hwnd que se
/// pide es el que devolvió la función que eligió qué leer, y el dónde es el <c>aqui</c> del localizador —quien
/// publica y quien pide los sacan de ahí y no de otra función—.
///
/// Estático y con un reloj inyectable, para que el contrato lo juzgue puro, sin pantalla ni tiempo real.
/// </remarks>
public static class Observatorio
{
    private static readonly object _cerrojo = new();
    private static Observacion? _ultima;
    private static long _version;
    private static string _porQueNoHay = "nunca se leyó";

    /// <summary>El reloj, en ms monotónicos. Null = el de la máquina. El contrato pone el suyo.</summary>
    public static Func<long>? Reloj { get; set; }

    /// <summary>Ahora, según el reloj que rija.</summary>
    public static long Ahora() => (Reloj ?? (() => Environment.TickCount64))();

    /// <summary>Publica una lectura: sube la versión y reemplaza a la anterior. Devuelve la publicada, ya con versión.</summary>
    public static Observacion Publica(Observacion lectura)
    {
        if (lectura == null) throw new ArgumentNullException(nameof(lectura));
        lock (_cerrojo)
        {
            var conVersion = lectura with { Version = ++_version };
            _ultima = conVersion;
            return conVersion;
        }
    }

    /// <summary>
    /// La última observación si es de ESA ventana, de ESE dónde y no más vieja que <paramref name="edadMaxMs"/>; si no, null.
    /// </summary>
    public static Observacion? Reciente(IntPtr hwnd, string donde, int edadMaxMs)
        => PorQueNoSirve(hwnd, donde, edadMaxMs, out var o) == null ? o : null;

    /// <summary>
    /// POR QUÉ no se puede reutilizar la última observación para esa ventana y ese dónde —o null si sí se puede,
    /// y entonces <paramref name="observacion"/> la trae—. El motivo va al log: «reutilizada: no (otra ventana 1 → 2)»
    /// distingue lo que «no se reutilizó» a secas no distinguiría (patrón nº2).
    /// </summary>
    public static string? PorQueNoSirve(IntPtr hwnd, string donde, int edadMaxMs, out Observacion? observacion)
    {
        Observacion? u;
        string porQueNoHay;
        lock (_cerrojo) { u = _ultima; porQueNoHay = _porQueNoHay; }
        observacion = null;
        if (u == null) return porQueNoHay;
        if (u.Hwnd != hwnd) return $"otra ventana {u.Hwnd.ToInt64()} → {hwnd.ToInt64()}";
        if (!string.Equals(u.Donde, donde ?? "", StringComparison.OrdinalIgnoreCase)) return $"otro dónde «{u.Donde}» → «{donde}»";
        long edad = u.EdadEn(Ahora());
        if (edad > edadMaxMs) return $"vieja {edad} ms (> {edadMaxMs})";
        observacion = u;
        return null;
    }

    /// <summary>Olvida la última observación. El siguiente que pida, lee; y se le dirá por qué.</summary>
    public static void Invalida(string porque)
    {
        lock (_cerrojo)
        {
            _ultima = null;
            _porQueNoHay = $"invalidada: {porque}";
        }
    }

    /// <summary>
    /// Olvida la última observación SALVO que ya sea de <paramref name="donde"/>. Es lo que llama el mapa vivo al
    /// notar un cambio de sitio: lo nota 200-500 ms tarde (spec 040), y para entonces el acto que nos trajo puede
    /// haber leído ya la pantalla nueva —tirarla obligaría al siguiente paso a pagar la lectura otra vez—.
    /// DEDUCIDO, no medido: el nivel 4 dice cuántas veces salva una lectura.
    /// </summary>
    public static void InvalidaLoQueNoSeaDe(string donde, string porque)
    {
        lock (_cerrojo)
        {
            if (_ultima != null && string.Equals(_ultima.Donde, donde ?? "", StringComparison.OrdinalIgnoreCase)) return;
            _ultima = null;
            _porQueNoHay = $"invalidada: {porque}";
        }
    }

    /// <summary>La versión de la última publicada, o 0 si no hay ninguna. Para el log y el contrato.</summary>
    public static long VersionActual { get { lock (_cerrojo) return _ultima?.Version ?? 0; } }
}
