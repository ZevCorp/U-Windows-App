import AppKit

@main
@MainActor
struct Fixture {
    @MainActor
    final class Delegate: NSObject, NSApplicationDelegate {
        var window: NSWindow!
        let counter = NSTextField(labelWithString: "Contador: 0")
        var count = 0
        func applicationDidFinishLaunching(_ notification: Notification) {
            window = NSWindow(contentRect: NSRect(x: 300, y: 250, width: 460, height: 240), styleMask: [.titled, .closable], backing: .buffered, defer: false)
            window.title = "Ü · Prueba local de Accessibility"
            window.isReleasedWhenClosed = false
            let stack = NSStackView(); stack.orientation = .vertical; stack.spacing = 20; stack.translatesAutoresizingMaskIntoConstraints = false
            let title = NSTextField(labelWithString: "Esta ventana es una prueba local. No usa internet.")
            let button = NSButton(title: "Sumar uno", target: self, action: #selector(increment))
            button.setAccessibilityIdentifier("increment")
            let field = NSTextField(string: "")
            field.placeholderString = "Texto de prueba"; field.setAccessibilityLabel("Texto de prueba"); field.setAccessibilityIdentifier("test-input")
            field.widthAnchor.constraint(equalToConstant: 350).isActive = true
            stack.addArrangedSubview(title); stack.addArrangedSubview(counter); stack.addArrangedSubview(button); stack.addArrangedSubview(field)
            window.contentView?.addSubview(stack)
            NSLayoutConstraint.activate([stack.centerXAnchor.constraint(equalTo: window.contentView!.centerXAnchor), stack.centerYAnchor.constraint(equalTo: window.contentView!.centerYAnchor)])
            window.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps: true)
        }
        @objc func increment() { count += 1; counter.stringValue = "Contador: \(count)" }
        func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }
    }
    static func main() {
        let app = NSApplication.shared, delegate = Delegate()
        app.delegate = delegate; app.setActivationPolicy(.regular)
        withExtendedLifetime(delegate) { app.run() }
    }
}
