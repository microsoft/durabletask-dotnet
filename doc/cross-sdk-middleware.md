# Cross-SDK Durable Task Middleware

> **Status:** Draft canonical cross-SDK guidance. The .NET v1 implementation in
> `Microsoft.DurableTask.Worker.Middleware` is the reference implementation for this slice; other SDKs should use idiomatic names and shapes while preserving the contracts below.

## Background and goals

Durable Task applications need a host-agnostic way to run cross-cutting logic around durable task execution. The immediate motivation was Azure Functions Durable Extension issue #3054, where Durable Functions users needed durable-task-aware access to host invocation context. The generalized requirement is broader: standalone Durable Task workers and hosted Durable Functions workers should share one SDK-level middleware model instead of relying on Azure Functions worker middleware.

Goals:

- Define SDK-level durable middleware for orchestration and activity execution.
- Keep the model host-agnostic so it works for standalone workers and hosted environments such as Azure Functions.
- Preserve orchestration replay determinism and existing durable task history semantics.
- Provide typed context and feature access for host-specific data without changing the wire protocol.
- Let each language SDK expose idiomatic APIs while preserving common behavior, ordering, and safety rules.

## Target use cases

Middleware should support these cross-cutting scenarios without requiring every orchestrator or activity to duplicate the logic:

| Use case | Orchestration middleware | Activity middleware |
|---|---|---|
| Tenant scoping and authorization decisions from task input, tags, or host context | Allowed only when decisions are deterministic. Dynamic policy lookups belong in activities or host code. Rejection should fail deterministically, not return an alternate success result. | Can use input, tags or host context, and injected services to authorize before user activity code runs. |
| Input validation and rejection before user code | Can validate deterministic input and fail by throwing a deterministic exception. A successful orchestration invocation must still call the next delegate exactly once. | Can throw validation errors, or use the explicit result API only when returning a valid activity result without invoking user code. |
| Correlation IDs and trace metadata propagation | Can read deterministic tags, instance metadata, and typed host features. Logging must be replay-safe. | Can read/write trace metadata using tags, host context, services, and activity context. |
| Replay-safe structured logging and audit event emission | Use `IsReplaying` or an SDK-provided replay-safe logger equivalent to avoid duplicate logs and audits during replay. | Log and emit audit events normally, subject to host logging policy. |
| Activity metrics and latency/error observation | Can observe scheduled activity results only through orchestrator-visible effects, not by performing I/O. | Can measure latency, record success/failure metrics, and wrap errors around the activity body. |
| Activity result normalization, redaction, or cached results | May observe orchestration result after the next delegate returns; cannot replace it in v1. | Can observe and replace/redact the activity result after `next`, or short-circuit with a cached result through the explicit result API. |
| Cross-cutting test and instrumentation hooks for standalone apps | Can capture deterministic execution metadata for test assertions. | Can capture invocation metadata, dependency usage, latency, and failures for test assertions. |
| Feature-flag or version-aware behavior by task name, version, or tags | Allowed when flags are deterministic for the instance, for example from tags, input, or immutable deployment configuration. | Can use injected feature-management services or host context. |

## Conceptual model

Middleware is an asynchronous inbound pipeline around durable task execution:

```text
registered middleware 1
  -> registered middleware 2
    -> ...
      -> orchestrator or activity body
    <- ...
<- registered middleware 1
```

Each middleware receives a typed context and a `next` delegate. The first registered middleware is the outermost middleware. It runs first before user code and last after user code.

The model is similar to ASP.NET Core middleware and Temporal inbound interceptors, but durable replay changes the orchestration rules: orchestration middleware participates in replay and must obey the same deterministic execution contract as orchestrator code.

## Scope and non-goals

In scope for v1:

- Orchestration middleware.
- Activity middleware.
- Per-worker or per-builder registration.
- Type-based and delegate/function-based middleware registration where idiomatic.
- Typed feature collection for host-specific objects, such as a Functions `FunctionContext` equivalent.

Out of scope for v1:

- Entity middleware. Entity middleware is deferred to v2.
- Wire protocol or protobuf changes.
- SDK-specific host worker middleware, such as Azure Functions worker middleware, as the durable middleware abstraction itself.
- A separate PowerShell target. PowerShell inherits .NET behavior where applicable.
- A guarantee that all languages expose identical names or type hierarchies.

## Required context model

Each SDK should expose context objects that include the fields below, using idiomatic names and types. Fields may be unavailable on older backends; SDKs should document null/default behavior and preserve backward compatibility.

### Orchestration middleware context

| Field | Meaning |
|---|---|
| `name` | Logical orchestration name. |
| `instanceId` | Current orchestration instance ID. |
| `version` | Orchestration version when supplied by the client or backend; otherwise empty/null/default. |
| `parent` | Parent orchestration name and instance ID, or null/none. |
| `tags` | Read-only orchestration tags when available. Tags must be copied or otherwise protected from mutation by later host changes. |
| `isReplaying` | Whether the current invocation is replaying prior history. |
| `inputType` | Declared input type when the language has runtime type metadata. |
| `input` | Deserialized input value. |
| `rawInput` | Raw serialized input when available. |
| `orchestrationContext` | The regular orchestrator context object used by user orchestrator code. |
| `features` | Typed feature collection for host-specific per-work-item objects. |
| `cancellationToken` / cancellation signal | Cancellation signal for stopping execution when the SDK supports one. |
| `result` | Orchestration result after `next` returns; unset before then. |

The orchestration context must not expose an ambient service provider or general dependency injection accessor. Any host-specific object exposed through `features` remains non-durable and must be used only in deterministic, replay-safe ways.

### Activity middleware context

| Field | Meaning |
|---|---|
| `name` | Logical activity name. |
| `instanceId` | Orchestration instance ID that scheduled the activity. |
| `inputType` | Declared input type when available. |
| `input` | Deserialized input value. |
| `rawInput` | Raw serialized input when available. |
| `activityContext` | The regular activity context object used by user activity code. |
| `features` | Typed feature collection for host-specific per-work-item objects. |
| `services` / dependency accessor | Invocation-scoped services when the language or host supports dependency injection. |
| `cancellationToken` / cancellation signal | Cancellation signal for activity execution when supported. |
| `result` | Activity result after `next` returns or after the middleware explicitly sets a result. |
| `setResult(result)` equivalent | Explicit API to set the activity result and skip invoking user activity code. |

### Typed feature collection

SDKs should provide a feature collection with type-keyed get/set semantics, or the closest language equivalent. Features are:

- Scoped to a single orchestration or activity work item.
- Not serialized into durable history.
- Intended for host-specific objects and integration data.
- Safe to omit when a host has no object to provide.

## Pipeline behavior and registration

- Registration is scoped to a worker, task hub worker, or builder instance. Multiple named workers must have isolated middleware lists.
- Middleware executes in registration order, outer-to-inner. On the way out, control unwinds in reverse order.
- Type/class middleware should be resolved from an invocation scope where the language has scoped dependency injection. The .NET reference registers middleware types as scoped services unless already registered.
- Delegate/function middleware instances may be reused across invocations. They must not capture unsynchronized mutable state.
- Orchestration middleware that completes successfully must call `next(context)` exactly once. SDKs should detect missing or duplicate calls where feasible.
- Activity middleware may call `next(context)` once, or may skip it by using the explicit `setResult`-like API. Calling `next` more than once should be rejected or documented as invalid.
- Exceptions thrown by middleware propagate through the durable task failure path in the same way as exceptions from user orchestrator or activity code.

## Orchestration determinism contract and guard expectations

Orchestration middleware runs on the orchestrator execution path and is replayed from history. It must follow the same deterministic rules as orchestrators:

- Do not call nondeterministic APIs directly, such as wall-clock time, random number generation, new GUID generation, process/environment state, network I/O, file I/O, or thread scheduling primitives.
- Use durable context APIs for deterministic time, GUIDs, timers, sub-orchestrations, activities, and external events.
- Do not perform outbound I/O from orchestration middleware. Move I/O to activities, host code, or activity middleware.
- Do not depend on mutable global state, current process state, or host objects that may differ across replay.
- Use `isReplaying` or replay-safe logging helpers to suppress duplicate logs and audit events.
- If validation or authorization rejects an orchestration, throw a deterministic exception. Do not return a synthetic success result in v1.

Short-circuiting orchestration middleware is disallowed in v1 because skipping user orchestrator code can create history divergence, hide scheduled durable operations, and produce results that were never recorded through normal orchestrator execution. A successful orchestration middleware invocation must therefore call `next` exactly once and may observe, but not replace, the result.

SDKs should protect orchestration middleware and orchestrator execution from illegal non-durable awaits where technically feasible. For example, if a runtime can detect an awaited non-durable promise/task/future before, during, or after middleware invokes `next`, it should fail the orchestration with a clear diagnostic.

## Host integration pattern

Durable middleware belongs in the Durable Task SDK layer. Hosts integrate by adding host objects to the middleware feature collection before the durable SDK invokes middleware.

### Azure Functions-style host

A Functions worker can attach its language-equivalent of `FunctionContext` to `features` for the current durable work item. Durable middleware can then read that feature without depending on Functions worker middleware APIs. Activity middleware may also use host services and bindings when the host exposes them. Orchestration middleware must still treat host objects as non-durable and replay-sensitive.

### Standalone worker

Standalone Durable Task apps can add their own feature objects, such as test recorders, correlation metadata, tenant descriptors, or custom service handles. No Functions dependency is required.

This pattern keeps the durable middleware API stable while allowing hosts to layer language- and platform-specific integration points on top.

## Language-specific API sketches and tracking issue content

The following sketches are not final API names. They describe the expected issue scope for each SDK.

### JavaScript / TypeScript (`microsoft/durabletask-js`)

Proposed shape:

```ts
worker.useOrchestrationMiddleware(async (ctx, next) => {
  if (!ctx.isReplaying) {
    ctx.logger?.info("starting orchestration", { name: ctx.name, instanceId: ctx.instanceId });
  }

  await next(ctx);
});

worker.useActivityMiddleware(async (ctx, next) => {
  const cached = await cache.tryGet(ctx.name, ctx.input);
  if (cached !== undefined) {
    ctx.setResult(cached);
    return;
  }

  await next(ctx);
});
```

Tracking issue should cover:

- Worker-level registration APIs for orchestration and activity middleware.
- Context fields matching this spec, with TypeScript interfaces.
- A typed feature map or symbol-keyed feature API for host context.
- Replay-safe logging guidance and illegal non-durable promise/await guard expectations.
- No entity middleware in the initial issue.

### Python (`microsoft/durabletask-python`)

Proposed shape:

```python
@app.orchestration_middleware
async def orchestration_middleware(ctx, next):
    if not ctx.is_replaying:
        ctx.logger.info("starting orchestration", extra={"instance_id": ctx.instance_id})

    await next(ctx)

@app.activity_middleware
async def activity_middleware(ctx, next):
    cached = await cache.try_get(ctx.name, ctx.input)
    if cached is not None:
        ctx.set_result(cached)
        return

    await next(ctx)
```

Tracking issue should cover:

- Decorator or builder registration that is per worker/app instance.
- Dataclass/protocol-style context definitions for orchestration and activity middleware.
- Feature collection using type keys or well-known keys when Python typing cannot enforce runtime type identity.
- Determinism guidance for `datetime`, `uuid`, `random`, file/network I/O, and `asyncio` awaits in orchestration middleware.
- No entity middleware in v1.

### Java (`microsoft/durabletask-java`)

Proposed shape:

```java
workerBuilder.useOrchestrationMiddleware((ctx, next) -> {
    if (!ctx.isReplaying()) {
        logger.info("starting orchestration {}", ctx.getInstanceId());
    }

    return next.invoke(ctx);
});

workerBuilder.useActivityMiddleware((ctx, next) -> {
    Optional<Object> cached = cache.tryGet(ctx.getName(), ctx.getInput());
    if (cached.isPresent()) {
        ctx.setResult(cached.get());
        return CompletableFuture.completedFuture(null);
    }

    return next.invoke(ctx);
});
```

Tracking issue should cover:

- Builder-level middleware registration and ordering.
- Context interfaces with Java naming conventions and nullable/optional behavior.
- Feature collection keyed by `Class<T>`.
- Integration with dependency injection frameworks where present, without requiring one.
- Replay-safe logging and guard expectations for non-durable `CompletableFuture`/threading usage.

### Go (`microsoft/durabletask-go`)

Proposed shape:

```go
worker.UseOrchestrationMiddleware(func(ctx OrchestrationMiddlewareContext, next OrchestrationMiddlewareNext) error {
    if !ctx.IsReplaying() {
        logger.Info("starting orchestration", "instanceId", ctx.InstanceID())
    }

    return next(ctx)
})

worker.UseActivityMiddleware(func(ctx ActivityMiddlewareContext, next ActivityMiddlewareNext) error {
    if value, ok := cache.TryGet(ctx.Name(), ctx.Input()); ok {
        ctx.SetResult(value)
        return nil
    }

    return next(ctx)
})
```

Tracking issue should cover:

- Worker options or registration functions for orchestration and activity middleware.
- Context interfaces using Go naming conventions.
- Feature collection keyed by typed handles, interface types, or comparable keys with type-safe helpers.
- Determinism guidance for `time.Now`, random values, goroutines, channels, file/network I/O, and nondurable blocking in orchestration middleware.
- No entity middleware in v1.

## Acceptance criteria for SDK implementations

An SDK implementation satisfies this spec when:

- It exposes orchestration and activity middleware APIs at the durable worker/builder level.
- Middleware executes in registration order, outer-to-inner, and unwinds in reverse order.
- Orchestration middleware receives the required orchestration context fields and cannot access ambient services through the orchestration middleware context.
- Activity middleware receives the required activity context fields and has an explicit result-setting API that can skip user activity code.
- Host-specific objects can be passed through a typed feature collection without serialization into history.
- Orchestration middleware that completes successfully must call `next` exactly once; missing and duplicate calls are guarded where feasible.
- Activity middleware can short-circuit only through the explicit result API; duplicate `next` invocation is invalid.
- Orchestration determinism rules are documented, including replay-safe logging guidance.
- Illegal non-durable awaits in orchestration middleware and orchestrator execution are detected where the runtime can reasonably detect them.
- No protobuf or wire protocol changes are required.
- Entity middleware is not exposed as part of v1.
- Tests cover context population, registration ordering, activity short-circuiting, orchestration next-call validation, feature access, and host integration.

## Open follow-ups

- Entity middleware design for v2, including entity-specific context fields and deterministic constraints.
- Static analyzers or lint rules that detect nondeterministic APIs inside orchestration middleware.
- Final language-specific naming, packaging, and overload decisions.
- Replay-safe logging helper standardization across SDKs.
- Additional host integration samples for Azure Functions and standalone workers.
- Guidance for feature-flag snapshots that remain stable for the lifetime of an orchestration instance.
