using System.Globalization;
using System.Text;

namespace U.WindowsClient.Uia;

/// <summary>
/// «De lo que hay en pantalla, ¿a qué se refiere esto que me han dicho?»
///
/// Una pregunta, una respuesta, un sitio. Antes cada quien la respondía a su manera: el que señala
/// buscaba por <c>Equals</c> y luego por <c>Contains</c>; el que pulsa buscaba en el mapa por
/// etiqueta exacta; y había DOS normalizadores de texto distintos escritos por separado. El
/// resultado es el que se vio el 2026-08-05: el asistente decía que veía la barra de búsqueda pero
/// que no podía pulsarla, porque ver y pulsar preguntaban a fuentes distintas y con reglas
/// distintas.
///
/// Aquí no se decide NADA sobre si conviene pulsar —eso es de <c>SafeToClick</c>— ni sobre lo que
/// el mapa recuerda. Solo se empareja lo dicho con lo que está a la vista.
/// </summary>
public static class Reconocedor
{
    /// <summary>
    /// El texto reducido a lo que de verdad distingue: sin tildes, sin mayúsculas, sin puntuación.
    ///
    /// «Imágenes» y «imagenes» son la misma carpeta, y estaban entrando en el mapa como dos sitios
    /// distintos. Quien dicta por voz no pone tildes, y quien lee de UIA las recibe: si el
    /// emparejamiento distingue, no se encuentran nunca.
    /// </summary>
    public static string Normalizar(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        string d = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (char c in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) || c == ' ' ? c : ' ');
        }
        // Espacios de más no distinguen nada y sí impiden encontrar.
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Cómo se le pide a UIA este mismo elemento más tarde. Es el formato que ya entiende
    /// <c>UiaExecutor</c>; se pone aquí para que quien reconoce y quien pulsa no puedan discrepar.
    /// </summary>
    /// <remarks>
    /// EL AutomationId MANDA CUANDO LO HAY, y esto no es una regla nueva: es la que
    /// <c>UiaSurface.SelectorsFor</c> ya aplicaba por su cuenta desde el 2026-08-01. Tenerla en un
    /// solo sitio otra vez era el problema de siempre —el vigilante de clics escribía
    /// «uia:aid=…» y el observador «uia:name=…», dos vocabularios para la misma cosa, y de diez
    /// caminos aprendidos llegaban tres—.
    ///
    /// LO QUE ARREGLA: hay elementos cuyo NOMBRE cambia a propósito. El campo del captcha de la
    /// Procuraduría se llama con la pregunta que hace —«¿Cuanto es 9 - 2?», «los tres primeros
    /// dígitos», «los dos últimos dígitos»: tres cargas, tres nombres— y su id es siempre
    /// `txtRespuestaPregunta`. Apuntar por nombre a eso es apuntar a nada, y además llenaba el grafo
    /// de un elemento muerto por cada pregunta vista (2026-08-13, medido).
    ///
    /// UN aid NUMÉRICO NO ES UNA IDENTIDAD, ES UNA POSICIÓN. En la lista del explorador cada fila
    /// lleva su índice, así que «uia:aid=1;ct=ListItem» significa «el segundo de lo que haya ahora»
    /// y apunta a otro archivo en cuanto se reordena. Ahí el nombre, con todos sus defectos, sí
    /// describe la cosa. Es la misma excepción que ya hacía el ejecutor.
    /// </remarks>
    public static string SelectorDe(UiaReader.UiElement e)
    {
        string aid = (e.AutomationId ?? "").Trim();
        bool esPosicion = aid.Length > 0 && aid.All(char.IsDigit);
        return aid.Length > 0 && !esPosicion
            ? $"uia:aid={aid};ct={e.ControlType}"
            : $"uia:name={e.Label};ct={e.ControlType}";
    }

    /// <summary>
    /// Los elementos a los que puede referirse <paramref name="dicho"/>, del más ajustado al menos.
    ///
    /// Se devuelven TODOS los del mejor grado que haya dado resultado, nunca uno elegido a dedo
    /// entre varios: si dos cosas se llaman igual, quien llama decide o pregunta, pero esta capa no
    /// adivina. Adivinar entre dos destinos es justo lo que no debe hacer un reconocedor.
    /// </summary>
    public static IReadOnlyList<UiaReader.UiElement> Buscar(
        IReadOnlyList<UiaReader.UiElement> aLaVista, string dicho)
    {
        string q = Normalizar(dicho);
        if (q.Length == 0 || aLaVista.Count == 0) return Array.Empty<UiaReader.UiElement>();

        // Un selector es una respuesta ya dada: se acepta tal cual y no se interpreta.
        if (dicho.StartsWith("uia:", StringComparison.OrdinalIgnoreCase))
        {
            string nombre = System.Text.RegularExpressions.Regex.Match(dicho, @"name=([^;]+)").Groups[1].Value;
            if (nombre.Length > 0) q = Normalizar(nombre);
        }

        var conNombre = aLaVista.Where(e => e.Label.Length > 0)
                                .Select(e => (El: e, N: Normalizar(e.Label)))
                                .Where(x => x.N.Length > 0)
                                .ToList();

        // Por grados. El primero que dé algo manda: si hay un «Buscar» exacto, no interesa que
        // «Barra de búsqueda de la carpeta» también contenga la palabra.
        var grados = new Func<(UiaReader.UiElement El, string N), bool>[]
        {
            x => x.N == q,
            x => x.N.StartsWith(q, StringComparison.Ordinal),
            x => x.N.Contains(q, StringComparison.Ordinal),
            // Todas las palabras de lo dicho, en cualquier orden: «búsqueda barra» encuentra
            // «Barra de búsqueda». Al dictar por voz el orden se pierde con facilidad.
            x => q.Split(' ').All(p => x.N.Contains(p, StringComparison.Ordinal)),
        };

        foreach (var grado in grados)
        {
            var hit = conNombre.Where(grado)
                               .GroupBy(x => x.N, StringComparer.Ordinal)   // el mismo nombre, una vez
                               .Select(g => g.First().El)
                               .ToList();
            if (hit.Count > 0) return hit;
        }
        return Array.Empty<UiaReader.UiElement>();
    }
}
