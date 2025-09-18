using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using TCDS.Importer.Models;

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
    
    public async Task<string> MergeRoadTrafficDataAsync(string roadGeoJsonPath, string trafficGeoJsonPath, string outputDirectory, bool excludeInactiveStations = true)
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
        
        // Filter out inactive stations if requested
        if (excludeInactiveStations && trafficData.Features != null)
        {
            var originalCount = trafficData.Features.Count;
            trafficData.Features = trafficData.Features
                .Where(f => f.Properties?.TryGetValue("active", out var active) != true || active?.ToString() != "No")
                .ToList();
            _logger.LogInformation("   ✅ Loaded {TrafficCount} traffic count locations, filtered to {FilteredCount} active stations (excludeInactive: {ExcludeInactive})", 
                originalCount, trafficData.Features.Count, excludeInactiveStations);
        }
        else
        {
            _logger.LogInformation("   ✅ Loaded {TrafficCount} traffic count locations (excludeInactive: {ExcludeInactive})", 
                trafficData.Features.Count, excludeInactiveStations);
        }
        
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

    public async Task<string> MergeRoadTrafficDataFromMasterAsync(string roadGeoJsonPath, string masterDataPath, string outputDirectory, bool excludeInactiveStations = true)
    {
        _logger.LogInformation("🚦 Starting Traffic-Road Data Merger (using MASTER data)");
        
        // 1. Load road data (same as before)
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
        
        // 2. Load MASTER traffic data
        _logger.LogInformation("🚥 Loading MASTER traffic data from: {MasterPath}", masterDataPath);
        
        if (!File.Exists(masterDataPath))
        {
            throw new FileNotFoundException($"MASTER data file not found: {masterDataPath}");
        }
        
        var masterJson = await File.ReadAllTextAsync(masterDataPath);
        var masterDocument = JsonSerializer.Deserialize<JsonElement>(masterJson);
        
        if (!masterDocument.TryGetProperty("records", out var recordsElement))
        {
            throw new InvalidOperationException("MASTER data file does not contain 'records' property");
        }
        
        var trafficRecords = JsonSerializer.Deserialize<List<TrafficCountData>>(recordsElement.GetRawText(), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        
        if (trafficRecords == null)
        {
            throw new InvalidOperationException("Failed to parse MASTER traffic data");
        }
        
        // Filter records with valid coordinates, latest AADT data, and optionally exclude inactive stations
        var validTrafficLocations = trafficRecords
            .Where(r => r.LocationInfo?.Latitude.HasValue == true && 
                       r.LocationInfo?.Longitude.HasValue == true &&
                       r.AadtData?.Any() == true)
            .Where(r => !excludeInactiveStations || r.LocationInfo?.Active != "No")
            .ToList();
        
        _logger.LogInformation("   ✅ Loaded {TotalRecords} total records, {ValidCount} with valid coordinates and AADT data (excludeInactive: {ExcludeInactive})", 
            trafficRecords.Count, validTrafficLocations.Count, excludeInactiveStations);
        
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
            foreach (var trafficRecord in validTrafficLocations)
            {
                // Convert TrafficCountData to GeoJSON Point format for intersection check
                var trafficPointFeature = new GeoJsonFeature
                {
                    Type = "Feature",
                    Geometry = new PointGeometry
                    {
                        Coordinates = new List<double> { (double)trafficRecord.LocationInfo!.Longitude!.Value, (double)trafficRecord.LocationInfo!.Latitude!.Value }
                    },
                    Properties = new Dictionary<string, object?>
                    {
                        ["locationId"] = trafficRecord.LocationInfo!.LocationId,
                        ["latestAadt"] = trafficRecord.AadtData!.OrderByDescending(a => a.Year).FirstOrDefault()?.Aadt
                    }
                };
                
                if (RoadIntersectsTrafficBuffer(road, trafficPointFeature, BUFFER_DISTANCE))
                {
                    hasTrafficData = true;
                    
                    // Get latest AADT data
                    var latestAadt = trafficRecord.AadtData!.OrderByDescending(a => a.Year).FirstOrDefault();
                    
                    trafficProperties = new TrafficProperties
                    {
                        Aadt = latestAadt?.Aadt,
                        Dhv30 = latestAadt?.Dhv30,
                        AadtYear = latestAadt?.Year,
                        LocationId = trafficRecord.LocationInfo!.LocationId,
                        LocatedOn = trafficRecord.LocationInfo!.LocatedOn
                    };
                    
                    var roadName = GetJsonValue<string>(road.Properties, "FULLNAME") ?? "Unknown Road";
                    _logger.LogInformation("   📍 Road {RoadIndex}: {RoadName} matched with traffic location {LocationId} (AADT: {Aadt})", 
                        roadIndex + 1, roadName, trafficProperties.LocationId, trafficProperties.Aadt);
                    
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
                ["title"] = "Parker County Roads with Traffic Data (from MASTER)",
                ["description"] = "Road segments enhanced with AADT traffic count data for gradient visualization",
                ["totalFeatures"] = roadsWithTraffic.Count,
                ["totalRoads"] = roadsData.Features.Count,
                ["totalTrafficLocations"] = validTrafficLocations.Count,
                ["trafficMatchingRate"] = $"{(matchCount / (double)roadsData.Features.Count * 100):F1}%",
                ["bufferDistance"] = $"{BUFFER_DISTANCE} degrees (~100m)",
                ["generatedAt"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ["source"] = "Texas Department of Transportation TCDS MASTER data + TIGER/Line"
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
        _logger.LogInformation("   • Total traffic locations processed: {TrafficLocations}", validTrafficLocations.Count);
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

// Models moved to TCDS.Importer.Models namespace