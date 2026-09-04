namespace U.WindowsClient.Clinical;

/// <summary>
/// EL ENCARGO: lo que el médico aprobó con un ✓ y quiere ver en la app. Promesa 112 (spec 008).
/// </summary>
/// <remarks>
/// SOLO VIAJA LO MARCADO. La nota organizada trae secciones que el médico todavía no ha leído; un
/// envío que las mandara todas por comodidad escribiría en la historia clínica cosas que nadie
/// aprobó. El ✓ es la aprobación, y esta clase es la que garantiza que no viaje nada más.
///
/// NO SABE DE SAP NI DE MEDICINA: son títulos y textos. Quien los escriba decide dónde. El texto
/// se compone con el título delante de cada sección, que es lo que el emparejador de Graph necesita
/// para saber qué es cada cosa («Hallazgos: peso 70 kg» dice más que «peso 70 kg»).
/// </remarks>
public sealed record Encargo(IReadOnlyList<SeccionDeNota> Secciones, string Texto)
{
    public bool EstaVacio => Secciones.Count == 0;

    /// <summary>Las secciones de la nota cuyas claves están marcadas, en el orden de la nota.</summary>
    public static Encargo De(NotaClinica nota, IReadOnlyCollection<string> clavesMarcadas)
    {
        var marcadas = new HashSet<string>(clavesMarcadas ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var secciones = (nota?.Secciones ?? Array.Empty<SeccionDeNota>())
            .Where(s => marcadas.Contains(s.Clave))
            .Where(s => !string.IsNullOrWhiteSpace(s.Contenido))
            .ToList();
        return new Encargo(secciones, Componer(secciones));
    }

    private static string Componer(IReadOnlyList<SeccionDeNota> secciones) =>
        string.Join("\n\n", secciones.Select(s =>
            (s.Titulo.Trim().Length > 0 ? s.Titulo.Trim() + ":\n" : "") + s.Contenido.Trim()));
}
