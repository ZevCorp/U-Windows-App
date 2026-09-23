import AVFoundation
import UCore

/// A single engine provides input and output so macOS voice processing has the speaker reference.
@MainActor
public final class DuplexAudio {
    private var engine: AVAudioEngine?
    private var player: AVAudioPlayerNode?
    private let playbackFormat = AVAudioFormat(standardFormatWithSampleRate: 24_000, channels: 1)!
    private var epoch = UUID()
    private var pendingFrames: Int = 0
    private var tapInstalled = false
    public var onSpeaking: ((Bool) -> Void)?
    public init() {}
    public func start(onPCM: @escaping @Sendable (Data) -> Void, onError: @escaping @Sendable (String) -> Void) throws {
        stop()
        let engine = AVAudioEngine(), player = AVAudioPlayerNode()
        self.engine = engine; self.player = player
        do {
            let input = engine.inputNode
            try input.setVoiceProcessingEnabled(true)
            let hardware = input.outputFormat(forBus: 0)
            let encoder = try PCMEncoder(source: hardware)
            engine.attach(player)
            engine.connect(player, to: engine.mainMixerNode, format: playbackFormat)
            let outputFormat = engine.outputNode.inputFormat(forBus: 0)
            engine.connect(engine.mainMixerNode, to: engine.outputNode, format: outputFormat)
            input.installTap(onBus: 0, bufferSize: 1024, format: hardware) { buffer, _ in
                do {
                    let data = try encoder.encode(buffer)
                    if !data.isEmpty { onPCM(data) }
                } catch { onError(error.localizedDescription) }
            }
            tapInstalled = true
            engine.prepare(); try engine.start(); player.play()
        } catch { stop(); throw error }
    }
    public func play(_ data: Data) throws {
        guard let player, engine?.isRunning == true, data.count % 2 == 0 else { return }
        let frames = data.count / 2
        guard frames > 0, pendingFrames + frames <= 24_000 * 30 else { throw AgentError.unavailable("La cola de voz se llenó; la conversación se detuvo.") }
        guard let buffer = AVAudioPCMBuffer(pcmFormat: playbackFormat, frameCapacity: AVAudioFrameCount(frames)), let samples = buffer.floatChannelData?[0] else { return }
        buffer.frameLength = AVAudioFrameCount(frames)
        data.withUnsafeBytes { raw in
            for index in 0..<frames {
                let lo = UInt16(raw[index * 2]), hi = UInt16(raw[index * 2 + 1]) << 8
                samples[index] = Float(Int16(bitPattern: lo | hi)) / 32768
            }
        }
        let id = epoch
        if pendingFrames == 0 { onSpeaking?(true) }
        pendingFrames += frames
        player.scheduleBuffer(buffer, completionCallbackType: .dataPlayedBack) { [weak self] _ in
            Task { @MainActor in
                guard let self, self.epoch == id else { return }
                self.pendingFrames = max(0, self.pendingFrames - frames)
                if self.pendingFrames == 0 { self.onSpeaking?(false) }
            }
        }
    }
    public func stop() {
        epoch = UUID(); pendingFrames = 0
        player?.stop()
        if let engine {
            engine.stop()
            if tapInstalled { engine.inputNode.removeTap(onBus: 0); tapInstalled = false }
            try? engine.inputNode.setVoiceProcessingEnabled(false)
            engine.reset()
        }
        engine = nil; player = nil; onSpeaking?(false)
    }
}
