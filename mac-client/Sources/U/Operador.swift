import AppKit
import Foundation

/// EL PROTOTIPO ACOTADO DE LA MITAD QUE ACTÚA. Igual de aislado que `Mirar` fue para la mitad que
/// mira: una sola tarea fija, fuera del bucle de voz, para probar el MECANISMO antes de conectarlo a
/// un modelo que decida qué apretar.
///
/// La tarea: abrir Calculadora y sumar 4+8. Y aquí está la parte incómoda que hay que decir clara —
/// medido el 2026-08-21: los botones de dígitos de Calculadora **no tienen ninguna etiqueta**
/// (`Arbol.swift` lo documenta con la medición completa). No hay «buscar el botón llamado 4». Lo
/// único que existe es la POSICIÓN, y por eso este archivo ubica los botones por punto esperado —no
/// por índice de recorrido, que podría no coincidir entre cómo AppleScript enumeró los elementos al
/// medir a mano y cómo los enumera este código—, y SE NIEGA si no encuentra un botón real cerca de
/// donde lo esperaba, en vez de clicar al elemento más parecido y arriesgarse a apretar el botón
/// equivocado.
///
/// Esto NO es la solución general — es un caso especial de una sola app, escrito para probar que el
/// mecanismo (permiso, `AXUIElement`, `CGEvent`, verificar leyendo el visor) funciona de punta a
/// punta. La solución general —recortar y preguntarle al modelo de visión por los botones sin
/// etiqueta, para cualquier app— es el paso siguiente, no este.
enum Operador {

    static var pedido: Bool { ProcessInfo.processInfo.environment["U_SUMAR"] == "1" }

    /// Dónde espero cada botón, en Calculadora modo Científico, con la ventana en la posición de esa
    /// sesión del 2026-08-21. Si moviste la ventana o cambiaste a modo Básico, estos puntos ya no van
    /// a caer cerca de nada — y el código de abajo se niega en ese caso, no adivina.
    ///
    /// El de "8" cuesta contarlo: el primer intento puso 811,877,943 = 7,8,9 por deducción visual de
    /// una captura, y salió mal — apretó «9» en vez de «8». Medido de verdad, columna por columna,
    /// leyendo el visor después de cada clic: 745=7, 811=8, 877=9, 943=×. Ni deducir de una foto ni de
    /// un recorrido de AppleScript sustituye medir la fila entera con el propio mecanismo.
    private static let puntoEsperado: [String: CGPoint] = [
        "AC": CGPoint(x: 811, y: 544),
        "4": CGPoint(x: 745, y: 652),
        "+": CGPoint(x: 943, y: 706),
        "8": CGPoint(x: 811, y: 598),
        "=": CGPoint(x: 943, y: 760),
    ]

    /// Cuánto puede desviarse un botón real de donde lo esperaba y seguir contando como «el mismo».
    /// 15 px cubre el ruido normal de medir a mano con AppleScript; más que eso y es más probable que
    /// sea otro botón, o que la ventana se movió.
    private static let tolerancia: CGFloat = 15

    /// Activa Calculadora y lee sus botones, con reintentos: algo del arranque de Ü —probablemente
    /// el aviso del sistema por el micrófono— le roba el primer plano a Calculadora por un instante
    /// justo después de activarla, y una sola lectura cae en ese hueco. Reintentar resuelve lo que un
    /// respiro más largo no resolvía.
    private static func abrirYLeerBotones() async -> [Arbol.Elemento]? {
        for intento in 1...4 {
            if let corriendo = NSRunningApplication.runningApplications(
                withBundleIdentifier: "com.apple.calculator").first {
                corriendo.activate(options: [.activateIgnoringOtherApps])
            } else {
                NSWorkspace.shared.launchApplication("Calculator")
            }
            let alFrente: () -> Bool = { Frente.appId == "com.apple.calculator" }
            _ = await esperarA(segundos: 5, que: "Calculadora al frente", alFrente)
            try? await Task.sleep(nanoseconds: 500_000_000)
            guard Frente.appId == "com.apple.calculator" else {
                Registro.di("🖐 intento \(intento)/4: al leer, el frente ya no era Calculadora (era \(Frente.descripcion))")
                continue
            }
            if let botones = Arbol.botonesDeLaAppEnFrente(), !botones.isEmpty {
                return botones
            }
            Registro.di("🖐 intento \(intento)/4: Calculadora al frente pero sin botones legibles")
        }
        return nil
    }

    static func correr() async {
        Registro.di("🖐 U_SUMAR=1 · prototipo acotado: abrir Calculadora y sumar 4+8")

        guard Entrada.permisoConcedido else {
            Registro.di("🖐 ✘ sin permiso de Accesibilidad — pidiéndolo ahora")
            Registro.di("🖐 ✋ si Ü ya estaba en la lista de Ajustes, quítala primero: la firma ad-hoc"
                + " cambia en cada build y deja el permiso viejo atado a un binario que ya no existe"
                + " (la misma trampa de hoy con Grabación de pantalla).")
            Entrada.pedirPermiso()
            return
        }

        guard let botones = await abrirYLeerBotones() else {
            Registro.di("🖐 ✘ no pude leer los botones de Calculadora por accesibilidad, en 4 intentos")
            return
        }
        Registro.di("🖐 leí \(botones.count) botones de Calculadora")

        for tecla in ["AC", "4", "+", "8", "="] {
            guard let esperado = puntoEsperado[tecla] else { continue }
            guard let real = botones.map({ ($0, distancia($0.centro, esperado)) })
                .min(by: { $0.1 < $1.1 }), real.1 <= tolerancia
            else {
                Registro.di("🖐 ✘ no encontré un botón real cerca de donde esperaba «\(tecla)»"
                    + " (\(Int(esperado.x)),\(Int(esperado.y))) — me detengo, no adivino")
                return
            }
            Entrada.clic(en: real.0.centro)
            Registro.di("🖐 apreté «\(tecla)» en (\(Int(real.0.centro.x)),\(Int(real.0.centro.y)))"
                + " · a \(String(format: "%.1f", real.1)) px de lo esperado")
            try? await Task.sleep(nanoseconds: 400_000_000)
        }

        // VERIFICAR LEYENDO, no creyendo. Se lee el visor de vuelta en vez de asumir que la secuencia
        // de clics funcionó — es la misma disciplina que ya probó Arbol.swift: preguntarle al dato
        // real, no a una suposición.
        let textos = Arbol.textosDeLaAppEnFrente()
        let limpios = textos.map { $0.trimmingCharacters(in: CharacterSet(charactersIn: "\u{200e}\u{200f}")) }
        Registro.di("🖐 el visor dice: \(limpios)")
        if limpios.contains(where: { $0.contains("12") }) {
            Registro.di("🖐 ✅ 4 + 8 = 12 — confirmado leyendo el visor, no la foto")
        } else {
            Registro.di("🖐 ✘ el visor NO muestra 12 — algo del mapa de botones está mal")
        }
    }

    private static func distancia(_ a: CGPoint, _ b: CGPoint) -> CGFloat {
        sqrt(pow(a.x - b.x, 2) + pow(a.y - b.y, 2))
    }

    /// Como `esperarA` en el contrato, pero para `async`: espera a que una condición sea cierta, con
    /// tope, sin bloquear el hilo.
    private static func esperarA(segundos: Double, que: String, _ condicion: () -> Bool) async -> Bool {
        let limite = Date().addingTimeInterval(segundos)
        while Date() < limite {
            if condicion() { return true }
            try? await Task.sleep(nanoseconds: 200_000_000)
        }
        return false
    }
}
