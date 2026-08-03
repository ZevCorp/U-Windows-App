using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace U.WindowsClient.Navigation;

/// <summary>
/// Detecta que algo se ha cruzado en el camino: un diálogo, un aviso, una confirmación.
///
/// Vive aparte porque hace falta en los DOS sitios y por razones distintas. La ejecución necesita
/// responderle —para eso está map_unblock—; el MAPEO necesita PARARSE, porque recorrer con un
/// diálogo delante es pulsar a ciegas: los clics aterrizan donde no deben y todo lo que se aprenda
/// después describe un camino que nadie hizo. Tener la detección solo en la capa MCP dejaba al
/// recorrido expuesto (2026-08-03, mapeando Configuración).
///
/// Se reconoce por la FORMA —pocos botones de respuesta más texto que explica— y no por el título,
/// que cambia con el idioma y con cada versión. El explorador normal, con 18 botones, no se
/// confunde con un diálogo.
/// </summary>
public static class Interrupcion
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

    /// <summary>Lee el diálogo que haya delante. Opciones vacías = no hay ninguno.</summary>
    public static (string Titulo, List<string> Textos, List<string> Opciones) Leer()
    {
        var textos = new List<string>();
        var opciones = new List<string>();
        string titulo = "";
        try
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return (titulo, textos, opciones);
            var ventana = AutomationElement.FromHandle(fg);
            if (ventana == null) return (titulo, textos, opciones);

            // PRIMERO el modal EMBEBIDO, que es el caso que se nos escapaba. Muchas apps modernas
            // no abren una ventana aparte: superponen el diálogo dentro de la suya (Configuración
            // hace eso con «Cambiar el nombre de tu PC»). Contar botones no lo detecta —al abrirse
            // el diálogo el número SUBE, no baja— y el recorrido seguía pulsando detrás, a ciegas
            // (2026-08-03). UIA lo marca con IsDialog, que es exactamente esta pregunta.
            var modal = ventana.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.IsDialogProperty, true));

            // Sin modal embebido: puede ser una ventana de diálogo aparte, que se reconoce por su
            // forma —pocos botones VISIBLES más texto que explica—. Los ocultos no cuentan: una app
            // arrastra decenas fuera de pantalla y falsearían la cuenta.
            var v = modal ?? ventana;
            foreach (AutomationElement b in v.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)))
            {
                try
                {
                    if (b.Current.IsOffscreen) continue;
                    string n = b.Current.Name?.Trim() ?? "";
                    if (n.Length > 0 && !opciones.Contains(n)) opciones.Add(n);
                }
                catch { }
            }
            // El límite solo aplica cuando NO hay modal marcado: si UIA dice que es un diálogo, lo es.
            if (opciones.Count == 0 || (modal == null && opciones.Count > 8))
            { opciones.Clear(); return (titulo, textos, opciones); }

            foreach (AutomationElement t in v.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text)))
            {
                try
                {
                    if (t.Current.IsOffscreen) continue;
                    string n = t.Current.Name?.Trim() ?? "";
                    if (n.Length > 12 && !textos.Contains(n)) textos.Add(n);
                }
                catch { }
            }
            // Un modal marcado por UIA es un diálogo aunque no traiga texto largo; el heurístico sí
            // lo exige, porque sin explicación no hay forma de distinguirlo de una barra cualquiera.
            if (textos.Count == 0 && modal == null) { opciones.Clear(); return (titulo, textos, opciones); }

            try { titulo = v.Current.Name?.Trim() ?? ""; } catch { }
        }
        catch { opciones.Clear(); }
        return (titulo, textos, opciones);
    }

    /// <summary>¿Hay algo cruzado delante? Consulta rápida para guardas.</summary>
    public static bool Hay() => Leer().Opciones.Count > 0;
}
