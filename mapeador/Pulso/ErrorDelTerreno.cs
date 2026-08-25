namespace Mapeador;

/// <summary>
/// DÓNDE SE ROMPIÓ EL TERRENO. Un hecho con nombre, no una línea de texto.
/// </summary>
/// <remarks>
/// EL TERRENO ES EL GRAFO, y detectarlo mal es el fallo que importa: un camino que no se aprendió,
/// un nodo que no era real, una pantalla que nadie llegó a mirar. Todos estos ya se detectaban —el
/// código los canta desde hace semanas— pero como texto suelto en el log, y con texto suelto no se
/// puede contestar la pregunta que de verdad se quiere contestar: <em>¿cuál de estos falla más, y en
/// qué aplicaciones?</em> Para eso hace falta que sean hechos contables.
///
/// SE ESCRIBE ANTES QUE EL VÍDEO, y a propósito. Los médicos de urgencias van a usar esto en sus
/// equipos, y grabar la pantalla 24 h tiene decisiones que no toma un programador. Esto no las
/// necesita: cuenta qué se rompe y dónde sin grabar un solo fotograma, y cuando llegue el vídeo, ya
/// hay una lista de instantes que recortar.
///
/// PURO Y SIN PANTALLA, como el resto de este proyecto: se le pasa lo que pasó y contesta. Así se
/// puede juzgar en la nube, en cada PR, sin abrir una ventana.
/// </remarks>
public enum QueSeRompio
{
    /// <summary>
    /// SE CAMBIÓ DE PANTALLA Y NINGÚN CLIC LO EXPLICA. Es el error más grave de todos: la arista NO
    /// se creó, así que ese camino no existe para siempre — el grafo sabe que hay dos sitios y no
    /// sabe cómo se va de uno al otro.
    /// </summary>
    AristaNoCreada,

    /// <summary>
    /// El elemento sobre el que se hizo clic no se reconoció: no está en lo que el núcleo conoce de
    /// esta pantalla. Se pierde la atribución, y con ella la arista.
    /// </summary>
    ElementoNoReconocido,

    /// <summary>
    /// Se cruzó una pantalla tan rápido que el clic guardado ya no explica el viaje. El nodo queda
    /// registrado pero suelto, sin cómo se entra ni cómo se sale.
    /// </summary>
    PasoDeLargo,

    /// <summary>
    /// Una dirección cambió sola —una web que reescribe su URL— así que el nodo puede no
    /// corresponder a ninguna pantalla real. Un nodo falso ensucia el mapa más que la ausencia de
    /// uno: manda a buscar algo que no existe.
    /// </summary>
    IdentidadTransitoria,

    /// <summary>
    /// Se descartaron lecturas de pantalla por no dar abasto. No es una queja de rendimiento: es
    /// TERRENO QUE NADIE MIRÓ, y lo que no se miró no está en el grafo.
    /// </summary>
    TerrenoNoLeido,

    /// <summary>
    /// Se creía estar en una pantalla y se estaba en otra. El ancla lo frenó —hizo bien— pero que
    /// haga falta frenar significa que la ubicación se estaba siguiendo mal.
    /// </summary>
    UbicacionEquivocada,

    /// <summary>
    /// Se pidió pulsar algo por su nombre y no se encontró en la pantalla. O el nombre que se dijo no
    /// es el que se ve, o el elemento no se está leyendo.
    /// </summary>
    NoSeEncontroLoPedido,
}

/// <summary>
/// Cuánto duele. Sirve para ordenar por dónde empezar a arreglar, no para decidir si se anota: se
/// anota todo.
/// </summary>
public enum Gravedad
{
    /// <summary>El grafo quedó MAL: falta un camino o sobra un nodo falso. Es lo que hay que arreglar.</summary>
    RompeElGrafo,

    /// <summary>El grafo no se estropeó, pero algo no se pudo hacer y quedó terreno sin ver.</summary>
    SeQuedaCorto,
}

/// <summary>
/// Un error del terreno, con todo lo que hace falta para encontrarlo después en el vídeo.
/// </summary>
/// <param name="Que">Qué se rompió.</param>
/// <param name="Donde">La ubicación donde pasó, tal como la nombra el grafo.</param>
/// <param name="Detalle">En castellano y para quien lo va a leer: qué se esperaba y qué había.</param>
/// <param name="Cuando">
/// EN UTC Y CON EL INSTANTE EXACTO, porque es la clave con la que esto se cruza con el vídeo: el
/// segundo del clip es esta hora menos la hora en que empezó el trozo grabado. Toda la correlación
/// entre lo que se rompió y lo que se ve es esta resta.
/// </param>
public sealed record ErrorDelTerreno(QueSeRompio Que, string Donde, string Detalle, DateTime Cuando)
{
    /// <summary>De qué app es. Se saca de la ubicación, que ya lleva la app dentro.</summary>
    public string App => AppDe(Donde);

    /// <summary>
    /// Lo grave que es. Va aquí y no en quien lo crea para que dos sitios no puedan discrepar sobre
    /// el mismo tipo de error — es una propiedad del error, no una opinión de quien lo detecta.
    /// </summary>
    public Gravedad Cuanto => Que switch
    {
        // Estos tres dejan el grafo mal: falta un camino o hay un nodo que no corresponde a nada.
        QueSeRompio.AristaNoCreada or QueSeRompio.ElementoNoReconocido
            or QueSeRompio.IdentidadTransitoria => Gravedad.RompeElGrafo,

        // «Pasó de largo» deja el nodo suelto pero no acuña nada falso; los demás son cosas que no
        // se pudieron hacer. Duelen menos porque se arreglan solas la próxima vez que se pase.
        _ => Gravedad.SeQuedaCorto,
    };

    /// <summary>Una línea para el log, con la forma de siempre.</summary>
    public string Linea() => $"{Que} en «{Donde}»: {Detalle}";

    /// <summary>
    /// La app de una ubicación. Copiada del núcleo a propósito y no compartida: el mapeador no
    /// depende del núcleo —esa dependencia va en una dirección y no se invierte— y son cuatro
    /// líneas. Si algún día divergen, lo dirá el contrato de los dos.
    /// </summary>
    private static string AppDe(string ubicacion)
    {
        if (string.IsNullOrWhiteSpace(ubicacion)) return "";
        int dosPuntos = ubicacion.IndexOf("://", StringComparison.Ordinal);
        if (dosPuntos < 0) return "";
        int barra = ubicacion.IndexOf('/', dosPuntos + 3);
        return barra < 0 ? ubicacion[(dosPuntos + 3)..] : ubicacion[(dosPuntos + 3)..barra];
    }
}
