import Foundation

// El arnés: arrancar la app, mirarle el registro, y decir la verdad sobre lo que pasó.
//
// Todo lo que hay aquí existe para una sola cosa: que el veredicto se pueda creer. Un juez que no
// consigue correr no dice «no sé», dice «culpable» — y manda la investigación al sitio equivocado.
// Por eso `Pendiente` y `NoPudeCorrer` son estados DISTINTOS de `Rota`, y solo uno de los tres se
// resuelve escribiendo código de producción.

enum Veredicto {
    case cumplida(String)          // la promesa se sostiene, con la medida que lo dice
    case rota(String)              // se pudo juzgar y NO se cumple
    case pendiente(String)         // todavía no existe lo que la cumple; roja a propósito
    case noPudeCorrer(String)      // el arnés falló. NO cuenta como cumplida NI como rota
}

struct Promesa {
    let numero: Int
    let enunciado: String
    let correr: () throws -> Veredicto
}

// ── Hablarle a la app y mirarle el registro ──────────────────────────────────────────────────────

enum App {
    static let carpeta = URL(fileURLWithPath: #filePath)
        .deletingLastPathComponent()   // Contrato
        .deletingLastPathComponent()   // Sources
        .deletingLastPathComponent()   // mac-client
    static var bundle: URL { carpeta.appendingPathComponent("U.app") }
    static var registro: URL {
        FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent(".u/u.log")
    }

    /// SIEMPRE CON `open`, NUNCA EL BINARIO SUELTO. Lanzado a pelo, macOS lo mata con
    /// `__TCC_CRASHING_DUE_TO_PRIVACY_VIOLATION__` diciendo que falta un permiso que SÍ está en el
    /// Info.plist. Cuesta media hora de diagnóstico la primera vez.
    static func abrir(_ variables: [String: String]) throws {
        var args = ["-n"]
        for (k, v) in variables { args += ["--env", "\(k)=\(v)"] }
        args.append(bundle.path)
        try correr("/usr/bin/open", args)
    }

    static func cerrar() {
        _ = try? correr("/usr/bin/pkill", ["-x", "U"])
        // EL RESPIRO NO ES OPCIONAL. El dispositivo de audio queda tomado unos segundos después de
        // matar el proceso, y relanzar antes deja la sesión colgada abriendo — sin error, sin nada.
        // Se pagó varias veces el 2026-08-19 antes de entender que el fallo era del arnés.
        esperarA(segundos: 25, "que el proceso muera") { !corriendo }
        Thread.sleep(forTimeInterval: 6)
    }

    static var corriendo: Bool {
        (try? correr("/usr/bin/pgrep", ["-x", "U"]))?.isEmpty == false
    }

    static func limpiarRegistro() throws {
        try Data().write(to: registro)
    }

    static var registroEntero: String {
        (try? String(contentsOf: registro, encoding: .utf8)) ?? ""
    }

    /// Le habla por el altavoz. Es la única forma de probar el oído de verdad: el micrófono es un
    /// micrófono, y un arnés que se salte el aire no juzga lo que la usuaria vive.
    static func decirleEnVozAlta(_ frase: String) throws {
        try correr("/usr/bin/say", ["-v", "Paulina", frase])
    }

    @discardableResult
    static func correr(_ ruta: String, _ args: [String]) throws -> String {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: ruta)
        p.arguments = args
        let salida = Pipe()
        p.standardOutput = salida
        p.standardError = Pipe()
        try p.run()
        let datos = salida.fileHandleForReading.readDataToEndOfFile()
        p.waitUntilExit()
        return String(decoding: datos, as: UTF8.self)
    }
}

// ── Esperar por una condición, nunca por un reloj ────────────────────────────────────────────────

/// Espera a que algo SEA CIERTO, con tope. Devuelve si lo consiguió.
///
/// Nunca se espera «tres segundos y asumimos». Un `sleep` fijo convierte una máquina lenta en una
/// promesa rota, y eso es un juez que miente por otro camino.
@discardableResult
func esperarA(segundos: Double, _ que: String, _ condicion: () -> Bool) -> Bool {
    let limite = Date().addingTimeInterval(segundos)
    while Date() < limite {
        if condicion() { return true }
        Thread.sleep(forTimeInterval: 0.25)
    }
    return false
}

func registroTiene(_ aguja: String) -> Bool { App.registroEntero.contains(aguja) }

func lineasDelRegistro(con aguja: String) -> [String] {
    App.registroEntero.split(separator: "\n").map(String.init).filter { $0.contains(aguja) }
}

/// El instante de una línea del registro, en segundos desde medianoche. Sirve para MEDIR, que es lo
/// que separa «contestó» de «contestó a tiempo».
func instante(_ linea: String) -> Double? {
    let p = linea.prefix(12).split(separator: ":")
    guard p.count == 3, let h = Double(p[0]), let m = Double(p[1]), let s = Double(p[2]) else {
        return nil
    }
    return h * 3600 + m * 60 + s
}

func primeraLinea(con aguja: String) -> String? { lineasDelRegistro(con: aguja).first }
