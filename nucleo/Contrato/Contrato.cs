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
    private static int _fallos;

    private static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("CONTRATO DEL NÚCLEO (el grafo, aislado)\n");

        Prueba("1. lo que se ve queda alcanzable desde donde se vio", LoVistoQueda);
        Prueba("2. observar NO borra: lo de antes sigue, marcado como no vivo", ObservarNoBorra);
        Prueba("3. cruzar guarda a dónde llevó, por ubicación Y selector", CruzarGuardaDestino);
        Prueba("4. el mismo selector puede llevar a sitios distintos según desde dónde", MismoSelectorDosDestinos);
        Prueba("5. el orden de las observaciones no cambia el grafo", ElOrdenNoImporta);
        Prueba("6. lo vivo y lo recordado nunca se confunden", VivoNoEsRecordado);
        Prueba("7. no se inventa nada: sin cruzar, no hay destino", SinCruzarNoHayDestino);
        Prueba("8. cambiar de sitio ES un cambio, aunque se vea lo mismo", MoverseEsCambio);
        Prueba("9. un destino de algo que nunca se vio aquí se RECHAZA, no se traga", NadaDeFantasmas);
        Prueba("10. navegar es UN paso cada vez, y el paso tiene que estar vivo", ElSiguientePaso);
        Prueba("11. lo que se recuerda vuelve como MEMORIA, nunca como vivo", RecordarNoEsVer);

        // LA FIDELIDAD DE LA PROYECCIÓN, que es donde estaban los fallos de verdad. Se comprueba
        // leyendo de vuelta desde Neo4j, no revisando el código: revisar el código demuestra lo que
        // el proyector PRETENDE hacer, y lo que hace falta saber es lo que hizo.
        //
        // Va aparte de las promesas y no cuenta como fallo si Neo4j no está: el núcleo es puro y
        // tiene que poder juzgarse sin levantar una base de datos. Pero cuando está, se mira —
        // porque un dibujo fiel a una base de datos equivocada sigue siendo un dibujo equivocado.
        Console.WriteLine();
        ComprobarFidelidad();

        Console.WriteLine();
        Console.WriteLine(_fallos == 0
            ? "NÚCLEO ÍNTEGRO: el grafo promete lo que dice prometer."
            : $"NÚCLEO ROTO: {_fallos} promesa(s) incumplida(s).");

        // EL VEREDICTO, POR ESCRITO, para que se pueda MIRAR sin correr nada. Es lo que el visor
        // enseña en su pestaña de reglas: no una descripción que alguien tecleó —esa envejecería
        // sin avisar— sino el resultado real de la última vez que las promesas se juzgaron, con su
        // hora. Ver «✔ 11 de 11, hace dos minutos» dice algo; ver una lista bonita, no.
        Apuntar();
        return _fallos;
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

        // ESTAS COMPROBACIONES NECESITAN NEO4J PARA ELLAS SOLAS. La de ida y vuelta restaura TODO
        // lo que haya, así que con la app corriendo se traía su grafo y fallaba por la sola
        // presencia del vecino. Un rojo que no significa «el núcleo está roto» es peor que no
        // comprobar: enseña a desconfiar del juez (2026-08-12).
        if (p.HayOtroInquilino(g.Ubicaciones()))
        {
            Console.WriteLine("⚪ fidelidad e ida y vuelta: NO COMPROBADAS — la app está usando Neo4j. "
                            + "Ciérrala y vuelve a correr esto para juzgarlas.");
            return;
        }

        string veredicto = p.Verificar(g);
        if (veredicto.Length == 0)
        {
            Console.WriteLine("✔ fidelidad de la proyección: lo que hay en Neo4j ES lo que dice el núcleo");
            Extra("la proyección en Neo4j es fiel al núcleo", true);
        }
        else
        {
            _fallos++;
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
        // MOVERSE Y NADA MÁS: el proyector manda entonces una consulta corta en vez del volcado
        // entero. Tiene que dejar Neo4j igual de fiel — si el atajo escribiera algo distinto del
        // camino largo, el visor mentiría solo al cambiar de ventana, que es justo cuando más se
        // mira. Se comprueba porque el atajo se añadió DESPUÉS de que el volcado completo tumbara
        // el servidor (2026-08-12).
        g.Observar("uia://otra.exe/sola", new[] { new Elemento("s:x", "Equis", "Button") });
        p.Proyectar(g);
        string trasMoverse = p.Verificar(g);
        if (trasMoverse.Length == 0)
        {
            Console.WriteLine("✔ …y el atajo de «solo me moví» deja Neo4j igual de fiel");
            Extra("moverse por el atajo deja Neo4j igual de fiel", true);
        }
        else
        {
            _fallos++;
            Console.WriteLine("✘ el atajo de «solo me moví» rompió la fidelidad:");
            Console.WriteLine("   " + trasMoverse.Replace("\n", "\n   "));
        }

        // IDA Y VUELTA: se apaga la app (un núcleo nuevo y vacío), se restaura desde Neo4j, y tiene
        // que salir el MISMO grafo. Es la prueba de que Neo4j es memoria y no solo espejo — sin
        // esto, reiniciar perdía el mapa entero y nadie lo notaba porque siempre limpiábamos a mano
        // antes de cada prueba (2026-08-12).
        var resucitado = new Grafo();
        int volvieron = p.Restaurar(resucitado);

        // Se compara la ESTRUCTURA —qué ubicaciones, qué elementos, a dónde llevan— y NO qué está
        // vivo. Lo vivo no se restaura ni debe restaurarse: al arrancar no hay nada en pantalla, y
        // pretender lo contrario mandaría al asistente a pulsar cosas que no están delante. Que
        // esta comparación excluya `vivo` no es aflojarla: es medir lo que la persistencia promete.
        string Esqueleto(Grafo x) => string.Join("\n", x.Ubicaciones().Select(u =>
            u + " => " + string.Join(",", x.DesdeAqui(u)
                .OrderBy(a => a.Que.Selector, StringComparer.Ordinal)
                .Select(a => $"{a.Que.Selector}->{a.Destino}"))));

        if (volvieron > 0 && Esqueleto(resucitado) == Esqueleto(g))
        {
            Console.WriteLine($"✔ …y SOBREVIVE AL REINICIO: {volvieron} ubicación(es), misma estructura");
            Extra("la memoria sobrevive al reinicio de la app", true);
        }
        else
        {
            _fallos++;
            Console.WriteLine("✘ la memoria no sobrevive al reinicio:");
            Console.WriteLine("   esperado: " + Esqueleto(g).Replace("\n", " | "));
            Console.WriteLine("   volvió:   " + Esqueleto(resucitado).Replace("\n", " | "));
        }
        Debe(resucitado.DesdeAqui(resucitado.Ubicaciones().First()).All(a => !a.Vivo),
            "y lo restaurado NO está vivo: al arrancar no hay nada en pantalla");

        p.Sabotear("MATCH (e:Elemento {selector:'s:x'}) DETACH DELETE e");
        string trasElSabotaje = p.Verificar(g);
        if (trasElSabotaje.Length > 0)
        {
            Console.WriteLine("✔ …y SABE FALLAR: al borrar un elemento por detrás, lo detectó");
            Extra("la comprobación de fidelidad sabe fallar", true);
        }
        else
        {
            _fallos++;
            Console.WriteLine("✘ la comprobación de fidelidad NO detectó un elemento borrado a mano: "
                            + "está dando verde sin mirar");
        }
        p.Proyectar(g);   // se deja el mundo como estaba
    }

    private static void MoverseEsCambio(Grafo g)
    {
        // EL CASO REAL, medido por el usuario el 2026-08-12: «lo que estoy enfocando ya lo detectó
        // la url, pero la visualización marca un app diferente».
        //
        // Se va de A a B y se vuelve a A sin que nada de A haya cambiado. Si «dónde estoy» no
        // cuenta como cambio, quien proyecta se salta la pasada —porque la versión no se movió— y
        // el dibujo se queda marcando B como actual para siempre. DÓNDE ESTOY ES UN HECHO DEL
        // GRAFO, tanto como qué se ve; que sea el más volátil de todos no lo hace menos hecho.
        var mismo = new[] { new Elemento("s:a", "Algo", "Button") };
        g.Observar("app://a", mismo);
        g.Observar("app://b", new[] { new Elemento("s:b", "Otro", "Button") });

        int antes = g.Version;
        g.Observar("app://a", mismo);   // se vuelve a A, y A no ha cambiado en nada

        Debe(g.Aqui == "app://a", "el grafo sabe que volvimos a A");
        Debe(g.Version != antes,
            "…y volver CUENTA como cambio: si no, quien pinta se salta la pasada y deja marcado el sitio anterior");
    }

    private static void NadaDeFantasmas(Grafo g)
    {
        // EL CASO REAL, medido el 2026-08-12: el vigilante de clics escribía
        // «uia:aid=navCatalogo;ct=Button» y el observador «uia:name=Catálogo;ct=Button». Dos
        // vocabularios de identidad para la misma cosa. `Cruzar` guardaba el destino bajo una clave
        // que ningún elemento observado tenía, así que el camino quedaba HUÉRFANO: invisible para
        // quien preguntara «qué alcanzo desde aquí», porque esa respuesta se arma con lo observado.
        // De diez caminos aprendidos llegaron tres, y los siete perdidos no dejaron rastro.
        g.Observar("app://a", new[] { new Elemento("uia:name=Ir;ct=Button", "Ir", "Button") });

        Debe(!g.Cruzar("app://a", "uia:aid=botonIr;ct=Button", "app://b"),
            "un selector que nunca se vio aquí se RECHAZA: decir que no es lo honesto");
        Debe(g.DesdeAqui("app://a").All(x => x.Destino.Length == 0),
            "…y no deja rastro fantasma en el grafo");
        Debe(g.Cruzar("app://a", "uia:name=Ir;ct=Button", "app://b"),
            "y el mismo elemento, nombrado como se observó, SÍ se acepta");
    }

    private static void ElSiguientePaso(Grafo g)
    {
        // Un pasillo de tres: inicio → medio → fondo, y desde inicio también un callejón.
        var aMedio = new Elemento("s:medio", "Ir al medio", "Button");
        var aCallejon = new Elemento("s:callejon", "Callejón", "Button");
        var aFondo = new Elemento("s:fondo", "Ir al fondo", "Button");

        g.Observar("app://inicio", new[] { aMedio, aCallejon });
        g.Cruzar("app://inicio", "s:medio", "app://medio");
        g.Cruzar("app://inicio", "s:callejon", "app://callejon");
        g.Observar("app://medio", new[] { aFondo });
        g.Cruzar("app://medio", "s:fondo", "app://fondo");
        g.Observar("app://inicio", new[] { aMedio, aCallejon });   // se vuelve: el pasillo sigue vivo

        var paso = g.SiguientePaso("app://inicio", "app://fondo");
        Debe(paso?.Que.Selector == "s:medio",
            "para llegar al fondo, el siguiente paso es el del MEDIO — no el destino final");
        Debe(paso!.Vivo, "y se devuelve porque está en pantalla ahora");

        Debe(g.SiguientePaso("app://inicio", "app://ninguna-parte") == null,
            "a donde no se sabe llegar se contesta que no se sabe, en vez de improvisar");
        Debe(g.SiguientePaso("app://inicio", "app://inicio") == null,
            "ya estar allí no es un paso");

        // Y AHORA EL CASO QUE IMPORTA: el camino sigue en el mapa, pero la puerta ya no está en
        // pantalla —un panel plegado, una lista con scroll—. Devolverla sería mandar a pulsar el
        // vacío, que es el fallo que este modelo entero vino a quitar.
        g.Observar("app://inicio", new[] { aCallejon });   // «Ir al medio» deja de verse
        Debe(g.SiguientePaso("app://inicio", "app://fondo") == null,
            "si el paso no está VIVO no se ofrece: el mapa recuerda, la pantalla manda");
    }

    private static void RecordarNoEsVer(Grafo g)
    {
        // Es como vuelve el mapa al arrancar la app. Si entrara como «observado», el grafo diría
        // que todo está en pantalla —cientos de elementos de pantallas que no están delante— y el
        // asistente creería que puede pulsar cualquier cosa desde cualquier sitio.
        g.Recordar("app://lejos", new[] { new Elemento("s:algo", "Algo", "Button") });

        var d = g.DesdeAqui("app://lejos");
        Debe(d.Count == 1, "lo recordado está en el grafo");
        Debe(!d[0].Vivo, "…pero NO vivo: no lo estamos viendo, lo recordamos");
        Debe(g.Aqui.Length == 0, "y recordar dónde estuviste no es estar allí");

        // Y las reglas siguen rigiendo sobre lo que viene de fuera: un camino de algo que no está
        // en esa ubicación se rechaza igual que se rechazaría en vivo.
        Debe(!g.Cruzar("app://lejos", "s:fantasma", "app://otra"),
            "restaurar no es una puerta trasera: lo que no cumple las reglas tampoco entra por aquí");
    }

    // ── El arnés ─────────────────────────────────────────────────────────────

    /// <summary>Una huella del grafo entero, estable, para comparar dos grafos.</summary>
    private static string Retrato(Grafo g) =>
        string.Join("\n", g.Ubicaciones().Select(u =>
            u + " => " + string.Join(", ", g.DesdeAqui(u)
                .OrderBy(a => a.Que.Selector, StringComparer.Ordinal)
                .Select(a => $"{a.Que.Selector}[{(a.Vivo ? "vivo" : "memoria")}]->{a.Destino}"))));

    private static readonly List<(string Nombre, bool Cumple)> _veredicto = new();

    private static void Prueba(string nombre, Action<Grafo> cuerpo)
    {
        int antes = _fallos;
        try { cuerpo(new Grafo()); }
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

    private static void Extra(string nombre, bool cumple)
    {
        _veredicto.Add((nombre, cumple));
        if (!cumple) _fallos++;
    }

    /// <summary>
    /// Deja el veredicto en disco, junto al visor, para poder mirarlo sin correr nada.
    /// </summary>
    private static void Apuntar()
    {
        try
        {
            // Se busca la carpeta del visor subiendo desde el binario: el contrato corre desde
            // bin/Release/... y el repo está unos niveles por encima. Si no se encuentra, se calla:
            // no poder dejar la nota no puede tumbar al juez.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "visor"))) dir = dir.Parent;
            if (dir == null) return;

            string ruta = Path.Combine(dir.FullName, "visor", "reglas.json");
            var filas = _veredicto.Select(v =>
                $"{{\"regla\":{Cita(v.Nombre)},\"cumple\":{(v.Cumple ? "true" : "false")}}}");
            File.WriteAllText(ruta,
                "{\"cuando\":" + Cita(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                + ",\"integro\":" + (_fallos == 0 ? "true" : "false")
                + ",\"reglas\":[" + string.Join(",", filas) + "]}",
                System.Text.Encoding.UTF8);
        }
        catch { }
    }

    private static string Cita(string s) =>
        "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static void Debe(bool condicion, string promesa)
    {
        if (condicion) return;
        _fallos++;
        Console.WriteLine($"   ✘ {promesa}");
    }
}
