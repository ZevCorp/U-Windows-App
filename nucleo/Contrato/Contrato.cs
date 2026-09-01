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
        Prueba("12. saber dónde estoy no dice nada de lo que se ve", EstarNoEsVer);
        Prueba("13. un grafo grande no encarece contestar «qué alcanzo desde aquí»", ElTamanoNoPesa);
        Prueba("14. si la puerta corta no se ve, se prueba la ruta larga que sí", OtraPuertaQueSiSeVe);
        Prueba("15. «no sé llegar» y «sé pero no se ve» son respuestas distintas", DosNoDistintos);
        Prueba("16. lo enseñado vive EN el grafo, colgado del elemento", LoEnsenadoVaConElElemento);
        Prueba("17. no se puede enseñar sobre algo que nunca se vio aquí", NoSeEnsenaSobreLoQueNoExiste);
        Prueba("18. los recuerdos de una pantalla salen SIEMPRE en el mismo orden", ElOrdenDeLosRecuerdosNoBaila);

        // HEREDERAS DEL CONTRATO VIEJO (gran limpieza, 2026-08-30). Las promesas 2 y 8 del núcleo
        // retirado —«la enseñanza sobrevive a borrar el grafo» y «guardar y cargar no pierde
        // nada»— no podían morir sin descendencia: son las dos cosas que el usuario paga con su
        // tiempo cuando fallan. Aquí se juzgan PURAS, sin Neo4j: la parte del núcleo es que lo
        // extraído se reaplique entero por sus propias puertas (Recordar→Cruzar→Ensenar, el mismo
        // orden que usa el restaurador de verdad); que la base lo conserve lo juzga aparte
        // ComprobarFidelidad cuando la base está.
        Prueba("19. apagar y volver no pierde nada: lo extraído se reaplica y da el mismo grafo", ApagarYVolverNoPierde);
        Prueba("20. la enseñanza sobrevive al olvido del terreno y se reengancha sola", LaEnsenanzaSobreviveAlOlvido);
        // El gesto (spec 003). Una arista que sabe A DÓNDE lleva pero no CÓMO se cruza obliga a
        // volver a averiguarlo cada vez — y averiguarlo son clics de más sobre la pantalla real.
        // Medido sobre la arquitectura anterior el 2026-08-26: tres clics físicos por acción, cada
        // vez, para siempre; y el doble cayendo ENCIMA de un clic que ya había funcionado cuando la
        // app tardaba más que la ventana de espera.
        Prueba("21. el gesto que abrió una puerta viaja con su arista", ElGestoViajaConLaArista);

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

        // OBSERVAR NO ES ESTAR. Esta promesa exigía que observar fijara además «dónde estoy», y esa
        // exigencia era la que rompía el sistema en marcha: leer la pantalla tarda ~400 ms, así que
        // el observador fijaba la ubicación con un valor viejo y REBOBINABA el sitio actual al
        // anterior. Con la ubicación mirándose cada 120 ms, el lento pisaba al rápido sin parar y
        // el grafo se quedaba clavado — «vaya donde vaya, se queda en claude.exe» (2026-08-12).
        //
        // Se cambia AQUÍ y no solo en el código, porque el contrato es donde vive el acuerdo: esta
        // promesa daba por buena una mezcla de dos hechos que ahora sabemos que hay que separar.
        Debe(g.Aqui.Length == 0, "…pero observar NO afirma que estemos ahí: eso lo dice «Estoy»");
        g.Estoy("app://inicio");
        Debe(g.Aqui == "app://inicio", "y dicho eso, el grafo sabe dónde está");
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

    /// <remarks>
    /// LA VERSIÓN ANTERIOR SE RENDÍA. Tomaba el camino más corto y, si su primera puerta no estaba
    /// en pantalla, devolvía nulo —aunque hubiera otra ruta más larga cuya puerta SÍ estuviera
    /// delante—. Eso no es prudencia, es dejar de mirar: el mapa es vivo justamente para poder
    /// preferir lo que se ve.
    ///
    /// El montaje es el del mundo real: desde «inicio» se llega a «fondo» en un paso por el atajo y
    /// en dos por el pasillo. Se quita el atajo de la pantalla —sigue en memoria— y tiene que salir
    /// el pasillo.
    /// </remarks>
    private static void OtraPuertaQueSiSeVe(Grafo g)
    {
        var atajo = new Elemento("s:atajo", "Atajo", "Button");
        var pasillo = new Elemento("s:pasillo", "Pasillo", "Button");

        g.Observar("app://inicio", new[] { atajo, pasillo });
        g.Cruzar("app://inicio", "s:atajo", "app://fondo");
        g.Cruzar("app://inicio", "s:pasillo", "app://medio");
        g.Observar("app://medio", new[] { new Elemento("s:sigue", "Sigue", "Button") });
        g.Cruzar("app://medio", "s:sigue", "app://fondo");

        // Con las dos puertas a la vista gana la corta: menos clics.
        g.Observar("app://inicio", new[] { atajo, pasillo });
        Debe(g.SiguientePaso("app://inicio", "app://fondo")?.Que.Selector == "s:atajo",
            "con todo a la vista se va por el camino más corto");

        // Y ahora el atajo deja de verse. Sigue en el grafo, pero no se puede pulsar.
        g.Observar("app://inicio", new[] { pasillo });
        var paso = g.SiguientePaso("app://inicio", "app://fondo");
        Debe(paso != null, "no se rinde: hay otra puerta que sí está en pantalla");
        Debe(paso?.Que.Selector == "s:pasillo", "…y es el pasillo, la ruta larga pero visible");
        Debe(paso is null || paso.Vivo, "el paso que se devuelve SIEMPRE está vivo: es lo único pulsable");
    }

    // ── LO ENSEÑADO ──────────────────────────────────────────────────────────
    //
    // Se probó primero en un archivo aparte, con las mismas claves que el grafo. El usuario lo vio
    // en cuanto se lo enseñé: «¿es paralelo al grafo?». Sí lo era — y dos sitios que saben de lo
    // mismo se desincronizan sin avisar. Ya nos costó tener dos mapas (2026-08-23).
    //
    // Aquí dentro, además, «llévame a donde se radican las facturas» es UNA consulta: se busca por
    // significado y desde ese elemento ya se sabe el camino. En dos almacenes son dos consultas y
    // un pegado a mano.

    private static void LoEnsenadoVaConElElemento(Grafo g)
    {
        const string donde = "uia://x.exe/factura";
        g.Observar(donde, new[] { new Elemento("uia:aid=num", "Número", "Edit") });

        Debe(g.Ensenar(donde, "uia:aid=num", "aquí va el número de factura, nunca el nombre", "C:/fotos/a.png"),
            "se puede enseñar sobre un elemento que se ha visto aquí");

        var todo = g.RecuerdosDe(donde);
        Debe(todo.Count == 1 && todo[0].Que.Etiqueta == "Número",
            "y lo enseñado sale CON su elemento, no en una lista aparte: quien pregunta por esta "
            + "pantalla recibe las dos cosas juntas");
        Debe(todo[0].Eso.Significado.Contains("número de factura"), "con su significado");
        Debe(todo[0].Eso.Foto == "C:/fotos/a.png",
            "y con la RUTA de la foto, no la foto: un PNG de 190 KB dentro de un grafo no aporta "
            + "nada y lo engorda mucho");

        // Volver a explicarlo no borra la imagen de cuando se explicó la primera vez.
        g.Ensenar(donde, "uia:aid=num", "el número, y va sin guiones");
        Debe(g.RecuerdoSobre(donde, "uia:aid=num")!.Foto == "C:/fotos/a.png",
            "y volver a explicarlo conserva la foto: explicar mejor algo no es olvidar dónde era");
    }

    private static void ElOrdenDeLosRecuerdosNoBaila(Grafo g)
    {
        // SE CUENTAN DE UNO EN UNO —«recuerdo 1 de 2», y luego «el 2»— así que «el 2» tiene que ser
        // el mismo entre una llamada y la siguiente. Salían en el orden del diccionario, o sea en el
        // que se fueron viendo: la misma pantalla los numeraba distinto en cada arranque (visto el
        // 2026-08-24: «FortiClient VPN» era el 1 y tras reiniciar pasó a serlo «SAP Logon 64»), y
        // bastaba que entrara un elemento nuevo para reordenarlo todo a media narración.
        const string donde = "uia://x.exe/pantalla";
        g.Observar(donde, new[]
        {
            new Elemento("s:zeta", "Zeta", "Button"),
            new Elemento("s:alfa", "Alfa", "Button"),
        });
        g.Ensenar(donde, "s:zeta", "esto es lo último");
        g.Ensenar(donde, "s:alfa", "esto es lo primero");

        var antes = g.RecuerdosDe(donde).Select(r => r.Que.Selector).ToList();
        Debe(antes.SequenceEqual(new[] { "s:alfa", "s:zeta" }),
            "el orden lo decide el selector, no el azar de cuándo se vio cada cosa");

        // Y AHORA ENTRA UNO NUEVO, que es lo que pasa en cuanto se lee la pantalla otra vez.
        g.Observar(donde, new[]
        {
            new Elemento("s:zeta", "Zeta", "Button"),
            new Elemento("s:alfa", "Alfa", "Button"),
            new Elemento("s:beta", "Beta", "Button"),
        });
        var despues = g.RecuerdosDe(donde).Select(r => r.Que.Selector).ToList();
        Debe(despues.SequenceEqual(antes),
            "y ver algo nuevo NO reordena los recuerdos: si el 2 cambiara a media cuenta, se estaría "
            + "señalando uno mientras se habla de otro");
    }

    private static void NoSeEnsenaSobreLoQueNoExiste(Grafo g)
    {
        const string donde = "uia://x.exe/factura";
        g.Observar(donde, new[] { new Elemento("uia:aid=num", "Número", "Edit") });

        Debe(!g.Ensenar(donde, "uia:aid=jamas-visto", "esto es el total"),
            "el significado de algo que nadie ha visto aquí NO se guarda: sería una frase sin "
            + "sujeto, y después nadie sabría a qué se refería");
        Debe(!g.Ensenar("uia://x.exe/otra-pantalla", "uia:aid=num", "esto es el total"),
            "ni el de un elemento que existe en OTRA pantalla: lo enseñado es de un sitio concreto");
        Debe(g.RecuerdosDe(donde).Count == 0, "y no queda rastro de esos intentos");
    }

    /// <remarks>
    /// DOS «NO» QUE PIDEN COSAS OPUESTAS. «No sé llegar» pide seguir explorando; «sé llegar pero la
    /// puerta no está delante» pide esperar, desplegar el panel o volver atrás. Devolver nulo para
    /// las dos dejaba al que navega sin saber cuál le tocaba — el usuario hizo clic en un nodo y
    /// solo obtuvo «no sé llegar desde aquí, O el paso no está en pantalla» (2026-08-13).
    /// </remarks>
    private static void DosNoDistintos(Grafo g)
    {
        var puerta = new Elemento("s:puerta", "Puerta", "Button");
        g.Observar("app://a", new[] { puerta });
        g.Cruzar("app://a", "s:puerta", "app://b");

        var conLaPuertaDelante = g.ComoLlego("app://a", "app://b");
        Debe(conLaPuertaDelante.Paso != null, "con la puerta a la vista, hay paso");
        Debe(conLaPuertaDelante.ConocidoEnMemoria, "y por supuesto se conoce el camino");

        g.Observar("app://a", Array.Empty<Elemento>());   // la puerta deja de verse
        var sinVerla = g.ComoLlego("app://a", "app://b");
        Debe(sinVerla.Paso == null, "sin la puerta delante no hay paso que dar");
        Debe(sinVerla.ConocidoEnMemoria,
            "…pero SE SABE llegar: es «espera o vuelve atrás», no «hay que explorar»");

        var jamas = g.ComoLlego("app://a", "app://nunca-vista");
        Debe(jamas.Paso == null && !jamas.ConocidoEnMemoria,
            "y a donde no se ha ido nunca, ni paso ni camino: eso sí es «hay que explorar»");
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

        // LA VELOCIDAD TAMBIÉN SE PROMETE. Va antes que nada porque, a diferencia de las de abajo,
        // esta se puede juzgar con la app corriendo: solo pregunta si están los índices.
        //
        // Se comprueba la CAUSA y no el cronómetro a propósito. Un umbral de milisegundos en esta
        // máquina daría rojos por tener el portátil ocupado, y un juez que da rojos falsos enseña a
        // desconfiar del juez —ya nos pasó—. «¿Está el índice?» es determinista, es lo que de
        // verdad decide, y no depende de la carga: sin él, proyectar 1.740 elementos costaba entre
        // 0,2 y 21 segundos según la caché; con él, 113-367 ms (2026-08-13, medido en la misma base
        // con las mismas filas, cinco pasadas cada uno).
        string sinIndices = p.IndicesQueFaltan();
        if (sinIndices.Length == 0)
        {
            Console.WriteLine("✔ velocidad: Neo4j tiene los índices que hacen barata la proyección");
            Extra("proyectar es barato: los ids están indexados", true);
        }
        else
        {
            _fallos++;
            Console.WriteLine("✘ velocidad: " + sinIndices);
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

        // CAMBIAR UN DESTINO NO DEJA EL ANTERIOR. El núcleo guarda UN destino por (ubicación,
        // selector) y lo sobrescribe; la proyección solo hacía MERGE y nunca quitaba el `LLEVA_A`
        // viejo, así que Neo4j se quedaba con los dos y dejaba de ser un espejo para volverse un
        // archivo histórico.
        //
        // No es hipotético: «datos-adjuntos + Datos adjuntos» apuntaba a `documentos` Y a
        // `escritorio` a la vez. El navegador siguió el tramo, pulsó la carpeta donde YA ESTABA y no
        // se movió nunca — el usuario lo vio como «le pedí ir a documentos y llegó a datos
        // adjuntos» (2026-08-13).
        //
        // Y llevaba días pudiendo pasar sin que nadie lo viera, porque esta comprobación se saltaba
        // en cuanto Neo4j tenía datos de otro grafo. Ya no: `Verificar` solo mira las ubicaciones
        // que ESTE grafo conoce, así que puede correr con la app en marcha. Una promesa que se salta
        // no es una promesa.
        g.Cruzar("uia://una.exe/inicio", "s:ir", "uia://una.exe/otro-sitio");
        p.Proyectar(g);
        string trasCambiarDestino = p.Verificar(g);
        if (trasCambiarDestino.Length == 0)
        {
            Console.WriteLine("✔ …y cambiar un destino REEMPLAZA el anterior, no lo acumula");
            Extra("cambiar un destino reemplaza el anterior en Neo4j", true);
        }
        else
        {
            _fallos++;
            Console.WriteLine("✘ al cambiar un destino, Neo4j se quedó con los dos:");
            Console.WriteLine("   " + trasCambiarDestino.Replace("\n", "\n   "));
        }

        // ESTA NECESITA NEO4J PARA ELLA SOLA: restaura TODO lo que haya, así que con la app
        // corriendo se trae su grafo y falla por la sola presencia del vecino. Un rojo que no
        // significa «el núcleo está roto» es peor que no comprobar (2026-08-12).
        // LO NUESTRO INCLUYE LOS DESTINOS, no solo lo observado. `Cruzar` apunta a dónde llevó algo
        // sin que eso convierta el destino en una ubicación observada, pero la proyección sí crea
        // ese nodo. Sin contarlo, el contrato se tomaba a sí mismo por un inquilino ajeno y se
        // saltaba la comprobación — un juez que se descalifica solo.
        var mias = g.Ubicaciones()
            .Concat(g.Ubicaciones().SelectMany(u => g.DesdeAqui(u).Select(a => a.Destino)))
            .Where(x => x.Length > 0);
        if (p.HayOtroInquilino(mias))
        {
            Console.WriteLine("⚪ ida y vuelta: NO COMPROBADA — hay otro grafo en Neo4j. "
                            + "Cierra la app y vuelve a correr esto para juzgarla.");
            return;
        }

        // IDA Y VUELTA: se apaga la app (un núcleo nuevo y vacío), se restaura desde Neo4j, y tiene
        // que salir el MISMO grafo. Es la prueba de que Neo4j es memoria y no solo espejo — sin
        // esto, reiniciar perdía el mapa entero y nadie lo notaba porque siempre limpiábamos a mano
        // antes de cada prueba (2026-08-12).
        // Y LO ENSEÑADO TIENE QUE VOLVER TAMBIÉN. Es lo que el usuario dijo con la boca: quiere
        // enseñarle qué es cada cosa y que lo recuerde. Un significado que se pierde al reiniciar no
        // es haber enseñado nada.
        g.Ensenar("uia://una.exe/inicio", "s:ir", "esto lleva al otro sitio", "C:/fotos/ir.png");
        p.Proyectar(g);

        var resucitado = new Grafo();
        int volvieron = p.Restaurar(resucitado);

        // Se compara la ESTRUCTURA —qué ubicaciones, qué elementos, a dónde llevan— y NO qué está
        // vivo. Lo vivo no se restaura ni debe restaurarse: al arrancar no hay nada en pantalla, y
        // pretender lo contrario mandaría al asistente a pulsar cosas que no están delante. Que
        // esta comparación excluya `vivo` no es aflojarla: es medir lo que la persistencia promete.
        string Esqueleto(Grafo x) => string.Join("\n", x.Ubicaciones().Select(u =>
            u + " => " + string.Join(",", x.DesdeAqui(u)
                .OrderBy(a => a.Que.Selector, StringComparer.Ordinal)
                .Select(a => $"{a.Que.Selector}->{a.Destino}"
                            + Coletilla(x.RecuerdoSobre(u, a.Que.Selector))))));

        // El significado y la ruta de la foto entran en la comparación; la fecha no, porque cambia
        // sola y haría fallar la promesa por algo que a nadie le importa.
        static string Coletilla(Recuerdo? e) => e == null ? "" : $"[{e.Significado}|{e.Foto}]";

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

        // UN RECUERDO NO SE BORRA POR NO TENERLO A MANO — y esto no es hipotético: dos recuerdos
        // reales del usuario murieron así el 2026-08-24. La proyección escribía el significado con
        // un SET incondicional, así que un núcleo que no lo tenía cargado —una restauración
        // incompleta, una pantalla aún sin leer— escribía la cadena VACÍA encima del que estaba
        // guardado. El nodo seguía en Neo4j, con el significado en blanco: no se veía como una
        // pérdida, se veía como si nunca se hubiera enseñado nada.
        //
        // Se proyecta un grafo que NO sabe nada de recuerdos sobre una base que sí los tiene: si lo
        // guardado sobrevive a eso, sobrevive al caso real.
        var amnesico = new Grafo();
        amnesico.Recordar("uia://una.exe/inicio", new[] { new Elemento("s:ir", "Ir", "Button") });
        p.Proyectar(amnesico);

        var traslaAmnesia = new Grafo();
        p.Restaurar(traslaAmnesia);
        Debe(traslaAmnesia.RecuerdoSobre("uia://una.exe/inicio", "s:ir")?.Significado == "esto lleva al otro sitio",
            "proyectar un grafo SIN el recuerdo cargado no borra el que ya estaba guardado: vacío "
            + "significa «no lo tengo», nunca «bórralo»");

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
        g.Estoy("app://a"); g.Observar("app://a", mismo);
        g.Estoy("app://b"); g.Observar("app://b", new[] { new Elemento("s:b", "Otro", "Button") });

        int antes = g.Version;
        g.Estoy("app://a");             // se vuelve a A…
        g.Observar("app://a", mismo);   // …y A no ha cambiado en nada

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

    private static void EstarNoEsVer(Grafo g)
    {
        // Son dos hechos con velocidades distintas: dónde estoy vale 32 ms, leer la pantalla 400.
        // Pasar por un sitio deprisa tiene que dejar constancia de que se pasó, sin inventarse que
        // se vio nada — si no, un sitio atravesado rápido no existiría y el camino se grabaría
        // como si fuera directo.
        g.Observar("app://a", new[] { new Elemento("s:1", "Uno", "Button") });
        g.Estoy("app://b");

        Debe(g.Aqui == "app://b", "el grafo sabe que estamos en B");
        Debe(g.Ubicaciones().Contains("app://b"), "…y B existe, aunque no se haya mirado qué hay");
        Debe(g.DesdeAqui("app://b").Count == 0, "pero NO se inventa ningún elemento allí");
        Debe(g.DesdeAqui("app://a").Count == 1 && g.DesdeAqui("app://a")[0].Vivo,
            "y lo que se sabía de A sigue intacto: pasar por otro sitio no borra lo visto");

        int antes = g.Version;
        g.Estoy("app://b");
        Debe(g.Version == antes, "repetir dónde estás no cambia nada: no es una novedad");
    }

    private static void ElTamanoNoPesa(Grafo g)
    {
        // LO ÚNICO DE LA VELOCIDAD QUE EL NÚCLEO SÍ CONTROLA. Leer la pantalla es del mapeador y
        // depende de la máquina; contestar «qué alcanzo desde aquí» es suyo, y tiene que costar lo
        // mismo con diez ubicaciones que con dos mil. Si algún día alguien recorre el grafo entero
        // para contestar por una sola pantalla, esta promesa lo caza.
        //
        // NO SE MIDE EN MILISEGUNDOS, y es deliberado: un umbral de tiempo falla en una máquina
        // cargada o un martes, y un juez que da rojos por motivos ajenos al código enseña a
        // desconfiar del juez. Se compara el coste consigo mismo — si crecer 200 veces multiplicara
        // el trabajo, se notaría de sobra aunque el reloj vaya flojo ese día.
        var uno = new[] { new Elemento("s:1", "Uno", "Button") };
        g.Estoy("app://sola"); g.Observar("app://sola", uno);

        var reloj = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 200; i++) g.DesdeAqui("app://sola");
        long chico = reloj.ElapsedTicks;

        for (int i = 0; i < 2000; i++)
        {
            g.Estoy($"app://relleno{i}");
            g.Observar($"app://relleno{i}", new[] { new Elemento($"s:{i}", $"E{i}", "Button") });
        }
        g.Estoy("app://sola");

        reloj.Restart();
        for (int i = 0; i < 200; i++) g.DesdeAqui("app://sola");
        long grande = reloj.ElapsedTicks;

        Debe(g.Ubicaciones().Count > 2000, "el grafo creció de verdad");
        Debe(grande < Math.Max(chico, 1) * 20,
            $"contestar sigue costando lo mismo con 2.000 ubicaciones que con una "
            + $"({chico} → {grande} ticks): la respuesta mira SU pantalla, no el grafo entero");
    }

    /// <summary>
    /// Lo que el grafo sabe, extraído por su API pública en la misma forma de filas que el
    /// proyector escribe y el restaurador lee: (dónde, elemento, destino, recuerdo).
    /// </summary>
    private static List<(string Donde, Elemento Que, string Destino, string Gesto, Recuerdo? Eso)> Filas(Grafo g) =>
        g.Ubicaciones().SelectMany(u => g.DesdeAqui(u)
            .Select(a => (u, a.Que, a.Destino, g.GestoDe(u, a.Que.Selector), g.RecuerdoSobre(u, a.Que.Selector))))
        .ToList();

    /// <summary>Reaplica filas a un grafo virgen POR LAS PUERTAS DEL NÚCLEO y en su orden:
    /// primero los elementos (Recordar), luego los caminos (Cruzar), luego lo enseñado (Ensenar).
    /// Es el mismo orden del restaurador real, porque Cruzar y Ensenar rechazan lo que aún no se
    /// conoce — al revés se perdería todo, en silencio.</summary>
    private static Grafo Renacido(List<(string Donde, Elemento Que, string Destino, string Gesto, Recuerdo? Eso)> filas)
    {
        var g = new Grafo();
        foreach (var grupo in filas.GroupBy(f => f.Donde))
            g.Recordar(grupo.Key, grupo.Select(f => f.Que).ToList());
        foreach (var f in filas.Where(f => f.Destino.Length > 0))
            // EL GESTO VUELVE CON SU CAMINO (promesa 21). Restaurar con el Cruzar de tres
            // argumentos parecia inocuo y no lo era: el gesto simplemente no volvia, y cada arista
            // re-pagaba el ensayo entero UNA VEZ POR SESION, para siempre — justo la metrica que la
            // spec 003 vino a bajar (lo vio el agente optimizador el 2026-08-31).
            g.Cruzar(f.Donde, f.Que.Selector, f.Destino, f.Gesto);
        foreach (var f in filas.Where(f => f.Eso != null))
            g.Ensenar(f.Donde, f.Que.Selector, f.Eso!.Significado, f.Eso.Foto);
        return g;
    }

    private static void ApagarYVolverNoPierde(Grafo g)
    {
        // Un mundo pequeño pero con TODO lo que el grafo sabe guardar: elementos en dos
        // ubicaciones, un camino cruzado, una puerta sin cruzar y un recuerdo con foto.
        g.Estoy("app://inicio");
        g.Observar("app://inicio", new[]
        {
            new Elemento("s:ir", "Ir", "Button"),
            new Elemento("s:misterio", "Misterio", "Button"),
        });
        g.Cruzar("app://inicio", "s:ir", "app://fondo");
        g.Estoy("app://fondo");
        g.Observar("app://fondo", new[]
        {
            new Elemento("s:volver", "Volver", "Button"),
            // Una carpeta cruzada CON DOBLE: el gesto es la mitad nueva del saber (promesa 21) y
            // tiene que sobrevivir al apagado igual que el destino — si no, cada arista re-paga el
            // ensayo una vez por sesión, para siempre.
            new Elemento("s:carpeta", "Docs", "ListItem"),
        });
        g.Cruzar("app://fondo", "s:carpeta", "app://docs", "doubleclick");
        g.Ensenar("app://inicio", "s:ir", "esto lleva a donde se radica", "C:/fotos/ir.png");

        var otraVida = Renacido(Filas(g));

        // La comparación incluye el recuerdo (significado y foto; la fecha no, cambia sola), el
        // GESTO del camino, y excluye lo vivo: lo vivo no se restaura ni debe — al arrancar no hay
        // nada en pantalla.
        static string Huella(Grafo x) => string.Join("\n", x.Ubicaciones().Select(u =>
            u + " => " + string.Join(",", x.DesdeAqui(u)
                .OrderBy(a => a.Que.Selector, StringComparer.Ordinal)
                .Select(a => $"{a.Que.Selector}|{a.Que.Etiqueta}|{a.Que.Tipo}->{a.Destino}~{x.GestoDe(u, a.Que.Selector)}"
                    + (x.RecuerdoSobre(u, a.Que.Selector) is { } e ? $"[{e.Significado}|{e.Foto}]" : "")))));

        Debe(Huella(otraVida) == Huella(g),
            "el grafo renacido es EL MISMO: ubicaciones, elementos, caminos, puertas sin cruzar y recuerdos");
        Debe(otraVida.Ubicaciones().All(u => otraVida.DesdeAqui(u).All(a => !a.Vivo)),
            "y nada renace vivo: la memoria vuelve como memoria");
        Debe(otraVida.Aqui.Length == 0, "ni renace el «aquí»: recordar dónde estuviste no es estar allí");
    }

    private static void ElGestoViajaConLaArista(Grafo g)
    {
        // POR REFLEXIÓN mientras no exista: este proyecto referencia al Grafo por proyecto, así que
        // llamar GestoDe directo romperia la COMPILACION del contrato entero y las 20 anteriores no
        // podrian ni juzgarse. Ausente => la promesa falla con su motivo, no en silencio.
        var gestoDe = typeof(Grafo).GetMethod("GestoDe", new[] { typeof(string), typeof(string) });
        var cruzar4 = typeof(Grafo).GetMethods()
            .FirstOrDefault(m => m.Name == "Cruzar" && m.GetParameters().Length == 4);
        Debe(gestoDe != null && cruzar4 != null,
            "todavía no existe «GestoDe»/«Cruzar con gesto» (fase 1 de la spec 003). La promesa "
            + "está escrita y en rojo, que es donde tiene que estar");
        if (gestoDe == null || cruzar4 == null) return;

        g.Observar("app://lista", new[] { new Elemento("s:carpeta", "specs", "ListItem") });

        Debe((string)gestoDe.Invoke(g, new object[] { "app://lista", "s:carpeta" })! == "",
            "sin cruzar, el gesto es vacío: no se inventa un cómo que nadie ejecutó");

        cruzar4.Invoke(g, new object[] { "app://lista", "s:carpeta", "app://carpeta", "doubleclick" });
        Debe((string)gestoDe.Invoke(g, new object[] { "app://lista", "s:carpeta" })! == "doubleclick",
            "cruzada con doble, la arista RECUERDA el doble: la próxima vez no hay que averiguarlo "
            + "a base de clics de más sobre la pantalla real");

        // El gesto es DE LA ARISTA, no del selector: el mismo selector cruzado desde otro sitio
        // con otro gesto guarda el suyo — misma razon por la que el destino ya se guarda por
        // ubicacion Y selector (promesa 4).
        g.Observar("app://menu", new[] { new Elemento("s:carpeta", "specs", "ListItem") });
        cruzar4.Invoke(g, new object[] { "app://menu", "s:carpeta", "app://otro", "" });
        Debe((string)gestoDe.Invoke(g, new object[] { "app://menu", "s:carpeta" })! == ""
             && (string)gestoDe.Invoke(g, new object[] { "app://lista", "s:carpeta" })! == "doubleclick",
            "cada arista lleva su gesto: aprender uno no reescribe el otro");

        // «NO SE EL GESTO» NO ES «FUE UN CLIC SIMPLE». Re-cruzar SIN decir gesto —el clic humano
        // atribuido, la restauracion— no borra lo sabido: quien no sabe, no borra. El primer
        // borrador de Cruzar delegaba con "" y cada visita manual degradaba la arista al ensayo
        // eterno (encontrado por el optimizador el 2026-08-31, el mismo dia que nacio).
        g.Cruzar("app://lista", "s:carpeta", "app://carpeta");
        Debe((string)gestoDe.Invoke(g, new object[] { "app://lista", "s:carpeta" })! == "doubleclick",
            "re-cruzar sin decir el gesto CONSERVA el aprendido: el clic humano no desaprende");
    }

    private static void LaEnsenanzaSobreviveAlOlvido(Grafo g)
    {
        // La promesa 2 del contrato viejo, en el mundo nuevo: se enseña algo, el terreno se borra
        // entero, la memoria lo trae de vuelta… y al VOLVER A VER el elemento, el recuerdo sigue
        // colgado de él — sin que nadie lo repita.
        g.Observar("app://inicio", new[] { new Elemento("s:num", "Número", "Edit") });
        g.Ensenar("app://inicio", "s:num", "aquí va el número de factura, nunca el nombre");
        var filas = Filas(g);

        g.Olvidar();
        Debe(g.Ubicaciones().Count == 0 && g.RecuerdoSobre("app://inicio", "s:num") == null,
            "borrar el grafo borra el terreno entero, recuerdos incluidos: no hay copias escondidas");

        var devuelta = Renacido(filas);
        Debe(devuelta.RecuerdoSobre("app://inicio", "s:num")?.Significado
                == "aquí va el número de factura, nunca el nombre",
            "…pero lo extraído lo trae de vuelta, colgado del mismo elemento");

        // El mundo se vuelve a ver desde cero, y la lección sigue puesta: enseñar una vez basta.
        devuelta.Observar("app://inicio", new[] { new Elemento("s:num", "Número", "Edit") });
        var alcanzable = devuelta.DesdeAqui("app://inicio").Single(a => a.Que.Selector == "s:num");
        Debe(alcanzable.Vivo, "el elemento vuelve a estar vivo al verse otra vez");
        Debe(devuelta.RecuerdoSobre("app://inicio", "s:num") != null,
            "y volver a verlo NO borra lo enseñado: observar actualiza el terreno, no la memoria");
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
