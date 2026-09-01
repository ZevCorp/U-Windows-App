import Foundation

/// Conversación en vivo con OpenAI Realtime (`gpt-realtime-2.1-mini`), en vez de Gemini.
///
/// Misma forma pública que `Vivo` a propósito — `viva`, `arrancar()`, `terminar()`, `decirle()`, los
/// cuatro callbacks — para que se pueda usar donde hoy se usa `Vivo` sin tocar `main.swift` más que
/// en la línea que decide cuál construir. Lo que cambia por dentro es el protocolo de cable: los dos
/// hablan con un socket de audio en los dos sentidos, pero ni el formato de los mensajes ni el ritmo
/// del audio son iguales.
///
/// Portado leyendo el comportamiento de la referencia (`voz/Realtime/ProtocoloOpenAI.cs`, en
/// `origin/main`, escrita por Felipe y comprobada contra el servidor real el 2026-08-24) — la regla
/// `solo-mac` pide reescribir, no copiar, y aquí además es otro lenguaje. Tres diferencias con Gemini
/// que no dan error si se ignoran, y por eso muerden:
///
///   1. EL RITMO DE ENTRADA ES 24 kHz, no 16 kHz. Mandar el equivocado no falla — el servidor oye
///      una voz acelerada o ralentizada y la transcripción se vuelve basura.
///   2. HAY QUE PEDIR LA RESPUESTA. Gemini sigue solo después de un resultado de herramienta; aquí,
///      sin un `response.create` explícito, el modelo se queda con el resultado en la mano y callado
///      para siempre — el síntoma exacto del que veníamos huyendo con Gemini.
///   3. NO HAY PASE DE REANUDACIÓN NI VIDEO EN VIVO. Se dice, no se disimula: una caída aquí siempre
///      empieza de cero, y no hay fotograma por segundo — solo fotos sueltas, a pedido (no
///      implementado todavía, igual que en `Vivo`).
final class VivoOpenAI: NSObject, URLSessionWebSocketDelegate {

    private static let modeloFijo = "gpt-realtime-2.1-mini"

    /// LA VOZ ES FIJA, misma razón que en `Vivo`: sin pedirla, Ü sonaría distinta en cada sesión.
    private static let voz = "marin"

    private let llave: String
    private var sesion: URLSession?
    private var socket: URLSessionWebSocketTask?
    /// 24 kHz en los dos sentidos — el ritmo lo fija OpenAI, distinto al de Gemini.
    private let audio = AudioVivo(ritmoEntrada: 24_000, ritmoSalida: 24_000)

    private(set) var viva = false
    private var reintentos = 0
    private var cayoSolo = false

    // ── Lo que la carita escucha — MISMA FORMA que `Vivo`, para poder intercambiarlos ──────────────

    var alCambiar: ((Bool) -> Void)?
    var alTalante: ((Talante) -> Void)?
    var alTranscribir: ((String, Bool) -> Void)?
    var alFallar: ((String) -> Void)?

    private var ultimaVezQueTeOyo = Date.distantPast
    var teOye: Bool { Date().timeIntervalSince(ultimaVezQueTeOyo) < 1.2 }

    var hablando: Bool { audio.hablando }
    var nivelVoz: Double { audio.nivelSalida }

    init(llave: String) { self.llave = llave }

    private var modelo: String {
        if let f = ProcessInfo.processInfo.environment["U_OPENAI_MODEL"], !f.isEmpty { return f }
        return Self.modeloFijo
    }

    // ── Abrir ────────────────────────────────────────────────────────────────────────────────────

    func arrancar() {
        guard !viva else { return }
        Task { await abrir(intento: 0) }
    }

    private func abrir(intento: Int) async {
        do {
            let m = modelo
            let conf = URLSessionConfiguration.default
            conf.timeoutIntervalForRequest = 15
            let s = URLSession(configuration: conf, delegate: self, delegateQueue: nil)

            // LA CLAVE VA EN LA CABECERA, no en la URL — al revés que Gemini. Una URL con la clave
            // dentro acaba en cualquier log de proxy o de red; una cabecera Authorization no.
            var peticion = URLRequest(url: URL(string:
                "wss://api.openai.com/v1/realtime?model=\(m.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? m)")!)
            peticion.setValue("Bearer \(llave)", forHTTPHeaderField: "Authorization")
            let ws = s.webSocketTask(with: peticion)
            sesion = s; socket = ws
            ws.resume()

            try await conReloj(segundos: 15) { [self] in try await enviar(configuracion(modelo: m)) }
            try audio.abrir()
            audio.alCapturar = { [weak self] pcm in self?.mandarTrozo(pcm) }

            viva = true
            cayoSolo = false
            alCambiar?(true)
            Registro.di("🎙 sesión de voz (OpenAI) abierta con «\(m)»")
            recibir()
        } catch {
            Registro.di("🎙 ✘ no se pudo abrir la voz (OpenAI): \(error)")
            terminar()
            if Self.esDeRed(error), intento < 2 {
                try? await Task.sleep(nanoseconds: UInt64(1 + intento) * 1_000_000_000)
                Registro.di("🎙 reintentando abrir la voz OpenAI (\(intento + 2)/3)…")
                await abrir(intento: intento + 1)
                return
            }
            alFallar?(Self.esDeRed(error)
                ? "No pude abrir la voz. Lo intenté tres veces; revisa la conexión."
                : (error as? LocalizedError)?.errorDescription ?? "No pude abrir la voz en vivo.")
        }
    }

    private static func esDeRed(_ e: Error) -> Bool {
        let n = e as NSError
        guard n.domain == NSURLErrorDomain else { return false }
        return [NSURLErrorNotConnectedToInternet, NSURLErrorNetworkConnectionLost,
                NSURLErrorCannotFindHost, NSURLErrorCannotConnectToHost,
                NSURLErrorTimedOut, NSURLErrorDNSLookupFailed].contains(n.code)
    }

    func terminar() {
        guard viva || socket != nil else { return }
        viva = false
        audio.alCapturar = nil
        audio.cerrar()
        socket?.cancel(with: .goingAway, reason: nil)
        socket = nil
        sesion?.invalidateAndCancel()
        sesion = nil
        alCambiar?(false)
        Registro.di("🎙 sesión de voz (OpenAI) cerrada")
    }

    // ── El setup ─────────────────────────────────────────────────────────────────────────────────

    /// QUIÉN DECIDE QUE TERMINASTE DE HABLAR, y por CUÁNTO TIEMPO EN SILENCIO — pedido así el
    /// 2026-08-31: «se queda con cara de que escucha y no responde» es el síntoma exacto de
    /// `semantic_vad`, que decide por SIGNIFICADO y trae un tope de espera que no se controla
    /// directo (`eagerness`, solo low/medium/high/auto). `server_vad` es la otra mitad del catálogo
    /// documentado por OpenAI: decide por silencio cronometrado, con `silence_duration_ms` como
    /// perilla explícita — lo mismo que ya se afinó una vez para Gemini (700 ms, «dentro de la banda
    /// recomendada 500-800» según su propia nota).
    ///
    /// Los valores de abajo son ESE MISMO punto de partida portado, no una medición nueva contra
    /// OpenAI — falta afinarlos con uso real, y por eso `U_SILENCIO_MS` los deja tocar sin
    /// recompilar.
    ///
    /// La interrupción va atada a la MISMA perilla que ya gobierna si el micrófono se calla
    /// mientras ella habla (`mandarTrozo`, más abajo): pedirle al servidor que permita interrumpir
    /// (`interrupt_response`) mientras el cliente NO manda audio durante su turno sería una promesa
    /// que nunca se puede cumplir. Las dos tienen que decir lo mismo.
    private static var deteccionDeVoz: [String: Any] {
        // REVERTIDO el 2026-08-31: probado en vivo, `server_vad` con estos valores dejó la sesión
        // COMPLETAMENTE MUDA — ni un solo evento del servidor durante casi 2 minutos, ni siquiera
        // un «error». Es peor que el síntoma que intentaba arreglar (demora, no silencio total), y
        // no se pudo diagnosticar la causa exacta sin arriesgar dejar la voz rota más tiempo. Vuelve
        // a `semantic_vad`, que SÍ está probado funcionando hoy mismo, hasta investigar aparte por
        // qué `server_vad` se calla — sospecha sin confirmar: puede que mandar audio continuo
        // (`mandarTrozo`) A LA VEZ que un `response.create` manual por texto confunda al VAD del
        // servidor sobre si el turno sigue abierto.
        let dejanInterrumpir = ProcessInfo.processInfo.environment["U_BARGE_IN"] == "1"
        // «high» en vez de «auto» — pedido el 2026-08-31 porque «a veces no responde». Documentado
        // por OpenAI como «qué tan pronta es a responder, ajustando el tope de espera máximo» — un
        // cambio DENTRO de semantic_vad, sin tocar el tipo, que es lo que se probó y se rompió.
        // Puede tocarse sin recompilar con U_EAGERNESS para volver a «auto» si esto empeora algo.
        let eagerness = ProcessInfo.processInfo.environment["U_EAGERNESS"] ?? "high"
        return [
            "type": "semantic_vad",
            "eagerness": eagerness,
            "create_response": true,
            "interrupt_response": dejanInterrumpir,
        ]
    }

    private func configuracion(modelo m: String) -> [String: Any] {
        [
            "type": "session.update",
            "session": [
                "type": "realtime",
                "model": m,
                "instructions": Self.instrucciones,
                "audio": [
                    "input": [
                        "format": ["type": "audio/pcm", "rate": Int(audio.ritmoEntrada)],
                        "turn_detection": Self.deteccionDeVoz,
                        "transcription": ["model": "gpt-4o-mini-transcribe"],
                    ],
                    "output": [
                        "format": ["type": "audio/pcm", "rate": Int(audio.ritmoSalida)],
                        "voice": Self.voz,
                    ],
                ],
                "tools": [Self.herramientaCara],
                "tool_choice": "auto",
            ],
        ]
    }

    /// Mismo talante que en `Vivo`, con la forma que pide OpenAI: la herramienta va plana (no
    /// anidada en `functionDeclarations` como en Gemini) y `required` va vacío — así lo comprobó
    /// Felipe contra el servidor real, no se inventa aquí.
    private static let herramientaCara: [String: Any] = [
        "type": "function",
        "name": "poner_cara",
        "description": "Pon tu cara. Llámala ANTES de hablar, cada vez que tu reacción cambie.",
        "parameters": [
            "type": "object",
            "properties": [
                "talante": ["type": "string", "enum": Talante.allCases.map(\.rawValue)],
            ],
            "required": [String](),
        ],
    ]

    private static var instrucciones: String {
        Cerebro.quienEs + """


        LA CARA:
        Antes de hablar, llama a la herramienta `poner_cara` con el talante que corresponde, y
        vuelve a llamarla cada vez que tu reacción cambie de verdad. No la llames por cortesía: una
        cara que cambia en cada frase no expresa nada. Si ninguna encaja, no la llames.

        """ + Cerebro.lasCaras
    }

    // ── Mandar ───────────────────────────────────────────────────────────────────────────────────

    private func conReloj(segundos: Double, _ obra: @escaping () async throws -> Void) async throws {
        try await withThrowingTaskGroup(of: Void.self) { grupo in
            grupo.addTask { try await obra() }
            grupo.addTask {
                try await Task.sleep(nanoseconds: UInt64(segundos * 1_000_000_000))
                throw Fallo.seColgo
            }
            try await grupo.next()
            grupo.cancelAll()
        }
    }

    private func enviar(_ objeto: [String: Any]) async throws {
        let datos = try JSONSerialization.data(withJSONObject: objeto)
        try await socket?.send(.string(String(decoding: datos, as: UTF8.self)))
    }

    private var trozosCallados = 0

    private func mandarTrozo(_ pcm: Data) {
        guard viva, let socket else { return }

        // MISMA COMPUERTA QUE `Vivo`: mientras habla, el micrófono no sube nada. OpenAI ni siquiera
        // manda un aviso de «te interrumpieron» como Gemini — la única señal es que EMPEZASTE a
        // hablar (`input_audio_buffer.speech_started`), así que evitar que el eco dispare esa señal
        // es aún más necesario aquí, no menos.
        if audio.hablando, ProcessInfo.processInfo.environment["U_BARGE_IN"] != "1" {
            trozosCallados += 1
            if trozosCallados % 50 == 0 {
                Registro.di("🎙 ⇣ callo el micrófono mientras hablo (\(trozosCallados) trozos)")
            }
            return
        }
        let mensaje: [String: Any] = ["type": "input_audio_buffer.append", "audio": pcm.base64EncodedString()]
        guard let datos = try? JSONSerialization.data(withJSONObject: mensaje) else { return }
        socket.send(.string(String(decoding: datos, as: UTF8.self))) { error in
            if let error { Registro.di("🎙 ✘ mandando audio: \(error.localizedDescription)") }
        }
    }

    /// Decirle algo por escrito sin colgar la conversación — mismo propósito que en `Vivo`: lo que
    /// venía detrás de «Mira» no se pierde. A diferencia de Gemini, aquí hace falta pedir la
    /// respuesta aparte (`response.create`) o el mensaje se queda leído y sin contestar.
    func decirle(_ texto: String) {
        let limpio = texto.trimmingCharacters(in: .whitespacesAndNewlines)
        guard viva else {
            if !limpio.isEmpty { Registro.di("🎙 ✋ descarto «\(limpio)»: la sesión viva no está abierta") }
            return
        }
        guard !limpio.isEmpty else { return }
        Registro.di("🎙 le paso lo que dijiste al despertarla: «\(limpio)»")
        Task {
            try? await enviar([
                "type": "conversation.item.create",
                "item": ["type": "message", "role": "user", "content": [["type": "input_text", "text": limpio]]],
            ])
            try? await enviar(["type": "response.create"])
        }
    }

    // ── Recibir ──────────────────────────────────────────────────────────────────────────────────

    private func recibir() {
        socket?.receive { [weak self] resultado in
            guard let self else { return }
            switch resultado {
            case .failure(let error):
                guard viva else { return }
                Registro.di("🎙 ✘ el socket se cayó: \(error.localizedDescription)")
                cayoSolo = true
                reconectar()
            case .success(let mensaje):
                let datos: Data
                switch mensaje {
                case .data(let d):   datos = d
                case .string(let t): datos = Data(t.utf8)
                @unknown default:    datos = Data()
                }
                atender(datos)
                recibir()
            }
        }
    }

    private func atender(_ datos: Data) {
        guard let raiz = try? JSONSerialization.jsonObject(with: datos) as? [String: Any],
              let tipo = raiz["type"] as? String else { return }

        switch tipo {
        case "response.output_audio.delta":
            if let b64 = raiz["delta"] as? String, let pcm = Data(base64Encoded: b64) {
                audio.reproducir(pcm)
            }

        case "response.output_audio_transcript.delta":
            if let texto = raiz["delta"] as? String, !texto.isEmpty { alTranscribir?(texto, true) }

        case "conversation.item.input_audio_transcription.delta":
            if let texto = raiz["delta"] as? String, !texto.isEmpty {
                ultimaVezQueTeOyo = Date()
                alTranscribir?(texto, false)
            }

        // HABLÓ ENCIMA. OpenAI no manda un «interrumpido» como Gemini — el aviso es que EMPEZÓ a
        // hablar, y hay que atenderlo igual: callar lo que quedaba sonando en la cola.
        case "input_audio_buffer.speech_started":
            Registro.di("🎙 me interrumpiste")
            audio.callar()

        case "response.function_call_arguments.done":
            atenderHerramienta(raiz)

        case "response.done":
            reintentos = 0

        case "error":
            let msg = (raiz["error"] as? [String: Any])?["message"] as? String ?? "error sin detalle"
            Registro.di("🎙 ✘ el servidor dice: \(msg)")

        default:
            // TODO lo que no se sabe interpretar SE ANOTA, misma disciplina que en `Vivo`: un tipo
            // de evento nuevo que se ignora en silencio deja la sesión abierta y ninguna pista de
            // por qué no contesta.
            if !["session.created", "session.updated", "response.created",
                 "response.output_item.added", "response.output_item.done",
                 "response.content_part.added", "response.content_part.done",
                 "conversation.item.created", "rate_limits.updated"].contains(tipo) {
                let plano = String(decoding: datos, as: UTF8.self).replacingOccurrences(of: "\n", with: " ")
                Registro.di("🎙 ← \(plano.count > 400 ? String(plano.prefix(400)) + "…" : plano)")
            }
        }
    }

    private func atenderHerramienta(_ evento: [String: Any]) {
        let id = evento["call_id"] as? String ?? ""
        let nombre = evento["name"] as? String ?? ""

        // LOS ARGUMENTOS VIENEN COMO TEXTO con un JSON dentro, no como objeto — el pinchazo exacto
        // que ya documentó Felipe en C#: no da error, da argumentos vacíos y la herramienta hace
        // otra cosa. Se parsea el string aparte.
        var args: [String: Any] = [:]
        if let crudo = evento["arguments"] as? String, !crudo.isEmpty,
           let datos = crudo.data(using: .utf8),
           let obj = try? JSONSerialization.jsonObject(with: datos) as? [String: Any] {
            args = obj
        }

        if nombre == "poner_cara", let etiqueta = args["talante"] as? String, let t = Talante(rawValue: etiqueta) {
            Registro.di("🎙 cara «\(etiqueta)»")
            DispatchQueue.main.async { [weak self] in self?.alTalante?(t) }
        }

        Task {
            try? await enviar([
                "type": "conversation.item.create",
                "item": ["type": "function_call_output", "call_id": id, "output": "ok"],
            ])
            // SIN ESTO EL MODELO SE QUEDA CALLADO PARA SIEMPRE — la diferencia nº2 del encabezado.
            // Gemini sigue solo; aquí hay que pedirlo, una vez por tanda de herramientas.
            try? await enviar(["type": "response.create"])
        }
    }

    // ── Volver tras una caída ────────────────────────────────────────────────────────────────────

    /// SIN PASE, porque no lo hay: OpenAI no da uno para retomar la MISMA conversación (`SabeVolver`
    /// es falso en la referencia). Cada reintento aquí es siempre una conversación nueva, y se dice
    /// así en vez de fingir que se recuperó lo anterior.
    private func reconectar() {
        guard cayoSolo, reintentos < 3 else {
            Registro.di("🎙 ✘ me rindo tras \(reintentos) intentos — cierro y devuelvo el micrófono")
            reintentos = 0
            terminar()
            alFallar?("Se cortó la conversación y no pude retomarla.")
            return
        }
        reintentos += 1
        let n = reintentos
        Registro.di("🎙 reabro la voz OpenAI (\(n)/3) — sin pase, empieza de cero")
        viva = false
        audio.alCapturar = nil
        audio.cerrar()
        socket = nil
        sesion?.invalidateAndCancel(); sesion = nil
        Task {
            try? await Task.sleep(nanoseconds: UInt64(pow(2.0, Double(n - 1)) * 1_000_000_000))
            await abrir(intento: 0)
        }
    }

    enum Fallo: LocalizedError {
        case seColgo
        var errorDescription: String? { "La voz tardó demasiado en abrir. Vuelve a intentarlo." }
    }
}
