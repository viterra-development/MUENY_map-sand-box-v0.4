using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace TCDS.Importer.Services;

public class RoadTrafficMerger
{
    private readonly ILogger<RoadTrafficMerger> _logger;
    
    // Buffer distance for traffic count location matching (in degrees, ~100m at Parker County latitude)
    private const double BUFFER_DISTANCE = 0.001;
    
    public RoadTrafficMerger(ILogger<RoadTrafficMerger> logger)
    {
        _logger = logger;
    }
    
    public async Task<string> MergeRoadTrafficDataAsync(string roadGeoJsonPath, string trafficGeoJsonPath, string outputDirectory)
    {
        _logger.LogInformation("🚦 Starting Traffic-Road Data Merger");
        
        // 1. Load road data
        _logger.LogInformation("📍 Loading Parker County roads from: {RoadPath}", roadGeoJsonPath);
        
        if (!File.Exists(roadGeoJsonPath))
        {
            throw new FileNotFoundException($"Roads file not found: {roadGeoJsonPath}");
        }
        
        var roadsJson = await File.ReadAllTextAsync(roadGeoJsonPath);
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
        var roadsData = JsonSerializer.Deserialize<GeoJsonFeatureCollection>(roadsJson, jsonOptions);
        
        if (roadsData?.Features == null)
        {
            throw new InvalidOperationException("Failed to parse roads GeoJSON data");
        }
        
        _logger.LogInformation("   ✅ Loaded {RoadCount} road features", roadsData.Features.Count);
        
        // 2. Load traffic data
        _logger.LogInformation("🚥 Loading traffic count data from: {TrafficPath}", trafficGeoJsonPath);
        
        if (!File.Exists(trafficGeoJsonPath))
        {
            throw new FileNotFoundException($"Traffic data file not found: {trafficGeoJsonPath}");
        }
        
        var trafficJson = await File.ReadAllTextAsync(trafficGeoJsonPath);
        var trafficData = JsonSerializer.Deserialize<GeoJsonFeatureCollection>(trafficJson, jsonOptions);
        
        if (trafficData?.Features == null)
        {
            throw new InvalidOperationException("Failed to parse traffic GeoJSON data");
        }
        
        _logger.LogInformation("   ✅ Loaded {TrafficCount} traffic count locations", trafficData.Features.Count);
        
        // 3. Perform spatial intersection
        _logger.LogInformation("🔍 Performing spatial intersection with buffer distance: {BufferDistance} degrees (~100m)", BUFFER_DISTANCE);
        
        var roadsWithTraffic = new List<GeoJsonFeature>();
        int matchCount = 0;
        
        for (int roadIndex = 0; roadIndex < roadsData.Features.Count; roadIndex++)
        {
            var road = roadsData.Features[roadIndex];
            var hasTrafficData = false;
            TrafficProperties? trafficProperties = null;
            
            // For each road, check if it intersects with any traffic count location buffer
            foreach (var trafficLocation in trafficData.Features)
            {
                if (RoadIntersectsTrafficBuffer(road, trafficLocation, BUFFER_DISTANCE))
                {
                    hasTrafficData = true;
                    
                    // Extract traffic properties from the traffic location
                    var props = trafficLocation.Properties;
                    if (props != null)
                    {
                        trafficProperties = new TrafficProperties
                        {
                            Aadt = GetJsonValue<int?>(props, "latestAadt"),
                            Dhv30 = GetJsonValue<int?>(props, "latestDhv30"),
                            AadtYear = GetJsonValue<int?>(props, "latestAadtYear"),
                            LocationId = GetJsonValue<string>(props, "locationId"),
                            LocatedOn = GetJsonValue<string>(props, "locatedOn")
                        };
                        
                        var roadName = GetJsonValue<string>(road.Properties, "FULLNAME") ?? "Unknown Road";
                        _logger.LogInformation("   📍 Road {RoadIndex}: {RoadName} matched with traffic location {LocationId} (AADT: {Aadt})", 
                            roadIndex + 1, roadName, trafficProperties.LocationId, trafficProperties.Aadt);
                    }
                    
                    // Use first match found
                    break;
                }
            }
            
            if (hasTrafficData && trafficProperties != null)
            {
                // Create enhanced road feature with traffic data
                var enhancedProperties = new Dictionary<string, object?>(road.Properties ?? new Dictionary<string, object?>());
                enhancedProperties["traffic"] = new Dictionary<string, object?>
                {
                    ["aadt"] = trafficProperties.Aadt,
                    ["dhv30"] = trafficProperties.Dhv30,
                    ["aadtYear"] = trafficProperties.AadtYear,
                    ["locationId"] = trafficProperties.LocationId,
                    ["locatedOn"] = trafficProperties.LocatedOn
                };
                
                var enhancedRoad = new GeoJsonFeature
                {
                    Type = "Feature",
                    Geometry = road.Geometry,
                    Properties = enhancedProperties
                };
                
                roadsWithTraffic.Add(enhancedRoad);
                matchCount++;
            }
        }
        
        _logger.LogInformation("   ✅ Found {MatchCount} roads with traffic data out of {TotalRoads} total roads", 
            matchCount, roadsData.Features.Count);
        
        // 4. Create output GeoJSON
        var outputData = new GeoJsonFeatureCollection
        {
            Type = "FeatureCollection",
            Metadata = new Dictionary<string, object>
            {
                ["title"] = "Parker County Roads with Traffic Data",
                ["description"] = "Road segments enhanced with AADT traffic count data for gradient visualization",
                ["totalFeatures"] = roadsWithTraffic.Count,
                ["totalRoads"] = roadsData.Features.Count,
                ["trafficMatchingRate"] = $"{(matchCount / (double)roadsData.Features.Count * 100):F1}%",
                ["bufferDistance"] = $"{BUFFER_DISTANCE} degrees (~100m)",
                ["generatedAt"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ["source"] = "Texas Department of Transportation TCDS + TIGER/Line"
            },
            Features = roadsWithTraffic
        };
        
        // 5. Save enhanced dataset
        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, "parker-roads-with-traffic.geojson");
        
        var serializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        
        var jsonData = JsonSerializer.Serialize(outputData, serializerOptions);
        await File.WriteAllTextAsync(outputPath, jsonData);
        
        _logger.LogInformation("📁 Enhanced dataset saved to: {OutputPath}", outputPath);
        
        // 6. Display statistics
        var fileSize = (new FileInfo(outputPath).Length / 1024.0);
        _logger.LogInformation("📊 Summary:");
        _logger.LogInformation("   • Total roads: {TotalRoads}", roadsData.Features.Count);
        _logger.LogInformation("   • Roads with traffic data: {MatchCount}", matchCount);
        _logger.LogInformation("   • Matching rate: {MatchingRate:F1}%", (matchCount / (double)roadsData.Features.Count * 100));
        _logger.LogInformation("   • Output file size: {FileSize:F1} KB", fileSize);
        
        // Display AADT statistics
        var aadtValues = roadsWithTraffic
            .Select(road => GetTrafficAadt(road))
            .Where(aadt => aadt.HasValue && aadt.Value > 0)
            .Select(aadt => aadt!.Value)
            .OrderBy(x => x)
            .ToList();
        
        if (aadtValues.Count > 0)
        {
            var minAADT = aadtValues.First();
            var maxAADT = aadtValues.Last();
            var medianAADT = aadtValues[aadtValues.Count / 2];
            var rangeFactor = maxAADT / (double)minAADT;
            var logMin = Math.Log10(minAADT);
            var logMax = Math.Log10(maxAADT);
            
            _logger.LogInformation("📈 AADT Statistics:");
            _logger.LogInformation("   • Minimum: {MinAADT}", minAADT);
            _logger.LogInformation("   • Median: {MedianAADT}", medianAADT);
            _logger.LogInformation("   • Maximum: {MaxAADT}", maxAADT);
            _logger.LogInformation("   • Range Factor: {RangeFactor:F1}x", rangeFactor);
            _logger.LogInformation("   • Log₁₀ range: {LogMin:F2} to {LogMax:F2}", logMin, logMax);
        }
        
        _logger.LogInformation("✅ Traffic-Road merger complete!");
        return outputPath;
    }
    
    private bool RoadIntersectsTrafficBuffer(GeoJsonFeature road, GeoJsonFeature trafficLocation, double bufferDistance)
    {
        // Get traffic location coordinates (should be Point geometry)
        if (trafficLocation.Geometry is not PointGeometry trafficPoint || trafficPoint.Coordinates.Count < 2)
            return false;
            
        var trafficLon = trafficPoint.Coordinates[0];
        var trafficLat = trafficPoint.Coordinates[1];
        
        // Check road geometry type and coordinates
        if (road.Geometry is LineStringGeometry lineString)
        {
            // For LineString roads, check if any coordinate is within buffer
            foreach (var coord in lineString.Coordinates)
            {
                if (coord.Count >= 2)
                {
                    var roadLon = coord[0];
                    var roadLat = coord[1];
                    var distance = Math.Sqrt(Math.Pow(roadLon - trafficLon, 2) + Math.Pow(roadLat - trafficLat, 2));
                    
                    if (distance <= bufferDistance)
                        return true;
                }
            }
        }
        else if (road.Geometry is MultiLineStringGeometry multiLineString)
        {
            // For MultiLineString roads, check all line segments
            foreach (var linestring in multiLineString.Coordinates)
            {
                foreach (var coord in linestring)
                {
                    if (coord.Count >= 2)
                    {
                        var roadLon = coord[0];
                        var roadLat = coord[1];
                        var distance = Math.Sqrt(Math.Pow(roadLon - trafficLon, 2) + Math.Pow(roadLat - trafficLat, 2));
                        
                        if (distance <= bufferDistance)
                            return true;
                    }
                }
            }
        }
        
        return false;
    }
    
    private T? GetJsonValue<T>(Dictionary<string, object?>? dict, string key)
    {
        if (dict == null || !dict.TryGetValue(key, out var value) || value == null)
            return default;
            
        if (value is JsonElement jsonElement)
        {
            try
            {
                return JsonSerializer.Deserialize<T>(jsonElement.GetRawText());
            }
            catch
            {
                return default;
            }
        }
        
        if (value is T directValue)
            return directValue;
            
        try
        {
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return default;
        }
    }
    
    private int? GetTrafficAadt(GeoJsonFeature road)
    {
        if (road.Properties?.TryGetValue("traffic", out var trafficObj) == true)
        {
            if (trafficObj is JsonElement trafficElement && trafficElement.ValueKind == JsonValueKind.Object)
            {
                if (trafficElement.TryGetProperty("aadt", out var aadtElement))
                {
                    return aadtElement.ValueKind == JsonValueKind.Number ? aadtElement.GetInt32() : null;
                }
            }
            else if (trafficObj is Dictionary<string, object?> trafficDict)
            {
                return GetJsonValue<int?>(trafficDict, "aadt");
            }
        }
        
        return null;
    }
}

// Data models for GeoJSON handling
public class GeoJsonFeatureCollection
{
    public string Type { get; set; } = "FeatureCollection";
    public Dictionary<string, object>? Metadata { get; set; }
    public List<GeoJsonFeature> Features { get; set; } = new();
}

public class GeoJsonFeature
{
    public string Type { get; set; } = "Feature";
    public GeoJsonGeometry? Geometry { get; set; }
    public Dictionary<string, object?>? Properties { get; set; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(PointGeometry), "Point")]
[JsonDerivedType(typeof(LineStringGeometry), "LineString")]
[JsonDerivedType(typeof(MultiLineStringGeometry), "MultiLineString")]
public abstract class GeoJsonGeometry
{
}

public class PointGeometry : GeoJsonGeometry
{
    [JsonPropertyName("coordinates")]
    public List<double> Coordinates { get; set; } = new(); // [lon, lat]
}

public class LineStringGeometry : GeoJsonGeometry
{
    [JsonPropertyName("coordinates")]
    public List<List<double>> Coordinates { get; set; } = new(); // [[lon, lat], [lon, lat], ...]
}

public class MultiLineStringGeometry : GeoJsonGeometry
{
    [JsonPropertyName("coordinates")]
    public List<List<List<double>>> Coordinates { get; set; } = new(); // [[[lon, lat], ...], [[lon, lat], ...]]
}

public class TrafficProperties
{
    public int? Aadt { get; set; }
    public int? Dhv30 { get; set; }
    public int? AadtYear { get; set; }
    public string? LocationId { get; set; }
    public string? LocatedOn { get; set; }
}