# Dominion - Claude Documentation

## Project Overview
Dominion is a grand strategy game that captures the political reality of ancient rulership - where every decision creates winners and losers among your subjects, and success comes from understanding and managing these internal dynamics rather than just optimizing abstract numbers. More is detailed in Docs folder.

Built with Unity Job System and Burst Compiler for maximum throughput on large game files. We are NOT remaking EU4, CK3, or any other specific game. 
We are doing our OWN game, with our own systems. Currently we are using EU 4 files for testing, to make sure our systems work. We will transition to our own later on.

You, Claude, cannot run tests. I have to do that manually.

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
- Always load data using Scripts/ParadoxParser. It has been highly optimized. If features are lacking, point it out so we can add
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

## Development Workflow

### Before Writing Code:
1. **Check existing implementations** - Look for similar features/patterns already in codebase
2. **Verify the approach** - Ask if unsure about implementation strategy
3. **Consider performance** - This game needs to handle 10,000+ provinces efficiently
4. **Plan for modularity** - Keep files under 500 lines, single responsibility

### Code Quality Checklist:
- [ ] Follows existing naming conventions
- [ ] Uses appropriate Unity systems (Job System, Burst, etc.)
- [ ] Handles edge cases and errors gracefully
- [ ] Maintains separation of concerns
- [ ] Compatible with URP rendering pipeline

## Project Structure

### Key Directories:
```
Assets/
├── Scripts/           # Core game code
│   ├── Parser/       # Paradox file format parser (Burst-optimized)
│   ├── Map/          # Map rendering and province systems
│   ├── UI/           # UI controllers and views
│   └── Systems/      # Game systems (Interest Groups, Policies, etc.)
├── Data/             # Game data files (Paradox format)
│   ├── history/      # Historical start dates
│   ├── common/       # Game definitions
│   └── map/          # Map data and provinces
├── Shaders/          # URP shaders for map rendering
├── Docs/             # Game design and technical documentation
└── Resources/        # Unity resources (textures, materials, etc.)
```

### Critical Files:
- `CLAUDE.md` - This file, your development guide
- `Assets/Docs/texture-based-map-guide.md` - Map rendering implementation plan
- `Assets/Docs/game_design_document.md` - Core game vision (for context only)

## Technical Requirements

### Performance Targets:
- 200+ FPS with 10,000 provinces visible
- Single draw call for base map
- Zero allocations during gameplay
- Sub-1ms province selection

### Unity Configuration:
- **Render Pipeline**: URP (Universal Render Pipeline)
- **Color Space**: Linear
- **Scripting Backend**: IL2CPP
- **Target Platform**: PC (Windows/Mac/Linux)

### Code Patterns to Follow:
```csharp
// Burst-compatible structs
[BurstCompile]
public struct ProvinceData
{
    public int ID;
    public float2 Position;
    // Use blittable types only
}

// Event-driven communication
public static event Action<ProvinceID> OnProvinceSelected;

// Job System for heavy operations
[BurstCompile]
struct ProcessProvincesJob : IJobParallelFor { }
```

### Testing Requirements:
- Manual testing only (you can't run automated tests)
- Always verify compilation before claiming completion
- Test with large datasets (thousands of provinces)
- Check performance with Unity Profiler

## Common Pitfalls to Avoid:
- ❌ Don't create GameObjects for each province (use texture-based rendering)
- ❌ Don't use texture filtering on province ID textures
- ❌ Don't allocate during gameplay (use object pools)
- ❌ Don't readback GPU data every frame
- ❌ Don't forget CBUFFER blocks for SRP Batcher compatibility
