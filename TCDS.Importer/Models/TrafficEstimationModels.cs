using System.Text.Json.Serialization;

namespace TCDS.Importer.Models;

/// <summary>
/// Result of traffic AADT estimation for a road segment
/// </summary>
public class AadtEstimation
{
    /// <summary>
    /// Estimated AADT value (vehicles per day)
    /// </summary>
    public int EstimatedAadt { get; set; }

    /// <summary>
    /// Confidence score (0.0 - 1.0)
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Estimation method used ("SpatialInterpolation_IDW", "RegressionEnhancedInterpolation", etc.)
    /// </summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// Source roads used for estimation
    /// </summary>
    public List<SourceRoadInfo> SourceRoads { get; set; } = new();

    /// <summary>
    /// Warnings or notes about the estimation
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// Year of the estimation
    /// </summary>
    public int EstimationYear { get; set; } = DateTime.Now.Year;

    /// <summary>
    /// Network topology metrics (Phase 1.5)
    /// </summary>
    public TopologyMetrics? Topology { get; set; }
}

/// <summary>
/// Information about a source road used in estimation
/// </summary>
public class SourceRoadInfo
{
    public string LinearId { get; set; } = string.Empty;
    public int Aadt { get; set; }
    public double DistanceMeters { get; set; }
    public double Weight { get; set; }
    public RoadHierarchy Hierarchy { get; set; }
}

/// <summary>
/// Road segment with geometry for spatial analysis
/// </summary>
public class RoadSegment
{
    public string LinearId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public RoadHierarchy Hierarchy { get; set; }
    public GeoJsonGeometry? Geometry { get; set; }

    // Existing traffic data (if available)
    public int? ExistingAadt { get; set; }
    public int? ExistingAadtYear { get; set; }

    // Estimated traffic data (if calculated)
    public AadtEstimation? Estimation { get; set; }

    // Centroid coordinates for distance calculations
    public double CentroidLatitude { get; set; }
    public double CentroidLongitude { get; set; }

    // Original properties for pass-through
    public Dictionary<string, object?>? OriginalProperties { get; set; }
}

/// <summary>
/// Validation metrics for estimation quality
/// </summary>
public class ValidationMetrics
{
    public double R2 { get; set; }
    public double MAE { get; set; }
    public double RMSE { get; set; }
    public double MAPE { get; set; }
    public int SampleSize { get; set; }
    public Dictionary<RoadHierarchy, HierarchyMetrics> ByHierarchy { get; set; } = new();
}

/// <summary>
/// Metrics for a specific road hierarchy
/// </summary>
public class HierarchyMetrics
{
    public int Count { get; set; }
    public double R2 { get; set; }
    public double MAE { get; set; }
    public double AverageError { get; set; }
    public double MedianError { get; set; }
}

/// <summary>
/// Configuration for spatial interpolation
/// </summary>
public class InterpolationConfig
{
    /// <summary>
    /// Maximum number of neighbors to use for interpolation
    /// </summary>
    public int MaxNeighbors { get; set; } = 10;

    /// <summary>
    /// Decay parameter lambda for exponential weighting (meters)
    /// </summary>
    public double DecayLambda { get; set; } = 750.0;

    /// <summary>
    /// Maximum search radius for neighbors (meters)
    /// </summary>
    public double MaxSearchRadius { get; set; } = 5000.0;

    /// <summary>
    /// Whether to require same hierarchy for matching
    /// </summary>
    public bool RequireSameHierarchy { get; set; } = true;

    /// <summary>
    /// Minimum confidence threshold (0.0 - 1.0)
    /// </summary>
    public double MinConfidence { get; set; } = 0.3;
}

/// <summary>
/// Summary statistics for estimation results
/// </summary>
public class EstimationSummary
{
    public int TotalRoads { get; set; }
    public int RoadsWithExistingData { get; set; }
    public int RoadsEstimated { get; set; }
    public int RoadsUnableToEstimate { get; set; }

    public double AverageConfidence { get; set; }
    public Dictionary<RoadHierarchy, int> EstimatedByHierarchy { get; set; } = new();
    public Dictionary<string, int> EstimationMethodCounts { get; set; } = new();

    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Network topology metrics for a road segment (Phase 1.5)
/// </summary>
public class TopologyMetrics
{
    /// <summary>
    /// Whether this road is a dead-end (connectivity degree = 1)
    /// </summary>
    public bool IsDeadEnd { get; set; }

    /// <summary>
    /// Number of roads connected to this segment
    /// </summary>
    public int ConnectivityDegree { get; set; }

    /// <summary>
    /// LinearIds of connected road segments
    /// </summary>
    public List<string> ConnectedRoads { get; set; } = new();

    /// <summary>
    /// Whether this road is isolated from the main network
    /// </summary>
    public bool IsIsolated { get; set; }

    /// <summary>
    /// Network betweenness centrality (0.0 - 1.0) - optional metric
    /// Higher values indicate important through routes
    /// </summary>
    public double? NetworkBetweenness { get; set; }
}

/// <summary>
/// Topology validation report (Phase 1.5)
/// </summary>
public class TopologyValidationReport
{
    public int TotalRoadsAnalyzed { get; set; }
    public int DeadEndRoads { get; set; }
    public int IsolatedRoads { get; set; }
    public int TopologyCorrectionsApplied { get; set; }
    public int DeadEndViolationsFixed { get; set; }
    public int LowConnectivityCorrections { get; set; }

    public double AverageConnectivity { get; set; }
    public double MedianConnectivity { get; set; }

    public List<TopologyViolation> TopViolations { get; set; } = new();
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Details about a topology constraint violation
/// </summary>
public class TopologyViolation
{
    public string LinearId { get; set; } = string.Empty;
    public string RoadName { get; set; } = string.Empty;
    public string ViolationType { get; set; } = string.Empty; // "DeadEnd", "LowConnectivity", etc.
    public int OriginalEstimate { get; set; }
    public int CorrectedEstimate { get; set; }
    public int Difference { get; set; }
    public double PercentChange { get; set; }
    public string Reason { get; set; } = string.Empty;
}
