import Foundation
import AVFoundation
import UMac

extension AgentTests {
    func testMultichannelMicrophoneProducesReal24kPCM() throws {
        let format = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: 48_000, channels: 2, interleaved: false)!
        let buffer = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: 4800)!
        buffer.frameLength = 4800
        for channel in 0..<2 {
            for index in 0..<4800 {
                buffer.floatChannelData![channel][index] = channel == 0 ? Float(sin(Double(index) * 2 * .pi * 440 / 48_000) * 0.4) : 0
            }
        }
        let encoder = try PCMEncoder(source: format)
        let data = try encoder.encode(buffer)
        XCTAssertEqual(data.count > 4000 && data.count <= 4800, true)
        var peak: Int = 0
        data.withUnsafeBytes { bytes in
            for index in stride(from: 0, to: bytes.count - 1, by: 2) {
                let sample = Int16(bitPattern: UInt16(bytes[index]) | UInt16(bytes[index + 1]) << 8)
                peak = max(peak, abs(Int(sample)))
            }
        }
        XCTAssertEqual(peak > 10000 && peak < 15000, true)
    }
}
