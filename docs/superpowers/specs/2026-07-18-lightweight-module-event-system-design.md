# Lightweight Module Repository and Event System Repair Design

Date: 2026-07-18

## Goal

Repair the lifecycle and dispatch defects in the current lightweight global module repository and event system without expanding `IocRepository` into a full dependency-injection container.

The public usage style remains intentionally small and global:

```csharp
IocRepository.Get<T>();
IocRepository.Register(instance);
EventModule.Subscribe<T>(callback);
EventModule.Unsubscribe<T>(callback);
data.Fire();
data.FireDelay(EventFireDelay.Update);
```

Interface mappings, constructor injection, scopes, and factories are outside this change.

## Module Repository

`IocRepository` remains the single global module repository. Each concrete module type has one generic slot for fast lookup, while the repository keeps ordered registration records so that all slots can be reset deterministically.

- `Get<T>()` returns the existing module or creates, registers, and returns one with `new T()`.
- `TryGet<T>(out T module)` performs a lookup without creating a module.
- `Register<T>(instance)` rejects `null` and throws `InvalidOperationException` if that exact module type is already registered. Failed registration does not mutate repository state.
- Newly registered update modules enter a pending collection. Pending modules are activated only at the beginning of the next `IocRepository.Update()` call.
- A module created during Update or LateUpdate therefore begins with Update on the following frame and cannot receive LateUpdate before its first Update.
- Update and LateUpdate execution order follows module registration order.
- An exception from one update module is logged with `Debug.LogException` and does not prevent other modules from running.
- Dispose runs modules in reverse registration order. Exceptions are logged and do not prevent remaining modules from being disposed.
- Cleanup of module collections and generic slots always runs, including when module disposal fails.
- Dispose is idempotent. After it completes, a subsequent `Get<T>()` may build a fresh repository state.
- Repository creation, registration, and ticking while disposal is actively in progress throw `InvalidOperationException`. Read-only `TryGet` remains available so module teardown can perform idempotent cleanup.

## Event System Ownership

`EventModule` becomes the actual owner of subscriptions and delayed events. Static methods remain only as a compatibility facade that forwards to the current `EventModule` instance.

Each event type uses an instance-owned `EventChannel<T>`. Channels are stored by event type inside EventModule and are removed when EventModule is disposed. No generic static subscriber remains.

Subscription rules:

- A null callback throws `ArgumentNullException`.
- Subscribing the same callback twice throws `InvalidOperationException`.
- Unsubscribing a callback that is not registered is a no-op.
- `Unsubscribe<T>` uses `IocRepository.TryGet` so teardown code cannot recreate EventModule merely to remove a subscription.
- Dispatch iterates a stable active-subscriber list in subscription order. Subscription changes made anywhere in a synchronous nested dispatch chain are deferred until the outermost dispatch completes.
- Each callback is invoked in an independent try/catch. Exceptions are logged through `Debug.LogException`, and remaining callbacks continue.
- Firing an event without subscribers is valid and has no effect.

### Subscription Mutation Strategy

Each channel owns a reusable active-subscriber list, a reusable pending-change list, and a dispatch-depth counter. Subscribe and unsubscribe modify the active list directly when no event is being dispatched. During dispatch they append a compact add/remove operation to the pending list instead, leaving the active list stable for indexed iteration without allocating a snapshot.

Nested immediate `Fire` calls increment the same depth counter and continue to observe the unchanged active list. When the outermost `Fire` exits, its `finally` path applies pending operations in call order. A callback removed during the chain therefore continues to receive both the current event and any synchronously nested events, but does not receive a later top-level event.

Duplicate-subscription checks evaluate both the active list and queued changes so they continue to fail synchronously. Removing an effectively absent callback remains a no-op. Clearing a channel during dispatch is likewise deferred until the outermost dispatch completes.

The lists retain their capacity. After normal warm-up, subscription mutations allocate only when a list must grow rather than allocating and copying a new subscriber array for every change. This deliberately trades exact C# multicast-delegate behavior during rare nested immediate dispatch for lower allocation pressure under dynamic Unity object lifecycles.

## Delayed Event Queues

EventModule owns two independent FIFO queues: one for Update and one for LateUpdate. Every delayed event is represented by a small type-safe envelope containing the event data and its channel. This favors clear behavior over premature pooling.

At the start of a phase, EventModule records the queue count and consumes exactly that many entries:

- Global enqueue order is preserved across different event types.
- Update and LateUpdate events cannot consume one another's data.
- An event enqueued into the phase currently being drained waits until that phase on the next frame.
- An event enqueued for LateUpdate during Update is eligible for LateUpdate in the same frame.
- An event enqueued for Update during LateUpdate waits for the next frame.

Pooling delayed-event envelopes is deferred until profiling demonstrates material allocation pressure.

## Unity Driver Lifecycle

`IocDriver` is the unique game-lifetime driver.

- The first instance becomes primary during Awake and calls `DontDestroyOnLoad`.
- A duplicate destroys only its own GameObject and never disposes the global repository.
- The primary instance drives repository Update and LateUpdate.
- Application quit or final destruction of the primary instance disposes the repository exactly once.
- A `RuntimeInitializeOnLoadMethod` using `SubsystemRegistration` resets driver static state and cleans repository state, including when Enter Play Mode has Domain Reload disabled.

The scene remains responsible for containing the initial driver. Automatic creation is outside this change.

## File Changes

- Refactor `IocRepository.cs` around ordered registration entries, generic slots, pending activation, safe update dispatch, and full reset.
- Replace static `EventEmitter<T>` behavior with an instance-owned `EventChannel<T>` implementation.
- Replace the shared `EventDataHolder<T>` delayed buffering model with per-event FIFO envelopes.
- Remove obsolete `IEventDataHolder` code.
- Refactor `EventModule.cs`, `EventExtensions.cs`, and `IocDriver.cs` to use the new ownership and lifecycle.
- Update the example behavior so phase separation is observable.
- Add runtime and EditMode test assembly definitions and automated tests.

## Verification

Automated tests cover:

- automatic creation, explicit registration, null registration, and duplicate registration;
- pending activation and stable Update/LateUpdate order;
- update exception isolation;
- reverse disposal, disposal exception isolation, idempotence, and recreation;
- safe no-subscriber dispatch and duplicate-subscription rejection;
- stable dispatch-chain behavior, ordered deferred subscription changes, and callback exception isolation;
- global FIFO ordering across event types;
- Update/LateUpdate separation;
- same-phase reentrant enqueue deferral;
- Update-to-LateUpdate same-frame enqueue;
- complete EventModule queue and subscription cleanup.

Validation runs the Unity EditMode tests and compiles `Assembly-CSharp.csproj`. Environment-specific Unity dependency-version warnings are reported separately from defects introduced by this change.
