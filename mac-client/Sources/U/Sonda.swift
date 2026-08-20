import AVFoundation
import Foundation
import Speech

// LA SONDA DEL MICRÓFONO, dentro de Ü y detrás de `U_SONDA=1`.
//
//     open --env U_SONDA=1 "…/U.app"        y el informe queda en ~/.u/sonda.txt
//
// Vive AQUÍ y no en un binario aparte por una razón práctica que costó media hora: una app nueva
// necesita que un humano le conceda el micrófono en un diálogo, y ese diálogo hay que estar delante
// para pulsarlo. Ü ya lo tiene concedido.
//
// Lo que importa se conserva: este modo NO arranca el oído, ni la voz, ni el cerebro, ni la sesión
// en vivo. Mide el APARATO y nada más — que era todo el punto de sacar la app de la ecuación. Aquí
// «no oye» solo puede tener un culpable.
//
// No concluye: describe. Cada paso dice cuántos búferes entraron Y con cuánta energía, porque
// «entró audio» y «entró audio con algo dentro» son cosas distintas, y confundirlas fue la trampa
// de los 9 canales.

enum Sonda {

    static var pedida: Bool { ProcessInfo.processInfo.environment["U_SONDA"] == "1" }

    static let informe = FileManager.default.homeDirectoryForCurrentUser
        .appendingPathComponent(".u/sonda.txt")

    static func di(_ t: String) {
        Registro.di("🔬 " + t)
        if let h = try? FileHandle(forWritingTo: informe) {
            h.seekToEndOfFile(); h.write(Data((t + "\n").utf8)); try? h.close()
        }
    }
    static func titulo(_ t: String) { di("\n── \(t) ───────────────────────────────") }

    // ── Medir una captura: cuántos búferes, y con cuánta energía ─────────────────────────────────────

    struct Medida {
    var buferes = 0
    var picoMaximo: Float = 0
    var formato = ""
    var error: String?

    var resumen: String {
        if let e = error { return "✘ \(e)" }
        if buferes == 0 { return "✘ CERO búferes · \(formato)" }
        let hay = picoMaximo > 0.001
        return "\(hay ? "✅" : "⚠︎") \(buferes) búferes · pico \(String(format: "%.4f", picoMaximo))"
            + " \(hay ? "" : "(SILENCIO ABSOLUTO) ")· \(formato)"
    }
    }

    /// Abre un motor, escucha `segundos`, y devuelve qué entró. `conEco` enciende la cancelación de eco,
    /// que es lo que hace `AudioVivo` y lo que se sospecha que ensucia el aparato.
    static func capturar(segundos: Double, conEco: Bool, alBuffer: ((AVAudioPCMBuffer) -> Void)? = nil) -> Medida {
    var m = Medida()
    let motor = AVAudioEngine()
    let entrada = motor.inputNode
    do {
        try entrada.setVoiceProcessingEnabled(conEco)
    } catch {
        m.error = "no pude \(conEco ? "encender" : "apagar") la cancelación de eco: \(error)"
        return m
    }
    let formato = entrada.outputFormat(forBus: 0)
    m.formato = "\(Int(formato.sampleRate)) Hz / \(formato.channelCount) canal(es)"
    guard formato.channelCount > 0, formato.sampleRate > 0 else {
        m.error = "el formato de entrada es inválido: \(m.formato)"
        return m
    }

    let candado = NSLock()
    entrada.installTap(onBus: 0, bufferSize: 1024, format: formato) { buffer, _ in
        candado.lock()
        m.buferes += 1
        if let datos = buffer.floatChannelData?[0] {
            for i in 0..<Int(buffer.frameLength) { m.picoMaximo = max(m.picoMaximo, abs(datos[i])) }
        }
        candado.unlock()
        alBuffer?(buffer)
    }
    motor.prepare()
    do { try motor.start() } catch {
        m.error = "el motor no arrancó: \(error)"
        entrada.removeTap(onBus: 0)
        return m
    }
    Thread.sleep(forTimeInterval: segundos)
    entrada.removeTap(onBus: 0)
    motor.stop()
    return m
    }

    /// Lo mismo, pero pasándoselo al reconocedor. Contesta la otra mitad de la pregunta: entra audio,
    /// ¿y se transcribe?
    static func transcribir(segundos: Double, conEco: Bool) -> (Medida, String) {
    guard let rec = SFSpeechRecognizer(locale: Locale(identifier: "es-MX")), rec.isAvailable else {
        return (Medida(), "✘ el reconocedor es-MX no está disponible")
    }
    let peticion = SFSpeechAudioBufferRecognitionRequest()
    peticion.shouldReportPartialResults = true

    var texto = ""
    var fallo: String?
    let candado = NSLock()
    let tarea = rec.recognitionTask(with: peticion) { r, e in
        candado.lock()
        if let r { texto = r.bestTranscription.formattedString }
        if let e, (e as NSError).code != 216 { fallo = "\((e as NSError).domain) \((e as NSError).code): \(e.localizedDescription)" }
        candado.unlock()
    }

    // Se le habla por el altavoz mientras escucha, si no se mide el silencio de la sala.
    DispatchQueue.global().asyncAfter(deadline: .now() + 0.6) {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/usr/bin/say")
        p.arguments = ["-v", "Paulina", "uno dos tres cuatro cinco seis"]
        try? p.run(); p.waitUntilExit()
    }

    let m = capturar(segundos: segundos, conEco: conEco) { peticion.append($0) }
    peticion.endAudio()
    Thread.sleep(forTimeInterval: 1.2)
    tarea.cancel()

    candado.lock(); defer { candado.unlock() }
    if let fallo { return (m, "✘ el reconocedor falló: \(fallo)") }
    return (m, texto.isEmpty ? "✘ NO TRANSCRIBIÓ NADA" : "✅ «\(texto)»")
    }


    static func correr() {
        try? Data().write(to: informe)
        Self.titulo("permisos (ya concedidos a Ü; aquí solo se comprueban)")
        Self.di("  micrófono: \(AVCaptureDevice.authorizationStatus(for: .audio).rawValue) (3 = concedido)")
        Self.di("  reconocimiento: \(SFSpeechRecognizer.authorizationStatus().rawValue) (3 = concedido)")

        Self.titulo("1 · captura limpia, sin que nadie haya tocado la cancelación de eco")
        Self.di("  " + capturar(segundos: 3, conEco: false).resumen)

        Self.titulo("2 · transcripción limpia")
        let (m2, t2) = transcribir(segundos: 6, conEco: false)
        Self.di("  audio:         " + m2.resumen)
        Self.di("  transcripción: " + t2)

        Self.titulo("3 · ahora alguien usa el micrófono CON cancelación de eco (lo que hace AudioVivo)")
        Self.di("  " + capturar(segundos: 3, conEco: true).resumen)

        Self.titulo("4 · y se apaga, como hace AudioVivo al cerrar")
        let apagador = AVAudioEngine()
        try? apagador.inputNode.setVoiceProcessingEnabled(false)
        Self.di("  apagada")
        Thread.sleep(forTimeInterval: 1.0)

        Self.titulo("5 · captura limpia OTRA VEZ — aquí es donde la app se queda sorda")
        Self.di("  " + capturar(segundos: 3, conEco: false).resumen)

        Self.titulo("6 · transcripción OTRA VEZ")
        let (m6, t6) = transcribir(segundos: 6, conEco: false)
        Self.di("  audio:         " + m6.resumen)
        Self.di("  transcripción: " + t6)

        Self.titulo("7 · y con un respiro de 3 s de por medio")
        Thread.sleep(forTimeInterval: 3)
        let (m7, t7) = transcribir(segundos: 6, conEco: false)
        Self.di("  audio:         " + m7.resumen)
        Self.di("  transcripción: " + t7)

        Self.titulo("qué dice esto")
        Self.di("""
          · Si 5 y 6 van bien, el aparato NO se ensucia y el fallo es de la app.
          · Si 5 trae búferes pero 6 no transcribe, son DOS problemas y el segundo es del reconocedor.
          · Si 5 no trae búferes, el aparato se queda tomado y hay que averiguar por quién.
          · Si 7 va bien y 6 no, es cuestión de TIEMPO y la cura es esperar, no reabrir.
        """)
        Self.di("\nFIN")
        exit(0)
    }
}
