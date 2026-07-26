namespace U.Graph.Surfaces;

/// <summary>
/// Resultado del hit-test nativo de SAP (<c>FindByPosition</c>) con TODO lo que la API devolvió.
///
/// Existe porque el repo tenía dos versiones contradictorias del contrato: el código histórico de
/// <see cref="SapGuiSurface.HitTest"/> asumía un GuiComponent con <c>.Id</c>, mientras la spec citada
/// en INVESTIGACION-SAPGUI-UIA.md documenta una GuiCollection de 2 strings — [0] el Id del componente
/// y [1] la descripción del "inner object": la parte INTERNA del control bajo el punto (la fila de un
/// árbol, el botón de una toolbar, la celda de un grid). <see cref="ComShape"/> registra cuál de las
/// formas llegó de verdad en ESTE SAP, porque de ese dato depende el diseño del mapeo de nodos del
/// scrolleable (ver SONDA-MAPEO-ARBOL.md): si el inner object identifica la fila, tenemos mapeo
/// determinista sin OCR.
/// </summary>
/// <param name="Id">Id de scripting del componente bajo el punto (crudo, sin normalizar).</param>
/// <param name="InnerObject">Descripción del objeto interno bajo el punto, si la API la dio.</param>
/// <param name="ComShape">Forma COM observada: "component", "collection[N]" o "collection-item[N]".</param>
public sealed record SapHit(
    string Id,
    string? InnerObject,
    string ComShape);
