namespace U.WindowsClient.Ui;

/// <summary>En qué punto está una línea del notch. Lo que decide cómo se dibuja su marca.</summary>
public enum EstadoDelNotch
{
    /// <summary>Empezó y todavía no se sabe cómo acaba.</summary>
    EnCurso,
    /// <summary>Salió bien.</summary>
    Hecho,
    /// <summary>Salió mal.</summary>
    Fallo,
    /// <summary>Se quedó sin desenlace: empezó otra encima.</summary>
    Omitido,
    /// <summary>Alguien está hablando: la persona o Ü (spec 028). No es un paso, es una frase.</summary>
    Voz,
}

/// <summary>
/// EL NOTCH ES BLANCO Y NEGRO. Promesa 242 (spec 023). Pura: solo números, ningún pincel.
/// </summary>
/// <remarks>
/// HEREDABA LA PALETA DE LA BARRA GRANDE —azul en curso, verde hecho, rojo fallo, y un blanco que
/// tiraba a azul para el texto— y son cuatro tonos en una pieza de dos centímetros que vive encima
/// de todo lo que la persona hace. Allí el color separa zonas y tiene sentido; aquí solo hace ruido.
/// El dueño, el 2026-09-15, antes de mandarle la app a un usuario: «que sea extremadamente limpio,
/// mucho negro y blanco, sin más colores que dañen la estética para el notch».
///
/// QUITAR EL COLOR NO PUEDE COSTAR INFORMACIÓN, y por eso lo que decía el tono pasa a decirlo la
/// FORMA y la LUZ: el fallo es un aro hueco en vez de un punto lleno, lo que está en curso late, lo
/// omitido baja de luz. Un estado que solo se distinguiera por ser rojo dejaría de distinguirse.
///
/// Y ESTÁ AQUÍ, EN SU PROPIA PALETA, para que no se deshaga solo. Quien añada un estado dentro de
/// tres semanas no va a leer esta conversación: va a mirar cómo se distinguen los que hay. Con la
/// paleta aparte y una promesa que exige los tres canales iguales, añadir un tono rompe el contrato
/// y se ve; con `UiPalette` a mano, no.
///
/// Se guardan como ARGB en un entero y no como <c>Color</c> a propósito: así esta pieza no depende
/// de WPF y el contrato la puede juzgar sin levantar una interfaz.
/// </remarks>
public static class PaletaDelNotch
{
    /// <summary>El fondo de la pieza. Negro de verdad, casi opaco.</summary>
    /// <remarks>
    /// Era <c>#0A0C12</c>, negro con una gota de azul, para que sobre un escritorio claro no se
    /// leyera como un agujero. Lo que lo despega del fondo es el filete de luz de un píxel, no esa
    /// gota, así que el negro puro se sostiene igual y es lo que se pidió.
    /// </remarks>
    public const uint Fondo = 0xF5000000;

    /// <summary>El canto iluminado de un píxel. A este alfa no se lee como marco, sino como luz.</summary>
    public const uint Filete = 0x24FFFFFF;

    /// <summary>El texto de una línea viva.</summary>
    public const uint Tinta = 0xF0FFFFFF;

    /// <summary>El texto de una línea que se quedó sin desenlace.</summary>
    public const uint TintaApagada = 0x80FFFFFF;

    /// <summary>
    /// La segunda línea: lo que pasa ahora. Más tenue que la tarea, para que las dos se lean como una
    /// jerarquía y no como dos frases sueltas, pero lejos del apagado de lo que ya no cuenta.
    /// </summary>
    public const uint TintaSecundaria = 0xC0FFFFFF;

    /// <summary>La marca «Ü» cuando habla ella.</summary>
    public const uint Suya = 0xF0FFFFFF;

    /// <summary>La marca «Tú» cuando hablas tú. Se separa por luz, que es lo que hacía el color.</summary>
    public const uint Tuya = 0x8CFFFFFF;

    /// <summary>El punto de una acción.</summary>
    public const uint Punto = 0xFFFFFFFF;

    /// <summary>El punto de lo que se quedó sin hacer.</summary>
    public const uint PuntoApagado = 0x66FFFFFF;

    /// <summary>De qué color va la marca de este estado. Todos son blanco o gris; lo dice el alfa.</summary>
    public static uint DelPunto(EstadoDelNotch estado)
        => estado == EstadoDelNotch.Omitido ? PuntoApagado : Punto;

    /// <summary>El texto de este estado.</summary>
    public static uint DeLaTinta(EstadoDelNotch estado)
        => estado == EstadoDelNotch.Omitido ? TintaApagada : Tinta;

    /// <summary>
    /// ¿La marca es un ARO en vez de un punto lleno? Solo el fallo, y es lo que sustituye al rojo:
    /// algo que se quedó abierto se lee abierto.
    /// </summary>
    public static bool EsAro(EstadoDelNotch estado) => estado == EstadoDelNotch.Fallo;

    /// <summary>¿La marca respira? Solo mientras la acción sigue en curso.</summary>
    public static bool Late(EstadoDelNotch estado) => estado == EstadoDelNotch.EnCurso;
}
