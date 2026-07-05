# Harness Leakage Diagnostic Report

Written 2026-07-03 by the one-time Fable 5 harness-setup session. This is the anchor document:
every other harness file implements a fix described here. Audience: future Sonnet/Opus/Haiku
sessions and the user. Nothing in this file is advice — each leak ends in a physical mechanism
that now exists on disk.

## 1. Scope and evidence base

Inspected: `~/.claude` (global config), the absent project `.claude/`, auto-memory at
`~/.claude/projects/D--Github-Inventory-Kamera/memory/`, `MODERNIZATION_PLAN.md` (105 KB),
both csproj files, CI workflows, git history, and session transcript sizes. State at
inspection time:

- No CLAUDE.md existed anywhere (global or project). No hooks, no commands, no agents,
  no permission rules. Global `settings.json` was 102 bytes of notification flags.
- All institutional knowledge lived in 5 auto-memory files + `MODERNIZATION_PLAN.md`.
- One session transcript (`64c2f74e`) is 29.4 MB — a single mega-session produced the entire
  net8 migration and all memory files. That working style dies with the strong model; this
  harness exists because future sessions will be shorter, weaker, and more numerous.

## 2. Leak #1 — Stale and invisible institutional knowledge (token waste + wrong-direction risk)

**Observed, not hypothetical.** At inspection time the auto-memory INDEX (`MEMORY.md`) claimed:

| Index claimed | Reality (verified in files/git) |
|---|---|
| "why the app still targets net472" | `InventoryKamera.csproj:12` targets `net8.0-windows7.0`; migration finished (commit `bec266a`) |
| "planned Windows.Graphics.Capture fix after net8" | WGC was **built, live-tested, and reverted** (revert commit `6b08803`); the fix failed against real HDR + a hooking overlay |

The same drift exists inside `MODERNIZATION_PLAN.md` itself: §0's table said "dark mode …
not started" while commit `e9a60a2` ("Phase 3 §3.4: dark mode") was already on the branch;
§9 item 3 said §6c was "still blocked on keyboard testing" while §6c's own header says
feasibility was confirmed and keyboard ruled out.

**Failure scenario for a weak model:** a session trusts the index, believes the app is net472,
and writes framework-compat code — or worse, re-proposes the WGC capture backend and burns an
entire session re-implementing a change that was already tried and reverted for reasons no
amount of code-reading reveals (real-hardware HDR behavior, Outplayed's in-process hooking).
Cost of that one mistake: a full session plus a user-led live test that fails predictably.

**Structural causes:** (a) subagents receive ONLY the first 200 lines of `MEMORY.md` — the
index one-liners, not the bodies — so every delegated task inherited "still targets net472"
as fact with no access to the correcting detail; (b) auto-memory is not git-versioned and
lives outside the repo; (c) the project had multiple "current status" locations (index
one-liners, §0 table, §9 list, section headers) with no rule for which one wins.

**Physical blocks now in place:**
- `CLAUDE.md` (auto-loaded in every session AND every subagent, git-tracked) is the single
  router. It declares the status-truth hierarchy: **git evidence > MODERNIZATION_PLAN §0 table
  > section headers > §9 prose > memory index**.
- `project-map.md` gives subagents the orientation that memory can't.
- `MEMORY.md` index corrected and demoted to pointers (done this session).
- `reflection.md` §4–§5 define the status-update ritual and date discipline that keep this true.

## 3. Leak #2 — Delivery-integrity gaps: the closed-loop failure (focus loss + wasted user cycles)

The workflow contract (recorded feedback): the model edits, the **user builds and live-tests**.
No per-edit builds. This is correct — but it means every handoff crosses an unverified gap, and
the evidence shows things fall into it:

- Commit `f34887c` exists solely to add `ControllerSpike.cs`, **forgotten from the previous
  commit**. At inspection time the same pattern was live again: `game/GameController.cs` and
  `game/ControllerNavigationTests.cs` sat untracked while files referencing them were modified.
  SDK-style csproj auto-globs `**/*.cs`, so nothing but `git add` registers a new file — the
  failure is silent until someone builds from a clone/stash.
- Plan §0 "Standing gap": two real bugs (a `NullReferenceException` and a cancel-latency
  regression) shipped through **build-green AND test-green** and were only caught live. So
  "compiles + 121 tests pass" is necessary, never sufficient.
- Plan §3.0 documents the Designer regeneration hazard: touching `MainForm.Designer.cs` risks
  (a) stripped `global::` qualifiers (namespace/class name collision → build break) and
  (b) settings rebound to a throwaway `new Settings()` instance (**silently wipes user settings
  at launch** — the worst kind of bug: builds clean, tests green, data loss live).

**Failure scenario:** a Sonnet session edits five files, reports "done", the user builds —
compile error from a hazard above, next session re-diagnoses from scratch without the failure
context. Two user build-cycles and half a session lost per occurrence.

**Physical blocks now in place:**
- `commit-guard` hook: physically blocks `git commit` while untracked source files exist
  (bypass token documented in the hook message itself).
- `judgment.md` §3 Definition of Done: binary D-rules, including "new files are `git add`ed"
  and the Designer-hazard checks.
- `build-and-verify.md`: one sanctioned build/test gate at handoff (matches the recorded
  feedback), canonical commands copied from CI (not invented), and a mandatory handoff report
  where every claim carries an evidence tier (E0–E4). Claiming live behavior above tier E3
  is forbidden — live truth belongs to the user.
- `dispatch.md` §5: the implementer never accepts their own work; a fresh-context verifier does.

## 4. Leak #3 — Windows tool minefield on an unconfigured harness (tool-invocation errors)

The highest-frequency, lowest-glamour leak. Concrete traps on this machine:

1. **PowerShell 5.1 has no `&&`/`||`** — a parser error, not a warning. Weak models write
   `git add -A && git commit` by reflex, fail, then retry variations; a retried compound
   command can leave partial state (the `add` succeeded on attempt 2, the `commit` never ran).
2. **PowerShell 5.1 writes UTF-16 LE with BOM by default.** A weak model "fixing" a JSON or
   .md file via `Out-File`/`Set-Content` without `-Encoding utf8` produces a file other tools
   read as garbage. Worst case is self-destruction: corrupting `.claude/settings.json` this way
   disables the harness itself.
3. **Irrelevant MCP surface.** This is a WinForms desktop app: `preview_*` tools (web dev
   servers), `claude-in-chrome`, and `computer-use` are off-topic by default. Plan §3.0 records
   the actual visual-verification workflow: **the user pastes screenshots**; no tool drives the
   running app. A weak model reaching for `preview_start` or screenshotting the desktop is
   burning tokens on a dead end.
4. **Zero permission rules** meant every shell call prompted the user — which trains users
   toward blanket auto-approval, the opposite of a safety harness.

**Physical blocks now in place:**
- `ps51-guard` hook: blocks any PowerShell tool call containing `&&`/`||` with a corrective
  message (use `;`, `if ($?)`, or the Bash tool).
- `CLAUDE.md` tool-notes section: Bash for chained commands; file edits via Write/Edit tools
  only (never shell redirection); MCP off-by-default list.
- `.claude/settings.json`: curated allowlist for read-only git/dotnet commands so routine
  verification doesn't prompt, while mutating commands still do.

## 5. Physical blocks implemented — summary table

| Leak | Mechanism | File | Active when |
|---|---|---|---|
| #1 | Router + truth hierarchy | `CLAUDE.md` | every session + every subagent |
| #1 | Repo orientation for subagents | `.claude/harness/project-map.md` | on demand via router |
| #1 | Status ritual + date discipline | `.claude/harness/reflection.md` §4–5 | at every handoff |
| #2 | Untracked-file commit block | `.claude/hooks/commit-guard.ps1` | after next session restart |
| #2 | Definition of Done, evidence tiers | `.claude/harness/judgment.md` | at every handoff |
| #2 | Build gate + handoff report format | `.claude/harness/build-and-verify.md` | at every handoff |
| #2 | Fresh-context verification | `.claude/harness/dispatch.md` §5 + `templates.md` T5 | any delegated implementation |
| #3 | PS 5.1 `&&` block | `.claude/hooks/ps51-guard.ps1` | after next session restart |
| #3 | Permission allowlist | `.claude/settings.json` | after next session restart |
| #3 | Tool routing notes | `CLAUDE.md` | every session |

Hook design principle: **fail-open**. Every hook wraps its body in try/catch and exits 0 on any
internal error — a broken hook must never brick the workflow. Hooks announce their own name in
their block message so a future model knows what stopped it and where to look.

## 6. Secondary leaks (accepted or cheaply mitigated)

- **Mega-session pattern** (29.4 MB transcript): context summarization churn late in such
  sessions degrades exactly the discipline this harness needs. Mitigation: `/catchup` and
  `/handoff` commands make short task-scoped sessions cheap. Not enforced physically — the
  user controls session length.
- **MODERNIZATION_PLAN.md is 105 KB** (~30k tokens if read whole). Iron rule in CLAUDE.md:
  grep its headings first, read only the section you need. Never load it wholesale.
- **Date unreliability**: prose in memories/plan references 2026-07-05 while git commits say
  2026-07-02 and the system clock said 2026-07-03 — at least one source is wrong. Rule
  (reflection.md §5): timestamps in prose are untrusted; anchor claims to commit hashes.
- **`.gitignore` has no `.claude/settings.local.json` entry.** Left unchanged (diagnosis-first
  rule; .gitignore is user-owned). Flagged in handoff-letter.md for the user.
- **Permission mode / model selection** are user-side toggles; harness assumes nothing about them.

## 7. Capability limits of this harness — honesty clause

Decomposition + isolated verification approximate senior judgment for **mechanical
correctness**: structure, regressions, consistency, completeness. They cannot approximate:

1. **Visual/UX taste** (theming, layout, copy tone). The proven workflow is §3.0's loop: model
   implements → user runs and screenshots → model adjusts. A weak model must never claim a
   visual change "looks right" (it cannot see it) and must never iterate on taste unprompted.
   **Response standard:** implement the documented spec if one exists; if none, present at most
   2 concrete options (as diffs or precise descriptions), implement the more conservative one,
   tag the handoff `TASTE-DECISION NEEDED`, stop.
2. **Live-behavior truth** (OCR accuracy, game timing, HDR, overlay interaction). Evidence tier
   E4 is reachable only by the user. **Response standard:** cap claims at E3, tag handoff
   `LIVE-VERIFY NEEDED`, list exactly what the user should observe.
3. **Novel systems/interop debugging** (the WGC vtable-dispatch saga is the benchmark: multiple
   plausible approaches fail identically with misleading errors). Weak models treat
   `wgc-interop-patterns` memory as settled fact rather than re-deriving. **Response standard:**
   two failed hypotheses → escalate per dispatch.md §4, never brute-force.
4. **Product prioritization** (what's worth building next). §9 of the plan lists candidates;
   choosing among them is the user's call. **Response standard:** never start unlisted feature
   work; if the plan seems exhausted, ask.

## 8. Fact verification ledger

Verified this session (evidence): net8.0-windows7.0 TFM (csproj:12); xUnit test project at
`tests/InventoryKamera.Tests/` (csproj read); canonical build/test commands (`.github/workflows/
build.yml:24-30`); SDK 9.0.101 on machine (`dotnet --version`); LangVersion `latest` (both
csproj); no pre-existing CLAUDE.md/.claude (filesystem checks); memory-index staleness (index
vs. memory bodies vs. csproj); f34887c forgotten-file commit (git log).

Hook mechanics were verified two ways: against current docs via a claude-code-guide agent
(exit-2 blocks with stderr fed to the model; `matcher` accepts `Bash|PowerShell` list syntax;
hooks may declare `"shell"`; `$CLAUDE_PROJECT_DIR` is provided), and empirically — both hook
scripts were pipe-tested with synthesized stdin payloads (all five cases returned the designed
exit codes and messages). One limitation confirmed live: a `.claude/settings.json` created
mid-session is not picked up by the settings watcher, so the hooks activate from the NEXT
session onward — see handoff-letter.md §4–§5 for the activation check.
