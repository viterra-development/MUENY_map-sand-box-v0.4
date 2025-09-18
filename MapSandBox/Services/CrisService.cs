using MapSandBox.Models;
using System.Text.Json;

namespace MapSandBox.Services;

public class CrisService
{
    private readonly CrisConfiguration _configuration;
    private readonly HttpClient _httpClient;

    public CrisService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _configuration = GetDefaultConfiguration();
    }

    public CrisConfiguration GetDefaultConfiguration()
    {
        return new CrisConfiguration
        {
            DataPath = "/cris-data/",
            ModelWeights = new CrisModelWeights(),
            Layers = GetCrisLayers(),
            ParkerCountyBounds = new CrisBounds
            {
                MinLatitude = 32.5m,
                MaxLatitude = 33.0m,
                MinLongitude = -98.0m,
                MaxLongitude = -97.0m
            }
        };
    }

    public List<CrisLayerConfig> GetCrisLayers()
    {
        return new List<CrisLayerConfig>
        {
            new CrisLayerConfig
            {
                Id = "crash-points",
                Type = "ScatterplotLayer",
                DataUrl = "/cris-data/parker-county-crashes-traffic-roads.geojson",
                Visible = true,
                LayerType = CrisLayerType.CrashPoints,
                LayerProperties = new CrashPointsLayerProperties(),
                FilterOptions = new List<string> { "severity", "date_range", "weather_condition" }
            },
            new CrisLayerConfig
            {
                Id = "risk-segments",
                Type = "PathLayer",
                DataUrl = "/cris-data/parker-county-risk-segments-traffic.geojson",
                Visible = false,
                LayerType = CrisLayerType.RiskSegments,
                LayerProperties = new RiskSegmentsLayerProperties(),
                FilterOptions = new List<string> { "risk_level", "crash_count_min", "aadt_min" }
            },
            new CrisLayerConfig
            {
                Id = "risk-heatmap",
                Type = "HeatmapLayer",
                DataUrl = "/cris-data/parker-county-crashes-traffic-roads.geojson",
                Visible = false,
                LayerType = CrisLayerType.RiskHeatmap,
                LayerProperties = new RiskHeatmapLayerProperties(),
                FilterOptions = new List<string> { "severity", "date_range" }
            },
            new CrisLayerConfig
            {
                Id = "intersection-risks",
                Type = "ScatterplotLayer",
                DataUrl = "/cris-data/parker-county-intersection-risks.geojson",
                Visible = false,
                LayerType = CrisLayerType.IntersectionRisks,
                LayerProperties = new IntersectionRisksLayerProperties(),
                FilterOptions = new List<string> { "risk_level", "crash_count_min" }
            }
        };
    }

    public CrisModelScore CalculateRiskScore(List<CrashRecord> crashes)
    {
        if (!crashes.Any())
        {
            return new CrisModelScore
            {
                LocationId = "unknown",
                RiskLevel = RiskLevel.VeryLow
            };
        }

        var weights = _configuration.ModelWeights;
        var score = new CrisModelScore
        {
            LocationId = crashes.First().RoadwayId
        };

        // Simple scoring implementation
        score.CrashFrequencyScore = Math.Min((decimal)crashes.Count / 10m, 1.0m);

        var severityWeights = new Dictionary<KabcoSeverity, decimal>
        {
            { KabcoSeverity.K_Fatal, 1.0m },
            { KabcoSeverity.A_IncapacitatingInjury, 0.8m },
            { KabcoSeverity.B_NonIncapacitatingInjury, 0.6m },
            { KabcoSeverity.C_PossibleInjury, 0.4m },
            { KabcoSeverity.O_NoInjury, 0.1m }
        };

        score.SeverityIndexScore = crashes.Average(c => severityWeights.GetValueOrDefault(c.Severity, 0.05m));
        score.TrafficVolumeScore = crashes.Average(c => Math.Min((decimal)(c.Aadt ?? 0) / 25000m, 1.0m));
        score.DrainageRiskScore = 0.1m;
        score.EnvironmentalScore = 0.1m;

        score.CompositeRiskScore =
            (score.CrashFrequencyScore * weights.CrashFrequency) +
            (score.SeverityIndexScore * weights.SeverityIndex) +
            (score.TrafficVolumeScore * weights.TrafficVolume) +
            (score.DrainageRiskScore * weights.DrainageRisk) +
            (score.EnvironmentalScore * weights.Environmental);

        score.RiskLevel = score.CompositeRiskScore switch
        {
            >= 0.8m => RiskLevel.VeryHigh,
            >= 0.6m => RiskLevel.High,
            >= 0.4m => RiskLevel.Moderate,
            >= 0.2m => RiskLevel.Low,
            _ => RiskLevel.VeryLow
        };

        return score;
    }

    public CrisGeoJsonCollection GetCrashDataByArea(BoundingBox area)
    {
        return new CrisGeoJsonCollection
        {
            Features = new List<CrisGeoJsonFeature>(),
            Metadata = new CrisMetadata
            {
                GeneratedAt = DateTime.UtcNow,
                DataSource = "CRIS",
                StartDate = DateOnly.FromDateTime(DateTime.Now.AddYears(-3)),
                EndDate = DateOnly.FromDateTime(DateTime.Now),
                TotalCrashes = 0,
                TrafficEnabledSegments = 634,
                ModelWeights = _configuration.ModelWeights
            }
        };
    }

    public List<RiskSegment> GetHighRiskSegments(decimal threshold = 0.7m)
    {
        return new List<RiskSegment>();
    }

    public List<LayerInfo> GetCrisLayerInfo()
    {
        return new List<LayerInfo>
        {
            new LayerInfo { Id = "crash-points", Name = "Crash Points", Visible = true },
            new LayerInfo { Id = "risk-segments", Name = "Risk Segments", Visible = false },
            new LayerInfo { Id = "risk-heatmap", Name = "Risk Heatmap", Visible = false },
            new LayerInfo { Id = "intersection-risks", Name = "Intersection Risks", Visible = false }
        };
    }

    public CrashStatistics GetStatistics()
    {
        return new CrashStatistics
        {
            TotalCrashes = 0,
            FatalCrashes = 0,
            InjuryCrashes = 0,
            PropertyDamageOnlyCrashes = 0,
            SeverityBreakdown = new Dictionary<KabcoSeverity, int>(),
            ContributingFactorCounts = new Dictionary<string, int>()
        };
    }

    public async Task<CrisConfiguration> LoadConfigurationAsync()
    {
        try
        {
            var metadataPath = "/cris-data/cris-model-metadata.json";
            var response = await _httpClient.GetAsync(metadataPath);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var metadata = JsonSerializer.Deserialize<CrisMetadata>(json);
                if (metadata?.ModelWeights != null)
                {
                    _configuration.ModelWeights = metadata.ModelWeights;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load CRIS configuration: {ex.Message}");
        }

        return _configuration;
    }

    public async Task<List<RiskSegment>> LoadRiskSegmentsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/cris-data/parker-county-risk-segments-traffic.geojson");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var geoJson = JsonSerializer.Deserialize<CrisGeoJsonCollection>(json);

                return geoJson?.Features?.Select(f => {
                    var segment = new RiskSegment
                    {
                        SegmentId = f.Properties?.GetValueOrDefault("segment_id", "unknown")?.ToString() ?? "unknown",
                        RiskScore = Convert.ToDecimal(f.Properties?.GetValueOrDefault("risk_score", 0) ?? 0),
                        RiskLevel = Enum.TryParse<RiskLevel>(f.Properties?.GetValueOrDefault("risk_level", "Low")?.ToString(), out var level) ? level : RiskLevel.Low,
                        CrashCount = Convert.ToInt32(f.Properties?.GetValueOrDefault("crash_count", 0) ?? 0)
                    };

                    // Extract AADT from enhanced traffic data structure: traffic.aadt
                    var trafficData = f.Properties?.GetValueOrDefault("traffic");
                    if (trafficData is Dictionary<string, object> trafficDict && trafficDict.TryGetValue("aadt", out var aadtValue))
                    {
                        segment.Aadt = Convert.ToInt32(aadtValue ?? 0);
                    }
                    else
                    {
                        // Fallback to old flat structure for backwards compatibility
                        segment.Aadt = Convert.ToInt32(f.Properties?.GetValueOrDefault("aadt", 0) ?? 0);
                    }

                    return segment;
                }).ToList() ?? new List<RiskSegment>();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load risk segments: {ex.Message}");
        }

        return new List<RiskSegment>();
    }

    public async Task<List<CrashRecord>> LoadCrashDataAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/cris-data/parker-county-crashes-traffic-roads.geojson");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var geoJson = JsonSerializer.Deserialize<CrisGeoJsonCollection>(json);

                return geoJson?.Features?.Select(f => new CrashRecord
                {
                    CrashId = f.Properties?.GetValueOrDefault("crash_id", "unknown")?.ToString() ?? "unknown",
                    CrashDateTime = DateTime.TryParse(f.Properties?.GetValueOrDefault("crash_datetime", "")?.ToString(), out var dt) ? dt : DateTime.Now,
                    Severity = Enum.TryParse<KabcoSeverity>(f.Properties?.GetValueOrDefault("severity", "O_NoInjury")?.ToString(), out var severity) ? severity : KabcoSeverity.O_NoInjury,
                    Latitude = Convert.ToDecimal(f.Properties?.GetValueOrDefault("latitude", 0) ?? 0),
                    Longitude = Convert.ToDecimal(f.Properties?.GetValueOrDefault("longitude", 0) ?? 0),
                    TotalPersons = Convert.ToInt32(f.Properties?.GetValueOrDefault("persons_involved", 1) ?? 1),
                    TotalVehicles = Convert.ToInt32(f.Properties?.GetValueOrDefault("vehicles_involved", 1) ?? 1),
                    WeatherCondition = f.Properties?.GetValueOrDefault("weather_condition", "Clear")?.ToString() ?? "Clear",
                    RoadwayCondition = f.Properties?.GetValueOrDefault("surface_condition", "Dry")?.ToString() ?? "Dry"
                }).ToList() ?? new List<CrashRecord>();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load crash data: {ex.Message}");
        }

        return new List<CrashRecord>();
    }
}