import AppKit
import AVFoundation
import Speech
import UCore

/// Native speech provides an independent path when the live voice service is unavailable.
@MainActor
public final class Speech: NSObject, AVSpeechSynthesizerDelegate {
    public var onText: ((String) -> Void)?
    public var onPartial: ((String) -> Void)?
    public var onState: ((Bool, Bool) -> Void)?
    public var onError: ((String) -> Void)?
    private let recognizer = SFSpeechRecognizer(locale: Locale(identifier: "es-CO"))
    private let engine = AVAudioEngine()
    private let synthesizer = AVSpeechSynthesizer()
    private var request: SFSpeechAudioBufferRecognitionRequest?
    private var recognition: SFSpeechRecognitionTask?
    private var silence: Task<Void, Never>?
    private var renew: Task<Void, Never>?
    private var restart: Task<Void, Never>?
    private var tapInstalled = false
    private var wanted = false
    private var listening = false
    private var speaking = false
    private var epoch = UUID()
    private var lastText = ""
    private var consecutiveErrors = 0
    public override init() { super.init(); synthesizer.delegate = self }
    public static func authorize() async -> Bool {
        let microphone = await AVCaptureDevice.requestAccess(for: .audio)
        guard microphone else { return false }
        let speech = await withCheckedContinuation { continuation in SFSpeechRecognizer.requestAuthorization { continuation.resume(returning: $0) } }
        return speech == .authorized
    }
    public func start() async {
        wanted = true; consecutiveErrors = 0
        guard await Self.authorize() else { wanted = false; onError?("Activa Micrófono y Reconocimiento de voz en Privacidad y seguridad."); return }
        guard wanted else { return }
        open()
    }
    public func stop() {
        wanted = false; restart?.cancel(); restart = nil
        closeInput(); synthesizer.stopSpeaking(at: .immediate); speaking = false
        onState?(false, false)
    }
    public func pauseInput() { closeInput() }
    public func resumeInput() { if wanted, !speaking { open() } }
    public func say(_ text: String) {
        guard !text.isEmpty else { return }
        closeInput()
        speaking = true
        onState?(false, true)
        let utterance = AVSpeechUtterance(string: text)
        utterance.voice = AVSpeechSynthesisVoice(language: "es-MX") ?? AVSpeechSynthesisVoice(language: "es-ES")
        utterance.rate = AVSpeechUtteranceDefaultSpeechRate
        synthesizer.speak(utterance)
    }
    private func open() {
        guard wanted, !speaking, !listening else { return }
        guard let recognizer, recognizer.isAvailable else { onError?("El reconocimiento de voz de macOS no está disponible."); return }
        do {
            closeInput()
            let id = UUID(); epoch = id; lastText = ""
            let request = SFSpeechAudioBufferRecognitionRequest()
            request.shouldReportPartialResults = true
            if recognizer.supportsOnDeviceRecognition { request.requiresOnDeviceRecognition = true }
            self.request = request
            let input = engine.inputNode
            let format = input.outputFormat(forBus: 0)
            guard format.sampleRate > 0, format.channelCount > 0 else { throw AgentError.unavailable("No hay un micrófono disponible.") }
            input.installTap(onBus: 0, bufferSize: 1024, format: format) { buffer, _ in request.append(buffer) }
            tapInstalled = true
            engine.prepare(); try engine.start()
            listening = true; onState?(true, false)
            recognition = recognizer.recognitionTask(with: request) { [weak self] result, error in
                let text = result?.bestTranscription.formattedString
                let final = result?.isFinal ?? false
                Task { @MainActor [weak self] in
                    guard let self, self.epoch == id else { return }
                    if let text, !text.isEmpty {
                        self.consecutiveErrors = 0
                        self.lastText = text; self.onPartial?(text)
                        self.silence?.cancel()
                        if final { self.deliver(id) }
                        else {
                            self.silence = Task { [weak self] in
                                do { try await Task.sleep(nanoseconds: 900_000_000) } catch { return }
                                self?.deliver(id)
                            }
                        }
                    } else if error != nil {
                        self.closeInput()
                        self.consecutiveErrors += 1
                        if self.consecutiveErrors >= 3 {
                            self.wanted = false
                            self.onError?("El dictado se interrumpió tres veces. Revisa el micrófono y vuelve a activarlo.")
                        } else { self.scheduleRestart() }
                    }
                }
            }
            renew = Task { [weak self] in
                do { try await Task.sleep(nanoseconds: 50_000_000_000) } catch { return }
                guard let self, self.epoch == id else { return }
                if !self.lastText.isEmpty { self.deliver(id) } else { self.closeInput(); self.scheduleRestart() }
            }
        } catch { closeInput(); onError?(error.localizedDescription) }
    }
    private func deliver(_ id: UUID) {
        guard epoch == id, !lastText.isEmpty else { return }
        let text = lastText
        closeInput()
        onText?(text)
        scheduleRestart()
    }
    private func closeInput() {
        epoch = UUID(); silence?.cancel(); silence = nil; renew?.cancel(); renew = nil
        engine.stop()
        if tapInstalled { engine.inputNode.removeTap(onBus: 0); tapInstalled = false }
        request?.endAudio(); request = nil; recognition?.cancel(); recognition = nil
        listening = false
    }
    private func scheduleRestart() {
        restart?.cancel()
        restart = Task { [weak self] in
            do { try await Task.sleep(nanoseconds: 450_000_000) } catch { return }
            self?.resumeInput()
        }
    }
    nonisolated public func speechSynthesizer(_ synthesizer: AVSpeechSynthesizer, didFinish utterance: AVSpeechUtterance) {
        Task { @MainActor [weak self] in
            guard let self, !self.synthesizer.isSpeaking else { return }
            self.speaking = false; self.onState?(false, false); self.scheduleRestart()
        }
    }
}
