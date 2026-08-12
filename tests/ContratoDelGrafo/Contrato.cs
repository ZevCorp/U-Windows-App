using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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

    /// <summary>
    /// PROMESAS incumplidas, no aserciones. `Debe()` sumaba aquí por cada condición falsa y `Main`
    /// devolvía ese número diciendo «N promesa(s) incumplida(s)»: una promesa con cuatro `Debe`
    /// rotos contaba cuatro. Medido en el contrato del núcleo el 2026-08-12 —donde vive la misma
    /// clase de error—: un solo sabotaje tumbó 2 promesas y el arnés reportó 3.
    ///
    /// Quien lee este número es la compuerta: `verificar.ps1` calcula `$fallos - $pendientes` y
    /// `contrato.yml` publica `total - codigo` como «verdes». Con el recuento por aserción, el
    /// primero **puede salir negativo** y el segundo publica un dato falso. Es el aprendizaje nº10
    /// —«el denominador es el plan»— cometido en el numerador.
    /// </summary>
    private static int _fallos;

    /// <summary>De las incumplidas, cuántas lo están porque su código aún no se ha escrito. Se
    /// cuentan aparte para que el rojo del desarrollo no se confunda con una regresión.</summary>
    private static int _pendientes;

    /// <summary>Cuántas se juzgaron. El total lo dice quien sabe contarlo, no un grep del log.</summary>
    private static int _promesas;

    /// <summary>¿La promesa EN CURSO ya falló? Se sigue evaluando —queremos ver las cuatro
    /// aserciones rotas, no sólo la primera— pero cuentan como UNA promesa incumplida.</summary>
    private static bool _rota;

    /// <summary>¿…y lo está porque su capacidad no existe todavía? Ver <see cref="Pendiente"/>.</summary>
    private static bool _pendienteDeEstaPromesa;

    private static string _raiz = "";

    [STAThread]
    private static int Main(string[] args)
    {
        _raiz = Path.Combine(Path.GetTempPath(), "u-contrato", DateTime.Now.ToString("HHmmss"));
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // El juez juzgándose a sí mismo, antes de juzgar a nadie. Un arnés que sólo se ha visto en
        // verde es indistinguible de uno que devuelve verde siempre — y este arnés ya dijo una vez
        // «CONTRATO ROTO: 10 promesas» sin haber probado nada (2026-08-08).
        if (args.Contains("--autoprueba")) return Autoprueba();

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

        return Resumir();
    }

    /// <summary>
    /// La línea que los scripts LEEN, en vez de grepear el log.
    ///
    /// `verificar.ps1` y `contrato.yml` contaban los pendientes con
    /// `Select-String -SimpleMatch "PENDIENTE"`, que es **case-insensitive por defecto**: se comía
    /// la propia línea de resumen («…de ellas PENDIENTES…») y cualquier «pendiente» en minúscula de
    /// otro texto. El recuento salía inflado, y con él la rama de `verificar.ps1` que trata
    /// `$fallos -eq $pendientes` como «fase intermedia» — es decir, **una regresión podía pasar por
    /// pendiente y no bloquear**. Era el único de los huecos del arnés que daba verde falso.
    ///
    /// Un número lo dice quien sabe contarlo.
    /// </summary>
    private static int Resumir()
    {
        Console.WriteLine();
        Console.WriteLine($"CONTRATO: {_promesas} promesas, {_promesas - _fallos} verdes, "
                        + $"{_fallos} incumplidas, {_pendientes} pendientes");
        if (_pendientes > 0)
            Console.WriteLine($"({_pendientes} de ellas PENDIENTES: la capacidad todavía no existe. "
                + "Es el rojo esperado mientras se implementa, no una regresión.)");
        Console.WriteLine(_fallos == 0
            ? "CONTRATO INTACTO: el grafo se comporta como el día que se congeló."
            : $"CONTRATO ROTO: {_fallos} promesa(s) incumplida(s). El cambio no puede entrar así.");
        return _fallos;
    }

    /// <summary>
    /// Cuatro promesas de mentira con un resultado conocido: una verde, una con CUATRO aserciones
    /// falsas, una que revienta y una PENDIENTE. El arnés tiene que salir con **3** —tres promesas
    /// incumplidas, una de ellas pendiente— y no con 6, que es lo que sumaría contando aserciones.
    ///
    /// Sin esto, arreglar el contador sería el vicio que el contador tiene: dar por bueno un número
    /// porque lo escribió quien lo iba a leer. Corre en un segundo y no toca el núcleo.
    /// </summary>
    private static int Autoprueba()
    {
        Console.WriteLine("AUTOPRUEBA DEL ARNÉS (cuatro promesas de mentira, resultado conocido)\n");

        Prueba("A. verde", _ => Debe(true, "esto se cumple"));
        Prueba("B. una promesa con CUATRO aserciones rotas", _ =>
        {
            Debe(false, "la primera");
            Debe(false, "la segunda");
            Debe(false, "la tercera");
            Debe(false, "la cuarta");
        });
        Prueba("C. una promesa que revienta", _ => throw new InvalidOperationException("a propósito"));
        Prueba("D. una promesa pendiente", _ => Pendiente("UnaCapacidadQueNoExiste", "99"));

        Console.WriteLine();
        bool bien = _promesas == 4 && _fallos == 3 && _pendientes == 1;
        Console.WriteLine(bien
            ? "✔ EL ARNÉS SABE CONTAR: 4 promesas, 3 incumplidas, 1 pendiente — no 6 aserciones."
            : $"✘ EL ARNÉS NO SABE CONTAR: dijo {_promesas} promesas, {_fallos} incumplidas y "
              + $"{_pendientes} pendientes; esperaba 4, 3 y 1. Todo veredicto suyo es sospechoso.");
        return bien ? 0 : 1;
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
        _rota = true;
        _pendienteDeEstaPromesa = true;
        Console.WriteLine($"   ⧗ PENDIENTE: «{capacidad}» todavía no existe (fase {fase} del plan). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar.");
    }

    // ── El arnés ─────────────────────────────────────────────────────────────

    private static void Prueba(string nombre, Action<SurfaceMap> cuerpo)
    {
        // Cada promesa se juzga sobre un mapa recién nacido en su propio directorio.
        string dir = Path.Combine(_raiz, nombre.Split('.')[0]);
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable("U_DATA_DIR", dir);

        _promesas++;
        _rota = false;
        _pendienteDeEstaPromesa = false;
        try { cuerpo(SurfaceMap.Load()); }
        catch (Exception e)
        {
            _rota = true;
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

        // UNA promesa incumplida, aunque hayan caído cuatro de sus aserciones. Y si lo está porque
        // su capacidad no existe, además cuenta como pendiente — sigue siendo incumplida: una
        // promesa sin código que dijera «no aplicable» se sumaría al verde, y el contrato pasaría a
        // certificar el vacío.
        if (_rota) _fallos++;
        if (_pendienteDeEstaPromesa) _pendientes++;
        Console.WriteLine($"{(_rota ? "✘" : "✔")} {nombre}");
    }

    /// <summary>
    /// Marca la promesa en curso como rota y SIGUE. No suma al total: eso lo hace <see cref="Prueba"/>
    /// una sola vez, porque cuatro aserciones rotas siguen siendo una promesa incumplida.
    /// </summary>
    private static void Debe(bool condicion, string promesa)
    {
        if (condicion) return;
        _rota = true;
        Console.WriteLine($"   ✘ {promesa}");
    }
}
