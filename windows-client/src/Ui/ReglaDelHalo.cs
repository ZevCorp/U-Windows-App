using System;

namespace U.WindowsClient.Ui;

/// <summary>
/// CUÁNTO CRECE Y DE QUÉ COLOR es el halo de la voz que rodea a la carita suelta. Separada de la
/// ventana para que el contrato la juzgue sin abrir una pantalla (promesa 163, spec 011).
/// </summary>
/// <remarks>
/// Hasta el 2026-09-02 este halo vivía DENTRO de la pastilla del micrófono y medía 26×26. Al morir
/// las pastillas (promesa 162) la señal de «te estoy oyendo» pasa a ser la carita misma, y con eso
/// el halo hereda un límite que antes no tenía: la ventana de la carita suelta mide exactamente
/// <see cref="CaritaPx"/> + 2·<see cref="AirePx"/>, y no hay un píxel más. Un halo que se pase de
/// ahí no se ve grande — se ve CORTADO contra un borde que es un círculo con una esquina, que es la
/// misma clase de fallo que la sombra recortada de la promesa 154.
///
/// Por eso el tope está calculado y no elegido a ojo, y por eso lo comprueba el contrato barriendo
/// el rango entero de voz: una constante que hoy cabe deja de caber en cuanto alguien toque el
/// tamaño de la carita, y el día que eso pase el juez lo dice antes que la pantalla.
/// </remarks>
public static class ReglaDelHalo
{
    /// <summary>El diámetro de la carita suelta (<c>CollapsedFace</c> en el XAML).</summary>
    public const double CaritaPx = 72;

    /// <summary>El aire alrededor (el <c>Margin</c> de <c>CollapsedGroup</c>): todo lo que hay.</summary>
    public const double AirePx = 28;

    /// <summary>Lo que mide la ventana de la carita suelta, y por tanto el techo del halo.</summary>
    public static double VentanaPx => CaritaPx + 2 * AirePx;

    /// <summary>
    /// Cuánto se agranda el halo. Al hablar late con la voz; callada, un latido lento que solo dice
    /// «sigo aquí» — porque un halo quieto y un micrófono cerrado se ven igual.
    /// </summary>
    /// <param name="nivelVoz">El nivel que mueve la boca, para que lo que se ve pulsar sea
    /// exactamente lo que se está oyendo y no una animación con vida propia.</param>
    /// <param name="pasoDeLaBoca">El contador de la boca, que da la fase del latido en reposo.</param>
    public static double Escala(double nivelVoz, int pasoDeLaBoca)
    {
        // 1,12 + 0,43 topa en 1,55: 72·1,55 = 111,6 y la ventana mide 128. El margen que sobra no
        // es desperdicio — es el sitio de la sombra de la carita (blur 24, promesa 154).
        return 1.12 + Fuerza(nivelVoz, pasoDeLaBoca) * 0.43;
    }

    /// <summary>
    /// Lo opaco que queda: se ve que hay voz sin taparla.
    /// </summary>
    /// <remarks>
    /// SUBIDO EL 2026-09-06 a petición del dueño —«que sea más claro para poder verlo»—: iba de
    /// 0,12 a 0,38 y contra un escritorio claro no se distinguía de la sombra de la carita. Ahora va
    /// de 0,26 a 0,68, que es donde se lee sin llegar a competir con la cara. Sigue siendo
    /// translúcido: el halo dice que hay voz, no tapa a quien la pone.
    /// </remarks>
    public static double Opacidad(double nivelVoz, int pasoDeLaBoca)
        => 0.26 + Fuerza(nivelVoz, pasoDeLaBoca) * 0.42;

    /// <summary>
    /// De qué color: azul si te oye el collar, gris si te oye el micrófono del computador. No es
    /// decoración — es la promesa 157 dicha en un sitio donde se ve sin abrir nada: lo que la
    /// interfaz dice que te oye tiene que ser lo que te oye.
    /// </summary>
    public static System.Windows.Media.Color Color(bool porElCollar)
        => porElCollar
            ? System.Windows.Media.Color.FromRgb(0x3E, 0x9B, 0xFF)
            : System.Windows.Media.Color.FromRgb(0xA8, 0xA8, 0xAE);

    /// <summary>
    /// Cuánta señal hay ahora, entre 0 y 1. El umbral de 0,004 separa «hay voz» de «hay sala»: por
    /// debajo, el ruido de fondo haría latir el halo como si alguien estuviera hablando.
    /// </summary>
    private static double Fuerza(double nivelVoz, int pasoDeLaBoca)
        => nivelVoz > 0.004
            ? Math.Min(1, Math.Pow(nivelVoz, 0.55) * 1.45)
            : 0.18 + 0.10 * Math.Sin(pasoDeLaBoca * 0.16);
}
