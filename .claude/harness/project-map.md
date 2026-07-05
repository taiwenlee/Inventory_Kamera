# Project Map — Inventory Kamera

Orientation for subagents and fresh sessions — read this instead of re-scanning the repo.
Surveyed 2026-07-03 on branch `modernize/phase0-foundation` (HEAD e9a60a2 + §6c WIP). Facts
only. If something here misleads you, fix it (FREE tier, `reflection.md` §1) with evidence.

## 1. Top-level layout

- `InventoryKamera/` — the app (WinForms, `net8.0-windows7.0`, SDK-style csproj, auto-globbed)
- `tests/InventoryKamera.Tests/` — xUnit suite (~121 tests green as of plan §0)
- `inventorylists/` — runtime lookup JSON (characters/weapons/artifacts/materials)
- `.github/workflows/` — `build.yml` (push/PR build+test), `release.yml` (tag-driven publish)
- `MODERNIZATION_PLAN.md` — the project ledger (105 KB — grep headings, read sections)

## 2. InventoryKamera/ subdirectories

**game/** — domain models + game interaction
- `Character.cs`, `Weapon.cs`, `Artifact.cs` — scan-result models (GOOD-oriented)
- `Navigation.cs` — static facade: game-window detection, screen capture (GDI
  `CopyFromScreen`), keyboard/mouse simulation, inventory-screen selection
- `GameController.cs` — (WIP §6c) ViGEmBus virtual Xbox 360 controller wrapper;
  `IsAvailable`/`FailureReason` for driver detection
- `ControllerSpike.cs` — §6c feasibility spike (connect, nudge stick, tap A)
- `ControllerNavigationTests.cs` — (WIP §6c) manual test routines run from MainForm's Options
  menu; deliberately OUTSIDE Designer files (the pattern to copy for new UI-triggered logic)

**scraping/** — the OCR pipeline
- `InventoryScraper.cs` — abstract base: page navigation, grid-item detection
  (`ProcessScreenshot`/`GetPageOfItems` — known intermittent grid-detection gaps; §6c exists
  to sidestep, do NOT invest in detection heuristics)
- `CharacterScraper.cs` / `WeaponScraper.cs` / `ArtifactScraper.cs` / `MaterialScraper.cs`
- `OcrService.cs` (+ `IOcrService`) — Tesseract engine pool; `AnalyzeText*` with confidence
- `ImageProcessor.cs` (+ `IImagePreprocessor`) — preprocessing for OCR
- `ImageProcessing.cs` — static pixel ops (Kirsch edges, blob counting — the pure
  System.Drawing reimplementation that replaced Accord, parity-tested)
- `GenshinProcesor.cs` — static lookup dictionaries + validity checks; `ReloadData()` per scan
- `LookupService.cs` — pure validity functions over supplied dictionaries
- `ScanSettings.cs` (+ `IScanSettings`) — live pass-through over `Properties.Settings.Default`
- `ScanViewModel.cs` (+ `IScanProgressReporter`) — observable scan state (counters, status,
  errors, gear/material display). Character-display group intentionally NOT converted — stays
  on static `UserInterface` pending §6c (memory `character-scan-revamp-plan`)
- `TextNormalizer.cs` — OCR text cleanup/matching

**data/** — orchestration + persistence
- `InventoryKamera.cs` — orchestrator; `GatherData()` (data/InventoryKamera.cs:129) drives:
  reload lookups → spawn Channel-based image workers → per-category scrape loop → await
  workers. Cooperative cancel via `CancelRequested` flag (no Thread.Abort on net8)
- `GOOD.cs` — GOOD-format export model; `WriteToJSON()`
- `DatabaseManager.cs` — downloads/refreshes `inventorylists/` from the AnimeGameData2 source
  ("Update Lookup Tables" menu)
- `OCRImageCollection.cs` — queued screenshot unit for workers

**ui/** — WinForms layer
- `main/MainForm.cs` + `.Designer.cs` + `.resx` — primary window; scan start/stop hotkeys;
  Designer file is HAZARDOUS (build-and-verify.md §5; resx LogicalName pinned in csproj)
- `main/MainUI.cs` + `.Designer.cs` — secondary/legacy form
- `SettingsForm.*` — Advanced Settings modal (moved out of MainForm in §3.0)
- `ExecutablesForm.*` — target-exe picker; `OcrCorrectionForm.cs` — low-confidence OCR override
- `UiTheme.cs` — palette (cream/terracotta), dark mode, `RoundCorners`, title-bar tinting —
  TASTE DOMAIN (judgment.md §5)
- `FlatGroupBox.cs` — owner-drawn flat GroupBox
- `UserInterface.cs` — static UI bridge (legacy; being replaced by ScanViewModel except
  character display)

**Properties/** — `Settings.cs` + `JsonUserSettingsProvider.cs` (settings persist to JSON, not
.config) + generated `Settings.Designer.cs`/`Resources.Designer.cs` (never hand-edit generated)

**tessdata/** — Tesseract trained data, copied to output via csproj `<None Update>` pins

## 3. Scan pipeline in one paragraph

MainForm start (button/hotkey) → scanner thread → `InventoryKamera.GatherData()` → per
category: `Navigation` selects the in-game screen → scraper pages through and queues
`OCRImageCollection`s → Channel workers preprocess (`ImageProcessor`) + OCR (`OcrService`) +
normalize (`TextNormalizer`) + validate (lookups) → models filled → `GOOD.WriteToJSON()` →
optimizer dialog. Progress/errors flow through `ScanViewModel` events to the UI.

## 4. Dependencies (csproj `PackageReference` block — the full authoritative list)

Tesseract 5.2.0, InputSimulator 1.0.4 (unmaintained, NU1701 compat-shim — watch), 
Nefarius.ViGEm.Client (§6c controller), Newtonsoft.Json, NLog, Octokit, NHotkey.WindowsForms,
HtmlAgilityPack.NetCore, Microsoft-WindowsAPICodePack-Shell, System.Configuration.ConfigurationManager.
No new packages without user approval.

## 5. Test project (`tests/InventoryKamera.Tests/`)

xUnit; covers models (Artifact/Weapon/Character), LookupService, GOOD serialization,
OcrService (real Tesseract round-trips — tessdata linked in test csproj), ImageProcessor +
Accord-parity golden tests (Kirsch/blob), TextNormalizer, ScanSettings live-forwarding,
ScanViewModel state/concurrency, manequin entries. Canonical run command:
build-and-verify.md §2.
