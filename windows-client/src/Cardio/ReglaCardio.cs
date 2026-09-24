using System.Text.RegularExpressions;

namespace U.WindowsClient.Cardio;

/// <summary>
/// Lo que el panel de estudios DICE, decidido fuera del panel para que el contrato lo juzgue sin
/// pantalla (promesa 356).
/// </summary>
public static class ReglaCardio
{
    /// <summary>
    /// El botón principal dice lo que va a hacer. «Resumen al día» no es un adorno: sin él, un botón
    /// que dice «Generar resumen» con el resumen ya hecho invita a pagar otra llamada para nada.
    /// </summary>
    public static string EtiquetaDelBoton(int fotos, bool hayResumen, bool hayCambios) =>
        !hayResumen ? $"Generar resumen ({fotos})"
        : hayCambios ? "Actualizar resumen"
        : "Resumen al día";

    public static bool BotonHabilitado(int fotos, bool hayResumen, bool hayCambios) =>
        fotos > 0 && (!hayResumen || hayCambios);

    /// <summary>«fotos 4–6» o «foto 7». Lo usan el progreso y el mensaje de error, que tienen que coincidir.</summary>
    public static string Rango(int desde, int hasta) => desde == hasta ? $"foto {desde}" : $"fotos {desde}–{hasta}";

    /// <summary>
    /// «Leyendo fotos 4–6 de 12…». EL TOTAL ES EL PLAN, no lo que ya se leyó (patrón nº10): un
    /// denominador que crece mientras se lee dice con exactitud una corrida que no es.
    /// </summary>
    public static string Progreso(int desde, int hasta, int total) => $"Leyendo {Rango(desde, hasta)} de {total}…";

    /// <summary>
    /// ¿El resumen ya no describe lo que hay? Sí si hay fotos sin leer, o si el conjunto de fotos no es
    /// el mismo que se resumió —agregar Y quitar cuentan: quitar deja en el resumen algo que ya no está—.
    /// </summary>
    public static bool HayCambios(SesionCardio s)
    {
        if (s.Fotos.Any(f => f.Resultado == null || f.Resultado.Estado == EstadoDeFoto.SinLeer)) return true;
        var ahora = s.Fotos.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
        return !ahora.SetEquals(s.FotosDelResumen);
    }

    /// <summary>El pie del panel: cuánto les queda a las fotos.</summary>
    public static string TextoDeCaducidad(TimeSpan queda)
    {
        if (queda <= TimeSpan.Zero) return "Se borran ahora";
        if (queda.TotalHours >= 1) return $"Se borran solas en {(int)Math.Ceiling(queda.TotalHours)} h";
        if (queda.TotalMinutes >= 1) return $"Se borran solas en {(int)Math.Ceiling(queda.TotalMinutes)} min";
        return "Se borran solas en menos de un minuto";
    }
}

public enum TipoDeBloque { Titulo, Vineta, Parrafo }

public sealed class TrozoMd
{
    public string Texto { get; set; } = "";
    public bool Negrita { get; set; }
}

public sealed class BloqueMd
{
    public TipoDeBloque Tipo { get; set; }
    public List<TrozoMd> Trozos { get; set; } = new();
}

/// <summary>
/// El markdown que escribe el modelo, reducido a lo que el panel pinta: títulos, viñetas y negritas.
/// </summary>
/// <remarks>
/// SEGURO POR CONSTRUCCIÓN (promesa 355): esto no produce HTML ni XAML, produce trozos de texto que
/// el panel mete en <c>Run</c>s. Un «&lt;script&gt;» o un «&lt;Button&gt;» que venga del modelo —o de una foto
/// que el modelo transcribió— se ve tal cual, porque no hay nada que lo interprete.
/// </remarks>
public static class MarkdownSimple
{
    private static readonly Regex Titulo = new(@"^\s{0,3}#{1,6}\s+(.*?)\s*#*\s*$", RegexOptions.Compiled);
    private static readonly Regex Vineta = new(@"^\s*[-*•]\s+(.*)$", RegexOptions.Compiled);

    public static List<BloqueMd> Bloques(string texto)
    {
        var bloques = new List<BloqueMd>();
        foreach (string cruda in (texto ?? "").Replace("\r", "").Split('\n'))
        {
            string linea = cruda.TrimEnd();
            if (linea.Trim().Length == 0) continue;
            var t = Titulo.Match(linea);
            if (t.Success) { bloques.Add(new BloqueMd { Tipo = TipoDeBloque.Titulo, Trozos = Trozos(t.Groups[1].Value) }); continue; }
            var v = Vineta.Match(linea);
            if (v.Success) { bloques.Add(new BloqueMd { Tipo = TipoDeBloque.Vineta, Trozos = Trozos(v.Groups[1].Value) }); continue; }
            bloques.Add(new BloqueMd { Tipo = TipoDeBloque.Parrafo, Trozos = Trozos(linea.Trim()) });
        }
        return bloques;
    }

    /// <summary>Negritas por pares de «**». Un «**» sin pareja se queda como texto: no se adivina dónde cerraba.</summary>
    public static List<TrozoMd> Trozos(string linea)
    {
        var partes = linea.Split("**").ToList();
        if (partes.Count % 2 == 0)
        {
            // Número impar de marcas: la última no cierra nada. Se devuelve a su sitio, literal.
            partes[^2] = partes[^2] + "**" + partes[^1];
            partes.RemoveAt(partes.Count - 1);
        }
        var trozos = new List<TrozoMd>();
        for (int i = 0; i < partes.Count; i++)
            if (partes[i].Length > 0) trozos.Add(new TrozoMd { Texto = partes[i], Negrita = i % 2 == 1 });
        return trozos;
    }
}
