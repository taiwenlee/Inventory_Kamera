# Inventory Kamera — Claude Code entry point

WinForms **.NET 8** desktop app (`net8.0-windows7.0`) that scans a Genshin Impact inventory via
Win32 screen capture + Tesseract OCR and exports GOOD-format JSON. Windows-only by design
(screen capture + input injection). Solo project: the user develops and live-tests; you assist.

## Iron rules (absolute — details live in the routed files)

1. **The user builds and live-tests; you don't.** Never run `dotnet build`/`dotnet test` after
   routine edits. One build/test gate is allowed ONLY right before a commit/handoff, or when the
   user asks. Contract: `.claude/harness/build-and-verify.md` §1.
2. **Never claim what you didn't verify.** Every "done" claim carries an evidence tier E0–E4
   (`.claude/harness/judgment.md` §1). Live behavior (E4) can only ever be claimed by the user.
3. **`git add` every new file immediately after creating it.** The csproj auto-globs `*.cs`;
   git is the only registration step. (Lesson: commit `f34887c` exists solely because this
   was missed once.)
4. **`MODERNIZATION_PLAN.md` is 105 KB — never read it whole.** Grep `^#` for headings, then
   read only the section you need. Status truth order: **git evidence > plan §0 table > section
   headers > §9 prose > memory index.** When two disagree, the higher one wins; fix the lower.
5. **Designer files are hazardous.** Before editing any `*.Designer.cs`, read
   `build-and-verify.md` §5. Keep new logic out of Designer files (follow the
   `ControllerNavigationTests.cs` pattern: plain static class, thin Click handlers).
6. **PowerShell here is 5.1**: `&&`/`||` are parser errors — chained commands go to the Bash
   tool. Never write files via shell redirection (`Out-File`/`>` default to UTF-16); use the
   Write/Edit tools for all file content.
7. **Delegate by protocol.** Location unknown? Locate first (Explore/T1). Small edit at a
   known location? Do it yourself (`dispatch.md` §6). Bigger: follow `dispatch.md` §1's
   standard sequence with a filled template from `templates.md`. Explore/Plan agents do NOT
   auto-load this file — inline the constraints they need.
8. **Two failed attempts on the same problem → stop.** Follow `judgment.md` §2 (abandon
   signals) and §4 (circuit breakers); attempt counting lives in `dispatch.md` §4. Never a
   silent third attempt.

## Routing table — read the right file before acting

| Situation | Go to |
|---|---|
| Session start / regaining context | run `/catchup` (or manually: `git log -5` + `git status` + plan §0 + §9) |
| About to delegate to a subagent | `.claude/harness/dispatch.md` + `templates.md` |
| Deciding "is this done?" / stuck / "should I ask?" | `.claude/harness/judgment.md` |
| Building, testing, committing, handing off | `.claude/harness/build-and-verify.md` (or run `/handoff`) |
| Need orientation — what lives where | `.claude/harness/project-map.md` |
| Hit a pitfall / learned something reusable | `.claude/harness/reflection.md` → append `LESSONS.md` |
| Editing harness files themselves | `.claude/harness/reflection.md` §1 (change control) |
| Why the harness is shaped this way | `.claude/harness/diagnostic-report.md` |
| Roadmap / phase status | `MODERNIZATION_PLAN.md` §0 table + the one relevant § |
| Tempted to fix HDR/overlay/capture | memory `hdr-overlay-root-cause`: WGC was tried and **reverted** — read it first |

## Tool notes for this machine

- Chained or compound shell commands → **Bash tool**. PowerShell only for Windows-specific ops.
- MCP tools OFF by default for this repo: `preview_*` (this is not a web app),
  `claude-in-chrome`, `computer-use`, `visualize`. Use only on explicit user request.
- Visual verification loop: **the user runs the app and pastes screenshots** (this is how all of
  plan §3.0 landed). Never claim a visual change looks right; you cannot see it.
- Active hooks: `ps51-guard` (blocks `&&`/`||` in PowerShell calls), `commit-guard` (blocks
  commits while untracked source files exist; its message contains the bypass token). Both fail
  open. If a hook blocks you, read its message and comply — don't fight it.

## Current focus (keep this section ≤5 lines; update per reflection.md §4)

Branch `Modernization-preview`, clean, up to date with origin. §6c controller-driven scan
navigation is **done**: all five scan types (Weapons/Artifacts/Materials/Character Dev
Items/Character) are controller-driven and mouse-mode code is removed. Remaining small gaps:
weapon locked-status detection, artifact sort-mode selection. Next-step candidates: plan §9.
