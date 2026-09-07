namespace U.WindowsClient.Piloto;

/// <summary>
/// LA CAJA DE HERRAMIENTAS DEL PILOTO. Promesa 174 (spec 013). Puro: nombres dentro, nombres fuera.
/// </summary>
/// <remarks>
/// UNA SOLA CAJA, con manos desde el principio. La primera versión tenía dos —entender sin manos,
/// hacer con ellas— por analogía con el aprendiz de la promesa 138. La analogía era falsa: allí
/// Ü no tiene manos porque la PERSONA conduce y no hay que estorbarle; al comprobar conduce el
/// piloto. Y había un hecho del código en contra: un recuerdo se cuelga de un par (pantalla,
/// selector), así que para colgar el del botón Triage hay que ESTAR en Urgencias Adultos. Entender
/// sin manos no podía colgar ni un recuerdo bien puesto. El dueño lo vio en la primera corrida
/// (2026-09-06): «abrir SAP no fue acertado? yo creo que sí». Lo era.
///
/// LO ÚNICO QUE SE PROHÍBE ES LO QUE VA EN TANDA. Comprobar es de uno en uno con juez en medio
/// (promesas 141 y 175): <c>map_batch</c> haría diez cosas y dejaría al juez sin nada que juzgar;
/// <c>map_skill_run</c> reproduciría la skill que estamos comprobando.
///
/// OFRECER MENOS NO ES PROHIBIR: el Agent SDK trae búsqueda de herramientas y lo que no se ofrece
/// se puede encontrar (medido el 2026-09-06: llamó lo que no estaba en la lista). Por eso van dos
/// listas, y la de prohibidas es la que manda.
///
/// LOS NOMBRES VAN COMO LOS VE EL AGENT SDK: <c>mcp__&lt;servidor&gt;__&lt;herramienta&gt;</c>. El
/// servidor de la app se llama «u» y el del propio piloto (ver_momento) «leccion».
/// </remarks>
public static class CajasDelPiloto
{
    public const string ServidorDeLaApp = "u";
    public const string ServidorDeLaLeccion = "leccion";

    /// <summary>Lo que hace varias cosas de golpe o reproduce una skill: fuera de la caja.</summary>
    public static readonly IReadOnlySet<string> EnTanda = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "map_batch", "map_skill_run",
    };

    /// <summary>La caja del piloto: todo lo de la app menos lo que va en tanda, más mirar la demo.</summary>
    public static IReadOnlyList<string> Caja(IEnumerable<string> todasLasDeLaApp) =>
        (todasLasDeLaApp ?? Array.Empty<string>())
            .Where(n => !EnTanda.Contains(n))
            .Select(n => $"mcp__{ServidorDeLaApp}__{n}")
            .Append($"mcp__{ServidorDeLaLeccion}__ver_momento")
            .Append($"mcp__{ServidorDeLaLeccion}__ver_alrededor")
            .Distinct().ToList();

    /// <summary>Lo prohibido por su nombre: lo que va en tanda.</summary>
    public static IReadOnlyList<string> Prohibidas(IEnumerable<string> todasLasDeLaApp) =>
        (todasLasDeLaApp ?? Array.Empty<string>())
            .Where(n => EnTanda.Contains(n))
            .Select(n => $"mcp__{ServidorDeLaApp}__{n}")
            .Distinct().ToList();
}
