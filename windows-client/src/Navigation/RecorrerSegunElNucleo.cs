namespace U.WindowsClient.Navigation;

/// <summary>
/// RECORRER EN BATCH: varios pasos de una sola llamada, con la compuerta de vida antes de cada uno.
/// </summary>
/// <remarks>
/// ES EL PATRÓN DEL computer_batch DEL AGENT SDK, aplicado a nuestro terreno. Su contrato, medido
/// el 2026-08-24: una llamada lleva una lista de acciones; se ejecutan en secuencia; LA COMPUERTA
/// CORRE ANTES DE CADA UNA (allí: ¿la app del frente está permitida?); a la primera que no pasa, el
/// batch se detiene y el modelo recupera el control con lo que alcanzó a hacer. La velocidad no es
/// magia: es predecir N pasos y pagar UN viaje al modelo en vez de N.
///
/// NUESTRA COMPUERTA ES MEJOR QUE LA SUYA: no «¿está permitida la app?» sino «¿el elemento que toca
/// pulsar está VIVO aquí?». La bandera de vida del núcleo —que ya distingue «lo veo ahora» de «lo
/// recuerdo» (promesas 1, 6 y 11)— puesta en la puerta. Y por selector, no por coordenadas: la
/// regla de la casa, porque las coordenadas fallan en silencio y reportan éxito.
///
/// CADA PASO FABRICA UNA ARISTA. Pulsar pasa por <see cref="PulsarSegunElNucleo"/>, que verifica
/// por consecuencia y cruza el tramo — y aquí la atribución es trivial porque el que pulsó fuimos
/// NOSOTROS. Es la salida al problema de los nodos incomunicados: las aristas entre ubicaciones no
/// se deducen mirando (adivinar clics humanos falla a cada rato, «salto SIN atribuir» en los logs),
/// se GANAN ejecutando (2026-08-24, propuesto por el usuario).
///
/// LA RESPUESTA NUNCA MIENTE: si hizo 3 de 5, dice 3 de 5, dónde quedó, por qué paró y qué SÍ está
/// vivo ahí — lo que el modelo necesita para replanificar sin gastar otra llamada de
/// reconocimiento, igual que el screenshot intercalado del computer_batch.
/// </remarks>
public sealed class RecorrerSegunElNucleo
{
    /// <summary>Un paso: pulsar <paramref name="Exit"/> (etiqueta o selector), o escribir
    /// <paramref name="Texto"/> si viene con texto. <paramref name="Llegada"/> es opcional y viene
    /// de una skill enseñada: A DÓNDE llegó ese paso en la demostración — y si viene, SE EXIGE.
    /// <paramref name="Tecla"/> es la que se pulsa DESPUÉS de escribir (o sola, si no hay nada más).
    /// <see cref="Cual"/> (1..N) elige entre varias puertas vivas con el mismo nombre, en el
    /// orden en que el propio batch las numeró (promesa 203); 0 = no se sabe, y entonces no se adivina.</summary>
    /// <remarks>
    /// LA TECLA NO ES UNA PUERTA, y esto costó una corrida entera. El grabador emite el Enter como
    /// un paso con selector «key:enter», y el batch resuelve los pasos contra el terreno: no hay
    /// ninguna puerta llamada «enter» en ningún sitio, así que TODA skill de SAP moría en el primer
    /// paso — «hice 0 de 4 y paré en el paso 1: key:enter no lo conozco» (2026-09-03 12:14:08).
    /// Pulsar una tecla es una ACCIÓN sobre lo que ya está, no un elemento que haya que encontrar.
    /// </remarks>
    public sealed record Paso(string Exit, string Texto = "", string Llegada = "", string Tecla = "")
    {
        // FUERA DEL CONSTRUCTOR a propósito: la promesa 103 construye pasos por reflexión con cuatro
        // argumentos, y reflexión no rellena opcionales. Un quinto parámetro la rompía (2026-09-11).
        public int Cual { get; init; }

        /// <summary>La consulta al tope de intentos de quien llama, con el selector que se VA a pulsar:
        /// null si puede; si no, el motivo, y el paso no se pulsa (promesa 204). La trae el paso porque
        /// solo aquí se sabe qué botón es, se haya pedido como se haya pedido.</summary>
        public Func<string, string?>? AntesDePulsar { get; init; }
    }

    /// <summary>Qué pasó: cuántos se hicieron, de cuántos, dónde quedamos, y el relato honesto.</summary>
    /// <remarks><paramref name="Cambio"/>: si el ÚLTIMO pulsar cambió la pantalla. Quien necesita saber
    /// si una acción se logró —el tope de intentos de la voz, promesa 204— lo lee de aquí y no de la
    /// prosa de <paramref name="Cuenta"/>: concluir leyendo un mensaje es el aprendizaje nº2.</remarks>
    // Ambiguo: la tanda se paró para PREGUNTAR cuál de varios homónimos, sin pulsar nada. No es un
    // intento fallido, y el tope de la voz no lo cuenta como tal (promesa 207, spec 017).
    public readonly record struct Resultado(int Hechos, int Total, string Donde, bool Termino, string Cuenta, bool Cambio = false, bool Ambiguo = false)
    {
        /// <summary>Con <see cref="Ambiguo"/>: los selectores de la lista, en el orden de su número. Viajan
        /// como datos para que el tope sepa que pedir uno por su selector es pedir ese candidato, sin leer
        /// la prosa de la cuenta (promesas 203 y 204; crítico, tercera pasada, 2026-09-11).</summary>
        public IReadOnlyList<string>? Candidatos { get; init; }

        /// <summary>El selector que se pulsó de verdad en la tanda, si se pulsó algo: el tope cuenta los
        /// fallos por el botón tocado, no por cómo se pidió (promesa 204).</summary>
        public string? Pulsado { get; init; }

        /// <summary>
        /// Cuál de los cuatro veredictos dio el ÚLTIMO pulsar (spec 047, promesa 353): de sitio, dentro, delante o nada.
        /// Viaja como dato hasta el detector de bucle del tramo, por <c>Mano</c>, por el mismo camino que
        /// <see cref="Cambio"/>. Quien no lo dice hereda de <see cref="Cambio"/>: cambió = de sitio; no = nada.
        /// </summary>
        public HuellaDeLoQueSeVe.QueCambio QueCambio { get; init; } =
            Cambio ? HuellaDeLoQueSeVe.QueCambio.DeSitio : HuellaDeLoQueSeVe.QueCambio.Nada;
    }

    private readonly Nucleo.Grafo _grafo;
    private readonly Func<string> _donde;
    private readonly PulsarSegunElNucleo _pulsar;
    private readonly Func<string, string, bool>? _escribir;
    private readonly Func<string, bool>? _teclear;
    private readonly Func<bool> _hayQueParar;

    /// <param name="escribir">(campo, texto) → ¿se pudo escribir? Lo hace quien sabe del mundo.</param>
    /// <param name="hayQueParar">El freno. Se pregunta antes de CADA paso, no al empezar la tanda.</param>
    /// <param name="teclear">Nombre de tecla («enter», «f3») → ¿se pudo pulsar? Sin esto, un paso
    /// con tecla no se ejecuta y se dice, en vez de darse por hecho.</param>
    /// <remarks>
    /// EL CAMPO VIAJA CON EL TEXTO (promesa 133). Hasta el 2026-09-03 este delegado recibía SOLO el
    /// texto y escribía donde estuviera el foco; quien cableaba adivinaba el campo por el último
    /// elemento pulsado, con un respaldo que fallaba en cuanto el paso anterior no era ese campo.
    /// Adivinar cuando la respuesta ya viaja dentro del paso es cómo se pierden los datos en
    /// silencio: el paso SABE en qué campo escribió la demo.
    /// </remarks>
    public RecorrerSegunElNucleo(Nucleo.Grafo grafo, Func<string> donde, PulsarSegunElNucleo pulsar,
        Func<string, string, bool>? escribir = null, Func<bool>? hayQueParar = null,
        Func<string, bool>? teclear = null)
    {
        _grafo = grafo;
        _donde = donde;
        _pulsar = pulsar;
        _escribir = escribir;
        _hayQueParar = hayQueParar ?? (() => false);
        _teclear = teclear;
    }

    /// <summary>Cuánto se espera a que un elemento aparezca vivo antes de rendirse: la pantalla
    /// nueva tarda en pintarse y en ser leída, y declarar «no está» sin esperar la lectura sería
    /// juzgar la pantalla de antes.</summary>
    public int EsperaMaximaMs { get; init; } = 1800;

    /// <summary>
    /// Qué selectores se pueden INTENTAR aunque no estén a la vista (promesa 80). El batch no
    /// sabe de mundos: quien cablea decide — hoy, las filas de árbol de SAP (sap:…#node=), cuya
    /// clave cargada se alcanza por identidad: seleccionarla la TRAE a la vista. La verificación
    /// por consecuencia sigue juzgando; sin delegado, la compuerta muerde como siempre.
    /// </summary>
    public Func<string, bool>? AccionableAunSinVerse { get; init; }

    /// <summary>
    /// MIRAR OTRA VEZ ANTES DE RENDIRSE. Promesa 264 (spec 030). Recibe la ubicación y vuelve a observar su
    /// ventana AHORA, dejando el resultado en el grafo; devuelve si vio algo. Nulo = como antes.
    /// </summary>
    /// <remarks>
    /// LA MITAD DE LOS CLICS QUE FALLABAN LOS RECHAZABA ESTA COMPUERTA (37 de 74 en tres días, 2026-09-17): juzga
    /// «vivo» contra la última observación del mapa vivo, que va 1–2 s por detrás de la pantalla y recorta. Medido:
    /// map_pointing_at leyó la pantalla y dijo «puedo pulsarlo ahora»; 26 s después, sobre el mismo botón, esta
    /// compuerta esperó 4 s y contestó «no lo conozco». Dos jueces con dos criterios (aprendizaje nº16), y vetaba el
    /// más viejo. Ahora, antes de esperar y antes de rendirse, se mira otra vez.
    /// </remarks>
    public Func<string, bool>? MiraOtraVez { get; set; }

    /// <summary>
    /// A LOS CUÁNTOS MILISEGUNDOS SE MIRA POR SEGUNDA VEZ para saber si la pantalla está asentada. Promesa 299
    /// (spec 040): si esa mirada ve lo mismo que la primera, esperar no puede traer la puerta y la compuerta se rinde.
    /// </summary>
    /// <remarks>
    /// MEDIDO EL 2026-09-18 en dos sesiones de voz del dueño: 7 `map_take` pidieron una puerta que no estaba —el
    /// modelo inventa nombres: «Dan Kost», «Enter»— y cada «no» costó 4,2-4,6 s, más del triple que pulsar (1,2 s).
    /// El log: dos miradas de 34 y 39 ms que veían LO MISMO, 45 y 45 elementos, con cuatro segundos de espera pura
    /// en medio. La espera existe para la pantalla que está CARGANDO, y una pantalla que carga cambia entre miradas.
    ///
    /// 400 ms y no menos: es del orden de lo que tarda en verse un cambio tras un clic (mediana 405-510 ms ese
    /// mismo día). Más corto, y una página que aún no empezó a pintarse pasaría por asentada.
    ///
    /// LA LLEGADA USA EL MISMO NÚMERO (356, spec 047): antes de esto, lo que se ve tras escribir o teclear no se da por
    /// asentado en ningún sitio, por la misma razón. Un número y no dos.
    /// </remarks>
    public int EsperaDeAsentarMs { get; init; } = 400;

    /// <summary>
    /// Quién mira lo que se ve al comprobar la llegada (spec 047, promesa 356). Nulo = los ojos del pulsar que ya usa este
    /// batch; y si ese tampoco tiene, nadie mira y la llegada se espera como hoy: la ubicación, hasta el techo.
    /// </summary>
    /// <remarks>
    /// LAS MISMAS MANOS, LOS MISMOS OJOS. La spec preveía que FaceWindow lo asignara (`recorrer.Huella = pulsar.Huella`)
    /// dentro de las ≤2 líneas de la fase 0, y esas dos ya las gastó el pulsar (FaceWindow :795-796). No hace falta una
    /// tercera en la zona de choque de la UI: este batch recibe EL MISMO pulsar (FaceWindow :809-812), y sus ojos ya están ahí.
    /// </remarks>
    public Func<HuellaDeLoQueSeVe?>? Huella { get => _huella ?? _pulsar.Huella; set => _huella = value; }
    private Func<HuellaDeLoQueSeVe?>? _huella;

    /// <summary>
    /// CUÁNTO SE SIGUE MIRANDO una pantalla que se asentó en OTRA —ni la esperada ni la de partida— antes de declarar el
    /// desvío (356). Contado desde que se asentó allí. Sin asignar, ES EL TECHO: con eso la llegada no recorta nada.
    /// </summary>
    /// <remarks>
    /// UNA WEB PASA A MENUDO POR UNA INTERMEDIA que se asienta unos cientos de ms y salta: `map_go_to docs.google.com` «y ya
    /// en …/document/u/0 (redirigió)», 12:10:56 del 18-09, spec 044 de Jose (M). Hasta el 22-09 la llegada no lo sabía:
    /// agotaba el techo mirando la ubicación y declaraba el desvío con la última que viera.
    ///
    /// SIN MEDIR, y por eso vale el techo: tiene que salir del p95 de los «ms entre dos cambios de sitio seguidos» en
    /// navegaciones con redirección —la medida (d) de la fase 0—, y esa medida la deja la línea de la llegada en el diario.
    /// Con el techo, una asentada en otra a los N ms vencería a los N + techo: después del techo. Nada se recorta.
    /// </remarks>
    public int PresupuestoDeRedireccionMs { get => _presupuestoDeRedireccionMs ?? EsperaMaximaMs; init => _presupuestoDeRedireccionMs = value; }
    private readonly int? _presupuestoDeRedireccionMs;

    /// <summary>
    /// Cuánto tienen que separarse dos huellas iguales para dar la llegada por asentada. Sin asignar, el del pulsar: es la
    /// misma regla (351), y una META hasta el nivel 4 de la fase 0, así que un número y no dos.
    /// </summary>
    public int RespiroMs { get => _respiroMs ?? _pulsar.RespiroMs; init => _respiroMs = value; }
    private readonly int? _respiroMs;

    /// <summary>
    /// DÓNDE DEJA DICHO LA COMPUERTA cuánto esperó y por qué dejó de esperar. El 2026-09-18 no se pudo medir si la
    /// espera había servido alguna vez: no dejaba línea, y «tardó mucho» no distinguía «esperó a la puerta» de «el
    /// clic era lento». Nulo = callada, como antes.
    /// </summary>
    public Action<string>? Diario { get; set; }

    public Resultado Recorre(IReadOnlyList<Paso> pasos)
    {
        string? pulsado = null;
        var r = RecorreDentro(pasos, s => pulsado = s);
        return pulsado == null ? r : r with { Pulsado = pulsado };
    }

    private Resultado RecorreDentro(IReadOnlyList<Paso> pasos, Action<string> alPulsar)
    {
        if (pasos.Count == 0)
            return new(0, 0, _donde() ?? "", true, "no me diste ningún paso.");

        PulsarSegunElNucleo.Resultado? ultimoPulso = null;   // el último hecho, para no taparlo (202)
        for (int i = 0; i < pasos.Count; i++)
        {
            // EL FRENO SE PREGUNTA ANTES DE CADA PASO, no al arrancar la tanda: una de veinte pasos
            // preguntando solo al principio correría entera con el usuario gritando que pare.
            if (_hayQueParar())
                return Parcial(i, pasos.Count, "paraste tú con Escape; no sigo.", conVivos: false);

            var paso = pasos[i];
            ultimoPulso = null;

            if (paso.Texto.Length > 0)
            {
                if (_escribir == null)
                    return Parcial(i, pasos.Count, "todavía no sé escribir dentro de un batch.", conVivos: false);
                string partida = LaDePartida(paso);
                if (!_escribir(paso.Exit, paso.Texto))
                    return Parcial(i, pasos.Count,
                        $"no pude escribir «{paso.Texto}»"
                        + (paso.Exit.Length > 0 ? $" en «{paso.Exit}»." : "."), conVivos: true);

                // ESCRIBIR NO NAVEGA; la tecla que va detrás, sí. Por eso el Enter viaja pegado al
                // texto y la llegada del paso es la SUYA: exigir aquí la pantalla de antes sería
                // exigir no haberse movido, y exigirla sin haber pulsado el Enter sería exigir un
                // salto que nadie dio.
                if (paso.Tecla.Length > 0 && !Teclea(paso.Tecla, out string porque))
                    return Parcial(i, pasos.Count, porque, conVivos: true);

                if (!LlegoDondeTocaba(paso, partida, out string desvio))
                    return Parcial(i, pasos.Count, desvio, conVivos: true);
                continue;
            }

            // UNA TECLA SOLA es una acción sobre lo que ya está delante —un F3, un F8—: no hay
            // elemento que buscar, así que no pasa por la compuerta de vida. Su llegada sí se exige.
            if (paso.Exit.Length == 0 && paso.Tecla.Length > 0)
            {
                string partida = LaDePartida(paso);
                if (!Teclea(paso.Tecla, out string porque)) return Parcial(i, pasos.Count, porque, conVivos: true);
                if (!LlegoDondeTocaba(paso, partida, out string desvio)) return Parcial(i, pasos.Count, desvio, conVivos: true);
                continue;
            }

            // LA COMPUERTA: el paso solo se pulsa si su elemento está VIVO aquí, y se le da tiempo a
            // la pantalla nueva a pintarse y a ser leída — declarar «no está» sin esperar la lectura
            // sería juzgar la pantalla de ANTES, que es justo el desfase que la compuerta evita.
            var (elegido, homonimos, motivo, aqui) = EsperarloVivo(paso.Exit);

            if (aqui.Length == 0)
                return Parcial(i, pasos.Count, "no sé dónde estoy, y sin eso no pulso nada.", conVivos: false);

            // VARIAS PUERTAS RECLAMAN LO PEDIDO: no se adivina — la misma regla que abrir (promesa
            // 40). Pero se NUMERAN, con su tipo y a dónde lleva cada una, y un paso que trae cuál
            // pulsa esa (promesa 203). Hasta el 2026-09-10 aquí se contestaba «dime el selector», sin
            // número, sin tipo y sin destino: el 2026-08-09 la voz pidió el mismo selector tres veces,
            // 6-7 s cada una, y no llegó (u-20260809.log, 09:37:31). El sistema veía la ambigüedad y
            // se la devolvía al cerebro sin nada con qué resolverla.
            if (homonimos.Count > 1)
            {
                // UN SOLO ORDEN, el de los selectores, para numerar y para elegir: dos órdenes harían
                // que «el 2» de la lista no fuera el 2 que se pulsa.
                var numeradas = homonimos.OrderBy(h => h.Que.Selector, StringComparer.Ordinal).ToList();
                if (paso.Cual >= 1 && paso.Cual <= numeradas.Count)
                    elegido = numeradas[paso.Cual - 1];
                else
                    return Parcial(i, pasos.Count,
                        (paso.Cual > numeradas.Count
                            ? $"pediste la {paso.Cual}, pero para «{paso.Exit}» hay {numeradas.Count} puertas vivas: "
                            : $"hay {numeradas.Count} puertas vivas para «{paso.Exit}»: ")
                        + string.Join("; ", numeradas.Select((h, k) =>
                            $"{k + 1}) «{h.Que.Etiqueta}» ({h.Que.Tipo}, «{h.Que.Selector}»)"
                            + (h.Destino.Length > 0 ? $" → lleva a «{h.Destino}»" : "")))
                        + ". Repite con which=N para pulsar esa; si con esto no sabes cuál, mira la "
                        + "pantalla (map_look) antes de elegir.", conVivos: false)
                        with { Ambiguo = true, Candidatos = numeradas.Select(h => h.Que.Selector).ToList() };
            }

            if (elegido == null)
                return Parcial(i, pasos.Count,
                    motivo ?? $"«{paso.Exit}» no lo conozco en «{aqui}».",
                    conVivos: true);

            // PULSAR pasa por el mismo camino de siempre: verificar por consecuencia (promesas 44 y
            // 45) y cruzar el tramo (46). El batch no inventa una segunda manera de tocar.
            // EL TOPE MIRA LO QUE SE VA A PULSAR, no lo que se pidió (promesa 204; crítico, quinta pasada,
            // 2026-09-11). Cinco pasadas encontraron cinco formas de la misma diferencia —el mismo id antes y
            // después de una lista, cualquier trozo de la etiqueta que LoNombra casa por contención (18
            // toques a un botón con 9 variantes)—, y cada una se cerraba copiando al tope una regla más de
            // este ejecutor. La clase se cierra donde se decide: aquí ya se sabe qué botón es.
            if (paso.AntesDePulsar?.Invoke(elegido.Que.Selector) is string frenado)
                return Parcial(i, pasos.Count, frenado, conVivos: false);
            alPulsar(elegido.Que.Selector);
            // CON LA LLEGADA DEL PASO (revisión del 23-09): pulsar sabe que tiene que haber una navegación y no da la pantalla por
            // asentada antes de que cambie de sitio. De los 3 sitios que juzgan la llegada de un paso, la fase 6 llevó la mirada a
            // 2 (escribir y teclear) y este, el del clic, seguía juzgando `r.Hasta` en el acto sobre una asentada que podía llegar
            // a los ~480 ms, antes que una web lenta.
            var r = _pulsar.PulsaParaLlegar(elegido.Que.Selector, elegido.Que.Etiqueta, paso.Llegada);
            if (!r.SePudo)
                return Parcial(i, pasos.Count, r.Cuenta, conVivos: true);
            ultimoPulso = r;

            // LA LLEGADA SE EXIGE CUANDO SE CONOCE (promesa 103, spec 005). Un paso de skill
            // enseñada trae a dónde llegó en la demostración; aterrizar en otro sitio y seguir
            // sería ejecutar el resto del plan sobre una pantalla que no es — el «29 de 30» del
            // salto-adelante, otra vez. Contar el paso como hecho tampoco: «terminé» deja de ser
            // opinión justo aquí. Sin llegada declarada, nada cambia: «Guardar» sigue siendo un
            // paso legítimo que no va a ninguna parte.
            if (paso.Llegada.Length > 0 && !Superficies.MismaPantalla(paso.Llegada, r.Hasta))   // promesa 203
                return Parcial(i, pasos.Count,
                    $"pulsé «{paso.Exit}» y quedé en «{r.Hasta}», pero la demostración llegaba a "
                    + $"«{paso.Llegada}»: eso NO es haberlo hecho, y no sigo sobre una pantalla que "
                    + "no es la del plan.", conVivos: true);

            // La tecla que sigue a un clic (raro, pero la demo puede haberla dado) va después de
            // haber juzgado la llegada del clic: son dos hechos distintos y se cuentan aparte.
            if (paso.Tecla.Length > 0 && !Teclea(paso.Tecla, out string tras))
                return Parcial(i, pasos.Count, tras, conVivos: true);

            // Que la pantalla no cambiara NO para el batch: «Guardar» o «Cortar» hacen su trabajo
            // sin ir a ninguna parte. Quien juzga si el plan sigue teniendo sentido es la compuerta
            // del paso SIGUIENTE — que mira el terreno, no la intención.
        }

        // EL ÚLTIMO HECHO NO SE TAPA (promesa 202). Hasta el 2026-09-10 esto cerraba SIEMPRE con «hice
        // los N paso(s): quedaste en…» y se tragaba lo que Pulsa ya sabía decir: «pulsé X y la
        // pantalla no cambió». Sin eso el cerebro no puede darse cuenta de que «por aquí no era» —lo
        // que la nota de voz de esa noche pedía con esas palabras— y paga otra mirada para averiguarlo.
        string fin = _donde() ?? "";
        // EL PREFIJO «hice los» SE QUEDA, y no por estilo: la demo de punta a punta (FaceWindow, tres
        // sitios) decide si una tanda terminó leyendo si la cuenta EMPIEZA por «hice los» —el aprendizaje
        // nº2 vivo en otro archivo, anotado en la spec 017—. Lo que cambia es lo que va detrás: el último hecho.
        if (ultimoPulso is { } u)
            return new(pasos.Count, pasos.Count, fin, true,
                $"hice los {pasos.Count} paso(s): {u.Cuenta}",
                u.CambioLaPantalla) { QueCambio = u.QueCambio };
        return new(pasos.Count, pasos.Count, fin, true,
            $"hice los {pasos.Count} paso(s): quedaste en «{fin}».");
    }

    /// <summary>
    /// Pulsa la tecla, o dice POR QUÉ no pudo — y distingue las dos causas, que le sirven distinto
    /// a quien replanifica: «nadie me enseñó a teclear aquí» no es «la tecla no entró».
    /// </summary>
    private bool Teclea(string tecla, out string porque)
    {
        if (_teclear == null)
        {
            porque = $"el paso pide pulsar «{tecla}» y en este montaje no sé teclear.";
            return false;
        }
        if (!_teclear(tecla)) { porque = $"no pude pulsar «{tecla}»."; return false; }
        porque = "";
        return true;
    }

    /// <summary>
    /// LA DE PARTIDA, leída ANTES de actuar (356): asentada ahí no es un desvío, es una página que aún no empezó a
    /// pintarse. Solo si el paso trae llegada y alguien mira: sin ojos se espera como hoy y no se paga ni una lectura más.
    /// </summary>
    private string LaDePartida(Paso paso) => paso.Llegada.Length > 0 && Huella != null ? _donde() ?? "" : "";

    /// <summary>
    /// ¿Aterrizó donde la demostración aterrizaba? La misma exigencia de la promesa 103, ahora
    /// también para escribir y teclear: un Enter que no cambia de pantalla no hizo su trabajo, y
    /// seguir el plan sobre la pantalla de antes es el «29 de 30» del salto-adelante otra vez.
    /// </summary>
    /// <param name="partida">Dónde se estaba antes de actuar (<see cref="LaDePartida"/>); vacío = no se leyó.</param>
    /// <remarks>
    /// LA LLEGADA MIRA LO QUE SE VE (spec 047, promesa 356). Hasta el 2026-09-22 esto agotaba el techo entero mirando solo
    /// la ubicación, y al agotarlo declaraba el desvío con la última que hubiera visto: no sabía si lo que veía estaba
    /// quieto, ni si era la de partida. Con ojos (<see cref="Huella"/>) distingue tres pantallas:
    ///
    ///   · LA ESPERADA: contesta en cuanto la ubicación coincide, como hoy.
    ///   · LA DE PARTIDA, asentada: se sigue esperando hasta el techo, porque una página que aún no empezó a pintarse
    ///     parece asentada.
    ///   · OTRA, asentada: se sigue mirando el presupuesto de redirección, contado DESDE QUE SE ASENTÓ allí —«cuando se
    ///     asentó en otra… se sigue mirando», dice el enunciado—. Si al agotarlo sigue allí, desvío nombrando las dos; si
    ///     en ese plazo llega a la esperada, es una llegada.
    ///
    /// «ASENTADA» ES LA REGLA DE PULSAR, NO UNA SEGUNDA: una <see cref="EsperaAsentada"/> por sitio que se juzga —dos huellas
    /// iguales con un respiro en medio, desde <see cref="EsperaDeAsentarMs"/>, y el sitio releído fresco justo antes—.
    /// «Asentada» se decide en un solo sitio; una segunda manera de decidirla sería el aprendizaje nº16 otra vez.
    /// </remarks>
    private bool LlegoDondeTocaba(Paso paso, string partida, out string desvio)
    {
        desvio = "";
        if (paso.Llegada.Length == 0) return true;

        string que = paso.Texto.Length > 0 ? $"escribí «{paso.Texto}»" : $"pulsé «{paso.Tecla}»";
        // EN EL DIARIO, SIN EL TEXTO (revisión del 23-09): el diario de la app es el log («compuerta») y EspejoDelLog sube cada
        // línea al backend. La línea de la llegada sale en CADA paso de escritura con llegada —en SAP y también cuando sale bien—,
        // y el texto de una skill de IS-H es el documento del paciente. Antes de esta rama solo quedaba en el log si fallaba.
        // La respuesta al modelo (`desvio`) sí lo lleva: es el texto que el propio modelo pidió escribir.
        string queParaElDiario = paso.Texto.Length > 0
            ? $"escribir {paso.Texto.Length} carácter(es)" + (paso.Exit.Length > 0 ? $" en «{paso.Exit}»" : "") + (paso.Tecla.Length > 0 ? $" y pulsar «{paso.Tecla}»" : "")
            : $"pulsar «{paso.Tecla}»";
        var ojos = Huella;
        // POR QUÉ SE ESPERA COMO HOY, con palabras que distinguen cada causa (patrón nº2); vacío = los ojos deciden.
        string comoHoy = PorQueLaLlegadaSeEsperaComoHoy(ojos, partida, paso.Llegada);
        // EL SITIO FRESCO ES EL DEL PULSAR (regla 2b): en la app, sin la memoria de 400 ms; en el contrato, «dónde».
        Func<string> sitioFresco = _pulsar.SitioFresco ?? _donde;

        // LO QUE SE VIO, para la línea del diario: cada sitio nuevo con su instante —la medida (d), de la que tiene que salir
        // el presupuesto— y cada asentada con el suyo.
        var cambios = new List<string>();
        var asentadas = new List<string>();
        string ultimoVisto = partida;
        void Vio(string sitio, long t)
        {
            if (sitio.Length == 0 || Superficies.MismaPantalla(sitio, ultimoVisto)) return;
            cambios.Add($"«{sitio}» a los {t} ms");
            ultimoVisto = sitio;
        }

        // EL SITIO QUE SE JUZGA, con su espera: una por sitio, porque «asentada» es asentada AQUÍ. `quietaDesde` es desde
        // cuándo la huella no cambia en ese sitio: solo sirve para mirar justo cuando la regla ya puede decidir (abajo).
        string juzgado = "";
        long desdeQueSeJuzga = 0, asentadaA = -1, quietaDesde = 0;
        int movidas = 0;
        EsperaAsentada? espera = null;
        void Juzga(string sitio, long t)
        {
            juzgado = sitio; desdeQueSeJuzga = t; asentadaA = -1; quietaDesde = t; movidas = 0;
            // CON EL COMPARADOR DE ESTA LLEGADA (revisión del 23-09): aquí todo sitio se compara con Superficies.MismaPantalla, y la
            // espera comparaba el suyo con Ordinal; con «www.» y sin él, la relectura fresca reiniciaba el juicio una vez.
            espera = new EsperaAsentada(ojos!, sitioFresco,
                HuellaDeLoQueSeVe.De(sitio, "", Array.Empty<string>(), Array.Empty<string>()), RespiroMs, EsperaDeAsentarMs,
                Superficies.MismaPantalla);
        }
        // «Ni la esperada ni la de partida»: la esperada ya contestó arriba en cada vuelta; aquí se descarta la de partida.
        bool AsentadaEnOtra() => asentadaA >= 0 && !Superficies.MismaPantalla(juzgado, partida);

        string PorQueNoDecidio()
        {
            if (espera == null) return "no hubo ubicación que juzgar (el «dónde» llegó vacío en todas las vueltas)";
            if (asentadaA >= 0 && !AsentadaEnOtra())
                return $"asentada en la de partida («{partida}»), y una página que aún no empezó a pintarse parece asentada: ahí no se declara nada antes del techo";
            if (asentadaA >= 0)
                return $"asentada en «{juzgado}» a los {asentadaA} ms, y el presupuesto de redirección ({PresupuestoDeRedireccionMs} ms) no venció antes del techo";
            if (espera.AvisoDelSitio.Length > 0) return espera.AvisoDelSitio + $" (en «{juzgado}»)";
            return espera.VecesQueSeMovio > 0
                ? $"la pantalla no paró de moverse en «{juzgado}» (se movió en {espera.VecesQueSeMovio} de {espera.Sondeos} sondeo(s))"
                : $"no le dio tiempo a asentarse en «{juzgado}»: la miraba desde los {desdeQueSeJuzga} ms ({espera.Sondeos} sondeo(s))";
        }

        void Linea(long t, string porQue) => Diario?.Invoke(
            $"🛬 llegada a «{paso.Llegada}» tras {queParaElDiario}: "
            + (partida.Length > 0 ? $"partida «{partida}»" : "partida sin leer")
            + " · " + (cambios.Count > 0 ? "cambió a " + string.Join(" → ", cambios) : "no cambió de sitio")
            + (asentadas.Count > 0 ? " · " + string.Join(" · ", asentadas) : "")
            + (espera != null ? $" · {espera.Sondeos} sondeo(s) de huella en «{juzgado}»" : "")
            + $" · dejó de esperar a los {t} ms: {porQue}");

        string donde = "";
        // EL RELOJ MANDA (promesa 245): el presupuesto se agota con el tiempo, no con las vueltas. Y es un Stopwatch, no el
        // reloj por defecto del Compas: Environment.TickCount64 avanza a saltos de ~15,6 ms, y aquí se decide por la
        // DIFERENCIA de dos instantes —asentada y presupuesto vencido—, que con ese reloj puede salir 15 ms corta o larga (el
        // mismo hallazgo de la fase 4 con MsHastaElVeredicto). Mide el mismo tiempo de pared: sin ojos, se espera lo mismo.
        var reloj = System.Diagnostics.Stopwatch.StartNew();
        var compasLlegada = new Compas(EsperaMaximaMs, () => reloj.ElapsedMilliseconds);
        for (;;)
        {
            long t = compasLlegada.Transcurrido;
            donde = _donde() ?? "";
            Vio(donde, t);
            // La pantalla cogida a medio cambiar es la misma pantalla (promesa 226): comparar con
            // Equals dejó una comprobación entera en «17 de 19» el 2026-09-11.
            if (Superficies.MismaPantalla(paso.Llegada, donde)) { Linea(t, "llegó"); return true; }

            if (comoHoy.Length == 0 && donde.Length > 0)
            {
                if (espera == null || !Superficies.MismaPantalla(donde, juzgado)) Juzga(donde, t);
                switch (espera!.Sondea(t - desdeQueSeJuzga))
                {
                    case EsperaAsentada.Paso.NoSePudoMirar:
                        comoHoy = espera.Causa;   // ya no se mira: se espera como hoy, y la línea dice por qué
                        break;
                    case EsperaAsentada.Paso.CambioDeSitio:
                        // EL SITIO RELEÍDO FRESCO MANDA (regla 2b): «dónde» sale de una memoria de 400 ms y puede ir por detrás.
                        Vio(espera.SitioAhora, t);
                        if (Superficies.MismaPantalla(paso.Llegada, espera.SitioAhora)) { Linea(t, "llegó (lo vio el sitio releído fresco)"); return true; }
                        Juzga(espera.SitioAhora, t);
                        break;
                    case EsperaAsentada.Paso.Asentada:
                        asentadaA = t;
                        asentadas.Add($"asentada en «{juzgado}» a los {t} ms");
                        break;
                    default:
                        // SE MOVIÓ DESPUÉS DE ASENTARSE: ya no está asentada, y su presupuesto no puede seguir corriendo.
                        if (asentadaA >= 0 && espera.VecesQueSeMovioTrasAsentarse > 0)
                        {
                            asentadas.Add($"se movió en «{juzgado}» a los {t} ms");
                            Juzga(juzgado, t);
                        }
                        break;
                }
                if (espera != null && espera.VecesQueSeMovio != movidas) { movidas = espera.VecesQueSeMovio; quietaDesde = t; }

                // ASENTADA EN OTRA Y SIGUIÓ ALLÍ EL PRESUPUESTO DE REDIRECCIÓN: ahora sí es un desvío, y no antes.
                if (comoHoy.Length == 0 && AsentadaEnOtra() && t - asentadaA >= PresupuestoDeRedireccionMs)
                {
                    // ANTES DE DECLARARLO, EL SITIO FRESCO (regla 2b), como antes de declarar una asentada.
                    string fresco = "";
                    try { fresco = sitioFresco() ?? ""; }
                    catch (Exception e) { comoHoy = "no pude releer el sitio antes de declarar el desvío: " + EsperaAsentada.Cadena(e); }
                    // Vacío no es «sigue allí» (patrón nº9): sin saber dónde, no se declara nada y se espera como hoy.
                    if (comoHoy.Length == 0 && fresco.Length == 0)
                        comoHoy = "el sitio releído fresco llegó vacío antes de declarar el desvío";
                    if (comoHoy.Length == 0)
                    {
                        Vio(fresco, t);
                        if (Superficies.MismaPantalla(paso.Llegada, fresco)) { Linea(t, "llegó (lo vio el sitio releído fresco)"); return true; }
                        if (Superficies.MismaPantalla(fresco, juzgado))
                        {
                            long alli = t - asentadaA;
                            Linea(t, $"desvío: asentada en «{juzgado}» —ni la esperada ni la de partida— y siguió allí {alli} ms, el presupuesto de redirección ({PresupuestoDeRedireccionMs} ms)");
                            desvio = $"{que} y quedé en «{juzgado}», pero la demostración llegaba a «{paso.Llegada}»: la pantalla se "
                                   + $"asentó en «{juzgado}» y siguió allí {alli} ms sin saltar a la esperada, así que no era una "
                                   + "redirección. Eso NO es haberlo hecho, y no sigo sobre una pantalla que no es la del plan.";
                            return false;
                        }
                        Juzga(fresco, t);
                    }
                }
            }

            // SE MIRA CUANDO SE PUEDE DECIDIR, no hasta 120 ms tarde: en el instante en que la regla ya puede dar la pantalla
            // por asentada (un respiro quieta, y pasada la primera), y en el instante en que vence el presupuesto de
            // redirección. Medido el 22-09 en el caso 1 de la 356: con la vuelta fija de 120 ms y el reloj a saltos, el desvío
            // salió a los 603 ms de un plazo que empieza a los ~500 (100 hasta asentarse, 400 de presupuesto): cada decisión
            // llegaba una vuelta tarde. Sin ojos no cambia nada: la vuelta es la de siempre.
            long siguiente = 120;
            if (comoHoy.Length == 0 && espera != null)
            {
                long ahora = compasLlegada.Transcurrido;
                if (asentadaA < 0)
                {
                    long puedeDecidir = Math.Max(quietaDesde + RespiroMs, desdeQueSeJuzga + EsperaDeAsentarMs);
                    if (puedeDecidir > ahora) siguiente = Math.Min(siguiente, puedeDecidir - ahora);
                }
                else if (AsentadaEnOtra())
                    siguiente = Math.Min(siguiente, asentadaA + PresupuestoDeRedireccionMs - ahora);
            }
            if (!compasLlegada.Respira((int)Math.Clamp(siguiente, 1, 120))) break;
        }

        Linea(compasLlegada.Transcurrido, comoHoy.Length > 0 ? "llegó al techo, como hoy: " + comoHoy : "llegó al techo: " + PorQueNoDecidio());
        desvio = $"{que} y quedé en «{donde}», pero la demostración llegaba a «{paso.Llegada}»: eso "
               + "NO es haberlo hecho, y no sigo sobre una pantalla que no es la del plan.";
        return false;
    }

    /// <summary>
    /// POR QUÉ LA LLEGADA NO MIRA y se espera como hoy (356), con palabras que distinguen cada causa (patrón nº2); vacío =
    /// sí mira. Los casos de la regla 4 de la spec 047 que aplican aquí, con las mismas palabras que en <c>Pulsa</c>.
    /// </summary>
    private static string PorQueLaLlegadaSeEsperaComoHoy(Func<HuellaDeLoQueSeVe?>? ojos, string partida, string llegada)
    {
        if (ojos == null) return "nadie miraba (sin huella inyectada)";
        // Sin la de partida no se distingue «aún no empezó a pintarse» de «se fue a otra»: vacío no es ausente (patrón nº9).
        if (partida.Length == 0) return "no supe dónde estaba antes de actuar (la ubicación llegó vacía), y sin la de partida no se distingue «aún no se pintó» de «se fue a otra»";
        // SAP: desde UIA una sesión es un Pane opaco; lo de dentro no cambiaría nunca y toda pantalla saldría «asentada».
        // Con el presupuesto en el techo no cambiaría nada; el día que la medida (d) lo baje, SAP no puede quedar debajo.
        // En sus dos identidades (al juntar A y B, 2026-09-24): «uia://saplogon.exe/…» es la misma sesión cuando el Scripting
        // no contesta. Este sitio no lo juzga ninguna aserción —la 356 no nombra SAP—; es el segundo de los 2 que tenían la
        // clase de error (patrón nº5), y el de Pulsa lo juzga el caso 4b de la 351.
        if (Teach.Mundos.EsSesionDeSap(partida))
            return PulsarSegunElNucleo.LaUbicacionEsDeSap(partida);
        if (Teach.Mundos.EsSesionDeSap(llegada))
            return PulsarSegunElNucleo.LaUbicacionEsDeSap(llegada);
        return "";
    }

    /// <summary>
    /// ¿Esta etiqueta nombra lo pedido? El difuso va en UNA SOLA dirección a propósito: lo pedido
    /// puede ser un trozo del nombre real —«Copilot» nombra a «Copilot anclado» (promesa 43)— pero
    /// un trozo de la página NO reclama lo pedido. Con las dos direcciones, los fragmentos de texto
    /// que las webs observan como elementos se tragaban los pasos: «que» reclamó «Paso Que No
    /// Existe» y «El» reclamó «El portal asociado a este artículo» (Wikipedia, 2026-08-24, en la
    /// primera prueba real del batch).
    /// </summary>
    private static bool LoNombra(string etiqueta, string exit)
    {
        string a = Nombres.Aplanar(etiqueta), b = Nombres.Aplanar(exit);
        return a.Length > 0 && b.Length > 0 && (a == b || a.Contains(b, StringComparison.Ordinal));
    }

    /// <summary>La cola de una ubicación —«web://x/Portal:Ajedrez» → «Portal:Ajedrez»—, que es como
    /// una persona (y un modelo) nombra el sitio.</summary>
    private static string Cola(string ubicacion)
    {
        int barra = ubicacion.LastIndexOf('/');
        return barra >= 0 && barra < ubicacion.Length - 1 ? ubicacion[(barra + 1)..] : ubicacion;
    }

    /// <summary>
    /// ¿Esta etiqueta PARECE una puerta? Una página web observa fragmentos de texto como elementos
    /// —«,», «[1]», «, dos», «_r_1fi7_», párrafos enteros— y tratarlos como puertas rompía las dos
    /// mitades del batch a la vez: reclamaban pasos por contención, y llenaban la lista de «vivo
    /// aquí» de escombros con los que no se puede replanificar (Wikipedia, 2026-08-25, medido en la
    /// primera corrida del piloto). Lo EXACTO no pasa por aquí: si alguien pide «[1]» con todas sus
    /// letras, se le da — el filtro es para lo difuso y para las listas, donde adivinar cuesta.
    /// </summary>
    private static bool ParecePuerta(string etiqueta)
    {
        string t = etiqueta.Trim();
        if (t.Length == 0 || t.Length > 80) return false;   // un párrafo no es una puerta
        return char.IsLetterOrDigit(t[0]);                  // «, dos», «[1]», «_r_1fi7_», «(beta)»…
    }

    /// <summary>
    /// LA ESCALERA DE RESOLUCIÓN, esperando a que la pantalla se pinte y se lea. En orden:
    ///
    ///   1. SELECTOR exacto — quien lo da ya eligió, y con homónimos es lo único que distingue.
    ///   2. ETIQUETA viva — exacto primero, difuso después y solo entre lo que parece puerta.
    ///   3. DESTINO de una arista viva — exacto por la cola primero, difuso después.
    ///
    /// El peldaño 3 es la lección de la primera corrida real (2026-08-25): el modelo pide a dónde
    /// QUIERE IR —«Portal:Ajedrez»— y la puerta se llama «El portal asociado a este artículo». El
    /// terreno tenía la arista aprendida y aun así costó 24 viajes, porque nadie la consultaba.
    /// Pedir el destino vale tanto como pedir la puerta: es exactamente para esto que el batch
    /// fabrica aristas (promesa 58) — un grafo que gana aristas que luego nadie usa es un mapa de
    /// adorno.
    ///
    /// Devuelve el elegido, o los homónimos si hay empate, o el MOTIVO ya redactado del fallo —
    /// que distingue «lo conozco pero no lo veo», «sé llegar por X pero X no está viva» y «no lo
    /// conozco», porque al modelo le sirven distinto (promesa 15 del núcleo, hablando por el batch).
    /// </summary>
    private (Nucleo.Alcanzable? Elegido, IReadOnlyList<Nucleo.Alcanzable> Homonimos, string? Motivo, string Aqui)
        EsperarloVivo(string exit)
    {
        var nada = Array.Empty<Nucleo.Alcanzable>();
        // EL RELOJ MANDA (promesa 245). Esta es la compuerta que costó 28,8 s en la máquina del dueño:
        // cada vuelta lee la pantalla, y el presupuesto se contaba como si leerla fuera gratis.
        var compasVida = new Compas(EsperaMaximaMs);
        int miradas = 0, vueltas = 0;

        // LA PANTALLA ASENTADA NO SE ESPERA (promesa 299). La huella es lo que una mirada deja en el grafo: dónde
        // estamos y qué puertas están vivas. Dos miradas con la misma huella = nada se está pintando.
        //
        // UNA SOLA DEFINICIÓN DE «CAMBIÓ» (promesa 352, spec 047). Hasta el 2026-09-22 esta compuerta fabricaba su
        // propia huella —un texto «dónde + selectores unidos por |»— mientras la espera de después de pulsar comparaba
        // otra cosa, y «cambió» se calculaba con criterios distintos según el sitio (aprendizaje nº16: dos lados de una
        // comparación que salen de funciones distintas). Ahora se construye por HuellaDeLoQueSeVe.De y se compara por
        // MismaPantallaQueVe, que juzga SOLO el sitio y lo de dentro: las dos partes de siempre, así que la 299 dice lo
        // mismo que decía. Aquí «dentro» son los selectores de las puertas vivas que dejó la mirada, no los RuntimeId de
        // la huella barata de la espera: la misma forma (identidades ordenadas por De) y el mismo comparador. «Delante» y
        // «ventanas» van vacías a propósito: la compuerta no las mira, y MismaPantallaQueVe no las compara.
        HuellaDeLoQueSeVe? huellaDeLaPrimera = null;
        bool asentada = false, seMovio = false;
        long msDeLaUltimaMirada = 0;
        HuellaDeLoQueSeVe HuellaDeLaCompuerta(string donde) => HuellaDeLoQueSeVe.De(
            donde, "", _grafo.DesdeAqui(donde).Where(a => a.Vivo).Select(a => a.Que.Selector), Array.Empty<string>());
        bool Mira(string donde)
        {
            var crono = System.Diagnostics.Stopwatch.StartNew();
            bool vio = MiraOtraVez!(donde);
            msDeLaUltimaMirada = crono.ElapsedMilliseconds;
            return vio;
        }
        // La espera que SIRVIÓ también se dice: es el dato que faltaba para saber cuánta espera hace falta.
        (Nucleo.Alcanzable?, IReadOnlyList<Nucleo.Alcanzable>, string?, string) Hallado(Nucleo.Alcanzable a, string donde, int ido)
        {
            if (vueltas > 0) Diario?.Invoke($"«{exit}» no estaba al pedirla y apareció tras {ido} ms ({miradas} mirada(s))");
            return (a, nada, null, donde);
        }

        for (int ido = 0; ; ido = (int)compasVida.Transcurrido, vueltas++)
        {
            string aqui = _donde() ?? "";
            if (aqui.Length > 0)
            {
                var todos = _grafo.DesdeAqui(aqui);

                // 1. El selector exacto manda.
                var porSelector = todos.FirstOrDefault(
                    a => a.Que.Selector.Equals(exit, StringComparison.Ordinal));
                if (porSelector is { Vivo: true }) return Hallado(porSelector, aqui, ido);

                if (porSelector == null)
                {
                    var vivas = todos.Where(a => a.Vivo).ToList();

                    // 2. ETIQUETA: lo exacto gana a lo difuso. «El» no puede tragarse «El portal
                    // asociado a este artículo», pero «Copilot» sí nombra a «Copilot anclado»
                    // (promesa 43) — por eso el difuso existe, filtrado a lo que parece puerta.
                    var exactas = vivas.Where(
                        a => Nombres.Aplanar(a.Que.Etiqueta) == Nombres.Aplanar(exit)).ToList();
                    var candidatas = exactas.Count > 0 ? exactas
                        : vivas.Where(a => ParecePuerta(a.Que.Etiqueta) && LoNombra(a.Que.Etiqueta, exit)).ToList();

                    if (candidatas.Count == 1) return Hallado(candidatas[0], aqui, ido);
                    if (candidatas.Count > 1)
                        return (null, candidatas, null, aqui);

                    // 3. DESTINO: la arista aprendida contesta por la puerta.
                    var conDestino = vivas.Where(a => a.Destino.Length > 0).ToList();
                    var destExactas = conDestino.Where(
                        a => Nombres.Aplanar(Cola(a.Destino)) == Nombres.Aplanar(exit)).ToList();
                    var destinos = destExactas.Count > 0 ? destExactas
                        : conDestino.Where(a => Nombres.Aplanar(Cola(a.Destino))
                            .Contains(Nombres.Aplanar(exit), StringComparison.Ordinal)).ToList();

                    if (destinos.Count == 1) return Hallado(destinos[0], aqui, ido);
                    if (destinos.Count > 1)
                        return (null, destinos, null, aqui);
                }

                // MIRAR OTRA VEZ, Y NO ES UN BUCLE (promesa 264): una vez al empezar, en el acto, y una vez más
                // antes de rendirse. Ni una más, y no por prudencia: la primera versión miraba «como mucho cada
                // 600 ms», y sobre Wikipedia recién cargada cada mirada costaba 0,8-2,2 s —más que el freno—, así
                // que el «continue» saltaba el reloj de abajo y la compuerta miró 76 veces en 90 s sin rendirse
                // (2026-09-17, nivel 4 de la spec 030). Si al mirar aparece, la vuelta siguiente lo encuentra vivo.
                if (MiraOtraVez != null && miradas == 0)
                {
                    miradas = 1;
                    // Sin haber podido mirar no hay huella, y sin huella no se sabe si está asentada: no se adivina.
                    if (Mira(aqui)) { huellaDeLaPrimera = HuellaDeLaCompuerta(aqui); continue; }
                }

                // ¿ASENTADA? La segunda mirada se adelanta: si ve lo mismo que la primera, esperar no trae nada.
                // Si ve otra cosa, la pantalla se está pintando y la espera de siempre es justo para eso.
                if (huellaDeLaPrimera != null && miradas == 1 && !asentada && ido >= EsperaDeAsentarMs && ido < EsperaMaximaMs)
                {
                    miradas = 2;
                    if (Mira(aqui))
                    {
                        if (HuellaDeLoQueSeVe.MismaPantallaQueVe(HuellaDeLaCompuerta(_donde() ?? ""), huellaDeLaPrimera)) asentada = true; else seMovio = true;
                        continue;   // una vuelta más: la mirada pudo traer la puerta, y eso se comprueba arriba
                    }
                }

                if (asentada || ido >= EsperaMaximaMs)
                {
                    if (MiraOtraVez != null && miradas == 1)
                    {
                        miradas = 2;
                        if (Mira(aqui)) continue;
                    }
                    // LA PANTALLA SE MOVÍA: una mirada más antes de rendirse, que es la de la 264. Solo si mirar es
                    // barato: una mirada que cuesta más que el freno no se repite (76 miradas en 90 s, 2026-09-17).
                    if (MiraOtraVez != null && miradas == 2 && seMovio && msDeLaUltimaMirada < 300)
                    {
                        miradas = 3;
                        if (Mira(aqui)) continue;
                    }

                    Diario?.Invoke(asentada
                        ? $"«{exit}» no está y la pantalla está asentada (dos miradas vieron las mismas puertas vivas): me rindo a los {ido} ms, sin agotar los {EsperaMaximaMs}"
                        : $"«{exit}» no apareció en {ido} ms ({(seMovio ? "la pantalla se movió entre miradas: se esperó entero" : miradas == 0 ? "sin poder mirar" : "sin saber si estaba asentada")}; {miradas} mirada(s))");
                    // LA FILA DESPLAZADA SE INTENTA (promesa 80): conocida y accionable por
                    // identidad —lo dice el delegado, no el batch—, se pulsa aunque no se vea;
                    // la consecuencia juzga. El atasco real: tras un relogin el árbol de NWP1
                    // quedó arriba y «Triage» —cruzada, con destino— quedó fuera de la vista
                    // (2026-08-30, la corrida completa del piloto).
                    if (AccionableAunSinVerse != null)
                    {
                        var desplazada = todos.FirstOrDefault(a => !a.Vivo
                            && AccionableAunSinVerse(a.Que.Selector)
                            && (a.Que.Selector.Equals(exit, StringComparison.Ordinal)
                                || Nombres.Aplanar(a.Que.Etiqueta) == Nombres.Aplanar(exit)
                                || (a.Destino.Length > 0
                                    && Nombres.Aplanar(Cola(a.Destino)) == Nombres.Aplanar(exit))));
                        if (desplazada != null) return (desplazada, nada, null, aqui);
                    }

                    // Se acabó la espera: se redacta el motivo MÁS útil que el terreno permita.
                    if (porSelector != null)
                        return (null, nada, $"«{exit}» lo conozco aquí pero AHORA no lo veo.", aqui);

                    if (todos.Any(a => !a.Vivo && (Nombres.Aplanar(a.Que.Etiqueta) == Nombres.Aplanar(exit)
                            || (ParecePuerta(a.Que.Etiqueta) && LoNombra(a.Que.Etiqueta, exit)))))
                        return (null, nada, $"«{exit}» lo conozco aquí pero AHORA no lo veo.", aqui);

                    // La puerta muerta con destino conocido se dice CON su nombre: es justo lo que
                    // el siguiente intento necesita para no adivinar.
                    var muertaConDestino = todos.FirstOrDefault(a => !a.Vivo && a.Destino.Length > 0
                        && Nombres.Aplanar(Cola(a.Destino)).Contains(Nombres.Aplanar(exit), StringComparison.Ordinal));
                    if (muertaConDestino != null)
                        return (null, nada,
                            $"sé llegar a «{exit}» por «{muertaConDestino.Que.Etiqueta}», pero esa "
                            + "puerta ahora no está viva.", aqui);

                    return (null, nada, $"«{exit}» no lo conozco en «{aqui}».", aqui);
                }
            }
            else if (ido >= EsperaMaximaMs)
                return (null, nada, null, "");

            compasVida.Respira(120);
        }
    }

    /// <summary>
    /// El relato de una tanda que no terminó: cuánto se hizo, por qué se paró, dónde quedamos y —si
    /// aporta— qué SÍ está vivo aquí. Es lo que el modelo necesita para replanificar sin gastar otra
    /// llamada de reconocimiento: el equivalente del screenshot intercalado del computer_batch.
    /// </summary>
    private Resultado Parcial(int hechos, int total, string motivo, bool conVivos)
    {
        string aqui = _donde() ?? "";
        string vivos = "";
        if (conVivos && aqui.Length > 0)
        {
            // PUERTAS, NO ESCOMBROS. En la web lo vivo incluye fragmentos de texto —«,», «[1]»,
            // párrafos— y una lista con eso no sirve para replanificar: el modelo gastaba otra
            // llamada de reconocimiento, que es lo que esta lista existe para evitar (2026-08-25).
            // Primero las que ya tienen arista aprendida: son las que se sabe a dónde llevan.
            var nombres = _grafo.DesdeAqui(aqui)
                .Where(a => a.Vivo && ParecePuerta(a.Que.Etiqueta))
                .OrderByDescending(a => a.Destino.Length > 0)
                .ThenBy(a => a.Que.Etiqueta.Length)
                .Select(a => $"«{a.Que.Etiqueta}»").Distinct().Take(15).ToList();
            if (nombres.Count > 0) vivos = " Vivo aquí: " + string.Join(", ", nombres) + ".";
        }
        return new(hechos, total, aqui, false,
            $"hice {hechos} de {total} y paré en el paso {hechos + 1}: {motivo}"
            + (aqui.Length > 0 ? $" Estás en «{aqui}»." : "") + vivos);
    }
}
