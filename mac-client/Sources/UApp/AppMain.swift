import AppKit
import SwiftUI
import UCore
import UMac

final class FloatingPanel: NSPanel {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSWindowDelegate {
    let model = AppModel()
    var face: NSPanel!
    var window: NSWindow!
    var statusItem: NSStatusItem!
    var globalKeys: Any?
    var localKeys: Any?
    var observation: NSObjectProtocol?
    var highlight: NSPanel?
    func applicationDidFinishLaunching(_ notification: Notification) {
        terminateOlderCopies()
        if let index = CommandLine.arguments.firstIndex(of: "--execution-test"), CommandLine.arguments.count > index + 1 {
            Task { await SmokeTest.execution(output: URL(fileURLWithPath: CommandLine.arguments[index + 1])) }
            return
        }
        if let index = CommandLine.arguments.firstIndex(of: "--smoke-test"), CommandLine.arguments.count > index + 1 {
            Task { await SmokeTest.run(output: URL(fileURLWithPath: CommandLine.arguments[index + 1])) }
            return
        }
        if CommandLine.arguments.contains("--diagnose") {
            let permissions = model.permissions.snapshot
            let result: [String: Any] = ["accessibility": permissions.accessibility.isGranted, "screenCapture": permissions.screenCapture.isGranted,
                                       "microphone": permissions.microphone.isGranted, "speech": permissions.speech.isGranted,
                                       "bundleIdentifier": Bundle.main.bundleIdentifier ?? "", "bundlePath": Bundle.main.bundleURL.path,
                                       "graphConfigured": Credentials.read("GRAPH_API_KEY") != nil, "architecture": "native-swift", "version": "0.1.0"]
            if let data = try? JSONSerialization.data(withJSONObject: result, options: [.prettyPrinted, .sortedKeys]) { print(String(decoding: data, as: UTF8.self)) }
            NSApp.terminate(nil); return
        }
        window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 510, height: 630), styleMask: [.titled, .closable, .miniaturizable, .resizable], backing: .buffered, defer: false)
        window.title = "Ü para Mac"; window.isReleasedWhenClosed = false; window.delegate = self
        window.contentView = NSHostingView(rootView: MainView(model: model)); window.center()
        face = FloatingPanel(contentRect: NSRect(x: 0, y: 0, width: 88, height: 88), styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        face.isOpaque = false; face.backgroundColor = .clear; face.hasShadow = false
        face.level = .floating; face.hidesOnDeactivate = false; face.isMovableByWindowBackground = true
        face.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        face.contentView = NSHostingView(rootView: Face(model: model))
        if let frame = NSScreen.main?.visibleFrame { face.setFrameOrigin(NSPoint(x: frame.maxX - 108, y: frame.minY + 95)) }
        face.orderFrontRegardless()
        model.showWindow = { [weak self] in self?.show() }
        model.hideWindow = { [weak self] in self?.window.orderOut(nil) }
        model.desktop.onHighlight = { [weak self] frame in self?.showHighlight(frame); self?.moveFace(beside: frame) }
        model.desktop.onAction = { [weak self] frame in self?.moveFace(beside: frame) }
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        statusItem.button?.title = "Ü"
        let menu = NSMenu()
        for (title, action, key) in [("Abrir Ü", #selector(show), ""), ("Hablar / silenciar", #selector(toggleVoice), ""), ("Detener tarea", #selector(stop), ""), ("Configuración…", #selector(settings), ","), ("Salir de Ü", #selector(quit), "q")] {
            let item = NSMenuItem(title: title, action: action, keyEquivalent: key); item.target = self; menu.addItem(item)
        }
        statusItem.menu = menu
        globalKeys = NSEvent.addGlobalMonitorForEvents(matching: .keyDown) { [weak self] event in
            if event.cgEvent?.getIntegerValueField(.eventSourceUserData) == InputDriver.syntheticEventTag { return }
            if event.keyCode == 53 { Task { @MainActor in self?.model.stop() } }
            if event.keyCode == 49 && event.modifierFlags.contains(.option) { Task { @MainActor in self?.show() } }
        }
        localKeys = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            if event.cgEvent?.getIntegerValueField(.eventSourceUserData) == InputDriver.syntheticEventTag { return event }
            if event.keyCode == 53 { Task { @MainActor in self?.model.stop() } }
            return event
        }
        observation = NSWorkspace.shared.notificationCenter.addObserver(forName: NSWorkspace.didActivateApplicationNotification, object: nil, queue: .main) { [weak self] note in
            guard let app = note.userInfo?[NSWorkspace.applicationUserInfoKey] as? NSRunningApplication, app.processIdentifier != getpid() else { return }
            Task { @MainActor in self?.model.lastExternalApp = app }
        }
        if let app = NSWorkspace.shared.frontmostApplication, app.processIdentifier != getpid() { model.lastExternalApp = app }
        if !model.hasCredential || !model.permissions.snapshot.canControlComputer { model.selectedTab = 1; show() }
    }
    private func terminateOlderCopies() {
        for app in NSWorkspace.shared.runningApplications {
            guard app.processIdentifier != getpid(),
                  app.executableURL?.lastPathComponent == "U",
                  [PermissionCenter.bundleIdentifier, "com.zevcorp.u", "com.zevcorp.u.mac.native"].contains(app.bundleIdentifier)
            else { continue }
            // There must be one process even when `open -n` was used by an old launcher.
            // Prefer a graceful close, then force the stale copy if it ignores the request.
            app.terminate()
            if !app.isTerminated { app.forceTerminate() }
        }
    }
    @objc func show() {
        if let app = NSWorkspace.shared.frontmostApplication, app.processIdentifier != getpid() { model.lastExternalApp = app }
        NSApp.activate(ignoringOtherApps: true); window.makeKeyAndOrderFront(nil); model.refreshPermissions()
    }
    @objc func settings() { model.selectedTab = 1; show() }
    @objc func toggleVoice() { model.toggleMicrophone() }
    @objc func stop() { model.stop() }
    @objc func quit() { NSApp.terminate(nil) }
    func applicationWillTerminate(_ notification: Notification) {
        model.stop()
        if let globalKeys { NSEvent.removeMonitor(globalKeys) }
        if let localKeys { NSEvent.removeMonitor(localKeys) }
        if let observation { NSWorkspace.shared.notificationCenter.removeObserver(observation) }
    }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }
    private func moveFace(beside quartzFrame: CGRect) {
        guard !window.isVisible else { return }
        let primaryHeight = CGDisplayBounds(CGMainDisplayID()).height
        let target = CGRect(x: quartzFrame.minX, y: primaryHeight - quartzFrame.maxY, width: quartzFrame.width, height: quartzFrame.height)
        let screen = NSScreen.screens.first { $0.frame.contains(CGPoint(x: target.midX, y: target.midY)) } ?? NSScreen.main
        guard let bounds = screen?.visibleFrame else { return }
        let size = face.frame.size, gap = 10.0
        var x = target.maxX + gap, y = target.midY - size.height / 2
        if x + size.width > bounds.maxX { x = target.minX - size.width - gap }
        if x < bounds.minX { x = target.midX - size.width / 2; y = target.minY - size.height - gap }
        x = max(bounds.minX, min(x, bounds.maxX - size.width))
        y = max(bounds.minY, min(y, bounds.maxY - size.height))
        model.faceEyeShift = target.midX < x + size.width / 2 ? -3.5 : 3.5
        // Never wait for a visual transition before issuing the AX or CGEvent action.
        face.setFrameOrigin(CGPoint(x: x, y: y))
    }
    func showHighlight(_ quartzFrame: CGRect) {
        highlight?.close()
        let primaryHeight = CGDisplayBounds(CGMainDisplayID()).height
        let frame = CGRect(x: quartzFrame.minX, y: primaryHeight - quartzFrame.maxY, width: quartzFrame.width, height: quartzFrame.height)
        let panel = NSPanel(contentRect: frame.insetBy(dx: -3, dy: -3), styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        panel.isOpaque = false; panel.backgroundColor = .clear; panel.level = .floating; panel.ignoresMouseEvents = true
        panel.contentView = NSHostingView(rootView: RoundedRectangle(cornerRadius: 5).stroke(Color.purple, lineWidth: 3).padding(2))
        panel.orderFrontRegardless(); highlight = panel
        DispatchQueue.main.asyncAfter(deadline: .now() + 2) { [weak panel] in panel?.orderOut(nil) }
    }
}

@main
@MainActor
struct UMacApplication {
    static func main() {
        let app = NSApplication.shared
        let delegate = AppDelegate()
        app.delegate = delegate
        app.setActivationPolicy(.accessory)
        withExtendedLifetime(delegate) { app.run() }
    }
}
