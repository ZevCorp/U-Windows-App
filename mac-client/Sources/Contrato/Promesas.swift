import Foundation

// LAS PROMESAS DE Ü EN MAC.
//
// Cada una es una frase que hoy es verdadera y que tiene que seguir siéndolo mañana. No son «tests
// de Oido.swift»: son lo que el sistema promete a quien le habla. Por eso están escritas en la voz
// de la usuaria y no en la del código — «contesta en menos de tres segundos», no «responderEnVivo
// devuelve».
//
// LOS NÚMEROS NO SE RECICLAN. Una promesa retirada deja su hueco, porque los commits la citan por
// número.
//
// Una promesa ROJA durante el desarrollo es lo correcto y es un entregable. Una promesa roja
// llegando a `main` no entra.

enum Promesas {

    static var todas: [Promesa] { [uno, dos, tres, cuatro, cinco, seis, siete, ocho, nueve, diez] }

    // ── Las que no necesitan micrófono ───────────────────────────────────────────────────────────

    /// 1 · Arranca y se pone a oír.
    static let uno = Promesa(numero: 1, enunciado: "Al abrirla, Ü queda oyendo") {
        App.cerrar()
        try App.limpiarRegistro()
        try App.abrir([:])
        guard esperarA(segundos: 180, "que arme el oído", { registroTiene("escuchando (formato") }) else {
            if registroTiene("DENEGADO") {
                return .noPudeCorrer("falta el permiso del micrófono; concédelo y repite")
            }
            return .rota("60 s y nunca imprimió «escuchando». Registro:\n\(ultimasLineas(6))")
        }
        return .cumplida(primeraLinea(con: "escuchando (formato")?.trimmingCharacters(in: .whitespaces) ?? "")
    }

    /// 2 · Contesta rápido. En una conversación hablada, el silencio es lo único que se siente.
    static let dos = Promesa(numero: 2, enunciado: "Contesta en menos de 3 segundos") {
        App.cerrar()
        try App.limpiarRegistro()
        try App.abrir(["U_PREGUNTAR": "mira, cuéntame algo corto"])
        guard esperarA(segundos: 90, "que conteste", { registroTiene("🧠 dice a los") }) else {
            if registroTiene("🧠 ✘") {
                return .rota("el cerebro falló: \(primeraLinea(con: "🧠 ✘") ?? "")")
            }
            return .noPudeCorrer("no contestó ni falló en 90 s — ¿hay llave en ~/.u/gemini-key.txt?")
        }
        guard let l = primeraLinea(con: "🧠 dice a los"),
              let seg = Double(l.split(separator: " a los ").last?.split(separator: "s").first ?? "") else {
            return .noPudeCorrer("contestó pero no pude leer el tiempo de «\(primeraLinea(con: "🧠 dice a los") ?? "")»")
        }
        return seg < 3.0 ? .cumplida("primera frase a los \(seg) s")
                         : .rota("tardó \(seg) s en la primera frase (el tope son 3)")
    }

    /// 3 · No promete lo que no puede. Hoy Ü no toca el Mac, y decir que sí deja a alguien esperando.
    static let tres = Promesa(numero: 3, enunciado: "No promete tocar el Mac, porque no puede") {
        App.cerrar()
        try App.limpiarRegistro()
        try App.abrir(["U_PREGUNTAR": "mira, ábreme el correo y contéstale a mi jefe"])
        guard esperarA(segundos: 90, "que conteste", { registroTiene("🧠 dice a los") }) else {
            return .noPudeCorrer("no contestó en 90 s")
        }
        let dicho = lineasDelRegistro(con: "🧠 dice a los").joined(separator: " ").lowercased()
        let niega = ["no puedo", "todavía no", "no sé hacer", "no consigo"].contains { dicho.contains($0) }
        let miente = ["ya lo abrí", "listo,", "abriendo", "te lo abro", "déjame revisar", "voy a buscar"]
            .contains { dicho.contains($0) }
        if miente { return .rota("dio a entender que lo hizo: «\(dicho.prefix(160))»") }
        return niega ? .cumplida("lo dijo sin rodeos: «\(dicho.prefix(90))»")
                     : .rota("ni lo negó ni lo hizo; contestó «\(dicho.prefix(160))»")
    }

    /// 4 · Habla en español y con la voz que se le pidió.
    static let cuatro = Promesa(numero: 4, enunciado: "Habla con voz masculina en español") {
        guard registroTiene("🔊 voz") else {
            return .noPudeCorrer("la promesa 3 no llegó a hacerla hablar; sin voz no hay qué juzgar")
        }
        guard let l = primeraLinea(con: "🔊 voz") else { return .noPudeCorrer("sin línea de voz") }
        guard l.contains("(es-") else { return .rota("no eligió una voz en español: \(l)") }
        let femeninas = ["Paulina", "Mónica", "Monica"]
        if let mala = femeninas.first(where: { l.contains($0) }) {
            return .rota("eligió \(mala), que es femenina — se pidió masculina el 2026-08-19")
        }
        return .cumplida(l.trimmingCharacters(in: .whitespaces))
    }

    /// 5 · La cara se DERIVA en un solo sitio. Ocho sitios escribiéndola es la última que corre
    ///     ganándole a la que sabe.
    static let cinco = Promesa(numero: 5, enunciado: "La cara se deriva sola de lo que está pasando") {
        App.cerrar()
        try App.limpiarRegistro()
        try App.abrir(["U_CARAS_LOG": "1", "U_PREGUNTAR": "mira, cuéntame algo corto"])
        guard esperarA(segundos: 90, "que hable", { registroTiene("🎭 cara «hablando»") }) else {
            return .rota("nunca puso cara de hablando. Caras vistas:\n\(lineasDelRegistro(con: "🎭").joined(separator: "\n"))")
        }
        let ramas = lineasDelRegistro(con: "🎭 cara").compactMap { l -> String? in
            l.split(separator: "← rama ").last.map { String($0).trimmingCharacters(in: .whitespaces) }
        }
        let esperadas = ["4 pensando", "3 voz.hablando"]
        let faltan = esperadas.filter { e in !ramas.contains(where: { $0.hasPrefix(e) }) }
        guard faltan.isEmpty else { return .rota("no se activaron las ramas \(faltan); salieron \(ramas)") }
        return .cumplida("ramas activadas: \(Set(ramas).sorted().joined(separator: " · "))")
    }

    /// 6 · Mientras habla, no se oye a sí misma. Sin esto entra en bucle: se transcribe, se pregunta
    ///     por lo que acaba de decir, y se contesta.
    static let seis = Promesa(numero: 6, enunciado: "No se transcribe a sí misma mientras habla") {
        guard registroTiene("🧠 dice a los") else {
            return .noPudeCorrer("la promesa 5 no llegó a hacerla hablar")
        }
        guard let inicio = primeraLinea(con: "🧠 dice a los").flatMap(instante) else {
            return .noPudeCorrer("no pude fechar cuándo empezó a hablar")
        }
        // Lo que el oído transcribió DESPUÉS de que ella empezara a hablar, dentro de su frase.
        let intrusos = lineasDelRegistro(con: "👂 oigo:").filter {
            guard let t = instante($0) else { return false }
            return t >= inicio && t < inicio + 8
        }
        guard intrusos.isEmpty else {
            return .rota("se oyó a sí misma \(intrusos.count) vez/veces:\n\(intrusos.prefix(3).joined(separator: "\n"))")
        }
        return .cumplida(registroTiene("no arranco: estoy hablando") || registroTiene("no abro: estoy hablando")
            ? "el oído se calló mientras hablaba, y lo dijo"
            : "cero transcripciones propias durante su frase")
    }

    /// 7 · Las 52 caras se pueden mirar juntas. Es la herramienta con la que se decide cuál cambiar.
    static let siete = Promesa(numero: 7, enunciado: "Se retrata sola: 52 caras en ~/.u/caras") {
        App.cerrar()
        let caras = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent(".u/caras")
        try? FileManager.default.removeItem(at: caras)
        try App.limpiarRegistro()
        try App.abrir(["U_RETRATOS": "1", "U_PESO": "1"])
        guard esperarA(segundos: 180, "que termine de retratarse", { registroTiene("📸 listo:") }) else {
            return .rota("no terminó los retratos en 3 minutos. \(ultimasLineas(4))")
        }
        let n = (try? FileManager.default.contentsOfDirectory(atPath: caras.path))?
            .filter { $0.hasSuffix(".png") }.count ?? 0
        return n == 52 ? .cumplida("\(n) retratos")
                       : .rota("salieron \(n) retratos, no 52")
    }

    // ── Las que necesitan el micrófono, o sea el aire de esta máquina ────────────────────────────

    /// 8 · Te oye por el micrófono. Es la promesa más básica y la que más veces se rompió sola.
    static let ocho = Promesa(numero: 8, enunciado: "Te oye cuando le hablas") {
        App.cerrar()
        try App.limpiarRegistro()
        try App.abrir([:])
        guard esperarA(segundos: 180, "que arme el oído", { registroTiene("escuchando (formato") }) else {
            return .noPudeCorrer("no llegó a abrir el oído; la promesa 1 lo explica")
        }
        try App.decirleEnVozAlta("prueba del contrato numero uno")
        guard esperarA(segundos: 25, "que transcriba", { registroTiene("👂 oigo:") }) else {
            return .rota("le hablé por el altavoz y no transcribió NADA en 25 s, con «escuchando» impreso")
        }
        return .cumplida(primeraLinea(con: "👂 oigo:")?.trimmingCharacters(in: .whitespaces) ?? "")
    }

    /// 9 · Despierta cuando la llaman por su nombre, y solo entonces.
    static let nueve = Promesa(numero: 9, enunciado: "Despierta cuando le dices «Mira», y no antes") {
        guard registroTiene("👂 oigo:") else {
            return .noPudeCorrer("la promesa 8 no consiguió que oyera; sin oído no hay nombre que juzgar")
        }
        // SE ESPERA EL RECHAZO, NO SE COMPRUEBA AL INSTANTE. El veredicto llega ~0,6 s después de la
        // transcripción, y mirarlo antes daba un ROJO FALSO: «no lo rechazó» cuando sí iba a hacerlo.
        // Un juez que corre más rápido que lo que juzga inventa culpables.
        guard esperarA(segundos: 15, "que rechace lo que no es su nombre", { registroTiene("no era para mí") }) else {
            return .rota("oyó algo que no era su nombre y NO lo rechazó — contestaría a la sala entera")
        }
        // La llamo con una frase LARGA y con su nombre delante, no con «Mira» a secas: una palabra
        // suelta por el altavoz se la come el ruido de la sala, y entonces el fallo es del arnés.
        var desperto = false
        for _ in 0..<3 where !desperto {
            try App.decirleEnVozAlta("Mira, prueba del contrato, me estás oyendo")
            desperto = esperarA(segundos: 15, "que despierte", { registroTiene("✦ me despertaron") })
        }
        guard desperto else {
            // ¿LLEGÓ SIQUIERA MI VOZ? Si el reconocedor nunca transcribió nada parecido a la llamada,
            // lo que falló es el aire de esta habitación, no la promesa. Decir «rota» aquí sería
            // culpar al código por el ruido de la sala, y ese veredicto manda la investigación al
            // sitio equivocado. Pasó de verdad el 2026-08-19: el micrófono pescaba una conversación
            // ajena y no el altavoz de al lado.
            let oido = lineasDelRegistro(con: "👂 oigo:").joined(separator: " ").lowercased()
            if !oido.contains("mira") && !oido.contains("prueba del contrato") {
                return .noPudeCorrer("mi voz no llegó al micrófono en tres intentos: el reconocedor solo pescó ruido de la sala. Repite con la habitación en silencio. Esto NO juzga el código.")
            }
            return .rota("transcribió su nombre y aun así NO despertó: \(oido.prefix(140))")
        }
        return .cumplida("rechazó lo ajeno y despertó con su nombre")
    }

    /// 10 · ROJA A PROPÓSITO, y este es el fallo que la usuaria reporta como «le hablo y no me
    ///      responde». Está descrito entero en PENDIENTES.md con sus tres hipótesis descartadas.
    ///
    ///      Se queda escrita y roja porque una promesa que no existe no puede contarse como «no
    ///      aplicable»: eso sumaría al verde y el contrato pasaría a certificar el vacío.
    static let diez = Promesa(numero: 10, enunciado: "Sigue oyéndote después de una conversación en vivo") {
        App.cerrar()
        try App.limpiarRegistro()
        try App.abrir([:])
        guard esperarA(segundos: 180, "que arme el oído", { registroTiene("escuchando (formato") }) else {
            return .noPudeCorrer("no llegó a abrir el oído")
        }
        // Se la llama hasta TRES veces: el reconocedor no siempre pesca una palabra suelta dicha por
        // el altavoz, y que no lo pesque no es que la promesa falle — es que el arnés no llegó.
        var desperto = false
        for _ in 0..<3 where !desperto {
            try App.decirleEnVozAlta("Mira, prueba del contrato, me estás oyendo")
            desperto = esperarA(segundos: 15, "que despierte", { registroTiene("✦ me despertaron") })
        }
        guard desperto else {
            return .noPudeCorrer("no conseguí despertarla por el altavoz en tres intentos")
        }
        guard esperarA(segundos: 40, "que abra la sesión en vivo", { registroTiene("sesión de voz abierta") }) else {
            return .noPudeCorrer("la sesión en vivo no abrió; sin ella no se puede juzgar el después")
        }
        guard esperarA(segundos: 90, "que se duerma y suelte el micrófono", { registroTiene("recupero el micrófono") }) else {
            return .noPudeCorrer("no se durmió en 90 s")
        }
        Thread.sleep(forTimeInterval: 6)
        let antes = lineasDelRegistro(con: "👂 oigo:").count
        try App.decirleEnVozAlta("prueba del contrato despues de la sesion en vivo")
        let oyo = esperarA(segundos: 25, "que vuelva a transcribir") {
            lineasDelRegistro(con: "👂 oigo:").count > antes
        }
        if oyo { return .cumplida("volvió a oír después de la sesión en vivo") }
        return .pendiente("""
            el oído queda sordo tras la sesión en vivo. Imprime «escuchando» y no entra un búfer.
            Tres hipótesis ya descartadas (respiro, motor nuevo, apagar la cancelación de eco):
            están en PENDIENTES.md — no volver a probarlas.
            """)
    }

    static func ultimasLineas(_ n: Int) -> String {
        App.registroEntero.split(separator: "\n").suffix(n).joined(separator: "\n")
    }
}
