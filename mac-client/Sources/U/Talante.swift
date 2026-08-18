import AppKit
import Foundation

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
    case enojado     // se le fue acumulando y se le nota
    case entendido   // «lo pillé, voy» — el guiño de confirmar
    case perdido     // «¿eh?» — no entendió, o va a preguntar
    case cuidado     // «ojo con esto» — el aviso, antes de que pase
    case apenado     // «uy, salió mal» — ya pasó, y lo sabe
    case feliz       // contenta de verdad, con los ojos cerrados
    case confundido  // se está perdiendo mientras le hablas

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
        case .enojado:  return "Enojada (se le va acumulando)"
        case .entendido: return "Entendido, voy"
        case .perdido:   return "No entendí (pregunta)"
        case .cuidado:   return "Ojo con esto (cuidado)"
        case .apenado:   return "Uy, salió mal"
        case .feliz:     return "Feliz (te alegró)"
        case .confundido: return "Se está perdiendo"
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

    /// CUÁNTO TARDA EN APARECER. Era una constante para todas —130 ms, rápido, porque una reacción
    /// tardona no reacciona a nada— y sigue siéndolo por defecto.
    ///
    /// Dejó de poder serlo con el enfado: un enfado que aparece en 130 ms es un susto, y lo que lo
    /// vuelve gracioso es justo lo contrario, VERLO LLEGAR. La duración no es un detalle de la
    /// animación, es de qué emoción se trata.
    let entra: TimeInterval

    enum Gesto { case ninguno, guinoDerecho, guinoIzquierdo, parpadeoDoble, pulso, ojosALado }

    init(pose: FacePose, aguanta: TimeInterval, inclina: CGFloat, boca: CGFloat,
         redondez: CGFloat, gesto: Gesto, entra: TimeInterval = 0.130) {
        self.pose = pose; self.aguanta = aguanta; self.inclina = inclina
        self.boca = boca; self.redondez = redondez; self.gesto = gesto; self.entra = entra
    }
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

        // EL ENFADO, Y ES DE MENTIRA. Lo dice todo la boca: no es un ceño con la boca hacia abajo
        // —eso da miedo— sino una MUECA ONDULADA, que es la cara del berrinche. Un enfado con la
        // boca torcida discute; uno con la boca ondulada está haciendo un puchero, y eso da risa.
        //
        // Las cejas van RECTAS (`curve` casi cero) y apuntando a la nariz (`browTilt`). Rectas y
        // anguladas es enfado; curvas hacia abajo es pena, que es la otra cara y no esta.
        //
        // NO ES `ofendido`, y la diferencia está en el tiempo, no en el dibujo. `ofendido` salta en
        // 130 ms: te pasaste con ella y te contesta. Este TARDA 850 ms en llegar, y esos 850 ms son
        // el chiste entero — se le va acumulando delante de ti. Un enfado instantáneo es un susto;
        // uno que ves venir es cómico.
        .enojado: .init(
            pose: .init(browL: 1.0, browR: 1.0, curveL: 0.05, curveR: 0.05, eyeOpen: 0.82, squint: 0.28,
                        mouthCurve: 0.0, mouthWidth: 34 * 0.76, cornerL: 0.0, cornerR: 0.0,
                        browTilt: 5.0, mouthWave: 0.55, accent: nil),
            // Aguanta más que las demás: después de tomarse ese tiempo en llegar, irse enseguida
            // sería no haberse enfadado.
            aguanta: 1.60, inclina: 2, boca: 0, redondez: 0, gesto: .ninguno, entra: 0.850),

        // «ENTENDIDO, VOY». Un guiño y media sonrisa: la cara de quien ya sabe lo que hay que hacer
        // y no necesita que se lo expliquen otra vez.
        //
        // La sonrisa es TORCIDA —la comisura de arriba es la del lado que guiña—, y eso no es
        // adorno: una sonrisa pareja con un guiño se lee como coqueteo. Torcida, el guiño y la boca
        // apuntan a lo mismo y sale un «dalo por hecho».
        //
        // La ceja del ojo que guiña BAJA y la otra se queda arriba. Es lo que hace una cara de
        // verdad al cerrar un ojo, y sin eso el guiño parece que se le metió algo en el ojo.
        //
        // El guiño va en la POSE y no en `gesto: .guinoDerecho`: así se cierra mientras la expresión
        // entra y se abre mientras sale, en vez de tener su propio reloj de 620 ms que se acaba
        // antes que la cara que lo trajo.
        .entendido: .init(
            pose: .init(browL: 3.5, browR: 1.0, curveL: 0.42, curveR: 0.28, eyeOpen: 0.90, squint: 0.15,
                        mouthCurve: 0.75, mouthWidth: 34 * 1.10, cornerL: 0.12, cornerR: 0.62,
                        guino: 1.0, accent: nil),
            // Entra un poco más despacio que las demás (320 ms y no 130): un guiño es un gesto que
            // se HACE, y hecho en un cuadro y medio no se ve guiñar, se ve el ojo ya cerrado.
            aguanta: 0.55, inclina: -3, boca: 0, redondez: 0, gesto: .ninguno, entra: 0.320),

        // «¿EH?». No entendió lo que le pediste, o va a preguntar algo. Es el negativo exacto de
        // `entendido`: mismo momento de la conversación, la otra respuesta posible.
        //
        // UNA SOLA CEJA, y muy arriba. La cúpula es todo el gesto: dos cejas subidas a la vez es
        // sorpresa —«¡anda!»— y una sola subida es una pregunta. La otra se queda abajo y casi
        // recta, porque lo que hace legible la pregunta es el CONTRASTE entre las dos, no lo alta
        // que esté la que sube.
        //
        // La boca se queda a medias: ni sonríe ni deja de sonreír. Una boca recta del todo sería la
        // cara de «no me hace gracia», y esto no es reproche — es que no se enteró. La onda es la
        // que dice «mmm…».
        //
        // Y la cabeza se ladea cuatro grados. Ese ladeo es lo que convierte una cara extrañada en
        // una PREGUNTA: es lo que hace todo el mundo al no entender, y sin él la cara se queda en
        // desconcierto sin pedir nada.
        .perdido: .init(
            pose: .init(browL: 1.0, browR: 10.5, curveL: 0.25, curveR: 0.95, eyeOpen: 0.92, squint: 0.10,
                        mouthCurve: -0.02, mouthWidth: 34 * 0.88, cornerL: 0.38, cornerR: 0.00,
                        mouthWave: 0.60, accent: nil),
            aguanta: 1.10, inclina: 4, boca: 0, redondez: 0, gesto: .ninguno, entra: 0.420),

        // «OJO CON ESTO». El aviso, y va ANTES de que pase algo — no después. Esa es la diferencia
        // con `fallo`, que es la cara de cuando ya se rompió: aquella pide perdón, ésta te frena.
        //
        // LA BOCA COMPLETAMENTE RECTA ES TODO EL GESTO. Ni sonríe ni se tuerce ni ondula: se queda
        // quieta y ancha. Es la única cara del repertorio con la boca a cero, y por eso funciona —
        // en una carita que siempre sonríe un poco, dejar de sonreír YA es decir algo. Torcerla
        // hacia abajo sería reñir, y esto no riñe: avisa.
        //
        // Las cejas SUBEN Y SE ARQUEAN, no se fruncen. Fruncidas con la boca recta sale una cara de
        // enfado contenido —«te lo dije»—, y el aviso dejaría de ser un favor para ser un reproche.
        // Arqueadas, la cara dice «mira esto», que es lo que se quiere.
        //
        // Sin tinte, aunque el naranja de aviso exista y encajara: el acento no se mezcla —es del
        // estado— así que puesto en una reacción no se pintaría, y un número que no hace nada es
        // peor que no ponerlo. Si algún día esto tiene que teñir, es porque debe ser un estado.
        .cuidado: .init(
            pose: .init(browL: 4.5, browR: 4.5, curveL: 0.60, curveR: 0.60, eyeOpen: 0.88, squint: 0.12,
                        mouthCurve: 0.0, mouthWidth: 34 * 1.60, cornerL: 0.0, cornerR: 0.0, accent: nil),
            // Aguanta más y entra más despacio que las demás: un aviso que pasa volando no avisa, y
            // uno que aparece de golpe asusta en vez de prevenir.
            aguanta: 1.30, inclina: 0, boca: 0, redondez: 0, gesto: .ninguno, entra: 0.380),

        // «UY, SALIÓ MAL». Ya pasó, y lo sabe. Es el par de `cuidado`: aquella avisa antes, ésta
        // reconoce después. Y no es `fallo`, que es la disculpa entera con su cabezazo y su
        // búsqueda — ésta es el instante de darse cuenta, y cabe en segundo y medio.
        //
        // LAS CEJAS AL REVÉS QUE EL ENFADO. En `enojado` los extremos de dentro BAJAN; aquí SUBEN
        // (`browTilt` negativo), y ese solo cambio de signo es la diferencia entre enfadarse y
        // apenarse. Es el gesto que no se puede fingir, y por eso convence.
        //
        // La boca en «o» hueca, no una mueca hacia abajo. Una boca caída es tristeza —pide consuelo—
        // y esto no pide nada: es el «uy» de darse cuenta, que dura un momento y sigue.
        .apenado: .init(
            pose: .init(browL: 3.0, browR: 3.0, curveL: 0.30, curveR: 0.30, eyeOpen: 0.85, squint: 0.15,
                        mouthCurve: -0.15, mouthWidth: 34 * 0.85, cornerL: 0.0, cornerR: 0.0,
                        browTilt: -6.0, bocaO: 1.0, accent: nil),
            aguanta: 1.25, inclina: 3, boca: 0, redondez: 0, gesto: .ninguno, entra: 0.400),

        // FELIZ, y lo dicen LOS OJOS. Ese es el hallazgo de esta cara: la sonrisa ancha ya la tenían
        // `risa`, `ternura` y `logrado`, así que una boca grande no distingue nada. Lo que no tenía
        // ninguna son los dos ojos cerrados en arco — y resulta que es el rasgo que separa una
        // sonrisa educada de una alegría de verdad. Nadie se alegra con los ojos abiertos.
        //
        // No es `risa`: aquella tiene la boca abierta y se está riendo DE algo. Ni `ternura`, que va
        // hacia ti. Ésta es hacia dentro — te hicieron un cumplido y te gustó.
        //
        // Y no es `logrado`, que es el brinco de terminar una tarea: eso se celebra con el cuerpo y
        // dura segundo y medio; esto es una cara y se pasa sola.
        .feliz: .init(
            pose: .init(browL: 5.0, browR: 5.0, curveL: 0.55, curveR: 0.55, eyeOpen: 0.85, squint: 0.15,
                        mouthCurve: 1.00, mouthWidth: 34 * 1.34, cornerL: 0.58, cornerR: 0.58,
                        ojosArco: 1.0, accent: nil),
            aguanta: 1.25, inclina: 4, boca: 0, redondez: 0, gesto: .ninguno, entra: 0.360),

        // SE ESTÁ PERDIENDO. No es `perdido`, y la diferencia está en las cejas:
        //
        //  · `perdido` levanta UNA ceja en cúpula. Eso es una PREGUNTA — ya se dio cuenta de que no
        //    entendió y te va a interrumpir para pedirte que repitas.
        //  · Ésta tiene las DOS caídas hacia fuera (`browTilt` negativo) y desiguales. Eso es estar
        //    perdiéndose MIENTRAS te oye, todavía sin decir nada. Es lo que se ve en una cara
        //    cuando alguien lleva un rato explicando y el hilo se le escapó hace dos frases.
        //
        // Las cejas desiguales son deliberadas: parejas darían una cara de pena simétrica, y esto no
        // da pena — da desconcierto. La que sube busca y la que cae se rinde, las dos a la vez.
        //
        // Y la boca en zigzag, que aquí trabaja distinto que en el enfado: allí es un puchero, y con
        // las cejas caídas es un «se me perdió». El mismo rasgo dice dos cosas según lo que tenga
        // encima — que es exactamente lo que se pretendía al añadirlo.
        .confundido: .init(
            pose: .init(browL: 1.5, browR: 5.0, curveL: 0.30, curveR: 0.35, eyeOpen: 0.85, squint: 0.18,
                        mouthCurve: -0.05, mouthWidth: 34 * 0.80, cornerL: 0.10, cornerR: 0.0,
                        browTilt: -5.0, mouthWave: 0.65, accent: nil),
            // Entra despacio: perderse es algo que va pasando, no algo que pasa de golpe.
            aguanta: 1.20, inclina: 5, boca: 0, redondez: 0, gesto: .ninguno, entra: 0.440),
    ]

    static func de(_ t: Talante) -> Reaccion { tabla[t] ?? tabla[.complice]! }

    /// Congela el peso de la reacción en un punto fijo (0…1) para poder mirarla quieta:
    ///
    ///     U_TALANTE=enojado U_PESO=0.6 ./U.app/Contents/MacOS/U
    ///
    /// Hermano de `U_CARA` y de `U_ATENCION`. `let` y no `var`: se consulta en cada cuadro.
    static let congelada: CGFloat? = {
        guard let v = ProcessInfo.processInfo.environment["U_PESO"], let n = Double(v) else { return nil }
        return CGFloat(min(1, max(0, n)))
    }()
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
            browTilt: m(browTilt, otra.browTilt), mouthWave: m(mouthWave, otra.mouthWave),
            lengua: m(lengua, otra.lengua), guino: m(guino, otra.guino),
            bocaO: m(bocaO, otra.bocaO), ojosArco: m(ojosArco, otra.ojosArco),
            // El acento no se mezcla: es del estado (grabando, fallo) y una reacción no lo cambia.
            accent: accent)
    }
}
