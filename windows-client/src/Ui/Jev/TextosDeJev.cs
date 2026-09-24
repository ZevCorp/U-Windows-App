using System.Globalization;

namespace U.WindowsClient.Ui.Jev;

/// <summary>
/// LAS CADENAS DEL PANEL DE JEV, una por estado, y ninguna concluye. Promesa 374 (spec 049).
/// </summary>
/// <remarks>
/// DESCRIBEN EL PASO, NO CONCLUYEN (patrón nº2). «Jev cree que ya está» y no «listo»: lo dice un modelo que ya
/// declaró éxito habiendo pulsado «Buscar pacientes» en vez de «Crear Triage Administrativo» (CLAUDE.md,
/// pendiente nº2). El contrato lee todas las constantes de esta clase y rechaza cualquiera que diga que algo
/// terminó bien; por eso los formatos son constantes y no cadenas interpoladas escondidas en un método.
///
/// LO QUE NO ESTÁ AQUÍ, a propósito: el motivo con que el tramo para y el porqué del decisor se enseñan TAL CUAL
/// llegan (<c>ElTramo.cs:150-202</c>, <c>ElDecisor.cs</c>). Reescribirlos haría que el panel y la voz
/// (<c>AvisarALaVoz</c>) contaran lo mismo con dos frases.
///
/// Números con <see cref="CultureInfo.InvariantCulture"/>, como el resto de Ü (<c>ElDecisor.cs</c>): «0.94» con
/// punto en cualquier máquina. Las cadenas son las del plano (<c>plano-para-wpf.md</c> §Cada estado) salvo dos,
/// que manda la spec 049: la segunda mejor dice la verdad de la mano («la 1.ª no estaba») y actuar lleva el
/// número de la elegida, como la línea de progreso, porque dos «Buscar» existen.
/// </remarks>
public static class TextosDeJev
{
    /// <summary>Mientras se lee la pantalla.</summary>
    public const string Mirando = "Mirando la pantalla";

    /// <summary>Eligiendo: {0} el paso, {1} cuántas candidatas se ofrecen.</summary>
    public const string FormatoEligiendo = "Paso {0} — {1} elementos";

    /// <summary>Jev actúa: {0} la etiqueta de la elegida, {1} su número. La mano está pulsando.</summary>
    public const string FormatoPulsando = "Pulsando «{0}» ({1})";

    /// <summary>
    /// La mano pulsó la segunda mejor porque la elegida no estaba: {0} su etiqueta, {1} su número. Es lo único
    /// que hace <c>UnPasoDecidido</c> cuando la primera «no está», y el panel lo dice como lo que es.
    /// </summary>
    public const string FormatoSegundaMejor = "la 1.ª no estaba: pulsé la 2.ª «{0}» ({1})";

    /// <summary>Cumplido alto: {0} el cumplido. NO dice que esté hecho: dice que Jev lo cree, y que por eso para.</summary>
    public const string FormatoCreeQueYaEsta = "Jev cree que ya está ({0:0.00}). No acciono más.";

    /// <summary>Peligro alto: {0} la etiqueta de la elegida, {1} el peligro.</summary>
    public const string FormatoNoSeDeshace = "«{0}» no se deshace (peligro {1:0.00}). Me detengo.";

    /// <summary>Jev dudó: {0} su confianza. Solo con distribución: sin ella Jev no llegó a dudar.</summary>
    public const string FormatoNoEstoySeguro = "No estoy seguro ({0:0.00}). Me detengo.";

    /// <summary>Cabecera: el paso.</summary>
    public const string FormatoCabeceraPaso = "paso {0}";

    /// <summary>Cabecera: cuántas candidatas se ofrecieron.</summary>
    public const string FormatoCabeceraDetectados = "· {0} detectados";

    /// <summary>Cabecera: los ms de decidir, con separador de miles («1,450ms», como el vídeo en t=14,55).</summary>
    public const string FormatoCabeceraMs = "{0:N0}ms";

    /// <summary>
    /// Lo que se enseña donde no hay dato: un medidor que no vino o un paso sin tokens facturados. Nunca «0»:
    /// cero es una medida, y «no vino» no lo es (373).
    /// </summary>
    public const string SinDato = "—";

    private static readonly CultureInfo Invariante = CultureInfo.InvariantCulture;

    /// <summary>«Paso {k} — {n} elementos».</summary>
    public static string Eligiendo(int paso, int elementos) => string.Format(Invariante, FormatoEligiendo, paso, elementos);

    /// <summary>«Pulsando «{etiqueta}» ({n})».</summary>
    public static string Pulsando(string etiqueta, string? numero) => string.Format(Invariante, FormatoPulsando, etiqueta, numero);

    /// <summary>«la 1.ª no estaba: pulsé la 2.ª «{etiqueta}» ({n})».</summary>
    public static string SegundaMejor(string etiqueta, string numero) => string.Format(Invariante, FormatoSegundaMejor, etiqueta, numero);

    /// <summary>«Jev cree que ya está ({c}). No acciono más.».</summary>
    public static string CreeQueYaEsta(double cumplido) => string.Format(Invariante, FormatoCreeQueYaEsta, cumplido);

    /// <summary>««{etiqueta}» no se deshace (peligro {p}). Me detengo.».</summary>
    public static string NoSeDeshace(string etiqueta, double peligro) => string.Format(Invariante, FormatoNoSeDeshace, etiqueta, peligro);

    /// <summary>«No estoy seguro ({c}). Me detengo.».</summary>
    public static string NoEstoySeguro(double confianza) => string.Format(Invariante, FormatoNoEstoySeguro, confianza);
}
