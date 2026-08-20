import AppKit

/// EL INTERRUPTOR: un botón en la barra de menús para encender y apagar a Ü.
///
/// Hasta hoy apagarla era un doble toque sobre la carita. Eso tiene dos problemas que no se arreglan
/// explicando mejor el gesto:
///
/// 1. **No se ve.** Un gesto que hay que saber no es un botón; es un secreto. El 2026-08-19 quedó en
///    el registro cómo se usa cuando no se sabe: `👆 doble toque` apagándola, y dos segundos después
///    otro encendiéndola — tanteando, no decidiendo.
/// 2. **La carita se puede arrastrar fuera de vista, o taparse.** Si el único interruptor vive encima
///    de ella, quedarse sin interruptor es cuestión de mover una ventana.
///
/// La barra de menús no se tapa nunca y el icono dice el estado sin abrir nada: **Ü** cuando te oye,
/// **Ü̶** tachada cuando está apagada. Un botón que no dice en qué estado está obliga a probarlo para
/// saberlo, y probar un interruptor de micrófono es justo lo que uno no quiere hacer a ciegas.
final class Interruptor: NSObject {

    private let item: NSStatusItem
    private weak var delegado: Delegado?

    /// Qué hay detrás del botón. Se le pasa cerrado para que el interruptor no sepa de oídos ni de
    /// sesiones: solo enciende, apaga y pregunta cómo está.
    struct Cables {
        let encendida: () -> Bool
        let alternar: () -> Void
        let vivaAhora: () -> Bool
        let colgarLaVoz: () -> Void
        let mostrarLaCarita: () -> Void
    }

    private let cables: Cables

    init(cables: Cables) {
        self.cables = cables
        item = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        super.init()

        let menu = NSMenu()
        menu.addItem(NSMenuItem(title: "", action: nil, keyEquivalent: ""))   // 0: el estado, se reescribe
        menu.addItem(.separator())
        menu.addItem(NSMenuItem(title: "", action: nil, keyEquivalent: ""))   // 2: la voz en vivo
        menu.addItem(.separator())
        menu.addItem(NSMenuItem(title: "Traer la carita al frente", action: nil, keyEquivalent: ""))
        menu.addItem(.separator())
        menu.addItem(NSMenuItem(title: "Salir de Ü", action: nil, keyEquivalent: "q"))
        for it in menu.items { it.target = self }
        menu.items[0].action = #selector(alternar)
        menu.items[2].action = #selector(colgar)
        menu.items[4].action = #selector(alFrente)
        menu.items[5].action = #selector(salir)
        menu.delegate = self
        item.menu = menu

        pintar()
        // QUE DEJE RASTRO DE QUE EXISTE. Un `NSStatusItem` que no se crea —barra llena, o la app sin
        // sesión de ventanas— falla en silencio y desde fuera es idéntico a «no lo pusieron».
        Registro.di(item.button == nil
            ? "🔘 ✘ no pude poner el botón en la barra de menús"
            : "🔘 botón en la barra de menús: puesto")
    }

    /// El icono Y su descripción para VoiceOver. Un botón de micrófono que solo se distingue por un
    /// dibujo deja fuera a quien no lo ve.
    func pintar() {
        guard let boton = item.button else { return }
        let oyendo = cables.encendida()
        boton.title = oyendo ? "Ü" : "Ü̶"
        boton.alphaValue = oyendo ? 1.0 : 0.45
        boton.toolTip = oyendo ? "Ü te está oyendo — clic para apagarla"
                               : "Ü está apagada — clic para encenderla"
        boton.setAccessibilityLabel(oyendo ? "Ü encendida, te está oyendo" : "Ü apagada")
    }

    @objc private func alternar() {
        Registro.di("🔘 me tocaste el botón de la barra")
        cables.alternar(); pintar()
    }
    @objc private func colgar()   { cables.colgarLaVoz(); pintar() }
    @objc private func alFrente() { cables.mostrarLaCarita() }
    @objc private func salir()    { NSApplication.shared.terminate(nil) }
}

extension Interruptor: NSMenuDelegate {
    /// Los títulos se escriben al ABRIR el menú, no al construirlo: el menú se construye una vez y el
    /// estado cambia todo el rato. Un menú con el estado congelado miente cada vez que se abre.
    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.items[0].title = cables.encendida() ? "🎙 Te está oyendo — apagar"
                                                 : "🔇 Está apagada — encender"
        let viva = cables.vivaAhora()
        menu.items[2].title = viva ? "⚡︎ Hablando en vivo — colgar y dejar de gastar"
                                   : "⚡︎ La voz en vivo está cerrada"
        menu.items[2].isEnabled = viva
        pintar()
    }
}
