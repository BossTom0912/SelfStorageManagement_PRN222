# WebMigration task memory rule

When a task is completed and independently VERIFIED, create `AgentReport/WebMigration_<TaskId>_Memory.md` before starting the next task. Update the TaskBoard with its path. Sync the memory to both workspaces and compare SHA-256. Carry the memory path into every new Codex and Gemini chat for the following task.

Each task memory records:

1. Task ID, owner, scope, dependencies, final TaskBoard status and the exact report path plus SHA-256.
2. Changed source and schema paths, the behavior or contract they establish, and the final independent review path plus SHA-256.
3. Exact test commands and observed pass/fail/skip totals; distinguish Gemini's results from Codex's independent runs.
4. Evidence paths and hashes, DB or environment state that matters, and any remaining limitations or conditional next steps.
5. Git branch, commit and merge references once available, plus files intentionally kept outside the commit.
6. The next task's entry conditions, owner, chat handoff links and any shared-file coordination rules.

Treat each completed task as a new snapshot. Do not rewrite an earlier memory to make a later state appear known at an earlier point; add an update or the next task's memory. Never mark a task VERIFIED from a Gemini self-report alone. Preserve the T12–T15 realtime Hub/Receiver contract and keep task dependencies in TaskBoard authoritative.
