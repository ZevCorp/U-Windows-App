namespace U.WindowsClient.Ui;

/// <summary>
/// CUÁNTO MIDE EL NOTCH, y por qué mide siempre lo mismo. Promesa 249 (spec 027). Pura.
/// </summary>
/// <remarks>
/// LA PIEZA RESPIRABA. La ventana se medía por su contenido (<c>SizeToContent</c>), así que una frase
/// larga la ensanchaba, una línea nueva la estiraba y una que caducaba la encogía. Guardando diez
/// líneas llegaba a 252 de alto. El dueño, el 2026-09-16: «que el tamaño no cambie y no tenga un
/// tamaño muy grande en un momento y pequeño en otro, sino un tamaño muy compacto».
///
/// Una pieza que cambia de tamaño encima del trabajo de alguien se lee como un sobresalto; una que
/// no cambia se lee como parte del sistema. Por eso el alto se calcula de sus partes y NO del
/// contenido: tres franjas iguales más el aire de la placa, haya cero líneas o diez.
///
/// TRES LÍNEAS son las justas para leer una secuencia —lo que pediste, lo que está pasando, lo que
/// salió—. Diez era un registro, y el registro ya vive en el log.
/// </remarks>
public static class MedidaDelNotch
{
    /// <summary>Cuántas líneas se ven a la vez. Las demás se van por arriba.</summary>
    public const double FilasALaVista = 3;

    /// <summary>Lo que ocupa una línea, pase lo que pase: el ritmo vertical es constante.</summary>
    public const double AltoDeFila = 18;

    /// <summary>El aire de la placa, arriba y abajo. Era 11; con una pieza de tres líneas, 7 basta.</summary>
    public const double AireVertical = 7;

    /// <summary>El aire a la izquierda, antes de la marca.</summary>
    public const double AireIzquierda = 14;

    /// <summary>El aire a la derecha, que va un punto más suelto para que el texto no muera en el borde.</summary>
    public const double AireDerecha = 16;

    /// <summary>Lo que mide de ancho, fijo. Antes lo decidía la frase más larga que hubiera pasado.</summary>
    public const double Ancho = 340;

    /// <summary>La columna de la marca: el punto de estado, o «Ü» y «Tú».</summary>
    public const double ColumnaDeLaMarca = 18;

    /// <summary>El punto de una acción. En una pieza de 68 de alto, 7 pesaba demasiado.</summary>
    public const double Punto = 6;

    /// <summary>El alto de la pieza. NO depende del número de líneas: ese es el punto.</summary>
    public static double Alto(int filas) => FilasALaVista * AltoDeFila + 2 * AireVertical;

    /// <summary>Lo que le queda al texto después de la marca y el aire.</summary>
    public static double AnchoDelTexto => Ancho - AireIzquierda - AireDerecha - ColumnaDeLaMarca;

    /// <summary>
    /// Cuánta luz tiene una línea según lo vieja que sea: la última entera, y las de atrás en dos
    /// peldaños. Con tres líneas la diferencia tiene que leerse de un vistazo, así que los peldaños
    /// son grandes; con diez, la escalera de 0,13 no distinguía nada.
    /// </summary>
    public static double Luz(int desdeElFinal) => desdeElFinal switch
    {
        <= 0 => 1.0,
        1 => 0.55,
        _ => 0.38,
    };
}
