import Foundation
import CoreGraphics

/// LA DISCULPA: perdón, y a resolverlo.
///
/// Una cara quieta no pide perdón. Un gesto triste estático se lee como lástima —«qué pena me doy»—
/// y eso PIDE consuelo en vez de darlo. Pedir perdón es algo que pasa EN EL TIEMPO, y por eso esto
/// no es una pose: es una secuencia. Y la secuencia no termina en la disculpa, termina **buscando**.
///
/// Lo que la gobierna, y no es un detalle de animación sino la idea entera:
///
/// > **La intención del perdón tiene que verse; el perdón no puede convertirse en espera.**
///
/// El que está al otro lado no está decepcionado, está furioso: a una inteligencia artificial se le
/// supone que lo sabe todo, así que cuando se equivoca la reacción humana es rabia. Y lo que calma
/// esa rabia no es una cara compungida — es ver que ya está buscando el problema.
///
/// Los cinco tiempos, poco más de dos segundos:
///
///   1. **El respingo** (0–160 ms). Se encoge de golpe y cierra los ojos. Es involuntario, y por eso
///      convence: nadie finge un respingo. Aquí admite que pasó algo.
///   2. **El perdón** (160–800 ms). La cabeza se ladea, se hace pequeña y LA MIRADA SE VA AL SUELO.
///      No poder mirarte a los ojos es la señal de vergüenza que no se puede fingir.
///   3. **Se endereza** (800–1080 ms). Sube los ojos, se destuerce y recupera su tamaño.
///   4. **El asentimiento** (1080–1400 ms). Baja la cabeza y la sube, firme. Es un «entendido, voy».
///      Un cabezazo corto y decidido dice lo que ninguna cara fija puede decir: que ya se hizo cargo.
///   5. **La búsqueda** (1400–2150 ms). Entorna los ojos y BARRE con la mirada, izquierda y derecha,
///      como quien repasa la pantalla buscando dónde se rompió. Al acabar se pasa sola a trabajando.
///
/// **POR QUÉ NO UN GUIÑO** (2026-08-14, lo descartó el usuario): un guiño es complicidad, y la
/// complicidad después de un fallo suena a quitarle importancia — «tranquilo, esto entre nosotros».
/// Justo lo contrario de lo que hay que transmitir. Asentir y buscar son gestos de hacerse cargo.
///
/// **HUBO UNA QUIETUD Y SE QUITÓ** (mismo día): más de un segundo inmóvil, para que el silencio
/// pesara. Hace lo contrario: **da más rabia**, porque una cara congelada esperando absolución le
/// pasa el trabajo a quien ya está furioso. El tiempo que se ganó volvió aquí, pero como movimiento:
/// todos estos tiempos hacen algo, ninguno espera.
struct Disculpa {

    /// Cuánto ha bajado la mirada, en unidades del viewBox. Positivo = mira al suelo.
    var mirada: CGFloat = 0
    /// Barrido horizontal de la mirada, para cuando está buscando.
    var miradaX: CGFloat = 0
    /// Cuánto se ha encogido. 1 = tamaño normal.
    var tamano: CGFloat = 1
    /// Cuánto ladea la cabeza, en grados.
    var ladeo: CGFloat = 0
    /// Cuánto baja la cabeza entera, en unidades del viewBox. Es el asentimiento.
    var cabezazo: CGFloat = 0
    /// Si en este instante le toca cerrar los ojos (el respingo).
    var cierraOjos: CGFloat = 0
    /// Cuánto pesa la cara de estar concentrada buscando (0 = la de siempre, 1 = entera).
    var decidida: CGFloat = 0
    /// Mientras es `true`, nadie más puede moverle los ojos ni ponerle gestos de relleno.
    var mandaElla = true
    /// Ya pidió perdón y ya se puso a buscar. Toca ponerse a trabajar.
    var terminada = false

    private static let suave  = KeySpline(0.25, 0, 0.15, 1)
    private static let vuelve = KeySpline(0.30, 0, 0.20, 1)
    private static let firme  = KeySpline(0.30, 0, 0.10, 1)

    /// Cuánto baja la mirada en lo hondo del perdón. Siete unidades es mucho para unos ojos que viven
    /// a la altura −14: se nota sin que la carita parezca dormida.
    private static let miradaAbajo: CGFloat = 7
    private static let tamanoHundida: CGFloat = 0.92

    /// La cara de estar buscando el problema: cejas bajas y RECTAS (concentración, no enfado — las
    /// rectas concentran, las anguladas enfadan), ojos entornados de foco, y la boca firme y pequeña,
    /// sin rastro de la sonrisa. Nadie busca algo con cara de simpático.
    static let caraDecidida = FacePose(
        browL: 0.5, browR: 1.5, curveL: 0.15, curveR: 0.20,
        eyeOpen: 0.78, squint: 0.32,
        mouthCurve: 0.25, mouthWidth: 34 * 0.85,
        cornerL: 0.10, cornerR: 0.15, accent: nil)

    static let dura: TimeInterval = 2.150

    static func en(_ t: TimeInterval) -> Disculpa {
        var d = Disculpa()

        // 1 · El respingo.
        if t < 0.160 {
            let p = CGFloat(t / 0.160)
            d.cierraOjos = p < 0.6 ? p / 0.6 : 1
            d.tamano = 1 - 0.12 * p                          // se encoge de golpe: 0.88
            d.mirada = miradaAbajo * 0.30 * p
            d.ladeo = 2 * p
            return d
        }

        // 2 · El perdón. Con tiempo suficiente para que se le vea la intención.
        if t < 0.800 {
            let p = Self.suave.eval(CGFloat((t - 0.160) / 0.640))
            d.cierraOjos = max(0, 1 - p * 2.4)               // abre los ojos mientras baja la mirada
            d.tamano = 0.88 + (tamanoHundida - 0.88) * p
            d.mirada = miradaAbajo * (0.30 + 0.70 * p)
            d.ladeo = 2 + 5 * p                              // acaba ladeada 7°
            return d
        }

        // 3 · Se endereza.
        if t < 1.080 {
            let p = Self.vuelve.eval(CGFloat((t - 0.800) / 0.280))
            d.tamano = tamanoHundida + (1 - tamanoHundida) * p
            d.mirada = miradaAbajo * (1 - p)
            d.ladeo = 7 * (1 - p)
            return d
        }

        // 4 · El asentimiento: «entendido, voy». Baja rápido y sube frenando, como una cabeza de
        // verdad — si baja y sube igual de rápido parece un rebote, no una decisión.
        if t < 1.400 {
            let u = CGFloat((t - 1.080) / 0.320)
            d.cabezazo = u < 0.42 ? 6 * (u / 0.42) : 6 * (1 - Self.firme.eval((u - 0.42) / 0.58))
            d.decidida = min(1, u * 1.6)
            return d
        }

        // 5 · La búsqueda: entorna los ojos y barre la pantalla, izquierda y derecha.
        if t < dura {
            let u = CGFloat((t - 1.400) / 0.750)
            d.decidida = 1
            // Un barrido de ida y vuelta, con una pausa en cada extremo: los ojos que se mueven sin
            // parar no leen nada, y eso se nota. Buscar es mirar, detenerse, y mirar en otro sitio.
            let barrido: CGFloat
            switch u {
            case ..<0.28: barrido = -Self.firme.eval(u / 0.28)
            case ..<0.42: barrido = -1
            case ..<0.72: barrido = -1 + 2 * Self.firme.eval((u - 0.42) / 0.30)
            case ..<0.86: barrido = 1
            default:      barrido = 1 - Self.firme.eval((u - 0.86) / 0.14)
            }
            d.miradaX = barrido * 3.4
            return d
        }

        d.mandaElla = false
        d.terminada = true
        return d
    }
}
