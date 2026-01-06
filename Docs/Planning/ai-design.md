# AI Architecture Design
**Type:** Architecture Design Document
**Scope:** AI decision-making for grand strategy games at scale

---

## THE PROBLEM

Grand strategy games have 200+ nations making strategic decisions simultaneously. Traditional "think about everything every frame" approaches fail at scale.

**Challenge:** Intelligent strategic AI without performance collapse
**Target Scale:** 200+ nations, <10ms total AI processing per frame
**Key Constraint:** Must remain deterministic for multiplayer

---

## CORE PRINCIPLES

### Principle 1: Temporal Hierarchy

**Insight:** Not all decisions need same update frequency.

**Three Decision Layers:**
- **Strategic (Monthly)** - Long-term goals, diplomatic strategy, economic focus
- **Tactical (Weekly)** - War planning, resource allocation, diplomatic actions
- **Operational (Daily)** - Army movement, combat decisions, emergency responses

**Why This Works:**
- Strategic decisions are rare, expensive to compute
- Tactical decisions are medium frequency, medium cost
- Operational decisions are frequent, cheap to compute
- Total cost amortized across time scales

**Trade-off:** AI less reactive to rapid changes vs computational feasibility

### Principle 2: Distance-Based Priority Scheduling

**Insight:** Not all AI nations need to think at the same frequency. Nations near the player matter more.

**Tier-Based Processing:**
- Calculate distance from player (BFS through province adjacency)
- Assign tiers based on distance thresholds
- Each tier has different processing interval
- Near AI = frequent (hourly), Far AI = infrequent (every 3 days)

**Default Tiers (Configurable):**
- Tier 0 (neighbors): Every 1 hour
- Tier 1 (near): Every 6 hours
- Tier 2 (medium): Every 24 hours
- Tier 3 (far): Every 72 hours

**Why This Works:**
- Player-relevant AI feels responsive
- Distant AI doesn't waste cycles
- Smooth frame times (staggered initialization)
- Recalculate distances monthly (borders change)

**Trade-off:** Far AI less reactive vs computational efficiency

### Principle 3: Shared Calculations

**Insight:** Many AI decisions require same expensive calculations.

**Calculate Once, Use Many Times:**
- Regional military strengths (who controls what)
- Threat assessment (who is dangerous)
- Province values (what territory is worth conquering)
- Diplomatic networks (who allies with whom)

**Why This Works:**
- O(N) shared calculation vs O(N²) per-AI recalculation
- Cache invalidation only on major changes (wars, alliances)
- All AI query same shared data structure

**Trade-off:** Memory cost for caching vs CPU cost for recalculation

### Principle 4: Goal-Oriented Behavior

**Insight:** AI needs persistent priorities, not reactive chaos.

**Goal System:**
- Each nation maintains prioritized goals (conquer region, build economy, form alliance)
- Goals scored by desirability (personality + situation + difficulty)
- Highest-scoring goal executed
- Goals persist until complete or situation changes

**Why This Works:**
- Predictable behavior (players understand AI motivations)
- Coherent strategy (AI commits to plans)
- Extensible (add new goals without refactoring)

**Trade-off:** Less reactive to opportunities vs strategic coherence

### Principle 5: Decision Pruning

**Insight:** Don't evaluate obviously bad decisions.

**Prune Early:**
- Bankrupt nations don't evaluate declaring wars
- Nations without manpower don't evaluate recruiting
- Max alliance limit reached → skip alliance evaluation

**Why This Works:**
- Reduces decision space exponentially
- Focuses computation on viable options
- Sanity checks prevent stupid AI behavior

**Trade-off:** Might miss creative edge cases vs performance and robustness

---

## ARCHITECTURE PATTERNS

### Pattern: Three-Layer Brain

**Strategic Layer:**
- **Frequency:** Monthly (every 30 days)
- **Scope:** Long-term planning, goal prioritization
- **Examples:** Should I focus on economy or military? Who to ally with?

**Tactical Layer:**
- **Frequency:** Weekly (every 7 days)
- **Scope:** Medium-term execution, resource allocation
- **Examples:** Where to attack? What to build? Improve relations with whom?

**Operational Layer:**
- **Frequency:** Daily or immediate
- **Scope:** Short-term execution, crisis response
- **Examples:** Move army to border, retreat from battle, emergency treasury management

**Integration:** Layers feed each other (strategic sets goals → tactical plans actions → operational executes)

### Pattern: Tier-Based Interval Processing

**Distance-Based Tiers:**
- Calculate province-hop distance from player via BFS
- Assign countries to tiers based on distance thresholds
- Each tier has configurable processing interval (hours)
- ENGINE provides mechanism, GAME defines tier policy

**Interval Scheduling:**
- Track lastProcessedHour per AI (hour-of-year, 0-8639)
- Each hour tick, check if interval elapsed for each AI
- Process AI whose interval has passed, update lastProcessedHour
- Handles year wrap correctly

**Staggered Initialization:**
- Stagger lastProcessedHour at game start
- Prevents all same-tier AI processing simultaneously
- Distributes load evenly across intervals

**Monthly Recalculation:**
- Recalculate distances when borders change
- Subscribe to MonthlyTickEvent for automatic updates
- Tiers reassigned based on new distances

### Pattern: Shared Intelligence Data

**Global Context:**
- Military strength maps (who has troops where)
- Economic rankings (richest to poorest nations)
- Threat assessments (who is dangerous)
- Strategic province values (what territory matters)

**Update Frequency:**
- Recalculate monthly (amortized cost)
- Invalidate on major events (wars, major conquests)
- All AI query from same shared data

**Memory vs CPU Trade-off:**
- Store derived data (threat levels, province values)
- Recompute only when inputs change
- Acceptable memory cost for massive CPU savings

### Pattern: Goal Hierarchy

**Goal Structure:**
- Goals have priorities (0-1000)
- Goals have evaluation functions (score desirability)
- Goals have execution functions (take actions)

**Goal Selection:**
- Evaluate all available goals
- Sort by desirability score
- Execute highest-scoring goal(s)

**Goal Persistence:**
- Goals remain active until completed or irrelevant
- Don't recalculate goal priorities every frame
- Goal state persists across frames

### Pattern: Personality-Driven Scoring

**Personality Traits:**
- Aggression (warlike vs peaceful)
- Economic focus (trader vs warrior)
- Diplomatic focus (alliance-builder vs isolationist)
- Risk tolerance (conservative vs ambitious)

**Score Modification:**
- Base goal scores modified by personality
- Aggressive AI: war goals × 2.0, economy goals × 0.5
- Economic AI: economy goals × 2.0, war goals × 0.5

**Emergent Behavior:**
- Same goal system, different personalities
- Players recognize AI archetypes (merchant, conqueror, diplomat)
- Personality-driven decisions feel human-like

---

## PERFORMANCE STRATEGIES

### Strategy: Amortized Computation

**Spread Expensive Work Over Time:**
- Monthly strategic updates amortized over 30 days
- Shared calculations amortized over all AI
- Crisis processing budgeted separately

**Frame Budget:**
- Target <10ms per frame for all AI
- Strategic: ~2ms (6-7 nations)
- Tactical: ~1ms (8 nations)
- Operational: ~2ms (war nations only)
- Shared calculations: ~0.5ms (amortized)

### Strategy: Aggressive Caching

**Cache Everything Expensive:**
- Path distances between provinces
- War evaluation results (win probability, expected gains)
- Province values (strategic importance)
- Threat assessments

**Cache Invalidation:**
- Version counter (increment on major changes)
- Lazy invalidation (check version on access)
- Partial invalidation (only affected data)

### Strategy: Parallel Processing

**Independent AI Can Process Simultaneously:**
- Group non-interacting nations (different continents, not at war)
- Process groups in parallel
- Synchronize only when interactions possible

**Constraints:**
- Nations at war cannot process in parallel (shared state)
- Nations in same trade node cannot process in parallel
- Otherwise safe to parallelize

---

## DESIGN DECISIONS & TRADE-OFFS

### Decision: Distance-Based Tiers Over Arbitrary Bucketing

**Chosen:** Distance-based tier scheduling (near AI = frequent, far AI = rare)
**Alternative:** Arbitrary bucketing (countryID % N)
**Trade-off:** Additional distance calculation vs meaningful prioritization
**Rationale:** Player-relevant AI should feel responsive; distant AI can be background processed

### Decision: Goal-Oriented Over Utility-Based

**Chosen:** Goal hierarchy (persistent priorities)
**Alternative:** Utility AI (reactive scoring)
**Trade-off:** Strategic coherence vs opportunity exploitation
**Rationale:** Predictable, human-like behavior more important than optimization

### Decision: Three Layers Over Single Layer

**Chosen:** Strategic/Tactical/Operational layers
**Alternative:** Single unified decision loop
**Trade-off:** Complexity vs computational efficiency
**Rationale:** Temporal hierarchy enables scale (200+ nations)

### Decision: Shared Data Over Per-AI Recalculation

**Chosen:** Shared global calculations
**Alternative:** Each AI computes independently
**Trade-off:** Memory cost vs CPU cost
**Rationale:** O(N) shared beats O(N²) independent at scale

### Decision: Simple Heuristics Over Perfect AI

**Chosen:** Fast, good-enough heuristics
**Alternative:** Deep search, perfect play
**Trade-off:** Computational cost vs AI quality
**Rationale:** 200 good-enough AI better than 10 perfect AI

---

## EXTENSIBILITY PRINCIPLES

### Add Goals Without Refactoring

New goals extend base goal interface, register at initialization. Existing goals unchanged.

### Add Personality Without Refactoring

Personality modifiers applied via optional overrides. Existing goals work with or without personality.

### Add Layers Without Refactoring

Layers call each other via interfaces. MVP starts single-layer, add tactical/operational later.

### Add Caching Without Refactoring

Caching layer inserted via optional context parameter. Existing code ignores null cache.

---

## INTEGRATION CONSTRAINTS

### Must Use Command Pattern

AI actions go through same commands as player. No AI-specific code paths. Enables multiplayer and replay.

### Must Be Deterministic

Same inputs must produce same outputs. Use fixed-point math, deterministic ordering. Critical for multiplayer.

### Must Respect Event System

AI reacts to events (war declared, peace made). Uses EventBus pattern, not C# events. Zero allocations.

### Must Serialize State

AI state must save/load. Goals, priorities, cached scores persist across sessions.

---

## SUCCESS METRICS

**Performance:**
- 200+ AI nations
- <10ms total AI processing per frame
- Smooth frame times (no stutters)

**Quality:**
- AI makes sensible decisions (doesn't bankrupt itself)
- AI adapts to player actions (defends when attacked)
- AI personalities feel distinct (aggressive vs economic)

**Architecture:**
- Extensible (add goals without refactoring)
- Deterministic (same seed = same behavior)
- Multiplayer-ready (command pattern integration)

---

## RELATED PATTERNS

- **Pattern 2 (Command Pattern)** - AI uses player commands
- **Pattern 3 (EventBus)** - AI reacts to game events
- **Pattern 5 (Fixed-Point Determinism)** - AI scoring uses FixedPoint64
- **Pattern 10 (Frame-Coherent Caching)** - AI caching strategy
- **Pattern 12 (Pre-Allocation)** - AI state in NativeArray

---

## KEY INSIGHTS

**Philosophy:** AI doesn't need to be smart every frame - it needs to be smart enough, often enough, without killing performance.

**Architecture:** Temporal hierarchy + bucketing + shared data = scalable grand strategy AI

**Trade-offs:** Strategic coherence over perfect reactions, good-enough heuristics over perfect play

**Extensibility:** Architecture designed for scale, implement features incrementally

---

*Design Document - Timeless Principles*
*See: ai-system-implementation.md for current implementation status*
*Last Updated: 2026-01-06 (tier-based scheduling)*
