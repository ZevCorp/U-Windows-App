import AppKit

/// Qué aplicación está en primer plano AHORA. Sin permiso: `NSWorkspace` lo da gratis.
///
/// Es la pieza más pequeña del prototipo de computer-use y la primera que se escribió a propósito:
/// es la que hace de COMPUERTA en el cliente de Windows (`AgentLoop.HereOrigin()`) — el 2026-07-26 un
/// agente tecleó en la aplicación equivocada porque nadie comprobó que el foco seguía siendo el
/// mismo entre leer y actuar. Aquí todavía no hay nada que actuar, pero la pieza que evita ese
/// desastre es la que se construye primero, no la última.
enum Frente {

    /// El identificador de la app en primer plano — `com.apple.finder`, `com.google.Chrome` — o
    /// `nil` si no se pudo saber. Nunca el nombre visible: el nombre cambia con el idioma del
    /// sistema, el bundle id no.
    static var appId: String? {
        NSWorkspace.shared.frontmostApplication?.bundleIdentifier
    }

    static var nombreLegible: String? {
        NSWorkspace.shared.frontmostApplication?.localizedName
    }

    static var descripcion: String {
        guard let id = appId else { return "(no se pudo saber qué app está al frente)" }
        return "\(nombreLegible ?? "?") (\(id))"
    }
}
