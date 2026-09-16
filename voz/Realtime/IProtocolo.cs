using System.Text.Json;

namespace Voz.Realtime;

/// <summary>
/// CÓMO SE HABLA CON EL SERVIDOR. Traduce en los dos sentidos y no guarda nada.
/// </summary>
/// <remarks>
/// SIN ESTADO A PROPÓSITO. Todo lo que hay que recordar —si la sesión está viva, cuántas fichas
/// van, qué llamadas se retiraron, el pase para volver— es de la CONVERSACIÓN y vale igual con
/// cualquier proveedor. Un traductor con memoria acabaría teniendo dos copias de esos datos, y dos
/// copias del mismo hecho divergen.
///
/// Y por eso se puede juzgar entero sin socket, sin micrófono y sin clave: se le da un mensaje y se
/// mira qué hechos saca. Un traductor que solo se prueba hablando con el servidor de verdad no se
/// prueba nunca — y estos fallan en silencio, que es la peor forma de fallar: la sesión queda
/// abierta y Ü simplemente no contesta.
/// </remarks>
public interface IProtocolo
{
    /// <summary>Cómo se llama, para los logs y para saber a quién culpar.</summary>
    string Quien { get; }

    /// <summary>El modelo con el que se abre. Clavado, nunca un alias: un alias se mueve solo.</summary>
    string Modelo { get; }

    /// <summary>
    /// A cuántos hercios hay que capturar el micrófono. NO es un detalle: Gemini quiere 16 kHz y
    /// OpenAI 24 kHz, y darle el otro no da error — suena acelerado o ralentizado, que es el primer
    /// síntoma cuando algo va mal aquí.
    /// </summary>
    int RitmoDeEntrada { get; }

    /// <summary>A cuántos hercios llega la voz de vuelta.</summary>
    int RitmoDeSalida { get; }

    /// <summary>
    /// Si sabe interpretar una imagen dentro de la conversación. NO implica que la reciba sola y
    /// seguida: aquí no se manda vídeo en directo — se manda una foto suelta cuando el usuario
    /// señala algo, o cuando el propio modelo pide mirar (herramienta <c>map_look</c>). Si esto es
    /// falso no se manda ninguna foto: <c>map_look</c> se sigue ofreciendo, pero contesta que no puede y
    /// ofrece <c>map_what_i_see</c>, y la foto de un recuerdo nuevo se queda en disco. Mandar una que el
    /// servidor rechaza es peor que no mandarla: el modelo contesta como si la hubiera visto mal.
    /// (Corregido el 2026-09-12: decía que la herramienta no se ofrecía, y ConversacionEnVivo sí la ofrece.)
    /// </summary>
    bool Mira { get; }

    /// <summary>
    /// Si sabe darnos un pase para volver a la misma conversación tras un corte. Si no lo sabe, una
    /// caída empieza de cero y hay que decirlo, no fingir que se recuperó.
    /// </summary>
    bool SabeVolver { get; }

    /// <summary>La dirección del socket. La clave va aquí o en las cabeceras, según quién.</summary>
    Uri Direccion();

    /// <summary>Lo que hay que poner en las cabeceras del socket. Vacío si la clave va en la URL.</summary>
    IReadOnlyDictionary<string, string> Cabeceras(string clave);

    /// <summary>
    /// Lo que se manda nada más abrir. Va en lista porque no todos lo dicen en un solo mensaje.
    /// <paramref name="pase"/> vacío = conversación nueva.
    /// </summary>
    IEnumerable<string> Apertura(string instrucciones, IReadOnlyList<Utensilio> utensilios, string pase);

    /// <summary>
    /// La misma apertura, diciendo si la sesión puede crear respuestas por su cuenta. Con
    /// <paramref name="soloCuandoSeLePide"/> el servidor sigue oyendo y transcribiendo, pero no
    /// contesta hasta que la app pide un turno (promesa 192: la voz prestada).
    /// </summary>
    IEnumerable<string> Apertura(string instrucciones, IReadOnlyList<Utensilio> utensilios, string pase, bool soloCuandoSeLePide)
        => Apertura(instrucciones, utensilios, pase);

    /// <summary>
    /// Si el servidor avisa de que el usuario empezó a hablar y de que un turno acabó (<see
    /// cref="Hecho.HablaronEncima"/> y <see cref="Hecho.CierraElTurno"/>). Si no, quien conversa los
    /// tiene que marcar por su cuenta.
    /// </summary>
    /// <remarks>
    /// Nació el 2026-09-12 con GPT-Live, que no manda NINGUNA: ni speech_started ni response.done, y su
    /// response.completed es del modelo delegado — la voz sigue hablando segundos después. Un traductor
    /// que las fingiera le daría a la conversación un reloj que no existe; uno que calla sin decirlo la
    /// deja con la línea de lo dicho abierta para siempre. Por defecto verdadero: es lo que hace Realtime.
    /// </remarks>
    bool MarcaLosTurnos => true;

    /// <summary>
    /// Si sabe oír sin contestar hasta que se le pide turno (promesa 192, la voz prestada). Si es falso,
    /// <c>soloCuandoSeLePide</c> no tiene efecto, y quien lo pide tiene que decirlo en vez de fingirlo:
    /// GPT-Live no tiene turn_detection ni create_response, y «no hables por tu cuenta» en las
    /// instrucciones no se respetó (medido el 2026-09-12: la voz contestó sola).
    /// </summary>
    bool SabeEsperarTurno => true;

    /// <summary>
    /// Si el servidor CONFIRMA que la sesión abrió (<see cref="Hecho.Abierta"/>). Si la confirma, hasta
    /// entonces no está abierta: un <see cref="Hecho.Falla"/> antes de la confirmación es que no abrió, y
    /// reenviar la misma apertura fallaría igual.
    /// </summary>
    /// <remarks>
    /// Nació el 2026-09-12 con GPT-Live: sin crédito, session.start contestó credit_balance_exhausted sin
    /// session.started y el socket murió a los ~2 s; la conversación lo tomaba por un corte, reenviaba el
    /// mismo session.start cuatro veces y decía «Sigo» cada vez, con la causa solo en el log. GPT Realtime lo
    /// declara desde el 2026-09-13, con session.created (promesa 50). Por defecto falso: un protocolo que no se
    /// ha medido así no confirma nada, y la conversación dice en su línea de apertura que nadie la confirmó.
    /// </remarks>
    bool ConfirmaQueAbrio => false;

    /// <summary>
    /// Lo que se manda para cambiar instrucciones y herramientas A MITAD de sesión.
    /// </summary>
    /// <remarks>
    /// Por defecto, la misma apertura sin pase: en Realtime la apertura es un session.update y repetirla
    /// es exactamente cambiar de modo. No vale para todos — en GPT-Live la apertura es session.start, que
    /// a mitad de sesión no cambia nada: es otra sesión. Quien tenga una sesión que no se reabre dice
    /// aquí cómo se cambia.
    /// </remarks>
    IEnumerable<string> CambioDeModo(string instrucciones, IReadOnlyList<Utensilio> utensilios, bool soloCuandoSeLePide)
        => Apertura(instrucciones, utensilios, "", soloCuandoSeLePide);

    /// <summary>Un trozo de micrófono, PCM de 16 bits mono al <see cref="RitmoDeEntrada"/>.</summary>
    string Audio(byte[] pcm);

    /// <summary>Una foto suelta, fuera del flujo normal de audio. Vacío si <see cref="Mira"/> es falso.</summary>
    string Fotograma(byte[] jpeg);

    /// <summary>
    /// LA MISMA FOTO, PERO POR REFERENCIA: solo su identificador, sin un byte de imagen dentro.
    /// Vacío si este protocolo no sabe mirar así, y entonces se manda con <see cref="Fotograma"/>.
    /// </summary>
    /// <remarks>
    /// GPT-Live tiene un buzón de 32.768 bytes para la sesión ENTERA y una captura pesa 118.000 ya
    /// codificada: metida dentro no cabe ninguna, y de ahí venía que Ü fuera ciega. Medido contra el
    /// servidor real el 2026-09-16: por referencia entra, describe bien tres imágenes seguidas sin
    /// vaciar nada, y en el buzón ocupan unos treinta bytes cada una (spec 027, promesa 54).
    /// </remarks>
    string FotogramaPorReferencia(string idDelArchivo) => "";

    /// <summary>¿La foto viaja por referencia? Si no, va dentro del mensaje.</summary>
    bool VePorReferencia => false;

    /// <summary>Una frase escrita, sin colgar la conversación.</summary>
    string Texto(string texto);

    /// <summary>
    /// Lo que salió de ejecutar lo que pidió. Va en lista porque puede pedir varias a la vez, y
    /// devolverlas de una es lo que evita que el modelo se quede esperando a la primera.
    ///
    /// NO PIDE RESPUESTA POR SU CUENTA — eso es <see cref="PedirRespuesta"/>, aparte. Juntar los
    /// dos aquí escondía un efecto secundario detrás de un nombre que no lo prometía.
    /// </summary>
    IEnumerable<string> Resultados(IReadOnlyList<(string Id, string Nombre, string Resultado)> hechas);

    /// <summary>
    /// Pide que el servidor conteste AHORA. Vacío si este protocolo ya contesta solo en cuanto
    /// recibe un resultado —como hacía Gemini— y no en falso: mandar esto de más no es gratis, abre
    /// una respuesta que puede pisar a otra que ya estuviera en curso.
    /// </summary>
    /// <param name="instrucciones">Qué decir, si se le quiere dictar. Vacío —lo normal— es pedirle
    /// turno y que conteste lo que crea. Con texto, Ü lo dice con SU voz: es lo que permite que
    /// narre un recorrido que decide otro (el piloto) sin que hable el sintetizador del sistema.</param>
    string PedirRespuesta(string instrucciones = "");

    /// <summary>Qué está diciendo el servidor, en hechos. Vacío si no dice nada que nos toque.</summary>
    IReadOnlyList<Hecho> Leer(JsonElement mensaje);
}
