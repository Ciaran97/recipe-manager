// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "ShareHandoff",
    platforms: [.iOS(.v18), .macOS(.v15)],
    products: [.library(name: "ShareHandoff", targets: ["ShareHandoff"])],
    targets: [.target(name: "ShareHandoff")]
)
