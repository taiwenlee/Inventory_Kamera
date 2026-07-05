# LESSONS — append-only pitfall log

Format and rules: `reflection.md` §2. Append at the bottom, sequential IDs, ≤10 lines each.
Condense per `reflection.md` §3 when >15 entries or >150 lines. Never rewrite old entries.

## L-001 | 2026-07-02 | git | Untracked new file missed by a commit
Symptom: commit c2de5e3 shipped code referencing ControllerSpike.cs, but the file wasn't in it;
fix-up commit f34887c exists only to add it.
Cause: SDK csproj auto-globs *.cs, so nothing forces `git add` — the gap is silent until
someone builds from that commit.
Rule: `git add` every new file immediately at creation; commit-guard hook now blocks commits
with untracked source files.
Evidence: commits c2de5e3 → f34887c.

## L-002 | 2026-07-03 | tooling | MEMORY.md index drifted from memory bodies
Symptom: index said "app still targets net472" and "WGC fix planned" while the bodies (and
csproj) said net8 was done and WGC was tried-and-reverted.
Cause: bodies were updated across sessions; index one-liners never were. Subagents see ONLY
the index, so they inherited falsehoods.
Rule: update the index line in the same turn as the body; on contradiction, code wins.
Evidence: MEMORY.md vs memory/net8-blocked-by-accord.md; InventoryKamera.csproj:12.

## L-003 | 2026-07-02 | winforms | VS Designer regeneration corrupts MainForm.Designer.cs
Symptom: build breaks (stripped `global::`) and/or all user settings silently reset at launch
(controls rebound to a throwaway `new Settings()` instance). Recurred 3+ times in one session.
Cause: namespace `InventoryKamera` collides with class `InventoryKamera`; the Designer
re-serializes without qualifiers and rebinds settings.
Rule: after ANY Designer-file change: grep `new Settings()` under InventoryKamera/ui/ (expect
0) and confirm `global::…Properties.Settings.Default` bindings survive.
Evidence: MODERNIZATION_PLAN.md §3.0; build-and-verify.md §5.

## L-004 | 2026-07-03 | git | git checkout on a corrupted Designer file destroyed real WIP
Symptom: L-003 fired live (font/size baked ~33% smaller — the "2x too small" bug, ratio was
exactly 96/144 DPI). Fixed it via `git checkout HEAD -- MainForm.Designer.cs`, which also wiped
~11 menu items' worth of genuine uncommitted WIP that predated the session, because I assumed
HEAD already had that wiring committed without checking.
Cause: `git status -sb` at session start already showed `M MainForm.Designer.cs` (uncommitted
WIP) — I read that line at catchup but didn't re-check it before running a destructive checkout
several turns later. `git checkout HEAD` on a file with real uncommitted work throws that work
away with no git object to recover it from.
Rule: before `git checkout HEAD --  <designer-file>` (or any destructive revert) to strip
corruption, re-run `git status`/`git diff HEAD -- <file>` in that same turn and confirm exactly
which hunks are corruption vs. wanted WIP. If both are mixed in one file, hand-revert only the
corrupted hunks (or reconstruct wanted content from conversation history) — never blanket
checkout on the assumption "HEAD already had this."
Evidence: this session's MainForm.Designer.cs recovery, 2026-07-03.
