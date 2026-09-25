using System.Runtime.InteropServices;
using System.Text;

namespace U.Ciclo;

/// <summary>Dónde estoy: la ventana de delante, su proceso y su título.</summary>
public sealed record Ubicacion(IntPtr Ventana, int Pid, string Proceso, string Titulo)
{
    public string Pantalla => Titulo.Length > 0 ? $"{Proceso} · {Titulo}" : Proceso;
}

/// <summary>
/// «Dónde estoy» por Win32 y nada más (promesa 430). En main costaba 48-590 ms por el MCP y un localizador
/// con temporizador; la primitiva cuesta 0,29 ms (medido el 2026-09-24). Sin UIA: UIA es para los
/// accionables, no para saber qué ventana está delante.
/// </summary>
public static class Donde
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder s, int max);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint acceso, bool heredar, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder s, ref int tam);

    private static readonly int PidPropio = Environment.ProcessId;
    private static Ubicacion? _ultimaAjena;
    private static readonly Dictionary<uint, string> NombreDe = new();

    /// <summary>La ventana de delante tal cual, sea de quien sea.</summary>
    public static Ubicacion Leer()
    {
        var h = GetForegroundWindow();
        GetWindowThreadProcessId(h, out uint pid);
        var sb = new StringBuilder(256);
        GetWindowText(h, sb, sb.Capacity);
        return new Ubicacion(h, (int)pid, Proceso(pid), sb.ToString());
    }

    /// <summary>
    /// Dónde estoy DE VERDAD: la de delante, salvo que sea la propia burbuja de Ü — entonces la última
    /// ventana ajena que estuvo delante. Hablarle a Ü le da el foco, y sin esto el ciclo se leía a sí mismo.
    /// </summary>
    public static Ubicacion? Ahora()
    {
        var u = Elegir(Leer(), PidPropio, _ultimaAjena);
        if (u != null && u.Pid != PidPropio) _ultimaAjena = u;
        return u;
    }

    /// <summary>La regla, pura: la de delante si es ajena; si es la propia, la última ajena; si no hay, nada.</summary>
    public static Ubicacion? Elegir(Ubicacion delante, int pidPropio, Ubicacion? ultimaAjena)
    {
        if (delante == null) return ultimaAjena;
        if (delante.Pid == pidPropio || delante.Ventana == IntPtr.Zero) return ultimaAjena;
        return delante;
    }

    /// <summary>El nombre del ejecutable, sin «.exe». Una vez por pid: es la única parte que no es instantánea.</summary>
    private static string Proceso(uint pid)
    {
        lock (NombreDe)
            if (NombreDe.TryGetValue(pid, out var n)) return n;
        string nombre = "";
        var h = OpenProcess(0x1000 /* QUERY_LIMITED_INFORMATION */, false, pid);
        if (h != IntPtr.Zero)
        {
            try
            {
                var sb = new StringBuilder(1024);
                int tam = sb.Capacity;
                if (QueryFullProcessImageName(h, 0, sb, ref tam)) nombre = Path.GetFileNameWithoutExtension(sb.ToString());
            }
            finally { CloseHandle(h); }
        }
        lock (NombreDe) NombreDe[pid] = nombre;
        return nombre;
    }
}
