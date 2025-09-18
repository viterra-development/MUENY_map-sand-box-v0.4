using Microsoft.Extensions.Logging;
using TCDS.Importer.Models;

namespace TCDS.Importer.Services;

public class DataQualityMonitor
{
    private readonly ILogger<DataQualityMonitor> _logger;

    public DataQualityMonitor(ILogger<DataQualityMonitor> logger)
    {
        _logger = logger;
    }

    public QualityMonitoringReport GenerateComprehensiveReport(
        EnhancedGeoJsonFeatureCollection featureCollection,
        List<TrafficMatchResult> matchResults)
    {
        _logger.LogInformation("🔍 Generating comprehensive data quality report");

        var report = new QualityMonitoringReport
        {
            GeneratedAt = DateTime.UtcNow,
            TotalFeatures = featureCollection.Features.Count
        };

        // Analyze I-20 specifically (the original issue)
        AnalyzeI20Data(featureCollection, report);

        // Check for ramp contamination
        CheckRampContamination(featureCollection, report);

        // Validate interstate highways
        ValidateInterstateHighways(featureCollection, report);

        // Check matching quality
        AnalyzeMatchingQuality(matchResults, report);

        // Generate alerts
        GenerateAlerts(report);

        LogQualityReport(report);

        return report;
    }

    private void AnalyzeI20Data(EnhancedGeoJsonFeatureCollection featureCollection, QualityMonitoringReport report)
    {
        var i20Roads = featureCollection.Features
            .Where(f => f.Properties.FullName?.Contains("I- 20") == true ||
                       f.Properties.FullName?.Contains("I-20") == true ||
                       f.Properties.Classification?.RouteDesignation == "I-20")
            .ToList();

        if (!i20Roads.Any())
        {
            report.Alerts.Add(new QualityAlert
            {
                Severity = AlertSeverity.Medium,
                Category = "MissingData",
                Message = "No I-20 road segments found in dataset",
                Details = "Expected to find Interstate 20 segments in Parker County data"
            });
            return;
        }

        var i20WithTraffic = i20Roads.Where(r => r.Properties.Traffic?.Aadt != null).ToList();
        var i20Analysis = new I20Analysis
        {
            TotalSegments = i20Roads.Count,
            SegmentsWithTraffic = i20WithTraffic.Count,
            AadtValues = i20WithTraffic.Select(r => r.Properties.Traffic!.Aadt!.Value).ToList()
        };

        // Check for the original issue - low AADT values
        foreach (var road in i20WithTraffic)
        {
            var aadt = road.Properties.Traffic!.Aadt!.Value;
            if (aadt < 20000) // Interstate should have much higher traffic
            {
                report.Alerts.Add(new QualityAlert
                {
                    Severity = AlertSeverity.Critical,
                    Category = "I20LowTraffic",
                    Message = $"I-20 segment has unusually low AADT: {aadt:N0}",
                    Details = $"I-20 typically has 40,000-80,000+ vehicles/day. Segment AADT: {aadt:N0}",
                    LocationId = road.Properties.Traffic.LocationId,
                    SegmentId = road.Properties.LinearId
                });
            }

            // Check if ramp data is being used
            if (road.Properties.TrafficMatch?.SourceType?.Contains("Ramp") == true)
            {
                report.Alerts.Add(new QualityAlert
                {
                    Severity = AlertSeverity.Critical,
                    Category = "RampContamination",
                    Message = "I-20 mainline using ramp traffic data",
                    Details = $"Interstate mainline should not use ramp AADT data. Current AADT: {aadt:N0}",
                    LocationId = road.Properties.Traffic.LocationId,
                    SegmentId = road.Properties.LinearId
                });
            }
        }

        report.I20Analysis = i20Analysis;
        _logger.LogInformation("🛣️  I-20 Analysis: {TotalSegments} segments, {WithTraffic} with traffic data",
            i20Analysis.TotalSegments, i20Analysis.SegmentsWithTraffic);
    }

    private void CheckRampContamination(EnhancedGeoJsonFeatureCollection featureCollection, QualityMonitoringReport report)
    {
        var mainlineRoadsWithRampData = featureCollection.Features
            .Where(f => f.Properties.Classification?.IsMainlineRoad == true &&
                       f.Properties.TrafficMatch?.SourceType?.Contains("Ramp") == true)
            .ToList();

        foreach (var road in mainlineRoadsWithRampData)
        {
            report.Alerts.Add(new QualityAlert
            {
                Severity = AlertSeverity.High,
                Category = "RampContamination",
                Message = $"Mainline road using ramp traffic data: {road.Properties.FullName}",
                Details = $"Road hierarchy: {road.Properties.Classification?.Hierarchy}, Source: {road.Properties.TrafficMatch?.SourceType}",
                LocationId = road.Properties.Traffic?.LocationId,
                SegmentId = road.Properties.LinearId
            });
        }

        report.RampContaminationCount = mainlineRoadsWithRampData.Count;
    }

    private void ValidateInterstateHighways(EnhancedGeoJsonFeatureCollection featureCollection, QualityMonitoringReport report)
    {
        var interstateRoads = featureCollection.Features
            .Where(f => f.Properties.Classification?.Hierarchy == RoadHierarchy.Interstate)
            .ToList();

        var interstatesWithTraffic = interstateRoads
            .Where(r => r.Properties.Traffic?.Aadt != null)
            .ToList();

        // Check for low traffic interstates
        var lowTrafficInterstates = interstatesWithTraffic
            .Where(r => r.Properties.Traffic!.Aadt < 25000)
            .ToList();

        foreach (var interstate in lowTrafficInterstates)
        {
            report.Alerts.Add(new QualityAlert
            {
                Severity = AlertSeverity.High,
                Category = "LowInterstateTraffic",
                Message = $"Interstate with low AADT: {interstate.Properties.FullName}",
                Details = $"AADT: {interstate.Properties.Traffic!.Aadt:N0} (expected 25,000+)",
                LocationId = interstate.Properties.Traffic.LocationId,
                SegmentId = interstate.Properties.LinearId
            });
        }

        // Check for missing traffic data on interstates
        var interstatesWithoutTraffic = interstateRoads
            .Where(r => r.Properties.Traffic?.Aadt == null)
            .ToList();

        if (interstatesWithoutTraffic.Any())
        {
            report.Alerts.Add(new QualityAlert
            {
                Severity = AlertSeverity.Medium,
                Category = "MissingInterstateData",
                Message = $"{interstatesWithoutTraffic.Count} interstate segments missing traffic data",
                Details = string.Join(", ", interstatesWithoutTraffic.Select(r => r.Properties.FullName).Distinct().Take(5))
            });
        }

        report.InterstateAnalysis = new InterstateAnalysis
        {
            TotalInterstates = interstateRoads.Count,
            WithTrafficData = interstatesWithTraffic.Count,
            LowTrafficCount = lowTrafficInterstates.Count,
            MissingDataCount = interstatesWithoutTraffic.Count
        };
    }

    private void AnalyzeMatchingQuality(List<TrafficMatchResult> matchResults, QualityMonitoringReport report)
    {
        var totalMatches = matchResults.Count(r => r.IsMatch);
        var matchingRate = totalMatches / (double)matchResults.Count * 100;

        // Check for poor matching rates
        if (matchingRate < 50)
        {
            report.Alerts.Add(new QualityAlert
            {
                Severity = AlertSeverity.High,
                Category = "LowMatchingRate",
                Message = $"Low traffic data matching rate: {matchingRate:F1}%",
                Details = $"Only {totalMatches} out of {matchResults.Count} roads have traffic data"
            });
        }

        // Check for excessive distance warnings
        var longDistanceMatches = matchResults
            .Where(r => r.IsMatch && r.Distance > 0.002) // > 200m
            .Count();

        if (longDistanceMatches > totalMatches * 0.1) // More than 10% are long distance
        {
            report.Alerts.Add(new QualityAlert
            {
                Severity = AlertSeverity.Medium,
                Category = "LongDistanceMatches",
                Message = $"{longDistanceMatches} matches use distant traffic data",
                Details = $"Traffic data located >200m from road centerline"
            });
        }

        report.MatchingQuality = new MatchingQuality
        {
            TotalRoads = matchResults.Count,
            MatchedRoads = totalMatches,
            MatchingRate = matchingRate,
            LongDistanceMatches = longDistanceMatches
        };
    }

    private void GenerateAlerts(QualityMonitoringReport report)
    {
        // Prioritize alerts
        var criticalAlerts = report.Alerts.Where(a => a.Severity == AlertSeverity.Critical).ToList();
        var highAlerts = report.Alerts.Where(a => a.Severity == AlertSeverity.High).ToList();

        // Create summary alerts for dashboard
        if (criticalAlerts.Any())
        {
            report.SummaryAlerts.Add($"🚨 {criticalAlerts.Count} CRITICAL issues require immediate attention");
        }

        if (highAlerts.Any())
        {
            report.SummaryAlerts.Add($"⚠️  {highAlerts.Count} HIGH priority issues need resolution");
        }

        if (report.I20Analysis?.AadtValues.Any(aadt => aadt < 20000) == true)
        {
            report.SummaryAlerts.Add("🛣️  I-20 traffic data validation failed - investigate ramp contamination");
        }

        if (report.RampContaminationCount > 0)
        {
            report.SummaryAlerts.Add($"🚧 {report.RampContaminationCount} roads incorrectly using ramp traffic data");
        }
    }

    private void LogQualityReport(QualityMonitoringReport report)
    {
        _logger.LogInformation("📊 Data Quality Monitoring Report Summary:");

        // Log critical issues first
        var criticalCount = report.Alerts.Count(a => a.Severity == AlertSeverity.Critical);
        var highCount = report.Alerts.Count(a => a.Severity == AlertSeverity.High);
        var mediumCount = report.Alerts.Count(a => a.Severity == AlertSeverity.Medium);

        if (criticalCount > 0)
            _logger.LogError("   🚨 CRITICAL Issues: {Count}", criticalCount);
        if (highCount > 0)
            _logger.LogWarning("   ⚠️  HIGH Priority Issues: {Count}", highCount);
        if (mediumCount > 0)
            _logger.LogInformation("   ℹ️  Medium Priority Issues: {Count}", mediumCount);

        // Log I-20 specific findings
        if (report.I20Analysis != null)
        {
            _logger.LogInformation("   🛣️  I-20 Status: {WithTraffic}/{Total} segments have traffic data",
                report.I20Analysis.SegmentsWithTraffic, report.I20Analysis.TotalSegments);

            if (report.I20Analysis.AadtValues.Any())
            {
                var avgI20Aadt = report.I20Analysis.AadtValues.Average();
                var minI20Aadt = report.I20Analysis.AadtValues.Min();
                var maxI20Aadt = report.I20Analysis.AadtValues.Max();

                _logger.LogInformation("   🛣️  I-20 AADT Range: {Min:N0} - {Max:N0} (avg: {Avg:N0})",
                    minI20Aadt, maxI20Aadt, avgI20Aadt);

                if (minI20Aadt < 20000)
                {
                    _logger.LogError("   🚨 I-20 VALIDATION FAILED: Minimum AADT {Min:N0} is too low for interstate", minI20Aadt);
                }
                else
                {
                    _logger.LogInformation("   ✅ I-20 VALIDATION PASSED: AADT values within expected range");
                }
            }
        }

        // Log summary alerts
        foreach (var alert in report.SummaryAlerts)
        {
            _logger.LogWarning("   {Alert}", alert);
        }

        if (!report.Alerts.Any())
        {
            _logger.LogInformation("   ✅ No data quality issues detected");
        }
    }
}

// Additional models for quality monitoring
public class QualityMonitoringReport
{
    public DateTime GeneratedAt { get; set; }
    public int TotalFeatures { get; set; }
    public List<QualityAlert> Alerts { get; set; } = new();
    public List<string> SummaryAlerts { get; set; } = new();
    public I20Analysis? I20Analysis { get; set; }
    public InterstateAnalysis? InterstateAnalysis { get; set; }
    public MatchingQuality? MatchingQuality { get; set; }
    public int RampContaminationCount { get; set; }
}

public class QualityAlert
{
    public AlertSeverity Severity { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string? LocationId { get; set; }
    public string? SegmentId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class I20Analysis
{
    public int TotalSegments { get; set; }
    public int SegmentsWithTraffic { get; set; }
    public List<int> AadtValues { get; set; } = new();
}

public class InterstateAnalysis
{
    public int TotalInterstates { get; set; }
    public int WithTrafficData { get; set; }
    public int LowTrafficCount { get; set; }
    public int MissingDataCount { get; set; }
}

public class MatchingQuality
{
    public int TotalRoads { get; set; }
    public int MatchedRoads { get; set; }
    public double MatchingRate { get; set; }
    public int LongDistanceMatches { get; set; }
}

public enum AlertSeverity
{
    Low,
    Medium,
    High,
    Critical
}