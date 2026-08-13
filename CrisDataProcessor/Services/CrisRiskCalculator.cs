using MapSandBox.Models;
using Microsoft.Extensions.Logging;

namespace CrisDataProcessor.Services;

public class CrisRiskCalculator
{
    private readonly ILogger<CrisRiskCalculator> _logger;
    private readonly CrisModelConfiguration _config;

    public CrisRiskCalculator(ILogger<CrisRiskCalculator> logger, CrisModelConfiguration config)
    {
        _logger = logger;
        _config = config;
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

    public DetailedCrisModelScore CalculateDetailedRiskScore(List<CrashRecord> crashes, RiskSegment segment, decimal yearsOfData)
    {
        _logger.LogDebug("Calculating detailed risk score for segment {SegmentId} with {CrashCount} crashes", segment.SegmentId, crashes.Count);

        var score = new DetailedCrisModelScore
        {
            LocationId = segment.SegmentId
        };

        // 1. Crash Frequency (Weight: configured)
        score.CrashFrequencyPerMile = CalculateCrashFrequencyPerMile(crashes, segment.SegmentLength, yearsOfData);
        score.CrashFrequencyScore = Math.Min(score.CrashFrequencyPerMile / 10m, 1.0m); // Normalize to 0-1

        // 2. Severity Index (Weight: configured)
        score.FatalCrashes = crashes.Count(c => c.Severity == KabcoSeverity.K_Fatal);
        score.IncapacitatingInjuryCrashes = crashes.Count(c => c.Severity == KabcoSeverity.A_IncapacitatingInjury);
        score.SeverityIndexScore = CalculateWeightedSeverityIndex(crashes);

        // 3. Traffic Volume (Weight: configured)
        score.TrafficVolumeScore = segment.Aadt.HasValue
            ? Math.Min((decimal)segment.Aadt.Value / 25000m, 1.0m)
            : 0.1m;

        // 4. Elevation/Drainage Risk (Weight: configured)
        score.SlopePercentage = segment.SlopePercentage;
        score.DrainageRiskScore = CalculateDrainageRiskScore(crashes, segment);

        // 5. Environmental Factors (Weight: configured)
        score.WeatherRelatedCrashes = CountWeatherRelatedCrashes(crashes);
        score.CrashBySurfaceCondition = crashes
            .GroupBy(c => c.RoadwayCondition)
            .ToDictionary(g => g.Key, g => g.Count());
        score.CrashByWeatherCondition = crashes
            .GroupBy(c => c.WeatherCondition)
            .ToDictionary(g => g.Key, g => g.Count());
        score.EnvironmentalScore = CalculateEnvironmentalScore(crashes);

        // Calculate composite score using configured weights
        var weights = _config.ModelWeights;
        score.CompositeRiskScore =
            (score.CrashFrequencyScore * weights.CrashFrequency) +
            (score.SeverityIndexScore * weights.SeverityIndex) +
            (score.TrafficVolumeScore * weights.TrafficVolume) +
            (score.DrainageRiskScore * weights.DrainageRisk) +
            (score.EnvironmentalScore * weights.Environmental);

        score.RiskLevel = DetermineRiskLevel(score.CompositeRiskScore);

        _logger.LogDebug("Detailed risk score calculated for {SegmentId}: CF={CF:F3}, SI={SI:F3}, TV={TV:F3}, DR={DR:F3}, ENV={ENV:F3} = {Composite:F3}",
            segment.SegmentId, score.CrashFrequencyScore, score.SeverityIndexScore, score.TrafficVolumeScore,
            score.DrainageRiskScore, score.EnvironmentalScore, score.CompositeRiskScore);

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
        var weights = _config.ModelWeights;
        var composite = (score.CrashFrequencyScore * weights.CrashFrequency) +
                       (score.SeverityIndexScore * weights.SeverityIndex) +
                       (score.TrafficVolumeScore * weights.TrafficVolume) +
                       (score.DrainageRiskScore * weights.DrainageRisk) +
                       (score.EnvironmentalScore * weights.Environmental);

        _logger.LogDebug("Composite score: {Frequency}*{FreqWeight} + {Severity}*{SevWeight} + {Traffic}*{TrafficWeight} + {Drainage}*{DrainageWeight} + {Environmental}*{EnvWeight} = {Composite}",
            score.CrashFrequencyScore, weights.CrashFrequency,
            score.SeverityIndexScore, weights.SeverityIndex,
            score.TrafficVolumeScore, weights.TrafficVolume,
            score.DrainageRiskScore, weights.DrainageRisk,
            score.EnvironmentalScore, weights.Environmental,
            composite);

        return composite;
    }

    private decimal CalculateCrashFrequencyPerMile(List<CrashRecord> crashes, decimal segmentLengthMiles, decimal yearsOfData)
    {
        if (segmentLengthMiles <= 0 || yearsOfData <= 0) return 0;

        return crashes.Count / (segmentLengthMiles * yearsOfData);
    }

    private decimal CalculateWeightedSeverityIndex(List<CrashRecord> crashes)
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
        return totalWeight / crashes.Count;
    }

    private decimal CalculateDrainageRiskScore(List<CrashRecord> crashes, RiskSegment segment)
    {
        var drainageRisk = 0m;

        // Slope risk (weight: 0.6)
        if (segment.SlopePercentage > _config.Thresholds.SlopeThreshold)
            drainageRisk += 0.6m;
        else if (segment.SlopePercentage > _config.Thresholds.SlopeThreshold * 0.6m)
            drainageRisk += 0.3m;

        // Wet surface crashes (weight: 0.4)
        var wetCrashes = crashes.Count(c =>
            c.RoadwayCondition.Contains("Wet", StringComparison.OrdinalIgnoreCase) ||
            c.RoadwayCondition.Contains("Standing Water", StringComparison.OrdinalIgnoreCase) ||
            c.WeatherCondition.Contains("Rain", StringComparison.OrdinalIgnoreCase));
        var wetCrashRatio = crashes.Count > 0 ? (decimal)wetCrashes / crashes.Count : 0;
        drainageRisk += wetCrashRatio * 0.4m;

        return Math.Min(drainageRisk, 1.0m);
    }

    private int CountWeatherRelatedCrashes(List<CrashRecord> crashes)
    {
        return crashes.Count(c =>
            !string.IsNullOrEmpty(c.WeatherCondition) &&
            !c.WeatherCondition.Contains("Clear", StringComparison.OrdinalIgnoreCase) &&
            !c.WeatherCondition.Contains("Cloudy", StringComparison.OrdinalIgnoreCase));
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