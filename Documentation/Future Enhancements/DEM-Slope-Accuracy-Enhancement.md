# DEM Slope Accuracy Enhancement Plan

## Overview

Current slope calculations use single-point DEM sampling at exact crash coordinates, which can result in micro-topography noise and values that don't match visual ground-truth assessment. This document outlines approaches to improve slope accuracy by implementing spatial averaging and road-aware sampling techniques.

## Current System Analysis

### Existing Implementation
- **Service**: `CrisDataProcessor/Services/GdalCommandLineService.cs:73-155`
- **Method**: Single-point sampling using `gdallocationinfo`
- **DEM Source**: 1m resolution Parker County slope raster (`parker_1m_dem_cog_slope_deg_new.tif`)
- **Issue**: Point sampling captures micro-topography artifacts rather than road-representative slopes

### Identified Problems
1. **Micro-topography Noise**: 1m resolution captures every small terrain feature, ditch edge, or data artifact
2. **Off-road Sampling**: Crash coordinates may be slightly offset from actual road centerline
3. **Localized Anomalies**: Construction zones, drainage features, or DEM processing artifacts at specific pixels
4. **Ground-truth Mismatch**: Calculated slopes don't match visual street view assessment

## Proposed Enhancement Approaches

### Approach 1: Spatial Buffer Averaging

**Concept**: Sample multiple points within a buffer around the crash location and calculate weighted average.

**Implementation Strategy**:
```csharp
public decimal GetSlopeAtCoordinateWithBuffer(double latitude, double longitude, int bufferRadius = 5)
{
    // Sample points in a grid pattern within buffer
    var samplePoints = GenerateGridSamples(latitude, longitude, bufferRadius, gridSpacing: 2);
    var validSlopes = new List<(decimal slope, double weight)>();

    foreach (var point in samplePoints)
    {
        var slope = GetSlopeAtCoordinate(point.lat, point.lon);
        var weight = CalculateDistanceWeight(point, centerCoordinate);
        if (slope > 0) validSlopes.Add((slope, weight));
    }

    return CalculateWeightedAverage(validSlopes);
}
```

**Benefits**:
- Reduces micro-topography noise
- More representative of road corridor conditions
- Handles coordinate precision issues

**Configuration**:
- Default buffer radius: 5-10 meters
- Grid sampling density: 2m spacing
- Distance-weighted averaging

### Approach 2: Road-Aware Sampling

**Concept**: Align sampling with actual road geometry by finding nearest road segment and sampling along the road centerline.

**Implementation Strategy**:
```csharp
public decimal GetRoadAlignedSlope(double latitude, double longitude, List<RoadSegment> roadNetwork)
{
    // Find nearest road segment within tolerance
    var nearestRoad = FindNearestRoadSegment(latitude, longitude, roadNetwork, maxDistance: 50);

    if (nearestRoad != null)
    {
        // Sample along road centerline ±10m
        var roadSamples = SampleAlongRoadSegment(nearestRoad, bufferDistance: 10);
        return CalculateAverageSlope(roadSamples);
    }

    // Fallback to buffer sampling if no road found
    return GetSlopeAtCoordinateWithBuffer(latitude, longitude);
}
```

**Benefits**:
- Ensures sampling occurs on actual road surface
- Accounts for road engineering (cut/fill, grading)
- Most relevant for crash analysis

**Requirements**:
- Integration with existing road network data (`parker-county-roads.geojson`)
- Spatial indexing for efficient nearest-road lookups
- Road segment interpolation algorithms

### Approach 3: Multi-Scale Validation

**Concept**: Use multiple sampling scales to validate slope calculations and flag potential data quality issues.

**Implementation Strategy**:
```csharp
public SlopeAnalysisResult GetValidatedSlope(double latitude, double longitude)
{
    var result = new SlopeAnalysisResult();

    // Primary sampling: 5m buffer (road corridor)
    result.PrimarySlope = GetSlopeAtCoordinateWithBuffer(latitude, longitude, bufferRadius: 5);

    // Context sampling: 15m buffer (broader terrain)
    result.ContextSlope = GetSlopeAtCoordinateWithBuffer(latitude, longitude, bufferRadius: 15);

    // Quality assessment
    result.SlopeDifference = Math.Abs(result.PrimarySlope - result.ContextSlope);
    result.QualityFlag = result.SlopeDifference > 3 ? "HIGH_VARIATION" : "CONSISTENT";

    return result;
}
```

**Benefits**:
- Quality assurance for slope calculations
- Identifies problematic locations requiring manual review
- Provides confidence metrics for slope data

## Technical Implementation Details

### GDAL Enhancement Options

#### Option 1: Multiple Point Sampling
```bash
# Current: Single point
gdallocationinfo -wgs84 -valonly slope.tif lon lat

# Enhanced: Grid sampling with custom script
for point in grid_around_coordinate:
    gdallocationinfo -wgs84 -valonly slope.tif point.lon point.lat
```

#### Option 2: Resampling with Averaging
```bash
# Create averaged raster for specific area
gdalwarp -tr 5 5 -r average -te $minx $miny $maxx $maxy slope.tif temp_averaged.tif
gdallocationinfo -wgs84 -valonly temp_averaged.tif lon lat
```

### Configuration Enhancements

**New Configuration Parameters**:
```json
{
  "DEMConfiguration": {
    "SlopeRasterPath": "/path/to/slope.tif",
    "EnableDEMSampling": true,
    "SamplingMethod": "BUFFER_AVERAGE", // POINT | BUFFER_AVERAGE | ROAD_ALIGNED
    "BufferRadius": 5,
    "GridSpacing": 2,
    "EnableMultiScale": true,
    "QualityThreshold": 3.0
  }
}
```

### Performance Considerations

- **Caching**: Cache slope results for coordinate clusters
- **Batch Processing**: Process multiple coordinates in single GDAL calls
- **Spatial Indexing**: Use R-tree for efficient road network queries
- **Memory Management**: Clean up temporary rasters for resampling approach

## Data Quality Improvements

### Enhanced Metrics Tracking

Expand existing quality metrics in `GdalCommandLineService.cs:157-186`:

```csharp
public class EnhancedQualityMetrics
{
    public int TotalSamples { get; set; }
    public int SuccessfulSamples { get; set; }
    public int OutOfBoundsSamples { get; set; }
    public int ErrorSamples { get; set; }
    public int InvalidValueSamples { get; set; }

    // New quality indicators
    public int HighVariationSamples { get; set; }
    public int RoadAlignedSamples { get; set; }
    public double AverageBufferStandardDeviation { get; set; }
    public List<QualityFlag> QualityFlags { get; set; }
}
```

### Validation Against Ground Truth

- Compare enhanced slopes against street view imagery assessment
- Flag locations with high discrepancy for manual review
- Build confidence intervals for slope calculations

## Implementation Phases

### Phase 1: Buffer Averaging
- Implement basic spatial buffer averaging
- Add configuration options
- Test against problematic coordinates identified

### Phase 2: Road-Aware Sampling
- Integrate with road network data
- Implement nearest-road algorithms
- Performance optimization for spatial queries

### Phase 3: Multi-Scale Validation
- Add quality assessment framework
- Implement validation reporting
- Create manual review workflows for flagged locations

## Expected Outcomes

### Immediate Benefits
- More accurate slope values matching visual ground-truth
- Reduced micro-topography noise in crash analysis
- Better correlation with actual driving conditions

### Long-term Value
- Higher confidence in slope-based risk assessment
- Improved crash factor analysis accuracy
- Foundation for additional terrain-aware analytics

## Related Files for Implementation

- **Core Service**: `CrisDataProcessor/Services/GdalCommandLineService.cs`
- **Elevation Service**: `CrisDataProcessor/Services/ElevationService.cs`
- **Configuration**: `CrisDataProcessor/appsettings.json`
- **Data Models**: `MapSandBox/Models/CrisModels.cs`
- **Road Network**: `MapSandBox/wwwroot/parker-county-roads.geojson`

## Success Criteria

1. **Accuracy**: Enhanced slopes match visual ground-truth assessment within ±2 degrees
2. **Reliability**: < 5% of locations flagged as high-variation outliers
3. **Performance**: < 10% increase in processing time over current implementation
4. **Coverage**: 95%+ successful slope calculations with quality metrics