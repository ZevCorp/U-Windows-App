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
/// Además, TODO se persiste a disco (%LOCALAPPDATA%\U\logs\u-AAAAMMDD.log): el ring de memoria son
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
    private static bool _fileBroken; // si el disco falla una vez, no insistir en cada línea

    public static event EventHandler<string>? Logged;

    public static void Log(string tag, string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] {tag}: {message}";
        lock (_lock)
        {
            _entries.Add(line);
            if (_entries.Count > MaxEntries) _entries.RemoveAt(0);
            AppendToFile(line);
        }
        Logged?.Invoke(null, line);
    }

    public static IReadOnlyList<string> Snapshot()
    {
        lock (_lock) return _entries.ToArray();
    }

    public static void Clear()
    {
        lock (_lock) _entries.Clear();
    }

    /// <summary>Ruta del archivo de hoy (para abrirlo desde la UI o adjuntarlo a un reporte).</summary>
    public static string TodayFile() => Path.Combine(_logDir, $"u-{DateTime.Now:yyyyMMdd}.log");

    // Ya dentro del lock. El log jamás puede tumbar la app: cualquier fallo de disco apaga el sink
    // y la bitácora en memoria sigue como siempre.
    private static void AppendToFile(string line)
    {
        if (_fileBroken) return;
        try
        {
            Directory.CreateDirectory(_logDir);
            File.AppendAllText(TodayFile(), line + Environment.NewLine);
        }
        catch { _fileBroken = true; }
    }
}
