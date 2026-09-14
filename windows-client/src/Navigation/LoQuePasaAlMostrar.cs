namespace U.WindowsClient.Navigation;

/// <summary>
/// QUÉ PASA AL PULSAR «MOSTRAR». Promesa 225 (spec 016). Puro.
/// </summary>
/// <remarks>
/// UN SOLO BOTÓN, DOS CAMINOS, y el usuario no tiene por qué saber cuál. Sobre un aprendizaje ya
/// repasado, mostrarlo es CORRERLO: los mismos pasos, con la coreografía de siempre —la carita al
/// lado, la frase, el toque— y sin datos de nadie, de modo que los huecos quedan en blanco y no se
/// escribe el valor del paciente de prueba (promesa 123). Sobre uno sin repasar, mostrarlo es
/// COMPROBARLO, que es lo que la promesa 127 exige antes de dejar ejecutar nada.
///
/// CONFUNDIRLOS SERÍA EJECUTAR EN SAP UNA TAREA QUE NADIE HA VISTO ANDAR. Por eso la decisión vive
/// aquí, en una función que el contrato puede juzgar, y no repartida por los manejadores de la
/// ventana.
///
/// Y SIN MANOS SE DICE. La ventana de la consulta nace antes que la carita (ver
/// <c>Clinical.PuenteASap</c>): si la carita no ha colgado su puente, un botón «Mostrar» sería un
/// botón que no hace nada — y un control que no responde se lee como app rota, no como app que
/// espera.
/// </remarks>
public static class LoQuePasaAlMostrar
{
    /// <param name="Que">«correr», «comprobar» o «no».</param>
    /// <param name="Motivo">Vacío si se puede hacer sin explicar nada. Si no, QUÉ pasa, dicho para
    /// la persona.</param>
    public sealed record Decision(string Que, string Motivo);

    public static Decision Decidir(SkillEnsenada? skill, bool hayManos)
    {
        if (skill == null) return new("no", "no encuentro ese aprendizaje.");
        if (!hayManos)
            return new("no", "la carita todavía no está lista. Ábrela y vuelve a pulsar «Mostrar».");
        return skill.Comprobada
            ? new("correr", "")
            : new("comprobar", "Todavía no la he repasado: al mostrarla, la repaso.");
    }
}
