using System.Text.RegularExpressions;

namespace U.Graph.Surfaces;

/// <summary>
/// El selector de SAP GUI. Envuelve el <c>id</c> de la Scripting API, que es una ruta tipo URL desde
/// la raíz del modelo de objetos:
///
///   <c>/app/con[0]/ses[0]/wnd[0]/usr/txtRSYST-BNAME</c>
///
/// Es un selector excelente — mucho más estable que un CSS, porque lo deriva SAP del nombre del campo
/// del Dynpro (<c>RSYST-BNAME</c>) y no de la maquetación.
///
/// PERO se guarda NORMALIZADO, sin el prefijo <c>/app/con[N]/ses[M]</c>:
///
///   <c>sap:wnd[0]/usr/txtRSYST-BNAME</c>
///
/// El motivo es que esos índices identifican la conexión y la sesión concretas del momento en que se
/// grabó. Un operador con dos sistemas SAP abiertos, o que cierre y reabra, tendrá otros índices y el
/// id absoluto dejaría de resolver. La parte a partir de <c>wnd[0]</c> sí describe la pantalla, y
/// <c>GuiSession.FindById</c> acepta rutas relativas a la sesión — que es justo como lo resolvemos.
/// </summary>
public static class SapSelector
{
    public const string Prefix = "sap:";

    /// <summary>
    /// Separador del FRAGMENTO de nodo. Una fila de un GuiTree no tiene id propio — su id ES el del
    /// árbol — así que el selector del árbol solo no basta para identificarla:
    ///
    ///   <c>sap:wnd[0]/shellcont/shellcont/shell/shellcont[0]/shell#node=vw00073</c>
    ///
    /// El fragmento va DETRÁS para que <see cref="IdOf"/> siga devolviendo un id que FindById acepta.
    /// Se guarda en el selector (y no solo en PlanStep.NodeKey) para que un step grabado sea
    /// autocontenido: quien lo lea sabe a qué fila apunta sin mirar otros campos.
    /// </summary>
    public const string NodeMark = "#node=";

    /// <summary>
    /// Separador del BOTÓN DE TOOLBAR de un shell. Los botones de la barra de un ALV/GridView no son
    /// <c>GuiComponent</c>: no aparecen como hijos, no tienen Id propio y un recorrido del árbol de
    /// componentes no los ve. Son items del control, con su propia clave:
    ///
    ///   <c>sap:wnd[0]/usr/ssubVIEW_SCREEN:SAPLN1LSTAMB:0007/cntlISH_VIEW_007/shellcont/shell#tbbtn=NV44</c>
    ///
    /// Comprobado contra el SAP real (2026-07-26): «Crear Triage Administrativo» es <c>NV44</c> en el
    /// grid de NWP1, y se acciona con <c>PressToolbarButton(id)</c> — sin coordenadas, igual que las
    /// filas del árbol. Es el paso que faltaba para que el workflow llegue a NV2000 por sí solo.
    /// </summary>
    public const string ToolbarMark = "#tbbtn=";

    /// <summary>
    /// Separador de una FILA DE ALV (GridView). Como la fila de árbol, no es un
    /// <c>GuiComponent</c>: el id resuelve al grid y la fila viaja en el fragmento.
    ///
    ///   <c>sap:wnd[0]/usr/…/cntlISH_VIEW_007/shellcont/shell#row=FALNR=2394336|GEBNAME=GIRALDO</c>
    ///
    /// La clave son PARES columna=valor, no un índice. «La fila 0» describe una posición y hoy hay
    /// un paciente en la lista; el día que haya tres, el índice abre la historia clínica de otra
    /// persona sin avisar. Los pares describen a QUIÉN se señala, y si esa fila ya no está, el paso
    /// falla en vez de acertar por casualidad.
    ///
    /// POR QUÉ HACE FALTA (verificado contra el SAP real, 2026-07-28): el botón de la barra del ALV
    /// actúa sobre la fila seleccionada. Con la lista cargada y sin selección,
    /// <c>PressToolbarButton("ZMEDTRIAGE")</c> se acepta sin excepción y NO PASA NADA; tras
    /// <c>SelectedRows="0"</c> + <c>SetCurrentCell(0,…)</c> el mismo botón abre el triage al
    /// instante. Por eso el operador acertaba con el ratón y el workflow no: al hacer clic, SAP le
    /// pone la selección; la API no.
    /// </summary>
    public const string RowMark = "#row=";

    /// <summary>Quita el prefijo de conexión/sesión: /app/con[0]/ses[0]/wnd[0]/... → wnd[0]/...</summary>
    private static readonly Regex AbsolutePrefix = new(
        @"^/?app/con\[\d+\]/ses\[\d+\]/", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Envuelve un id de SAP como selector, normalizándolo a relativo a la sesión.</summary>
    public static string ById(string id) => Prefix + Normalize(id);

    /// <summary>Selector de una FILA de árbol: el id del árbol más la clave del nodo.</summary>
    public static string ByNode(string treeId, string nodeKey) =>
        ById(treeId) + NodeMark + (nodeKey ?? "").Trim();

    /// <summary>Selector de un BOTÓN DE TOOLBAR de un shell: el id del shell más la clave del botón.</summary>
    public static string ByToolbarButton(string shellId, string buttonId) =>
        ById(shellId) + ToolbarMark + (buttonId ?? "").Trim();

    /// <summary>La clave del botón de toolbar que lleva el selector, o null si no apunta a uno.</summary>
    public static string? ToolbarButtonOf(string selector)
    {
        if (!Owns(selector)) return null;
        int cut = selector.IndexOf(ToolbarMark, StringComparison.Ordinal);
        if (cut < 0) return null;
        string b = selector.Substring(cut + ToolbarMark.Length).Trim();
        return b.Length > 0 ? b : null;
    }

    public static bool Owns(string selector) =>
        !string.IsNullOrWhiteSpace(selector) && selector.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// El id que se le pasa a GuiSession.FindById, SIN el fragmento de nodo (FindById no lo entendería).
    /// Vacío si el selector no es de esta superficie.
    /// </summary>
    public static string IdOf(string selector)
    {
        if (!Owns(selector)) return "";
        string v = selector.Substring(Prefix.Length);
        // Cualquier fragmento, no solo el de nodo: si se olvida uno, FindById recibe el id CON el
        // fragmento pegado, devuelve null, y el paso falla con «no se encontró el campo» — un mensaje
        // que apunta al sitio equivocado.
        int cut = v.IndexOf('#');
        return cut >= 0 ? v.Substring(0, cut) : v;
    }

    /// <summary>Selector de una FILA de ALV: el id del grid más los pares columna=valor.</summary>
    public static string ByRow(string gridId, string rowKey) =>
        ById(gridId) + RowMark + (rowKey ?? "").Trim();

    /// <summary>Los pares columna=valor de la fila de ALV, o null si el selector no apunta a una.</summary>
    public static string? RowKeyOf(string selector)
    {
        if (!Owns(selector)) return null;
        int cut = selector.IndexOf(RowMark, StringComparison.Ordinal);
        if (cut < 0) return null;
        string key = selector.Substring(cut + RowMark.Length).Trim();
        return key.Length > 0 ? key : null;
    }

    /// <summary>La clave del nodo que lleva el selector, o null si apunta a un control normal.</summary>
    public static string? NodeKeyOf(string selector)
    {
        if (!Owns(selector)) return null;
        int cut = selector.IndexOf(NodeMark, StringComparison.Ordinal);
        if (cut < 0) return null;
        string key = selector.Substring(cut + NodeMark.Length).Trim();
        return key.Length > 0 ? key : null;
    }

    /// <summary>Deja el id relativo a la sesión y sin barra inicial.</summary>
    public static string Normalize(string id)
    {
        string v = (id ?? "").Trim();
        if (v.Length == 0) return "";

        v = AbsolutePrefix.Replace(v, "");
        return v.TrimStart('/');
    }
}
