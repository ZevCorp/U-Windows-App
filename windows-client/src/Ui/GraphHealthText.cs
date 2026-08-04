using System.Windows.Media;
using U.Graph;

namespace U.WindowsClient.Ui;

/// <summary>
/// Traduce lo que se sabe del enlace con Graph (<see cref="GraphHealth"/>) a un color y una frase.
///
/// **Si aparece un tercer indicador de conexión, tiene que pasar por aquí.** Ya hubo dos —el punto
/// de la sección Backend y el de la biblioteca de workflows— y los dos mentían igual porque cada uno
/// tenía su propia copia de la lógica. Contar cuántos sitios comparten una clase de error ANTES de
/// arreglar uno es un aprendizaje que en este repo ya se incumplió una vez, citándolo en el commit.
/// </summary>
public static class GraphHealthText
{
    /// <summary>El punto y el texto que describen la observación. Nada más decide colores.</summary>
    public static (Brush Dot, string Text) Describe(GraphObservation o)
    {
        string host = o.Host.Length > 0 ? o.Host : "Graph";
        string hace = Edad(o);

        return o.Link switch
        {
            GraphLink.SinKey => (UiPalette.InactivoBrush,
                "⚠ Sin API key. Una sola vez en dev: setx GRAPH_API_KEY \"tu_key\" y reinicia Ü."),

            // No es un fallo: es que todavía no ha pasado nada. Pintarlo de rojo sería afirmar algo
            // que no se ha comprobado; de verde, afirmar lo contrario.
            GraphLink.Desconocido => (UiPalette.AtencionBrush,
                $"· Hay credencial, pero aún no se ha hablado con {host} en esta sesión."),

            GraphLink.Ok => (UiPalette.VivoBrush, $"✓ {host} respondió{hace}."),

            GraphLink.KeyRechazada => (UiPalette.AtencionBrush,
                $"⚠ {host} rechazó la API key (HTTP {o.StatusCode}){hace}."),

            GraphLink.ErrorDelServidor => (UiPalette.FalloBrush,
                $"✕ {host} respondió HTTP {o.StatusCode}{hace}."),

            GraphLink.SinContacto => (UiPalette.FalloBrush,
                $"✕ No se pudo contactar con {host}{hace}: {o.Reason}"),

            GraphLink.SinRespuesta => (UiPalette.FalloBrush,
                $"✕ {host} no contestó{hace}: {o.Reason}"),

            _ => (UiPalette.InactivoBrush, $"· Estado desconocido de {host}."),
        };
    }

    /// <summary>
    /// «hace 12 s» / «hace 3 min». La edad importa tanto como el estado: un verde de hace media hora
    /// no dice que Graph esté vivo AHORA, dice que lo estaba entonces.
    /// </summary>
    private static string Edad(GraphObservation o)
    {
        if (o.Edad is not TimeSpan t) return "";
        if (t.TotalSeconds < 2) return " ahora mismo";
        if (t.TotalSeconds < 90) return $" hace {t.TotalSeconds:0} s";
        if (t.TotalMinutes < 90) return $" hace {t.TotalMinutes:0} min";
        return $" hace {t.TotalHours:0} h";
    }
}
