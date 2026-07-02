# Inventory Kamera — Modernization Plan

> Status: **In progress** · Scope: full phased modernization (foundation → efficiency → UX)
> Drafted 2026-06-28 · Last updated 2026-07-01 · Target: incremental, `master` stays releasable throughout
> Working branch: `modernize/phase0-foundation` (holds Phase 0 + Phase 1, both complete; not yet merged)

---

## 0. Status at a glance

| Phase | State | Notes |
|---|---|---|
| **0 — Foundation** | ✅ **complete** | SDK-style project, xUnit tests, CI. |
| **1 — Efficiency** | ✅ **complete** | Accord removed, net8.0-windows retarget, Channels/async pipeline, right-sized parallelism, manequin hack killed, concurrency benchmark. §1.4 (System.Text.Json) deliberately deferred to Phase 2 — see §1.4. |
| **2 — Architecture** | 🔄 **in progress** | §2.1 done: `IOcrService`, `LookupService`, `IImagePreprocessor`/`ImageProcessor`, and `TextNormalizer` all extracted from `GenshinProcesor` with real unit tests. §2.2 done: both stateful services fully constructor-injected into all 5 scrapers; `GenshinProcesor` static forwarding wrappers deleted; no DI container yet (hand-wired composition root). §2.3 done for scan logic: `IScanSettings` seam added, still backed by `Properties.Settings.Default` on purpose (see §2.3 for why). §2.4 investigated and deliberately deferred (remote/variable-shape data + a mutable field make it a real design problem, not a mechanical one). §2.5 first slice done: `IScanProgressReporter` seam added over the static `UserInterface`; the actual MVVM redesign (observable view model, `MainForm` as a thin view) is unstarted and substantially bigger. |
| **3 — UX** | ⬜ not started | §6b (Windows.Graphics.Capture) was implemented and tested against real usage, then **reverted** — see §6b for why. HDR/overlay support issues remain unresolved. |

**Runtime:** the app now targets **`net8.0-windows7.0`** (was net472 through Phase 0; bumped from bare `net8.0-windows` after live testing surfaced 670+ spurious CA1416 warnings — see below). Single-file self-contained publish verified working. OCR worker pipeline runs on `System.Threading.Channels` + `Task`s instead of a hand-rolled locking queue + polling `Thread`s.

**Test/CI status:** 106 tests green (net8.0), including real Tesseract OCR round-trip tests — previously impossible, since touching `GenshinProcesor` at all used to eagerly load the whole engine pool from disk — `LookupService`/`TextNormalizer` tests using fake dictionaries, `ImageProcessor` delegation tests, and `ScanSettings` live-forwarding tests. GitHub Actions build+test on push/PR and a tag-driven release workflow (publishing single-file self-contained) are live.

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

### 2.5 Decouple UI from logic (MVVM-lite) 🔄 first slice done
- **`IScanProgressReporter` seam** ✅ **done** (this scope) — added `IScanProgressReporter`/
  `UserInterfaceReporter`, same instance-method-seam shape as `IImagePreprocessor`/`ImageProcessor`
  and `IScanSettings`/`ScanSettings`. `UserInterfaceReporter` delegates straight to the existing
  static `UserInterface` (unchanged — still does direct WinForms control manipulation with
  `Control.Invoke` thread marshaling, wired up once via `UserInterface.Init` from `MainForm`).
  Constructor-injected into all 5 scrapers and `InventoryKamera` alongside `ocrService`/
  `imagePreprocessor`/`scanSettings`; replaced the ~48 `UserInterface.*` calls in scraper files and
  `InventoryKamera.cs` with `progressReporter.*`. Left `GOOD.cs` (1 call, export logic) and
  `MainForm.cs` (12 calls — `UserInterface.Init` itself plus a few direct status/reset calls issued
  from UI event handlers, not scan logic) on the static class — same "not scan logic" scoping used in
  §2.3.
  - **No test added:** unlike `ImageProcessor`/`ScanSettings`, exercising `UserInterfaceReporter`
    means calling through to real `Control.Invoke`, which requires a live window handle and pumped
    message loop — calling it from a headless xUnit test risks a hang rather than a fast, useful
    assertion. Confirmed correct by compilation + the existing OCR/ImagePreprocessor/ScanSettings test
    coverage exercising the same scraper code paths that now call through this seam.
  - **This is only the first, low-risk slice of §2.5** — it decouples scan logic call sites from the
    concrete static type, same pattern as every other Phase 2 service, but does **not** touch the
    actual mechanism: progress still reaches the UI via direct `Control.Invoke` calls into WinForms
    controls, not an observable `ScanViewModel`. The real MVVM redesign — a view model exposing
    observable progress/state, `MainForm` becoming a thin view bound to it instead of controls being
    written into directly — is unstarted, substantially bigger, and UI-wide (touches `MainForm.cs`'s
    ~676 lines and its Designer-generated control wiring) in a way this slice deliberately avoided.

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
3. **Visual reskin (Phase 3).** Only after step 1 lands: dark mode, layout/typography pass, live
   per-category progress with counts/ETA (already sketched in §3.1 below), inline correction UI.
   Doing this before the view model exists means redoing the visual work once the control-binding
   story changes underneath it.

**Exit criteria:** no `static` mutable engine/lookup state; services unit-tested in isolation; UI receives progress through an abstraction; behavior parity maintained. **Not yet met** — both genuinely stateful/mutable services (`IOcrService`'s engine pool, `IImagePreprocessor`) are now off statics and constructor-injected (✅) across all 5 scrapers, scan logic's config reads go through `IScanSettings` instead of `Properties.Settings.Default` directly (✅), and scan logic's progress-reporting calls go through `IScanProgressReporter` instead of the static `UserInterface` directly (✅). `LookupService`/`TextNormalizer` are intentionally stateless static classes (no mutable state to remove — they take the lookup data as parameters each call), but the lookup *dictionaries themselves* still live as mutable static fields on `GenshinProcesor`; moving those into an owned, non-static data store is unstarted follow-up work (likely folds into §2.4's typed models). `Properties.Settings.Default` and the static `UserInterface` are both still the underlying mechanisms behind their respective seams (by design — see §2.3/§2.5). The actual "UI receives progress through an abstraction" criterion — an observable view model instead of direct control manipulation — hasn't started.

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
| 1 | `modernize/phase0-foundation` (same branch so far) | 0 | ✅ complete |
| 2 | `modernize/phase2-architecture` | 1 | ⬜ not started (`ImageProcessing` seam started early) |
| 3 | `modernize/phase3-ux` (incl. §6b capture) | 2 (net8 ✅ available now) | ⬜ not started |

Phase 0 and Phase 1 currently share `modernize/phase0-foundation` (not yet merged to `master`); they
can be split into a dedicated Phase 1 branch/PR before merge if preferred. Each phase merges to
`master` only when it builds in CI and passes a manual smoke scan — **that smoke scan is still
outstanding** for everything that's landed on this branch. Phase 1 landed as ~13 small internal
commits (SDK conversion → seam → per-pixel swap → stats swap → Kirsch/Blob swap → Thread.Abort
replacement → net8 retarget → manequin hack → Channels/async pipeline → concurrency benchmark).

---

## 9. Immediate next step

**Phase 1 is complete** (79 tests, `net8.0-windows`, Channels-based OCR pipeline). **§6b
(Windows.Graphics.Capture) was tried, tested against real usage, and reverted** — see §6b for the
two real-world failure modes found (HDR still washes out; the user's actual overlay software uses
in-process hooking, which WGC can't exclude). HDR and overlays remain unresolved problems. Candidate
next steps, not yet sequenced:

1. **A manual smoke scan** against the live game — the one standing verification gap across all of
   Phase 0+1 — before merging this branch to `master`. The safest checkpoint given how much has
   landed without ever touching a real game session.
2. **Start Phase 2** (§2.1–2.5: decompose `GenshinProcesor`, DI, typed config, typed data models,
   MVVM-lite) — architectural cleanup, lower user-visible urgency.
3. **Revisit HDR/overlays with a narrower approach** — per §6b's "if this gets revisited" notes:
   confirm the SDR-brightness-slider theory for HDR before attempting a code fix, and consider a
   detect-and-warn approach for overlays instead of trying to exclude them.
