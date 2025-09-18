using Microsoft.Extensions.Logging;
using TCDS.Importer.Models;

namespace TCDS.Importer.Services;

public class AadtValidationService
{
    private readonly ILogger<AadtValidationService> _logger;

    public AadtValidationService(ILogger<AadtValidationService> logger)
    {
        _logger = logger;
    }

    public ValidationResult ValidateAadt(int aadt, RoadHierarchy roadType, List<int> similarRoadAadts)
    {
        var warnings = new List<string>();

        // Relative validation against peer roads
        if (similarRoadAadts.Any())
        {
            var sortedAadts = similarRoadAadts.OrderBy(x => x).ToList();
            var median = sortedAadts[sortedAadts.Count / 2];
            var percentile90Index = (int)(sortedAadts.Count * 0.9);
            var percentile10Index = (int)(sortedAadts.Count * 0.1);

            var percentile90 = sortedAadts[Math.Min(percentile90Index, sortedAadts.Count - 1)];
            var percentile10 = sortedAadts[Math.Max(percentile10Index, 0)];

            // Warn if significantly different from peers
            if (aadt < percentile10 * 0.3)
            {
                warnings.Add($"AADT unusually low for {roadType} ({aadt} is 30% below 10th percentile of {percentile10})");
                _logger.LogWarning("Low AADT detected: {Aadt} for {RoadType}, 10th percentile: {Percentile10}",
                    aadt, roadType, percentile10);
            }

            if (aadt > percentile90 * 3.0)
            {
                warnings.Add($"AADT unusually high for {roadType} ({aadt} is 3x above 90th percentile of {percentile90})");
                _logger.LogWarning("High AADT detected: {Aadt} for {RoadType}, 90th percentile: {Percentile90}",
                    aadt, roadType, percentile90);
            }

            _logger.LogDebug("AADT validation for {RoadType}: {Aadt} (median: {Median}, 10th: {P10}, 90th: {P90})",
                roadType, aadt, median, percentile10, percentile90);
        }

        // Hierarchy consistency warnings
        var hierarchyWarnings = ValidateHierarchyConsistency(aadt, roadType);
        warnings.AddRange(hierarchyWarnings);

        return new ValidationResult(warnings);
    }

    public ValidationResult ValidateAadtForLocation(EnhancedTrafficLocation location,
        IEnumerable<EnhancedTrafficLocation> allLocations)
    {
        if (!location.Aadt.HasValue)
            return new ValidationResult();

        // Get similar road types for comparison
        var similarRoads = allLocations
            .Where(l => l.TargetRoadHierarchy == location.TargetRoadHierarchy &&
                       l.Aadt.HasValue &&
                       l.LocationId != location.LocationId)
            .Select(l => l.Aadt!.Value)
            .ToList();

        return ValidateAadt(location.Aadt.Value, location.TargetRoadHierarchy, similarRoads);
    }

    private List<string> ValidateHierarchyConsistency(int aadt, RoadHierarchy roadType)
    {
        var warnings = new List<string>();

        // Logical hierarchy warnings (flexible thresholds)
        switch (roadType)
        {
            case RoadHierarchy.Interstate when aadt < 10000:
                warnings.Add("Interstate with unexpectedly low traffic volume");
                _logger.LogWarning("Low interstate AADT: {Aadt}", aadt);
                break;

            case RoadHierarchy.Ramp when aadt > 50000:
                warnings.Add("Ramp with unexpectedly high traffic volume");
                _logger.LogWarning("High ramp AADT: {Aadt}", aadt);
                break;

            case RoadHierarchy.LocalRoad when aadt > 20000:
                warnings.Add("Local road with unexpectedly high traffic volume");
                _logger.LogWarning("High local road AADT: {Aadt}", aadt);
                break;

            case RoadHierarchy.USHighway when aadt < 1000:
                warnings.Add("US Highway with unexpectedly low traffic volume");
                break;
        }

        return warnings;
    }

    public QualityReport GenerateQualityReport(List<TrafficMatchResult> matchResults)
    {
        _logger.LogInformation("Generating traffic data quality report for {Count} matches", matchResults.Count);

        var report = new QualityReport
        {
            TotalMatches = matchResults.Count,
            GeneratedAt = DateTime.UtcNow
        };

        // Find interstate anomalies
        report.InterstateAnomalies = FindInterstateAnomalies(matchResults);

        // Find ramp contamination
        report.RampToMainlineContamination = FindRampContamination(matchResults);

        // Find AADT outliers
        report.AadtOutliers = FindAadtOutliers(matchResults);

        _logger.LogInformation("Quality report generated: {InterstateAnomalies} interstate anomalies, " +
                              "{RampContamination} ramp contaminations, {Outliers} outliers",
            report.InterstateAnomalies.Count, report.RampToMainlineContamination.Count,
            report.AadtOutliers.Count);

        return report;
    }

    private List<QualityAnomaly> FindInterstateAnomalies(List<TrafficMatchResult> matchResults)
    {
        var anomalies = new List<QualityAnomaly>();

        var interstateMatches = matchResults
            .Where(r => r.IsMatch && r.MatchedLocation != null &&
                       r.MatchedLocation.TargetRoadHierarchy == RoadHierarchy.Interstate)
            .ToList();

        foreach (var match in interstateMatches)
        {
            if (match.MatchedLocation!.Aadt < 15000)
            {
                anomalies.Add(new QualityAnomaly
                {
                    RoadId = match.MatchedLocation.LocationId,
                    RoadName = match.MatchedLocation.RouteDesignation ?? "Unknown Interstate",
                    Description = $"Interstate with very low AADT: {match.MatchedLocation.Aadt}",
                    Severity = "Critical",
                    ActualAadt = match.MatchedLocation.Aadt,
                    ExpectedAadt = 40000,
                    LocationId = match.MatchedLocation.LocationId
                });
            }
        }

        return anomalies;
    }

    private List<QualityAnomaly> FindRampContamination(List<TrafficMatchResult> matchResults)
    {
        var contamination = new List<QualityAnomaly>();

        foreach (var match in matchResults.Where(r => r.IsMatch && r.MatchedLocation != null))
        {
            // Check if ramp data is being used for mainline roads
            if (match.MatchedLocation!.LocationType == TrafficLocationType.Ramp &&
                match.MatchedLocation.TargetRoadHierarchy != RoadHierarchy.Ramp)
            {
                contamination.Add(new QualityAnomaly
                {
                    RoadId = match.MatchedLocation.LocationId,
                    RoadName = match.MatchedLocation.RouteDesignation ?? "Unknown Road",
                    Description = $"Ramp traffic data applied to {match.MatchedLocation.TargetRoadHierarchy} road",
                    Severity = "High",
                    ActualAadt = match.MatchedLocation.Aadt,
                    LocationId = match.MatchedLocation.LocationId
                });
            }
        }

        return contamination;
    }

    private List<QualityAnomaly> FindAadtOutliers(List<TrafficMatchResult> matchResults)
    {
        var outliers = new List<QualityAnomaly>();

        var roadTypeGroups = matchResults
            .Where(r => r.IsMatch && r.MatchedLocation?.Aadt.HasValue == true)
            .GroupBy(r => r.MatchedLocation!.TargetRoadHierarchy);

        foreach (var group in roadTypeGroups)
        {
            var aadtValues = group.Select(g => g.MatchedLocation!.Aadt!.Value).OrderBy(x => x).ToList();

            if (aadtValues.Count < 3) continue; // Need at least 3 values for meaningful outlier detection

            var q1Index = aadtValues.Count / 4;
            var q3Index = (3 * aadtValues.Count) / 4;
            var q1 = aadtValues[q1Index];
            var q3 = aadtValues[q3Index];
            var iqr = q3 - q1;
            var lowerBound = q1 - (1.5 * iqr);
            var upperBound = q3 + (1.5 * iqr);

            foreach (var match in group)
            {
                var aadt = match.MatchedLocation!.Aadt!.Value;
                if (aadt < lowerBound || aadt > upperBound)
                {
                    outliers.Add(new QualityAnomaly
                    {
                        RoadId = match.MatchedLocation.LocationId,
                        RoadName = match.MatchedLocation.RouteDesignation ?? $"Unknown {group.Key}",
                        Description = $"AADT {aadt} is statistical outlier for {group.Key} roads (expected {q1}-{q3})",
                        Severity = "Medium",
                        ActualAadt = aadt,
                        ExpectedAadt = (q1 + q3) / 2,
                        LocationId = match.MatchedLocation.LocationId
                    });
                }
            }
        }

        return outliers;
    }
}