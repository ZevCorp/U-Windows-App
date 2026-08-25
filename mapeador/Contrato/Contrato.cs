using Mapeador;

namespace Mapeador.Pruebas;

/// <summary>
/// EL CONTRATO DEL MAPEADOR: lo que promete el PROCESO que alimenta al grafo, escrito como pruebas
/// que llaman al código real y corren sin abrir una ventana.
///
/// Hermano del contrato del núcleo, y separado a propósito: aquel juzga QUÉ SE SABE, este juzga CÓMO
/// SE SABE. La razón de que exista es que el 2026-08-12 el mapeo pasó de 0,4 s a 2,2 y no había
/// ningún sitio donde esa clase de promesa cupiera — el invariante estaba escrito como dos
/// `Interlocked` sueltos dentro de una clase de WPF, sin nombre y sin forma de probarse.
///
/// Ninguna promesa mide milisegundos. Un umbral de tiempo falla en una máquina cargada o un martes,
/// y un juez que da rojos por motivos ajenos al código enseña a desconfiar del juez.
/// </summary>
internal static class Contrato
{
    private static int _fallos;

    private static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("CONTRATO DEL MAPEADOR (el proceso, aislado)\n");

        Prueba("1. una vuelta a la vez: la que llega tarde se DESCARTA, no espera", UnaALaVez);
        Prueba("2. descartar deja rastro, y se sabe de qué reloj", DescartarSeCuenta);
        Prueba("3. al terminar, la siguiente entra: el candado se suelta", ElCandadoSeSuelta);
        Prueba("4. si la vuelta revienta, el candado se suelta igual", RevantarNoDejaCerrado);
        Prueba("5. los dos relojes no se estorban: el caro no bloquea al barato", DosRelojesIndependientes);
        Prueba("6. con hilos de verdad, nunca hay dos dentro a la vez", NuncaDosDentro);
        Prueba("7. todo rechazo lleva motivo", RechazoConMotivo);
        Prueba("8. de cada coste se guarda también LA PEOR, no solo la media", SeGuardaLaPeor);
        Prueba("9. el pulso no opina: no hay veredictos, solo cuentas", ElPulsoNoOpina);
        Prueba("10. el clic sobre las letras de un botón ES el botón", ElTextoEsSuControl);
        Prueba("11. la coincidencia exacta manda sobre la caída al control", ExactoPrimero);
        Prueba("12. una etiqueta que nombra a varias cosas NO se atribuye", AmbiguoNoSeAtribuye);
        Prueba("13. un clic en un control no cae nunca a un texto suelto", NoSeCaeHaciaTexto);
        Prueba("14. cada app lleva su propia cuenta, y el total se deriva de ellas", CadaAppPorSuLado);
        Prueba("15. cambiar de ventana no es navegar, y no cuenta como fallo", CambiarDeVentanaNoEsFallar);
        Prueba("16. «cambió la pantalla y no el sitio» se cuenta, aunque no deje salto", ElFalloQueNoDejaSalto);
        Prueba("17. el clic que te trajo aquí NO puede ser el que te saca", ElQueTeTrajoNoTeSaca);
        Prueba("18. lo que no contesta a tiempo se abandona, y se dice", NoEsperarParaSiempre);
        Prueba("19. mientras una sigue colgada NO se lanza otra: nada de fuga de hilos", UnaColgadaALaVez);
        Prueba("20. cuando la colgada vuelve, se sigue trabajando", DelCuelgueSeSale);
        Prueba("21. una página se alcanza por su PESTAÑA, no lanzando un programa", LoWebVaPorPestanas);
        Prueba("22. lo que no se reconoce se dice: NUNCA se abre algo al azar", NoAdivinarQueAbrir);
        Prueba("23. la dirección de una página sale de su id, con el esquema que se vio", LaUrlSaleDelId);
        Prueba("24. estar en el sitio ES llegar, si lo que se pidió era el sitio", LlegarAlSitioEsLlegar);

        // MEDIR EL TERRENO. Los errores del grafo ya se detectaban —el código los canta desde hace
        // semanas— pero como texto suelto en el log, y con texto suelto no se puede contestar cuál
        // falla más y en qué apps. Ver docs/specs/medir-el-terreno.md.
        Prueba("25. lo que rompe el grafo se distingue de lo que solo se queda corto", LoQueRompeYLoQueNo);
        Prueba("26. un error sabe de qué app es, sin que nadie se lo diga", ElErrorSabeSuApp);
        Prueba("27. el instante se guarda en UTC: es la clave que cruza con el vídeo", ElInstanteCruzaConElVideo);
        Prueba("28. lo urgente va primero aunque pase menos veces", LoUrgenteNoSeEntierra);

        Console.WriteLine();
        Console.WriteLine(_fallos == 0
            ? "MAPEADOR ÍNTEGRO: el proceso promete lo que dice prometer."
            : $"MAPEADOR ROTO: {_fallos} promesa(s) incumplida(s).");

        Apuntar();
        return _fallos;
    }

    /// <summary>
    /// Deja el veredicto en disco, junto al del núcleo, para poder mirarlo sin correr nada.
    /// </summary>
    /// <remarks>
    /// EN LA MISMA CARPETA Y EN LA MISMA PESTAÑA que el del núcleo, y no en un panel aparte. Las dos
    /// contestan la misma pregunta —«¿qué está garantizado?»— y partirla en dos sitios obliga a
    /// acordarse de mirar los dos; el que se olvida es siempre el que está en rojo. Lo que sí es
    /// otra pregunta es el pulso —«¿qué está pasando AHORA?»—, y por eso esa sí tiene su pestaña.
    ///
    /// Y lo que se enseña es el RESULTADO de correrlas, con su hora, no una lista que alguien
    /// tecleó: una descripción envejece en silencio en cuanto alguien toca el código.
    /// </remarks>
    private static void Apuntar()
    {
        try
        {
            // Se sube desde el binario buscando el repo —el que tenga `nucleo/visor` dentro—. Si no
            // aparece, se calla: no poder dejar la nota no puede tumbar al juez.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "nucleo", "visor"))) dir = dir.Parent;
            if (dir == null) return;

            var filas = _veredicto.Select(v =>
                $"{{\"regla\":{Cita(v.Nombre)},\"cumple\":{(v.Cumple ? "true" : "false")}}}");
            File.WriteAllText(Path.Combine(dir.FullName, "nucleo", "visor", "reglas-mapeador.json"),
                "{\"cuando\":" + Cita(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                + ",\"integro\":" + (_fallos == 0 ? "true" : "false")
                + ",\"reglas\":[" + string.Join(",", filas) + "]}",
                System.Text.Encoding.UTF8);
        }
        catch { }
    }

    private static string Cita(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static readonly List<(string Nombre, bool Cumple)> _veredicto = new();

    // ── Las promesas ─────────────────────────────────────────────────────────

    /// <remarks>
    /// EL FALLO QUE ESTO IMPIDE: un `Timer` de .NET dispara la siguiente vuelta aunque la anterior no
    /// haya vuelto. El intervalo bajó a 120 ms para algo que costaba 200, las llamadas se encolaron,
    /// la cola creció sola, y leer la Maqueta pasó de 0,4 s a 2,2 (2026-08-12).
    ///
    /// Se descarta y NO se encola: si el estado volvió a cambiar, el siguiente latido lo recogerá
    /// igual, y una cola solo serviría para pintar con retraso una foto ya caducada.
    /// </remarks>
    private static void UnaALaVez()
    {
        var v = new VueltaUnica("prueba");
        Debe(v.MeToca(), "la primera entra");
        Debe(!v.MeToca(), "la segunda, con la primera dentro, NO entra");
        Debe(!v.MeToca(), "ni la tercera: no se hace cola");
        v.Termine();
    }

    private static void DescartarSeCuenta()
    {
        var v = new VueltaUnica("pantalla");
        Debe(v.Descartadas == 0, "recién hecha, no ha descartado nada");
        v.MeToca();
        v.MeToca(); v.MeToca();
        Debe(v.Descartadas == 2, "se cuentan las dos que llegaron tarde");
        Debe(v.Nombre == "pantalla", "y se sabe de qué reloj se está hablando");
        v.Termine();

        // Y el candado es el ÚNICO que lleva esa cuenta: tenerla también en el pulso era tener dos
        // opiniones sobre el mismo hecho, que es la avería que este panel existe para no repetir.
        Debe(PulsoDelMapeador.Actual.DescartadasPantalla == PulsoDelMapeador.Actual.Pantalla.Descartadas,
            "el pulso no lleva su propia cuenta: lee la del candado");
    }

    private static void ElCandadoSeSuelta()
    {
        var v = new VueltaUnica("prueba");
        v.MeToca();
        v.Termine();
        Debe(v.MeToca(), "terminada la anterior, la siguiente entra");
        v.Termine();
        Debe(v.Descartadas == 0, "y ninguna de las dos se descartó: no se pisaron");
    }

    /// <remarks>
    /// OLVIDARSE DE SOLTAR NO SE VE COMO UN FALLO, SE VE COMO «SE QUEDÓ PARADO». El candado queda
    /// cerrado para siempre, todas las vueltas siguientes se descartan, y el mapa deja de moverse sin
    /// un solo error en el log. Por eso existe <see cref="VueltaUnica.Corre"/>: suelta en un
    /// `finally` que no se puede omitir.
    /// </remarks>
    private static void RevantarNoDejaCerrado()
    {
        var v = new VueltaUnica("prueba");
        try { v.Corre(() => throw new InvalidOperationException("algo se rompió dentro")); }
        catch (InvalidOperationException) { }
        Debe(v.MeToca(), "tras reventar la vuelta, la siguiente PUEDE entrar");
        v.Termine();
    }

    /// <remarks>
    /// Saber dónde estoy es barato (~45 ms) y va cada 250 ms; leer la pantalla es caro (~1 s) y va
    /// cada 900. Con un candado compartido, la vuelta cara bloquearía a la barata y el sitio actual
    /// se quedaría atrasado — que es exactamente el síntoma de «vaya donde vaya, se queda en
    /// claude.exe» por el otro camino.
    /// </remarks>
    private static void DosRelojesIndependientes()
    {
        var p = PulsoDelMapeador.Actual;
        Debe(!ReferenceEquals(p.Ubicacion, p.Pantalla), "son dos candados, no uno");
        Debe(p.Pantalla.MeToca(), "la vuelta cara entra");
        Debe(p.Ubicacion.MeToca(), "…y la barata entra IGUAL, sin esperarla");
        p.Ubicacion.Termine();
        p.Pantalla.Termine();
    }

    /// <remarks>
    /// Las cinco promesas de arriba pasarían con un `bool` sin `Interlocked`: con un solo hilo nunca
    /// se ve la carrera. Esta la ve. Sin ella, el contrato certificaría un candado que no cierra.
    /// </remarks>
    private static void NuncaDosDentro()
    {
        var v = new VueltaUnica("prueba");
        int dentro = 0, maximo = 0, entraron = 0;
        var hilos = new Thread[8];
        for (int i = 0; i < hilos.Length; i++)
        {
            hilos[i] = new Thread(() =>
            {
                for (int k = 0; k < 4000; k++)
                {
                    if (!v.MeToca()) continue;
                    Interlocked.Increment(ref entraron);
                    int ahora = Interlocked.Increment(ref dentro);
                    // Se apunta el máximo visto. Si el candado no cerrara, aquí se vería un 2.
                    int visto;
                    while (ahora > (visto = Volatile.Read(ref maximo)))
                        Interlocked.CompareExchange(ref maximo, ahora, visto);
                    Interlocked.Decrement(ref dentro);
                    v.Termine();
                }
            });
            hilos[i].Start();
        }
        foreach (var h in hilos) h.Join();

        Debe(maximo == 1, $"nunca hubo más de uno dentro (el máximo visto fue {maximo})");
        Debe(entraron > 0, "y alguien entró: la prueba no pasó por vacía");
        Debe(entraron + v.Descartadas == 8 * 4000, "cada intento acabó o dentro o descartado, ninguno se perdió");
    }

    /// <remarks>
    /// «No se atribuyó» sin causa obliga a volver al log, que es justo de donde el panel viene a
    /// sacarnos. El motivo ES el dato.
    /// </remarks>
    private static void RechazoConMotivo()
    {
        var p = PulsoDelMapeador.Actual;
        int antes = p.Saltos;
        p.Rechazada("el clic ya explicó otro salto", "prueba.exe");
        Debe(p.Saltos == antes + 1, "un rechazo cuenta como salto: el salto ocurrió igual");
        Debe(p.Rechazos.ContainsKey("el clic ya explicó otro salto"), "y queda apuntado con su causa");
        Debe(p.Rechazos.Keys.All(m => !string.IsNullOrWhiteSpace(m)), "ningún motivo está en blanco");
    }

    /// <remarks>
    /// SIN ESTE DESGLOSE NO SE PUEDE CONTESTAR si el mapeo tiene que ser específico por aplicación o
    /// puede ser universal — y de esa respuesta depende la arquitectura entera. En el total
    /// agregado, «la misma causa en todas las apps» y «una causa distinta en cada app» dan
    /// exactamente el mismo número. La promesa fija que los totales SE DERIVAN del desglose: dos
    /// contadores sumando en paralelo acabarían discrepando, y entonces no se podría creer a
    /// ninguno de los dos.
    /// </remarks>
    private static void CadaAppPorSuLado()
    {
        var p = PulsoDelMapeador.Actual;
        int saltosAntes = p.Saltos, aprendidasAntes = p.Aprendidas;

        p.Aprendida("una.exe");
        p.Rechazada("el clic era viejo", "una.exe");
        p.Rechazada("el clic era viejo", "otra.exe");

        Debe(p.Apps["una.exe"].Saltos == 2 && p.Apps["una.exe"].Aprendidas == 1,
            "lo de una.exe se cuenta en una.exe");
        Debe(p.Apps["otra.exe"].Saltos == 1 && p.Apps["otra.exe"].Aprendidas == 0,
            "y lo de otra.exe, en otra.exe: no se mezclan");
        Debe(p.Apps["una.exe"].Rechazos["el clic era viejo"] == 1,
            "el mismo motivo en dos apps NO se suma en una sola");

        Debe(p.Saltos == saltosAntes + 3 && p.Aprendidas == aprendidasAntes + 1,
            "y el total es exactamente la suma de las partes, porque se deriva de ellas");
    }

    /// <remarks>
    /// «NO ERA NAVEGACIÓN» Y «NO SUPIMOS EXPLICAR LA NAVEGACIÓN» SON COSAS OPUESTAS, y hasta el
    /// 2026-08-13 salían en el mismo número. Rechazar un alt-tab es el sistema ACERTANDO: sin esa
    /// valla el grafo acuñó «pulsar Ajustes en la Maqueta lleva a la terminal». Pero al contarlo
    /// como salto fallido, Spotify aparecía con 0 de 2 explicados cuando sus dos saltos eran
    /// cambios de ventana correctos, y el porcentaje decía «mapeamos mal» donde no había nada que
    /// mapear. El usuario lo detectó al ver su propia prueba ensuciada.
    /// </remarks>
    private static void CambiarDeVentanaNoEsFallar()
    {
        var p = PulsoDelMapeador.Actual;
        p.NoEraNavegacion("tercera.exe");
        p.NoEraNavegacion("tercera.exe");

        var a = p.Apps["tercera.exe"];
        Debe(a.NoEranNavegacion == 2, "los cambios de ventana se cuentan");
        Debe(a.Saltos == 0, "…pero NO como saltos: no lo eran");
        Debe(a.Rechazos.Count == 0, "…ni como rechazos: no hubo navegación que rechazar");
    }

    /// <remarks>
    /// EL FALLO QUE NO CUELGA DE NINGÚN SALTO. Todo lo demás que cuenta el panel nace de un cambio
    /// de sitio: sin salto no hay atribución que rechazar ni motivo que apuntar. Una app cuya
    /// identidad no se mueve al navegar —Spotify recorriendo varias pantallas y dejando dos
    /// ubicaciones— salía con cero errores, que se lee como «aquí no pasa nada» cuando lo que pasa
    /// es que no nos enteramos. Callar un fallo entero es peor que contarlo mal.
    /// </remarks>
    private static void ElFalloQueNoDejaSalto()
    {
        var p = PulsoDelMapeador.Actual;
        p.CambioLaPantallaYNoElSitio("cuarta.exe");

        var a = p.Apps["cuarta.exe"];
        Debe(a.CambioLaPantallaYNoElSitio == 1, "se cuenta que la pantalla cambió sin cambiar de sitio");
        Debe(a.Saltos == 0 && a.Rechazos.Count == 0,
            "…y no se disfraza de salto ni de rechazo: es una avería distinta y se mira aparte");
    }

    /// <remarks>
    /// Una media buena con una peor terrible es justo el perfil que se SIENTE lento y no lo parece en
    /// la media. La media de proyectar tapó durante horas unos picos que chocaban con el corte de
    /// 5 s del cliente (2026-08-13).
    /// </remarks>
    private static void SeGuardaLaPeor()
    {
        var p = PulsoDelMapeador.Actual;
        p.Costo("prueba-coste", 10);
        p.Costo("prueba-coste", 900);
        p.Costo("prueba-coste", 10);
        var t = p.Tiempos["prueba-coste"];
        Debe(t.Veces == 3, "se cuentan las veces");
        Debe(t.PeorMs == 900, "y se guarda LA PEOR, que la media esconde");
        Debe(t.TotalMs / t.Veces < 400, "…mientras la media sigue pareciendo buena");
    }

    /// <remarks>
    /// El panel no calcula nada: proyecta estos contadores tal cual. Si el pulso emitiera además un
    /// juicio —«saturado», «lento»— tendríamos dos opiniones sobre el mismo hecho, y esa es
    /// exactamente la avería que llevamos la semana entera pagando. Esta promesa lo vigila leyendo el
    /// propio tipo: si alguien añade un `bool EstaSaturado`, sale en rojo.
    /// </remarks>
    private static void ElPulsoNoOpina()
    {
        var juicios = typeof(PulsoDelMapeador).GetProperties()
            .Where(x => x.PropertyType == typeof(bool))
            .Select(x => x.Name)
            .ToList();
        Debe(juicios.Count == 0,
            "el pulso solo cuenta, no dictamina" + (juicios.Count == 0 ? "" : ": sobra " + string.Join(", ", juicios)));
    }

    /// <remarks>
    /// LA PANTALLA ES LA DE VERDAD: es lo que el núcleo conocía de «maqueta-inicio» el 2026-08-13,
    /// leído de Neo4j. El observador ya había descartado los `Text` duplicados, así que solo quedan
    /// los `Button` — y el vigilante de clics, que resuelve al elemento más interno, devolvía
    /// «Ajustes» como `Text`. Ahí se perdían las aristas.
    /// </remarks>
    private static readonly List<(string Selector, string Etiqueta, string Tipo)> MaquetaInicio = new()
    {
        ("uia:name=Inicio;ct=Button",     "Inicio",     "Button"),
        ("uia:name=Catálogo;ct=Button",   "Catálogo",   "Button"),
        ("uia:name=Informes;ct=Button",   "Informes",   "Button"),
        ("uia:name=Documentos;ct=Button", "Documentos", "Button"),
        ("uia:name=Ajustes;ct=Button",    "Ajustes",    "Button"),
    };

    private static void ElTextoEsSuControl()
    {
        var a = AQuienSeLeDioClic.Resolver(MaquetaInicio, "Ajustes", "Text");
        Debe(a.Hay, "un clic sobre las letras de «Ajustes» sí se atribuye");
        Debe(a.Selector == "uia:name=Ajustes;ct=Button", "y se atribuye al BOTÓN, que es lo que se pulsó");

        // El caso que dejó «maqueta-familia-c» con 35 elementos y cero salidas.
        Debe(AQuienSeLeDioClic.Resolver(MaquetaInicio, "Catálogo", "Text").Hay,
            "y lo mismo con «Catálogo», que falló seis veces seguidas");
    }

    /// <remarks>
    /// Si en la pantalla hay a la vez un `Text` y un `Button` con el mismo nombre —pasa: el filtro
    /// del observador solo quita el `Text` cuando los ve juntos en la MISMA lectura— gana el que
    /// coincide de verdad. Caer al control cuando existe la coincidencia exacta sería inventar.
    /// </remarks>
    private static void ExactoPrimero()
    {
        var pantalla = new List<(string, string, string)>
        {
            ("sel:texto", "Familia C", "Text"),
            ("sel:boton", "Familia C", "Button"),
        };
        Debe(AQuienSeLeDioClic.Resolver(pantalla, "Familia C", "Text").Selector == "sel:texto",
            "clic en el texto → el texto, que es la coincidencia exacta");
        Debe(AQuienSeLeDioClic.Resolver(pantalla, "Familia C", "Button").Selector == "sel:boton",
            "clic en el botón → el botón");
    }

    /// <remarks>
    /// En el escritorio de Windows hay dos cosas llamadas «Nombre» —la columna y la celda— y elegir
    /// una a ojo acuñó una arista falsa que dejó al navegador pulsando lo que no era, para siempre.
    /// Sin camino se puede seguir explorando; con un camino equivocado, no.
    /// </remarks>
    private static void AmbiguoNoSeAtribuye()
    {
        var pantalla = new List<(string, string, string)>
        {
            ("sel:columna", "Nombre", "Header"),
            ("sel:celda",   "Nombre", "SplitButton"),
        };
        var a = AQuienSeLeDioClic.Resolver(pantalla, "Nombre", "Text");
        Debe(!a.Hay, "dos cosas se llaman igual: no se atribuye");
        Debe(a.Candidatos == 2, "…y se dice cuántas eran, para poder contarlo por causa");
    }

    private static void NoSeCaeHaciaTexto()
    {
        var pantalla = new List<(string, string, string)> { ("sel:rotulo", "Total", "Text") };
        Debe(!AQuienSeLeDioClic.Resolver(pantalla, "Total", "Button").Hay,
            "un clic en un Button no se resuelve a un texto suelto del mismo nombre");
        Debe(AQuienSeLeDioClic.Resolver(pantalla, "Total", "Text").Hay,
            "…pero un texto suelto sigue siendo alcanzable por sí mismo");
    }

    /// <remarks>
    /// Los tiempos son los reales del 2026-08-13, en segundos desde el arranque de la prueba: se
    /// llega a datos-adjuntos a los 15, el clic que llevó allí ocurrió también a los 15 —justo
    /// antes—, y a los 18 se sale hacia escritorio. Sin esta valla, ese clic explicaba la salida.
    /// </remarks>
    private static void ElQueTeTrajoNoTeSaca()
    {
        var t0 = new DateTime(2026, 8, 13, 10, 35, 0, DateTimeKind.Utc);
        var elClicQueNosTrajo = t0.AddSeconds(14.6);
        var llegamos          = t0.AddSeconds(15.0);
        var elClicQueNosSaca  = t0.AddSeconds(17.8);

        Debe(!AQuienSeLeDioClic.PuedeExplicarLaSalida(elClicQueNosTrajo, llegamos),
            "el clic anterior a la llegada NO explica la salida");
        Debe(AQuienSeLeDioClic.PuedeExplicarLaSalida(elClicQueNosSaca, llegamos),
            "…y el posterior sí");

        // El borde exacto: un clic simultáneo a la llegada es el que trajo, no el que saca. Ante la
        // duda no se acuña: una arista falsa manda al navegador a pulsar lo que no es, para siempre.
        Debe(!AQuienSeLeDioClic.PuedeExplicarLaSalida(llegamos, llegamos),
            "y en el empate se rechaza: sin camino se sigue explorando, con uno falso no");
    }

    /// <remarks>
    /// LA VALLA CONTRA UIA. Aquí el cuelgue se simula con un candado que no se abre, que es
    /// exactamente lo que hace una ventana que no bombea mensajes: no falla, no vuelve.
    ///
    /// Sin esta prueba, el mecanismo solo se vería funcionar el día que algo se cuelgue de verdad
    /// —y ese día ya sería tarde—. Un mecanismo de seguridad cuyo fallo es SILENCIOSO es justo el
    /// que hay que probar a mano: el 2026-08-13 estuvimos veintiún minutos ciegos sin un error.
    /// </remarks>
    private static void NoEsperarParaSiempre()
    {
        using var nuncaSeAbre = new ManualResetEventSlim(false);
        var v = new SinColgarse(TimeSpan.FromMilliseconds(150));
        int avisos = 0;

        var reloj = System.Diagnostics.Stopwatch.StartNew();
        string r = v.Pregunta(() => { nuncaSeAbre.Wait(); return "tarde"; }, alColgarse: () => avisos++);
        reloj.Stop();

        Debe(r.Length == 0, "lo que no contesta a tiempo devuelve vacío: «no sé dónde estoy»");
        Debe(avisos == 1, "…y se avisa, que es lo que hace visible un mapa ciego");
        Debe(reloj.ElapsedMilliseconds < 1000, $"…y se deja de esperar pronto (esperó {reloj.ElapsedMilliseconds} ms)");
        Debe(v.Colgada, "la pregunta sigue ahí fuera, y el vigía lo sabe");

        nuncaSeAbre.Set();   // se suelta para no dejar el hilo colgado al acabar la prueba
    }

    /// <remarks>
    /// LO QUE CONVIERTE LA VALLA EN SEGURA. Abandonar la espera y volver a preguntar cada 250 ms
    /// contra la MISMA ventana muerta cambiaría un cuelgue por una fuga: cientos de hilos parados.
    /// </remarks>
    private static void UnaColgadaALaVez()
    {
        using var nuncaSeAbre = new ManualResetEventSlim(false);
        var v = new SinColgarse(TimeSpan.FromMilliseconds(150));
        int arranques = 0;

        string Trabajo() { Interlocked.Increment(ref arranques); nuncaSeAbre.Wait(); return "tarde"; }

        v.Pregunta(Trabajo);
        for (int i = 0; i < 20; i++) Debe(v.Pregunta(Trabajo).Length == 0, "sigue sin saberse dónde estamos");

        Debe(arranques == 1, $"veintiuna preguntas y UN solo hilo lanzado (fueron {arranques})");

        nuncaSeAbre.Set();
    }

    /// <remarks>
    /// Y NO SE QUEDA ATASCADO PARA SIEMPRE: cuando la ventana revive, la siguiente vuelta trabaja.
    /// Lo que trae la que volvió se descarta a propósito —contesta dónde estabas hace un buen rato,
    /// y una respuesta caducada sobre dónde estás es la mentira que rebobinaba el mapa (2026-08-12)—.
    /// </remarks>
    private static void DelCuelgueSeSale()
    {
        using var puerta = new ManualResetEventSlim(false);
        var v = new SinColgarse(TimeSpan.FromMilliseconds(150));
        int volvio = 0;

        v.Pregunta(() => { puerta.Wait(); return "la caducada"; });
        Debe(v.Colgada, "colgada");

        puerta.Set();
        Thread.Sleep(120);   // se le da tiempo a la abandonada para terminar

        string r = v.Pregunta(() => "nueva", alVolver: () => volvio++);
        Debe(volvio == 1, "se avisa de que la colgada volvió");
        Debe(r.Length == 0, "…y su respuesta se TIRA: dice dónde estabas hace rato, no dónde estás");
        Debe(!v.Colgada, "ya no hay ninguna colgada");

        Debe(v.Pregunta(() => "por fin") == "por fin", "y la siguiente pregunta funciona con normalidad");
    }

    /// <remarks>
    /// EL FALLO, TRES VECES LA MISMA FORMA: coger la parte izquierda del id y tratarla como el nombre
    /// de un ejecutable. «web://itsmiracleai.com.co/…» buscaba un proceso con ese nombre y, al no
    /// haberlo, intentaba LANZAR un programa llamado así — el usuario lo vio como «no pude traer
    /// "itsmiracleai.com.co" al frente» (2026-08-14). Con SAP habría intentado lanzar «QAS».
    /// </remarks>
    private static void LoWebVaPorPestanas()
    {
        var web = ComoMePongoDelante.De("web://itsmiracleai.com.co/servicios");
        Debe(web.Via == ComoMePongoDelante.Via.PestanaDelNavegador, "una página se alcanza por su pestaña");
        Debe(web.Que == "itsmiracleai.com.co", "…y se busca por el DOMINIO, que es lo que la memoria indexa");

        var sap = ComoMePongoDelante.De("sapgui://QAS/NWP1/SAPLN_WP_FRAMEWORK/0100");
        Debe(sap.Via == ComoMePongoDelante.Via.SapGui, "SAP tiene su propia vía");
        Debe(sap.Que != "QAS", "…y NO se busca «QAS»: es un sistema, no un ejecutable");

        var nativa = ComoMePongoDelante.De("uia://explorer.exe/documentos");
        Debe(nativa.Via == ComoMePongoDelante.Via.Proceso, "una app nativa sí va por su proceso");
        Debe(nativa.Que == "explorer", "…sin el «.exe», que es como se lanza y se enfoca");
    }

    /// <remarks>
    /// LA PROMESA QUE MÁS PROTEGE. Un workflow sellado en «uia://desktop» hizo que se intentara
    /// «lanzar un programa llamado desktop», y el shell resolvió… Docker Desktop (2026-07-31). Abrir
    /// un programa al azar en la máquina de alguien es de las cosas más molestas que puede hacer un
    /// agente, y encima la alineación fallaba igual. Rendirse diciéndolo es seguro; adivinar no.
    /// </remarks>
    private static void NoAdivinarQueAbrir()
    {
        foreach (string raro in new[] { "", "   ", "desktop", "sapgui://", "web://", "uia://", "vaya://cosa" })
            Debe(ComoMePongoDelante.De(raro).Via == ComoMePongoDelante.Via.NoSe,
                $"«{raro}» no se reconoce, y eso se dice en vez de abrir algo al azar");
    }

    /// <remarks>
    /// SI LA PESTAÑA NO ESTÁ ABIERTA HAY QUE ABRIRLA, y para eso hace falta la dirección. Sale del
    /// id, que es lo que hay: el núcleo guarda «web://dominio/ruta» sin query ni fragmento, porque
    /// esos son estado volátil y no ubicación — y esa es justo la dirección de esa pantalla.
    ///
    /// EL ESQUEMA SE RECUERDA, NO SE SUPONE. Poner «https» a ciegas rompe los sitios que solo hablan
    /// http —un portal cautivo, un equipo de la red local— y el fallo sería MUDO: el navegador abre,
    /// no carga, y el mapa dice que no llegó sin decir por qué.
    /// </remarks>
    private static void LaUrlSaleDelId()
    {
        Debe(ComoMePongoDelante.UrlDe("web://github.com/BasedHardware/omi", "https")
             == "https://github.com/BasedHardware/omi", "la ruta se conserva entera");
        Debe(ComoMePongoDelante.UrlDe("web://canva.com", "https") == "https://canva.com",
            "un dominio sin ruta da su raíz");
        Debe(ComoMePongoDelante.UrlDe("web://app.datawifi.co/portal", "http")
             == "http://app.datawifi.co/portal", "y se usa el esquema QUE SE VIO, no el supuesto");

        Debe(ComoMePongoDelante.UrlDe("web://canva.com", "") == "https://canva.com",
            "sin esquema recordado se cae a https, que es lo habitual");
        Debe(ComoMePongoDelante.UrlDe("web://canva.com", "javascript") == "https://canva.com",
            "y un esquema que no es de navegador NO se ejecuta: se cae a https");

        foreach (string noEsWeb in new[] { "uia://explorer.exe/documentos", "sapgui://QAS/NWP1", "web://", "" })
            Debe(ComoMePongoDelante.UrlDe(noEsWeb, "https").Length == 0,
                $"«{noEsWeb}» no es una página: no se inventa una dirección");
    }

    /// <remarks>
    /// «Abre GitHub» con GitHub YA abierto en «…/settings/secrets/actions» contestaba «no hay ningún
    /// camino aprendido de actions hasta ahí»: se traía la pestaña al frente, se comparaba esa
    /// dirección con «web://github.com», no eran iguales, y se buscaba una ruta A PIE dentro de una
    /// web. Decía que no sabía llegar estando ya allí (2026-08-16, lo vio el usuario en el panel).
    /// </remarks>
    private static void LlegarAlSitioEsLlegar()
    {
        Debe(ComoMePongoDelante.EstarEnElSitioBasta("web://github.com",
                "web://github.com/ZevCorp/U-Windows-App/settings/secrets/actions"),
            "pedir el SITIO y estar en una de sus páginas es haber llegado");
        Debe(ComoMePongoDelante.EstarEnElSitioBasta("web://github.com/", "web://github.com"),
            "y la barra final no cambia lo que se pidió");

        // CON RUTA SE PIDE UNA PÁGINA, y estar en otra NO es haber llegado. Aflojar esto convertiría
        // «llévame a mis notificaciones» en «te dejo en cualquier parte de GitHub».
        Debe(!ComoMePongoDelante.EstarEnElSitioBasta("web://github.com/notifications", "web://github.com"),
            "pedir una PÁGINA y estar en el sitio NO es haber llegado");
        Debe(!ComoMePongoDelante.EstarEnElSitioBasta("web://github.com", "web://gitlab.com/algo"),
            "otro sitio no cuenta, aunque se parezca");
        Debe(!ComoMePongoDelante.EstarEnElSitioBasta("uia://explorer.exe/documentos", "uia://explorer.exe/videos"),
            "esto es de la web: dos carpetas distintas siguen siendo dos sitios distintos");
    }

    // ── El arnés ─────────────────────────────────────────────────────────────

    // ── MEDIR EL TERRENO ─────────────────────────────────────────────────────

    private static void LoQueRompeYLoQueNo()
    {
        // LA DIFERENCIA QUE DECIDE POR DÓNDE EMPEZAR. No todos los errores duelen igual: un camino
        // que no se aprendió es un agujero permanente en el mapa —nadie va a volver a pasar por ahí
        // a arreglarlo— mientras que una lectura descartada se arregla sola en la siguiente vuelta.
        // Mezclarlos en una sola lista de «errores» haría que lo urgente se perdiera entre lo que se
        // cura solo.
        Debe(Roto(QueSeRompio.AristaNoCreada) == Gravedad.RompeElGrafo,
            "una arista que no se creó ROMPE el grafo: ese camino no existe para siempre");
        Debe(Roto(QueSeRompio.ElementoNoReconocido) == Gravedad.RompeElGrafo,
            "y un clic que no se supo atribuir es la misma herida, vista un paso antes");
        Debe(Roto(QueSeRompio.IdentidadTransitoria) == Gravedad.RompeElGrafo,
            "un nodo que no corresponde a ninguna pantalla real ensucia MÁS que la ausencia de uno: "
            + "manda a buscar algo que no existe");

        Debe(Roto(QueSeRompio.TerrenoNoLeido) == Gravedad.SeQuedaCorto,
            "una lectura descartada deja terreno sin ver, pero no acuña nada falso: la próxima "
            + "vuelta lo recoge");
        Debe(Roto(QueSeRompio.UbicacionEquivocada) == Gravedad.SeQuedaCorto,
            "y que el ancla frene es que el sistema hizo bien su trabajo, no que el grafo quedara mal");

        // LA GRAVEDAD ES DEL ERROR, NO DE QUIEN LO DETECTA. Si la decidiera quien lo crea, dos
        // sitios podrían discrepar sobre el mismo hecho — y ya hemos pagado eso antes: el observador
        // y el vigilante de clics opinando distinto sobre un mismo botón dejó una app entera sin
        // aristas (ver AQuienSeLeDioClic).
        var aqui = new ErrorDelTerreno(QueSeRompio.AristaNoCreada, "uia://x.exe/a", "d", DateTime.UtcNow);
        var alla = new ErrorDelTerreno(QueSeRompio.AristaNoCreada, "uia://otra.exe/z", "otro", DateTime.UtcNow);
        Debe(aqui.Cuanto == alla.Cuanto,
            "el mismo tipo de error pesa lo mismo lo detecte quien lo detecte y pase donde pase");
    }

    private static void ElErrorSabeSuApp()
    {
        // «¿QUÉ APP FALLA MÁS?» ES LA PREGUNTA. Se contesta agrupando, y agrupar exige que la app
        // salga del propio error y no de que quien lo crea se acuerde de pasarla — de ese olvido
        // vive la mitad de los datos que no sirven para nada.
        Debe(new ErrorDelTerreno(QueSeRompio.PasoDeLargo, "uia://saplogon.exe/sap-logon-800", "", DateTime.UtcNow)
            .App == "saplogon.exe", "de una ubicación de UIA sale el proceso");
        Debe(new ErrorDelTerreno(QueSeRompio.PasoDeLargo, "web://chatgpt.com/c/abc", "", DateTime.UtcNow)
            .App == "chatgpt.com", "de una web, su dominio");
        Debe(new ErrorDelTerreno(QueSeRompio.PasoDeLargo, "sapgui://QAS/NWP1/X/0100", "", DateTime.UtcNow)
            .App == "QAS", "y de SAP, su sistema");

        // Sin ubicación no se inventa una app: quedarse sin agrupar es honesto, agrupar mal no.
        Debe(new ErrorDelTerreno(QueSeRompio.PasoDeLargo, "", "", DateTime.UtcNow).App.Length == 0,
            "y sin ubicación no se inventa nada");
        Debe(new ErrorDelTerreno(QueSeRompio.PasoDeLargo, "una-cosa-rara", "", DateTime.UtcNow).App.Length == 0,
            "ni con una ubicación que no tenga la forma esperada");
    }

    private static void ElInstanteCruzaConElVideo()
    {
        // TODA LA CORRELACIÓN CON EL VÍDEO ES UNA RESTA: el segundo del clip es la hora del error
        // menos la hora en que empezó el trozo grabado. Por eso el instante tiene que ser UTC — con
        // hora local, un error a las 02:30 del cambio de horario cae en dos sitios o en ninguno, y
        // el clip se recorta del momento equivocado justo el día que peor viene.
        var arranque = new DateTime(2026, 8, 24, 20, 0, 0, DateTimeKind.Utc);
        var e = new ErrorDelTerreno(QueSeRompio.AristaNoCreada, "uia://x.exe/a", "d",
            arranque.AddSeconds(37));

        Debe(e.Cuando.Kind == DateTimeKind.Utc, "el instante es UTC, no la hora del reloj de esa máquina");
        Debe((e.Cuando - arranque).TotalSeconds == 37,
            "y el segundo del vídeo sale de restar: no hace falta sincronizar relojes ni marcar el vídeo");

        // Y se puede leer sin abrir nada: la línea dice qué pasó y dónde.
        Debe(e.Linea().Contains("AristaNoCreada") && e.Linea().Contains("uia://x.exe/a"),
            "la línea del log lleva qué se rompió y dónde, para poder buscarla con los ojos");
    }

    private static Gravedad Roto(QueSeRompio q)
        => new ErrorDelTerreno(q, "uia://x.exe/a", "", DateTime.UtcNow).Cuanto;

    private static void LoUrgenteNoSeEntierra()
    {
        var roto = new LoQueSeHaRoto();

        // Treinta lecturas descartadas y UNA arista perdida. Ordenar por frecuencia pondría lo que
        // se cura solo por encima del agujero permanente, y quien mire la tabla empezaría por lo
        // que no hay que arreglar.
        for (int i = 0; i < 30; i++)
            roto.Anotar(QueSeRompio.TerrenoNoLeido, "uia://x.exe/a", "vuelta descartada");
        roto.Anotar(QueSeRompio.AristaNoCreada, "uia://x.exe/a", "salto sin clic que lo explique");

        var tabla = roto.PorTipo();
        Debe(tabla[0].Que == QueSeRompio.AristaNoCreada,
            "la arista perdida va PRIMERA aunque pasara una vez contra treinta: no se arregla sola");
        Debe(tabla[0].Veces == 1 && tabla[1].Veces == 30, "y las cuentas siguen siendo las de verdad");

        // POR APP, SOLO LO QUE ROMPE. Saber que una app acumula lecturas descartadas dice que va
        // lenta; saber que acumula aristas perdidas dice que su terreno está mal, que es lo que se
        // viene a buscar.
        roto.Anotar(QueSeRompio.AristaNoCreada, "uia://saplogon.exe/z", "otra más");
        roto.Anotar(QueSeRompio.AristaNoCreada, "uia://saplogon.exe/w", "y otra");
        var apps = roto.PorApp();
        Debe(apps[0].App == "saplogon.exe" && apps[0].Veces == 2, "la app que más terreno rompe, primera");
        Debe(apps.All(a => a.App != "x.exe" || a.Veces == 1),
            "y las lecturas descartadas de x.exe NO inflan su cuenta: ahí solo se cuenta lo que rompe");

        // AVISA AL INSTANTE. Quien graba o escribe se engancha aquí, y si el aviso no saliera, el
        // recorte de vídeo se perdería el momento — que es justo para lo que existe todo esto.
        ErrorDelTerreno? avisado = null;
        var otro = new LoQueSeHaRoto();
        otro.Ocurrio += e => avisado = e;
        otro.Anotar(QueSeRompio.ElementoNoReconocido, "uia://y.exe/b", "no casó con nada conocido");
        Debe(avisado?.Que == QueSeRompio.ElementoNoReconocido, "el aviso sale en el momento, no al final");
    }

    private static void Prueba(string nombre, Action cuerpo)
    {
        int antes = _fallos;
        try { cuerpo(); }
        catch (Exception e)
        {
            _fallos++;
            for (var x = e; x != null; x = x.InnerException)
                Console.WriteLine($"   ✘ {x.GetType().Name}: {x.Message}");
        }
        bool cumple = _fallos == antes;
        _veredicto.Add((nombre, cumple));
        Console.WriteLine($"{(cumple ? "✔" : "✘")} {nombre}");
    }

    private static void Debe(bool condicion, string promesa)
    {
        if (condicion) return;
        _fallos++;
        Console.WriteLine($"   ✘ {promesa}");
    }
}
