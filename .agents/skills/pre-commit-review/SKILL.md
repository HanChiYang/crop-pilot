---
name: pre-commit-review
description: Review the exact changes intended for a Git commit and prepare a summary for approval. Use when asked to prepare a commit, review staged changes, or do a final code review before committing.
---

# Pre-commit review

1. Read the applicable project rules. Inspect Git status, the index, and the working tree. Identify the intended paths and comparison base. Use the staged diff against HEAD when it matches the intended scope. If reviewing before staging, account for intended untracked files and keep unrelated changes out of the review. Resolve unclear scope before presenting a commit for approval.
2. Match the review and verification to the change. For wording, documentation, or local test data, check the diff and scope. For behavior changes, inspect affected callers, edge cases, and likely regressions. Use existing verification when it covers the final diff. For authentication, permissions, migrations, and behavior that crosses modules, use an independent pass when authorized or required by project rules.
3. Start independent reviewers with fresh context and a read-only assignment. Provide the intended diff scope, comparison base, applicable requirements, and project rules. Let the reviewer inspect the code directly. Withhold the main conversation, the implementer's reasoning, and earlier findings. Ask for actionable issues with severity, file and line, and impact. If a required independent review cannot run, report that before presenting the commit for approval.
4. Give the user the proposed commit message, the exact scope, the verification performed, and any remaining findings or tradeoffs. State whether the reviewed changes are staged. If preparing a commit, capture the reviewed base and diff so they can be compared with the actual staged changes before executing it.
5. Follow the user's and repository's approval requirements. If the intended changes or comparison base change after review, review the new diff and obtain fresh approval where required. Treat commit, push, PR creation, and merge as separate actions with their own authorization scope. When project rules allow asking together, use one approval card with separate choices for commit, push, and PR creation once the review and proposed details are ready. Ask only about steps that are still pending and lack approval.
