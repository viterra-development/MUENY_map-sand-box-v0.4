using MapSandBox.Models;
using Microsoft.Extensions.Logging;

namespace CrisDataProcessor.Services;

public class CrisRiskCalculator
{
    private readonly ILogger<CrisRiskCalculator> _logger;
    private readonly CrisModelWeights _modelWeights;

    public CrisRiskCalculator(ILogger<CrisRiskCalculator> logger, CrisModelWeights? modelWeights = null)
    {
        _logger = logger;
        _modelWeights = modelWeights ?? new CrisModelWeights();
    }

    public CrisModelScore CalculateRiskScore(string locationId, List<CrashRecord> crashes, int? aadt = null, decimal segmentLength = 1.0m)
    {
        _logger.LogDebug("Calculating risk score for location {LocationId} with {CrashCount} crashes", locationId, crashes.Count);

        var score = new CrisModelScore
        {
            LocationId = locationId
        };

        // Calculate crash frequency score (crashes per mile per year)
        score.CrashFrequencyScore = CalculateCrashFrequencyScore(crashes, segmentLength);

        // Calculate severity index score (weighted by KABCO severity)
        score.SeverityIndexScore = CalculateSeverityIndexScore(crashes);

        // Calculate traffic volume score (normalized AADT)
        score.TrafficVolumeScore = CalculateTrafficVolumeScore(aadt);

        // Calculate drainage risk score (elevation-based)
        score.DrainageRiskScore = CalculateDrainageRiskScore(crashes);

        // Calculate environmental score (weather/light conditions)
        score.EnvironmentalScore = CalculateEnvironmentalScore(crashes);

        // Calculate composite risk score
        score.CompositeRiskScore = CalculateCompositeScore(score);

        // Determine risk level
        score.RiskLevel = DetermineRiskLevel(score.CompositeRiskScore);

        _logger.LogDebug("Risk score calculated for {LocationId}: {CompositeScore} ({RiskLevel})",
            locationId, score.CompositeRiskScore, score.RiskLevel);

        return score;
    }

    private decimal CalculateCrashFrequencyScore(List<CrashRecord> crashes, decimal segmentLength)
    {
        if (!crashes.Any()) return 0;

        // Calculate crashes per mile per year
        var years = CalculateTimeSpanYears(crashes);
        var crashesPerMilePerYear = (decimal)crashes.Count / (segmentLength * years);

        // Normalize to 0-1 scale (assuming max reasonable rate is 10 crashes/mile/year)
        var maxRate = 10m;
        var normalizedScore = Math.Min(crashesPerMilePerYear / maxRate, 1.0m);

        _logger.LogDebug("Crash frequency: {CrashCount} crashes over {Years} years on {Length} miles = {Rate} crashes/mile/year (score: {Score})",
            crashes.Count, years, segmentLength, crashesPerMilePerYear, normalizedScore);

        return normalizedScore;
    }

    private decimal CalculateSeverityIndexScore(List<CrashRecord> crashes)
    {
        if (!crashes.Any()) return 0;

        // KABCO severity weights (higher values for more severe crashes)
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

        _logger.LogDebug("Severity index: total weight {TotalWeight} for {CrashCount} crashes = average {Average}",
            totalWeight, crashes.Count, averageWeight);

        return averageWeight;
    }

    private decimal CalculateTrafficVolumeScore(int? aadt)
    {
        if (!aadt.HasValue || aadt <= 0) return 0;

        // Normalize AADT (assuming max reasonable AADT is 50,000)
        var maxAadt = 50000m;
        var normalizedScore = Math.Min((decimal)aadt.Value / maxAadt, 1.0m);

        // Higher traffic volume should correlate with higher risk
        _logger.LogDebug("Traffic volume: AADT {Aadt} = score {Score}", aadt, normalizedScore);

        return normalizedScore;
    }

    private decimal CalculateDrainageRiskScore(List<CrashRecord> crashes)
    {
        if (!crashes.Any()) return 0;

        // Calculate based on elevation variance and weather-related crashes
        var weatherRelatedCrashes = crashes.Count(c =>
            c.WeatherCondition.Contains("rain", StringComparison.OrdinalIgnoreCase) ||
            c.WeatherCondition.Contains("wet", StringComparison.OrdinalIgnoreCase) ||
            c.RoadwayCondition.Contains("wet", StringComparison.OrdinalIgnoreCase));

        var weatherRatio = (decimal)weatherRelatedCrashes / crashes.Count;

        // Consider elevation variance (placeholder - would need actual elevation data)
        var elevationVariance = CalculateElevationVariance(crashes);
        var drainageScore = (weatherRatio * 0.7m) + (elevationVariance * 0.3m);

        _logger.LogDebug("Drainage risk: {WeatherCrashes}/{TotalCrashes} weather-related crashes + elevation variance = score {Score}",
            weatherRelatedCrashes, crashes.Count, drainageScore);

        return Math.Min(drainageScore, 1.0m);
    }

    private decimal CalculateEnvironmentalScore(List<CrashRecord> crashes)
    {
        if (!crashes.Any()) return 0;

        // Calculate based on adverse environmental conditions
        var adverseConditionCrashes = crashes.Count(c =>
            IsAdverseWeatherCondition(c.WeatherCondition) ||
            IsAdverseSurfaceCondition(c.RoadwayCondition));

        var adverseRatio = (decimal)adverseConditionCrashes / crashes.Count;

        _logger.LogDebug("Environmental score: {AdverseCrashes}/{TotalCrashes} adverse condition crashes = score {Score}",
            adverseConditionCrashes, crashes.Count, adverseRatio);

        return adverseRatio;
    }

    private decimal CalculateCompositeScore(CrisModelScore score)
    {
        var composite = (score.CrashFrequencyScore * _modelWeights.CrashFrequency) +
                       (score.SeverityIndexScore * _modelWeights.SeverityIndex) +
                       (score.TrafficVolumeScore * _modelWeights.TrafficVolume) +
                       (score.DrainageRiskScore * _modelWeights.DrainageRisk) +
                       (score.EnvironmentalScore * _modelWeights.Environmental);

        _logger.LogDebug("Composite score: {Frequency}*{FreqWeight} + {Severity}*{SevWeight} + {Traffic}*{TrafficWeight} + {Drainage}*{DrainageWeight} + {Environmental}*{EnvWeight} = {Composite}",
            score.CrashFrequencyScore, _modelWeights.CrashFrequency,
            score.SeverityIndexScore, _modelWeights.SeverityIndex,
            score.TrafficVolumeScore, _modelWeights.TrafficVolume,
            score.DrainageRiskScore, _modelWeights.DrainageRisk,
            score.EnvironmentalScore, _modelWeights.Environmental,
            composite);

        return composite;
    }

    private RiskLevel DetermineRiskLevel(decimal compositeScore)
    {
        return compositeScore switch
        {
            >= 0.8m => RiskLevel.VeryHigh,
            >= 0.6m => RiskLevel.High,
            >= 0.4m => RiskLevel.Moderate,
            >= 0.2m => RiskLevel.Low,
            _ => RiskLevel.VeryLow
        };
    }

    public List<RiskSegment> CalculateSegmentRisks(Dictionary<string, List<CrashRecord>> crashesBySegment, Dictionary<string, int> aadtBySegment)
    {
        _logger.LogInformation("Calculating risk scores for {SegmentCount} road segments", crashesBySegment.Count);

        var riskSegments = new List<RiskSegment>();

        foreach (var (segmentId, crashes) in crashesBySegment)
        {
            var aadt = aadtBySegment.GetValueOrDefault(segmentId);
            var riskScore = CalculateRiskScore(segmentId, crashes, aadt);

            var segment = new RiskSegment
            {
                SegmentId = segmentId,
                RiskScore = riskScore.CompositeRiskScore,
                RiskLevel = riskScore.RiskLevel,
                CrashCount = crashes.Count,
                Aadt = aadt,
                RecentCrashes = crashes.OrderByDescending(c => c.CrashDateTime).Take(5).ToList()
            };

            // Set coordinates from crashes (simplified - would need actual segment geometry)
            if (crashes.Any())
            {
                segment.StartLatitude = crashes.First().Latitude;
                segment.StartLongitude = crashes.First().Longitude;
                segment.EndLatitude = crashes.Last().Latitude;
                segment.EndLongitude = crashes.Last().Longitude;
            }

            riskSegments.Add(segment);
        }

        _logger.LogInformation("Calculated risk scores for {Count} segments. High/Very High risk segments: {HighRiskCount}",
            riskSegments.Count, riskSegments.Count(s => s.RiskLevel is RiskLevel.High or RiskLevel.VeryHigh));

        return riskSegments.OrderByDescending(s => s.RiskScore).ToList();
    }

    private decimal CalculateTimeSpanYears(List<CrashRecord> crashes)
    {
        if (crashes.Count <= 1) return 1.0m;

        var minDate = crashes.Min(c => c.CrashDateTime);
        var maxDate = crashes.Max(c => c.CrashDateTime);
        var timeSpan = maxDate - minDate;

        return Math.Max((decimal)timeSpan.TotalDays / 365.25m, 1.0m);
    }

    private decimal CalculateElevationVariance(List<CrashRecord> crashes)
    {
        var elevations = crashes
            .Where(c => c.LidarElevation.HasValue)
            .Select(c => c.LidarElevation!.Value)
            .ToList();

        if (elevations.Count < 2) return 0;

        var mean = elevations.Average();
        var variance = elevations.Sum(e => (e - mean) * (e - mean)) / elevations.Count;

        // Normalize variance (assuming max reasonable variance is 100 feet²)
        return Math.Min(variance / 100m, 1.0m);
    }

    private bool IsAdverseWeatherCondition(string condition)
    {
        var adverseConditions = new[] { "rain", "snow", "sleet", "fog", "wind", "storm" };
        return adverseConditions.Any(c => condition.Contains(c, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsAdverseLightCondition(string lightCondition)
    {
        var adverseConditions = new[] { "dark", "dusk", "dawn" };
        return adverseConditions.Any(c => lightCondition.Contains(c, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsAdverseSurfaceCondition(string surfaceCondition)
    {
        var adverseConditions = new[] { "wet", "icy", "snowy", "muddy", "loose material" };
        return adverseConditions.Any(c => surfaceCondition.Contains(c, StringComparison.OrdinalIgnoreCase));
    }
}