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
    case reposo, escuchando, conversando, trabajando, pensando, grabando, esperando, hablando, detenido, fallo, logrado
}

extension FaceMood {
    /// CUÁNTO TARDA ESTA CARA EN APARECER. Cero para casi todas, y no por descuido: cambiar de
    /// estado siempre fue instantáneo aquí, y para «grabando» o «detenido» está bien —son avisos, y
    /// un aviso que aparece despacio avisa tarde.
    ///
    /// Pensar es el caso que lo rompe. Ponerse a pensar de golpe no se lee como pensar, se lee como
    /// un cambio de imagen; lo que se reconoce como pensar es la cara ENTRANDO en ello. Por eso la
    /// duración es de la cara y no un ajuste común: es parte de qué estado es.
    var tarda: TimeInterval {
        switch self {
        case .pensando: return 0.700
        // Lo que tarda en agacharse antes del primer brinco: la cara de alegría tiene que estar ya
        // puesta cuando despega, no llegar a mitad del salto.
        case .logrado:  return 0.220
        default:        return 0
        }
    }
}

/// Los escalares que definen una expresión, más el acento.
struct FacePose {
    let browL: CGFloat, browR: CGFloat, curveL: CGFloat, curveR: CGFloat
    let eyeOpen: CGFloat, squint: CGFloat, mouthCurve: CGFloat, mouthWidth: CGFloat
    let cornerL: CGFloat, cornerR: CGFloat

    /// CUÁNTO SE INCLINA LA CEJA: el extremo de dentro baja y el de fuera sube, en unidades del
    /// viewBox. Positivo es el ceño; negativo, la ceja de pena.
    ///
    /// Nació el 2026-08-16 con la cara de enojo, y nació porque HACÍA FALTA: hasta entonces los dos
    /// extremos de cada ceja estaban clavados a la misma altura y solo se podía cambiar el arco. Con
    /// arco se hacen la sorpresa, la duda y la pena — el enfado NO. Un enfado son dos cejas rectas
    /// apuntando a la nariz, y eso con un arco no se dibuja: sale una ceja triste, que es otra cosa.
    let browTilt: CGFloat

    /// LA ONDA DE LA BOCA: sube un lado y baja el otro, convirtiendo la curva en una eñe tumbada.
    ///
    /// Misma historia. Los dos puntos de control de la boca iban a la misma altura, así que la boca
    /// solo podía sonreír, torcerse o quedarse recta. La mueca ondulada —que es la que hace que un
    /// enfado se lea como un berrinche y no como una amenaza— necesita que un control suba mientras
    /// el otro baja. Es lo que separa «divertido» de «de verdad».
    let mouthWave: CGFloat

    /// LA LENGUA ASOMADA POR LA COMISURA. 0 = ninguna, 1 = fuera del todo. **El signo dice por qué
    /// lado**: positivo la saca por la derecha, negativo por la izquierda.
    ///
    /// Ya había una lengua, y no sirve para esto: aquella vive DENTRO de la boca abierta y va
    /// recortada contra ella —es la que se ve al hablar—. Esta asoma por la esquina de una boca
    /// CERRADA, que es el gesto de estar concentrado. Son dos cosas distintas que se llaman igual.
    ///
    /// Que el lado vaya en el signo y no en un campo aparte tiene una consecuencia buena al mezclar:
    /// al pasar de una cara con la lengua a la derecha a otra con la lengua a la izquierda, la
    /// interpolación cruza el cero, así que la lengua se mete y vuelve a salir por el otro lado en
    /// vez de atravesar la boca de lado a lado.
    let lengua: CGFloat

    /// EL GUIÑO, dicho por la cara y no por un gesto. 0 = los dos ojos abiertos. **El signo dice qué
    /// ojo se cierra**: positivo el de la derecha, negativo el de la izquierda.
    ///
    /// Ya existía `guinar()`, y no vale para esto: es un gesto con su propia duración —cierra,
    /// aguanta y abre en 620 ms— así que dentro de una expresión que dura más, el ojo se reabre a
    /// media cara. Puesto en la POSE, el guiño entra y sale con la expresión que lo trae, sin que
    /// nadie tenga que cuadrar dos relojes.
    ///
    /// Y se cierra distinto: el guiño de la pose dibuja el ojo como un ARCO, no como la rayita corta
    /// que deja un parpadeo. Un ojo cerrado de verdad es una curva; acortar la línea vertical vale
    /// para el parpadeo —que dura un suspiro y no se llega a ver— pero no para un guiño que se
    /// queda puesto y hay que mirar.
    let guino: CGFloat

    /// LA BOCA EN «O», Y HUECA. 0 = la boca de siempre; 1 = un aro pequeño y nada más.
    ///
    /// No es la boca abierta que ya existía, y la diferencia no es de tamaño: aquella se dibuja
    /// RELLENA —es la cavidad oscura de cuando habla, con su lengua dentro— y a este tamaño sale un
    /// puntito negro. Esta es un ARO: se traza y se queda hueca, que es lo que se lee como la «o»
    /// del susto o del «uy».
    ///
    /// Al crecer, la línea de la boca se encoge en la misma proporción. Así el gesto es una boca que
    /// SE CIERRA EN O, y no una boca a la que le aparece un círculo al lado.
    let bocaO: CGFloat

    /// LOS DOS OJOS CERRADOS EN ARCO — la cara de contenta. 0 = abiertos, 1 = los dos cerrados.
    ///
    /// Es el hermano simétrico de `guino`, y comparte todo el dibujo con él: el mismo arco, la misma
    /// curva que interpola desde la rayita vertical. Lo único que `guino` no podía hacer es cerrar
    /// LOS DOS, porque lleva el lado en el signo — con un solo número no caben «el derecho», «el
    /// izquierdo» y «los dos». De ahí este segundo.
    ///
    /// Y ahí está la diferencia de lo que significan: un ojo cerrado es complicidad, te la está
    /// haciendo A TI. Los dos cerrados no te miran a nadie — es alegría hacia dentro, la de que algo
    /// te guste de verdad.
    let ojosArco: CGFloat

    let accent: CGColor?

    /// Los dos nuevos van con valor por defecto A PROPÓSITO: así las diez caras que ya existían no
    /// cambian ni un píxel, y siguen escritas exactamente igual que en `FaceControl.cs`.
    init(browL: CGFloat, browR: CGFloat, curveL: CGFloat, curveR: CGFloat,
         eyeOpen: CGFloat, squint: CGFloat, mouthCurve: CGFloat, mouthWidth: CGFloat,
         cornerL: CGFloat, cornerR: CGFloat,
         browTilt: CGFloat = 0, mouthWave: CGFloat = 0, lengua: CGFloat = 0, guino: CGFloat = 0,
         bocaO: CGFloat = 0, ojosArco: CGFloat = 0,
         accent: CGColor?) {
        self.browL = browL; self.browR = browR; self.curveL = curveL; self.curveR = curveR
        self.eyeOpen = eyeOpen; self.squint = squint
        self.mouthCurve = mouthCurve; self.mouthWidth = mouthWidth
        self.cornerL = cornerL; self.cornerR = cornerR
        self.browTilt = browTilt; self.mouthWave = mouthWave; self.lengua = lengua
        self.guino = guino; self.bocaO = bocaO; self.ojosArco = ojosArco
        self.accent = accent
    }
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
        // LA CARA DE ESTARTE OYENDO, y ya no se usa como estado: es la cara a la que mezcla la capa
        // de [`Atencion`](Atencion.swift) mientras hablas. Tres cosas la definen (2026-08-16):
        //
        //  · Las cejas SUBEN Y SE ARQUEAN. Subirlas sin arquearlas es sorpresa; arquearlas sin
        //    subirlas es duda. Atención es las dos a la vez.
        //  · SON DESIGUALES a propósito —la derecha más alta y más curva—, y va con el lado al que
        //    se ladea la cabeza. Dos cejas idénticas dan la cara de la foto del carnet: correcta y
        //    de nadie. La asimetría es lo que la vuelve un gesto.
        //  · Los ojos NO van como platos. Estaban a 1.00, que es la cara de un susto; el gesto ya lo
        //    llevan el ladeo y las cejas, y unos ojos abiertos del todo encima de eso pasan de
        //    «te escucho» a «me alarmaste».
        // PENSANDO, con la lengua fuera por la comisura: la cara de quien está concentrado en algo
        // y se le olvida la boca. Es el gesto del niño que dibuja con cuidado, y funciona porque es
        // INVOLUNTARIO — nadie saca la lengua a propósito, así que verla es ver que de verdad está
        // metida en el asunto.
        //
        // Las cejas ARQUEADAS Y ALTAS, no fruncidas. Fruncir es esfuerzo con disgusto, la cara de
        // pelearse con el problema. Arquear es interés. Ü no sufre pensando.
        //
        // Sin tinte, por lo mismo que «trabajando»: es un estado en el que puede pasar rato.
        // La comisura de la lengua CAE y la otra sube: la boca se descuelga por el lado por donde
        // se escapa, que es lo que hace que la lengua parezca que se salió sola y no que está puesta.
        .pensando:    .init(browL: 4.5, browR: 5.5, curveL: 0.50, curveR: 0.58, eyeOpen: 0.85, squint: 0.15, mouthCurve: 0.45, mouthWidth: 34 * 0.90, cornerL: 0.28, cornerR: 0.02,
                            mouthWave: 0.18, lengua: 1.0, accent: nil),
        .escuchando:  .init(browL:  7, browR: 8.5, curveL: 0.48, curveR: 0.62, eyeOpen: 0.95, squint: 0.08, mouthCurve: 0.65, mouthWidth: 34 * 1.05, cornerL: 0.35, cornerR: 0.42, accent: nil),
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
        // LO CONSIGUIÓ. Cejas altas y muy arqueadas, ojos entornados de gusto y la boca lo más ancha
        // que llega. Los ojos ENTORNADOS y no abiertos: una sonrisa grande con los ojos como platos
        // es una mueca: la alegría de verdad cierra los ojos, y por eso `squint` sube aquí en vez de
        // bajar. Es la misma razón por la que la risa lleva 0,50.
        //
        // Simétrica del todo, y a propósito. Casi todas las caras de aquí son desiguales porque la
        // asimetría las vuelve un gesto en vez de un icono — pero la asimetría también añade ironía,
        // y esta es de las pocas que no la quiere. Terminar algo bien se celebra de frente.
        //
        // Sin tinte: quien lo pidió lo está mirando, y el color se guarda para lo que interrumpe.
        .logrado:     .init(browL: 5.5, browR: 5.5, curveL: 0.60, curveR: 0.60, eyeOpen: 0.78, squint: 0.32, mouthCurve: 1.00, mouthWidth: 34 * 1.28, cornerL: 0.55, cornerR: 0.55, accent: nil),

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
