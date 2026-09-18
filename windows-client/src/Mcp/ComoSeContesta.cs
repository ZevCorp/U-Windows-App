namespace U.WindowsClient.Mcp;

/// <summary>
/// CÓMO SE CONTESTA UN ACTO: con lo que pasó, y detrás con lo que quedó delante. Promesa 263 (spec 029). Pura.
/// </summary>
/// <remarks>
/// LA MITAD DE LOS ACTOS IBAN SEGUIDOS DE «¿Y AHORA QUÉ HAY?». Medido en tres días de corridas (15–17 de septiembre
/// de 2026): 214 actos, 106 seguidos inmediatamente de un map_what_i_see o un map_where_am_i, porque escribir, ir y
/// abrir contestaban «escribí X» o «te puse delante» sin decir qué quedó delante. Cada una de esas vueltas son
/// unos 3 s de modelo, que es la parte del reloj que no se puede acelerar de otra forma —nuestro código ya era el
/// 20%—.
///
/// TAMBIÉN CUANDO NO PUDO. La promesa 38 ya dice que «si no se pudo, se dice QUÉ hay ahora»: el inventario es
/// exactamente eso, y saber qué hay delante tras un fallo es lo que permite elegir otra puerta sin otra vuelta.
///
/// Y NO CUANDO NI LLEGÓ A ACTUAR: si faltó un argumento o el núcleo no está conectado, pegar la pantalla sería
/// contestar a una pregunta que nadie hizo. Un saber tampoco lo repite: ya es lo que contesta.
/// </remarks>
public static class ComoSeContesta
{
    /// <summary>Lo que hace avanzar una tarea. Lo demás son saberes o aprender.</summary>
    // map_decidir es un acto aunque el decisor no se atreva: entonces el inventario es justo lo que Luna
    // necesita para elegir ella (promesa 286).
    public static readonly string[] Actos = { "map_take", "map_type", "map_go_to", "map_open_app", "map_scroll", "map_unblock", "map_decidir" };

    /// <summary>La primera línea con la que empieza el inventario de map_what_i_see. Es la marca de «ya lo lleva».</summary>
    public const string MarcaDelInventario = "EN PANTALLA AHORA";

    public static bool EsActo(string herramienta) =>
        Actos.Any(a => a.Equals((herramienta ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

    public static bool LlevaInventario(string herramienta, string resultado)
    {
        if (!EsActo(herramienta)) return false;
        string r = (resultado ?? "").TrimStart();
        if (r.Length == 0) return false;
        // Ni llegó a actuar: le faltó un argumento, la herramienta no existe, o el núcleo no está conectado.
        if (r.StartsWith("falta ", StringComparison.OrdinalIgnoreCase)) return false;
        if (r.StartsWith("herramienta de mapa no soportada", StringComparison.OrdinalIgnoreCase)) return false;
        if (r.StartsWith("todavía no sé", StringComparison.OrdinalIgnoreCase)) return false;
        // Ya lo lleva: no se pega dos veces.
        if (r.Contains(MarcaDelInventario, StringComparison.Ordinal)) return false;
        return true;
    }

    /// <summary>Lo que pasó va PRIMERO; el inventario detrás, separado. Sin inventario de verdad no se pega ruido.</summary>
    public static string Pegar(string resultado, string inventario)
    {
        string inv = (inventario ?? "").Trim();
        if (!inv.StartsWith(MarcaDelInventario, StringComparison.Ordinal)) return resultado;
        return (resultado ?? "").TrimEnd() + "\n\n" + inv;
    }
}
