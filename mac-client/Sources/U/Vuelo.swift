import AppKit

/// Mover la ventana CON FÍSICA, cuadro a cuadro. El único sitio que la hace viajar.
///
/// Se mueve a mano, un cuadro cada vez, en vez de pedirle a una animación del sistema que lo haga:
/// así la curva se respeta de verdad y el viaje manda sobre cualquier corrección de posición que
/// llegue mientras dura. Es lo mismo que hace la burbuja de Android con su ValueAnimator.
enum Vuelo {

    private static var reloj: Timer?

    /// Se va SIEMPRE a un lado. La velocidad del gesto decide a cuál y con cuánto ímpetu.
    static func aUnLado(_ win: NSWindow, velocidad: CGPoint) {
        guard let zona = (win.screen ?? NSScreen.main)?.visibleFrame else { return }
        let f = win.frame
        let centro = f.midX

        // Si venía lanzada de verdad, manda la dirección del lanzamiento; si no, el lado más cercano.
        let izquierda: Bool = abs(velocidad.x) > 320 ? velocidad.x < 0 : centro < zona.midX

        // 24 puntos de LA CARITA al borde. El aire invisible que la ventana lleva alrededor para
        // que las esquinas quepan al ladearse no cuenta como separación: si contara, la carita
        // acabaría ocho puntos más adentro de lo que se pidió, y al lanzarla se vería despegada.
        let margen: CGFloat = 24 - FaceView.Medidas.margen
        let destinoX = izquierda ? zona.minX + margen : zona.maxX - f.width - margen

        // Vertical: se queda donde está, pero sin salirse de la pantalla.
        let destinoY = min(max(f.minY + velocidad.y * 0.10, zona.minY + margen),
                           zona.maxY - f.height - margen)

        mover(win, a: CGPoint(x: destinoX, y: destinoY), duracion: 0.42)
    }

    /// Lleva la ventana hasta un punto con una curva que frena al final y rebota un pelín.
    static func mover(_ win: NSWindow, a destino: CGPoint, duracion: TimeInterval) {
        reloj?.invalidate()
        let origen = win.frame.origin
        let inicio = CACurrentMediaTime()
        let muelle = KeySpline(0.16, 0.90, 0.26, 1.0)   // sale rápido, llega frenando

        let t = Timer(timeInterval: 1.0 / 120.0, repeats: true) { reloj in
            let p = CGFloat((CACurrentMediaTime() - inicio) / duracion)
            if p >= 1 {
                win.setFrameOrigin(destino)
                reloj.invalidate()
                Vuelo.reloj = nil
                return
            }
            let e = muelle.eval(p)
            win.setFrameOrigin(CGPoint(x: origen.x + (destino.x - origen.x) * e,
                                       y: origen.y + (destino.y - origen.y) * e))
        }
        RunLoop.main.add(t, forMode: .common)
        reloj = t
    }
}
