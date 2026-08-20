// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "U",
    platforms: [.macOS(.v13)],
    targets: [
        .executableTarget(name: "U", path: "Sources/U"),

        // EL JUEZ. Se compila aparte de la app a propósito: un contrato que viva dentro de lo que
        // juzga se cae con ello, y entonces «no pude correr» y «la promesa falló» se confunden —
        // que es lo peor que puede decir un juez (aprendizaje nº17 del repo).
        .executableTarget(name: "Contrato", path: "Sources/Contrato"),

    ]
)
