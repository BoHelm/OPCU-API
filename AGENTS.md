# Repository Guidelines

## Commit & Pull Request Guidelines

Use German Conventional Commits, matching existing history:

Pull requests should describe the behavior change, affected device variants, build result, and any remaining warnings or hardware-test gaps. Link the relevant issue or requirement when available.

## Agent-Specific Instructions

Do not overwrite existing local work. This repository may contain dirty working-tree changes and generated artifacts. Stage only the files and hunks that belong to the requested change.

Keep existing comments if they still meet the criteria.

When an agent contributes code, name the agent as a co-author in the commit message, for example `Co-authored-by: Codex <codex@openai.com>`.

## Change Approval & Review

- Analyze existing code before proposing modifications.
- Do not create, modify, or delete files without explicit user approval.
- Present proposed changes with file names, affected functions, and technical reasoning before implementation.
- Keep changes minimal and focused on the requested task.
- Do not perform unrelated refactoring.
- Preserve existing behavior unless explicitly requested.
- Do not modify `AGENTS.md` without explicit approval.
- Do not execute builds, tests, or Git operations that modify repository state without approval.
- Never create commits without explicit user approval.
- Prefer step-by-step reviews for complex changes.
- The build instructions above apply only after the user approves execution.
