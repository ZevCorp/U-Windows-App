using System.Text;
using System.Windows.Automation;

namespace U.Graph.Surfaces;

/// <summary>
/// El "selector CSS" de UIA. Graph guarda un string opaco y nos lo devuelve intacto en el plan, así
/// que el formato lo elegimos nosotros — pero tiene que ser RE-RESOLUBLE en otra ejecución, en otra
/// sesión y quizá en otra máquina.
///
/// Formato: <c>uia:clave=valor;clave=valor</c>
///
///   uia:aid=txtUsuario;ct=Edit        ← AutomationId. El único de verdad estable.
///   uia:name=Aceptar;ct=Button        ← Name visible. Estable si no está traducido/duplicado.
///   uia:path=0/3/1/2;ct=Edit          ← Índices desde la ventana. Frágil: último recurso.
///
/// Se emiten los TRES cuando se puede: el mejor como selector y los demás como
/// <c>surfaceHints.alternativeTargets</c>, que es la convención que Graph ya entiende. Si la app
/// cambia y el AutomationId desaparece, el ejecutor cae al name y luego al path.
///
/// Ni siquiera el AutomationId es garantía: Microsoft avisa de que "a major drawback to using
/// AutomationID for recording user interactions in a volatile UI is the probability of catastrophic
/// change in the UI". De ahí que los alternativos no sean un lujo — son el plan de contingencia.
///
/// RuntimeId queda fuera: se documenta como OPACO y reutilizable en el tiempo. Identifica una
/// instancia viva, no el mismo control lógico en otra ejecución, así que no sirve de selector
/// persistente.
/// </summary>
public static class UiaSelector
{
    public const string Prefix = "uia:";

    public static string ByAutomationId(string automationId, string controlType) =>
        $"{Prefix}aid={Escape(automationId)};ct={Escape(controlType)}";

    public static string ByName(string name, string controlType) =>
        $"{Prefix}name={Escape(name)};ct={Escape(controlType)}";

    public static string ByPath(IEnumerable<int> path, string controlType) =>
        $"{Prefix}path={string.Join("/", path)};ct={Escape(controlType)}";

    public static bool Owns(string selector) =>
        !string.IsNullOrWhiteSpace(selector) && selector.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>Parte un selector en sus claves. Devuelve vacío si no es de esta superficie.</summary>
    public static Dictionary<string, string> Parse(string selector)
    {
        var parts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Owns(selector)) return parts;

        foreach (string chunk in selector.Substring(Prefix.Length).Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = chunk.IndexOf('=');
            if (eq <= 0) continue;
            parts[chunk.Substring(0, eq).Trim()] = Unescape(chunk.Substring(eq + 1));
        }
        return parts;
    }

    /// <summary>
    /// Construye la condición de búsqueda de un selector. Null si el selector no trae nada con lo que
    /// buscar (el caso path se resuelve aparte, recorriendo índices).
    /// </summary>
    public static Condition? ConditionFor(Dictionary<string, string> parts)
    {
        var conditions = new List<Condition>();

        if (parts.TryGetValue("aid", out string? aid) && !string.IsNullOrWhiteSpace(aid))
            conditions.Add(new PropertyCondition(AutomationElement.AutomationIdProperty, aid));

        if (parts.TryGetValue("name", out string? name) && !string.IsNullOrWhiteSpace(name))
            conditions.Add(new PropertyCondition(AutomationElement.NameProperty, name));

        // El TIPO también filtra, y no era un adorno. Sin esto, «Documentos» resolvía al primer
        // elemento con ese nombre —el Pane de contenido de 1578×571— en vez del TreeItem del panel,
        // y el clic caía en el centro de la lista de archivos (medido el 2026-07-31). El selector
        // SIEMPRE llevaba el ct; lo que faltaba era usarlo aquí.
        if (parts.TryGetValue("ct", out string? ct) && !string.IsNullOrWhiteSpace(ct))
        {
            var tipo = ControlTypePorNombre(ct);
            if (tipo != null)
                conditions.Add(new PropertyCondition(AutomationElement.ControlTypeProperty, tipo));
        }

        if (conditions.Count == 0) return null;
        return conditions.Count == 1 ? conditions[0] : new AndCondition(conditions.ToArray());
    }

    /// <summary>
    /// ¿ES EL MISMO NOMBRE, salvo los espacios de los bordes? Promesa 331 (spec 041). Puro.
    /// </summary>
    /// <remarks>
    /// MEDIDO EL 2026-09-18: Chrome nombra su barra 'Barra de direcciones y de búsqueda ' —con un espacio al final—.
    /// El lector recorta las etiquetas, así que el selector guardado no lo lleva, y la búsqueda por
    /// <see cref="PropertyCondition"/> es EXACTA: no casaba nunca. 9 `map_take` fallidos en dos pruebas del dueño.
    /// Dos lados de una comparación que salen de funciones distintas (aprendizaje nº16).
    ///
    /// SOLO LOS BORDES, y respetando mayúsculas: «Buscar» no es «buscar» ni «Buscar en Google», y los espacios de
    /// dentro son parte del nombre. Vacío no casa con nada, ni con vacío (patrón nº9).
    /// </remarks>
    public static bool MismoNombre(string? guardado, string? real)
    {
        string a = Recorta(guardado), b = Recorta(real);
        return a.Length > 0 && b.Length > 0 && a.Equals(b, StringComparison.Ordinal);
    }

    // Trim() ya quita el espacio duro (U+00A0), los tabuladores y los saltos: todo lo que char.IsWhiteSpace reconoce.
    private static string Recorta(string? v) => (v ?? "").Trim();

    /// <summary>
    /// LA BÚSQUEDA DE RESPALDO: lo demás del selector —el tipo, el id— SIN el nombre, para comparar el nombre a mano
    /// con <see cref="MismoNombre"/>. Null si el selector no trae nombre (no hace falta respaldo) o si no trae nada
    /// más que el nombre: recorrer la ventana entera comparando no es un respaldo, es otro lector.
    /// </summary>
    public static Condition? CondicionSinNombre(Dictionary<string, string> parts)
    {
        if (!parts.TryGetValue("name", out string? name) || string.IsNullOrWhiteSpace(name)) return null;
        var sinNombre = new Dictionary<string, string>(parts, StringComparer.OrdinalIgnoreCase);
        sinNombre.Remove("name");
        return ConditionFor(sinNombre);
    }

    /// <summary>El nombre de tipo que emite ControlTypeName (sin el «ControlType.») → el ControlType.</summary>
    private static ControlType? ControlTypePorNombre(string n) => n.Trim().ToLowerInvariant() switch
    {
        "button" => ControlType.Button,
        "menuitem" => ControlType.MenuItem,
        "listitem" => ControlType.ListItem,
        "treeitem" => ControlType.TreeItem,
        "tabitem" => ControlType.TabItem,
        "hyperlink" => ControlType.Hyperlink,
        "edit" => ControlType.Edit,
        "checkbox" => ControlType.CheckBox,
        "radiobutton" => ControlType.RadioButton,
        "combobox" => ControlType.ComboBox,
        "splitbutton" => ControlType.SplitButton,
        "text" => ControlType.Text,
        "pane" => ControlType.Pane,
        "image" => ControlType.Image,
        _ => null, // desconocido: no se filtra por tipo, solo por nombre (comportamiento anterior)
    };

    // Los separadores del formato tienen que sobrevivir dentro de un Name arbitrario.
    private static string Escape(string v) =>
        (v ?? "").Replace("%", "%25").Replace(";", "%3B").Replace("=", "%3D");

    private static string Unescape(string v) =>
        (v ?? "").Replace("%3D", "=").Replace("%3B", ";").Replace("%25", "%");
}
