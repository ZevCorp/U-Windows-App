using System.Windows;

namespace U.WindowsClient.Ui;

/// <summary>
/// EN QUÉ ANFITRIÓN CAE LA CARITA AL SOLTARLA. Promesa 272 (spec 031). Pura.
/// </summary>
/// <remarks>
/// LA MISMA REGLA QUE EL MUELLE, aplicada a varios: cada caja se pregunta con
/// <see cref="ReglaDelMuelle.Guarda"/>, con su mismo margen de agarre, y gana la PRIMERA en orden Z
/// que contenga el punto. El orden importa donde se solapan: el muelle es topmost y vive pegado al
/// borde derecho; si la consulta está debajo de él, lo que la persona ve al soltar es el muelle, y
/// es el muelle quien se la queda. Una caja vacía es un anfitrión que no se ve —consulta minimizada
/// o escondida— y no cuenta: <c>Rect.Inflate</c> sobre <c>Rect.Empty</c> lanza, y un anfitrión
/// invisible no puede recibir un gesto.
/// </remarks>
public static class ReglaDelAnfitrion
{
    /// <param name="cajasEnOrdenZ">Las cajas de los anfitriones, de delante hacia atrás.</param>
    /// <param name="suelta">Dónde estaba el cursor al soltarla.</param>
    /// <returns>El índice del anfitrión que se la queda, o -1 si no cae en ninguno.</returns>
    public static int Elegir(Rect[] cajasEnOrdenZ, Point suelta)
    {
        if (cajasEnOrdenZ == null) return -1;
        for (int i = 0; i < cajasEnOrdenZ.Length; i++)
        {
            if (cajasEnOrdenZ[i].IsEmpty) continue;
            if (ReglaDelMuelle.Guarda(cajasEnOrdenZ[i], suelta)) return i;
        }
        return -1;
    }
}
