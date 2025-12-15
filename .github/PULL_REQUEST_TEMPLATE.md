<!--
`durabletask-dotnet` Pull Request Template

Fill in all sections. If a section does not apply, write "N/A".
PRs that change runtime behavior must include manual validation steps and results.
-->

# Summary

## What changed?
<!-- 1 to 5 sentences. What does this PR do? -->

## Why is this change needed?
<!-- Link to bug/feature request and describe the problem being solved. -->

## Related issues / work items
- Fixes #
- Related #

---

# Type of change
- [ ] Bug fix
- [ ] New feature
- [ ] Performance improvement
- [ ] Reliability / resiliency improvement
- [ ] Refactor (no behavior change intended)
- [ ] Test-only change
- [ ] Build / CI change
- [ ] Documentation-only change

---

# AI-assisted code disclosure (required)

## Was an AI tool used? (select one)
- [ ] No, this PR was written without AI assistance
- [ ] Yes, AI helped write parts of this PR (e.g., GitHub Copilot)
- [ ] Yes, an AI agent generated most of this PR

## If AI was used, complete the following
- Tool(s) used:
- Which files / areas were AI-assisted:
- What you changed after AI generation (review, refactor, bug fixes):

### AI verification checklist (required if AI was used)
- [ ] I understand the code in this PR and can explain it
- [ ] I verified all referenced APIs/types exist and are correct
- [ ] I reviewed edge cases and failure paths (timeouts, retries, cancellation, exceptions)
- [ ] I reviewed concurrency/async behavior (no deadlocks, no blocking waits, correct cancellation tokens)
- [ ] I checked for unintended breaking changes or behavior changes

---

# Testing

## Automated tests
### What did you run?
<!-- Examples: `dotnet test`, specific test projects, filters -->
- Command(s):

### Results
- [ ] Passed
- [ ] Failed (explain and link logs)

### Tests added/updated in this PR
<!-- Briefly list new or updated tests, or write N/A -->
- 

---

## Manual validation (required for runtime/behavior changes)
> If this is docs-only or test-only, explain why manual validation is N/A.

### Environment
- OS:
- .NET SDK/runtime version:
- DurableTask component(s) affected (client/worker/backend/etc.):

### Scenarios executed (check all that apply)
- [ ] Orchestration start, completion, and replay behavior
- [ ] Activity execution (including retries)
- [ ] Failure handling (exceptions, poison messages, transient failures)
- [ ] Cancellation and termination flows
- [ ] Timers and long-running orchestration behavior
- [ ] Concurrency / scale behavior (multiple instances, parallel activities)
- [ ] Backward compatibility check (old history / upgraded worker) if applicable
- [ ] Other (describe):

### Steps and observed results (required)
<!-- Provide exact steps so a reviewer can reproduce.
Include relevant logs/trace snippets or describe what you observed. -->
1.
2.
3.

Evidence (logs, screenshots, traces, links):
- 

---

# Compatibility / Breaking changes

- [ ] No breaking changes
- [ ] Breaking changes (describe below)

If breaking:
- Impacted APIs/behavior:
- Migration guidance:
- Versioning considerations:

---

# Review checklist (author)

- [ ] Code builds locally
- [ ] No unnecessary refactors or unrelated formatting changes
- [ ] Public API changes are justified and documented (XML docs / README / samples as appropriate)
- [ ] Logging is useful and not noisy (no secrets, no PII)
- [ ] Error handling follows existing DurableTask patterns
- [ ] Performance impact considered (hot paths, allocations, I/O)
- [ ] Security considerations reviewed (input validation, secrets, injection, SSRF, etc.)

---

# Notes for reviewers
<!-- Anything that helps reviewers: design notes, tradeoffs, follow-ups, risky areas -->
-
