import AppKit
import Foundation

/// EL PROTOTIPO ACOTADO DE COMPUTER-USE, y su límite es a propósito: SOLO MIRA.
///
/// No hace clic, no teclea, no manda un solo evento de entrada. Es la mitad de lectura del bucle que
/// existe en Windows (`AgentLoop.ReadStateAsync` + `Screenshotter`), aislada del resto de Ü: no vive
/// en la conversación en vivo, no lo puede disparar una frase hablada, solo el arnés de pruebas.
///
/// Por qué la lectura primero y no el bucle entero: cada pieza nueva pide un permiso de macOS que el
/// usuario tiene que conceder a mano en Ajustes del Sistema, y hasta no probar que el permiso de
/// Grabación de pantalla se concede limpio, no vale la pena escribir la de Accesibilidad (la que hace
/// falta para `CGEvent` — clic y tecleo). Un prototipo que pide dos permisos a la vez y falla no dice
/// cuál de los dos fue.
///
///     open --env U_MIRAR=1 "…/U.app"
///
/// Sale por el registro, nunca actúa, y termina solo.
enum Mirar {

    static var pedido: Bool { ProcessInfo.processInfo.environment["U_MIRAR"] == "1" }

    /// La captura. `CGWindowListCreateImage` está OBSOLETA desde macOS 14 a favor de
    /// `ScreenCaptureKit`, y se usa aquí a propósito por ser síncrona y sin delegado: para un
    /// prototipo que solo necesita UNA foto, el camino corto vale más que el correcto. Si esto pasa
    /// de prototipo a producción, se cambia por `SCScreenshotManager` — no antes.
    private static func capturar() -> CGImage? {
        CGWindowListCreateImage(.infinite, .optionOnScreenOnly, kCGNullWindowID, .bestResolution)
    }

    private static func aBase64Jpeg(_ imagen: CGImage) -> String? {
        let rep = NSBitmapImageRep(cgImage: imagen)
        guard let datos = rep.representation(using: .jpeg, properties: [.compressionFactor: 0.6]) else {
            return nil
        }
        return datos.base64EncodedString()
    }

    /// Mira, le pregunta al modelo qué ve, y lo dice. NO TOCA NADA — ni el teclado ni el ratón.
    static func correr(llave: String) async {
        Registro.di("👁 U_MIRAR=1 · miro la pantalla")
        Registro.di("👁 primer plano: \(Frente.descripcion)")

        // LA COMPUERTA QUE FALTABA, y es la razón por la que este prototipo dio un falso positivo el
        // 2026-08-21: `CGWindowListCreateImage` SIN permiso no falla ni da nil — entrega una captura
        // "segura" con solo el fondo de pantalla, indistinguible a simple vista de una captura real.
        // El modelo entonces «acertó» que había una Calculadora porque leyó su nombre en la barra de
        // menú (que SÍ viaja en la imagen degradada), sin haber visto un solo botón ni el resultado.
        // Una caja que miente es peor que no tener caja: se pregunta ANTES, con la única API que
        // distingue esto sin ambigüedad, y si no hay permiso de verdad, no se manda nada al modelo.
        guard CGPreflightScreenCaptureAccess() else {
            Registro.di("👁 ✘ sin permiso de Grabación de pantalla — CGPreflightScreenCaptureAccess() = false")
            Registro.di("👁 ✋ pidiéndolo ahora. Ajustes del Sistema debería abrirse solo; si no, ve a"
                + " Privacidad y seguridad → Grabación de pantalla, quita a Ü si ya está en la lista"
                + " (queda obsoleta al recompilar, la firma ad-hoc cambia), actívala de nuevo, y"
                + " vuelve a correr U_MIRAR=1.")
            CGRequestScreenCaptureAccess()
            return
        }

        guard let imagen = capturar() else {
            Registro.di("👁 ✘ tengo el permiso pero no pude capturar de todos modos")
            return
        }
        guard let b64 = aBase64Jpeg(imagen) else {
            Registro.di("👁 ✘ capturé la pantalla pero no pude codificarla a JPEG")
            return
        }
        Registro.di("👁 capturada: \(imagen.width)×\(imagen.height) px · \(b64.count / 1024) KB en base64")

        do {
            let descripcion = try await preguntarleAlModelo(llave: llave, imagenB64: b64)
            Registro.di("👁 el modelo ve: «\(descripcion)»")
        } catch {
            Registro.di("👁 ✘ el modelo no contestó: \(error.localizedDescription)")
        }
    }

    /// Llamada de una sola vez, deliberadamente SEPARADA de `Cerebro`: este prototipo no comparte
    /// historia de conversación ni modelo con la voz — es una pregunta suelta y aislada, para que un
    /// fallo aquí no pueda tocar la conversación de verdad.
    private static func preguntarleAlModelo(llave: String, imagenB64: String) async throws -> String {
        let cuerpo: [String: Any] = [
            "contents": [[
                "role": "user",
                "parts": [
                    ["inline_data": ["mime_type": "image/jpeg", "data": imagenB64]],
                    ["text": "Describe en una frase corta y en español qué se ve en esta captura de pantalla de Mac."],
                ],
            ]],
        ]
        var pet = URLRequest(url: URL(string:
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash-lite:generateContent?key=\(llave)")!)
        pet.httpMethod = "POST"
        pet.setValue("application/json", forHTTPHeaderField: "Content-Type")
        pet.httpBody = try JSONSerialization.data(withJSONObject: cuerpo)
        pet.timeoutInterval = 30

        let (datos, _) = try await URLSession.shared.data(for: pet)
        guard let raiz = try JSONSerialization.jsonObject(with: datos) as? [String: Any] else {
            throw NSError(domain: "Mirar", code: 1, userInfo: [NSLocalizedDescriptionKey: "respuesta rara"])
        }
        if let error = raiz["error"] as? [String: Any], let msg = error["message"] as? String {
            throw NSError(domain: "Mirar", code: 2, userInfo: [NSLocalizedDescriptionKey: msg])
        }
        guard let texto = ((raiz["candidates"] as? [[String: Any]])?.first?["content"] as? [String: Any])
            .flatMap({ ($0["parts"] as? [[String: Any]])?.first?["text"] as? String })
        else {
            throw NSError(domain: "Mirar", code: 3, userInfo: [NSLocalizedDescriptionKey: "sin texto en la respuesta"])
        }
        return texto.trimmingCharacters(in: .whitespacesAndNewlines)
    }
}
