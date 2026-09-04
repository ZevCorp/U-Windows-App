using Omi;

namespace U.WindowsClient.Voice;

/// <summary>
/// DE DÓNDE ENTRA EL AUDIO, PARA TODA LA APP. Una sola elección. Promesa 146.
/// </summary>
/// <remarks>
/// LO QUE PASABA, y no era el bug de nadie: nunca se cableó. <c>ConsultaWindow</c> tiene su propio
/// <c>LiveAudio</c> y su propio <c>Omi.Selector</c> —los dos <c>private readonly … = new()</c>— y la
/// carita tiene otro <c>LiveAudio</c> y ningún selector. Elegir «collar por teléfono» en la ventana
/// de la consulta abría ese caño en la instancia de la consulta y en ninguna otra: al pulsar 🎓 la
/// voz volvía al micrófono del portátil sin decir nada, y el médico enseñaba hablándole al aire.
///
/// El dueño lo dijo corto y así se implementa: «solo necesito que el audio entre por el micrófono
/// Omi si está conectado». **Un aparato, una elección, y quien la cambia la cambia para todos.**
///
/// LO QUE VIVE AQUÍ ES LA ELECCIÓN, NO EL AUDIO. Cada <c>LiveAudio</c> sigue abriendo su propio
/// caño —no se puede compartir un micrófono entre dos consumidores sin duplicar muestras—; lo que
/// se comparte es QUÉ caño hay que abrir. Por eso esto es una decisión, y por eso es puro: el
/// contrato lo juzga sin micrófono, sin collar y sin teléfono.
///
/// Y AVISA CUANDO CAMBIA. Quien elige no sabe quién más tiene el micrófono abierto en ese momento,
/// así que no puede ir avisando uno a uno: se anuncia el cambio y cada quien se rehace. Sin el
/// aviso, el que ya estaba escuchando se queda con la fuente de antes y nadie se entera — que es
/// exactamente la avería que esto viene a cerrar, con otra cara.
/// </remarks>
public static class ElMicrofonoDeLaApp
{
    /// <summary>Qué hay que hacerle al captador para que suene lo elegido.</summary>
    public enum QueHacer
    {
        /// <summary>Ya está sonando lo que toca. NO se toca nada.</summary>
        Nada,
        AbrirCollar,
        AbrirTelefono,
        VolverAlLocal,
    }

    private static readonly object _candado = new();
    private static Origen _preferida = Origen.MicrofonoDelPc;
    private static string _codigo = "";

    /// <summary>Lo elegido, legible desde cualquier parte de la app.</summary>
    public static Origen Preferida { get { lock (_candado) return _preferida; } }

    /// <summary>El código de emparejamiento del teléfono, cuando la fuente es el teléfono.</summary>
    /// <remarks>
    /// Viaja con la elección porque sin él la carita NO PUEDE abrir el caño del teléfono: el enlace
    /// se prepara en la ventana de la consulta y hasta hoy el código se quedaba allí. Una elección
    /// que otro no puede cumplir no es una elección compartida.
    /// </remarks>
    public static string Codigo { get { lock (_candado) return _codigo; } }

    /// <summary>Se cambió de dónde entra el audio. Quien tenga el micrófono abierto, que se rehaga.</summary>
    public static event Action? Cambio;

    /// <summary>
    /// Elegir de dónde entra el audio para toda la app.
    /// </summary>
    /// <remarks>
    /// ELEGIR LO MISMO NO AVISA. Un aviso sin cambio haría que todo el mundo rehiciera su micrófono
    /// para nada — y rehacer un caño que ya suena corta la voz en curso, que es justo lo que
    /// <see cref="QueHacer.Nada"/> existe para impedir un peldaño más abajo.
    /// </remarks>
    public static void Elegir(Origen origen, string codigo = "")
    {
        bool cambió;
        lock (_candado)
        {
            cambió = _preferida != origen || (_codigo ?? "") != (codigo ?? "");
            _preferida = origen;
            _codigo = codigo ?? "";
        }
        if (cambió) Cambio?.Invoke();
    }

    /// <summary>
    /// Qué hay que hacerle a un captador que ahora mismo está así, para que suene lo elegido.
    /// </summary>
    /// <remarks>
    /// PEDIR LO QUE YA ESTÁ PUESTO NO HACE NADA, y es la regla que más protege: reabrir el caño que
    /// ya suena corta la conversación en curso. Quien vuelve a elegir lo que ya tenía no está
    /// pidiendo que se le corte — es la misma razón por la que <c>LiveAudio.PasarAlCollar</c> no
    /// toca nada si ya hay collar.
    ///
    /// UN COLLAR HABLA CON UN APARATO. Elegir el teléfono con el collar enlazado por Bluetooth a
    /// este PC no es «añadir una fuente»: es cambiar de caño, y hay que soltar el Bluetooth o el
    /// collar seguiría oyéndose por el otro camino.
    /// </remarks>
    public static QueHacer LoQueToca(Origen elegido, bool yaPorCollar, bool yaPorTelefono) => elegido switch
    {
        Origen.CollarPorBluetooth => yaPorCollar ? QueHacer.Nada : QueHacer.AbrirCollar,
        Origen.CollarPorTelefono => yaPorTelefono ? QueHacer.Nada : QueHacer.AbrirTelefono,
        _ => yaPorCollar || yaPorTelefono ? QueHacer.VolverAlLocal : QueHacer.Nada,
    };

    /// <summary>Cómo se dice en voz alta de dónde está entrando el audio.</summary>
    public static string ComoSeLlama(Origen origen) => origen switch
    {
        Origen.CollarPorBluetooth => "el collar por Bluetooth",
        Origen.CollarPorTelefono => "el collar por el teléfono",
        _ => "el micrófono del computador",
    };
}
