namespace U.Graph.Surfaces;

/// <summary>
/// ¿CUÁL de las sesiones SAP es «la nuestra»? La de la ventana que está DELANTE.
/// </summary>
/// <remarks>
/// Lo destapó el piloto con dos ventanas SAP abiertas (2026-08-26): una sesión buena y un login
/// paralelo. <c>Session()</c> tomaba «la primera sesión de la primera conexión», así que el
/// localizador acuñó la identidad DE LA OTRA VENTANA («sapgui://QAS/S000/SAPMSYST/0020», la
/// pantalla de login) mientras delante había otra cosa. Una identidad que describe otra ventana es
/// la mentira más desorientadora posible: la compuerta, el batch y las aristas se apoyan en ella.
///
/// PURO A PROPÓSITO: recibe los handles y decide. El COM (sacar el handle de cada
/// <c>wnd[0]</c>) vive en <see cref="SapGuiSurface"/>; la decisión se juzga sin SAP (promesa 72).
/// </remarks>
public static class CualSesion
{
    /// <summary>
    /// El índice de la sesión elegida, o -1 si no hay ninguna honesta.
    /// </summary>
    /// <param name="ventanas">El handle de la ventana principal de cada sesión, en su orden.</param>
    /// <param name="delante">El handle de la ventana que está delante ahora.</param>
    /// <remarks>
    /// Sin casar y con UNA sola sesión, esa: es el caso de siempre (una ventana SAP, el foco en
    /// cualquier parte) y las sondas que corren con la terminal delante viven de él. Sin casar y
    /// con VARIAS, ninguna — «no sé» es mejor que la identidad de otra ventana.
    /// </remarks>
    public static int Elige(IReadOnlyList<long> ventanas, long delante)
    {
        if (ventanas == null || ventanas.Count == 0) return -1;

        for (int i = 0; i < ventanas.Count; i++)
            if (ventanas[i] != 0 && ventanas[i] == delante) return i;

        return ventanas.Count == 1 ? 0 : -1;
    }
}
