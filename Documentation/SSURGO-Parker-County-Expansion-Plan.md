# SSURGO Soil Data Expansion Plan for Parker County

## Current State Analysis

The SoilDataProcessor (`/workspaces/map-sand-box/SoilDataProcessor/`) is now **fully operational with real SSURGO API integration** and covers a small test area in Parker County, Texas. Both the USDA SSURGO API and geometry retrieval are working correctly with strongly-typed models.

### Current Implementation ✅ COMPLETED
- **Location**: `SoilDataProcessor/Program.cs` (fully implemented)
- **Coverage**: 6 real soil map units in small test area (~1 km²)
- **Test Area**: Around -97.795°, 32.755° (central Parker County)
- **Output**: 3 GeoJSON files (combined, clay visualization, ksat visualization)
- **Data Structure**: Strongly-typed `GeoJsonFeature<SoilProperties>` models
- **API Integration**: ✅ Real SSURGO vector soil data query working
- **Geometry Retrieval**: ✅ Real polygon boundaries from `SDA_Get_MupolygonWktWgs84_from_Mukey()`
- **Data Quality**: ✅ Weighted averaging by component percentage, duplicate handling

### Existing Infrastructure ✅ COMPLETED
- **SSURGO API Integration**: ✅ Complete and tested implementation in `QuerySSURGOApiAsync()` and `BuildVectorSoilDataQuery()`
- **Geometry API Integration**: ✅ Complete implementation in `GetSoilGeometriesAsync()` with `SDA_Get_MupolygonWktWgs84_from_Mukey()`
- **Data Models**: ✅ Strongly-typed models in `SoilModels.cs` integrated with shared `MapSandBox.Shared` models
- **Geometry Conversion**: ✅ NetTopologySuite integration for WKT to GeoJSON conversion working
- **Multi-polygon Handling**: ✅ Handles map units with multiple polygons (up to 302 per mukey)
- **Parker County Boundary**: Available at `/workspaces/map-sand-box/CrisDataProcessor/Data/parker-county-boundary.geojson`

## Expansion Plan

### Phase 0: Fix Critical Polygon Coverage Issue ⚠️ IMMEDIATE PRIORITY

#### 0.1 Problem: Missing 90%+ of Soil Coverage
**Current Issue**: The processor currently only uses the first polygon per MUKEY, missing the majority of soil areas.

**Impact**:
- Map unit 390893 (May fine sandy loam): Using 1 out of 302 polygons (99.7% missing)
- Map unit 390925: Using 1 out of 117 polygons (99.1% missing)
- Total coverage loss: ~90% of actual soil areas not represented

#### 0.2 Root Cause Analysis
**Why Multiple Polygons Exist**:
- Each MUKEY represents a soil type (e.g., "May fine sandy loam, 1 to 3 percent slopes")
- The same soil type occurs in multiple scattered locations across the landscape
- SSURGO correctly maps all locations where identical soil conditions exist
- This is standard soil survey methodology - not a data error

#### 0.3 Solution: Separate Feature per Polygon
**Implementation**: Replace current logic in `ConvertSSURGOToGeoJSONWithGeometry()` (lines 183-201)

**Current Logic** (BROKEN):
```csharp
// Multiple polygons - use first one for now
Console.WriteLine($"Note: Map unit {mukey} has {wktGeometries.Count} polygons, using first one");
feature.Geometry = ConvertWktToGeoJsonGeometry(wktGeometries[0]);
```

**New Logic** (COMPLETE COVERAGE):
```csharp
// Create separate features for each polygon
var allFeatures = new List<GeoJsonFeature<SoilProperties>>();
foreach (var propertiesFeature in propertiesFeatures)
{
    var mukey = propertiesFeature.Properties.MuKey;
    if (geometryGroups.TryGetValue(mukey, out var wktGeometries))
    {
        // Create a separate feature for each polygon with this soil type
        for (int i = 0; i < wktGeometries.Count; i++)
        {
            var newFeature = new GeoJsonFeature<SoilProperties>
            {
                Properties = CloneProperties(propertiesFeature.Properties),
                Geometry = ConvertWktToGeoJsonGeometry(wktGeometries[i])
            };
            allFeatures.Add(newFeature);
        }
    }
}
return allFeatures;
```

**Expected Results**:
- Test area: 6 soil types → ~450 individual polygon features (instead of 6)
- Complete soil coverage with no spatial gaps
- Accurate representation of soil distribution patterns

### Phase 1: County-Wide Data Retrieval ⚠️ NEXT STEP

#### 1.1 Load Parker County Boundary
**New Method**: Add to `Program.cs`

```csharp
private static string LoadParkerCountyBoundary()
{
    var boundaryPath = Path.Combine("..", "CrisDataProcessor", "Data", "parker-county-boundary.geojson");
    var boundaryGeoJson = File.ReadAllText(boundaryPath);
    var featureCollection = JsonSerializer.Deserialize<GeoJsonFeatureCollection>(boundaryGeoJson);

    // Convert the county boundary to WKT for SSURGO API
    var countyGeometry = featureCollection.Features.First().Geometry;
    return ConvertGeometryToWkt(countyGeometry);
}
```

#### 1.2 Update Main Processing Logic
**File**: `SoilDataProcessor/Program.cs` - Replace test area with full county

Replace the current small test area with Parker County boundary:

```csharp
static async Task Main(string[] args)
{
    Console.WriteLine("SSURGO Soil Data Processor - Parker County Full Import");
    Console.WriteLine("======================================================");

    // Load Parker County boundary geometry
    var parkerCountyWkt = LoadParkerCountyBoundary();

    // Query real SSURGO data for entire county
    var soilData = await QuerySSURGOApiAsync(parkerCountyWkt);
    // ... rest of existing processing logic
}
```

#### 1.3 Add Geometry Conversion Utilities
**New Methods**: Add WKT/GeoJSON conversion utilities

- `ConvertGeometryToWkt()` - Convert Parker County boundary from GeoJSON to WKT
- `LoadParkerCountyBoundary()` - Load and convert county boundary file
- Handle large polygon geometries efficiently

### Phase 2: Handle Large Dataset Challenges

#### 2.1 Implement Chunked Processing ⚠️ MANDATORY
**Issue**: Parker County (~900 km²) will exceed SSURGO API limits

**Confirmed API Limits**:
- 📏 **32MB JSON response limit** per query
- 📊 **100,000 records limit** per query
- 🗺️ **250,000 features limit** for spatial queries
- ✅ **No rate limiting** - can make requests as fast as needed

**Current vs. Full County**:
- Current test (6 mukeys): 1.6MB response ✅
- Full county estimate: 200-500MB response ❌ (exceeds 32MB limit by 6-15x)

**Status**: MANDATORY for full county - chunking required due to hard API limits

**Solution**: Divide county into grid cells that stay under API limits:

```csharp
private static async Task<List<GeoJsonFeature<SoilProperties>>> ProcessCountyInChunks(string countyBoundary)
{
    // Target: 20MB per chunk (well under 32MB limit)
    // Parker County: 900 km² ÷ 36 chunks = 25 km² per chunk
    var chunks = CreateSpatialChunks(countyBoundary, maxChunkSize: 25);
    var allFeatures = new List<GeoJsonFeature<SoilProperties>>();

    Console.WriteLine($"Processing {chunks.Count} chunks to stay under 32MB API limit...");

    foreach (var (chunk, index) in chunks.WithIndex())
    {
        Console.WriteLine($"Processing chunk {index + 1}/{chunks.Count}...");

        var chunkResponse = await QuerySSURGOApiAsync(chunk);

        // Check response size before processing
        var responseSizeMB = chunkResponse.Length / (1024.0 * 1024.0);
        Console.WriteLine($"   Chunk response: {responseSizeMB:F1}MB");

        if (responseSizeMB > 30)
        {
            Console.WriteLine($"   WARNING: Chunk approaching 32MB limit!");
        }

        var chunkFeatures = await ConvertSSURGOToGeoJSONWithGeometry(chunkResponse);
        allFeatures.AddRange(chunkFeatures);

        // Conservative delay to be respectful to USDA servers
        await Task.Delay(500); // Brief pause between chunks
    }

    return DeduplicateFeatures(allFeatures);
}
```

#### 2.2 Add Error Handling and Size Monitoring
**Enhancement**: Robust API interaction with size limit protection

```csharp
private static async Task<string> QuerySSURGOApiAsync(string areaGeometry, int maxRetries = 3)
{
    for (int attempt = 1; attempt <= maxRetries; attempt++)
    {
        try
        {
            var response = await _httpClient.PostAsync(SSURGO_API_BASE, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();

                // Check for size limit error
                if (errorContent.Contains("exceeds") || errorContent.Contains("limit"))
                {
                    throw new InvalidOperationException($"Query exceeds API size limits: {errorContent}");
                }

                throw new HttpRequestException($"API returned {response.StatusCode}: {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();

            // Monitor response size
            var sizeMB = responseContent.Length / (1024.0 * 1024.0);
            if (sizeMB > 30)
            {
                Console.WriteLine($"WARNING: Response {sizeMB:F1}MB approaching 32MB limit");
            }

            return responseContent;
        }
        catch (HttpRequestException ex) when (attempt < maxRetries)
        {
            Console.WriteLine($"Attempt {attempt} failed: {ex.Message}. Retrying...");
            await Task.Delay(1000 * attempt); // Network retry delay + respectful pause
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

### High Priority (Immediate) ⚠️ READY TO IMPLEMENT
1. ✅ **~~Enable Real SSURGO Data~~** - COMPLETED: Real API calls working with geometry retrieval
2. ⚠️ **Fix Polygon Coverage Issue** - CRITICAL: Create separate features for all polygons per mukey
3. ⚠️ **County Boundary Loading** - Load Parker County boundary and convert to WKT
4. ⚠️ **Chunked Processing** - Handle large county area efficiently (CRITICAL for success)
5. ⚠️ **Basic Error Handling** - Ensure robust API interaction for large datasets

### Medium Priority (Phase 2)
1. **Data Caching** - Avoid redundant API calls during chunked processing
2. **Enhanced Validation** - Ensure data quality across all chunks
3. **Configuration Management** - Make system configurable

### Low Priority (Future Enhancement)
1. **Multi-Resolution Outputs** - Optimize for different zoom levels
2. **Advanced Statistics** - Detailed processing reports
3. **Command Line Interface** - Full CLI with options

## Expected Challenges

1. ⚠️ **API Size Limits**: USDA SSURGO API has **32MB JSON response limit** and **100,000 records per query** (chunking MANDATORY)
2. **Large Dataset Size**: Parker County soil data could be 100+ MB (current test: 1.6MB for 6 units)
3. ⚠️ **Geometry Coverage Issue** - CRITICAL: Currently only using first polygon per mukey (missing 90%+ of soil areas)
4. ✅ **~~Data Consistency~~** - RESOLVED: Weighted averaging and validation implemented

## Success Criteria

- ⚠️ Complete Parker County soil coverage (900+ km²) - **READY TO IMPLEMENT**
- ✅ All major soil types and properties included (clay %, Ksat working)
- ✅ Data quality validation passes (weighted averaging implemented)
- ✅ Integration with existing MapSandBox visualization (strongly-typed models ready)
- ⚠️ Processing time under 10 minutes (needs chunking optimization)
- ⚠️ Output files manageable size (revised estimate: ~500MB with complete polygon coverage, may need optimization)

## Testing Strategy

1. **Unit Testing**: Test individual conversion functions
2. **Integration Testing**: Test full county processing pipeline
3. **Performance Testing**: Measure processing time and memory usage
4. **Data Validation**: Verify output against known soil survey data
5. **Visual Testing**: Confirm data displays correctly in MapSandBox

## Timeline Estimate

- ✅ **~~Phase 1~~** (Real Data): **COMPLETED** - Real API integration working with geometry
- ⚠️ **Phase 2** (County Boundary + Chunking): 1-2 days - **NEXT STEP**
- **Phase 3** (Error Handling/Caching): 1-2 days
- **Phase 4** (Optimization/Configuration): 1-2 days

**Remaining**: 3-6 days for full Parker County implementation

## Current Status Summary

✅ **COMPLETED:**
- Real SSURGO API integration with soil property data
- Real geometry retrieval from SSURGO
- Strongly-typed models with shared GeoJSON classes
- Weighted averaging by component percentage
- Working test implementation (6 map units, 1.6MB geometry data)

⚠️ **CRITICAL ISSUE IDENTIFIED:**
- Currently only using 1 polygon per soil type (missing 90%+ of coverage)
- Need to fix polygon handling BEFORE county expansion

⚠️ **CONFIRMED API LIMITS:**
- 32MB JSON response limit per query (HARD LIMIT)
- 100,000 records limit per query
- 250,000 features limit for spatial queries
- No rate limiting (but using 500ms delays to be respectful)

⚠️ **IMMEDIATE NEXT STEPS:**
1. **Fix polygon coverage** - Create separate features for all polygons per mukey
2. Load Parker County boundary geometry
3. **Implement chunked processing** - MANDATORY due to 32MB limit (not optional)
4. Add error handling with size monitoring
5. Test with full county data

**Ready to proceed with full Parker County expansion!**