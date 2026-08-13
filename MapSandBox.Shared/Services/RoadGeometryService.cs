using System.Text.Json;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.IO;
using NetTopologySuite.Operation.Distance;
using MapSandBox.Shared.Models;
using Microsoft.Extensions.Logging;

namespace MapSandBox.Shared.Services;

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

        _spatialIndex = new STRtree<RoadFeature>();
        _roadFeatures = new List<RoadFeature>();

        int totalFeatures = 0;
        int discardedFeatures = 0;
        int unnamedRoads = 0;
        int unknownTypes = 0;

        // Try to deserialize as enhanced road collection first, fall back to basic if needed
        EnhancedRoadFeatureCollection? enhancedCollection = null;
        try
        {
            enhancedCollection = JsonSerializer.Deserialize<EnhancedRoadFeatureCollection>(geoJsonContent);
        }
        catch (JsonException)
        {
            enhancedCollection = null;
        }

        // A basic-format file deserializes into the enhanced model without error but with
        // every linearId empty (basic files use LINEARID), so a collection whose features
        // all lack a LinearId is actually basic format.
        var isEnhancedFormat = enhancedCollection?.Features != null &&
                               enhancedCollection.Features.Count > 0 &&
                               enhancedCollection.Features.Any(f => !string.IsNullOrWhiteSpace(f.Properties?.LinearId));

        if (isEnhancedFormat)
        {
            _logger.LogInformation("Detected enhanced road format; loading road features with traffic data");
            foreach (var feature in enhancedCollection!.Features)
            {
                totalFeatures++;
                try
                {
                    var roadFeature = ConvertFromEnhancedFeature(feature);
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
                    _logger.LogWarning(ex, "Failed to parse enhanced road feature");
                    discardedFeatures++;
                }
            }
        }
        else
        {
            // Fall back to basic road collection
            _logger.LogInformation("Detected basic road format; loading basic road features");
            try
            {
                var basicCollection = JsonSerializer.Deserialize<BasicRoadFeatureCollection>(geoJsonContent);
                if (basicCollection?.Features != null)
                {
                    foreach (var feature in basicCollection.Features)
                    {
                        totalFeatures++;
                        try
                        {
                            var roadFeature = ConvertFromBasicFeature(feature);
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
                            _logger.LogWarning(ex, "Failed to parse basic road feature");
                            discardedFeatures++;
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Failed to parse road data as either enhanced or basic format: {ex.Message}", ex);
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
        // Convert tolerance from meters to degrees; divide by cos(lat) so the envelope
        // also covers the tolerance along the longitude axis.
        var toleranceDegrees = toleranceMeters / (111000.0 * Math.Cos(latitude * Math.PI / 180.0));
        var searchEnvelope = point.Buffer(toleranceDegrees).EnvelopeInternal;

        var candidates = _spatialIndex.Query(searchEnvelope).ToList();
        var results = new List<RoadMatchResult>();

        foreach (var road in candidates)
        {
            var distanceMeters = CalculateDistanceMeters(road.Geometry, point);

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

    /// <summary>
    /// Equirectangular distance in meters between two geometries: the longitude delta is
    /// corrected by cos(midLat) before converting degrees to meters (1 degree ≈ 111km).
    /// </summary>
    private static double CalculateDistanceMeters(Geometry a, Geometry b)
    {
        var nearest = DistanceOp.NearestPoints(a, b);
        var midLatRad = (nearest[0].Y + nearest[1].Y) / 2.0 * (Math.PI / 180.0);
        var dx = (nearest[0].X - nearest[1].X) * Math.Cos(midLatRad) * 111000.0;
        var dy = (nearest[0].Y - nearest[1].Y) * 111000.0;
        return Math.Sqrt(dx * dx + dy * dy);
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

    public List<RoadMatchResult> FindAllEligibleRoadMatches(
        double latitude,
        double longitude,
        double toleranceMeters = 50.0,
        double overlapThreshold = 0.8)
    {
        var nearbyRoads = FindNearestRoads(latitude, longitude, toleranceMeters);

        if (nearbyRoads.Count <= 1)
        {
            return nearbyRoads;
        }

        var eligibleRoads = new List<RoadMatchResult>();

        foreach (var road in nearbyRoads.OrderBy(r => r.DistanceMeters))
        {
            bool shouldInclude = false;

            if (eligibleRoads.Count == 0)
            {
                // Always include the closest road
                shouldInclude = true;
            }
            else
            {
                // Check if this road overlaps significantly with any already selected roads
                foreach (var selectedRoad in eligibleRoads)
                {
                    var overlapPercentage = CalculateOverlapPercentage(road.Road.Geometry, selectedRoad.Road.Geometry);
                    if (overlapPercentage >= overlapThreshold)
                    {
                        shouldInclude = true;
                        _logger.LogDebug("Including overlapping road: {RoadId1} ({Name1}) overlaps {Percentage:F1}% with {RoadId2} ({Name2})",
                            road.Road.LinearId, road.Road.FullName, overlapPercentage,
                            selectedRoad.Road.LinearId, selectedRoad.Road.FullName);
                        break;
                    }
                }
            }

            if (shouldInclude)
            {
                eligibleRoads.Add(road);
            }
        }

        return eligibleRoads;
    }

    private double CalculateOverlapPercentage(LineString geom1, LineString geom2)
    {
        try
        {
            // Use a small buffer to account for minor coordinate differences
            const double bufferDistance = 0.00005; // ~5 meters in decimal degrees

            var length1 = geom1.Length;
            var length2 = geom2.Length;

            if (length1 == 0 || length2 == 0)
                return 0.0;

            // Create buffers around both lines
            var buffer1 = geom1.Buffer(bufferDistance);
            var buffer2 = geom2.Buffer(bufferDistance);

            // Calculate intersection
            var intersection = buffer1.Intersection(buffer2);

            // Estimate overlap length by calculating intersection area and dividing by buffer width
            var overlapLength = intersection.Area / (bufferDistance * 2);

            // Calculate overlap percentage based on the shorter segment
            var shorterLength = Math.Min(length1, length2);
            var overlapPercentage = shorterLength > 0 ? (overlapLength / shorterLength) * 100.0 : 0.0;

            return Math.Min(overlapPercentage, 100.0); // Cap at 100%
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error calculating overlap between road segments");
            return 0.0;
        }
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

    private RoadFeature? ConvertFromEnhancedFeature(GeoJsonFeature<EnhancedRoadProperties> feature)
    {
        // Validate required properties
        if (string.IsNullOrWhiteSpace(feature.Properties.LinearId))
        {
            return null; // DISCARD: No unique identifier
        }

        if (feature.Geometry is not LineStringGeometry lineStringGeometry)
        {
            return null; // DISCARD: We only work with LineString geometries for roads
        }

        // Convert GeoJSON coordinates to NetTopologySuite geometry
        var coordinates = lineStringGeometry.Coordinates
            .Select(coord => new Coordinate(coord[0], coord[1]))
            .ToArray();

        var lineString = new LineString(coordinates);

        // Use strongly typed properties with defaults
        var fullName = !string.IsNullOrWhiteSpace(feature.Properties.FullName)
            ? feature.Properties.FullName
            : "Unnamed Road";

        var roadType = !string.IsNullOrWhiteSpace(feature.Properties.RoadType)
            ? feature.Properties.RoadType
            : "U";

        var mtfccCode = !string.IsNullOrWhiteSpace(feature.Properties.Mtfcc)
            ? feature.Properties.Mtfcc
            : "S9999";

        return new RoadFeature
        {
            LinearId = feature.Properties.LinearId,
            FullName = fullName,
            RoadType = roadType,
            MtfccCode = mtfccCode,
            Geometry = lineString,
            Coordinates = lineStringGeometry.Coordinates.Select(coord => new double[] { coord[0], coord[1] }).ToList()
        };
    }

    private RoadFeature? ConvertFromBasicFeature(GeoJsonFeature<BasicRoadProperties> feature)
    {
        // Validate required properties
        if (string.IsNullOrWhiteSpace(feature.Properties.LinearId))
        {
            return null; // DISCARD: No unique identifier
        }

        if (feature.Geometry is not LineStringGeometry lineStringGeometry)
        {
            return null; // DISCARD: We only work with LineString geometries for roads
        }

        // Convert GeoJSON coordinates to NetTopologySuite geometry
        var coordinates = lineStringGeometry.Coordinates
            .Select(coord => new Coordinate(coord[0], coord[1]))
            .ToArray();

        var lineString = new LineString(coordinates);

        // Use strongly typed properties with defaults
        var fullName = !string.IsNullOrWhiteSpace(feature.Properties.FullName)
            ? feature.Properties.FullName
            : "Unnamed Road";

        var roadType = !string.IsNullOrWhiteSpace(feature.Properties.RoadType)
            ? feature.Properties.RoadType
            : "U";

        var mtfccCode = !string.IsNullOrWhiteSpace(feature.Properties.Mtfcc)
            ? feature.Properties.Mtfcc
            : "S9999";

        return new RoadFeature
        {
            LinearId = feature.Properties.LinearId,
            FullName = fullName,
            RoadType = roadType,
            MtfccCode = mtfccCode,
            Geometry = lineString,
            Coordinates = lineStringGeometry.Coordinates.Select(coord => new double[] { coord[0], coord[1] }).ToList()
        };
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