# Recipe Manager web design system

Status: authoritative for all new and modified web UI. The owner's supplied recipe-library
reference sets the visual direction. This document, the shared tokens in `src/styles.scss`, and
the shared shell take precedence over ad hoc feature styling. Update them together when making
an intentional design-system change. The native app keeps its platform conventions; this spec
is for the Angular web client.

## Visual direction

A quiet, photo-led household cookbook: a persistent navigation rail, generous working area,
compact recipe cards, softly rounded surfaces, restrained borders and a small raspberry accent.
Dark mode follows the reference's midnight canvas, slate sidebar and bright food photography.
Light mode uses the same geometry with a cool off-white canvas and white surfaces. Neither mode
changes information architecture, density, capabilities or the order of actions.

Keep the Recipe Manager identity and actual household data. The reference is inspiration, not
an instruction to reproduce unavailable nutrition scores, calorie counts, drag handles, timers
or total counts. Show only supported actions and authoritative data. Never invent sample recipes
or images in the production app to make the grid appear populated.

## Themes and preferences

- Offer **System**, **Light** and **Dark** through the always-visible **Appearance** control.
- Default to System. Follow OS changes immediately while System is selected; an explicit choice
  must stay selected when the OS changes.
- Persist the preference under `recipe-manager.theme` on this browser. Share changes between
  tabs through the storage event. The preference is local device presentation, not a household setting.
- Apply an explicit saved preference before the first painted frame through `public/theme-init.js`.
  CSS supplies the system palette before Angular starts. Storage failures must not stop rendering
  or prevent switching themes for the current session.
- Apply palette changes atomically; do not animate backgrounds across a theme change while text changes immediately.
- Set `color-scheme` for native controls. Every feature, validation state and empty/loading state
  must work in both modes. Do not add a light-only panel to a dark page or invert photos.

## Foundation tokens

Use semantic CSS custom properties. Only the shared palette defines literal UI colors; component
styles reference tokens. Theme-independent photo overlays and collection swatches also have tokens.

| Token | Light | Dark | Purpose |
|---|---|---|---|
| `--canvas` | `#f5f6fa` | `#0c111c` | Page background |
| `--sidebar` | `#ffffff` | `#111827` | Navigation rail |
| `--surface` | `#ffffff` | `#171e2a` | Cards, editors and panels |
| `--surface-raised` | `#e9edf5` | `#202b3d` | Selected rows, secondary buttons |
| `--field` | `#f6f7fb` | `#111827` | Form controls and quiet insets |
| `--border` | `#dce1ea` | `#2b3547` | Decorative separators |
| `--border-strong` | `#8290a5` | `#69788f` | Input boundaries and hover borders |
| `--text` | `#1b2638` | `#edf0f6` | Primary text |
| `--muted` | `#58677d` | `#a0adc0` | Secondary text and metadata |
| `--accent` | `#b92349` | `#ff7189` | Links, favourites and selected checkboxes |
| `--accent-soft` | `#fce8ed` | `#372331` | Accent-tinted supporting surface |
| `--primary` | `#172338` | `#f7f8fc` | Main action fill |
| `--on-primary` | `#ffffff` | `#111827` | Main action text |
| `--focus` | `#215fc4` | `#87baff` | Keyboard focus indicator |
| `--success` / `--success-bg` | Green pair | Green pair | Accepted states |
| `--warning` / `--warning-bg` | Amber pair | Amber pair | Review/conflict states |
| `--danger` / `--danger-bg` | Red pair | Red pair | Destructive actions and errors |
| `--placeholder` | `#e8edf2` | `#202d3b` | Missing recipe photography |

Radii: `--radius-sm: 8px` for controls, `--radius-md: 12px` for selected navigation and fieldsets,
`--radius-lg: 20px` for cards and large panels. Avoid pill-shaped ordinary buttons. Use pills for
short badges. Shadows are subtle; borders establish hierarchy in dark mode.

Spacing uses a 4px base: 4, 8, 12, 16, 24, 32 and 48px. Prefer layout gaps over margins on every
child. Use 16–24px card padding, 16–24px grid gaps and 16–48px responsive page padding.

Typography uses the local system sans-serif stack, with 16px body text and 1.5 line height.
Page titles are 24–32px/1.2; card titles 15–16px/1.4; supporting text 14px; compact recipe metadata
at least 12px. Uppercase section labels may be smaller when decorative navigation grouping,
with increased letter spacing. Wrap long titles; never force horizontal page scrolling.

## Application shell

Desktop, above 960px:

- A fixed 264px sidebar (`--sidebar-width`) runs the viewport height. Its own contents scroll.
- The 72px topbar (`--topbar-height`) contains the current area and Appearance control.
- Main content sits beside the rail, centered within a maximum 1400px working area including padding.
- Sidebar order: brand, global recipe search, Recipes, Favourites, household collections,
  Meal Planner, Shopping Lists, Ingredients, Settings, and a persistent organiser management link.
- List household collections alphabetically until explicit ordering is supported.
- Collection chips use the six shared swatch tokens, assigned by displayed order. Their colors are
  decorative; collection names and selected-row styling carry meaning. Do not imply a saved custom
  color or drag ordering until those are supported features.
- The selected destination has a raised surface, stronger text and `aria-current="page"`.
  Navigation collections and favourites use real query parameters and server filtering.

At 960px and below, replace the fixed rail with a menu button and navigation drawer. The drawer
has a close button, Escape dismissal, contained keyboard focus, an inert background and a backdrop.
Its links remain the same. Restore focus to the menu button when dismissed. On desktop resize,
close the mobile drawer. Never squeeze a desktop sidebar into a 320px screen.

Keep a keyboard-visible **Skip to content** link. The brand is navigation, not an extra `h1`.
Each routed page owns its meaningful main heading.

## Page anatomy and controls

Use `.page-header` for a title, optional supporting text and an `.actions` group. Put the main
creation or save action first in visual prominence, using `.primary`; supporting actions use the
raised surface, and links handle navigation. Destructive actions have a text label and danger
color, with the existing confirmation behavior retained.

Use `.card` for related editing content. Do not wrap every line of text in a card. Forms keep
visible labels above controls, 44px minimum control height and `--border-strong` boundaries.
Use fieldsets/legends for related inputs and checkboxes. Supporting hints use `--muted`; errors
must include explanatory text and an alert association, not just a color change.

Use the shared `app-icon` for line icons: consistent 24px viewBox, 1.7px rounded strokes, usually
20px rendered size. Decorative icons are hidden from assistive technology. Icon-only controls
need an accessible name and a 44×44px target. Do not substitute emoji or platform-dependent text
glyphs for product icons. Use one primary action per local decision where practical.

## Recipe library

- Place the collection/library title and truthful loaded-result count in a large surface header,
  with New recipe and Import recipe actions.
- Follow with a wide search control and discoverable archive, favourite and organiser filters.
  Keep these functional on small screens; do not hide capability for visual similarity.
- Use four columns on wide desktops, three below 1312px, two below 672px, and one below 400px.
  The layout must handle very long titles and missing photos.
- Each card contains a 4:3 photo, a 44px favourite control in the top-right corner, linked title,
  available preparation/cooking time and serving count. Preserve photo proportions with `object-fit`.
- Use a consistent dish illustration and quiet caption when a photo is missing. Do not fetch
  remote stock photos on behalf of users. A placeholder is not recipe content.
- Favourite state uses both filled/outline hearts and `aria-pressed`; do not depend on color alone.
- Do not show calories or scores until the backend supplies real, defined values. If pagination
  remains, describe loaded counts honestly rather than claiming a total library size.
- Preserve the existing recipe detail, edit, import, organiser and shopping actions. This first
  layout pass does not change ingredient calculations, source data, ETags or SignalR behavior.

## State and accessibility requirements

Target WCAG 2.2 AA in both modes. Normal text needs 4.5:1 contrast; meaningful non-text controls
and large text need 3:1. Decorative separators and disabled controls are not the only way to
identify an action. The focus ring is 3px with a 3px offset and must not be clipped.

Provide loading, empty, error, success, disabled and selected states. Preserve typed drafts and
focus during asynchronous changes; never move focus after the user has moved to another field.
Retain status/alert roles, meaningful link text, keyboard form submission and reduced-motion support.
No essential interaction may depend only on hover. No page may overflow horizontally at 320px.
A desktop hover effect is a subtle border/shadow change, not a moving/scaling card.

## Feature delivery checklist

Every future web feature or UI change must:

1. Read this spec and reuse the shared shell, tokens, control styles and icon component.
2. Keep business behavior and accessible labels intact while updating presentation.
3. Verify light, dark, persisted choice and System behavior when changing theme foundations.
4. Check desktop and 320px layouts, keyboard operation, long content and empty/error states.
5. Run appropriate web checks and accessibility/browser tests. Use synthetic data for screenshots.
6. Document any deliberate new pattern here, including its states and theme tokens, rather than
   introducing a one-off feature palette.

## Reference implementation previews

These previews use synthetic test recipes and the missing-photo fallback. Uploaded recipe photos
fill the same 4:3 cover areas. Refresh these examples through `tests/e2e/web-design.spec.ts` when
intentionally changing the shared design.

| Theme | Desktop | Mobile |
|---|---|---|
| Dark | [Preview](previews/recipes-dark-desktop.png) | [Preview](previews/recipes-dark-mobile.png) |
| Light | [Preview](previews/recipes-light-desktop.png) | [Preview](previews/recipes-light-mobile.png) |

## Initial implementation scope

The initial pass delivers the shared shell, functional sidebar collection/favourite search,
light/dark/system appearance, reusable line icons, recipe browse grid and theme-safe foundations
across the existing forms, planner, ingredient catalogue, shopping and settings screens.
Subsequent page-specific layout refinements should use this system and preserve existing functionality.
