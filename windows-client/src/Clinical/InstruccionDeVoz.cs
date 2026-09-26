using System.Text.RegularExpressions;

namespace U.WindowsClient.Clinical;

/// <summary>
/// Qué quiso decir el médico al hablarle a una sección: «literal», «dictado» o «ajuste».
/// </summary>
/// <param name="Texto">Lo que se escribe (literal), el dato que se aporta (dictado) o la instrucción (ajuste).</param>
public sealed record Intencion(string Modo, string Texto);

/// <summary>
/// LO QUE SE DICE POR EL MICRÓFONO DE UNA SECCIÓN, entendido como lo entiende la web. Promesa 452
/// (spec 055).
/// </summary>
/// <remarks>
/// PUERTO LÍNEA A LÍNEA de `lib/clinical/voice-instruction.ts`. Tres cosas distintas llegan por el
/// mismo micrófono, y el médico elige cuál al hablar, sin botones:
///
///   · «quiero que diga: control en ocho días» — LITERAL: va tal cual, sin modelo. Es lo que esquiva
///     la guarda del ajuste («prohibido agregar datos clínicos nuevos»): no es el modelo quien añade
///     el dato, es el médico quien lo escribe. Instantáneo y sin coste.
///   · «agrega que el paciente niega fiebre» — DICTADO: el médico aporta un dato y quiere que quede
///     redactado dentro de la sección. Va al modelo como `dictation`.
///   · «hazla más corta» — AJUSTE: una instrucción sobre lo que ya hay. Va como `rewrite`.
///
/// Las regex no llevan `\b`, así que el modo normal de .NET se comporta como el de JavaScript; la
/// promesa 452 lo comprueba contra lo que la web contestó de verdad.
/// </remarks>
public static class InstruccionDeVoz
{
    private const RegexOptions Opciones = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    // «Quiero que diga X», «que quede X». El verbo tiene que ser de DECIR.
    private static readonly Regex AnunciaTexto = new(
        @"^\s*(?:quiero|necesito|quisiera|agrega|agregue|a[ñn]ade|escribe|escriba|anota|anote|pon|ponga|coloca|coloque)?\s*que\s+(?:diga|digas|diga\s+as[ií]|quede|quede\s+as[ií]|se\s+lea)\s*(?:esto|lo\s+siguiente|as[ií])?\s*[:,]?\s+(.+)$",
        Opciones);

    // «Textualmente X», «escribe literal X», «tal cual X».
    private static readonly Regex MarcaLiteral = new(
        @"^\s*(?:escribe|escriba|anota|anote|pon|ponga|coloca|coloque|agrega|agregue|a[ñn]ade|dicta|dicte)?\s*(?:esto\s+)?(?:textualmente|textual|literalmente|literal|tal\s+cual)\s*[:,]?\s+(.+)$",
        Opciones);

    // «Escribe esto: X», «anota lo siguiente: X».
    private static readonly Regex AnunciaEsto = new(
        @"^\s*(?:escribe|escriba|anota|anote|pon|ponga|coloca|coloque|agrega|agregue|a[ñn]ade)\s+(?:esto|lo\s+siguiente)\s*[:,]?\s+(.+)$",
        Opciones);

    private static readonly Regex[] Literales = { AnunciaTexto, MarcaLiteral, AnunciaEsto };

    // «Agrega que el paciente niega fiebre»: añadir + «que» + un dato (no un verbo de decir).
    private static readonly Regex DictaDato = new(
        @"^\s*(?:agrega|agregue|a[ñn]ade|a[ñn]ada|anota|anote|pon|ponga|escribe|escriba|incluye|incluya|registra|registre|consigna|consigne)\s+que\s+(?!diga|digas|quede|se\s+lea)(.+)$",
        Opciones);

    // Las frases con las que el generador rellena lo que no se habló: cuentan como vacío.
    private static readonly Regex RellenoDeSeccion = new(
        @"^(?:no\s+(?:referid|mencionad|document|registrad|consignad|explorad|interrogad|evaluad|realizad)|sin\s+(?:dato|informaci|hallazg)|no\s+se\s+(?:menciona|refiere|document|registra|interrog)|pendiente)",
        RegexOptions.CultureInvariant);

    private static readonly Regex TerminaEnPuntuacion = new(@"[.:;!?]$");

    /// <summary>Lee lo dicho. Nulo si no hay nada utilizable (el micrófono a veces entrega vacío).</summary>
    public static Intencion? Interpretar(string? dicho)
    {
        string limpio = (dicho ?? "").Trim();
        if (limpio.Length == 0) return null;

        foreach (var patron in Literales)
        {
            var m = patron.Match(limpio);
            string texto = m.Success ? m.Groups[1].Value.Trim() : "";
            // Un anuncio sin texto detrás es una frase a medias, no un literal vacío que borre la sección.
            if (texto.Length > 0) return new Intencion("literal", texto);
        }

        var d = DictaDato.Match(limpio);
        string dato = d.Success ? d.Groups[1].Value.Trim() : "";
        if (dato.Length > 0) return new Intencion("dictado", dato);

        return new Intencion("ajuste", limpio);
    }

    /// <summary>¿Lo que hay en la sección es relleno del generador y no contenido?</summary>
    public static bool EsRelleno(string? contenido)
    {
        string limpio = (contenido ?? "").Trim();
        if (limpio.Length == 0) return true;
        return RellenoDeSeccion.IsMatch(ConceptosClinicos.SinTildes(limpio.ToLowerInvariant()));
    }

    /// <summary>
    /// La sección después de un literal: sustituye el relleno, y si había contenido lo AÑADE al final.
    /// Nunca borra lo que ya estaba: si se dictó de más, se corrige a mano.
    /// </summary>
    public static string AplicarLiteral(string? actual, string dictado)
    {
        string texto = (dictado ?? "").Trim();
        if (texto.Length == 0) return actual ?? "";
        if (EsRelleno(actual)) return texto;
        string previo = (actual ?? "").TrimEnd();
        string separador = TerminaEnPuntuacion.IsMatch(previo) ? " " : ". ";
        return previo + separador + texto;
    }
}
