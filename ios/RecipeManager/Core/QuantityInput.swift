import SwiftUI

// Preserve entered strings. Authoritative validation, scaling and conversion use the API.
struct QuantityInput: View {
    let title: String
    @Binding var text: String
    var body: some View {
        TextField(title, text: $text)
            .keyboardType(.numbersAndPunctuation)
            .autocorrectionDisabled()
            .accessibilityLabel(title)
    }
}
