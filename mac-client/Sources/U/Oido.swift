import AppKit
import AVFoundation
import Speech

/// El oído de Ü: convierte lo que dices en texto.
///
/// Usa el reconocimiento del propio Mac y no un servicio de red, por dos razones que importan al
/// producto: no hay que esperar a que suba el audio —empieza a entender mientras hablas— y lo que
/// dices no sale de tu máquina hasta que hay una tarea que ejecutar.
///
/// Deja de escuchar sola después de un silencio: obligar a pulsar algo para terminar de hablar
/// rompe la ilusión de estar hablando con alguien.
final class Oido {

    /// RECIÉN HECHO EN CADA APERTURA. Era un `let` de toda la vida de la app, y un `SFSpeechRecognizer`
    /// que ya vio morir su tarea sigue diciendo `isAvailable == true` y aceptando tareas nuevas que no
    /// transcriben NADA. Se vio el 2026-08-19 después de cada sesión en vivo: el audio entraba —el
    /// vigía dejaba de quejarse— y aun así no salía una sola palabra.
    private var reconocedor = SFSpeechRecognizer(locale: Locale(identifier: "es-MX"))
    /// MOTOR NUEVO EN CADA APERTURA — y no vale sin la línea que lo acompaña en `arrancar()`.
    ///
    /// La sonda del 2026-08-19 (`U_SONDA=1`) demostró que el aparato NO se ensucia: encender la
    /// cancelación de eco, apagarla, y volver a capturar y transcribir funciona perfecto —30 búferes,
    /// pico 0.10, «123456» transcrito—. O sea que la sordera era de la app, no del micrófono.
    ///
    /// Lo que la sonda hace y aquí no se hacía son DOS cosas a la vez, y ese es el detalle que costó
    /// tres intentos fallidos: motor recién creado **Y** `setVoiceProcessingEnabled(false)` sobre su
    /// entrada antes de leer el formato. Por separado, ninguna sirve:
    ///
    ///   · motor nuevo solo          → la entrada aparece con 3 canales y el tap queda mudo
    ///   · apagar el eco solo        → entra audio pero el reconocedor no transcribe
    ///   · las dos juntas            → funciona
    private var motor = AVAudioEngine()
    private var peticion: SFSpeechAudioBufferRecognitionRequest?
    private var tarea: SFSpeechRecognitionTask?
    private var cortaPorSilencio: DispatchWorkItem?

    /// Qué número de escucha va. Sube en cada arranque y en cada parada.
    ///
    /// EL BUG QUE ARREGLA, porque no es obvio y costó una tarde: al cerrar una escucha se cancela su
    /// tarea de reconocimiento, y esa cancelación NO es inmediata — vuelve como un error 216 unos
    /// milisegundos después. Para entonces ya se había abierto la escucha siguiente, y el aviso
    /// atrasado de la anterior llamaba a `parar()`… matando a la nueva. Resultado: oía la primera
    /// frase y se quedaba muda para siempre, sin un solo error a la vista. Con esto, cada tarea sabe
    /// de qué escucha es y las viejas no pueden tocar a las vivas.
    private var generacion = 0

    /// Los permisos se piden UNA vez. Volver a pedirlos en cada frase mete un salto entre hilos que
    /// retrasa la reapertura del micrófono justo cuando hay que ser rápido.
    private var permisosDados = false

    private weak var cara: FaceView?

    /// Lo que va entendiendo, mientras lo dice.
    var alOir: ((String) -> Void)?
    /// Lo que entendió del todo, cuando se calla.
    var alEntender: ((String) -> Void)?
    /// Algo salió mal (sin permiso, sin micrófono, sin idioma).
    var alFallar: ((String) -> Void)?

    /// Cuánto silencio hay que dejar para que dé por terminada la frase.
    ///
    /// Medio segundo y no uno y medio. Este número se paga ENTERO en cada turno y antes de que el
    /// modelo haya empezado siquiera a pensar: es puro tiempo muerto mirando una cara que no hace
    /// nada. Bajarlo de 1,4 a 0,55 le quitó casi un segundo a cada respuesta sin que se note al
    /// hablar, porque las pausas dentro de una frase hablada son más cortas que eso.
    private let silencioParaCortar: TimeInterval = 0.55

    private(set) var escuchando = false

    /// Alguien le está hablando AHORA MISMO: hay palabras entrando, no solo el micrófono abierto.
    ///
    /// La distinción es la que hace que la cara de `escuchando` sirva de algo. «Micrófono abierto»
    /// lo está siempre —está razonado en `arrancar()`— y por eso no vale como estado: sería una cara
    /// fija. Esto dura lo que dura tu frase, que es exactamente para lo que se dibujó.
    private(set) var hayVoz = false

    /// Algo cambió en el oído (abrió, cerró, empezó a oírte). Quien lleva la cara lo necesita para
    /// volver a derivarla: el oído sigue sin tocarla, solo avisa de que hay algo nuevo que mirar.
    var alCambiar: (() -> Void)?

    /// Modo conversación: el micrófono se vuelve a abrir solo después de cada respuesta.
    ///
    /// Es lo que separa un compañero de una herramienta. Tener que tocarlo antes de cada frase
    /// convierte cada intercambio en un trámite, y a los tres trámites ya nadie le habla.
    var continuo = true

    init(cara: FaceView) { self.cara = cara }

    /// Vuelve a abrir el micrófono, si no estaba ya abierto. A diferencia de `escuchar()`, esto
    /// NUNCA apaga: es para que la conversación siga sola, no para alternar.
    /// Ü está hablando por el altavoz AHORA MISMO. Lo pone quien maneja la voz.
    ///
    /// Sin esto, el oído se reabría mientras ella hablaba y **se transcribía a sí misma**: el
    /// 2026-08-19 dijo «las nutrias se agarran de las manos mientras duermen», se oyó «las frutas se
    /// agarran de las manos», se lo preguntó al modelo, y el modelo contestó «creo que se te cruzaron
    /// los cables con las nutrias». Un bucle de realimentación entero, cobrado, y desde fuera se ve
    /// como que va lenta y habla cortado.
    ///
    /// No es un detector por volumen —eso está prohibido y con razón—: es un hecho que sabemos, no
    /// algo que se infiera del audio.
    private(set) var mudaPorqueHabla = false

    func mientrasHabla(_ hablando: Bool) {
        mudaPorqueHabla = hablando
        if hablando { parar() } else { reanudar() }
    }

    func reanudar() {
        guard continuo, !cedido, !escuchando else { return }
        guard !mudaPorqueHabla else { Registro.di("👂 no abro: estoy hablando"); return }
        escuchar()
    }

    /// El oído le CEDIÓ el micrófono a la conversación en vivo.
    ///
    /// Distinto de `callarse()`, y la diferencia importa para la cara: callarse es «me apagaste tú»
    /// y se dibuja `detenido`; ceder es «hay otra boca usando el micrófono» y la carita sigue en
    /// conversación. Hace falta la bandera porque `reanudar()` se llama desde cinco sitios y
    /// cualquiera de ellos reabriría el micrófono por debajo de la sesión viva — y dos motores de
    /// audio sobre el mismo micrófono no se turnan, se pisan.
    private(set) var cedido = false

    func ceder() {
        guard !cedido else { return }
        cedido = true
        parar()
        Registro.di("👂 le cedo el micrófono a la voz en vivo")
    }

    /// Le da al aparato un respiro antes de volver a tomarlo. Reabrir 50 ms después de que la voz
    /// viva soltara el micrófono era justo lo que dejaba el tap mudo (ver `vigilarQueLlegueAudio`).
    private static let respiroTrasCeder = 0.8

    func recuperar() {
        guard cedido else { return }
        cedido = false
        Registro.di("👂 recupero el micrófono (espero \(Self.respiroTrasCeder)s a que el aparato quede libre)")
        DispatchQueue.main.asyncAfter(deadline: .now() + Self.respiroTrasCeder) { [weak self] in
            self?.reanudar()
        }
    }

    /// Pide los permisos la primera vez y arranca.
    ///
    /// SON DOS PERMISOS, no uno, y es el error fácil: «reconocimiento de voz» deja entender el audio,
    /// pero el MICRÓFONO es otro permiso aparte. Con el primero concedido y el segundo no, el motor
    /// de audio arranca sin quejarse y entrega puro silencio — la carita se queda escuchando para
    /// siempre y no contesta nunca, sin un solo error por ninguna parte.
    func escuchar() {
        // LA CESIÓN SE COMPRUEBA AQUÍ TAMBIÉN, y no solo en `reanudar()`. Estaba solo allí, y el
        // camino que la saltaba era justo el que corre al arrancar: se piden los permisos, el usuario
        // concede, y el callback llama a `arrancar()` por debajo — sin pasar por `reanudar()`. En el
        // log del 2026-08-18 se vio entero: «le cedo el micrófono a la voz en vivo» y catorce
        // segundos después «escuchando (44100 Hz, 2 canales)», con la sesión viva ya abierta a
        // 48000. Dos motores de audio sobre el mismo micrófono.
        guard !cedido else { Registro.di("👂 no abro: el micrófono lo tiene la voz en vivo"); return }
        guard !escuchando else { Registro.di("👂 ya estaba escuchando → paro"); parar(); return }

        if permisosDados { arrancar(); return }
        Registro.di("👂 pido permisos (solo la primera vez)")

        AVCaptureDevice.requestAccess(for: .audio) { [weak self] micOk in
            guard let self else { return }
            Registro.di("👂 micrófono: \(micOk ? "concedido" : "DENEGADO")")
            guard micOk else {
                DispatchQueue.main.async {
                    self.alFallar?("Necesito permiso del micrófono. Está en Ajustes del Sistema, Privacidad y seguridad, Micrófono.")
                }
                return
            }
            SFSpeechRecognizer.requestAuthorization { estado in
                DispatchQueue.main.async {
                    Registro.di("👂 reconocimiento de voz: \(estado.rawValue) (3 = concedido)")
                    guard estado == .authorized else {
                        self.alFallar?("Necesito permiso para reconocer lo que dices. Está en Ajustes del Sistema, Privacidad y seguridad, Reconocimiento de voz.")
                        return
                    }
                    self.permisosDados = true
                    self.arrancar()
                }
            }
        }
    }

    private func arrancar() {
        // El mismo portazo que en `escuchar()`, y hace falta porque el camino de los permisos entra
        // POR AQUÍ: el callback de «micrófono concedido» llama directo, sin pasar por arriba.
        guard !cedido else { Registro.di("👂 no arranco: el micrófono lo tiene la voz en vivo"); return }
        guard !mudaPorqueHabla else { Registro.di("👂 no arranco: estoy hablando"); return }
        reconocedor = SFSpeechRecognizer(locale: Locale(identifier: "es-MX"))
        guard let reconocedor, reconocedor.isAvailable else {
            Registro.di("👂 ✘ el reconocedor es-MX no está disponible")
            alFallar?("El reconocimiento de voz en español no está disponible en este Mac.")
            return
        }

        let p = SFSpeechAudioBufferRecognitionRequest()
        p.shouldReportPartialResults = true
        peticion = p

        // El motor viejo, a la basura: ver el comentario de `motor`. Va JUNTO con apagar la
        // cancelación de eco de la línea siguiente; una sin la otra no arregla nada.
        if motor.isRunning { motor.stop() }
        motor = AVAudioEngine()

        let entrada = motor.inputNode
        // APAGAR LA CANCELACIÓN DE ECO ANTES DE NADA, y es la causa de raíz de que Ü se quedara
        // sorda después de cada conversación en vivo.
        //
        // `AudioVivo` la ENCIENDE sobre este mismo micrófono para no oírse a sí misma. Es un ajuste
        // del aparato, no del motor: cuando la sesión viva cierra, el nodo de entrada de aquí sigue
        // en modo procesado y el tap no entrega un solo búfer. Sale `👂 escuchando` y no se oye nada
        // NUNCA MÁS. Medido el 2026-08-19: despertar → sesión viva → dormirse → hablarle = silencio.
        //
        // Y un motor recién creado NO lo arregla: se probó y salió peor —el aparato aparecía con 3
        // canales y el tap seguía mudo—. Lo que hay que deshacer es el ajuste, no el motor.
        // CON EL MOTIVO A LA VISTA. Iba con `try?` y eso convirtió «falló al apagarlo» en «no hace
        // falta apagarlo»: la entrada seguía apareciendo con 3 canales y el tap mudo, sin una sola
        // línea que dijera por qué. Un catch mudo es el antipatrón nº3 del repo, cometido aquí mismo
        // mientras se arreglaba otra cosa.
        do {
            try entrada.setVoiceProcessingEnabled(false)
        } catch {
            Registro.di("👂 ✘ no pude apagar la cancelación de eco: \((error as NSError).domain) \((error as NSError).code) · \(error.localizedDescription)")
        }
        let formato = entrada.outputFormat(forBus: 0)
        entrada.removeTap(onBus: 0)
        trozosLlegados = 0
        entrada.installTap(onBus: 0, bufferSize: 1024, format: formato) { [weak self] buffer, _ in
            p.append(buffer)
            self?.trozosLlegados += 1
        }

        motor.prepare()
        do {
            try motor.start()
        } catch {
            Registro.di("👂 ✘ no arrancó el motor de audio: \(error)")
            alFallar?("No pude abrir el micrófono: \(error.localizedDescription)")
            return
        }

        escuchando = true
        alCambiar?()
        generacion += 1
        let mia = generacion
        vigilarQueLlegueAudio(mia)
        // El oído NO toca la cara. Con el micrófono abierto de continuo, «estoy escuchando» dejó de
        // ser un estado que valga la pena mostrar —lo está siempre— y lo que hay que mostrar es otra
        // cosa: si está dormida o en conversación. Eso lo sabe quien lleva la conversación, no el
        // micrófono, así que la cara la pone él.
        Registro.di("👂 escuchando (formato \(formato.sampleRate) Hz, \(formato.channelCount) canal/es)")

        tarea = reconocedor.recognitionTask(with: p) { [weak self] resultado, error in
            guard let self else { return }
            // El aviso de una escucha vieja no puede tocar a la que está viva.
            guard mia == self.generacion else { return }

            if let resultado {
                let texto = resultado.bestTranscription.formattedString
                Registro.di("👂 oigo: «\(texto)»")
                self.hayVoz = true
                self.alCambiar?()
                self.alOir?(texto)
                // Cada vez que sigue hablando se reinicia la cuenta del silencio.
                self.reprogramarCorte(con: texto)
            }
            if let error {
                // El 216 es la cancelación que provocamos nosotros al cerrar: no es un fallo.
                let codigo = (error as NSError).code
                if codigo != 216 { Registro.di("👂 ✘ error reconociendo: \(error.localizedDescription)") }
                self.parar()
                self.reanudar()          // que un tropiezo no la deje sorda el resto del día
            } else if resultado?.isFinal ?? false {
                // Y VUELVE A ARRANCAR. Aquí solo había `parar()`, y ese olvido es EL fallo de
                // estabilidad: `SFSpeechRecognizer` cierra la tarea por su cuenta —al minuto largo
                // de audio, o cuando decide que la frase acabó—, y entonces el oído se paraba y no
                // lo reabría nadie. Ü se quedaba sorda para el resto del día SIN UN SOLO RENGLÓN en
                // el registro: ni error, ni aviso, ni cara distinta. Desde fuera, idéntico a «no me
                // contesta». Medido el 2026-08-19: el registro se cortó a las 20:41:45 y no volvió a
                // escribir nada.
                //
                // La rama del error, tres líneas más arriba, sí reanudaba. Son las dos formas que
                // tiene una escucha de terminar y solo una sabía volver.
                Registro.di("👂 la escucha terminó sola — la reabro")
                self.parar()
                self.reanudar()
            }
        }
    }

    /// Cuántos búferes entraron por el tap desde que se armó esta escucha.
    private var trozosLlegados = 0
    private var reaperturasEnVano = 0

    /// EL VIGÍA: «arrancó el motor» NO ES «está entrando audio».
    ///
    /// `motor.start()` devuelve sin error y `👂 escuchando` se imprime igual cuando el dispositivo
    /// todavía lo tiene otro — y entonces el tap no entrega un solo búfer. El mensaje afirmaba una
    /// cosa que no había comprobado, que es el antipatrón nº2 del repo cometido en su propia casa.
    ///
    /// Se reproduce así, medido el 2026-08-19: se despierta a Ü, la sesión en vivo abre y toma el
    /// micrófono, a los 30 s se duerme y `recuperar()` reabre el oído 50 ms después de que el motor
    /// de la voz viva soltara el aparato. Sale `👂 escuchando` y no vuelve a oírse nada NUNCA. Desde
    /// fuera: «le hablo y no me contesta», con la app abierta y la carita en reposo.
    ///
    /// Comprobar en vez de suponer cuesta un temporizador.
    private func vigilarQueLlegueAudio(_ mia: Int) {
        DispatchQueue.main.asyncAfter(deadline: .now() + 2.5) { [weak self] in
            guard let self, mia == self.generacion, self.escuchando else { return }
            guard self.trozosLlegados == 0 else { self.reaperturasEnVano = 0; return }
            // (el contador vuelve a cero en cuanto UNA apertura trae audio; si no, tras rendirse una
            //  vez la app se quedaba rindiéndose para siempre aunque el micrófono ya estuviera libre)
            // CON TOPE. Un reintento sin tope es un bucle, y este repo ya pagó uno de 2.089 vueltas.
            // Si tres aperturas seguidas no traen audio, el problema no se arregla reabriendo: se
            // dice, con todas las letras, y se para.
            self.reaperturasEnVano += 1
            guard self.reaperturasEnVano < 3 else {
                Registro.di("👂 ✘✘ tres aperturas seguidas sin audio — me rindo. El micrófono se queda tomado por la sesión en vivo que acaba de cerrar; hay que reabrir la app.")
                self.parar()
                self.alFallar?("Me quedé sin micrófono. Ciérrame y ábreme otra vez.")
                return
            }
            Registro.di("👂 ✘ armé la escucha y no entró un solo búfer — el micrófono no es mío todavía, reabro (\(self.reaperturasEnVano)/3)")
            self.parar()
            // Un respiro antes de reintentar: si el aparato sigue ocupado, reabrir al instante vuelve
            // a fallar igual y se convierte en un bucle.
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.8) { [weak self] in self?.reanudar() }
        }
    }

    private func reprogramarCorte(con texto: String) {
        cortaPorSilencio?.cancel()
        let t = DispatchWorkItem { [weak self] in
            guard let self, self.escuchando else { return }
            self.parar()
            let limpio = texto.trimmingCharacters(in: .whitespacesAndNewlines)
            if !limpio.isEmpty { self.alEntender?(limpio) }
        }
        cortaPorSilencio = t
        DispatchQueue.main.asyncAfter(deadline: .now() + silencioParaCortar, execute: t)
    }

    func parar() {
        guard escuchando else { return }
        escuchando = false
        hayVoz = false
        alCambiar?()
        generacion += 1          // lo que llegue de aquí en adelante es de una escucha muerta
        cortaPorSilencio?.cancel(); cortaPorSilencio = nil
        motor.inputNode.removeTap(onBus: 0)
        if motor.isRunning { motor.stop() }
        peticion?.endAudio()
        tarea?.cancel()
        peticion = nil; tarea = nil
    }

    /// Apaga el modo conversación y cierra el micrófono.
    func callarse() {
        continuo = false
        parar()
        Registro.di("👂 modo conversación APAGADO")
        alCambiar?()
    }

    /// Enciende el modo conversación y abre el micrófono.
    func ponerseAOir() {
        continuo = true
        Registro.di("👂 modo conversación ENCENDIDO")
        alCambiar?()
        reanudar()
    }
}
