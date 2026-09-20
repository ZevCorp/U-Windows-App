// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "UMac",
    platforms: [.macOS(.v14)],
    products: [.executable(name: "U", targets: ["UApp"])],
    targets: [
        .target(name: "UCore"),
        .target(name: "UMac", dependencies: ["UCore"]),
        .executableTarget(name: "UApp", dependencies: ["UCore", "UMac"]),
        .executableTarget(name: "UFixture"),
        .executableTarget(name: "NativeContract", dependencies: ["UCore", "UMac"], path: "Tests/UCoreTests")
    ]
)
