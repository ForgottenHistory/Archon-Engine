# Session Log System

**Purpose:** Maintain context between AI coding sessions and track architectural decisions, patterns, and lessons learned.

---

## Why This Exists

**The Problem:** AI has no memory between sessions. Without logs:
- Every session starts from scratch
- Failed approaches get tried again
- Architectural decisions get forgotten
- Context is lost

**The Solution:** Detailed session logs that serve as:
- **AI Context** - Claude reads these to understand project history
- **Developer Log** - You review what worked/didn't work
- **Architecture Chronicle** - Track how design evolved
- **Decision Record** - Why we chose specific approaches

---

## File Naming Convention

```
YYYY-MM-DD-[sequence]-[short-description].md
```

**Examples:**
- `2025-09-30-documentation-reorganization.md`
- `2025-09-30-1-province-loading.md` (first session of day)
- `2025-09-30-2-map-rendering.md` (second session of day)

**Why:**
- Chronological sorting works automatically
- Easy to find specific sessions
- Clear at a glance what was worked on

---

## Log Types

### 1. Session Logs (Main Type)
**File:** Use TEMPLATE.md as starting point
**When:** Every coding session with Claude
**Contains:**
- What we tried
- What worked/didn't work
- Decisions made
- Code references
- Next steps

**Goal:** Future Claude (or you) can pick up exactly where we left off

### 2. Decision Records (Optional)
**File:** `decisions/[decision-name].md`
**When:** Major architectural decision that will be referenced often
**Contains:**
- Context of decision
- Options considered
- Final decision
- Trade-offs
- Rationale

**Example:** `decisions/why-8-byte-provincestate.md`

**Goal:** Have a single source of truth for "why did we do it this way?"

### 3. Learning Documents (Optional)
**File:** `learnings/[topic].md`
**When:** Discovered a pattern/pitfall worth documenting
**Contains:**
- Pattern/technique description
- When to use it
- Common mistakes
- Code examples
- Related sessions

**Example:** `learnings/unity-urp-compute-shader-gotchas.md`

**Goal:** Build a knowledge base of project-specific patterns

---

## Workflow

### Starting a Session

**1. Claude Reads (in order):**
```
1. Assets/CLAUDE.md (rules & constraints)
2. Docs/Engine/ARCHITECTURE_OVERVIEW.md (current state)
3. Docs/Log/[most-recent-session].md (what we tried last)
4. Docs/Engine/[relevant-doc].md (specific system details)
```

**2. You Brief Claude:**
"Today we're working on [X]. Read [specific docs]. Here's the goal: [Y]"

**3. Start Working!**

### During a Session

**Track as you go:**
- Copy TEMPLATE.md → new session file
- Fill in sections as you work
- Note decisions immediately (they're fresh in mind)
- Document failed approaches (why they failed)
- Link to code with file:line references

**Don't worry about perfection** - rough notes are better than no notes

### Ending a Session

**Wrap up the log:**
1. ✅ Fill out "What Worked" / "What Didn't Work"
2. ✅ Document any decisions made
3. ✅ Note "Next Session" tasks
4. ✅ Add "Quick Reference for Future Claude" section
5. ✅ Link to changed files
6. ⚠️ Update architecture docs if needed
7. ⚠️ Git commit with reference to log: `git commit -m "Implement X (see Log/2025-09-30-x.md)"`

### Next Session

**Claude starts by reading your last log** - instant context!

---

## What to Log

### ✅ DO Log These

**Decisions:**
- Why we chose approach X over Y
- Trade-offs we accepted
- Constraints that influenced design

**Failed Approaches:**
- What we tried that didn't work
- **WHY it failed** (root cause)
- "Don't try this again because..."

**Patterns:**
- Successful patterns discovered
- Anti-patterns to avoid
- Reusable solutions

**Problems Solved:**
- Symptom → Root Cause → Solution
- Investigation steps
- How we figured it out

**Architecture Impact:**
- Changes to design
- New constraints discovered
- Documentation updates needed

**Code References:**
- `File.cs:LineStart-LineEnd` for key implementations
- Link to commits
- Reference to tests

### ❌ DON'T Log These

**Avoid:**
- Copy-pasting entire files (link to them instead)
- Play-by-play of every line of code
- Obvious stuff ("imported library X")
- Temporary debugging notes
- Personal thoughts unrelated to code

---

## Log Structure Philosophy

### Core Sections (Always Include)

**1. Session Goal** - What are we trying to accomplish?
**2. What We Did** - The work completed
**3. Decisions Made** - Significant choices with rationale
**4. What Worked / Didn't Work** - Lessons learned
**5. Next Session** - Where to pick up

### Optional Sections (Add When Relevant)

**Problems Encountered** - If you hit blockers
**Architecture Impact** - If design changed
**Code Quality Notes** - Performance, testing, debt
**Quick Reference for Future Claude** - Context for next session

### Guiding Principles

**Be Specific:**
- ❌ "Fixed the bug"
- ✅ "Fixed race condition in ProvinceSystem.cs:145 by adding lock"

**Explain Why:**
- ❌ "Changed to NativeArray"
- ✅ "Changed to NativeArray for Burst compatibility (see performance-guide.md)"

**Link Everything:**
- Code: `File.cs:Line`
- Docs: `[doc-name.md](../Engine/doc.md)`
- Previous sessions: `[session-name.md](./session.md)`

**Think Future You:**
- Will you understand this in 3 months?
- Can Claude pick up where you left off?
- Is the "why" clear?

---

## Maintenance

### Weekly
- ✅ Review last week's sessions
- ✅ Extract patterns → `learnings/`
- ✅ Extract major decisions → `decisions/`
- ✅ Update architecture docs if needed

### Monthly
- ✅ Archive old sessions to `archive/YYYY-MM/`
- ✅ Update ARCHITECTURE_OVERVIEW.md stats
- ✅ Review `learnings/` - anything to formalize in engine docs?

### When Starting New Major Feature
- ✅ Read relevant past sessions
- ✅ Check `decisions/` for related choices
- ✅ Review `learnings/` for applicable patterns

---

## Integration with Architecture Docs

### Logs vs. Docs - Different Purposes

**Session Logs (Log/):**
- Temporal - "what happened when"
- Exploratory - "we tried A, B, C"
- Context-rich - "here's why we did it"
- Messy is OK - rough notes

**Architecture Docs (Engine/):**
- Timeless - "this is how it works"
- Prescriptive - "do it this way"
- Patterns - "use this approach"
- Polished - source of truth

### The Flow

```
1. Work happens (logged in Log/)
2. Patterns emerge (multiple sessions)
3. Pattern stabilizes (becomes part of architecture)
4. Update Docs/Engine/ (formalize the pattern)
5. Future sessions reference the doc (not the logs)
```

**Logs are raw material → Docs are refined product**

### When to Update Docs

**Update Engine/ docs when:**
- Pattern has been used successfully 3+ times
- Decision is permanent (not exploratory)
- Implementation matches the doc (verify!)
- Other developers need to follow this pattern

**Update Planning/ docs when:**
- Future feature design evolves
- New constraints discovered
- Approach changes significantly

---

## Example: Good vs. Bad Logging

### ❌ Bad Example
```markdown
# Fixed the bug
2025-09-30

We had a bug. I fixed it. The code works now.

Next: Do more stuff.
```
**Problems:** No context, no details, useless in 2 weeks

### ✅ Good Example
```markdown
# Province Selection Race Condition Fix
**Date**: 2025-09-30
**Status**: ✅ Complete

## Problem
Province selection was returning wrong IDs intermittently.
See: ProvinceSystem.cs:145

## Root Cause
Race condition: GPU readback completing while provinces array
was being modified by simulation thread.

## What We Tried
1. Lock the entire ProvinceSystem - too slow (20ms)
2. Double buffering - complex, error-prone
3. Copy-on-read with NativeArray - ✅ Works!

## Solution
```csharp
// ProvinceSystem.cs:145-152
private NativeArray<ProvinceState> GetSnapshotForRead() {
    var snapshot = new NativeArray<ProvinceState>(
        provinceStates, Allocator.Temp
    );
    return snapshot;
}
```

## Why This Works
- NativeArray copy is <0.1ms (acceptable)
- Snapshot is immutable → no race
- Auto-disposed (Allocator.Temp)

## Pattern
Reusable for any CPU→GPU data read.
Add to performance-architecture-guide.md

## Next Session
Test with 10k provinces, verify performance
```
**Why Good:** Context, investigation, solution, pattern, next steps

---

## Tips for Effective Logging

### 1. Log Immediately, Not Later
- Decisions are fresh in your mind
- "Why" is obvious now, not in 2 weeks

### 2. Focus on "Why", Not Just "What"
- Code shows "what" - logs explain "why"

### 3. Document Failures Prominently
- Failed approaches teach more than successes
- Prevent repeating mistakes

### 4. Link Generously
- Code references: `File.cs:Line`
- Doc references: `[doc.md](path)`
- Previous sessions: Make it easy to follow the thread

### 5. Write for Future You/Claude
- Assume zero memory of this session
- What context is needed?

### 6. Use Templates as Guides, Not Straitjackets
- TEMPLATE.md has everything possible
- Your log might only need 50% of sections
- Adapt to what's relevant

### 7. Commit Logs with Code
- Git commit message: references the log
- Log references: the commit/files
- Two-way linkage

---

## Anti-Patterns to Avoid

### ❌ "I'll Document It Later"
**Problem:** You won't, or you'll forget details
**Solution:** Rough notes during session, polish at end

### ❌ "The Code Documents Itself"
**Problem:** Code shows "what", not "why" or "what we tried"
**Solution:** Comments for "what", logs for "why" and "failed approaches"

### ❌ "Too Busy to Log"
**Problem:** Next session wastes time reconstructing context
**Solution:** 10 min logging saves 60 min next session

### ❌ "Logs Are Too Long, Nobody Reads Them"
**Problem:** Not targeted - Claude reads specific sections
**Solution:** Good structure → skim for relevant parts

### ❌ "This Is Obvious, No Need to Log"
**Problem:** "Obvious" now ≠ "obvious" in 3 months
**Solution:** If you had to figure it out, log it

---

## Quick Start Checklist

**New Session:**
- [ ] Copy TEMPLATE.md → `YYYY-MM-DD-name.md`
- [ ] Fill in Goal, Context
- [ ] Claude reads last session + relevant docs

**During Session:**
- [ ] Note decisions as they happen
- [ ] Log failed approaches (with why)
- [ ] Link to code: `File.cs:Line`

**End Session:**
- [ ] Fill "What Worked" / "Didn't Work"
- [ ] Complete "Next Session"
- [ ] Add "Quick Reference for Future Claude"
- [ ] Update architecture docs if needed
- [ ] Git commit with log reference

**Next Session:**
- [ ] Claude reads previous log first
- [ ] Check "Next Session" for priorities
- [ ] Continue from where you left off

---

## Success Metrics

**Good logging = Better AI sessions:**
- ✅ Claude asks fewer "what did we decide?" questions
- ✅ No repeating failed approaches
- ✅ Faster onboarding for new features
- ✅ Clear architectural evolution trail
- ✅ You can pick up work after breaks

**If logs aren't helping:**
- Too vague? → Add more specifics
- Too long? → Focus on decisions, not play-by-play
- Not reading them? → Make them more scannable
- Outdated? → Update or delete

---

## Resources

**Templates:**
- TEMPLATE.md - Full session log template

**Examples:**
- Browse existing logs for patterns
- Best examples: [to be added as project evolves]

**Related:**
- `../Engine/ARCHITECTURE_OVERVIEW.md` - Current system state
- `../CLAUDE.md` - AI session rules
- `../DOCUMENTATION_AUDIT.md` - Documentation history

---

## Questions?

**"How detailed should I be?"**
→ Enough that Future You (3 months later) understands the context

**"What if the log gets really long?"**
→ Fine! Better too much than too little. Use headings for navigation.

**"Should I log every tiny thing?"**
→ No. Focus on: decisions, failed approaches, patterns, problems solved

**"What if I don't know what went wrong?"**
→ Log what you observed, what you tried, where you're stuck. Next session can investigate.

**"How do I make Claude read my logs?"**
→ Start each session: "Read Docs/Log/[latest].md before we begin"

---

*Log System Version: 1.0 - Created 2025-09-30*