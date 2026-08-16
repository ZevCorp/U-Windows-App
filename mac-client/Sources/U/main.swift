import AppKit

/// Ü para Mac. De momento: la carita, flotando, para poder verla y tocarla.
final class Delegado: NSObject, NSApplicationDelegate {

    var panel: FacePanel!
    var voz: Voz!
    var oido: Oido!
    var cerebro: Cerebro?

    /// Está en conversación. Dormida, solo reacciona a su nombre.
    var despierta = false
    /// Cuándo fue la última vez que le hablaste. Con esto decide cuándo volver a dormirse.
    var ultimoRoce = Date.distantPast
    /// Cuánto aguanta despierta sin que le digas nada.
    ///
    /// Medio minuto: lo bastante para pensar la siguiente frase, pedirle otra cosa o corregirla, y
    /// lo bastante poco para que no se quede escuchando la reunión entera porque la llamaste una vez.
    let cuantoAguantaDespierta: TimeInterval = 30

    func despertar() {
        guard !despierta else { return }
        despierta = true
        ultimoRoce = Date()
        Registro.di("👂 ✦ me despertaron")
        panel.face.mood = .conversando
    }

    func dormirse() {
        guard despierta else { return }
        despierta = false
        Registro.di("👂 ✧ me duermo (nadie me habla hace \(Int(cuantoAguantaDespierta))s)")
        panel.face.mood = .reposo
        panel.face.parpadear(2)
    }

    func applicationDidFinishLaunching(_ note: Notification) {
        panel = FacePanel()
        panel.orderFrontRegardless()

        voz = Voz(cara: panel.face)
        oido = Oido(cara: panel.face)

        // Un toque: un pulsito de vida.
        panel.alTocar = { [weak self] in self?.panel.face.pulso() }

        // Doble toque: la callas o la vuelves a poner a oír. Ya no hace falta tocarla para hablarle
        // —está oyendo siempre—, así que este gesto pasó a ser el interruptor.
        panel.alTocarDoble = { [weak self] in
            guard let self else { return }
            Registro.di("👆 doble toque")
            if voz.hablando { voz.callar(); return }
            if oido.continuo {
                oido.callarse()
                panel.face.reaccionar(.pillado)
            } else {
                oido.ponerseAOir()
                panel.face.reaccionar(.complice)
            }
        }

        // El micrófono se cierra mientras ella habla y se vuelve a abrir cuando termina.
        //
        // CERRARLO NO ES OPCIONAL: con el micrófono abierto mientras suena su propia voz, se oye a sí
        // misma, se transcribe, y contesta a lo que acaba de decir — y otra vez, y otra. Un bucle que
        // no para y que además cuesta dinero en cada vuelta.
        voz.alTerminar = { [weak self] in
            guard let self else { return }
            self.ultimoRoce = Date()   // acabar de hablar cuenta como roce: no se duerme recién dicha
            if panel.face.mood == .hablando { panel.face.mood = despierta ? .conversando : .reposo }
            // Un respiro antes de reabrir: el altavoz tarda un instante en callarse de verdad, y la
            // cola de ese instante entra como si fuera una frase tuya.
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.45) { [weak self] in
                self?.oido.reanudar()
            }
        }

        if let k = Llave.gemini { cerebro = Cerebro(llave: k) }

        oido.alEntender = { [weak self] texto in
            guard let self else { return }

            // ¿Me están hablando A MÍ?
            //
            // Con el micrófono abierto todo el día, contestar a todo lo que se oye es insoportable:
            // entra la llamada que tienes con otro, la tele, y el «mmm» que el reconocedor saca del
            // ruido del ventilador. Así que duerme, y solo despierta cuando la llaman por su nombre.
            var loQuePregunta = texto
            if !self.despierta {
                guard let resto = Llamado.resto(de: texto) else {
                    Registro.di("👂 no era para mí · oí «\(texto)» · lo comparo como «\(Llamado.comoLoVeo(texto))»")
                    oido.reanudar()
                    return
                }
                self.despertar()
                // «Ü, abre el correo» de un tirón: si detrás del nombre venía la petición, se atiende
                // ya. Obligar a llamarla, esperar, y recién ahí pedir, es una conversación que nadie
                // tiene con nadie.
                guard !resto.isEmpty else {
                    Registro.di("👂 me llamaste · espero qué quieres")
                    panel.face.reaccionar(.complice)
                    oido.reanudar()
                    return
                }
                loQuePregunta = resto
            }

            self.ultimoRoce = Date()
            guard Self.valeLaPenaContestar(loQuePregunta) else {
                Registro.di("👂 ignoro «\(loQuePregunta)» (ruido)")
                oido.reanudar()
                return
            }
            let pregunta = loQuePregunta

            guard let cerebro else {
                voz.decir("Todavía no tengo llave para pensar.")
                return
            }

            // Mientras piensa, la cara lo dice. Un silencio sin cara es lo que hace dudar de si te
            // oyó — y volver a hablarle encima es lo que rompe la conversación.
            panel.face.mood = .trabajando

            Registro.di("🧠 le pregunto: «\(pregunta)»")
            let arranque = Date()
            Task { @MainActor [weak self] in
                guard let self else { return }
                do {
                    try await cerebro.responderEnVivo(
                        a: pregunta,
                        alTalante: { [weak self] t in
                            guard let self else { return }
                            Registro.di("🧠 cara «\(t?.rawValue ?? "ninguno")» a los \(String(format: "%.2f", -arranque.timeIntervalSinceNow))s")
                            if let t { self.panel.face.reaccionar(t) }
                        },
                        alFrase: { [weak self] frase in
                            guard let self else { return }
                            Registro.di("🧠 dice a los \(String(format: "%.2f", -arranque.timeIntervalSinceNow))s: «\(frase)»")
                            self.voz.decir(frase)
                        })
                } catch {
                    // El detalle técnico va al registro; en voz alta va una frase que se pueda oír.
                    // Pegarle el error crudo detrás de «perdón» era leerle a la cara una URL de
                    // facturación en inglés.
                    Registro.di("🧠 ✘ \(error)")
                    self.panel.face.mood = .fallo     // pide perdón y se pone a buscar, sola
                    self.voz.decir(error.localizedDescription)
                }
            }
        }

        oido.alFallar = { [weak self] motivo in
            guard let self else { return }
            panel.face.mood = .fallo
            voz.decir(motivo)
        }

        // Arranca oyendo, pero dormida: espera a que la llamen por su nombre.
        oido.ponerseAOir()

        let vigia = Timer(timeInterval: 2, repeats: true) { [weak self] _ in
            guard let self, self.despierta else { return }
            if Date().timeIntervalSince(self.ultimoRoce) > self.cuantoAguantaDespierta,
               !self.voz.hablando {
                self.dormirse()
            }
        }
        RunLoop.main.add(vigia, forMode: .common)

        // Mantener oprimido: cambia claro ↔ oscuro.
        panel.alMantener = { [weak self] in
            guard let self else { return }
            panel.face.theme = panel.face.theme == .light ? .dark : .light
        }

        panel.face.menu = construirMenu()

        // Para afinar una cara sin tener que buscarla en el menú cada vez que se recompila:
        //   U_CARA=fallo ./U.app/Contents/MacOS/U
        if let n = ProcessInfo.processInfo.environment["U_CARA"], let m = FaceMood(rawValue: n) {
            panel.face.mood = m
        }
    }

    private func construirMenu() -> NSMenu {
        let m = NSMenu()

        // Primero las reacciones: es lo que se está afinando ahora, y lo que se prueba veinte veces
        // seguidas tiene que estar arriba del menú.
        m.addItem(withTitle: "Cómo se lo toma", action: nil, keyEquivalent: "")
        for t in Talante.allCases {
            let it = NSMenuItem(title: "   " + t.titulo, action: #selector(reaccionar(_:)), keyEquivalent: "")
            it.target = self
            it.representedObject = t
            m.addItem(it)
        }

        m.addItem(.separator())
        m.addItem(withTitle: "Qué está haciendo", action: nil, keyEquivalent: "")
        for mood in FaceMood.allCases {
            let it = NSMenuItem(title: "   " + mood.rawValue.capitalized,
                                action: #selector(elegirEstado(_:)), keyEquivalent: "")
            it.target = self
            it.representedObject = mood
            m.addItem(it)
        }

        m.addItem(.separator())
        for (titulo, sel) in [("Guiñar", #selector(guinar)),
                              ("Parpadear dos veces", #selector(parpadear2)),
                              ("Pulso", #selector(pulso)),
                              ("Mirar a un lado", #selector(mirar)),
                              ("Claro / oscuro", #selector(tema))] {
            let it = NSMenuItem(title: titulo, action: sel, keyEquivalent: "")
            it.target = self
            m.addItem(it)
        }

        m.addItem(.separator())
        let salir = NSMenuItem(title: "Salir", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        m.addItem(salir)
        return m
    }

    /// Lo que el reconocedor entrega cuando en realidad no dijiste nada: una sílaba, un ruido, o una
    /// de esas muletillas que salen solas. Dos palabras de verdad ya son una frase; una sola tiene
    /// que ser larga para no ser un carraspeo.
    private static func valeLaPenaContestar(_ texto: String) -> Bool {
        let t = texto.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        let ruido: Set<String> = ["ah", "eh", "mm", "mmm", "hm", "hmm", "uh", "uy", "ay", "oh",
                                  "ya", "sí", "si", "no", "ok", "the", "you", "a", "e", "y"]
        if ruido.contains(t) { return false }
        let palabras = t.split(separator: " ").count
        return palabras >= 2 || t.count >= 5
    }

    @objc private func elegirEstado(_ sender: NSMenuItem) {
        guard let mood = sender.representedObject as? FaceMood else { return }
        panel.face.mood = mood
    }

    @objc private func reaccionar(_ sender: NSMenuItem) {
        guard let t = sender.representedObject as? Talante else { return }
        panel.face.reaccionar(t)
    }

    @objc private func guinar()    { panel.face.guinar(izquierdo: false) }
    @objc private func parpadear2() { panel.face.parpadear(2) }
    @objc private func pulso()      { panel.face.pulso() }
    @objc private func mirar()      { panel.face.mirarHacia(izquierda: true) }
    @objc private func tema()       { panel.face.theme = panel.face.theme == .light ? .dark : .light }
}

let app = NSApplication.shared
let delegado = Delegado()
app.delegate = delegado
// `.accessory`: sin icono en el Dock y sin barra de menús propia. Ü no es una app que se «abre»,
// es algo que está ahí.
app.setActivationPolicy(.accessory)
app.run()
