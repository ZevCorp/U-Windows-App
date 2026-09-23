import Foundation
import UCore

extension AgentTests {
    @MainActor func testJevHTTPDeadlineRetryAndCancellation() async throws {
        let config = URLSessionConfiguration.ephemeral; config.protocolClasses = [MockGraphProtocol.self]
        let session = URLSession(configuration: config)
        let client = JevClient(key: "mock-key", transport: session)
        defer { session.invalidateAndCancel(); MockGraphProtocol.respond = nil; MockGraphProtocol.delay = 0 }
        var calls = 0
        MockGraphProtocol.respond = { request in
            calls += 1
            XCTAssertEqual(request.url?.path, "/v1/systemone")
            XCTAssertEqual(request.value(forHTTPHeaderField: "Authorization"), "Bearer mock-key")
            return (calls == 1 ? 429 : 200, Data(#"{"answers":{"puerta":{"choice":"A","confidence":0.9},"cumplido":{"noul":0},"peligro":{"noul":0}}}"#.utf8))
        }
        XCTAssertEqual(try await client.decide(screen: "Fixture", goal: "A", choices: ["A"]), .take("A"))
        XCTAssertEqual(calls, 2)
        calls = 0
        MockGraphProtocol.respond = { _ in calls += 1; return (401, Data()) }
        if case .handoff = try await client.decide(screen: "Fixture", goal: "A", choices: ["A"]) {} else { XCTFail("401 accepted") }
        XCTAssertEqual(calls, 1)
        MockGraphProtocol.delay = 5
        let started = ContinuousClock.now
        if case .handoff = try await client.decide(screen: "Fixture", goal: "A", choices: ["A"]) {} else { XCTFail("Deadline ignored") }
        XCTAssertEqual(started.duration(to: .now) < .seconds(3), true)
        let pending = Task { try await client.decide(screen: "Fixture", goal: "A", choices: ["A"]) }
        pending.cancel()
        do { _ = try await pending.value; XCTFail("Cancellation ignored") } catch {}
    }
    func testLiveOneWireAndUTF8Limit() throws {
        let start = LiveProtocol.start()
        XCTAssertEqual(start["type"] as? String, "session.start")
        let session = start["session"] as! [String: Any]
        XCTAssertEqual(session["model"] as? String, "gpt-live-1")
        let delegation = session["delegation"] as! [String: Any]
        let responses = delegation["responses"] as! [String: Any]
        XCTAssertEqual(responses["model"] as? String, "gpt-5.6-luna")
        XCTAssertEqual(responses["parallel_tool_calls"] as? Bool, false)
        let item: [String: Any] = ["type": "function_call", "call_id": "c1", "name": "map_tramo", "arguments": "{}"]
        XCTAssertNil(LiveProtocol.call(in: ["type": "response.output_item.added", "item": item]))
        XCTAssertNil(LiveProtocol.call(in: ["type": "response.function_call_arguments.done", "item": item]))
        XCTAssertEqual(LiveProtocol.call(in: ["type": "response.output_item.done", "item": item])?.id, "c1")
        let output = try LiveProtocol.output(call: "c1", text: String(repeating: "👨‍👩‍👧‍👦\n\"é", count: 9000))
        let bytes = try JSONSerialization.data(withJSONObject: output)
        XCTAssertEqual(bytes.count <= 32768, true)
        let decoded = try JSONSerialization.jsonObject(with: bytes) as! [String: Any]
        XCTAssertEqual(decoded["type"] as? String, "response.item.create")
    }
    func testJevClosedChoicesAndHandoff() throws {
        let choices = ["1) Abrir (AXButton)", "2) Abrir (AXButton)"]
        func answer(_ choice: String, _ confidence: Double, _ done: Double, _ danger: Double) throws -> Data {
            try JSONSerialization.data(withJSONObject: ["answers": ["puerta": ["choice": choice, "confidence": confidence], "cumplido": ["noul": done], "peligro": ["noul": danger]]])
        }
        XCTAssertEqual(try JevClient.decode(answer(choices[1], 0.70, 0.1, 0.1), choices: choices), .take(choices[1]))
        XCTAssertEqual(try JevClient.decode(answer(choices[0], 0.9, 0.70, 0.1), choices: choices), .finished)
        for data in [try answer("inventado", 1, 0, 0), try answer(choices[0], 0.69, 0, 0), try answer(choices[0], 1, 0, 0.5), Data(#"{"answers":{"puerta":{"choice":"1) Abrir (AXButton)","confidence":1}}}"#.utf8)] {
            if case .handoff = try JevClient.decode(data, choices: choices) {} else { XCTFail("Unsafe decision accepted") }
        }
        let body = try JSONSerialization.jsonObject(with: JevClient.requestBody(screen: "Fixture", goal: "Abrir", choices: choices)) as! [String: Any]
        XCTAssertEqual(body["model"] as? String, "jev-latest")
        let questions = body["questions"] as! [String: Any]
        XCTAssertEqual(questions.count, 3)
    }
}
