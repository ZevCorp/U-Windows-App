using U.WindowsClient.Diagnostics;
using U.WindowsClient.SystemApi;

namespace U.WindowsClient.Onboarding;

/// <summary>
/// Lo que Ü mira del equipo la primera vez, para poder decir qué puede hacer POR ESTA PERSONA en
/// concreto y no recitar un folleto.
///
/// POR QUÉ EXISTE. Recién instalada, Ü no se presenta: aparece una carita en la esquina y el usuario
/// tiene que adivinar qué hacer con ella. Lo que convierte eso en una herramienta es la diferencia
/// entre «puedo automatizar aplicaciones» —que no significa nada— y «veo que tienes SAP, Chrome y
/// Excel; con SAP ya sé rellenar una historia clínica entera» (2026-08-16, pedido por el usuario:
/// que al instalar pregunte si quiere que escanee el computador).
///
/// LO QUE MIRA Y LO QUE NO. Solo la lista de aplicaciones del menú Inicio y qué hay abierto ahora
/// mismo. Nada de archivos, ni de documentos, ni de contenido: para decir «puedo ayudarte con Excel»
/// basta saber que Excel está instalado, y todo lo demás sería mirar dentro de la vida de alguien
/// para un saludo. Lo que se mira es exactamente lo que se le dice que se va a mirar.
/// </summary>
public static class Presentacion
{
    /// <summary>
    /// Las que Ü ya sabe conducir de verdad, con su frase concreta. No es «apps soportadas»: es lo
    /// que se puede prometer sin exagerar.
    ///
    /// Se nombra por proceso y no por el nombre visible del menú Inicio, porque ese cambia con el
    /// idioma y la versión («Google Chrome» vs «Chrome»), y el ejecutable no.
    /// </summary>
    private static readonly (string Proceso, string Nombre, string Puedo)[] LoQueSeConducir =
    {
        ("saplogon", "SAP",
            "abrir una historia clínica, navegar hasta triage y llenar sus campos desde la consulta web"),
        ("chrome", "Chrome",
            "moverme por páginas web, rellenar formularios y traerte datos de una pestaña a otra"),
        ("msedge", "Edge",
            "moverme por páginas web y rellenar formularios"),
        ("explorer", "el explorador de archivos",
            "buscar carpetas, mover archivos y ordenarte cosas sin que abras nada"),
        ("excel", "Excel",
            "leer y escribir celdas, y pasar datos de otra aplicación a una hoja"),
        ("winword", "Word",
            "escribir y editar documentos al dictado"),
        ("outlook", "Outlook",
            "leerte el correo y redactar respuestas"),
        ("notepad", "el Bloc de notas",
            "escribir al dictado"),
    };

    /// <summary>
    /// El resumen que Ü lee en voz alta. Va en texto llano y en segunda persona porque lo que sale de
    /// aquí no se pinta: se dice.
    /// </summary>
    public static string Escanear()
    {
        var partes = new List<string>();

        IReadOnlyList<AppInstalada> instaladas;
        try { instaladas = AppsInstaladas.Todas(); }
        catch (Exception e)
        {
            LogBus.Log("presentacion", $"no se pudo leer el menú Inicio: {e.Message}");
            instaladas = Array.Empty<AppInstalada>();
        }

        // Abiertas AHORA: es lo que hace que el saludo suene a esta máquina y no a un catálogo. Si
        // falla, se sigue: el escaneo vale igual sin esta parte.
        var abiertas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var p in System.Diagnostics.Process.GetProcesses())
            {
                if (p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrWhiteSpace(p.ProcessName))
                    abiertas.Add(p.ProcessName);
            }
        }
        catch (Exception e) { LogBus.Log("presentacion", $"no se pudo mirar lo abierto: {e.Message}"); }

        var reconocidas = LoQueSeConducir
            .Where(x => abiertas.Contains(x.Proceso)
                     || instaladas.Any(a => a.Nombre.Contains(x.Nombre, StringComparison.OrdinalIgnoreCase))
                     || instaladas.Any(a => a.Lnk.Contains(x.Proceso, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        partes.Add($"En este equipo hay {instaladas.Count} aplicaciones instaladas"
                 + (abiertas.Count > 0 ? $" y {abiertas.Count} abiertas ahora mismo." : "."));

        if (reconocidas.Count > 0)
        {
            partes.Add("De las que veo, estas las sé conducir hoy:");
            foreach (var r in reconocidas)
            {
                string estado = abiertas.Contains(r.Proceso) ? " (abierta ahora)" : "";
                partes.Add($"· {r.Nombre}{estado}: {r.Puedo}.");
            }
        }
        else
        {
            // NO SE FINGE QUE SE CONOCE ALGO. Prometer de más aquí es peor que quedarse corto: la
            // primera orden que no funcione confirma que no servía para nada.
            partes.Add("Ninguna de las que tengo aprendidas está en este equipo, "
                     + "así que empezaríamos desde cero: me enseñas una vez y a partir de ahí la repito sola.");
        }

        partes.Add("Y con cualquier aplicación, aunque no la conozca: me la enseñas una vez, "
                 + "la aprendo, y luego la repito sola cuando se lo pidas.");

        string resumen = string.Join("\n", partes);

        // SE ANOTA QUÉ SE ENCONTRÓ, NO SOLO CUÁNTO. Un recuento dice que el escaneo corrió; los
        // nombres dicen si acertó. Cuando alguien reporte «no me reconoció el SAP que tengo
        // abierto», la respuesta tiene que estar en el log y no en una nueva sesión de pruebas.
        LogBus.Log("presentacion",
            $"ESCANEO: {instaladas.Count} app(s) instaladas · {abiertas.Count} abierta(s) · "
            + $"{reconocidas.Count} que sé conducir");
        LogBus.Log("presentacion", reconocidas.Count > 0
            ? "reconocidas: " + string.Join(", ",
                reconocidas.Select(r => r.Nombre + (abiertas.Contains(r.Proceso) ? " (abierta)" : "")))
            : "ninguna reconocida. Procesos abiertos ahora: "
              + string.Join(", ", abiertas.OrderBy(x => x).Take(25)));
        return resumen;
    }

    /// <summary>
    /// Lo que se le manda a Ü al abrir la conversación la primera vez, para que salude ELLA y no
    /// espere a que hable el usuario.
    ///
    /// Se manda como turno del usuario porque es la única forma de que el modelo arranque hablando;
    /// va marcado como instrucción para que no lo lea en voz alta creyendo que se lo dijeron.
    /// </summary>
    public static string Saludo(string nombre) =>
        "[instrucción del sistema, no la leas en voz alta] Es la PRIMERA vez que te abren en este "
        + $"equipo{(string.IsNullOrWhiteSpace(nombre) ? "" : $" y la persona se llama {nombre}")}. "
        + "Preséntate en dos frases cortas: quién eres y que trabajas sobre las aplicaciones que ya "
        + "usa. Después haz UNA pregunta: si quiere que mires su computador para contarle qué puedes "
        + "hacer por él. Si dice que sí, llama a scan_computer y cuéntale el resultado con tus "
        + "palabras, sin leer la lista entera: lo más útil primero. Si dice que no, dilo bien y "
        + "quédate esperando.\n"
        + "ESA ES LA ÚNICA PREGUNTA DE CORTESÍA QUE HARÁS EN TODA LA CONVERSACIÓN. Se pregunta "
        + "porque mirar el equipo es idea TUYA y nadie te la pidió. A partir de ahí, todo lo que te "
        + "pidan lo haces sin volver a pedir permiso: la presentación no puede dejar la costumbre de "
        + "consultar antes de cada paso. No hagas nada más en este primer turno.";
}
