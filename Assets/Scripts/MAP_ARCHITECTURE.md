# Map System Architecture

## Overview
The map system has been refactored from a single 561-line `SimpleBMPMapViewer` into a modular architecture for better development flow.

## New Components

### 1. **SimpleMapViewer.cs** (~20 lines)
- **Purpose**: Minimal entry point, just configuration and startup
- **Responsibilities**: Creates MapInitializer and passes settings
- **Usage**: Replace SimpleBMPMapViewer component with this

### 2. **MapController.cs** (~250 lines)
- **Purpose**: Central coordinator for all map systems
- **Responsibilities**:
  - Manages initialization sequence
  - Provides shared references (texture, plane, camera)
  - Coordinates between subsystems
  - Exposes public API for map operations

### 3. **MapLoader.cs** (~120 lines)
- **Purpose**: Handles BMP loading and texture creation
- **Responsibilities**:
  - BMP file parsing
  - Texture conversion
  - Map plane setup and scaling

### 4. **MapInteractionManager.cs** (~150 lines)
- **Purpose**: Handles user interaction with the map
- **Responsibilities**:
  - Province click detection
  - Province highlighting
  - Neighbor visualization
  - Camera centering

### 5. **MapInitializer.cs** (~60 lines)
- **Purpose**: Bootstrap system with configurable settings
- **Responsibilities**:
  - System initialization
  - Settings management
  - Context menu actions

### 6. **MapSettings.cs** (~25 lines)
- **Purpose**: Configuration data structure
- **Responsibilities**: Centralized settings for all map systems

## Benefits

1. **Separation of Concerns**: Each file has a single, clear responsibility
2. **Easy Extension**: Adding new game systems is straightforward
3. **Better Testing**: Individual systems can be tested in isolation
4. **AI-Friendly**: Smaller files reduce context length for AI development
5. **Maintainability**: Easier to debug and modify specific functionality

## Migration Guide

### From SimpleBMPMapViewer
1. Replace `SimpleBMPMapViewer` component with `SimpleMapViewer`
2. Configure `MapSettings` in the inspector
3. All existing functionality is preserved through the new architecture

### For New Development
- Add new map features by creating new managers
- Hook into MapController for shared resources
- Use MapInitializer for system coordination

## File Sizes
- SimpleMapViewer: ~20 lines
- MapController: ~250 lines
- MapLoader: ~120 lines
- MapInteractionManager: ~150 lines
- MapInitializer: ~60 lines
- MapSettings: ~25 lines

**Total: ~625 lines** (vs original 561 lines, with much better organization)