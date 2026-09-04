namespace U.WindowsClient.Navigation;

/// <summary>
/// ¿ESE RECORRIDO CUENTA COMO HABER COMPROBADO LA SKILL? Promesa 131 (spec 009, segunda tanda).
/// </summary>
/// <remarks>
/// NACE DE UN FALLO MEDIDO, no de una idea. El 2026-09-03 el dueño pulsó «Comprobar aprendizaje»
/// cuatro veces y su lectura fue «no hizo nada». El log dice algo peor:
///
///   12:12:47  comprobar: recorrido: hice 0 de 4 y paré en el paso 1: «sap:wnd[0]/tbar[0]/btn[3]»…
///   12:12:47  comprobar: «…» COMPROBADA en 4187 ms · 0 recuerdo(s) colgado(s) de 0
///
/// Sí corrió. Hizo CERO pasos de cuatro. Y estampó la skill como comprobada — tres veces seguidas.
/// A partir de ahí la promesa 127 («una skill sin comprobar no se ejecuta») deja pasar cualquier
/// cosa, porque todo lo que se intenta comprobar acaba comprobado. Un juez que absuelve sin haber
/// mirado es exactamente lo que este repo lleva dos meses aprendiendo a no construir: lo peor no es
/// que falle, es que parezca que funcionó.
///
/// LA REGLA, y no tiene matices: comprobar es ANDAR EL CAMINO ENTERO. Un plan a medias no dice si
/// el resto se puede andar — solo dice dónde se atascó, que es útil para arreglarlo y nada más.
///
/// Y EL PLAN VACÍO TAMPOCO CUENTA. «No había nada que recorrer» y «lo recorrí todo» dan el mismo
/// <c>hechos == total</c> con cero, y son lo contrario: el primero es una skill que no se pudo
/// traducir a pasos, que es justo la que no hay que dejar correr.
///
/// PURO: no toca disco, ni pantalla, ni skill. Solo decide, para que el contrato pueda juzgarlo.
/// </remarks>
public static class LaComprobacion
{
    /// <param name="Comprobada">Si este recorrido certifica la skill.</param>
    /// <param name="Motivo">Qué pasó, con números. Nunca vacío: una compuerta muda se aprende a
    /// saltar.</param>
    public readonly record struct Veredicto(bool Comprobada, string Motivo);

    /// <summary>
    /// ¿Vale como comprobación? <paramref name="relato"/> es la cuenta honesta del batch, que ya
    /// dice dónde paró y qué había vivo; aquí solo se decide y se antepone el veredicto.
    /// </summary>
    public static Veredicto Juzgar(int hechos, int total, string relato)
    {
        string cuenta = (relato ?? "").Trim();

        if (total <= 0)
            return new(false, "no salió ningún paso que recorrer, así que no hay nada que haya "
                            + "quedado comprobado. Vuelve a enseñarla con la aplicación delante."
                            + (cuenta.Length > 0 ? $" ({cuenta})" : ""));

        if (hechos >= total)
            return new(true, $"anduve los {total} paso(s) del plan."
                           + (cuenta.Length > 0 ? $" {cuenta}" : ""));

        return new(false, $"solo pude andar {hechos} de {total} paso(s), así que NO la doy por "
                        + $"comprobada: hasta que el camino entero se pueda repetir, ejecutarla "
                        + $"sería una apuesta." + (cuenta.Length > 0 ? $" {cuenta}" : ""));
    }
}
