# Inventory Kamera — Modernization Plan

> Status: **In progress** · Scope: full phased modernization (foundation → efficiency → UX)
> Drafted 2026-06-28 · Last updated 2026-07-05 · Target: incremental, `master` stays releasable throughout
> Working branch: `modernize/phase0-foundation` (holds Phase 0 + Phase 1, both complete; not yet merged)

---

## 0. Status at a glance

| Phase | State | Notes |
|---|---|---|
| **0 — Foundation** | ✅ **complete** | SDK-style project, xUnit tests, CI. |
| **1 — Efficiency** | ✅ **complete** | Accord removed, net8.0-windows retarget, Channels/async pipeline, right-sized parallelism, manequin hack killed, concurrency benchmark. §1.4 (System.Text.Json) deliberately deferred to Phase 2 — see §1.4. |
| **2 — Architecture** | 🔄 **in progress** | §2.1 done: `IOcrService`, `LookupService`, `IImagePreprocessor`/`ImageProcessor`, and `TextNormalizer` all extracted from `GenshinProcesor` with real unit tests. §2.2 done: both stateful services fully constructor-injected into all 5 scrapers; `GenshinProcesor` static forwarding wrappers deleted; no DI container yet (hand-wired composition root). §2.3 done for scan logic: `IScanSettings` seam added, still backed by `Properties.Settings.Default` on purpose (see §2.3 for why). §2.4 investigated and deliberately deferred (remote/variable-shape data + a mutable field make it a real design problem, not a mechanical one). §2.5 done except character display: `ScanViewModel` now owns real observable state for counters, status/errors, gear, material/mora, and navigation image (six slices, unit-tested where possible). Character display stays on the static `UserInterface` bridge pending the scan-input revamp (§6c). |
| **3 — UX** | 🔄 **in progress** | §3.0 (declutter/reorganize `MainForm`) substantially done — see §3.0 for the full slice history (GroupBoxes → tabbed layout → dedicated Advanced Settings dialog → flat/warm visual theme). §3.1 (live scan feedback) done. §3.2 (pre-flight validation) mostly done — resolution/aspect-ratio/keybind/game-running checks land before a scan starts; HDR/language detection deferred. §3.3 (inline OCR correction) first slice done — confidence capture + threshold setting + correction dialog, wired into one call site (item count), not yet expanded further or live-verified. §3.4's DPI-awareness bullet done (landed as a side effect of the 4K windowed-capture bugfix); dark mode done (commit e9a60a2 — Options-menu toggle, `UiTheme.ApplyTheme` across MainForm/SettingsForm/OcrCorrectionForm); remaining §3.4 polish (keyboard navigation, status/error surfaces) and §3.5 (onboarding) not started. §6b (Windows.Graphics.Capture) was implemented and tested against real usage, then **reverted** — see §6b for why. HDR/overlay support issues remain unresolved. |
| **Scan input revamp** | ✅ **all five scan types (Weapons, Artifacts, Materials, Character Development Items, Character) controller-driven and live-verified; mouse-based scan code fully removed** | Keyboard-only ruled out; controller input confirmed viable. All five scrapers now go through `GameController`-driven navigation, wired into `GatherData` as a full replacement (no opt-in toggle, per the original decision) — the old mouse-based `ScanWeapons`/`ScanArtifacts`/etc. methods were deleted (commit `33d5e3e`) once each controller path proved out live. Weapons: sort-by-level/quality with early-stop, gated behind confirming the sort actually applied (fixed live 2026-07-05) rather than trusting it blindly. Artifacts: sanctify-shift, sort-by-obtained toggle, filter-reset all working; no sort-MODE (Level/Quality/Type) selection yet, so filtering never early-stops (correct, just slower on aggressive filters). Materials/Character Dev Items: quantity read via precomputed grid-cell percentages (not blob detection) with per-tab scroll-drift constants, live-verified working. Character: full Attributes→Constellations→Talents pipeline, batched by sub-tab across the roster, gap-aware roster walking, Rarity field added to the character DB, Traveler's per-element `ConstellationOrder` preserved across DB updates. **Known remaining gaps:** weapon locked-status detection (unmeasured, always reports unlocked); constellation/talent panels still require a mouse-input fallback (unavoidable per the game's own UI); artifact sort-mode selection not ported. See §6c. |

**Runtime:** the app now targets **`net8.0-windows7.0`** (was net472 through Phase 0; bumped from bare `net8.0-windows` after live testing surfaced 670+ spurious CA1416 warnings — see below). Single-file self-contained publish verified working. OCR worker pipeline runs on `System.Threading.Channels` + `Task`s instead of a hand-rolled locking queue + polling `Thread`s.

**Test/CI status:** 121 tests green (net8.0), including real Tesseract OCR round-trip tests — previously impossible, since touching `GenshinProcesor` at all used to eagerly load the whole engine pool from disk — `LookupService`/`TextNormalizer` tests using fake dictionaries, `ImageProcessor` delegation tests, `ScanSettings` live-forwarding tests, and `ScanViewModel` counter/gear/material/mora/navigation-image-state tests (including a concurrency regression test). GitHub Actions build+test on push/PR and a tag-driven release workflow (publishing single-file self-contained) are live.

**Standing gap:** live smoke-testing during Phase 2 (2026-07-01) surfaced two real bugs missed by build/test verification alone — a pre-existing `NullReferenceException` in `GetPageOfItems` when page-item detection exhausts its retries with `LogScreenshots` enabled, and a cancel-latency regression (same method's retry loop had no `CancelRequested` check, so Stop couldn't interrupt it — previously masked by the crash). Both fixed and verified live. A full scan was also run live after the `IScanProgressReporter` seam (§2.5's first slice) landed, confirming progress display, error reporting, and cancel all still behave identically now that scan logic goes through the injected interface instead of the static `UserInterface` directly. This is a reminder that build+test-green doesn't substitute for live verification on a scan-heavy, UI-automation-driven app like this one; keep testing live where practical as Phase 2 continues.

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

### 0.3 Verify build + run parity ✅ done (static + net8 runtime checks; live-verified by the user throughout)
- `dotnet build` clean in Debug + Release (0 errors) on **net8.0-windows**; native Tesseract/Leptonica binaries, tessdata, and `System.Configuration.ConfigurationManager` deploy correctly (the net472-era `System.Resources.Extensions` DLL is no longer needed — it's part of the net8 shared framework).
- Re-verified WinForms resource manifest names + preserialized-resource deserialization under the actual net8 runtime (via a throwaway harness, since reflection tools running under other CLRs can't resolve net8 shared-framework assemblies) — still correct.
- Single-file self-contained publish (`-r win-x64 -p:PublishSingleFile=true --self-contained`) verified producing a working exe with all native deps.
- **Live-verified:** the user has been running the app against the live game continuously throughout this branch's work, with no issues reported.

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

**Exit criteria: fully met.** SDK-style project **on net8.0-windows**, builds in CI, static + net8-runtime build/resource parity confirmed, single-file publish works, tests green (121). Manual end-to-end smoke-testing has been ongoing throughout, live-verified by the user with no issues.

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
    intervals dominated. A live-game *timing* comparison (as opposed to correctness, which the user
    has been continuously verifying live) remains open.

**Exit criteria: met.** Accord removed + on net8, async pipeline with `CancellationToken` + channel
completion, engine pool derived from `Environment.ProcessorCount`, parity tests green (79 total),
primitive-level timing benchmark shows large measured improvements.

---

## 5. Phase 2 — Architecture 🔄 in progress (started 2026-07-01)

**Goal:** testable, decoupled core; remove global statics; prepare for UX work.

> **Sequencing note:** this is 5 large, interdependent sub-items — realistically the biggest phase
> yet, bigger than Phase 1. Landing as small, independently-verified vertical slices (same discipline
> as Phase 1), not attempted all at once. `2.1` is being extracted one service at a time; `2.2`–`2.5`
> not started.

### 2.1 Decompose `GenshinProcesor` ✅ done
Split the 957-line static class into injected services:
- **`IOcrService`** ✅ **done** — extracted the Tesseract engine pool (`engines`, `InitEngines`,
  `RestartEngines`, `AnalyzeText`, `BitmapToPix`) into `OcrService`, a real class with an explicit,
  I/O-free constructor (engine loading happens in `Restart()`, not eagerly). Unblocked something
  that was flagged as impossible back in Phase 0: touching *any* static member of `GenshinProcesor`
  used to eagerly load the whole Tesseract engine pool from disk as a static-constructor side
  effect, so OCR couldn't be unit-tested at all. `OcrServiceTests` now does real Tesseract
  recognition round-trips (render known digits → `AnalyzeText` → assert exact match) — verified
  passing with exact recognition, not just "close enough."
  - **Fully rewired in §2.2** (see below) — `GenshinProcesor.AnalyzeText`/`RestartEngines` are gone;
    every scraper now takes `IOcrService` via constructor injection.
- **`IImagePreprocessor`** ✅ **done** (this scope) — added `IImagePreprocessor`/`ImageProcessor`,
  an instance-method seam over the existing static `ImageProcessing` (extracted in Phase 1). The
  static class stays as the actual implementation — it's already pinned pixel-for-pixel to Accord's
  output by `ImagePreprocessingParityTests`/`KirschBlobParityTests`, no reason to move that logic —
  `ImageProcessor` just delegates each method, giving §2.2's DI wiring something to construct and
  inject. `ImageProcessorTests` confirms the delegation matches the static calls directly.
  - **Scoped deliberately smaller than the ideal end state:** the 11 existing call sites
    (`GenshinProcesor`, `ArtifactScraper`, `CharacterScraper`, `InventoryScraper`) still call
    `ImageProcessing.*` directly; rewiring them to take `IImagePreprocessor` via constructor is
    follow-up work for §2.2, same deferral pattern as `IOcrService`'s call sites.
- **`ILookupService`** 🔄 **started** — extracted the 8 `IsValidX` validity checks (set name,
  material, stat, slot, character, element, enhancement material, weapon) into `LookupService`, a
  pure static class. Deliberately **not** a stateful injected service like `OcrService`: each method
  takes its lookup data (`Dictionary`/`ICollection`) as an explicit parameter instead of capturing it
  at construction. Reason — `GenshinProcesor.ReloadData()` *reassigns* (not mutates) its lookup
  dictionaries (`Characters`, `Artifacts`, `Weapons`, `Materials`, etc.) every scan; a service that
  captured them via constructor injection would silently go stale after the first reload. Passing
  data as parameters sidesteps that risk entirely and, as a side effect, made these checks unit
  testable for the first time with small fake dictionaries (`LookupServiceTests`, 7 new tests).
  - **Scoped deliberately smaller than the ideal end state:** only the validity-check surface is
    moved. `GenshinProcesor`'s `IsValidX` methods remain as one-line forwarding wrappers that pass
    the current static fields into `LookupService`, so existing call sites are unchanged. The raw
    lookup dictionaries themselves (`Characters`, `Artifacts`, `Weapons`, `Materials`, `Elements`,
    `Stats`, `enhancementMaterials`, `gearSlots`) still live on `GenshinProcesor` and are still
    referenced directly by `Character.cs`, `ArtifactScraper.cs`, `CharacterScraper.cs`, and
    `MainForm.cs` — not yet routed through the service. The larger fuzzy-matching/normalization
    logic (the ~255-line "Element Searching" region) is separate, unexamined work.
- **`ITextNormalizer`** ✅ **done** (this scope) — extracted the fuzzy-matching/normalization logic
  from the old "Element Searching" region (`FindClosestGearSlot`, `FindClosestStat`,
  `FindElementByName`, `FindClosestWeapon`, `FindClosestSetName`,
  `FindClosestArtifactSetFromArtifactName`, `FindClosestCharacterName`,
  `FindClosestDevelopmentName`, `FindClosestMaterialName`, plus their private Levenshtein/similarity
  helpers) into `TextNormalizer`, same stateless-parameter shape as `LookupService` and for the same
  reason — `ReloadData()` reassigns the lookup dictionaries every scan. Dropped `CalcDistance_1`, a
  dead private method with zero call sites (confirmed via search before extraction), rather than
  porting unused code forward. `TextNormalizerTests` (10 new tests) exercises exact matches, fuzzy
  typo tolerance, and the empty-string-on-no-match fallback behavior — the last of which surprised
  initial test-writing (`FindClosestInDict` reassigns its `source` parameter to the fuzzy-match
  result, so a total miss returns `""`, not the original input; existing callers already tolerate
  this).
  - **Scoped deliberately smaller than the ideal end state:** `GenshinProcesor`'s `FindClosestX`
    methods remain as one-line forwarding wrappers, same as `IsValidX`/`LookupService`. Call sites in
    `ArtifactScraper.cs`, `WeaponScraper.cs`, `MaterialScraper.cs`, `CharacterScraper.cs` are
    unchanged.

### 2.2 Introduce DI + Hosting ✅ services injected, container deferred
- **`IOcrService` constructor injection** ✅ **done** — `InventoryKamera` (the single orchestrator)
  now constructs one `OcrService` and passes it into all 5 scrapers' constructors
  (`WeaponScraper`, `ArtifactScraper`, `MaterialScraper` via the shared `InventoryScraper` base;
  `CharacterScraper` directly, since it doesn't share that base). `GenshinProcesor.AnalyzeText`/
  `RestartEngines` are deleted — no more static forwarding wrapper for OCR. This is a genuine
  composition root, not a DI container yet: no `Microsoft.Extensions.DependencyInjection` package
  added, since there's currently exactly one consumer per service and one constructor call site,
  which doesn't earn a container. Revisit once `IImagePreprocessor` gets the same treatment and the
  wiring in `InventoryKamera`'s constructor gets noisy enough to justify one.
  - **Mechanical fallout handled along the way:** several scraper methods that only ever called
    static `GenshinProcesor`/`ImageProcessing` helpers had themselves been declared `static`
    (`ArtifactScraper.CatalogueFromBitmapsAsync`, `IsEnhancementMaterial`, and ~8 private
    `ScanArtifactX`/`ScanX` helpers across `ArtifactScraper`/`MaterialScraper`/`CharacterScraper`).
    Once those bodies needed the injected `ocrService` instance field, the methods themselves had to
    become instance methods too — flagged as likely follow-up work in the §2.1 write-up above, and
    it was: call sites in `InventoryKamera.cs` that used to say `ArtifactScraper.SomeMethod(...)`
    now go through the `artifactScraper`/`weaponScraper` instance fields it already held.
    `CharacterScraper.ScanMainCharacterName` stayed `static` but now takes `IOcrService` as an
    explicit parameter (mirrors `LookupService`/`TextNormalizer`'s shape) since it's called from
    `GenshinProcesor.AssignTravelerName`, a static method with no scraper instance to reach through.
- **`IImagePreprocessor` constructor injection** ✅ **done** — same treatment as `IOcrService`:
  `InventoryKamera` constructs one `ImageProcessor` and threads it through all 5 scrapers alongside
  `ocrService`. Deleted `GenshinProcesor`'s 5 thin image-op forwarding wrappers
  (`ConvertToGrayscale`, `SetContrast`, `SetInvert`, `SetThreshold`, `FilterColors`) now that nothing
  calls them; left the non-`IImagePreprocessor` image helpers (`SetGamma`, `SetColor`,
  `SetBrightness`, `ResizeImage`, `ScaleImage`, `CompareColors`, `ClosestColor`,
  `CompareBitmapsFast`, `CopyBitmap`) as static — they're pure functions with no interface seam to
  route through, so making them instance methods would just be static→instance churn with no
  actual state to eliminate.
  - Same "hidden `static` methods" fallout as the OCR slice, this time in `InventoryScraper`
    (`GetQuality`) and `CharacterScraper` (`GetRarity`→`ArtifactScraper.GetRarity`,
    `ScanConstellations`) — converted to instance methods once they needed `imagePreprocessor`.
    `CharacterScraper.ScanMainCharacterName` (already an `IOcrService`-parameter static method from
    the OCR slice) picked up an `IImagePreprocessor` parameter the same way, and
    `GenshinProcesor.AssignTravelerName` forwards both through.
- `Microsoft.Extensions.Hosting` / typed startup composition — not started. `InventoryKamera`'s
  constructor now wires two services by hand (`ocrService`, `imagePreprocessor`); still doesn't earn
  a DI container on its own, but is the last one before that tradeoff should be revisited.

### 2.3 Modern configuration ✅ scan logic done, UI intentionally untouched
- **Original plan** (quoted from the first draft of this doc) was "replace `Properties.Settings` +
  `JsonUserSettingsProvider` with `Microsoft.Extensions.Configuration` + a typed
  `IOptions<ScanSettings>` bound to a user `appsettings.json`." **Revised after investigating actual
  usage** — two things that first draft didn't account for:
  1. 52 of the 79 `Properties.Settings.Default.*` call sites are in `MainForm.Designer.cs`/
     `MainUI.Designer.cs` — WinForms Designer-generated two-way data bindings (e.g.
     `checkBox.DataBindings.Add("Checked", Properties.Settings.Default, "EquipWeapons", ...)`), which
     only work against a `System.Configuration.ApplicationSettingsBase`-derived object. Hand-rewriting
     these would mean replacing WinForms' native settings-binding with manual event-handler wiring
     throughout both forms — a much bigger, fragile change that risks the Designer regenerating over
     hand edits if the forms are ever reopened in it. **Left untouched** — still `Properties.Settings`.
  2. `Properties.Settings.Default` is read *live* at multiple different points in the scan lifecycle
     (some at scraper-construction time, e.g. `SortByLevel`; most per-call, e.g. `LogScreenshots`
     checked repeatedly mid-scan) specifically so a user's mid-session checkbox change (no
     restart/save needed) applies to the very next scan. A `Microsoft.Extensions.Configuration`
     snapshot loaded once from `appsettings.json` would silently go stale for exactly that case —
     `Properties.Settings.Default.Save()` only fires on app close, so a snapshot read from disk
     wouldn't even see in-session UI changes at all.
- **What was actually done:** added `IScanSettings`/`ScanSettings` — a thin instance-method seam,
  same shape as `IImagePreprocessor`/`ImageProcessor`, whose properties live-forward to
  `Properties.Settings.Default` under the hood rather than snapshotting it. This preserves both
  existing timing behaviors (constructor-time reads still happen once at construction; per-call reads
  still reflect live changes) while decoupling scan logic from the concrete WinForms settings type.
  Constructor-injected into all 5 scrapers and `InventoryKamera` alongside `ocrService`/
  `imagePreprocessor`. Replaced all 27 `Properties.Settings.Default.*` reads in scraper files +
  `InventoryKamera.cs` (the two files that own scan logic) with `scanSettings.*`; left
  `DatabaseManager.cs` (update-check), `GOOD.cs` (export format), `Navigation.cs` (process discovery),
  and `ExecutablesForm.cs`/`MainForm.cs`/`MainUI.cs` (UI) on `Properties.Settings.Default` directly —
  those are different concerns, not scan logic. `ScanSettingsTests` (3 tests) confirms the live
  pass-through behavior.
  - **Scoped deliberately smaller than the original idea:** the underlying persistence mechanism
    (`JsonUserSettingsProvider` writing `settings.json` to `%LocalAppData%`) is unchanged — this slice
    is only about *who reaches into it and how*, not *how/where it's stored*. A real migration off
    `Properties.Settings`/`ApplicationSettingsBase` entirely would need to also solve the WinForms
    Designer-binding problem above, which is realistically §2.5 (MVVM) territory — once `MainForm`
    binds to a view model instead of controls binding straight to `Properties.Settings.Default`, the
    same live-settings requirement can be satisfied by the view model instead.

### 2.4 Typed data models — investigated, deliberately deferred
- Original idea: replace `Dictionary<string,JObject>` for characters/artifacts with typed records;
  complete the System.Text.Json migration started in 1.4.
- **Investigated (2026-07-01), not implemented.** Direct usage is small (~11 call sites outside
  `LookupService`/`TextNormalizer`, which already take `Dictionary<string, JObject>` params from
  §2.1), but two real problems surfaced that the original one-line plan didn't account for:
  1. `Characters`/`Artifacts` are loaded from **remote, semi-externally-controlled JSON**
     (`DatabaseManager.LoadCharacters()`/`LoadArtifacts()`, fetched from a hosted database this repo
     doesn't own). One field, `ConstellationOrder`, is shaped differently per character — a flat
     array normally, a dictionary keyed by element for Travelers
     (`Characters[name]["ConstellationOrder"][element][0]` in `CharacterScraper.cs`). A strict typed
     record needs a custom converter to tolerate that shape switch, and any future drift in the
     remote schema becomes a hard deserialization failure instead of just an ignored field — a
     meaningfully different risk profile than typing purely local, self-controlled data.
  2. It's not read-only: `GenshinProcesor.UpdateCharacterName()` mutates the loaded data in place
     (`Characters[target]["CustomName"] = name`) to implement the Traveler/Manequin custom-name
     feature. A record model needs to decide how to handle that one mutable field without losing the
     rest of the read-only safety a typed model is meant to buy.
  - **Decision:** skip for now rather than force a design under time pressure. Small enough surface
    area to revisit later without blocking anything else in Phase 2; the eventual owner will need to
    resolve both the tolerant-deserialization and the mutable-field questions before this is a clean
    win over `Dictionary<string, JObject>`.

### 2.5 Decouple UI from logic (MVVM-lite) 🔄 six slices done — only character display remains
- **`IScanProgressReporter` seam** ✅ **done** — added the interface, initially implemented by a
  `UserInterfaceReporter` that delegated every method straight to the existing static `UserInterface`
  (same instance-method-seam shape as `IImagePreprocessor`/`ImageProcessor` and `IScanSettings`/
  `ScanSettings`). Constructor-injected into all 5 scrapers and `InventoryKamera` alongside
  `ocrService`/`imagePreprocessor`/`scanSettings`; replaced the ~48 `UserInterface.*` calls in scraper
  files and `InventoryKamera.cs` with `progressReporter.*`. Left `GOOD.cs` (1 call, export logic) and
  `MainForm.cs` (`UserInterface.Init` itself plus a few direct status/reset calls from UI event
  handlers, not scan logic) on the static class — same "not scan logic" scoping used in §2.3.
- **`ScanViewModel` — first real MVVM slice, counters group** ✅ **done** — replaced
  `UserInterfaceReporter` with `ScanViewModel`, which owns genuine observable state (not a delegating
  bridge) for exactly one control group: the weapon/artifact/character counters. `MainForm` now owns
  one long-lived `ScanViewModel` instance (declared *before* the `InventoryKamera data` field so its
  static initializer runs first — important because `MainForm` recreates `data` per scan but the view
  model needs to outlive that so subscribers never re-subscribe), subscribes to
  `ScanViewModel.CountersChanged` once at startup, and renders the 5 counter labels itself in that
  handler — instead of `UserInterface` owning those controls directly. `UserInterface`'s
  `SetWeapon_Max`/`SetArtifact_Max`/`Increment*Count`/`ResetCounters`/`ResetAll` and their backing
  `Label` fields are deleted (dead once nothing called them); `UserInterfaceReporter` is deleted too
  (its only use was inside `InventoryKamera`'s constructor, which now takes an injected
  `IScanProgressReporter` from `MainForm` instead of constructing one itself).
  - **Preserved an easy-to-miss detail:** the original `ResetCounters()` set the *count* labels to
    `"0"` but the *max* labels to `"?"` (unknown until `SetWeapon_Max`/`SetArtifact_Max` run) — not the
    same value. `ScanViewModel.WeaponMax`/`ArtifactMax` are `int?`, null until set, so `MainForm`'s
    render handler reproduces the same `"?"` placeholder via `?.ToString() ?? "?"`.
  - **This part is genuinely unit-tested** (`ScanViewModelTests`, 4 tests) — unlike the rest of
    `IScanProgressReporter`, counter state is plain fields + a C# event with no `Control.Invoke`
    dependency, so it doesn't have the WinForms-message-loop testing problem the rest of this seam has.
  - **Everything else in `IScanProgressReporter` still bridges straight to `UserInterface`** at the
    time this slice landed — character display, mora/material display, navigation image. Carving those
    out into more `ScanViewModel` state is the same kind of slice, done one control group at a time
    with live testing after each, per the sequencing note below. `MainForm.cs`'s Designer-generated
    control wiring for those groups is untouched.
- **`ScanViewModel` — status/errors group** ✅ **done** — same treatment as the counters slice:
  `ScanViewModel` now owns `ProgramStatus`/`ProgramStatusOk` state (raising `ProgramStatusChanged`)
  and error reporting (`ErrorAdded(string)` per error, `ErrorsReset` on clear) instead of
  `UserInterface` owning `programStatus_Label`/`error_TextBox` directly. `MainForm` subscribes to all
  three events once at startup and renders/appends into those controls itself. `AddError` is the most
  exercised method on the whole interface — used from scan logic across every scraper plus
  `InventoryKamera.cs`'s error handling — so this slice touched real, frequently-hit code, not just
  edge cases.
  - `UserInterface.SetProgramStatus`/`AddError`/`ResetErrors`/`ResetAll` and their backing
    `programStatus_Label`/`error_TextBox` fields are deleted, same as the counters slice's dead-code
    cleanup. `MainForm.cs`'s own direct status/error calls (10 call sites, e.g. "Scanning" on Start,
    "Stopping scan..." on the Stop hotkey) now go through `scanViewModel` too, since `MainForm` already
    holds that instance.
  - `GOOD.cs`'s single `AddError` call (file-export failure) couldn't stay on the now-deleted static
    method; `GOOD.WriteToJSON` picked up an `IScanProgressReporter` parameter from its one caller in
    `MainForm.cs`, rather than reintroducing a static bridge just for one call site.
  - No new tests for this slice specifically (status/errors still needs `Control.Invoke` in `MainForm`'s
    handlers, same testing constraint as the rest of the non-counters surface) — verified by
    compilation plus the existing scraper test coverage exercising the same `AddError` call paths.
- **`ScanViewModel` — gear display group** ✅ **done** — same treatment again: `ScanViewModel` now
  owns `GearImage`/`GearText` state (raising `GearChanged`) instead of `UserInterface` owning
  `gear_PictureBox`/`gear_TextBox` directly. Unlike the counters/status groups (plain value state),
  this one holds a `Bitmap`, so ownership/disposal needed explicit handling: `SetGearImage` clones the
  incoming bitmap (matching the original `UpdatePictureBox`'s defensive clone, so scan logic can freely
  dispose its own copy) and disposes the *previous* `GearImage` before replacing it, preventing a slow
  GDI-handle leak across a long scan.
  - **Found a real concurrency bug via live testing, not caught by build/test:** `InventoryKamera`'s
    worker pool runs 2-3 background threads concurrently, any of which can call `SetGear`/
    `SetGearPictureBox` for a different weapon/artifact at the same time. The first version disposed
    the old image and reassigned the field as two unsynchronized steps; a second thread could read the
    field in the gap between them and hand `MainForm` an already-disposed `Bitmap`, which WinForms
    renders as a white box with red X's (its broken-image placeholder) — exactly what the user saw
    mid-scan. Fixed with a lock around the dispose-and-replace, plus a new `CloneGearImage()` method
    that `MainForm` calls instead of reading the `GearImage` property directly — it clones under the
    same lock, so the renderer always owns an independent copy no concurrent thread can dispose out
    from under it. `MainForm`'s handler also now disposes its *own* previous `PictureBox.Image` before
    replacing it, since it owns full copies now instead of a shared reference.
  - **Unlike status/errors, this part is genuinely unit-tested** (6 new `ScanViewModelTests`, including
    a regression test for the concurrency fix) — the clone-not-reference and dispose-on-replace
    behavior doesn't need a live `Control`, just a `Bitmap`, so it doesn't have the WinForms-message-loop
    constraint the render-handler side does.
  - `UserInterface.SetGear`/`SetGearPictureBox`/`SetGearTextBox`/`ResetGearDisplay` and their backing
    `gear_PictureBox`/`gear_TextBox` fields are deleted, same dead-code cleanup pattern as the other
    slices.
  - **Remaining in `IScanProgressReporter`, still bridging to `UserInterface`:** character display
    (name/element/level/constellation/talents) only — see below for why it's deliberately deferred.
- **`ScanViewModel` — material/mora display group** ✅ **done** — same treatment as gear: `ScanViewModel`
  now owns `MaterialText`/material nameplate+quantity images and `MoraText`/mora image (raising
  `MaterialChanged`/`MoraChanged`) instead of `UserInterface` writing into them directly. Full-replace
  semantics (not `ErrorAdded`'s incremental-append pattern) — `MaterialScraper` calls
  `ResetCharacterDisplay()` immediately before every `SetMaterial`/`SetMora`, so the original UI only
  ever showed the most recently scanned material, not an accumulating log, same as gear.
  - **Notable wrinkle: materials/mora reuse character display's controls.** `SetMaterial`/`SetMora`
    write into `cName_PictureBox`/`cLevel_PictureBox`/`navigation_PictureBox`/`character_TextBox` — the
    exact same WinForms controls the still-unconverted character-display methods use (this coupling
    predates this slice; not something introduced here). Since character display stays on the static
    `UserInterface` bridge for now, those backing fields and `UserInterface.Init`'s signature are
    untouched — only the two now-dead methods (`SetMaterial`/`SetMora`) were removed from
    `UserInterface`. `MainForm` renders both `ScanViewModel`'s new material/mora state and (for now)
    `UserInterface`'s character-display state into the same target controls; they don't run
    concurrently (materials/mora scan as their own sequential phase, not through the worker pool), so
    there's no ordering conflict, just two code paths converging on the same controls until character
    display is converted too.
  - Reused the `imageLock`/`CloneBitmap` infrastructure from the gear slice's concurrency fix (renamed
    from `gearLock`) rather than duplicating it — materials/mora are called from `MaterialScraper`'s
    single scan thread, not the concurrent worker pool weapons/artifacts use, so the lock isn't
    strictly required for correctness today, but keeping the same defensive pattern avoids relying on
    "this happens to be single-threaded" as a correctness invariant that could silently break later.
  - `ResetAll()` now also disposes/clears the material and mora images, matching gear's reset hygiene.
  - 5 new `ScanViewModelTests` (text/image state, dispose-on-replace safety). No test for the `ResetAll()`
    path specifically — it also calls `UserInterface.ResetCharacterDisplay()`, which needs live WinForms
    controls `UserInterface.Init` never receives in a headless test, so it would crash there; the same
    testing constraint noted for character display.
- **`ScanViewModel` — navigation image group** ✅ **done** — the last group besides character display.
  `ScanViewModel` now owns a generic `NavigationImage` (raising `NavigationImageChanged`) instead of
  `UserInterface.SetNavigation_Image` writing into `navigation_PictureBox` directly; same
  clone/lock/dispose pattern as gear/material/mora. This is a broad "current capture region" preview
  called from every scraper (weapons/artifacts/characters/materials), not tied to one scan phase.
  - `navigation_PictureBox` shares its physical control (`Navigation_Image` in `MainForm`) with mora
    display — that coupling already existed before this slice (both `SetMora` and
    `SetNavigation_Image` wrote into the same control originally); `MainForm`'s `OnNavigationImageChanged`
    and `OnMoraChanged` handlers both still write into `Navigation_Image`, preserving it rather than
    trying to separate concerns that weren't separated in the original design.
  - `UserInterface.SetNavigation_Image` and its backing `navigation_PictureBox` field (now fully dead —
    it had no other users once this was the last method touching it) are deleted, along with the
    corresponding parameter on `UserInterface.Init`.
  - 3 new `ScanViewModelTests` (clone-not-reference, dispose-on-replace).
  - **This closes out §2.5's non-character surface.** Every `IScanProgressReporter` method scan logic
    calls now goes through real `ScanViewModel` state except the character-display group, which stays
    deliberately deferred: Genshin has added keyboard-navigable controls to most of the character
    screen (everything except clicking into constellations and talents, which still need the mouse),
    so the user is planning a navigation revamp there — converting the display layer ahead of that
    redesign risks being thrown away.

**Bugs found during §2.5 live testing (2026-07-01), unrelated to the MVVM changes:**
- **Negative list index in `ArtifactScraper.ScanArtifacts`/`WeaponScraper.ScanWeapons`:** both compute
  a queueing loop's start index as `(rows - (totalRows - rowsQueued)) * cols`. When `GetPageOfItems`
  falls back to a previous page's row count (the `NullReferenceException` fix from earlier in this
  session), `rows` can legitimately differ from what the caller's `totalRows`/`rowsQueued` bookkeeping
  assumed, driving this negative — and a negative `List<T>` index throws exactly this error. Same
  "one crash was masking another" pattern as the cancel-latency bug: this was very likely unreachable
  before the `GetPageOfItems` fix, since the app crashed earlier in that method first. Fixed by
  clamping to `Math.Max(0, ...)` in both scrapers.
- **`CharacterScraper.ScanCharacter` NRE on `character.Element.ToLower()`:** the name/element
  validation guard only checked `string.IsNullOrWhiteSpace(name)`, so a character whose *name* scan
  succeeded but *element* scan failed fell through with a null `character.Element`, crashing later.
  Fixed the guard to check both.
- **`CharacterScraper.ScanCharacter` NRE on missing `ConstellationOrder`:** a character found in
  `GenshinProcesor.Characters` but missing its `ConstellationOrder` field crashed instead of failing
  gracefully. Per the user: every character in the database should have this field, so its absence
  means the character data failed to fully download/parse — **not** a legitimately-missing field to
  silently tolerate. Fixed to surface `progressReporter.AddError($"{character.NameGOOD}: missing
  ConstellationOrder data...")` and skip just the talent-scaling adjustment for that character, rather
  than either crashing the whole scan or silently proceeding with wrong talent levels.

**Remaining §2.5/Phase 3 sequencing, planned but not started (2026-07-01):** the user wants the UI
visually modernized eventually (dark mode, better layout/progress display, more guided flow) and
asked for a recommendation. **Stay in WinForms rather than migrate to WPF/MAUI/a web UI** — this is a
single-window automation utility with no cross-platform need, and a framework migration is a full
rewrite disproportionate to the visual payoff; WinForms has enough modern theming options (owner-draw
dark mode, updated layout/typography, lightweight third-party control libraries) once the architecture
underneath is clean. Sequencing:
1. **`ScanViewModel` + real MVVM (§2.5, remaining work).** Replace `UserInterface`'s direct
   `Control.Invoke` writes with an observable view model (plain C# events or `INotifyPropertyChanged`
   — doesn't need a full MVVM framework given the app's single-window scope) that `MainForm`
   subscribes to and marshals onto the UI thread itself, instead of the facade owning that
   responsibility. `IScanProgressReporter`'s public surface (already just 24 semantic methods scan
   logic calls) is designed to map cleanly onto this — a `ScanViewModel`-backed implementation could
   plausibly satisfy the same interface, meaning scan logic and its tests wouldn't need to change
   again. `MainForm.cs` (~676 lines) and its Designer-generated bindings would need rewiring to read
   from/subscribe to the view model instead of owning control state directly.
2. **Verification strategy — apply the WGC lesson.** The WGC capture rewrite earlier this session was
   reverted specifically because build+test-green didn't catch real-world failures that only live
   testing surfaced (HDR washout, overlay capture). This MVVM rewrite has the same risk shape — a
   headless test can confirm the view model's state machine is correct, but not that `MainForm`
   updates correctly on screen. Do this as its own small, isolated slices (e.g., one control group at
   a time — gear display, then character display, then counters/status) with a live smoke test after
   each, rather than one large rewrite verified only at the end.
3. **UX modernization, not just a visual reskin (Phase 3, already scoped below).** This is more than
   theming: live per-category progress with counts/ETA (§3.1), pre-flight validation that catches bad
   configs before a scan wastes 20+ minutes (§3.2), inline OCR review/correction instead of dumping
   failures into `logging/` for a GitHub issue (§3.3), dark mode/DPI/accessibility polish (§3.4), and
   guided onboarding + a real error-reporting flow (§3.5). All of it sits on top of the view model from
   step 1 — pre-flight checks and inline correction in particular need two-way state (the view model
   reacting to user corrections mid-scan), which direct `Control.Invoke` calls from background threads
   can't cleanly support. Doing any of this before step 1 exists means redoing it once the
   control-binding story changes underneath it.

**Exit criteria:** no `static` mutable engine/lookup state; services unit-tested in isolation; UI receives progress through an abstraction; behavior parity maintained. **Not yet met** — both genuinely stateful/mutable services (`IOcrService`'s engine pool, `IImagePreprocessor`) are now off statics and constructor-injected (✅) across all 5 scrapers, scan logic's config reads go through `IScanSettings` instead of `Properties.Settings.Default` directly (✅), and scan logic's progress-reporting calls go through `IScanProgressReporter` instead of the static `UserInterface` directly (✅). `LookupService`/`TextNormalizer` are intentionally stateless static classes (no mutable state to remove — they take the lookup data as parameters each call), but the lookup *dictionaries themselves* still live as mutable static fields on `GenshinProcesor`; moving those into an owned, non-static data store is unstarted follow-up work (likely folds into §2.4's typed models). `Properties.Settings.Default` and the static `UserInterface` are both still the underlying mechanisms behind their respective seams (by design — see §2.3/§2.5). The actual "UI receives progress through an abstraction" criterion — an observable view model instead of direct control manipulation — hasn't started.

---

## 6. Phase 3 — UX modernization

**Goal:** turn the "don't touch your mouse and wait" black box into a guided, transparent tool. Built on the Phase 2 view model.

### 3.0 Declutter/reorganize MainForm 🔄 substantially done (started 2026-07-01, most recent work 2026-07-05)
**Why this is first, ahead of §3.1–3.5's feature list:** the user's own words — "the UX of the program
[is] super cluttered and things are all over the place." Adding more features (live progress, inline
correction, onboarding) onto a cluttered layout compounds the problem instead of fixing it. This is a
layout/information-architecture pass on the existing `MainForm`, not new functionality — group related
controls, fix visual hierarchy, cut down on cramming everything into one dense screen.

Landed as individually-verified slices, live-tested via user screenshots each round (no computer-use
access to the running app in this environment — the user pasted screenshots each iteration instead):
- **Grouped the flat control soup** into labeled `GroupBox` sections (What to Scan, Filters/Output,
  Character Names, Output) — fixed several real label/control overlap bugs found only via screenshot
  (e.g. "Char Development Items" checkbox text physically overlapping the artifact-page-count column).
- **Tried a `TabControl`** to fit everything in the original window footprint; then, per user feedback,
  **moved Character Names + Output into a dedicated modal `SettingsForm`** (`InventoryKamera/ui/
  SettingsForm.cs`/`.Designer.cs`) reached via a new "Advanced Settings..." item on the Options menu —
  removes that content from `MainForm` entirely rather than hiding it behind a second tab. `MainForm`
  now shows only "What to Scan" + "Output" (renamed from "Filters" — it holds rarity/level thresholds
  that filter the *exported* data, not scan-time filters) directly, with the scan-controls column moved
  into the space beside the output panel (matching how the original layout used that space) instead of
  a horizontal band above it. Final size **595×495**, close to the original **595×519** footprint.
- **Fixed the "0 = scan all" ambiguity**: added explicit "All" checkboxes next to the artifact-pages and
  character-count numeric selectors instead of relying on an undocumented sentinel value; capped the
  character-count selector at 10 (was uncapped).
- **Visual theme pass** (2026-07-05, "make it look modern"/"look like Claude Desktop"): swapped the
  dated "Microsoft Sans Serif" default font for Segoe UI everywhere; added `InventoryKamera/ui/
  UiTheme.cs` holding a warm cream/terracotta palette (background `#F5F4EE`, accent `#CC785C`) plus a
  `RoundCorners` region-clip helper applied to the primary action buttons; added `InventoryKamera/ui/
  FlatGroupBox.cs`, an owner-drawn `GroupBox` subclass with a thin flat border instead of the OS's
  notched/beveled one, used for all four groups; `NumericUpDown.BorderStyle = FixedSingle` for a flat
  border (spinner arrows stay native — a full custom spinner control was scoped but declined as
  higher-risk/effort than warranted); `DwmSetWindowAttribute`-based native title-bar tinting
  (`UiTheme.ApplyWindowChromeTint`, Windows 11 22H2+ only, silently no-ops on older Windows).
- **Known ongoing friction, not yet resolved:** opening either `MainForm` or the WinForms Designer
  surface in Visual Studio has repeatedly (3+ times this session) triggered a full re-serialization of
  `MainForm.Designer.cs` that (a) strips `global::` qualifiers needed because the `InventoryKamera`
  *namespace* collides with the `InventoryKamera` *class* name, breaking the build, and (b) rewrites
  every `Properties.Settings.Default`-bound control to bind against a throwaway `new Settings()`
  instance instead — which would silently reset all user settings to hardcoded defaults on every launch
  if left unfixed. Recovered each time by removing the throwaway instance and restoring
  `global::InventoryKamera.Properties.Settings.Default` bindings. **If this keeps recurring, worth
  either renaming the `InventoryKamera` class to remove the collision, or asking the user to edit these
  two files via "View Code" rather than the Designer surface.**
- **Live-verified (2026-07-05):** the user has been running the live app continuously throughout this
  work (not just per-slice screenshots) and confirms no issues with the reorganized layout, the
  Advanced Settings dialog, or the visual theme end-to-end.

### 3.1 Live scan feedback — ✅ done (2026-07-05), live-verified
- Per-category progress (characters / weapons / artifacts / materials) with running counts + ETA —
  done. `ScanViewModel` gained `MaterialCount` (distinct materials scanned so far, no max since
  material scanning has no known total upfront), `CharacterMax` (set only when a fixed character
  count is chosen instead of "All"), and `EstimatedTimeRemaining` (extrapolated from progress-so-far
  across whichever categories have a known max). `MainForm` gained a Materials count row and an ETA
  label. Live thumbnail of current capture/last-recognized item was already substantially covered by
  the existing Gear/Material/Navigation image displays from Phase 2 §2.5 — not rebuilt separately.

### 3.2 Pre-flight validation — 🔄 mostly done (2026-07-05), live-verified
- Before scanning, detect game window resolution, aspect ratio (16:9 / 16:10), and keybinds; warn
  inline. **Done**: new `PreflightChecksPass()` in `MainForm.cs`, called at the top of
  `StartButton_Clicked` before any scan state changes — catches duplicate/conflicting keybinds
  (including a keybind accidentally set to Enter, which collides with the Stop hotkey), Genshin not
  running, unsupported aspect ratio, and fullscreen window-size mismatch. These checks previously
  lived deep inside the scan thread (after "Scanning" status + hotkey registration already fired) —
  moved to fail immediately and visibly instead.
- **Not done: HDR detection and language detection.** The previous DXGI-based `HdrDetector` was fully
  removed when §6b's capture rewrite was reverted — rebuilding it is real native-interop work
  (adapter/output enumeration), deliberately scoped out of this pass as a separate follow-up. Language
  detection wasn't pursued since only "ENG" is currently supported at all (nothing to detect against).

### 3.3 Inline OCR review/correction — 🔄 first slice done (2026-07-05)
- **Done**: `IOcrService` gained `AnalyzeTextWithConfidence`, exposing Tesseract's `Page.GetMeanConfidence()`
  (previously read by nothing in the codebase — every caller discarded it). New `OcrConfidenceThreshold`
  setting (default 60%, `IScanSettings`/`ScanSettings`/`Settings.settings`/`Settings.Designer.cs`), with
  a numeric control in `SettingsForm` under Output (bound the same way every other setting there is).
  `ScanViewModel` gained `RequestCorrection`/`CorrectionRequested` (via `IScanProgressReporter`,
  `OcrCorrectionEventArgs`) — the scan thread calls it and blocks; `MainForm.OnCorrectionRequested`
  shows a new modal `ui/OcrCorrectionForm` (captured image + recognized text + editable field) inside
  `Control.Invoke`, whose blocking-until-`ShowDialog`-returns behavior is the entire pause mechanism —
  deliberately no separate wait handle/`ManualResetEventSlim`, reusing the same `Invoke`-blocks-the-caller
  idiom every other `ScanViewModel` event already relies on. `OcrCorrectionForm` is built entirely in
  code (no `.Designer.cs`) to avoid the `MainForm.Designer.cs`-corruption risk documented in §3.0.
  **Wired into one representative call site so far**: `InventoryScraper.ScanItemCount()` — the exact
  path behind the "Unable to locate Artifacts item count" error hit earlier this session. Below-threshold
  or blank OCR now pauses for correction instead of going straight to the `logging/`-dump +
  `FormatException` path (which still runs afterward if the user's correction is also blank/empty, so
  the failure mode degrades gracefully instead of changing shape). All other `AnalyzeText` call sites
  (item names, material quantities, character stats) are untouched — expanding coverage is a follow-up,
  done deliberately as small live-tested slices per the plan's own §2.4 lesson about the WGC rewrite.
  Build + all 127 tests green; **not yet live-verified against a real low-confidence scan** (the
  threshold has to actually be crossed to see the dialog — pending user testing).
- **Expanded to two more call sites (2026-07-05)**: weapon name (`WeaponScraper.ScanWeaponNameWithCorrection`)
  and artifact set name (`ArtifactScraper.ScanArtifactSet`) — both core identifying fields where a
  misread silently corrupts export data, unlike lower-stakes fields like enhancement-fodder material
  names (deliberately left ungated — that path runs dozens of times per scan on junk items, so gating
  it would mean a popup almost every scan). Added debug logging matching the item-count slice so
  triggering can be confirmed from `logging/InventoryKamera.debug.log` without relying on the dialog
  being visually noticed.
- **Real bug found via live testing, then fixed same day**: the correction dialog *popped up*, but
  the scan kept clicking/scrolling to the next item anyway instead of pausing. Root cause:
  `ArtifactScraper`/`WeaponScraper`'s `QueueScan` writes captured images to a shared
  `InventoryKamera.workerChannel` and returns immediately — actual OCR/recognition (including the
  correction gate) runs later on a separate background worker thread pool, fully decoupled from the
  main loop that's clicking through items and scrolling the game. Blocking a worker thread inside
  `RequestCorrection` therefore had no effect on the loop still driving the game forward. **Fixed** by
  adding a shared gate to `ScanViewModel`: a `correctionsPending` counter (not a single flag —
  multiple low-confidence recognitions can be in flight on different workers at once) plus a
  `ManualResetEventSlim` that closes when the count goes 0→1 and reopens at 1→0, exposed via
  `IScanProgressReporter.WaitIfCorrectionPending()`. Both scrapers' click loops call it before each
  item's click and before each page's scroll, so the game genuinely stalls for as long as any
  correction is outstanding, then resumes automatically once every pending one resolves. Build + all
  127 tests green. **Live-verified (2026-07-05):** the scan now genuinely pauses (game stops being
  clicked/scrolled) while a correction dialog is open, and resumes automatically once resolved.
- **Not done**: confidence-gated correction at any other call site; the confidence threshold is
  currently a single global setting, not per-field.

### 3.4 Visual & accessibility polish
- ~~PerMonitorV2 DPI awareness (modern WinForms) for crisp scaling on high-DPI displays.~~ **Done as a
  side effect of the 4K windowed-mode capture-position bugfix (2026-07-05)** — see §7's "4K/high-DPI
  scan performance" entry for the full saga. `Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)` in
  `Program.cs` + `<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>` in the csproj (the
  manifest-based declaration doesn't work for .NET 5+ WinForms, per the SDK's own `WFAC010` warning).
  Live-verified: app UI renders correctly, no layout regressions observed.
- ~~Dark mode + consistent theming.~~ **Done (commit e9a60a2)** — Options-menu Dark Mode toggle backed
  by a new `DarkMode` setting; `UiTheme.ApplyTheme` recursively themes MainForm/SettingsForm/
  OcrCorrectionForm with an exclusion list for deliberately-colored controls, plus a custom
  `ToolStripProfessionalRenderer` color table for menus.
- Keyboard navigation and clear status/error surfaces — still open.

### 3.5 Onboarding & errors
- First-run guided setup wizard (resolution/language/keybinds).
- Friendly error panel with "copy diagnostics" (zips `logging/` + version) to streamline bug reports.

**Exit criteria:** real-time progress, pre-flight checks, inline correction shipped; DPI + dark mode; positive scan-success-rate feedback.

---

## 6b. Capture modernization — Windows.Graphics.Capture — 🔻 tried, reverted (2026-07-01)

> **Status: implemented, tested against real usage, reverted.** Not abandoned forever, but the
> straightforward version doesn't deliver enough real-world value to justify keeping — see below.

### Root cause (still accurate)
All capture goes through `Graphics.CopyFromScreen` in `game/Navigation.cs` — a GDI BitBlt of the
**desktop** at the game window's screen coordinates. It photographs whatever is on screen there, so:
- **Overlays** composited over the window are captured verbatim, corrupting OCR regions.
- **HDR**: with HDR on, GDI reads back an SDR tone-mapped approximation, shifting every pixel value
  against the app's hard-coded SDR calibration (grayscale thresholds, brightness checks, rarity
  colour matching).

### What was built (and how it verified)
`IScreenCapture` seam + `GdiScreenCapture` (unchanged behaviour, default) + `WgcScreenCapture`
(`GraphicsCaptureItem` → `Direct3D11CaptureFramePool` → `GraphicsCaptureSession`, own message-pumped
thread, latest-frame cache) + `HdrDetector` (DXGI-based HDR pre-flight warning), gated behind an
opt-in `CaptureBackend` setting ("Gdi" default / "Wgc"). Fully implemented, built clean, verified
against a real synthetic window and real display hardware on the dev machine (no live game session
available in this environment). Full implementation notes and the interop gotchas encountered are
preserved in the [[wgc-interop-patterns]] memory even though the code itself was reverted — the
technique works and would be the starting point if this is revisited.

### Why it was reverted — real user feedback, not speculation
The user tested the opt-in `"Wgc"` backend against the actual game and found **both target problems
still occurred in practice**:
1. **HDR**: still "white-washed" — text/background contrast collapsed, unreadable. Leading theory
   (untested further): Windows' "SDR content brightness" slider (Settings → Display → HDR) boosts
   SDR-range content when HDR is active, and that boost rides along into WGC's 8-bit
   (`B8G8R8A8UIntNormalized`) capture of the game's HDR-rendered frame. This was the exact open
   question flagged in the original design ("does B8G8R8A8 capture from an HDR swapchain match
   native SDR, or does it need an explicit tone-map?") — the answer in practice was no, it doesn't
   match closely enough, and neither backend correctly handles HDR today.
2. **Overlays**: the user's actual overlay (Outplayed, a gameplay-recording/clip tool) still showed
   up in WGC captures. Root cause: WGC only excludes overlays implemented as **separate
   compositor-level windows/layers**. Overlays that **hook directly into the game's own
   DirectX/OpenGL/Vulkan rendering calls** (drawing into the same frame buffer before it's
   presented) are baked into the frame by the time WGC reads it, indistinguishable from the game's
   own content. This turns out to describe most popular overlay/recording software — Discord, Steam,
   NVIDIA GeForce Experience, RTSS, and recording tools like Outplayed/Medal.tv all use hooking for
   exactly this reason (lower latency, reliable frame-accurate capture for their own recording
   features). So WGC's overlay-exclusion benefit is real but much narrower in practice than the
   original framing suggested — it doesn't help against the overlays people actually run day to day.

Given neither of the two problems this was meant to fix was actually fixed for the user's real
setup, and the implementation adds real complexity (native interop, a dedicated pumped thread, new
dependencies), it was reverted via `git revert` rather than kept as a not-quite-working opt-in.

### If this gets revisited
- **HDR**: the SDR-content-brightness-slider theory is untested past the "try lowering it and see"
  stage — worth confirming before attempting a code fix (e.g. querying the display's actual SDR
  white level and correcting for it, or capturing raw HDR format and doing a calibrated tone-map
  ourselves). Both are real, nontrivial work that needs iterative testing against live HDR content,
  which isn't available in this dev environment.
- **Overlays**: a WGC-based fix only ever helps against compositor-level overlays, not hook-based
  ones. If overlay exclusion is still wanted, the practical options are either accepting that
  narrower scope, or a fundamentally different approach (e.g. detecting known overlay/recording
  processes and warning the user to close them before scanning — the original "Tier 1" idea from
  before the full rewrite was attempted).
- The `IScreenCapture` seam pattern itself is sound and reusable regardless — reintroducing it
  wouldn't need to be redone from scratch.

---

## 6c. Scan input revamp — controller-driven navigation — ✅ all five scan types (weapon/artifact/materials/dev-items/character) live-verified and wired in, mouse-mode code removed (started 2026-07-05, weapon scan proven 2026-07-04, character scan + cleanup landed 2026-07-05)

**Motivation:** artifact/weapon grid-item detection (`ProcessScreenshot`/`GetPageOfItems` in
`InventoryScraper.cs`) reconstructs the item grid from blob-detected column/row coordinates —
reliable most of the time, but if an entire row or column has zero detected blobs, that row/column
silently never appears in the reconstructed grid (see the live-testing note under §2.5's bugs list).
Separately, character-screen navigation currently uses mouse clicks at computed screen-percentage
coordinates. The idea: drive grid/menu navigation with a game-native cursor (arrow/D-pad moving a
selection, a confirm button to select) instead of mouse clicks against computed pixel coordinates —
this sidesteps the blob-detection failure mode entirely, since the cursor's grid position is known
deterministically rather than re-detected from a screenshot each time.

**Keyboard-only ruled out (2026-07-05):** live-tested by the user — Genshin's UI does **not** have
full keyboard-only navigation coverage. This directly contradicts the original plan (based on earlier
research suggesting keyboard nav had shipped broadly) and rules out keyboard as the sole input method
for a complete revamp.

**Controller input confirmed viable instead (2026-07-05):** the user confirmed Genshin **does** have
full controller-input support for its UI. This flips the original prioritization — controller,
not keyboard, is now the path to full navigation coverage. A feasibility spike
(`InventoryKamera/game/ControllerSpike.cs`, `Nefarius.ViGEm.Client` package, temporary "Test
Controller Input (Spike)" Options-menu item wired in `MainForm.cs`) confirmed a ViGEmBus-backed
virtual Xbox 360 controller works end-to-end: connecting the virtual device + nudging the left stick
+ tapping the A button while Genshin was focused made the game switch to its controller UI scheme.
(It reverted a moment later only because the spike disconnects the virtual device right after that
one test press — expected for a one-shot test, not a sign of unreliability.) **Revises the earlier
"ViGEmBus archived Nov 2023 → too risky" call from 2026-07-01** — the driver is unmaintained but
still functions correctly today; the risk is about long-term support, not current functionality, and
it's now the only viable path since keyboard-only doesn't cover the full UI.

**Known constraint (still applies):** per the user, clicking into constellations and clicking into
talents require mouse input regardless of input scheme — any design needs a mouse fallback for just
those two panels.

**Driver distribution decision (2026-07-05):** ViGEmBus is a kernel-mode driver — it can't be
silently installed by the app; Windows requires an explicit admin-elevation (UAC) consent for any
kernel driver install, which is an OS-level constraint with no code-side workaround. Two options were
weighed: (1) document it as a manual one-time prerequisite (simplest for us, but reintroduces the
"extra setup step" friction that Phase 0's single-file self-contained publish specifically eliminated
— see §0.5), or (2) bundle the official installer and offer a guided "Install Driver" button when the
spike's `VigemBusNotFoundException` fires. **Decision: go with (1), manual/documented prerequisite,
for now** — this feature is explicitly experimental/opt-in, and there's a real chance Genshin ships
full keyboard-only UI navigation in a future update, which would remove the need for a driver
dependency entirely. Revisit distribution UX (option 2) only if/when this graduates from experimental
to a default-on feature.

**Real implementation started (2026-07-05):** `InventoryKamera/game/GameController.cs` — a real (not
throwaway) service holding the virtual Xbox 360 controller connected for its lifetime, unlike
`ControllerSpike`'s connect-per-call pattern. `ControllerSpike.TapAButton` now delegates to it rather
than duplicating ViGEm calls. Live-tested and fixed through several real issues, each only found by
actually running it against the game:
- **200ms wasn't enough settle time** between the stick-nudge scheme switch and a subsequent Start
  press — Start didn't register. A stick nudge alone also weakly flips the visible prompt icons but
  isn't reliably enough to make the game start routing controller input for a button press; tapping A
  right after (matching the original spike's known-working sequence) is now part of
  `EnterControllerMode()`. Also added a 500ms settle delay right after `Connect()` in the constructor
  and a 1000ms delay before the first button press in test call sites — Windows/the game need a
  moment to actually enumerate the new virtual device.
- **Disconnecting the virtual controller while Genshin still expected controller input (e.g. a
  controller-driven menu was open) surfaced a blocking "controller disconnected, reconnect or exit"
  prompt.** Fixed with `GameController.ExitControllerMode()` — a net-zero real mouse nudge that
  makes Genshin switch back to keyboard/mouse control *before* the virtual device disconnects;
  `Dispose()` calls it automatically.
- **Grid navigation confirmed working**: the pause menu's tab bar is a 4-wide x 5-tall grid, cursor
  starts at `[0,0]` (top-left, column-row) when the menu opens. `GameController.MoveStep`/`Move`
  (discrete left-stick full-deflection taps, 150ms hold + 150ms settle each, horizontal/vertical legs
  never diagonal) live-verified moving `[0,0]` → `[0,2]` (Inventory, pure vertical) then confirming
  with A actually opened Inventory.
- **Combined horizontal+vertical movement confirmed working (2026-07-05)**: Character menu at `[2,1]`
  reached via `Move(Right, 2)` then `Move(Down, 1)` (two separate straight-line legs, not diagonal)
  and landed correctly. Between this and the `[0,2]` Inventory test, the core navigation primitives
  (`EnterControllerMode`, `OpenMenu`, `MoveStep`/`Move`, `TapButton`, `ExitControllerMode`) are now all
  live-verified for the pause-menu tab bar specifically.
- **Not yet tested**: navigation *within* a submenu once opened (Inventory/Character's own internal
  grids, not just the top-level tab bar — these may have different dimensions/behavior than the
  4x5 tab bar), reading back cursor position/selection state (still likely needs a screenshot-based
  check, much simpler than full blob detection), constellation/talent panels' confirmed mouse-only
  requirement, and how the virtual controller's lifecycle should be managed across a full scan
  (connect once at scan start vs. per-navigation-action — currently each test connects fresh per
  test-menu click). No general `NavigateTo(col, row)` helper yet — each test hand-codes its own
  Right/Down call sequence from the known `[0,0]` start; worth extracting once a couple more grids
  are confirmed to behave the same way.
- **A/B are swapped from standard Xbox convention (2026-07-05, corrected same day):** confirmed via
  direct user correction: **A is Genshin's back/cancel button, B is confirm/select** — the opposite of
  the usual Xbox mapping. Initial testing suggested Character's tab (`[2,1]`) opened fine with A while
  only Inventory (`[0,2]`) needed B, which looked like a per-tab inconsistency; that was wrong —
  Character opening with an A press was a red herring (likely landing on the tab was enough on its
  own, or something else coincidentally happened), not evidence A confirms. `MashBack` (previously B)
  and the Character-menu test (previously A) both corrected to match the real global mapping: A backs
  out, B confirms, everywhere. No more per-tab inconsistency once the mapping itself was fixed.
- **Safety net added: `GameController.MashBack(times, delayMs)`** repeatedly taps B to back out of
  however many menus deep a bad navigation sequence left the game in; over-pressing past the top
  level is harmless. `ExitControllerMode()` (and therefore `Dispose()`) now calls it automatically
  before the graceful KBM-switch mouse nudge, so every existing test's cleanup benefits for free.
  Also exposed as a standalone "Panic Button: Mash Back to Exit Menus" Options-menu item for manual
  recovery without needing to alt-tab and clean up by hand.
- **Inventory tab detection, live-verified end to end (2026-07-05):** Genshin remembers whichever
  inventory tab was last open, so navigation can't assume a known starting tab once inside Inventory.
  `ControllerNavigationTests.DetectCurrentTabIndex` captures a top-left region, OCRs it, and
  fuzzy-matches against the known tab order (`Weapons, Artifacts, Character Development Items, Food,
  Materials, Gadget, Quest, Precious Items, Furnishings`) via `TextNormalizer.FindClosestInList`
  (widened from `private` to `internal` to reuse it here). Two real bugs found and fixed via live
  screenshots: (1) the capture region's bottom edge clipped the Quest tab's text (Furnishings
  happened to sit more centered in the same band, masking the bug) — fixed by widening the height and
  shifting y down slightly; (2) the OCR call was skipping preprocessing entirely, unlike every real
  scraper call site — added the standard grayscale/contrast/invert pipeline, matching
  `InventoryScraper.ScanItemCount`.
- **Tab switching, live-verified working (2026-07-05):** `RunSwitchToTab(targetTab)` detects the
  current tab, then cycles LB/RB (the inventory sub-tab row's own input — distinct from the pause
  menu's stick-driven grid) to reach the target. Tabs wrap around (circular), so it compares
  forward vs. backward distance and takes whichever is fewer presses; also sped up from 300ms/tap to
  100ms since faster presses still register reliably. Wired up for Weapons, Artifacts, and Character
  Development Items (`RunSwitchToWeaponsTabTest`/`RunSwitchToArtifactsTabTest`/
  `RunSwitchToCharacterDevItemsTabTest`), all sharing the same generic implementation.
- **All controller-test logic extracted to `InventoryKamera/game/ControllerNavigationTests.cs`
  (2026-07-05).** Previously lived directly in `MainForm.cs`, which meant every coordinate/timing
  tweak risked tripping the WinForms Designer regeneration bug (see §3.0) whenever `MainForm` got
  touched in Visual Studio — happened twice more this session even after the extraction (each time
  fixed by restoring stripped `global::` qualifiers). `MainForm.cs`/`MainForm.Designer.cs` now only
  contain menu-item declarations and one-line passthrough handlers; zero navigation/OCR logic.
- **First real slice toward replacing the mouse-based scan: reading a selected item's name via
  controller, live-verified working (2026-07-05).** `RunReadSelectedWeaponNameTest` reuses
  `InventoryScraper.GetItemCard()`'s existing card-region percentages (the mouse-hover detail popup)
  as its first guess for controller mode's always-visible detail panel — confirmed by the user to be
  the right region, just needing the OCR preprocessing fixed (see below). Per explicit user direction,
  this is a **separate, independently-tuned region living in `ControllerNavigationTests.cs`**, not a
  modification to `InventoryScraper`'s mouse-mode percentages — keeps the PC/mouse path fully intact
  for if Genshin ever ships full keyboard-only navigation. **Root cause of the first failed
  attempt:** a generic grayscale+contrast(60)+invert pipeline produced total garbage
  (`"RS\nT fT% TF Tf =&& Ff Rt TT 8"`) despite the crop itself being clean and legible to the eye —
  contrast boosting doesn't suit this card's orange/brown gradient background. Fixed by matching
  `InventoryScraper.ScanItemName`'s already-proven pipeline for this exact visual pattern (white
  embossed text on a gradient nameplate) instead of inventing a new one: gamma correction
  (`GenshinProcesor.SetGamma(0.2, 0.2, 0.2, ...)`) then grayscale + invert, no contrast step.
- **Advance-to-next-item test added (2026-07-05), verification caveat found:** `RunAdvanceToNextWeaponTest`
  reads the selected weapon's name, sends one `MoveStep(Right)`, reads again, and reports both side
  by side. Per user: comparing matched names alone is **not a reliable pass/fail signal** — duplicate
  weapons commonly sit next to each other in the grid, so identical before/after names don't
  necessarily mean the stick nudge failed to advance. The test now just reports both results plus
  saved before/after screenshots for visual comparison instead of asserting a warning from name
  equality alone.
- **Full scan replacement will need more than just the name.** Per user: the real weapon scan must
  also read refinement, level, and equipped character for each item — matching what
  `WeaponScraper.ScanWeapons()`'s existing mouse-based path already captures (`ScanLevel`,
  `ScanRefinement`, `ScanEquippedCharacter`). `ReadSelectedWeaponName`'s card-region capture already
  grabs the *whole* detail card (not just the name strip), so these should be extractable as
  additional sub-region crops within the same already-proven card capture, likely needing their own
  gamma/grayscale/invert pipeline tuning per field the same way the name did — not yet built.
- **Region-calibration tool added (2026-07-03, not yet live-verified):** `ui/CoordinatePickerForm.cs`
  (Options → "Tool: Coordinate Picker for Captures") — loads a saved capture from `logging/`
  (open/reload/drag-drop, no file lock so test reruns can overwrite it while open), drag a rectangle,
  get the region as a copy-ready C# snippet in both the window-relative (`Navigation.GetWidth()`) and
  card-relative (`card.Width`) percentage formats. Replaces the guess → run test → send screenshot →
  adjust loop that every §6c sub-region crop (level/refinement/equipped) has needed so far.
  First real use (2026-07-03): the user measured the selected-item detail card off a full-window
  capture (x 0.7031, y 0.1231, w 0.2167, h 0.7556 — shared by the Weapons/Artifacts/Character
  Development Items tabs), replacing the initial guess borrowed from `InventoryScraper.GetItemCard()`'s
  mouse-popup percentages; now a single `CaptureSelectedItemCard()` helper used by both read tests.
  The within-card sub-crops (name strip 0–0.0574h, level, refinement, equipped) were then also all
  user-measured with the picker against the new card frame (2026-07-03) and applied, replacing the
  per-field guesses and their leftover `Navigation.IsNormal` variants.
- **Real replacement scan wired up and live-verified working (2026-07-03):** `WeaponScraper.ScanWeaponsViaController`
  (`InventoryKamera/scraping/WeaponScraper.cs`) is a full controller-driven scan loop -- enters
  Inventory, switches to Weapons, then repeatedly reads the selected item's card and advances one
  grid cell right, wrapping to the next row (`Down` + `Left×(cols-1)`, cols from
  `InventoryScraper.GetPageOfItems`'s existing blob detection, reused only for the column count).
  Shared navigation/capture primitives (`GetItemCardViaController`, `EnterInventoryViaController`,
  `DetectCurrentTabIndexViaController`, `SwitchToTabViaController`, plus name/equipped sub-crops)
  moved onto the `InventoryScraper` base class so Artifacts/Character Development Items scans can
  reuse them later. First live test (`RunControllerWeaponScanTest`, Options → Debug menu, 3 items)
  passed. **Not yet done:** locked-status detection (no measured region, always reports false --
  needs a coordinate-picker pass), sort-by-level/quality isn't ported, and it's still not wired
  into `InventoryKamera.GatherData`'s real scan (deliberately deferred pending a full-size live
  test, not just 3 items). Dead-end test methods and the throwaway `ControllerSpike` feasibility
  spike were removed once this real path proved out; all remaining `§6c` debug/test menu items
  moved from Options into a new **Debug** top-level menu, hidden outside Debug builds
  (`MainForm.cs`'s constructor, `#if !DEBUG`).
- **Not yet done:** verifying the left stick actually advances the grid selection to the next item
  with a verification method that isn't confused by duplicate adjacent weapons (e.g. comparing level/
  refinement/equipped-character too, or a raw pixel diff of the card capture, once those fields are
  read) — the last primitive needed before wiring a real replacement scan loop into
  `WeaponScraper`/`ArtifactScraper`. Per explicit user decision, this will be a **full replacement** of
  the mouse-based scan (no opt-in toggle) once proven, not a parallel path. Still unverified: whether
  the grid auto-scrolls into view as the selection moves past the visible area (user's understanding
  is yes, not yet directly tested), reading back "which item is currently selected" / detecting the
  end of the list, artifact and character-dev-item card layouts (may differ from the weapon card
  tested so far), and how the virtual controller's lifecycle should be managed across a full scan
  (currently each test connects fresh; a real scan should likely connect once at scan start).
- **New finding (2026-07-05): Materials/Character Development Items need a different approach than
  Weapons/Artifacts.** Both of those work entirely off the always-visible detail card
  (`GetItemCardViaController`), which never shows quantity — fine, since weapons/artifacts don't
  stack. Materials and Dev Items don't have that luxury: a stack's quantity is only shown as a small
  badge overlaid directly on the inventory grid slot, not anywhere in the detail card. Reading it via
  controller therefore needs each grid cell's own on-screen bounding box, not just the always-visible
  card — something the `ScanWeaponsViaController`/`ScanArtifactsViaController` approach has avoided
  needing entirely so far.
  Also confirmed by the user: controller mode's grid is **10 items per row** (already used as a fixed
  constant for the artifact per-row page cap, `ArtifactScraper.ScanArtifactsViaController`'s
  `artifactsPerRow`), and the viewport **auto-scrolls once the selection advances far enough down**,
  by a set amount that keeps the row you're currently on fully visible but *not* the next row —
  exact trigger depth and scroll amount not yet measured.
  **Potential faster approach for later (not yet built, needs live verification):** controller-mode
  navigation moves through the grid in known row-major order, unlike mouse mode, which has to
  *discover* an unknown grid layout fresh via `GetPageOfItems`' blob-detection retry loop on every
  page. That means the currently-selected cell's approximate on-screen rectangle could instead be
  *computed* directly from its known row/column index plus a single one-time coordinate-picker
  measurement of the grid's cell size/spacing, rather than re-detecting a bounding box per item. Open
  unknowns before this is buildable: the auto-scroll trigger/amount above, whether computed cell
  positions stay valid across a scroll event or need recalculating, and whether the quantity badge
  sits in a consistent enough position within a cell to crop reliably regardless of item icon
  artwork underneath it.
- **Update (2026-07-05): quantity is grid-slot-based after all, confirmed by live coordinate-picker
  measurements -- the initial "same card as Weapons/Artifacts" premise was wrong.** Three measured
  items (row 0/col 0, row 4/col 9, row 5/col 0-after-one-scroll) landed at three different on-screen
  positions, ruling out a single fixed card region. Per user: the grid shows **5 rows visible before
  scrolling** (rows 0-4 on a fixed layout); advancing past row 4 triggers an auto-snap-scroll, and
  every row from index 5 onward re-pins to the *same* fixed on-screen position ("row 6 and up are
  all on the same scrolled row near the bottom of the page") -- so only 6 distinct y-positions exist
  total (5 pre-scroll rows + 1 shared post-scroll position), not one per inventory row.
  **Per user: computed via fixed precomputed percentages rather than per-item blob detection**, for
  scan speed -- `InventoryScraper.GetPageOfItems` (mouse-mode's blob detector) was tried as a
  scroll-agnostic alternative first (self-corrects for any scroll state by re-detecting from pixels
  every call) and does work, but re-screenshots and re-analyzes the whole window per item, which is
  needless overhead once the fixed-position model above was confirmed. `MaterialScraper` now has:
  `GetQuantityRegionViaController(globalRow, column)` -- column spacing `(0.6325-0.0831)/9` and row
  spacing `(0.7340-0.2150)/4` derived from the 3 measured points (cross-checked: reapplying the
  column formula to row 4 reproduces its measured x exactly), `ScanQuantityBitmapViaController` --
  same digit-recognition pipeline as mouse-mode's `ScanMaterialCount`, just fed this bitmap instead
  of a mouse-detected grid-cell crop, `ScanMaterialsViaController` -- reads name via the shared
  always-visible card technique (`GetItemNameBitmapViaController`, unaffected by this finding, still
  correct), then quantity via the two methods above, tracking only `column`/`globalRow` itself (its
  own advance-loop state, not detected). Wired into `InventoryKamera.GatherData` inside the same
  shared `GameController` session as Weapons/Artifacts, replacing the old mouse-based
  `Scan_Materials`/`Navigation.SelectCharacterDevelopmentInventory`/`SelectMaterialInventory` calls
  entirely. Since this tab has no per-page count readout (`ScanItemCountViaController` only covers
  Weapons/Artifacts/Furnishings), the loop stops the same way mouse-mode did: once a scanned name
  repeats one already recorded in `inventory.Materials`.
  **STILL UNVERIFIED, none of this has been live-tested yet:** only 3 of the 6 distinct row
  y-positions were directly measured (rows 0 and 4, plus the shared post-scroll position) -- rows 1-3
  are interpolated, assumed evenly spaced; whether a single right-stick step reliably
  advances/auto-scrolls through this grid the same way it does for Weapons (carried over from
  `ScanWeaponsViaController`, never confirmed for this tab); and whether the name-repeat stop
  condition correctly detects end-of-list without an explicit scroll-to-bottom/backwards-pass step
  (mouse-mode's `Scan_Materials` has one via its `LastPage` block; this controller version does not).
- **Live-tested and working (2026-07-05), with one tuning round.** The interpolated row spacing
  (rows 1-3) held up fine. The one real miss: quantity position past row 4 doesn't sit at one fixed
  post-scroll spot the way first assumed -- per user, each additional scroll drifts it down by a
  small amount, and Materials vs Character Development Items drift at *different* rates (plausibly
  because Genshin's scroll-snap distance scales with the tab's total item count rather than being a
  fixed pixel amount). Widening the crop height to absorb this was tried first and reverted -- it
  broke digit OCR entirely, since `ScanQuantityBitmapViaController`'s top-whiteout band (tuned for
  the original tight crop) started eating into the actual digits once the crop got taller. Fixed
  instead by modeling the drift directly: `quantityDriftPerScrollRowMaterials` /
  `quantityDriftPerScrollRowCharDevItems` (separate per-tab constants, `MaterialScraper.cs`) added
  per scroll past row 4, tuned live down to `0.001` (0.1% of window height) each. Result: scans run
  end-to-end with only occasional "Failed to parse quantity" errors -- good enough to ship, revisit
  the constants only if a particular save shows a worse failure rate.
- **Character scanning, controller-driven, live-verified working (2026-07-05, commit `9a6a4d9`).**
  `CharacterScraper` replaced entirely with a controller pipeline batched by sub-tab
  (Attributes -> Constellations -> Talents) across the whole roster instead of per-character, to
  minimize sub-tab-switch animation cost. Wired into `GatherData` as a full replacement, matching
  the weapon/artifact/materials controller scans. Bugs found and fixed via live testing: gap-aware
  roster walking (manequin/duplicate slots no longer misalign Phase 2/3 navigation), gamma-based OCR
  preprocessing for the constellation "Activated" check, a dead-code contrast bug in level scanning,
  cancel-request propagation into Phase 2/3, a wrapped-name parsing bug, and a greedy C6-first
  constellation scan for 4-star characters. Also added a `Rarity` field to the character database and
  fixed a bug where a force database update could overwrite Traveler's manually-maintained
  per-element `ConstellationOrder`.
- **Follow-up hardening pass, live-testing-driven (2026-07-05, commit `33d5e3e`):** bounded the
  materials controller scan loop (was unbounded, could hang on consecutive unreadable names);
  `GatherData` now wraps `EnterInventoryViaController` in the same try/catch as its sibling phases;
  artifact controller-path debug screenshots gated behind `LogScreenshots` like every other scraper;
  weapon sort-by-level/quality early-stop now only trusted once `SetSortModeViaController` confirms
  the sort actually applied; `SwitchToTabViaController`'s unrecognized-target-tab case guarded the
  same way the unrecognized-current-tab case already was; `CharacterScraper.EnterCharacterMenuViaController`
  gained a `MashBack()` safety net before assuming a clean free-roam baseline. **All now-dead
  mouse-driven scan methods removed** (`WeaponScraper.ScanWeapons`, `ArtifactScraper`'s equivalent,
  and related mouse-only navigation) -- the controller path is the sole scan path for these five
  areas, per the original "full replacement, not opt-in" decision.
- **Remaining known gaps (not yet built, none blocking the above):** weapon locked-status detection
  (no measured region, always reports unlocked); artifact sort-MODE (Level/Quality/Type) selection;
  constellation/talent panels still require the confirmed mouse-input fallback (game-side
  constraint, not a code gap).

---

## 7. Cross-cutting / risks

- **Game-update fragility:** scanning depends on Genshin UI layout + lookup tables (`inventorylists`, Dimbreath sync). Modernization must not disturb the auto-updater path (`DatabaseManager`). Add tests around lookup parsing. **Update (2026-07-05):** the source repo `Dimbreath/AnimeGameData` was deprecated (hit GitLab's repo size limit) in favor of `Dimbreath/animegamedata2` — `DatabaseManager.cs`'s `commitsAPIURL`/`repoBaseURL` constants updated to the new project ID (`83871005`) and default branch (`main`, was `master`); the old repo is frozen, so "Update Lookup Tables" would have silently kept serving stale data indefinitely rather than erroring. Live-verified working against the new repo.
- **OCR parity:** any image-pipeline change (1.1) risks recognition regressions. Gated by **golden pixel-parity tests** on synthetic inputs (see §1.1) — proven identical (or interior-identical, for Kirsch's border) output to Accord for every reimplemented op, including `KirschEdgeDetector`/`BlobCounter`. Still no live-game verification (see below).
- **Third-party net8 support:** validated as part of the retarget (§0.4) — no code changes needed beyond the three known breaks. **Standing watch item:** `InputSimulator` restores via the net-framework compat-shim (NU1701); unmaintained, works so far.
- **No automated game testing:** screen-automation can't run in CI. Maintain a manual smoke-scan checklist per release (still outstanding for the net8 flip); push as much logic as possible behind unit tests.
- **4K/high-DPI scan performance (2026-07-05, revised same day):** first pass only downscaled the grid-detection copy inside `InventoryScraper.GetPageOfItems` (see git history); **revised to downscale at the capture source instead**, per explicit user direction ("overkill for OCR quality to be untouched, it can be downscaled to 1080p") -- `Navigation.CaptureWindow()`/`CaptureRegion()` now downscale their own output above a 1080px real window height (`Navigation.CaptureScale`, `Navigation.MaxCaptureHeight`), so both grid detection *and* every OCR crop taken elsewhere get proportionally fewer pixels at 4K, not just detection. Verified every consumer of captured bitmaps falls into one of two safe patterns: (1) a region computed from `Navigation.GetWidth()/GetHeight()` directly and passed to `CaptureRegion` (still correct -- capture *position* is still sourced from the real screen, only the returned bitmap's pixel density shrinks), or (2) an in-memory crop proportional to an already-captured bitmap's own (possibly-downscaled) dimensions. The one exception -- blob-detected grid rectangles from `GetPageOfItems`, whose pixel coordinates get fed straight into `Navigation.SetCursor`/`CaptureRegion` by every scraper -- are rescaled by `1/Navigation.CaptureScale` back to real coordinates once, right before `GetPageOfItems` returns them, so every caller (clicking, per-item OCR-region capture) stays oblivious to downscaling ever happening. Also fixed the `PreflightChecksPass`/scan-thread fullscreen-mismatch check (`MainForm.cs`), which previously asserted `CaptureWindow().Size == GetSize()` and would have false-failed for every window above 1080p once capture started downscaling -- now compares against the expected *scaled* size. **Not covered by the existing golden pixel-parity tests** (those test per-pixel-filter correctness, not this geometric scale/rescale logic, nor OCR accuracy at reduced input resolution) -- needs live verification specifically on a 4K (or other >1080p) setup: clicks landing on the right items, and recognition accuracy holding up with smaller OCR crops. **Real regression found and fixed same day:** the user reported scans got *slower*, not faster. Root cause: `DownscaleCapture` applied the whole-window `CaptureScale` ratio to every single capture uniformly, including the hundreds of small per-item OCR crops (nameplate, quantity, stat lines -- typically well under 200px tall even at 4K) a scan makes. Downscaling those saves negligible per-pixel-filter/OCR time but each one still pays a full bitmap-allocation + high-quality-bicubic-resize cost, and that overhead across hundreds of small calls outweighed the one real win (the large, infrequent whole-window grid-detection capture). Fixed by deciding per-call based on the *captured bitmap's own height* rather than the window-wide ratio -- small crops now correctly no-op through `DownscaleCapture`, while `CaptureWindow()`'s output (always exactly `GetHeight()` tall) still downscales exactly as before. `Navigation.CaptureScale` (used by `GetPageOfItems`'s rectangle rescaling and the fullscreen-mismatch check) is unaffected since it's specifically about `CaptureWindow()`'s behavior, which didn't change.

**Second regression found and fixed same day:** still slower after the above fix -- specifically "the action between clicking artifacts." Root cause: `InventoryScraper.GetItemCard()` (called once *per item clicked* via `QueueScan`, not once per page) called `Navigation.CaptureWindow()` to grab the *entire* window just to crop out a ~26%x78% sub-region -- meaning every single artifact/weapon click paid a full native-resolution `CopyFromScreen` (unavoidable OS-level cost, always was there) *plus*, after this change, a new bicubic downscale on top of the *entire* captured window, every single item. Attempted fix: rewrote `GetItemCard()` to capture only the card region directly via `Navigation.CaptureRegion(...)` instead of capturing the whole window and cropping in memory. Also switched `DownscaleCapture`'s resize from `HighQualityBicubic` to `Bilinear` (kept -- see below).

**Third regression found same day, this one a correctness bug, not perf:** the `GetItemCard()` rewrite above **broke capture on at least one non-4K windowed setup** -- reported as "screenshotting the whole screen rather than just the window." Root cause: the original `CaptureWindow()` + `GenshinProcesor.CopyBitmap(window, cardRectangle)` path got a safety net for free -- `CopyBitmap`'s `ClipToSource` helper clamps the crop rectangle to the already-captured bitmap's bounds before cropping. `Navigation.CaptureRegion(cardRectangle)` has no equivalent clamping; `CopyFromScreen` will happily copy past the window edge into whatever's on the real desktop there if the percentage-based rectangle math overshoots by even a little. **Reverted `GetItemCard()` back to the original `CaptureWindow()` + `CopyBitmap` pattern** rather than risk a fourth regression by patching the clamp in blind -- correctness over the remaining performance win here. The `GetItemCard` per-item capture-region optimization is now explicitly **not done**; worth revisiting later with an explicit bounds clamp on `cardRectangle` before any direct `CaptureRegion` call, tested carefully against a real non-4K windowed setup before landing again. `DownscaleCapture`'s per-call-size-based decision and the `Bilinear` interpolation swap are unaffected by this revert and remain in place.

**Root cause actually found (not a regression from this session's work -- pre-existing):** even after the `GetItemCard()` revert, the user confirmed the same "capturing outside the window" symptom on a 1440p *windowed* game window on a 4K monitor (fullscreen still worked, just slow). This ruled out `GetItemCard()`/`CaptureRegion` entirely -- fullscreen and windowed go through the exact same capture code. Actual cause: **the app was DPI-unaware.** `app.manifest` had its `dpiAware` declaration commented out, and `Program.cs` called the legacy `SetProcessDPIAware()` Win32 API at runtime -- which is silently a no-op, since DPI awareness can only be set once per process and an absent manifest declaration locks the process in as unaware *before* `Main()` ever runs. On a DPI-unaware app running on a scaled display (a 4K monitor is essentially always scaled 150%+ in Windows), `GetClientRect`/`ClientToScreen` report virtualized (logical) coordinates while `Graphics.CopyFromScreen` operates in real physical pixels -- a systematic mismatch. Exclusive fullscreen sidesteps this entirely (native-resolution rendering, position always (0,0)), which is exactly why only windowed mode broke. **Attempted fix, reverted:** `app.manifest` was changed to declare Per-Monitor-V2 DPI awareness (`dpiAwareness=PerMonitorV2`, `dpiAware=true/PM` fallback) and the dead `SetProcessDPIAware()` call was removed from `Program.cs`. This caused a hard launch-blocking failure on the user's machine -- **"Unable to start program because the application configuration is incorrect"**, a Windows SxS/manifest-load error, worse than the bug it was meant to fix. **Reverted both files completely** back to their pre-DPI-edit state (manifest's `dpiAware` block re-commented with a note explaining what was tried and why it was reverted; `Program.cs`'s `SetProcessDPIAware()` call and P/Invoke declaration restored) rather than debug manifest XML blind without the ability to run the app locally.

**Actual root cause of the launch failure, found and fixed:** the user reported the exact Windows error via Event Viewer -- `Activation context generation failed ... Error in manifest or policy file ... on line 44. Invalid Xml syntax.` Extracted the *embedded* `RT_MANIFEST` resource directly from the compiled exe (via `LoadLibraryEx`/`FindResource`/`LoadResource` P/Invoke from PowerShell, since the on-disk `app.manifest` source and the compiled resource can differ) and found the actual defect: the revert's own explanatory comment used a literal double-hyphen (`--`) as a prose dash (e.g. "displays -- caused"). **The XML spec forbids the sequence `--` anywhere inside a comment body, only permitted as the closing `-->`.** Lenient parsers (`dotnet build`'s manifest embedding, .NET's `XmlDocument` when loaded from a `string` that's already had its BOM decoded to a literal character) silently tolerated it -- which is why the build always succeeded and gave no warning -- but Windows' native SxS/Fusion manifest parser used specifically at process *activation* does not, hence the error appearing only at launch, never at build time. Rewrote the offending comment without any `--` sequences; verified via a stream-based (BOM-correct) `XmlDocument.Load()` against the freshly-rebuilt exe's actual embedded resource that it now parses as valid XML. **Live-verified (2026-07-05): the app launches normally again.**

**Second DPI-awareness attempt, also didn't fix the real bug, but for an instructive reason:** re-applied Per-Monitor-V2 DPI awareness in `app.manifest` (carefully, no `--`), rebuilt, self-validated the embedded manifest -- launched fine, but windowed-mode-on-4K capture was still broken. Root cause: **manifest-declared DPI awareness has no effect in .NET (Core) 5+ WinForms apps** -- confirmed by the SDK's own `WFAC010` build warning ("Remove high DPI settings from app.manifest and configure via Application.SetHighDpiMode API or 'ApplicationHighDpiMode' project property"), which had been silently ignored/glossed over during the manifest-syntax firefighting. Removed the manifest DPI block entirely; DPI mode is now set via `Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)` as the first line of `Program.cs`'s `Main()` (the SDK-generated-template-equivalent, guaranteed-correct API for a hand-written `Program.cs`), plus `<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>` in the csproj for good measure. This got `GetWindowRect`/`GetClientRect`/`ClientToScreen` reporting real physical-pixel coordinates (confirmed via added debug logging) -- **but the capture region was still wrong**, which is what led to the real fix below.

**The actual, final root cause (2026-07-05): entirely unrelated to DPI.** Added debug logging to `Navigation.Initialize()`/`CaptureWindow()` and had the user run a live windowed-4K repro; the log showed `GetWindowRect` correctly reporting the real window origin `(629,332)`, but the position actually used for capture was `(1280,754)` -- almost exactly double. Cause: `Navigation.WindowPosition` is a `static RECT` field, and `ClientToScreen(handle, ref WindowPosition)` treats its *current* value as the client-space point to convert to screen space. `Navigation.Initialize()` is called twice per scan with no `Reset()` in between (once by `MainForm.PreflightChecksPass()`, again immediately after by the scan thread -- the §3.2 comment introducing this even claimed the redundant call was "harmless," which was the wrong assumption). On the second call, `WindowPosition` already held the *first* call's screen coordinates, so `ClientToScreen` converted an already-screen coordinate a second time, compounding the window's own offset. Invisible in fullscreen (origin is always ~(0,0); doubling zero is still zero) but broke windowed mode outright, and had nothing to do with DPI scaling, monitor scaling, or any of the manifest work above -- both DPI-awareness attempts were solving a bug that didn't exist. **Fixed** by resetting `WindowPosition = new RECT()` immediately before the `ClientToScreen` call in `Navigation.Initialize()`, making it idempotent regardless of call count. **Live-verified (2026-07-05) on windowed 1440p on a 4K display: fixed.** The DPI-awareness work (`Application.SetHighDpiMode`, `ApplicationHighDpiMode`) was kept regardless since it's still correct/beneficial for a WinForms app already using `AutoScaleMode.Dpi`, just wasn't the fix for this bug.

---

## 8. Suggested sequencing & branches

| Phase | Branch | Depends on | State |
|---|---|---|---|
| 0 | `modernize/phase0-foundation` | — | ✅ complete |
| 1 | `modernize/phase0-foundation` (same branch so far) | 0 | ✅ complete |
| 2 | `modernize/phase0-foundation` (same branch so far) | 1 | 🔄 mostly done (§2.1 lookup dicts + §2.4 typed models outstanding) |
| 3 | `modernize/phase0-foundation` (same branch so far) | 2 (net8 ✅ available now) | 🔄 in progress (§3.0 substantially done; §3.1–§3.5 not started) |

Phase 0, 1, 2, and the §3.0 UX work currently all share `modernize/phase0-foundation` (not yet merged
to `master`); they can be split into dedicated branches/PRs before merge if preferred. Each phase
merges to `master` only when it builds in CI and passes manual smoke-testing — the user has been
live-testing continuously throughout, so this is satisfied on an ongoing basis rather than as a single
gate. Phase 1 landed as ~13 small internal commits (SDK conversion → seam → per-pixel swap → stats
swap → Kirsch/Blob swap → Thread.Abort replacement → net8 retarget → manequin hack → Channels/async
pipeline → concurrency benchmark).

---

## 9. Immediate next step

**Phase 1 is complete** (121 tests, `net8.0-windows`, Channels-based OCR pipeline). **Phase 2 is
mostly done** except the deliberately-deferred character-display MVVM slice and typed data models.
**§3.0 (MainForm declutter + visual theme) is substantially done** as of 2026-07-05 — see §3.0 for
the full slice history. **§6b (Windows.Graphics.Capture) was tried, tested against real usage, and
reverted** — see §6b for the two real-world failure modes found (HDR still washes out; the user's
actual overlay software uses in-process hooking, which WGC can't exclude). HDR and overlays remain
unresolved problems. Candidate next steps, not yet sequenced:

1. **Manual smoke-scanning is no longer an open gap** — per the user (2026-07-05), they've been
   running the live app continuously throughout Phase 0–2 and the §3.0 UI/theme work without issues,
   including the new `animegamedata2` data source via "Update Lookup Tables". This branch can be
   considered live-verified going into whatever's next, rather than needing a dedicated gate.
2. **Continue Phase 3** (§3.1–§3.5: live scan feedback, pre-flight validation, inline OCR correction,
   dark mode/DPI polish, onboarding) — none started yet.
3. **Scan-input revamp** (§6c) — ✅ done: all five scan types (Weapons, Artifacts, Materials,
   Character Development Items, Character) are controller-driven, wired into `GatherData`, and
   mouse-mode code is removed. Remaining gaps are small and non-blocking: weapon locked-status
   detection, artifact sort-mode selection, and the confirmed constellation/talent mouse fallback.
4. **Revisit HDR/overlays with a narrower approach** — per §6b's "if this gets revisited" notes:
   confirm the SDR-brightness-slider theory for HDR before attempting a code fix, and consider a
   detect-and-warn approach for overlays instead of trying to exclude them.
5. **Finish Phase 2's loose ends** (§2.1's `ILookupService` dictionaries still static on
   `GenshinProcesor`; §2.4 typed data models) — lower user-visible urgency, architectural cleanup.
