# CRIS Crash Data Slope Integration Plan

## Overview

This plan outlines the integration of high-quality Digital Elevation Model (DEM) slope data with CRIS crash data to enhance crash risk analysis capabilities. The goal is to replace the current basic slope estimation with precise slope values derived from 1-meter resolution DEM data.

## Current State Analysis

### Existing Slope Calculation
- **Location**: `CrisDataProcessor/Services/ElevationService.cs:17-35`
- **Method**: Basic coordinate difference estimation
- **Limitations**:
  - Not using actual elevation data
  - Inaccurate slope calculations
  - Capped at 15% maximum slope arbitrarily

### Available High-Quality Data Sources
1. **DEM Slope Raster**: `/DEM/parker_1m_dem_cog_slope_deg.tif`
   - 1-meter resolution
   - Slope values in degrees
   - Industry-standard GDAL processing

2. **Processed Slope Tiles**: Available via `parker-slope` layer
   - Already integrated in MapLibreService.cs:210-220
   - Raster tiles for visualization
   - XYZ tile format at zoom levels 8-18

## Implementation Strategy

### Phase 1: Enhanced ElevationService with Raster Sampling

#### Objective
Replace basic slope calculation with precise DEM-based slope extraction at crash point coordinates.

#### Technical Approach
1. **Raster Data Integration**
   - Add GDAL.NET or NetTopologySuite.IO.GeoTiff dependency
   - Implement GeoTIFF reading capability
   - Create coordinate-to-slope sampling function

2. **Service Enhancement**
   - Extend `ElevationService` with DEM sampling methods
   - Add configuration options for DEM file paths

#### Implementation Components

**New Dependencies**
```xml
<!-- Add to CrisDataProcessor.csproj -->
<PackageReference Include="GDAL" Version="3.7.0" />
<PackageReference Include="NetTopologySuite.IO.GeoTiff" Version="4.0.0" />
```

**Enhanced ElevationService Methods**
```csharp
// New methods to add to ElevationService.cs
public decimal GetSlopeFromDEM(double latitude, double longitude)
public void EnhanceRoadSegmentsWithDEMSlope(List<RiskSegment> segments)
public decimal SampleSlopeRaster(double x, double y)
```

**Configuration Updates**
```json
// Add to CrisDataProcessor appsettings.json
"DEMConfiguration": {
  "SlopeRasterPath": "/DEM/parker_1m_dem_cog_slope_deg.tif",
  "EnableDEMSampling": true
}
```

### Phase 2: Crash Point Slope Analysis

#### Enhanced Crash Data Structure
Add slope information to crash records:
```csharp
public class EnhancedCrashRecord : CrashRecord
{
    public decimal SlopeAtLocation { get; set; }  // Degrees
    public decimal SlopePercentage { get; set; }  // Percentage
    public string SlopeCategory { get; set; }     // Flat/Moderate/Steep
}
```

#### Slope-Based Risk Analysis
1. **Slope Categories**
   - Flat: 0-2 degrees
   - Moderate: 2-5 degrees
   - Steep: 5+ degrees

2. **Enhanced Risk Metrics**
   - Slope-adjusted crash severity scores
   - Slope-based crash pattern analysis
   - Integration with existing environmental factors

### Phase 3: Visualization and Analysis Integration

#### MapSandBox Integration
1. **New Visualization Layers**
   - Crash points colored by slope category
   - Slope-risk correlation heatmaps
   - High-slope crash concentration areas

2. **Enhanced Analytics**
   - Slope vs. crash severity correlation analysis
   - Slope-based crash hotspot identification
   - Environmental factor interaction analysis

#### Popup Enhancements
Update crash point popups to display:
- Slope at crash location (degrees and percentage)
- Slope category classification
- Slope-related risk factors

## Implementation Timeline

### Week 1: Foundation
- [ ] Add GDAL/GeoTIFF dependencies to CrisDataProcessor
- [ ] Implement basic DEM raster reading functionality
- [ ] Create slope sampling methods
- [ ] Unit tests for raster sampling accuracy

### Week 2: Integration
- [ ] Enhance ElevationService with DEM-based calculations
- [ ] Update crash data models to include slope information
- [ ] Integrate slope calculation into CRIS processing pipeline
- [ ] Validation against known slope values

### Week 3: Analysis Enhancement
- [ ] Implement slope-based risk analysis algorithms
- [ ] Create slope category classification system
- [ ] Enhance crash clustering to consider slope factors
- [ ] Generate slope-enhanced GeoJSON outputs

### Week 4: Visualization
- [ ] Add slope-based visualization layers to MapSandBox
- [ ] Implement slope-colored crash point rendering
- [ ] Enhance popup components with slope information
- [ ] Create slope-crash correlation analysis tools

## Technical Specifications

### Coordinate System Handling
- **DEM Coordinate System**: Ensure proper CRS handling for sampling
- **Crash Coordinates**: Validate coordinate system alignment
- **Projection**: Handle any necessary coordinate transformations

### Performance Considerations
1. **Raster Caching**: Implement in-memory caching for frequently accessed areas
2. **Batch Processing**: Process crash points in spatial batches for efficiency

### Data Validation
1. **Slope Range Validation**: Verify reasonable slope values (0-90 degrees)
2. **Coordinate Bounds**: Ensure crash points fall within DEM coverage area
3. **Quality Metrics**: Track sampling success rates and data quality

## Expected Outcomes

### Enhanced Accuracy
- Replace estimated slopes with precise 1-meter DEM-derived values
- Improve crash risk assessment accuracy through environmental factors
- Enable slope-based crash pattern identification

### New Analysis Capabilities
- Slope-severity correlation analysis
- Environmental factor interaction studies
- Enhanced crash hotspot identification
- Slope-based risk prediction models

### Improved Visualization
- More accurate environmental context for crash analysis
- Enhanced interactive mapping with slope information
- Better decision-making tools for traffic safety planning

## Risk Mitigation

### Technical Risks
1. **Dependency Issues**: Test GDAL integration thoroughly
2. **Performance Impact**: Monitor processing time with large datasets
3. **Memory Usage**: Implement efficient raster handling

### Data Quality Risks
1. **Coordinate Misalignment**: Validate coordinate system compatibility
2. **DEM Coverage**: Handle areas outside DEM bounds gracefully
3. **Sampling Accuracy**: Validate against known ground truth data

## Success Metrics

1. **Accuracy Improvement**: Compare DEM-based vs. basic slope calculations
2. **Processing Performance**: Maintain reasonable processing times
3. **Analysis Enhancement**: Measure improvement in crash risk predictions
4. **User Adoption**: Track usage of slope-enhanced features in MapSandBox

## Future Enhancements

### Advanced DEM Integration
- Multi-resolution DEM handling
- Slope aspect (direction) analysis
- Terrain roughness calculations
- Hydrology integration with slope data

### Machine Learning Integration
- Slope-based crash prediction models
- Environmental factor correlation analysis
- Risk pattern recognition using slope data

This plan provides a comprehensive roadmap for integrating high-quality DEM slope data into the CRIS crash analysis pipeline, significantly enhancing the accuracy and analytical capabilities of the crash risk assessment system.