using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using Omi;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using U.Graph;
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

        // ── LA CONSULTA CLÍNICA EN WINDOWS (spec 004) ────────────────────────
        //
        // La web ya graba la conversación médico-paciente, la transcribe y organiza la nota. Traer
        // eso aquí no es portar el audio —`DictadoSoniox` ya transcribía contra el MISMO backend
        // desde el 2026-08-14—: es darle al cliente una IDENTIDAD DE MÉDICO y el hilo que va del
        // micrófono al encounter.
        //
        // Las ocho se escriben antes que su código porque las tres piezas se dan por buenas a sí
        // mismas: la sesión diría que está viva teniendo un token vencido (aprendizaje nº9, vacío
        // no es ausente), la transcripción diría que mandó todo sin haber mandado la última frase
        // (patrón nº10, un paso no ejecutado deja rastro), y el dictado diría «el backend no
        // contestó» cuando lo que pasa es que contestó Deepgram y solo sabemos leer Soniox
        // (aprendizaje nº2, un mensaje no debe concluir). Las tres fallan EN VERDE.
        //
        // Ninguna toca la red, el micrófono ni la pantalla: el backend es de mentira y ANOTA lo
        // que se le pidió, que es lo que permite distinguir «no se creó el encounter» de «se creó
        // y falló».
        Console.WriteLine();
        Prueba("84. sin médico con sesión, la consulta no empieza: ni micrófono ni encounter", SinSesionLaConsultaNoEmpieza);
        Prueba("85. un token vencido se renueva ANTES de usarse, no cuando el backend lo rechaza", ElTokenSeRenuevaAntesDeUsarse);
        Prueba("86. cerrar sesión no deja el token en disco", SalirNoDejaRastro);
        Prueba("87. cada fallo del backend clínico dice una cosa distinta, no «no se pudo»", CadaFalloClinicoDiceLoSuyo);
        Prueba("88. se manda TODO lo dicho: la frase que quedó sin cerrar al parar también viaja", ElVerbatimNoSeDejaLaUltimaFrase);
        Prueba("89. el lector del stream lo elige la SESIÓN, no lo que se compiló", ElLectorLoEligeLaSesion);
        Prueba("90. la consulta se atribuye al MÉDICO que entró, no a la máquina", LaConsultaEsDelMedico);
        Prueba("91. si la nota no se generó, la consulta NO se declara terminada", SinNotaNoHayConsultaTerminada);

        // ── EL ARRANQUE QUE NO SE MUERE EN SILENCIO ──────────────────────────
        //
        // Medido en el log el 2026-09-01: `U.exe --consulta`, el medico entró bien
        // («cuenta: médico dentro · 67530d77…») y esa fue la ÚLTIMA LÍNEA DEL ARCHIVO. Sin
        // excepción, sin ventana, sin nada. La causa: `OnStartup` corre antes de que StartupUri
        // cree la carita, así que el login era la única ventana y al cerrarse
        // ShutdownMode.OnLastWindowClose dio la aplicación por terminada.
        //
        // El bug de WPF en sí es nivel 4 y se dice: solo se caza ejecutando. Lo que SÍ se puede
        // prometer —y es lo que impide que la próxima vez vuelva a ser invisible— es que el
        // arranque nombre el paso en el que se quedó. Un fallo que no se ve no se arregla.
        Console.WriteLine();
        Prueba("92. arrancar la consulta dice EN QUÉ PASO se quedó: morir callado no es un resultado", ElArranqueDiceDondeSeQuedo);

        // ── QUE LA CONSULTA SEA DE VERDAD LA MISMA (2026-09-01) ──────────────
        //
        // Al mirar dónde lista el portal sus consultas apareció un hueco que llevaba escrito en la
        // spec como si estuviera resuelto: el portal lista la tabla `consultations`, y quien la
        // escribe es EL PROPIO PORTAL (upsertConsultation(encounterToConsultation(...)), en
        // en-vivo/page.tsx:655). Windows creaba el `clinical_encounter` —que existe— y nada más,
        // así que una consulta grabada aquí era INVISIBLE en la web. «Misma base de datos» era
        // cierto de la mitad de abajo y falso de la que ve el médico.
        Prueba("93. una consulta grabada en Windows se VE en el portal: el espejo se escribe, no se supone", ElEspejoSeEscribe);
        Prueba("94. elegir plantilla deja de ser un paso: la consulta arranca sola", NadieEligePlantilla);

        // ── EL MICRÓFONO QUE NO ABRIÓ (2026-09-01, visto en el log del usuario) ──
        //
        //   [15:51:47] clinica: encounter 76cc7aec… created
        //   [15:51:48] dictado: no pude abrir el stream de dictado: Unable to connect…
        //   [15:51:48] consulta: grabando · encounter 76cc7aec…          ← y era MENTIRA
        //   [15:52:05] consulta: falló · no se oyó nada que transcribir: comprueba el micrófono
        //
        // El stream nunca conectó y la consulta se declaró GRABANDO igual: el usuario habló
        // diecisiete segundos a una app que no escuchaba, y al parar se le mandó a revisar el
        // MICRÓFONO — que no tenía nada que ver. Es el patrón nº10 (un paso no ejecutado deja
        // rastro) y el aprendizaje nº2 (un mensaje que no distingue sus causas) a la vez.
        Prueba("95. si el micrófono no llegó a abrir, la consulta NO dice que está grabando", SinMicrofonoNoSeGraba);

        // ── UN TROPIEZO DE RED NO ES UNA AVERÍA (2026-09-01, medido) ─────────
        //
        // El dictado se rendía al primer fallo. Medido en la máquina del usuario, en tres segundos
        // seguidos: el DNS de stt-rt.soniox.com contestó «Host desconocido», el TCP a :443 no
        // abrió... y el WebSocket al MISMO host abrió a la primera. El DNS de ese equipo falla de
        // forma intermitente, y con un solo intento eso se lleva la consulta entera por delante.
        //
        // El motor del portal, del que se portó todo lo demás, lleva desde siempre cuatro intentos
        // con espera creciente (useDictation.ts:40 — MAX_RECONNECT_ATTEMPTS y RECONNECT_DELAYS_MS).
        // Eso NO se portó, y es el hueco.
        Prueba("96. un tropiezo de red no tumba la grabación: se reintenta antes de rendirse", ElDictadoReintenta);

        // ── CREAR CUENTA DESDE LA APP (2026-09-01) ───────────────────────────
        //
        // Supabase contesta lo MISMO —«ok»— tanto cuando la cuenta queda lista como cuando queda
        // creada pero pendiente de confirmar por correo; lo único que las separa es si vino sesión
        // en la respuesta. Confundirlas mandaría al médico a una consulta con una sesión que no
        // existe, y el 401 llegaría después, sin relación aparente con el alta.
        Prueba("97. una cuenta que pide confirmación NO cuenta como haber entrado", CrearCuentaNoEsEntrar);

        // ── LA IDENTIDAD SE PIDE UNA VEZ (2026-09-01, lo vio el usuario) ─────
        //
        // Con la Dra. Rincón ya dentro de la consulta, la carita le planto encima el popup viejo de
        // «Te damos la bienvenida», pidiéndole otra vez nombre y correo. Son DOS identidades
        // conviviendo: la de verdad —la sesión de Supabase, que sabe quién es y tiene su token— y
        // la vieja de máquina, un correo tecleado en config.json que la carita seguía mirando.
        //
        // Al reemplazar el login se cambió el camino de --consulta y se dejó el de la carita, así
        // que cada uno pregunta por su cuenta. Es el aprendizaje nº16 otra vez: dos identidades de
        // distinta forma, y quien las junta hereda el desacuerdo.
        Prueba("98. con un médico dentro, la identidad NO se vuelve a pedir", LaIdentidadSePideUnaVez);

        // ── EL SELECTOR DE CUENTA (2026-09-01) ────────────────────────────────
        //
        // Click en el nombre del médico → cambiar de cuenta, agregar una nueva, o cerrar sesión.
        // Los tres pasan por cerrar la sesión ACTUAL primero, y eso es justo lo que no se puede
        // hacer a media consulta: cerrar sesión con el micrófono abierto deja un dictado huérfano
        // —nadie lo para, nadie lo guarda— y el médico se queda sin saber que perdió lo grabado.
        Prueba("99. cambiar de cuenta se bloquea mientras se está grabando", NoSeCambiaDeCuentaGrabando);

        // ── GUARDAR EL NOMBRE (2026-09-01, lo vio el usuario) ────────────────
        //
        // «no guardo mi nombre, quedo con mi correo en vez de mi nombre». El cliente solo LEÍA
        // full_name; no había ningún camino para escribirlo. El portal lo hace con la RPC
        // update_own_profile —una lista blanca de columnas (supabase/migrations/
        // 20260829120100_update_own_profile.sql)— y no con un update directo, porque las dos
        // políticas de UPDATE de `profiles` no sirven para autoeditarse sin abrir de más.
        //
        // La RPC pide los SIETE campos del perfil profesional a la vez y los reescribe todos. Si
        // aquí solo se mandara el nombre y el resto en null, se BORRARÍAN el documento, el
        // registro profesional y la especialidad de cualquier médico que ya los tuviera llenos —
        // un dato compartido con el portal, dañado desde un cliente que solo quería cambiar una
        // cosa.
        Prueba("100. guardar el nombre no borra los demás campos del perfil profesional", GuardarNombreNoBorraElResto);

        // ── Enseñar por demostración (spec 005) ──────────────────────────────
        //
        // La cadena demostración→artefacto→reproducción existe entera salvo el EMPAQUETADOR, y el
        // vigía de clics acuña los sintéticos como humanos (nunca consulta LLMHF_INJECTED): la
        // arista falsa del 2026-08-31, medida por el tester. Seis promesas; la 102 cierra el asunto
        // pero la 101 va primero — sin el filtro, cada reproducción re-contamina el grafo.
        //
        // Nacieron como 84-89 y spec 004 en la rama; al rebasar sobre main (2026-09-01) esos
        // números ya los tenía la consulta clínica (#43-#45). Los números no se reciclan: 101-106.
        Console.WriteLine();
        Prueba("101. una pulsación sintética no se acuña como clic humano: el vigía la deja pasar", ElVigiaNoAcunaLoSintetico);
        Prueba("102. una demostración termina en una skill con nombre: los pasos en orden, de dónde parte y a dónde llega cada paso", LaDemoTerminaEnSkill);
        Prueba("103. reproducir una skill exige la llegada de cada paso: acabar en otro sitio no es haberlo hecho", ReproducirExigeLaLlegada);
        Prueba("104. una demo descartada no publica nada: ni video, ni pasos, ni skill", LaDemoDescartadaNoPublica);
        Prueba("105. lo dicho durante la demo viaja con su paso: la frase queda anclada al paso que sonaba", LoDichoViajaConSuPaso);
        Prueba("106. las skills enseñadas se anuncian al cerebro: se piden por nombre, y el catálogo dice cuándo usarlas", LasSkillsSeAnuncian);

        // ── El aura de aprendizaje (spec 006) ────────────────────────────────
        //
        // Mientras Ü aprende, lo único que lo decía era un botón de 24 px en rojo y la pose de la
        // carita —plegada en una esquina, porque el operador está mirando SAP—, y ese rojo ya
        // significa «fallo» en la misma superficie (UiPalette). La pantalla entera tiene que
        // decirlo, en el color de Ü, sin tapar el trabajo (2026-09-02, pedido por el usuario).
        Console.WriteLine();
        Prueba("107. mientras Ü aprende, los bordes de la pantalla lo dicen: el aura se enciende al grabar, se apaga al terminar y deja el centro limpio", ElAuraDiceQueUAprende);

        // ── El workflow a la mano (spec 007) ─────────────────────────────────
        //
        // Medido contra el Graph vivo el 2026-09-02: de 4 workflows, 3 se llaman «Workflow sin
        // descripción» y el cuarto «User workflow summary:» (un encabezado del LLM). La lista
        // llega del más viejo al más nuevo y el carrusel no mueve el índice al terminar de
        // enseñar: el recién guardado queda al final, sin nombre y sin elegir. Y darle play
        // empieza por pedir el plan a Vercel (342-484 ms en caliente, 2,6 s en frío).
        //
        // La 107 es la del aura (spec 006); los números no se reciclan: 108-111.
        Console.WriteLine();
        Prueba("108. un workflow se presenta por lo que se sabe de él —app, ventana, cuándo, cuántos pasos— y nunca por el relleno del cerebro: «Workflow sin descripción» y «User workflow summary:» no son nombres", ElWorkflowSePresentaPorLoQueSeSabe);
        Prueba("109. el nombre que le pones manda y sobrevive al reinicio: se guarda en disco por id, y sin nombre puesto se vuelve al derivado", ElNombrePuestoMandaYSobrevive);
        Prueba("110. la lista va del más nuevo al más viejo, y el recién enseñado queda elegido; si no se sabe cuál es, se elige el más nuevo", ElRecienEnsenadoQuedaElegido);
        Prueba("111. darle play no vuelve a pedir el plan si ya está en la mano: se pide al elegir el workflow y la corrida lo usa; sin plan a la mano se pide una sola vez", ElPlayNoVuelveAPedirElPlan);

        // ── La nota llega al triage con un ✓ (spec 008) ─────────────────────
        //
        // La experiencia core cerrada por el camino ya validado (2026-09-02, decisión del dueño):
        // los dos batches de la demo del 31 llevan al triage y el rellenador escribe con la nota.
        // Lo que puede fallar en silencio, y por eso va con promesa: mandar una sección que el
        // médico no aprobó, inventar un motivo de consulta, y escribir fuera del triage.
        Console.WriteLine();
        Prueba("112. solo viaja lo marcado: una sección sin ✓ no entra en el envío a SAP", SoloViajaLoMarcado);
        Prueba("113. el motivo de consulta y la conducta salen de las secciones por su título; sin una sección que lo diga, quedan vacíos y no se inventan", MotivoYConductaSalenPorTitulo);
        Prueba("114. fuera de la pantalla del triage no se escribe nada: el envío dice dónde está y para", FueraDelTriageNoSeEscribe);

        // Medido el 2026-09-02 (18:10): de 9 campos del triage se llenaron 3. Las dos causas NO eran
        // que al modelo le falte capacidad, sino que le faltaba saber de la PANTALLA: la casilla
        // diastólica se llama «/» y se escondía del inventario, y lo enseñado sobre un elemento
        // —los recuerdos, que existen desde el 2026-08-23— no llegaba a la hora de decidir. La
        // apuesta de esta etapa es que el dato vuelva de la prosa con inteligencia, así que se le
        // quitan las vendas al modelo en vez de sustituirlo por reglas de dominio.
        Prueba("115. lo que me enseñaron sobre un campo viaja con él cuando se decide qué escribir", LoEnsenadoViajaConSuCampo);
        Prueba("116. una casilla que no se nombra a sí misma se ofrece diciendo de quién es; la que nadie puede nombrar no se ofrece", LaCasillaSinNombreSePresenta);
        Prueba("117. enseñar funciona dentro de SAP: un campo se nombra por lo que ve el terreno, y dos que se llaman igual no se adivinan", EnsenarAlcanzaADentroDeSap);

        // EL DESPACHO ENTRE MUNDOS SE COMPLETA. La regla de la casa (José David, 2026-08-26) dice
        // que vive en UN solo sitio y que nadie más sabe en qué mundo mira: se migraron observar,
        // pulsar y escribir, y ahí se paró. Señalar e iluminar seguían preguntándole a UIA —ocho
        // sitios, contados con grep— y dentro de SAP eso devuelve el Pane opaco: señalar la casilla
        // de la presión contestó «Gos Container», y los seis recuerdos del triage no se encendieron
        // ninguno (medido por el dueño el 2026-09-02).
        Prueba("118. lo que se señala dentro de SAP lo dice SAP, y si SAP no lo reconoce se dice que no se sabe en vez de devolver el panel que lo contiene todo", SenalarDentroDeSapLoDiceSap);
        Prueba("119. un recuerdo se ilumina en el mundo del que es: el selector decide quién sabe dónde está en pantalla", CadaCajaLaDaSuMundo);

        // ── Lo enseñado alimenta los batches (spec 009) ──────────────────────
        //
        // VALIDADO EL 2026-09-02 sobre el triage real: con seis recuerdos colgados de seis campos el
        // relleno pasó de 3 campos de 9 a todos, y sin una sola regla de dominio en el código. Esta
        // tanda convierte el hallazgo en arquitectura — la enseñanza produce OBJETIVOS y RECUERDOS, y
        // hay UN solo ejecutor (map_batch) con computer use como rescate hablando identidades.
        //
        // LOS NÚMEROS 112-119 NO ESTÁN LIBRES: son de la spec 008 (la nota llega al triage), que vive
        // en la rama `jose/la-nota-llega-al-triage` y todavía no entró a main. Los números no se
        // reciclan, así que esta tanda empieza en 120 y el hueco se cierra cuando la 008 llegue.
        Console.WriteLine();
        Prueba("120. cuando un batch para, el relato lleva el objetivo que faltó y lo accionable por identidad: el rescate no recibe «termina la tarea»", ElRescateRecibeElObjetivo);
        Prueba("121. el aterrizaje del rescate lo juzga la misma compuerta que el batch: «terminé» no es un veredicto", ElAterrizajeLoJuzgaLaCompuerta);
        Prueba("122. reproducir una skill es un batch: el mismo ejecutor, la misma compuerta, la misma cuenta", ReproducirUnaSkillEsUnBatch);
        Prueba("123. un valor tecleado en la demo jamás se reproduce: es un hueco, y sin dato queda vacío", ElValorDeLaDemoNoSeReproduce);
        Prueba("124. lo dicho durante la demo se vuelve recuerdo del elemento DONDE se dijo, sin que nadie señale", LoDichoSeVuelveRecuerdo);
        Prueba("125. un recuerdo creado al comprobar se cuelga del elemento que el paso TOCÓ, nunca de uno que el modelo elija", ElRecuerdoSeCuelgaDeLoQueElPasoToco);
        Prueba("126. comprobar no graba: una skill que termina en Grabar se recorre hasta esa puerta y ahí se detiene", ComprobarNoGraba);
        Prueba("127. una skill sin comprobar no se ejecuta: el catálogo la anuncia como pendiente y el «no» dice qué falta", SinComprobarNoSeEjecuta);
        Prueba("128. si el video no se pudo procesar, la comprobación lo dice y sigue con lo dicho: no se inventa contexto ni se calla el hueco", ElVideoQueFalloSeDice);

        // EL MODELO INTERPRETA, LAS REGLAS NO (decisión del dueño, 2026-09-03): qué de lo tecleado
        // es un dato y qué es parte de la tarea lo sabe quien entiende el contexto entero de la
        // demo, no un `if`. La regla «lo que narras es un dato» se queda como RESPALDO, no como ley.
        Prueba("129. la interpretación del modelo solo puede hablar de lo que la demo TOCÓ: lo que nombre de más se descarta", ElModeloNoInventaCampos);
        Prueba("130. si el cerebro no contesta, lo enseñado no se pierde: se sigue con lo que se narró y se dice que el modelo no opinó", SinCerebroNoSePierdeLoEnsenado);

        // SEGUNDA TANDA (2026-09-03), y estas cinco no salen de una idea: salen de la PRIMERA
        // corrida real de «Comprobar aprendizaje». El dueño lo pulsó cuatro veces y su lectura fue
        // «no hizo nada». El log dice algo peor: sí corrió, hizo CERO pasos de cuatro, y estampó la
        // skill como COMPROBADA. Un juez que absuelve sin mirar — el aprendizaje nº10 con otra cara.
        Prueba("131. comprobar no certifica un camino que no se anduvo: si el recorrido no completó el plan, la skill sigue SIN comprobar y se dice hasta dónde llegó", ComprobarNoCertificaLoQueNoAnduvo);
        Prueba("132. una tecla que sigue a lo tecleado viaja CON ese paso y hereda su llegada: «key:enter» deja de ser una puerta que nadie puede abrir", LaTeclaViajaConSuPaso);
        Prueba("133. lo tecleado va a SU campo: el paso lleva el selector donde la demo escribió, y quien escribe lo recibe en vez de adivinar el foco", LoTecleadoVaASuCampo);
        Prueba("134. lo que el modelo interpreta manda sobre la regla del narrado, y sin interpretación la regla sigue mandando", LoInterpretadoMandaSobreLaRegla);
        Prueba("135. al video se le pregunta por los pasos de ESTA demo: lo que no tocó no viaja, y lo que conteste de más se descarta antes de tocar la skill", AlVideoSeLePreguntaPorEstosPasos);
        Prueba("136. la interpretación no cuelga del video: si el video no llegó se pregunta con los pasos y lo narrado, y solo si eso tampoco llega manda la regla", LaInterpretacionNoCuelgaDelVideo);
        Prueba("137. empezar a grabar no corre contra un reloj: se espera a que la app que vas a enseñar esté delante, se dice mientras se espera, y nunca se dice «grabando» sin estarlo", ElArranqueEsperaNoCuentaAtras);

        // TERCERA TANDA (2026-09-03, noche). El dueño, tras la primera demo con video: «siento que
        // todavía estamos tratando la enseñanza como si fueran workflows». Tenía razón: reproducir
        // pasos ES un guion. Enseñar es dar CONTEXTO; comprobar es ALCANZAR EL OBJETIVO con él,
        // colgando recuerdos por el camino para que el batch de después lo planifique entero.
        Prueba("138. mientras enseñas, Ü no tiene manos: ninguna herramienta que mueva la pantalla está en su catálogo, sus instrucciones son las de un aprendiz que escucha, y al terminar vuelve todo", MientrasEnsenasNoTieneManos);
        Prueba("139. comprobar es un ENCARGO al piloto con el objetivo y todo el contexto de la demo, nunca un guion que se reproduce: pide un recuerdo por elemento usado, para en las puertas peligrosas, y nombra destinos pero jamás valores", ComprobarEsUnEncargoNoUnGuion);
        Prueba("140. la llegada del último paso es donde ACABÓ la demo, y se sabe, no se estima: una skill siempre tiene un destino que exigir", ElUltimoPasoTieneDestino);

        // CUARTA TANDA (2026-09-03, 21:00), tras la PRIMERA comprobación que llega al final: 25 s
        // del menú de SAP a la ficha de triage, aterrizaje verificado. El dueño pidió tres cosas y
        // las tres eran justas — y una era culpa de una frase que yo mismo puse en el encargo.
        Prueba("141. comprobar va elemento a elemento y no en tanda: el encargo no ofrece el batch, y por cada elemento pide decir en voz qué entendió, actuar, y colgar ahí el recuerdo", ComprobarVaElementoAElemento);
        Prueba("142. durante la comprobación Ü habla con SU voz: se le puede pedir que diga algo, y eso viaja con la petición de respuesta", LaVozDeLaComprobacionEsLaDeU);
        Prueba("143. el recuerdo que se cuelga es lo ENTENDIDO, no la transcripción: donde el modelo tiene un significado para ese elemento, gana al balbuceo de la demo", ElRecuerdoEsLoEntendidoNoLoBalbuceado);
        Prueba("144. la caja de un botón de barra de SAP la da UIA y su identidad la da SAP: se casa por el texto que SAP declara, y si no se encuentra no se dibuja nada", LaCajaDelBotonDeBarraLaDaUia);
        Prueba("145. la caja de un selector de SAP se resuelve en UN solo sitio: los dos caminos —la vista de recuerdos y el señalar de uno— dan la MISMA respuesta, y un elemento sin caja no se ilumina", LaCajaDeSapSeResuelveEnUnSitio);

        // UN SOLO MICRÓFONO PARA TODA LA APP (2026-09-04). El médico elige el collar en la ventana
        // de la consulta y al ponerse a ENSEÑAR vuelve a hablarle al micrófono del portátil, porque
        // la elección era privada de esa ventana.
        Prueba("146. de dónde entra el audio se elige UNA vez y vale para toda la app: lo elegido en la consulta manda también al enseñar y al hablar con Ü, y pedir lo que ya está puesto no corta nada", UnSoloMicrofonoParaTodaLaApp);

        // UN CLIC HABLA, Y EL PANEL VIVE A LA DERECHA (spec 010, 2026-09-05). El gesto más usado de
        // la aplicación —hablarle— estaba detrás de un doble clic, y el clic simple abría un panel.
        // Se cambian los dos: el clic abre el micrófono, y el panel se muda al borde derecho donde
        // está siempre y se despliega al pasar el cursor.
        Prueba("147. sin gesto de doble toque, el toque simple no espera a nadie: el micrófono abre en el acto, y la espera de 250 ms solo existe mientras haya un segundo toque que distinguir", ElToqueSimpleNoEsperaANadie);
        Prueba("148. el muelle se despliega porque el cursor está encima y se pliega al irse — pero no mientras haya algo abierto que se perdería: una conversación en marcha o el cursor dentro de lo desplegado lo mantienen abierto", ElMuelleNoSeCierraSobreLoQueEstasHaciendo);
        Prueba("149. soltar la carita encima del muelle la guarda, y soltarla en cualquier otro sitio no: la caja que decide es la del muelle desplegado, y se juzga con el punto donde se soltó", SoltarlaEnElMuelleLaGuarda);
        Prueba("150. sacada del muelle, la carita vuelve al punto donde se soltó y nunca fuera de la pantalla: un escondite del que se sale a un sitio que no ves no es un escondite, es una pérdida", SacadaVuelveDondeLaSueltasYSeVe);

        // LOS DISPOSITIVOS DEL SELECTOR (spec 010, segunda ronda, 2026-09-05). Al borrar el panel
        // viejo del collar se quedaron sin puerta DOS cosas —olvidar un collar y ver su estado—, y
        // el dueño pidió que vivieran donde de verdad se elige el micrófono. De paso, poder
        // nombrarlos: «Collar Omi» no distingue un collar de otro.
        Prueba("151. un dispositivo enlazado se puede nombrar, y el nombre sobrevive al reinicio: se guarda en disco por dispositivo, y poner vacío QUITA el nombre y devuelve el de fábrica", ElDispositivoSePuedeNombrar);
        Prueba("152. la lista de enlazados dice lo que HAY: solo collares que llegaron a conectarse con este computador —el teléfono es un canal, no un aparato de esta máquina— y olvidar uno lo quita de la lista Y borra su nombre", OlvidarUnDispositivoNoDejaHuerfanos);
        Prueba("153. el collar distingue querer de tener: elegirlo en el menú es una intención y no lo mete en la lista de enlazados; solo entra cuando contestó de verdad, y olvidarlo lo saca", QuererUnCollarNoEsTenerlo);
        Prueba("154. lo elevado reserva el hueco que su sombra necesita: una sombra recortada contra el borde de su ventana no dibuja profundidad, dibuja un corte — y el hueco es mayor por abajo, que es hacia donde cae la luz de este estudio", LaSombraSeDibujaEntera);
        Prueba("155. el globo es para conversar, no para informar: lo abre quien lo pide y una pregunta que hay que contestar, nunca el progreso; un fallo no lo abre pero sí saca el panel, porque un fallo que nadie ve es una app que no hace nada", ElGloboNoSeAbreSolo);
        Prueba("156. un botón que abre una ventana también la cierra, y sabe distinguir minimizada de delante: con la ventana al frente la esconde, y en cualquier otro estado —minimizada, detrás, escondida— la trae en vez de no hacer nada", ElBotonDeUnaVentanaAlterna);
        Prueba("157. elegir una fuente SUELTA las demás: con el computador o el teléfono elegidos, el collar se apaga en vez de quedarse conectado entregando audio por detrás — lo que la interfaz dice que te oye es lo que te oye", ElegirUnaFuenteSueltaLasDemas);
        Prueba("158. el verde dice quién ENTREGA, no quién está elegido: una fuente elegida y muda se ve distinta de una que está oyendo, porque la única prueba de que hay micrófono es que llegue audio", ElVerdeEsDeQuienEntrega);
        Prueba("160. al tirar de la carita guardada, aparece BAJO el cursor y no donde estaba escondida: un objeto que reaparece a diez centímetros de tu mano es un objeto que hay que volver a agarrar", LaCaritaApareceBajoElCursor);
        Prueba("161. Ü no anuncia lo que va a hacer: sus instrucciones prohíben el futuro, mandan hablar en pasado y solo cuando hay algo que decir", UNoAnunciaLoQueVaAHacer);
        Prueba("159. la ventana se agarra por el borde que se VE y no por el de su ventana —que vive 24 px más afuera, en el hueco de la sombra—, y en una esquina manda la esquina: redimensionar en una dirección donde se esperaban dos se siente como que la ventana se resiste", LaVentanaSeAgarraPorDondeSeVe);


        // FUERA LAS PASTILLAS Y LOS CARTELES (spec 011, 2026-09-06). Rescate selectivo de la rama
        // 008 tras probarla a mano: entran las pastillas fuera, el halo alrededor de la carita y
        // escribirle sin boton; se quedan fuera el anillo, su modelo de gestos —main ya decidio lo
        // contrario en la 147— y los ojos siguiendo al cursor. Y de paso se apagan los 44 textos
        // que asomaban al pasar el raton, que es la promesa que de verdad cierra el asunto: una
        // lista de 44 tachones deja que el 45 nazca manana.
        Prueba("162. fuera las tres pastillas: la carita no lleva colgando voz, chat ni dictado a SAP — y quitarles la puerta no mata lo que la consulta, la demo y la exportación a HC usan por dentro", FueraLasTresPastillas);
        Prueba("163. el halo de la voz rodea a la carita y cabe entero en el aire que tiene: un halo recortado contra el borde de su ventana no dibuja voz, dibuja un corte — y su color dice por dónde te oyen", ElHaloRodeaALaCarita);
        Prueba("164. ningún elemento de la interfaz muestra texto al pasar el ratón: no queda ni una declaración viva, y el apagado es de una sola pieza para que lo que se escriba mañana tampoco lo muestre", NadieMuestraTextoAlPasarElRaton);
        Prueba("165. la línea «Escríbele…» pide reposo: rozar la carita de camino a otra cosa no la llama, y arrastrarla tampoco — ir a agarrar algo no es ir a escribirle", LaLineaPideReposo);
        Prueba("166. escribir con el ratón sobre la carita abre el globo con esa letra, y ni un atajo, tecla F, Esc, Enter, Tab o flecha se le roba a la app de debajo", EscribirleEsEscribir);

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
            uia: (campo, texto) => { escrito.Add("uia→" + campo + "→" + texto); return true; },
            sap: (campo, texto) => { escrito.Add("sap→" + campo + "→" + texto); return true; });

        Debe(lapiz.Escribe("sap:wnd[0]/tbar[0]/okcd", "NWP1"), "en una sesión SAP se puede escribir");
        Debe(escrito.Count == 1 && escrito[0] == "sap→sap:wnd[0]/tbar[0]/okcd→NWP1",
            "…y va por el lápiz de SAP: al campo por su identidad, no al aire — y el campo llega "
            + "entero, que es la promesa 133");

        donde = "uia://notepad.exe/sin-titulo";
        Debe(lapiz.Escribe("", "hola"), "fuera de SAP también");
        Debe(escrito.Count == 2 && escrito[1] == "uia→→hola",
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

    // ── Enseñar por demostración (spec 005) ──────────────────────────────────

    /// <summary>Capacidad de la spec 005 pedida por nombre; ausente = rojo con su fase.</summary>
    /// <remarks>Por el ensamblado de U.dll y no por «Nucleo.GetType»: en este contrato «Nucleo» es
    /// el ESPACIO DE NOMBRES del terreno, no el alias de ensamblado que tenía el contrato viejo —
    /// la primera versión de este helper confundió los dos y no compilaba (2026-09-01).</remarks>
    private static Type? Cap004(string tipo) => typeof(PulsarSegunElNucleo).Assembly.GetType(tipo);

    private static void ElVigiaNoAcunaLoSintetico()
    {
        // La arista falsa del 2026-08-31, medida: el vigía atribuyó NUESTRO doble sintético a
        // «Fecha de modificación» y acuñó docs→specs por una cabecera de columna. El hook de
        // Windows YA dice quién inyecta (LLMHF_INJECTED en flags); ClickWatcher lo marshalea y
        // jamás lo consulta. Sin este filtro, reproducir una skill re-contamina el grafo en cada
        // corrida — por eso esta promesa va ANTES que el empaquetador.
        var m = Cap004("U.WindowsClient.Navigation.ClickWatcher")
            ?.GetMethod("EsSintetico", new[] { typeof(uint) });
        Debe(m != null, "todavía no existe «ClickWatcher.EsSintetico» (fase 1 de la spec 005). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (m == null) return;

        Debe(m.Invoke(null, new object[] { 0x1u }) is true,
            "un golpe con LLMHF_INJECTED es sintético: el vigía lo deja pasar sin atribuirlo");
        Debe(m.Invoke(null, new object[] { 0x2u }) is true,
            "y el inyectado a menor integridad (bit 1) también: inyectado es inyectado");
        Debe(m.Invoke(null, new object[] { 0x0u }) is false,
            "un golpe sin banderas de inyección es del humano, y ese SÍ enseña");
    }

    private static void LaDemoTerminaEnSkill()
    {
        // EL ESLABÓN QUE NO EXISTE (2026-09-01, medido por cinco lectores): el grafo guarda
        // aristas sueltas sin orden ni nombre, y el único empaquetador manda PlanStep a un formato
        // que map_batch no lee. La skill es el artefacto: nombre, disparador, de dónde parte, y
        // los pasos EN EL ORDEN de la demo con la llegada que la demo vio.
        var tSkill = Cap004("U.WindowsClient.Navigation.SkillEnsenada");
        var tPaso = Cap004("U.WindowsClient.Navigation.PasoEnsenado");
        var emp = tSkill?.GetMethods().FirstOrDefault(m => m.Name == "Empaquetar" && m.GetParameters().Length == 4);
        Debe(tSkill != null && tPaso != null && emp != null,
            "todavía no existen «SkillEnsenada/PasoEnsenado/Empaquetar» (fase 2 de la spec 005). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (tSkill == null || tPaso == null || emp == null) return;

        object P(string exit, string texto, string llegada, string dicho)
            => Activator.CreateInstance(tPaso, exit, texto, llegada, dicho)!;
        var lista = (System.Collections.IList)Activator.CreateInstance(
            typeof(List<>).MakeGenericType(tPaso))!;
        lista.Add(P("sap:wnd[0]#tbbtn=NV44", "", "sapgui://QAS/NV2000", "aquí se crea el triage"));
        lista.Add(P("", "12345", "", ""));   // lo tecleado, que solo el recorder ve

        var skill = emp.Invoke(null, new object?[] { "radicar-factura", "cuando pidan radicar", "sapgui://QAS/NWP1", lista });
        Debe(skill != null, "una demo con pasos SÍ produce skill");
        if (skill == null) return;

        var pasos = (System.Collections.IList)tSkill.GetProperty("Pasos")!.GetValue(skill)!;
        string ExitDe(int i) => (string)tPaso.GetProperty("Exit")!.GetValue(pasos[i])!;
        Debe((string)tSkill.GetProperty("Nombre")!.GetValue(skill)! == "radicar-factura"
             && (string)tSkill.GetProperty("DondeEmpieza")!.GetValue(skill)! == "sapgui://QAS/NWP1",
            "la skill lleva su nombre y de dónde parte: sin eso no es invocable ni reproducible");
        Debe(pasos.Count == 2 && ExitDe(0).Contains("NV44")
             && (string)tPaso.GetProperty("Texto")!.GetValue(pasos[1])! == "12345",
            "los pasos quedan EN EL ORDEN de la demo, con el valor tecleado incluido");
        Debe((string)tPaso.GetProperty("Llegada")!.GetValue(pasos[0])! == "sapgui://QAS/NV2000",
            "y cada paso que navegó recuerda A DÓNDE llegó: esa llegada es lo que la reproducción exigirá");

        var vacia = emp.Invoke(null, new object?[] { "x", "y", "sitio",
            Activator.CreateInstance(typeof(List<>).MakeGenericType(tPaso)) });
        Debe(vacia == null,
            "una demo SIN pasos no produce skill: empaquetar el vacío sería una skill que no enseña nada");
    }

    private static void ReproducirExigeLaLlegada()
    {
        // El pendiente histórico nº1 («terminé» no es un veredicto) hecho promesa: la skill trae
        // la llegada porque la demo la vio, y el batch la exige porque puede — el destino ya vive
        // en la arista y nadie lo pedía (medido 2026-09-01).
        var pLlegada = typeof(RecorrerSegunElNucleo.Paso).GetProperty("Llegada");
        Debe(pLlegada != null,
            "todavía no existe «Paso.Llegada» en el batch (fase 3 de la spec 005). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (pLlegada == null) return;

        // La ruta real lleva a OTRO sitio distinto del prometido.
        var g = new Nucleo.Grafo();
        g.Observar("uia://x.exe/a", new[] { new Nucleo.Elemento("s:1", "Uno", "Button") });
        var rutas = new Dictionary<string, string> { ["uia://x.exe/a|s:1"] = "uia://x.exe/OTRO" };
        var (batch, _, _) = BatchCon(g, "uia://x.exe/a", rutas);

        var paso = (RecorrerSegunElNucleo.Paso)Activator.CreateInstance(
            typeof(RecorrerSegunElNucleo.Paso), "Uno", "", "uia://x.exe/PROMETIDO", "")!;
        var r = batch.Recorre(new[] { paso });
        Debe(!r.Termino && r.Hechos == 0,
            $"aterrizar en «OTRO» cuando la skill prometía «PROMETIDO» NO cuenta como hecho "
            + $"(dijo hechos={r.Hechos}, terminó={r.Termino}): contar eso sería el «29 de 30» otra vez");

        // Y cuando la llegada coincide, el paso cuenta.
        var g2 = new Nucleo.Grafo();
        g2.Observar("uia://x.exe/a", new[] { new Nucleo.Elemento("s:1", "Uno", "Button") });
        var (batch2, _, _) = BatchCon(g2, "uia://x.exe/a",
            new Dictionary<string, string> { ["uia://x.exe/a|s:1"] = "uia://x.exe/PROMETIDO" });
        var r2 = batch2.Recorre(new[] { (RecorrerSegunElNucleo.Paso)Activator.CreateInstance(
            typeof(RecorrerSegunElNucleo.Paso), "Uno", "", "uia://x.exe/PROMETIDO", "")! });
        Debe(r2.Termino && r2.Hechos == 1,
            "llegar a donde la demo llegó SÍ es haberlo hecho: la exigencia no vuelve imposible lo posible");
    }

    private static void LaDemoDescartadaNoPublica()
    {
        // «Me equivoqué» hoy sube el video igual: DiscardAsync existe con CERO llamadores y el
        // botón está Collapsed (medido 2026-09-01). La decisión se juzga aquí; el borrado real de
        // archivos es nivel 4.
        var t = Cap004("U.WindowsClient.Teach.SesionDeDemo");
        Debe(t != null, "todavía no existe «SesionDeDemo» (fase 4 de la spec 005). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null) return;

        var descartada = Activator.CreateInstance(t)!;
        t.GetMethod("Descartar")!.Invoke(descartada, null);
        Debe(t.GetMethod("Entrega")!.Invoke(descartada, null) == null,
            "descartada, la sesión no entrega NADA: ni video, ni pasos, ni skill — descartar que "
            + "publica es peor que no poder descartar");

        var cerrada = Activator.CreateInstance(t)!;
        t.GetMethod("Cerrar")!.Invoke(cerrada, null);
        Debe(t.GetMethod("Entrega")!.Invoke(cerrada, null) != null,
            "y cerrada de verdad, SÍ entrega: la puerta existe para la demo mala, no para todas");

        t.GetMethod("Descartar")!.Invoke(cerrada, null);
        Debe(t.GetMethod("Entrega")!.Invoke(cerrada, null) != null,
            "descartar DESPUÉS de cerrar no des-publica: lo entregado ya no es de la sesión");
    }

    private static void LoDichoViajaConSuPaso()
    {
        // «Aquí va el NIT» solo sirve colgado del campo que sonaba. Hoy la frase sobrevive como
        // log y la hora del clic es la de la RESOLUCIÓN, no la del golpe (medido 2026-09-01).
        var tA = Cap004("U.WindowsClient.Navigation.AncladorDeVoz");
        var tF = Cap004("U.WindowsClient.Navigation.FraseDicha");
        var m = tA?.GetMethod("Ancla");
        Debe(tA != null && tF != null && m != null,
            "todavía no existe «AncladorDeVoz/FraseDicha» (fase 5 de la spec 005). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (tA == null || tF == null || m == null) return;

        var frases = (System.Collections.IList)Activator.CreateInstance(
            typeof(List<>).MakeGenericType(tF))!;
        frases.Add(Activator.CreateInstance(tF, "aquí se crea el triage", 5_200L)!);
        frases.Add(Activator.CreateInstance(tF, "esto es del hospital en general", 900_000L)!);

        var horas = new List<long> { 5_000L, 60_000L };   // dos pasos, con su hora del golpe
        var r = m.Invoke(null, new object?[] { frases, horas })!;
        var porPaso = (IReadOnlyList<string>)r.GetType().GetProperty("DichoPorPaso")!.GetValue(r)!;
        string contexto = (string)r.GetType().GetProperty("Contexto")!.GetValue(r)!;

        Debe(porPaso.Count == 2 && porPaso[0].Contains("triage"),
            "la frase que sonaba con el paso queda EN ese paso");
        Debe(porPaso[1].Length == 0 && contexto.Contains("hospital"),
            "y la frase lejos de todo paso queda como CONTEXTO general: inventarle un ancla sería "
            + "colgar «aquí va el NIT» de un botón cualquiera");
    }

    private static void LasSkillsSeAnuncian()
    {
        // Sin catálogo no hay «reproduce radicar factura»: el cerebro no puede pedir lo que no se
        // anuncia. El catálogo dice QUÉ hay y CUÁNDO usarla (la description-disparador, el patrón
        // de las skills de Claude), y con cero skills dice cero — no se inventa.
        var tSkill = Cap004("U.WindowsClient.Navigation.SkillEnsenada");
        var guardar = tSkill?.GetMethod("Guardar");
        var catalogo = tSkill?.GetMethod("Catalogo");
        Debe(tSkill != null && guardar != null && catalogo != null,
            "todavía no existen «Guardar/Catalogo» (fase 6 de la spec 005). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (tSkill == null || guardar == null || catalogo == null) return;

        string carpeta = Path.Combine(_raiz, "skills-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(carpeta);
        Debe(((System.Collections.IList)catalogo.Invoke(null, new object[] { carpeta })!).Count == 0,
            "con cero skills el catálogo dice cero: anunciar lo que no hay es inventar");

        var tPaso = Cap004("U.WindowsClient.Navigation.PasoEnsenado")!;
        var lista = (System.Collections.IList)Activator.CreateInstance(
            typeof(List<>).MakeGenericType(tPaso))!;
        lista.Add(Activator.CreateInstance(tPaso, "s:x", "", "uia://x.exe/b", "")!);
        var skill = tSkill.GetMethods().FirstOrDefault(m => m.Name == "Empaquetar" && m.GetParameters().Length == 4)!.Invoke(null,
            new object?[] { "radicar-factura", "cuando pidan radicar una factura", "uia://x.exe/a", lista })!;
        guardar.Invoke(skill, new object[] { carpeta });

        var cat = (System.Collections.IList)catalogo.Invoke(null, new object[] { carpeta })!;
        Debe(cat.Count == 1, $"guardada una, el catálogo anuncia una (dijo {cat.Count})");
        var e0 = cat[0]!;
        Debe((string)e0.GetType().GetProperty("Nombre")!.GetValue(e0)! == "radicar-factura"
             && ((string)e0.GetType().GetProperty("Description")!.GetValue(e0)!).Contains("radicar"),
            "y la anuncia con nombre y con su CUÁNDO: el disparador es lo que deja pedirla sin verla");
    }

    // ── La nota llega al triage con un ✓ (spec 008): las promesas ────────────

    private static U.WindowsClient.Clinical.NotaClinica NotaDeTres() =>
        new("", new[]
        {
            new U.WindowsClient.Clinical.SeccionDeNota("motivo_consulta", "Motivo de consulta", "dolor torácico opresivo de dos horas"),
            new U.WindowsClient.Clinical.SeccionDeNota("hallazgos", "Hallazgos y datos objetivos", "peso 70 kg, talla 170 cm, TA 120/80"),
            new U.WindowsClient.Clinical.SeccionDeNota("plan", "Plan y recomendaciones", "reposo y control en 48 horas"),
        }, Array.Empty<string>(), Array.Empty<string>());

    private static void SoloViajaLoMarcado()
    {
        // El ✓ es la aprobación del médico. Un envío que mandara la nota entera por comodidad
        // escribiría en la historia clínica secciones que nadie leyó — y eso se vería igual que
        // un envío correcto. Se juzga por lo que NO viaja.
        var t = Capacidad("U.WindowsClient.Clinical.Encargo");
        var de = t?.GetMethod("De");
        Debe(t != null && de != null, "todavía no existe «Clinical.Encargo.De» (fase 1 de la spec 008). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || de == null) return;

        var nota = NotaDeTres();
        var encargo = de.Invoke(null, new object[] { nota, new[] { "motivo_consulta", "plan" } })!;
        var secciones = (System.Collections.IList)t.GetProperty("Secciones")!.GetValue(encargo)!;
        string texto = (string)t.GetProperty("Texto")!.GetValue(encargo)!;

        Debe(secciones.Count == 2, $"marcadas dos, viajan dos (viajaron {secciones.Count})");
        Debe(texto.Contains("Motivo de consulta:") && texto.Contains("reposo y control"),
            "y viajan con su título delante y su texto tal cual: el emparejador necesita saber qué es cada cosa");
        Debe(!texto.Contains("peso 70"),
            "la sección SIN ✓ no entra: «Hallazgos» no se marcó y su peso no puede llegar a SAP");

        var vacio = de.Invoke(null, new object[] { nota, Array.Empty<string>() })!;
        Debe(t.GetProperty("EstaVacio")!.GetValue(vacio) is true,
            "sin nada marcado el encargo está vacío: no hay envío que hacer, y se sabe antes de tocar SAP");
    }

    private static void MotivoYConductaSalenPorTitulo()
    {
        // Los dos editores de texto libre del triage no los ve el rellenador (son shells
        // GuiTextedit; la demo del 31 los escribía aparte con frases fijas). Se reparten por el
        // título de la sección, y sin sección que lo diga quedan vacíos: un motivo de consulta
        // inventado es peor que uno en blanco.
        var t = Capacidad("U.WindowsClient.Clinical.EditoresDelTriage");
        var repartir = t?.GetMethod("Repartir");
        Debe(t != null && repartir != null, "todavía no existe «Clinical.EditoresDelTriage.Repartir» (fase 2 de la spec 008). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || repartir == null) return;

        string De(object reparto, string prop) => (string)reparto.GetType().GetProperty(prop)!.GetValue(reparto)!;

        var tres = repartir.Invoke(null, new object[] { NotaDeTres().Secciones })!;
        Debe(De(tres, "Motivo") == "dolor torácico opresivo de dos horas",
            "«Motivo de consulta» va al editor de motivo, tal cual se dijo");
        Debe(De(tres, "Conducta") == "reposo y control en 48 horas",
            "y «Plan y recomendaciones» va a Conducta");

        var soloHallazgos = repartir.Invoke(null, new object[]
        {
            new[] { new U.WindowsClient.Clinical.SeccionDeNota("hallazgos", "Hallazgos", "peso 70 kg") },
        })!;
        Debe(De(soloHallazgos, "Motivo").Length == 0 && De(soloHallazgos, "Conducta").Length == 0,
            "sin una sección que lo diga, los dos quedan vacíos: no se pega la nota entera en «Motivo» por llenar algo");
    }

    private static void FueraDelTriageNoSeEscribe()
    {
        // El guardián existía (RellenadorSap.EsLaPantallaDeTriage, lo usa el exportador desde el
        // 2026-08-25) y nadie lo prometía: un ✓ que llegara a otra pantalla escribiría datos
        // clínicos en el formulario que fuera. Se pregunta a SAP, no al foco.
        var t = Capacidad("U.WindowsClient.Clinical.EnvioAlTriage");
        var puede = t?.GetMethod("PuedeEscribir");
        Debe(t != null && puede != null, "todavía no existe «Clinical.EnvioAlTriage.PuedeEscribir» (fase 3 de la spec 008). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || puede == null) return;

        (bool Puede, string Motivo) V(string donde)
        {
            var v = puede.Invoke(null, new object[] { donde })!;
            return ((bool)v.GetType().GetProperty("Puede")!.GetValue(v)!,
                    (string)v.GetType().GetProperty("Motivo")!.GetValue(v)!);
        }

        Debe(V("sapgui://QAS/NWP1/SAPLY000/0100").Puede, "con el triage delante se escribe");
        var puesto = V("sapgui://QAS/NWP1/SAPLN_WP_FRAMEWORK/0100");
        Debe(!puesto.Puede && puesto.Motivo.Contains("SAPLN_WP_FRAMEWORK"),
            "en el puesto de trabajo NO se escribe, y el motivo nombra la pantalla en la que está");
        Debe(!V("uia://explorer.exe/descargas").Puede, "fuera de SAP tampoco");
        Debe(!V("").Puede, "y sin saber qué muestra SAP, menos: no se escribe a ciegas");
    }

    private static void LoEnsenadoViajaConSuCampo()
    {
        // EL CANAL ENTRE ENSEÑAR Y HACER. Los recuerdos existen desde el 2026-08-23 y hasta hoy no
        // tocaban lo que Ü escribe: se contaban al llegar a una pantalla y ahí morían. Esta promesa
        // es la que convierte una enseñanza en comportamiento, sin una sola regla de dominio.
        var t = Capacidad("U.WindowsClient.Clinical.LoQueVeElEmparejador");
        var etiquetaCon = t?.GetMethod("EtiquetaCon");
        var merece = t?.GetMethod("MereceOfrecerse");
        var identidad = t?.GetMethod("MismaIdentidad");
        Debe(t != null && etiquetaCon != null && merece != null && identidad != null,
            "todavía no existe «Clinical.LoQueVeElEmparejador» (fase 5 de la spec 008). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || etiquetaCon == null || merece == null || identidad == null) return;

        string Con(string etq, string ant, string rec) =>
            (string)etiquetaCon.Invoke(null, new object[] { etq, ant, rec })!;

        string conRecuerdo = Con("Frec. Cardíaca", "", "se escribe sin decimales y en pulsaciones por minuto");
        Debe(conRecuerdo.Contains("Frec. Cardíaca") && conRecuerdo.Contains("pulsaciones por minuto"),
            "el campo llega con su etiqueta Y con lo que se enseñó sobre él: si lo enseñado no viaja, "
            + "enseñar no cambia nada de lo que Ü hace");
        Debe(Con("Peso", "", "") == "Peso",
            "y un campo sin nada enseñado llega tal cual: no se le inventa contexto");

        // Un campo que NADIE sabe nombrar entra igual si alguien le enseñó qué es: la enseñanza
        // basta por sí sola para hacerlo accionable.
        Debe((bool)merece.Invoke(null, new object[] { "", "", "aquí va la frecuencia cardíaca" })! ,
            "lo enseñado hace ofrecible un campo que ni su etiqueta ni su vecina nombran");

        Debe((string)identidad.Invoke(null, new object[] { "sap:wnd[0]/usr/txtX" })!
             == (string)identidad.Invoke(null, new object[] { "wnd[0]/usr/txtX" })!,
            "y las dos formas del mismo selector —la del grafo, con «sap:», y la del formulario, sin "
            + "él— se comparan por el mismo camino: si no, el recuerdo nunca casaría con su campo y "
            + "el fallo sería mudo (aprendizaje nº16)");
    }

    private static void LaCasillaSinNombreSePresenta()
    {
        // La diastólica de «Presión Arterial» se llama «/». Estaba EXCLUIDA del inventario para que
        // un número suelto no cayera ahí, y el efecto real era que la presión no se podía llenar
        // entera NUNCA. Esconder un campo no es enseñar dónde va.
        var t = Capacidad("U.WindowsClient.Clinical.LoQueVeElEmparejador");
        var merece = t?.GetMethod("MereceOfrecerse");
        var etiquetaCon = t?.GetMethod("EtiquetaCon");
        Debe(t != null && merece != null && etiquetaCon != null,
            "todavía no existe «Clinical.LoQueVeElEmparejador» (fase 5 de la spec 008). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || merece == null || etiquetaCon == null) return;

        bool Merece(string etq, string ant) => (bool)merece.Invoke(null, new object[] { etq, ant, "" })!;
        string Con(string etq, string ant) => (string)etiquetaCon.Invoke(null, new object[] { etq, ant, "" })!;

        Debe(Merece("/", "Presión Arterial"),
            "la casilla «/» que sigue a «Presión Arterial» SÍ se ofrece: escondida, la diastólica no "
            + "se podía llenar nunca");
        string presentada = Con("/", "Presión Arterial");
        Debe(presentada.Contains("Presión Arterial") && presentada.Contains("/"),
            "y se ofrece diciendo de quién es la casilla, que es lo que una persona deduce mirando");
        Debe(Con("Peso", "Talla") == "Peso",
            "una etiqueta que ya nombra su campo NO se ensucia con la vecina: «Peso» es «Peso», no «Peso, que sigue a Talla»");
        Debe(!Merece("/", ""),
            "pero una casilla que nadie puede nombrar —ni ella ni su vecina ni una enseñanza— no se "
            + "ofrece: el modelo no tendría con qué decidir y adivinaría");
    }

    private static void EnsenarAlcanzaADentroDeSap()
    {
        // «Esto es X» por NOMBRE pasaba siempre por el lector de UIA, y dentro de SAP UIA ve un Pane
        // opaco: enseñar un campo del triage era imposible salvo con el cursor encima. El terreno sí
        // los ve, con su identidad «sap:...», desde la tanda de T1.
        var t = Capacidad("U.WindowsClient.Navigation.ElCampoQueNombras");
        var resolver = t?.GetMethod("Resolver");
        Debe(t != null && resolver != null,
            "todavía no existe «Navigation.ElCampoQueNombras» (fase 6 de la spec 008). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || resolver == null) return;

        var puertas = new List<(string, string, string)>
        {
            ("sap:wnd[0]/usr/txtFRCAR", "Frec. Cardíaca", "GuiTextField"),
            ("sap:wnd[0]/usr/txtFRRES", "Frec. Respiratoria", "GuiTextField"),
            ("sap:wnd[0]/usr/txtPESO", "Peso", "GuiTextField"),
        };
        object? R(string nombre) => resolver.Invoke(null, new object[] { nombre, puertas });

        // En una ValueTuple, «Item1» es un CAMPO y no una propiedad: pedirlo con GetProperty
        // devuelve null y el arnés revienta con un NullReference que no habla de la promesa.
        var frec = R("Frec. Cardíaca");
        Debe(frec != null && ((string)frec.GetType().GetField("Item1")!.GetValue(frec)!).Contains("FRCAR"),
            "un campo del formulario de SAP se encuentra por su nombre, aunque UIA no vea nada ahí dentro");

        var peso = R("peso");
        Debe(peso != null, "y sin exigir que se diga clavado: tildes y mayúsculas aparte");

        Debe(R("Frec.") == null,
            "«Frec.» nombra a DOS campos y no se adivina: colgar la enseñanza del elemento equivocado "
            + "es peor que no guardarla — quien enseña se queda tranquilo y el dato acabará en otro sitio");
        Debe(R("Temperatura") == null, "y lo que no está aquí no se resuelve por parecido lejano");
    }

    private static void SenalarDentroDeSapLoDiceSap()
    {
        // El bug, tal cual lo vio el dueño: cursor sobre la casilla de la presión arterial, y la
        // respuesta fue «Gos Container» (Pane) — el contenedor opaco que UIA ve en lugar de SAP.
        // Lo que se juzga aquí es el DESPACHO, que es la parte que puede equivocarse en silencio.
        string donde = "uia://explorer.exe/x";
        var pedidos = new List<string>();
        var senalar = new LoSenaladoPorMundo(() => donde, (x, y) =>
        {
            pedidos.Add($"sap:{x},{y}");
            return x == 100
                ? ("sap:wnd[0]/usr/txtTASIS", "Y0000000-ZTXTTASIS", "GuiTextField",
                   new System.Windows.Rect(90, 40, 60, 20))
                : ((string, string, string, System.Windows.Rect)?)null;
        });

        var fuera = senalar.ElPunto(100, 50);
        Debe(!fuera.MandaSap && pedidos.Count == 0,
            "fuera de SAP no se le pregunta a SAP: lo resuelve el camino de siempre");

        donde = "sapgui://QAS/NWP1/SAPLY000/0001";
        var dentro = senalar.ElPunto(100, 50);
        Debe(dentro.MandaSap && dentro.Que is { } q && q.Etiqueta == "Y0000000-ZTXTTASIS",
            "dentro de SAP contesta SAP, con la identidad del campo y no la del panel");
        Debe(dentro.Que is { } r && r.Caja.Width > 0 && r.Caja.Height > 0,
            "y con su CAJA, porque señalar sin poder iluminar es decir un nombre que nadie puede "
            + "comprobar — el 2026-09-02 contestó «lo estoy iluminando» sin encender nada");

        var noSabe = senalar.ElPunto(900, 900);
        Debe(noSabe.MandaSap && noSabe.Que == null,
            "y si SAP no reconoce lo que hay bajo el punto se DICE que manda SAP y que no lo sabe: "
            + "caer a UIA aquí devolvería justo el «Gos Container» que este despacho viene a evitar");
    }

    private static void CadaCajaLaDaSuMundo()
    {
        // Los seis recuerdos enseñados sobre el triage el 2026-09-02 no se encendieron ninguno: su
        // rectángulo se buscaba siempre en el lector de UIA, que dentro de SAP no ve ni un campo.
        var aQuienSePregunto = new List<string>();
        var geo = new GeometriaPorMundo(
            uia: sel => { aQuienSePregunto.Add("uia"); return new System.Windows.Rect(1, 1, 10, 10); },
            sap: sel => { aQuienSePregunto.Add("sap"); return new System.Windows.Rect(2, 2, 20, 20); });

        var deSap = geo.Caja("sap:wnd[0]/usr/txtY0000000-ZTXTFRCAR");
        Debe(aQuienSePregunto.LastOrDefault() == "sap" && deSap is { Width: 20 },
            "un recuerdo enseñado en SAP se localiza por la geometría de SAP: por UIA no se "
            + "encendería ninguno");

        var deUia = geo.Caja("uia:name=Guardar;ct=Button");
        Debe(aQuienSePregunto.LastOrDefault() == "uia" && deUia is { Width: 10 },
            "y uno de una ventana normal, por UIA: el SELECTOR decide, no dónde estemos parados");
    }

    // ── Lo enseñado alimenta los batches (spec 009): las promesas ────────────

    /// <summary>Una skill de prueba, armada por la misma puerta que la promesa 102.</summary>
    private static object? SkillDePrueba(params (string Exit, string Texto, string Llegada, string Dicho)[] pasos)
    {
        var tSkill = Cap004("U.WindowsClient.Navigation.SkillEnsenada");
        var tPaso = Cap004("U.WindowsClient.Navigation.PasoEnsenado");
        // LA DE CUATRO, elegida a propósito: desde la promesa 140 hay otra de cinco que exige dónde
        // termina la demo, y GetMethod por nombre solo se vuelve ambiguo. Estas promesas juzgan lo
        // que juzgaban; no se les cambia la puerta por la que entran.
        var emp = tSkill?.GetMethods().FirstOrDefault(m => m.Name == "Empaquetar" && m.GetParameters().Length == 4);
        if (tSkill == null || tPaso == null || emp == null) return null;

        var lista = (System.Collections.IList)Activator.CreateInstance(
            typeof(List<>).MakeGenericType(tPaso))!;
        foreach (var p in pasos)
            lista.Add(Activator.CreateInstance(tPaso, p.Exit, p.Texto, p.Llegada, p.Dicho)!);

        return emp.Invoke(null, new object?[]
            { "llenar-el-triage", "cuando haya que pasar la consulta al triage", "sapgui://QAS/NWP1", lista });
    }

    private static void ElRescateRecibeElObjetivo()
    {
        // EL PUENTE CONSCIENTE IMPROVISA DESDE JULIO: recibe «retoma y termina la tarea» y elige por
        // su cuenta — una vez pulsó «Buscar pacientes» en vez de «Crear Triage Administrativo» y
        // declaró éxito. Y el batch YA SABE a dónde tenía que llegar: es la Llegada del paso que
        // falló. Decírselo convierte la improvisación en una búsqueda acotada con criterio de éxito.
        var t = Cap004("U.WindowsClient.Navigation.ElRescate");
        var encargo = t?.GetMethod("Encargo");
        Debe(t != null && encargo != null,
            "todavía no existe «Navigation.ElRescate.Encargo» (fase 1 de la spec 009). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || encargo == null) return;

        var accionable = new List<(string, string)>
        {
            ("sap:wnd[0]/tbar[0]/btn[0]", "Continuar"),
            ("sap:wnd[0]/usr/cmbY0000000-ZCMBCLTRG", "Clasificación Triage"),
        };
        string texto = (string)encargo.Invoke(null, new object[]
        {
            "sapgui://QAS/NWP1/SAPLY000/0001",
            "sapgui://QAS/NWP1/SAPLN_WP_FRAMEWORK/0100",
            accionable,
        })!;

        Debe(texto.Contains("sapgui://QAS/NWP1/SAPLY000/0001"),
            "el encargo del rescate NOMBRA el objetivo: sin él, «termina la tarea» deja que el modelo "
            + "elija a dónde ir — y ya eligió mal una vez");
        Debe(texto.Contains("sap:wnd[0]/tbar[0]/btn[0]") && texto.Contains("Continuar"),
            "y lleva lo accionable POR IDENTIDAD, con su selector: «pulsa btn[0]» en vez de «clic en "
            + "(683, 242)», que es lo único que sobrevive a que algo se mueva de sitio");
        Debe(!texto.Contains("termina", StringComparison.OrdinalIgnoreCase),
            "y NO le pide que termine la tarea por su cuenta: esa frase es exactamente el bug");
    }

    private static void ElAterrizajeLoJuzgaLaCompuerta()
    {
        // Hoy hay dos jueces y uno es un modelo optimista. El aterrizaje del rescate tiene que pasar
        // por la MISMA comprobación que el batch: estar en el objetivo, no decir que se llegó.
        var t = Cap004("U.WindowsClient.Navigation.ElRescate");
        var aterrizo = t?.GetMethod("Aterrizo");
        Debe(t != null && aterrizo != null,
            "todavía no existe «Navigation.ElRescate.Aterrizo» (fase 1 de la spec 009). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || aterrizo == null) return;

        (bool Llego, string Motivo) V(string objetivo, string donde)
        {
            var r = aterrizo.Invoke(null, new object[] { objetivo, donde })!;
            return ((bool)r.GetType().GetProperty("Llego")!.GetValue(r)!,
                    (string)r.GetType().GetProperty("Motivo")!.GetValue(r)!);
        }

        Debe(V("sapgui://QAS/NWP1/SAPLY000/0001", "sapgui://QAS/NWP1/SAPLY000/0001").Llego,
            "estar en el objetivo ES haber llegado");
        var no = V("sapgui://QAS/NWP1/SAPLY000/0001", "sapgui://QAS/NWP1/SAPLN_WP_FRAMEWORK/0100");
        Debe(!no.Llego,
            "y estar en otro sitio NO lo es, diga el modelo lo que diga: «terminé» no es un veredicto");
        Debe(no.Motivo.Contains("SAPLY000") && no.Motivo.Contains("SAPLN_WP_FRAMEWORK"),
            "el motivo nombra las DOS pantallas —la que se buscaba y la que hay— porque «no llegaste» "
            + "a secas manda la investigación a ciegas");
    }

    private static void ReproducirUnaSkillEsUnBatch()
    {
        // MIENTRAS UNA SKILL SE REPRODUZCA POR OTRO EJECUTOR hay dos opiniones sobre el mismo hecho.
        // El player viejo exige lo exacto —esta ventana, este elemento— y la pantalla cambia; el
        // batch exige el OBJETIVO contra lo que está vivo, y para honesto cuando no puede.
        var tInst = Cap004("U.WindowsClient.Navigation.InstanciarSkill");
        var metodo = tInst?.GetMethod("Pasos");
        Debe(tInst != null && metodo != null,
            "todavía no existe «Navigation.InstanciarSkill.Pasos» (fase 2 de la spec 009). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (tInst == null || metodo == null) return;

        var skill = SkillDePrueba(
            ("sap:wnd[0]/tbar[0]/okcd", "nwp1", "sapgui://QAS/NWP1", ""),
            ("sap:wnd[0]#node=vw1", "", "sapgui://QAS/NWP1/vista:Triage", ""),
            ("sap:wnd[0]#row=1", "", "sapgui://QAS/NWP1/SAPLY000/0001", ""));
        Debe(skill != null, "la skill de prueba se pudo empaquetar");
        if (skill == null) return;

        var pasos = (System.Collections.IList)metodo.Invoke(null,
            new object?[] { skill, new Dictionary<string, string>() })!;

        Debe(pasos.Count == 3, $"los 3 pasos enseñados salen como 3 pasos del batch (salieron {pasos.Count})");
        if (pasos.Count != 3) return;

        Debe(pasos[0]!.GetType() == typeof(RecorrerSegunElNucleo.Paso),
            "y salen como pasos DEL BATCH, no en un formato propio: un segundo ejecutor sería una "
            + "segunda opinión del mismo hecho");
        Debe(((RecorrerSegunElNucleo.Paso)pasos[2]!).Llegada == "sapgui://QAS/NWP1/SAPLY000/0001",
            "cada paso llega con su LLEGADA: es el objetivo que la compuerta exige, y sin él "
            + "reproducir sería volver a fiarse de que el plan sirve (promesa 103)");
    }

    private static void ElValorDeLaDemoNoSeReproduce()
    {
        // LA BOMBA SILENCIOSA de la skill tal como quedó en la spec 005: el valor tecleado viaja como
        // un paso normal. La demo del triage se hizo sobre un PACIENTE DE PRUEBA con «70» de peso;
        // reproducir eso sobre un paciente real es un dato clínico falso que nadie notaría.
        var tSkill = Cap004("U.WindowsClient.Navigation.SkillEnsenada");
        var huecos = tSkill?.GetProperty("Huecos");
        var tInst = Cap004("U.WindowsClient.Navigation.InstanciarSkill");
        var metodo = tInst?.GetMethod("Pasos");
        Debe(huecos != null && metodo != null,
            "todavía no existen «SkillEnsenada.Huecos» ni «InstanciarSkill.Pasos» (fase 2 de la "
            + "spec 009). La promesa está escrita y en rojo, que es donde tiene que estar");
        if (tSkill == null || huecos == null || tInst == null || metodo == null) return;

        var skill = SkillDePrueba(
            ("sap:wnd[0]/usr/txtY0000000-ZTXTPESO", "70", "", "aquí va el peso del paciente"));
        if (skill == null) { Debe(false, "la skill de prueba se pudo empaquetar"); return; }

        int cuantos = 0;
        foreach (var _ in (System.Collections.IEnumerable)huecos.GetValue(skill)!) cuantos++;
        Debe(cuantos == 1,
            $"un valor tecleado en la demo se declara HUECO, no paso fijo (se declararon {cuantos})");

        var pasos = (System.Collections.IList)metodo.Invoke(null,
            new object?[] { skill, new Dictionary<string, string>() })!;
        bool alguienEscribe70 = false;
        foreach (var p in pasos)
            if (p is RecorrerSegunElNucleo.Paso pb && pb.Texto.Contains("70")) alguienEscribe70 = true;
        Debe(!alguienEscribe70,
            "y sin dato que lo llene el hueco queda VACÍO: el «70» del paciente de prueba no puede "
            + "acabar en la historia clínica de otro");
    }

    private static void LoDichoSeVuelveRecuerdo()
    {
        // LA EXPERIENCIA TEDIOSA QUE ESTO ALIVIA: señalar elemento por elemento para enseñar. Los
        // recuerdos a mano SE QUEDAN —no se retira nada—, pero la demo ya oyó «aquí va el peso»
        // mientras el operador lo tecleaba, y eso basta para colgarlo solo.
        var t = Cap004("U.WindowsClient.Navigation.RecuerdosDeUnaSkill");
        var de = t?.GetMethod("De");
        Debe(t != null && de != null,
            "todavía no existe «Navigation.RecuerdosDeUnaSkill.De» (fase 3 de la spec 009). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || de == null) return;

        var skill = SkillDePrueba(
            ("sap:wnd[0]/usr/txtPESO", "70", "sapgui://QAS/B", "aquí va el peso del paciente"),
            ("sap:wnd[0]/usr/txtTALLA", "170", "sapgui://QAS/C", ""));
        if (skill == null) { Debe(false, "la skill de prueba se pudo empaquetar"); return; }

        var salida = (System.Collections.IList)de.Invoke(null,
            new object?[] { skill, "", new List<(string, string)>() })!;

        Debe(salida.Count == 1,
            $"solo el paso que SONABA deja recuerdo: el otro no se inventa (salieron {salida.Count})");
        if (salida.Count != 1) return;

        var r = salida[0]!;
        string Campo(string n) => (string)r.GetType().GetProperty(n)!.GetValue(r)!;
        Debe(Campo("Selector") == "sap:wnd[0]/usr/txtPESO" && Campo("Significado").Contains("peso"),
            "el recuerdo lleva el elemento y lo que se dijo de él, sin que nadie señale");
        Debe(Campo("Ubicacion") == "sapgui://QAS/NWP1",
            "y se cuelga DONDE EL PASO SE DIO —el inicio de la skill—, no donde llegó: un recuerdo "
            + "colgado de la pantalla siguiente no se encontraría nunca al volver");
    }

    private static void ElRecuerdoSeCuelgaDeLoQueElPasoToco()
    {
        // EL GUARDARRAÍL DEL VIDEO. El resumen del video es prosa de un LLM, y la forma fácil de
        // usarlo es dejar que diga a qué elemento se refiere. Eso es justo lo que no puede hacer: el
        // selector lo pone el PASO —lo que la mano tocó— y una sugerencia que no case con ningún
        // paso se descarta. La misma disciplina que salvó el relleno el 2026-09-02.
        var t = Cap004("U.WindowsClient.Navigation.RecuerdosDeUnaSkill");
        var de = t?.GetMethod("De");
        Debe(t != null && de != null,
            "todavía no existe «Navigation.RecuerdosDeUnaSkill.De» (fase 3 de la spec 009). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || de == null) return;

        var skill = SkillDePrueba(("sap:wnd[0]/usr/txtPESO", "70", "sapgui://QAS/B", ""));
        if (skill == null) { Debe(false, "la skill de prueba se pudo empaquetar"); return; }

        var sugerencias = new List<(string, string)>
        {
            ("sap:wnd[0]/usr/txtPESO", "es el peso en kilogramos"),
            ("sap:wnd[0]/usr/txtJAMAS_TOCADO", "es la temperatura"),
        };
        var salida = (System.Collections.IList)de.Invoke(null,
            new object?[] { skill, "el médico escribe el peso y sigue", sugerencias })!;

        Debe(salida.Count == 1,
            $"la sugerencia sobre un elemento que el paso NO tocó se descarta (quedaron {salida.Count})");
        if (salida.Count != 1) return;
        var r = salida[0]!;
        Debe((string)r.GetType().GetProperty("Selector")!.GetValue(r)! == "sap:wnd[0]/usr/txtPESO",
            "y la que sí case se cuelga del elemento del paso: el modelo aporta el SIGNIFICADO, "
            + "nunca la identidad");
    }

    private static void ComprobarNoGraba()
    {
        // «Finalizar (Shift+F3)» cerró la sesión de SAP y colgó un batch 120 s (2026-08-30). Y una
        // comprobación que grabe deja datos reales en la historia clínica de un paciente por el
        // simple hecho de estar aprendiendo.
        var tP = Cap004("U.WindowsClient.Navigation.PuertasPeligrosas");
        var esPeligrosa = tP?.GetMethod("EsPeligrosa");
        var tInst = Cap004("U.WindowsClient.Navigation.InstanciarSkill");
        var metodo = tInst?.GetMethod("Pasos");
        Debe(tP != null && esPeligrosa != null && metodo != null,
            "todavía no existen «Navigation.PuertasPeligrosas» ni «InstanciarSkill.Pasos» (fase 3 de "
            + "la spec 009). La promesa está escrita y en rojo, que es donde tiene que estar");
        if (tP == null || esPeligrosa == null || tInst == null || metodo == null) return;

        Debe((bool)esPeligrosa.Invoke(null, new object[] { "Grabar" })!
             && (bool)esPeligrosa.Invoke(null, new object[] { "Finalizar   (Shift+F3)" })!,
            "Grabar y Finalizar son puertas peligrosas, se llamen como se llamen en pantalla");
        Debe(!(bool)esPeligrosa.Invoke(null, new object[] { "Peso" })!,
            "y un campo cualquiera NO lo es: una lista que muerde de más deja de poder usarse");

        var skill = SkillDePrueba(
            ("sap:wnd[0]/usr/txtPESO", "70", "sapgui://QAS/B", ""),
            ("sap:wnd[0]/tbar[0]/btnGRABAR", "", "sapgui://QAS/C", "Grabar"));
        if (skill == null) { Debe(false, "la skill de prueba se pudo empaquetar"); return; }

        var pasos = (System.Collections.IList)metodo.Invoke(null,
            new object?[] { skill, new Dictionary<string, string>() })!;
        Debe(pasos.Count == 1,
            $"la skill se recorre HASTA la puerta peligrosa y ahí se detiene (salieron {pasos.Count} de 2)");
    }

    private static void SinComprobarNoSeEjecuta()
    {
        // DECISIÓN DEL DUEÑO (2026-09-03): comprobar es OBLIGATORIO. Y una compuerta que solo dice
        // «no» es papeleo: tiene que decir qué falta, o se aprende a saltársela.
        var tSkill = Cap004("U.WindowsClient.Navigation.SkillEnsenada");
        var comprobada = tSkill?.GetProperty("Comprobada");
        var puedeCorrer = tSkill?.GetMethod("PuedeCorrer");
        Debe(comprobada != null && puedeCorrer != null,
            "todavía no existen «SkillEnsenada.Comprobada» ni «PuedeCorrer» (fase 4 de la spec 009). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (tSkill == null || comprobada == null || puedeCorrer == null) return;

        var skill = SkillDePrueba(("sap:wnd[0]/usr/txtPESO", "70", "sapgui://QAS/B", ""));
        if (skill == null) { Debe(false, "la skill de prueba se pudo empaquetar"); return; }

        (bool Puede, string Motivo) V(object s)
        {
            var r = puedeCorrer.Invoke(s, null)!;
            return ((bool)r.GetType().GetProperty("Puede")!.GetValue(r)!,
                    (string)r.GetType().GetProperty("Motivo")!.GetValue(r)!);
        }

        var recien = V(skill);
        Debe(!recien.Puede,
            "una skill recién enseñada y SIN comprobar no se ejecuta: reproducir a ciegas lo que "
            + "nadie repasó es la apuesta que esta compuerta existe para impedir");
        Debe(recien.Motivo.Contains("comprob", StringComparison.OrdinalIgnoreCase),
            "y el «no» dice QUÉ FALTA —comprobarla—, porque una compuerta muda se aprende a saltar");

        var yaComprobada = tSkill.GetMethod("ConLaComprobacionHecha")?.Invoke(skill, null);
        Debe(yaComprobada != null && V(yaComprobada).Puede,
            "y comprobada SÍ corre: la compuerta existe para la skill sin repasar, no para todas");
    }

    private static void ElVideoQueFalloSeDice()
    {
        // DECISIÓN DEL DUEÑO (2026-09-03): el video se procesa con IA. El riesgo conocido es el 504
        // de Vercel en flujos largos —ya pasó con seis pantallas y tres minutos—, así que el fallo
        // tiene que VERSE: un contexto que se perdió en silencio se parece demasiado a un contexto
        // que no hacía falta.
        var t = Cap004("U.WindowsClient.Navigation.RecuerdosDeUnaSkill");
        var cuenta = t?.GetMethod("Cuenta");
        Debe(t != null && cuenta != null,
            "todavía no existe «Navigation.RecuerdosDeUnaSkill.Cuenta» (fase 4 de la spec 009). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || cuenta == null) return;

        string sinVideo = (string)cuenta.Invoke(null, new object[] { "", 3, 0 })!;
        Debe(sinVideo.Contains("video", StringComparison.OrdinalIgnoreCase) && sinVideo.Contains("3"),
            "sin resumen del video se DICE que no llegó, y se cuentan los 3 recuerdos que sí salieron "
            + "de lo dicho: la comprobación sigue, no se cae");

        string conVideo = (string)cuenta.Invoke(null, new object[] { "el médico llena los signos vitales", 3, 2 })!;
        Debe(conVideo.Contains("2") && conVideo.Contains("3"),
            "y con video se distingue lo que aportó cada fuente —3 de lo dicho y 2 del video— para "
            + "poder revisar de dónde salió un recuerdo raro");
    }

    private static void ElModeloNoInventaCampos()
    {
        // EL MISMO GUARDARRAÍL QUE EL DEL VIDEO (promesa 125), ahora sobre la clasificación: el
        // modelo aporta el CRITERIO —esto es un dato, esto es navegación— pero jamás la identidad.
        // Un campo que la demo no tocó no existe para esta skill, lo nombre quien lo nombre.
        var t = Cap004("U.WindowsClient.Navigation.LoQueElModeloInterpreta");
        var leer = t?.GetMethod("Leer");
        Debe(t != null && leer != null,
            "todavía no existe «Navigation.LoQueElModeloInterpreta.Leer» (spec 009). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || leer == null) return;

        var skill = SkillDePrueba(
            ("sap:wnd[0]/usr/txtPESO", "70", "sapgui://QAS/B", ""),
            ("sap:wnd[0]/tbar[0]/okcd", "nwp1", "sapgui://QAS/C", ""));
        if (skill == null) { Debe(false, "la skill de prueba se pudo empaquetar"); return; }

        string json = "{\"campos\":["
            + "{\"campo\":\"sap:wnd[0]/usr/txtPESO\",\"esDato\":true,\"significado\":\"el peso del paciente en kg\"},"
            + "{\"campo\":\"sap:wnd[0]/tbar[0]/okcd\",\"esDato\":false,\"significado\":\"el código de transacción\"},"
            + "{\"campo\":\"sap:wnd[0]/usr/txtJAMAS\",\"esDato\":true,\"significado\":\"la temperatura\"}],"
            + "\"recuerdos\":["
            + "{\"campo\":\"sap:wnd[0]/usr/txtPESO\",\"significado\":\"se escribe sin decimales\"},"
            + "{\"campo\":\"sap:wnd[0]/usr/txtJAMAS\",\"significado\":\"esto no lo tocó nadie\"}]}";

        var leido = leer.Invoke(null, new object?[] { json, skill })!;
        var huecos = (System.Collections.IList)leido.GetType().GetProperty("Huecos")!.GetValue(leido)!;
        var recuerdos = (System.Collections.IList)leido.GetType().GetProperty("Recuerdos")!.GetValue(leido)!;

        Debe(huecos.Count == 1,
            $"de tres campos nombrados, solo el que la demo TOCÓ y el modelo llamó dato es hueco "
            + $"(salieron {huecos.Count})");
        if (huecos.Count == 1)
        {
            var h = huecos[0]!;
            Debe((string)h.GetType().GetProperty("Campo")!.GetValue(h)! == "sap:wnd[0]/usr/txtPESO"
                 && (string)h.GetType().GetProperty("Significado")!.GetValue(h)!.ToString()!.Length.ToString() != "0",
                "y llega con el significado que el modelo le puso: ahí es donde aporta");
        }
        Debe(recuerdos.Count == 1,
            $"y el recuerdo sobre un campo que nadie tocó se descarta igual (quedaron {recuerdos.Count})");
    }

    private static void SinCerebroNoSePierdeLoEnsenado()
    {
        // EL CEREBRO VIVE AL OTRO LADO DE LA RED, y esa red se cae —este repo tiene un DNS que
        // parpadea y un Vercel que da 504 en flujos largos—. Perder la demo entera por eso sería
        // castigar al humano por un problema que no es suyo: se sigue con lo que se narró, que es
        // determinista y ya está en disco, y se DICE que el modelo no opinó.
        var t = Cap004("U.WindowsClient.Navigation.LoQueElModeloInterpreta");
        var leer = t?.GetMethod("Leer");
        var hubo = t?.GetMethod("HuboInterpretacion");
        Debe(t != null && leer != null && hubo != null,
            "todavía no existen «LoQueElModeloInterpreta.Leer/HuboInterpretacion» (spec 009). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || leer == null || hubo == null) return;

        var skill = SkillDePrueba(
            ("sap:wnd[0]/usr/txtPESO", "70", "sapgui://QAS/B", "aquí va el peso del paciente"));
        if (skill == null) { Debe(false, "la skill de prueba se pudo empaquetar"); return; }

        foreach (string sinRespuesta in new[] { "", "   ", "no soy json", "{\"otra\":1}" })
        {
            var leido = leer.Invoke(null, new object?[] { sinRespuesta, skill })!;
            var huecos = (System.Collections.IList)leido.GetType().GetProperty("Huecos")!.GetValue(leido)!;
            Debe(huecos.Count == 0,
                $"con «{sinRespuesta}» el modelo no aporta ningún hueco, en vez de inventar uno");
            Debe((bool)hubo.Invoke(null, new object?[] { leido })! == false,
                "y se puede SABER que no interpretó, que es lo que permite decirlo en vez de callarlo");
        }

        // Con lo narrado, la skill ya trae su hueco: eso no se pierde pase lo que pase con la red.
        Debe(skill.GetType().GetProperty("Huecos")!.GetValue(skill) is System.Collections.IList h2 && h2.Count == 1,
            "lo que se narró sigue siendo un hueco sin que el cerebro tenga que decir nada: la red "
            + "puede fallar, la demo ya ocurrió");
    }

    private static void LaInterpretacionNoCuelgaDelVideo()
    {
        // LA CUENTA DE GEMINI SE QUEDÓ SIN SALDO EL 2026-09-03 —«429: Your prepayment credits are
        // depleted», cuatro veces— y con ella se cayó la interpretación entera, aunque el juicio que
        // le estamos pidiendo NO NECESITA VER LA PANTALLA: distinguir «nwp1 es cómo se llega» de
        // «70 es el peso de este paciente» se hace con los pasos y con lo que la persona dijo, que
        // ya están en disco y no cuestan un video.
        //
        // Así que el video es lo que MEJORA la interpretación, no de lo que depende. Tres peldaños,
        // y cada uno solo se pisa si falló el anterior: video → texto → la regla del narrado. Se
        // juzga aquí que existe el peldaño de en medio y que el de abajo sigue debajo.
        var t = Cap004("U.WindowsClient.Teach.LoQueSePregunta");
        var sinPantalla = t?.GetMethod("SinPantalla");
        Debe(t != null && sinPantalla != null,
            "todavía no existe «Teach.LoQueSePregunta.SinPantalla» (fase 8 de la spec 009). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || sinPantalla == null) return;

        var skill = SkillDePrueba(
            ("sap:wnd[0]/tbar[0]/okcd", "nwp1", "sapgui://QAS/B", "escribimos nwp1 y enter"),
            ("sap:wnd[0]/usr/txtPESO", "70", "sapgui://QAS/C", "aquí va el peso"));
        if (skill == null) { Debe(false, "la skill de prueba se pudo empaquetar"); return; }

        // SIN VIDEO SÍ HAY PREGUNTA: es exactamente lo que permite trabajar con la cuenta vacía.
        bool hayQuePreguntar = (bool)sinPantalla.Invoke(null, new object?[] { skill, "" })!;
        Debe(hayQuePreguntar,
            "sin resumen de video la demo TODAVÍA se puede interpretar: los pasos y lo narrado ya "
            + "están en disco, y el juicio que se pide no necesita ver la pantalla");

        // Si el video SÍ interpretó, no se vuelve a preguntar: sería pagar dos veces por lo mismo
        // y quedarse con dos opiniones del mismo hecho, que es lo que este repo no hace.
        Debe(!(bool)sinPantalla.Invoke(null, new object?[] { skill, "ya lo interpretó el video" })!,
            "y si el video ya opinó no se pregunta otra vez: dos opiniones del mismo hecho acaban "
            + "contradiciéndose sin avisar");

        // Y una demo sin un solo valor tecleado no tiene nada que interpretar: preguntar sería
        // gastar una llamada para que el modelo conteste sobre una lista de clics.
        var soloClics = SkillDePrueba(
            ("sap:wnd[0]/tbar[0]/btn[0]", "", "sapgui://QAS/B", "pulsamos continuar"));
        Debe(soloClics == null || !(bool)sinPantalla.Invoke(null, new object?[] { soloClics, "" })!,
            "una demo sin nada tecleado no se manda a interpretar: no hay ningún valor del que "
            + "decidir si es dato o navegación");
    }

    private static void ElArranqueEsperaNoCuentaAtras()
    {
        // LO QUE PASÓ, 2026-09-03 a las 18:45 y otra vez a las 18:46, las dos veces igual:
        //
        //   18:46:20  voz-viva: micrófono abierto a 24000 Hz        ← 4 s tras pulsar 🎓
        //   18:46:23  voz-viva: usuario dijo: ¿y tú me escuchas?
        //   18:46:27  ✋ el primer plano sigue siendo Ü tras 4000 ms de gracia — NO se graba
        //
        // El micrófono que ahora se abre solo al pulsar 🎓 mete una CONVERSACIÓN encima de un plazo
        // de 7 s (3 de cuenta atrás + 4 de gracia) que exige cambiar de ventana. El humano estaba
        // haciendo lo correcto —comprobar que Ü le oía— y el reloj corría en su contra. Antes del
        // micrófono automático nadie se paraba a hablar, y por eso el plazo nunca había estorbado:
        // el cambio no rompió esta pieza, cambió las condiciones bajo las que era suficiente.
        //
        // ALARGAR EL PLAZO NO ES EL ARREGLO. Un plazo más largo sigue siendo un plazo, y sigue
        // compitiendo con lo que Ü está diciendo — solo que falla más de vez en cuando, que es peor
        // para diagnosticar. La señal buena no es «pasaron N segundos»: es «el humano ya puso
        // delante la app». Eso se espera, no se cronometra.
        var t = Cap004("U.WindowsClient.Teach.ElArranqueDeLaDemo");
        var juzgar = t?.GetMethod("Juzgar");
        Debe(t != null && juzgar != null,
            "todavía no existe «Teach.ElArranqueDeLaDemo.Juzgar» (fase 9 de la spec 009). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || juzgar == null) return;

        const int Techo = 60000;
        object Juzga(bool nuestro, int esperadoMs) =>
            juzgar.Invoke(null, new object[] { nuestro, esperadoMs, Techo })!;
        bool Empieza(object v) => (bool)v.GetType().GetProperty("Empezar")!.GetValue(v)!;
        bool Sigue(object v) => (bool)v.GetType().GetProperty("Seguir")!.GetValue(v)!;
        string Dice(object v) => (string)v.GetType().GetProperty("Decir")!.GetValue(v)!;

        // EL CASO QUE FALLÓ, clavado: a los 7 s con Ü todavía delante porque estaba hablando.
        var alos7 = Juzga(nuestro: true, esperadoMs: 7000);
        Debe(!Empieza(alos7) && Sigue(alos7),
            "a los 7 segundos con Ü delante NO se rinde: sigue esperando. Ese plazo exacto es el que "
            + "se comió las dos demos del 2026-09-03 mientras Ü saludaba");

        var alos20 = Juzga(nuestro: true, esperadoMs: 20000);
        Debe(Sigue(alos20),
            "y a los 20 segundos tampoco: mientras el humano no haya cambiado de ventana no hay "
            + "nada que grabar, y rendirse solo le obliga a volver a empezar");

        // NUNCA DICE «GRABANDO» SIN ESTARLO. Un mensaje que promete lo que no ocurre es peor que
        // ninguno: el humano se pone a hacer la tarea y nadie la está viendo.
        foreach (int ms in new[] { 0, 3000, 7000, 20000 })
        {
            string dice = Dice(Juzga(nuestro: true, esperadoMs: ms));
            Debe(dice.Length > 0, $"a los {ms} ms se DICE algo: una espera muda es indistinguible de un cuelgue");
            Debe(!dice.Contains("grabando", StringComparison.OrdinalIgnoreCase)
                 && !dice.Contains("grabar en", StringComparison.OrdinalIgnoreCase),
                $"y a los {ms} ms NO dice que esté grabando, porque no lo está (dijo: «{dice}»)");
        }

        // EN CUANTO LA APP ESTÁ DELANTE, se empieza. Sin esperar a que se cumpla ningún plazo: el
        // plazo era lo que sobraba.
        var yaEsta = Juzga(nuestro: false, esperadoMs: 300);
        Debe(Empieza(yaEsta) && !Sigue(yaEsta),
            "en cuanto el primer plano deja de ser Ü se empieza a grabar, a los 300 ms o a los 30 s: "
            + "la señal es que cambiaste de ventana, no que pasara un tiempo");

        // Y SE RINDE, pero tarde y diciendo por qué. Sin techo, un 🎓 olvidado deja un hilo mirando
        // el escritorio para siempre.
        var alTecho = Juzga(nuestro: true, esperadoMs: Techo);
        Debe(!Empieza(alTecho) && !Sigue(alTecho),
            "al llegar al techo se para: un botón olvidado no puede dejar un hilo esperando para siempre");
        Debe(Dice(alTecho).Length > 0 && Dice(alTecho).Contains("delante", StringComparison.OrdinalIgnoreCase),
            $"y al rendirse dice QUÉ faltó, no «no se pudo» (dijo: «{Dice(alTecho)}»)");
    }

    /// <summary>
    /// LO QUE SE OÍA, y por qué es una promesa y no una preferencia de estilo. El prompt ORDENABA
    /// narrar antes de cada llamada: «DI LO QUE VAS A HACER, Y LUEGO HAZLO. Antes de cada llamada,
    /// una frase corta en voz —"voy a Descargas"— y a continuación la herramienta». Eso producía
    /// dos averías a la vez, las dos vistas por el dueño el 2026-09-05:
    ///
    ///   · BALBUCEO. Una frase por herramienta. «Habla un 80 % y hace un 30 %; lo quiero al revés».
    ///   · DESALINEAMIENTO. El futuro se oye SIEMPRE tarde: la herramienta tarda milisegundos y el
    ///     audio segundos, así que «voy a abrirlo» suena cuando ya está abierto. En el log del
    ///     2026-09-03, «ejecutando map_pointing_at» y «voy a mirarlo» llevan el MISMO segundo.
    ///
    /// Se juzga el texto de las instrucciones y no la conversación porque es lo único que se puede
    /// juzgar sin pantalla y sin gastar audio: si la orden vuelve al prompt, el balbuceo vuelve.
    /// </summary>
    private static void UNoAnunciaLoQueVaAHacer()
    {
        var t = Cap004("U.WindowsClient.Voice.ConversacionEnVivo");
        var prop = t?.GetProperty("InstruccionesNormales",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        Debe(t != null && prop != null, "no encuentro «ConversacionEnVivo.InstruccionesNormales»");
        if (prop == null) return;
        string texto = (string)prop.GetValue(null)!;

        Debe(!texto.Contains("DI LO QUE VAS A HACER", StringComparison.Ordinal),
            "la orden de anunciar antes de cada llamada NO puede volver al prompt: era la causa "
            + "directa del balbuceo y del «voy a…» que se oye después de haberlo hecho");
        Debe(!texto.Contains("Ve contando lo que haces mientras lo haces", StringComparison.Ordinal),
            "y tampoco su gemela de más arriba, que pedía lo mismo con otras palabras: se arregló "
            + "una y la otra seguía ordenando narrar cada paso (aprendizaje nº7, la clase de error)");

        Debe(texto.Contains("NO ANUNCIES LO QUE VAS A HACER", StringComparison.Ordinal),
            "está dicha la regla, y en mayúsculas como el resto de las que se incumplían");
        Debe(texto.Contains("HABLA EN PASADO", StringComparison.Ordinal),
            "y con qué sustituirlo: en pasado y del resultado. Prohibir sin dar el reemplazo deja "
            + "al modelo eligiendo, y elige narrar");
        foreach (string relleno in new[] { "«voy a…»", "«vamos a…»" })
            Debe(texto.Contains(relleno, StringComparison.Ordinal),
                $"y se nombran las fórmulas concretas que se oían ({relleno}): una regla abstracta "
                + "no se cumple, una lista de frases prohibidas sí");

        Debe(texto.Contains("Habla, sin que te lo pidan, SOLO en estos casos", StringComparison.Ordinal),
            "y se dice CUÁNDO sí toca hablar. Sin esa lista, «habla menos» se lee como «cállate», y "
            + "un fallo sin contar es peor que un balbuceo");
    }

    private static void MientrasEnsenasNoTieneManos()
    {
        // 2026-09-03 19:51:47: «voz-viva: llamada recibida: map_scroll». El dueño narraba «vas a
        // hacer scroll hacia abajo» para la GRABACIÓN, y Ü lo tomó por una orden y scrolleó. Antes
        // había intentado tres batches. La voz seguía en modo asistente mientras se le enseñaba: la
        // narración se leía como órdenes, y lo que Ü hizo con sus manos no es un paso del humano.
        // Un aprendiz que aprende de otra persona no toca: escucha, asiente, y habla si le hablan.
        var t = Cap004("U.WindowsClient.Teach.ModoAprendiz");
        var utensilios = t?.GetMethod("Utensilios");
        var instrucciones = t?.GetProperty("Instrucciones") ?? (MemberInfo?)t?.GetField("Instrucciones");
        Debe(t != null && utensilios != null && instrucciones != null,
            "todavía no existe «Teach.ModoAprendiz» (fase 9 de la spec 009). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || utensilios == null || instrucciones == null) return;

        var todas = (System.Collections.IEnumerable)Cap004("U.WindowsClient.Voice.ConversacionEnVivo")!
            .GetMethod("Herramientas", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null)!;
        var conManos = new List<string>();
        foreach (var u in todas) conManos.Add((string)u.GetType().GetProperty("Nombre")!.GetValue(u)!);
        Debe(conManos.Contains("map_take") && conManos.Contains("map_scroll"),
            "el catálogo normal SÍ tiene manos (map_take, map_scroll): si no, esta promesa no juzga nada");

        var sinManos = (System.Collections.IEnumerable)utensilios.Invoke(null, new object[] { todas })!;
        var nombres = new List<string>();
        foreach (var u in sinManos) nombres.Add((string)u.GetType().GetProperty("Nombre")!.GetValue(u)!);

        foreach (string mano in new[] { "map_take", "map_type", "map_scroll", "map_go_to", "map_unblock", "map_open_app", "file_open" })
            Debe(!nombres.Contains(mano),
                $"«{mano}» mueve la pantalla y NO está en el catálogo del aprendiz: lo que Ü haga con "
                + "sus manos no es un paso del humano, y lo que narras es para la grabación, no una orden");
        foreach (string ojo in new[] { "map_pointing_at", "map_where_am_i", "map_what_i_see" })
            Debe(nombres.Contains(ojo),
                $"«{ojo}» sí está: un aprendiz mira lo que le señalan, y eso es contexto");
        Debe(nombres.Count > 0 && nombres.Count < conManos.Count,
            $"el catálogo del aprendiz es un SUBCONJUNTO ({nombres.Count} de {conManos.Count}), no otro catálogo");

        string texto = (string)(instrucciones is PropertyInfo pi ? pi.GetValue(null) : ((FieldInfo)instrucciones).GetValue(null))!;
        Debe(!texto.Contains("Tienes manos", StringComparison.OrdinalIgnoreCase)
             && !texto.Contains("NO PIDAS PERMISO", StringComparison.OrdinalIgnoreCase),
            "las instrucciones del aprendiz no son las del asistente con un parche encima: no le "
            + "dicen que tiene manos ni que actúe sin pedir permiso");
        Debe(texto.Contains("ajá", StringComparison.OrdinalIgnoreCase)
             || texto.Contains("uhum", StringComparison.OrdinalIgnoreCase)
             || texto.Contains("entiendo", StringComparison.OrdinalIgnoreCase),
            "y le dicen cómo asentir sin interrumpir: un aprendiz que se calla del todo parece "
            + "desconectado; uno que contesta todo, un asistente");
        Debe(texto.Contains("no toques", StringComparison.OrdinalIgnoreCase)
             || texto.Contains("no actúes", StringComparison.OrdinalIgnoreCase)
             || texto.Contains("no hagas nada", StringComparison.OrdinalIgnoreCase),
            "y le dicen con todas las letras que NO toque: quitar las herramientas evita el daño, "
            + "decírselo evita que lo intente y se frustre");
    }

    private static void ComprobarEsUnEncargoNoUnGuion()
    {
        // LA FRASE DEL DUEÑO (2026-09-03): «siento que todavía estamos tratando la enseñanza como si
        // fueran workflows». Y era verdad: InstanciarSkill.Pasos → batch reproduce paso a paso. Lo
        // que se quiere es que Ü, con el objetivo y TODO el contexto de la demo, vaya hacia el
        // objetivo por su cuenta —por identidad, con la compuerta— colgando un recuerdo de cada
        // elemento que use. El scroll que la demo no grabó deja de importar: el batch alcanza una
        // fila conocida por identidad aunque esté desplazada (promesa 80).
        var t = Cap004("U.WindowsClient.Navigation.ElEncargoDeComprobar");
        var texto = t?.GetMethod("Texto");
        Debe(t != null && texto != null,
            "todavía no existe «Navigation.ElEncargoDeComprobar.Texto» (fase 10 de la spec 009). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || texto == null) return;

        var skill = SkillDePrueba(
            ("sap:wnd[0]/tbar[0]/okcd", "nwp1", "sapgui://QAS/NWP1/SAPLN_WP_FRAMEWORK/0100", "aquí escribes el código de transacción"),
            ("sap:wnd[0]/usr/txtY0000000-ZTXTPESO", "70", "sapgui://QAS/NWP1/SAPLY000/0001", "aquí va el peso del paciente"),
            ("sap:wnd[0]/tbar[1]/btn[11]", "", "sapgui://QAS/NWP1/SAPLY000/0001", "y con esto se graba"));
        if (skill == null) { Debe(false, "la skill de prueba se pudo empaquetar"); return; }

        string encargo = (string)texto.Invoke(null, new object?[] { skill })!;

        Debe(encargo.Contains("cuando haya que pasar la consulta al triage"),
            "el encargo lleva el OBJETIVO tal como se describió: es lo que hay que alcanzar");
        Debe(encargo.Contains("sap:wnd[0]/tbar[0]/okcd") && encargo.Contains("sap:wnd[0]/usr/txtY0000000-ZTXTPESO"),
            "y cada elemento que la demo tocó, por su identidad exacta —es lo que el piloto puede pedir por nombre—");
        Debe(encargo.Contains("aquí escribes el código de transacción") && encargo.Contains("aquí va el peso del paciente"),
            "con lo que se DIJO sobre cada uno: ese es el contexto que convierte un selector en un significado");
        Debe(encargo.Contains("sapgui://QAS/NWP1/SAPLY000/0001"),
            "y a dónde tiene que llegar: sin destino, «terminé» vuelve a ser una opinión");

        // LA ASIMETRÍA: destinos sí, valores jamás. «70» era el peso de un paciente de prueba y
        // «nwp1» ya viaja como lo que ES (un selector con su significado), no como un valor a teclear.
        Debe(!System.Text.RegularExpressions.Regex.IsMatch(encargo, @"\b70\b"),
            "ningún VALOR tecleado de la demo viaja en el encargo: el 70 del paciente de prueba no "
            + "puede acabar en la historia de otro");

        Debe(encargo.Contains("recuerdo", StringComparison.OrdinalIgnoreCase)
             && encargo.Contains("map_esto_es", StringComparison.OrdinalIgnoreCase),
            "y pide colgar un RECUERDO de cada elemento que use, por la puerta de siempre (map_esto_es): "
            + "es lo que deja el terreno preparado para el batch de después");
        Debe(encargo.Contains("Grabar", StringComparison.OrdinalIgnoreCase)
             || encargo.Contains("peligros", StringComparison.OrdinalIgnoreCase),
            "y para ANTES de las puertas peligrosas: comprobar no graba (promesa 126), también por este camino");
        Debe(!encargo.Contains("reproduce", StringComparison.OrdinalIgnoreCase)
             && !encargo.Contains("paso a paso exacto", StringComparison.OrdinalIgnoreCase),
            "y no le pide reproducir nada: los pasos son CONTEXTO, no un guion — el orden en que la "
            + "demo lo hizo es una pista, no una obligación");
    }

    private static void ElUltimoPasoTieneDestino()
    {
        // 2026-09-03 20:02:52: «hice los 1 paso(s): quedaste en «…SESSION_MANAGER…»» — comprobada
        // en 77 ms. Escribió NWP1, pulsó Enter, y la llegada que exigía era la pantalla de la que
        // SALÍA. Por qué: la llegada del paso N es la superficie del paso N+1, y el Enter era el
        // último paso — sin siguiente no hay llegada, y al plegarse heredó la de antes. Una skill
        // cuyo último paso es el que navega no tenía destino que exigir. Lo tiene: es donde acabó
        // la demo, y eso se OBSERVA al parar, no se estima.
        var tSkill = Cap004("U.WindowsClient.Navigation.SkillEnsenada");
        var tPaso = Cap004("U.WindowsClient.Navigation.PasoEnsenado");
        var emp = tSkill?.GetMethods().FirstOrDefault(m => m.Name == "Empaquetar" && m.GetParameters().Length == 5);
        Debe(tSkill != null && tPaso != null && emp != null,
            "todavía no existe «SkillEnsenada.Empaquetar» con dónde TERMINA la demo (fase 11 de la "
            + "spec 009). La promesa está escrita y en rojo, que es donde tiene que estar");
        if (tSkill == null || tPaso == null || emp == null) return;

        var lista = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(tPaso))!;
        lista.Add(Activator.CreateInstance(tPaso, "sap:wnd[0]/tbar[0]/okcd", "nwp1", "sapgui://QAS/A", "")!);
        lista.Add(Activator.CreateInstance(tPaso, "key:enter", "", "", "")!);   // último: sin siguiente, sin llegada

        var skill = emp.Invoke(null, new object?[] { "ir-a-nwp1", "", "sapgui://QAS/A", lista, "sapgui://QAS/NWP1" });
        Debe(skill != null, "con dónde termina, la skill se empaqueta");
        if (skill == null) return;
        Debe((string)skill.GetType().GetProperty("DondeTermina")!.GetValue(skill)! == "sapgui://QAS/NWP1",
            "y guarda dónde ACABÓ la demo, que es lo que se observó al parar");

        var pasosDe = Cap004("U.WindowsClient.Navigation.InstanciarSkill")?.GetMethod("Pasos");
        if (pasosDe != null)
        {
            var plan = (System.Collections.IList)pasosDe.Invoke(null, new object?[] { skill, new Dictionary<string, string>() })!;
            Debe(plan.Count == 1, $"okcd + Enter se pliegan en UN paso (salieron {plan.Count})");
            if (plan.Count == 1)
            {
                string llega = (string)plan[0]!.GetType().GetProperty("Llegada")!.GetValue(plan[0])!;
                Debe(llega == "sapgui://QAS/NWP1",
                    $"y ese último paso EXIGE llegar a donde acabó la demo, no a la pantalla de la que "
                    + $"salía (exige «{llega}»): el «comprobada en 77 ms sin moverse» no puede volver");
            }
        }

        var sinFin = emp.Invoke(null, new object?[] { "ir-a-nwp1", "", "sapgui://QAS/A", lista, "" });
        Debe(sinFin == null,
            "y sin saber dónde terminó, la skill NO se empaqueta: una skill sin destino es una que "
            + "certifica no haberse movido");
    }

    private static void ComprobarVaElementoAElemento()
    {
        // 2026-09-03 20:57:45: «turno 1: 1 acción(es)» y debajo los CUATRO clics del recorrido
        // entero, en dos segundos. El piloto usó map_batch — y lo usó porque el encargo se lo
        // ofrecía: la frase «map_batch si tienes varios pasos claros» la escribí yo. Comprobar no
        // es correr: es aprender delante de alguien. Una tanda de cuatro clics no deja narrar, no
        // deja mover la cara al elemento, y sobre todo no deja colgar un recuerdo por elemento —
        // cero map_esto_es en toda la corrida.
        var t = Cap004("U.WindowsClient.Navigation.ElEncargoDeComprobar");
        var texto = t?.GetMethod("Texto");
        if (t == null || texto == null) { Debe(false, "falta ElEncargoDeComprobar.Texto"); return; }

        var skill = SkillDePrueba(
            ("sap:wnd[0]/tbar[0]/okcd", "nwp1", "sapgui://QAS/B", "aquí escribes el código"),
            ("sap:wnd[0]/usr/btnTRIAGE", "", "sapgui://QAS/C", "y esto abre el triage"));
        if (skill == null) { Debe(false, "la skill de prueba se pudo empaquetar"); return; }
        string encargo = (string)texto.Invoke(null, new object?[] { skill })!;

        Debe(!encargo.Contains("map_batch", StringComparison.OrdinalIgnoreCase),
            "el encargo NO ofrece el batch: ofrecerlo es pedirle que se salte lo único que la "
            + "comprobación existe para hacer — pasar por cada elemento");
        Debe(!encargo.Contains("varios pasos", StringComparison.OrdinalIgnoreCase),
            "ni lo insinúa con otras palabras: «varios pasos de una vez» es el batch con otro nombre");
        Debe(encargo.Contains("UNO", StringComparison.Ordinal)
             || encargo.Contains("uno a uno", StringComparison.OrdinalIgnoreCase)
             || encargo.Contains("de uno en uno", StringComparison.OrdinalIgnoreCase),
            "y dice con todas las letras que va de uno en uno");
        Debe(encargo.Contains("en voz", StringComparison.OrdinalIgnoreCase),
            "por cada elemento se DICE EN VOZ qué entendió: si no se oye, el humano no puede "
            + "corregir lo que Ü entendió mal, que es para lo que está mirando");
        Debe(encargo.Contains("map_esto_es", StringComparison.Ordinal),
            "y se cuelga el recuerdo ahí mismo, por la puerta de siempre");

        // Y LAS TRES COSAS EN EL MISMO SITIO, no repartidas por el encargo: lo que se lee junto se
        // hace junto. Un modelo que lee «narra» arriba y «cuelga el recuerdo» treinta líneas más
        // abajo hace una de las dos.
        int narra = encargo.IndexOf("en voz", StringComparison.OrdinalIgnoreCase);
        int cuelga = encargo.IndexOf("map_esto_es", StringComparison.Ordinal);
        Debe(narra >= 0 && cuelga >= 0 && Math.Abs(narra - cuelga) < 700,
            $"decir, actuar y colgar van juntos en el encargo (están a {Math.Abs(narra - cuelga)} "
            + "caracteres): separados, el modelo hace uno y se olvida del otro");
    }

    private static void LaVozDeLaComprobacionEsLaDeU()
    {
        // El dueño: «habló al final con una voz diferente, como de Windows». Y así era: Speak cae
        // al sintetizador del sistema cuando la voz viva está cerrada, y comprobar la había cerrado
        // al terminar de enseñar. Para que Ü narre su propio recorrido con su voz hace falta poder
        // pedirle que DIGA algo — hasta hoy solo se le podía pedir que contestara.
        var tp = typeof(Voz.Realtime.ProtocoloOpenAI);
        var pide = tp.GetMethod("PedirRespuesta");
        Debe(pide != null && pide.GetParameters().Length == 1
             && pide.GetParameters()[0].HasDefaultValue,
            "«PedirRespuesta» todavía no acepta qué decir (fase 12 de la spec 009), o lo pide sin "
            + "valor por defecto — y el contrato de la voz la llama sin argumentos. La promesa está "
            + "escrita y en rojo, que es donde tiene que estar");
        if (pide == null || pide.GetParameters().Length != 1) return;

        var p = new Voz.Realtime.ProtocoloOpenAI();
        string conTexto = (string)pide.Invoke(p, new object?[] { "di exactamente: voy por el peso" })!;
        Debe(conTexto.Contains("response.create"),
            "sigue siendo la misma petición de respuesta: no se estrena un segundo canal");
        Debe(conTexto.Contains("voy por el peso"),
            $"y lo que tiene que decir viaja con ella (mandó: «{Recorta(conTexto, 160)}»)");

        string sinNada = (string)pide.Invoke(p, new object?[] { "" })!;
        Debe(sinNada.Contains("response.create") && !sinNada.Contains("instructions"),
            "y sin texto es exactamente la de siempre: pedir turno no puede convertirse en dictarle "
            + "qué decir, que es lo que hace el resto de la conversación");
    }

    private static string Recorta(string s, int n) =>
        s.Length <= n ? s : s[..n] + "…";

    private static void ElRecuerdoEsLoEntendidoNoLoBalbuceado()
    {
        // LO QUE SE COLGÓ DE VERDAD, 2026-09-03, sobre el árbol de IS-H:
        //
        //   «Sí, perfecto. Entonces, um Entonces...hay otra forma de entrar y es con esto de aquí,
        //    de ISH, um le das slowly click aquí. ¿Listo? Y de esa forma vamos a ir.»
        //
        // Y para ESE MISMO elemento el modelo ya había entendido, y estaba guardado en la skill:
        //
        //   «Al hacer doble clic aquí, se ingresa directamente a la misma pantalla que con NWP1.»
        //
        // Se colgó el balbuceo y se ignoró la frase buena. Un recuerdo es lo que le va a servir a
        // quien llegue ahí dentro de tres meses: la transcripción de cómo se dijo no sirve para eso.
        var t = Cap004("U.WindowsClient.Navigation.RecuerdosDeUnaSkill");
        var de = t?.GetMethod("De");
        if (t == null || de == null) { Debe(false, "falta RecuerdosDeUnaSkill.De"); return; }

        const string balbuceo = "Sí, perfecto. Entonces, um Entonces...hay otra forma de entrar y es "
                              + "con esto de aquí, de ISH, um le das slowly click aquí. ¿Listo?";
        const string entendido = "Al hacer doble clic aquí, se ingresa directamente a la misma pantalla que con NWP1.";
        var skill = SkillDePrueba(
            ("sap:node=F00002", "", "sapgui://QAS/B", balbuceo),
            ("sap:node=OTRO", "", "sapgui://QAS/C", "y esto abre el triage"));
        if (skill == null) { Debe(false, "la skill de prueba se pudo empaquetar"); return; }

        var sugerencias = new List<(string, string)> { ("sap:node=F00002", entendido) };
        var salida = (System.Collections.IList)de.Invoke(null, new object?[] { skill, "", sugerencias })!;

        string DeQuien(object r, string prop) => (string)r.GetType().GetProperty(prop)!.GetValue(r)!;
        object? delArbol = null;
        foreach (var r in salida) if (DeQuien(r!, "Selector") == "sap:node=F00002") delArbol = r;

        Debe(delArbol != null, "el elemento del que se habló sigue teniendo su recuerdo");
        if (delArbol == null) return;
        Debe(DeQuien(delArbol, "Significado") == entendido,
            $"y lo que se cuelga es lo que el modelo ENTENDIÓ, no cómo se dijo (se colgó: "
            + $"«{Recorta(DeQuien(delArbol, "Significado"), 90)}»)");

        // SIN SUGERENCIA NO SE PIERDE NADA: lo dicho sigue siendo mejor que el silencio.
        object? delOtro = null;
        foreach (var r in salida) if (DeQuien(r!, "Selector") == "sap:node=OTRO") delOtro = r;
        Debe(delOtro != null && DeQuien(delOtro, "Significado").Contains("triage", StringComparison.OrdinalIgnoreCase),
            "y donde el modelo no opinó, lo que se dijo se cuelga igual: se prefiere lo entendido, "
            + "no se depende de ello");
    }

    private static void UnSoloMicrofonoParaTodaLaApp()
    {
        // LO QUE PASA HOY, y no es un bug de nadie: nunca se cableó. `ConsultaWindow` tiene su
        // propio `LiveAudio` y su propio `Omi.Selector` —los dos `private readonly … = new()`— y la
        // carita tiene otro `LiveAudio` y NINGÚN selector. Elegir «collar por teléfono» en la
        // consulta abre ese caño en la instancia de la consulta y en ninguna otra, así que al
        // pulsar 🎓 la voz vuelve al micrófono del portátil sin decir nada.
        //
        // El dueño lo dijo corto: «solo necesito que el audio entre por el micrófono Omi si está
        // conectado». Un aparato, una elección, y quien la cambie la cambia para todos.
        var t = Cap004("U.WindowsClient.Voice.ElMicrofonoDeLaApp");
        var loQueToca = t?.GetMethod("LoQueToca");
        Debe(t != null && loQueToca != null,
            "todavía no existe «Voice.ElMicrofonoDeLaApp.LoQueToca» (spec 010). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || loQueToca == null) return;

        object Toca(Origen elegido, bool porCollar, bool porTelefono) =>
            loQueToca.Invoke(null, new object[] { elegido, porCollar, porTelefono })!;
        string Nombre(object v) => v.ToString() ?? "";

        // SE ABRE LO QUE FALTA…
        Debe(Nombre(Toca(Origen.CollarPorBluetooth, false, false)) == "AbrirCollar",
            "con el collar elegido y nada abierto, se abre el collar");
        Debe(Nombre(Toca(Origen.CollarPorTelefono, false, false)) == "AbrirTelefono",
            "con el teléfono elegido y nada abierto, se abre el teléfono");
        Debe(Nombre(Toca(Origen.MicrofonoDelPc, true, false)) == "VolverAlLocal",
            "y elegir el micrófono del PC con el collar abierto lo cierra: lo que elige la persona "
            + "manda sobre la preferencia automática (promesa 30, ahora para toda la app)");

        // …Y NO SE TOCA LO QUE YA ESTÁ. Reabrir el caño que ya suena corta la conversación en
        // curso: quien vuelve a elegir lo que ya tenía no está pidiendo que se le corte.
        Debe(Nombre(Toca(Origen.CollarPorBluetooth, true, false)) == "Nada",
            "pedir el collar cuando YA se oye por el collar no hace nada: reabrirlo cortaría la voz");
        Debe(Nombre(Toca(Origen.CollarPorTelefono, false, true)) == "Nada",
            "y lo mismo con el teléfono: pedirlo dos veces no puede cortar lo que ya funciona");
        Debe(Nombre(Toca(Origen.MicrofonoDelPc, false, false)) == "Nada",
            "y el micrófono del PC ya puesto tampoco se rehace");

        // UN APARATO HABLA CON UNO SOLO: elegir el teléfono con el collar enlazado por Bluetooth
        // aquí tiene que SOLTAR el Bluetooth, o el collar seguiría oyéndose por el otro camino.
        Debe(Nombre(Toca(Origen.CollarPorTelefono, true, false)) == "AbrirTelefono",
            "elegir el teléfono con el collar por Bluetooth abierto cambia de caño: un collar habla "
            + "con un aparato, no con dos");

        // Y LA ELECCIÓN ES UNA SOLA PARA TODOS, con aviso: el que la cambia no sabe quién más está
        // escuchando, así que no puede ser él quien vaya avisando uno a uno.
        var elegir = t.GetMethod("Elegir");
        var preferida = t.GetProperty("Preferida");
        var evento = t.GetEvent("Cambio");
        Debe(elegir != null && preferida != null && evento != null,
            "la elección vive en UN sitio, se puede leer, y avisa cuando cambia — sin el aviso, "
            + "quien ya tenía el micrófono abierto se queda con el de antes y nadie se entera");
        if (elegir == null || preferida == null || evento == null) return;

        int avisos = 0;
        Action oyente = () => avisos++;
        evento.AddEventHandler(null, oyente);
        try
        {
            elegir.Invoke(null, new object?[] { Origen.CollarPorTelefono, "ABC123" });
            Debe((Origen)preferida.GetValue(null)! == Origen.CollarPorTelefono,
                "lo elegido se lee desde cualquier parte de la app");
            Debe(avisos == 1, $"y avisa UNA vez, no ninguna ni tres (avisó {avisos})");

            elegir.Invoke(null, new object?[] { Origen.CollarPorTelefono, "ABC123" });
            Debe(avisos == 1,
                "elegir lo mismo otra vez no avisa: un aviso sin cambio haría que todo el mundo "
                + "rehiciera su micrófono para nada");
        }
        finally
        {
            evento.RemoveEventHandler(null, oyente);
            elegir.Invoke(null, new object?[] { Origen.MicrofonoDelPc, "" });
        }
    }

    private static void LaCajaDeSapSeResuelveEnUnSitio()
    {
        // DOS COPIAS DEL MISMO MAPEO, encontradas al cerrar la 144 (aprendizaje nº5: arreglar la
        // CLASE de error y contar cuántos sitios la tienen). `RecuerdosEnPantalla` y el señalar de
        // uno tenían cada uno su propio bucle selector→caja sobre la misma lista. Al empezar a
        // meter los botones de barra SIN caja —para que su rótulo llegue al puente con UIA— la
        // copia arreglada los saltaba y la otra los daba por buenos: habría iluminado un
        // rectángulo vacío en la esquina, que es la caja que miente del aprendizaje nº4.
        var t = Cap004("U.WindowsClient.Navigation.LaCajaDeUnaIdentidadDeSap");
        var de = t?.GetMethod("De");
        Debe(t != null && de != null,
            "todavía no existe «Navigation.LaCajaDeUnaIdentidadDeSap.De» (fase 15 de la spec 009). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || de == null) return;

        const string campo = "sap:wnd[0]/usr/txtPESO";
        const string boton = "sap:wnd[0]/usr/cntlGRID/shellcont/shell#tbbtn=ZMEDTRIAGE";
        var lista = new List<(string, string, string, System.Windows.Rect)>
        {
            (campo, "Peso", "GuiTextField", new System.Windows.Rect(10, 20, 100, 22)),
            // El botón de barra entra SIN caja: SAP no se la da, y su rótulo es lo único que aporta.
            (boton, "Triage", "ToolbarButton", System.Windows.Rect.Empty),
        };
        Func<string, System.Windows.Rect?> uiaPorNombre = n =>
            n == "Triage" ? new System.Windows.Rect(793, 227, 84, 30) : (System.Windows.Rect?)null;

        var delCampo = (System.Windows.Rect?)de.Invoke(null, new object?[] { campo, lista, uiaPorNombre });
        Debe(delCampo is { X: 10, Y: 20, Width: 100, Height: 22 },
            $"un campo del dynpro da la caja que SAP le puso (salió: {delCampo?.ToString() ?? "nada"})");

        var delBoton = (System.Windows.Rect?)de.Invoke(null, new object?[] { boton, lista, uiaPorNombre });
        Debe(delBoton is { X: 793, Y: 227, Width: 84, Height: 30 },
            $"y un botón de barra da la de UIA, casada por el rótulo que SAP declara (salió: "
            + $"{delBoton?.ToString() ?? "nada"}) — es la promesa 144, ahora por el único camino que hay");

        // LO QUE NO PUEDE PASAR: devolver la caja vacía como si fuera una posición.
        var soloSinCaja = new List<(string, string, string, System.Windows.Rect)>
            { (campo, "Peso", "GuiTextField", System.Windows.Rect.Empty) };
        var nada = (System.Windows.Rect?)de.Invoke(null, new object?[] { campo, soloSinCaja, uiaPorNombre });
        Debe(nada == null,
            "un elemento SIN caja no devuelve la caja vacía: eso se dibujaría como un rectángulo en "
            + "la esquina de la pantalla, y una caja que miente es peor que ninguna");

        Debe((System.Windows.Rect?)de.Invoke(null, new object?[]
            { "sap:wnd[0]/usr/txtNADIE", lista, uiaPorNombre }) == null,
            "y un selector que no está en la lista tampoco inventa nada");
    }

    private static void LaCajaDelBotonDeBarraLaDaUia()
    {
        // MEDIDO CON UNA SONDA DE SOLO LECTURA (2026-09-03), porque la limitación estaba
        // documentada y este repo ya aprendió a no creerle al código (aprendizaje nº13):
        //
        //   · SAP NO DA LA CAJA, y ahora es un hecho: DumpState("Toolbar") devuelve 85 entradas
        //     para 12 botones — los siete getters de siempre por índice, más el contador. Ni Left,
        //     ni Top, ni Rect. Los getters geométricos no existen.
        //   · UIA SÍ LA DA: «Triage» → Button [793,227 84x30]. La creencia «dentro de SAP UIA no ve
        //     nada» es cierta para los campos del DYNPRO y FALSA para la barra de un ALV, que son
        //     botones de Windows de verdad. La frase se escribió para el dynpro y se heredó entera.
        //
        // Así que la identidad la pone SAP —él sabe que ese botón es ZMEDTRIAGE y que se lee
        // «Triage»— y la caja la pone UIA. Ninguno de los dos puede solo.
        var t = Cap004("U.WindowsClient.Navigation.LaCajaDeUnBotonDeBarra");
        var de = t?.GetMethod("De");
        Debe(t != null && de != null,
            "todavía no existe «Navigation.LaCajaDeUnBotonDeBarra.De» (fase 14 de la spec 009). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || de == null) return;

        const string sel = "sap:wnd[0]/usr/ssubVIEW_SCREEN:SAPLN1LSTAMB:0007/cntlISH_VIEW_007/shellcont/shell#tbbtn=ZMEDTRIAGE";
        var pedidoASap = new List<string>();
        var pedidoAUia = new List<string>();

        Func<string, IReadOnlyList<string>> comoSeLee = s =>
        {
            pedidoASap.Add(s);
            return s.EndsWith("ZMEDTRIAGE", StringComparison.Ordinal)
                ? new[] { "Triage", "Triage del paciente" } : Array.Empty<string>();
        };
        Func<string, System.Windows.Rect?> cajaPorNombre = n =>
        {
            pedidoAUia.Add(n);
            return n == "Triage" ? new System.Windows.Rect(793, 227, 84, 30) : (System.Windows.Rect?)null;
        };

        var caja = (System.Windows.Rect?)de.Invoke(null, new object?[] { sel, comoSeLee, cajaPorNombre });
        Debe(caja is { X: 793, Y: 227, Width: 84, Height: 30 },
            $"la caja sale de UIA, casada por el texto que declara SAP (salió: {caja?.ToString() ?? "nada"})");
        Debe(pedidoASap.Count == 1 && pedidoASap[0] == sel,
            "y a SAP se le pregunta por el SELECTOR entero: es él quien sabe qué botón es ése");

        // NO SE INVENTA NADA. De los 12 botones reales, 4 no casaron por nombre en la sonda: ésos
        // no se dibujan. Una caja que miente es peor que no tener caja (aprendizaje nº4), y
        // estimarla por el ancho del texto o por el rectángulo del shell sería justo eso.
        Func<string, IReadOnlyList<string>> mudo = _ => Array.Empty<string>();
        Debe((System.Windows.Rect?)de.Invoke(null, new object?[] { sel, mudo, cajaPorNombre }) == null,
            "si SAP no sabe cómo se lee ese botón, NO se dibuja nada");

        Func<string, System.Windows.Rect?> ciego = _ => null;
        Debe((System.Windows.Rect?)de.Invoke(null, new object?[] { sel, comoSeLee, ciego }) == null,
            "y si UIA no lo encuentra tampoco: no se estima por el ancho del texto ni por el shell");

        // Y ESTO ES SOLO PARA BOTONES DE BARRA: un campo del dynpro sigue siendo de SAP, y
        // preguntarle a UIA por él devuelve el Pane opaco de siempre.
        pedidoAUia.Clear();
        Debe((System.Windows.Rect?)de.Invoke(null, new object?[]
            { "sap:wnd[0]/usr/txtRSYST-BNAME", comoSeLee, cajaPorNombre }) == null
             && pedidoAUia.Count == 0,
            "un selector que no es de barra ni se intenta: dentro del dynpro UIA sigue sin ver nada, "
            + "y preguntarle sería volver al Pane opaco por otra puerta");
    }

    private static void ComprobarNoCertificaLoQueNoAnduvo()
    {
        // LO QUE PASÓ DE VERDAD, 2026-09-03 12:12:47 y dos veces más: «hice 0 de 4 y paré en el paso
        // 1» seguido de «COMPROBADA en 4187 ms». Comprobar existe para certificar que el camino se
        // puede volver a andar; certificar uno que no se anduvo es peor que no tener el botón,
        // porque la promesa 127 —sin comprobar no se ejecuta— pasa a dejar correr cualquier cosa.
        var t = Cap004("U.WindowsClient.Navigation.LaComprobacion");
        var juzgar = t?.GetMethod("Juzgar");
        Debe(t != null && juzgar != null,
            "todavía no existe «Navigation.LaComprobacion.Juzgar» (fase 6 de la spec 009). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || juzgar == null) return;

        object Juzga(int hechos, int total) =>
            juzgar.Invoke(null, new object[] { hechos, total, $"hice {hechos} de {total}" })!;
        bool Vale(object v) => (bool)v.GetType().GetProperty("Comprobada")!.GetValue(v)!;
        string Por(object v) => (string)v.GetType().GetProperty("Motivo")!.GetValue(v)!;

        var cero = Juzga(0, 4);
        Debe(!Vale(cero), "cero pasos de cuatro NO es una comprobación: la skill sigue pendiente");
        Debe(Por(cero).Contains("0") && Por(cero).Contains("4"),
            $"y el motivo dice hasta dónde llegó, con números (dijo: «{Por(cero)}»)");

        Debe(!Vale(Juzga(1, 3)), "uno de tres tampoco: comprobar es andar el camino ENTERO");
        Debe(Vale(Juzga(3, 3)), "tres de tres sí: eso es el camino andado");

        var vacio = Juzga(0, 0);
        Debe(!Vale(vacio),
            "y un plan de CERO pasos no se certifica: «no había nada que recorrer» se parece "
            + "demasiado a «lo recorrí todo», y es justo lo contrario");
        Debe(Por(vacio).Length > 0, "y también dice por qué, que es lo que evita el botón mudo");
    }

    private static void LaTeclaViajaConSuPaso()
    {
        // 2026-09-03 12:14:08: «hice 0 de 4 y paré en el paso 1: key:enter no lo conozco». El
        // grabador emite la tecla como si fuera un elemento de la pantalla, y no lo es: no hay
        // ninguna puerta llamada «enter» en ningún sitio, así que TODA skill de SAP moría en el
        // primer paso. La tecla pertenece a lo que se tecleó, y la LLEGADA es de la tecla: escribir
        // «nwp1» no navega, el Enter sí.
        var t = Cap004("U.WindowsClient.Navigation.InstanciarSkill");
        var pasosDe = t?.GetMethod("Pasos");
        var skill = SkillDePrueba(
            ("sap:wnd[0]/tbar[0]/okcd", "nwp1", "sapgui://QAS/A", ""),
            ("key:enter", "", "sapgui://QAS/B", ""),
            ("key:f3", "", "sapgui://QAS/C", ""));
        if (t == null || pasosDe == null || skill == null)
        {
            Debe(false, "todavía no existe «InstanciarSkill.Pasos» con la tecla plegada (fase 6 de "
                + "la spec 009). La promesa está escrita y en rojo, que es donde tiene que estar");
            return;
        }

        var lista = (System.Collections.IList)pasosDe.Invoke(null, new object?[]
            { skill, new Dictionary<string, string>() })!;

        Debe(lista.Count == 2,
            $"tres pasos enseñados dan DOS del batch: el Enter se pliega sobre el tecleado y el F3 "
            + $"—que no sigue a nada tecleado— se queda solo (salieron {lista.Count})");
        if (lista.Count != 2) return;

        var p0 = lista[0]!;
        string Campo(object p) => (string)p.GetType().GetProperty("Exit")!.GetValue(p)!;
        string Texto(object p) => (string)p.GetType().GetProperty("Texto")!.GetValue(p)!;
        string Llega(object p) => (string)p.GetType().GetProperty("Llegada")!.GetValue(p)!;
        var pTecla = p0.GetType().GetProperty("Tecla");
        Debe(pTecla != null, "el paso del batch tiene que poder llevar su tecla");
        if (pTecla == null) return;

        Debe(Campo(p0) == "sap:wnd[0]/tbar[0]/okcd" && Texto(p0) == "nwp1",
            "el paso plegado sigue siendo el tecleado: mismo campo, mismo texto");
        Debe((string)pTecla.GetValue(p0)! == "enter",
            $"y se lleva la tecla consigo (lleva «{pTecla.GetValue(p0)}»)");
        Debe(Llega(p0) == "sapgui://QAS/B",
            $"Y HEREDA LA LLEGADA DE LA TECLA: escribir no navega, el Enter sí — exigir la pantalla "
            + $"de antes sería exigir no haberse movido (llegó a «{Llega(p0)}»)");

        var p1 = lista[1]!;
        Debe(Campo(p1).Length == 0 && (string)pTecla.GetValue(p1)! == "f3",
            "y el F3 suelto se queda como paso propio, sin puerta: pulsar una tecla es una acción, "
            + "no un elemento de la pantalla");
    }

    /// <summary>Lo que el batch le pidió escribir, y DÓNDE. Estático porque el delegado se fabrica
    /// por reflexión: la promesa 133 se escribió antes que la firma que juzga.</summary>
    private static readonly List<(string Campo, string Texto)> _escrituras = new();

    private static bool AnotaEscritura(string campo, string texto)
    {
        _escrituras.Add((campo, texto));
        return true;
    }

    private static void LoTecleadoVaASuCampo()
    {
        // «no pude escribir» y el respaldo por _ultimoSapPulsado: hasta hoy el batch escribía DONDE
        // ESTUVIERA EL FOCO, y quien cablea adivinaba el campo por el último elemento pulsado. En
        // una skill enseñada eso es tirar un dato que ya tenemos: el paso SABE en qué campo escribió
        // la demo. Adivinar cuando la respuesta ya viaja en el paso es cómo se pierden los datos.
        //
        // EL DELEGADO SE FABRICA POR REFLEXIÓN a propósito: esta promesa se escribió ANTES de que la
        // firma existiera, y una promesa que no compila contra el núcleo de hoy no puede ponerse en
        // rojo — se quedaría en un error de build, que no distingue «la promesa falla» de «el arnés
        // no corre» (aprendizaje nº17).
        var ctor = typeof(RecorrerSegunElNucleo).GetConstructors()[0];
        var escribir = ctor.GetParameters().FirstOrDefault(p => p.Name == "escribir");
        bool conCampo = escribir != null
            && escribir.ParameterType.IsGenericType
            && escribir.ParameterType.GetGenericArguments().Length == 3;
        Debe(conCampo,
            "quien escribe todavía recibe solo el texto (fase 6 de la spec 009): tiene que recibir "
            + "el CAMPO y el texto. La promesa está escrita y en rojo, que es donde tiene que estar");
        if (!conCampo) return;

        var g = MundoDeTres();
        string donde = "uia://x.exe/a";
        var pulsar = new PulsarSegunElNucleo(g, () => donde, (sel, et) => true) { EsperaMaximaMs = 240 };

        _escrituras.Clear();
        var anotador = Delegate.CreateDelegate(escribir!.ParameterType,
            typeof(Contrato).GetMethod(nameof(AnotaEscritura),
                BindingFlags.NonPublic | BindingFlags.Static)!);

        var args = ctor.GetParameters().Select(p =>
            p.Name == "grafo" ? (object?)g
            : p.Name == "donde" ? (object?)(Func<string>)(() => donde)
            : p.Name == "pulsar" ? (object?)pulsar
            : p.Name == "escribir" ? (object?)anotador
            : p.HasDefaultValue ? p.DefaultValue : null).ToArray();
        var batch = (RecorrerSegunElNucleo)ctor.Invoke(args);

        batch.Recorre(new[] { new RecorrerSegunElNucleo.Paso("campo:x", "hola") });

        Debe(_escrituras.Count == 1 && _escrituras[0].Campo == "campo:x" && _escrituras[0].Texto == "hola",
            "el campo donde escribir viaja con el paso, y llega entero a quien sabe escribir: "
            + $"se recibió {_escrituras.Count} escritura(s)"
            + (_escrituras.Count == 1 ? $" («{_escrituras[0].Campo}», «{_escrituras[0].Texto}»)" : ""));
    }

    private static void LoInterpretadoMandaSobreLaRegla()
    {
        // LA REGLA DEL NARRADO FALLÓ EN SU PRIMERA CORRIDA REAL (2026-09-03): «lo que narras es un
        // dato» convirtió «nwp1» —el código de transacción, o sea la navegación— en un hueco,
        // porque el dueño estaba hablando TODO EL RATO. Claro que hablaba: estaba enseñando. La
        // regla acierta en el caso que la inspiró y falla en el siguiente, que es exactamente por
        // qué el dueño decidió delegar ESTE juicio al modelo.
        var tSkill = Cap004("U.WindowsClient.Navigation.SkillEnsenada");
        var con = tSkill?.GetMethod("ConLoInterpretado");
        var leer = Cap004("U.WindowsClient.Navigation.LoQueElModeloInterpreta")?.GetMethod("Leer");
        Debe(tSkill != null && con != null && leer != null,
            "todavía no existe «SkillEnsenada.ConLoInterpretado» (fase 7 de la spec 009). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (tSkill == null || con == null || leer == null) return;

        // Los dos tecleados van narrados: para la regla, los dos son datos. Uno no lo es.
        var skill = SkillDePrueba(
            ("sap:wnd[0]/tbar[0]/okcd", "nwp1", "sapgui://QAS/B", "vamos a escribir nwp1 y enter"),
            ("sap:wnd[0]/usr/txtPESO", "70", "sapgui://QAS/C", "aquí va el peso"));
        if (skill == null) { Debe(false, "la skill de prueba se pudo empaquetar"); return; }

        Debe(skill.GetType().GetProperty("Huecos")!.GetValue(skill) is System.Collections.IList r
             && r.Count == 2,
            "de partida la regla del narrado marca los DOS como huecos — que es el fallo que esto arregla");

        string json = "{\"campos\":["
            + "{\"campo\":\"sap:wnd[0]/tbar[0]/okcd\",\"esDato\":false,\"significado\":\"el código de transacción: es cómo se llega\"},"
            + "{\"campo\":\"sap:wnd[0]/usr/txtPESO\",\"esDato\":true,\"significado\":\"el peso del paciente\"}]}";
        var interpretado = leer.Invoke(null, new object?[] { json, skill })!;

        var conModelo = con.Invoke(skill, new object?[] { interpretado })!;
        var huecos = (System.Collections.IList)conModelo.GetType().GetProperty("Huecos")!.GetValue(conModelo)!;
        Debe(huecos.Count == 1,
            $"el modelo deja UN hueco de los dos: lo que llamó navegación deja de serlo (quedaron {huecos.Count})");
        if (huecos.Count == 1)
        {
            var h = huecos[0]!;
            Debe((string)h.GetType().GetProperty("Campo")!.GetValue(h)! == "sap:wnd[0]/usr/txtPESO",
                "y el que queda es el dato del paciente, no el código de transacción");
            Debe((string)h.GetType().GetProperty("Significado")!.GetValue(h)! == "el peso del paciente",
                "con el significado CORTO del modelo, no con el párrafo que se dijo mientras");
        }

        // Y «nwp1» vuelve a reproducirse: sin eso la skill no arranca, que es el bug entero.
        var pasosDe = Cap004("U.WindowsClient.Navigation.InstanciarSkill")?.GetMethod("Pasos");
        if (pasosDe != null)
        {
            var plan = (System.Collections.IList)pasosDe.Invoke(null, new object?[]
                { conModelo, new Dictionary<string, string>() })!;
            bool hayNwp1 = false;
            foreach (var p in plan)
                if ((string)p!.GetType().GetProperty("Texto")!.GetValue(p)! == "nwp1") hayNwp1 = true;
            Debe(hayNwp1,
                "y «nwp1» vuelve al plan: es navegación, y sin él la skill no arranca — ese fue el "
                + "0 de 4 del 2026-09-03");
        }

        // SIN INTERPRETACIÓN NO SE PIERDE NADA (la 130, ahora aplicada): la regla sigue mandando.
        var sinNada = leer.Invoke(null, new object?[] { "", skill })!;
        var comoEstaba = con.Invoke(skill, new object?[] { sinNada })!;
        Debe(comoEstaba.GetType().GetProperty("Huecos")!.GetValue(comoEstaba) is System.Collections.IList r2
             && r2.Count == 2,
            "y si el modelo no opinó, los huecos de la regla se quedan intactos: se prefiere el "
            + "criterio del modelo, no se depende de él");
    }

    private static void AlVideoSeLePreguntaPorEstosPasos()
    {
        // EL GUARDARRAÍL DE LA IDENTIDAD, en el lado de la PREGUNTA. La 129 ya descarta lo que el
        // modelo nombre de más al leer la respuesta; esta promesa dice que la pregunta tampoco le
        // ofrece campos que la demo no tocó. Las dos, porque un guardarraíl solo en la lectura
        // depende de que nadie cambie el orden de las capas.
        var t = Cap004("U.WindowsClient.Teach.LoQueSePregunta");
        var de = t?.GetMethod("De");
        Debe(t != null && de != null,
            "todavía no existe «Teach.LoQueSePregunta.De» (fase 7 de la spec 009). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || de == null) return;

        var skill = SkillDePrueba(
            ("sap:wnd[0]/tbar[0]/okcd", "nwp1", "sapgui://QAS/B", "escribimos nwp1"),
            ("sap:wnd[0]/usr/txtPESO", "70", "sapgui://QAS/C", "aquí va el peso"),
            ("sap:wnd[0]/tbar[0]/btn[0]", "", "sapgui://QAS/D", ""));
        if (skill == null) { Debe(false, "la skill de prueba se pudo empaquetar"); return; }

        var lista = (System.Collections.IList)de.Invoke(null, new object?[] { skill })!;
        Debe(lista.Count == 3,
            $"viajan los tres pasos de la demo, ni uno más (viajaron {lista.Count})");
        if (lista.Count == 0) return;

        var p0 = lista[0]!;
        foreach (string prop in new[] { "Campo", "Valor", "Dicho" })
            Debe(p0.GetType().GetProperty(prop) != null,
                $"cada paso viaja con su «{prop}»: sin el valor el modelo no puede juzgar si es dato, "
                + "y sin lo dicho no tiene el contexto que lo hace mejor que una regla");

        Debe((string)p0.GetType().GetProperty("Campo")!.GetValue(p0)! == "sap:wnd[0]/tbar[0]/okcd"
             && (string)p0.GetType().GetProperty("Valor")!.GetValue(p0)! == "nwp1",
            "y van con su identidad exacta —el selector—, que es lo que permite casar la respuesta "
            + "sin que el modelo tenga que acertar un nombre");

        // Nada que no esté en la demo puede colarse: la lista ES la demo.
        var campos = new List<string>();
        foreach (var p in lista) campos.Add((string)p!.GetType().GetProperty("Campo")!.GetValue(p)!);
        Debe(!campos.Contains("sap:wnd[0]/usr/txtJAMAS"),
            "un campo que la demo no tocó no aparece en la pregunta, porque no hay de dónde sacarlo");
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

    // ── LA CONSULTA CLÍNICA (spec 004): el arnés ─────────────────────────────

    /// <summary>
    /// El ensamblado del cliente. Las capacidades de la spec 004 se piden por NOMBRE y con
    /// reflexión, como manda <c>.claude/rules/flujo-sdd.md</c>: así el contrato sigue compilando
    /// contra un núcleo que todavía no las tiene, y una promesa sin código dice PENDIENTE —que
    /// cuenta como incumplida— en vez de «no aplicable», que se sumaría al verde y haría que el
    /// contrato certificara el vacío.
    /// </summary>
    private static readonly Assembly Cliente = typeof(Freno).Assembly;

    private static Type? Capacidad(string nombre) => Cliente.GetType(nombre);

    private static void Pendiente(string que, string promesa, string spec = "004")
    {
        _fallos++;
        Console.WriteLine($"   ⧗ PENDIENTE: «{que}» todavía no existe (spec {spec}). La promesa "
                        + $"{promesa} está escrita y en ROJO, que es donde tiene que estar.");
    }

    /// <summary>
    /// Un backend de mentira que ANOTA lo que se le pidió.
    /// </summary>
    /// <remarks>
    /// El registro no es comodidad: es lo único que distingue «no se creó el encounter» de «se creó
    /// y falló», y esa distinción ES la promesa 84. Un doble que solo devuelve respuestas dejaría
    /// pasar exactamente el bug que se quiere impedir — un criterio que no puede fallar con el bug
    /// presente no es un criterio (patrón nº7).
    /// </remarks>
    private sealed class BackendDeMentira : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, (HttpStatusCode, string)> _responder;

        public BackendDeMentira(Func<HttpRequestMessage, (HttpStatusCode, string)> responder)
            => _responder = responder;

        /// <summary>Método + ruta de cada petición, en orden. Sin cuerpo: aquí no viaja PHI.</summary>
        public List<string> Peticiones { get; } = new();

        /// <summary>Las cabeceras de la última petición, para juzgar la atribución.</summary>
        public Dictionary<string, string> UltimasCabeceras { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Peticiones.Add($"{req.Method} {req.RequestUri?.PathAndQuery}");
            UltimasCabeceras.Clear();
            foreach (var h in req.Headers) UltimasCabeceras[h.Key] = string.Join(",", h.Value);

            var (codigo, cuerpo) = _responder(req);
            return Task.FromResult(new HttpResponseMessage(codigo)
            {
                Content = new StringContent(cuerpo, Encoding.UTF8, "application/json"),
            });
        }
    }

    /// <summary>
    /// Un JWT con la forma de uno de Supabase: cabecera, carga y firma en base64url. No se firma de
    /// verdad y da igual — quien lo verifica es el backend; aquí solo hace falta que el cliente sepa
    /// sacarle el <c>sub</c>, que es el uuid que Graph valida contra `profiles`.
    /// </summary>
    private static string JwtDeMentira(string sub, string marca = "x")
    {
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return B64("{\"alg\":\"HS256\",\"typ\":\"JWT\"}") + "."
             + B64($"{{\"sub\":\"{sub}\",\"email\":\"medico@miracle.app\",\"marca\":\"{marca}\"}}") + "."
             + B64("firma-de-mentira");
    }

    /// <summary>La respuesta de <c>POST /auth/v1/token</c> tal como la devuelve Supabase.</summary>
    private static string RespuestaDeLogin(string sub, string marca, int duracionSegundos) =>
        $"{{\"access_token\":\"{JwtDeMentira(sub, marca)}\",\"refresh_token\":\"refresh-{marca}\","
        + $"\"expires_in\":{duracionSegundos},\"token_type\":\"bearer\","
        + $"\"user\":{{\"id\":\"{sub}\",\"email\":\"medico@miracle.app\"}}}}";

    // ── LA CONSULTA CLÍNICA (spec 004): las promesas ─────────────────────────

    /// <remarks>
    /// LA QUE ABRE TODO. Sin el JWT del médico, `/api/clinical/*` contesta 401 —el backend lo exige
    /// con `requireClinicalAuth`—, así que sin esta promesa no hay encounter, no hay nota, y «misma
    /// base de datos que la web» es una frase y no un hecho.
    ///
    /// Se juzga por lo que NO pasó, y por eso hacen falta las tres afirmaciones: que conteste «no»
    /// es lo barato; que no abra el micrófono de alguien que no ha entrado y que no deje un
    /// encounter huérfano en la base es lo que de verdad protege.
    /// </remarks>
    private static void SinSesionLaConsultaNoEmpieza()
    {
        var tConsulta = Capacidad("U.WindowsClient.Clinical.Consulta");
        var tClinica = Capacidad("U.WindowsClient.Clinical.ClinicaClient");
        var tSesion = Capacidad("U.WindowsClient.Cuenta.SesionMiracle");
        if (tConsulta == null || tClinica == null || tSesion == null)
        {
            Pendiente("Clinical.Consulta · Clinical.ClinicaClient · Cuenta.SesionMiracle", "84");
            return;
        }

        var backend = new BackendDeMentira(_ => (HttpStatusCode.OK, "{}"));
        var sesion = Activator.CreateInstance(tSesion,
            "https://supabase.test", "publishable", backend,
            (Func<DateTimeOffset>)(() => DateTimeOffset.UnixEpoch))!;
        // Nadie ha entrado. No se llama a EntrarAsync a propósito: ESE es el escenario.

        var clinica = Activator.CreateInstance(tClinica, "https://graph.test", sesion, backend)!;

        bool microfonoAbierto = false;
        var consulta = Activator.CreateInstance(tConsulta, sesion, clinica,
            (Func<CancellationToken, Task<bool>>)(_ => { microfonoAbierto = true; return Task.FromResult(true); }),
            (Func<Task<string>>)(() => Task.FromResult("")),
            // El espejo va explícito aunque sea opcional: Activator no rellena los que faltan.
            null)!;

        bool arranco = ((Task<bool>)tConsulta.GetMethod("EmpezarAsync")!
            .Invoke(consulta, new object?[] { "plantilla-x", CancellationToken.None })!)
            .GetAwaiter().GetResult();

        Debe(!arranco, "sin sesión, empezar la consulta contesta que NO");
        Debe(!microfonoAbierto,
            "y sobre todo NO se abre el micrófono: grabar a un paciente sin saber de quién es la "
            + "consulta es lo único aquí que no tiene vuelta atrás");
        Debe(backend.Peticiones.Count == 0,
            "ni se crea un encounter que quedaría huérfano en la base. La cuenta es sobre lo que se "
            + $"PIDIÓ, no sobre lo que falló: se pidieron {backend.Peticiones.Count}");
    }

    /// <remarks>
    /// POR ADELANTADO Y CON MARGEN, no cuando llega el 401. Un refresco reactivo convierte cada
    /// expiración en una llamada fallida visible, y en mitad de una consulta eso es una frase
    /// perdida. Se mide con RELOJ FALSO: esperar cinco minutos en una prueba la volvería caprichosa,
    /// y un juez caprichoso deja de creerse.
    ///
    /// Las dos mitades son la promesa entera: que renueve cuando toca (si no, se usa un token
    /// muerto) y que NO renueve cuando no toca (una llamada de red por consulta, por gusto).
    /// </remarks>
    private static void ElTokenSeRenuevaAntesDeUsarse()
    {
        var t = Capacidad("U.WindowsClient.Cuenta.SesionMiracle");
        if (t == null) { Pendiente("Cuenta.SesionMiracle", "85"); return; }

        var t0 = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var ahora = t0;
        int refrescos = 0;

        var backend = new BackendDeMentira(req =>
        {
            string url = req.RequestUri?.ToString() ?? "";
            if (url.Contains("refresh_token"))
            {
                refrescos++;
                return (HttpStatusCode.OK, RespuestaDeLogin("medico-1", "renovado", 3600));
            }
            return (HttpStatusCode.OK, RespuestaDeLogin("medico-1", "primero", 300));
        });

        var sesion = Activator.CreateInstance(t,
            "https://supabase.test", "publishable", backend, (Func<DateTimeOffset>)(() => ahora))!;

        ((Task<bool>)t.GetMethod("EntrarAsync")!
            .Invoke(sesion, new object?[] { "medico@miracle.app", "clave", CancellationToken.None })!)
            .GetAwaiter().GetResult();

        var pedirToken = t.GetMethod("TokenVigenteAsync")!;
        string primero = ((Task<string>)pedirToken.Invoke(sesion, new object?[] { CancellationToken.None })!)
            .GetAwaiter().GetResult();
        Debe(refrescos == 0,
            "recién entrado el token está fresco (le quedan 300 s): pedirlo no gasta una llamada de red");

        // Quedan 50 s de vida. NO ha caducado —un refresco reactivo aquí no haría nada—, pero está
        // dentro del margen, que es exactamente el hueco donde se pierde la frase.
        ahora = t0.AddSeconds(250);
        string segundo = ((Task<string>)pedirToken.Invoke(sesion, new object?[] { CancellationToken.None })!)
            .GetAwaiter().GetResult();

        Debe(refrescos == 1, $"dentro del margen se renueva UNA vez, no {refrescos}");
        Debe(segundo != primero && segundo.Length > 0,
            "y lo que se entrega es el token NUEVO: renovar y seguir usando el viejo es no renovar");
    }

    /// <remarks>
    /// Cerrar sesión en un ordenador compartido —que es el caso del hospital— tiene que dejar la
    /// máquina como si nadie hubiera entrado. Se mira el ARCHIVO, no un booleano en memoria: un
    /// `Salir()` que solo pone una bandera deja el refresh token en disco, y con él se vuelve a
    /// entrar sin contraseña.
    /// </remarks>
    private static void SalirNoDejaRastro()
    {
        var t = Capacidad("U.WindowsClient.Cuenta.SesionMiracle");
        if (t == null) { Pendiente("Cuenta.SesionMiracle", "86"); return; }

        var backend = new BackendDeMentira(_ =>
            (HttpStatusCode.OK, RespuestaDeLogin("medico-1", "unico", 3600)));
        var sesion = Activator.CreateInstance(t,
            "https://supabase.test", "publishable", backend,
            (Func<DateTimeOffset>)(() => DateTimeOffset.UtcNow))!;

        string archivo = (string)t.GetProperty("RutaDeLaCredencial",
            BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

        ((Task<bool>)t.GetMethod("EntrarAsync")!
            .Invoke(sesion, new object?[] { "medico@miracle.app", "clave", CancellationToken.None })!)
            .GetAwaiter().GetResult();
        Debe(File.Exists(archivo),
            "entrar deja la credencial guardada (si no, no habría nada que juzgar en esta promesa)");

        t.GetMethod("Salir")!.Invoke(sesion, null);
        Debe(!File.Exists(archivo), $"salir la borra del disco, y no solo de la memoria: {archivo}");
        Debe((bool)t.GetProperty("HayMedico")!.GetValue(sesion)! == false,
            "y la sesión deja de decir que hay médico");
    }

    /// <remarks>
    /// Los doce códigos del contrato del backend (docs/backend-clinical-api-contract.md) tienen que
    /// llegar al médico como doce cosas distintas. Un `catch` que contesta «no se pudo generar la
    /// nota» para los doce es el aprendizaje nº2 —un mensaje que no distingue sus causas manda la
    /// investigación al lugar equivocado— cometido otra vez, y aquí el que investiga es alguien con
    /// un paciente delante: «falta configurar el proveedor» y «el texto está vacío» se arreglan de
    /// formas opuestas.
    /// </remarks>
    private static void CadaFalloClinicoDiceLoSuyo()
    {
        var t = Capacidad("U.WindowsClient.Clinical.MensajesClinicos");
        if (t == null) { Pendiente("Clinical.MensajesClinicos", "87"); return; }

        string[] codigos =
        {
            "TEMPLATE_NOT_FOUND", "TEMPLATE_INVALID", "ENCOUNTER_NOT_FOUND", "ENCOUNTER_INVALID",
            "TRANSCRIPT_REQUIRED", "TRANSCRIPT_TOO_LONG", "LLM_NOT_CONFIGURED",
            "NOTE_GENERATION_FAILED", "NOTE_JSON_INVALID", "UNAUTHORIZED",
            "SUPABASE_NOT_CONFIGURED", "INTERNAL_ERROR",
        };

        var traducir = t.GetMethod("Traducir", new[] { typeof(string) })!;
        var dichos = codigos.Select(c => (string)traducir.Invoke(null, new object?[] { c })!).ToList();

        Debe(dichos.All(d => !string.IsNullOrWhiteSpace(d)), "ningún código se queda sin frase");
        Debe(dichos.Distinct().Count() == codigos.Length,
            $"los {codigos.Length} códigos del contrato dicen {codigos.Length} cosas distintas; "
            + $"hoy dicen {dichos.Distinct().Count()}");

        // Y un código que no conocemos NO se disfraza de conocido: se dice tal cual, para que el
        // día que el backend añada uno se vea en el log en vez de perderse en un genérico.
        string desconocido = (string)traducir.Invoke(null, new object?[] { "ALGO_NUEVO_DEL_BACKEND" })!;
        Debe(desconocido.Contains("ALGO_NUEVO_DEL_BACKEND"),
            "un código desconocido se nombra, no se traga: si no, el backend puede cambiar sin que "
            + "nadie se entere");
    }

    /// <remarks>
    /// Es el patrón nº10 —un paso no ejecutado deja rastro— aplicado al texto: el médico para de
    /// grabar a media frase y esa frase, que suele ser la conclusión, se queda dentro del
    /// acumulador si el verbatim solo cuenta lo CERRADO. El sabotaje que lo destapa es de una
    /// línea: que `Todo` devuelva únicamente las frases cerradas.
    /// </remarks>
    private static void ElVerbatimNoSeDejaLaUltimaFrase()
    {
        var t = Capacidad("U.WindowsClient.Clinical.Transcripcion.Verbatim");
        if (t == null) { Pendiente("Clinical.Transcripcion.Verbatim", "88"); return; }

        var v = Activator.CreateInstance(t)!;
        var confirmar = t.GetMethod("Confirmar")!;
        var cerrar = t.GetMethod("CerrarFrase")!;

        confirmar.Invoke(v, new object?[] { "El paciente refiere cefalea de tres días." });
        cerrar.Invoke(v, null);                          // <end>: Soniox cerró la frase
        confirmar.Invoke(v, new object?[] { " Impresión: cefalea tensional" });
        // Y AQUÍ SE PARA DE GRABAR. Sin <end>: es lo que pasa de verdad cuando alguien deja de
        // hablar y pulsa el botón.

        string todo = (string)t.GetProperty("Todo")!.GetValue(v)!;
        Debe(todo.Contains("cefalea de tres días"), "lo cerrado viaja");
        Debe(todo.Contains("cefalea tensional"),
            "y lo que quedó sin cerrar TAMBIÉN viaja: se dijo. Perderlo es perder justo la frase "
            + "por la que se grabó la consulta");

        // Que cuente lo que hay: un verbatim vacío tiene que poder distinguirse de uno con texto,
        // porque de esa distinción depende que se llame o no a /transcript (400 TRANSCRIPT_REQUIRED).
        var vacio = Activator.CreateInstance(t)!;
        Debe((bool)t.GetProperty("Vacio")!.GetValue(vacio)! == true, "recién nacido está vacío");
        Debe((bool)t.GetProperty("Vacio")!.GetValue(v)! == false, "y con texto dentro, no");
    }

    /// <remarks>
    /// EL PROVEEDOR LO DECIDE EL PROVIDER STUDIO, no el nombre del archivo. Hoy el cliente solo sabe
    /// leer Soniox (`DictadoSoniox.MensajeDeArranque` devuelve "" si la sesión no trae
    /// `start_message`), así que el día que se conmute a Deepgram el dictado muere diciendo «el
    /// backend no devolvió la configuración del stream» — un mensaje que no distingue «es Deepgram»
    /// de «el backend falló». El motor de la web lleva desde siempre hablando los dos.
    ///
    /// Se juzgan las dos mitades: cómo se ABRE el socket (subprotocolo o primer mensaje) y cómo se
    /// LEE lo que llega. Solo la primera dejaría pasar un lector que conecta y no entiende nada.
    /// </remarks>
    private static void ElLectorLoEligeLaSesion()
    {
        var t = Capacidad("U.WindowsClient.Clinical.Transcripcion.SesionDeStream");
        var tVerbatim = Capacidad("U.WindowsClient.Clinical.Transcripcion.Verbatim");
        if (t == null || tVerbatim == null)
        {
            Pendiente("Clinical.Transcripcion.SesionDeStream", "89");
            return;
        }

        var leer = t.GetMethod("Leer", BindingFlags.Public | BindingFlags.Static)!;

        // Soniox: autentica y se configura en el PRIMER MENSAJE, socket pelado.
        var soniox = leer.Invoke(null, new object?[]
        {
            "{\"provider\":\"soniox\",\"auth_scheme\":\"message\",\"access_token\":\"tok-sx\","
            + "\"websocket_url\":\"wss://stt.soniox.test/transcribe\","
            + "\"start_message\":{\"api_key\":\"tok-sx\",\"model\":\"stt-rt\",\"audio_format\":\"auto\"}}",
        })!;
        var lectorSx = t.GetProperty("Lector")!.GetValue(soniox)!;
        var tLector = lectorSx.GetType();

        Debe((string)tLector.GetProperty("Nombre")!.GetValue(lectorSx)! == "soniox",
            "una sesión con auth_scheme «message» se lee con el lector de Soniox");
        Debe(((System.Collections.IEnumerable)tLector.GetProperty("Subprotocolos")!.GetValue(lectorSx)!)
                .Cast<object>().Count() == 0,
            "Soniox abre un socket PELADO: declarar subprotocolo lo cierra sin decir por qué");
        string arranque = (string?)tLector.GetMethod("MensajeDeArranque")!
            .Invoke(lectorSx, new object?[] { 16000 }) ?? "";
        Debe(arranque.Contains("pcm_s16le") && arranque.Contains("16000"),
            "y el formato se DECLARA: el backend pide «auto», que sirve para WebM (trae cabecera) "
            + "pero no para el PCM crudo del micrófono, que no tiene ninguna (medido 2026-08-14)");

        // Deepgram: autentica por subprotocolo, no manda primer mensaje.
        var deepgram = leer.Invoke(null, new object?[]
        {
            "{\"provider\":\"deepgram\",\"auth_scheme\":\"bearer\",\"access_token\":\"tok-dg\","
            + "\"websocket_url\":\"wss://api.deepgram.test/v1/listen\",\"start_message\":null}",
        })!;
        var lectorDg = t.GetProperty("Lector")!.GetValue(deepgram)!;
        // Cada lector se interroga por SU tipo: son dos clases distintas —eso es justo lo que la
        // promesa afirma— y pedirle a una las propiedades de la otra tira TargetException.
        var tLectorDg = lectorDg.GetType();

        Debe((string)tLectorDg.GetProperty("Nombre")!.GetValue(lectorDg)! == "deepgram",
            "y una con auth_scheme «bearer» se lee con el de Deepgram");
        var subs = ((System.Collections.IEnumerable)tLectorDg.GetProperty("Subprotocolos")!.GetValue(lectorDg)!)
            .Cast<string>().ToList();
        Debe(subs.Count == 2 && subs[0] == "bearer" && subs[1] == "tok-dg",
            "Deepgram autentica en la TUPLA del subprotocolo: [esquema, token]");
        Debe(tLectorDg.GetMethod("MensajeDeArranque")!.Invoke(lectorDg, new object?[] { 16000 }) == null,
            "y no manda primer mensaje: mandarlo sería audio que Deepgram no espera");

        // La otra mitad: que lo que llega se ENTIENDA. Dos formas de mensaje irreconciliables.
        var vSx = Activator.CreateInstance(tVerbatim)!;
        var vDg = Activator.CreateInstance(tVerbatim)!;
        var digerir = tLector.GetMethod("Digerir")!;
        var nada = (Action<string>)(_ => { });

        digerir.Invoke(lectorSx, new object?[]
        {
            "{\"tokens\":[{\"text\":\"hola \",\"is_final\":true},{\"text\":\"doctor\",\"is_final\":true}]}",
            vSx, nada, nada, nada,
        });
        tLectorDg.GetMethod("Digerir")!.Invoke(lectorDg, new object?[]
        {
            "{\"is_final\":true,\"channel\":{\"alternatives\":[{\"transcript\":\"hola doctor\"}]}}",
            vDg, nada, nada, nada,
        });

        var todo = tVerbatim.GetProperty("Todo")!;
        Debe(((string)todo.GetValue(vSx)!).Contains("hola doctor"),
            "de Soniox se leen los tokens confirmados");
        Debe(((string)todo.GetValue(vDg)!).Contains("hola doctor"),
            "y de Deepgram, channel.alternatives[0].transcript — la misma frase por dos caminos que "
            + "no se parecen en nada");
    }

    /// <remarks>
    /// Es el aprendizaje nº16 —una comparación entre identidades de distinta forma da falso siempre,
    /// y en silencio— por quinta vez en este repo. La web manda `X-Miracle-User-Id` con el uuid del
    /// token y Graph lo valida contra `profiles`; el cliente Windows manda hoy
    /// `X-Miracle-User-Email`. Los dos lados creen que están atribuyendo, y uno de los dos no lo
    /// está: los minutos de transcripción de Windows no caen en la cuenta del médico.
    /// </remarks>
    private static void LaConsultaEsDelMedico()
    {
        var t = Capacidad("U.WindowsClient.Cuenta.SesionMiracle");
        if (t == null) { Pendiente("Cuenta.SesionMiracle", "90"); return; }

        const string uuid = "7b8a4c8e-1f2d-4c3b-9a10-0d1e2f3a4b5c";
        var backend = new BackendDeMentira(_ =>
            (HttpStatusCode.OK, RespuestaDeLogin(uuid, "unico", 3600)));
        var sesion = Activator.CreateInstance(t,
            "https://supabase.test", "publishable", backend,
            (Func<DateTimeOffset>)(() => DateTimeOffset.UtcNow))!;

        var cabeceras = t.GetMethod("CabecerasDeAtribucion")!;

        // Antes de entrar no se atribuye nada. Inventar aquí un id de máquina sería atribuirle a
        // alguien un consumo que no hizo.
        var sinEntrar = (IReadOnlyDictionary<string, string>)cabeceras.Invoke(sesion, null)!;
        Debe(!sinEntrar.ContainsKey("X-Miracle-User-Id"),
            "sin médico dentro no se atribuye a nadie");

        ((Task<bool>)t.GetMethod("EntrarAsync")!
            .Invoke(sesion, new object?[] { "medico@miracle.app", "clave", CancellationToken.None })!)
            .GetAwaiter().GetResult();

        var con = (IReadOnlyDictionary<string, string>)cabeceras.Invoke(sesion, null)!;
        Debe(con.TryGetValue("X-Miracle-User-Id", out var id) && id == uuid,
            "la atribución viaja con el UUID del token, que es lo que Graph valida contra profiles — "
            + "no el correo, que es de otra forma y no casa");
        Debe(con.TryGetValue("X-Miracle-App", out var app) && app == "windows_app",
            "y se dice desde qué app, para poder separar este consumo del de la web");
        Debe((string)t.GetProperty("MedicoId")!.GetValue(sesion)! == uuid,
            "el mismo uuid es el que la consulta usa como identidad del médico: una sola fuente");
    }

    /// <remarks>
    /// «Terminé» no es un veredicto: el puente consciente ya declaró éxito habiendo pulsado el botón
    /// equivocado. Aquí lo mismo con la nota — si `generate-note` falla y la consulta se marca
    /// terminada, el médico cree que su nota está en la base y no está.
    ///
    /// Y la otra mitad, que es la que hace la promesa útil: el `encounter_id` SOBREVIVE al fallo.
    /// El contrato del backend permite repetir generate-note mientras haya transcript; reintentar
    /// sobre el mismo encounter no duplica nada, y volver a crearlo sí.
    /// </remarks>
    private static void SinNotaNoHayConsultaTerminada()
    {
        var tConsulta = Capacidad("U.WindowsClient.Clinical.Consulta");
        var tClinica = Capacidad("U.WindowsClient.Clinical.ClinicaClient");
        var tSesion = Capacidad("U.WindowsClient.Cuenta.SesionMiracle");
        if (tConsulta == null || tClinica == null || tSesion == null)
        {
            Pendiente("Clinical.Consulta", "91");
            return;
        }

        var backend = new BackendDeMentira(req =>
        {
            string ruta = req.RequestUri?.AbsolutePath ?? "";
            if (ruta.Contains("/auth/v1/token"))
                return (HttpStatusCode.OK, RespuestaDeLogin("medico-1", "unico", 3600));
            if (ruta.EndsWith("/generate-note"))
                return (HttpStatusCode.BadGateway,
                    "{\"error\":{\"code\":\"NOTE_GENERATION_FAILED\",\"message\":\"el LLM falló\"}}");
            if (ruta.EndsWith("/transcript"))
                return (HttpStatusCode.OK,
                    "{\"encounter_id\":\"enc-1\",\"status\":\"transcript_ready\",\"transcript_length\":42}");
            return (HttpStatusCode.Created, "{\"encounter_id\":\"enc-1\",\"status\":\"created\"}");
        });

        var sesion = Activator.CreateInstance(tSesion,
            "https://supabase.test", "publishable", backend,
            (Func<DateTimeOffset>)(() => DateTimeOffset.UtcNow))!;
        ((Task<bool>)tSesion.GetMethod("EntrarAsync")!
            .Invoke(sesion, new object?[] { "medico@miracle.app", "clave", CancellationToken.None })!)
            .GetAwaiter().GetResult();

        var clinica = Activator.CreateInstance(tClinica, "https://graph.test", sesion, backend)!;
        var consulta = Activator.CreateInstance(tConsulta, sesion, clinica,
            (Func<CancellationToken, Task<bool>>)(_ => Task.FromResult(true)),
            (Func<Task<string>>)(() => Task.FromResult("El paciente refiere cefalea.")),
            null)!;

        bool arranco = ((Task<bool>)tConsulta.GetMethod("EmpezarAsync")!
            .Invoke(consulta, new object?[] { "plantilla-x", CancellationToken.None })!)
            .GetAwaiter().GetResult();
        Debe(arranco, "con médico dentro, la consulta sí empieza (si no, esta promesa no juzga nada)");

        ((Task)tConsulta.GetMethod("TerminarAsync")!
            .Invoke(consulta, new object?[] { CancellationToken.None })!)
            .GetAwaiter().GetResult();

        string estado = tConsulta.GetProperty("Estado")!.GetValue(consulta)!.ToString()!;
        string motivo = (string)tConsulta.GetProperty("Motivo")!.GetValue(consulta)!;

        Debe(estado != "NotaLista",
            $"con la nota sin generar, la consulta NO se declara terminada; dice «{estado}»");
        Debe(motivo.Length > 0 && motivo != "no se pudo",
            $"y el motivo se puede NOMBRAR, que es lo que decide si se reintenta o se llama a "
            + $"alguien: «{motivo}»");
        Debe((string)tConsulta.GetProperty("EncounterId")!.GetValue(consulta)! == "enc-1",
            "el encounter SOBREVIVE al fallo: reintentar sobre él no duplica la consulta, volver a "
            + "crearlo sí");
        Debe(backend.Peticiones.Count(p => p.Contains("/transcript")) == 1,
            "y el texto se guardó UNA vez: el fallo fue de la nota, no de la transcripción");
    }

    /// <remarks>
    /// Las tres afirmaciones son la promesa entera, y la tercera es la que más costó: **cancelar no
    /// es fallar**. Si cerrar el login sin entrar disparara el aviso, el aviso se volvería ruido —
    /// y un aviso que sale cuando no pasa nada se aprende a ignorar, que es como se pierde el que
    /// sí importa.
    /// </remarks>
    private static void ElArranqueDiceDondeSeQuedo()
    {
        var t = Capacidad("U.WindowsClient.Clinical.ArranqueDeConsulta");
        var correr = t?.GetMethod("Correr");
        if (t == null || correr == null) { Pendiente("Clinical.ArranqueDeConsulta", "92"); return; }

        object Armar(Func<bool> restaurar, Func<bool> login, Action abrir, Action<string, string> avisar)
            => Activator.CreateInstance(t, restaurar, login, abrir, avisar)!;

        // 1. La ventana revienta: se avisa, con el PASO nombrado y el porqué REAL.
        string paso = "", porque = "";
        var revienta = Armar(() => false, () => true,
            () => throw new InvalidOperationException("no hay micrófono en este equipo"),
            (p, q) => { paso = p; porque = q; });
        Debe((bool)correr.Invoke(revienta, null)! == false,
            "si la ventana no abre, el arranque contesta que NO");
        Debe(paso.Length > 0 && !paso.Equals("no se pudo", StringComparison.OrdinalIgnoreCase),
            $"y nombra el PASO en el que se quedó, no una conclusión: «{paso}»");
        Debe(porque.Contains("no hay micrófono en este equipo"),
            $"con el motivo REAL dentro, no un genérico que manda a mirar donde no es: «{porque}»");

        // 2. Cancelar el login NO es un fallo: no se avisa de nada.
        bool avisaron = false;
        var cancela = Armar(() => false, () => false, () => { }, (_, _) => avisaron = true);
        Debe((bool)correr.Invoke(cancela, null)! == false, "cancelar tampoco abre la ventana");
        Debe(!avisaron,
            "pero NO se avisa: cerrar el login sin entrar es una decisión, no una avería, y un "
            + "aviso que salta cuando no pasa nada se aprende a ignorar");

        // 3. Con sesión restaurada no se pide contraseña, y el camino feliz no avisa de nada.
        bool pidioLogin = false, avisoEnFeliz = false, abrio = false;
        var feliz = Armar(() => true, () => { pidioLogin = true; return true; },
            () => abrio = true, (_, _) => avisoEnFeliz = true);
        Debe((bool)correr.Invoke(feliz, null)! && abrio, "con sesión guardada se abre la ventana");
        Debe(!pidioLogin,
            "y no se pide la contraseña otra vez: para eso se guardó la sesión (promesa 85)");
        Debe(!avisoEnFeliz, "el camino feliz no enseña ningún aviso");
    }

    /// <remarks>
    /// EL MAPEO ES EL DEL PORTAL, campo por campo (lib/clinical/encounter-to-consultation.ts): si
    /// aquí se inventara otro, la misma consulta se vería distinta en cada sitio — que es peor que
    /// no verse, porque nadie sospecharía. Se juzga la FILA que se manda, no que la llamada no
    /// falle: un POST que sale con `note` vacío devuelve 201 igual.
    /// </remarks>
    private static void ElEspejoSeEscribe()
    {
        var t = Capacidad("U.WindowsClient.Clinical.EspejoDeConsulta");
        var fabricar = t?.GetMethod("Fila", BindingFlags.Public | BindingFlags.Static);
        if (t == null || fabricar == null) { Pendiente("Clinical.EspejoDeConsulta.Fila", "93"); return; }

        var tNota = Capacidad("U.WindowsClient.Clinical.NotaClinica")!;
        var nota = tNota.GetMethod("Leer", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, new object?[]
            {
                JsonDocument.Parse(
                    "{\"summary\":\"Cefalea de tres días sin signos de alarma.\","
                    + "\"sections\":[{\"key\":\"motivo_consulta\",\"label\":\"Motivo de consulta\","
                    + "\"content\":\"Cefalea de 3 días.\"},{\"key\":\"plan\",\"label\":\"Plan\","
                    + "\"content\":\"Hidratación y control.\"}],"
                    + "\"warnings\":[],\"missing_required_sections\":[]}").RootElement,
            })!;

        string json = (string)fabricar.Invoke(null, new object?[]
        {
            "enc-1", nota, "El paciente refiere cefalea.", "Consulta inicial adulto",
            "medicina_general", new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
        })!;

        using var fila = JsonDocument.Parse(json);
        var r = fila.RootElement;

        string Str(string campo) => r.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";

        Debe(Str("id") == "enc-1",
            "la fila usa el MISMO id que el encounter: así el puente es 1:1 e idempotente, y "
            + "/app/consultas/<id> del portal lleva a esta consulta");
        Debe(Str("estado") == "borrador",
            "nace en borrador, que es lo que la mete en el ciclo de revisión y firma del portal");
        Debe(Str("resumen").Contains("Cefalea de tres días"), "el resumen viaja");
        Debe(Str("motivo").Contains("Cefalea de 3 días"),
            $"y el motivo sale de la sección «motivo…», como en el portal: «{Str("motivo")}»");
        Debe(Str("plantilla") == "Consulta inicial adulto", "la plantilla se nombra");

        // La nota: sections del contrato → el shape del portal (id/titulo/kind/texto). Un nombre de
        // campo distinto aquí y el detalle del portal pinta secciones vacías sin dar ningún error.
        Debe(r.TryGetProperty("note", out var note) && note.ValueKind == JsonValueKind.Array
             && note.GetArrayLength() == 2, "las dos secciones viajan");
        var primera = note[0];
        Debe(primera.TryGetProperty("id", out var sid) && sid.GetString() == "motivo_consulta"
             && primera.TryGetProperty("titulo", out _) && primera.TryGetProperty("kind", out var k)
             && k.GetString() == "texto" && primera.TryGetProperty("texto", out var tx)
             && (tx.GetString() ?? "").Contains("Cefalea de 3 días"),
            "con los nombres de campo del portal: id/titulo/kind/texto — no key/label/content");

        // El verbatim se espeja como turnos, que es como el detalle sabe pintarlo.
        Debe(r.TryGetProperty("transcript", out var tr) && tr.ValueKind == JsonValueKind.Array
             && tr.GetArrayLength() == 1
             && (tr[0].TryGetProperty("texto", out var tt) ? tt.GetString() ?? "" : "")
                .Contains("cefalea"),
            "y la transcripción verbatim va como un turno, tal cual se dijo");

        // Sin transcripción no se fabrica una: vacío es vacío (aprendizaje nº9).
        string sinTexto = (string)fabricar.Invoke(null, new object?[]
        {
            "enc-2", nota, "   ", "P", "medicina_general", DateTimeOffset.UtcNow,
        })!;
        using var fila2 = JsonDocument.Parse(sinTexto);
        Debe(fila2.RootElement.GetProperty("transcript").GetArrayLength() == 0,
            "sin nada dicho, la transcripción va VACÍA: no se inventa un turno en blanco");
    }

    /// <remarks>
    /// El médico no quiere elegir plantilla: quiere hablar. El backend EXIGE `template_id` al crear
    /// el encounter, así que «ninguna» no es una opción — lo que se puede es que la elija el sistema
    /// y no se pregunte nunca.
    ///
    /// La plantilla ABIERTA de verdad —que la IA diseñe las secciones de cada consulta— existe en
    /// el backend solo para biopsias (SYSTEM_DYNAMIC en BiopsyExtractionService) y pedirla para
    /// consultas es trabajo del repo Graph. Lo que esta promesa cubre es lo de este lado: que nadie
    /// tenga que elegir, y que si la plantilla abierta no existe todavía se CREE en vez de caer a
    /// una cualquiera del catálogo — caer a una cualquiera es elegir por el médico sin decírselo.
    /// </remarks>
    private static void NadieEligePlantilla()
    {
        var t = Capacidad("U.WindowsClient.Clinical.PlantillaAbierta");
        var elegir = t?.GetMethod("Elegir", BindingFlags.Public | BindingFlags.Static);
        var secciones = t?.GetMethod("Secciones", BindingFlags.Public | BindingFlags.Static);
        if (t == null || elegir == null || secciones == null)
        {
            Pendiente("Clinical.PlantillaAbierta", "94");
            return;
        }

        var tPlantilla = Capacidad("U.WindowsClient.Clinical.PlantillaClinica")!;
        object Plantilla(string id, string nombre) =>
            Activator.CreateInstance(tPlantilla, id, nombre, "medicina_general", false)!;

        var lista = (System.Collections.IList)Activator.CreateInstance(
            typeof(List<>).MakeGenericType(tPlantilla))!;

        // Catálogo sin la abierta: NO se cae a una cualquiera.
        lista.Add(Plantilla("inst-1", "Consulta inicial adulto"));
        lista.Add(Plantilla("inst-2", "Atención general de urgencias"));
        object? sinAbierta = elegir.Invoke(null, new object?[] { lista });
        Debe(sinAbierta == null,
            "con 204 plantillas institucionales y ninguna abierta, NO se elige una cualquiera: "
            + "elegir por el médico sin decírselo es peor que preguntarle");

        // Con la abierta dentro: se usa esa, sin preguntar.
        string nombreAbierta = (string)t.GetProperty("Nombre",
            BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        lista.Add(Plantilla("mia-9", nombreAbierta));
        object? conAbierta = elegir.Invoke(null, new object?[] { lista });
        Debe(conAbierta != null
             && (string)tPlantilla.GetProperty("Id")!.GetValue(conAbierta)! == "mia-9",
            "y cuando existe, se usa esa y no se pregunta nada");

        // Las secciones con las que nace: pocas, anchas, y con instrucción de organizar libre.
        var trozos = ((System.Collections.IEnumerable)secciones.Invoke(null, null)!)
            .Cast<object>().ToList();
        Debe(trozos.Count is >= 2 and <= 30,
            $"el contrato del backend exige entre 2 y 30 secciones; se piden {trozos.Count}");
        string todo = string.Join(" ", trozos.Select(x => x?.ToString() ?? ""));
        Debe(todo.Contains("organiza", StringComparison.OrdinalIgnoreCase)
             || todo.Contains("estructura", StringComparison.OrdinalIgnoreCase),
            "y la instrucción le pide al organizador que ESTRUCTURE lo que se dijo, que es lo más "
            + "cerca de una plantilla dinámica que se puede llegar sin tocar Graph");
    }

    /// <remarks>
    /// LA SEGUNDA MITAD ES LA QUE DUELE: que el motivo distinga «el dictado no conectó» de «el
    /// micrófono no entregó audio». Son dos averías con dos arreglos opuestos —una es la red, la
    /// otra es el aparato— y hasta hoy las dos salían como «comprueba el micrófono», que manda a
    /// desenchufar cables cuando lo que falla es el wifi.
    /// </remarks>
    private static void SinMicrofonoNoSeGraba()
    {
        var tConsulta = Capacidad("U.WindowsClient.Clinical.Consulta");
        var tClinica = Capacidad("U.WindowsClient.Clinical.ClinicaClient");
        var tSesion = Capacidad("U.WindowsClient.Cuenta.SesionMiracle");
        if (tConsulta == null || tClinica == null || tSesion == null)
        {
            Pendiente("Clinical.Consulta", "95");
            return;
        }

        // El micrófono se pide como una función que CONTESTA si abrió. Si siguiera siendo una que
        // no devuelve nada, esta promesa no se podría ni escribir: es la firma la que hace posible
        // enterarse.
        var abrir = tConsulta.GetConstructors()[0].GetParameters()
            .FirstOrDefault(p => p.Name != null && p.Name.Contains("icrofono"));
        if (abrir == null || abrir.ParameterType != typeof(Func<CancellationToken, Task<bool>>))
        {
            Pendiente("Consulta(abrirMicrofono que CONTESTE si abrió)", "95");
            return;
        }

        var backend = new BackendDeMentira(req =>
            req.RequestUri!.AbsolutePath.Contains("/auth/v1/token")
                ? (HttpStatusCode.OK, RespuestaDeLogin("medico-1", "unico", 3600))
                : (HttpStatusCode.Created, "{\"encounter_id\":\"enc-1\",\"status\":\"created\"}"));

        var sesion = Activator.CreateInstance(tSesion,
            "https://supabase.test", "publishable", backend,
            (Func<DateTimeOffset>)(() => DateTimeOffset.UtcNow))!;
        ((Task<bool>)tSesion.GetMethod("EntrarAsync")!
            .Invoke(sesion, new object?[] { "medico@miracle.app", "clave", CancellationToken.None })!)
            .GetAwaiter().GetResult();

        var clinica = Activator.CreateInstance(tClinica, "https://graph.test", sesion, backend)!;

        // El micrófono dice que NO abrió — exactamente lo que pasó con «Unable to connect».
        var consulta = Activator.CreateInstance(tConsulta, sesion, clinica,
            (Func<CancellationToken, Task<bool>>)(_ => Task.FromResult(false)),
            (Func<Task<string>>)(() => Task.FromResult("")),
            null)!;

        bool arranco = ((Task<bool>)tConsulta.GetMethod("EmpezarAsync")!
            .Invoke(consulta, new object?[] { "plantilla-x", CancellationToken.None })!)
            .GetAwaiter().GetResult();

        string estado = tConsulta.GetProperty("Estado")!.GetValue(consulta)!.ToString()!;
        string motivo = (string)tConsulta.GetProperty("Motivo")!.GetValue(consulta)!;

        Debe(!arranco, "si el micrófono no abrió, empezar contesta que NO");
        Debe(estado != "Grabando",
            $"y sobre todo la consulta NO se declara grabando; dice «{estado}». Decir que graba sin "
            + "grabar es dejar que alguien le hable diecisiete segundos a nada");
        Debe(motivo.Length > 0 && !motivo.Contains("micrófono", StringComparison.OrdinalIgnoreCase),
            $"el motivo NO manda a revisar el micrófono cuando lo que falló fue el dictado: "
            + $"son dos averías con arreglos opuestos. Dice: «{motivo}»");

        // Y con el micrófono abriendo bien, sí se graba: una promesa que solo sabe decir que no
        // pasaría igual con un EmpezarAsync que devolviera false siempre.
        var buena = Activator.CreateInstance(tConsulta, sesion, clinica,
            (Func<CancellationToken, Task<bool>>)(_ => Task.FromResult(true)),
            (Func<Task<string>>)(() => Task.FromResult("algo dicho")),
            null)!;
        Debe(((Task<bool>)tConsulta.GetMethod("EmpezarAsync")!
                 .Invoke(buena, new object?[] { "plantilla-x", CancellationToken.None })!)
                 .GetAwaiter().GetResult()
             && tConsulta.GetProperty("Estado")!.GetValue(buena)!.ToString() == "Grabando",
            "con el micrófono abierto de verdad, la consulta sí graba");
    }

    /// <remarks>
    /// La espera se INYECTA para que esta promesa no duerma nueve segundos. Una prueba lenta se
    /// acaba saltando, y un juez que no se corre no juzga nada.
    /// </remarks>
    private static void ElDictadoReintenta()
    {
        var t = Capacidad("U.WindowsClient.Clinical.Transcripcion.PoliticaDeReintento");
        var hasta = t?.GetMethod("HastaQueSalgaAsync");
        if (t == null || hasta == null) { Pendiente("Transcripcion.PoliticaDeReintento", "96"); return; }

        var dormido = new List<int>();
        object Politica() => Activator.CreateInstance(t,
            (Func<int, CancellationToken, Task>)((ms, _) => { dormido.Add(ms); return Task.CompletedTask; }))!;

        bool Correr(object p, Func<CancellationToken, Task<bool>> intento) =>
            ((Task<bool>)hasta.Invoke(p, new object?[] { intento, CancellationToken.None })!)
            .GetAwaiter().GetResult();

        // 1. A la primera: ni se reintenta ni se espera. Un tropiezo que no ocurrió no cuesta nada.
        dormido.Clear();
        var alaPrimera = Politica();
        Debe(Correr(alaPrimera, _ => Task.FromResult(true)), "lo que sale a la primera, sale");
        Debe((int)t.GetProperty("Intentos")!.GetValue(alaPrimera)! == 1 && dormido.Count == 0,
            "un intento y cero esperas: nadie paga por un fallo que no hubo");

        // 2. Falla dos veces y a la tercera abre — que es EXACTAMENTE lo que se midió.
        dormido.Clear();
        int veces = 0;
        var terca = Politica();
        Debe(Correr(terca, _ => Task.FromResult(++veces >= 3)),
            "dos tropiezos seguidos no tumban la grabación: al tercer intento entra");
        Debe((int)t.GetProperty("Intentos")!.GetValue(terca)! == 3,
            "y se intentó tres veces, ni una más");
        Debe(dormido.Count == 2 && dormido[0] < dormido[1],
            $"esperando cada vez un poco más entre intentos: [{string.Join(", ", dormido)}] ms. "
            + "Reintentar de golpe contra un servicio caído es martillearlo");

        // 3. Si de verdad no hay red, se rinde — pero con un tope, no eternamente, y sabiendo
        //    cuántas veces lo intentó. Rendirse en silencio es lo que había antes.
        dormido.Clear();
        var imposible = Politica();
        Debe(!Correr(imposible, _ => Task.FromResult(false)), "sin red de verdad, se rinde");
        int gastados = (int)t.GetProperty("Intentos")!.GetValue(imposible)!;
        Debe(gastados is >= 3 and <= 6,
            $"con un presupuesto acotado ({gastados} intentos): reintentar sin tope deja al médico "
            + "mirando un botón que no contesta");
        Debe(gastados > 1 && dormido.Count == gastados - 1,
            "y se esperó entre todos ellos menos antes del primero");
    }

    /// <remarks>
    /// El mínimo de contraseña es el MISMO que el del portal (8), y se comprueba antes de salir a
    /// la red: no por ahorrar una llamada, sino porque el error de Supabase llega en inglés y
    /// genérico, y «la contraseña necesita al menos 8 caracteres» se arregla solo.
    /// </remarks>
    private static void CrearCuentaNoEsEntrar()
    {
        var t = Capacidad("U.WindowsClient.Cuenta.SesionMiracle");
        var alta = t?.GetMethod("CrearCuentaAsync");
        if (t == null || alta == null) { Pendiente("SesionMiracle.CrearCuentaAsync", "97"); return; }

        object Sesion(Func<HttpRequestMessage, (HttpStatusCode, string)> responde, out BackendDeMentira b)
        {
            b = new BackendDeMentira(responde);
            return Activator.CreateInstance(t, "https://supabase.test", "publishable", b,
                (Func<DateTimeOffset>)(() => DateTimeOffset.UtcNow))!;
        }
        string Correr(object s, string clave) =>
            ((Task<object>)typeof(Contrato).GetMethod(nameof(ComoTexto),
                BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(alta!.ReturnType.GetGenericArguments()[0])
                .Invoke(null, new object?[] { alta.Invoke(s, new object?[]
                    { "Dra. Prueba", "nueva@miracle.app", clave, CancellationToken.None }) })!)
            .GetAwaiter().GetResult().ToString()!;

        // 1. Supabase contesta 200 SIN sesión: la cuenta se creó y falta confirmar el correo.
        var sinSesion = Sesion(_ => (HttpStatusCode.OK,
            "{\"id\":\"u-1\",\"email\":\"nueva@miracle.app\",\"confirmation_sent_at\":\"2026-09-01T12:00:00Z\"}"),
            out _);
        string r1 = Correr(sinSesion, "contrasena-larga");
        Debe(r1.Contains("Confirm", StringComparison.OrdinalIgnoreCase),
            $"sin sesión en la respuesta, el resultado dice que falta confirmar: «{r1}»");
        Debe((bool)t.GetProperty("HayMedico")!.GetValue(sinSesion)! == false,
            "y NO hay médico dentro: mandarle a grabar con una sesión que no existe haría que el "
            + "401 llegara después, sin relación aparente con el alta");
        Debe(!File.Exists((string)t.GetProperty("RutaDeLaCredencial",
                BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!),
            "ni se guarda credencial de una sesión que no llegó");

        // 2. Con sesión en la respuesta (confirmación desactivada): entra directo.
        var conSesion = Sesion(_ => (HttpStatusCode.OK,
            RespuestaDeLogin("7b8a4c8e-1f2d-4c3b-9a10-0d1e2f3a4b5c", "alta", 3600)), out _);
        string r2 = Correr(conSesion, "contrasena-larga");
        Debe(r2.Contains("Entro", StringComparison.OrdinalIgnoreCase),
            $"con sesión, el alta entra directo: «{r2}»");
        Debe((bool)t.GetProperty("HayMedico")!.GetValue(conSesion)! == true,
            "y ahí sí hay médico dentro");

        // 3. Una contraseña corta se para AQUÍ, sin salir a la red.
        var corta = Sesion(_ => (HttpStatusCode.OK, "{}"), out var backendCorta);
        string r3 = Correr(corta, "1234");
        Debe(r3.Contains("Fallo", StringComparison.OrdinalIgnoreCase)
             && backendCorta.Peticiones.Count == 0,
            "una contraseña de 4 caracteres no llega a viajar: se dice antes y no se gasta una "
            + "llamada en que Supabase conteste en inglés");
        Debe(((string)t.GetProperty("UltimoFallo")!.GetValue(corta)!).Contains("8"),
            "y el motivo dice el mínimo concreto, no «contraseña inválida»");
    }

    /// <summary>Adaptador para await-ear un Task&lt;T&gt; que solo se conoce por reflexión.</summary>
    private static async Task<object> ComoTexto<T>(object tarea) => (await (Task<T>)tarea)!;

    /// <remarks>
    /// LA PREGUNTA SE HACE UNA VEZ Y LA CONTESTA UN SOLO SITIO. Se juzga el DECISOR —quién manda
    /// cuando las dos identidades no coinciden— y no la ventana: la ventana es nivel 4, pero la
    /// regla de precedencia es lo que se rompió, y eso sí se puede escribir.
    ///
    /// Las cuatro combinaciones importan. La tercera es la que estaba mal el 2026-09-01 (había
    /// médico y se preguntaba igual) y la cuarta es la que impide «arreglarlo» silenciando el popup
    /// siempre: en un equipo sin médico y sin correo, preguntar sigue siendo lo correcto.
    /// </remarks>
    private static void LaIdentidadSePideUnaVez()
    {
        var t = Capacidad("U.WindowsClient.Cuenta.Identidad");
        var hay = t?.GetMethod("HayQuePreguntar", BindingFlags.Public | BindingFlags.Static);
        if (t == null || hay == null) { Pendiente("Cuenta.Identidad.HayQuePreguntar", "98"); return; }

        bool Preguntar(bool medicoDentro, string correoDeMaquina) =>
            (bool)hay.Invoke(null, new object?[] { medicoDentro, correoDeMaquina })!;

        Debe(Preguntar(false, "") == true,
            "equipo nuevo, sin médico y sin correo: preguntar es lo correcto");
        Debe(Preguntar(false, "alguien@hospital.co") == false,
            "con el correo de máquina ya puesto no se vuelve a preguntar (lo de siempre)");
        Debe(Preguntar(true, "") == false,
            "CON MÉDICO DENTRO NO SE PREGUNTA, aunque no haya correo de máquina: la sesión de "
            + "Supabase ya sabe quién es, y volver a pedírselo es no haberla mirado");
        Debe(Preguntar(true, "otro@hospital.co") == false,
            "y con las dos, tampoco: manda la sesión");

        // Y de dónde sale el correo cuando hay médico: del token, no de lo que teclearon una vez.
        var deQuien = t.GetMethod("CorreoQueMandaEnLaMaquina", BindingFlags.Public | BindingFlags.Static);
        if (deQuien == null) { Pendiente("Identidad.CorreoQueMandaEnLaMaquina", "98"); return; }
        Debe((string)deQuien.Invoke(null, new object?[] { "medico@miracle.app", "viejo@teclado.co" })!
                == "medico@miracle.app",
            "el correo que manda es el de la SESIÓN, no el que se tecleó una vez en config.json");
        Debe((string)deQuien.Invoke(null, new object?[] { "", "viejo@teclado.co" })!
                == "viejo@teclado.co",
            "y sin sesión se conserva el de máquina: los workflows y la telemetría que ya lo usaban "
            + "no se quedan sin identidad de golpe");
    }

    /// <remarks>
    /// Se juzga el GUARDIA, no el menú: el menú es nivel 4. Lo que sí se puede escribir es que
    /// <c>Consulta</c> sepa distinguir «estoy grabando» de todo lo demás, porque esa es la frase que
    /// hoy sería falsa (nada impedía cerrar sesión a media consulta) y mañana tiene que ser
    /// verdadera.
    /// </remarks>
    private static void NoSeCambiaDeCuentaGrabando()
    {
        var tConsulta = Capacidad("U.WindowsClient.Clinical.Consulta");
        var tClinica = Capacidad("U.WindowsClient.Clinical.ClinicaClient");
        var tSesion = Capacidad("U.WindowsClient.Cuenta.SesionMiracle");
        var puede = tConsulta?.GetProperty("PuedeCambiarDeUsuario");
        if (tConsulta == null || tClinica == null || tSesion == null || puede == null)
        {
            Pendiente("Clinical.Consulta.PuedeCambiarDeUsuario", "99");
            return;
        }

        var backend = new BackendDeMentira(req =>
            req.RequestUri!.AbsolutePath.Contains("/auth/v1/token")
                ? (HttpStatusCode.OK, RespuestaDeLogin("medico-1", "unico", 3600))
                : (HttpStatusCode.Created, "{\"encounter_id\":\"enc-1\",\"status\":\"created\"}"));
        var sesion = Activator.CreateInstance(tSesion,
            "https://supabase.test", "publishable", backend,
            (Func<DateTimeOffset>)(() => DateTimeOffset.UtcNow))!;
        ((Task<bool>)tSesion.GetMethod("EntrarAsync")!
            .Invoke(sesion, new object?[] { "medico@miracle.app", "clave", CancellationToken.None })!)
            .GetAwaiter().GetResult();
        var clinica = Activator.CreateInstance(tClinica, "https://graph.test", sesion, backend)!;

        var consulta = Activator.CreateInstance(tConsulta, sesion, clinica,
            (Func<CancellationToken, Task<bool>>)(_ => Task.FromResult(true)),
            (Func<Task<string>>)(() => Task.FromResult("algo dicho")),
            null)!;

        Debe((bool)puede.GetValue(consulta)!,
            "sin haber empezado nada, cambiar de cuenta está permitido");

        ((Task<bool>)tConsulta.GetMethod("EmpezarAsync")!
            .Invoke(consulta, new object?[] { "plantilla-x", CancellationToken.None })!)
            .GetAwaiter().GetResult();

        Debe(!(bool)puede.GetValue(consulta)!,
            "GRABANDO, se bloquea: cerrar sesión con el micrófono abierto dejaría un dictado "
            + "huérfano que nadie para ni guarda");

        ((Task)tConsulta.GetMethod("TerminarAsync")!
            .Invoke(consulta, new object?[] { CancellationToken.None })!)
            .GetAwaiter().GetResult();

        Debe((bool)puede.GetValue(consulta)!,
            "con la nota lista, se puede cambiar de cuenta otra vez: el bloqueo es SOLO mientras "
            + "se graba, no para siempre después de la primera consulta");
    }

    /// <remarks>
    /// SE JUZGA LA FUNCIÓN QUE ARMA EL CUERPO DE LA RPC, pura y sin red — como
    /// <see cref="EspejoDeConsulta.Fila"/>. Ahí es donde vive el riesgo real: no en si la llamada
    /// sale, sino en QUÉ lleva dentro. Un dato de otro médico —especialidad, documento, ciudad de
    /// práctica— que se pierde por guardar el nombre es un daño silencioso al mismo perfil que lee
    /// el portal, y nadie lo notaría hasta que ese médico volviera a mirar su configuración.
    /// </remarks>
    private static void GuardarNombreNoBorraElResto()
    {
        var tPerfil = Capacidad("U.WindowsClient.Cuenta.PerfilProfesional");
        var tRpc = Capacidad("U.WindowsClient.Cuenta.PerfilRpc");
        var metodo = tRpc?.GetMethod("CuerpoDeGuardarNombre", BindingFlags.Public | BindingFlags.Static);
        if (tPerfil == null || tRpc == null || metodo == null)
        {
            Pendiente("Cuenta.PerfilRpc.CuerpoDeGuardarNombre", "100");
            return;
        }

        object Perfil(string doc, string registro, string espCodigo, string espNombre, string pais, string ciudad) =>
            Activator.CreateInstance(tPerfil, doc, registro, espCodigo, espNombre, pais, ciudad)!;

        // Un médico con el perfil profesional lleno: los seis campos tienen que volver EXACTOS.
        var lleno = Perfil("CC 1035 421 987", "RM-4471", "cardiologia", "Cardiología", "Colombia", "Medellín");
        using var doc1 = JsonDocument.Parse((string)metodo.Invoke(null, new object?[] { "Nueva Dra.", lleno })!);
        var r1 = doc1.RootElement;

        string Str(JsonElement e, string campo) => e.GetProperty(campo).GetString() ?? "";

        Debe(Str(r1, "p_full_name") == "Nueva Dra.", "el nombre nuevo viaja tal cual se pidió");
        Debe(Str(r1, "p_identification_number") == "CC 1035 421 987",
            "el documento NO se borra: viaja idéntico al que ya tenía");
        Debe(Str(r1, "p_professional_registration") == "RM-4471",
            "el registro profesional tampoco: cambiar el nombre no es lo mismo que cambiar esto");
        Debe(Str(r1, "p_specialty_code") == "cardiologia" && Str(r1, "p_specialty_name") == "Cardiología",
            "la especialidad se conserva — perderla aquí cambiaría qué secciones ve ese médico");
        Debe(Str(r1, "p_practice_country") == "Colombia" && Str(r1, "p_practice_city") == "Medellín",
            "y el país/ciudad de práctica, igual");

        // Una cuenta nueva sin nada más que el nombre: no se INVENTA un valor donde no había.
        var vacio = Perfil("", "", "", "", "", "");
        using var doc2 = JsonDocument.Parse((string)metodo.Invoke(null, new object?[] { "Alguien", vacio })!);
        Debe(Str(doc2.RootElement, "p_specialty_code") == "" && Str(doc2.RootElement, "p_practice_city") == "",
            "sin dato previo, los demás campos viajan vacíos — no se fabrica una especialidad de la nada");
    }

    // ── El aura de aprendizaje (spec 006) ────────────────────────────────────

    private static void ElAuraDiceQueUAprende()
    {
        // Se juzga LA REGLA, separada del dibujo, por el mismo camino que la 104 juzga
        // SesionDeDemo: el overlay real construye sus pinceles muestreando esta misma función,
        // así lo juzgado y lo pintado no pueden discrepar (aprendizaje nº16).
        var t = Capacidad("U.WindowsClient.Ui.ReglaDelAura");
        var decidir = t?.GetMethod("Decidir");
        var opacidad = t?.GetMethod("Opacidad");
        Debe(t != null && decidir != null && opacidad != null,
            "todavía no existe «ReglaDelAura» (fase 1 de la spec 006). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || decidir == null || opacidad == null) return;

        string Fase(bool ensenando, bool grabando)
            => decidir.Invoke(null, new object[] { ensenando, grabando })!.ToString()!;

        Debe(Fase(false, false) == "Apagada",
            "sin enseñar no hay aura: un borde encendido en reposo no significaría nada");
        Debe(Fase(true, false) == "Preparando",
            "pulsado Enseñar y aún sin grabar (la cuenta atrás), el aura avisa TENUE: encendida "
            + "del todo diría que ya graba, y todavía no");
        Debe(Fase(true, true) == "Aprendiendo",
            "grabando, el aura está encendida: es el momento que existe para decir");
        Debe(Fase(false, true) == "Apagada",
            "al pulsar terminar el aura se apaga AUNQUE el cierre siga subiendo el video: "
            + "encendida diría que sigue aprendiendo lo que hagas ahora, y no");

        double O(double distanciaAlBorde)
            => (double)opacidad.Invoke(null, new object[] { distanciaAlBorde, 96.0 })!;

        Debe(O(0) > 0.5, "en el borde mismo el aura se ve");
        Debe(O(96) == 0 && O(500) == 0,
            "a partir del grosor no queda NADA: el centro, donde está el trabajo, se deja limpio");
        Debe(O(0) > O(32) && O(32) > O(64) && O(64) > O(95) && O(95) > 0,
            "y entre medias baja sin escalones: es un degradado, no una franja");
    }

    // ── El workflow a la mano (spec 007) ─────────────────────────────────────

    /// <summary>Un workflow tal como lo lista Graph, con el <c>createdAt</c> como entero Neo4j
    /// <c>{low, high}</c> —que es como llega de verdad— para la hora local dada.</summary>
    private static JsonElement WorkflowDeGraph(string id, string description, string origin, string titulo,
        DateTimeOffset creado, int pasos, bool enteroNeo4j = true)
    {
        long ms = creado.ToUnixTimeMilliseconds();
        object createdAt = enteroNeo4j
            ? new { low = (int)(ms & 0xFFFFFFFF), high = (int)(ms >> 32) }
            : (object)ms;
        string json = JsonSerializer.Serialize(new
        {
            id, description, summary = "", sourceOrigin = origin, sourceTitle = titulo,
            createdAt, totalSteps = pasos,
        });
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static void ElWorkflowSePresentaPorLoQueSeSabe()
    {
        var t = Capacidad("U.WindowsClient.Workflows.NombreDeWorkflow");
        var m = t?.GetMethod("Derivar");
        Debe(t != null && m != null, "todavía no existe «NombreDeWorkflow.Derivar» (fase 1 de la spec 007). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || m == null) return;

        string Nombre(JsonElement e) => (string)m.Invoke(null, new object[] { e })!;
        var hora = new DateTimeOffset(2026, 9, 2, 13, 42, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 9, 2)));

        string relleno = Nombre(WorkflowDeGraph("wf_1", "Workflow sin descripción", "uia://claude.exe", "Claude", hora, 6));
        Debe(!relleno.Contains("sin descripción", StringComparison.OrdinalIgnoreCase),
            "«Workflow sin descripción» no es un nombre: tres de cuatro se llamaban así");
        Debe(relleno.Contains("Claude") && relleno.Contains("13:42") && relleno.Contains("6 pasos") && relleno.Contains("2 sep"),
            $"con el relleno, el nombre se compone de lo que SÍ se sabe —app, cuándo, cuántos pasos—; salió «{relleno}»");

        string encabezado = Nombre(WorkflowDeGraph("wf_2", "User workflow summary:", "uia://claude.exe", "Claude", hora, 3));
        Debe(!encabezado.Contains("summary", StringComparison.OrdinalIgnoreCase) && encabezado.Contains("3 pasos"),
            $"un encabezado del LLM («User workflow summary:») tampoco es un nombre; salió «{encabezado}»");

        string vacio = Nombre(WorkflowDeGraph("wf_3", "", "sapgui://QAS/NWP1", "SAP Easy Access", hora, 9, enteroNeo4j: false));
        Debe(vacio.Contains("SAP") && vacio.Contains("13:42"),
            $"vacío tampoco, y el createdAt como número llano se lee igual que el entero Neo4j; salió «{vacio}»");

        string real = Nombre(WorkflowDeGraph("wf_4", "Radicar factura en SAP", "sapgui://QAS/NWP1", "SAP", hora, 9));
        Debe(real == "Radicar factura en SAP", "una descripción de verdad se respeta tal cual");
    }

    private static void ElNombrePuestoMandaYSobrevive()
    {
        var t = Capacidad("U.WindowsClient.Workflows.NombresDeWorkflows");
        Debe(t != null, "todavía no existe «NombresDeWorkflows» (fase 2 de la spec 007). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null) return;

        var poner = t.GetMethod("Poner")!;
        var de = t.GetMethod("De")!;

        var primero = Activator.CreateInstance(t)!;
        poner.Invoke(primero, new object[] { "wf_9", "Radicar factura" });
        Debe((string?)de.Invoke(primero, new object[] { "wf_9" }) == "Radicar factura", "puesto, se lee");

        var otroArranque = Activator.CreateInstance(t)!;   // otra instancia = otro arranque de Ü
        Debe((string?)de.Invoke(otroArranque, new object[] { "wf_9" }) == "Radicar factura",
            "y sobrevive al reinicio: se guardó en disco por id");
        Debe(de.Invoke(otroArranque, new object[] { "wf_otro" }) == null,
            "un id sin nombre puesto devuelve nada, para que mande el derivado");

        poner.Invoke(otroArranque, new object[] { "wf_9", "   " });
        Debe(de.Invoke(otroArranque, new object[] { "wf_9" }) == null,
            "poner vacío QUITA el nombre: vacío no es un nombre (patrón nº9), y se vuelve al derivado");
        Debe(de.Invoke(Activator.CreateInstance(t)!, new object[] { "wf_9" }) == null,
            "y el borrado también sobrevive al reinicio");
    }

    private static void ElRecienEnsenadoQuedaElegido()
    {
        var tSel = Capacidad("U.WindowsClient.Workflows.SelectorDeWorkflows");
        var tRes = Capacidad("U.WindowsClient.Workflows.WorkflowSummary");
        var ordenar = tSel?.GetMethod("Ordenar");
        var indice = tSel?.GetMethod("IndiceDe");
        var fromJson = tRes?.GetMethod("FromJson", new[] { typeof(JsonElement) });
        Debe(ordenar != null && indice != null && fromJson != null,
            "todavía no existe «SelectorDeWorkflows» (fase 3 de la spec 007). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (ordenar == null || indice == null || fromJson == null || tRes == null) return;

        var t0 = new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.Zero);
        var lista = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(tRes))!;
        // Como llega de Graph: del más viejo al más nuevo.
        lista.Add(fromJson.Invoke(null, new object[] { WorkflowDeGraph("wf_viejo", "", "uia://a.exe", "A", t0, 1) })!);
        lista.Add(fromJson.Invoke(null, new object[] { WorkflowDeGraph("wf_medio", "", "uia://a.exe", "A", t0.AddMinutes(5), 1) })!);
        lista.Add(fromJson.Invoke(null, new object[] { WorkflowDeGraph("wf_nuevo", "", "uia://a.exe", "A", t0.AddMinutes(9), 1) })!);

        var ordenada = (System.Collections.IList)ordenar.Invoke(null, new object[] { lista })!;
        string IdEn(int i) => (string)tRes.GetProperty("Id")!.GetValue(ordenada[i])!;
        Debe(ordenada.Count == 3 && IdEn(0) == "wf_nuevo" && IdEn(2) == "wf_viejo",
            "la lista va del más nuevo al más viejo: lo último que enseñaste es lo primero que ves");

        Debe((int)indice.Invoke(null, new object[] { ordenada, "wf_medio" })! == 1,
            "el recién enseñado se elige por su id, esté donde esté");
        Debe((int)indice.Invoke(null, new object[] { ordenada, "wf_desconocido" })! == 0,
            "y si no se sabe cuál es (el finish no volvió), se elige el más nuevo, no el que estaba");
    }

    private static void ElPlayNoVuelveAPedirElPlan()
    {
        var planSource = typeof(WorkflowPlayer).GetProperty("PlanSource");
        Debe(planSource != null, "todavía no existe «WorkflowPlayer.PlanSource» (fase 4 de la spec 007). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (planSource == null) return;

        // Un plan cuyo único paso vive en una superficie que este cliente no maneja: la corrida
        // para ANTES de tocar la pantalla, que es lo que hace falta para juzgarla sin pantalla.
        const string planJson = "{\"execution_plan\":{\"workflowId\":\"wf_p\",\"sourceOrigin\":\"marte://x\","
            + "\"steps\":[{\"stepOrder\":1,\"actionType\":\"click\",\"selector\":\"marte:boton\",\"label\":\"b\"}]}}";
        var backend = new BackendDeMentira(_ => (HttpStatusCode.OK, planJson));
        var cfg = new GraphConfig { BaseUrl = "https://graph.test", ApiKey = "k" };
        var graph = new GraphClient(cfg, new HttpClient(backend));
        int Planes() => backend.Peticiones.Count(p => p.Contains("/plan"));

        var conPlan = new WorkflowPlayer(graph, cfg);
        var aLaMano = JsonSerializer.Deserialize<PlanResponse>(planJson)!.ExecutionPlan!;
        planSource.SetValue(conPlan, (Func<string, CancellationToken, Task<ExecutionPlan>>)((_, _) => Task.FromResult(aLaMano)));
        var r1 = conPlan.RunAsync("wf_p", null, true, CancellationToken.None).GetAwaiter().GetResult();
        Debe(Planes() == 0, $"con el plan a la mano, darle play no le pide NADA a Graph ({Planes()} peticiones a /plan)");
        Debe(!r1.Ok && (r1.Error ?? "").Contains("marte"),
            "y la corrida usó ESE plan: se detuvo en la superficie que le dimos, no en otra");

        var sinPlan = new WorkflowPlayer(graph, cfg);
        sinPlan.RunAsync("wf_p", null, true, CancellationToken.None).GetAwaiter().GetResult();
        Debe(Planes() == 1, $"sin plan a la mano se pide UNA vez, como siempre ({Planes()} peticiones)");
    }

    // ── Un clic habla, y el panel vive a la derecha (spec 010) ───────────────

    /// <summary>
    /// Se juzga LA REGLA, separada del gesto, por el mismo camino que la 104 y el aura: quien de
    /// verdad temporiza el toque —<c>FaceGestures</c>— pregunta a esta misma función, así lo juzgado
    /// y lo que corre no pueden discrepar (aprendizaje nº16).
    /// </summary>
    private static void ElToqueSimpleNoEsperaANadie()
    {
        var t = Capacidad("U.WindowsClient.Ui.ReglaDelToque");
        var espera = t?.GetMethod("EsperaMs");
        Debe(t != null && espera != null,
            "todavía no existe «ReglaDelToque.EsperaMs» (fase 1 de la spec 010). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || espera == null) return;

        int Ms(bool hayDobleToque) => (int)espera.Invoke(null, new object[] { hayDobleToque })!;

        Debe(Ms(false) == 0,
            $"sin doble toque cableado no hay nada que distinguir, así que esperar es retardo puro: "
            + $"un botón de encender que tarda {Ms(false)} ms se siente roto");
        Debe(Ms(true) >= 200,
            $"y donde el doble toque SÍ exista, la ventana para distinguirlo sigue siendo usable "
            + $"(salió {Ms(true)} ms; por debajo de 200 el doble clic deja de poderse hacer)");
        Debe(Ms(true) > Ms(false),
            "esperar tiene que costar algo: si las dos respuestas fueran iguales, la regla no estaría "
            + "decidiendo nada y daría lo mismo llamarla que no");

        // Y QUE NO SE HAYA QUEDADO UNA COPIA. La constante vivía dentro de FaceGestures; si sigue
        // ahí, el gesto puede estar usando la suya mientras esta regla dice otra cosa — que es
        // exactamente la forma de fallo del aprendizaje nº16: dos caminos para el mismo hecho.
        var gestos = Capacidad("U.WindowsClient.Ui.FaceGestures");
        var copia = gestos?.GetField("TapWindowMs",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Debe(copia == null,
            "«FaceGestures» conserva su propia constante TapWindowMs: la espera se decide en DOS "
            + "sitios y acabarán discrepando");
    }

    private static void ElMuelleNoSeCierraSobreLoQueEstasHaciendo()
    {
        var t = Capacidad("U.WindowsClient.Ui.ReglaDelMuelle");
        var m = t?.GetMethod("Desplegado");
        Debe(t != null && m != null,
            "todavía no existe «ReglaDelMuelle.Desplegado» (fase 2 de la spec 010). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || m == null) return;

        bool Abierto(bool cursorEncima, bool conversacion, bool tecladoDentro)
            => (bool)m.Invoke(null, new object[] { cursorEncima, conversacion, tecladoDentro })!;

        Debe(Abierto(true, false, false),
            "el cursor encima lo despliega: ese ES el gesto, no hay otro");
        Debe(!Abierto(false, false, false),
            "y al irse el cursor se pliega, o dejaría de ser una pestaña y sería una barra permanente "
            + "encima del trabajo de alguien");
        Debe(Abierto(false, true, false),
            "pero con una conversación en marcha NO se pliega: lo que está pasando ahora mismo no "
            + "puede depender de dónde tengas el ratón");
        Debe(Abierto(false, false, true),
            "ni mientras escribes dentro: al llevar la mano al teclado el cursor sale del muelle, y "
            + "plegarse ahí se comería el texto a medias — que es el fallo que esta promesa existe "
            + "para impedir");
    }

    private static void SoltarlaEnElMuelleLaGuarda()
    {
        var t = Capacidad("U.WindowsClient.Ui.ReglaDelMuelle");
        var m = t?.GetMethod("Guarda");
        var margen = t?.GetField("MargenDeAgarre");
        Debe(t != null && m != null && margen != null,
            "todavía no existe «ReglaDelMuelle.Guarda» (fase 3 de la spec 010). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || m == null || margen == null) return;

        // Un muelle desplegado, angosto y alto, pegado al borde derecho de una pantalla de 1920.
        var muelle = new System.Windows.Rect(1820, 300, 96, 420);
        bool Guarda(double x, double y) => (bool)m.Invoke(null, new object[] { muelle, new System.Windows.Point(x, y) })!;
        double tol = (double)margen.GetValue(null)!;

        Debe(Guarda(1860, 500), "soltarla dentro del muelle la guarda");
        Debe(tol >= 12,
            $"el blanco del gesto no puede ser tan pequeño como el dibujo: con {tol} px de margen, "
            + "acertarle a un muelle angosto es puntería, no una interfaz");
        Debe(Guarda(1820 - tol / 2, 500),
            "y soltarla justo al lado también: el margen de agarre existe para eso");
        Debe(!Guarda(1820 - tol - 40, 500),
            "pero soltarla LEJOS no la guarda, o el muelle se tragaría la carita cada vez que alguien "
            + "la lanza contra el borde derecho — que es adonde se lanza sola");
        Debe(!Guarda(200, 500),
            "y desde luego no al otro lado de la pantalla");
        Debe(!Guarda(1860, 900),
            "el alto cuenta igual que el ancho: por debajo del muelle no hay muelle");
    }

    private static void SacadaVuelveDondeLaSueltasYSeVe()
    {
        var t = Capacidad("U.WindowsClient.Ui.ReglaDelMuelle");
        var m = t?.GetMethod("SitioAlSacar");
        Debe(t != null && m != null,
            "todavía no existe «ReglaDelMuelle.SitioAlSacar» (fase 3 de la spec 010). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || m == null) return;

        var area = new System.Windows.Rect(0, 0, 1920, 1040);        // pantalla menos la barra de tareas
        var carita = new System.Windows.Size(128, 128);
        System.Windows.Point Sitio(double x, double y)
            => (System.Windows.Point)m.Invoke(null, new object[] { new System.Windows.Point(x, y), carita, area })!;

        var suelta = Sitio(640, 400);
        Debe(suelta.X == 640 && suelta.Y == 400,
            $"sacada, vuelve al punto donde la soltaste y no a una esquina por defecto; salió {suelta}");

        var abajoDerecha = Sitio(1900, 1030);
        Debe(abajoDerecha.X <= area.Right - carita.Width && abajoDerecha.Y <= area.Bottom - carita.Height,
            $"soltada contra la esquina, entra ENTERA en la pantalla: un escondite del que se sale a "
            + $"un sitio que no ves no es un escondite, es una pérdida; salió {abajoDerecha}");
        Debe(abajoDerecha.X > 0 && abajoDerecha.Y > 0,
            "y se queda cerca de donde la soltaste, no se va al origen");

        var fuera = Sitio(-300, -300);
        Debe(fuera.X >= area.Left && fuera.Y >= area.Top,
            $"y soltada fuera por arriba tampoco desaparece; salió {fuera}");
    }

    // ── Los dispositivos del selector de micrófono (spec 010, segunda ronda) ─

    private static void ElDispositivoSePuedeNombrar()
    {
        var t = Capacidad("U.WindowsClient.Voice.NombresDeDispositivos");
        var de = t?.GetMethod("De");
        var poner = t?.GetMethod("Poner");
        var fabrica = t?.GetMethod("DeFabrica");
        Debe(t != null && de != null && poner != null && fabrica != null,
            "todavía no existe «NombresDeDispositivos» (fase 4 de la spec 010). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || de == null || poner == null || fabrica == null) return;

        Debe(!string.IsNullOrWhiteSpace((string?)fabrica.Invoke(null, new object[] { "collar" })),
            "un dispositivo sin nombre puesto tiene que poder presentarse igual: sin nombre de "
            + "fábrica, la lista enseñaría un hueco donde debería haber un aparato");

        var primero = Activator.CreateInstance(t)!;
        poner.Invoke(primero, new object?[] { "collar", "El collar de urgencias" });
        Debe((string?)de.Invoke(primero, new object[] { "collar" }) == "El collar de urgencias",
            "puesto, se lee");

        var otroArranque = Activator.CreateInstance(t)!;   // otra instancia = otro arranque de Ü
        Debe((string?)de.Invoke(otroArranque, new object[] { "collar" }) == "El collar de urgencias",
            "y sobrevive al reinicio: se guardó en disco por dispositivo. Un nombre que hay que "
            + "volver a escribir cada mañana no es un nombre");
        Debe(de.Invoke(otroArranque, new object[] { "telefono" }) == null,
            "un dispositivo sin nombre puesto devuelve nada, para que mande el de fábrica");

        poner.Invoke(otroArranque, new object?[] { "collar", "   " });
        Debe(de.Invoke(otroArranque, new object[] { "collar" }) == null,
            "poner vacío QUITA el nombre: vacío no es un nombre (patrón nº9), y se vuelve al de fábrica");
        Debe(de.Invoke(Activator.CreateInstance(t)!, new object[] { "collar" }) == null,
            "y el borrado también sobrevive al reinicio");
    }

    private static void OlvidarUnDispositivoNoDejaHuerfanos()
    {
        var tLista = Capacidad("U.WindowsClient.Voice.DispositivosEnlazados");
        var listar = tLista?.GetMethod("Listar");
        var tNombres = Capacidad("U.WindowsClient.Voice.NombresDeDispositivos");
        var olvidar = tNombres?.GetMethod("Olvidar");
        var de = tNombres?.GetMethod("De");
        var poner = tNombres?.GetMethod("Poner");
        Debe(listar != null && olvidar != null,
            "todavía no existe «DispositivosEnlazados.Listar» / «NombresDeDispositivos.Olvidar» "
            + "(fase 4 de la spec 010). La promesa está escrita y en rojo, que es donde tiene que estar");
        if (listar == null || olvidar == null || de == null || poner == null || tNombres == null) return;

        // LA FIRMA SE COMPRUEBA ANTES DE LLAMAR. Con la comprobación detrás, un parámetro de más
        // hacía estallar el Invoke y la promesa salía roja por una excepción en vez de por su
        // motivo: el juez decía «culpable» donde tenía que decir que ni pudo ejecutarla. Es el
        // aprendizaje nº17, cometido dentro del arnés que existe justo para eso (2026-09-06).
        Debe(listar.GetParameters().Length == 1,
            "la lista se hace SOLO con collares: el teléfono no se empareja con esta máquina —se le "
            + "da un código y manda por la red— así que no hay nada suyo que olvidar aquí");
        if (listar.GetParameters().Length != 1) return;

        string[] Lista(bool collar) => (string[])listar.Invoke(null, new object[] { collar })!;

        Debe(Lista(false).Length == 0,
            "sin collar enlazado la sección no inventa aparatos: uno que no existe, ofrecido para "
            + "olvidar, es peor que no ofrecer nada");
        Debe(Lista(true).Length == 1 && Lista(true)[0] == "collar",
            "con el collar enlazado, el collar");

        // Y OLVIDAR TIENE QUE LLEVARSE EL NOMBRE. Si el nombre sobreviviera al olvido, el siguiente
        // collar que alguien enlazara heredaría el nombre del anterior sin haberlo pedido — y ese
        // es justo el aparato que el nombre existía para distinguir.
        var nombres = Activator.CreateInstance(tNombres)!;
        poner.Invoke(nombres, new object?[] { "collar", "El de urgencias" });
        olvidar.Invoke(nombres, new object[] { "collar" });
        Debe(de.Invoke(nombres, new object[] { "collar" }) == null,
            "olvidado el dispositivo, su nombre se va con él");
        Debe(de.Invoke(Activator.CreateInstance(tNombres)!, new object[] { "collar" }) == null,
            "y no vuelve al reiniciar: el olvido también se guardó");
    }

    private static void QuererUnCollarNoEsTenerlo()
    {
        var t = Capacidad("U.WindowsClient.Voice.CollarPermanente");
        var intencion = t?.GetProperty("Permanente");
        var hecho = t?.GetProperty("Enlazado");
        Debe(t != null && hecho != null,
            "todavía no existe «CollarPermanente.Enlazado» (fase 5 de la spec 010). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || hecho == null || intencion == null) return;

        Debe(hecho.PropertyType == typeof(bool) && intencion.PropertyType == typeof(bool),
            "las dos son hechos de sí/no; si alguna dejara de serlo, quien las lee estaría "
            + "interpretando en vez de preguntando");
        Debe(hecho.Name != intencion.Name,
            "SON DOS PROPIEDADES, no una: «Permanente» dice «quiero que se conecte solo» y se pone "
            + "en cuanto alguien elige el collar en el menú; «Enlazado» dice «hay uno». Con una "
            + "sola, elegir bastaba para que apareciera un collar inexistente ofreciéndose a ser "
            + "olvidado (2026-09-06)");
        Debe(hecho.GetSetMethod() == null,
            "y el hecho no lo escribe quien quiera: lo enciende el servicio cuando el collar "
            + "contestó, que es el único momento en que consta");

        // Sin archivo de collar —un equipo recién estrenado— no consta ninguno. Cada promesa corre
        // en su propio U_DATA_DIR, así que esto se juzga en limpio.
        Debe((bool)hecho.GetValue(null)! == false,
            "en una máquina donde nunca se conectó un collar, no hay collar enlazado. Lo contrario "
            + "sería la lista ofreciendo olvidar un aparato que nadie ha visto nunca");
    }

    private static void LaSombraSeDibujaEntera()
    {
        var t = Capacidad("U.WindowsClient.Ui.Estudio");
        var holgura = t?.GetMethod("HolguraDe");
        var elevar = t?.GetMethod("Elevar");
        Debe(t != null && holgura != null,
            "todavía no existe «Estudio.HolguraDe» (fase 6 de la spec 010). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || holgura == null || elevar == null) return;

        System.Windows.Thickness Holgura(double desenfoque, double profundidad)
        {
            var s = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = desenfoque,
                ShadowDepth = profundidad,
                Direction = 270,
            };
            return (System.Windows.Thickness)holgura.Invoke(null, new object[] { s })!;
        }

        var h = Holgura(48, 12);   // la sombra de una ventana sobre el escritorio
        Debe(h.Left >= 24 && h.Right >= 24 && h.Top >= 24,
            $"el desenfoque se sale por los cuatro lados y hay que reservarle sitio; con 48 de "
            + $"desenfoque salió izquierda={h.Left}, derecha={h.Right}, arriba={h.Top}");
        Debe(h.Bottom >= h.Top + 12,
            $"y por ABAJO hace falta más, porque la sombra cae hacia abajo —la luz de este estudio "
            + $"viene de arriba—: con 12 de profundidad, abajo={h.Bottom} contra arriba={h.Top}");

        var chica = Holgura(14, 3);
        Debe(chica.Left < h.Left && chica.Bottom < h.Bottom,
            "una sombra pequeña reserva menos: si el hueco no dependiera de la sombra, o sobraría "
            + "aire en los botones o faltaría en las ventanas");
        Debe(Holgura(0, 0).Bottom == 0,
            "y sin sombra no se reserva nada: un hueco que no protege nada es un hueco que descuadra");

        // Y QUE ELEVAR LO APLIQUE DE VERDAD. Sin esto, la regla sería correcta y la interfaz seguiría
        // cortando sombras: es el aprendizaje nº16 —lo juzgado y lo pintado por caminos distintos—
        // y aquí se puede cerrar porque Elevar se puede llamar sin abrir una ventana.
        var tarjeta = new System.Windows.Controls.Border();
        var sombra = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 48, ShadowDepth = 12, Direction = 270,
        };
        var caja = (System.Windows.FrameworkElement)elevar.Invoke(null, new object[] { tarjeta, sombra })!;
        var esperada = Holgura(48, 12);
        Debe(caja.Margin.Left >= esperada.Left && caja.Margin.Bottom >= esperada.Bottom,
            $"«Elevar» tiene que RESERVAR ese hueco, no solo saber calcularlo; dejó "
            + $"{caja.Margin} donde hacían falta {esperada}");
    }

    private static void ElGloboNoSeAbreSolo()
    {
        var t = Capacidad("U.WindowsClient.Ui.ReglaDelGlobo");
        var abre = t?.GetMethod("SeAbre");
        var despliega = t?.GetMethod("DespliegaElMuelle");
        var motivos = Capacidad("U.WindowsClient.Ui.MotivoDelGlobo");
        Debe(t != null && abre != null && despliega != null && motivos != null,
            "todavía no existe «ReglaDelGlobo» (fase 7 de la spec 010). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (abre == null || despliega == null || motivos == null) return;

        object M(string nombre) => Enum.Parse(motivos, nombre);
        bool Abre(string m) => (bool)abre.Invoke(null, new[] { M(m) })!;
        bool Saca(string m) => (bool)despliega.Invoke(null, new[] { M(m) })!;

        Debe(Abre("LoPidioAlguien"), "si lo pide una persona, se abre: ese ES el gesto");
        Debe(Abre("HayQueContestar"),
            "y si Ü pregunta algo que hay que escribir, también: sin globo la pregunta no se puede "
            + "contestar, y quedaría esperando una respuesta que nadie puede dar");
        Debe(!Abre("SoloEsProgreso"),
            "pero NARRAR no abre nada. Es el fallo que el dueño vio dos veces: veinte sitios "
            + "distintos abrían el globo para contar lo que Ü iba haciendo, y contar no es conversar");
        Debe(!Abre("AlgoFallo"),
            "y un fallo tampoco lo abre: lo que hay que leer cabe en la píldora, y el globo trae "
            + "consigo la caja de texto, que invita a contestarle a un error");

        Debe(Saca("AlgoFallo"),
            "AHORA BIEN, un fallo SÍ saca el panel: la píldora vive dentro, y un fallo que nadie ve "
            + "es indistinguible de una aplicación que no hace nada");
        Debe(!Saca("SoloEsProgreso"),
            "y el progreso no saca nada: si cada paso de un workflow abriera el panel, trabajar con "
            + "Ü delante sería imposible");

        Debe(Enum.GetNames(motivos).Length == 4,
            "cuatro motivos y no un booleano: «lo pidió alguien», «hay que contestar», «algo falló» "
            + "y «solo es progreso» se tratan de tres formas distintas, y un sí/no no puede decirlo");
    }

    private static void ElBotonDeUnaVentanaAlterna()
    {
        var t = Capacidad("U.WindowsClient.Ui.ReglaDeLaVentana");
        var m = t?.GetMethod("AlPulsarSuBoton");
        Debe(t != null && m != null,
            "todavía no existe «ReglaDeLaVentana» (fase 7 de la spec 010). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (m == null) return;

        string Pulsar(bool existe, bool alFrente)
            => m.Invoke(null, new object[] { existe, alFrente })!.ToString()!;

        Debe(Pulsar(false, false) == "Abrir", "sin ventana, el botón la abre");
        Debe(Pulsar(true, true) == "Ocultar",
            "con la ventana delante, el mismo botón la quita de en medio: uno que solo sabe abrir "
            + "obliga a ir a buscar la equis");
        Debe(Pulsar(true, false) == "TraerAlFrente",
            "y MINIMIZADA o detrás se TRAE, no se ignora. Ese era el fallo: minimizada no es "
            + "oculta para Windows, así que la ventana existía y pulsar no hacía absolutamente "
            + "nada — el peor resultado posible, porque invita a pulsar otra vez");
    }

    private static void ElegirUnaFuenteSueltaLasDemas()
    {
        var t = Capacidad("U.WindowsClient.Voice.ReglaDeLaFuente");
        var m = t?.GetMethod("AlElegir");
        Debe(t != null && m != null,
            "todavía no existe «ReglaDeLaFuente.AlElegir» (fase 8 de la spec 010). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (m == null) return;

        string Elegir(Origen o) => m.Invoke(null, new object[] { o })!.ToString()!;

        Debe(Elegir(Origen.CollarPorBluetooth) == "Conectarlo",
            "elegir el collar lo conecta: es el único caso en que se queda escuchando");
        Debe(Elegir(Origen.MicrofonoDelPc) == "Soltarlo",
            "ELEGIR EL COMPUTADOR SUELTA EL COLLAR. Antes solo se dejaba de LEER: seguía conectado "
            + "y su audio seguía entrando, así que alguien que silenciaba el micrófono del portátil "
            + "grababa igual por el collar sin saberlo (2026-09-06). En una consulta clínica eso no "
            + "es un detalle de interfaz");
        Debe(Elegir(Origen.CollarPorTelefono) == "Soltarlo",
            "y elegir el teléfono también: un collar habla con UN aparato, así que si sigue "
            + "enlazado a este PC se seguiría oyendo por él — lo decía ya el comentario del código "
            + "que no lo hacía");
    }

    private static void ElVerdeEsDeQuienEntrega()
    {
        var t = Capacidad("U.WindowsClient.Voice.ReglaDeLaFuente");
        var m = t?.GetMethod("SeVeActiva");
        Debe(t != null && m != null,
            "todavía no existe «ReglaDeLaFuente.SeVeActiva» (fase 8 de la spec 010). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (m == null) return;

        bool Verde(Origen fila, Origen real, bool entregando)
            => (bool)m.Invoke(null, new object[] { fila, real, entregando })!;

        Debe(Verde(Origen.CollarPorBluetooth, Origen.CollarPorBluetooth, true),
            "la fuente que manda Y entrega se ve activa: para eso está el color");
        Debe(!Verde(Origen.CollarPorBluetooth, Origen.CollarPorBluetooth, false),
            "PERO MUDA NO. Es el verde falso del 2026-08-25: 56 minutos de «conectado» sin una sola "
            + "trama, delante de una demo. La única prueba de que hay micrófono es que llegue audio");
        Debe(!Verde(Origen.MicrofonoDelPc, Origen.CollarPorBluetooth, true),
            "y una fila que no es la fuente real no se pinta activa aunque esté llegando audio por "
            + "otra: el color de cada fila habla de ESA fila");
    }

    private static void LaVentanaSeAgarraPorDondeSeVe()
    {
        var t = Capacidad("U.WindowsClient.Ui.ReglaDelBorde");
        var m = t?.GetMethod("De");
        Debe(t != null && m != null,
            "todavía no existe «ReglaDelBorde.De» (fase 9 de la spec 010). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (m == null) return;

        // Una tarjeta metida 24 px dentro de una ventana de 470x660: exactamente lo que deja el
        // hueco de la sombra (promesa 154).
        var tarjeta = new System.Windows.Rect(24, 24, 422, 600);
        const double agarre = 8;
        string Zona(double x, double y)
            => m.Invoke(null, new object[] { new System.Windows.Point(x, y), tarjeta, agarre })!.ToString()!;

        Debe(Zona(235, 300) == "Ninguna",
            "en mitad de la ventana no hay borde: si lo hubiera, no se podría pulsar nada");
        Debe(Zona(24, 300) == "Izquierda" && Zona(446, 300) == "Derecha",
            "los lados de la TARJETA son los tiradores");
        Debe(Zona(235, 24) == "Arriba" && Zona(235, 624) == "Abajo", "y arriba y abajo igual");

        Debe(Zona(24, 24) == "ArribaIzquierda" && Zona(446, 624) == "AbajoDerecha",
            "EN UNA ESQUINA MANDA LA ESQUINA. Las dos condiciones se cumplen a la vez ahí, así que "
            + "quien pregunte por los lados primero devuelve «izquierda» en un punto que se ve "
            + "claramente como esquina");

        Debe(Zona(12, 300) == "Ninguna",
            "y el borde de la VENTANA no es un tirador: a 12 px del canto está la sombra, no el "
            + "dibujo. Medir contra la ventana pondría el agarre en el aire transparente");
        Debe(Zona(30, 300) == "Izquierda",
            "el agarre perdona hacia dentro, que es de donde viene la mano");
    }

    private static void LaCaritaApareceBajoElCursor()
    {
        var t = Capacidad("U.WindowsClient.Ui.ReglaDelMuelle");
        var m = t?.GetMethod("SitioAlAparecer");
        Debe(t != null && m != null,
            "todavía no existe «ReglaDelMuelle.SitioAlAparecer» (fase 10 de la spec 010). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (m == null) return;

        var area = new System.Windows.Rect(0, 0, 1920, 1040);
        var carita = new System.Windows.Size(128, 128);
        System.Windows.Point Aparece(double x, double y)
            => (System.Windows.Point)m.Invoke(null,
                new object[] { new System.Windows.Point(x, y), carita, area })!;

        var p1 = Aparece(800, 500);
        Debe(p1.X == 800 - 64 && p1.Y == 500 - 64,
            $"aparece CENTRADA en el cursor: lo que crees estar agarrando es la cara, no el vértice "
            + $"de una caja invisible; salió {p1}");

        // El caso real: se saca del muelle, que vive pegado al borde derecho.
        var enElBorde = Aparece(1530, 400);
        Debe(enElBorde.X <= area.Right - carita.Width,
            $"y sacándola pegada al borde derecho —que es de donde SIEMPRE se saca, porque el muelle "
            + $"vive ahí— entra entera en la pantalla; salió {enElBorde}");
        Debe(enElBorde.X > 1300,
            "pero sin irse lejos: acotar no es reubicar");
    }


    // ────────────────────────────────────────────────────────────────────────────────────────
    // SPEC 011 — FUERA LAS PASTILLAS Y LOS CARTELES (2026-09-06)
    //
    // Las cinco juzgan AUSENCIAS, y una ausencia no se comprueba mirando la pantalla: una captura
    // no distingue «no está» de «está y no se ve». Se le pregunta al binario que se distribuye.
    // ────────────────────────────────────────────────────────────────────────────────────────

    private const BindingFlags TodosLosCampos =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>Promesa 162.</summary>
    private static void FueraLasTresPastillas()
    {
        var cara = Capacidad("U.WindowsClient.Ui.FaceWindow");
        if (cara == null) { Pendiente("FaceWindow", "162", "011"); return; }

        // Los x:Name del XAML nacen como CAMPOS de la clase parcial generada, así que preguntar por
        // el campo es preguntar por el elemento — sin abrir una ventana y sin tocar la pantalla.
        foreach (var colgante in new[] { "VoiceDotGrupo", "ZonaVoz", "ZonaChat", "ZonaDictado" })
            Debe(cara.GetField(colgante, TodosLosCampos) == null,
                $"la carita ya no lleva «{colgante}» colgando: quitar un botón es quitar el elemento, no esconderlo");

        var dictado = Capacidad("U.WindowsClient.Clinical.Transcripcion.DictadoEnVivo");
        if (dictado == null)
        {
            Debe(false, "«DictadoEnVivo» tiene que seguir existiendo: la consulta (spec 004) lo usa");
            return;
        }

        Debe(cara.GetFields(TodosLosCampos).All(f => f.FieldType != dictado),
            "y no le queda ningún dictado clínico dentro: la puerta desde la carita se fue entera, no solo su icono");

        // LO COMPARTIDO SIGUE EN PIE, y esta mitad no es adorno: sin ella, «quité la pastilla» y
        // «rompí tres funciones» darían exactamente el mismo verde. El rellenador lo usa la
        // exportación a la historia clínica, la superficie de SAP la usa la demo, y el dictado la
        // consulta.
        Debe(Capacidad("U.WindowsClient.Clinical.RellenadorSap") != null,
            "«RellenadorSap» sigue vivo: lo usa la exportación a la historia clínica");
        Debe(typeof(GraphClient).Assembly.GetType("U.Graph.Surfaces.SapGuiSurface") != null,
            "«SapGuiSurface» sigue viva: la usan la demo y todo el camino de SAP");
    }

    /// <summary>Promesa 163.</summary>
    private static void ElHaloRodeaALaCarita()
    {
        var t = Capacidad("U.WindowsClient.Ui.ReglaDelHalo");
        if (t == null) { Pendiente("ReglaDelHalo", "163", "011"); return; }

        var escala = t.GetMethod("Escala", BindingFlags.Public | BindingFlags.Static);
        var color  = t.GetMethod("Color",  BindingFlags.Public | BindingFlags.Static);
        var caritaPx = t.GetField("CaritaPx")?.GetValue(null);
        var airePx   = t.GetField("AirePx")?.GetValue(null);
        if (escala == null || color == null || caritaPx == null || airePx == null)
        { Pendiente("ReglaDelHalo.Escala/Color/CaritaPx/AirePx", "163", "011"); return; }

        double carita = Convert.ToDouble(caritaPx), aire = Convert.ToDouble(airePx);
        double ventana = carita + 2 * aire;

        // CABE, y no «casi». La ventana de la carita suelta mide 72 + 2·28 = 128, y un halo que se
        // pasa de ahí no se ve grande: se ve CORTADO contra un borde que es un círculo con esquina.
        // Se barre el rango entero de voz y toda la fase del latido, porque el máximo puede estar
        // en cualquier punto y una regla solo se conoce por su peor caso.
        double mayor = 0;
        for (double nivel = 0; nivel <= 1.0001; nivel += 0.01)
            for (int paso = 0; paso < 80; paso++)
                mayor = Math.Max(mayor, Convert.ToDouble(escala.Invoke(null, new object[] { nivel, paso })));
        Debe(mayor * carita <= ventana,
            $"el halo cabe entero en el aire de la carita en TODO el rango de voz: el mayor fue "
            + $"{mayor:0.###}×{carita} = {mayor * carita:0.#} px y la ventana mide {ventana} px");

        // Y LATE CON LA VOZ, o es un adorno. Sin esta línea, una regla que devolviera siempre 1
        // pasaría la comprobación de arriba con nota — un criterio que no puede fallar con el bug
        // presente no es un criterio (patrón nº7).
        double callada  = Convert.ToDouble(escala.Invoke(null, new object[] { 0.0, 0 }));
        double gritando = Convert.ToDouble(escala.Invoke(null, new object[] { 1.0, 0 }));
        Debe(gritando > callada,
            $"y late con la voz en vez de quedarse quieto: callada {callada:0.###}, a todo volumen {gritando:0.###}");

        // EL COLOR DICE POR DÓNDE TE OYEN. Es la mitad que hace útil al halo: con el collar puesto,
        // saber que te oye el collar y no el portátil no es decoración (promesa 157).
        Debe(!Equals(color.Invoke(null, new object[] { true }), color.Invoke(null, new object[] { false })),
            "el halo del collar y el del micrófono del computador no son el mismo color");
    }

    /// <summary>Promesa 164.</summary>
    private static void NadieMuestraTextoAlPasarElRaton()
    {
        // NO PUDE ≠ CULPABLE (aprendizaje nº17). Esta promesa mira el CÓDIGO FUENTE, que es lo
        // único capaz de volver a llenarse de carteles, así que necesita el repo. Se lo pasa
        // scripts/contrato-del-grafo.ps1 en U_REPO. Sin eso el juez dice que NO PUDO, no que está
        // roto: un arnés que no distingue las dos cosas manda la investigación al sitio equivocado.
        string repo = Environment.GetEnvironmentVariable("U_REPO") ?? "";
        string fuentes = Path.Combine(repo, "windows-client", "src");
        if (repo.Length == 0 || !Directory.Exists(fuentes))
        {
            _fallos++;
            Console.WriteLine("   ⚠ NO PUDE JUZGARLA: sin U_REPO no hay fuentes que mirar "
                            + "(lo pone scripts/contrato-del-grafo.ps1). No es que la promesa falle: "
                            + "es que no llegué a probarla.");
            return;
        }

        var vivos = new List<string>();
        foreach (var f in Directory.EnumerateFiles(fuentes, "*.*", SearchOption.AllDirectories))
        {
            if (!f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
             && !f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)) continue;
            // El apagador NOMBRA lo que apaga: contarlo sería pedirle que no se pueda escribir.
            if (Path.GetFileName(f).Equals("SinCarteles.cs", StringComparison.OrdinalIgnoreCase)) continue;

            // CUALQUIER mención, no tres formas concretas. El primer detector buscaba `ToolTip="`,
            // `.ToolTip =` y `ToolTip = "`, y se dejó fuera el de CarruselDeApps.cs porque ese usa
            // interpolación (`ToolTip = $"…"`): una comparación que enumera las formas que se me
            // ocurrieron da falso en silencio para la que no (aprendizaje nº16). Aquí no hay formas
            // que enumerar: si la palabra aparece, hay cartel.
            int n = File.ReadLines(f).Count(l => l.Contains("ToolTip"));
            if (n > 0) vivos.Add($"{Path.GetFileName(f)}×{n}");
        }
        Debe(vivos.Count == 0,
            $"no queda ni una declaración de texto al pasar el ratón en windows-client; siguen vivas en: "
            + string.Join(", ", vivos));

        // Y EL APAGADO, que es lo que hace que esto no sea una lista de 44 tachones: un elemento
        // que declare su cartel MAÑANA tampoco lo enseña. Se comprueba de verdad —creando un
        // control y preguntándole— y no viendo si el método existe: existir no es hacer.
        var t = Capacidad("U.WindowsClient.Ui.SinCarteles");
        var aplicar = t?.GetMethod("Aplicar", BindingFlags.Public | BindingFlags.Static);
        if (aplicar == null) { Pendiente("SinCarteles.Aplicar", "164", "011"); return; }

        aplicar.Invoke(null, null);
        var reciente = new System.Windows.Controls.Button { ToolTip = "un cartel escrito mañana" };
        Debe(!System.Windows.Controls.ToolTipService.GetIsEnabled(reciente),
            "y con el apagado puesto, un control que declare su cartel tampoco lo enseña: "
            + "el apagado vive en UN sitio y alcanza a lo que aún no se ha escrito");
    }

    /// <summary>Promesa 165.</summary>
    private static void LaLineaPideReposo()
    {
        var t = Capacidad("U.WindowsClient.Ui.ReglaDeLaLinea");
        if (t == null) { Pendiente("ReglaDeLaLinea", "165", "011"); return; }

        var asoma = t.GetMethod("Asoma", BindingFlags.Public | BindingFlags.Static);
        var reposoMs = t.GetField("ReposoMs")?.GetValue(null);
        if (asoma == null || reposoMs == null) { Pendiente("ReglaDeLaLinea.Asoma/ReposoMs", "165", "011"); return; }
        int ms = Convert.ToInt32(reposoMs);
        bool Asoma(int quieto, bool arrastrando) =>
            (bool)asoma.Invoke(null, new object[] { quieto, arrastrando })!;

        // Medio segundo es el suelo, y no es gusto: por debajo, ir a agarrar la carita ya invoca el
        // cartel — que es exactamente el motivo por el que esto existe.
        Debe(ms >= 500, $"el reposo es un reposo y no un parpadeo: son {ms} ms");
        Debe(!Asoma(0, false), "rozarla de camino a otra cosa no la llama");
        Debe(!Asoma(ms - 1, false), "ni fallando un milisegundo para el reposo");
        Debe(Asoma(ms, false), "cumplido el reposo, asoma");
        Debe(!Asoma(ms * 10, true), "pero arrastrándola no asoma por mucho que se tarde: ir a moverla no es ir a escribirle");
    }

    /// <summary>Promesa 166.</summary>
    private static void EscribirleEsEscribir()
    {
        var t = Capacidad("U.WindowsClient.Ui.ReglaDeEscritura");
        var abre = t?.GetMethod("Abre", BindingFlags.Public | BindingFlags.Static);
        if (abre == null) { Pendiente("ReglaDeEscritura.Abre", "166", "011"); return; }
        bool Abre(uint vk, bool encima, bool ctrl, bool alt) =>
            (bool)abre.Invoke(null, new object[] { vk, encima, ctrl, alt })!;

        Debe(Abre(0x41, true, false, false), "una letra con el ratón encima abre el globo");
        Debe(Abre(0x31, true, false, false), "y un dígito también");
        Debe(!Abre(0x41, false, false, false), "sin el ratón encima, ninguna tecla es para Ü");
        Debe(!Abre(0x41, true, true, false), "Ctrl+A sigue siendo de la app de debajo");
        Debe(!Abre(0x41, true, false, true), "y Alt+A también");

        // LO IMPORTANTE NO ES QUÉ ABRE, SINO QUÉ NO SE ROBA. El cursor puede estar apoyado en la
        // carita mientras alguien trabaja en SAP: quitarle un F5, un Enter o un Esc a esa persona
        // sería peor que no tener el gesto.
        var jamas = new (uint Vk, string Nombre)[]
        {
            (0x70, "F1"), (0x74, "F5"), (0x7B, "F12"), (0x1B, "Esc"), (0x0D, "Enter"),
            (0x09, "Tab"), (0x25, "flecha izquierda"), (0x26, "flecha arriba"),
            (0x20, "espacio"), (0x08, "retroceso"), (0x2E, "suprimir"),
        };
        foreach (var (vk, nombre) in jamas)
            Debe(!Abre(vk, true, false, false),
                $"«{nombre}» nunca se le roba a la app de debajo, ni con el ratón sobre la carita");
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
