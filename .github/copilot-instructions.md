---
applyTo: '**'
---
# MED - Project Context

## Project Overview
MED is a standalone WPF emoji picker for Windows 10 x64 with Russian and English search. Windows 10 x64 is tested; Windows 11 has not been tested and compatibility is not guaranteed.

## Current Development Status
See Project README.md

## Design Goals
1. Must match Windows 10 emoji picker functionality as 1:1 as possible
2. Ship a self-contained executable that needs nothing installed beyond what Windows 11 already provides (the emoji font). Library dependencies (Emoji.Wpf, VirtualizingWrapPanel) are bundled into the build, not required on the machine

## Technical Stack & Decisions
- **Framework**: WPF with .NET 10 (runtime bundled in self-contained builds)
- **Language**: C# with modern language features
- **UI**: XAML with custom styling to match Windows 10 design
- **Emoji Rendering**: Fluent 3D 64 px embedded PNG, asynchronous bounded LRU cache; Emoji.Wpf catalog/system fallback
- **Target Platform**: Windows 10 x64, .NET 10; background resident, tray icon hidden by default; settings/exit available from panel gear
- **Packaging**: Self-contained publish, packaged by Inno Setup (`installer/EmojiPicker.iss`)

## Code Architecture
- **App.xaml.cs**: Tray host - single-instance mutex, `NotifyIcon` menu, installs the hook, applies the theme, owns the resident lifecycle (`ShutdownMode.OnExplicitShutdown`)
- **HotkeyListener.cs**: `WH_KEYBOARD_LL` hook; on Win+. it captures the foreground window, focused child control, and text caret rectangle, raises an event, and swallows the key so the built-in panel is suppressed
- **MainWindow.xaml.cs**: `Emoji`/`EmojiCategory` classes and picker logic; `ShowPicker()` anchors to the captured text caret (mouse-pointer fallback) and force-foregrounds (AttachThreadInput); selection keeps the reused window open; Esc/outside click hides it. The emoji grid is UI-virtualized (VirtualizingWrapPanel); search is debounced and matches names + keyword tags with name matches ranked first
- **Emoji Data**: Full Unicode set from Emoji.Wpf's `EmojiData.AllGroups`, mapped to the seven Win10 categories in `GroupToCategory`
- **Insertion**: `TextInjector.cs` puts the emoji into the previously focused window. Simple emoji are typed (SendInput/KEYEVENTF_UNICODE); joined graphemes (ZWJ/flag/skin-tone) are pasted (clipboard + Ctrl+V, previous text restored) because keystrokes split them in some apps. Mode is configurable via `Settings` (hybrid/paste/keystroke); refuses insertion when there is no safely validated target
- **Theming**: `ThemeManager.cs` merges `Theme/Light|DarkTheme.xaml` per the Windows setting and swaps live on `SystemEvents.UserPreferenceChanged`; XAML uses `DynamicResource` brushes
- **Settings**: `Settings.cs` - user prefs in the `AppDataPaths`-selected MED directory (`%APPDATA%\MED` installed, local `Data` portable) (`emojiInsertMode`), loaded once at startup
- **Recent Emojis**: Persisted as `recent.json` under `AppDataPaths.DirectoryPath`
- **Keyboard**: Window-level PreviewKeyDown - arrows move selection, Tab/Shift+Tab switch category, Enter commits, ESC dismisses; focus lives in the search box
- **WinForms interop note**: `UseWindowsForms` is on for the tray icon, but the WinForms implicit global using is removed in the csproj to avoid clashing with WPF types; import `System.Windows.Forms` explicitly where needed

## Development Environment
- Primary validation platform: Windows 10 x64. Windows 11 is untested.
- Project targets: .NET 10 (modern SDK-style project)
- Building with: .NET SDK 10.0.401 CLI workflow

## Key Design Principles
- **Simplicity**: Zero bloat, just emoji picking functionality
- **Windows 10 Fidelity**: Match original design exactly
- **Performance**: Lightweight, fast startup and search
- **Standalone**: No system modifications, easy to install/uninstall

## Development Context
- This project was created to replace Windows 11's emoji picker which became bloated
- User specifically wanted Windows 10 design with no additional features
- No fonts are bundled; Fluent PNG are primary, with system Segoe UI Emoji fallback via Emoji.Wpf
- Focus is on clean, functional implementation over feature richness

## When Providing Assistance
- Follow the user's requested language; user-facing documentation is Russian first, then English
- Prioritise performance and simplicity over features
- Maintain focus on core emoji picking functionality
- Respect the "no bloat" philosophy
- Think about memory usage and startup time impact
- Code format must abide by `.editorconfig` (run `dotnet format`)
- Do not add "// FIXED" comments to code
- Do not stray from the users request or make changes that were not asked for
- MED repository: https://github.com/Goblincheg/MED; upstream: https://github.com/platima/Classic-EmojiPicker

## File Dependencies
- **Segoe UI Emoji**: System font used for rendering (ships with Windows 10 1809+ / Windows 11)
- **Development environment**: .NET SDK 10.0.401 CLI or an IDE with .NET 10 support
- **.NET 10 (and SDK)**: Must be installed (may need separate download)
