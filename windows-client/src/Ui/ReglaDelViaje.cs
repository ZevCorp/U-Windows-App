using U.Graph.Surfaces;

namespace U.WindowsClient.Ui;

/// <summary>
/// LAS REGLAS DE LLEVAR LA CARITA A OTRO ESCRITORIO. Promesas 273 y 274 (spec 031). Puras.
/// </summary>
/// <remarks>
/// LOS DESTINOS SE OFRECEN POR SU NOMBRE, que es como la persona piensa en sus escritorios: en esta
/// máquina, el 2026-09-17, «Anuncios», «Whatsapp» y «SEO», y dos sin nombre. Los sin nombre se
/// llaman «Escritorio N» por su posición en la vista de tareas, que es como los llama Windows. El
/// actual no se ofrece —llevarla a donde ya está no es un viaje— y «Uno nuevo» va siempre al final.
///
/// EL BOTÓN ES DE LA CONSULTA. Sentada en el muelle la carita no viaja: el muelle es un escondite
/// pegado al borde, no el centro de operaciones.
/// </remarks>
public static class ReglaDelViaje
{
    public const string UnoNuevo = "Uno nuevo";

    /// <param name="escritorios">Los escritorios en el orden de la vista de tareas.</param>
    /// <param name="nombres">El nombre puesto a cada uno, en el mismo orden; vacío si no tiene.</param>
    /// <param name="actual">El escritorio a la vista, que no se ofrece.</param>
    public static string[] Destinos(Guid[] escritorios, string[] nombres, Guid actual)
    {
        var lista = new List<string>();
        if (escritorios != null)
        {
            for (int i = 0; i < escritorios.Length; i++)
            {
                if (escritorios[i] == actual) continue;
                string? nombre = nombres != null && i < nombres.Length ? nombres[i] : null;
                lista.Add(ReglaDelEscritorio.NombreDe(escritorios[i], escritorios, nombre));
            }
        }
        lista.Add(UnoNuevo);
        return lista.ToArray();
    }

    /// <summary>¿Hay botón de llevar? Solo con la carita sentada, y solo en la consulta.</summary>
    public static bool HayBoton(bool sillaOcupada, string? anfitrion)
        => sillaOcupada && string.Equals(anfitrion, "consulta", StringComparison.Ordinal);
}
