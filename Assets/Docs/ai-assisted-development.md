# AI-Assisted Development Guide for Solo Tech Leads

## Core Philosophy
You are the architect and quality gatekeeper. AI is your team of junior-to-mid level developers who work incredibly fast but need clear direction and constant supervision. They don't truly understand the system - they pattern match. Your job is to provide context, catch mistakes, and maintain the vision.

## Managing AI Context Effectively

### The Context Window Problem
AI has limited memory and no persistence between conversations. It cannot see your codebase, remember previous discussions, or understand the bigger picture unless you explicitly provide it. Think of each conversation as briefing a new developer who's smart but knows nothing about your project.

### Context Strategy
Start each session with a brief project summary that includes your tech stack, architecture decisions, and current focus area. Keep a "project context document" that you paste at the beginning of important conversations. This should contain your core architectural decisions, naming conventions, and critical constraints. Update this document as the project evolves.

Reference your existing documentation explicitly. When asking for additions to existing systems, provide the current implementation or at least describe its structure. AI cannot infer what you already have built.

### Working Around Context Limits
Break large tasks into smaller, focused conversations. Instead of "build the entire economic system," ask for "the tax calculation module following our existing province data structure." This keeps the AI focused and reduces the chance of inconsistencies.

When context gets too long, start a new conversation with a summary of decisions made. Copy key code artifacts to your project immediately - don't rely on retrieving them from old conversations.

## Preventing AI Hallucinations

### Common Hallucination Patterns
AI will confidently make up function names, libraries, and even entire APIs that don't exist. It often assumes Unity has features it doesn't, invents C# language features, or creates plausible-sounding but fictional optimization techniques.

AI tends to oversimplify complex problems, especially around performance and multiplayer synchronization. It might suggest solutions that work in theory but fail at scale or with edge cases.

### Validation Strategies
Always verify external references. If the AI suggests a library or API, search for it yourself. If it claims something about performance, test it. If it proposes an algorithm, trace through it mentally with edge cases.

Be especially skeptical of specific numbers (cache sizes, performance metrics, memory usage). AI often provides reasonable-sounding but made-up benchmarks.

Watch for overcomplicated solutions to simple problems and oversimplified solutions to complex problems. AI doesn't truly understand computational complexity or system constraints.

## Maintaining Consistency

### Architectural Consistency
Never let AI make architectural decisions. You decide the patterns, data structures, and system boundaries. AI implements within those constraints. If AI suggests major structural changes, evaluate them yourself but don't let it implement them without careful consideration.

Keep a "decisions document" listing key architectural choices and why you made them. Reference this when AI suggests alternatives. This prevents drift over time as you work with different AI sessions.

### Code Style Consistency
Establish naming conventions early and remind AI of them frequently. AI will default to different styles between sessions. Be explicit about preferences for things like error handling, logging, and comment style.

Create templates for common code patterns in your project. When asking AI to create similar features, provide the template to maintain consistency.

### System Integration
When adding new features, always describe how they should integrate with existing systems. AI cannot infer integration points and will often create isolated solutions that don't fit your architecture.

Be explicit about dependencies and communication patterns between systems. Should this new system emit events? Use the command pattern? Update directly? AI needs to be told.

## Quality Control Strategies

### Code Review Mindset
Review AI code like you would a junior developer's PR - assume it works for the happy path but has missed edge cases, error handling, and performance implications. Look for null reference potential, unbounded loops, and memory leaks.

AI often ignores thread safety, assumes perfect input, and doesn't consider failure modes. Add these yourself or explicitly ask for them.

### Testing AI Code
Never trust AI's first solution for critical systems. Test with edge cases, large data sets, and adverse conditions. AI code often works perfectly for the example case but fails in production scenarios.

Performance test everything. AI doesn't understand your performance budget and will suggest O(n²) solutions when O(n) is required. It rarely considers cache efficiency or memory layout.

### Iterative Refinement
Use AI for the first 80% of implementation, then take over for the critical 20%. AI is excellent at boilerplate and structure but weak at optimization and edge cases.

Don't hesitate to ask AI to revise its solution multiple times. Each iteration usually improves quality if you provide specific feedback about what's wrong.

## Managing Development Flow

### Task Decomposition
Break features into layers: data structure, basic operations, integration, optimization, error handling. Have AI handle each layer separately with clear requirements for each.

Use AI for parallel development of independent systems, then handle integration yourself. This prevents integration issues and maintains architectural vision.

### Documentation Strategy
Have AI write documentation alongside code. It's excellent at explaining what code does, though you'll need to add the why and the architectural context.

Use AI to maintain technical documentation but review for accuracy. It can update API docs and README files efficiently but may introduce subtle errors.

### Avoiding Development Traps
Don't let AI refactor working code without specific requirements. It tends to "improve" things in ways that break subtle dependencies or performance characteristics.

Avoid open-ended requests like "make this better" or "optimize this." Be specific about what needs improvement and what constraints must be maintained.

Don't chain too many AI modifications without testing. Errors compound and become harder to debug the further you get from working code.

## Red Flags to Watch For

### Dangerous Patterns
- "This should work" or "This might work" - means AI is guessing
- Deeply nested callbacks or complex promise chains - often hiding logic errors
- Multiple type casts in a row - suggests type system problems
- Try-catch blocks that swallow errors - hiding problems, not solving them
- Comments like "TODO: handle error" - AI knows it's incomplete

### When to Take Control
- Integration between major systems - do this yourself
- Performance-critical hot paths - AI doesn't understand your constraints
- Network synchronization - too many subtle requirements
- Save/load systems - one mistake corrupts everything
- Memory management - AI assumes garbage collection solves everything

## Maximizing AI Effectiveness

### Clear Requirements
Provide constraints upfront: performance targets, memory budgets, platform limitations. AI cannot infer these and will default to generic solutions.

Include negative requirements - what the solution should NOT do. This prevents AI from adding unnecessary complexity or breaking existing features.

### Leveraging AI Strengths
AI excels at boilerplate, data structures, and standard algorithms. Use it heavily for these tasks. It's also excellent at converting between formats, generating test data, and writing serialization code.

AI is good at explaining existing code and suggesting improvements if you provide specific criteria. Use it as a code review partner, asking "what could go wrong with this?"

### Avoiding AI Weaknesses
Don't rely on AI for novel algorithms or complex optimizations. It can only recombine patterns it has seen before.

Avoid using AI for security-critical code without expert review. It often implements authentication and encryption incorrectly.

Be cautious with AI-generated SQL or database operations. It frequently creates queries that work but don't scale or miss important indexes.

## Long-Term Project Health

### Technical Debt Management
AI can quickly generate massive amounts of code, creating technical debt faster than you can refactor. Regularly schedule consolidation passes where you simplify and unify AI-generated code.

Keep a "debt document" listing known issues, inconsistencies, and planned refactoring. This prevents losing track of problems in the rapid development pace.

### Knowledge Retention
Maintain your own understanding of all critical systems. Don't let AI become a black box where you don't understand your own codebase. If you can't explain it, you can't debug it.

Write your own summary documentation of how systems work and interact. This becomes invaluable when debugging integration issues or explaining the system to future AI sessions.

### Project Sustainability
Establish clear boundaries between generated and hand-written code. Keep critical business logic and architectural code under your direct control.

Build debugging and monitoring tools early. AI-generated code is harder to debug because you didn't write it. Comprehensive logging and metrics become essential.

Create integration tests that verify system interactions. AI won't catch integration bugs, and they're the hardest to debug later.

## Summary

Using AI effectively as a solo developer means maintaining strong technical leadership while leveraging AI for rapid implementation. You provide the vision, constraints, and quality control. AI provides the velocity. 

Never forget: AI doesn't understand your project, it pattern matches from its training. Your expertise in knowing what's right for your specific project is irreplaceable. AI is a powerful tool, but you are the engineer.