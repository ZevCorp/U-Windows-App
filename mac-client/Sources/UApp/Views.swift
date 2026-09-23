import SwiftUI
import AppKit
import AVFoundation
import Speech
import UMac

struct Face: View {
    @ObservedObject var model: AppModel
    var body: some View {
        TimelineView(.animation(minimumInterval: 0.08)) { context in
            let time = context.date.timeIntervalSinceReferenceDate
            let blinking = Int(time * 10) % 47 == 0
            ZStack {
                Circle().fill(.black.opacity(0.10)).blur(radius: 4).offset(y: 4)
                Circle().fill(LinearGradient(colors: [Color(red: 0.98, green: 0.97, blue: 1), Color(red: 0.87, green: 0.82, blue: 1)], startPoint: .topLeading, endPoint: .bottomTrailing))
                Circle().strokeBorder(color.opacity(0.65), lineWidth: model.busy ? 3 : 1.5)
                VStack(spacing: 11) {
                    HStack(spacing: 17) {
                        Capsule().fill(Color(red: 0.25, green: 0.16, blue: 0.40)).frame(width: 7, height: blinking ? 2 : 12)
                        Capsule().fill(Color(red: 0.25, green: 0.16, blue: 0.40)).frame(width: 7, height: blinking ? 2 : 12)
                    }
                    Capsule().fill(Color(red: 0.25, green: 0.16, blue: 0.40))
                        .frame(width: model.mode == .error ? 14 : 21, height: model.mode == .speaking ? 5 + 8 * abs(sin(time * 9)) : 4)
                }
                if model.busy { Circle().trim(from: 0, to: 0.22).stroke(color, style: StrokeStyle(lineWidth: 3, lineCap: .round)).rotationEffect(.degrees(time * 150)) }
                VStack { Spacer(); HStack { Spacer(); Circle().fill(model.microphone ? .green : .gray).frame(width: 11, height: 11).overlay(Circle().stroke(.white, lineWidth: 2)) }.padding(4) }
            }
        }
        .frame(width: 70, height: 70)
        .padding(9)
        .contentShape(Circle())
        .onTapGesture(count: 2) { model.toggleMicrophone() }
        .onTapGesture { model.showWindow?() }
        .contextMenu {
            Button("Hablar / silenciar") { model.toggleMicrophone() }
            Button("Detener tarea") { model.stop() }
            Button("Configuración") { model.selectedTab = 1; model.showWindow?() }
            Divider()
            Button("Salir de Ü") { NSApp.terminate(nil) }
        }
        .help("\(model.mode.rawValue): \(model.status)")
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("Ü, \(model.mode.rawValue)")
        .accessibilityAddTraits(.isButton)
    }
    var color: Color { model.mode == .error ? .orange : model.busy ? .purple : .indigo }
}

struct MainView: View {
    @ObservedObject var model: AppModel
    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 12) {
                Text("Ü").font(.system(size: 30, weight: .semibold, design: .rounded)).foregroundStyle(.purple)
                VStack(alignment: .leading, spacing: 3) {
                    Text("Tu asistente en Mac").font(.headline)
                    Text(model.mode.rawValue).font(.caption).foregroundStyle(.secondary)
                }
                Spacer()
                Button { model.stop() } label: { Label("Detener", systemImage: "stop.fill") }.disabled(!model.busy && !model.microphone)
            }.padding(20)
            Picker("Sección", selection: $model.selectedTab) {
                Text("Conversación").tag(0)
                Text("Configuración").tag(1)
            }.pickerStyle(.segmented).padding(.horizontal, 20).padding(.bottom, 16)
            if model.selectedTab == 0 { conversation } else { configuration }
        }.frame(minWidth: 480, minHeight: 550)
    }
    var conversation: some View {
        VStack(spacing: 0) {
            if model.messages.isEmpty {
                VStack(spacing: 14) {
                    Image(systemName: "waveform.circle").font(.system(size: 48, weight: .ultraLight)).foregroundStyle(.purple)
                    Text("¿Qué hacemos?").font(.title2.weight(.medium))
                    Text("Abre una aplicación, busca algo en el navegador o trabaja con lo que tienes en pantalla.")
                        .multilineTextAlignment(.center).foregroundStyle(.secondary).padding(.horizontal, 35)
                    Text("Pulsa el micrófono para conversar y vuelve a pulsarlo para desconectar.")
                        .font(.caption).foregroundStyle(.secondary).multilineTextAlignment(.center)
                }.frame(maxWidth: .infinity, maxHeight: .infinity)
            } else {
                ScrollViewReader { proxy in
                    ScrollView {
                        LazyVStack(alignment: .leading, spacing: 14) {
                            ForEach(model.messages) { message in
                                HStack {
                                    if message.user { Spacer(minLength: 35) }
                                    Text(message.text).textSelection(.enabled).padding(12)
                                        .background(message.user ? Color.purple.opacity(0.12) : Color.secondary.opacity(0.08), in: RoundedRectangle(cornerRadius: 14))
                                    if !message.user { Spacer(minLength: 20) }
                                }.id(message.id)
                            }
                        }.padding(20)
                    }.onChange(of: model.messages.count) { if let last = model.messages.last { proxy.scrollTo(last.id, anchor: .bottom) } }
                }
            }
            VStack(alignment: .leading, spacing: 10) {
                if model.busy { HStack { ProgressView().controlSize(.small); Text(model.status).font(.caption).lineLimit(2) } }
                if !model.partial.isEmpty { Text(model.partial).font(.caption).foregroundStyle(.secondary).lineLimit(2) }
                HStack(spacing: 10) {
                    Button { model.toggleMicrophone() } label: { Image(systemName: model.microphone ? "mic.fill" : "mic").foregroundStyle(model.microphone ? .purple : .secondary).frame(width: 24, height: 24) }.help("Hablar / silenciar")
                    TextField(model.mode == .question ? "Tu respuesta…" : "Pídele algo a Ü…", text: $model.draft).textFieldStyle(.plain).onSubmit { model.submitDraft() }
                    Button { model.submitDraft() } label: { Image(systemName: "arrow.up.circle.fill").font(.title2).foregroundStyle(.purple) }.buttonStyle(.plain).disabled(model.draft.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                }.padding(12).background(Color.secondary.opacity(0.07), in: RoundedRectangle(cornerRadius: 14))
                Text("Esc detiene la tarea · La carita sigue disponible al cerrar esta ventana")
                    .font(.system(size: 10)).foregroundStyle(.secondary)
            }.padding(16)
        }
    }
    var configuration: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 18) {
                Text("Conexión").font(.headline)
                Text("Usa tu credencial de Graph, la misma cuenta del asistente de Windows. Se guarda en el Llavero de este Mac.").font(.callout).foregroundStyle(.secondary)
                TextField("Dirección de Graph", text: $model.graphURL).textFieldStyle(.roundedBorder)
                SecureField(model.hasCredential ? "Nueva credencial (ya hay una guardada)" : "Credencial de Graph", text: $model.credential).textFieldStyle(.roundedBorder)
                HStack {
                    Button("Guardar") { model.saveConfiguration() }
                    Button("Comprobar conexión") { model.checkConnection() }
                }
                if !model.configurationMessage.isEmpty { Text(model.configurationMessage).font(.caption).foregroundStyle(.secondary) }
                Toggle("Usar dictado y voz de macOS como respaldo", isOn: $model.nativeDictation)
                    .onChange(of: model.nativeDictation) { UserDefaults.standard.set(model.nativeDictation, forKey: "nativeDictation") }
                Text("La voz en vivo permite conversar e interrumpir. El dictado nativo envía cada petición a Graph; tras 45 segundos, vuelve a llamarme «oye U».").font(.caption).foregroundStyle(.secondary)
                Divider()
                Text("Permisos del Mac").font(.headline)
                permission("Accesibilidad", detail: "Leer controles y usar teclado y ratón.", state: model.permissionSnapshot.accessibility) { model.permissions.request(.accessibility) }
                permission("Grabación de pantalla", detail: "Ver imágenes cuando una aplicación no expone sus controles.", state: model.permissionSnapshot.screenCapture) { model.permissions.request(.screenCapture) }
                permission(model.nativeDictation ? "Micrófono y dictado" : "Micrófono", detail: "Entender lo que le pides. El indicador verde muestra cuándo escucha.", state: model.nativeDictation ? model.permissionSnapshot.voice : model.permissionSnapshot.microphone) { model.permissions.request(model.nativeDictation ? .speech : .microphone) }
                HStack(spacing: 10) {
                    Button("Revisar permisos") { model.refreshPermissions() }
                    Button("Reiniciar Ü para aplicar") { model.permissions.relaunchApp() }
                }
                Divider()
                Text("Live 1 · Luna · \(model.jevStatus)").font(.caption).foregroundStyle(.secondary)
                Text("Control y privacidad").font(.headline)
                Text("La voz en vivo transmite el micrófono al proveedor mientras está conectada y termina a los 15 minutos. Graph recibe el texto de la pantalla durante una tarea. Las imágenes se envían solo cuando las solicita. Los campos protegidos se ocultan del árbol de accesibilidad. La conversación se mantiene en memoria y se borra al salir.")
                    .font(.caption).foregroundStyle(.secondary)
                Text("Autoriza siempre la entrada «Ü para Mac» que aparece desde esta app. Al volver de Ajustes, el estado se revisa automáticamente. Grabación de pantalla y algunos cambios de TCC pueden exigir reiniciar Ü; el botón anterior relanza exactamente este bundle instalado.")
                    .font(.caption).foregroundStyle(.secondary)
                Text("Bundle: \(Bundle.main.bundleIdentifier ?? "desconocido")\nRuta: \(Bundle.main.bundleURL.path)")
                    .font(.system(size: 10, design: .monospaced)).foregroundStyle(.secondary).textSelection(.enabled)
                Text("Firma: \(Bundle.main.object(forInfoDictionaryKey: "USigningMode") as? String ?? "desconocida"). Esta identidad no cambia al actualizar la app.")
                    .font(.caption).foregroundStyle(.secondary)
            }.padding(20)
        }
    }
    func permission(_ title: String, detail: String, state: PermissionState, action: @escaping () -> Void) -> some View {
        HStack(alignment: .top) {
            Image(systemName: state.isGranted ? "checkmark.circle.fill" : "circle").foregroundStyle(state.isGranted ? .green : .secondary).padding(.top, 2)
            VStack(alignment: .leading, spacing: 3) { Text(title); Text(detail).font(.caption).foregroundStyle(.secondary) }
            Spacer()
            if !state.isGranted { Button(state == .denied ? "Abrir Ajustes" : "Permitir", action: action) }
        }
    }
}
