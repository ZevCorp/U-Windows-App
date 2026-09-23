import AppKit
import UCore
import UMac

@MainActor
struct SmokeTest {
    /// Opt-in integration probe. Only clicks the local fixture; never opens the microphone.
    static func execution(output: URL) async {
        var evidence: [String: Any] = ["date": ISO8601DateFormatter().string(from: Date()), "passed": false]
        let desktop = Desktop(); desktop.begin()
        defer {
            desktop.stop()
            if let data = try? JSONSerialization.data(withJSONObject: evidence, options: [.prettyPrinted, .sortedKeys]) { try? data.write(to: output, options: .atomic) }
            NSApp.terminate(nil)
        }
        do {
            let graph = try GraphClient(baseURL: UserDefaults.standard.string(forKey: "graphURL") ?? GraphClient.defaultURL,
                                        apiKey: Credentials.read("GRAPH_API_KEY") ?? "")
            let keys = try await graph.providerKeys()
            evidence["graphVoiceKey"] = keys.openai?.isEmpty == false
            evidence["graphJevKey"] = keys.typesafe?.isEmpty == false
            if let key = Credentials.read("OPENAI_API_KEY") ?? keys.openai { evidence["liveOneLunaToolRoundtrip"] = try await voiceContract(key: key) }
            guard let key = Credentials.read("TYPESAFE_API_KEY") ?? keys.typesafe, !key.isEmpty else { throw AgentError.unavailable("Graph no entrega typesafe.") }
            guard let fixture = NSRunningApplication.runningApplications(withBundleIdentifier: "com.zevcorp.u.mac.fixture").first else { throw AgentError.unavailable("Abre UFixture.app antes de la prueba.") }
            fixture.activate(options: [])
            for _ in 0..<30 {
                if NSWorkspace.shared.frontmostApplication?.processIdentifier == fixture.processIdentifier { break }
                try await Task.sleep(for: .milliseconds(20))
            }
            let client = JevClient(key: key)
            var timings: [[String: Double]] = []
            for _ in 0..<5 {
                var previous = "", repeats = 0
                let outcome = try await desktop.jevStep(client, goal: "Pulsa el botón Sumar uno una vez ahora. No está cumplido todavía.", previous: &previous, repeats: &repeats, onStep: { _ in })
                timings.append(desktop.lastJevTiming)
                if let outcome { throw AgentError.unavailable(outcome) }
            }
            evidence["steps"] = timings
            let observed = try await desktop.observe()
            evidence["fiveClicksVerified"] = observed.uiContext.contains("Contador: 5")
            evidence["passed"] = evidence["fiveClicksVerified"] as? Bool == true && evidence["liveOneLunaToolRoundtrip"] as? Bool == true
        } catch { evidence["error"] = error.localizedDescription }
    }
    private static func voiceContract(key: String) async throws -> Bool {
        let session = URLSession(configuration: .ephemeral)
        var request = URLRequest(url: URL(string: "wss://api.openai.com/v1/live/sessions")!)
        request.setValue("Bearer \(key)", forHTTPHeaderField: "Authorization")
        let socket = session.webSocketTask(with: request); socket.resume()
        defer { socket.cancel(with: .normalClosure, reason: nil); session.invalidateAndCancel() }
        let timeout = Task { try await Task.sleep(for: .seconds(30)); socket.cancel(with: .goingAway, reason: nil) }
        defer { timeout.cancel() }
        func send(_ event: [String: Any]) async throws {
            try await socket.send(.string(String(decoding: JSONSerialization.data(withJSONObject: event), as: UTF8.self)))
        }
        var start = LiveProtocol.start(), config = LiveProtocol.start()["session"] as! [String: Any]
        config["delegation"] = ["type": "responses", "responses": ["model": "gpt-5.6-luna", "parallel_tool_calls": false,
            "instructions": "Call health_check exactly once. After the result say listo.", "tools": [["type": "function", "name": "health_check", "description": "Local harmless connection check", "parameters": ["type": "object", "properties": [:], "additionalProperties": false]]]]]
        start["session"] = config
        try await send(start)
        var batch = ToolBatch(), returned = false
        while true {
            let message = try await socket.receive()
            let data: Data
            switch message { case .data(let value): data = value; case .string(let value): data = Data(value.utf8); @unknown default: continue }
            guard let event = try JSONSerialization.jsonObject(with: data) as? [String: Any] else { continue }
            if event["type"] as? String == "error" {
                let code = (event["error"] as? [String: Any])?["code"] as? String ?? "unknown"
                throw AgentError.unavailable("Live 1 rechazó la prueba: \(code)")
            }
            if event["type"] as? String == "session.started" {
                try await send(["type": "response.item.create", "item": ["type": "message", "role": "user", "content": [["type": "input_text", "text": "Ejecuta health_check ahora."]]]])
                try await send(["type": "response.create"])
            }
            if let nested = event["event"] as? [String: Any] {
                if let call = LiveProtocol.call(in: nested) {
                    guard call.name == "health_check", !returned else { throw AgentError.invalid("Llamada inesperada en la prueba de voz.") }
                    _ = batch.begin(call.id)
                    try await send(LiveProtocol.output(call: call.id, text: "ok"))
                    _ = batch.finish(call.id); returned = true
                }
                if nested["type"] as? String == "response.completed" {
                    if batch.responseDone() { try await send(["type": "response.create"]) }
                    else if returned { return true }
                }
                if nested["type"] as? String == "response.failed" { throw AgentError.unavailable("Falló el delegado Luna.") }
            }
        }
    }
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
