import Foundation

/// El registro de lo que hace Ü, en `~/.u/u.log`.
///
/// Existe porque una carita no puede contar lo que le pasó por dentro: cuando no contesta, desde
/// fuera se ven exactamente las mismas cero reacciones tanto si no oyó, como si oyó y no entendió,
/// como si entendió y el modelo falló. Son tres problemas distintos con tres arreglos distintos, y
/// sin registro se prueba a ciegas. **Leerlo antes de teorizar.**
enum Registro {

    static var archivo: URL {
        FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent(".u/u.log")
    }

    private static let formato: DateFormatter = {
        let f = DateFormatter()
        f.dateFormat = "HH:mm:ss.SSS"
        return f
    }()

    private static let cola = DispatchQueue(label: "u.registro")

    static func di(_ linea: String) {
        let texto = "\(formato.string(from: Date()))  \(linea)\n"
        FileHandle.standardError.write(Data(texto.utf8))
        cola.async {
            let url = archivo
            try? FileManager.default.createDirectory(at: url.deletingLastPathComponent(),
                                                     withIntermediateDirectories: true)
            if let h = try? FileHandle(forWritingTo: url) {
                h.seekToEndOfFile()
                h.write(Data(texto.utf8))
                try? h.close()
            } else {
                try? texto.write(to: url, atomically: true, encoding: .utf8)
            }
        }
    }
}
