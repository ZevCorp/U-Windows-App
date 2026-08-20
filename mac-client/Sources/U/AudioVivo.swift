import AVFoundation

/// El micrófono y el altavoz de la conversación en vivo, en crudo.
///
/// Live API no habla de «frases»: es un caño de audio abierto en los dos sentidos. Eso lo separa de
/// `Oido`, que entrega UNA frase reconocida y se calla — allí el turno lo decide un temporizador de
/// silencio, aquí lo decide el modelo mientras te oye. Por eso hace falta PCM crudo y no un
/// reconocedor.
///
/// **Los dos ritmos son distintos a propósito y los fija Google: se ENVÍA a 16 kHz y se RECIBE a
/// 24 kHz, ambos mono y de 16 bits.** Mezclarlos suena a acelerado o a ralentizado, y ese es el
/// primer síntoma cuando algo va mal aquí.
final class AudioVivo {

    static let ritmoEntrada: Double = 16_000
    static let ritmoSalida: Double = 24_000

    /// UN SOLO MOTOR para entrada y salida, y no es organización: es lo que hace posible la
    /// cancelación de eco.
    ///
    /// `setVoiceProcessingEnabled` cancela de la entrada lo que sale por la salida — necesita ver
    /// las dos. Con dos motores separados no hay nada que cancelar, y entonces Ü se oye a sí misma,
    /// se transcribe, y se contesta. Con el micrófono siempre abierto eso no es un tropiezo: es un
    /// bucle que no para y que cuesta dinero en cada vuelta.
    private let motor = AVAudioEngine()
    private let reproductor = AVAudioPlayerNode()

    private var conversor: AVAudioConverter?
    /// De los 24 kHz que manda el modelo al ritmo al que de verdad va la mezcla.
    private var conversorSalida: AVAudioConverter?
    private var formatoDelModelo: AVAudioFormat?
    private var formatoReproductor: AVAudioFormat?

    /// Un trozo de micrófono, ya en el formato que espera el modelo.
    var alCapturar: ((Data) -> Void)?

    private(set) var abierto = false

    /// Cuenta y nivel de lo que ENTRA, para poder ver si el micrófono da algo.
    ///
    /// Existe porque el 2026-08-18 hubo once sesiones abiertas y CERO transcripciones de lo dicho: el
    /// socket, el altavoz y la cara funcionaban, y la única pieza sin comprobar —el micrófono— era la
    /// que fallaba. «Abrió» y «oye» son cosas distintas y se estaban confundiendo.
    private var trozos = 0
    private var picoEntrada: Double = 0
    private var ultimoAviso = Date.distantPast

    // ── Cuánto estamos sonando ───────────────────────────────────────────────────────────────────

    /// Trozos entregados al altavoz que todavía no han terminado de sonar.
    private var enCola = 0
    private var pico: Double = 0
    private let candado = NSLock()

    /// SONAR NO ES RECIBIR. El modelo manda el audio mucho más rápido de lo que se oye: una frase de
    /// cinco segundos llega en menos de uno y se queda en la cola. Medir «¿llegó algo hace poco?»
    /// daría cero a los 400 ms con el altavoz todavía hablando — o sea, cero justo cuando hace
    /// falta. Mientras quede cola estamos sonando.
    var hablando: Bool { candado.lock(); defer { candado.unlock() }; return enCola > 0 }

    /// Lo fuerte que suena Ü ahora mismo (0–1). **La boca de la carita se mueve con esto**, y por eso
    /// es mejor que lo que había: la boca deja de inventarse la forma a partir de la letra que toca y
    /// pasa a seguir el sonido de verdad.
    var nivelSalida: Double {
        candado.lock(); defer { candado.unlock() }
        return enCola > 0 ? pico : 0
    }

    // ── Abrir y cerrar ───────────────────────────────────────────────────────────────────────────

    func abrir() throws {
        guard !abierto else { return }
        do {
            try intentar(conCancelacionDeEco: true)
        } catch {
            // SE INTENTA IGUAL, SIN CANCELACIÓN, Y SE DICE. Quedarse sin voz porque el procesado de
            // voz no arrancó es cambiar un problema (se oye a sí misma) por otro peor (no hay
            // conversación). Degradada y avisando es mejor que muerta y callada.
            Registro.di("🎙 ⚠︎ el procesado de voz no arrancó (\(error.localizedDescription)) — voy sin cancelación de eco")
            try intentar(conCancelacionDeEco: false)
            Registro.di("🎙 ⚠︎ SIN cancelación: si empieza a contestarse sola, es esto")
        }
    }

    private func intentar(conCancelacionDeEco conEco: Bool) throws {
        let entrada = motor.inputNode

        // LA CANCELACIÓN DE ECO, que es lo que permite INTERRUMPIRLA.
        //
        // La otra salida —cerrar el micrófono mientras habla, que es lo que hace `Oido`— mata justo
        // aquello por lo que existe esta clase: hablar encima de ella. En Windows se intentó el
        // camino de en medio (un umbral por volumen que ignorase el eco) y estuvo puesto del
        // 2026-08-04 al 2026-08-16: hacía imposible interrumpir, porque interrumpir es hablar
        // MIENTRAS ella habla, que es cuando el umbral estaba más alto. Se retiró. Aquí se va
        // directo a la cancelación del sistema.
        // SOLO EN LA ENTRADA. Encenderlo también en la salida reventaba el arranque:
        //   Code=-10875 · failed call=err = PerformCommand(*outputNode, kAUInitialize, NULL, 0)
        // (2026-08-18, primer arranque en el Mac de Isabel). En Apple el procesado de voz es UNA
        // unidad de entrada/salida: encendiéndolo en la entrada, la salida entra en el mismo trato.
        // Pedirlo dos veces no lo pone «más», lo reinicializa a media configuración.
        try entrada.setVoiceProcessingEnabled(conEco)
        if conEco {
            // LE PREGUNTAMOS A LA API QUÉ FORMATO QUIERE, en vez de deducirlo. Es la sonda barata del
            // aprendizaje nº13 del repo, aplicada a CoreAudio: veinte segundos de registro contra
            // otra ronda de teoría sobre por qué no inicializa.
            func f(_ x: AVAudioFormat) -> String { "\(Int(x.sampleRate))Hz/\(x.channelCount)ch" }
            Registro.di("🎙 eco: entrada \(entrada.isVoiceProcessingEnabled ? "sí" : "no")"
                      + " · salida \(motor.outputNode.isVoiceProcessingEnabled ? "sí" : "no")"
                      + " || in.in=\(f(entrada.inputFormat(forBus: 0)))"
                      + " in.out=\(f(entrada.outputFormat(forBus: 0)))"
                      + " out.in=\(f(motor.outputNode.inputFormat(forBus: 0)))"
                      + " out.out=\(f(motor.outputNode.outputFormat(forBus: 0)))")
            // AQUÍ SE MEDÍA TAMBIÉN `mainMixerNode.outputFormat`, y la medición ESTROPEABA lo medido:
            // nombrar el mezclador es lo que lo crea y lo cablea a la salida a 44 100 Hz. O sea que
            // la línea de diagnóstico puesta para averiguar por qué fallaba el arranque era una de
            // las causas de que fallara. Se quita, y no se vuelve a poner. (2026-08-18)
        }

        // El formato hay que leerlo DESPUÉS de encender el procesado de voz: encenderlo cambia el
        // formato del nodo, y quedarse con el de antes es convertir desde algo que ya no es.
        let formatoEntrada = entrada.outputFormat(forBus: 0)
        guard let destino = AVAudioFormat(commonFormat: .pcmFormatInt16,
                                          sampleRate: Self.ritmoEntrada,
                                          channels: 1, interleaved: true) else {
            throw Fallo.sinFormato
        }
        conversor = AVAudioConverter(from: formatoEntrada, to: destino)

        // EL MAPA DE CANALES, y sin él no se oye NADA — literalmente ceros.
        //
        // El micrófono de esta MacBook entrega **9 canales** (el array de micros en crudo, todos con
        // la misma señal). `AVAudioConverter` sabe cambiar el ritmo y el tipo de muestra, pero para
        // bajar de 9 a 1 necesita que le digan CUÁL; sin mapa no mezcla, devuelve silencio. Y lo
        // devuelve sin error: el conversor dice que sí, entrega el buffer del tamaño correcto, y
        // dentro hay ceros.
        //
        // Así se veía, el 2026-08-18, con once sesiones abiertas y cero transcripciones:
        //     🎙 crudo 9ch → [0.178 0.178 … 0.178]     ← el micrófono oye perfectamente
        //     🎙 micrófono → 21 trozos · nivel 0.0000  ← lo que salía del conversor
        // Todo lo demás funcionaba —socket, altavoz, cara, cancelación de eco— y la conversación
        // seguía muda, porque a Gemini le llegaba silencio a 16 kHz.
        if formatoEntrada.channelCount > 1 {
            conversor?.channelMap = [0]
            Registro.di("🎙 entrada de \(formatoEntrada.channelCount) canales → me quedo con el 0")
        }

        // EL ALTAVOZ SE CONECTA AL RITMO DE LA MEZCLA, NO A 24 kHz — y esto es lo que costaba el
        // arranque. Conectando el reproductor a 24 kHz, la inicialización del nodo de salida moría
        // con «-10875 · PerformCommand(*outputNode, kAUInitialize)»: con el procesado de voz puesto,
        // la cadena de salida corre al ritmo que fija esa unidad y no admite que le enchufen otro.
        // Los 24 kHz siguen siendo los del modelo; lo que cambia es que la conversión la hacemos
        // nosotros y no se la pedimos al motor cuando ya no puede hacerla (2026-08-18).
        // MANDA LA SALIDA, NO LA MEZCLA. La sonda del 2026-08-18 enseñó el desacuerdo entero:
        //
        //     out.in = 48000 Hz / 2ch     ← lo que exige la salida con procesado de voz
        //     mezcla = 44100 Hz / 2ch     ← a lo que AVAudioEngine pone el mezclador por su cuenta
        //
        // El mezclador se conecta a la salida al ritmo que él eligió, la salida no lo acepta, y todo
        // el arranque muere en «kAUInitialize». Se reconecta a mano al formato que pide la salida.
        // Sin procesado de voz los dos coinciden y esto no hace nada; con él, es la diferencia entre
        // poder interrumpirla y no poder.
        // NO SE TOCA EL MEZCLADOR. `mainMixerNode` es perezoso: la primera vez que se nombra,
        // AVAudioEngine lo crea Y lo cablea a la salida al ritmo que él decide —44 100— mientras la
        // salida con procesado de voz exige 48 000. Ese desacuerdo es el que mata el arranque, y
        // reconectarlo después no lo deshace. Se conecta el reproductor DIRECTO a la salida.
        let mezcla = motor.outputNode.inputFormat(forBus: 0)
        guard mezcla.sampleRate > 0,
              let alReproductor = AVAudioFormat(commonFormat: .pcmFormatFloat32,
                                                sampleRate: mezcla.sampleRate,
                                                channels: 1, interleaved: false),
              let delModelo = AVAudioFormat(commonFormat: .pcmFormatFloat32,
                                            sampleRate: Self.ritmoSalida,
                                            channels: 1, interleaved: false) else {
            throw Fallo.sinFormato
        }
        formatoReproductor = alReproductor
        formatoDelModelo = delModelo
        conversorSalida = AVAudioConverter(from: delModelo, to: alReproductor)

        if reproductor.engine != nil { motor.detach(reproductor) }
        motor.attach(reproductor)
        motor.connect(reproductor, to: motor.outputNode, format: alReproductor)

        entrada.removeTap(onBus: 0)
        entrada.installTap(onBus: 0, bufferSize: 2048, format: formatoEntrada) { [weak self] buffer, _ in
            self?.mandar(buffer, a: destino)
        }

        motor.prepare()
        try motor.start()
        reproductor.play()
        abierto = true
        Registro.di("🎙 audio vivo abierto · entra \(Int(formatoEntrada.sampleRate)) Hz → \(Int(Self.ritmoEntrada)) Hz"
                  + " · sale \(Int(Self.ritmoSalida)) Hz → \(Int(mezcla.sampleRate)) Hz")
    }

    func cerrar() {
        guard abierto else { return }
        abierto = false
        motor.inputNode.removeTap(onBus: 0)
        reproductor.stop()
        if motor.isRunning { motor.stop() }

        // SE DEVUELVE EL PROCESADO DE VOZ, Y **DESPUÉS** DE PARAR EL MOTOR. El orden es el arreglo.
        //
        // Estaba antes del `stop()` y con `try?`: apagarlo sobre un motor EN MARCHA falla, el `try?`
        // se comía el motivo, y la unidad de entrada del proceso se quedaba en modo procesado para
        // siempre. Quien lo heredaba era el oído local al recuperar el micrófono, que abría y veía
        // la entrada con 3 CANALES en vez de 1 y no recibía un solo búfer: Ü sorda después de cada
        // conversación, sin un renglón que lo explicara. Ni reabrir la app lo curaba.
        //
        // Cuesta creer que sea el orden hasta que se mide: la sonda (`U_SONDA=1`) mostró que hacerlo
        // sobre un motor parado deja la entrada en 1 canal y transcribiendo. 2026-08-19.
        do {
            try motor.inputNode.setVoiceProcessingEnabled(false)
        } catch {
            Registro.di("🎙 ✘ no pude devolver el procesado de voz: \((error as NSError).code) · \(error.localizedDescription)")
        }
        motor.detach(reproductor)
        conversor = nil
        candado.lock(); enCola = 0; pico = 0; candado.unlock()
        Registro.di("🎙 audio vivo cerrado")
    }

    // ── Micrófono → modelo ───────────────────────────────────────────────────────────────────────

    private func mandar(_ buffer: AVAudioPCMBuffer, a destino: AVAudioFormat) {
        guard let conversor else { return }


        let razon = destino.sampleRate / buffer.format.sampleRate
        let capacidad = AVAudioFrameCount(Double(buffer.frameLength) * razon) + 1024
        guard let salida = AVAudioPCMBuffer(pcmFormat: destino, frameCapacity: capacidad) else { return }

        var entregado = false
        var error: NSError?
        conversor.convert(to: salida, error: &error) { _, estado in
            if entregado { estado.pointee = .noDataNow; return nil }
            entregado = true
            estado.pointee = .haveData
            return buffer
        }
        if let error {
            Registro.di("🎙 ✘ convirtiendo el micrófono: \(error.localizedDescription)")
            return
        }
        guard salida.frameLength > 0, let crudo = salida.int16ChannelData else { return }
        let bytes = Int(salida.frameLength) * MemoryLayout<Int16>.size

        var suma: Double = 0
        for i in 0..<Int(salida.frameLength) {
            let v = Double(crudo[0][i]) / 32768.0
            suma += v * v
        }
        trozos += 1
        picoEntrada = max(picoEntrada, (suma / Double(salida.frameLength)).squareRoot())
        // Se anota poco cuando va bien y en cuanto se calla cuando no: cada dos segundos si entra
        // silencio —que es la avería— y cada quince si hay sonido, solo para dejar constancia de que
        // el micrófono sigue vivo sin llenar el registro.
        let mudo = picoEntrada < 0.0005
        if Date().timeIntervalSince(ultimoAviso) > (mudo ? 2 : 15) {
            ultimoAviso = Date()
            Registro.di(String(format: "🎙 micrófono → %d trozos · nivel %.4f%@",
                               trozos, picoEntrada, mudo ? "  ⚠︎ SILENCIO" : ""))
            trozos = 0; picoEntrada = 0
        }

        alCapturar?(Data(bytes: crudo[0], count: bytes))
    }

    // ── Modelo → altavoz ─────────────────────────────────────────────────────────────────────────

    /// Un trozo de PCM16 a 24 kHz recién llegado. Se encola; no se espera a tenerlo todo.
    func reproducir(_ pcm: Data) {
        guard abierto, let formatoDelModelo, let formatoReproductor, !pcm.isEmpty else { return }
        let muestras = pcm.count / MemoryLayout<Int16>.size
        guard muestras > 0,
              let buffer = AVAudioPCMBuffer(pcmFormat: formatoDelModelo,
                                            frameCapacity: AVAudioFrameCount(muestras)) else { return }
        buffer.frameLength = AVAudioFrameCount(muestras)

        var suma: Double = 0
        pcm.withUnsafeBytes { crudo in
            let enteros = crudo.bindMemory(to: Int16.self)
            let destino = buffer.floatChannelData![0]
            for i in 0..<muestras {
                let v = Float(enteros[i]) / 32768.0
                destino[i] = v
                suma += Double(v * v)
            }
        }
        let rms = (suma / Double(muestras)).squareRoot()

        // Al ritmo de la mezcla. Si coinciden, no se toca nada.
        let aSonar: AVAudioPCMBuffer
        if formatoDelModelo.sampleRate == formatoReproductor.sampleRate {
            aSonar = buffer
        } else {
            guard let conversorSalida else { return }
            let razon = formatoReproductor.sampleRate / formatoDelModelo.sampleRate
            let capacidad = AVAudioFrameCount(Double(muestras) * razon) + 1024
            guard let convertido = AVAudioPCMBuffer(pcmFormat: formatoReproductor,
                                                    frameCapacity: capacidad) else { return }
            var entregado = false
            var error: NSError?
            conversorSalida.convert(to: convertido, error: &error) { _, estado in
                if entregado { estado.pointee = .noDataNow; return nil }
                entregado = true
                estado.pointee = .haveData
                return buffer
            }
            if let error {
                Registro.di("🎙 ✘ convirtiendo lo que dice: \(error.localizedDescription)")
                return
            }
            aSonar = convertido
        }

        candado.lock()
        enCola += 1
        // El pico se queda y baja despacio: el silencio ENTRE dos palabras de una misma frase no
        // tiene que cerrar la boca de la carita, o la boca parpadea en vez de hablar.
        pico = max(rms * 3.0, pico * 0.82)
        candado.unlock()

        reproductor.scheduleBuffer(aSonar) { [weak self] in
            guard let self else { return }
            candado.lock(); enCola = max(0, enCola - 1); candado.unlock()
        }
    }

    /// La cortaron. Se tira TODO lo que quedaba por decir, no se deja terminar la frase.
    ///
    /// Es la mitad visible del barge-in: si sigue sonando medio segundo después de que la
    /// interrumpiste, la interrupción no se siente como tal — se siente como que no te hizo caso.
    func callar() {
        guard abierto else { return }
        reproductor.stop()
        candado.lock(); enCola = 0; pico = 0; candado.unlock()
        reproductor.play()
    }

    enum Fallo: LocalizedError {
        case sinFormato
        var errorDescription: String? { "No pude preparar el audio de la conversación." }
    }
}
