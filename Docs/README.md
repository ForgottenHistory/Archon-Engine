# Dominion Documentation

**Quick Start:** Read [Engine/ARCHITECTURE_OVERVIEW.md](Engine/ARCHITECTURE_OVERVIEW.md)

---

## Documentation Structure

```
Assets/Docs/
├── README.md (you are here)
├── SESSION_START_GUIDE.md ⭐ Quick reference for starting sessions
├── DOCUMENTATION_AUDIT.md (quality assessment & history)
│
├── Engine/ (9 docs - IMPLEMENTED & PARTIAL systems)
│   ├── ARCHITECTURE_OVERVIEW.md ⭐ START HERE
│   ├── master-architecture-document.md
│   ├── map-system-architecture.md (consolidated from 4 docs)
│   ├── core-data-access-guide.md
│   ├── data-linking-architecture.md
│   ├── data-flow-architecture.md
│   ├── time-system-architecture.md
│   ├── performance-architecture-guide.md
│   └── unity-burst-jobs-architecture.md
│
├── Planning/ (5 docs - FUTURE features, not implemented)
│   ├── README.md
│   ├── ai-design.md (future)
│   ├── multiplayer-design.md (future)
│   ├── modding-design.md (future)
│   ├── save-load-design.md (future)
│   └── error-recovery-design.md (future)
│
└── Log/ (Session logs - AI context & developer journal)
    ├── README.md (explains log system)
    ├── TEMPLATE.md (template for new sessions)
    └── YYYY-MM-DD-session-name.md (chronological logs)
```

---

## For New Developers

**Read in this order:**
1. [Engine/ARCHITECTURE_OVERVIEW.md](Engine/ARCHITECTURE_OVERVIEW.md) - 5-10 min read
2. [Engine/master-architecture-document.md](Engine/master-architecture-document.md) - 15-20 min
3. [Engine/core-data-access-guide.md](Engine/core-data-access-guide.md) - 10 min
4. Browse codebase: `Assets/Scripts/Core/`

**Total onboarding time:** ~1 hour to understand core architecture

---

## Implementation Status Legend

Each document has a status badge:

- ✅ **Implemented** - System exists in codebase and is functional
- ⚠️ **Partial** - Some components implemented, others pending
- ❌ **Not Implemented** - Design only, no code (in Planning/ folder)
- 📚 **Reference** - Tutorial/guide, not architecture

---

## Documentation History

### 2025-09-30: Major Reorganization
**Actions:**
- ❌ Deleted: performance-monitoring-architecture.md (needless over-engineering)
- 📁 Created: Planning/ folder for unimplemented systems
- 📋 Moved to Planning/: AI, multiplayer, modding (not implemented)
- 🔗 Added: Cross-references between docs
- 📦 Consolidated: 4 map docs → 1 comprehensive map-system-architecture.md
- ⭐ Created: ARCHITECTURE_OVERVIEW.md quick reference
- 📊 Added: Implementation status badges to all docs

**Result:** 17 docs → 13 docs (10 Engine + 3 Planning)
- 50% less repetition
- Clear separation of implemented vs planned
- Honest about what exists

**See:** [DOCUMENTATION_AUDIT.md](DOCUMENTATION_AUDIT.md) for full analysis

### Before 2025-09-30
- 17 documents in Engine/ folder
- Mixed implemented and unimplemented systems
- Significant repetition (dual-layer architecture explained 8+ times)
- Unclear implementation status
- ~60% of documented systems didn't exist

---

## Document Summaries

### Engine/ (Implemented & Partial)

**ARCHITECTURE_OVERVIEW.md** ⭐ **START HERE**
Quick reference guide with decision trees, status overview, and onboarding path

**master-architecture-document.md** (✅ Core Implemented)
Dual-layer architecture, 8-byte ProvinceState, command pattern foundation

**map-system-architecture.md** (⚠️ Partial)
Consolidated: Texture-based rendering, coordinates, map modes, URP integration
- Previously 4 separate docs
- Phase 1 complete, Phases 2-5 in progress

**core-data-access-guide.md** (✅ Implemented)
Hot/cold data separation, ProvinceState, ProvinceColdData patterns

**data-linking-architecture.md** (✅ Implemented)
CrossReferenceBuilder, ReferenceResolver, data validation

**data-flow-architecture.md** (⚠️ Partial)
System communication, command pattern, event system

**time-system-architecture.md** (✅ Implemented)
TimeManager, layered update frequencies, dirty flags

**performance-architecture-guide.md** (⚠️ Partial)
Cache patterns, Structure of Arrays, hot/cold separation

**save-load-architecture.md** (⚠️ Unknown)
Command-based save system design (implementation status unclear)

**error-recovery-architecture.md** (⚠️ Mostly Speculative)
Error handling principles (advanced features not implemented)

**unity-burst-jobs-architecture.md** (⚠️ Partial | 📚 Reference)
Burst compiler tutorial and patterns (BurstProvinceHistoryLoader exists)

### Planning/ (Future Features)

**ai-design.md** (❌ Not Implemented)
Hierarchical AI system (Strategic/Tactical/Operational layers)

**multiplayer-design.md** (❌ Not Implemented)
Deterministic simulation, network optimization, rollback netcode

**modding-design.md** (❌ Not Implemented)
Moddable engine architecture

---

## Using Docs with Claude

**Session Start Workflow:**
1. Claude reads: `CLAUDE.md` (rules & constraints)
2. Claude reads: `Engine/ARCHITECTURE_OVERVIEW.md` (current state)
3. Claude reads: `Log/[latest-session].md` (what we tried last)
4. Claude reads: Specific docs as needed for the task

**For human reading:**
- Start with ARCHITECTURE_OVERVIEW.md
- Follow links to detailed docs
- Check status badges to know what's implemented
- Review Log/ for recent changes and decisions

---

## Maintenance Guidelines

### Adding New Documentation
1. Create in Engine/ if implemented, Planning/ if not
2. Add implementation status badge at top
3. Add cross-references to related docs
4. Update this README
5. Update ARCHITECTURE_OVERVIEW.md if it's a core system

### Moving from Planning/ to Engine/
When implementing a planned system:
1. Move doc from Planning/ to Engine/
2. Update status badge (❌ → ⚠️ or ✅)
3. Remove speculative sections
4. Add actual code examples
5. Update cross-references
6. Update ARCHITECTURE_OVERVIEW.md
7. Update Planning/README.md and this file

### Updating Existing Docs
- Keep status badges current
- Remove repetitive content, add cross-references
- Verify code examples match actual implementation
- Update DOCUMENTATION_AUDIT.md if major changes

---

## Documentation Principles

1. **Honesty** - Status badges show what's actually implemented
2. **Conciseness** - Explain once, cross-reference elsewhere
3. **Separation** - Implemented (Engine/) vs planned (Planning/)
4. **Accessibility** - Quick reference (OVERVIEW) + detailed docs
5. **Maintainability** - Cross-references make updates easier

---

## Questions?

**"Where do I start?"**
→ [Engine/ARCHITECTURE_OVERVIEW.md](Engine/ARCHITECTURE_OVERVIEW.md)

**"What's actually implemented?"**
→ Check status badges in each doc

**"Where's the AI/multiplayer/modding?"**
→ Planning/ folder (not implemented yet)

**"Why was this reorganized?"**
→ [DOCUMENTATION_AUDIT.md](DOCUMENTATION_AUDIT.md)

**"How do I use the session logs?"**
→ [Log/README.md](Log/README.md)

**"Where do I log my work?"**
→ Copy `Log/TEMPLATE.md`, fill it out during/after each session

**"How do I start a session?"**
→ [SESSION_START_GUIDE.md](SESSION_START_GUIDE.md) - Quick checklist & workflow

---

## The Documentation Philosophy

**"Documentation is not overhead—it's a productivity multiplier."**

With AI development:
- Good docs = Effective AI = Better code = Update docs = Better AI
- Bad docs = Every session starts from scratch = Wasted time
- Session logs = Continuous context = Compound progress

**Every hour spent on docs saves 10 hours in AI sessions.**

---

*Documentation v2.0 - Reorganized 2025-09-30*