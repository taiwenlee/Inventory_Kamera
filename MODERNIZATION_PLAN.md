# Inventory Kamera — Modernization Plan

> Status: Proposed · Scope: full phased modernization (foundation → efficiency → UX)
> Author: drafted 2026-06-28 · Target: incremental, branch-per-phase, `master` stays releasable throughout

---

## 1. Current state (baseline)

| Area | Today | Notes |
|---|---|---|
| Runtime | .NET Framework **4.7.2** | Windows-only, requires VC++ redist + framework on user machine |
| Project format | Legacy non-SDK `.csproj` (278 lines, `ToolsVersion=15.0`) | Manual `<Compile Include>` lists, `packages` via PackageReference |
| UI | WinForms — `MainForm.cs` (671) + `MainForm.Designer.cs` (1,347) | Logic-heavy code-behind, static `UserInterface` holds control refs |
| OCR / CV | Tesseract 5.2, **Accord.Imaging 3.8** | Accord is **archived/abandoned (2020)** — top tech-debt risk |
| Automation | InputSimulator, NHotkey, `System.Drawing` / GDI | Inherent Win32 dependency — keep |
| Concurrency | raw `System.Threading.Thread` + `static Queue<OCRImageCollection>` workerQueue | `volatile bool b_threadCancel`, no `CancellationToken`, no `async` |
| Core engine | `GenshinProcesor` — **957-line `static` class** | Holds OCR engines + all lookup dictionaries; global state, untestable |
| Config | `Properties.Settings` / `Settings.settings` + custom JSON provider | Legacy settings model |
| Data | Newtonsoft.Json; `Dictionary<string,JObject>` for characters/artifacts | Dynamic JSON shapes |
| Tests | **None** | No test project |
| CI/CD | **None** (no `.github/workflows`) | Releases built by hand; `AssemblyVersion` bumped in commits |

**Hard constraint:** the app fundamentally depends on Win32 screen capture + input injection + OCR. It stays a **Windows desktop app**. Web/cross-platform rewrite is out of scope and not feasible. WinForms-on-modern-.NET is the pragmatic target.

### Representative tech-debt examples (grounded)
- **`data/InventoryKamera.cs:140-226`** — "manequin" character support is implemented by reading `characters.json` as text, `initialJson.Remove(initialJson.Length - 3, 3)` to chop the trailing `}`, then string-concatenating hand-built JSON and re-saving. Fragile, runs inside a `catch`, and recursively calls `GatherData()`.
- **`scraping/GenshinProcesor.cs`** — 957 lines, fully `static`, owns the Tesseract engine pool (`numEngines = 8`), all lookup dictionaries, image preprocessing, and text normalization in one unit.
- **`data/InventoryKamera.cs:28`** — `public static Queue<OCRImageCollection> workerQueue;` shared mutable global; producer/consumer coordination via a sentinel `OCRImageCollection(null, "END", 0)` enqueue (`:272`).
- **`ui/UserInterface.cs`** — `static` class wired to live WinForms controls (picture boxes, labels), so scan logic writes directly into UI widgets. Hard UI/logic coupling.
- Worker count chosen by a magic `switch` on `ScannerDelay` (`InventoryKamera.cs:60`), not machine capability.

---

## 2. Guiding principles

1. **Always releasable.** Each phase is a branch that builds, runs, and scans before merge. No long-lived rewrite branch.
2. **Characterize before refactor.** Add tests that pin current behavior *before* changing the code they cover.
3. **Lowest-risk framework path:** stay WinForms, move to .NET 8. Don't rewrite the UI to WPF/web — the value is the OCR pipeline, not the chrome. UX gains land incrementally inside modernized WinForms.
4. **Delete dead weight** (Accord, legacy settings, hand-rolled JSON) rather than port it.
5. **Reversible commits.** Small PRs, each with a clear rollback.

---

## 3. Phase 0 — Foundation

**Goal:** modern toolchain and a safety net. No behavior change.

> **Sequencing decision (updated during Phase 0):** the initial attempt to retarget to
> `net8.0-windows` in this phase revealed that **Accord.Imaging 3.8 is a hard *compile* blocker**
> on modern .NET — it was built against the abandoned `CoreCompat.System.Drawing` fork, so its
> `UnmanagedImage`/filter APIs will not accept the in-box `System.Drawing.Bitmap` the rest of the
> app uses. The net8 migration and the Accord replacement are therefore **coupled** and cannot be
> separated. Phase 0 was re-scoped to convert to an SDK-style project **while staying on net472**
> (zero behavior change, builds today); the TFM flip to net8 moves into Phase 1 alongside the
> Accord replacement, gated by the §1.6 benchmark harness.

### 0.1 Convert to SDK-style project ✅ done
- Replaced legacy `InventoryKamera.csproj` with SDK-style (`<Project Sdk="Microsoft.NET.Sdk">`). Dropped explicit `<Compile Include>` lists (globbing), `BootstrapperPackage`, ClickOnce publish props, and the dead framework polyfill packages.
- Kept `ApplicationIcon`, `ApplicationManifest` (`app.manifest`), `StartupObject`, embedded `.resx`, and `tessdata` as `CopyToOutputDirectory` content.
- Added `Microsoft.NETFramework.ReferenceAssemblies` so it builds headlessly with just the .NET SDK (no VS/targeting pack), and `System.Resources.Extensions` + `GenerateResourceUsePreserializedResources` for the WinForms image resources.
- Pinned `MainForm.resx`'s manifest name via `LogicalName` (its namespace `InventoryKamera` doesn't match its `ui/main` folder, which would otherwise break resource loading under the SDK's path-based naming).

### 0.2 Retarget to .NET 8 — deferred to Phase 1 (coupled with Accord removal)
- `<TargetFramework>net8.0-windows</TargetFramework>`, `<UseWindowsForms>true</UseWindowsForms>`.
- Enable `<Nullable>enable</Nullable>` (warnings first, not errors) and `<LangVersion>latest</LangVersion>`.
- Known breaks already surfaced by the trial build (to fix during the flip): `MethodInvoker` now ambiguous with the new `System.Reflection.MethodInvoker`; Tesseract 5.x needs `Bitmap`→`Pix` conversion; `Thread.Abort` unsupported — see 0.4.

### 0.3 Verify build + run parity
- `dotnet build` and `dotnet run` clean.
- Manual smoke scan against the game (or recorded screenshots) to confirm OCR + export unchanged.
- Confirm `tessdata` traineddata files copy to output and load.

### 0.4 Address .NET 8 incompatibilities (known)
- `Thread.Abort` / `ThreadAbortException` (caught in `MainForm`/`InventoryKamera`) is **not supported** on modern .NET → replace with cooperative cancellation (preview of Phase 1 §1.2; minimal shim here).
- Audit `Octokit`, `NHotkey.WindowsForms`, `InputSimulator`, `Microsoft-WindowsAPICodePack-Shell`, `HtmlAgilityPack.NetCore` for net8 support; replace `Microsoft-WindowsAPICodePack-Shell` file dialogs with `Microsoft.WindowsAPICodePack-Shell` successor or WinForms `OpenFileDialog`/`FolderBrowserDialog`.

### 0.5 Single-file self-contained publish
- `dotnet publish -r win-x64 -p:PublishSingleFile=true --self-contained`.
- **UX payoff:** removes the README's "install VC++ redist + restart" and ".NET Framework" prerequisites — users download one `.exe`.

### 0.6 Test project
- Add `InventoryKamera.Tests` (xUnit). Characterization tests for **pure logic only**:
  - OCR text normalization / fuzzy-match in `GenshinProcesor` (stat keys, element names, name correction).
  - GOOD serialization round-trip (`data/GOOD.cs`).
  - Lookup/database parsing (`data/DatabaseManager.cs`).
- These lock behavior before Phase 1/2 refactors touch them.

### 0.7 CI
- `.github/workflows/build.yml`: restore → build → test on push/PR.
- `release.yml`: on tag, publish single-file artifact + attach to GitHub Release. Replaces manual `AssemblyVersion` bump commits with a tag-driven flow (use `MinVer`/`Nerdbank.GitVersioning` or a simple `<Version>` from tag).

**Exit criteria:** SDK project on net8.0-windows, builds in CI, smoke-scan parity confirmed, single-file publish works, tests green.

---

## 4. Phase 1 — Efficiency & dependency modernization

**Goal:** kill dead dependencies, modernize the concurrency model, measurable scan throughput.

### 1.1 Replace Accord.Imaging
- Swap to **OpenCvSharp4** (richest CV API; good for template matching / thresholding) or **ImageSharp** (pure-managed, simpler licensing) depending on which Accord filters are used.
- Inventory the exact Accord calls first; wrap image preprocessing behind an `IImagePreprocessor` interface so the swap is localized and testable.

### 1.2 Replace thread/queue model with Channels + async
- Producer (screen capture) / N consumers (OCR) over `System.Threading.Channels.Channel<OCRImageCollection>`.
- Replace `volatile bool b_threadCancel` and the sentinel `"END"` enqueue with a single `CancellationToken` + channel completion.
- Workers become `Task`s; `await` instead of `Thread.Join`. Removes the `static workerQueue` global.

### 1.3 Right-size parallelism
- Derive worker/engine count from `Environment.ProcessorCount` (and an optional user cap), not the `ScannerDelay` magic switch.
- Pool Tesseract engines via a proper bounded `ObjectPool<TesseractEngine>` sized to worker count.

### 1.4 Selective System.Text.Json migration
- Move GOOD export and simple key/value lists to `System.Text.Json` (faster, no extra dep).
- **Keep Newtonsoft** where dynamic `JObject` shapes are used (`Dictionary<string,JObject> Characters, Artifacts`) — convert opportunistically to typed models in Phase 2, not here.

### 1.5 Kill the manequin JSON-string hack
- Replace `InventoryKamera.cs:140-226` text-surgery with proper deserialize → mutate object model → serialize. Removes the recursive `GatherData()` retry.

### 1.6 Benchmark
- Add a repeatable benchmark (recorded screenshot set → scan → timing) so throughput gains from 1.1–1.3 are measured, not assumed. Capture before/after numbers.

**Exit criteria:** Accord removed, async pipeline with `CancellationToken`, engine pool, benchmark shows ≥ parity (target improvement), tests still green.

---

## 5. Phase 2 — Architecture

**Goal:** testable, decoupled core; remove global statics; prepare for UX work.

### 2.1 Decompose `GenshinProcesor`
Split the 957-line static class into injected services:
- `IOcrService` (engine pool + recognize).
- `IImagePreprocessor` (from 1.1).
- `ILookupService` (characters/weapons/artifacts/materials normalization + fuzzy match).
- `ITextNormalizer` (stat/element/name correction).

### 2.2 Introduce DI + Hosting
- `Microsoft.Extensions.DependencyInjection` + `Microsoft.Extensions.Hosting`. Compose services at startup; scrapers receive dependencies via ctor instead of reaching into statics.

### 2.3 Modern configuration
- Replace `Properties.Settings`/`Settings.settings` + `JsonUserSettingsProvider` with `Microsoft.Extensions.Configuration` + a typed `IOptions<ScanSettings>` bound to a user `appsettings.json` in `%AppData%`.

### 2.4 Typed data models
- Replace `Dictionary<string,JObject>` for characters/artifacts with typed records; complete the System.Text.Json migration started in 1.4.

### 2.5 Decouple UI from logic (MVVM-lite)
- Introduce a `ScanViewModel` exposing observable progress/state. Scrapers report progress via an `IProgress<ScanProgress>` / events — **not** by writing into static `UserInterface` WinForms controls.
- `MainForm` becomes a thin view bound to the view model. This is the bridge to Phase 3.

**Exit criteria:** no `static` mutable engine/lookup state; services unit-tested in isolation; UI receives progress through an abstraction; behavior parity maintained.

---

## 6. Phase 3 — UX modernization

**Goal:** turn the "don't touch your mouse and wait" black box into a guided, transparent tool. Built on the Phase 2 view model.

### 3.1 Live scan feedback
- Per-category progress (characters / weapons / artifacts / materials) with running counts + ETA.
- Live thumbnail of the current capture region and last-recognized item.

### 3.2 Pre-flight validation
- Before scanning, detect game window resolution, aspect ratio (16:9 / 16:10), language, HDR, and keybinds; warn inline. This automates ~40 lines of manual README setup and prevents the most common bad scans.

### 3.3 Inline OCR review/correction
- When recognition confidence is low, surface the item inline for one-click correction instead of dumping screenshots into `logging/` for the user to file as a GitHub issue.
- Confidence threshold configurable.

### 3.4 Visual & accessibility polish
- PerMonitorV2 DPI awareness (modern WinForms) for crisp scaling on high-DPI displays.
- Dark mode + consistent theming. Keyboard navigation and clear status/error surfaces.

### 3.5 Onboarding & errors
- First-run guided setup wizard (resolution/language/keybinds).
- Friendly error panel with "copy diagnostics" (zips `logging/` + version) to streamline bug reports.

**Exit criteria:** real-time progress, pre-flight checks, inline correction shipped; DPI + dark mode; positive scan-success-rate feedback.

---

## 7. Cross-cutting / risks

- **Game-update fragility:** scanning depends on Genshin UI layout + lookup tables (`inventorylists`, Dimbreath sync). Modernization must not disturb the auto-updater path (`DatabaseManager`). Add tests around lookup parsing.
- **OCR parity:** any image-pipeline change (1.1) risks recognition regressions. Gate with the §1.6 benchmark on a fixed screenshot corpus.
- **Third-party net8 support:** validate every NuGet dep in Phase 0 before committing to the retarget (§0.4).
- **No automated game testing:** screen-automation can't run in CI. Maintain a manual smoke-scan checklist per release; push as much logic as possible behind unit tests.

---

## 8. Suggested sequencing & branches

| Phase | Branch | Depends on | Rough size |
|---|---|---|---|
| 0 | `modernize/phase0-foundation` | — | Medium |
| 1 | `modernize/phase1-efficiency` | 0 | Medium-Large |
| 2 | `modernize/phase2-architecture` | 1 | Large |
| 3 | `modernize/phase3-ux` | 2 | Large |

Each phase merges to `master` only when it builds in CI and passes a manual smoke scan. Phases 1–3 can each be split into smaller PRs internally (e.g., 1.1 Accord swap is its own PR).

---

## 9. Immediate next step

Begin **Phase 0.1–0.3**: SDK-style `net8.0-windows` conversion on `modernize/phase0-foundation`, confirm `dotnet build` + smoke scan, then add the test project (0.6) and CI (0.7). This is the reversible foundation everything else builds on.
