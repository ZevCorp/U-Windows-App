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
    /// falso, ni se ofrece esa herramienta ni se manda la foto de señalar: prometerlas y que no
    /// sirvan de nada es peor que no tenerlas.
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

    /// <summary>Un trozo de micrófono, PCM de 16 bits mono al <see cref="RitmoDeEntrada"/>.</summary>
    string Audio(byte[] pcm);

    /// <summary>Una foto suelta, fuera del flujo normal de audio. Vacío si <see cref="Mira"/> es falso.</summary>
    string Fotograma(byte[] jpeg);

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
    string PedirRespuesta();

    /// <summary>Qué está diciendo el servidor, en hechos. Vacío si no dice nada que nos toque.</summary>
    IReadOnlyList<Hecho> Leer(JsonElement mensaje);
}
