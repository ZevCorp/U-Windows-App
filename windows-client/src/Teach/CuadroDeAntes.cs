namespace U.WindowsClient.Teach;

/// <summary>Un cuadro de la cámara de la demo: cuándo se tomó, dónde quedó y a qué se parece.</summary>
/// <param name="HoraMs">Hora del reloj de la demo en que EMPEZÓ a copiarse la pantalla.</param>
/// <param name="Ruta">Dónde quedó el png, o vacío mientras vive solo en memoria.</param>
/// <param name="Huella">Un resumen barato de la imagen (16×16 en gris). Dos cuadros con la misma
/// huella son la misma pantalla para lo que aquí importa: saber si se asentó.</param>
public sealed record Cuadro(long HoraMs, string Ruta, ulong Huella);

/// <summary>
/// QUÉ CUADRO ES EL DE ANTES DE UN CLIC. Promesa 168 (spec 012).
/// </summary>
/// <remarks>
/// LA CARRERA QUE ESTO MATA, dicha por el dueño: «que los screenshots se tomen cuando ya un
/// elemento cambió por el clic». El pantallazo por paso (<c>StepShotCamera</c>) se dispara al
/// OBSERVAR el paso — después del clic y de su efecto — y el modelo acaba describiendo el botón
/// que apareció en vez del que se pulsó. Nadie lo ve mal en el disco: se ve mal tres pasos después.
///
/// LA SALIDA NO ES DISPARAR MÁS RÁPIDO. Es no disparar: la cámara copia la pantalla todo el tiempo
/// en un anillo, y cuando el ratón baja se ELIGE del pasado. Elegir del pasado es una función sobre
/// horas, y por eso es pura y por eso el contrato puede juzgar el caso exacto: «el clic fue en t;
/// ningún cuadro con hora ≥ t puede ser el de antes».
///
/// EL MARGEN existe porque la hora de un cuadro es la de EMPEZAR a copiarlo, y copiar 1920×1080
/// tarda unos milisegundos: un cuadro que empezó 20 ms antes del clic es de antes con seguridad; uno
/// que empezó en el mismo milisegundo, no se puede demostrar. El margen solo puede llevar hacia
/// atrás — nunca acerca al clic.
/// </remarks>
public static class CuadroDeAntes
{
    /// <summary>Cuánto antes del clic tiene que haber empezado un cuadro para valer como «antes».</summary>
    /// <remarks>
    /// 100 ms: más que lo que tarda un BitBlt de pantalla completa en esta máquina (medido en
    /// decenas de ms) y menos que el intervalo del anillo (250 ms), para que casi siempre haya un
    /// cuadro que lo cumpla sin retroceder dos.
    /// </remarks>
    public const int MargenMs = 100;

    /// <summary>
    /// El cuadro más nuevo con hora ≤ hora del clic − margen, o null si no hay ninguno.
    /// </summary>
    /// <remarks>
    /// NULL Y NO «EL MÁS CERCANO». Devolver el primero de después cuando no hay ninguno de antes
    /// sería exactamente la mentira que esto viene a cerrar, con la firma de la función encima.
    /// </remarks>
    public static Cuadro? Elegir(IReadOnlyList<Cuadro> cuadros, long horaDelClicMs, int margenMs = MargenMs)
    {
        if (cuadros == null) return null;
        long limite = horaDelClicMs - Math.Max(0, margenMs);
        Cuadro? elegido = null;
        foreach (var c in cuadros)
        {
            if (c.HoraMs > limite) continue;
            if (elegido == null || c.HoraMs > elegido.HoraMs) elegido = c;
        }
        return elegido;
    }
}
