namespace U.WindowsClient.Clinical;

/// <summary>
/// LOS DOS EDITORES DE TEXTO LIBRE DEL TRIAGE —Motivo de Consulta y Conducta— y de qué sección
/// de la nota sale cada uno. Promesa 113 (spec 008).
/// </summary>
/// <remarks>
/// EXISTE PORQUE EL RELLENADOR NO LOS VE: son shells <c>GuiTextedit</c>, no campos del dynpro, y
/// <c>ReadFields</c> no los devuelve (medido en la demo del 2026-08-31, que los escribía aparte
/// con dos frases fijas). El emparejador de Graph no puede decidir sobre lo que no le llega, así
/// que se reparten aquí, por el TÍTULO de la sección, que es lo que una persona miraría.
///
/// SIN SECCIÓN QUE LO DIGA, VACÍO. No se pega la nota entera en «Motivo de consulta» por llenar
/// algo: en una historia clínica, un motivo inventado es peor que un motivo en blanco (aprendizaje
/// nº4: una caja que miente es peor que no tener caja).
/// </remarks>
public static class EditoresDelTriage
{
    public readonly record struct Reparto(string Motivo, string Conducta);

    private static readonly string[] SuenaAMotivo = { "motivo", "enfermedad actual", "anamnesis", "consulta por" };
    private static readonly string[] SuenaAConducta = { "conducta", "plan", "recomend", "manejo", "tratamiento" };

    public static Reparto Repartir(IReadOnlyList<SeccionDeNota> secciones)
    {
        secciones ??= Array.Empty<SeccionDeNota>();
        string motivo = Primera(secciones, SuenaAMotivo);
        string conducta = Primera(secciones, SuenaAConducta);
        return new Reparto(motivo, conducta);
    }

    private static string Primera(IReadOnlyList<SeccionDeNota> secciones, string[] pistas)
    {
        foreach (var s in secciones)
        {
            string nombre = Navigation.Nombres.Aplanar(s.Titulo + " " + s.Clave);
            if (pistas.Any(p => nombre.Contains(Navigation.Nombres.Aplanar(p), StringComparison.Ordinal))
                && !string.IsNullOrWhiteSpace(s.Contenido))
                return s.Contenido.Trim();
        }
        return "";
    }
}
