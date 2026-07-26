using System.Windows;
using U.Graph.Surfaces;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Uia;

/// <summary>
/// Logging de diagnóstico del inspector visual. Cada clic que el inspector analiza deja aquí una
/// bitácora estructurada en <see cref="LogBus"/> (visible en la ventana de Logs) explicando POR QUÉ el
/// asistente resolvería —o no— el mismo elemento que el usuario tocó. No pinta nada: solo narra la
/// decisión que ya tomó <see cref="UiInspector"/>, para poder auditar el rojo/ámbar del overlay.
///
/// Se llama SOLO desde el diagnóstico de clic del inspector, que a su vez solo corre mientras el
/// inspector está encendido (el hook de ratón se instala en <see cref="UiInspector.Start"/> y se quita
/// en <see cref="UiInspector.Stop"/>). Por eso estas líneas aparecen en el panel de Logs únicamente con
/// el inspector activo, sin necesidad de una compuerta extra.
///
/// Motivación: en SAP GUI muchos controles comparten etiqueta (mismo Tooltip/Name) o no traen ninguna
/// (Tooltip/Name/Text vacíos). El asistente resuelve "por etiqueta, el primero en orden de lectura", así
/// que ahí es donde el Id guardado apunta a un control distinto del que el usuario quería. Este log
/// nombra la causa exacta (ambigua vs. sin-etiqueta), cuántos controles colisionan y con qué Id, para
/// convertir el "falla mucho en SAP" en algo medible y accionable.
/// </summary>
internal static class InspectorDiagnostics
{
    private const string Tag = "inspector";
    private const int MaxIdsListed = 6;

    // ── SAP ──────────────────────────────────────────────────────────────────
    // La identidad real del selector SAP es el Id absoluto (wnd[0]/usr/…). El asistente resuelve por
    // etiqueta y compara Id, por eso el diagnóstico gira en torno a la colisión de etiquetas.
    public static void LogSap(
        int px, int py, string? hitId,
        SapVisualElement clicked, SapVisualElement? intended,
        IReadOnlyList<SapVisualElement> all, bool mismatch)
    {
        string label = clicked.Label?.Trim() ?? "";
        bool emptyLabel = label.Length == 0;
        // Población que ve la resolución del asistente: mismo predicado que UiInspector.DiagnoseSap.
        var sharing = all
            .Where(e => e.BoundsKnown && string.Equals(e.Label.Trim(), label, StringComparison.OrdinalIgnoreCase))
            .ToList();
        int clickedRank = sharing.FindIndex(e => e.Id == clicked.Id); // 0 = el asistente lo tomaría a él
        string hitSrc = hitId != null ? "hit-test nativo" : "bbox (fallback)";

        if (mismatch && intended != null)
        {
            LogBus.Log(Tag,
                $"✗ MISMATCH @({px},{py}) — tocaste [{clicked.Type}] id={Short(clicked.Id)} etiqueta={Q(label)}; " +
                $"el asistente ejecutaría id={Short(intended.Id)} (otro control con la misma etiqueta).");

            if (emptyLabel)
                LogBus.Log(Tag,
                    $"   causa=SIN-ETIQUETA · {clicked.Type} sin Tooltip/Name/Text → el asistente no tiene texto " +
                    $"estable y resuelve al 1º de {sharing.Count} controles sin etiqueta. " +
                    "fix: guardar el Id absoluto o el texto de un GuiLabel adyacente.");
            else
                LogBus.Log(Tag,
                    $"   causa=ETIQUETA-AMBIGUA · {Q(label)} compartida por {sharing.Count} controles → el asistente " +
                    $"toma el 1º ({Short(intended.Id)}); tú tocaste el nº{clickedRank + 1}. " +
                    $"fix: desambiguar por Id/posición. comparten: {Ids(sharing)}");

            if (hitId == null)
                LogBus.Log(Tag,
                    "   nota=hit-test nativo sin resultado; el \"tocaste\" viene del bbox más pequeño bajo el punto " +
                    "(aproximado, no verdad de terreno).");
            return;
        }

        if (intended == null)
        {
            LogBus.Log(Tag,
                $"✗ NO-RESOLUBLE @({px},{py}) — tocaste [{clicked.Type}] id={Short(clicked.Id)} etiqueta={Q(label)} " +
                "pero ningún control resoluble comparte esa etiqueta; el asistente no lo encontraría.");
            return;
        }

        // Coincide (ámbar). Anota si la coincidencia es frágil: acertó por ser el 1º, pero hay colisión.
        string ok = $"✓ OK @({px},{py}) [{clicked.Type}] id={Short(clicked.Id)} etiqueta={Q(label)} · vía {hitSrc}";
        if (sharing.Count > 1)
            ok += $" · FRÁGIL: {sharing.Count} controles comparten esta etiqueta; acierta solo porque es el 1º " +
                  "en orden de lectura.";
        LogBus.Log(Tag, ok);
    }

    /// <summary>
    /// Clic dentro de un ÁRBOL SAP. El shell aparece como un solo elemento ("· N nodos"), pero la fila
    /// real (p.ej. «Consulta») no tiene caja porque SAP no da geometría por nodo (§1.6). Aquí se nombra
    /// la fila leyendo la selección del árbol tras el clic — que es también la identidad con la que el
    /// asistente la reproduciría (<c>doubleClickNode(key)</c>). Si la selección no se pudo leer, se dice
    /// explícitamente qué hace falta (grabar por evento Change) para no confundirlo con "no pasó nada".
    /// </summary>
    /// <param name="reason">
    /// Por qué NO se pudo leer la fila, tal cual lo reporta la superficie. Antes esta rama decía siempre
    /// "los getters de selección no respondieron", que era una conclusión y no un hecho: el mismo mensaje
    /// salía cuando el árbol ni se había resuelto. Nombrar el paso que falló es la diferencia entre
    /// diagnosticar y adivinar.
    /// </param>
    public static void LogSapTreeNode(
        SapVisualElement tree, (string Key, string Text, string Via)? node, string reason)
    {
        if (node is { } n)
            LogBus.Log(Tag,
                $"   ↳ ÁRBOL {Short(tree.Id)} · fila seleccionada {Q(n.Text)} key={Q(n.Key)} vía {n.Via} → el " +
                "asistente la ejecutaría con doubleClickNode(key), que no depende de píxeles.");
        else
            LogBus.Log(Tag,
                $"   ↳ ÁRBOL {Short(tree.Id)} · no se pudo leer la fila bajo el clic — {reason}.");
    }

    // ── UIA ──────────────────────────────────────────────────────────────────
    // En UIA no hay Id de selector; la identidad efectiva es el rectángulo. El asistente resuelve por
    // etiqueta y compara Bounds, así que la causa vuelve a ser la colisión de etiquetas.
    public static void LogUia(
        int px, int py,
        UiaReader.UiElement clicked, UiaReader.UiElement? intended,
        IReadOnlyList<UiaReader.UiElement> all, bool mismatch)
    {
        string label = clicked.Label?.Trim() ?? "";
        bool emptyLabel = label.Length == 0;
        var sharing = all
            .Where(e => string.Equals(e.Label.Trim(), label, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (mismatch && intended != null)
        {
            LogBus.Log(Tag,
                $"✗ MISMATCH @({px},{py}) — tocaste [{clicked.ControlType}] etiqueta={Q(label)} en {Box(clicked.Bounds)}; " +
                $"el asistente ejecutaría [{intended.ControlType}] en {Box(intended.Bounds)}.");
            LogBus.Log(Tag,
                emptyLabel
                    ? $"   causa=SIN-ETIQUETA · el asistente resuelve al 1º de {sharing.Count} elementos sin etiqueta."
                    : $"   causa=ETIQUETA-AMBIGUA · {Q(label)} compartida por {sharing.Count} elementos; el asistente " +
                      "toma el 1º en orden de lectura.");
            return;
        }

        string ok = $"✓ OK @({px},{py}) [{clicked.ControlType}] etiqueta={Q(label)} en {Box(clicked.Bounds)}";
        if (sharing.Count > 1)
            ok += $" · FRÁGIL: {sharing.Count} elementos comparten esta etiqueta.";
        LogBus.Log(Tag, ok);
    }

    // ── Formato ──────────────────────────────────────────────────────────────
    private static string Q(string s) => s.Length == 0 ? "∅(vacía)" : $"«{s}»";

    /// <summary>Ids de SAP son largos; deja el arranque y la cola, que es donde vive el control real.</summary>
    private static string Short(string id)
    {
        if (id.Length <= 48) return id;
        return id.Substring(0, 20) + "…" + id.Substring(id.Length - 25);
    }

    private static string Ids(IReadOnlyList<SapVisualElement> els)
    {
        var shown = els.Take(MaxIdsListed).Select(e => Short(e.Id));
        string joined = string.Join(", ", shown);
        return els.Count > MaxIdsListed ? $"{joined}, …(+{els.Count - MaxIdsListed})" : joined;
    }

    private static string Box(Rect r) =>
        $"({(int)r.X},{(int)r.Y} {(int)r.Width}×{(int)r.Height})";
}
