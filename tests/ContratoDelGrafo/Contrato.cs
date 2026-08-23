using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using U.WindowsClient.Actions;
using U.WindowsClient.Navigation;

namespace ContratoDelGrafo;

/// <summary>
/// EL CONTRATO DEL GRAFO: lo que el núcleo de navegación promete, escrito como pruebas que llaman
/// al código real. Cada invariante de aquí costó una prueba manual del usuario y un diagnóstico;
/// este archivo existe para que ninguna se vuelva a pagar dos veces.
///
/// La regla del proyecto desde el 2026-08-08: el núcleo (SurfaceMap) está CONGELADO. Se puede
/// tocar —hay candado, no muralla— pero cualquier cambio tiene que salir de aquí en verde:
///
///     .\scripts\contrato-del-grafo.ps1
///
/// Si una prueba estorba para un cambio, la conversación es sobre el CONTRATO, no sobre la prueba:
/// cambiarla es cambiar lo que el grafo promete a todo lo que se construye encima.
///
/// Cada prueba corre sobre un mapa RECIÉN nacido en un directorio propio (U_DATA_DIR), porque el
/// contrato describe el comportamiento del núcleo, no el historial de nadie.
/// </summary>
internal static class Contrato
{
    /// <summary>El MinDwell del mapa es 1200 ms; se espera con margen para no medir la casualidad.</summary>
    private const int Dwell = 1450;

    private static int _fallos;

    /// <summary>De las incumplidas, cuántas lo están porque su código aún no se ha escrito. Se
    /// cuentan aparte para que el rojo del desarrollo no se confunda con una regresión.</summary>
    private static int _pendientes;

    private static string _raiz = "";

    [STAThread]
    private static int Main()
    {
        _raiz = Path.Combine(Path.GetTempPath(), "u-contrato", DateTime.Now.ToString("HHmmss"));
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        Prueba("1. un sitio se confirma tras quedarse; pasar de largo no crea nodo", DwellYPasoDeLargo);
        Prueba("2. la enseñanza sobrevive a borrar el grafo y se reaplica sola", EnsenanzaSobrevive);
        Prueba("3. un nivel fijado no se mueve por volver a ver la puerta desde dentro", NivelFijadoNoSeMueve);
        Prueba("4. el gesto de atrás no acuña aristas: se purga al cargar", AtrasEsEfimero);
        Prueba("5. dos puertas al mismo sitio son dos aristas", DosPuertasDosAristas);
        Prueba("6. un clic no lleva a otra app: el robo de foco no es transición", RoboDeFocoNoAprende);
        Prueba("7. el cromo es propiedad de cualquier nivel y se alcanza desde cualquier pantalla", CromoDesdeCualquierParte);
        Prueba("8. guardar y cargar no pierde nada: nodos, aristas, niveles, enseñanzas", Persistencia);
        Prueba("9. una ruta jamás incluye un tramo que no sabe recorrerse", RutaSinHuecos);
        Prueba("10. olvidar una app no toca a las demás", OlvidarPorApp);
        Prueba("11. clasificar es decidir una vez: la aparición nueva nace ya clasificada", ClaseSePropaga);

        // ── LA PLATA DERIVADA ────────────────────────────────────────────────
        //
        // Estas siete se escribieron ANTES que el código que las cumple (2026-08-10), y no por
        // metodología: lo que arreglan es un subsistema que se daba por bueno a sí mismo —la plata
        // declarada sube su métrica escribiendo, y el criterio de terminado del arquitecto se
        // satisface declarando—. Una prueba escrita después de ese código se habría escrito para
        // que pasara, que es el mismo vicio con otro nombre.
        //
        // Las que aún no se pueden cumplir se declaran PENDIENTES y cuentan como incumplidas: el
        // rojo es el entregable de la primera fase. Lo que no vale es que una promesa que todavía
        // no tiene código diga «no aplicable» y se sume al verde.
        Prueba("12. la misma entrada da la misma plata", PlataDetermista);
        Prueba("13. el orden del paseo no cambia la estructura derivada", PlataNoDependeDelPaseo);
        Prueba("14. el bronce no se entera de lo declarado", BronceIgnoraLaPlata);
        Prueba("15. lo que dijo una persona manda, y queda anotado como desacuerdo", LaPersonaManda);
        Prueba("16. lo que dijo un modelo no mueve la derivación", ElModeloNoManda);
        Prueba("17. sin cromo derivado no hay atajo: quitar la plata rompe una ruta", SinPlataNoHayAtajo);
        Prueba("18. una app sin raíz observada no sitúa nada, y lo dice", SinRaizNoSeSitua);
        Prueba("19. el archivo del bronce no contiene plata", ElDiscoNoMezcla);
        Prueba("20. una sección alcanzada solo por el mobiliario sigue teniendo hijos", ElCromoNoCortaLaRama);

        // ── EL FRENO ─────────────────────────────────────────────────────────
        // Lo que promete Actions.Freno: que el ordenador siga siendo de quien está delante.
        // Aquí se juzga la LÓGICA, que es lo determinista. Que el gancho de teclado esté puesto y
        // que Escape se vea de verdad no se puede probar sin un teclado, y meterlo aquí volvería
        // caprichoso a un juez que ahora es fiable — se comprueba a mano y se declara en el PR.
        Console.WriteLine();
        Prueba("21. sin nada en marcha, pedir el alto no deja el freno armado", FrenoOciosoNoSeArma);
        Prueba("22. empezar desarma lo pedido antes: un Escape viejo no aborta lo siguiente", FrenoSeArmaAlEmpezar);
        Prueba("23. el alto se avisa UNA vez por tarea, aunque se pida diez", FrenoAvisaUnaSolaVez);
        Prueba("24. dormir se corta en cuanto se pide el alto, no al agotar el plazo", FrenoCortaElSueno);
        Prueba("25. al terminar, Escape vuelve a ser una tecla cualquiera", FrenoSueltaAlTerminar);
        // Y estas tres son la diferencia entre pedir y garantizar: que ninguna acción llegue a la
        // máquina con el freno echado NO puede depender de que cada bucle se acuerde de mirarlo.
        Prueba("26. con el freno echado, NADA llega al teclado ni al ratón", FrenoCierraLaPuertaDeEntrada);
        Prueba("27. con el freno echado, la puerta de la pantalla se niega y dice por qué", FrenoCierraLaPuertaDeUia);
        Prueba("28. al soltarse, Ü avisa de que devuelve el control", FrenoDevuelveElControlHablando);

        // ── LAS CINCO CAPACIDADES, SOBRE EL NÚCLEO ───────────────────────────
        // Salen de contar 26 días de uso real, no de decidir qué es importante: señalar, situarse,
        // abrir, pulsar e ir. Cada una se muda al núcleo en su propia rama y con sus promesas.
        Console.WriteLine();
        Prueba("29. situarse separa lo que se alcanza AHORA de lo que solo se recuerda", SituarseSeparaVivoDeMemoria);
        Prueba("30. de un sitio sin mirar se dice que no se ha mirado, no que esté vacío", SituarseNoConfundeVacioConSinMirar);
        Prueba("31. sin saber dónde estamos se dice, no se inventa", SituarseNoAdivina);
        Prueba("32. señalar distingue «puedo pulsarlo» de «lo recuerdo» y de «no lo conozco»", SenalarDistingueLasTres);
        Prueba("33. sin nada con nombre bajo el cursor se pide mover, no se inventa", SenalarNoAdivina);
        Prueba("34. lo señalado es lo más pequeño que contiene el punto, esté arriba o abajo", SenalarEligeLoMasPequeno);
        Prueba("35. elegir no depende del orden en que lleguen los candidatos", ElegirNoDependeDelOrden);
        Prueba("36. si ya estás delante, abrir no relanza nada", AbrirNoRelanzaLoQueYaEsta);
        Prueba("37. abrir se comprueba por consecuencia: dice dónde quedamos", AbrirDiceDondeQuedamos);
        Prueba("38. si no se pudo, se dice QUÉ hay ahora, no solo que no se pudo", AbrirDiceDondeEstamosAlFallar);
        Prueba("39. una app instalada se encuentra por su nombre hablado, tildes aparte", AbrirEncuentraLaAppInstalada);
        Prueba("40. si de verdad hay empate, se devuelven TODAS: no se adivina", AbrirNoAdivinaElEmpate);
        Prueba("41. lo señalado CADUCA: un gesto viejo no decide lo que se pide ahora", LoSenaladoCaduca);
        Prueba("42. una app INSTALADA con ese nombre gana a una pestaña abierta", LaAppInstaladaGanaALaPestana);
        Prueba("43. «Copilot» se refiere a «Copilot anclado»: no hay que decirlo clavado", SenalarNoExigeElNombreClavado);
        Prueba("44. pulsar y que no se mueva nada NO se cuenta como llegada", PulsarSinMoverNoEsLlegar);
        Prueba("45. un clic que no se pudo dar no se cuenta como dado", PulsarQueNoSePudoNoCuenta);
        Prueba("46. lo que se cruza queda aprendido, y manda el terreno", PulsarAprendeADondeLlevoDeVerdad);

        Console.WriteLine();
        if (_pendientes > 0)
            Console.WriteLine($"({_pendientes} de ellas PENDIENTES: la capacidad todavía no existe. "
                + "Es el rojo esperado mientras se implementa, no una regresión.)");
        Console.WriteLine(_fallos == 0
            ? "CONTRATO INTACTO: el grafo se comporta como el día que se congeló."
            : $"CONTRATO ROTO: {_fallos} promesa(s) incumplida(s). El cambio no puede entrar así.");
        return _fallos;
    }

    // ── Las promesas ─────────────────────────────────────────────────────────

    private static void DwellYPasoDeLargo(SurfaceMap m)
    {
        // El caso medido el 2026-08-07: Descargas → (Escritorio, un instante) → carpeta de dentro.
        m.Observe("uia://fake.exe/descargas");
        Thread.Sleep(Dwell);
        m.Observe("uia://fake.exe/escritorio");     // confirma descargas; escritorio queda pendiente
        Thread.Sleep(180);                          // …pero no aguanta el mínimo
        m.Observe("uia://fake.exe/nueva-carpeta");  // pasó de largo por escritorio
        Thread.Sleep(Dwell);
        m.Observe("uia://fake.exe/otra");           // confirma nueva-carpeta

        Debe(m.Nodes.ContainsKey("uia://fake.exe/descargas"), "el sitio donde se estuvo es un nodo");
        Debe(!m.Nodes.ContainsKey("uia://fake.exe/escritorio"),
            "la pantalla que no aguantó el mínimo NO es un nodo");
        var arista = m.Edges().SingleOrDefault(e =>
            e.From == "uia://fake.exe/descargas" && e.To == "uia://fake.exe/nueva-carpeta");
        Debe(arista.Info != null, "el viaje colapsa al par confirmado: descargas → nueva-carpeta");
        Debe(arista.Info!.Selector.Length == 0,
            "la arista que cruzó una pantalla sin confirmar nace SIN acción: muda antes que mentirosa");
    }

    private static void EnsenanzaSobrevive(SurfaceMap m)
    {
        // Nace la puerta, se cruza, y una persona la fija al primer nivel.
        m.ObserveExits("uia://fake.exe/inicio",
            new[] { ("Escritorio", "TreeItem", "uia:name=Escritorio;ct=TreeItem", Array.Empty<string>(), "") });
        m.LearnTraversal("uia://fake.exe/inicio", "uia://fake.exe/escritorio",
            "uia:name=Escritorio;ct=TreeItem", Array.Empty<string>(), "Escritorio", "TreeItem");
        m.FijarNivel("fake.exe", "Escritorio", 1);

        var (nodos, aristas) = m.OlvidarTodo();
        Debe(nodos > 0 && m.Nodes.Count == 0 && !m.Edges().Any(), "borrar el grafo borra el terreno entero");
        Debe(m.EnsenanzasDe("fake.exe").Any(e => e.Etiqueta.Equals("escritorio", StringComparison.OrdinalIgnoreCase)),
            "…pero la jerarquía enseñada NO se pierde: es aprendizaje, no terreno");

        // El mundo se vuelve a ver desde cero, y la lección vuelve sola, sin repetírsela.
        m.ObserveExits("uia://fake.exe/inicio",
            new[] { ("Escritorio", "TreeItem", "uia:name=Escritorio;ct=TreeItem", Array.Empty<string>(), "") });
        var puerta = m.Edges().Single(e => e.Info.Label == "Escritorio");
        Debe(puerta.Info.NivelNav == 1 && puerta.Info.NivelFijado,
            "la puerta renace ya en su nivel enseñado");
        Debe(puerta.Info.PorPersona, "…y con el sello de que lo dijo una persona");
    }

    private static void NivelFijadoNoSeMueve(SurfaceMap m)
    {
        m.ObserveExits("uia://fake.exe/inicio",
            new[] { ("Escritorio", "TreeItem", "uia:name=Escritorio;ct=TreeItem", Array.Empty<string>(), "") });
        m.LearnTraversal("uia://fake.exe/inicio", "uia://fake.exe/escritorio",
            "uia:name=Escritorio;ct=TreeItem", Array.Empty<string>(), "Escritorio", "TreeItem");
        m.FijarNivel("fake.exe", "Escritorio", 1);

        // Tres carpetas más abajo, el panel lateral vuelve a enseñar la misma puerta. Verla desde
        // dentro no la mueve: la estructura no depende del paseo (2026-08-06, la lección más cara).
        m.ObserveExits("uia://fake.exe/muy/adentro",
            new[] { ("Escritorio", "TreeItem", "uia:name=Escritorio;ct=TreeItem", Array.Empty<string>(), "") });
        m.LearnTraversal("uia://fake.exe/muy/adentro", "uia://fake.exe/escritorio",
            "uia:name=Escritorio;ct=TreeItem", Array.Empty<string>(), "Escritorio", "TreeItem");

        Debe(m.Edges().Where(e => e.Info.Label == "Escritorio")
                .All(e => e.Info.NivelNav == 1 && e.Info.NivelFijado),
            "TODAS las apariciones de la puerta conservan el nivel fijado");
        Debe(m.Nodes["uia://fake.exe/escritorio"].Nivel == 1,
            "y la pantalla de detrás vive en el nivel de su puerta, venga por donde venga el viaje");
    }

    private static void AtrasEsEfimero(SurfaceMap m)
    {
        m.AprenderAtras("fake.exe", "Atrás", humano: true);
        Debe(m.EsGestoDeAtras("fake.exe", "Atrás", "uia:aid=backButton;ct=Button"),
            "el mapa reconoce el gesto de volver");

        m.ObserveExits("uia://fake.exe/inicio", new[]
        {
            ("Atrás", "Button", "uia:aid=backButton;ct=Button", Array.Empty<string>(), ""),
            ("Escritorio", "TreeItem", "uia:name=Escritorio;ct=TreeItem", Array.Empty<string>(), ""),
        });
        m.Save();

        var otraVez = SurfaceMap.Load();
        Debe(!otraVez.Edges().Any(e => e.Info.Label.Equals("Atrás", StringComparison.OrdinalIgnoreCase)),
            "el atrás no deja arista que sobreviva una carga: su rastro es efímero por naturaleza");
        Debe(otraVez.Edges().Any(e => e.Info.Label == "Escritorio"),
            "…y la purga se lleva SOLO el atrás, no a sus vecinas");
    }

    private static void DosPuertasDosAristas(SurfaceMap m)
    {
        // Al panel de Imágenes se llega desde el árbol y desde los accesos anclados. Son dos
        // caminos, y el usuario pidió explícitamente que no se fundieran (2026-08-05).
        m.LearnTraversal("uia://fake.exe/inicio", "uia://fake.exe/imagenes",
            "uia:name=Imágenes;ct=TreeItem", Array.Empty<string>(), "Imágenes", "TreeItem");
        m.LearnTraversal("uia://fake.exe/inicio", "uia://fake.exe/imagenes",
            "uia:name=Imágenes;ct=ListItem", Array.Empty<string>(), "Imágenes", "ListItem");

        Debe(m.Edges().Count(e => e.To == "uia://fake.exe/imagenes") == 2,
            "el mismo destino por dos puertas distintas son DOS aristas, no una que pisa a la otra");
    }

    private static void RoboDeFocoNoAprende(SurfaceMap m)
    {
        m.LearnTraversal("uia://fake.exe/inicio", "uia://otra.exe/ventana",
            "uia:name=Loquesea;ct=Button", Array.Empty<string>(), "Loquesea", "Button");
        Debe(!m.Edges().Any(),
            "un clic dentro de una app no lleva a otra: eso es un robo de foco, no una transición");
    }

    private static void CromoDesdeCualquierParte(SurfaceMap m)
    {
        // El cromo es una PROPIEDAD, no un sinónimo de nivel 1: una web puede tener barra fija
        // dentro de cada sección (cromo de nivel 2). Lo desmintió una página real (2026-08-07).
        m.LearnTraversal("uia://fake.exe/inicio", "uia://fake.exe/ajustes",
            "uia:name=Ajustes;ct=Button", Array.Empty<string>(), "Ajustes", "Button");
        m.FijarNivel("fake.exe", "Ajustes", 2, cromo: true);

        var puerta = m.Edges().Single(e => e.Info.Label == "Ajustes");
        Debe(puerta.Info.EsCromo && puerta.Info.NivelNav == 2,
            "cromo de nivel 2: las dos cosas a la vez, sin que una implique la otra");
        Debe(m.CromoDe("fake.exe").Any(h => h.Info.Label == "Ajustes"),
            "el mobiliario declarado se enumera como cromo de la app");

        // Y al cromo se llega desde CUALQUIER pantalla, sin arista escrita: es un tramo virtual.
        m.LearnTraversal("uia://fake.exe/inicio", "uia://fake.exe/lejos",
            "uia:name=Lejos;ct=Button", Array.Empty<string>(), "Lejos", "Button");
        var ruta = m.Route("uia://fake.exe/lejos", "uia://fake.exe/ajustes");
        Debe(ruta != null && ruta.Count == 1,
            "desde una pantalla cualquiera, el cromo está a UN salto — eso es ser mobiliario");
    }

    private static void Persistencia(SurfaceMap m)
    {
        m.ObserveExits("uia://fake.exe/inicio",
            new[] { ("Escritorio", "TreeItem", "uia:name=Escritorio;ct=TreeItem", Array.Empty<string>(), "") });
        m.LearnTraversal("uia://fake.exe/inicio", "uia://fake.exe/escritorio",
            "uia:name=Escritorio;ct=TreeItem", Array.Empty<string>(), "Escritorio", "TreeItem");
        m.FijarNivel("fake.exe", "Escritorio", 1);
        m.Save();

        var otraVez = SurfaceMap.Load();
        Debe(otraVez.Nodes.ContainsKey("uia://fake.exe/escritorio"), "los nodos vuelven");
        var puerta = otraVez.Edges().Single(e => e.Info.Label == "Escritorio"
            && !SurfaceMap.EsPuerta(e.To));
        Debe(puerta.Info.NivelNav == 1 && puerta.Info.NivelFijado && puerta.Info.PorPersona,
            "el nivel, el fijado y el sello humano sobreviven al disco");
        Debe(otraVez.EnsenanzasDe("fake.exe").Any(), "las enseñanzas también");
        Debe(otraVez.Nodes["uia://fake.exe/escritorio"].Nivel == 1, "y el nivel del nodo");
    }

    private static void RutaSinHuecos(SurfaceMap m)
    {
        // Una puerta vista pero nunca cruzada existe en el mapa —eso es valioso— pero una RUTA no
        // puede apoyarse en ella: empezar un camino que no se sabe terminar deja al asistente a
        // mitad de la máquina de alguien.
        m.ObserveExits("uia://fake.exe/inicio",
            new[] { ("Misterio", "Button", "uia:name=Misterio;ct=Button", Array.Empty<string>(), "") });
        Debe(m.ExitsFrom("uia://fake.exe/inicio").Any(h => SurfaceMap.EsPuerta(h.To)),
            "la puerta sin cruzar se conoce y se puede enumerar");
        Debe(m.Route("uia://fake.exe/inicio", "uia://fake.exe/misterio") == null,
            "…pero no sostiene una ruta: «no sé llegar» antes que un camino a medias");
    }

    private static void OlvidarPorApp(SurfaceMap m)
    {
        // POR NOMBRE, no directo: OlvidarApp nació en la v1 y este MISMO contrato juzga también a
        // la v0, que no lo tiene. Llamarlo directo rompía la COMPILACIÓN del contrato contra los
        // núcleos viejos (medido 2026-08-08 reconstruyendo v0) — y un núcleo viejo no promete
        // capacidades que no conoce: sin el método, la promesa es «no aplicable», no «rota».
        var olvidar = typeof(SurfaceMap).GetMethod("OlvidarApp");
        if (olvidar == null)
        {
            Console.WriteLine("   (este núcleo no tiene OlvidarApp: promesa no aplicable, no rota)");
            return;
        }

        // Dos apps con terreno y una enseñanza cada una.
        m.LearnTraversal("uia://fake.exe/inicio", "uia://fake.exe/escritorio",
            "uia:name=Escritorio;ct=TreeItem", Array.Empty<string>(), "Escritorio", "TreeItem");
        m.FijarNivel("fake.exe", "Escritorio", 1);
        m.LearnTraversal("uia://otra.exe/inicio", "uia://otra.exe/ajustes",
            "uia:name=Ajustes;ct=Button", Array.Empty<string>(), "Ajustes", "Button");

        olvidar.Invoke(m, new object[] { "fake.exe" });
        Debe(!m.Nodes.Keys.Any(k => k.Contains("fake.exe")),
            "el terreno de la app olvidada desaparece entero");
        Debe(m.Nodes.ContainsKey("uia://otra.exe/ajustes") && m.Edges().Any(e => e.Info.Label == "Ajustes"),
            "…y el de las DEMÁS apps queda intacto: el borrado es un bisturí, no una escoba");
        Debe(m.EnsenanzasDe("fake.exe").Any(),
            "la enseñanza de la app olvidada sobrevive: es aprendizaje, no terreno");
    }

    private static void ClaseSePropaga(SurfaceMap m)
    {
        // El caso medido el 2026-08-10: el arquitecto marcó «Nuevo» como acción ocho veces —una por
        // pantalla— y al entrar en OneDrive le reaparecieron 27 controles ya clasificados. La
        // clasificación se escribía en las apariciones de ese instante, así que vaciar la lista de
        // pendientes costaba O(controles × pantallas); en un explorador, el número de pantallas es
        // el número de carpetas del disco, y el criterio de terminado era inalcanzable.
        // POR NOMBRE, como la promesa 10 y por la misma razón: este contrato juzga también a la v0,
        // que no conoce ClasificarSalida. Un núcleo viejo no promete lo que no sabe hacer.
        var clasificar = typeof(SurfaceMap).GetMethod("ClasificarSalida");
        if (clasificar == null)
        {
            Console.WriteLine("   (este núcleo no tiene ClasificarSalida: promesa no aplicable, no rota)");
            return;
        }

        m.ObserveExits("uia://fake.exe/inicio",
            new[] { ("Nuevo", "Button", "uia:name=Nuevo;ct=Button", Array.Empty<string>(), "herramientas") });
        var r = (string)clasificar.Invoke(m, new object[] { "fake.exe", "Nuevo", "accion" })!;
        Debe(!r.StartsWith("no encuentro"), "clasificar encuentra la salida que está delante");
        Debe(m.Edges().Single(e => e.Info.Label == "Nuevo").Info.KindDeclarado == "accion",
            "la aparición de esta pantalla queda clasificada");

        // Tres carpetas más adentro aparece el MISMO botón. Es el mismo control, y ya se decidió.
        m.ObserveExits("uia://fake.exe/muy/adentro",
            new[] { ("Nuevo", "Button", "uia:name=Nuevo;ct=Button", Array.Empty<string>(), "herramientas") });
        Debe(m.Edges().Where(e => e.Info.Label == "Nuevo").All(e => e.Info.KindDeclarado == "accion"),
            "…y la que nace en otra pantalla nace YA clasificada: no se vuelve a preguntar");

        // Y sobrevive al disco, como el nivel: una decisión no se pierde al cerrar la app.
        m.Save();
        var otraVez = SurfaceMap.Load();
        otraVez.ObserveExits("uia://fake.exe/otra/mas",
            new[] { ("Nuevo", "Button", "uia:name=Nuevo;ct=Button", Array.Empty<string>(), "herramientas") });
        Debe(otraVez.Edges().Where(e => e.Info.Label == "Nuevo").All(e => e.Info.KindDeclarado == "accion"),
            "la clasificación es aprendizaje: sobrevive a cargar el mapa de nuevo");

        // Nivel y clase se dicen por separado y no se pisan: son dos cosas sobre la misma salida.
        otraVez.FijarNivel("fake.exe", "Nuevo", 1);
        var puerta = otraVez.Edges().First(e => e.Info.Label == "Nuevo");
        Debe(puerta.Info.NivelNav == 1 && puerta.Info.KindDeclarado == "accion",
            "poner nivel no borra la clasificación, ni al revés");
    }

    // ── La plata derivada ────────────────────────────────────────────────────

    /// <summary>
    /// EL BRONCE DE PRUEBA: una app pequeña con todo lo que la derivación tiene que distinguir.
    ///
    /// Es sintético y se dice: no es una captura de `explorer.exe` disfrazada. Un fixture que
    /// pretendiera ser real y no lo fuera sería exactamente el vicio que este trabajo persigue —un
    /// dato que parece observado y está fabricado—. Lo que sí es real es la FORMA: panel lateral
    /// que sigue al usuario, una carpeta dentro de otra, un «Subir» que lleva a un sitio distinto
    /// según desde dónde se pulse, y una lista de archivos que no es estructura.
    ///
    /// El bronce capturado de una máquina de verdad entra después, en `bronce/`, y se juzga con
    /// estas mismas promesas: lo sintético prueba las reglas, lo capturado prueba los umbrales.
    /// </summary>
    /// <param name="alReves">
    /// El MISMO terreno recorrido en otro orden. La primera pantalla no cambia —es la raíz, y
    /// cambiarla cambiaría la app, no el paseo— pero todo lo demás se visita al contrario.
    /// </param>
    private static void MontarBronce(SurfaceMap m, bool alReves = false)
    {
        const string ini = "uia://fake.exe/inicio";
        const string docs = "uia://fake.exe/docs";
        const string anio = "uia://fake.exe/docs/2026";
        const string fotos = "uia://fake.exe/fotos";

        // El mobiliario: las dos puertas del panel lateral, presentes en TODAS las pantallas.
        (string, string, string, string[], string)[] Panel() => new[]
        {
            ("Documentos", "TreeItem", "uia:name=Documentos;ct=TreeItem", Array.Empty<string>(), ""),
            ("Fotos", "TreeItem", "uia:name=Fotos;ct=TreeItem", Array.Empty<string>(), ""),
        };

        // La raíz se observa siempre primero: es lo que la convierte en raíz.
        m.ObserveExits(ini, Panel().Concat(new[]
        {
            ("Ajustes", "Button", "uia:name=Ajustes;ct=Button", Array.Empty<string>(), ""),
        }).ToArray());

        void Documentos()
        {
            m.LearnTraversal(ini, docs, "uia:name=Documentos;ct=TreeItem", Array.Empty<string>(),
                "Documentos", "TreeItem");
            m.ObserveExits(docs, Panel().Concat(new[]
            {
                ("2026", "TreeItem", "uia:name=2026;ct=TreeItem", Array.Empty<string>(), ""),
                ("Subir", "Button", "uia:aid=upButton;ct=Button", Array.Empty<string>(), ""),
            }).ToArray());

            m.LearnTraversal(docs, anio, "uia:name=2026;ct=TreeItem", Array.Empty<string>(),
                "2026", "TreeItem");

            // La carpeta con contenido: diez hermanos iguales y un campo con el que estrecharlos.
            var dentro = Panel().Concat(new[]
            {
                ("Subir", "Button", "uia:aid=upButton;ct=Button", Array.Empty<string>(), ""),
                ("Buscar", "Edit", "uia:aid=searchBox;ct=Edit", Array.Empty<string>(), ""),
            }).ToList();
            for (int i = 1; i <= 10; i++)
                dentro.Add(($"factura-{i:00}.pdf", "ListItem",
                    $"uia:name=factura-{i:00}.pdf;ct=ListItem", Array.Empty<string>(), ""));
            m.ObserveExits(anio, dentro);

            // «Subir» desde aquí lleva a docs…
            m.LearnTraversal(anio, docs, "uia:aid=upButton;ct=Button", Array.Empty<string>(),
                "Subir", "Button");
            // …y desde docs lleva a inicio. Mismo selector, dos destinos: eso es ser relativo, y es
            // lo que hay que poder derivar sin una lista de nombres de botón.
            m.LearnTraversal(docs, ini, "uia:aid=upButton;ct=Button", Array.Empty<string>(),
                "Subir", "Button");
        }

        void Fotos()
        {
            m.LearnTraversal(ini, fotos, "uia:name=Fotos;ct=TreeItem", Array.Empty<string>(),
                "Fotos", "TreeItem");
            m.ObserveExits(fotos, Panel());
        }

        if (alReves) { Fotos(); Documentos(); }
        else { Documentos(); Fotos(); }
    }

    private static void PlataDetermista(SurfaceMap m)
    {
        MontarBronce(m);
        string? una = HuellaDePlata(m, "fake.exe");
        string? otra = HuellaDePlata(m, "fake.exe");
        if (una == null) { Pendiente("Plata.Derivar", "0"); return; }

        Debe(una == otra, "derivar dos veces sobre el mismo bronce da exactamente lo mismo");
        Debe(una.Contains("uia://fake.exe/inicio=0"), "la raíz observada es el suelo de la app");
        Debe(una.Contains("uia:name=Documentos;ct=TreeItem=Cromo"),
            "una puerta presente en todas las pantallas se deriva como mobiliario, sin que nadie lo diga");
        // «UN SELECTOR CON DOS DESTINOS ES RELATIVO» NO SE PUEDE JUZGAR TODAVÍA, y la razón es más
        // interesante que la promesa. Este fixture cruza `upButton` a DOS destinos distintos a
        // propósito, que es justo la evidencia que haría falta. Pero `LearnTraversal` empieza
        // preguntando `EsGestoDeAtras`, que reconoce upButton/backButton/forwardButton POR SU
        // NOMBRE y se va sin acuñar arista (promesa 4: el atrás no acuña, porque una arista de
        // vuelta afirma una jerarquía que no existe). Resultado: el bronce no guarda ni uno de los
        // dos destinos, y la derivación no puede ver lo que la lista de nombres ya borró.
        //
        // Es la MISMA raíz por la que hoy no se puede derivar `Accion` —un clic que no cambió de
        // pantalla tampoco deja rastro—: al bronce le falta anotar los cruces que no produjeron
        // nodo. Mientras eso no exista, exigirlo aquí sería pedirle al contrato que juzgue una
        // capacidad que ninguna fase del plan ha empezado (2026-08-10, medido al integrar).
        //
        // Se deja escrito y NO se borra: el día que el bronce anote esos cruces, esta línea vuelve.
        if (una.Contains("uia:aid=upButton;ct=Button="))
            Console.WriteLine("   ⧗ «relativa derivada» sigue pendiente: el gesto de volver se "
                + "reconoce por nombre y borra la evidencia antes de que la derivación la vea");
        Debe(una.Contains("uia:name=factura-01.pdf;ct=ListItem=Contenido"),
            "uno de diez hermanos iguales es contenido, no estructura");
        Debe(una.Contains("uia:name=Ajustes;ct=Button=SinCruzar"),
            "lo que nunca se cruzó se queda SIN CRUZAR: el hueco se ve, no se rellena");
    }

    private static void PlataNoDependeDelPaseo(SurfaceMap m)
    {
        MontarBronce(m, alReves: false);
        string? comoUno = HuellaDePlata(m, "fake.exe");
        if (comoUno == null) { Pendiente("Plata.Derivar", "0"); return; }

        // El mismo terreno, andado al revés, en un mapa recién nacido y en su propio directorio.
        string otroDir = Path.Combine(_raiz, "12-al-reves");
        Directory.CreateDirectory(otroDir);
        Environment.SetEnvironmentVariable("U_DATA_DIR", otroDir);
        var otro = SurfaceMap.Load();
        MontarBronce(otro, alReves: true);
        string? comoOtro = HuellaDePlata(otro, "fake.exe");

        Debe(comoUno == comoOtro,
            "la estructura sale del terreno, no del orden en que se paseó por él");
    }

    private static void BronceIgnoraLaPlata(SurfaceMap m)
    {
        MontarBronce(m);
        string? antes = HuellaDeBronce(m, "fake.exe");
        if (antes == null) { Pendiente("Bronce.De", "1"); return; }

        m.FijarNivel("fake.exe", "Ajustes", 1, porPersona: true, cromo: true);
        m.FijarNivel("fake.exe", "2026", 3, porPersona: false);

        Debe(antes == HuellaDeBronce(m, "fake.exe"),
            "declarar niveles no cambia el bronce: lo observado es lo observado");
    }

    private static void LaPersonaManda(SurfaceMap m)
    {
        MontarBronce(m);
        // La derivación dice que «Ajustes» no se ha cruzado. Una persona sabe que es mobiliario.
        m.FijarNivel("fake.exe", "Ajustes", 1, porPersona: true, cromo: true);

        string? h = HuellaDePlata(m, "fake.exe");
        if (h == null) { Pendiente("Plata.Derivar", "0"); return; }

        Debe(h.Contains("uia:name=Ajustes;ct=Button=Cromo"),
            "lo que declaró una PERSONA manda sobre el cálculo: sabe algo que el bronce no dice");
        Debe(Metrica(m, "fake.exe", "Desacuerdos") >= 1,
            "…y queda anotado como desacuerdo: o el cálculo aprende, o la declaración estaba mal");
    }

    private static void ElModeloNoManda(SurfaceMap m)
    {
        MontarBronce(m);
        string? antes = HuellaDePlata(m, "fake.exe");
        if (antes == null) { Pendiente("Plata.Derivar", "0"); return; }

        // El maestro de visión y los landmarks de una web entran por aquí: porPersona = false.
        m.FijarNivel("fake.exe", "Ajustes", 1, porPersona: false, cromo: true);
        m.FijarNivel("fake.exe", "2026", 4, porPersona: false);

        Debe(antes == HuellaDePlata(m, "fake.exe"),
            "lo que dice un modelo se contrasta, no se obedece: la derivación no se mueve");
        Debe(Metrica(m, "fake.exe", "Desacuerdos") >= 1,
            "…pero se anota, que para eso se le pregunta");
    }

    private static void SinPlataNoHayAtajo(SurfaceMap m)
    {
        MontarBronce(m);
        // NADIE ha declarado nada: todo el mobiliario de este bronce es derivado.
        Debe(!m.Edges().Any(e => e.Info.NivelFijado),
            "el fixture no trae ninguna declaración: lo que venga, viene del cálculo");

        var ruta = m.Route("uia://fake.exe/fotos", "uia://fake.exe/docs");
        Debe(ruta != null && ruta.Count == 1,
            "desde una pantalla cualquiera se llega al mobiliario derivado en UN salto");

        if (ruta == null)
            Console.WriteLine("   (pendiente de la fase 2: SelectoresCromo() todavía solo lee lo declarado)");
    }

    private static void SinRaizNoSeSitua(SurfaceMap m)
    {
        // Terreno sin una sola observación: hay aristas, pero nadie entró por la puerta principal,
        // así que ninguna pantalla lleva el sello de raíz.
        m.LearnTraversal("uia://huerfana.exe/a", "uia://huerfana.exe/b",
            "uia:name=B;ct=Button", Array.Empty<string>(), "B", "Button");

        string? h = HuellaDePlata(m, "huerfana.exe");
        if (h == null) { Pendiente("Plata.Derivar", "0"); return; }

        Debe(h.StartsWith("raiz=|"), "sin raíz observada no se elige una: adivinarla sería peor");
        Debe(!h.Contains("=0,") && !h.EndsWith("=0"),
            "y entonces NADA queda situado: la jerarquía entera colgaría de una suposición");
    }

    private static void ElDiscoNoMezcla(SurfaceMap m)
    {
        MontarBronce(m);
        m.FijarNivel("fake.exe", "Ajustes", 1, porPersona: true, cromo: true);
        m.Save();

        string archivo = Path.Combine(U.Graph.UserPaths.Local, "U", "surface-map.json");
        Debe(File.Exists(archivo), "el bronce se guarda donde dice que lo guarda");
        string crudo = File.Exists(archivo) ? File.ReadAllText(archivo) : "";

        foreach (string campo in new[] { "NivelFijado", "PorPersona", "EsCromo", "KindDeclarado" })
            Debe(!crudo.Contains(campo, StringComparison.Ordinal),
                $"el archivo del bronce no guarda «{campo}»: lo declarado vive en su propia capa");
    }

    /// <summary>
    /// Lo encontró la propia spec antes de que existiera el código, que es para lo que sirve
    /// escribirla primero: una sección a la que solo se llega por el panel lateral se coloca en el
    /// primer nivel y ahí se acaba el recorrido, así que TODO lo que cuelga de ella se queda sin
    /// situar. En un explorador de archivos eso es la app entera.
    /// </summary>
    private static void ElCromoNoCortaLaRama(SurfaceMap m)
    {
        MontarBronce(m);
        string? h = HuellaDePlata(m, "fake.exe");
        if (h == null) { Pendiente("Plata.Derivar", "0"); return; }

        Debe(h.Contains("uia://fake.exe/docs=1"),
            "la sección a la que solo se llega por el mobiliario vive en el primer nivel");
        Debe(h.Contains("uia://fake.exe/docs/2026=2"),
            "y lo que hay DENTRO de ella está un nivel más abajo, no sin situar");
    }

    // ── Pedir por nombre lo que quizá no existe ──────────────────────────────
    //
    // Mismo motivo que en la promesa 10: este contrato juzga también a núcleos de antes de que
    // existiera nada de esto, y llamar a `Plata` directamente rompería su COMPILACIÓN. Con una
    // diferencia deliberada: allí la ausencia es «no aplicable», aquí es PENDIENTE y cuenta como
    // incumplida. Un núcleo viejo no promete lo que no conoce; el que estamos escribiendo sí.

    private static readonly System.Reflection.Assembly Nucleo = typeof(SurfaceMap).Assembly;

    private static object? Derivacion(SurfaceMap m, string app)
    {
        var t = Nucleo.GetType("U.WindowsClient.Navigation.Plata");
        return t?.GetMethod("Derivar")?.Invoke(null, new object[] { m, app });
    }

    private static string? HuellaDePlata(SurfaceMap m, string app)
    {
        var t = Nucleo.GetType("U.WindowsClient.Navigation.Plata");
        var p = Derivacion(m, app);
        if (t == null || p == null) return null;
        return t.GetMethod("Huella")?.Invoke(null, new[] { p }) as string;
    }

    private static string? HuellaDeBronce(SurfaceMap m, string app)
    {
        var t = Nucleo.GetType("U.WindowsClient.Navigation.Bronce");
        var b = t?.GetMethod("De")?.Invoke(null, new object[] { m, app });
        if (t == null || b == null) return null;
        return t.GetMethod("Huella")?.Invoke(null, new[] { b }) as string;
    }

    private static int Metrica(SurfaceMap m, string app, string nombre)
    {
        var p = Derivacion(m, app);
        var metricas = p?.GetType().GetProperty("M")?.GetValue(p);
        var valor = metricas?.GetType().GetProperty(nombre)?.GetValue(metricas);
        return valor is int i ? i : -1;
    }

    private static void Pendiente(string capacidad, string fase)
    {
        _fallos++;
        _pendientes++;
        Console.WriteLine($"   ⧗ PENDIENTE: «{capacidad}» todavía no existe (fase {fase} del plan). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar.");
    }

    // ── El arnés ─────────────────────────────────────────────────────────────

    // ── El freno ─────────────────────────────────────────────────────────────
    //
    // Nació de un incidente: una recolocación del escritorio se quedó en bucle moviendo el cursor
    // entre dos casillas, y no había forma de intervenir salvo matar la app (2026-08-16). El bucle
    // se arregló; la ausencia de freno no era un fallo, era que nunca se había puesto.
    //
    // Estas cinco promesas no hablan de teclas: hablan de CUÁNDO un alto cuenta y cuándo no. Es la
    // parte que se puede romper en silencio — la tecla, si deja de verse, se nota al primer intento.

    private static void FrenoOciosoNoSeArma(SurfaceMap _)
    {
        Freno.Termine();                       // nada en marcha
        Freno.Pide("prueba");
        Debe(!Freno.Pidieron,
            "un alto pedido sin nada en marcha NO deja el freno armado; si lo dejara, el siguiente "
            + "trabajo nacería abortado sin que nadie hubiera pedido nada");
    }

    private static void FrenoSeArmaAlEmpezar(SurfaceMap _)
    {
        Freno.Empezar("lo primero");
        Freno.Pide("el usuario se arrepintió");
        Debe(Freno.Pidieron, "con algo en marcha, pedir el alto SÍ arma el freno");

        Freno.Termine();
        Freno.Empezar("lo siguiente");
        Debe(!Freno.Pidieron,
            "empezar una tarea nueva desarma lo pedido antes: un Escape de hace diez minutos, para "
            + "otra cosa, no puede abortar lo que se pida ahora");
        Freno.Termine();
    }

    private static void FrenoAvisaUnaSolaVez(SurfaceMap _)
    {
        int avisos = 0;
        void Contar() => Interlocked.Increment(ref avisos);
        Freno.Pidio += Contar;
        try
        {
            Freno.Empezar("algo largo");
            for (int i = 0; i < 10; i++) Freno.Pide($"insistencia {i}");
            Debe(avisos == 1,
                $"se avisa UNA vez por tarea, no una por pulsación (llegaron {avisos}); quien escucha "
                + "esto suelta el ratón y habla, y hacerlo diez veces se ve como un tartamudeo");
        }
        finally { Freno.Pidio -= Contar; Freno.Termine(); }
    }

    private static void FrenoCortaElSueno(SurfaceMap _)
    {
        Freno.Empezar("una pausa larga");
        var reloj = System.Diagnostics.Stopwatch.StartNew();
        var pide = new Thread(() => { Thread.Sleep(120); Freno.Pide("Escape"); });
        pide.Start();

        bool hayQueParar = Freno.Duerme(3000);
        reloj.Stop();
        pide.Join();
        Freno.Termine();

        Debe(hayQueParar, "dormir devuelve true cuando se pidió el alto mientras dormía");
        Debe(reloj.ElapsedMilliseconds < 1000,
            $"y CORTA de verdad: tardó {reloj.ElapsedMilliseconds} ms de 3000. Dormir de un tirón es "
            + "tiempo sin poder pararse, y son justo los ratos en que alguien decide que ya vio bastante");
    }

    private static void FrenoSueltaAlTerminar(SurfaceMap _)
    {
        Freno.Empezar("algo");
        Freno.Termine();
        Freno.Pide("Escape después de acabar");
        Debe(!Freno.Pidieron,
            "acabada la tarea, Escape vuelve a ser una tecla cualquiera: quien no está haciendo nada "
            + "no se entera de nada");
    }

    // ── El freno, EN LA PUERTA ───────────────────────────────────────────────
    //
    // Las promesas 21-25 describen cuándo un alto cuenta. Estas tres describen algo distinto y más
    // fuerte: que con el alto echado NO SE PUEDE actuar, aunque el código que actúa no sepa que el
    // freno existe.
    //
    // El porqué lo dijo el usuario mirando el diseño anterior (2026-08-22): «me parece raro que
    // tengamos que fijarnos por nosotros mismos que esc esté habilitado en todo». Tenía razón. Un
    // freno que cada bucle debe acordarse de consultar protege los bucles que ya existen y ninguno
    // de los que se escriban mañana. Puesto en la puerta, es imposible escribir código que lo
    // ignore — que es la misma diferencia que hay entre un documento y un hook.

    private static void FrenoCierraLaPuertaDeEntrada(SurfaceMap _)
    {
        Freno.Empezar("algo que mueve el ratón");
        Freno.Pide("Escape");

        Debe(!InputExecutor.Key("tab"), "una tecla NO se manda con el freno echado");
        Debe(!InputExecutor.Tap(10, 10), "un clic NO se manda con el freno echado");
        Debe(!InputExecutor.TypeText("hola"), "escribir NO se manda con el freno echado");
        Debe(!InputExecutor.Scroll(true), "desplazar NO se manda con el freno echado");

        Freno.Termine();
        // Se sonda con texto VACÍO: pasa por la misma guarda y no teclea nada. El contrato corre
        // sobre la máquina de verdad, y una prueba que escribe de verdad acaba escribiendo en la
        // ventana de alguien.
        Debe(InputExecutor.TypeText(""),
            "y al soltarse vuelve a funcionar: el freno no puede dejar la máquina muerta");
    }

    private static void FrenoCierraLaPuertaDeUia(SurfaceMap _)
    {
        Freno.Empezar("pulsar algo en pantalla");
        Freno.Pide("Escape");

        var puerta = new U.Graph.Surfaces.UiaSurface();
        bool hizo = puerta.Execute(
            new U.Graph.PlanStep { StepOrder = 1, ActionType = "click", Selector = "uia:name=loQueSea", Label = "loQueSea" },
            out string error);

        Freno.Termine();

        Debe(!hizo, "la puerta de la pantalla se NIEGA a actuar con el freno echado");
        Debe(error.Contains("paraste", StringComparison.OrdinalIgnoreCase)
             || error.Contains("Escape", StringComparison.OrdinalIgnoreCase)
             || error.Contains("freno", StringComparison.OrdinalIgnoreCase),
            $"y DICE que fue el freno, no un fallo cualquiera (dijo: «{error}»). Un «no se encontró» "
            + "haría que quien lo lea busque el elemento en vez de entender que lo paraste tú");
    }

    private static void FrenoDevuelveElControlHablando(SurfaceMap _)
    {
        string dicho = "";
        void Oir(string t) => dicho = t;
        Freno.Dice += Oir;
        try
        {
            Freno.Empezar("algo largo");
            Freno.Pide("Escape");
            Debe(dicho.Length > 0,
                "al pararse, Ü DICE algo: pararse en silencio se vive igual que colgarse, y la "
                + "diferencia entre las dos es justo lo que hay que comunicar");
            Debe(dicho.Contains("control", StringComparison.OrdinalIgnoreCase),
                $"y lo que dice es que devuelve el control (dijo: «{dicho}»)");
        }
        finally { Freno.Dice -= Oir; Freno.Termine(); }
    }

    // ── SITUARSE ─────────────────────────────────────────────────────────────
    //
    // La segunda capacidad más pedida por una persona en 26 días (105 veces) y la que sostiene a las
    // otras cuatro: señalar, pulsar e ir heredan lo que esta diga. Por eso se muda la primera.
    //
    // Las tres promesas son sobre lo mismo: NO PROMETER TERRENO QUE NO ESTÁ. Un mapa que cuenta
    // cuarenta salidas cuando treinta y ocho son recuerdo no está informando, está apostando — y la
    // apuesta la paga quien intente cruzarlas.

    private static Nucleo.Grafo GrafoConUnaPantalla(out string donde)
    {
        donde = "uia://falsa.exe/pantalla";
        var g = new Nucleo.Grafo();
        g.Observar(donde, new[]
        {
            new Nucleo.Elemento("uia:name=Uno", "Uno", "Button"),
            new Nucleo.Elemento("uia:name=Dos", "Dos", "Button"),
        });
        return g;
    }

    private static void SituarseSeparaVivoDeMemoria(SurfaceMap _)
    {
        var g = GrafoConUnaPantalla(out string donde);
        var situarse = new AquiSegunElNucleo(g, () => donde);

        string conLasDos = situarse.Ahora();
        Debe(conLasDos.Contains("2 salida"),
            $"con las dos a la vista se dicen dos (dijo: «{conLasDos}»)");

        // Ahora solo se ve una: la otra pasa a ser recuerdo, y eso TIENE que notarse.
        g.Observar(donde, new[] { new Nucleo.Elemento("uia:name=Uno", "Uno", "Button") });
        string conUna = situarse.Ahora();

        Debe(conUna.Contains("1 salida"),
            $"cuando solo se ve una, se dice una (dijo: «{conUna}»)");
        Debe(conUna.Contains("recuerdo"),
            "y se DICE que hay más recordadas: callarlas haría creer que desaparecieron, y "
            + "contarlas como vivas prometería un camino que ahora no está delante");
    }

    private static void SituarseNoConfundeVacioConSinMirar(SurfaceMap _)
    {
        var g = new Nucleo.Grafo();
        var situarse = new AquiSegunElNucleo(g, () => "uia://falsa.exe/jamas-mirada");
        string r = situarse.Ahora();

        Debe(r.Contains("no he mirado", StringComparison.OrdinalIgnoreCase),
            $"de un sitio sin mirar se dice que no se ha mirado (dijo: «{r}»). «Aquí no hay nada» "
            + "invita a rendirse; «no he mirado» invita a mirar, y solo una de las dos es cierta");
        Debe(!r.Contains("0 salida"), "y NO se cuenta como cero");
    }

    private static void SituarseNoAdivina(SurfaceMap _)
    {
        var situarse = new AquiSegunElNucleo(new Nucleo.Grafo(), () => "");
        Debe(situarse.Ahora() == AquiSegunElNucleo.NiIdea,
            "sin ubicación no se contesta con la última conocida ni con una aproximación: se dice "
            + "que no se sabe. Una ubicación inventada envenena todo lo que se apoye en ella");
    }

    // ── SEÑALAR ──────────────────────────────────────────────────────────────
    //
    // La capacidad más usada de todas (168 veces en 26 días) y la que nadie diseñó como tal.
    //
    // Lo que se juzga aquí NO es leer la pantalla —eso es UIA y necesita un cursor— sino lo único
    // que puede equivocarse en silencio: qué se contesta sobre lo señalado. Las tres respuestas
    // posibles llevan a conversaciones distintas, y fundirlas en «no puedo» haría que quien
    // pregunta se rinda en los dos casos en los que sí había salida.

    // ── ABRIR ────────────────────────────────────────────────────────────────
    //
    // 92 veces en 26 días. CÓMO se llega no se decide aquí —eso es del mapeador, que tiene sus
    // propias promesas— sino las tres cosas que se hacían mal: relanzar lo que ya estaba, dar por
    // hecho que lanzar es llegar, y fallar sin decir dónde te deja.

    /// <summary>Un abridor de mentira: la ubicación CAMBIA cuando se logra traer algo al frente,
    /// que es lo que pasa de verdad. Un arnés con una ubicación fija no podría distinguir «miró
    /// después» de «contestó lo que ya sabía», que es justo lo que la promesa 37 juzga.</summary>
    private static AbrirSegunElNucleo AbrirCon(string antes, string despues, bool loLogra, List<string> lanzados)
    {
        string donde = antes;
        return new(() => donde,
            plan => { lanzados.Add(plan.Que); if (loLogra) donde = despues; return loLogra; },
            _ => "");
    }

    private static void AbrirNoRelanzaLoQueYaEsta(SurfaceMap _)
    {
        var lanzados = new List<string>();
        string r = AbrirCon("uia://chrome.exe/inicio", "uia://chrome.exe/inicio", true, lanzados).Abrir("chrome");

        Debe(lanzados.Count == 0,
            "estando ya delante NO se toca nada: relanzar deja dos ventanas de lo mismo y pierde lo "
            + "que hubiera a medias en la primera");
        Debe(r.Contains("ya estás"), $"y se dice que ya estabas (dijo: «{r}»)");
    }

    private static void AbrirDiceDondeQuedamos(SurfaceMap _)
    {
        var lanzados = new List<string>();
        string r = AbrirCon("uia://chrome.exe/inicio", "uia://notepad.exe/sin-titulo", true, lanzados).Abrir("notepad");

        Debe(lanzados.Count == 1, "no estando delante, sí se abre");
        Debe(r.Contains("uia://notepad.exe/sin-titulo"),
            $"y se contesta con DÓNDE quedamos, mirando DESPUÉS (dijo: «{r}»). Lanzar es una "
            + "petición, no una llegada: un «lo abrí» sin mirar es éxito declarado");
        Debe(!r.Contains("chrome"), "y no con dónde estábamos antes");
    }

    private static void AbrirDiceDondeEstamosAlFallar(SurfaceMap _)
    {
        var lanzados = new List<string>();
        string r = AbrirCon("uia://otracosa.exe/loquesea", "", false, lanzados).Abrir("notepad");

        Debe(r.Contains("uia://otracosa.exe/loquesea"),
            $"al fallar se dice QUÉ hay ahora (dijo: «{r}»). Un «no pude» pelado deja a quien lo lee "
            + "sin saber si está donde creía — y el 2026-08-21 eso costó un mensaje que se "
            + "desmentía a sí mismo: «no pude traerla al frente; ahora hay saplogon»");
    }

    /// <summary>El catálogo real de esta máquina, medido el 2026-08-23 en shell:AppsFolder.</summary>
    private static readonly AbrirSegunElNucleo.AppDelSistema[] Instaladas =
    {
        new("Microsoft To Do", "Microsoft.Todos_8wekyb3d8bbwe!App"),
        new("Click to Do", "MicrosoftWindows.Client.CoreAI_cw5n1h2txyewy!ClickToDoApp"),
        new("Claude", "Claude_pzs8sxrjxfjjc!Claude"),
        new("Spotify", "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify"),
        new("Calculadora", "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"),
    };

    private static void AbrirEncuentraLaAppInstalada(SurfaceMap _)
    {
        var r = AbrirSegunElNucleo.Emparejar("microsoft to do", Instaladas);
        Debe(r.Count == 1 && r[0].ComoSeLanza.StartsWith("Microsoft.Todos"),
            $"«microsoft to do» encuentra Microsoft To Do (encontró {r.Count}). Hasta hoy abrir "
            + "asumía que todo era un .exe, y las apps empaquetadas NO se podían abrir: el menú "
            + "Inicio tiene 93 accesos directos y To Do no está entre ellos");

        Debe(AbrirSegunElNucleo.Emparejar("CALCULADORA", Instaladas).Count == 1,
            "las mayúsculas no cuentan");
        Debe(AbrirSegunElNucleo.Emparejar("calculadora", Instaladas).Count == 1
             && AbrirSegunElNucleo.Emparejar("cálculadora", Instaladas).Count == 1,
            "y las tildes tampoco: quien habla no escribe los acentos");
        Debe(AbrirSegunElNucleo.Emparejar("spotify", Instaladas).Count == 1, "y un nombre suelto acierta");
        Debe(AbrirSegunElNucleo.Emparejar("pepito", Instaladas).Count == 0,
            "y lo que no está no se parece a nada: cero, no lo más cercano");
    }

    private static void AbrirNoAdivinaElEmpate(SurfaceMap _)
    {
        // «to do» encaja de verdad con las dos, y no hay forma honesta de saber cuál.
        var r = AbrirSegunElNucleo.Emparejar("to do", Instaladas);
        Debe(r.Count == 2,
            $"con un empate real se devuelven TODAS (devolvió {r.Count}). Elegir por longitud o por "
            + "orden alfabético es acertar la mitad de las veces y equivocarse EN SILENCIO la otra "
            + "mitad, que es peor que preguntar");
    }

    private static void LoSenaladoCaduca(SurfaceMap _)
    {
        var ahora = new DateTime(2026, 8, 23, 15, 0, 0, DateTimeKind.Utc);

        Debe(LoQueSenalas.SigueValiendo(ahora.AddSeconds(-5), ahora),
            "lo señalado hace cinco segundos vale: se señala, se pregunta, se contesta y se pide");
        Debe(!LoQueSenalas.SigueValiendo(ahora.AddMinutes(-10), ahora),
            "lo señalado hace diez minutos NO. Señalar hace que algo sea accionable aunque no esté "
            + "en el mapa de esta pantalla; si eso no caducara, un «púlsalo» dicho mucho después "
            + "actuaría sobre algo que ya no está delante, y con la confianza de haber acertado");
    }

    private static void LaAppInstaladaGanaALaPestana(SurfaceMap _)
    {
        // El caso real del 2026-08-23: pedir «copilot» con copilot.microsoft.com abierto llevaba a
        // la WEB, y el usuario tuvo que decir «no quiero la web, quiero la instalada». Antes pasó
        // igual con Claude y no se reprodujo porque la pestaña no estaba abierta.
        var lanzadas = new List<string>();
        var instaladas = new[]
        {
            new AbrirSegunElNucleo.AppDelSistema("Copilot", "Microsoft.Copilot_8wekyb3d8bbwe!App"),
            new AbrirSegunElNucleo.AppDelSistema("Microsoft 365 Copilot", "Microsoft.MicrosoftOfficeHub_8wekyb3d8bbwe!App"),
        };
        // La ubicación CAMBIA al lanzar, que es lo que pasa de verdad: un arnés con una ubicación
        // fija no distingue «miró después» de «contestó lo que ya sabía».
        string donde = "web://copilot.microsoft.com";
        var abrir = new AbrirSegunElNucleo(
            () => donde,
            _ => false,
            _ => "copilot.microsoft.com",              // sí suena a una pestaña abierta
            () => instaladas,
            id => { lanzadas.Add(id); donde = "uia://mscopilot.exe/copilot"; return true; });

        string r = abrir.Abrir("copilot");
        Debe(lanzadas.Count == 1 && lanzadas[0].StartsWith("Microsoft.Copilot"),
            $"se abre la app INSTALADA, no la pestaña (lanzó {lanzadas.Count}). Una web que se llama "
            + "igual que una app no es esa app");
        Debe(r.Contains("uia://mscopilot.exe/copilot") && !r.Contains("web://"),
            $"y se acaba EN LA APP, no en la web (dijo: «{r}»)");

        // Pero NO se secuestra lo que solo se PARECE: pedir una web es igual de legítimo.
        var soloParecido = new List<string>();
        var abrir2 = new AbrirSegunElNucleo(
            () => "uia://chrome.exe/x", _ => true, _ => "github.com",
            () => new[] { new AbrirSegunElNucleo.AppDelSistema("GitHub Desktop", "GitHubDesktop!App") },
            id => { soloParecido.Add(id); return true; });
        abrir2.Abrir("github");
        Debe(soloParecido.Count == 0,
            "«github» NO abre «GitHub Desktop»: lo que da la preferencia es llamarse ASÍ, no "
            + "parecerse. Con «contiene» bastaría una app con esa palabra dentro para secuestrar "
            + "cualquier web");
    }

    private static void SenalarNoExigeElNombreClavado(SurfaceMap _)
    {
        Debe(LoQueSenalas.SeRefiereA("Copilot", "Copilot anclado"),
            "«Copilot» se refiere a «Copilot anclado». Windows llama a las cosas como le da la gana "
            + "y nadie dice «anclado»: exigir el nombre clavado hacía fallar «¿ves esto? ábrelo» "
            + "SIEMPRE que el nombre real llevara una palabra de más, que es casi siempre");
        Debe(LoQueSenalas.SeRefiereA("claude", "Claude- 2 ventanas de ejecución"),
            "y tampoco con las mayúsculas ni la coletilla de las ventanas");
        Debe(!LoQueSenalas.SeRefiereA("spotify", "Copilot anclado"),
            "pero dos cosas distintas siguen siendo distintas");
    }

    // ── PULSAR ───────────────────────────────────────────────────────────────
    //
    // La versión mínima de IR: ir no es más que preguntar el siguiente paso y pulsarlo, en bucle.
    // Por eso va antes — construir el bucle antes que el paso es construir sobre nada.

    private static PulsarSegunElNucleo PulsarCon(Nucleo.Grafo g, string antes, string despues, bool loLogra, List<string> tocados)
    {
        string donde = antes;
        return new PulsarSegunElNucleo(g, () => donde,
            (sel, et) => { tocados.Add(et); if (loLogra) donde = despues; return loLogra; })
            { EsperaMaximaMs = 240 };   // el arnés no necesita esperar a ninguna pantalla
    }

    private static void PulsarSinMoverNoEsLlegar(SurfaceMap _)
    {
        var g = new Nucleo.Grafo();
        var r = PulsarCon(g, "uia://x.exe/uno", "uia://x.exe/uno", true, new List<string>())
            .Pulsa("uia:name=Guardar", "Guardar");

        Debe(r.SePudo && !r.CambioLaPantalla,
            "se pudo pulsar y la pantalla NO cambió, y son dos cosas distintas");
        Debe(r.Cuenta.Contains("no cambió"),
            $"y se dice tal cual (dijo: «{r.Cuenta}»). Un botón de acción —Guardar, Copiar— hace "
            + "su trabajo sin cambiar de pantalla: llamar a eso un fracaso sería reportar mal algo "
            + "que salió bien");
    }

    private static void PulsarQueNoSePudoNoCuenta(SurfaceMap _)
    {
        // Esta promesa sustituye a una que escribí mal: «si no se movió, no se acuña el tramo» NO
        // podía ponerse roja, porque el propio núcleo lo impide (Grafo.Cruzar rechaza un destino
        // igual al origen). Una promesa que la capa de abajo ya garantiza es un verde que no prueba
        // nada — justo lo que este contrato existe para no tener (2026-08-23).
        //
        // Esto sí puede romperse: dar por hecho un clic que ni siquiera se llegó a dar. Y es de los
        // fallos que más caro salen, porque lo siguiente se pide creyendo que estamos en otro sitio.
        var g = new Nucleo.Grafo();
        g.Observar("uia://x.exe/uno", new[] { new Nucleo.Elemento("uia:name=Ir", "Ir", "Button") });

        var tocados = new List<string>();
        var r = PulsarCon(g, "uia://x.exe/uno", "uia://x.exe/otro", loLogra: false, tocados)
            .Pulsa("uia:name=Ir", "Ir");

        Debe(!r.SePudo, "un clic que la pantalla no aceptó se dice que NO se pudo");
        Debe(!r.CambioLaPantalla && !r.Aprendido,
            "y no se inventa ni movimiento ni aprendizaje a partir de él");
        Debe(r.Desde == r.Hasta && r.Desde == "uia://x.exe/uno",
            $"y se sigue estando donde se estaba (dijo: de «{r.Desde}» a «{r.Hasta}»). Dar por hecho "
            + "un clic que no ocurrió hace que lo SIGUIENTE se pida creyéndose en otro sitio");
    }

    private static void PulsarAprendeADondeLlevoDeVerdad(SurfaceMap _)
    {
        var g = new Nucleo.Grafo();
        g.Observar("uia://x.exe/uno", new[] { new Nucleo.Elemento("uia:name=Ir", "Ir", "Button") });

        var r = PulsarCon(g, "uia://x.exe/uno", "uia://x.exe/OTRO-distinto", true, new List<string>())
            .Pulsa("uia:name=Ir", "Ir");

        Debe(r.CambioLaPantalla && r.Aprendido, "cruzar de verdad SÍ se aprende");
        Debe(r.Hasta == "uia://x.exe/OTRO-distinto",
            $"y se aprende a dónde llevó DE VERDAD (dijo: «{r.Hasta}»), no a dónde se creía. "
            + "El terreno manda sobre el mapa: así el grafo se corrige solo yendo");
        Debe(g.DesdeAqui("uia://x.exe/uno").Any(a => a.Destino == "uia://x.exe/OTRO-distinto"),
            "y queda en el grafo, no solo en la respuesta");
    }

    private static void SenalarDistingueLasTres(SurfaceMap _)
    {
        const string donde = "uia://falsa.exe/pantalla";
        var g = new Nucleo.Grafo();
        g.Observar(donde, new[]
        {
            new Nucleo.Elemento("uia:name=Guardar", "Guardar", "Button"),
            new Nucleo.Elemento("uia:name=Cerrar", "Cerrar", "Button"),
        });
        // Ahora solo se ve «Guardar»: «Cerrar» pasa a ser recuerdo.
        g.Observar(donde, new[] { new Nucleo.Elemento("uia:name=Guardar", "Guardar", "Button") });

        var senalar = new LoQueSenalas(g, () => donde);

        string vivo = senalar.Con(new LoQueSenalas.Senalado("Guardar", "Button", true));
        Debe(vivo.Contains("puedo pulsarlo"), $"lo que se ve AHORA se ofrece (dijo: «{vivo}»)");

        string recordado = senalar.Con(new LoQueSenalas.Senalado("Cerrar", "Button", true));
        Debe(recordado.Contains("recuerdo"),
            $"lo que se recuerda pero no se ve se dice ASÍ, no como imposible (dijo: «{recordado}»)");
        Debe(!recordado.Contains("puedo pulsarlo"),
            "y sobre todo NO se ofrece como pulsable: ofrecerlo manda a alguien contra una pared");

        string desconocido = senalar.Con(new LoQueSenalas.Senalado("Jamás visto", "Button", true));
        Debe(desconocido.Contains("no lo tengo en el mapa"),
            $"y lo que no se conoce se dice desconocido (dijo: «{desconocido}»)");
    }

    private static void SenalarEligeLoMasPequeno(SurfaceMap _)
    {
        // El caso real de la barra de tareas de Windows 11, medido el 2026-08-22: bajo el cursor
        // hay un Pane SIN NOMBRE cuyo padre tampoco lo tiene, y el nombre está en los DESCENDIENTES.
        var punto = new System.Windows.Point(660, 1055);
        var candidatos = new[]
        {
            new LoQueSenalas.Candidato("", "Pane", new System.Windows.Rect(0, 1040, 1920, 40)),
            new LoQueSenalas.Candidato("Aplicaciones en ejecución", "Pane", new System.Windows.Rect(400, 1040, 660, 40)),
            new LoQueSenalas.Candidato("Vista de tareas", "Button", new System.Windows.Rect(640, 1040, 82, 40)),
            new LoQueSenalas.Candidato("Otra cosa lejos", "Button", new System.Windows.Rect(0, 0, 50, 50)),
        };

        var elegido = LoQueSenalas.Elegir(candidatos, punto);
        Debe(elegido?.Nombre == "Vista de tareas",
            $"se elige el MÁS PEQUEÑO que contiene el punto (eligió: «{elegido?.Nombre}»). "
            + "Bajo un mismo píxel hay siempre varias cosas y todas lo contienen; la que una persona diría "
            + "que señala es la más específica, nunca el panel entero");

        Debe(LoQueSenalas.Elegir(candidatos, new System.Windows.Point(1900, 20)) == null,
            "y donde no hay nada con nombre no se devuelve lo más cercano: se devuelve nada. "
            + "Acercarse no es acertar");
    }

    private static void ElegirNoDependeDelOrden(SurfaceMap _)
    {
        // El árbol de UIA devuelve los descendientes en un orden que no controlamos, y al saltar
        // nuestra propia ventana se recorren además VARIAS ventanas seguidas. Si elegir dependiera
        // del orden, lo señalado cambiaría entre dos preguntas idénticas — y eso es de los fallos
        // que solo aparecen en la máquina de otro (2026-08-23).
        var punto = new System.Windows.Point(660, 1055);
        var grande = new LoQueSenalas.Candidato("El panel entero", "Pane", new System.Windows.Rect(0, 1040, 1920, 40));
        var chico  = new LoQueSenalas.Candidato("El botón", "Button", new System.Windows.Rect(640, 1040, 82, 40));

        Debe(LoQueSenalas.Elegir(new[] { grande, chico }, punto)?.Nombre == "El botón",
            "el pequeño gana llegando el segundo");
        Debe(LoQueSenalas.Elegir(new[] { chico, grande }, punto)?.Nombre == "El botón",
            "y también llegando el primero: dos preguntas iguales tienen que dar la misma respuesta");
    }

    private static void SenalarNoAdivina(SurfaceMap _)
    {
        var senalar = new LoQueSenalas(new Nucleo.Grafo(), () => "uia://falsa.exe/x");
        Debe(senalar.Con(null) == LoQueSenalas.NadaDebajo,
            "sin nada con nombre bajo el cursor se pide mover el cursor. Contestar con lo último "
            + "señalado sería peor que no contestar: quien pregunta creería que acertó");
    }

    private static void Prueba(string nombre, Action<SurfaceMap> cuerpo)
    {
        // Cada promesa se juzga sobre un mapa recién nacido en su propio directorio.
        string dir = Path.Combine(_raiz, nombre.Split('.')[0]);
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable("U_DATA_DIR", dir);

        int antes = _fallos;
        try { cuerpo(SurfaceMap.Load()); }
        catch (Exception e)
        {
            _fallos++;
            // LA CADENA ENTERA, no solo el mensaje de arriba. Un TypeInitializationException dice
            // «el inicializador de tipo de X lanzó una excepción» y se guarda para sí POR QUÉ, que
            // es lo único que sirve: las diez promesas fallaron con ese texto y no se podía saber
            // si el núcleo estaba roto o si era el arnés (2026-08-08). Es el aprendizaje nº3 —un
            // catch mudo convierte un fallo concreto en «algo no va»— cometido dentro del arnés
            // que existe justo para que eso no pase.
            for (var x = e; x != null; x = x.InnerException)
                Console.WriteLine($"   ✘ {x.GetType().Name}: {x.Message}");
            Console.WriteLine($"     en {e.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}");
        }
        Console.WriteLine($"{(_fallos == antes ? "✔" : "✘")} {nombre}");
    }

    private static void Debe(bool condicion, string promesa)
    {
        if (condicion) return;
        _fallos++;
        Console.WriteLine($"   ✘ {promesa}");
    }
}
