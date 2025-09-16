using MapSandBox.Models;
using Microsoft.Extensions.Logging;

namespace CrisDataProcessor.Services;

public class EnvironmentalAnalyzer
{
    private readonly ILogger<EnvironmentalAnalyzer> _logger;
    private readonly CrisModelConfiguration _config;

    public EnvironmentalAnalyzer(ILogger<EnvironmentalAnalyzer> logger, CrisModelConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    public EnvironmentalRiskFactors AnalyzeEnvironmentalRisk(
        List<CrashRecord> crashes,
        RiskSegment segment)
    {
        _logger.LogDebug("Analyzing environmental risk for segment {SegmentId} with {CrashCount} crashes",
            segment.SegmentId, crashes.Count);

        var factors = new EnvironmentalRiskFactors
        {
            SlopePercentage = segment.SlopePercentage,
            WetSurfaceCrashes = CountWetSurfaceCrashes(crashes),
            IcySurfaceCrashes = CountIcySurfaceCrashes(crashes),
            FogRelatedCrashes = CountFogRelatedCrashes(crashes),
            HydroplaningIncidents = CountHydroplaningIncidents(crashes),
        };

        // Determine if segment has drainage issues
        factors.HasDrainageIssues = factors.SlopePercentage > _config.Thresholds.SlopeThreshold ||
                                   factors.HydroplaningIncidents > 0 ||
                                   (crashes.Any() && factors.WetSurfaceCrashes > crashes.Count * 0.3m);

        _logger.LogDebug("Environmental factors for {SegmentId}: Wet={Wet}, Icy={Icy}, Fog={Fog}, Hydroplaning={Hydro}, DrainageIssues={Drainage}",
            segment.SegmentId, factors.WetSurfaceCrashes, factors.IcySurfaceCrashes,
            factors.FogRelatedCrashes, factors.HydroplaningIncidents, factors.HasDrainageIssues);

        return factors;
    }

    public void EnhanceSegmentsWithEnvironmentalAnalysis(
        List<RiskSegment> segments,
        Dictionary<string, List<CrashRecord>> crashesBySegment)
    {
        _logger.LogInformation("Enhancing {SegmentCount} segments with environmental analysis", segments.Count);

        if (!_config.ProcessingOptions.EnableEnvironmentalAnalysis)
        {
            _logger.LogInformation("Environmental analysis disabled in configuration, skipping...");
            return;
        }

        var processedCount = 0;
        foreach (var segment in segments)
        {
            try
            {
                var segmentCrashes = crashesBySegment.GetValueOrDefault(segment.SegmentId, new List<CrashRecord>());
                segment.EnvironmentalFactors = AnalyzeEnvironmentalRisk(segmentCrashes, segment);
                processedCount++;

                if (processedCount % 100 == 0)
                {
                    _logger.LogDebug("Processed environmental analysis for {ProcessedCount}/{TotalCount} segments",
                        processedCount, segments.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to analyze environmental risk for segment {SegmentId}", segment.SegmentId);
                segment.EnvironmentalFactors = new EnvironmentalRiskFactors(); // Default empty factors
            }
        }

        var drainageIssueCount = segments.Count(s => s.EnvironmentalFactors.HasDrainageIssues);
        var highWeatherRiskCount = segments.Count(s => s.EnvironmentalFactors.WetSurfaceCrashes +
                                                       s.EnvironmentalFactors.IcySurfaceCrashes > 0);

        _logger.LogInformation("Enhanced {ProcessedCount}/{TotalCount} segments with environmental analysis. " +
                             "Drainage issues: {DrainageCount}, Weather-related crashes: {WeatherCount}",
            processedCount, segments.Count, drainageIssueCount, highWeatherRiskCount);
    }

    private int CountWetSurfaceCrashes(List<CrashRecord> crashes)
    {
        return crashes.Count(c => IsWetSurfaceCrash(c));
    }

    private int CountIcySurfaceCrashes(List<CrashRecord> crashes)
    {
        return crashes.Count(c => IsIcySurfaceCrash(c));
    }

    private int CountFogRelatedCrashes(List<CrashRecord> crashes)
    {
        return crashes.Count(c => IsFogRelatedCrash(c));
    }

    private int CountHydroplaningIncidents(List<CrashRecord> crashes)
    {
        return crashes.Count(c => IsHydroplaningIncident(c));
    }

    private bool IsWetSurfaceCrash(CrashRecord crash)
    {
        return ContainsAnyIgnoreCase(crash.RoadwayCondition, "Wet", "Standing Water", "Puddles") ||
               ContainsAnyIgnoreCase(crash.WeatherCondition, "Rain", "Drizzle", "Shower", "Downpour") ||
               (ContainsAnyIgnoreCase(crash.WeatherCondition, "Rain") &&
                ContainsAnyIgnoreCase(crash.RoadwayCondition, "Slick", "Slippery"));
    }

    private bool IsIcySurfaceCrash(CrashRecord crash)
    {
        return ContainsAnyIgnoreCase(crash.RoadwayCondition, "Ice", "Snow", "Sleet", "Frost", "Frozen") ||
               ContainsAnyIgnoreCase(crash.WeatherCondition, "Snow", "Sleet", "Freezing", "Ice") ||
               (ContainsAnyIgnoreCase(crash.WeatherCondition, "Snow", "Sleet") &&
                ContainsAnyIgnoreCase(crash.RoadwayCondition, "Slick", "Slippery"));
    }

    private bool IsFogRelatedCrash(CrashRecord crash)
    {
        return ContainsAnyIgnoreCase(crash.WeatherCondition, "Fog", "Mist", "Haze", "Smoke") ||
               ContainsAnyIgnoreCase(crash.LightCondition, "Fog") ||
               (ContainsAnyIgnoreCase(crash.WeatherCondition, "Fog") &&
                ContainsAnyIgnoreCase(crash.LightCondition, "Dark", "Dusk", "Dawn"));
    }

    private bool IsHydroplaningIncident(CrashRecord crash)
    {
        // Look for contributing factors or crash patterns that suggest hydroplaning
        var hasHydroplaningFactor = crash.ContributingFactors.Any(f =>
            ContainsAnyIgnoreCase(f.Description, "Hydroplan", "Water", "Aquaplan", "Standing Water"));

        var hasHydroplaningPattern = IsWetSurfaceCrash(crash) &&
                                    crash.ContributingFactors.Any(f =>
                                        ContainsAnyIgnoreCase(f.Description, "Speed", "Control", "Skid", "Lost Control"));

        return hasHydroplaningFactor || hasHydroplaningPattern;
    }

    private bool ContainsAnyIgnoreCase(string text, params string[] searchTerms)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        return searchTerms.Any(term =>
            text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    public EnvironmentalRiskSummary GenerateEnvironmentalRiskSummary(List<RiskSegment> segments)
    {
        var summary = new EnvironmentalRiskSummary
        {
            TotalSegments = segments.Count,
            SegmentsWithDrainageIssues = segments.Count(s => s.EnvironmentalFactors.HasDrainageIssues),
            TotalWetSurfaceCrashes = segments.Sum(s => s.EnvironmentalFactors.WetSurfaceCrashes),
            TotalIcySurfaceCrashes = segments.Sum(s => s.EnvironmentalFactors.IcySurfaceCrashes),
            TotalFogRelatedCrashes = segments.Sum(s => s.EnvironmentalFactors.FogRelatedCrashes),
            TotalHydroplaningIncidents = segments.Sum(s => s.EnvironmentalFactors.HydroplaningIncidents),
            HighSlopeSegments = segments.Count(s => s.SlopePercentage > _config.Thresholds.SlopeThreshold)
        };

        summary.DrainageIssuePercentage = summary.TotalSegments > 0
            ? (decimal)summary.SegmentsWithDrainageIssues / summary.TotalSegments * 100
            : 0;

        summary.WeatherSensitiveSegments = segments.Count(s =>
            s.EnvironmentalFactors.WetSurfaceCrashes + s.EnvironmentalFactors.IcySurfaceCrashes > 0);

        _logger.LogInformation("Environmental risk summary: {DrainageCount}/{Total} segments with drainage issues ({DrainagePercent:F1}%), " +
                             "{WeatherCount} weather-sensitive segments, {WetCrashes} wet surface crashes",
            summary.SegmentsWithDrainageIssues, summary.TotalSegments, summary.DrainageIssuePercentage,
            summary.WeatherSensitiveSegments, summary.TotalWetSurfaceCrashes);

        return summary;
    }

    public List<RiskSegment> GetHighEnvironmentalRiskSegments(List<RiskSegment> segments, int topCount = 10)
    {
        return segments
            .Where(s => s.EnvironmentalFactors.HasDrainageIssues ||
                       s.EnvironmentalFactors.WetSurfaceCrashes + s.EnvironmentalFactors.IcySurfaceCrashes > 0)
            .OrderByDescending(s => s.EnvironmentalFactors.WetSurfaceCrashes +
                                   s.EnvironmentalFactors.IcySurfaceCrashes +
                                   s.EnvironmentalFactors.HydroplaningIncidents)
            .ThenByDescending(s => s.SlopePercentage)
            .Take(topCount)
            .ToList();
    }
}

public class EnvironmentalRiskSummary
{
    public int TotalSegments { get; set; }
    public int SegmentsWithDrainageIssues { get; set; }
    public decimal DrainageIssuePercentage { get; set; }
    public int WeatherSensitiveSegments { get; set; }
    public int HighSlopeSegments { get; set; }
    public int TotalWetSurfaceCrashes { get; set; }
    public int TotalIcySurfaceCrashes { get; set; }
    public int TotalFogRelatedCrashes { get; set; }
    public int TotalHydroplaningIncidents { get; set; }
}