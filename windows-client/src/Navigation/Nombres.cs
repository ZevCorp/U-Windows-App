namespace U.WindowsClient.Navigation;

/// <summary>
/// CÓMO SE COMPARAN DOS NOMBRES CUANDO UNO LO DIJO UNA PERSONA.
/// </summary>
/// <remarks>
/// Vive aparte porque la usan dos capacidades —abrir, para encontrar una app instalada, y señalar,
/// para saber si «Copilot» se refiere a «Copilot anclado»— y porque una misma pregunta tiene que
/// responderse en UN solo sitio: dos copias de este criterio acabarían discrepando el día que
/// alguien afine una, y entonces «abre X» y «pulsa X» dejarían de entender lo mismo.
/// </remarks>
public static class Nombres
{
    /// <summary>
    /// Sin mayúsculas, sin tildes y sin espacios de más.
    ///
    /// «Microsoft To Do» y «microsoft  to do» son la misma app, y quien habla no escribe los
    /// acentos: exigirlos convierte «cálculadora» en un fallo cuando es la misma palabra.
    /// </summary>
    public static string Aplanar(string s)
    {
        s = (s ?? "").Trim().ToLowerInvariant();
        var sb = new System.Text.StringBuilder(s.Length);
        bool espacio = false;
        foreach (char c in s.Normalize(System.Text.NormalizationForm.FormD))
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            if (char.IsWhiteSpace(c)) { if (!espacio && sb.Length > 0) { sb.Append(' '); espacio = true; } continue; }
            sb.Append(c); espacio = false;
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// ¿Estas dos formas de nombrar algo hablan de lo mismo? Sin exigir que se diga clavado.
    /// </summary>
    /// <remarks>
    /// Windows llama a las cosas como le da la gana: el icono de la barra es «Copilot anclado» y una
    /// ventana abierta es «Claude- 2 ventanas de ejecución». Nadie dice eso; se dice «Copilot».
    /// Exigir igualdad exacta hacía fallar «¿ves este icono? ábrelo» SIEMPRE que el nombre real
    /// llevara una palabra de más — que es casi siempre (2026-08-23, probado por el usuario).
    /// </remarks>
    public static bool HablanDeLoMismo(string uno, string otro)
    {
        string a = Aplanar(uno), b = Aplanar(otro);
        if (a.Length == 0 || b.Length == 0) return false;
        return a == b
            || b.Contains(a, StringComparison.Ordinal)
            || a.Contains(b, StringComparison.Ordinal);
    }
}
