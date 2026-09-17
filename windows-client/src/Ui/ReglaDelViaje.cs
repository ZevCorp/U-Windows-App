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
///
/// EL VIAJE ES UN PLAN DE PASOS FIJO, decidido aquí y ejecutado por <see cref="ViajeDeEscritorio"/>:
/// se fija la ventana en todos los escritorios (si el sistema deja), se mueven las ventanas propias
/// al destino, se cambia de escritorio con el atajo del sistema tantas veces como distancia haya en
/// el orden de la vista de tareas, se espera a que el registro diga que el actual ES el destino, y se
/// suelta la fijación. A uno nuevo, primero se crea (el atajo crea Y cambia) y se lee su GUID del
/// registro; solo entonces se mueven las ventanas. Llegar lo dice el sistema, no el botón
/// (<see cref="Llegue"/>): pulsar un atajo no es haber llegado. Y si no se llega, se deshace
/// (<see cref="Deshacer"/>): las ventanas vuelven al origen y se vuelve a él, sin fijar nada.
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

    // ── El viaje (promesa 274) ───────────────────────────────────────────────

    public const string Fijar = "fijar", Mover = "mover", Esperar = "esperar", SoltarFijacion = "soltar-fijacion",
                        Crear = "crear", LeerNuevo = "leer-nuevo";

    /// <summary>El paso de cambiar de escritorio con su distancia: «cambiar:+2» son dos a la derecha.</summary>
    public static string Cambiar(int distancia) => $"cambiar:{(distancia >= 0 ? "+" : "")}{distancia}";

    /// <summary>La distancia de un paso «cambiar:±N», o 0 si el paso no es de cambiar.</summary>
    public static int Distancia(string paso)
        => paso != null && paso.StartsWith("cambiar:", StringComparison.Ordinal)
           && int.TryParse(paso.AsSpan(8), out int d) ? d : 0;

    /// <param name="origen">Donde está el centro de operaciones ahora.</param>
    /// <param name="destino">A dónde va; se ignora si <paramref name="nuevo"/>.</param>
    /// <param name="orden">Los escritorios en el orden de la vista de tareas.</param>
    /// <param name="nuevo">Ir a uno que todavía no existe.</param>
    public static string[] Plan(Guid origen, Guid destino, Guid[] orden, bool nuevo)
    {
        if (nuevo) return new[] { Fijar, Crear, LeerNuevo, Mover, Esperar, SoltarFijacion };
        int d = Array.IndexOf(orden, destino) - Array.IndexOf(orden, origen);
        return new[] { Fijar, Mover, Cambiar(d), Esperar, SoltarFijacion };
    }

    /// <summary>Llegar es que el registro diga que el actual ES el destino. Nada más lo declara.</summary>
    public static bool Llegue(Guid actualDelRegistro, Guid destino) => destino != Guid.Empty && actualDelRegistro == destino;

    /// <summary>Si no se llega: las ventanas vuelven al origen y se vuelve a él, sin fijar nada.</summary>
    public static string[] Deshacer(Guid origen, Guid destino, Guid[] orden)
    {
        int d = Array.IndexOf(orden, origen) - Array.IndexOf(orden, destino);
        return new[] { Mover, Cambiar(d), Esperar };
    }
}
