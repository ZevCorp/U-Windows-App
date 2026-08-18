import CoreGraphics
import Foundation

/// LA ATENCIÓN: la cabeza que se ladea porque te está oyendo A TI, ahora mismo.
///
/// Es el gesto que hace un perro cuando le hablas, y una persona cuando le interesa lo que oye. No
/// dice «tengo el micrófono abierto» —eso lo está siempre y no es noticia—: dice **«esto que estás
/// diciendo va para mí y lo estoy siguiendo»**. Sin él, mientras hablas, la carita se queda con la
/// misma cara que tenía cuando no había nadie, y hablarle se parece demasiado a hablarle a una
/// pared que resulta que contesta.
///
/// **ES UNA CAPA, NO UN ESTADO**, y es la misma decisión que ya se tomó con los `Talante`: se pinta
/// encima de lo que estuviera haciendo y se va sola. Ü no deja de conversar para escucharte, te
/// escucha MIENTRAS conversa. Meterlo como `FaceMood` habría obligado a acordarse de restaurar el
/// estado anterior en cada camino de salida — y hay cinco.
///
/// Los tres tiempos:
///
///   1. **Entra** (420 ms). El ladeo va de 0° a `ladeo`, y con él suben las cejas. Rápido, porque
///      una atención que tarda medio segundo en aparecer llega cuando ya dijiste la frase.
///   2. **Aguanta** (lo que dures hablando). Ladeada, pero NO quieta: `deriva` le da un vaivén de
///      menos de un grado. Una cabeza ladeada absolutamente inmóvil se lee como una imagen pegada,
///      no como alguien escuchando — es el mismo hallazgo que hizo quitar la quietud de la disculpa.
///   3. **Sale** (340 ms). Vuelve a 0° cuando terminas de hablar. Más lenta que la entrada: soltar
///      la cara de golpe parece un corte de montaje.
enum Atencion {

    /// Cuánto ladea la cabeza, en grados, con la atención entera.
    ///
    /// Nueve y no cuatro: por debajo de unos seis grados el ladeo se confunde con el balanceo de
    /// «trabajando» (que va a ±3°) y deja de leerse como un gesto a propósito. Y no veinte, porque
    /// a partir de ahí ya no es atención, es extrañeza.
    static let ladeo: CGFloat = 9

    /// A qué lado. Cambiar el signo la ladea al otro — es lo único que hay que tocar si se prefiere.
    static let sentido: CGFloat = -1

    static let entra: TimeInterval = 0.420
    static let sale: TimeInterval  = 0.340

    /// Sale disparada y frena al llegar: así el ladeo parece que lo decide ella, no que lo interpola
    /// un reloj.
    static let curvaEntra = KeySpline(0.20, 0.90, 0.28, 1.0)
    static let curvaSale  = KeySpline(0.35, 0.00, 0.25, 1.0)

    /// El vaivén del sostén: amplitud en grados y ciclo completo en segundos.
    ///
    /// Menos de un grado y más de cinco segundos a propósito. Tiene que notarse que respira sin que
    /// se note que hay una animación: en cuanto se ve el bucle, deja de ser una cabeza y vuelve a
    /// ser un GIF.
    static let deriva: CGFloat = 0.9
    static let cicloDeriva: CGFloat = 5.2

    /// Congela la atención en un punto fijo (0…1) para poder mirarla quieta mientras se afina:
    ///
    ///     U_ATENCION=0.6 ./U.app/Contents/MacOS/U
    ///
    /// Hermano de `U_CARA`, y por el mismo motivo: un gesto que dura 420 ms no se ajusta a ojo
    /// viéndolo pasar.
    /// `let` y no `var`: esto se consulta EN CADA CUADRO, y una lectura del entorno 60 veces por
    /// segundo es justo el tipo de coste por iteración que este repo ya pagó una vez.
    static let congelada: CGFloat? = {
        guard let v = ProcessInfo.processInfo.environment["U_ATENCION"], let n = Double(v) else { return nil }
        return CGFloat(min(1, max(0, n)))
    }()
}
