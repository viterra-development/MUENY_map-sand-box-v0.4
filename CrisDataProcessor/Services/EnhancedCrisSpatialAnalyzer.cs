using MapSandBox.Models;
using Microsoft.Extensions.Logging;

namespace CrisDataProcessor.Services;

public class EnhancedCrisSpatialAnalyzer
{
    private readonly ILogger<EnhancedCrisSpatialAnalyzer> _logger;
    private readonly RoadGeometryService _roadGeometryService;
    private readonly double _proximityThresholdMeters;

    public EnhancedCrisSpatialAnalyzer(
        ILogger<EnhancedCrisSpatialAnalyzer> logger,
        RoadGeometryService roadGeometryService,
        double proximityThresholdMeters = 50.0)
    {
        _logger = logger;
        _roadGeometryService = roadGeometryService;
        _proximityThresholdMeters = proximityThresholdMeters;
    }

    public async Task<List<EnhancedSpatialJoinResult>> SpatialJoinCrashesToRoadsAsync(
        List<CrashRecord> crashes,
        string roadGeoJsonPath)
    {
        _logger.LogInformation("Performing enhanced spatial join of {CrashCount} crashes to road network at {RoadPath}",
            crashes.Count, roadGeoJsonPath);

        // Load road data into our enhanced geometry service
        await _roadGeometryService.LoadRoadDataAsync(roadGeoJsonPath);
        var qualityMetrics = _roadGeometryService.GetQualityMetrics();

        _logger.LogInformation("Road geometry loaded: {TotalRoads} roads, {PercentNamed:F1}% named",
            qualityMetrics.TotalRoadFeatures, qualityMetrics.PercentageWithNames);

        var results = new List<EnhancedSpatialJoinResult>();
        var matchedCount = 0;

        foreach (var crash in crashes)
        {
            var roadMatch = _roadGeometryService.FindBestRoadMatch(
                (double)crash.Latitude,
                (double)crash.Longitude,
                _proximityThresholdMeters);

            if (roadMatch != null)
            {
                matchedCount++;

                results.Add(new EnhancedSpatialJoinResult
                {
                    Crash = crash,
                    RoadSegmentId = roadMatch.Road.LinearId,
                    DistanceToRoad = (decimal)roadMatch.DistanceMeters,
                    WithinThreshold = roadMatch.IsWithinTolerance,
                    RoadFeature = roadMatch.Road // Include the full road feature
                });
            }
            else
            {
                // Create unmatched result for tracking
                results.Add(new EnhancedSpatialJoinResult
                {
                    Crash = crash,
                    RoadSegmentId = "",
                    DistanceToRoad = decimal.MaxValue,
                    WithinThreshold = false,
                    RoadFeature = null
                });
            }
        }

        _logger.LogInformation("Enhanced spatial join completed: {MatchedCount}/{TotalCrashes} crashes matched to road segments within {Threshold}m",
            matchedCount, crashes.Count, _proximityThresholdMeters);

        return results;
    }

    public async Task<List<EnhancedSpatialJoinResult>> SpatialJoinCrashesToRoadsMultiAsync(
        List<CrashRecord> crashes,
        string roadGeoJsonPath)
    {
        _logger.LogInformation("Performing MULTI-ASSOCIATION spatial join of {CrashCount} crashes to road network at {RoadPath}",
            crashes.Count, roadGeoJsonPath);

        // Load road data into our enhanced geometry service
        await _roadGeometryService.LoadRoadDataAsync(roadGeoJsonPath);
        var qualityMetrics = _roadGeometryService.GetQualityMetrics();

        _logger.LogInformation("Road geometry loaded: {TotalRoads} roads, {PercentNamed:F1}% named",
            qualityMetrics.TotalRoadFeatures, qualityMetrics.PercentageWithNames);

        var results = new List<EnhancedSpatialJoinResult>();
        var totalMatches = 0;
        var multiAssociationCount = 0;

        foreach (var crash in crashes)
        {
            // Get ALL eligible road matches (not just the best one)
            var roadMatches = _roadGeometryService.FindAllEligibleRoadMatches(
                (double)crash.Latitude,
                (double)crash.Longitude,
                _proximityThresholdMeters);

            if (roadMatches.Any())
            {
                totalMatches += roadMatches.Count;
                var isMultiAssociation = roadMatches.Count > 1;

                if (isMultiAssociation)
                {
                    multiAssociationCount++;
                    _logger.LogDebug("Multi-association: Crash {CrashId} matched to {Count} road segments",
                        crash.CrashId, roadMatches.Count);
                }

                // Create separate result for each matching road segment
                for (int i = 0; i < roadMatches.Count; i++)
                {
                    var roadMatch = roadMatches[i];
                    results.Add(new EnhancedSpatialJoinResult
                    {
                        Crash = crash,
                        RoadSegmentId = roadMatch.Road.LinearId,
                        DistanceToRoad = (decimal)roadMatch.DistanceMeters,
                        WithinThreshold = roadMatch.IsWithinTolerance,
                        RoadFeature = roadMatch.Road,
                        IsMultiAssociation = isMultiAssociation,
                        AssociationRank = i + 1,
                        OverlapReason = isMultiAssociation ? "overlapping_geometry" : "single_match"
                    });
                }
            }
            else
            {
                // Create unmatched result for tracking
                results.Add(new EnhancedSpatialJoinResult
                {
                    Crash = crash,
                    RoadSegmentId = "",
                    DistanceToRoad = decimal.MaxValue,
                    WithinThreshold = false,
                    RoadFeature = null,
                    IsMultiAssociation = false,
                    AssociationRank = 0,
                    OverlapReason = "no_match"
                });
            }
        }

        _logger.LogInformation("Multi-association spatial join completed:");
        _logger.LogInformation("  - Total crashes: {TotalCrashes}", crashes.Count);
        _logger.LogInformation("  - Total road associations: {TotalMatches}", totalMatches);
        _logger.LogInformation("  - Crashes with multiple associations: {MultiCount}", multiAssociationCount);
        _logger.LogInformation("  - Average associations per crash: {AvgAssociations:F2}",
            crashes.Count > 0 ? (double)totalMatches / crashes.Count : 0);

        return results;
    }

    public Dictionary<string, List<CrashRecord>> GroupCrashesByRoadSegment(List<EnhancedSpatialJoinResult> spatialJoinResults)
    {
        var crashesBySegment = spatialJoinResults
            .Where(r => r.WithinThreshold)
            .GroupBy(r => r.RoadSegmentId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Crash).ToList());

        _logger.LogInformation("Grouped crashes into {SegmentCount} road segments", crashesBySegment.Count);
        return crashesBySegment;
    }

    public Dictionary<string, SegmentCrashSummary> GroupCrashesByRoadSegmentEnhanced(List<EnhancedSpatialJoinResult> spatialJoinResults)
    {
        var crashesBySegment = spatialJoinResults
            .Where(r => r.WithinThreshold)
            .GroupBy(r => r.RoadSegmentId)
            .ToDictionary(g => g.Key, g => new SegmentCrashSummary
            {
                SegmentId = g.Key,
                CrashCount = g.Count(),
                UniqueCrashCount = g.Select(r => r.Crash.CrashId).Distinct().Count(),
                Crashes = g.Select(r => r.Crash).ToList(),
                MultiAssociationCount = g.Count(r => r.IsMultiAssociation),
                RoadType = g.First().RoadFeature?.RoadType ?? "U",
                RoadName = g.First().RoadFeature?.FullName ?? "Unnamed Road",
                MtfccCode = g.First().RoadFeature?.MtfccCode ?? "",
                HasMultiAssociations = g.Any(r => r.IsMultiAssociation)
            });

        var totalSegments = crashesBySegment.Count;
        var segmentsWithMultiAssoc = crashesBySegment.Values.Count(s => s.HasMultiAssociations);

        _logger.LogInformation("Enhanced grouping completed:");
        _logger.LogInformation("  - Road segments with crashes: {TotalSegments}", totalSegments);
        _logger.LogInformation("  - Segments with multi-associations: {MultiSegments}", segmentsWithMultiAssoc);

        return crashesBySegment;
    }
}

// Enhanced spatial join result that includes road geometry information
public class EnhancedSpatialJoinResult
{
    public CrashRecord Crash { get; set; } = null!;
    public string RoadSegmentId { get; set; } = "";
    public decimal DistanceToRoad { get; set; }
    public bool WithinThreshold { get; set; }
    public RoadFeature? RoadFeature { get; set; } // Enhanced: includes full road geometry

    // Multi-association fields
    public bool IsMultiAssociation { get; set; }
    public int AssociationRank { get; set; } // 1 = closest, 2 = second closest, etc.
    public string OverlapReason { get; set; } = ""; // "overlapping_geometry", "single_match", etc.
}

public class SegmentCrashSummary
{
    public string SegmentId { get; set; } = "";
    public int CrashCount { get; set; }
    public int UniqueCrashCount { get; set; } // Number of unique crashes (in case of duplicates)
    public List<CrashRecord> Crashes { get; set; } = new();
    public int MultiAssociationCount { get; set; } // Number of crashes that were multi-associated
    public string RoadType { get; set; } = "";
    public string RoadName { get; set; } = "";
    public string MtfccCode { get; set; } = "";
    public bool HasMultiAssociations { get; set; }
}