import Foundation
import AVFoundation
import UCore

@MainActor
public final class LiveVoice {
    public var onState: ((String) -> Void)?
    public var onText: ((String, Bool) -> Void)?
    public var onSpeaking: ((Bool) -> Void)?
    public var onError: ((String) -> Void)?
    public var onTool: ((String, [String: String]) async throws -> String)?
    public private(set) var connected = false
    private var epoch = UUID()
    private var socket: URLSessionWebSocketTask?
    private var session: URLSession?
    private var receiver: Task<Void, Never>?
    private var sender: Task<Void, Never>?
    private var timeout: Task<Void, Never>?
    private var lifetime: Task<Void, Never>?
    private var toolTasks: [String: Task<Void, Never>] = [:]
    private var seenCalls = Set<String>()
    private var batches: [String: ToolBatch] = [:]
    private let audio = DuplexAudio()
    private var frames: AsyncStream<Data>.Continuation?
    private var responseIDs: [String: String] = [:]
    private var activeResponses = Set<String>()
    private var continuationNeeded = false
    private var apiKey = ""
    public init() {
        audio.onSpeaking = { [weak self] speaking in self?.onSpeaking?(speaking) }
    }
    public func start(key: String, model: String = "gpt-live-1") async throws {
        stop()
        let id = UUID(); epoch = id
        guard await AVCaptureDevice.requestAccess(for: .audio) else { throw AgentError.permission("Micrófono") }
        guard epoch == id, !Task.isCancelled else { throw CancellationError() }
        onState?("Conectando la voz…")
        apiKey = key
        var request = URLRequest(url: URL(string: "wss://api.openai.com/v1/live/sessions")!)
        request.setValue("Bearer \(key)", forHTTPHeaderField: "Authorization")
        request.timeoutInterval = 20
        let session = URLSession(configuration: .ephemeral)
        let socket = session.webSocketTask(with: request)
        self.session = session; self.socket = socket
        socket.resume()
        receiver = Task { [weak self] in
            do {
                while !Task.isCancelled {
                    let message = try await socket.receive()
                    guard let self, self.epoch == id else { return }
                    let data: Data
                    switch message { case .data(let bytes): data = bytes; case .string(let text): data = Data(text.utf8); @unknown default: continue }
                    try await self.handle(data, epoch: id)
                }
            } catch {
                guard let self, self.epoch == id, !Task.isCancelled else { return }
                self.fail("Se cortó la conexión de voz. Puedes volver a conectarla.")
            }
        }
        timeout = Task { [weak self] in
            do { try await Task.sleep(nanoseconds: 20_000_000_000) } catch { return }
            guard let self, self.epoch == id, !self.connected else { return }
            self.fail("El servicio de voz no confirmó la sesión.")
        }
        do {
            try await send(LiveProtocol.start(model: model))
        } catch { if epoch == id { stop() }; throw error }
    }
    private func startAudio(epoch id: UUID) throws {
        guard epoch == id, !connected else { return }
        let stream = AsyncStream<Data>(bufferingPolicy: .bufferingNewest(16)) { frames = $0 }
        let continuation = frames!
        try audio.start(onPCM: { data in continuation.yield(data) }, onError: { [weak self] reason in
            Task { @MainActor in if self?.epoch == id { self?.fail(reason) } }
        })
        connected = true; timeout?.cancel(); timeout = nil
        onState?("Conversación en vivo")
        sender = Task { [weak self] in
            for await data in stream {
                guard let self, self.epoch == id, !Task.isCancelled else { return }
                do { try await self.send(["type": "session.input_audio.append", "audio": data.base64EncodedString()]) }
                catch { if self.epoch == id { self.fail("No pude enviar el audio. La conversación se cerró.") }; return }
            }
        }
        // A forgotten live session must not send the room indefinitely.
        lifetime = Task { [weak self] in
            do { try await Task.sleep(nanoseconds: 900_000_000_000) } catch { return }
            guard let self, self.epoch == id else { return }
            self.stop(); self.onState?("La sesión de voz terminó a los 15 minutos. Puedes reconectarla.")
        }
    }
    public func stop() {
        epoch = UUID(); connected = false
        frames?.finish(); frames = nil
        sender?.cancel(); sender = nil; receiver?.cancel(); receiver = nil
        timeout?.cancel(); timeout = nil; lifetime?.cancel(); lifetime = nil
        for task in toolTasks.values { task.cancel() }; toolTasks.removeAll()
        batches.removeAll(); seenCalls.removeAll(); responseIDs.removeAll(); apiKey = ""
        activeResponses.removeAll(); continuationNeeded = false
        audio.stop(); socket?.cancel(with: .normalClosure, reason: nil); socket = nil
        session?.invalidateAndCancel(); session = nil
    }
    public func text(_ text: String) async throws {
        guard connected else { throw AgentError.unavailable("La conversación todavía no está conectada.") }
        try await send(["type": "response.item.create", "item": ["type": "message", "role": "user", "content": [["type": "input_text", "text": text]]]])
        try await send(["type": "response.create"])
    }
    public func notify(_ text: String) async throws {
        try await send(["type": "session.commentary.append", "delegation_id": NSNull(), "content": String(text.prefix(1500))])
        // Wake the planner only after outstanding tool results have been returned.
        try await send(["type": "response.item.create", "item": ["type": "message", "role": "developer", "content": [["type": "input_text", "text": String(text.prefix(6000))]]]])
        continuationNeeded = true
        try await continueIfReady()
    }
    public func addImage(_ base64: String) async throws {
        guard connected, let png = Data(base64Encoded: base64), let session else { throw AgentError.unavailable("La voz no está conectada.") }
        let id = epoch, boundary = UUID().uuidString
        var body = Data("--\(boundary)\r\nContent-Disposition: form-data; name=\"purpose\"\r\n\r\nvision\r\n--\(boundary)\r\nContent-Disposition: form-data; name=\"file\"; filename=\"screen.png\"\r\nContent-Type: image/png\r\n\r\n".utf8)
        body.append(png); body.append(Data("\r\n--\(boundary)--\r\n".utf8))
        var request = URLRequest(url: URL(string: "https://api.openai.com/v1/files")!)
        request.httpMethod = "POST"; request.httpBody = body; request.timeoutInterval = 30
        request.setValue("Bearer \(apiKey)", forHTTPHeaderField: "Authorization")
        request.setValue("multipart/form-data; boundary=\(boundary)", forHTTPHeaderField: "Content-Type")
        let (data, response) = try await session.data(for: request)
        guard epoch == id, !Task.isCancelled else { throw CancellationError() }
        guard (response as? HTTPURLResponse)?.statusCode == 200,
              let result = try JSONSerialization.jsonObject(with: data) as? [String: Any], let file = result["id"] as? String else {
            throw AgentError.unavailable("No se pudo adjuntar la captura a Live 1.")
        }
        try await send(["type": "response.item.create", "item": ["type": "message", "role": "user", "content": [["type": "input_image", "file_id": file, "detail": "high"]]]])
    }
    private func send(_ object: [String: Any]) async throws {
        guard let socket else { throw CancellationError() }
        let bytes = try JSONSerialization.data(withJSONObject: object)
        try await socket.send(.string(String(decoding: bytes, as: UTF8.self)))
    }
    private func continueIfReady() async throws {
        guard continuationNeeded, activeResponses.isEmpty, batches.isEmpty else { return }
        continuationNeeded = false
        try await send(["type": "response.create"])
    }
    private func handle(_ bytes: Data, epoch id: UUID) async throws {
        guard let event = try JSONSerialization.jsonObject(with: bytes) as? [String: Any], let type = event["type"] as? String else { return }
        switch type {
        case "session.started": try startAudio(epoch: id)
        case "session.output_audio.delta":
            if let value = event["delta"] as? String, let data = Data(base64Encoded: value), data.contains(where: { $0 != 0 }) { try audio.play(data) }
        case "session.output_transcript.delta":
            if let text = event["delta"] as? String { onText?(text, false) }
        case "session.input_transcript.delta":
            if let text = event["delta"] as? String { onText?(text, true) }
        case "session.closed": fail("La sesión Live 1 se cerró.")
        case "response.event":
            guard let nested = event["event"] as? [String: Any] else { return }
            let delegation = event["delegation_id"] as? String ?? "default"
            if nested["type"] as? String == "response.created",
               let response = nested["response"] as? [String: Any], let responseID = response["id"] as? String {
                responseIDs[delegation] = responseID
                activeResponses.insert(responseID)
            }
            let response = responseIDs[delegation] ?? delegation
            if let call = LiveProtocol.call(in: nested), seenCalls.insert(call.id).inserted {
                guard batches[response, default: ToolBatch()].begin(call.id) else { return }
                toolTasks[call.id] = Task { [weak self] in
                    guard let self, self.epoch == id else { return }
                    let output: String
                    do {
                        let args = try LiveTools.parseArguments(call.arguments)
                        guard let tool = self.onTool else { throw AgentError.unavailable("El operador no está disponible.") }
                        output = try await tool(call.name, args)
                    } catch { output = "error: \(error.localizedDescription)" }
                    guard self.epoch == id, !Task.isCancelled else { return }
                    do {
                        try await self.send(LiveProtocol.output(call: call.id, text: output))
                        guard self.epoch == id, !Task.isCancelled else { return }
                        self.toolTasks.removeValue(forKey: call.id)
                        if self.batches[response, default: ToolBatch()].finish(call.id) {
                            self.batches.removeValue(forKey: response)
                            self.continuationNeeded = true
                            try await self.continueIfReady()
                        }
                    } catch { if self.epoch == id { self.fail("No pude devolver el resultado a Live 1.") } }
                }
            }
            if nested["type"] as? String == "response.completed" {
                activeResponses.remove(response)
                if var batch = batches[response] {
                    if batch.responseDone() {
                        batches.removeValue(forKey: response)
                        continuationNeeded = true
                    } else { batches[response] = batch }
                }
                try await continueIfReady()
            }
            if ["response.failed", "response.incomplete"].contains(nested["type"] as? String ?? "") {
                fail("Luna no pudo completar el turno delegado.")
            }
        case "error":
            // Do not log the raw response: it can include user data or authentication details.
            let code = (event["error"] as? [String: Any])?["code"] as? String ?? "unknown"
            if code != "response_cancel_not_active" { fail("El servicio de voz rechazó una operación (\(code)).") }
        default: break
        }
    }
    private func fail(_ reason: String) { stop(); onError?(reason) }
}
