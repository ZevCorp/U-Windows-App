using U.Graph.Surfaces;

namespace U.Graph;

/// <summary>
/// EL comparador de lugares: la única vara con la que el sistema decide si dos URLs de superficie
/// son "el mismo sitio".
///
/// Antes había TRES varas distintas y cada una daba su propio veredicto sobre la misma pantalla:
/// el player normalizaba el pathname (quitaba contadores vivos de los títulos), el motor de carga
/// comparaba el string crudo (así que "Inbox (1,956)" nunca casaba con "Inbox (1,988)" y la fase
/// de ubicación agotaba su techo entero en pantallas correctas), y el pre-check ignoraba el
/// pathname por completo. Dos verdades contradictorias = pasos ejecutados sobre la pantalla
/// equivocada. Una sola implementación, un solo veredicto.
///
/// Reglas:
///   · El ORIGIN (sistema SAP, proceso, host) debe coincidir exacto — es la app.
///   · El PATHNAME se compara según su naturaleza:
///       - estructural (sapgui://, web://): identificador real; se conservan dígitos y jerarquía.
///         /NV2000 y /NV3000 son pantallas DISTINTAS (borrar dígitos los fundía en "nv").
///       - título vivo (uia://): trae estado dentro ("Inbox (1,956)"); se comparan solo las letras.
///   · Tolerancia por PREFIJO jerárquico solo en rutas estructurales: una grabación menos
///     específica (/NV2000) cubre una lectura más específica (/NV2000/SAPMNPA10/0100), nunca al
///     revés, y siempre exigiendo el separador (/NV2000 no cubre /NV20001).
/// </summary>
public static class SurfacePlace
{
    /// <summary>¿Misma app/sistema? <c>uia://chrome.exe/x</c> vs <c>uia://chrome.exe/y</c> → sí.</summary>
    public static bool SameOrigin(string urlA, string urlB)
    {
        string a = OriginOf(urlA), b = OriginOf(urlB);
        return a.Length > 0 && string.Equals(a.TrimEnd('/'), b.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ¿Mismo LUGAR (origin + pathname comparado según su naturaleza)? Falso si falta alguno de los
    /// dos datos: sin nodo grabado no hay nada que afirmar.
    /// </summary>
    public static bool Same(string nowUrl, string recordedUrl)
    {
        if (string.IsNullOrWhiteSpace(nowUrl) || string.IsNullOrWhiteSpace(recordedUrl)) return false;
        if (!SameOrigin(nowUrl, recordedUrl)) return false;

        bool structural = HasStructuralPath(OriginOf(nowUrl));
        string a = Normalize(PathnameOf(nowUrl), structural);
        string b = Normalize(PathnameOf(recordedUrl), structural);
        if (a.Length == 0 || b.Length == 0) return false;
        if (a == b) return true;

        // Grabación MENOS específica que la lectura actual: la identidad SAP pasó de /nv2000 a
        // /nv2000/sapmnpa10/0100, y sin esto ninguna grabación anterior al cambio volvería a casar.
        return structural && a.StartsWith(b + "/", StringComparison.Ordinal);
    }

    /// <summary>Overload para el player, que ya tiene la identidad viva desmontada.</summary>
    public static bool Same(SurfaceIdentity now, string recordedUrl) => Same(now.Url, recordedUrl);

    /// <summary>
    /// ¿La ruta GRABADA cubre la actual? Igual, o la grabada es un prefijo jerárquico de la actual.
    /// (La versión "cruda" de <see cref="Same"/>, para el pre-check de workflow que compara
    /// pathnames ya separados y estables — web/sapgui.)
    /// </summary>
    public static bool Covers(string recordedPathname, string nowPathname)
    {
        string a = (recordedPathname ?? "").TrimEnd('/');
        string b = (nowPathname ?? "").TrimEnd('/');
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        return b.StartsWith(a + "/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>El origin de una URL de superficie: <c>uia://chrome.exe/x</c> → <c>uia://chrome.exe</c>.</summary>
    public static string OriginOf(string url)
    {
        string s = (url ?? "").Trim();
        int scheme = s.IndexOf("://", StringComparison.Ordinal);
        if (scheme < 0) return s;
        int slash = s.IndexOf('/', scheme + 3);
        return slash < 0 ? s : s[..slash];
    }

    /// <summary>El pathname de una URL de superficie: <c>uia://chrome.exe/x</c> → <c>/x</c> ("" si no hay).</summary>
    public static string PathnameOf(string url)
    {
        string s = (url ?? "").Trim();
        int scheme = s.IndexOf("://", StringComparison.Ordinal);
        if (scheme < 0) return "";
        int slash = s.IndexOf('/', scheme + 3);
        return slash < 0 ? "" : s[slash..];
    }

    /// <summary>
    /// ¿El pathname de esta superficie es un IDENTIFICADOR estructural o un título vivo? En
    /// <c>sapgui://</c> es transacción/programa/dynpro y en <c>web://</c> es la ruta de la página:
    /// los dígitos SON identidad. En <c>uia://</c> es el título de la ventana: estado vivo.
    /// </summary>
    public static bool HasStructuralPath(string origin) =>
        (origin ?? "").StartsWith("sapgui://", StringComparison.OrdinalIgnoreCase) ||
        (origin ?? "").StartsWith("web://", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// ¿Esta superficie es SAP vista por el Scripting? Se pregunta por el ESQUEMA, que es lo único
    /// que lo sabe: `sapgui://` lo produce <c>SapGuiSurface.Identity()</c>, y solo cuando el
    /// scripting responde. Así que esto no es «hay SAP en la pantalla» sino algo más útil —
    /// «podemos preguntarle a SAP» — que es justo la condición para leer sus campos.
    ///
    /// Vive aquí, con las demás preguntas sobre lugares, porque quien decide qué cuenta como el
    /// mismo sitio tiene que decidir también qué cuenta como SAP, o acabarían siendo dos criterios
    /// que se separan en silencio.
    ///
    /// NO SE PREGUNTA POR EL NOMBRE DEL PROCESO, y esa confusión dejó al cerebro sin los campos de
    /// SAP desde que existe la rama que los lee (medido el 2026-08-12):
    /// `AppAligner.ProcessFromOrigin` quita el esquema, así que `sapgui://QAS` da `"QAS"` —que no
    /// empieza por «sap»— y el respaldo `uia://saplogon.exe` da `"saplogon"`, que sí. Exactamente al
    /// revés de lo que hace falta.
    ///
    /// Acepta la URL entera o solo el origen: las dos formas circulan por el cliente y exigir una
    /// sería otra identidad de dos formas (aprendizaje nº16).
    /// </summary>
    public static bool EsSap(string urlUOrigen) =>
        (urlUOrigen ?? "").TrimStart().StartsWith("sapgui://", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Normaliza un pathname para comparar LUGARES. Estructural: se conserva lo significativo
    /// (alfanumérico + jerarquía). Título vivo: solo letras, porque el resto es estado del momento.
    /// </summary>
    public static string Normalize(string pathname, bool structural)
    {
        string p = (pathname ?? "").ToLowerInvariant();
        var sb = new System.Text.StringBuilder(p.Length);
        foreach (char c in p)
        {
            if (structural) { if (char.IsLetterOrDigit(c) || c == '/') sb.Append(c); }
            else if (char.IsLetter(c)) sb.Append(c);
        }
        return sb.ToString().TrimEnd('/');
    }
}
