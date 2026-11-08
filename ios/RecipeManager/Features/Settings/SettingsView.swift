import SwiftUI
import Observation
import RecipeManagerAPI

@Observable @MainActor
final class SettingsViewModel {
    var profile: Components.Schemas.ProfileRead?
    var household: Components.Schemas.HouseholdSettingsRead?
    var members: [Components.Schemas.MemberRead] = []
    var invitations: [Components.Schemas.InvitationRead] = []
    var memberCursor: String?
    var invitationCursor: String?
    var name = ""
    var householdName = ""
    var timezone = "UTC"
    var aiEnabled = false
    var aiModel = ""
    var aiBaseUrl = ""
    var newApiKey = ""
    var clearApiKey = false
    var currentPassword = ""
    var newPassword = ""
    var confirmation = ""
    var invitationLink: URL?
    var error: String?
    var notice: String?
    var loading = true
    var busy = false
    var remoteChanged = false
    private var profileETag: String?
    private var householdETag: String?
    private var invitationRetryKey: UUID?
    private let selection: ServerSelection
    private let requests: ScopedAPI
    var administrator: Bool { selection.user?.role == .administrator }
    var isDirty: Bool {
        name != (profile?.name ?? "") || householdName != (household?.name ?? "") || timezone != (household?.timezone ?? "UTC")
        || aiEnabled != (household?.ai.enabled ?? false) || aiModel != (household?.ai.model ?? "") || aiBaseUrl != (household?.ai.baseUrl ?? "")
        || !newApiKey.isEmpty || clearApiKey || !currentPassword.isEmpty || !newPassword.isEmpty || !confirmation.isEmpty
    }
    init(selection: ServerSelection) { self.selection = selection; requests = ScopedAPI(selection: selection) }

    func load(background: Bool = false) async {
        remoteChanged = false
        if !background { loading = true }
        defer { if !background { loading = false } }
        await perform(background: background) {
            let profileResponse = try await self.requests.send(path: "/api/v1/profile")
            let householdResponse = try await self.requests.send(path: "/api/v1/household/settings")
            let profile = try APIJSON.decoder().decode(Components.Schemas.ProfileRead.self, from: profileResponse.data)
            let household = try APIJSON.decoder().decode(Components.Schemas.HouseholdSettingsRead.self, from: householdResponse.data)
            self.profile = profile; self.household = household
            self.profileETag = profileResponse.etag; self.householdETag = householdResponse.etag
            self.name = profile.name; self.householdName = household.name; self.timezone = household.timezone
            self.aiEnabled = household.ai.enabled; self.aiModel = household.ai.model; self.aiBaseUrl = household.ai.baseUrl
            self.newApiKey = ""; self.clearApiKey = false
            self.currentPassword = ""; self.newPassword = ""; self.confirmation = ""
            try await self.loadMembers(append: false)
            if self.administrator { try await self.loadInvitations(append: false) }
        }
    }
    func saveProfile() async {
        guard let profile else { return }
        await perform {
            let body = try APIJSON.encoder().encode(Components.Schemas.ProfileWrite(name: self.name, preferences: profile.preferences))
            let response = try await self.requests.send(path: "/api/v1/profile", method: "PUT", body: body, etag: self.profileETag)
            self.profile = try APIJSON.decoder().decode(Components.Schemas.ProfileRead.self, from: response.data)
            self.profileETag = response.etag; self.notice = "Profile saved."
        }
    }
    func saveHousehold() async {
        guard administrator else { return }
        if clearApiKey && !newApiKey.isEmpty { error = "Choose either replacing or clearing the API key."; return }
        await perform {
            let body = try APIJSON.encoder().encode(Components.Schemas.HouseholdSettingsWrite(name: self.householdName, timezone: self.timezone,
                ai: .init(enabled: self.aiEnabled, provider: .OpenAICompatible, model: self.aiModel, baseUrl: self.aiBaseUrl,
                    apiKey: self.newApiKey.isEmpty ? nil : self.newApiKey, clearApiKey: self.clearApiKey)))
            let response = try await self.requests.send(path: "/api/v1/household/settings", method: "PUT", body: body, etag: self.householdETag)
            self.household = try APIJSON.decoder().decode(Components.Schemas.HouseholdSettingsRead.self, from: response.data)
            self.householdETag = response.etag; self.newApiKey = ""; self.clearApiKey = false; self.notice = "Household settings saved."
        }
    }
    func changePassword() async {
        guard newPassword == confirmation, newPassword.count >= 12 else { error = "Enter matching new passwords of at least 12 characters."; return }
        await perform {
            let body = try APIJSON.encoder().encode(Components.Schemas.PasswordWrite(currentPassword: self.currentPassword, newPassword: self.newPassword))
            _ = try await self.requests.send(path: "/api/v1/profile/password", method: "POST", body: body, etag: self.profileETag)
            self.currentPassword = ""; self.newPassword = ""; self.confirmation = ""
            await self.selection.clearAccount()
            self.selection.error = "Password changed. Sign in with your new password."
        }
    }
    private func loadMembers(append: Bool) async throws {
        let query = append ? memberCursor.map { "?cursor=\($0.addingPercentEncoding(withAllowedCharacters: .alphanumerics) ?? $0)" } ?? "" : ""
        let response = try await requests.send(path: "/api/v1/household/members" + query)
        let page = try APIJSON.decoder().decode(Components.Schemas.MemberPage.self, from: response.data)
        members = append ? members + page.items : page.items; memberCursor = page.nextCursor
    }
    private func loadInvitations(append: Bool) async throws {
        let query = append ? invitationCursor.map { "?cursor=\($0.addingPercentEncoding(withAllowedCharacters: .alphanumerics) ?? $0)" } ?? "" : ""
        let response = try await requests.send(path: "/api/v1/household/invitations" + query)
        let page = try APIJSON.decoder().decode(Components.Schemas.InvitationPage.self, from: response.data)
        invitations = append ? invitations + page.items : page.items; invitationCursor = page.nextCursor
    }
    func moreMembers() async { await perform { try await self.loadMembers(append: true) } }
    func moreInvitations() async { await perform { try await self.loadInvitations(append: true) } }
    func createInvitation() async {
        invitationRetryKey = invitationRetryKey ?? UUID()
        await perform {
            let response = try await self.requests.send(path: "/api/v1/household/invitations", method: "POST", body: Data("{}".utf8), retryKey: self.invitationRetryKey)
            let created = try APIJSON.decoder().decode(Components.Schemas.InvitationCreated.self, from: response.data)
            self.invitationLink = URL(string: created.link, relativeTo: self.selection.origin?.url)?.absoluteURL
            self.invitationRetryKey = nil
            try await self.loadInvitations(append: false)
            self.notice = "Invitation created. Share the link before leaving this screen."
        }
    }
    func revoke(_ invitation: Components.Schemas.InvitationRead) async {
        await perform {
            _ = try await self.requests.send(path: "/api/v1/household/invitations/\(invitation.id)", method: "DELETE", etag: invitation.etag)
            try await self.loadInvitations(append: false); self.invitationLink = nil; self.notice = "Invitation revoked."
        }
    }
    func remove(_ member: Components.Schemas.MemberRead) async {
        await perform {
            _ = try await self.requests.send(path: "/api/v1/household/members/\(member.id)", method: "DELETE", etag: member.etag)
            try await self.loadMembers(append: false); self.notice = "Member access revoked. Shared data is retained."
        }
    }
    func logout() async {
        await perform {
            _ = try await self.requests.send(path: "/api/v1/auth/logout", method: "POST")
            await self.selection.clearAccount()
        }
    }
    private func perform(background: Bool = false, _ operation: () async throws -> Void) async {
        guard !busy else { return }
        busy = true; if !background { error = nil; notice = nil }
        defer { busy = false }
        do { try await operation() } catch { self.error = apiErrorMessage(error) }
    }
}

struct SettingsView: View {
    @Bindable var selection: ServerSelection
    let changeServer: () -> Void
    @State private var model: SettingsViewModel
    @AccessibilityFocusState private var errorFocused: Bool
    @ScaledMetric(relativeTo: .body) private var inputHeight = 28
    init(selection: ServerSelection, changeServer: @escaping () -> Void) {
        self.selection = selection; self.changeServer = changeServer
        _model = State(initialValue: SettingsViewModel(selection: selection))
    }
    var body: some View {
        Form {
            if model.remoteChanged { Section { Text("Your profile or household settings changed. Entered values are retained; save or discard them before reloading.").foregroundStyle(.orange) } }
            if let error = model.error { Section { Text(error).foregroundStyle(.red).accessibilityFocused($errorFocused) } }
            if let notice = model.notice { Section { Text(notice) } }
            if model.loading { ProgressView("Loading your household") }
            else {
                Section { NavigationLink("Ingredient catalogue") { CatalogueView(selection: selection) } }
                Section {
                    TextField("Your name", text: $model.name).textContentType(.name).frame(minHeight: inputHeight).accessibilityIdentifier("profile-name")
                    Button("Save profile") { Task { await model.saveProfile() } }.disabled(model.busy)
                } header: { Text("Your profile").foregroundStyle(Color(uiColor: .label)) }
                Section {
                    SecureField("Current password", text: $model.currentPassword).textContentType(.password).frame(minHeight: inputHeight)
                    SecureField("New password", text: $model.newPassword).textContentType(.newPassword).frame(minHeight: inputHeight)
                    SecureField("Confirm new password", text: $model.confirmation).textContentType(.newPassword).frame(minHeight: inputHeight)
                    Text("Changing your password signs out all devices.").font(.footnote).foregroundStyle(Color(uiColor: .label)).fixedSize(horizontal: false, vertical: true)
                    Button { Task { await model.changePassword() } } label: { Text("Change password").font(.body).fixedSize(horizontal: false, vertical: true) }.disabled(model.busy)
                } header: { Text("Password").foregroundStyle(Color(uiColor: .label)) }
                Section {
                    if model.administrator {
                        TextField("Household name", text: $model.householdName)
                        Picker("Time zone", selection: $model.timezone) { ForEach(TimeZone.knownTimeZoneIdentifiers, id: \.self) { Text($0).tag($0) } }
                        Button("Save household") { Task { await model.saveHousehold() } }.disabled(model.busy)
                    } else {
                        LabeledContent { Text(model.household?.name ?? "").fixedSize(horizontal: false, vertical: true) } label: { Text("Name").font(.body).foregroundStyle(Color(uiColor: .label)).fixedSize(horizontal: false, vertical: true) }
                        LabeledContent { Text(model.household?.timezone ?? "").fixedSize(horizontal: false, vertical: true) } label: { Text("Time zone").foregroundStyle(Color(uiColor: .label)).fixedSize(horizontal: false, vertical: true) }
                        Text("An administrator manages household settings and membership.").font(.footnote).foregroundStyle(Color(uiColor: .label)).fixedSize(horizontal: false, vertical: true)
                    }
                } header: { Text("Household").foregroundStyle(Color(uiColor: .label)) }
                if model.administrator {
                    Section {
                        Toggle("Enable AI text imports", isOn: $model.aiEnabled)
                        LabeledContent("Provider", value: "OpenAI-compatible API")
                        TextField("Provider base URL", text: $model.aiBaseUrl).keyboardType(.URL).textInputAutocapitalization(.never).autocorrectionDisabled()
                        TextField("Model", text: $model.aiModel).textInputAutocapitalization(.never).autocorrectionDisabled()
                        if model.household?.ai.hasApiKey == true { Text("A key is configured. Leave the new key blank to keep it.").font(.footnote).foregroundStyle(Color(uiColor: .label).opacity(0.8)) }
                        SecureField("New API key (optional)", text: $model.newApiKey).textInputAutocapitalization(.never).autocorrectionDisabled()
                        if model.household?.ai.hasApiKey == true { Toggle("Clear saved key", isOn: $model.clearApiKey) }
                        Text("Manual entry and website imports work without AI. Recipe text is sent to your provider when you start an AI import.").font(.footnote).foregroundStyle(Color(uiColor: .label).opacity(0.8))
                        Button("Save AI settings") { Task { await model.saveHousehold() } }.disabled(model.busy)
                    } header: { Text("Optional AI imports").foregroundStyle(Color(uiColor: .label)) }
                }
                Section {
                    ForEach(model.members, id: \.id) { member in
                        VStack(alignment: .leading) {
                            Text(member.name.isEmpty ? member.email : member.name)
                            Text(member.role.rawValue).font(.caption).foregroundStyle(Color(uiColor: .label).opacity(0.8))
                            if model.administrator && member.role != .Administrator {
                                Button("Remove access", role: .destructive) { Task { await model.remove(member) } }.disabled(model.busy)
                            }
                        }
                    }
                    if model.memberCursor != nil { Button("Load more members") { Task { await model.moreMembers() } } }
                } header: { Text("Household members").foregroundStyle(Color(uiColor: .label)) }
                if model.administrator {
                    Section {
                        Button("Create invitation") { Task { await model.createInvitation() } }.disabled(model.busy)
                        if let link = model.invitationLink { ShareLink("Share invitation", item: link) }
                        ForEach(model.invitations, id: \.id) { invitation in
                            VStack(alignment: .leading) {
                                Text(invitation.acceptedAt != nil ? "Accepted" : invitation.revokedAt != nil ? "Revoked" : "Expires")
                                Text(invitation.expiresAt, format: .dateTime.day().month().hour().minute()).font(.caption).foregroundStyle(Color(uiColor: .label).opacity(0.8))
                                if invitation.acceptedAt == nil && invitation.revokedAt == nil {
                                    Button("Revoke invitation", role: .destructive) { Task { await model.revoke(invitation) } }.disabled(model.busy)
                                }
                            }
                        }
                        if model.invitationCursor != nil { Button("Load more invitations") { Task { await model.moreInvitations() } } }
                    } header: { Text("Invitations").foregroundStyle(Color(uiColor: .label)) }
                }
                Section {
                    Text(selection.origin?.key ?? "").font(.footnote)
                    Button("Change server", action: changeServer)
                    Button("Sign out all sessions", role: .destructive) { Task { await model.logout() } }.disabled(model.busy)
                } header: { Text("Server and session").foregroundStyle(Color(uiColor: .label)) }
                Button("Reload and discard unsaved changes") { Task { await model.load() } }.disabled(model.busy)
            }
        }
        .disabled(model.busy)
        .navigationTitle("Settings")
        .task { if model.profile == nil { await model.load() } else if !model.isDirty { await model.load(background: true) } }
        .onHouseholdChange(domains: [.settings, .membership], personal: true) { _ in
            if model.isDirty || model.busy || model.loading { model.remoteChanged = true }
            else { Task { await model.load(background: true) } }
        }
        .onChange(of: model.error) { _, value in if value != nil { errorFocused = true } }
        .onChange(of: model.isDirty) { _, dirty in if !model.loading { selection.hasPendingInput = dirty } }
    }
}
