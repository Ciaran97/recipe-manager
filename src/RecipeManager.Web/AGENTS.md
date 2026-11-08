# Web UI requirements

For every web feature or UI change, read and follow [the web design system](../../docs/design/web-design-system.md).
It is the owner's required visual specification for current and future features.

- Reuse the shared shell, semantic CSS tokens, global controls and `app-icon` component.
- Support Light, Dark and System appearance. Do not put literal theme colors in feature styles.
- Preserve backend-owned calculations, API contracts, ETags, SignalR behavior and existing workflows.
- Preserve accessible labels, keyboard operation, visible focus, draft retention and 44px targets.
- Validate appropriate mobile/desktop and light/dark states; never invent counts, nutrition data or functionality for visual polish.
- Update the design spec when intentionally adding a reusable pattern.
