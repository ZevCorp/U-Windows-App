using System.IO;
using System.Runtime.InteropServices;

namespace U.WindowsClient.Diagnostics;

/// <summary>
/// Una consola de verdad, adosada a la app, para ver un proceso largo MIENTRAS ocurre.
///
/// Nace por el arquitecto: corre varios minutos razonando y llamando herramientas, y su salida solo
/// llegaba al log. Para saber si estaba pensando, atascado o muerto había que abrir el visor y
/// refrescar — y un agente que no se ve por ningún sitio se parece demasiado a uno colgado
/// (2026-08-08, pedido por el usuario).
///
/// NO SUSTITUYE AL LOG, lo acompaña. El log es la evidencia que se lee después; esto es la ventana
/// que se mira durante. Escribir en los dos cuesta nada y cada uno sirve para lo que sirve.
///
/// Una app WPF no tiene consola: se pide una con <c>AllocConsole</c>. Solo se puede tener UNA por
/// proceso, así que <see cref="Abrir"/> es idempotente y lo que hace en las siguientes llamadas es
/// cambiarle el título y separar la corrida con una línea.
/// </summary>
public static class ConsolaViva
{
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AllocConsole();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GetConsoleWindow();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool SetConsoleTitle(string t);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    private const int SW_SHOW = 5, SW_RESTORE = 9;

    private static readonly object _lock = new();
    private static bool _rota;   // si el SO no da consola, no insistir en cada línea

    /// <summary>
    /// Abrir la consola —o traerla al frente si ya estaba— y titularla con lo que se va a ver.
    /// Silenciosa si falla: quedarse sin ventana de diagnóstico no puede tumbar lo que se diagnostica.
    /// </summary>
    public static void Abrir(string titulo)
    {
        if (_rota) return;
        lock (_lock)
        {
            try
            {
                var h = GetConsoleWindow();
                if (h == IntPtr.Zero)
                {
                    if (!AllocConsole()) { _rota = true; return; }
                    h = GetConsoleWindow();
                    // La salida estándar de un proceso WPF apunta a la nada hasta que hay consola.
                    var w = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
                    Console.SetOut(w);
                    Console.OutputEncoding = System.Text.Encoding.UTF8;
                }
                else
                {
                    // Ya había una de una corrida anterior: se separa para que no se lean pegadas.
                    Console.WriteLine();
                    Console.WriteLine(new string('─', 70));
                }
                SetConsoleTitle(titulo);
                if (h != IntPtr.Zero) { ShowWindow(h, SW_RESTORE); ShowWindow(h, SW_SHOW); }
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {titulo}");
            }
            catch { _rota = true; }
        }
    }

    /// <summary>Una línea, con su hora. No hace nada si nunca se abrió la consola.</summary>
    public static void Escribir(string linea)
    {
        if (_rota) return;
        try
        {
            if (GetConsoleWindow() == IntPtr.Zero) return;
            lock (_lock) Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {linea}");
        }
        catch { _rota = true; }
    }
}
