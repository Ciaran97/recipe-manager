import SwiftUI
import Observation
import RecipeManagerAPI
import UniformTypeIdentifiers

@Observable @MainActor
final class AuthViewModel {
    enum Mode: String, CaseIterable { case login = "Sign in", setup = "Create household", invite = "Join household" }
    var mode: Mode
    var email = ""
    var password = ""
    var confirmation = ""
    var householdName = ""
    var timezone = TimeZone.current.identifier
    var setupToken = ""
    var name = ""
    var token = ""
    var busy = false
    var loading = true
    var setupRequired = false
    private(set) var connectionFailed = false
    var error: String?
    private let selection: ServerSelection
    private let requests: ScopedAPI

    init(selection: ServerSelection) {
        let initialToken = selection.invitationToken ?? ""
        self.selection = selection
        self.requests = ScopedAPI(selection: selection)
        token = initialToken
        mode = selection.invitationToken == nil ? .login : .invite
    }
    func load() async {
        loading = true; error = nil; connectionFailed = false
        defer { loading = false }
        do {
            let response = try await requests.send(path: "/api/v1/setup")
            setupRequired = try APIJSON.decoder().decode(Components.Schemas.SetupStatus.self, from: response.data).setupRequired
            if setupRequired && token.isEmpty { mode = .setup }
        } catch { connectionFailed = true; self.error = apiErrorMessage(error) }
    }
    func submit() async {
        guard !busy else { return }
        error = nil
        guard !email.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty, password.count >= 12,
            mode == .login || password == confirmation else {
            error = "Enter your email and a password of at least 12 characters. New passwords must match."
            return
        }
        if mode == .invite && token.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { error = "Paste your invitation link or token."; return }
        busy = true
        defer { busy = false }
        do {
            let body: Data
            let path: String
            switch mode {
            case .login:
                body = try APIJSON.encoder().encode(Components.Schemas.LoginWrite(email: email, password: password)); path = "/api/v1/auth/login"
            case .setup:
                body = try APIJSON.encoder().encode(Components.Schemas.SetupRequest(setupToken: setupToken, email: email, password: password, householdName: householdName, timezone: timezone)); path = "/api/v1/setup"
            case .invite:
                let extracted = try IncomingInvitation.token(from: token, expected: selection.origin)
                body = try APIJSON.encoder().encode(Components.Schemas.AcceptInvitationRequest(token: extracted, email: email, password: password, name: name)); path = "/api/v1/invitations/accept"
            }
            let response = try await requests.send(path: path, method: "POST", body: body)
            let user = try APIJSON.decoder().decode(ClientUser.self, from: response.data)
            password = ""; confirmation = ""; setupToken = ""; token = ""
            await selection.signedIn(user)
        } catch { self.error = apiErrorMessage(error) }
    }
}

struct AuthView: View {
    @Bindable var selection: ServerSelection
    @State private var model: AuthViewModel
    @AccessibilityFocusState private var errorFocused: Bool
    init(selection: ServerSelection) {
        self.selection = selection
        _model = State(initialValue: AuthViewModel(selection: selection))
    }
    var body: some View {
        VStack(spacing: 0) {
            if let error = model.error ?? selection.error {
                VStack {
                    Text(error).foregroundStyle(.red)
                        .accessibilityFocused($errorFocused).accessibilityIdentifier("auth-error")
                    if model.connectionFailed {
                        Button("Retry server connection") { Task { await model.load() } }.disabled(model.loading || model.busy)
                    }
                }.padding()
            }
            Form {
                Section {
                    Text(selection.origin?.key ?? "").font(.footnote).foregroundStyle(.secondary)
                    if model.loading { ProgressView("Checking your household") }
                }
                if !model.loading {
                    Section {
                        Picker("Account", selection: $model.mode) {
                            Text("Sign in").tag(AuthViewModel.Mode.login)
                            if model.setupRequired { Text("Create household").tag(AuthViewModel.Mode.setup) }
                            Text("Join household").tag(AuthViewModel.Mode.invite)
                        }.accessibilityIdentifier("auth-mode")
                    }
                    if model.mode == .setup {
                        Section("New household") {
                            TextField("Household name", text: $model.householdName).accessibilityIdentifier("household-name")
                            Picker("Time zone", selection: $model.timezone) {
                                ForEach(TimeZone.knownTimeZoneIdentifiers, id: \.self) { Text($0).tag($0) }
                            }
                            SecureField("Operator setup secret", text: $model.setupToken).textInputAutocapitalization(.never).autocorrectionDisabled().accessibilityIdentifier("setup-token")
                        }
                    }
                    if model.mode == .invite {
                        Section("Invitation") {
                            SecureField("Invitation link or token", text: $model.token).textInputAutocapitalization(.never).autocorrectionDisabled().accessibilityIdentifier("invitation-token")
                            PasteButton(supportedContentTypes: [.url, .plainText]) { providers in
                                InvitationPaste.load(providers) { value in
                                if let value { model.token = value }
                                else { model.error = "Could not paste this invitation. Copy its URL or token and try again." }
                            }
                            }
                            TextField("Your name (optional)", text: $model.name).textContentType(.name)
                        }
                    }
                    Section("Your account") {
                        TextField("Email", text: $model.email).textContentType(.username).keyboardType(.emailAddress)
                            .textInputAutocapitalization(.never).autocorrectionDisabled().accessibilityIdentifier("email")
                        SecureField("Password", text: $model.password).textContentType(model.mode == .login ? .password : .newPassword).textInputAutocapitalization(.never).autocorrectionDisabled().accessibilityIdentifier("password")
                        if model.mode != .login { SecureField("Confirm password", text: $model.confirmation).textContentType(.newPassword).textInputAutocapitalization(.never).autocorrectionDisabled().accessibilityIdentifier("confirm-password") }
                    }
                    Section {
                        Button(model.busy ? "Please wait…" : model.mode.rawValue) { Task { await model.submit() } }
                            .disabled(model.busy).accessibilityIdentifier("submit-auth")
                        Text("Use at least 12 characters. Each household member has their own account.").font(.footnote).foregroundStyle(.secondary)
                    }
                }
            }
        }
        .disabled(model.busy)
        .navigationTitle("Recipe Manager")
        .task { await model.load() }
        .onChange(of: model.error) { _, value in if value != nil { errorFocused = true } }
        .onChange(of: [model.email, model.password, model.confirmation, model.householdName, model.setupToken, model.name, model.token]) { _, values in
            if selection.user == nil { selection.hasPendingInput = values.contains { !$0.isEmpty } }
        }
    }
}
