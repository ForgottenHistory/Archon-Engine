# Dominion - Claude Documentation

## Project Overview
Dominion is a passion project focused on creating a high-performance, modern Unity-based grand strategy game experience inspired by Paradox Interactive's Clausewitz engine. The project emphasizes performance and speed while maintaining the depth and complexity of traditional grand strategy games.

## Core Philosophy
- **Performance First**: All systems are designed with performance optimization as a primary concern
- **Modern Unity Architecture**: Leverages Unity's Job System, Burst Compiler, and Native Collections
- **Clausewitz-Inspired**: Replicates the province-based map system and adjacency detection of Paradox games
- **Scalable Design**: Built to handle large-scale maps with thousands of provinces

## Architecture Overview

### Province System
The core of Dominion is built around a province-based map system similar to Paradox games:

#### Key Components:
- **ProvinceDataService**: Central data management for all province information
- **OptimizedProvinceMeshGenerator**: High-performance mesh generation for 3D province visualization
- **ProvinceManager**: Handles province interactions, selection, and neighbor relationships
- **FastAdjacencyScanner**: Clausewitz-style adjacency detection with modern optimizations

#### Performance Features:
- Unity Job System integration for parallel processing
- Burst-compiled adjacency detection
- Native Collections for memory-efficient data handling
- Event-driven initialization sequencing
- Comprehensive failsafes against infinite loops

### Map Generation Pipeline
1. **Province Map Loading**: BMP bitmap loading with custom Color32 parsing
2. **Province Analysis**: Multi-threaded province identification and pixel analysis
3. **Mesh Generation**: Optimized 3D mesh creation with multiple generation methods
4. **Adjacency Calculation**: Fast neighbor detection using bitmap scanning
5. **Neighbor Integration**: Event-driven system ensuring proper initialization order

### Performance Optimizations

#### Failsafe Systems
All major systems include comprehensive failsafes to prevent Unity lockups:
- **Pixel Check Limits**: Max 10,000 pixel comparisons per province border check
- **Iteration Limits**: BFS pathfinding limited to 50,000 iterations
- **Timeout Systems**: Job system operations have 30-second timeouts
- **Progress Monitoring**: Regular timeout checks during intensive operations
- **Graceful Degradation**: Automatic fallback to traditional methods when limits exceeded

#### Memory Management
- Native Collections with proper disposal patterns
- Efficient Color32 comparers for dictionary operations
- Minimal garbage collection through struct-based job systems
- Pooled object patterns where appropriate

## Key Systems

### FastAdjacencyScanner
Modern implementation of Clausewitz-style adjacency detection:
- **Single-pass bitmap scanning**: Efficient neighbor detection
- **Parallel processing**: Burst-compiled job system implementation
- **Color-to-ID mapping**: Seamless integration with province data
- **Export compatibility**: CSV format matching Paradox adjacencies.csv

### Province Mesh Generation
Multiple mesh generation methods for different use cases:
- **MergedRectangles**: Fast rectangular approximation
- **DetailedContours**: Accurate province shapes
- **Optimized batching**: Reduces draw calls through mesh combining

### Camera System
Paradox-style camera controller:
- **Strategic zoom levels**: Smooth zoom from province detail to world overview
- **Edge scrolling**: Traditional RTS-style camera movement
- **Focus targeting**: Automatic centering on selected provinces

## Project Structure

### Core Scripts
- `Assets/Scripts/Managers/ProvinceManager.cs` - Province interaction and management
- `Assets/Scripts/Map/OptimizedProvinceMeshGenerator.cs` - 3D mesh generation
- `Assets/Scripts/Map/FastAdjacencyScanner.cs` - Adjacency detection system
- `Assets/Scripts/Services/ProvinceDataService.cs` - Central data management
- `Assets/Scripts/TestMap.cs` - Main initialization and testing

### Performance Scripts
- `Assets/Scripts/Jobs/ProvinceNeighborDetectionJob.cs` - Parallel neighbor detection
- `Assets/Scripts/Jobs/AdjacencyScanJob.cs` - Burst-compiled adjacency scanning

### Utilities
- `Assets/Scripts/Utils/BMPLoader.cs` - Custom BMP file loading
- `Assets/Scripts/Camera/ParadoxStyleCameraController.cs` - Strategic camera system

## Development Guidelines

### Performance Considerations
1. **Always use Native Collections** for job system operations
2. **Implement timeout systems** for any potentially long-running operations
3. **Prefer event-driven architecture** over polling for state changes
4. **Use Burst compilation** for mathematical operations
5. **Test with large datasets** (10,000+ provinces) to ensure scalability

### Code Standards
- Comprehensive failsafes in all loops and recursive operations
- Event-driven initialization to prevent race conditions
- Reflection-based component integration where necessary
- Detailed logging for performance monitoring

### Testing Protocol
- Test with various map sizes (small: 100 provinces, medium: 1,000, large: 10,000+)
- Verify all timeout systems function correctly
- Performance profile all major operations
- Validate memory usage patterns

## Known Limitations

### Current Constraints
- Traditional method limited to 2,000 provinces (60-second timeout)
- Job system method limited to 5,000 provinces (safety threshold)
- Pixel border checking capped at 10,000 comparisons per province pair
- Path reconstruction limited to 10,000 steps

### Future Optimizations
- Implement LOD system for distant provinces
- Add spatial partitioning for faster neighbor queries
- Consider texture streaming for very large maps
- Investigate GPU-based province rendering

## Build Commands

### Testing
```
npm run test  # If test framework exists
```

### Linting/Validation
```
# Unity built-in validation
# Check for compilation errors in Unity Console
```

## Hardware Requirements

### Minimum Specifications
- Unity 2022.3 LTS or newer
- 8GB RAM (for medium-sized maps)
- Multi-core CPU (job system utilization)
- DirectX 11 compatible GPU

### Recommended Specifications
- 16GB+ RAM (for large maps with 5,000+ provinces)
- 8+ core CPU (optimal job system performance)
- Dedicated GPU with 4GB+ VRAM

## Contributing Guidelines

### Performance-First Development
1. All new features must include performance benchmarks
2. Memory allocation should be minimized in hot paths
3. Job system integration required for computationally intensive operations
4. Comprehensive failsafe implementation mandatory

### Code Review Checklist
- [ ] Timeout systems implemented for loops
- [ ] Native Collections properly disposed
- [ ] Event-driven architecture maintained
- [ ] Performance tested with large datasets
- [ ] Memory usage validated
- [ ] Failsafe logging includes helpful debugging information

## License
This is a passion project. Please respect the educational and personal nature of this work.

---

*Dominion - Building the future of grand strategy gaming with modern performance and classic depth.*