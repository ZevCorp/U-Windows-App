using U.WindowsClient.Teach;

namespace U.WindowsClient.Navigation;

/// <summary>Una captura del paso a paso: qué paso es, qué pantalla se vio y qué pasa en ella.</summary>
public sealed record Captura(int Paso, string Cuadro, string Que);

/// <summary>
/// LAS CAPTURAS DE UN APRENDIZAJE, SACADAS DE SU LECCIÓN. Promesa 200 (spec 016). Puro.
/// </summary>
/// <remarks>
/// SE EMPAREJA POR IDENTIDAD, NO POR POSICIÓN, y la diferencia no es un detalle: los pasos que no
/// aterrizaron al comprobar NO entran en la skill (promesa 176), así que la skill casi nunca tiene
/// tantos pasos como eventos la lección. Emparejar por posición habría corrido todas las capturas
/// un sitio y cada paso enseñaría la pantalla del siguiente — sin error, sin aviso, y con toda la
/// pinta de funcionar. Una caja que miente es peor que no tener caja (aprendizaje nº8).
///
/// EL CUADRO ES EL DE ANTES DE TOCAR: es la pantalla tal como estaba cuando la persona hizo ese
/// paso, con el anillo del ratón sobre el elemento. El de después es el resultado, y pertenece al
/// paso siguiente.
///
/// SIN EVENTO QUE CASE, SIN CUADRO. No se toma «el más cercano» ni se hereda el del paso anterior:
/// un hueco en la tira se ve y se entiende; un cuadro ajeno, no.
/// </remarks>
public static class CapturasDeLaSkill
{
    public static IReadOnlyList<Captura> De(SkillEnsenada skill, Leccion leccion)
    {
        var salida = new List<Captura>();
        if (skill == null) return salida;
        var frases = LoQueHaceLaSkill.EnCastellano(skill);
        var eventos = leccion?.Eventos ?? Array.Empty<EventoDeLaLeccion>();

        for (int i = 0; i < skill.Pasos.Count; i++)
        {
            var p = skill.Pasos[i];
            salida.Add(new Captura(i + 1, CuadroDe(p, eventos), i < frases.Count ? frases[i] : ""));
        }
        return salida;
    }

    /// <summary>
    /// El cuadro del evento que este paso repite, o vacío. El ÚLTIMO que case, por la misma
    /// convención que el juez: si alguien escribió 80 y corrigió a 82, el paso es el 82.
    /// </summary>
    private static string CuadroDe(PasoEnsenado paso, IReadOnlyList<EventoDeLaLeccion> eventos)
    {
        string busco = InstanciarSkill.Identidad(paso.Exit);
        if (busco.Length == 0) return "";
        string texto = (paso.Texto ?? "").Trim();
        EventoDeLaLeccion? elegido = null;
        foreach (var e in eventos)
        {
            // La identidad del evento es la que la skill guardó: la puerta por su nombre si la hay,
            // y el selector si no (ver SkillDeLoVerificado).
            string suya = InstanciarSkill.Identidad(
                !string.IsNullOrWhiteSpace(e.Etiqueta) ? e.Etiqueta : e.Selector ?? "");
            if (suya.Length == 0 || !suya.Equals(busco, StringComparison.OrdinalIgnoreCase)) continue;
            // Con dos tecleos en el mismo campo, manda el que escribió lo mismo que el paso.
            if (texto.Length > 0 && (e.Texto ?? "").Trim().Length > 0
                && !(e.Texto ?? "").Trim().Equals(texto, StringComparison.OrdinalIgnoreCase)) continue;
            elegido = e;
        }
        return (elegido?.CuadroAntes ?? "").Trim();
    }
}
