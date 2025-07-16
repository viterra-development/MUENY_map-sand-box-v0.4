# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

MapSandBox is a C# Blazor WebAssembly application that provides interactive mapping capabilities using both deck.gl and ArcGIS JavaScript API. The application visualizes geospatial data including countries, rivers, airports, and flight paths using Natural Earth datasets.

## Architecture

### Core Technologies
- **Blazor WebAssembly** (.NET 9.0) - Main application framework
- **deck.gl** (v9.0.0-beta.2) - Primary mapping library for WebGL-based visualization
- **ArcGIS JavaScript API** - Alternative mapping implementation
- **JavaScript Interop** - Bridge between C# and JavaScript mapping libraries

### Key Components

#### C# Backend Structure
- **MapService** (`Services/MapService.cs`) - Provides default map configuration and layer definitions
- **MapModels** (`Models/MapModels.cs`) - Data models for map configuration, layers, and events
- **Map Component** (`Components/Map.razor`) - Blazor wrapper for deck.gl integration
- **LayerControl Component** (`Components/LayerControl.razor`) - UI for toggling layer visibility

#### JavaScript Integration
- **map.js** (`wwwroot/js/map.js`) - deck.gl integration with layer management
- **arcgisInterop.js** (`wwwroot/arcgisInterop.js`) - ArcGIS JavaScript API integration
- Both JavaScript modules handle different mapping backends

#### Data Sources
All geospatial data comes from Natural Earth via CloudFront CDN:
- Countries: `ne_50m_admin_0_scale_rank.geojson`
- Rivers: `ne_50m_rivers_lake_centerlines.geojson` 
- Airports: `ne_10m_airports.geojson`
- Flight paths: Generated from airport data using great circle routes

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
```bash
# Generate appsettings.json with API keys from .env file
./generate-appsettings.sh
```

### Development Server
- **HTTP**: http://localhost:5214
- **HTTPS**: https://localhost:7067
- The application automatically opens in browser during development

## Configuration

### Environment Setup
1. Create `.env` file in root directory with:
   ```
   ARCGIS_API_KEY=your_arcgis_api_key_here
   ```

2. Run `./generate-appsettings.sh` to generate `MapSandBox/wwwroot/appsettings.json`

### Map Configuration
- Default map center: London, UK (51.47°N, 0.45°E)
- Default zoom: 3
- Default view: Top-down (90° pitch)
- Layer configurations defined in `MapService.GetDefaultLayers()`

## Key Implementation Details

### Dual Mapping System
The application supports two mapping backends:
1. **deck.gl** - Main implementation with WebGL rendering
2. **ArcGIS** - Alternative implementation with enterprise features

### JavaScript Interop Pattern
- C# components use `IJSObjectReference` for calling JavaScript functions
- JavaScript modules export functions that return object references
- Proper disposal implemented with `IAsyncDisposable`

### Layer Management
- Layers defined in C# models with properties dictionary
- JavaScript handles layer creation and styling based on layer type
- Layer visibility controlled through Blazor UI components

## Project Structure Notes

- `Pages/` - Blazor pages including both deck.gl and ArcGIS implementations
- `Components/` - Reusable Blazor components for mapping functionality
- `Services/` - Business logic and configuration services
- `Models/` - Data models and DTOs
- `wwwroot/js/` - JavaScript modules for mapping integration
- `wwwroot/` - Static assets and configuration files

## Development Notes

- The application uses JavaScript ES6 modules loaded dynamically
- deck.gl is loaded from CDN (unpkg.com) in the browser
- ArcGIS API is loaded using AMD module pattern
- Both mapping systems share the same data sources but have different styling approaches