namespace U.WindowsClient.Ui;

/// <summary>
/// CUÁNTO MIDE EL NOTCH, y por qué mide siempre lo mismo. Promesa 249 (spec 027, ajustada en la 028).
/// Pura.
/// </summary>
/// <remarks>
/// LA PIEZA RESPIRABA. La ventana se medía por su contenido, así que una frase larga la ensanchaba y
/// una línea nueva la estiraba: con las diez líneas que guardaba llegaba a 252 de alto. El dueño, el
/// 2026-09-16: «que el tamaño no cambie y no tenga un tamaño muy grande en un momento y pequeño en
/// otro, sino un tamaño muy compacto».
///
/// AHORA SON DOS LÍNEAS FIJAS Y UN ICONO, con la proporción del diseño que pasó el dueño: la pieza
/// mide unas cinco veces y media su alto, el icono ocupa algo menos de la mitad de ese alto, y la
/// tarea pesa bastante más que el paso. Eso último es lo que hace que se lea de un vistazo: dos
/// PESOS distintos, no dos tamaños parecidos.
///
/// El alto se calcula de sus partes y NO del contenido: diga lo que diga, mide 62.
/// </remarks>
public static class MedidaDelNotch
{
    /// <summary>Lo que mide de ancho, fijo. Antes lo decidía la frase más larga que hubiera pasado.</summary>
    public const double Ancho = 340;

    /// <summary>Lo que mide de alto, fijo: dos líneas y su aire.</summary>
    public const double Alto = 62;

    /// <summary>El aire de la placa, arriba y abajo.</summary>
    public const double AireVertical = 11;

    /// <summary>El aire a la izquierda, antes del icono.</summary>
    public const double AireIzquierda = 16;

    /// <summary>El aire a la derecha, un punto más suelto para que el texto no muera en el borde.</summary>
    public const double AireDerecha = 18;

    /// <summary>La caja del icono.</summary>
    public const double CajaDelIcono = 26;

    /// <summary>El hueco entre el icono y el texto.</summary>
    public const double AireDelIcono = 13;

    /// <summary>El tamaño de la tarea, la línea de arriba.</summary>
    public const double LetraDeLaTarea = 14;

    /// <summary>El tamaño de lo que pasa ahora, la de abajo.</summary>
    public const double LetraDelPaso = 11.5;

    /// <summary>Lo que le queda al texto después del icono y el aire.</summary>
    public static double AnchoDelTexto => Ancho - AireIzquierda - AireDerecha - CajaDelIcono - AireDelIcono;

    /// <summary>
    /// El alto de la pieza. NO depende de lo que tenga dentro: ese es justo el punto de la promesa 249.
    /// El parámetro existe para poder preguntárselo con cualquier número y comprobar que contesta lo
    /// mismo.
    /// </summary>
    public static double AltoDe(int lineas) => Alto;
}
