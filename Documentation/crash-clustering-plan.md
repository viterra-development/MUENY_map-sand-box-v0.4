# Exact Location Crash Clustering Implementation Plan

## Overview
Replace the current HexagonLayer aggregation approach with exact-coordinate clustering combined with a ScatterplotLayer. Only crashes at identical coordinates will be clustered together, while offset crashes remain separate and visible.

## Current State
- **HexagonLayer**: Aggregates crashes into fixed grid hexagons
- **Issue**: Hexagons not centered on actual crash locations, groups crashes that should remain separate
- **Data**: Unique crashes in `/cris-data/parker-county-crashes-unique-deckgl.json`

## Proposed Solution

### 1. Exact Coordinate Clustering (C# - CrisDataProcessor)

**Location**: `CrisDataProcessor/Program.cs`

**Algorithm**: Group crashes by identical coordinates only
- Group crashes that share exact `[longitude, latitude]` coordinates
- Calculate cluster statistics (total crashes, max severity, persons involved)
- Preserve exact coordinates (no centroid calculation needed)

**Output Format**:
```json
// All crashes become clusters (cluster of 1 for single crashes)
{
  "clusterId": "cluster_001",
  "position": [longitude, latitude], // Exact coordinates
  "crashCount": 1, // or 3, 5, etc.
  "maxSeverity": "K", // Highest severity at this location
  "totalPersonsInvolved": 2, // Sum for location
  "crashes": [
    {
      "crashId": "20899324",
      "severityCode": "K",
      "crashDate": "2023-01-15",
      "personsInvolved": 2,
      // ... crash details
    }
    // ... additional crashes if clustered
  ]
}
```

**New Output File**: `parker-county-crashes-exact-clustered-deckgl.json`

### 2. ScatterplotLayer Implementation (JavaScript)

**Location**: `wwwroot/js/maplibre-deckgl-integration.js`

**Layer Type**: `deck.ScatterplotLayer`

**Properties**:
- **Position**: Exact crash/cluster coordinates
- **Radius**:
  - Single crashes: 4-6 pixels (base size)
  - Clustered crashes: `Math.sqrt(crashCount) * 4` pixels (proportional scaling)
- **Color**: Based on severity (single) or max severity (cluster)
- **Visual Distinction**: Clustered crashes could have stroke/border

**Simple Clustering Logic**:
```csharp
// Group by exact coordinate string - everything becomes a cluster
var clusteredData = crashes
    .GroupBy(c => $"{c.Longitude:F6},{c.Latitude:F6}") // 6 decimal precision
    .Select(locationGroup =>
    {
        var crashList = locationGroup.ToList();
        return new {
            ClusterId = $"cluster_{locationGroup.Key.Replace(",", "_")}",
            Position = new[] { crashList.First().Longitude, crashList.First().Latitude },
            CrashCount = crashList.Count, // 1 for single, N for multiple
            MaxSeverity = crashList.Max(c => GetSeverityWeight(c.SeverityCode)),
            TotalPersonsInvolved = crashList.Sum(c => c.PersonsInvolved),
            Crashes = crashList
        };
    });
```

### 3. Popup Integration

**Update**: `CrashClusterPopup.razor`

**Data Binding**:
- All data is cluster format (crashCount = 1 for singles)
- Display "1 crash at this location" or "X crashes at this location"
- List all crashes in the cluster (1 or more)

### 4. Implementation Steps

#### Phase 1: Exact Clustering Algorithm (C# Backend)
1. **Add exact coordinate clustering** to `CrisDataProcessor/Program.cs`
   - Group by exact longitude/latitude match
   - No distance calculations needed
   - Generate mixed single/cluster output

2. **Output clustered data** as new file
   - Keep existing files for compatibility
   - Add exact-clustered file

#### Phase 2: Frontend Layer Update (JavaScript)
1. **Update layer configuration** in `MapLibreService.cs`
   - Change from `HexagonLayer` to `ScatterplotLayer`
   - Update data URL to exact-clustered file

2. **Implement ScatterplotLayer** in `maplibre-deckgl-integration.js`
   - Handle uniform cluster data format
   - Size based on crash count (1, 2, 3, etc.)
   - Color based on max severity in cluster

#### Phase 3: UI Integration (Blazor)
1. **Update popup** to handle uniform cluster format
2. **Test exact coordinate matching**

## Technical Considerations

### Coordinate Precision
- **Precision Level**: Use 6 decimal places (~0.1 meter precision)
- **String Comparison**: Simple exact string match
- **No Distance Calculation**: Pure coordinate equality

### Performance
- **Preprocessing**: Very fast grouping operation
- **Runtime**: Optimal - no complex calculations
- **Data Reduction**: Only where crashes truly overlap

### Visual Design
- **All items**: Uniform cluster format with count-based sizing
- **Size scaling**: `Math.sqrt(crashCount) * baseRadius` for proportional growth
- **Color Consistency**: Same severity colors as popups
- **Visual scaling**: Natural size progression from 1 to N crashes

## Benefits
1. **Intuitive Behavior**: Only identical locations get clustered
2. **Preserved Separation**: Offset crashes remain distinct
3. **Simple Logic**: No complex distance/radius calculations
4. **Performance**: Minimal processing overhead
5. **Accurate Representation**: True to actual crash locations

## Edge Cases
- **GPS Precision**: Some crashes might be very close but not identical
- **Data Quality**: Handle potential coordinate rounding differences
- **Large Clusters**: Cap visual size for locations with many crashes

## Migration Path
1. Implement exact clustering in data processor
2. Generate new clustered data file
3. Switch to ScatterplotLayer
4. Update popup handling
5. Remove aggregation layer code