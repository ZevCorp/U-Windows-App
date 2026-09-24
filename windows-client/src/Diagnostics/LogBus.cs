using System.Collections.Generic;
using System.IO;

namespace U.WindowsClient.Diagnostics;

/// <summary>
/// Bitácora en memoria de todo lo que pasa en las funcionalidades nuevas (enseñanza por video, etc.):
/// sin esto, un error en una tarea "fire-and-forget" o en un evento de una librería nativa se pierde
/// en el aire y el usuario solo ve un mensaje de estado genérico y sobreescrito. Cada entrada queda
/// aquí, visible en <see cref="U.WindowsClient.Ui.LogWindow"/>, incluso después de que el globo de
/// estado ya mostró otra cosa.
///
/// Además, TODO se persiste a disco en un archivo por instancia
/// (%LOCALAPPDATA%\U\logs\u-AAAAMMDD-{worktree}-p{pid}-{hora}.log): el ring de memoria son
/// 500 líneas — un solo run de workflow con polls de 120 ms lo desborda — y sin archivo era imposible
/// reconstruir a posteriori sobre qué pantalla se ejecutó cada paso. El archivo es la evidencia.
///
/// Mismo patrón que el LogBus de la versión Android (com.zevcorp.graph.platform.LogBus).
/// </summary>
public static class LogBus
{
    private const int MaxEntries = 500;
    private static readonly List<string> _entries = new();
    private static readonly object _lock = new();

    private static readonly string _logDir = Path.Combine(U.Graph.UserPaths.Local, "U", "logs");
    private static readonly DateTime _inicio = DateTime.Now;
    private static readonly string _instanceId = CrearIdDeInstancia();
    private static bool _fileBroken; // si el disco falla una vez, no insistir en cada línea
    private static bool _cabeceraEscrita;

    public static event EventHandler<string>? Logged;

    /// <summary>
    /// Lo mismo que <see cref="Logged"/> pero con la etiqueta y el texto SEPARADOS, no pegados en una
    /// línea ya formateada.
    ///
    /// Existe porque quien reenvía el log a otro sitio necesita la etiqueta para decidir: el espejo
    /// que lo sube al backend tiene que poder callar los suyos propios, y volver a sacarla de la línea
    /// con un recorte sería adivinar sobre un formato pensado para leerse, no para analizarse. Ver
    /// <see cref="U.WindowsClient.Telemetry.EspejoDelLog"/>.
    /// </summary>
    public static event Action<string, string>? Anotado;

    /// <summary>
    /// Lo mismo que <see cref="Anotado"/> y además SI LA LÍNEA ESTÁ MARCADA para salir entera del
    /// equipo: <c>true</c> si vino por <see cref="Publico"/>, <c>false</c> si vino por <see cref="Log"/>.
    ///
    /// Es a lo que se engancha el espejo (<see cref="U.WindowsClient.Telemetry.EspejoDelLog"/>) desde
    /// la spec 051. Hasta el 2026-09-24 el espejo oía <see cref="Anotado"/> y subía TODA línea, así
    /// que lo que Ü escribía en SAP (««Talla» = «38,5»») salía del equipo del médico hacia el backend.
    /// La marca viaja con la línea, y no en una lista de etiquetas del espejo, porque una etiqueta no
    /// decide por la línea: bajo «exportar» conviven «3 campo(s) escritos» y el cuerpo de un error.
    /// </summary>
    public static event Action<string, string, bool>? AnotadoConMarca;

    /// <summary>Anota una línea. Del equipo sale solo como cadencia: su etiqueta y su longitud.</summary>
    public static void Log(string tag, string message) => Anotar(tag, message, publica: false);

    /// <summary>
    /// Anota una línea igual que <see cref="Log"/> —anillo, archivo, <see cref="Logged"/>,
    /// <see cref="Anotado"/>— y además la MARCA: el espejo la sube entera al panel.
    ///
    /// Marcar es decidir que el texto sale de la máquina del médico. Cada llamada está congelada en el
    /// censo de la promesa 394 (docs/specs/051-el-espejo-no-sube-lo-escrito.md, P1–P26) con su
    /// etiqueta, su ancla y sus huecos, y un hueco solo puede ser una cifra, un identificador, el tipo
    /// de una excepción o una llamada a <c>SinValor</c>: nunca un <c>Message</c>, un cuerpo HTTP ni la
    /// etiqueta de un paso. Añadir una, o cambiarle un hueco, pone el contrato rojo — y ese es el
    /// momento de leerla.
    /// </summary>
    public static void Publico(string tag, string message) => Anotar(tag, message, publica: true);

    private static void Anotar(string tag, string message, bool publica)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] [{_instanceId}] {tag}: {message}";
        lock (_lock)
        {
            _entries.Add(line);
            if (_entries.Count > MaxEntries) _entries.RemoveAt(0);
            AppendToFile(line);
        }
        Logged?.Invoke(null, line);
        // Nunca puede tumbar a quien está logueando: escribir una línea no es hacerse cargo de lo que
        // otros hagan con ella. Pero tampoco se calla: el oyente que revienta deja su rastro.
        try { Anotado?.Invoke(tag, message); }
        catch (Exception e) { OyenteReventado(nameof(Anotado), e); }
        try { AnotadoConMarca?.Invoke(tag, message, publica); }
        catch (Exception e) { OyenteReventado(nameof(AnotadoConMarca), e); }
    }

    /// <summary>
    /// Un oyente del log lanzó: se escribe en el anillo y en el archivo, y NO se reparte a los oyentes.
    /// </summary>
    /// <remarks>
    /// Repartirla sería pedirle al oyente que acaba de reventar que lea su propio fallo, y un oyente
    /// que revienta en cada línea lo haría en esta también: un bucle. Hasta el 2026-09-24 esto era un
    /// <c>catch { }</c> (patrón nº3), y la spec 051 lo tuvo que esquivar en sus jueces: un fallo del
    /// oyente se leía como «no llegó ningún evento».
    /// </remarks>
    private static void OyenteReventado(string evento, Exception e)
    {
        var cadena = new List<string>();
        for (var x = e; x != null; x = x.InnerException) cadena.Add($"{x.GetType().Name}: {x.Message}");
        string line = $"[{DateTime.Now:HH:mm:ss}] [{_instanceId}] logbus: un oyente de {evento} lanzó · {string.Join(" ← ", cadena)}";
        lock (_lock)
        {
            _entries.Add(line);
            if (_entries.Count > MaxEntries) _entries.RemoveAt(0);
            AppendToFile(line);
        }
    }

    public static IReadOnlyList<string> Snapshot()
    {
        lock (_lock) return _entries.ToArray();
    }

    public static void Clear()
    {
        lock (_lock) _entries.Clear();
    }

    /// <summary>Identidad estable durante la vida de este proceso, visible también en cada línea.</summary>
    public static string InstanceId => _instanceId;

    /// <summary>Ruta del archivo de esta instancia (para abrirlo desde la UI o adjuntarlo a un reporte).</summary>
    public static string TodayFile() => Path.Combine(_logDir, $"u-{DateTime.Now:yyyyMMdd}-{_instanceId}.log");

    // Ya dentro del lock. El log jamás puede tumbar la app: cualquier fallo de disco apaga el sink
    // y la bitácora en memoria sigue como siempre.
    private static void AppendToFile(string line)
    {
        if (_fileBroken) return;
        try
        {
            Directory.CreateDirectory(_logDir);
            if (!_cabeceraEscrita)
            {
                _cabeceraEscrita = true;
                File.AppendAllText(TodayFile(),
                    $"[{_inicio:HH:mm:ss}] [{_instanceId}] instancia: pid={Environment.ProcessId} · ejecutable={Environment.ProcessPath ?? AppContext.BaseDirectory}"
                    + Environment.NewLine);
            }
            File.AppendAllText(TodayFile(), line + Environment.NewLine);
        }
        catch { _fileBroken = true; }
    }

    private static string CrearIdDeInstancia()
    {
        string origen = NombreDelOrigen();
        string limpio = new(origen.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray());
        limpio = limpio.Trim('-');
        if (limpio.Length == 0) limpio = "u";
        return $"{limpio}-p{Environment.ProcessId}-{_inicio:HHmmss}";
    }

    private static string NombreDelOrigen()
    {
        try
        {
            DirectoryInfo? actual = new(AppContext.BaseDirectory);
            while (actual != null)
            {
                if (actual.Name.Equals("windows-client", StringComparison.OrdinalIgnoreCase)
                    && actual.Parent != null) return actual.Parent.Name;
                if (Directory.Exists(Path.Combine(actual.FullName, ".git"))
                    || File.Exists(Path.Combine(actual.FullName, ".git"))) return actual.Name;
                actual = actual.Parent;
            }
        }
        catch { }
        return "instalada";
    }
}
