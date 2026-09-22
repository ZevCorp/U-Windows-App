namespace U.WindowsClient.Ui;

/// <summary>
/// CUÁNTO MIDE EL NOTCH, y por qué mide siempre lo mismo. Promesa 249 (spec 027, ajustada en la 028).
/// Pura.
/// </summary>
/// <remarks>El alto se calcula de las partes fijas y no del contenido: diga lo que diga, mide 62.</remarks>
public static class MedidaDelNotch
{
    /// <summary>Lo que mide de ancho, fijo. Antes lo decidía la frase más larga que hubiera pasado.</summary>
    public const double Ancho = 340;

    /// <summary>Lo que mide de alto, fijo: una línea, un icono y su aire.</summary>
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

    /// <summary>El target táctil/ratón del botón de mensajes.</summary>
    public const double CajaDelChat = 32;

    /// <summary>Separación entre la frase y el botón de mensajes.</summary>
    public const double AireDelChat = 6;

    /// <summary>El notch expandido conserva el mismo borde superior y crece hacia abajo.</summary>
    public const double AnchoDelChat = 420;
    public const double AltoDelChat = 360;

    /// <summary>El tamaño del único texto visible.</summary>
    public const double LetraDeLaTarea = 14;

    /// <summary>Escala secundaria.</summary>
    public const double LetraDelPaso = 11.5;

    /// <summary>Lo que le queda al texto después del icono y el aire.</summary>
    public static double AnchoDelTexto => Ancho - AireIzquierda - AireDerecha - CajaDelIcono - AireDelIcono - AireDelChat - CajaDelChat;

    /// <summary>
    /// El alto de la pieza. NO depende de lo que tenga dentro: ese es justo el punto de la promesa 249.
    /// El parámetro existe para poder preguntárselo con cualquier número y comprobar que contesta lo
    /// mismo.
    /// </summary>
    public static double AltoDe(int lineas) => Alto;
}
