using System.Windows;

namespace U.WindowsClient.Navigation;

/// <summary>
/// LA CAJA DE UN BOTÓN DE LA BARRA DE UN ALV: la identidad la pone SAP, el rectángulo lo pone UIA.
/// Promesa 144 (spec 009, fase 14).
/// </summary>
/// <remarks>
/// ESTO ESTUVO ESCRITO COMO «HUECO QUE NO SE PUEDE TAPAR» hasta que se le preguntó a la API en vez
/// de creerle al código (aprendizaje nº13). Lo que la sonda de solo lectura midió el 2026-09-03:
///
///   · SAP NO DA LA CAJA, y ahora es un hecho medido y no una creencia heredada.
///     <c>DumpState("Toolbar")</c> sobre el ALV del triage devuelve 85 entradas para 12 botones:
///     los siete getters de siempre —Id, Icon, Type, Enabled, Text, Checked, Tooltip— repetidos por
///     índice, más el contador. 7×12+1 = 85. Los getters geométricos que uno esperaría
///     (<c>GetToolbarButtonLeft/Top/Width/Height/Rect/Bounds</c>) sencillamente NO EXISTEN.
///
///   · UIA SÍ LA DA: «Triage» → <c>Button [793,227 84x30]</c>. Dentro de la ventana de SAP, UIA ve
///     96 elementos y 89 con caja. La creencia de la casa —«dentro de SAP, UIA no ve nada, se queda
///     en un Pane opaco»— es CIERTA para los campos del dynpro y FALSA para la barra de un ALV, que
///     son botones de Windows de verdad. La frase se escribió una vez para el dynpro y se heredó
///     para todo lo demás sin que nadie volviera a mirarlo.
///
/// DE AHÍ LA REGLA: ninguno de los dos puede solo. SAP sabe que ese botón es <c>ZMEDTRIAGE</c> y
/// que se lee «Triage», pero no dónde está; UIA sabe que hay un botón «Triage» en 793,227 pero no
/// cómo se llama para SAP ni cómo pulsarlo sin coordenadas. El puente entre los dos es el TEXTO.
///
/// Y NO SE INVENTA NADA (aprendizaje nº4: una caja que miente es peor que no tener caja). De los 12
/// botones reales, 4 no casaron por nombre en la sonda. Ésos no se dibujan. No se estima su
/// posición por el ancho del texto, ni repartiendo el rectángulo del shell entre los botones: eso
/// sería exactamente la maquinaria de compensación que este repo ya tuvo que borrar una vez
/// (aprendizaje nº6).
///
/// PURO: no habla ni con SAP ni con UIA — se le pasan los dos como delegados. Así el contrato juzga
/// la decisión sin necesitar una pantalla, y el despacho por mundo sigue viviendo donde le toca.
/// </remarks>
public static class LaCajaDeUnBotonDeBarra
{
    /// <summary>La marca que distingue a un botón de barra dentro de un selector de SAP.</summary>
    private const string Marca = "#tbbtn=";

    /// <summary>
    /// El rectángulo de ese botón, o null si no se sabe con certeza.
    /// </summary>
    /// <param name="selector">El selector de SAP. Si no es de barra, esto no es asunto suyo.</param>
    /// <param name="comoSeLee">Selector → los nombres con los que SAP declara ese botón (su texto y
    /// su tooltip). Dos, porque UIA a veces toma uno y a veces el otro.</param>
    /// <param name="cajaPorNombre">Nombre → la caja que UIA le da, o null si no lo encuentra.</param>
    public static Rect? De(string selector,
        Func<string, IReadOnlyList<string>> comoSeLee,
        Func<string, Rect?> cajaPorNombre)
    {
        string sel = (selector ?? "").Trim();
        // SOLO BOTONES DE BARRA. Un campo del dynpro sigue siendo de SAP, y preguntarle a UIA por él
        // devuelve el Pane opaco de siempre: volver a intentarlo por esta puerta sería reabrir el
        // agujero que la promesa 68 cerró.
        if (sel.IndexOf(Marca, StringComparison.OrdinalIgnoreCase) < 0) return null;
        if (comoSeLee == null || cajaPorNombre == null) return null;

        var nombres = comoSeLee(sel) ?? Array.Empty<string>();
        foreach (string n in nombres)
        {
            if (string.IsNullOrWhiteSpace(n)) continue;
            var caja = cajaPorNombre(n.Trim());
            if (caja is { Width: > 0, Height: > 0 }) return caja;
        }
        return null;   // nadie lo encontró: no se dibuja, y no se estima
    }

    /// <summary>¿Este selector nombra un botón de la barra de un shell?</summary>
    public static bool EsDeBarra(string selector) =>
        (selector ?? "").IndexOf(Marca, StringComparison.OrdinalIgnoreCase) >= 0;
}
