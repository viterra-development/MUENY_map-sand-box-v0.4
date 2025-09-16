using MapSandBox.Models;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using NetTopologySuite.Features;

namespace CrisDataProcessor.Services;

public class CrisSpatialAnalyzer
{
    private readonly ILogger<CrisSpatialAnalyzer> _logger;
    private readonly double _proximityThresholdMeters;
    private readonly GeometryFactory _geometryFactory;
    private readonly GeoJsonReader _geoJsonReader;

    public CrisSpatialAnalyzer(ILogger<CrisSpatialAnalyzer> logger, double proximityThresholdMeters = 100.0)
    {
        _logger = logger;
        _proximityThresholdMeters = proximityThresholdMeters;
        _geometryFactory = new GeometryFactory(new PrecisionModel(), 4326); // WGS84
        _geoJsonReader = new GeoJsonReader();
    }

    public async Task<List<SpatialJoinResult>> SpatialJoinCrashesToRoadsAsync(
        List<CrashRecord> crashes,
        string roadGeoJsonPath)
    {
        _logger.LogInformation("Performing spatial join of {CrashCount} crashes to road network at {RoadPath}",
            crashes.Count, roadGeoJsonPath);

        var roadFeatures = await LoadRoadFeaturesAsync(roadGeoJsonPath);
        var results = new List<SpatialJoinResult>();

        foreach (var crash in crashes)
        {
            var crashPoint = CreatePoint((double)crash.Latitude, (double)crash.Longitude);
            var closestRoad = FindClosestRoadSegment(crashPoint, roadFeatures);

            if (closestRoad != null)
            {
                var distance = crashPoint.Distance(closestRoad.Geometry);
                // Convert degrees to meters (approximate)
                var distanceMeters = distance * 111320.0; // 1 degree ≈ 111.32 km

                results.Add(new SpatialJoinResult
                {
                    Crash = crash,
                    RoadSegmentId = closestRoad.Id,
                    DistanceToRoad = (decimal)distanceMeters,
                    WithinThreshold = distanceMeters <= _proximityThresholdMeters
                });
            }
        }

        var successfulJoins = results.Count(r => r.WithinThreshold);
        _logger.LogInformation("Spatial join completed: {SuccessfulJoins}/{TotalCrashes} crashes matched to road segments within {Threshold}m",
            successfulJoins, crashes.Count, _proximityThresholdMeters);

        return results;
    }

    public Dictionary<string, List<CrashRecord>> GroupCrashesByRoadSegment(List<SpatialJoinResult> spatialJoinResults)
    {
        var crashesBySegment = spatialJoinResults
            .Where(r => r.WithinThreshold)
            .GroupBy(r => r.RoadSegmentId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Crash).ToList());

        _logger.LogInformation("Grouped crashes into {SegmentCount} road segments", crashesBySegment.Count);
        return crashesBySegment;
    }

    public async Task<Dictionary<string, int>> ExtractAadtFromRoadDataAsync(string roadGeoJsonPath)
    {
        _logger.LogInformation("Extracting AADT data from road GeoJSON: {RoadPath}", roadGeoJsonPath);

        var roadFeatures = await LoadRoadFeaturesAsync(roadGeoJsonPath);
        var aadtBySegment = roadFeatures
            .Where(f => f.Aadt.HasValue && f.Aadt > 0)
            .ToDictionary(f => f.Id, f => f.Aadt!.Value);

        _logger.LogInformation("Extracted AADT data for {Count} road segments", aadtBySegment.Count);
        return aadtBySegment;
    }

    public List<IntersectionRisk> IdentifyHighRiskIntersections(
        List<CrashRecord> crashes,
        double proximityRadiusMeters = 50.0)
    {
        _logger.LogInformation("Identifying high-risk intersections from {CrashCount} crashes", crashes.Count);

        var crashPoints = crashes.Select(c => new CrashPoint
        {
            Crash = c,
            Point = CreatePoint((double)c.Latitude, (double)c.Longitude)
        }).ToList();

        var intersectionClusters = new List<IntersectionCluster>();

        foreach (var crashPoint in crashPoints)
        {
            var proximityRadiusDegrees = proximityRadiusMeters / 111320.0; // Convert meters to degrees
            var buffer = crashPoint.Point.Buffer(proximityRadiusDegrees);

            var nearbyCluster = intersectionClusters.FirstOrDefault(c =>
                buffer.Intersects(c.CenterPoint));

            if (nearbyCluster != null)
            {
                nearbyCluster.CrashPoints.Add(crashPoint);
                // Recalculate cluster center
                nearbyCluster.UpdateCenter();
            }
            else
            {
                intersectionClusters.Add(new IntersectionCluster
                {
                    Id = Guid.NewGuid().ToString(),
                    CrashPoints = new List<CrashPoint> { crashPoint }
                });
            }
        }

        var intersectionRisks = intersectionClusters
            .Where(c => c.CrashPoints.Count >= 2)
            .Select(c => new IntersectionRisk
            {
                IntersectionId = c.Id,
                Latitude = (decimal)c.CenterPoint.Y,
                Longitude = (decimal)c.CenterPoint.X,
                CrashCount = c.CrashPoints.Count,
                RecentCrashes = c.CrashPoints
                    .Select(cp => cp.Crash)
                    .OrderByDescending(cr => cr.CrashDateTime)
                    .Take(10)
                    .ToList(),
                RiskScore = CalculateIntersectionRiskScore(c.CrashPoints.Select(cp => cp.Crash).ToList()),
                RiskLevel = DetermineRiskLevel(CalculateIntersectionRiskScore(c.CrashPoints.Select(cp => cp.Crash).ToList()))
            })
            .OrderByDescending(i => i.RiskScore)
            .ToList();

        _logger.LogInformation("Identified {Count} high-risk intersections", intersectionRisks.Count);
        return intersectionRisks;
    }

    private async Task<List<RoadFeature>> LoadRoadFeaturesAsync(string geoJsonPath)
    {
        if (!File.Exists(geoJsonPath))
        {
            _logger.LogWarning("Road GeoJSON file not found: {Path}", geoJsonPath);
            return new List<RoadFeature>();
        }

        var geoJson = await File.ReadAllTextAsync(geoJsonPath);
        var featureCollection = _geoJsonReader.Read<FeatureCollection>(geoJson);

        var features = new List<RoadFeature>();

        foreach (var feature in featureCollection)
        {
            var roadFeature = new RoadFeature
            {
                Id = feature.Attributes.GetOptionalValue("id")?.ToString() ??
                     feature.Attributes.GetOptionalValue("ID")?.ToString() ??
                     Guid.NewGuid().ToString(),
                Geometry = feature.Geometry
            };

            // Try to extract AADT from various possible field names
            var aadtValue = feature.Attributes.GetOptionalValue("AADT") ??
                           feature.Attributes.GetOptionalValue("aadt") ??
                           feature.Attributes.GetOptionalValue("traffic_volume");

            if (aadtValue != null && int.TryParse(aadtValue.ToString(), out var aadt))
            {
                roadFeature.Aadt = aadt;
            }

            features.Add(roadFeature);
        }

        _logger.LogDebug("Loaded {Count} road features from {Path}", features.Count, geoJsonPath);
        return features;
    }

    private Point CreatePoint(double latitude, double longitude)
    {
        return _geometryFactory.CreatePoint(new Coordinate(longitude, latitude));
    }

    private RoadFeature? FindClosestRoadSegment(Point crashPoint, List<RoadFeature> roadFeatures)
    {
        return roadFeatures
            .OrderBy(r => crashPoint.Distance(r.Geometry))
            .FirstOrDefault();
    }

    private decimal CalculateIntersectionRiskScore(List<CrashRecord> crashes)
    {
        if (!crashes.Any()) return 0;

        var severityWeights = new Dictionary<KabcoSeverity, decimal>
        {
            { KabcoSeverity.K_Fatal, 1.0m },
            { KabcoSeverity.A_IncapacitatingInjury, 0.8m },
            { KabcoSeverity.B_NonIncapacitatingInjury, 0.6m },
            { KabcoSeverity.C_PossibleInjury, 0.4m },
            { KabcoSeverity.O_NoInjury, 0.1m },
            { KabcoSeverity.Unknown, 0.05m }
        };

        var totalWeight = crashes.Sum(c => severityWeights.GetValueOrDefault(c.Severity, 0.05m));
        var averageWeight = totalWeight / crashes.Count;
        var countFactor = Math.Min((decimal)crashes.Count / 10m, 1.0m);

        return (averageWeight * 0.7m) + (countFactor * 0.3m);
    }

    private RiskLevel DetermineRiskLevel(decimal score)
    {
        return score switch
        {
            >= 0.8m => RiskLevel.VeryHigh,
            >= 0.6m => RiskLevel.High,
            >= 0.4m => RiskLevel.Moderate,
            >= 0.2m => RiskLevel.Low,
            _ => RiskLevel.VeryLow
        };
    }

    private class RoadFeature
    {
        public string Id { get; set; } = "";
        public Geometry Geometry { get; set; } = null!;
        public int? Aadt { get; set; }
    }

    private class CrashPoint
    {
        public CrashRecord Crash { get; set; } = null!;
        public Point Point { get; set; } = null!;
    }

    private class IntersectionCluster
    {
        public string Id { get; set; } = "";
        public List<CrashPoint> CrashPoints { get; set; } = new();
        public Point CenterPoint => CalculateCenter();

        public void UpdateCenter()
        {
            // Center is recalculated via the CenterPoint property
        }

        private Point CalculateCenter()
        {
            if (!CrashPoints.Any()) return new GeometryFactory().CreatePoint(new Coordinate(0, 0));

            var avgX = CrashPoints.Average(cp => cp.Point.X);
            var avgY = CrashPoints.Average(cp => cp.Point.Y);
            return new GeometryFactory().CreatePoint(new Coordinate(avgX, avgY));
        }
    }
}