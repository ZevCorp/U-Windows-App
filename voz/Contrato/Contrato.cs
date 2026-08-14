using System.Reflection;

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
