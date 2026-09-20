import AppKit
import ScreenCaptureKit
import UCore

public enum ScreenCapture {
    public static var allowed: Bool { CGPreflightScreenCaptureAccess() }
    public static func requestPermission() {
        _ = CGRequestScreenCaptureAccess()
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture") {
            NSWorkspace.shared.open(url)
        }
    }
    public static func display(for window: CGRect?) -> CGDirectDisplayID {
        var ids = [CGDirectDisplayID](repeating: 0, count: 16)
        var count: UInt32 = 0
        CGGetActiveDisplayList(16, &ids, &count)
        guard let window else { return CGMainDisplayID() }
        let point = CGPoint(x: window.midX, y: window.midY)
        return ids.prefix(Int(count)).first { CGDisplayBounds($0).contains(point) } ?? CGMainDisplayID()
    }
    public static func geometry(_ id: CGDirectDisplayID) -> ScreenGeometry {
        let rect = CGDisplayBounds(id)
        return ScreenGeometry(originX: rect.minX, originY: rect.minY, width: rect.width, height: rect.height)
    }
    public static func png(displayID: CGDirectDisplayID) async throws -> String {
        guard allowed else { throw AgentError.permission("Grabación de pantalla") }
        let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)
        guard let display = content.displays.first(where: { $0.displayID == displayID }) else { throw AgentError.unavailable("La pantalla observada se desconectó.") }
        let own = content.applications.filter { $0.processID == getpid() }
        let filter = SCContentFilter(display: display, excludingApplications: own, exceptingWindows: [])
        let config = SCStreamConfiguration()
        let size = geometry(displayID)
        // Exactly one image pixel per Quartz point, independent of Retina scale.
        config.width = Int(size.width); config.height = Int(size.height)
        config.showsCursor = true
        let image = try await SCScreenshotManager.captureImage(contentFilter: filter, configuration: config)
        try Task.checkCancellation()
        guard let png = NSBitmapImageRep(cgImage: image).representation(using: .png, properties: [:]) else { throw AgentError.unavailable("No pude codificar la captura.") }
        return png.base64EncodedString()
    }
}
