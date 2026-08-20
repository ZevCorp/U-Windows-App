import AppKit

/// Ü se retrata a sí misma: un PNG por cara, en `~/.u/caras/`.
///
/// **No es una captura de pantalla y esa es toda la gracia.** Capturar la ventana exigiría permiso de
/// grabación de pantalla, saber dónde está la carita —que se arrastra— y acertarle al instante exacto
/// de una animación que dura medio segundo. Aquí es la vista la que se dibuja en un mapa de bits: sale
/// recortada, con fondo transparente, del tamaño exacto, y siempre igual.
///
/// Sirve para lo que costó descubrir a mano: **ver las caras juntas para decidir cuál cambiar.** Con
/// `U_TALANTE` se mira una por arranque; aquí salen las veintiséis de una vez.
///
///     open --env U_RETRATOS=1 --env U_PESO=1 "…/U.app"
///
/// `U_PESO=1` no es opcional: sin él, cada reacción se está yendo mientras se la retrata y la mitad
/// salen a medio camino de reposo.
enum Retratos {

    static var pedidos: Bool { ProcessInfo.processInfo.environment["U_RETRATOS"] == "1" }

    static var carpeta: URL {
        FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent(".u/caras")
    }

    /// Retrata todo y avisa al terminar. Va en cadena y con esperas porque las caras se ANIMAN: hay
    /// que darle a cada una el tiempo que tarda en llegar, o se retrata la transición.
    static func tomarTodos(_ cara: FaceView, delegado: Delegado, alTerminar: @escaping () -> Void) {
        try? FileManager.default.createDirectory(at: carpeta, withIntermediateDirectories: true)
        if Reaccion.congelada == nil {
            Registro.di("📸 ⚠︎ sin U_PESO=1 las reacciones se retratan a medio camino")
        }
        Registro.di("📸 retratando en \(carpeta.path)")

        // Los estados primero, en los dos temas: la carita se usa en claro y en oscuro, y hay caras
        // que solo se leen mal en uno de los dos.
        var trabajos: [(String, () -> Void)] = []
        for tema in [FaceTheme.dark, FaceTheme.light] {
            let sufijo = tema == .dark ? "oscuro" : "claro"
            for m in FaceMood.allCases {
                trabajos.append(("estado-\(m.rawValue)-\(sufijo)", {
                    cara.theme = tema
                    delegado.caraForzada = m
                }))
            }
            for t in Talante.allCases {
                trabajos.append(("talante-\(t.rawValue)-\(sufijo)", {
                    cara.theme = tema
                    delegado.caraForzada = .reposo
                    cara.reaccionar(t)
                }))
            }
        }

        var i = 0
        func siguiente() {
            guard i < trabajos.count else {
                Registro.di("📸 listo: \(trabajos.count) retratos en \(carpeta.path)")
                alTerminar()
                return
            }
            let (nombre, poner) = trabajos[i]
            i += 1
            poner()
            // 0,9 s: más que la más lenta en llegar (el enfado tarda 0,85) y que la transición de
            // estado más lenta (pensando, 0,70).
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.9) {
                guardar(cara, como: nombre)
                siguiente()
            }
        }
        siguiente()
    }

    /// La vista, dibujada en un PNG con transparencia. A 2× para que se vea en pantalla Retina.
    private static func guardar(_ cara: FaceView, como nombre: String) {
        let lado = cara.bounds
        guard let rep = cara.bitmapImageRepForCachingDisplay(in: lado) else {
            Registro.di("📸 ✘ no pude preparar el mapa de bits de «\(nombre)»")
            return
        }
        rep.size = lado.size
        cara.cacheDisplay(in: lado, to: rep)
        guard let datos = rep.representation(using: .png, properties: [:]) else {
            Registro.di("📸 ✘ no pude codificar «\(nombre)»")
            return
        }
        let destino = carpeta.appendingPathComponent("\(nombre).png")
        do { try datos.write(to: destino) } catch {
            Registro.di("📸 ✘ no pude escribir «\(nombre)»: \(error.localizedDescription)")
        }
    }
}
