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
    private readonly UiaReader _lector = new();

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

        ObservarAqui(loc.Id);
        var salidas = _map.ExitsFrom(loc.Id);
        int recorribles = salidas.Count(h => h.Info.Selector.Length > 0);
        return $"Estás en «{loc.Id}». Desde aquí el mapa conoce {salidas.Count} salida(s), "
             + $"{recorribles} de ellas recorribles.";
    }

    /// <summary>
    /// Registra las salidas de la pantalla actual, si aún no se conocen.
    ///
    /// El mapa solo se llenaba durante un recorrido automático, así que el asistente podía LLEGAR
    /// a un sitio nuevo por MCP y quedarse ciego allí: «0 salidas conocidas» estando delante de
    /// una carpeta llena de cosas (2026-08-02). Preguntar dónde estoy es el momento natural para
    /// mirar alrededor — el terreno se aprende viviendo, no solo explorando a propósito.
    /// </summary>
    private void ObservarAqui(string nodo, bool forzar = false)
    {
        try
        {
            if (forzar) { ObservarSinGuardia(nodo); return; }
            // Se mira alrededor salvo que la pantalla ya se conozca COMPLETA: con salidas
            // recorribles Y con sus acciones. «Alguna salida» no bastaba (conectividad pasiva sin
            // acción), y «alguna recorrible» tampoco: los nodos mapeados antes de clasificar
            // puertas conocían la navegación pero ninguna acción, y sin este repaso se quedaban
            // así para siempre.
            var conocidas = _map.ExitsFrom(nodo);
            if (conocidas.Any(h => h.Info.Selector.Length > 0)
                && conocidas.Any(h => h.Info.Kind.Equals("accion", StringComparison.OrdinalIgnoreCase))) return;
            ObservarSinGuardia(nodo);
        }
        catch { }
    }

    /// <summary>
    /// Relee la pantalla y anota lo que haya, sin preguntarse si ya se conocía.
    ///
    /// Hace falta después de EJECUTAR una acción: un menú abierto no es una pantalla nueva —la
    /// superficie sigue siendo la misma— así que el guardia de «esto ya se conoce» impedía ver los
    /// elementos que acababan de aparecer. Se pulsaba «Nuevo», el menú se abría con «Carpeta»
    /// dentro, y el asistente seguía viendo la lista de antes (2026-08-02).
    /// </summary>
    private void ObservarSinGuardia(string nodo)
    {
        try
        {
            _lector.Read();

            var puertas = new List<(string, string, string, string[])>();
            foreach (var el in _lector.Elements)
            {
                // Igual que el crawler: TODO lo accionable, también los botones de ejecución.
                if (el.ControlType.Equals("text", StringComparison.OrdinalIgnoreCase)
                    || el.ControlType.Equals("image", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var (l, t, sels) = UiaSurface.DescribeElement(el.Native);
                    var utiles = sels.Where(s => !s.Contains("path=", StringComparison.Ordinal)
                        && !System.Text.RegularExpressions.Regex.IsMatch(s, @"(name|aid)=(;|$)")).ToArray();
                    if (utiles.Length == 0) continue;
                    puertas.Add((l.Length > 0 ? l : el.Label, t.Length > 0 ? t : el.ControlType,
                                 utiles[0], utiles.Skip(1).ToArray()));
                }
                catch { }
            }
            if (puertas.Count == 0) return;

            _map.ObserveExits(nodo, puertas);
            LogBus.Log("mapa-mcp", $"al llegar a '{nodo}' se anotaron {puertas.Count} salida(s)");
        }
        catch { }
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

        // Navegación y ejecución separadas: son preguntas distintas («¿a dónde puedo ir?» vs
        // «¿qué puedo hacer aquí?») y mezclarlas obliga al modelo a adivinar cuál es cuál.
        var sb = new System.Text.StringBuilder($"Desde «{desde}»:\n");
        foreach (var h in salidas.Where(x => !x.Info.Kind.Equals("accion", StringComparison.OrdinalIgnoreCase)))
            sb.AppendLine(h.Info.Selector.Length > 0
                ? $"  → {h.To}   pulsando «{h.Info.Label}»  ({h.Info.Count} vez/veces)"
                : $"  → {h.To}   (observado {h.Info.Count} vez/veces, pero NO se sabe con qué acción)");

        var acciones = salidas.Where(x => x.Info.Kind.Equals("accion", StringComparison.OrdinalIgnoreCase)
                                       && x.Info.Selector.Length > 0).ToList();
        if (acciones.Count > 0)
        {
            sb.AppendLine("Acciones disponibles aquí (se toman con map_take, no navegan):");
            sb.AppendLine("  " + string.Join(", ", acciones.Select(a => $"«{a.Info.Label}»")));
        }
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

        string desde = actual.Id;
        if (!_uia.Execute(paso, out string error))
            return $"no se pudo pulsar «{elegida.Info.Label}»: {error}";

        // PUERTA DE ACCIÓN: su éxito no es llegar a otra pantalla — es haber hecho algo AQUÍ.
        // Exigirle navegación reportaría fallo a un «Nuevo» que abrió su menú perfectamente. Si
        // resulta que sí navegó (un «Guardar como…» que abre diálogo), eso también se cuenta.
        if (elegida.Info.Kind.Equals("accion", StringComparison.OrdinalIgnoreCase))
        {
            string tras = EsperarCambio(desde, 2000);
            LogBus.Log("mapa-mcp", $"✓ acción «{elegida.Info.Label}» ejecutada" + (tras.Length > 0 ? $" → {tras}" : ""));

            // Se relee SIEMPRE: una acción suele destapar cosas nuevas —un menú, un diálogo— en la
            // misma superficie, y sin releer el asistente actuaría sobre la pantalla de antes.
            string aqui = tras.Length > 0 ? tras : desde;
            ObservarAqui(aqui, forzar: true);
            var nuevas = _map.ExitsFrom(aqui)
                .Where(h => h.Info.Kind.Equals("accion", StringComparison.OrdinalIgnoreCase) && h.Info.Selector.Length > 0)
                .Select(h => h.Info.Label).Distinct().Take(18).ToList();

            return (tras.Length > 0
                    ? $"ejecuté «{elegida.Info.Label}» y la pantalla pasó a «{tras}». "
                    : $"ejecuté «{elegida.Info.Label}» (la superficie sigue siendo «{desde}»). ")
                 + (nuevas.Count > 0 ? "Ahora hay: " + string.Join(", ", nuevas.Select(n => $"«{n}»")) : "");
        }

        // PUERTA SIN CRUZAR: no hay destino contra el que comparar, así que el éxito es que la
        // pantalla CAMBIE, y lo que se descubre se aprende. Comparar contra el marcador «?selector»
        // hacía que cruzar una puerta se reportara siempre como fallo, incluso llegando —justo lo
        // contrario de para lo que existen las puertas, que es descubrir a dónde dan (2026-08-02).
        if (SurfaceMap.EsPuerta(elegida.To))
        {
            string llegada = EsperarCambio(desde, 4000);
            if (llegada.Length == 0)
                return $"pulsé «{elegida.Info.Label}» pero la pantalla no cambió; sigue sin saberse a dónde da.";

            _map.LearnTraversal(desde, llegada, elegida.Info.Selector, elegida.Info.Alternatives,
                elegida.Info.Label, elegida.Info.ControlType, elegida.Info.ActionType);
            LogBus.Log("mapa-mcp", $"✓ puerta «{elegida.Info.Label}» descubierta → {llegada}");
            return $"tomé «{elegida.Info.Label}»: era una puerta sin explorar y lleva a «{llegada}». Queda aprendida.";
        }

        if (!Llego(elegida.To, 4000))
            return $"pulsé «{elegida.Info.Label}» pero no se llegó a «{elegida.To}». "
                 + $"Estamos en «{_where()?.Id}».";

        LogBus.Log("mapa-mcp", $"✓ salida «{elegida.Info.Label}» → {elegida.To}");
        return $"tomé «{elegida.Info.Label}» y llegué a «{elegida.To}»";
    }

    /// <summary>Espera a que la superficie DEJE de ser la de partida y devuelve la nueva, o "".</summary>
    private string EsperarCambio(string desde, int msMax)
    {
        for (int i = 0; i < msMax / 150; i++)
        {
            System.Threading.Thread.Sleep(150);
            string ahora = _where()?.Id ?? "";
            if (ahora.Length > 0 && !string.Equals(ahora, desde, StringComparison.OrdinalIgnoreCase)
                && !ahora.EndsWith("/ventana", StringComparison.OrdinalIgnoreCase))
                return ahora;
        }
        return "";
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
