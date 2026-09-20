import Foundation
import UCore

@MainActor
final class AgentTests {
    func testGraphWireContractAndQuestionContinuation() async throws {
        let wire = Data(#"{"session":"opaque","actions":[{"kind":"mcp","tool":"launch_app","args":{"app":"Safari"}}],"question":"¿Qué sitio?","done":false,"needsScreenshot":true}"#.utf8)
        let first = try JSONDecoder().decode(TurnResponse.self, from: wire)
        var requests: [TurnRequest] = []
        var shots: [Bool] = []
        let engine = AgentEngine(
            turn: { request in
                requests.append(request)
                return requests.count == 1 ? first : TurnResponse(session: "opaque2", done: true, text: "Abierto")
            },
            observe: { shot in shots.append(shot); return ScreenState(screen: "Safari", uiContext: "AXLink Gmail", width: 1440, height: 900) },
            execute: { action in XCTAssertEqual(action.tool, "launch_app"); return "ok" },
            ask: { question in XCTAssertEqual(question, "¿Qué sitio?"); return "apple.com" }
        )
        let result = try await engine.run(goal: "Abre Safari")
        XCTAssertEqual(result, "Abierto")
        XCTAssertEqual(shots, [false, true])
        XCTAssertEqual(requests[0].goal, "Abre Safari")
        XCTAssertNil(requests[1].goal)
        XCTAssertEqual(requests[1].session, "opaque")
        XCTAssertEqual(requests[1].results, ["ok"])
        XCTAssertEqual(requests[1].inform, "apple.com")
        let json = try JSONSerialization.jsonObject(with: JSONEncoder().encode(requests[1])) as! [String: Any]
        XCTAssertNil(json["goal"])
        XCTAssertEqual((json["state"] as? [String: Any])?["platform"] as? String, "macos")
    }

    func testTurnLimitNeverReportsSuccess() async throws {
        let engine = AgentEngine(turn: { _ in TurnResponse(session: "pending") }, observe: { _ in .empty }, execute: { _ in "ok" }, ask: { _ in "" }, maxTurns: 2)
        do { _ = try await engine.run(goal: "loop"); XCTFail("Must stop as incomplete") }
        catch { XCTAssertEqual(error as? AgentError, .turnLimit) }
    }

    func testCancellationPreventsActionsAfterNetworkReturns() async throws {
        var actions = 0
        let task = Task {
            let engine = AgentEngine(turn: { _ in
                try? await Task.sleep(nanoseconds: 200_000_000)
                return TurnResponse(session: "s", actions: [AgentAction(kind: "tap", x: 4, y: 8)])
            }, observe: { _ in .empty }, execute: { _ in actions += 1; return "ok" }, ask: { _ in "" })
            return try await engine.run(goal: "cancel")
        }
        await Task.yield()
        task.cancel()
        do { _ = try await task.value; XCTFail("Cancelled task completed") } catch is CancellationError {} catch { XCTFail("\(error)") }
        XCTAssertEqual(actions, 0)
    }

    func testMalformedActionsDoNotClickOrigin() throws {
        let a = try JSONDecoder().decode(AgentAction.self, from: Data(#"{"kind":"tap"}"#.utf8))
        XCTAssertThrowsError(try a.validatedPoint())
        XCTAssertThrowsError(try AgentAction(kind: "tap", x: -1, y: 0).validatedPoint())
    }

    func testRetinaAndSecondaryDisplayCoordinates() throws {
        let space = ScreenGeometry(originX: -1920, originY: 120, width: 1920, height: 1080)
        XCTAssertEqual(try space.absolute(x: 960, y: 540).x, -960)
        XCTAssertEqual(try space.absolute(x: 960, y: 540).y, 660)
        XCTAssertThrowsError(try space.absolute(x: 1920, y: 20))
        XCTAssertThrowsError(try space.absolute(x: .nan, y: 20))
    }

    func testStopGateAndStaleObservation() throws {
        let gate = ActionGate()
        let generation = gate.begin()
        try gate.check(generation: generation, expectedPID: 20, currentPID: 20)
        XCTAssertThrowsError(try gate.check(generation: generation, expectedPID: 20, currentPID: 21))
        gate.stop()
        XCTAssertThrowsError(try gate.check(generation: generation))
        _ = gate.begin()
        XCTAssertThrowsError(try gate.check(generation: generation))
    }

    func testGraphRequiresHTTPSAndKeepsCredentialsOutOfURL() throws {
        XCTAssertThrowsError(try GraphClient(baseURL: "http://example.com", apiKey: "secret"))
        let client = try GraphClient(baseURL: "https://example.com/", apiKey: "secret")
        let request = try client.request(path: "agent/turn", body: Data("{}".utf8))
        XCTAssertEqual(request.url?.absoluteString, "https://example.com/api/v1/agent/turn")
        XCTAssertEqual(request.value(forHTTPHeaderField: "X-API-Key"), "secret")
        XCTAssertEqual(request.value(forHTTPHeaderField: "X-Miracle-App"), "mac_app")
    }

    func testAmbiguousLabelsRequireDisambiguation() throws {
        let targets = [AXTarget(id: "1", role: "AXButton", label: "Enviar"), AXTarget(id: "2", role: "AXButton", label: "Enviar")]
        XCTAssertThrowsError(try TargetResolver.resolve("Enviar", in: targets))
        XCTAssertEqual(try TargetResolver.resolve("2", in: targets).id, "2")
        XCTAssertThrowsError(try TargetResolver.resolve("Envia", in: targets))
    }
}

extension AgentTests {
    func testToolBatchWaitsForEveryResultAndOnlyContinuesOnce() throws {
        var batch = ToolBatch()
        XCTAssertEqual(batch.begin("one"), true)
        XCTAssertEqual(batch.begin("two"), true)
        XCTAssertEqual(batch.begin("one"), false)
        XCTAssertEqual(batch.responseDone(), false)
        XCTAssertEqual(batch.finish("two"), false)
        XCTAssertEqual(batch.finish("one"), true)
        XCTAssertEqual(batch.responseDone(), false)
        XCTAssertEqual(batch.begin("three"), true)
        XCTAssertEqual(batch.finish("three"), false)
        XCTAssertEqual(batch.responseDone(), true)
        XCTAssertThrowsError(try LiveTools.parseArguments(#"{"key": 42}"#))
        XCTAssertEqual(try LiveTools.parseArguments(#"{"key":"cmd+l"}"#), ["key": "cmd+l"])
    }
    func testFailedActionStopsDependentBatchAndCannotBecomeSuccess() async throws {
        var turns = 0, performed = 0
        let engine = AgentEngine(turn: { request in
            turns += 1
            if turns == 1 { return TurnResponse(session: "a", actions: [AgentAction(kind: "tap"), AgentAction(kind: "type", text: "wrong place")]) }
            XCTAssertEqual(request.results.count, 2)
            XCTAssertEqual(request.results[1].hasPrefix("omitida:"), true)
            return TurnResponse(session: "b", done: true, text: "Done")
        }, observe: { _ in .empty }, execute: { _ in performed += 1; throw AgentError.staleFocus }, ask: { _ in "" })
        do { _ = try await engine.run(goal: "fail"); XCTFail("A failed action was reported successful") } catch {}
        XCTAssertEqual(performed, 1)
    }
}
