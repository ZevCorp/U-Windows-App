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

    /// <summary>
    /// ¿SE REPITE UN CLIC QUE NO MOVIÓ NADA? Promesa 248 (spec 026).
    /// </summary>
    /// <remarks>
    /// EL GESTO ERA BUENO Y EL ELEMENTO TAMBIÉN; lo que falló fue el momento. El 2026-09-16 Ü resolvió
    /// «Compose» de Gmail —la carita se puso a su lado, así que el elemento era el correcto—, lo pulsó por
    /// patrón, la llamada devolvió éxito y la redacción no se abrió. Probando después sobre ese mismo
    /// botón con Gmail ya asentado, el mismo Invoke la abre a la primera: nueve segundos antes se había
    /// activado la pestaña, y Gmail es pesado. Lo caro no es que un clic se pierda, es enterarse: hoy
    /// cuesta una vuelta al modelo, entre cinco y diez segundos. Repetirlo aquí cuesta uno.
    ///
    /// LA PROMESA 83 TIENE RAZÓN EN LO SUYO Y NO SE TOCA: «Guardar» hace su trabajo SIN cambiar de
    /// pantalla, y repetirlo es guardar dos veces —Configuración llegó a ANULAR con el segundo clic
    /// (2026-08-03)—. Desde fuera, un «Guardar» que funcionó y un «Compose» que se perdió son idénticos:
    /// misma pantalla, mismos elementos vivos, mismo todo.
    ///
    /// LA SEÑAL QUE SÍ LOS SEPARA LA TIENE EL TERRENO. Una puerta que ya se vio llevar a algún sitio
    /// tiene destino aprendido en el grafo; un botón que aplica algo no lo tiene ni lo tendrá, porque
    /// nunca cruzó a ninguna parte. Así que solo se repite lo que SABEMOS que navega, y de paso esto
    /// mejora con el uso, que es de lo que va el producto entero.
    /// </remarks>
    /// <param name="sabeQueLleva">El terreno ya aprendió que esta puerta lleva a algún sitio.</param>
    public static bool HayQueRepetir(bool cambioLaPantalla, int vivosAntes, int vivosDespues,
        bool esDestructivo, bool sabeQueLleva, bool yaSeRepitio)
        => !cambioLaPantalla && vivosAntes == vivosDespues && !esDestructivo && sabeQueLleva && !yaSeRepitio;

    /// <summary>¿Hay que devolverle el foco a la persona después de pulsar?</summary>
    public static bool HayQueDevolver(Gesto gesto, IntPtr focoAntes, IntPtr focoDespues)
        => gesto == Gesto.Fisico && focoAntes != IntPtr.Zero && focoAntes != focoDespues;
}
