using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Navigation;

/// <summary>
/// PLATA REAL: la estructura DERIVADA del bronce, con la evidencia que la sostiene.
///
/// Nace de un diagnóstico del usuario (2026-08-10): lo que hoy se llama plata no es una
/// transformación, es una ANOTACIÓN. <see cref="SurfaceMap.FijarNivel"/> escribe el nivel encima
/// de los mismos campos del mapa, así que bronce y plata son el mismo dato con marcas; el criterio
/// de terminado (map_unsituated) cuenta lo declarado, así que un agente lo satisface ESCRIBIENDO
/// NÚMEROS; y `NodeInfo.Nivel` solo lo leen el dibujo y su copia clásica, así que declarar no
/// cambia nada de lo que el sistema hace. Se ajustaba el resultado hasta que lo visual cuadraba.
///
/// Esto es lo contrario, y son tres reglas, no una:
///
///  1. NO TOCA EL BRONCE. Entra un <see cref="SurfaceMap"/>, sale un <see cref="PlataApp"/>. Se
///     puede recalcular cuantas veces se quiera y el mismo bronce da siempre la misma plata: no
///     depende del orden en que se paseó ni de en qué orden enumere el diccionario.
///
///  2. TODA AFIRMACIÓN TRAE SU EVIDENCIA. Cromo no es un booleano que alguien puso: es «visto en 9
///     de 11 pantallas observadas». Cada salida y cada pantalla llevan su <see cref="Porque"/>, y
///     lo que no se puede sostener con evidencia se queda en <see cref="Clase.SinCruzar"/> —
///     honestamente sin saber— en vez de rellenarse. Un hueco tapado con una suposición es un
///     hueco que nadie va a arreglar nunca; eso ya está escrito en el pintor y aquí también rige.
///
///  3. LO DECLARADO NO SE MEZCLA CON LO DERIVADO: se CONTRASTA. Lo que dijo una persona gana (sabe
///     algo que este cálculo no), pero se anota como desacuerdo; lo que dijo un modelo no gana
///     nada — se compara. Por eso la métrica de avance deja de ser «cuántas declaraciones hay» y
///     pasa a ser «cuánto explica la derivación y en qué discrepa de lo declarado». Una métrica
///     que se sube escribiendo es la que produjo la plata falsa.
///
/// Deliberadamente NO escribe en el mapa todavía. Vive en paralelo a la plata declarada para poder
/// verlas una al lado de la otra sobre el mismo grafo —lo mismo que hace <see cref="ProfundidadClasica"/>
/// con el dibujo— y sustituirla cuando le gane, no antes.
/// </summary>
public static class Plata
{
    // ── Los umbrales, en un solo sitio y con su razón ────────────────────────────────────────

    /// <summary>Por debajo de esto no hay app que juzgar: con dos pantallas observadas, «está en
    /// todas» no significa nada. La permanencia es una afirmación estadística y necesita muestra.</summary>
    public const int MinPantallasParaOpinar = 3;

    /// <summary>Qué fracción de las pantallas observadas tiene que contener una salida para que sea
    /// mobiliario. No es 1.0 a propósito: un panel lateral se pliega, una barra desaparece en modo
    /// pantalla completa, y exigir unanimidad haría que una sola pantalla rara tumbara el cromo.</summary>
    public const double UmbralPermanencia = 0.60;

    /// <summary>Cuántos hermanos del mismo tipo hacen de una población CONTENIDO y de su pantalla un
    /// CONTENEDOR. Una pantalla con tres pestañas es estructura; con cuarenta filas, una lista.</summary>
    public const int MinHermanosContenido = 8;

    // ── Lo que sale ──────────────────────────────────────────────────────────────────────────

    /// <summary>De dónde sale una afirmación. Sin esto, plata vuelve a ser un número sin autor.</summary>
    public sealed record Porque(string Regla, string Evidencia, double Confianza);

    /// <summary>
    /// Qué es una salida. <see cref="SinCruzar"/> no es un fallo del cálculo: es el estado honesto
    /// de una puerta que se ha visto y nunca se ha abierto, y saber cuántas hay es justamente la
    /// medida de lo que falta por explorar.
    /// </summary>
    public enum Clase
    {
        /// <summary>Se ve, nadie la ha cruzado. No se sabe a dónde lleva ni si lleva.</summary>
        SinCruzar,
        /// <summary>Cruzada: lleva a otra pantalla. Es la única clase PROBADA por ejecución.</summary>
        Navegacion,
        /// <summary>Mobiliario: está en (casi) todas las pantallas y siempre lleva al mismo sitio.</summary>
        Cromo,
        /// <summary>Mismo selector, destino distinto según desde dónde se pulse: volver, subir.</summary>
        Relativa,
        /// <summary>Uno de muchos hermanos iguales: un archivo, una fila, un resultado.</summary>
        Contenido,
        /// <summary>Hace algo aquí y no lleva a ninguna parte. Hoy SOLO llega declarada — ver la
        /// nota sobre lo que le falta al bronce en <see cref="Derivar"/>.</summary>
        Accion,
    }

    /// <summary>Una salida de la app, vista por su SELECTOR: el nivel y la clase son propiedades de
    /// la salida, no del sitio desde el que se mire (misma regla que ya rige en FijarNivel).</summary>
    public sealed record SalidaPlata(
        string Selector,
        string Etiqueta,
        string ControlType,
        Clase Clase,
        Porque Porque,
        int EnCuantasPantallas,
        int DestinosDistintos,
        bool Cruzada,
        Clase ClaseDeclarada,
        bool DeclaradaPorPersona);

    /// <summary>Una pantalla. La profundidad es DERIVADA: camino más corto por aristas
    /// estructurales, que es lo que significa «cuántas puertas hay que abrir para verla».</summary>
    public sealed record PantallaPlata(
        string Id,
        int Profundidad,
        Porque Porque,
        bool EsContenedor,
        string TipoDeContenido,
        int CuantosAprox,
        IReadOnlyList<string> Afordancias);

    /// <summary>Derivado y declarado dicen cosas distintas del mismo sitio. No es un error: es el
    /// material de trabajo — o el cálculo aprende algo, o la declaración estaba mal.</summary>
    public sealed record Desacuerdo(string Selector, string Etiqueta, string Derivado, string Declarado, bool DeUnaPersona);

    /// <summary>
    /// La medida que sustituye a «N situadas · M sin situar». Aquella subía escribiendo; esta solo
    /// sube si el sistema entiende más, y por eso sirve de criterio de terminado para un agente.
    /// </summary>
    public sealed record Metricas(
        int PantallasObservadas,
        int PantallasSituadas,
        int SalidasTotal,
        int SalidasConEvidencia,
        int SinCruzar,
        int Desacuerdos,
        int DeclaradasSinEvidencia)
    {
        /// <summary>Cuánto de la superficie explica la DERIVACIÓN. 1.0 no es «terminado»: es
        /// «no queda nada que este cálculo pueda decidir con lo que hay en el bronce».</summary>
        public double Cobertura => SalidasTotal == 0 ? 0 : (double)SalidasConEvidencia / SalidasTotal;
    }

    public sealed record PlataApp(
        string App,
        string Raiz,
        IReadOnlyDictionary<string, PantallaPlata> Pantallas,
        IReadOnlyDictionary<string, SalidaPlata> SalidasPorSelector,
        IReadOnlyList<Desacuerdo> Desacuerdos,
        Metricas M)
    {
        /// <summary>Lo que el dibujo necesita: pantalla → fila. Se expone aparte para que el pintor
        /// no tenga que saber nada de cómo se derivó.</summary>
        public IReadOnlyDictionary<string, int> Profundidades =>
            Pantallas.ToDictionary(kv => kv.Key, kv => kv.Value.Profundidad, StringComparer.OrdinalIgnoreCase);
    }

    // ── La derivación ────────────────────────────────────────────────────────────────────────

    // ── La derivación, servida ───────────────────────────────────────────────────────────────

    private static readonly object _candado = new();

    /// <summary>
    /// La caché va colgada del MAPA que la produjo, no de esta clase.
    ///
    /// Un diccionario estático parecía suficiente —clave: la app; caducidad: la versión— y es
    /// falso en cuanto hay dos mapas vivos a la vez: el contrato crea uno por promesa, todos
    /// arrancan con la versión en cero y todos hablan de «fake.exe», así que el segundo se habría
    /// comido la plata del primero. Un caché que confunde dos mundos es peor que no tener caché.
    ///
    /// `ConditionalWeakTable` ata la entrada a la vida del mapa: cuando el mapa se va, se va con él.
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        SurfaceMap, Dictionary<string, (int Version, PlataApp Plata)>> _cache = new();

    /// <summary>
    /// La plata de una app, derivada una vez por cada cambio del mapa.
    ///
    /// Existe por dónde se usa: desde que el cromo derivado alimenta las rutas, esto acaba llamado
    /// por cada punto y en cada cuadro del pintor —el mismo camino caliente que ya obligó a
    /// <see cref="SurfaceMap.SelectoresCromo"/> a resolverse de una pasada—. Derivar ahí sería
    /// meter un cálculo O(aristas) donde antes había una consulta a un conjunto.
    ///
    /// La llave es <see cref="SurfaceMap.Version"/>, que es lo que el mapa mueve cuando aprende
    /// algo. Es la misma llave que ya usa la caché de <c>CromoDe</c>, a propósito: dos cachés del
    /// mismo hecho con criterios de caducidad distintos acaban contestando cosas distintas.
    /// </summary>
    public static PlataApp DerivadaDe(SurfaceMap mapa, string app)
    {
        if (app.Length == 0) return Derivar(mapa, app);
        lock (_candado)
        {
            var suyas = _cache.GetValue(mapa,
                _ => new Dictionary<string, (int, PlataApp)>(StringComparer.OrdinalIgnoreCase));
            if (suyas.TryGetValue(app, out var guardada) && guardada.Version == mapa.Version)
                return guardada.Plata;
            var fresca = Derivar(mapa, app);
            suyas[app] = (mapa.Version, fresca);
            return fresca;
        }
    }

    /// <summary>
    /// Del bronce de una app a su plata. Puro: no muta el mapa, no escribe en disco, no depende de
    /// la hora ni del orden de enumeración.
    ///
    /// LO QUE HOY NO SE PUEDE DERIVAR, Y POR QUÉ IMPORTA DECIRLO: <see cref="Clase.Accion"/>. Para
    /// afirmar «esto hace algo pero no lleva a ninguna parte» hace falta la evidencia de que se
    /// pulsó y la pantalla NO cambió — y el bronce no la guarda: `LearnTraversal` solo acuña
    /// aristas cuando el destino es OTRO nodo, así que un clic que no movió nada no deja rastro.
    /// Eso no es un hueco de plata, es un hueco de BRONCE, y la plata lo señala en vez de taparlo
    /// con la heurística del nombre del botón (que es exactamente cómo se cuela lo artificial).
    /// </summary>
    public static PlataApp Derivar(SurfaceMap mapa, string app)
    {
        bool DeLaApp(string id) => SurfaceMap.AppDe(id).Equals(app, StringComparison.OrdinalIgnoreCase);

        var pantallas = mapa.Nodes
            .Where(kv => DeLaApp(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

        var aristas = mapa.Edges()
            .Where(e => DeLaApp(e.From) && e.Info.Selector.Length > 0)
            .ToList();

        // OBSERVADA ES LA PANTALLA A LA QUE ALGUIEN LE MIRÓ LAS SALIDAS, no cualquier nodo del mapa.
        // La permanencia es una fracción, y meter en el denominador pantallas de las que no se
        // apuntó nada hundiría el porcentaje de todo el mobiliario: el cromo parecería no estar en
        // sitios donde nunca se miró si estaba.
        var observadas = new HashSet<string>(
            aristas.Select(e => e.From)
                   .Concat(pantallas.Where(kv => kv.Value.UltimaObservacion != default).Select(kv => kv.Key)),
            StringComparer.OrdinalIgnoreCase);
        int n = observadas.Count;

        // ── Lo que el bronce dice de cada SELECTOR, agrupado una sola vez ────────────────────
        var porSelector = aristas
            .GroupBy(e => e.Info.Selector, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var salidas = new Dictionary<string, SalidaPlata>(StringComparer.Ordinal);
        var desacuerdos = new List<Desacuerdo>();

        // CONTENIDO: se decide por pantalla y no por selector, porque «ser uno de muchos» es una
        // propiedad de la POBLACIÓN. Un TreeItem suelto en una barra lateral es navegación; cuarenta
        // TreeItem hermanos son una lista de archivos. Se calcula antes que nada porque el contenido
        // no puede ser cromo ni estructura, y así no compite con las demás reglas.
        var esContenido = new HashSet<string>(StringComparer.Ordinal);
        var contenedores = new Dictionary<string, (string Tipo, int Cuantos)>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in aristas.GroupBy(e => e.From, StringComparer.OrdinalIgnoreCase))
            foreach (var porTipo in g.GroupBy(e => e.Info.ControlType, StringComparer.OrdinalIgnoreCase))
            {
                if (porTipo.Count() < MinHermanosContenido) continue;
                if (!EsTipoDeLista(porTipo.Key)) continue;
                foreach (var e in porTipo) esContenido.Add(e.Info.Selector);
                if (!contenedores.TryGetValue(g.Key, out var ya) || porTipo.Count() > ya.Cuantos)
                    contenedores[g.Key] = (porTipo.Key, porTipo.Count());
            }

        foreach (var (selector, grupo) in porSelector)
        {
            var info = grupo[0].Info;
            var origenes = grupo.Select(e => e.From).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var destinos = grupo.Select(e => e.To)
                .Where(t => !SurfaceMap.EsPuerta(t) && DeLaApp(t))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            bool cruzada = grupo.Any(e => e.Info.Explored && !SurfaceMap.EsPuerta(e.To));
            int k = origenes.Count;

            Clase clase;
            Porque porque;

            if (esContenido.Contains(selector))
            {
                clase = Clase.Contenido;
                int hermanos = origenes
                    .Select(o => contenedores.TryGetValue(o, out var c) ? c.Cuantos : 0)
                    .DefaultIfEmpty(0).Max();
                porque = new Porque("población",
                    $"uno de {hermanos} «{info.ControlType}» hermanos en la misma pantalla", 0.9);
            }
            // RELATIVA ANTES QUE CROMO, y esto es una derivación que hoy es una lista de nombres.
            // `SurfaceMap.EsRelativo` reconoce upButton/backButton/forwardButton escritos a mano —
            // vale para el explorador de Windows y para nada más. La evidencia que de verdad los
            // distingue está en el bronce: el mismo selector con DOS destinos reales distintos no
            // puede ser un atajo global, porque un atajo lleva siempre al mismo sitio. Eso es cierto
            // en cualquier app y sin conocerla, que es el punto entero de derivar.
            else if (destinos.Count >= 2)
            {
                clase = Clase.Relativa;
                porque = new Porque("destino variable",
                    $"el mismo selector lleva a {destinos.Count} pantallas distintas según desde dónde se pulse", 0.95);
            }
            else if (n >= MinPantallasParaOpinar && k >= 2 && (double)k / n >= UmbralPermanencia)
            {
                clase = Clase.Cromo;
                porque = new Porque("permanencia",
                    $"visto en {k} de {n} pantallas observadas", (double)k / n);
            }
            else if (cruzada && destinos.Count == 1)
            {
                clase = Clase.Navegacion;
                porque = new Porque("cruzada",
                    $"se cruzó y llevó a «{SurfaceMap.AppDe(destinos[0])}»/…: navegación probada", 1.0);
            }
            else
            {
                clase = Clase.SinCruzar;
                porque = new Porque("sin evidencia",
                    k > 1 ? $"vista en {k} pantallas, nunca cruzada" : "vista una vez, nunca cruzada", 0.0);
            }

            // LO DECLARADO, APARTE. Se lee para contrastar, no para decidir. `KindDeclarado` y
            // `NivelFijado` son las dos vías por las que alguien afirmó algo sobre esta salida.
            Clase declarada = Clase.SinCruzar;
            if (info.KindDeclarado.Equals("accion", StringComparison.OrdinalIgnoreCase)) declarada = Clase.Accion;
            else if (mapa.EsGestoDeAtras(app, info.Label, selector)) declarada = Clase.Relativa;
            else if (info.NivelFijado && info.EsCromo) declarada = Clase.Cromo;
            else if (info.NivelFijado) declarada = Clase.Navegacion;

            // Y EL DESACUERDO SE ANOTA CONTRA LO QUE DIJO EL CÁLCULO, no contra el resultado final:
            // si se comparara después de aplicar el override, una corrección humana nunca aparecería
            // como desacuerdo —quedarían iguales por construcción— y perderíamos justo la señal que
            // dice dónde el cálculo se queda corto.
            Clase derivada = clase;
            if (declarada != Clase.SinCruzar && declarada != derivada)
                desacuerdos.Add(new Desacuerdo(selector, info.Label, derivada.ToString(), declarada.ToString(),
                    info.PorPersona));

            // LO QUE DIJO UNA PERSONA MANDA. Sabe algo que este cálculo no puede saber: que ese botón
            // raro es navegación principal, que ese otro no lo es. Lo que dijo un MODELO —el maestro
            // de visión, los landmarks de una web— no manda: se contrasta y se anota. Esa asimetría
            // es la que impide que la plata vuelva a ser lo que alguien escribió encima.
            bool loDijoUnaPersona = grupo.Any(e => e.Info.PorPersona && e.Info.NivelFijado);
            if (loDijoUnaPersona && declarada != Clase.SinCruzar)
            {
                clase = declarada;
                porque = new Porque("lo dijo una persona",
                    $"declarado {declarada}; el cálculo decía {derivada}", 1.0);
            }

            salidas[selector] = new SalidaPlata(selector, info.Label, info.ControlType, clase, porque,
                k, destinos.Count, cruzada, declarada, info.PorPersona);
        }

        // ── La profundidad, por camino más corto sobre lo ESTRUCTURAL ────────────────────────
        //
        // Misma idea que SurfaceMap.RecalcularProfundidades —y a propósito, porque aquel cálculo es
        // correcto— con la diferencia que lo cambia todo: allí lo estructural se decide mirando
        // `EsCromo`, que solo existe si alguien lo declaró; aquí se decide con el cromo DERIVADO de
        // arriba. Esa es la línea entre una transformación y una anotación.
        //
        // LA RAÍZ ES DATO DE BRONCE, NO UNA ELECCIÓN: la pantalla por la que se entró en la app la
        // marca `ObserveExits` con nivel 0 la primera vez que se ve. Aquí había un respaldo —a
        // falta de ancla, la más visitada— y se quita: sin ancla, la más visitada es una ADIVINANZA
        // con forma de dato, y toda la jerarquía colgaría de ella. Sin raíz no se sitúa nada y se
        // dice; es la misma decisión que ya tomó RecalcularProfundidades («adivinarla sería peor»).
        // La marca de raíz es de bronce y tiene campo propio desde la fase 3; el `Nivel == 0` queda
        // como respaldo para los mapas guardados antes, que aún no la traen.
        string raiz = pantallas.Where(kv => kv.Value.EsRaiz || kv.Value.Nivel == 0)
                               .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                               .Select(kv => kv.Key).FirstOrDefault() ?? "";

        var hijos = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var soloPorCromo = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (f, t, info) in aristas)
        {
            if (SurfaceMap.EsPuerta(t) || !DeLaApp(t)) continue;
            var cl = salidas.TryGetValue(info.Selector, out var s) ? s.Clase : Clase.SinCruzar;
            if (cl == Clase.Cromo) { soloPorCromo.Add(t); continue; }
            if (cl == Clase.Contenido || cl == Clase.Relativa || cl == Clase.Accion) continue;
            if (!hijos.TryGetValue(f, out var l)) hijos[f] = l = new List<string>();
            if (!l.Contains(t, StringComparer.OrdinalIgnoreCase)) l.Add(t);
        }

        var prof = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var porqueProf = new Dictionary<string, Porque>(StringComparer.OrdinalIgnoreCase);
        var cola = new Queue<string>();

        void Sembrar(string id, int p, Porque pq)
        {
            if (prof.ContainsKey(id)) return;
            prof[id] = p;
            porqueProf[id] = pq;
            cola.Enqueue(id);
        }

        void Recorrer()
        {
            while (cola.Count > 0)
            {
                string aqui = cola.Dequeue();
                if (!hijos.TryGetValue(aqui, out var l)) continue;
                // El orden de los hermanos no puede cambiar el resultado: se recorren ordenados.
                foreach (var h in l.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    Sembrar(h, prof[aqui] + 1, new Porque("camino más corto",
                        $"a {prof[aqui] + 1} puerta(s) estructural(es) de la raíz", 1.0));
            }
        }

        if (raiz.Length > 0)
            Sembrar(raiz, 0, new Porque("raíz", "la pantalla por la que se entra en la app", 1.0));

        // LAS PANTALLAS QUE SITUÓ UNA PERSONA SE SIEMBRAN CON SU NIVEL, y desde ellas se sigue
        // bajando. No es una excepción al cálculo: es la misma regla de autoridad de arriba, vista
        // desde las pantallas en vez de desde las salidas. Sin esto, hacer de esta derivación la
        // fuente de `RecalcularProfundidades` habría movido lo que alguien fijó a mano — que es
        // exactamente lo que la promesa 3 del contrato lleva desde el principio impidiendo.
        var situadasAMano = mapa.Edges()
            .Where(e => e.Info.NivelFijado && e.Info.PorPersona && e.Info.NivelNav >= 0
                     && !SurfaceMap.EsPuerta(e.To) && DeLaApp(e.To))
            .GroupBy(e => e.To, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Min(e => e.Info.NivelNav), StringComparer.OrdinalIgnoreCase);
        foreach (var (id, nivel) in situadasAMano.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            Sembrar(id, nivel, new Porque("lo situó una persona", $"declarada en el nivel {nivel}", 1.0));

        Recorrer();

        // Lo que solo se alcanza por mobiliario es una sección de primer nivel: está a un clic de
        // todas partes, y eso es exactamente lo que significa vivir en el primer nivel.
        //
        // Y SE SIGUE BAJANDO DESDE AHÍ, que es lo que faltaba. Colocarlas y parar dejaba sin situar
        // todo lo que cuelga de ellas: en un explorador de archivos, donde a cada carpeta grande se
        // entra por el panel lateral, eso es la app entera. Lo encontró la promesa 19 del contrato
        // ANTES de que este código existiera — para eso se escribe la spec primero.
        foreach (var t in soloPorCromo.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            Sembrar(t, 1, new Porque("solo por cromo", "se alcanza únicamente desde el mobiliario", 0.8));
        Recorrer();

        var pantallasPlata = new Dictionary<string, PantallaPlata>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in pantallas.Keys)
        {
            bool hayCont = contenedores.TryGetValue(id, out var cont);
            var afordancias = aristas
                .Where(e => e.From.Equals(id, StringComparison.OrdinalIgnoreCase) && EsAfordancia(e.Info.ControlType))
                .Select(e => e.Info.Label.Length > 0 ? e.Info.Label : e.Info.ControlType)
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToList();

            pantallasPlata[id] = new PantallaPlata(
                id,
                prof.TryGetValue(id, out int p) ? p : -1,
                porqueProf.TryGetValue(id, out var pq) ? pq : new Porque("sin situar",
                    raiz.Length == 0
                        ? "no hay raíz observada en esta app: nada tiene desde dónde contarse"
                        : "no hay camino estructural conocido desde la raíz", 0.0),
                hayCont && cont.Cuantos >= MinHermanosContenido,
                hayCont ? cont.Tipo : "",
                hayCont ? cont.Cuantos : 0,
                afordancias);
        }

        var m = new Metricas(
            PantallasObservadas: n,
            PantallasSituadas: prof.Count,
            SalidasTotal: salidas.Count,
            SalidasConEvidencia: salidas.Values.Count(s => s.Clase != Clase.SinCruzar),
            SinCruzar: salidas.Values.Count(s => s.Clase == Clase.SinCruzar),
            Desacuerdos: desacuerdos.Count,
            DeclaradasSinEvidencia: salidas.Values.Count(s =>
                s.ClaseDeclarada != Clase.SinCruzar && s.Clase == Clase.SinCruzar));

        return new PlataApp(app, raiz, pantallasPlata, salidas, desacuerdos, m);
    }

    /// <summary>
    /// LA DERIVACIÓN ENTERA EN UNA CADENA CANÓNICA: raíz, cada pantalla con su profundidad, cada
    /// salida con su clase; todo ordenado, para que dos derivaciones iguales den cadenas iguales
    /// carácter a carácter.
    ///
    /// Existe porque sin ella la promesa «la misma entrada da la misma plata» no se puede escribir:
    /// comparar dos grafos campo a campo desde el contrato es exactamente el tipo de código que
    /// falla por su cuenta y manda la investigación al sitio equivocado. Y de paso es lo que
    /// permite afirmar lo segundo, que es lo que de verdad importa: que el ORDEN DEL PASEO no
    /// cambia la estructura.
    ///
    /// No lleva la evidencia a propósito — los textos de <see cref="Porque"/> están para leerse,
    /// no para compararse, y meterlos aquí haría que cambiar una palabra rompiera una promesa.
    /// </summary>
    public static string Huella(PlataApp p) =>
        $"raiz={p.Raiz}|"
        + string.Join(",", p.Pantallas.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                            .Select(kv => $"{kv.Key}={kv.Value.Profundidad}"))
        + "|"
        + string.Join(",", p.SalidasPorSelector.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                            .Select(kv => $"{kv.Key}={kv.Value.Clase}"));

    /// <summary>
    /// Lo que hay que leer de una derivación, en una línea. Se escribe solo cuando cambia (quien
    /// llama lleva la huella): el pintor deriva en cada cuadro y esto llenaría el log.
    /// </summary>
    public static string Resumen(PlataApp p) =>
        $"PLATA REAL «{p.App}» · {p.M.PantallasSituadas}/{p.M.PantallasObservadas} pantalla(s) situada(s) · "
        + $"cobertura {p.M.Cobertura:P0} ({p.M.SalidasConEvidencia}/{p.M.SalidasTotal}) · "
        + $"{p.M.SinCruzar} sin cruzar · {p.M.Desacuerdos} desacuerdo(s) · "
        + $"{p.M.DeclaradasSinEvidencia} declarada(s) sin evidencia";

    /// <summary>El detalle que hace falta para ARREGLAR algo, no solo para saber que está mal.</summary>
    public static void Registrar(PlataApp p)
    {
        LogBus.Log("plata", Resumen(p));
        foreach (var d in p.Desacuerdos.Take(10))
            LogBus.Log("plata", $"desacuerdo: «{d.Etiqueta}» — derivado {d.Derivado}, declarado {d.Declarado}"
                + (d.DeUnaPersona ? " POR UNA PERSONA (manda la persona)" : " por un modelo (no manda)"));
    }

    // ── Los tipos, en un sitio y no repartidos ───────────────────────────────────────────────

    /// <summary>Tipos de control que APARECEN EN POBLACIÓN: filas, elementos de lista, celdas. No es
    /// una lista de nombres de botones —eso sería adivinar—: es la forma que toma el contenido en
    /// UIA, y solo cuenta cuando hay muchos hermanos iguales.</summary>
    private static bool EsTipoDeLista(string controlType) =>
        controlType.Equals("listitem", StringComparison.OrdinalIgnoreCase)
        || controlType.Equals("treeitem", StringComparison.OrdinalIgnoreCase)
        || controlType.Equals("dataitem", StringComparison.OrdinalIgnoreCase);

    /// <summary>Con qué se ESTRECHA un contenedor: un campo donde escribir, un desplegable de orden.
    /// Es la pieza que pedía la doctrina del contenido (docs/graphify.md, 2026-08-10): «aquí hay mil
    /// archivos» no sirve; «y este es el campo donde se buscan», sí.</summary>
    private static bool EsAfordancia(string controlType) =>
        controlType.Equals("edit", StringComparison.OrdinalIgnoreCase)
        || controlType.Equals("combobox", StringComparison.OrdinalIgnoreCase);
}
