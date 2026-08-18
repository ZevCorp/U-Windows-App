import AppKit

/// La ventana donde vive la carita: flota sobre todo, no roba el foco, no sale en el Dock ni en
/// Mission Control, y se puede agarrar y tirar a un lado.
final class FacePanel: NSPanel {

    /// La ventana la dimensiona la VISTA, no al revés: quien sabe cuánto aire necesita la carita
    /// para poder ladearse sin que la corten es quien la dibuja.
    let face = FaceView(frame: NSRect(x: 0, y: 0,
                                      width: FaceView.Medidas.lado, height: FaceView.Medidas.lado))

    // Estado del gesto en curso.
    private var arrastrando = false
    /// Si el gesto en curso empezó SOBRE la carita. Sin esto, un clic en el aire de alrededor no
    /// arrastraría pero sí contaría como toque al soltar.
    private var agarroValido = false
    private var agarre = CGPoint.zero          // dónde se agarró, dentro de la ventana
    private var bajoDesde: TimeInterval = 0
    private var ultimoToque: TimeInterval = 0
    private var origenAlBajar = CGPoint.zero
    private var largoPendiente: DispatchWorkItem?

    /// Para tirarla al lado con impulso: se mide la velocidad de los últimos instantes del arrastre.
    private var ultimoPunto = CGPoint.zero
    private var ultimoInstante: TimeInterval = 0
    private var velocidad = CGPoint.zero

    var alTocar: (() -> Void)?
    var alTocarDoble: (() -> Void)?
    var alMantener: (() -> Void)?

    init() {
        super.init(contentRect: NSRect(x: 0, y: 0,
                                       width: FaceView.Medidas.lado, height: FaceView.Medidas.lado),
                   styleMask: [.borderless, .nonactivatingPanel],
                   backing: .buffered, defer: false)

        isFloatingPanel = true
        level = .statusBar                  // por encima de casi todo, incluso de apps a pantalla completa
        backgroundColor = .clear
        isOpaque = false
        hasShadow = false
        ignoresMouseEvents = false
        hidesOnDeactivate = false
        isMovableByWindowBackground = false // el arrastre se maneja a mano, para medir la velocidad
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]

        contentView = face
        colocarAlInicio()
    }

    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }

    private func colocarAlInicio() {
        guard let z = NSScreen.main?.visibleFrame else { return }
        // Los 24 y los 120 se miden de LA CARITA al borde, no de la ventana. Sin descontar el aire
        // invisible, la carita se habría metido ocho puntos adentro sin que nadie lo pidiera — y el
        // sitio donde aparece es de las pocas cosas que el usuario nota sin saber por qué.
        let aire = FaceView.Medidas.margen
        setFrameOrigin(CGPoint(x: z.maxX - FaceView.Medidas.lado - 24 + aire,
                               y: z.minY + 120 - aire))
    }

    // ── Gestos ───────────────────────────────────────────────────────────────────────────────────
    //
    // Los mismos umbrales que la burbuja de Android y que el cliente Windows: 13 px separan un toque
    // de un arrastre, 250 ms separan un toque de un doble toque, y 750 ms quieto es «mantener».

    private enum Umbral {
        static let arrastre: CGFloat = 13
        static let dobleToque: TimeInterval = 0.250
        static let mantener: TimeInterval = 0.750
    }

    /// Un punto de la pantalla, en coordenadas de la vista — que lleva la Y hacia abajo.
    private func enVista(_ p: CGPoint) -> CGPoint {
        CGPoint(x: p.x - frame.minX, y: frame.height - (p.y - frame.minY))
    }

    override func mouseDown(with event: NSEvent) {
        let enPantalla = NSEvent.mouseLocation

        // EL AIRE DE ALREDEDOR NO AGARRA. La ventana es mayor que la carita para que las esquinas
        // quepan al ladearse; si ese margen agarrara, quedaría un anillo invisible de ocho puntos
        // que se lleva la carita cuando ibas a la ventana de detrás.
        //
        // Y dicho con todas las letras, porque la diferencia importa: esto impide AGARRARLA, no
        // hace que el clic llegue a lo que hay debajo. Que pase de largo es otro mecanismo y no
        // está puesto — antes tampoco lo estaba, pero antes el anillo no existía.
        agarroValido = face.dentroDeLaCarita(enVista(enPantalla))
        guard agarroValido else { return }

        // Si la agarras a mitad de brinco, manda tu mano. Sin esto, la celebración le seguiría
        // poniendo la ventana donde toca por su cuenta y el arrastre iría a tirones.
        face.cancelarCelebracion(devolviendo: false)

        agarre = CGPoint(x: enPantalla.x - frame.minX, y: enPantalla.y - frame.minY)
        origenAlBajar = enPantalla
        bajoDesde = ProcessInfo.processInfo.systemUptime
        arrastrando = false
        velocidad = .zero
        ultimoPunto = enPantalla
        ultimoInstante = bajoDesde

        // Mantener oprimido SIN moverse. Si se mueve, el arrastre lo cancela.
        let tarea = DispatchWorkItem { [weak self] in
            guard let self, !self.arrastrando else { return }
            self.largoPendiente = nil
            self.alMantener?()
        }
        largoPendiente = tarea
        DispatchQueue.main.asyncAfter(deadline: .now() + Umbral.mantener, execute: tarea)
    }

    override func mouseDragged(with event: NSEvent) {
        guard agarroValido else { return }
        let p = NSEvent.mouseLocation
        if !arrastrando {
            let d = hypot(p.x - origenAlBajar.x, p.y - origenAlBajar.y)
            guard d > Umbral.arrastre else { return }
            arrastrando = true
            largoPendiente?.cancel(); largoPendiente = nil
        }

        // Velocidad suavizada: 65 % de lo que traía + 35 % de lo instantáneo. Sin suavizar, un
        // temblor del dedo justo al soltar decide el lanzamiento.
        let ahora = ProcessInfo.processInfo.systemUptime
        let dt = max(1.0 / 240.0, ahora - ultimoInstante)
        let inst = CGPoint(x: (p.x - ultimoPunto.x) / CGFloat(dt), y: (p.y - ultimoPunto.y) / CGFloat(dt))
        velocidad = CGPoint(x: velocidad.x * 0.65 + inst.x * 0.35, y: velocidad.y * 0.65 + inst.y * 0.35)
        ultimoPunto = p; ultimoInstante = ahora

        setFrameOrigin(CGPoint(x: p.x - agarre.x, y: p.y - agarre.y))
    }

    override func mouseUp(with event: NSEvent) {
        guard agarroValido else { return }
        largoPendiente?.cancel(); largoPendiente = nil
        let ahora = ProcessInfo.processInfo.systemUptime

        if arrastrando {
            arrastrando = false
            Vuelo.aUnLado(self, velocidad: velocidad)
            return
        }

        // Toque simple o doble, según lo que tarde el siguiente.
        if ahora - ultimoToque < Umbral.dobleToque {
            ultimoToque = 0
            alTocarDoble?()
        } else {
            ultimoToque = ahora
            let marca = ahora
            DispatchQueue.main.asyncAfter(deadline: .now() + Umbral.dobleToque) { [weak self] in
                guard let self, self.ultimoToque == marca else { return }   // llegó el segundo toque
                self.ultimoToque = 0
                self.alTocar?()
            }
        }
    }
}
