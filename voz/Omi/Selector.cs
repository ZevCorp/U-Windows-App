namespace Omi;

/// <summary>De dónde entra la voz. El orden es la prioridad: a mayor número, más cerca del oído.</summary>
public enum Origen
{
    /// <summary>El micrófono del portátil. Siempre está, y por eso es el suelo del que nunca se cae.</summary>
    MicrofonoDelPc = 0,

    /// <summary>El collar hablando por Bluetooth directamente con este PC.</summary>
    CollarPorBluetooth = 1,

    /// <summary>El collar hablando con el teléfono, y el teléfono con nosotros.</summary>
    CollarPorTelefono = 2,
}

/// <summary>
/// Quién manda cuando hay más de un micrófono disponible, y por qué.
///
/// Hasta hoy la app tenía dos fuentes y la elección vivía repartida en cinco banderas dentro de
/// <c>LiveAudio</c>. Con tres fuentes esa forma deja de sostenerse: cada pantalla que quiera pintar
/// «por dónde entra la voz» tendría que deducirlo por su cuenta, y ese es exactamente el fallo que
/// ya se pagó el 2026-08-14 —la carita pintando gris con el audio entrando por el collar—.
///
/// LA PRIORIDAD NO ES UN GUSTO, ES UNA MEDIDA. El collar por Bluetooth va primero porque su camino
/// es el más corto y el único que no depende de un tercero. El teléfono va segundo: el 2026-09-01 se
/// midió que entrega PCM16 de 16 kHz en tramas de 20 ms —tan bueno como el Bluetooth para oír— pero
/// añade un teléfono, una app ajena y una red. El micrófono del PC va último y no se quita nunca:
/// es el que garantiza que Ü nunca se quede sorda.
///
/// Sólo la DECISIÓN vive aquí. Abrir dispositivos, hablar WinRT o sostener un WebSocket es de
/// <c>windows-client</c>. Aquí se puede juzgar sin collar, sin teléfono y sin tarjeta de sonido;
/// allí no.
/// </summary>
public sealed class Selector
{
    /// <summary>
    /// Por qué manda la que manda. Vacío hasta la primera decisión.
    ///
    /// Se guarda el motivo y no sólo el resultado por lo mismo que en <see cref="Relevo"/>: un
    /// cambio de micrófono sin explicación es indistinguible de una avería, y se investiga como tal.
    /// </summary>
    public string Motivo { get; private set; } = "";

    /// <summary>La última decisión tomada. Antes de la primera, el micrófono del PC.</summary>
    public Origen Activa { get; private set; } = Origen.MicrofonoDelPc;

    /// <summary>Lo que el médico eligió a mano, o nada si nunca eligió.</summary>
    public Origen? Preferida { get; private set; }

    /// <summary>
    /// El médico elige una fuente, o suelta el volante pasando <c>null</c>.
    ///
    /// EL FALLO QUE OBLIGÓ A ESTO, medido el 2026-09-01 con el selector recién dibujado: con el
    /// collar conectado por Bluetooth, elegir «micrófono del PC» o «teléfono» no hacía nada. La
    /// elección se pintaba y al segundo siguiente la prioridad automática la deshacía. La puerta
    /// estaba cerrada por dentro y no había manera de salir.
    ///
    /// La regla, entonces, es asimétrica a propósito: <b>elegir apaga la automática, y sólo la
    /// ausencia de lo elegido la vuelve a encender.</b> Un selector en el que la automática pueda
    /// ganarle a una elección explícita no es un selector, es una sugerencia.
    /// </summary>
    public void Preferir(Origen? origen) => Preferida = origen;

    /// <param name="collarPorBluetooth">El collar está enlazado con ESTE PC y entregando.</param>
    /// <param name="collarPorTelefono">El teléfono está entregando audio del collar.</param>
    /// <param name="ahoraMs">Reloj, para poder juzgar esto sin esperar en tiempo real.</param>
    /// <returns>La fuente que manda a partir de ahora.</returns>
    public Origen Decidir(bool collarPorBluetooth, bool collarPorTelefono, long ahoraMs)
    {
        var antes = Activa;

        // LO ELEGIDO MANDA MIENTRAS EXISTA. Va lo primero, antes que cualquier prioridad: si la
        // automática pudiera colarse por delante, elegir no serviría de nada — que es exactamente
        // lo que pasaba antes de la promesa 30.
        if (Preferida is { } elegida && Disponible(elegida, collarPorBluetooth, collarPorTelefono))
        {
            Activa = elegida;
            Motivo = $"lo eligió el médico: {Nombre((int)elegida)}";
            UltimaDecisionMs = ahoraMs;
            return Activa;
        }

        // Y si lo elegido desapareció, no se puede quedar muda esperándolo: se cae a la mejor
        // disponible, diciéndolo. La elección NO se borra — cuando vuelva a estar, vuelve a mandar.
        var seCayoDeLoElegido = Preferida is { } falta && !Disponible(falta, collarPorBluetooth, collarPorTelefono);

        // UN COLLAR HABLA CON UN APARATO A LA VEZ, así que las dos primeras no pueden ser ciertas a
        // la vez en la práctica. Si el sistema dice que sí, algo está mintiendo: se prefiere el
        // camino corto y se deja escrito, en vez de elegir en silencio.
        if (collarPorBluetooth && collarPorTelefono)
        {
            Activa = Origen.CollarPorBluetooth;
            Motivo = "el collar dice estar por Bluetooth Y por el teléfono a la vez, que no puede ser: "
                   + "manda el Bluetooth por ser el camino corto, y esto queda anotado porque "
                   + "significa que uno de los dos estados está caducado";
        }
        else if (collarPorBluetooth)
        {
            Activa = Origen.CollarPorBluetooth;
            Motivo = "el collar está enlazado con este PC y entregando: manda el camino corto";
        }
        else if (collarPorTelefono)
        {
            Activa = Origen.CollarPorTelefono;
            Motivo = "el collar no está en este PC pero el teléfono está entregando su audio";
        }
        else
        {
            Activa = Origen.MicrofonoDelPc;
            Motivo = antes == Origen.MicrofonoDelPc
                ? "no hay collar por ningún camino: la voz entra por el micrófono del PC"
                : $"se perdió {Nombre((int)antes)}: la voz vuelve al micrófono del PC para que Ü no se quede sorda";
        }

        if (seCayoDeLoElegido)
            Motivo = $"{Nombre((int)Preferida!.Value)} no está disponible: se usa {Nombre((int)Activa)} mientras tanto";

        UltimaDecisionMs = ahoraMs;
        return Activa;
    }

    /// <summary>
    /// Si una fuente se puede usar ahora mismo. El micrófono del PC siempre puede: es el suelo, y
    /// que exista es justo lo que permite que elegirlo se respete siempre.
    /// </summary>
    private static bool Disponible(Origen origen, bool collarPorBluetooth, bool collarPorTelefono) => origen switch
    {
        Origen.CollarPorBluetooth => collarPorBluetooth,
        Origen.CollarPorTelefono => collarPorTelefono,
        _ => true,
    };

    /// <summary>Cuándo se decidió lo último. Sirve para no repintar la interfaz en cada trama.</summary>
    public long UltimaDecisionMs { get; private set; }

    /// <summary>
    /// Cómo se llama cada fuente cuando hay que decírselo a una persona.
    ///
    /// Los tres nombres tienen que ser DISTINTOS entre sí, y no es una obviedad: un selector que
    /// llame «collar» a las dos rutas del collar deja al usuario sin saber cuál está usando, que es
    /// justo la pregunta que el indicador existe para contestar.
    /// </summary>
    public static string Nombre(int fuente) => fuente switch
    {
        (int)Origen.MicrofonoDelPc => "Micrófono del PC",
        (int)Origen.CollarPorBluetooth => "Collar por Bluetooth",
        (int)Origen.CollarPorTelefono => "Collar por el teléfono",
        _ => "Fuente desconocida",
    };

    /// <summary>
    /// Cuánto tarda la voz en llegar por cada camino, en milisegundos, para que quien la consuma no
    /// tenga que adivinarlo.
    ///
    /// MEDIDO EL 2026-09-01, no estimado. El micrófono del PC entrega en el buffer de NAudio (100 ms
    /// configurados). El collar por Bluetooth entrega tramas de 20 ms sobre un enlace local. El
    /// teléfono, en la corrida de 100,7 s contra nuestro WebSocket, entregó tramas de 640 bytes cada
    /// 20 ms — pero cruzando app, red móvil e internet, así que lo que se declara es el camino, no
    /// el tamaño de la trama.
    ///
    /// Para qué sirve el número: con 4-5 s (lo que daba el webhook de Omi) se puede dictar pero no
    /// interrumpir a Ü a media frase. Quien consume la voz necesita saber en cuál de los dos mundos
    /// está antes de prometerle nada al usuario.
    /// </summary>
    public static int LatenciaNominalMs(int fuente) => fuente switch
    {
        (int)Origen.MicrofonoDelPc => 100,
        (int)Origen.CollarPorBluetooth => 120,
        (int)Origen.CollarPorTelefono => 400,
        _ => 0,
    };
}
