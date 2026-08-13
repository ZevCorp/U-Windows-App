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
}
