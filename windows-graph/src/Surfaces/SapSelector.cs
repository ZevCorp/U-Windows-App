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

    /// <summary>Quita el prefijo de conexión/sesión: /app/con[0]/ses[0]/wnd[0]/... → wnd[0]/...</summary>
    private static readonly Regex AbsolutePrefix = new(
        @"^/?app/con\[\d+\]/ses\[\d+\]/", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Envuelve un id de SAP como selector, normalizándolo a relativo a la sesión.</summary>
    public static string ById(string id) => Prefix + Normalize(id);

    /// <summary>Selector de una FILA de árbol: el id del árbol más la clave del nodo.</summary>
    public static string ByNode(string treeId, string nodeKey) =>
        ById(treeId) + NodeMark + (nodeKey ?? "").Trim();

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
        int cut = v.IndexOf(NodeMark, StringComparison.Ordinal);
        return cut >= 0 ? v.Substring(0, cut) : v;
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
