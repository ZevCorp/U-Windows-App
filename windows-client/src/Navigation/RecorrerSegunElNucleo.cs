using U.Graph;

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
    /// </remarks>
    public int EsperaDeAsentarMs { get; init; } = 400;

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
                // LO ESCRITO, POR SU LONGITUD, AQUÍ Y NO EN CADA SALIDA (spec 051, revisión del 2026-09-24):
                // esta cuenta tenía cinco —la respuesta al modelo, la «←» del batch, el rastro que sirve
                // /batches, el «plan · PARÓ» de la comprobación y la «←» de mostrar— y solo una tapada. El
                // modelo no pierde nada: la cuenta contesta a los pasos que él mandó, y nombra el campo.
                if (!_escribir(paso.Exit, paso.Texto))
                    return Parcial(i, pasos.Count,
                        $"no pude escribir {SinValor.Forma(paso.Texto)}"
                        + (paso.Exit.Length > 0 ? $" en «{paso.Exit}»." : "."), conVivos: true);

                // ESCRIBIR NO NAVEGA; la tecla que va detrás, sí. Por eso el Enter viaja pegado al
                // texto y la llegada del paso es la SUYA: exigir aquí la pantalla de antes sería
                // exigir no haberse movido, y exigirla sin haber pulsado el Enter sería exigir un
                // salto que nadie dio.
                if (paso.Tecla.Length > 0 && !Teclea(paso.Tecla, out string porque))
                    return Parcial(i, pasos.Count, porque, conVivos: true);

                if (!LlegoDondeTocaba(paso, out string desvio))
                    return Parcial(i, pasos.Count, desvio, conVivos: true);
                continue;
            }

            // UNA TECLA SOLA es una acción sobre lo que ya está delante —un F3, un F8—: no hay
            // elemento que buscar, así que no pasa por la compuerta de vida. Su llegada sí se exige.
            if (paso.Exit.Length == 0 && paso.Tecla.Length > 0)
            {
                if (!Teclea(paso.Tecla, out string porque)) return Parcial(i, pasos.Count, porque, conVivos: true);
                if (!LlegoDondeTocaba(paso, out string desvio)) return Parcial(i, pasos.Count, desvio, conVivos: true);
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
            var r = _pulsar.Pulsa(elegido.Que.Selector, elegido.Que.Etiqueta);
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
                u.CambioLaPantalla);
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
    /// ¿Aterrizó donde la demostración aterrizaba? La misma exigencia de la promesa 103, ahora
    /// también para escribir y teclear: un Enter que no cambia de pantalla no hizo su trabajo, y
    /// seguir el plan sobre la pantalla de antes es el «29 de 30» del salto-adelante otra vez.
    /// </summary>
    private bool LlegoDondeTocaba(Paso paso, out string desvio)
    {
        desvio = "";
        if (paso.Llegada.Length == 0) return true;

        // La pantalla nueva tarda en pintarse y en ser leída: declarar el desvío sin esperar sería
        // juzgar la de antes, que es el mismo desfase que la compuerta evita antes de pulsar.
        string donde = "";
        // EL RELOJ MANDA (promesa 245): el presupuesto se agota con el tiempo, no con las vueltas.
        var compasLlegada = new Compas(EsperaMaximaMs);
        do
        {
            donde = _donde() ?? "";
            // La pantalla cogida a medio cambiar es la misma pantalla (promesa 226): comparar con
            // Equals dejó una comprobación entera en «17 de 19» el 2026-09-11. La espera la lleva
            // el Compas (promesa 245), no un Sleep de esta función.
            if (Superficies.MismaPantalla(paso.Llegada, donde)) return true;
        }
        while (compasLlegada.Respira(120));

        // Lo escrito por su longitud, como arriba (spec 051): esta cuenta sale por las mismas cinco puertas.
        string que = paso.Texto.Length > 0 ? $"escribí {SinValor.Forma(paso.Texto)}" : $"pulsé «{paso.Tecla}»";
        desvio = $"{que} y quedé en «{donde}», pero la demostración llegaba a «{paso.Llegada}»: eso "
               + "NO es haberlo hecho, y no sigo sobre una pantalla que no es la del plan.";
        return false;
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
        string? huellaDeLaPrimera = null;
        bool asentada = false, seMovio = false;
        long msDeLaUltimaMirada = 0;
        string Huella(string donde) => donde + "\n" + string.Join("|",
            _grafo.DesdeAqui(donde).Where(a => a.Vivo).Select(a => a.Que.Selector).OrderBy(x => x, StringComparer.Ordinal));
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
                    if (Mira(aqui)) { huellaDeLaPrimera = Huella(aqui); continue; }
                }

                // ¿ASENTADA? La segunda mirada se adelanta: si ve lo mismo que la primera, esperar no trae nada.
                // Si ve otra cosa, la pantalla se está pintando y la espera de siempre es justo para eso.
                if (huellaDeLaPrimera != null && miradas == 1 && !asentada && ido >= EsperaDeAsentarMs && ido < EsperaMaximaMs)
                {
                    miradas = 2;
                    if (Mira(aqui))
                    {
                        if (Huella(_donde() ?? "") == huellaDeLaPrimera) asentada = true; else seMovio = true;
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
