using MapSandBox.Models;
using Microsoft.Extensions.Logging;

namespace CrisDataProcessor.Services;

public class EnhancedRiskSegmentGenerator
{
    private readonly ILogger<EnhancedRiskSegmentGenerator> _logger;
    private readonly RoadGeometryService _roadGeometryService;

    public EnhancedRiskSegmentGenerator(
        ILogger<EnhancedRiskSegmentGenerator> logger,
        RoadGeometryService roadGeometryService)
    {
        _logger = logger;
        _roadGeometryService = roadGeometryService;
    }

    public async Task<List<RiskSegment>> EnhanceRiskSegmentsWithRoadGeometry(
        List<RiskSegment> basicRiskSegments,
        List<EnhancedSpatialJoinResult> spatialJoinResults,
        string roadGeoJsonPath)
    {
        _logger.LogInformation("Enhancing {Count} risk segments with road geometry from {Path}",
            basicRiskSegments.Count, roadGeoJsonPath);

        // Ensure road geometry service is loaded
        await _roadGeometryService.LoadRoadDataAsync(roadGeoJsonPath);

        var enhancedSegments = new List<RiskSegment>();
        var geometryEnhancedCount = 0;
        var qualityMetrics = _roadGeometryService.GetQualityMetrics();

        _logger.LogInformation("Road data quality: {TotalRoads} roads loaded, {PercentNamed:F1}% named",
            qualityMetrics.TotalRoadFeatures, qualityMetrics.PercentageWithNames);

        // Group spatial join results by road segment to find the road geometry for each segment
        var spatialJoinsBySegment = spatialJoinResults
            .Where(r => r.WithinThreshold && r.RoadFeature != null)
            .GroupBy(r => r.RoadSegmentId)
            .ToDictionary(g => g.Key, g => g.First().RoadFeature!);

        foreach (var segment in basicRiskSegments)
        {
            var roadMatch = GetRoadMatchForSegment(segment, spatialJoinsBySegment);

            var enhancedSegment = new RiskSegment
            {
                // Copy all existing properties
                SegmentId = segment.SegmentId,
                StartLatitude = segment.StartLatitude,
                StartLongitude = segment.StartLongitude,
                EndLatitude = segment.EndLatitude,
                EndLongitude = segment.EndLongitude,
                RiskScore = segment.RiskScore,
                RiskLevel = segment.RiskLevel,
                CrashCount = segment.CrashCount,
                SegmentLength = segment.SegmentLength,
                Aadt = segment.Aadt,
                RecentCrashes = segment.RecentCrashes,

                // Enhanced properties from road matching
                RoadGeometry = roadMatch.Coordinates,
                RoadLinearId = roadMatch.LinearId,
                RoadName = roadMatch.Name,
                RoadType = roadMatch.Type,
                GeometryType = roadMatch.GeometryType
            };

            if (roadMatch.GeometryType == "actual_road")
            {
                geometryEnhancedCount++;
                _logger.LogDebug("Enhanced segment {SegmentId} with road geometry from {RoadName} ({LinearId})",
                    segment.SegmentId, roadMatch.Name, roadMatch.LinearId);
            }

            enhancedSegments.Add(enhancedSegment);
        }

        var enhancementRate = basicRiskSegments.Count > 0
            ? (double)geometryEnhancedCount / basicRiskSegments.Count * 100
            : 0;

        _logger.LogInformation("Road geometry enhancement completed: {EnhancedCount}/{TotalCount} segments enhanced ({Rate:F1}%)",
            geometryEnhancedCount, basicRiskSegments.Count, enhancementRate);

        return enhancedSegments;
    }

    public async Task<List<RiskSegment>> GenerateEnhancedRiskSegmentsFromCrashes(
        List<CrashRecord> crashes,
        List<EnhancedSpatialJoinResult> spatialJoinResults,
        Dictionary<string, int> aadtBySegment,
        string roadGeoJsonPath)
    {
        _logger.LogInformation("Generating enhanced risk segments directly from {Count} crashes", crashes.Count);

        // Ensure road geometry service is loaded
        await _roadGeometryService.LoadRoadDataAsync(roadGeoJsonPath);

        // Group crashes by road segment ID from spatial join results
        var crashesByRoadSegment = spatialJoinResults
            .Where(r => r.WithinThreshold && !string.IsNullOrEmpty(r.RoadSegmentId))
            .GroupBy(r => r.RoadSegmentId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Crash).ToList());

        // Get road features by segment ID
        var roadFeaturesBySegment = spatialJoinResults
            .Where(r => r.WithinThreshold && r.RoadFeature != null)
            .GroupBy(r => r.RoadSegmentId)
            .ToDictionary(g => g.Key, g => g.First().RoadFeature!);

        var enhancedSegments = new List<RiskSegment>();
        int segmentCounter = 1;

        foreach (var (segmentId, segmentCrashes) in crashesByRoadSegment)
        {
            if (segmentCrashes.Count == 0) continue;

            // Calculate basic risk metrics
            var riskScore = CalculateSegmentRiskScore(segmentCrashes, aadtBySegment.GetValueOrDefault(segmentId));
            var riskLevel = DetermineRiskLevel(riskScore);

            // Get bounding coordinates from crashes
            var minLat = segmentCrashes.Min(c => c.Latitude);
            var maxLat = segmentCrashes.Max(c => c.Latitude);
            var minLon = segmentCrashes.Min(c => c.Longitude);
            var maxLon = segmentCrashes.Max(c => c.Longitude);

            // Get road match for this segment
            var roadMatch = GetRoadMatchForSegmentId(segmentId, roadFeaturesBySegment, minLat, minLon, maxLat, maxLon, segmentCounter);

            var enhancedSegment = new RiskSegment
            {
                SegmentId = segmentId,
                StartLatitude = minLat,
                StartLongitude = minLon,
                EndLatitude = maxLat,
                EndLongitude = maxLon,
                RiskScore = riskScore,
                RiskLevel = riskLevel,
                CrashCount = segmentCrashes.Count,
                SegmentLength = CalculateSegmentLength(minLat, minLon, maxLat, maxLon),
                Aadt = aadtBySegment.GetValueOrDefault(segmentId),
                RecentCrashes = segmentCrashes.OrderByDescending(c => c.CrashDateTime).Take(10).ToList(),

                // Enhanced properties from road matching
                RoadGeometry = roadMatch.Coordinates,
                RoadLinearId = roadMatch.LinearId,
                RoadName = roadMatch.Name,
                RoadType = roadMatch.Type,
                GeometryType = roadMatch.GeometryType
            };

            enhancedSegments.Add(enhancedSegment);
            segmentCounter++;
        }

        var geometryEnhancedCount = enhancedSegments.Count(s => s.GeometryType == "actual_road");
        var enhancementRate = enhancedSegments.Count > 0
            ? (double)geometryEnhancedCount / enhancedSegments.Count * 100
            : 0;

        _logger.LogInformation("Generated {TotalCount} enhanced risk segments, {EnhancedCount} with actual road geometry ({Rate:F1}%)",
            enhancedSegments.Count, geometryEnhancedCount, enhancementRate);

        return enhancedSegments.OrderByDescending(s => s.RiskScore).ToList();
    }

    private RoadMatchInfo GetRoadMatchForSegment(RiskSegment segment, Dictionary<string, RoadFeature> spatialJoinsBySegment)
    {
        if (spatialJoinsBySegment.TryGetValue(segment.SegmentId, out var roadFeature))
        {
            return new RoadMatchInfo
            {
                Coordinates = roadFeature.Coordinates,
                LinearId = roadFeature.LinearId,
                Name = roadFeature.FullName,
                Type = roadFeature.RoadType,
                GeometryType = "actual_road"
            };
        }

        // Fallback to straight line geometry
        return new RoadMatchInfo
        {
            Coordinates = new List<double[]>
            {
                new[] { (double)segment.StartLongitude, (double)segment.StartLatitude },
                new[] { (double)segment.EndLongitude, (double)segment.EndLatitude }
            },
            LinearId = segment.SegmentId,
            Name = $"Segment_{segment.SegmentId}",
            Type = "U",
            GeometryType = "straight_line"
        };
    }

    private RoadMatchInfo GetRoadMatchForSegmentId(
        string segmentId,
        Dictionary<string, RoadFeature> roadFeaturesBySegment,
        decimal minLat, decimal minLon, decimal maxLat, decimal maxLon,
        int segmentCounter)
    {
        if (roadFeaturesBySegment.TryGetValue(segmentId, out var roadFeature))
        {
            return new RoadMatchInfo
            {
                Coordinates = roadFeature.Coordinates,
                LinearId = roadFeature.LinearId,
                Name = roadFeature.FullName,
                Type = roadFeature.RoadType,
                GeometryType = "actual_road"
            };
        }

        // Fallback to straight line geometry
        return new RoadMatchInfo
        {
            Coordinates = new List<double[]>
            {
                new[] { (double)minLon, (double)minLat },
                new[] { (double)maxLon, (double)maxLat }
            },
            LinearId = segmentId,
            Name = $"Road_Segment_{segmentCounter}",
            Type = "U",
            GeometryType = "straight_line"
        };
    }

    private decimal CalculateSegmentRiskScore(List<CrashRecord> crashes, int? aadt)
    {
        if (!crashes.Any()) return 0;

        // Basic risk score calculation based on crash severity and frequency
        var severityWeights = new Dictionary<KabcoSeverity, decimal>
        {
            { KabcoSeverity.K_Fatal, 1.0m },
            { KabcoSeverity.A_IncapacitatingInjury, 0.8m },
            { KabcoSeverity.B_NonIncapacitatingInjury, 0.6m },
            { KabcoSeverity.C_PossibleInjury, 0.4m },
            { KabcoSeverity.O_NoInjury, 0.2m },
            { KabcoSeverity.Unknown, 0.1m }
        };

        var severityScore = crashes.Average(c => severityWeights.GetValueOrDefault(c.Severity, 0.1m));
        var frequencyScore = Math.Min((decimal)crashes.Count / 10m, 1.0m);

        // Factor in traffic volume if available
        var trafficFactor = 1.0m;
        if (aadt.HasValue && aadt > 0)
        {
            // Normalize AADT impact (higher traffic = higher expected crashes, so lower relative risk)
            trafficFactor = 1.0m / (1.0m + ((decimal)aadt.Value / 10000m));
        }

        return (severityScore * 0.6m + frequencyScore * 0.4m) * trafficFactor;
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

    private decimal CalculateSegmentLength(decimal lat1, decimal lon1, decimal lat2, decimal lon2)
    {
        // Simple Euclidean distance in degrees (rough approximation)
        var deltaLat = lat2 - lat1;
        var deltaLon = lon2 - lon1;
        var distance = (decimal)Math.Sqrt((double)(deltaLat * deltaLat + deltaLon * deltaLon));

        // Convert degrees to miles (very rough approximation: 1 degree ≈ 69 miles)
        return distance * 69m;
    }
}

public class RoadMatchInfo
{
    public List<double[]> Coordinates { get; set; } = new();
    public string LinearId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string GeometryType { get; set; } = "";
}