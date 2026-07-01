# Inventory Kamera — Modernization Plan

> Status: **In progress** · Scope: full phased modernization (foundation → efficiency → UX)
> Drafted 2026-06-28 · Last updated 2026-07-01 · Target: incremental, `master` stays releasable throughout
> Working branch: `modernize/phase0-foundation` (holds Phase 0 + in-progress Phase 1; not yet merged)

---

## 0. Status at a glance

| Phase | State | Notes |
|---|---|---|
| **0 — Foundation** | ✅ **complete** | SDK-style project, xUnit tests, CI. |
| **1 — Efficiency** | 🔄 **in progress** | Accord fully removed; **net8.0-windows retarget done**; remaining: async/pipeline rework (§1.2–1.6). |
| **2 — Architecture** | ⬜ not started | `ImageProcessing` seam (§2.1) already extracted early during Phase 1. |
| **3 — UX** | ⬜ not started | Includes capture modernization (§6b) that fixes the HDR + overlay support issues — now unblocked by net8. |

**Runtime:** the app now targets **`net8.0-windows`** (was net472 through Phase 0; the coupling described below is resolved). Single-file self-contained publish verified working.

**Test/CI status:** 72 tests green (net8.0); GitHub Actions build+test on push/PR and a tag-driven release workflow (now publishing single-file self-contained) are live.

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

> **Sequencing decision (made during Phase 0, resolved during Phase 1):** the initial attempt to
> retarget to `net8.0-windows` in this phase revealed that **Accord.Imaging 3.8 is a hard *compile*
> blocker** on modern .NET — it was built against the abandoned `CoreCompat.System.Drawing` fork, so
> its `UnmanagedImage`/filter APIs will not accept the in-box `System.Drawing.Bitmap` the rest of the
> app uses. The net8 migration and the Accord replacement were therefore **coupled** and could not be
> separated. Phase 0 was re-scoped to convert to an SDK-style project **while staying on net472**
> (zero behavior change); the TFM flip to net8 moved into Phase 1 alongside the Accord replacement,
> gated by **golden pixel-parity tests** (see §1.1 — a stronger, game-asset-free gate than the
> originally-planned OCR-corpus benchmark). **Both are now done** — the app is on net8.0-windows.

### 0.1 Convert to SDK-style project ✅ done
- Replaced legacy `InventoryKamera.csproj` with SDK-style (`<Project Sdk="Microsoft.NET.Sdk">`). Dropped explicit `<Compile Include>` lists (globbing), `BootstrapperPackage`, ClickOnce publish props, and the dead framework polyfill packages.
- Kept `ApplicationIcon`, `ApplicationManifest` (`app.manifest`), `StartupObject`, embedded `.resx`, and `tessdata` as `CopyToOutputDirectory` content.
- Added `Microsoft.NETFramework.ReferenceAssemblies` so it builds headlessly with just the .NET SDK (no VS/targeting pack), and `System.Resources.Extensions` + `GenerateResourceUsePreserializedResources` for the WinForms image resources.
- Pinned `MainForm.resx`'s manifest name via `LogicalName` (its namespace `InventoryKamera` doesn't match its `ui/main` folder, which would otherwise break resource loading under the SDK's path-based naming).

### 0.2 Retarget to .NET 8 — ✅ done (landed in Phase 1, see §1.1)
- Actually executed as part of the Phase 1 Accord removal (they were coupled — see the sequencing
  decision above): `<TargetFramework>net8.0-windows</TargetFramework>`, `<UseWindowsForms>true</UseWindowsForms>`.
  `<Nullable>` left `disable` for now (revisit later; not required for the flip itself).
- All three known breaks from the trial build fixed: `MethodInvoker` qualified to
  `System.Windows.Forms.MethodInvoker`; Tesseract `Bitmap`→`Pix` via an in-memory PNG round-trip;
  `Thread.Abort` replaced with cooperative cancellation — see §0.4.

### 0.3 Verify build + run parity ✅ done (static + net8 runtime checks); manual smoke scan pending
- `dotnet build` clean in Debug + Release (0 errors) on **net8.0-windows**; native Tesseract/Leptonica binaries, tessdata, and `System.Configuration.ConfigurationManager` deploy correctly (the net472-era `System.Resources.Extensions` DLL is no longer needed — it's part of the net8 shared framework).
- Re-verified WinForms resource manifest names + preserialized-resource deserialization under the actual net8 runtime (via a throwaway harness, since reflection tools running under other CLRs can't resolve net8 shared-framework assemblies) — still correct.
- Single-file self-contained publish (`-r win-x64 -p:PublishSingleFile=true --self-contained`) verified producing a working exe with all native deps.
- **Pending (needs admin + the game running):** an end-to-end smoke scan — the standing manual release check, unchanged.

### 0.4 Address .NET 8 incompatibilities ✅ done
- `Thread.Abort` / `ThreadAbortException` replaced with a cooperative `InventoryKamera.CancelRequested` flag, checked between scan phases and within each scraper's per-item loop. Not a "minimal shim" — a real fix, since the Stop hotkey is a documented user-facing feature. Arguably safer than the original: cancellation now only takes effect between items, so it can't corrupt a half-scanned item's state.
- `Octokit`, `NHotkey.WindowsForms`, `InputSimulator`, `Microsoft-WindowsAPICodePack-Shell`, `HtmlAgilityPack.NetCore` all restore and build on net8 (no code changes needed for these). **Known follow-up:** `InputSimulator` restores via the net-framework compat-shim (NU1701 warning) — works so far but is unmaintained; watch for issues.

### 0.5 Single-file self-contained publish ✅ done
- `dotnet publish -r win-x64 -p:PublishSingleFile=true --self-contained true` verified working — one `.exe` (~157MB; bundles the full WindowsDesktop runtime pack, WinForms+WPF natives together regardless of which UI stack is used — an expected self-contained trade-off, not chased further here).
- `release.yml` now publishes this way instead of a framework-dependent build+zip.
- **UX payoff:** removes the README's "install VC++ redist + restart" and ".NET Framework" prerequisites — users download one `.exe`.

### 0.6 Test project ✅ done
- Added `InventoryKamera.Tests` (xUnit, now net8.0-windows). Characterization suites shipped: `RECT` geometry, `GOOD` export envelope, `Weapon` (ascension mapping, validators, serialization keys), `Artifact` (substat filtering, formatting), and the `ImageProcessing` golden parity tests (per-pixel filters + Kirsch/Blob).
- **Still to add** (as those areas are refactored): OCR text normalization / fuzzy-match in `GenshinProcesor` and lookup/database parsing (`DatabaseManager`) — both need fixtures + `InternalsVisibleTo` (the latter already added for the image work).

### 0.7 CI ✅ done
- `.github/workflows/build.yml`: restore → build → test on push/PR.
- `.github/workflows/release.yml`: tag-driven single-file self-contained publish → zip → GitHub Release, replacing the manual `AssemblyVersion`-bump flow. (Version-from-tag via `MinVer`/`Nerdbank.GitVersioning` is a later nicety.)

**Exit criteria: fully met.** SDK-style project **on net8.0-windows**, builds in CI, static + net8-runtime build/resource parity confirmed, single-file publish works, tests green (72). Manual end-to-end smoke scan remains the one standing pending item (needs admin + the live game).

---

## 4. Phase 1 — Efficiency & dependency modernization

**Goal:** kill dead dependencies, modernize the concurrency model, measurable scan throughput.

### 1.1 Replace Accord.Imaging ✅ done
**Approach chosen:** the Accord filter surface turned out to be small and standard, so instead of
pulling in a heavy CV library (OpenCvSharp4 native binaries) or a Bitmap-incompatible one (ImageSharp),
the operations are reimplemented in **pure `System.Drawing` (LockBits)** — zero new dependencies,
keeps `System.Drawing.Bitmap` end-to-end, and produces **byte-identical output** to Accord.

- **Seam:** extracted an `ImageProcessing` class out of `GenshinProcesor` (this is §2.1's
  `IImagePreprocessor`, pulled forward). `GenshinProcesor` delegates so call sites are unchanged.
- **Verification gate:** a probe captured Accord's exact pixel semantics, pinned as **golden parity
  tests**. Because OCR only ever sees the pre-processed image, pixel-for-pixel parity guarantees
  scan behaviour is unchanged — no game screenshots needed.
- **Per-pixel filters (exact match):** grayscale (luma-truncated → 8bpp indexed), invert, threshold
  (inclusive), contrast (levels-linear stretch), colour-filter, and `ImageStatistics` (per-channel
  mean). Direct scraper Accord calls routed through `ImageProcessing`.
- **`KirschEdgeDetector`** (8-kernel compass convolution) and **`BlobCounter`** (8-connected
  component labeling — produces inventory item bounding boxes, the hardest piece) reimplemented in
  `ImageProcessing.EdgeDetectKirsch`/`FindBlobRectangles`. Kirsch matches Accord exactly on every
  interior pixel (thousands of samples incl. a pseudo-random image); Accord applies an undocumented
  normalization to only the outermost 1px border, which is immaterial (edge of the captured window,
  can't form an icon-sized blob). Blob detection is fully exact (connectivity, size filter, discovery
  order). `Accord.IntRange` → local `IntRange` struct; `Rectangle.Center()` extension (Accord.Imaging
  supplied it) → local `RectangleExtensions`.
- **Result:** `Accord.Imaging` PackageReference removed from the app project entirely — 0 Accord DLLs
  ship. The parity tests originally compared live against Accord in the test project too; once proven
  correct, they were converted to assert against golden values captured from that comparison (Accord
  can't be referenced from net8 at all, so it couldn't stay a live test dependency once §0.2 landed).

### 1.1b Replace Thread.Abort scan cancellation ✅ done
Not originally itemized, but required to reach net8 (§0.4): `Thread.Abort()`/`ThreadAbortException`
(used by the Stop hotkey to interrupt an in-progress scan) is unsupported on modern .NET. Replaced
with `InventoryKamera.CancelRequested`, a cooperative volatile flag checked between scan phases in
`GatherData()` and within each scraper's per-item loop, at the same breakpoints already used for the
existing `StopScanning` (rarity/level filter) exit. On cancellation `MainForm` now explicitly skips
GOOD conversion/export/the optimizer dialog (previously implicit via the exception unwind) — matches
the documented behaviour that the user must use "Export Scanned Data" for partial results. This is
arguably *safer* than before: cancellation only takes effect between items, so it can't corrupt a
half-scanned item's state.

### 1.1c Retarget to net8.0-windows ✅ done
Unblocked by 1.1's Accord removal. `<TargetFramework>net8.0-windows</TargetFramework>`,
`<UseWindowsForms>true</UseWindowsForms>`. Fixed the two remaining known breaks: `MethodInvoker`
ambiguity (qualified to `System.Windows.Forms.MethodInvoker`) and Tesseract's missing `Bitmap`
overload on netstandard2.0 (round-trip through an in-memory PNG → `Pix.LoadFromMemory`). Dropped the
legacy `<Reference>` assembly list and net-framework polyfill packages (all built into net8's shared
framework now); added `System.Configuration.ConfigurationManager` for `ApplicationSettingsBase`. Full
detail in §0.2–§0.5 above (the net8 sub-items of Phase 0 that were deferred here).

### 1.2 Replace thread/queue model with Channels + async ✅ done
- Producer (weapon/artifact scan loops) / N consumers (OCR) over `System.Threading.Channels.Channel<OCRImageCollection>`, replacing a hand-rolled locking `Queue<T>` + `Thread`s polling with `Thread.Sleep(250)`.
- Normal end-of-work is `workerChannel.Writer.Complete()`, replacing the `"END"` sentinel + shared `volatile bool b_threadCancel`. Abrupt cancellation (`StopImageProcessorWorkers`) uses a dedicated `CancellationToken` instead — cleanly separating "no more work is coming, drain what's queued" from "abort now, drop what's queued."
- **Real bug fixed along the way:** the old shared-flag design meant *any* worker dequeuing the `"END"` sentinel flipped a flag that made *every* worker (including ones with their own pending items) clear the whole queue and exit, silently dropping unprocessed items. Channel completion drains correctly by construction.
- Workers are `Task`s (`ImageProcessorWorkerAsync`) using `await foreach`/`ReadAllAsync`; `AwaitProcessors` is `Task.WaitAll` instead of a busy-poll loop removing dead threads from a list. Each queued item now runs in its own try/catch so one bad item can't silently kill a whole worker.

### 1.3 Right-size parallelism ✅ done
- `InventoryKamera.NumWorkers`: base = `Environment.ProcessorCount - 1` (headroom for the UI/nav thread), capped by the existing scanner-speed-derived ceiling (3/2) — scales down on small machines instead of blindly spinning up 2–3 threads on a 1–2 core box.
- `GenshinProcesor`'s Tesseract engine pool: `numEngines` is `clamp(ProcessorCount, 4, 12)` instead of a hardcoded `8`. Also swapped the pool itself from `ConcurrentBag` + a `Thread.Sleep(10)` busy-poll to `BlockingCollection`, whose blocking `Take()` removes the busy-poll — a genuine efficiency win, not just a sizing tweak. (Didn't introduce a new `ObjectPool<T>` package as originally sketched — `BlockingCollection` already gives the needed bounded-pool behavior with zero new dependencies; revisit if Phase 2's DI work wants a more formal abstraction.)

### 1.4 Selective System.Text.Json migration — 🔻 reconsidered, deprioritized
**Original plan:** move GOOD export and simple key/value lists to `System.Text.Json` ("faster, no
extra dep"). **On reflection, both premises don't hold once §2.1–2.4 aren't done yet:**
- Newtonsoft.Json stays a hard dependency regardless — it's used throughout for the dynamic
  `Dictionary<string, JObject>` character/artifact/weapon lookups, `DatabaseManager`, and the JSON
  settings provider. Migrating only GOOD export doesn't remove it; it adds a *second* JSON library.
- GOOD export happens once per scan (not a hot path) — negligible real performance benefit.
- **Real risk, low reward:** `Weapon`/`Artifact` use `[DefaultValue(-1)]` + Newtonsoft's
  `DefaultValueHandling.Ignore` to omit sentinel values from the export. `System.Text.Json`'s
  `JsonIgnoreCondition.WhenWritingDefault` only recognizes true CLR defaults (`0`, not `-1`) — subtly
  different semantics. A mismatch here would silently corrupt the exported GOOD JSON that external
  tools (Genshin Optimizer, SEELIE.me) parse — a much higher blast radius than an internal refactor.

**Decision:** defer full migration until Phase 2's typed-model work (§2.4) removes the dynamic
`JObject` dependency on Newtonsoft anyway, at which point a clean full switch (not a split) makes
more sense. Revisit then; not blocking Phase 1's exit criteria.

### 1.5 Kill the manequin JSON-string hack ✅ done
Replaced the `InventoryKamera.cs` string-surgery-on-`characters.json` + recursive `GatherData()`
retry with `GenshinProcesor.EnsureManequinEntriesExist()`, called from `ReloadData()`: missing
`manequin1`/`manequin2` entries are added through the JSON object model (`JObject`/`JArray`) and
persisted with one clean serialize. `InventoryKamera.GatherData()`'s `UpdateCharacterName` calls for
the manequins now always succeed, no exception-driven control flow. Also fixed a latent typo in the
original hand-written JSON (`"skills"` → `"skill"`, inconsistent with every other character entry).

### 1.6 Benchmark ✅ done (primitive-level; end-to-end scan timing still needs live game assets)
- Correctness of the Accord swap (§1.1) is gated by **golden pixel-parity tests**, not a timing
  benchmark — pixel-identical pre-processing means OCR is provably unchanged.
- **Honest scoping call:** the plan's original "recorded screenshot set → scan → timing" benchmark
  needs real game screenshots or a live session, which this environment doesn't have. What's
  genuinely measurable without that: the specific concurrency primitives §1.2/1.3 replaced.
  `ConcurrencyBenchmark.cs` measures "time from an item becoming available to a waiting consumer
  noticing it" — exactly what a fixed-interval poll bounds from below — for both the new and the
  (reproduced-for-comparison) old design:
  - Channel-based hand-off vs. the removed lock-queue + `Thread.Sleep(250)` polling:
    **~0.07ms vs. ~248ms average — ~3,300× lower latency.**
  - `BlockingCollection.Take()` vs. the removed `ConcurrentBag` + `Thread.Sleep(10)` spin-wait:
    **~0.05ms vs. ~8ms average — ~150× lower latency.**
  - These are real, reproducible, run-it-yourself measurements (`dotnet test --filter
    ConcurrencyBenchmark -l "console;verbosity=detailed"`), not asserted as tight CI performance
    gates (timing is noisy on shared hardware) — the test assertions are generous sanity bounds.
  - **What this does not prove:** end-to-end scan wall-clock time, which also depends on Tesseract
    OCR time, navigation/input delays, and game rendering — none of which the removed polling
    intervals dominated. A live-game timing comparison remains open (folds into the standing manual
    smoke-scan gap).

**Exit criteria: met.** Accord removed + on net8, async pipeline with `CancellationToken` + channel
completion, engine pool derived from `Environment.ProcessorCount`, parity tests green (79 total),
primitive-level timing benchmark shows large measured improvements.

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
- **OCR parity:** any image-pipeline change (1.1) risks recognition regressions. Gated by **golden pixel-parity tests** on synthetic inputs (see §1.1) — proven identical (or interior-identical, for Kirsch's border) output to Accord for every reimplemented op, including `KirschEdgeDetector`/`BlobCounter`. Still no live-game verification (see below).
- **Third-party net8 support:** validated as part of the retarget (§0.4) — no code changes needed beyond the three known breaks. **Standing watch item:** `InputSimulator` restores via the net-framework compat-shim (NU1701); unmaintained, works so far.
- **No automated game testing:** screen-automation can't run in CI. Maintain a manual smoke-scan checklist per release (still outstanding for the net8 flip); push as much logic as possible behind unit tests.

---

## 8. Suggested sequencing & branches

| Phase | Branch | Depends on | State |
|---|---|---|---|
| 0 | `modernize/phase0-foundation` | — | ✅ complete |
| 1 | `modernize/phase0-foundation` (same branch so far) | 0 | 🔄 in progress — Accord removal + net8 retarget **done**; async/pipeline rework (§1.2–1.6) remaining |
| 2 | `modernize/phase2-architecture` | 1 | ⬜ not started (`ImageProcessing` seam started early) |
| 3 | `modernize/phase3-ux` (incl. §6b capture) | 2 (net8 ✅ available now) | ⬜ not started |

Phase 0 and the in-progress Phase 1 currently share `modernize/phase0-foundation` (not yet merged to
`master`); they can be split into a dedicated Phase 1 branch/PR before merge if preferred. Each phase
merges to `master` only when it builds in CI and passes a manual smoke scan. Phase 1 is landing as
small internal commits (SDK conversion → seam → per-pixel swap → stats swap → Kirsch/Blob swap →
Thread.Abort replacement → net8 retarget → …).

---

## 9. Immediate next step

The **Accord removal and net8 retarget are done** — the app builds and tests green on
`net8.0-windows`, and this unblocks the §6b Windows.Graphics.Capture work that fixes the HDR +
overlay support issues. Two candidate next steps, not yet sequenced:

1. **A manual smoke scan** against the live game (the one standing verification gap from §0.3/§0.5)
   before merging this branch to `master` — the safest checkpoint given how much has landed.
2. **Continue Phase 1** into §1.2–1.6 (Channels/async pipeline, engine pooling, System.Text.Json,
   the manequin JSON-string hack) or **jump to §6b** (WGC capture, now unblocked) depending on
   priority — HDR/overlay fixes are higher user-visible value; the async rework is more
   architectural cleanup.
