using System;

namespace U.WindowsClient.Teach;

/// <summary>Un paso que el grabador de SAP publicó: cuándo, desde qué pantalla y qué puerta.</summary>
public sealed record PasoQueVioSap(long HoraMs, string Superficie, string Selector);

/// <summary>Un cambio de pantalla que SAP anunció: cuándo, de dónde y a dónde.</summary>
public sealed record CambioQueVioSap(long HoraMs, string Desde, string Hasta);

/// <summary>
/// LA ARISTA QUE VIO EL GRABADOR DE SAP. Promesa 189: un paso publicado y la pantalla nueva que SAP
/// anuncia justo después son un cruce que se le puede enseñar al terreno.
/// </summary>
/// <remarks>
/// POR QUÉ (2026-09-08, y ya en la quinta y la séptima prueba): al pulsar «Triage», SAP tarda más de
/// lo que el mapa vivo espera (6 s) y el mapa se queda ciego mientras SAP no contesta; al volver, el
/// salto queda «SIN atribuir» y la lección sale con la llegada de Triage vacía. El grabador SÍ lo vio:
/// publicó el paso a las 01:11:38 y anunció «pantalla nueva» a las 01:11:41. Esto empareja los dos.
///
/// LA VENTANA ES CORTA a propósito, como en el mapa vivo: una arista falsa es peor que ninguna, porque
/// el navegador la usaría para volver y pulsaría otra cosa. SAP visto por UIA no es un destino.
/// </remarks>
public static class ElCruceQueVioSap
{
    /// <summary>Cuánto puede tardar SAP entre publicar el paso y anunciar la pantalla nueva.</summary>
    public const long VentanaMs = 15_000;

    public static (string Desde, string Selector, string Hasta)? Emparejar(PasoQueVioSap? ultimoPaso, CambioQueVioSap cambio)
    {
        if (ultimoPaso == null || cambio == null) return null;
        string selector = (ultimoPaso.Selector ?? "").Trim();
        string hasta = (cambio.Hasta ?? "").Trim();
        string desde = (cambio.Desde ?? "").Trim();
        if (desde.Length == 0) desde = (ultimoPaso.Superficie ?? "").Trim();
        if (selector.Length == 0 || hasta.Length == 0 || desde.Length == 0) return null;
        if (hasta.Equals(desde, StringComparison.OrdinalIgnoreCase)) return null;
        if (!Mundos.EsSap(hasta)) return null;

        long tardo = cambio.HoraMs - ultimoPaso.HoraMs;
        if (tardo < 0 || tardo > VentanaMs) return null;

        string origenDelPaso = (ultimoPaso.Superficie ?? "").Trim();
        if (origenDelPaso.Length > 0 && !origenDelPaso.Equals(desde, StringComparison.OrdinalIgnoreCase)) return null;

        return (desde, selector, hasta);
    }
}
