namespace U.Graph.Surfaces;

/// <summary>
/// CÓMO SE PULSA UN ELEMENTO: por el patrón de accesibilidad, o con el ratón. Promesa 234 (spec 020). Pura.
/// </summary>
/// <remarks>
/// EL CLIC FÍSICO ERA EL CAMINO NORMAL y el patrón el respaldo, y eso es lo que impedía que la persona
/// siguiera usando su computador mientras Ü trabaja: el físico necesita la ventana delante y mueve el
/// cursor. El patrón (Invoke, Toggle) se lo pide al control directamente, sin foco y sin ratón, y es lo
/// único que llega a una ventana de otro escritorio virtual. Así que va primero donde el elemento lo
/// admite.
///
/// EL FÍSICO SIGUE EXISTIENDO, y no por nostalgia: hay dos casos medidos en los que el patrón no hace
/// el trabajo. Un TreeItem o un ListItem exponen Invoke por herencia y Select por su naturaleza, y ni
/// uno ni otro NAVEGA: Select() marcó «Descargas» en el panel del explorador sin entrar (2026-08-08), e
/// Invoke sobre un ListItem devolvió true sin abrir nada (2026-08-02). Ahí el clic real hace las dos
/// cosas. Y lo que no admite patrón alguno solo se toca con el ratón.
///
/// Y CUANDO SE USA, SE DEVUELVE LO QUE ERA DE LA PERSONA: el foco y el cursor. Si el clic físico no
/// cambió el foco, no se toca nada; si no se sabía dónde estaba el foco, no se devuelve nada a ciegas.
/// </remarks>
public static class ComoSePulsa
{
    public enum Gesto
    {
        /// <summary>Por el patrón de accesibilidad: sin foco, sin ratón, sin traer nada al frente.</summary>
        Patron,
        /// <summary>Con el ratón de verdad: trae la ventana al frente, pulsa, y devuelve lo que tocó.</summary>
        Fisico,
        /// <summary>
        /// Por mensaje a la ventana del elemento (WM_LBUTTONDOWN/UP en su punto pulsable): no mueve el
        /// cursor de la persona ni necesita el foco. Promesa 237 (spec 021). Es el peldaño entre el
        /// patrón y el ratón real, para lo que no expone Invoke ni Toggle: un campo web, una pestaña.
        /// </summary>
        Mensaje,
    }

    /// <param name="admiteInvoke">El elemento expone InvokePattern.</param>
    /// <param name="admiteToggle">El elemento expone TogglePattern.</param>
    /// <param name="admiteSeleccion">El elemento expone SelectionItemPattern.</param>
    /// <param name="esContenidoDeLista">Es un ListItem, TreeItem o DataItem: lo que navega con el clic real y no con el patrón.</param>
    public static Gesto Decidir(bool admiteInvoke, bool admiteToggle, bool admiteSeleccion, bool esContenidoDeLista)
        => Decidir(admiteInvoke, admiteToggle, admiteSeleccion, esContenidoDeLista, tienePuntoPulsable: false);

    /// <summary>
    /// LA ESCALERA ENTERA (promesa 237): patrón, mensaje, ratón. El contenido de una lista va directo al
    /// ratón real, porque ahí se midió que ni el patrón abre (2026-08-02, 2026-08-08); lo que no tiene
    /// punto pulsable tampoco tiene a dónde mandar un mensaje.
    /// </summary>
    /// <param name="tienePuntoPulsable">UIA da un punto alcanzable del elemento (GetClickablePoint).</param>
    public static Gesto Decidir(bool admiteInvoke, bool admiteToggle, bool admiteSeleccion, bool esContenidoDeLista, bool tienePuntoPulsable)
    {
        if (esContenidoDeLista) return Gesto.Fisico;
        if (admiteInvoke || admiteToggle) return Gesto.Patron;
        if (tienePuntoPulsable) return Gesto.Mensaje;
        return Gesto.Fisico;   // sin patrón y sin punto: el ratón real, que trae la ventana y lo busca
    }

    /// <summary>¿Hay que devolverle el foco a la persona después de pulsar?</summary>
    public static bool HayQueDevolver(Gesto gesto, IntPtr focoAntes, IntPtr focoDespues)
        => gesto == Gesto.Fisico && focoAntes != IntPtr.Zero && focoAntes != focoDespues;
}
