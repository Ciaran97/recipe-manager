import Foundation
import UniformTypeIdentifiers
import Testing
import ShareHandoff

@MainActor struct HandoffProviderTests {
    @Test func urlAndChatGPTStyleTextUseDistinctReviewableCandidates() async throws {
        let url = NSItemProvider(object: NSURL(string: "https://recipes.example.test/one")!)
        let text = NSItemProvider(object: "ChatGPT recipe\n1/3 cup rice\nSalt to taste" as NSString)
        let candidates = try await ShareProviderReader.candidates(from: [url, text])
        #expect(candidates.contains { $0.kind == .url && $0.content == "https://recipes.example.test/one" })
        #expect(candidates.contains { $0.kind == .text && $0.content.contains("1/3 cup rice") })
        #expect(ShareProviderReader.requiresChoice(candidates))
    }
    @Test func unsupportedMediaCannotBecomeAnInventedTextRecipe() async throws {
        let provider = NSItemProvider()
        provider.registerDataRepresentation(forTypeIdentifier: UTType.movie.identifier, visibility: .all) { completion in completion(Data([1, 2, 3]), nil); return nil }
        await #expect(throws: HandoffFailure.self) { try await ShareProviderReader.candidates(from: [provider]) }
    }
    @Test func oversizeTextReportsAnErrorAndNeverTruncatesContent() async throws {
        let provider = NSItemProvider(object: String(repeating: "a", count: HandoffLimits.maximumTextBytes + 1) as NSString)
        await #expect(throws: HandoffFailure.self) { try await ShareProviderReader.candidates(from: [provider]) }
    }
    @Test func cancelledCaptureDoesNotProduceASelection() async {
        let provider = NSItemProvider(object: "A recipe" as NSString)
        let task = Task { try await ShareProviderReader.candidates(from: [provider]) }
        task.cancel()
        await #expect(throws: CancellationError.self) { try await task.value }
    }
    @Test func failedAdvertisedURLDoesNotHideValidSharedText() async throws {
        let provider = NSItemProvider(object: "ChatGPT recipe\nSalt to taste" as NSString)
        provider.registerDataRepresentation(forTypeIdentifier: UTType.url.identifier, visibility: .all) { completion in
            completion(nil, NSError(domain: NSCocoaErrorDomain, code: NSFileReadCorruptFileError)); return nil
        }
        #expect(provider.canLoadObject(ofClass: NSURL.self))
        let result = try await ShareProviderReader.candidates(from: [provider])
        #expect(result.count == 1); #expect(result.first?.kind == .text)
        #expect(result.first?.content == "ChatGPT recipe\nSalt to taste")
    }

    @Test func advertisedUTF8DataRepresentationIsValidRecipeText() async throws {
        let provider = NSItemProvider(), content = "Recipe from text\n1/3 cup rice\nSalt to taste"
        provider.registerDataRepresentation(forTypeIdentifier: UTType.utf8PlainText.identifier, visibility: .all) { completion in
            completion(Data(content.utf8), nil); return nil
        }
        let result = try await ShareProviderReader.candidates(from: [provider])
        #expect(result.contains { $0.kind == .text && $0.content == content })
    }

}
