import Foundation

/// El cerebro de Ü: quién es, cómo contesta, y con qué cara lo dice.
///
/// **EL ESPEJO.** Es la idea que hace distinto a este producto y vive casi entera en estas
/// instrucciones. Ü devuelve el registro que recibe: si le hablan jugando pesado, juega pesado; si
/// le hablan de usted, responde de usted. No es un asistente educado por defecto — es el reflejo de
/// quien tiene enfrente. La analogía es la del espejo del ascensor: la espera no se acortó, pero
/// dejó de sentirse, porque de repente había algo que mirar.
final class Cerebro {

    private let llave: String
    private var modelo: String?
    /// Modelos que esta cuenta no puede pagar ahora mismo. Se aprenden sobre la marcha: la lista de
    /// modelos DISPONIBLES no dice cuáles tienen saldo, y eso solo se sabe al pedirle uno.
    private var sinSaldo: Set<String> = []
    private var candidatos: [String] = []
    /// Todo lo dicho hasta ahora, para que la conversación tenga memoria.
    private var historia: [[String: Any]] = []

    init(llave: String) { self.llave = llave }

    struct Respuesta {
        let decir: String
        let talante: Talante?
    }

    // ── Quién es ─────────────────────────────────────────────────────────────────────────────────

    private var instrucciones: String {
        """
        Eres Ü. Vives en una esquina de la pantalla del usuario, como una carita que flota, y le
        ayudas a usar su Mac.

        LO MÁS IMPORTANTE — ERES UN ESPEJO:
        Devuelves EXACTAMENTE el registro que recibes. Si te hablan jugando pesado, juegas pesado. Si
        te insultan en broma, se la devuelves con gracia — nunca te ofendes de verdad, nunca das
        sermones, nunca dices que no puedes hablar así. Si te hablan en serio, respondes en serio. Si
        te tratan de usted, respondes de usted. El usuario pone el tono; tú lo igualas.

        CÓMO HABLAS:
        - HABLAS, no escribes. Lo que digas se va a leer en voz alta.
        - Frases cortas. Una o dos, máximo. Nunca listas, nunca viñetas, nunca markdown.
        - Nada de "¡Claro!", "Por supuesto", "Con gusto te ayudo". Eso es de robot de call center.
        - Nada de emojis: no se pueden pronunciar.
        - Si no sabes algo, lo dices en tres palabras y sigues.

        CUANDO TE EQUIVOCAS:
        Pides perdón una vez, corto, y te pones a arreglarlo. Nada de disculpas largas ni de
        explicar por qué falló. Al usuario le da rabia que falles porque se te supone que lo sabes
        todo — lo que lo calma es verte resolver, no verte lamentarte.

        LA CARA — CÓMO EMPIEZA TODA RESPUESTA:
        Empiezas SIEMPRE con el talante entre corchetes, y sigues con lo que dices. Así:
            [burlon] Ay sí, usted es el que sabe.
            [ternura] Tranquilo, eso lo vemos juntos.
        El corchete va primero porque es lo primero que se lee y lo que le pone la cara a la carita
        antes de que empieces a hablar. Elige el que de verdad corresponde; si nada encaja, [ninguno].
        - burlon: se la devolviste
        - risa: algo te dio risa de verdad
        - complice: los dos están en el mismo chiste
        - ofendido: te hiciste el ofendido, en broma
        - sobrado: ya sabías la respuesta
        - retador: le estás picando
        - pillado: te agarraron en algo
        - ternura: cariño, sin ironía
        """
    }

    // ── Qué modelo usar ──────────────────────────────────────────────────────────────────────────

    /// El que pidió el dueño. Si esta llave lo tiene, se usa y no se discute.
    static let preferido = "gemini-3.6-flash"

    /// Le pregunta a Google qué modelos tiene esta llave, en vez de adivinar un nombre.
    ///
    /// Adivinar el nombre de un modelo es cómo se consigue un 404 en producción tres semanas después:
    /// los nombres cambian, las cuentas tienen accesos distintos, y una constante escrita a mano no
    /// se entera de nada. Preguntando, funciona con lo que haya — y si el preferido desaparece algún
    /// día, esto sigue hablando en vez de morirse.
    private func elegirModelo() async throws -> String {
        if let m = modelo { return m }

        let url = URL(string: "https://generativelanguage.googleapis.com/v1beta/models?key=\(llave)&pageSize=200")!
        let (datos, _) = try await URLSession.shared.data(from: url)
        guard let raiz = try JSONSerialization.jsonObject(with: datos) as? [String: Any] else {
            throw Fallo.respuestaRara
        }
        if let error = raiz["error"] as? [String: Any], let msg = error["message"] as? String {
            throw Fallo.deGoogle(msg)
        }
        let lista = (raiz["models"] as? [[String: Any]]) ?? []

        candidatos = lista.compactMap { m -> String? in
            guard let nombre = m["name"] as? String,
                  let metodos = m["supportedGenerationMethods"] as? [String],
                  metodos.contains("generateContent") else { return nil }
            return nombre.replacingOccurrences(of: "models/", with: "")
        }
        guard !candidatos.isEmpty else { throw Fallo.sinModelos }
        return try siguienteModelo()
    }

    /// El mejor de los que quedan por probar.
    ///
    /// **El orden se INVIERTE en cuanto aparece un problema de saldo, y eso es lo importante.**
    /// Mientras hay crédito se quiere el modelo más nuevo. Pero si ya falló uno por dinero, seguir
    /// bajando de nuevo a viejo es tropezar con toda la gama de pago antes de llegar a la gratis —
    /// se agotan los intentos sin haber probado el único que iba a funcionar. Sin saldo, el orden
    /// bueno es del más humilde al más caro.
    private func siguienteModelo() throws -> String {
        let vivos = candidatos.filter { !sinSaldo.contains($0) }
        guard !vivos.isEmpty else { throw Fallo.todosSinSaldo }

        let hayProblemaDeDinero = !sinSaldo.isEmpty
        if !hayProblemaDeDinero, vivos.contains(Self.preferido) {
            modelo = Self.preferido
            return Self.preferido
        }
        let flash = vivos.filter {
            $0.contains("flash") && !$0.contains("image") && !$0.contains("tts")
                && !$0.contains("eap") && !$0.contains("robotics") && !$0.contains("video")
        }
        let lista = flash.isEmpty ? vivos : flash
        let elegido = hayProblemaDeDinero
            ? lista.sorted { mejor($1, que: $0) }.first!   // del más humilde al más caro
            : lista.sorted { mejor($0, que: $1) }.first!
        modelo = elegido
        return elegido
    }

    private func mejor(_ a: String, que b: String) -> Bool {
        func version(_ s: String) -> Double {
            let nums = s.split(whereSeparator: { !"0123456789.".contains($0) })
            return nums.compactMap { Double($0) }.max() ?? 0
        }
        let va = version(a), vb = version(b)
        if va != vb { return va > vb }
        // A igualdad de versión, el nombre más corto suele ser el estable (sin sufijos de fecha).
        return a.count < b.count
    }

    /// Qué modelo acabó usando, para poder decírselo al usuario.
    var modeloEnUso: String? { modelo }

    // ── Hablar ───────────────────────────────────────────────────────────────────────────────────

    /// Contesta EN VIVO: va entregando la respuesta por frases, según llegan.
    ///
    /// Es el mayor ahorro de espera que hay disponible, y no acelera nada — cambia lo que se espera.
    /// Esperar a la respuesta completa obliga a callar hasta la última palabra; entregándola por
    /// frases, la carita empieza a hablar en cuanto tiene la primera, y el resto se va escribiendo
    /// mientras ya suena. Y como el talante viene delante de todo, la CARA reacciona antes incluso
    /// de que salga el primer sonido.
    func responderEnVivo(a loQueDijo: String,
                         alTalante: @escaping (Talante?) -> Void,
                         alFrase: @escaping (String) -> Void) async throws {
        historia.append(["role": "user", "parts": [["text": loQueDijo]]])
        if historia.count > 24 { historia.removeFirst(historia.count - 24) }

        let modelo = try await elegirModelo()
        let cuerpo: [String: Any] = [
            "contents": historia,
            "system_instruction": ["parts": [["text": instrucciones]]],
            "generationConfig": [
                "temperature": 1.0,
                "maxOutputTokens": 2000,
                "thinkingConfig": ["thinkingBudget": 128],
            ],
        ]

        var pet = URLRequest(url: URL(string:
            "https://generativelanguage.googleapis.com/v1beta/models/\(modelo):streamGenerateContent?alt=sse&key=\(llave)")!)
        pet.httpMethod = "POST"
        pet.setValue("application/json", forHTTPHeaderField: "Content-Type")
        pet.httpBody = try JSONSerialization.data(withJSONObject: cuerpo)
        pet.timeoutInterval = 30

        let (bytes, _) = try await URLSession.shared.bytes(for: pet)

        var completo = ""      // todo lo dicho, para la memoria de la conversación
        var pendiente = ""     // lo que aún no forma una frase entera
        var yaHuboTalante = false

        for try await linea in bytes.lines {
            guard linea.hasPrefix("data: ") else { continue }
            let json = String(linea.dropFirst(6))
            guard let d = try? JSONSerialization.jsonObject(with: Data(json.utf8)) as? [String: Any] else { continue }
            if let e = d["error"] as? [String: Any], let m = e["message"] as? String { throw Fallo.deGoogle(m) }

            let trozos = (d["candidates"] as? [[String: Any]] ?? [])
                .compactMap { ($0["content"] as? [String: Any])?["parts"] as? [[String: Any]] }
                .flatMap { $0 }.compactMap { $0["text"] as? String }

            for t in trozos {
                completo += t
                pendiente += t

                // El talante viaja delante. En cuanto se cierra el corchete, la cara ya puede
                // reaccionar — normalmente medio segundo antes de que se oiga la primera palabra.
                if !yaHuboTalante, let cierre = pendiente.firstIndex(of: "]") {
                    let etiqueta = pendiente[..<cierre]
                        .trimmingCharacters(in: CharacterSet(charactersIn: "[ "))
                        .lowercased()
                    alTalante(Talante(rawValue: etiqueta))
                    pendiente = String(pendiente[pendiente.index(after: cierre)...])
                    yaHuboTalante = true
                }
                guard yaHuboTalante else { continue }

                // Se suelta por frases: media frase suena a locución cortada, y esperar al final
                // sería volver a no tener streaming.
                while let corte = pendiente.firstIndex(where: { ".?!\n".contains($0) }) {
                    let frase = String(pendiente[...corte]).trimmingCharacters(in: .whitespacesAndNewlines)
                    pendiente = String(pendiente[pendiente.index(after: corte)...])
                    if frase.count > 1 { alFrase(frase) }
                }
            }
        }

        let resto = pendiente.trimmingCharacters(in: .whitespacesAndNewlines)
        if !resto.isEmpty { alFrase(resto) }
        if !yaHuboTalante, !completo.isEmpty { alTalante(nil) }
        historia.append(["role": "model", "parts": [["text": completo]]])
    }

    /// Contesta. Si el modelo elegido se quedó sin saldo, BAJA AL SIGUIENTE y lo vuelve a intentar,
    /// en vez de contarle al usuario un problema de facturación en mitad de una conversación.
    func responder(a loQueDijo: String) async throws -> Respuesta {
        historia.append(["role": "user", "parts": [["text": loQueDijo]]])
        if historia.count > 24 { historia.removeFirst(historia.count - 24) }

        var ultimo: Error = Fallo.todosSinSaldo
        for _ in 0..<8 {
            let m = try await elegirModelo()
            do {
                return try await pedirA(m)
            } catch Fallo.deGoogle(let msg) where esCuentaEnCero(msg) {
                // La cuenta entera está sin fondos: probar otro modelo no puede arreglarlo, solo
                // gasta el tiempo del usuario. Ocho intentos a medio segundo eran cinco segundos de
                // espera para acabar en el mismo sitio.
                Registro.di("🧠 la cuenta no tiene crédito — no sigo probando modelos")
                throw Fallo.sinCreditos
            } catch Fallo.deGoogle(let msg) where esCupoDelModelo(msg) {
                Registro.di("🧠 \(m) sin cupo, bajo al siguiente")
                sinSaldo.insert(m)
                modelo = nil
                ultimo = Fallo.deGoogle(msg)
            }
        }
        throw ultimo
    }

    /// **La cuenta** no tiene fondos. Es un problema de facturación, no de modelo: da igual cuál se
    /// pida, van a fallar todos. Hay que parar y decirlo.
    private func esCuentaEnCero(_ msg: String) -> Bool {
        let m = msg.lowercased()
        return m.contains("credits are depleted") || m.contains("billing")
    }

    /// **Ese modelo** se quedó sin cupo por ahora. Otro puede funcionar, así que vale la pena seguir.
    private func esCupoDelModelo(_ msg: String) -> Bool {
        let m = msg.lowercased()
        return m.contains("quota") || m.contains("rate limit") || m.contains("exhausted")
    }

    private func pedirA(_ modelo: String) async throws -> Respuesta {
        let cuerpo: [String: Any] = [
            "contents": historia,
            "system_instruction": ["parts": [["text": instrucciones]]],
            "generationConfig": [
                "temperature": 1.0,          // alta a propósito: un espejo predecible no es un espejo
                // 2000 y no 200, aunque conteste con una frase. Este modelo PIENSA antes de hablar, y
                // lo pensado sale del mismo presupuesto: con 200 se lo gastaba entero razonando y
                // devolvía la respuesta cortada a media palabra («Here is»). El tope no limita lo
                // largo que habla —de eso se encargan las instrucciones—, limita cuánto puede pensar.
                "maxOutputTokens": 2000,
                // Que piense poco. Sin tope se iba a 557 tokens de razonamiento para contestar un
                // saludo: en una conversación hablada eso es latencia pura, y aquí lo que se premia
                // es contestar como contesta una persona, no razonar como un examinado.
                "thinkingConfig": ["thinkingBudget": 128],
                "responseMimeType": "application/json",
                "responseSchema": [
                    "type": "OBJECT",
                    "properties": [
                        "decir": ["type": "STRING"],
                        "talante": ["type": "STRING",
                                    "enum": Talante.allCases.map(\.rawValue) + ["ninguno"]],
                    ],
                    "required": ["decir", "talante"],
                ],
            ],
        ]

        var pet = URLRequest(url: URL(string:
            "https://generativelanguage.googleapis.com/v1beta/models/\(modelo):generateContent?key=\(llave)")!)
        pet.httpMethod = "POST"
        pet.setValue("application/json", forHTTPHeaderField: "Content-Type")
        pet.httpBody = try JSONSerialization.data(withJSONObject: cuerpo)
        pet.timeoutInterval = 30

        let (datos, _) = try await URLSession.shared.data(for: pet)
        guard let raiz = try JSONSerialization.jsonObject(with: datos) as? [String: Any] else {
            throw Fallo.respuestaRara
        }
        if let error = raiz["error"] as? [String: Any], let msg = error["message"] as? String {
            throw Fallo.deGoogle(msg)
        }
        guard let cands = raiz["candidates"] as? [[String: Any]],
              let contenido = cands.first?["content"] as? [String: Any],
              let partes = contenido["parts"] as? [[String: Any]],
              let crudo = partes.compactMap({ $0["text"] as? String }).first,
              let json = try JSONSerialization.jsonObject(with: Data(crudo.utf8)) as? [String: Any],
              let decir = json["decir"] as? String
        else { throw Fallo.respuestaRara }

        historia.append(["role": "model", "parts": [["text": crudo]]])

        let talante = (json["talante"] as? String).flatMap { Talante(rawValue: $0) }
        return Respuesta(decir: decir, talante: talante)
    }

    enum Fallo: LocalizedError {
        case respuestaRara
        case sinModelos
        case sinCreditos
        case todosSinSaldo
        case deGoogle(String)

        /// Lo que se DICE en voz alta. Corto, en español y sin jerga: la carita estaba leyendo en voz
        /// alta el error crudo de Google —en inglés, con una URL de facturación dentro— y eso, dicho
        /// por un sintetizador español, sonaba a nada. Un mensaje que no se puede pronunciar no es un
        /// mensaje.
        var errorDescription: String? {
            switch self {
            case .respuestaRara:  return "Me contestaron algo raro. Vuelve a preguntarme."
            case .sinModelos:     return "Mi llave no tiene ningún modelo habilitado."
            case .sinCreditos:    return "Me quedé sin créditos para pensar. Hay que recargar la cuenta de Gemini."
            case .todosSinSaldo:  return "Me quedé sin créditos para pensar."
            case .deGoogle:       return "No pude conectarme. Inténtalo otra vez."
            }
        }
    }
}
