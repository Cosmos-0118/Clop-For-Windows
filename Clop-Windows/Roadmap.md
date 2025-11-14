# Clop for Windows – Implementation Roadmap

This roadmap mirrors every macOS capability in `Clop/` and sequences the work so Windows feature parity can be achieved with minimal backtracking. Use the checklist to track progress and keep the Windows tree aligned with upstream Swift sources.

## Parity Goals

| macOS capability                                                | Swift reference                                                                | Windows deliverable                                                                                    |
| --------------------------------------------------------------- | ------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------ |
| Clipboard/image optimiser, floating thumbnails, drag/drop zones | `ClopApp.swift`, `ContentView.swift`, `FloatingResult.swift`, `DropZone.swift` | WPF HUD + background agent mirroring live previews and drop targets                                    |
| Video/PDF/image pipeline w/ aggressive presets, metadata rules  | `Video.swift`, `Images.swift`, `PDF.swift`, `OptimisationUtils.swift`          | `src/Core/` media pipeline with identical presets + EXIF rules and multi-process orchestration         |
| Automation: Shortcuts, CLI, services, Finder extension          | `ClopShortcuts.swift`, `InstallCLI.swift`, `FinderOptimiser/`                  | Windows App Service + CLI bridge + Explorer context integration                                        |
| Settings, migrations, support CTA, telemetry                    | `Settings.swift`, `Migrations.swift`, `ClopApp.swift`                          | `%AppData%` settings store, migration helpers, "Buy Me a Coffee" surfaces, Sentry-equivalent telemetry |
| Auto backups, file watchers, upload helpers                     | `Uploads.swift`, `CherryPicks.swift`, `Automation.swift`                       | BackgroundService file watcher with same throttling/backoff semantics                                  |

Treat the left column as the definition of “done” for Windows.

> **Free availability**: Every optimisation feature ships unlocked. The only monetisation surface is an optional "Buy Me a Coffee" link—no trials, paywalls, or feature gating on Windows.

## Windows Solution Layout

```
Clop-Windows/
├─ Roadmap.md
├─ README.md                      # Windows-specific build/run notes
├─ assets/                        # Reused PNGs, GIFs, marketing art
├─ tools/                         # ffmpeg, pngquant, gifski, etc.
├─ docs/
│  ├─ windows-deps.md             # Binary install/licensing instructions
│  └─ architecture.md             # Mapping of Swift modules → C# projects
├─ src/
│  ├─ ClopWindows.sln             # WPF / .NET 8 solution
│  ├─ App/                        # WPF front-end (MVVM, notifications)
│  ├─ BackgroundService/          # Windows Service / packaged agent
│  ├─ Core/                       # Optimisation engine + abstractions
│  ├─ Core/Optimizers/            # Per-format wrappers (ffmpeg, pngquant…)
│  ├─ Integrations/Explorer/      # Shell extension / context menu bridge
│  └─ CliBridge/                  # dotnet CLI for automation & tests
└─ tests/
   ├─ Core.Tests/                 # Unit tests for pipeline + presets
   └─ Integration.Tests/          # Clipboard, drag-drop, and CLI smoke tests
```

## Execution Plan

### Phase 0 – Foundation & Inventory

- [x] **F0.1 – Feature matrix**: Expand `docs/architecture.md` with a table mapping every macOS Swift file to its Windows owner. Note behavioural quirks (e.g., `Defaults` sync, support CTA entry points, floating window animations).
- [x] **F0.2 – Tooling baseline**: Install .NET 8 SDK, Visual Studio 2022 (WPF workload), Windows 11 SDK, and clang/LLVM for native helpers. Record versions + install commands in `docs/windows-deps.md`.
- [x] **F0.3 – Binary audit**: List required third-party binaries (pngquant, jpegoptim, gifski, ffmpeg, libvips, ghostscript). For each, capture command-line args currently used (search `Clop/*.swift`) and Windows equivalents, then plan distribution/licensing notes.

### Phase 1 – Repository Skeleton & Shared Utilities

- [x] **P1.1 – Solution scaffolding**: Folder tree created, `ClopWindows.sln` initialised with `App`, `BackgroundService`, `Core`, `CliBridge`, and `Integrations.Explorer` projects, plus WPF desktop packaging metadata and RID matrix.
- [x] **P1.2 – Shared helpers**: Port `Shared.swift`, `CherryPicks.swift`, `ClopUtils.swift` to C# helpers inside `Core` (e.g., `FilePathExtensions`, `MachPort` → `NamedPipeChannel`). Ensure feature flags and enums (optimisation sources, behaviours, presets) match Swift names for easier diffing.
  - Implemented `Core/Shared/Primitives.cs`, `NumericExtensions.cs`, `StringExtensions.cs`, `NanoId.cs`, `FilePathGenerator.cs`, and `ClopPaths.cs`, plus a `Core/IPC/NamedPipeChannel.cs` bridge and `Core/Processes/ProcessRunner.cs`.
  - Mirrored macOS data sets (crop sizes, device + paper tables) via embedded JSON, added safe filename/templating helpers, and introduced xUnit coverage for the new utilities.
- [x] **P1.3 – Defaults & migrations**: Re-implement `Settings.swift`/`Migrations.swift` using `System.Text.Json` + versioned schema files stored under `%AppData%\Clop\config.json`. Add unit tests covering migrations and default hydration.
  - Added strongly-typed registry (`SettingKey<T>`, `SettingsRegistry`, and `SettingsStore`) plus an app-wide `SettingsHost` so WPF and background components can read/write defaults consistently.
  - Ported macOS migration logic (`SettingsMigrations`, `ClopIgnoreMigration`) and wired it into the new JSON document pipeline so `.clopignore` files are reshaped per media type on first launch.
  - Covered the store with xUnit tests (default hydration, persistence, migration side-effects) and disabled parallel execution to keep the shared `FilePath.Workdir` deterministic.

### Phase 2 – Optimisation Engine

- [x] **P2.1 – Pipeline orchestration**: Translate `OptimisationUtils.swift` + `Optimisable.swift` into a pluggable C# pipeline (request queue, progress reporting, cancellation). Provide interfaces for media-specific optimisers.
  - Added `Core/Optimizers` infrastructure: `OptimisationCoordinator`, `OptimisationRequest/Result`, `ItemType`, and `IOptimiser` contracts backed by `System.Threading.Channels` for multi-worker scheduling.
  - Introduced an execution context that forwards optimiser progress to UI listeners, plus queue status tracking and graceful shutdown semantics.
  - Covered the coordinator with xUnit tests (`OptimisationCoordinatorTests`) verifying success paths, unsupported types, cancellation, and progress notifications.
- [x] **P2.2 – Image pipeline**: Port logic from `Images.swift`, including `pngquant`, `jpegoptim`, EXIF preservation (`copyExif`), Retina downscale options, and crop presets. Validate outputs with golden files in `tests/Core.Tests/Images`. Bundle libvips (or ImageMagick) fallback.
  - Introduced `Core/Optimizers/ImageOptimiser.cs` plus `ImageOptimiserOptions` to encapsulate supported formats, JPEG conversion, metadata rules, and Retina downscale thresholds. The optimiser streams files through `System.Drawing` with progress reporting, preserves EXIF when requested, strips metadata by default, and produces `.clop` outputs only when they beat or enhance the source.
  - Added `Core.Tests/ImageOptimiserTests.cs` covering PNG→JPEG conversions, Retina downscale limits, and metadata preservation to ensure parity behaviours remain stable.
- [x] **P2.3 – Video pipeline**: Mirror `Video.swift` features: ffmpeg progress scraping, FPS caps, Media Engine hardware acceleration (map to Windows Video Acceleration), GIF export via `gifski`. Provide regression tests using sample MOV/MP4 assets.
  - Added `Core/Optimizers/VideoOptimiser.cs` with a configurable `VideoOptimiserOptions` surface, public `IVideoToolchain` contract, ffmpeg progress parsing, GIF export via gifski, and timestamp preservation. Hardware-accelerated encodes default to `d3d11va`/`h264_amf` with software fallback and filter support for resizing, playback-speed changes, and audio stripping.
  - Created `VideoOptimiserTests` with a fake toolchain to verify fps caps, metadata overrides, and GIF conversion semantics, ensuring the coordinator can process MOV/MP4-style inputs without requiring the real binaries during CI.
- [x] **P2.4 – PDF pipeline**: Reproduce `PDF.swift` Ghostscript command set, progress parsing, and metadata stripping toggles. Document Ghostscript licensing requirements in `docs/windows-deps.md`.
  - Added `Core/Optimizers/PdfOptimiser.cs` plus injectable `IPdfToolchain` so the coordinator can shell out to Ghostscript with the same lossy/lossless stacks, metadata stripping hooks, and timestamp preservation as macOS.
  - Introduced `PdfOptimiserOptions` for configuring Ghostscript paths, font search directories, and size/metadata policies, with sane defaults sourced from `%LOCALAPPDATA%` + bundled binaries.
  - Created `Core.Tests/PdfOptimiserTests.cs` using a fake toolchain to cover success paths, metadata overrides, and invalid PDF handling without requiring the real binary.
  - Expanded `docs/windows-deps.md` with explicit Ghostscript licensing guidance (AGPL vs commercial Artifex license) so release engineering can choose a compliant distribution path.
- [x] **P2.5 – CLI parity**: Build `CliBridge` commands analogous to macOS CLI (optimize clipboard, files, directories, automation hooks). Ensure command names/flags match for portability.
  - Implemented a System.CommandLine-driven `clop optimise` entry point that mirrors macOS flags (types/include/exclude, recursive, aggressive, playback-speed, JSON, progress, clipboard/UI toggles) and streams work through the existing optimisation coordinator.
  - Added guard rails & warnings for mac-only flags (GUI, clipboard copy, adaptive optimisation) so scripts can pass the same arguments without breaking.
  - Introduced a dedicated `CliBridge.Tests` project with unit coverage for `TypeFilter` and `TargetResolver`, ensuring include/exclude tokens and recursion behave exactly like the Swift CLI.

### Phase 3 – Background Agents & Automation

- [x] **P3.1 – Clipboard watcher**: Implement a background agent (Worker Service with tray hook or packaged desktop task) that watches clipboard changes, honours pause states, and invokes the pipeline, mirroring `ClopApp.swift` clipboard logic.
- [x] **P3.2 – File/directory watchers**: Port `Uploads.swift` + `Automation.swift` semantics (directory allow-lists, size caps, concurrency limits). Use `FileSystemWatcher` with debounce logic equivalent to `EonilFSEvents`.
- [x] **P3.3 – Automation hooks**: Provide App Service endpoints / named pipes for Windows Shortcuts, Power Automate, and Explorer context menu triggers, matching macOS Shortcut identifiers.
- [x] **P3.4 – Finder-equivalent integration**: Create Explorer context menu or Share contract extension mirroring `FinderOptimiser`. Ensure drag/drop promises behave like macOS NSFilePromiseReceiver flows.

### Phase 4 – WPF Experience

- [x] **P4.1 – Floating HUD**: Recreate the SwiftUI floating results (`FloatingResult.swift`, `CompactResult.swift`) as WPF windows with acrylic/compact styling (Win32 composition brushes or WinAppSDK interop where useful). Include progress bars, preview thumbnails, and quick actions.
- [x] **P4.2 – Main shell**: Implement Settings, Onboarding, and Compare views (feature toggles, aggressive optimisation switches, preset editors) in WPF MVVM, mirroring `SettingsView.swift`, `CompareView.swift`, `Onboarding.swift`.
  - Implemented navigation rail, onboarding tour, compare intake, and settings panels bound to `SettingsHost` with DI wiring.
- [x] **P4.3 – Keyboard + gesture support**: Map hotkeys (`SauceKey` equivalents) to Windows accelerator keys and global shortcuts, keeping defaults in sync. Document interplay with Windows input APIs.
  - Expanded `ShortcutCatalog` into a settings-backed registry with dynamic bindings, remapping helpers, and conflict detection, plus live re-registration in `KeyboardShortcutService`.
  - Surfaced in-app and global shortcuts inside Settings with capture/clear/reset UI and a shortcut recording dialog so users can rebind keys without touching JSON.
  - Documented drop-zone pointer gestures (Alt to reveal overlay, Ctrl for preset zones, right-click for HUD context menus) alongside keyboard coverage in `docs/architecture.md`.
- [ ] **P4.4 – Localization & accessibility**: Ensure UI texts reuse the macOS strings and pass Windows accessibility checks (High Contrast, screen readers).

### Phase 5 – Support CTA, Telemetry, Packaging

- [ ] **P5.1 – Support CTA**: Replace the legacy Pro button with a "Buy Me a Coffee" link in the tray menu, Settings, and onboarding screens. All optimisation features stay free; the CTA is a simple browser link / WebView wrapper with zero gating logic.
- [ ] **P5.2 – Telemetry**: Integrate Sentry or Azure App Center equivalent, mirroring events emitted in `ClopApp.swift`. Offer opt-in controls consistent with macOS preferences.
- [ ] **P5.3 – Installers & updates**: Produce MSIX/App Installer packages, optional winget manifest, and scriptable install (PowerShell). Include automatic update strategy akin to Sparkle.
- [ ] **P5.4 – QA matrix**: Draft test checklist (multi-monitor, HDR, touch, battery saver, offline). Automate core scenarios via WinAppDriver / Playwright where feasible.

### Phase 6 – Release Readiness

- [ ] **P6.1 – Docs & onboarding**: Update `Clop-Windows/README.md` with build/run steps, screenshots, and troubleshooting. Add migration guide for macOS users switching platforms.
- [ ] **P6.2 – Feature lock + regression pass**: Run parity tests comparing macOS outputs vs Windows outputs on the same sample set (images, videos, PDFs). Store diff results in `tests/fixtures/results`.
- [ ] **P6.3 – Release packaging**: Publish MSI/MSIX artifacts, update `Releases/appcast.xml` (or Windows-equivalent feed), and append Windows notes to `ReleaseNotes/`.

Each task references the macOS source of truth; check it first, then reproduce behaviour in the Windows project. Track progress by checking the boxes above and linking commits/issues for traceability.
