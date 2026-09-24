import AppKit
import ApplicationServices
import UCore

@MainActor
public final class InputDriver {
    public static let syntheticEventTag: Int64 = 0x554D4143
    private let gate: ActionGate
    public var onClick: ((CGPoint) -> Void)?
    public init(gate: ActionGate) { self.gate = gate }
    public func check(_ generation: UInt64, pid: pid_t? = nil) throws {
        try Task.checkCancellation()
        try gate.check(generation: generation, expectedPID: pid, currentPID: NSWorkspace.shared.frontmostApplication?.processIdentifier)
        guard AccessibilityReader.trusted else { throw AgentError.permission("Accesibilidad") }
    }
    private func mouse(_ type: CGEventType, _ point: CGPoint, button: CGMouseButton = .left, clicks: Int64 = 1) throws -> CGEvent {
        guard let event = CGEvent(mouseEventSource: nil, mouseType: type, mouseCursorPosition: point, mouseButton: button) else { throw AgentError.unavailable("No pude crear el evento de ratón.") }
        event.setIntegerValueField(.eventSourceUserData, value: Self.syntheticEventTag)
        event.setIntegerValueField(.mouseEventClickState, value: clicks)
        return event
    }
    public func click(_ point: CGPoint, generation: UInt64, pid: pid_t, right: Bool = false, double: Bool = false) async throws {
        try check(generation, pid: pid)
        onClick?(point)
        let button: CGMouseButton = right ? .right : .left
        for number in 1...(double ? 2 : 1) {
            try check(generation, pid: pid)
            let down = try mouse(right ? .rightMouseDown : .leftMouseDown, point, button: button, clicks: Int64(number))
            let up = try mouse(right ? .rightMouseUp : .leftMouseUp, point, button: button, clicks: Int64(number))
            down.post(tap: .cghidEventTap)
            // Release always, including cancellation: never leave the user's mouse button held.
            defer { up.post(tap: .cghidEventTap) }
            try await Task.sleep(nanoseconds: 35_000_000)
        }
    }
    public func type(_ text: String, generation: UInt64, pid: pid_t) async throws {
        guard text.utf16.count <= 100_000 else { throw AgentError.invalid("El texto supera el límite de una acción.") }
        // Character boundaries preserve surrogate pairs and composed characters. Clipboard is untouched.
        for character in text {
            try check(generation, pid: pid)
            let units = Array(String(character).utf16)
            guard let down = CGEvent(keyboardEventSource: nil, virtualKey: 0, keyDown: true),
                  let up = CGEvent(keyboardEventSource: nil, virtualKey: 0, keyDown: false) else { throw AgentError.unavailable("No pude crear el evento de teclado.") }
            units.withUnsafeBufferPointer {
                down.keyboardSetUnicodeString(stringLength: units.count, unicodeString: $0.baseAddress!)
                up.keyboardSetUnicodeString(stringLength: units.count, unicodeString: $0.baseAddress!)
            }
            down.setIntegerValueField(.eventSourceUserData, value: Self.syntheticEventTag)
            up.setIntegerValueField(.eventSourceUserData, value: Self.syntheticEventTag)
            down.post(tap: .cghidEventTap); up.post(tap: .cghidEventTap)
            try await Task.sleep(nanoseconds: 1_000_000)
        }
    }
    public func key(_ description: String, generation: UInt64, pid: pid_t? = nil) throws {
        try check(generation, pid: pid)
        let parts = description.lowercased().split(separator: "+").map(String.init)
        guard let key = parts.last, let code = Self.codes[key] else { throw AgentError.invalid("Tecla no reconocida: \(description)") }
        var flags: CGEventFlags = []
        for modifier in parts.dropLast() {
            switch modifier {
            case "cmd", "command", "super", "win": flags.insert(.maskCommand)
            case "ctrl", "control": flags.insert(.maskControl)
            case "alt", "option": flags.insert(.maskAlternate)
            case "shift": flags.insert(.maskShift)
            default: throw AgentError.invalid("Modificador no reconocido: \(modifier)")
            }
        }
        guard let down = CGEvent(keyboardEventSource: nil, virtualKey: code, keyDown: true),
              let up = CGEvent(keyboardEventSource: nil, virtualKey: code, keyDown: false) else { throw AgentError.unavailable("No pude crear la tecla.") }
        down.setIntegerValueField(.eventSourceUserData, value: Self.syntheticEventTag)
        up.setIntegerValueField(.eventSourceUserData, value: Self.syntheticEventTag)
        down.flags = flags; up.flags = flags
        down.post(tap: .cghidEventTap); up.post(tap: .cghidEventTap)
    }
    public func scroll(down: Bool, generation: UInt64, pid: pid_t, amount: Int = 5) throws {
        try check(generation, pid: pid)
        guard let event = CGEvent(scrollWheelEvent2Source: nil, units: .line, wheelCount: 1,
                                  wheel1: Int32((down ? -1 : 1) * min(30, max(1, amount))), wheel2: 0, wheel3: 0) else { throw AgentError.unavailable("No pude crear el desplazamiento.") }
        event.setIntegerValueField(.eventSourceUserData, value: Self.syntheticEventTag)
        event.post(tap: .cghidEventTap)
    }
    public func drag(from: CGPoint, to: CGPoint, milliseconds: Int, generation: UInt64, pid: pid_t) async throws {
        try check(generation, pid: pid)
        let up = try mouse(.leftMouseUp, from)
        try mouse(.leftMouseDown, from).post(tap: .cghidEventTap)
        defer { up.post(tap: .cghidEventTap) }
        let duration = min(5000, max(100, milliseconds)), steps = max(10, duration / 20)
        for index in 1...steps {
            try check(generation, pid: pid)
            let t = Double(index) / Double(steps)
            let current = CGPoint(x: from.x + (to.x - from.x) * t, y: from.y + (to.y - from.y) * t)
            up.location = current
            try mouse(.leftMouseDragged, current).post(tap: .cghidEventTap)
            try await Task.sleep(nanoseconds: UInt64(duration / steps) * 1_000_000)
        }
    }
    private static let codes: [String: CGKeyCode] = [
        "a":0,"s":1,"d":2,"f":3,"h":4,"g":5,"z":6,"x":7,"c":8,"v":9,"b":11,
        "q":12,"w":13,"e":14,"r":15,"y":16,"t":17,"1":18,"2":19,"3":20,"4":21,"6":22,"5":23,"9":25,"7":26,"8":28,"0":29,
        "o":31,"u":32,"i":34,"p":35,"l":37,"j":38,"k":40,"n":45,"m":46,
        "enter":36,"return":36,"tab":48,"space":49,"backspace":51,"esc":53,"escape":53,"back":53,
        "delete":117,"home":115,"end":119,"pageup":116,"pagedown":121,"left":123,"right":124,"down":125,"up":126,
        "f1":122,"f2":120,"f3":99,"f4":118,"f5":96,"f6":97,"f7":98,"f8":100,"f9":101,"f10":109,"f11":103,"f12":111]
}
