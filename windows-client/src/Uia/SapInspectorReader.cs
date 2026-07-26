using System.Collections.Concurrent;
using System.Windows;
using U.Graph.Surfaces;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Uia;

/// <summary>
/// La mitad SAP del inspector visual. UIA se queda en un Pane y no ve NADA dentro de SAP GUI (ni el
/// árbol de la izquierda, ni la barra, ni los campos del dynpro); la Scripting API sí. Este lector
/// llama a <see cref="SapGuiSurface.ReadVisibleElements"/> y traduce cada elemento a un recuadro en
/// coordenadas FÍSICAS de pantalla, listo para que el overlay lo pinte junto a las cajas de UIA.
///
/// Se usa desde el mismo hilo de fondo que la lectura de UIA (nunca el de UI): el COM de SAP se
/// resuelve por la ROT en cada llamada, igual que el resto de <see cref="SapContextReader"/>. Nunca
/// lanza: si SAP no está, o el scripting está apagado por Basis, devuelve una lista vacía y el overlay
/// simplemente no dibuja cajas SAP.
/// </summary>
public sealed class SapInspectorReader
{
    private readonly SapGuiSurface _sap = new();

    /// <summary>Un recuadro SAP para el overlay. <paramref name="Bounds"/> va en píxeles físicos.</summary>
    /// <summary>
    /// <paramref name="IsMapped"/>: el shell está MAPEADO — sabemos qué hay dentro y cómo accionarlo
    /// (hoy: árboles con filas enumeradas por clave). Un shell mapeado ya no es territorio desconocido
    /// y el overlay lo pinta neutro; el ámbar queda reservado para lo que sigue opaco (grids, toolbars).
    /// </summary>
    public sealed record SapBox(Rect Bounds, string Caption, bool IsShell, bool IsMapped = false);

    /// <summary>
    /// TODOS los elementos SAP visibles (con y sin geometría: los nodos de árbol no traen rect). Es la
    /// forma cruda que usa el diagnóstico de clic; <see cref="Read"/> la envuelve para el overlay. Vacío
    /// si SAP/scripting no está. Nunca lanza.
    /// </summary>
    public IReadOnlyList<SapVisualElement> ReadElements()
    {
        try
        {
            if (!_sap.Check().Available) return Array.Empty<SapVisualElement>();
            return _sap.ReadVisibleElements();
        }
        catch (Exception ex)
        {
            LogBus.Log("sap", $"inspector SAP falló: {ex.Message}");
            return Array.Empty<SapVisualElement>();
        }
    }

    /// <summary>Firma de los shells del último refresco, para no repetir el log en cada tick.</summary>
    private string _lastShellSig = "";

    /// <summary>Cuántos sub-elementos se omitieron por caer dentro de un árbol mapeado. Solo para el registro.</summary>
    private int _suppressedInTrees;

    /// <summary>
    /// Último recuento de filas BUENO por shell (id → filas). Ver <see cref="HoldRows"/>. Concurrente
    /// porque este lector se comparte entre el hilo del refresco periódico y el del diagnóstico de clic.
    /// </summary>
    private readonly ConcurrentDictionary<string, int> _lastRows = new();

    /// <summary>
    /// Filas del shell, sosteniendo el último valor bueno si este refresco no pudo contarlas.
    ///
    /// El COM de SAP falla de vez en cuando cuando la pantalla está ocupada (un viaje al servidor, un
    /// diálogo abriéndose), y un fallo se veía como 0 filas → "sin mapear" → la caja del árbol parpadeaba
    /// entre verde y ámbar varias veces por segundo. Un shell que YA demostró tener filas no vuelve a
    /// territorio desconocido por un tropiezo puntual: mientras siga siendo el mismo shell, se conserva lo
    /// último que se leyó bien. Si de verdad se quedó sin filas, el shell cambia de pantalla y con ella de
    /// Id, así que no se arrastra un recuento viejo a otro control.
    /// </summary>
    private int HoldRows(SapVisualElement e)
    {
        if (e.ChildCount > 0)
        {
            _lastRows[e.Id] = e.ChildCount;
            return e.ChildCount;
        }
        return _lastRows.TryGetValue(e.Id, out int last) ? last : 0;
    }

    /// <summary>
    /// Narra los SHELLS de cada refresco al registro, una sola vez por cambio. El diagnóstico del
    /// inspector solo hablaba en el CLIC (<see cref="InspectorDiagnostics"/>), así que el color de una
    /// caja —que se decide en el refresco— era invisible: no había forma de saber si un árbol salía ámbar
    /// porque no se mapeó o porque otra caja se le pintó encima. Aquí se ve el reparto completo: cuántos
    /// shells hay, con qué SubType, qué recuento de filas trae cada uno y con qué rectángulo, que es lo
    /// que delata a dos shells solapados.
    /// </summary>
    private void LogShells(IReadOnlyList<SapVisualElement> elements)
    {
        var shells = elements.Where(e => e.BoundsKnown && e.SubType.Length > 0).ToList();
        // La firma NO incluye el recuento: si el recuento crudo oscila (COM de SAP tropezando), no queremos
        // una tanda de líneas por cada oscilación. Una vez por reparto de shells basta para auditar colores.
        string sig = string.Join("|", shells.Select(s => $"{s.Id}:{s.SubType}"));
        if (sig == _lastShellSig) return;
        _lastShellSig = sig;

        if (shells.Count == 0)
        {
            LogBus.Log("inspector", "shells SAP: ninguno con geometría en esta pantalla.");
            return;
        }

        LogBus.Log("inspector",
            $"shells SAP: {shells.Count} con geometría (se pintan en este orden). " +
            $"{_suppressedInTrees} sub-elementos dentro de árboles no se enmarcan (la fila ya los cubre).");
        _suppressedInTrees = 0;
        foreach (var s in shells)
            LogBus.Log("inspector",
                $"   shell subType={s.SubType} filas={s.ChildCount} → " +
                $"{(s.ChildCount > 0 ? "MAPEADO/gris" : "sin mapear/ÁMBAR")} " +
                $"caja=({s.ScreenLeft},{s.ScreenTop} {s.Width}×{s.Height}) id={s.Id}");
    }

    /// <summary>Cajas de todos los elementos SAP visibles con geometría, o vacío si SAP/scripting no está.</summary>
    public IReadOnlyList<SapBox> Read() => Read(ReadElements());

    /// <summary>
    /// Igual que <see cref="Read()"/> pero sobre elementos ya leídos. El refresco necesita los elementos
    /// para dos cosas (las cajas y las filas de árbol); leerlos una vez y pasarlos evita pagar dos veces el
    /// recorrido COM de la pantalla entera.
    /// </summary>
    public IReadOnlyList<SapBox> Read(IReadOnlyList<SapVisualElement> elements)
    {
        LogShells(elements);

        // Cajas de los ÁRBOLES mapeados: lo que caiga dentro de una de ellas no se vuelve a enmarcar como
        // campo. Dentro de un árbol la unidad accionable es la FILA (por clave), y sus filas ya se dibujan
        // aparte; los sub-elementos que el recorrido encuentra ahí —el texto de la hoja, su icono— no se
        // accionan por separado, así que su recuadro solo duplica el de la fila. El efecto visible era una
        // caja estrecha pegada al texto ENCIMA de la caja de la fila, en cada hoja del árbol.
        var treeBoxes = elements
            .Where(e => e.BoundsKnown && e.ChildCount > 0 &&
                        e.SubType.IndexOf("Tree", StringComparison.OrdinalIgnoreCase) >= 0)
            .Select(e => new Rect(e.ScreenLeft, e.ScreenTop, e.Width, e.Height))
            .ToList();
        var boxes = new List<SapBox>(elements.Count);
        foreach (var e in elements)
        {
            if (!e.BoundsKnown) continue; // los nodos de árbol no traen rect: se detectan, no se enmarcan
            bool shell = e.SubType.Length > 0;

            // Sub-elemento dentro de un árbol mapeado: no se enmarca (ver treeBoxes). El propio árbol sí,
            // porque es un shell y no está "dentro de sí mismo".
            if (!shell && treeBoxes.Any(t => t.Contains(new Rect(e.ScreenLeft, e.ScreenTop, e.Width, e.Height))))
            {
                _suppressedInTrees++;
                continue;
            }

            int rows = shell ? HoldRows(e) : 0;
            // Un shell con filas enumeradas está MAPEADO: cada fila tiene clave y ruta, se puede enseñar
            // y reproducir. Ya no se marca en ámbar de "no mapeado".
            bool mapped = shell && rows > 0;
            // Solo los shells (árbol, grid…) llevan rótulo: son pocos y es donde el rótulo ayuda
            // ("Favoritos · 20 nodos"). Enmarcar cada campo con texto saturaría la pantalla.
            string caption = shell
                ? (mapped ? $"{e.Label} · {rows} nodos · mapeado" : e.Label)
                : "";
            boxes.Add(new SapBox(new Rect(e.ScreenLeft, e.ScreenTop, e.Width, e.Height), caption, shell, mapped));
        }
        return boxes;
    }

    // ── Filas del árbol, enmarcadas una a una ─────────────────────────────────

    /// <summary>
    /// Cada cuánto se vuelve a preguntar por las filas. El overlay refresca cada 200 ms, pero resolver las
    /// filas cuesta dos llamadas COM por CLAVE del árbol (525 en la pantalla real), así que a 200 ms se
    /// volvería a martillar el COM de SAP — el problema que ya hizo parpadear el inspector una vez. Las filas
    /// solo cambian con el scroll o al desplegar, no cada cuadro; entre medias se reutiliza lo leído. El coste
    /// asumido es que un scroll rápido va un poco por detrás.
    /// </summary>
    private static readonly TimeSpan RowsMaxAge = TimeSpan.FromMilliseconds(900);

    /// <summary>
    /// Cuánto sube el recuadro respecto a la banda que SAP declara para la fila, como fracción del alto.
    ///
    /// La banda de SAP CONTIENE la fila —está verificado con clics reales— pero el texto no se dibuja
    /// centrado en ella: se dibuja en su mitad superior, así que un recuadro que ocupa la banda entera se ve
    /// bajo respecto al texto. Medido sobre los clics del operador: cuatro clics sobre texto cayeron a los
    /// offsets 1, 7, 8 y 15 de bandas de 30 px, mediana ~7,5 frente al centro 15.
    ///
    /// Va como FRACCIÓN y no como constante de píxeles para que escale con el DPI, el zoom de SAP y el tema
    /// de fuente: si la fila mide 40 px en otra máquina, la corrección crece con ella.
    ///
    /// Hubo una corrección así, calibrada con clics, y la quité con un argumento equivocado: que el contraste
    /// «CUADRA» probaba que la banda cruda ya era correcta. CUADRA solo comprueba CONTENCIÓN —que el clic caiga
    /// dentro— y una banda desplazada la sigue pasando. Contención no es alineación.
    /// </summary>
    private const double RowShiftFraction = 0.23;

    /// <summary>Offsets de clic dentro de la banda, para afinar la corrección con datos del sitio.</summary>
    private readonly List<double> _clickOffsets = new();
    private readonly object _shiftLock = new();
    private double _measuredShift = double.NaN;

    /// <summary>
    /// Anota dónde cayó un clic dentro de la banda de su fila. Como el operador clica el TEXTO, la mediana de
    /// esos offsets dice dónde está el texto dentro de la banda; centrar el recuadro ahí lo alinea con lo que
    /// se ve. Mediana y no media: un clic al borde de la fila sesga, y con pocas muestras la media lo arrastra.
    /// </summary>
    public void ObserveRowClick(int clickY, double bandTopScreen, int height)
    {
        if (height <= 0) return;
        double offset = clickY - bandTopScreen;
        if (offset < 0 || offset > height) return; // fuera de su propia banda: muestra inservible

        lock (_shiftLock)
        {
            _clickOffsets.Add(offset);
            if (_clickOffsets.Count > 40) _clickOffsets.RemoveAt(0);
            if (_clickOffsets.Count < 3) return; // con una o dos muestras la mediana no dice nada

            var sorted = _clickOffsets.OrderBy(v => v).ToList();
            _measuredShift = sorted[sorted.Count / 2] - height / 2.0;
        }
    }

    /// <summary>Corrección vigente: la medida con clics si hay suficientes, si no la fracción por defecto.</summary>
    private double RowShift(int height)
    {
        lock (_shiftLock)
            return double.IsNaN(_measuredShift) ? -RowShiftFraction * height : _measuredShift;
    }

    private readonly ConcurrentDictionary<string, List<SapGuiSurface.TreeRow>> _rowCache = new();
    private readonly ConcurrentDictionary<string, int> _rowCacheStamp = new();
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    /// <summary>Filas visibles cacheadas de un árbol (vacío si aún no se leyeron).</summary>
    public IReadOnlyList<SapGuiSurface.TreeRow> CachedRows(string treeId) =>
        _rowCache.TryGetValue(treeId, out var v) ? v : Array.Empty<SapGuiSurface.TreeRow>();

    /// <summary>
    /// Las cajas de las filas visibles de cada árbol, en píxeles físicos. La caja sale DIRECTA de lo que SAP
    /// dice: borde del árbol + top de la fila, con su alto. No hay orden que acertar, ni scroll que localizar,
    /// ni calibración — por eso tampoco hay nada que pueda quedar desalineado.
    /// </summary>
    public IReadOnlyList<(Rect Box, string Text, bool IsFolder)> ReadTreeRowBoxes(
        IReadOnlyList<SapVisualElement> elements)
    {
        var result = new List<(Rect, string, bool)>();

        foreach (var e in elements)
        {
            if (!e.BoundsKnown) continue;
            if (e.SubType.IndexOf("Tree", StringComparison.OrdinalIgnoreCase) < 0) continue;

            var box = new Rect(e.ScreenLeft, e.ScreenTop, e.Width, e.Height);
            int now = (int)_clock.ElapsedMilliseconds;
            bool stale = !_rowCacheStamp.TryGetValue(e.Id, out int at) || now - at > RowsMaxAge.TotalMilliseconds;

            if (stale)
            {
                _rowCacheStamp[e.Id] = now;
                try
                {
                    var rows = _sap.VisibleTreeRows(e.Id, e.Height, out string why).ToList();
                    _rowCache[e.Id] = rows;
                    LogRows(e.Id, box, rows, why);
                }
                catch (Exception ex) { LogBus.Log("inspector", $"filas del árbol fallaron: {ex.Message}"); }
            }

            if (!_rowCache.TryGetValue(e.Id, out var cached)) continue;
            foreach (var row in cached)
            {
                double top = box.Y + row.Top + RowShift(row.Height);
                // RECORTE contra el borde inferior del árbol. La última fila suele estar medio scrolleada:
                // SAP da su top dentro del árbol pero su alto completo se sale, y sin recortar la caja
                // invadía lo que hay debajo (en la pantalla real, el rótulo del segundo árbol). Recortar
                // —en vez de descartar la fila— es lo que el operador ve: media fila asomando.
                double bottom = Math.Min(top + row.Height, box.Y + box.Height);
                double height = bottom - top;
                if (height < 3) continue; // una astilla de 1-2 px es ruido, no una fila

                result.Add((new Rect(box.X + 1, top, Math.Max(0, box.Width - 2), height),
                            row.Text, row.IsFolder));
            }
        }

        return result;
    }

    /// <summary>
    /// Deja en el registro qué filas se leyeron y dónde, una vez por cambio. Es lo que permite auditar las
    /// cajas sin mirar un pantallazo: un pantallazo no distingue "espaciado bueno con desplazamiento
    /// constante" de "filas equivocadas", y confundir esos dos casos ya costó varias vueltas.
    /// </summary>
    private void LogRows(string treeId, Rect box, List<SapGuiSurface.TreeRow> rows, string why)
    {
        string sig = $"{treeId}:{rows.Count}:{(rows.Count > 0 ? rows[0].Key : "")}";
        if (sig == _lastRowSig) return;
        _lastRowSig = sig;

        if (rows.Count == 0) { LogBus.Log("inspector", $"filas del árbol: {why}"); return; }

        var head = rows.Take(3).Select(r => $"top={r.Top} alto={r.Height} «{Trim(r.Text)}»");
        LogBus.Log("inspector",
            $"filas del árbol · caja=({box.X},{box.Y} {box.Width}×{box.Height}) · {why} · " +
            string.Join(" · ", head));
    }

    private string _lastRowSig = "";

    private static string Trim(string s) => s.Length <= 28 ? s : s.Substring(0, 27) + "…";

    /// <summary>Geometría que SAP atribuye a una fila concreta. false si no la da. Para contrastar con el clic.</summary>
    public bool ItemGeometry(string treeId, string nodeKey, out int itemTop, out int itemHeight)
    {
        try { return _sap.TreeItemGeometry(treeId, nodeKey, out itemTop, out itemHeight); }
        catch { itemTop = 0; itemHeight = 0; return false; }
    }

    /// <summary>Qué componente SAP hay bajo un punto de pantalla (hit-test nativo), o null.</summary>
    public string? HitTest(int screenX, int screenY) => _sap.HitTest(screenX, screenY);

    /// <summary>
    /// El nodo seleccionado de un árbol (por Id del shell), o null. Coordinate-free: como los nodos no
    /// traen geometría, es la única forma de saber qué FILA tocó el usuario dentro de un árbol SAP.
    /// </summary>
    public (string Key, string Text, string Via)? SelectedTreeNode(string treeId, out string reason)
    {
        try { return _sap.SelectedTreeNode(treeId, out reason); }
        catch (Exception ex) { reason = $"excepción leyendo la selección: {ex.Message}"; return null; }
    }

}
