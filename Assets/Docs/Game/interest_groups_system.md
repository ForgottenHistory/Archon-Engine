# Interest Groups System - Detailed Design

## Overview

The Interest Groups system is the **core political engine** of the game. Unlike traditional grand strategy games where you manage abstract concepts like "stability" or arbitrary "monarch points," this system simulates the real political constituencies that ancient rulers had to balance.

Every major decision in the game affects these groups, creating a web of political consequences that feels organic and forces players to think like actual rulers rather than spreadsheet optimizers.

---

## Core Interest Groups

### Military Faction
**Who They Are**: Professional soldiers, generals, veterans, warrior aristocracy, military engineers, and anyone whose livelihood depends on warfare and martial glory.

**What They Want**:
- **Glorious Conquests**: Regular military campaigns that bring honor and loot
- **Military Investment**: Better equipment, fortifications, training facilities
- **Veteran Care**: Rewards for retired soldiers, land grants, pensions
- **Military Prestige**: Recognition of martial achievements, triumph parades
- **Strong Defense**: Border security, military preparedness

**What They Hate**:
- **Long Periods of Peace**: Without campaigns, military careers stagnate
- **Military Cuts**: Reducing army size or military spending
- **Humiliating Defeats**: Lost battles damage their reputation and honor
- **Diplomatic Solutions**: Negotiating when they believe force would work better
- **Foreign Military Advisors**: Outside interference in military matters

**Political Behavior**:
- **High Approval (75-100)**: Volunteer for dangerous missions, provide tactical advice, recruit more soldiers
- **Moderate Approval (25-74)**: Fulfill basic duties, some grumbling about leadership
- **Low Approval (-24 to 24)**: Reluctant service, slower recruitment, minor insubordination
- **Very Low Approval (-75 to -25)**: Refuse dangerous missions, demand leadership changes
- **Crisis Level (-100 to -76)**: Military coup attempts, desertion, joining rebels

### Economic Faction  
**Who They Are**: Merchants, tax collectors, urban artisans, trade guild leaders, bankers, port masters, and anyone whose wealth comes from commerce and production.

**What They Want**:
- **Trade Expansion**: New trade routes, commercial agreements, market access
- **Economic Infrastructure**: Roads, ports, markets, monetary systems
- **Legal Protection**: Contract enforcement, property rights, anti-piracy measures
- **Tax Efficiency**: Fair and predictable taxation that doesn't kill commerce
- **Urban Development**: Cities, workshops, commercial districts

**What They Hate**:
- **Excessive Taxation**: High taxes that reduce trade profits
- **Trade Disruption**: Wars that interfere with commerce, trade embargos
- **Military Prioritization**: Spending all resources on armies instead of infrastructure
- **Economic Instability**: Currency debasement, hyperinflation, trade wars
- **Rural Focus**: Policies that favor agriculture over commerce

**Political Behavior**:
- **High Approval (75-100)**: Increased trade efficiency, voluntary tax contributions, economic expansion
- **Moderate Approval (25-74)**: Normal tax collection, routine trade activities
- **Low Approval (-24 to 24)**: Tax avoidance, reduced trade, economic hoarding
- **Very Low Approval (-75 to -25)**: Capital flight, black market activities, funding opposition
- **Crisis Level (-100 to -76)**: Economic embargo, funding rebellions, trade with enemies

### Population Faction
**Who They Are**: Farmers, rural peasants, urban workers, provincial peoples, slaves, and the common masses who form the backbone of society.

**What They Want**:
- **Basic Prosperity**: Food security, reasonable taxes, economic opportunities
- **Peace and Stability**: No disruptive wars, protection from bandits, law and order
- **Cultural Respect**: Recognition of local customs, religious freedom
- **Fair Treatment**: Justice, reasonable laws, protection from exploitation
- **Local Autonomy**: Some degree of self-governance, traditional rights

**What They Hate**:
- **Heavy Conscription**: Forced military service that disrupts families and farming
- **Crushing Taxation**: Taxes so high they can't maintain basic living standards
- **Cultural Suppression**: Forced assimilation, religious persecution
- **Neglect**: Being ignored while elites prosper, lack of basic services
- **Foreign Rule**: Being treated as conquered subjects rather than citizens

**Political Behavior**:
- **High Approval (75-100)**: Higher birth rates, voluntary support, cultural integration
- **Moderate Approval (25-74)**: Normal productivity, routine compliance with laws
- **Low Approval (-24 to 24)**: Reduced productivity, minor tax resistance, unrest
- **Very Low Approval (-75 to -25)**: Strikes, protests, migration to other regions
- **Crisis Level (-100 to -76)**: Open revolt, supporting foreign invaders, massive unrest

---

## Advanced Interest Group Mechanics

### Approval Dynamics

**Base Approval Decay**:
- All approval ratings slowly drift toward 0 over time (representing natural political entropy)
- Extreme ratings decay faster (it's hard to maintain very high or very low approval)
- Decay rate: ±1 point per month, with faster decay for ratings above ±50

**Approval Momentum**:
- Recent actions have stronger effects than old ones
- Multiple similar actions can create momentum (military victories stack, multiple tax increases compound)
- Contradictory actions create confusion and reduced effectiveness

**Cross-Group Effects**:
- Very high approval in one group can create resentment in others
- Crisis-level disapproval in any group affects all others negatively
- Balanced approval across groups provides stability bonuses

### Group Interaction Matrices

**Military ↔ Economic**:
- **Conflict Areas**: Resource allocation (armies vs infrastructure), war vs trade priorities
- **Common Ground**: Need for secure trade routes, anti-piracy, territorial expansion for resources
- **Synergies**: Military conquest opening new markets, economic prosperity funding better armies

**Military ↔ Population**:
- **Conflict Areas**: Conscription, war casualties, military taxation
- **Common Ground**: Border defense, law and order, protection from raiders
- **Synergies**: Military victories bringing prestige and loot, veteran settlement programs

**Economic ↔ Population**:
- **Conflict Areas**: Urban vs rural priorities, labor conditions, tax burden distribution  
- **Common Ground**: Basic prosperity, infrastructure, law and order
- **Synergies**: Economic growth creating jobs, population growth expanding markets

### Regional Variations

**Core Provinces**: Groups have standard behavior and influence
**Recently Conquered**: Population faction has additional "Foreign Rule" penalty, other groups may have loyalty questions
**Border Regions**: Military faction gains additional influence, Population faction values security more
**Trade Hubs**: Economic faction has enhanced influence, generates more wealth but also more demands
**Cultural Minorities**: Population faction has different priorities, may resist central policies

---

## Gameplay Integration

### Decision Framework
Every major player action should be evaluated through the interest group lens:

1. **Immediate Effects**: Which groups are directly affected and how?
2. **Secondary Effects**: What indirect consequences might emerge?
3. **Long-term Implications**: How does this change the political landscape?
4. **Regional Variations**: Do different provinces react differently?

### Example Decision: "Declare War on Neighboring Kingdom"

**Immediate Effects**:
- Military: +15 (opportunity for glory and advancement)
- Economic: -10 (trade disruption, war costs)
- Population: -8 (fear of conscription and casualties)

**Secondary Effects** (3-6 months later):
- If war goes well: Military +5 more, Population -3 more (casualties)
- If war goes poorly: Military -10, Economic -5 more (wasted resources), Population -10 (anger at losses)
- Border provinces: Population +3 (security concerns overriding peace preference)

**Long-term Implications**:
- Successful conquest: All groups eventually benefit from increased territory and resources
- Failed conquest: Long-term trust damage, potential political crisis
- Extended war: Economic faction may actively oppose continuation

### Approval Thresholds & Consequences

**Approval Ranges**:
```
75-100:  Enthusiastic Support   (Active help, bonuses, voluntary contributions)
25-74:   General Support        (Normal cooperation, standard efficiency)  
-24-24:  Neutral/Mixed          (Reluctant cooperation, some resistance)
-75-25:  Opposition             (Active resistance, penalties, obstruction)
-100-76: Crisis                 (Open hostility, rebellion, coup attempts)
```

**Mechanical Effects**:
- **Military Approval**: Affects recruitment speed, army morale, siege effectiveness, revolt suppression
- **Economic Approval**: Affects tax collection efficiency, trade income, building construction speed
- **Population Approval**: Affects manpower availability, provincial unrest, cultural integration speed

---

## Future Expansion Features

### Secondary Interest Groups (Phase 2)
Once the core system is stable, additional groups can be added:

**Religious Faction**: Priests, temple officials, devout believers
- Wants: Religious buildings, moral policies, persecution of heretics
- Conflicts with: Cultural tolerance, secular policies, foreign religions

**Cultural Minorities**: Conquered peoples, ethnic enclaves, tribal groups  
- Wants: Cultural autonomy, traditional rights, representation
- Conflicts with: Assimilation policies, cultural suppression, discrimination

**Provincial Elites**: Local aristocrats, regional governors, feudal lords
- Wants: Local autonomy, traditional privileges, regional development
- Conflicts with: Centralization, direct rule, standardization

### Dynamic Group Formation (Phase 3)
Groups could split or merge based on circumstances:
- **Military Schism**: Army vs Navy factions during major maritime conflicts
- **Economic Split**: Urban merchants vs rural landowners during economic transitions
- **Population Division**: Core citizens vs conquered subjects vs allied peoples

### Interest Group Leaders (Phase 4)
Individual personalities representing each group:
- **Named Characters**: Famous generals, wealthy merchants, popular tribunes
- **Character Traits**: Aggressive, cautious, corrupt, idealistic, ambitious
- **Personal Relationships**: Some leaders work well together, others are rivals
- **Succession**: When leaders die or retire, their replacements may have different priorities

---

## Integration with Other Systems

### Governor System Integration
When governors are added, they will have their own interest group management:
- **Governor Loyalties**: Each governor favors certain interest groups based on background
- **Local Politics**: Provincial interest groups may differ from national ones
- **Policy Implementation**: Governors interpret central policies through their group biases
- **Reporting System**: Governors provide updates on local interest group situations

### Cultural System Integration
Different cultures will have different interest group compositions:
- **Roman Culture**: Strong military tradition, sophisticated economic systems
- **Greek Culture**: Emphasis on urban merchants, intellectual elites
- **Barbarian Cultures**: Tribal military councils, limited economic factions
- **Eastern Cultures**: Religious hierarchies, ancient bureaucracies

### Technology & Progress Integration
Advancement unlocks new ways to satisfy interest groups:
- **Military**: Professional armies, siege engines, military engineering
- **Economic**: Banking systems, trade companies, industrial techniques
- **Population**: Public works, legal codes, cultural institutions

---

## Balancing Considerations

### Avoiding the "Optimization Trap"
- **No Perfect Solutions**: Most decisions should have trade-offs, not clear winners
- **Context Dependency**: The "right" choice should depend on current situation, not universal rules
- **Cascading Effects**: Today's solutions create tomorrow's problems

### Preventing Exploitation
- **Diminishing Returns**: Repeatedly doing the same thing to please a group becomes less effective
- **Opportunity Costs**: Resources spent pleasing one group can't be used elsewhere
- **Dynamic Expectations**: Successful groups expect more, creating escalating demands

### Maintaining Engagement
- **Clear Feedback**: Players should understand why groups react as they do
- **Meaningful Choices**: All options should be viable in different circumstances  
- **Emergent Stories**: The system should create memorable political situations

---

## Success Metrics

The Interest Group system succeeds when:
1. **Players think politically**: They consider group reactions before taking action
2. **Choices feel meaningful**: No decision is purely mechanical or obvious
3. **Stories emerge**: Players develop narratives about their political management
4. **Replay value**: Different approaches to group management create different experiences
5. **Authentic feel**: The system captures the essence of ancient political leadership

The system fails if:
1. **Players ignore it**: Group mechanics become irrelevant optimization puzzles  
2. **One strategy dominates**: There's an obviously "correct" way to manage all groups
3. **Micromanagement**: Players spend more time managing approval than playing the game
4. **Arbitrary feeling**: Group reactions seem random or unfair
5. **Complexity overload**: The system becomes too complicated to understand or enjoy

---

*This system forms the political heart of the game - every other mechanic should connect to and be influenced by these interest group dynamics.*