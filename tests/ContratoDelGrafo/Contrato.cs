using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text.Json;
using U.WindowsClient.Actions;
using U.WindowsClient.Mcp;
using U.WindowsClient.Navigation;

namespace ContratoDelGrafo;

/// <summary>
/// EL CONTRATO DEL GRAFO: lo que el núcleo de navegación promete, escrito como pruebas que llaman
/// al código real. Cada invariante de aquí costó una prueba manual del usuario y un diagnóstico;
/// este archivo existe para que ninguna se vuelva a pagar dos veces.
///
/// Desde la gran limpieza (2026-08-30) el núcleo que se juzga es el TERRENO: el grafo puro
/// (nucleo/Grafo, con su propio contrato al lado) y las capacidades que caminan sobre él —situarse,
/// señalar, abrir, pulsar, ir, recorrer en batch, el MCP y SAP—. Cualquier cambio tiene que salir
/// de aquí en verde:
///
///     .\scripts\contrato-del-grafo.ps1
///
/// Si una prueba estorba para un cambio, la conversación es sobre el CONTRATO, no sobre la prueba:
/// cambiarla es cambiar lo que el grafo promete a todo lo que se construye encima.
///
/// Cada prueba corre en un directorio propio (U_DATA_DIR), porque el contrato describe el
/// comportamiento del núcleo, no el historial de nadie.
/// </summary>
internal static class Contrato
{
    private static int _fallos;

    private static string _raiz = "";

    [STAThread]
    private static int Main()
    {
        _raiz = Path.Combine(Path.GetTempPath(), "u-contrato", DateTime.Now.ToString("HHmmss"));
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // ── ACTA DE RETIRO (gran limpieza, 2026-08-30) ──────────────────────
        //
        // Aquí vivieron las promesas 1-20: el mapa por niveles (SurfaceMap), su dwell, sus
        // niveles fijados, su clasificación y la plata derivada del bronce. Ese núcleo se
        // retiró el día que el terreno de verdad —el núcleo puro con Estoy/Observar/Cruzar—
        // navegó SAP de punta a punta: dos modelos del mismo mundo eran dos opiniones, y la
        // que ganó es la que se prueba caminando.
        //
        // No murieron sin descendencia. Las dos que protegían al usuario de perder trabajo
        // tienen herederas en el contrato del NÚCLEO (nucleo/Contrato): la vieja 2 («la
        // enseñanza sobrevive a borrar el grafo») es hoy su promesa 20, y la vieja 8
        // («guardar y cargar no pierde nada») es hoy su promesa 19 — puras, sin Neo4j, por
        // las puertas Recordar→Cruzar→Ensenar que usa el restaurador real. El resto juzgaba
        // maquinaria que ya no existe; su foto vive en la rama experimentos-viejos.
        //
        // La numeración 21+ se conserva: una promesa se cita por su número en commits y
        // diagnósticos viejos, y renumerar rompería esas referencias.

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
        Prueba("47. las homónimas se iluminan por SELECTOR y en el orden que se dicen", IluminarHomonimasPorSelector);
        // Lo que el significado promete —que se guarda, que no se traga frases sin sujeto, que
        // volver a explicar no borra la foto— se juzga ahora en el contrato del NÚCLEO (16 y 17),
        // porque ahí es donde vive. Aquí se queda lo que es de este lado: que la identidad de la
        // que cuelga se pueda volver a encontrar en pantalla.
        Prueba("48. lo enseñado se cuelga de una identidad que se pueda reencontrar", EnsenarExigeIdentidadUtil);
        Prueba("49. una lección se reconoce por cómo se dice, no solo por «esto es X»", UnaLeccionSeReconoce);
        Prueba("50. hablar de memoria no es enseñar: no todo lo que dice «recuerda» es una lección", NoTodoLoQueSuenaEsLeccion);
        Prueba("51. los recuerdos se cuentan de uno en uno: sin hablar no hay siguiente", DeUnoEnUnoONoHaySiguiente);
        Prueba("52. repetir o volver atrás sí se puede: solo AVANZAR exige haber hablado", VolverAtrasNoEsAvanzar);
        Prueba("53. contar el primero no es haber contestado: se sabe cuál falta", ContarNoEsAbandonarAMedias);
        Prueba("54. mientras alguien corrige un recuerdo, la narración espera", EscribirDetieneLaNarracion);
        Prueba("55. el recuadro no adelanta a la voz: sonar no es recibir", ElRecuadroNoAdelantaALaVoz);
        Prueba("56. un paso que no está VIVO no se pulsa: el batch para en la compuerta", LaCompuertaDelBatchMuerde);
        Prueba("57. el batch cuenta lo que hizo: N de M, dónde quedó y qué hay vivo", ElBatchNoMiente);
        Prueba("58. cada paso del batch deja su arista: el grafo se conecta ejecutando", ElBatchFabricaAristas);
        Prueba("59. Escape corta el batch donde va, y se dice", ElFrenoCortaElBatch);
        Prueba("60. el servidor MCP se presenta como MCP manda", ElMcpSePresenta);
        Prueba("61. tools/list publica el catálogo con su esquema", ElMcpPublicaElCatalogo);
        Prueba("62. tools/call despacha por el mismo camino y contesta en content", ElMcpDespachaYContesta);
        Prueba("63. pedir el DESTINO vale tanto como pedir la puerta", PedirElDestinoValeComoLaPuerta);
        Prueba("64. dos puertas al mismo nombre de destino no se adivinan", DosDestinosNoSeAdivinan);
        Prueba("65. la basura de la web ni reclama pasos ni se cuenta como puerta", LaBasuraNoEsUnaPuerta);
        Prueba("66. una web es DIRECCIONABLE: sin camino aprendido se va directo, no se rinde", UnaWebSeVaDirecto);
        Prueba("67. una herramienta colgada no cuelga la puerta MCP", UnaHerramientaColgadaNoCuelgaLaPuerta);
        Prueba("68. cada mundo se OBSERVA por su propia puerta: SAP por scripting, no por UIA", CadaMundoSeObservaPorSuPuerta);
        Prueba("69. cada mundo se PULSA por su propia mano, y el selector decide", CadaMundoSePulsaPorSuMano);
        Prueba("70. en SAP el contenido navegable son las FILAS del árbol, no el árbol", LasFilasDelArbolSonPuertas);
        Prueba("71. cada mundo se ESCRIBE por su propio lápiz: en SAP el texto va al campo, no al aire", CadaMundoSeEscribePorSuLapiz);
        Prueba("72. la sesión SAP es la de la ventana que está DELANTE, no «la primera»", LaSesionEsLaDeDelante);
        Prueba("73. el terreno por delante se CUENTA: tras cada puerta cruzada, lo que recuerda allí", ElTerrenoPorDelanteSeCuenta);
        Prueba("74. el terreno por delante no INVENTA: lo no cruzado es «por descubrir» y la lista no ahoga", ElTerrenoNoInventa);
        Prueba("75. el visor recibe el terreno como ÁRBOL: vivo, recordado y destino, sin inventar", ElArbolDelVisor);
        Prueba("76. cada batch deja rastro consultable, y el anillo no crece sin tope", ElRastroDeLosBatches);
        Prueba("77. el cruce HUMANO en SAP también enseña: el clic se nombra por la puerta de SAP", ElClicHumanoEnSapEnsena);
        Prueba("78. la REJILLA entra al terreno: sus botones y sus filas visibles son puertas", LaRejillaEntraAlTerreno);
        Prueba("79. en el Puesto de trabajo la VISTA elegida es parte del LUGAR", LaVistaEsElLugar);
        Prueba("80. una fila CONOCIDA de un árbol a la vista se alcanza por identidad, aunque esté desplazada", LaFilaDesplazadaSeAlcanza);
        Prueba("81. la CARPETA es parte del nombre: dos «Triage» en carpetas distintas son dos puertas distinguibles", LaCarpetaEsParteDelNombre);

        // ── El gesto (spec 003) ──────────────────────────────────────────────
        //
        // En main hoy la mano manda SIEMPRE «click» (FaceWindow.xaml.cs:465) y nadie escala al
        // doble: un batch que llega a una carpeta del Explorador la SELECCIONA y no la abre. El
        // comentario de FaceWindow:571 dice que la escalada «sigue siendo de UIA» — y es falso, se
        // borró con el codigo viejo. Estas dos son el port de la leccion medida el 2026-08-26 sobre
        // la arquitectura anterior: el gesto se ensaya UNA vez, se guarda EN LA ARISTA (promesa 21
        // del nucleo), y jamas se deduce del tipo — el tipo solo acota que es SEGURO ensayar.
        Prueba("82. el gesto aprendido no se vuelve a ensayar: la segunda vez va directo", ElGestoAprendidoNoSeEnsaya);
        Prueba("83. el doble solo se ensaya sobre contenido: un botón jamás recibe un segundo clic", ElBotonNoRecibeSegundoClic);

        Console.WriteLine();
        Console.WriteLine(_fallos == 0
            ? "CONTRATO INTACTO: el grafo se comporta como el día que se congeló."
            : $"CONTRATO ROTO: {_fallos} promesa(s) incumplida(s). El cambio no puede entrar así.");
        return _fallos;
    }

    // ── Las promesas ─────────────────────────────────────────────────────────



    /// <remarks>
    /// EL HUECO QUE DEJABA A SAP FUERA DEL TERRENO (T1 del plan terreno-profundo, 2026-08-25):
    /// `MapaVivo` observaba SIEMPRE con el lector UIA, y dentro de una ventana SAP el sistema
    /// operativo ve un Pane opaco — el grafo aprendía 12 elementos del marco y ninguno de la
    /// sesión. SAP tiene su propia puerta (la Scripting API) y su propio vocabulario de identidad
    /// (`sap:wnd[0]/…`), ya construidos y probados en este repo. Lo que faltaba era el DESPACHO:
    /// que el sentido mire por la puerta del mundo en el que está.
    ///
    /// La traducción al núcleo también se juzga aquí, porque es donde se decide qué es PUERTA:
    /// lo interactivo entra con su Id envuelto como selector `sap:`; el decorado (GuiLabel) no
    /// entra — un rótulo no se pulsa—; y el campo de comandos (GuiOkCodeField) entra CON NOMBRE
    /// aunque SAP no le ponga etiqueta, porque es la puerta a cualquier transacción.
    /// </remarks>
    private static void CadaMundoSeObservaPorSuPuerta()
    {
        // El despacho: la ubicación decide el sentido. Con fakes, que es como se juzga sin pantalla.
        bool leyoUia = false, leyoSap = false;
        var sentido = new SentidoPorMundo(
            uia: () => { leyoUia = true; return new List<Nucleo.Elemento> { new("uia:name=A;ct=Button", "A", "Button") }; },
            sap: () => { leyoSap = true; return new List<Nucleo.Elemento> { new("sap:wnd[0]/tbar[0]/okcd", "comando", "GuiOkCodeField") }; });

        var enSap = sentido.Lee("sapgui://QAS/SESSION_MANAGER/SAPLSMTR_NAVIGATION/0100");
        Debe(leyoSap && !leyoUia, "en una ubicación sapgui:// se mira por la puerta de SAP, no por UIA");
        Debe(enSap.Count == 1 && enSap[0].Selector.StartsWith("sap:", StringComparison.Ordinal),
            "y lo leído llega con la identidad de SAP");

        leyoUia = leyoSap = false;
        sentido.Lee("uia://saplogon.exe/sap-logon-800");
        Debe(leyoUia && !leyoSap,
            "el MARCO de SAP Logon sigue siendo una ventana normal: ahí se mira por UIA como siempre");

        // La traducción: qué entra al núcleo desde lo que la Scripting API devuelve.
        var vistos = new[]
        {
            new U.Graph.Surfaces.SapVisualElement("wnd[0]/usr/btnBUSCAR", "GuiButton", "", "Buscar", null,
                0, 0, 10, 10, true, "click", "button", false, null),
            new U.Graph.Surfaces.SapVisualElement("wnd[0]/usr/txtPACIENTE", "GuiTextField", "", "Paciente", "",
                0, 0, 10, 10, true, "input", "text", false, null),
            new U.Graph.Surfaces.SapVisualElement("wnd[0]/usr/lblTITULO", "GuiLabel", "", "Datos del ingreso", null,
                0, 0, 10, 10, true, "input", "text", false, null),
            new U.Graph.Surfaces.SapVisualElement("wnd[0]/tbar[0]/okcd", "GuiOkCodeField", "", "GuiOkCodeField", "",
                0, 0, 10, 10, true, "input", "text", false, null),
        };
        var elementos = SentidoSap.Traducir(vistos);

        Debe(elementos.Any(e => e.Selector == "sap:wnd[0]/usr/btnBUSCAR" && e.Etiqueta == "Buscar"),
            "un botón entra como puerta, con su Id envuelto en el vocabulario sap:");
        Debe(elementos.Any(e => e.Selector == "sap:wnd[0]/usr/txtPACIENTE"),
            "un campo entra: es donde luego se escribe por identidad");
        Debe(!elementos.Any(e => e.Selector.Contains("lblTITULO")),
            "un rótulo NO entra: un GuiLabel no se pulsa, y ofrecerlo sería la basura de la web otra vez");
        var okcd = elementos.FirstOrDefault(e => e.Selector == "sap:wnd[0]/tbar[0]/okcd");
        Debe(okcd != null && okcd.Etiqueta == "comando",
            "el campo de comandos entra CON NOMBRE aunque SAP no lo etiquete: es la puerta a cualquier transacción");
    }

    /// <remarks>
    /// LA OTRA MITAD DEL DESPACHO: pulsar. La mano UIA no puede tocar un control SAP (el Pane
    /// opaco otra vez), y mandarle un selector `sap:` sería pedirle a Windows algo que no ve —
    /// fallaría en silencio o, peor, acertaría sobre otra cosa. El SELECTOR decide la mano, porque
    /// el selector ES la identidad y lleva escrito de qué mundo viene (`SapSelector.Owns`). La
    /// misma regla de la casa dicha al revés: nunca por coordenadas, siempre por identidad — y la
    /// identidad sabe quién la entiende.
    /// </remarks>
    private static void CadaMundoSePulsaPorSuMano()
    {
        var pulsadas = new List<string>();
        var mano = new ManoPorMundo(
            uia: (sel, etq) => { pulsadas.Add("uia→" + sel); return true; },
            sap: (sel, etq) => { pulsadas.Add("sap→" + sel); return true; });

        Debe(mano.Pulsa("sap:wnd[0]/usr/btnBUSCAR", "Buscar"),
            "un selector sap: se pulsa");
        Debe(pulsadas.Count == 1 && pulsadas[0] == "sap→sap:wnd[0]/usr/btnBUSCAR",
            "…por la mano de SAP, que es la única que ve dentro de la sesión");

        Debe(mano.Pulsa("uia:name=Aceptar;ct=Button", "Aceptar"),
            "un selector uia: se pulsa");
        Debe(pulsadas.Count == 2 && pulsadas[1] == "uia→uia:name=Aceptar;ct=Button",
            "…por la mano UIA de siempre: el despacho no cambia el camino de nadie más");

        // Los selectores con fragmento (fila de árbol, botón de toolbar, fila de ALV) son de SAP
        // aunque lleven cola: el vocabulario los reconoce enteros.
        mano.Pulsa("sap:wnd[0]/shellcont/shell#node=vw00073", "Órdenes Clínicas");
        Debe(pulsadas.Count == 3 && pulsadas[2].StartsWith("sap→", StringComparison.Ordinal),
            "una fila de árbol —selector con fragmento— también va por la mano de SAP");
    }

    /// <remarks>
    /// EL TECHO DE PROFUNDIDAD, MEDIDO CONTRA EL SAP REAL (2026-08-26, sesión QAS/NWP1 viva): el
    /// sentido de SAP entró al terreno y trajo 12 puertas… todas de la barra de herramientas
    /// («Atrás», «Continuar», «comando»). El recorrido del árbol de componentes explicó por qué:
    /// TODO el contenido de esa pantalla son dos `GuiShell[Tree]` bajo un splitter, y un árbol no
    /// se pulsa — se pulsa una de sus filas. Sin filas, el terreno tenía la orilla y ningún camino
    /// tierra adentro.
    ///
    /// SOLO LAS VISIBLES, y eso no es una limitación: es la bandera Vivo diciendo lo mismo de
    /// siempre. Un árbol clínico trae 1197 claves cargadas del servidor; ofrecerlas todas como
    /// puertas sería prometer pantalla para lo que solo es memoria —y ahogar la respuesta, que es
    /// justo el daño que ya hizo la basura de la web (promesa 65)—. `VisibleTreeRows` filtra por
    /// geometría: las que están en pantalla AHORA.
    /// </remarks>
    private static void LasFilasDelArbolSonPuertas()
    {
        const string arbol = "wnd[0]/shellcont/shellcont/shell/shellcont[0]/shell";
        var vistos = new[]
        {
            // El árbol mismo: se ve, ocupa sitio, y NO es una puerta.
            new U.Graph.Surfaces.SapVisualElement(arbol, "GuiShell", "Tree", "Área de trabajo", null,
                0, 0, 300, 400, true, "click", "text", false, null),
            new U.Graph.Surfaces.SapVisualElement("wnd[0]/tbar[0]/okcd", "GuiOkCodeField", "", "GuiOkCodeField", "",
                0, 0, 10, 10, true, "input", "text", false, null),
        };
        var filas = new Dictionary<string, IReadOnlyList<U.Graph.Surfaces.SapGuiSurface.TreeRow>>
        {
            [arbol] = new[]
            {
                new U.Graph.Surfaces.SapGuiSurface.TreeRow("vw00073", "Órdenes Clínicas", 10, 16, false),
                new U.Graph.Surfaces.SapGuiSurface.TreeRow("vw00081", "Favoritos", 26, 16, true),
            },
        };

        var elementos = SentidoSap.Traducir(vistos, filas);

        Debe(!elementos.Any(e => e.Selector == "sap:" + arbol),
            "el árbol NO entra como puerta: un árbol no se pulsa");

        var orden = elementos.FirstOrDefault(e => e.Etiqueta == "Órdenes Clínicas");
        Debe(orden != null, "una fila visible SÍ entra: es contenido navegable, y es lo único que hay aquí");
        Debe(orden != null && orden.Selector == "sap:" + arbol + "#node=vw00073",
            "…con la identidad entera —árbol MÁS clave—, porque la fila sola no resuelve por FindById");

        Debe(elementos.Any(e => e.Etiqueta == "Favoritos"),
            "una carpeta también es puerta: desplegarla es navegar");
        Debe(elementos.Any(e => e.Selector == "sap:wnd[0]/tbar[0]/okcd"),
            "y lo de siempre sigue entrando: las filas se SUMAN, no sustituyen");

        // Sin árboles en pantalla, la traducción es la de antes: nada cambia para el resto.
        Debe(SentidoSap.Traducir(vistos).Count == 1,
            "sin filas que pasar, solo entra lo interactivo de siempre");
    }

    /// <remarks>
    /// LA TERCERA PATA DEL DESPACHO, y la exigió una prueba real (2026-08-26): el batch
    /// [«comando» → escribir «NWP1» → «Continuar»] en Easy Access paró en el paso 2 con «no pude
    /// escribir». El pulsar ya despachaba por mundo; el escribir seguía yendo SIEMPRE por
    /// map_type —teclear por UIA hacia el foco de Windows—, y dentro de SAP eso es mandar letras
    /// al aire. La Scripting API deja hacer lo honesto: ponerle el texto AL CAMPO por su identidad
    /// (.Text) y releerlo para comprobar que quedó.
    ///
    /// El mismo patrón que el sentido: la UBICACIÓN decide el lápiz, con fakes se juzga la
    /// decisión, y nadie aguas arriba —batch, compuerta, MCP— sabe en qué mundo escribe.
    /// </remarks>
    private static void CadaMundoSeEscribePorSuLapiz()
    {
        var escrito = new List<string>();
        string donde = "sapgui://QAS/SESSION_MANAGER/SAPLSMTR_NAVIGATION/0100";
        var lapiz = new EscribirPorMundo(
            donde: () => donde,
            uia: texto => { escrito.Add("uia→" + texto); return true; },
            sap: texto => { escrito.Add("sap→" + texto); return true; });

        Debe(lapiz.Escribe("NWP1"), "en una sesión SAP se puede escribir");
        Debe(escrito.Count == 1 && escrito[0] == "sap→NWP1",
            "…y va por el lápiz de SAP: al campo por su identidad, no al aire");

        donde = "uia://notepad.exe/sin-titulo";
        Debe(lapiz.Escribe("hola"), "fuera de SAP también");
        Debe(escrito.Count == 2 && escrito[1] == "uia→hola",
            "…por el camino de siempre: el despacho no cambia a nadie más");
    }

    /// <remarks>
    /// LO DESTAPÓ EL PILOTO con dos ventanas SAP abiertas (2026-08-26): una sesión buena
    /// (GCALDERO, mandante 300) y un login paralelo. `Session()` tomaba «la primera sesión de la
    /// primera conexión», así que el localizador acuñó «sapgui://QAS/S000/SAPMSYST/0020» —la
    /// pantalla de login— mientras la ventana de delante era otra. Una identidad que describe OTRA
    /// ventana es la mentira más desorientadora posible: todo lo demás (compuerta, batch, aristas)
    /// se apoya en ella.
    ///
    /// La regla: la sesión cuya ventana está DELANTE. Sin casar y con UNA sola sesión, esa (el
    /// caso de siempre, y las sondas de fondo siguen funcionando); sin casar y con VARIAS, ninguna
    /// — «no sé» es mejor que la identidad de otra ventana.
    /// </remarks>
    private static void LaSesionEsLaDeDelante()
    {
        Debe(U.Graph.Surfaces.CualSesion.Elige(new long[] { 111, 222, 333 }, delante: 222) == 1,
            "con varias sesiones, manda la que tiene su ventana delante");
        Debe(U.Graph.Surfaces.CualSesion.Elige(new long[] { 111 }, delante: 999) == 0,
            "con UNA sola sesión y el foco en otra parte, esa: es el caso de siempre y las sondas de fondo viven de él");
        Debe(U.Graph.Surfaces.CualSesion.Elige(new long[] { 111, 222 }, delante: 999) == -1,
            "con varias y ninguna delante, NINGUNA: mejor «no sé» que la identidad de otra ventana");
        Debe(U.Graph.Surfaces.CualSesion.Elige(Array.Empty<long>(), delante: 111) == -1,
            "sin sesiones no hay nada que elegir");
    }

    /// <summary>El terreno de tres pantallas SAP para juzgar la consulta por delante.</summary>
    /// <remarks>
    /// La forma del caso real (QAS/NWP1, 2026-08-26): un menú con puertas cruzadas y sin cruzar,
    /// y detrás de cada cruzada una pantalla cuyos elementos el grafo RECUERDA aunque no estemos
    /// allí. Eso es lo que la profundidad explota: `_vistos` de sitios donde no estás.
    /// </remarks>
    private static Nucleo.Grafo TerrenoDeTres()
    {
        var g = new Nucleo.Grafo();
        const string menu = "sapgui://QAS/NWP1/FRAME/0100";
        const string censo = "sapgui://QAS/NWP1/FRAME/0100/ssubCENSO";
        const string triage = "sapgui://QAS/NWP1/FRAME/0100/ssubCENSO/subTRIAGE";

        g.Estoy(menu);
        g.Observar(menu, new[]
        {
            new Nucleo.Elemento("sap:shell#node=vw1", "Censo Pacientes", "GuiTreeFila"),
            new Nucleo.Elemento("sap:shell#node=vw2", "Cirugías Avaladas", "GuiTreeFila"),
            new Nucleo.Elemento("sap:wnd[0]/tbar[0]/okcd", "comando", "GuiOkCodeField"),
        });
        g.Cruzar(menu, "sap:shell#node=vw1", censo);

        g.Observar(censo, new[]
        {
            new Nucleo.Elemento("sap:usr/btnTRIAGE", "Crear Triage", "GuiButton"),
            new Nucleo.Elemento("sap:usr/txtPACIENTE", "Paciente", "GuiTextField"),
        });
        g.Cruzar(censo, "sap:usr/btnTRIAGE", triage);

        g.Observar(triage, new[] { new Nucleo.Elemento("sap:usr/btnGRABAR", "Grabar", "GuiButton") });

        // De vuelta al menú: lo de allí es MEMORIA ahora, no pantalla.
        g.Estoy(menu);
        return g;
    }

    /// <remarks>
    /// LA NOVEDAD DE LA PROFUNDIDAD (T3 del plan terreno-profundo): el grafo YA recuerda qué hay
    /// en pantallas donde no estamos —`_vistos` por ubicación— y cada cruce sabe su destino. Lo
    /// que faltaba era la PREGUNTA: «¿qué habrá tras esta puerta?». Con la respuesta, el modelo
    /// planifica batches que atraviesan pantallas que aún no ve — y la compuerta de vida sigue
    /// mandando en ejecución: la predicción propone, el terreno vivo dispone.
    /// </remarks>
    private static void ElTerrenoPorDelanteSeCuenta()
    {
        var g = TerrenoDeTres();
        var t = new TerrenoPorDelante(g);

        string desde0 = t.Cuenta("sapgui://QAS/NWP1/FRAME/0100", "", 1);
        Debe(desde0.Contains("Censo Pacientes") && desde0.Contains("ssubCENSO"),
            "sin puerta concreta, cuenta las cruzadas de aquí y a dónde llevan");

        string tras = t.Cuenta("sapgui://QAS/NWP1/FRAME/0100", "Censo Pacientes", 2);
        Debe(tras.Contains("ssubCENSO"),
            "tras la puerta nombra el destino aprendido");
        Debe(tras.Contains("Crear Triage") && tras.Contains("Paciente"),
            "…y lo que RECUERDA allí, que es la predicción que el batch necesita");
        Debe(tras.Contains("subTRIAGE") && tras.Contains("Grabar"),
            "…y con niveles de sobra, sigue por las cruzadas de allí: profundidad 2 real");

        Debe(t.Cuenta("sapgui://QAS/NWP1/FRAME/0100", "Censo Pacientes", 1).Contains("Crear Triage") == true
             && !t.Cuenta("sapgui://QAS/NWP1/FRAME/0100", "Censo Pacientes", 1).Contains("Grabar"),
            "el nivel pedido es un tope de verdad: a 1 nivel no se asoma al triage");
    }

    /// <remarks>
    /// LAS DOS MENTIRAS QUE ESTA CONSULTA PODRÍA DECIR, prohibidas de nacimiento: prometer destino
    /// para una puerta que nadie cruzó (el grafo «no se inventa nada» — regla del núcleo), y
    /// ahogar la respuesta en cien puertas (la basura de la web, promesa 65; y la regla 8 del
    /// génesis: respuestas cortas — el SDK manda a archivo lo que pasa de 25k tokens y el modelo
    /// pierde el hilo).
    /// </remarks>
    private static void ElTerrenoNoInventa()
    {
        var g = TerrenoDeTres();
        var t = new TerrenoPorDelante(g);

        string porDescubrir = t.Cuenta("sapgui://QAS/NWP1/FRAME/0100", "Cirugías Avaladas", 2);
        Debe(porDescubrir.Contains("por descubrir"),
            "una puerta sin cruzar se anuncia como por descubrir, con sus palabras");
        Debe(!porDescubrir.Contains("ssub"),
            "…y NO se le inventa ningún destino");

        Debe(t.Cuenta("sapgui://QAS/NWP1/FRAME/0100", "comando", 2).Contains("por descubrir"),
            "el campo de comandos también: puerta a cualquier parte, destino de ninguna hasta cruzarla");

        // Una pantalla con 30 puertas recordadas no se vuelca entera.
        var lleno = new Nucleo.Grafo();
        lleno.Estoy("a://x");
        lleno.Observar("a://x", new[] { new Nucleo.Elemento("s0", "puerta", "Button") });
        lleno.Cruzar("a://x", "s0", "a://y");
        lleno.Observar("a://y", Enumerable.Range(0, 30)
            .Select(i => new Nucleo.Elemento($"sy{i}", $"puerta {i:D2}", "Button")).ToList());
        lleno.Estoy("a://x");
        string corto = new TerrenoPorDelante(lleno).Cuenta("a://x", "puerta", 1);
        Debe(corto.Contains("más") && !corto.Contains("puerta 29"),
            "pasadas ~12 puertas se dice «y N más», no se vuelca el inventario");

        Debe(new TerrenoPorDelante(g).Cuenta("sapgui://QAS/NWP1/FRAME/0100", "no-existe", 1)
                .Contains("no"),
            "una puerta que no está ni en memoria se dice, no se adivina");
    }

    /// <remarks>
    /// T2 DEL PLAN: la misma pregunta del terreno por delante, contestada en DATOS para que el
    /// visor la pinte — vivo en trazo lleno, recordado punteado, que es la distinción que el
    /// núcleo ya hace y el dibujo solo repite. El visor no lee al pintor ni a Neo4j para esto:
    /// lee el grafo por el 8792, la fuente sin proyección de por medio.
    /// </remarks>
    private static void ElArbolDelVisor()
    {
        var g = TerrenoDeTres();   // estamos en el menú; lo del censo es memoria
        var raiz = TerrenoParaElVisor.Arbol(g, "sapgui://QAS/NWP1/FRAME/0100", 2);

        Debe(raiz.Puertas.Any(p => p.Etiqueta == "Censo Pacientes" && p.Vivo),
            "lo vivo de aquí llega marcado vivo");
        var censo = raiz.Puertas.First(p => p.Etiqueta == "Censo Pacientes");
        Debe(censo.Destino.EndsWith("ssubCENSO", StringComparison.Ordinal),
            "una puerta cruzada lleva su destino");
        Debe(raiz.Puertas.Any(p => p.Etiqueta == "comando" && p.Destino.Length == 0),
            "una puerta sin cruzar va SIN destino: el árbol tampoco inventa");

        var dentro = raiz.Dentro.FirstOrDefault(d => d.Id.EndsWith("ssubCENSO", StringComparison.Ordinal));
        Debe(dentro != null, "detrás de la cruzada viene la pantalla recordada");
        Debe(dentro != null && dentro.Puertas.Any(p => p.Etiqueta == "Crear Triage" && !p.Vivo),
            "…y lo de allí llega como RECORDADO (no vivo): no estamos allí");
        Debe(dentro != null && dentro.Dentro.Any(d2 => d2.Id.EndsWith("subTRIAGE", StringComparison.Ordinal)),
            "la profundidad sigue por las cruzadas de allí");

        Debe(TerrenoParaElVisor.Arbol(g, "sapgui://QAS/NWP1/FRAME/0100", 1).Dentro
                .First(d => d.Id.EndsWith("ssubCENSO", StringComparison.Ordinal)).Dentro.Count == 0,
            "el tope de niveles corta de verdad");

        // NI UNA PUERTA OCULTA. Lo pidió José David mirando su NWP1 (2026-08-30): la pestaña decía
        // «…y 10 más» y esa frase, en un visor, no es un resumen — es una pregunta sin contestar.
        // El recorte tenía sentido en la respuesta AL MODELO, donde el tamaño cuesta tokens; aquí
        // el lienzo crece y la página hace scroll, así que ocultar solo esconde terreno.
        var muchas = new Nucleo.Grafo();
        muchas.Estoy("a://x");
        muchas.Observar("a://x", Enumerable.Range(0, 40)
            .Select(i => new Nucleo.Elemento($"s{i:D2}", $"puerta {i:D2}", "Button")).ToList());
        var todas = TerrenoParaElVisor.Arbol(muchas, "a://x", 1);
        Debe(todas.Puertas.Count == 40, "el visor recibe TODAS las puertas, sean 3 o 40");
        Debe(todas.Puertas.Any(q => q.Etiqueta == "puerta 39"),
            "…incluida la última: nada se queda fuera del lienzo");
    }

    /// <remarks>
    /// LO QUE EL BATCH CONTESTÓ SE PUEDE VOLVER A MIRAR. Hasta ahora el relato de cada tanda vivía
    /// solo en la respuesta MCP y en el log — el visor no tenía de dónde pintarlo. Un anillo corto:
    /// lo último manda, lo viejo se cae, y no crece sin tope (un visor que pagina historia es un
    /// archivo, no un pulso).
    /// </remarks>
    private static void ElRastroDeLosBatches()
    {
        var r = new RastroDeBatches(tope: 3);
        r.Agrega("hice 1 de 1: A");
        r.Agrega("hice 2 de 2: B");
        Debe(r.Ultimas().Count == 2 && r.Ultimas()[0].Cuenta.EndsWith(": B", StringComparison.Ordinal),
            "lo más reciente sale primero");

        r.Agrega("hice 0 de 3: C");
        r.Agrega("hice 3 de 3: D");
        Debe(r.Ultimas().Count == 3, "el anillo respeta su tope");
        Debe(!r.Ultimas().Any(c => c.Cuenta.EndsWith(": A", StringComparison.Ordinal)),
            "…y lo que se cae es LO MÁS VIEJO");
        Debe(r.Ultimas()[0].Cuenta.EndsWith(": D", StringComparison.Ordinal),
            "el último batch es el primero de la lista");
    }

    /// <remarks>
    /// LO ENCONTRÓ JOSÉ DAVID EN LA PRIMERA RONDA DE T4 (2026-08-30): hizo el recorrido del triage
    /// A MANO —nwp1, el árbol, Triage, el paciente— y al volver, sus puertas seguían «por
    /// descubrir». No leyó mal: sus PANTALLAS entraron al terreno (Observar), pero sus CRUCES no
    /// dejaron arista, porque la atribución del clic humano nombra lo clicado con UIA — y dentro
    /// de SAP, UIA ve un Pane sin etiquetas. «Salto SIN atribuir», cada vez.
    ///
    /// SAP sabe decir qué se clicó (findByPosition, y en un árbol la fila clicada ES la
    /// seleccionada). Esta promesa juzga el NOMBRADO —la parte pura—: las mismas vallas que el
    /// camino UIA (sin etiqueta no hay paso), y la identidad entera para las filas (árbol MÁS
    /// clave, como la promesa 70). El casado contra lo observado sigue siendo de
    /// AQuienSeLeDioClic, con sus vallas de ambigüedad: dos «Consultas» → no se atribuye.
    /// </remarks>
    private static void ElClicHumanoEnSapEnsena()
    {
        var boton = AtribucionSap.NombraElClic("wnd[0]/tbar[1]/btn[19]", "GuiButton", "Otro menú", nodo: null);
        Debe(boton != null && boton.Value.Etiqueta == "Otro menú" && boton.Value.Tipo == "GuiButton",
            "un botón clicado se nombra con su etiqueta y tipo de SAP");
        Debe(boton != null && boton.Value.Selector == "sap:wnd[0]/tbar[1]/btn[19]",
            "…y con su Id envuelto en el vocabulario sap:");

        var fila = AtribucionSap.NombraElClic("wnd[0]/shellcont/shell", "GuiShell", "Tree",
            nodo: ("vw00576", "Triage"));
        Debe(fila != null && fila.Value.Etiqueta == "Triage" && fila.Value.Tipo == "GuiTreeFila",
            "un clic en el árbol se nombra por la FILA seleccionada, no por el árbol");
        Debe(fila != null && fila.Value.Selector == "sap:wnd[0]/shellcont/shell#node=vw00576",
            "…con la identidad entera: árbol MÁS clave (promesa 70)");

        Debe(AtribucionSap.NombraElClic("wnd[0]/usr/lbl", "GuiLabel", "", nodo: null) == null,
            "sin etiqueta no hay paso: la misma valla que el camino UIA");
        Debe(AtribucionSap.NombraElClic("", "GuiButton", "Continuar", nodo: null) == null,
            "sin Id no hay identidad, y sin identidad no se atribuye nada");

        // EL PUNTO CIEGO DEL CAMBIO DE SELECCIÓN (ronda 3, 2026-08-30): el clic del usuario en
        // «Triage» cayó en la fila YA seleccionada de su visita anterior — sin cambio, sin nombre.
        // La geometría lo resuelve: si el punto del clic cae dentro del rectángulo de la fila,
        // esa fila ES el clic, cambie o no cambie la selección.
        var filasConCaja = new (string Key, string Text, int Top, int Height)[]
        {
            ("vw00722", "Triage", 90, 30), ("vw00723", "Consulta", 120, 30),
        };
        Debe(AtribucionSap.FilaEnElPunto(yLocal: 105, filasConCaja) is { } f1 && f1.Key == "vw00722",
            "el punto dentro del rectángulo nombra la fila, aunque ya estuviera seleccionada");
        Debe(AtribucionSap.FilaEnElPunto(yLocal: 121, filasConCaja) is { } f2 && f2.Key == "vw00723",
            "…y el límite entre filas respeta a la de abajo");
        Debe(AtribucionSap.FilaEnElPunto(yLocal: 400, filasConCaja) == null,
            "un punto fuera de toda fila no nombra nada: mejor mudo que equivocado");

        // FILA Y CARPETA NO SON EL MISMO TIPO (ronda 4, 2026-08-30): el clic en «Favoritos» se
        // nombró GuiTreeFila, el observador la había escrito GuiTreeCarpeta, y el juez —que exige
        // tipo exacto— rechazó una puerta que existía.
        var carpeta = AtribucionSap.NombraElClic("wnd[0]/shell", "GuiShell", "Tree",
            nodo: ("Favo", "Favoritos"), esCarpeta: true);
        Debe(carpeta != null && carpeta.Value.Tipo == "GuiTreeCarpeta",
            "una carpeta clicada se nombra carpeta: el juez exige el tipo exacto y hay que dárselo");
    }

    /// <remarks>
    /// LA OBSERVACIÓN 3 DE JOSÉ DAVID, confirmada a cuatro ojos (2026-08-30): en la pantalla del
    /// Triage el inspector pintaba el panel derecho como UNA caja ámbar —«shell · GridView · sin
    /// mapear»— y el terreno no tenía ni un botón de allí. Los botones («Triage», «Pasar a
    /// Consulta»…) y la fila del paciente viven DENTRO del control ALV: no son GuiComponents, el
    /// recorrido no los ve. Sondeada la rejilla viva: 12 botones con id propio (ZMEDTRIAGE, APPST…)
    /// y las filas con sus 23 columnas — todo legible, nada era puerta.
    ///
    /// La fila entra por PARES columna=valor, no por índice (la regla del vocabulario, SapSelector
    /// .RowMark): «la fila 0» es una posición y mañana es otro paciente; los pares dicen a QUIÉN.
    /// </remarks>
    private static void LaRejillaEntraAlTerreno()
    {
        const string rejilla = "wnd[0]/usr/ssubVIEW_SCREEN:SAPLN1LSTAMB:0007/cntlISH_VIEW_007/shellcont/shell";
        var rejillas = new[]
        {
            new SentidoSap.RejillaVista(rejilla,
                Botones: new[] { ("ZMEDTRIAGE", "Triage"), ("APPST", "Pasar a Consulta"), ("", "sin id") },
                Filas: new[] { ("FALNR=2394346|PATNNAME=GIRALDO", "GIRALDO HERNAN · 2394346") }),
        };
        var elementos = SentidoSap.Traducir(
            Array.Empty<U.Graph.Surfaces.SapVisualElement>(), filasPorArbol: null, rejillas);

        var triage = elementos.FirstOrDefault(e => e.Etiqueta == "Triage");
        Debe(triage != null, "un botón de la toolbar de la rejilla es una puerta");
        Debe(triage != null && triage.Selector == "sap:" + rejilla + "#tbbtn=ZMEDTRIAGE",
            "…con su identidad entera: rejilla MÁS id de botón, que es como se pulsa sin coordenadas");

        var fila = elementos.FirstOrDefault(e => e.Etiqueta.Contains("GIRALDO"));
        Debe(fila != null, "una fila visible de la rejilla es una puerta: es el contenido con el que se trabaja");
        Debe(fila != null && fila.Selector == "sap:" + rejilla + "#row=FALNR=2394346|PATNNAME=GIRALDO",
            "…identificada por PARES columna=valor, nunca por índice: los pares dicen a QUIÉN se señala");

        Debe(!elementos.Any(e => e.Etiqueta == "sin id"),
            "un botón sin id no entra: sin identidad no hay puerta");
    }

    /// <remarks>
    /// EL BLOQUEO DE LA RONDA 3 (2026-08-30): el usuario clicó «Consulta» y luego «Triage», el
    /// resolver nombró los dos clics… y no se aprendió nada, porque NO HUBO SALTO: ambas vistas
    /// son el visor genérico de listas (ssubVIEW_SCREEN:SAPLN1LSTAMB:0007) y compartían identidad.
    /// El propio remark de Identity lo anticipó: «si algún día dos paneles distintos resultan
    /// indistinguibles sin subdynpro, se extiende AQUÍ». Llegó el día.
    ///
    /// El discriminador correcto es semántico: en el Puesto de trabajo, la fila seleccionada del
    /// árbol de navegación ES la vista que la pantalla muestra — cambiarla ES navegar. Fuera del
    /// patrón del Puesto (subdynpros normales) NO se toca nada: en Easy Access la selección cambia
    /// sin navegar, y una identidad que aletea con cada clic sería peor que una gruesa.
    /// </remarks>
    private static void LaVistaEsElLugar()
    {
        Debe(U.Graph.Surfaces.LaVistaEsElLugarDelPuesto.Sufijo(
                "ssubVIEW_SCREEN:SAPLN1LSTAMB:0007", "Triage") == "vista:Triage",
            "en el visor genérico del Puesto, la vista elegida entra al lugar");
        Debe(U.Graph.Surfaces.LaVistaEsElLugarDelPuesto.Sufijo(
                "ssubVIEW_SCREEN:SAPLN1LSTAMB:0007", "Consulta") == "vista:Consulta",
            "…y otra vista es OTRO lugar: eso es lo que faltaba para que el salto exista");
        Debe(U.Graph.Surfaces.LaVistaEsElLugarDelPuesto.Sufijo(
                "subPATEINST:SAPLNCHD:2000", "Triage") == "",
            "fuera del patrón del Puesto no se toca nada: un subdynpro normal ya distingue solo");
        Debe(U.Graph.Surfaces.LaVistaEsElLugarDelPuesto.Sufijo(
                "ssubVIEW_SCREEN:SAPLN1LSTAMB:0007", "  ") == "",
            "sin selección legible no se añade nada: una identidad que aletea es peor que una gruesa");
        Debe(U.Graph.Surfaces.LaVistaEsElLugarDelPuesto.Sufijo(
                "ssubVIEW_SCREEN:SAPLN1LSTAMB:0007", "Censo / Pacientes") == "vista:Censo Pacientes",
            "la barra se limpia: el separador de la identidad no puede venir dentro del nombre");
    }

    /// <remarks>
    /// EL ATASCO DE LA CORRIDA COMPLETA (2026-08-30): tras el relogin, el árbol de NWP1 quedó
    /// arriba del todo y «Triage» —conocida, cruzada, con destino— quedó fuera de la vista. La
    /// compuerta contestó «lo conozco pero AHORA no lo veo» y el piloto se quedó dando vueltas
    /// (el scroll genérico mueve otro panel). Pero en SAP una clave CARGADA se alcanza por
    /// identidad: seleccionarla LA TRAE a la vista — no es pulsar de memoria a ciegas, y la
    /// verificación por consecuencia sigue juzgando el resultado.
    ///
    /// El batch NO sabe de SAP (regla del despacho): recibe un delegado que dice qué selectores
    /// son accionables sin verse, y quién lo cablea decide (hoy: filas de árbol sap:…#node=).
    /// Sin delegado, la compuerta muerde como siempre — la promesa 15 sigue intacta para todo lo
    /// demás.
    /// </remarks>
    private static void LaFilaDesplazadaSeAlcanza()
    {
        // «Triage» se observó una vez (con su clave de árbol) y ahora está desplazada: recordada,
        // no viva. El mundo falso la deja cruzar igual — como SAP.
        Nucleo.Grafo Mundo()
        {
            var g = new Nucleo.Grafo();
            g.Observar("sapgui://q/n", new[]
            {
                new Nucleo.Elemento("sap:shell#node=vw1", "Triage", "GuiTreeFila"),
                new Nucleo.Elemento("sap:b", "Otro", "GuiButton"),
            });
            g.Observar("sapgui://q/n", new[] { new Nucleo.Elemento("sap:b", "Otro", "GuiButton") });
            return g;
        }
        var rutas = new Dictionary<string, string> { ["sapgui://q/n|sap:shell#node=vw1"] = "sapgui://q/n/vista" };

        var (batch, donde, tocados) = BatchCon(Mundo(), "sapgui://q/n", rutas);
        var sinDelegado = batch.Recorre(new[] { new RecorrerSegunElNucleo.Paso("Triage") });
        Debe(sinDelegado.Hechos == 0 && sinDelegado.Cuenta.Contains("AHORA no lo veo"),
            "sin delegado, la compuerta muerde como siempre: lo desplazado no se promete");

        var g2 = Mundo();
        string donde2 = "sapgui://q/n";
        var pulsar2 = new PulsarSegunElNucleo(g2, () => donde2,
            (sel, et) => { if (rutas.TryGetValue(donde2 + "|" + sel, out var alla)) donde2 = alla; return true; })
        { EsperaMaximaMs = 240 };
        var batch2 = new RecorrerSegunElNucleo(g2, () => donde2, pulsar2)
        {
            EsperaMaximaMs = 240,
            AccionableAunSinVerse = sel => sel.Contains("#node=", StringComparison.Ordinal),
        };
        var r = batch2.Recorre(new[] { new RecorrerSegunElNucleo.Paso("Triage") });
        Debe(r.Hechos == 1 && r.Donde == "sapgui://q/n/vista",
            "con el delegado, la fila desplazada SE INTENTA por identidad y la consecuencia manda");

        var r2 = batch2.Recorre(new[] { new RecorrerSegunElNucleo.Paso("Otro paso inexistente") });
        Debe(r2.Hechos == 0 && r2.Cuenta.Contains("no lo conozco"),
            "…y lo desconocido sigue siendo desconocido: el delegado no abre esa puerta");
    }

    /// <remarks>
    /// LO VIO JOSÉ DAVID EN LA RONDA FINAL (2026-08-30): hay un «Triage» bajo «Urgencias Adultos»
    /// y otro bajo «Urgencias Pediatría», y con la hoja sola como nombre el piloto abrió el de
    /// pediatría (vacío) creyendo que era el suyo — y la identidad «vista:Triage» mezclaba los
    /// aprendizajes de ambos. El propio archivo ya sabía la lección para otro caso: «"Órdenes
    /// Clínicas" aparece 17 veces, una por servicio; el texto nunca identifica la fila — la RUTA
    /// sí». El sistema debe manejar la estructura de carpetas de SAP: la carpeta es parte del
    /// nombre.
    /// </remarks>
    private static void LaCarpetaEsParteDelNombre()
    {
        const string arbol = "wnd[0]/shellcont/shell";
        var filas = new Dictionary<string, IReadOnlyList<U.Graph.Surfaces.SapGuiSurface.TreeRow>>
        {
            [arbol] = new[]
            {
                new U.Graph.Surfaces.SapGuiSurface.TreeRow("vw00722", "Triage", 10, 16, false,
                    Ruta: "Urgencias Adultos/Triage"),
                new U.Graph.Surfaces.SapGuiSurface.TreeRow("vw00736", "Triage", 40, 16, false,
                    Ruta: "Urgencias Pediatría/Triage"),
                new U.Graph.Surfaces.SapGuiSurface.TreeRow("vw00001", "Favoritos", 70, 16, true),
            },
        };
        var elementos = SentidoSap.Traducir(Array.Empty<U.Graph.Surfaces.SapVisualElement>(), filas, null);

        Debe(elementos.Any(e => e.Etiqueta == "Urgencias Adultos/Triage"
                             && e.Selector == "sap:" + arbol + "#node=vw00722"),
            "el Triage de adultos se llama con su carpeta, y conserva su clave");
        Debe(elementos.Any(e => e.Etiqueta == "Urgencias Pediatría/Triage"
                             && e.Selector == "sap:" + arbol + "#node=vw00736"),
            "…y el de pediatría con la suya: dos puertas DISTINGUIBLES, que era todo el problema");
        Debe(elementos.Any(e => e.Etiqueta == "Favoritos"),
            "sin ruta que contar, la hoja se llama como siempre: nada más cambia");
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

    private static void FrenoOciosoNoSeArma()
    {
        Freno.Termine();                       // nada en marcha
        Freno.Pide("prueba");
        Debe(!Freno.Pidieron,
            "un alto pedido sin nada en marcha NO deja el freno armado; si lo dejara, el siguiente "
            + "trabajo nacería abortado sin que nadie hubiera pedido nada");
    }

    private static void FrenoSeArmaAlEmpezar()
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

    private static void FrenoAvisaUnaSolaVez()
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

    private static void FrenoCortaElSueno()
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

    private static void FrenoSueltaAlTerminar()
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

    private static void FrenoCierraLaPuertaDeEntrada()
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

    private static void FrenoCierraLaPuertaDeUia()
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

    private static void FrenoDevuelveElControlHablando()
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

    private static void SituarseSeparaVivoDeMemoria()
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

    private static void SituarseNoConfundeVacioConSinMirar()
    {
        var g = new Nucleo.Grafo();
        var situarse = new AquiSegunElNucleo(g, () => "uia://falsa.exe/jamas-mirada");
        string r = situarse.Ahora();

        Debe(r.Contains("no he mirado", StringComparison.OrdinalIgnoreCase),
            $"de un sitio sin mirar se dice que no se ha mirado (dijo: «{r}»). «Aquí no hay nada» "
            + "invita a rendirse; «no he mirado» invita a mirar, y solo una de las dos es cierta");
        Debe(!r.Contains("0 salida"), "y NO se cuenta como cero");
    }

    private static void SituarseNoAdivina()
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

    private static void AbrirNoRelanzaLoQueYaEsta()
    {
        var lanzados = new List<string>();
        string r = AbrirCon("uia://chrome.exe/inicio", "uia://chrome.exe/inicio", true, lanzados).Abrir("chrome");

        Debe(lanzados.Count == 0,
            "estando ya delante NO se toca nada: relanzar deja dos ventanas de lo mismo y pierde lo "
            + "que hubiera a medias en la primera");
        Debe(r.Contains("ya estás"), $"y se dice que ya estabas (dijo: «{r}»)");
    }

    private static void AbrirDiceDondeQuedamos()
    {
        var lanzados = new List<string>();
        string r = AbrirCon("uia://chrome.exe/inicio", "uia://notepad.exe/sin-titulo", true, lanzados).Abrir("notepad");

        Debe(lanzados.Count == 1, "no estando delante, sí se abre");
        Debe(r.Contains("uia://notepad.exe/sin-titulo"),
            $"y se contesta con DÓNDE quedamos, mirando DESPUÉS (dijo: «{r}»). Lanzar es una "
            + "petición, no una llegada: un «lo abrí» sin mirar es éxito declarado");
        Debe(!r.Contains("chrome"), "y no con dónde estábamos antes");
    }

    private static void AbrirDiceDondeEstamosAlFallar()
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

    private static void AbrirEncuentraLaAppInstalada()
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

    private static void AbrirNoAdivinaElEmpate()
    {
        // «to do» encaja de verdad con las dos, y no hay forma honesta de saber cuál.
        var r = AbrirSegunElNucleo.Emparejar("to do", Instaladas);
        Debe(r.Count == 2,
            $"con un empate real se devuelven TODAS (devolvió {r.Count}). Elegir por longitud o por "
            + "orden alfabético es acertar la mitad de las veces y equivocarse EN SILENCIO la otra "
            + "mitad, que es peor que preguntar");
    }

    private static void LoSenaladoCaduca()
    {
        var ahora = new DateTime(2026, 8, 23, 15, 0, 0, DateTimeKind.Utc);

        Debe(LoQueSenalas.SigueValiendo(ahora.AddSeconds(-5), ahora),
            "lo señalado hace cinco segundos vale: se señala, se pregunta, se contesta y se pide");
        Debe(!LoQueSenalas.SigueValiendo(ahora.AddMinutes(-10), ahora),
            "lo señalado hace diez minutos NO. Señalar hace que algo sea accionable aunque no esté "
            + "en el mapa de esta pantalla; si eso no caducara, un «púlsalo» dicho mucho después "
            + "actuaría sobre algo que ya no está delante, y con la confianza de haber acertado");
    }

    private static void LaAppInstaladaGanaALaPestana()
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

    private static void SenalarNoExigeElNombreClavado()
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

    // ── RECORRER EN BATCH ────────────────────────────────────────────────────
    //
    // El patrón del computer_batch del Agent SDK sobre nuestro terreno: N pasos por llamada, la
    // compuerta de VIDA antes de cada uno, y parar honesto devolviendo el control. Ver
    // docs/plan-batch-sobre-nodos-vivos.md. Las cuatro promesas son las cuatro formas en que esto
    // puede mentir: pulsar lo que no está, contar lo que no hizo, no aprender lo que cruzó, y
    // seguir cuando le pidieron parar.

    /// <summary>Un mundo de tres pantallas encadenadas, con el dedo falso que mueve el mapa.</summary>
    /// <summary>
    /// Un pulsador cuya mano REGISTRA (etiqueta, gesto) y navega según el gesto que le llegue.
    /// Pide la mano de TRES argumentos por reflexión: mientras no exista, devuelve null y la
    /// promesa falla con su motivo — llamarla directo romperia la compilacion de todo el contrato.
    /// </summary>
    private static (object? Pulsador, Func<string> Donde, Action<string> Volver, List<(string Etiqueta, string Gesto)> Toques) PulsadorConGesto(
        Nucleo.Grafo g, string inicio, Func<string, string, string?> rutaSegunGesto)
    {
        var toques = new List<(string, string)>();
        string donde = inicio;
        // «Volver» simula lo que en la pantalla real hace el usuario o el botón Atrás: el gesto
        // aprendido es DE LA ARISTA (ubicación + selector), así que para pulsar la misma puerta
        // dos veces hay que estar dos veces en el mismo sitio — pulsarla desde el destino sería
        // otra arista, y esa no ha aprendido nada (promesa 4 del núcleo).
        Action<string> volver = a => donde = a;
        var ctor = typeof(PulsarSegunElNucleo).GetConstructors()
            .FirstOrDefault(c => c.GetParameters() is { Length: 3 } p
                && p[2].ParameterType == typeof(Func<string, string, string, bool>));
        if (ctor == null) return (null, () => donde, volver, toques);

        var pulsador = ctor.Invoke(new object[]
        {
            g,
            (Func<string>)(() => donde),
            (Func<string, string, string, bool>)((sel, et, gesto) =>
            {
                toques.Add((et, gesto));
                var alla = rutaSegunGesto(sel, gesto);
                if (alla != null) donde = alla;
                return true;
            }),
        });
        typeof(PulsarSegunElNucleo).GetProperty("EsperaMaximaMs")?.SetValue(pulsador, 240);
        return (pulsador, () => donde, volver, toques);
    }

    private static PulsarSegunElNucleo.Resultado Pulsa(object pulsador, string selector, string etiqueta)
        => (PulsarSegunElNucleo.Resultado)typeof(PulsarSegunElNucleo)
            .GetMethod("Pulsa")!.Invoke(pulsador, new object[] { selector, etiqueta })!;

    private static void ElGestoAprendidoNoSeEnsaya()
    {
        // Una carpeta del Explorador: el clic simple SELECCIONA (no navega), solo el doble abre.
        // La mano falsa reproduce exactamente eso: navega únicamente cuando le llega «doubleclick».
        var g = new Nucleo.Grafo();
        g.Observar("uia://x.exe/docs", new[] { new Nucleo.Elemento("s:carpeta", "specs", "ListItem") });

        var (pulsador, donde, volver, toques) = PulsadorConGesto(g, "uia://x.exe/docs",
            (sel, gesto) => sel == "s:carpeta" && gesto == "doubleclick" ? "uia://x.exe/specs" : null);
        Debe(pulsador != null,
            "todavía no existe la mano con gesto (fase 2 de la spec 003). La promesa está escrita "
            + "y en rojo, que es donde tiene que estar");
        if (pulsador == null) return;

        var r1 = Pulsa(pulsador, "s:carpeta", "specs");
        Debe(r1.CambioLaPantalla && donde() == "uia://x.exe/specs",
            $"la primera vez ABRE: ensaya el clic, no alcanza, sube al doble (quedó en «{donde()}»)");
        Debe(toques.Count == 2 && toques[0].Gesto == "" && toques[1].Gesto == "doubleclick",
            $"y el ensayo es UNA escalera —clic, luego doble— no una ráfaga ({string.Join(" → ", toques.Select(t => $"'{t.Gesto}'"))})");

        // Se vuelve a la lista (como volvería el usuario con Atrás) y se pulsa la MISMA puerta otra
        // vez. Sin volver, la segunda pulsación sería desde «specs» — OTRA arista, que no sabe nada
        // (así falló el primer borrador de esta promesa, y el fallo era del arnés).
        volver("uia://x.exe/docs");
        g.Observar("uia://x.exe/docs", new[] { new Nucleo.Elemento("s:carpeta", "specs", "ListItem") });
        toques.Clear();
        var r2 = Pulsa(pulsador, "s:carpeta", "specs");
        Debe(r2.CambioLaPantalla,
            "la segunda vez también abre");
        Debe(toques.Count == 1 && toques[0].Gesto == "doubleclick",
            $"pero SIN ensayar: un solo toque, directo con el gesto aprendido "
            + $"({toques.Count} toque(s): {string.Join(" → ", toques.Select(t => $"'{t.Gesto}'"))}). "
            + "Ensayar otra vez es pagar el mismo riesgo dos veces por algo que ya se sabe — y el "
            + "clic de más cae sobre la pantalla real");
    }

    private static void ElBotonNoRecibeSegundoClic()
    {
        // «Guardar» hace su trabajo SIN cambiar de pantalla. Escalar ahí es guardar dos veces: el
        // tipo no dice qué gesto hace falta (2026-08-03, Configuración anula con el segundo clic),
        // pero SÍ dice qué es seguro ensayar — y sobre un botón, el doble no lo es.
        var g = new Nucleo.Grafo();
        g.Observar("uia://x.exe/form", new[] { new Nucleo.Elemento("s:guardar", "Guardar", "Button") });

        var (pulsador, _, _, toques) = PulsadorConGesto(g, "uia://x.exe/form", (_, _) => null);
        Debe(pulsador != null,
            "todavía no existe la mano con gesto (fase 2 de la spec 003). La promesa está escrita "
            + "y en rojo, que es donde tiene que estar");
        if (pulsador == null) return;

        var r = Pulsa(pulsador, "s:guardar", "Guardar");
        Debe(r.SePudo && !r.CambioLaPantalla,
            "se pulsó y la pantalla no cambió: un botón de acción hizo su trabajo");
        Debe(toques.Count == 1,
            $"y recibió EXACTAMENTE un clic ({toques.Count} toque(s)): el doble solo se ensaya "
            + "sobre contenido de lista, porque sobre un botón el segundo clic es repetir la acción");
    }

    private static (RecorrerSegunElNucleo Batch, Func<string> Donde, List<string> Tocados) BatchCon(
        Nucleo.Grafo g, string inicio, Dictionary<string, string> rutas, Func<int, bool>? frenoTrasTocar = null)
    {
        string donde = inicio;
        var tocados = new List<string>();
        var pulsar = new PulsarSegunElNucleo(g, () => donde,
            (sel, et) =>
            {
                tocados.Add(et);
                if (rutas.TryGetValue(donde + "|" + sel, out var alla)) donde = alla;
                return true;
            })
        { EsperaMaximaMs = 240 };

        var batch = new RecorrerSegunElNucleo(g, () => donde, pulsar,
            hayQueParar: () => frenoTrasTocar?.Invoke(tocados.Count) ?? false)
        { EsperaMaximaMs = 240 };
        return (batch, () => donde, tocados);
    }

    private static Nucleo.Grafo MundoDeTres()
    {
        var g = new Nucleo.Grafo();
        g.Observar("uia://x.exe/a", new[] { new Nucleo.Elemento("s:1", "Uno", "Button") });
        g.Observar("uia://x.exe/b", new[] { new Nucleo.Elemento("s:2", "Dos", "Button") });
        g.Observar("uia://x.exe/c", new[] { new Nucleo.Elemento("s:3", "Tres", "Button") });
        return g;
    }

    private static readonly Dictionary<string, string> RutasDeTres = new()
    {
        ["uia://x.exe/a|s:1"] = "uia://x.exe/b",
        ["uia://x.exe/b|s:2"] = "uia://x.exe/c",
        ["uia://x.exe/c|s:3"] = "uia://x.exe/d",
    };

    private static void LaCompuertaDelBatchMuerde()
    {
        // «Viejo» se vio aquí una vez y ya no está: recordado, NO vivo. Es exactamente lo que la
        // compuerta existe para no pulsar — pulsar de memoria es pulsar donde ya no hay nada.
        var g = MundoDeTres();
        g.Observar("uia://x.exe/a", new[]
        {
            new Nucleo.Elemento("s:1", "Uno", "Button"),
            new Nucleo.Elemento("s:v", "Viejo", "Button"),
        });
        g.Observar("uia://x.exe/a", new[] { new Nucleo.Elemento("s:1", "Uno", "Button") });

        var (batch, _, tocados) = BatchCon(g, "uia://x.exe/a", RutasDeTres);
        var r = batch.Recorre(new[] { new RecorrerSegunElNucleo.Paso("Viejo"), new RecorrerSegunElNucleo.Paso("Uno") });

        Debe(tocados.Count == 0,
            $"un paso RECORDADO pero no vivo NO se pulsa (se pulsaron {tocados.Count}): pulsar de "
            + "memoria es pulsar donde ya no hay nada, y el clic cae en lo que sea que esté ahí ahora");
        Debe(r.Hechos == 0 && !r.Termino, "y el batch para AHÍ, no salta el paso para seguir con el resto");
        Debe(r.Cuenta.Contains("no lo veo") || r.Cuenta.Contains("ahora no"),
            $"y distingue «lo conozco pero AHORA no lo veo» de no conocerlo (dijo: «{r.Cuenta}») — es "
            + "la promesa 15 del núcleo hablando por el batch");

        var (batch2, _, tocados2) = BatchCon(g, "uia://x.exe/a", RutasDeTres);
        var r2 = batch2.Recorre(new[] { new RecorrerSegunElNucleo.Paso("Fantasma") });
        Debe(tocados2.Count == 0 && r2.Cuenta.Contains("no lo conozco"),
            $"y lo que nunca se vio aquí se dice como desconocido, sin pulsar nada (dijo: «{r2.Cuenta}»)");

        // LO EXACTO GANA A LO DIFUSO. Encontrado en terreno real (Wikipedia, 2026-08-24): una
        // página web observa FRAGMENTOS de texto como elementos —«,», «[1]», «El»— y el
        // emparejamiento por contención hacía que la basura «El» se tragara el exit «El portal
        // asociado a este artículo»: el batch pulsó «El» y reportó «no pude pulsar "El"». Si hay
        // un vivo cuyo nombre es EXACTAMENTE el pedido, ese manda; lo difuso queda para cuando no
        // hay exacto (que es el caso de «Copilot» → «Copilot anclado», promesa 43).
        var g4 = new Nucleo.Grafo();
        g4.Observar("uia://x.exe/wiki", new[]
        {
            new Nucleo.Elemento("s:basura", "El", "Text"),
            new Nucleo.Elemento("s:portal", "El portal asociado a este artículo", "Hyperlink"),
        });
        var rutas4 = new Dictionary<string, string> { ["uia://x.exe/wiki|s:portal"] = "uia://x.exe/portal" };
        var (batch4, donde4, tocados4) = BatchCon(g4, "uia://x.exe/wiki", rutas4);
        var r4 = batch4.Recorre(new[] { new RecorrerSegunElNucleo.Paso("El portal asociado a este artículo") });
        Debe(tocados4.Count == 1 && tocados4[0] == "El portal asociado a este artículo",
            $"con un vivo EXACTO y otro que solo se le parece, se pulsa el exacto (se pulsó "
            + $"«{(tocados4.Count > 0 ? tocados4[0] : "nada")}»): un fragmento de texto de dos letras "
            + "no puede tragarse un enlace entero");
        Debe(donde4() == "uia://x.exe/portal", "y se llegó a donde el enlace de verdad lleva");

        // Y EL DIFUSO VA EN UNA SOLA DIRECCIÓN: lo pedido puede ser un TROZO del nombre real
        // («Copilot» → «Copilot anclado», promesa 43), pero un trozo de página NO puede reclamar lo
        // pedido. Encontrado en la misma prueba real: el fragmento «que» se tragó «Paso Que No
        // Existe» por contención inversa, y el batch contestó «no pude pulsar "que"» — un
        // diagnóstico equivocado sobre un paso que simplemente no existía (Wikipedia, 2026-08-24).
        var g5 = new Nucleo.Grafo();
        g5.Observar("uia://x.exe/wiki", new[] { new Nucleo.Elemento("s:frag", "que", "Text") });
        var (batch5, _, tocados5) = BatchCon(g5, "uia://x.exe/wiki", new Dictionary<string, string>());
        var r5 = batch5.Recorre(new[] { new RecorrerSegunElNucleo.Paso("Paso Que No Existe") });
        Debe(tocados5.Count == 0 && r5.Cuenta.Contains("no lo conozco"),
            $"un fragmento de la página no reclama lo pedido: «Paso Que No Existe» se contesta como "
            + $"desconocido, no pulsando «que» (dijo: «{r5.Cuenta}»)");

        // DOS VIVOS CON EL MISMO NOMBRE: no se adivina — la misma regla que abrir (promesa 40).
        var g3 = new Nucleo.Grafo();
        g3.Observar("uia://x.exe/a", new[]
        {
            new Nucleo.Elemento("s:g1", "Guardar", "Button"),
            new Nucleo.Elemento("s:g2", "Guardar", "Button"),
        });
        var (batch3, _, tocados3) = BatchCon(g3, "uia://x.exe/a", RutasDeTres);
        var r3 = batch3.Recorre(new[] { new RecorrerSegunElNucleo.Paso("Guardar") });
        Debe(tocados3.Count == 0 && r3.Cuenta.Contains("s:g1") && r3.Cuenta.Contains("s:g2"),
            $"con dos vivos homónimos no se adivina: se paran y se dan los DOS selectores para que "
            + $"el que pide elija (dijo: «{r3.Cuenta}»)");
    }

    private static void ElBatchNoMiente()
    {
        // Paso 1 va bien (a→b), el 2 pide algo que no existe: se hizo UNO, y se dice uno.
        var g = MundoDeTres();
        var (batch, donde, _) = BatchCon(g, "uia://x.exe/a", RutasDeTres);
        var r = batch.Recorre(new[]
        {
            new RecorrerSegunElNucleo.Paso("Uno"),
            new RecorrerSegunElNucleo.Paso("Fantasma"),
            new RecorrerSegunElNucleo.Paso("Tres"),
        });

        Debe(r.Hechos == 1 && r.Total == 3 && !r.Termino,
            $"hizo 1 de 3 y lo dice como 1 de 3 (dijo {r.Hechos} de {r.Total}): el progreso parcial "
            + "contado como total haría que el modelo siguiera creyendo que ya está donde no está");
        Debe(r.Cuenta.Contains("1 de 3"), $"y el relato lleva la cuenta tal cual (dijo: «{r.Cuenta}»)");
        Debe(r.Donde == "uia://x.exe/b" && donde() == "uia://x.exe/b",
            $"y dice DÓNDE quedó de verdad (dijo «{r.Donde}»)");
        Debe(r.Cuenta.Contains("Dos"),
            $"y cuenta qué SÍ está vivo ahí —«Dos»— para que el modelo replanifique sin gastar otra "
            + $"llamada de reconocimiento (dijo: «{r.Cuenta}»)");

        // Y cuando lo hace todo, lo dice completo y con el destino final.
        var (batch2, _, _) = BatchCon(MundoDeTres(), "uia://x.exe/a", RutasDeTres);
        var r2 = batch2.Recorre(new[]
        {
            new RecorrerSegunElNucleo.Paso("Uno"),
            new RecorrerSegunElNucleo.Paso("Dos"),
        });
        Debe(r2.Termino && r2.Hechos == 2 && r2.Donde == "uia://x.exe/c",
            $"los 2 de 2 terminan en «c» y así se cuenta (dijo: {r2.Hechos} de {r2.Total}, en «{r2.Donde}»)");
    }

    private static void ElBatchFabricaAristas()
    {
        // La tesis entera del plan: las aristas entre ubicaciones no se deducen mirando, se GANAN
        // ejecutando. Tras un batch de tres pasos, los tres tramos tienen que estar en el grafo —
        // aquí la atribución es trivial porque el que pulsó fuimos nosotros.
        var g = MundoDeTres();
        var (batch, _, _) = BatchCon(g, "uia://x.exe/a", RutasDeTres);
        var r = batch.Recorre(new[]
        {
            new RecorrerSegunElNucleo.Paso("Uno"),
            new RecorrerSegunElNucleo.Paso("Dos"),
            new RecorrerSegunElNucleo.Paso("Tres"),
        });

        Debe(r.Termino && r.Hechos == 3, $"los tres pasos se hicieron ({r.Hechos} de {r.Total})");
        Debe(g.DesdeAqui("uia://x.exe/a").Single(x => x.Que.Selector == "s:1").Destino == "uia://x.exe/b",
            "la arista del paso 1 quedó: a —s:1→ b");
        Debe(g.DesdeAqui("uia://x.exe/b").Single(x => x.Que.Selector == "s:2").Destino == "uia://x.exe/c",
            "la del paso 2: b —s:2→ c");
        Debe(g.DesdeAqui("uia://x.exe/c").Single(x => x.Que.Selector == "s:3").Destino == "uia://x.exe/d",
            "y la del paso 3: c —s:3→ d. Un batch que navega sin dejar aristas deja el grafo tan "
            + "incomunicado como estaba — y era EL problema que esto vino a resolver");
    }

    private static void ElFrenoCortaElBatch()
    {
        // Escape se pulsa DURANTE el batch: después del primer paso, antes del segundo. El freno se
        // pregunta antes de CADA paso — preguntarlo solo al empezar dejaría una tanda de veinte
        // pasos corriendo entera con el usuario gritando que pare.
        var g = MundoDeTres();
        var (batch, _, tocados) = BatchCon(g, "uia://x.exe/a", RutasDeTres,
            frenoTrasTocar: yaTocados => yaTocados >= 1);
        var r = batch.Recorre(new[]
        {
            new RecorrerSegunElNucleo.Paso("Uno"),
            new RecorrerSegunElNucleo.Paso("Dos"),
            new RecorrerSegunElNucleo.Paso("Tres"),
        });

        Debe(tocados.Count == 1,
            $"tras el Escape no se pulsó ni uno más (se pulsaron {tocados.Count}): el freno manda "
            + "sobre la tanda entera, no solo sobre el arranque");
        Debe(r.Hechos == 1 && !r.Termino, $"y se cuenta como 1 de 3, no como terminado");
        Debe(r.Cuenta.Contains("Escape") || r.Cuenta.Contains("paraste"),
            $"y se DICE que fue el freno (dijo: «{r.Cuenta}»): pararse en silencio se vive igual "
            + "que colgarse, y son cosas opuestas");
    }

    // ── EL SERVIDOR MCP (F2 del plan de batch) ───────────────────────────────
    //
    // La puerta por la que entra el Agent SDK. La sonda 8791 NO habla MCP —es un shim de
    // desarrollo— y estas tres promesas son lo que un cliente genérico necesita para funcionar sin
    // saber nada de U: presentarse bien, publicar el catálogo con esquemas, y despachar sin
    // inventar. Se juzga el protocolo puro (ProtocoloMcp), sin HTTP: el cable no puede equivocarse
    // en silencio; el protocolo sí.

    private static ProtocoloMcp McpCon(List<(string Tool, string Args)> llamadas, string contesta = "estás en «x»")
        => new(
            new[]
            {
                new Voz.Realtime.Utensilio("map_where_am_i", "Dice dónde estás.", Array.Empty<Voz.Realtime.Argumento>()),
                new Voz.Realtime.Utensilio("map_batch", "N pasos por llamada.",
                    new[] { new Voz.Realtime.Argumento("pasos", "Lista JSON de pasos.") }),
            },
            (tool, args) =>
            {
                llamadas.Add((tool, string.Join(",", args.Select(a => $"{a.Key}={a.Value}"))));
                return contesta;
            });

    private static JsonElement Json(string? s)
    {
        Debe(s != null, "hubo respuesta donde tenía que haberla");
        return JsonDocument.Parse(s!).RootElement;
    }

    private static void ElMcpSePresenta()
    {
        var p = McpCon(new());

        var r = Json(p.Atiende("""{"jsonrpc":"2.0","id":7,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"inspector","version":"1.0"}}}"""));
        Debe(r.GetProperty("jsonrpc").GetString() == "2.0", "contesta JSON-RPC 2.0, no un JSON cualquiera");
        Debe(r.GetProperty("id").GetInt32() == 7,
            "con el MISMO id que preguntó: el id es cómo el cliente casa pregunta y respuesta, y sin "
            + "él las respuestas se asignan a la petición equivocada");
        var res = r.GetProperty("result");
        Debe(res.GetProperty("protocolVersion").GetString() == "2025-06-18",
            "y acepta la versión de protocolo que el cliente trae: contestar otra obliga al cliente a renegociar o rendirse");
        Debe(res.TryGetProperty("capabilities", out var cap) && cap.TryGetProperty("tools", out JsonElement _tools),
            "declara que tiene herramientas: sin esa capacidad, el cliente ni pregunta por ellas");
        Debe(res.GetProperty("serverInfo").GetProperty("name").GetString()!.Length > 0, "y dice quién es");

        Debe(p.Atiende("""{"jsonrpc":"2.0","method":"notifications/initialized"}""") == null,
            "una NOTIFICACIÓN no se contesta: no trae id, y contestar a quien no preguntó rompe el flujo del cliente");

        var mal = Json(p.Atiende("esto no es json"));
        Debe(mal.GetProperty("error").GetProperty("code").GetInt32() == -32700,
            "el JSON roto se contesta con el error -32700 del estándar, no con silencio ni con prosa");

        var desconocido = Json(p.Atiende("""{"jsonrpc":"2.0","id":8,"method":"metodo/inventado"}"""));
        Debe(desconocido.GetProperty("error").GetProperty("code").GetInt32() == -32601,
            "y un método que no existe se dice con -32601: el cliente genérico SABE leer ese código");
    }

    private static void ElMcpPublicaElCatalogo()
    {
        var p = McpCon(new());
        var r = Json(p.Atiende("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}"""));
        var tools = r.GetProperty("result").GetProperty("tools");

        var nombres = tools.EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
        Debe(nombres.Contains("map_where_am_i") && nombres.Contains("map_batch"),
            $"el catálogo trae las herramientas del mapa (trajo: {string.Join(", ", nombres)})");

        foreach (var t in tools.EnumerateArray())
        {
            Debe(t.GetProperty("description").GetString()!.Length > 0,
                $"«{t.GetProperty("name")}» lleva descripción: sin ella el modelo no sabe cuándo usarla");
            var schema = t.GetProperty("inputSchema");
            Debe(schema.GetProperty("type").GetString() == "object",
                "y un inputSchema de objeto, que es lo que el estándar exige aunque no haya argumentos");
        }

        var batch = tools.EnumerateArray().First(t => t.GetProperty("name").GetString() == "map_batch");
        var pasos = batch.GetProperty("inputSchema").GetProperty("properties").GetProperty("pasos");
        Debe(pasos.GetProperty("type").GetString() == "string" && pasos.GetProperty("description").GetString()!.Length > 0,
            "cada argumento va tipado y descrito: el esquema ES la documentación que el cliente enseña al modelo");
    }

    private static void ElMcpDespachaYContesta()
    {
        var llamadas = new List<(string Tool, string Args)>();
        var p = McpCon(llamadas, contesta: "Estás en «uia://x.exe/uno». Veo 3 salida(s).");

        var r = Json(p.Atiende("""{"jsonrpc":"2.0","id":9,"method":"tools/call","params":{"name":"map_where_am_i","arguments":{}}}"""));
        Debe(llamadas.Count == 1 && llamadas[0].Tool == "map_where_am_i",
            "la llamada llegó al MISMO despachador de siempre — el MCP no inventa un segundo camino de acción");
        var content = r.GetProperty("result").GetProperty("content");
        Debe(content[0].GetProperty("type").GetString() == "text"
             && content[0].GetProperty("text").GetString()!.Contains("uia://x.exe/uno"),
            "y lo que la herramienta contestó vuelve TAL CUAL en content: resumirlo le quitaría al "
            + "modelo justo la pista que necesita");
        Debe(r.GetProperty("id").GetInt32() == 9, "con su id");

        var antes = llamadas.Count;
        var mal = Json(p.Atiende("""{"jsonrpc":"2.0","id":10,"method":"tools/call","params":{"name":"tool_inventada","arguments":{}}}"""));
        Debe(mal.TryGetProperty("error", out var err) && err.GetProperty("code").GetInt32() == -32602,
            "una herramienta que no está en el catálogo se rechaza con -32602");
        Debe(llamadas.Count == antes,
            "y NO se despacha: ejecutar lo que no se publicó sería un catálogo de mentira");

        // Los argumentos llegan como los manda el cliente (números incluidos) y se aplanan a texto,
        // que es lo que nuestras herramientas hablan.
        p.Atiende("""{"jsonrpc":"2.0","id":11,"method":"tools/call","params":{"name":"map_batch","arguments":{"pasos":"[{\"exit\":\"Uno\"}]"}}}""");
        Debe(llamadas.Last().Args.Contains("pasos=[{\"exit\":\"Uno\"}]"),
            $"los argumentos llegan enteros al despachador (llegó: {llamadas.Last().Args})");
    }

    // ── LA RESOLUCIÓN ESTABLE (la lección de la primera corrida real del piloto) ─────────────
    //
    // Medido el 2026-08-25 con el Agent SDK de verdad sobre Wikipedia: el terreno TENÍA las dos
    // aristas que la tarea necesitaba, y aun así costó 24 viajes al modelo y no terminó. Dos causas,
    // y ninguna era falta de mapa: (1) el modelo pide por el nombre del DESTINO —«Portal:Ajedrez»—
    // y la puerta se llama «El portal asociado a este artículo»; (2) una página web observa
    // FRAGMENTOS de texto como elementos —«,», «[1]», «, dos», párrafos enteros— que reclaman pasos
    // por contención y ensucian la lista de «vivo aquí» hasta volverla inservible para replanificar.

    private static void PedirElDestinoValeComoLaPuerta()
    {
        // El caso real, tal cual: la puerta se llama de una manera y el sitio de otra.
        var g = new Nucleo.Grafo();
        g.Observar("web://x/Ajedrez", new[] { new Nucleo.Elemento("s:portal", "El portal asociado a este artículo", "Hyperlink") });
        g.Cruzar("web://x/Ajedrez", "s:portal", "web://x/Portal:Ajedrez");

        var rutas = new Dictionary<string, string> { ["web://x/Ajedrez|s:portal"] = "web://x/Portal:Ajedrez" };
        var (batch, _, tocados) = BatchCon(g, "web://x/Ajedrez", rutas);
        var r = batch.Recorre(new[] { new RecorrerSegunElNucleo.Paso("Portal:Ajedrez") });

        Debe(r.Hechos == 1 && r.Donde == "web://x/Portal:Ajedrez",
            $"«Portal:Ajedrez» no es ninguna puerta de aquí, pero SÍ es a dónde lleva una arista "
            + $"aprendida: se cruza por ella (hizo {r.Hechos}, quedó en «{r.Donde}»). El modelo "
            + "piensa en destinos; obligarlo a saberse el nombre exacto del enlace es tirar las "
            + "aristas que el grafo ya ganó — le costó 24 viajes en la primera corrida real");
        Debe(tocados.Count == 1 && tocados[0] == "El portal asociado a este artículo",
            "y la puerta pulsada fue LA DE VERDAD, con su nombre real");

        // Lo mismo en una app nativa, por la cola de la ubicación.
        var g2 = new Nucleo.Grafo();
        g2.Observar("uia://explorer.exe/documentos", new[] { new Nucleo.Elemento("s:d", "Descargas (acceso)", "ListItem") });
        g2.Cruzar("uia://explorer.exe/documentos", "s:d", "uia://explorer.exe/descargas");
        var (batch2, _, tocados2) = BatchCon(g2, "uia://explorer.exe/documentos",
            new Dictionary<string, string> { ["uia://explorer.exe/documentos|s:d"] = "uia://explorer.exe/descargas" });
        var r2 = batch2.Recorre(new[] { new RecorrerSegunElNucleo.Paso("Descargas") });
        Debe(r2.Hechos == 1 && tocados2.Count == 1,
            $"«Descargas» resuelve por la cola del destino «uia://explorer.exe/descargas» aunque la "
            + $"puerta se llame «Descargas (acceso)» (dijo: «{r2.Cuenta}»)");

        // Y la puerta MUERTA con destino conocido se dice con las dos mitades: sé llegar, y por
        // dónde — el nombre de la puerta es justo lo que el modelo necesita para reintentarlo bien.
        var g3 = new Nucleo.Grafo();
        g3.Observar("web://x/Ajedrez", new[] { new Nucleo.Elemento("s:portal", "El portal asociado a este artículo", "Hyperlink") });
        g3.Cruzar("web://x/Ajedrez", "s:portal", "web://x/Portal:Ajedrez");
        g3.Observar("web://x/Ajedrez", new[] { new Nucleo.Elemento("s:otro", "Otra cosa", "Hyperlink") });
        var (batch3, _, tocados3) = BatchCon(g3, "web://x/Ajedrez", rutas);
        var r3 = batch3.Recorre(new[] { new RecorrerSegunElNucleo.Paso("Portal:Ajedrez") });
        Debe(tocados3.Count == 0 && r3.Hechos == 0,
            "con la puerta muerta no se pulsa nada — la compuerta sigue mandando");
        Debe(r3.Cuenta.Contains("El portal asociado a este artículo"),
            $"pero se dice POR DÓNDE se sabía llegar (dijo: «{r3.Cuenta}»): con el nombre real de "
            + "la puerta, el siguiente intento del modelo ya no adivina");
    }

    private static void DosDestinosNoSeAdivinan()
    {
        // Dos aristas cuyos destinos se llaman igual en la cola: «uno» está en un uia y en un web.
        var g = new Nucleo.Grafo();
        g.Observar("uia://x.exe/a", new[]
        {
            new Nucleo.Elemento("s:1", "Primera puerta", "Button"),
            new Nucleo.Elemento("s:2", "Segunda puerta", "Button"),
        });
        g.Cruzar("uia://x.exe/a", "s:1", "uia://x.exe/uno");
        g.Cruzar("uia://x.exe/a", "s:2", "web://y/uno");

        var (batch, _, tocados) = BatchCon(g, "uia://x.exe/a", RutasDeTres);
        var r = batch.Recorre(new[] { new RecorrerSegunElNucleo.Paso("uno") });

        Debe(tocados.Count == 0 && r.Hechos == 0,
            "con dos caminos que llevan a un «uno» no se adivina cuál quería: se para");
        Debe(r.Cuenta.Contains("s:1") && r.Cuenta.Contains("s:2"),
            $"y se dan los DOS selectores para que quien pidió elija (dijo: «{r.Cuenta}») — la misma "
            + "regla de siempre: si de verdad hay empate, se devuelven todas (promesa 40)");
    }

    private static void LaBasuraNoEsUnaPuerta()
    {
        // Los elementos REALES que Wikipedia puso vivos en la corrida del 2026-08-25.
        var g = new Nucleo.Grafo();
        g.Observar("web://x/pagina", new[]
        {
            new Nucleo.Elemento("s:ok", "Discusión", "Hyperlink"),
            new Nucleo.Elemento("s:coma", ",", "Text"),
            new Nucleo.Elemento("s:cita", "[1]", "Hyperlink"),
            new Nucleo.Elemento("s:css", "_r_1fi7_", "Text"),
            new Nucleo.Elemento("s:frag", ", dos", "Text"),
            new Nucleo.Elemento("s:parrafo", ". Se trata de un juego de estrategia en el que el objetivo es encerrar al rey del oponente sin que el otro jugador pueda protegerlo", "Text"),
        });

        // (a) La basura NO reclama pasos por contención: «, dos» contiene «dos», y sin el filtro se
        // lo tragaba — pulsar un fragmento de párrafo es pulsar un punto ciego de la página.
        var (batch, _, tocados) = BatchCon(g, "web://x/pagina", RutasDeTres);
        var r = batch.Recorre(new[] { new RecorrerSegunElNucleo.Paso("dos") });
        Debe(tocados.Count == 0 && r.Cuenta.Contains("no lo conozco"),
            $"«dos» no lo reclama el fragmento «, dos»: un trozo de párrafo no es una puerta "
            + $"(dijo: «{r.Cuenta}»)");

        // (b) Y la lista de «vivo aquí» —lo que el modelo usa para REPLANIFICAR— trae puertas, no
        // escombros: en la corrida real la lista era «,», «[1]», párrafos… y con eso no se
        // replanifica nada; se gasta otra llamada de reconocimiento, que es lo que veníamos a evitar.
        Debe(r.Cuenta.Contains("Discusión"), $"la puerta real SÍ se cuenta (dijo: «{r.Cuenta}»)");
        Debe(!r.Cuenta.Contains("«,»") && !r.Cuenta.Contains("[1]") && !r.Cuenta.Contains("_r_1fi7_")
             && !r.Cuenta.Contains("Se trata de un juego"),
            $"y los escombros no: ni puntuación suelta, ni notas al pie, ni clases CSS, ni párrafos "
            + $"(dijo: «{r.Cuenta}»)");
    }

    private static void UnaWebSeVaDirecto()
    {
        // El caso real (2026-08-25, revancha del piloto): estando en Portal:Ajedrez se pidió ir a
        // Ajedrez —mismo dominio— y map_go_to contestó tres veces «no hay ningún camino aprendido»
        // pudiendo abrir la URL directo. La regresión venía DEL APRENDIZAJE: con el destino ya
        // conocido en el grafo, el camino directo del navegador dejaba de intentarse. Una web no es
        // un laberinto: CADA ubicación tiene puerta directa desde cualquier parte — la URL.
        var g = new Nucleo.Grafo();
        var puestos = new List<string>();
        string donde = "web://x/Portal:Ajedrez";
        var paso = new PasoDelNucleo(g, () => donde,
            (_, __) => true,
            destino => { puestos.Add(destino); donde = destino; return true; });

        var r = paso.Hacia("web://x/Ajedrez");
        Debe(puestos.Count == 1 && puestos[0] == "web://x/Ajedrez",
            $"sin camino aprendido, a una web se va DIRECTO (se pidió ponerse delante {puestos.Count} vez/veces): "
            + "rendirse con «no hay camino aprendido» teniendo la URL en la mano costó tres rebotes "
            + "seguidos en la corrida real");
        Debe(r.Llegado, $"y se llega (dijo: «{r.Porque}»)");

        // EL EXPLORADOR NO SE ATAJA — la regla de siempre (2026-08-16): sin recordar la ruta, la
        // misma hoja significa sitios distintos según dónde estés. El salto directo es de la web.
        var g2 = new Nucleo.Grafo();
        var puestos2 = new List<string>();
        string donde2 = "uia://explorer.exe/documentos";
        var paso2 = new PasoDelNucleo(g2, () => donde2, (_, __) => true,
            destino => { puestos2.Add(destino); donde2 = destino; return true; });
        var r2 = paso2.Hacia("uia://explorer.exe/fotos-de-2019");
        Debe(puestos2.Count == 0 && !r2.Llegado,
            "dentro del explorador NO se salta: llegar rápido al sitio equivocado es peor que llegar "
            + "despacio al correcto");
    }

    private static void UnaHerramientaColgadaNoCuelgaLaPuerta()
    {
        // Medido el 2026-08-25: map_what_i_see se quedó 1014 SEGUNDOS sin contestar y, como la
        // puerta atendía en serie, TODO lo demás murió detrás — «The operation timed out» en cadena
        // y la tarea entera perdida. Una herramienta puede colgarse; la puerta no puede colgarse
        // con ella.
        var p = new ProtocoloMcp(
            new[] { new Voz.Realtime.Utensilio("lenta", "tarda demasiado", Array.Empty<Voz.Realtime.Argumento>()) },
            (_, __) => { Thread.Sleep(600); return "llegué tardísimo"; })
        { TiempoMaximoDeHerramienta = TimeSpan.FromMilliseconds(150) };

        var r = Json(p.Atiende("""{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"lenta","arguments":{}}}"""));
        var res = r.GetProperty("result");
        Debe(res.GetProperty("isError").GetBoolean(),
            "pasado el plazo se contesta ERROR, no se espera para siempre");
        Debe(res.GetProperty("content")[0].GetProperty("text").GetString()!.Contains("no contestó"),
            "y el texto dice qué pasó — que la herramienta no contestó a tiempo — no un silencio");
        Debe(r.GetProperty("id").GetInt32() == 5, "con su id, para que el cliente sepa cuál murió");
    }

    private static void PulsarSinMoverNoEsLlegar()
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

    private static void PulsarQueNoSePudoNoCuenta()
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

    private static void PulsarAprendeADondeLlevoDeVerdad()
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

    private static void IluminarHomonimasPorSelector()
    {
        // Dos cosas que se llaman IGUAL — que es justo el caso que se está resolviendo.
        var pantalla = new Dictionary<string, LoQueSenalas.Candidato>(StringComparer.OrdinalIgnoreCase)
        {
            ["uia:name=Pausar;ct=Button"]     = new("Pausar", "Button", new System.Windows.Rect(10, 10, 40, 40)),
            ["uia:aid=pausar2;ct=Button"]     = new("Pausar", "Button", new System.Windows.Rect(90, 10, 40, 40)),
            ["uia:name=Otra;ct=Button"]       = new("Otra",   "Button", new System.Windows.Rect(200, 10, 40, 40)),
            ["uia:name=SinCaja;ct=Button"]    = new("SinCaja","Button", System.Windows.Rect.Empty),
        };

        var r = LoQueSenalas.Iluminables(
            new[] { "uia:aid=pausar2;ct=Button", "uia:name=Pausar;ct=Button" }, pantalla);

        Debe(r.Count == 2, $"se iluminan las DOS homónimas (fueron {r.Count})");
        Debe(r[0].Caja.X == 90 && r[1].Caja.X == 10,
            "y EN EL ORDEN en que se piden, no en el de la pantalla: la respuesta las numera —«la 1 "
            + "es…, la 2 es…»— y quien elige lo hace por ese número. Si el orden cambiara entre lo "
            + "que se dice y lo que se pinta, señalaría la 2 y se pulsaría la 1");

        Debe(LoQueSenalas.Iluminables(new[] { "uia:name=SinCaja;ct=Button" }, pantalla).Count == 0,
            "lo que no tiene caja no se ilumina: un recuadro de tamaño cero no enseña nada");
        Debe(LoQueSenalas.Iluminables(new[] { "uia:name=NoExiste" }, pantalla).Count == 0,
            "y lo que no está en pantalla se cae en silencio: enseñar tres de cuatro es mejor que "
            + "no enseñar ninguna");
    }

    // ── ENSEÑAR ──────────────────────────────────────────────────────────────
    //
    // El grafo ya aprende CAMINOS solo pulsando. Lo que no sabía guardar es lo que se pidió el
    // primer día: «ves esto de aquí, aquí vas a escribir X cuando Y». Un camino no es un
    // significado — y sin significado no se pueden pedir tareas por lo que son, solo por dónde
    // están.


    private static void EnsenarExigeIdentidadUtil()
    {
        // Medido el 2026-08-23 en ensenanzas.json: se guardó «uia:path=;ct=Pane» — el selector del
        // CONTENEDOR, no el del botón, porque el nombre apareció en un descendiente. Un selector
        // así no vuelve a encontrar nada, y lo enseñado cuelga de él: no es un detalle de formato,
        // es una enseñanza perdida el día que se vuelve a esa pantalla.
        Debe(!SirveComoIdentidad("uia:path=;ct=Pane"),
            "un selector con la ruta VACÍA no sirve como identidad: no distingue nada");
        Debe(!SirveComoIdentidad(""), "y uno vacío tampoco");
        Debe(SirveComoIdentidad("uia:name=Guardar;ct=Button"),
            "uno con nombre y tipo sí: con eso se vuelve a encontrar");
        Debe(SirveComoIdentidad("uia:aid=btnGuardar;ct=Button"),
            "y uno con AutomationId, que es el mejor de todos");
    }

    /// <summary>La misma regla que usa SurfaceMapTools al anotar lo señalado.</summary>
    private static bool SirveComoIdentidad(string sel)
        => sel.Length > 0 && !sel.Contains("path=;") && !sel.StartsWith("uia:path=;");

    // ── QUE UNA LECCIÓN NO SE PIERDA EN SILENCIO ─────────────────────────────
    //
    // Estas dos existen por un fallo medido, no por completitud. El catálogo de herramientas y el
    // prompt ya PEDÍAN guardar lo enseñado, con los disparadores escritos uno por uno. Se probó el
    // 2026-08-24 con el arreglo puesto: tres lecciones seguidas —«SIEMPRE hacemos clic aquí»,
    // «lo primero que haremos SIEMPRE será…», «SIEMPRE escribirás NWP1»— trece llamadas a
    // herramientas, y map_esto_es CERO veces.
    //
    // Una petición en el prompt no es una garantía. Lo que sí se puede garantizar desde este lado es
    // que la omisión SE VEA: si aquí se dice «esto era una lección» y no se creó ningún recuerdo,
    // sale un aviso. Por eso lo que hay que juzgar es este juicio — y se puede, sin micrófono.

    private static void UnaLeccionSeReconoce()
    {
        // LAS TRES QUE SE PERDIERON DE VERDAD. Si alguna de estas dejara de reconocerse, volveríamos
        // exactamente al día en que el usuario dijo «no sé cuándo está aprendiendo».
        Debe(UnaLeccion.Parece("Para atender a un paciente, siempre hacemos clic aquí en acceder al sistema"),
            "«siempre hacemos X» es enseñar un procedimiento, aunque no diga «esto es»");
        Debe(UnaLeccion.Parece("Lo primero que haremos siempre será verificar si tenemos contexto"),
            "«lo primero que haremos» también, y esta no lleva ningún «esto es» por ningún lado");
        Debe(UnaLeccion.Parece("aquí vamos a escribir NWP1, siempre escribirás NWP1"),
            "y «siempre escribirás X» es la más clara de las tres");

        // LOS IMPERATIVOS DE MEMORIA, que son los que no tienen forma de definición y por eso se
        // escapaban: quien enseña un procedimiento no dice «esto es», dice «recuerda que».
        Debe(UnaLeccion.Parece("Recuérdalo, recuerda que para iniciar sesión se hace clic en acceder al sistema"),
            "«recuerda que…» es una lección");
        Debe(UnaLeccion.Parece("antes de abrir SAP, verifica que FortiClient esté habilitado"),
            "«antes de X, hay que Y» enseña el orden de las cosas");
        Debe(UnaLeccion.Parece("de ahora en adelante el número de factura va sin guiones"),
            "«de ahora en adelante» dice literalmente que esto tiene que quedarse");

        // Y las definiciones de toda la vida, que ya funcionaban y no pueden dejar de hacerlo.
        Debe(UnaLeccion.Parece("esto es el número de factura, nunca el nombre"), "«esto es X» sigue contando");
        Debe(UnaLeccion.Parece("este botón sirve para radicar las cuentas"), "«sirve para» también");
    }

    // ── CONTAR LOS RECUERDOS DE UNO EN UNO ───────────────────────────────────
    //
    // Tercera vez en esta sesión que una petición del prompt no basta. El catálogo decía «te dice
    // recuerdo 1 de N, lo ilumina, y tú lo CUENTAS EN VOZ; cuando termines, pídeme el 2», y el
    // modelo encadenó las dos llamadas igual. La secuencia del servidor, medida el 2026-08-24:
    //
    //   20:10:39  respuesta A → map_recuerdos          · CERO audio
    //   20:10:40  respuesta B → map_recuerdos cual=2   · CERO audio
    //   20:10:43  respuesta C → la única voz, contando LOS DOS
    //
    // El recuadro del primero duró un segundo. Lo que se juzga aquí es la regla que lo impide.

    private static void DeUnoEnUnoONoHaySiguiente()
    {
        var turno = new ElTurnoDeContar();

        Debe(turno.PuedeContar(1), "el primero siempre se puede contar: no hay nada anterior que contar antes");
        turno.SeConto(1);

        Debe(!turno.PuedeContar(2),
            "pedir el 2 SIN haber hablado se niega — es exactamente lo que pasó: dos llamadas "
            + "seguidas sin una palabra en medio, y el recuadro saltó al segundo en un segundo");

        turno.Hablo();
        Debe(turno.PuedeContar(2), "y en cuanto habla, el 2 se le da: la negativa era por el silencio, no por el número");

        // Y no se queda desbloqueado para siempre: cada entrega vuelve a exigir su turno de voz.
        turno.SeConto(2);
        Debe(!turno.PuedeContar(3),
            "haber hablado UNA vez no compra todos los siguientes: cada recuerdo pide el suyo");
    }

    private static void VolverAtrasNoEsAvanzar()
    {
        var turno = new ElTurnoDeContar();
        turno.SeConto(1);
        turno.Hablo();
        turno.SeConto(2);   // ya va por el 2 y todavía no ha hablado de él

        Debe(turno.PuedeContar(2),
            "repetir el que se está contando se deja pasar: no adelanta el recuadro, así que no "
            + "puede desincronizar nada");
        Debe(turno.PuedeContar(1),
            "y volver atrás también — «espera, ¿cuál era el primero?» es lo más natural del mundo "
            + "y negarlo convertiría una garantía en un estorbo");
        Debe(!turno.PuedeContar(3), "pero avanzar sigue exigiendo haber hablado");

        // Preguntar otra vez «¿qué recuerdas de aquí?» empieza de cero.
        turno.Reiniciar();
        Debe(turno.PuedeContar(1),
            "y una tanda nueva arranca limpia: sin esto, la segunda vez que alguien pregunta se "
            + "encontraría con que el primero «ya se contó»");
    }

    /// <summary>
    /// La cuenta de «cuál falta», con la misma aritmética que usa la herramienta.
    /// </summary>
    private static int SiguienteTras(int contado, int total) => contado < total ? contado + 1 : 0;

    private static void ContarNoEsAbandonarAMedias()
    {
        // HABLAR CIERRA EL TURNO. Contó el 1 de 2, lo dijo bien, y el segundo se quedó sin contar
        // porque después de hablar ya no hay nada que despierte al modelo (2026-08-24, medido:
        // «contando 1/2», una respuesta impecable, y silencio). La regla de uno-en-uno impide
        // atropellarlos; sin esta otra, la conversación se queda a medias educadamente.
        Debe(SiguienteTras(1, 2) == 2, "contado el 1 de 2, se sabe que falta el 2: quedarse ahí es dejar a medias");
        Debe(SiguienteTras(1, 3) == 2, "y con tres, igual");
        Debe(SiguienteTras(2, 3) == 3, "y se sigue sabiendo por el segundo");

        Debe(SiguienteTras(2, 2) == 0,
            "pero contado el último NO queda ninguno: seguir empujando después de terminar sería "
            + "insistir sobre una pregunta ya contestada");
        Debe(SiguienteTras(1, 1) == 0, "y con uno solo se termina en el primero");
    }

    private static void EscribirDetieneLaNarracion()
    {
        bool escribiendo = false;
        var turno = new ElTurnoDeContar { EscribiendoAlguien = () => escribiendo };

        turno.SeConto(1);
        turno.Hablo();
        Debe(turno.PuedeContar(2), "sin nadie escribiendo, contado y hablado el 1, el 2 se da");

        // Y AHORA ALGUIEN SE PONE A CORREGIR la tarjeta que tiene delante.
        escribiendo = true;
        Debe(!turno.PuedeContar(2),
            "con alguien escribiendo NO se pasa al siguiente: cambiar de recuerdo a media frase le "
            + "quita el foco y le borra la corrección");
        Debe(!turno.PuedeContar(1),
            "y tampoco se REPITE el actual, aunque repetir normalmente se deje: repintar la tarjeta "
            + "que está editando es exactamente lo que le tiraría lo escrito");

        escribiendo = false;
        Debe(turno.PuedeContar(2), "y en cuanto suelta el teclado, la narración sigue donde iba");
    }

    private static void ElRecuadroNoAdelantaALaVoz()
    {
        // SONAR NO ES RECIBIR. El turno se cierra cuando el servidor termina de MANDAR el audio, y
        // para entonces quedan segundos de voz en la cola del altavoz. Medido el 2026-08-24:
        //
        //   23:02:02  recuadro sobre el primero
        //   23:02:05  el servidor termina de mandar (~40 palabras ≈ 16 s de habla)
        //   23:02:05  el recuadro salta al segundo
        //
        // Tres segundos de recuadro para dieciséis de voz. El usuario lo dijo exacto: «menciona
        // bien el primer elemento, pero a destiempo con la señalización».
        bool sonando = true;
        var turno = new ElTurnoDeContar { SigueSonando = () => sonando };

        turno.SeConto(1);
        turno.Hablo();   // ya generó su narración: el servidor terminó

        Debe(!turno.PuedeContar(2),
            "haber hablado NO basta si todavía se está oyendo: el audio llega en un segundo y se "
            + "oye en dieciséis, así que aquí es donde el recuadro adelantaba a la voz");

        sonando = false;
        Debe(turno.PuedeContar(2),
            "y en cuanto el altavoz se vacía, sí: lo que manda es haber terminado de SONAR");

        // Y no se cuela por la puerta de repetir: mientras suene, tampoco se repinta.
        turno.SeConto(2);
        turno.Hablo();
        sonando = true;
        Debe(!turno.PuedeContar(3), "sigue sin poder avanzar mientras suene el anterior");
    }

    private static void NoTodoLoQueSuenaEsLeccion()
    {
        // AVISAR DE MÁS TIENE UN COSTE. Un aviso que salta en cada frase se vuelve ruido, y un ruido
        // que se ignora es exactamente igual de inútil que no avisar — solo que además estorba.
        Debe(!UnaLeccion.Parece("sí"), "un «sí» no enseña nada");
        Debe(!UnaLeccion.Parece("dale"), "ni un «dale»");
        Debe(!UnaLeccion.Parece("ábreme el explorador"), "una orden no es una lección: se ejecuta y ya");
        Debe(!UnaLeccion.Parece("¿recuerdas dónde estábamos?"),
            "PREGUNTAR por la memoria no es enseñar — quien pregunta no está dando un dato nuevo");

        // LA FRASE EXACTA CON LA QUE SE ESTRENA map_recuerdos. Saltó el aviso de «te enseñó algo y
        // no lo guardé» mientras el asistente contestaba perfectamente: la marca «recuerda» encajaba
        // dentro de «recuerdas», y el filtro solo cubría «¿recuerdas» pegado — con el interrogativo
        // en medio («¿QUÉ recuerdas») dejaba de pegar (2026-08-24, visto por el usuario).
        Debe(!UnaLeccion.Parece("Cuéntame qué sabes sobre esta pantalla, ¿qué recuerdas"),
            "preguntar QUÉ sabe de una pantalla es la pregunta que estrena los recuerdos, no una "
            + "lección: avisar ahí acusa al asistente justo cuando está haciéndolo bien");
        Debe(!UnaLeccion.Parece("¿qué te enseñé aquí la última vez?"),
            "y preguntar qué se le enseñó tampoco: se está pidiendo lo guardado, no dando algo nuevo");
        Debe(!UnaLeccion.Parece("¿no recuerdas algo sobre esta área de acá?"),
            "ni preguntarlo en negativo, que es como se pregunta cuando uno duda de si lo enseñó");
        Debe(!UnaLeccion.Parece("no recuerdo cómo se llamaba"),
            "y decir que NO se acuerda es lo contrario de enseñar");
        Debe(!UnaLeccion.Parece("puedes hacerlo siempre y cuando esté abierto"),
            "«siempre y cuando» es una condición, no un «siempre haz esto»");
        Debe(!UnaLeccion.Parece("como siempre, gracias"), "ni «como siempre», que es una muletilla");
    }

    private static void SenalarDistingueLasTres()
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

    private static void SenalarEligeLoMasPequeno()
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

    private static void ElegirNoDependeDelOrden()
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

    private static void SenalarNoAdivina()
    {
        var senalar = new LoQueSenalas(new Nucleo.Grafo(), () => "uia://falsa.exe/x");
        Debe(senalar.Con(null) == LoQueSenalas.NadaDebajo,
            "sin nada con nombre bajo el cursor se pide mover el cursor. Contestar con lo último "
            + "señalado sería peor que no contestar: quien pregunta creería que acertó");
    }

    private static void Prueba(string nombre, Action cuerpo)
    {
        // Cada promesa se juzga sobre un mapa recién nacido en su propio directorio.
        string dir = Path.Combine(_raiz, nombre.Split('.')[0]);
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable("U_DATA_DIR", dir);

        int antes = _fallos;
        try { cuerpo(); }
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
