# Archon-Engine Scenes

---

## Available Scenes

### StarterKit.unity
**Purpose:** Minimal working game demonstrating ENGINE patterns.

**Use for:**
- Learning Archon-Engine architecture
- Testing ENGINE features
- Starting point for new games

**Entry point:** `StarterKit.Initializer` coordinates all systems.

**Documentation:** See `Scripts/StarterKit/README.md`

---

## Scene Architecture

Scenes should follow phase-based initialization:
1. ENGINE systems initialize first (EngineMapInitializer)
2. GAME/StarterKit systems initialize after ENGINE completes
3. UI components initialize after systems are ready

Never use `Awake()` or `Start()` for game logic - let initializers control load order.
