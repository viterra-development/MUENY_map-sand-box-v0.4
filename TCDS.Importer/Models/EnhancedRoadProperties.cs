using System.Text.Json.Serialization;

namespace TCDS.Importer.Models;

// Move common GeoJSON models here to avoid duplication
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(PointGeometry), "Point")]
[JsonDerivedType(typeof(LineStringGeometry), "LineString")]
[JsonDerivedType(typeof(MultiLineStringGeometry), "MultiLineString")]
public abstract class GeoJsonGeometry
{
}

public class PointGeometry : GeoJsonGeometry
{
    [JsonPropertyName("coordinates")]
    public List<double> Coordinates { get; set; } = new(); // [lon, lat]
}

public class LineStringGeometry : GeoJsonGeometry
{
    [JsonPropertyName("coordinates")]
    public List<List<double>> Coordinates { get; set; } = new(); // [[lon, lat], [lon, lat], ...]
}

public class MultiLineStringGeometry : GeoJsonGeometry
{
    [JsonPropertyName("coordinates")]
    public List<List<List<double>>> Coordinates { get; set; } = new(); // [[[lon, lat], ...], [[lon, lat], ...]]
}

public class GeoJsonFeatureCollection
{
    public string Type { get; set; } = "FeatureCollection";
    public Dictionary<string, object>? Metadata { get; set; }
    public List<GeoJsonFeature> Features { get; set; } = new();
}

public class GeoJsonFeature
{
    public string Type { get; set; } = "Feature";
    public GeoJsonGeometry? Geometry { get; set; }
    public Dictionary<string, object?>? Properties { get; set; }
}

public class TrafficProperties
{
    public int? Aadt { get; set; }
    public int? Dhv30 { get; set; }
    public int? AadtYear { get; set; }
    public string? LocationId { get; set; }
    public string? LocatedOn { get; set; }
}

public class EnhancedRoadProperties
{
    // Original road properties
    public string? LinearId { get; set; }
    public string? FullName { get; set; }
    public string? RoadType { get; set; } // RTTYP
    public string? Mtfcc { get; set; }    // MAF/TIGER Feature Class Code

    // Enhanced traffic data
    public TrafficData? Traffic { get; set; }

    // Matching metadata
    public TrafficMatchMetadata? TrafficMatch { get; set; }

    // Validation results
    public List<string>? ValidationWarnings { get; set; }

    // Additional road classification
    public RoadClassification? Classification { get; set; }
}

public class TrafficData
{
    public int? Aadt { get; set; }
    public int? Dhv30 { get; set; }
    public int? AadtYear { get; set; }
    public string? LocationId { get; set; }
    public string? LocatedOn { get; set; }
}

public class TrafficMatchMetadata
{
    public string MatchType { get; set; } = string.Empty;
    public double DistanceMeters { get; set; }
    public string? SourceType { get; set; }
    public List<string> Warnings { get; set; } = new();
    public DateTime MatchedAt { get; set; } = DateTime.UtcNow;
}

public class RoadClassification
{
    public RoadHierarchy Hierarchy { get; set; }
    public string? RouteDesignation { get; set; } // "I-20", "US-180"
    public bool IsMainlineRoad { get; set; }
    public string? FunctionalClass { get; set; }
}

public class EnhancedGeoJsonFeature
{
    public string Type { get; set; } = "Feature";
    public GeoJsonGeometry? Geometry { get; set; }
    public EnhancedRoadProperties Properties { get; set; } = new();
}

public class EnhancedGeoJsonFeatureCollection
{
    public string Type { get; set; } = "FeatureCollection";
    public EnhancedDatasetMetadata? Metadata { get; set; }
    public List<EnhancedGeoJsonFeature> Features { get; set; } = new();
}

public class EnhancedDatasetMetadata
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int TotalFeatures { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public string Source { get; set; } = string.Empty;
    public QualitySummary QualitySummary { get; set; } = new();
    public MatchingStatistics MatchingStatistics { get; set; } = new();
}

public class QualitySummary
{
    public int InterstateAnomalies { get; set; }
    public int RampContamination { get; set; }
    public int AadtOutliers { get; set; }
    public int TotalWarnings { get; set; }
    public double DataQualityScore { get; set; } // 0-100
}

public class MatchingStatistics
{
    public int TotalRoads { get; set; }
    public int MatchedRoads { get; set; }
    public double MatchingRate => TotalRoads > 0 ? (MatchedRoads / (double)TotalRoads * 100) : 0;
    public Dictionary<string, int> MatchTypeCount { get; set; } = new();
    public Dictionary<string, AadtStatistics> AadtByRoadType { get; set; } = new();
}

public class AadtStatistics
{
    public int Count { get; set; }
    public int MinAadt { get; set; }
    public int MaxAadt { get; set; }
    public int MedianAadt { get; set; }
    public double AverageAadt { get; set; }
}