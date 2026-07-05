# Handoff Letter to Future Sessions

Written 2026-07-03 by the one-time Fable 5 setup session, after all other harness files.
Audience: the user (tomorrow morning) and every future Sonnet/Opus/Haiku session.

## 1. Three things the user didn't ask about (but matter most)

**1. The user's build-test cycle is the scarcest resource in this project — spend it like money.**
Every handoff that fails at the user's machine costs: a build, an app launch, often a live
Genshin scan attempt, a report back, and a re-diagnosis by a session that wasn't there when it
broke. That's 10–30 minutes of human time per failure, against seconds of model time saved by
sloppy verification. Practical consequences: batch related changes into ONE coherent testable
unit per handoff (never two half-features); make the handoff report's "User should" line so
concrete that failure is unambiguous ("scan one 4×8 artifact page; all 32 items detected"
— not "try scanning"); and when a change is risky, prefer the sanctioned pre-handoff build
gate over optimism. The entire harness is economically justified by preventing ~2 failed
handoffs per week.

**2. The §6c controller work — the current center of gravity — is structurally unverifiable
by models, and the codebase already contains the right pattern for that.**
Everything that matters about controller navigation (Genshin focus handling, ViGEmBus timing,
UI-scheme switching, tab detection) is evidence-tier E4: only observable in the live game.
The division of labor that works: models build the mechanism plus a *manual test routine* in
`ControllerNavigationTests.cs` (one Options-menu item per capability), the user runs it and
reports. That file's pattern — plain static class, one-liner Click handlers, zero Designer
churn — is the template for ALL future UI-triggered test code. Two §6c constraints that must
survive every design iteration: constellation/talent panels need a mouse fallback regardless
of input scheme (user-stated, in memory `character-scan-revamp-plan`), and ViGEmBus is
unmaintained — treat any driver-level weirdness as a CB3 circuit breaker (stop, report), not
a debugging opportunity.

**3. This harness only works if it is committed, kept small, and treated as closed.**
Until `git add .claude CLAUDE.md` + commit happens, the harness is one `git clean` away from
nonexistence and invisible to any clone. It is deliberately a CLOSED set: 10 harness files, 2
hooks, 3 commands. The only file designed to grow is `LESSONS.md`. If a future session feels
the urge to add an eleventh concept file, the correct move is almost always a LESSONS entry
or a `[PROPOSAL]` instead. Also inherit this warning: the machine's clock, git dates, and
prose dates disagreed on setup day — never trust prose dates; anchor to commit hashes
(`reflection.md` §5).

## 2. How this system decays under weak models — and the countermeasures

| # | Decay mode | Countermeasure (already installed) |
|---|---|---|
| D1 | Status pointers go stale; sessions work on the wrong thing | `/catchup` step 5 forces a git-vs-plan-vs-CLAUDE.md discrepancy check before any work; truth order in iron rule 4 says which source to fix |
| D2 | LESSONS.md becomes an unread landfill | Hard condensation trigger (15 entries / 150 lines) enforced at `/catchup` step 4 |
| D3 | Rule erosion by convenience ("just this once") | The two worst violations are physically blocked (hooks); the rest are D-rules a fresh verifier re-checks — the implementer eroding a rule doesn't help them pass acceptance |
| D4 | The harness edits away its own guardrails | Change-control tiers (`reflection.md` §1): protected files need a user-approved `[PROPOSAL]`; bypass-token uses must appear in handoffs |
| D5 | Hook rot (paths move, git output changes) | Hooks fail OPEN — rot degrades silently to "no protection", never to "blocked workflow". Re-run the smoke tests below after any settings change |
| D6 | Delegation regresses to free-form prompts | `/delegate` requires showing the filled template before launch; format violations count as failures in the escalation ladder |
| D7 | Mega-session relapse (the 29 MB pattern) | Late-session summarization is where discipline dies first. `/handoff` makes ending a session cheap; prefer one session per task |
| D8 | CLAUDE.md regrows into a manual | Hard budget: CLAUDE.md ≤90 lines. Adding a line means routing or deleting one. New rules go in harness files, never inline |

**Hook smoke tests** (run after any settings/hook change, or if you suspect rot — both should
print a block message and exit 2):

```
echo '{"tool_name":"PowerShell","tool_input":{"command":"a && b"}}' | powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".claude/hooks/ps51-guard.ps1"
echo '{"tool_name":"Bash","tool_input":{"command":"git commit -m x"}}' | powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".claude/hooks/commit-guard.ps1"
```
(The second one exits 0 instead if the tree has no untracked source files — that's correct behavior.)

## 3. Open questions awaiting the user's answers

Assumptions were made to proceed (per the setup session's mandate). Record answers here.

| Q | Assumption in force | If the answer differs |
|---|---|---|
| Q1 Build gates: model may run one build/test at handoff/commit boundaries? | YES (grounded in feedback memory: "fine to build once before a big handoff") | Edit build-and-verify.md §1 (protected — needs your approval anyway) |
| Q2 Harness scope: project-only, nothing in ~/.claude? | Project-only | Copying the pattern to other repos is manual, by you |
| Q3 Commits: sessions commit only when asked; harness left uncommitted for review? | YES | If you want auto-commits at handoffs, say so — dispatch/judgment stay unchanged, build-and-verify §6 gains a step |
| Q4 computer-use/Chrome/preview MCP tools stay off by default? | OFF by default | Edit CLAUDE.md tool notes |
| Q5 GameController.cs + ControllerNavigationTests.cs are intentional WIP? | YES — untouched by setup | If stale, delete them; commit-guard will stop flagging once resolved |

**Answers recorded:** (none yet)

## 4. Unfinished items from the setup session

- **Hooks are written and pipe-tested but NOT active in the setup session itself** — the
  settings watcher only picks up `.claude/settings.json` created before session start.
  They activate on the next session (verified plan: step 3 of the checklist below).
- `MEMORY.md.bak` (in the auto-memory directory) can be deleted once you've reviewed the
  corrected index.
- `MODERNIZATION_PLAN.md` §0/§9 still carry the dark-mode and §6c status drift documented in
  `diagnostic-report.md` §2 — left in place DELIBERATELY: the plan is the user's document and
  had uncommitted edits during setup, so the setup session didn't touch it. The first
  `/catchup` will flag the discrepancies; fix them per `reflection.md` §4 once the user has
  committed their WIP.
- Nothing else — all planned files were written and verified. (If a later session finds a
  SKELETON marker anywhere, that file was truncated; restore from git history.)

## 5. First-session checklist — do this tomorrow morning

1. **Review** (10 min): read `.claude/harness/diagnostic-report.md`, then `CLAUDE.md`, skim
   the rest. Reading order matters — the diagnostic explains why everything else exists.
2. **Answer** the §3 questions above (edit the table or just tell the session; it will
   record them here — that's FREE-tier).
3. **Commit the harness** as its own commit: `git add CLAUDE.md .claude` then commit.
   Staging first matters: in any fresh Claude session the commit-guard is live and will list
   every unstaged harness file. Decide whether the two WIP §6c .cs files ride along; if they
   stay untracked, the guard blocking that commit is it WORKING — append `# [allow-untracked]`
   to that one commit command. (The guard only intercepts commits made through Claude
   sessions; commits from your own terminal never see it.)
4. **Start a fresh session** (hooks activate). Verify: ask the model to run
   `echo a && echo b` via the PowerShell tool — you should see the `[ps51-guard]` block
   message, NOT the raw PS parser error.
5. **Run `/catchup`** — it should report branch, WIP, phase, and "discrepancies: …"
   (it will likely flag that plan §0 lags the dark-mode commit — letting it flag that is
   itself a test that the discrepancy check works).
6. From then on: normal work. `/delegate` for dispatches, `/handoff` to end. After a few
   days, optionally run the `fewer-permission-prompts` skill to extend the allowlist from
   real usage data.
