using System;

namespace U.WindowsClient.Decision;

/// <summary>
/// QUÉ MEJORA INSISTIENDO Y QUÉ NO. Promesa 283 (spec 035).
/// </summary>
/// <remarks>
/// LOS CUATRO ERRORES QUE DOCUMENTA TYPESAFE se parten en dos grupos, y la línea es si el mismo
/// cuerpo con la misma clave podría salir bien más tarde:
///
/// | Código | Qué es | ¿Se reintenta? |
/// |---|---|---|
/// | 429 | pasado de cupo | sí — TypeSafe pide esperar, no rendirse |
/// | 529 | sobrecargado | sí — es suyo, y pasa |
/// | 401 | clave mala o ausente | NO — la segunda vez es igual de mala |
/// | 422 | cuerpo inválido | NO — el mismo cuerpo sale igual de inválido |
///
/// Y NO ES UNA SUTILEZA: la documentación de TypeSafe avisa de que los límites (hoy 1.200
/// peticiones por minuto) «can change without notice» mientras sirven la demanda del lanzamiento.
/// Reintentar un 401 en un bucle de pasos gasta ese cupo para no decidir nada, y deja sin
/// peticiones a lo que sí podría funcionar.
///
/// La espera dobla desde 200 ms y se detiene en ~6,4 s, porque por encima de eso ya perdió contra
/// el plazo del decisor (2.000 ms por paso) y lo honesto es devolverle el turno a Luna.
/// </remarks>
public static class PoliticaDeReintento
{
    /// <summary>Cuántas veces se intenta en total, contando la primera.</summary>
    public const int IntentosMaximos = 3;

    /// <summary>¿Este código HTTP puede salir distinto si se vuelve a intentar?</summary>
    public static bool SeReintenta(int codigoHttp) => codigoHttp == 429 || codigoHttp == 529;

    /// <summary>Cuánto esperar antes del intento número <paramref name="intento"/> (0 es el primer reintento).</summary>
    public static int EsperaMs(int intento)
    {
        int n = intento < 0 ? 0 : (intento > 5 ? 5 : intento);
        return 200 * (1 << n);
    }
}
