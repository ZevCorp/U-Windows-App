import AppKit
import ApplicationServices
import UCore

public struct AccessibleControl: Sendable {
    public let target: AXTarget
    public let frame: CGRect
    public let value: String
    public let actions: [String]
}

public struct DesktopSnapshot: @unchecked Sendable {
    public let pid: pid_t
    public let bundleID: String
    public let appName: String
    public let title: String
    public let windowFrame: CGRect?
    public let controls: [AccessibleControl]
    public let truncated: Bool
    public let documentURL: URL?
    fileprivate let elements: [String: AXUIElement]
}

/// AX is synchronous IPC. All reads and actions run on one background queue with a time budget.
public final class AccessibilityReader: @unchecked Sendable {
    private let queue = DispatchQueue(label: "com.zevcorp.u.mac.accessibility", qos: .userInitiated)
    public init() {}
    public static var trusted: Bool { AXIsProcessTrusted() }
    public static func requestPermission() {
        _ = AXIsProcessTrustedWithOptions([kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary)
        // Open the exact pane so the user does not accidentally enable the old U.app copy.
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility") {
            NSWorkspace.shared.open(url)
        }
    }
    private func run<T>(_ block: @escaping () throws -> T) async throws -> T {
        try await withCheckedThrowingContinuation { continuation in
            queue.async { do { continuation.resume(returning: try block()) } catch { continuation.resume(throwing: error) } }
        }
    }
    public func read(pid: pid_t, bundleID: String, appName: String, actionableOnly: Bool = false) async throws -> DesktopSnapshot {
        try await run {
            guard Self.trusted else { throw AgentError.permission("Accesibilidad") }
            AXUIElementSetMessagingTimeout(AXUIElementCreateSystemWide(), 0.2)
            let app = AXUIElementCreateApplication(pid)
            // Chromium/Electron expose their web subtree when an accessibility client requests it.
            if bundleID.contains("Chrome") || bundleID.contains("chromium") || bundleID.contains("Edge") {
                AXUIElementSetAttributeValue(app, "AXManualAccessibility" as CFString, kCFBooleanTrue)
                AXUIElementSetAttributeValue(app, "AXEnhancedUserInterface" as CFString, kCFBooleanTrue)
            }
            let window = Self.element(app, kAXFocusedWindowAttribute)
                ?? (Self.attribute(app, kAXWindowsAttribute) as? [AXUIElement])?.first
            var roots: [AXUIElement] = []
            if let window { roots.append(window) }
            if let menu = Self.element(app, kAXMenuBarAttribute) { roots.append(menu) }

            let prefix = String(UUID().uuidString.prefix(8))
            var stack = roots.map { ($0, 0) }
            var seen = Set<AXUIElement>()
            var controls: [AccessibleControl] = []
            var elements: [String: AXUIElement] = [:]
            var documentURL: URL?
            let deadline = ProcessInfo.processInfo.systemUptime + 2
            while let (element, depth) = stack.popLast() {
                if seen.count >= 1600 || ProcessInfo.processInfo.systemUptime > deadline { break }
                guard depth <= 28, seen.insert(element).inserted else { continue }
                // Fetch scalar attributes in one AX IPC instead of a dozen round trips per node.
                let keys = [kAXRoleAttribute, kAXSubroleAttribute, kAXTitleAttribute, kAXDescriptionAttribute,
                            kAXHelpAttribute, kAXIdentifierAttribute, kAXPositionAttribute, kAXSizeAttribute,
                            kAXEnabledAttribute, "AXHidden"] + (actionableOnly ? [] : [kAXValueAttribute])
                var raw: CFArray?
                AXUIElementCopyMultipleAttributeValues(element, keys as CFArray, [], &raw)
                let values = raw as? [Any] ?? []
                func value(_ key: String) -> Any? { guard let i = keys.firstIndex(of: key), i < values.count else { return nil }; return values[i] }
                func string(_ key: String) -> String { value(key) as? String ?? "" }
                let role = string(kAXRoleAttribute), subrole = string(kAXSubroleAttribute)
                let secure = subrole == "AXSecureTextField" || role == "AXSecureTextField"
                let fieldValue = secure ? "[protegido]" : String(string(kAXValueAttribute).prefix(300))
                let label = [kAXTitleAttribute, kAXDescriptionAttribute, kAXHelpAttribute, kAXIdentifierAttribute]
                    .lazy.map { string($0) }.first(where: { !$0.isEmpty }) ?? (secure ? "Campo protegido" : fieldValue)
                var names: CFArray?
                AXUIElementCopyActionNames(element, &names)
                let actions = names as? [String] ?? []
                let meaningful = !label.isEmpty || !actions.isEmpty || ["AXTextField", "AXTextArea", "AXCheckBox", "AXLink", "AXRow"].contains(role)
                let hidden = value("AXHidden") as? Bool ?? false
                if role == "AXWebArea", !hidden, documentURL == nil {
                    let rawURL = Self.attribute(element, "AXURL")
                    let url = (rawURL as? URL) ?? (rawURL as? String).flatMap(URL.init(string:))
                    if let url, ["http", "https"].contains(url.scheme ?? "") { documentURL = url }
                }
                if meaningful, !hidden, (!actionableOnly || (actions.contains("AXPress") && value(kAXEnabledAttribute) as? Bool != false)), let frame = Self.frame(position: value(kAXPositionAttribute), size: value(kAXSizeAttribute)), frame.width > 0, frame.height > 0 {
                    let id = "\(prefix)-\(controls.count + 1)"
                    controls.append(AccessibleControl(target: AXTarget(id: id, role: role, label: String(label.prefix(300))), frame: frame, value: fieldValue, actions: actions))
                    elements[id] = element
                }
                // Copy only a bounded slice. Some virtualized browser trees contain millions of descendants.
                var children: CFArray?
                if AXUIElementCopyAttributeValues(element, kAXChildrenAttribute as CFString, 0, 200, &children) == .success,
                   let items = children as? [AXUIElement] {
                    stack.append(contentsOf: items.reversed().map { ($0, depth + 1) })
                }
            }
            return DesktopSnapshot(pid: pid, bundleID: bundleID, appName: appName,
                                   title: window.map { Self.string($0, kAXTitleAttribute) } ?? "",
                                   windowFrame: window.flatMap(Self.frame), controls: controls,
                                   truncated: !stack.isEmpty, documentURL: documentURL, elements: elements)
        }
    }
    public func press(_ query: String, snapshot: DesktopSnapshot, gate: ActionGate, generation: UInt64) async throws {
        try await run {
            try gate.check(generation: generation, expectedPID: snapshot.pid, currentPID: NSWorkspace.shared.frontmostApplication?.processIdentifier)
            let target = try TargetResolver.resolve(query, in: snapshot.controls.map(\.target))
            guard let element = snapshot.elements[target.id] else { throw AgentError.staleFocus }
            guard Self.attribute(element, kAXEnabledAttribute) as? Bool != false else { throw AgentError.unavailable("El control está deshabilitado.") }
            let error = AXUIElementPerformAction(element, kAXPressAction as CFString)
            guard error == .success else { throw AgentError.unavailable("AXPress no está disponible para este control (\(error.rawValue)). Usa la captura para localizarlo.") }
        }
    }
    public func setValue(_ query: String, text: String, snapshot: DesktopSnapshot, gate: ActionGate, generation: UInt64) async throws {
        try await run {
            try gate.check(generation: generation, expectedPID: snapshot.pid, currentPID: NSWorkspace.shared.frontmostApplication?.processIdentifier)
            let target = try TargetResolver.resolve(query, in: snapshot.controls.map(\.target))
            guard let element = snapshot.elements[target.id] else { throw AgentError.staleFocus }
            var writable: DarwinBoolean = false
            guard AXUIElementIsAttributeSettable(element, kAXValueAttribute as CFString, &writable) == .success, writable.boolValue else {
                throw AgentError.unavailable("Este campo no acepta AXValue. Enfócalo y escribe con el teclado.")
            }
            guard AXUIElementSetAttributeValue(element, kAXValueAttribute as CFString, text as CFString) == .success else { throw AgentError.unavailable("No se pudo escribir en el campo.") }
            if Self.string(element, kAXSubroleAttribute) != "AXSecureTextField", Self.string(element, kAXValueAttribute) != text {
                throw AgentError.unavailable("El campo no conservó el valor escrito.")
            }
        }
    }
    private static func attribute(_ element: AXUIElement, _ key: String) -> CFTypeRef? {
        var value: CFTypeRef?
        return AXUIElementCopyAttributeValue(element, key as CFString, &value) == .success ? value : nil
    }
    private static func string(_ element: AXUIElement, _ key: String) -> String { attribute(element, key) as? String ?? "" }
    private static func element(_ element: AXUIElement, _ key: String) -> AXUIElement? {
        guard let value = attribute(element, key), CFGetTypeID(value) == AXUIElementGetTypeID() else { return nil }
        return (value as! AXUIElement)
    }
    private static func frame(_ element: AXUIElement) -> CGRect? {
        frame(position: attribute(element, kAXPositionAttribute), size: attribute(element, kAXSizeAttribute))
    }
    private static func frame(position: Any?, size: Any?) -> CGRect? {
        guard let p = position as CFTypeRef?, CFGetTypeID(p) == AXValueGetTypeID(),
              let s = size as CFTypeRef?, CFGetTypeID(s) == AXValueGetTypeID() else { return nil }
        var point = CGPoint.zero, extent = CGSize.zero
        guard AXValueGetValue(p as! AXValue, .cgPoint, &point), AXValueGetValue(s as! AXValue, .cgSize, &extent) else { return nil }
        return CGRect(origin: point, size: extent)
    }
}
