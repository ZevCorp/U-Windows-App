using System.Linq;
using System.Reflection;
using System.Text.Json;
using Voz.Realtime;

namespace Omi.Pruebas;

/// <summary>
/// EL CONTRATO DE LA VOZ POR EL COLLAR: lo que promete la parte que convierte tramas del Omi en
/// muestras, escrito como pruebas que llaman al código real y corren sin Bluetooth ni ventana.
///
/// Las cinco salen de la spec 001 y están escritas ANTES que su código. Eso no es metodología: una
/// fuente de audio se da por buena a sí misma con facilidad. La primera corrida de la sonda del
/// 2026-08-13 reportó «0 tramas falladas» y señal real mientras se comía el 46 % del reloj — un test
/// escrito después habría contado tramas entregadas, que es justo la métrica que ese fallo no rompe.
///
/// Las capacidades se piden POR NOMBRE, con reflexión, para que este contrato compile aunque el
/// código todavía no exista. Ausente ⇒ <see cref="Pendiente"/>, que cuenta como incumplida: una
/// promesa sin código que dijera «no aplicable» se sumaría al verde y el contrato pasaría a
/// certificar el vacío.
/// </summary>
internal static class Contrato
{
    private static int _fallos;
    private static int _pendientes;

    /// <summary>El ensamblado que se juzga. <see cref="Voz"/> es sólo el ancla para alcanzarlo.</summary>
    private static readonly Assembly Ensamblado = typeof(Voz).Assembly;

    private static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("CONTRATO DE LA VOZ (el collar, sin Bluetooth)\n");

        Prueba("1. el códec se le pregunta al collar, y uno que no se sabe decodificar se rechaza diciendo cuál era", ElCodecSePregunta);
        Prueba("2. el silencio que el collar no transmite se repone: lo entregado dura lo que duró el reloj", ElSilencioSeRepone);
        Prueba("3. la carita no distingue de dónde viene la voz: el collar y el micrófono local entregan el mismo formato", MismoFormatoVengaDeDonde);
        Prueba("4. perder el collar a media sesión no deja muda a Ü: la voz vuelve al micrófono local", PerderElCollarNoEnmudece);
        Prueba("5. un paquete corto o vacío no produce muestras ni tumba la sesión", UnPaqueteMaloNoTumbaNada);
        Prueba("6. callarse no es desconectarse: mientras el collar siga conectado se le sigue esperando", CallarNoEsIrse);
        Prueba("7. un parpadeo de la conexión no es una pérdida: se le da tiempo a Windows a rehacerla", ElParpadeoNoEsPerdida);

        // EL PROTOCOLO CON EL SERVIDOR (Gemini→OpenAI, 2026-08-24). Se prueba SIN socket, sin
        // micrófono y sin clave —justo la promesa de diseño de IProtocolo— dándole mensajes tal
        // como los manda el servidor real (capturados de la sonda contra la API en vivo) y mirando
        // qué hechos saca.
        Prueba("8. un trozo de audio del servidor se traduce en un hecho que suena, con el PCM correcto", ElAudioSeTraduce);
        Prueba("9. una respuesta cancelada retira la llamada retirada, no la ejecuta ni la calla en silencio", LaCanceladaSeRetira);
        Prueba("10. pedir que conteste es un paso APARTE de mandar los resultados, y solo se pide una vez", ContestarNoVaEscondido);

        // LA COMPUERTA DE ECO (spec 002, 2026-08-30). El bucle que estas cuatro cortan: el micrófono
        // capta lo que suena por los altavoces, el semantic_vad del servidor cree que le hablan
        // encima, manda speech_started y Ü se calla a media frase — oyéndose a sí misma. La llave es
        // EL ESTADO DE NUESTRA COLA DE REPRODUCCIÓN, jamás el volumen: las defensas por volumen se
        // enterraron el 2026-08-16 («nunca puedo interrumpirlo») porque un número no distingue
        // «¿esto es el eco de Ü?» de «¿esto es quien me habla?».
        Prueba("11. mientras Ü suena, el micrófono viaja al servidor como silencio del mismo tamaño: ni una muestra de la sala", SonandoViajaSilencio);
        Prueba("12. al vaciarse la cola la compuerta no se abre de golpe: aguanta la gracia que tarda el eco en morir, y pasada la gracia el micrófono viaja intacto", LaGraciaSeAguanta);
        Prueba("13. lo tragado deja rastro: la compuerta cuenta los milisegundos que sustituyó y el total se puede leer", LoTragadoSeCuenta);
        Prueba("14. con el AEC del sistema puesto la compuerta no actúa, y sin él —o forzada— actúa siempre: nunca los dos, nunca ninguno", OCompuertaOAec);

        // EL BARGE-IN DE LA COMPUERTA (spec 002, fase 4 · 2026-08-31). La compuerta arregló que Ü
        // se oyera a sí misma y de paso la volvió ININTERRUMPIBLE: con el micrófono viajando como
        // silencio, el servidor no puede oír a quien le habla encima («no lo puedo interrumpir»).
        // La salida fina es el AEC del sistema (fase 3, aplazada); mientras la compuerta mande,
        // este detector le devuelve la interrupción: la LLAVE sigue siendo el estado de la cola
        // —el volumen jamás decide qué es eco—, y la energía solo decide UNA cosa acotada: que
        // hay voz sostenida MUY por encima del eco aprendido, y entonces se corta la cola y se
        // reabre la compuerta. Lo enterrado el 2026-08-16 (volumen para FILTRAR) sigue enterrado.
        Prueba("15. hablarle encima con voz sostenida la interrumpe: el detector dispara sobre el eco aprendido y la compuerta reabierta deja viajar ese mismo trozo intacto", VozSostenidaInterrumpe);
        Prueba("16. un golpe corto no la interrumpe: sin sostén no hay disparo, y el eco fuerte de Ü tampoco dispara — la línea base es suya", UnGolpeNoInterrumpe);
        Prueba("17. tras disparar, el detector no ametralla: no re-dispara hasta que Ü vuelva a sonar", ElDisparoNoSeAmetralla);

        // EL AEC POR SOFTWARE (spec 002, fase 3 reescrita · 2026-08-31). El AEC del sistema es
        // decorativo en el hardware medido (A/B: 0,0 dB), así que el eco se resta EN EL CLIENTE
        // con la ventaja que nadie más tiene: la referencia es NUESTRA PROPIA cola, tapeada en el
        // consumo del dispositivo. El cancelador es speexdsp (nativo); lo que se promete aquí es
        // la parte PURA que lo alimenta: el compás de la referencia. El canceladorreal se juzga
        // con el arnés de medición (atenuación en dB), no con este contrato.
        Prueba("18. la referencia baja de 24k a 16k sin perder el compás: por cada 3 muestras entran 2, byte a byte contables", LaReferenciaBajaDeRitmo);
        Prueba("19. sin referencia pendiente sale SILENCIO del tamaño del marco: el cancelador nunca espera", SinReferenciaSaleSilencio);
        Prueba("20. vaciar la referencia tira lo pendiente: lo que ya no va a sonar no puede restarse", VaciarTiraLoPendiente);
        Prueba("21. sin camino de eco declarado (auriculares) la compuerta se aparta, y forzarla gana igual: la garantía solo se enciende", SinCaminoDeEcoAbreLaCompuerta);
        Prueba("22. por defecto el micrófono viaja SIEMPRE (la experiencia OpenAI de fábrica); solo U_SIN_ECO=0 devuelve la compuerta", ElDefaultEsLaExperienciaOpenAI);

        // EL COLLAR COMO MICRÓFONO DE LA CONSULTA (spec 005, 2026-09-01). Tres fuentes —el
        // micrófono del PC, el collar por Bluetooth, y el collar a través del teléfono— y UN solo
        // sitio que decide cuál manda. La ruta del teléfono se midió el 2026-09-01 contra un
        // WebSocket propio: 100,7 s continuos, tramas de 640 bytes cada 20 ms, ~32.000 B/s, que es
        // PCM16 16 kHz en tiempo real. Los 640 bytes son la firma del collar (320 muestras, la
        // trama nativa del CV1), no del micrófono del teléfono.
        //
        // La que de verdad cierra el asunto es la 29, y el motivo tiene fechas: este repo ya pagó
        // TRES veces el mismo fallo —el enlace decía «conectado» y no llegaba una sola trama—.
        // 2026-08-25, con el usuario delante de una demo: 56 minutos en verde y cero audio. Un
        // indicador nuevo sin la 29 es la cuarta oportunidad de repetirlo.
        Prueba("23. la consulta se graba por UN solo captador, y ningún sitio nuevo abre un micrófono sin declararse", UnSoloSitioAbreCaptura);
        Prueba("24. la fuente activa se puede leer y decir con su nombre: micrófono del PC, collar por Bluetooth, o collar por teléfono", LaFuenteSeSabeYSeDice);
        Prueba("25. con el collar disponible manda el collar; al perderlo la voz vuelve al micrófono local, y el cambio deja el motivo", MandaElCollarYSeReleva);
        Prueba("26. el enlace de emparejamiento lleva el código del médico, y un código que no cuadra se rechaza NOMBRANDO que no cuadra", ElEnlaceLlevaElCodigo);
        Prueba("27. un código caducado no acepta audio, y lo dice con 410 y no con silencio", ElCodigoCaducadoLoDice);
        Prueba("28. la fuente por teléfono declara su latencia nominal, y quien la consume la puede leer antes de esperar nada", LaLatenciaSeDeclara);
        Prueba("29. el indicador no puede pintar «conectado» sin trama reciente: sin audio en N segundos, deja de decir que hay micrófono", SinTramaNoHayVerde);

        // LO QUE ELIGE EL MÉDICO MANDA (2026-09-01, encontrado probando el selector recién hecho).
        // Con el collar conectado no se podía volver al micrófono del PC ni pasar al teléfono: la
        // elección se pintaba y al segundo siguiente la prioridad automática la deshacía. La causa
        // está en LiveAudio.QuiereCollar, que devuelve true SIEMPRE que el collar esté conectado —
        // una cláusula que se añadió el 2026-08-13 para arreglar el fallo contrario («se pulsaba el
        // collar para hablarle al collar y el collar no escuchaba») y que al no tener contraparte
        // dejó la puerta cerrada por dentro.
        //
        // La automática sigue mandando mientras nadie elija: es lo que hace que el collar se use
        // solo al aparecer. Elegir es lo que la desactiva, y sólo lo deshace que la elegida
        // desaparezca.
        Prueba("30. lo que elige el médico manda sobre la prioridad automática, y elegir el micrófono del PC se respeta aunque el collar esté conectado", LoElegidoManda);

        // RECONECTAR ES RECONSTRUIR (2026-09-01, medido con el collar en la mano). El log de la
        // prueba del dueño: 149 reconexiones, 145 «ObjectDisposedException: Cannot access a disposed
        // object» al reenganchar, y CINCO líneas con tramas en toda la sesión. Windows tira los
        // objetos GATT al caer el enlace; reescribir el descriptor sobre la característica vieja no
        // puede funcionar ni una sola vez, y el bucle se repetía cada segundo y medio.
        //
        // El parche de reenganche del 2026-08-30 nació para el fallo correcto —volver a conectarse
        // no es volver a estar suscrito— y eligió el camino equivocado: remendar en vez de rehacer.
        // Es el aprendizaje nº6, que este repo ya pagó con las tres capas de geometría.
        Prueba("31. reconectar es reconstruir: tras una desconexión no se reutiliza NADA del enlace anterior", ReconectarEsReconstruir);

        // EL VERDE PRUEBA LA FUENTE, NO EL AUDIO (2026-09-01, visto por el dueño). El icono se puso
        // verde para el collar mientras el collar seguía en ROJO, o sea sin transmitir: llegaba
        // audio —del micrófono del portátil— y el indicador lo daba por bueno para la fuente que
        // tocaba pintar. Es la cuarta vez que este repo tropieza con la misma clase de mentira, y
        // esta vez estaba DENTRO de la pieza escrita para impedirla.
        //
        // «Llega audio» y «llega audio POR ESTA FUENTE» no son la misma frase, y un indicador que
        // no las distinga acaba certificando el respaldo como si fuera lo elegido.
        Prueba("32. el verde prueba que llega audio POR ESA FUENTE: anotar tramas de una no puede hacer que otra parezca viva", CadaFuenteResponde);

        // LA VOZ ES GPT-LIVE (spec 018, 2026-09-12). Del 33 al 39 quedan libres a propósito: son de
        // ramas abiertas de Jose, y los números no se reciclan. GPT-Live no es «otro modelo en el
        // mismo socket»: gpt-live-1 en /v1/realtime contesta «not supported in realtime mode». Tiene
        // su propia puerta (/v1/live/sessions), su propio saludo (session.start), y la voz NO acepta
        // herramientas — las lleva un modelo delegado. Todo lo que sigue se midió contra el servidor
        // con la clave de esta máquina, y los mensajes van copiados de lo que contestó, no de la doc.
        // La 41 carga con la lección más cara: el servidor no manda NINGUNA marca de turno, y un
        // traductor que se las inventara le daría a la conversación un reloj que no existe.
        Prueba("40. GPT-Live abre por su propio endpoint con session.start: modelo gpt-live-1, voz marin, PCM 24 kHz, y las herramientas de Ü van en la delegación con su modelo delegado, no en la voz", GptLiveAbrePorSuPropiaPuerta);
        Prueba("41. GPT-Live traduce lo que manda el servidor: audio, lo que dice Ü, lo que dice el usuario, una llamada delegada con sus argumentos y un error; y no inventa marcas de turno que el servidor no manda", GptLiveTraduceLoQueManda);
        Prueba("42. GPT-Live manda lo de Ü con sus eventos: micrófono, texto y foto como mensajes de usuario, resultados como function_call_output, pedir turno como response.create, dictar como commentary; y cambiar de modo a mitad de sesión es un session.update de la delegación, no otro session.start", GptLiveMandaConSusEventos);

        // Y GPT REALTIME, que se queda como respaldo (U_VOZ=realtime), deja de pedir la transcripción
        // a un modelo con fecha de apagado.
        Prueba("43. GPT Realtime pide la transcripción de lo que dice el usuario a gpt-transcribe, no a gpt-4o-mini-transcribe, que se apaga el 2027-02-26", LaTranscripcionNoVaAlQueSeApaga);

        // EL SILENCIO DE GPT-LIVE NO SUENA (2026-09-12, revisión de regresiones). El servidor manda
        // audio también cuando la voz calla, y ese silencio entraba a la cola del altavoz: todo lo que
        // decide con «Ü está sonando» —la compuerta de eco, el turno de contar recuerdos, la boca—
        // leía ruido. El umbral se eligió midiendo, y la medida dice que es CERO: ver la promesa.
        Prueba("44. GPT-Live no hace sonar su silencio: un delta de audio con todas las muestras a cero no es Hecho.Suena, y uno con voz —también la pausa de pico 1 entre dos frases— sí, con el PCM exacto", GptLiveNoHaceSonarSuSilencio);

        // LO QUE LA MIGRACIÓN DEJÓ EN EL DELEGADO Y NO EN LA VOZ (revisiones del 2026-09-12). Con GPT-Live
        // habla uno y actúa otro: las reglas de Ü van al delegado, pero quien suena es la voz, con su
        // persona corta. Y lo que el servidor cuenta de la sesión llega en segundos, no en fichas.
        Prueba("46. la voz de GPT-Live no anuncia lo que va a hacer: la persona con la que abre la sesión prohíbe el futuro y el relleno de espera, y manda hablar en pasado y del resultado, como la 161 se lo manda al delegado", LaVozNoAnuncia);
        Prueba("47. con GPT-Live, cambiar de modo también cambia a quien habla: detrás del session.update de la delegación va un session.instructions.append a la voz con las reglas del modo nuevo, y al volver al modo con el que abrió, con su persona de siempre y no con las instrucciones de operar", CambiarDeModoCambiaLaVoz);
        Prueba("48. GPT-Live traduce lo que dura la sesión: session.usage.updated es un Hecho.Duracion con los segundos que trae, que son el acumulado de la sesión y no un incremento; un uso sin segundos no inventa duración", GptLiveCuentaLaDuracion);

        // LO QUE EL SERVIDOR DE GPT-LIVE NO ACEPTA (revisión de fidelidad, 2026-09-12). Tres cosas que el
        // traductor mandaba o callaba y el servidor rechaza sin cerrar nada: la voz sigue hablando y el delegado
        // deja de hacer. Del 44 al 48 son de los arregladores que corren a la vez: los números no se pisan.
        Prueba("49. GPT-Live no manda lo que su servidor rechaza: no se declara capaz de mirar, porque una captura de pantalla no cabe y una segunda foto pequeña tampoco; un resultado de más de 32.768 bytes sale como un function_call_output de 32.768 bytes o menos, con su call_id, sin partir un carácter y diciendo cuánto se recortó; y declara que confirma la apertura: session.started es un Hecho.Abierta, y ni un error ni ningún otro mensaje lo es", GptLiveNoMandaLoQueSeRechaza);

        // LO QUE NO SE ARREGLA REINTENTANDO SE RECONOCE POR SU CÓDIGO, NO POR SU PROSA (2026-09-13, spec 018). Sin
        // crédito, GPT Realtime reconectó cuatro veces y GPT-Live no reintentó: la misma clase de error con dos
        // tratamientos. Decidirlo le toca a la conversación (223 y 224 del grafo); aquí, que el traductor no se
        // guarde el código, que es lo único estable: el message está en inglés y cambia de redacción.
        Prueba("53. los traductores de OpenAI dicen el código de un error: un error es un Hecho.Falla con su message y con su code tal como llega —credit_balance_exhausted e invalid_model por GPT-Live, invalid_api_key y model_not_found por GPT Realtime—, y sin code no se inventa uno", LosErroresDicenSuCodigo);

        Console.WriteLine();
        if (_pendientes > 0)
            Console.WriteLine($"({_pendientes} de ellas PENDIENTES: la capacidad todavía no existe. "
                + "Es el rojo esperado mientras se implementa, no una regresión.)");
        Console.WriteLine(_fallos == 0
            ? "VOZ ÍNTEGRA: el collar promete lo que dice prometer."
            : $"VOZ ROTA: {_fallos} promesa(s) incumplida(s). El cambio no puede entrar así.");
        return _fallos;
    }

    // ── Las promesas ─────────────────────────────────────────────────────────

    /// <remarks>
    /// EL FALLO QUE ESTO IMPIDE: suponer el códec. El CV1 declara 21 (Opus FS320, tramas de 20 ms) y
    /// el DevKit declara 20 (tramas de 10 ms); hay además dos variantes de PCM. Decodificar Opus lo
    /// que venía en PCM no da un error, da ruido — y un códec nuevo que nadie previó daría ruido
    /// igual, en silencio. Rechazar NOMBRANDO el número es lo que separa «no lo soporto» de «no sé
    /// qué pasa» (aprendizaje nº2).
    /// </remarks>
    private static void ElCodecSePregunta()
    {
        var soportado = Estatico("Codec", "Soportado");
        var motivo = Estatico("Codec", "Motivo");
        if (soportado == null || motivo == null) { Pendiente("Omi.Codec", "2"); return; }

        Debe(true.Equals(soportado.Invoke(null, new object[] { (byte)21 })),
            "el 21 del CV1 se acepta");
        Debe(false.Equals(soportado.Invoke(null, new object[] { (byte)7 })),
            "un códec que nadie previó NO se acepta");

        var texto = motivo.Invoke(null, new object[] { (byte)7 }) as string ?? "";
        Debe(texto.Contains("7"), "y al rechazarlo dice CUÁL era: el 7 aparece en el motivo");
    }

    /// <remarks>
    /// LA PROMESA QUE CIERRA EL ASUNTO, y la única cuyo síntoma no se parece a su causa.
    ///
    /// Medido el 2026-08-13: el collar DEJA DE EMITIR cuando no hay voz —hablando seguido da 43,3
    /// tramas/s y el 86,7 % del reloj; callado baja a 27,0/s y el 54 %—. Si esos huecos se
    /// concatenan, Gemini Live no oye ninguna pausa; y la pausa es exactamente cómo Live API decide
    /// que tu turno terminó. El resultado es que Ü escucha para siempre y no contesta nunca: el audio
    /// llega, decodifica y suena perfecto en un WAV.
    ///
    /// EL FIXTURE LLEVA NUMERACIÓN CONSECUTIVA A PROPÓSITO. También se midió que el contador de
    /// paquete NO avanza durante el silencio: es contiguo a través de un parón de 2,01 s. Contar
    /// paquetes es por eso la implementación que parece correcta y no lo es, y este fixture es lo
    /// único que la falla.
    /// </remarks>
    private static void ElSilencioSeRepone()
    {
        var tipo = Ensamblado.GetType("Omi.Reposicion");
        var metodo = tipo?.GetMethod("Muestras", BindingFlags.Public | BindingFlags.Instance);
        if (tipo == null || metodo == null) { Pendiente("Omi.Reposicion", "4"); return; }

        var r = Activator.CreateInstance(tipo);

        // Dos tramas de 320 muestras (20 ms), separadas DOS SEGUNDOS de reloj.
        int primera = (int)(metodo.Invoke(r, new object[] { 0L, 320 }) ?? 0);
        int segunda = (int)(metodo.Invoke(r, new object[] { 2000L, 320 }) ?? 0);
        int total = primera + segunda;

        Debe(primera == 320, "la primera trama no inventa silencio delante: son sus 320 muestras");

        // 2.020 ms de reloj a 16 kHz = 32.320 muestras. Se deja holgura porque la reposición puede
        // redondear a trama entera, pero NO tanta como para que 640 se cuele.
        Debe(total > 30000 && total < 34000,
            $"lo entregado dura lo que duró el reloj: ~32.000 muestras para 2 s (salieron {total})");
        Debe(total != 640,
            "y no son las dos tramas pegadas: concatenar daría 640 muestras para dos segundos");
    }

    /// <remarks>
    /// El punto de integración es <c>LiveAudio.Capturado</c>, que ya emite PCM16 a 16 kHz mono desde
    /// el micrófono local. Si el collar entregara otra cosa, GeminiLive tendría que enterarse de cuál
    /// de las dos fuentes está puesta — y ese es exactamente el acoplamiento que esta promesa impide.
    /// Los dos ritmos los fija Google y no son negociables: se ENVÍA a 16 kHz.
    /// </remarks>
    private static void MismoFormatoVengaDeDonde()
    {
        var tipo = Ensamblado.GetType("Omi.Fuente");
        if (tipo == null) { Pendiente("Omi.Fuente", "5"); return; }

        Debe(16000.Equals(Propiedad(tipo, "Hz")), "16.000 Hz, como el micrófono local");
        Debe(16.Equals(Propiedad(tipo, "Bits")), "16 bits por muestra");
        Debe(1.Equals(Propiedad(tipo, "Canales")), "mono");
    }

    /// <remarks>
    /// El collar se queda sin batería, te lo quitas, o sales del alcance. Lo que NO puede pasar es
    /// que Ü se quede muda esperando tramas que ya no vienen: el micrófono local sigue ahí. Y tiene
    /// que decir por qué relevó, porque «se cambió sola de micrófono» sin motivo es indistinguible de
    /// un fallo.
    /// </remarks>
    private static void PerderElCollarNoEnmudece()
    {
        var tipo = Ensamblado.GetType("Omi.Relevo");
        var metodo = tipo?.GetMethod("HayQueRelevar", BindingFlags.Public | BindingFlags.Instance);
        if (tipo == null || metodo == null) { Pendiente("Omi.Relevo", "6"); return; }

        var r = Activator.CreateInstance(tipo, new object[] { 30000 });

        // Cinco segundos desconectado ya no es un parpadeo: Windows no lo va a recuperar.
        Debe(true.Equals(metodo.Invoke(r, new object[] { 0L, 5000L })),
            "cinco segundos desconectado sí es una pérdida: se releva al micrófono local");

        var motivo = Propiedad(r!.GetType(), "Motivo", r) as string ?? "";
        Debe(motivo.Length > 0, "y queda dicho por qué se relevó, no sólo que se relevó");
    }

    /// <remarks>
    /// La sonda del 2026-08-13 sólo vio paquetes bien formados —1.555, cero saltos—, pero BLE puede
    /// entregar cualquier cosa: una notificación truncada al desconectar, o los 3 bytes de cabecera
    /// sin payload detrás. Una excepción aquí sube por el callback de BLE y se lleva la sesión de voz
    /// por delante, que es un fallo mucho peor que perder una trama de 20 ms.
    /// </remarks>
    private static void UnPaqueteMaloNoTumbaNada()
    {
        var payload = Estatico("Trama", "Payload");
        if (payload == null) { Pendiente("Omi.Trama", "3"); return; }

        Debe(payload.Invoke(null, new object[] { Array.Empty<byte>() }) == null,
            "un paquete vacío no da payload");
        Debe(payload.Invoke(null, new object[] { new byte[] { 1, 0 } }) == null,
            "dos bytes no llegan ni a cabecera: no dan payload");
        Debe(payload.Invoke(null, new object[] { new byte[] { 1, 0, 0 } }) == null,
            "la cabecera sola, sin nada detrás, no da payload");

        var bueno = payload.Invoke(null, new object[] { new byte[] { 1, 0, 0, 0xB8 } }) as byte[];
        Debe(bueno != null && bueno.Length == 1,
            "y un paquete con un byte de audio detrás de la cabecera sí lo da, sin la cabecera dentro");
    }

    /// <remarks>
    /// ESTA PROMESA NACE DE UN FALLO MEDIDO, no de una precaución. El 2026-08-13, con el collar
    /// funcionando, la voz se cayó sola a los pocos minutos:
    ///
    ///     [21:24:56] Ü dijo: De acuerdo, ya estamos en Descargas. ¿buscas algo en particular?
    ///     [21:25:01] omi: collar perdido: 4416 ms sin una sola trama (umbral 4000 ms)
    ///
    /// El usuario estaba ESCUCHANDO. El collar no transmite silencio —eso ya se sabía y por eso
    /// existe la promesa 2—, así que estarse callado se veía idéntico a haberse ido. El umbral se
    /// eligió midiendo pausas de alguien HABLANDO (2,01 s la mayor); escuchando, el silencio dura lo
    /// que dure la frase de Ü, que son diez o veinte segundos.
    ///
    /// Y el fondo es el aprendizaje nº2: el tiempo sin tramas NO PUEDE distinguir sus dos causas.
    /// Subir el umbral sólo hace el fallo más raro y más difícil de reproducir. La señal que sí
    /// distingue la da Bluetooth: el aparato está conectado, o no lo está. El tiempo se queda sólo
    /// como red por si la conexión se queda colgada sin avisar.
    /// </remarks>
    private static void CallarNoEsIrse()
    {
        var tipo = Ensamblado.GetType("Omi.Relevo");
        var metodo = tipo?.GetMethod("HayQueRelevar", BindingFlags.Public | BindingFlags.Instance);
        if (tipo == null || metodo == null) { Pendiente("Omi.Relevo", "8"); return; }

        var r = Activator.CreateInstance(tipo, new object[] { 30000 });

        Debe(false.Equals(metodo.Invoke(r, new object[] { 500L, 0L })),
            "medio segundo callado y conectado: no se releva");
        Debe(false.Equals(metodo.Invoke(r, new object[] { 10000L, 0L })),
            "DIEZ SEGUNDOS callado y conectado tampoco: es alguien escuchando, no un collar perdido");

        var motivo = Propiedad(r!.GetType(), "Motivo", r) as string ?? "";
        Debe(motivo.Length == 0, "y no se inventa un motivo de pérdida cuando no se ha perdido nada");
    }

    /// <remarks>
    /// TAMBIÉN NACE DE UNA MEDIDA, y de la misma noche. Con MaintainConnection puesto, el log del
    /// 2026-08-13 enseñó lo que de verdad pasa:
    ///
    ///     [22:32:48] Bluetooth dice que el collar se desconectó
    ///     [22:32:49] Bluetooth dice que el collar volvió a conectarse
    ///
    /// Un segundo. Windows tira el enlace y lo rehace él solo, porque eso es exactamente lo que
    /// <c>MaintainConnection</c> le pide. El enlace ya se cura; quien no se curaba era el relevo, que
    /// se iba al micrófono local en el primer sondeo y dejaba la sensación de «esto es inestable».
    ///
    /// Es el mismo patrón que la carrera del `Busy` que el repo lleva apuntada desde julio: una
    /// condición que parpadea no se juzga en el primer vistazo. Se le da tiempo, y si aguanta, ahí sí
    /// es una pérdida.
    /// </remarks>
    private static void ElParpadeoNoEsPerdida()
    {
        var tipo = Ensamblado.GetType("Omi.Relevo");
        var metodo = tipo?.GetMethod("HayQueRelevar", BindingFlags.Public | BindingFlags.Instance);
        if (tipo == null || metodo == null) { Pendiente("Omi.Relevo", "9"); return; }

        var r = Activator.CreateInstance(tipo, new object[] { 30000 });

        Debe(false.Equals(metodo.Invoke(r, new object[] { 0L, 1000L })),
            "un segundo desconectado NO releva: es el parpadeo que Windows rehace solo");

        var motivo = Propiedad(r!.GetType(), "Motivo", r) as string ?? "";
        Debe(motivo.Length == 0, "y no se anuncia una pérdida que no ha ocurrido");
    }

    // ── El protocolo con OpenAI (2026-08-24) ────────────────────────────────

    private static JsonElement Mensaje(string json) => JsonDocument.Parse(json).RootElement;

    /// <remarks>
    /// EL FALLO QUE ESTO IMPIDE: un delta vacío ("") produciendo un "hecho de sonido" de cero bytes.
    /// El servidor manda algún mensaje con el campo presente pero vacío entre trozos reales, y
    /// tratarlo como audio de verdad metería silencio inaudible en la cola sin que nadie lo pidiera.
    /// </remarks>
    private static void ElAudioSeTraduce()
    {
        IProtocolo p = new ProtocoloOpenAI();
        byte[] pcm = { 1, 2, 3, 4, 250, 251, 0, 255 };
        string b64 = Convert.ToBase64String(pcm);

        var hechos = p.Leer(Mensaje($$"""{"type":"response.output_audio.delta","delta":"{{b64}}"}"""));
        Debe(hechos.Count == 1 && hechos[0] is Hecho.Suena,
            "un trozo de audio del servidor se traduce en UN Hecho.Suena");
        Debe(hechos[0] is Hecho.Suena s && s.Pcm.SequenceEqual(pcm),
            "con el PCM decodificado EXACTO, no una aproximación ni una copia truncada");

        var vacio = p.Leer(Mensaje("""{"type":"response.output_audio.delta","delta":""}"""));
        Debe(vacio.Count == 0, "y un delta vacío no produce un hecho de sonido de la nada");
    }

    /// <remarks>
    /// LA MISMA AVERÍA QUE YA SE VIO CON GEMINI EL 2026-08-05, ahora del lado de OpenAI: si el
    /// usuario habla encima, lo que el modelo iba a pedir se CANCELA, y ejecutarlo o contestarlo
    /// igual es lo que hacía que el modelo lo volviera a pedir en bucle sin que la conversación
    /// avanzara nunca. Aquí el aviso no es un evento aparte —"toolCallCancellation" en Gemini— sino
    /// un <c>response.done</c> con <c>status:"cancelled"</c> y la llamada retirada dentro de su
    /// <c>output</c>: si no se mira ahí dentro, la cancelación se pierde en silencio.
    /// </remarks>
    private static void LaCanceladaSeRetira()
    {
        IProtocolo p = new ProtocoloOpenAI();

        var cancelada = p.Leer(Mensaje("""
            {"type":"response.done","response":{"status":"cancelled","output":[
                {"type":"function_call","call_id":"call_abc123"}
            ]}}
            """));
        Debe(cancelada.Any(h => h is Hecho.CierraElTurno), "una respuesta cancelada SIGUE cerrando el turno");
        Debe(cancelada.OfType<Hecho.Retira>().Any(r => r.Ids.SequenceEqual(new[] { "call_abc123" })),
            "y retira la llamada que traía dentro, con su id exacto");

        var completa = p.Leer(Mensaje("""
            {"type":"response.done","response":{"status":"completed","output":[
                {"type":"function_call","call_id":"call_no_deberia_retirarse"}
            ]}}
            """));
        Debe(!completa.Any(h => h is Hecho.Retira),
            "una respuesta COMPLETADA no retira nada, aunque su output tenga una function_call: "
            + "esa llamada ya se está atendiendo, no se retiró");

        var sinLlamadas = p.Leer(Mensaje("""
            {"type":"response.done","response":{"status":"cancelled","output":[
                {"type":"message"}
            ]}}
            """));
        Debe(!sinLlamadas.Any(h => h is Hecho.Retira),
            "y una cancelada sin ninguna function_call dentro no inventa una retirada vacía");
    }

    /// <remarks>
    /// SIN ESTO EL MODELO SE QUEDA CON EL RESULTADO EN LA MANO Y CALLADO — el síntoma exacto del
    /// que se venía huyendo con Gemini, con otra cara. `Resultados` y `PedirRespuesta` se separaron
    /// a propósito el 2026-08-24 (antes iban juntos) para que "devolver lo que pidió" y "pedirle que
    /// hable" fueran dos pasos que quien llama controla por separado; esta promesa es la que impide
    /// que alguien los vuelva a fusionar sin darse cuenta.
    /// </remarks>
    private static void ContestarNoVaEscondido()
    {
        IProtocolo p = new ProtocoloOpenAI();
        var hechas = new List<(string Id, string Nombre, string Resultado)> { ("call_1", "map_where_am_i", "estás en el escritorio") };

        var resultados = p.Resultados(hechas).ToList();
        Debe(resultados.Count == 1, "una llamada resuelta produce UN mensaje de resultado, no dos");
        Debe(!resultados.Any(m => m.Contains("response.create")),
            "y ESE mensaje no pide respuesta por su cuenta: eso es un paso aparte");

        string pide = p.PedirRespuesta();
        Debe(pide.Contains("response.create"), "PedirRespuesta sí la pide, explícitamente, cuando se llama");
    }

    // ── La compuerta de eco (spec 002, 2026-08-30) ──────────────────────────

    /// <summary>El ensamblado del protocolo, donde vive la compuerta. Se busca por nombre: la
    /// capacidad todavía no existe cuando estas promesas se escriben, y así el contrato compila.</summary>
    private static readonly Assembly Realtime = typeof(ProtocoloOpenAI).Assembly;

    /// <summary>100 ms de PCM16 mono a 24 kHz (4.800 bytes), con NINGUNA muestra nula: si una sola
    /// sobreviviera a la compuerta, la comprobación de «todo ceros» la caza.</summary>
    private static byte[] VozDeLaSala()
    {
        var voz = new byte[4800];
        for (int i = 0; i < voz.Length; i++) voz[i] = (byte)(i % 251 + 1);
        return voz;
    }

    private static object? Compuerta(int graciaMs, int ritmoHz)
    {
        var t = Realtime.GetType("Voz.Realtime.CompuertaDeEco");
        return t == null ? null : Activator.CreateInstance(t, graciaMs, ritmoHz);
    }

    private static byte[]? Filtrar(object c, byte[] trozo, bool sonando, long ahoraMs)
        => c.GetType().GetMethod("Filtrar")?.Invoke(c, new object[] { trozo, sonando, ahoraMs }) as byte[];

    // ── El detector de interrupción (promesas 15-17) ─────────────────────────

    private static object? Detector(int sostenMs, double factor, double pisoRms)
    {
        var t = Realtime.GetType("Voz.Realtime.DetectorDeInterrupcion");
        return t == null ? null : Activator.CreateInstance(t, sostenMs, factor, pisoRms);
    }

    private static bool? Oye(object d, double rms, bool sonando, long ahoraMs)
        => d.GetType().GetMethod("Oye")?.Invoke(d, new object[] { rms, sonando, ahoraMs }) as bool?;

    // ── La referencia del eco (promesas 18-20) ───────────────────────────────

    private static object? Referencia(int marcoMuestras)
    {
        var t = Realtime.GetType("Voz.Realtime.ReferenciaDelEco");
        return t == null ? null : Activator.CreateInstance(t, marcoMuestras);
    }

    private static void Empuja(object r, byte[] pcm24k)
        => r.GetType().GetMethod("Empuja")!.Invoke(r, new object[] { pcm24k });

    private static byte[]? SacaMarco(object r)
        => r.GetType().GetMethod("SacaMarco")!.Invoke(r, null) as byte[];

    /// <remarks>
    /// 24000 y 16000 comparten compás 3:2. Si la bajada pierde o inventa muestras, el cancelador
    /// compara el micrófono contra una referencia corrida en el tiempo y no resta nada — el mismo
    /// tipo de descuadre que audio_end_ms castigaba en la promesa 11.
    /// </remarks>
    private static void LaReferenciaBajaDeRitmo()
    {
        var r = Referencia(320);
        if (r == null) { Pendiente("Voz.Realtime.ReferenciaDelEco", "3"); return; }

        // 960 muestras a 24k (40 ms) = 1920 bytes → deben volverse 640 muestras a 16k = dos marcos
        var pcm = new byte[1920];
        for (int i = 0; i < pcm.Length; i++) pcm[i] = (byte)(i * 31);   // señal no trivial
        Empuja(r, pcm);

        var m1 = SacaMarco(r); var m2 = SacaMarco(r);
        Debe(m1 != null && m1.Length == 640, "el primer marco sale entero: 320 muestras, 640 bytes");
        Debe(m2 != null && m2.Length == 640, "y el segundo también: 3 entran, 2 salen, nada se pierde");
        Debe(m1 != null && m1.Any(b => b != 0), "y no es silencio: la señal viaja, no se inventa");
    }

    private static void SinReferenciaSaleSilencio()
    {
        var r = Referencia(320);
        if (r == null) { Pendiente("Voz.Realtime.ReferenciaDelEco", "3"); return; }

        var marco = SacaMarco(r);
        Debe(marco != null && marco.Length == 640,
            "sin nada empujado, el marco sale igual del tamaño exacto: el cancelador nunca espera");
        Debe(marco != null && marco.All(b => b == 0),
            "y es silencio puro: cuando Ü no suena, restar nada es restar cero");
    }

    private static void VaciarTiraLoPendiente()
    {
        var r = Referencia(320);
        if (r == null) { Pendiente("Voz.Realtime.ReferenciaDelEco", "3"); return; }

        Empuja(r, new byte[1920]);
        r.GetType().GetMethod("Vacia")!.Invoke(r, null);
        var marco = SacaMarco(r);
        Debe(marco != null && marco.All(b => b == 0),
            "tras vaciar (la cola se calló), lo pendiente no sale: lo que no va a sonar no se resta");
    }

    /// <remarks>
    /// El guion del caso real: Ü suena (eco de RMS ~800 en la línea base), el usuario le habla
    /// encima a RMS 6000 durante más del sostén. El disparo tiene que llegar, y la compuerta
    /// reabierta (Abrir) tiene que dejar pasar EL MISMO trozo intacto — sin esperar la gracia,
    /// porque el arranque de la frase del usuario es justo lo que el VAD del servidor necesita oír.
    /// </remarks>
    private static void VozSostenidaInterrumpe()
    {
        var d = Detector(240, 3.0, 1500);
        if (d == null) { Pendiente("Voz.Realtime.DetectorDeInterrupcion", "4"); return; }

        // la línea base del eco se aprende mientras Ü suena
        long t = 0;
        for (int i = 0; i < 10; i++) Debe(Oye(d, 800, sonando: true, t += 100) == false,
            "el eco solo, aunque suene un rato, jamás dispara");

        // voz sostenida encima del eco: dispara al cumplirse el sostén, no antes
        bool disparo = false;
        for (int i = 0; i < 5 && !disparo; i++) disparo = Oye(d, 6000, sonando: true, t += 100) == true;
        Debe(disparo, "voz sostenida MUY por encima del eco aprendido dispara la interrupción");

        // y la compuerta reabierta deja viajar el trozo YA, sin gracia
        var c = Compuerta(300, 24000);
        if (c == null) { Pendiente("Voz.Realtime.CompuertaDeEco", "1"); return; }
        var voz = VozDeLaSala();
        Filtrar(c, voz, sonando: true, ahoraMs: 5000);
        bool tieneAbrir = c.GetType().GetMethod("Abrir") != null;
        Debe(tieneAbrir, "la compuerta sabe reabrirse a la orden (Abrir)");
        if (!tieneAbrir) return;
        c.GetType().GetMethod("Abrir")!.Invoke(c, null);
        var sale = Filtrar(c, voz, sonando: false, ahoraMs: 5050);
        Debe(sale != null && sale.SequenceEqual(voz),
            "reabierta a la orden, el arranque de la frase viaja intacto: la gracia no se lo come");
    }

    private static void UnGolpeNoInterrumpe()
    {
        var d = Detector(240, 3.0, 1500);
        if (d == null) { Pendiente("Voz.Realtime.DetectorDeInterrupcion", "4"); return; }

        long t = 0;
        for (int i = 0; i < 10; i++) Oye(d, 800, sonando: true, t += 100);

        // un portazo: un solo trozo fortísimo, y de vuelta al eco
        Debe(Oye(d, 20000, sonando: true, t += 100) == false, "un golpe de un solo trozo no dispara");
        Debe(Oye(d, 800, sonando: true, t += 100) == false, "y al volver el eco no queda nada armado");

        // una ráfaga de DOS trozos (ventana de ~100 ms, menos que el sostén) tampoco: aquí es
        // donde el sostén muerde de verdad — sin esta pareja, quitarle el sostén al detector
        // pasaba el contrato entero (medido con sabotaje, 2026-08-31).
        Debe(Oye(d, 20000, sonando: true, t += 100) == false, "ráfaga: el primer trozo no dispara");
        Debe(Oye(d, 20000, sonando: true, t += 100) == false, "ráfaga: el segundo, aún bajo el sostén, tampoco");
        Debe(Oye(d, 800, sonando: true, t += 100) == false, "y el eco de vuelta desarma la ráfaga");

        // Ü arranca una frase nueva MÁS FUERTE tras un silencio: sus primeros trozos son SU eco
        // —la siembra— y ponen la línea base ahí, así que su propio volumen no la interrumpe.
        for (int i = 0; i < 3; i++) Oye(d, 400, sonando: false, t += 100);
        for (int i = 0; i < 20; i++)
            Debe(Oye(d, 2600, sonando: true, t += 100) != true,
                "la frase nueva más fuerte siembra la base con su propio eco: no se interrumpe sola");
    }

    private static void ElDisparoNoSeAmetralla()
    {
        var d = Detector(240, 3.0, 1500);
        if (d == null) { Pendiente("Voz.Realtime.DetectorDeInterrupcion", "4"); return; }

        long t = 0;
        for (int i = 0; i < 10; i++) Oye(d, 800, sonando: true, t += 100);
        bool disparo = false;
        for (int i = 0; i < 5 && !disparo; i++) disparo = Oye(d, 6000, sonando: true, t += 100) == true;
        Debe(disparo, "el primer disparo llega");

        // la voz sigue y la cola YA se cortó (sonando: false): ni un segundo disparo
        for (int i = 0; i < 10; i++)
            Debe(Oye(d, 6000, sonando: false, t += 100) != true,
                "cortada la cola, seguir hablando no re-dispara: interrumpir una vez basta");

        // Ü vuelve a sonar → el detector vuelve a estar en guardia
        for (int i = 0; i < 10; i++) Oye(d, 800, sonando: true, t += 100);
        disparo = false;
        for (int i = 0; i < 5 && !disparo; i++) disparo = Oye(d, 6000, sonando: true, t += 100) == true;
        Debe(disparo, "con Ü sonando otra vez, la guardia vuelve y el disparo también");
    }

    /// <remarks>
    /// LA QUE CIERRA EL ASUNTO: mientras no exista, el eco sigue llegando al VAD del servidor y Ü
    /// sigue callándose sola. Silencio DEL MISMO TAMAÑO y no ausencia, a propósito: no mandar nada
    /// rompería el compás del buffer del servidor (audio_end_ms dejaría de cuadrar con el reloj).
    /// </remarks>
    private static void SonandoViajaSilencio()
    {
        var c = Compuerta(300, 24000);
        if (c == null) { Pendiente("Voz.Realtime.CompuertaDeEco", "1"); return; }

        var voz = VozDeLaSala();
        var sale = Filtrar(c, voz, sonando: true, ahoraMs: 0);
        Debe(sale != null && sale.Length == voz.Length,
            "el trozo sale del mismo tamaño: el compás del buffer no se pierde");
        Debe(sale != null && sale.All(b => b == 0),
            "y todo ceros: ni una muestra de la sala viaja al servidor");
    }

    /// <remarks>
    /// La gracia existe porque «la cola se vació» no es «se dejó de oír»: el altavoz lleva 120 ms de
    /// latencia declarada y la sala añade el resto. Y una compuerta que naciera cerrada dejaría muda
    /// la sesión hasta que Ü hablara por primera vez — por eso la primera comprobación es que recién
    /// nacida deja pasar.
    /// </remarks>
    private static void LaGraciaSeAguanta()
    {
        var c = Compuerta(300, 24000);
        if (c == null) { Pendiente("Voz.Realtime.CompuertaDeEco", "1"); return; }

        var voz = VozDeLaSala();
        var recienNacida = Filtrar(c, voz, sonando: false, ahoraMs: 0);
        Debe(recienNacida != null && recienNacida.SequenceEqual(voz),
            "antes de que Ü haya sonado nunca, el micrófono pasa intacto: la compuerta nace abierta");

        Filtrar(c, voz, sonando: true, ahoraMs: 1000);
        var enGracia = Filtrar(c, voz, sonando: false, ahoraMs: 1100);
        Debe(enGracia != null && enGracia.All(b => b == 0),
            "recién vaciada la cola sigue tragando: el eco tarda la gracia en morir");

        var pasada = Filtrar(c, voz, sonando: false, ahoraMs: 1400);
        Debe(pasada != null && pasada.SequenceEqual(voz),
            "pasada la gracia el micrófono viaja intacto, byte a byte, no una copia recortada");
    }

    /// <remarks>
    /// Patrón nº10: un paso no ejecutado deja rastro. Sin este contador, la interrupción que la
    /// compuerta evita y el micrófono que la compuerta calla serían igual de invisibles que el
    /// speech_started que hoy no deja ni una línea de log. Y la cuenta es de lo TRAGADO, no de lo
    /// enviado — contar lo enviado es justo el denominador que encoge (el 29/30 del salto-adelante).
    /// </remarks>
    private static void LoTragadoSeCuenta()
    {
        var c = Compuerta(300, 24000);
        if (c == null) { Pendiente("Voz.Realtime.CompuertaDeEco", "1"); return; }

        var voz = VozDeLaSala(); // 100 ms exactos a 24 kHz
        Filtrar(c, voz, sonando: true, ahoraMs: 0);
        Filtrar(c, voz, sonando: true, ahoraMs: 100);
        Filtrar(c, voz, sonando: true, ahoraMs: 200);
        var tragados = Propiedad(c.GetType(), "MsTragados", c);
        Debe(300L.Equals(tragados),
            $"tres trozos de 100 ms tragados son 300 ms contados (salieron {tragados})");

        Filtrar(c, voz, sonando: false, ahoraMs: 5000);
        var despues = Propiedad(c.GetType(), "MsTragados", c);
        Debe(300L.Equals(despues),
            "lo que pasa intacto no engorda la cuenta: se cuenta lo tragado, no lo que fluye");
    }

    /// <remarks>
    /// La decisión de modo es lo ÚNICO determinístico del AEC físico, y por eso es lo único que se
    /// promete: los dos encendidos a la vez harían el AEC inútil (la compuerta calla todo igual), y
    /// los dos apagados son exactamente el bug del que viene la spec 002.
    /// </remarks>
    /// <remarks>
    /// La decisión del dueño (2026-08-31): interrumpir con la voz como en la documentación de
    /// OpenAI, sin capas en medio — y con altavoces, audífonos. El default tiene que decirlo el
    /// CÓDIGO y no una variable que alguien recuerde poner: sin declarar nada, el micrófono viaja.
    /// </remarks>
    private static void ElDefaultEsLaExperienciaOpenAI()
    {
        var t = Realtime.GetType("Voz.Realtime.ModoDeCaptura");
        var m = t?.GetMethod("SinEcoDeclarado");
        if (t == null || m == null) { Pendiente("ModoDeCaptura.SinEcoDeclarado", "3"); return; }

        bool SinEco(string? valor) => (bool)m.Invoke(null, new object?[] { valor })!;

        Debe(SinEco(null), "sin declarar nada, el micrófono viaja: el default es el de la doc de OpenAI");
        Debe(SinEco(""), "la variable vacía tampoco cambia el default");
        Debe(SinEco("1"), "declararlo explícito también vale");
        Debe(!SinEco("0"), "y U_SIN_ECO=0 devuelve la compuerta a quien use parlantes");
        Debe(!SinEco("false"), "en cualquiera de sus grafías");
    }

    private static void SinCaminoDeEcoAbreLaCompuerta()
    {
        var t = Realtime.GetType("Voz.Realtime.ModoDeCaptura");
        var m = t?.GetMethod("CompuertaActiva");
        if (t == null || m == null || m.GetParameters().Length < 3)
        { Pendiente("ModoDeCaptura.CompuertaActiva(sinCaminoDeEco)", "3"); return; }

        bool Activa(bool aec, bool forzada, bool sinEco) =>
            (bool)m.Invoke(null, new object[] { aec, forzada, sinEco })!;

        Debe(!Activa(false, false, true),
            "con auriculares declarados la compuerta se aparta: no hay eco que tragar y el barge-in vuelve");
        Debe(Activa(false, false, false),
            "sin declarar nada, la compuerta sigue mandando como siempre");
        Debe(Activa(false, true, true),
            "y forzarla GANA incluso con auriculares: forzar solo enciende la garantía, jamás la apaga");
    }

    private static void OCompuertaOAec()
    {
        var m = Realtime.GetType("Voz.Realtime.ModoDeCaptura")
            ?.GetMethod("CompuertaActiva", BindingFlags.Public | BindingFlags.Static);
        if (m == null) { Pendiente("Voz.Realtime.ModoDeCaptura", "2"); return; }

        Debe(false.Equals(m.Invoke(null, new object[] { true, false, false })),
            "con el AEC del sistema puesto la compuerta no actúa: el barge-in es del AEC");
        Debe(true.Equals(m.Invoke(null, new object[] { false, false, false })),
            "sin AEC la compuerta actúa: es la garantía determinística");
        Debe(true.Equals(m.Invoke(null, new object[] { true, true, false })),
            "y forzada actúa aunque haya AEC: la perilla de esta máquina manda");
        Debe(true.Equals(m.Invoke(null, new object[] { false, true, false })),
            "forzada sin AEC también: forzar nunca puede APAGAR la garantía");
    }

    // ── spec 005: el collar es el micrófono de la consulta ────────────────────────────────────

    /// <remarks>
    /// «Un solo captador» se cuenta MIRANDO EL REPO, no declarándolo: es un hecho sobre los archivos
    /// y por eso lo mide el contrato leyéndolos. Contarlo desde el ensamblado puro sería imposible
    /// —no ve windows-client— y afirmarlo sin medir sería justo el vicio que este contrato existe
    /// para cortar.
    ///
    /// LA RUTA DE LA CONSULTA TIENE UNO: LiveAudio. Es lo que hace que elegir el micrófono en la
    /// ventana de consulta signifique algo.
    ///
    /// Y HAY DOS SITIOS MÁS QUE ABREN MICRÓFONO, declarados aquí con su motivo (2026-09-02):
    ///
    ///   · ScreenRecorder — graba la SALA en un vídeo de enseñanza. No es «por dónde oye Ü al
    ///     médico», es otra cosa: forzarlo por el selector sería un error, no un arreglo.
    ///   · VoiceIO — el dictado de una frase de la carita cuando NO hay conversación viva. Es un
    ///     segundo captador de verdad y un hueco real: con el collar puesto, ese camino sigue
    ///     oyendo por el portátil. Queda anotado como deuda con nombre en la spec 005; no se tapa
    ///     aquí porque un refactor a System.Speech con stream no cabe en el hueco en que se
    ///     descubrió, y taparlo en falso sería peor que dejarlo dicho.
    ///
    /// Lo que la promesa protege desde hoy es un TRINQUETE: si aparece un cuarto sitio que abre un
    /// micrófono, esto se pone rojo y obliga a la conversación. Es lo que impide que la cuenta
    /// vuelva a crecer sin que nadie se entere, que es como llegó a tres.
    /// </remarks>
    private static void UnSoloSitioAbreCaptura()
    {
        // Las tres formas de abrir un micrófono que este repo usa. Si alguien añade una cuarta API,
        // esta lista se queda corta — y por eso el criterio de terminado de cualquier fuente nueva
        // incluye añadirla aquí.
        string[] aperturas = { "new WaveInEvent", "SetInputToDefaultAudioDevice", "IsInputDeviceEnabled" };

        var raiz = RaizDelRepo();
        if (raiz == null)
        {
            // NO PUDE EJECUTARLA no es LO INCUMPLIÓ (aprendizaje nº17): un juez que no puede correr
            // tiene que decir eso, y no «culpable».
            Console.WriteLine("   ⚠ NO SE PUDO MIRAR: no encontré la raíz del repo desde "
                + AppContext.BaseDirectory + ". La promesa 23 no se juzgó.");
            _fallos++;
            return;
        }

        var conCaptura = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var archivo in Directory.EnumerateFiles(
                     Path.Combine(raiz, "windows-client", "src"), "*.cs", SearchOption.AllDirectories))
        {
            string texto = File.ReadAllText(archivo);
            foreach (var a in aperturas)
                if (texto.Contains(a, StringComparison.Ordinal)) { conCaptura.Add(Path.GetFileName(archivo)); break; }
        }

        // Los dos declarados, con su motivo escrito arriba. Cualquier otro es un sitio nuevo.
        var declarados = new SortedSet<string>(new[] { "ScreenRecorder.cs", "VoiceIO.cs" }, StringComparer.OrdinalIgnoreCase);
        var delaVoz = new SortedSet<string>(conCaptura.Where(f => !declarados.Contains(f)), StringComparer.OrdinalIgnoreCase);

        Debe(delaVoz.Count == 1 && delaVoz.Contains("LiveAudio.cs"),
            "fuera de los dos declarados, un solo archivo abre micrófono y es LiveAudio "
            + $"(encontrados: {string.Join(", ", delaVoz)})");

        Debe(conCaptura.Count == 3,
            $"y en total siguen siendo tres sitios, ni uno más (encontrados: {string.Join(", ", conCaptura)})");
    }

    /// <summary>
    /// La raíz del repo, subiendo desde ESTE archivo fuente hasta encontrar «windows-client».
    ///
    /// Se parte del <c>CallerFilePath</c> y no del binario a propósito: el arnés copia el ejecutable
    /// a una carpeta temporal y desde allí no hay ningún repo encima — medido el 2026-09-02, la
    /// primera versión de esta promesa no pudo mirar nada y lo dijo. El compilador deja escrita la
    /// ruta del fuente, que sí apunta al sitio correcto.
    /// </summary>
    private static string? RaizDelRepo([System.Runtime.CompilerServices.CallerFilePath] string archivo = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(archivo) ?? AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "windows-client", "src"))) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    /// <remarks>
    /// El 2026-08-14 la carita pintaba gris con el audio entrando por el collar: el log probaba
    /// que la voz SÍ entraba y era el dibujo el que no se enteraba. Un selector con tres opciones
    /// multiplica por tres esa clase de mentira, así que la fuente activa tiene que ser un dato
    /// que se pregunta, no un estado que cada pantalla deduce por su cuenta.
    /// </remarks>
    private static void LaFuenteSeSabeYSeDice()
    {
        var tipo = Ensamblado.GetType("Omi.Selector");
        if (tipo == null) { Pendiente("Omi.Selector", "1"); return; }

        var m = tipo.GetMethod("Nombre", BindingFlags.Public | BindingFlags.Static);
        if (m == null) { Pendiente("Omi.Selector.Nombre", "1"); return; }

        // Las tres fuentes se nombran, y con nombres distintos: un selector que llame igual a dos
        // no distingue nada.
        var local = m.Invoke(null, new object[] { 0 }) as string ?? "";
        var ble = m.Invoke(null, new object[] { 1 }) as string ?? "";
        var tele = m.Invoke(null, new object[] { 2 }) as string ?? "";

        Debe(local.Length > 0 && ble.Length > 0 && tele.Length > 0,
            "las tres fuentes tienen nombre: micrófono del PC, collar por Bluetooth, collar por teléfono");
        Debe(local != ble && ble != tele && local != tele,
            "y los tres nombres son distintos entre sí");
    }

    /// <remarks>
    /// Es la promesa 4 llevada a tres fuentes. La 4 ya exige que perder el collar no deje muda a Ü;
    /// aquí se añade lo que el selector introduce: que la PRIORIDAD sea explícita (con collar, manda
    /// el collar) y que el cambio diga POR QUÉ. El 2026-08-13 el collar se «perdió» a los 4.416 ms
    /// porque el umbral medía silencio y no ausencia, y el log no distinguía una cosa de la otra.
    /// </remarks>
    private static void MandaElCollarYSeReleva()
    {
        var tipo = Ensamblado.GetType("Omi.Selector");
        var metodo = tipo?.GetMethod("Decidir", BindingFlags.Public | BindingFlags.Instance);
        if (tipo == null || metodo == null) { Pendiente("Omi.Selector.Decidir", "1"); return; }

        var s = Activator.CreateInstance(tipo);

        // La decisión viaja como enum; aquí se compara por su número para no tener que referenciar
        // el tipo. Es plomería de reflexión, no una rebaja de lo prometido.
        int Decide(bool ble, bool tel) => Convert.ToInt32(metodo.Invoke(s, new object[] { ble, tel, 0L }));

        // Con el collar vivo manda el collar, aunque el micrófono local esté disponible.
        Debe(Decide(true, false) == 1, "con el collar por Bluetooth vivo, manda el collar");

        // Sin collar y con teléfono, manda el teléfono; sin ninguno de los dos, el micrófono local.
        Debe(Decide(false, true) == 2, "sin collar por Bluetooth pero con teléfono, manda el teléfono");
        Debe(Decide(false, false) == 0, "sin collar y sin teléfono, la voz vuelve al micrófono del PC");

        var motivo = Propiedad(s!.GetType(), "Motivo", s) as string ?? "";
        Debe(motivo.Length > 0, "y el último cambio dice por qué cambió, no sólo que cambió");
    }

    /// <remarks>
    /// El enlace se pega a mano en el teléfono de cada médico, así que va a viajar por WhatsApp,
    /// se va a copiar mal y se va a reutilizar entre dos médicos del mismo servicio. Un rechazo
    /// que diga «error» manda la investigación al sitio equivocado — es el aprendizaje nº2, que
    /// este repo ya incumplió al escribirlo. Tiene que distinguir SUS causas: código ausente,
    /// código que no cuadra, código de otro.
    /// </remarks>
    private static void ElEnlaceLlevaElCodigo()
    {
        var tipo = Ensamblado.GetType("Omi.Emparejamiento");
        if (tipo == null) { Pendiente("Omi.Emparejamiento", "3"); return; }

        var armar = tipo.GetMethod("Enlace", BindingFlags.Public | BindingFlags.Static);
        var juzgar = tipo.GetMethod("Motivo", BindingFlags.Public | BindingFlags.Static);
        if (armar == null || juzgar == null) { Pendiente("Omi.Emparejamiento.Enlace/Motivo", "3"); return; }

        var enlace = armar.Invoke(null, new object[] { "https://ejemplo", "ABCD1234" }) as string ?? "";
        Debe(enlace.Contains("ABCD1234"), "el enlace lleva el código del médico dentro");

        var bueno = juzgar.Invoke(null, new object[] { "ABCD1234", "ABCD1234" }) as string ?? "no vacío";
        Debe(bueno.Length == 0, "un código que cuadra no da motivo de rechazo");

        var ajeno = juzgar.Invoke(null, new object[] { "ABCD1234", "OTRO5678" }) as string ?? "";
        var vacio = juzgar.Invoke(null, new object[] { "ABCD1234", "" }) as string ?? "";
        Debe(ajeno.Length > 0 && vacio.Length > 0, "un código ajeno y un código ausente se rechazan los dos");
        Debe(ajeno != vacio,
            "y se rechazan con motivos DISTINTOS: «no cuadra» no es lo mismo que «no venía ninguno»");
    }

    /// <remarks>
    /// Medido el 2026-09-01 y es la razón de esta promesa: Omi apaga el envío del usuario tras
    /// CIEN respuestas seguidas que no sean 2xx, y lo hace en silencio. Un código caducado que
    /// conteste 200 deja a Omi mandando audio a un destino muerto para siempre; uno que conteste
    /// 410 gasta ese presupuesto a propósito y el envío se apaga solo, que es lo correcto.
    /// </remarks>
    private static void ElCodigoCaducadoLoDice()
    {
        var tipo = Ensamblado.GetType("Omi.Emparejamiento");
        var metodo = tipo?.GetMethod("Estado", BindingFlags.Public | BindingFlags.Static);
        if (tipo == null || metodo == null) { Pendiente("Omi.Emparejamiento.Estado", "3"); return; }

        // Emitido en t=0 con 8 horas de vida: a las 7 vale, a las 9 ya no.
        Debe(200.Equals(metodo.Invoke(null, new object[] { 0L, 7L * 3600_000L })),
            "dentro de su vida, el código acepta audio");
        Debe(410.Equals(metodo.Invoke(null, new object[] { 0L, 9L * 3600_000L })),
            "pasada su vida contesta 410, que es lo que hace que Omi deje de mandar");
    }

    /// <remarks>
    /// Medido el 2026-09-01: por el webhook de Omi el audio llega en ráfagas de 4 s; por el
    /// WebSocket directo, en tramas de 20 ms. Son dos productos distintos —uno sirve para dictar,
    /// el otro para conversar— y quien consume la voz no puede tener que adivinar cuál le tocó.
    /// </remarks>
    private static void LaLatenciaSeDeclara()
    {
        var tipo = Ensamblado.GetType("Omi.Selector");
        var metodo = tipo?.GetMethod("LatenciaNominalMs", BindingFlags.Public | BindingFlags.Static);
        if (tipo == null || metodo == null) { Pendiente("Omi.Selector.LatenciaNominalMs", "1"); return; }

        var local = (int)(metodo.Invoke(null, new object[] { 0 }) ?? -1);
        var tele = (int)(metodo.Invoke(null, new object[] { 2 }) ?? -1);

        Debe(local >= 0 && tele >= 0, "las fuentes declaran su latencia nominal en milisegundos");
        Debe(tele > local, "y la del teléfono es mayor que la del micrófono del PC: el camino es más largo");
    }

    /// <remarks>
    /// LA QUE CIERRA EL ASUNTO, y su fecha: 2026-08-25, con el usuario delante de una demo. El
    /// panel decía «conectado», la carita pintaba el punto azul, y pasaron 3.358.464 ms —56
    /// minutos— sin una sola trama. WinRT tira las suscripciones GATT al caer el enlace y nadie
    /// resuscribía; el estado del socket decía la verdad y aun así el indicador mentía.
    ///
    /// Por eso el verde NO puede depender del estado de la conexión: depende de que haya llegado
    /// audio hace poco. Un indicador que mire el socket vuelve a pintar el mismo verde falso.
    /// </remarks>
    private static void SinTramaNoHayVerde()
    {
        var tipo = Ensamblado.GetType("Omi.Vigia");
        var metodo = tipo?.GetMethod("HayMicrofono", BindingFlags.Public | BindingFlags.Instance);
        if (tipo == null || metodo == null) { Pendiente("Omi.Vigia", "4"); return; }

        // Tolerancia de 3 s. El enlace dice «conectado» en los tres casos: es lo único que el bug
        // del 2026-08-25 tenía a favor.
        var v = Activator.CreateInstance(tipo, new object[] { 3000 });

        Debe(true.Equals(metodo.Invoke(v, new object[] { true, 1000L, 2000L })),
            "con una trama de hace un segundo, sí hay micrófono");
        Debe(false.Equals(metodo.Invoke(v, new object[] { true, 1000L, 9000L })),
            "con la última trama hace ocho segundos, NO hay micrófono aunque el enlace diga que sí");
        Debe(false.Equals(metodo.Invoke(v, new object[] { true, 0L, 1000L })),
            "y sin ninguna trama todavía, tampoco: conectarse no es entregar");

        // Y EL REVÉS, que es la otra mitad de la misma regla y faltaba (2026-09-01): si está
        // llegando audio, HAY micrófono, diga lo que diga la bandera del enlace. Se descubrió con
        // el collar entregando 4.815 tramas y el icono sin ponerse verde: `Conectado` sale de un
        // evento de TRANSICIÓN de Bluetooth, y al abrir sobre un aparato que ya estaba conectado no
        // hay transición que lo dispare. Exigir las dos cosas convertía la prueba en una opinión.
        Debe(true.Equals(metodo.Invoke(v, new object[] { false, 1000L, 2000L })),
            "si llega audio hay micrófono, aunque la bandera del enlace diga que no: la trama es la prueba");
    }

    /// <remarks>
    /// El fallo que la trajo, medido el 2026-09-01 con el selector recién dibujado: con el collar
    /// conectado por Bluetooth, elegir «micrófono del PC» o «teléfono» no hacía nada. La elección se
    /// pintaba y al segundo siguiente la prioridad automática la deshacía.
    ///
    /// Lo que se promete es la ASIMETRÍA: elegir apaga la automática, y sólo la ausencia de lo
    /// elegido la vuelve a encender. Un selector en el que la automática pueda ganar a una elección
    /// explícita no es un selector, es una sugerencia.
    /// </remarks>
    private static void LoElegidoManda()
    {
        var tipo = Ensamblado.GetType("Omi.Selector");
        var decidir = tipo?.GetMethod("Decidir", BindingFlags.Public | BindingFlags.Instance);
        var preferir = tipo?.GetMethod("Preferir", BindingFlags.Public | BindingFlags.Instance);
        if (tipo == null || decidir == null || preferir == null) { Pendiente("Omi.Selector.Preferir", "1"); return; }

        var origen = Ensamblado.GetType("Omi.Origen");
        if (origen == null) { Pendiente("Omi.Origen", "1"); return; }

        var s = Activator.CreateInstance(tipo);
        int Decide(bool ble, bool tel) => Convert.ToInt32(decidir.Invoke(s, new object[] { ble, tel, 0L }));
        // La preferencia viaja como enum anulable; aquí se construye desde su número para no tener
        // que referenciar el tipo. Plomería de reflexión, no una rebaja de lo prometido.
        void Preferir(int? n) => preferir.Invoke(s, new object?[] { n is null ? null : Enum.ToObject(origen, n.Value) });

        // EL CASO EXACTO DEL FALLO: collar conectado, y el médico pide el micrófono del PC.
        Preferir(0);
        Debe(Decide(true, false) == 0,
            "con el collar conectado, elegir el micrófono del PC se respeta: la automática no lo deshace");

        // Y el otro medio del mismo fallo: del collar al teléfono.
        Preferir(2);
        Debe(Decide(true, true) == 2,
            "con el collar conectado, elegir el teléfono se respeta");

        // Lo elegido manda mientras EXISTA. Si desaparece, no se puede quedar muda esperándolo.
        Debe(Decide(true, false) == 1,
            "si lo elegido deja de estar, se cae a la mejor disponible en vez de quedarse callada");

        // Y sin elección, la prioridad automática sigue mandando: es lo que hace que el collar se
        // use solo en cuanto aparece, sin que nadie toque nada.
        Preferir(null);
        Debe(Decide(true, false) == 1, "sin elección, manda la prioridad automática");
        Debe(Decide(false, false) == 0, "y sin nada disponible, el micrófono del PC");
    }

    /// <remarks>
    /// Lo que se congela aquí es la POLÍTICA, que es la parte que se puede juzgar sin Bluetooth: qué
    /// hay que hacer cuando el enlace vuelve. El transporte —soltar los objetos de WinRT, redescubrir
    /// el servicio, releer el códec— vive en <c>windows-client</c> y se comprueba a mano.
    ///
    /// La medida que la trajo, del log del 2026-09-01: 149 «volvió a conectarse», 145
    /// «ObjectDisposedException» al reenganchar, 5 líneas con tramas. Un remiendo que falla el 97 %
    /// de las veces no es un remiendo: es la prueba de que el camino era otro.
    /// </remarks>
    private static void ReconectarEsReconstruir()
    {
        var tipo = Ensamblado.GetType("Omi.Enlace");
        var metodo = tipo?.GetMethod("QueHacer", BindingFlags.Public | BindingFlags.Static);
        if (tipo == null || metodo == null) { Pendiente("Omi.Enlace.QueHacer", "7"); return; }

        // 0 = nada · 1 = reconstruir entero · 2 = relevar al micrófono local
        int Hacer(bool conectado, bool huboCaida) => Convert.ToInt32(metodo.Invoke(null, new object[] { conectado, huboCaida }));

        Debe(Hacer(true, true) == 1,
            "tras una caída, volver a estar conectado obliga a RECONSTRUIR, no a remendar");
        Debe(Hacer(true, false) == 0,
            "sin caída de por medio no hay nada que rehacer: un aviso repetido no reconstruye");
        Debe(Hacer(false, true) == 0,
            "y mientras siga caído no se reconstruye contra nada: se espera a que vuelva");
    }

    /// <remarks>
    /// El caso exacto, contado por el dueño el 2026-09-01: «se mostraba en verde el icono de
    /// Bluetooth mientras el Omi aún se mostraba en rojo, es decir que el Omi realmente no estaba
    /// transmitiendo todavía». Lo que pasaba: el reloj de la última trama era UNO SOLO para toda la
    /// aplicación, y lo movía cualquier audio — incluido el del micrófono del portátil, que estaba
    /// grabando de respaldo. El collar muerto heredaba la prueba de vida del micrófono.
    ///
    /// Lo que se congela: cada fuente responde por sí misma. Es lo que convierte el verde en una
    /// prueba y no en una coincidencia.
    /// </remarks>
    private static void CadaFuenteResponde()
    {
        var tipo = Ensamblado.GetType("Omi.Testigo");
        var anota = tipo?.GetMethod("Anota", BindingFlags.Public | BindingFlags.Instance);
        var ultima = tipo?.GetMethod("UltimaTrama", BindingFlags.Public | BindingFlags.Instance);
        var origen = Ensamblado.GetType("Omi.Origen");
        if (tipo == null || anota == null || ultima == null || origen == null) { Pendiente("Omi.Testigo", "4"); return; }

        var t = Activator.CreateInstance(tipo);
        void Anotar(int o, long ms) => anota.Invoke(t, new object[] { Enum.ToObject(origen, o), ms });
        long Ultima(int o) => Convert.ToInt64(ultima.Invoke(t, new object[] { Enum.ToObject(origen, o) }));

        // Llega audio por el micrófono del PC (0). El collar (1) NO puede heredar esa prueba.
        Anotar(0, 5000L);
        Debe(Ultima(0) == 5000L, "la fuente que entregó queda anotada con su hora");
        Debe(Ultima(1) == 0L,
            "y la que no entregó sigue a cero: el audio de una no es prueba de vida de la otra");
        Debe(Ultima(2) == 0L, "ni de la tercera");

        // Cada una lleva su propio reloj, sin pisarse.
        Anotar(1, 7000L);
        Debe(Ultima(0) == 5000L && Ultima(1) == 7000L,
            "dos fuentes entregando llevan dos relojes distintos, no uno compartido");
    }

    // ── La voz es GPT-Live (spec 018, 2026-09-12) ───────────────────────────

    /// <summary>
    /// GPT-Live pedido POR NOMBRE, con reflexión: la clase no existe cuando se escriben estas
    /// promesas, y así el contrato compila en rojo. Los argumentos que no se den toman el valor por
    /// defecto del constructor — que es justo lo que usa la app, y por eso también se juzga.
    /// </summary>
    private static IProtocolo? GptLive(params object?[] args)
    {
        var c = Realtime.GetType("Voz.Realtime.ProtocoloGptLive")?.GetConstructors().FirstOrDefault();
        if (c == null) return null;
        var valores = c.GetParameters()
            .Select((p, i) => i < args.Length ? args[i] : p.HasDefaultValue ? p.DefaultValue : null).ToArray();
        return c.Invoke(valores) as IProtocolo;
    }

    /// <summary>Un miembro de <see cref="IProtocolo"/> que todavía puede no existir. Por la interfaz,
    /// no por la clase: así se juzga también el valor por defecto que hereda quien no lo declara.</summary>
    private static object? DeLaInterfaz(string propiedad, IProtocolo p)
        => typeof(IProtocolo).GetProperty(propiedad)?.GetValue(p);

    private static List<string>? CambioDeModo(IProtocolo p, string instrucciones, IReadOnlyList<Utensilio> utensilios, bool soloCuandoSeLePide)
        => (typeof(IProtocolo).GetMethod("CambioDeModo")?.Invoke(p, new object[] { instrucciones, utensilios, soloCuandoSeLePide })
            as IEnumerable<string>)?.ToList();

    private static JsonElement? Nodo(JsonElement e, params string[] camino)
    {
        foreach (string paso in camino)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(paso, out var siguiente)) return null;
            e = siguiente;
        }
        return e;
    }

    /// <summary>El campo como texto; vacío si no está. Un número sale con su forma cruda («24000»).</summary>
    private static string Campo(JsonElement e, params string[] camino)
        => Nodo(e, camino) is { } x ? x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : x.GetRawText() : "";

    /// <remarks>
    /// EL FALLO QUE ESTO IMPIDE: abrir GPT-Live como si fuera Realtime con otro nombre. Se probó el
    /// 2026-09-11: gpt-live-1 en /v1/realtime da «not supported in realtime mode», y las herramientas
    /// en la sesión de la voz no se aceptan — las llama un modelo DELEGADO (gpt-5.6-luna) que va en
    /// session.delegation.responses. Si las instrucciones completas de Ü se quedaran en la voz, el que
    /// opera la pantalla no sabría qué es un recuerdo ni cómo se pulsa un botón de SAP.
    /// </remarks>
    private static void GptLiveAbrePorSuPropiaPuerta()
    {
        var p = GptLive();
        if (p == null) { Pendiente("Voz.Realtime.ProtocoloGptLive", "1"); return; }

        Debe(p.Modelo == "gpt-live-1", $"el modelo es gpt-live-1, clavado y no un alias (dice «{p.Modelo}»)");
        Debe(p.Direccion().AbsoluteUri == "wss://api.openai.com/v1/live/sessions",
            $"abre por /v1/live/sessions y sin ?model=, que es su puerta (abre «{p.Direccion()}»)");
        Debe(p.Cabeceras("clave-de-prueba").TryGetValue("Authorization", out var aut) && aut == "Bearer clave-de-prueba",
            "la clave va en la cabecera Authorization, no en la URL: una URL acaba en los logs");
        Debe(p.RitmoDeEntrada == 24000 && p.RitmoDeSalida == 24000,
            $"PCM a 24 kHz en los dos sentidos (entrada {p.RitmoDeEntrada}, salida {p.RitmoDeSalida})");

        const string primeraLinea = "ERES Ü Y ESTAS SON TUS INSTRUCCIONES COMPLETAS";
        string completas = InstruccionesComoLasDeU(primeraLinea);
        var utensilios = new List<Utensilio> { new("map_look", "Mira la pantalla", new List<Argumento> { new("que", "qué mirar") }) };
        var apertura = p.Apertura(completas, utensilios, "").ToList();
        Debe(apertura.Count == 1, $"la apertura es UN mensaje (salieron {apertura.Count})");
        if (apertura.Count == 0) return;

        var m = Mensaje(apertura[0]);
        Debe(Campo(m, "type") == "session.start",
            $"y ese mensaje es session.start, no el session.update de Realtime (es «{Campo(m, "type")}»)");
        Debe(Campo(m, "session", "model") == "gpt-live-1", "la sesión pide gpt-live-1");
        Debe(Campo(m, "session", "audio", "format", "type") == "audio/pcm" && Campo(m, "session", "audio", "format", "rate") == "24000",
            "el audio se declara audio/pcm a 24000");
        Debe(Campo(m, "session", "audio", "output", "voice") == "marin", "con la voz marin, la misma de siempre");
        Debe(Nodo(m, "session", "tools") == null,
            "la VOZ no lleva herramientas: el servidor no las acepta ahí, y ponerlas es perderlas");
        Debe(Campo(m, "session", "delegation", "type") == "responses", "hay delegación, de tipo responses");
        Debe(Campo(m, "session", "delegation", "responses", "model") == "gpt-5.6-luna",
            $"con gpt-5.6-luna por defecto como delegado (pide «{Campo(m, "session", "delegation", "responses", "model")}»)");

        var tools = Nodo(m, "session", "delegation", "responses", "tools");
        Debe(tools is { ValueKind: JsonValueKind.Array } t && t.GetArrayLength() == 1
             && Campo(t[0], "type") == "function" && Campo(t[0], "name") == "map_look"
             && Campo(t[0], "parameters", "type") == "object"
             && Campo(t[0], "parameters", "properties", "que", "type") == "string",
            "las herramientas de Ü van en la delegación, como function con sus argumentos de texto");
        Debe(Campo(m, "session", "delegation", "responses", "tool_choice") == "auto", "y el delegado elige cuándo usarlas");
        string alDelegado = Campo(m, "session", "delegation", "responses", "instructions");
        Debe(MismosBytes(alDelegado, completas),
            $"las instrucciones completas de Ü van al delegado, que es quien mira y opera, ÍNTEGRAS: iguales byte a byte ({completas.Length} caracteres; llegan {alDelegado.Length})");
        string deLaVoz = Campo(m, "session", "instructions");
        Debe(deLaVoz.Trim().Length > 0 && !deLaVoz.Contains(primeraLinea),
            "y la voz lleva las suyas, cortas: quién es y que delega todo lo que sea mirar u operar");

        var terra = GptLive("gpt-live-1", "gpt-5.6-terra");
        string conTerra = terra == null ? "" : Campo(Mensaje(terra.Apertura(completas, utensilios, "").First()), "session", "delegation", "responses", "model");
        Debe(conTerra == "gpt-5.6-terra", $"el delegado se elige al construir, no va escrito dentro (con terra pidió «{conTerra}»)");
    }

    /// <summary>
    /// UNAS INSTRUCCIONES DEL TAMAÑO DE LAS DE Ü, no un marcador. Las de Ü miden 20.694 caracteres
    /// (21.497 bytes UTF-8, medido el 2026-09-12 por la sonda de huecos), y comprobarlas con Contains de
    /// una frase de 46 dejaba en verde recortarlas: el sabotaje V2 de la revisión —cortar a 4.000, una
    /// defensa verosímil ante el tope de 16.384 fichas— salió VOZ ÍNTEGRA. Estas miden más que las de Ü,
    /// llevan lo que JSON escapa (tildes, Ü, comillas, barra invertida, tabulador, saltos de línea) y
    /// terminan en la regla que se pierde primero si alguien las corta.
    /// </summary>
    private static string InstruccionesComoLasDeU(string primeraLinea)
    {
        var sb = new System.Text.StringBuilder(primeraLinea).Append('\n');
        for (int i = 1; sb.Length < 24_000; i++)
            sb.Append($"Regla {i:D4}: «no anuncies», pulsa \"NV44\" en SAP\\GUI, ñandú y Ü;\ttermina.\n");
        return sb.Append("ÚLTIMA REGLA: la que se pierde si alguien las recorta.").ToString();
    }

    /// <summary>Iguales byte a byte en UTF-8: lo que llega al servidor, no una parte que se le parezca.</summary>
    private static bool MismosBytes(string llegan, string mandadas)
        => System.Text.Encoding.UTF8.GetBytes(llegan).SequenceEqual(System.Text.Encoding.UTF8.GetBytes(mandadas));

    /// <remarks>
    /// Los mensajes son los que mandó el servidor el 2026-09-12. Tres trampas, las tres silenciosas:
    ///
    ///  · LA LLAMADA LLEGA TRES VECES, envuelta en response.event: response.output_item.added con el
    ///    item EN CURSO (call_id y name, arguments vacío), response.function_call_arguments.done y
    ///    response.output_item.done con el item completo. El borrador decía «dos veces»; la revisión del
    ///    2026-09-12 capturó la tercera. Traducir más de una es ejecutar la herramienta dos o tres veces.
    ///  · response.completed ES DEL DELEGADO, no de la voz: la voz sigue hablando segundos después.
    ///    Tomarlo por cierre de turno cortaría la frase de Ü por la mitad.
    ///  · NO HAY speech_started. Hablarle encima no produce ningún evento; un traductor que lo
    ///    fingiera callaría a Ü por cualquier cosa.
    /// </remarks>
    private static void GptLiveTraduceLoQueManda()
    {
        var p = GptLive();
        if (p == null) { Pendiente("Voz.Realtime.ProtocoloGptLive", "1"); return; }

        byte[] pcm = { 1, 2, 3, 4, 250, 251, 0, 255 };
        string b64 = Convert.ToBase64String(pcm);
        var suena = p.Leer(Mensaje($$"""{"type":"session.output_audio.delta","delta":"{{b64}}"}"""));
        Debe(suena.Count == 1 && suena[0] is Hecho.Suena s && s.Pcm.SequenceEqual(pcm),
            "un trozo de session.output_audio.delta es UN Hecho.Suena con el PCM exacto");
        Debe(p.Leer(Mensaje("""{"type":"session.output_audio.delta","delta":""}""")).Count == 0,
            "y un delta vacío no produce sonido de la nada");

        var dice = p.Leer(Mensaje("""{"type":"session.output_transcript.delta","delta":" Veo la pantalla principal"}"""));
        Debe(dice.Count == 1 && dice[0] is Hecho.DiceU { Trozo: " Veo la pantalla principal" },
            "session.output_transcript.delta es lo que dice Ü, con su trozo intacto");
        var oye = p.Leer(Mensaje("""{"type":"session.input_transcript.delta","delta":"mira la pantalla"}"""));
        Debe(oye.Count == 1 && oye[0] is Hecho.DiceElUsuario { Trozo: "mira la pantalla" },
            "session.input_transcript.delta es lo que dice el usuario");

        var llamada = p.Leer(Mensaje("""
            {"event_id":"event_ENOyB5NxpE0Gh7AUuGbMK","type":"response.event","delegation_id":"item_ENOyA2AApz1ePY8UFCDIw","event":{"type":"response.output_item.done","item":{"id":"fc_0b0f65fd1c15980c006aa5bb1b98e487d1a21fef4659aabd29","type":"function_call","status":"completed","arguments":"{\"que\":\"Mira toda la pantalla y describe claramente qué aparece\"}","call_id":"call_I4zD28ktm3U3JVOTbkgCO34C","name":"map_look"},"output_index":0,"sequence_number":27}}
            """));
        var pide = llamada.OfType<Hecho.Pide>().SelectMany(x => x.Cuales).ToList();
        Debe(pide.Count == 1 && pide[0].Id == "call_I4zD28ktm3U3JVOTbkgCO34C" && pide[0].Nombre == "map_look",
            $"la llamada delegada llega como UN Hecho.Pide con su call_id y su nombre (salieron {pide.Count})");
        Debe(pide.Count == 1 && pide[0].Args.TryGetValue("que", out var que) && que == "Mira toda la pantalla y describe claramente qué aparece",
            "con los argumentos LEÍDOS del texto JSON que traen, no un diccionario vacío");

        var argumentos = p.Leer(Mensaje("""
            {"event_id":"event_ENOyBSBbqW2BEA5p7TSif","type":"response.event","delegation_id":"item_ENOyA2AApz1ePY8UFCDIw","event":{"type":"response.function_call_arguments.done","arguments":"{\"que\":\"Mira toda la pantalla y describe claramente qué aparece\"}","item_id":"fc_0b0f65fd1c15980c006aa5bb1b98e487d1a21fef4659aabd29","output_index":0,"sequence_number":26}}
            """));
        Debe(!argumentos.Any(h => h is Hecho.Pide),
            "los argumentos terminados NO son otra llamada: el servidor manda las dos, y traducir las dos ejecuta la herramienta dos veces");

        // Y LLEGA UNA TERCERA VEZ, la primera en el tiempo: response.output_item.added, con el item
        // function_call EN CURSO —call_id y name ya puestos, arguments vacío—. Las tres copias de UNA
        // llamada, capturadas juntas el 2026-09-12 (rev-sonda-added.ps1): added a 1551 ms,
        // arguments.done a 1788 y output_item.done a 1822. Leer «response.output_item.*» sin distinguir
        // added de done ejecutaría map_look dos veces —la primera sin argumentos— y una sola llamada
        // gastaría el tope de 2 intentos de la 204. Se juzga el TOTAL de las tres, no cada una suelta.
        string[] tresCopias =
        {
            """{"event_id":"event_ENQCU7SRZjenI4jZV3IPk","type":"response.event","delegation_id":"item_ENQCUyla2TB9mFPFUmDHi","event":{"type":"response.output_item.added","item":{"id":"fc_0135f681d41dbe77006aa5cd96d41c87d183fffa8e5ea4ae5b","type":"function_call","status":"in_progress","arguments":"","call_id":"call_ydaLTWADFkH6AtEXUxsfdltF","name":"map_look"},"output_index":0,"sequence_number":2}}""",
            """{"event_id":"event_ENQCVuB7VByR5RTl3N7bO","type":"response.event","delegation_id":"item_ENQCUyla2TB9mFPFUmDHi","event":{"type":"response.function_call_arguments.done","arguments":"{\"que\":\"Mira la pantalla completa y describe brevemente qué aparece, especialmente cualquier texto, botón o elemento relevante para la solicitud del usuario.\"}","item_id":"fc_0135f681d41dbe77006aa5cd96d41c87d183fffa8e5ea4ae5b","output_index":0,"sequence_number":33}}""",
            """{"event_id":"event_ENQCVNq9s9tBGNWcT02bm","type":"response.event","delegation_id":"item_ENQCUyla2TB9mFPFUmDHi","event":{"type":"response.output_item.done","item":{"id":"fc_0135f681d41dbe77006aa5cd96d41c87d183fffa8e5ea4ae5b","type":"function_call","status":"completed","arguments":"{\"que\":\"Mira la pantalla completa y describe brevemente qué aparece, especialmente cualquier texto, botón o elemento relevante para la solicitud del usuario.\"}","call_id":"call_ydaLTWADFkH6AtEXUxsfdltF","name":"map_look"},"output_index":0,"sequence_number":34}}""",
        };
        Debe(!p.Leer(Mensaje(tresCopias[0])).Any(h => h is Hecho.Pide),
            "output_item.added NO es la llamada: llega en curso y con arguments vacío, y ejecutarla es mirar sin saber qué");
        var enLasTres = tresCopias.SelectMany(x => p.Leer(Mensaje(x))).OfType<Hecho.Pide>().SelectMany(x => x.Cuales).ToList();
        Debe(enLasTres.Count == 1 && enLasTres[0].Id == "call_ydaLTWADFkH6AtEXUxsfdltF" && enLasTres[0].Nombre == "map_look"
             && enLasTres[0].Args.TryGetValue("que", out var queDeLasTres)
             && queDeLasTres == "Mira la pantalla completa y describe brevemente qué aparece, especialmente cualquier texto, botón o elemento relevante para la solicitud del usuario.",
            $"las TRES copias que manda el servidor de una misma llamada —added, arguments.done y output_item.done— son UNA llamada en total, con sus argumentos (salieron {enLasTres.Count}: "
            + string.Join(" · ", enLasTres.Select(x => $"{x.Nombre}[{x.Args.Count} arg]")) + ")");
        var mensajeDelDelegado = p.Leer(Mensaje("""
            {"type":"response.event","event":{"type":"response.output_item.done","item":{"id":"msg_1","type":"message","status":"completed","content":[]}}}
            """));
        Debe(!mensajeDelDelegado.Any(h => h is Hecho.Pide), "y un item terminado que no es function_call no pide nada");

        var error = p.Leer(Mensaje("""
            {"type":"error","event_id":"event_ENOy9VsLzg0PIpUAbkOqn","error":{"type":"invalid_request_error","code":"unknown_parameter","message":"Unknown parameter: 'session.instructions'.","param":"session.instructions","client_event_id":"sonda_voz"}}
            """));
        Debe(error.Count == 1 && error[0] is Hecho.Falla f && f.Que.Contains("Unknown parameter: 'session.instructions'."),
            "un error es un Hecho.Falla con el mensaje del servidor, no un silencio");
        var cerrada = p.Leer(Mensaje("""{"event_id":"event_ENOyNXoVEuKQV2BXwf0YH","type":"session.closed","reason":"close_requested","usage":{"seconds":13.0},"client_event_id":"sonda_fin"}"""));
        Debe(cerrada.Count == 1 && cerrada[0] is Hecho.Falla fc && fc.Que.Contains("close_requested"),
            "y una sesión cerrada por el servidor se cuenta como falla, con su motivo");

        // Todo lo que se leyó arriba, más lo que el servidor manda sin que toque a nadie.
        string[] sinMarca =
        {
            """{"type":"session.started","session":{"id":"live_u2_ENOy6GhblDeLrMlDOGSX1","model":"gpt-live-1","status":"active","input":[]}}""",
            """{"type":"session.delegation.created","offset_ms":2600,"delegation":{"id":"item_ENOyA2AApz1ePY8UFCDIw","type":"delegation","response_id":"resp_0b0f65fd","target":"responses"},"client_event_id":"sonda_pide"}""",
            """{"type":"response.event","delegation_id":"item_ENOyA2AApz1ePY8UFCDIw","event":{"type":"response.completed"}}""",
            """{"type":"session.usage.updated","usage":{"seconds":12.0},"context_window":{"usage_ratio":0.01003125}}""",
        };
        var todos = sinMarca.SelectMany(x => p.Leer(Mensaje(x)))
            .Concat(suena).Concat(dice).Concat(oye).Concat(llamada).Concat(argumentos).Concat(error).Concat(cerrada).ToList();
        Debe(!todos.Any(h => h is Hecho.CierraElTurno or Hecho.HablaronEncima),
            "ningún mensaje de GPT-Live se traduce en cierre de turno ni en «hablaron encima»: el servidor no los manda, "
            + "y response.completed es del delegado — la voz sigue hablando después");

        var marca = DeLaInterfaz("MarcaLosTurnos", p);
        if (marca == null) { Pendiente("IProtocolo.MarcaLosTurnos", "1"); return; }
        Debe(marca is false, "y lo DECLARA: MarcaLosTurnos es falso, para que la conversación los marque ella");
        Debe(DeLaInterfaz("MarcaLosTurnos", new ProtocoloOpenAI()) is true,
            "mientras GPT Realtime, que sí manda speech_started y response.done, sigue diciendo que los marca");
    }

    /// <remarks>
    /// Tres cosas medidas el 2026-09-12 que esta promesa congela:
    ///
    ///  · UN TEXTO SIN response.create DETRÁS NO SE CONTESTA; con él, delega, llama la herramienta y
    ///    habla. Por eso Texto no pide turno —eso es PedirRespuesta, aparte, como en Realtime—.
    ///  · DICTAR es session.commentary.append: dijo la frase literal. Un response.create con
    ///    instrucciones, que es lo que usa Realtime, aquí no existe.
    ///  · LA SESIÓN ES INMUTABLE SALVO LA DELEGACIÓN: session.update con session.instructions contesta
    ///    «Unknown parameter: 'session.instructions'», y con session.delegation contesta session.updated
    ///    — y el delegado llamó a la herramienta NUEVA (map_where_am_i, con su argumento nuevo). Otro
    ///    session.start a mitad de sesión no es cambiar de modo: es otra sesión.
    /// </remarks>
    private static void GptLiveMandaConSusEventos()
    {
        var p = GptLive();
        if (p == null) { Pendiente("Voz.Realtime.ProtocoloGptLive", "1"); return; }

        byte[] pcm = { 9, 8, 7, 6, 5, 4 };
        var audio = Mensaje(p.Audio(pcm));
        Debe(Campo(audio, "type") == "session.input_audio.append" && Campo(audio, "audio") == Convert.ToBase64String(pcm),
            $"el micrófono va como session.input_audio.append con el PCM en base64 exacto (va «{Campo(audio, "type")}»)");

        var texto = Mensaje(p.Texto("abre la admisión"));
        var contenido = Nodo(texto, "item", "content");
        Debe(Campo(texto, "type") == "response.item.create" && Campo(texto, "item", "type") == "message" && Campo(texto, "item", "role") == "user"
             && contenido is { ValueKind: JsonValueKind.Array } ct && ct.GetArrayLength() == 1
             && Campo(ct[0], "type") == "input_text" && Campo(ct[0], "text") == "abre la admisión",
            "el texto escrito es un response.item.create: mensaje del usuario con un input_text");

        // Si GPT-Live mira o no lo juzga la 49: la FORMA de la foto sí se leyó, pero una de tamaño real no cabe.
        byte[] jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 };
        var foto = Mensaje(p.Fotograma(jpeg));
        var enFoto = Nodo(foto, "item", "content");
        Debe(Campo(foto, "type") == "response.item.create" && Campo(foto, "item", "role") == "user"
             && enFoto is { ValueKind: JsonValueKind.Array } cf
             && cf.EnumerateArray().Any(x => Campo(x, "type") == "input_image"
                 && Campo(x, "image_url") == "data:image/jpeg;base64," + Convert.ToBase64String(jpeg)),
            "la foto es un mensaje del usuario con un input_image en data URL JPEG");

        var hechas = new List<(string Id, string Nombre, string Resultado)>
            { ("call_1", "map_look", "SAP Easy Access"), ("call_2", "map_where_am_i", "NWP1") };
        var resultados = p.Resultados(hechas).ToList();
        var leidos = resultados.Select(Mensaje).ToList();
        Debe(leidos.Count == 2
             && leidos.All(r => Campo(r, "type") == "response.item.create" && Campo(r, "item", "type") == "function_call_output")
             && Campo(leidos[0], "item", "call_id") == "call_1" && Campo(leidos[0], "item", "output") == "SAP Easy Access"
             && Campo(leidos[1], "item", "call_id") == "call_2" && Campo(leidos[1], "item", "output") == "NWP1",
            $"cada resultado es su propio function_call_output con su call_id (salieron {leidos.Count})");
        Debe(!resultados.Any(r => r.Contains("\"response.create\"")),
            "y devolver resultados no pide turno por su cuenta: eso es un paso aparte");

        var turno = Mensaje(p.PedirRespuesta());
        Debe(Campo(turno, "type") == "response.create", $"pedir turno es response.create (es «{Campo(turno, "type")}»)");
        const string frase = "Di exactamente esto, sin añadir nada ni comentarlo: voy por el peso";
        var dictado = Mensaje(p.PedirRespuesta(frase));
        Debe(Campo(dictado, "type") == "session.commentary.append"
             && Nodo(dictado, "delegation_id") is { ValueKind: JsonValueKind.Null }
             && Campo(dictado, "content") == frase,
            $"dictar es session.commentary.append con delegation_id nulo y la frase entera (es «{Campo(dictado, "type")}»)");

        var nuevas = new List<Utensilio> { new("map_where_am_i", "Dice en qué pantalla está", new List<Argumento> { new("detalle", "cuánto detalle") }) };
        string otroModo = InstruccionesComoLasDeU("AHORA SOLO DICES LO QUE SE TE PIDE");
        var cambio = CambioDeModo(p, otroModo, nuevas, false);
        var sabe = DeLaInterfaz("SabeEsperarTurno", p);
        if (cambio == null || sabe == null) { Pendiente("IProtocolo.CambioDeModo / IProtocolo.SabeEsperarTurno", "1"); return; }

        // UN session.update y ningún session.start; ya no «un único mensaje»: desde la 47, detrás va el
        // session.instructions.append que cambia también a quien habla (medido el 2026-09-12). Lo que esta
        // promesa congela de la delegación no cambia.
        var cambios = cambio.Select(Mensaje).ToList();
        var actualizaciones = cambios.Where(x => Campo(x, "type") == "session.update").ToList();
        Debe(actualizaciones.Count == 1 && !cambios.Any(x => Campo(x, "type") == "session.start"),
            $"cambiar de modo es UN session.update, nunca otro session.start (salió: {string.Join(", ", cambios.Select(x => Campo(x, "type")))})");
        if (actualizaciones.Count == 0) return;
        var upd = actualizaciones[0];
        var toolsNuevas = Nodo(upd, "session", "delegation", "responses", "tools");
        Debe(Campo(upd, "session", "delegation", "type") == "responses"
             && toolsNuevas is { ValueKind: JsonValueKind.Array } tn && tn.GetArrayLength() == 1
             && Campo(tn[0], "name") == "map_where_am_i" && Campo(tn[0], "parameters", "properties", "detalle", "type") == "string"
             && Campo(upd, "session", "delegation", "responses", "model") == "gpt-5.6-luna",
            "el session.update lleva la delegación entera: su modelo y las herramientas nuevas");
        string nuevasAlDelegado = Campo(upd, "session", "delegation", "responses", "instructions");
        Debe(MismosBytes(nuevasAlDelegado, otroModo),
            $"y las instrucciones nuevas ÍNTEGRAS, iguales byte a byte ({otroModo.Length} caracteres; llegan {nuevasAlDelegado.Length})");
        Debe(Nodo(upd, "session", "instructions") == null && Nodo(upd, "session", "model") == null && Nodo(upd, "session", "audio") == null,
            "y no toca nada fuera de la delegación: session.instructions a mitad de sesión el servidor lo rechaza");

        Debe(sabe is false,
            "GPT-Live no sabe esperar turno (no hay create_response ni turn_detection: «no hables por tu cuenta» no se respetó), y lo declara");
        var realtime = new ProtocoloOpenAI();
        Debe(DeLaInterfaz("SabeEsperarTurno", realtime) is true, "GPT Realtime sí sabe, y lo sigue diciendo");
        string prestada = string.Join("\n", CambioDeModo(realtime, "x", nuevas, true) ?? new List<string>());
        Debe(prestada.Contains("\"session.update\"") && prestada.Contains("\"create_response\":false"),
            "y en GPT Realtime cambiar de modo sigue siendo su apertura: la de la voz prestada lleva create_response=false");
    }

    /// <remarks>
    /// gpt-4o-mini-transcribe está deprecado y se apaga el 2027-02-26: ese día la carita dejaría de
    /// escribir lo que oye sin que nada diera error. Medido el 2026-09-12 con la misma frase hablada
    /// contra gpt-realtime-2.1-mini: gpt-transcribe se acepta (session.updated sin error) y manda la
    /// transcripción POR TROZOS igual que el antiguo —9 deltas y la misma frase, «Mira la pantalla y
    /// dime qué ves.»—, que es lo único que lee el traductor. Aceptarse no bastaba: un transcriptor que
    /// solo mandara .completed dejaría la transcripción del usuario muda igual.
    /// </remarks>
    private static void LaTranscripcionNoVaAlQueSeApaga()
    {
        IProtocolo p = new ProtocoloOpenAI();
        var m = Mensaje(p.Apertura("x", new List<Utensilio>(), "").First());
        string modelo = Campo(m, "session", "audio", "input", "transcription", "model");
        Debe(modelo == "gpt-transcribe", $"la transcripción se pide a gpt-transcribe (pide «{modelo}»)");
    }

    /// <remarks>
    /// EL SILENCIO SONABA. Medido el 2026-09-12 contra /v1/live/sessions (sonda-silencio-pico.ps1, tres
    /// sesiones): el servidor manda un delta de 100 ms (4800 B) cada ~100–130 ms aunque la voz calle, y
    /// ese silencio son CEROS EXACTOS — 43 de 47, 168 de 172 y 60 de 64 deltas; los cuatro que no, en
    /// las tres, son la cola que se apaga al abrir la sesión (picos 45, 8, 3 y 2). Traducido a
    /// Hecho.Suena entraba a la cola del altavoz, y LiveAudio.Hablando parpadeaba sin que nadie hablara.
    ///
    /// EL UMBRAL ES CERO, y también es medida, no gusto: DENTRO de una frase de Ü las pausas entre
    /// oraciones bajan a pico 1 (0, 14 y 11 deltas por frase: hasta 1,4 s de pausa) y una vez a cero
    /// exacto (1 delta de 256). Un umbral de 64 —el que sugería el pico 45 del silencio— se comía 11, 27
    /// y 21 deltas de pausa, y Ü diría «Uno.Dos.Tres.» de corrido. Con cero se pierde a lo sumo ese
    /// delta suelto: 100 ms de una pausa.
    /// </remarks>
    private static void GptLiveNoHaceSonarSuSilencio()
    {
        var p = GptLive();
        if (p == null) { Pendiente("Voz.Realtime.ProtocoloGptLive", "1"); return; }

        static JsonElement Delta(byte[] pcm)
            => Mensaje($$"""{"type":"session.output_audio.delta","delta":"{{Convert.ToBase64String(pcm)}}"}""");

        var silencio = new byte[4800];
        var callado = p.Leer(Delta(silencio));
        Debe(!callado.Any(h => h is Hecho.Suena),
            $"un delta de 100 ms con todas las muestras a cero, como manda el servidor cuando la voz calla, no es Hecho.Suena (salieron {callado.Count(h => h is Hecho.Suena)})");

        // La pausa entre dos oraciones, como la midió la sonda: todo cero salvo alguna muestra a ±1.
        var pausa = new byte[4800];
        pausa[2400] = 0x01;                       // muestra 1200 = +1
        pausa[3600] = 0xFF; pausa[3601] = 0xFF;   // muestra 1800 = −1
        var enPausa = p.Leer(Delta(pausa));
        Debe(enPausa.Count == 1 && enPausa[0] is Hecho.Suena sp && sp.Pcm.SequenceEqual(pausa),
            "la pausa de pico 1 entre dos frases SÍ suena, con su PCM exacto: quitarla acorta lo que dice Ü");

        var soloAlto = new byte[4800];
        soloAlto[1001] = 0x01;                    // muestra 500 = 256: cero en el byte bajo
        var alto = p.Leer(Delta(soloAlto));
        Debe(alto.Count == 1 && alto[0] is Hecho.Suena sa && sa.Pcm.SequenceEqual(soloAlto),
            "y una muestra que solo tiene el byte ALTO distinto de cero también es sonido: el umbral mira muestras, no bytes sueltos");

        var voz = new byte[4800];
        for (int i = 0; i < voz.Length / 2; i++)
            BitConverter.TryWriteBytes(voz.AsSpan(2 * i), (short)(7000 * Math.Sin(2 * Math.PI * 220 * i / 24000.0)));
        var hablando = p.Leer(Delta(voz));
        Debe(hablando.Count == 1 && hablando[0] is Hecho.Suena sv && sv.Pcm.SequenceEqual(voz),
            "y un delta con voz (pico 7000, como las frases medidas) es UN Hecho.Suena con el PCM exacto");
    }

    /// <remarks>
    /// EL FALLO QUE ESTO IMPIDE: la 161 del grafo quitó el «voy a…» de Ü, y con GPT-Live esa regla viaja
    /// al DELEGADO, que no habla. La voz abre con su persona corta y en la sonda del 2026-09-12 dijo
    /// «Vale. Dame un momento para revisarlo.» y «Dime a qué transacción quieres ir y la abro.»: en
    /// futuro, antes de que el delegado hubiera hecho nada. Se juzga el session.start que sale, no la
    /// constante: una persona bien escrita que no llegara a la sesión no cumpliría nada.
    /// </remarks>
    private static void LaVozNoAnuncia()
    {
        var p = GptLive();
        if (p == null) { Pendiente("Voz.Realtime.ProtocoloGptLive", "1"); return; }

        var inicio = p.Apertura("INSTRUCCIONES DEL DELEGADO", new List<Utensilio>(), "").ToList();
        Debe(inicio.Count == 1, $"la apertura es un mensaje (salieron {inicio.Count})");
        if (inicio.Count == 0) return;
        string voz = Campo(Mensaje(inicio[0]), "session", "instructions");

        Debe(voz.Contains("NO ANUNCIES LO QUE VAS A HACER", StringComparison.Ordinal),
            "la persona de la voz dice la regla, con las mismas palabras que la 161 le exige al delegado");
        Debe(voz.Contains("HABLA EN PASADO", StringComparison.Ordinal),
            "y dice con qué sustituirlo: en pasado y del resultado. Prohibir sin dar el reemplazo deja a la voz eligiendo, y elige anunciar");
        foreach (string relleno in new[] { "«voy a…»", "«vamos a…»", "«dame un momento»" })
            Debe(voz.Contains(relleno, StringComparison.Ordinal),
                $"y nombra las fórmulas que se oyen ({relleno}): «Dame un momento para revisarlo» es literal de la sonda");
        Debe(!voz.Contains("INSTRUCCIONES DEL DELEGADO", StringComparison.Ordinal),
            "y la regla va en la persona de la VOZ, no copiando las del delegado: la voz sigue sin las instrucciones de operar");
    }

    /// <remarks>
    /// EL FALLO QUE ESTO IMPIDE: 🎓 (promesa 138) y la voz prestada (192) solo cambiaban al DELEGADO. Medido
    /// el 2026-09-12 contra el servidor, con la narración hablada «Ahora escribo NWP1 en el campo de
    /// transacción y pulso Enter» después de poner el modo aprendiz:
    ///
    ///  · solo el session.update (lo que hacía la rama): 3 de 3 la voz afirmó lo que nadie hizo —
    ///    «Listo, ejecuté VP1 en el campo de transacción», «Ya quedó lanzada», «Estás en la pantalla
    ///    inicial de ese programa»—.
    ///  · update + session.instructions.append con las reglas del aprendiz: 3 de 3 asintió con una
    ///    palabra («Ajá», «Uhum», «Entiendo») y no afirmó nada.
    ///  · y al volver con un append de su persona: 2 de 2 delegó map_look y dijo «Estabas en SAP Easy Access».
    ///
    /// Al volver NO se le pasan las instrucciones de operar: la voz no las lleva (40), y el servidor
    /// rechaza un append de más de 500 fichas («Context append text must not exceed 500 tokens.», medido
    /// con 2.400 caracteres; las de Ü son 20.694).
    /// </remarks>
    private static void CambiarDeModoCambiaLaVoz()
    {
        var p = GptLive();
        if (p == null) { Pendiente("Voz.Realtime.ProtocoloGptLive", "1"); return; }

        const string completas = "ERES Ü Y ESTAS SON TUS INSTRUCCIONES COMPLETAS DE OPERAR";
        const string aprendiz = """
            Eres Ü, y ahora mismo te están ENSEÑANDO.
              · Habla muy poco. Mientras te explican, asiente con algo corto: «ajá», «uhum», «entiendo».
              · No hagas nada, no lo intentes, no digas que lo vas a hacer.
            """;
        var utensilios = new List<Utensilio> { new("map_look", "Mira la pantalla", new List<Argumento>()) };
        string deLaVoz = Campo(Mensaje(p.Apertura(completas, utensilios, "").First()), "session", "instructions");

        List<JsonElement>? Cambio(string instrucciones) => CambioDeModo(p, instrucciones, utensilios, false)?.Select(Mensaje).ToList();
        string Tipos(List<JsonElement> m) => string.Join(" · ", m.Select(x => Campo(x, "type")));

        var aAprendiz = Cambio(aprendiz);
        if (aAprendiz == null) { Pendiente("IProtocolo.CambioDeModo", "1"); return; }
        int iUpd = aAprendiz.FindIndex(x => Campo(x, "type") == "session.update");
        var anadidos = aAprendiz.Where(x => Campo(x, "type") == "session.instructions.append").ToList();
        Debe(iUpd >= 0 && anadidos.Count == 1 && aAprendiz.FindIndex(x => Campo(x, "type") == "session.instructions.append") > iUpd,
            $"cambiar de modo es el session.update de la delegación y DESPUÉS un session.instructions.append a la voz (salió: {Tipos(aAprendiz)})");
        if (anadidos.Count == 0) return;

        // LOS PREFIJOS SON LOS TEXTOS MEDIDOS, y se comparan letra por letra (revisión contrato r2, 2026-09-13). Hasta
        // aquí se miraba Contains, y dos sabotajes de una línea dejaban VOZ ÍNTEGRA (medido): el append del aprendiz
        // sin «CAMBIO DE MODO…», y la vuelta con el prefijo de cambiar de modo en vez de «VUELVES…». La sonda de ese
        // día no vio que el prefijo fuera lo que hace asentir: sin él también asintió, 3 de 3, y con el prefijo
        // equivocado también volvió a delegar, 2 de 2. Se congelan igual, por dos razones medidas: son los textos con
        // los que se midió todo lo demás (Cambiarlos es volver a medir, dice ProtocoloGptLive), y el append del
        // aprendiz va al tope de 500 fichas — un prefijo más largo lo haría rechazar, y con solo el session.update la
        // voz afirmó acciones que nadie hizo, 3 de 3 (2026-09-12).
        const string alCambiarDeModoMedido = "CAMBIO DE MODO. Desde ahora mandan estas reglas sobre cuándo y cómo hablas, por encima de las anteriores:\n";
        const string alVolverMedido = "VUELVES A TU MODO DE SIEMPRE. Lo anterior sobre el modo especial ya no manda; desde ahora mandan estas reglas:\n";
        static string Diferencia(string esperado, string llega)
        {
            int i = 0;
            while (i < esperado.Length && i < llega.Length && esperado[i] == llega[i]) i++;
            return $"esperados {esperado.Length} caracteres, llegan {llega.Length}; la primera diferencia en el {i}: «{llega.Substring(i, Math.Min(40, llega.Length - i))}»";
        }

        string alAprendiz = Campo(anadidos[0], "content");
        Debe(alAprendiz == alCambiarDeModoMedido + aprendiz,
            $"el append es el prefijo medido («CAMBIO DE MODO…») y detrás las reglas del modo nuevo ENTERAS, letra por letra: la voz asiente con una palabra porque las recibe, no un resumen ({Diferencia(alCambiarDeModoMedido + aprendiz, alAprendiz)})");
        Debe(Nodo(anadidos[0], "delegation_id") is { ValueKind: JsonValueKind.Null },
            "con delegation_id nulo, que es la forma que el servidor aceptó (session.instructions.appended)");

        var aNormal = Cambio(completas)!;
        var vuelta = aNormal.Where(x => Campo(x, "type") == "session.instructions.append").ToList();
        Debe(vuelta.Count == 1, $"volver al modo con el que abrió también le habla a la voz (salió: {Tipos(aNormal)})");
        if (vuelta.Count == 0) return;
        string alVolver = Campo(vuelta[0], "content");
        Debe(deLaVoz.Length > 0 && alVolver == alVolverMedido + deLaVoz,
            $"y le devuelve su persona de siempre, la misma con la que abrió la sesión, detrás del prefijo medido de la vuelta («VUELVES A TU MODO DE SIEMPRE…»), letra por letra ({Diferencia(alVolverMedido + deLaVoz, alVolver)})");
        Debe(!alVolver.Contains(completas, StringComparison.Ordinal),
            "y NO las instrucciones de operar: la voz no las lleva, y en un append no caben (tope de 500 fichas)");
        var delegado = aNormal.FirstOrDefault(x => Campo(x, "type") == "session.update");
        Debe(Campo(delegado, "session", "delegation", "responses", "instructions") == completas,
            "mientras el delegado sí recupera las suyas enteras");

        string realtime = string.Join("\n", CambioDeModo(new ProtocoloOpenAI(), aprendiz, utensilios, false) ?? new List<string>());
        Debe(!realtime.Contains("session.instructions.append"),
            "y GPT Realtime no lo necesita: su cambio de modo es la apertura reenviada, que ya cambia la voz");
    }

    /// <remarks>
    /// EL FALLO QUE ESTO IMPIDE: con GPT-Live por defecto, el panel de costos no recibía nada. El
    /// servidor no manda fichas: manda session.usage.updated con usage.seconds cada ~15 s, y nadie lo
    /// traducía — ReportarConsumo veía cero y salía sin una línea. Medido en las sondas del 2026-09-12
    /// (sonda-huecos r1G y r2c): 12.0 a los 15 s y 25.0 a los 30 s de la misma sesión. Es el ACUMULADO; quien
    /// lo sume como un incremento cuenta 37 s donde hubo 25.
    /// </remarks>
    private static void GptLiveCuentaLaDuracion()
    {
        var p = GptLive();
        if (p == null) { Pendiente("Voz.Realtime.ProtocoloGptLive", "1"); return; }
        var tipo = typeof(Hecho).GetNestedType("Duracion");
        var segundos = tipo?.GetProperty("Segundos");
        if (tipo == null || segundos == null) { Pendiente("Voz.Realtime.Hecho.Duracion (Segundos)", "1"); return; }

        List<double> Leidos(string json) => p.Leer(Mensaje(json))
            .Where(h => tipo.IsInstanceOfType(h)).Select(h => Convert.ToDouble(segundos.GetValue(h))).ToList();

        var a15 = Leidos("""{"type":"session.usage.updated","usage":{"seconds":12.0},"context_window":{"usage_ratio":0.0102343750},"event_id":"event_ENOzZAVHSZZ5pO7sNDsal"}""");
        Debe(a15.Count == 1 && a15[0] == 12.0,
            $"session.usage.updated es UN Hecho.Duracion con los segundos que trae (salieron {a15.Count}: {string.Join(", ", a15)})");
        var a30 = Leidos("""{"type":"session.usage.updated","usage":{"seconds":25.0},"context_window":{"usage_ratio":0.0153984375},"event_id":"event_ENOzotrjrxl5LZz8nedUQ"}""");
        Debe(a30.Count == 1 && a30[0] == 25.0,
            $"y el siguiente de la misma sesión trae 25, no los 13 de diferencia: es el acumulado y se entrega tal cual (salió {string.Join(", ", a30)})");
        var entero = Leidos("""{"type":"session.usage.updated","usage":{"seconds":7}}""");
        Debe(entero.Count == 1 && entero[0] == 7.0, "un número sin decimales también son segundos");

        Debe(Leidos("""{"type":"session.usage.updated","context_window":{"usage_ratio":0.01}}""").Count == 0
             && Leidos("""{"type":"session.usage.updated","usage":{"seconds":"12"}}""").Count == 0
             && Leidos("""{"type":"session.usage.updated","usage":{}}""").Count == 0,
            "un uso sin segundos numéricos no inventa duración: vacío no es cero");
        Debe(Leidos("""{"type":"session.output_transcript.delta","delta":"Hola"}""").Count == 0,
            "y la duración solo sale del mensaje de uso");
    }

    /// <remarks>
    /// MEDIDO el 2026-09-12 contra /v1/live/sessions con los bytes exactos de la rama (catálogo real, 23
    /// herramientas, un session.start de 43.507 B que el servidor aceptó). Tres rechazos, ninguno cierra nada:
    ///
    ///  · UNA FOTO DE PANTALLA NO CABE. Un Fotograma de 117.962 B (JPEG 1024×576 a calidad 60; la captura real
    ///    de esta máquina pesa 67–69 KB, 90–92 KB en base64) contestó response_input_buffer_full «Backend
    ///    response input history is limited to 128 items and 32768 UTF-8 bytes per session», y el delegado
    ///    dijo «No puedo distinguir el texto del recuadro blanco con suficiente claridad», como si la hubiera
    ///    visto borrosa. Una de 520 px (32.146 B) se leyó; de tres de 400 px (20.208 B) en la misma sesión,
    ///    la 1ª se leyó y la 2ª y la 3ª dieron el mismo error. Achicar la foto no basta.
    ///  · UN RESULTADO DE 40 KB TAMPOCO (un mensaje de 41.084 B): el mismo error y, en el mismo milisegundo,
    ///    function_call_outputs_required — la llamada queda pendiente y cada response.create de la sesión
    ///    falla. Ocho resultados de 17.741 B pasaron en una misma sesión: los de texto no se acumulan.
    ///  · UN ERROR ANTES DE session.started ES DE NO HABER ABIERTO: credit_balance_exhausted llegó sin
    ///    session.started y a los ~2,0 s el socket quedó Aborted (medido dos veces, la última a 604 ms y
    ///    2.598 ms); «Instructions must not exceed 16384 tokens» hizo lo mismo en la sonda de huecos. Sin
    ///    saber qué es abrir, la conversación lo tomaba por un corte y reenviaba el mismo session.start.
    /// </remarks>
    private static void GptLiveNoMandaLoQueSeRechaza()
    {
        var p = GptLive();
        if (p == null) { Pendiente("Voz.Realtime.ProtocoloGptLive", "1"); return; }

        Debe(!p.Mira,
            "GPT-Live no se declara capaz de mirar: una captura de pantalla da response_input_buffer_full, y una segunda foto pequeña también");

        const int tope = 32_768;
        int Bytes(string s) => System.Text.Encoding.UTF8.GetByteCount(s);
        const string marca = "…[recortado: ";

        // UN RESULTADO DE 40 KB, como el que el servidor rechazó.
        string grande = "Estas en SAP Easy Access (saplogon.exe). Salidas conocidas desde aqui: NWP1 Gestion de pacientes. "
            .PadRight(40 * 1024, '.');
        var deGrande = p.Resultados(new List<(string Id, string Nombre, string Resultado)> { ("call_grande", "map_where_am_i", grande) }).ToList();
        Debe(deGrande.Count == 1, $"un resultado grande sigue siendo UN mensaje (salieron {deGrande.Count})");
        if (deGrande.Count == 1)
        {
            int enviados = Bytes(deGrande[0]);
            Debe(enviados <= tope, $"un resultado de 40 KB sale en un mensaje de {tope} bytes o menos (salió de {enviados})");
            Debe(enviados > tope - 16, $"y aprovecha el tope: no recorta de más (salió de {enviados})");
            var m = Mensaje(deGrande[0]);
            Debe(Campo(m, "type") == "response.item.create" && Campo(m, "item", "type") == "function_call_output"
                 && Campo(m, "item", "call_id") == "call_grande",
                "como function_call_output con su call_id: la llamada no queda pendiente");
            string salida = Campo(m, "item", "output");
            int corte = salida.LastIndexOf(marca, StringComparison.Ordinal);
            string guardado = corte > 0 ? salida[..corte] : "";
            Debe(corte > 0 && grande.StartsWith(guardado, StringComparison.Ordinal)
                 && salida.EndsWith($"{marca}{Bytes(guardado)} de {Bytes(grande)} bytes]", StringComparison.Ordinal),
                $"con el principio del resultado y diciendo cuánto se mandó de cuánto (termina en «{(salida.Length > 60 ? salida[^60..] : salida)}»)");
        }

        // SIN PARTIR UN CARÁCTER: con emojis, cada uno son dos char, y un corte impar deja medio.
        string emojis = string.Concat(Enumerable.Repeat("😀", 6000));
        var deEmojis = p.Resultados(new List<(string Id, string Nombre, string Resultado)> { ("call_emoji", "map_what_i_see", emojis) }).ToList();
        if (deEmojis.Count == 1)
        {
            int enviados = Bytes(deEmojis[0]);
            string salida = Campo(Mensaje(deEmojis[0]), "item", "output");
            int corte = salida.LastIndexOf(marca, StringComparison.Ordinal);
            string guardado = corte > 0 ? salida[..corte] : "";
            Debe(enviados <= tope && corte > 0 && guardado.Length > 0 && guardado.Length % 2 == 0
                 && emojis.StartsWith(guardado, StringComparison.Ordinal),
                $"sin partir un carácter: lo que se manda son emojis enteros (mensaje de {enviados} B, {guardado.Length} char guardados)");
        }
        else Debe(false, $"un resultado con emojis sigue siendo UN mensaje (salieron {deEmojis.Count})");

        // Y LO QUE CABE VA ENTERO: 17 KB con acentos y comillas, del tamaño de los que el servidor aceptó ocho veces.
        string mediano = string.Concat(Enumerable.Repeat("Estás en «SAP Easy Access»; puertas: NWP1 Gestión de pacientes, NV2000 Admisión. ", 150));
        var deMediano = p.Resultados(new List<(string Id, string Nombre, string Resultado)> { ("call_mediano", "map_where_am_i", mediano) }).ToList();
        Debe(deMediano.Count == 1 && Bytes(deMediano[0]) <= tope && Campo(Mensaje(deMediano[0]), "item", "output") == mediano,
            $"y un resultado que cabe va entero, sin marca de recorte ({(deMediano.Count == 1 ? Bytes(deMediano[0]) : 0)} B)");

        // LA APERTURA LA CONFIRMA EL SERVIDOR, no el socket.
        var confirma = DeLaInterfaz("ConfirmaQueAbrio", p);
        var tAbierta = typeof(Hecho).GetNestedType("Abierta");
        if (confirma == null || tAbierta == null) { Pendiente("IProtocolo.ConfirmaQueAbrio / Hecho.Abierta", "018·49"); return; }
        Debe(confirma is true, "GPT-Live declara que confirma la apertura: hasta session.started la sesión no está abierta");
        Debe(DeLaInterfaz("ConfirmaQueAbrio", new ProtocoloOpenAI()) is false,
            "y GPT Realtime, que no se juzgó así, no lo declara: con él un error se sigue leyendo como hasta ahora");

        var abre = p.Leer(Mensaje("""{"type":"session.started","session":{"id":"live_u2_ENOy6GhblDeLrMlDOGSX1","model":"gpt-live-1","status":"active","input":[]}}"""));
        Debe(abre.Count == 1 && abre[0].GetType() == tAbierta, $"session.started es UN Hecho.Abierta (salieron {abre.Count})");

        var sinCredito = p.Leer(Mensaje("""
            {"type":"error","event_id":"event_7f0763e4-314d-4930-9bfa-eb831d673918","error":{"type":"invalid_request_error","code":"credit_balance_exhausted","message":"You have no credits remaining. Add credits to continue using the API at https://platform.openai.com/settings/organization/billing/."}}
            """));
        Debe(sinCredito.Count == 1 && sinCredito[0] is Hecho.Falla fc && fc.Que.Contains("You have no credits remaining"),
            "el error que llega en vez de session.started sigue siendo UN Hecho.Falla con su mensaje, no una apertura");

        string[] otros =
        {
            """{"event_id":"event_ENQAm4enOKNwSSblCsgzt","type":"session.updated","session":{"id":"live_u2_ENQAkzfUN8f6pBngGHwcm","expires_at":1789258059,"model":"gpt-live-1","status":"active"}}""",
            """{"type":"session.delegation.created","offset_ms":0,"delegation":{"id":"item_ENQ9ycRoNI3i5l8qMNGhG","type":"delegation","response_id":"resp_061ec1834255794e006aa5ccfa32f487d186a28557e1e4cecd","target":"responses"},"event_id":"event_ENQ9ypk910OIS296dUTWS"}""",
            """{"type":"error","event_id":"event_ENQ9zsNQ1Zl59krrM4MFC","error":{"type":"invalid_request_error","code":"response_input_buffer_full","message":"Backend response input history is limited to 128 items and 32768 UTF-8 bytes per session.","param":"item"}}""",
            """{"type":"response.event","delegation_id":"item_ENOyA2AApz1ePY8UFCDIw","event":{"type":"response.completed"}}""",
            """{"type":"session.usage.updated","usage":{"seconds":12.0},"context_window":{"usage_ratio":0.01003125}}""",
            """{"event_id":"event_ENOyNXoVEuKQV2BXwf0YH","type":"session.closed","reason":"close_requested","usage":{"seconds":13.0},"client_event_id":"sonda_fin"}""",
        };
        Debe(!otros.SelectMany(x => p.Leer(Mensaje(x))).Concat(sinCredito).Any(h => h.GetType() == tAbierta),
            "y ningún otro mensaje es una apertura: ni session.updated, ni una delegación, ni un error, ni el cierre");
    }

    /// <remarks>
    /// MEDIDO el 2026-09-13 contra el servidor, con .NET 8 y el mismo ClientWebSocket de la app (sonda-fatal, fuera del
    /// repo). Los cuatro mensajes van copiados de lo que contestó; la clave falsa sale enmascarada por el propio servidor:
    ///
    ///  · GPT-Live sin crédito (2026-09-12): error credit_balance_exhausted en vez de session.started.
    ///  · GPT-Live con un modelo que no existe: error invalid_model a 262 ms, y el socket Aborted a los ~2 s.
    ///  · GPT Realtime con una clave falsa: el apretón de manos PASA (101), llega error invalid_api_key y el servidor
    ///    cierra con 3000 «invalid_request_error.invalid_api_key».
    ///  · GPT Realtime con un modelo que no existe en la dirección: error model_not_found y cierre 4004
    ///    «invalid_request_error.model_not_found».
    ///
    /// EL CÓDIGO Y NO EL TYPE: los cuatro traen type invalid_request_error, que es de todos los errores —también de
    /// response_input_buffer_full, que no es fatal—. Un traductor que lo usara como código cuando falta el code
    /// inventaría una causa.
    /// </remarks>
    private static void LosErroresDicenSuCodigo()
    {
        var live = GptLive();
        if (live == null) { Pendiente("Voz.Realtime.ProtocoloGptLive", "1"); return; }
        var codigo = typeof(Hecho.Falla).GetProperty("Codigo");
        if (codigo == null) { Pendiente("Hecho.Falla.Codigo", "018·53"); return; }
        var realtime = new ProtocoloOpenAI();

        var casos = new (IProtocolo P, string Quien, string Json, string Code, string Mensaje)[]
        {
            (live, "GPT-Live", """{"type":"error","event_id":"event_7f0763e4-314d-4930-9bfa-eb831d673918","error":{"type":"invalid_request_error","code":"credit_balance_exhausted","message":"You have no credits remaining. Add credits to continue using the API at https://platform.openai.com/settings/organization/billing/."}}""",
                "credit_balance_exhausted", "You have no credits remaining"),
            (live, "GPT-Live", """{"type":"error","event_id":"event_d7252ece-60b5-4b34-88a4-30b46a4124f4","error":{"type":"invalid_request_error","code":"invalid_model","message":"Model \"gpt-live-inexistente-9\" is not supported in realtime mode."}}""",
                "invalid_model", "is not supported in realtime mode"),
            (realtime, "GPT Realtime", """{"type":"error","event_id":"event_ENgrrx8v946dIyk6vQTOj","error":{"type":"invalid_request_error","code":"invalid_api_key","message":"Incorrect API key provided: sk-proj-**************************************************0000. You can find your API key at https://platform.openai.com/account/api-keys.","param":null,"event_id":null}}""",
                "invalid_api_key", "Incorrect API key provided"),
            (realtime, "GPT Realtime", """{"type":"error","event_id":"event_ENgs3fIvSRtnqIx8JCGfh","error":{"type":"invalid_request_error","code":"model_not_found","message":"The model `gpt-realtime-inexistente-9` does not exist or you do not have access to it.","param":null,"event_id":null}}""",
                "model_not_found", "does not exist or you do not have access to it"),
        };
        foreach (var (p, quien, json, code, mensaje) in casos)
        {
            var h = p.Leer(Mensaje(json));
            string salio = string.Join(" · ", h.Select(x => x is Hecho.Falla f ? $"Falla(«{f.Que}», código «{codigo.GetValue(f)}»)" : x.GetType().Name));
            Debe(h.Count == 1 && h[0] is Hecho.Falla falla && falla.Que.Contains(mensaje) && (codigo.GetValue(falla) as string) == code,
                $"con {quien}, el error {code} es UN Hecho.Falla con su message y con el código «{code}» (salió: {salio})");
        }

        // SIN CODE NO HAY CÓDIGO: ni el type, que comparten todos, ni el message.
        const string sinCode = """{"type":"error","event_id":"event_sin_code","error":{"type":"invalid_request_error","message":"Algo que el servidor no clasificó."}}""";
        foreach (var (p, quien) in new (IProtocolo, string)[] { (live, "GPT-Live"), (realtime, "GPT Realtime") })
        {
            var h = p.Leer(Mensaje(sinCode));
            Debe(h.Count == 1 && h[0] is Hecho.Falla f && f.Que.Contains("Algo que el servidor no clasificó.") && (codigo.GetValue(f) as string) == "",
                $"con {quien}, un error sin code es un Hecho.Falla con su message y el código vacío: no se inventa con el type (salió: "
                + string.Join(" · ", h.Select(x => x is Hecho.Falla ff ? $"código «{codigo.GetValue(ff)}»" : x.GetType().Name)) + ")");
        }
        var cerrada = live.Leer(Mensaje("""{"event_id":"event_ENOyNXoVEuKQV2BXwf0YH","type":"session.closed","reason":"close_requested","usage":{"seconds":13.0},"client_event_id":"sonda_fin"}"""));
        Debe(cerrada.Count == 1 && cerrada[0] is Hecho.Falla fc && (codigo.GetValue(fc) as string) == "",
            "y la sesión que el servidor cierra sigue siendo una Falla sin código: su motivo no es un code");
    }

    // ── El arnés ─────────────────────────────────────────────────────────────

    private static MethodInfo? Estatico(string tipo, string metodo)
        => Ensamblado.GetType("Omi." + tipo)?.GetMethod(metodo, BindingFlags.Public | BindingFlags.Static);

    private static object? Propiedad(Type tipo, string nombre, object? instancia = null)
        => tipo.GetProperty(nombre)?.GetValue(instancia);

    private static void Prueba(string nombre, Action cuerpo)
    {
        int antes = _fallos;
        try { cuerpo(); }
        catch (Exception e)
        {
            _fallos++;
            // LA CADENA ENTERA. Un TargetInvocationException dice «una excepción durante la
            // invocación» y se guarda para sí POR QUÉ, que es lo único que sirve (aprendizaje nº3).
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

    /// <summary>
    /// La capacidad todavía no existe. Cuenta como INCUMPLIDA, y se dice en qué fase llega para que
    /// el rojo del desarrollo no se confunda con una regresión.
    /// </summary>
    private static void Pendiente(string capacidad, string fase)
    {
        _fallos++;
        _pendientes++;
        Console.WriteLine($"   ⧗ PENDIENTE: «{capacidad}» todavía no existe (fase {fase} del plan). "
            + "La promesa está escrita y en rojo, que es donde tiene que estar.");
    }
}
