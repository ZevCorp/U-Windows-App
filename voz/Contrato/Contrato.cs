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
