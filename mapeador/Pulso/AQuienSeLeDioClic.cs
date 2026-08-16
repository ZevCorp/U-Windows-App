namespace Mapeador;

/// <summary>
/// A QUÉ ELEMENTO CONOCIDO CORRESPONDE UN CLIC. Puro: recibe lo que el núcleo dice conocer en una
/// pantalla y lo que el vigilante de clics vio, y devuelve UN selector o ninguno.
/// </summary>
/// <remarks>
/// EL FALLO QUE ARREGLA (2026-08-13, lo vio el usuario paseando por la Maqueta): navegar hacía clic
/// en los botones y el grafo se quedaba sin aristas —«maqueta-familia-c» acabó con 35 elementos y
/// cero salidas—. Parecía aleatorio: la misma transición fallaba a las 06:46:41 y se aprendía a las
/// 06:46:55.
///
/// No era aleatorio. UIA expone un botón Y, dentro, el texto con su mismo nombre. El vigilante de
/// clics resuelve al elemento MÁS INTERNO, así que devuelve el `Text`; el observador, en cambio, ya
/// había descartado ese `Text` por duplicado y el núcleo solo conoce el `Button`. Como la atribución
/// exigía que coincidieran etiqueta Y tipo, no casaban nunca:
///
///   · clic sobre las letras del botón   → `Text`   → el núcleo no lo conoce → sin arista
///   · clic en el margen del botón       → `Button` → casa                   → arista aprendida
///
/// Dos sitios opinando distinto sobre el mismo hecho: el filtro DECLARA que el Text y el control
/// homónimo son la misma cosa, y acto seguido tira esa declaración. Aquí se usa la misma
/// equivalencia para las dos cosas, que es lo que la hace una regla y no una casualidad.
///
/// LA GUARDA DE AMBIGÜEDAD SE MANTIENE ENTERA, y no es opcional: en el escritorio de Windows hay dos
/// cosas llamadas «Nombre» —la columna y la celda— y elegir una a ojo acuñó una arista falsa que
/// dejó al navegador pulsando lo que no era, para siempre. Sin camino se puede seguir explorando;
/// con un camino equivocado, no.
/// </remarks>
public static class AQuienSeLeDioClic
{
    /// <summary>Qué salió de buscar: el selector si hubo UNO solo, y cuántos había.</summary>
    public readonly record struct Atribucion(string Selector, int Candidatos)
    {
        public bool Hay => Selector.Length > 0;
    }

    /// <summary>
    /// Busca primero la coincidencia exacta —etiqueta y tipo—; y solo si no la hay y lo clicado fue
    /// un `Text`, cae al control del mismo nombre, que es lo que el filtro del observador ya había
    /// declarado que era. Nunca al revés: un clic en un control no debe resolverse a un texto suelto.
    /// </summary>
    public static Atribucion Resolver(
        IReadOnlyList<(string Selector, string Etiqueta, string Tipo)> conocidos,
        string etiqueta, string tipo)
    {
        if (conocidos.Count == 0 || string.IsNullOrEmpty(etiqueta)) return new("", 0);

        var exactos = conocidos.Where(c =>
            c.Etiqueta.Equals(etiqueta, StringComparison.OrdinalIgnoreCase) &&
            c.Tipo.Equals(tipo, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exactos.Count > 0)
            return new(exactos.Count == 1 ? exactos[0].Selector : "", exactos.Count);

        if (!EsTexto(tipo)) return new("", 0);

        var duenos = conocidos.Where(c =>
            c.Etiqueta.Equals(etiqueta, StringComparison.OrdinalIgnoreCase) && !EsTexto(c.Tipo)).ToList();
        return new(duenos.Count == 1 ? duenos[0].Selector : "", duenos.Count);
    }

    private static bool EsTexto(string tipo) => tipo.Equals("Text", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// ¿PUEDE ESTE CLIC EXPLICAR QUE NOS FUÉRAMOS DE AQUÍ? Solo si ocurrió DESPUÉS de que
    /// llegáramos: el clic que te trajo no puede ser el que te saca.
    /// </summary>
    /// <remarks>
    /// LA ARISTA FALSA QUE ESTO MATA (2026-08-13, el usuario pidió ir a «documentos» y el navegador
    /// se quedó en «datos-adjuntos»):
    ///
    ///   10:35:15  salto documentos→datos-adjuntos SIN atribuir («Documentos» ya explicó…)
    ///   10:35:15  pulsado «Datos adjuntos» → datos-adjuntos
    ///   10:35:18  aprendido: «Datos adjuntos» lleva de datos-adjuntos a escritorio   ← falsa
    ///
    /// «Datos adjuntos» es el clic que nos METIÓ en datos-adjuntos. Como el salto de entrada se
    /// rechazó por otro motivo, ese clic nunca quedó marcado como usado y siguió disponible para
    /// explicar el salto SIGUIENTE. La guarda de «un clic explica UNA transición» no lo cubre: solo
    /// marca los clics que llegaron a explicar algo, y este no explicó nada — por eso pudo mentir.
    ///
    /// El grafo acuñó «datos-adjuntos --[Datos adjuntos]--> documentos». El navegador la siguió
    /// fielmente, pulsó la carpeta en la que YA ESTABA, y no se movió nunca. Una arista falsa no es
    /// un dato de menos: es un dato que hace daño.
    /// </remarks>
    public static bool PuedeExplicarLaSalida(DateTime cuandoElClic, DateTime cuandoLlegamos) =>
        cuandoElClic > cuandoLlegamos;
}
