namespace U.WindowsClient.Navigation;

/// <summary>
/// QUÉ HACE UN APRENDIZAJE, DICHO EN CASTELLANO. Promesa 199 (spec 016). Puro.
/// </summary>
/// <remarks>
/// EL MÉDICO NO LEE SELECTORES, y esta es la regla que decide si el panel de aprendizajes se
/// entiende o es una lista de nombres. Un paso es, por dentro, una puerta y un valor:
/// «sap:wnd[0]/usr/txtY0000000-ZTXTPESO» con «52». Enseñar eso a alguien que va a firmar una
/// historia clínica no explica nada; y enseñar el «52» sería peor, porque es el peso del paciente
/// de prueba y no el que se va a escribir (promesa 123).
///
/// LAS CUATRO FORMAS DE UN PASO, y cada una se dice distinta:
///   · UNA TECLA (<c>key:enter</c>) — se dice por su tecla.
///   · UN HUECO — se dice por el NOMBRE DEL DATO, nunca por el valor de la demo.
///   · UN TECLEO FIJO — el código de transacción y los filtros son parte de la tarea (promesa 196),
///     y ahí el valor SÍ se dice: «abre la transacción nwp1» es justo lo que pasa.
///   · UN TOQUE — se dice por el nombre de la puerta, que es el mismo texto que la persona lee en
///     la pantalla. Si el paso solo trae un selector crudo, se resume sin enseñarlo.
///
/// ABRE o PULSA según el nombre lleve carpeta: «Urgencias Adultos/Triage» se abre, «Triage» se
/// pulsa. Es una heurística de redacción, no una decisión de comportamiento: si se equivoca, la
/// frase queda algo torpe y nada más.
/// </remarks>
public static class LoQueHaceLaSkill
{
    public static IReadOnlyList<string> EnCastellano(SkillEnsenada skill)
    {
        var salida = new List<string>();
        if (skill == null) return salida;
        var porCampo = new Dictionary<string, Hueco>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in skill.Huecos ?? Array.Empty<Hueco>())
            if (h.Campo.Length > 0) porCampo[InstanciarSkill.Identidad(h.Campo)] = h;

        foreach (var p in skill.Pasos ?? Array.Empty<PasoEnsenado>())
            salida.Add(UnPaso(p, porCampo));
        return salida;
    }

    /// <summary>La frase de un paso. Separada para que el panel pueda pedir solo una.</summary>
    public static string UnPaso(PasoEnsenado paso, IReadOnlyDictionary<string, Hueco> porCampo)
    {
        string exit = (paso?.Exit ?? "").Trim();
        string texto = (paso?.Texto ?? "").Trim();

        string tecla = Tecla(exit);
        if (tecla.Length > 0) return $"Pulsa {tecla}";

        if (texto.Length > 0)
        {
            if (porCampo != null && porCampo.TryGetValue(InstanciarSkill.Identidad(exit), out var hueco)
                && hueco.Significado.Trim().Length > 0)
                return $"Escribe «{hueco.Significado.Trim()}»";
            // El campo de comandos es la puerta de entrada de SAP: ahí el valor ES la tarea.
            if (Piloto.SkillDeLoVerificado.EsElCampoDeComandos(exit))
                return $"Abre la transacción «{texto}»";
            return EsSelector(exit) ? $"Escribe «{texto}»" : $"Escribe «{texto}» en {exit}";
        }

        if (exit.Length == 0) return "Espera a que la pantalla responda";
        if (EsSelector(exit)) return "Toca un elemento de la pantalla";
        return exit.Contains('/') ? $"Abre {exit}" : $"Pulsa «{exit}»";
    }

    /// <summary>
    /// Un selector es el idioma de la máquina, y no sale nunca a la pantalla del médico.
    /// </summary>
    /// <remarks>
    /// El grabador emite <c>sap:</c> y <c>uia:</c>; las llegadas, <c>sapgui://</c> y <c>uia://</c>.
    /// Los cuatro se reconocen por prefijo, que es como los reconoce el resto del sistema.
    /// </remarks>
    public static bool EsSelector(string s)
    {
        string x = (s ?? "").Trim();
        return x.StartsWith("sap:", StringComparison.OrdinalIgnoreCase)
            || x.StartsWith("uia:", StringComparison.OrdinalIgnoreCase)
            || x.StartsWith("sapgui://", StringComparison.OrdinalIgnoreCase)
            || x.Contains("wnd[", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>El nombre legible de una tecla, o vacío si el paso no es una tecla.</summary>
    private static string Tecla(string exit)
    {
        string x = (exit ?? "").Trim();
        if (!x.StartsWith("key:", StringComparison.OrdinalIgnoreCase)) return "";
        string t = x[4..].Trim();
        return t.ToLowerInvariant() switch
        {
            "enter" or "return" => "Intro",
            "escape" or "esc" => "Escape",
            "tab" => "Tabulador",
            "" => "una tecla",
            _ => t.ToUpperInvariant(),
        };
    }
}
