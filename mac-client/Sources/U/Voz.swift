import AppKit
import AVFoundation

/// La voz de Ü — y, sobre todo, SU BOCA.
///
/// Hablar no es solo que salga sonido: es que la cara se mueva con lo que dice. Una carita que
/// suelta una frase entera con la boca quieta no parece que hable, parece un altavoz con una
/// pegatina encima.
///
/// La boca no intenta acertar la vocal exacta —eso exigiría analizar el sonido, que es otro problema
/// entero—, pero tampoco se mueve al azar: LEE LA PALABRA que está diciendo en ese momento y saca de
/// ahí la forma. Las vocales cerradas («o», «u») redondean y estrechan; las abiertas («a», «e», «i»)
/// ensanchan. Con eso basta para que deje de parecer un muñeco.
final class Voz: NSObject, AVSpeechSynthesizerDelegate {

    private let sintetizador = AVSpeechSynthesizer()
    private weak var cara: FaceView?
    private var reloj: Timer?
    private var palabraActual = ""
    private var fase: CGFloat = 0

    /// Se avisa al terminar de hablar TODO, para que la carita vuelva a lo suyo.
    var alTerminar: (() -> Void)?

    /// Frases encoladas que aún no han terminado de sonar.
    ///
    /// Hace falta llevar la cuenta porque la respuesta llega por trozos: cada frase se encola en
    /// cuanto el modelo la suelta, así que hay varias en vuelo. Sin este contador, la primera en
    /// terminar dispararía «ya acabé», el micrófono se abriría en mitad de la segunda frase, y la
    /// carita se oiría a sí misma.
    private var enVuelo = 0

    init(cara: FaceView) {
        self.cara = cara
        super.init()
        sintetizador.delegate = self
    }

    var hablando: Bool { sintetizador.isSpeaking }

    /// La mejor voz en español que haya EN ESTE Mac, elegida al vuelo.
    ///
    /// No se fija a un nombre concreto a propósito. macOS trae de fábrica solo las voces
    /// «compactas» —las que suenan a robot de los noventa— y las buenas (mejoradas y premium) hay
    /// que descargarlas. Si esto pidiera «Paulina» y ya está, daría igual que el usuario se bajara
    /// la voz buena: seguiría sonando igual de mal. Preguntando por calidad, el día que se instale
    /// una mejor la carita empieza a usarla sola, sin tocar nada.
    private lazy var vozElegida: AVSpeechSynthesisVoice? = {
        if let forzada = ProcessInfo.processInfo.environment["U_VOZ"],
           let v = AVSpeechSynthesisVoice.speechVoices().first(where: {
               $0.identifier == forzada || $0.name.lowercased() == forzada.lowercased()
           }) {
            Registro.di("🔊 voz forzada: \(v.name)")
            return v
        }

        let candidatas = AVSpeechSynthesisVoice.speechVoices().filter { $0.language.hasPrefix("es") }
        let mejor = candidatas.max { a, b in
            if a.quality.rawValue != b.quality.rawValue { return a.quality.rawValue < b.quality.rawValue }
            // A igual calidad, el español de México antes que el de España: es el acento del usuario.
            let am = a.language == "es-MX" ? 1 : 0, bm = b.language == "es-MX" ? 1 : 0
            if am != bm { return am < bm }
            // Y antes las voces «de verdad» que las de la síntesis vieja (Eloquence), que son las que
            // suenan a máquina por mucho que se les baje la velocidad.
            let ae = a.identifier.contains("eloquence") ? 0 : 1
            let be = b.identifier.contains("eloquence") ? 0 : 1
            return ae < be
        }

        if let mejor {
            // `.default` vale 1, no 0 — restar uno o el registro dice «mejorada» de una voz compacta,
            // que es exactamente lo que hizo la primera vez y mandó a buscar el problema a otro lado.
            let cal = ["normal", "mejorada", "premium"][max(0, min(2, mejor.quality.rawValue - 1))]
            Registro.di("🔊 voz: \(mejor.name) (\(mejor.language), calidad \(cal))")
            if mejor.quality == .default {
                Registro.di("🔊 ⚠︎ solo hay voces compactas instaladas — suena a robot hasta que se baje una mejorada")
            }
        } else {
            Registro.di("🔊 ✘ no hay ninguna voz en español instalada")
        }
        return mejor
    }()

    func decir(_ texto: String) {
        guard !texto.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return }
        let frase = AVSpeechUtterance(string: texto)
        frase.voice = vozElegida
        // Un pelo más rápido y más agudo que el ajuste de fábrica. El ritmo por defecto suena a
        // locutor de aeropuerto, y nadie se encariña con un anuncio de aeropuerto.
        frase.rate = 0.52
        frase.pitchMultiplier = 1.04
        frase.preUtteranceDelay = 0
        enVuelo += 1
        sintetizador.speak(frase)
    }

    func callar() {
        enVuelo = 0
        sintetizador.stopSpeaking(at: .immediate)
    }

    // ── La boca ──────────────────────────────────────────────────────────────────────────────────

    private func arrancarBoca() {
        guard reloj == nil else { return }
        cara?.mood = .hablando
        // A ~16 cuadros por segundo, no a 60: la boca al hablar cambia de forma, y una forma nueva hay
        // que dibujarla entera. A 60 serían cuatro veces más repintados para un movimiento que a esta
        // velocidad ya se ve continuo.
        let t = Timer(timeInterval: 1.0 / 16.0, repeats: true) { [weak self] _ in self?.cuadroDeBoca() }
        RunLoop.main.add(t, forMode: .common)
        reloj = t
    }

    private func pararBoca() {
        reloj?.invalidate(); reloj = nil
        cara?.mouthOpen = 0
        cara?.mouthRound = 0
        alTerminar?()
    }

    /// Una frase terminó. Solo cuando NO queda ninguna más en la cola se da por acabado el turno.
    private func termino() {
        enVuelo = max(0, enVuelo - 1)
        guard enVuelo == 0, !sintetizador.isSpeaking else { return }
        pararBoca()
    }

    private func cuadroDeBoca() {
        guard let cara else { return }
        fase += 1

        // La apertura oscila, pero no con una sola onda: dos ritmos distintos sumados evitan el
        // vaivén mecánico que delata una animación en bucle.
        let a = sin(fase * 0.9), b = sin(fase * 1.7 + 1.2)
        var abre = 0.42 + 0.30 * a + 0.16 * b
        abre = max(0.10, min(0.95, abre))

        cara.mouthOpen = abre
        cara.mouthRound = redondezDe(palabraActual)
    }

    /// Cuánto redondea la boca la palabra que está diciendo: manda su vocal más cerrada.
    private func redondezDe(_ palabra: String) -> CGFloat {
        let v = palabra.lowercased()
        if v.contains("u") || v.contains("ú") { return 0.92 }
        if v.contains("o") || v.contains("ó") { return 0.72 }
        if v.contains("a") || v.contains("á") { return 0.10 }
        if v.contains("e") || v.contains("i") { return 0.05 }
        return 0.30
    }

    // ── Avisos del sintetizador ──────────────────────────────────────────────────────────────────

    func speechSynthesizer(_ s: AVSpeechSynthesizer, didStart utterance: AVSpeechUtterance) {
        arrancarBoca()
    }

    func speechSynthesizer(_ s: AVSpeechSynthesizer, willSpeakRangeOfSpeechString rango: NSRange,
                           utterance: AVSpeechUtterance) {
        let texto = utterance.speechString as NSString
        guard rango.location + rango.length <= texto.length else { return }
        palabraActual = texto.substring(with: rango)
    }

    func speechSynthesizer(_ s: AVSpeechSynthesizer, didFinish utterance: AVSpeechUtterance) {
        termino()
    }

    func speechSynthesizer(_ s: AVSpeechSynthesizer, didCancel utterance: AVSpeechUtterance) {
        enVuelo = 0
        pararBoca()
    }
}
