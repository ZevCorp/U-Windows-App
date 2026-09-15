using System;
using System.Collections.Generic;
using System.Linq;
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
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

    /// <summary>¿La ventana es del propio asistente? Nuestras ventanas nunca son un bloqueo.</summary>
    private static bool EsNuestra(IntPtr h) => Uia.Propio.EsVentana(h);

    /// <summary>Lee el diálogo que haya delante. Opciones vacías = no hay ninguno.</summary>
    public static (string Titulo, List<string> Textos, List<string> Opciones) Leer() => Leer(IntPtr.Zero);

    /// <summary>
    /// Lee el diálogo que haya EN ESA VENTANA (spec 020, promesa 233): la de trabajo de Ü, que no es
    /// la de delante cuando la persona sigue en la suya. El 2026-09-14, con la Tienda abierta por Ü y
    /// la persona en Chrome, «dónde estoy» contestaba con la barra de información de Chrome: una
    /// interrupción de la persona, no de Ü. Cero = la ventana de delante, como siempre.
    /// </summary>
    public static (string Titulo, List<string> Textos, List<string> Opciones) Leer(IntPtr donde)
    {
        var d = LeerDialogo(donde);
        return d == null ? ("", new List<string>(), new List<string>()) : (d.Titulo, d.Textos.ToList(), d.Opciones.ToList());
    }

    /// <summary>
    /// EL DIÁLOGO CON SUS BOTONES ENGANCHADOS (promesa 236), o null si no hay ninguno. Cada opción se pulsa
    /// sobre el elemento que se leyó, por Invoke (o Toggle, o Select): nunca por nombre en la ventana.
    /// </summary>
    public static Desbloqueo.Dialogo? LeerDialogo(IntPtr donde)
    {
        var textos = new List<string>();
        var opciones = new List<string>();
        var botones = new List<(string Nombre, AutomationElement El)>();
        string titulo = "";
        (string, List<string>, List<string>) nada = ("", new List<string>(), new List<string>());
        try
        {
            IntPtr fg = donde != IntPtr.Zero ? donde : GetForegroundWindow();
            if (fg == IntPtr.Zero) return null;

            // NUESTRAS PROPIAS VENTANAS NO SON UNA INTERRUPCIÓN. El panel del grafo tiene pocos
            // botones y textos largos, la misma forma que un diálogo, así que se detectaba a sí
            // mismo como un bloqueo y paraba el mapeo (2026-08-03). Un sistema que se confunde con
            // lo que está mirando no puede opinar sobre lo demás.
            if (EsNuestra(fg)) return null;

            var ventana = AutomationElement.FromHandle(fg);
            if (ventana == null) return null;

            // PRIMERO el modal EMBEBIDO, que es el caso que se nos escapaba. Muchas apps modernas
            // no abren una ventana aparte: superponen el diálogo dentro de la suya (Configuración
            // hace eso con «Cambiar el nombre de tu PC»). Contar botones no lo detecta —al abrirse
            // el diálogo el número SUBE, no baja— y el recorrido seguía pulsando detrás, a ciegas
            // (2026-08-03). UIA lo marca con IsDialog, que es exactamente esta pregunta.
            var modal = ventana.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.IsDialogProperty, true));

            // SI HAY NAVEGACIÓN, ES UNA APP, NO UN DIÁLOGO. Contar botones no basta como
            // discriminador: la ventana de Configuración pasó de 9 botones visibles a 6 según la
            // sección y de golpe se detectaba como diálogo, parando el mapeo de la app entera
            // (2026-08-03). La diferencia de fondo no es cuántos botones hay: un diálogo PREGUNTA
            // —texto y opciones, nada más— mientras que una app OFRECE IR a sitios. Si esto tiene
            // menú, lista o pestañas, no es una pregunta.
            if (modal == null)
            {
                foreach (var ct in new[] { ControlType.ListItem, ControlType.TreeItem, ControlType.TabItem })
                {
                    if (ventana.FindFirst(TreeScope.Descendants,
                            new PropertyCondition(AutomationElement.ControlTypeProperty, ct)) != null)
                        return null;   // hay a dónde ir: es una app
                }
            }

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
                    if (n.Length > 0 && !opciones.Contains(n)) { opciones.Add(n); botones.Add((n, b)); }
                }
                catch { }
            }
            // El límite solo aplica cuando NO hay modal marcado: si UIA dice que es un diálogo, lo es.
            if (opciones.Count == 0 || (modal == null && opciones.Count > 8)) return null;

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
            if (textos.Count == 0 && modal == null) return null;

            try { titulo = v.Current.Name?.Trim() ?? ""; } catch { }
        }
        catch { return null; }
        _ = nada;
        return new Desbloqueo.Dialogo(titulo, textos, opciones, opcion => PulsarBoton(botones, opcion));
    }

    /// <summary>Pulsa la opción sobre SU botón: Invoke, o Toggle, o Select. Sin patrón no se pulsa a ciegas.</summary>
    private static bool PulsarBoton(List<(string Nombre, AutomationElement El)> botones, string opcion)
    {
        var b = botones.FirstOrDefault(x => string.Equals(x.Nombre, opcion, StringComparison.OrdinalIgnoreCase));
        if (b.El == null) { Diagnostics.LogBus.Log("desbloqueo", $"la opción «{opcion}» no está entre los botones leídos"); return false; }
        try
        {
            if (b.El.TryGetCurrentPattern(InvokePattern.Pattern, out var i) && i is InvokePattern inv) { inv.Invoke(); Diagnostics.LogBus.Log("desbloqueo", $"pulsado «{opcion}» por Invoke sobre el botón del diálogo"); return true; }
            if (b.El.TryGetCurrentPattern(TogglePattern.Pattern, out var t) && t is TogglePattern tog) { tog.Toggle(); Diagnostics.LogBus.Log("desbloqueo", $"pulsado «{opcion}» por Toggle"); return true; }
            if (b.El.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var s) && s is SelectionItemPattern sel) { sel.Select(); Diagnostics.LogBus.Log("desbloqueo", $"pulsado «{opcion}» por Select"); return true; }
            Diagnostics.LogBus.Log("desbloqueo", $"el botón «{opcion}» no admite Invoke, Toggle ni Select: no se pulsa a ciegas");
            return false;
        }
        catch (Exception e) { Diagnostics.LogBus.Log("desbloqueo", $"pulsar «{opcion}» falló: {e.Message}"); return false; }
    }

    /// <summary>¿Hay algo cruzado delante? Consulta rápida para guardas.</summary>
    public static bool Hay() => Leer().Opciones.Count > 0 || EsOpaca();

    /// <summary>
    /// La pantalla de delante no expone NADA accionable: ni un botón, ni un texto, ni una lista.
    ///
    /// No es un lugar; es algo que no se puede leer. Hay diálogos de Windows —el de cambiar el
    /// nombre del equipo, clase Shell_Dialog— cuyo contenido es INVISIBLE para UIA: una búsqueda
    /// global de sus botones devuelve cero (comprobado el 2026-08-03). No se pueden leer ni pulsar,
    /// y ese es el límite honesto de esta tecnología.
    ///
    /// Detectarlo igual importa: la alternativa era reportarlo como «estás en tal sitio, 0 salidas»,
    /// que suena a mapa incompleto cuando en realidad hay algo tapando la pantalla. Saber que estás
    /// bloqueado —aunque no sepas por qué— es mejor que creer que estás en un sitio vacío.
    /// </summary>
    public static bool EsOpaca() => EsOpaca(IntPtr.Zero);

    /// <summary>La misma pregunta sobre UNA ventana dada; cero = la de delante.</summary>
    public static bool EsOpaca(IntPtr ventana)
    {
        try
        {
            IntPtr fg = ventana != IntPtr.Zero ? ventana : GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;

            // NUESTRAS PROPIAS VENTANAS TAMPOCO SON UN BLOQUEO OPACO. La misma regla que ya rige en
            // Leer(), que aquí faltaba: la barra flotante tiene una ventana a pantalla completa sin
            // nada que UIA pueda leer, así que en cuanto Ü pasaba al frente el sistema se declaraba
            // «BLOQUEADO por algo que NO puedo leer» —bloqueado por sí mismo— y dejaba de saber
            // dónde estaba el usuario (2026-08-04). Un sistema que se confunde con lo que está
            // mirando no puede opinar sobre lo demás; el guardia tiene que estar en los DOS sitios.
            if (EsNuestra(fg)) return false;

            var v = AutomationElement.FromHandle(fg);
            if (v == null) return false;
            foreach (var ct in new[] { ControlType.Button, ControlType.Text, ControlType.ListItem,
                                       ControlType.Edit, ControlType.TreeItem, ControlType.Hyperlink })
            {
                if (v.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ct)) != null)
                    return false;
            }
            return true;   // ni una sola cosa con la que interactuar
        }
        catch { return false; }
    }

    /// <summary>Cómo describirle a quien decide lo que hay delante, o "" si no hay nada cruzado.</summary>
    public static string Describir() => Describir(IntPtr.Zero);

    /// <summary>Lo mismo, sobre UNA ventana dada; cero = la de delante.</summary>
    public static string Describir(IntPtr ventana)
    {
        var (titulo, textos, opciones) = Leer(ventana);
        if (opciones.Count > 0)
            return $"INTERRUPCIÓN, no una ubicación: diálogo «{titulo}».\n"
                 + $"  Dice: {string.Join(" ", textos.Count > 3 ? textos.GetRange(0, 3) : textos)}\n"
                 + $"  Opciones: {string.Join(", ", opciones.ConvertAll(o => $"«{o}»"))}\n"
                 + "  No hay rutas desde aquí: primero hay que responder (map_unblock con `at`).";

        if (EsOpaca(ventana))
            return "BLOQUEADO por algo que NO puedo leer: la ventana de delante no expone ni un "
                 + "botón ni un texto a UIA. Hay diálogos de Windows así —el de cambiar el nombre "
                 + "del equipo, por ejemplo—. No puedo resolverlo por interfaz; hace falta cerrarlo "
                 + "a mano o con teclado (Escape suele valer).";
        return "";
    }
}
