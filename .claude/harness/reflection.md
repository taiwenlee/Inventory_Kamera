# Knowledge Iteration & Reflection Protocol

How future sessions update this harness without degrading it. Core principle: **the guarded
don't edit their guardrails** — knowledge files evolve freely, rule files need the user.

## 1. Change-control tiers

**FREE — edit without asking, any session:**
- `LESSONS.md` — append entries (format §2). This is the default destination for anything learned.
- `project-map.md` — factual corrections, with path evidence stated in the handoff.
- `CLAUDE.md` → **only** the "Current focus" section (keep ≤5 lines).
- `handoff-letter.md` §3 — recording the user's answers when they arrive.
- Auto-memory directory files — per the memory system's own rules (and §6 below).
- `MODERNIZATION_PLAN.md` status markers — per §4 ritual (the plan is the user's document;
  status-marker updates only, never restructure it).

**PROPOSE-FIRST — write a `[PROPOSAL]` entry in LESSONS.md; the user approves BEFORE editing:**
- `CLAUDE.md` iron rules or routing table
- `dispatch.md`, `judgment.md`, `templates.md`, `build-and-verify.md`
- `reflection.md` (this file)
- `.claude/settings.json`, anything in `.claude/hooks/`, `.claude/commands/`
- Deleting or renaming any harness file

**Never:** disabling a hook "temporarily" to get past it. The hooks' bypass tokens exist for
legitimate paths; every bypass use must appear in that session's handoff report.

## 2. LESSONS.md entry format

Append-only, sequential IDs, ≤10 lines per entry. Area enum:
`build | git | winforms | ocr | scan | tooling | dispatch | other`.

```
## L-{NNN} | {YYYY-MM-DD} | {area} | {one-line title}
Symptom: {≤2 lines — what was observed}
Cause: {1 line — root cause, not the proximate error}
Rule: {1 line, imperative — what to do next time}
Evidence: {commit hash / path:line / plan §}
```

Proposal variant — same shape plus two lines:

```
Proposed change: {target file §} — {exact new/changed wording}
Status: awaiting user       ← the user flips this to "approved YYYY-MM-DD" or "rejected"
```

Entry-worthiness test: would this rule have saved ≥10 minutes or one user build-cycle if it
had existed yesterday? If no, don't log it. Never log one-off typos or already-documented rules.

## 3. Condensation trigger

When `LESSONS.md` exceeds **15 entries or 150 lines**, the NEXT session must condense before
starting new work (the `/catchup` command surfaces this):

1. Group entries by area.
2. Any ≥2 entries sharing a root cause → promote to a single rule: if the natural home is a
   FREE-tier file, edit it directly; if a protected file, write one `[PROPOSAL]` entry.
3. Move promoted and stale raw entries to `LESSONS-ARCHIVE.md` (create it on first need).
4. IDs stay monotonic forever — never renumber, never reuse.

## 4. Status-update ritual — keeping MODERNIZATION_PLAN.md true

Observed failure mode (2026-07-03): §0 said dark mode "not started" while the dark-mode commit
was already on the branch; §9 contradicted §6c's own header. Multiple status locations drift
independently. Therefore, any handoff that completes or starts plan-tracked work updates ALL of:

1. §0 "Status at a glance" table — the affected row.
2. The relevant section header's marker (✅ / 🔄 / 🔻) and a one-line note with date.
3. §9's candidate list, if the item appears there.
4. `CLAUDE.md` "Current focus" section.

Anchor claims with a commit hash once one exists. Never introduce status claims anywhere else
in the plan — three sanctioned locations is already the maximum this discipline can hold.

## 5. Date discipline

On 2026-07-03 the system clock, git commit dates, and prose dates in memories disagreed with
each other (see `diagnostic-report.md` §6). Therefore:

- Prose dates are informational; **commit hashes are authoritative.** When a date matters,
  run `git log -1 --format="%h %ad"` and cite both.
- Never compute "N days ago" arithmetic from prose dates.
- Memory files describe the past; code describes the present. On contradiction: code wins,
  fix the memory in the same turn.

## 6. Memory-directory hygiene

Subagents receive only the first 200 lines of `MEMORY.md` — the index IS the memory as far as
delegated work is concerned. Observed failure (2026-07-03): the index claimed "still targets
net472" long after the net8 migration landed; any subagent would have inherited that as fact.

- After updating any memory body, update its index line in the same turn.
- Index lines must be true stand-alone (assume the body is never read).
- Contradiction between index/body/code → fix immediately, code wins.
- Run the `consolidate-memory` skill when the index exceeds ~20 entries or after a major
  phase completes.
- New memories about code behavior must carry a commit anchor.
