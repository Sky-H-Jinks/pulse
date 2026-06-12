---
description: Research → plan → approve → implement pipeline for one feature
---

Feature request: $ARGUMENTS

Run this pipeline in order:

1. Use the **researcher** subagent to investigate everything needed for
   this feature: the service README spec, codebase patterns, library docs,
   risks. If it reports the feature is outside the current phase, stop and
   tell me instead of proceeding.
2. Use the **planner** subagent to turn the findings into a plan file
   under `docs/plans/`.
3. Present me the plan summary and **STOP. Do not implement until I give
   explicit approval.** If I request changes, send them back through the
   planner.
4. After approval, implement the plan yourself step by step, running the
   affected service's build and test commands (CLAUDE.md → Commands) after
   each step. Fix failures before moving on.
5. Finish with: the full test suite for the affected service(s) passing,
   the completed checklist item(s) ticked in the service README, and a
   summary of what changed versus the plan. Propose a conventional commit
   message — do not push.
