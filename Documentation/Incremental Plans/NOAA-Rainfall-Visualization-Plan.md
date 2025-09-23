# NOAA Rainfall Data Visualization Implementation Plan

## Overview
This plan outlines the implementation of NOAA rainfall data visualization using the existing MapSandBox deck.gl platform. The goal is to integrate 2-year rainfall data (5% annual exceedance probability) clipped to Parker County, Texas boundary into the current mapping system.

## Data Analysis
**Source Files**:
- **Rainfall Data**: `/NOAA/tx2yr05ma.asc` (Texas statewide)
- **Boundary**: `/CrisDataProcessor/Data/parker-county-boundary.geojson`

**Rainfall Grid Specifications**:
- **Format**: ASCII Grid (ESRI .asc format)
- **Dimensions**: 1579 columns × 1282 rows
- **Spatial Coverage**: Texas (xllcorner: -106.65°, yllcorner: 25.83°)
- **Resolution**: 0.008333° (~1km grid cells)
- **Coordinate System**: NAD83 Geographic (EPSG:4269)
- **Data Values**: 373-410+ (likely 0.01 inches units)
- **No Data Value**: -9

**Parker County Boundary**:
- **Format**: GeoJSON Polygon
- **Coverage**: Parker County, Texas
- **Existing Integration**: Already used in CRIS data processing

## Implementation Strategy: Server-Side Preprocessing

### Phase 1: Data Conversion and Clipping
**Objective**: Convert ASCII Grid to deck.gl-compatible GeoJSON format, clipped to Parker County

#### Step 1.1: Create NoaaDataProcessor Utility
- **Project**: New `NoaaDataProcessor` .NET 9.0 console application
- **Pattern**: Follow existing `CrisDataProcessor` and `SoilDataProcessor` architecture
- **Dependencies**:
  - `NetTopologySuite` (spatial operations and geometry handling)
  - `NetTopologySuite.IO.GeoJSON` (GeoJSON serialization)
  - `System.Text.Json` (JSON processing)
  - `Microsoft.Extensions.Configuration` (configuration management)
  - `Microsoft.Extensions.Logging` (logging infrastructure)
- **Input Sources**:
  - `/workspaces/map-sand-box/NOAA/tx2yr05ma.asc` (rainfall ASCII grid)
  - `/workspaces/map-sand-box/CrisDataProcessor/Data/parker-county-boundary.geojson` (clipping boundary)
- **Output**: `/workspaces/map-sand-box/MapSandBox/wwwroot/noaa-rainfall-parker-county.geojson`
- **Core Processing**:
  - ASCII grid parser for ESRI .asc format
  - Grid cell to geographic coordinate conversion
  - **Spatial clipping** using NetTopologySuite point-in-polygon operations
  - GeoJSON feature generation with rainfall properties
  - Data validation and filtering (remove -9 no-data values)

#### Step 1.2: Optimize Data Size
- **Benefit of County Clipping**: Dramatically reduces data size (~99% reduction from statewide)
- **Estimated Output**: ~1,000-2,000 points instead of ~800K statewide
- **Value Precision**: Round rainfall values to appropriate precision
- **Coordinate Precision**: Limit decimal places for web performance

### Phase 2: Integration with MapSandBox

#### Step 2.1: Add to Static Assets
- **Location**: `/workspaces/map-sand-box/MapSandBox/wwwroot/noaa-rainfall-parker-county.geojson`
- **Size**: Small enough (~1-2K points) to serve directly without CDN
- **Integration**: Follow existing pattern like parker-county-roads.geojson

#### Step 2.2: Extend MapService Configuration
- **File**: `Services/MapService.cs`
- **Add Layer Definition**:
  ```csharp
  new LayerConfig
  {
      Id = "noaa-rainfall-parker",
      Name = "NOAA Rainfall (Parker County)",
      Type = "heatmap", // or "grid"
      Visible = false,
      Properties = new Dictionary<string, object>
      {
          ["dataUrl"] = "/noaa-rainfall-parker-county.geojson",
          ["valueProperty"] = "rainfall",
          ["colorScale"] = "blues",
          ["radiusScale"] = 500,
          ["elevationScale"] = 100
      }
  }
  ```

#### Step 2.3: Implement deck.gl Layer
- **File**: `wwwroot/js/map.js`
- **Layer Type Options**:
  - **GridLayer**: For cell-based visualization
  - **HeatmapLayer**: For smooth interpolated surface
  - **ScatterplotLayer**: For point-based with color coding

### Phase 3: UI Integration

#### Step 3.1: Layer Control Updates
- **File**: `Components/LayerControl.razor`
- **Add rainfall layer toggle**
- **Consider layer group organization**

#### Step 3.2: Legend/Color Scale
- **Add rainfall value legend**
- **Color scale reference (inches/mm)**
- **Interactive tooltips showing rainfall values**

### Phase 4: NoaaDataProcessor Project Structure

#### Step 4.1: Project Setup
**Directory Structure** (following existing patterns):
```
/workspaces/map-sand-box/NoaaDataProcessor/
├── NoaaDataProcessor.csproj      # .NET 9.0 console app
├── Program.cs                    # Main entry point and processing logic
├── appsettings.json             # Configuration (paths, settings)
├── Services/                    # Optional: Extract services if complex
│   ├── AsciiGridParser.cs       # ASCII grid parsing logic
│   ├── SpatialClippingService.cs # Spatial operations
│   └── GeoJsonExporter.cs       # GeoJSON output generation
└── Models/                      # Optional: Data models
    ├── GridHeader.cs            # ASCII grid header model
    ├── GridCell.cs              # Grid cell data model
    └── RainfallPoint.cs         # Rainfall point feature model
```

#### Step 4.2: Configuration Management
**appsettings.json**:
```json
{
  "InputPaths": {
    "RainfallGrid": "/workspaces/map-sand-box/NOAA/tx2yr05ma.asc",
    "CountyBoundary": "/workspaces/map-sand-box/CrisDataProcessor/Data/parker-county-boundary.geojson"
  },
  "OutputPaths": {
    "RainfallGeoJson": "/workspaces/map-sand-box/MapSandBox/wwwroot/noaa-rainfall-parker-county.geojson"
  },
  "Processing": {
    "NoDataValue": -9,
    "CoordinatePrecision": 6,
    "RainfallPrecision": 2
  }
}
```

#### Step 4.3: Solution Integration
- **Add to main solution**: Follow `CrisDataProcessor` and `SoilDataProcessor` pattern
- **Build order**: Independent utility that can be run separately
- **CI/CD**: Can be integrated into data processing pipeline if needed

## Technical Specifications

### Data Conversion Details
```
Input Grid Specs:
- Columns: 1579 (statewide)
- Rows: 1282 (statewide)
- Total Cells: ~2M (before filtering no-data)
- Parker County Subset: ~1,000-2,000 valid cells (99% reduction)
- Estimated File Size: <100KB (vs ~25MB statewide)
```

### Expected Output
```json
{
  "type": "FeatureCollection",
  "features": [
    {
      "type": "Feature",
      "geometry": {
        "type": "Point",
        "coordinates": [-97.758, 32.657]
      },
      "properties": {
        "rainfall": 385,
        "rainfallInches": 3.85
      }
    }
  ]
}
```
**Note**: Coordinates now reflect Parker County area (~97.8°W, 32.7°N) instead of statewide Texas

### Deck.gl Layer Configuration
```javascript
// GridLayer option
new GridLayer({
  id: 'noaa-rainfall',
  data: rainfallData,
  getPosition: d => d.geometry.coordinates,
  getWeight: d => d.properties.rainfall,
  cellSize: 1000,
  colorRange: RAINFALL_COLOR_SCALE
});

// HeatmapLayer option
new HeatmapLayer({
  id: 'noaa-rainfall-heatmap',
  data: rainfallData,
  getPosition: d => d.geometry.coordinates,
  getWeight: d => d.properties.rainfall,
  radiusPixels: 50
});
```

## Implementation Timeline

### Phase 1: NoaaDataProcessor Development (Week 1)
- [ ] Create `NoaaDataProcessor` project structure
- [ ] Implement ASCII grid parser service
- [ ] Add spatial clipping with NetTopologySuite
- [ ] Generate and validate GeoJSON output
- [ ] Add configuration and logging infrastructure
- [ ] Test with Parker County boundary

### Phase 2: MapSandBox Integration (Week 2)
- [ ] Add rainfall layer to MapService configuration
- [ ] Implement deck.gl HeatmapLayer rendering
- [ ] Add LayerControl UI integration
- [ ] Test layer visibility and interaction
- [ ] Validate performance with existing layers

### Phase 3: Polish and Documentation (Week 3)
- [ ] Add rainfall value legend and color scale
- [ ] Implement click tooltips for rainfall values
- [ ] Update project documentation
- [ ] Add NoaaDataProcessor to solution build process

## Considerations

### Data Quality
- **Coordinate Validation**: Verify projection accuracy
- **Value Validation**: Confirm rainfall units and ranges
- **Temporal Context**: Document data vintage and methodology

### Performance
- **File Size**: ~1-2K points (highly optimized due to county clipping)
- **Rendering**: Excellent performance with small dataset
- **Memory Usage**: Minimal impact on browser performance

### User Experience
- **Layer Interaction**: Click for rainfall values
- **Visual Clarity**: Appropriate opacity and color scales
- **Context**: Clear labeling of units and time period

### Future Enhancements
- **Multi-temporal Data**: Support for different return periods
- **Animation**: Time-series rainfall visualization
- **Analysis Tools**: Rainfall statistics and querying
- **Export**: Allow data download for selected areas

## Dependencies
- **Existing MapSandBox components**: MapService, LayerControl, deck.gl integration
- **Data Processing**: `NoaaDataProcessor` .NET 9.0 utility project
- **Spatial Libraries**:
  - `NetTopologySuite` (already used in `CrisDataProcessor` and `SoilDataProcessor`)
  - `NetTopologySuite.IO.GeoJSON` (consistent with existing projects)
- **Project References**:
  - Reference to `MapSandBox.Shared` (following `SoilDataProcessor` pattern)
  - Optional reference to main `MapSandBox` project if needed (following `CrisDataProcessor` pattern)

## Success Metrics
- [ ] `NoaaDataProcessor` successfully creates Parker County rainfall GeoJSON
- [ ] Accurate spatial positioning of rainfall data within county boundaries
- [ ] Smooth rendering performance with ~1-2K data points
- [ ] Intuitive LayerControl integration following existing patterns
- [ ] Proper rainfall value interpretation and units display
- [ ] Compatible with existing deck.gl layer system
- [ ] Consistent with `CrisDataProcessor` and `SoilDataProcessor` architecture