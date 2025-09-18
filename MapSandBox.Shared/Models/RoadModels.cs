using System.Text.Json.Serialization;

namespace MapSandBox.Shared.Models;

// Enhanced road properties with traffic data
public class EnhancedRoadProperties
{
    [JsonPropertyName("linearId")]
    public string LinearId { get; set; } = string.Empty;

    [JsonPropertyName("fullName")]
    public string FullName { get; set; } = string.Empty;

    [JsonPropertyName("roadType")]
    public string RoadType { get; set; } = string.Empty; // RTTYP

    [JsonPropertyName("mtfcc")]
    public string Mtfcc { get; set; } = string.Empty; // MAF/TIGER Feature Class Code

    [JsonPropertyName("traffic")]
    public TrafficData? Traffic { get; set; }

    [JsonPropertyName("trafficMatch")]
    public TrafficMatchMetadata? TrafficMatch { get; set; }

    [JsonPropertyName("validationWarnings")]
    public List<string>? ValidationWarnings { get; set; }

    [JsonPropertyName("classification")]
    public RoadClassification? Classification { get; set; }
}

public class TrafficData
{
    [JsonPropertyName("aadt")]
    public int? Aadt { get; set; }

    [JsonPropertyName("dhv30")]
    public int? Dhv30 { get; set; }

    [JsonPropertyName("aadtYear")]
    public int? AadtYear { get; set; }

    [JsonPropertyName("locationId")]
    public string? LocationId { get; set; }

    [JsonPropertyName("locatedOn")]
    public string? LocatedOn { get; set; }
}

public class TrafficMatchMetadata
{
    [JsonPropertyName("matchType")]
    public string MatchType { get; set; } = string.Empty;

    [JsonPropertyName("distanceMeters")]
    public double DistanceMeters { get; set; }

    [JsonPropertyName("sourceType")]
    public string? SourceType { get; set; }

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = new();

    [JsonPropertyName("matchedAt")]
    public DateTime MatchedAt { get; set; } = DateTime.UtcNow;
}

public class RoadClassification
{
    [JsonPropertyName("hierarchy")]
    public RoadHierarchy Hierarchy { get; set; }

    [JsonPropertyName("routeDesignation")]
    public string? RouteDesignation { get; set; } // "I-20", "US-180"

    [JsonPropertyName("isMainlineRoad")]
    public bool IsMainlineRoad { get; set; }

    [JsonPropertyName("functionalClass")]
    public string? FunctionalClass { get; set; }
}

public enum RoadHierarchy
{
    Interstate = 1,
    USHighway = 2,
    StateHighway = 3,
    Arterial = 4,
    LocalRoad = 5,
    Ramp = 6
}

// Basic road properties (for backwards compatibility)
public class BasicRoadProperties
{
    [JsonPropertyName("LINEARID")]
    public string LinearId { get; set; } = string.Empty;

    [JsonPropertyName("FULLNAME")]
    public string FullName { get; set; } = string.Empty;

    [JsonPropertyName("RTTYP")]
    public string RoadType { get; set; } = string.Empty;

    [JsonPropertyName("MTFCC")]
    public string Mtfcc { get; set; } = string.Empty;
}

// Specific feature collections with custom metadata
public class EnhancedRoadFeatureCollection : GeoJsonFeatureCollection<EnhancedRoadProperties>
{
    [JsonPropertyName("metadata")]
    public new EnhancedRoadMetadata? Metadata { get; set; }
}

public class BasicRoadFeatureCollection : GeoJsonFeatureCollection<BasicRoadProperties>
{
}

public class EnhancedRoadMetadata
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("totalFeatures")]
    public int TotalFeatures { get; set; }

    [JsonPropertyName("generatedAt")]
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("qualitySummary")]
    public QualitySummary QualitySummary { get; set; } = new();

    [JsonPropertyName("matchingStatistics")]
    public MatchingStatistics MatchingStatistics { get; set; } = new();
}

public class QualitySummary
{
    [JsonPropertyName("interstateAnomalies")]
    public int InterstateAnomalies { get; set; }

    [JsonPropertyName("rampContamination")]
    public int RampContamination { get; set; }

    [JsonPropertyName("aadtOutliers")]
    public int AadtOutliers { get; set; }

    [JsonPropertyName("totalWarnings")]
    public int TotalWarnings { get; set; }

    [JsonPropertyName("dataQualityScore")]
    public double DataQualityScore { get; set; } // 0-100
}

public class MatchingStatistics
{
    [JsonPropertyName("totalRoads")]
    public int TotalRoads { get; set; }

    [JsonPropertyName("matchedRoads")]
    public int MatchedRoads { get; set; }

    [JsonPropertyName("matchingRate")]
    public double MatchingRate => TotalRoads > 0 ? (MatchedRoads / (double)TotalRoads * 100) : 0;

    [JsonPropertyName("matchTypeCount")]
    public Dictionary<string, int> MatchTypeCount { get; set; } = new();

    [JsonPropertyName("aadtByRoadType")]
    public Dictionary<string, AadtStatistics> AadtByRoadType { get; set; } = new();
}

public class AadtStatistics
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("minAadt")]
    public int MinAadt { get; set; }

    [JsonPropertyName("maxAadt")]
    public int MaxAadt { get; set; }

    [JsonPropertyName("medianAadt")]
    public int MedianAadt { get; set; }

    [JsonPropertyName("averageAadt")]
    public double AverageAadt { get; set; }
}