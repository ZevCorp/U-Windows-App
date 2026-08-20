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

    /// La llave, o `nil` si todavía no hay ninguna. La variable de entorno gana, para poder probar
    /// otra sin tocar el archivo.
    static var gemini: String? {
        if let e = ProcessInfo.processInfo.environment["GEMINI_API_KEY"], !e.isEmpty { return e }
        guard let t = try? String(contentsOf: archivo, encoding: .utf8) else { return nil }
        let limpia = t.trimmingCharacters(in: .whitespacesAndNewlines)
        return limpia.isEmpty ? nil : limpia
    }
}
