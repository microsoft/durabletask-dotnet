# Copilot Code Review Instructions

These review-specific instructions supplement `.github/copilot-instructions.md`. For code review behavior, this file takes precedence where the two overlap.

## Scope

- **Only review lines that are part of the PR diff.** Do not comment on pre-existing code that was not modified by the PR. If a file shows in the diff only due to line-ending changes, whitespace reformatting, or import reordering, skip it entirely.
- **Limit comments to the changed code surface.** Refactoring suggestions (e.g., "combine nested `if`", "use ternary operator") on unchanged code are out of scope and should not be posted.

## Language Accuracy

- **Verify C# syntax claims before posting.** This repository uses `<LangVersion>latest</LangVersion>` on .NET 10 (currently C# 14). Do not flag valid modern C# syntax as errors. In particular:
  - Since C# 7.2, named arguments that are in their correct ordinal position may be followed by positional arguments. Do not flag this as a syntax error.
  - Pattern matching with `is not null`, `is { }`, and recursive patterns are valid.
  - File-scoped namespaces, primary constructors, and collection expressions are valid.
- **Do not flag code that compiles and passes CI.** If the PR's CI checks are green, do not claim that a line "will fail to compile" unless you can cite the specific compiler rule being violated.

## Comment Quality

- **Prioritize correctness bugs over style preferences.** Focus on:
  - Logic errors, off-by-one, null dereference, race conditions
  - Breaking API changes (per [breaking-change-rules](https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/breaking-change-rules.md))
  - Security issues (injection, SSRF, credential exposure)
  - Missing error handling on public API boundaries
- **Avoid low-signal style comments** such as:
  - Suggesting ternary operators for existing `if/else` blocks
  - Suggesting combining nested `if` statements
  - Recommending `sealed` on existing classes unless the class itself is newly introduced or its visibility/inheritance was changed in the PR
  - Spelling corrections on pre-existing comments not modified by the PR
- **Do not suggest changes that would themselves be breaking.** For example, do not suggest making a `virtual` method `abstract`, adding required parameters to existing public methods, or removing default parameter values on public APIs without understanding the backward-compatibility implications.

## Virtual Methods Are Intentional

- This SDK uses `virtual` methods with default implementations as a deliberate pattern to add new API surface without forcing existing subclasses to implement the new method. This is not a bug. Do not suggest converting these to `abstract` or throwing `NotSupportedException` in the default implementation unless the PR author has explicitly asked for that behavior.

## Nullability

- The repository has nullable reference types enabled. Override methods should match the base method's nullability annotations. When they differ, flag it — but verify the base declaration first before claiming a mismatch.

## Test Code

- Test helper classes and mock implementations in `test/` directories are internal to the test project. Apply the same `sealed` requirement from `copilot-instructions.md` only to new classes introduced in the PR.
- Do not suggest additional test cases for code paths that were not modified in the PR.

## XML Documentation

- XML doc comments on `virtual` methods document the **method contract** (what subclasses should implement), not the base class's default behavior. This is standard .NET practice. Do not flag a discrepancy between a `virtual` method's XML docs and its default `base` implementation body as a bug.

## Consolidation

The consolidation rules in `.github/copilot-instructions.md` (single-pass review, no re-posting resolved comments, respecting author justifications) apply here. They are not restated to avoid drift.
