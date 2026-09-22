using System;

namespace U.WindowsClient.Decision;

/// <summary>
/// QUÉ SUPERFICIES PUEDEN MANDAR SU TEXTO A JEV. Promesa 393 (spec 046).
/// </summary>
/// <remarks>
/// NACE EN LA FASE 4 CON SOLO EL VALOR POR DEFECTO, y no es un hueco olvidado: <c>ElegirConModelo</c> (392) la
/// recibe como octavo argumento para que la firma no cambie dos veces, y el contrato la pide por reflexión
/// (<c>PorDefecto</c>) para poder delegar desde <c>Elegir</c>. Quién puede viajar y quién no —<c>Leer</c>,
/// <c>PuedeViajar</c>, <c>VetadosPorDefecto</c>— lo escribe la fase 6, con su promesa en rojo delante; hasta
/// entonces NADIE la consulta, y el decisor se comporta como el día antes de que existiera (2026-09-22).
/// </remarks>
public sealed class PoliticaDeLoQueViaja
{
    private PoliticaDeLoQueViaja() { }

    /// <summary>La política sin variables de entorno: la que usa <c>Elegir</c> de seis argumentos.</summary>
    public static PoliticaDeLoQueViaja PorDefecto { get; } = new PoliticaDeLoQueViaja();
}