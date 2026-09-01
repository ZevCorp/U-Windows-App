namespace U.WindowsClient.Clinical.Transcripcion;

/// <summary>
/// CÓMO SE HABLA CON UN PROVEEDOR DE TRANSCRIPCIÓN. Una implementación por proveedor; cuál se usa lo
/// decide la SESIÓN que entrega el backend, no lo que se compiló.
/// </summary>
/// <remarks>
/// POR QUÉ EXISTE ESTA INTERFAZ. El proveedor se configura en el Provider Studio y puede cambiar sin
/// tocar el cliente: el motor del portal (lib/stt/deepgram-dictation.js) lleva desde siempre hablando
/// los dos, y el de Windows solo hablaba Soniox — el archivo se llamaba <c>DictadoSoniox</c>, así que
/// «no arranca con Deepgram» parecía correcto en vez de parecer un fallo. El día que se conmutara,
/// el dictado moría diciendo «el backend no devolvió la configuración del stream», que es un mensaje
/// que no distingue «es Deepgram» de «el backend falló» (aprendizaje nº2).
///
/// LAS DOS DIFERENCIAS SON IRRECONCILIABLES, y por eso son una interfaz y no un `if`:
///
/// | | Soniox | Deepgram |
/// |---|---|---|
/// | autenticación | primer mensaje JSON (`start_message`) | tupla del subprotocolo del WebSocket |
/// | formato de audio | se declara en el primer mensaje | va en la URL que firma el backend |
/// | lo que llega | `tokens[]` con `is_final` y `&lt;end&gt;` | `channel.alternatives[0].transcript` |
/// | despedida | `{"type":"finalize"}` | `{"type":"Finalize"}` |
///
/// Sí, la despedida se diferencia solo en una mayúscula. Está en el motor del portal desde el
/// principio y no es un capricho de nadie: cada proveedor la escribe a su manera.
/// </remarks>
public interface ILectorDeStream
{
    /// <summary>Cómo se llama el proveedor, en minúscula. Es lo que se anota en el log.</summary>
    string Nombre { get; }

    /// <summary>
    /// Los subprotocolos con los que abrir el WebSocket. VACÍO significa socket pelado — declarar
    /// uno donde no toca hace que el proveedor cierre sin decir por qué.
    /// </summary>
    IReadOnlyList<string> Subprotocolos { get; }

    /// <summary>
    /// El primer mensaje que se manda al abrir, o <c>null</c> si este proveedor no espera ninguno.
    /// Va ANTES que cualquier byte de audio.
    /// </summary>
    string? MensajeDeArranque(int ritmoHz);

    /// <summary>Lo que se manda al parar para que suelte lo último antes de colgar.</summary>
    string MensajeDeFinalize { get; }

    /// <summary>
    /// Digiere un mensaje del proveedor: lo confirmado va al <paramref name="verbatim"/>, lo
    /// provisional se avisa por <paramref name="parcial"/> (se pinta, no se actúa), y cada frase
    /// cerrada por <paramref name="frase"/>.
    /// </summary>
    void Digerir(string json, Verbatim verbatim,
        Action<string> parcial, Action<string> frase, Action<string> fallo);
}
