---
name: breaking-change-check
description: Use when adding, removing, or changing any public API surface in this repo (method signatures, class members, interface members, enum values, constructor parameters). Guides a systematic backward-compatibility check before committing.
---

# Breaking Change Safety Check

## When to Use

Invoke this skill whenever you are about to:
- Remove or rename a public method, property, class, or interface
- Change a method signature (parameter types, return type, parameter count)
- Change a public `enum` value or add a new one to a `[Flags]` enum
- Change a default value that affects serialization or wire behavior
- Modify `TaskOrchestrationContext`, `TaskActivityContext`, `DurableTaskClientOptions`, or `DurableTaskWorkerOptions`

## Step 1 — Identify the API Surface Being Changed

Read the file containing the change and list every `public` or `protected` member being modified.
For each, determine: is this member in a `Microsoft.DurableTask.*` NuGet package (shipped to customers)?
Check `src/<area>/<area>.csproj` — if it produces a NuGet package, the answer is yes.

## Step 2 — Search for All Callers in This Repo

For each changed member, search the entire solution:
```bash
grep -rn "MemberName" --include="*.cs" .
```
Also search `test/` and `samples/` — these are first-party consumers that must be updated.

## Step 3 — Check Orchestration Replay Safety

If the changed API is called inside orchestrator code (anything reachable from `TaskOrchestrationContext`):
- Would the changed behavior produce a different result when replaying existing history?
- If yes, the change is NOT backward-compatible — it requires a new orchestration version or a feature flag.

## Step 4 — Check Serialization Compatibility

If the change affects any type that is serialized to/from the gRPC wire format or JSON data converter:
- Adding new properties is generally safe if they are optional with sensible defaults.
- Removing properties, renaming them, or changing their serialized form is breaking.
- Check `JsonDataConverter.cs` for any serializer configuration that applies.

## Step 5 — Apply the Breaking Change Rules

Reference: https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/breaking-change-rules.md

Classify the change as:
- **Binary breaking** — existing compiled assemblies will fail to load
- **Source breaking** — existing source code will fail to compile against the new version
- **Behavioral breaking** — existing code compiles and loads but behaves differently

Binary and source breaking changes require explicit approval in the PR (noted in PR summary or linked issue).
Behavioral breaking changes on serialization/wire paths require a migration plan.

## Step 6 — Document in PR

If the change is breaking (any category), add to the PR description:
```
## Breaking Change
Type: [binary | source | behavioral]
Impact: [what breaks and who is affected]
Migration: [what callers must do to upgrade]
```

## Common Mistakes in This Repo

- Changing a `DataConverter` default in `DurableTaskWorkerOptions` without noting it breaks in-flight orchestrations
- Modifying the `TaskActivityContext` abstract interface without updating the source generator output
- Adding a required constructor parameter to a type that is registered via DI (breaks existing DI setups)
