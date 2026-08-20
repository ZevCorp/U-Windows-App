import Foundation

// El contrato del cliente de Mac. Es el nivel 2 de la compuerta, que hasta hoy no existía aquí.
//
//     swift run -c release Contrato            todas
//     swift run -c release Contrato 2 5 6      solo esas
//     swift run -c release Contrato --rapido   solo las que no necesitan micrófono
//
// Sale con 0 si el contrato está INTACTO, y con 1 si algo se rompió. Ese código de salida es lo que
// convierte esto en una compuerta y no en un informe: `hacer-app.sh` lo corre en cada build.

// SIN BÚFER. `print` va por líneas y `FileHandle.write` va directo: mezclados, el encabezado sale
// DESPUÉS de las promesas y el informe se lee al revés. Un juez que imprime desordenado se lee mal,
// y lo que se lee mal no se lee.
setvbuf(stdout, nil, _IONBF, 0)

let argumentos = Array(CommandLine.arguments.dropFirst())
let rapido = argumentos.contains("--rapido")
let pedidas = Set(argumentos.compactMap(Int.init))

// Las que necesitan el altavoz y el micrófono de esta máquina. En `--rapido` se saltan: son las
// lentas, y en un bucle de «cambio algo y vuelvo a compilar» nadie espera tres minutos.
let conMicrofono: Set<Int> = [8, 9, 10]

var aJuzgar = Promesas.todas
if !pedidas.isEmpty { aJuzgar = aJuzgar.filter { pedidas.contains($0.numero) } }
if rapido { aJuzgar = aJuzgar.filter { !conMicrofono.contains($0.numero) } }

guard FileManager.default.fileExists(atPath: App.bundle.path) else {
    // NO ES «CONTRATO ROTO». Es que no hay nada que juzgar, y decir lo contrario sería exactamente
    // el juez que se cae y grita culpable.
    print("""

    ✋ NO PUEDO CORRER: no encuentro \(App.bundle.path)
       Compila primero:  ./hacer-app.sh

    """)
    exit(2)
}

print("\n  EL CONTRATO DE Ü EN MAC — \(aJuzgar.count) promesa(s)\(rapido ? "  ·  modo rápido, sin micrófono" : "")\n")

var cumplidas = 0, rotas = 0, pendientes = 0, sinJuzgar = 0
let arranque = Date()

for p in aJuzgar {
    let etiqueta = String(format: "%2d", p.numero)
    print("  \(etiqueta) · \(p.enunciado) … ", terminator: "")
    let t0 = Date()
    let v: Veredicto
    do { v = try p.correr() }
    catch {
        // La cadena ENTERA de la excepción. Un catch que se traga el motivo convierte un bug de
        // aridad en «la API no existe», y eso ya estuvo documentado como cierto en este repo.
        var motivo = ""
        var e: Error? = error
        while let x = e {
            motivo += "\(type(of: x)): \(x.localizedDescription); "
            e = (x as NSError).userInfo[NSUnderlyingErrorKey] as? Error
        }
        v = .noPudeCorrer(motivo)
    }
    let tardo = String(format: "%.0fs", -t0.timeIntervalSinceNow)

    switch v {
    case .cumplida(let d):
        cumplidas += 1;  print("✅ \(tardo)\n       \(d)")
    case .rota(let d):
        rotas += 1;      print("❌ \(tardo)\n       ROTA: \(indentar(d))")
    case .pendiente(let d):
        pendientes += 1; print("⧗ \(tardo)\n       PENDIENTE: \(indentar(d))")
    case .noPudeCorrer(let d):
        sinJuzgar += 1;  print("✋ \(tardo)\n       NO PUDE JUZGARLA: \(indentar(d))")
    }
}

App.cerrar()

func indentar(_ t: String) -> String {
    t.split(separator: "\n").joined(separator: "\n       ")
}

print("""

  ─────────────────────────────────────────────────────────────
   \(cumplidas) cumplida(s) · \(rotas) rota(s) · \(pendientes) pendiente(s) · \(sinJuzgar) sin juzgar
   en \(String(format: "%.0f", -arranque.timeIntervalSinceNow))s
""")

// LAS PENDIENTES NO SUMAN AL VERDE, y esa distinción es todo el valor de esto. Una promesa escrita
// sin su código que dijera «no aplicable» se sumaría a las cumplidas, y el contrato pasaría a
// certificar el vacío. Pendiente es rojo — un rojo que se sabe de dónde viene.
if rotas == 0 && sinJuzgar == 0 && pendientes == 0 {
    print("\n  CONTRATO INTACTO: Ü se comporta como el día que se congeló.\n")
    exit(0)
}
if rotas == 0 && sinJuzgar == 0 {
    print("""

      CONTRATO CON DEUDA CONOCIDA: \(pendientes) promesa(s) todavía sin cumplir.
      Nada de lo que ya funcionaba se rompió. La deuda está escrita en PENDIENTES.md.
    """)
    exit(1)
}
if rotas > 0 {
    print("\n  CONTRATO ROTO: \(rotas) promesa(s) incumplida(s). El cambio no puede entrar así.\n")
} else {
    print("""

      SIN VEREDICTO: \(sinJuzgar) promesa(s) no se pudieron ejecutar.
      Esto NO dice que el código esté mal — dice que el arnés no llegó a juzgarlo.
    """)
}
exit(1)
