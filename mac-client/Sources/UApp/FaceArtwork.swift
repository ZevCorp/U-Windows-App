import SwiftUI

/// Geometry and poses ported from windows-client/src/Ui/FaceControl.cs (150-unit viewBox).
struct FaceArtwork: View {
    let mode: AppModel.Mode
    var dark = false
    var blink = false
    var eyeShift = 0.0
    var mouthOpen = 0.0
    var mouthRound = 0.0

    private var pose: [Double] {
        switch mode {
        case .ready: return [2, 2.5, 0.3, 0.4, 0.85, 0.15, 0.7, 37.4, 0.3, 0.5]
        case .listening: return [3, 3.5, 0.3, 0.4, 0.90, 0.12, 0.7, 37.4, 0.3, 0.45]
        case .working: return [-1, 4, 0.1, 0.5, 0.75, 0.20, 0.7, 32.3, 0.2, 0.1]
        case .question: return [6, -1, 0.45, 0.15, 0.9, 0.10, 0.2, 32.3, 0.4, 0.1]
        case .speaking: return [2, 2.5, 0.3, 0.4, 0.85, 0.15, 0.9, 42.5, 0.4, 0.4]
        case .error: return [-3, -3, 0.15, 0.15, 0.8, 0.15, -0.5, 30.6, 0.1, 0.1]
        }
    }
    private static func squircle(radius: Double) -> Path {
        Path { path in
            for i in 0...72 {
                let t = 2 * Double.pi * Double(i) / 72, c = cos(t), s = sin(t)
                let point = CGPoint(x: radius * (c < 0 ? -1 : 1) * sqrt(abs(c)), y: radius * (s < 0 ? -1 : 1) * sqrt(abs(s)))
                if i == 0 { path.move(to: point) } else { path.addLine(to: point) }
            }
            path.closeSubpath()
        }
    }
    private static let outer = squircle(radius: 74)
    private static let inner = squircle(radius: 74 - 0.35 * 1.5)
    var body: some View {
        Canvas { context, size in
            let scale = min(size.width, size.height) / 150
            context.translateBy(x: size.width / 2, y: size.height / 2)
            context.scaleBy(x: scale, y: scale)
            let accent: [Double]? = mode == .error ? [255, 59, 48] : mode == .question ? [255, 165, 31] : nil
            func color(_ rgb: [Double]) -> Color { Color(red: rgb[0] / 255, green: rgb[1] / 255, blue: rgb[2] / 255) }
            func fill(_ base: Double) -> Color {
                guard let accent else { return color([base, base, base]) }
                let weight = dark ? 0.20 : 0.14
                return color(accent.map { floor(base + ($0 - base) * weight) })
            }
            let ink = accent.map(color) ?? (dark ? .white : .black)
            context.fill(Self.outer, with: .linearGradient(Gradient(colors: [fill(dark ? 26 : 255), fill(dark ? 0 : 255)]), startPoint: CGPoint(x: -74, y: -74), endPoint: CGPoint(x: 74, y: 74)))
            context.stroke(dark ? Self.inner : Self.outer, with: .color(dark ? ink : .black.opacity(31.0 / 255)), lineWidth: dark ? 0.35 : 1.5)
            context.rotate(by: .degrees(-2))
            let p = pose, stroke = StrokeStyle(lineWidth: 4, lineCap: .round, lineJoin: .round)
            for (x, height, curve) in [(-30.0, p[0], p[2]), (30.0, p[1], p[3])] {
                var brow = Path(); brow.move(to: CGPoint(x: x - 10, y: -34 - height))
                brow.addQuadCurve(to: CGPoint(x: x + 10, y: -34 - height), control: CGPoint(x: x, y: -34 - height - curve * 15))
                context.stroke(brow, with: .color(ink), style: stroke)
            }
            let length = 25 * p[4] * (1 - p[5] * 0.4) * (blink ? 0.08 : 1)
            for x in [-30.0, 30.0] {
                var eye = Path(); eye.move(to: CGPoint(x: x + eyeShift, y: -14 - length / 2))
                eye.addLine(to: CGPoint(x: x + eyeShift, y: -14 + length / 2))
                context.stroke(eye, with: .color(ink), style: stroke)
            }
            let left = 34 - p[6] * 15 - p[8] * 8, right = 34 - p[6] * 15 - p[9] * 8
            let mid = 34 - p[6] * 12, shift = (p[9] - p[8]) * 10, half = p[7] / 2
            var mouth = Path()
            if mouthOpen <= 0.02 {
                mouth.move(to: CGPoint(x: -half, y: left))
                mouth.addCurve(to: CGPoint(x: half, y: right), control1: CGPoint(x: -half * 0.3 + shift, y: mid), control2: CGPoint(x: half * 0.3 + shift, y: mid))
                context.stroke(mouth, with: .color(ink), style: stroke)
            } else {
                let round = mouthRound, open = mouthOpen, h = 3 + open * 20 * (0.75 + round * 0.45), w = half * (1 - round * 0.58)
                let l = left - open * 2, r = right - open * 2, m = mid - open * 1.5, center = (l + r) / 2
                func mix(_ a: Double, _ b: Double) -> Double { a + (b - a) * round }
                let top = mix(m, center - h * 0.45), bottom = mix(m + h, center + h * 0.55)
                let upper = w * mix(0.30, 0.62), lower = w * mix(0.45, 0.78)
                mouth.move(to: CGPoint(x: -w, y: l))
                mouth.addCurve(to: CGPoint(x: w, y: r), control1: CGPoint(x: -upper + shift, y: top), control2: CGPoint(x: upper + shift, y: top))
                mouth.addCurve(to: CGPoint(x: -w, y: l), control1: CGPoint(x: lower, y: bottom), control2: CGPoint(x: -lower, y: bottom))
                mouth.closeSubpath(); context.fill(mouth, with: .color(ink))
                if open > 0.35 {
                    let floorY = (l + r) / 8 + bottom * 0.75, rx = w * 0.42, ry = h * 0.20
                    context.clip(to: mouth)
                    context.fill(Path(ellipseIn: CGRect(x: shift * 0.4 - rx, y: floorY - ry * 1.25, width: rx * 2, height: ry * 2)), with: .color(color([255, 154, 165])))
                }
            }
        }
    }
}
