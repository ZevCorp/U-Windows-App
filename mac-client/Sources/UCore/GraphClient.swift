import Foundation

public final class GraphClient: @unchecked Sendable {
    public static let defaultURL = "https://graph-eight-pied.vercel.app"
    private let base: URL
    private let apiKey: String
    private let transport: URLSession
    public init(baseURL: String = defaultURL, apiKey: String, transport: URLSession = .shared) throws {
        guard let url = URL(string: baseURL), url.scheme == "https", url.host != nil,
              url.user == nil, url.password == nil, url.query == nil, url.fragment == nil else {
            throw AgentError.invalid("La dirección de Graph debe ser HTTPS y no contener credenciales.")
        }
        self.base = url; self.apiKey = apiKey.trimmingCharacters(in: .whitespacesAndNewlines); self.transport = transport
    }
    public func request(path: String, body: Data? = nil) throws -> URLRequest {
        guard !apiKey.isEmpty else { throw AgentError.invalid("Añade tu credencial de Graph en Configuración.") }
        var request = URLRequest(url: base.appendingPathComponent("api/v1").appendingPathComponent(path))
        request.httpMethod = body == nil ? "GET" : "POST"
        request.httpBody = body
        request.timeoutInterval = 120
        request.setValue(apiKey, forHTTPHeaderField: "X-API-Key")
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue("mac_app", forHTTPHeaderField: "X-Miracle-App")
        request.setValue("conscious_bridge", forHTTPHeaderField: "X-Miracle-Feature")
        return request
    }
    public func data(path: String, body: Data? = nil) async throws -> Data {
        let (data, response) = try await transport.data(for: request(path: path, body: body))
        try Task.checkCancellation()
        guard let http = response as? HTTPURLResponse else { throw AgentError.unavailable("Graph no entregó una respuesta HTTP.") }
        guard (200..<300).contains(http.statusCode) else { throw AgentError.backend(http.statusCode) }
        return data
    }
    public func turn(_ value: TurnRequest) async throws -> TurnResponse {
        let bytes = try await data(path: "agent/turn", body: JSONEncoder().encode(value))
        let response = try JSONDecoder().decode(TurnResponse.self, from: bytes)
        if response.error != nil { throw AgentError.unavailable("Graph no pudo resolver este turno.") }
        return response
    }
    public func voiceKey() async throws -> String {
        struct Keys: Decodable { let openai: String? }
        let keys = try JSONDecoder().decode(Keys.self, from: await data(path: "agent/claves"))
        guard let key = keys.openai, !key.isEmpty else { throw AgentError.unavailable("Graph no tiene configurada la credencial de voz.") }
        return key
    }
}
