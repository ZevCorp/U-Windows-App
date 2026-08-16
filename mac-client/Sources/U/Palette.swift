import AppKit

/// Los MISMOS valores que `windows-client/src/Ui/UiPalette.cs`, que a su vez vienen de
/// `Palette.kt` de la app Android. Si aquí se separan, la carita deja de ser la misma carita.
enum UiPalette {
    static let vivo       = rgb(0x2F, 0xB4, 0x57)
    static let trabajando = rgb(0x3B, 0x82, 0xF6)
    static let atencion   = rgb(0xFF, 0xA5, 0x1F)
    /// Fallo — y también grabando. El mismo rojo que llevan ⏹ y 🎓 mientras enseñas.
    static let fallo      = rgb(0xFF, 0x3B, 0x30)
    static let inactivo   = rgb(0x8A, 0x8A, 0x8E)
    static let lengua     = rgb(0xFF, 0x9A, 0xA5)

    static func rgb(_ r: Int, _ g: Int, _ b: Int) -> CGColor {
        CGColor(srgbRed: CGFloat(r)/255, green: CGFloat(g)/255, blue: CGFloat(b)/255, alpha: 1)
    }

    /// Mezcla lineal por canal, igual que `UiPalette.Blend`.
    static func blend(_ a: CGColor, _ b: CGColor, _ t: CGFloat) -> CGColor {
        let ca = a.components ?? [0, 0, 0, 1]
        let cb = b.components ?? [0, 0, 0, 1]
        return CGColor(srgbRed: ca[0] + (cb[0] - ca[0]) * t,
                       green:   ca[1] + (cb[1] - ca[1]) * t,
                       blue:    ca[2] + (cb[2] - ca[2]) * t,
                       alpha:   1)
    }
}
