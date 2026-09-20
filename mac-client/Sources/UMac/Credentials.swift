import Foundation
import Security
import UCore

public enum Credentials {
    private static let service = "com.zevcorp.u.mac.native"
    public static func read(_ name: String) -> String? {
        if let env = ProcessInfo.processInfo.environment[name], !env.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { return env }
        var result: CFTypeRef?
        let query: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service,
                                    kSecAttrAccount as String: name, kSecReturnData as String: true, kSecMatchLimit as String: kSecMatchLimitOne]
        guard SecItemCopyMatching(query as CFDictionary, &result) == errSecSuccess, let bytes = result as? Data else { return nil }
        return String(data: bytes, encoding: .utf8)
    }
    public static func save(_ name: String, value: String) throws {
        let query: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service, kSecAttrAccount as String: name]
        let clean = value.trimmingCharacters(in: .whitespacesAndNewlines)
        if clean.isEmpty { SecItemDelete(query as CFDictionary); return }
        let attributes: [String: Any] = [kSecValueData as String: Data(clean.utf8)]
        var status = SecItemUpdate(query as CFDictionary, attributes as CFDictionary)
        if status == errSecItemNotFound {
            var create = query.merging(attributes) { _, new in new }
            create[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
            status = SecItemAdd(create as CFDictionary, nil)
        }
        guard status == errSecSuccess else { throw AgentError.unavailable("No pude guardar la credencial en el Llavero (\(status)).") }
    }
}
