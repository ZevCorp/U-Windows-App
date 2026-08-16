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

    private let reconocedor = SFSpeechRecognizer(locale: Locale(identifier: "es-MX"))
    private let motor = AVAudioEngine()
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

    /// Modo conversación: el micrófono se vuelve a abrir solo después de cada respuesta.
    ///
    /// Es lo que separa un compañero de una herramienta. Tener que tocarlo antes de cada frase
    /// convierte cada intercambio en un trámite, y a los tres trámites ya nadie le habla.
    var continuo = true

    init(cara: FaceView) { self.cara = cara }

    /// Vuelve a abrir el micrófono, si no estaba ya abierto. A diferencia de `escuchar()`, esto
    /// NUNCA apaga: es para que la conversación siga sola, no para alternar.
    func reanudar() {
        guard continuo, !escuchando else { return }
        escuchar()
    }

    /// Pide los permisos la primera vez y arranca.
    ///
    /// SON DOS PERMISOS, no uno, y es el error fácil: «reconocimiento de voz» deja entender el audio,
    /// pero el MICRÓFONO es otro permiso aparte. Con el primero concedido y el segundo no, el motor
    /// de audio arranca sin quejarse y entrega puro silencio — la carita se queda escuchando para
    /// siempre y no contesta nunca, sin un solo error por ninguna parte.
    func escuchar() {
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
        guard let reconocedor, reconocedor.isAvailable else {
            Registro.di("👂 ✘ el reconocedor es-MX no está disponible")
            alFallar?("El reconocimiento de voz en español no está disponible en este Mac.")
            return
        }

        let p = SFSpeechAudioBufferRecognitionRequest()
        p.shouldReportPartialResults = true
        peticion = p

        let entrada = motor.inputNode
        let formato = entrada.outputFormat(forBus: 0)
        entrada.removeTap(onBus: 0)
        entrada.installTap(onBus: 0, bufferSize: 1024, format: formato) { buffer, _ in
            p.append(buffer)
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
        generacion += 1
        let mia = generacion
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
                self.parar()
            }
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
    }

    /// Enciende el modo conversación y abre el micrófono.
    func ponerseAOir() {
        continuo = true
        Registro.di("👂 modo conversación ENCENDIDO")
        reanudar()
    }
}
