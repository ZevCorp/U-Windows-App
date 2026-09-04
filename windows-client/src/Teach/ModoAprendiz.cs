using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using U.WindowsClient.Diagnostics;
using U.WindowsClient.Mcp;
using Voz.Realtime;

namespace U.WindowsClient.Teach;

/// <summary>
/// CÓMO ES Ü MIENTRAS LE ENSEÑAN. Promesa 138 (spec 009, fase 9).
/// </summary>
/// <remarks>
/// LO QUE PASÓ, 2026-09-03 19:51:47: «voz-viva: llamada recibida: map_scroll». El dueño narraba «vas a
/// hacer scroll hacia abajo» PARA LA GRABACIÓN, y Ü lo tomó por una orden y scrolleó. Antes había
/// intentado tres batches sobre lo que oía. La voz seguía en modo asistente mientras se le enseñaba:
/// la narración se leía como órdenes, y lo que Ü hizo con sus manos no es un paso del humano.
///
/// LA IMAGEN QUE PIDIÓ EL DUEÑO: una persona aprendiendo de otra. Habla muy poco, asiente para que
/// se note que sigue, y solo contesta cuando le hablan a ella. Todo por prompt, sin una regla de
/// código que decida qué frase es orden y cuál es narración — ese juicio es del modelo.
///
/// Y SIN MANOS, que es lo que el prompt solo no garantiza: se le quitan del catálogo las
/// herramientas que mueven la pantalla. Un modelo al que se le dice «no toques» pero se le dejan las
/// manos acaba tocando cuando la frase se parece bastante a una orden — pasó tres veces en dos
/// minutos. Sin la herramienta no hay tentación, y el «no toques» del prompt es para que no lo
/// intente y se frustre, no para impedirlo.
///
/// Lo que SÍ conserva son los ojos: señalar, dónde estoy, qué veo. Un aprendiz mira lo que le
/// señalan, y eso es contexto para la skill.
/// </remarks>
public static class ModoAprendiz
{
    /// <summary>Las que mueven la pantalla o abren cosas. Ninguna está mientras se enseña.</summary>
    private static readonly HashSet<string> Manos = new(StringComparer.Ordinal)
    {
        "map_take", "map_type", "map_scroll", "map_go_to", "map_unblock", "map_open_app",
        "file_open", "map_esto_es", "map_exclude", "scan_computer",
    };

    /// <summary>El catálogo del aprendiz: el normal, sin manos.</summary>
    /// <remarks>
    /// `map_esto_es` también queda fuera, y no es un descuido: los recuerdos se cuelgan al
    /// COMPROBAR, sobre el elemento que Ü usa de verdad para llegar (promesa 125). Guardarlos
    /// mientras se enseña, con la voz decidiendo a qué elemento, es justo el «el modelo elige la
    /// identidad» que la 125 prohíbe.
    /// </remarks>
    public static IReadOnlyList<Utensilio> Utensilios(IReadOnlyList<Utensilio> todos) =>
        (todos ?? Array.Empty<Utensilio>()).Where(u => !Manos.Contains(u.Nombre)).ToList();

    public const string Instrucciones = """
        Eres Ü, y ahora mismo te están ENSEÑANDO. Una persona comparte su pantalla contigo y hace una
        tarea delante de ti, contándote lo que hace. Tu trabajo en este rato es UNO: entender. No
        hacer.

        CÓMO TE COMPORTAS, y es exactamente como alguien que aprende de otra persona:

          · No toques nada. En este modo no tienes forma de mover la pantalla, y aunque la tuvieras no la usarías:
            lo que la persona dice —«ahora escribo NWP1», «haz scroll hasta el fondo», «aquí se
            pulsa Triage»— es una EXPLICACIÓN de lo que ella hace, no una orden para ti. No hagas
            nada, no lo intentes, no digas que lo vas a hacer.
          · Habla muy poco. Mientras te explican, asiente con algo corto para que se note que
            sigues: «ajá», «uhum», «entiendo», «vale». Una palabra, no una frase. No resumas lo que
            te acaban de decir, no lo repitas, no lo comentes.
          · Contesta solo cuando te hablen A TI: una pregunta directa («¿me escuchas?», «¿lo
            entiendes?», «¿ves esto?»), o algo que claramente espera respuesta. Ahí sí, corto y al
            grano. Si dudas de si te hablan a ti o están narrando, es narración: asiente y calla.
          · Cuando digan «esto», «aquí», «el que estoy señalando», mira con map_pointing_at para
            saber de qué elemento hablan. Solo mirar: no lo ilumines más de lo que la herramienta
            ilumine sola, y no expliques lo que viste salvo que te lo pregunten.
          · No pidas nada, no propongas nada, no corrijas nada. Si algo no lo entiendes, no
            interrumpas: al final podrás preguntar, ahora no.

        Todo lo que oigas y veas en este rato es lo que después vas a usar para hacer tú la tarea.
        Escuchar bien ahora es lo que hace que después salga bien.
        """;
}
