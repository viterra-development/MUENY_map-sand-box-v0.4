# SSURGO Soil Data Expansion Plan for Parker County

## Current State Analysis

The SoilDataProcessor (`/workspaces/map-sand-box/SoilDataProcessor/`) currently generates **synthetic test data** with 5 sample soil map units covering a small area in Parker County, Texas. The system has the infrastructure to query the USDA SSURGO API but is currently configured to use sample data for testing.

### Current Implementation
- **Location**: `SoilDataProcessor/Program.cs:241-291`
- **Coverage**: 5 synthetic soil units in a ~0.01° square area (approximately 1 km²)
- **Test Area**: Around -97.795°, 32.755° (central Parker County)
- **Output**: 3 GeoJSON files (combined, clay visualization, ksat visualization)
- **Data Structure**: Includes mukey, musym, muname, soil_clay_pct, soil_ksat_um_per_s

### Existing Infrastructure
- **SSURGO API Integration**: Complete implementation in `QuerySSURGOApiAsync()` and `BuildVectorSoilDataQuery()`
- **Data Models**: Robust models in `SoilModels.cs` for API responses and GeoJSON output
- **Geometry Conversion**: NetTopologySuite integration for WKT to GeoJSON conversion
- **Parker County Boundary**: Available at `/workspaces/map-sand-box/CrisDataProcessor/Data/parker-county-boundary.geojson`

## Expansion Plan

### Phase 1: Enable Real SSURGO Data Retrieval

#### 1.1 Update Main Processing Logic
**File**: `SoilDataProcessor/Program.cs:10-62`

Replace the current synthetic data approach with real SSURGO API calls:

```csharp
static async Task Main(string[] args)
{
    // Load Parker County boundary geometry
    var parkerCountyGeometry = LoadParkerCountyBoundary();

    // Query real SSURGO data for the county
    var ssurgoResponse = await QuerySSURGOApiAsync(parkerCountyGeometry);
    var geoJsonFeatures = ConvertSSURGOToGeoJSON(ssurgoResponse);

    // Generate output files
    // ... existing file generation logic
}
```

#### 1.2 Implement County Boundary Loading
**New Method**: Add to `Program.cs`

```csharp
private static string LoadParkerCountyBoundary()
{
    var boundaryPath = Path.Combine("..", "CrisDataProcessor", "Data", "parker-county-boundary.geojson");
    var boundaryData = JsonSerializer.Deserialize<dynamic>(File.ReadAllText(boundaryPath));

    // Extract WKT geometry from the boundary GeoJSON
    // Convert to WKT format required by SSURGO API
    return ConvertGeoJsonToWkt(boundaryData);
}
```

#### 1.3 Add Geometry Conversion Utilities
**New Methods**: Add WKT/GeoJSON conversion utilities

- `ConvertGeoJsonToWkt()` - Convert Parker County boundary to WKT
- Handle large polygon geometries efficiently
- Implement geometry simplification if needed for API limits

### Phase 2: Handle Large Dataset Challenges

#### 2.1 Implement Chunked Processing
**Issue**: Parker County (~900 km²) may exceed SSURGO API limits

**Solution**: Divide county into grid cells and process individually:

```csharp
private static async Task<List<SoilGeoJsonFeature>> ProcessCountyInChunks(string countyBoundary)
{
    var chunks = CreateSpatialChunks(countyBoundary, maxChunkSize: 25); // ~25 km² per chunk
    var allFeatures = new List<SoilGeoJsonFeature>();

    foreach (var chunk in chunks)
    {
        var chunkResponse = await QuerySSURGOApiAsync(chunk);
        var chunkFeatures = ConvertSSURGOToGeoJSON(chunkResponse);
        allFeatures.AddRange(chunkFeatures);

        // Rate limiting: Wait between API calls
        await Task.Delay(1000);
    }

    return DeduplicateMapUnits(allFeatures);
}
```

#### 2.2 Add Error Handling and Retry Logic
**Enhancement**: Robust API interaction

```csharp
private static async Task<string> QuerySSURGOApiAsync(string areaGeometry, int maxRetries = 3)
{
    for (int attempt = 1; attempt <= maxRetries; attempt++)
    {
        try
        {
            // Existing API call logic
            return responseContent;
        }
        catch (HttpRequestException ex) when (attempt < maxRetries)
        {
            Console.WriteLine($"Attempt {attempt} failed: {ex.Message}. Retrying...");
            await Task.Delay(2000 * attempt); // Exponential backoff
        }
    }
    throw new Exception("Max retries exceeded");
}
```

#### 2.3 Implement Data Caching
**Enhancement**: Cache API responses to avoid re-downloading

```csharp
private static async Task<string> QueryWithCache(string geometry)
{
    var cacheKey = ComputeGeometryHash(geometry);
    var cacheFile = Path.Combine("cache", $"{cacheKey}.json");

    if (File.Exists(cacheFile))
    {
        Console.WriteLine("Using cached data...");
        return File.ReadAllText(cacheFile);
    }

    var response = await QuerySSURGOApiAsync(geometry);
    Directory.CreateDirectory("cache");
    await File.WriteAllTextAsync(cacheFile, response);

    return response;
}
```

### Phase 3: Data Quality and Optimization

#### 3.1 Enhanced Data Validation
**File**: `SoilDataProcessor/Program.cs:114-178`

Add validation to the `ConvertSSURGOToGeoJSON()` method:

```csharp
private static List<SoilGeoJsonFeature> ConvertSSURGOToGeoJSON(string ssurgoResponse)
{
    // Existing conversion logic...

    // Add validation:
    // - Check for reasonable clay percentage values (0-100%)
    // - Validate Ksat values are positive
    // - Ensure geometries are within Parker County bounds
    // - Remove duplicate or invalid map units

    return ValidateAndCleanFeatures(features);
}
```

#### 3.2 Optimize Output File Structure
**Enhancement**: Generate additional specialized outputs

1. **Simplified Geometries**: For web display performance
2. **Property-Specific Files**: Separate files for different soil properties
3. **Multi-Resolution**: Different detail levels for different zoom ranges

```csharp
// Generate multiple output variants
await GenerateMultiResolutionOutputs(geoJsonFeatures, outputDir);
```

#### 3.3 Add Statistics and Reporting
**Enhancement**: Generate processing summary

```csharp
private static void GenerateProcessingSummary(List<SoilGeoJsonFeature> features)
{
    var summary = new
    {
        TotalMapUnits = features.Count,
        ClayRange = new { Min = features.Min(f => f.Properties["soil_clay_pct"]),
                        Max = features.Max(f => f.Properties["soil_clay_pct"]) },
        KsatRange = new { Min = features.Min(f => f.Properties["soil_ksat_um_per_s"]),
                         Max = features.Max(f => f.Properties["soil_ksat_um_per_s"]) },
        ProcessingTime = DateTime.Now
    };

    File.WriteAllText("processing-summary.json", JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
}
```

### Phase 4: Integration and Configuration

#### 4.1 Add Configuration Management
**New File**: `SoilDataProcessor/appsettings.json`

```json
{
  "SsurgoApi": {
    "BaseUrl": "https://SDMDataAccess.sc.egov.usda.gov/Tabular/post.rest",
    "MaxRetries": 3,
    "RateLimitMs": 1000,
    "ChunkSizeKm2": 25
  },
  "Output": {
    "Directory": "../MapSandBox/wwwroot/soil-data",
    "GenerateMultiResolution": true,
    "SimplifyGeometries": true
  },
  "Cache": {
    "Enabled": true,
    "Directory": "./cache",
    "ExpirationDays": 30
  }
}
```

#### 4.2 Add Command Line Arguments
**Enhancement**: Make the processor more flexible

```csharp
static async Task Main(string[] args)
{
    var options = ParseCommandLineArgs(args);

    // Options: --county-boundary, --output-dir, --no-cache, --chunk-size, etc.

    if (options.ShowHelp)
    {
        ShowUsage();
        return;
    }

    // Process with options...
}
```

#### 4.3 Update Project Dependencies
**File**: `SoilDataProcessor/SoilDataProcessor.csproj`

Add configuration support:

```xml
<PackageReference Include="Microsoft.Extensions.Configuration" Version="8.0.0" />
<PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="8.0.0" />
```

## Implementation Priority

### High Priority (Immediate)
1. **Enable Real SSURGO Data** - Replace synthetic data with actual API calls
2. **Chunked Processing** - Handle large county area efficiently
3. **Basic Error Handling** - Ensure robust API interaction

### Medium Priority (Phase 2)
1. **Data Caching** - Avoid redundant API calls
2. **Enhanced Validation** - Ensure data quality
3. **Configuration Management** - Make system configurable

### Low Priority (Future Enhancement)
1. **Multi-Resolution Outputs** - Optimize for different zoom levels
2. **Advanced Statistics** - Detailed processing reports
3. **Command Line Interface** - Full CLI with options

## Expected Challenges

1. **API Rate Limits**: USDA SSURGO API may have usage restrictions
2. **Large Dataset Size**: Parker County soil data could be 100+ MB
3. **Geometry Complexity**: Some soil map units may have complex boundaries
4. **Data Consistency**: Real SSURGO data may have missing or inconsistent values

## Success Criteria

- ✅ Complete Parker County soil coverage (900+ km²)
- ✅ All major soil types and properties included
- ✅ Data quality validation passes
- ✅ Integration with existing MapSandBox visualization
- ✅ Processing time under 10 minutes
- ✅ Output files under 50 MB total

## Testing Strategy

1. **Unit Testing**: Test individual conversion functions
2. **Integration Testing**: Test full county processing pipeline
3. **Performance Testing**: Measure processing time and memory usage
4. **Data Validation**: Verify output against known soil survey data
5. **Visual Testing**: Confirm data displays correctly in MapSandBox

## Timeline Estimate

- **Phase 1** (Real Data): 2-3 days
- **Phase 2** (Chunking/Error Handling): 2-3 days
- **Phase 3** (Quality/Optimization): 2-3 days
- **Phase 4** (Configuration/Integration): 1-2 days

**Total**: 7-11 days for complete implementation