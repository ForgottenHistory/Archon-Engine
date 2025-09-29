# Grand Strategy Game Design Discussion - Compiled Notes

## Project Overview

**Current Status**: 2 weeks into development, core architecture complete, rendering proof-of-concept working
**Developer**: Solo developer with full-time job, ~30 hours/week on project
**Goal**: Paradox-style grand strategy game with 10,000+ provinces at 200+ FPS
**Unique Positioning**: Political simulation grand strategy - not competing directly with Paradox but offering a fundamentally different approach

## Core Design Philosophy

### Simulation Over Systems
- **Command pattern architecture**: AI and players use identical functions
- **Natural limits through consequences**: No artificial caps like "aggressive expansion points"
- **Cascading effects**: Every decision creates ripples through political and economic systems
- **Political memory**: Groups remember and react to past promises/betrayals for decades

### Progressive Complexity
- **Early game**: Simple resource management (gold, manpower, armies)
- **Mid game**: Political constituencies emerge and grow
- **Late game**: Managing competing Strata becomes primary challenge
- Difficulty increases through systemic complexity, not artificial modifiers

## Key Innovations

### 1. The Political Strata System

**Three-Variable Model** replacing traditional approval:
- **Influence** (0-100): Current political power
- **Expectation** (0-100): What they believe they deserve
- **Discontent** (0-100): Gap between expectation and influence

**Three Axes of Power**:
- **Military**: Legionaries, Veterans, Guard units, etc.
- **Economic**: Merchants, Artisans, Traders, etc.
- **Civic**: Citizens, Peasants, Clergy, etc.

**Dynamic Formation**: Strata emerge based on civilization context (government type, economic system, culture) - not preset groups

**Plot System**: When Discontent ≥60, Strata initiate plots that run automatically unless player intervenes with Political Capital

### 2. Institutional Memory & Political Debt

Every significant decision creates **Political Debt Entries**:
```
{Target Stratum, Expectation Change, Time to Live}
Example: "Praetorian Guard, +15 Expectation, 12 years remaining"
```

When debts mature:
- If promise kept (Influence ≥ Expectation): Debt dissolves
- If promise broken: Discontent spike, Trust degradation
- Creates long-term consequences spanning decades

### 3. Dual Ambition System

**Personal Ambitions** (Player's goals):
- Static, culturally-specific objectives
- Provide immediate direction ("Master of Italia" for Rome)
- Cost scales based on political situation

**Stratum Ambitions** (Political demands):
- Emerge from Strata at 40-50 Discontent
- Last attempt to work within system before plotting
- Can conflict with personal ambitions and each other

**Competing Ambitions Example - "The Syracuse Question"**:
- Roman Legionaries: "Formal Conquest of Syracuse"
- Germanic Foederati: "Syracuse Raid"
- Greek Hoplites: "Syracuse Alliance"
(Player can only choose one, others gain Discontent)

### 4. Resource Management

**Primary Resources**:
- Gold, Manpower, Trade Goods

**Political Resources**:
- **Political Capital** (5/month): Emergency actions, postponing ambitions
- **Diplomatic Influence** (2/month): Foreign relations
- **Administrative Capacity**: Limits simultaneous ambitions

**No Mana System**: Resources represent actual constraints, not abstract points

## Technical Advantages

### GPU-Based Architecture (200+ FPS target)
- Entire map rendered as single quad mesh
- Province data stored in textures
- Borders generated via compute shaders
- Province selection via texture lookup (<1ms)

### Performance Solutions
- **Hot/cold data separation**: Frequently accessed data in contiguous arrays
- **Dirty flag system**: Only update changed provinces
- **Hierarchical pathfinding**: Three-layer system with aggressive caching
- **Late-game stability**: Performance doesn't degrade like Paradox games

## Differentiation from Paradox Games

### Where Paradox Fails (and we succeed):
1. **Late-game performance**: GPU architecture maintains 200+ FPS
2. **Notification spam**: Plots tick silently, three-icon status bar
3. **Mechanical transparency**: Political debts explicitly tracked
4. **Artificial limits**: Natural consequences instead of arbitrary caps
5. **Character spam**: Abstract Strata instead of thousands of courtiers

### Our Unique Strengths:
- **Expectation/Influence/Discontent triangle**: Groups can be powerful but still unhappy
- **Political debt with TTL**: Promises create long-term obligations
- **Competing ambitions**: Multiple groups want contradictory things
- **Failure creates story**: Failed ambitions fork into new political realities
- **Trust system**: Pattern of kept/broken promises affects future proposals

## EU5 Comparison (Releasing November 2025)

EU5 is iterating on Paradox's formula:
- Adding population "pops" (still abstract units)
- Automation options to handle complexity
- Policy sliders (abstract value adjustments)
- Extended timeline (starting 1337)
- Already showing performance issues in preview builds

**Our advantages remain**:
- Political groups with actual memory vs abstract pops
- Progressive complexity vs automation bypass
- Cascading consequences vs number adjustments
- Simulation depth vs mechanical width

## Release Strategy

### Demo (Rome-focused)
- Shows all core systems
- Natural progression from simple to complex
- 50-100 year timespan to demonstrate political debt maturation

### Early Access
- Major powers with unique mechanics:
  - **Carthage**: Economic-dominated politics
  - **Athens**: Democratic consensus requirements
  - **Macedonia**: Military aristocracy dynamics
  - **Egypt**: Religious-economic fusion
  - **Seleucids**: Diversity management

### DLC Philosophy
- **Depth over width**: Each DLC makes all nations deeper
- **Interconnected systems**: New mechanics hook into existing ones
- **No isolated features**: Everything affects the political simulation

## Market Positioning

**Target Audience**: 
- Grand strategy players wanting deeper simulation
- Those who bounce off Paradox's gamification
- Players seeking better performance
- History/politics simulation enthusiasts

**Market Reality**:
- Don't need Paradox's full audience
- 5% of EU4's 2 million owners = 100,000 potential customers
- At $30-40 price point = viable solo developer success

**Messaging**: "Political grand strategy where decisions create cascading consequences through living political systems"

## Development Priorities

### Immediate
- Map mode system completion
- Province ownership changes
- Time tick implementation
- Political Capital actions

### Near-term (2-3 weeks)
- Economy basics
- Nation system
- Command pattern implementation
- Save/load prototype

### Medium-term (1-3 months)
- Military units
- Culture/religion basics
- UI framework
- Vertical slice with 50 provinces

### Long-term (6-12 months)
- Full early access release
- Mod support implementation
- Performance optimization
- Community building

## Design Principles

### Core Tenets
1. **Every exploit makes the game harder**: Breaking promises has lasting consequences
2. **Simulation purity**: Ask "what would really happen?" not "what number to tweak?"
3. **Make failure interesting**: Failed ambitions create new political dynamics
4. **Visible consequences**: Show the political web and how actions affect it
5. **Sacred timers**: Political debts always come due on schedule

### Anti-Patterns to Avoid
- No artificial caps or "mana"
- No notification spam
- No consequence-free decisions
- No hidden calculations
- No save-scum incentives

## Community & Modding

### Open Source Engine Strategy
- Engine open source, game proprietary
- Enables community bug fixes and optimizations
- Builds technical credibility
- More moddable than Paradox games

### Template System Benefits
- Events are text files with variables
- Command pattern makes everything moddable
- Registry system for easy entity addition
- Community can fill content gaps

## Success Metrics

**The game succeeds when**:
- Players think dynastically about 10+ year consequences
- Political trade-offs feel authentic with no "correct" choice
- Natural historical parallels emerge through simulation
- Each campaign creates unique political stories
- Complexity feels intuitive despite depth

**The game fails if**:
- Micromanagement overwhelms strategic decisions
- One political strategy dominates
- Consequences feel arbitrary or disconnected
- Players can't understand cause-effect relationships
- Exploits trivialize the simulation

## Key Insight

This isn't "EU4 but harder" - it's a fundamentally different type of grand strategy game. Where Paradox games are wide but shallow with mechanical systems, this is narrow but deep with genuine political simulation. The cascading consequences of the political system create emergent narratives that Paradox's architecture cannot achieve.

The combination of:
- Technical performance advantages (200+ FPS)
- Novel political mechanics (Strata with memory)
- Progressive complexity (grows with empire)
- Simulation depth (everything interconnects)

...positions this as the grand strategy game for players who want their decisions to matter across decades, where success creates new challenges, and where managing internal politics is as important as external conquest.

---

*"Build the game a significant subset of the community has been waiting for without knowing it existed."*