import AppKit

/// Ü para Mac. De momento: la carita, flotando, para poder verla y tocarla.
final class Delegado: NSObject, NSApplicationDelegate, NSMenuDelegate {

    var panel: FacePanel!
    var voz: Voz!
    var oido: Oido!
    var cerebro: Cerebro?
    /// La conversación en vivo. `nil` mientras no haya llave.
    /// LA VOZ EN VIVO, POR OPENAI. Reemplaza a `Vivo` (Gemini) como camino principal el 2026-08-31 —
    /// `Vivo.swift` se queda en el repo, probado y sin borrar, para el día que haga falta volver o
    /// comparar, pero ya no es lo que arranca `armarVozViva()`.
    var vivo: VivoOpenAI?
    /// El arnés de prueba aislado sigue existiendo por separado — `U_VIVO_OPENAI=1` construye el
    /// suyo propio, sin tocar esta propiedad.
    var vivoOpenAI: VivoOpenAI?
    /// Mueve la boca con el sonido REAL mientras habla la voz en vivo. Por texto la mueve `Voz`
    /// leyendo la palabra que toca; aquí no hay palabras que leer, hay una onda — y seguirla es más
    /// fiel que adivinar la vocal.
    var bocaViva: Timer?
    var itemMicrofono: NSMenuItem?
    var interruptor: Interruptor?
    var itemVoz: NSMenuItem?

    /// Está en conversación. Dormida, solo reacciona a su nombre.
    var despierta = false
    /// Cuándo fue la última vez que le hablaste. Con esto decide cuándo volver a dormirse.
    var ultimoRoce = Date.distantPast
    /// El turno que acaba de hablar venía del modelo, no de una frase de trámite («todavía no tengo
    /// llave para pensar»). Solo esos se celebran: una carita que brinca por decirte que no puede
    /// hacer algo se está riendo de ti.
    var turnoDelModelo = false

    /// Cuánto aguanta despierta sin que le digas nada.
    ///
    /// Medio minuto: lo bastante para pensar la siguiente frase, pedirle otra cosa o corregirla, y
    /// lo bastante poco para que no se quede escuchando la reunión entera porque la llamaste una vez.
    let cuantoAguantaDespierta: TimeInterval = 30

    /// EL TOPE DURO DE LA SESIÓN EN VIVO, y es una decisión de dinero, no de ingeniería.
    ///
    /// Mientras la sesión está abierta, **todo lo que suena en la sala se manda a Google y se paga**.
    /// El portero del nombre solo existe en el oído local: dentro de la sesión viva no hay filtro
    /// ninguno, así que un descuido de veinte minutos es una factura de veinte minutos.
    ///
    /// Los 30 s de silencio ya la cierran, pero solo cubren el caso de que NADIE hable. Una
    /// conversación ajena cerca, una reunión, la tele: eso la mantendría abierta indefinidamente.
    /// Este tope acota el peor caso a un número que se puede mirar y decidir.
    ///
    /// Volver a llamarla por su nombre la reabre al instante, así que el coste de equivocarse por
    /// abajo es decir «mira» otra vez. El de equivocarse por arriba es una factura.
    let topeDeSesionViva: TimeInterval = {
        if let t = ProcessInfo.processInfo.environment["U_TOPE_VIVO"], let n = Double(t), n > 0 {
            return n
        }
        return 180
    }()

    /// Cuándo se abrió la sesión viva que está corriendo ahora.
    var vivaDesde: Date?

    // ── La carita como semáforo ──────────────────────────────────────────────────────────────────
    //
    // El estado NO se asigna a mano desde cada sitio: se DERIVA de los flags, en una sola función.
    // Es la regla que ya regía en Windows (`ResolveMood`, FaceWindow.xaml.cs) y que aquí se había
    // perdido: había OCHO sitios escribiendo `mood = .algo` por su cuenta —el oído, la voz, la vista
    // y cinco puntos de la conversación— y por eso salían caras donde no tocaba. Dos partes del
    // código afirmando cosas distintas sobre lo mismo siempre acaba en que gana la última que corrió,
    // que no es la que sabe.
    //
    // Efecto colateral que se cobra solo: `escuchando` y `detenido` estaban dibujados, con su pose y
    // su color, y no los ponía NADIE. Código muerto que aquí es peor que en otro sitio, porque lo que
    // muere es la única forma que tiene Ü de contarte en qué estado está.

    /// Está pensando la respuesta: se le preguntó al modelo y todavía no llegó la primera frase.
    var pensando = false
    /// La última vez salió mal. Se limpia al empezar el turno siguiente.
    var fallando = false
    /// Ya pidió perdón; ahora busca la solución sola.
    var disculpaHecha = false
    /// Está en mitad del brinco de celebración.
    var celebrando = false

    /// El orden de las ramas ES la prioridad: primero lo que pasa AHORA, después lo que acaba de
    /// pasar.
    ///
    /// `detenido` va por encima de `conversando` a propósito. Si te callé el micrófono en mitad de
    /// una conversación, `despierta` sigue puesto — pero conversar ya no puede pasar, y enseñar la
    /// cara de conversar mientras está sorda es la peor mentira que puede decir esta carita: te deja
    /// hablándole a algo que no te oye.
    func resolverCara() -> FaceMood { resolverCaraConMotivo().0 }

    /// La misma derivación, pero diciendo QUÉ RAMA ganó.
    ///
    /// No es adorno de diagnóstico: nueve ramas ordenadas por prioridad son nueve
    /// condiciones que pueden estar cableadas y no dispararse nunca —le pasó ya a
    /// `escuchando` y a `detenido`, dibujados y sin que los pusiera nadie—. Y desde
    /// fuera, una rama muerta y una rama que gana se ven exactamente igual: una cara.
    /// Con `U_CARAS_LOG=1` el registro dice cuál ganó, y entonces se puede CONTAR.
    func resolverCaraConMotivo() -> (FaceMood, String) {
        if celebrando            { return (.logrado, "1 celebrando") }
        if fallando              { return disculpaHecha ? (.trabajando, "2 fallando+disculpaHecha") : (.fallo, "2 fallando") }
        if vivo?.hablando == true { return (.hablando, "3 vivo.hablando") }
        if voz.hablando          { return (.hablando, "3 voz.hablando") }
        if pensando              { return (.pensando, "4 pensando") }
        if !oido.continuo        { return (.detenido, "5 !oido.continuo") }
        // En vivo no hay «pensando»: no hay ida y vuelta que esperar, contesta mientras te oye.
        if vivo?.viva == true    { return (.conversando, "6 vivo.viva") }
        // `escuchando` NO es «el micrófono está abierto» —lo está siempre, y eso sería una cara fija;
        // está razonado en `Oido.arrancar()`—. Es «te estoy oyendo decir algo AHORA», acotado a tu
        // frase, que es la misma semántica que tiene en Windows y para la que se dibujó. Y solo si ya
        // está despierta: dormida, lo que entra es la tele y la llamada que tienes con otro.
        // ESCUCHANDO SE DISPARA SOLA, y la condición es literal del catálogo «Las caras de Ü»:
        // «en cuanto el reconocedor te oye — solo si está despierta o si la llamaste por su nombre».
        // Es la misma condición que ya gobierna el ladeo (`atender()`), y tiene que serlo: son la
        // misma cara contada dos veces, el estado y el gesto.
        //
        // NO es «el micrófono está abierto». Eso, dice el catálogo, es `reposo`: «ahí, sin nadie
        // hablándole». Se probó lo contrario el 2026-08-18 y se deshizo el mismo día — y ya había
        // aviso: en Windows, el 2026-08-05, esta cara puesta durante una conversación entera se leyó
        // como «ojos como platos que jadea sin parar delante de alguien que trabaja».
        if despierta && oido.hayVoz          { return (.escuchando, "7 oido.hayVoz") }
        if despierta && vivo?.teOye == true  { return (.escuchando, "7 vivo.teOye") }
        if despierta             { return (.conversando, "8 despierta") }
        return (.reposo, "9 —")
    }

    /// Una cara elegida A MANO desde el menú, que manda sobre la derivación hasta que se suelte.
    ///
    /// Hace falta justo PORQUE ahora se deriva: sin esto, elegir «Grabando» en el menú la pintaría
    /// un instante y el siguiente `refrescarCara()` —que llega con la primera palabra que se oiga—
    /// la borraría. Mirar una cara para afinarla es el trabajo de todos los días aquí; no puede
    /// depender de que nadie hable cerca.
    var caraForzada: FaceMood? {
        didSet { refrescarCara() }
    }

    /// Recalcula y aplica. Se puede llamar todas las veces que haga falta: el setter de la vista
    /// ignora el valor repetido, así que derivar de más no cuesta un repintado.
    func refrescarCara() {
        let (derivada, motivo) = resolverCaraConMotivo()
        let cara = caraForzada ?? derivada
        // Solo cuando CAMBIA: a 16 cuadros por segundo, un registro por refresco es un registro que
        // nadie lee. Y se anota el motivo, no solo la cara: dos ramas distintas dan `conversando`.
        if Delegado.registrarCaras, cara != ultimaCaraAnotada {
            ultimaCaraAnotada = cara
            Registro.di("🎭 cara «\(cara.rawValue)» ← rama \(caraForzada != nil ? "MENU (forzada)" : motivo)")
        }
        panel.face.mood = cara
    }

    /// `U_CARAS_LOG=1` — el registro dice qué rama ganó cada vez que la cara cambia.
    static let registrarCaras = ProcessInfo.processInfo.environment["U_CARAS_LOG"] == "1"
    private var ultimaCaraAnotada: FaceMood?

    func despertar() {
        guard !despierta else { return }
        despierta = true
        ultimoRoce = Date()
        Registro.di("👂 ✦ me despertaron")
        // EL PORTERO SE QUEDA, y esta es la decisión de diseño que sostiene todo lo demás. El oído
        // local reconoce el nombre sin que salga un byte de este Mac y sin costar un céntimo; la
        // sesión viva solo se abre cuando de verdad te está atendiendo. Al revés —el caño abierto
        // todo el día— sería mandarle a Google la reunión entera y pagarla.
        //
        // EL MICRÓFONO NO SE CEDE AQUÍ. Cederlo al ARRANCAR la sesión es apostar a que va a abrir:
        // si no abre —y el 2026-08-19 no abrió tres veces de tres, colgada y sin un solo renglón de
        // error—, el oído local se queda callado para siempre y Ü se vuelve sorda justo después de
        // que la llames. Se cede en `alCambiar(true)`, que es el único sitio que sabe que la sesión
        // existe de verdad. Es el mismo vicio que el contador de reintentos: abrir no es conversar.
        if let vivo, !vivo.viva { vivo.arrancar() }
        refrescarCara()
    }

    /// AL SALIR SE DEJA EL MICRÓFONO COMO SE ENCONTRÓ.
    ///
    /// La cancelación de eco es un ajuste del APARATO, no del proceso: dejarla puesta al morir deja
    /// sordo al siguiente arranque —y a cualquier otra app que grabe—. `AudioVivo.cerrar()` lo
    /// devuelve, pero solo corre si a Ü la cierran por las buenas; con un `pkill` no corre nada.
    /// Esto cubre el cierre ordenado, que es el caso que sí podemos cubrir; el resto lo recoge el
    /// ciclado de `Oido.arrancar()`.
    func applicationWillTerminate(_ note: Notification) {
        vivo?.terminar()
        oido.parar()
        Registro.di("👋 me cierro y devuelvo el micrófono como lo encontré")
    }

    func dormirse() {
        guard despierta else { return }
        despierta = false
        Registro.di("👂 ✧ me duermo (nadie me habla hace \(Int(cuantoAguantaDespierta))s)")
        vivo?.terminar()
        oido.recuperar()
        refrescarCara()
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
            // Si la cortas a media frase, NO se celebra: cortar es lo contrario de haber terminado,
            // y el brinco de «lo logré» encima de un «cállate» se lee como burla.
            if voz.hablando { turnoDelModelo = false; voz.callar(); refrescarCara(); return }
            // Aquí se reaccionaba con `.pillado` al apagar y `.complice` al encender, y estaba mal:
            // esos son TALANTES DEL ESPEJO, con su condición escrita —«lo cacharon», «los dos sabemos
            // de qué va esto»—, y un micrófono apagado no es ninguna de las dos. Gastados como acuse
            // de recibo de un botón, además, duran medio segundo: un segundo después la cara volvía a
            // estar igual y no sabías en qué había quedado. Lo que hace falta no es un aplauso, es un
            // ESTADO que se quede puesto, y ese es `detenido`.
            alternarOido()
        }

        // El micrófono se cierra mientras ella habla y se vuelve a abrir cuando termina.
        //
        // CERRARLO NO ES OPCIONAL: con el micrófono abierto mientras suena su propia voz, se oye a sí
        // misma, se transcribe, y contesta a lo que acaba de decir — y otra vez, y otra. Un bucle que
        // no para y que además cuesta dinero en cada vuelta.
        // Al acabar el brinco, la cara vuelve a lo que toque. Lo decide aquí y no la vista porque
        // depende de si hay alguien conversando con ella, que es cosa de la conversación.
        panel.face.alAcabarDeCelebrar = { [weak self] in
            guard let self else { return }
            celebrando = false
            refrescarCara()
        }

        panel.face.alAcabarDeDisculparse = { [weak self] in
            guard let self else { return }
            disculpaHecha = true
            refrescarCara()
        }

        // EMPIEZA A HABLAR → EL OÍDO SE CALLA. Iba solo la cara, y el oído se quedaba abierto: Ü se
        // transcribía a sí misma y le preguntaba al modelo por lo que ella acababa de decir. El
        // relato entero está en `Oido.mudaPorqueHabla`.
        voz.alEmpezar = { [weak self] in
            self?.oido.mientrasHabla(true)
            self?.refrescarCara()
        }
        // Abrió, cerró, o empezó a oírte: hay algo nuevo que mirar. El oído sigue sin tocar la cara.
        oido.alCambiar = { [weak self] in self?.refrescarCara() }

        voz.alTerminar = { [weak self] in
            guard let self else { return }
            self.ultimoRoce = Date()   // acabar de hablar cuenta como roce: no se duerme recién dicha
            self.pensando = false
            // TERMINÓ SU PROCESO → celebra. Y hay que decir qué significa hoy «su proceso»:
            // significa que le preguntaste, fue al modelo, y acabó de contestarte. No hay nada más
            // que completar en este cliente todavía.
            //
            // Cuando Ü empiece a OPERAR APPS en el Mac, el sitio de esta llamada es el final de la
            // tarea, no el final de la frase — y entonces `turnoDelModelo` sobra. Se deja aquí, y
            // anotado, para que se mueva a sabiendas y no por descubrimiento.
            //
            // La condición que había aquí era `mood == .hablando`, o sea: preguntarle a la CARA si
            // se había hablado. Con el estado derivado eso ya no se puede —la cara es la consecuencia,
            // no la fuente—, y tampoco se debía: `turnoDelModelo` es el que sabe, y es el único que
            // distingue una respuesta del modelo de una frase de trámite.
            // LO QUE FALLA NO SE CELEBRA, y hace falta decirlo AQUÍ y no solo en el `catch`.
            //
            // El 2026-08-18 la carita bailó después de decir «no pude abrir la voz en vivo». La
            // celebración no miraba si lo que se acababa de decir era una respuesta o una excusa:
            // solo miraba que hubiera un turno abierto. Y un turno abierto lo hay también cuando lo
            // que suena es el mensaje de un fallo — la voz es la misma boca.
            //
            // El baile dice «terminé lo que me pediste». Encima de un «no pude», dice lo contrario
            // de lo que pasó, y eso es peor que no reaccionar: enseña a no creerle a la cara.
            if turnoDelModelo, !fallando {
                turnoDelModelo = false
                celebrando = true
            }
            turnoDelModelo = false
            refrescarCara()
            // Un respiro antes de reabrir: el altavoz tarda un instante en callarse de verdad, y la
            // cola de ese instante entra como si fuera una frase tuya.
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.45) { [weak self] in
                self?.oido.mientrasHabla(false)
            }
        }

        // LA VOZ EN VIVO ES OPENAI, EL TEXTO DE RESPALDO SIGUE EN GEMINI — a propósito, y son dos
        // llaves distintas. `Cerebro` (el camino de texto, sin voz nativa) no se tocó: sigue
        // llamando a Gemini. Lo que cambió es la conversación EN VIVO, que era la que se caía a
        // diario con «Socket is not connected» cuando la cuenta de Gemini se quedó sin saldo
        // (2026-08-31) — Felipe ya había resuelto exactamente esto en Windows con el mismo cambio.
        if let k = Llave.gemini {
            cerebro = Cerebro(llave: k)
        } else {
            Registro.di("🧠 sin llave de Gemini: no hay camino de texto de respaldo (falta ~/.u/gemini-key.txt)")
        }
        if let k = Llave.openai {
            vivo = armarVozViva(llave: k)
        } else {
            Registro.di("🎙 sin llave de OpenAI: no hay voz en vivo (falta ~/.u/openai-key.txt)")
        }

        // SE LADEA MIENTRAS LE HABLAS. La señal no hubo que inventarla: `alOir` llega con cada
        // resultado parcial, o sea cada vez que el reconocedor entiende una palabra más de lo que
        // estás diciendo. Estaba declarada y nadie la escuchaba.
        //
        // Y NO CON CUALQUIER `alOir`, que es lo que la salva de ser un tic: dormida, casi nada de
        // lo que se oye va para ella —la llamada que tienes con otro, la tele, el ventilador— y una
        // carita que se ladea con cada frase ajena no está atenta, está fingiendo. Se ladea si ya
        // está despierta, o si lo que va oyendo EMPIEZA POR SU NOMBRE.
        oido.alOir = { [weak self] texto in
            guard let self else { return }
            guard self.despierta || Llamado.resto(de: texto) != nil else { return }
            panel.face.atender()
        }

        oido.alEntender = { [weak self] texto in
            guard let self else { return }
            // Terminaste de hablar: la cabeza vuelve a su sitio, por el camino que sea. Va aquí
            // arriba y no en cada rama porque hay cinco salidas, y la que se olvide deja la carita
            // ladeada para siempre.
            //
            // El instante que tarda no es un descuido: `alEntender` llega 0,55 s después de que te
            // calles, así que aguanta la atención un momento más — que es lo que hace una persona.
            panel.face.dejarDeAtender()

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

                // SI LA VOZ EN VIVO SE HIZO CARGO, EL CAMINO DE TEXTO NO CORRE. Sin esto contestaban
                // las dos a la vez: el 2026-08-18 el log mostró «le cedo el micrófono a la voz en
                // vivo» y en el mismo milisegundo «🧠 le pregunto: me escuchas». Dos cerebros sobre
                // la misma frase, y el de texto además dejaba el turno abierto — que es de donde
                // salió el baile encima del mensaje de fallo.
                //
                // Lo que venía detrás del nombre no se pierde: se le pasa a la sesión por escrito.
                //
                // LA CONDICIÓN PREGUNTA POR LA SESIÓN, Y SOLO POR ELLA. Decía
                // `vivo.viva || cerebro != nil`, y ese segundo término es cierto SIEMPRE que hay
                // llave: la rama ganaba con la sesión cerrada, `decirle` tiraba la frase al suelo
                // por su `guard viva`, y el `return` se llevaba por delante el camino de texto —que
                // funciona—. Desde fuera: la llamas por su nombre y no contesta nunca. Medido el
                // 2026-08-19, con el puente HTTP verificado sano en la misma sesión.
                //
                // Es el aprendizaje nº16 entero: los dos lados de la comparación contestan preguntas
                // distintas. «¿Se hizo cargo la voz viva?» solo la contesta `viva`.
                if let vivo, vivo.viva {
                    if !resto.isEmpty { vivo.decirle(resto) }
                    else { panel.face.reaccionar(.complice) }
                    return
                }

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
                // ESTANDO EN CONVERSACIÓN, no entender no puede ser NO HACER NADA. Desde fuera,
                // cero reacción es indistinguible de no haberte oído —y lo que hace uno entonces es
                // repetir más alto, que es justo lo que rompe la conversación. Pone la cara de
                // pregunta y así sabes que llegaste, pero no llegó el qué.
                //
                // Solo despierta: dormida esto se dispara con la tele y con el ventilador, y una
                // carita que pregunta «¿eh?» al aire no está atenta, está estorbando.
                if self.despierta { panel.face.reaccionar(.perdido) }
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
            self.pensando = true
            self.fallando = false          // turno nuevo: lo de antes ya no es lo que está pasando
            self.disculpaHecha = false
            refrescarCara()
            // AQUÍ SE PROBÓ A METER EL GUIÑO DE «entendido» y se quitó (2026-08-17): encima de la
            // cara de pensar las dos se estorban —el guiño se come la lengua, y lo que sale no es
            // ninguna de las dos—. `entendido` se queda como cara propia, que es donde funciona: la
            // elige el modelo cuando de verdad arranca una tarea.

            Registro.di("🧠 le pregunto: «\(pregunta)»")
            self.turnoDelModelo = true
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
                    self.turnoDelModelo = false       // lo que falla no se celebra
                    self.pensando = false
                    self.fallando = true              // pide perdón y se pone a buscar, sola
                    self.refrescarCara()
                    self.voz.decir(error.localizedDescription)
                }
            }
        }

        oido.alFallar = { [weak self] motivo in
            guard let self else { return }
            fallando = true
            disculpaHecha = false
            refrescarCara()
            voz.decir(motivo)
        }

        // Arranca oyendo, pero dormida: espera a que la llamen por su nombre.
        oido.ponerseAOir()

        let vigia = Timer(timeInterval: 2, repeats: true) { [weak self] _ in
            guard let self, self.despierta else { return }
            if Date().timeIntervalSince(self.ultimoRoce) > self.cuantoAguantaDespierta,
               !self.voz.hablando {
                self.dormirse()
                return
            }
            // EL TOPE MANDA POR ENCIMA DE LA CONVERSACIÓN, incluso si de verdad le estás hablando.
            // Es a propósito: el caso que arruina no es el que conversa, es el que se olvidó.
            if let desde = self.vivaDesde, self.vivo?.viva == true,
               Date().timeIntervalSince(desde) > self.topeDeSesionViva, !self.voz.hablando {
                Registro.di("🎙 ⏹ tope de sesión en vivo (\(Int(self.topeDeSesionViva))s) — cuelgo. Dime «mira» y vuelvo.")
                self.dormirse()
            }
        }
        RunLoop.main.add(vigia, forMode: .common)

        // Mantener oprimido: cambia claro ↔ oscuro.
        panel.alMantener = { [weak self] in
            guard let self else { return }
            panel.face.theme = panel.face.theme == .light ? .dark : .light
        }

        let menu = construirMenu()
        menu.delegate = self
        panel.face.menu = menu

        // EL INTERRUPTOR EN LA BARRA DE MENÚS. El doble toque sobre la carita sigue funcionando, pero
        // deja de ser la única forma: un gesto que hay que saber no es un botón, y la carita se puede
        // arrastrar fuera de vista con el interruptor dentro.
        interruptor = Interruptor(cables: .init(
            encendida:  { [weak self] in self?.oido.continuo ?? false },
            alternar:   { [weak self] in self?.alternarOido() },
            vivaAhora:  { [weak self] in self?.vivo?.viva ?? false },
            colgarLaVoz: { [weak self] in
                guard let self, let vivo, vivo.viva else { return }
                Registro.di("🎙 ⏹ me colgaste desde la barra de menús")
                self.dormirse()
            },
            mostrarLaCarita: { [weak self] in
                self?.panel.center()
                self?.panel.orderFrontRegardless()
            }))

        // SUMAR, Y SALE. El prototipo acotado de la mitad que ACTÚA. Aislado igual que Mirar.
        if Operador.pedido {
            oido.callarse()
            Task {
                await Operador.correr()
                NSApplication.shared.terminate(nil)
            }
            return
        }

        // MIRAR, Y SALE. El prototipo acotado de computer-use: mira la pantalla, dice qué ve, y
        // termina. No arranca el oído ni la voz — está aislado a propósito, ver Mirar.swift.
        if Mirar.pedido {
            oido.callarse()
            if let k = Llave.gemini {
                Task {
                    await Mirar.correr(llave: k)
                    NSApplication.shared.terminate(nil)
                }
            } else {
                Registro.di("👁 ✘ sin llave en ~/.u/gemini-key.txt, no puedo preguntarle al modelo")
                NSApplication.shared.terminate(nil)
            }
            return
        }

        // U_VIVO_OPENAI=1 — el prototipo acotado de la voz por OpenAI Realtime, AISLADO del resto:
        // no toca `vivo` (Gemini), no pasa por `despertar()`, no entra al bucle normal. Solo prueba
        // que el protocolo nuevo abre, oye, contesta y pide su cara, de punta a punta.
        //
        //   open --env U_VIVO_OPENAI=1 --env "U_DECIR=hola, cuéntame algo" "…/U.app"
        if ProcessInfo.processInfo.environment["U_VIVO_OPENAI"] == "1" {
            oido.callarse()
            guard let k = Llave.openai else {
                Registro.di("🎙 ✘ sin llave en ~/.u/openai-key.txt, no puedo abrir OpenAI Realtime")
                return
            }
            Registro.di("🎙 U_VIVO_OPENAI=1 · abro la conversación en vivo con OpenAI")
            let v = VivoOpenAI(llave: k)
            v.alTranscribir = { texto, esDeU in
                Registro.di(esDeU ? "🎙 dice: «\(texto)»" : "🎙 oye: «\(texto)»")
            }
            v.alTalante = { [weak self] t in self?.panel.face.reaccionar(t) }
            v.alFallar = { motivo in Registro.di("🎙 ✘ falló: \(motivo)") }
            vivoOpenAI = v
            v.arrancar()
            if let frase = ProcessInfo.processInfo.environment["U_DECIR"], !frase.isEmpty {
                DispatchQueue.main.asyncAfter(deadline: .now() + 4) { v.decirle(frase) }
            }
            return
        }

        // La sonda del micrófono, y SALE. Va la primera de todas: mide el aparato, y para eso el
        // oído, la voz y la sesión en vivo tienen que no haber tocado nada.
        if Sonda.pedida {
            oido.callarse()
            Thread.detachNewThread { Sonda.correr() }
            return
        }

        // Retratarse y salir. Va ANTES de todo lo demás: no tiene sentido abrir el micrófono ni la
        // conversación para hacer fotos, y el oído encendido metería ladeos en los retratos.
        if Retratos.pedidos {
            oido.callarse()
            Retratos.tomarTodos(panel.face, delegado: self) {
                NSApplication.shared.terminate(nil)
            }
            return
        }

        // Abrir la conversación en vivo NADA MÁS ARRANCAR, sin tener que llamarla por su nombre:
        //   U_VIVO=1 ./U.app/Contents/MacOS/U
        // Está para poder PROBAR el audio —que es donde se rompe— sin depender de que el
        // reconocedor acierte el nombre. Sin esto, comprobar un arreglo del micrófono exige que
        // funcione antes el oído, y entonces un fallo no dice cuál de los dos falló.
        if ProcessInfo.processInfo.environment["U_VIVO"] == "1" {
            Registro.di("🎙 U_VIVO=1 · abro la conversación en vivo al arrancar")
            despertar()
            // Y con U_DECIR se le manda una frase por escrito en cuanto la sesión está abierta, para
            // poder comprobar la vuelta ENTERA —que contesta, que suena, y que pide su cara— sin
            // tener que hablarle. Sin esto, «abrió el socket» y «habla» se confunden, y son cosas
            // muy distintas cuando lo que falla es el altavoz.
            if let frase = ProcessInfo.processInfo.environment["U_DECIR"], !frase.isEmpty {
                DispatchQueue.main.asyncAfter(deadline: .now() + 4) { [weak self] in
                    self?.vivo?.decirle(frase)
                }
            }
        }

        // U_PREGUNTAR — una frase POR EL CAMINO DE TEXTO, sin micrófono y sin llamarla por su
        // nombre. Es el gemelo de `U_DECIR`, que solo llega a la voz en vivo, y hacía falta: el
        // 2026-08-19, con el puente HTTP sano y verificado a mano, «le hablo y no me contesta» no
        // se pudo reproducir en un día porque probar el cerebro de texto exigía que acertaran antes
        // el micrófono Y el reconocedor Y el nombre. Cuatro piezas para juzgar una.
        if let frase = ProcessInfo.processInfo.environment["U_PREGUNTAR"], !frase.isEmpty {
            DispatchQueue.main.asyncAfter(deadline: .now() + 1) { [weak self] in
                Registro.di("🧪 U_PREGUNTAR: entro por el camino de texto con «\(frase)»")
                self?.despierta = true
                self?.oido.alEntender?(frase)
            }
        }

        // Para afinar una cara sin tener que buscarla en el menú cada vez que se recompila:
        //   U_CARA=fallo ./U.app/Contents/MacOS/U
        if let n = ProcessInfo.processInfo.environment["U_CARA"], let m = FaceMood(rawValue: n) {
            caraForzada = m
        }
        // Y lo mismo para las reacciones:  U_TALANTE=enojado U_PESO=0.6
        if let n = ProcessInfo.processInfo.environment["U_TALANTE"], let t = Talante(rawValue: n) {
            panel.face.reaccionar(t)
        }
    }

    private func armarVozViva(llave: String) -> VivoOpenAI {
        let v = VivoOpenAI(llave: llave)

        v.alCambiar = { [weak self] abierta in
            guard let self else { return }
            DispatchQueue.main.async {
                // Y AQUÍ se cede el micrófono, no antes: éste es el único punto del programa que
                // sabe que la sesión viva existe. Mientras no abra, el oído local sigue trabajando
                // y el camino de texto puede contestar — que es lo que hace que Ü nunca se quede
                // muda por un fallo de la voz en vivo.
                if abierta {
                    self.vivaDesde = Date()
                    self.oido.ceder()
                } else {
                    // EL GASTO, DICHO. Un caño abierto que no deja rastro de cuánto estuvo abierto es
                    // un caño que nadie audita — y este cuesta dinero por segundo.
                    if let desde = self.vivaDesde {
                        Registro.di("🎙 💸 la sesión en vivo estuvo abierta \(Int(Date().timeIntervalSince(desde)))s")
                    }
                    self.vivaDesde = nil
                    self.oido.recuperar()
                }
                self.refrescarCara()
                if abierta { self.arrancarBocaViva() } else { self.pararBocaViva() }
            }
        }

        v.alTalante = { [weak self] t in self?.panel.face.reaccionar(t) }

        v.alTranscribir = { [weak self] texto, esDeU in
            guard let self else { return }
            Registro.di(esDeU ? "🎙 dice: «\(texto)»" : "🎙 oye: «\(texto)»")
            // SE LADEA MIENTRAS LE HABLAS, también en vivo. El catálogo lo pide «en cuanto el
            // reconocedor te oye», y en una sesión viva quien oye es Gemini, no el reconocedor local.
            if !esDeU {
                DispatchQueue.main.async {
                    self.panel.face.atender()
                    self.refrescarCara()
                }
            } else {
                DispatchQueue.main.async { self.panel.face.dejarDeAtender() }
            }
            // ROCE ES QUE **TÚ** HABLES. Lo que dice ELLA no cuenta, y ahí estaba el agujero del
            // portero: contaba las dos, así que Ü se mantenía despierta a sí misma. Y como dentro de
            // la sesión viva contesta a cualquier voz de la sala —no hay filtro de nombre ahí—, el
            // ciclo se cerraba solo: la tele hablaba, Ü contestaba, su respuesta renovaba los 30 s, y
            // el caño seguía abierto mandándole a Google la habitación entera. Con factura.
            //
            // Se vio literal en el registro: Ü preguntando «¿me están hablando a mí?» a una
            // conversación ajena, y la sesión sin cerrarse nunca.
            //
            // Sin esto, `cuantoAguantaDespierta` no medía el silencio de la sala: medía si Ü se
            // estaba callada.
            guard !esDeU else { return }
            DispatchQueue.main.async { self.ultimoRoce = Date() }
        }

        v.alFallar = { [weak self] motivo in
            guard let self else { return }
            DispatchQueue.main.async {
                self.fallando = true
                self.disculpaHecha = false
                self.turnoDelModelo = false   // lo que falla no se celebra
                self.oido.recuperar()      // que un fallo de la voz viva no la deje sorda
                self.refrescarCara()
                self.voz.decir(motivo)
            }
        }
        return v
    }

    /// A ~16 cuadros por segundo, el mismo ritmo que la boca de `Voz`: la boca al hablar cambia de
    /// FORMA, y una forma nueva hay que dibujarla entera.
    private func arrancarBocaViva() {
        guard bocaViva == nil else { return }
        let t = Timer(timeInterval: 1.0 / 16.0, repeats: true) { [weak self] _ in
            guard let self, let vivo else { return }
            guard vivo.hablando else {
                panel.face.mouthOpen = 0
                panel.face.mouthRound = 0
                return
            }
            // El nivel crudo se queda corto para la vista: una boca que solo se abre un 20 % no se
            // lee como hablar. Se estira y se acota, con un mínimo que la deja siempre en movimiento
            // mientras suene.
            let n = min(1.0, vivo.nivelVoz * 1.8)
            panel.face.mouthOpen = max(0.12, CGFloat(n) * 0.85)
            panel.face.mouthRound = 0.30
        }
        RunLoop.main.add(t, forMode: .common)
        bocaViva = t
    }

    private func pararBocaViva() {
        bocaViva?.invalidate(); bocaViva = nil
        panel.face.mouthOpen = 0
        panel.face.mouthRound = 0
    }

    @objc private func alternarVozViva() {
        guard let vivo else { return }
        // Ceder y recuperar los hace `alCambiar`, que es quien sabe si la sesión abrió de verdad.
        if vivo.viva { vivo.terminar() } else { vivo.arrancar() }
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
        // Primero la salida. Una cara clavada a mano y sin forma visible de soltarla es una carita
        // que se queda mintiendo hasta el siguiente reinicio.
        let auto = NSMenuItem(title: "   ↩︎ Automático (soltar)", action: #selector(soltarCara), keyEquivalent: "")
        auto.target = self
        m.addItem(auto)
        for mood in FaceMood.allCases {
            let it = NSMenuItem(title: "   " + mood.rawValue.capitalized,
                                action: #selector(elegirEstado(_:)), keyEquivalent: "")
            it.target = self
            it.representedObject = mood
            m.addItem(it)
        }

        m.addItem(.separator())
        // ARRIBA DEL TODO de los gestos, y con el estado escrito en el propio título. El doble toque
        // ya hacía esto, pero un gesto que nadie te enseña es un gesto que no existe: en 6.800 líneas
        // de registro no hay UN SOLO doble toque. Y el título dice en qué estado está porque un item
        // que solo dice «micrófono» no te saca de la duda que te trajo al menú.
        itemMicrofono = NSMenuItem(title: "", action: #selector(alternarMicrofono), keyEquivalent: "")
        itemMicrofono?.target = self
        m.addItem(itemMicrofono!)

        itemVoz = NSMenuItem(title: "", action: #selector(alternarVozViva), keyEquivalent: "")
        itemVoz?.target = self
        m.addItem(itemVoz!)

        m.addItem(.separator())
        for (titulo, sel) in [("Te escucho (ladeo) ⇄", #selector(alternarAtencion)),
                              ("Guiñar", #selector(guinar)),
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
        caraForzada = mood
    }

    @objc private func soltarCara() { caraForzada = nil }

    @objc private func reaccionar(_ sender: NSMenuItem) {
        guard let t = sender.representedObject as? Talante else { return }
        panel.face.reaccionar(t)
    }

    /// El ladeo se enciende y se apaga con el mismo item: dura lo que dure la frase de quien habla,
    /// así que no hay una duración que enseñar — hay que poder dejarla puesta y mirarla.
    @objc private func alternarAtencion() {
        if panel.face.atendiendo { panel.face.dejarDeAtender() } else { panel.face.atender() }
    }

    /// El título se escribe al abrirse el menú, no al construirlo: se construye una vez y el estado
    /// cambia todo el rato.
    func menuNeedsUpdate(_ menu: NSMenu) {
        itemMicrofono?.title = oido.continuo ? "🎙 Te está oyendo — callarla" : "🔇 Está callada — que oiga"
        if let vivo {
            itemVoz?.isHidden = false
            itemVoz?.title = vivo.viva ? "⚡︎ Voz en vivo — colgar" : "⚡︎ Hablar en vivo con Gemini"
        } else {
            itemVoz?.isHidden = true
        }
    }

    @objc private func alternarMicrofono() { alternarOido() }

    /// UN SOLO SITIO QUE ENCIENDE Y APAGA. Hay tres formas de hacerlo —el doble toque, el menú de la
    /// carita y el botón de la barra— y si cada una lo hiciera por su cuenta, el icono de la barra
    /// diría una cosa mientras el oído hace otra. Un interruptor que miente sobre su estado es peor
    /// que no tener interruptor: te hace hablarle a algo que no te oye, o creerte apagada cuando no.
    func alternarOido() {
        if oido.continuo {
            oido.callarse()
            // Apagarla es apagarla ENTERA: si estaba conversando en vivo, ese caño también se cierra.
            // Dejarlo abierto con el micrófono «apagado» sería seguir mandando y pagando con el
            // interruptor en «off», que es exactamente lo que nadie espera de un botón de apagado.
            if despierta { dormirse() }
        } else {
            oido.ponerseAOir()
        }
        interruptor?.pintar()
        refrescarCara()
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
