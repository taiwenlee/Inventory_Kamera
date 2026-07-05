# Judgment Externalization Matrix

Checklists that replace "senior-model intuition" with comparable criteria. Written for
Sonnet/Haiku-class readers: every rule is binary or countable, and carries one positive and one
negative example from THIS repo. When a rule here conflicts with your instinct, the rule wins;
if the rule is genuinely wrong, propose a change per `reflection.md` §1 — don't silently ignore it.

## 1. Evidence tiers — how much a claim is worth

| Tier | Meaning | You may write |
|---|---|---|
| E0 | Reasoning only, nothing observed | Nothing. E0 claims are banned in handoffs. Phrases that signal E0: "should work", "this fixes", unqualified "done". |
| E1 | Static evidence: files re-read, grep counts, diff reviewed | "grep `SelectWeaponInventory` → 4 call sites, all updated (E1)" |
| E2 | Compiles: `dotnet build` exit 0 at an allowed gate | "builds clean, Release (E2)" |
| E3 | Tests: `dotnet test` green, count stated | "121 passed / 0 failed, includes 2 new TextNormalizer cases (E3)" |
| E4 | Live behavior observed by the USER | You may never assert E4. You may only request it: "LIVE-VERIFY NEEDED: scan one artifact page, expect no missing rows." |

Rules: every claim in a handoff is tagged with its tier. UI/visual changes cap at E2 plus a
`TASTE-DECISION NEEDED` or `LIVE-VERIFY NEEDED` flag — plan §0's "Standing gap" records two real
bugs that were build-green AND test-green and only failed live. That is why E3 ≠ done here.

## 2. Abandon-the-direction signals (A-rules)

**A1 — Documented dead end.** Before implementing an approach, grep `MODERNIZATION_PLAN.md`
headings and the memory index for it. If it was tried-and-reverted or ruled out, you need NEW
facts (a changed dependency, OS, or game behavior — stated in writing) to proceed.
- ✅ *Positive:* "User wants better HDR handling → §6b says WGC was built, live-tested, reverted;
  its 'if revisited' notes say confirm the SDR-slider theory first → I propose the
  detect-and-warn path and ask for that confirmation."
- ❌ *Negative:* "HDR capture is broken → I'll implement Windows.Graphics.Capture!" — burns a
  full session reproducing a documented failure whose causes (real HDR hardware, in-process
  hooking overlays) cannot be seen from code.

**A2 — Growing diff, frozen understanding.** Before retry N+1, write one sentence: "Attempt N
failed because X; N+1 changes only Y." If you can't write it, or the diff keeps growing while
the error stays the same, stop — you are guessing.
- ✅ "Attempt 1 failed because the threshold 0.62 rejects the new font's confidence ~0.58;
  attempt 2 changes only that constant to 0.55."
- ❌ Attempt 3 rewrites `ImageProcessor`, adds a helper class, touches 6 files — original test
  still red.

**A3 — Fix requires the wrong layer.** Editing generated/derived/foreign surfaces to silence a
symptom: `Settings.Designer.cs`, `Resources.Designer.cs`, anything under NuGet packages,
`tessdata/*`, or `*.Designer.cs` edits beyond the checklist in `build-and-verify.md` §5.
- ✅ Setting needs a new default → change the settings schema/`Settings.settings` source, let
  generated files regenerate.
- ❌ Hand-edit `Settings.Designer.cs` so the compiler stops complaining.

**A4 — Scope creep past the criteria.** Your acceptance criteria named ≤N files; you are now
editing file N+3. Stop, report `PARTIAL` with what you learned. A discovered dependency is a
finding, not a license.
- ✅ "Criterion said touch `ArtifactScraper.cs` only; the fix also needs `InventoryScraper.cs`
  (base class) — reporting back before proceeding."
- ❌ "While I was in there I also modernized the material scraper."

**A5 — Two strikes, then escalate.** Two failed attempts on the same subtask → escalate with
template T6. Counting lives in `dispatch.md` §4: criterion FAILs, verifier REJECTs, and
tool-error loops all feed ONE counter. "Distinct hypotheses" is not a separate threshold —
A2 already demands every retry carry a NEW one-sentence failure cause, so repeating an
identical fix is both illegitimate (A2) and strike two (here). A silent third attempt is a
protocol violation regardless of how promising it looks.

## 3. Definition of Done (D-rules) — run ALL at every handoff; each is binary

- **D1 Untracked-file check.** `git status --porcelain` reviewed; every new source file is
  staged, or listed in the handoff as intentionally untracked with a reason.
  ✅ "`?? game/GameController.cs` — staged." ❌ The f34887c incident: commit shipped, file
  didn't, build broke for anyone at that commit.
- **D2 Symbol consistency.** For every rename/signature change: grep old symbol = 0 hits
  (excluding plan/history docs); grep new symbol = every expected call site. State the counts.
  ✅ "`AnalyzeText(` old 2-arg form: 0 hits; new overload: 7 call sites (E1)."
  ❌ "Renamed everywhere I saw it."
- **D3 Designer-touch check** (only when a `*.Designer.cs` is in the diff): grep
  `new Settings()` under `InventoryKamera/ui/` = 0 hits; all settings bindings still read
  `global::InventoryKamera.Properties.Settings.Default`; every control referenced in
  code-behind still declared in `InitializeComponent`. (The §3.0 regeneration bug silently
  wipes user settings at launch — build-green, test-green, data loss live.)
- **D4 Build gate** if this boundary requires one (`build-and-verify.md` §1): exit code stated.
  A red build = `Status: BLOCKED`, never "done with caveats".
- **D5 Test accounting.** Scan logic/model changes → the relevant test file updated, or one
  written line in the handoff saying why not. "Tests pass" requires E3 evidence (counts).
  ✅ "Added `TextNormalizerTests.NumericOcrConfusions`, 123 passed (E3)."
  ❌ "Tests should still pass" (that's E0).
- **D6 No stray artifacts.** No new TODO/HACK/`NotImplementedException`/commented-out blocks
  unless each is listed in the handoff.
- **D7 Handoff report** written in the exact format of `build-and-verify.md` §6, every claim
  tiered.

## 4. Circuit breakers — stop autonomous work and ask the user (CB-rules)

- **CB1 Irreversible or outward.** Deleting/overwriting >20 lines of uncommitted work you
  didn't write this session; `git reset`/`checkout --`/stash on a dirty tree; force-push; any
  push/PR/release/publish; anything leaving the machine.
  ✅ "My change collides with uncommitted `GameController.cs` WIP → stop, ask."
  ❌ "I stashed the user's changes to get a clean tree."
- **CB2 Requirement fork.** Two readings of the request lead to different file sets, plan and
  memory don't disambiguate, and a wrong guess costs a user build-test cycle. If the cheaper
  reading is plan-consistent and easily reversible: take it and STATE the assumption at the top
  of the handoff. Otherwise ask ONE question with concrete options.
- **CB3 Plan-invalidating discovery.** Evidence that a recorded decision's premise broke (e.g.
  ViGEmBus virtual controller stops connecting on a new Windows build). Stop feature work,
  report the evidence, wait. Don't design around a broken premise.
- **CB4 Taste boundary hit** (§5 below) with no documented spec.
- **CB5 Ladder exhausted:** the `dispatch.md` §4 hard budget — max 2 attempts per tier, max
  2 tier-hops per issue — is used up. Stop all attempts; hand the T6 package to the user.
- **CB6 Money, credentials, accounts, releases.** Always. No exceptions.

## 5. The taste boundary — decisions weak models must not make alone

Taste domains in this repo: `UiTheme.cs` palette/spacing, control layout and grouping, label
copy/wording, dark-mode colors, onboarding flow (§3.5), any "make it look modern" judgment.
The established loop (all of §3.0 landed this way): **implement → user runs and screenshots →
adjust.** Hard rules:

1. Never assert a visual change looks right — you cannot see the rendered UI.
2. If a spec/precedent exists (UiTheme constants, §3.0 decisions), implement it exactly.
3. If not: at most 2 options, presented as concrete diffs or precise descriptions; implement
   the more conservative one; flag `TASTE-DECISION NEEDED`; stop after ONE round.
- ✅ "Dark-mode disabled-button color unspecified → used the existing palette's muted variant,
  flagged for screenshot review."
- ❌ Three self-directed "polish" iterations restyling buttons nobody asked about.

## 6. Direction-check ritual — before any task expected to exceed ~30 minutes

Write three lines before the first edit (in your working notes or the task description):
1. Goal, one sentence, falsifiable.
2. First `file:symbol` to touch.
3. The E1 check that will prove it worked **without a build**, and the highest tier the final
   claim can reach.

If you cannot write line 3, the task has no verification story — design one
(`build-and-verify.md` §4) or ask before starting. This ritual is what prevents the closed
loop: work that can only be validated by "run it and see" must say so *before* the work, not
after.
