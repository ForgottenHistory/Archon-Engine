# Scoped Event Subscriptions
**Date**: 2026-01-13
**Session**: 2
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Eliminate manual event subscription management in GameSystems

**Success Criteria:**
- Token-based subscriptions with IDisposable
- GameSystem base class helper for auto-cleanup
- Refactor existing systems to use new pattern

---

## Context & Background

**Previous Work:**
- See: [1-simple-command-infrastructure.md](1-simple-command-infrastructure.md)
- Identified event subscription lifecycle as friction point

**Current State:**
- Manual `EventBus.Subscribe` / `Unsubscribe` required
- Easy to forget unsubscription → memory leaks
- Boilerplate in every GameSystem's OnShutdown

---

## What We Did

### 1. Created Token Infrastructure

**Files Created in `Core/Events/`:**

| File | Purpose |
|------|---------|
| `SubscriptionToken.cs` | IDisposable tokens for explicit lifecycle |
| `CompositeDisposable.cs` | Groups disposables for batch cleanup |

### 2. Modified EventBus

Changed `Subscribe<T>()` to return `IDisposable` token:
```csharp
public IDisposable Subscribe<T>(Action<T> handler) where T : struct, IGameEvent
{
    // ... add listener ...
    return new SubscriptionTokenGeneric<T>(this, handler);
}
```

### 3. Added GameSystem Helper

Added `Subscribe<T>()` to GameSystem base class:
- Auto-tracks subscriptions in CompositeDisposable
- Auto-disposes on Shutdown()
- Two overloads: default EventBus and explicit EventBus

### 4. Refactored Game Systems

**Decision:** GameSystems should use EventBus struct events, not C# events.

TimeManager already emits BOTH:
- C# events: `OnMonthlyTick += handler` (old pattern)
- EventBus: `Emit(new MonthlyTickEvent{...})` (preferred)

| System | Before | After |
|--------|--------|-------|
| EconomySystem | `TimeManager.OnMonthlyTick +=` | `Subscribe<MonthlyTickEvent>()` |
| BuildingConstructionSystem | `TimeManager.OnMonthlyTick +=` | `Subscribe<MonthlyTickEvent>()` |

### 5. Updated StarterKit (Best Practice Showcase)

StarterKit systems updated to use `CompositeDisposable`:

| System | Subscriptions |
|--------|---------------|
| EconomySystem | MonthlyTickEvent |
| AISystem | MonthlyTickEvent |
| UnitSystem | UnitCreatedEvent, UnitDestroyedEvent, UnitMovedEvent |
| UnitVisualization | UnitCreatedEvent, UnitDestroyedEvent, UnitMovedEvent |
| ResourceBarUI | PlayerCountrySelectedEvent |

---

## Architecture Decision

**Why EventBus over C# events for GameSystems:**
- Zero-allocation (struct events, no boxing)
- Frame-coherent processing (batched)
- Automatic lifecycle management via `Subscribe<T>()`
- Loose coupling (no TimeManager reference needed)

---

## Quick Reference for Future Claude

**Key Files:**
- `Core/Events/SubscriptionToken.cs` - IDisposable tokens
- `Core/Events/CompositeDisposable.cs` - Batch disposal
- `Core/EventBus.cs` - Returns IDisposable from Subscribe
- `Core/Systems/GameSystem.cs` - Subscribe<T>() helper

**Usage in GameSystem:**
```csharp
protected override void OnInitialize()
{
    Subscribe<MonthlyTickEvent>(OnMonthlyTick);  // Auto-cleanup
}

private void OnMonthlyTick(MonthlyTickEvent evt)
{
    // Handle event
}
// No OnShutdown override needed for unsubscription!
```

**Usage in IDisposable/MonoBehaviour (StarterKit pattern):**
```csharp
private readonly CompositeDisposable subscriptions = new CompositeDisposable();

void Initialize()
{
    subscriptions.Add(eventBus.Subscribe<EventA>(HandleA));
    subscriptions.Add(eventBus.Subscribe<EventB>(HandleB));
}

void Dispose() // or OnDestroy()
{
    subscriptions.Dispose();  // Cleans up all subscriptions
}
```

---

## Links & References

### Related Sessions
- [Previous: SimpleCommand Infrastructure](1-simple-command-infrastructure.md)

---

*Scoped subscriptions eliminate manual unsubscribe boilerplate and prevent memory leaks.*
