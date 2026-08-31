import Foundation

/// Dónde vive la llave de Gemini.
///
/// En `~/.u/gemini-key.txt`, y NUNCA dentro del proyecto: una llave dentro de la carpeta del código
/// es una llave que tarde o temprano se sube a un repo. Fuera, ningún `git add .` puede alcanzarla.
enum Llave {

    static var archivo: URL {
        FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent(".u/gemini-key.txt")
    }

    /// `~/.u/openai-key.txt` — mismo patrón que la de Gemini, y por la misma razón que dejó escrita
    /// Felipe en el lado de Windows: cada persona con la SUYA, no una compartida entre todos. Una
    /// sola llave repartida es exactamente lo que dejó a todo el equipo sin voz el 2026-08-31 cuando
    /// se agotó el saldo de Gemini.
    static var archivoOpenAI: URL {
        FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent(".u/openai-key.txt")
    }

    /// La llave, o `nil` si todavía no hay ninguna. La variable de entorno gana, para poder probar
    /// otra sin tocar el archivo.
    static var gemini: String? {
        if let e = ProcessInfo.processInfo.environment["GEMINI_API_KEY"], !e.isEmpty { return e }
        guard let t = try? String(contentsOf: archivo, encoding: .utf8) else { return nil }
        let limpia = t.trimmingCharacters(in: .whitespacesAndNewlines)
        return limpia.isEmpty ? nil : limpia
    }

    static var openai: String? {
        if let e = ProcessInfo.processInfo.environment["OPENAI_API_KEY"], !e.isEmpty { return e }
        guard let t = try? String(contentsOf: archivoOpenAI, encoding: .utf8) else { return nil }
        let limpia = t.trimmingCharacters(in: .whitespacesAndNewlines)
        return limpia.isEmpty ? nil : limpia
    }
}
