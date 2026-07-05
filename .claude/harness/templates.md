# Task-Assignment Prompt Templates

Fill-in-the-blank delegation prompts implementing `dispatch.md` §3. Copy the template, fill
every `{BRACE}`, delete lines marked `(opt)` if unused. Never delegate free-form.

## 0. Rules that apply to every template

- **Repeat, don't reference.** Explore/Plan agents don't load CLAUDE.md; assume the agent knows
  NOTHING about this project's rules. The CONSTRAINTS block below is embedded in every
  dispatch, verbatim.
- **Criteria are binary.** Banned words inside acceptance criteria: *improve, clean, robust,
  better, modern, high-quality* — none of them is checkable.
- **Report caps:** ≤60 lines, no code block >10 lines. A report violating format gets ONE
  reminder; a second violation counts as a failure (`dispatch.md` §4).
- **On receipt:** check format compliance first, then evidence quality (path:line present?),
  only then content.

**Standard CONSTRAINTS block** (paste into every template where marked):

```
CONSTRAINTS (non-negotiable):
- Repo: D:\Github\Inventory_Kamera, branch {branch}. WinForms app, net8.0-windows7.0.
- Do NOT run dotnet build / dotnet test unless this dispatch explicitly grants a BUILD GATE.
- Do NOT edit *.Designer.cs, *.resx, *.csproj, or any file outside SCOPE.
- Chained shell commands: use the Bash tool (PowerShell here is 5.1 — `&&` is a parser error).
- If you create a file: `git add` it immediately.
- 2 failed attempts at the same problem → STOP, report BLOCKED with both attempts described.
```

---

## T1 — Research / search (→ Explore agent, default model)

```
Search breadth: {medium | very thorough}.
GOAL: {the single question that must be answered}
CONTEXT:
- {≤4 bullets, each carrying a file path}
CONSTRAINTS: read-only — do not propose fixes; return conclusions, not code dumps.
ANSWER THESE:
1. {specific question}
2. {specific question}          (opt)
REPORT FORMAT: per question — answer in ≤3 sentences, then evidence as a path:line list.
End with "UNCERTAIN:" listing anything you could not confirm (write "UNCERTAIN: none" if so).
Max 40 lines. No code blocks >5 lines.
```

**Worked example (filled):**

> Search breadth: medium.
> GOAL: find every place artifact substat text from OCR is parsed into numeric values.
> CONTEXT:
> - Artifact scanning lives in `InventoryKamera/scraping/ArtifactScraper.cs`
> - Models in `InventoryKamera/game/Artifact.cs` (MainStat/SubStat)
> ANSWER THESE:
> 1. Which method(s) convert substat strings to numbers, and where are they called?
> 2. Is there existing tolerance/fuzz handling for OCR misreads (e.g. `IntRange`)?
> REPORT FORMAT: (as above)

A good report back looks like: *"1. `ArtifactScraper.ParseSubStat` (scraping/ArtifactScraper.cs:214)
does the conversion; called from `ScanArtifacts` loop (:167). 2. Yes — `IntRange`
(scraping/IntRange.cs:9) used for level tolerance, not substats. UNCERTAIN: none."*

---

## T2 — Feature implementation (→ general-purpose, model: sonnet)

```
You are implementing one bounded change in Inventory Kamera (a WinForms .NET 8 Genshin
inventory OCR scanner). Work only within SCOPE.

GOAL: {one sentence}
CONTEXT:
- {≤6 bullets with paths — include the WHY, not just the WHAT}
- Prior decisions that bind you: {verbatim quote from plan § / memory, with source}   (opt)
SCOPE: touch ONLY: {file list}. If the change genuinely requires another file: STOP, report
PARTIAL with the reason. Do not proceed past scope.
[paste standard CONSTRAINTS block]
BUILD GATE: {not granted | granted — run `dotnet build InventoryKamera.sln -c Debug` exactly
once, at the end, and report the exit code}
ACCEPTANCE CRITERIA:
1. {binary-checkable}
2. {binary-checkable}
FORBIDDEN: new NuGet packages; new TODO/HACK/NotImplementedException; public API changes
beyond {list or "none"}.
REPORT FORMAT:
Status: DONE | PARTIAL | BLOCKED
Criteria: {n}. PASS/FAIL — evidence (path:line)
Files changed: {path — one line each}
New files: {path — staged yes/no}
Surprises/risks: ≤3 bullets
If BLOCKED: both attempts, hypothesis + exact error each.
Max 50 lines. No code blocks >10 lines.
```

**Worked example (abbreviated fill):** GOAL: "Add a `HoldButton(Xbox360Button, int ms)` overload
to `game/GameController.cs` so navigation code can hold a stick/button for a caller-chosen
duration." SCOPE: `game/GameController.cs` only. CRITERIA: "1. New overload exists with
signature `public void HoldButton(Xbox360Button button, int milliseconds)`. 2. Existing
callers still compile-consistent: grep `HoldButton(` shows old call sites unchanged.
3. No `Thread.Sleep` on the UI thread — verify the method is not called from a Form event
handler directly (grep call sites)."

---

## T3 — Refactoring (→ general-purpose, model: sonnet)

Use T2's skeleton with these mandatory additions:

```
BEHAVIOR FREEZE: this is a refactor — zero observable behavior change. Every public signature
preserved except: {list or "none"}.
BEFORE any edit: record call-site counts for every symbol you will move/rename
(grep, note the numbers).
AFTER: counts must match, or every delta explained line-by-line in the report.
ACCEPTANCE CRITERIA must include:
- N. Old symbol `{name}`: 0 remaining references (grep count stated).
- N+1. New symbol `{name}`: exactly {K} call sites (grep count stated).
- N+2. {If BUILD GATE granted: build exit 0. If tests exist for the moved code: `dotnet test`
  green with counts.}
```

**Worked example (abbreviated):** "Move `ShowAltTabPrompt` from
`game/ControllerNavigationTests.cs` into `game/GameController.cs` as an internal static helper.
Freeze: no signature change. Before: grep `ShowAltTabPrompt` → expect 4 hits in tests file.
After: 0 hits in old location, 4 call sites resolve to the new one."

---

## T4 — Code review (→ general-purpose, model: sonnet)

For the *current working diff*, prefer the built-in `/code-review` skill. Use this template for
delegated review of specific files/commits with targeted concerns:

```
GOAL: review {files | commit range} for the specific concerns below. Nothing else.
CONTEXT:
- {≤4 bullets with paths}
CHECK SPECIFICALLY:
1. {concern — e.g. "Designer hazard: any `new Settings()` binding in the diff?"}
2. {concern — e.g. "Every event handler attached with += has a definition"}
3. {concern}
DO NOT: restyle, propose rewrites, review unlisted files, flag style/naming nits.
[paste standard CONSTRAINTS block]
REPORT FORMAT: per concern — CLEAR, or FINDING (severity high/med/low, path:line, one-sentence
concrete failure scenario: "given X, Y happens"). "Could be cleaner" is not a finding.
Max 40 lines.
```

---

## T5 — Fresh-context verification (→ general-purpose, model: sonnet; NEVER the implementer)

```
You are an acceptance verifier with fresh context. You have NOT seen the implementation
conversation. Do not trust any summary — verify everything from the actual files.

TASK UNDER VERIFICATION: {one sentence}
CLAIMED CHANGED FILES: {list}
ACCEPTANCE CRITERIA (verbatim from the original dispatch):
1. {…}
2. {…}
ALLOWED CHECKS: Read the files; `git diff --stat {range}`; grep; BUILD GATE:
{granted per build-and-verify.md §1(b/c) | not granted}.
MANDATORY EXTRA CHECKS (from judgment.md §3):
- D1: `git status --porcelain` — any untracked source files? List them.
- D2: symbol-consistency grep counts for anything renamed/added.
- D3: only if a *.Designer.cs is in the diff — grep `new Settings()` under InventoryKamera/ui/
  (must be 0) and confirm `global::` qualifiers present.
[paste standard CONSTRAINTS block]
REPORT FORMAT:
VERDICT: ACCEPT | REJECT
Per criterion: PASS/FAIL + path:line evidence
D-checks: D1 {result} | D2 {counts} | D3 {result or n/a}
If REJECT: the minimal facts the implementer needs to fix it — ≤5 bullets, no advice essays.
Max 40 lines.
```

**Worked example of a REJECT worth sending back:** *"VERDICT: REJECT. Criterion 2 FAIL —
`HoldButton(` grep shows a new call site in `ControllerNavigationTests.cs:88` calling the
2-arg overload with a bool; does not compile-check statically. D1: `game/GameController.cs`
still untracked. Facts: (1) fix the arg type at :88, (2) git add the new file."*

---

## T6 — Escalation package (fills `dispatch.md` §4; sent UP a tier or to the user)

```
ESCALATION from {haiku|sonnet|opus} after {N} failed attempts.
TASK (original goal, verbatim): {…}
CRITERIA CURRENTLY FAILING: {which numbers + the FAIL evidence}
ATTEMPT LOG:
- Attempt 1 — hypothesis: "{one sentence}" — change made: {files, one-line summary}
  — result: {exact error/failure output, verbatim, ≤5 lines}
- Attempt 2 — hypothesis: "{…}" — change made: {…} — result: {…}
TREE STATE NOW: {clean | diffs kept at {paths} | reverted}
RULED OUT: {bullets — hypotheses eliminated with evidence}
COULD NOT CHECK: {bullets — missing knowledge/permissions/tools}
REQUEST: {diagnose only | take over | approve new approach: {…}}
```

Escalating without this package is a protocol violation: the receiving tier would re-derive
everything you already learned, which is the exact waste this harness exists to prevent.
