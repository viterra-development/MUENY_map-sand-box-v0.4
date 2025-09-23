# CRIS Risk Segments Road Geometry Enhancement Plan

## Overview

Currently, the CRIS risk segments are visualized as straight-line connections between crash cluster points, which creates unrealistic geometric representations that don't follow actual road paths. This enhancement plan outlines how to improve the CrisDataProcessor to generate risk segments that follow actual road geometry.

## Current Implementation Issues

### Problem Description
The current `CrisGeoJsonGenerator.GenerateRiskSegmentsGeoJson()` method creates LineString geometries with only 2 points:

```csharp
Coordinates = new[]
{
    new[] { (double)segment.StartLongitude, (double)segment.StartLatitude },
    new[] { (double)segment.EndLongitude, (double)segment.EndLatitude }
}
```

This results in:
- **Straight-line segments** that don't follow road curves
- **Unrealistic visual representation** of actual crash risk zones
- **Misalignment** with actual road infrastructure where crashes occur

### Current vs. Desired Output
- **Current**: Straight lines between crash cluster endpoints
- **Desired**: Curved paths that follow actual road geometry from TIGER/Line data

## Available Resources

### Parker County Roads Data
We have detailed road geometry available in:
- **File**: `/workspaces/map-sand-box/MapSandBox/wwwroot/parker-county-roads.geojson`
- **Source**: TIGER/Line 2023 road data
- **Format**: GeoJSON FeatureCollection with detailed LineString geometries
- **Properties**: Each road feature includes:
  - `LINEARID`: Unique road segment identifier
  - `FULLNAME`: Road name
  - `RTTYP`: Road type
  - `MTFCC`: MAF/TIGER Feature Class Code

### Road Geometry Quality
Example road geometry shows high-quality curved paths:
- **Detailed coordinates**: Roads have 20-100+ coordinate points
- **Curved geometry**: Follows actual road alignment including curves and intersections
- **Complete coverage**: Covers all Parker County roads

## Enhancement Implementation Plan

### Phase 1: Spatial Matching Infrastructure

#### 1.1 Add Spatial Analysis Dependencies
```xml
<!-- Add to CrisDataProcessor.csproj -->
<PackageReference Include="NetTopologySuite" Version="2.5.0" />
<PackageReference Include="NetTopologySuite.IO.GeoJSON" Version="4.0.0" />
```

#### 1.2 Create Road Geometry Service
Create `Services/RoadGeometryService.cs`:
- Load Parker County roads GeoJSON
- Index roads by spatial location (R-tree or similar)
- Provide road segment lookup by crash coordinates
- Match crashes to nearest road segments within tolerance

#### 1.3 Enhance Data Models
Extend existing models to include road geometry:
```csharp
public class RiskSegment
{
    // Existing properties...
    public List<double[]> RoadGeometry { get; set; } = new(); // Full road coordinates
    public string RoadLinearId { get; set; } = ""; // TIGER LINEARID
    public string RoadName { get; set; } = ""; // Road name for display
}
```

### Phase 2: Crash-to-Road Matching

#### 2.1 Spatial Proximity Matching
Implement crash-to-road matching algorithm:
1. **Buffer crashes** by configurable distance (e.g., 50 meters)
2. **Find intersecting roads** using spatial index
3. **Select best match** based on:
   - Distance to road centerline
   - Road classification (prefer major roads for ambiguous cases)
   - Traffic volume (AADT) if available

#### 2.2 Road Segment Aggregation
Group crashes by road segment:
- Use TIGER `LINEARID` as primary grouping key
- Handle road intersections by choosing dominant road
- Create segments that span logical road sections

### Phase 3: Geometry Enhancement

#### 3.1 Update GeoJSON Generator
Modify `CrisGeoJsonGenerator.GenerateRiskSegmentsGeoJson()`:

```csharp
public CrisGeoJsonCollection GenerateRiskSegmentsGeoJson(List<RiskSegment> riskSegments)
{
    var features = riskSegments.Select(segment => new CrisGeoJsonFeature
    {
        Type = "Feature",
        Geometry = new CrisGeoJsonGeometry
        {
            Type = "LineString",
            Coordinates = segment.RoadGeometry.Any()
                ? segment.RoadGeometry  // Use actual road geometry
                : new[]  // Fallback to straight line
                {
                    new[] { (double)segment.StartLongitude, (double)segment.StartLatitude },
                    new[] { (double)segment.EndLongitude, (double)segment.EndLatitude }
                }
        },
        Properties = new Dictionary<string, object>
        {
            // Existing properties...
            ["road_linear_id"] = segment.RoadLinearId,
            ["road_name"] = segment.RoadName,
            ["geometry_type"] = segment.RoadGeometry.Any() ? "actual_road" : "straight_line"
        }
    }).ToList();

    // ... rest of method
}
```

#### 3.2 Configuration Options
Add processing options:
- **Spatial tolerance**: Distance for crash-to-road matching
- **Geometry simplification**: Option to simplify complex road geometries
- **Fallback behavior**: How to handle unmatched crashes

### Phase 4: Processing Pipeline Integration

#### 4.1 Update Main Processing Flow
Modify `Program.cs` processing sequence:
1. Load crash data (existing)
2. **NEW**: Load road geometry data
3. **NEW**: Match crashes to road segments
4. Calculate risk scores (existing, but now by road segment)
5. **NEW**: Enhance segments with road geometry
6. Generate output files (modified)

#### 4.2 Performance Considerations
- **Spatial indexing**: Use R-tree for fast spatial queries
- **Caching**: Cache road geometry lookups
- **Memory management**: Stream large datasets when possible
- **Progress reporting**: Show spatial matching progress

### Phase 5: Validation and Quality Assurance

#### 5.1 Matching Quality Metrics
Track and report:
- Percentage of crashes successfully matched to roads
- Average distance from crash to matched road
- Distribution of matches by road type
- Unmatched crashes for manual review

#### 5.2 Geometry Quality Validation
- Verify no gaps or discontinuities in road segments
- Check coordinate ordering (start-to-end consistency)
- Validate segment length calculations
- Compare straight-line vs. road-following distances

## Expected Benefits

### Improved Visual Accuracy
- **Realistic risk visualization**: Segments follow actual road paths
- **Better spatial understanding**: Users can see exactly which roads have high risk
- **Enhanced decision making**: More accurate representation for planning

### Enhanced Analysis Capabilities
- **Road-specific metrics**: Risk analysis tied to specific road segments
- **Better crash clustering**: Group crashes by actual road infrastructure
- **Improved reporting**: Road names and classifications in risk reports

### Future Integration Opportunities
- **Traffic data correlation**: Match risk segments with traffic count locations
- **Road condition integration**: Correlate with maintenance and infrastructure data
- **Dynamic routing**: Use risk data for route planning applications

## Implementation Timeline

### Phase 1 (Week 1): Infrastructure
- Set up spatial analysis dependencies
- Create RoadGeometryService
- Load and index road data

### Phase 2 (Week 2): Spatial Matching
- Implement crash-to-road matching algorithm
- Create road segment aggregation logic
- Add quality metrics tracking

### Phase 3 (Week 3): Geometry Integration
- Update GeoJSON generator with road geometry
- Add configuration options
- Implement fallback mechanisms

### Phase 4 (Week 4): Testing & Validation
- Validate matching quality
- Performance testing with full dataset
- Generate comparison reports (before/after)

## Technical Notes

### Spatial Analysis Libraries
- **NetTopologySuite**: Primary spatial operations library for .NET
- **GeoJSON.NET**: GeoJSON serialization/deserialization
- **Spatial indexing**: R-tree implementation for fast spatial queries

### Performance Optimization
- Use spatial indexing for O(log n) road lookups instead of O(n) linear search
- Consider geometry simplification for very detailed road segments
- Implement streaming processing for large crash datasets

### Data Quality Considerations
- Handle edge cases: crashes near intersections, bridges, overpasses
- Address coordinate precision issues between datasets
- Manage temporal mismatches (road data vintage vs. crash data dates)

## Future Enhancements

### Integration with Traffic Data
- Correlate risk segments with TCDS traffic count locations
- Use AADT data to weight risk calculations by traffic volume
- Create traffic-normalized risk metrics

### Dynamic Segmentation
- Instead of using fixed TIGER segments, create logical risk segments
- Combine multiple TIGER segments into meaningful risk analysis units
- Handle complex intersections and highway interchanges

### Multi-Modal Analysis
- Extend to pedestrian and bicycle crash analysis
- Different geometric matching rules for different crash types
- Integration with sidewalk and bike lane data

## Conclusion

This enhancement will significantly improve the visual accuracy and analytical value of CRIS risk segments by replacing straight-line approximations with actual road geometry. The implementation leverages existing TIGER/Line road data and modern spatial analysis techniques to create more realistic and useful risk visualizations.

The phased approach ensures incremental progress with validation at each step, maintaining system reliability while adding sophisticated spatial analysis capabilities.