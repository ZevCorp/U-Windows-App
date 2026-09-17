namespace U.Graph.Surfaces;

/// <summary>
/// LAS REGLAS DEL ESCRITORIO VIRTUAL, sin sistema debajo. Promesa 270 (spec 031). Pura.
/// </summary>
/// <remarks>
/// EL ORDEN DE LOS ESCRITORIOS ES EL DEL REGISTRO, y no hay otra fuente pública: Windows no da API
/// para enumerarlos ni para saber cuál está a la vista, pero escribe en
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops</c> la lista
/// (<c>VirtualDesktopIDs</c>: 16 bytes por GUID, pegados, en el orden de la vista de tareas) y el
/// actual (<c>CurrentVirtualDesktop</c>). Medido el 2026-09-17 en esta máquina: cinco escritorios,
/// tres con nombre. Leer el blob es lo único que hace <see cref="Orden"/>; lo que sobre del último
/// múltiplo de 16 no es un escritorio y no se inventa.
///
/// «DESCOLOCADA» ES LA QUE ESTÁ EN OTRO ESCRITORIO, no la que no está en ninguno. La API contesta
/// vacío para ventanas que no son de nadie (Program Manager, la ventana de entrada de texto), y
/// moverlas sería adivinar: se dejan en paz. Solo se trae la que el sistema ubica en un escritorio
/// distinto del del asistente.
/// </remarks>
public static class ReglaDelEscritorio
{
    /// <summary>Los GUID del blob del registro, enteros y en su orden.</summary>
    public static Guid[] Orden(byte[] blob)
    {
        if (blob == null || blob.Length < 16) return Array.Empty<Guid>();
        var lista = new Guid[blob.Length / 16];
        for (int i = 0; i < lista.Length; i++)
        {
            var trozo = new byte[16];
            Array.Copy(blob, i * 16, trozo, 0, 16);
            lista[i] = new Guid(trozo);
        }
        return lista;
    }

    /// <summary>
    /// Qué ventanas hay que traer al escritorio del asistente: las que el sistema ubica en OTRO.
    /// </summary>
    /// <param name="ventanas">Las ventanas propias, en cualquier orden.</param>
    /// <param name="donde">El escritorio de cada una, en el mismo orden; <see cref="Guid.Empty"/> si el sistema no la ubica.</param>
    /// <param name="mio">El escritorio del asistente.</param>
    public static IntPtr[] Descolocadas(IntPtr[] ventanas, Guid[] donde, Guid mio)
    {
        if (ventanas == null || donde == null) return Array.Empty<IntPtr>();
        var traer = new List<IntPtr>();
        int n = Math.Min(ventanas.Length, donde.Length);
        for (int i = 0; i < n; i++)
        {
            if (donde[i] == Guid.Empty) continue;   // sin escritorio no está «en otro»: no se toca
            if (donde[i] != mio) traer.Add(ventanas[i]);
        }
        return traer.ToArray();
    }

    /// <summary>
    /// Cómo se llama un escritorio para la persona: su nombre si lo tiene, y si no «Escritorio N»,
    /// con N su posición en el orden de la vista de tareas (desde 1, que es como los cuenta Windows).
    /// </summary>
    public static string NombreDe(Guid id, Guid[] orden, string? nombre)
    {
        if (!string.IsNullOrWhiteSpace(nombre)) return nombre.Trim();
        int i = Array.IndexOf(orden ?? Array.Empty<Guid>(), id);
        return i >= 0 ? $"Escritorio {i + 1}" : "Escritorio";
    }
}
