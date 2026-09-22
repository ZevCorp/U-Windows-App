import AppKit
import ApplicationServices
import AVFoundation
import Combine
import CoreGraphics
import Speech

public enum PermissionKind: String, CaseIterable, Identifiable, Sendable {
    case accessibility
    case screenCapture
    case microphone
    case speech

    public var id: String { rawValue }
}

public enum PermissionState: String, Equatable, Sendable {
    case granted
    case needsUserAction
    case denied
    case restricted

    public var isGranted: Bool { self == .granted }
}

public struct PermissionSnapshot: Equatable, Sendable {
    public var accessibility: PermissionState
    public var screenCapture: PermissionState
    public var microphone: PermissionState
    public var speech: PermissionState
    public var checkedAt: Date

    public var voice: PermissionState {
        if microphone == .granted && speech == .granted { return .granted }
        if microphone == .denied || speech == .denied { return .denied }
        if microphone == .restricted || speech == .restricted { return .restricted }
        return .needsUserAction
    }

    public var canControlComputer: Bool { accessibility == .granted }

    public init(
        accessibility: PermissionState,
        screenCapture: PermissionState,
        microphone: PermissionState,
        speech: PermissionState,
        checkedAt: Date = .now
    ) {
        self.accessibility = accessibility
        self.screenCapture = screenCapture
        self.microphone = microphone
        self.speech = speech
        self.checkedAt = checkedAt
    }
}

/// Single source of truth for macOS TCC permissions.
///
/// TCC can update while this process is suspended in System Settings. The center
/// therefore refreshes on every app activation and briefly polls after a request,
/// so the UI never depends on a manually invalidated SwiftUI view.
@MainActor
public final class PermissionCenter: ObservableObject {
    public static let bundleIdentifier = "com.zevcorp.u.mac"
    @Published public private(set) var snapshot: PermissionSnapshot

    private var activationObserver: NSObjectProtocol?
    private var appActivationObserver: NSObjectProtocol?
    private var pollingTask: Task<Void, Never>?

    public init() {
        snapshot = Self.readSnapshot()
        activationObserver = NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.didWakeNotification,
            object: nil,
            queue: .main
        ) { [weak self] _ in
            Task { @MainActor in self?.refreshAndPoll() }
        }
        appActivationObserver = NotificationCenter.default.addObserver(
            forName: NSApplication.didBecomeActiveNotification,
            object: nil,
            queue: .main
        ) { [weak self] _ in
            Task { @MainActor in self?.refreshAndPoll() }
        }
    }

    deinit {
        if let activationObserver { NSWorkspace.shared.notificationCenter.removeObserver(activationObserver) }
        if let appActivationObserver { NotificationCenter.default.removeObserver(appActivationObserver) }
        pollingTask?.cancel()
    }

    public func refresh() {
        snapshot = Self.readSnapshot()
    }

    public func refreshAndPoll() {
        refresh()
        startPolling()
    }

    public func request(_ kind: PermissionKind) {
        switch kind {
        case .accessibility:
            _ = AXIsProcessTrustedWithOptions([
                kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true
            ] as CFDictionary)
            openSettings(for: kind)
        case .screenCapture:
            _ = CGRequestScreenCaptureAccess()
            openSettings(for: kind)
        case .microphone, .speech:
            Task { await requestVoice() }
        }
        startPolling()
    }

    public func requestVoice() async {
        if AVCaptureDevice.authorizationStatus(for: .audio) == .notDetermined {
            _ = await AVCaptureDevice.requestAccess(for: .audio)
        }
        if SFSpeechRecognizer.authorizationStatus() == .notDetermined {
            _ = await withCheckedContinuation { continuation in
                SFSpeechRecognizer.requestAuthorization { status in continuation.resume(returning: status) }
            }
        }
        refreshAndPoll()
        if snapshot.microphone != .granted { openSettings(for: .microphone) }
        else if snapshot.speech != .granted { openSettings(for: .speech) }
    }

    public func openSettings(for kind: PermissionKind) {
        let pane: String
        switch kind {
        case .accessibility: pane = "Privacy_Accessibility"
        case .screenCapture: pane = "Privacy_ScreenCapture"
        case .microphone: pane = "Privacy_Microphone"
        case .speech: pane = "Privacy_SpeechRecognition"
        }
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?\(pane)") {
            NSWorkspace.shared.open(url)
        }
    }

    /// Relaunches the exact bundle that owns the current TCC record.
    public func relaunchApp() {
        let bundleURL = Bundle.main.bundleURL
        let task = Process()
        task.executableURL = URL(fileURLWithPath: "/usr/bin/open")
        task.arguments = ["-n", bundleURL.path]
        try? task.run()
        NSApp.terminate(nil)
    }

    public static func readSnapshot() -> PermissionSnapshot {
        PermissionSnapshot(
            accessibility: AXIsProcessTrusted() ? .granted : .needsUserAction,
            screenCapture: CGPreflightScreenCaptureAccess() ? .granted : .needsUserAction,
            microphone: microphoneState(AVCaptureDevice.authorizationStatus(for: .audio)),
            speech: speechState(SFSpeechRecognizer.authorizationStatus())
        )
    }

    private func startPolling() {
        pollingTask?.cancel()
        pollingTask = Task { @MainActor [weak self] in
            for _ in 0..<60 {
                guard !Task.isCancelled else { return }
                self?.refresh()
                try? await Task.sleep(for: .milliseconds(500))
            }
        }
    }

    private static func microphoneState(_ status: AVAuthorizationStatus) -> PermissionState {
        switch status {
        case .authorized: return .granted
        case .denied: return .denied
        case .restricted: return .restricted
        case .notDetermined: return .needsUserAction
        @unknown default: return .needsUserAction
        }
    }

    private static func speechState(_ status: SFSpeechRecognizerAuthorizationStatus) -> PermissionState {
        switch status {
        case .authorized: return .granted
        case .denied: return .denied
        case .restricted: return .restricted
        case .notDetermined: return .needsUserAction
        @unknown default: return .needsUserAction
        }
    }
}
