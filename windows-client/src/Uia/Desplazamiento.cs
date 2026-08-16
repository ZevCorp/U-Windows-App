using System.Runtime.InteropServices;
using System.Windows.Automation;
using U.WindowsClient.Actions;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Uia;

/// <summary>
/// DESPLAZAR LA PANTALLA, y saber si se movió.
/// </summary>
/// <remarks>
/// Faltaba entero. El usuario pidió por voz «scrollea hasta arriba» y Ü contestó «no puedo scrolear
/// directamente, ¿ves algo que pueda pulsar para bajar?» — y era verdad: no había ninguna herramienta
/// que hiciera esto, así que el modelo ni siquiera lo intentó (2026-08-16, en el log).
///
/// SE HACE POR UIA Y NO POR LA RUEDA DEL RATÓN, y el motivo es que la rueda no se puede comprobar:
/// manda una muesca donde esté el puntero —que puede estar sobre otra ventana, o sobre un panel que
/// no es el que se quería mover— y contesta que sí siempre. `ScrollPattern` dice EN QUÉ PORCENTAJE
/// está antes y después, así que «se movió» deja de ser una suposición.
///
/// Y cuando la pantalla no expone ese patrón se cae al teclado, que es lo que haría una persona;
/// pero entonces se DICE que no se pudo comprobar, en vez de contestar que sí porque no falló nada.
/// </remarks>
public static class Desplazamiento
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

    public enum Hacia { Arriba, Abajo, Inicio, Final }

    /// <summary>Lee «arriba», «abajo», «inicio»/«principio», «final»/«fondo». Abajo por defecto.</summary>
    public static Hacia Leer(string texto)
    {
        string t = (texto ?? "").Trim().ToLowerInvariant();
        if (t.Contains("arriba") || t.Contains("up") || t.Contains("sub")) return Hacia.Arriba;
        if (t.Contains("inicio") || t.Contains("principio") || t.Contains("top")) return Hacia.Inicio;
        if (t.Contains("final") || t.Contains("fondo") || t.Contains("abajo del todo")
            || t.Contains("bottom")) return Hacia.Final;
        return Hacia.Abajo;
    }

    /// <summary>Desplaza y cuenta qué pasó, en castellano y para quien preguntó.</summary>
    public static string Mover(Hacia hacia)
    {
        var ventana = GetForegroundWindow();
        var scroll = BuscarScroll(ventana);

        if (scroll != null)
        {
            double antes = scroll.Current.VerticalScrollPercent;
            try
            {
                switch (hacia)
                {
                    case Hacia.Inicio: scroll.SetScrollPercent(ScrollPatternIdentifiers.NoScroll, 0); break;
                    case Hacia.Final: scroll.SetScrollPercent(ScrollPatternIdentifiers.NoScroll, 100); break;
                    case Hacia.Arriba: scroll.ScrollVertical(ScrollAmount.LargeDecrement); break;
                    default: scroll.ScrollVertical(ScrollAmount.LargeIncrement); break;
                }
            }
            catch (Exception e)
            {
                LogBus.Log("scroll", $"UIA no dejó desplazar: {e.Message}; se prueba con el teclado");
                return PorTeclado(hacia, "el patrón de desplazamiento falló");
            }

            System.Threading.Thread.Sleep(350);
            double despues = scroll.Current.VerticalScrollPercent;
            LogBus.Log("scroll", $"{hacia}: {antes:N0}% → {despues:N0}%");

            // SE COMPRUEBA POR CONSECUENCIA. Y no moverse no siempre es un fallo: si ya estabas
            // abajo del todo, pedir «abajo» no puede hacer nada, y decir «no pude» sería mentir
            // sobre el estado de la pantalla.
            if (Math.Abs(despues - antes) > 0.5)
                return $"desplazado {Nombre(hacia)}: ibas por el {antes:N0}% y ahora estás en el {despues:N0}%.";

            bool alTope = (hacia is Hacia.Arriba or Hacia.Inicio && despues <= 0.5)
                       || (hacia is Hacia.Abajo or Hacia.Final && despues >= 99.5);
            return alTope
                ? $"ya estabas {(hacia is Hacia.Arriba or Hacia.Inicio ? "arriba del todo" : "al final")}: no hay más."
                : $"no se movió (sigue en el {despues:N0}%). Puede que lo que hay que desplazar sea otro panel.";
        }

        return PorTeclado(hacia, "esta pantalla no expone desplazamiento a UIA");
    }

    /// <summary>
    /// Lo que haría una persona: las teclas. Se dice SIEMPRE que no se pudo comprobar — sin
    /// porcentaje que leer, «no falló» no es «se movió», y darlo por bueno es como contestar que sí
    /// porque nadie miró.
    /// </summary>
    private static string PorTeclado(Hacia hacia, string porque)
    {
        bool ok = hacia switch
        {
            Hacia.Inicio => ConControl("home"),
            Hacia.Final => ConControl("end"),
            Hacia.Arriba => Repetir("up", 12),
            _ => Repetir("down", 12),
        };
        LogBus.Log("scroll", $"{hacia} por teclado ({porque}) → {(ok ? "enviado" : "no se pudo")}");
        return ok
            ? $"mandé las teclas para ir {Nombre(hacia)}, pero esta pantalla no dice en qué punto está: "
            + "no puedo confirmarte que se haya movido."
            : $"no pude desplazar {Nombre(hacia)}: {porque}.";
    }

    private static bool Repetir(string tecla, int veces)
    {
        for (int i = 0; i < veces; i++)
        {
            if (!InputExecutor.Key(tecla)) return false;
            System.Threading.Thread.Sleep(15);
        }
        return true;
    }

    private static bool ConControl(string tecla)
    {
        // Ctrl+Inicio / Ctrl+Fin: lo que se pulsa para ir al principio o al final de un documento.
        const ushort VK_CONTROL = 0x11;
        Teclado(VK_CONTROL, false);
        bool ok = InputExecutor.Key(tecla);
        Teclado(VK_CONTROL, true);
        return ok;
    }

    private static string Nombre(Hacia h) => h switch
    {
        Hacia.Arriba => "hacia arriba",
        Hacia.Inicio => "al principio",
        Hacia.Final => "al final",
        _ => "hacia abajo",
    };

    /// <summary>
    /// El elemento que de verdad se desplaza. Se busca DE DENTRO HACIA FUERA, empezando por lo que
    /// tiene el foco: una página web tiene varios contenedores desplazables —la página, un panel
    /// lateral, un desplegable— y quedarse con el primero que aparezca en el árbol mueve el que no
    /// es. El que tiene el foco es el que la persona está mirando.
    /// </summary>
    private static ScrollPattern? BuscarScroll(IntPtr ventana)
    {
        try
        {
            var raiz = AutomationElement.FromHandle(ventana);
            if (raiz == null) return null;

            var desde = AutomationElement.FocusedElement ?? raiz;
            for (var e = desde; e != null; e = Padre(e))
            {
                if (e.TryGetCurrentPattern(ScrollPattern.Pattern, out var p)
                    && p is ScrollPattern sp && sp.Current.VerticallyScrollable) return sp;
                if (e.Equals(raiz)) break;
            }

            // Sin foco útil: el primero de la ventana que se pueda desplazar.
            var todos = raiz.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.IsScrollPatternAvailableProperty, true));
            foreach (AutomationElement e in todos)
                if (e.TryGetCurrentPattern(ScrollPattern.Pattern, out var p)
                    && p is ScrollPattern sp2 && sp2.Current.VerticallyScrollable) return sp2;
        }
        catch (Exception e) { LogBus.Log("scroll", $"buscando qué desplazar: {e.Message}"); }
        return null;
    }

    private static AutomationElement? Padre(AutomationElement e)
    {
        try { return TreeWalker.ControlViewWalker.GetParent(e); } catch { return null; }
    }

    [DllImport("user32.dll")] private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);
    private static void Teclado(ushort vk, bool arriba) => keybd_event((byte)vk, 0, arriba ? 2u : 0u, IntPtr.Zero);
}
