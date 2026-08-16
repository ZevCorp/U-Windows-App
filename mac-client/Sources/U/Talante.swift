import AppKit

/// EL ESPEJO.
///
/// Los `FaceMood` dicen QUÉ ESTÁ HACIENDO Ü — trabajando, escuchando, fallando. Son nueve y ninguno
/// sirve para lo que hace distinto a este producto, porque ninguno dice CÓMO SE LO ESTÁ TOMANDO.
///
/// Un talante es una reacción: dura un momento, se pinta ENCIMA de lo que estuviera haciendo, y se
/// va sola. Es exactamente lo que pasa en una conversación de verdad — estás trabajando, te dicen
/// algo, pones una cara dos segundos, y vuelves a lo tuyo. Ü no deja de trabajar para reírse.
enum Talante: String, CaseIterable {
    case burlon      // «te la devolví»
    case risa        // muerto de risa
    case complice    // los dos sabemos de qué va esto
    case ofendido    // ofendido de mentira
    case sobrado     // engreído, con la barbilla arriba
    case retador     // «¿ah sí?»
    case pillado     // lo cacharon
    case ternura     // cariño de verdad, sin ironía

    var titulo: String {
        switch self {
        case .burlon:   return "Te la devolví"
        case .risa:     return "Muerto de risa"
        case .complice: return "Cómplice"
        case .ofendido: return "Ofendido (de mentira)"
        case .sobrado:  return "Sobrado"
        case .retador:  return "¿Ah sí?"
        case .pillado:  return "Lo cacharon"
        case .ternura:  return "Ternura"
        }
    }
}

/// Lo que define una reacción: la cara, cuánto dura, si inclina la cabeza, y qué gesto la acompaña.
struct Reaccion {
    let pose: FacePose
    /// Cuánto se queda la cara puesta, sin contar la entrada ni la salida.
    let aguanta: TimeInterval
    /// Inclinación de la cabeza en grados. La barbilla arriba es media personalidad.
    let inclina: CGFloat
    /// Cuánto abre la boca y qué forma tiene, para las que hablan con la boca (risa, sorpresa).
    let boca: CGFloat
    let redondez: CGFloat
    let gesto: Gesto

    enum Gesto { case ninguno, guinoDerecho, guinoIzquierdo, parpadeoDoble, pulso, ojosALado }
}

extension Reaccion {
    /// El repertorio. Cada una está hecha con los MISMOS diez números que las caras de siempre —no
    /// hay un dibujo nuevo—, y ahí está la gracia: la personalidad no es otra carita, es esta misma
    /// sabiendo poner caras.
    static let tabla: [Talante: Reaccion] = [

        // Una ceja disparada, la otra caída, ojos entornados y media sonrisa que solo sube de un lado.
        // La asimetría ES el chiste: una sonrisa simétrica es amable, una torcida es una respuesta.
        .burlon: .init(
            pose: .init(browL: -2, browR: 9, curveL: 0.10, curveR: 0.45, eyeOpen: 0.70, squint: 0.35,
                        mouthCurve: 0.50, mouthWidth: 34 * 0.95, cornerL: 0.00, cornerR: 0.70, accent: nil),
            aguanta: 1.10, inclina: -4, boca: 0, redondez: 0, gesto: .ninguno),

        // Los ojos casi cerrados y la boca bien abierta. Nadie se ríe de verdad con los ojos abiertos.
        .risa: .init(
            pose: .init(browL: 5, browR: 5, curveL: 0.40, curveR: 0.40, eyeOpen: 0.25, squint: 0.50,
                        mouthCurve: 1.00, mouthWidth: 34 * 1.30, cornerL: 0.60, cornerR: 0.60, accent: nil),
            aguanta: 1.30, inclina: 3, boca: 0.75, redondez: 0.15, gesto: .ninguno),

        // El guiño hace el trabajo; la cara solo tiene que acompañarlo sin robarle protagonismo.
        .complice: .init(
            pose: .init(browL: 4, browR: 1, curveL: 0.35, curveR: 0.20, eyeOpen: 0.85, squint: 0.20,
                        mouthCurve: 0.75, mouthWidth: 34 * 1.05, cornerL: 0.20, cornerR: 0.55, accent: nil),
            aguanta: 0.75, inclina: -3, boca: 0, redondez: 0, gesto: .guinoDerecho),

        // Cejas caídas de enfado… y una comisura arriba que lo delata. ESA comisura es todo: sin ella
        // es un enfado de verdad, y con ella es un juego. Un píxel separa las dos cosas.
        .ofendido: .init(
            pose: .init(browL: -4, browR: -3, curveL: 0.12, curveR: 0.15, eyeOpen: 0.90, squint: 0.10,
                        mouthCurve: 0.10, mouthWidth: 34 * 0.85, cornerL: 0.45, cornerR: 0.00, accent: nil),
            aguanta: 1.20, inclina: 5, boca: 0, redondez: 0, gesto: .ojosALado),

        // Ojos a media asta y barbilla arriba: el que ya sabía la respuesta.
        .sobrado: .init(
            pose: .init(browL: 1, browR: 3, curveL: 0.20, curveR: 0.35, eyeOpen: 0.50, squint: 0.40,
                        mouthCurve: 0.45, mouthWidth: 34 * 0.90, cornerL: 0.05, cornerR: 0.80, accent: nil),
            aguanta: 1.15, inclina: 7, boca: 0, redondez: 0, gesto: .ninguno),

        // Una sola ceja, lo más arriba que llega, y quieta. Es una pregunta sin palabras.
        .retador: .init(
            pose: .init(browL: 1, browR: 10, curveL: 0.15, curveR: 0.50, eyeOpen: 0.95, squint: 0.05,
                        mouthCurve: 0.35, mouthWidth: 34 * 0.90, cornerL: 0.05, cornerR: 0.50, accent: nil),
            aguanta: 1.25, inclina: -5, boca: 0, redondez: 0, gesto: .ninguno),

        // Cejas arriba del todo, ojos como platos y la boca en «o». La cara de «me viste».
        .pillado: .init(
            pose: .init(browL: 7, browR: 7, curveL: 0.45, curveR: 0.45, eyeOpen: 1.00, squint: 0.00,
                        mouthCurve: 0.30, mouthWidth: 34 * 0.70, cornerL: 0.15, cornerR: 0.15, accent: nil),
            aguanta: 0.85, inclina: 0, boca: 0.55, redondez: 0.85, gesto: .parpadeoDoble),

        // La única sin ironía. Cejas suaves, ojos entornados de gusto, sonrisa ancha y pareja: aquí la
        // simetría sí se quiere, porque es lo que la separa de todas las de arriba.
        .ternura: .init(
            pose: .init(browL: 4, browR: 4, curveL: 0.40, curveR: 0.40, eyeOpen: 0.80, squint: 0.25,
                        mouthCurve: 0.95, mouthWidth: 34 * 1.15, cornerL: 0.50, cornerR: 0.50, accent: nil),
            aguanta: 1.20, inclina: 4, boca: 0, redondez: 0, gesto: .ninguno),
    ]

    static func de(_ t: Talante) -> Reaccion { tabla[t] ?? tabla[.complice]! }
}

extension FacePose {
    /// Mezcla dos caras. Es lo que hace que una reacción ENTRE y SALGA en vez de aparecer de golpe:
    /// una cara que salta de un cuadro a otro se lee como un error de dibujo, no como un gesto.
    func mezclando(hacia otra: FacePose, _ t: CGFloat) -> FacePose {
        func m(_ a: CGFloat, _ b: CGFloat) -> CGFloat { a + (b - a) * t }
        return FacePose(
            browL: m(browL, otra.browL), browR: m(browR, otra.browR),
            curveL: m(curveL, otra.curveL), curveR: m(curveR, otra.curveR),
            eyeOpen: m(eyeOpen, otra.eyeOpen), squint: m(squint, otra.squint),
            mouthCurve: m(mouthCurve, otra.mouthCurve), mouthWidth: m(mouthWidth, otra.mouthWidth),
            cornerL: m(cornerL, otra.cornerL), cornerR: m(cornerR, otra.cornerR),
            // El acento no se mezcla: es del estado (grabando, fallo) y una reacción no lo cambia.
            accent: accent)
    }
}
