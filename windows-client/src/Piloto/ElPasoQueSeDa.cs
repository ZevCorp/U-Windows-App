using System;

namespace U.WindowsClient.Piloto;

/// <summary>
/// LA IDENTIDAD DE UN PASO, decidida en un solo sitio. Promesa 191.
/// </summary>
/// <remarks>
/// POR QUÉ (2026-09-08, undécima prueba): el piloto trajo el SELECTOR de la fila del paciente en vez
/// de su nombre, y el plan paró en el paso 3 con «no lo conozco»: el selector de una fila de ALV
/// lleva la fecha y la hora de la lista y cambia con ella; el nombre —«GIRALDO»— no. Y para
/// escribir es al revés: manda el selector exacto que el grabador leyó, aunque el piloto traiga el
/// nombre. Dos reglas que vivían sueltas en el recorrido del plan y no valían para las manos.
/// </remarks>
public static class ElPasoQueSeDa
{
    /// <summary>Un selector se reconoce por su prefijo de mundo; un nombre no lo lleva.</summary>
    public static bool EsSelector(string s) =>
        (s ?? "").StartsWith("sap:", StringComparison.OrdinalIgnoreCase)
        || (s ?? "").StartsWith("uia:", StringComparison.OrdinalIgnoreCase);

    /// <summary>Con qué se señala y se nombra el paso, y con qué se le pide al ejecutor.</summary>
    public sealed record Identidad(string ParaSenalar, string ParaElEjecutor);

    public static Identidad Resolver(string exitDelPiloto, string etiquetaDelEvento, string selectorDelEvento, string texto)
    {
        string exit = (exitDelPiloto ?? "").Trim();
        string etiqueta = (etiquetaDelEvento ?? "").Trim();
        string selector = (selectorDelEvento ?? "").Trim();
        bool teclea = (texto ?? "").Length > 0;

        // SE SEÑALA Y SE NOMBRA POR LA PUERTA: el nombre que la persona lee y el terreno conoce.
        string senalar = exit.Length == 0 ? etiqueta
                       : EsSelector(exit) && etiqueta.Length > 0 ? etiqueta
                       : exit;

        // SE ESCRIBE POR EL SELECTOR DE LA LECCIÓN (promesa 133): es el campo exacto donde la demo
        // tecleó. Para pulsar, por la puerta, por la misma razón que arriba.
        string ejecutor = teclea && selector.Length > 0 ? selector : senalar;

        return new Identidad(senalar, ejecutor);
    }
}
