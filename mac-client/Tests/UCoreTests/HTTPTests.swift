import Foundation
import UCore

final class MockGraphProtocol: URLProtocol, @unchecked Sendable {
    static var respond: ((URLRequest) -> (Int, Data))?
    static var delay: TimeInterval = 0
    private var delivery: DispatchWorkItem?
    override class func canInit(with request: URLRequest) -> Bool { ["test.invalid", "api.typesafe.ai"].contains(request.url?.host ?? "") }
    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }
    override func startLoading() {
        guard let respond = Self.respond else { fatalError("Missing HTTP fixture") }
        let (status, data) = respond(request)
        let response = HTTPURLResponse(url: request.url!, statusCode: status, httpVersion: "HTTP/1.1", headerFields: ["Content-Type": "application/json"])!
        let item = DispatchWorkItem { [weak self] in
            guard let self else { return }
            self.client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
            self.client?.urlProtocol(self, didLoad: data)
            self.client?.urlProtocolDidFinishLoading(self)
        }
        delivery = item
        DispatchQueue.main.asyncAfter(deadline: .now() + Self.delay, execute: item)
    }
    override func stopLoading() { delivery?.cancel() }
}

extension AgentTests {
    func testHTTPAuthenticationErrorsAndCredentialFetch() async throws {
        let config = URLSessionConfiguration.ephemeral; config.protocolClasses = [MockGraphProtocol.self]
        let session = URLSession(configuration: config)
        defer { session.invalidateAndCancel(); MockGraphProtocol.respond = nil }
        let client = try GraphClient(baseURL: "https://test.invalid", apiKey: "test-only", transport: session)
        MockGraphProtocol.respond = { request in
            XCTAssertEqual(request.value(forHTTPHeaderField: "X-API-Key"), "test-only")
            return (401, Data("not JSON, must still report authentication".utf8))
        }
        do { _ = try await client.turn(TurnRequest(session: nil, goal: "test", state: .empty)); XCTFail("401 was accepted") }
        catch { XCTAssertEqual(error as? AgentError, .backend(401)) }
        MockGraphProtocol.respond = { request in
            XCTAssertEqual(request.url?.path, "/api/v1/agent/claves")
            return (200, Data(#"{"openai":"mock-key"}"#.utf8))
        }
        XCTAssertEqual(try await client.voiceKey(), "mock-key")
        MockGraphProtocol.respond = { _ in (200, Data(#"{"session":"s","done":true,"text":"Test completed"}"#.utf8)) }
        XCTAssertEqual(try await client.turn(TurnRequest(session: nil, goal: "test", state: .empty)).text, "Test completed")
    }
}
