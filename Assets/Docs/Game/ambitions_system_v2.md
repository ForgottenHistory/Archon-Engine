### Competing Ambitions Within Categories

The real political tension comes when multiple Strata in the same category want contradictory things:

**Military Competition Example - "The Syracuse Question":**
- **Roman Legionaries** propose: "Formal Conquest of Syracuse" (proper siege, occupation, integration)
- **Germanic Foederati** propose: "Syracuse Raid" (quick loot, burn, return)
- **Greek Hoplites** propose: "Syracuse Alliance" (they're Greeks too, should be partners not subjects)

You can only choose one. The others see their proposal rejected and gain Discontent.

**Economic Competition Example - "Trade Policy Crisis":**
- **Egyptian Grain Merchants** propose: "Free Trade Act" (remove all tariffs)
- **Urban Artisans** propose: "Protect Local Production" (high tariffs on crafts)
- **Syrian Traders** propose: "Eastern Privilege Zone" (tariffs only on western goods)

Accepting any one makes the others hostile. This is intentional - empire management is about choosing which groups to favor.

**Civic Competition Example - "Citizenship Question":**
- **Roman Citizens** propose: "Preserve Citizenship Value" (keep exclusive)
- **Latin Rights holders** propose: "Gradual Integration Path" (slow expansion)
- **Peregrini** propose: "Universal Citizenship Now" (immediate inclusion)

The same issue, three incompatible visions from three groups you need to manage.# Ambitions System - Revised Design

## Overview

The Ambitions system provides both **personal strategic goals** and **dynamic political demands** that shape your empire's trajectory. Personal ambitions give players immediate direction and fantasy fulfillment (like EU4's mission trees), while Stratum ambitions emerge from political tensions and create genuine strategic dilemmas - the military doesn't care that you're fighting in Gaul when they demand conquest of Syracuse.

This dual system ensures players always have clear goals while navigating the messy reality of political management.

---

## Core Philosophy

### Dual-Layer Goal System
- **Personal Ambitions**: Static, culturally-specific goals that give immediate direction (the "fantasy")
- **Stratum Ambitions**: Dynamic political demands based on current situation (the "reality")
- **Resource Competition**: Both types compete for the same limited resources and attention

### Political Reality Over Player Convenience
- **Inconvenient Timing**: Factions propose ambitions based on THEIR priorities, not yours
- **Political Pressure**: Ignoring proposals has consequences through the Strata system
- **No Perfect Paths**: Every ambition accepted is another refused

### Unified Political Debt
- **Single Ledger**: Ambitions feed directly into the Strata Expectation system
- **No Parallel Mechanics**: Accepting ambitions creates Expectation debt, not separate approval
- **Natural Consequences**: Success/failure flows through existing political mechanics

---

## Personal Ambitions (The Fantasy Layer)

### What They Are
Personal ambitions are the **player's** goals - static, culturally-appropriate objectives that provide immediate direction when starting a campaign. Like EU4's mission tree or HOI4's focus tree, these give that crucial "here's what a Roman player should aspire to" guidance.

### Key Characteristics
- **Always Available**: Visible from day 1, giving immediate goals
- **Culturally Specific**: Each civilization has unique personal ambitions matching their historical trajectory
- **Player-Paced**: No time pressure or political cost to pursue (or ignore)
- **Significant Rewards**: Completing these shapes your empire's identity

### Examples by Culture

#### Roman Personal Ambitions
1. **"Master of Italia"** 
   - Control all Italian provinces
   - Reward: "Italian Hegemony" - permanent +25% manpower from Italian cultures
   - Unlocks: "Mare Nostrum" personal ambition

2. **"Mare Nostrum"**
   - Control entire Mediterranean coastline
   - Reward: "Thalassocracy" - naval maintenance -50%, naval morale +20%
   - Unlocks: "Pax Romana" personal ambition

3. **"The Gallic Conquest"**
   - Control all of Gaul
   - Reward: "Gallic Auxiliaries" - unique cavalry unit type
   - Unlocks: "Rhine Frontier" personal ambition

#### Greek Personal Ambitions
1. **"Restore the Polis"**
   - Develop capital to 50+ development
   - Reward: "Philosophical Schools" - technology cost -15%
   - Unlocks: "Hellenic League" personal ambition

2. **"Olympic Supremacy"**
   - Build Grand Temple, Theatre, and Gymnasium
   - Reward: "Cultural Prestige" - diplomatic reputation +2
   - Unlocks: "Pan-Hellenic Hegemony" personal ambition

#### Carthaginian Personal Ambitions
1. **"Merchant Empire"**
   - Establish 10 trade routes
   - Reward: "Commercial Dominance" - trade income +30%
   - Unlocks: "New Phoenicia" personal ambition

2. **"Sacred Band"**
   - Maintain elite army of 10+ units for 10 years
   - Reward: "Elite Traditions" - army morale +15%
   - Unlocks: "Revenge on Rome" personal ambition

### Mutual Exclusivity with Stratum Ambitions

**Critical Design Point**: Personal and Stratum ambitions can CONFLICT:

- You're pursuing "Master of Italia" (personal)
- Praetorian Guard demands "The Syracuse Campaign" (stratum)
- Urban Artisans demand "Trade Peace with Syracuse" (stratum)

You must choose: Continue your personal goal? Satisfy the military? Pursue economic opportunity?

### Dynamic Cost Scaling

Personal ambitions become **more expensive** based on your political situation:

| Situation | Personal Ambition Cost Modifier |
|-----------|--------------------------------|
| No active Stratum ambitions | 100% (normal) |
| 1 Stratum ambition active | 125% cost |
| 2+ Stratum ambitions active | 150% cost |
| Any Stratum >50 Discontent | 200% cost |

This represents the political capital required to pursue personal glory while managing internal pressures.

## Stratum Ambitions (The Political Layer)

### How They Generate

### Stratum-Driven Proposals

Ambitions emerge from specific Strata based on their Discontent levels and current context:

| Discontent Level | Stratum Behavior | Ambition Type |
|-----------------|------------------|---------------|
| 0-30 | Content, occasional suggestions | Opportunistic improvements |
| 31-50 | Active political agenda | Aggressive proposals to secure position |
| 51-59 | Pre-plot desperation | "Work within system" demands |
| 60+ | Plotting (see Strata doc) | Ambitions blocked, plots active |

**Key Mechanism**: Strata at 40-50 Discontent preferentially propose ambitions instead of plots - it's their last attempt to work within the system before resorting to conspiracy.

### Context Triggers

Each type of Stratum has trigger conditions based on their cultural identity and position:

**Roman Legionaries (Founding Military):**
- Weak neighbor + Roman borders → "Expand the Republic/Empire"
- Recent victory + high Influence → "Permanent Legion Commands"
- Peace for 5+ years → "Frontier Fortifications"
- Low Influence → "Restore Traditional Privileges"

**Germanic Foederati (Conquered Military):**
- Weak neighbor → "Season of Raids"
- High Roman cultural pressure → "Preserve Warrior Traditions"
- Low pay → "Renegotiate Foederati Treaty"
- Border threat → "Defend Our Homelands First"

**Greek Hoplites (Integrated Military):**
- Cultural dominance in region → "Hellenic Officer Corps"
- Eastern conflict → "No Persian Wars" (oppose)
- High prosperity → "Olympic Games Sponsorship"
- Roman dominance → "Restore Greek Military Traditions"

**Egyptian Grain Merchants (Regional Economic):**
- Mediterranean trade dominance → "Grain Monopoly"
- Nile flooding → "State Granary Investment"
- War with grain producers → "Protect Trade Partners" (oppose war)
- Competition from Sicily → "Exclusive Supply Rights"

**Syrian Traders (Frontier Economic):**
- Eastern contact → "Silk Road Expansion"
- Religious differences → "Merchant Immunity Laws"
- Western focus → "Don't Abandon Eastern Trade" (oppose)
- High tariffs → "Free Trade Zones"

**Urban Artisans (Core Economic):**
- Import competition → "Protective Tariffs"
- Military dominance → "Public Works over Wars"
- Raw material shortage → "Secure Resource Provinces"
- Peace → "Grand Construction Projects"

### Activity Cycles

Rather than artificial seasons, Strata activity fluctuates based on events:

| Event | Effect on Strata Activity |
|-------|--------------------------|
| Won Major War | Military quiet for 2 years, Economic/Civic become active |
| Lost Major War | Military hyperactive for 3 years |
| Economic Boom | Economic quiet for 1 year, others seek share of prosperity |
| Succession | ALL Strata become active (new ruler = new deals) |
| Plague/Disaster | Civic hyperactive, others temporarily suppressed |

---

## Ambition Mechanics

### The Proposal

When a Stratum proposes an ambition, it appears as a **political demand**, not a quest:

```
The Praetorian Guard demands: "The Syracuse Campaign"

Legate Marcus addresses the Senate: "Syracuse grows fat while our veterans 
lack land. Their walls are old, their allies distant. Grant us this campaign 
and the Guard shall remember your support."

Accepting Creates: +15 Expectation for Praetorian Guard
Time Limit: Conquer within 3 years
Success Converts: Expectation → Influence for Praetorian Guard
Failure Maintains: Expectation remains, creating Discontent

Other Strata Reactions:
- Urban Artisans: +5 Expectation (war contracts)
- Provincial Elites: +10 Expectation (war taxes)

[Accept] [Postpone - 5 Political Capital] [Refuse]
```

### Postponement Politics

---

## Failure as Political Reality

### Opposition Memory System

Failed ambitions become **political ammunition**:

**The "Syracuse Embarrassment" Mechanism**:
- You fail "Conquer Syracuse" 
- For next 5 years, when Military proposes ANY conquest:
  - Economic Strata can invoke "Remember Syracuse" for +10 Influence in opposing
  - Population Strata gain "War Weariness" modifier (+5 Discontent if you accept)
  - Military Strata has -50% chance to propose conquests (lost credibility)

**Faction Trust Ratings**:
Each Stratum tracks trust separately from Influence/Expectation:
- **High Trust (75-100)**: Proposes ambitious, high-reward goals
- **Medium Trust (40-74)**: Proposes moderate, achievable goals  
- **Low Trust (0-39)**: Proposes only safe, minor goals (or stops proposing)

Trust changes:
- Success: +15-25 Trust with that Stratum
- Failure: -20-30 Trust with that Stratum
- Pattern of Success/Failure: Compounds effects

### Failure Forks

Failed ambitions don't end - they transform:

| Original Ambition | Failure Result | Political Fork |
|-------------------|----------------|----------------|
| Conquer Syracuse | Lost the war | **"Sicilian Reparations"**: Pay 2000 gold tribute OR Military plots begin |
| Trade Monopoly: Tin | Only achieved 40% | **"Cartel Agreement"**: Accept shared monopoly OR Economic Strata demand subsidies |
| Cultural Integration | Provinces revolt | **"Harsh Suppression"** OR **"Cultural Autonomy"** - both have long-term consequences |

---

## Strategic Load Management

### Ambition Slots and Political Bandwidth

You have limited capacity to pursue ambitions simultaneously:

| Era | Personal Ambitions | Stratum Ambitions | Total Bandwidth |
|-----|-------------------|-------------------|-----------------|
| Early (Small Empire) | 1 active | 1 active | 2 total |
| Classical (Regional Power) | 1 active | 2 active | 3 total |
| Imperial (Major Empire) | 2 active | 2 active | 4 total |

**Exceeding Bandwidth**: Can accept more ambitions but:
- All ambitions progress 50% slower
- Administrative efficiency -20%
- All Strata +5 Discontent (government is overextended)

### Late-Game Compression

Once you control >50 provinces, Stratum ambitions become **Grand Programs**:

Instead of "Conquer Syracuse" + "Conquer Athens" + "Conquer Sparta", you get:
- **"Hellenic Subjugation Program"**: Control 75% of Greek culture provinces

Instead of multiple trade route ambitions:
- **"Mediterranean Commerce Domination"**: Establish trade supremacy in 3 sea zones

These Grand Programs:
- Still occupy only 1 ambition slot
- Represent entire Stratum agendas rather than single goals
- Take 5-10 years to complete
- Provide scaling rewards based on completion percentage

---

## Event-Driven Dynamics

### Ambitions Don't Wait for Convenience

**The Cascading Crisis Example**:
1. You're pursuing "Master of Italia" (personal ambition)
2. Gauls invade from north - must respond militarily
3. DURING the Gallic War, Praetorian Guard proposes "Conquer Syracuse"
4. Urban Artisans simultaneously propose "Peace & Prosperity Focus"
5. Provincial Elites, seeing you're distracted, propose "Local Autonomy Rights"

You must choose priorities while everything burns.

### World Reactions

AI nations develop **counter-ambitions** to player success:

| Player Ambition | AI Counter-Response |
|-----------------|---------------------|
| "Mediterranean Trade Monopoly" | Carthage develops "Break Roman Monopoly" |
| "Conquer Greek Cities" | Greek states form "Pan-Hellenic Defense League" |
| "Cultural Supremacy" | Neighbors develop "Cultural Resistance" ambitions |

These aren't just difficulty increases - they're visible AI ambitions you can spy on and counter.

---

## Integration with Core Systems

### Unified with Strata System

**No Separate Approval**:
- Ambitions don't give "+20 approval" 
- They add Expectation debt to the Strata ledger
- Success converts Expectation → Influence
- Failure leaves Expectation hanging → Discontent

**Plot Prevention Window**:
- Strata at 40-50 Discontent propose ambitions before plotting
- Accepting their ambition can prevent the plot
- But adds Expectation debt you MUST fulfill

### Personal vs Political Trade-offs

The dynamic cost scaling for personal ambitions based on political situation means players constantly weigh:
- **Glory** (personal ambitions) vs **Stability** (Stratum ambitions)
- **Long-term vision** vs **Immediate political needs**
- **Historical trajectory** vs **Opportunistic adaptation**

---

## Success Metrics

The system succeeds when:

1. **Players Feel Political Pressure**: "I need to throw the military a bone before they revolt"
2. **Timing Creates Drama**: "Of course they want conquest NOW, mid-plague"
3. **Failure Has Meaning**: Failed ambitions create new political realities, not just penalties
4. **Personal Goals Matter**: Players still pursue their fantasy despite political chaos
5. **Every Campaign Differs**: Same start position creates different stories based on which Strata get loud when

The system fails if:

1. **Players Ignore Politics**: Just pursuing personal ambitions without consequence
2. **Predictable Patterns**: Same Strata always propose same ambitions at same times
3. **Failure Means Reload**: Players savescum rather than accept political consequences
4. **Bandwidth Ignorable**: No real penalty for accepting everything
5. **Trust Becomes Binary**: Strata either always trust or never trust, no middle ground

---

## Implementation Priority

### Phase 1: Core Personal + Political
- Personal ambitions for 3 major cultures (Rome, Greece, Carthage)
- Basic Stratum ambition proposals tied to Discontent levels
- Expectation integration with Strata system
- Accept/Refuse/Postpone mechanics

### Phase 2: Political Memory
- Opposition memory ("Remember Syracuse")
- Trust ratings affecting proposal quality
- Failure fork system
- Mutual exclusivity windows

### Phase 3: Dynamic Systems
- Activity cycles based on events
- World counter-ambitions
- Late-game Grand Programs
- Strategic load/bandwidth limits

### Phase 4: Polish
- Full cultural variety for personal ambitions
- Complex cascading crisis scenarios
- AI ambition visibility/spying
- Achievement integration

---

*The Ambitions system creates a constant tension between your grand vision and political reality. Every campaign becomes a story of which dreams you achieved, which you sacrificed, and which were torn from you by the endless demands of those you rule.*