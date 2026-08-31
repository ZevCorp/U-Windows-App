import Foundation

/// Conversación en vivo con Gemini: el caño de audio abierto en los dos sentidos.
///
/// La diferencia con `Cerebro` no es la calidad de la voz — es **quién decide el turno**. Por texto,
/// `Oido` abre el micrófono, espera una frase, la cierra y recién ahí se pregunta; aquí el audio va y
/// viene sin parar y el modelo puede callarse en cuanto empiezas a hablar. Eso es lo que hace que se
/// pueda **interrumpir**, que es la mitad de lo que separa una conversación de un intercomunicador.
///
/// Portado del cliente de Windows (`windows-client/src/Voice/GeminiLive.cs`, en `main`) leyendo su
/// comportamiento y sus razones, sin tocar aquel código: la regla `solo-mac` pide reescribir, no
/// copiar. Lo que aquel trae y este NO, a propósito y por ahora: los ojos (un fotograma por segundo
/// de la pantalla), el collar Omi, las herramientas del grafo de SAP, y el reporte de consumo.
final class Vivo: NSObject, URLSessionWebSocketDelegate {

    private static let host = "wss://generativelanguage.googleapis.com/ws/"
        + "google.ai.generativelanguage.v1beta.GenerativeService.BidiGenerateContent"

    /// LA VOZ ES FIJA, y no es capricho: sin pedirla, el servidor sortea una de las treinta en cada
    /// sesión nueva y Ü sonaba distinta cada vez que se abría una conversación (visto en Windows el
    /// 2026-08-10). Una voz que cambia sola no es una voz, es un altavoz.
    private static let voz = "Iapetus"

    private let llave: String
    private var sesion: URLSession?
    private var socket: URLSessionWebSocketTask?
    private let audio = AudioVivo()

    private(set) var viva = false

    /// El pase para volver a la MISMA conversación si se cae el socket. **No es un lujo:** el
    /// servidor corta cuando le parece —en Windows se midieron cortes a los 36 s, a los 2 min y a los
    /// 3 min la misma tarde— y sin esto cada corte apagaba la conversación a media frase.
    private var pase = ""
    private var reintentos = 0
    /// El último cierre no lo pedimos nosotros.
    private var cayoSolo = false

    // ── Lo que la carita escucha ─────────────────────────────────────────────────────────────────

    /// Arrancó o terminó.
    var alCambiar: ((Bool) -> Void)?
    /// El modelo pidió una cara. Llega por llamada de herramienta, que es lo ÚNICO que llega antes
    /// del audio: la transcripción de lo que dice viene detrás de haberlo dicho, y una cara que
    /// aparece después de la frase llega tarde.
    var alTalante: ((Talante) -> Void)?
    /// Lo que se oyó (`esDeU == false`) y lo que contestó (`true`). Solo para el registro.
    var alTranscribir: ((String, Bool) -> Void)?
    /// Algo se rompió y hay que decirlo en voz alta.
    var alFallar: ((String) -> Void)?

    /// Te está oyendo decir algo AHORA MISMO.
    ///
    /// Es el equivalente de `Oido.hayVoz` para la sesión viva, y hace falta porque durante una
    /// conversación en vivo el oído local está cedido: `hayVoz` es false para siempre, así que la
    /// cara de escuchar NO PODÍA SALIR NUNCA — que es exactamente lo que se notó al probarla
    /// (2026-08-18). La señal es la transcripción de lo que entra: llega mientras hablas.
    ///
    /// Se sostiene un momento porque entre dos palabras de una frase hay silencio, y una cara que se
    /// enciende y se apaga tres veces por frase no está atendiendo, está parpadeando.
    private var ultimaVezQueTeOyo = Date.distantPast
    var teOye: Bool { Date().timeIntervalSince(ultimaVezQueTeOyo) < 1.2 }

    /// Está sonando ahora mismo, y cuánto. La boca de la carita se mueve con esto.
    var hablando: Bool { audio.hablando }
    var nivelVoz: Double { audio.nivelSalida }

    init(llave: String) { self.llave = llave }

    // ── Qué modelo ───────────────────────────────────────────────────────────────────────────────

    private static var modeloElegido = ""

    /// SE PREGUNTA, no se supone. Escribir «gemini-3.1-flash-live» a mano es apostar a que el
    /// catálogo de Google no se mueva, y se mueve más rápido que nuestros despliegues: el día que
    /// cambie, la voz deja de arrancar con un error que no dice nada. La API sabe cuáles hablan en
    /// vivo — son los que soportan `bidiGenerateContent`.
    private func modelo() async throws -> String {
        if let f = ProcessInfo.processInfo.environment["U_LIVE_MODEL"], !f.isEmpty { return f }
        if !Self.modeloElegido.isEmpty { return Self.modeloElegido }

        let url = URL(string: "https://generativelanguage.googleapis.com/v1beta/models?pageSize=200&key=\(llave)")!
        let (datos, _) = try await URLSession.shared.data(from: url)
        guard let raiz = try JSONSerialization.jsonObject(with: datos) as? [String: Any] else {
            throw Fallo.respuestaRara
        }
        if let e = raiz["error"] as? [String: Any], let m = e["message"] as? String { throw Fallo.deGoogle(m) }

        let vivos = ((raiz["models"] as? [[String: Any]]) ?? []).compactMap { m -> String? in
            guard let metodos = m["supportedGenerationMethods"] as? [String],
                  metodos.contains(where: { $0.caseInsensitiveCompare("bidiGenerateContent") == .orderedSame }),
                  let nombre = m["name"] as? String else { return nil }
            return nombre.replacingOccurrences(of: "models/", with: "")
        }
        guard !vivos.isEmpty else { throw Fallo.sinVoz }

        // No todo lo que habla en vivo sirve para esto: la lista trae también un modelo de robótica y
        // uno de traducción simultánea, que hablarían pero no son un asistente.
        var utiles = vivos.filter { !$0.localizedCaseInsensitiveContains("robotics")
                                 && !$0.localizedCaseInsensitiveContains("translate") }
        if utiles.isEmpty { utiles = vivos }
        let elegido = utiles.first(where: { $0.localizedCaseInsensitiveContains("flash-live") })
            ?? utiles.first(where: { $0.localizedCaseInsensitiveContains("flash") })
            ?? utiles[0]
        Self.modeloElegido = elegido
        Registro.di("🎙 modelos con voz en vivo: \(vivos.joined(separator: ", ")) → se usa «\(elegido)»")
        return elegido
    }

    // ── Abrir ────────────────────────────────────────────────────────────────────────────────────

    func arrancar() {
        guard !viva else { return }
        Task { await abrir(intento: 0) }
    }

    private func abrir(intento: Int) async {
        do {
            let m = try await modelo()
            let conf = URLSessionConfiguration.default
            // CON TIEMPO LÍMITE. Sin esto, un socket que no llega a conectar deja el `send` del setup
            // esperando para siempre: la sesión ni abre ni falla, y en el registro queda la lista de
            // modelos y después nada — que es el peor sitio donde dejar a alguien buscando (se vio
            // dos veces el 2026-08-18, al reabrir la app justo tras cerrarla).
            conf.timeoutIntervalForRequest = 15
            let s = URLSession(configuration: conf, delegate: self, delegateQueue: nil)
            let ws = s.webSocketTask(with: URL(string: "\(Self.host)?key=\(llave)")!)
            sesion = s; socket = ws
            ws.resume()

            // Y el envío del setup, con su propio reloj: el tiempo límite de la petición no cubre
            // un `send` que se queda esperando a que el socket termine de abrirse.
            let arranque = configuracion(modelo: m, pase: pase)
            try await conReloj(segundos: 15) { [self] in try await enviar(arranque) }
            try audio.abrir()
            audio.alCapturar = { [weak self] pcm in self?.mandarTrozo(pcm) }

            viva = true
            // AQUÍ NO SE PONE EL CONTADOR A CERO, y ese fue el bug: abrir no es conversar.
            //
            // Estaba puesto aquí, y el socket que se caía a los 200 ms alcanzaba a pasar por esta
            // línea antes de morir. Así el contador volvía a cero en cada vuelta, el tope de tres no
            // se alcanzaba jamás, y el resultado fue un bucle infinito —2.089 vueltas medidas el
            // 2026-08-18, una por segundo— abriendo y cerrando el motor de audio. Desde fuera: el
            // punto naranja del micrófono parpadeando sin parar con la app «cerrada».
            //
            // Se pone a cero cuando llega CONTENIDO de verdad, en `atender()`. Esa es la prueba de
            // que la conversación revivió; ésta solo prueba que el socket saludó.
            cayoSolo = false
            alCambiar?(true)
            Registro.di("🎙 sesión de voz abierta con «\(m)»\(pase.isEmpty ? "" : " (reanudada)")")
            recibir()
        } catch {
            Registro.di("🎙 ✘ no se pudo abrir la voz: \(error)")
            terminar()
            // Un corte de red de unos segundos no debería costarle un gesto al usuario. Pero solo se
            // reintenta lo que puede arreglarse solo: una llave inválida va a fallar igual las tres
            // veces, y reintentarla solo retrasa el momento de enterarse.
            if Self.esDeRed(error), intento < 2 {
                try? await Task.sleep(nanoseconds: UInt64(1 + intento) * 1_000_000_000)
                Registro.di("🎙 reintentando abrir la voz (\(intento + 2)/3)…")
                await abrir(intento: intento + 1)
                return
            }
            alFallar?(Self.esDeRed(error)
                ? "No pude abrir la voz. Lo intenté tres veces; revisa la conexión."
                : (error as? LocalizedError)?.errorDescription ?? "No pude abrir la voz en vivo.")
        }
    }

    /// Si el fallo es del CAMINO —de los que se arreglan solos— y no del otro lado diciendo que no.
    /// Se mira el código, no el texto: los mensajes del sistema vienen traducidos al idioma del Mac,
    /// así que buscar palabras dentro funcionaría en esta máquina y en ninguna otra.
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
        Registro.di("🎙 sesión de voz cerrada")
    }

    // ── El setup ─────────────────────────────────────────────────────────────────────────────────

    private func configuracion(modelo m: String, pase: String) -> [String: Any] {
        var setup: [String: Any] = [
            "model": "models/\(m)",
            "generationConfig": [
                "responseModalities": ["AUDIO"],
                // EN ESPAÑOL, dicho explícitamente. Sin pedirlo, el modelo lo deduce del audio — y
                // con audio dudoso deduce mal: en la primera prueba transcribió portugués sobre una
                // frase en castellano. Las instrucciones están en español, pero eso guía lo que dice,
                // no el idioma en que decide oír y hablar.
                "speechConfig": [
                    "voiceConfig": ["prebuiltVoiceConfig": ["voiceName": Self.voz]],
                    "languageCode": "es-US",
                ],
            ],
            "systemInstruction": ["parts": [["text": Self.instrucciones]]],
            "tools": [["functionDeclarations": [Self.herramientaCara]]],
            // Las dos transcripciones son para el REGISTRO, no para la cara. Sin ellas, cuando la
            // conversación sale mal no hay forma de saber qué entendió: solo queda audio que ya no
            // existe. Es la diferencia entre diagnosticar y adivinar.
            "inputAudioTranscription": [:],
            "outputAudioTranscription": [:],
            "realtimeInputConfig": Self.deteccionDeVoz,
            "sessionResumption": pase.isEmpty ? [:] : ["handle": pase],
        ]
        if pase.isEmpty { setup["sessionResumption"] = [String: Any]() }
        return ["setup": setup]
    }

    /// QUIÉN DECIDE QUE ESTÁS HABLANDO: el servidor. Y esto se aprendió caro.
    ///
    /// En Windows hubo un detector propio del 2026-08-04 al 2026-08-16: umbral aprendido sobre el
    /// ruido de sala más un margen sobre el eco de la propia Ü. Hacía IMPOSIBLE interrumpir, porque
    /// interrumpir es hablar MIENTRAS ella habla — justo cuando aquel umbral estaba más alto. No era
    /// cuestión de calibrarlo: usaba UN número, el volumen, para contestar dos preguntas que por
    /// volumen son indistinguibles («¿esto es mi eco?» y «¿esto es alguien cortándome?»). Los dos
    /// síntomas que lo destaparon fueron «nunca puedo interrumpirlo» y «me toca hablarle muy duro».
    ///
    /// Las cuatro perillas, y por qué estas:
    ///   · startOfSpeechSensitivity LOW — el ruido de sala leído como voz era el problema original, y
    ///     ESTA es la perilla para eso; viene en HIGH por defecto.
    ///   · endOfSpeechSensitivity LOW — no dar el turno por cerrado en la primera pausa. Que te
    ///     corten a media frase se siente como no ser escuchado.
    ///   · silenceDurationMs 700 — dentro de la banda recomendada (500–800). Por debajo, el audio se
    ///     fragmenta y la transcripción se degrada.
    ///   · prefixPaddingMs 20 — que no se coma el arranque de la primera sílaba.
    ///
    /// Y LA QUINTA, `activityHandling`, que decidió el uso real y no la teoría.
    ///
    /// Por defecto vale `START_OF_ACTIVITY_INTERRUPTS`: cualquier voz que entre le corta el turno. En
    /// un escritorio con gente hablando alrededor eso no significa «me pueden interrumpir», significa
    /// **que no llega a decir una frase entera nunca**. El registro del 2026-08-18 lo enseña seguido:
    ///
    ///     🎙 dice: «A ver,» · «¿qué pasa?»
    ///     🎙 me interrumpiste            ← 150 ms después
    ///
    /// …y lo que la interrumpía no era la usuaria, era la conversación de la sala entrando por el
    /// micrófono («me voy para donde Tommy», «fue una semana movidita»). Ü llegó a preguntar «¿me
    /// están hablando a mí?». Con el altavoz vaciado en cada corte, desde fuera se ve exactamente lo
    /// que se reportó: la carita con cara de escuchar, callada.
    ///
    /// Así que se pide NO_INTERRUPTION. Se pierde el barge-in, y es una pérdida real — pero poder
    /// cortarla no vale nada si nunca llega a hablar. `U_BARGE_IN=1` devuelve el comportamiento de
    /// fábrica para cuando haya con qué distinguir la voz de la dueña del resto de la sala.
    private static var deteccionDeVoz: [String: Any] {
        let dejarQueCorten = ProcessInfo.processInfo.environment["U_BARGE_IN"] == "1"
        return [
            "automaticActivityDetection": [
                "startOfSpeechSensitivity": "START_SENSITIVITY_LOW",
                "endOfSpeechSensitivity": "END_SENSITIVITY_LOW",
                "silenceDurationMs": 700,
                "prefixPaddingMs": 20,
            ],
            "activityHandling": dejarQueCorten ? "START_OF_ACTIVITY_INTERRUPTS" : "NO_INTERRUPTION",
        ]
    }

    /// LA CARA, PEDIDA COMO HERRAMIENTA.
    ///
    /// Por texto el talante viaja delante entre corchetes y por eso la cara llega ANTES del primer
    /// sonido — medio segundo, está medido en el registro. En voz nativa no hay texto donde meterlo.
    /// La transcripción de salida no sirve: llega detrás del audio, o sea que la cara aparecería
    /// cuando la frase ya se dijo. Una llamada de herramienta sí llega antes, y además es explícita:
    /// el modelo elige la cara a sabiendas en vez de que se la deduzcamos del texto.
    private static let herramientaCara: [String: Any] = [
        "name": "poner_cara",
        "description": "Pon tu cara. Llámala ANTES de hablar, cada vez que tu reacción cambie.",
        "parameters": [
            "type": "OBJECT",
            "properties": [
                "talante": ["type": "STRING", "enum": Talante.allCases.map(\.rawValue)],
            ],
            "required": ["talante"],
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

    /// Corre algo con reloj. Si no termina a tiempo, lanza en vez de esperar para siempre.
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

    /// Cuántos trozos se callaron mientras hablaba, para poder decirlo en el registro en vez de
    /// que el caño adelgace en silencio.
    private var trozosCallados = 0

    private func mandarTrozo(_ pcm: Data) {
        guard viva, let socket else { return }

        // MIENTRAS HABLA, EL MICRÓFONO NO SUBE NADA. Y esto NO es un detector de voz por volumen —
        // eso está prohibido aquí, y con razón: usa un solo número para dos preguntas que por volumen
        // son indistinguibles. Esto pregunta por un hecho que sabemos con certeza, sin inferir nada:
        // ¿está sonando NUESTRO altavoz? Lo sabe `AudioVivo.hablando`, que cuenta los búferes en cola.
        //
        // El síntoma que arregla, medido el 2026-08-19: NUEVE interrupciones en dos minutos, y la voz
        // saliendo a pedazos («Entendido, no» · «volverá» · «a pasar»). La cancelación de eco tapa lo
        // que sale por el altavoz, pero no del todo: lo que quedaba bastaba para que el servidor
        // abriera turno nuevo, y un turno nuevo CANCELA el anterior a media palabra. O sea que Ü se
        // interrumpía a sí misma con su propio eco.
        //
        // Se pierde poder cortarla hablándole encima. No se pierde nada: ya estaba pedido
        // `NO_INTERRUPTION` por la misma razón, y `U_BARGE_IN=1` devuelve las dos cosas a la vez.
        if audio.hablando, ProcessInfo.processInfo.environment["U_BARGE_IN"] != "1" {
            trozosCallados += 1
            if trozosCallados % 50 == 0 {
                Registro.di("🎙 ⇣ callo el micrófono mientras hablo (\(trozosCallados) trozos)")
            }
            return
        }
        // `realtimeInput.audio`, no `mediaChunks`: el segundo es la forma vieja del protocolo y el
        // servidor la acepta sin quejarse pero sin oírla. Un caño que traga y no contesta.
        let mensaje: [String: Any] = [
            "realtimeInput": [
                "audio": [
                    "mimeType": "audio/pcm;rate=\(Int(audio.ritmoEntrada))",
                    "data": pcm.base64EncodedString(),
                ],
            ],
        ]
        guard let datos = try? JSONSerialization.data(withJSONObject: mensaje) else { return }
        socket.send(.string(String(decoding: datos, as: UTF8.self))) { error in
            if let error { Registro.di("🎙 ✘ mandando audio: \(error.localizedDescription)") }
        }
    }

    /// Decirle algo POR ESCRITO sin colgar la conversación.
    ///
    /// Existe para una frase concreta: «Mira, ¿me escuchas?». El nombre lo reconoce el oído local y
    /// abre la sesión, pero lo que venía DETRÁS del nombre ya se dijo — y si no se le pasa, se pierde
    /// y hay que repetirlo. Repetir lo que acabas de decir es exactamente lo que rompe la sensación
    /// de estar hablando con alguien.
    func decirle(_ texto: String) {
        let limpio = texto.trimmingCharacters(in: .whitespacesAndNewlines)
        // LO QUE NO SE MANDA DEJA RASTRO (patrón nº10). Este `guard` descartaba en silencio lo que
        // le habías dicho cuando la sesión no estaba abierta, y en el registro no quedaba NADA: ni
        // la frase, ni que se hubiera perdido. Un descarte mudo es indistinguible de no haber oído.
        guard viva else {
            if !limpio.isEmpty { Registro.di("🎙 ✋ descarto «\(limpio)»: la sesión viva no está abierta") }
            return
        }
        guard !limpio.isEmpty else { return }
        Registro.di("🎙 le paso lo que dijiste al despertarla: «\(limpio)»")
        Task {
            try? await enviar(["clientContent": [
                "turns": [["role": "user", "parts": [["text": limpio]]]],
                "turnComplete": true,
            ]])
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
        guard let raiz = try? JSONSerialization.jsonObject(with: datos) as? [String: Any] else { return }

        // El pase se guarda cada vez que llega. Llega a menudo y no dice nada más, así que no se
        // registra: llenaría el log y taparía lo que sí importa.
        if let p = raiz["sessionResumptionUpdate"] as? [String: Any] {
            // Solo el que viene marcado reanudable. Guardar uno que no lo es es peor que no tener
            // ninguno: se intenta volver con él, el servidor lo rechaza, y el fallo aparece en la
            // reconexión en vez de aquí.
            let sirve = (p["resumable"] as? Bool) ?? true
            if sirve, let h = p["newHandle"] as? String, !h.isEmpty { pase = h }
        }

        // El servidor avisa de que va a cerrar. Es un cierre ordenado, no una caída: se reconecta con
        // el pase antes de que se note.
        if raiz["goAway"] != nil {
            Registro.di("🎙 el servidor avisa de que cierra — reconecto con el pase")
            cayoSolo = true
            terminar()
            Task { await abrir(intento: 0) }
            return
        }

        if let llamada = raiz["toolCall"] as? [String: Any] {
            atenderHerramienta(llamada)
            return
        }

        // TODO lo que no se sabe interpretar SE ANOTA. Tirarlo en silencio deja el peor diagnóstico
        // posible: la sesión abierta, el micrófono encendido, y ninguna pista de por qué no
        // contesta. Un error del servidor tiene que verse. (Se excluye el pase, que llega cada
        // segundo y llenaría el registro tapando lo que importa.)
        if raiz["serverContent"] == nil, raiz["toolCall"] == nil, raiz["sessionResumptionUpdate"] == nil,
           raiz["setupComplete"] == nil {
            let plano = String(decoding: datos, as: UTF8.self)
                .replacingOccurrences(of: "\n", with: " ")
            Registro.di("🎙 ← \(plano.count > 400 ? String(plano.prefix(400)) + "…" : plano)")
        }

        guard let contenido = raiz["serverContent"] as? [String: Any] else { return }

        // HAY CONVERSACIÓN DE VERDAD OTRA VEZ: el contador de caídas seguidas vuelve a cero. Este es
        // el único sitio donde se puede poner a cero con honradez — llegó contenido, o sea que la
        // sesión no solo abrió, funciona. Ponerlo a cero al abrir el socket es lo que convirtió el
        // tope de tres intentos en un bucle sin fondo.
        reintentos = 0

        // TE INTERRUMPIÓ. Se tira lo que quedaba por decir; si sigue sonando medio segundo después,
        // la interrupción no se siente como tal.
        if contenido["interrupted"] != nil {
            Registro.di("🎙 me interrumpiste")
            audio.callar()
        }

        if let t = contenido["inputTranscription"] as? [String: Any], let texto = t["text"] as? String,
           !texto.isEmpty {
            ultimaVezQueTeOyo = Date()
            alTranscribir?(texto, false)
        }
        if let t = contenido["outputTranscription"] as? [String: Any], let texto = t["text"] as? String,
           !texto.isEmpty {
            alTranscribir?(texto, true)
        }

        if let turno = contenido["modelTurn"] as? [String: Any],
           let partes = turno["parts"] as? [[String: Any]] {
            for parte in partes {
                guard let en = parte["inlineData"] as? [String: Any],
                      let b64 = en["data"] as? String,
                      let pcm = Data(base64Encoded: b64) else { continue }
                audio.reproducir(pcm)
            }
        }
    }

    private func atenderHerramienta(_ llamada: [String: Any]) {
        guard let funciones = llamada["functionCalls"] as? [[String: Any]] else { return }
        var respuestas: [[String: Any]] = []
        for f in funciones {
            let nombre = f["name"] as? String ?? ""
            if nombre == "poner_cara",
               let args = f["args"] as? [String: Any],
               let etiqueta = args["talante"] as? String,
               let t = Talante(rawValue: etiqueta) {
                Registro.di("🎙 cara «\(etiqueta)»")
                DispatchQueue.main.async { [weak self] in self?.alTalante?(t) }
            }
            // SIEMPRE se contesta, aunque no la conozcamos. Una llamada sin respuesta deja al modelo
            // esperando y la conversación se queda muda sin un solo error a la vista.
            respuestas.append([
                "id": f["id"] as? String ?? "",
                "name": nombre,
                "response": ["result": "ok"],
            ])
        }
        guard !respuestas.isEmpty else { return }
        Task { try? await enviar(["toolResponse": ["functionResponses": respuestas]]) }
    }

    // ── Volver tras una caída ────────────────────────────────────────────────────────────────────

    private func reconectar() {
        guard cayoSolo, reintentos < 3 else {
            Registro.di("🎙 ✘ me rindo tras \(reintentos) intentos de retomar — cierro y devuelvo el micrófono")
            pase = ""                       // que el siguiente arranque empiece limpio
            reintentos = 0
            terminar()
            alFallar?("Se cortó la conversación y no pude retomarla.")
            return
        }
        reintentos += 1
        let n = reintentos

        // EL PASE SE TIRA AL SEGUNDO INTENTO. Un pase caducado no falla diciendo «caducado»: el
        // servidor acepta el socket y lo cierra al instante («Socket is not connected»), que se
        // parece a un problema de red y no lo es. Reintentar con el mismo pase reproduce el mismo
        // cierre para siempre — así se llegó a las 2.089 vueltas del 2026-08-18. Si el primer
        // reintento no levanta, el pase deja de ser la solución y pasa a ser el problema.
        if n >= 2, !pase.isEmpty {
            Registro.di("🎙 el pase no sirve — lo tiro y empiezo conversación nueva")
            pase = ""
        }

        Registro.di("🎙 retomo la conversación (\(n)/3)\(pase.isEmpty ? " SIN pase — empieza de cero" : " con el pase")")
        viva = false
        audio.alCapturar = nil
        audio.cerrar()
        socket = nil
        sesion?.invalidateAndCancel(); sesion = nil
        Task {
            // Cada vuelta espera más: 1 s, 2 s, 4 s. Reintentar cada medio segundo contra un servidor
            // que te está cerrando la puerta no es insistir, es golpear.
            try? await Task.sleep(nanoseconds: UInt64(pow(2.0, Double(n - 1)) * 1_000_000_000))
            await abrir(intento: 0)
        }
    }

    enum Fallo: LocalizedError {
        case respuestaRara, sinVoz, seColgo, deGoogle(String)
        var errorDescription: String? {
            switch self {
            case .respuestaRara: return "Me contestaron algo raro al abrir la voz."
            case .sinVoz:        return "Mi llave no tiene ningún modelo de voz en vivo."
            case .seColgo:       return "La voz tardó demasiado en abrir. Vuelve a intentarlo."
            case .deGoogle:      return "No pude abrir la voz en vivo."
            }
        }
    }
}
