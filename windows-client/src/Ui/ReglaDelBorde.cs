using System.Windows;

namespace U.WindowsClient.Ui;

/// <summary>Por dónde se está agarrando una ventana para cambiarle el tamaño.</summary>
public enum ZonaDelBorde
{
    Ninguna,
    Izquierda, Derecha, Arriba, Abajo,
    ArribaIzquierda, ArribaDerecha, AbajoIzquierda, AbajoDerecha,
}

/// <summary>
/// QUÉ BORDE SE ESTÁ AGARRANDO.
/// </summary>
/// <remarks>
/// POR QUÉ HACE FALTA CALCULARLO (2026-09-06, lo pidió el dueño: «que la pueda coger de los
/// bordes/esquinas y cambiar su tamaño»). La ventana ya tenía <c>ResizeMode.CanResize</c> puesto y
/// aun así no se dejaba agarrar, y las dos razones se suman:
///
///   · con <c>WindowStyle.None</c> y <c>AllowsTransparency</c> no hay área NO CLIENTE: Windows no
///     tiene marco donde poner los tiradores, así que <c>CanResize</c> queda de adorno y hay que
///     contestar a <c>WM_NCHITTEST</c> a mano;
///   · y el borde que la persona VE no es el borde de la ventana. La tarjeta vive 24 px por dentro,
///     que es el hueco que la sombra necesita (promesa 154). Medir contra la ventana pondría el
///     tirador en el aire transparente, a dos centímetros de donde está el dibujo.
///
/// LAS ESQUINAS GANAN A LOS LADOS, y no es un detalle: en una esquina las dos condiciones se
/// cumplen a la vez, así que quien pregunte por los lados primero devolverá «izquierda» en un punto
/// que la persona ve claramente como esquina — y redimensionar en una sola dirección cuando
/// esperabas dos se siente como que la ventana se resiste.
/// </remarks>
public static class ReglaDelBorde
{
    /// <summary>
    /// Cuánto se perdona a cada lado del filete. Ocho a cada lado da una franja de 16, que es lo
    /// que Windows usa para sus propios bordes: menos exige puntería y más se come el contenido.
    /// </summary>
    public const double Agarre = 8;

    /// <param name="p">El punto, en las mismas coordenadas que <paramref name="tarjeta"/>.</param>
    /// <param name="tarjeta">La caja de lo que se VE, no la de la ventana.</param>
    /// <param name="agarre">Cuánto se perdona; por defecto <see cref="Agarre"/>.</param>
    public static ZonaDelBorde De(Point p, Rect tarjeta, double agarre = Agarre)
    {
        bool izq = Math.Abs(p.X - tarjeta.Left) <= agarre;
        bool der = Math.Abs(p.X - tarjeta.Right) <= agarre;
        bool arr = Math.Abs(p.Y - tarjeta.Top) <= agarre;
        bool aba = Math.Abs(p.Y - tarjeta.Bottom) <= agarre;

        // Fuera de la caja del todo —más allá del margen— no hay nada que agarrar: ahí está la
        // sombra, y la sombra no es un tirador.
        var conMargen = Rect.Inflate(tarjeta, agarre, agarre);
        if (!conMargen.Contains(p)) return ZonaDelBorde.Ninguna;

        // Las esquinas PRIMERO. Ver el comentario de arriba.
        if (arr && izq) return ZonaDelBorde.ArribaIzquierda;
        if (arr && der) return ZonaDelBorde.ArribaDerecha;
        if (aba && izq) return ZonaDelBorde.AbajoIzquierda;
        if (aba && der) return ZonaDelBorde.AbajoDerecha;

        if (izq) return ZonaDelBorde.Izquierda;
        if (der) return ZonaDelBorde.Derecha;
        if (arr) return ZonaDelBorde.Arriba;
        if (aba) return ZonaDelBorde.Abajo;

        return ZonaDelBorde.Ninguna;
    }
}
