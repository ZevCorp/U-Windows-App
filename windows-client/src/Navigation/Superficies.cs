namespace U.WindowsClient.Navigation;

/// <summary>
/// ¿SON LA MISMA PANTALLA? Promesa 226 (spec 019). Puro. La única regla de igualdad entre dos
/// identidades de superficie que el batch y el juez consultan.
/// </summary>
/// <remarks>
/// LA PANTALLA A MEDIO CAMBIAR. Medido el 2026-09-11: la demo grabó que Favoritos lleva a
/// «sapgui://QAS/SESSION_MANAGER/SAPLN_WP_FRAMEWORK/0100» y el piloto llegó a
/// «sapgui://QAS/NWP1/SAPLN_WP_FRAMEWORK/0100». SAP cambia el programa un instante antes que el
/// código de transacción: durante ese instante la sesión dice «SESSION_MANAGER» (la transacción de
/// Easy Access) con un programa que ya no es el de Easy Access. El terreno cogió la pantalla así, y
/// una comprobación de doce minutos quedó en «17 de 19» por una comparación de cadenas.
///
/// LA REGLA ES ESTRECHA A PROPÓSITO: solo SESSION_MANAGER con un programa que NO es
/// SAPLSMTR_NAVIGATION cuenta como transitorio. Dos transacciones reales distintas sobre el mismo
/// programa (NWP1 y NV2000) siguen siendo pantallas distintas: ahí el código de transacción sí
/// dice algo. Y Easy Access de verdad (SESSION_MANAGER + SAPLSMTR_NAVIGATION) no casa con nada
/// que no sea Easy Access.
///
/// VIVE EN UN SITIO porque estaba en tres —el rescate, y dos veces el batch— comparando con
/// Equals: una comparación entre identidades de distinta forma da falso siempre y en silencio
/// (aprendizaje nº16), y arreglarla en dos de tres es el aprendizaje nº11.
/// </remarks>
public static class Superficies
{
    public static bool MismaPantalla(string a, string b)
    {
        string x = (a ?? "").Trim(), y = (b ?? "").Trim();
        if (x.Length == 0 || y.Length == 0) return false;
        if (x.Equals(y, StringComparison.OrdinalIgnoreCase)) return true;
        return Partes(x) is { } px && Partes(y) is { } py
            && px.Sistema.Equals(py.Sistema, StringComparison.OrdinalIgnoreCase)
            && px.Resto.Equals(py.Resto, StringComparison.OrdinalIgnoreCase)
            && (EsTransitoria(px) || EsTransitoria(py));
    }

    private const string TransaccionDeEasyAccess = "SESSION_MANAGER";
    private const string ProgramaDeEasyAccess = "SAPLSMTR_NAVIGATION";

    private static bool EsTransitoria((string Sistema, string Transaccion, string Resto) p) =>
        p.Transaccion.Equals(TransaccionDeEasyAccess, StringComparison.OrdinalIgnoreCase)
        && !p.Resto.StartsWith(ProgramaDeEasyAccess, StringComparison.OrdinalIgnoreCase);

    /// <summary>«sapgui://SIS/TCODE/PROGRAMA/DYNPRO[/…]» → (SIS, TCODE, PROGRAMA/DYNPRO[/…]), o null si no es una pantalla de SAP.</summary>
    private static (string Sistema, string Transaccion, string Resto)? Partes(string id)
    {
        const string prefijo = "sapgui://";
        if (!id.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase)) return null;
        var trozos = id[prefijo.Length..].Split('/', 3);
        if (trozos.Length < 3 || trozos[0].Length == 0 || trozos[1].Length == 0 || trozos[2].Length == 0) return null;
        return (trozos[0], trozos[1], trozos[2]);
    }
}
