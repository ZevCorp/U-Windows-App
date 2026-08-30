using System.Text;

namespace U.WindowsClient.Navigation;

/// <summary>
/// EL TERRENO POR DELANTE: ¿qué habrá tras esta puerta? La consulta de la profundidad.
/// </summary>
/// <remarks>
/// LA NOVEDAD DE T3 (plan terreno-profundo, 2026-08-26). El grafo ya recordaba todo lo necesario
/// —los elementos de cada ubicación aunque no estés allí, y el destino de cada cruce— pero nadie
/// hacía la PREGUNTA. Con la respuesta, el modelo planifica batches que atraviesan pantallas que
/// aún no ve, y paga un viaje solo donde el terreno diverge. La compuerta de vida no se relaja:
/// la predicción PROPONE con memoria, el batch DISPONE con pantalla.
///
/// LAS DOS MENTIRAS PROHIBIDAS DE NACIMIENTO (promesa 74): prometer destino de una puerta que
/// nadie cruzó —el núcleo «no se inventa nada», y esta consulta tampoco—, y ahogar la respuesta
/// en inventario: pasadas ~12 puertas por pantalla se dice «y N más» (la regla 8 del génesis:
/// un resultado >25k tokens se va a archivo y el modelo pierde el hilo).
///
/// PURO: lee el grafo y compone prosa. Ni pantalla, ni COM, ni MCP — por eso el contrato lo juzga
/// entero con un grafo de tres pantallas (promesas 73-74). La sirven el MCP (`map_ahead`) y el
/// 8792 para el visor: una pregunta, UN sitio; dos bocas.
/// </remarks>
public sealed class TerrenoPorDelante
{
    private readonly Nucleo.Grafo _grafo;

    /// <summary>Cuántas puertas de una pantalla se nombran antes de decir «y N más».</summary>
    private const int PuertasPorPantalla = 12;

    public TerrenoPorDelante(Nucleo.Grafo grafo) => _grafo = grafo;

    /// <summary>
    /// El terreno por delante, contado para quien planifica.
    /// </summary>
    /// <param name="desde">Desde dónde se mira (normalmente, donde estás).</param>
    /// <param name="puerta">
    /// La puerta que interesa (etiqueta o selector), o vacío para el panorama: las puertas
    /// cruzadas de aquí y a dónde llevan.
    /// </param>
    /// <param name="niveles">Cuántas pantallas hacia delante como máximo (1-3).</param>
    public string Cuenta(string desde, string puerta = "", int niveles = 2)
    {
        if (string.IsNullOrWhiteSpace(desde)) return "no sé desde dónde miras: falta la ubicación.";
        niveles = Math.Clamp(niveles, 1, 3);

        var aqui = _grafo.DesdeAqui(desde);
        if (aqui.Count == 0)
            return $"de «{Corto(desde)}» no recuerdo todavía ni una puerta: recórrelo una vez y el terreno aprende.";

        var sb = new StringBuilder();

        if (string.IsNullOrWhiteSpace(puerta))
        {
            var cruzadas = aqui.Where(a => a.Destino.Length > 0).ToList();
            int sinCruzar = aqui.Count - cruzadas.Count;
            if (cruzadas.Count == 0)
                return $"desde «{Corto(desde)}» recuerdo {aqui.Count} puerta(s), ninguna cruzada aún: "
                     + "todas por descubrir. Cruza una y te cuento qué hay detrás.";

            sb.Append($"Desde «{Corto(desde)}», lo cruzado:\n");
            foreach (var a in cruzadas.Take(PuertasPorPantalla))
            {
                sb.Append($"  tras «{a.Que.Etiqueta}» → «{Corto(a.Destino)}»\n");
                Adentro(sb, a.Destino, niveles - 1, sangria: "    ", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { desde });
            }
            if (cruzadas.Count > PuertasPorPantalla) sb.Append($"  …y {cruzadas.Count - PuertasPorPantalla} cruzadas más.\n");
            if (sinCruzar > 0) sb.Append($"  (+{sinCruzar} puerta(s) por descubrir aquí)\n");
            return sb.ToString().TrimEnd();
        }

        // La puerta pedida: exacta por selector, exacta por etiqueta, o difusa si es única.
        var elegida = aqui.FirstOrDefault(a => a.Que.Selector.Equals(puerta, StringComparison.Ordinal))
                   ?? aqui.FirstOrDefault(a => a.Que.Etiqueta.Equals(puerta, StringComparison.OrdinalIgnoreCase));
        if (elegida == null)
        {
            var parecidas = aqui.Where(a =>
                a.Que.Etiqueta.Contains(puerta, StringComparison.OrdinalIgnoreCase)).ToList();
            if (parecidas.Count == 1) elegida = parecidas[0];
            else if (parecidas.Count > 1)
                return $"hay {parecidas.Count} puertas que suenan a «{puerta}» en «{Corto(desde)}»: "
                     + string.Join(", ", parecidas.Take(6).Select(a => $"«{a.Que.Etiqueta}» ({a.Que.Selector})"))
                     + ". Dime cuál.";
        }
        if (elegida == null)
            return $"«{puerta}» no está ni en lo que recuerdo de «{Corto(desde)}»: no la conozco.";

        if (elegida.Destino.Length == 0)
            return $"«{elegida.Que.Etiqueta}» está por descubrir: nadie la ha cruzado desde «{Corto(desde)}», "
                 + "así que no prometo lo que hay detrás. Crúzala una vez y el terreno lo aprende.";

        sb.Append($"Tras «{elegida.Que.Etiqueta}» quedarás en «{Corto(elegida.Destino)}».\n");
        Adentro(sb, elegida.Destino, niveles - 1, sangria: "  ",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { desde });
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Lo que el terreno recuerda DENTRO de una ubicación, y —si quedan niveles— qué hay tras sus
    /// puertas cruzadas. El conjunto <paramref name="vistas"/> corta los ciclos: un «Atrás» que
    /// vuelve por donde vinimos no debe hacer girar la cuenta en redondo.
    /// </summary>
    private void Adentro(StringBuilder sb, string ubicacion, int nivelesQueQuedan, string sangria, HashSet<string> vistas)
    {
        if (!vistas.Add(ubicacion)) return;

        var alli = _grafo.DesdeAqui(ubicacion);
        if (alli.Count == 0)
        {
            sb.Append($"{sangria}(de allí no recuerdo todavía nada)\n");
            return;
        }

        // Las cruzadas primero: son las que ya saben a dónde llevan.
        var orden = alli.OrderByDescending(a => a.Destino.Length > 0)
                        .ThenBy(a => a.Que.Etiqueta.Length).ToList();
        sb.Append(sangria + "allí recuerdo: "
            + string.Join(", ", orden.Take(PuertasPorPantalla)
                .Select(a => $"«{a.Que.Etiqueta}»" + (a.Destino.Length > 0 ? $" (→ {Corto(a.Destino)})" : "")))
            + (alli.Count > PuertasPorPantalla ? $" …y {alli.Count - PuertasPorPantalla} más" : "")
            + ".\n");

        if (nivelesQueQuedan <= 0) return;
        foreach (var a in orden.Where(a => a.Destino.Length > 0).Take(PuertasPorPantalla))
        {
            if (vistas.Contains(a.Destino)) continue;
            sb.Append($"{sangria}tras «{a.Que.Etiqueta}» → «{Corto(a.Destino)}»\n");
            Adentro(sb, a.Destino, nivelesQueQuedan - 1, sangria + "  ", vistas);
        }
    }

    private static string Corto(string id)
    {
        id ??= "";
        // En SAP, la cola sola miente: Easy Access y NWP1 terminan los dos en «0100». El nombre
        // corto lleva la TRANSACCIÓN, que es como el operador nombra la pantalla.
        if (id.StartsWith("sapgui://", StringComparison.OrdinalIgnoreCase))
        {
            var partes = id[9..].Split('/');
            if (partes.Length >= 2)
            {
                string cola = partes[^1];
                return partes[1] + (cola.Length > 0 && !cola.Equals(partes[1], StringComparison.OrdinalIgnoreCase)
                    ? "·" + cola : "");
            }
        }
        int i = id.LastIndexOf('/');
        return i > 0 ? id[(i + 1)..] : id;
    }
}
