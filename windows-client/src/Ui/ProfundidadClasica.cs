using U.WindowsClient.Navigation;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Ui;

/// <summary>
/// EL DIBUJO CLÁSICO, tal como estaba antes de dejar que mandara solo el mapa.
///
/// Existe para PODER CONTRASTAR, que es lo que pidió el usuario el 2026-08-08: lo que él llama
/// «plata» no es una capa de datos, es un COMPORTAMIENTO que vio funcionar en el explorador de
/// archivos —el cromo quieto en su fila, la raíz firme, bajar a una subcarpeta y volver sin que
/// nada saltara—. Ese comportamiento lo sostenían estas cinco fuentes de profundidad, aunque
/// vivieran en el pintor y no en el mapa.
///
/// Borrarlo sin más dejaba sin referencia contra la que medir lo nuevo. Así que aquí está, ENTERO
/// y AISLADO: mismas reglas, mismo orden, sin tocar nada del camino nuevo. Se elige con un botón
/// y se comparan los dos dibujos sobre el mismo grafo.
///
/// No es la solución: es el patrón de oro provisional. Cuando el camino por el mapa reproduzca lo
/// que aquí se ve —y lo explique, que es lo que esto no hacía— este archivo sobra y se borra.
/// </summary>
internal static class ProfundidadClasica
{
    /// <summary>
    /// Calcula la profundidad de cada nodo con las CINCO fuentes de siempre, en su orden original.
    /// Devuelve además, por nodo, por qué vía quedó declarado (para el diagnóstico).
    /// </summary>
    public static Dictionary<string, int> Calcular(
        SurfaceMap _map,
        string appActual,
        string raiz,
        string centro,
        List<string> pisados,
        List<(string From, string To, string Etiqueta)> traza,
        Func<string, string> NivelDe,
        Dictionary<string, int> declaradosSalida,
        Dictionary<string, string> _porQueDeclarado,
        ref string _huellaPuente)
    {
        _porQueDeclarado.Clear();   // por qué vía llegó cada declarado: para no volver a adivinarlo
        var declarados = _map.Edges()
            .Where(e => e.Info.NivelFijado && e.Info.NivelNav >= 0 && !SurfaceMap.EsPuerta(e.To)
                     && (appActual.Length == 0
                         || NivelDe(e.To).Equals(appActual, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(e => e.To, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Min(e => e.Info.NivelNav), StringComparer.OrdinalIgnoreCase);
        foreach (var k in declarados.Keys) _porQueDeclarado[k] = "arista fijada";

        // Y LA ENSEÑANZA SE CONECTA A LOS NODOS POR SU NOMBRE, no solo a través de las aristas. El
        // camino por aristas depende de que la arista exista Y conserve su etiqueta, y las que
        // nacen viendo pasar una navegación a mano pierden la etiqueta cuando la atribución del
        // clic falla — el diagnóstico dio «0 declarados» con la enseñanza intacta en disco
        // (2026-08-07). El puente que no se rompe es la identidad: la pantalla
        // «explorer.exe/notas» NACE de la puerta «Notas», su nombre ES la etiqueta enseñada.
        if (appActual.Length > 0)
        {
            // El puente recorre TAMBIÉN los extremos de las aristas del mapa, no solo lo paseado en
            // esta sesión: desde que la estructura sale del mapa, un nodo enseñado puede entrar al
            // dibujo sin que nadie lo haya pisado hoy — «videos» apareció en fila 2 sin asterisco,
            // estando enseñada, porque llegó por una arista y el puente no la miró (2026-08-07).
            var candidatosPuente = pisados
                .Concat(traza.SelectMany(x => new[] { x.From, x.To }))
                .Concat(_map.Edges()
                    .Where(e => !SurfaceMap.EsPuerta(e.To) && SurfaceMap.MismaApp(e.From, e.To))
                    .SelectMany(e => new[] { e.From, e.To }))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            var ensenadas = _map.EnsenanzasDe(appActual);
            if (ensenadas.Count == 0 && _huellaPuente != appActual)
            {
                _huellaPuente = appActual;
                LogBus.Log("grafo", $"puente: EnsenanzasDe(«{appActual}») = 0 — ¿la clave del "
                    + $"diccionario no coincide? apps con enseñanza: "
                    + string.Join(", ", _map.AppsConJerarquia().Select(x => $"«{x.App}»")));
            }
            if (ensenadas.Count > 0)
            {
                // EL PUENTE POR NOMBRE ES UNA RED, NO UNA SEGUNDA FUENTE. Dos reglas que salieron
                // de enseñar GitHub (2026-08-07, observado por el usuario):
                // · si la etiqueta ya está anclada por una ARISTA FIJADA, el puente se abstiene —
                //   la pestaña «Code» lleva a la pantalla «graph» (así se llama su URL), y el
                //   puente anclaba ADEMÁS un nodo fantasma «code»: la misma pantalla, dos veces
                //   en la fila 1;
                // · si el nombre casa con MÁS DE UN nodo, la ambigüedad no es evidencia — «pulls»
                //   existe como pantalla global y como pestaña del repo, y anclar las dos duplicaba
                //   la fila 1. En la duda, mandan las aristas, que sí distinguen.
                var ancladas = new HashSet<string>(
                    _map.Edges().Where(e => e.Info.NivelFijado && !SurfaceMap.EsPuerta(e.To)
                                         && e.Info.Label.Length > 0)
                        .Select(e => Uia.Reconocedor.Normalizar(e.Info.Label)),
                    StringComparer.Ordinal);

                var candidatosPorEtiqueta = new Dictionary<string, (int Nivel, List<string> Nodos)>(StringComparer.Ordinal);
                foreach (var n in candidatosPuente)
                {
                    if (declarados.ContainsKey(n)) continue;
                    string cola = n.TrimEnd('/');
                    int barra = cola.LastIndexOf('/');
                    string slug = Uia.Reconocedor.Normalizar(barra >= 0 ? cola[(barra + 1)..] : cola);
                    foreach (var (etiqueta, nivel) in ensenadas)
                    {
                        string norm = Uia.Reconocedor.Normalizar(etiqueta);
                        if (!slug.Equals(norm, StringComparison.Ordinal)) continue;
                        if (!candidatosPorEtiqueta.TryGetValue(norm, out var acc))
                            candidatosPorEtiqueta[norm] = acc = (nivel, new List<string>());
                        acc.Nodos.Add(n);
                        break;
                    }
                }
                foreach (var (norm, (nivel, nodos)) in candidatosPorEtiqueta)
                {
                    if (ancladas.Contains(norm))
                    { LogBus.Log("grafo", $"puente: «{norm}» ya anclada por arista fijada; el nombre no opina"); continue; }
                    if (nodos.Count != 1)
                    { LogBus.Log("grafo", $"puente: «{norm}» casa con {nodos.Count} nodos; ambigüedad no es evidencia"); continue; }
                    declarados[nodos[0]] = nivel;
                    _porQueDeclarado[nodos[0]] = $"nombre≈«{norm}»";
                }
            }
        }

        foreach (var kv in declarados) declaradosSalida[kv.Key] = kv.Value;

        var prof = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [raiz] = 0 };
        int suelo = centro.Length > 0 ? 1 : 0;

        // Y MIENTRAS MANDA LO DECLARADO, LA FILA 1 ES SUYA: lo pisado sin informacion no puede
        // colocar mejor que lo conocido.
        // nivel: en el grafo aparecían «code», «diagtrack» o «leykiara» a la altura del panel,
        // sin que nadie los hubiera declarado (2026-08-07, observado por el usuario; el
        // diagnóstico los mostró en fila 1 SIN asterisco y con Nivel=-1 en el mapa — no los subió
        // nadie: aterrizaron ahí). Ser desconocido no puede colocar mejor que ser conocido.
        int sueloDesconocido = centro.Length > 0 && SurfaceMap.SoloLoDeclarado ? 2 : suelo;

        // 1. Lo declarado: nivel N → fila N, colgando del centro.
        foreach (var d in declarados)
            prof[d.Key] = Math.Max(suelo, d.Value);

        // LA ESTRUCTURA SALE DEL MAPA, NO DEL PASEO DE ESTA SESIÓN. La profundidad de los niveles
        // inferiores se rellenaba con la traza viva (_ultimaCorrida), que cambia con cada
        // movimiento y olvida tramos al pasar de cuarenta: moverse entre dos elementos del primer
        // nivel REDIBUJABA todo el drill-down ya aprendido, porque su colocación dependía del
        // orden del paseo de hoy (2026-08-07, observado por el usuario). La superficie de
        // navegación, una vez aprendida, es ESTÁTICA — y quien la sabe es el mapa, cuyas aristas
        // cruzadas no cambian por volver a pasear. La traza queda solo como rastro visual (las
        // líneas verdes), sin voz en la estructura.
        var aristasMapa = _map.Edges()
            .Where(e => !SurfaceMap.EsPuerta(e.To) && e.Info.Selector.Length > 0
                     && SurfaceMap.MismaApp(e.From, e.To)
                     && (appActual.Length == 0
                         || NivelDe(e.From).Equals(appActual, StringComparison.OrdinalIgnoreCase)))
            .Select(e => (e.From, e.To))
            .Distinct()
            .ToList();

        // 2. Lo que el mapa sabe de cada pantalla, TAL CUAL. Aquí había un
        //    `Math.Max(sueloDesconocido, ni.Nivel)` que impedía a estas pantallas reclamar la fila 1
        //    — un guardián contra la DEDUCCIÓN ESTADÍSTICA, que existía cuando el nivel de un nodo
        //    podía salir de contar apariciones y no de que alguien lo declarara.
        //
        //    Esa fuente ya no existe (se eliminó el 2026-08-08) y el guardián se volvió el problema:
        //    pulsar una puerta CROMO —nivel 1— llevaba a una pantalla que el mapa situaba en 1 y el
        //    dibujo empujaba a la 2. Se veía como «hice clic en un botón que es cromo y se colocó
        //    debajo» (observado por el usuario). El mapa y el dibujo decían cosas distintas del
        //    mismo sitio, y eso es exactamente lo que un mapa no puede hacer.
        //
        //    Hoy el nivel de una pantalla solo llega por una puerta DECLARADA (ver Commit y
        //    LearnTraversal), así que no hay nada de lo que protegerse: si el mapa lo sitúa, va ahí.
        foreach (var n in pisados
                     .Concat(aristasMapa.SelectMany(x => new[] { x.From, x.To }))
                     .Concat(traza.SelectMany(x => new[] { x.From, x.To })))
            if (!prof.ContainsKey(n) && _map.Nodes.TryGetValue(n, out var ni) && ni.Nivel >= 0)
                prof[n] = ni.Nivel;

        // 3. Las aristas DEL MAPA rellenan los huecos por DISTANCIA MÍNIMA a lo ya colocado. Con
        //    «la primera asignación gana», el resultado dependía del orden de enumeración de las
        //    aristas — y ese orden CAMBIA cuando el diccionario recicla el hueco de una puerta
        //    borrada. Con un ciclo de por medio (vercel→graph del atrás), cada redibujo podía
        //    resolverse distinto: «vercel» saltó a la altura de su padre y al rato volvió a su
        //    sitio (2026-08-07, observado por el usuario). La distancia mínima no depende de
        //    ningún orden. Lo colocado por las fuentes 1 y 2 queda FIJO: relajar no lo toca.
        var fijos = new HashSet<string>(prof.Keys, StringComparer.OrdinalIgnoreCase);
        for (int pasada = 0; pasada < 8; pasada++)
            foreach (var (f, t) in aristasMapa)
                if (prof.TryGetValue(f, out int d) && !fijos.Contains(t)
                    && (!prof.TryGetValue(t, out int dt) || dt > d + 1))
                    prof[t] = d + 1;

        // 4. Solo lo que el mapa aún no encadena cae al paseo de la sesión, y lo huérfano al suelo
        //    de lo desconocido.
        for (int pasada = 0; pasada < 6; pasada++)
            foreach (var (f, t, _) in traza)
                if (prof.TryGetValue(f, out int d) && !fijos.Contains(t) && !prof.ContainsKey(t))
                    prof[t] = d + 1;
        foreach (var (f, t, _) in traza)
        {
            if (!prof.ContainsKey(f)) prof[f] = sueloDesconocido;
            if (!prof.ContainsKey(t)) prof[t] = prof[f] + 1;
        }
        foreach (var n in pisados) if (!prof.ContainsKey(n)) prof[n] = sueloDesconocido;
        return prof;
    }
}
