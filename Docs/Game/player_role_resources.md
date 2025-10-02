# Player Role, Resources & Actions - Design Document

## The Player's Role

You are the **guiding spirit** of your civilization - not quite a single ruler, not quite an immortal god-king, but the continuous decision-making consciousness that shapes your empire across centuries. Think of yourself as the embodiment of your civilization's ambition and institutional memory.

This abstraction explains why:
- You survive succession crises and ruler deaths
- You can make long-term plans spanning generations
- Different government types don't fundamentally change your control
- You remember promises and debts even when individual characters wouldn't

---

## Core Resources

### Primary Resources
These are physical, tangible assets that directly enable actions:

| Resource | Generation | Primary Uses | Storage |
|----------|------------|--------------|---------|
| **Gold** | Taxes, trade, tribute, loot | Military upkeep, construction, Stratum payments, diplomacy | Treasury (no cap) |
| **Manpower** | Population growth, migration | Recruiting units, colonization | Pool (cap based on population) |
| **Trade Goods** | Provincial production | Bonuses when controlled, income when sold | Automatic (not stockpiled) |

### Political Resources
These represent your governmental capacity and influence:

| Resource | Generation | Primary Uses | Storage |
|----------|------------|--------------|---------|
| **Political Capital** | 5/month base (modified by stability, government, active ambitions) | Postpone ambitions, emergency actions, force decisions | 0-100 pool |
| **Diplomatic Influence** | 2/month base (modified by reputation, trade) | Treaties, demands, foreign manipulation | 0-50 pool |
| **Administrative Capacity** | Fixed limit based on government + tech | Limits simultaneous ambitions, policies | Not a pool - a ceiling |

### Derived States
These emerge from your management of resources and represent empire health:

| State | Range | What It Affects | How It Changes |
|-------|-------|-----------------|----------------|
| **Stability** | -3 to +3 | Tax efficiency, revolt risk, PC generation | Events, decisions, Stratum satisfaction |
| **War Exhaustion** | 0-20 | Military morale, maintenance costs, unrest | War duration, battles, peace |
| **Legitimacy** | 0-100 | All Stratum Discontent modifier, stability cap | Succession, victories, time |
| **Corruption** | 0-100 | All costs increase, efficiency decrease | Too many territories, low admin capacity |

---

## Available Actions

### Always Available Actions
These require only core resources and time:

| Action | Cost | Duration | Effect |
|--------|------|----------|--------|
| **Construct Building** | Gold (varies) | 6-24 months | Permanent provincial modifier |
| **Recruit Regiment** | 100 gold, 1000 manpower | 3 months | New military unit |
| **Move Units** | Free | Distance-based | Repositions forces |
| **Improve Relations** | 1 Diplomatic Influence | Continuous | +1 opinion/month with target |
| **Fabricate Claim** | 2 Diplomatic Influence | 12-24 months | Casus belli on target province |

### Political Capital Actions
These require spending your limited political attention:

| Action | PC Cost | Effect | Cooldown |
|--------|---------|--------|----------|
| **Force Through Law** | 20-30 | Ignore Stratum objections to policy | None |
| **Postpone Ambition** | 5 | Delay Stratum demand by 1 year | Per ambition |
| **Emergency Conscription** | 15 | Instant 5000 manpower, -1 stability | 5 years |
| **Placate Stratum** | 10 | -20 Discontent for target Stratum | 2 years per Stratum |
| **Purge Leadership** | 25 | Reset Stratum Expectation to base, +30 Discontent | 10 years per Stratum |
| **Declare Emergency** | 10 | +50% action speed, all Strata +10 Discontent | 5 years |
| **Grant Amnesty** | 15 | Cancel all active plots, -10 Legitimacy | 5 years |

### Diplomatic Actions
These shape your foreign relations:

| Action | Diplomatic Influence Cost | Effect | Requirements |
|--------|---------------------------|--------|--------------|
| **Propose Trade Agreement** | 5 | Establish trade route | Discovered nation |
| **Demand Tribute** | 5 | Target pays gold/month or gains CB against you | Stronger military |
| **Guarantee Independence** | 10 | Protects small nation, gains you influence | 3x their military |
| **Threaten War** | 3 | Target concedes demand or you gain CB | Military superiority |
| **Royal Marriage** | 8 | Alliance + potential inheritance | Compatible governments |

### Contextual Actions
Available only in specific situations:

**During War:**
- **Forced March**: Army moves 50% faster, takes attrition
- **Scorched Earth**: Destroy own province value to deny enemy
- **War Bonds**: Loan gold from Strata at high interest
- **Call to Arms**: All allies must join or break alliance

**During Peace:**
- **Hold Games**: -500 gold, all Strata -10 Discontent
- **Census**: -5 Administrative Capacity for 1 year, permanent tax efficiency bonus
- **Military Parade**: -2 Political Capital, Military Stratum +10 Influence
- **Trade Fair**: -300 gold, Economic Stratum +10 Influence, temporary trade bonus

**Based on Government Type:**
- **Republic**: Call Emergency Senate Session (instant policy change, -Legitimacy)
- **Monarchy**: Royal Tour (-PC generation, +Legitimacy in visited provinces)
- **Tribal**: Tribal Assembly (reset all Stratum Expectations, massive stability hit)
- **Theocracy**: Divine Mandate (convert Legitimacy to Stability)

---

## Resource Flow & Economy

### The Basic Loop
```
PROVINCES generate →
    Tax (Gold/month)
    Manpower (Bodies/month)
    Trade Goods (Resources)
    ↓
GOLD maintains →
    Army/Navy (Enable conquest & protection)
    Buildings (Boost economy & satisfaction)
    Stratum Payments (Prevent discontent)
    ↓
STABILITY affects →
    Tax Efficiency (0.5x at -3, 1.5x at +3)
    Political Capital Generation
    Revolt Risk
    ↓
POLITICAL CAPITAL enables →
    Crisis Management
    Ambition Handling
    Emergency Actions
```

### Resource Generation Modifiers

**Gold Income Modified By:**
- Stability (-50% to +50%)
- Corruption (-1% per point)
- Trade agreements (+10% per route)
- Economic buildings (+5-15% per building)
- Economic Stratum Influence (up to +20%)

**Political Capital Generation Modified By:**
- Stability (+1 per positive point)
- Government Type (Republic +2, Monarchy +0, Tribal -2)
- Active Ambitions (-20% per ambition)
- Corruption (-0.5 per 10 points)
- Administrative Efficiency (+1 per 20% efficiency)

**Manpower Recovery Modified By:**
- Provincial Development (base rate)
- Military Stratum Influence (up to +30%)
- War Exhaustion (-5% per point)
- Cultural Unity (same culture provinces +20%)

---

## Strategic Resource Management

### Early Game Priorities
- **Gold**: Critical for first armies and essential buildings
- **Political Capital**: Save for crisis moments, avoid ambition overload
- **Diplomatic Influence**: Secure 1-2 defensive alliances
- **Stability**: Keep at +1 minimum for efficiency

### Mid Game Balancing
- **Gold**: Balance military upkeep vs economic investment
- **Political Capital**: Juggle 1-2 ambitions while keeping reserve
- **Administrative Capacity**: Approach limit carefully, corruption hurts
- **War Exhaustion**: Plan peace breaks between conquests

### Late Game Challenges
- **Corruption**: Multiple solutions but all costly
- **Political Capital**: Many simultaneous demands on limited resource
- **Stratum Management**: Multiple powerful groups with conflicting demands
- **Administrative Overload**: Need reforms or governors to manage empire

---

## Action Priority Guidelines

### Crisis Response Hierarchy
1. **Active Invasions** - Military response mandatory
2. **Stratum Plots** - Address before they trigger
3. **Stability Crisis** (-2 or worse) - Immediate action needed
4. **Economic Collapse** - Can spiral if ignored
5. **Diplomatic Isolation** - Dangerous but not immediately fatal

### Political Capital Spending Priority
1. **Reserve 10-15** for emergencies always
2. **Postpone ambitions** you can't fulfill
3. **Placate Strata** approaching plot threshold
4. **Force laws** only when absolutely necessary
5. **Purges** as last resort (long-term consequences)

### Opportunity Cost Awareness
Every action has alternatives:
- Building a fort vs building a market
- Recruiting troops vs saving for buildings  
- Accepting military ambition vs economic ambition
- Using PC to postpone vs saving for emergency

---

## Resource Scarcity & Pressure Points

### What Creates Tension

**Gold Scarcity:**
- Multiple simultaneous wars drain treasury
- Building programs compete with military needs
- Stratum payments during economic downturn
- Emergency purchases (mercenaries, bribes)

**Political Capital Crunch:**
- Multiple Strata demanding action
- Active ambitions reducing generation
- Need to force unpopular decisions
- Succession crisis requiring intervention

**Administrative Overload:**
- Too many provinces for government type
- Multiple active policies straining capacity
- Corruption from overextension
- Cannot accept beneficial ambitions

### Recovery Mechanisms

| Crisis | Short-term Solution | Long-term Solution |
|--------|-------------------|-------------------|
| Gold Shortage | War Bonds, emergency taxes, loot | Economic buildings, trade routes |
| PC Deficit | Complete/abandon ambitions | Government reform, stability improvement |
| Admin Overload | Grant autonomy, release vassals | Tech advancement, government reform |
| Manpower Crisis | Emergency conscription, mercenaries | Population growth, cultural integration |

---

## The Player Experience

### You Always Have Options
Even in crisis, multiple paths exist:
- Can't afford military ambition? Negotiate postponement or accept consequences
- Treasury empty? Raise war bonds, loot, or emergency tax
- Strata rebelling? Placate, purge, or ride it out

### Every Choice Has Trade-offs
- Spend PC now vs save for emergency
- Accept ambition (tied up resources) vs refuse (political cost)
- Quick conquest (war exhaustion) vs slow buildup (opportunity loss)
- Placate Stratum (PC cost) vs let them plot (risk)

### Long-term Consequences Matter
- Today's emergency conscription is tomorrow's manpower shortage
- Purged Stratum remembers for a generation
- War bonds must be repaid with interest
- Refused ambitions affect faction trust permanently

---

*The player role is to navigate the constant tension between ambition and capacity, between what your empire wants to become and what your resources allow. Every decision creates ripples through the political and economic system, and success comes from managing these cascading consequences across centuries of growth and crisis.*