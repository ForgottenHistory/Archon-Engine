# Ambitions System - Detailed Design

## Overview

The Ambitions system provides **dynamic, contextual goals** that guide player strategy while creating emergent storytelling opportunities. Unlike static mission trees that every player experiences identically, ambitions are generated based on your empire's unique situation, interest group priorities, and world events.

This system ensures players always have meaningful objectives while surfacing opportunities they might otherwise miss, creating the feeling of a living world that responds to your actions and presents new possibilities.

---

## Core Philosophy

### Dynamic Goal Generation
- **Context-Aware**: Ambitions only appear when they're actually achievable and relevant
- **Emergent**: Your unique playthrough generates unique opportunities
- **Responsive**: The world reacts to your success/failure by presenting new ambitions

### Information as Gameplay
- **Opportunity Spotting**: Surface strategic possibilities the player might miss
- **Timing Awareness**: Alert to limited-time windows (weak neighbors, succession crises)
- **Strategic Guidance**: Help players understand what's possible with their current resources

### Interest Group Integration
- **Political Agenda**: Groups actively propose ambitions that advance their interests
- **Competing Visions**: Different groups suggest conflicting strategic directions
- **Buy-In Rewards**: Supporting a group's preferred ambition increases their approval

---

## Ambition Categories

### Historical & Cultural Ambitions
*Pre-defined goals based on your civilization's historical trajectory and cultural identity*

#### **Roman Examples**:
- **"Unite the Italian Peninsula"**: Control all provinces in Italia region
  - *Requirements*: Be Rome, control at least 3 Italian provinces
  - *Rewards*: "Italian Confederacy" permanent modifier (+25% manpower from Italian provinces)
  - *Unlocks*: "Mare Nostrum", "Gallic Conquest" ambitions

- **"Mare Nostrum"**: Control all coastline of Mediterranean Sea
  - *Requirements*: Complete "Unite the Italian Peninsula", have 50+ provinces
  - *Rewards*: Massive prestige, "Mediterranean Empire" modifier
  - *Interest Groups*: Military +30, Economic +20, Population +10

#### **Greek City-State Examples**:
- **"Hegemon of Hellas"**: Lead a confederation of all Greek city-states
  - *Requirements*: Be Greek culture, have alliance with 5+ Greek cities
  - *Rewards*: Ability to call Pan-Hellenic assemblies, defensive alliance bonuses
  - *Unlocks*: "New Alexander", "Hellenistic Revival" ambitions

#### **Celtic Tribal Examples**:
- **"High King of the Celts"**: Unite all Celtic tribes under your leadership
  - *Requirements*: Be Celtic culture, control 15+ Celtic provinces
  - *Rewards*: "Celtic Unity" modifier, special tribal confederation mechanics

### Interest Group Proposed Ambitions
*Goals suggested by your political factions based on their priorities and current situation*

#### **Military Faction Proposals**:

**"Conquer the Wealthy [City Name]"**:
- *Triggered By*: Adjacent rich city, military approval >50, recent military victory
- *Proposed By*: Successful general or military faction leader
- *Requirements*: Declare war within 2 years, siege target city
- *Rewards*: Economic boost, military prestige, potential for follow-up ambitions
- *Supporting Groups*: Military +20, Economic +10 (loot), Population -5 (war costs)

**"Build the Greatest Army"**:
- *Triggered By*: Military approval >75, sufficient treasury reserves
- *Requirements*: Maintain army size 50% larger than nearest rival for 5 years
- *Rewards*: "Military Supremacy" modifier, intimidation diplomatic options
- *Supporting Groups*: Military +25, Economic -15 (expensive), Population -10 (conscription)

**"Avenge Our Defeat"**:
- *Triggered By*: Losing a major war, military approval <25
- *Requirements*: Defeat the nation that humiliated you within 10 years
- *Rewards*: Massive military approval boost, removes "Humiliated" penalties
- *Supporting Groups*: Military +35, Population +15 (national pride)

#### **Economic Faction Proposals**:

**"Control the [Trade Good] Trade"**:
- *Triggered By*: Economic approval >60, contact with regions producing valuable trade goods
- *Requirements*: Control 60% of provinces producing specific trade good
- *Rewards*: "Trade Monopoly" modifier for that good, increased diplomatic weight
- *Supporting Groups*: Economic +30, Population +5 (prosperity)

**"Establish Trade Route to [Distant Region]"**:
- *Triggered By*: Discovery of wealthy distant civilization, naval technology
- *Requirements*: Build trade posts, negotiate trade agreements, protect sea lanes
- *Rewards*: Permanent income boost, access to exotic goods, cultural exchange
- *Supporting Groups*: Economic +25, Military +5 (naval expansion)

**"Build Wonder of the Ancient World"**:
- *Triggered By*: Economic prosperity, high approval from all groups, cultural achievements
- *Requirements*: Massive resource investment over multiple years
- *Rewards*: Permanent prestige boost, tourism income, cultural influence
- *Supporting Groups*: Economic +15, Population +20 (civic pride), Military -10 (resource diversion)

#### **Population Faction Proposals**:

**"Secure Our Borders"**:
- *Triggered By*: Recent raids, population approval <50, border instability
- *Requirements*: Build fortifications, maintain border garrisons, establish buffer zones
- *Rewards*: "Secure Borders" modifier, reduced unrest, population growth
- *Supporting Groups*: Population +25, Military +15 (defensive focus)

**"Great Public Works"**:
- *Triggered By*: Population approval >70, economic prosperity, urban development
- *Requirements*: Build aqueducts, baths, theaters in major cities
- *Rewards*: "Prosperous Cities" modifier, cultural development, happiness boost
- *Supporting Groups*: Population +30, Economic +10 (construction jobs)

**"Cultural Renaissance"**:
- *Triggered By*: High cultural development, contact with advanced civilizations
- *Requirements*: Patronize arts, build libraries and schools, attract foreign scholars
- *Rewards*: Technological advancement bonuses, diplomatic prestige, cultural influence
- *Supporting Groups*: Population +20, Economic +15 (cultural economy)

### Event-Driven Ambitions
*Special goals triggered by random events, world developments, or unique circumstances*

#### **Opportunistic Ambitions**:

**"The Weak Neighbor"**:
- *Triggered By*: Adjacent nation suffering civil war, succession crisis, or major defeat
- *Requirements*: Declare war within 1 year while they're weakened
- *Time Limit*: 2 years (opportunity window closes when they recover)
- *Rewards*: Easy conquest, reduced war exhaustion, potential vassal creation

**"The Great Alliance"**:
- *Triggered By*: Major threat emerges (barbarian migration, expanding empire)
- *Requirements*: Form defensive coalition with 3+ nations
- *Rewards*: "United We Stand" modifier, shared military bonuses, cultural exchange

**"The Succession Crisis"**:
- *Triggered By*: Allied ruler dies without clear heir
- *Requirements*: Support favored candidate, potentially intervene militarily
- *Rewards*: Favorable new ruler, increased influence, potential personal union

#### **Crisis Response Ambitions**:

**"Survive the Storm"**:
- *Triggered By*: Multiple simultaneous crises (plague, famine, invasion)
- *Requirements*: Maintain stability for 3 years during crisis period
- *Rewards*: "Weathered the Storm" modifier, increased group loyalty, recovery bonuses

**"The Phoenix Rising"**:
- *Triggered By*: Recovering from near-total defeat (lost 75% of territory)
- *Requirements*: Reclaim 50% of lost territory within 15 years
- *Rewards*: "Phoenix Empire" modifier, rapid development bonuses, legendary status

#### **Discovery Ambitions**:

**"The New World"**:
- *Triggered By*: Discovery of previously unknown lands or peoples
- *Requirements*: Establish diplomatic contact, found colonies, or conquer territories
- *Rewards*: Access to new resources, technologies, or military techniques

**"The Lost Library"**:
- *Triggered By*: Discovering ruins of ancient civilization with preserved knowledge
- *Requirements*: Fund archaeological expedition, translate ancient texts
- *Rewards*: Significant technological advancement, cultural bonuses

---

## Ambition Mechanics

### Proposal System

#### **Ambition Generation**:
1. **Context Check**: System evaluates current situation (military strength, economic status, recent events)
2. **Group Priority**: Interest groups propose ambitions aligned with their agendas
3. **Feasibility Assessment**: Only realistic ambitions are presented
4. **Player Notification**: New ambitions appear with full details and group support

#### **Presentation Format**:
```
AMBITION PROPOSED: "Conquer Syracuse"
Proposed by: General Marcus Aurelius (Military Faction)

"My lord, Syracuse sits fat and wealthy just across the sea. Their navy is weak, 
their walls are old, and their allies are few. One swift campaign could bring 
their riches into our treasury and their skilled artisans to our cities."

Requirements:
- Declare war on Syracuse within 2 years
- Successfully siege Syracuse city
- Control Syracuse and 2 adjacent provinces

Estimated Difficulty: Moderate
Time Frame: 2-4 years

Rewards:
- 5000 gold from Syracusan treasury
- +15 Military approval (glorious victory)
- +10 Economic approval (new wealth)
- Access to Syracusan ship-building techniques

Supporting Groups: Military (+25), Economic (+15)
Opposing Groups: Population (-10, war weariness)

[Accept] [Reject] [Consider Later]
```

### Active Pursuit Phase

#### **Progress Tracking**:
- **Clear Milestones**: Break complex ambitions into observable steps
- **Visual Progress**: Show completion percentage and next steps
- **Bonus Effects**: Pursuing ambitions provides minor bonuses even before completion

#### **Multiple Ambition Management**:
- **Primary Ambition**: Full bonuses, maximum interest group approval
- **Secondary Ambitions**: Reduced bonuses, but still meaningful
- **Ambition Slots**: Limited number based on administrative capacity
  - Early Game: 1 primary ambition
  - Mid Game: 1 primary + 1 secondary
  - Late Game: 1 primary + 2 secondary

#### **Resource Investment**:
- **Political Capital**: Pursuing ambitious goals requires ongoing political attention
- **Economic Investment**: Many ambitions require gold, materials, or infrastructure
- **Military Commitment**: Conquest ambitions tie up armies and resources

### Completion & Consequences

#### **Success Rewards**:
- **Immediate Benefits**: Gold, territory, buildings, troops, or special units
- **Permanent Modifiers**: Long-lasting bonuses that define your empire's character
- **Interest Group Reactions**: Massive approval boosts from supporting groups
- **Unlocked Opportunities**: Successful ambitions often unlock new, more ambitious goals
- **Prestige & Reputation**: Other nations react to your achievements

#### **Partial Success**:
- **Modified Rewards**: Reduced but still meaningful benefits
- **Learning Experience**: Bonuses to similar future attempts
- **Group Reactions**: Mixed approval changes based on how well you did

#### **Failure Consequences**:
- **Group Disappointment**: Supporting groups lose approval
- **Resource Loss**: Wasted investments in failed attempts
- **Opportunity Cost**: Time and resources that could have been used elsewhere
- **Alternative Ambitions**: Failure sometimes opens different strategic paths

### Abandonment & Modification

#### **Changing Priorities**:
- **Peaceful Abandonment**: Stop pursuing ambition with minor approval penalties
- **Crisis Abandonment**: Major events (invasions, natural disasters) allow abandonment without penalty
- **Modification Options**: Sometimes ambitions can be adjusted rather than abandoned

#### **Political Costs**:
- **Broken Promises**: Groups that proposed abandoned ambitions remember
- **Wasted Investment**: Resources spent on abandoned ambitions are lost
- **Reputation Impact**: Pattern of abandoning ambitions affects how groups trust future proposals

---

## Dynamic Interaction Systems

### Cascading Ambitions

#### **Success Breeds Success**:
- **"Unite Italia" → "Mare Nostrum" → "Conquer Gaul"**: Each success unlocks greater ambitions
- **"Control Tin Trade" → "Bronze Monopoly" → "Metalworking Empire"**: Economic success creates industrial ambitions
- **"Secure Borders" → "Regional Hegemon" → "Great Empire"**: Defensive success leads to expansion

#### **Failure Creates New Paths**:
- **Failed "Conquer Egypt" → "Defend Against Egyptian Retaliation"**: Failed aggression creates defensive challenges
- **Failed "Trade Monopoly" → "Pirate Suppression" → "Naval Supremacy"**: Economic failure might lead to military solutions

### Interest Group Competition

#### **Competing Proposals**:
- **Military**: "Conquer the Gold Mines of [Region]"
- **Economic**: "Negotiate Trade Agreement for Gold Access"
- **Population**: "Develop Our Own Gold Deposits"

All three achieve similar goals through different methods, creating meaningful strategic choices.

#### **Group Coalition Building**:
- **Military + Population**: "Defensive Alliance" ambitions that satisfy both security and peace desires
- **Economic + Military**: "Trade Route Protection" ambitions that combine profit and conquest
- **Economic + Population**: "Infrastructure Development" ambitions that boost prosperity and happiness

### World Response System

#### **AI Reactions**:
- **Neighboring Ambitions**: Other nations pursue their own ambitions, creating dynamic world
- **Reactive Ambitions**: AI nations develop counter-ambitions to player success
- **Cooperative Opportunities**: Some ambitions encourage or require cooperation with AI nations

#### **Escalating Competition**:
- **Arms Race**: Player military ambitions trigger AI military responses
- **Economic Competition**: Trade ambitions create economic rivalry with other powers
- **Cultural Influence**: Cultural ambitions spark counter-cultural movements

---

## Integration with Other Systems

### Policy System Integration

#### **Ambition-Policy Synergy**:
- **Military Ambitions** + **Professional Army Policy** = Faster completion, better rewards
- **Economic Ambitions** + **Trade Supremacy Policy** = Enhanced trade route ambitions
- **Cultural Ambitions** + **Assimilation Policy** = Cultural integration opportunities

#### **Policy Prerequisites**:
- Some advanced ambitions require specific policies to be viable
- **"Imperial Administration"** ambition needs **State Control** policy
- **"Cultural Hegemony"** ambition needs **Assimilation** or **Cultural Tolerance** policies

### Governor System Integration (Future)

#### **Governor Proposals**:
- Regional governors propose local ambitions based on their territory's situation
- **"Develop the Egyptian Trade Hub"** proposed by Egyptian governor
- **"Pacify the Gallic Tribes"** proposed by Gallic frontier governor

#### **Implementation Assistance**:
- Governors help implement ambitions in their regions
- Military governors excel at conquest ambitions
- Economic governors excel at development ambitions
- Cultural governors excel at integration ambitions

### Technology & Cultural Integration

#### **Technological Unlocks**:
- New technologies enable new types of ambitions
- **Navigation Advances** → **"Ocean Exploration"** ambitions
- **Military Engineering** → **"Siege Mastery"** ambitions
- **Administrative Systems** → **"Bureaucratic Empire"** ambitions

#### **Cultural Variations**:
- Same ambition type has different flavors for different cultures
- **Roman "Mare Nostrum"** vs **Carthaginian "Commercial Empire"** vs **Greek "Hellenistic Revival"**
- Each feels authentic to that civilization's historical character

---

## Player Experience Design

### Discovery & Surprise
- **Unexpected Opportunities**: Ambitions should sometimes surprise players with new possibilities
- **Emergent Storytelling**: Unique combinations create memorable narratives
- **"I Never Thought of That"**: System should suggest strategies players wouldn't consider

### Clear Communication
- **Transparent Requirements**: Players always understand what they need to do
- **Progress Feedback**: Clear indication of how close they are to completion
- **Consequence Preview**: Understanding of rewards and risks before committing

### Strategic Depth
- **Meaningful Choices**: All ambitions should be viable in different circumstances
- **Long-term Planning**: Ambition chains reward strategic thinking
- **Risk/Reward Balance**: High-risk ambitions offer proportionally high rewards

### Avoiding Pitfalls

#### **Choice Paralysis**:
- Limited active ambitions force decision-making
- Clear categorization helps players understand options
- Guidance from interest groups provides direction

#### **Optimal Path Problem**:
- Multiple viable routes to similar goals
- Situational advantages for different approaches
- Cultural and preference variations prevent one-size-fits-all solutions

#### **Micromanagement**:
- Ambitions run largely automatically once accepted
- Player intervention only needed for major decisions or crises
- Clear next-step guidance prevents confusion

---

## Success Metrics

The Ambitions system succeeds when:

1. **Players Always Know What to Do**: Clear, appealing goals are always available
2. **Emergent Storytelling**: Unique ambition combinations create memorable campaigns
3. **Strategic Planning**: Players think multiple ambitions ahead
4. **Political Integration**: Ambition choices consider interest group politics
5. **World Reactivity**: AI and events respond meaningfully to player ambitions

The system fails if:

1. **Ignored Optimization**: Players find ways to game the system without engaging with content
2. **Choice Overwhelm**: Too many ambitions create analysis paralysis
3. **Meaningless Variety**: All ambitions feel mechanically identical
4. **Political Disconnection**: Ambitions ignore interest group and policy systems
5. **Predictable Outcomes**: System becomes mechanical rather than emergent

---

## Implementation Priority

### Phase 1: Basic Framework
- Ambition proposal, acceptance, and completion mechanics
- Integration with interest group approval system
- Historical/cultural ambitions for major civilizations

### Phase 2: Dynamic Generation
- Event-driven ambition creation
- Interest group proposal system
- Basic cascading ambition chains

### Phase 3: Advanced Features
- Multiple ambition management
- Complex ambition interactions
- AI ambition systems

### Phase 4: Polish & Expansion
- Full governor system integration
- Advanced cultural variations
- Complex cascading chains and world reactions

---

*The Ambitions system should make every campaign feel like a unique historical epic, where the player's choices and the world's reactions create a personalized narrative of empire-building in the ancient Mediterranean.*