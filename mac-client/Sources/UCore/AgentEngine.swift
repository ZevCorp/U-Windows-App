import Foundation

@MainActor
public final class AgentEngine {
    public typealias Turn = (TurnRequest) async throws -> TurnResponse
    private let turn: Turn
    private let observe: (Bool) async throws -> ScreenState
    private let execute: (AgentAction) async throws -> String
    private let ask: (String) async throws -> String
    private let maxTurns: Int
    public var onStatus: ((String) -> Void)?
    public var onSpeech: ((String) -> Void)?
    public var userID: String?
    public init(turn: @escaping Turn, observe: @escaping (Bool) async throws -> ScreenState,
                execute: @escaping (AgentAction) async throws -> String,
                ask: @escaping (String) async throws -> String, maxTurns: Int = 40) {
        self.turn = turn; self.observe = observe; self.execute = execute; self.ask = ask; self.maxTurns = maxTurns
    }
    public func run(goal: String) async throws -> String {
        guard !goal.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { throw AgentError.invalid("Escribe o di qué necesitas.") }
        var session: String?, inform: String?
        var results: [String] = []
        var screenshot = false
        var lastFailed = false
        for index in 0..<maxTurns {
            try Task.checkCancellation()
            onStatus?("Mirando la pantalla · \(index + 1)")
            let state = try await observe(screenshot)
            let response = try await turn(TurnRequest(session: session, goal: index == 0 ? goal : nil, userId: userID, state: state, results: results, inform: inform))
            try Task.checkCancellation()
            if response.done {
                guard response.actions.isEmpty, response.question == nil else { throw AgentError.invalid("Graph terminó el turno con acciones pendientes.") }
                guard !lastFailed else { throw AgentError.unavailable("La última acción falló; no puedo confirmar que la tarea esté terminada.") }
                return response.text.isEmpty ? "Tarea terminada." : response.text
            }
            guard !response.session.isEmpty else { throw AgentError.invalid("Graph no devolvió una sesión para continuar.") }
            session = response.session; screenshot = response.needsScreenshot; inform = nil; results = []; lastFailed = false
            if !response.narration.isEmpty { onStatus?(response.narration) }
            if let speech = response.speech, !speech.isEmpty { onSpeech?(speech) }
            for action in response.actions {
                try Task.checkCancellation()
                do { results.append(try await execute(action)) }
                catch is CancellationError { throw CancellationError() }
                catch AgentError.stopped { throw AgentError.stopped }
                catch {
                    lastFailed = true
                    results.append("error: \(error.localizedDescription)")
                    // Subsequent actions may depend on the failed one. Never blindly continue a batch.
                    results += Array(repeating: "omitida: falló una acción anterior; observa y vuelve a planificar", count: response.actions.count - results.count)
                    break
                }
            }
            if let question = response.question, !question.isEmpty { inform = try await ask(question) }
            try await Task.sleep(nanoseconds: 150_000_000)
        }
        throw AgentError.turnLimit
    }
}
