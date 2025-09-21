# Unity Paradox Parser - Claude Documentation

## Project Overview
High-performance Unity-native parser for Paradox Interactive file formats (EU4, HOI4, CK3, etc.). Built with Unity Job System and Burst Compiler for maximum throughput on large game files.

## Important files:
- Data files are "Assets\Data" then follow a Paradox filepath format. history, common, map, etc.

## Paradox Format Examples:
```
# Basic key-value
culture = "german"
population = 1000000

# Nested blocks
technology = {
    military = 5
    diplomatic = 3
    administrative = 4
}

# Lists and dates
provinces = { 1 2 3 4 }
start_date = 1444.11.11

# Complex nested structures
country = {
    tag = GER
    government = monarchy
    technology_group = western
    capital = 50

    history = {
        1066.1.1 = { owner = HRE }
        1871.1.18 = {
            government = german_empire
            add_government_reform = prussian_monarchy
        }
    }
}
```

## Core Philosophy
- **Performance First**: Burst compilation, SIMD optimization, zero-allocation parsing
- **Generic Design**: Handles all Paradox formats, not game-specific implementations
- **Unity Native**: Leverages Unity Job System, Native Collections, and modern C# features
- **Robust Testing**: Comprehensive test coverage before advancing phases

## Code Standards

### Performance Requirements
- Always use Burst compilation for hot paths (avoid NativeSlice parameters in Burst methods)
- Zero-allocation parsing patterns using structs and unsafe pointers
- Implement dual APIs: unsafe pointers for Burst, NativeSlice overloads for convenience
- Use `[MethodImpl(MethodImplOptions.AggressiveInlining)]` for critical path methods

### Development Process
1. **Phase-by-Phase Development**: Complete each phase fully before advancing
2. **Test-Driven**: All features must have passing tests before phase completion
3. **Incremental Testing**: Run tests after each significant change
4. **Error Resolution**: Fix all compilation errors before proceeding

### Code Structure
- Struct-based result types with `Success` boolean and `BytesConsumed` tracking
- Static utility classes with focused responsibilities
- Comprehensive operator precedence and tokenization support
- Event-driven architecture for complex parsing workflows

## Key Rules
- NEVER remove Burst compilation entirely - optimize for compatibility instead
- Always verify compilation success before claiming task completion
- Maintain generic parser design - avoid game-specific hardcoding
- Test all utility functions with edge cases and performance scenarios
- Keep files modular and preferably under 500 lines

## Code Standards
- Single Responsibility: Each file has clear, focused purpose
- Easy to Extend: Simple to add new features/settings
- Type Safety: Proper validation and error handling
- Consistent patterns throughout
- Avoid tight coupling. Create independent systems.

**IMPORTANT**: Have good separation of concerns and smaller, focused files. I use AI to develop, so output and context length is important.
