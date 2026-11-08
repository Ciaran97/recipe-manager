import SwiftUI

@main
struct RecipeManagerApp: App {
    @State private var selection = ServerSelection()
    var body: some Scene {
        WindowGroup {
            RootView(selection: selection)
                .tint(Color(uiColor: .label))
                .task { await selection.restore(UserDefaults.standard.string(forKey: "serverOrigin")) }
        }
    }
}

private struct RootView: View {
    @Bindable var selection: ServerSelection
    @State private var showingServer = false
    @State private var showingCaptures = false
    @State private var scene: SceneCoordinator
    @Environment(\.scenePhase) private var phase
    init(selection: ServerSelection) { self.selection = selection; _scene = State(initialValue: SceneCoordinator(selection: selection)) }
    var body: some View {
        Group {
            if selection.user != nil {
                TabView {
                    NavigationStack { RecipeLibraryView(selection: selection) }
                        .tabItem { Label("Recipes", systemImage: "book") }
                    NavigationStack { MealPlannerView(selection: selection) }
                        .tabItem { Label("Meal Planner", systemImage: "calendar") }
                    NavigationStack { ShoppingListView(selection: selection) }
                        .tabItem { Label("Shopping Lists", systemImage: "cart") }
                    NavigationStack {
                        SettingsView(selection: selection, changeServer: { showingServer = true })
                    }.tabItem { Label("Settings", systemImage: "gear") }
                }.id(selection.sessionIdentity)
            } else {
                NavigationStack {
                    if selection.origin == nil || selection.api == nil {
                        ServerForm(selection: selection)
                            .navigationTitle("Recipe Manager")
                    } else {
                        AuthView(selection: selection).id(selection.sessionIdentity)
                            .toolbar {
                                ToolbarItem(placement: .topBarTrailing) { Button("Server") { showingServer = true } }
                                ToolbarItem(placement: .topBarLeading) { Button("Saved captures", systemImage: "tray") { showingCaptures = true } }
                            }
                    }
                }
            }
        }
        .sheet(isPresented: $showingCaptures) { NavigationStack {
            PendingImportInbox(selection: selection) { _ in showingCaptures = false }
                .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Done") { showingCaptures = false } } }
        } }
        .onChange(of: selection.user != nil) { _, authenticated in if authenticated { showingCaptures = false } }
        .environment(\.householdSync, scene)
        .task(id: "\(selection.sessionIdentity):\(selection.user != nil):\(phase == .active)") { await scene.update(active: phase == .active) }
        .onOpenURL { url in selection.receiveInvitation(url) }
        .sheet(item: Binding(get: { selection.pendingInvitation }, set: { if $0 == nil { selection.cancelInvitation() } })) { invitation in
            NavigationStack {
                Form {
                    Section("Join household") {
                        Text("Confirm the server before continuing.")
                        Text(invitation.origin.key).font(.headline)
                        Text("Continuing discards unsaved input and clears this device's saved sign-in. The invitation is submitted only when you review and join.")
                        Button("Continue with this server") { Task { await selection.confirmInvitation(invitation) } }
                        Button("Cancel", role: .cancel) { selection.cancelInvitation() }
                        if let error = selection.error { Text(error).foregroundStyle(.red) }
                    }
                }.navigationTitle("Invitation")
            }
        }
        .sheet(isPresented: $showingServer) {
            NavigationStack { ServerForm(selection: selection).navigationTitle("Server").toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Done") { showingServer = false } }
            } }
        }
        .confirmationDialog("Discard unsaved input and change server?", isPresented: Binding(
            get: { selection.pendingOrigin != nil }, set: { if !$0 { selection.cancelChange() } })) {
            Button("Discard and change server", role: .destructive) {
                let next = selection.pendingOrigin
                Task { await selection.confirmChange(next) }
            }
            Button("Cancel", role: .cancel) { selection.cancelChange() }
        } message: { Text("Changing server clears this device's saved sign-in.") }
    }
}

private struct ServerForm: View {
    @Bindable var selection: ServerSelection
    @State private var address = ""
    var body: some View {
        Form {
            Section("Self-hosted server") {
                TextField("https://recipes.example.com", text: $address)
                    .keyboardType(.URL).textContentType(.URL).textInputAutocapitalization(.never)
                    .autocorrectionDisabled().accessibilityLabel("Server HTTPS address")
                Button("Use server") { Task { await selection.requestChange(address) } }
                    .disabled(address.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
            }
            if let error = selection.error { Section { Text(error).foregroundStyle(.red) } }
        }.onAppear { address = selection.origin?.key ?? "" }
    }
}
