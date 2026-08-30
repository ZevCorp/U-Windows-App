namespace U.Graph.Surfaces;

/// <summary>
/// EN EL PUESTO DE TRABAJO, LA VISTA ELEGIDA ES PARTE DEL LUGAR (promesa 79).
/// </summary>
/// <remarks>
/// El bloqueo de la ronda 3 (2026-08-30): «Triage» y «Consulta» son las dos el visor genérico de
/// listas (<c>ssubVIEW_SCREEN:SAPLN1LSTAMB:0007</c>) y compartían identidad — el usuario cambiaba
/// de vista, el localizador no veía salto, y sin salto no hay arista que aprender. El remark de
/// <c>Identity</c> lo anticipó: «si algún día dos paneles distintos resultan indistinguibles sin
/// subdynpro, se extiende AQUÍ». Llegó el día.
///
/// El discriminador es semántico, no un hash: en el Puesto, la fila seleccionada del árbol de
/// navegación ES la vista que la pantalla muestra — cambiarla ES navegar, y su texto es como el
/// operador nombra el sitio. FUERA del patrón del Puesto no se toca nada: en Easy Access la
/// selección cambia sin navegar, y una identidad que aletea con cada clic es peor que una gruesa.
/// </remarks>
public static class LaVistaEsElLugarDelPuesto
{
    /// <summary>El patrón del visor genérico del Puesto: solo ahí la vista discrimina.</summary>
    private const string PatronDelPuesto = "ssubVIEW_SCREEN:";

    /// <param name="subdynpro">El subdynpro del área de usuario (lo que Identity ya extrae).</param>
    /// <param name="seleccionDelArbol">El texto de la fila seleccionada del árbol de navegación.</param>
    /// <returns>«vista:X» para añadir al lugar, o vacío si aquí la vista no discrimina.</returns>
    public static string Sufijo(string subdynpro, string seleccionDelArbol)
    {
        if (!(subdynpro ?? "").StartsWith(PatronDelPuesto, StringComparison.OrdinalIgnoreCase))
            return "";

        string vista = (seleccionDelArbol ?? "").Trim();
        if (vista.Length == 0) return "";

        // El separador de la identidad no puede venir dentro del nombre.
        vista = vista.Replace("/", " ");
        while (vista.Contains("  ", StringComparison.Ordinal))
            vista = vista.Replace("  ", " ");

        return "vista:" + vista.Trim();
    }
}
