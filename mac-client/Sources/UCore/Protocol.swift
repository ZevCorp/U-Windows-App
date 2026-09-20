import Foundation

public enum AgentError: Error, LocalizedError, Equatable {
    case invalid(String), permission(String), unavailable(String), backend(Int), turnLimit, stopped, staleFocus, ambiguous(String)
    public var errorDescription: String? {
        switch self {
        case .invalid(let s), .unavailable(let s): return s
        case .permission(let s): return "Activa \(s) para Ü en Ajustes del Sistema → Privacidad y seguridad."
        case .backend(let code): return code == 401 || code == 403 ? "Graph rechazó la credencial. Revísala en Configuración." : "Graph respondió HTTP \(code)."
        case .turnLimit: return "Me detuve antes de terminar: la tarea superó el límite de pasos."
        case .stopped: return "Tarea detenida."
        case .staleFocus: return "La aplicación cambió desde la última observación. Vuelve a mirar antes de actuar."
        case .ambiguous(let s): return "Hay varios controles llamados «\(s)». Usa el identificador exacto."
        }
    }
}

public struct ScreenState: Codable, Sendable {
    public var screen: String
    public var uiContext: String
    public var width: Int
    public var height: Int
    public var screenshot: String?
    public var apps: [String]?
    public var surfaceId: String?
    public var surfaceOrigin: String?
    public var surfacePathname: String?
    public var platform = "macos"
    public init(screen: String, uiContext: String, width: Int, height: Int) {
        self.screen = screen; self.uiContext = uiContext; self.width = width; self.height = height
    }
    public static var empty: Self { .init(screen: "", uiContext: "", width: 1, height: 1) }
}

public struct TurnRequest: Codable, Sendable {
    public var session: String?
    public var goal: String?
    public var userId: String?
    public var state: ScreenState
    public var results: [String]
    public var inform: String?
    public init(session: String?, goal: String?, userId: String? = nil, state: ScreenState, results: [String] = [], inform: String? = nil) {
        self.session = session; self.goal = goal; self.userId = userId; self.state = state; self.results = results; self.inform = inform
    }
}

public struct AgentAction: Codable, Sendable {
    public var kind: String
    public var x: Double?, y: Double?, x1: Double?, y1: Double?, x2: Double?, y2: Double?
    public var ms: Int?
    public var text: String?, key: String?, tool: String?
    public var down: Bool?
    public var args: [String: String]?
    public init(kind: String, x: Double? = nil, y: Double? = nil, text: String? = nil, key: String? = nil, tool: String? = nil, args: [String: String]? = nil) {
        self.kind = kind; self.x = x; self.y = y; self.text = text; self.key = key; self.tool = tool; self.args = args
    }
    public func validatedPoint() throws -> (x: Double, y: Double) {
        guard let x, let y, x.isFinite, y.isFinite, x >= 0, y >= 0 else { throw AgentError.invalid("La acción no contiene coordenadas válidas.") }
        return (x, y)
    }
}

public struct TurnResponse: Decodable, Sendable {
    public var session: String
    public var actions: [AgentAction]
    public var question: String?
    public var done: Bool
    public var text: String
    public var needsScreenshot: Bool
    public var narration: String
    public var speech: String?
    public var error: String?
    public init(session: String, actions: [AgentAction] = [], done: Bool = false, text: String = "") {
        self.session = session; self.actions = actions; self.done = done; self.text = text; needsScreenshot = false; narration = ""
    }
    enum CodingKeys: String, CodingKey { case session, actions, question, done, text, needsScreenshot, narration, speech, error }
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        session = try c.decodeIfPresent(String.self, forKey: .session) ?? ""
        actions = try c.decodeIfPresent([AgentAction].self, forKey: .actions) ?? []
        question = try c.decodeIfPresent(String.self, forKey: .question)
        done = try c.decodeIfPresent(Bool.self, forKey: .done) ?? false
        text = try c.decodeIfPresent(String.self, forKey: .text) ?? ""
        needsScreenshot = try c.decodeIfPresent(Bool.self, forKey: .needsScreenshot) ?? false
        narration = try c.decodeIfPresent(String.self, forKey: .narration) ?? ""
        speech = try c.decodeIfPresent(String.self, forKey: .speech)
        error = try c.decodeIfPresent(String.self, forKey: .error)
    }
}

public struct ScreenGeometry: Sendable, Equatable {
    public let originX: Double, originY: Double, width: Double, height: Double
    public init(originX: Double, originY: Double, width: Double, height: Double) {
        self.originX = originX; self.originY = originY; self.width = width; self.height = height
    }
    public func absolute(x: Double, y: Double) throws -> (x: Double, y: Double) {
        guard x.isFinite, y.isFinite, x >= 0, y >= 0, x < width, y < height else { throw AgentError.invalid("Coordenadas fuera de la pantalla observada.") }
        return (originX + x, originY + y)
    }
}

public struct AXTarget: Sendable {
    public let id: String, role: String, label: String
    public init(id: String, role: String, label: String) { self.id = id; self.role = role; self.label = label }
}
public enum TargetResolver {
    public static func resolve(_ query: String, in targets: [AXTarget]) throws -> AXTarget {
        if let exact = targets.first(where: { $0.id == query }) { return exact }
        let matches = targets.filter { $0.label.compare(query, options: [.caseInsensitive, .diacriticInsensitive]) == .orderedSame }
        guard matches.count < 2 else { throw AgentError.ambiguous(query) }
        guard let match = matches.first else { throw AgentError.unavailable("No encontré «\(query)» en la pantalla observada.") }
        return match
    }
}

/// All input paths share this generation check; cancelling invalidates in-flight work permanently.
public final class ActionGate: @unchecked Sendable {
    private let lock = NSLock()
    private var generation: UInt64 = 0
    private var stopped = true
    public init() {}
    @discardableResult public func begin() -> UInt64 {
        lock.lock(); defer { lock.unlock() }
        generation &+= 1; stopped = false; return generation
    }
    public func stop() { lock.lock(); stopped = true; generation &+= 1; lock.unlock() }
    public func check(generation expected: UInt64, expectedPID: Int32? = nil, currentPID: Int32? = nil) throws {
        lock.lock(); defer { lock.unlock() }
        guard !stopped, expected == generation else { throw AgentError.stopped }
        if let pid = expectedPID, pid != currentPID { throw AgentError.staleFocus }
    }
}
