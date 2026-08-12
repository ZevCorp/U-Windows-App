using Nucleo;

namespace Nucleo.Pruebas;

/// <summary>
/// EL CONTRATO DEL NÚCLEO NUEVO: lo que el grafo promete, escrito como pruebas que llaman al código
/// real y corren sin abrir una ventana.
///
/// Cada promesa de aquí existe porque su ausencia costó un diagnóstico en el núcleo anterior. No se
/// añade ninguna «por si acaso»: una promesa que nadie ha visto fallar es una que nadie sabe leer.
/// </summary>
internal static class Contrato
{
    /// <summary>
    /// PROMESAS incumplidas, no aserciones. La diferencia costó una medida el 2026-08-12: se rompió
    /// UNA cosa en el grafo —que `Observar` olvidara, como hacía el núcleo anterior—, cayeron DOS
    /// promesas (la 2 y la 6), y el contrato reportó «3 promesa(s) incumplida(s)» con código 3.
    /// La 2 sumaba dos veces: una por su `Debe` fallido y otra por la excepción que vino detrás.
    ///
    /// El recuento por aserción es el aprendizaje nº10 —«el denominador es el plan»— cometido en el
    /// numerador, y aquí duele más que en otros sitios: quien lee este número es la compuerta.
    /// </summary>
    private static int _fallos;

    /// <summary>Cuántas se juzgaron. El total lo dice quien sabe contarlo, no un grep del log.</summary>
    private static int _promesas;

    /// <summary>¿La promesa EN CURSO ya falló? Se sigue evaluando: queremos ver las tres aserciones
    /// rotas, no sólo la primera — pero cuentan como una promesa incumplida, que es lo que son.</summary>
    private static bool _rota;

    /// <summary>La fidelidad no es una promesa del grafo: es del proyector. Cuenta aparte para que
    /// «el núcleo está roto» y «lo que se ve no es el núcleo» no se confundan en un solo número.</summary>
    private static int _fallosDeFidelidad;

    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // El juez juzgándose a sí mismo. Un arnés que sólo se ha visto en verde es indistinguible de
        // uno que devuelve verde siempre — el mismo argumento que ya hace `Sabotear` con Neo4j más
        // abajo, aplicado al contador que decide si algo entra a `main`.
        if (args.Contains("--autoprueba")) return Autoprueba();

        Console.WriteLine("CONTRATO DEL NÚCLEO (el grafo, aislado)\n");

        Prueba("1. lo que se ve queda alcanzable desde donde se vio", LoVistoQueda);
        Prueba("2. observar NO borra: lo de antes sigue, marcado como no vivo", ObservarNoBorra);
        Prueba("3. cruzar guarda a dónde llevó, por ubicación Y selector", CruzarGuardaDestino);
        Prueba("4. el mismo selector puede llevar a sitios distintos según desde dónde", MismoSelectorDosDestinos);
        Prueba("5. el orden de las observaciones no cambia el grafo", ElOrdenNoImporta);
        Prueba("6. lo vivo y lo recordado nunca se confunden", VivoNoEsRecordado);
        Prueba("7. no se inventa nada: sin cruzar, no hay destino", SinCruzarNoHayDestino);

        // LA FIDELIDAD DE LA PROYECCIÓN, que es donde estaban los fallos de verdad. Se comprueba
        // leyendo de vuelta desde Neo4j, no revisando el código: revisar el código demuestra lo que
        // el proyector PRETENDE hacer, y lo que hace falta saber es lo que hizo.
        //
        // Va aparte de las promesas y no cuenta como fallo si Neo4j no está: el núcleo es puro y
        // tiene que poder juzgarse sin levantar una base de datos. Pero cuando está, se mira —
        // porque un dibujo fiel a una base de datos equivocada sigue siendo un dibujo equivocado.
        Console.WriteLine();
        ComprobarFidelidad();

        return Resumir("NÚCLEO");
    }

    /// <summary>
    /// La línea que los scripts LEEN, en vez de grepear el log. `verificar.ps1` contaba los
    /// pendientes con `Select-String "PENDIENTE"`, que es case-insensitive por defecto y se comía
    /// la propia línea de resumen y cualquier «pendiente» en minúscula de otro texto. Un número lo
    /// dice quien sabe contarlo.
    /// </summary>
    private static int Resumir(string quien)
    {
        Console.WriteLine();
        Console.WriteLine($"CONTRATO: {_promesas} promesas, {_promesas - _fallos} verdes, "
                        + $"{_fallos} incumplidas, 0 pendientes");
        Console.WriteLine(_fallos == 0
            ? $"{quien} ÍNTEGRO: el grafo promete lo que dice prometer."
            : $"{quien} ROTO: {_fallos} promesa(s) incumplida(s).");

        // La fidelidad bloquea igual —un visor que miente sobre el núcleo es tan malo como un núcleo
        // roto— pero suma aparte del recuento de promesas, para que el veredicto diga cuál de las
        // dos cosas pasó.
        return _fallos + _fallosDeFidelidad;
    }

    // ── Las promesas ─────────────────────────────────────────────────────────

    private static void LoVistoQueda(Grafo g)
    {
        g.Observar("app://inicio", new[] { new Elemento("s:catalogo", "Catálogo", "Button") });
        var d = g.DesdeAqui("app://inicio");
        Debe(d.Count == 1 && d[0].Que.Etiqueta == "Catálogo", "lo observado se puede volver a preguntar");
        Debe(d[0].Vivo, "y está vivo, porque se acaba de ver");
        Debe(g.Aqui == "app://inicio", "el grafo sabe dónde está");
    }

    private static void ObservarNoBorra(Grafo g)
    {
        // Un menú abierto y luego cerrado: sus opciones dejan de estar en pantalla, y el núcleo
        // anterior las habría perdido. Olvidar en cuanto algo se oculta vacía el grafo solo.
        g.Observar("app://informes", new[]
        {
            new Elemento("s:exportar", "Exportar", "Button"),
            new Elemento("s:programar", "Programar envío", "MenuItem"),
        });
        g.Observar("app://informes", new[] { new Elemento("s:exportar", "Exportar", "Button") });

        var d = g.DesdeAqui("app://informes");
        Debe(d.Count == 2, "las dos siguen en el grafo: observar no borra");
        Debe(d.Single(x => x.Que.Selector == "s:exportar").Vivo, "la que está en pantalla, viva");
        Debe(!d.Single(x => x.Que.Selector == "s:programar").Vivo,
            "la que ya no está, RECORDADA pero no viva — memoria, no promesa");
    }

    private static void CruzarGuardaDestino(Grafo g)
    {
        g.Observar("app://inicio", new[] { new Elemento("s:catalogo", "Catálogo", "Button") });
        g.Cruzar("app://inicio", "s:catalogo", "app://catalogo");
        Debe(g.DesdeAqui("app://inicio")[0].Destino == "app://catalogo",
            "el destino cruzado queda guardado");
    }

    private static void MismoSelectorDosDestinos(Grafo g)
    {
        // El caso que rompía el núcleo anterior: «Atrás» lleva a un sitio distinto según de dónde
        // vengas. Guardarlo solo por selector obligaba a inventar el concepto de «relativa».
        var atras = new Elemento("s:atras", "Atrás", "Button");
        g.Observar("app://a", new[] { atras });
        g.Observar("app://b", new[] { atras });
        g.Cruzar("app://a", "s:atras", "app://inicio");
        g.Cruzar("app://b", "s:atras", "app://catalogo");

        Debe(g.DesdeAqui("app://a")[0].Destino == "app://inicio", "desde A lleva a inicio");
        Debe(g.DesdeAqui("app://b")[0].Destino == "app://catalogo",
            "…y desde B a catálogo, sin que uno pise al otro");
    }

    private static void ElOrdenNoImporta(Grafo g)
    {
        var uno = new Elemento("s:1", "Uno", "Button");
        var dos = new Elemento("s:2", "Dos", "Button");

        g.Observar("app://x", new[] { uno, dos });
        g.Cruzar("app://x", "s:1", "app://y");
        string primero = Retrato(g);

        var otro = new Grafo();
        otro.Observar("app://x", new[] { dos, uno });   // el mismo mundo, visto al revés
        otro.Cruzar("app://x", "s:1", "app://y");

        Debe(Retrato(otro) == primero, "el mismo mundo da el mismo grafo aunque se recorra en otro orden");
    }

    private static void VivoNoEsRecordado(Grafo g)
    {
        var e = new Elemento("s:menu", "Más opciones", "MenuItem");
        g.Observar("app://p", new[] { e });
        Debe(g.DesdeAqui("app://p")[0].Vivo, "recién visto: vivo");

        g.Observar("app://p", Array.Empty<Elemento>());
        Debe(!g.DesdeAqui("app://p")[0].Vivo, "ya no está en pantalla: deja de estar vivo…");
        Debe(g.DesdeAqui("app://p").Count == 1, "…pero no desaparece del grafo");
    }

    private static void SinCruzarNoHayDestino(Grafo g)
    {
        g.Observar("app://z", new[] { new Elemento("s:misterio", "Misterio", "Button") });
        Debe(g.DesdeAqui("app://z")[0].Destino.Length == 0,
            "una puerta sin cruzar no tiene destino, y decirlo vacío es la respuesta honesta");
        // Y no se acepta un destino inventado: cruzar a donde ya estás no es cruzar.
        g.Cruzar("app://z", "s:misterio", "app://z");
        Debe(g.DesdeAqui("app://z")[0].Destino.Length == 0, "cruzar a uno mismo no acuña nada");
    }

    /// <summary>
    /// ¿Lo que se ve en Neo4j es EXACTAMENTE lo que dice el núcleo? Se proyecta un grafo con las
    /// formas que más nos han mordido —dos apps a la vez, un elemento que ya no está en pantalla,
    /// un selector que lleva a sitios distintos— y se lee de vuelta para compararlo.
    /// </summary>
    private static void ComprobarFidelidad()
    {
        var g = new Grafo();
        var atras = new Elemento("s:atras", "Atrás", "Button");
        g.Observar("uia://una.exe/inicio", new[] { atras, new Elemento("s:ir", "Ir", "Button") });
        g.Cruzar("uia://una.exe/inicio", "s:ir", "uia://una.exe/dentro");
        g.Observar("uia://una.exe/dentro", new[] { atras });
        g.Cruzar("uia://una.exe/dentro", "s:atras", "uia://una.exe/inicio");
        g.Observar("uia://otra.exe/sola", new[] { new Elemento("s:x", "Equis", "Button") });
        // Y uno que se ve y luego desaparece: lo GRABADO no puede confundirse con lo VIVO.
        g.Observar("uia://una.exe/inicio", new[] { new Elemento("s:ir", "Ir", "Button") });

        using var p = new ProyectorNeo4j();
        p.Cuenta = m => Console.WriteLine($"   {m}");
        if (!p.Proyectar(g))
        {
            Console.WriteLine("⚪ fidelidad de la proyección: NO COMPROBADA (Neo4j no respondió)");
            return;
        }

        string veredicto = p.Verificar(g);
        if (veredicto.Length == 0)
            Console.WriteLine("✔ fidelidad de la proyección: lo que hay en Neo4j ES lo que dice el núcleo");
        else
        {
            _fallosDeFidelidad++;
            Console.WriteLine("✘ fidelidad de la proyección:");
            Console.WriteLine("   " + veredicto.Replace("\n", "\n   "));
            return;
        }

        // Y AHORA SE COMPRUEBA QUE SABE FALLAR. Un candado que nunca ha cerrado no se sabe si
        // cierra: una comprobación que solo se ha visto en verde es indistinguible de una que
        // devuelve verde siempre, y esa fue exactamente la forma del peor fallo que hemos tenido
        // —«sin_situar» contestando «no queda nada» sobre un grafo vacío—.
        //
        // Se estropea Neo4j A PROPÓSITO, por fuera del proyector, y se exige que lo note. Después
        // se deja como estaba.
        p.Sabotear("MATCH (e:Elemento {selector:'s:x'}) DETACH DELETE e");
        string trasElSabotaje = p.Verificar(g);
        if (trasElSabotaje.Length > 0)
            Console.WriteLine("✔ …y SABE FALLAR: al borrar un elemento por detrás, lo detectó");
        else
        {
            _fallosDeFidelidad++;
            Console.WriteLine("✘ la comprobación de fidelidad NO detectó un elemento borrado a mano: "
                            + "está dando verde sin mirar");
        }
        p.Proyectar(g);   // se deja el mundo como estaba
    }

    // ── El arnés ─────────────────────────────────────────────────────────────

    /// <summary>Una huella del grafo entero, estable, para comparar dos grafos.</summary>
    private static string Retrato(Grafo g) =>
        string.Join("\n", g.Ubicaciones().Select(u =>
            u + " => " + string.Join(", ", g.DesdeAqui(u)
                .OrderBy(a => a.Que.Selector, StringComparer.Ordinal)
                .Select(a => $"{a.Que.Selector}[{(a.Vivo ? "vivo" : "memoria")}]->{a.Destino}"))));

    private static void Prueba(string nombre, Action<Grafo> cuerpo)
    {
        _promesas++;
        _rota = false;
        try { cuerpo(new Grafo()); }
        catch (Exception e)
        {
            _rota = true;
            // La cadena ENTERA: un TypeInitializationException dice «el inicializador lanzó una
            // excepción» y se guarda para sí POR QUÉ, que es lo único que sirve.
            for (var x = e; x != null; x = x.InnerException)
                Console.WriteLine($"   ✘ {x.GetType().Name}: {x.Message}");
        }
        if (_rota) _fallos++;
        Console.WriteLine($"{(_rota ? "✘" : "✔")} {nombre}");
    }

    /// <summary>
    /// Marca la promesa en curso como rota y SIGUE. No suma al total: eso lo hace <see cref="Prueba"/>
    /// una sola vez, porque tres aserciones rotas siguen siendo una promesa incumplida.
    /// </summary>
    private static void Debe(bool condicion, string promesa)
    {
        if (condicion) return;
        _rota = true;
        Console.WriteLine($"   ✘ {promesa}");
    }

    // ── El juez juzgándose ───────────────────────────────────────────────────

    /// <summary>
    /// Tres promesas de mentira con un resultado conocido: una verde, una con TRES aserciones
    /// falsas, y una que revienta. El arnés tiene que salir con **2** —dos promesas incumplidas—
    /// y no con 4, que es lo que sumaría contando aserciones.
    ///
    /// Sin esto, arreglar el contador sería exactamente el vicio que el contador tiene: dar por
    /// bueno un número porque lo escribió quien lo iba a leer.
    /// </summary>
    private static int Autoprueba()
    {
        Console.WriteLine("AUTOPRUEBA DEL ARNÉS (tres promesas de mentira, resultado conocido)\n");

        Prueba("A. verde", _ => Debe(true, "esto se cumple"));
        Prueba("B. una promesa con TRES aserciones rotas", _ =>
        {
            Debe(false, "la primera");
            Debe(false, "la segunda");
            Debe(false, "la tercera");
        });
        Prueba("C. una promesa que revienta", _ => throw new InvalidOperationException("a propósito"));

        Console.WriteLine();
        bool bien = _promesas == 3 && _fallos == 2;
        Console.WriteLine(bien
            ? "✔ EL ARNÉS SABE CONTAR: 3 promesas, 2 incumplidas — no 4 aserciones."
            : $"✘ EL ARNÉS NO SABE CONTAR: dijo {_promesas} promesas y {_fallos} incumplidas; "
              + "esperaba 3 y 2. Todo veredicto que dé este contrato es sospechoso.");
        return bien ? 0 : 1;
    }
}
