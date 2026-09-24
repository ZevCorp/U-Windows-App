namespace U.WindowsClient.Navigation;

/// <summary>
/// LA PUERTA DE LOS SERVIDORES LOCALES: quién puede hablarles (spec 051, promesa 401).
/// </summary>
/// <remarks>
/// ESCUCHAR SOLO EN 127.0.0.1 NO ES «SOLO ESTA MÁQUINA». El navegador del médico también es esta
/// máquina, y cualquier página abierta en él —un script de terceros incluido— puede pedir
/// <c>http://127.0.0.1:8792/batches</c>. Medido leyendo el código el 2026-09-24: de los DOS servidores
/// locales (el MCP en el 8790 y el del núcleo en el 8792) ninguno miraba el Origin, y el del núcleo
/// contestaba además con CORS abierto, así que una página podía LEER el rastro de batches —que citaba
/// lo escrito en SAP— y, con o sin CORS, hacer que Ü escribiera o despachara una herramienta: un POST
/// con text/plain no pide permiso previo, y los dos servidores leían el cuerpo como JSON sin mirar su
/// tipo.
///
/// LA REGLA, y por qué basta: un navegador SIEMPRE manda Origin cuando una página habla con otro
/// origen, y no le deja a la página falsificarlo. Quien no es navegador —el piloto en Node, el Agent
/// SDK, PowerShell, la propia app— no lo manda. Así que sin Origin se atiende, y con Origin solo si es
/// la página del propio servidor (el visor, que se sirve desde /visor). «null» —una página abierta con
/// file:// o un iframe con sandbox— no es esa página. Y el Host tiene que ser esta máquina en ese
/// puerto: un Host ajeno es un nombre que alguien resolvió a 127.0.0.1 (DNS rebinding), y con él la
/// página ajena pasa a ser «del mismo origen» y el Origin deja de delatarla. Se creía que http.sys
/// cortaba ese Host antes de llegar aquí, porque el prefijo es explícito: MEDIDO el 2026-09-24 con el
/// contrato y la puerta quitada a propósito, un POST con «Host: pagina-ajena.example:PUERTO» contestó
/// 200 y escribió. Esta comprobación no es un segundo cinturón: es la única.
///
/// Pura y en un solo sitio: los dos servidores la llaman ANTES de leer el cuerpo, y dos copias serían
/// dos criterios (aprendizaje nº16).
/// </remarks>
public static class PuertaLocal
{
    /// <summary>Si se atiende, y si no, por qué: cada rechazo dice cuál de sus causas fue (patrón nº2).</summary>
    public readonly record struct Veredicto(bool Pasa, string Porque);

    public static Veredicto Admite(string? origen, string? host, int puerto)
    {
        string h = (host ?? "").Trim();
        if (h.Length == 0) return new(false, "la petición no trae Host");
        if (!EsEstaMaquina(h, puerto))
            return new(false, $"el Host «{h}» no es esta máquina en el puerto {puerto}");

        // Vacío y ausente, aquí, son lo mismo: ningún navegador manda un Origin vacío.
        string o = (origen ?? "").Trim();
        if (o.Length == 0) return new(true, "");
        if (!EsLaPropiaPagina(o, puerto))
            return new(false, $"el Origin «{o}» no es la página de este servidor (http://127.0.0.1:{puerto})");
        return new(true, "");
    }

    private static bool EsEstaMaquina(string host, int puerto) =>
        host.Equals($"127.0.0.1:{puerto}", StringComparison.OrdinalIgnoreCase)
        || host.Equals($"localhost:{puerto}", StringComparison.OrdinalIgnoreCase);

    private static bool EsLaPropiaPagina(string origen, int puerto)
    {
        string o = origen.TrimEnd('/');
        return o.Equals($"http://127.0.0.1:{puerto}", StringComparison.OrdinalIgnoreCase)
            || o.Equals($"http://localhost:{puerto}", StringComparison.OrdinalIgnoreCase);
    }
}
