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
    public sealed record SapBox(Rect Bounds, string Caption, bool IsShell);

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

    /// <summary>Cajas de todos los elementos SAP visibles con geometría, o vacío si SAP/scripting no está.</summary>
    public IReadOnlyList<SapBox> Read()
    {
        var elements = ReadElements();
        var boxes = new List<SapBox>(elements.Count);
        foreach (var e in elements)
        {
            if (!e.BoundsKnown) continue; // los nodos de árbol no traen rect: se detectan, no se enmarcan
            bool shell = e.SubType.Length > 0;
            // Solo los shells (árbol, grid…) llevan rótulo: son pocos y es donde el rótulo ayuda
            // ("Favoritos · 20 nodos"). Enmarcar cada campo con texto saturaría la pantalla.
            string caption = shell ? e.Label : "";
            boxes.Add(new SapBox(new Rect(e.ScreenLeft, e.ScreenTop, e.Width, e.Height), caption, shell));
        }
        return boxes;
    }

    /// <summary>Qué componente SAP hay bajo un punto de pantalla (hit-test nativo), o null.</summary>
    public string? HitTest(int screenX, int screenY) => _sap.HitTest(screenX, screenY);

    /// <summary>
    /// El nodo seleccionado de un árbol (por Id del shell), o null. Coordinate-free: como los nodos no
    /// traen geometría, es la única forma de saber qué FILA tocó el usuario dentro de un árbol SAP.
    /// </summary>
    public (string Key, string Text)? SelectedTreeNode(string treeId)
    {
        try { return _sap.SelectedTreeNode(treeId); }
        catch { return null; }
    }
}
