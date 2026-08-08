using System;
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

    private static int _fallos;
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

        Console.WriteLine();
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

    // ── El arnés ─────────────────────────────────────────────────────────────

    private static void Prueba(string nombre, Action<SurfaceMap> cuerpo)
    {
        // Cada promesa se juzga sobre un mapa recién nacido en su propio directorio.
        string dir = Path.Combine(_raiz, nombre[..1]);
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable("U_DATA_DIR", dir);

        int antes = _fallos;
        try { cuerpo(SurfaceMap.Load()); }
        catch (Exception e) { _fallos++; Console.WriteLine($"   ✘ reventó: {e.Message}"); }
        Console.WriteLine($"{(_fallos == antes ? "✔" : "✘")} {nombre}");
    }

    private static void Debe(bool condicion, string promesa)
    {
        if (condicion) return;
        _fallos++;
        Console.WriteLine($"   ✘ {promesa}");
    }
}
