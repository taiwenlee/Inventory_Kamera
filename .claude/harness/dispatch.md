# Model Dispatch & Escalation Protocol

Purpose: keep the main session's context clean (the commander), push bulk work into disposable
subagent contexts (the field), and make failure handling mechanical instead of judgment-based.
Written for Sonnet/Opus-class main sessions. Templates referenced here live in `templates.md`.

## 1. Roles — commander stays off the field

**Standard task sequence** (anything bigger than a trivial known-location tweak):
1. **LOCATE** — target files unknown? Run a T1/Explore pass first. Never dispatch
   implementation work at an unknown location.
2. **DECIDE** — locations known: apply §6. Small single-file edit → do it yourself;
   bigger → dispatch T2/T3.
3. **IMPLEMENT** — direct edit or dispatch.
4. **VERIFY** — per §5; the MAIN SESSION spawns the T5 verifier. Then hand off per
   build-and-verify.md §6.

**The main session does:** decisions, architecture, user communication, small surgical edits
(≤3 files whose locations are already known), filling templates, reading reports.

**The main session MUST delegate:**
- Repo-wide searches ("where is X handled?", "find all writers of Y") → Explore agent
  (Explore does NOT load CLAUDE.md — inline the constraints it needs; see §2).
- Reading more than 2 files merely to understand something → Explore agent, ask for conclusions.
- Implementation touching >3 files or an expected diff >150 lines → general-purpose (T2/T3).
- All acceptance verification of delegated or multi-file work → fresh verifier (§5, T5).
- Mechanical batch changes (same edit at many sites) → general-purpose after the pattern is
  proven once (see de-escalation, §4).

**The main session MUST NOT:**
- Pull large code bodies into its own context when a subagent could return a conclusion.
- Accept its own multi-file work without the §5 verifier.
- Spawn a sibling agent to continue a conversation — use SendMessage to the existing agent.

**Report hygiene:** a subagent report longer than ~60 lines, or containing a code block over
10 lines, is a protocol violation. Extract what you need, discard the rest, and tighten the
REPORT FORMAT section of your next dispatch.

## 2. Routing table: task type → agent type → model

| Task | Agent type | `model` param | Notes |
|---|---|---|---|
| Locate code / facts in repo | Explore | (leave default) | Read-only. Does NOT load CLAUDE.md — inline any constraint it needs. State breadth: "medium" or "very thorough". |
| Summarize a long doc / log / diff | general-purpose | haiku | Extraction only. Never give haiku judgment calls or edits. |
| Bounded implementation (files named, binary criteria) | general-purpose | sonnet | Template T2 (feature) / T3 (refactor). |
| Open-ended debugging, design, ambiguity | main session or Plan agent | opus | Do not delegate ambiguity downward — resolve it first, then dispatch. |
| Acceptance verification | general-purpose | sonnet | Template T5. Fresh context. Never the implementer. |
| Claude Code / harness mechanics questions | claude-code-guide | (default) | Never answer Claude-Code-config questions from memory. |
| Codebase-map refresh | Explore | (default) | Main session then updates `project-map.md` itself. |

`model` values to use here: `haiku`, `sonnet`, `opus`. The tool schema may also accept other
values (e.g. `fable`); they are not part of this project's tiering — requesting them is the
user's call, never yours (policy set 2026-07-03).

## 3. The three-part assignment contract

Every dispatch contains all three parts. Templates in `templates.md` enforce this shape.

1. **GOAL + CONTEXT** — one-sentence goal; ≤6 bullets of background, each carrying a file path;
   the iron-rule constraints the agent needs, inlined verbatim (assume the agent has NOT read
   CLAUDE.md — for Explore/Plan that is literally true).
2. **ACCEPTANCE CRITERIA** — numbered; each one binary-checkable by reading a file or running an
   allowed command. Banned words inside criteria: *improve, clean, robust, better, modern,
   high-quality* (not checkable). Good: "grep `new Settings()` in `InventoryKamera/ui/` returns
   0 hits." Bad: "make sure the Designer file stays clean."
3. **REPORT FORMAT** — fixed structure: `Status: DONE|PARTIAL|BLOCKED`, then per-criterion
   PASS/FAIL with `path:line` evidence, then surprises/risks. No code blocks >10 lines.

## 4. Escalation / de-escalation ladder

**Definitions.** A *failure* = (a) two consecutive errored tool calls, (b) an acceptance
criterion returned FAIL, (c) a report ignoring the required format after one reminder, or
(d) a T5 verifier verdict of REJECT (counts against the implementing tier, not the verifier).
*Same subtask* = same acceptance-criteria set. All failure kinds increment ONE counter per
subtask, and the count travels with the task. If you don't know which tier YOU are, check
your system context for the model name; still unclear → assume Sonnet and escalate upward to
an opus subagent when the ladder requires it (the conservative choice).

| Event | Action |
|---|---|
| Haiku fails ONCE (tool/syntax error) | Re-dispatch the identical template to **sonnet**. Do not coach haiku, do not retry haiku. |
| Sonnet fails ONCE | One retry at sonnet is allowed — but only with a NEW one-sentence failure cause per judgment.md A2 (same agent via SendMessage, or a re-dispatch; either way this is attempt 2 of 2 at this tier). |
| Sonnet fails the same subtask TWICE | STOP. Fill template **T6** (escalation package: both attempts, exact error text, files touched, one-sentence hypothesis per attempt). Escalate to **opus** (main session, or an opus subagent if the main session is Sonnet). |
| Opus resolves it | If the fix is a repeatable mechanical pattern (needed at ≥3 sites), **de-escalate**: re-dispatch to sonnet/haiku with the worked example embedded in the template. Cheap models apply proven patterns; they don't discover them. |
| Opus fails twice, OR 2 full escalation rounds consumed | Circuit breaker CB5 (`judgment.md` §4): stop all attempts, write the T6 package into the handoff, ask the user. |

**Hard budget: max 2 attempts per tier, max 2 tier-hops per issue.** Every escalation event —
up or down — gets a `LESSONS.md` entry (format: `reflection.md` §2): what triggered it and what
pattern resolved it.

## 5. Isolated verification — the implementer never self-accepts

- Delegated implementations (T2/T3) are accepted only by a **fresh** general-purpose sonnet
  running template **T5**. *Fresh, operationally:* a NEW Agent tool call. Never SendMessage
  to the implementer's agent, never reuse its instance — a context that watched the
  implementation happen cannot verify it.
- The verifier receives: the acceptance criteria, the claimed changed-file list, and
  `build-and-verify.md`. It does **NOT** receive the implementer's report narrative — that
  prevents anchoring on the implementer's claims.
- The verifier must: re-read the actual files (never trust the summary), run the allowed checks
  (`git diff --stat`, grep consistency counts, and the build/test gate only if
  `build-and-verify.md` §1 permits it at this boundary), and return PASS/FAIL per criterion
  with `path:line` evidence.
- A REJECT verdict is one failure on the implementer's §4 counter for that subtask.
- If the **main session implemented something itself**: for any change >1 file or >30 diff
  lines, the T5 verifier is still mandatory before telling the user "ready". At or below that
  size, running `judgment.md` §3's D-checklist in the main session suffices.
- **Taste-flavored forks** (two viable designs): dispatch both directions as cheap T1 probes,
  compare returned artifacts, decide in the main session. Never ask a single subagent to "pick
  the best" — that outsources judgment to the least-informed context.

## 6. When NOT to delegate

- The answer is already in CLAUDE.md, this harness, or plan §0 — 30 seconds of looking beats
  a dispatch.
- Single-file edit at a known location — the briefing costs more than the doing.
- The task is ambiguous or taste-based — subagents amplify ambiguity, never resolve it.
- You want to follow up with an agent that already has context — SendMessage, don't respawn.

## 7. Concurrency and cost rules

- Never two agents with write access to the same file at the same time. Parallel read-only
  agents are fine; cap at 3 concurrent.
- Subagent context is disposable by design. Agents do not write scratch files into the repo;
  results come back in the report, or into files this harness explicitly designates.
- Expected runtime >2 minutes → `run_in_background: true` and keep working; foreground only
  when the very next step depends on the result.
- One follow-up question to a finished agent is cheaper than a fresh spawn re-deriving context.
