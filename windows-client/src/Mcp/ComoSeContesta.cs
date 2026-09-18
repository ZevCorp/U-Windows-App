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

    // ── Tras navegar se cuenta la página, no su esqueleto (promesa 335, spec 044) ────────────────

    /// <summary>Los actos que pueden dejar una página cargando. `map_scroll` y `map_unblock` no navegan.</summary>
    private static readonly string[] ActosQueNavegan = { "map_go_to", "map_take", "map_decidir", "map_type", "map_open_app" };

    /// <summary>
    /// ¿HAY QUE ESPERAR A QUE LA PÁGINA SE ASIENTE antes de contar lo que hay? Solo si se navegó, y solo a una web.
    /// </summary>
    /// <remarks>
    /// MEDIDO EL 2026-09-18 en tres pruebas del dueño: de los 9 actos tras los que el modelo volvió a pedir
    /// `map_what_i_see` en menos de 6 s, en 8 el inventario del acto se había quedado corto —24→64, 26→49, 33→84,
    /// 32→95, 27→59 elementos—. Veintitantos elementos es el cromo del navegador sin la página. El modelo decidía con
    /// eso: volvía a mirar (~2 s de ida y vuelta), nombraba una puerta que aún no estaba, o creía la página rota y
    /// pulsaba «Volver a cargar» (tres veces, 3-5 s cada una).
    ///
    /// `map_go_to` ESPERA AUNQUE LA DIRECCIÓN NO CAMBIE: una búsqueda nueva es la misma «google.com/search» y otra
    /// página entera. Los demás, solo si la ubicación cambió: un clic que no navegó no dejó nada cargando, y pagar
    /// una mirada de más en cada uno sería cobrarle a todos lo que solo deben las navegaciones.
    /// </remarks>
    public static bool HayQueAsentar(string herramienta, string antes, string ahora)
    {
        string h = (herramienta ?? "").Trim(), a = (antes ?? "").Trim(), d = (ahora ?? "").Trim();
        if (!ActosQueNavegan.Any(x => x.Equals(h, StringComparison.OrdinalIgnoreCase))) return false;
        if (!d.StartsWith("web://", StringComparison.OrdinalIgnoreCase)) return false;
        if (h.Equals("map_go_to", StringComparison.OrdinalIgnoreCase)) return true;
        return !Navigation.Superficies.MismaPantalla(a, d);
    }

    /// <summary>Hasta cuántos elementos se considera que lo visto es el cromo del navegador sin la página.</summary>
    public const int ElementosDeUnEsqueleto = 60;

    /// <summary>
    /// ¿LO QUE SE VIO AL TERMINAR ES UN ESQUELETO? Solo entonces merece la pena esperar a que la página se asiente.
    /// </summary>
    /// <remarks>
    /// LA PRIMERA VERSIÓN ESPERABA TRAS TODA NAVEGACIÓN, y el nivel 4 la midió el mismo día: `map_go_to` pasó de
    /// 0,8-1,7 s a 1,9-3,4 s —entre +1 y +2,6 s por navegación—, cuando el modelo solo había vuelto a mirar tras una de
    /// cada cinco, perdiendo ~2 s. Esperar siempre salía más caro que el problema que arreglaba.
    ///
    /// LOS DATOS DAN EL CORTE: en los ocho casos del día en que el inventario se quedó corto, el primer vistazo traía
    /// 24, 26, 27, 32, 33, 33, 38 y 57 elementos —las pestañas, la barra y los botones del navegador—. Las páginas que
    /// ya venían con contenido traían de 105 a 196, y esas se entregan en el acto aunque sigan creciendo: el modelo
    /// tarda ~2 s en contestar, y para entonces lo que eligió de esa lista sigue estando.
    /// </remarks>
    public static bool EsEsqueleto(string inventario)
    {
        string c = Cabecera(inventario);
        int abre = c.LastIndexOf(" (", StringComparison.Ordinal), cierra = c.IndexOf(" elemento", StringComparison.Ordinal);
        if (!c.StartsWith(MarcaDelInventario, StringComparison.Ordinal) || abre < 0 || cierra <= abre) return false;
        return int.TryParse(c[(abre + 2)..cierra], out int n) && n <= ElementosDeUnEsqueleto;
    }

    /// <summary>
    /// MIRAR HASTA QUE DOS MIRADAS SEGUIDAS COINCIDAN, con tope de RELOJ (promesa 245). Se compara la cabecera del
    /// inventario —dónde y cuántos elementos—, no el texto entero: una página con un contador no se asentaría nunca.
    /// Si el tope se agota se entrega lo último y SE DICE, para que el modelo sepa que puede faltar algo.
    /// </summary>
    /// <param name="primero">El inventario que el acto ya leyó al terminar.</param>
    /// <param name="dormir">Inyectable, como <paramref name="ahoraMs"/>: el contrato lo juzga sin dormir de verdad.</param>
    public static string InventarioAsentado(string primero, Func<string> mirar, int pausaMs, int topeMs,
        Action<int> dormir, Func<long> ahoraMs)
    {
        string ultimo = primero ?? "";
        long t0 = ahoraMs();
        while (ahoraMs() - t0 + pausaMs < topeMs)
        {
            dormir(pausaMs);
            string otra = mirar() ?? "";
            if (otra.Length == 0) return ultimo;               // no se pudo mirar: lo que había
            bool igual = Cabecera(otra) == Cabecera(ultimo);
            ultimo = otra;
            if (igual) return ultimo;
        }
        return ultimo.TrimEnd() + "\n(la página seguía cambiando al contarla: puede faltar algo; vuelve a mirar si lo que buscas no está.)";
    }

    /// <summary>La primera línea del inventario: «EN PANTALLA AHORA, en «dónde» (N elemento(s)):».</summary>
    public static string Cabecera(string inventario)
    {
        string i = inventario ?? "";
        int salto = i.IndexOf('\n');
        return (salto >= 0 ? i[..salto] : i).Trim();
    }
}
