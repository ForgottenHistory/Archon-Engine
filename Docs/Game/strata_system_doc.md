# Political Strata System - Design Document

## Overview

The Political Strata system is the **core political engine** of the game, replacing traditional "approval ratings" with a dynamic simulation of actual political constituencies. Unlike static interest groups, **Strata** emerge organically from your civilization's structure - government type, economic system, dominant culture, and technological era - creating authentic political pressures that evolve as your society changes.

This system forces players to think like actual rulers: managing competing constituencies, honoring past promises, and navigating the long-term consequences of every political decision.

---

## From Interest Groups to Strata

### The Three Axes of Power

Political power flows through three fundamental axes, each containing multiple **Strata** that spawn, merge, or dissolve based on your civilization's context:

| **Axis** | **What It Represents** | **Example Context Dependencies** |
|----------|------------------------|----------------------------------|
| **Military** | Professional soldiers, warrior aristocracy, veterans, military engineers | Government type, military tradition, current conflicts |
| **Economic** | Merchants, artisans, tax collectors, landowners, laborers | Economic system, trade networks, urbanization level |
| **Civic** | Citizens, peasants, clergy, intellectuals, provincial peoples | Religion, culture, administrative structure |

### Dynamic Strata Formation

**Strata exist only when relevant**: A stratum must represent ≥3% of total population to have political weight. Below this threshold, it dissolves and its population redistributes to related strata.

**Context-dependent emergence**: Your civilization's tags determine which strata can exist:

| **Government Type** | **Available Military Strata** |
|---------------------|-------------------------------|
| Republic | Legionary Veterans, Citizen Militias, Senate Guards |
| Empire | Praetorian Guard, Professional Legions, Auxiliary Forces |
| Tribal | War-Band Chiefs, Warrior Societies, Clan Militia |
| Theocracy | Temple Guards, Holy Orders, Crusading Knights |

| **Economic System** | **Available Economic Strata** |
|---------------------|-------------------------------|
| Slave Economy | Latifundia Owners, Urban Slaves, Slave Traders |
| Guild System | Master Craftsmen, Guild Merchants, Apprentice Workers |
| Market Economy | Free Traders, Banking Houses, Wage Laborers |
| Subsistence | Subsistence Farmers, Village Elders, Local Traders |

**Population automatically redistributes** - players never manually move pops between strata. The system recalculates weights annually based on economic changes, conquests, laws, and technological developments.

---

## The Three-Variable Political Model

Each stratum tracks three interconnected variables that replace traditional approval ratings:

### Influence (0-100)
**What it represents**: Current political power and access to resources  
**How it increases**: Jobs, buildings, military commands, trade shares, temple holdings, administrative positions  
**How it decreases**: Economic downturns, military defeats, rival strata gaining power, purges or reforms  

### Expectation (0-100)  
**What it represents**: What the stratum believes it deserves based on past treatment and cultural narratives  
**How it increases**: Player promises, rewards to similar strata, prosperity of comparable groups in other nations (via trade routes), cultural traditions  
**How it decreases**: Very slowly over generations, or through major cultural/religious shifts  

### Discontent (0-100)
**What it represents**: Political frustration and willingness to act against the current order  
**Calculation**: `max(0, Expectation - Influence)`  
**Critical threshold**: At ≥60 Discontent, strata begin **plotting**

---

## The Plot System: Politics Without Pop-Up Spam

### How Plots Work

When a stratum reaches 60+ Discontent, it automatically selects a **Plot Template** from its available list. Plots are self-contained political stories that:
- Run for 2-5 years automatically
- Resolve without player intervention unless they choose to spend political capital to interfere
- Create new long-term political consequences regardless of how they're handled

### Plot Examples by Strata

#### Praetorian Guard: "Offer Purple to General"
**Trigger**: Discontent ≥60, Recent military victory  
**Duration**: 18 months  
**Player Options**:
- **Pay Donative** (500 gold): Influence +20, Discontent -30, but Expectation permanently +10
- **Refuse**: 30% chance of coup attempt next year, other Military strata +5 Discontent
- **Ignore**: Plot resolves automatically with moderate negative consequences

#### Urban Artisans: "Guild Strike"  
**Trigger**: Discontent ≥60, Economic hardship  
**Duration**: 2 years  
**Player Options**:
- **Grant Monopoly Charter**: Economic tech costs -10%, but all other Economic strata +15 Discontent
- **Send Troops**: Population Influence -10, Urban Stability -1, quick resolution
- **Ignore**: Reduced trade income for 3 years, potential spread to other cities

#### Provincial Elites: "Appeal to Foreign King"
**Trigger**: Discontent ≥60, Border region, Foreign diplomatic contact  
**Duration**: 3 years  
**Player Options**:
- **Allow Local Mint**: Lose 5% central tax revenue, but stratum stops foreign correspondence
- **Increase Autonomy**: Province gains special status, reduces central control
- **Ignore**: Foreign nation gains "Liberate Province" casus belli

### Plot UI Integration
- **Visual Representation**: Parchment scroll icons on the map with hourglass timers
- **No Interruption**: Plots tick down silently unless player clicks to interact
- **Information on Demand**: Hover shows plot progress, click opens intervention options
- **One Plot Per Stratum**: No overwhelming multiple crises from the same group

---

## Government as Power Structure

Government type determines **which strata can legally hold maximum Influence**, creating authentic constitutional limitations:

### Legal Power Hierarchies

| **Government** | **Legal Top Strata** | **Illegal Top Strata** | **Constitutional Crisis** |
|----------------|---------------------|------------------------|---------------------------|
| **Republic** | Senate Families, Citizen Soldiers | Slaves, Foreign Merchants, Professional Armies | "Crisis of the Republic" - civil war mechanics |
| **Empire** | Imperial Household, Legionary Veterans | City Plebs, Barbarian Foederati | Emperor can issue "Edict of Tolerance" for 10 years (costs Stability) |
| **Tribal Confederation** | War Chieftains, Elder Councils | Urban Merchants, Foreign Settlers | "Breaking of the Sacred Ways" - tribal dissolution |
| **Theocracy** | High Clergy, Temple Estates | Secular Merchants, Foreign Priests | "Heresy Crisis" - religious purge demands |

### Dynamic Power Ranking
- **Annual Recalculation**: Top Influence rankings updated every January
- **Predictable Trends**: UI shows rising/falling strata with 12-month projections
- **Constitutional Preparation**: Players can see potential crises coming and prepare responses
- **Systemic Consequences**: Illegal influence doesn't just create events - it fundamentally changes how the government functions

---

## Institutional Memory: The Long-Term Consequence Engine

### How Promises Become Debts

Every significant player action creates **Political Debt Entries** in the Institutional Memory ledger:

```
Entry Format: {Target Stratum, Expectation Change, Time to Live}
Example: "Praetorian Guard, +15 Expectation, 12 years remaining"
```

**Sources of Political Debts**:
- Event choices ("We shall reward the loyal legions!")
- Policy decisions (Land redistribution, tax changes, military reforms)
- War outcomes (Victory bonuses, defeat consequences)
- Building projects (Who benefits from new infrastructure)
- Trade agreements (Which strata gain/lose from new commerce)

### Debt Maturation and Collection

**Time to Live (TTL)**: Debts mature over 5-15 years depending on the promise type
- **Immediate rewards**: 5-8 years (military bonuses, emergency aid)
- **Structural promises**: 8-12 years (legal reforms, institutional changes)  
- **Generational commitments**: 12-15 years (cultural integration, religious policies)

**Collection Mechanics**: When TTL expires, the debt "comes due"
- **If Current Influence ≥ Expected Influence**: Debt dissolves peacefully
- **If Current Influence < Expected Influence**: Discontent increases by the difference
- **Compound Interest**: Unmet debts create additional Expectation increases

### Example Debt Cycle

**Year 1**: Player chooses "Grant Land to Veterans" event option
- Creates debt: `{Legionary Veterans, +20 Expectation, 10 years}`
- Immediate effect: Veterans gain +15 Influence from land grants

**Years 2-9**: Veterans maintain higher Expectation baseline
- Player must continue providing military opportunities, buildings, or other Influence sources
- If Veterans' Influence drops below elevated Expectation, Discontent begins accumulating

**Year 11**: Debt matures and "comes due"
- If Veterans have maintained sufficient Influence: Debt dissolves, no consequences
- If Veterans' current Influence < Promised level: Immediate Discontent spike
- Creates potential for "Broken Promises" plot template activation

---

## Difficulty Through Rising Expectations

### Era-Based Pressure Increases

Instead of artificial difficulty modifiers, challenge comes from **structural expectation inflation**:

**Era Progression Effects**:
- **Tribal Era**: Base Expectation +0 (survival-focused, low expectations)
- **Classical Era**: Base Expectation +10 (organized society, higher standards)
- **Imperial Era**: Base Expectation +20 (sophisticated civilization, imperial grandeur expected)
- **Late Imperial**: Base Expectation +30 (decadent period, massive entitlement)

**Technology-Driven Expectations**:
- Aqueducts → Urban strata expect better infrastructure
- Professional armies → Military strata expect higher pay and equipment standards
- Currency systems → Economic strata expect stable monetary policy
- Legal codes → All strata expect consistent justice and legal protection

**Result**: Players must continuously expand Influence sources (territory, buildings, reforms, trade) just to maintain political stability. **Standing still becomes the ultimate enemy.**

---

## User Experience: One-Glance Politics

### Main UI Integration

**Three-Icon Status Bar**:
1. **Eagle (Military)** - Color indicates highest Discontent among Military strata
2. **Mercury (Economic)** - Color indicates highest Discontent among Economic strata  
3. **Fasces (Civic)** - Color indicates highest Discontent among Civic strata

**Color Coding**:
- **Green**: All strata content (Discontent <30)
- **Yellow**: Some tension (Discontent 30-59)
- **Orange**: Active plotting (Discontent 60-79)
- **Red**: Crisis level (Discontent 80-100)

**Hover Expansion**: Shows mini-pie chart of top 3 strata in each axis with current Discontent levels

**Click Interaction**: Opens the most urgent active plot for that axis, or detailed strata breakdown if no plots active

### Information Hierarchy

**Essential Information** (always visible): Three-icon status bar
**Important Information** (hover): Current top strata and Discontent levels  
**Detailed Information** (click): Full strata breakdowns, plot details, Institutional Memory
**Optional Information** (separate screens): Historical trends, detailed Influence/Expectation calculations

---

## Implementation Phases

### Phase 1: Core System (Launch Target)
**Strata Count**: 6 total (2 per axis) for Roman Republic
- **Military**: Legionary Veterans, Citizen Militias
- **Economic**: Urban Artisans, Rural Landowners  
- **Civic**: City Plebs, Country Peasants

**Plot Templates**: 18 total (3 per stratum)
**Context Tags**: 5 government types, 4 economic systems
**Institutional Memory**: Full system with 50-entry limit

### Phase 2: Cultural Expansion
**Additional Strata**: Barbarian, Greek, Eastern cultural variants
**New Plot Templates**: Culture-specific political stories
**Advanced Context Tags**: Religious systems, military traditions, administrative structures

### Phase 3: Temporal Expansion  
**Era Transitions**: Mechanics for moving between historical periods
**Dynamic Strata Evolution**: How Republican strata transform into Imperial ones
**Legacy Systems**: How previous era decisions affect new period politics

### Phase 4: Character Integration
**Strata Leaders**: Named personalities representing each group
**Personal Relationships**: Individual interactions affecting group dynamics
**Succession Crises**: Leadership changes creating political opportunities/dangers

---

## Success Metrics

### The System Works When:
1. **Players think dynastically**: Considering 10+ year consequences of current decisions
2. **Political trade-offs feel authentic**: No obviously "correct" choice for complex situations
3. **Emergent historical parallels**: Players naturally encounter situations resembling real historical crises
4. **Replayability through politics**: Different political approaches create genuinely different gameplay experiences
5. **Intuitive complexity**: System feels deep but not overwhelming or arbitrary

### The System Fails If:
1. **Micromanagement creep**: Players spend more time optimizing approval than making strategic decisions
2. **Single-strategy dominance**: One approach to political management clearly superior to all others
3. **Arbitrary feeling**: Strata reactions seem random or disconnected from player actions
4. **Complexity overload**: Players can't understand cause-and-effect relationships
5. **Ahistorical outcomes**: System regularly produces situations that feel disconnected from ancient world realities

---

## Integration with Future Systems

### Governor System Preparation
- **Delegation Framework**: Current direct-rule mechanics become templates for provincial management
- **Political Autonomy**: Governors handle local strata relationships within parameters set by central authority
- **Reporting Structure**: Provincial political situations summarized and elevated to player attention as needed
- **Central vs Local**: Some strata remain centrally managed (Praetorian Guard) while others become provincial (Local Landowners)

### Cultural System Integration
- **Strata Templates**: Different cultures generate different available strata combinations
- **Cultural Modifiers**: Same actions have different political effects in different cultural contexts
- **Integration Mechanics**: How conquered peoples' strata merge with or remain separate from conqueror strata
- **Cultural Evolution**: How long-term rule changes available strata over generations

---

## Design Philosophy

This system embodies three core principles:

**Authentic Complexity**: Political challenges emerge from realistic historical pressures rather than abstract game mechanics. The complexity comes from managing real constituencies with real interests, not from optimizing arbitrary numbers.

**Consequential Decision-Making**: Every choice creates long-term obligations that constrain future options. Players must think like actual rulers balancing competing interests and honoring past commitments.

**Emergent Narrative**: The most memorable political moments should arise naturally from the intersection of player decisions, strata reactions, and historical context - not from scripted events or predetermined story arcs.

The goal is to capture the essence of ancient political leadership: the constant balancing act between competing groups, the weight of past promises, and the inexorable pressure of rising expectations as civilizations grow more sophisticated and demanding.

---

*This system forms the beating heart of the game - every other mechanic should connect to and be influenced by these political dynamics. When players remember their campaigns years later, they should recall the political crises they navigated, the constituencies they cultivated, and the long-term consequences of the promises they made.*