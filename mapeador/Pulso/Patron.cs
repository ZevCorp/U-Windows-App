namespace Mapeador;

/// <summary>
/// LA MISMA CASILLA AUNQUE LE CAMBIEN EL TEXTO. Reconoce un elemento por la FORMA de su etiqueta
/// cuando su nombre no es estable.
/// </summary>
/// <remarks>
/// HAY ELEMENTOS CUYO NOMBRE CAMBIA A PROPÓSITO. El captcha de la Procuraduría se llama, para el
/// sistema de accesibilidad, con la pregunta que hace: hoy «¿ Cuanto es 9 - 2 ?» y al recargar
/// «¿ Cuanto es 5 + 3 ?». Es literalmente su función —cambiar— así que no hay nombre que apuntar
/// (2026-08-13, leído del grafo: los dos elementos coexistían, uno vivo y otro muerto).
///
/// LA IDENTIDAD ES LA FORMA, NO EL TEXTO. «¿ Cuanto es * ?» describe la casilla igual de bien hoy
/// que mañana, y sin adivinar: no es «el segundo campo de texto» —que rompería en cuanto la página
/// añada un campo— sino «el que pregunta cuánto es algo», que es lo que esa casilla ES.
///
/// SE HACE CON UN PATRÓN Y NO CON UN MODELO, y eso no es tacañería. Un modelo mirando la pantalla
/// acertaría casi siempre; un patrón acierta siempre o falla en voz alta, y se puede probar en
/// milisegundos sin abrir una ventana. Para un problema determinista, la respuesta determinista.
///
/// EL RUIDO NO ESTORBA. El grafo acumula una etiqueta muerta por cada pregunta distinta que haya
/// visto, pero quien busca filtra primero por VIVO —solo una lo está en cada momento— así que las
/// muertas no crean ambigüedad. La memoria sobra; no miente.
/// </remarks>
public static class Patron
{
    /// <summary>¿Este texto tiene la forma que describe el patrón? `*` vale por cualquier cosa.</summary>
    /// <remarks>
    /// Sin comodines es una comparación normal, así que el mismo camino sirve para las etiquetas
    /// estables y para las que no lo son: quien busca no tiene que saber de antemano cuál es cuál.
    /// </remarks>
    public static bool Casa(string patron, string texto)
    {
        if (patron is null || texto is null) return false;
        if (!EsPatron(patron)) return string.Equals(patron, texto, StringComparison.OrdinalIgnoreCase);

        // Se parte por los comodines y se van consumiendo los trozos EN ORDEN. Cada trozo tiene que
        // aparecer después del anterior; si uno no aparece, no casa.
        var trozos = patron.Split('*');
        int desde = 0;

        for (int i = 0; i < trozos.Length; i++)
        {
            string t = trozos[i];
            if (t.Length == 0) continue;

            // El primer trozo, si el patrón no empieza por comodín, tiene que estar AL PRINCIPIO —y
            // el último, si no acaba en comodín, al FINAL—. Sin eso «* ?» casaría con cualquier
            // cosa que contenga « ?» en medio, y un patrón que casa de más es peor que ninguno:
            // devolvería el elemento equivocado sin que nadie lo note.
            if (i == 0)
            {
                if (!texto.StartsWith(t, StringComparison.OrdinalIgnoreCase)) return false;
                desde = t.Length;
                continue;
            }

            if (i == trozos.Length - 1)
            {
                if (!texto.EndsWith(t, StringComparison.OrdinalIgnoreCase)) return false;
                // Y tiene que caber DESPUÉS de lo ya consumido: si no, el mismo trozo estaría
                // haciendo de principio y de final a la vez.
                return texto.Length - t.Length >= desde;
            }

            int donde = texto.IndexOf(t, desde, StringComparison.OrdinalIgnoreCase);
            if (donde < 0) return false;
            desde = donde + t.Length;
        }

        return true;
    }

    /// <summary>¿Esto es un patrón, o una etiqueta literal?</summary>
    public static bool EsPatron(string texto) => texto is not null && texto.Contains('*');
}
