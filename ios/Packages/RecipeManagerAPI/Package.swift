// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "RecipeManagerAPI",
    platforms: [.iOS(.v18), .macOS(.v15)],
    products: [.library(name: "RecipeManagerAPI", targets: ["RecipeManagerAPI"])],
    dependencies: [
        .package(url: "https://github.com/apple/swift-openapi-generator", exact: "1.14.3"),
        .package(url: "https://github.com/apple/swift-openapi-runtime", exact: "1.13.3"),
        .package(url: "https://github.com/apple/swift-openapi-urlsession", exact: "1.4.0")
    ],
    targets: [
        .target(name: "RecipeManagerAPI", dependencies: [
            .product(name: "OpenAPIRuntime", package: "swift-openapi-runtime"),
            .product(name: "OpenAPIURLSession", package: "swift-openapi-urlsession")
        ], path: "Sources")
    ]
)
