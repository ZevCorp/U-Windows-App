namespace U.WindowsClient.Ui;

/// <summary>Por qué querría abrirse el globo de conversación. No todos lo consiguen.</summary>
public enum MotivoDelGlobo
{
    /// <summary>Una persona lo pidió: el atajo de escribirle, o la pastilla de chat.</summary>
    LoPidioAlguien,

    /// <summary>Ü preguntó algo y espera una respuesta escrita. Sin globo, la pregunta no se puede contestar.</summary>
    HayQueContestar,

    /// <summary>Algo no se pudo hacer y hay que decir por qué.</summary>
    AlgoFallo,

    /// <summary>Ü está contando lo que va haciendo. Es un estado, no una conversación.</summary>
    SoloEsProgreso,
}

/// <summary>
/// CUÁNDO SE ABRE EL GLOBO, Y CUÁNDO NO.
/// </summary>
/// <remarks>
/// POR QUÉ EXISTE (2026-09-06, tras probarlo el dueño dos veces). La primera vez dijo que el chat
/// era «superestorboso» y quité los DOS sitios de la voz que lo abrían. Seguía saliendo: había
/// <b>veinte</b>. Narrar, contar el progreso de un workflow, la cuenta atrás de una grabación, el
/// cierre de una enseñanza… cada uno abría el globo por su cuenta, y ninguno era una conversación.
///
/// Es el aprendizaje nº11 del CLAUDE.md, cometido por mí: arreglé la clase de error en dos de los
/// veinte sitios que la tenían, y me quedé tranquilo porque el caso que me habían enseñado dejó de
/// pasar. Contar los sitios ANTES habría dicho que dos no era el arreglo.
///
/// LA REGLA, dicha una vez y en un solo sitio: <b>el globo es para conversar, no para informar.</b>
/// Se abre si lo pide una persona, o si Ü hace una pregunta que hay que contestar escribiendo.
///
///   · Un FALLO no abre el globo, pero SÍ despliega el muelle: la píldora vive dentro, y un fallo
///     que nadie ve es indistinguible de una aplicación que no hace nada (es lo que motivó
///     <c>Aviso</c> el 2026-09-01).
///   · El PROGRESO no abre nada. Va a la píldora si el panel está abierto, y al log siempre. Un
///     asistente que interrumpe para decir «voy por el paso 3» cansa, y el texto no se pierde: se
///     queda escrito en el globo para quien lo abra.
/// </remarks>
public static class ReglaDelGlobo
{
    /// <summary>¿Se abre el globo?</summary>
    public static bool SeAbre(MotivoDelGlobo motivo) =>
        motivo is MotivoDelGlobo.LoPidioAlguien or MotivoDelGlobo.HayQueContestar;

    /// <summary>
    /// ¿Hay que desplegar el muelle? Todo menos el progreso: sin el panel a la vista no hay dónde
    /// enseñar ni el globo ni la píldora, así que callar sería lo mismo que no haber pasado nada.
    /// </summary>
    public static bool DespliegaElMuelle(MotivoDelGlobo motivo) =>
        motivo != MotivoDelGlobo.SoloEsProgreso;
}
