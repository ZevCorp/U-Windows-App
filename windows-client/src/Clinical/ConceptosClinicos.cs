using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace U.WindowsClient.Clinical;

/// <summary>Un dato clínico leído de la nota: el valor ya formateado y el trozo que lo justifica.</summary>
public sealed record Concepto(string Valor, string Evidencia);

/// <summary>
/// LOS CONCEPTOS CANÓNICOS DE LA NOTA —`vital.talla`, `vital.peso`, `consulta.motivo`…—, leídos
/// EXACTAMENTE como los lee la web. Promesa 451 (spec 055).
/// </summary>
/// <remarks>
/// PUERTO LÍNEA A LÍNEA de `lib/clinical/vital-concepts.ts` (`extractConcepts`) del portal. Aquí lo
/// usa la revisión de la nota (¿quedaron signos vitales?), y es el mismo contrato que el portal
/// entrega al agente de escritorio por `/api/agent/values`: si Windows leyera un 120/80 que la web
/// no lee, los dos productos dirían cosas distintas de la misma nota.
///
/// Las tres reglas de la web, que son de seguridad clínica y no de estilo: todo patrón va ANCLADO A
/// SU ETIQUETA (un «57» suelto no es un peso), todo valor pasa por un RANGO PLAUSIBLE (una
/// temperatura de 896 es una frecuencia mal leída) y lo dudoso NO SE DEVUELVE.
///
/// ECMASCRIPT Y NO EL MODO DE .NET, a propósito: en JavaScript `\b` solo conoce letras ASCII y en
/// .NET todas. «card[ií]aca\b» se comporta distinto en cuanto hay una tilde al lado de la frontera.
/// La promesa 451 lo juzga contra lo que la web contestó de verdad.
/// </remarks>
public static class ConceptosClinicos
{
    private sealed record Regla(string Clave, Regex Patron, double Min, double Max, int Decimales);

    // «talla de 1.70», «FC: 88 x min»: ruido entre etiqueta y número SIN cruzar a la frase siguiente.
    private const string Hueco = @"[^.\n]{0,12}?";
    private const string Numero = @"(\d{1,3}(?:[.,]\d{1,2})?)";
    private const RegexOptions Js = RegexOptions.IgnoreCase | RegexOptions.ECMAScript | RegexOptions.CultureInvariant;

    private static readonly Regla[] Reglas =
    {
        new("vital.talla", new Regex(@"\b(?:talla|estatura)\b" + Hueco + Numero, Js), 0.4, 2.6, 2),
        new("vital.peso", new Regex(@"\bpeso\b" + Hueco + Numero, Js), 0.5, 400, 1),
        new("vital.frecuencia.cardiaca", new Regex(@"\b(?:frecuencia\s+card[ií]aca|f\.?\s?c\.?|pulso)\b" + Hueco + Numero, Js), 20, 250, 0),
        new("vital.frecuencia.respiratoria", new Regex(@"\b(?:frecuencia\s+respiratoria|f\.?\s?r\.?)\b" + Hueco + Numero, Js), 4, 80, 0),
        new("vital.temperatura", new Regex(@"\b(?:temperatura|temp\.?)\b" + Hueco + Numero, Js), 30, 43, 1),
        new("vital.saturacion", new Regex(@"\b(?:saturaci[oó]n(?:\s+de\s+ox[ií]geno)?|sat\.?\s?o2|spo2)\b" + Hueco + Numero, Js), 40, 100, 0),
        // La edad se ancla por la UNIDAD de después: casi nunca se dice «edad», se dice «de 68 años».
        new("paciente.edad", new Regex(Numero + @"\s*a[nñ]os?\b", Js), 0, 120, 0),
    };

    // Dos números en una expresión: se leen juntos o no se leen.
    private static readonly Regex Presion = new(
        @"\b(?:t\.?\s?a\.?|tensi[oó]n|presi[oó]n)(?:\s+arterial)?\b" + Hueco + @"(\d{2,3})\s*(?:\/|sobre)\s*(\d{2,3})", Js);

    private static readonly Regex Espacios = new(@"\s+");

    /// <summary>Los conceptos que se pueden afirmar de estas secciones. Lo que no encaja no sale.</summary>
    public static IReadOnlyDictionary<string, Concepto> Extraer(IReadOnlyList<SeccionDeNota> secciones)
    {
        var fuera = new Dictionary<string, Concepto>(StringComparer.Ordinal);
        string texto = TextoDe(secciones);
        if (texto.Trim().Length == 0) return fuera;

        foreach (var regla in Reglas)
        {
            var m = regla.Patron.Match(texto);
            if (!m.Success || !m.Groups[1].Success) continue;
            string? valor = Formatear(m.Groups[1].Value, regla);
            if (valor == null) continue;
            fuera[regla.Clave] = new Concepto(valor, Alrededor(texto, m.Index, m.Length));
        }

        var motivo = MotivoDeConsulta(secciones);
        if (motivo != null) fuera["consulta.motivo"] = motivo;

        var pa = Presion.Match(texto);
        if (pa.Success)
        {
            int sis = int.Parse(pa.Groups[1].Value, CultureInfo.InvariantCulture);
            int dia = int.Parse(pa.Groups[2].Value, CultureInfo.InvariantCulture);
            // La sistólica es mayor que la diastólica, siempre: al revés es que se leyó mal.
            if (sis > dia && sis >= 50 && sis <= 300 && dia >= 20 && dia <= 200)
            {
                string evidencia = Alrededor(texto, pa.Index, pa.Length);
                fuera["vital.presion.sistolica"] = new Concepto(sis.ToString(CultureInfo.InvariantCulture), evidencia);
                fuera["vital.presion.diastolica"] = new Concepto(dia.ToString(CultureInfo.InvariantCulture), evidencia);
            }
        }
        return fuera;
    }

    /// <summary>`noteToText`: los contenidos no vacíos, uno por línea. Los títulos no entran.</summary>
    public static string TextoDe(IReadOnlyList<SeccionDeNota> secciones)
    {
        var partes = new List<string>();
        foreach (var s in secciones)
        {
            string t = (s.Contenido ?? "").Trim();
            if (t.Length > 0) partes.Add(t);
        }
        return string.Join("\n", partes);
    }

    private static string? Formatear(string crudo, Regla regla)
    {
        // `raw.replace(",", ".")` de la web cambia SOLO la primera coma, y aquí también.
        int coma = crudo.IndexOf(',');
        string normal = coma >= 0 ? crudo[..coma] + "." + crudo[(coma + 1)..] : crudo;
        if (!double.TryParse(normal, NumberStyles.Float, CultureInfo.InvariantCulture, out double n)) return null;

        double valor = n;
        // Talla dictada en centímetros: 170 → 1,70. Solo aquí, y solo con un valor que no puede ser metros.
        if (regla.Clave == "vital.talla" && n >= 40 && n <= 260) valor = n / 100;
        if (valor < regla.Min || valor > regla.Max) return null;

        // Math.round de JavaScript sube en el .5; Math.Round de .NET redondea al par. Se usa el de la web.
        return regla.Decimales == 0
            ? Math.Floor(valor + 0.5).ToString(CultureInfo.InvariantCulture)
            : valor.ToString("F" + regla.Decimales, CultureInfo.InvariantCulture);
    }

    private static string Alrededor(string texto, int indice, int largo)
    {
        int desde = Math.Max(0, indice - 24);
        int hasta = Math.Min(texto.Length, indice + largo + 16);
        return Espacios.Replace(texto[desde..hasta], " ").Trim();
    }

    /// <summary>
    /// El motivo se identifica por la SECCIÓN donde vive («motivo» en su nombre o su clave, sin
    /// tildes), no por una palabra dentro de la frase. La primera que case y tenga contenido.
    /// </summary>
    private static Concepto? MotivoDeConsulta(IReadOnlyList<SeccionDeNota> secciones)
    {
        foreach (var s in secciones)
        {
            string nombre = SinTildes($"{s.Titulo ?? ""} {s.Clave ?? ""}".ToLowerInvariant());
            if (!nombre.Contains("motivo", StringComparison.Ordinal)) continue;
            string texto = (s.Contenido ?? "").Trim();
            if (texto.Length == 0) continue;
            return new Concepto(texto, texto);
        }
        return null;
    }

    /// <summary>`normalize("NFD").replace(/[̀-ͯ]/g, "")` de la web.</summary>
    public static string SinTildes(string texto)
    {
        var sb = new StringBuilder(texto.Length);
        foreach (char c in texto.Normalize(NormalizationForm.FormD))
            if (c < '̀' || c > 'ͯ') sb.Append(c);
        return sb.ToString();
    }
}
