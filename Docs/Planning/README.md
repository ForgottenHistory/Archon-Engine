# Planning Documents

**⚠️ IMPORTANT: These are FUTURE PLANNING documents, NOT current architecture.**

This folder contains design documents for **unimplemented features**. These documents describe systems that:
- Are not yet implemented in the codebase
- May be implemented in the future
- Should not be confused with current architecture

---

## Documents in this Folder

### ai-design.md
**Status:** ❌ Not Implemented
**Description:** Comprehensive AI system design with hierarchical decision-making (Strategic/Tactical/Operational layers)
**Complexity:** ~1000 lines
**Notes:** Well-designed but entirely speculative. No AI code exists in codebase yet.

### modding-design.md
**Status:** ❌ Not Implemented
**Description:** Moddable engine architecture for user customization
**Complexity:** ~600 lines
**Notes:** Forward-thinking design but premature. No modding support exists yet.

### multiplayer-design.md
**Status:** ❌ Not Implemented
**Description:** Multiplayer architecture with deterministic simulation and network optimization
**Complexity:** ~500 lines
**Notes:** Solid multiplayer principles but no multiplayer code exists yet. The dual-layer architecture supports multiplayer but implementation is entirely future work.

### save-load-design.md
**Status:** ❌ Not Implemented
**Description:** Command-based save/load system with replay functionality
**Complexity:** ~650 lines
**Notes:** Leverages command pattern for efficient saves. Command system exists but save/load implementation doesn't.

### error-recovery-design.md
**Status:** ❌ Not Implemented
**Description:** Comprehensive error handling and recovery system
**Complexity:** ~400 lines
**Notes:** Basic validation exists (DataValidator), but sophisticated recovery systems are future work.

---

## When to Reference These Documents

✅ **DO reference when:**
- Planning future feature development
- Researching design patterns for these systems
- Understanding long-term project vision
- Evaluating architectural decisions

❌ **DON'T reference when:**
- Looking for current codebase architecture
- Trying to understand existing systems
- Writing code that needs to work today
- Onboarding new developers (show them Engine/ docs first)

---

## Moving Documents Back to Engine/

If you implement one of these systems, **move the document back to Engine/** and:
1. Update the implementation status header
2. Remove speculative sections
3. Add actual code examples from the implementation
4. Update the DOCUMENTATION_AUDIT.md

---

## See Also

- `Assets/Docs/Engine/` - Current implemented architecture