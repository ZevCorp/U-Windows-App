using System.Diagnostics;
using System.Runtime.InteropServices;

namespace U.WindowsClient.Uia;

/// <summary>
/// Lo nuestro: cómo se reconoce que una ventana, un proceso o una superficie son de Ü.
///
/// Es la pregunta que más veces se contesta en esta aplicación —el observador no puede observarse a
/// sí mismo sin entrar en bucle— y se contestaba en SIETE sitios de CUATRO formas distintas: por
/// PID contra Environment.ProcessId, por PID contra GetCurrentProcess().Id, por nombre de proceso
/// «U», y comparando la cadena «//u.exe» (2026-08-04).
///
/// Las tres últimas son aproximaciones frágiles: dependen de cómo se llame el ejecutable, y este se
/// distribuye. El PID no depende de nada. Tenerlas repartidas significaba que el día que el nombre
/// cambie, unas se enteran y otras no — y las que no, dejan al sistema mirándose a sí mismo:
/// registrando su propia interfaz como terreno, o dándose por bloqueado por su propia capa.
///
/// Una pregunta, una respuesta, y que reciba lo que cada quien tenga a mano.
/// </summary>
public static class Propio
{
    /// <summary>El nombre del proceso, sea cual sea. No se escribe a mano en ningún sitio.</summary>
    public static string Proceso { get; } = SacarNombre();

    private static string SacarNombre()
    {
        try { return Process.GetCurrentProcess().ProcessName; }
        catch { return "U"; }
    }

    /// <summary>¿Esta ventana es nuestra? La forma FIABLE: por PID, que no depende del nombre.</summary>
    public static bool EsVentana(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        try
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            return pid == (uint)Environment.ProcessId;
        }
        catch { return false; }
    }

    /// <summary>¿Este nombre de proceso es el nuestro? Para donde solo se tiene el nombre.</summary>
    public static bool EsProceso(string proc) =>
        !string.IsNullOrWhiteSpace(proc)
        && proc.Equals(Proceso, StringComparison.OrdinalIgnoreCase);

    /// <summary>¿Esta superficie es de nuestra propia interfaz? Ej.: uia://u.exe/…</summary>
    public static bool EsSuperficie(string idUOrigen) =>
        !string.IsNullOrWhiteSpace(idUOrigen)
        && idUOrigen.Contains($"//{Proceso}.exe", StringComparison.OrdinalIgnoreCase);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
}
