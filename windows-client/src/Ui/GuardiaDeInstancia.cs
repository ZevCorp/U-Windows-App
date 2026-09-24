using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using U.Graph.Surfaces;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Ui;

/// <summary>
/// Mantiene una carita por worktree y escritorio virtual.
///
/// La ruta del ejecutable forma parte de la identidad: dos worktrees pueden correr a la vez.
/// El escritorio también forma parte de ella: una persona puede tener una carita en cada escritorio.
/// </summary>
internal sealed class GuardiaDeInstancia : IDisposable
{
    private const string Prefijo = "Local\\U.Face.";
    private Mutex? _candado;
    private string _identidad = "";
    private Guid _escritorio;

    public static bool IntentarIniciar(string identidad, Guid escritorio, out GuardiaDeInstancia? guardia)
    {
        guardia = null;
        if (HayOtraVentana(identidad, escritorio))
        {
            TraerAlFrente(identidad, escritorio);
            return false;
        }

        var candidato = new GuardiaDeInstancia { _identidad = identidad, _escritorio = escritorio };
        string nombre = NombreDelCandado(identidad, escritorio);
        try
        {
            candidato._candado = new Mutex(initiallyOwned: true, name: nombre, createdNew: out bool nuevo);
            if (!nuevo)
            {
                candidato.Dispose();
                TraerAlFrente(identidad, escritorio);
                return false;
            }
            guardia = candidato;
            return true;
        }
        catch (Exception e)
        {
            candidato.Dispose();
            LogBus.Log("instancia", $"no se pudo crear el candado: {e.GetType().Name}: {e.Message}");
            // Si Windows no permite el mutex, no convertimos un problema de diagnóstico en una
            // segunda carita silenciosa: el proceso sí puede arrancar, pero queda registrado.
            guardia = null;
            return true;
        }
    }

    /// <summary>Actualiza el candado cuando la carita viaja a otro escritorio.</summary>
    public bool CambiarEscritorio(Guid nuevo)
    {
        if (nuevo == Guid.Empty || nuevo == _escritorio) return true;
        string nombre = NombreDelCandado(_identidad, nuevo);
        Mutex? siguiente = null;
        try
        {
            siguiente = new Mutex(initiallyOwned: true, name: nombre, createdNew: out bool creado);
            if (!creado)
            {
                siguiente.Dispose();
                LogBus.Log("instancia", $"el escritorio «{EscritorioVirtual.Nombre(nuevo)}» ya tiene una carita de este worktree");
                return false;
            }
            _candado?.Dispose();
            _candado = siguiente;
            _escritorio = nuevo;
            return true;
        }
        catch (Exception e)
        {
            siguiente?.Dispose();
            LogBus.Log("instancia", $"no se pudo cambiar el candado de escritorio: {e.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        try { _candado?.ReleaseMutex(); } catch { }
        _candado?.Dispose();
        _candado = null;
    }

    public static string IdentidadDelProceso()
    {
        string ruta = Process.GetCurrentProcess().MainModule?.FileName ?? AppContext.BaseDirectory;
        return Path.GetFullPath(ruta).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
    }

    private static string NombreDelCandado(string identidad, Guid escritorio)
    {
        byte[] datos = Encoding.UTF8.GetBytes(identidad + "|" + escritorio.ToString("D"));
        string hash = Convert.ToHexString(SHA256.HashData(datos));
        return Prefijo + hash;
    }

    private static bool HayOtraVentana(string identidad, Guid escritorio)
    {
        foreach (var proceso in ProcesosDelMismoEjecutable(identidad))
        {
            try
            {
                IntPtr hwnd = VentanaDelProceso(proceso.Id);
                if (hwnd != IntPtr.Zero && EscritorioVirtual.EscritorioDe(hwnd) == escritorio) return true;
            }
            catch { }
            finally { proceso.Dispose(); }
        }
        return false;
    }

    private static void TraerAlFrente(string identidad, Guid escritorio)
    {
        foreach (var proceso in ProcesosDelMismoEjecutable(identidad))
        {
            try
            {
                IntPtr hwnd = VentanaDelProceso(proceso.Id);
                if (hwnd == IntPtr.Zero || EscritorioVirtual.EscritorioDe(hwnd) != escritorio) continue;
                ShowWindow(hwnd, 9); // SW_RESTORE
                SetForegroundWindow(hwnd);
                LogBus.Publico("instancia", "segunda apertura ignorada: se enfocó la carita existente");
                return;
            }
            catch { }
            finally { proceso.Dispose(); }
        }
    }

    private static IEnumerable<Process> ProcesosDelMismoEjecutable(string identidad)
    {
        string nombre = Path.GetFileNameWithoutExtension(identidad);
        foreach (var proceso in Process.GetProcessesByName(nombre))
        {
            bool esElMismo = false;
            try { esElMismo = string.Equals(Path.GetFullPath(proceso.MainModule?.FileName ?? ""), identidad, StringComparison.OrdinalIgnoreCase); }
            catch { }
            if (esElMismo) yield return proceso;
            else proceso.Dispose();
        }
    }

    private static IntPtr VentanaDelProceso(int pid)
    {
        IntPtr hallada = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint dueño);
            if (dueño == (uint)pid && IsWindowVisible(hwnd)) { hallada = hwnd; return false; }
            return true;
        }, IntPtr.Zero);
        return hallada;
    }

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int comando);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
}
