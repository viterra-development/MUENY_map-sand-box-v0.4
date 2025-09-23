# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

MapSandBox is a C# Blazor WebAssembly application that provides interactive mapping capabilities using multiple mapping backends: deck.gl, ArcGIS JavaScript API, and MapLibre GL JS with deck.gl overlay. The application visualizes geospatial data including Natural Earth datasets and local Parker County, Texas geospatial data.

## Architecture

### Core Technologies
- **Blazor WebAssembly** (.NET 9.0) - Main application framework
- **deck.gl** (v9.0.0-beta.2) - Primary mapping library for WebGL-based visualization
- **ArcGIS JavaScript API** - Enterprise mapping implementation
- **MapLibre GL JS** - Open-source vector tile mapping with deck.gl overlay integration
- **JavaScript Interop** - Bridge between C# and JavaScript mapping libraries

### Key Components

#### C# Backend Structure
- **MapService** (`Services/MapService.cs`) - Provides default map configuration and layer definitions for deck.gl
- **MapLibreService** (`Services/MapLibreService.cs`) - Configuration service for MapLibre integration with base map styles
- **MapModels** (`Models/MapModels.cs`) - Data models for map configuration, layers, and events
- **MapLibreModels** (`Models/MapLibreModels.cs`) - Models specific to MapLibre configuration and base map styles
- **Map Component** (`Components/Map.razor`) - Blazor wrapper for pure deck.gl integration
- **MapLibreMap Component** (`Components/MapLibreMap.razor`) - Blazor wrapper for MapLibre + deck.gl integration
- **LayerControl Component** (`Components/LayerControl.razor`) - UI for toggling layer visibility

#### JavaScript Integration
- **map.js** (`wwwroot/js/map.js`) - Pure deck.gl integration with layer management
- **maplibre-deckgl-integration.js** (`wwwroot/js/maplibre-deckgl-integration.js`) - MapLibre GL JS with deck.gl overlay integration
- **arcgisInterop.js** (`wwwroot/arcgisInterop.js`) - ArcGIS JavaScript API integration
- Each JavaScript module handles different mapping backends with shared layer configuration patterns

#### Data Sources
**Natural Earth (via CloudFront CDN):**
- Countries: `ne_50m_admin_0_scale_rank.geojson`
- Rivers: `ne_50m_rivers_lake_centerlines.geojson` 
- Airports: `ne_10m_airports.geojson`
- Flight paths: Generated from airport data using great circle routes

**Local Parker County, Texas Data:**
- Parker County Roads: `/parker-county-roads.geojson` (TIGER/Line data)
- County CAD Parcels: `/sample-data/county-cad-parcel-test.geojson` (test parcel data)

## Development Commands

### Building and Running
```bash
# Build the project
dotnet build MapSandBox.sln

# Run the application in development mode
dotnet run --project MapSandBox

# Run with specific profile
dotnet run --project MapSandBox --launch-profile https
```

### Configuration Setup
Configuration files are already present in the repository. No additional setup required for basic functionality.

### Development Server
- **HTTP**: http://localhost:5214
- **HTTPS**: https://localhost:7067
- The application automatically opens in browser during development

## Configuration

### Environment Setup
Configuration files are already present in the repository - no additional setup required.

### Map Configuration
**deck.gl Configuration (MapService):**
- Default map center: London, UK (51.47°N, 0.45°E)
- Default zoom: 3
- Default view: Top-down (90° pitch)
- Layer configurations defined in `MapService.GetDefaultLayers()`

**MapLibre Configuration (MapLibreService):**
- Default map center: Parker County, TX (32.758°N, 97.65°W)
- Default zoom: 14 (city-level view)
- Default view: Flat (0° pitch)
- Default base map: Voyager style (Carto)
- Available base map styles: Light, Dark, Voyager, OpenStreetMap

## Key Implementation Details

### Dual Mapping System
The application supports two mapping backends:
1. **deck.gl** - Pure WebGL implementation for high-performance data visualization
2. **MapLibre + deck.gl** - Hybrid approach combining vector tile base maps with deck.gl overlays

### JavaScript Interop Pattern
- C# components use `IJSObjectReference` for calling JavaScript functions
- JavaScript modules export functions that return object references
- Proper disposal implemented with `IAsyncDisposable`

### Layer Management
- Layers defined in C# models with properties dictionary
- JavaScript handles layer creation and styling based on layer type
- Layer visibility controlled through Blazor UI components

## Project Structure Notes

- `Pages/` - Blazor pages for deck.gl and MapLibre implementations
- `Components/` - Reusable Blazor components for mapping functionality
- `Services/` - Business logic and configuration services
- `Models/` - Data models and DTOs
- `wwwroot/js/` - JavaScript modules for mapping integration
- `wwwroot/` - Static assets and configuration files

## Documentation Structure

The project maintains organized documentation to track development evolution and current state:

### `/Documentation/` - Current Documentation
- **`DATA_PROCESSING_README.md`** - Comprehensive data processing pipeline documentation
- Contains up-to-date documentation that should be referenced AND updated when making changes
- This is the authoritative source for current system architecture and processes

### `/Documentation/Future Enhancements/` - Unimplemented Plans
- Contains planning documents for features **not yet implemented**
- Use these to understand planned future work and architectural directions
- Example: `Soil-Data-Viewport-Optimization.md` - viewport-based soil data loading optimization

### `/Documentation/Incremental Plans/` - Implementation History
- Contains ~27 planning documents for features that **have been executed**
- Shows the iterative evolution of the codebase over time
- Reference only to understand historical development decisions and implementation approaches
- Examples include TCDS implementation, CRIS processing, DEM integration, traffic data matching

**Documentation Usage Guidelines:**
- **For current system understanding**: Use `/Documentation/` root files
- **For future planning**: Reference `/Documentation/Future Enhancements/`
- **For development history**: Reference `/Documentation/Incremental Plans/` (read-only)

## Development Notes

- The application uses JavaScript ES6 modules loaded dynamically
- deck.gl is loaded from CDN (unpkg.com) in the browser
- MapLibre GL JS is loaded from CDN with deck.gl overlay integration
- All mapping systems share similar layer configuration patterns but handle rendering differently
- MapLibre integration uses `MapboxOverlay` from deck.gl for compatibility
- We are using deck.gl v9.1.14