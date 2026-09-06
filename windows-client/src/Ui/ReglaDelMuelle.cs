using System.Windows;

namespace U.WindowsClient.Ui;

/// <summary>
/// CUÁNDO ESTÁ ABIERTO EL MUELLE, Y QUÉ PASA AL SOLTAR LA CARITA ENCIMA.
/// </summary>
/// <remarks>
/// Separada del dibujo por el mismo camino que <see cref="ReglaDelAura"/>: el contrato juzga sin
/// pantalla, y <see cref="Muelle"/> pregunta a estas funciones en vez de llevar la decisión suelta
/// entre los <c>MouseEnter</c>. Lo juzgado y lo que corre no pueden discrepar (aprendizaje nº16).
/// </remarks>
public static class ReglaDelMuelle
{
    /// <summary>
    /// ¿Tiene que estar desplegado ahora mismo?
    /// </summary>
    /// <remarks>
    /// EL CURSOR ABRE, PERO NO ES EL ÚNICO QUE MANDA PARA CERRAR, y esa asimetría es la promesa 148
    /// entera. Un muelle que se plegara con solo salir el cursor tendría dos formas de traicionar a
    /// quien lo usa, las dos encontradas ANTES de escribirlo (spec 010, 2026-09-05):
    ///
    ///   · escribiéndole a Ü en el globo, la mano va del ratón al teclado y el cursor sale del
    ///     muelle — el panel se plegaría con la frase a medias;
    ///   · leyendo lo que Ü acaba de contestar, apartar el ratón se llevaría el texto por delante.
    ///
    /// LO QUE MANDA ES QUE HAYA ALGO QUE LEER O ESCRIBIR, NO QUE Ü ESTÉ ESCUCHANDO, y esto se
    /// corrigió el 2026-09-05 con la app ya en la mano: hablarle por voz mantenía el panel abierto
    /// toda la conversación, y el dueño lo describió como «superestorboso». Tenía razón — quien
    /// habla está mirando su trabajo, no el panel, y que Ü escucha ya lo dicen la cara y la
    /// pastilla de voz sin ocupar sitio.
    /// </remarks>
    /// <param name="cursorEncima">El ratón está sobre el muelle (pestaña o panel desplegado).</param>
    /// <param name="conversacionAbierta">El globo está abierto: hay algo que leer o que escribir.</param>
    /// <param name="tecladoDentro">El foco de teclado vive dentro del muelle: se está escribiendo.</param>
    public static bool Desplegado(bool cursorEncima, bool conversacionAbierta, bool tecladoDentro)
        => cursorEncima || conversacionAbierta || tecladoDentro;

    /// <summary>
    /// Cuánto se perdona al apuntar al muelle, en píxeles alrededor de su caja.
    /// </summary>
    /// <remarks>
    /// La misma regla que ya siguen las pastillas de la carita —blanco de 26 px para un dibujo de
    /// 4,5— dicha aquí para el muelle: el blanco de un gesto no puede ser tan pequeño como su
    /// dibujo, o acertarle se convierte en puntería.
    ///
    /// Y no más: 24 px es aproximadamente el ancho de una barra de desplazamiento. Pasado eso, el
    /// muelle empezaría a tragarse caritas que iban al borde derecho de la pantalla y no a él — y la
    /// carita se lanza sola a ese borde, así que sería el error frecuente, no el raro.
    /// </remarks>
    public const double MargenDeAgarre = 24;

    /// <summary>¿Soltar la carita en <paramref name="suelta"/> la guarda dentro del muelle?</summary>
    /// <param name="muelle">La caja que ocupa el muelle AHORA, en coordenadas de pantalla.</param>
    /// <param name="suelta">Dónde estaba el cursor al soltarla.</param>
    public static bool Guarda(Rect muelle, Point suelta)
        => Rect.Inflate(muelle, MargenDeAgarre, MargenDeAgarre).Contains(suelta);

    /// <summary>
    /// Dónde APARECE la carita en el instante en que tiras de ella: centrada en el cursor.
    /// </summary>
    /// <remarks>
    /// POR QUÉ NO VALE «DONDE ESTABA» (2026-09-06, segunda vuelta del mismo fallo). Guardada, la
    /// carita conserva su último sitio —encima del muelle— y su ventana sigue ahí, solo que oculta.
    /// Al enseñarla en mitad del arrastre aparecía en ESE punto, a varios centímetros de la mano, y
    /// el gesto continuaba desde allí: se leía exactamente como lo describió el dueño, «la carita se
    /// salta a una ubicación diferente y me toca ir a cogerla de nuevo».
    ///
    /// Centrada en el cursor y no con una esquina en él: lo que la persona cree estar agarrando es
    /// la cara, no el vértice de una caja invisible.
    /// </remarks>
    public static Point SitioAlAparecer(Point cursor, Size carita, Rect areaDeTrabajo) =>
        SitioAlSacar(new Point(cursor.X - carita.Width / 2, cursor.Y - carita.Height / 2),
                     carita, areaDeTrabajo);

    /// <summary>
    /// Dónde queda la carita al sacarla del muelle: donde la sueltes, y entera dentro de la pantalla.
    /// </summary>
    /// <remarks>
    /// ES LA EXCEPCIÓN DELIBERADA A «SIEMPRE A UN BORDE». Soltar la carita en cualquier otro sitio la
    /// lanza al lado más cercano, y con razón: vive encima del trabajo de alguien. Pero sacarla del
    /// muelle es un gesto que dice DÓNDE la quieres —si no, bastaba con un botón— y devolverla a un
    /// borde convertiría ese gesto en «reaparece por ahí».
    ///
    /// El acotado no es decoración: el muelle está pegado al borde derecho, así que el sitio natural
    /// para soltarla al sacarla es justo la franja donde la carita se saldría de la pantalla. Volver
    /// de un escondite a un sitio que no se ve no es volver, es perderla.
    /// </remarks>
    /// <param name="suelta">Dónde estaba el cursor al soltarla.</param>
    /// <param name="carita">Lo que ocupa la ventana de la carita.</param>
    /// <param name="areaDeTrabajo">La pantalla útil, sin la barra de tareas.</param>
    public static Point SitioAlSacar(Point suelta, Size carita, Rect areaDeTrabajo)
    {
        double x = Math.Clamp(suelta.X, areaDeTrabajo.Left,
            Math.Max(areaDeTrabajo.Left, areaDeTrabajo.Right - carita.Width));
        double y = Math.Clamp(suelta.Y, areaDeTrabajo.Top,
            Math.Max(areaDeTrabajo.Top, areaDeTrabajo.Bottom - carita.Height));
        return new Point(x, y);
    }
}
