namespace U.Graph.Surfaces;

/// <summary>
/// Un elemento VISIBLE de una pantalla SAP GUI, leído por la Scripting API para el inspector visual
/// (el overlay que enmarca lo que la app detecta). Es un primo de <see cref="DetectedField"/>, pero con
/// dos diferencias que importan al inspector:
///
///  1. Trae GEOMETRÍA DE PANTALLA. Todo <c>GuiVComponent</c> expone <c>ScreenLeft/ScreenTop/Width/
///     Height</c> en píxeles físicos de pantalla (no relativos al contenedor) — justo lo que el overlay
///     necesita para dibujar un recuadro alineado sobre el control real, igual que hace con los rects
///     de UIA. Ver <see cref="SapGuiSurface.ReadVisibleElements"/>.
///
///  2. Incluye lo que NO es un campo de formulario: barra de herramientas, código OK, títulos, y sobre
///     todo los SHELLS (árbol de SAP Easy Access, grids…). UIA se queda en un Pane y no ve nada de esto;
///     el scripting sí. Por eso el inspector lee AMBAS superficies a la vez y las pinta juntas.
///
/// LÍMITE DE LA API DE SAP, no nuestro: los NODOS de un árbol (<c>GuiTree</c>) no exponen rectángulo
/// propio — la Scripting API da sus claves y textos (<c>GetNodesCol</c>/<c>GetNodeTextByKey</c>) pero
/// ninguna coordenada por nodo. Comprobado por sonda contra el SAP real: <c>GetItemLeft/Top/Width/
/// Height</c> y <c>GetNodeHeight</c> devuelven 0 para cualquier clave. Así que un nodo se emite como
/// elemento LÓGICO (<see cref="IsNode"/> = true, <see cref="BoundsKnown"/> = false): el cerebro lo "ve"
/// y lo acciona POR CLAVE, pero el overlay solo puede enmarcar el árbol entero, no fila a fila.
///
/// No hace falta rectángulo para clicar: <c>selectNode</c>/<c>doubleClickNode</c> hacen el clic real
/// con su viaje al servidor, sin depender de píxeles ni de dónde esté el scroll.
/// </summary>
public sealed record SapVisualElement(
    string Id,
    string Type,
    string SubType,
    string Label,
    string? Value,
    int ScreenLeft,
    int ScreenTop,
    int Width,
    int Height,
    bool BoundsKnown,
    string ActionType,
    string ControlType,
    bool IsNode,
    string? ParentId,
    string? NodeKey = null,
    int ChildCount = 0,

    /// <summary>
    /// Ruta jerárquica del nodo (<c>GetNodePathByKey</c>, p.ej. <c>1\2</c>). Es lo que DESAMBIGUA una
    /// fila: en un árbol clínico real "Órdenes Clínicas" aparece 17 veces, una por servicio, así que el
    /// texto nunca identifica la fila y la clave puede cambiar entre sesiones. La ruta describe la
    /// POSICIÓN en el árbol y sobrevive a ambas cosas.
    /// </summary>
    string? NodePath = null,

    /// <summary>Si el nodo es carpeta (<c>IsFolder</c>): decide entre expandir y accionar.</summary>
    bool IsFolder = false,

    /// <summary>Si, siendo carpeta, ya está desplegada (<c>IsFolderExpanded</c>).</summary>
    bool IsExpanded = false);
