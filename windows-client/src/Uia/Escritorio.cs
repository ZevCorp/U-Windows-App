using System.Runtime.InteropServices;
using System.Text;

namespace U.WindowsClient.Uia;

/// <summary>
/// El escritorio de Windows: qué es, cómo se reconoce y cómo se llega.
///
/// Existe porque «¿esto es el escritorio?» se contestaba en CINCO sitios —por id de superficie, por
/// nombre de proceso, por clase de ventana, en la vista del grafo y en una comparación suelta dentro
/// del mapa— cada uno con su propia lista de nombres, mantenidas por separado. No eran copias
/// literales: cada una recibe algo distinto, y por eso parecían justificadas. Pero codifican el
/// MISMO concepto, así que el día que Windows añada otra clase de escritorio cuatro se quedan mal, y
/// la que se quede atrás será siempre la que nadie mira (2026-08-04).
///
/// Y ya había pasado con el gesto: mostrar el escritorio se hacía con Win+D en un sitio y por COM en
/// otro, y el de las teclas se rompía si el usuario tenía modificadores oprimidos.
///
/// La regla que esto aplica: cuando una pregunta se contesta en varios sitios, no se mantiene —se
/// desincroniza—. Una pregunta, una respuesta, y que reciba lo que cada quien tenga a mano.
/// </summary>
public static class Escritorio
{
    /// <summary>Cómo lo nombran el localizador y los workflows.</summary>
    private static readonly string[] Nombres = { "desktop", "escritorio", "program-manager", "progman" };

    /// <summary>Las clases de ventana del shell. WorkerW aparece con fondo dinámico.</summary>
    private static readonly string[] Clases = { "Progman", "WorkerW" };

    /// <summary>¿Este identificador de superficie es el escritorio? Ej.: uia://explorer.exe/program-manager</summary>
    public static bool EsId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        // Por CONTENIDO y no por final: al escritorio le puede llegar su sufijo de sección, y
        // comparar por el final no reconocía «program-manager#imágenes-acceso-directo» (2026-08-04).
        return id.Contains("/program-manager", StringComparison.OrdinalIgnoreCase)
            || id.StartsWith("uia://desktop", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>¿Este nombre de proceso o de destino se refiere al escritorio?</summary>
    public static bool EsProceso(string proc) =>
        !string.IsNullOrWhiteSpace(proc)
        && Nombres.Any(n => proc.Equals(n, StringComparison.OrdinalIgnoreCase));

    /// <summary>¿Esta ventana es el escritorio?</summary>
    public static bool EsVentana(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        try
        {
            var sb = new StringBuilder(64);
            GetClassName(hwnd, sb, sb.Capacity);
            string c = sb.ToString();
            return Clases.Any(k => c.Equals(k, StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    /// <summary>
    /// Mostrar el escritorio, SIN teclas.
    ///
    /// Se hacía con un Win+D sintético, y eso falla en cuanto quien lo pide tiene modificadores
    /// oprimidos: al pulsarlo desde la capa del grafo —que exige mantener Ctrl+Shift— llegaba
    /// Ctrl+Shift+Win+D, un atajo que no existe, y el sistema solo pitaba (2026-08-04). El shell
    /// sabe hacerlo por COM, y así no depende de qué tenga el usuario en las manos.
    /// </summary>
    public static bool Mostrar()
    {
        try
        {
            var tipo = Type.GetTypeFromProgID("Shell.Application");
            if (tipo == null) return false;
            var shell = Activator.CreateInstance(tipo);
            if (shell == null) return false;
            shell.GetType().InvokeMember("MinimizeAll",
                System.Reflection.BindingFlags.InvokeMethod, null, shell, null);
            return true;
        }
        catch { return false; }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
}
