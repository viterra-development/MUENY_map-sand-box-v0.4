using System.Text.Json;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.IO;
using MapSandBox.Models;
using Microsoft.Extensions.Logging;

namespace CrisDataProcessor.Services;

public class RoadGeometryService
{
    private readonly ILogger<RoadGeometryService> _logger;
    private readonly GeoJsonReader _geoJsonReader;
    private STRtree<RoadFeature>? _spatialIndex;
    private List<RoadFeature> _roadFeatures = new();
    private bool _isLoaded = false;
    private RoadGeometryQualityMetrics _qualityMetrics = new();

    public RoadGeometryService(ILogger<RoadGeometryService> logger)
    {
        _logger = logger;
        _geoJsonReader = new GeoJsonReader();
    }

    public async Task LoadRoadDataAsync(string roadGeoJsonPath)
    {
        if (_isLoaded)
        {
            _logger.LogInformation("Road data already loaded");
            return;
        }

        _logger.LogInformation("Loading road data from {Path}", roadGeoJsonPath);

        if (!File.Exists(roadGeoJsonPath))
        {
            throw new FileNotFoundException($"Road data file not found: {roadGeoJsonPath}");
        }

        var geoJsonContent = await File.ReadAllTextAsync(roadGeoJsonPath);
        var geoJsonDoc = JsonDocument.Parse(geoJsonContent);

        _spatialIndex = new STRtree<RoadFeature>();
        _roadFeatures = new List<RoadFeature>();

        var featuresArray = geoJsonDoc.RootElement.GetProperty("features");
        int totalFeatures = 0;
        int discardedFeatures = 0;
        int unnamedRoads = 0;
        int unknownTypes = 0;

        foreach (var featureElement in featuresArray.EnumerateArray())
        {
            totalFeatures++;
            try
            {
                var roadFeature = ParseRoadFeature(featureElement);
                if (roadFeature != null)
                {
                    _roadFeatures.Add(roadFeature);
                    _spatialIndex.Insert(roadFeature.Geometry.EnvelopeInternal, roadFeature);

                    // Track quality metrics
                    if (roadFeature.FullName == "Unnamed Road") unnamedRoads++;
                    if (roadFeature.RoadType == "U") unknownTypes++;
                }
                else
                {
                    discardedFeatures++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse road feature");
                discardedFeatures++;
            }
        }

        _spatialIndex.Build();
        _isLoaded = true;

        // Update quality metrics
        _qualityMetrics = new RoadGeometryQualityMetrics
        {
            TotalRoadFeatures = _roadFeatures.Count,
            RoadsWithNames = _roadFeatures.Count - unnamedRoads,
            PercentageWithNames = _roadFeatures.Count > 0 ? (double)(_roadFeatures.Count - unnamedRoads) / _roadFeatures.Count * 100 : 0,
            AverageCoordinatesPerRoad = _roadFeatures.Count > 0 ? _roadFeatures.Average(r => r.Coordinates.Count) : 0,
            RoadsByType = _roadFeatures.GroupBy(r => r.RoadType).ToDictionary(g => g.Key, g => g.Count()),
            TotalFeaturesProcessed = totalFeatures,
            DiscardedFeatures = discardedFeatures,
            UnnamedRoads = unnamedRoads,
            UnknownTypes = unknownTypes
        };

        _logger.LogInformation("Loaded {ValidCount} valid road features from {TotalCount} total features",
            _roadFeatures.Count, totalFeatures);
        _logger.LogInformation("Quality: {DiscardedCount} discarded, {UnnamedCount} unnamed, {UnknownTypeCount} unknown types",
            discardedFeatures, unnamedRoads, unknownTypes);
    }

    private RoadFeature? ParseRoadFeature(JsonElement featureElement)
    {
        var geometryElement = featureElement.GetProperty("geometry");
        var propertiesElement = featureElement.GetProperty("properties");

        // Extract LINEARID first - this is CRITICAL and must exist
        if (!propertiesElement.TryGetProperty("LINEARID", out var linearIdProp) ||
            string.IsNullOrWhiteSpace(linearIdProp.GetString()))
        {
            // DISCARD: No point in keeping a road feature without a unique identifier
            return null;
        }

        var linearId = linearIdProp.GetString()!;

        // Parse geometry using NetTopologySuite
        var geometryJson = JsonSerializer.Serialize(geometryElement);
        var geometry = _geoJsonReader.Read<Geometry>(geometryJson);

        if (geometry is not LineString lineString)
        {
            // DISCARD: We only work with LineString geometries for roads
            return null;
        }

        // Extract other properties with opinionated defaults
        var fullName = propertiesElement.TryGetProperty("FULLNAME", out var fullNameProp) &&
                       !string.IsNullOrWhiteSpace(fullNameProp.GetString())
            ? fullNameProp.GetString()!
            : "Unnamed Road"; // Default for missing/empty road names

        var roadType = propertiesElement.TryGetProperty("RTTYP", out var rttypProp) &&
                       !string.IsNullOrWhiteSpace(rttypProp.GetString())
            ? rttypProp.GetString()!
            : "U"; // Default for unknown road type

        var mtfccCode = propertiesElement.TryGetProperty("MTFCC", out var mtfccProp) &&
                        !string.IsNullOrWhiteSpace(mtfccProp.GetString())
            ? mtfccProp.GetString()!
            : "S9999"; // Default for unknown MTFCC code

        // Convert coordinates to our format
        var coordinates = lineString.Coordinates
            .Select(coord => new double[] { coord.X, coord.Y })
            .ToList();

        return new RoadFeature
        {
            LinearId = linearId,
            FullName = fullName,
            RoadType = roadType,
            MtfccCode = mtfccCode,
            Geometry = lineString,
            Coordinates = coordinates
        };
    }

    public List<RoadMatchResult> FindNearestRoads(double latitude, double longitude, double toleranceMeters = 50.0)
    {
        if (!_isLoaded || _spatialIndex == null)
        {
            throw new InvalidOperationException("Road data not loaded. Call LoadRoadDataAsync first.");
        }

        var point = new Point(longitude, latitude);
        // Convert tolerance from meters to degrees (rough approximation: 1 degree ≈ 111km)
        var toleranceDegrees = toleranceMeters / 111000.0;
        var searchEnvelope = point.Buffer(toleranceDegrees).EnvelopeInternal;

        var candidates = _spatialIndex.Query(searchEnvelope).ToList();
        var results = new List<RoadMatchResult>();

        foreach (var road in candidates)
        {
            // Calculate distance in meters (rough approximation)
            var distanceDegrees = road.Geometry.Distance(point);
            var distanceMeters = distanceDegrees * 111000.0;

            if (distanceMeters <= toleranceMeters)
            {
                results.Add(new RoadMatchResult
                {
                    Road = road,
                    DistanceMeters = distanceMeters,
                    IsWithinTolerance = true
                });
            }
        }

        return results.OrderBy(r => r.DistanceMeters).ToList();
    }

    public RoadMatchResult? FindBestRoadMatch(double latitude, double longitude, double toleranceMeters = 50.0)
    {
        var matches = FindNearestRoads(latitude, longitude, toleranceMeters);

        // If multiple matches, prefer roads with names and better road types
        if (matches.Count > 1)
        {
            // Prioritize named roads over unnamed ones
            var namedRoads = matches.Where(m => m.Road.FullName != "Unnamed Road").ToList();
            if (namedRoads.Any())
            {
                matches = namedRoads;
            }

            // Among remaining matches, prefer major roads (M) over others
            var majorRoads = matches.Where(m => m.Road.RoadType == "M").ToList();
            if (majorRoads.Any())
            {
                matches = majorRoads;
            }
        }

        return matches.FirstOrDefault();
    }

    public List<RoadMatchResult> FindNearestRoadsBatch(List<(double Latitude, double Longitude)> locations, double toleranceMeters = 50.0)
    {
        var results = new List<RoadMatchResult>();

        foreach (var (latitude, longitude) in locations)
        {
            var match = FindBestRoadMatch(latitude, longitude, toleranceMeters);
            if (match != null)
            {
                results.Add(match);
            }
        }

        return results;
    }

    public RoadGeometryQualityMetrics GetQualityMetrics()
    {
        return _qualityMetrics;
    }
}

public class RoadFeature
{
    public string LinearId { get; set; } = "";
    public string FullName { get; set; } = "";
    public string RoadType { get; set; } = "";
    public string MtfccCode { get; set; } = "";
    public LineString Geometry { get; set; } = null!;
    public List<double[]> Coordinates { get; set; } = new();
}

public class RoadMatchResult
{
    public RoadFeature Road { get; set; } = null!;
    public double DistanceMeters { get; set; }
    public bool IsWithinTolerance { get; set; }
}

public class RoadGeometryQualityMetrics
{
    public int TotalRoadFeatures { get; set; }
    public int RoadsWithNames { get; set; }
    public double PercentageWithNames { get; set; }
    public double AverageCoordinatesPerRoad { get; set; }
    public Dictionary<string, int> RoadsByType { get; set; } = new();
    public int TotalFeaturesProcessed { get; set; }
    public int DiscardedFeatures { get; set; }
    public int UnnamedRoads { get; set; }
    public int UnknownTypes { get; set; }
}