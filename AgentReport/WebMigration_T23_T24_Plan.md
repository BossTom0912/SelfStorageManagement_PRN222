# WebMigration T23–T24 — close outstanding reviews and release

Date: 2026-10-19 (Asia/Bangkok). Implementation workspace: `D:\FPT\PRN222\Project\Final_Project_Web`. SHA-256 comparison copy: `D:\FPT\PRN222\Project\Final_Project`. Base: `main` / `origin/main` at `1201742ded6d338a00043cd08533392fe4c1a6c7`.

## Why these tasks exist

T22 is VERIFIED, but TaskBoard still lists T01 and T08 as IMPLEMENTED awaiting independent review. Later verified tasks and a passing regression suite do not by themselves change those historical statuses. This plan closes that review debt without rewriting the original T01/T08 reports or assuming their findings are resolved.

## T23 — reconcile T01 and T08 acceptance

- Owner: Gemini A prepares a current-snapshot evidence report and any scoped fixes; Codex performs independent review using `C:\Users\tommy\.codex\skills\.system\review-agent\SKILL.md`.
- Dependencies: T00, T05–T07, T22 VERIFIED; T01 and T08 reports available.
- Entry status: READY. T01 and T08 remain IMPLEMENTED until Codex accepts their current behavior and evidence.
- Gemini reads `AgentReport/WebMigration_T01.md`, `AgentReport/WebMigration_T08.md`, `AgentReport/WebMigration_TaskBoard.md`, `AgentReport/WebMigration_T22_Memory.md`, `AgentReport/WebMigration_TaskMemoryGuide.md`, and the applicable contract and tests. It traces both previously requested T01 corrections (HMAC configuration documentation and the exact eight Admin methods) and T08-F01 through F04 through current code, test fixtures, and call sites. It reports which requirements remain valid after later tasks, with exact paths and hashes. It repairs only demonstrated defects, runs focused tests, and synchronizes only scoped files and evidence to the comparison copy.
- Deliverable: `AgentReport/WebMigration_T23_BacklogClosure.md` with report SHA-256, changed-file hashes in both workspaces, exact commands/pass/fail/skip totals, coverage limits, and a proposed T01/T08 disposition. Gemini leaves T23 IMPLEMENTED and does not commit, push, merge, or self-mark T01/T08 VERIFIED.
- Codex reviews each final report snapshot once by path and SHA-256, inspects the full scoped diff, relevant code and call sites, independently tests meaningful behavior, and writes one scoped Gemini fix prompt per review round when findings exist. Once No findings, Codex records the review, marks T01, T08, and T23 VERIFIED, writes `AgentReport/WebMigration_T23_Memory.md`, and synchronizes the evidence.

## T24 — independent final gate and Git release

- Owner: Codex. Dependency: T23 VERIFIED with T01 and T08 VERIFIED.
- Entry status: WAITING.
- Recheck architecture, cookie/SignalR/Origin contracts, T01/T08 corrections, and all scoped changes. Run the full Web solution (including browser tests), legacy default solution, and the 19 SQL Server concurrency tests against the isolated WebTest database; require zero failures and zero unexpected skips. Check the six business triggers remain enabled and test fixtures are cleaned up. Avoid concurrent DB/browser runs with Gemini, and restore only regenerated T18 PNGs from verified R6 evidence.
- Record exact commands and totals in `AgentReport/WebMigration_T24_FinalReview.md`. If a required environment is unavailable or a test fails, do not merge; diagnose or report the concrete blocker. Review code using the review-agent criteria and resolve all actionable findings before release.
- After accepted review and SHA-256 parity, write `AgentReport/WebMigration_T24_Memory.md`, update TaskBoard, create a fresh `codex/` branch, stage only T23/T24 scoped paths, commit, push the branch, merge into `main`, and push `main`. Record the Git references in task memory and synchronize the final documents.

## Shared constraints

- Preserve `/storageUnitHub`, its three `Hub*StorageUnit` methods and three `Receiver_*StorageUnit` events, the five-minute cookie lifetime, and strict RFC 6454 Origin validation.
- Leave `docs/KeHoach_Web_Gemini_Va_Codex_Review.docx`, `AgentReport/WebMigration_T15_CodexLoopState.json`, and `scratch/generate_r3_report.js` outside commits. Do not stage unrelated T16–T18 line-ending changes.
- Use new Gemini and Codex conversations for this cycle. Gemini receives a `/goal` instruction in the new `Final_Project_Web` project conversation; Codex independently reviews Gemini's completed work and controls the Git release.
