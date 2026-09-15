namespace U.Graph.Surfaces;

/// <summary>
/// CUÁL ES «LA VENTANA DE DELANTE», con una sola regla para todos. Promesa 230 (spec 020). Pura.
/// </summary>
/// <remarks>
/// HABÍA DOS REGLAS PARA LA MISMA PREGUNTA, y se contradijeron el 2026-09-14: el localizador tomaba
/// la ventana con el foco y, cuando esa era la carita, devolvía la última ubicación recordada sin
/// recalcular nada; el lector de elementos bajaba por el orden Z hasta la primera ventana ajena. Tras
/// cerrar la Tienda de Microsoft con la carita delante, Ü dijo durante seis segundos «estás en
/// microsoft-store» —la ubicación recordada— mientras listaba los elementos de OTRA ventana —la que
/// el lector encontró—. Una comparación entre dos formas de decidir lo mismo da falso en silencio
/// (aprendizaje nº16).
///
/// LA REGLA: desde la ventana que tiene el foco, bajando por el orden Z, la primera que esté visible,
/// tenga título y no sea de Ü. Si no hay ninguna, NO HAY —ni la de Ü, ni la recordada—, y quien
/// pregunta lo sabe. Todo lo que hace falta para decidirlo se inyecta, para que la regla se pueda
/// juzgar sin escritorio y para que las dos capas (cliente y mapeador) la usen con sus propios
/// medios sin copiarla.
/// </remarks>
public static class VentanaDeDelante
{
    /// <param name="foco">La ventana con el foco del sistema.</param>
    /// <param name="visible">¿Se ve? (visible y no encubierta por el gestor de ventanas).</param>
    /// <param name="conTitulo">¿Tiene título? Una sin título no identifica ninguna pantalla.</param>
    /// <param name="propia">¿Es de Ü? La carita, las tarjetas y el recuadro nunca son «la ventana de delante».</param>
    /// <param name="siguiente">La siguiente en el orden Z, o cero si no hay más.</param>
    /// <param name="tope">Cuántas se miran como mucho: el orden Z de un escritorio es largo y una cadena rota podría dar vueltas.</param>
    /// <returns>La ventana de delante, o cero si no hay ninguna que cuente.</returns>
    public static IntPtr Elegir(IntPtr foco, Func<IntPtr, bool> visible, Func<IntPtr, bool> conTitulo,
        Func<IntPtr, bool> propia, Func<IntPtr, IntPtr> siguiente, int tope = 50)
    {
        IntPtr h = foco;
        for (int i = 0; i < tope && h != IntPtr.Zero; i++)
        {
            bool cuenta;
            try { cuenta = !propia(h) && visible(h) && conTitulo(h); }
            catch { cuenta = false; }
            if (cuenta) return h;
            IntPtr sig;
            try { sig = siguiente(h); } catch { sig = IntPtr.Zero; }
            if (sig == h) break;   // una cadena que se muerde la cola no es una cadena
            h = sig;
        }
        return IntPtr.Zero;
    }
}
