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
        Prueba("142. durante la comprobación Ü habla con SU voz: se le puede pedir que diga algo, y eso viaja con la petición de respuesta; y se pide FUERA de la conversación, sin contexto que la tiente a parafrasear, añadir o proponer", LaVozDeLaComprobacionEsLaDeU);
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

        // ── ACTA DE RETIRO: 165 y 166 (spec 011, 2026-09-06) ────────────────
        //
        // Aqui vivieron la linea «Escribele…» y el escribir con el raton sobre la carita. Llegaron
        // a verde, se sabotearon las dos y las dos se pusieron rojas por su motivo. Las retira el
        // dueno tras verlas en pantalla: la burbuja no le convence, y una puerta que no gusta es
        // peor que no tener puerta cuando ya existen dos que si — Ctrl+Alt+U abre el globo con el
        // foco puesto, y el muelle lo tiene a un cursor de distancia.
        //
        // Los numeros NO se reciclan: la spec 011 y los commits de esta rama los citan por numero,
        // y reusarlos haria que un plan viejo hablara de otra cosa.

        // LA LINEA «ESCRIBELE…» VUELVE, con el diseno original y esperando de verdad (2026-09-06).
        // La 165 se retiro hace una hora porque la burbuja no convencia; el dueno pidio la de la
        // rama 008 tal cual y que tardara «un segundo y medio». Numero NUEVO: 165 esta retirada y
        // los numeros no se reciclan.
        Prueba("167. la línea «Escríbele…» se hace esperar: segundo y medio de ratón quieto sobre la carita, y arrastrarla no la llama por mucho que se tarde", LaLineaSeHaceEsperar);

        // LA LECCIÓN QUE CLAUDE VE (spec 013, 2026-09-06). El pantallazo por paso se disparaba AL
        // OBSERVAR el paso —después del clic y de su efecto—, que es la carrera que el dueño lleva
        // años pagando: «que los screenshots se tomen cuando ya un elemento cambió por el clic».
        // La salida no es disparar más rápido: es no disparar, y elegir del pasado.
        Prueba("168. el cuadro de ANTES de un clic se elige del pasado: es el más nuevo con hora ≤ hora del clic menos el margen, y ningún cuadro tomado en o después del clic puede ser elegido; si no hay ninguno, no hay cuadro de antes (null), nunca uno de después disfrazado", ElCuadroDeAntesSeEligeDelPasado);
        Prueba("169. el cuadro de DESPUÉS es el primero, pasado el clic más la espera mínima, en que la pantalla se asentó (dos cuadros seguidos iguales); si no se asienta antes del techo, se entrega el último y se dice asentado=false — nunca se calla", ElCuadroDeDespuesEsElPrimeroAsentado);
        Prueba("170. cada clic físico de la demostración deja UN evento en la lección, con su hora, su punto y sus dos cuadros, aunque SAP no haya emitido paso: 26 clics son 26 eventos, no 1", CadaClicFisicoDejaUnEvento);
        Prueba("171. lo que SAP observó (selector, texto tecleado, tecla) y lo que la persona dijo se cuelgan del evento por cercanía en el mismo reloj; una frase se cuelga de UN clic, el más cercano, y un paso de SAP sin clic cercano queda como evento propio con porTeclado=true; y si SAP trae OTRA puerta que la que el vigía adivinó por geometría, manda la de SAP con su nombre", LoObservadoYLoDichoSeCuelganPorCercania);
        Prueba("172. la lección se escribe en disco entera o no se escribe: leccion.json + cuadros/ + demo.mp4 + donde empezó y donde terminó; una lección sin cuadros o sin mp4 no se entrega, y el motivo queda escrito", LaLeccionSeEntregaEnteraONada);
        Prueba("173. el mensaje que recibe el piloto se arma de la lección en orden de tiempo, con una etiqueta t=MM:SS · clic N en (x,y) · selector · «lo dicho» delante de cada cuadro; ningún cuadro pasa de 2000 px de lado, y si hay más cuadros que el presupuesto se quitan primero los de después repetidos y NUNCA el de antes de un clic con selector", ElMensajeDelPilotoEsUnTutorialEnOrden);
        Prueba("174. el piloto tiene UNA caja con manos desde el principio —entender es ir, colgar el recuerdo donde vive el elemento y declarar la llegada— y lo único prohibido, por su nombre, es lo que va en tanda: map_batch y map_skill_run; las manos, los ojos, la voz y mirar cualquier momento de la demo están todos", ElPilotoTieneUnaCajaConManos);
        // ENMENDADA EL 2026-09-08 con el dueño: «habrá enseñanzas que no naveguen». Nació diciendo
        // «el total = eventos que navegan», y con eso una demo que solo rellena el triage daba «0 de 0:
        // sigue pendiente» habiendo hecho los 14 pasos. Ahora lo tecleado también cuenta, y se juzga
        // LEYENDO el campo, no creyéndole al modelo.
        Prueba("175. la comprobación es hacer de uno en uno lo que la lección enseña y que cada paso lo juzgue la app: un evento que navega aterriza si la pantalla de ahora es la grabada; un campo tecleado que no navega está hecho si el campo dice AHORA lo que la demo tecleó, leído de la pantalla y no declarado; el recuento es hechos/total con el total = los eventos que navegan más los campos tecleados, uno por campo con su último valor, y COMPROBADA solo si todos están: una lección que no navega pero teclea se comprueba entera, y un «llegué» sobre el clic que abrió un campo se juzga sobre el campo", LaAppJuzgaCadaLlegada);
        Prueba("176. la skill se empaqueta de lo VERIFICADO: cada paso lleva la llegada real medida al comprobar, y un paso que no aterrizó no entra en la skill; lo que el modelo dijo entender se guarda aparte, como recuerdos, no como pasos", LaSkillNaceDeLoVerificado);
        Prueba("177. el piloto puede pedir la pantalla de cualquier momento de la demo, aunque ahí no hubiera clic: se le da el cuadro más cercano a ese instante con dónde estaba el ratón, y sin cuadros se dice que no hay", CualquierMomentoDeLaDemoSePuedeMirar);
        Prueba("178. a dónde llevó un clic lo dice el TERRENO —la misma arista que el batch verifica—, no la lección: si el terreno aprendió que esa puerta lleva a aquella pantalla, esa es la llegada; si no aprendió nada, queda vacía y se dice; solo el último clic tiene de respaldo donde acabó la demo; los clics sobre Ü no cuentan; y al terreno se le pregunta con la puerta que SAP vio, no con la que el vigía adivinó", LaLlegadaLaDiceElTerreno);
        Prueba("179. comprobar es un PLAN que el piloto entrega en el idioma del ejecutor y la app recorre: por cada paso la voz, el recuerdo antes de tocar, el paso por el mismo ejecutor de tanda y el juez; si un paso no se puede dar la app para ahí y le devuelve al piloto dónde quedó y qué faltó, y solo entonces el piloto actúa con las manos; y el relato nombra lo que falta por juzgar, evento por evento, para que las manos vayan a eso y no a repetir lo hecho", ComprobarEsUnPlanQueLaAppRecorre);

        // EL RECUERDO SE VE (spec 014, 2026-09-07). La quinta prueba de la 013 salió COMPROBADA y
        // nadie vio nada: el recuerdo se colgaba y el paso se daba sin tarjeta ni carita al lado.
        Prueba("180. al recorrer el plan, cada paso se VE antes de tocarse: la carita se pone al lado del elemento y el elemento se enciende, se dice y se escribe el recuerdo, la tarjeta con el recuerdo queda a la vista el tiempo que tarda leerla, y solo después se toca; al tocar, la tarjeta se cierra y la señal se suelta; si el elemento no está en pantalla no se muestra tarjeta sobre la nada y el paso va igual al ejecutor", ElRecuerdoSeVeAntesDeTocar);

        // LOS OJOS DEL PILOTO (2026-09-07). Seleccioné la fila de un paciente CORRECTAMENTE y no tuve
        // forma de saberlo: seleccionar NO cambia la pantalla, así que el juez por llegada es ciego a
        // media SAP. El dueño lo vio de golpe: «con otro screenshot de lo actual puedes determinar si
        // está bien o mal e iterar rápidamente». map_shot ya tomaba la foto; el protocolo la
        // empaquetaba como TEXTO y al modelo le llegaba un chorro de base64 que no puede mirar.
        Prueba("181. una herramienta que devuelve una imagen la entrega como IMAGEN y no como un texto de base64 que el modelo no puede mirar: el data URI se parte en su tipo y sus datos y viaja como bloque de imagen; lo que no es imagen, y un data URI roto, siguen viajando como texto", UnaImagenViajaComoImagen);

        // LA FILA DEL PACIENTE (spec 014, 2026-09-07). La 6ª prueba se rompió en el clic que
        // selecciona al paciente: el vigía solo nombra árboles, no rejillas, así que la fila quedó
        // muda al enseñar; y map_what_i_see solo lista lo de UIA, así que «GIRALDO» era invisible al
        // comprobar aunque map_take sabía seleccionarlo. Dos ajustes que reusan la lectura del grabador.
        Prueba("182. la fila de una rejilla ALV bajo un clic se elige por su selección, o por el cursor, o —si solo hay una fila— por ser la única, aunque nada esté marcado todavía: un paciente premarcado ya no queda sin identidad", LaFilaDeAlvSeElige);
        Prueba("183. map_what_i_see suma a lo que ve UIA las puertas que el terreno conoce y UIA no puede ver —las filas de una rejilla, las de un árbol— sin duplicar las que ya tienen nombre: la fila del paciente deja de ser una puerta invisible", ElTerrenoCompletaLoQueUiaNoVe);

        // LO TECLEADO EN LA DEMO (2026-09-07, séptima prueba). La persona rellenó el triage entero y la
        // lección no traía NI UN texto: SAP solo publica lo tecleado cuando la pantalla VIAJA (Change /
        // StartRequest), y la demo acabó sin viajar. Al parar se descargan todos los campos a la vez,
        // con la hora de parar: por cercanía irían al último clic —o a ninguno—, y no al campo que
        // cada uno abrió. La identidad del selector, que ahora el vigía también nombra, es lo que cuadra.
        Prueba("184. un paso tecleado que SAP publica tarde —al parar la demo, todos a la vez— se cuelga del clic que abrió ESE campo, por identidad de selector, aunque por tiempo le tocara otro clic; sin clic con esa identidad, rige la cercanía de siempre", LoTecleadoSeCuelgaDelCampoQueLoAbrio);
        Prueba("185. un campo de SAP se encuentra por lo que la persona ve o por lo que la lección trae: su selector exacto, su etiqueta («Nombre del paciente») o el nombre técnico del campo sin el prefijo del tipo («Y0000000-ZTRNOMPAC» para txtY0000000-ZTRNOMPAC); con dos etiquetas iguales no se adivina: nadie", UnCampoDeSapSeEncuentraPorSuNombre);

        // EL HIT-TEST QUE SÍ CONTESTA (2026-09-07, octava prueba). Escribí «el campo bajo el clic» con
        // FindByPosition y la demo entera salió sin un solo clic nombrado: medido sobre el SAP real,
        // FindByPosition devuelve NULL en los 8 puntos probados y la geometría acierta el campo en los
        // 8, la caja de texto largo incluida. El repo YA lo tenía documentado y ya tenía el hit-test
        // por geometría —lo usa el grabador para los botones del dynpro—: escribí una tercera vía en
        // vez de reusar la que funciona. La promesa fija cuál manda y qué se hace cuando la primera calla.
        Prueba("186. quién está bajo un punto dentro de SAP se pregunta primero a SAP y, cuando SAP calla —que es lo que hace este SAP siempre—, se decide por geometría: el elemento MÁS PEQUEÑO que contiene el punto, sin contar nodos ni cajas desconocidas; y del id que salga se sube por su ruta hasta el campo que la lección puede reencontrar, porque el punto puede caer en un hijo de la caja de texto y no en la caja", ElComponenteBajoElPuntoSeDecidePorGeometria);

        // EL ORDEN AL CERRAR LA DEMO (2026-09-07, octava prueba). Con eventos COM, SAP solo publica lo
        // tecleado cuando la pantalla viaja; una demo que acaba sin viajar deja los campos sin publicar.
        // Puse la descarga en StopObserving, que corre en recorder.StopAsync… treinta líneas DESPUÉS de
        // escribir la lección. La descarga funcionaba y la lección salía vacía igual.
        Prueba("187. al cerrar la demo, lo que la persona tecleó y SAP no llegó a publicar se descarga LO PRIMERO, con el oyente todavía enganchado: descargar va antes de soltar la superficie, antes de parar el video y antes de armar la lección, y parar al grabador va el último; sin video no se para ningún video y sin superficie observada no hay nada que descargar ni que soltar, pero donde hay superficie, descargar precede a todo", LoTecleadoSeDescargaAntesDeArmarLaLeccion);

        // LOS RECUERDOS QUE NO SE COLGARON (2026-09-08, novena prueba). El piloto pidió un recuerdo en
        // los 14 campos del triage y la app rechazó 11: «no veo nada que se llame Presión Arterial en
        // esta pantalla». Colgar un recuerdo buscaba el nombre entre lo de UIA y las puertas del
        // terreno; un campo del dynpro no está en ninguna de las dos. Escribir SÍ lo encontraba
        // (promesa 185). Dos criterios para nombrar la misma cosa es un bug esperando su turno.
        Prueba("188. lo que la persona NOMBRA dentro de SAP —para colgarle un recuerdo, para señalarlo o para verlo en la lista de lo que hay— se resuelve en UN solo sitio y con el mismo criterio que para escribir: primero las puertas del terreno, después los campos del dynpro por su etiqueta o su nombre técnico; con dos iguales nadie, y lo que ningún sitio conoce no se inventa", LoQueSeNombraEnSapSeResuelveEnUnSitio);

        // LA ARISTA QUE EL MAPA VIVO NO ALCANZA (2026-09-08, décima prueba, y ya en la 5ª y la 7ª). Al
        // pulsar «Triage», SAP tarda más de lo que el mapa vivo espera (6 s) y encima se queda ciego
        // mientras SAP no contesta; al volver, el salto queda «SIN atribuir». El grabador de SAP sí lo
        // vio: publicó el paso a las 01:11:38 y anunció la pantalla nueva a las 01:11:41. Tres pruebas
        // seguidas con la llegada de Triage vacía, y una lección que sin ella no se puede comprobar.
        Prueba("189. lo que el grabador de SAP ve cruzar —un paso publicado y la pantalla nueva que SAP anuncia justo después— se le enseña al terreno como arista, con el mismo selector con el que el terreno conoce la puerta: el paso tiene que ser reciente, haber salido de la pantalla que cambió, y el destino ser una pantalla de SAP y no la misma; la llegada la sigue diciendo el terreno, que ahora también aprende de SAP", ElGrabadorDeSapEnsenaAlTerreno);

        // LOS SELECTORES DE GLASGOW (2026-09-08, décima prueba, vista por el dueño). Un GuiComboBox no
        // acepta Text: se le fija la CLAVE. La lección trae la clave («4»); el piloto, con las manos,
        // intentó el texto («Espontánea»). Las dos tienen que valer, y una que no es ninguna, decirlo.
        Prueba("190. en un desplegable de SAP se fija la CLAVE de la opción, y esa clave se encuentra por la clave misma o por el texto que la persona lee, sin distinguir mayúsculas, acentos ni espacios; con dos opciones iguales nadie, y un valor que no es ninguna opción se rechaza diciendo cuáles hay", EnUnDesplegableSeFijaLaClave);

        // LA EXPERIENCIA ESTÁNDAR (2026-09-08, el dueño sobre la undécima prueba): «la carita se movía
        // al lado de cada campo y narraba lo que iba haciendo, y justo después sucedía; en esta no».
        // En la undécima el plan paró en el paso 3 —el piloto trajo el SELECTOR de la fila, no su
        // nombre— y el resto lo hizo el piloto con las manos, que no tenían coreografía: ni carita, ni
        // narración, ni tarjeta. La coreografía era del plan, no del paso.
        Prueba("191. dar un paso es UNA coreografía, lo dé el plan o la mano del piloto —señalar, decir, escribir el recuerdo, mostrar la tarjeta, actuar, cerrar—, y map_take y map_type llevan «decir» y «recuerdo» para eso; y la identidad del paso se decide en un solo sitio: se señala y se nombra por la PUERTA aunque el piloto traiga el selector, y se escribe por el SELECTOR de la lección aunque el piloto traiga el nombre", DarUnPasoEsUnaCoreografia);

        // DOS MANOS A LA VEZ (2026-09-08, duodécima prueba). Mientras la app recorría el plan, la
        // conversación de voz en vivo —que solo tenía que PRESTAR la voz— llamaba map_take y map_type por
        // su cuenta, con sus propias frases: «Cambio de foco para intentar escribir en el campo
        // adecuado», «Escribo la presión arterial» con un 120/80 que no era el de la demo. Tenía el
        // catálogo entero y respuestas automáticas: cada «di exactamente esto» era un turno más de un
        // asistente con manos. El dueño: «la voz se desalineaba de lo que realmente se estaba haciendo».
        Prueba("192. durante la comprobación la voz en vivo es una voz PRESTADA: no tiene ni una herramienta, sus instrucciones son decir exactamente lo que la app le pide y callar ante todo lo demás, y la sesión no crea respuestas por su cuenta —solo cuando la app se lo pide—; al terminar, vuelve a ser quien era", LaVozDeLaComprobacionEsPrestada);

        // ── Lo hace a la primera (spec 017) ──────────────────────────────────
        //
        // Una nota de voz del 2026-09-10 fijó el foco: «que lo que uno le pida lo haga, y lo haga a
        // la primera, máximo dos intentos, máximo dos segundos». Medido esa noche: ante homónimos el
        // ejecutor pedía «el selector» sin número ni destino, no había tope de intentos en el código,
        // un éxito no decía si la pantalla había cambiado, y el catálogo pedía argumentos muertos.
        //
        // Empiezan en la 202 y no en la 193 a propósito: dos ramas abiertas de Jose ya usan números
        // por encima de la 192 y tendrán que renumerar sobre main (docs/specs/017, «Numeración»).
        Console.WriteLine();
        Prueba("202. pulsar dice lo que pasó: una tanda que termina bien distingue «ahora estás en Y» de «la pantalla no cambió», y «hice los N paso(s)» ya no tapa el último hecho", PulsarDiceLoQuePaso);
        Prueba("203. con varias puertas vivas para un mismo nombre no se pide un selector a ciegas: se numeran 1..N en orden estable con su tipo y, si se sabe, a dónde llevan; y un paso que trae cuál pulsa esa y solo esa", LosHomonimosSeNumeran);
        Prueba("204. dos intentos y no tres: en un mismo turno del usuario, la tercera acción hacia un destino que ya falló dos veces no se ejecuta, y se dice qué salió en cada una; al pulsar con map_take, el mismo botón es el mismo destino se pida como se pida; al escribir, el campo cuenta tal como se pidió", DosIntentosYNoTres);
        Prueba("205. cada turno del usuario deja una línea voz-turno con su medida: llamadas, herramientas distintas, el máximo de intentos a un mismo destino y los milisegundos hasta la primera acción y la última; lo rechazado y lo retirado también cuentan", CadaTurnoDejaSuMedida);
        Prueba("206. el catálogo le pide al cerebro lo que las manos usan: map_take y map_type no ofrecen argumentos que su cuerpo ignora, map_take trae which, y las instrucciones mandan mirar y elegir con which antes que preguntar; la regla escrita de intentos es la del código: dos", ElCatalogoPideLoQueLasManosUsan);
        Prueba("207. la mano dice, sin prosa, si fue un intento y si lo logró: pulsar y que cambie la pantalla es logro, pulsar y que no cambie no lo es, pedir algo que no está es un intento fallido, y la lista de homónimos no es un intento", LaManoDiceSiLoLogro);

        // ── LA VOZ ES GPT-LIVE (spec 018, 2026-09-12) ─────────────────────────
        // GPT-Live abre por otra puerta, no manda marcas de turno y no sabe esperar a que se le pida:
        // lo que depende de eso es de la CONVERSACIÓN, que vive de este lado (el traductor lo juzga el
        // contrato de la voz, 40-43). Y un agujero que ya estaba en main con Realtime: lo escrito no
        // pedía respuesta, y la noche del 2026-09-11 el nivel 4 lo midió como una voz muda.
        Console.WriteLine();
        Prueba("208. escribir con la voz abierta pide respuesta: el texto va seguido de pedir turno, con cualquier protocolo", EscribirPideRespuesta);
        Prueba("209. con una voz que no marca los turnos, la conversación los marca: el primer trozo de lo que dice el usuario abre un turno y un silencio lo cierra", SinMarcasLaConversacionMarcaLosTurnos);
        Prueba("210. la voz por defecto es GPT-Live y U_VOZ=realtime vuelve a GPT Realtime", LaVozPorDefectoEsGptLive);
        Prueba("211. sin marcas de turno, el turno no se cierra con trabajo en marcha: ni con una llamada a herramienta sin devolver ni mientras suena la voz de Ü, y el silencio se cuenta desde la devolución o desde lo último que sonó; el audio en silencio no cuenta", SinMarcasElTurnoEsperaAlTrabajo);
        Prueba("212. sin marcas de turno, una pausa del usuario sin que Ü le haya contestado sigue siendo la misma petición: lo que dice después no abre turno ni reinicia el tope; lo que dice después de que Ü le conteste, sí", UnaPausaSinRespuestaEsLaMismaPeticion);
        Prueba("214. con GPT-Live, varias llamadas pedidas a la vez se contestan todas antes de pedir turno, y el turno se pide una sola vez; con GPT Realtime una tanda sigue pidiendo turno detrás de sus resultados", VariasLlamadasUnSoloTurno);
        Prueba("217. con GPT-Live el log no se inunda: ni los deltas del delegado ni el audio dejan una línea «←», y lo demás que no se traduce la sigue dejando", ElLogNoSeInundaConLosDeltas);
        Prueba("218. con GPT-Live lo que dura la voz llega al cierre: los segundos que cuenta el servidor —el último acumulado de cada conexión, sumado entre conexiones— se reportan aunque no haya fichas, y el cierre deja una línea voz-viva con esos segundos; con fichas y sin segundos, el reporte sigue como estaba", LaDuracionDeGptLiveLlegaAlCierre);
        // LA MISMA CLASE DE ERROR, UN SOLO TRATAMIENTO (2026-09-13). Sin crédito, Realtime reconectó cuatro veces y GPT-Live
        // no reintentó. Lo que no se arregla reintentando —cuenta, clave, modelo— se reconoce por su código medido (223) y
        // la conversación lo usa con los dos protocolos (224).
        Prueba("223. lo que no se arregla reintentando se reconoce por su código y dice su causa: sin crédito (insufficient_quota, credit_balance_exhausted), una clave que no vale (invalid_api_key, o el apretón de manos rechazado con 401) y un modelo que no existe (invalid_model, model_not_found), también dentro de la descripción de un cierre; cada causa se distingue de las otras, y cualquier otro código, un número de cierre, la prosa del mensaje o nada se pueden reintentar", LoQueNoSeArreglaReintentandoSeReconoce);
        Prueba("224. con los dos protocolos, lo que no se arregla reintentando no reconecta: venga en un error, en el cierre del socket o en el apretón de manos de la reconexión, la conversación dice una vez por qué y cierra la voz; un corte sin esa causa sigue reconectando, y una conexión nueva no hereda la causa de la anterior", LoQueNoSeArreglaNoReconecta);
        Prueba("230. la ventana de delante se elige con UNA regla, la misma para el localizador y para el lector de elementos: bajando por el orden Z desde la que tiene el foco, la primera visible y con título que no sea de Ü; si delante está Ü, la ubicación se calcula ahora con esa regla y no se devuelve la última recordada", LaVentanaDeDelanteSeEligeConUnaRegla);
        Prueba("231. la mano dice por qué no pudo: el registro de la superficie llega al log, y «no pude pulsar» trae la causa —no encontré el elemento en esa ventana, no admite ningún patrón, el patrón falló—, en vez de una sola frase para las tres", LaManoDicePorQueNoPudo);
        Prueba("232. abrir una app mira primero qué ventanas suyas existen y lo dice: con una o varias abiertas trae una al frente y las nombra, y solo lanza otra si se pide una instancia nueva; sin ninguna abierta, lanza como siempre", AbrirMiraQueVentanasHay);
        Prueba("233. Ü tiene una ventana de trabajo distinta del foco de la persona: lo que ejecuta se resuelve respecto a ella; si no hay, es el foco de la persona; y si la que tenía ya no existe, lo dice y vuelve al foco de la persona en vez de describir una pantalla cerrada", LaVentanaDeTrabajoDeU);
        Prueba("234. pulsar en la ventana de trabajo va por el patrón de accesibilidad cuando el elemento lo admite, sin traer nada al frente y sin mover el ratón; el clic físico es la excepción para lo que no admite patrón o no navega con él, y cuando se usa se devuelven el foco y el cursor a la persona", ElClicSinRatonVaPorElPatron);

        // SPEC 021: LA MANO ESCRIBE Y DESBLOQUEA DONDE SE LE PIDIÓ (2026-09-14, 19:38 a 20:02). Escribir con
        // `target` por nombre fallaba con «no encontré el elemento «»», una terminal nunca se podía escribir, y
        // map_unblock cerró Chrome dos veces pulsando el primer «Cerrar» de la ventana en vez del de la barra.
        Prueba("235. escribir va a la ventana de trabajo: un `target` por nombre se resuelve a un campo de texto de esa ventana y se escribe por patrón, sin foco ni ratón; en una terminal, donde no hay campo, se teclea trayéndola al frente y devolviendo el foco y el cursor; y el error nombra el campo y la ventana en vez de decir «no encontré el elemento «»»", EscribirVaALaVentanaDeTrabajo);
        Prueba("236. desbloquear pulsa el botón que leyó dentro del diálogo, no un nombre buscado en toda la ventana: el detector entrega el diálogo con sus botones enganchados, `map_unblock` pulsa esa opción y ninguna otra, la política de opciones seguras y los vetos siguen iguales, y un `at` que es el título del diálogo no dispara ninguna reanudación", DesbloquearPulsaElBotonQueLeyo);
        Prueba("237. la escalera del clic sin cursor tiene tres peldaños en este orden: el patrón (Invoke o Toggle), el clic por mensaje a la ventana del elemento en su punto pulsable, y el ratón real; el contenido de listas y lo que no tiene punto pulsable van directo al ratón real, y el patrón sigue ganando a todo cuando existe", LaEscaleraDelClicSinCursor);
        Prueba("238. cerrar la ventana de trabajo cuando el modelo lo decide sigue siendo un clic normal: «Cerrar» no es un verbo destructivo, se pulsa por patrón sin cursor, y la cuenta dice que la ventana ya no existe en vez de inventar a dónde se fue (regresión del 2026-09-14 19:40:43)", CerrarLaVentanaSigueSiendoUnClicNormal);

        // SPEC 022: GUARDAR ES UNA DECISIÓN, Y LA CARITA VA A DONDE SE PULSA (2026-09-14, 20:26). El veto de
        // responder diálogos usaba la lista del explorador autónomo y bloqueaba «No guardar»; y desde que los
        // clics van sin ratón, la carita ya no acompaña a la mano a ninguna parte.
        Prueba("239. responder un diálogo deja guardar y deja NO guardar: el veto de lo destructivo tiene su propia lista —lo que no se deshace— y no la del explorador autónomo; «Guardar», «No guardar» y «Aplicar» se pulsan cuando el modelo lo pide, mientras «Eliminar», «Formatear», «Reiniciar», «Enviar» y «Aceptar» siguen vetados; una etiqueta que niega el verbo pegado a él no es ese verbo; y el explorador autónomo no se relaja", GuardarYNoGuardarSePuedenPulsar);
        Prueba("240. la carita va a donde Ü acaba de pulsar: los tres clics de la mano avisan con la caja del elemento y escribir o elegir no, el viaje solo vale la pena a partir de un salto real, y su curva es fluida y rápida —empieza acelerando, no se devuelve, no rebota, hace más de medio camino en el primer tercio del tiempo y cruzar la pantalla entera no pasa de 450 ms—", LaCaritaVaADondeSePulsa);

        // «SESIÓN ABIERTA» SE ESCRIBÍA AL CONECTAR EL SOCKET (2026-09-13, nivel 4 del 12): con la cuenta sin crédito
        // salió en el mismo segundo que el error, y el conductor del nivel 4 la tomó por voz abierta. Del 220 al 222 son
        // de la rama de la apertura; el 219 quedó sin usar en la spec 018 y no se recicla.
        Prueba("220. la voz dice que la sesión abrió cuando el servidor lo confirma, no cuando conecta el socket: al conectar deja una línea que dice que espera la confirmación, y la de «sesión abierta con» sale una sola vez, al confirmarla, con «Te escucho.» detrás, con GPT-Live y con GPT Realtime; un error antes de confirmar no la escribe, y con un protocolo que no confirma la línea dice que nadie la confirmó", LaVozDiceQueAbrioCuandoElServidorLoConfirma);

        // EL NOTCH SE APOYA EN LA BARRA DE TAREAS Y EN SU HUECO LIBRE (2026-09-14). El panel de
        // acciones vivía clavado en `wa.Left + 12`, que es la esquina correcta en un Windows 10
        // —iconos a la izquierda— y la equivocada en un Windows 11 de fábrica, donde los iconos van
        // al centro y esa esquina es justo la ocupada. La promesa juzga el INTERCAMBIO, que es lo
        // único que aquí no es dibujo: si los iconos ocupan el centro, el notch se va a la esquina,
        // y si ocupan la esquina, el notch se va al centro.
        Prueba("241. el notch se apoya en la barra de tareas y ocupa la mitad que ella deja libre: con los iconos al centro (el Windows 11 de fábrica) se va a la esquina, y con los iconos a la izquierda se va al centro — y nunca sale del cristal", ElNotchSeApoyaDondeLaBarraDejaSitio);

        // EL NOTCH ES BLANCO Y NEGRO (spec 023, 2026-09-15). Heredaba la paleta de la barra grande —azul en
        // curso, verde hecho, rojo fallo— y son cuatro tonos en una pieza de dos centímetros que vive encima
        // de todo. El dueño, antes de mandársela a un usuario: «mucho negro y blanco, sin más colores».
        Prueba("242. el notch es blanco y negro: todo color que pinta tiene sus tres canales iguales, el estado se distingue por forma y por luz —el fallo es un aro y no un punto rojo, lo omitido baja de luz, lo que está en curso late— y la paleta de la barra grande, que sí tiene color, no entra aquí", ElNotchEsBlancoYNegro);

        // ESCRIBIR SE COMPROBABA SOLO: el campo de Instagram acepta ValuePattern, no guarda nada, y la
        // herramienta contestaba «escribí X y confirmé con Enter» con la caja vacía (2026-09-15, 18:48, dos
        // veces seguidas). Y la voz llegó a decir «ya quedó enviado». Aceptado no es ejecutado, otra vez.
        Prueba("243. escribir se comprueba en el campo: tras escribir por patrón se relee, y si el campo se quedó como estaba —el editor de Instagram, que acepta la orden y no guarda nada— se teclea de verdad y se vuelve a comprobar; lo que no se puede leer no se juzga, y si no cuajó por ninguna vía se dice, en vez de contestar «escribí»", EscribirSeCompruebaEnElCampo);
        Prueba("244. Ü decide en vez de preguntar: sus instrucciones mandan elegir la opción más razonable cuando falta un dato y decir cuál se eligió, dejan preguntar solo cuando elegir mal no se puede deshacer, y prohíben trocear una tarea larga en preguntas", UDecideEnVezDePreguntar);

        // LAS ESPERAS SE CONTABAN EN MILISEGUNDOS FICTICIOS (2026-09-15). Los bucles sumaban 120 por vuelta
        // y además pagaban el sondeo: con un «dónde estoy» de 2,8 s, una espera de «1,8 s» duraba más de
        // treinta. Medido: un map_take de 28,8 s para decir «lo conozco aquí pero AHORA no lo veo».
        Prueba("245. las esperas se acotan con el RELOJ y no contando vueltas: con un sondeo lento, una espera de N milisegundos termina en N y no en N por el número de vueltas — ni al pulsar, ni al comprobar la llegada, ni en la compuerta que espera a que un elemento esté vivo", LasEsperasSeMidenConElReloj);
        Prueba("246. lo que se acaba de mirar no se vuelve a mirar: una memoria corta con su caducidad devuelve lo recordado sin volver a la fuente mientras no caduque, vuelve a preguntar cuando caduca, y se puede olvidar a mano cuando algo cambió", LoQueSeAcabaDeMirarNoSeVuelveAMirar);

        // UN INFORME ENTERO SE ESCRIBIÓ CUATRO VECES (2026-09-16). La comprobación de la 243 daba falso
        // negativo con un campo que no cuenta lo que tiene (Google Docs) y con un texto largo cuyos saltos
        // el editor normaliza (el Bloc de notas): el veredicto era «no pude escribir», el modelo lo tomaba
        // al pie de la letra y reescribía. Dar por falso lo que no se pudo comprobar sale caro.
        Prueba("247. tras escribir hay tres respuestas y no dos: cuajó, no cuajó, o no se sabe porque el campo no cuenta lo que tiene; un campo mudo se da por escrito en vez de por fallido —que es lo que hizo que un informe se escribiera cuatro veces— y la comparación mira el texto normalizado y no su formato", TrasEscribirHayTresRespuestas);
        Prueba("248. un clic que no movió NADA se repite una vez, y solo cuando el terreno ya sabía que esa puerta lleva a algún sitio: un botón que hace su trabajo sin cambiar de pantalla —«Guardar»— no tiene destino aprendido y por eso jamás recibe un segundo clic, que es justo lo que promete la 83; tampoco se repite lo que no se puede deshacer, ni se repite dos veces", ElClicQueNoMovioNadaSeRepiteUnaVez);
        Console.WriteLine();
        Console.WriteLine(_fallos == 0
            ? "CONTRATO INTACTO: el grafo se comporta como el día que se congeló."
            : $"CONTRATO ROTO: {_fallos} promesa(s) incumplida(s). El cambio no puede entrar así.");
        return _fallos;
    }

    // ── Las promesas ─────────────────────────────────────────────────────────

    private static void ElNotchSeApoyaDondeLaBarraDejaSitio()
    {
        var t = Capacidad("U.WindowsClient.Ui.ReglaDeLaBandeja");
        var sitio = t?.GetMethod("Sitio");
        var lado = t?.GetMethod("Lado");
        var aire = t?.GetField("Aire");
        Debe(t != null && sitio != null && lado != null && aire != null,
            "todavía no existe «ReglaDeLaBandeja». La promesa está escrita y en rojo, que es donde "
            + "tiene que estar");
        if (t == null || sitio == null || lado == null || aire == null) return;

        var tLado = Capacidad("U.WindowsClient.Ui.LadoDeLaBandeja")!;
        var tIconos = Capacidad("U.WindowsClient.Ui.IconosDeLaBandeja")!;
        object L(string n) => Enum.Parse(tLado, n);
        object I(string n) => Enum.Parse(tIconos, n);

        System.Windows.Rect Sitio(System.Windows.Rect libre, string ladoN, string iconosN,
                                  double ancho, double alto)
            => (System.Windows.Rect)sitio.Invoke(null, new object[]
               { libre, L(ladoN), I(iconosN), new System.Windows.Size(ancho, alto) })!;

        // Una pantalla de 1920×1080 con la barra de tareas abajo, de 48 px. El notch mide 360×90.
        var pantalla = new System.Windows.Rect(0, 0, 1920, 1080);
        var libre = new System.Windows.Rect(0, 0, 1920, 1032);
        double hueco = (double)aire.GetValue(null)!;

        // 1. POR DÓNDE ESTÁ LA BARRA, deducido del trozo que se reserva.
        Debe(lado.Invoke(null, new object[] { pantalla, libre })!.ToString() == "Abajo",
            "una barra que se come los 48 px de abajo está ABAJO: si esto se leyera mal, el notch "
            + "se apoyaría en el borde equivocado de la pantalla");
        Debe(lado.Invoke(null, new object[]
            { pantalla, new System.Windows.Rect(0, 48, 1920, 1032) })!.ToString() == "Arriba",
            "y una que se come los de arriba está arriba");
        Debe(lado.Invoke(null, new object[] { pantalla, pantalla })!.ToString() == "Abajo",
            "con la barra oculta automáticamente no se reserva nada y el área de trabajo ES la "
            + "pantalla: se contesta «abajo», que es donde está casi toda barra y donde el notch "
            + "queda bien igual — no se puede contestar «no sé» a una pregunta que hay que responder "
            + "para pintar algo");

        // 2. EL INTERCAMBIO, que es la promesa entera.
        var alCentro = Sitio(libre, "Abajo", "AlCentro", 360, 90);
        var aLaIzquierda = Sitio(libre, "Abajo", "ALaIzquierda", 360, 90);

        Debe(alCentro.Left < 40,
            $"con los iconos AL CENTRO —el Windows 11 de fábrica— el notch se va a la esquina "
            + $"izquierda, y se fue a x={alCentro.Left}. Es el caso que rompió lo de antes: una "
            + "posición fija en la esquina acertaba en el 10 y plantaba el panel encima del racimo "
            + "de iconos en el 11");
        Debe(Math.Abs(aLaIzquierda.Left - (1920 - 360) / 2) < 1,
            $"y con los iconos A LA IZQUIERDA se va al centro, que es la mitad que queda libre; se "
            + $"fue a x={aLaIzquierda.Left}");
        Debe(Math.Abs(alCentro.Left - aLaIzquierda.Left) > 400,
            "las dos posiciones tienen que ser DISTINTAS de verdad: una regla que contestara casi lo "
            + "mismo en los dos casos pasaría esta promesa sin hacer nada");

        // 3. SE APOYA EN LA BARRA, no flota a media pantalla ni la toca.
        Debe(Math.Abs(alCentro.Bottom - (libre.Bottom - hueco)) < 0.5,
            $"el notch se apoya en la barra de tareas con {hueco} px de junta, y su base quedó en "
            + $"y={alCentro.Bottom} con el área libre acabando en {libre.Bottom}");
        Debe(hueco > 0,
            "y NO pegado del todo: sin junta, el notch y la barra se leen como una sola pieza rota "
            + "—dos negros tocándose sin costura— en vez de como algo que flota encima");
        var colgando = Sitio(new System.Windows.Rect(0, 48, 1920, 1032), "Arriba", "AlCentro", 360, 90);
        Debe(Math.Abs(colgando.Top - (48 + hueco)) < 0.5,
            $"con la barra ARRIBA cuelga de ella en vez de irse al suelo, y se quedó en y={colgando.Top}");

        // 4. NUNCA FUERA DEL CRISTAL. Un notch ancho en una pantalla estrecha no puede acabar con
        //    media caja fuera: es el caso de un portátil pequeño con una frase larga dentro.
        var estrecha = new System.Windows.Rect(0, 0, 500, 700);
        var apretado = Sitio(estrecha, "Abajo", "AlCentro", 460, 120);
        Debe(apretado.Left >= estrecha.Left - 0.5 && apretado.Right <= estrecha.Right + 0.5,
            $"el notch se quedó en x={apretado.Left}..{apretado.Right} sobre una pantalla de "
            + $"{estrecha.Width}: lo que no se ve no informa de nada");
        Debe(apretado.Top >= estrecha.Top - 0.5 && apretado.Bottom <= estrecha.Bottom + 0.5,
            "y lo mismo por arriba y por abajo");

        // 5. CON LA BARRA EN VERTICAL no hay esquina libre que valga —los iconos bajan por el costado
        //    entero— y el sitio honesto es el centro de abajo.
        var conBarraLateral = new System.Windows.Rect(72, 0, 1848, 1080);
        var lateral = Sitio(conBarraLateral, "Izquierda", "AlCentro", 360, 90);
        Debe(Math.Abs(lateral.Left - (72 + (1848 - 360) / 2)) < 1,
            $"con la barra de tareas en vertical el notch se centra en lo que queda de pantalla, y "
            + $"se fue a x={lateral.Left}: irse a «la esquina» ahí es irse encima de la propia barra");
    }



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
        // EL ESCRITORIO DELANTE TAMPOCO ES «LA APP DELANTE» (cuarta prueba real, 2026-09-07 16:45):
        // la demo arrancó con el escritorio en primer plano, el detector eligió UIA, y la lección de
        // SAP salió con 35 eventos por pulsación y llegadas «uia://» que ningún juez podía casar.
        {
            var t4 = Capacidad("U.WindowsClient.Teach.ElArranqueDeLaDemo");
            var esEscritorio = t4?.GetMethod("EsEscritorio");
            var juzgar4 = t4?.GetMethod("Juzgar", new[] { typeof(bool), typeof(bool), typeof(int), typeof(int) });
            Debe(esEscritorio != null && juzgar4 != null, "el arranque sabe qué es el escritorio y lo trata como «todavía no hay app delante»");
            if (esEscritorio != null && juzgar4 != null)
            {
                Debe((bool)esEscritorio.Invoke(null, new object[] { "Progman" })! && (bool)esEscritorio.Invoke(null, new object[] { "WorkerW" })!
                     && (bool)esEscritorio.Invoke(null, new object[] { "Shell_TrayWnd" })! && !(bool)esEscritorio.Invoke(null, new object[] { "SAP_FRONTEND_SESSION" })!,
                    "Progman, WorkerW y la barra de tareas son el escritorio; una sesión de SAP no");
                var p = juzgar4.Invoke(null, new object[] { false, true, 1000, 60000 })!;
                Debe(!(bool)Prop(p, "Empezar")! && (bool)Prop(p, "Seguir")! && ((string)Prop(p, "Decir")!).Contains("escritorio"),
                    "con el escritorio delante no se graba: se sigue esperando y se dice que se ve el escritorio");
                var fin = juzgar4.Invoke(null, new object[] { false, true, 60000, 60000 })!;
                Debe(!(bool)Prop(fin, "Empezar")! && !(bool)Prop(fin, "Seguir")!, "y al techo se deja de esperar, como con Ü delante");
                var app = juzgar4.Invoke(null, new object[] { false, false, 1000, 60000 })!;
                Debe((bool)Prop(app, "Empezar")!, "con una app de verdad delante, se empieza");
            }
        }
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
        var juzgar = t?.GetMethod("Juzgar", new[] { typeof(bool), typeof(int), typeof(int) });
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
        // FUERA DE LA CONVERSACIÓN (2026-09-08, decimotercera prueba): con el triage en el contexto, a
        // «Di exactamente esto: Temperatura.» la voz contestó «Glasgow, entre 3 y 15», y a «Elijo al
        // paciente» le antepuso «Vale, déjame pensar un momento sobre cómo ayudarte…». Sin contexto no
        // hay nada que la tiente: solo la frase.
        Debe(conTexto.Contains("\"conversation\":\"none\""), $"lo dictado se pide fuera de la conversación: conversation=none ({Recorta(conTexto, 200)})");
        Debe(!sinNada.Contains("\"conversation\""), "y pedir turno de normal sigue dentro de la conversación, que es donde vive");
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

    // ── Spec 013, fases 2-5: la lección, el mensaje, las cajas, el juez y la skill ─────────

    private static object Nuevo(Type t, params object?[] args) => Activator.CreateInstance(t, args)!;
    private static System.Collections.IList ListaDe(Type t) =>
        (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(t))!;
    private static object? Prop(object o, string nombre) => o.GetType().GetProperty(nombre)!.GetValue(o);

    private static object Clic(Type t, long hora, int x, int y, string llegada = "", string sel = "", string etq = "", string tipo = "", bool deU = false, string pantalla = "")
        => Nuevo(t, hora, x, y, llegada, sel, etq, tipo, deU, pantalla);

    /// <summary>La lección de juguete: tipos por reflexión, 26 clics, 1 paso de SAP, frases.</summary>
    private sealed class TiposDeLaLeccion
    {
        public Type Clic = null!, Paso = null!, Frase = null!, Cuadro = null!, CuadroLeccion = null!, Evento = null!, Leccion = null!, Veredicto = null!;
        public static TiposDeLaLeccion? Cargar()
        {
            var t = new TiposDeLaLeccion
            {
                Clic = Capacidad("U.WindowsClient.Teach.ClicVisto")!,
                Paso = Capacidad("U.WindowsClient.Teach.PasoVisto")!,
                Frase = Capacidad("U.WindowsClient.Navigation.FraseDicha")!,
                Cuadro = Capacidad("U.WindowsClient.Teach.Cuadro")!,
                CuadroLeccion = Capacidad("U.WindowsClient.Teach.CuadroDeLaLeccion")!,
                Evento = Capacidad("U.WindowsClient.Teach.EventoDeLaLeccion")!,
                Leccion = Capacidad("U.WindowsClient.Teach.Leccion")!,
                Veredicto = Capacidad("U.WindowsClient.Piloto.VeredictoDeEvento")!,
            };
            return new[] { t.Clic, t.Paso, t.Frase, t.Cuadro, t.CuadroLeccion, t.Evento, t.Leccion, t.Veredicto }.Any(x => x == null) ? null : t;
        }
        public object Evento_(int n, long hora, string tipo, int x, int y, string selector, string texto, string llegada, string antes, string despues, string[] dicho, bool porTeclado = false, string etiqueta = "")
        {
            var d = new List<string>(dicho);
            return Nuevo(Evento, n, hora, tipo, x, y, selector, etiqueta, texto, "", llegada, antes, despues, true, (IReadOnlyList<string>)d, porTeclado);
        }
        public object Leccion_(string empezo, string termino, System.Collections.IList eventos, System.Collections.IList cuadros, System.Collections.IList? frases = null)
            => Nuevo(Leccion, "leccion-de-prueba", empezo, termino, 60_000L, "demo.mp4", eventos, frases ?? ListaDe(Frase), cuadros, "");
    }

    private static void CadaClicFisicoDejaUnEvento()
    {
        var tt = TiposDeLaLeccion.Cargar();
        var armar = Capacidad("U.WindowsClient.Teach.ArmarLaLeccion")?.GetMethod("Eventos");
        Debe(tt != null && armar != null,
            "todavía no existe «Teach.ArmarLaLeccion.Eventos» (fase 2 de la spec 013). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (tt == null || armar == null) return;

        // LO MEDIDO DOS VECES (spec 009): 26 clics en el árbol de SAP, 1 paso observado. Aquí los 26
        // clics tienen que salir como 26 eventos, con el único paso colgado del clic que le queda cerca.
        var clics = ListaDe(tt.Clic); var pasos = ListaDe(tt.Paso); var frases = ListaDe(tt.Frase); var cuadros = ListaDe(tt.Cuadro);
        for (int i = 0; i < 26; i++) clics.Add(Clic(tt.Clic, 1000L + i * 1500, 80 + i, 240 + i * 20, "", "", i == 3 ? "Urgencias Adultos/Triage" : ""));
        // Y dos clics sobre la propia ventana de Ü (parar la demo, abrir el panel): no son la tarea.
        clics.Add(Clic(tt.Clic, 41_500, 1793, 440, "", "", "", "", deU: true));
        clics.Add(Clic(tt.Clic, 41_900, 1800, 400, "", "", "", "", deU: true));
        pasos.Add(Nuevo(tt.Paso, 1000L + 19 * 1500 + 2500, "/app/con[0]/ses[0]/wnd[0]/usr/cntl/shell", "Triage", "GuiTree", "", "", "sapgui://QAS/NWP1"));
        // Cuadros cada 250 ms durante toda la demo, con huella que cambia tras cada clic.
        for (long t = 0; t < 41_000; t += 250) cuadros.Add(Nuevo(tt.Cuadro, t, $"c{t}.jpg", (ulong)(t / 1500)));

        var eventos = (System.Collections.IList)armar.Invoke(null, new object[] { clics, pasos, frases, cuadros })!;
        Debe(eventos.Count == 26, $"26 clics → 26 eventos, no {eventos.Count}: lo que Ü hizo con las manos deja rastro aunque SAP no emitiera paso — y los dos clics sobre la ventana de Ü NO cuentan");
        if (eventos.Count != 26) return;

        int conSelector = 0, conAntes = 0, conDespues = 0;
        foreach (var e in eventos)
        {
            if (((string)Prop(e, "Selector")!).Length > 0) conSelector++;
            if (((string)Prop(e, "CuadroAntes")!).Length > 0) conAntes++;
            if (((string)Prop(e, "CuadroDespues")!).Length > 0) conDespues++;
        }
        Debe(conSelector == 1, $"el único paso de SAP se cuelga de UN clic, no de {conSelector}");
        Debe((string)Prop(eventos[20]!, "Selector")! == "/app/con[0]/ses[0]/wnd[0]/usr/cntl/shell",
            "y se cuelga del último clic ANTERIOR (el 21, un segundo antes; el 20 queda a 2,5 s): el paso cuenta lo que pasó después de un clic");
        Debe((string)Prop(eventos[3]!, "Etiqueta")! == "Urgencias Adultos/Triage",
            "la identidad que el vigía resolvió al pulsar viaja en el evento: es la etiqueta con la que el terreno "
            + "nombra la puerta, y lo único que map_take entiende (primera prueba real: 7 clics, 0 con identidad)");
        Debe(conAntes == 26 && conDespues == 26, $"cada evento lleva sus dos cuadros (antes: {conAntes}, después: {conDespues})");
        Debe((int)Prop(eventos[4]!, "X")! == 84 && (int)Prop(eventos[4]!, "Y")! == 320 && (long)Prop(eventos[4]!, "HoraMs")! == 7000,
            "cada evento conserva su punto y su hora: es lo que permite dibujar el anillo donde se hizo clic");
        Debe(eventos.Cast<object>().Select((e, i) => (int)Prop(e, "N")! == i + 1).All(x => x),
            "los eventos van numerados 1..N en orden de tiempo: es como el piloto los va a declarar al comprobar");
    }

    private static void LoObservadoYLoDichoSeCuelganPorCercania()
    {
        var tt = TiposDeLaLeccion.Cargar();
        var armar = Capacidad("U.WindowsClient.Teach.ArmarLaLeccion")?.GetMethod("Eventos");
        Debe(tt != null && armar != null,
            "todavía no existe «Teach.ArmarLaLeccion.Eventos» (fase 2 de la spec 013). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (tt == null || armar == null) return;

        var clics = ListaDe(tt.Clic); var pasos = ListaDe(tt.Paso); var frases = ListaDe(tt.Frase); var cuadros = ListaDe(tt.Cuadro);
        clics.Add(Clic(tt.Clic, 2000L, 100, 100, "sapgui://A", "", "comando"));
        clics.Add(Clic(tt.Clic, 9000L, 200, 200, "sapgui://B", "", "Pto.tbjo.clínico"));
        // Lo MEDIDO en la primera prueba real: el input de SAP llega 3,07 s después del clic en el campo
        // (teclear «nwp1» y Enter), y el Enter se observa en el mismo instante. Los dos son ese clic.
        pasos.Add(Nuevo(tt.Paso, 5070L, "wnd[0]/tbar[0]/okcd", "", "GuiOkCodeField", "nwp1", "", "sapgui://S"));
        pasos.Add(Nuevo(tt.Paso, 5070L, "", "", "", "", "enter", "sapgui://S"));
        // Un Enter 30 s después de todo: nadie hizo clic cerca → por teclado, evento propio.
        pasos.Add(Nuevo(tt.Paso, 40_000L, "", "", "", "", "enter", "sapgui://S"));
        // Y lo que emite UIA al teclear: un paso por PULSACIÓN sobre el mismo campo, 50 s después de
        // todo clic (cuarta prueba real: 35 eventos y 70 cuadros por esto). Son UNO, el último.
        foreach (var (t, txt) in new[] { (100_000L, "n"), (100_020L, "nw"), (100_040L, "nwp"), (100_060L, "nwp1") }) // a 30 s de la frase suelta: fuera de la ventana del anclador
            pasos.Add(Nuevo(tt.Paso, t, "uia:aid=1001", "1001", "Edit", txt, "", "uia://S"));
        // Una frase a 300 ms del segundo clic, y otra lejos de todo.
        frases.Add(Nuevo(tt.Frase, "y aquí entramos a triage", 9300L));
        frases.Add(Nuevo(tt.Frase, "hola YouTube, hoy vamos a ver", 70_000L)); // a 30 s de todo: fuera de la ventana de 15 s del anclador (promesa 105)
        for (long t = 0; t < 45_000; t += 250) cuadros.Add(Nuevo(tt.Cuadro, t, $"c{t}.jpg", (ulong)(t / 3000)));

        var eventos = (System.Collections.IList)armar.Invoke(null, new object[] { clics, pasos, frases, cuadros })!;
        Debe(eventos.Count == 4, $"dos clics, un Enter suelto y CUATRO pulsaciones plegadas en una son CUATRO eventos (salieron {eventos.Count})");
        if (eventos.Count != 4) return;
        Debe((string)Prop(eventos[3]!, "Texto")! == "nwp1" && (string)Prop(eventos[3]!, "Selector")! == "uia:aid=1001",
            "las pulsaciones seguidas sobre el mismo campo son un solo evento con el texto completo");

        var e1 = eventos[0]!; var e2 = eventos[1]!; var e3 = eventos[2]!;
        Debe((string)Prop(e1, "Selector")! == "wnd[0]/tbar[0]/okcd" && (string)Prop(e1, "Texto")! == "nwp1" && (string)Prop(e1, "Tecla")! == "enter",
            "el input de SAP observado 3 s después del clic se cuelga de ESE clic —el último anterior—, con su selector, lo tecleado Y el Enter plegado (como la 132)");
        Debe((string)Prop(e1, "Etiqueta")! == "comando",
            "y conserva la etiqueta del vigía: la puerta por su nombre, que es lo que las manos entienden");
        Debe((string)Prop(e2, "Selector")! == "" && (string)Prop(e2, "Etiqueta")! == "Pto.tbjo.clínico",
            "el segundo clic no se lleva el paso del primero aunque esté dentro de la ventana: un paso cuelga del clic ANTERIOR, nunca del posterior");
        Debe((string)Prop(e1, "Llegada")! == "sapgui://A", "y la llegada es la que leyó el vigía tras el clic, no la superficie de antes");
        Debe((bool)Prop(e3, "PorTeclado")! && (string)Prop(e3, "Tecla")! == "enter" && (string)Prop(e3, "Tipo")! == "teclado",
            "un paso de SAP sin clic cerca es un evento propio, por teclado, con su tecla");
        var dicho2 = (IReadOnlyList<string>)Prop(e2, "Dicho")!;
        var dicho1 = (IReadOnlyList<string>)Prop(e1, "Dicho")!;
        Debe(dicho2.Count == 1 && dicho2[0] == "y aquí entramos a triage",
            "la frase dicha a 300 ms del segundo clic se cuelga del segundo clic");
        Debe(!dicho1.Contains("y aquí entramos a triage"), "…y de UN solo clic: no se repite en el primero");
        Debe(!dicho1.Contains("hola YouTube, hoy vamos a ver"),
            "lo dicho lejos de cualquier evento (fuera de la ventana de 15 s del anclador) no se le cuelga a ninguno: va al contexto");
        var contexto = Capacidad("U.WindowsClient.Teach.ArmarLaLeccion")!.GetMethod("Contexto")!;
        string ctx = (string)contexto.Invoke(null, new object[] { frases, eventos })!;
        Debe(ctx.Contains("hola YouTube"), $"y el contexto de la lección la conserva (salió «{ctx}»)");

        // LA PUERTA QUE SAP VIO MANDA (2026-09-08, undécima prueba). El vigía nombró el clic en el botón
        // «Triage» de la barra de la lista como la fila «GIRALDO»: el botón vive dentro del shell de la
        // rejilla y la geometría dio la rejilla. SAP publicó el paso correcto —botón de toolbar
        // ZMEDTRIAGE, «Triage»— y el evento quedó con el selector de SAP y el NOMBRE del vigía: una
        // puerta con dos identidades, y el plan se rompió al pedirla.
        var clics2 = ListaDe(tt.Clic); var pasos2 = ListaDe(tt.Paso);
        clics2.Add(Clic(tt.Clic, 1000L, 10, 10, "sapgui://L", "sap:grid#row=PATNNAME=GIRALDO", "GIRALDO"));
        clics2.Add(Clic(tt.Clic, 5000L, 20, 20, "sapgui://T", "sap:grid#row=PATNNAME=GIRALDO", "GIRALDO")); // el vigía adivinó la fila
        pasos2.Add(Nuevo(tt.Paso, 6000L, "sap:grid#tbbtn=ZMEDTRIAGE", "Triage", "button", "", "", "sapgui://L"));   // SAP vio el botón
        var ev2 = (System.Collections.IList)armar.Invoke(null, new object[] { clics2, pasos2, ListaDe(tt.Frase), cuadros })!;
        Debe(ev2.Count == 2 && (string)Prop(ev2[1]!, "Selector")! == "sap:grid#tbbtn=ZMEDTRIAGE" && (string)Prop(ev2[1]!, "Etiqueta")! == "Triage",
            $"cuando SAP trae OTRA puerta que la adivinada, el evento se queda con la de SAP entera: selector Y nombre (salió «{Prop(ev2[1]!, "Etiqueta")}»)");
        Debe((string)Prop(ev2[0]!, "Etiqueta")! == "GIRALDO", "y el clic anterior, sobre la fila, sigue siendo la fila");
    }

    private static void LaLeccionSeEntregaEnteraONada()
    {
        var t = Capacidad("U.WindowsClient.Teach.LaEntregaDeLaLeccion");
        var m = t?.GetMethod("Juzgar", new[] { typeof(bool), typeof(int), typeof(int) });
        Debe(t != null && m != null,
            "todavía no existe «Teach.LaEntregaDeLaLeccion.Juzgar» (fase 2 de la spec 013). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || m == null) return;

        (bool ok, string motivo) J(bool mp4, int cuadros, int eventos)
        {
            var r = m.Invoke(null, new object[] { mp4, cuadros, eventos })!;
            return ((bool)Prop(r, "Entregable")!, (string)Prop(r, "Motivo")!);
        }
        var sinMp4 = J(false, 120, 8);
        Debe(!sinMp4.ok && sinMp4.motivo.Contains("mp4"), $"sin mp4 no se entrega, y el motivo lo dice ({sinMp4.motivo})");
        var sinCuadros = J(true, 0, 8);
        Debe(!sinCuadros.ok && sinCuadros.motivo.Contains("cuadro"), $"sin cuadros no se entrega: el piloto describiría lo que imagina ({sinCuadros.motivo})");
        var sinEventos = J(true, 120, 0);
        Debe(!sinEventos.ok, "sin un solo clic ni paso no hay lección que entregar");
        var entera = J(true, 120, 8);
        Debe(entera.ok && entera.motivo.Contains("8") && entera.motivo.Contains("120"), $"entera, se entrega y se cuenta ({entera.motivo})");
        Debe(!string.IsNullOrWhiteSpace(sinMp4.motivo) && !string.IsNullOrWhiteSpace(sinCuadros.motivo), "el motivo nunca va vacío: una compuerta muda se aprende a saltar");

        var disco = Capacidad("U.WindowsClient.Teach.LeccionEnDisco");
        Debe(disco?.GetMethod("Guardar") != null && disco?.GetMethod("Cargar") != null && disco?.GetMethod("Ultima") != null,
            "y hay un solo sitio que escribe y lee la lección en disco (LeccionEnDisco.Guardar/Cargar/Ultima)");
    }

    private static void ElMensajeDelPilotoEsUnTutorialEnOrden()
    {
        var tt = TiposDeLaLeccion.Cargar();
        var t = Capacidad("U.WindowsClient.Teach.MensajeDeLaLeccion");
        var m = t?.GetMethod("Armar");
        var lado = t?.GetField("LadoMaximoPx");
        var anchoCamara = Capacidad("U.WindowsClient.Teach.CamaraDeCuadros")?.GetField("AnchoMaximo");
        Debe(tt != null && m != null && lado != null && anchoCamara != null,
            "todavía no existe «Teach.MensajeDeLaLeccion.Armar» (fase 3 de la spec 013). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (tt == null || m == null || lado == null || anchoCamara == null) return;

        Debe((int)lado.GetValue(null)! == 2000 && (int)anchoCamara.GetValue(null)! <= 2000,
            "el tope es 2000 px de lado (con más de 20 imágenes la API rechaza la petición entera si una pasa) y la cámara ya graba por debajo");

        var cuadros = ListaDe(tt.CuadroLeccion);
        cuadros.Add(Nuevo(tt.CuadroLeccion, 800L, "a1.jpg", 10, 20, 1280, 720));
        cuadros.Add(Nuevo(tt.CuadroLeccion, 1900L, "d1.jpg", 10, 20, 1280, 720));
        cuadros.Add(Nuevo(tt.CuadroLeccion, 4800L, "a2.jpg", 30, 40, 1280, 720));
        cuadros.Add(Nuevo(tt.CuadroLeccion, 5900L, "d2.jpg", 30, 40, 1280, 720));
        cuadros.Add(Nuevo(tt.CuadroLeccion, 8800L, "a3.jpg", 50, 60, 4000, 2250)); // demasiado grande
        cuadros.Add(Nuevo(tt.CuadroLeccion, 9900L, "d3.jpg", 50, 60, 1280, 720));
        var eventos = ListaDe(tt.Evento);
        eventos.Add(tt.Evento_(1, 1000, "clic", 10, 20, "wnd[0]/usr/cntl/shell", "", "sapgui://B", "a1.jpg", "d1.jpg", new[] { "aquí entramos" }));
        eventos.Add(tt.Evento_(2, 5000, "clic", 30, 40, "", "", "sapgui://B", "a2.jpg", "d2.jpg", Array.Empty<string>()));
        eventos.Add(tt.Evento_(3, 9000, "clic", 50, 60, "", "", "sapgui://C", "a3.jpg", "d3.jpg", Array.Empty<string>()));
        var frases = ListaDe(tt.Frase); frases.Add(Nuevo(tt.Frase, "aquí entramos", 1200L));
        var leccion = tt.Leccion_("sapgui://A", "sapgui://C", eventos, cuadros, frases);

        var bloques = ((System.Collections.IEnumerable)m.Invoke(null, new object[] { leccion, 60 })!).Cast<object>().ToList();
        string Tipo(object b) => (string)Prop(b, "Tipo")!; string Texto(object b) => (string)Prop(b, "Texto")!; string Ruta(object b) => (string)Prop(b, "Ruta")!;
        Debe(bloques.Count > 0 && Tipo(bloques[0]) == "text" && Texto(bloques[0]).Contains("sapgui://A") && Texto(bloques[0]).Contains("sapgui://C"),
            "el mensaje abre diciendo de dónde a dónde va la lección");
        int i1 = bloques.FindIndex(b => Tipo(b) == "text" && Texto(b).StartsWith("t=00:01"));
        Debe(i1 > 0, "cada evento lleva delante su etiqueta con la hora t=MM:SS");
        if (i1 > 0)
        {
            string et = Texto(bloques[i1]);
            Debe(et.Contains("clic 1 en (10,20)") && et.Contains("wnd[0]/usr/cntl/shell") && et.Contains("«aquí entramos»") && et.Contains("sapgui://B"),
                $"…y la etiqueta dice el clic, el punto, el selector, lo dicho y la llegada ({et})");
            Debe(i1 + 2 < bloques.Count && Tipo(bloques[i1 + 1]) == "image" && Ruta(bloques[i1 + 1]) == "a1.jpg"
                 && Tipo(bloques[i1 + 2]) == "image" && Ruta(bloques[i1 + 2]) == "d1.jpg",
                "detrás de la etiqueta van sus dos cuadros, antes y después, en ese orden");
        }
        var imagenes = bloques.Where(b => Tipo(b) == "image").Select(Ruta).ToList();
        Debe(!imagenes.Contains("a3.jpg") && imagenes.Contains("d3.jpg"),
            "un cuadro de 4000 px no viaja: la petición entera se rechazaría; el resto del evento sí viaja");
        int iT = bloques.FindIndex(b => Tipo(b) == "text" && Texto(b).Contains("TRANSCRIPCIÓN"));
        Debe(iT == bloques.Count - 1 && Texto(bloques[iT]).Contains("[00:01] aquí entramos"),
            "la transcripción completa con hora cierra el mensaje");

        // EL PRESUPUESTO: con sitio para 3 cuadros de 5, se van primero los de después que repiten
        // pantalla (d2 llega a la misma «sapgui://B» que d1) y NUNCA el de antes con selector (a1).
        var recortado = ((System.Collections.IEnumerable)m.Invoke(null, new object[] { leccion, 3 })!).Cast<object>().Where(b => Tipo(b) == "image").Select(Ruta).ToList();
        Debe(recortado.Count <= 3, $"con presupuesto 3 viajan como mucho 3 cuadros (viajaron {recortado.Count})");
        Debe(recortado.Contains("a1.jpg"), "el de antes del clic con selector NUNCA se recorta: es el que enseña qué se tocó");
        Debe(!recortado.Contains("d2.jpg"), "y el primero en irse es el de después que repite una pantalla ya mostrada");
    }

    private static void ElPilotoTieneUnaCajaConManos()
    {
        // UNA SOLA CAJA, y no dos. La primera versión separaba «entender sin manos» de «hacer», por
        // analogía con el aprendiz de la 138. En la primera corrida real (2026-09-06) el piloto abrió
        // SAP mientras «entendía», y el dueño lo vio: «abrir SAP no fue acertado? yo creo que sí».
        // Lo era, y por un hecho del código: un recuerdo se cuelga de (pantalla, selector), así que
        // entender sin poder ir a la pantalla no puede colgar ni un recuerdo bien puesto.
        var t = Capacidad("U.WindowsClient.Piloto.CajasDelPiloto");
        var caja = t?.GetMethod("Caja"); var prohibidas = t?.GetMethod("Prohibidas");
        Debe(t != null && caja != null && prohibidas != null,
            "todavía no existe «Piloto.CajasDelPiloto.Caja/Prohibidas» (fase 3 de la spec 013). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || caja == null || prohibidas == null) return;

        var todas = new[] { "map_where_am_i", "map_go_to", "map_take", "map_type", "map_unblock", "map_open_app", "map_what_i_see",
            "map_pointing_at", "map_show", "map_shot", "map_scroll", "map_esto_es", "map_recuerdos", "map_batch", "map_ahead",
            "map_skills", "map_skill_run", "file_open", "voz_decir", "voz_preguntar", "leccion_llegue", "leccion_guardar_skill" };
        var C = ((IEnumerable<string>)caja.Invoke(null, new object[] { todas })!).ToList();
        var P = ((IEnumerable<string>)prohibidas.Invoke(null, new object[] { todas })!).ToList();
        bool Tiene(List<string> l, string n) => l.Any(x => x.EndsWith("__" + n));

        foreach (var mano in new[] { "map_take", "map_type", "map_go_to", "map_scroll", "map_open_app", "map_unblock" })
            Debe(Tiene(C, mano), $"las manos están desde el principio: «{mano}» — abrir SAP para llegar a donde vive el elemento ES entender");
        foreach (var ojo in new[] { "map_esto_es", "map_recuerdos", "map_what_i_see", "map_where_am_i", "voz_decir", "voz_preguntar", "leccion_llegue", "leccion_guardar_skill" })
            Debe(Tiene(C, ojo), $"y los ojos, la voz y el juez también: «{ojo}»");
        Debe(Tiene(C, "ver_momento") && Tiene(C, "ver_alrededor"), "y mirar cualquier momento de la demo");
        foreach (var tanda in new[] { "map_batch", "map_skill_run" })
        {
            Debe(!Tiene(C, tanda), $"lo que va en tanda no se ofrece: «{tanda}» — comprobar es de uno en uno con juez en medio");
            // OFRECER MENOS NO ES PROHIBIR: el Agent SDK busca herramientas por su cuenta y lo que no
            // está en la lista se encuentra igual (medido el 2026-09-06: llamó lo que no se le ofreció).
            Debe(Tiene(P, tanda), $"…y además se PROHÍBE por su nombre: «{tanda}»");
        }
        Debe(P.Count == 2, $"y no se prohíbe nada más: {P.Count} prohibida(s), tenían que ser 2");
        Debe(C.All(x => x.StartsWith("mcp__")) && P.All(x => x.StartsWith("mcp__")), "los nombres van como los ve el Agent SDK: mcp__<servidor>__<herramienta>");
    }

    private static void LaAppJuzgaCadaLlegada()
    {
        var tt = TiposDeLaLeccion.Cargar();
        var t = Capacidad("U.WindowsClient.Piloto.RegistroDeLaComprobacion");
        Debe(tt != null && t != null && t.GetMethod("Llegue") != null && t.GetMethod("Final") != null && t.GetMethod("EventosQueNavegan") != null,
            "todavía no existe «Piloto.RegistroDeLaComprobacion» (fase 4 de la spec 013). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (tt == null || t == null) return;

        // Cuatro eventos: el 1 y el 3 navegan; el 2 se queda en la misma pantalla; el 4 no tiene llegada.
        var eventos = ListaDe(tt.Evento);
        eventos.Add(tt.Evento_(1, 1000, "clic", 1, 1, "s1", "", "sapgui://B", "", "", Array.Empty<string>()));
        eventos.Add(tt.Evento_(2, 2000, "clic", 2, 2, "s2", "", "sapgui://B", "", "", Array.Empty<string>()));
        eventos.Add(tt.Evento_(3, 3000, "clic", 3, 3, "s3", "", "sapgui://C", "", "", Array.Empty<string>()));
        eventos.Add(tt.Evento_(4, 4000, "clic", 4, 4, "s4", "", "", "", "", Array.Empty<string>()));
        var leccion = tt.Leccion_("sapgui://A", "sapgui://C", eventos, ListaDe(tt.CuadroLeccion));

        var navegan = (System.Collections.IList)t.GetMethod("EventosQueNavegan")!.Invoke(null, new object[] { leccion })!;
        Debe(navegan.Count == 2, $"el TOTAL es el plan: los eventos que navegan son 2 (salieron {navegan.Count}); el que se queda en la misma pantalla y el sin llegada no cuentan");

        var registro = Nuevo(t, leccion);
        object Llegue(int n, string donde) => t.GetMethod("Llegue")!.Invoke(registro, new object[] { n, donde })!;
        object Final() => t.GetMethod("Final")!.Invoke(registro, null)!;

        var v1 = Llegue(1, "sapgui://X");
        Debe(!(bool)Prop(v1, "Aterrizo")! && ((string)Prop(v1, "Motivo")!).Contains("sapgui://B") && ((string)Prop(v1, "Motivo")!).Contains("sapgui://X"),
            "declarar «llegué» en otra pantalla NO aterriza, y el motivo nombra las dos pantallas: el veredicto lo da la app, no el modelo");
        var f0 = Final();
        Debe(!(bool)Prop(f0, "Comprobada")!, "con un evento fallido la comprobación NO certifica");

        Llegue(1, "sapgui://B");
        Llegue(2, "sapgui://B"); // el 2 no navega: aterriza, pero no cuenta en el plan
        int hechos = (int)t.GetProperty("Hechos")!.GetValue(registro)!;
        Debe(hechos == 1, $"UN solo denominador: con el 1 y el 2 aterrizados, hechos = 1 porque el 2 no está en el plan (salió {hechos}; la primera prueba real dijo «5/2»)");
        var f1 = Final();
        Debe(!(bool)Prop(f1, "Comprobada")! && ((string)Prop(f1, "Motivo")!).Contains("1 de 2"),
            $"1 de 2 aterrizados: sigue sin comprobar y lo dice con números ({Prop(f1, "Motivo")})");
        var v3 = Llegue(3, "sapgui://C");
        Debe((bool)Prop(v3, "Aterrizo")!, "el evento 3 aterriza cuando la pantalla de ahora ES la grabada");
        var f2 = Final();
        Debe((bool)Prop(f2, "Comprobada")!, $"con los 2 que navegan aterrizados, COMPROBADA ({Prop(f2, "Motivo")})");
        var v4 = Llegue(4, "sapgui://C");
        Debe(!(bool)Prop(v4, "Aterrizo")! && ((string)Prop(v4, "Motivo")!).Contains("no tiene llegada"),
            "un evento sin llegada grabada no se puede juzgar, y se dice en vez de darlo por bueno");
        Debe((bool)Prop(Final(), "Comprobada")!, "…y no resta: no estaba en el plan");

        // ── LO TECLEADO CUENTA, y se juzga leyendo el campo (enmienda del 2026-09-08) ──
        var cuentan = t.GetMethod("EventosQueCuentan");
        var mismo = t.GetMethod("LoMismoTecleado");
        Debe(cuentan != null && mismo != null,
            "todavía no existen «RegistroDeLaComprobacion.EventosQueCuentan» y «LoMismoTecleado» (spec 014, enmienda de la 175). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (cuentan == null || mismo == null) return;

        var ev2 = ListaDe(tt.Evento);
        ev2.Add(tt.Evento_(1, 1000, "clic", 1, 1, "s1", "", "sapgui://B", "", "", Array.Empty<string>()));
        ev2.Add(tt.Evento_(2, 2000, "clic", 2, 2, "s5", "", "", "", "", Array.Empty<string>(), etiqueta: "Peso"));      // el clic que abre el campo
        ev2.Add(tt.Evento_(3, 3000, "clic", 3, 3, "s5", "80", "", "", "", Array.Empty<string>(), etiqueta: "Peso"));    // tecleó 80…
        ev2.Add(tt.Evento_(4, 4000, "clic", 4, 4, "s6", "38", "", "", "", Array.Empty<string>(), etiqueta: "Temperatura"));
        ev2.Add(tt.Evento_(5, 5000, "clic", 5, 5, "s5", "82", "", "", "", Array.Empty<string>(), etiqueta: "Peso"));    // …y lo corrigió a 82
        var leccion2 = tt.Leccion_("sapgui://A", "sapgui://B", ev2, ListaDe(tt.CuadroLeccion));
        var losQueCuentan = ((System.Collections.IEnumerable)cuentan.Invoke(null, new object[] { leccion2 })!).Cast<object>().Select(e => (int)Prop(e, "N")!).ToList();
        Debe(losQueCuentan.SequenceEqual(new[] { 1, 4, 5 }),
            $"cuentan el que navega y UN evento por campo tecleado, el último: 1, 4 y 5 (salieron {string.Join(",", losQueCuentan)}); el clic que solo abre el campo no cuenta, y el 80 corregido a 82 tampoco");

        var valores = new Dictionary<string, string?> { ["s5"] = "82,000", ["s6"] = "37" };
        var registro2 = Nuevo(t, leccion2, (Func<string, string?>)(sel => valores.TryGetValue(sel, out var v) ? v : null));
        object Llegue2(int n, string donde) => t.GetMethod("Llegue")!.Invoke(registro2, new object[] { n, donde })!;
        Debe((int)t.GetProperty("Total")!.GetValue(registro2)! == 3, "el total son 3: uno que navega y dos campos");

        var vTemp = Llegue2(4, "sapgui://B");
        Debe(!(bool)Prop(vTemp, "Aterrizo")! && ((string)Prop(vTemp, "Motivo")!).Contains("38") && ((string)Prop(vTemp, "Motivo")!).Contains("37"),
            $"el campo dice 37 y la demo tecleó 38: NO está hecho, y el motivo nombra los dos valores ({Prop(vTemp, "Motivo")})");
        var vPeso = Llegue2(5, "sapgui://B");
        Debe((bool)Prop(vPeso, "Aterrizo")!,
            $"el campo dice «82,000» y la demo tecleó «82»: está hecho, porque SAP formatea al viajar y 82 es 82 ({Prop(vPeso, "Motivo")})");
        var vClic = Llegue2(2, "sapgui://B");
        Debe((int)Prop(vClic, "N")! == 5 && (bool)Prop(vClic, "Aterrizo")!,
            "un «llegué» sobre el clic que ABRIÓ el campo (evento 2) se juzga sobre el campo (evento 5), por identidad: el piloto no tiene por qué saber cuál de los dos numeros cuenta");
        valores["s6"] = "38";
        Llegue2(4, "sapgui://B");
        Debe(!(bool)Prop(t.GetMethod("Final")!.Invoke(registro2, null)!, "Comprobada")!, "sin el que navega, todavía no");
        Llegue2(1, "sapgui://B");
        var final2 = t.GetMethod("Final")!.Invoke(registro2, null)!;
        Debe((bool)Prop(final2, "Comprobada")! && ((string)Prop(final2, "Motivo")!).Contains("3"),
            $"con el que navega aterrizado y los dos campos leídos con su valor, COMPROBADA 3 de 3 ({Prop(final2, "Motivo")})");
        valores["s6"] = null;
        var vSinLeer = Llegue2(4, "sapgui://B");
        Debe(!(bool)Prop(vSinLeer, "Aterrizo")! && ((string)Prop(vSinLeer, "Motivo")!).Contains("leer"),
            "si el campo no se puede leer, NO se da por hecho y se dice que no se pudo leer: declarar no es leer");

        bool Mismo(string a, string b) => (bool)mismo.Invoke(null, new object[] { a, b })!;
        Debe(Mismo("80", "80,000") && Mismo("1.70", "1,70") && Mismo("ALTA", " alta") && Mismo("", ""), "lo mismo tecleado: número es número aunque SAP lo formatee, y el texto no distingue mayúsculas ni espacios");
        Debe(!Mismo("80", "81") && !Mismo("Normal", "Norma"), "…y lo que no es lo mismo, no lo es");

        // Y EL JUEZ DICE QUÉ FALTA, por su nombre, para que las manos vayan a eso.
        var pendientes = t.GetMethod("Pendientes");
        Debe(pendientes != null, "todavía no existe «RegistroDeLaComprobacion.Pendientes» (spec 014, promesa 175 extendida)");
        if (pendientes == null) return;
        var registro3 = Nuevo(t, leccion2, (Func<string, string?>)(sel => valores.TryGetValue(sel, out var v) ? v : null));
        var faltan = ((System.Collections.IEnumerable)pendientes.Invoke(registro3, null)!).Cast<object>().ToList();
        Debe(faltan.Count == 3, $"sin juzgar nada, faltan los 3 que cuentan (salieron {faltan.Count})");
        t.GetMethod("Llegue")!.Invoke(registro3, new object[] { 1, "sapgui://B" });
        faltan = ((System.Collections.IEnumerable)pendientes.Invoke(registro3, null)!).Cast<object>().ToList();
        int NDe(object f) => (int)f.GetType().GetField("Item1")!.GetValue(f)!;
        string QueDe(object f) => (string)f.GetType().GetField("Item2")!.GetValue(f)!;
        Debe(faltan.Count == 2 && faltan.All(f => NDe(f) != 1) && faltan.Any(f => QueDe(f) == "Temperatura"),
            "aterrizado el 1, faltan los dos campos, con su nombre");
    }

    private static void LaSkillNaceDeLoVerificado()
    {
        var tt = TiposDeLaLeccion.Cargar();
        var t = Capacidad("U.WindowsClient.Piloto.SkillDeLoVerificado");
        var m = t?.GetMethod("Empaquetar");
        Debe(tt != null && m != null,
            "todavía no existe «Piloto.SkillDeLoVerificado.Empaquetar» (fase 5 de la spec 013). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (tt == null || m == null) return;

        var eventos = ListaDe(tt.Evento);
        eventos.Add(tt.Evento_(1, 1000, "clic", 1, 1, "wnd[0]/tbar[0]/okcd", "nwp1", "sapgui://B", "", "", new[] { "escribimos nwp1" }));
        eventos.Add(tt.Evento_(2, 2000, "clic", 2, 2, "wnd[0]/usr/cntl/shell", "", "sapgui://C", "", "", Array.Empty<string>()));
        eventos.Add(tt.Evento_(3, 3000, "clic", 3, 3, "wnd[0]/usr/btn", "", "sapgui://D", "", "", Array.Empty<string>()));
        // Y uno con PUERTA y sin selector, como los clics de árbol que resuelve el vigía por nombre.
        eventos.Add(tt.Evento_(4, 4000, "clic", 4, 4, "", "", "sapgui://E", "", "", Array.Empty<string>(), etiqueta: "Urgencias Adultos/Triage"));
        var leccion = tt.Leccion_("sapgui://A", "sapgui://E", eventos, ListaDe(tt.CuadroLeccion));

        var veredictos = ListaDe(tt.Veredicto);
        veredictos.Add(Nuevo(tt.Veredicto, 1, true, "sapgui://B", "sapgui://B", "aterrizó"));
        veredictos.Add(Nuevo(tt.Veredicto, 2, true, "sapgui://C", "sapgui://C-real", "aterrizó")); // la real difiere de la grabada
        veredictos.Add(Nuevo(tt.Veredicto, 3, false, "sapgui://D", "sapgui://C-real", "había que llegar a D"));
        veredictos.Add(Nuevo(tt.Veredicto, 4, true, "sapgui://E", "sapgui://E", "aterrizó"));

        var skill = m.Invoke(null, new object[] { leccion, veredictos, "Abrir triage", "cuando haya que abrir el triage" });
        Debe(skill != null, "con pasos verificados hay skill");
        if (skill == null) return;
        var pasos = (System.Collections.IList)Prop(skill, "Pasos")!;
        Debe(pasos.Count == 3, $"solo entran los pasos que ATERRIZARON: 3 de 4 (salieron {pasos.Count}); el que no aterrizó no entra ni marcado");
        if (pasos.Count == 3)
        {
            Debe((string)Prop(pasos[2]!, "Exit")! == "Urgencias Adultos/Triage",
                "la identidad de un paso es la PUERTA por su nombre cuando la hay: es lo que map_take y el batch entienden "
                + "(segunda prueba real: un evento aterrizó con puerta y sin selector, y la skill no se guardó)");
            Debe((string)Prop(pasos[0]!, "Exit")! == "wnd[0]/tbar[0]/okcd" && (string)Prop(pasos[0]!, "Texto")! == "nwp1" && (string)Prop(pasos[0]!, "Llegada")! == "sapgui://B",
                "el paso lleva el selector, lo tecleado y la llegada");
            Debe((string)Prop(pasos[1]!, "Llegada")! == "sapgui://C-real",
                "y la llegada es la REAL medida al comprobar, no la grabada en la demo");
        }
        Debe(!(bool)Prop(skill, "Comprobada")!, "como no todos los que navegan aterrizaron, la skill queda SIN comprobar");
        Debe((string)Prop(skill, "Nombre")! == "Abrir triage" && (string)Prop(skill, "DondeEmpieza")! == "sapgui://A",
            "con el nombre que le puso el piloto y el punto de partida de la lección");

        var ninguno = ListaDe(tt.Veredicto);
        Debe(m.Invoke(null, new object[] { leccion, ninguno, "Abrir triage", "" }) == null,
            "sin nada verificado no hay skill: null, no una skill vacía que parezca funcionar");
        var todos = ListaDe(tt.Veredicto);
        todos.Add(Nuevo(tt.Veredicto, 1, true, "sapgui://B", "sapgui://B", "")); todos.Add(Nuevo(tt.Veredicto, 2, true, "sapgui://C", "sapgui://C", "")); todos.Add(Nuevo(tt.Veredicto, 3, true, "sapgui://D", "sapgui://D", "")); todos.Add(Nuevo(tt.Veredicto, 4, true, "sapgui://E", "sapgui://E", ""));
        var completa = m.Invoke(null, new object[] { leccion, todos, "Abrir triage", "" });
        Debe(completa != null && (bool)Prop(completa, "Comprobada")! && (string)Prop(completa, "DondeTermina")! == "sapgui://E",
            "con todos aterrizados, la skill sale COMPROBADA y termina donde terminó de verdad");
    }

    private static void LaLlegadaLaDiceElTerreno()
    {
        // TRES SOLUCIONES A LO MISMO había en el repo (2026-09-07): el terreno vivo aprende aristas
        // desde agosto, el grabador de pasos sella la superficie del paso siguiente, y la lección
        // calculaba la suya —con reloj, luego con «la pantalla del clic siguiente»— y la grabó mal
        // dos veces mientras el terreno la aprendía bien en la misma demo. El dueño: «una solución
        // sólida estándar en vez de múltiples soluciones a lo mismo». La estándar es el terreno.
        var tt = TiposDeLaLeccion.Cargar();
        var m = Capacidad("U.WindowsClient.Teach.ArmarLaLeccion")?.GetMethod("Llegadas");
        Debe(tt != null && m != null,
            "todavía no existe «Teach.ArmarLaLeccion.Llegadas(clics, dondeTermino, terreno)» (spec 013, promesa 178). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (tt == null || m == null) return;

        object ClicEn(long hora, string pantalla, string sel = "", string etq = "", bool deU = false) => Nuevo(tt.Clic, hora, 10, 10, "", sel, etq, "", deU, pantalla);
        var clics = ListaDe(tt.Clic);
        clics.Add(ClicEn(1000, "sapgui://S", "shell#node=F00002", "Favoritos/IS-H: Pto.tbjo.clínico")); // 1: primer toque del favorito, no navega
        clics.Add(ClicEn(5000, "sapgui://S", "shell#node=F00002", "Favoritos/IS-H: Pto.tbjo.clínico")); // 2: el que navega a NWP1
        clics.Add(ClicEn(20000, "sapgui://NWP1/0100", "", "Urgencias Adultos/Triage"));               // 3: sin selector, con puerta
        clics.Add(ClicEn(25000, "uia://claude", deU: true));                                            // clic sobre Ü
        clics.Add(ClicEn(30000, "sapgui://NWP1/0100/ssub:Triage", "", "fila 1"));                       // 4: el terreno no aprendió nada

        // EL TERRENO DE JUGUETE: lo que MapaVivo habría aprendido en esa demo.
        Func<string, string, string, string> terreno = (desde, sel, etq) =>
            desde == "sapgui://S" && sel == "shell#node=F00002" ? "sapgui://NWP1/0100"
            : desde == "sapgui://NWP1/0100" && etq == "Urgencias Adultos/Triage" ? "sapgui://NWP1/0100/ssub:Triage"
            : "";
        var salida = ((System.Collections.IEnumerable)m.Invoke(null, new object[] { clics, "sapgui://NWP1/0100/ssub:Triage/paciente", terreno })!).Cast<object>().ToList();
        string L(int i) => (string)Prop(salida[i], "Llegada")!;

        Debe(L(1) == "sapgui://NWP1/0100", $"la llegada del clic al favorito es la arista del terreno, por selector (salió «{L(1)}»)");
        Debe(L(2) == "sapgui://NWP1/0100/ssub:Triage", $"y la del nodo Triage, por el nombre de la puerta cuando el vigía no trajo selector (salió «{L(2)}»)");
        Debe(L(0) == "sapgui://NWP1/0100", "el primer toque del favorito recibe la MISMA arista: el terreno sabe a dónde lleva esa puerta, no cuántas veces se tocó");
        Debe(L(3) == "", "el clic sobre Ü no recibe llegada");
        Debe(L(4) == "sapgui://NWP1/0100/ssub:Triage/paciente", $"el último clic, del que el terreno no aprendió nada, tiene de respaldo donde acabó la demo: un hecho leído al parar (salió «{L(4)}»)");

        clics.Add(ClicEn(40000, "sapgui://X", "", "otra"));
        var conHueco = ((System.Collections.IEnumerable)m.Invoke(null, new object[] { clics, "sapgui://fin", terreno })!).Cast<object>().ToList();
        Debe((string)Prop(conHueco[4], "Llegada")! == "", "un clic de en medio del que el terreno no aprendió nada queda VACÍO: no se le inventa la pantalla del clic siguiente ni ninguna otra");
        var sinTerreno = ((System.Collections.IEnumerable)m.Invoke(null, new object?[] { clics, "sapgui://fin", null })!).Cast<object>().ToList();
        Debe((string)Prop(sinTerreno[1], "Llegada")! == "" && (string)Prop(sinTerreno[5], "Llegada")! == "sapgui://fin",
            "sin terreno a mano, todo queda vacío salvo el último: la lección no calcula llegadas por su cuenta");

        // SAP VISTO POR UIA NO ES UNA LLEGADA (2026-09-08, décima prueba). Tras pulsar «Triage», SAP
        // tardó y el mapa vivo se quedó ciego; al volver, identificó la ventana de SAP por su título
        // —«uia://saplogon.exe/reg-triage-crear-…»— y le colgó esa «llegada» al último clic, que
        // era Apertura Ocular. El evento contó como navegante y no podía aterrizar jamás: la app
        // estaba en «sapgui://…/0001», que es la MISMA ventana vista por el ojo correcto.
        var conOjoEquivocado = ListaDe(tt.Clic);
        conOjoEquivocado.Add(ClicEn(1000, "sapgui://QAS/NWP1/0001", "sap:…/cmbY0000000-ZCMBAPOCU", "Apertura Ocular"));
        conOjoEquivocado.Add(ClicEn(2000, "uia://explorer", "uia:aid=x", "SAP Logon"));
        Func<string, string, string, string> terrenoCiego = (desde, sel, etq) =>
            etq == "Apertura Ocular" ? "uia://saplogon.exe/reg-triage-crear-h-giraldo-status-cd"
            : etq == "SAP Logon" ? "uia://saplogon.exe/sap-logon-770" : "";
        var ciego = ((System.Collections.IEnumerable)m.Invoke(null, new object[] { conOjoEquivocado, "sapgui://fin", terrenoCiego })!).Cast<object>().ToList();
        Debe((string)Prop(ciego[0], "Llegada")! == "",
            "un clic dado DENTRO de SAP cuya «llegada» es SAP visto por UIA no tiene llegada: es la misma ventana con el ojo equivocado, y queda vacía");
        Debe((string)Prop(ciego[1], "Llegada")! == "uia://saplogon.exe/sap-logon-770",
            "pero un clic dado FUERA de SAP que acaba en una ventana de saplogon (el SAP Logon, que es UIA de verdad) sí llega ahí: la valla es solo para el ojo equivocado sobre la misma sesión");

        // LA PUERTA CON LA QUE SE PREGUNTA (2026-09-08, decimotercera prueba). El terreno SÍ había
        // aprendido que el botón «Triage» lleva al formulario (promesa 189), y la lección salió con esa
        // llegada VACÍA: las llegadas se buscaban con la identidad del vigía —la fila «GIRALDO», que
        // adivinó por geometría— antes de que SAP dijera que era el botón. Contaba entonces el último
        // clic sin identidad, que nadie puede dar: «19 de 20» con todo hecho.
        var conSap = Capacidad("U.WindowsClient.Teach.ArmarLaLeccion")?.GetMethod("ConLaIdentidadDeSap");
        Debe(conSap != null, "todavía no existe «ArmarLaLeccion.ConLaIdentidadDeSap» (spec 014, promesa 178 extendida)");
        if (conSap == null) return;
        var adivinados = ListaDe(tt.Clic); var vistosPorSap = ListaDe(tt.Paso);
        adivinados.Add(ClicEn(1000, "sapgui://lista", "sap:grid#row=PATNNAME=GIRALDO", "GIRALDO"));
        adivinados.Add(ClicEn(5000, "sapgui://lista", "sap:grid#row=PATNNAME=GIRALDO", "GIRALDO"));  // el vigía adivinó la fila
        vistosPorSap.Add(Nuevo(tt.Paso, 6000L, "sap:grid#tbbtn=ZMEDTRIAGE", "Triage", "button", "", "", "sapgui://lista"));
        var conLaDeSapCrudo = conSap.Invoke(null, new object[] { adivinados, vistosPorSap })!;
        var conLaDeSap = ((System.Collections.IEnumerable)conLaDeSapCrudo).Cast<object>().ToList();
        Debe((string)Prop(conLaDeSap[1], "Etiqueta")! == "Triage" && (string)Prop(conLaDeSap[1], "Selector")! == "sap:grid#tbbtn=ZMEDTRIAGE",
            "el clic que SAP vio como el botón «Triage» lleva esa identidad ANTES de buscar llegadas");
        Debe((string)Prop(conLaDeSap[0], "Etiqueta")! == "GIRALDO", "y el clic sobre la fila sigue siendo la fila");
        Func<string, string, string, string> terrenoQueSabe = (desde, sel, etq) => sel == "sap:grid#tbbtn=ZMEDTRIAGE" ? "sapgui://triage" : "";
        var llegadas = ((System.Collections.IEnumerable)m.Invoke(null, new object[] { conLaDeSapCrudo, "sapgui://triage", terrenoQueSabe })!).Cast<object>().ToList();
        Debe((string)Prop(llegadas[1], "Llegada")! == "sapgui://triage", "…y con esa identidad el terreno contesta: la llegada del botón deja de estar vacía");
    }

    private static void LaVozDeLaComprobacionEsPrestada()
    {
        var t = Cap004("U.WindowsClient.Piloto.VozPrestada");
        var utensilios = t?.GetMethod("Utensilios");
        var instrucciones = t?.GetProperty("Instrucciones") ?? (MemberInfo?)t?.GetField("Instrucciones");
        Debe(t != null && utensilios != null && instrucciones != null,
            "todavía no existe «Piloto.VozPrestada» (spec 014, promesa 192). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || utensilios == null || instrucciones == null) return;

        var todas = (System.Collections.IEnumerable)Cap004("U.WindowsClient.Voice.ConversacionEnVivo")!
            .GetMethod("Herramientas", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null)!;
        var sinNada = ((System.Collections.IEnumerable)utensilios.Invoke(null, new object[] { todas })!).Cast<object>().ToList();
        Debe(sinNada.Count == 0, $"la voz prestada no tiene NI UNA herramienta: ni manos ni ojos, porque quien actúa y mira es el piloto (salieron {sinNada.Count})");

        string texto = instrucciones is PropertyInfo pi ? (string)pi.GetValue(null)! : (string)((FieldInfo)instrucciones).GetValue(null)!;
        Debe(texto.Contains("exactamente") && texto.Contains("calla", StringComparison.OrdinalIgnoreCase),
            "sus instrucciones son decir exactamente lo que se le pide y callar ante todo lo demás");
        Debe(!texto.Contains("propón", StringComparison.OrdinalIgnoreCase) || texto.Contains("no propongas", StringComparison.OrdinalIgnoreCase),
            "y no proponer: sugerir es justo lo que se oyó y no tocaba");

        // Y LA SESIÓN NO CREA RESPUESTAS POR SU CUENTA: la apertura lleva la perilla apagada.
        var tp = typeof(Voz.Realtime.ProtocoloOpenAI);
        var apertura = tp.GetMethods().FirstOrDefault(m => m.Name == "Apertura" && m.GetParameters().Length == 4);
        Debe(apertura != null && apertura.GetParameters()[3].ParameterType == typeof(bool),
            "«Apertura» todavía no acepta «soloCuandoSeLePide» (spec 014, promesa 192)");
        if (apertura == null) return;
        var proto = new Voz.Realtime.ProtocoloOpenAI();
        var lista = ListaDe(typeof(Voz.Realtime.Utensilio));
        string prestada = string.Join("\n", ((IEnumerable<string>)apertura.Invoke(proto, new object[] { "x", lista, "", true })!));
        string normal = string.Join("\n", ((IEnumerable<string>)apertura.Invoke(proto, new object[] { "x", lista, "", false })!));
        Debe(prestada.Contains("\"create_response\":false"), $"con la voz prestada, la sesión no crea respuestas sola: create_response=false en la apertura ({Recorta(prestada, 200)})");
        Debe(!normal.Contains("\"create_response\":false"), "y de normal sí: la conversación de siempre contesta cuando le hablan");
    }

    private static void DarUnPasoEsUnaCoreografia()
    {
        var t = Capacidad("U.WindowsClient.Piloto.ElPasoQueSeDa");
        var m = t?.GetMethod("Resolver");
        var esSelector = t?.GetMethod("EsSelector");
        Debe(t != null && m != null && esSelector != null,
            "todavía no existe «Piloto.ElPasoQueSeDa.Resolver» (spec 014, promesa 191). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || m == null || esSelector == null) return;

        (string Senalar, string Ejecutor) I(string exit, string etiqueta, string selector, string texto)
        {
            var r = m.Invoke(null, new object[] { exit, etiqueta, selector, texto })!;
            return ((string)Prop(r, "ParaSenalar")!, (string)Prop(r, "ParaElEjecutor")!);
        }
        const string fila = "sap:wnd[0]/usr/…/shell#row=DATUM=08.09.2026|ZEIT=00:51|FALNR=2394353|PATNNAME=GIRALDO";
        const string peso = "sap:wnd[0]/usr/…/txtY0000000-ZTXTPESO";
        Debe(I("GIRALDO", "GIRALDO", fila, "") == ("GIRALDO", "GIRALDO"), "con el nombre de la puerta, se señala y se da por el nombre");
        Debe(I(fila, "GIRALDO", fila, "") == ("GIRALDO", "GIRALDO"),
            "si el piloto trae el SELECTOR de la fila y el evento tiene puerta, se señala y se da por la PUERTA: el selector de una fila cambia con la lista, el nombre no (undécima prueba: «no lo conozco»)");
        Debe(I("Peso", "Peso", peso, "80") == ("Peso", peso), "un paso que teclea se señala por el nombre y se escribe por el SELECTOR de la lección");
        Debe(I(peso, "Peso", peso, "80") == ("Peso", peso), "…aunque el piloto traiga el selector: se señala por el nombre igual");
        Debe(I("", "Triage", "sap:…#tbbtn=ZMEDTRIAGE", "") == ("Triage", "Triage"), "sin exit del piloto, la puerta del evento");
        Debe(I("Triage", "", "", "") == ("Triage", "Triage"), "sin evento, lo que el piloto trajo");
        Debe(I(fila, "", "", "") == (fila, fila), "un selector sin evento que lo nombre se queda como selector: no se inventa un nombre");
        Debe((bool)esSelector.Invoke(null, new object[] { "sap:wnd[0]/usr/x" })! && (bool)esSelector.Invoke(null, new object[] { "uia:name=X;ct=Button" })!
             && !(bool)esSelector.Invoke(null, new object[] { "Presión Arterial" })!,
            "un selector se reconoce por su prefijo de mundo: sap: o uia:; un nombre no lo lleva");

        // Y la coreografía de la mano es la MISMA que la del plan: sin decir no se dice, sin recuerdo no hay tarjeta, y se actúa igual.
        var core = Capacidad("U.WindowsClient.Piloto.ElRecuerdoQueSeVe")!.GetMethod("Coreografia")!;
        var sinNada = ((System.Collections.IEnumerable)core.Invoke(null, new object[] { true, false, false })!).Cast<object>().Select(g => g.ToString()).ToList();
        Debe(sinNada.SequenceEqual(new[] { "Senalar", "Actuar", "Soltar" }), $"una mano sin decir ni recuerdo: señalar, actuar y soltar, nada más (salió {string.Join(",", sinNada)})");
        var conTodo = ((System.Collections.IEnumerable)core.Invoke(null, new object[] { true, true, true })!).Cast<object>().Select(g => g.ToString()).ToList();
        Debe(conTodo.IndexOf("Decir") < conTodo.IndexOf("Actuar") && conTodo.IndexOf("Mostrar") < conTodo.IndexOf("Actuar"),
            "y con decir y recuerdo, se dice y se muestra ANTES de actuar: «justo después de que lo narraba, sucedía»");
    }

    private static void ElGrabadorDeSapEnsenaAlTerreno()
    {
        var t = Capacidad("U.WindowsClient.Teach.ElCruceQueVioSap");
        var tPaso = Capacidad("U.WindowsClient.Teach.PasoQueVioSap");
        var tCambio = Capacidad("U.WindowsClient.Teach.CambioQueVioSap");
        var m = t?.GetMethod("Emparejar");
        Debe(t != null && tPaso != null && tCambio != null && m != null,
            "todavía no existe «Teach.ElCruceQueVioSap.Emparejar» (spec 014, promesa 189). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || tPaso == null || tCambio == null || m == null) return;

        object Paso(long hora, string superficie, string selector) => Nuevo(tPaso, hora, superficie, selector);
        object Cambio(long hora, string desde, string hasta) => Nuevo(tCambio, hora, desde, hasta);
        (string, string, string)? E(object? paso, object cambio)
        {
            var r = m.Invoke(null, new object?[] { paso, cambio });
            if (r == null) return null;
            return ((string)r.GetType().GetField("Item1")!.GetValue(r)!, (string)r.GetType().GetField("Item2")!.GetValue(r)!, (string)r.GetType().GetField("Item3")!.GetValue(r)!);
        }
        const string lista = "sapgui://QAS/NWP1/SAPLN_WP_FRAMEWORK/0100/ssub:vista:Urgencias", triage = "sapgui://QAS/NWP1/SAPLY000/0001";
        const string boton = "sap:wnd[0]/usr/ssub/cntlISH_VIEW_007/shellcont/shell#tbbtn=ZMEDTRIAGE";

        var visto = E(Paso(1000, lista, boton), Cambio(4000, lista, triage));
        Debe(visto == (lista, boton, triage), $"el botón Triage, publicado 3 s antes de que SAP anunciara la pantalla nueva, es la arista lista → triage (salió {visto})");
        Debe(E(Paso(1000, lista, boton), Cambio(30_000, lista, triage)) == null, "un paso de hace 29 s no explica este salto: la ventana es corta, como en el mapa vivo, porque una arista falsa es peor que ninguna");
        Debe(E(Paso(1000, "sapgui://otra", boton), Cambio(4000, lista, triage)) == null, "un paso publicado desde OTRA pantalla no explica un salto que sale de esta");
        Debe(E(Paso(1000, "", boton), Cambio(4000, lista, triage)) == (lista, boton, triage), "un paso sin pantalla anotada (SAP no contestó al publicarlo) se acepta: la pantalla de salida la pone el cambio");
        Debe(E(null, Cambio(4000, lista, triage)) == null, "sin paso publicado no hay a quién atribuirle el salto");
        Debe(E(Paso(1000, lista, boton), Cambio(4000, lista, lista)) == null, "quedarse en la misma pantalla no es cruzar");
        Debe(E(Paso(1000, lista, boton), Cambio(4000, lista, "uia://saplogon.exe/reg-triage-crear")) == null, "y SAP visto por UIA no es un destino: es el ojo equivocado");
        Debe(E(Paso(1000, lista, ""), Cambio(4000, lista, triage)) == null, "un paso sin selector no es una puerta que el terreno pueda conocer");
        Debe(E(Paso(5000, lista, boton), Cambio(4000, lista, triage)) == null, "un paso publicado DESPUÉS del cambio no lo causó");
    }

    private static void EnUnDesplegableSeFijaLaClave()
    {
        var tSap = AppDominio().FirstOrDefault(x => x.FullName == "U.Graph.Surfaces.SapGuiSurface");
        var tOpcion = AppDominio().FirstOrDefault(x => x.FullName == "U.Graph.FieldOption");
        var m = tSap?.GetMethod("ClaveDeLaOpcion");
        Debe(tSap != null && tOpcion != null && m != null,
            "todavía no existe «SapGuiSurface.ClaveDeLaOpcion» (spec 014, promesa 190). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (tSap == null || tOpcion == null || m == null) return;

        var opciones = ListaDe(tOpcion);
        void Opcion(string clave, string texto)
        {
            var o = Activator.CreateInstance(tOpcion)!;
            tOpcion.GetProperty("Value")!.SetValue(o, clave);
            tOpcion.GetProperty("Label")!.SetValue(o, texto);
            tOpcion.GetProperty("Text")!.SetValue(o, texto);
            opciones.Add(o);
        }
        Opcion("1", "Ninguna"); Opcion("2", "Al dolor"); Opcion("3", "A la voz"); Opcion("4", "Espontánea"); Opcion("9", "Ninguna");
        string C(string v) => (string)m.Invoke(null, new object?[] { opciones, v })!;
        Debe(C("4") == "4", "la clave misma, que es lo que trae la lección");
        Debe(C("Espontánea") == "4", "el texto que la persona lee, que es lo que el piloto teclea con las manos");
        Debe(C("espontanea ") == "4", "…sin acentos, mayúsculas ni espacios de más");
        Debe(C("A la voz") == "3", "un texto con espacios dentro se encuentra igual");
        Debe(C("Ninguna") == "", "dos opciones con el mismo texto no se adivinan: nadie");
        Debe(C("Orientado") == "", "un valor que no es ninguna opción no se inventa");
        Debe((string)m.Invoke(null, new object?[] { null, "4" })! == "", "sin opciones no hay clave, y no hay excepción");
    }

    private static void LoQueSeNombraEnSapSeResuelveEnUnSitio()
    {
        var t = Cap004("U.WindowsClient.Mcp.SurfaceMapTools");
        var tCampo = AppDominio().FirstOrDefault(x => x.FullName == "U.Graph.DetectedField");
        var m = t?.GetMethod("LoQueSeNombra");
        Debe(t != null && tCampo != null && m != null,
            "todavía no existe «SurfaceMapTools.LoQueSeNombra» (spec 014, promesa 188). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || tCampo == null || m == null) return;

        var terreno = new List<(string, string, string)>
        {
            ("sap:…#tbbtn=ZMEDTRIAGE", "Triage", "GuiGridBoton"),
            ("sap:wnd[0]/usr/txtY0000000-ZTXTPESO", "Peso", "GuiTextField"),   // el terreno ya lo conoce
        };
        var campos = ListaDe(tCampo);
        void Campo(string selector, string etiqueta)
        {
            var c = Activator.CreateInstance(tCampo)!;
            tCampo.GetProperty("Selector")!.SetValue(c, selector);
            tCampo.GetProperty("Label")!.SetValue(c, etiqueta);
            tCampo.GetProperty("ControlType")!.SetValue(c, "text");
            campos.Add(c);
        }
        Campo("sap:wnd[0]/usr/txtY0000000-ZTXTTASIS", "Presión Arterial");
        Campo("sap:wnd[0]/usr/txtY0000000-ZTXTPESO", "Peso");
        Campo("sap:wnd[0]/usr/cntlCT__ZTXTMTVCN/shellcont/shell", "Motivo de Consulta");
        Campo("sap:wnd[0]/usr/ctxtY0000000-ZFECHA1", "Fecha");
        Campo("sap:wnd[0]/usr/ctxtY0000000-ZFECHA2", "Fecha");

        object? Nombra(string que) => m.Invoke(null, new object[] { que, terreno, campos });
        string Sel(object? o) => o == null ? "" : (string)o.GetType().GetField("Item1")!.GetValue(o)!;
        string Tipo(object? o) => o == null ? "" : (string)o.GetType().GetField("Item3")!.GetValue(o)!;

        Debe(Sel(Nombra("Triage")) == "sap:…#tbbtn=ZMEDTRIAGE", "una puerta del terreno se encuentra como siempre");
        Debe(Sel(Nombra("Presión Arterial")) == "sap:wnd[0]/usr/txtY0000000-ZTXTTASIS",
            "un campo del dynpro que el terreno no conoce se encuentra por su etiqueta: es lo que el piloto ve y lo que rechazaba «no veo nada que se llame»");
        Debe(Sel(Nombra("Y0000000-ZTXTTASIS")) == "sap:wnd[0]/usr/txtY0000000-ZTXTTASIS", "y por su nombre técnico, con el mismo resolutor que para escribir");
        Debe(Sel(Nombra("Motivo de Consulta")).EndsWith("shellcont/shell"), "la caja de texto largo también");
        Debe(Tipo(Nombra("Peso")) == "GuiTextField", "cuando el terreno y el dynpro conocen el mismo campo, manda el terreno: lleva su tipo y su historia");
        Debe(Nombra("Fecha") == null, "dos campos que se llaman igual: nadie");
        Debe(Nombra("Glasgow total") == null, "lo que ningún sitio conoce no se inventa");
        Debe(Sel(m.Invoke(null, new object?[] { "Presión Arterial", null, campos })) == "sap:wnd[0]/usr/txtY0000000-ZTXTTASIS"
             && m.Invoke(null, new object?[] { "Presión Arterial", terreno, null }) == null,
            "sin terreno se busca en los campos; sin campos, no hay campo: null y no una excepción");
        Debe(Sel(Nombra("sap:…#tbbtn=ZMEDTRIAGE")) == "sap:…#tbbtn=ZMEDTRIAGE",
            "y una puerta del terreno se encuentra también por su selector exacto: el plan a veces trae el selector en vez del nombre");

        // Y EN LA LISTA, la etiqueta que se lee: el terreno conoce el campo por su nombre técnico y el
        // dynpro por lo que la persona lee; mismo selector, mismo campo, y gana lo que se lee.
        var conEtiqueta = t.GetMethod("ConLaEtiquetaQueSeLee");
        Debe(conEtiqueta != null, "todavía no existe «SurfaceMapTools.ConLaEtiquetaQueSeLee» (spec 014, promesa 188)");
        if (conEtiqueta == null) return;
        var tecnico = new List<(string, string, string)>
        {
            ("sap:wnd[0]/usr/txtY0000000-ZTXTTASIS", "Y0000000-ZTXTTASIS", "GuiTextField"),
            ("sap:…#tbbtn=ZMEDTRIAGE", "Triage", "GuiGridBoton"),
        };
        var leidos = new List<(string, string, string)>
        {
            ("sap:wnd[0]/usr/txtY0000000-ZTXTTASIS", "Presión Arterial", "text"),
            ("sap:wnd[0]/usr/txtY0000000-ZTXTPESO", "Peso", "text"),
        };
        var lista = ((System.Collections.IEnumerable)conEtiqueta.Invoke(null, new object[] { tecnico, leidos })!).Cast<object>().ToList();
        string E(object o) => (string)o.GetType().GetField("Item2")!.GetValue(o)!;
        string T(object o) => (string)o.GetType().GetField("Item3")!.GetValue(o)!;
        Debe(lista.Count == 3, $"dos del terreno y un campo que el terreno no conocía: 3 (salieron {lista.Count}), sin duplicar el que conocen los dos");
        Debe(lista.Any(o => E(o) == "Presión Arterial" && T(o) == "GuiTextField"),
            "el campo que los dos conocen sale con la etiqueta que se LEE y el tipo del terreno");
        Debe(!lista.Any(o => E(o) == "Y0000000-ZTXTTASIS"), "…y su nombre técnico ya no se lista: nadie lo llama así");
        Debe(lista.Any(o => E(o) == "Triage") && lista.Any(o => E(o) == "Peso"), "lo demás sigue: la puerta del terreno y el campo nuevo");
    }

    private static void ElComponenteBajoElPuntoSeDecidePorGeometria()
    {
        var tSap = AppDominio().FirstOrDefault(x => x.FullName == "U.Graph.Surfaces.SapGuiSurface");
        var tVisual = AppDominio().FirstOrDefault(x => x.FullName == "U.Graph.Surfaces.SapVisualElement");
        var tCampo = AppDominio().FirstOrDefault(x => x.FullName == "U.Graph.DetectedField");
        var elMasPequeno = tSap?.GetMethod("ElMasPequenoQueContiene");
        var elCampoDe = tSap?.GetMethod("ElCampoDeEsteId");
        Debe(tSap != null && tVisual != null && tCampo != null && elMasPequeno != null && elCampoDe != null,
            "todavía no existen «SapGuiSurface.ElMasPequenoQueContiene» y «ElCampoDeEsteId» (spec 014, promesa 186). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (tSap == null || tVisual == null || tCampo == null || elMasPequeno == null || elCampoDe == null) return;

        var vistos = ListaDe(tVisual);
        void Visual(string id, int left, int top, int w, int h, bool caja = true, bool nodo = false)
            => vistos.Add(Nuevo(tVisual, id, "GuiTextField", "", "", "", left, top, w, h, caja,
                "input", "text", nodo, null, null, 0, null, false, false));

        const string panel = "/app/con[0]/ses[0]/wnd[0]/usr/ssubSUB:SAPLY000:0002";
        const string peso = panel + "/txtY0000000-ZTXTPESO";
        const string caja = panel + "/cntlCT__ZTXTMTVCN/shellcont/shell";
        Visual(panel, 0, 0, 800, 600);                 // el contenedor también contiene el punto
        Visual(peso, 100, 100, 60, 20);
        Visual(caja, 100, 300, 300, 80);
        Visual("/app/con[0]/ses[0]/wnd[0]/usr/fila", 100, 100, 60, 20, nodo: true);      // un nodo no se elige
        Visual("/app/con[0]/ses[0]/wnd[0]/usr/sinCaja", 100, 100, 60, 20, caja: false);  // sin caja tampoco

        string Donde(int x, int y) => (string)elMasPequeno.Invoke(null, new object[] { vistos, x, y })!;
        Debe(Donde(130, 110) == peso,
            "el MÁS PEQUEÑO que contiene el punto, no el panel que lo contiene todo: un contenedor gana siempre por ser primero");
        Debe(Donde(700, 550) == panel, "donde solo cabe el contenedor, el contenedor");
        Debe(Donde(2000, 2000) == "", "fuera de todo no se devuelve nada: no se elige por descarte");
        Debe(Donde(105, 305) == caja, "una caja de texto largo se elige igual que un campo normal");

        var campos = ListaDe(tCampo);
        object Campo(string selector, string etiqueta)
        {
            var c = Activator.CreateInstance(tCampo)!;
            tCampo.GetProperty("Selector")!.SetValue(c, selector);
            tCampo.GetProperty("Label")!.SetValue(c, etiqueta);
            campos.Add(c);
            return c;
        }
        var elPeso = Campo("sap:wnd[0]/usr/ssubSUB:SAPLY000:0002/txtY0000000-ZTXTPESO", "Peso");
        var elMotivo = Campo("sap:wnd[0]/usr/ssubSUB:SAPLY000:0002/cntlCT__ZTXTMTVCN/shellcont/shell", "Motivo de Consulta");

        object? De(string id) => elCampoDe.Invoke(null, new object[] { campos, id });
        Debe(ReferenceEquals(De(peso), elPeso), "del id de un campo sale ese campo, con el prefijo absoluto quitado");
        Debe(ReferenceEquals(De(caja), elMotivo), "y del id de la caja de texto largo, la caja");
        Debe(ReferenceEquals(De(caja + "/hijo[2]"), elMotivo),
            "un punto que cae en un HIJO de la caja sube por la ruta hasta la caja: FindByPosition no es lo único que devuelve un hijo");
        Debe(De(panel) == null, "y el panel no es un campo: subir no puede acabar entregando el contenedor de todo");
    }

    private static void LoTecleadoSeDescargaAntesDeArmarLaLeccion()
    {
        var t = Capacidad("U.WindowsClient.Teach.ElCierreDeLaDemo");
        var m = t?.GetMethod("Orden");
        Debe(t != null && m != null,
            "todavía no existe «Teach.ElCierreDeLaDemo.Orden» (spec 014, promesa 187). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || m == null) return;

        List<string> Orden(bool hayVideo, bool haySuperficie)
        {
            var l = (System.Collections.IEnumerable)m.Invoke(null, new object[] { hayVideo, haySuperficie })!;
            return l.Cast<object>().Select(x => x.ToString()!).ToList();
        }

        var todo = Orden(true, true);
        Debe(todo.SequenceEqual(new[] { "DescargarLoPendiente", "SoltarLaSuperficie", "PararElVideo", "ArmarLaLeccion", "PararElGrabador" }),
            $"con video y superficie, los cinco en ese orden (salió «{string.Join(" → ", todo)}»)");
        if (todo.Count != 5) return;
        Debe(todo.IndexOf("DescargarLoPendiente") < todo.IndexOf("SoltarLaSuperficie"),
            "descargar va antes de SOLTAR la superficie: soltada, el oyente ya no está y lo descargado no llega a la lección");
        Debe(todo.IndexOf("DescargarLoPendiente") < todo.IndexOf("ArmarLaLeccion"),
            "y antes de ARMAR: al revés, la descarga funciona y la lección sale vacía igual — que es lo que pasó el 2026-09-07");
        Debe(todo.IndexOf("ArmarLaLeccion") < todo.IndexOf("PararElGrabador"),
            "y la lección se arma antes de parar al grabador, que es lo que ya hacía y no se rompe");

        var sinVideo = Orden(false, true);
        Debe(!sinVideo.Contains("PararElVideo") && sinVideo.IndexOf("DescargarLoPendiente") < sinVideo.IndexOf("ArmarLaLeccion"),
            "sin video no se para ningún video, y descargar sigue precediendo a armar");
        var sinSuperficie = Orden(true, false);
        Debe(!sinSuperficie.Contains("DescargarLoPendiente") && !sinSuperficie.Contains("SoltarLaSuperficie")
             && sinSuperficie.Contains("ArmarLaLeccion"),
            "sin superficie observada no hay nada que descargar ni que soltar, pero la lección se arma igual");
    }

    private static void LoTecleadoSeCuelgaDelCampoQueLoAbrio()
    {
        var tt = TiposDeLaLeccion.Cargar();
        var armar = Capacidad("U.WindowsClient.Teach.ArmarLaLeccion")?.GetMethod("Eventos");
        Debe(tt != null && armar != null, "no existe «Teach.ArmarLaLeccion.Eventos» (spec 013)");
        if (tt == null || armar == null) return;

        const string nombre = "sap:wnd[0]/usr/txtY0000000-ZTRNOMPAC", peso = "sap:wnd[0]/usr/txtY0000000-ZTXTPESO";
        var clics = ListaDe(tt.Clic); var pasos = ListaDe(tt.Paso); var frases = ListaDe(tt.Frase); var cuadros = ListaDe(tt.Cuadro);
        clics.Add(Clic(tt.Clic, 2000L, 100, 100, "sapgui://F", nombre, "Nombre del paciente"));
        clics.Add(Clic(tt.Clic, 4000L, 100, 140, "sapgui://F", peso, "Peso"));
        clics.Add(Clic(tt.Clic, 9000L, 300, 300, "sapgui://F", "", "Triage"));
        // Un tecleo DENTRO de la ventana de tiempo del clic de Peso, pero sobre el campo Nombre.
        pasos.Add(Nuevo(tt.Paso, 5000L, nombre, "Nombre del paciente", "GuiTextField", "GIRALDO", "", "sapgui://F"));
        // Y la descarga al parar: 30 s después de todo, los dos campos a la vez.
        pasos.Add(Nuevo(tt.Paso, 30_000L, peso, "Peso", "GuiTextField", "70", "", "sapgui://F"));
        pasos.Add(Nuevo(tt.Paso, 30_000L, "sap:wnd[0]/usr/txtY0000000-ZTXTTALLA", "Talla", "GuiTextField", "1.70", "", "sapgui://F"));
        for (long t = 0; t < 32_000; t += 250) cuadros.Add(Nuevo(tt.Cuadro, t, $"c{t}.jpg", (ulong)(t / 3000)));

        var eventos = (System.Collections.IList)armar.Invoke(null, new object[] { clics, pasos, frases, cuadros })!;
        Debe(eventos.Count == 4, $"tres clics y un tecleo sin clic que lo abriera son CUATRO eventos (salieron {eventos.Count})");
        if (eventos.Count != 4) return;
        var e1 = eventos[0]!; var e2 = eventos[1]!; var e3 = eventos[2]!; var e4 = eventos[3]!;
        Debe((string)Prop(e1, "Texto")! == "GIRALDO" && (string)Prop(e1, "Selector")! == nombre,
            "el tecleo sobre Nombre se cuelga del clic en Nombre, aunque por tiempo le tocara el clic en Peso");
        Debe((string)Prop(e2, "Texto")! == "70" && (string)Prop(e2, "Selector")! == peso,
            "el Peso descargado al parar —26 s después del clic— se cuelga del clic que abrió ese campo");
        Debe((string)Prop(e3, "Texto")! == "" && (string)Prop(e3, "Etiqueta")! == "Triage",
            "y el último clic NO se lleva lo de los demás campos");
        Debe((string)Prop(e4, "Tipo")! == "teclado" && (string)Prop(e4, "Texto")! == "1.70",
            "un tecleo sin clic con su identidad y lejos de todos sigue siendo un evento propio, por teclado, con su texto");
    }

    private static void UnCampoDeSapSeEncuentraPorSuNombre()
    {
        var tSap = AppDominio().FirstOrDefault(x => x.FullName == "U.Graph.Surfaces.SapGuiSurface");
        var tCampo = AppDominio().FirstOrDefault(x => x.FullName == "U.Graph.DetectedField");
        var m = tSap?.GetMethod("ElCampoQueSeLlama");
        Debe(tSap != null && tCampo != null && m != null,
            "todavía no existe «SapGuiSurface.ElCampoQueSeLlama» (spec 014, promesa 185). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (tSap == null || tCampo == null || m == null) return;

        var lista = ListaDe(tCampo);
        object Campo(string selector, string etiqueta)
        {
            var c = Activator.CreateInstance(tCampo)!;
            tCampo.GetProperty("Selector")!.SetValue(c, selector);
            tCampo.GetProperty("Label")!.SetValue(c, etiqueta);
            tCampo.GetProperty("ControlType")!.SetValue(c, "text");
            lista.Add(c);
            return c;
        }
        var nombre = Campo("sap:wnd[0]/usr/txtY0000000-ZTRNOMPAC", "Nombre del paciente");
        var peso = Campo("sap:wnd[0]/usr/txtY0000000-ZTXTPESO", "Peso");
        var motivo = Campo("sap:wnd[0]/usr/cntlCT__ZTXTMTVCN/shellcont/shell", "Motivo de Consulta");
        Campo("sap:wnd[0]/usr/ctxtY0000000-ZFECHA1", "Fecha");
        Campo("sap:wnd[0]/usr/ctxtY0000000-ZFECHA2", "Fecha");

        object? Busca(string que) => m.Invoke(null, new object[] { lista, que });
        Debe(ReferenceEquals(Busca("sap:wnd[0]/usr/txtY0000000-ZTXTPESO"), peso), "por su selector exacto");
        Debe(ReferenceEquals(Busca("Nombre del paciente"), nombre), "por su etiqueta, la que la persona ve");
        Debe(ReferenceEquals(Busca("nombre del paciente "), nombre), "…sin que importen mayúsculas ni un espacio de más");
        Debe(ReferenceEquals(Busca("Y0000000-ZTRNOMPAC"), nombre), "por el nombre técnico sin el prefijo del tipo: «txt» no es parte del nombre");
        Debe(ReferenceEquals(Busca("Motivo de Consulta"), motivo), "una caja de texto largo —un shell— se encuentra igual, por su etiqueta");
        Debe(Busca("Fecha") == null, "con dos campos que se llaman igual NO se adivina: nadie, y que lo diga");
        Debe(Busca("Presión arterial") == null, "lo que no está, no está");
    }

    private static void LaFilaDeAlvSeElige()
    {
        var t = typeof(PulsarSegunElNucleo).Assembly; // núcleo; SapGuiSurface vive en windows-graph
        var tSap = AppDominio().FirstOrDefault(x => x.FullName == "U.Graph.Surfaces.SapGuiSurface");
        var m = tSap?.GetMethod("FilaAElegir", new[] { typeof(string), typeof(int), typeof(int) });
        Debe(tSap != null && m != null,
            "todavía no existe «SapGuiSurface.FilaAElegir» (spec 014, promesa 182). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (tSap == null || m == null) return;

        int F(string sel, int cur, int rows) => (int)m.Invoke(null, new object[] { sel, cur, rows })!;
        Debe(F("2", 5, 10) == 2, "la SELECCIÓN manda sobre el cursor: al pulsar, SAP marca la fila del clic");
        Debe(F("0,2", 5, 10) == 0, "de una selección múltiple, la primera fila");
        Debe(F("", 5, 10) == 5, "sin selección, la fila del cursor");
        Debe(F("", -1, 1) == 0, "con UNA sola fila y nada marcado, es esa: el paciente premarcado que rompió la 6ª prueba");
        Debe(F("", -1, 8) == -1, "pero con varias filas y nada que las señale, NO se adivina: -1, y el clic no se nombra a lo loco");
    }

    private static void ElTerrenoCompletaLoQueUiaNoVe()
    {
        var t = Cap004("U.WindowsClient.Mcp.SurfaceMapTools");
        var m = t?.GetMethod("FundirPuertas");
        Debe(t != null && m != null,
            "todavía no existe «SurfaceMapTools.FundirPuertas» (spec 014, promesa 183). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || m == null) return;

        var uia = new[] { "Triage", "Buscar pacientes", "Atrás" };
        var terreno = new List<(string, string, string)>
        {
            ("sap:…#row=PATNNAME=GIRALDO", "GIRALDO", "GuiGridFila"),
            ("sap:…#tbbtn=ZMEDTRIAGE", "Triage", "GuiGridBoton"),   // UIA ya lo nombra: no se duplica
            ("sap:…#node=vw00576", "Urgencias Adultos/Triage", "GuiTreeFila"),
        };
        var fundido = ((System.Collections.IEnumerable)m.Invoke(null, new object[] { uia, terreno })!).Cast<object>().ToList();
        string Etq(object o) => (string)o.GetType().GetField("Item2")!.GetValue(o)!;
        var etqs = fundido.Select(Etq).ToList();

        Debe(etqs.Contains("GIRALDO"),
            "la fila del paciente, que UIA no puede ver, entra: es lo que el piloto necesita para pedirla por su nombre");
        Debe(etqs.Contains("Urgencias Adultos/Triage"), "y las filas de árbol también");
        Debe(!etqs.Contains("Triage"),
            "pero «Triage», que UIA ya nombró, NO se duplica: dos puertas con el mismo nombre confunden al que elige");
        Debe(fundido.Count == 2, $"solo lo que UIA no vio: 2 (salió {fundido.Count})");

        Debe(((System.Collections.IEnumerable)m.Invoke(null, new object?[] { uia, null })!).Cast<object>().Count() == 0,
            "sin puertas del terreno, no se añade nada: no se inventa");
    }

    /// <summary>Los ensamblados cargados, para alcanzar tipos de windows-graph desde el arnés.</summary>
    private static IEnumerable<Type> AppDominio()
    {
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] ts; try { ts = a.GetTypes(); } catch { continue; }
            foreach (var x in ts) yield return x;
        }
    }

    private static void UnaImagenViajaComoImagen()
    {
        var t = Cap004("U.WindowsClient.Mcp.ProtocoloMcp");
        var m = t?.GetMethod("ComoContenido");
        Debe(t != null && m != null,
            "todavía no existe «Mcp.ProtocoloMcp.ComoContenido» (spec 014, promesa 181). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || m == null) return;

        (string tipo, string datos, string mime, string texto) Bloque(string respuesta)
        {
            var arr = (object[])m.Invoke(null, new object[] { respuesta })!;
            if (arr.Length != 1) { Debe(false, "un bloque por respuesta"); return ("", "", "", ""); }
            var b = arr[0];
            string P(string n) => b.GetType().GetProperty(n)?.GetValue(b) as string ?? "";
            return (P("type"), P("data"), P("mimeType"), P("text"));
        }

        var foto = Bloque("data:image/png;base64,iVBORw0KGgo=");
        Debe(foto.tipo == "image" && foto.datos == "iVBORw0KGgo=" && foto.mime == "image/png",
            $"una foto viaja como bloque de imagen, con los datos SIN el prefijo del data URI y con su "
            + $"mimeType (salió type={foto.tipo}, data={foto.datos}, mime={foto.mime})");
        Debe(foto.texto.Length == 0,
            "y NO como texto: un base64 en prosa es exactamente lo que el modelo no puede mirar, y por "
            + "eso el piloto nunca miraba");

        var jpeg = Bloque("data:image/jpeg;base64,/9j/4AAQ");
        Debe(jpeg.tipo == "image" && jpeg.mime == "image/jpeg",
            "el mimeType sale del propio data URI, no se supone png");

        var prosa = Bloque("Estás en «sapgui://QAS/NWP1». Veo 3 salidas.");
        Debe(prosa.tipo == "text" && prosa.texto.StartsWith("Estás en"),
            "lo que no es una imagen sigue viajando como texto, tal cual, con su porqué dentro");

        // UN DATA URI ROTO VUELVE COMO TEXTO. Un bloque de imagen con datos que no son una imagen
        // rompe la petición ENTERA del modelo: perder la respuesta es peor que verla en prosa.
        foreach (var roto in new[] { "data:image/png;base64,", "data:image/png", "data:image/png;utf8,<svg/>" })
            Debe(Bloque(roto).tipo == "text",
                $"un data URI roto («{roto}») vuelve como texto, no como una imagen vacía que tumbe la petición");
    }

    private static void ElRecuerdoSeVeAntesDeTocar()
    {
        var t = Capacidad("U.WindowsClient.Piloto.ElRecuerdoQueSeVe");
        var coreo = t?.GetMethod("Coreografia"); var lectura = t?.GetMethod("TiempoDeLectura");
        Debe(t != null && coreo != null && lectura != null,
            "todavía no existe «Piloto.ElRecuerdoQueSeVe» (spec 014, promesa 180). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || coreo == null || lectura == null) return;

        List<string> C(bool elemento, bool recuerdo, bool decir) =>
            ((System.Collections.IEnumerable)coreo.Invoke(null, new object[] { elemento, recuerdo, decir })!).Cast<object>().Select(g => g.ToString()!).ToList();

        var completa = C(true, true, true);
        Debe(string.Join(">", completa) == "Senalar>Decir>Escribir>Mostrar>Esperar>Actuar>Cerrar>Soltar",
            $"con elemento, recuerdo y frase: señalar, decir, escribir, mostrar, esperar, TOCAR, cerrar, soltar — en ese orden (salió {string.Join(">", completa)})");
        Debe(completa.IndexOf("Mostrar") < completa.IndexOf("Actuar") && completa.IndexOf("Esperar") < completa.IndexOf("Actuar"),
            "la tarjeta se ve y se deja leer ANTES de tocar: tocar antes enseñaría el recuerdo de una pantalla que ya no está");
        Debe(completa.IndexOf("Cerrar") > completa.IndexOf("Actuar") && completa.IndexOf("Soltar") > completa.IndexOf("Actuar"),
            "y se recoge DESPUÉS de tocar: la tarjeta se cierra y la señal se suelta");

        var sinElemento = C(false, true, true);
        Debe(!sinElemento.Contains("Senalar") && !sinElemento.Contains("Mostrar") && !sinElemento.Contains("Esperar") && !sinElemento.Contains("Soltar"),
            $"sin elemento en pantalla no hay señal ni tarjeta sobre la nada (salió {string.Join(">", sinElemento)})");
        Debe(sinElemento.Contains("Escribir") && sinElemento.Contains("Actuar"),
            "…pero el recuerdo se escribe igual y el paso va igual al ejecutor, que lo juzgará por su cuenta");

        var sinRecuerdo = C(true, false, false);
        Debe(string.Join(">", sinRecuerdo) == "Senalar>Actuar>Soltar",
            $"un paso sin recuerdo ni frase solo se señala, se toca y se suelta (salió {string.Join(">", sinRecuerdo)})");

        int corto = (int)lectura.Invoke(null, new object[] { "un botón" })!;
        int largo = (int)lectura.Invoke(null, new object[] { new string('x', 500) })!;
        int medio = (int)lectura.Invoke(null, new object[] { new string('x', 60) })!;
        Debe(corto >= 900, $"un recuerdo de tres palabras se deja ver al menos 900 ms: no parpadea ({corto})");
        Debe(largo <= 4000, $"un párrafo no detiene la comprobación más de 4 s ({largo})");
        Debe(medio > corto && medio < largo, $"y entre medias, más texto es más tiempo ({corto} < {medio} < {largo})");
    }

    private static void ComprobarEsUnPlanQueLaAppRecorre()
    {
        // POR QUÉ UN PLAN (2026-09-07): 333 s y $4,06, luego 114 s y $1,75, con el modelo dando cada
        // paso. El batch hizo la misma ruta en 25 s. El valor del modelo está en interpretar; el «de
        // uno en uno» se conserva DENTRO del recorrido de la app (voz, recuerdo, paso, juez).
        var t = Capacidad("U.WindowsClient.Piloto.PlanDeComprobacion");
        var tPaso = Capacidad("U.WindowsClient.Piloto.PasoDelPlan");
        var leer = t?.GetMethod("Leer"); var relato = t?.GetMethod("Relato");
        Debe(t != null && tPaso != null && leer != null && relato != null,
            "todavía no existe «Piloto.PlanDeComprobacion.Leer/Relato» (spec 013, promesa 179). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || tPaso == null || leer == null || relato == null) return;

        var ok = leer.Invoke(null, new object[] { "[{\"n\":1,\"exit\":\"comando\",\"text\":\"nwp1\",\"tecla\":\"enter\",\"recuerdo\":\"el campo de comandos\",\"decir\":\"voy a la transacción\"},{\"n\":3,\"exit\":\"Urgencias Adultos/Triage\"}]" })!;
        Debe((string)Prop(ok, "Error")! == "", "un plan bien formado se lee sin error");
        var pasos = (System.Collections.IList)Prop(ok, "Pasos")!;
        Debe(pasos.Count == 2, $"y trae sus pasos ({pasos.Count})");
        if (pasos.Count == 2)
        {
            var p1 = pasos[0]!;
            Debe((int)Prop(p1, "N")! == 1 && (string)Prop(p1, "Exit")! == "comando" && (string)Prop(p1, "Texto")! == "nwp1"
                 && (string)Prop(p1, "Tecla")! == "enter" && (string)Prop(p1, "Recuerdo")! == "el campo de comandos" && (string)Prop(p1, "Decir")! == "voy a la transacción",
                "cada paso lleva el evento que cumple, la puerta, lo que teclea, el recuerdo y qué decir");
            // «"n":"7"» (2026-09-08, décima prueba): el piloto mandó los números como texto, el lector los
            // leyó como 0 y el juez no fue llamado en NINGÚN paso: 17 de 18 hechos y «2 de 16» de veredicto.
            var comoTexto = (System.Collections.IList)Prop(leer.Invoke(null, new object[] { "[{\"n\":\"7\",\"exit\":\"Peso\",\"text\":\"80\"},{\"n\":\"x\",\"exit\":\"Talla\"}]" })!, "Pasos")!;
            Debe(comoTexto.Count == 2 && (int)Prop(comoTexto[0]!, "N")! == 7 && (int)Prop(comoTexto[1]!, "N")! == 0,
                "un «n» que viene como texto numérico vale como número; uno que no es un número queda en 0, sin romper el plan");
            Debe((int)Prop(pasos[1]!, "N")! == 3 && (string)Prop(pasos[1]!, "Recuerdo")! == "" && (string)Prop(pasos[1]!, "Decir")! == "",
                "el recuerdo y el decir son opcionales: un paso puede ser solo la puerta");
        }
        var mal = leer.Invoke(null, new object[] { "[{\"n\":2,\"recuerdo\":\"algo\"}]" })!;
        Debe(((string)Prop(mal, "Error")!).Contains("paso 1"), "un paso sin puerta, texto ni tecla es un error que nombra el paso");
        Debe(((string)Prop(leer.Invoke(null, new object[] { "{\"exit\":\"x\"}" })!, "Error")!).Length > 0, "un objeto suelto no es una lista de pasos");
        Debe(((string)Prop(leer.Invoke(null, new object[] { "" })!, "Error")!).Contains("pasos"), "sin plan, se dice cómo se pide");

        string R(int hechos, int total, int paradoEn, string porQue, string donde, int at, int nav)
            => (string)relato.Invoke(null, relato.GetParameters().Length >= 8
                ? new object[] { hechos, total, paradoEn, porQue, donde, at, nav, "" }
                : new object[] { hechos, total, paradoEn, porQue, donde, at, nav })!;
        string paro = R(2, 5, 3, "«Triage» lo conozco aquí pero AHORA no lo veo.", "sapgui://X/0100", 1, 3);
        Debe(paro.Contains("HICE 2 DE 5") && paro.Contains("PARÉ en el paso 3") && paro.Contains("AHORA no lo veo") && paro.Contains("sapgui://X/0100"),
            $"si paró, el relato dice cuántos hizo, en cuál paró, por qué y dónde quedó ({paro})");
        Debe(paro.Contains("Sigue tú") && paro.Contains("leccion_llegue"),
            "…y le pasa las manos al piloto desde ese paso, con el juez de siempre");
        Debe(paro.Contains("1 de 3"), "y el juez habla con UN denominador: los eventos que navegan");
        string fin = R(5, 5, 0, "", "sapgui://X/fin", 3, 3);
        Debe(fin.Contains("HICE LOS 5 PASO(S)") && fin.Contains("3 de 3") && fin.Contains("leccion_guardar_skill"),
            $"si acabó, el relato lo dice con la cuenta del juez y qué hacer después ({fin})");
        // LO QUE FALTA, POR SU NOMBRE (2026-09-08, decimotercera prueba): «19 de 20» sin decir cuál, y el
        // piloto repitió cinco pasos ya hechos buscando el que faltaba.
        var relatoConFaltas = relato.GetParameters().Length >= 8 ? relato : null;
        Debe(relatoConFaltas != null, "«Relato» todavía no acepta lo que falta por juzgar (spec 014, promesa 179 extendida)");
        if (relatoConFaltas == null) return;
        string conFaltas = (string)relato.Invoke(null, new object[] { 5, 5, 0, "", "sapgui://X/fin", 4, 5, "evento 41 «Triage»: había que llegar a «sapgui://T» y acabé en «sapgui://X/fin»" })!;
        Debe(conFaltas.Contains("evento 41") && conFaltas.Contains("Triage") && conFaltas.Contains("FALTA", StringComparison.OrdinalIgnoreCase),
            $"con algo pendiente, el relato lo nombra, evento por evento, y dice que es eso lo que hay que resolver ({Recorta(conFaltas, 240)})");
        Debe(!fin.Contains("FALTA", StringComparison.OrdinalIgnoreCase), "y sin nada pendiente no se inventa una lista");
    }

    private static void CualquierMomentoDeLaDemoSePuedeMirar()
    {
        var tt = TiposDeLaLeccion.Cargar();
        var t = Capacidad("U.WindowsClient.Teach.CuadroDelMomento");
        var m = t?.GetMethod("Elegir");
        Debe(tt != null && m != null,
            "todavía no existe «Teach.CuadroDelMomento.Elegir» (fase 3 de la spec 013). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (tt == null || m == null) return;

        var cuadros = ListaDe(tt.CuadroLeccion);
        foreach (long h in new long[] { 0, 250, 500, 750, 1000, 33_500, 33_750 })
            cuadros.Add(Nuevo(tt.CuadroLeccion, h, $"c{h}.jpg", (int)(h / 10), 7, 1280, 720));

        object? E(long ms) => m.Invoke(null, new object[] { cuadros, ms });
        Debe(E(33_600) != null && (long)Prop(E(33_600)!, "HoraMs")! == 33_500,
            "pedir el segundo 33,6 da el cuadro más cercano (33,5), aunque ahí nadie hiciera clic");
        Debe((int)Prop(E(33_600)!, "CursorX")! == 3350, "…y trae dónde estaba el ratón: eso es lo que se señalaba");
        Debe(E(100_000) != null && (long)Prop(E(100_000)!, "HoraMs")! == 33_750,
            "pedir más allá del final da el último cuadro, no un error");
        Debe(m.Invoke(null, new object[] { ListaDe(tt.CuadroLeccion), 5_000L }) == null,
            "sin cuadros no hay nada que mirar: null, y quien pregunta lo dice");
    }

    // ── Spec 013: la lección que Claude ve ──────────────────────────────────────────────────

    /// <summary>Un cuadro de la cámara, construido por reflexión: (horaMs, ruta, huella).</summary>
    private static object Cuadro(Type t, long horaMs, ulong huella) =>
        Activator.CreateInstance(t, new object[] { horaMs, $"cuadro-{horaMs}.png", huella })!;

    private static void ElCuadroDeAntesSeEligeDelPasado()
    {
        var tCuadro = Capacidad("U.WindowsClient.Teach.Cuadro");
        var t = Capacidad("U.WindowsClient.Teach.CuadroDeAntes");
        var m = t?.GetMethod("Elegir");
        var margen = t?.GetField("MargenMs");
        Debe(tCuadro != null && t != null && m != null && margen != null,
            "todavía no existe «Teach.CuadroDeAntes.Elegir» (fase 1 de la spec 013). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (tCuadro == null || t == null || m == null || margen == null) return;

        int margenMs = (int)margen.GetValue(null)!;
        Debe(margenMs > 0,
            "el margen es positivo: un cuadro tomado en el MISMO milisegundo del clic no puede "
            + "demostrar que empezó a copiarse antes de que el ratón bajara");

        var lista = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(tCuadro))!;
        foreach (var h in new long[] { 600, 850, 1010, 1300 }) lista.Add(Cuadro(tCuadro, h, (ulong)h));
        object? Elige(object cuadros, long clic) => m.Invoke(null, new object[] { cuadros, clic, margenMs });
        long Hora(object c) => (long)tCuadro.GetProperty("HoraMs")!.GetValue(c)!;

        // El clic fue en 1000. El cuadro de 1010 ya puede llevar el efecto; el de 850 no.
        var antes = Elige(lista, 1000);
        Debe(antes != null && Hora(antes) == 850,
            $"con cuadros en 600, 850, 1010 y 1300 y el clic en 1000, el de antes es el de 850 "
            + $"(salió {(antes == null ? "null" : Hora(antes).ToString())})");

        // Y si el margen se lo come, retrocede: nunca avanza.
        var conMargenGrande = m.Invoke(null, new object[] { lista, 1000L, 200 });
        Debe(conMargenGrande != null && Hora(conMargenGrande) == 600,
            "con un margen de 200 ms el de 850 ya no vale y se retrocede al de 600: el margen "
            + "solo puede llevar hacia atrás");

        // NADA ANTES → NULL, no el más cercano de después. Un cuadro de después disfrazado de antes
        // es exactamente la mentira que esto viene a cerrar.
        var soloDespues = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(tCuadro))!;
        foreach (var h in new long[] { 1000, 1010, 1300 }) soloDespues.Add(Cuadro(tCuadro, h, (ulong)h));
        Debe(Elige(soloDespues, 1000) == null,
            "si todos los cuadros son del clic o posteriores, no hay cuadro de antes: null, y no "
            + "«el más cercano»");
        Debe(Elige(Activator.CreateInstance(typeof(List<>).MakeGenericType(tCuadro))!, 1000) == null,
            "y sin cuadros tampoco");
    }

    private static void ElCuadroDeDespuesEsElPrimeroAsentado()
    {
        var tCuadro = Capacidad("U.WindowsClient.Teach.Cuadro");
        var t = Capacidad("U.WindowsClient.Teach.CuadroDeDespues");
        var m = t?.GetMethod("Elegir");
        var espera = t?.GetField("EsperaMinimaMs");
        var techo = t?.GetField("TechoMs");
        Debe(tCuadro != null && t != null && m != null && espera != null && techo != null,
            "todavía no existe «Teach.CuadroDeDespues.Elegir» (fase 1 de la spec 013). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (tCuadro == null || t == null || m == null || espera == null || techo == null) return;

        int esperaMs = (int)espera.GetValue(null)!, techoMs = (int)techo.GetValue(null)!;
        Debe(esperaMs > 0 && techoMs > esperaMs,
            $"hay una espera mínima ({esperaMs}) y un techo por encima de ella ({techoMs}): sin "
            + "espera se elegiría el cuadro en que SAP aún no contestó; sin techo, un reloj que no "
            + "se asienta nunca dejaría al médico esperando para siempre");

        long Hora(object c) => (long)tCuadro.GetProperty("HoraMs")!.GetValue(c)!;
        object Lista(params (long h, ulong huella)[] cuadros)
        {
            var l = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(tCuadro))!;
            foreach (var (h, huella) in cuadros) l.Add(Cuadro(tCuadro, h, huella));
            return l;
        }
        (long hora, bool asentado)? Elige(object cuadros, long clic)
        {
            var r = m.Invoke(null, new object[] { cuadros, clic, 400, 2000 });
            if (r == null) return null;
            var tr = r.GetType();
            var cuadro = tr.GetProperty("Cuadro")!.GetValue(r)!;
            return (Hora(cuadro), (bool)tr.GetProperty("Asentado")!.GetValue(r)!);
        }

        // Clic en 1000. A los 1200 la pantalla vieja (A); 1450 en transición (B); en 1700 llegó la
        // nueva (C) y en 1950 sigue igual (C): se asentó en 1700.
        var r1 = Elige(Lista((1200, 1), (1450, 2), (1700, 3), (1950, 3), (2200, 3)), 1000);
        Debe(r1 is { hora: 1700, asentado: true },
            $"el de después es el de 1700, el primero que se repite pasado el clic más 400 ms "
            + $"(salió {r1})");

        // El de 1200 se repite con nadie y además cae antes de la espera mínima: no cuenta aunque
        // fuera igual al siguiente.
        var r2 = Elige(Lista((1200, 1), (1350, 1), (1700, 3), (1950, 3)), 1000);
        Debe(r2 is { hora: 1700, asentado: true },
            "dos cuadros iguales ANTES de la espera mínima no son «asentado»: la pantalla vieja "
            + "también se repite consigo misma");

        // NUNCA SE ASIENTA DENTRO DEL TECHO → el último dentro del techo, y se dice.
        var r3 = Elige(Lista((1450, 2), (1700, 3), (1950, 4), (3100, 5), (3400, 5)), 1000);
        Debe(r3 is { hora: 1950, asentado: false },
            $"si no se asienta antes del techo (3000), se entrega el último de dentro (1950) con "
            + $"asentado=false — y el par igual de 3100/3400 NO cuenta, está fuera del techo (salió {r3})");

        Debe(Elige(Lista((1200, 1)), 1000) == null,
            "sin ningún cuadro pasado el clic más la espera, no hay cuadro de después: null");
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




    /// <summary>Promesa 167.</summary>
    private static void LaLineaSeHaceEsperar()
    {
        var t = Capacidad("U.WindowsClient.Ui.ReglaDeLaLinea");
        if (t == null) { Pendiente("ReglaDeLaLinea", "167", "011"); return; }

        var asoma = t.GetMethod("Asoma", BindingFlags.Public | BindingFlags.Static);
        var reposoMs = t.GetField("ReposoMs")?.GetValue(null);
        if (asoma == null || reposoMs == null) { Pendiente("ReglaDeLaLinea.Asoma/ReposoMs", "167", "011"); return; }
        int ms = Convert.ToInt32(reposoMs);
        bool Asoma(int quieto, bool arrastrando) =>
            (bool)asoma.Invoke(null, new object[] { quieto, arrastrando })!;

        // SEGUNDO Y MEDIO, y el suelo es alto a proposito: la primera version salia al instante y la
        // segunda a 550 ms, y las dos se rechazaron por lo mismo — «lo veo muy rápido». Por debajo
        // de 1,2 s esto vuelve a ser un cartel que se adelanta a lo que ibas a hacer.
        Debe(ms >= 1200, $"la línea se hace esperar de verdad: son {ms} ms, y el dueño pidió segundo y medio");
        Debe(!Asoma(0, false), "rozarla de camino a otra cosa no la llama");
        Debe(!Asoma(ms - 1, false), "ni fallando un milisegundo para el reposo");
        Debe(!Asoma(550, false), "ni con el reposo que tenía antes: 550 ms se rechazó por rápido");
        Debe(Asoma(ms, false), "cumplido el reposo, asoma");
        Debe(!Asoma(ms * 10, true), "pero arrastrándola no asoma por mucho que se tarde: ir a moverla no es ir a escribirle");
    }

    // ── Lo hace a la primera (spec 017) ──────────────────────────────────────

    /// <summary>
    /// Dos «Descargas» vivas en «a»: un TreeItem que lleva a «b» y un TabItem que lleva a «c», las
    /// dos ya cruzadas, así que el grafo sabe a dónde lleva cada una. Es el caso medido el
    /// 2026-08-09 (u-20260809.log, 09:37:31): tres «Descargas» de tipos distintos y un «dime el
    /// selector» sin número ni destino.
    /// </summary>
    private static Nucleo.Grafo DosDescargas()
    {
        var g = new Nucleo.Grafo();
        g.Observar("uia://x.exe/b", new[] { new Nucleo.Elemento("s:b", "Bee", "Button") });
        g.Observar("uia://x.exe/c", new[] { new Nucleo.Elemento("s:c", "Cee", "Button") });
        g.Observar("uia://x.exe/a", new[]
        {
            new Nucleo.Elemento("s:1", "Descargas", "TreeItem"),
            new Nucleo.Elemento("s:2", "Descargas", "TabItem"),
        });
        g.Cruzar("uia://x.exe/a", "s:1", "uia://x.exe/b", "");
        g.Cruzar("uia://x.exe/a", "s:2", "uia://x.exe/c", "");
        return g;
    }

    private static readonly Dictionary<string, string> RutasDeDescargas = new()
    {
        ["uia://x.exe/a|s:1"] = "uia://x.exe/b",
        ["uia://x.exe/a|s:2"] = "uia://x.exe/c",
    };

    /// <summary>
    /// Los nombres de los argumentos que el catálogo de la voz declara para una herramienta, o null
    /// si no aparece. El catálogo lo arma <c>ConversacionEnVivo.Herramientas()</c> con los
    /// <c>Utensilio</c>/<c>Argumento</c> de Voz.Realtime; se lee por reflexión, como el resto.
    /// </summary>
    private static HashSet<string>? ArgumentosDe(string herramienta)
    {
        var t = Cap004("U.WindowsClient.Voice.ConversacionEnVivo");
        var m = t?.GetMethod("Herramientas", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        if (m?.Invoke(null, null) is not System.Collections.IEnumerable todas) return null;
        foreach (var u in todas)
        {
            if (u == null) continue;
            var tu = u.GetType();
            if ((string?)tu.GetProperty("Nombre")?.GetValue(u) != herramienta) continue;
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (tu.GetProperty("Args")?.GetValue(u) is System.Collections.IEnumerable args)
                foreach (var a in args)
                    if (a?.GetType().GetProperty("Nombre")?.GetValue(a) is string n) set.Add(n);
            return set;
        }
        return null;
    }

    private static void PulsarDiceLoQuePaso()
    {
        // Hoy «Recorre» cierra con «hice los N paso(s): quedaste en…» y se traga el «pulsé X y la
        // pantalla no cambió» que «Pulsa» ya sabía decir (RecorrerSegunElNucleo.cs:183-185 frente a
        // PulsarSegunElNucleo.cs:101-103). Sin ese dato el cerebro no puede darse cuenta de que
        // «por aquí no era» —lo que el audio pide literalmente— y paga otra mirada para averiguarlo.
        var (b1, _, t1) = BatchCon(MundoDeTres(), "uia://x.exe/a",
            new Dictionary<string, string> { ["uia://x.exe/a|s:1"] = "uia://x.exe/b" });
        var r1 = b1.Recorre(new[] { new RecorrerSegunElNucleo.Paso("Uno") });
        Debe(t1.Count == 1 && r1.Termino && r1.Cuenta.Contains("ahora estás en «uia://x.exe/b»"),
            $"una tanda de un clic que navega dice A DÓNDE llevó (dijo: «{r1.Cuenta}»)");

        var (b2, _, t2) = BatchCon(MundoDeTres(), "uia://x.exe/a", new Dictionary<string, string>());
        var r2 = b2.Recorre(new[] { new RecorrerSegunElNucleo.Paso("Uno") });
        Debe(t2.Count == 1 && r2.Termino && r2.Cuenta.Contains("no cambió"),
            $"y la que pulsa sin que nada cambie lo dice con esas palabras, aunque la tanda termine "
            + $"(dijo: «{r2.Cuenta}»): sin esto no hay «me di cuenta de que por aquí no era»");
        Debe(!r1.Cuenta.Contains("quedaste en") && !r2.Cuenta.Contains("quedaste en"),
            "y «hice los 1 paso(s): quedaste en…» ya no tapa el último hecho: detrás de «hice los» va lo que pasó");

        var (b3, _, _) = BatchCon(MundoDeTres(), "uia://x.exe/a", RutasDeTres);
        var r3 = b3.Recorre(new[] { new RecorrerSegunElNucleo.Paso("Uno"), new RecorrerSegunElNucleo.Paso("Dos") });
        Debe(r3.Termino && r3.Cuenta.Contains("ahora estás en «uia://x.exe/c»"),
            $"en una tanda de varios, el último hecho también se dice (dijo: «{r3.Cuenta}»)");
    }

    private static void LosHomonimosSeNumeran()
    {
        // El 2026-08-09 (09:37:31) «map_take exit=Descargas» casó con tres salidas vivas y la
        // respuesta fue «hay 3 puertas vivas… Dime el selector y sigo»: sin número, sin tipo, sin
        // destino. La voz pidió el mismo selector tres veces, 6-7 s cada una, y no llegó.
        var (b1, _, t1) = BatchCon(DosDescargas(), "uia://x.exe/a", RutasDeDescargas);
        var r1 = b1.Recorre(new[] { new RecorrerSegunElNucleo.Paso("Descargas") });
        Debe(t1.Count == 0 && !r1.Termino,
            $"con dos «Descargas» vivas no se adivina: no se pulsa ninguna (se pulsaron {t1.Count})");
        int i1 = r1.Cuenta.IndexOf("1)", StringComparison.Ordinal);
        int i2 = r1.Cuenta.IndexOf("2)", StringComparison.Ordinal);
        Debe(i1 >= 0 && i2 > i1 && r1.Cuenta.Contains("TreeItem") && r1.Cuenta.Contains("TabItem"),
            $"se numeran 1..N con su tipo, en vez de pedir un selector a ciegas (dijo: «{r1.Cuenta}»)");
        Debe(r1.Cuenta.Contains("uia://x.exe/b") && r1.Cuenta.Contains("uia://x.exe/c"),
            "y, si el grafo lo sabe, a dónde lleva cada una: es lo que el cerebro necesita para elegir");

        var cual = typeof(RecorrerSegunElNucleo.Paso).GetProperty("Cual");
        if (cual == null) { Pendiente("RecorrerSegunElNucleo.Paso.Cual", "203", "017"); return; }
        var (b2, donde2, t2) = BatchCon(DosDescargas(), "uia://x.exe/a", RutasDeDescargas);
        var paso = new RecorrerSegunElNucleo.Paso("Descargas");
        cual.SetValue(paso, 2);
        b2.Recorre(new[] { paso });
        Debe(t2.Count == 1 && donde2() == "uia://x.exe/c",
            $"un paso que trae cuál (2) pulsa esa y solo esa: quedó en «{donde2()}» con {t2.Count} toque(s)");

        // LOS CANDIDATOS VIAJAN COMO DATOS, no solo en la prosa (crítico, tercera pasada, 2026-09-11): el
        // tope necesita saber que un selector de la lista ES ese candidato, y sacarlo de la cuenta sería
        // concluir leyendo un mensaje (aprendizaje nº2).
        var pCand = typeof(RecorrerSegunElNucleo.Resultado).GetProperty("Candidatos");
        if (pCand == null) { Pendiente("RecorrerSegunElNucleo.Resultado.Candidatos", "203", "017"); return; }
        var cands = pCand.GetValue(r1) as IReadOnlyList<string>;
        int c0 = cands is { Count: 2 } ? r1.Cuenta.IndexOf("«" + cands[0] + "»", StringComparison.Ordinal) : -1;
        int c1 = cands is { Count: 2 } ? r1.Cuenta.IndexOf("«" + cands[1] + "»", Math.Max(i2, 0), StringComparison.Ordinal) : -1;
        Debe(c0 > i1 && c0 < i2 && c1 > i2,
            $"y la lista viaja también como datos, en el MISMO orden que su número (llegó: {(cands == null ? "nada" : string.Join(" · ", cands))})");

        var args = ArgumentosDe("map_take");
        Debe(args != null && args.Contains("which"),
            "y el cerebro puede decir cuál: map_take declara `which`, como ya lo tenía map_show");
    }

    private static void DosIntentosYNoTres()
    {
        // No había tope en el código: EjecutarNucleoAsync ejecuta todo lo que se pide, y la regla
        // escrita toleraba tres llamadas («más de dos veces»). El 2026-08-09 fueron tres idénticas a
        // «Descargas», 6-7 s cada una. El audio: «máximo dos intentos».
        var t = Cap004("U.WindowsClient.Voice.TopeDeIntentos");
        var rechazo = t?.GetMethod("Rechazo");
        var anota = t?.GetMethod("Anota");
        var nuevo = t?.GetMethod("NuevoTurno");
        if (t == null || rechazo == null || anota == null || nuevo == null)
        { Pendiente("Voice.TopeDeIntentos", "204", "017"); return; }

        var tope = Activator.CreateInstance(t)!;
        string? R(string h, string d) => (string?)rechazo.Invoke(tope, new object[] { h, d });
        void A(string h, string d, bool logrado, string salio) => anota.Invoke(tope, new object[] { h, d, logrado, salio });

        Debe(R("map_take", "Descargas") == null, "el primer intento pasa");
        A("map_take", "Descargas", false, "pulsé «Descargas» y la pantalla no cambió");
        Debe(R("map_take", "uia:name=Descargas;ct=TabItem") == null, "el segundo también, aunque venga por selector");
        A("map_take", "uia:name=Descargas;ct=TabItem", false, "no lo encontré vivo");
        string? tercero = R("map_take", "descargas");
        Debe(tercero != null && tercero.Contains("dos"),
            $"el TERCERO a un destino que ya falló dos veces no se ejecuta, y se dice que van dos (dijo: «{tercero}»). "
            + "Por nombre, por selector o en minúsculas es el MISMO destino (aprendizaje nº16)");
        Debe(tercero != null && tercero.Contains("no cambió") && tercero.Contains("no lo encontré"),
            "y dice qué salió en cada uno: es lo que el cerebro necesita para cambiar de vía en vez de insistir");
        Debe(tercero != null && tercero.Contains("mismo botón"),
            "y al pulsar dice que pedirlo de otra forma es el mismo botón: el ejecutor lo cumple (séptima pasada)");
        Debe(R("map_take", "Documentos") == null, "otro destino no hereda el castigo");

        nuevo.Invoke(tope, null);
        Debe(R("map_take", "Descargas") == null, "un turno nuevo del usuario empieza de cero");

        for (int i = 0; i < 3; i++)
        {
            Debe(R("map_take", "Siguiente") == null, $"lo logrado no cuenta como intento fallido (vez {i + 1})");
            A("map_take", "Siguiente", true, "ahora estás en…");
        }
        for (int i = 0; i < 3; i++) { A("map_look", "", false, "aquí tienes lo que hay"); A("map_where_am_i", "", false, "estás en…"); }
        Debe(R("map_look", "") == null && R("map_where_am_i", "") == null, "y mirar nunca cuenta: mirar no es insistir");

        // EL OTRO BOTÓN SÍ SE PRUEBA (crítico de la rama, 2026-09-11). El audio pide «me di cuenta que
        // por aquí no era… voy atrás, pruebo este otro botón». Sin el candidato en la clave, dos fallos
        // al 1 de una lista numerada frenaban también al 2 —con un rechazo que encima decía «elige otro
        // candidato con which»—.
        nuevo.Invoke(tope, null);
        var destinoDe = t.GetMethod("DestinoDe");
        string Cand(string n) => (string)destinoDe!.Invoke(null, new object?[] { "map_take",
            new Dictionary<string, string> { ["exit"] = "Descargas", ["which"] = n } })!;
        // Despues recibe también los candidatos de la lista. Se busca por aridad: reflexión no rellena
        // opcionales, y un parámetro nuevo rompería la llamada (la lección de la 103).
        var despues = t.GetMethods().FirstOrDefault(m => m.Name == "Despues" && m.GetParameters().Length == 7);
        if (despues == null) { Pendiente("Voice.TopeDeIntentos.Despues(…, candidatos)", "204", "017"); return; }
        void DC(string d, bool revento, bool? intento, bool? logro, string salio, IReadOnlyList<string>? candidatos)
            => despues.Invoke(tope, new object?[] { "map_take", d, revento, intento, logro, salio, candidatos });
        void D(string d, bool revento, bool? intento, bool? logro, string salio) => DC(d, revento, intento, logro, salio, null);
        const string Lista = "hay 2 puertas vivas para «Descargas»: 1) «Descargas» (TreeItem) · 2) «Descargas» (TabItem)";

        D("Descargas", false, false, false, Lista);
        D(Cand("1"), false, true, false, "pulsé «Descargas» y la pantalla no cambió");
        D(Cand("1"), false, true, false, "pulsé «Descargas» y la pantalla no cambió");
        string? alUno = R("map_take", Cand("1"));
        Debe(alUno != null, "el mismo candidato, por tercera vez, se frena");
        Debe(alUno != null && alUno.Contains("which") && alUno.Contains("TabItem"),
            $"y el rechazo recuerda la lista, para elegir otro de verdad y no a ciegas (dijo: «{alUno}»)");
        Debe(R("map_take", Cand("2")) == null,
            "pero el OTRO candidato de la lista es otro destino: probarlo es lo que el audio pide, no insistir");

        // SIN LISTA, `which` NO ABRE UNA CLAVE NUEVA (crítico final, 2026-09-11). El ejecutor solo lee
        // `which` cuando hay homónimos: con un único «Descargas», which=1, 2, 3… pulsan el MISMO botón, y
        // cada número era una clave nueva con dos intentos más —la espiral de «20 segundos con 20
        // herramientas» del audio—. Y el rechazo, encima, sugería hacerlo.
        nuevo.Invoke(tope, null);
        D("Descargas", false, true, false, "pulsé «Descargas» y la pantalla no cambió");
        D("Descargas", false, true, false, "pulsé «Descargas» y la pantalla no cambió");
        string? sinLista = R("map_take", Cand("1"));
        Debe(sinLista != null, "sin una lista de homónimos, «Descargas» con which=1 es el mismo botón: se frena");
        Debe(sinLista != null && !sinLista.Contains("which"),
            $"y el rechazo no sugiere `which` cuando no hubo lista: sería mandar al modelo a esquivar el tope (dijo: «{sinLista}»)");

        // LO QUE LA VOZ HACE DESPUÉS DE CADA HERRAMIENTA, en un solo sitio juzgable (crítico final): antes
        // vivía en ConversacionEnVivo, y cambiarlo por «siempre logrado» dejaba el contrato INTACTO.
        nuevo.Invoke(tope, null);
        D("Nuevo", true, null, null, "la herramienta falló: …");
        D("Nuevo", true, null, null, "la herramienta falló: …");
        Debe(R("map_take", "Nuevo") != null, "una excepción es un intento que no se logró");
        for (int i = 0; i < 3; i++) D("Buscar", false, null, null, "todavía no sé pulsar: el núcleo no está conectado.");
        Debe(R("map_take", "Buscar") == null, "lo que no trae mano no cuenta como fallo: no se adivina");
        for (int i = 0; i < 3; i++) D("Pegar", false, false, false, "hay 2 puertas vivas para «Pegar»: …");
        Debe(R("map_take", "Pegar") == null, "y pedir la lista tres veces no es insistir: no se pulsó nada");
        for (int i = 0; i < 3; i++) D("Siguiente", false, true, true, "ahora estás en…");
        Debe(R("map_take", "Siguiente") == null, "y lo logrado, tampoco");

        // TRAS UNA LISTA, EL SELECTOR DE UN CANDIDATO ES ESE CANDIDATO (crítico, tercera pasada): el
        // rechazo trae la lista con sus selectores, y pedir «uia:name=Descargas;ct=TabItem» después de dos
        // fallos con which=2 abría una clave nueva para el MISMO botón: cuatro intentos, repitiendo un
        // selector, que es justo lo del 2026-08-09. Y which=02 o +2 son el 2, como los lee el ejecutor.
        nuevo.Invoke(tope, null);
        var sel = new[] { "uia:name=Descargas;ct=TreeItem", "uia:name=Descargas;ct=TabItem" };
        DC("Descargas", false, false, false, Lista, sel);
        D(Cand("2"), false, true, false, "pulsé «Descargas» y la pantalla no cambió");
        D(Cand("2"), false, true, false, "pulsé «Descargas» y la pantalla no cambió");
        Debe(R("map_take", sel[1]) != null,
            "tras la lista, el selector del candidato 2 ES el candidato 2: pedirlo por selector no le da dos intentos más");
        Debe(R("map_take", Cand("02")) != null && R("map_take", Cand("+2")) != null,
            "y which=02 o which=+2 son el 2, como los lee el ejecutor");
        Debe(R("map_take", sel[0]) == null && R("map_take", Cand("1")) == null,
            "pero el 1, por su selector o por su número, sigue siendo otro botón");

        // CON `which` O CON LOS SELECTORES REALES, EL MISMO BOTÓN (crítico, cuarta pasada, 2026-09-11).
        // El ejecutor pulsa el selector exacto e ignora `which` (EsperarloVivo: «el selector exacto
        // manda»), y un selector que no es `uia:name=` no se aplana a su etiqueta. «selector#which=N» daba
        // dos intentos más por cada N, y con selectores como los de la tanda (s:1, s:2) o los de SAP la
        // lista no se encontraba nunca: la sonda sobre el U.dll contó 6 y 4 toques al mismo botón.
        string Sel(string sl, string n) => (string)destinoDe!.Invoke(null, new object?[] { "map_take",
            new Dictionary<string, string> { ["exit"] = sl, ["which"] = n } })!;
        nuevo.Invoke(tope, null);
        DC("Descargas", false, false, false, Lista, sel);
        D(sel[1], false, true, false, "pulsé «Descargas» y la pantalla no cambió");
        D(sel[1], false, true, false, "pulsé «Descargas» y la pantalla no cambió");
        Debe(R("map_take", Sel(sel[1], "5")) != null && R("map_take", Sel(sel[1], "7")) != null,
            "el selector exacto de un candidato, con cualquier which, es ese candidato: el ejecutor pulsa el selector e ignora which");
        Debe(R("map_take", Sel(sel[0], "2")) == null,
            "y el selector del 1 con which=2 es el 1 —el que se pulsa—: los fallos del 2 no lo frenan");

        // Y CON LOS CANDIDATOS REALES DEL EJECUTOR, no con los de este test: la tanda de la 203. Si el
        // contrato no cruza las dos piezas, elige el caso que pasa (crítico, cuarta pasada).
        nuevo.Invoke(tope, null);
        var (bR, _, _) = BatchCon(DosDescargas(), "uia://x.exe/a", RutasDeDescargas);
        var rR = bR.Recorre(new[] { new RecorrerSegunElNucleo.Paso("Descargas") });
        var candsR = typeof(RecorrerSegunElNucleo.Resultado).GetProperty("Candidatos")?.GetValue(rR) as IReadOnlyList<string>;
        Debe(candsR is { Count: 2 }, "la tanda de la 203 devuelve sus dos candidatos como datos");
        if (candsR is { Count: 2 })
        {
            DC("Descargas", false, false, false, rR.Cuenta, candsR);
            D(Cand("2"), false, true, false, "pulsé «Descargas» y la pantalla no cambió");
            D(Cand("2"), false, true, false, "pulsé «Descargas» y la pantalla no cambió");
            Debe(R("map_take", candsR[1]) != null && R("map_take", Sel(candsR[1], "9")) != null,
                $"con los selectores reales del ejecutor («{candsR[1]}»), pedir el 2 por su selector —con o sin which— es el 2");
            Debe(R("map_take", candsR[0]) == null, "y el 1, por su selector real, sigue siendo otro botón");
        }

        // EL TOPE MIRA LO QUE SE VA A PULSAR (crítico, quinta pasada, 2026-09-11). Cinco pasadas
        // encontraron cinco formas de la misma diferencia —el tope juzgaba lo PEDIDO y el ejecutor decide
        // por sus reglas—: el mismo id antes y después de una lista (4 toques), y cualquier trozo de la
        // etiqueta, que el ejecutor casa por contención (18 toques a un botón con 9 variantes). La clase se
        // cierra donde se decide: el ejecutor le pregunta al tope con el selector que VA a pulsar.
        var antesDe = t.GetMethod("AntesDePulsar");
        var pAntes = typeof(RecorrerSegunElNucleo.Paso).GetProperty("AntesDePulsar");
        var pPulsado = typeof(RecorrerSegunElNucleo.Resultado).GetProperty("Pulsado");
        var despues8 = t.GetMethods().FirstOrDefault(m => m.Name == "Despues" && m.GetParameters().Length == 8);
        if (antesDe == null || pAntes == null || pPulsado == null || despues8 == null)
        { Pendiente("TopeDeIntentos.AntesDePulsar · Paso.AntesDePulsar · Resultado.Pulsado", "204", "017"); return; }
        var pCual = typeof(RecorrerSegunElNucleo.Paso).GetProperty("Cual")!;
        var pAmb = typeof(RecorrerSegunElNucleo.Resultado).GetProperty("Ambiguo")!;
        var pCands = typeof(RecorrerSegunElNucleo.Resultado).GetProperty("Candidatos")!;
        // Lo que hacen la voz y la mano en cada llamada, sin pantalla: el tope ante lo pedido, el
        // ejecutor con la consulta dentro del paso, y lo que salió de vuelta al tope.
        // Cuenta PULSACIONES —llamadas que tocaron algo—, no toques: una pulsación que no cambia la
        // pantalla da dos toques (el ensayo de doble clic), y la primera versión de esta prueba los confundió.
        int pulsadas = 0;
        string Llamada(RecorrerSegunElNucleo b, List<string> toques, string exit, int cual)
        {
            string pedido = cual > 0 ? $"{exit}#which={cual}" : exit;
            if (R("map_take", pedido) is string antesDeLlamar) return antesDeLlamar;
            var pasoT = new RecorrerSegunElNucleo.Paso(exit);
            if (cual > 0) pCual.SetValue(pasoT, cual);
            pAntes.SetValue(pasoT, (Func<string, string?>)(sl => (string?)antesDe.Invoke(tope, new object[] { "map_take", sl })));
            int toquesAntes = toques.Count;
            var res = b.Recorre(new[] { pasoT });
            if (toques.Count > toquesAntes) pulsadas++;
            despues8.Invoke(tope, new object?[] { "map_take", pedido, false, !(bool)pAmb.GetValue(res)!,
                res.Termino && res.Cambio, res.Cuenta, pCands.GetValue(res), pPulsado.GetValue(res) });
            return res.Cuenta;
        }

        // El mismo id antes y después de una lista: dos toques, no cuatro.
        nuevo.Invoke(tope, null);
        var (bId, _, tId) = BatchCon(DosDescargas(), "uia://x.exe/a", new Dictionary<string, string>());
        pulsadas = 0;
        Llamada(bId, tId, "s:1", 0);
        Llamada(bId, tId, "s:1", 0);
        Llamada(bId, tId, "Descargas", 0);
        string terceraVez = Llamada(bId, tId, "s:1", 0);
        Debe(pulsadas == 2 && terceraVez.Contains("tercera"),
            $"el mismo botón, pedido por su id antes y después de una lista, se pulsa dos veces y no más (se pulsó {pulsadas} veces; dijo: «{terceraVez}»)");
        Debe(terceraVez.Contains("which") && terceraVez.Contains("«s:2»"),
            $"y el freno del ejecutor recuerda la lista, para probar el OTRO candidato en vez de rendirse (sexta pasada; dijo: «{terceraVez}»)");

        // Cualquier trozo de la etiqueta es el mismo botón: dos toques, no dos por variante.
        nuevo.Invoke(tope, null);
        var gUno = new Nucleo.Grafo();
        gUno.Observar("uia://x.exe/a", new[] { new Nucleo.Elemento("s:9", "Descargas recientes", "ListItem") });
        var (bUno, _, tUno) = BatchCon(gUno, "uia://x.exe/a", new Dictionary<string, string>());
        pulsadas = 0;
        foreach (var variante in new[] { "Descargas recientes", "Descargas", "descargas rec", "Descarga", "recientes" })
            Llamada(bUno, tUno, variante, 0);
        Debe(pulsadas == 2,
            $"cinco formas de nombrar el mismo botón son el mismo botón: se pulsa dos veces y no más (se pulsó {pulsadas} veces)");

        // ESCRIBIR también tiene su tope, por el campo TAL COMO SE PIDIÓ (sexta pasada del crítico): el mismo
        // nombre, en minúsculas o por su selector UIA, es el mismo campo. Es una guarda de lo que ya hacía el
        // código, no una promesa nueva. Lo que NO cubre —tres nombres de un campo de SAP, que resuelve
        // EscribirPorMundo en FaceWindow— está declarado en la spec, y por eso el enunciado se acotó.
        nuevo.Invoke(tope, null);
        despues8.Invoke(tope, new object?[] { "map_type", "Nombre", false, true, false, "no pude escribir en «Nombre»", null, null });
        despues8.Invoke(tope, new object?[] { "map_type", "uia:name=Nombre;ct=Edit", false, true, false, "no pude escribir en «Nombre»", null, null });
        string? alEscribir = R("map_type", "nombre");
        Debe(alEscribir != null,
            "escribir por tercera vez en un campo que ya falló dos veces, por su nombre o por su selector UIA, tampoco se ejecuta");
        Debe(alEscribir != null && !alEscribir.Contains("mismo botón") && alEscribir.Contains("otro campo"),
            $"y al escribir el rechazo no promete «es el mismo botón» —ahí no se cumple— ni manda pulsar: manda otro campo (séptima pasada; dijo: «{alEscribir}»)");
    }

    private static void CadaTurnoDejaSuMedida()
    {
        // No existía la unidad «petición»: «usuario dijo» se escribe al cerrar el turno y «mapa-mcp:
        // →» no dice quién llamó. Sin una línea por turno no se puede decir si algo se hizo a la
        // primera: solo adivinarlo sumando líneas sueltas.
        var t = Cap004("U.WindowsClient.Voice.CuentaDelTurno");
        if (t == null) { Pendiente("Voice.CuentaDelTurno", "205", "017"); return; }

        long ahora = 1000;
        var c = Activator.CreateInstance(t, new object[] { (Func<long>)(() => ahora) })!;
        void M(string metodo, params object[] a) => t.GetMethod(metodo)!.Invoke(c, a);

        M("Llamada", "map_where_am_i", "");
        ahora = 1200; M("Resultado", "map_where_am_i", "", false);
        ahora = 1300; M("Llamada", "map_take", "Descargas");
        ahora = 2900; M("Resultado", "map_take", "Descargas", false);
        ahora = 3000; M("Llamada", "map_take", "uia:name=Descargas;ct=TabItem");
        ahora = 4000; M("Resultado", "map_take", "uia:name=Descargas;ct=TabItem", true);
        ahora = 4100; M("Llamada", "map_take", "Descargas"); M("Rechazada", "map_take", "Descargas");
        ahora = 4200; M("Llamada", "map_type", "Nombre"); M("Retirada", "map_type");

        string? linea = (string?)t.GetMethod("Cerrar")!.Invoke(c, null);
        Debe(linea != null && linea.Contains("llamadas=5"),
            $"cuenta TODO lo pedido, también lo rechazado y lo retirado: el denominador es lo pedido (dijo: «{linea}»)");
        Debe(linea != null && linea.Contains("distintas=3"), "cuántas herramientas distintas usó");
        Debe(linea != null && linea.Contains("intentos_max=3") && linea.Contains("descargas"),
            "el máximo de intentos a un mismo destino, y cuál: por nombre y por selector es el mismo");
        Debe(linea != null && linea.Contains("primera=3000") && linea.Contains("ultima=3000"),
            "y los milisegundos desde la primera llamada hasta la primera acción que actuó, y la última");
        Debe(t.GetMethod("Cerrar")!.Invoke(c, null) == null,
            "cerrar deja la cuenta a cero: el turno siguiente no hereda nada");
        Debe(linea != null && linea.Contains("rechazadas=1") && linea.Contains("retiradas=1"),
            "y lo frenado y lo retirado se dicen aparte, además de contar como llamadas");

        // UN RESULTADO QUE CRUZA EL CIERRE NO DA TIEMPOS NEGATIVOS, y se mide desde que el usuario pidió
        // (crítico de la rama, 2026-09-11). «Resultado» sin «Llamada» en el turno dejaba «primera»
        // puesta, y salía negativa. Y los dos segundos del audio van desde que se pide, no desde la
        // primera llamada del modelo.
        var peticion = t.GetMethod("Peticion");
        if (peticion == null) { Pendiente("Voice.CuentaDelTurno.Peticion", "205", "017"); return; }
        var c2 = Activator.CreateInstance(t, new object[] { (Func<long>)(() => ahora) })!;
        void M2(string metodo, params object[] a) => t.GetMethod(metodo)!.Invoke(c2, a);
        ahora = 900; M2("Resultado", "map_take", "X", true);
        ahora = 950; peticion.Invoke(c2, null);
        ahora = 1000; M2("Llamada", "map_take", "Descargas");
        ahora = 3000; M2("Resultado", "map_take", "Descargas", true);
        ahora = 3100; M2("Llamada", "map_type", "Nombre");
        ahora = 4000; M2("Resultado", "map_type", "Nombre", true);
        string? l2 = (string?)t.GetMethod("Cerrar")!.Invoke(c2, null);
        Debe(l2 != null && l2.Contains("primera=2000") && l2.Contains("ultima=3000") && !l2.Contains("=-"),
            $"un resultado que llega sin llamada en el turno no cuenta, y nunca sale un tiempo negativo (dijo: «{l2}»)");
        Debe(l2 != null && l2.Contains("desde_peticion=2050"),
            "y se dice cuánto pasó desde que el usuario pidió hasta la primera acción que actuó: son los «dos segundos» del audio");
    }

    private static void ElCatalogoPideLoQueLasManosUsan()
    {
        // `at` y `action` de map_take, y `at` de map_type, no se usan en su cuerpo desde que
        // e3c3ad8 borró ComprobarUbicacion (SurfaceMapTools.cs, Take/Type); aun así el catálogo los
        // ofrecía y las instrucciones mandaban pedir map_where_am_i para rellenarlos.
        var take = ArgumentosDe("map_take");
        var type = ArgumentosDe("map_type");
        var unblock = ArgumentosDe("map_unblock");
        Debe(take != null && type != null && unblock != null, "encuentro el catálogo de la voz");
        if (take == null || type == null || unblock == null) return;

        Debe(!take.Contains("at") && !take.Contains("action"),
            "map_take no ofrece `at` ni `action`: su cuerpo los ignora, y ofrecerlos es la ilusión de controlar el gesto");
        Debe(!type.Contains("at"), "map_type tampoco ofrece `at`");
        Debe(unblock.Contains("at"), "map_unblock SÍ conserva `at`: ahí se usa, y no se borra lo vivo con lo muerto");
        Debe(take.Contains("which"), "y map_take trae `which` para elegir entre homónimos");

        var p = Cap004("U.WindowsClient.Voice.ConversacionEnVivo")?.GetProperty("InstruccionesNormales",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        string texto = (string?)p?.GetValue(null) ?? "";
        Debe(texto.Length > 0, "encuentro las instrucciones de la voz");
        Debe(!texto.Contains("pide map_where_am_i primero", StringComparison.OrdinalIgnoreCase),
            "las instrucciones no mandan pedir map_where_am_i para rellenar un argumento que ya no existe");
        Debe(!texto.Contains("action=«doubleclick»", StringComparison.Ordinal),
            "ni ofrecen un gesto que las manos no leen");
        Debe(!texto.Contains("más de dos veces", StringComparison.OrdinalIgnoreCase),
            "la regla escrita de intentos no tolera tres: es la del código, dos");
        bool juntos = false;
        for (int i = texto.IndexOf("which", StringComparison.Ordinal); i >= 0 && !juntos;
             i = texto.IndexOf("which", i + 1, StringComparison.Ordinal))
        {
            int j = texto.IndexOf("map_look", Math.Max(0, i - 700), StringComparison.Ordinal);
            juntos = j >= 0 && Math.Abs(j - i) <= 700;
        }
        Debe(juntos, "y ante varios candidatos mandan MIRAR (map_look) y elegir con `which`, en el mismo "
            + "sitio y antes que preguntar al usuario: mirar tiene que poder desempatar");

        // DOS NUMERACIONES NO SE MEZCLAN (crítico de la rama, 2026-09-11): map_show cuenta los homónimos
        // por su posición en la pantalla y map_take por su selector. Decir que el which de uno vale para
        // el otro es mentir con un número.
        string whichShow = QueDelArgumento("map_show", "which") ?? "";
        Debe(whichShow.Length > 0 && !whichShow.Contains("map_take"),
            "el which de map_show no manda al de map_take: numeran en órdenes distintos");
        Debe((DescripcionDe("map_take") ?? "").Contains("Guardar"),
            "y map_take avisa de que un botón que hace su trabajo sin cambiar de pantalla —Guardar— está "
            + "bien: «no cambió» no siempre es «por aquí no era»");
    }

    private static object? UtensilioDe(string herramienta)
    {
        var t = Cap004("U.WindowsClient.Voice.ConversacionEnVivo");
        var m = t?.GetMethod("Herramientas", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        if (m?.Invoke(null, null) is not System.Collections.IEnumerable todas) return null;
        foreach (var u in todas)
            if (u != null && (string?)u.GetType().GetProperty("Nombre")?.GetValue(u) == herramienta) return u;
        return null;
    }

    private static string? DescripcionDe(string herramienta)
    {
        var u = UtensilioDe(herramienta);
        return u?.GetType().GetProperty("Descripcion")?.GetValue(u) as string;
    }

    private static string? QueDelArgumento(string herramienta, string argumento)
    {
        var u = UtensilioDe(herramienta);
        if (u?.GetType().GetProperty("Args")?.GetValue(u) is not System.Collections.IEnumerable args) return null;
        foreach (var a in args)
            if (a != null && (string?)a.GetType().GetProperty("Nombre")?.GetValue(a) == argumento)
                return a.GetType().GetProperty("Que")?.GetValue(a) as string;
        return null;
    }

    private static void LaManoDiceSiLoLogro()
    {
        // El tope de intentos (204) y la medida del turno (205) leen si una acción se logró de
        // SurfaceMapTools.UltimaMano, no de la prosa. El crítico de la rama (2026-09-11) lo comprobó:
        // sabotear ese dato dejaba el contrato INTACTO —un guardia que se cree puesto, aprendizaje
        // nº18—, y la lista numerada de homónimos contaba como fallo aunque no se pulsara nada.
        var tMano = typeof(SurfaceMapTools).GetNestedType("Mano");
        var pLogro = tMano?.GetProperty("Logro");
        var pIntento = tMano?.GetProperty("Intento");
        var pAmbiguo = typeof(RecorrerSegunElNucleo.Resultado).GetProperty("Ambiguo");
        if (pLogro == null || pIntento == null || pAmbiguo == null)
        { Pendiente("SurfaceMapTools.Mano.Intento · RecorrerSegunElNucleo.Resultado.Ambiguo", "207", "017"); return; }

        var mapa = new SurfaceMapTools(() => null);
        var siguiente = default(RecorrerSegunElNucleo.Resultado);
        mapa.RecorrerPorElNucleo = _ => siguiente;
        (bool Logro, bool Intento)? Toma(RecorrerSegunElNucleo.Resultado r)
        {
            siguiente = r;
            mapa.Call("map_take", new Dictionary<string, string> { ["exit"] = "Descargas" });
            if (mapa.UltimaMano is not { } m) return null;
            object caja = m;
            return ((bool)pLogro.GetValue(caja)!, (bool)pIntento.GetValue(caja)!);
        }
        bool Es((bool Logro, bool Intento)? x, bool logro, bool intento)
            => x.HasValue && x.Value.Logro == logro && x.Value.Intento == intento;

        var navega = Toma(new RecorrerSegunElNucleo.Resultado(1, 1, "uia://x.exe/b", true,
            "hice los 1 paso(s): pulsé «Descargas» y ahora estás en «uia://x.exe/b».", true));
        Debe(Es(navega, logro: true, intento: true), $"pulsar y que cambie la pantalla es un logro (salió {navega})");

        var quieta = Toma(new RecorrerSegunElNucleo.Resultado(1, 1, "uia://x.exe/a", true,
            "hice los 1 paso(s): pulsé «Descargas» y la pantalla no cambió.", false));
        Debe(Es(quieta, logro: false, intento: true),
            $"pulsar y que no cambie nada es un intento que no se logró: es lo que el tope cuenta para no dejar insistir (salió {quieta})");

        var perdida = Toma(new RecorrerSegunElNucleo.Resultado(0, 1, "uia://x.exe/a", false, "«Descargas» no lo conozco en «a»."));
        Debe(Es(perdida, logro: false, intento: true),
            "pedir algo que no está SÍ es un intento fallido: pedirlo otra vez igual es la insistencia que el tope frena");

        object lista = new RecorrerSegunElNucleo.Resultado(0, 1, "uia://x.exe/a", false,
            "hice 0 de 1 y paré en el paso 1: hay 2 puertas vivas para «Descargas»: 1) … 2) …");
        pAmbiguo.SetValue(lista, true);
        var pregunta = Toma((RecorrerSegunElNucleo.Resultado)lista);
        Debe(pregunta.HasValue && !pregunta.Value.Intento,
            "y la lista numerada de homónimos NO es un intento: no se pulsó nada, y contarla como fallo "
            + "frenaba el «pruebo este otro botón» que pide el audio");

        // Y la mano lleva los candidatos de la lista: sin ellos el tope no sabe qué selector es cuál.
        var pCandR = typeof(RecorrerSegunElNucleo.Resultado).GetProperty("Candidatos");
        var pCandM = tMano!.GetProperty("Candidatos");
        if (pCandR == null || pCandM == null) { Pendiente("SurfaceMapTools.Mano.Candidatos", "207", "017"); return; }
        pCandR.SetValue(lista, new[] { "uia:a", "uia:b" });
        Toma((RecorrerSegunElNucleo.Resultado)lista);
        object? mCaja = mapa.UltimaMano;
        var llevados = mCaja == null ? null : pCandM.GetValue(mCaja) as IReadOnlyList<string>;
        Debe(llevados != null && llevados.SequenceEqual(new[] { "uia:a", "uia:b" }),
            "y la mano lleva los candidatos de la lista, en su orden: el tope los necesita para saber qué selector es cuál");

        // Y la mano lleva lo que se pulsó de verdad, y la consulta al tope viaja dentro del paso hasta el
        // ejecutor (promesa 204; quinta pasada del crítico).
        var pPulsR = typeof(RecorrerSegunElNucleo.Resultado).GetProperty("Pulsado");
        var pPulsM = tMano!.GetProperty("Pulsado");
        var pHilo = typeof(SurfaceMapTools).GetProperty("AntesDePulsarEnEsteHilo");
        var pAntesP = typeof(RecorrerSegunElNucleo.Paso).GetProperty("AntesDePulsar");
        if (pPulsR == null || pPulsM == null || pHilo == null || pAntesP == null)
        { Pendiente("SurfaceMapTools.Mano.Pulsado · SurfaceMapTools.AntesDePulsarEnEsteHilo", "207", "017"); return; }
        object pulsada = new RecorrerSegunElNucleo.Resultado(1, 1, "uia://x.exe/a", true,
            "hice los 1 paso(s): pulsé «Descargas» y la pantalla no cambió.", false);
        pPulsR.SetValue(pulsada, "s:1");
        Toma((RecorrerSegunElNucleo.Resultado)pulsada);
        object? mP = mapa.UltimaMano;
        Debe(mP != null && (string?)pPulsM.GetValue(mP) == "s:1", "la mano dice qué selector se pulsó de verdad");

        Func<string, string?> consulta = _ => null;
        RecorrerSegunElNucleo.Paso? visto = null;
        mapa.RecorrerPorElNucleo = pasos => { visto = pasos[0]; return siguiente; };
        pHilo.SetValue(mapa, consulta);
        mapa.Call("map_take", new Dictionary<string, string> { ["exit"] = "Descargas" });
        pHilo.SetValue(mapa, null);
        Debe(visto != null && ReferenceEquals(pAntesP.GetValue(visto), consulta),
            "y la consulta al tope que la voz pone en su hilo viaja dentro del paso hasta el ejecutor");
    }

    /// <remarks>
    /// LO ESCRITO NO PEDÍA RESPUESTA, y el micrófono lo tapaba. <c>EnviarTextoAsync</c> mandaba el texto y
    /// nada más; sin un response.create detrás el modelo no contesta — cero eventos, medido contra el
    /// servidor el 2026-09-12 —, así que Ü solo respondía a lo escrito si el micrófono oía algo a la vez.
    /// Invalidó el nivel 4 del 2026-09-11, y el saludo de la presentación tenía el mismo agujero.
    /// </remarks>
    private static void EscribirPideRespuesta()
    {
        var m = Cap004("U.WindowsClient.Voice.ConversacionEnVivo")
            ?.GetMethod("MensajesDeTexto", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (m == null) { Pendiente("Voice.ConversacionEnVivo.MensajesDeTexto", "208", "018"); return; }
        List<string> Mensajes(Voz.Realtime.IProtocolo p, string texto)
            => ((System.Collections.IEnumerable?)m.Invoke(null, new object[] { p, texto }) ?? Array.Empty<string>())
                .Cast<object?>().Select(o => o as string ?? "").ToList();
        string Tipos(List<string> l) => string.Join(" · ", l.Select(TipoDelMensaje));

        var rt = Mensajes(new Voz.Realtime.ProtocoloOpenAI(), "abre el bloc de notas");
        Debe(rt.Count == 2 && TipoDelMensaje(rt[0]) == "conversation.item.create" && rt[0].Contains("abre el bloc de notas")
             && TipoDelMensaje(rt[1]) == "response.create",
            $"con GPT Realtime van el texto y DESPUÉS response.create, en ese orden (salió: {Tipos(rt)})");

        var tLive = typeof(Voz.Realtime.IProtocolo).Assembly.GetType("Voz.Realtime.ProtocoloGptLive");
        if (tLive == null) { Pendiente("Voz.Realtime.ProtocoloGptLive", "208", "018"); return; }
        var live = (Voz.Realtime.IProtocolo)Activator.CreateInstance(tLive,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.CreateInstance | BindingFlags.OptionalParamBinding,
            null, new[] { Type.Missing, Type.Missing }, null)!;
        var gl = Mensajes(live, "abre el bloc de notas");
        Debe(gl.Count == 2 && TipoDelMensaje(gl[0]) == "response.item.create" && gl[0].Contains("abre el bloc de notas")
             && TipoDelMensaje(gl[1]) == "response.create",
            $"con GPT-Live van el mensaje del usuario y DESPUÉS response.create, en ese orden (salió: {Tipos(gl)})");

        var solo = Mensajes(new ProtocoloDeMentira(pideRespuesta: false, marcaLosTurnos: true), "hola");
        Debe(solo.Count == 1 && solo[0].Length > 0,
            $"con un protocolo que contesta solo (PedirRespuesta vacío) va un único mensaje y ninguno vacío (salieron {solo.Count})");

        // ── Y LA CONVERSACIÓN LO MANDA DE VERDAD (W208, 2026-09-12) ──────────
        // Juzgar solo MensajesDeTexto dejaba verde la vuelta a main: EnviarTextoAsync mandando solo el texto
        // dio CONTRATO INTACTO (medido), porque el método sale si no hay socket y aquí no lo hay. La
        // conversación deja sustituir su salida y el «hay socket», y se juzga lo que sale de verdad: lo
        // escrito (EnviarTextoAsync, que usan «Escríbele…» y el saludo) y la nota al modelo.
        var tc = Cap004("U.WindowsClient.Voice.ConversacionEnVivo");
        var salida = tc?.GetField("_puerta", BindingFlags.NonPublic | BindingFlags.Instance);
        var abierta = tc?.GetField("_puertaAbierta", BindingFlags.NonPublic | BindingFlags.Instance);
        var escribir = tc?.GetMethod("EnviarTextoAsync", BindingFlags.Public | BindingFlags.Instance);
        var nota = tc?.GetMethod("EnviarTextoAlModeloAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        if (tc == null || salida == null || abierta == null || escribir == null || nota == null)
        { Pendiente("ConversacionEnVivo._puerta y _puertaAbierta (lo que la conversación manda al escribir)", "208", "018"); return; }

        List<string> Manda(Voz.Realtime.IProtocolo p, MethodInfo metodo, bool conLaVozAbierta)
        {
            using var conv = (IDisposable)Activator.CreateInstance(tc, new object?[] { new SurfaceMapTools(() => null), p })!;
            var mandados = new List<string>();
            salida.SetValue(conv, (Func<string, CancellationToken, Task>)((json, _) =>
            {
                lock (mandados) mandados.Add(json);
                return Task.CompletedTask;
            }));
            if (conLaVozAbierta) abierta.SetValue(conv, (Func<bool>)(() => true));
            ((Task)metodo.Invoke(conv, new object[] { "abre el bloc de notas" })!).GetAwaiter().GetResult();
            lock (mandados) return mandados.ToList();
        }

        foreach (var (quien, p, tipoDelTexto) in new (string, Voz.Realtime.IProtocolo, string)[]
                 {
                     ("con GPT Realtime", new Voz.Realtime.ProtocoloOpenAI(), "conversation.item.create"),
                     ("con GPT-Live", live, "response.item.create"),
                 })
            foreach (var (como, metodo) in new (string, MethodInfo)[]
                     { ("lo escrito (EnviarTextoAsync)", escribir), ("la nota al modelo (EnviarTextoAlModeloAsync)", nota) })
            {
                var l = Manda(p, metodo, conLaVozAbierta: true);
                Debe(l.Count == 2 && TipoDelMensaje(l[0]) == tipoDelTexto && l[0].Contains("abre el bloc de notas")
                     && TipoDelMensaje(l[1]) == "response.create",
                    $"{quien}, {como} sale de la conversación con el texto y DESPUÉS response.create (salió: {Tipos(l)})");
            }

        var cerrada = Manda(new Voz.Realtime.ProtocoloOpenAI(), escribir, conLaVozAbierta: false);
        Debe(cerrada.Count == 0,
            $"y con la voz cerrada no sale nada: sustituir la salida no abre la puerta por su cuenta (salieron {cerrada.Count})");
    }

    /// <remarks>
    /// VARIAS LLAMADAS A LA VEZ, Y UN PEDIR TURNO POR CADA UNA. GPT-Live entrega cada function_call del
    /// delegado en su propio response.event, y la conversación contestaba cada Hecho.Pide por separado con su
    /// propio response.create. El primero llegaba sin la salida de la otra llamada y el servidor contestaba
    /// function_call_outputs_required (medido con sonda-paralelo.ps1 el 2026-09-12): el log decía un fallo
    /// donde no lo había (aprendizaje nº2). La mano de la prueba es el autocontrol, que se puede retener sin
    /// pantalla: así las dos llamadas están pedidas antes de que termine ninguna, que es lo que pasa cuando
    /// una herramienta tarda más de lo que tarda en llegar la siguiente.
    /// </remarks>
    private static void VariasLlamadasUnSoloTurno()
    {
        var tc = Cap004("U.WindowsClient.Voice.ConversacionEnVivo");
        var salida = tc?.GetField("_puerta", BindingFlags.NonPublic | BindingFlags.Instance);
        var abierta = tc?.GetField("_puertaAbierta", BindingFlags.NonPublic | BindingFlags.Instance);
        var procesar = tc?.GetMethod("Procesar", BindingFlags.NonPublic | BindingFlags.Instance);
        var nucleo = tc?.GetMethod("EjecutarNucleoAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        var sesion = tc?.GetField("_sesionId", BindingFlags.NonPublic | BindingFlags.Instance);
        var autocontrol = tc?.GetProperty("Autocontrol", BindingFlags.Public | BindingFlags.Instance);
        if (tc == null || salida == null || abierta == null || procesar == null || nucleo == null || sesion == null || autocontrol == null)
        { Pendiente("ConversacionEnVivo._puerta y _puertaAbierta (lo que la conversación manda tras las herramientas)", "214", "018"); return; }
        var tLive = typeof(Voz.Realtime.IProtocolo).Assembly.GetType("Voz.Realtime.ProtocoloGptLive");
        if (tLive == null) { Pendiente("Voz.Realtime.ProtocoloGptLive", "214", "018"); return; }
        var live = (Voz.Realtime.IProtocolo)Activator.CreateInstance(tLive,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.CreateInstance | BindingFlags.OptionalParamBinding,
            null, new[] { Type.Missing, Type.Missing }, null)!;

        // Tal como la manda el servidor: una llamada por response.event (sonda-paralelo.ps1).
        string LlegaLaLlamada(string id, string nombre)
            => "{\"type\":\"response.event\",\"event\":{\"type\":\"response.output_item.done\",\"item\":{\"type\":\"function_call\","
               + "\"call_id\":\"" + id + "\",\"name\":\"" + nombre + "\",\"arguments\":\"{}\"}}}";
        string? ItemDe(string json, string campo)
        {
            try
            {
                using var d = JsonDocument.Parse(json);
                return d.RootElement.TryGetProperty("item", out var it) && it.ValueKind == JsonValueKind.Object
                       && it.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            }
            catch (JsonException) { return null; }
        }
        bool EsSalida(string json) => ItemDe(json, "type") == "function_call_output";
        bool EsTurno(string json) => TipoDelMensaje(json) == "response.create";
        string Tipos(List<string> l) => string.Join(" · ", l.Select(m => EsSalida(m) ? "salida " + ItemDe(m, "call_id") : TipoDelMensaje(m)));

        List<string> Conversa(Voz.Realtime.IProtocolo p, Action<object> guion, int salidasEsperadas)
        {
            using var conv = (IDisposable)Activator.CreateInstance(tc, new object?[] { new SurfaceMapTools(() => null), p })!;
            var mandados = new List<string>();
            salida.SetValue(conv, (Func<string, CancellationToken, Task>)((json, _) =>
            {
                lock (mandados) mandados.Add(json);
                return Task.CompletedTask;
            }));
            abierta.SetValue(conv, (Func<bool>)(() => true));
            guion(conv);
            // Las llamadas corren en otro hilo: se espera a que salgan sus resultados y un pedir turno, y un
            // poco más, por si detrás sale otro de más.
            var reloj = System.Diagnostics.Stopwatch.StartNew();
            while (reloj.ElapsedMilliseconds < 5000)
            {
                lock (mandados)
                    if (mandados.Count(EsSalida) >= salidasEsperadas && mandados.Any(EsTurno)) break;
                Thread.Sleep(20);
            }
            Thread.Sleep(300);
            lock (mandados) return mandados.ToList();
        }
        Func<string, string> Contesta = n => n + ": hecho";

        using var suelta = new ManualResetEventSlim(false);
        var dos = Conversa(live, conv =>
        {
            autocontrol.SetValue(conv, (Func<string, string>)(n => { suelta.Wait(5000); return n + ": hecho"; }));
            procesar.Invoke(conv, new object[] { LlegaLaLlamada("call_A", "self_mute"), CancellationToken.None });
            procesar.Invoke(conv, new object[] { LlegaLaLlamada("call_B", "self_hide"), CancellationToken.None });
            suelta.Set();
        }, salidasEsperadas: 2);
        var ids = dos.Where(EsSalida).Select(m => ItemDe(m, "call_id")).ToList();
        Debe(ids.Count == 2 && ids.Contains("call_A") && ids.Contains("call_B"),
            $"con GPT-Live, dos llamadas pedidas a la vez se contestan las dos, cada una con su function_call_output (salió: {Tipos(dos)})");
        Debe(dos.Count(EsTurno) == 1,
            $"y el turno se pide UNA vez, no una por llamada: el primero salía sin la otra salida y el servidor lo rechazaba (salió: {Tipos(dos)})");
        Debe(dos.FindIndex(m => EsTurno(m)) > dos.FindLastIndex(m => EsSalida(m)),
            $"y se pide detrás de la última salida, cuando ya no falta ninguna (salió: {Tipos(dos)})");

        var una = Conversa(live, conv =>
        {
            autocontrol.SetValue(conv, Contesta);
            procesar.Invoke(conv, new object[] { LlegaLaLlamada("call_C", "self_mute"), CancellationToken.None });
        }, salidasEsperadas: 1);
        Debe(una.Count(EsSalida) == 1 && una.Count(EsTurno) == 1 && una.Count > 0 && EsTurno(una[^1]),
            $"una llamada sola sigue pidiendo turno detrás de su resultado (salió: {Tipos(una)})");

        var tras = Conversa(live, conv =>
        {
            autocontrol.SetValue(conv, Contesta);
            // Pedida justo cuando se cerraba la voz: el token ya estaba cancelado y la llamada nunca llegó a correr.
            using (var cancelado = new CancellationTokenSource())
            {
                cancelado.Cancel();
                procesar.Invoke(conv, new object[] { LlegaLaLlamada("call_vieja", "self_mute"), cancelado.Token });
            }
            sesion.SetValue(conv, "la sesión siguiente");
            procesar.Invoke(conv, new object[] { LlegaLaLlamada("call_D", "self_hide"), CancellationToken.None });
        }, salidasEsperadas: 1);
        Debe(tras.Count(EsSalida) == 1 && ItemDe(tras.First(EsSalida), "call_id") == "call_D" && tras.Count(EsTurno) == 1,
            $"una llamada de una sesión anterior que nunca llegó a correr no retiene el turno de la sesión siguiente (salió: {Tipos(tras)})");

        var rt = Conversa(new Voz.Realtime.ProtocoloOpenAI(), conv =>
        {
            autocontrol.SetValue(conv, Contesta);
            var tanda = new List<Voz.Realtime.Llamada>
            {
                new("call_E", "self_mute", new Dictionary<string, string>()),
                new("call_F", "self_hide", new Dictionary<string, string>()),
            };
            ((Task)nucleo.Invoke(conv, new object[] { tanda, CancellationToken.None })!).GetAwaiter().GetResult();
        }, salidasEsperadas: 2);
        Debe(rt.Count(EsSalida) == 2 && rt.Count(EsTurno) == 1 && rt.Count > 0 && EsTurno(rt[^1]),
            $"con GPT Realtime una tanda de dos sigue como estaba: sus dos resultados y detrás un response.create (salió: {Tipos(rt)})");
    }

    private static string TipoDelMensaje(string json)
    {
        if (json.Length == 0) return "(vacío)";
        try
        {
            using var d = JsonDocument.Parse(json);
            return d.RootElement.TryGetProperty("type", out var v) ? v.GetString() ?? "" : "(sin type)";
        }
        catch (JsonException) { return "(no es JSON)"; }
    }

    /// <remarks>
    /// SIN MARCAS NO SE CIERRA NADA, y nada da error. GPT-Live no manda speech_started ni response.done
    /// (medido el 2026-09-12): sin cierre, TurnoCerrado, Cerro, la línea «Ü dijo» y la frase que recibe
    /// quien aprende (promesa 105) o el piloto no llegan nunca. La regla se juzga con reloj de mentira, y
    /// después su USO dentro de la conversación: una regla sin cablear es un guardia que se cree puesto
    /// (aprendizaje nº18).
    /// </remarks>
    private static void SinMarcasLaConversacionMarcaLosTurnos()
    {
        var t = Cap004("U.WindowsClient.Voice.TurnosSinMarca");
        var oye = t?.GetMethod("Oye");
        var toca = t?.GetMethod("TocaCerrar");
        if (t == null || oye == null || toca == null) { Pendiente("Voice.TurnosSinMarca (Oye, TocaCerrar)", "209", "018"); return; }

        long ahora = 0;
        Func<long> reloj = () => ahora;
        object Nuevo(object silencio) => Activator.CreateInstance(t,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.CreateInstance | BindingFlags.OptionalParamBinding,
            null, new[] { reloj, silencio }, null)!;
        var pcm = new byte[480];
        bool Oye(object turnos, Voz.Realtime.Hecho h) => (bool)oye.Invoke(turnos, new object[] { h })!;
        // El audio llega SIN PARAR, también en silencio (medido): se le da un trozo antes de cada pregunta.
        bool Cierra(object turnos) { Oye(turnos, new Voz.Realtime.Hecho.Suena(pcm)); return (bool)toca.Invoke(turnos, null)!; }
        Voz.Realtime.Hecho Usuario(string s) => new Voz.Realtime.Hecho.DiceElUsuario(s);
        Voz.Realtime.Hecho DeU(string s) => new Voz.Realtime.Hecho.DiceU(s);

        var turnos = Nuevo(Type.Missing);
        ahora = 0;
        Debe(!Cierra(turnos), "recién nacido no hay nada que cerrar");
        ahora = 60_000;
        Debe(!Cierra(turnos), "sin nada oído no se cierra nunca, aunque llegue audio un minuto entero: el audio continuo no es actividad");

        ahora = 100_000; bool abre = Oye(turnos, Usuario("abre el"));
        ahora = 100_300; bool abreOtra = Oye(turnos, Usuario(" bloc de notas"));
        Debe(abre && !abreOtra, "el primer trozo de lo que dice el usuario abre un turno, y el segundo de la misma frase no");
        // EL SILENCIO POR DEFECTO SON 2000 ms, MEDIDOS (2026-09-12, sonda de los turnos contra GPT-Live, 3
        // corridas): entre devolver el resultado de una herramienta y lo siguiente que dice Ü pasan 1658-1707 ms
        // (4 de 4). Con 1500 el turno se cerraba a mitad de la tarea; ver la 211 y la spec 018.
        ahora = 101_300;
        Debe(!Cierra(turnos), "a 1000 ms del último trozo no toca cerrar");
        ahora = 102_000;
        Debe(!Cierra(turnos), "a 2000 ms del PRIMER trozo tampoco: el silencio se cuenta desde el último");
        // NI UN MILISEGUNDO ANTES (revisión contrato r2, 2026-09-13). Con solo 1000 y 2000, cualquier silencio por
        // defecto entre 1701 y 2000 pasaba: con 1705, CONTRATO INTACTO (medido). Y justo ahí caen dos de los cuatro
        // huecos que fijaron los 2000 —1705 y 1707 ms entre devolver una herramienta y lo siguiente que dice Ü—: con
        // 1705 el turno se volvería a cerrar a mitad de la tarea, que es la regresión que los 2000 arreglaron.
        ahora = 102_299;
        Debe(!Cierra(turnos), "a 1999 ms del último trozo todavía no toca cerrar: el silencio por defecto son los 2000 ms medidos, no algo menos");
        ahora = 102_300; bool cierra = Cierra(turnos);
        ahora = 102_400; bool otraVez = Cierra(turnos);
        Debe(cierra && !otraVez, "a 2000 ms del último trozo toca cerrar, y una sola vez");

        ahora = 102_500;
        Debe(!Oye(turnos, DeU("Listo, ")), "lo que dice Ü no abre un turno del usuario");
        ahora = 104_000;
        Debe(!Cierra(turnos), "lo que dice Ü también cuenta como actividad");
        ahora = 104_500;
        Debe(Cierra(turnos), "y también se cierra por silencio: sin eso la línea «Ü dijo» no se escribe nunca");

        ahora = 110_000;
        Debe(Oye(turnos, Usuario("mira la pantalla")), "tras un cierre, lo siguiente que dice el usuario abre otro turno");
        ahora = 111_000; Oye(turnos, DeU("Veo SAP."));
        ahora = 112_500;
        Debe(!Cierra(turnos), "mientras Ü sigue hablando el turno no se cierra, aunque el usuario lleve 2500 ms callado");
        ahora = 113_000;
        Debe(Cierra(turnos), "y se cierra a 2000 ms de lo último que dijo cualquiera de los dos");

        var corto = Nuevo(500);
        ahora = 200_000; Oye(corto, Usuario("sí"));
        ahora = 200_400; bool a400 = Cierra(corto);
        ahora = 200_500; bool a500 = Cierra(corto);
        Debe(!a400 && a500, "un silencio configurado de 500 ms se respeta");

        // ── Y LA CONVERSACIÓN LA USA, TAL COMO LA CONSTRUYE ──────────────────
        // Por la misma puerta que el socket: Procesar recibe el JSON tal como llega. Se cambia SOLO el
        // reloj de los turnos, nunca el marcador. Hasta el 2026-09-12 se cambiaba el marcador entero por uno
        // del contrato, y eso dejaba sin juez con qué silencio y con qué reloj lo construye la app: la
        // revisión lo midió con dos sabotajes que dejaban el contrato INTACTO (G1, construir con 60 000 ms,
        // y G4, reutilizar el marcador de la sesión anterior). El «tic» es un mensaje que NO TRAE HECHOS y no
        // suena, así que no abre el altavoz: el silencio de ceros (TicSinHechos). Hasta el 2026-09-12 era un
        // session.usage.updated, y desde la 48 de la voz ese mensaje trae un Hecho.Duracion: la 209 dejó de pasar
        // por el camino «sin hechos» sin que nada se pusiera rojo. Medido con SG209i (consultar el marcador solo
        // cuando el mensaje trae hechos): ✔ 209, y solo la 211 y la 212 en rojo.
        var tc = Cap004("U.WindowsClient.Voice.ConversacionEnVivo");
        var procesar = tc?.GetMethod("Procesar", BindingFlags.NonPublic | BindingFlags.Instance);
        var campo = tc?.GetField("_turnosSinMarca", BindingFlags.NonPublic | BindingFlags.Instance);
        var campoReloj = tc?.GetField("_relojDeLosTurnos", BindingFlags.NonPublic | BindingFlags.Instance);
        var deSesionNueva = tc?.GetMethod("EmpezarLosTurnosDeLaSesion", BindingFlags.NonPublic | BindingFlags.Instance);
        var anotado = Cap004("U.WindowsClient.Diagnostics.LogBus")?.GetEvent("Anotado");
        if (tc == null || procesar == null || campo == null || anotado == null)
        { Pendiente("ConversacionEnVivo._turnosSinMarca y su uso en Procesar", "209", "018"); return; }
        if (campoReloj == null || deSesionNueva == null)
        { Pendiente("ConversacionEnVivo._relojDeLosTurnos y EmpezarLosTurnosDeLaSesion (el marcador tal como lo construye la app)", "209", "018"); return; }
        var tLive = typeof(Voz.Realtime.IProtocolo).Assembly.GetType("Voz.Realtime.ProtocoloGptLive");
        if (tLive == null) { Pendiente("Voz.Realtime.ProtocoloGptLive", "209", "018"); return; }
        string tic = TicSinHechos;

        (bool TieneMarcador, bool RelojDelSistema, int CerroAntes, int CerroA2000, int CerroDespues, List<string> Dijo, bool AbrioPorVoz) Conversa(
            Voz.Realtime.IProtocolo protocolo, string trozo)
        {
            using var conv = (IDisposable)Activator.CreateInstance(tc, new object?[] { new SurfaceMapTools(() => null), protocolo })!;
            int cerro = 0;
            var dijo = new List<string>();
            bool abrioPorVoz = false;
            tc.GetEvent("Cerro")!.AddEventHandler(conv, (Action)(() => cerro++));
            tc.GetEvent("DijoElUsuario")!.AddEventHandler(conv, (Action<string>)(s => dijo.Add(s)));
            Action<string, string> oyeLog = (tag, msg) => { if (tag == "voz-turno" && msg.Contains("por voz")) abrioPorVoz = true; };
            anotado.AddEventHandler(null, oyeLog);
            try
            {
                bool tiene = campo.GetValue(conv) != null;
                // EL RELOJ DE LA APP ES EL DEL SISTEMA: el mismo origen que Environment.TickCount64, y avanza
                // con él. Con un reloj parado (() => 0) los turnos no se cerrarían nunca, sin error.
                var deLaApp = campoReloj.GetValue(conv) as Func<long>;
                long a = deLaApp?.Invoke() ?? long.MinValue, sistema = Environment.TickCount64;
                Thread.Sleep(60);
                long b = deLaApp?.Invoke() ?? long.MinValue;
                bool delSistema = deLaApp != null && Math.Abs(a - sistema) < 250 && b - a >= 30;
                campoReloj.SetValue(conv, reloj);
                void Llega(string json) => procesar.Invoke(conv, new object[] { json, CancellationToken.None });
                // A 1999 ms, y no solo a 1700 (revisión contrato r2, 2026-09-13): construir el marcador con 1705 ms
                // pasaba por aquí igual que con 2000. Y a 2000 exactos, no solo a 2100: con 2050 también pasaba.
                ahora = 300_000; Llega(trozo);
                ahora = 301_000; Llega(tic);
                ahora = 301_700; Llega(tic);
                ahora = 301_999; Llega(tic);
                int antes = cerro;
                ahora = 302_000; Llega(tic);
                int a2000 = cerro;
                ahora = 302_100; Llega(tic);
                return (tiene, delSistema, antes, a2000, cerro, dijo, abrioPorVoz);
            }
            finally { anotado.RemoveEventHandler(null, oyeLog); }
        }

        var live = (Voz.Realtime.IProtocolo)Activator.CreateInstance(tLive,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.CreateInstance | BindingFlags.OptionalParamBinding,
            null, new[] { Type.Missing, Type.Missing }, null)!;
        var conLive = Conversa(live, "{\"type\":\"session.input_transcript.delta\",\"delta\":\"abre el bloc de notas\"}");
        Debe(conLive.TieneMarcador, "con GPT-Live la conversación lleva su marcador de turnos");
        Debe(conLive.RelojDelSistema, "y lo construye con el reloj del sistema: el mismo origen que Environment.TickCount64, y avanza con él");
        Debe(conLive.AbrioPorVoz, "y el primer trozo de lo que dice el usuario abre un turno en la conversación (línea voz-turno «por voz»)");
        Debe(conLive.CerroAntes == 0 && conLive.CerroA2000 == 1 && conLive.CerroDespues == 1,
            $"con el silencio con que la construye la app, los 2000 ms medidos: a 1999 ms no cierra, a 2000 ms cierra, y una sola vez, sin que el servidor mande nada (cerró {conLive.CerroAntes}, {conLive.CerroA2000} y luego {conLive.CerroDespues})");
        Debe(conLive.Dijo.Count == 1 && conLive.Dijo[0] == "abre el bloc de notas",
            $"y al cerrar entrega lo que dijo el usuario, que es lo que leen quien aprende y el piloto (entregó {conLive.Dijo.Count})");

        // CADA SESIÓN EMPIEZA SIN LO DICHO EN LA ANTERIOR: el marcador se recrea, no se reutiliza. Si se
        // reutilizara, cerrar la voz a menos de 2 s de que hablara el usuario dejaría un cierre fantasma
        // (Cerro y TurnoCerrado) en la primera respuesta de la sesión siguiente.
        using (var otra = (IDisposable)Activator.CreateInstance(tc, new object?[] { new SurfaceMapTools(() => null), live })!)
        {
            int cerro = 0;
            tc.GetEvent("Cerro")!.AddEventHandler(otra, (Action)(() => cerro++));
            campoReloj.SetValue(otra, reloj);
            ahora = 500_000; procesar.Invoke(otra, new object[] { "{\"type\":\"session.input_transcript.delta\",\"delta\":\"de la sesión anterior\"}", CancellationToken.None });
            object? marcadorViejo = campo.GetValue(otra);
            deSesionNueva.Invoke(otra, null);
            object? marcadorNuevo = campo.GetValue(otra);
            ahora = 503_000; procesar.Invoke(otra, new object[] { tic, CancellationToken.None });
            Debe(marcadorNuevo != null && !ReferenceEquals(marcadorViejo, marcadorNuevo) && cerro == 0,
                $"al empezar otra sesión el marcador es otro: lo dicho en la anterior no cierra un turno de esta (cerró {cerro})");
        }

        var conMarca = Conversa(new ProtocoloDeMentira(pideRespuesta: true, marcaLosTurnos: true),
            "{\"type\":\"trozo\",\"delta\":\"abre el bloc de notas\"}");
        Debe(!conMarca.TieneMarcador && conMarca.CerroDespues == 0,
            "con una voz que marca sus turnos la conversación no inventa otro cierre: los pone el servidor");
        var conRealtime = Conversa(new Voz.Realtime.ProtocoloOpenAI(),
            "{\"type\":\"conversation.item.input_audio_transcription.delta\",\"delta\":\"abre el bloc de notas\"}");
        Debe(!conRealtime.TieneMarcador && conRealtime.CerroDespues == 0,
            "y con GPT Realtime, que marca los suyos, tampoco: ni marcador ni cierre inventado");
    }

    /// <remarks>
    /// EL DEFECTO ES LO QUE DECIDE LA MIGRACIÓN. Sin esto existen el protocolo, los turnos y lo escrito, y
    /// la voz que abre la app sigue siendo la de antes — FaceWindow construye la conversación sin decir
    /// protocolo. Y la vuelta atrás tiene que ser una variable, sin recompilar.
    /// </remarks>
    private static void LaVozPorDefectoEsGptLive()
    {
        var tc = Cap004("U.WindowsClient.Voice.ConversacionEnVivo");
        var m = tc?.GetMethod("ProtocoloPorDefecto", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (tc == null || m == null) { Pendiente("Voice.ConversacionEnVivo.ProtocoloPorDefecto", "210", "018"); return; }

        // LO QUE ABRE, NO CÓMO SE LLAMA (2026-09-12). Mirar solo el nombre del tipo dejaba verde abrir con
        // un modelo que no existe: con «new ProtocoloGptLive("gpt-live-1-mini")» en ProtocoloPorDefecto
        // salieron ✔ 210 y CONTRATO INTACTO, exit 0 (sabotaje G2 de la revisión, repetido en esta rama), y
        // en U.exe cada session.start pediría ese modelo y la voz no abriría. La 40 no lo ve: juzga una
        // instancia que construye ella. Se juzga lo que manda la apertura: modelo, delegado y dirección.
        const string gptLive = "ProtocoloGptLive · modelo gpt-live-1 · delegado gpt-5.6-luna · wss://api.openai.com/v1/live/sessions";
        const string realtime = "ProtocoloOpenAI · modelo gpt-realtime-2.1-mini · wss://api.openai.com/v1/realtime?model=gpt-realtime-2.1-mini";
        static string Abre(object? p)
        {
            if (p is not Voz.Realtime.IProtocolo proto) return p == null ? "(nada)" : $"(no es un protocolo: {p.GetType().Name})";
            // El delegado solo existe en GPT-Live, y se pide por nombre: si desaparece, la cadena ya no cuadra.
            string delegado = proto.GetType().GetProperty("Delegado")?.GetValue(proto) is string d ? $" · delegado {d}" : "";
            return $"{proto.GetType().Name} · modelo {proto.Modelo}{delegado} · {proto.Direccion().AbsoluteUri}";
        }

        var preguntadas = new List<string>();
        string Sale(string? valor)
        {
            Func<string, string?> variable = n => { preguntadas.Add(n); return valor; };
            return Abre(m.Invoke(null, new object[] { variable }));
        }
        foreach (var (valor, como) in new (string?, string)[]
                 { (null, "sin U_VOZ"), ("", "con U_VOZ vacío"), ("   ", "con U_VOZ en blanco"), ("gpt-live", "con U_VOZ=gpt-live") })
        {
            string s = Sale(valor);
            Debe(s == gptLive, $"{como} la voz es GPT-Live y abre {gptLive} (abre {s})");
        }
        string rt = Sale("realtime");
        Debe(rt == realtime, $"con U_VOZ=realtime vuelve GPT Realtime y abre {realtime} (abre {rt})");
        string rtEscrito = Sale(" Realtime ");
        Debe(rtEscrito == realtime, $"y escrito a mano, con mayúscula o espacios, también: lo teclea una persona en setx (abre {rtEscrito})");
        string raro = Sale("gemini");
        Debe(raro == gptLive, $"un valor que no se conoce no elige otra voz por su cuenta: abre la de por defecto (abre {raro})");
        Debe(preguntadas.Count > 0 && preguntadas.All(n => n == "U_VOZ"),
            $"y la variable que se pregunta es U_VOZ (se preguntó: {string.Join(", ", preguntadas.Distinct())})");

        // EL CONSTRUCTOR SIN PROTOCOLO, que es como lo llama FaceWindow. Construirlo no abre ni micrófono
        // ni altavoz (LiveAudio se crea sin dispositivo), así que se juzga aquí y no se deja dicho.
        //
        // Y LA LÍNEA QUE AVISA DE UN VALOR DESCONOCIDO. La spec decía que la juzgaba la 210 y no la miraba
        // nadie: con la condición del aviso invertida salieron ✔ 210 y CONTRATO INTACTO, exit 0 (sabotaje G3
        // de la revisión). Sin esa línea, «setx U_VOZ gemini» no deja rastro y parece haber funcionado.
        var campo = tc.GetField("_protocolo", BindingFlags.NonPublic | BindingFlags.Instance);
        var anotado = Cap004("U.WindowsClient.Diagnostics.LogBus")?.GetEvent("Anotado");
        if (anotado == null) { Pendiente("Diagnostics.LogBus.Anotado", "210", "018"); return; }
        var avisos = new List<string>();
        Action<string, string> oyeLog = (tag, msg) => { if (tag == "voz-viva" && msg.Contains("U_VOZ")) lock (avisos) avisos.Add(msg); };
        string? antes = Environment.GetEnvironmentVariable("U_VOZ");
        anotado.AddEventHandler(null, oyeLog);
        try
        {
            foreach (var (valor, esperado) in new (string?, string)[] { (null, gptLive), ("realtime", realtime), ("gemini", gptLive) })
            {
                lock (avisos) avisos.Clear();
                Environment.SetEnvironmentVariable("U_VOZ", valor);
                string como = valor == null ? "sin U_VOZ" : "con U_VOZ=" + valor;
                using var conv = (IDisposable)Activator.CreateInstance(tc, new object?[] { new SurfaceMapTools(() => null), null })!;
                string abre = campo == null ? "(no encuentro _protocolo)" : Abre(campo.GetValue(conv));
                Debe(abre == esperado, $"construida sin protocolo, {como} la conversación abre {esperado} (abre {abre})");
                string[] dichos; lock (avisos) dichos = avisos.ToArray();
                if (valor == "gemini")
                    Debe(dichos.Length == 1 && dichos[0].Contains("«gemini»"),
                        $"{como} el constructor deja UNA línea voz-viva que nombra «gemini»: la variable se ignoró y el log lo dice (dejó {dichos.Length}: {string.Join(" | ", dichos)})");
                else
                    Debe(dichos.Length == 0,
                        $"{como} no deja ninguna línea voz-viva sobre U_VOZ: no hay nada que avisar (dejó {dichos.Length}: {string.Join(" | ", dichos)})");
            }
        }
        finally
        {
            anotado.RemoveEventHandler(null, oyeLog);
            Environment.SetEnvironmentVariable("U_VOZ", antes);
        }
    }

    /// <remarks>
    /// EL LOG ES LA FUENTE DE VERDAD, y un log inundado no la dice. Medido el 2026-09-12 contra
    /// /v1/live/sessions (sonda de solo lectura, un turno delegado: mirar la pantalla y contestar en cinco
    /// frases): 379 mensajes, 98 sin hechos —cada uno una línea «← …» de hasta 400 caracteres— y 78 de esos
    /// 98 eran deltas del delegado, uno por ficha: 50 response.output_text.delta y 28
    /// response.function_call_arguments.delta. Con cinco turnos así el anillo de 500 líneas del panel pierde
    /// las voz-turno, los topes y las «llamada recibida». Lo demás se sigue volcando: un evento que no se
    /// traduce es justo lo que hay que poder ver.
    ///
    /// Se juzga por la puerta del socket (Procesar, que es donde se escribe la línea) y la regla, pura.
    /// </remarks>
    private static void ElLogNoSeInundaConLosDeltas()
    {
        var tc = Cap004("U.WindowsClient.Voice.ConversacionEnVivo");
        var procesar = tc?.GetMethod("Procesar", BindingFlags.NonPublic | BindingFlags.Instance);
        var anotado = Cap004("U.WindowsClient.Diagnostics.LogBus")?.GetEvent("Anotado");
        var tLive = typeof(Voz.Realtime.IProtocolo).Assembly.GetType("Voz.Realtime.ProtocoloGptLive");
        if (tc == null || procesar == null || anotado == null || tLive == null)
        { Pendiente("ConversacionEnVivo.Procesar, LogBus.Anotado y Voz.Realtime.ProtocoloGptLive", "217", "018"); return; }

        // Con la forma que mandó el servidor en las sondas, recortados a lo que se mira.
        const string textoDelta = "{\"type\":\"response.event\",\"delegation_id\":\"item_1\",\"event\":{\"type\":\"response.output_text.delta\",\"delta\":\"Veo\",\"sequence_number\":12}}";
        const string argumentosDelta = "{\"type\":\"response.event\",\"delegation_id\":\"item_1\",\"event\":{\"type\":\"response.function_call_arguments.delta\",\"delta\":\"{\\\"que\",\"sequence_number\":40}}";
        const string audioVacio = "{\"type\":\"session.output_audio.delta\",\"delta\":\"\"}";
        const string completado = "{\"type\":\"response.event\",\"delegation_id\":\"item_1\",\"event\":{\"type\":\"response.completed\",\"response\":{\"id\":\"resp_1\",\"status\":\"completed\"}}}";
        const string argumentosHechos = "{\"type\":\"response.event\",\"delegation_id\":\"item_1\",\"event\":{\"type\":\"response.function_call_arguments.done\",\"arguments\":\"{}\",\"item_id\":\"fc_1\"}}";
        const string delegacion = "{\"type\":\"session.delegation.created\",\"delegation\":{\"id\":\"item_1\",\"type\":\"delegation\",\"target\":\"responses\"}}";
        // ACOTADO AL INTEGRAR (2026-09-12): el ejemplo de «lo que no se traduce» era un session.usage.updated, y la 48 de
        // la voz lo traduce ahora a Hecho.Duracion (su duración va al cierre, la 218). Con él la 217 salía roja
        // («dejó 0») sin que el log hubiera cambiado: el ejemplo había dejado de ser lo que decía ser. El de ahora es un
        // evento medido que ningún traductor atiende.
        const string instruccionesAnotadas = "{\"type\":\"session.instructions.appended\",\"event_id\":\"event_1\"}";
        const string sobreVacio = "{\"type\":\"response.event\"}";
        const string deltaDeRealtime = "{\"type\":\"response.function_call_arguments.delta\",\"delta\":\"{\"}";

        var callan = new (string Json, string Como)[]
        {
            (textoDelta, "un response.output_text.delta del delegado"),
            (argumentosDelta, "un response.function_call_arguments.delta del delegado"),
            (audioVacio, "un session.output_audio.delta vacío"),
        };
        var hablan = new (string Json, string Como)[]
        {
            (completado, "un response.completed del delegado"),
            (argumentosHechos, "un response.function_call_arguments.done"),
            (delegacion, "un session.delegation.created"),
            (instruccionesAnotadas, "un session.instructions.appended"),
        };

        // ── POR LA PUERTA DEL SOCKET ──────────────────────────────────────────
        // Ninguno de estos mensajes trae hechos, así que no suena nada: construir la conversación no abre
        // el altavoz. El marcador de turnos no oyó a nadie, así que tampoco cierra nada.
        var live = (Voz.Realtime.IProtocolo)Activator.CreateInstance(tLive,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.CreateInstance | BindingFlags.OptionalParamBinding,
            null, new[] { Type.Missing, Type.Missing }, null)!;
        int volcadas = 0;
        Action<string, string> oyeLog = (tag, msg) => { if (tag == "voz-viva" && msg.StartsWith("← ")) Interlocked.Increment(ref volcadas); };
        anotado.AddEventHandler(null, oyeLog);
        try
        {
            using var conv = (IDisposable)Activator.CreateInstance(tc, new object?[] { new SurfaceMapTools(() => null), live })!;
            int Deja(string json)
            {
                Interlocked.Exchange(ref volcadas, 0);
                procesar.Invoke(conv, new object[] { json, CancellationToken.None });
                return Interlocked.Exchange(ref volcadas, 0);
            }
            foreach (var (json, como) in callan)
            {
                int n = Deja(json);
                Debe(n == 0, $"por la puerta del socket, {como} no deja ninguna línea «←» (dejó {n})");
            }
            foreach (var (json, como) in hablan)
            {
                int n = Deja(json);
                Debe(n == 1, $"por la puerta del socket, {como} sigue dejando su línea «←», una (dejó {n})");
            }
        }
        finally { anotado.RemoveEventHandler(null, oyeLog); }

        // ── LA REGLA, PURA ───────────────────────────────────────────────────
        var vuelca = tc.GetMethod("SeVuelcaCrudo", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (vuelca == null) { Pendiente("Voice.ConversacionEnVivo.SeVuelcaCrudo", "217", "018"); return; }
        bool Vuelca(string json)
        {
            using var d = JsonDocument.Parse(json);
            return (bool)vuelca.Invoke(null, new object[] { d.RootElement })!;
        }
        foreach (var (json, como) in callan)
            Debe(!Vuelca(json), $"{como} no se vuelca crudo al log");
        foreach (var (json, como) in hablan)
            Debe(Vuelca(json), $"{como} sí se vuelca");
        Debe(Vuelca(sobreVacio), "un response.event sin evento dentro sí se vuelca: lo raro es lo que hay que ver");
        Debe(Vuelca(deltaDeRealtime),
            "y un .delta que no viene dentro de response.event sigue como estaba: la regla es de los deltas del delegado, no de todo lo que acabe en .delta");
    }

    /// <remarks>
    /// EL PANEL DE COSTOS NO SE ENTERABA DE GPT-LIVE, Y NADA LO DECÍA (revisa:regresiones, 2026-09-12). GPT-Live no
    /// cuenta fichas: cuenta segundos, en session.usage.updated, que la 48 de la voz traduce a Hecho.Duracion. La
    /// conversación no lo atendía, y ReportarConsumo salía en la guarda «_total &lt;= 0» sin reportar y sin dejar
    /// línea: con GPT-Live como voz por defecto, el consumo de voz desaparecía del panel en silencio.
    ///
    /// LOS SEGUNDOS SON EL ACUMULADO DE LA SESIÓN (12.0 a los 15 s y 25.0 a los 30 s de la misma sesión, medido):
    /// sumarlos como fichas daría 37 donde hubo 25. Pero una conexión nueva es otra sesión del servidor, que cuenta
    /// desde cero, y entre conexiones sí se suman. El panel cuenta fichas y FaceWindow no le pasa los segundos (zona de
    /// choque, no se toca): la línea del cierre es lo que los deja escritos.
    ///
    /// Se juzga por la puerta del socket (Procesar), por el cambio de conexión (EmpiezaUnaConexion) y por el cierre de
    /// verdad (TerminarAsync, con Viva puesta a mano: sin micrófono, collar ni altavoz abiertos, cerrar no toca ningún
    /// dispositivo). Que ArrancarAsync ponga la cuenta a cero necesita clave y socket, y no se juzga aquí (W218); tampoco
    /// que ReconectarAsync, al volver sin continuidad, pase por EmpiezaUnaConexion: aquí se invoca por reflexión, y
    /// cambiar esa llamada por un Dice deja el contrato INTACTO (W218b, medido el 2026-09-12).
    /// </remarks>
    private static void LaDuracionDeGptLiveLlegaAlCierre()
    {
        const BindingFlags Privado = BindingFlags.NonPublic | BindingFlags.Instance;
        var tc = Cap004("U.WindowsClient.Voice.ConversacionEnVivo");
        var procesar = tc?.GetMethod("Procesar", Privado);
        var reaccionar = tc?.GetMethod("Reaccionar", Privado);
        var conexion = tc?.GetMethod("EmpiezaUnaConexion", Privado);
        var terminar = tc?.GetMethod("TerminarAsync");
        var viva = tc?.GetProperty("Viva");
        var reporta = tc?.GetProperty("ReportaConsumo");
        var tConsumo = tc?.GetNestedType("ConsumoVivo");
        var anotado = Cap004("U.WindowsClient.Diagnostics.LogBus")?.GetEvent("Anotado");
        var tLive = typeof(Voz.Realtime.IProtocolo).Assembly.GetType("Voz.Realtime.ProtocoloGptLive");
        if (tc == null || procesar == null || reaccionar == null || conexion == null || terminar == null || viva == null
            || reporta == null || tConsumo == null || anotado == null || tLive == null)
        { Pendiente("ConversacionEnVivo (Procesar, Reaccionar, EmpiezaUnaConexion, TerminarAsync, ReportaConsumo) y Voz.Realtime.ProtocoloGptLive", "218", "018"); return; }
        var segundosDelServidor = tConsumo.GetProperty("SegundosDelServidor");
        if (segundosDelServidor == null) Pendiente("Voice.ConversacionEnVivo.ConsumoVivo.SegundosDelServidor", "218", "018");

        static string Uso(double s)
            => "{\"type\":\"session.usage.updated\",\"usage\":{\"seconds\":" + s.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "}}";
        Voz.Realtime.IProtocolo GptLive() => (Voz.Realtime.IProtocolo)Activator.CreateInstance(tLive,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.CreateInstance | BindingFlags.OptionalParamBinding,
            null, new[] { Type.Missing, Type.Missing }, null)!;
        IDisposable Conversacion(Voz.Realtime.IProtocolo p)
            => (IDisposable)Activator.CreateInstance(tc, new object?[] { new SurfaceMapTools(() => null), p })!;
        Reportes Escuchar(object conv)
        {
            var r = new Reportes();
            reporta.SetValue(conv, Delegate.CreateDelegate(reporta.PropertyType, r,
                typeof(Reportes).GetMethod(nameof(Reportes.Recibe))!.MakeGenericMethod(tConsumo)));
            return r;
        }
        void Llega(object conv, string json) => procesar.Invoke(conv, new object[] { json, CancellationToken.None });
        void Cerrar(object conv) { viva.SetValue(conv, true); ((Task)terminar.Invoke(conv, null)!).Wait(5000); }
        // El reporte sale en un Task.Run a propósito (no retrasa el cierre): se espera a que llegue, y un poco más para ver que no llega otro.
        static int Espera(Reportes r, int cuantos)
        {
            long fin = Environment.TickCount64 + 3000;
            while (r.Cuantos < cuantos && Environment.TickCount64 < fin) Thread.Sleep(20);
            Thread.Sleep(200);
            return r.Cuantos;
        }
        object? Campo(object parte, string nombre) => tConsumo.GetProperty(nombre)?.GetValue(parte);

        var lineas = new List<string>();
        Action<string, string> oyeLog = (tag, msg) => { if (tag == "voz-viva" && msg.Contains("según el servidor")) lock (lineas) lineas.Add(msg); };
        string[] Lineas() { lock (lineas) { var l = lineas.ToArray(); lineas.Clear(); return l; } }
        anotado.AddEventHandler(null, oyeLog);
        try
        {
            // ── UNA SESIÓN DE GPT-LIVE QUE SE CORTÓ Y VOLVIÓ ─────────────────────
            using (var conv = Conversacion(GptLive()))
            {
                var r = Escuchar(conv);
                Lineas();
                Llega(conv, Uso(12.0));
                Llega(conv, Uso(25.0));
                conexion.Invoke(conv, new object[] { "" });   // la conexión nueva es otra sesión del servidor: vuelve a contar desde cero
                Llega(conv, Uso(3.0));
                // TRES CONEXIONES, NO DOS: con una sola reconexión, «sumar lo de las anteriores» y «quedarse con la anterior»
                // dan lo mismo (0 + 25 = 25). El 2026-09-12 EmpiezaUnaConexion con «=» en vez de «+=» (X218b) dejó ✔ 218 y
                // CONTRATO INTACTO; con dos cortes, 25 + 3 + 4 s se reportaban como 7.
                conexion.Invoke(conv, new object[] { "" });
                Llega(conv, Uso(4.0));
                Cerrar(conv);
                int n = Espera(r, 1);
                string[] dichas = Lineas();
                Debe(n == 1, $"una sesión de GPT-Live sin fichas y con segundos se reporta al cerrar la voz, una vez (se reportó {n})");
                Debe(dichas.Length == 1 && dichas[0].Contains(" 32 s "),
                    $"y el cierre deja UNA línea voz-viva con los segundos del servidor: 25 de la primera conexión —el último acumulado, no 12 + 25—, 3 de la segunda y 4 de la tercera, 32 s (dejó {dichas.Length}: {string.Join(" | ", dichas)})");
                if (n >= 1)
                {
                    var parte = r.Primera!;
                    Debe(Convert.ToInt64(Campo(parte, "Total")) == 0, $"y el reporte no se inventa fichas: GPT-Live no las cuenta (lleva {Campo(parte, "Total")})");
                    if (segundosDelServidor != null)
                    {
                        double s = Convert.ToDouble(segundosDelServidor.GetValue(parte));
                        Debe(Math.Abs(s - 32.0) < 1e-9, $"y el reporte lleva esos 32 s del servidor (lleva {s})");
                    }
                }

                Cerrar(conv);
                int otra = Espera(r, 2);
                string[] otraVez = Lineas();
                Debe(otra == 1 && otraVez.Length == 0,
                    $"y reportar pone la cuenta a cero: cerrar otra vez no reporta ni escribe los mismos segundos (reportes {otra}, líneas {otraVez.Length})");
            }

            // ── SIN NADIE A QUIEN REPORTAR, LA LÍNEA SALE IGUAL ─────────────────
            using (var conv = Conversacion(GptLive()))
            {
                Lineas();
                Llega(conv, Uso(40.0));
                Cerrar(conv);
                string[] dichas = Lineas();
                Debe(dichas.Length == 1 && dichas[0].Contains(" 40 s "),
                    $"sin nadie a quien reportar (sin backend), el cierre deja igual su línea con los 40 s: el log es la fuente de verdad (dejó {dichas.Length}: {string.Join(" | ", dichas)})");
            }

            // ── GPT REALTIME: FICHAS Y NINGÚN SEGUNDO, COMO ANTES ────────────────
            using (var conv = Conversacion(new Voz.Realtime.ProtocoloOpenAI()))
            {
                var r = Escuchar(conv);
                Lineas();
                reaccionar.Invoke(conv, new object[] { new Voz.Realtime.Hecho.Consumo(100, 50, 150), CancellationToken.None });
                Cerrar(conv);
                int n = Espera(r, 1);
                string[] dichas = Lineas();
                Debe(n == 1 && Convert.ToInt64(Campo(r.Primera!, "Total")) == 150,
                    $"con GPT Realtime, una sesión con fichas se sigue reportando con sus fichas (reportes {n})");
                Debe(dichas.Length == 0, $"y sin segundos del servidor no deja línea de segundos (dejó {dichas.Length}: {string.Join(" | ", dichas)})");
                if (n >= 1 && segundosDelServidor != null)
                    Debe(Convert.ToDouble(segundosDelServidor.GetValue(r.Primera!)) == 0, "y su reporte no lleva segundos del servidor");
            }

            // ── NADA QUE CONTAR ──────────────────────────────────────────────────
            using (var conv = Conversacion(GptLive()))
            {
                var r = Escuchar(conv);
                Lineas();
                Cerrar(conv);
                int n = Espera(r, 1);
                string[] dichas = Lineas();
                Debe(n == 0 && dichas.Length == 0,
                    $"una sesión sin fichas ni segundos no reporta nada ni deja línea: no hubo nada que contar (reportes {n}, líneas {dichas.Length})");
            }
        }
        finally { anotado.RemoveEventHandler(null, oyeLog); }
    }

    /// <remarks>
    /// LA MISMA CLASE DE ERROR, DOS TRATAMIENTOS (nivel 4 del 2026-09-12, cuenta sin crédito). Con U_VOZ=realtime el
    /// servidor cerró con 1013 «insufficient_quota.credit_balance_exhausted» y la conversación reconectó cuatro veces
    /// («reconectada SIN continuidad … intento 1..4», «se cayó 5 veces seguidas: se deja»); GPT-Live, con la 49, dijo la
    /// causa una vez. Reconectar con la misma clave, el mismo modelo y la misma cuenta falla igual.
    ///
    /// LOS CÓDIGOS SON LOS MEDIDOS, no los de la documentación. El 2026-09-13, con .NET 8 y el mismo ClientWebSocket
    /// de la app (sonda-fatal, fuera del repo): una clave falsa por /v1/live/sessions se rechaza en el apretón de
    /// manos con HTTP 401 (WebSocketException NotAWebSocket); por /v1/realtime el apretón pasa, llega el error
    /// invalid_api_key y el cierre 3000 «invalid_request_error.invalid_api_key». Un modelo que no existe: invalid_model
    /// en GPT-Live, model_not_found y cierre 4004 en Realtime. La descripción del cierre es «type.code» del error.
    ///
    /// SE RECONOCE POR EL CÓDIGO Y NO POR LA PROSA: el message está en inglés, cambia de redacción y lleva cifras
    /// (la del 401 viene dentro de una frase de .NET). Y el número del cierre no es la causa: 1013 es «vuelve a
    /// intentarlo» en el RFC 6455, y el servidor lo usó para decir que no hay crédito.
    /// </remarks>
    private static void LoQueNoSeArreglaReintentandoSeReconoce()
    {
        var t = Cap004("U.WindowsClient.Voice.NoSeArreglaReintentando");
        var m = t?.GetMethod("PorQue", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string) });
        if (m == null) { Pendiente("Voice.NoSeArreglaReintentando.PorQue", "223", "018"); return; }
        string PorQue(string codigo) => (string)(m.Invoke(null, new object[] { codigo }) ?? "");

        var causas = new (string Palabra, string[] Codigos)[]
        {
            ("crédito", new[] { "credit_balance_exhausted", "insufficient_quota", "insufficient_quota.credit_balance_exhausted" }),
            ("clave", new[] { "invalid_api_key", "invalid_request_error.invalid_api_key", "401" }),
            ("modelo", new[] { "invalid_model", "model_not_found", "invalid_request_error.model_not_found" }),
        };
        foreach (var (palabra, codigos) in causas)
        {
            var dichas = codigos.Select(PorQue).ToArray();
            for (int i = 0; i < codigos.Length; i++)
                Debe(dichas[i].Contains(palabra) && causas.Where(o => o.Palabra != palabra).All(o => !dichas[i].Contains(o.Palabra)),
                    $"«{codigos[i]}» no se arregla reintentando, y lo que dice nombra «{palabra}» y ninguna de las otras dos causas (dijo «{dichas[i]}»)");
            Debe(dichas.Distinct().Count() == 1,
                $"los códigos de una misma causa la dicen igual: es una causa, no tres ({string.Join(" | ", dichas)})");
        }

        string[] reintentables =
        {
            "", "   ",
            // Errores medidos que no son de clave, cuenta ni modelo: la sesión sigue viva o un corte los arregla.
            "response_input_buffer_full", "function_call_outputs_required", "unknown_parameter",
            "invalid_request_error", "invalid_request_error.unknown_parameter",
            // Motivos de cierre que no son una causa: el del servidor y el nuestro.
            "close_requested", "fin",
            // El número del cierre no es la causa.
            "1013", "3000", "4004",
            // Y la prosa no es un código, aunque hable de lo mismo.
            "You have no credits remaining. Add credits to continue using the API at https://platform.openai.com/settings/organization/billing/.",
            "Model \"gpt-live-inexistente-9\" is not supported in realtime mode.",
            "The server returned status code '401' when status code '101' was expected.",
        };
        foreach (string codigo in reintentables)
        {
            string dicha = PorQue(codigo);
            Debe(dicha.Length == 0, $"«{codigo}» se puede reintentar: no dice causa (dijo «{dicha}»)");
        }
    }

    /// <remarks>
    /// EL CABLEADO DE LA 223, con los dos protocolos y sin socket. Hasta el 2026-09-13 lo que hacía la conversación al
    /// quedarse sin escucha vivía en el finally de RecibirAsync y en el catch de ReconectarAsync, que se llamaba a sí
    /// mismo con cualquier excepción: ningún contrato llegaba ahí. Ahora las tres puertas por las que llega la causa
    /// —un error (Procesar), el cierre del socket (CerroElServidor) y un apretón de manos rechazado (NoConecto)— la
    /// anotan, y UN solo sitio decide si se reconecta (SeAcaboLaEscuchaAsync). La reconexión se sustituye como la
    /// puerta de salida de la 208: el contrato no abre sockets ni lleva la clave.
    ///
    /// Cada fuente se juzga SOLA además de junta: con Realtime el error y el cierre traen el mismo código, y juzgarlos
    /// solo juntos dejaría verde que una de las dos puertas no anotara nada.
    /// </remarks>
    private static void LoQueNoSeArreglaNoReconecta()
    {
        const BindingFlags Privado = BindingFlags.NonPublic | BindingFlags.Instance;
        var tc = Cap004("U.WindowsClient.Voice.ConversacionEnVivo");
        var procesar = tc?.GetMethod("Procesar", Privado);
        var conexion = tc?.GetMethod("EmpiezaUnaConexion", Privado);
        var viva = tc?.GetProperty("Viva");
        var dice = tc?.GetEvent("Dice");
        var anotado = Cap004("U.WindowsClient.Diagnostics.LogBus")?.GetEvent("Anotado");
        var tLive = typeof(Voz.Realtime.IProtocolo).Assembly.GetType("Voz.Realtime.ProtocoloGptLive");
        if (tc == null || procesar == null || conexion == null || viva == null || dice == null || anotado == null || tLive == null)
        { Pendiente("ConversacionEnVivo (Procesar, EmpiezaUnaConexion, Viva, Dice), LogBus.Anotado y Voz.Realtime.ProtocoloGptLive", "224", "018"); return; }
        var cerro = tc.GetMethod("CerroElServidor", Privado, new[] { typeof(int), typeof(string) });
        var corto = tc.GetMethod("SeCortoLaEscucha", Privado, new[] { typeof(Exception) });
        var noConecto = tc.GetMethod("NoConecto", Privado, new[] { typeof(Exception), typeof(int) });
        var seAcabo = tc.GetMethod("SeAcaboLaEscuchaAsync", Privado, new[] { typeof(CancellationToken) });
        var reconectar = tc.GetField("_reconectar", Privado);
        if (cerro == null || corto == null || noConecto == null || seAcabo == null || reconectar == null)
        { Pendiente("ConversacionEnVivo: CerroElServidor(int, string), SeCortoLaEscucha(Exception), NoConecto(Exception, int), SeAcaboLaEscuchaAsync y la puerta _reconectar", "224", "018"); return; }

        // Copiados de lo que contestó el servidor (2026-09-12 y 2026-09-13).
        const string rtClave = """{"type":"error","event_id":"event_ENgrrx8v946dIyk6vQTOj","error":{"type":"invalid_request_error","code":"invalid_api_key","message":"Incorrect API key provided: sk-proj-**************************************************0000. You can find your API key at https://platform.openai.com/account/api-keys.","param":null,"event_id":null}}""";
        const string rtModelo = """{"type":"error","event_id":"event_ENgs3fIvSRtnqIx8JCGfh","error":{"type":"invalid_request_error","code":"model_not_found","message":"The model `gpt-realtime-inexistente-9` does not exist or you do not have access to it.","param":null,"event_id":null}}""";
        const string liveAbrio = """{"type":"session.started","session":{"id":"live_u2_ENOy6GhblDeLrMlDOGSX1","model":"gpt-live-1","status":"active","input":[]}}""";
        const string liveCredito = """{"type":"error","event_id":"event_7f0763e4-314d-4930-9bfa-eb831d673918","error":{"type":"invalid_request_error","code":"credit_balance_exhausted","message":"You have no credits remaining. Add credits to continue using the API at https://platform.openai.com/settings/organization/billing/."}}""";
        const string liveModelo = """{"type":"error","event_id":"event_d7252ece-60b5-4b34-88a4-30b46a4124f4","error":{"type":"invalid_request_error","code":"invalid_model","message":"Model \"gpt-live-inexistente-9\" is not supported in realtime mode."}}""";
        const string liveBuffer = """{"type":"error","event_id":"event_ENQ9zsNQ1Zl59krrM4MFC","error":{"type":"invalid_request_error","code":"response_input_buffer_full","message":"Backend response input history is limited to 128 items and 32768 UTF-8 bytes per session.","param":"item"}}""";

        Voz.Realtime.IProtocolo GptLive() => (Voz.Realtime.IProtocolo)Activator.CreateInstance(tLive,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.CreateInstance | BindingFlags.OptionalParamBinding,
            null, new[] { Type.Missing, Type.Missing }, null)!;
        void Llega(object conv, string json) => procesar.Invoke(conv, new object[] { json, CancellationToken.None });
        void Cierra(object conv, int estado, string descripcion) => cerro.Invoke(conv, new object[] { estado, descripcion });
        void SeCorta(object conv) => corto.Invoke(conv, new object[] { new System.Net.WebSockets.WebSocketException(
            System.Net.WebSockets.WebSocketError.ConnectionClosedPrematurely, "The remote party closed the WebSocket connection without completing the close handshake.") });
        void NoConecta(object conv, int estadoHttp) => noConecto.Invoke(conv, new object[] { new System.Net.WebSockets.WebSocketException(
            System.Net.WebSockets.WebSocketError.NotAWebSocket, $"The server returned status code '{estadoHttp}' when status code '101' was expected."), estadoHttp });

        var lineas = new List<string>();
        Action<string, string> oyeLog = (tag, msg) => { if (tag == "voz-viva" && msg.StartsWith("no se reintenta", StringComparison.Ordinal)) lock (lineas) lineas.Add(msg); };
        anotado.AddEventHandler(null, oyeLog);
        try
        {
            (int Reconexiones, string[] Dichos, string[] Lineas, bool Viva) Corre(Voz.Realtime.IProtocolo p, Action<object> queLlega)
            {
                lock (lineas) lineas.Clear();
                using var conv = (IDisposable)Activator.CreateInstance(tc, new object?[] { new SurfaceMapTools(() => null), p })!;
                int reconexiones = 0;
                var dichos = new List<string>();
                reconectar.SetValue(conv, (Func<Task>)(() => { Interlocked.Increment(ref reconexiones); return Task.CompletedTask; }));
                dice.AddEventHandler(conv, (Action<string>)(s => { lock (dichos) dichos.Add(s); }));
                viva.SetValue(conv, true);
                conexion.Invoke(conv, new object[] { "" });   // una conexión recién mandada su apertura, como la deja ArrancarAsync
                queLlega(conv);
                ((Task)seAcabo.Invoke(conv, new object[] { CancellationToken.None })!).Wait(5000);
                bool sigueViva = (bool)viva.GetValue(conv)!;
                string[] l; lock (lineas) l = lineas.ToArray();
                string[] d; lock (dichos) d = dichos.ToArray();
                return (reconexiones, d, l, sigueViva);
            }
            void NoReintenta(string como, (int Reconexiones, string[] Dichos, string[] Lineas, bool Viva) r, string causa)
            {
                Debe(r.Reconexiones == 0, $"{como}: no reconecta (reconectó {r.Reconexiones})");
                Debe(!r.Viva, $"{como}: cierra la voz en vez de quedarse abierta y muda");
                Debe(r.Dichos.Length == 1 && r.Dichos[0].Contains(causa),
                    $"{como}: dice por qué UNA vez, nombrando «{causa}» (dijo {r.Dichos.Length}: {string.Join(" | ", r.Dichos)})");
                Debe(r.Lineas.Length == 1 && r.Lineas[0].Contains(causa),
                    $"{como}: y deja UNA línea voz-viva «no se reintenta» con esa causa (dejó {r.Lineas.Length}: {string.Join(" | ", r.Lineas)})");
            }
            void Reintenta(string como, (int Reconexiones, string[] Dichos, string[] Lineas, bool Viva) r)
                => Debe(r.Reconexiones == 1 && r.Viva && r.Dichos.Length == 0 && r.Lineas.Length == 0,
                    $"{como}: sigue reconectando, una vez y sin decir ninguna causa (reconectó {r.Reconexiones}, viva {r.Viva}, dijo {r.Dichos.Length}: {string.Join(" | ", r.Dichos)}, líneas {r.Lineas.Length})");

            // ── GPT REALTIME ────────────────────────────────────────────────────
            var realtime = new Voz.Realtime.ProtocoloOpenAI();
            NoReintenta("con GPT Realtime, una clave falsa (error invalid_api_key y cierre 3000)",
                Corre(realtime, c => { Llega(c, rtClave); Cierra(c, 3000, "invalid_request_error.invalid_api_key"); }), "clave");
            NoReintenta("con GPT Realtime, el cierre 1013 «insufficient_quota.credit_balance_exhausted» solo, como el del nivel 4 del 2026-09-12",
                Corre(realtime, c => Cierra(c, 1013, "insufficient_quota.credit_balance_exhausted")), "crédito");
            NoReintenta("con GPT Realtime, un modelo que no existe (error model_not_found y cierre 4004)",
                Corre(realtime, c => { Llega(c, rtModelo); Cierra(c, 4004, "invalid_request_error.model_not_found"); }), "modelo");
            NoReintenta("con GPT Realtime, el error invalid_api_key solo, y la escucha cortada sin cierre",
                Corre(realtime, c => { Llega(c, rtClave); SeCorta(c); }), "clave");
            Reintenta("con GPT Realtime, un cierre sin descripción",
                Corre(realtime, c => Cierra(c, 1001, "")));

            // ── GPT-LIVE ───────────────────────────────────────────────────────
            NoReintenta("con GPT-Live, credit_balance_exhausted con la sesión ya confirmada, y el socket muerto",
                Corre(GptLive(), c => { Llega(c, liveAbrio); Llega(c, liveCredito); SeCorta(c); }), "crédito");
            NoReintenta("con GPT-Live, la reconexión rechazada en el apretón de manos con HTTP 401 (clave falsa, medido)",
                Corre(GptLive(), c => { Llega(c, liveAbrio); SeCorta(c); NoConecta(c, 401); }), "clave");
            Reintenta("con GPT-Live, un error que no es de clave, cuenta ni modelo (response_input_buffer_full) y el socket muerto",
                Corre(GptLive(), c => { Llega(c, liveAbrio); Llega(c, liveBuffer); SeCorta(c); }));
            Reintenta("con GPT-Live, una reconexión que no conecta sin respuesta HTTP (la red)",
                Corre(GptLive(), c => { Llega(c, liveAbrio); SeCorta(c); NoConecta(c, 0); }));
            Reintenta("con GPT-Live, una conexión nueva no hereda la causa de la anterior",
                Corre(GptLive(), c => { Llega(c, liveAbrio); Llega(c, liveCredito); conexion.Invoke(c, new object[] { "" }); Llega(c, liveAbrio); SeCorta(c); }));

            // Y LO QUE LA 49 YA HACÍA, AHORA CON JUEZ: un error antes de session.started es que no abrió (W49c, hasta hoy sin
            // juez). Con un error que NO es de cuenta, clave ni modelo —unknown_parameter, medido contra un session.start mal
            // formado—, porque con invalid_model la rama de la causa también lo pararía y romper la de la 49 quedaría verde.
            const string liveParametro = """{"type":"error","event_id":"event_ENOy9VsLzg0PIpUAbkOqn","error":{"type":"invalid_request_error","code":"unknown_parameter","message":"Unknown parameter: 'session.instructions'.","param":"session.instructions","client_event_id":"sonda_voz"}}""";
            var noAbrio = Corre(GptLive(), c => { Llega(c, liveParametro); SeCorta(c); });
            Debe(noAbrio.Reconexiones == 0 && !noAbrio.Viva && noAbrio.Dichos.Length == 1 && noAbrio.Dichos[0].Contains("Unknown parameter: 'session.instructions'."),
                $"con GPT-Live, un error antes de session.started que no es de cuenta, clave ni modelo: no abrió, no reconecta y lo dice una vez con lo que dijo el servidor (reconectó {noAbrio.Reconexiones}, viva {noAbrio.Viva}, dijo {noAbrio.Dichos.Length}: {string.Join(" | ", noAbrio.Dichos)})");
            var noAbrioModelo = Corre(GptLive(), c => { Llega(c, liveModelo); SeCorta(c); });
            Debe(noAbrioModelo.Reconexiones == 0 && !noAbrioModelo.Viva && noAbrioModelo.Dichos.Length == 1 && noAbrioModelo.Dichos[0].Contains("is not supported in realtime mode"),
                $"con GPT-Live, invalid_model antes de session.started: tampoco reconecta, y lo dice una vez con lo que dijo el servidor (reconectó {noAbrioModelo.Reconexiones}, viva {noAbrioModelo.Viva}, dijo {noAbrioModelo.Dichos.Length}: {string.Join(" | ", noAbrioModelo.Dichos)})");
        }
        finally { anotado.RemoveEventHandler(null, oyeLog); }
    }

    // ── Spec 020: Ü trabaja en su ventana, y la persona sigue en la suya ─────────────────────

    private static Type? Grafico(string nombre) => typeof(U.Graph.Surfaces.UiaSurface).Assembly.GetType(nombre);

    private static void LaVentanaDeDelanteSeEligeConUnaRegla()
    {
        // DOS REGLAS PARA LA MISMA PREGUNTA (2026-09-14): el localizador tomaba la ventana con foco y,
        // si era la carita, devolvía la última recordada sin recalcular; el lector bajaba por el
        // orden Z. Tras cerrar la Tienda, Ü dijo «estás en microsoft-store» durante seis segundos con
        // la lista de elementos de OTRA ventana.
        var m = Grafico("U.Graph.Surfaces.VentanaDeDelante")?.GetMethod("Elegir");
        Debe(m != null, "todavía no existe «Surfaces.VentanaDeDelante.Elegir» (spec 020, promesa 230). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (m == null) return;
        // El orden Z: 1 (la carita) → 2 (visible, sin título) → 3 (con título, invisible) → 4 (la buena) → nada.
        Func<IntPtr, bool> visible = h => h != (IntPtr)3;
        Func<IntPtr, bool> conTitulo = h => h != (IntPtr)2;
        Func<IntPtr, bool> propia = h => h == (IntPtr)1;
        var cadena = new Dictionary<IntPtr, IntPtr> { [(IntPtr)1] = (IntPtr)2, [(IntPtr)2] = (IntPtr)3, [(IntPtr)3] = (IntPtr)4 };
        Func<IntPtr, IntPtr> siguiente = h => cadena.TryGetValue(h, out var n) ? n : IntPtr.Zero;
        IntPtr Elegir(IntPtr foco, Func<IntPtr, IntPtr> sig) => (IntPtr)m.Invoke(null, new object[] { foco, visible, conTitulo, propia, sig, 50 })!;
        Debe(Elegir((IntPtr)1, siguiente) == (IntPtr)4,
            "con la carita delante, la ventana de delante es la primera visible, con título y ajena bajando por el orden Z: la 4, calculada AHORA");
        Debe(Elegir((IntPtr)4, siguiente) == (IntPtr)4, "si la que tiene el foco ya vale, es esa");
        Debe(Elegir((IntPtr)2, siguiente) == (IntPtr)4, "una ventana sin título no cuenta: no identifica ninguna pantalla");
        Debe(Elegir((IntPtr)1, _ => IntPtr.Zero) == IntPtr.Zero,
            "y si no hay ninguna ajena se dice que no hay: ni la de Ü ni la última recordada, que es lo que describía una Tienda ya cerrada");
    }

    private static void LaManoDicePorQueNoPudo()
    {
        // «no pude pulsar «Elipse».» cubría tres causas (patrón nº2): no encontré el elemento en esa
        // ventana, no admite ningún patrón, el patrón falló. Y el registro detallado de UiaSurface
        // (L) no llegaba a ningún log: la única línea que decía en qué ventana buscó nunca se escribió.
        var conMotivo = Capacidad("U.WindowsClient.Navigation.PulsarSegunElNucleo")?.GetMethod("ConMotivo");
        var logGlobal = Grafico("U.Graph.Surfaces.UiaSurface")?.GetProperty("LogGlobal");
        Debe(conMotivo != null && logGlobal != null,
            "todavía no existen «PulsarSegunElNucleo.ConMotivo» ni «UiaSurface.LogGlobal» (spec 020, promesa 231). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (conMotivo == null || logGlobal == null) return;
        var g = new Nucleo.Grafo();
        g.Observar("uia://mspaint.exe/sin-titulo", new[] { new Nucleo.Elemento("uia:name=Cerrar", "Cerrar", "Button") });
        Func<string> donde = () => "uia://mspaint.exe/sin-titulo";
        Func<string, string, string, string?> manoQueNoPudo = (sel, et, gesto) => "no encontré «Cerrar» en la ventana «Vista de tareas»";
        var pulsar = (PulsarSegunElNucleo)conMotivo.Invoke(null, new object[] { g, donde, manoQueNoPudo })!;
        var r = pulsar.Pulsa("uia:name=Cerrar", "Cerrar");
        Debe(!r.SePudo && r.Cuenta.Contains("no encontré «Cerrar» en la ventana «Vista de tareas»"),
            $"la cuenta trae la CAUSA que dio la mano, no solo «no pude» ({r.Cuenta})");
        Func<string, string, string, string?> manoQuePudo = (sel, et, gesto) => null;
        var pulsar2 = ((PulsarSegunElNucleo)conMotivo.Invoke(null, new object[] { g, donde, manoQuePudo })!);
        Debe(pulsar2.Pulsa("uia:name=Cerrar", "Cerrar").SePudo, "y sin motivo (null) es que pudo");
        // EL REGISTRO DE LA SUPERFICIE LLEGA: la app lo conecta al log como «mano».
        var lineas = new List<string>();
        logGlobal.SetValue(null, (Action<string>)(l => lineas.Add(l)));
        try
        {
            var superficie = new U.Graph.Surfaces.UiaSurface { SoloEnFoco = true };
            bool ok = superficie.Execute(new U.Graph.PlanStep
            {
                StepOrder = 1, ActionType = "click", Selector = "uia:name=__no_existe_en_ninguna_parte__;ct=Button", Label = "__nada__",
            }, out string error);
            Debe(!ok && error.Contains("__nada__"), $"un elemento que no está se dice por su nombre ({error})");
            Debe(lineas.Count > 0, "y lo que la superficie fue decidiendo llegó al registro global, que es el que la app conecta al log");
        }
        finally { logGlobal.SetValue(null, null); }
    }

    private static void AbrirMiraQueVentanasHay()
    {
        // «abre Paint» con un Paint abierto lanzó OTRO (2026-09-14, 16:53:48): cuando el nombre coincide
        // exacto con una app instalada se lanzaba antes de preguntar si ya estaba. Y el dueño no quiere
        // ni «siempre la existente» ni «siempre nueva»: quiere que el modelo lo decida sabiendo qué hay.
        var t = Capacidad("U.WindowsClient.Navigation.AbrirSegunElNucleo");
        var lasDe = t?.GetMethod("LasDe");
        var abrir2 = t?.GetMethods().FirstOrDefault(m => m.Name == "Abrir" && m.GetParameters().Length == 2);
        var ctor = t?.GetConstructors().FirstOrDefault(c => c.GetParameters().Length == 7);
        Debe(lasDe != null && abrir2 != null && ctor != null,
            "todavía no existen «AbrirSegunElNucleo.LasDe», «Abrir(pedido, instancia)» ni el constructor con las ventanas abiertas (spec 020, promesa 232). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || lasDe == null || abrir2 == null || ctor == null) return;
        var todas = new List<(IntPtr Hwnd, string Proceso, string Titulo)>
        {
            ((IntPtr)11, "mspaint.exe", "Sin título - Paint"),
            ((IntPtr)12, "ApplicationFrameHost.exe", "Microsoft Store"),
            ((IntPtr)13, "chrome.exe", "Meet"),
        };
        IntPtr H(object o) => (IntPtr)o.GetType().GetField("Item1")!.GetValue(o)!;
        var dePaint = ((System.Collections.IEnumerable)lasDe.Invoke(null, new object[] { "paint", todas })!).Cast<object>().ToList();
        Debe(dePaint.Count == 1 && H(dePaint[0]) == (IntPtr)11,
            "«paint» encuentra la ventana de mspaint.exe: el proceso no se llama como la app, y la comparación lo sabe");
        var deTienda = ((System.Collections.IEnumerable)lasDe.Invoke(null, new object[] { "microsoft store", todas })!).Cast<object>().ToList();
        Debe(deTienda.Count == 1 && H(deTienda[0]) == (IntPtr)12,
            "y «microsoft store» la encuentra por el TÍTULO, porque su proceso es ApplicationFrameHost");
        Debe(((System.Collections.IEnumerable)lasDe.Invoke(null, new object[] { "notepad", todas })!).Cast<object>().Count() == 0,
            "y lo que no está abierto no se inventa");

        var instaladas = new AbrirSegunElNucleo.AppDelSistema[] { new("Paint", "Microsoft.Paint_8wekyb3d8bbwe!App") };
        (string Cuenta, List<string> Lanzados, List<IntPtr> Traidas) Abrir(string pedido, string instancia, List<(IntPtr Hwnd, string Proceso, string Titulo)> abiertas)
        {
            var lanzados = new List<string>(); var traidas = new List<IntPtr>();
            string donde = "uia://chrome.exe/meet";
            var abrir = ctor.Invoke(new object[]
            {
                (Func<string>)(() => donde),
                (Func<Mapeador.ComoMePongoDelante.Plan, bool>)(p => { lanzados.Add(p.Que); donde = "uia://mspaint.exe/sin-titulo"; return true; }),
                (Func<string, string>)(_ => ""),
                (Func<IReadOnlyList<AbrirSegunElNucleo.AppDelSistema>>)(() => instaladas),
                (Func<string, bool>)(cmd => { lanzados.Add(cmd); donde = "uia://mspaint.exe/sin-titulo"; return true; }),
                (Func<IReadOnlyList<(IntPtr Hwnd, string Proceso, string Titulo)>>)(() => abiertas),
                (Func<IntPtr, bool>)(h => { traidas.Add(h); donde = "uia://mspaint.exe/sin-titulo"; return true; }),
            });
            return ((string)abrir2.Invoke(abrir, new object[] { pedido, instancia })!, lanzados, traidas);
        }
        var unaAbierta = Abrir("paint", "", todas);
        Debe(unaAbierta.Lanzados.Count == 0 && unaAbierta.Traidas.SequenceEqual(new[] { (IntPtr)11 }),
            $"con un Paint abierto, «abre Paint» NO lanza otro: trae ese (lanzó {unaAbierta.Lanzados.Count}, trajo {unaAbierta.Traidas.Count})");
        Debe(unaAbierta.Cuenta.Contains("Sin título - Paint") && unaAbierta.Cuenta.Contains("nueva", StringComparison.OrdinalIgnoreCase),
            $"y lo dice: nombra la ventana que había y cómo pedir otra ({unaAbierta.Cuenta})");
        var nueva = Abrir("paint", "nueva", todas);
        Debe(nueva.Lanzados.Count == 1 && nueva.Traidas.Count == 0,
            $"pedida una instancia NUEVA, se lanza aunque haya una abierta: el modelo decide, no la herramienta (lanzó {nueva.Lanzados.Count})");
        Debe(nueva.Cuenta.Contains("2"), $"y la cuenta dice cuántas hay ahora ({nueva.Cuenta})");
        var ninguna = Abrir("paint", "", new List<(IntPtr, string, string)>());
        Debe(ninguna.Lanzados.Count == 1, "sin ninguna abierta se lanza como siempre");
        var dos = todas.Concat(new[] { ((IntPtr)14, "mspaint.exe", "dibujo.png - Paint") }).ToList();
        var conDos = Abrir("paint", "", dos);
        Debe(conDos.Lanzados.Count == 0 && conDos.Cuenta.Contains("Sin título - Paint") && conDos.Cuenta.Contains("dibujo.png - Paint"),
            $"con dos abiertas se nombran las dos, para que el modelo sepa entre cuáles elige ({conDos.Cuenta})");
    }

    private static void LaVentanaDeTrabajoDeU()
    {
        // UNA SOLA IDEA DE «DÓNDE ESTOY» PARA DOS COSAS INCOMPATIBLES: aprender de la persona (su foco)
        // y ejecutar (la ventana que Ü opera). En cuanto la persona sigue trabajando, la segunda deja de
        // ser la primera: «no pude pulsar «Elipse». Estás en «uia://explorer.exe/vista-de-tareas»».
        var t = Capacidad("U.WindowsClient.Navigation.VentanaDeTrabajo");
        var resolver = t?.GetMethod("Resolver");
        var fijar = t?.GetMethod("Fijar");
        var aviso = Capacidad("U.WindowsClient.Navigation.PulsarSegunElNucleo")?.GetProperty("AvisoDeLaVentana");
        Debe(t != null && resolver != null && fijar != null && aviso != null,
            "todavía no existen «Navigation.VentanaDeTrabajo» ni «PulsarSegunElNucleo.AvisoDeLaVentana» (spec 020, promesa 233). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null || resolver == null || fijar == null || aviso == null) return;
        var trabajo = Activator.CreateInstance(t)!;
        string foco = "uia://chrome.exe/meet";
        var existentes = new HashSet<IntPtr> { (IntPtr)7 };
        Func<IntPtr, bool> existe = h => existentes.Contains(h);
        Func<string> focoDeLaPersona = () => foco;
        object Donde() => resolver.Invoke(trabajo, new object[] { existe, focoDeLaPersona })!;
        string Id(object d) => (string)Prop(d, "Id")!;
        string Aviso(object d) => (string)Prop(d, "Aviso")!;
        var sin = Donde();
        Debe(Id(sin) == foco && Aviso(sin).Length == 0, "sin ventana de trabajo, Ü ejecuta sobre el foco de la persona: es exactamente lo de hoy");
        fijar.Invoke(trabajo, new object[] { (IntPtr)7, "uia://mspaint.exe/sin-titulo" });
        var con = Donde();
        Debe(Id(con) == "uia://mspaint.exe/sin-titulo" && Aviso(con).Length == 0,
            "con ventana de trabajo, «dónde» es ESA ventana aunque la persona tenga el foco en otra: es lo que le deja seguir trabajando");
        existentes.Clear();
        var ida = Donde();
        Debe(Id(ida) == foco && Aviso(ida).Contains("ya no existe") && Aviso(ida).Contains("mspaint"),
            $"si la ventana de trabajo desapareció, se dice por su nombre y se vuelve al foco de la persona: no se describe una pantalla cerrada ({Aviso(ida)})");
        Debe(Aviso(Donde()).Length == 0, "y el aviso se da UNA vez: la siguiente pregunta ya es limpia");
        // Y EL EJECUTOR LO CUENTA, sin inventar un tramo.
        var g = new Nucleo.Grafo();
        g.Observar("uia://mspaint.exe/sin-titulo", new[] { new Nucleo.Elemento("uia:name=Cerrar", "Cerrar", "Button") });
        string donde = "uia://mspaint.exe/sin-titulo";
        var pulsar = new PulsarSegunElNucleo(g, () => donde, (sel, et) => { donde = "uia://chrome.exe/meet"; return true; }) { EsperaMaximaMs = 240 };
        aviso.SetValue(pulsar, (Func<string>)(() => "la ventana en la que trabajaba («uia://mspaint.exe/sin-titulo») ya no existe"));
        var r = pulsar.Pulsa("uia:name=Cerrar", "Cerrar");
        Debe(r.SePudo && r.Cuenta.Contains("ya no existe"),
            $"cuando pulsar cierra la ventana de trabajo, la cuenta lo dice, y no «ahora estás en Meet» como si Ü hubiera ido allí ({r.Cuenta})");
        Debe(!r.Aprendido && g.DesdeAqui("uia://mspaint.exe/sin-titulo").All(a => a.Destino != "uia://chrome.exe/meet"),
            "y no se aprende una arista de Paint a Meet: Ü no cruzó ninguna puerta, la ventana se cerró");
    }

    private static void ElClicSinRatonVaPorElPatron()
    {
        // EL CLIC FÍSICO ERA EL CAMINO NORMAL y el patrón el respaldo. Al revés: el patrón no necesita
        // foco ni ratón, y es lo que deja a la persona seguir trabajando (y lo único que cruza a otro
        // escritorio virtual). El físico queda para lo que no lo admite, y devuelve lo que era de la persona.
        var t = Grafico("U.Graph.Surfaces.ComoSePulsa");
        var decidir = t?.GetMethods().FirstOrDefault(m => m.Name == "Decidir" && m.GetParameters().Length == 4);
        var tGesto = Grafico("U.Graph.Surfaces.ComoSePulsa+Gesto");
        var devolver = t?.GetMethod("HayQueDevolver");
        var ejecutarEn = Grafico("U.Graph.Surfaces.UiaSurface")?.GetMethods()
            .FirstOrDefault(m => m.Name == "Execute" && m.GetParameters().Length == 3 && m.GetParameters()[1].ParameterType == typeof(IntPtr));
        Debe(decidir != null && tGesto != null && devolver != null && ejecutarEn != null,
            "todavía no existen «Surfaces.ComoSePulsa.Decidir/HayQueDevolver» ni «UiaSurface.Execute(paso, ventanaObjetivo, …)» (spec 020, promesa 234). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (decidir == null || tGesto == null || devolver == null) return;
        string G(bool invoke, bool toggle, bool seleccion, bool contenidoDeLista) =>
            decidir.Invoke(null, new object[] { invoke, toggle, seleccion, contenidoDeLista })!.ToString()!;
        Debe(G(true, false, false, false) == "Patron", "un botón con Invoke se pulsa por patrón: sin foco y sin ratón, la persona no se entera");
        Debe(G(false, true, false, false) == "Patron", "una casilla con Toggle, igual");
        Debe(G(false, false, true, false) == "Fisico",
            "lo que solo admite selección va por el clic físico: Select() marca sin navegar (el panel del explorador, 2026-08-08)");
        Debe(G(false, false, false, false) == "Fisico", "y lo que no admite ningún patrón, también: es la única forma de tocarlo");
        Debe(G(true, false, true, true) == "Fisico",
            "un elemento de lista con Invoke por herencia va por el físico: invocar un ListItem devuelve true sin abrir nada (2026-08-02)");
        bool D(string gesto, IntPtr antes, IntPtr despues) => (bool)devolver.Invoke(null, new object[] { Enum.Parse(tGesto, gesto), antes, despues })!;
        Debe(D("Fisico", (IntPtr)5, (IntPtr)9), "tras un clic físico que cambió el foco, hay que devolvérselo a la persona");
        Debe(!D("Fisico", (IntPtr)5, (IntPtr)5), "si el foco no cambió no se toca nada");
        Debe(!D("Patron", (IntPtr)5, (IntPtr)9), "y por patrón nunca: no se tocó ni el foco ni el ratón");
        Debe(!D("Fisico", IntPtr.Zero, (IntPtr)9), "sin foco previo conocido no se devuelve nada a ciegas");
    }

    private static void EscribirVaALaVentanaDeTrabajo()
    {
        // «no pude escribir en «Git Bash»: no encontré el elemento «» (Git Bash)» (20:01:57) y «NO escribo: no hay
        // ningún campo de texto abierto. El foco lo tiene «…;ct=TabItem»» (20:00:58): el nombre se mandaba como
        // selector, y una terminal no tiene campo que aceptar. Escribir tiene que decidir como pulsar: en la
        // ventana de trabajo, por patrón cuando hay campo, por teclado cuando es una terminal.
        var t = Grafico("U.Graph.Surfaces.ComoSeEscribe");
        var decidir = t?.GetMethod("Decidir");
        var esTerminal = t?.GetMethod("EsTerminal");
        var noEncontre = t?.GetMethod("NoEncontre");
        var teclear = Grafico("U.Graph.Surfaces.UiaSurface")?.GetMethod("TeclearEnLaVentana");
        var campo = Grafico("U.Graph.Surfaces.UiaSurface")?.GetMethod("CampoDeTexto");
        Debe(decidir != null && esTerminal != null && noEncontre != null && teclear != null && campo != null,
            "todavía no existen «Surfaces.ComoSeEscribe.Decidir/EsTerminal/NoEncontre» ni «UiaSurface.TeclearEnLaVentana/CampoDeTexto» (spec 021, promesa 235). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (decidir == null || esTerminal == null || noEncontre == null) return;
        bool T(string proceso, string clase) => (bool)esTerminal.Invoke(null, new object[] { proceso, clase })!;
        Debe(T("WindowsTerminal.exe", "CASCADIA_HOSTING_WINDOW_CLASS"), "Windows Terminal es una terminal, con o sin .exe");
        Debe(T("mintty", "mintty"), "Git Bash (mintty) es una terminal");
        Debe(T("conhost.exe", "ConsoleWindowClass") && T("cmd", "") && T("powershell", "") && T("pwsh.exe", ""),
            "conhost, cmd, PowerShell y pwsh son terminales, por proceso o por clase de ventana");
        Debe(!T("chrome.exe", "Chrome_WidgetWin_1") && !T("mspaint.exe", ""), "Chrome y Paint no lo son: ahí se escribe en campos");
        string D(bool hayCampo, bool terminal) => decidir.Invoke(null, new object[] { hayCampo, terminal })!.ToString()!;
        Debe(D(true, false) == "Valor", "con un campo de texto en la ventana de trabajo se escribe por patrón Value: sin foco ni ratón");
        Debe(D(true, true) == "Valor", "y si por lo que sea una terminal expone un campo, también: el patrón gana");
        Debe(D(false, true) == "Teclado", "en una terminal sin campo se teclea: es lo único que una consola escucha");
        Debe(D(false, false) == "SinCampo", "sin campo y sin terminal no se escribe a ciegas: escribir sobre lo seleccionado es renombrar (2026-08-03)");
        string m = (string)noEncontre.Invoke(null, new object[] { "Git Bash", "MINGW64:/c/Users/felip" })!;
        Debe(m.Contains("«Git Bash»") && m.Contains("MINGW64") && !m.Contains("«»"),
            $"el error nombra el campo pedido y la ventana donde se buscó, no «no encontré el elemento «»» ({m})");
    }

    private static void DesbloquearPulsaElBotonQueLeyo()
    {
        // 19:42:29 y 19:43:46: la barra «Continúa por donde lo dejaste» de Chrome se leyó como diálogo con opción
        // «Cerrar»; map_unblock construyó uia:name=Cerrar;ct=Button y lo resolvió en TODA la ventana, donde el
        // primer «Cerrar» es el de la barra de título. Cerró Chrome dos veces y dijo «DESBLOQUEADO · resuelto».
        var tDialogo = Capacidad("U.WindowsClient.Navigation.Desbloqueo+Dialogo");
        var tTools = Capacidad("U.WindowsClient.Mcp.SurfaceMapTools");
        var desbloquear = tTools?.GetMethod("Desbloquear");
        var lector = tTools?.GetProperty("LeerDialogo");
        var leerDialogo = Capacidad("U.WindowsClient.Navigation.Interrupcion")?.GetMethod("LeerDialogo");
        Debe(tDialogo != null && desbloquear != null && lector != null && leerDialogo != null,
            "todavía no existen «Navigation.Desbloqueo.Dialogo», «Interrupcion.LeerDialogo» ni «SurfaceMapTools.Desbloquear/LeerDialogo» (spec 021, promesa 236). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (tDialogo == null || tTools == null || desbloquear == null || lector == null) return;

        var ctor = tTools.GetConstructors().First();
        var pWhere = ctor.GetParameters()[0].ParameterType;
        var tLoc = pWhere.GetGenericArguments()[0];
        var where = System.Linq.Expressions.Expression.Lambda(pWhere, System.Linq.Expressions.Expression.Constant(null, tLoc)).Compile();
        var tools = ctor.Invoke(new object[] { where });

        var pulsados = new List<string>();
        bool abierto = true;
        Func<string, bool> pulsar = o => { pulsados.Add(o); abierto = false; return true; };
        object Dialogo(params string[] opciones) => Activator.CreateInstance(tDialogo, "Barra de información",
            (IReadOnlyList<string>)new[] { "Continúa por donde lo dejaste: Chrome puede restaurar tus pestañas cada vez que lo reinicies." },
            (IReadOnlyList<string>)opciones, pulsar)!;
        void Inyectar(Func<object?> fuente)
        {
            var fType = typeof(Func<>).MakeGenericType(tDialogo);
            var body = System.Linq.Expressions.Expression.Convert(
                System.Linq.Expressions.Expression.Invoke(System.Linq.Expressions.Expression.Constant(fuente)), tDialogo);
            lector.SetValue(tools, System.Linq.Expressions.Expression.Lambda(fType, body).Compile());
        }
        string Desbloquea(string at, string choose) => (string)desbloquear.Invoke(tools, new object[] { at, choose })!;

        var barra = Dialogo("Cerrar");
        Inyectar(() => abierto ? barra : null);
        string cuenta = Desbloquea("Barra de información", "Cerrar");
        Debe(pulsados.SequenceEqual(new[] { "Cerrar" }),
            $"la opción se pulsa sobre el botón que el detector leyó dentro del diálogo, y ninguna otra ({string.Join(",", pulsados)})");
        Debe(cuenta.Contains("DESBLOQUEADO") && cuenta.Contains("Cerrar"), $"y se cuenta como desbloqueo ({cuenta})");
        Debe(!cuenta.Contains("no sé ponerme delante") && !cuenta.Contains("no hay ningún camino"),
            $"un `at` que es el título del diálogo no es un sitio al que volver: no se intenta ninguna reanudación ({cuenta})");

        pulsados.Clear(); abierto = true;
        var confirmacion = Dialogo("Eliminar", "Cancelar");
        Inyectar(() => abierto ? confirmacion : null);
        string veto = Desbloquea("", "Eliminar");
        Debe(pulsados.Count == 0 && veto.Contains("NO pulso"), $"el veto de lo destructivo sigue igual: «Eliminar» no se pulsa ni pidiéndolo ({veto})");
        Desbloquea("", "");
        Debe(pulsados.SequenceEqual(new[] { "Cancelar" }), $"y sin elección la política elige la opción que no compromete nada ({string.Join(",", pulsados)})");

        pulsados.Clear(); abierto = true;
        var decision = Dialogo("Guardar", "Reemplazar");
        Inyectar(() => abierto ? decision : null);
        string atascado = Desbloquea("", "");
        Debe(pulsados.Count == 0 && atascado.Contains("ATASCADO"), $"una decisión de verdad no se adivina: se describe y decide la capa consciente ({atascado})");
    }

    private static void LaEscaleraDelClicSinCursor()
    {
        // 8 de 11 clics de la corrida fueron por Invoke; los 3 físicos fueron un campo web sin patrón y una pestaña
        // con solo selección. Entre el patrón y el ratón real cabe un peldaño que no mueve el cursor: el clic por
        // mensaje a la ventana del elemento. El ratón real queda para lo que se midió que solo él navega.
        var t = Grafico("U.Graph.Surfaces.ComoSePulsa");
        var decidir = t?.GetMethods().FirstOrDefault(m => m.Name == "Decidir" && m.GetParameters().Length == 5);
        var tGesto = Grafico("U.Graph.Surfaces.ComoSePulsa+Gesto");
        Debe(decidir != null && tGesto != null && Enum.GetNames(tGesto).Contains("Mensaje"),
            "todavía no existe «ComoSePulsa.Decidir» con el punto pulsable ni el gesto «Mensaje» (spec 021, promesa 237). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (decidir == null) return;
        string G(bool invoke, bool toggle, bool seleccion, bool lista, bool punto) =>
            decidir.Invoke(null, new object[] { invoke, toggle, seleccion, lista, punto })!.ToString()!;
        Debe(G(true, false, false, false, true) == "Patron", "con Invoke, el patrón: gana a todo");
        Debe(G(false, true, false, false, true) == "Patron", "con Toggle, igual");
        Debe(G(false, false, false, false, true) == "Mensaje", "sin patrón pero con punto pulsable, el clic por mensaje: no mueve el cursor ni necesita el foco");
        Debe(G(false, false, true, false, true) == "Mensaje", "una pestaña que solo admite selección también va por mensaje antes que por el ratón");
        Debe(G(false, false, false, false, false) == "Fisico", "sin punto pulsable no hay a dónde mandar el mensaje: el ratón real, que trae la ventana y lo busca");
        Debe(G(true, false, true, true, true) == "Fisico", "el contenido de una lista va directo al ratón real: ahí ni el patrón ni el mensaje abren (2026-08-02, 2026-08-08)");
        var viejo = t!.GetMethods().FirstOrDefault(m => m.Name == "Decidir" && m.GetParameters().Length == 4);
        Debe(viejo != null && viejo.Invoke(null, new object[] { false, false, false, false })!.ToString() == "Fisico",
            "la decisión de la promesa 234, sin punto pulsable, sigue diciendo lo mismo");
    }

    private static void CerrarLaVentanaSigueSiendoUnClicNormal()
    {
        // 19:40:43: el modelo decidió cerrar el navegador («Cierra la ventana del navegador»), la mano lo resolvió
        // en la ventana de trabajo, lo pulsó por Invoke y contó «la ventana en la que trabajaba ya no existe».
        // Eso estuvo bien, y arreglar el desbloqueo no puede convertir «Cerrar» en un verbo prohibido.
        Debe(!SafeToClick.EsDestructivo("Cerrar", out string motivo) && !SafeToClick.EsDestructivo("Cerrar Microsoft Store", out _),
            $"«Cerrar» no es un verbo destructivo: cerrar una ventana es una decisión del modelo, no un daño ({motivo})");
        var decidir = Grafico("U.Graph.Surfaces.ComoSePulsa")?.GetMethods().FirstOrDefault(m => m.Name == "Decidir" && m.GetParameters().Length == 4);
        Debe(decidir != null && decidir.Invoke(null, new object[] { true, false, false, false })!.ToString() == "Patron",
            "el botón de cerrar de una ventana expone Invoke y se pulsa por patrón: sin cursor");
        var g = new Nucleo.Grafo();
        g.Observar("web://instagram.com", new[] { new Nucleo.Elemento("uia:name=Cerrar;ct=Button", "Cerrar", "Button") });
        string donde = "web://instagram.com";
        var pulsar = new PulsarSegunElNucleo(g, () => donde, (sel, et) => { donde = "uia://claude.exe/claude"; return true; }) { EsperaMaximaMs = 240 };
        var aviso = Capacidad("U.WindowsClient.Navigation.PulsarSegunElNucleo")?.GetProperty("AvisoDeLaVentana");
        aviso?.SetValue(pulsar, (Func<string>)(() => "la ventana en la que trabajaba («web://instagram.com») ya no existe"));
        var r = pulsar.Pulsa("uia:name=Cerrar;ct=Button", "Cerrar");
        Debe(r.SePudo && r.Cuenta.Contains("pulsé «Cerrar»") && r.Cuenta.Contains("ya no existe") && !r.Aprendido,
            $"la cuenta dice que la ventana ya no existe y no aprende un tramo hasta Claude: Ü no fue a ninguna parte ({r.Cuenta})");
    }

    private static void GuardarYNoGuardarSePuedenPulsar()
    {
        // 20:26:37: «NO pulso «No guardar»: «No guardar» contiene «guardar»». El veto de responder diálogos
        // recorría la lista del EXPLORADOR AUTÓNOMO, que contesta otra pregunta —qué se puede tocar mientras
        // se mapea, sin nadie mirando— y por eso incluye «guardar» junto a «formatear». Dos preguntas, dos
        // listas. Y la negación invierte el verbo: «No eliminar» es justo la opción que salva el archivo.
        Debe(!SafeToClick.EsDestructivo("Guardar", out string m1), $"guardar no destruye nada: es lo que la persona pidió ({m1})");
        Debe(!SafeToClick.EsDestructivo("No guardar", out string m2), $"y decidir no guardar es una decisión legítima, no un daño ({m2})");
        Debe(!SafeToClick.EsDestructivo("Save", out _) && !SafeToClick.EsDestructivo("Don't save", out _),
            "en inglés igual: la UI de Windows mezcla los dos idiomas");
        Debe(!SafeToClick.EsDestructivo("Guardar como...", out _) && !SafeToClick.EsDestructivo("Aplicar", out _),
            "guardar en otro sitio y aplicar lo escrito son la misma familia");
        Debe(SafeToClick.EsDestructivo("Eliminar", out _) && SafeToClick.EsDestructivo("Delete", out _)
             && SafeToClick.EsDestructivo("Vaciar papelera", out _) && SafeToClick.EsDestructivo("Formatear", out _)
             && SafeToClick.EsDestructivo("Desinstalar", out _),
            "lo que destruye datos sigue vetado aunque lo pida el modelo: un fallo aquí no es un mapa peor, es un archivo perdido");
        Debe(SafeToClick.EsDestructivo("Reiniciar ahora", out _) && SafeToClick.EsDestructivo("Cerrar sesión", out _),
            "apagar, reiniciar o cerrar la sesión se lleva por delante lo que la persona tenía abierto");
        Debe(SafeToClick.EsDestructivo("Enviar", out _) && SafeToClick.EsDestructivo("Publicar", out _),
            "sacar los datos de la máquina no se deshace");
        Debe(SafeToClick.EsDestructivo("Aceptar", out _) && SafeToClick.EsDestructivo("Permitir", out _)
             && SafeToClick.EsDestructivo("Instalar", out _) && SafeToClick.EsDestructivo("Pagar", out _),
            "consentir, permitir, instalar o pagar compromete a la persona, y eso lo pulsa ella");
        Debe(!SafeToClick.EsDestructivo("No eliminar", out string m3),
            $"una etiqueta que NIEGA el verbo no es ese verbo: «No eliminar» es la que salva el archivo ({m3})");
        Debe(!SafeToClick.EsDestructivo("Don't delete", out _) && !SafeToClick.EsDestructivo("No permitir", out _),
            "y declinar un permiso tampoco compromete nada");
        Debe(SafeToClick.EsDestructivo("Eliminar y no volver a preguntar", out _),
            "pero un «no» que no va pegado al verbo no lo salva: ahí el verbo manda");
        // Y EL EXPLORADOR AUTÓNOMO NO SE RELAJA: es la otra pregunta, y su lista sigue entera.
        Debe(!SafeToClick.Auto("Guardar", "hyperlink", out _) && !SafeToClick.Auto("Opciones", "listitem", out _),
            "mapear solo es navegar: el explorador autónomo sigue sin pulsar «Guardar» ni «Opciones»");
        Debe(SafeToClick.Auto("Documentos", "treeitem", out _), "y lo que era navegable lo sigue siendo");
    }

    private static void LaCaritaVaADondeSePulsa()
    {
        // DESDE LA SPEC 020 LA MAYORÍA DE LOS CLICS VAN SIN RATÓN, y la carita solo sabía seguir al cursor
        // cuando el cursor se movía de verdad: dejó de acompañar a la mano justo cuando la mano mejoró. El
        // dueño lo pidió con su curva: «fluido rápidamente, no brusco, con una aceleración suave pero rápida».
        var t = Grafico("U.Graph.Surfaces.ComoViajaLaCarita");
        var esClic = t?.GetMethod("EsClic");
        var mereceViaje = t?.GetMethod("MereceViaje");
        var cuanto = t?.GetMethod("Cuanto");
        var curva = t?.GetMethod("Curva");
        var pulso = Grafico("U.Graph.Surfaces.UiaSurface")?.GetEvent("Pulso");
        Debe(esClic != null && mereceViaje != null && cuanto != null && curva != null && pulso != null,
            "todavía no existen «Surfaces.ComoViajaLaCarita.EsClic/MereceViaje/Cuanto/Curva» ni el aviso «UiaSurface.Pulso» (spec 022, promesa 240). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (esClic == null || mereceViaje == null || cuanto == null || curva == null) return;
        bool Clic(string a) => (bool)esClic.Invoke(null, new object[] { a })!;
        Debe(Clic("click") && Clic("doubleclick") && Clic("rightclick"),
            "los tres clics de la mano avisan de dónde cayeron: para eso se mueve la carita, para que se vea quién pulsa");
        Debe(!Clic("input") && !Clic("select") && !Clic("scroll") && !Clic(""),
            "escribir, elegir en una lista o desplazar no es pulsar: la carita no sale corriendo por eso");
        bool Merece(double d) => (bool)mereceViaje.Invoke(null, new object[] { d })!;
        Debe(!Merece(12) && Merece(200), "un salto de 12 px es un parpadeo, no un viaje; 200 px sí se ve");
        double C(double x) => (double)curva.Invoke(null, new object[] { x })!;
        Debe(Math.Abs(C(0)) < 1e-6 && Math.Abs(C(1) - 1) < 1e-6, "el viaje empieza donde estaba y termina exactamente donde se pulsó");
        bool sube = true, rebota = false; double previo = double.MinValue;
        for (int i = 0; i <= 200; i++)
        {
            double v = C(i / 200.0);
            if (v < previo - 1e-9) sube = false;
            if (v > 1 + 1e-9) rebota = true;
            previo = v;
        }
        Debe(sube, "fluida: no se devuelve a mitad de camino");
        Debe(!rebota, "y no rebota: un rebote está bien una vez, pero en CADA clic se lee como gelatina");
        Debe(C(0.02) < 0.02, "empieza acelerando y no de un tirón: en el primer 2% del tiempo no ha hecho ni el 2% del camino");
        Debe(C(1.0 / 3) > 0.5, "pero es rápida: al primer tercio del tiempo ya lleva más de medio camino");
        var corto = (TimeSpan)cuanto.Invoke(null, new object[] { 30.0 })!;
        var largo = (TimeSpan)cuanto.Invoke(null, new object[] { 3000.0 })!;
        Debe(corto.TotalMilliseconds >= 120 && corto <= largo, $"un salto corto dura poco pero se ve ({corto.TotalMilliseconds:0} ms)");
        Debe(largo.TotalMilliseconds <= 450, $"y cruzar la pantalla entera no pasa de 450 ms: rápido es parte de lo pedido ({largo.TotalMilliseconds:0} ms)");
    }

    private static void ElNotchEsBlancoYNegro()
    {
        // QUITAR EL COLOR SE DESHACE SOLO: el que anada un estado dentro de tres semanas vera que los
        // estados se distinguen por tono y seguira el patron. Por eso la regla se juzga, no se comenta.
        var t = Capacidad("U.WindowsClient.Ui.PaletaDelNotch");
        var estados = Capacidad("U.WindowsClient.Ui.EstadoDelNotch");
        var delPunto = t?.GetMethod("DelPunto");
        var esAro = t?.GetMethod("EsAro");
        var late = t?.GetMethod("Late");
        Debe(t != null && estados != null && delPunto != null && esAro != null && late != null,
            "todavia no existen «Ui.PaletaDelNotch» ni «Ui.EstadoDelNotch» (spec 023, promesa 242). "
            + "La promesa esta escrita y en rojo, que es donde tiene que estar");
        if (t == null || estados == null || delPunto == null || esAro == null || late == null) return;

        var pintados = t.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(uint))
            .Select(f => (f.Name, Valor: (uint)f.GetRawConstantValue()!))
            .ToList();
        Debe(pintados.Count >= 6, $"la paleta del notch nombra lo que pinta, y son varias cosas ({pintados.Count})");
        foreach (var (nombre, v) in pintados)
        {
            byte r = (byte)(v >> 16), g = (byte)(v >> 8), b = (byte)v;
            Debe(r == g && g == b,
                $"«{nombre}» tiene tono ({r:X2}{g:X2}{b:X2}): en el notch, rojo, verde y azul valen lo mismo o es un color");
        }

        object E(string n) => Enum.Parse(estados, n);
        uint Punto(string n) => (uint)delPunto.Invoke(null, new[] { E(n) })!;
        bool Aro(string n) => (bool)esAro.Invoke(null, new[] { E(n) })!;
        bool Latir(string n) => (bool)late.Invoke(null, new[] { E(n) })!;

        Debe(Aro("Fallo") && !Aro("Hecho") && !Aro("EnCurso") && !Aro("Omitido"),
            "el fallo se dice con la FORMA —un aro hueco— y no con el rojo: sin eso, quitar el color se lleva por delante la informacion");
        Debe(Latir("EnCurso") && !Latir("Hecho") && !Latir("Fallo") && !Latir("Omitido"),
            "lo que esta en curso late, y lo que termino se queda quieto: es la otra mitad de lo que decia el tono");
        Debe(Punto("Omitido") != Punto("Hecho"),
            "lo omitido baja de luz respecto a lo hecho: dos grises distintos siguen siendo dos estados distintos");
        Debe((byte)(Punto("Hecho") >> 24) > (byte)(Punto("Omitido") >> 24),
            "y baja, no sube: lo que no se hizo pesa menos en la vista que lo que si");

        // Y LA PALETA DE LA BARRA GRANDE SIGUE TENIENDO COLOR: son dos sitios distintos y solo uno se limpio.
        var barra = Capacidad("U.WindowsClient.Ui.UiPalette");
        var vivo = barra?.GetField("Vivo", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        Debe(vivo != null && Prop(vivo, "R")!.ToString() != Prop(vivo, "G")!.ToString(),
            "la barra grande conserva su verde: el dueno pidio limpiar el notch, no el resto");
    }

    private static void EscribirSeCompruebaEnElCampo()
    {
        // MEDIDO CON UNA SONDA SOBRE EL CAMPO REAL (2026-09-15): SetValue no lanza, no falla y el valor
        // sigue vacío; teclear con el teclado sí entra. El editor de un sitio moderno es un contenteditable
        // gobernado por JavaScript, y escribir su valor por accesibilidad no dispara los eventos que ese
        // JavaScript escucha. UIA acepta la orden y devuelve éxito: nadie miente, nadie comprueba.
        var t = Grafico("U.Graph.Surfaces.ComoSeEscribe");
        var cuajo = t?.GetMethod("Cuajo");
        var teclearEnElCampo = Grafico("U.Graph.Surfaces.UiaSurface")?.GetMethod("TeclearEnElCampo");
        Debe(cuajo != null && teclearEnElCampo != null,
            "todavía no existen «ComoSeEscribe.Cuajo» ni «UiaSurface.TeclearEnElCampo» (spec 024, promesa 243). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (cuajo == null) return;
        bool C(string pedido, string? leido) => (bool)cuajo.Invoke(null, new object?[] { pedido, leido })!;

        Debe(!C("hola", ""), "un campo que se queda VACÍO después de escribirle no se quedó con nada: es el caso de Instagram");
        Debe(!C("hola", "\n"), "y el «vacío» de un editor web es un salto de línea, que es literalmente lo que devolvió la sonda");
        Debe(!C("hola", "   "), "espacios tampoco son el texto");
        Debe(C("hola", "hola"), "si el campo dice lo que se le escribió, cuajó");
        Debe(C("hola", "hola\n"), "con el salto de línea que añade el editor, también");
        Debe(C("hola", " hola "), "y con los espacios de más del propio control");
        Debe(!C("hola", "adiós"), "si dice otra cosa, no cuajó: eso es haber escrito en otro sitio");
        Debe(C("hola", null),
            "LO QUE NO SE PUEDE LEER NO SE JUZGA: hay controles que no devuelven su valor, y ahí se deja pasar como hasta hoy. "
            + "El arreglo actúa sobre una prueba de que el texto no entró, nunca sobre una sospecha");
        Debe(C("", "lo que sea"), "escribir vacío no se puede desmentir");
    }

    private static void UDecideEnVezDePreguntar()
    {
        // «SIEMPRE QUE LE PIDO UNA TAREA COMPLEJA ME EMPIEZA A PREGUNTAR COSAS» (el dueño, 2026-09-15). Las
        // instrucciones ya prohibían pedir permiso, pero dejaban abierta la puerta de al lado —preguntar por
        // el dato que falta— sin decir cuándo un dato se deduce. En una tarea larga cualquier paso tiene un
        // dato opinable, así que la excepción se comía la regla.
        var t = Cap004("U.WindowsClient.Voice.ConversacionEnVivo");
        var prop = t?.GetProperty("InstruccionesNormales",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        Debe(t != null && prop != null, "no encuentro «ConversacionEnVivo.InstruccionesNormales»");
        if (prop == null) return;
        string texto = (string)prop.GetValue(null)!;

        Debe(texto.Contains("ELIGE TÚ", StringComparison.Ordinal),
            "está dicha la regla nueva, y en mayúsculas como las demás que se incumplían");
        Debe(texto.Contains("no se puede deshacer", StringComparison.Ordinal),
            "y cuál es la ÚNICA frontera para preguntar: que elegir mal no tenga vuelta atrás");
        Debe(texto.Contains("dilo al terminar", StringComparison.Ordinal) || texto.Contains("dices cuál elegiste", StringComparison.Ordinal),
            "elegir sin contarlo es adivinar a escondidas: la regla obliga a decir qué se eligió, después de hacerlo");
        Debe(texto.Contains("no la trocees en preguntas", StringComparison.Ordinal),
            "y una tarea larga se hace entera: trocearla en preguntas es la forma en que el interrogatorio volvía");
        Debe(!texto.Contains("pregunta por el DATO que te falta, y solo", StringComparison.Ordinal),
            "y la puerta de al lado se cierra: mientras el texto invite a preguntar por el dato, el modelo va a preferir preguntar");
        Debe(texto.Contains("NO PIDAS PERMISO", StringComparison.Ordinal),
            "lo que ya funcionaba se queda: la regla nueva no sustituye a la vieja, la completa");
    }

    private static void LasEsperasSeMidenConElReloj()
    {
        // EL COMPÁS es la pieza que los cuatro bucles comparten: sabe cuánto llevas esperando DE VERDAD.
        // Antes cada bucle sumaba 120 por vuelta y además pagaba el sondeo, así que el presupuesto era
        // ficticio y se inflaba con lo que costara mirar la pantalla.
        var t = Capacidad("U.WindowsClient.Navigation.Compas");
        Debe(t != null, "todavía no existe «Navigation.Compas» (spec 025, promesa 245). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null) return;

        long reloj = 0;
        var compas = Activator.CreateInstance(t, 1200, (Func<long>)(() => reloj))!;
        var seAcabo = t.GetProperty("SeAcabo")!;
        var transcurrido = t.GetProperty("Transcurrido")!;
        bool Acabo() => (bool)seAcabo.GetValue(compas)!;
        long Llevo() => (long)transcurrido.GetValue(compas)!;

        Debe(!Acabo() && Llevo() == 0, "recién empezado no se ha acabado nada");
        reloj = 400;  Debe(!Acabo() && Llevo() == 400, "lleva lo que dice el reloj, no las vueltas que haya dado");
        reloj = 1199; Debe(!Acabo(), "un milisegundo antes del tope, todavía se espera");
        reloj = 1200; Debe(Acabo(), "y al llegar al tope se acabó: tres sondeos caros agotan un presupuesto de 1200, no diez");

        // Y AHORA LA CLASE DE VERDAD, cronometrada: un «dónde estoy» de 400 ms como el de la máquina del
        // dueño, donde este bucle tardaba treinta segundos en contestar.
        const int LENTO = 400, TOPE = 1200;
        var g = new Nucleo.Grafo();
        g.Observar("uia://app/a", new[] { new Nucleo.Elemento("uia:name=Ir", "Ir", "Button") });
        string donde = "uia://app/a";
        var pulsar = new PulsarSegunElNucleo(g,
            () => { Thread.Sleep(LENTO); return donde; },   // el sondeo caro
            (sel, et) => true)                              // pulsa bien, pero la pantalla no cambia
            { EsperaMaximaMs = TOPE };
        var cronometro = System.Diagnostics.Stopwatch.StartNew();
        var r = pulsar.Pulsa("uia:name=Ir", "Ir");
        long tardo = cronometro.ElapsedMilliseconds;
        Debe(r.SePudo, "el clic se da igual: esto mide el tiempo, no el resultado");
        Debe(tardo < TOPE + 3 * LENTO,
            $"esperar {TOPE} ms con un sondeo de {LENTO} ms no puede tardar {tardo} ms: con el presupuesto "
            + "ficticio eran diez vueltas de 400, y así es como un «1,8 s» acababa siendo medio minuto");
    }

    private static void LoQueSeAcabaDeMirarNoSeVuelveAMirar()
    {
        // Contestar «dónde estás» costaba 2.771 ms —más que leer la pantalla entera, 823— porque cada
        // pregunta volvía a identificar la ventana de trabajo en vivo. Lo que se acaba de mirar se recuerda
        // un instante, como ya hace LaBarraDeTareas con su caducidad.
        var t = Capacidad("U.WindowsClient.Navigation.MemoriaCorta`1");
        Debe(t != null, "todavía no existe «Navigation.MemoriaCorta» (spec 025, promesa 246). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (t == null) return;
        var cerrado = t.MakeGenericType(typeof(string));
        long ahora = 0;
        int llamadas = 0;
        var mem = Activator.CreateInstance(cerrado, 500, (Func<long>)(() => ahora))!;
        var pide = cerrado.GetMethod("Pide")!;
        var olvida = cerrado.GetMethod("Olvida")!;
        string Pide() => (string)pide.Invoke(mem, new object[] { (Func<string>)(() => { llamadas++; return $"valor{llamadas}"; }) })!;

        Debe(Pide() == "valor1" && llamadas == 1, "la primera vez se va a la fuente");
        ahora = 200; Pide(); ahora = 400; Pide();
        Debe(llamadas == 1, $"dentro de la caducidad se contesta de memoria y NO se vuelve a mirar ({llamadas} lecturas)");
        ahora = 501;
        Debe(Pide() == "valor2" && llamadas == 2, "pasada la caducidad se vuelve a preguntar: es memoria corta, no un congelado");
        olvida.Invoke(mem, null);
        Debe(Pide() == "valor3" && llamadas == 3,
            "y se puede olvidar a mano: cuando una acción acaba de cambiar la pantalla, lo recordado ya no vale");
    }

    private static void TrasEscribirHayTresRespuestas()
    {
        var t = Grafico("U.Graph.Surfaces.ComoSeEscribe");
        var tras = t?.GetMethod("TrasEscribir");
        var seDa = t?.GetMethod("SeDaPorEscrito");
        Debe(tras != null && seDa != null,
            "todavía no existen «ComoSeEscribe.TrasEscribir» ni «SeDaPorEscrito» (spec 026, promesa 247). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (tras == null || seDa == null) return;
        string V(string pedido, string? antes, string? despues)
            => tras.Invoke(null, new object?[] { pedido, antes, despues })!.ToString()!;
        bool Escrito(string veredicto)
            => (bool)seDa.Invoke(null, new[] { Enum.Parse(tras.ReturnType, veredicto) })!;

        Debe(V("hola", "", "hola") == "Cuajo", "si el campo enseña el texto, cuajó");
        Debe(V("hola", "", "adiós") == "NoCuajo", "si enseña otra cosa, no cuajó: fue a parar a otro sitio");
        Debe(V("hola", "", "") == "MudoNoSeSabe",
            "UN CAMPO QUE NO CUENTA LO QUE TIENE NO ES UN FALLO: Google Docs dibuja el texto en un lienzo y "
            + "responde vacío. Leerlo como «no entró» es lo que hizo que un informe se escribiera cuatro veces");
        Debe(V("hola", "\n", "\n") == "MudoNoSeSabe", "y el vacío de un editor web es un salto de línea: tampoco cuenta nada");
        Debe(V("hola", null, null) == "MudoNoSeSabe", "lo ilegible tampoco se juzga");
        Debe(V("", "lo que sea", "lo que sea") == "Cuajo", "escribir vacío no se puede desmentir");

        // EL TEXTO, NO SU FORMATO: el editor normaliza los saltos y eso no es no haber escrito.
        string largo = "TÍTULO DEL INFORME\n\nResumen ejecutivo\n\nEste documento sintetiza la investigación.";
        string comoLoGuarda = "TÍTULO DEL INFORME Resumen ejecutivo Este documento sintetiza la investigación.";
        Debe(V(largo, "", comoLoGuarda) == "Cuajo",
            "con los saltos normalizados por el editor, el texto SÍ está: exigirlo literal es lo que dio el falso fallo del Bloc de notas");
        Debe(V(largo, "", "TÍTULO DEL INFORME Resumen ejecutivo Este documento sintetiza") == "Cuajo",
            "y con un texto largo basta reconocer su comienzo: un editor puede recortar, envolver o paginar el resto");

        Debe(Escrito("Cuajo") && Escrito("MudoNoSeSabe") && !Escrito("NoCuajo"),
            "solo el «no cuajó» se cuenta como fallo: reescribir un informe entero es peor daño que no poder confirmarlo");
    }

    private static void ElClicQueNoMovioNadaSeRepiteUnaVez()
    {
        // GMAIL: Ü resolvió «Compose» bien —la carita se puso a su lado—, lo pulsó por patrón, la llamada
        // devolvió éxito y la redacción no se abrió. Probado después sobre el mismo botón con Gmail
        // asentado, ese mismo Invoke la abre a la primera. El gesto era bueno; el momento, no. Enterarse
        // cuesta una vuelta al modelo (5-10 s); repetirlo aquí cuesta uno.
        //
        // Y LA 83 TIENE RAZÓN EN LO SUYO: «Guardar» hace su trabajo SIN cambiar de pantalla, así que desde
        // fuera un «Guardar» que funcionó y un «Compose» que se perdió son idénticos. La señal que sí los
        // separa la tiene el terreno: una puerta que YA se vio llevar a algún sitio tiene destino aprendido;
        // un botón que aplica algo no lo tiene ni lo tendrá. Solo se repite lo que sabemos que navega.
        var t = Grafico("U.Graph.Surfaces.ComoSePulsa");
        var repetir = t?.GetMethod("HayQueRepetir");
        Debe(repetir != null, "todavía no existe «ComoSePulsa.HayQueRepetir» (spec 026, promesa 248). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar");
        if (repetir == null) return;
        bool R(bool cambioPantalla, int vivosAntes, int vivosDespues, bool destructivo, bool sabeQueLleva, bool yaRepetido)
            => (bool)repetir.Invoke(null, new object[] { cambioPantalla, vivosAntes, vivosDespues, destructivo, sabeQueLleva, yaRepetido })!;

        Debe(R(false, 40, 40, false, true, false),
            "una puerta que el terreno ya vio llevar a algún sitio y que no movió nada: el clic se perdió, se repite");
        Debe(!R(false, 40, 40, false, false, false),
            "PERO «GUARDAR» NO: un botón que hace su trabajo sin cambiar de pantalla no tiene destino aprendido, y "
            + "repetirlo sería guardar dos veces. Es lo que promete la 83 desde el 2026-08-03, y sigue en pie");
        Debe(!R(true, 40, 40, false, true, false), "si la pantalla cambió, el clic hizo su trabajo");
        Debe(!R(false, 40, 47, false, true, false),
            "si cambió lo que hay vivo, algo se abrió —un menú, un panel— y repetirlo lo desharía");
        Debe(!R(false, 40, 33, false, true, false), "y si desapareció algo, también hizo efecto");
        Debe(!R(false, 40, 40, true, true, false),
            "lo que no se puede deshacer no se repite NUNCA, aunque sepamos a dónde lleva");
        Debe(!R(false, 40, 40, false, true, true), "y se repite UNA vez: a la segunda se cuenta lo que pasó");
    }

    /// <summary>Lo que la conversación le manda al panel de costos, anotado. Genérico para no nombrar ConsumoVivo al compilar.</summary>
    private sealed class Reportes
    {
        private readonly List<object> _partes = new();
        public Task Recibe<T>(T parte) { lock (_partes) _partes.Add(parte!); return Task.CompletedTask; }
        public int Cuantos { get { lock (_partes) return _partes.Count; } }
        public object? Primera { get { lock (_partes) return _partes.FirstOrDefault(); } }
    }

    /// <summary>Un trozo de 100 ms de PCM a 24 kHz con UNA muestra de ese pico y el resto a cero.</summary>
    private static byte[] PcmConPico(short pico)
    {
        var b = new byte[4800];
        BitConverter.GetBytes(pico).CopyTo(b, 2400);
        return b;
    }

    /// <summary>
    /// Una ConversacionEnVivo con GPT-Live, sin socket, con SOLO el reloj de sus turnos cambiado por el del
    /// contrato. Null, y la promesa Pendiente, si falta algo que se pide por nombre.
    /// </summary>
    private static (IDisposable Conv, Action<string> Llega, Func<int> Cierres, Type Tipo)? GptLiveConReloj(Func<long> reloj, string promesa)
    {
        var tc = Cap004("U.WindowsClient.Voice.ConversacionEnVivo");
        var procesar = tc?.GetMethod("Procesar", BindingFlags.NonPublic | BindingFlags.Instance);
        var campoReloj = tc?.GetField("_relojDeLosTurnos", BindingFlags.NonPublic | BindingFlags.Instance);
        var tLive = typeof(Voz.Realtime.IProtocolo).Assembly.GetType("Voz.Realtime.ProtocoloGptLive");
        if (tc == null || procesar == null || campoReloj == null || tLive == null)
        { Pendiente("ConversacionEnVivo._relojDeLosTurnos (el reloj de los turnos) con ProtocoloGptLive", promesa, "018"); return null; }
        var live = Activator.CreateInstance(tLive,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.CreateInstance | BindingFlags.OptionalParamBinding,
            null, new[] { Type.Missing, Type.Missing }, null)!;
        var conv = (IDisposable)Activator.CreateInstance(tc, new object?[] { new SurfaceMapTools(() => null), live })!;
        campoReloj.SetValue(conv, reloj);
        var cierres = new int[1];
        tc.GetEvent("Cerro")!.AddEventHandler(conv, (Action)(() => Interlocked.Increment(ref cierres[0])));
        return (conv, json => procesar.Invoke(conv, new object[] { json, CancellationToken.None }), () => Volatile.Read(ref cierres[0]), tc);
    }

    // UN TIC QUE DE VERDAD NO TRAE HECHOS. Era un session.usage.updated, y al integrar las ramas del 2026-09-12 dejó de
    // serlo: la 48 de la voz lo traduce a Hecho.Duracion. Lo que el servidor manda sin parar entre palabras es su
    // silencio —100 ms de ceros exactos (4800 B, 6400 «A» en base64)—, que desde la 44 tampoco es Hecho.Suena.
    private static readonly string TicSinHechos = "{\"type\":\"session.output_audio.delta\",\"delta\":\"" + new string('A', 6400) + "\"}";
    private static string OyeDelUsuario(string s) => JsonSerializer.Serialize(new { type = "session.input_transcript.delta", delta = s });
    private static string DiceLaVozDeU(string s) => JsonSerializer.Serialize(new { type = "session.output_transcript.delta", delta = s });

    /// <remarks>
    /// EL TURNO SE CERRABA A MITAD DE LA TAREA (revisa:regresiones, 2026-09-12). La regla de la 209 cerraba
    /// con 1,5 s sin transcripción de nadie, y con GPT-Live eso pasa mientras corre una herramienta: la voz
    /// dice «Claro», el delegado llama, y si la herramienta tarda el turno se cierra — «Ü dijo: Claro»,
    /// SeguirContandoSiQuedan antes de tiempo, y conducir.ps1 contando su quietud desde el anuncio. La sonda
    /// de los turnos lo midió sobre la línea de tiempo real: sin guardas, 4 cierres a mitad de tarea en 3
    /// corridas; con las guardas y 1500 ms, todavía 4 (entre devolver y hablar pasan 1658-1707 ms); con las
    /// guardas y 2000 ms, ninguno. La voz se juzga por su PICO: el servidor manda audio sin parar también en
    /// silencio (pico 45, medido) y la palabra más floja medida pica en 1152.
    /// </remarks>
    private static void SinMarcasElTurnoEsperaAlTrabajo()
    {
        var t = Cap004("U.WindowsClient.Voice.TurnosSinMarca");
        var oye = t?.GetMethod("Oye");
        var toca = t?.GetMethod("TocaCerrar");
        var devuelta = t?.GetMethod("Devuelta");
        var enCurso = t?.GetProperty("LlamadasEnCurso");
        if (t == null || oye == null || toca == null || devuelta == null || enCurso == null)
        { Pendiente("Voice.TurnosSinMarca (Devuelta, LlamadasEnCurso)", "211", "018"); return; }

        long ahora = 0;
        Func<long> reloj = () => Volatile.Read(ref ahora);
        object Nuevo() => Activator.CreateInstance(t,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.CreateInstance | BindingFlags.OptionalParamBinding,
            null, new object[] { reloj, 1500 }, null)!;
        var ruido = PcmConPico(45);
        var voz = PcmConPico(1152);
        bool Oye(object m, Voz.Realtime.Hecho h) => (bool)oye.Invoke(m, new object[] { h })!;
        bool Cierra(object m) { Oye(m, new Voz.Realtime.Hecho.Suena(ruido)); return (bool)toca.Invoke(m, null)!; }
        void Devuelve(object m, params Voz.Realtime.Llamada[] ls) => devuelta.Invoke(m, new object[] { (IReadOnlyList<Voz.Realtime.Llamada>)ls });
        int EnCurso(object m) => (int)enCurso.GetValue(m)!;
        Voz.Realtime.Llamada Llamada(string id, string nombre) => new(id, nombre, new Dictionary<string, string>());
        Voz.Realtime.Hecho Usuario(string s) => new Voz.Realtime.Hecho.DiceElUsuario(s);
        Voz.Realtime.Hecho DeU(string s) => new Voz.Realtime.Hecho.DiceU(s);

        // UNA LLAMADA EN CURSO SUJETA EL TURNO, y el silencio empieza al devolverla.
        var m = Nuevo();
        ahora = 0; Oye(m, Usuario("abre la configuración"));
        var abrir = Llamada("call_abrir", "map_open_app");
        ahora = 1_200; Oye(m, new Voz.Realtime.Hecho.Pide(new[] { abrir }));
        Debe(EnCurso(m) == 1, $"una llamada que pide el delegado queda en curso (en curso: {EnCurso(m)})");
        ahora = 6_000;
        Debe(!Cierra(m), "con una llamada a herramienta sin devolver no se cierra, aunque lleven 6000 ms sin decir nada");
        Devuelve(m, abrir);
        Debe(EnCurso(m) == 0, $"al devolverla deja de estar en curso (en curso: {EnCurso(m)})");
        ahora = 7_000;
        Debe(!Cierra(m), "a 1000 ms de devolverla no se cierra: el silencio se cuenta desde la devolución, que es cuando el delegado sigue");
        ahora = 7_500; bool alSilencio = Cierra(m);
        ahora = 7_600; bool otraVez = Cierra(m);
        Debe(alSilencio && !otraVez, "a 1500 ms de devolverla se cierra, y una sola vez");

        // DOS A LA VEZ: hasta devolver la última.
        var m2 = Nuevo();
        ahora = 10_000; Oye(m2, Usuario("mira y abre la configuración"));
        var mirar = Llamada("call_mirar", "map_look");
        var abrir2 = Llamada("call_abrir2", "map_open_app");
        ahora = 10_500; Oye(m2, new Voz.Realtime.Hecho.Pide(new[] { mirar, abrir2 }));
        ahora = 11_000; Devuelve(m2, mirar);
        ahora = 14_000;
        Debe(!Cierra(m2), "con dos llamadas pedidas y una sola devuelta sigue sin cerrarse");
        Devuelve(m2, abrir2);
        ahora = 15_600;
        Debe(Cierra(m2), "y con las dos devueltas se cierra tras el silencio");

        // EL HILO QUE EJECUTA PUEDE GANARLE AL QUE RECIBE: devuelta antes de oírse no se queda en curso.
        var m3 = Nuevo();
        ahora = 20_000; Oye(m3, Usuario("silénciate"));
        var callar = Llamada("call_callar", "self_mute");
        ahora = 20_300; Devuelve(m3, callar); Oye(m3, new Voz.Realtime.Hecho.Pide(new[] { callar }));
        Debe(EnCurso(m3) == 0, $"una llamada devuelta antes de que el marcador la oiga no queda en curso (en curso: {EnCurso(m3)})");
        ahora = 21_900;
        Debe(Cierra(m3), "y el turno se cierra tras el silencio, en vez de quedarse abierto para siempre");

        // LA VOZ QUE SUENA SUJETA EL TURNO: la transcripción llega por delante del audio (medido: 650-750 ms).
        var m4 = Nuevo();
        ahora = 30_000; Oye(m4, DeU("Estás en SAP Easy Access."));
        for (long ms = 30_100; ms <= 33_000; ms += 100) { ahora = ms; Oye(m4, new Voz.Realtime.Hecho.Suena(voz)); }
        Debe(!(bool)toca.Invoke(m4, null)!, "mientras suena la voz de Ü no se cierra, aunque su transcripción terminó hace 3000 ms");
        ahora = 34_000;
        Debe(!Cierra(m4), "a 1000 ms de lo último que sonó con voz no se cierra");
        ahora = 34_500;
        Debe(Cierra(m4), "a 1500 ms sí: el silencio se cuenta desde que calla la voz");

        // EL AUDIO EN SILENCIO NO ES VOZ: si contara, con GPT-Live no se cerraría nunca.
        var m5 = Nuevo();
        ahora = 40_000; Oye(m5, DeU("Listo."));
        for (long ms = 40_100; ms <= 41_500; ms += 100) { ahora = ms; Oye(m5, new Voz.Realtime.Hecho.Suena(ruido)); }
        Debe(Cierra(m5), "el audio en silencio que manda el servidor (pico 45, medido) no retrasa el cierre");

        // Y SONIDO SIN NADA DICHO NO ES UN TURNO: un cierre sin frase dispararía Cerro sobre nada.
        var m6 = Nuevo();
        for (long ms = 50_000; ms <= 51_000; ms += 100) { ahora = ms; Oye(m6, new Voz.Realtime.Hecho.Suena(voz)); }
        ahora = 54_000;
        Debe(!Cierra(m6), "sonido con voz pero sin nada dicho desde el último cierre no abre nada que cerrar");

        // ── Y LA CONVERSACIÓN SE LA DEVUELVE ─────────────────────────────────
        // Una herramienta de autocontrol que el contrato sujeta: mientras no la suelte, está en curso de verdad
        // en el hilo que ejecuta. Sin la devolución cableada el turno no se cerraría nunca; sin el registro, se
        // cerraría a mitad.
        var c = GptLiveConReloj(reloj, "211");
        if (c == null) return;
        var (conv, llega, cierres, tc) = c.Value;
        using (conv)
        using (var entro = new ManualResetEventSlim(false))
        using (var suelta = new ManualResetEventSlim(false))
        {
            var autocontrol = tc.GetProperty("Autocontrol");
            var campo = tc.GetField("_turnosSinMarca", BindingFlags.NonPublic | BindingFlags.Instance);
            if (autocontrol == null || campo == null) { Pendiente("ConversacionEnVivo.Autocontrol y _turnosSinMarca", "211", "018"); return; }
            autocontrol.SetValue(conv, (Func<string, string>)(_ => { entro.Set(); suelta.Wait(TimeSpan.FromSeconds(10)); return "callado"; }));
            try
            {
                Volatile.Write(ref ahora, 400_000); llega(OyeDelUsuario("silénciate"));
                Volatile.Write(ref ahora, 400_300);
                llega(JsonSerializer.Serialize(new
                {
                    type = "response.event",
                    @event = new { type = "response.output_item.done", item = new { type = "function_call", call_id = "call_calla", name = "self_mute", arguments = "{}" } },
                }));
                bool seEjecuta = entro.Wait(TimeSpan.FromSeconds(5));
                Volatile.Write(ref ahora, 403_000); llega(TicSinHechos);
                int conLaLlamada = cierres();
                suelta.Set();
                var marcador = campo.GetValue(conv);
                bool yaDevuelta = false;
                for (int i = 0; i < 100 && !yaDevuelta; i++)
                {
                    yaDevuelta = marcador != null && (int)enCurso.GetValue(marcador)! == 0;
                    if (!yaDevuelta) Thread.Sleep(50);
                }
                Volatile.Write(ref ahora, 404_500); llega(TicSinHechos);
                int a1500 = cierres();
                Volatile.Write(ref ahora, 405_100); llega(TicSinHechos);
                int a2100 = cierres();
                Debe(seEjecuta, "en la conversación, la herramienta pedida llega a ejecutarse (sin esto lo demás no dice nada)");
                Debe(conLaLlamada == 0, $"y con la llamada ejecutándose y 3000 ms sin decir nada el turno no se cierra (cerró {conLaLlamada})");
                Debe(yaDevuelta, "al terminar de ejecutarla, la conversación se la devuelve al marcador: deja de estar en curso");
                Debe(a1500 == 0 && a2100 == 1,
                    $"y el turno se cierra con el silencio de la app contado desde la devolución: a 1500 ms no, a 2100 ms sí (cerró {a1500} y luego {a2100})");
            }
            finally { suelta.Set(); }
        }

        // ── Y LA CONVERSACIÓN LE PASA LA VOZ QUE SUENA ───────────────────────
        // Revisión contrato r2 (2026-09-13): la voz que suena solo se juzgaba sobre un marcador suelto. Con la
        // conversación saltándose los Hecho.Suena al llamar a Oye, CONTRATO INTACTO (medido), y la guarda quedaba
        // muerta sin que nada lo dijera. No es teórico: en una respuesta larga de GPT-Live la voz siguió llegando
        // 3089 y 2667 ms sin transcripción nueva entre dos frases (sonda del adelanto, contra el servidor, ese día),
        // y sin la guarda el turno se cerraba ahí, con Ü a media respuesta: «Ü dijo», Cerro y DijoElUsuario antes
        // de tiempo.
        var cv = GptLiveConReloj(reloj, "211");
        if (cv == null) return;
        var (convVoz, llegaVoz, cierresVoz, tcVoz) = cv.Value;
        using (convVoz)
        {
            // Sin tocar el altavoz: Reaccionar tira el audio mientras dura la ventana de una interrupción a la orden,
            // y el marcador lo oye igual, porque la conversación se lo da DESPUÉS de reaccionar.
            var silencioHasta = tcVoz.GetField("_silencioHastaMs", BindingFlags.NonPublic | BindingFlags.Instance);
            if (silencioHasta == null) { Pendiente("ConversacionEnVivo._silencioHastaMs (el altavoz callado en el contrato)", "211", "018"); return; }
            silencioHasta.SetValue(convVoz, long.MaxValue);
            string ConVoz() => JsonSerializer.Serialize(new { type = "session.output_audio.delta", delta = Convert.ToBase64String(voz) });

            Volatile.Write(ref ahora, 700_000); llegaVoz(DiceLaVozDeU("Estás en SAP Easy Access."));
            for (long ms = 700_100; ms <= 703_000; ms += 100) { Volatile.Write(ref ahora, ms); llegaVoz(ConVoz()); }
            int mientrasSuena = cierresVoz();
            Volatile.Write(ref ahora, 704_500); llegaVoz(TicSinHechos);
            int trasCallar1500 = cierresVoz();
            Volatile.Write(ref ahora, 705_100); llegaVoz(TicSinHechos);
            int trasCallar2100 = cierresVoz();
            Debe(mientrasSuena == 0,
                $"en la conversación, mientras llega la voz de Ü (pico 1152) no se cierra el turno, aunque su transcripción terminó hace 3000 ms (cerró {mientrasSuena})");
            Debe(trasCallar1500 == 0 && trasCallar2100 == 1,
                $"y el silencio de la app se cuenta desde lo último que sonó: a 1500 ms no cierra, a 2100 ms sí (cerró {trasCallar1500} y luego {trasCallar2100})");
        }
    }

    /// <remarks>
    /// UNA PAUSA REINICIABA EL TOPE DENTRO DE LA MISMA PETICIÓN (revisa:regresiones, 2026-09-12): «abre la
    /// configuración… [pausa] …y entra en Bluetooth» abría dos turnos del usuario, y el tope de la 204 volvía a
    /// cero a mitad. La regla nueva mira si Ü CONTESTÓ: lo que el usuario dice tras una pausa, sin que Ü haya
    /// hablado después de lo último suyo, es la misma petición. No es «desde el último cierre»: con GPT-Live lo
    /// que contesta Ü cae dentro del mismo turno sintético, porque el cierre solo llega cuando callan los dos
    /// (medido en la sonda de los turnos: «Listo, estás en Bluetooth» y el cierre, en el mismo turno, 3 de 3).
    /// </remarks>
    private static void UnaPausaSinRespuestaEsLaMismaPeticion()
    {
        var t = Cap004("U.WindowsClient.Voice.TurnosSinMarca");
        var oye = t?.GetMethod("Oye");
        var toca = t?.GetMethod("TocaCerrar");
        if (t == null || oye == null || toca == null) { Pendiente("Voice.TurnosSinMarca (Oye, TocaCerrar)", "212", "018"); return; }

        long ahora = 0;
        Func<long> reloj = () => Volatile.Read(ref ahora);
        object Nuevo() => Activator.CreateInstance(t,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.CreateInstance | BindingFlags.OptionalParamBinding,
            null, new object[] { reloj, 1500 }, null)!;
        bool Oye(object m, Voz.Realtime.Hecho h) => (bool)oye.Invoke(m, new object[] { h })!;
        bool Cierra(object m) => (bool)toca.Invoke(m, null)!;
        Voz.Realtime.Hecho Usuario(string s) => new Voz.Realtime.Hecho.DiceElUsuario(s);
        Voz.Realtime.Hecho DeU(string s) => new Voz.Realtime.Hecho.DiceU(s);

        var m = Nuevo();
        ahora = 0; bool abre = Oye(m, Usuario("abre la configuración"));
        ahora = 1_600; bool cerroLaPausa = Cierra(m);
        ahora = 1_800; bool sigue = Oye(m, Usuario(" y entra en Bluetooth"));
        Debe(abre && cerroLaPausa && !sigue,
            "tras una pausa que cerró el turno, lo que sigue diciendo el usuario sin que Ü haya contestado no abre otro: es la misma petición");
        ahora = 3_400; Cierra(m);
        ahora = 3_500; Oye(m, DeU("Listo, abierta."));
        ahora = 5_100; Cierra(m);
        ahora = 6_000;
        Debe(Oye(m, Usuario("ahora el sonido")), "cuando Ü ya le contestó, lo siguiente que dice el usuario es otra petición y abre turno");

        var m2 = Nuevo();
        ahora = 10_000; Oye(m2, Usuario("mira la pantalla"));
        ahora = 10_400; Oye(m2, DeU("Claro."));
        ahora = 12_000; Oye(m2, DeU(" Veo SAP."));
        ahora = 13_600; Cierra(m2);
        ahora = 14_000;
        Debe(Oye(m2, Usuario("abre NWP1")), "la respuesta de Ü cuenta aunque llegue antes del cierre, dentro del mismo turno");

        var m3 = Nuevo();
        ahora = 20_000; Oye(m3, Usuario("abre el"));
        ahora = 20_300; Oye(m3, DeU("Claro"));
        ahora = 20_700; bool encima = Oye(m3, Usuario(" bloc de notas"));
        ahora = 22_300; bool cerro3 = Cierra(m3);
        ahora = 23_000; bool porFavor = Oye(m3, Usuario(" por favor"));
        Debe(!encima && cerro3 && !porFavor,
            "si el usuario siguió hablando después de lo que dijo Ü, lo que dice tras el cierre sigue siendo su petición: Ü no le contestó a eso");

        var m4 = Nuevo();
        ahora = 30_000; Oye(m4, DeU("Hola, te escucho."));
        ahora = 31_600; Cierra(m4);
        ahora = 32_000;
        Debe(Oye(m4, Usuario("abre el bloc de notas")), "lo primero que dice el usuario abre turno aunque Ü hablara antes: el saludo no contesta a nada");

        // ── Y EN LA CONVERSACIÓN NO VUELVE A CERO EL TOPE ────────────────────
        var anotado = Cap004("U.WindowsClient.Diagnostics.LogBus")?.GetEvent("Anotado");
        if (anotado == null) { Pendiente("Diagnostics.LogBus.Anotado", "212", "018"); return; }
        var c = GptLiveConReloj(reloj, "212");
        if (c == null) return;
        var (conv, llega, cierres, _) = c.Value;
        int turnosNuevos = 0;
        Action<string, string> oyeLog = (tag, msg) => { if (tag == "voz-turno" && msg.StartsWith("turno nuevo (por voz)")) Interlocked.Increment(ref turnosNuevos); };
        anotado.AddEventHandler(null, oyeLog);
        try
        {
            using (conv)
            {
                ahora = 600_000; llega(OyeDelUsuario("abre la configuración"));
                ahora = 602_100; llega(TicSinHechos);
                int cerroPausa = cierres();
                ahora = 602_300; llega(OyeDelUsuario(" y entra en Bluetooth"));
                int trasPausa = turnosNuevos;
                ahora = 604_400; llega(TicSinHechos);
                ahora = 604_500; llega(DiceLaVozDeU("Listo."));
                ahora = 606_600; llega(TicSinHechos);
                ahora = 607_000; llega(OyeDelUsuario("ahora el sonido"));
                int trasRespuesta = turnosNuevos;
                Debe(cerroPausa == 1 && trasPausa == 1,
                    $"en la conversación, la pausa cierra el turno pero no parte la petición: una sola línea «turno nuevo (por voz)» y el tope no vuelve a cero (cierres {cerroPausa}, líneas {trasPausa})");
                Debe(trasRespuesta == 2,
                    $"y lo que dice después de que Ü le conteste sí abre turno y reinicia el tope (líneas {trasRespuesta})");
            }
        }
        finally { anotado.RemoveEventHandler(null, oyeLog); }
    }

    /// <remarks>
    /// «SESIÓN ABIERTA» SE ESCRIBÍA AL CONECTAR EL SOCKET, ANTES DE QUE EL SERVIDOR DIJERA NADA (nivel 4 del 2026-09-12). Con
    /// la cuenta sin crédito salió en el mismo segundo que «el servidor dice: You have no credits remaining», y el
    /// conductor del nivel 4 (scripts\nivel4-voz\conducir.ps1) la tomó por «voz abierta». Es el patrón nº2: una línea
    /// que afirma «abrió» cuando solo sabe «conectó». Desde la 49 (GPT-Live) y la 50 (GPT Realtime) de la voz, el
    /// traductor dice cuándo confirma el servidor; esto juzga que la conversación lo espera para decirlo.
    ///
    /// Se juzga sin socket: por EmpiezaUnaConexion, por donde pasan ArrancarAsync y ReconectarAsync después de conectar
    /// y mandar la apertura, y por Procesar, la puerta del socket, con la puerta de salida sustituida. Que ArrancarAsync
    /// no vuelva a escribir la línea por su cuenta necesita clave y socket, y no se juzga aquí (W220).
    /// </remarks>
    private static void LaVozDiceQueAbrioCuandoElServidorLoConfirma()
    {
        const BindingFlags Privado = BindingFlags.NonPublic | BindingFlags.Instance;
        var tc = Cap004("U.WindowsClient.Voice.ConversacionEnVivo");
        var procesar = tc?.GetMethod("Procesar", Privado);
        var conexion = tc?.GetMethod("EmpiezaUnaConexion", Privado);
        var salida = tc?.GetField("_puerta", Privado);
        var abierta = tc?.GetField("_puertaAbierta", Privado);
        var dice = tc?.GetEvent("Dice");
        var anotado = Cap004("U.WindowsClient.Diagnostics.LogBus")?.GetEvent("Anotado");
        var tLive = typeof(Voz.Realtime.IProtocolo).Assembly.GetType("Voz.Realtime.ProtocoloGptLive");
        if (tc == null || procesar == null || conexion == null || salida == null || abierta == null || dice == null
            || anotado == null || tLive == null)
        { Pendiente("ConversacionEnVivo (Procesar, EmpiezaUnaConexion, _puerta, _puertaAbierta, Dice) y Voz.Realtime.ProtocoloGptLive", "220", "018"); return; }
        var live = (Voz.Realtime.IProtocolo)Activator.CreateInstance(tLive,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.CreateInstance | BindingFlags.OptionalParamBinding,
            null, new[] { Type.Missing, Type.Missing }, null)!;

        // Tal como los mandó el servidor: la confirmación de cada uno, medida con sonda-apertura.ps1 el 2026-09-13 (las
        // instrucciones por defecto de Realtime, recortadas), y el error sin crédito del 2026-09-12, el de la 49 de la voz.
        const string SesionStarted = """{"event_id":"event_ENgqCrigFUMtyYlkgZPm6","type":"session.started","session":{"id":"live_u2_ENgqAxk3F96xLroZHUQ5A","expires_at":1789322132,"model":"gpt-live-1","instructions":"Eres una sonda. Responde en una frase.","audio":{"output":{"voice":"marin"},"format":{"type":"audio/pcm","rate":24000}},"delegation":{"type":"responses","responses":{"model":"gpt-5.6-luna","instructions":"Sonda.","tools":[],"tool_choice":"auto"}},"status":"active","input":[]}}""";
        const string SesionCreated = """{"type":"session.created","event_id":"event_ENgpu4Ic9UMb8h9QL7IeW","session":{"type":"realtime","object":"realtime.session","id":"sess_ENgpurpMSkQ6r492KYlaU","model":"gpt-realtime-2.1-mini","output_modalities":["audio"],"instructions":"Your knowledge cutoff is 2023-10. [...]","tools":[],"tool_choice":"auto","max_output_tokens":"inf","tracing":null,"truncation":"auto","prompt":null,"expires_at":1789318514,"audio":{"input":{"format":{"type":"audio/pcm","rate":24000},"transcription":null,"noise_reduction":null,"turn_detection":{"type":"server_vad","threshold":0.5,"prefix_padding_ms":300,"silence_duration_ms":500,"idle_timeout_ms":null,"create_response":true,"interrupt_response":true}},"output":{"format":{"type":"audio/pcm","rate":24000},"voice":"marin","speed":1.0}},"include":null}}""";
        const string SinCredito = """{"type":"error","event_id":"event_7f0763e4-314d-4930-9bfa-eb831d673918","error":{"type":"invalid_request_error","code":"credit_balance_exhausted","message":"You have no credits remaining. Add credits to continue using the API at https://platform.openai.com/settings/organization/billing/."}}""";

        var lineas = new List<string>();
        Action<string, string> oyeLog = (tag, msg) =>
        {
            if (tag == "voz-viva" && (msg.Contains("sesión abierta") || msg.Contains("socket conectado"))) lock (lineas) lineas.Add(msg);
        };
        string[] Lineas() { lock (lineas) { var l = lineas.ToArray(); lineas.Clear(); return l; } }
        static string Juntas(IEnumerable<string> l) => l.Any() ? string.Join(" | ", l) : "nada";

        // Una conversación sin socket: lo que manda sale a una lista, lo que dice a otra.
        (IDisposable Conv, List<string> Mandados, List<string> Dichas) Nueva(Voz.Realtime.IProtocolo p)
        {
            var conv = (IDisposable)Activator.CreateInstance(tc, new object?[] { new SurfaceMapTools(() => null), p })!;
            var mandados = new List<string>();
            var dichas = new List<string>();
            salida.SetValue(conv, (Func<string, CancellationToken, Task>)((json, _) => { lock (mandados) mandados.Add(json); return Task.CompletedTask; }));
            abierta.SetValue(conv, (Func<bool>)(() => true));
            dice.AddEventHandler(conv, (Action<string>)(s => { lock (dichas) dichas.Add(s); }));
            return (conv, mandados, dichas);
        }
        void Conecta(object conv) => conexion.Invoke(conv, new object[] { "Te escucho." });
        void Llega(object conv, string json) => procesar.Invoke(conv, new object[] { json, CancellationToken.None });

        anotado.AddEventHandler(null, oyeLog);
        try
        {
            var confirman = new (Voz.Realtime.IProtocolo P, string Nombre, string Confirmacion)[]
            {
                (live, "GPT-Live", SesionStarted),
                (new Voz.Realtime.ProtocoloOpenAI(), "GPT Realtime", SesionCreated),
            };
            foreach (var (p, nombre, confirmacion) in confirman)
            {
                string quien = $"«{p.Modelo}» ({p.Quien})";

                // ── EL SERVIDOR CONFIRMA ─────────────────────────────────────────
                var (conv, mandados, dichas) = Nueva(p);
                using (conv)
                {
                    Lineas();
                    Conecta(conv);
                    string[] alConectar = Lineas();
                    Debe(alConectar.Length == 1 && alConectar[0] == $"socket conectado, esperando confirmación de {quien}",
                        $"con {nombre}, al conectar el socket la voz deja UNA línea que describe el paso, «socket conectado, esperando confirmación de {quien}», y no dice todavía que la sesión abrió (dejó: {Juntas(alConectar)})");
                    Debe(dichas.Count == 0, $"y «Te escucho.» espera a la confirmación (dijo: {Juntas(dichas)})");

                    Llega(conv, confirmacion);
                    string[] alConfirmar = Lineas();
                    Debe(alConfirmar.Length == 1 && alConfirmar[0] == $"sesión abierta con {quien}: el servidor la confirmó",
                        $"con {nombre}, al llegar la confirmación del servidor sale UNA línea «sesión abierta con {quien}: el servidor la confirmó» (dejó: {Juntas(alConfirmar)})");
                    Debe(dichas.Count == 1 && dichas[0] == "Te escucho.", $"y entonces dice «Te escucho.», una vez (dijo: {Juntas(dichas)})");
                    Debe(mandados.Count == 0, $"y confirmar no manda nada al servidor (mandó: {Juntas(mandados)})");
                }

                // ── EL SERVIDOR CONTESTA UN ERROR EN VEZ DE CONFIRMAR ────────────
                var (conv2, _, dichas2) = Nueva(p);
                using (conv2)
                {
                    Lineas();
                    Conecta(conv2);
                    Lineas();
                    Llega(conv2, SinCredito);
                    string[] trasElError = Lineas();
                    Debe(trasElError.Length == 0 && dichas2.Count == 0,
                        $"con {nombre}, un error antes de confirmar no escribe «sesión abierta con» ni dice «Te escucho.» (dejó: {Juntas(trasElError)}; dijo: {Juntas(dichas2)})");
                }
            }

            // ── UN PROTOCOLO QUE NO CONFIRMA ─────────────────────────────────────
            var (conv3, _, dichas3) = Nueva(new ProtocoloDeMentira(pideRespuesta: true, marcaLosTurnos: true));
            using (conv3)
            {
                Lineas();
                Conecta(conv3);
                string[] sinConfirmar = Lineas();
                Debe(sinConfirmar.Length == 1 && sinConfirmar[0] == "sesión abierta con «ninguno» (de mentira), sin confirmación: este protocolo no la manda",
                    $"con un protocolo que no confirma, la línea de apertura sale al conectar y dice que nadie la confirmó (dejó: {Juntas(sinConfirmar)})");
                Debe(dichas3.Count == 1 && dichas3[0] == "Te escucho.",
                    $"y con él «Te escucho.» se dice al conectar, como siempre: no hay confirmación que esperar (dijo: {Juntas(dichas3)})");
            }
        }
        finally { anotado.RemoveEventHandler(null, oyeLog); }
    }

    /// <summary>
    /// Un protocolo de mentira para juzgar a la conversación sin servidor: dice si pide respuesta y si
    /// marca los turnos, y traduce un único mensaje («trozo») a lo que dice el usuario.
    /// </summary>
    private sealed class ProtocoloDeMentira : Voz.Realtime.IProtocolo
    {
        private readonly bool _pideRespuesta;

        public ProtocoloDeMentira(bool pideRespuesta, bool marcaLosTurnos)
        {
            _pideRespuesta = pideRespuesta;
            MarcaLosTurnos = marcaLosTurnos;
        }

        public string Quien => "de mentira";
        public string Modelo => "ninguno";
        public int RitmoDeEntrada => 24000;
        public int RitmoDeSalida => 24000;
        public bool Mira => false;
        public bool SabeVolver => false;
        public bool MarcaLosTurnos { get; }
        public Uri Direccion() => new("wss://localhost/nada");
        public IReadOnlyDictionary<string, string> Cabeceras(string clave) => new Dictionary<string, string>();
        public IEnumerable<string> Apertura(string instrucciones, IReadOnlyList<Voz.Realtime.Utensilio> utensilios, string pase)
            => Array.Empty<string>();
        public string Audio(byte[] pcm) => "";
        public string Fotograma(byte[] jpeg) => "";
        public string Texto(string texto) => JsonSerializer.Serialize(new { type = "texto", texto });
        public IEnumerable<string> Resultados(IReadOnlyList<(string Id, string Nombre, string Resultado)> hechas)
            => Array.Empty<string>();
        public string PedirRespuesta(string instrucciones = "") => _pideRespuesta ? JsonSerializer.Serialize(new { type = "turno" }) : "";
        public IReadOnlyList<Voz.Realtime.Hecho> Leer(JsonElement m)
            => m.TryGetProperty("type", out var t) && t.GetString() == "trozo"
                ? new Voz.Realtime.Hecho[] { new Voz.Realtime.Hecho.DiceElUsuario(m.GetProperty("delta").GetString() ?? "") }
                : Array.Empty<Voz.Realtime.Hecho>();
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
