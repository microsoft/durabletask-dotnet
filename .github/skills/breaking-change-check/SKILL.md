---
name: breaking-change-check
description: Use when adding, removing, or changing any public API surface in this repo (method signatures, class members, interface members, enum values, constructor parameters, or serialization behavior). Guides a systematic backward-compatibility check before committing.
---

# Breaking Change Safety Check

## When to Use

Invoke this skill whenever you are about to:
- Remove or rename a `public` or `protected` method, property, class, or interface
- Change a method signature (parameter types, return type, parameter count, optionality)
- Change a public `enum` value or add a new value to a `[Flags]` enum
- Change a default value that affects serialization or wire behavior
- Modify `TaskOrchestrationContext`, `TaskActivityContext`, `DurableTaskClientOptions`, or `DurableTaskWorkerOptions`

## Step 1 — Identify the Changed API Surface

Read the file containing the change. List every `public` or `protected` member being modified.

For each, determine: is this member shipped in a `Microsoft.DurableTask.*` NuGet package?
To decide this, inspect `src/<area>/<area>.csproj` and our build/CI configuration: look for NuGet packaging metadata (such as `<PackageId>`, `IsPackable`, `GeneratePackageOnBuild`, or inclusion in a packing target or release artifact). If the project is packed into a `Microsoft.DurableTask.*` NuGet, assume customers may depend on it.

## Step 2 — Verify All Callers Inside This Repo

For each changed member, search the full solution before modifying the signature:

```bash
grep -rn "MemberName" --include="*.cs" .
```

Also search `test/` and `samples/` — these are first-party consumers that must be updated alongside the change.

## Step 3 — Check Orchestration Replay Safety

If the changed API is callable from inside an orchestrator (anything reachable from `TaskOrchestrationContext`):
- Would the new behavior produce a different result when replaying existing orchestration history?
- If yes: the change is NOT backward-compatible. It requires a new orchestration version (via `TaskVersion`) or an explicit feature flag. Do not proceed without documenting this.

## Step 4 — Check Serialization Compatibility

If the change affects a type serialized to/from the gRPC wire format or the JSON data converter:
- Adding new optional properties with sensible defaults is generally backward-compatible.
- Removing properties, renaming them, or changing their serialized representation is breaking.
- Inspect `src/Abstractions/Converters/JsonDataConverter.cs` for any serializer configuration that applies.

## Step 5 — Classify the Breaking Change

Reference: https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/breaking-change-rules.md

Classify as one of:
- **Binary breaking** — existing compiled assemblies will fail to load against the new version
- **Source breaking** — existing source code will fail to compile against the new version
- **Behavioral breaking** — existing code compiles and loads but produces different results

Binary and source breaking changes require explicit maintainer approval (noted in PR summary or linked issue).
Behavioral breaking changes on serialization or wire paths require a documented migration plan.

## Step 6 — Document in the PR

If the change is breaking (any category), add this block to the PR description:

```
## Breaking Change
Type: [binary | source | behavioral]
Impact: [what breaks and who is affected]
Migration: [what callers must do to upgrade to this version]
```

If not breaking, state explicitly: "No breaking change — [reason]."

## Common Mistakes in This Repo

- Changing a `DataConverter` default in `DurableTaskWorkerOptions` without noting it affects in-flight orchestrations
- Modifying the `TaskActivityContext` abstract interface without updating the source generator output in `src/Generators/`
- Adding a required constructor parameter to a type registered via DI (breaks existing DI registrations)
- Changing a proto field number or removing a proto field (breaks wire compatibility — field numbers are permanent)
