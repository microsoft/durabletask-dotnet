---
applyTo: "src/Analyzers/**,src/Generators/**"
---
# Roslyn Analyzers and Source Generators

## Runtime vs. Compile-Time Boundary

Code in `src/Analyzers/` runs at **compile time only** inside the compiler process. Code in `src/Generators/` runs at **compile time only** to emit new C# source files.

- Do not reference any NuGet package that has a runtime dependency (e.g., `Microsoft.Extensions.*`, gRPC libs). Analyzer/generator projects may only reference `Microsoft.CodeAnalysis.*` packages.
- Do not use `System.Reflection` APIs at runtime — use Roslyn symbol APIs (`INamedTypeSymbol`, `IMethodSymbol`, etc.) instead.
- Generator output must reproduce the same source given the same input — generators must be **deterministic and idempotent**.

## Interface Consistency Constraint

`TaskActivityContext` (in `src/Abstractions/`) is implemented by the source generator output. If you change any `abstract` member on `TaskActivityContext` or `TaskOrchestrationContext`, you must also update the generator templates in `src/Generators/` to match. The `// IMPORTANT` comments in those files mark the exact coupling points.

## Testing Analyzers

- Analyzer tests use `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing` — do not use xUnit directly without that infrastructure.
- Each analyzer test must provide the exact source snippet that triggers the diagnostic and the expected `DiagnosticResult` with descriptor ID, location, and message.
- Verify both the diagnostic fires on bad code and does NOT fire on correct code (false positive check).

## Testing Generators

- Generator tests use `Microsoft.CodeAnalysis.CSharp.SourceGenerators.Testing` — use `GeneratorDriver` or the test helper wrappers in `test/Generators.Tests/`.
- Compare generated output with explicit expected source strings (inline in the tests or via shared helpers). When changing generator templates, update all affected expected outputs or helper expectations.
- Do not emit `#pragma warning disable` in generated output without explicit justification.
