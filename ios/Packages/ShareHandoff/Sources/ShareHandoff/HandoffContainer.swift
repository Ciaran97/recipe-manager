import Foundation

public enum HandoffContainer {
    public static func directory(bundle: Bundle = .main) throws -> URL {
        guard let group = bundle.object(forInfoDictionaryKey: "RecipeManagerAppGroup") as? String,
              group.hasPrefix("group."), !group.contains("$("),
              let directory = FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: group) else { throw HandoffFailure.persistence }
        return directory.appendingPathComponent("PendingRecipeCaptures", isDirectory: true)
    }
}
