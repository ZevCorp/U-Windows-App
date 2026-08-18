import CoreGraphics
import Foundation

/// LA CELEBRACIÓN: lo consiguió, y se le nota en el cuerpo.
///
/// Es la hermana de [`Disculpa`](Disculpa.swift) y está hecha con la misma idea: lo que se siente
/// no se enseña con una pose, se enseña con una SECUENCIA. Una carita sonriendo quieta después de
/// terminar algo no celebra nada — es la misma cara de siempre con la boca más ancha. Lo que se lee
/// como celebrar es el movimiento.
///
/// **Y por eso esta es la primera que mueve la ventana.** Todo lo demás —el ladeo de la atención,
/// el enfado, la lengua— pasa dentro del dibujo. Aquí la carita BRINCA de lado a lado, porque la
/// alegría que no despega los pies no es alegría. El desplazamiento y el ladeo se calculan en el
/// mismo cuadro a propósito: llevados por relojes distintos se separan unos milisegundos y se ven
/// como dos animaciones sueltas en vez de como un cuerpo.
///
/// Los cinco tiempos, poco más de un segundo y medio:
///
///   1. **Se agacha** (0–220 ms). Baja un pelo antes de saltar. Es la anticipación de toda la vida:
///      sin ella el primer brinco parece que la empujaron, con ella parece que saltó.
///   2. **Brinco a la derecha** (220–640 ms). Va, sube en arco y se ladea HACIA DONDE VA. Ladearse
///      hacia el lado contrario sería frenar; ladearse hacia donde vas es lanzarte.
///   3. **Brinco a la izquierda** (640–1060 ms). El grande de vuelta, cruzando entero.
///   4. **Brinco corto a la derecha** (1060–1380 ms). Más pequeño: lo que hace que parezca que se
///      va apagando sola y no que alguien paró la música.
///   5. **Se asienta** (1380–1550 ms). Vuelve al sitio exacto del que salió.
///
/// Ese «exacto» del final no es un detalle: la carita vive donde el usuario la dejó, y una
/// celebración que la deposita seis puntos más allá se la va moviendo por la pantalla cada vez que
/// termina algo.
enum Celebracion {

    /// Lo que se le pide a la ventana en este instante.
    struct Paso {
        /// Desplazamiento respecto al sitio del que salió, en puntos. Positivo = a la derecha.
        var dx: CGFloat = 0
        /// Cuánto ha despegado. Positivo = arriba.
        var dy: CGFloat = 0
        /// Ladeo en grados.
        var ladeo: CGFloat = 0
        var terminada = false
    }

    static let dura: TimeInterval = 1.550

    /// Cuánto se aparta a cada lado. Dieciséis puntos sobre una carita de 104 se ven de sobra sin
    /// que parezca que se escapa: la carita vive pegada a un borde, y un brinco de cuarenta la
    /// sacaría de la pantalla la mitad de las veces.
    static let alcance: CGFloat = 16
    /// Cuánto despega en el arco.
    static let altura: CGFloat = 14
    /// Cuánto se ladea en lo alto del brinco.
    static let ladeo: CGFloat = 9

    private static let suave = KeySpline(0.42, 0, 0.58, 1)

    /// El arco del salto: sube y baja en el mismo tramo. Un seno y no dos rectas — un brinco
    /// triangular se lee como un pitido, no como un cuerpo con peso.
    private static func arco(_ u: CGFloat) -> CGFloat { sin(.pi * Easing.clamp01(u)) }

    static func en(_ t: TimeInterval) -> Paso {
        var p = Paso()

        // 1 · Se agacha para tomar impulso.
        if t < 0.220 {
            let u = CGFloat(t / 0.220)
            p.dy = -4 * sin(.pi * u)
            return p
        }

        // 2 · Brinco grande a la derecha.
        if t < 0.640 {
            let u = CGFloat((t - 0.220) / 0.420), e = suave.eval(u)
            p.dx = alcance * e
            p.dy = altura * arco(u)
            p.ladeo = ladeo * e
            return p
        }

        // 3 · Brinco grande a la izquierda, cruzando entero.
        if t < 1.060 {
            let u = CGFloat((t - 0.640) / 0.420), e = suave.eval(u)
            p.dx = alcance - 2 * alcance * e
            p.dy = altura * arco(u)
            p.ladeo = ladeo - 2 * ladeo * e
            return p
        }

        // 4 · Uno corto de vuelta: se va apagando sola.
        if t < 1.380 {
            let u = CGFloat((t - 1.060) / 0.320), e = suave.eval(u)
            p.dx = -alcance + (alcance * 1.4) * e
            p.dy = (altura * 0.62) * arco(u)
            p.ladeo = -ladeo + (ladeo * 1.45) * e
            return p
        }

        // 5 · Se asienta EXACTAMENTE donde estaba.
        if t < dura {
            let u = CGFloat((t - 1.380) / 0.170), e = suave.eval(u)
            p.dx = (alcance * 0.40) * (1 - e)
            p.dy = (altura * 0.22) * arco(u)
            p.ladeo = (ladeo * 0.45) * (1 - e)
            return p
        }

        p.terminada = true
        return p
    }

    /// Congela la celebración en un instante fijo (0…1 del total) para poder mirarla quieta:
    ///
    ///     U_CARA=logrado U_CELEBRA=0.45 ./U.app/Contents/MacOS/U
    static let congelada: TimeInterval? = {
        guard let v = ProcessInfo.processInfo.environment["U_CELEBRA"], let n = Double(v) else { return nil }
        return dura * min(0.999, max(0, n))
    }()
}
