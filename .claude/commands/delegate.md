---
description: Dispatch a subagent by protocol — routing table, filled template, then launch
argument-hint: [task type + one-line goal, e.g. "research: where are substats parsed"]
---

Task hint: $ARGUMENTS

1. First check dispatch.md §6 — should this be delegated at all? If not, say so and do it
   directly instead.
2. Pick the agent type + model from dispatch.md §2's routing table.
3. Copy the matching template from templates.md (T1 research / T2 feature / T3 refactor /
   T4 review / T5 verify) and fill EVERY {BRACE}, including the standard CONSTRAINTS block
   verbatim.
4. Show the filled template in your reply BEFORE dispatching, then launch via the Agent tool
   with the chosen model param.
5. When the report returns: check format compliance first (caps: ≤60 lines, no code block
   >10 lines), then evidence quality (path:line present for every claim), then content.
   Format violation → one reminder; a second violation counts as a failure per dispatch §4.
6. If the dispatch was an implementation (T2/T3): schedule the T5 fresh-context verifier
   before reporting "ready" to the user (dispatch §5).
