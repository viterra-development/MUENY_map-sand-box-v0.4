using System.Text.Json.Serialization;

namespace TCDS.Importer.Models;

public enum TrafficLocationType
{
    MainlineInterstate,     // Primary interstate traffic
    MainlineHighway,        // US/State highways
    Ramp,                   // On/off ramps
    LocalRoad,              // City/county roads
    Arterial,               // Major surface streets
    Unknown
}

public enum RoadHierarchy
{
    Interstate = 1,         // I-20, I-35, etc.
    USHighway = 2,          // US 180, US 287
    StateHighway = 3,       // FM roads, State routes
    Arterial = 4,           // Major city streets
    LocalRoad = 5,          // Residential, local access
    Ramp = 6               // Highway ramps
}

public class EnhancedTrafficLocation
{
    public string LocationId { get; set; } = string.Empty;
    public TrafficLocationType LocationType { get; set; }
    public RoadHierarchy TargetRoadHierarchy { get; set; }
    public string? RouteDesignation { get; set; }  // "I-20", "US-180"
    public int? Aadt { get; set; }
    public int? Dhv30 { get; set; }
    public int? AadtYear { get; set; }
    public bool IsMainlineLocation { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? LocatedOn { get; set; }
    public string? Category { get; set; }
    public string? RouteType { get; set; }
    public string? FunctionalClass { get; set; }
    public ValidationResult ValidationResult { get; set; } = new();
}

public class ValidationResult
{
    public List<string> Warnings { get; set; } = new();
    public bool HasWarnings => Warnings.Count > 0;

    public ValidationResult() { }
    public ValidationResult(List<string> warnings)
    {
        Warnings = warnings ?? new List<string>();
    }
}

public class TrafficMatchResult
{
    public bool IsMatch { get; set; }
    public EnhancedTrafficLocation? MatchedLocation { get; set; }
    public double Distance { get; set; }
    public string MatchType { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = new();
    public TrafficLocationType? SourceType { get; set; }

    public static TrafficMatchResult NoMatch() => new() { IsMatch = false };

    public void AddWarning(string warning)
    {
        Warnings.Add(warning);
    }

    public TrafficProperties ToTrafficProperties()
    {
        if (!IsMatch || MatchedLocation == null)
            throw new InvalidOperationException("Cannot convert non-match to TrafficProperties");

        return new TrafficProperties
        {
            Aadt = MatchedLocation.Aadt,
            Dhv30 = MatchedLocation.Dhv30,
            AadtYear = MatchedLocation.AadtYear,
            LocationId = MatchedLocation.LocationId,
            LocatedOn = MatchedLocation.LocatedOn
        };
    }

    public TrafficData ToTrafficData()
    {
        if (!IsMatch || MatchedLocation == null)
            throw new InvalidOperationException("Cannot convert non-match to TrafficData");

        return new TrafficData
        {
            Aadt = MatchedLocation.Aadt,
            Dhv30 = MatchedLocation.Dhv30,
            AadtYear = MatchedLocation.AadtYear,
            LocationId = MatchedLocation.LocationId,
            LocatedOn = MatchedLocation.LocatedOn
        };
    }

    public TrafficMatchMetadata ToMatchMetadata()
    {
        return new TrafficMatchMetadata
        {
            MatchType = MatchType,
            DistanceMeters = Math.Round(Distance * 111000, 1), // Convert degrees to meters
            SourceType = SourceType?.ToString(),
            Warnings = new List<string>(Warnings),
            MatchedAt = DateTime.UtcNow
        };
    }
}

public class QualityReport
{
    public int TotalMatches { get; set; }
    public List<QualityAnomaly> InterstateAnomalies { get; set; } = new();
    public List<string> UnmatchedHighPriorityRoads { get; set; } = new();
    public List<QualityAnomaly> RampToMainlineContamination { get; set; } = new();
    public List<QualityAnomaly> AadtOutliers { get; set; } = new();
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}

public class QualityAnomaly
{
    public string RoadId { get; set; } = string.Empty;
    public string RoadName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty; // "Critical", "High", "Medium", "Low"
    public int? ActualAadt { get; set; }
    public int? ExpectedAadt { get; set; }
    public string? LocationId { get; set; }
}

public class AggregatedTrafficData
{
    public int? Aadt { get; set; }
    public int? Dhv30 { get; set; }
    public int? AadtYear { get; set; }
    public string AggregationMethod { get; set; } = string.Empty;
    public List<string> SourceLocationIds { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}