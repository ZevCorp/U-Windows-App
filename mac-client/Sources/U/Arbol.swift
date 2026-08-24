import ApplicationServices
import AppKit
import Foundation

/// EL ÁRBOL: lee la interfaz de la app en primer plano por accesibilidad (`AXUIElement`), el
/// equivalente Mac de `UiaReader.cs` en Windows.
///
/// Medido el 2026-08-21, en esta misma máquina, contra tres apps reales — no en teoría:
///
///     Calculadora (Apple, nativa)  → título, valor y descripción VACÍOS en los dígitos. Ni el
///                                    caso más favorable posible se deja leer por etiqueta.
///     Google.com en Safari         → títulos perfectos: «Gmail», «Voy a tener suerte».
///     WhatsApp                     → título vacío, pero la DESCRIPCIÓN sí trae el nombre real.
///
/// Tres apps, tres sitios distintos donde vive la etiqueta (o ninguno). Por eso `mejorEtiqueta`
/// prueba varios atributos en orden en vez de confiar en uno solo — y por eso, cuando ninguno
/// contesta, lo único que queda es la POSICIÓN: se devuelve igual, nunca se descarta el elemento.
enum Arbol {

    struct Elemento {
        let rol: String
        let etiqueta: String?      // nil si NINGÚN atributo trajo texto — ver Calculadora arriba
        let centro: CGPoint        // en coordenadas de PANTALLA, listas para `Entrada.clic`
    }

    /// Los botones (y campos) de la ventana principal de la app en primer plano. `nil` si no se pudo
    /// leer nada — sin permiso, o la app no tiene ventana, o no expone accesibilidad en absoluto.
    static func botonesDeLaAppEnFrente() -> [Elemento]? {
        guard let app = NSWorkspace.shared.frontmostApplication else { return nil }
        let raiz = AXUIElementCreateApplication(app.processIdentifier)

        guard let ventanas = atributo(raiz, kAXWindowsAttribute) as? [AXUIElement],
              let ventana = ventanas.first
        else {
            Registro.di("🌳 ✘ no encontré ventanas de \(app.localizedName ?? "?") por accesibilidad")
            return nil
        }

        var encontrados: [Elemento] = []
        recorrer(ventana, en: &encontrados)
        return encontrados
    }

    /// Recorrido en profundidad. Sin tope explícito de profundidad porque los árboles de UI reales
    /// —incluida Calculadora, con sus 54 botones planos— no anidan tanto como para desbordar la pila;
    /// si algún día una app lo hace, se verá como un cuelgue y se le pone tope entonces, no antes.
    private static func recorrer(_ el: AXUIElement, en encontrados: inout [Elemento]) {
        if let rol = atributo(el, kAXRoleAttribute) as? String,
           rol == kAXButtonRole as String || rol == kAXTextFieldRole as String,
           let centro = centroDe(el) {
            encontrados.append(Elemento(rol: rol, etiqueta: mejorEtiqueta(el), centro: centro))
        }
        guard let hijos = atributo(el, kAXChildrenAttribute) as? [AXUIElement] else { return }
        for hijo in hijos { recorrer(hijo, en: &encontrados) }
    }

    /// Todo el texto estático visible (visores, resultados, etiquetas de solo lectura) de la app en
    /// primer plano. Es cómo se VERIFICA una acción sin creerle a una foto: se lee el dato real, no
    /// se le pregunta a un modelo de visión qué cree que ve.
    static func textosDeLaAppEnFrente() -> [String] {
        guard let app = NSWorkspace.shared.frontmostApplication else { return [] }
        let raiz = AXUIElementCreateApplication(app.processIdentifier)
        guard let ventanas = atributo(raiz, kAXWindowsAttribute) as? [AXUIElement],
              let ventana = ventanas.first
        else { return [] }
        var textos: [String] = []
        recorrerTextos(ventana, en: &textos)
        return textos
    }

    private static func recorrerTextos(_ el: AXUIElement, en textos: inout [String]) {
        if let rol = atributo(el, kAXRoleAttribute) as? String, rol == "AXStaticText",
           let v = atributo(el, kAXValueAttribute) as? String, !v.isEmpty {
            textos.append(v)
        }
        guard let hijos = atributo(el, kAXChildrenAttribute) as? [AXUIElement] else { return }
        for hijo in hijos { recorrerTextos(hijo, en: &textos) }
    }

    /// EN ESTE ORDEN, y el orden ES el hallazgo de hoy: cada app pone el texto legible en un sitio
    /// distinto, y preguntar solo por `AXTitle` —lo obvio— se queda ciego en WhatsApp y en
    /// Calculadora por igual. `nil` cuando NINGUNO de los cuatro contesta: es una respuesta real, no
    /// un error, y `Elemento.etiqueta` se queda `nil` a propósito para que quien lo use sepa que ahí
    /// no hay nada que leer y tiene que caer a posición (o, más adelante, a una foto recortada).
    private static func mejorEtiqueta(_ el: AXUIElement) -> String? {
        for clave in [kAXTitleAttribute, "AXDescription", kAXValueAttribute, kAXHelpAttribute] {
            if let texto = atributo(el, clave) as? String, !texto.isEmpty { return texto }
        }
        return nil
    }

    private static func centroDe(_ el: AXUIElement) -> CGPoint? {
        guard let posValor = atributo(el, kAXPositionAttribute),
              let tamValor = atributo(el, kAXSizeAttribute)
        else { return nil }
        var pos = CGPoint.zero
        var tam = CGSize.zero
        // swiftlint:disable force_cast — AXValueGetValue exige el tipo exacto por parámetro `AXValueType`;
        // es la única forma que da la API de sacar CGPoint/CGSize de un AXValue.
        guard AXValueGetValue(posValor as! AXValue, .cgPoint, &pos),
              AXValueGetValue(tamValor as! AXValue, .cgSize, &tam)
        else { return nil }
        return CGPoint(x: pos.x + tam.width / 2, y: pos.y + tam.height / 2)
    }

    private static func atributo(_ el: AXUIElement, _ nombre: String) -> AnyObject? {
        var valor: AnyObject?
        let err = AXUIElementCopyAttributeValue(el, nombre as CFString, &valor)
        return err == .success ? valor : nil
    }
}
