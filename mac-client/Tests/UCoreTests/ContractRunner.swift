import Foundation

private var checks = 0
func XCTFail(_ message: String, file: StaticString = #filePath, line: UInt = #line) { fatalError("\(file):\(line): \(message)") }
func XCTAssertEqual<T: Equatable>(_ a: T, _ b: T, file: StaticString = #filePath, line: UInt = #line) {
    checks += 1
    if a != b { XCTFail("\(a) != \(b)", file: file, line: line) }
}
func XCTAssertNil<T>(_ value: T?, file: StaticString = #filePath, line: UInt = #line) {
    checks += 1
    if value != nil { XCTFail("Expected nil", file: file, line: line) }
}
func XCTAssertThrowsError<T>(_ work: @autoclosure () throws -> T, file: StaticString = #filePath, line: UInt = #line) {
    checks += 1
    do { _ = try work(); XCTFail("Expected error", file: file, line: line) } catch {}
}
@main
struct ContractRunner {
    @MainActor static func main() async throws {
        let tests = AgentTests()
        try await tests.testGraphWireContractAndQuestionContinuation()
        try await tests.testTurnLimitNeverReportsSuccess()
        try await tests.testCancellationPreventsActionsAfterNetworkReturns()
        try tests.testMalformedActionsDoNotClickOrigin()
        try tests.testRetinaAndSecondaryDisplayCoordinates()
        try tests.testStopGateAndStaleObservation()
        try tests.testGraphRequiresHTTPSAndKeepsCredentialsOutOfURL()
        try tests.testAmbiguousLabelsRequireDisambiguation()
        try tests.testToolBatchWaitsForEveryResultAndOnlyContinuesOnce()
        try await tests.testFailedActionStopsDependentBatchAndCannotBecomeSuccess()
        try await tests.testHTTPAuthenticationErrorsAndCredentialFetch()
        try tests.testMultichannelMicrophoneProducesReal24kPCM()
        try tests.testLiveOneWireAndUTF8Limit()
        try tests.testJevClosedChoicesAndHandoff()
        try await tests.testJevHTTPDeadlineRetryAndCancellation()
        print("PASS: 15 contracts, \(checks) assertions. No network, microphone or desktop access.")
    }
}
