import AppKit
import Combine
import UCore
import UMac

struct ChatMessage: Identifiable { let id = UUID(); let text: String; let user: Bool }

@MainActor
final class AppModel: ObservableObject {
    enum Mode: String { case ready = "Lista", listening = "Te escucho", working = "Trabajando", speaking = "Hablando", question = "Necesito un dato", error = "Necesito atención" }
    @Published var mode: Mode = .ready
    @Published var status = "Dime qué necesitas hacer."
    @Published var draft = ""
    @Published var partial = ""
    @Published var messages: [ChatMessage] = []
    @Published var microphone = false
    @Published var busy = false
    @Published var liveConnected = false
    @Published var nativeDictation = UserDefaults.standard.bool(forKey: "nativeDictation")
    let liveVoice = LiveVoice()
    private var voiceConnection: Task<Void, Never>?
    private var voiceID = UUID()
    @Published var graphURL = UserDefaults.standard.string(forKey: "graphURL") ?? GraphClient.defaultURL
    @Published var credential = ""
    @Published var hasCredential = Credentials.read("GRAPH_API_KEY") != nil
    @Published var configurationMessage = ""
    @Published var permissionsVersion = 0
    @Published var selectedTab = 0
    let desktop = Desktop()
    let speech = Speech()
    var showWindow: (() -> Void)?
    var hideWindow: (() -> Void)?
    var lastExternalApp: NSRunningApplication?
    private var work: Task<Void, Never>?
    private var answer: CheckedContinuation<String, Error>?
    private var questionID = UUID()
    private var runID = UUID()
    private var awakeUntil = Date.distantPast
    private let userID: String = {
        if let id = UserDefaults.standard.string(forKey: "userID") { return id }
        let id = UUID().uuidString; UserDefaults.standard.set(id, forKey: "userID"); return id
    }()
    init() {
        speech.onPartial = { [weak self] text in self?.partial = text }
        speech.onText = { [weak self] text in self?.heard(text) }
        speech.onState = { [weak self] listening, speaking in
            guard let self else { return }
            if speaking { self.mode = .speaking }
            else if self.answer != nil { self.mode = .question }
            else if self.busy { self.mode = .working }
            else { self.mode = listening ? .listening : .ready }
        }
        speech.onError = { [weak self] text in self?.microphone = false; self?.fail(text) }
        liveVoice.onState = { [weak self] text in
            guard let self else { return }
            self.liveConnected = self.liveVoice.connected
            self.status = text
            self.mode = self.liveConnected ? .listening : .ready
            if !self.liveConnected && text.contains("terminó") { self.microphone = false; self.desktop.stop() }
        }
        liveVoice.onText = { [weak self] text, user in
            guard let self else { return }
            if user, self.answer != nil { self.submit(text) }
            else { self.append(text, user: user) }
        }
        liveVoice.onSpeaking = { [weak self] speaking in
            guard let self else { return }
            self.mode = speaking ? .speaking : self.busy ? .working : .listening
        }
        liveVoice.onError = { [weak self] text in
            guard let self else { return }
            self.desktop.stop(); self.busy = false; self.liveConnected = false; self.microphone = false
            self.fail(text + " Puedes volver a conectar o activar el dictado nativo en Configuración.")
        }
        liveVoice.onTool = { [weak self] name, args in
            guard let self else { throw CancellationError() }
            return try await self.liveTool(name, args: args)
        }
    }
    func refreshPermissions() { permissionsVersion += 1 }
    func saveConfiguration() {
        do {
            _ = try GraphClient(baseURL: graphURL, apiKey: "validation")
            if !credential.isEmpty { try Credentials.save("GRAPH_API_KEY", value: credential); credential = "" }
            UserDefaults.standard.set(graphURL, forKey: "graphURL")
            hasCredential = Credentials.read("GRAPH_API_KEY") != nil
            configurationMessage = hasCredential ? "Guardado en el Llavero de macOS." : "Falta la credencial de Graph."
        } catch { configurationMessage = error.localizedDescription }
    }
    func checkConnection() {
        saveConfiguration()
        Task {
            do {
                let client = try makeClient()
                _ = try await client.voiceKey()
                configurationMessage = "Graph conectado. Credencial de voz disponible."
            } catch { configurationMessage = error.localizedDescription }
        }
    }
    func makeClient() throws -> GraphClient { try GraphClient(baseURL: graphURL, apiKey: Credentials.read("GRAPH_API_KEY") ?? "") }
    func toggleMicrophone() {
        if microphone {
            voiceID = UUID(); voiceConnection?.cancel(); voiceConnection = nil
            microphone = false; liveConnected = false; liveVoice.stop(); speech.stop(); partial = ""
            if busy { stop() } else { mode = .ready }
            return
        }
        guard !busy else { status = "Detén la tarea antes de cambiar el modo de voz."; return }
        microphone = true; awakeUntil = Date().addingTimeInterval(45)
        if nativeDictation { Task { await speech.start() }; return }
        guard hasCredential else { microphone = false; selectedTab = 1; showWindow?(); fail("Conecta Graph para usar la voz en vivo."); return }
        let id = UUID(); voiceID = id
        hideWindow?(); lastExternalApp?.activate(options: [])
        desktop.begin()
        voiceConnection = Task { [weak self] in
            guard let self else { return }
            do {
                self.status = "Conectando la voz…"
                let key: String
                if let configured = Credentials.read("OPENAI_API_KEY") { key = configured }
                else { key = try await self.makeClient().voiceKey() }
                guard self.voiceID == id, !Task.isCancelled else { return }
                try await self.liveVoice.start(key: key, model: ProcessInfo.processInfo.environment["U_VOICE_MODEL"] ?? "gpt-realtime-2.1-mini")
            } catch {
                guard self.voiceID == id else { return }
                self.microphone = false; self.liveConnected = false; self.fail(error.localizedDescription)
            }
        }
    }
    private func liveTool(_ name: String, args: [String: String]) async throws -> String {
        if name == "stop_task" { stop(); return "Tarea detenida." }
        guard !busy else { throw AgentError.unavailable("Hay otra acción en curso. Espera su resultado antes de operar otra vez.") }
        busy = true; mode = .working
        let id = voiceID
        defer { if voiceID == id { busy = false; mode = answer == nil ? .listening : .question } }
        if name == "operate_computer" {
            let client = try makeClient()
            let engine = AgentEngine(turn: { try await client.turn($0) },
                observe: { try await self.desktop.observe(screenshot: $0) },
                execute: { try await self.desktop.execute($0) }, ask: { try await self.ask($0) })
            engine.userID = userID
            engine.onStatus = { [weak self] text in self?.status = text }
            let result = try await engine.run(goal: args["goal"] ?? "")
            try Task.checkCancellation()
            status = result
            return result
        }
        if name == "look" {
            let state = try await desktop.observe(screenshot: true)
            guard let screenshot = state.screenshot else { throw AgentError.unavailable("No se obtuvo una captura.") }
            try await liveVoice.addImage(screenshot)
            return "Captura adjunta. \(state.screen), \(state.width) × \(state.height)."
        }
        if name == "key" { return try await desktop.execute(AgentAction(kind: "key", key: args["key"])) }
        if name == "scroll" { return try await desktop.tool("map_scroll", args: args) }
        return try await desktop.tool(name, args: args)
    }
    private func heard(_ phrase: String) {
        partial = ""
        let trimmed = phrase.trimmingCharacters(in: .whitespacesAndNewlines)
        let folded = trimmed.folding(options: [.diacriticInsensitive, .caseInsensitive], locale: Locale(identifier: "es"))
        if ["detente", "para", "cancela", "cancelar", "alto"].contains(folded) { stop(); return }
        if answer != nil { submit(trimmed); return }
        let prefixes = ["oye u", "hola u", "oye ü", "hola ü", "u ", "ü "]
        if let prefix = prefixes.first(where: { trimmed.lowercased().hasPrefix($0) }) {
            awakeUntil = Date().addingTimeInterval(45)
            let command = String(trimmed.dropFirst(prefix.count)).trimmingCharacters(in: .whitespacesAndNewlines.union(.punctuationCharacters))
            if !command.isEmpty { submit(command) }
            return
        }
        guard Date() < awakeUntil else { return }
        guard !busy else { return }
        awakeUntil = Date().addingTimeInterval(45)
        submit(trimmed)
    }
    func submitDraft() { let text = draft; draft = ""; submit(text) }
    func submit(_ text: String) {
        let goal = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !goal.isEmpty else { return }
        append(goal, user: true)
        if let continuation = answer {
            answer = nil; mode = .working; hideWindow?()
            lastExternalApp?.activate(options: [])
            continuation.resume(returning: goal); return
        }
        if liveConnected {
            hideWindow?(); lastExternalApp?.activate(options: [])
            Task { do { try await liveVoice.text(goal) } catch { fail(error.localizedDescription) } }
            return
        }
        guard !busy else { append("Hay una tarea en curso. Deténla antes de iniciar otra."); return }
        guard hasCredential else {
            selectedTab = 1; showWindow?(); fail("Conecta tu cuenta de Graph para comenzar."); return
        }
        guard AccessibilityReader.trusted else {
            selectedTab = 1; showWindow?(); fail("Falta el permiso de Accesibilidad."); return
        }
        hideWindow?()
        if NSWorkspace.shared.frontmostApplication?.processIdentifier == getpid() { lastExternalApp?.activate(options: []) }
        let id = UUID(); runID = id
        busy = true; mode = .working; status = "Mirando la pantalla…"; desktop.begin()
        work = Task { [weak self] in
            guard let self else { return }
            defer { if self.runID == id { self.busy = false; self.work = nil } }
            do {
                // Give macOS the focus handoff after the command window closes.
                try await Task.sleep(nanoseconds: 250_000_000)
                let client = try self.makeClient()
                let engine = AgentEngine(turn: { try await client.turn($0) },
                    observe: { try await self.desktop.observe(screenshot: $0) },
                    execute: { try await self.desktop.execute($0) },
                    ask: { try await self.ask($0) })
                engine.userID = self.userID
                engine.onStatus = { [weak self] text in self?.status = text; self?.mode = .working }
                engine.onSpeech = { [weak self] text in self?.append(text); if self?.microphone == true { self?.speech.say(text) } }
                let result = try await engine.run(goal: goal)
                try Task.checkCancellation()
                guard self.runID == id else { return }
                self.status = result; self.mode = .ready; self.append(result)
                if self.microphone && !self.liveConnected { self.speech.say(result) }
            } catch is CancellationError {
                if self.runID == id { self.status = "Tarea detenida."; self.mode = .ready }
            } catch {
                if self.runID == id { self.fail(error.localizedDescription) }
            }
        }
    }
    private func ask(_ question: String) async throws -> String {
        try Task.checkCancellation()
        let pendingID = UUID(); questionID = pendingID
        mode = .question; status = question; append(question); showWindow?()
        if microphone && !liveConnected { speech.say(question) }
        return try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { continuation in
                if Task.isCancelled { continuation.resume(throwing: CancellationError()) }
                else { answer = continuation }
            }
        } onCancel: { [weak self] in
            Task { @MainActor in
                guard let self, self.questionID == pendingID else { return }
                self.answer?.resume(throwing: CancellationError()); self.answer = nil
            }
        }
    }
    func stop() {
        voiceID = UUID(); voiceConnection?.cancel(); voiceConnection = nil
        liveVoice.stop(); liveConnected = false
        desktop.stop(); work?.cancel(); work = nil; runID = UUID()
        answer?.resume(throwing: CancellationError()); answer = nil
        busy = false; mode = .ready; status = "Tarea detenida."; partial = ""
        speech.stop(); microphone = false
    }
    func fail(_ text: String) { mode = .error; status = text; append(text) }
    func append(_ text: String, user: Bool = false) {
        messages.append(ChatMessage(text: text, user: user))
        if messages.count > 150 { messages.removeFirst(messages.count - 150) }
    }
}
