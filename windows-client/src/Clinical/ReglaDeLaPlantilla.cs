namespace U.WindowsClient.Clinical;

/// <summary>
/// CON QUÉ PLANTILLA SE GRABA. Promesa 189 (spec 015).
/// </summary>
/// <remarks>
/// EL ORDEN ES LA PROMESA, y por eso vive en una regla pura en vez de repartido por la ventana: un
/// «si no hay esto, entonces aquello» escrito dentro de un manejador de clic no se puede juzgar sin
/// pantalla, y este orden decide con qué estructura sale la historia clínica de un paciente.
///
/// LOS CUATRO ESLABONES, y qué significa cada uno:
///
///   1. **Lo que el médico eligió** en el selector, para ESTA consulta. Manda sobre todo lo demás:
///      es lo único explícito que hay en la cadena.
///   2. **Su sugerida** (<see cref="SugeridaDelMedico"/>, la tabla `user_template_preferences` que
///      el portal llama «Tu sugerida»). Es su decisión de otro día, y sigue siendo suya.
///   3. **La de urgencias**, la institucional marcada por defecto. Es la de la casa: este despliegue
///      es el servicio de urgencias del Hospital General de Medellín.
///   4. **La abierta** (<see cref="PlantillaAbierta"/>), donde la estructura la pone el organizador.
///      Es el último recurso y sigue siendo el que garantiza que grabar nunca se quede sin poder
///      arrancar.
///
/// LO QUE NUNCA PASA, y es la mitad de la promesa: **caer en una cualquiera del catálogo**. Hay 204
/// plantillas institucionales (medido el 2026-09-07 en el log: «clinica: 204 plantilla(s)»), así que
/// «la primera de la lista» es una plantilla concreta de un servicio concreto elegida por el orden
/// que tuviera ese día la respuesta del backend. Una nota sale bien formada con la plantilla
/// equivocada, y eso es justo lo que no se ve — aprendizaje nº4: una caja que miente es peor que no
/// tener caja. Si ningún eslabón resuelve, se contesta <c>null</c> y quien llama CREA la abierta.
///
/// LA ESPECIALIDAD DEL MÉDICO NO ENTRA en la cadena, y es una decisión con fecha (2026-09-07). El
/// portal sí la usa, pero allí el médico puede ser de cualquier servicio; aquí la app se instala en
/// urgencias. Meter los dos respaldos obligaba a decidir cuál gana —la de su especialidad o la de la
/// casa— sin que nadie lo hubiera pedido, y esa decisión se toma cuando haya un caso que la pida.
/// </remarks>
public static class ReglaDeLaPlantilla
{
    /// <summary>Cómo se reconoce el servicio de urgencias en el código de especialidad.</summary>
    /// <remarks>
    /// Por prefijo aplanado y no por igualdad exacta: el backend normaliza a snake_case
    /// (`"Medicina de Urgencias"` → `medicina_urgencias`) y el catálogo real trae variantes
    /// («urgencias», «medicina_de_urgencias»). Comparar por igualdad contra una sola de ellas es la
    /// forma nº16 de fallar: una comparación entre identidades de distinta forma da falso SIEMPRE y
    /// en silencio, y aquí el silencio sería arrancar con la abierta creyendo que no hay urgencias.
    /// </remarks>
    private const string Urgencias = "urgencia";

    /// <summary>
    /// La plantilla con la que se graba, o <c>null</c> si el catálogo no da ninguna de las cuatro.
    /// </summary>
    /// <param name="catalogo">Lo que devolvió el backend para este médico.</param>
    /// <param name="elegidaId">Lo que tocó en el selector para esta consulta. Vacío si no tocó nada.</param>
    /// <param name="sugeridaId">Su «Tu sugerida» guardada. Vacío si no tiene.</param>
    public static PlantillaClinica? Elegir(IReadOnlyList<PlantillaClinica>? catalogo,
        string? elegidaId = null, string? sugeridaId = null)
    {
        if (catalogo == null || catalogo.Count == 0) return null;

        // 1 y 2 — por id. VACÍO NO ES AUSENTE (patrón nº9): un id en blanco que llegara de la red o
        // del disco no puede casar con nada, y sin este filtro casaría con cualquier plantilla cuyo
        // Id también viniera vacío.
        var elegida = PorId(catalogo, elegidaId);
        if (elegida != null) return elegida;

        var sugerida = PorId(catalogo, sugeridaId);
        if (sugerida != null) return sugerida;

        // 3 — la de la casa. Se exige que esté MARCADA por defecto: «una de urgencias» son decenas,
        // y coger la primera que suene a urgencias sería la misma lotería que coger la primera del
        // catálogo, solo que con menos números.
        foreach (var p in catalogo)
        {
            if (p.EsLaPorDefecto && EsDeUrgencias(p.Especialidad)) return p;
        }

        // 4 — la abierta. Misma búsqueda por nombre que ya hacía la promesa 94, que sigue siendo
        // suya: este eslabón es el que ella juzga.
        return PlantillaAbierta.Elegir(catalogo);
    }

    /// <summary>¿Ese código de especialidad es el de urgencias?</summary>
    public static bool EsDeUrgencias(string? especialidad) =>
        Navigation.Nombres.Aplanar(especialidad ?? "").Contains(Urgencias, StringComparison.Ordinal);

    private static PlantillaClinica? PorId(IReadOnlyList<PlantillaClinica> catalogo, string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        foreach (var p in catalogo)
        {
            if (string.Equals(p.Id, id, StringComparison.Ordinal)) return p;
        }
        return null;
    }
}
