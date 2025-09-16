using MapSandBox.Models;
using Microsoft.Extensions.Logging;

namespace CrisDataProcessor.Services;

public class CrisGeoJsonGenerator
{
    private readonly ILogger<CrisGeoJsonGenerator> _logger;

    public CrisGeoJsonGenerator(ILogger<CrisGeoJsonGenerator> logger)
    {
        _logger = logger;
    }

    public CrisGeoJsonCollection GenerateCrashPointsGeoJson(List<CrashRecord> crashes)
    {
        _logger.LogInformation("Generating crash points GeoJSON for {Count} crashes", crashes.Count);

        var features = crashes.Select(crash => new CrisGeoJsonFeature
        {
            Type = "Feature",
            Geometry = new CrisGeoJsonGeometry
            {
                Type = "Point",
                Coordinates = new[] { (double)crash.Longitude, (double)crash.Latitude }
            },
            Properties = new Dictionary<string, object>
            {
                ["crash_id"] = crash.CrashId,
                ["crash_date"] = crash.CrashDateTime.ToString("yyyy-MM-dd"),
                ["crash_time"] = crash.CrashDateTime.ToString("HH:mm"),
                ["crash_datetime"] = crash.CrashDateTime.ToString("yyyy-MM-dd HH:mm"),
                ["severity"] = crash.Severity.ToString(),
                ["severity_code"] = GetSeverityCode(crash.Severity),
                ["latitude"] = (double)crash.Latitude,
                ["longitude"] = (double)crash.Longitude,
                ["persons_involved"] = crash.Persons.Count,
                ["vehicles_involved"] = crash.Vehicles.Count,
                ["weather_condition"] = crash.WeatherCondition ?? "",
                ["light_condition"] = "",
                ["surface_condition"] = crash.RoadwayCondition ?? "",
                ["roadway_id"] = crash.RoadwayId,
                ["aadt"] = crash.Aadt,
                ["fatal_count"] = crash.Persons.Count(p => p.InjurySeverity == KabcoSeverity.K_Fatal),
                ["injury_count"] = crash.Persons.Count(p => p.InjurySeverity != KabcoSeverity.K_Fatal && p.InjurySeverity != KabcoSeverity.O_NoInjury),
                ["contributing_factors"] = crash.ContributingFactors.Select(f => f.Description).ToArray()
            }
        }).ToList();

        return new CrisGeoJsonCollection
        {
            Type = "FeatureCollection",
            Features = features,
            Metadata = new CrisMetadata
            {
                GeneratedAt = DateTime.UtcNow,
                DataSource = "CRIS Crash Points",
                TotalCrashes = crashes.Count,
                ModelWeights = new CrisModelWeights()
            }
        };
    }

    public CrisGeoJsonCollection GenerateRiskSegmentsGeoJson(List<RiskSegment> riskSegments)
    {
        _logger.LogInformation("Generating risk segments GeoJSON for {Count} segments", riskSegments.Count);

        var features = riskSegments.Select(segment => new CrisGeoJsonFeature
        {
            Type = "Feature",
            Geometry = new CrisGeoJsonGeometry
            {
                Type = "LineString",
                Coordinates = new[]
                {
                    new[] { (double)segment.StartLongitude, (double)segment.StartLatitude },
                    new[] { (double)segment.EndLongitude, (double)segment.EndLatitude }
                }
            },
            Properties = new Dictionary<string, object>
            {
                ["segment_id"] = segment.SegmentId,
                ["risk_score"] = (double)segment.RiskScore,
                ["risk_level"] = segment.RiskLevel.ToString(),
                ["risk_level_numeric"] = (int)segment.RiskLevel,
                ["crash_count"] = segment.CrashCount,
                ["aadt"] = segment.Aadt,
                ["segment_length"] = (double)segment.SegmentLength,
                ["crashes_per_mile"] = segment.SegmentLength > 0 ? (double)(segment.CrashCount / segment.SegmentLength) : 0,
                ["recent_crashes"] = segment.RecentCrashes.Select(c => new
                {
                    crash_id = c.CrashId,
                    crash_date = c.CrashDateTime.ToString("yyyy-MM-dd"),
                    severity = c.Severity.ToString()
                }).ToArray(),
                ["start_latitude"] = (double)segment.StartLatitude,
                ["start_longitude"] = (double)segment.StartLongitude,
                ["end_latitude"] = (double)segment.EndLatitude,
                ["end_longitude"] = (double)segment.EndLongitude
            }
        }).ToList();

        return new CrisGeoJsonCollection
        {
            Type = "FeatureCollection",
            Features = features,
            Metadata = new CrisMetadata
            {
                GeneratedAt = DateTime.UtcNow,
                DataSource = "CRIS Risk Segments",
                TrafficEnabledSegments = riskSegments.Count,
                ModelWeights = new CrisModelWeights()
            }
        };
    }

    public CrisGeoJsonCollection GenerateIntersectionRisksGeoJson(List<IntersectionRisk> intersectionRisks)
    {
        _logger.LogInformation("Generating intersection risks GeoJSON for {Count} intersections", intersectionRisks.Count);

        var features = intersectionRisks.Select(intersection => new CrisGeoJsonFeature
        {
            Type = "Feature",
            Geometry = new CrisGeoJsonGeometry
            {
                Type = "Point",
                Coordinates = new[] { (double)intersection.Longitude, (double)intersection.Latitude }
            },
            Properties = new Dictionary<string, object>
            {
                ["intersection_id"] = intersection.IntersectionId,
                ["risk_score"] = (double)intersection.RiskScore,
                ["risk_level"] = intersection.RiskLevel.ToString(),
                ["risk_level_numeric"] = (int)intersection.RiskLevel,
                ["crash_count"] = intersection.CrashCount,
                ["latitude"] = (double)intersection.Latitude,
                ["longitude"] = (double)intersection.Longitude,
                ["intersecting_roads"] = intersection.IntersectingRoads.ToArray(),
                ["recent_crashes"] = intersection.RecentCrashes.Select(c => new
                {
                    crash_id = c.CrashId,
                    crash_date = c.CrashDateTime.ToString("yyyy-MM-dd"),
                    severity = c.Severity.ToString(),
                    persons_involved = c.Persons.Count
                }).ToArray(),
                ["fatal_crashes"] = intersection.RecentCrashes.Count(c => c.Severity == KabcoSeverity.K_Fatal),
                ["injury_crashes"] = intersection.RecentCrashes.Count(c => c.Severity != KabcoSeverity.K_Fatal && c.Severity != KabcoSeverity.O_NoInjury),
                ["property_damage_crashes"] = intersection.RecentCrashes.Count(c => c.Severity == KabcoSeverity.O_NoInjury)
            }
        }).ToList();

        return new CrisGeoJsonCollection
        {
            Type = "FeatureCollection",
            Features = features,
            Metadata = new CrisMetadata
            {
                GeneratedAt = DateTime.UtcNow,
                DataSource = "CRIS Intersection Risks",
                ModelWeights = new CrisModelWeights()
            }
        };
    }

    private string GetSeverityCode(KabcoSeverity severity)
    {
        return severity switch
        {
            KabcoSeverity.K_Fatal => "K",
            KabcoSeverity.A_IncapacitatingInjury => "A",
            KabcoSeverity.B_NonIncapacitatingInjury => "B",
            KabcoSeverity.C_PossibleInjury => "C",
            KabcoSeverity.O_NoInjury => "O",
            _ => "U"
        };
    }
}