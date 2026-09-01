import ApplicationServices
import Foundation

/// LAS MANOS: clic sintético por `CGEvent`. Es el equivalente Mac de `InputExecutor.cs` en Windows
/// (`SendInput`), y la primera pieza de este repo que de verdad mueve el ratón por su cuenta.
///
/// Pide el permiso de ACCESIBILIDAD — distinto del de Grabación de pantalla que ya tiene `Mirar`—.
/// Mismo patrón honesto: si no está concedido de verdad, no manda un solo evento y lo dice.
enum Entrada {

    /// `AXIsProcessTrusted()` es la única fuente de verdad — no hay atajo que la sustituya. A
    /// diferencia de `CGWindowListCreateImage`, `CGEventPost` SÍ falla en silencio sin permiso: el
    /// evento se descarta y no pasa nada, ni error ni excepción. Comprobar antes es lo único que
    /// distingue «no hice clic porque no debía» de «hice clic y no sirvió de nada».
    static var permisoConcedido: Bool { AXIsProcessTrusted() }

    /// Pide el permiso con el diálogo nativo del sistema. Si el proceso ya está en la lista de
    /// Ajustes pero con una firma vieja (el mismo problema que tuvo Grabación de pantalla hoy), esto
    /// no hace magia: hay que quitarlo de la lista a mano y dejar que vuelva a preguntar.
    static func pedirPermiso() {
        let opciones: NSDictionary = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true]
        _ = AXIsProcessTrustedWithOptions(opciones)
    }

    /// Clic izquierdo en un punto de la PANTALLA (coordenadas absolutas, como las que da `Arbol`).
    ///
    /// Se manda como una pareja mouseDown/mouseUp, no un solo evento .leftMouseDown: muchas apps
    /// esperan la secuencia completa para reconocer un clic real, y NSButton específicamente reacciona
    /// al mouseUp dentro de sus límites, no al Down solo.
    @discardableResult
    static func clic(en punto: CGPoint) -> Bool {
        guard permisoConcedido else {
            Registro.di("🖱 ✘ no clico: sin permiso de Accesibilidad (AXIsProcessTrusted() = false)")
            return false
        }
        guard let bajar = CGEvent(mouseEventSource: nil, mouseType: .leftMouseDown,
                                   mouseCursorPosition: punto, mouseButton: .left),
              let subir = CGEvent(mouseEventSource: nil, mouseType: .leftMouseUp,
                                   mouseCursorPosition: punto, mouseButton: .left)
        else {
            Registro.di("🖱 ✘ no pude construir el evento de clic")
            return false
        }
        bajar.post(tap: .cghidEventTap)
        // Un respiro entre bajar y subir: sin él, algunas apps (Calculadora incluida) no lo leen
        // como un clic humano y lo descartan.
        Thread.sleep(forTimeInterval: 0.03)
        subir.post(tap: .cghidEventTap)
        Registro.di("🖱 clic en (\(Int(punto.x)), \(Int(punto.y)))")
        return true
    }
}
