import AppKit
import UCore
import UMac

@MainActor
struct SmokeTest {
    static func run(output: URL) async {
        var evidence: [String: Any] = ["date": ISO8601DateFormatter().string(from: Date()), "passed": false]
        let desktop = Desktop(); desktop.begin()
        defer {
            desktop.stop()
            if let data = try? JSONSerialization.data(withJSONObject: evidence, options: [.prettyPrinted, .sortedKeys]) { try? data.write(to: output, options: .atomic) }
            NSApp.terminate(nil)
        }
        do {
            guard let fixture = NSRunningApplication.runningApplications(withBundleIdentifier: "com.zevcorp.u.mac.fixture").first else { throw AgentError.unavailable("Abre primero UFixture.app.") }
            fixture.activate(options: [])
            try await Task.sleep(nanoseconds: 500_000_000)
            let before = try await desktop.observe()
            evidence["app"] = before.screen
            guard before.uiContext.contains("Sumar uno") else { throw AgentError.unavailable("AX no encontró el botón de prueba.") }
            _ = try await desktop.tool("click_element", args: ["label": "Sumar uno"])
            let after = try await desktop.observe()
            guard after.uiContext.contains("Contador: 1") else { throw AgentError.unavailable("AXPress no incrementó el contador.") }
            evidence["axPress"] = true
            _ = try await desktop.tool("set_value", args: ["label": "Texto de prueba", "text": "¡Hola, Mac! 👋"])
            let typed = try await desktop.observe()
            guard typed.uiContext.contains("¡Hola, Mac! 👋") else { throw AgentError.unavailable("AXValue no conservó Unicode.") }
            evidence["axValueUnicode"] = true
            if ScreenCapture.allowed {
                let shot = try await desktop.observe(screenshot: true)
                guard let encoded = shot.screenshot, let bytes = Data(base64Encoded: encoded), let image = NSBitmapImageRep(data: bytes), image.pixelsWide == shot.width, image.pixelsHigh == shot.height else { throw AgentError.unavailable("La captura no coincide con el espacio de coordenadas.") }
                evidence["screenshotCoordinates"] = true
                try bytes.write(to: output.deletingPathExtension().appendingPathExtension("png"))
            } else { evidence["screenshotCoordinates"] = "blocked: screen capture permission" }
            desktop.stop()
            do { _ = try await desktop.execute(AgentAction(kind: "key", key: "space")); throw AgentError.invalid("La cancelación no bloqueó la entrada.") }
            catch AgentError.stopped { evidence["stopGate"] = true }
            evidence["passed"] = true
        } catch { evidence["error"] = error.localizedDescription }
    }
}
