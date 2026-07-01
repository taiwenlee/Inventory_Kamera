# Inventory Kamera — Modernization Plan

> Status: **In progress** · Scope: full phased modernization (foundation → efficiency → UX)
> Drafted 2026-06-28 · Last updated 2026-07-01 · Target: incremental, `master` stays releasable throughout
> Working branch: `modernize/phase0-foundation` (holds Phase 0 + in-progress Phase 1)

---

## 0. Status at a glance

| Phase | State | Notes |
|---|---|---|
| **0 — Foundation** | ✅ **complete** | SDK-style project on net472, xUnit tests, CI. net8 deferred (coupled to Accord). |
| **1 — Efficiency** | 🔄 **in progress** | Accord per-pixel filters + `ImageStatistics` reimplemented in pure GDI with golden parity; **remaining:** `KirschEdgeDetector` + `BlobCounter` + `IntRange`, then net8 flip, then async/pipeline (§1.2–1.6). |
| **2 — Architecture** | ⬜ not started | `ImageProcessing` seam (§2.1) already extracted early during Phase 1. |
| **3 — UX** | ⬜ not started | Includes capture modernization (§6b) that fixes the HDR + overlay support issues. |

**Test/CI status:** 62 tests green; GitHub Actions build+test on push/PR and a tag-driven release workflow are live (the two "None" rows in the baseline below are now done).

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
> Accord replacement, gated by **golden pixel-parity tests** (see §1.1 — a stronger, game-asset-free
> gate than the originally-planned OCR-corpus benchmark).

### 0.1 Convert to SDK-style project ✅ done
- Replaced legacy `InventoryKamera.csproj` with SDK-style (`<Project Sdk="Microsoft.NET.Sdk">`). Dropped explicit `<Compile Include>` lists (globbing), `BootstrapperPackage`, ClickOnce publish props, and the dead framework polyfill packages.
- Kept `ApplicationIcon`, `ApplicationManifest` (`app.manifest`), `StartupObject`, embedded `.resx`, and `tessdata` as `CopyToOutputDirectory` content.
- Added `Microsoft.NETFramework.ReferenceAssemblies` so it builds headlessly with just the .NET SDK (no VS/targeting pack), and `System.Resources.Extensions` + `GenerateResourceUsePreserializedResources` for the WinForms image resources.
- Pinned `MainForm.resx`'s manifest name via `LogicalName` (its namespace `InventoryKamera` doesn't match its `ui/main` folder, which would otherwise break resource loading under the SDK's path-based naming).

### 0.2 Retarget to .NET 8 — deferred to Phase 1 (coupled with Accord removal)
- `<TargetFramework>net8.0-windows</TargetFramework>`, `<UseWindowsForms>true</UseWindowsForms>`.
- Enable `<Nullable>enable</Nullable>` (warnings first, not errors) and `<LangVersion>latest</LangVersion>`.
- Known breaks already surfaced by the trial build (to fix during the flip): `MethodInvoker` now ambiguous with the new `System.Reflection.MethodInvoker`; Tesseract 5.x needs `Bitmap`→`Pix` conversion; `Thread.Abort` unsupported — see 0.4.

### 0.3 Verify build + run parity ✅ done (static); manual smoke scan pending
- `dotnet build` clean in Debug + Release (0 errors); native Tesseract/Leptonica binaries, tessdata, and generated `.exe.config` all deploy.
- Verified WinForms resource manifest names match the form types (caught + fixed a `MainForm` mis-naming that would have crashed startup) and that preserialized image resources deserialize at runtime.
- **Pending (needs admin + the game running):** an end-to-end smoke scan — the standing manual release check.

### 0.4 Address .NET 8 incompatibilities (known) — deferred to Phase 1 net8 flip
- `Thread.Abort` / `ThreadAbortException` (caught in `MainForm`/`InventoryKamera`) is **not supported** on modern .NET → replace with cooperative cancellation (preview of Phase 1 §1.2; minimal shim here).
- Audit `Octokit`, `NHotkey.WindowsForms`, `InputSimulator`, `Microsoft-WindowsAPICodePack-Shell`, `HtmlAgilityPack.NetCore` for net8 support; replace `Microsoft-WindowsAPICodePack-Shell` file dialogs with `Microsoft.WindowsAPICodePack-Shell` successor or WinForms `OpenFileDialog`/`FolderBrowserDialog`.

### 0.5 Single-file self-contained publish — deferred to Phase 1 net8 flip (a net-core feature)
- `dotnet publish -r win-x64 -p:PublishSingleFile=true --self-contained`.
- **UX payoff:** removes the README's "install VC++ redist + restart" and ".NET Framework" prerequisites — users download one `.exe`.

### 0.6 Test project ✅ done
- Added `InventoryKamera.Tests` (xUnit, net472). Characterization suites shipped: `RECT` geometry, `GOOD` export envelope, `Weapon` (ascension mapping, validators, serialization keys), `Artifact` (substat filtering, formatting), and the `ImageProcessing` golden parity tests.
- **Still to add** (as those areas are refactored): OCR text normalization / fuzzy-match in `GenshinProcesor` and lookup/database parsing (`DatabaseManager`) — both need fixtures + `InternalsVisibleTo` (the latter already added for the image work).

### 0.7 CI ✅ done
- `.github/workflows/build.yml`: restore → build → test on push/PR.
- `.github/workflows/release.yml`: tag-driven build → zip → GitHub Release, replacing the manual `AssemblyVersion`-bump flow. (Single-file publish gets wired in once on net8; version-from-tag via `MinVer`/`Nerdbank.GitVersioning` is a later nicety.)

**Exit criteria (met, adjusted):** SDK-style project **on net472** (net8 deferred to Phase 1 per the coupling above), builds in CI, static build/resource parity confirmed, tests green. Single-file publish + smoke-scan parity carry into the net8 flip.

---

## 4. Phase 1 — Efficiency & dependency modernization

**Goal:** kill dead dependencies, modernize the concurrency model, measurable scan throughput.

### 1.1 Replace Accord.Imaging 🔄 in progress
**Approach chosen:** the Accord filter surface turned out to be small and standard, so instead of
pulling in a heavy CV library (OpenCvSharp4 native binaries) or a Bitmap-incompatible one (ImageSharp),
the operations are reimplemented in **pure `System.Drawing` (LockBits)** — zero new dependencies,
keeps `System.Drawing.Bitmap` end-to-end, and produces **byte-identical output** to Accord.

- **Seam:** extracted an `ImageProcessing` class out of `GenshinProcesor` (this is §2.1's
  `IImagePreprocessor`, pulled forward). `GenshinProcesor` delegates so call sites are unchanged.
- **Verification gate:** a probe captured Accord's exact pixel semantics, pinned as **golden parity
  tests**. Because OCR only ever sees the pre-processed image, pixel-for-pixel parity guarantees
  scan behaviour is unchanged — no game screenshots needed.
- **Done (proven equivalent):** grayscale (luma-truncated → 8bpp indexed), invert, threshold
  (inclusive), contrast (levels-linear stretch), colour-filter, and `ImageStatistics` (per-channel
  mean). Direct scraper Accord calls routed through `ImageProcessing`.
- **Remaining:** `KirschEdgeDetector` (deterministic 8-kernel convolution — replicable exactly) and
  `BlobCounter` (connected-component labeling — the hard one; produces inventory item bounding
  boxes) in the inventory-grid path, plus the trivial `Accord.IntRange` value type. Once those land,
  delete the Accord package and **flip to net8** (§0.2/0.4/0.5 fold in here).

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
- Correctness of the Accord swap (§1.1) is gated by **golden pixel-parity tests**, not a timing
  benchmark — pixel-identical pre-processing means OCR is provably unchanged.
- A **timing** benchmark (recorded screenshot set → process → timing) is still worth adding for the
  throughput work in §1.2–1.3 (async pipeline, engine pool), so those gains are measured not assumed.

**Exit criteria:** Accord removed + on net8, async pipeline with `CancellationToken`, engine pool, parity tests green, timing benchmark shows ≥ parity.

---

## 5. Phase 2 — Architecture

**Goal:** testable, decoupled core; remove global statics; prepare for UX work.

### 2.1 Decompose `GenshinProcesor`
Split the 957-line static class into injected services:
- `IOcrService` (engine pool + recognize).
- `IImagePreprocessor` — 🔄 **started early in Phase 1**: the `ImageProcessing` class is already
  extracted (image ops no longer live in `GenshinProcesor`). Currently a `static` class; formalize as
  an injectable service here.
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
- HDR detection is the reliable, high-value piece (see §6b for why); overlays are only heuristically detectable until the capture rewrite lands.

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

## 6b. Capture modernization — Windows.Graphics.Capture (fixes HDR + overlays)

> **Depends on:** the net8 flip (Phase 1). WinRT/WGC is far easier to consume on modern .NET.
> **Fixes:** the two most common "bad scan" support issues (HDR, overlays) at the source.

### Root cause (confirmed)
All capture goes through `Graphics.CopyFromScreen` in `game/Navigation.cs`
(`CaptureWindow` :87, `CaptureRegion` :104) — a GDI BitBlt of the **desktop** at the game
window's screen coordinates. It photographs whatever is on screen there, so:
- **Overlays** (Discord, NVIDIA ShadowPlay/Freestyle, Steam, RTSS/Afterburner, FPS/PC-stat widgets)
  composited over the window are captured verbatim, corrupting OCR regions.
- **HDR**: with HDR on, the desktop is composited in 10-bit HDR and GDI reads back an
  SDR-tone-mapped 8-bit approximation. Every pixel's luminance/colour shifts, which breaks the
  hard-coded SDR calibration this project relies on — grayscale thresholds (75/70/50), the
  "activate" brightness check `>= 190`, and artifact-rarity colour matching to fixed RGB constants
  (e.g. 5★ `(188,105,50)`). See [[net8-blocked-by-accord]] for where those thresholds now live
  (`ImageProcessing`).

### Target design
Replace GDI screen-scraping with **Windows.Graphics.Capture (WGC)** capturing the game window's
own frames. WGC excludes overlays composited on top and gives controlled access to the swapchain
(correct HDR handling) — it's the API OBS/game-capture tools use.

1. **Seam first (same pattern as `ImageProcessing`/Accord).** Introduce `IScreenCapture` with
   `Bitmap CaptureWindow()` / `Bitmap CaptureRegion(RECT)`. Implementations:
   - `GdiScreenCapture` — the current `CopyFromScreen` code (kept as fallback for < Win10 1803 and
     as an A/B baseline).
   - `WgcScreenCapture` — the new path. `Navigation` delegates to the injected capture.
2. **WGC pipeline.** `GraphicsCaptureItem` from the game HWND via `IGraphicsCaptureItemInterop.
   CreateForWindow` → `Direct3D11CaptureFramePool` on a D3D11 device → `GraphicsCaptureSession`.
   Maintain a latest-frame cache updated by `FrameArrived`; expose a synchronous "latest frame as
   `Bitmap`" so the scrapers' on-demand `CaptureRegion` model is unchanged. `CaptureRegion` crops
   the cached full-window frame. **Keep returning `System.Drawing.Bitmap`** so all scrapers/OCR are
   untouched: `IDirect3DSurface` → `ID3D11Texture2D` → staging texture (CPU read) → map → `Bitmap`.
3. **Native interop deps (net8).** Bump TFM to `net8.0-windows10.0.19041.0` to light up WGC WinRT
   projections (CsWinRT). D3D11 device/texture via **Vortice.Windows** (maintained, net8-friendly).
4. **HDR path.** Capture SDR as `DirectXPixelFormat.B8G8R8A8UIntNormalized`. Under HDR the content is
   scRGB `R16G16B16A16Float`; **open question** whether B8G8R8A8 capture yields values matching
   native SDR or needs an explicit HDR→SDR tone-map to preserve the existing thresholds. Research +
   A/B before committing. Either way WGC makes it *possible* (GDI does not).

### Verification (no game screenshots → can't golden-test)
- **A/B harness / diagnostic:** capture the same region via `GdiScreenCapture` and `WgcScreenCapture`
  and save both. In SDR + no-overlay conditions they should be near-identical (validates WGC
  correctness/coordinate mapping); under HDR/overlay, WGC should be correct where GDI is wrong.
- **Capture-backend setting** so testers can switch and fall back.
- Manual smoke-scan on a real account (the standing release checklist).

### Risks / open items
- **Coordinate/border mapping:** WGC frames are window-local, not screen-offset; the existing region
  math offsets by `GetPosition()` for the BitBlt and assumes client-area size (`GetWidth/Height`).
  WGC window capture may include the non-client frame → needs a calibration/crop step. Biggest
  correctness risk; mitigate with the A/B harness.
- **HDR fidelity** (tone-map question above).
- **OS floor:** WGC needs Win10 1803+; the borderless-capture option (`IsBorderRequired=false`) needs
  Win11. Keep `GdiScreenCapture` as fallback.
- Also unblocks retiring the manual `SetProcessDPIAware`/DPI hacks.

### Sequencing
Post-net8 (after Phase 1). Slots as a **Phase 2.5 / early Phase 3** item; §3.2 pre-flight HDR
detection is the cheap stopgap that ships before this.

---

## 7. Cross-cutting / risks

- **Game-update fragility:** scanning depends on Genshin UI layout + lookup tables (`inventorylists`, Dimbreath sync). Modernization must not disturb the auto-updater path (`DatabaseManager`). Add tests around lookup parsing.
- **OCR parity:** any image-pipeline change (1.1) risks recognition regressions. Gated by **golden pixel-parity tests** on synthetic inputs (see §1.1) — proven identical output to Accord for every reimplemented op. `KirschEdgeDetector`/`BlobCounter` still need this treatment.
- **Third-party net8 support:** validate every NuGet dep before the retarget (§0.4). Trial build already flagged the concrete breaks (`MethodInvoker`, Tesseract `Bitmap`→`Pix`, `Thread.Abort`).
- **No automated game testing:** screen-automation can't run in CI. Maintain a manual smoke-scan checklist per release; push as much logic as possible behind unit tests.

---

## 8. Suggested sequencing & branches

| Phase | Branch | Depends on | State |
|---|---|---|---|
| 0 | `modernize/phase0-foundation` | — | ✅ complete |
| 1 | `modernize/phase0-foundation` (same branch so far) | 0 | 🔄 in progress (Accord swap) |
| 2 | `modernize/phase2-architecture` | 1 | ⬜ not started (`ImageProcessing` seam started early) |
| 3 | `modernize/phase3-ux` (incl. §6b capture) | 2 (net8) | ⬜ not started |

Phase 0 and the in-progress Phase 1 currently share `modernize/phase0-foundation` (not yet merged to
`master`); they can be split into a dedicated Phase 1 branch/PR before merge if preferred. Each phase
merges to `master` only when it builds in CI and passes a manual smoke scan. Phase 1 is landing as
small internal commits (SDK conversion → seam → per-pixel swap → stats swap → …).

---

## 9. Immediate next step

Finish the **§1.1 Accord removal**: reimplement `KirschEdgeDetector` + `BlobCounter` (with golden
parity where feasible) and drop `Accord.IntRange`, delete the Accord package, then execute the
**net8 flip** (folding in §0.4 breaks + §0.5 single-file publish). That unblocks the §6b
Windows.Graphics.Capture work that fixes the HDR + overlay support issues.
