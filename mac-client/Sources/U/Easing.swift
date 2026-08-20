import CoreGraphics

/// Las curvas que usa WPF, reimplementadas para que las animaciones tengan la MISMA forma.
///
/// No es purismo: la carita se reconoce tanto por el ritmo como por el dibujo. Un parpadeo lineal
/// donde había lineal y una respiración senoidal donde había senoidal es lo que hace que se vea
/// igual y no «parecida».
enum Easing {

    /// `SineEase { EasingMode = EaseInOut }` de WPF.
    static func sineInOut(_ t: CGFloat) -> CGFloat {
        (1 - cos(.pi * clamp01(t))) / 2
    }

    static func clamp01(_ v: CGFloat) -> CGFloat { min(1, max(0, v)) }

    /// Onda triangular con AutoReverse+Forever: `period` es el ciclo COMPLETO (ida + vuelta).
    /// Devuelve el progreso 0→1→0 ya suavizado.
    static func autoReverse(_ elapsed: CGFloat, period: CGFloat) -> CGFloat {
        guard period > 0 else { return 0 }
        let p = (elapsed.truncatingRemainder(dividingBy: period)) / period
        let t = p < 0.5 ? p * 2 : (1 - p) * 2
        return sineInOut(t)
    }
}

/// `KeySpline` de WPF: bezier cúbica con P0=(0,0), P3=(1,1) y dos controles.
/// Dado el progreso en X (el tiempo del tramo), devuelve el progreso en Y (el valor).
struct KeySpline {
    let x1: CGFloat, y1: CGFloat, x2: CGFloat, y2: CGFloat

    init(_ x1: CGFloat, _ y1: CGFloat, _ x2: CGFloat, _ y2: CGFloat) {
        self.x1 = x1; self.y1 = y1; self.x2 = x2; self.y2 = y2
    }

    private func bezier(_ t: CGFloat, _ a: CGFloat, _ b: CGFloat) -> CGFloat {
        let mt = 1 - t
        return 3 * mt * mt * t * a + 3 * mt * t * t * b + t * t * t
    }

    private func bezierPrime(_ t: CGFloat, _ a: CGFloat, _ b: CGFloat) -> CGFloat {
        let mt = 1 - t
        return 3 * mt * mt * a + 6 * mt * t * (b - a) + 3 * t * t * (1 - b)
    }

    func eval(_ x: CGFloat) -> CGFloat {
        let xc = Easing.clamp01(x)
        // Newton-Raphson: converge en 4–5 pasos para las curvas suaves que usamos aquí.
        var t = xc
        for _ in 0..<8 {
            let err = bezier(t, x1, x2) - xc
            if abs(err) < 1e-5 { break }
            let d = bezierPrime(t, x1, x2)
            if abs(d) < 1e-6 { break }
            t -= err / d
            t = Easing.clamp01(t)
        }
        return bezier(t, y1, y2)
    }
}
