using U.Graph;
using U.Graph.Surfaces;
using U.WindowsClient.Diagnostics;
using U.WindowsClient.Navigation;
using U.WindowsClient.Uia;

namespace U.WindowsClient.Mcp;

/// <summary>
/// El mapa del computador, expuesto al cerebro. Cuatro herramientas: tres que MIRAN y una que
/// ACTÚA, y esa separación es el diseño, no una casualidad de implementación.
///
/// Lo que hace útil a este mapa es que se aprendió solo, viendo al usuario trabajar: el asistente
/// puede llegar a sitios que nadie le enseñó como workflow. Lo que lo hace peligroso es lo mismo —
/// nadie revisó esas rutas. De ahí las dos reglas que gobiernan todo lo de abajo:
///
///   · SOLO SE OFRECEN RUTAS COMPLETAS. Una arista sin acción no se puede recorrer, así que no
///     entra en ninguna ruta. Mejor «no sé llegar» que dejar al asistente a mitad de camino en la
///     máquina de alguien.
///   · SE VERIFICA CADA TRAMO. Tras ejecutar la acción se comprueba que la pantalla es la esperada,
///     y si no llegó se PARA y se dice dónde quedó. La medición de este mapa dio ~78% de acciones
///     correctas: sin verificación, una de cada cinco navegaciones acabaría en un sitio distinto
///     del que el modelo cree, actuando sobre lo que no reconoce.
/// </summary>
public sealed class SurfaceMapTools
{
    private readonly SurfaceMap _map;
    private readonly Func<SurfaceLocator.SurfaceLocation?> _where;
    private readonly UiaSurface _uia = new() { Log = s => LogBus.Log("mapa-mcp", s) };

    public SurfaceMapTools(SurfaceMap map, Func<SurfaceLocator.SurfaceLocation?> where)
    {
        _map = map;
        _where = where;
    }

    public static bool IsMapTool(string tool) => tool is
        "map_where_am_i" or "map_places" or "map_routes_from" or "map_go_to" or "map_take";

    public string Call(string tool, IReadOnlyDictionary<string, string> args)
    {
        string A(string k) => args.TryGetValue(k, out var v) ? v.Trim() : "";

        // Se registra CADA llamada y su respuesta. Sin esto, «el mapa no aportó nada» y «el modelo
        // ni lo intentó» se ven exactamente igual en el log — y esa ambigüedad me llevó a un
        // diagnóstico equivocado el 2026-07-31, buscando en el mapa un fallo que estaba en el
        // lanzador de apps.
        string args_ = string.Join(" ", args.Select(kv => $"{kv.Key}={kv.Value}"));
        LogBus.Log("mapa-mcp", $"→ {tool} {args_}".TrimEnd());

        string r = tool switch
        {
            "map_where_am_i" => WhereAmI(),
            "map_places" => Places(A("app")),
            "map_routes_from" => Routes(A("surface")),
            "map_go_to" => GoTo(A("surface")),
            "map_take" => Take(A("exit")),
            _ => $"herramienta de mapa no soportada: {tool}",
        };

        LogBus.Log("mapa-mcp", "← " + (r.Length > 200 ? r[..200] + "…" : r).Replace("\n", " | "));
        return r;
    }

    private string WhereAmI()
    {
        var loc = _where();
        if (loc == null) return "no se pudo determinar la superficie actual";
        var salidas = _map.ExitsFrom(loc.Id);
        int recorribles = salidas.Count(h => h.Info.Selector.Length > 0);
        return $"Estás en «{loc.Id}». Desde aquí el mapa conoce {salidas.Count} salida(s), "
             + $"{recorribles} de ellas recorribles.";
    }

    /// <summary>
    /// Las superficies conocidas, agrupadas por app y ordenadas por frecuencia — lo más visitado
    /// primero, que es lo que un humano llamaría "los sitios donde trabajo".
    /// </summary>
    private string Places(string app)
    {
        var nodos = _map.Nodes.AsEnumerable();
        if (app.Length > 0)
            nodos = nodos.Where(kv => kv.Key.Contains(app, StringComparison.OrdinalIgnoreCase));

        var lista = nodos.OrderByDescending(kv => kv.Value.Visits).Take(60).ToList();
        if (lista.Count == 0) return app.Length > 0
            ? $"el mapa no conoce ninguna pantalla de «{app}» todavía"
            : "el mapa está vacío: aún no se ha observado ninguna pantalla";

        var sb = new System.Text.StringBuilder($"{lista.Count} pantalla(s) conocida(s):\n");
        foreach (var kv in lista)
            sb.AppendLine($"  {kv.Key}  ({kv.Value.Visits} visita/s)");
        return sb.ToString();
    }

    /// <summary>
    /// A dónde se puede ir desde una pantalla, y con qué. Se dice EXPLÍCITAMENTE cuáles no tienen
    /// acción: que el modelo sepa que existe un camino pero que no sabemos recorrerlo es
    /// información útil —puede pedirlo al usuario o buscar otra vía—, y ocultarlo sería fingir que
    /// el mapa es más completo de lo que es.
    /// </summary>
    private string Routes(string surface)
    {
        string desde = surface.Length > 0 ? surface : (_where()?.Id ?? "");
        if (desde.Length == 0) return "no sé desde dónde: pasa `surface` o asegúrate de que hay una app en primer plano";

        var salidas = _map.ExitsFrom(desde);
        if (salidas.Count == 0) return $"el mapa no conoce ninguna salida desde «{desde}»";

        var sb = new System.Text.StringBuilder($"Desde «{desde}»:\n");
        foreach (var h in salidas)
            sb.AppendLine(h.Info.Selector.Length > 0
                ? $"  → {h.To}   pulsando «{h.Info.Label}»  ({h.Info.Count} vez/veces)"
                : $"  → {h.To}   (observado {h.Info.Count} vez/veces, pero NO se sabe con qué acción)");
        return sb.ToString();
    }

    /// <summary>
    /// Recorre la ruta. Ejecuta cada tramo y COMPRUEBA la llegada antes de seguir; si un tramo no
    /// lleva a donde debía, se detiene y lo dice. Nunca improvisa: si no hay ruta completa, no se
    /// mueve — el asistente ya tiene computer-use para lo desconocido, y mezclar ambas cosas
    /// convertiría un fallo de mapa en clics a ciegas.
    /// </summary>
    private string GoTo(string destino)
    {
        if (destino.Length == 0) return "falta `surface`: a dónde hay que ir";

        var actual = _where();
        if (actual == null) return "no se pudo determinar dónde estamos ahora mismo";

        var ruta = _map.Route(actual.Id, destino);

        // Sin ruta conocida, queda el ATAJO: un elemento presente en TODAS las pantallas —el panel
        // lateral, una barra de la app— que lleva al destino desde donde sea. No hace falta haber
        // recorrido nunca este camino concreto para poder usarlo.
        if (ruta == null)
        {
            var atajo = _map.AtajoHacia(destino);
            if (atajo != null)
            {
                LogBus.Log("mapa-mcp", $"sin ruta; atajo por cromo «{atajo.Info.Label}» hacia «{destino}»");
                ruta = new List<SurfaceMap.Hop> { atajo };
            }
        }
        if (ruta == null)
            return $"no conozco una ruta COMPLETA de «{actual.Id}» a «{destino}». "
                 + "Puede que el camino exista pero falte saber con qué acción se recorre alguno de sus tramos.";
        if (ruta.Count == 0) return $"ya estás en «{destino}»";

        LogBus.Log("mapa-mcp", $"ruta de {ruta.Count} tramo(s) hacia «{destino}»");

        for (int i = 0; i < ruta.Count; i++)
        {
            var h = ruta[i];
            var paso = new PlanStep
            {
                StepOrder = i + 1,
                // La acción con la que se APRENDIÓ la arista, no un clic por defecto: una carpeta
                // de la lista solo se abre con doble clic, y recorrerla con un clic la seleccionaría
                // sin navegar — la ruta prometería un camino que no cumple.
                ActionType = h.Info.ActionType,
                Selector = h.Info.Selector,
                Label = h.Info.Label,
            };

            if (!_uia.Execute(paso, out string error))
                return $"tramo {i + 1}/{ruta.Count}: no se pudo pulsar «{h.Info.Label}» ({error}). "
                     + $"El recorrido se detuvo en «{_where()?.Id}».";

            // La llegada se COMPRUEBA, no se supone. Sin esto, una acción equivocada —y el mapa
            // tiene ~1 de cada 5— dejaría al modelo creyendo que está donde no está.
            if (!Llego(h.To, 4000))
                return $"tramo {i + 1}/{ruta.Count}: pulsé «{h.Info.Label}» pero no se llegó a «{h.To}». "
                     + $"Estamos en «{_where()?.Id}». La ruta del mapa no coincide con la realidad aquí.";

            LogBus.Log("mapa-mcp", $"✓ tramo {i + 1}/{ruta.Count}: «{h.Info.Label}» → {h.To}");
        }
        return $"llegué a «{destino}» en {ruta.Count} paso(s)";
    }

    /// <summary>
    /// Toma UNA salida de la pantalla actual, la que el modelo elija por su nombre.
    ///
    /// Es la pieza que faltaba, y la pidió el usuario con mejor criterio que el mío: yo había hecho
    /// que <see cref="GoTo"/> exigiera el id exacto del destino —«uia://explorer.exe/videos-
    /// explorador-de-archivos»— y el modelo no tenía por qué acertar esa forma. Le estaba pidiendo
    /// que hablara mi idioma. Con esto el reparto es el natural: el modelo LEE las salidas
    /// (map_routes_from), DECIDE cuál sirve, y el cliente EJECUTA y verifica. La inteligencia de la
    /// ruta es del modelo; la honestidad del paso, nuestra.
    ///
    /// Un salto cada vez, a propósito: así el modelo ve a dónde llegó antes de decidir el
    /// siguiente, en vez de encadenar a ciegas una ruta que quizá dejó de ser válida.
    /// </summary>
    private string Take(string salida)
    {
        if (salida.Length == 0) return "falta `exit`: el nombre de la salida a tomar (el que aparece en map_routes_from)";

        var actual = _where();
        if (actual == null) return "no se pudo determinar dónde estamos ahora mismo";

        var opciones = _map.ExitsFrom(actual.Id).Where(h => h.Info.Selector.Length > 0).ToList();
        if (opciones.Count == 0)
            return $"desde «{actual.Id}» el mapa no conoce ninguna salida recorrible";

        // Coincidencia exacta primero, y luego por contención — «videos» debe encontrar «Videos»,
        // pero si dos salidas contienen lo pedido NO se elige por el modelo: se le devuelven las
        // candidatas. Adivinar entre dos destinos es exactamente lo que no debe hacer esta capa.
        var exactas = opciones.Where(h => h.Info.Label.Equals(salida, StringComparison.OrdinalIgnoreCase)).ToList();
        var candidatas = exactas.Count > 0
            ? exactas
            : opciones.Where(h => h.Info.Label.Contains(salida, StringComparison.OrdinalIgnoreCase)).ToList();

        if (candidatas.Count == 0)
            return $"desde «{actual.Id}» no hay ninguna salida que se llame «{salida}». Disponibles: "
                 + string.Join(", ", opciones.Select(h => $"«{h.Info.Label}»"));
        if (candidatas.Count > 1)
            return $"«{salida}» coincide con {candidatas.Count} salidas: "
                 + string.Join(", ", candidatas.Select(h => $"«{h.Info.Label}» → {h.To}"))
                 + ". Elige una por su nombre exacto.";

        var elegida = candidatas[0];
        var paso = new PlanStep
        {
            StepOrder = 1,
            ActionType = elegida.Info.ActionType,
            Selector = elegida.Info.Selector,
            Label = elegida.Info.Label,
        };

        if (!_uia.Execute(paso, out string error))
            return $"no se pudo pulsar «{elegida.Info.Label}»: {error}";

        if (!Llego(elegida.To, 4000))
            return $"pulsé «{elegida.Info.Label}» pero no se llegó a «{elegida.To}». "
                 + $"Estamos en «{_where()?.Id}».";

        LogBus.Log("mapa-mcp", $"✓ salida «{elegida.Info.Label}» → {elegida.To}");
        return $"tomé «{elegida.Info.Label}» y llegué a «{elegida.To}»";
    }

    /// <summary>Espera a que la superficie sea la esperada. La UI tarda; la paciencia va aquí.</summary>
    private bool Llego(string esperada, int msMax)
    {
        var hasta = DateTime.UtcNow.AddMilliseconds(msMax);
        while (DateTime.UtcNow < hasta)
        {
            string ahora = _where()?.Id ?? "";
            if (ahora.Length > 0 && SurfacePlace.Same(ahora, esperada)) return true;
            System.Threading.Thread.Sleep(200);
        }
        return false;
    }
}
