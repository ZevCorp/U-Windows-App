namespace U.WindowsClient.Clinical;

/// <summary>
/// LA COMPUERTA DEL ENVÍO: solo se escribe con el triage delante. Promesa 114 (spec 008).
/// </summary>
/// <remarks>
/// El guardián existía (<see cref="RellenadorSap.EsLaPantallaDeTriage"/>, por programa
/// <c>SAPLY000</c>) y lo usaba el ejecutor de exportaciones desde el 2026-08-25 — pero nadie lo
/// prometía, y un envío desde la ventana de consulta que llegara a otra pantalla escribiría datos
/// clínicos en el formulario que fuera. Se pregunta a SAP, no al foco: la escritura va por
/// scripting y no necesita la ventana delante (la lección del exportador, 2026-08-25).
/// </remarks>
public static class EnvioAlTriage
{
    public readonly record struct Veredicto(bool Puede, string Motivo);

    public static Veredicto PuedeEscribir(string ubicacionDeSap)
    {
        string donde = (ubicacionDeSap ?? "").Trim();
        if (donde.Length == 0)
            return new(false, "no sé qué pantalla muestra SAP: no escribo a ciegas");
        if (RellenadorSap.EsLaPantallaDeTriage(donde))
            return new(true, "");
        return new(false, $"SAP está en «{donde}», no en el triage: no se escribe nada ahí");
    }
}
