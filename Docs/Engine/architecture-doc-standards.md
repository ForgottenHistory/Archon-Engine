# Architecture Documentation Standards

**Purpose:** Guidelines for writing timeless architecture documentation.

---

## Core Principle

**Architecture docs explain WHY and WHAT, not HOW or WHERE.**

They should remain valid even as implementation details change.

---

## What Architecture Docs ARE

- Principles and patterns
- Trade-offs and decisions
- Constraints and requirements
- Anti-patterns to avoid
- Conceptual relationships

## What Architecture Docs ARE NOT

- Code examples or snippets
- File paths or locations
- Implementation details
- Performance metrics
- Temporal references

---

## Document Structure

### Required Sections

1. **Title** - Clear, descriptive name
2. **Status** - Production Standard, Draft, Deprecated
3. **Core Principle** - One-sentence summary of the key insight
4. **The Problem** - What challenge does this solve?
5. **The Solution** - High-level approach
6. **Anti-Patterns** - What to avoid
7. **Trade-offs** - Benefits vs costs
8. **Summary** - Numbered key points
9. **Related Patterns** - Cross-references

### Optional Sections

- Architecture layers
- Component responsibilities
- When to use / not use
- Integration points
- Key constraints

---

## Writing Guidelines

### DO

- Use present tense ("Commands validate before execution")
- State principles clearly ("All state changes flow through commands")
- Explain trade-offs objectively
- Use tables for comparisons
- Keep sections focused and short
- Reference patterns by number ("Pattern 2")

### DON'T

- Include code snippets (readers should check actual code)
- Reference specific files or line numbers
- Use temporal language ("recently", "last week", "will be")
- Include performance numbers (they change)
- Add implementation tutorials
- Use excessive formatting

---

## Anti-Pattern Examples

| Bad | Why | Good |
|-----|-----|------|
| "See ProvinceSystem.cs line 156" | File may move/change | "ProvinceSystem handles ownership" |
| "Takes ~50ms on average" | Performance varies | "Optimized for large datasets" |
| "We added this last week" | Temporal reference | "This pattern enables..." |
| `code examples here` | Implementation detail | Describe the concept |
| "In the future we'll..." | Speculative | Document current state |

---

## Table Formats

### Anti-Pattern Table
```
| Don't | Do Instead |
|-------|------------|
| Bad practice | Good practice |
```

### Trade-off Table
```
| Aspect | Benefit | Cost |
|--------|---------|------|
| Feature | What you gain | What you pay |
```

### Comparison Table
```
| Approach A | Approach B |
|------------|------------|
| Characteristic | Characteristic |
```

---

## Length Guidelines

- **Core Principle**: 1-2 sentences
- **Problem/Solution**: 3-5 bullet points each
- **Sections**: 50-150 words
- **Total document**: 150-300 lines
- **Summary**: 5-10 numbered points

---

## Cross-References

Reference patterns from CLAUDE.md by number:
- "Pattern 2 (Command Pattern)"
- "Pattern 3 (Event-Driven)"

Reference other architecture docs by concept:
- "See Command System Architecture"
- "Related: Data Flow Architecture"

Never reference by file path.

---

## Maintenance

Architecture docs should:
- Rarely need updates (timeless by design)
- Update only when patterns fundamentally change
- Never track implementation changes

If you're updating frequently, the doc contains too much implementation detail.

---

## Checklist Before Writing

- [ ] Is this a pattern/principle, not implementation?
- [ ] Will this be true in 6 months?
- [ ] Are there no file paths?
- [ ] Are there no code examples?
- [ ] Are there no performance numbers?
- [ ] Is the core principle clear in one sentence?

---

## Summary

1. **Principles over implementation** - Explain why, not how
2. **Timeless content** - No temporal references
3. **No code or paths** - Implementation lives in code
4. **Clear trade-offs** - Every decision has costs
5. **Anti-patterns included** - What NOT to do matters
6. **Structured format** - Consistent sections
7. **Concise writing** - Every word counts

---

*Architecture docs are maps, not turn-by-turn directions.*
