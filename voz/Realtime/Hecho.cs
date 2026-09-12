namespace Voz.Realtime;

/// <summary>
/// UN HECHO DE LA CONVERSACIÓN, dicho sin acento de nadie.
/// </summary>
/// <remarks>
/// Los dos servidores cuentan lo mismo con palabras distintas: donde Gemini manda
/// <c>serverContent.modelTurn.parts[].inlineData</c>, OpenAI manda
/// <c>response.output_audio.delta</c>; y son el mismo hecho — «aquí va un trozo de voz».
///
/// Esta lista es la frontera. Al otro lado, la conversación no sabe con quién habla: enciende el
/// micrófono, ejecuta herramientas, reproduce audio y pinta transcripciones igual venga de donde
/// venga. Cambiar de proveedor deja de ser un archivo de mil líneas y pasa a ser una clase de
/// ciento y pico que solo traduce.
///
/// UN MENSAJE PUEDE TRAER VARIOS HECHOS, y por eso se leen en lista y no de uno en uno: el
/// <c>response.done</c> de OpenAI cierra el turno Y trae el consumo Y a veces trae la llamada a una
/// herramienta. Devolver solo el primero perdería los otros en silencio.
/// </remarks>
public abstract record Hecho
{
    /// <summary>Un trozo de voz de Ü, PCM de 16 bits al ritmo de salida.</summary>
    public sealed record Suena(byte[] Pcm) : Hecho;

    /// <summary>Un trozo de lo que dijo quien habla. Llega por pedazos, no por frases.</summary>
    public sealed record DiceElUsuario(string Trozo) : Hecho;

    /// <summary>Un trozo de lo que dice Ü.</summary>
    public sealed record DiceU(string Trozo) : Hecho;

    /// <summary>Se acabó el turno: lo dicho queda fijo y la siguiente frase empieza línea nueva.</summary>
    public sealed record CierraElTurno : Hecho;

    /// <summary>
    /// Habló encima. Lo que ya nos habían mandado sigue en la cola de audio, y seguir diciéndolo es
    /// la sensación exacta de no ser escuchado.
    /// </summary>
    public sealed record HablaronEncima : Hecho;

    /// <summary>El modelo pide ejecutar cosas.</summary>
    public sealed record Pide(IReadOnlyList<Llamada> Cuales) : Hecho;

    /// <summary>
    /// El modelo RETIRA lo que había pedido. Contestar a algo retirado es lo que lo hacía repetirlo:
    /// el usuario hablaba, se cancelaban las llamadas, el modelo volvía a pedir las mismas, y la
    /// conversación no avanzaba nunca (2026-08-05).
    /// </summary>
    public sealed record Retira(IReadOnlyList<string> Ids) : Hecho;

    /// <summary>El pase para volver a ESTA conversación si se cae el socket. No todos lo dan.</summary>
    public sealed record PaseParaVolver(string Handle) : Hecho;

    /// <summary>Lo que va costando la conversación, en fichas.</summary>
    public sealed record Consumo(int Entrada, int Salida, int Total) : Hecho;

    /// <summary>
    /// Lo que va durando la conversación, en segundos: el ACUMULADO de la sesión, no un incremento.
    /// </summary>
    /// <remarks>
    /// Es lo que cuenta GPT-Live en vez de fichas (<c>session.usage.updated</c>). Medido el 2026-09-12
    /// en la misma sesión: 12.0 a los 15 s y 25.0 a los 30 s. Quien lo sume como <see cref="Consumo"/>
    /// cuenta 37 s donde hubo 25; se guarda el último. Promesa 48 de la voz.
    /// </remarks>
    public sealed record Duracion(double Segundos) : Hecho;

    /// <summary>
    /// El servidor dice que algo va mal. Se cuenta como hecho y no se traga: una sesión abierta, el
    /// micrófono en rojo y ninguna pista de por qué no contesta es el peor diagnóstico posible.
    /// </summary>
    public sealed record Falla(string Que) : Hecho;

    /// <summary>
    /// El servidor CONFIRMA que la sesión abrió. Solo lo mandan los protocolos que lo declaran (<see
    /// cref="IProtocolo.ConfirmaQueAbrio"/>), y para ellos lo que llega antes es que no abrió, no un corte.
    /// </summary>
    public sealed record Abierta : Hecho;
}

/// <summary>Algo que el modelo pide ejecutar.</summary>
public sealed record Llamada(string Id, string Nombre, IReadOnlyDictionary<string, string> Args);

/// <summary>
/// Una herramienta, descrita sin forma de nadie: cada protocolo la viste a su manera.
/// </summary>
/// <remarks>
/// Todos los argumentos son texto. No es una simplificación perezosa: lo que llega del modelo se
/// convierte a texto igual —los dos protocolos permiten números y objetos— y la única consumidora,
/// <c>SurfaceMapTools.Call</c>, recibe un diccionario de cadenas. Declarar tipos que después se
/// aplanan sería prometer una precisión que nadie usa.
/// </remarks>
public sealed record Utensilio(string Nombre, string Descripcion, IReadOnlyList<Argumento> Args);

/// <summary>Un argumento de una herramienta.</summary>
public sealed record Argumento(string Nombre, string Que);
