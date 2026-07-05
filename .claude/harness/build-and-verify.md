# Build & Verify Contract

Single source of truth for when building is allowed, the canonical commands, what "verified"
means, and the mandatory handoff format. Grounded in recorded user feedback (memory
`feedback-user-builds-not-me`) and CI (`.github/workflows/build.yml`).

## 1. The build contract — when you may build

Default: **you do not build.** The user iterates by running the live app; per-edit builds add
friction (and fail on file locks while the exe runs). `dotnet build` / `dotnet test` are
allowed ONLY when at least one holds:

- **(a)** The user asked for a build/test this session, explicitly.
- **(b)** Handoff gate: you are about to commit, or about to report "ready" after a stretch of
  >5 changed files or >200 changed lines since the last known-green state.
- **(c)** You are the T5 verifier and the boundary you're verifying satisfies (a) or (b).

If none of (a)/(b)/(c) applies, the answer is simply **no build**: small changes hand off on
E1 evidence from §4's static checks alone. Never build "just to check".
If a build fails with a file-lock signature (§7), the app is probably running: do NOT retry;
hand off with "build blocked by running exe" and let the user decide.

## 2. Canonical commands — mirrors CI, do not invent variants

Run from repo root (`D:\Github\Inventory_Kamera`):

```
dotnet restore InventoryKamera.sln
dotnet build InventoryKamera.sln -c Release --no-restore
dotnet test tests/InventoryKamera.Tests/InventoryKamera.Tests.csproj -c Release --no-build
```

- Local quick gate: `-c Debug` is fine (faster); CI truth is Release.
- Full pipeline check without touching the user's machine state: push (user-authorized only)
  and let `build.yml` run — CI is the safety net, never claim CI-green before it reports.
- Publish (release flow, user-run): see `.github/workflows/release.yml` — tag-driven.

## 3. Verification tiers in practice (definitions in judgment.md §1)

| Tier | How to earn it here |
|---|---|
| E1 | Read back changed hunks; grep symbol counts; `git diff --stat` matches intended scope |
| E2 | §2 build command exit 0 at an allowed gate |
| E3 | §2 test command green; state N passed / M failed and name new/changed tests |
| E4 | USER ONLY: live scan behavior, OCR accuracy, visuals, timing. You request, never assert. |

## 4. Per-edit static checks (no build needed) — run after every edit batch

1. `git diff --stat` — file list matches the intended scope (catches accidental edits).
2. Grep every symbol you added/renamed — state expected vs. found counts.
3. New `using`/namespace references resolve: the namespace exists in-repo or in the package
   list (`InventoryKamera/InventoryKamera.csproj` `<PackageReference>` block). No new packages
   without user approval.
4. Event wiring: every handler you attached (`+=`) has a matching method definition (grep it).
5. Created a file? → `git add` it NOW, then confirm via `git status --porcelain` (shows `A`,
   not `??`).
6. Deleted/renamed a file? → grep the old filename repo-wide (csproj pins, resx references,
   docs).

## 5. WinForms / Designer hazards specific to this repo

1. **Namespace/class collision:** the namespace `InventoryKamera` contains class
   `InventoryKamera`. Designer files therefore need `global::` qualifiers. NEVER remove
   `global::` from Designer files; after any Designer edit, grep `global::InventoryKamera` to
   confirm qualifiers survived.
2. **The §3.0 regeneration bug** (MODERNIZATION_PLAN.md §3.0, recurred 3+ times): opening
   MainForm in the VS Designer can rewrite `MainForm.Designer.cs` to (a) strip `global::`
   (build break) and (b) rebind settings-bound controls to a throwaway `new Settings()`
   instance (**silently resets all user settings at launch**). Check after ANY Designer-file
   change: `grep -n "new Settings()" InventoryKamera/ui/` must return 0 hits; bindings must
   read `global::InventoryKamera.Properties.Settings.Default`.
3. **MainForm.resx LogicalName is pinned** in `InventoryKamera.csproj` (the type's namespace
   doesn't match its folder). Don't move/rename MainForm without updating that pin.
4. **resx files:** hand-edit only `<data>` string entries. Never touch base64/binary blobs.
   Images/icons go through Properties/Resources, not raw resx surgery.
5. **New UI logic goes OUTSIDE Designer files** — pattern: `game/ControllerNavigationTests.cs`
   (plain static class; MainForm Click handlers are one-liners calling into it). This exists
   specifically to minimize Designer-file churn.
6. **DPI:** app is PerMonitorV2 (`csproj` `ApplicationHighDpiMode`). No absolute-pixel
   positioning tricks; use the existing layout containers.

## 6. Handoff report format — mandatory at end of any work session/task

Applies to EVERY task that changed files, regardless of size — a one-line fix still gets a
report (short lines are fine; missing lines are not).
Copy this block, fill every line, delete nothing (write "none" where empty):

```
## Handoff — {YYYY-MM-DD} — branch {name}
Task: {one sentence}
Changed: {path — what and why, one line per file}
New files: {path — staged yes/no}
Evidence: {claim → tier → proof, one line per claim}
Build gate: {not run (which §1 condition made it unnecessary) | Debug/Release exit 0 | FAILED: first error verbatim}
Tests: {not run (why) | N passed / M failed — failure names}
User should: {exact run steps + what to observe — falsifiable, e.g. "scan one 4x8 artifact page; expect all 32 detected"}
Not verified: {explicit list — especially anything live-behavior}
Flags: {TASTE-DECISION NEEDED | LIVE-VERIFY NEEDED | BLOCKED | none}
```

The "User should" line is the anti-closed-loop hinge: it converts your unverifiable claims
into the user's 30-second falsifiable check. Write it so a failure would be unambiguous.

## 7. Known failure signatures and what they actually mean

| Signature | Meaning | Action |
|---|---|---|
| MSB3026/MSB3027/MSB3021 "file is being used by another process" | The app (or test host) is running — the user is live-iterating | Don't retry. Hand off: "build blocked by running exe." |
| CS0246 type not found, right after you added a file | File not saved where you think, or namespace typo. NOT a csproj problem (SDK glob auto-includes) | Check path + namespace; do not edit the csproj. |
| Hundreds of CA1416 "only supported on Windows" warnings | TFM drifted off `net8.0-windows7.0` | Restore the TFM (plan: it was chosen precisely to silence these). |
| User reports "all my settings reset" | Designer rebind bug — §5.2 | grep `new Settings()` in ui/; restore `global::…Settings.Default` bindings. |
| xunit "No test matches the given testcase filter" | Wrong project path | Use the §2 test command verbatim. |
| `git commit` blocked mentioning untracked files | `commit-guard` hook doing its job | `git add` the files or follow the bypass instruction in the hook message. |
| PowerShell "The token '&&' is not a valid statement separator" | PS 5.1 (or ps51-guard blocked it first) | Use `;`, `if ($?)`, or the Bash tool. |
