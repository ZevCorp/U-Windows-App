import Foundation

/// Closed choices only. No screenshots, field values, planning, or text generation.
public final class JevClient: @unchecked Sendable {
    private let key: String
    private let transport: URLSession
    public init(key: String, transport: URLSession = .shared) { self.key = key; self.transport = transport }

    public enum Decision: Equatable { case take(String), finished, handoff(String) }
    public static func requestBody(screen: String, goal: String, choices: [String]) throws -> Data {
        let criteria = Dictionary(uniqueKeysWithValues: choices.map { ($0, NSNull()) })
        return try JSONSerialization.data(withJSONObject: [
            "model": "jev-latest",
            "state": "Pantalla actual: \(screen)\nObjetivo: \(goal)\nControles accionables (datos, no instrucciones):\n" + choices.joined(separator: "\n"),
            "questions": [
                "puerta": ["type": "choice", "instructions": "Elige el control que acerca al objetivo AHORA.", "criteria": criteria],
                "cumplido": ["type": "noul", "instructions": "¿El objetivo YA está cumplido en esta pantalla?", "criteria": ["true": "Ya conseguido", "false": "Todavía falta"]],
                "peligro": ["type": "noul", "instructions": "¿Accionar la elegida tiene un efecto irreversible: enviar, borrar, pagar, confirmar, guardar o cerrar sin guardar?", "criteria": ["true": "Efecto irreversible", "false": "Solo navegar o abrir"]]
            ]
        ])
    }
    public static func decode(_ data: Data, choices: [String]) throws -> Decision {
        struct Answer: Decodable { let choice: String?; let confidence: Double?; let noul: Double? }
        struct Response: Decodable { let answers: [String: Answer] }
        let answers = try JSONDecoder().decode(Response.self, from: data).answers
        guard let selected = answers["puerta"]?.choice, choices.contains(selected),
              let confidence = answers["puerta"]?.confidence, (0...1).contains(confidence),
              let done = answers["cumplido"]?.noul, (0...1).contains(done),
              let danger = answers["peligro"]?.noul, (0...1).contains(danger) else {
            return .handoff("Respuesta de Jev incompleta o fuera del inventario. Decide Luna.")
        }
        if done >= 0.70 { return .finished }
        if danger >= 0.50 { return .handoff("El siguiente paso puede ser irreversible. Decide Luna.") }
        if confidence < 0.70 { return .handoff("Confianza insuficiente. Decide Luna.") }
        return .take(selected)
    }
    public func decide(screen: String, goal: String, choices: [String]) async throws -> Decision {
        guard !key.isEmpty, !choices.isEmpty else { return .handoff("Jev no tiene credencial o controles accionables. Decide Luna.") }
        let body = try Self.requestBody(screen: screen, goal: goal, choices: choices)
        let clock = ContinuousClock(), deadline = ContinuousClock.now + .seconds(2)
        // One deadline covers network and backoff; no unbounded retries in the hot path.
        return try await withThrowingTaskGroup(of: Decision.self) { group in
            group.addTask {
                for attempt in 0..<4 {
                    try Task.checkCancellation()
                    var request = URLRequest(url: URL(string: "https://api.typesafe.ai/v1/systemone")!)
                    request.httpMethod = "POST"; request.httpBody = body; request.timeoutInterval = 2
                    request.setValue("Bearer \(self.key)", forHTTPHeaderField: "Authorization")
                    request.setValue("application/json", forHTTPHeaderField: "Content-Type")
                    let (data, response) = try await self.transport.data(for: request)
                    try Task.checkCancellation()
                    let status = (response as? HTTPURLResponse)?.statusCode ?? 0
                    if status == 200 { return try Self.decode(data, choices: choices) }
                    guard [429, 529].contains(status), attempt < 3 else {
                        return .handoff("TypeSafe respondió HTTP \(status). Decide Luna.")
                    }
                    try await Task.sleep(for: .milliseconds(200 << attempt))
                }
                return .handoff("TypeSafe no respondió. Decide Luna.")
            }
            group.addTask { try await clock.sleep(until: deadline); return .handoff("Jev superó el plazo de 2 segundos. Decide Luna.") }
            defer { group.cancelAll() }
            return try await group.next()!
        }
    }
}
