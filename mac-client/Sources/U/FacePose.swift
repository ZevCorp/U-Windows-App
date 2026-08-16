import AppKit

/// Modos de color de la carita: claro (fondo blanco) u oscuro (línea blanca).
enum FaceTheme { case light, dark }

/// Qué está haciendo Ü, dicho con la cara. Nueve y no seis por dos separaciones que importan:
///
///   · `detenido` ≠ `fallo` — «yo lo paré» y «se rompió solo» son causas distintas con acciones
///     distintas; juntarlas es el vicio de los mensajes que no distinguen.
///   · `conversando` ≠ `escuchando`, y la diferencia es CUÁNTO DURA. Escuchando se hizo para el
///     dictado: ocho segundos como mucho, ojos bien abiertos y respirando, porque en ocho segundos
///     eso se lee como atención. Una conversación en vivo dura minutos, y ahí lo mismo pasa a ser
///     una carita con los ojos como platos que jadea sin parar delante de alguien que trabaja.
enum FaceMood: String, CaseIterable {
    case reposo, escuchando, conversando, trabajando, grabando, esperando, hablando, detenido, fallo
}

/// Los diez escalares que definen una expresión, más el acento.
struct FacePose {
    let browL: CGFloat, browR: CGFloat, curveL: CGFloat, curveR: CGFloat
    let eyeOpen: CGFloat, squint: CGFloat, mouthCurve: CGFloat, mouthWidth: CGFloat
    let cornerL: CGFloat, cornerR: CGFloat
    let accent: CGColor?
}

extension FacePose {
    /// Copiados VALOR A VALOR de la tabla `Poses` de `FaceControl.cs`. Si un número cambia aquí y no
    /// allá, las dos caritas dejan de ser la misma — que es justo lo que este puerto evita.
    static let table: [FaceMood: FacePose] = [
        //                     browL browR curvL curvR eyeOpen squint mCurve  width      cornL cornR  acento
        .reposo:      .init(browL:  2, browR: 2.5, curveL: 0.30, curveR: 0.40, eyeOpen: 0.85, squint: 0.15, mouthCurve:  0.7, mouthWidth: 34 * 1.10, cornerL: 0.30, cornerR: 0.50, accent: nil),

        // SIN TINTE, los dos siguientes. Escuchar y trabajar son los estados en los que más rato pasa
        // la carita —con la conversación en vivo, «escuchando» es casi toda la sesión— y teñir la cara
        // entera de verde o de azul durante minutos cansa a quien la tiene siempre delante. Su gesto ya
        // los distingue: cejas altas y ojos abiertos para escuchar, ceja torcida para trabajar. El color
        // se guarda para lo que interrumpe —grabando, esperando, fallo—, que es cuando merece la pena
        // robar la mirada.
        .trabajando:  .init(browL: -1, browR: 4.0, curveL: 0.10, curveR: 0.50, eyeOpen: 0.75, squint: 0.20, mouthCurve:  0.7, mouthWidth: 34 * 0.95, cornerL: 0.20, cornerR: 0.10, accent: nil),
        // Cejas altas y ojos bien abiertos: la cara de estar prestando atención.
        .escuchando:  .init(browL:  6, browR: 6.0, curveL: 0.35, curveR: 0.35, eyeOpen: 1.00, squint: 0.05, mouthCurve:  0.6, mouthWidth: 34 * 1.05, cornerL: 0.35, cornerR: 0.35, accent: nil),
        // Casi el reposo, con la ceja un pelo más alta. A propósito: es lo que se ve durante toda una
        // conversación —el rato en que no dice nada es la mayor parte— y tiene que poder mirarse sin
        // cansar.
        .conversando: .init(browL:  3, browR: 3.5, curveL: 0.30, curveR: 0.40, eyeOpen: 0.90, squint: 0.12, mouthCurve:  0.7, mouthWidth: 34 * 1.10, cornerL: 0.30, cornerR: 0.45, accent: nil),
        // Quieta y mirando de frente: «te estoy viendo». La quietud es la señal.
        .grabando:    .init(browL:  2, browR: 2.0, curveL: 0.30, curveR: 0.30, eyeOpen: 0.95, squint: 0.10, mouthCurve:  0.4, mouthWidth: 34 * 0.80, cornerL: 0.20, cornerR: 0.20, accent: UiPalette.fallo),
        // Asimetría interrogativa: una ceja sube, la otra baja.
        .esperando:   .init(browL:  6, browR: -1.0, curveL: 0.45, curveR: 0.15, eyeOpen: 0.90, squint: 0.10, mouthCurve: 0.2, mouthWidth: 34 * 0.95, cornerL: 0.40, cornerR: 0.10, accent: UiPalette.atencion),
        .hablando:    .init(browL:  2, browR: 2.5, curveL: 0.30, curveR: 0.40, eyeOpen: 0.85, squint: 0.15, mouthCurve:  0.9, mouthWidth: 34 * 1.25, cornerL: 0.40, cornerR: 0.40, accent: nil),
        // Boca recta y ojos entornados: ni contenta ni enfadada, parada.
        .detenido:    .init(browL:  0, browR: 0.0, curveL: 0.20, curveR: 0.20, eyeOpen: 0.60, squint: 0.25, mouthCurve:  0.0, mouthWidth: 34 * 0.90, cornerL: 0.00, cornerR: 0.00, accent: UiPalette.inactivo),
        // PIDE PERDÓN, no se enoja. Antes tenía las cejas caídas (−3) y un ceño marcado (−0.5), y esa
        // es la cara de estar molesto CON EL OTRO: exactamente lo contrario de lo que hay que poner
        // cuando el que falló fuiste tú.
        //
        // Disculparse es al revés en todo: las cejas SUBEN y se arquean (es el gesto que no se puede
        // fingir), los ojos se achican en vez de abrirse, y la boca se mete —pequeña y apenas caída—
        // en lugar de dibujar un ceño. Un ceño grande pide pelea; una boca chiquita pide perdón.
        // La cabeza ladeada la pone `inclinacionPorEstado`.
        .fallo:       .init(browL:  7, browR: 6.0, curveL: 0.50, curveR: 0.45, eyeOpen: 0.62, squint: 0.30, mouthCurve: -0.18, mouthWidth: 34 * 0.72, cornerL: 0.05, cornerR: 0.02, accent: UiPalette.fallo),
    ]

    static func pose(_ mood: FaceMood) -> FacePose { table[mood] ?? table[.reposo]! }
}
