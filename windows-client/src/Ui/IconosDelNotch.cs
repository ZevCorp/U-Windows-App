namespace U.WindowsClient.Ui;

/// <summary>
/// EL JUEGO DE ICONOS DEL NOTCH: uno por estado, todos del mismo trazo y la misma caja.
/// Promesa 253 (spec 028). Datos, no dibujo: aquí solo viven los trazos.
/// </summary>
/// <remarks>
/// NINGUNO ES UNA LETRA NI UN EMOJI, y no es purismo. Un emoji trae su propio color —rompe la paleta
/// en blanco y negro de la 242—, su propia métrica y su propio peso óptico, así que se lee como un
/// adorno pegado encima en vez de como parte de la pieza. Y una letra dentro de un círculo se lee
/// como una abreviatura, no como un estado.
///
/// LO QUE HACE QUE UN JUEGO DE ICONOS SE VEA DE UNA SOLA MANO son tres cosas, y las tres se cumplen
/// aquí: la misma caja (24), el mismo grosor de trazo (1,6) y la misma familia de formas —monolínea,
/// extremos redondeados, nada relleno—. Es la receta de los símbolos del sistema de Apple, y es por
/// lo que se reconocen como un conjunto aunque cada uno diga algo distinto.
///
/// Se guardan como trazos de vector en texto para que el contrato los pueda juzgar sin levantar una
/// interfaz: qué dibujo tiene cada estado es una decisión, y las decisiones se prueban.
/// </remarks>
public static class IconosDelNotch
{
    /// <summary>La caja en la que está dibujado cada trazo. Todos la misma, o pesan distinto.</summary>
    public const double Caja = 24;

    /// <summary>El grosor del trazo. Uno solo para todo el juego.</summary>
    public const double Grosor = 1.6;

    /// <summary>
    /// EN CURSO: un aro con un hueco, que gira. Es el gesto universal de «esto sigue», y girando no
    /// hace falta ningún texto que lo diga. Tres cuartos de vuelta: con menos parece un aro roto.
    /// </summary>
    private const string Girando = "M 12 3.2 A 8.8 8.8 0 1 1 3.2 12";

    /// <summary>HECHO: el visto, a secas. Sin aro alrededor: lo que terminó no necesita marco.</summary>
    private const string Visto = "M 4.5 12.6 L 9.8 17.6 L 19.5 6.8";

    /// <summary>
    /// FALLO: una admiración dentro de un aro. El aro cerrado es lo que lo separa del visto de un
    /// vistazo —uno abierto y otro cerrado—, sin recurrir al rojo, que aquí no existe.
    /// </summary>
    private const string Admiracion = "M 12 2.6 A 9.4 9.4 0 1 1 11.99 2.6 Z M 12 7.4 L 12 13.4 M 12 16.9 L 12 17.5";

    /// <summary>
    /// VOZ: cuatro barras de distinta altura, centradas. Es la onda de sonido reducida a lo mínimo:
    /// se lee como voz a 20 px, que es donde un micrófono dibujado se convierte en una mancha.
    /// </summary>
    private const string Onda = "M 5.5 10 L 5.5 14 M 10 6.5 L 10 17.5 M 14 8.5 L 14 15.5 M 18.5 11 L 18.5 13";

    /// <summary>
    /// OMITIDO: una raya dentro de un aro. Ni se hizo ni fallo: se quedo sin desenlace, y una raya es
    /// justo eso, un hueco donde iba una respuesta.
    /// </summary>
    private const string Guion = "M 12 2.6 A 9.4 9.4 0 1 1 11.99 2.6 Z M 8 12 L 16 12";

    /// <summary>El trazo que le toca a este estado.</summary>
    public static string De(EstadoDelNotch estado) => estado switch
    {
        EstadoDelNotch.EnCurso => Girando,
        EstadoDelNotch.Hecho => Visto,
        EstadoDelNotch.Fallo => Admiracion,
        EstadoDelNotch.Omitido => Guion,
        _ => Onda,
    };

    /// <summary>¿Este icono gira? Solo el de «en curso», y es la mitad de lo que dice.</summary>
    public static bool Gira(EstadoDelNotch estado) => estado == EstadoDelNotch.EnCurso;
}
