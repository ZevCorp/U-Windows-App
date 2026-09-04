using System.Windows;

namespace U.WindowsClient.Navigation;

/// <summary>
/// DÓNDE ESTÁ EN PANTALLA UN SELECTOR DE SAP. El único sitio donde se decide. Promesa 145.
/// </summary>
/// <remarks>
/// HABÍA DOS COPIAS DE ESTE BUCLE, y se encontraron al cerrar la promesa 144 (aprendizaje nº5:
/// arreglar la CLASE de error y contar cuántos sitios la tienen — el número va en el commit). La
/// vista de recuerdos de golpe y el señalar de uno recorrían cada una su propio bucle sobre la
/// misma lista de cajas.
///
/// MIENTRAS LAS DOS HACÍAN LO MISMO, no pasaba nada. Dejaron de hacerlo en cuanto los botones de
/// barra empezaron a entrar en la lista SIN caja —a propósito: SAP no se la da, y lo que aportan es
/// su rótulo para que UIA los encuentre (promesa 144)—. Una copia los saltaba y la otra los daba
/// por buenos, y esa habría iluminado un rectángulo vacío en la esquina de la pantalla: la caja que
/// miente del aprendizaje nº4, servida por el camino que menos se mira.
///
/// LA ESCALERA, en orden y sin atajos:
///
///   1. LA CAJA QUE SAP DIO, si la dio con tamaño. Es la buena: viene del propio dynpro.
///   2. EL PUENTE CON UIA para los botones de barra, casando por el rótulo que SAP declara
///      (<see cref="LaCajaDeUnBotonDeBarra"/>).
///   3. NADA. Ni la caja vacía, ni el rectángulo del shell, ni una estimación por el ancho del
///      texto: un elemento cuya posición no se sabe no se ilumina, y punto.
///
/// PURO: la lista de cajas y el buscador de UIA llegan como argumentos, así que el contrato juzga
/// la escalera entera sin necesitar una pantalla ni una sesión de SAP.
/// </remarks>
public static class LaCajaDeUnaIdentidadDeSap
{
    /// <param name="cajas">Lo que SAP dice que hay en pantalla: selector, rótulo, tipo y caja. Un
    /// botón de barra entra aquí con su rótulo y con la caja VACÍA — es lo único que SAP sabe de
    /// él.</param>
    /// <param name="cajaPorNombreEnUia">Rótulo → la caja que UIA le da, o null.</param>
    public static Rect? De(string selector,
        IReadOnlyList<(string Selector, string Etiqueta, string Tipo, Rect Caja)> cajas,
        Func<string, Rect?> cajaPorNombreEnUia)
    {
        string sel = (selector ?? "").Trim();
        if (sel.Length == 0 || cajas == null) return null;
        string busco = U.Graph.Surfaces.SapSelector.Normalize(sel);

        // 1. La que SAP dio, si la dio de verdad. `Rect.Empty` no es una posición: es la ausencia
        //    de una, y devolverla dibujaría un recuadro en la esquina.
        foreach (var c in cajas)
            if (U.Graph.Surfaces.SapSelector.Normalize(c.Selector).Equals(busco, StringComparison.OrdinalIgnoreCase)
                && c.Caja.Width > 0 && c.Caja.Height > 0)
                return c.Caja;

        // 2. El puente con UIA, solo para los botones de barra y solo por el rótulo que SAP declara.
        return LaCajaDeUnBotonDeBarra.De(sel,
            comoSeLee: s =>
            {
                string id = U.Graph.Surfaces.SapSelector.Normalize(s);
                var nombres = new List<string>();
                foreach (var c in cajas)
                    if (U.Graph.Surfaces.SapSelector.Normalize(c.Selector).Equals(id, StringComparison.OrdinalIgnoreCase)
                        && (c.Etiqueta ?? "").Trim().Length > 0)
                        nombres.Add(c.Etiqueta.Trim());
                return nombres;
            },
            cajaPorNombre: cajaPorNombreEnUia ?? (_ => null));

        // 3. Si nada de lo anterior contestó, `De` ya devolvió null. No hay peldaño 3 con relleno.
    }
}
