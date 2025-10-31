using Microsoft.Extensions.Logging;
using TCDS.Importer.Models;
using System.Text.Json;

namespace TCDS.Importer.Services;

/// <summary>
/// Orchestrates traffic AADT estimation for roads without direct traffic count data
/// </summary>
public class TrafficEstimationService
{
    private readonly ILogger<TrafficEstimationService> _logger;
    private readonly SpatialInterpolationEstimator _estimator;

    public TrafficEstimationService(
        ILogger<TrafficEstimationService> logger,
        SpatialInterpolationEstimator estimator)
    {
        _logger = logger;
        _estimator = estimator;
    }

    /// <summary>
    /// Loads road data from GeoJSON and converts to RoadSegment objects
    /// </summary>
    public async Task<List<RoadSegment>> LoadRoadSegmentsAsync(string geoJsonPath)
    {
        _logger.LogInformation("Loading road segments from: {Path}", geoJsonPath);

        if (!File.Exists(geoJsonPath))
            throw new FileNotFoundException($"GeoJSON file not found: {geoJsonPath}");

        var json = await File.ReadAllTextAsync(geoJsonPath);
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        var featureCollection = JsonSerializer.Deserialize<GeoJsonFeatureCollection>(json, jsonOptions);
        if (featureCollection?.Features == null)
            throw new InvalidOperationException("Failed to parse GeoJSON data");

        var segments = new List<RoadSegment>();

        foreach (var feature in featureCollection.Features)
        {
            var props = feature.Properties ?? new Dictionary<string, object?>();

            // Normalize property keys to camelCase for consistency
            var normalizedProps = NormalizePropertyKeys(props);

            var segment = new RoadSegment
            {
                LinearId = GetPropertyValue<string>(normalizedProps, "linearId") ?? string.Empty,
                FullName = GetPropertyValue<string>(normalizedProps, "fullName") ?? "Unknown Road",
                Geometry = feature.Geometry,
                OriginalProperties = normalizedProps
            };

            // Try to get hierarchy from existing classification first
            var classificationObj = normalizedProps.TryGetValue("classification", out var classVal) ? classVal : null;
            if (classificationObj != null)
            {
                try
                {
                    var classJson = JsonSerializer.Serialize(classificationObj);
                    var classDict = JsonSerializer.Deserialize<Dictionary<string, object?>>(classJson);
                    if (classDict != null && classDict.TryGetValue("hierarchy", out var hierVal))
                    {
                        var hierarchyInt = Convert.ToInt32(hierVal);
                        segment.Hierarchy = (RoadHierarchy)hierarchyInt;
                    }
                }
                catch
                {
                    // Fall through to manual determination
                }
            }

            // If no classification found, determine hierarchy manually
            if (segment.Hierarchy == 0)
            {
                var roadType = GetPropertyValue<string>(normalizedProps, "roadType");
                var mtfcc = GetPropertyValue<string>(normalizedProps, "mtfcc");
                segment.Hierarchy = DetermineHierarchy(roadType, mtfcc, segment.FullName);
            }

            // Extract existing traffic data if present
            ExtractExistingTraffic(segment, normalizedProps);

            // Calculate centroid for distance calculations
            CalculateCentroid(segment);

            segments.Add(segment);
        }

        _logger.LogInformation("Loaded {Count} road segments", segments.Count);
        _logger.LogInformation("  - With existing AADT: {WithAadt}", segments.Count(s => s.ExistingAadt.HasValue));
        _logger.LogInformation("  - Without AADT: {WithoutAadt}", segments.Count(s => !s.ExistingAadt.HasValue));

        return segments;
    }

    /// <summary>
    /// Estimates AADT for all roads without existing traffic data
    /// </summary>
    public EstimationSummary EstimateTrafficForAllRoads(List<RoadSegment> allSegments)
    {
        _logger.LogInformation("Starting traffic estimation for {Count} road segments", allSegments.Count);

        var roadsWithData = allSegments.Where(s => s.ExistingAadt.HasValue).ToList();
        var roadsNeedingEstimation = allSegments.Where(s => !s.ExistingAadt.HasValue).ToList();

        _logger.LogInformation("  - Roads with existing data: {Count}", roadsWithData.Count);
        _logger.LogInformation("  - Roads needing estimation: {Count}", roadsNeedingEstimation.Count);

        var estimatedCount = 0;
        var failedCount = 0;
        var totalConfidence = 0.0;

        foreach (var road in roadsNeedingEstimation)
        {
            var estimation = _estimator.EstimateAadt(road, roadsWithData);

            if (estimation != null)
            {
                road.Estimation = estimation;
                estimatedCount++;
                totalConfidence += estimation.Confidence;

                if (estimatedCount % 500 == 0)
                {
                    _logger.LogDebug("Estimated traffic for {Count}/{Total} roads",
                        estimatedCount, roadsNeedingEstimation.Count);
                }
            }
            else
            {
                failedCount++;
            }
        }

        var summary = new EstimationSummary
        {
            TotalRoads = allSegments.Count,
            RoadsWithExistingData = roadsWithData.Count,
            RoadsEstimated = estimatedCount,
            RoadsUnableToEstimate = failedCount,
            AverageConfidence = estimatedCount > 0 ? totalConfidence / estimatedCount : 0.0
        };

        // Calculate breakdown by hierarchy
        foreach (RoadHierarchy hierarchy in Enum.GetValues(typeof(RoadHierarchy)))
        {
            var count = roadsNeedingEstimation.Count(s =>
                s.Hierarchy == hierarchy && s.Estimation != null);
            if (count > 0)
            {
                summary.EstimatedByHierarchy[hierarchy] = count;
            }
        }

        // Count by estimation method
        var methodCounts = roadsNeedingEstimation
            .Where(s => s.Estimation != null)
            .GroupBy(s => s.Estimation!.Method)
            .ToDictionary(g => g.Key, g => g.Count());

        summary.EstimationMethodCounts = methodCounts;

        _logger.LogInformation("Estimation complete:");
        _logger.LogInformation("  ✓ Successfully estimated: {Count} roads ({Percentage:F1}%)",
            estimatedCount, estimatedCount * 100.0 / roadsNeedingEstimation.Count);
        _logger.LogInformation("  ✗ Unable to estimate: {Count} roads ({Percentage:F1}%)",
            failedCount, failedCount * 100.0 / roadsNeedingEstimation.Count);
        _logger.LogInformation("  📊 Average confidence: {Confidence:F2}",
            summary.AverageConfidence);

        return summary;
    }

    /// <summary>
    /// Saves estimated roads to GeoJSON file
    /// </summary>
    public async Task<string> SaveEstimatedRoadsAsync(
        List<RoadSegment> segments,
        EstimationSummary summary,
        string outputPath)
    {
        _logger.LogInformation("Saving estimated roads to: {Path}", outputPath);

        var features = new List<GeoJsonFeature>();

        foreach (var segment in segments)
        {
            var props = new Dictionary<string, object?>(segment.OriginalProperties ?? new());

            // Add or update traffic properties
            if (segment.ExistingAadt.HasValue)
            {
                // Keep existing traffic data
                props["traffic"] = new Dictionary<string, object?>
                {
                    ["aadt"] = segment.ExistingAadt.Value,
                    ["aadtYear"] = segment.ExistingAadtYear,
                    ["source"] = "measured"
                };
            }
            else if (segment.Estimation != null)
            {
                // Add estimated traffic data
                props["traffic"] = new Dictionary<string, object?>
                {
                    ["aadt"] = segment.Estimation.EstimatedAadt,
                    ["aadtYear"] = segment.Estimation.EstimationYear,
                    ["source"] = "estimated",
                    ["method"] = segment.Estimation.Method,
                    ["confidence"] = Math.Round(segment.Estimation.Confidence, 3),
                    ["sourceRoadCount"] = segment.Estimation.SourceRoads.Count
                };

                // Add warnings if present
                if (segment.Estimation.Warnings.Any())
                {
                    props["estimationWarnings"] = segment.Estimation.Warnings;
                }
            }
            else
            {
                // No data available
                props["traffic"] = null;
            }

            features.Add(new GeoJsonFeature
            {
                Type = "Feature",
                Geometry = segment.Geometry,
                Properties = props
            });
        }

        var metadata = new Dictionary<string, object>
        {
            ["title"] = "Parker County Roads with Traffic Estimates (Phase 1)",
            ["description"] = "Road network with measured and estimated AADT values using Spatial Interpolation (IDW)",
            ["method"] = "Phase 1: Spatial Interpolation using Inverse Distance Weighting",
            ["generatedAt"] = DateTime.UtcNow,
            ["totalFeatures"] = segments.Count,
            ["summary"] = new Dictionary<string, object>
            {
                ["totalRoads"] = summary.TotalRoads,
                ["roadsWithExistingData"] = summary.RoadsWithExistingData,
                ["roadsEstimated"] = summary.RoadsEstimated,
                ["roadsUnableToEstimate"] = summary.RoadsUnableToEstimate,
                ["averageConfidence"] = Math.Round(summary.AverageConfidence, 3),
                ["estimatedByHierarchy"] = summary.EstimatedByHierarchy
                    .ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value)
            }
        };

        var featureCollection = new GeoJsonFeatureCollection
        {
            Type = "FeatureCollection",
            Metadata = metadata,
            Features = features
        };

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        var json = JsonSerializer.Serialize(featureCollection, jsonOptions);
        await File.WriteAllTextAsync(outputPath, json);

        _logger.LogInformation("✅ Saved {Count} features to {Path}", features.Count, outputPath);

        return outputPath;
    }

    // Helper methods

    private void ExtractExistingTraffic(RoadSegment segment, Dictionary<string, object?> props)
    {
        // Check for traffic data in properties
        if (props.TryGetValue("traffic", out var trafficObj) && trafficObj != null)
        {
            try
            {
                var trafficJson = JsonSerializer.Serialize(trafficObj);
                var trafficDict = JsonSerializer.Deserialize<Dictionary<string, object?>>(trafficJson);

                if (trafficDict != null)
                {
                    segment.ExistingAadt = GetPropertyValue<int?>(trafficDict, "aadt");
                    segment.ExistingAadtYear = GetPropertyValue<int?>(trafficDict, "aadtYear");
                }
            }
            catch
            {
                // Traffic data format not recognized
            }
        }
    }

    private void CalculateCentroid(RoadSegment segment)
    {
        if (segment.Geometry is LineStringGeometry lineString && lineString.Coordinates.Any())
        {
            // Use midpoint of line for centroid
            var midIndex = lineString.Coordinates.Count / 2;
            var coord = lineString.Coordinates[midIndex];
            segment.CentroidLongitude = coord[0];
            segment.CentroidLatitude = coord[1];
        }
        else if (segment.Geometry is MultiLineStringGeometry multiLineString && multiLineString.Coordinates.Any())
        {
            // Use first line's midpoint
            var firstLine = multiLineString.Coordinates[0];
            if (firstLine.Any())
            {
                var midIndex = firstLine.Count / 2;
                var coord = firstLine[midIndex];
                segment.CentroidLongitude = coord[0];
                segment.CentroidLatitude = coord[1];
            }
        }
    }

    private RoadHierarchy DetermineHierarchy(string? rttyp, string? mtfcc, string fullName)
    {
        // Interstate
        if (rttyp == "I" || fullName?.StartsWith("I-") == true || fullName?.StartsWith("I ") == true)
            return RoadHierarchy.Interstate;

        // US Highway
        if (rttyp == "U" || fullName?.Contains("US Hwy") == true || fullName?.Contains("US-") == true)
            return RoadHierarchy.USHighway;

        // State Highway (includes FM roads)
        if (rttyp == "S" || fullName?.StartsWith("FM ") == true || fullName?.StartsWith("State Hwy") == true)
            return RoadHierarchy.StateHighway;

        // Use MTFCC codes
        if (mtfcc != null)
        {
            return mtfcc switch
            {
                "S1100" => RoadHierarchy.Interstate,
                "S1200" => RoadHierarchy.USHighway,
                "S1400" => RoadHierarchy.Arterial,
                "S1630" or "S1640" => RoadHierarchy.Ramp,
                _ => RoadHierarchy.LocalRoad
            };
        }

        // Major roads
        if (rttyp == "M") return RoadHierarchy.Arterial;

        return RoadHierarchy.LocalRoad;
    }

    /// <summary>
    /// Normalizes property keys to camelCase for consistency across all GeoJSON files.
    /// Converts UPPERCASE keys (from TIGER/Line) to camelCase (standard JSON convention).
    /// </summary>
    private Dictionary<string, object?> NormalizePropertyKeys(Dictionary<string, object?> props)
    {
        var normalized = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in props)
        {
            // Convert key to camelCase
            var camelCaseKey = ToCamelCase(kvp.Key);
            normalized[camelCaseKey] = kvp.Value;
        }

        return normalized;
    }

    /// <summary>
    /// Converts a string to camelCase (first letter lowercase, rest as-is)
    /// </summary>
    private string ToCamelCase(string str)
    {
        if (string.IsNullOrEmpty(str) || char.IsLower(str[0]))
            return str;

        return char.ToLowerInvariant(str[0]) + str.Substring(1);
    }

    private T? GetPropertyValue<T>(Dictionary<string, object?> props, string key)
    {
        if (props.TryGetValue(key, out var value) && value != null)
        {
            try
            {
                if (value is JsonElement jsonElement)
                {
                    return JsonSerializer.Deserialize<T>(jsonElement.GetRawText());
                }
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return default;
            }
        }
        return default;
    }
}
