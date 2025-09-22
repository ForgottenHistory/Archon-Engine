# Unity Native Paradox Parser - Development Guide

## Project Overview

A high-performance, Unity-native parsing system for Paradox Interactive game formats (EU4, CK3, HOI4, VIC3). Built from scratch using Unity's Job System, Burst Compiler, and Native Collections to achieve optimal performance while maintaining full mod compatibility.

### Core Design Principles

- **Zero Preprocessing**: Parse raw Paradox text files fast enough for runtime use
- **Mod-First Architecture**: Maintain compatibility with existing mod ecosystems
- **Unity Native Performance**: Leverage Burst/Jobs/ECS from the ground up
- **Generic Parser Core**: Single parser handles all Paradox format variations
- **Streaming Ready**: Support loading data on-demand for large datasets
- **Development Friendly**: Hot-reload and instant iteration in Unity Editor

### Performance Targets

Based on reference implementation (3,708 files in 1.6 seconds on consumer PC):

| Metric | Reference Parser | Unity Target | Unity Stretch Goal |
|--------|-----------------|--------------|-------------------|
| Files/second | 2,364 | 2,500+ | 3,000+ |
| Parse time (3.7k files) | 1.6s | <1.5s | <1.2s |
| Memory usage | 51.6MB peak | <60MB | <40MB |
| Single province parse | ~0.4ms | <0.3ms | <0.2ms |
| Province map (5632x2048) | N/A | <100ms | <50ms |
| CSV parsing (5k rows) | N/A | <20ms | <10ms |
| GC allocations | Unknown | 0 bytes | 0 bytes |
| Parallel scaling | Single-thread | 6-8x on 8-core | Linear scaling |

## Development Phases

### Phase 1: Foundation & Architecture

#### 1.1 Project Setup
- [x] Install packages: Burst 1.8+, Collections 2.1+, Jobs, Mathematics
- [x] Configure project for IL2CPP backend
- [x] Setup assembly definitions for modular compilation
- [x] Create folder structure for parser systems
- [x] Configure performance testing framework
- [x] Setup memory profiler integration

#### 1.2 Core Data Structures
- [x] Define blittable struct for generic parse nodes
- [x] Create native string pool implementation
- [x] Design hash-based string interning system
- [x] Implement fixed-size string types for common data
- [x] Create native dynamic array for variable-length data
- [x] Define blittable structs
- [x] Setup native collections for runtime storage
- [x] Design spatial data structures for map queries

#### 1.3 Memory Management System
- [x] Implement native memory allocator wrapper
- [x] Create object pooling for parse nodes
- [x] Design reusable buffer system for file I/O
- [x] Setup persistent allocator for game data
- [x] Implement temp allocator for parsing
- [x] Create memory tracking utilities
- [x] Design cache-friendly data layouts

#### 1.4 File I/O System
- [x] Implement async file reading to native buffers
- [x] Create memory-mapped file support for large files
- [x] Design streaming system for partial loading
- [x] Implement file change detection for hot-reload
- [x] Create directory scanning utilities
- [x] Setup file priority/queuing system
- [x] Add compression support detection

#### 1.5 Error Handling Framework
- [x] Design error reporting without exceptions
- [x] Create validation result structures
- [x] Implement error accumulation system
- [x] Setup debug visualization for errors
- [x] Create error recovery strategies
- [x] Design user-friendly error messages

### Phase 2: Generic Parser Core

#### 2.1 Tokenizer Foundation
- [x] Implement byte-level tokenization
- [x] Create token type enumeration
- [x] Design token stream structure
- [x] Implement whitespace handling
- [x] Add comment detection and skipping
- [x] Create line/column tracking
- [x] Setup token caching system

#### 2.2 Burst-Compiled Tokenizer
- [x] Convert tokenizer to Burst job
- [x] Implement parallel tokenization for large files
- [x] Add SIMD optimizations for byte scanning
- [x] Create fast string hashing
- [x] Implement number parsing without allocations
- [x] Add date parsing (YYYY.MM.DD format)
- [x] Optimize operator detection

#### 2.3 Parser State Machine
- [x] Design parser state enumeration
- [x] Implement state transition logic
- [x] Create nested block handling
- [x] Add bracket depth tracking
- [x] Implement key-value pair parsing
- [x] Handle list parsing (space-separated)
- [x] Add support for quoted strings

#### 2.4 Generic Parser Job
- [x] Create IParseJob interface
- [x] Implement recursive descent parser
- [x] Add support for all Paradox operators
- [x] Handle special keywords (yes/no)
- [x] Parse RGB color values
- [x] Support modifier blocks
- [x] Add include file handling

#### 2.5 Parser Validation
- [x] Implement syntax validation (SyntaxValidator.cs)
- [x] Add semantic validation hooks (SemanticValidator.cs)
- [x] Create validation rule system (ValidationResult.cs, ValidationOptions)
- [x] Implement type checking (TypeValidator.cs)
- [x] Add range validation (RangeValidator.cs)
- [x] Create cross-reference validation (CrossReferenceValidator.cs)
- [x] Comprehensive test coverage (ValidationSystemTests.cs)
- [x] FastHasher string overload for convenience
- [x] Generic validation system (no game-specific logic)

### Phase 3: Data Extraction Utilities

**DISCOVERY**: Our generic parser from Phase 2 already handles ALL Paradox file formats perfectly, including province files. Specialized parsers are unnecessary - we just need data extraction utilities.

#### 3.1 Generic Data Extraction 
- [x] **PROVEN**: Generic parser handles province files (ProvinceParsingTest.cs)
- [x] **PROVEN**: Parses key-value pairs (owner, culture, base_tax, etc.)
- [x] **PROVEN**: Handles historical date blocks (1436.4.28 = { ... })
- [x] **PROVEN**: Supports complex nested structures (revolt blocks)
- [x] **PROVEN**: Extracts numeric values and quoted strings
- [ ] Create utility functions for common data extraction patterns
- [ ] Add helper methods for finding specific keys in parsed data
- [ ] Create date-range querying utilities for historical data
- [ ] Add value type conversion helpers (string->int, string->float)
- [ ] Document generic parser usage patterns for different file types

#### 3.2 Country Data Extraction
**NOTE**: Generic parser handles country files same as provinces. Only need extraction utilities.
- [ ] Use generic parser for country files (same format as provinces)
- [ ] Create helper for extracting government data
- [ ] Add utility for technology group extraction
- [ ] Build diplomatic relations extractor
- [ ] Create idea groups parser using generic system
- [ ] Add modifier extraction utilities
- [ ] Support historical ruler data extraction
- [ ] Create flags/variables extraction helpers

#### 3.3 CSV Parser (Day 9)
- [x] Implement high-speed CSV tokenizer (CSVTokenizer.cs)
- [x] Handle Paradox CSV format (semicolons)
- [x] ~~Support Windows-1252 encoding~~ **REPLACED**: Python preprocessing script converts to UTF-8
- [x] Parse province definitions (using generic CSVParser)
- [x] Handle adjacencies data (using generic CSVParser)
- [x] Support localization CSVs (using generic CSVParser)
- [ ] Implement streaming for large files
- [x] Add header detection (automatic in CSVParser.cs)
- [x] Handle quoted fields with special characters
- [x] Comprehensive test coverage (CSVParserTests.cs)
- [x] UTF-8 conversion utility (convert_csv_to_utf8.py)

#### 3.4 Bitmap Parser (Day 9)
- [ ] Implement BMP header parsing
- [ ] Create memory-mapped bitmap reader
- [ ] Support 24-bit and 32-bit formats
- [ ] Implement RGB to province ID mapping
- [ ] Create heightmap parser
- [ ] Add terrain type detection
- [ ] Support river/lake detection
- [ ] Implement climate zone parsing

#### 3.5 Localization Data Extraction
**NOTE**: YAML localization might work with generic parser too
- [ ] Test generic parser with YAML localization files
- [ ] Create multi-language extraction utilities
- [ ] Implement fallback chains
- [ ] Create string replacement system
- [ ] Support colored text markup
- [ ] Handle special characters
- [ ] Implement dynamic key resolution

### Phase 4: Data Management Layer (Week 2-3)

#### 4.1 Native Collection Management (Day 11)
- [ ] Create province storage system
- [ ] Implement country collection
- [ ] Design culture/religion registries
- [ ] Add trade goods database
- [ ] Create modifier storage
- [ ] Implement technology trees
- [ ] Handle idea groups

#### 4.2 Cross-Reference System (Day 11)
- [ ] Build province-country links
- [ ] Create culture group mappings
- [ ] Implement trade node connections
- [ ] Handle diplomatic relations
- [ ] Create adjacency graphs
- [ ] Build area/region hierarchies

#### 4.3 Spatial Query System (Day 12)
- [ ] Implement province map lookups
- [ ] Create region-based queries
- [ ] Add distance calculations
- [ ] Implement pathfinding preparation
- [ ] Create area-of-effect queries
- [ ] Add border detection

#### 4.4 String Management (Day 12)
- [ ] Implement string interning
- [ ] Create localization cache
- [ ] Handle dynamic strings
- [ ] Implement format strings
- [ ] Add pluralization support
- [ ] Create number formatting

#### 4.5 Date/Time System (Day 13)
- [ ] Parse Paradox date format
- [ ] Implement date arithmetic
- [ ] Handle historical bookmarks
- [ ] Create timeline management
- [ ] Support date-based triggers
- [ ] Implement age calculations

### Phase 5: Performance Optimization (Week 3)

#### 5.1 Profiling Infrastructure (Day 14)
- [ ] Setup Unity Profiler markers
- [ ] Implement custom timing system
- [ ] Add memory tracking
- [ ] Create bottleneck detection
- [ ] Setup automated benchmarks
- [ ] Add regression testing

#### 5.2 Parser Optimizations (Day 15)
- [ ] Implement parse node pooling
- [ ] Add token stream caching
- [ ] Optimize string hashing
- [ ] Implement SIMD scanning
- [ ] Add branch prediction hints
- [ ] Optimize memory access patterns
- [ ] Reduce cache misses

#### 5.3 Job System Optimization (Day 16)
- [ ] Balance job granularity
- [ ] Optimize job dependencies
- [ ] Implement batch processing
- [ ] Add work stealing
- [ ] Optimize thread synchronization
- [ ] Reduce job scheduling overhead

#### 5.4 Memory Optimization (Day 17)
- [ ] Implement memory pre-warming
- [ ] Optimize allocation patterns
- [ ] Reduce memory fragmentation
- [ ] Implement data compression
- [ ] Add memory pooling
- [ ] Optimize struct layouts

#### 5.5 I/O Optimization (Day 18)
- [ ] Implement read-ahead buffering
- [ ] Add async I/O pipelines
- [ ] Optimize file access patterns
- [ ] Implement caching layer
- [ ] Add memory-mapped I/O
- [ ] Reduce system calls

### Phase 6: Mod Support System (Week 3-4)

#### 6.1 Mod Detection (Day 19)
- [ ] Scan for .mod files
- [ ] Parse mod descriptors
- [ ] Detect mod dependencies
- [ ] Handle version requirements
- [ ] Implement load order resolution
- [ ] Detect conflicts

#### 6.2 File Override System (Day 19)
- [ ] Implement file replacement logic
- [ ] Handle partial overwrites
- [ ] Support file additions
- [ ] Implement merge strategies
- [ ] Handle deletion markers
- [ ] Create conflict resolution

#### 6.3 Hot Reload System (Day 20)
- [ ] Implement file watchers
- [ ] Create change detection
- [ ] Handle incremental updates
- [ ] Implement safe reload
- [ ] Add rollback support
- [ ] Create reload notifications

#### 6.4 Mod Validation (Day 20)
- [ ] Check syntax validity
- [ ] Validate references
- [ ] Detect missing dependencies
- [ ] Check compatibility
- [ ] Validate file structures
- [ ] Create error reports

#### 6.5 Mod Tools Integration (Day 21)
- [ ] Create mod loading UI
- [ ] Implement mod manager
- [ ] Add conflict visualizer
- [ ] Create performance profiler
- [ ] Add validation tools
- [ ] Implement mod packaging

### Phase 7: Editor Integration (Week 4)

#### 7.1 Custom Inspectors (Day 22)
- [ ] Create province inspector
- [ ] Add country data viewer
- [ ] Implement map visualizer
- [ ] Create performance monitor
- [ ] Add memory inspector
- [ ] Implement validation UI

#### 7.2 Asset Pipeline (Day 22)
- [ ] Create ScriptableObject wrappers
- [ ] Implement asset importers
- [ ] Add asset processors
- [ ] Create asset validation
- [ ] Implement asset caching
- [ ] Add dependency tracking

#### 7.3 Debug Tools (Day 23)
- [ ] Create parsing debugger
- [ ] Add step-through parsing
- [ ] Implement token visualizer
- [ ] Create memory debugger
- [ ] Add performance overlay
- [ ] Implement error highlighting

#### 7.4 Build Pipeline (Day 24)
- [ ] Create build processors
- [ ] Implement data stripping
- [ ] Add platform optimization
- [ ] Create build validation
- [ ] Implement size optimization
- [ ] Add build reporting

### Phase 8: Testing & Validation (Week 4)

#### 8.1 Unit Tests (Day 25)
- [ ] Test tokenizer accuracy
- [ ] Validate parser correctness
- [ ] Test edge cases
- [ ] Verify error handling
- [ ] Test memory management
- [ ] Validate thread safety

#### 8.2 Performance Tests (Day 26)
- [ ] Benchmark against targets
- [ ] Test scaling behavior
- [ ] Measure memory usage
- [ ] Profile hot paths
- [ ] Test cache behavior
- [ ] Validate optimization impact

#### 8.3 Integration Tests (Day 27)
- [ ] Test full game loading
- [ ] Validate mod loading
- [ ] Test hot reload
- [ ] Verify data integrity
- [ ] Test error recovery
- [ ] Validate UI integration

#### 8.4 Stress Tests (Day 28)
- [ ] Test with maximum data
- [ ] Validate memory limits
- [ ] Test concurrent access
- [ ] Verify stability
- [ ] Test error accumulation
- [ ] Validate performance degradation

## Success Criteria

### Quality Metrics

- **Code Coverage**: >80% test coverage
- **Performance**: Consistent 1500+ files/second
- **Memory**: Peak usage under 500MB
- **Reliability**: <0.1% parse failure rate
- **Compatibility**: >80% mod compatibility
- **Maintainability**: Clear separation of concerns

## Risk Mitigation

### Technical Risks
- **Burst Limitations**: Fallback to managed code paths
- **Memory Constraints**: Implement streaming for large datasets
- **Platform Differences**: Test on all target platforms early
- **Threading Issues**: Extensive synchronization testing

### Schedule Risks
- **Scope Creep**: Strict phase boundaries
- **Performance Issues**: Early and continuous profiling
- **Compatibility Problems**: Test with real mod files throughout
- **Integration Delays**: Parallel development of systems
