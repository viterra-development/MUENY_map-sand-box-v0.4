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
                ["light_condition"] = crash.LightCondition,
                ["surface_condition"] = crash.RoadwayCondition ?? "",
                ["roadway_id"] = crash.RoadwayId,
                ["aadt"] = crash.Aadt ?? 0,
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
                // Enhanced: Use actual road geometry if available, fallback to straight line
                Coordinates = segment.RoadGeometry.Any()
                    ? segment.RoadGeometry.ToArray()  // Use actual road geometry
                    : new[]  // Fallback to straight line
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
                ["aadt"] = segment.Aadt ?? 0,
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
                ["end_longitude"] = (double)segment.EndLongitude,
                // Enhanced: Add road geometry properties
                ["road_linear_id"] = segment.RoadLinearId,
                ["road_name"] = segment.RoadName,
                ["road_type"] = segment.RoadType,
                ["geometry_type"] = segment.GeometryType,

                // Environmental factors
                ["environmental_factors"] = new
                {
                    wet_surface_crashes = segment.EnvironmentalFactors.WetSurfaceCrashes,
                    icy_surface_crashes = segment.EnvironmentalFactors.IcySurfaceCrashes,
                    fog_related_crashes = segment.EnvironmentalFactors.FogRelatedCrashes,
                    hydroplaning_incidents = segment.EnvironmentalFactors.HydroplaningIncidents,
                    slope_percentage = (double)segment.EnvironmentalFactors.SlopePercentage,
                    has_drainage_issues = segment.EnvironmentalFactors.HasDrainageIssues
                },

                // Flattened environmental data for easier access
                ["wet_surface_crashes"] = segment.EnvironmentalFactors.WetSurfaceCrashes,
                ["icy_surface_crashes"] = segment.EnvironmentalFactors.IcySurfaceCrashes,
                ["fog_related_crashes"] = segment.EnvironmentalFactors.FogRelatedCrashes,
                ["hydroplaning_incidents"] = segment.EnvironmentalFactors.HydroplaningIncidents,
                ["slope_percentage"] = (double)segment.EnvironmentalFactors.SlopePercentage,
                ["has_drainage_issues"] = segment.EnvironmentalFactors.HasDrainageIssues,
                ["has_environmental_risk"] = segment.HasEnvironmentalRisk
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

    // Deck.gl format generators using strongly typed models
    public CrashPointDeckGl[] GenerateCrashPointsDeckGlData(List<CrashRecord> crashes)
    {
        _logger.LogInformation("Generating crash points deck.gl data for {Count} crashes", crashes.Count);

        var data = crashes.Select(crash => new CrashPointDeckGl
        {
            CrashId = crash.CrashId,
            CrashDate = crash.CrashDateTime.ToString("yyyy-MM-dd"),
            CrashTime = crash.CrashDateTime.ToString("HH:mm"),
            CrashDateTime = crash.CrashDateTime.ToString("yyyy-MM-dd HH:mm"),
            Severity = crash.Severity.ToString(),
            SeverityCode = GetSeverityCode(crash.Severity),
            Latitude = (double)crash.Latitude,
            Longitude = (double)crash.Longitude,
            PersonsInvolved = crash.Persons.Count,
            VehiclesInvolved = crash.Vehicles.Count,
            WeatherCondition = crash.WeatherCondition ?? "",
            LightCondition = crash.LightCondition,
            SurfaceCondition = crash.RoadwayCondition ?? "",
            RoadwayId = crash.RoadwayId,
            Aadt = crash.Aadt,
            FatalCount = crash.Persons.Count(p => p.InjurySeverity == KabcoSeverity.K_Fatal),
            InjuryCount = crash.Persons.Count(p => p.InjurySeverity != KabcoSeverity.K_Fatal && p.InjurySeverity != KabcoSeverity.O_NoInjury),
            ContributingFactors = crash.ContributingFactors.Select(f => f.Description).ToArray(),
            Coordinates = new[] { (double)crash.Longitude, (double)crash.Latitude }
        }).ToArray();

        return data;
    }

    public RiskSegmentDeckGl[] GenerateRiskSegmentsDeckGlData(List<RiskSegment> riskSegments)
    {
        _logger.LogInformation("Generating risk segments deck.gl data for {Count} segments", riskSegments.Count);

        var data = riskSegments.Select(segment => new RiskSegmentDeckGl
        {
            SegmentId = segment.SegmentId,
            RiskScore = (double)segment.RiskScore,
            RiskLevel = segment.RiskLevel.ToString(),
            RiskLevelNumeric = (int)segment.RiskLevel,
            CrashCount = segment.CrashCount,
            Aadt = segment.Aadt,
            SegmentLength = (double)segment.SegmentLength,
            CrashesPerMile = segment.SegmentLength > 0 ? (double)(segment.CrashCount / segment.SegmentLength) : 0,
            RecentCrashes = segment.RecentCrashes.Select(c => new CrashSummaryDeckGl
            {
                CrashId = c.CrashId,
                CrashDate = c.CrashDateTime.ToString("yyyy-MM-dd"),
                Severity = c.Severity.ToString(),
                PersonsInvolved = c.Persons.Count,
                VehiclesInvolved = c.Vehicles.Count
            }).ToArray(),
            StartLatitude = (double)segment.StartLatitude,
            StartLongitude = (double)segment.StartLongitude,
            EndLatitude = (double)segment.EndLatitude,
            EndLongitude = (double)segment.EndLongitude,
            // Enhanced: Use actual road geometry if available, fallback to straight line
            Coordinates = segment.RoadGeometry.Any()
                ? segment.RoadGeometry.ToArray()  // Use actual road geometry
                : new[]  // Fallback to straight line
                {
                    new[] { (double)segment.StartLongitude, (double)segment.StartLatitude },
                    new[] { (double)segment.EndLongitude, (double)segment.EndLatitude }
                },
            // Enhanced: Add road geometry properties
            RoadLinearId = segment.RoadLinearId,
            RoadName = segment.RoadName,
            RoadType = segment.RoadType,
            GeometryType = segment.GeometryType,
            // Fatal and injury crash counts for severity analysis
            FatalCrashes = segment.FatalCrashCount,
            InjuryCrashes = segment.SeriousInjuryCrashCount,
            PropertyDamageCrashes = segment.RecentCrashes.Count(c => c.Severity == KabcoSeverity.O_NoInjury),

            // Environmental factors
            WetSurfaceCrashes = segment.EnvironmentalFactors.WetSurfaceCrashes,
            IcySurfaceCrashes = segment.EnvironmentalFactors.IcySurfaceCrashes,
            FogRelatedCrashes = segment.EnvironmentalFactors.FogRelatedCrashes,
            HydroplaningIncidents = segment.EnvironmentalFactors.HydroplaningIncidents,
            SlopePercentage = (double)segment.EnvironmentalFactors.SlopePercentage,
            HasDrainageIssues = segment.EnvironmentalFactors.HasDrainageIssues,
            HasEnvironmentalRisk = segment.HasEnvironmentalRisk
        }).ToArray();

        return data;
    }

    public IntersectionRiskDeckGl[] GenerateIntersectionRisksDeckGlData(List<IntersectionRisk> intersectionRisks)
    {
        _logger.LogInformation("Generating intersection risks deck.gl data for {Count} intersections", intersectionRisks.Count);

        var data = intersectionRisks.Select(intersection => new IntersectionRiskDeckGl
        {
            IntersectionId = intersection.IntersectionId,
            RiskScore = (double)intersection.RiskScore,
            RiskLevel = intersection.RiskLevel.ToString(),
            RiskLevelNumeric = (int)intersection.RiskLevel,
            CrashCount = intersection.CrashCount,
            Latitude = (double)intersection.Latitude,
            Longitude = (double)intersection.Longitude,
            IntersectingRoads = intersection.IntersectingRoads.ToArray(),
            RecentCrashes = intersection.RecentCrashes.Select(c => new CrashSummaryDeckGl
            {
                CrashId = c.CrashId,
                CrashDate = c.CrashDateTime.ToString("yyyy-MM-dd"),
                Severity = c.Severity.ToString(),
                PersonsInvolved = c.Persons.Count,
                VehiclesInvolved = c.Vehicles.Count
            }).ToArray(),
            FatalCrashes = intersection.RecentCrashes.Count(c => c.Persons.Any(p => p.InjurySeverity == KabcoSeverity.K_Fatal)),
            InjuryCrashes = intersection.RecentCrashes.Count(c => c.Persons.Any(p => p.InjurySeverity != KabcoSeverity.K_Fatal && p.InjurySeverity != KabcoSeverity.O_NoInjury)),
            PropertyDamageCrashes = intersection.RecentCrashes.Count(c => c.Persons.All(p => p.InjurySeverity == KabcoSeverity.O_NoInjury)),
            Coordinates = new[] { (double)intersection.Longitude, (double)intersection.Latitude }
        }).ToArray();

        return data;
    }
}