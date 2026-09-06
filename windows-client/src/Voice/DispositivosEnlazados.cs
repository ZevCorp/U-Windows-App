namespace U.WindowsClient.Voice;

/// <summary>
/// QUÉ APARATOS HAY ENLAZADOS AHORA MISMO, para poder enseñarlos y poder olvidarlos.
/// </summary>
/// <remarks>
/// NACE DE UN HUECO QUE ABRIMOS NOSOTROS (2026-09-05, spec 010). Al retirar la ventana vieja del
/// collar se fueron con ella las dos únicas puertas a <c>CollarPermanente.Olvidar()</c> y a
/// <c>CollarPermanente.Estado</c>: enlazar seguía teniendo sitio —el selector de micrófono—, pero
/// DESENLAZAR dejó de tenerlo. Un emparejamiento que se puede hacer y no se puede deshacer es una
/// trampa, sobre todo en un hospital donde los collares se prestan.
///
/// SE LISTA LO QUE HAY, NO LO QUE CABE. Las tres opciones del selector (computador, collar,
/// teléfono) son SITIOS por donde puede entrar la voz y están siempre; esto es otra cosa: los
/// aparatos que de verdad quedaron emparejados con esta máquina. El micrófono del computador nunca
/// aparece aquí — no se empareja ni se puede olvidar, es el que hay.
///
/// La decisión vive separada de la ventana por el mismo camino que <see cref="Ui.ReglaDelAura"/>:
/// el contrato la juzga sin pantalla, y quien pinta la sección pregunta a ESTA función.
/// </remarks>
public static class DispositivosEnlazados
{
    /// <summary>El collar Omi, emparejado por Bluetooth con esta máquina.</summary>
    public const string Collar = "collar";

    /// <summary>
    /// Los aparatos emparejados con este computador.
    /// </summary>
    /// <remarks>
    /// EL TELÉFONO NO ENTRA, y no es un olvido (2026-09-06, decisión del dueño). Un teléfono no se
    /// empareja con esta máquina: se le da un código y manda audio por la red. No hay nada que
    /// «olvidar» en este computador —el código caduca solo a las 8 horas— y ponerlo aquí prometía
    /// una relación que no existe. Su sitio es la caja del enlace, que es donde de verdad se
    /// gestiona.
    ///
    /// SE PIDE EL HECHO, NO LA INTENCIÓN. El parámetro es
    /// <see cref="CollarPermanente.Enlazado"/> —«se llegó a conectar»— y NO <c>Permanente</c>, que
    /// se pone en cuanto alguien elige el collar en el menú. Pasarle la intención era justamente el
    /// bug: aparecía un collar para olvidar sin que existiera ninguno.
    /// </remarks>
    /// <param name="collarEnlazado"><c>CollarPermanente.Enlazado</c>: un collar contestó alguna vez.</param>
    public static string[] Listar(bool collarEnlazado) =>
        collarEnlazado ? new[] { Collar } : Array.Empty<string>();
}
