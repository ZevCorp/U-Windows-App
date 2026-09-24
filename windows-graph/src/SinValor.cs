namespace U.Graph;

/// <summary>
/// UN VALOR SE REGISTRA POR SU FORMA, Y EN UN SOLO SITIO. Lo que Ü escribe en SAP, lo que la persona
/// dice y lo que el piloto narra no se anota en el log: se anota cuánto mide y si coincide con lo
/// pedido. Spec 051, decisión 2.
/// </summary>
/// <remarks>
/// POR QUÉ EXISTE. El log local sale entero del equipo por el espejo (<c>EspejoDelLog</c>, desde el
/// 2026-08-16), y <c>RellenadorSap.cs</c> anotaba el valor de cada campo clínico que metía en SAP:
/// «= «38,5» (pedido «38.5»)». Eso es la historia clínica saliendo de la máquina del médico por un
/// canal de diagnóstico. La 012 lo vio el 2026-09-07 y su promesa nunca llegó al contrato.
///
/// POR QUÉ AQUÍ Y NO EN EL CLIENTE. <c>windows-graph</c> es otro ensamblado (<c>U.Graph</c>) que
/// <c>windows-client</c> referencia, y tres de los sitios que interpolan un valor viven en él
/// (<c>SapGuiSurface</c>, <c>UiaSurface</c>). Una clase del cliente no se podría llamar desde allí, y
/// dos copias serían dos criterios — el aprendizaje nº16, con cuatro casos el mismo día.
///
/// SIN HASH NI PREFIJO DEL VALOR, y no por pereza: un hash de «170» se invierte probando, y el prefijo
/// de un documento de identidad ya es medio documento. Lo que se pierde es «qué se escribió» exacto;
/// lo escrito sigue donde vive a propósito —en SAP, en la nota del portal—, y el log no tiene que ser
/// una segunda copia de la historia clínica.
///
/// Pura y estática: sin estado, sin E/S, juzgable por el contrato sin pantalla (promesa 396).
/// </remarks>
public static class SinValor
{
    /// <summary>
    /// La forma de un valor: <c>‹N car.›</c> con N su longitud, o <c>‹vacío›</c> si es nulo o vacío.
    /// </summary>
    /// <remarks>
    /// Nulo y vacío se dicen igual A PROPÓSITO: los dos significan «no había nada que escribir», y lo
    /// que el log necesita distinguir es eso de «había N caracteres». Un solo espacio no es vacío —mide
    /// 1—, porque un campo que devuelve un espacio es justo lo que hay que ver (patrón nº9).
    /// </remarks>
    public static string Forma(string? v) =>
        string.IsNullOrEmpty(v) ? "‹vacío›" : $"‹{v.Length} car.›";

    /// <summary>
    /// Lo leído contra lo pedido, sin ninguno de los dos: la forma de lo leído y si es igual, igual
    /// salvo mayúsculas o espacios, o distinto —y entonces también la forma de lo pedido—.
    /// </summary>
    /// <remarks>
    /// Es lo que la línea vieja del rellenador servía para ver —¿SAP aceptó el valor tal cual, lo
    /// recortó, lo convirtió?— sin el valor. «Distinto» lleva las dos longitudes porque «pedí 3 y quedó
    /// 2» ya dice «lo recortó», y «pedí 4 y quedó 4» dice «lo convirtió» (el «38.5» que SAP deja en
    /// «38,5»). Nulo cuenta como vacío en los dos lados: vacío no es ausente, pero aquí se leen igual.
    /// </remarks>
    public static string Contraste(string? pedido, string? leido)
    {
        string p = pedido ?? "", l = leido ?? "";
        if (string.Equals(p, l, StringComparison.Ordinal))
            return $"{Forma(l)}, igual a lo pedido";
        if (string.Equals(SinEspacios(p), SinEspacios(l), StringComparison.OrdinalIgnoreCase))
            return $"{Forma(l)}, igual salvo mayúsculas o espacios";
        return $"{Forma(l)}, distinto de lo pedido ({Forma(p)})";
    }

    /// <summary>
    /// Una excepción por lo que es y por dónde nació, sin lo que dice: los tipos de la cadena, de fuera
    /// adentro (<c>A ← B</c>), y el método del primer marco de la pila de la más honda. Ningún
    /// <c>Message</c>.
    /// </summary>
    /// <remarks>
    /// Para lo que SALE del equipo (spec 051, P1–P3). El <c>Message</c> es texto ajeno cuyo contenido no
    /// está en el código: el de <c>BackendClient</c> lleva el cuerpo entero de la respuesta, y un
    /// <c>ex.ToString()</c> lo arrastra con él —es lo que <c>App.xaml.cs</c> subía en cada «fatal»—.
    /// El mensaje sigue en el log local, en su propia línea. El marco se da por el método y no por la
    /// línea del archivo: la ruta del archivo es la de la máquina que compiló, y no dice nada que el
    /// método no diga ya.
    /// </remarks>
    public static string Excepcion(Exception? e)
    {
        if (e == null) return "‹sin excepción›";
        var tipos = new List<string>();
        Exception honda = e;
        for (var x = e; x != null; x = x.InnerException)
        {
            tipos.Add(x.GetType().Name);
            honda = x;
        }
        var metodo = new System.Diagnostics.StackTrace(honda, false).GetFrame(0)?.GetMethod();
        string donde = metodo == null ? "sin marco" : $"{metodo.DeclaringType?.FullName ?? "?"}.{metodo.Name}";
        return $"{string.Join(" ← ", tipos)} · en {donde}";
    }

    /// <summary>
    /// El texto sin ningún espacio, en cualquier sitio: SAP rellena por la derecha los campos de ancho
    /// fijo y colapsa los de en medio, y ninguna de las dos cosas cambia lo que el campo dice.
    /// </summary>
    private static string SinEspacios(string s) =>
        string.Concat(s.Where(c => !char.IsWhiteSpace(c)));
}
