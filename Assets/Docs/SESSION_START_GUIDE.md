# Session Start Guide - Quick Reference

**Purpose:** Quick checklist to start a productive AI coding session

---

## ⚡ Quick Start (2 minutes)

```
1. Copy Log/TEMPLATE.md → Log/YYYY-MM-DD-name.md
2. Tell Claude: "Read CLAUDE.md, ARCHITECTURE_OVERVIEW.md, and Log/[last-session].md"
3. Brief Claude on today's goal
4. Start coding!
```

---

## 📋 Full Session Start Checklist

### Before You Start
- [ ] Know your goal for this session
- [ ] Have relevant architecture docs identified
- [ ] Check if any Planning/ docs need to be reviewed

### Claude's Reading Order
```
1. Assets/CLAUDE.md
   → Core rules, constraints, must-follow patterns

2. Docs/Engine/ARCHITECTURE_OVERVIEW.md
   → Current system status, quick reference

3. Docs/Log/[most-recent-session].md
   → What we tried last, decisions made, next steps

4. Docs/Engine/[specific-doc].md (as needed)
   → System-specific details for today's work
```

### Your Session Prep
- [ ] Create new log file: `Log/YYYY-MM-DD-description.md`
- [ ] Copy from TEMPLATE.md
- [ ] Fill in "Session Goal" section
- [ ] Note any context from previous sessions

---

## 🎯 Effective Prompts

### Starting the Session

**✅ Good:**
```
"Read CLAUDE.md, ARCHITECTURE_OVERVIEW.md, and Log/2025-09-29-map-rendering.md.

Today we're implementing province selection (Phase 2.3 from map-system-architecture.md).
Read map-system-architecture.md for context.

Goal: Async GPU readback for mouse→province lookup.
Keep it under 1ms per selection."
```

**❌ Bad:**
```
"Help me with the map stuff"
```

### During the Session

**Ask for architecture compliance:**
```
"Before we implement this, check if it follows our dual-layer architecture.
Reference master-architecture-document.md."
```

**Cite documentation:**
```
"This approach - does it match the pattern in performance-architecture-guide.md?"
```

**Link previous work:**
```
"We tried something similar in Log/2025-09-28-x.md - what did we learn?"
```

---

## 🚦 Session Types

### 1. Implementation Session
**Goal:** Build a specific feature
**Claude reads:**
- CLAUDE.md
- ARCHITECTURE_OVERVIEW.md
- Last session log
- Relevant architecture doc (e.g., map-system-architecture.md)

**Your prep:**
- Clear goal defined
- Success criteria identified
- Know which doc covers this system

### 2. Debugging Session
**Goal:** Fix a specific problem
**Claude reads:**
- CLAUDE.md
- Last session log (problem context)
- Relevant architecture doc
- Any logs about related issues

**Your prep:**
- Document the symptom clearly
- Note what you've already tried
- Have reproduction steps ready

### 3. Architecture Review
**Goal:** Evaluate if code matches docs
**Claude reads:**
- ARCHITECTURE_OVERVIEW.md
- Specific architecture docs to verify
- Recent implementation logs

**Your prep:**
- Identify which system to audit
- Have specific concerns or questions
- Be ready to update docs if needed

### 4. Refactoring Session
**Goal:** Improve existing code
**Claude reads:**
- CLAUDE.md (to enforce constraints)
- ARCHITECTURE_OVERVIEW.md
- Relevant performance/architecture docs
- Logs about why current code exists

**Your prep:**
- Know why you're refactoring (performance? clarity?)
- Have metrics/goals
- Understand current approach (read old logs)

### 5. Planning Session
**Goal:** Design a new feature
**Claude reads:**
- CLAUDE.md (architectural constraints)
- ARCHITECTURE_OVERVIEW.md (current state)
- Planning/[relevant-design].md (if exists)

**Your prep:**
- Clear problem statement
- Constraints identified
- Success criteria defined

---

## ⚠️ Common Mistakes to Avoid

### ❌ Starting Cold
**Mistake:** "Hey Claude, can you help with province loading?"
**Problem:** No context, will ask basic questions, waste time

**✅ Fix:** "Read these docs [list], here's what we're doing today"

### ❌ Not Logging During Session
**Mistake:** "I'll write the log at the end"
**Problem:** You'll forget decisions, why things didn't work, context

**✅ Fix:** Keep log file open, jot notes as you go

### ❌ Ignoring Previous Logs
**Mistake:** Not reading what you tried before
**Problem:** Repeat failed approaches

**✅ Fix:** Claude reads last session, you review "What Didn't Work"

### ❌ Vague Goals
**Mistake:** "Make the map faster"
**Problem:** Unclear success, no direction

**✅ Fix:** "Reduce province selection time from 5ms to <1ms"

### ❌ No Architecture Check
**Mistake:** Jump into implementation
**Problem:** Violate architectural constraints, have to redo

**✅ Fix:** "Does this approach fit our dual-layer architecture?"

---

## 📊 Session Quality Checklist

**Good session indicators:**
- ✅ Clear goal stated upfront
- ✅ Claude read relevant docs before starting
- ✅ Decisions documented as made
- ✅ Failed approaches noted (with why)
- ✅ Next steps defined
- ✅ Architecture compliance verified
- ✅ Log updated before ending

**Warning signs:**
- ⚠️ Claude asking basic architecture questions (didn't read docs)
- ⚠️ Trying approaches you already failed at (didn't read logs)
- ⚠️ Unclear why you're making certain choices (document now!)
- ⚠️ Code doesn't match architecture (stop, review docs)
- ⚠️ Not sure what to do next (needs clearer goals)

---

## 🎯 Goal-Setting Templates

### Implementation Goal
```
Implement [specific feature] from [doc-name.md Phase X.X]

Success criteria:
- [Measurable outcome 1]
- [Measurable outcome 2]
- Matches architecture in [doc.md]

Time budget: [X hours]
```

### Debugging Goal
```
Fix [specific symptom] in [File.cs:Line]

Success criteria:
- Symptom no longer occurs
- Root cause identified and documented
- Solution follows [doc.md] patterns

Must understand: Why it broke, why fix works
```

### Refactoring Goal
```
Refactor [system] to improve [metric]

Current state: [measurement]
Target state: [measurement]
Constraint: Must maintain [behavior]

Reference: [architecture-doc.md]
```

---

## 🔄 End-of-Session Checklist

Before you wrap up:

**1. Complete Your Log**
- [ ] "What Worked" section filled
- [ ] "What Didn't Work" section filled (with why!)
- [ ] Decisions documented
- [ ] "Next Session" section has clear tasks
- [ ] Code references added (File.cs:Line)

**2. Update Documentation**
- [ ] Architecture docs updated if design changed
- [ ] ARCHITECTURE_OVERVIEW.md stats updated if needed
- [ ] Planning/ docs moved to Engine/ if implemented

**3. Git Commit**
- [ ] Commit message references log: `(see Log/YYYY-MM-DD-x.md)`
- [ ] Log references commit hash

**4. Prep for Next Session**
- [ ] "Next Session" section is clear
- [ ] Blockers noted if any
- [ ] Docs to read next time listed

---

## 💡 Pro Tips

### Tip 1: Read Selectively
You don't need to read entire docs. Tell Claude:
```
"Read the 'Performance Patterns' section of performance-architecture-guide.md"
"Skim map-system-architecture.md for compute shader patterns"
```

### Tip 2: Build on Logs
After 3-4 sessions on same system, logs contain rich context:
```
"Read all logs from 2025-09-27 to 2025-09-30 about map rendering.
What patterns emerged? What consistently didn't work?"
```

### Tip 3: Quick Context Refresh
If you haven't worked on the project in a while:
```
"Read ARCHITECTURE_OVERVIEW.md and the last 3 session logs.
Summarize: Where are we? What's the current focus?"
```

### Tip 4: Prevent Scope Creep
State your scope explicitly:
```
"Today's scope: ONLY implement async province selection.
Don't suggest improving other parts of the map system."
```

### Tip 5: Use Logs as Research
Before implementing new feature:
```
"Search all logs for 'compute shader' - what have we learned?"
```

---

## 📚 Quick Doc References

**Core Rules:** `CLAUDE.md`
**Current State:** `Engine/ARCHITECTURE_OVERVIEW.md`
**Last Session:** `Log/[latest].md`

**System-Specific:**
- Map: `Engine/map-system-architecture.md`
- Performance: `Engine/performance-architecture-guide.md`
- Time: `Engine/time-system-architecture.md`
- Data: `Engine/core-data-access-guide.md`

**Future Features:** `Planning/` folder

**Log System:** `Log/README.md`
**Log Template:** `Log/TEMPLATE.md`

---

## ⏱️ Time Budgets

**Quick Session (1-2 hours):**
- 5 min: Read docs, prep log
- 60-100 min: Implementation
- 10-15 min: Complete log, commit

**Deep Session (3-4 hours):**
- 10 min: Read docs, review context
- 150-210 min: Implementation with breaks
- 20 min: Complete log, update architecture docs

**Planning Session (1 hour):**
- 10 min: Read current architecture
- 40 min: Design discussion with Claude
- 10 min: Update Planning/ doc with decisions

---

## 🎓 Learning from Sessions

### After 5-10 Sessions
Review your logs and ask:
- What patterns keep appearing?
- What anti-patterns keep causing problems?
- Are there learnings to extract to `Log/learnings/`?
- Are there decisions to formalize in `Log/decisions/`?

### After 20-30 Sessions
Consider:
- Creating a "Common Pitfalls" document
- Adding proven patterns to architecture docs
- Archiving old logs (keep recent 2-3 months easily accessible)
- Reviewing if log template needs updating

---

*Session Start Guide v1.0 - Created 2025-09-30*