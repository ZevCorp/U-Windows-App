import AVFoundation
import UCore

/// Owned by one audio input tap. Testable with generated buffers, without opening a microphone.
public final class PCMEncoder {
    private let sourceRate: Double
    private let format: AVAudioFormat
    private let converter: AVAudioConverter
    public init(source: AVAudioFormat) throws {
        guard source.sampleRate > 0, source.channelCount > 0,
              let destination = AVAudioFormat(commonFormat: .pcmFormatInt16, sampleRate: 24_000, channels: 1, interleaved: false),
              let converter = AVAudioConverter(from: source, to: destination) else { throw AgentError.unavailable("Formato del micrófono no compatible.") }
        self.format = destination; self.converter = converter; self.sourceRate = source.sampleRate
        // Select a real microphone channel instead of letting a multichannel downmix produce zeros.
        converter.channelMap = [0]
    }
    public func encode(_ buffer: AVAudioPCMBuffer) throws -> Data {
        let capacity = AVAudioFrameCount(ceil(Double(buffer.frameLength) * 24_000 / sourceRate) + 32)
        guard let output = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: capacity) else { throw AgentError.unavailable("No pude reservar el audio de entrada.") }
        var delivered = false
        var error: NSError?
        let status = converter.convert(to: output, error: &error) { _, state in
            if delivered { state.pointee = .noDataNow; return nil }
            delivered = true; state.pointee = .haveData; return buffer
        }
        guard error == nil, status != .error else { throw AgentError.unavailable("Falló la conversión del micrófono a PCM.") }
        guard output.frameLength > 0, let samples = output.int16ChannelData?[0] else { return Data() }
        return Data(bytes: samples, count: Int(output.frameLength) * 2)
    }
}
