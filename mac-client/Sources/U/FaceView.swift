import AppKit

/// La carita de Ü, tercera encarnación del mismo dibujo: `FaceView.kt` (Android) → `FaceControl.cs`
/// (WPF) → esta. Cejas curvas + ojos de línea vertical + sonrisa bezier sobre un squircle.
/// Coordenadas en el sistema original del SVG (viewBox −75..75), escaladas al tamaño de la vista.
///
/// Solo dibuja y anima; no decide nada. Quién está en cada momento lo dice `mood`.
///
/// DOS REGLAS DE RENDIMIENTO heredadas del puerto WPF, y que aquí siguen valiendo:
///
///  1. **Los squircles se cachean.** Cada uno son 73 puntos con dos `pow`, y se dibujan dos por
///     cuadro. Solo dependen del tamaño, así que recalcularlos en cada cuadro es tirar trabajo.
///  2. **El pulso y el balanceo van en la TRANSFORMADA, no en la geometría.** Escalar y rotar el
///     contexto no cambia los caminos; recalcular el dibujo entero 60 veces por segundo sí.
final class FaceView: NSView {

    // ── Lo que se puede pedir desde fuera ────────────────────────────────────────────────────────

    var theme: FaceTheme = .light { didSet { needsDisplay = true } }

    var mood: FaceMood = .reposo {
        didSet { guard mood != oldValue else { return }; onMoodChanged(mood) }
    }

    /// 0 = ojo abierto, 1 = cerrado. UNO POR OJO, y no uno para los dos.
    ///
    /// En Android y en Windows era un solo número que cerraba los dos ojos a la vez, porque allí la
    /// carita solo parpadeaba. Aquí tiene que poder GUIÑAR, que es el gesto que dice «los dos sabemos
    /// de qué va esto» — y un guiño con los dos ojos es un parpadeo.
    private(set) var blinkL: CGFloat = 0
    private(set) var blinkR: CGFloat = 0
    /// Desplazamiento horizontal de los ojos, en unidades del viewBox (±3.5 como mucho).
    private(set) var eyeShift: CGFloat = 0

    /// Desplazamiento VERTICAL de los ojos. Positivo = mira hacia abajo.
    ///
    /// No existía ni en Android ni en Windows, porque allí la carita nunca tuvo que avergonzarse. No
    /// poder mirarte a los ojos es la señal de vergüenza que ningún otro rasgo sustituye: puedes
    /// fingir la boca y las cejas, pero apartar la mirada es lo que se hace sin querer.
    private(set) var eyeShiftY: CGFloat = 0

    /// Cuánto está ABIERTA la boca, 0 (la sonrisa de siempre) a 1 (bien abierta).
    var mouthOpen: CGFloat = 0 { didSet { needsDisplay = true } }
    /// La FORMA de la abertura: 0 = ancha y plana («i», «e»), 1 = redonda y estrecha («o», «u»).
    ///
    /// Por el volumen no se puede saber qué vocal se dice —eso exige analizar el sonido, que es otro
    /// problema entero— así que esto no pretende acertar la vocal: pretende que la boca no repita
    /// siempre el mismo gesto, que es lo que delata a un muñeco.
    var mouthRound: CGFloat = 0 { didSet { needsDisplay = true } }

    // ── Estado de las animaciones ────────────────────────────────────────────────────────────────

    private enum Continua { case ninguna, respirar, balancear }
    private var continua: Continua = .ninguna
    private var continuaDesde: CFTimeInterval = 0

    private var parpadeoDesde: CFTimeInterval?
    private var parpadeoVeces = 1
    /// Qué ojos participan: los dos es un parpadeo, uno solo es un guiño.
    private var parpadeoOjos: (izq: Bool, der: Bool) = (true, true)

    private var pulsoDesde: CFTimeInterval?

    private enum Mirada { case vagar(objetivo: CGFloat), fijar(objetivo: CGFloat), soltar(desde: CGFloat) }
    private var mirada: Mirada?
    private var miradaDesde: CFTimeInterval = 0
    /// Mientras está fija, los gestos de reposo no le corren los ojos: no se puede estar mirando algo
    /// y distraerse cada ocho segundos.
    private var mirandoFijo = false

    /// La reacción en curso: la cara que se pinta ENCIMA de lo que estuviera haciendo, y se va sola.
    private var reaccion: Reaccion?
    private var reaccionDesde: CFTimeInterval = 0

    /// Cuándo empezó a pedir perdón. Mientras dura, manda sobre los ojos y sobre los gestos ociosos.
    private var disculpaDesde: CFTimeInterval?
    private var disculpa = Disculpa()

    private var reloj: Timer?
    private var proximoOcioso: CFTimeInterval = 0

    private enum Envolvente {
        static let entra: TimeInterval = 0.130   // rápido: una reacción tardona no reacciona a nada
        static let sale: TimeInterval  = 0.340   // más lenta: soltar la cara de golpe parece un corte
    }

    private let splineMirada = KeySpline(0.3, 0, 0.2, 1)
    private let splinePulso  = KeySpline(0.2, 0.9, 0.3, 1)

    // ── Ciclo de vida ────────────────────────────────────────────────────────────────────────────

    override var isFlipped: Bool { true }   // y hacia abajo, igual que WPF: el puerto es literal

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true
        arrancarReloj()
    }

    required init?(coder: NSCoder) { fatalError("no se usa NIB") }

    deinit { reloj?.invalidate() }

    private func arrancarReloj() {
        // `.common` y no el modo por defecto: si no, el reloj se para mientras se arrastra la ventana
        // o hay un menú abierto, y la carita se congela justo cuando el usuario la está tocando.
        let t = Timer(timeInterval: 1.0 / 60.0, repeats: true) { [weak self] _ in self?.cuadro() }
        RunLoop.main.add(t, forMode: .common)
        reloj = t
        programarProximoOcioso()
    }

    // ── Coreografía del estado ───────────────────────────────────────────────────────────────────

    /// Limpia lo del estado anterior y arranca lo del nuevo.
    private func onMoodChanged(_ mood: FaceMood) {
        parpadeoDesde = nil
        mirada = nil
        mirandoFijo = false
        blinkL = 0; blinkR = 0
        eyeShift = 0
        continua = .ninguna
        pulsoDesde = nil

        switch mood {
        case .escuchando:
            // Respiración: el único estado con movimiento propio permanente, porque «te escucho»
            // tiene que notarse mientras dura el micrófono (8 s como mucho).
            continua = .respirar
            continuaDesde = CACurrentMediaTime()
        case .trabajando:
            // Balanceo mínimo. Es una rotación, no un repintado: cuesta cero por cuadro.
            continua = .balancear
            continuaDesde = CACurrentMediaTime()
        case .fallo:
            // No un pulso y ya: la disculpa entera, que dura casi tres segundos y manda sobre todo
            // lo demás mientras pasa.
            disculpaDesde = CACurrentMediaTime()
        default:
            break
        }
        if mood != .fallo { disculpaDesde = nil }
        needsDisplay = true
    }

    // ── Gestos ───────────────────────────────────────────────────────────────────────────────────

    /// Parpadea `veces` veces (onda triangular: abre→cierra→abre), 340 ms cada una.
    func parpadear(_ veces: Int = 1) {
        guard veces > 0 else { return }
        parpadeoVeces = veces
        parpadeoOjos = (true, true)
        parpadeoDesde = CACurrentMediaTime()
    }

    /// El guiño: un solo ojo, y más lento que un parpadeo.
    ///
    /// La lentitud es el chiste. Un guiño a la velocidad de un parpadeo no se lee como guiño, se lee
    /// como un tic — hay que ver el ojo cerrado el tiempo suficiente para saber que fue a propósito.
    func guinar(izquierdo: Bool) {
        parpadeoVeces = 1
        parpadeoOjos = (izq: izquierdo, der: !izquierdo)
        parpadeoDesde = CACurrentMediaTime()
    }

    /// Pulso de vida: encoge y rebota (señal de acción sin coordenadas, como en Android).
    func pulso() { pulsoDesde = CACurrentMediaTime() }

    /// EL ESPEJO: pone una cara un momento y vuelve sola a lo que estuviera haciendo.
    ///
    /// No cambia el `mood` a propósito. Ü no deja de trabajar para reírse — se ríe MIENTRAS trabaja,
    /// y por eso al acabar la reacción la cara de trabajar sigue ahí, sin que nadie la reponga.
    func reaccionar(_ talante: Talante) {
        let r = Reaccion.de(talante)
        reaccion = r
        reaccionDesde = CACurrentMediaTime()

        switch r.gesto {
        case .ninguno:        break
        case .guinoDerecho:   guinar(izquierdo: false)
        case .guinoIzquierdo: guinar(izquierdo: true)
        case .parpadeoDoble:  parpadear(2)
        case .pulso:          pulso()
        case .ojosALado:      vagar()
        }
        needsDisplay = true
    }

    /// Cuánto pesa la reacción ahora mismo: 0 = la cara de siempre, 1 = la reacción entera.
    private func pesoReaccion(_ ahora: CFTimeInterval) -> CGFloat {
        guard let r = reaccion else { return 0 }
        let t = ahora - reaccionDesde
        let total = Envolvente.entra + r.aguanta + Envolvente.sale
        if t >= total { reaccion = nil; return 0 }
        if t < Envolvente.entra { return CGFloat(t / Envolvente.entra) }
        if t < Envolvente.entra + r.aguanta { return 1 }
        return CGFloat(1 - (t - Envolvente.entra - r.aguanta) / Envolvente.sale)
    }

    /// Mirar hacia un lado y QUEDARSE mirando, hasta que se suelte.
    ///
    /// Es lo que hace creíble que la carita esté señalando algo: ponerse al lado del elemento y
    /// seguir mirando al frente es raro, casi desatento.
    func mirarHacia(izquierda: Bool) {
        mirandoFijo = true
        mirada = .fijar(objetivo: (izquierda ? -1 : 1) * 3.5)
        miradaDesde = CACurrentMediaTime()
    }

    /// Vuelve a mirar al frente y deja que los gestos de reposo sigan su curso.
    func dejarDeMirar() {
        guard mirandoFijo else { return }
        mirandoFijo = false
        mirada = .soltar(desde: eyeShift)
        miradaDesde = CACurrentMediaTime()
    }

    // ── Gestos casuales en reposo ────────────────────────────────────────────────────────────────

    private func programarProximoOcioso() {
        // Tranquila: un gesto cada 8–18 s (antes cambiaba demasiado seguido y se veía ansiosa).
        proximoOcioso = CACurrentMediaTime() + 8.0 + Double.random(in: 0..<10.0)
    }

    private func gestoOcioso() {
        switch Int.random(in: 0..<100) {
        case ..<65:  parpadear(Int.random(in: 0..<5) == 0 ? 2 : 1)   // ~65 %: casi siempre uno solo
        case ..<92:  vagar()                                        // ~27 %: mirar a un lado y volver
        default:     pulso()                                        // ~8 %: pequeño pulso de vida
        }
    }

    private func vagar() {
        guard !mirandoFijo else { return }   // está mirando algo: no se distrae
        let signo: CGFloat = Bool.random() ? -1 : 1
        mirada = .vagar(objetivo: signo * (2.0 + CGFloat.random(in: 0..<1.5)))   // ±2..3.5 unidades
        miradaDesde = CACurrentMediaTime()
    }

    // ── El cuadro ────────────────────────────────────────────────────────────────────────────────

    private func cuadro() {
        let ahora = CACurrentMediaTime()
        var vivo = continua != .ninguna

        // La disculpa manda mientras pasa: nada le corre los ojos ni le pone gestos de relleno. Un
        // parpadeo casual en mitad de una disculpa la deshace entera — la vuelve una cara que
        // simplemente está ahí.
        if let desde = disculpaDesde {
            disculpa = Disculpa.en(ahora - desde)
            eyeShiftY = disculpa.mirada
            if disculpa.mandaElla {
                blinkL = disculpa.cierraOjos
                blinkR = disculpa.cierraOjos
                eyeShift = disculpa.miradaX      // el barrido de cuando está buscando
                parpadeoDesde = nil
                mirada = nil
            }
            // Pidió perdón; ahora se pone a buscar la solución SOLA. Que haya que decírselo sería lo
            // que convierte la disculpa en una espera — y esperar es lo que da rabia.
            if disculpa.terminada {
                disculpaDesde = nil
                eyeShiftY = 0
                if mood == .fallo { mood = .trabajando }
            }
            vivo = true
        } else {
            disculpa = Disculpa()
            eyeShiftY = 0
        }

        let calladita = disculpaDesde != nil && disculpa.mandaElla
        if ahora >= proximoOcioso {
            if !calladita { gestoOcioso() }
            programarProximoOcioso()
        }

        // Parpadeo: keyframes lineales, 340 ms por parpadeo. El guiño va por su cuenta y dura más.
        if let desde = parpadeoDesde {
            let guino = !(parpadeoOjos.izq && parpadeoOjos.der)
            let dur = guino ? 0.620 : 0.340 * Double(parpadeoVeces)
            let t = ahora - desde
            var v: CGFloat = 0
            var acabo = false
            if t >= dur { acabo = true }
            else if guino { v = Self.valorGuino(CGFloat(t)) }
            else { v = Self.valorParpadeo(CGFloat(t / dur), veces: parpadeoVeces) }

            blinkL = parpadeoOjos.izq ? v : 0
            blinkR = parpadeoOjos.der ? v : 0
            if acabo { blinkL = 0; blinkR = 0; parpadeoDesde = nil } else { vivo = true }
        }

        // Mirada.
        if let m = mirada {
            let t = CGFloat(ahora - miradaDesde)
            switch m {
            case .vagar(let objetivo):
                // 0→450 ms llega · 450→1250 ms se queda mirando · 1250→1800 ms vuelve.
                if t < 0.450 { eyeShift = objetivo * splineMirada.eval(t / 0.450) }
                else if t < 1.250 { eyeShift = objetivo }
                else if t < 1.800 { eyeShift = objetivo * (1 - splineMirada.eval((t - 1.250) / 0.550)) }
                else { eyeShift = 0; mirada = nil }
            case .fijar(let objetivo):
                if t < 0.320 { eyeShift = objetivo * splineMirada.eval(t / 0.320) }
                else { eyeShift = objetivo; mirada = nil }
            case .soltar(let desde):
                if t < 0.420 { eyeShift = desde * (1 - splineMirada.eval(t / 0.420)) }
                else { eyeShift = 0; mirada = nil }
            }
            vivo = true
        }

        if pulsoDesde != nil { vivo = true }
        if reaccion != nil { vivo = true }

        if vivo { needsDisplay = true }
    }

    /// El guiño cierra rápido, SE QUEDA cerrado, y abre sin prisa. Ese aguante en medio es lo que lo
    /// separa de un tic: sin él no se lee como algo hecho a propósito.
    private static func valorGuino(_ t: CGFloat) -> CGFloat {
        if t < 0.130 { return t / 0.130 }            // cierra
        if t < 0.400 { return 1 }                    // aguanta
        return max(0, 1 - (t - 0.400) / 0.220)       // abre
    }

    private static func valorParpadeo(_ p: CGFloat, veces: Int) -> CGFloat {
        let n = CGFloat(veces)
        let i = min(n - 1, floor(p * n))
        let local = p * n - i          // 0..1 dentro de este parpadeo
        return local < 0.5 ? local * 2 : (1 - local) * 2
    }

    /// Escala del momento: la disculpa (que la deja encogida), la respiración (1.0→1.05) o el pulso
    /// (1.0→0.84→rebote), lo que esté activo.
    private func escalaActual(_ ahora: CFTimeInterval) -> CGFloat {
        // Encogerse es vergüenza, y gana sobre todo lo demás: nadie respira tranquilo mientras pide
        // perdón.
        if disculpaDesde != nil { return disculpa.tamano }
        if let desde = pulsoDesde {
            let t = CGFloat(ahora - desde)
            let dur: CGFloat = 0.290
            if t >= dur { pulsoDesde = nil }
            else {
                let corte: CGFloat = 0.110
                if t < corte { return 1.0 + (0.84 - 1.0) * (t / corte) }
                return 0.84 + (1.0 - 0.84) * splinePulso.eval((t - corte) / (dur - corte))
            }
        }
        if continua == .respirar {
            // 1200 ms de ida con AutoReverse ⇒ ciclo completo de 2400 ms.
            return 1.0 + 0.05 * Easing.autoReverse(CGFloat(ahora - continuaDesde), period: 2.400)
        }
        return 1
    }

    /// Inclinación del momento: la de la disculpa, MÁS el balanceo de «trabajando» (±3°, ciclo de
    /// 4800 ms), MÁS la que pida la reacción. Se suman en vez de pisarse: reírse mientras trabaja no
    /// la debe dejar quieta.
    private func inclinacionActual(_ ahora: CFTimeInterval, peso: CGFloat) -> CGFloat {
        var g = disculpaDesde != nil ? disculpa.ladeo : 0
        if continua == .balancear {
            g += -3 + 6 * Easing.autoReverse(CGFloat(ahora - continuaDesde), period: 4.800)
        }
        if let r = reaccion { g += r.inclina * peso }
        return g
    }

    // ── El dibujo ────────────────────────────────────────────────────────────────────────────────

    override func draw(_ dirtyRect: NSRect) {
        guard let ctx = NSGraphicsContext.current?.cgContext else { return }
        let w = bounds.width, h = bounds.height
        guard w > 0, h > 0 else { return }

        let ahora = CACurrentMediaTime()
        let s = min(w, h) / 150.0          // unidades del viewBox → px
        let cx = w / 2, cy = h / 2
        func X(_ v: CGFloat) -> CGFloat { cx + v * s }
        func Y(_ v: CGFloat) -> CGFloat { cy + v * s }
        let r = min(w, h) / 2 - s

        // Pulso/respiración y balanceo, alrededor del centro. La geometría no se entera.
        ctx.saveGState()
        let peso = pesoReaccion(ahora)
        let esc = escalaActual(ahora), inc = inclinacionActual(ahora, peso: peso)
        ctx.translateBy(x: cx, y: cy)
        ctx.rotate(by: inc * .pi / 180)
        ctx.scaleBy(x: esc, y: esc)
        ctx.translateBy(x: -cx, y: -cy)
        // El asentimiento mueve la cabeza ENTERA hacia abajo, no solo la mirada: un «entendido» se
        // dice con el cuello, y bajar solo los ojos sería seguir mirando al suelo.
        if disculpa.cabezazo != 0 { ctx.translateBy(x: 0, y: disculpa.cabezazo * s) }

        // --- Paleta según el tema (mismos valores que Palette.kt de Android) ---
        let dark = theme == .dark
        var faceLine: CGColor = dark ? UiPalette.rgb(255, 255, 255) : UiPalette.rgb(0, 0, 0)
        var fillTop: CGColor = dark ? UiPalette.rgb(0x1A, 0x1A, 0x1A) : UiPalette.rgb(255, 255, 255)
        var fillBottom: CGColor = dark ? UiPalette.rgb(0, 0, 0) : UiPalette.rgb(255, 255, 255)
        let faceBorder = CGColor(srgbRed: 0, green: 0, blue: 0, alpha: 0x1F / 255.0)

        // --- El acento del estado ---
        //
        // A 42 px de lado la pluma de los rasgos mide 1,12 px: un anillo o un punto de color
        // sencillamente NO SE VEN. Lo único legible a ese tamaño son los rasgos (que son casi toda la
        // tinta) y el bloque de relleno. Así que el acento tiñe los dos: la carita entera cambia de
        // carácter y se distingue de reojo, que es de lo que se trata.
        // La cara de siempre, mezclada con la reacción que esté en curso.
        var pose = FacePose.pose(mood)
        var bocaAbierta = mouthOpen
        var bocaRedonda = mouthRound
        if let r = reaccion, peso > 0 {
            pose = pose.mezclando(hacia: r.pose, peso)
            bocaAbierta = mouthOpen + (r.boca - mouthOpen) * peso
            bocaRedonda = mouthRound + (r.redondez - mouthRound) * peso
        }
        // La cara de estar buscando el problema, al final de la disculpa. Va DESPUÉS de la reacción
        // porque mientras se hace cargo de un fallo no está para bromas.
        if disculpa.decidida > 0 {
            pose = pose.mezclando(hacia: Disculpa.caraDecidida, disculpa.decidida)
        }

        if let acento = pose.accent {
            faceLine = acento
            // Sobre el relleno casi negro del tema oscuro hace falta más peso para que se aprecie.
            let peso: CGFloat = dark ? 0.20 : 0.14
            fillTop = UiPalette.blend(fillTop, acento, peso)
            fillBottom = UiPalette.blend(fillBottom, acento, peso)
        }

        // Relleno del rostro (degradado diagonal, esquina sup-izq → inf-der del squircle).
        let fuera = squircle(cx: cx, cy: cy, r: r, cache: &cacheFuera, clave: &claveFuera)
        ctx.saveGState()
        ctx.addPath(fuera)
        ctx.clip()
        if let grad = CGGradient(colorsSpace: CGColorSpaceCreateDeviceRGB(),
                                 colors: [fillTop, fillBottom] as CFArray, locations: [0, 1]) {
            ctx.drawLinearGradient(grad,
                                   start: CGPoint(x: cx - r, y: cy - r),
                                   end: CGPoint(x: cx + r, y: cy + r),
                                   options: [.drawsBeforeStartLocation, .drawsAfterEndLocation])
        }
        ctx.restoreGState()

        if dark {
            // Hairline blanca delgada, separada del borde hacia adentro lo mismo que su grosor.
            let hairline = 0.35 * s
            let dentro = squircle(cx: cx, cy: cy, r: r - hairline * 1.5, cache: &cacheDentro, clave: &claveDentro)
            ctx.setStrokeColor(faceLine)
            ctx.setLineWidth(hairline)
            ctx.addPath(dentro)
            ctx.strokePath()
        } else {
            ctx.setStrokeColor(faceBorder)
            ctx.setLineWidth(1.5 * s)
            ctx.addPath(fuera)
            ctx.strokePath()
        }

        // Rasgos: trazo grueso del color de la línea, con el lienzo rotado −2° como en Android.
        ctx.setStrokeColor(faceLine)
        ctx.setFillColor(faceLine)
        ctx.setLineWidth(4 * s)
        ctx.setLineCap(.round)
        ctx.setLineJoin(.round)

        ctx.saveGState()
        ctx.translateBy(x: cx, y: cy)
        ctx.rotate(by: -2 * .pi / 180)
        ctx.translateBy(x: -cx, y: -cy)

        // Cejas: bezier cuadrática sobre cada ojo.
        for (bx, bh, c) in [(CGFloat(-30), pose.browL, pose.curveL), (CGFloat(30), pose.browR, pose.curveR)] {
            let ceja = CGMutablePath()
            ceja.move(to: CGPoint(x: X(bx - 10), y: Y(-34 - bh)))
            ceja.addQuadCurve(to: CGPoint(x: X(bx + 10), y: Y(-34 - bh)),
                              control: CGPoint(x: X(bx), y: Y(-34 - bh - c * 15)))
            ctx.addPath(ceja)
            ctx.strokePath()
        }

        // Ojos: líneas verticales (el cierre los acorta casi del todo; eyeShift los corre a un lado).
        // Cada ojo lleva su propio cierre, que es lo que permite el guiño.
        let eyeBase = 25 * pose.eyeOpen * (1 - pose.squint * 0.4)
        for (ex, cierre) in [(CGFloat(-30), blinkL), (CGFloat(30), blinkR)] {
            let eyeLen = eyeBase * (1 - cierre * 0.92)
            let ey = -14 + eyeShiftY
            ctx.move(to: CGPoint(x: X(ex + eyeShift), y: Y(ey - eyeLen / 2)))
            ctx.addLine(to: CGPoint(x: X(ex + eyeShift), y: Y(ey + eyeLen / 2)))
            ctx.strokePath()
        }

        // Boca: bezier cúbica asimétrica (sonrisa).
        let base = pose.mouthCurve * 15
        let leftY = 34 - base - pose.cornerL * 8
        let rightY = 34 - base - pose.cornerR * 8
        let midY = 34 - pose.mouthCurve * 12
        let shift = (pose.cornerR - pose.cornerL) * 10
        let half = pose.mouthWidth / 2

        // ABIERTA O CERRADA. Cerrada es la sonrisa de siempre —una línea— y así se queda en reposo:
        // esto no puede cambiar la cara que ya existía. Abierta, la MISMA curva pasa a ser el labio
        // de arriba y se le añade otro por debajo, cerrando una figura que se rellena. Un solo dibujo
        // con dos estados, en vez de dos bocas distintas que habría que mantener a la par.
        let abierta = Easing.clamp01(bocaAbierta)
        if abierta <= 0.02 {
            let linea = CGMutablePath()
            linea.move(to: CGPoint(x: X(-half), y: Y(leftY)))
            linea.addCurve(to: CGPoint(x: X(half), y: Y(rightY)),
                           control1: CGPoint(x: X(-half * 0.3 + shift), y: Y(midY)),
                           control2: CGPoint(x: X(half * 0.3 + shift), y: Y(midY)))
            ctx.addPath(linea)
            ctx.strokePath()
        } else {
            // Redonda estrecha la boca; ancha la deja como está. Es lo que separa una «o» de una «e».
            let redonda = Easing.clamp01(bocaRedonda)
            let halfA = half * (1 - redonda * 0.58)
            let alto = 3 + abierta * 20 * (0.75 + redonda * 0.45)

            // La comisura sube un poco al abrir, como una boca de verdad: si las esquinas se quedan
            // clavadas mientras el centro baja, parece una bisagra y no una boca.
            let lY = leftY - abierta * 2, rY = rightY - abierta * 2
            let mY = midY - abierta * 1.5
            let centro = (lY + rY) / 2

            // DE MEDIA LUNA A ÓVALO. Con la sonrisa de siempre arriba, estrechar la boca la cierra en
            // PUNTA y sale un colmillo, no una «o». Así que al redondear no basta con estrechar: el
            // labio de arriba tiene que dejar de sonreír —se levanta hasta curvarse al revés— y los
            // dos tiran hacia fuera, que es lo que convierte la media luna en un óvalo.
            func mezcla(_ plano: CGFloat, _ redondo: CGFloat) -> CGFloat { plano + (redondo - plano) * redonda }
            let ctrlArribaY = mezcla(mY, centro - alto * 0.45)
            let ctrlAbajoY = mezcla(mY + alto, centro + alto * 0.55)
            let anchoArriba = halfA * mezcla(0.30, 0.62)
            let anchoAbajo = halfA * mezcla(0.45, 0.78)

            let boca = CGMutablePath()
            boca.move(to: CGPoint(x: X(-halfA), y: Y(lY)))
            boca.addCurve(to: CGPoint(x: X(halfA), y: Y(rY)),
                          control1: CGPoint(x: X(-anchoArriba + shift), y: Y(ctrlArribaY)),
                          control2: CGPoint(x: X(anchoArriba + shift), y: Y(ctrlArribaY)))
            boca.addCurve(to: CGPoint(x: X(-halfA), y: Y(lY)),
                          control1: CGPoint(x: X(anchoAbajo), y: Y(ctrlAbajoY)),
                          control2: CGPoint(x: X(-anchoAbajo), y: Y(ctrlAbajoY)))
            boca.closeSubpath()
            ctx.addPath(boca)
            ctx.fillPath()

            // La lengua. Solo cuando la boca está lo bastante abierta para que se vea algo dentro:
            // dibujarla siempre la convierte en una mancha pegada al labio.
            if abierta > 0.35 {
                // DÓNDE ACABA LA BOCA NO ES DONDE ESTÁ SU PUNTO DE CONTROL. Una bezier cúbica no
                // llega hasta sus controles: con los dos a la misma altura se queda en tres cuartos
                // del camino. Colocar la lengua contando desde el control la dejaba POR DEBAJO del
                // labio, asomando fuera de la boca. El punto más bajo de la curva sale de evaluarla
                // en la mitad: (P0 + 3·C1 + 3·C2 + P3) / 8.
                let fondo = (lY + rY) / 8 + ctrlAbajoY * 0.75
                let rx = halfA * 0.42, ry = alto * 0.20

                // Y ADEMÁS se recorta contra la boca, que es lo que garantiza que no pueda salirse
                // aunque la cuenta de arriba falle en algún tamaño raro: la geometría manda sobre la
                // aritmética. De paso es como se ve en el dibujo de referencia — la lengua no es un
                // óvalo entero flotando, es un óvalo cortado por el borde del labio.
                ctx.saveGState()
                ctx.addPath(boca)
                ctx.clip()
                ctx.setFillColor(UiPalette.lengua)
                ctx.fillEllipse(in: CGRect(x: X(shift * 0.4) - rx * s,
                                           y: Y(fondo - ry * 0.25) - ry * s,
                                           width: rx * s * 2, height: ry * s * 2))
                ctx.restoreGState()
            }
        }

        ctx.restoreGState()   // rotación −2°
        ctx.restoreGState()   // pulso + balanceo
    }

    // ── Caché de los squircles ───────────────────────────────────────────────────────────────────
    //
    // Solo dependen de (cx, cy, r), que solo cambian si la vista cambia de tamaño — es decir, casi
    // nunca. Sin caché se reconstruían 2 × 73 puntos con dos `pow` cada uno EN CADA REPINTADO, y hay
    // repintados de sobra: cada parpadeo repinta a la velocidad del cuadro varias veces por minuto.

    private var cacheFuera: CGPath?
    private var claveFuera: CGFloat = .nan
    private var cacheDentro: CGPath?
    private var claveDentro: CGFloat = .nan

    private func squircle(cx: CGFloat, cy: CGFloat, r: CGFloat, cache: inout CGPath?, clave: inout CGFloat) -> CGPath {
        if let c = cache, clave == r { return c }
        let p = Self.construirSquircle(cx: cx, cy: cy, r: r)
        cache = p; clave = r
        return p
    }

    /// Squircle (superelipse |x|^n+|y|^n=1, n≈4): el «cuadrado con curva de Euler» de Apple.
    private static func construirSquircle(cx: CGFloat, cy: CGFloat, r: CGFloat) -> CGPath {
        let n: CGFloat = 4.0
        let steps = 72
        let p = CGMutablePath()
        for i in 0...steps {
            let t = 2.0 * CGFloat.pi * CGFloat(i) / CGFloat(steps)
            let ct = cos(t), st = sin(t)
            let px = cx + r * (ct < 0 ? -1 : 1) * pow(abs(ct), 2.0 / n)
            let py = cy + r * (st < 0 ? -1 : 1) * pow(abs(st), 2.0 / n)
            if i == 0 { p.move(to: CGPoint(x: px, y: py)) } else { p.addLine(to: CGPoint(x: px, y: py)) }
        }
        p.closeSubpath()
        return p
    }
}
