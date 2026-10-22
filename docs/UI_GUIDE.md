# RbxManager UI/UX Guide

## 1. Design Tokens (`Themes/Tokens.xaml`)
- Spacing: 8px grid (`SpacingTight`: 4, `SpacingBase`: 8, `SpacingMedium`: 16, `SpacingLarge`: 24, `SpacingXLarge`: 32).
- Typography Scale: 12 / 14 / 16 / 20 / 28 (`Caption`, `Body`, `Subtitle`, `Title`, `Display`).
- Fonts: `FontFamilyPrimary` (Segoe UI Variable), `FontFamilyMonospace` (Cascadia Mono, Consolas).
- Zero hex colors in pages; all brushes must reference theme tokens.

## 2. Status Chips
- Reserved vivid colors for states: `Valid` (Green), `Expired` (Orange), `Error` (Red), `Unknown` (Slate), `Running` (Blue).
- Rule: Chip must display BOTH a glyph/icon and text label. Never color alone.
- Contrast ratio must meet or exceed WCAG AA 4.5:1 across both Dark and Light themes.

## 3. High Density & Virtualization
- Compact row height (36px). Subtle borders over heavy drop shadows.
- Virtualization on DataGrids: `IsVirtualizing="True"`, `VirtualizationMode="Recycling"`, `ScrollUnit="Pixel"`.

## 4. Window & Persistence (`UiState`)
- Window dimensions, position, state, and column widths persist under `UiState` in `settings.json`.
- Coordinates are validated on startup against screen bounds (minimum size 960x600; centered on fallback).

## 5. Keyboard Navigation
- `Ctrl+N`: Add via Login. `F5`: Refresh active page. `Ctrl+F`: Focus search.
- `Enter`: Launch selected. `Delete`: Delete selected. `Esc`: Dismiss dialogs.

## 6. How to Add a New Page
1. Create `Views/Pages/NewPage.xaml` using design tokens for padding, margins, and typography.
2. Create `ViewModels/NewViewModel.cs` inheriting `ObservableObject`.
3. Register ViewModel (transient) and Page (transient) in `App.xaml.cs` DI setup.
4. Add `ui:NavigationViewItem` inside `MainWindow.xaml` with localized title and vector icon.
5. All visible user strings must be defined in `Strings.resx` and `Strings.vi.resx` (with full diacritics).
