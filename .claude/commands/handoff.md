---
description: Run the Definition-of-Done checklist and produce the mandatory handoff report
---

End-of-work ritual. Run in this order:

1. Execute judgment.md §3 D-rules D1–D7 in order; print each rule's PASS/FAIL with its
   evidence (D1 needs `git status --porcelain -uall`).
2. Decide the build gate per build-and-verify.md §1. If condition (a)/(b) applies, run the
   §2 canonical commands and record exit codes. If not, record which condition made it
   unnecessary.
3. Fill the handoff report template from build-and-verify.md §6 verbatim — every line,
   "none" where empty, every claim tagged with its evidence tier (judgment.md §1).
4. If plan-tracked work was completed or started: run the status ritual (reflection.md §4 —
   plan §0 row, section marker, §9 list, CLAUDE.md Current focus).
5. If anything reusable was learned (test: reflection.md §2's entry-worthiness rule), append
   a LESSONS.md entry.

A handoff without this report is not a handoff. If any D-rule FAILs and can't be fixed now,
Status is PARTIAL or BLOCKED — never DONE.
