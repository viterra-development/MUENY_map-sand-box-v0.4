# SSURGO Vector-Based Integration Plan for MapSandBox

## Overview

This plan implements SSURGO soil data integration using the **industry-standard vector-based approach** with **Azure Blob Storage** as a temporary solution. SSURGO data is naturally vector-based (soil map unit polygons), and this plan preserves that structure while focusing on two key properties: `soil_clay_pct` and `soil_ksat_um_per_s`.

**Approach**: SSURGO API → GeoJSON Processing → Azure Blob Storage → Vector Visualization (Industry Standard)

## Phase 1: Data Storage Strategy - Azure Blob Storage

### 1.1 Storage Architecture

**Primary Storage**: Azure Blob Storage with static GeoJSON files

```
Azure Blob Storage Container: geospatial-data
├── soil-data/
│   ├── parker-county-test-combined.geojson    # All soil properties in one file
│   ├── parker-county-test-clay.geojson        # Clay percentage visualization
│   └── parker-county-test-ksat.geojson        # Ksat visualization  

File Structure for each GeoJSON:
{
  "type": "FeatureCollection",
  "features": [
    {
      "type": "Feature",
      "properties": {
        "mukey": "123456",
        "musym": "AbB",
        "muname": "Aledo clay loam, 1 to 5 percent slopes",
        "soil_clay_pct": 35.5,
        "soil_ksat_um_per_s": 1.2,
        "component_pct": 85
      },
      "geometry": {
        "type": "Polygon",
        "coordinates": [[...]]
      }
    }
  ]
}
```

### 1.2 Data Models for Blob Storage

```csharp
// Models/SoilModels.cs - Simplified for blob storage approach
namespace MapSandBox.Models;

// SSURGO API response models
public class SsurgoApiResponse
{
    public List<SsurgoRecord> Table { get; set; } = new();
}

public class SsurgoRecord
{
    [JsonPropertyName("mukey")]
    public string MuKey { get; set; } = "";
    
    [JsonPropertyName("musym")]
    public string MuSym { get; set; } = "";
    
    [JsonPropertyName("muname")]
    public string MuName { get; set; } = "";
    
    [JsonPropertyName("soil_clay_pct")]
    public decimal? SoilClayPct { get; set; }
    
    [JsonPropertyName("soil_ksat_um_per_s")]
    public decimal? SoilKsatUmPerS { get; set; }
    
    [JsonPropertyName("component_pct")]
    public int? ComponentPct { get; set; }
    
    [JsonPropertyName("geom")]
    public string Geom { get; set; } = ""; // WKT format from SSURGO
}

// GeoJSON output models
public class SoilGeoJsonFeature
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "Feature";
    
    [JsonPropertyName("properties")]
    public Dictionary<string, object> Properties { get; set; } = new();
    
    [JsonPropertyName("geometry")]
    public object Geometry { get; set; } = null!;
}

public class SoilGeoJsonCollection
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "FeatureCollection";
    
    [JsonPropertyName("features")]
    public List<SoilGeoJsonFeature> Features { get; set; } = new();
}

// Geometry conversion models
public class GeoJsonGeometry
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";
    
    [JsonPropertyName("coordinates")]
    public object Coordinates { get; set; } = null!;
}
```

### 1.3 Blob Storage Service

```csharp
// Services/BlobBasedSoilService.cs
namespace MapSandBox.Services;

public class BlobBasedSoilService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILogger<BlobBasedSoilService> _logger;
    private readonly IMemoryCache _cache;
    
    public BlobBasedSoilService(BlobServiceClient blobServiceClient, ILogger<BlobBasedSoilService> logger, IMemoryCache cache)
    {
        _blobServiceClient = blobServiceClient;
        _logger = logger;
        _cache = cache;
    }
    
    /// <summary>
    /// Get soil data GeoJSON from Azure Blob Storage with caching
    /// </summary>
    public async Task<string> GetSoilGeoJsonAsync(string fileName = "parker-county-test-combined.geojson")
    {
        var cacheKey = $"soil-geojson-{fileName}";
        
        if (_cache.TryGetValue(cacheKey, out string cachedData))
        {
            return cachedData;
        }
        
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient("geospatial-data");
            var blobClient = containerClient.GetBlobClient($"soil-data/{fileName}");
            
            var response = await blobClient.DownloadContentAsync();
            var geoJsonContent = response.Value.Content.ToString();
            
            // Cache for 15 minutes
            _cache.Set(cacheKey, geoJsonContent, TimeSpan.FromMinutes(15));
            
            return geoJsonContent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to load soil data from blob: {fileName}");
            return @"{""type"":""FeatureCollection"",""features"":[]}"; // Empty GeoJSON
        }
    }
    
    /// <summary>
    /// Store processed SSURGO data as GeoJSON in blob storage
    /// </summary>
    public async Task<bool> StoreSoilGeoJsonAsync(string geoJsonContent, string fileName)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient("geospatial-data");
            await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob);
            
            var blobClient = containerClient.GetBlobClient($"soil-data/{fileName}");
            
            await blobClient.UploadAsync(
                BinaryData.FromString(geoJsonContent),
                overwrite: true);
                
            // Set content type and cache headers
            await blobClient.SetHttpHeadersAsync(new BlobHttpHeaders
            {
                ContentType = "application/geo+json",
                CacheControl = "public, max-age=3600" // 1 hour cache
            });
            
            _logger.LogInformation($"Stored soil GeoJSON: {fileName}");
            
            // Clear cache for this file
            var cacheKey = $"soil-geojson-{fileName}";
            _cache.Remove(cacheKey);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to store soil GeoJSON: {fileName}");
            return false;
        }
    }
    
    /// <summary>
    /// Get the public URL for a soil GeoJSON file
    /// </summary>
    public string GetSoilDataUrl(string fileName)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient("geospatial-data");
        var blobClient = containerClient.GetBlobClient($"soil-data/{fileName}");
        
        return blobClient.Uri.ToString();
    }
    
    /// <summary>
    /// Check if soil data files exist in blob storage
    /// </summary>
    public async Task<bool> SoilDataExistsAsync()
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient("geospatial-data");
            var blobClient = containerClient.GetBlobClient("soil-data/parker-county-test-combined.geojson");
            
            var response = await blobClient.ExistsAsync();
            return response.Value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check if soil data exists");
            return false;
        }
    }
}
```

## Phase 2: SSURGO API Integration & Processing

### 2.1 SSURGO Processing Service

```csharp
// Services/SSURGOProcessingService.cs
using NetTopologySuite.IO;
using System.Text.Json;

namespace MapSandBox.Services;

public class SSURGOProcessingService
{
    private readonly HttpClient _httpClient;
    private readonly BlobBasedSoilService _blobService;
    private readonly ILogger<SSURGOProcessingService> _logger;
    
    private const string SSURGO_API_BASE = "https://SDMDataAccess.sc.egov.usda.gov/Tabular/post.rest";
    
    public SSURGOProcessingService(HttpClient httpClient, BlobBasedSoilService blobService, ILogger<SSURGOProcessingService> logger)
    {
        _httpClient = httpClient;
        _blobService = blobService;
        _logger = logger;
    }
    
    /// <summary>
    /// Download SSURGO data and convert directly to GeoJSON files
    /// </summary>
    public async Task<bool> ProcessAndStoreTestSoilDataAsync()
    {
        try
        {
            // 1. Download SSURGO data for test area
            var testAreaBounds = "POLYGON((-97.80 32.75, -97.79 32.75, -97.79 32.76, -97.80 32.76, -97.80 32.75))";
            var ssurgoResponse = await QuerySSURGOApiAsync(testAreaBounds);
            
            if (string.IsNullOrEmpty(ssurgoResponse))
            {
                _logger.LogWarning("No SSURGO data returned for test area");
                return false;
            }
            
            // 2. Convert to GeoJSON format
            var geoJsonFeatures = ConvertSSURGOToGeoJSON(ssurgoResponse);
            
            if (!geoJsonFeatures.Any())
            {
                _logger.LogWarning("No valid soil features found in SSURGO response");
                return false;
            }
            
            // 3. Create different GeoJSON files for different visualizations
            var combinedGeoJson = CreateGeoJsonCollection(geoJsonFeatures);
            var clayVisualizationGeoJson = CreateClayVisualizationGeoJson(geoJsonFeatures);
            var ksatVisualizationGeoJson = CreateKsatVisualizationGeoJson(geoJsonFeatures);
            
            // 4. Store in Azure Blob Storage
            var tasks = new[]
            {
                _blobService.StoreSoilGeoJsonAsync(combinedGeoJson, "parker-county-test-combined.geojson"),
                _blobService.StoreSoilGeoJsonAsync(clayVisualizationGeoJson, "parker-county-test-clay.geojson"),
                _blobService.StoreSoilGeoJsonAsync(ksatVisualizationGeoJson, "parker-county-test-ksat.geojson")
            };
            
            var results = await Task.WhenAll(tasks);
            
            if (results.All(r => r))
            {
                _logger.LogInformation($"Successfully processed and stored {geoJsonFeatures.Count} soil map units");
                return true;
            }
            else
            {
                _logger.LogError("Failed to store one or more GeoJSON files");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process and store test soil data");
            return false;
        }
    }
    
    private async Task<string> QuerySSURGOApiAsync(string areaGeometry)
    {
        var query = BuildVectorSoilDataQuery(areaGeometry);
        
        var requestBody = new { query = query, format = "json" };
        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        
        _logger.LogInformation($"Querying SSURGO API for area: {areaGeometry}");
        
        var response = await _httpClient.PostAsync(SSURGO_API_BASE, content);
        response.EnsureSuccessStatusCode();
        
        var responseContent = await response.Content.ReadAsStringAsync();
        _logger.LogInformation($"SSURGO API response received: {responseContent.Length} characters");
        
        return responseContent;
    }
    
    private string BuildVectorSoilDataQuery(string areaGeometry)
    {
        return $@"
            SELECT 
                mu.mukey,
                mu.musym,
                mu.muname,
                co.cokey,
                co.compname,
                co.comppct_r as component_pct,
                ch.claytotal_r as soil_clay_pct,
                ch.ksat_r as soil_ksat_um_per_s,
                mu.mupolygonwkt as geom
            FROM mapunit mu
            INNER JOIN component co ON mu.mukey = co.mukey
            INNER JOIN chorizon ch ON co.cokey = ch.cokey
            WHERE mu.mukey IN (
                SELECT mukey FROM SDA_Get_Mukey_from_intersection_with_WktWgs84('{areaGeometry}')
            )
            AND co.comppct_r >= 25
            AND ch.hzdept_r = 0
            ORDER BY mu.mukey, co.comppct_r DESC";
    }
    
    private List<SoilGeoJsonFeature> ConvertSSURGOToGeoJSON(string ssurgoResponse)
    {
        var ssurgoData = JsonSerializer.Deserialize<SsurgoApiResponse>(ssurgoResponse);
        var features = new List<SoilGeoJsonFeature>();
        
        // Group by map unit key and aggregate component data
        var groupedData = ssurgoData.Table
            .GroupBy(r => r.MuKey)
            .Select(g => new
            {
                MuKey = g.Key,
                MuSym = g.First().MuSym,
                MuName = g.First().MuName,
                Geom = g.First().Geom,
                // Weight-averaged soil properties by component percentage
                ClayPct = g.Where(r => r.SoilClayPct.HasValue && r.ComponentPct.HasValue)
                          .Sum(r => r.SoilClayPct.Value * r.ComponentPct.Value) /
                          g.Where(r => r.SoilClayPct.HasValue && r.ComponentPct.HasValue)
                          .Sum(r => r.ComponentPct.Value),
                Ksat = g.Where(r => r.SoilKsatUmPerS.HasValue && r.ComponentPct.HasValue)
                      .Sum(r => r.SoilKsatUmPerS.Value * r.ComponentPct.Value) /
                      g.Where(r => r.SoilKsatUmPerS.HasValue && r.ComponentPct.HasValue)
                      .Sum(r => r.ComponentPct.Value)
            })
            .Where(g => !string.IsNullOrEmpty(g.Geom))
            .ToList();
        
        foreach (var mapUnit in groupedData)
        {
            try
            {
                // Convert WKT geometry to GeoJSON geometry
                var geoJsonGeometry = ConvertWktToGeoJsonGeometry(mapUnit.Geom);
                
                features.Add(new SoilGeoJsonFeature
                {
                    Type = "Feature",
                    Properties = new Dictionary<string, object>
                    {
                        ["mukey"] = mapUnit.MuKey,
                        ["musym"] = mapUnit.MuSym,
                        ["muname"] = mapUnit.MuName ?? "",
                        ["soil_clay_pct"] = Math.Round(mapUnit.ClayPct, 1),
                        ["soil_ksat_um_per_s"] = Math.Round(mapUnit.Ksat, 3)
                    },
                    Geometry = geoJsonGeometry
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Failed to convert map unit {mapUnit.MuKey} to GeoJSON");
            }
        }
        
        return features;
    }
    
    private object ConvertWktToGeoJsonGeometry(string wkt)
    {
        try
        {
            // Use NetTopologySuite for robust WKT to GeoJSON conversion
            var wktReader = new NetTopologySuite.IO.WKTReader();
            var geometry = wktReader.Read(wkt);
            
            // Convert to GeoJSON format
            var geoJsonWriter = new NetTopologySuite.IO.GeoJsonWriter();
            var geoJsonString = geoJsonWriter.Write(geometry);
            
            // Parse the geometry portion of the GeoJSON
            var geoJsonObject = JsonSerializer.Deserialize<JsonElement>(geoJsonString);
            return geoJsonObject.GetProperty("geometry").Deserialize<object>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to convert WKT to GeoJSON: {wkt.Substring(0, Math.Min(100, wkt.Length))}...");
            throw new ArgumentException($"Invalid WKT geometry: {wkt}", ex);
        }
    }
    
    private string CreateGeoJsonCollection(List<SoilGeoJsonFeature> features)
    {
        var collection = new SoilGeoJsonCollection { Features = features };
        return JsonSerializer.Serialize(collection, new JsonSerializerOptions { WriteIndented = false });
    }
    
    private string CreateClayVisualizationGeoJson(List<SoilGeoJsonFeature> features)
    {
        // Create version optimized for clay percentage visualization
        var clayFeatures = features.Select(f => new SoilGeoJsonFeature
        {
            Type = f.Type,
            Geometry = f.Geometry,
            Properties = new Dictionary<string, object>
            {
                ["mukey"] = f.Properties["mukey"],
                ["musym"] = f.Properties["musym"],
                ["muname"] = f.Properties["muname"],
                ["soil_clay_pct"] = f.Properties["soil_clay_pct"],
                ["visualization"] = "clay"
            }
        }).ToList();
        
        return CreateGeoJsonCollection(clayFeatures);
    }
    
    private string CreateKsatVisualizationGeoJson(List<SoilGeoJsonFeature> features)
    {
        // Create version optimized for permeability visualization
        var ksatFeatures = features.Select(f => new SoilGeoJsonFeature
        {
            Type = f.Type,
            Geometry = f.Geometry,
            Properties = new Dictionary<string, object>
            {
                ["mukey"] = f.Properties["mukey"],
                ["musym"] = f.Properties["musym"],
                ["muname"] = f.Properties["muname"],
                ["soil_ksat_um_per_s"] = f.Properties["soil_ksat_um_per_s"],
                ["visualization"] = "ksat"
            }
        }).ToList();
        
        return CreateGeoJsonCollection(ksatFeatures);
    }
}
```

## Phase 3: Industry-Standard Vector Visualization

### 3.1 API Controllers

```csharp
// Controllers/SoilController.cs
[ApiController]
[Route("api/[controller]")]
public class SoilController : ControllerBase
{
    private readonly BlobBasedSoilService _soilService;
    private readonly SSURGOProcessingService _processingService;
    private readonly ILogger<SoilController> _logger;
    
    public SoilController(BlobBasedSoilService soilService, SSURGOProcessingService processingService, ILogger<SoilController> logger)
    {
        _soilService = soilService;
        _processingService = processingService;
        _logger = logger;
    }
    
    /// <summary>
    /// Get soil data GeoJSON from blob storage
    /// </summary>
    [HttpGet("geojson/{fileName}")]
    public async Task<IActionResult> GetSoilGeoJson(string fileName)
    {
        try
        {
            var geoJson = await _soilService.GetSoilGeoJsonAsync(fileName);
            return Content(geoJson, "application/geo+json");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to retrieve soil GeoJSON: {fileName}");
            return NotFound($"Soil data file not found: {fileName}");
        }
    }
    
    /// <summary>
    /// Get available soil data files
    /// </summary>
    [HttpGet("files")]
    public async Task<ActionResult<object>> GetAvailableFiles()
    {
        var exists = await _soilService.SoilDataExistsAsync();
        
        return Ok(new
        {
            dataExists = exists,
            availableFiles = new[]
            {
                new { name = "parker-county-test-combined.geojson", description = "All soil properties", url = _soilService.GetSoilDataUrl("parker-county-test-combined.geojson") },
                new { name = "parker-county-test-clay.geojson", description = "Clay percentage visualization", url = _soilService.GetSoilDataUrl("parker-county-test-clay.geojson") },
                new { name = "parker-county-test-ksat.geojson", description = "Permeability visualization", url = _soilService.GetSoilDataUrl("parker-county-test-ksat.geojson") }
            }
        });
    }
    
    /// <summary>
    /// Process and store test soil data
    /// </summary>
    [HttpPost("process-test-data")]
    public async Task<IActionResult> ProcessTestData()
    {
        try
        {
            _logger.LogInformation("Starting SSURGO test data processing...");
            
            var success = await _processingService.ProcessAndStoreTestSoilDataAsync();
            
            if (success)
            {
                return Ok(new { message = "Test soil data processed and stored successfully" });
            }
            else
            {
                return BadRequest(new { error = "Failed to process test soil data" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing test soil data");
            return StatusCode(500, new { error = "Internal server error processing soil data" });
        }
    }
    
    /// <summary>
    /// Health check for soil data service
    /// </summary>
    [HttpGet("health")]
    public async Task<ActionResult<object>> HealthCheck()
    {
        var dataExists = await _soilService.SoilDataExistsAsync();
        
        return Ok(new
        {
            status = "healthy",
            soilDataAvailable = dataExists,
            timestamp = DateTime.UtcNow
        });
    }
}
```

### 3.2 MapLibre Vector Layer Configuration

```csharp
// Update MapLibreService.cs
public List<LayerConfig> GetDefaultLayers()
{
    var azureCdnBaseUrl = _azureTileConfig.UseCdn ? _azureTileConfig.CdnUrl : _azureTileConfig.BaseUrl;
    
    return new List<LayerConfig>
    {
        // ... existing layers ...
        
        // SSURGO Soil Map Units - Vector-based from Azure Blob Storage (Industry Standard)
        new LayerConfig
        {
            Id = "soil-clay-visualization",
            Type = "GeoJson",
            DataUrl = $"{azureCdnBaseUrl}/geospatial-data/soil-data/parker-county-test-clay.geojson",
            Visible = true,
            Properties = new Dictionary<string, object>
            {
                ["filled"] = true,
                ["stroked"] = true,
                ["getFillColor"] = "getSoilClayColor", // JavaScript function for clay % coloring
                ["getLineColor"] = new int[] { 139, 69, 19, 255 }, // Brown soil boundary
                ["getLineWidth"] = 2,
                ["opacity"] = 0.8,
                ["pickable"] = true,
                ["autoHighlight"] = true,
                ["onClick"] = "handleSoilUnitClick"
            }
        },
        
        // Soil permeability visualization layer
        new LayerConfig
        {
            Id = "soil-ksat-visualization", 
            Type = "GeoJson",
            DataUrl = $"{azureCdnBaseUrl}/geospatial-data/soil-data/parker-county-test-ksat.geojson",
            Visible = false,
            Properties = new Dictionary<string, object>
            {
                ["filled"] = true,
                ["stroked"] = true,
                ["getFillColor"] = "getSoilKsatColor", // JavaScript function for Ksat coloring
                ["getLineColor"] = new int[] { 139, 69, 19, 255 },
                ["getLineWidth"] = 1,
                ["opacity"] = 0.7,
                ["pickable"] = true,
                ["autoHighlight"] = true,
                ["onClick"] = "handleSoilUnitClick"
            }
        }
    };
}

public List<LayerInfo> GetLayerInfo()
{
    return new List<LayerInfo>
    {
        // ... existing layers ...
        new LayerInfo { Id = "soil-clay-visualization", Name = "Soil Clay Content (%)", Visible = true },
        new LayerInfo { Id = "soil-ksat-visualization", Name = "Soil Permeability (Ksat)", Visible = false }
    };
}
```

### 3.3 JavaScript Visualization Functions

```javascript
// Add to maplibre-deckgl-integration.js

// Color scheme for clay percentage (industry standard brown tones)
function getSoilClayColor(feature) {
    const clayPct = feature.properties.soil_clay_pct || 0;
    
    // USDA standard clay content color ramp
    if (clayPct < 10) return [245, 222, 179, 200];      // Light wheat (sandy)
    if (clayPct < 20) return [222, 184, 135, 200];      // Burlywood (sandy loam)
    if (clayPct < 35) return [205, 133, 63, 200];       // Peru (loam)
    if (clayPct < 50) return [160, 82, 45, 200];        // Saddle brown (clay loam)
    return [101, 67, 33, 200];                          // Dark brown (clay)
}

// Color scheme for saturated hydraulic conductivity
function getSoilKsatColor(feature) {
    const ksat = feature.properties.soil_ksat_um_per_s || 0;
    
    // Permeability color ramp (blue = high permeability, red = low)
    if (ksat > 10) return [0, 100, 255, 200];           // High permeability (blue)
    if (ksat > 5) return [0, 150, 200, 200];            // Moderate-high
    if (ksat > 1) return [100, 200, 100, 200];          // Moderate (green)
    if (ksat > 0.1) return [255, 150, 0, 200];          // Low-moderate (orange)
    return [255, 0, 0, 200];                            // Low permeability (red)
}

// Handle soil unit clicks (industry standard popup)
function handleSoilUnitClick(info) {
    if (info.object) {
        const props = info.object.properties;
        
        const popupContent = `
            <div class="soil-popup">
                <h3>Soil Map Unit: ${props.musym}</h3>
                <h4>${props.muname || 'Unknown soil type'}</h4>
                <hr>
                <div class="soil-properties">
                    <div class="property">
                        <strong>Clay Content:</strong> ${props.soil_clay_pct?.toFixed(1) || 'N/A'}%
                    </div>
                    <div class="property">
                        <strong>Permeability:</strong> ${props.soil_ksat_um_per_s?.toFixed(3) || 'N/A'} μm/s
                    </div>
                    <div class="property">
                        <strong>Map Unit Key:</strong> ${props.mukey}
                    </div>
                </div>
            </div>
        `;
        
        // Show popup at clicked location
        showSoilPopup(info.coordinate, popupContent);
    }
}

function showSoilPopup(coordinate, content) {
    // Implementation depends on your popup system
    // Similar to existing traffic popup functionality
    console.log('Soil unit clicked:', content);
}
```

## Phase 4: Data Loading & Deployment Strategy

### 4.1 NuGet Package Installation

```bash
# Install required NuGet packages for spatial operations
dotnet add package NetTopologySuite --version 2.5.0
dotnet add package NetTopologySuite.IO.GeoJSON --version 4.0.0
dotnet add package Azure.Storage.Blobs --version 12.19.1
```

### 4.2 Service Registration

```csharp
// Program.cs - Add soil services to dependency injection
builder.Services.AddScoped<BlobBasedSoilService>();
builder.Services.AddScoped<SSURGOProcessingService>();

// Configure Azure Blob Storage client
builder.Services.AddSingleton(x =>
{
    var connectionString = builder.Configuration.GetConnectionString("AzureStorage");
    return new BlobServiceClient(connectionString);
});

// Add memory cache for blob storage caching
builder.Services.AddMemoryCache();
```

### 4.3 Initial Data Load

```bash
#!/bin/bash
# load-test-soil-data.sh

echo "Loading test SSURGO data..."

# 1. Ensure Azure storage container exists
echo "Checking Azure storage setup..."

# 2. Download and process test soil data
echo "Processing SSURGO test data..."
curl -X POST "https://localhost:7067/api/soil/process-test-data" \
     -H "accept: */*"

# 3. Verify data was loaded
echo "Verifying soil data files..."
curl -X GET "https://localhost:7067/api/soil/files" \
     -H "accept: application/json"

# 4. Test GeoJSON endpoints
echo "Testing GeoJSON endpoints..."
curl -X GET "https://localhost:7067/api/soil/geojson/parker-county-test-clay.geojson" \
     -H "accept: application/geo+json"

echo "Test soil data loading complete"
```

### 4.4 Azure Storage Setup

```bash
#!/bin/bash
# setup-azure-storage.sh

# Create storage container for geospatial data
az storage container create \
    --name geospatial-data \
    --public-access blob \
    --connection-string $AZURE_STORAGE_CONNECTION_STRING

# Set CORS policy for web access
az storage cors add \
    --services b \
    --methods GET POST OPTIONS \
    --origins "*" \
    --allowed-headers "*" \
    --connection-string $AZURE_STORAGE_CONNECTION_STRING

echo "Azure storage setup complete"
```

## NetTopologySuite Benefits

### Handles All Geometry Types
```csharp
// NetTopologySuite automatically handles:

// Simple Polygon
POLYGON((-97.8001 32.7501, -97.8000 32.7501, -97.8000 32.7502, -97.8001 32.7502, -97.8001 32.7501))

// Polygon with Holes
POLYGON((-97.8001 32.7501, -97.8000 32.7501, -97.8000 32.7502, -97.8001 32.7502, -97.8001 32.7501), 
        (-97.8005 32.7505, -97.8004 32.7505, -97.8004 32.7506, -97.8005 32.7506, -97.8005 32.7505))

// MultiPolygon (multiple separate areas)
MULTIPOLYGON(((-97.8001 32.7501, -97.8000 32.7501, -97.8000 32.7502, -97.8001 32.7502, -97.8001 32.7501)),
             ((-97.7995 32.7505, -97.7994 32.7505, -97.7994 32.7506, -97.7995 32.7506, -97.7995 32.7505)))

// Points (if SSURGO ever returns point data)
POINT(-97.8001 32.7501)
```

### Error Handling & Validation
```csharp
private object ConvertWktToGeoJsonGeometry(string wkt)
{
    try
    {
        var wktReader = new WKTReader();
        var geometry = wktReader.Read(wkt);
        
        // NetTopologySuite validates geometry automatically
        if (!geometry.IsValid)
        {
            _logger.LogWarning($"Invalid geometry detected for WKT: {wkt.Substring(0, 50)}...");
            // Optionally fix invalid geometries
            geometry = NetTopologySuite.Operation.Valid.GeometryFixer.Fix(geometry);
        }
        
        var geoJsonWriter = new GeoJsonWriter();
        var geoJsonString = geoJsonWriter.Write(geometry);
        
        var geoJsonObject = JsonSerializer.Deserialize<JsonElement>(geoJsonString);
        return geoJsonObject.GetProperty("geometry").Deserialize<object>();
    }
    catch (NetTopologySuite.IO.ParseException ex)
    {
        _logger.LogError(ex, $"Invalid WKT format: {wkt}");
        throw new ArgumentException($"Invalid WKT geometry format", ex);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, $"Failed to convert WKT to GeoJSON: {wkt.Substring(0, Math.Min(100, wkt.Length))}...");
        throw;
    }
}
```

## Implementation Benefits - Azure Blob Storage Approach

### 1. **Industry Standard Compliance**
- ✅ Preserves SSURGO's native vector format
- ✅ Maintains soil map unit boundaries exactly as surveyed
- ✅ Compatible with USDA Web Soil Survey visualization patterns
- ✅ Standard GeoJSON format for interoperability

### 2. **Azure-Native Architecture**
- ✅ Uses existing Azure infrastructure (blob storage, CDN)
- ✅ Leverages Azure CDN for global performance
- ✅ Simple setup with no database management overhead
- ✅ Cost-effective for prototype and testing

### 3. **Performance & Simplicity**  
- ✅ Fast CDN-cached GeoJSON delivery
- ✅ Efficient vector rendering in MapLibre
- ✅ In-memory caching for frequently accessed data
- ✅ Simple file-based architecture

### 4. **Development Efficiency**
- ✅ Quick to implement and test
- ✅ Easy debugging with direct file access
- ✅ Minimal infrastructure dependencies
- ✅ Clear migration path to database when needed

### 5. **User Experience**
- ✅ Click soil polygons for detailed information
- ✅ Standard soil science color schemes
- ✅ Familiar soil survey interface patterns
- ✅ Integration with existing layer controls

## Expected Results

After implementation:
- **Vector-based soil map units** displaying clay content with standard color schemes
- **Interactive soil polygons** showing detailed soil properties on click
- **Azure blob storage-based delivery** with CDN caching for performance
- **Industry-standard visualization** matching USDA Web Soil Survey patterns
- **Two soil property layers**: clay percentage and permeability visualization modes
- **~1 square mile test area** with complete vector soil map unit coverage
- **File sizes**: ~10-100 KB total for test area (very manageable)
- **Load times**: <1 second with Azure CDN

## Migration Strategy

This blob storage approach provides a **perfect stepping stone**:

### Phase 1: Blob Storage (Current Plan)
- ✅ Quick implementation for testing and validation
- ✅ Proves the complete pipeline works end-to-end
- ✅ Industry-standard vector visualization
- ✅ Cost-effective prototype

### Phase 2: Database Migration (Future)
- Migrate from static GeoJSON files to Azure SQL Database
- Add dynamic spatial queries and filtering
- Scale to full Parker County coverage
- **Same visualization code** - only data source changes

## Cost Estimates

### Blob Storage Approach:
- **Storage**: ~$0.10/month for test files
- **CDN**: ~$1-5/month depending on usage
- **Total**: ~$5-10/month for development/testing

### Alternative Database Approach:
- **Azure SQL**: ~$50-200/month for development tier
- **Total**: Significantly higher for testing phase

The blob storage approach allows you to validate the complete SSURGO integration pipeline at a fraction of the cost, with an easy migration path when you're ready to scale.

## Implementation Steps

### Step 1: Setup Dependencies
```bash
# Install required NuGet packages
cd MapSandBox
dotnet add package NetTopologySuite --version 2.5.0
dotnet add package NetTopologySuite.IO.GeoJSON --version 4.0.0
dotnet add package Azure.Storage.Blobs --version 12.19.1
```

### Step 2: Create Data Models
Create `Models/SoilModels.cs` with the models defined in Phase 1.2.

### Step 3: Implement Blob Service
Create `Services/BlobBasedSoilService.cs` with the implementation from Phase 1.3.

### Step 4: Implement Processing Service
Create `Services/SSURGOProcessingService.cs` with NetTopologySuite-powered WKT conversion from Phase 2.1.

### Step 5: Create API Controller
Create `Controllers/SoilController.cs` with endpoints from Phase 3.1.

### Step 6: Update MapLibre Configuration
Update `Services/MapLibreService.cs` to add soil layers from Phase 3.2.

### Step 7: Add JavaScript Visualization
Update `wwwroot/js/maplibre-deckgl-integration.js` with soil color functions from Phase 3.3.

### Step 8: Configure Services
Update `Program.cs` with service registration from Phase 4.2.

### Step 9: Setup Azure Storage
Run Azure setup script from Phase 4.4.

### Step 10: Test End-to-End
Run data loading script from Phase 4.3 and verify soil layers display in MapLibre.

## Implementation Timeline

### Week 1: Core Infrastructure
- [ ] Install NuGet packages and dependencies
- [ ] Create soil data models and blob service
- [ ] Implement SSURGO processing service with NetTopologySuite
- [ ] Create basic API controller

**Deliverable**: SSURGO API integration working, can convert WKT to GeoJSON

### Week 2: Data Processing & Storage
- [ ] Test SSURGO API queries for Parker County test area
- [ ] Implement complete data processing pipeline
- [ ] Store test GeoJSON files in Azure Blob Storage
- [ ] Verify data integrity and file accessibility

**Deliverable**: Soil data stored as GeoJSON files in Azure, accessible via CDN

### Week 3: Frontend Integration
- [ ] Update MapLibreService with soil layer configurations
- [ ] Implement JavaScript color schemes and interactivity
- [ ] Test layer display and performance
- [ ] Implement popup functionality for soil properties

**Deliverable**: Interactive soil layers working in MapLibre interface

### Week 4: Testing & Validation
- [ ] End-to-end pipeline testing
- [ ] Performance optimization and caching
- [ ] Error handling and edge case testing
- [ ] Documentation and deployment verification

**Deliverable**: Complete working soil data integration ready for use

## Success Criteria

### ✅ Technical Success
- [ ] SSURGO API successfully queried for test area
- [ ] WKT geometries converted to GeoJSON using NetTopologySuite
- [ ] 3 GeoJSON files stored in Azure Blob Storage (combined, clay, ksat)
- [ ] Soil layers display correctly in MapLibre interface
- [ ] Interactive soil polygons with property popups working
- [ ] Industry-standard soil color schemes implemented

### ✅ Performance Success
- [ ] GeoJSON files load in <1 second via Azure CDN
- [ ] Memory usage acceptable with caching enabled
- [ ] No browser performance issues with vector rendering
- [ ] Smooth layer toggle and interaction

### ✅ Data Quality Success
- [ ] Soil map unit boundaries accurate and complete
- [ ] Clay percentage values reasonable (0-100%)
- [ ] Ksat values reasonable (>0 μm/s)
- [ ] No missing or corrupted geometries
- [ ] Proper component-weighted property averaging

## Troubleshooting Guide

### Common Issues & Solutions

**SSURGO API Returns No Data:**
- Check polygon coordinates are in correct order (counter-clockwise)
- Verify test area coordinates are within Parker County bounds
- Ensure component percentage filter (>=25) isn't too restrictive

**WKT Conversion Errors:**
- NetTopologySuite will log specific parsing errors
- Check for malformed WKT in SSURGO response
- Invalid geometries can be auto-fixed with GeometryFixer

**Azure Blob Storage Issues:**
- Verify connection string and container permissions
- Check CORS settings for web access
- Ensure content-type set to "application/geo+json"

**MapLibre Display Issues:**
- Verify GeoJSON structure with online validators
- Check JavaScript console for loading errors
- Ensure CDN URLs are accessible from browser

## Next Steps After Success

### Phase 2: Scale to Full County
- Modify test area to full Parker County bounds
- Implement data chunking for large datasets
- Add progress monitoring for large data processing

### Phase 3: Add More Soil Properties
- Extend models to include pH, drainage class, hydrologic group
- Create additional visualization layers
- Implement property filtering and analysis tools

### Phase 4: Database Migration
- Migrate from blob storage to Azure SQL Database
- Add spatial indexing and dynamic queries
- Implement real-time spatial analysis capabilities

This plan provides a complete roadmap from setup to production-ready soil data integration using industry-standard tools and Azure-native architecture.