import Foundation
import Security

actor KeychainStore: CredentialStore {
    private func query(origin: String) -> [String: Any] {
        [kSecClass as String: kSecClassGenericPassword,
         kSecAttrService as String: "RecipeManager.Session.\(origin)", kSecAttrAccount as String: "session"]
    }
    func load(origin: String) throws -> SessionCredential? {
        var attributes = query(origin: origin)
        attributes[kSecReturnData as String] = true
        attributes[kSecMatchLimit as String] = kSecMatchLimitOne
        var value: CFTypeRef?
        let status = SecItemCopyMatching(attributes as CFDictionary, &value)
        if status == errSecItemNotFound { return nil }
        guard status == errSecSuccess, let data = value as? Data else { throw SessionFailure.storage }
        do { return try JSONDecoder().decode(SessionCredential.self, from: data) }
        catch { try remove(origin: origin); return nil }
    }
    func save(_ credential: SessionCredential, origin: String) throws {
        let data = try JSONEncoder().encode(credential)
        let values: [String: Any] = [kSecValueData as String: data,
            kSecAttrAccessible as String: kSecAttrAccessibleWhenUnlockedThisDeviceOnly]
        let status = SecItemUpdate(query(origin: origin) as CFDictionary, values as CFDictionary)
        if status == errSecItemNotFound {
            var attributes = query(origin: origin)
            values.forEach { attributes[$0.key] = $0.value }
            guard SecItemAdd(attributes as CFDictionary, nil) == errSecSuccess else { throw SessionFailure.storage }
        } else if status != errSecSuccess { throw SessionFailure.storage }
    }
    func remove(origin: String) throws {
        let status = SecItemDelete(query(origin: origin) as CFDictionary)
        guard status == errSecSuccess || status == errSecItemNotFound else { throw SessionFailure.storage }
    }
}
