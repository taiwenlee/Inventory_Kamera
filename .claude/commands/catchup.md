---
description: Re-orient at session start — git reality vs. plan status vs. current focus, plus lessons-log health
---

Re-orient in this exact order; do not start other work until step 5 is printed.

1. Run `git log -5 --oneline` and `git status -sb` (Bash tool).
2. Grep `^#` headings of MODERNIZATION_PLAN.md, then read ONLY the §0 table (~lines 9–25).
   Never read the whole file (CLAUDE.md iron rule 4).
3. Read the "Current focus" section of CLAUDE.md.
4. Check `.claude/harness/LESSONS.md`: if it has more than 15 entries, condensation is due —
   do `reflection.md` §3 BEFORE new work.
5. Print ≤10 lines: current branch + HEAD; uncommitted/untracked WIP; active phase/§ per plan
   §0; any DISCREPANCY between git evidence, plan §0, and CLAUDE.md Current focus (say
   "none" explicitly if none); the task you're about to start.

Discrepancies are findings, not blockers: state them, fix the lower-ranked source per the
truth order in CLAUDE.md iron rule 4 (git > §0 > headers > §9 > memory index), then proceed.
