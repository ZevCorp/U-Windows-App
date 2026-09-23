namespace U.WindowsClient.Ui.Jev;

/// <summary>
/// DÓNDE CORRE LO QUE PINTA. Promesa 382 (spec 049). Dos operaciones, y la diferencia entre ellas es la promesa:
/// <see cref="Encolar"/> deja el trabajo para el hilo que pinta y vuelve en el acto; <see cref="Ahora"/> lo
/// ejecuta ya, en el hilo que llama.
/// </summary>
/// <remarks>
/// POR QUÉ UNA INTERFAZ Y NO EL <c>Dispatcher</c> A SECAS. El ciclo de Jev corre en la tarea del tramo, no en la de
/// la ventana (<c>ElTramo</c>, <c>Task.Run</c>), y lo único que la vista le puede pedir es «encola esto»: un
/// <c>Dispatcher.Invoke</c> en la costura devolvería al ciclo los segundos que las ramas B y C le quitan. Con el
/// despachador inyectado, el contrato juzga eso SIN RELOJ —con un doble que lanza si se le pide «ya», otro que
/// acumula y otro que tira lo encolado— en vez de medir milisegundos en un runner sin garantías (revisión 6 de la
/// 049). El adaptador a WPF (<c>DespachadorDeWpf</c>, fase 8) es el único sitio de <c>Ui/Jev/</c> que encola en
/// el <c>Dispatcher</c>, y eso se cuenta en el fuente.
///
/// NINGÚN COMENTARIO DE <c>Ui/Jev/</c> NOMBRA ESE MÉTODO POR SU NOMBRE: la 382 (b) cuenta sus apariciones como texto
/// y exige una, la del adaptador. La primera versión de este archivo lo nombraba aquí y la cuenta salió «1 en
/// IDespachador.cs» (fase 6, medido).
/// </remarks>
public interface IDespachador
{
    /// <summary>Deja <paramref name="accion"/> para el hilo que pinta y vuelve sin esperarla. Es lo único que usa <see cref="ConectorDeLaVista.Publicar"/>.</summary>
    void Encolar(Action accion);

    /// <summary>
    /// Ejecuta <paramref name="accion"/> ya, en el hilo que llama. Existe para quien ya está en el hilo que pinta;
    /// desde el ciclo de Jev no se llama NUNCA, y el contrato lo juzga con un doble que lanza aquí (382).
    /// </summary>
    void Ahora(Action accion);
}
